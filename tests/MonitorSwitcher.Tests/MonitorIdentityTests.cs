using System;
using System.Collections.Generic;
using System.Text;
using Loupedeck.MonitorSwitcherPlugin;
using MonitorSwitcher;

internal static class MonitorIdentityTests
{
    internal static void Run()
    {
        var screen = Monitor("TV", 2, false);
        var monitors = new List<MonitorInfo> { Monitor("Desk", 1, true), screen };
        var writes = new List<string>();
        var menu = new MonitorMenu(() => monitors, deviceName =>
        {
            writes.Add(deviceName);
            foreach (var monitor in monitors)
                monitor.IsPrimary = monitor.DeviceName == deviceName;
            // Windows can reassign the GDI source again while applying the change.
            screen.DeviceName = @"\\.\DISPLAY4";
            return true;
        });
        menu.Refresh();
        var legacy = Convert.ToHexString(Encoding.UTF8.GetBytes(screen.DeviceName + "\0" + screen.MonitorDeviceId));
        var stable = MonitorMenu.Key(screen);

        screen = Monitor("TV", 1, false);
        monitors = new List<MonitorInfo> { screen };
        menu.Refresh();
        Assert(MonitorMenu.Key(screen) == stable, "stable binding does not include the GDI source");
        Assert(menu.Find(legacy) == screen, "existing profile resolves the same TV after DISPLAY2 becomes DISPLAY1");
        menu.Select(legacy);
        Assert(writes.Count == 1 && writes[0] == @"\\.\DISPLAY1", "legacy action switches the current source, not the stored number");
        Assert(menu.Find(legacy).IsPrimary, "legacy action remains resolvable after switching renumbers it again");
        Assert(MonitorIcons.Render(menu.Find(legacy).Index, menu.Find(legacy).IsPrimary).Contains("#ffd15b"),
            "legacy action receives the current primary icon");

        screen.MonitorDevicePath = screen.MonitorDevicePath.ToLowerInvariant();
        screen.MonitorDeviceId = "New driver key";
        menu.Refresh();
        Assert(MonitorMenu.Key(screen) == stable, "interface keys are case-insensitive and independent of the driver key");
        Assert(menu.Find(stable) == screen, "interface binding survives driver-key changes");

        monitors = new List<MonitorInfo> { Monitor("Replacement", 4, true) };
        menu.Refresh();
        Assert(menu.Find(stable) == null, "a different device on the same source is not substituted");
        Assert(menu.Find(legacy) == null, "legacy bindings also reject a different physical device");
        ExpectFailure(() => menu.Select(stable), "no longer");
        Assert(writes.Count == 1, "missing-device selection does not call Windows");

        var one = Monitor("TV", 1, false);
        var two = Monitor("TV", 3, false);
        two.MonitorDevicePath += "-second-instance";
        monitors = new List<MonitorInfo> { one, two };
        menu.Refresh();
        Assert(menu.Find(legacy) == null, "duplicate legacy identities never silently choose a display");
        Assert(menu.DisplayName(legacy) == "Display identity ambiguous", "ambiguous and disconnected identities are distinguished");
        ExpectFailure(() => menu.Select(legacy), "multiple monitors");
        Assert(menu.Find(MonitorMenu.Key(two)) == two, "interface identifiers distinguish duplicate driver identities");
        Assert(writes.Count == 1, "ambiguous selection never calls Windows");

        foreach (var invalid in new[] { "", "id-", "path-", "XYZ", "00FF", "id-FF", "123", new string('A', 4097) })
            Assert(menu.Find(invalid) == null, "invalid action parameter is rejected");
        Console.WriteLine("Monitor identity checks passed (GDI renumbering, existing profiles, reconnects, and ambiguous devices).");
    }

    private static MonitorInfo Monitor(string identity, int source, bool primary) => new MonitorInfo
    {
        Index = source, DeviceName = @"\\.\DISPLAY" + source,
        MonitorDeviceId = "MONITOR\\" + identity + "\\driver-key",
        MonitorDevicePath = @"\\?\DISPLAY#" + identity + "#instance",
        MonitorString = identity, Width = 3840, Height = 2160, IsPrimary = primary
    };

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
            Assert(ex.Message.Contains(message), "expected error containing " + message);
            return;
        }
        throw new Exception("FAIL: expected " + message);
    }
}
