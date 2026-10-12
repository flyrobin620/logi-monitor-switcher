using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Loupedeck.MonitorSwitcherPlugin;
using MonitorSwitcher;

internal static class MonitorNameTests
{
    internal static void Run()
    {
        var monitor = new MonitorInfo
        {
            Index = 2, DeviceName = @"\\.\DISPLAY2",
            MonitorDeviceId = "MONITOR\\TV", MonitorDevicePath = @"\\?\DISPLAY#TV#instance",
            MonitorString = "Generic PnP Monitor", Width = 3840, Height = 2160, IsPrimary = true
        };
        var key = MonitorMenu.Key(monitor);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { @"\\?\DISPLAY#DESK#instance", "32M2V" },
            { @"\\?\display#tv#instance", " SONY TV  *30 " }
        };
        monitor.ApplyFriendlyName(names);
        Assert(monitor.MonitorString == "SONY TV  *30", "hardware name is matched by case-insensitive interface identity");
        Assert(MonitorMenu.Key(monitor) == key, "changing the label does not change saved action identity");
        Assert(monitor.MenuLabel == "Monitor 2 - SONY TV  *30", "tray menu label includes the real hardware name");

        var menu = new MonitorMenu(() => new List<MonitorInfo> { monitor }, _ => throw new Exception("No switch expected"));
        menu.Refresh();
        Assert(menu.DisplayName(key) == "Monitor 2 - SONY TV  *30\n3840 x 2160\n(Main)",
            "Actions Ring uses the same name and preserves resolution and primary status");
        monitor.DeviceName = @"\\.\DISPLAY9";
        monitor.ApplyFriendlyName(names);
        Assert(monitor.DisplayName == "SONY TV  *30", "source renumbering does not misassign a name");

        monitor.MonitorString = "Driver description";
        names[monitor.MonitorDevicePath] = "   ";
        monitor.ApplyFriendlyName(names);
        Assert(monitor.DisplayName == "Driver description", "blank hardware name retains driver fallback");
        monitor.MonitorDevicePath = @"\\?\DISPLAY#UNKNOWN#instance";
        monitor.ApplyFriendlyName(names);
        Assert(monitor.DisplayName == "Driver description", "missing device never takes a different monitor's name");
        monitor.MonitorDevicePath = null;
        monitor.MonitorString = "";
        monitor.ApplyFriendlyName(names);
        Assert(monitor.DisplayName == monitor.DeviceName, "unnamed output falls back to device name");

        var targetType = typeof(CcdNative).GetNestedType("DISPLAYCONFIG_TARGET_DEVICE_NAME", BindingFlags.NonPublic);
        Assert(Marshal.SizeOf(targetType) == 420, "native target name structure has the Windows ABI size");
        Assert(Marshal.OffsetOf(targetType, "monitorFriendlyDeviceName").ToInt32() == 36, "friendly name offset is correct");
        Assert(Marshal.OffsetOf(targetType, "monitorDevicePath").ToInt32() == 164, "device path offset is correct");
        Console.WriteLine("Monitor name checks passed (hardware identity mapping, both menu labels, and fallback behavior).");
    }

    internal static void VerifyLive()
    {
        var names = CcdNative.GetMonitorNames();
        var monitors = MonitorService.GetMonitors();
        var menu = new MonitorMenu(() => monitors, _ => throw new Exception("Live name checks must not change display settings."));
        menu.Refresh();
        Assert(monitors.Count > 0, "live monitors are enumerated");
        var hardwareNames = 0;
        foreach (var monitor in monitors)
        {
            if (!string.IsNullOrEmpty(monitor.MonitorDevicePath) &&
                names.TryGetValue(monitor.MonitorDevicePath, out var expected))
            {
                Assert(monitor.DisplayName == expected, "live hardware name reaches shared enumeration");
                hardwareNames++;
            }
            Assert(menu.DisplayName(MonitorMenu.Key(monitor)).StartsWith(monitor.MenuLabel + "\n"),
                "live Actions Ring and tray labels agree");
            Console.WriteLine("{0} | {1} x {2} | Main={3}", monitor.MenuLabel, monitor.Width, monitor.Height, monitor.IsPrimary);
        }
        Assert(hardwareNames > 0, "the native name query returns real hardware names");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception("FAIL: " + message);
    }
}
