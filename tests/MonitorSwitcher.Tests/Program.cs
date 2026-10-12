using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Loupedeck.MonitorSwitcherPlugin;
using MonitorSwitcher;

internal static class Program
{
    private static void Main(string[] args)
    {
        if (args.Contains("--verify-live-names"))
        {
            MonitorNameTests.VerifyLive();
            return;
        }
        if (args.Contains("--list"))
        {
            foreach (var monitor in MonitorService.GetMonitors())
                Console.WriteLine("{0}: {1} | {2} | {3} x {4} | Primary={5}",
                    monitor.Index, monitor.DeviceName, monitor.MonitorString,
                    monitor.Width, monitor.Height, monitor.IsPrimary);
            return;
        }

        var first = Monitor(1, "A", true);
        var second = Monitor(2, "B", false);
        var monitors = new List<MonitorInfo> { first, second };
        var writes = new List<string>();
        var succeed = true;
        var apply = true;
        var menu = new MonitorMenu(() => monitors, name =>
        {
            writes.Add(name);
            if (succeed && apply)
                foreach (var monitor in monitors)
                    monitor.IsPrimary = monitor.DeviceName == name;
            return succeed;
        });

        Assert(menu.Refresh().Length == 2, "all active displays are listed");
        var key = MonitorMenu.Key(second);
        Assert(!key.Contains('/'), "action keys cannot contain SDK separators");
        Assert(menu.DisplayName(MonitorMenu.Key(first)).Contains("(Main)"), "primary is marked");
        Assert(menu.DisplayName(key).Contains("Monitor 2 - Screen B"), "display has a friendly label");
        Assert(menu.DisplayName(key).Contains("1920 x 1080"), "resolution is included");
        Assert(writes.Count == 0, "enumeration does not switch displays");

        monitors.Reverse();
        menu.Select(key);
        Assert(writes.SequenceEqual(new[] { second.DeviceName }), "selection survives reordering");
        Assert(second.IsPrimary, "selected display becomes primary");
        Assert(MonitorIcons.Render(menu.Find(key).Index, menu.Find(key).IsPrimary).Contains("#ffd15b"),
            "selected monitor receives the main icon after a switch");
        Assert(!MonitorIcons.Render(menu.Find(MonitorMenu.Key(first)).Index,
            menu.Find(MonitorMenu.Key(first)).IsPrimary).Contains("#ffd15b"),
            "previous primary loses its main icon after a switch");
        menu.Select(key);
        Assert(writes.Count == 1, "already-primary selection does not reapply");

        monitors.Remove(second);
        ExpectFailure(() => menu.Select(key), "disconnected");
        monitors.Add(Monitor(2, "Replacement", false));
        ExpectFailure(() => menu.Select(key), "disconnected");
        Assert(writes.Count == 1, "a replacement at the same DISPLAY index is not selected");
        ExpectFailure(() => menu.Select("invalid"), "no longer");

        monitors = new List<MonitorInfo> { first, second };
        first.IsPrimary = true;
        second.IsPrimary = false;
        menu.Refresh();
        succeed = false;
        ExpectFailure(() => menu.Select(key), "could not");
        succeed = true;
        apply = false;
        ExpectFailure(() => menu.Select(key), "did not keep");

        monitors = Enumerable.Range(1, 12).Select(i => Monitor(i, i.ToString(), i == 1)).ToList();
        Assert(menu.Refresh().Length == 12, "display list is not capped at hotkey or ring slot count");
        monitors.Clear();
        Assert(menu.Refresh().Length == 0, "empty topology is supported");
        foreach (var index in new[] { 1, 2, 3, 9, 10, 12, 99 })
        {
            var icon = XDocument.Parse(MonitorIcons.Render(index, false));
            Assert(icon.Root.Name.LocalName == "svg", "monitor icon is valid SVG");
            Assert(!icon.Descendants().Any(n => n.Name.LocalName == "text"), "digits do not depend on installed fonts");
            Assert(MonitorIcons.Render(index, true).Contains("#ffd15b"), "primary has a star as well as a distinct color");
            Assert(!MonitorIcons.Render(index, false).Contains("#ffd15b"), "secondary does not have the primary star");
        }
        XDocument.Parse(MonitorIcons.ParentSvg);
        XDocument.Parse(MonitorIcons.Render(null, false));
        ProfileGeneratorTests.Run();
        MonitorIdentityTests.Run();
        MonitorNameTests.Run();
        Console.WriteLine("All monitor menu tests passed. No Windows display settings were changed.");
    }

    private static MonitorInfo Monitor(int index, string id, bool primary)
    {
        return new MonitorInfo
        {
            Index = index, DeviceName = @"\\.\DISPLAY" + index,
            MonitorDeviceId = id, MonitorString = "Screen " + id,
            Width = 1920, Height = 1080, IsPrimary = primary
        };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception("FAIL: " + message);
    }

    private static void ExpectFailure(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException ex)
        {
            Assert(ex.Message.Contains(message), "expected error: " + message);
            return;
        }
        throw new Exception("FAIL: expected an error containing " + message);
    }
}
