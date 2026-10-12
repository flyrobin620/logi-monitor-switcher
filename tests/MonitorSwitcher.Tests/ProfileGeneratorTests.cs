using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Loupedeck.MonitorSwitcherPlugin;
using MonitorSwitcher;

internal static class ProfileGeneratorTests
{
    internal static void Run()
    {
        var source = JsonNode.Parse("""
        {
          "name":"ORIGINAL","displayName":"My Ring","deviceType":"Loupedeck72",
          "applicationName":"@_defaultwin","nativePluginName":"DefaultWin",
          "additionalNativePluginNames":["DefaultWin"],
          "layout":{
            "deviceType":"Loupedeck72",
            "folderPages":[],
            "layoutModes":[{
              "workspaces":[{
                "pressPages":[{
                  "controls":[
                    {"controlId":0,"pressAction":"$DefaultWin___MediaPlayPause","rotateAction":null},
                    {"controlId":1,"pressAction":"$DefaultWin___WindowsExplorer","rotateAction":null}
                  ]
                }]
              }]
            }]
          },
          "profileActions":[]
        }
        """).AsObject();
        var before = source.ToJsonString();
        var monitors = Enumerable.Range(1, 9).Select(i => new MonitorInfo
        {
            Index = i, DeviceName = @"\\.\DISPLAY" + i, MonitorDeviceId = "Device" + i,
            MonitorString = "Screen " + i, Width = 1920, Height = 1080, IsPrimary = i == 2
        }).ToArray();

        foreach (var count in new[] { 1, 3, 9 })
        {
            var profile = ProfileGenerator.Create(source, monitors.Take(count).ToArray(), 0, out var folder);
            Assert(source.ToJsonString() == before, "source profile is never mutated");
            Assert(profile["name"].GetValue<string>() != "ORIGINAL", "new identity prevents overwriting the original");
            Assert(profile["layout"]["folderPages"][0]["controls"].AsArray().Count == count, "exactly one child action per display");
            Assert(profile["layout"]["layoutModes"][0]["workspaces"][0]["pressPages"][0]["controls"][1]["pressAction"].GetValue<string>() ==
                "$DefaultWin___WindowsExplorer", "unrelated root actions are preserved");
            Assert(profile["layout"]["folderPages"][0]["controls"][count - 1]["pressAction"].GetValue<string>() ==
                ProfileGenerator.CommandPrefix + MonitorMenu.Key(monitors[count - 1]), "children use real device identifiers");
            var updated = ProfileGenerator.Create(profile, monitors.Take(2).ToArray(), 7, out var sameFolder);
            Assert(sameFolder == folder, "updating an existing folder preserves its binding");
            Assert(updated["layout"]["folderPages"].AsArray().Count == 1, "regeneration does not add duplicate folders");
            Assert(updated["layout"]["folderPages"][0]["controls"].AsArray().Count == 2, "regeneration removes stale children");
            Assert(updated["additionalNativePluginNames"].AsArray().Count(p => p.GetValue<string>() == "MonitorSwitcher") == 1,
                "plugin dependency is not duplicated");
        }
        Fail<InvalidOperationException>(() => ProfileGenerator.Create(source, Array.Empty<MonitorInfo>(), 0, out _));
        Fail<InvalidOperationException>(() => ProfileGenerator.Create(source, monitors.Concat(new[] { monitors[0] }).ToArray(), 0, out _));
        Fail<InvalidOperationException>(() => ProfileGenerator.Create(source, new[] { monitors[0], monitors[0] }, 0, out _));
        Fail<ArgumentOutOfRangeException>(() => ProfileGenerator.Create(source, monitors, 8, out _));
        var wrongDevice = source.DeepClone().AsObject();
        wrongDevice["deviceType"] = "Loupedeck70";
        Fail<InvalidDataException>(() => ProfileGenerator.Create(wrongDevice, monitors, 0, out _));
        var broken = source.DeepClone().AsObject();
        broken["layout"].AsObject().Remove("folderPages");
        Fail<InvalidDataException>(() => ProfileGenerator.Create(broken, monitors, 0, out _));

        var icon = JsonNode.Parse(ProfileGenerator.ParentIconTemplate());
        var encoded = icon["items"][0]["image"].GetValue<string>();
        Assert(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)) == MonitorIcons.ParentSvg, "parent template embeds the switch icon");
        Assert(icon["items"].AsArray().Count == 1, "parent icon is full-size with no tiny text");

        var temporary = Path.Combine(Path.GetTempPath(), "MonitorSwitcherTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var app = Path.Combine(temporary, "App");
            var directory = Path.Combine(app, "Profiles", "Original");
            Directory.CreateDirectory(Path.Combine(directory, "ActionIcons"));
            Directory.CreateDirectory(Path.Combine(directory, "metadata"));
            File.WriteAllText(Path.Combine(app, "ApplicationInfo.json"),
                """{"name":"@_defaultwin","deviceType":"Loupedeck72","defaultProfileName":"ORIGINAL"}""");
            File.WriteAllText(Path.Combine(directory, "ProfileInfo.json"), before);
            File.WriteAllText(Path.Combine(directory, "ActionIcons", "$MonitorSwitcher___old.ict"), "old");
            File.WriteAllText(Path.Combine(directory, "ActionIcons", "Unrelated.ict"), "keep");
            File.WriteAllText(Path.Combine(directory, "metadata", "ProfilePreview.json"), "stale");
            var output = Path.Combine(temporary, "Import.lp5");
            ProfileGenerator.Export(directory, output, monitors.Take(3).ToArray(), 0);
            Assert(File.ReadAllText(Path.Combine(directory, "ProfileInfo.json")) == before, "export leaves disk source unchanged");
            using (var zip = ZipFile.OpenRead(output))
            {
                Assert(zip.GetEntry("ApplicationInfo.json") != null, "archive includes application metadata");
                Assert(zip.GetEntry("ProfileInfo.json") != null, "archive includes profile metadata");
                Assert(zip.GetEntry("metadata/LoupedeckPackage.yaml") != null, "archive has official package manifest");
                Assert(zip.GetEntry("ActionIcons/Unrelated.ict") != null, "unrelated icon overrides survive");
                Assert(zip.GetEntry("ActionIcons/$MonitorSwitcher___old.ict") == null, "stale monitor icon overrides are removed from copy");
                Assert(zip.GetEntry("metadata/ProfilePreview.json") == null, "stale layout preview is not copied");
                using var reader = new StreamReader(zip.GetEntry("ProfileInfo.json").Open());
                File.WriteAllText(Path.Combine(directory, "ProfileInfo.json"), reader.ReadToEnd());
            }
            var originalBindings = File.ReadAllText(Path.Combine(directory, "ProfileInfo.json"));
            var errors = new List<string>();
            var saved = SavedMonitorBindings.Read(temporary, errors.Add);
            Assert(saved.Count == 3 && errors.Count == 0, "saved parameters are discovered without including unrelated actions");
            Assert(saved.Contains(MonitorMenu.Key(monitors[0])), "stored bindings can be registered as compatibility aliases");
            Assert(File.ReadAllText(Path.Combine(directory, "ProfileInfo.json")) == originalBindings, "compatibility scan never edits profiles");
            var corrupt = Path.Combine(temporary, "Corrupt");
            Directory.CreateDirectory(corrupt);
            File.WriteAllText(Path.Combine(corrupt, "ProfileInfo.json"), "{");
            saved = SavedMonitorBindings.Read(temporary, errors.Add);
            Assert(saved.Count == 3 && errors.Count == 1, "invalid profiles are reported without losing valid bindings");
            Fail<IOException>(() => ProfileGenerator.Export(directory, output, monitors, 0));
            Fail<InvalidOperationException>(() => ProfileGenerator.Export(directory, Path.Combine(directory, "DoNotWrite.lp5"), monitors, 0));
            Assert(!Directory.EnumerateFiles(temporary, "*.tmp").Any(), "no incomplete export files remain");
        }
        finally
        {
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, true);
        }
        Console.WriteLine("Profile generation checks passed (1, 3, and 9 monitors, regeneration, archive layout, and source preservation).");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception("FAIL: " + message);
    }

    private static void Fail<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("FAIL: expected " + typeof(T).Name);
    }
}
