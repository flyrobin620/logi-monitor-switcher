using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MonitorSwitcher;

namespace Loupedeck.MonitorSwitcherPlugin
{
    internal static class ProfileGenerator
    {
        internal const string FolderTitle = "Main Monitor Switch";
        private const string FolderPrefix = "$@Generic___@ProfileAction___";
        internal const string CommandPrefix = SavedMonitorBindings.ActionPrefix;
        private const string PageType = "Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutPage7, LoupedeckService";
        private const string ControlType = "Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutControl7, LoupedeckService";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        internal static string ProfilesRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Logi", "LogiPluginService", "Applications", "Loupedeck72");

        internal static JsonObject Create(JsonObject source, IReadOnlyList<MonitorInfo> monitors,
            int slot, out string folderAction)
        {
            if (monitors.Count == 0 || monitors.Count > 9)
                throw new InvalidOperationException("Select 1 to 9 displays. An Actions Ring folder supports at most nine actions.");
            if (slot < 0 || slot > 7)
                throw new ArgumentOutOfRangeException(nameof(slot));
            if (monitors.Select(MonitorMenu.Key).Distinct(StringComparer.Ordinal).Count() != monitors.Count)
                throw new InvalidOperationException("The display list contains duplicate devices.");

            var profile = source.DeepClone().AsObject();
            if (Text(profile, "deviceType") != "Loupedeck72")
                throw new InvalidDataException("Only Actions Ring (Loupedeck72) profiles are supported.");
            var layout = Object(profile, "layout");
            var folders = Array(layout, "folderPages");
            var actions = Array(profile, "profileActions");
            var existing = actions.OfType<JsonObject>().Where(a =>
                Text(a, "templateActionName") == "$@Generic___@OpenFolder" &&
                Text(a, "displayName") == FolderTitle).ToArray();
            if (existing.Length > 1)
                throw new InvalidDataException("Multiple Main Monitor Switch folders were found. Rename duplicates in Options+ first.");

            JsonObject page;
            if (existing.Length == 1)
            {
                folderAction = Text(existing[0], "name");
                if (!folderAction.StartsWith(FolderPrefix, StringComparison.Ordinal))
                    throw new InvalidDataException("Unrecognized folder action identifier.");
                var folderId = folderAction.Substring(FolderPrefix.Length);
                if (!Guid.TryParseExact(folderId, "N", out _))
                    throw new InvalidDataException("Unrecognized folder page identifier.");
                var pages = folders.OfType<JsonObject>().Where(f => Text(f, "name") == folderId).ToArray();
                if (pages.Length != 1)
                    throw new InvalidDataException("The folder action does not have exactly one matching folder page.");
                page = pages[0];
            }
            else
            {
                var modes = Array(layout, "layoutModes");
                if (modes.Count != 1)
                    throw new InvalidDataException("Creating a folder requires a profile with one layout mode.");
                var workspaces = Array(modes[0].AsObject(), "workspaces");
                if (workspaces.Count != 1)
                    throw new InvalidDataException("Creating a folder requires a profile with one workspace.");
                var pages = Array(workspaces[0].AsObject(), "pressPages");
                if (pages.Count != 1)
                    throw new InvalidDataException("Creating a folder requires a profile with one ring page.");
                var controls = Array(pages[0].AsObject(), "controls");
                var targets = controls.OfType<JsonObject>().Where(c => c["controlId"]?.GetValue<int>() == slot).ToArray();
                if (targets.Length > 1)
                    throw new InvalidDataException("Duplicate ring slot identifiers were found.");

                var id = Guid.NewGuid().ToString("N").ToUpperInvariant();
                folderAction = FolderPrefix + id;
                var control = targets.SingleOrDefault();
                if (control == null)
                {
                    control = new JsonObject { ["$type"] = ControlType, ["controlId"] = slot, ["rotateAction"] = null };
                    controls.Add(control);
                }
                control["pressAction"] = folderAction;
                page = new JsonObject { ["$type"] = PageType, ["name"] = id };
                folders.Add(page);
                actions.Add(new JsonObject
                {
                    ["$type"] = "Loupedeck.Service.ApplicationProfileCommand, LoupedeckService",
                    ["isCommand"] = true, ["name"] = folderAction,
                    ["templateActionName"] = "$@Generic___@OpenFolder",
                    ["actionParameters"] = new JsonObject
                    {
                        ["$type"] = "Loupedeck.ActionEditorActionParameters, PluginApi",
                        ["parameters"] = new JsonObject
                        {
                            ["$type"] = "Loupedeck.StringDictionaryNoCase, PluginApi", ["folderName"] = ""
                        },
                        ["count"] = 1
                    },
                    ["displayName"] = FolderTitle, ["description"] = "Choose the Windows main monitor",
                    ["groupName"] = "Folders", ["superGroupName"] = "@navigation",
                    ["isProfileAction"] = true, ["isMultiState"] = false,
                    ["isResetCommand"] = false, ["adjustmentName"] = null, ["states"] = null
                });
            }

            page["displayName"] = FolderTitle;
            page["description"] = "Choose the Windows main monitor";
            var monitorControls = new JsonArray();
            for (var i = 0; i < monitors.Count; i++)
                monitorControls.Add(new JsonObject
                {
                    ["$type"] = ControlType, ["controlId"] = i,
                    ["pressAction"] = CommandPrefix + MonitorMenu.Key(monitors[i]), ["rotateAction"] = null
                });
            page["controls"] = monitorControls;

            var plugins = Array(profile, "additionalNativePluginNames");
            if (!plugins.Any(p => p?.GetValue<string>() == "MonitorSwitcher"))
                plugins.Add("MonitorSwitcher");
            profile["name"] = Guid.NewGuid().ToString("N").ToUpperInvariant();
            profile["packageName"] = Guid.NewGuid().ToString("N").ToUpperInvariant();
            profile["packageVersion"] = "1.0.0";
            profile["displayName"] = Text(source, "displayName") + " - " + FolderTitle;
            profile["lastModifiedTimeUtc"] = DateTime.UtcNow.ToString("O");
            return profile;
        }

        internal static void Export(string sourceDirectory, string destination,
            IReadOnlyList<MonitorInfo> monitors, int slot)
        {
            sourceDirectory = Path.GetFullPath(sourceDirectory);
            destination = Path.GetFullPath(destination);
            if (!destination.EndsWith(".lp5", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The output file must have the .lp5 extension.");
            if (IsUnder(destination, sourceDirectory) || IsUnder(destination, ProfilesRoot))
                throw new InvalidOperationException("The output must be outside Logi's live profile directories.");
            if (File.Exists(destination))
                throw new IOException("The destination already exists. Choose a new filename.");
            var profilePath = Path.Combine(sourceDirectory, "ProfileInfo.json");
            var original = File.ReadAllBytes(profilePath);
            var profile = Create(JsonNode.Parse(original).AsObject(), monitors, slot, out var folderAction);
            var parent = Directory.GetParent(sourceDirectory);
            if (parent?.Name != "Profiles" || parent.Parent == null)
                throw new InvalidDataException("Choose an installed profile directory underneath an application's Profiles folder.");
            var applicationDirectory = parent.Parent.FullName;
            var application = JsonNode.Parse(File.ReadAllText(Path.Combine(applicationDirectory, "ApplicationInfo.json"))).AsObject();
            if (Text(application, "name") != Text(profile, "applicationName") || Text(application, "deviceType") != "Loupedeck72")
                throw new InvalidDataException("The source application does not match this Actions Ring profile.");
            application["defaultProfileName"] = Text(profile, "name");
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
                    foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", options))
                    {
                        var relative = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
                        if (relative.Equals("ProfileInfo.json", StringComparison.OrdinalIgnoreCase) ||
                            relative.Equals("ApplicationInfo.json", StringComparison.OrdinalIgnoreCase) ||
                            relative.Equals("ApplicationIcon.png", StringComparison.OrdinalIgnoreCase) ||
                            relative.Equals("metadata/LoupedeckPackage.yaml", StringComparison.OrdinalIgnoreCase) ||
                            relative.StartsWith("metadata/ProfilePreview", StringComparison.OrdinalIgnoreCase) ||
                            relative.Equals("ActionIcons/" + folderAction + ".ict", StringComparison.OrdinalIgnoreCase) ||
                            relative.StartsWith("ActionIcons/$MonitorSwitcher___", StringComparison.OrdinalIgnoreCase))
                            continue;
                        zip.CreateEntryFromFile(file, relative);
                    }
                    Write(zip, "ApplicationInfo.json", application.ToJsonString(JsonOptions));
                    var applicationIcon = Path.Combine(applicationDirectory, "ApplicationIcon.png");
                    if (File.Exists(applicationIcon))
                        zip.CreateEntryFromFile(applicationIcon, "ApplicationIcon.png");
                    Write(zip, "ProfileInfo.json", profile.ToJsonString(JsonOptions));
                    Write(zip, "metadata/LoupedeckPackage.yaml", "type: Profile5\nname: " +
                        Text(profile, "packageName") + "\ndisplayName: " +
                        JsonSerializer.Serialize(Text(profile, "displayName")) + "\nversion: 1.0.0\n");
                    Write(zip, "ActionIcons/" + folderAction + ".ict", ParentIconTemplate());
                }
                if (!original.SequenceEqual(File.ReadAllBytes(profilePath)))
                    throw new IOException("The source profile changed while exporting. Retry after finishing your Options+ edits.");
                File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        internal static string ParentIconTemplate()
        {
            return new JsonObject
            {
                ["backgroundColor"] = 4278190080L,
                ["items"] = new JsonArray(new JsonObject
                {
                    ["image"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(MonitorIcons.ParentSvg)),
                    ["imageFileName"] = null, ["imageColor"] = 4294967295L,
                    ["imageRotation"] = "None", ["isVisible"] = true, ["itemType"] = "Image",
                    ["area"] = new JsonObject
                    {
                        ["x"] = 0, ["y"] = 0, ["width"] = 100, ["height"] = 100, ["isFullScreen"] = true
                    }
                })
            }.ToJsonString(JsonOptions);
        }

        private static bool IsUnder(string path, string directory) =>
            path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        private static string Text(JsonObject node, string name) => node[name]?.GetValue<string>() ?? "";
        private static JsonObject Object(JsonObject node, string name) =>
            node[name] as JsonObject ?? throw new InvalidDataException("Missing profile object: " + name);
        private static JsonArray Array(JsonObject node, string name) =>
            node[name] as JsonArray ?? throw new InvalidDataException("Missing profile array: " + name);
        private static void Write(ZipArchive zip, string name, string content)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }
}
