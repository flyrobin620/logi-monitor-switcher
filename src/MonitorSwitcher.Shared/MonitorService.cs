using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MonitorSwitcher
{
    // A lightweight description of one attached display.
    internal class MonitorInfo
    {
        public int Index;            // 1-based label for the currently enumerated displays
        public string DeviceName;    // \\.\DISPLAY1
        public string AdapterString; // e.g. "NVIDIA GeForce RTX ..."
        public string MonitorString; // hardware model, falling back to the driver description
        public string MonitorDeviceId; // distinguishes devices after a reconnect
        public string MonitorDevicePath; // independent of the GDI DISPLAY<n> source name
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public bool IsPrimary;

        public string DisplayName
        {
            get { return string.IsNullOrWhiteSpace(MonitorString) ? DeviceName : MonitorString; }
        }

        public string MenuLabel
        {
            get { return string.Format("Monitor {0} - {1}", Index, DisplayName); }
        }

        internal void ApplyFriendlyName(IDictionary<string, string> names)
        {
            string name;
            if (!string.IsNullOrEmpty(MonitorDevicePath) &&
                names.TryGetValue(MonitorDevicePath, out name) && !string.IsNullOrWhiteSpace(name))
                MonitorString = name.Trim();
        }
    }

    // Enumerates displays and changes which one is the Windows primary monitor,
    // using only the documented Win32 display APIs.
    internal static class MonitorService
    {
        public static List<MonitorInfo> GetMonitors()
        {
            var list = new List<MonitorInfo>();
            var monitorNames = CcdNative.GetMonitorNames();
            uint i = 0;
            int index = 0;

            while (true)
            {
                var dd = new NativeMethods.DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf(typeof(NativeMethods.DISPLAY_DEVICE));
                if (!NativeMethods.EnumDisplayDevices(null, i, ref dd, 0))
                    break;
                i++;

                bool attached = (dd.StateFlags & NativeMethods.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0;
                bool mirroring = (dd.StateFlags & NativeMethods.DISPLAY_DEVICE_MIRRORING_DRIVER) != 0;
                if (!attached || mirroring)
                    continue;

                var dm = new NativeMethods.DEVMODE();
                dm.dmSize = (ushort)Marshal.SizeOf(typeof(NativeMethods.DEVMODE));
                if (!NativeMethods.EnumDisplaySettings(dd.DeviceName,
                        NativeMethods.ENUM_CURRENT_SETTINGS, ref dm))
                    continue;

                index++;
                var info = new MonitorInfo();
                info.Index = index;
                info.DeviceName = dd.DeviceName;
                info.AdapterString = dd.DeviceString;
                info.MonitorString = GetMonitorName(dd.DeviceName, out info.MonitorDeviceId, out info.MonitorDevicePath);
                info.ApplyFriendlyName(monitorNames);
                info.X = dm.dmPositionX;
                info.Y = dm.dmPositionY;
                info.Width = (int)dm.dmPelsWidth;
                info.Height = (int)dm.dmPelsHeight;
                info.IsPrimary = (dm.dmPositionX == 0 && dm.dmPositionY == 0);
                list.Add(info);
            }
            return list;
        }

        // Friendly monitor name (the child device of an adapter), if available.
        private static string GetMonitorName(string adapterDeviceName, out string deviceId, out string devicePath)
        {
            deviceId = "";
            devicePath = "";
            for (uint index = 0; ; index++)
            {
                var dd = new NativeMethods.DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf(typeof(NativeMethods.DISPLAY_DEVICE));
                if (!NativeMethods.EnumDisplayDevices(adapterDeviceName, index, ref dd, 0))
                    break;
                // Inactive children can remain at index zero after a topology change.
                if ((dd.StateFlags & NativeMethods.DISPLAY_DEVICE_ACTIVE) == 0)
                    continue;

                deviceId = dd.DeviceID;
                var displayInterface = new NativeMethods.DISPLAY_DEVICE();
                displayInterface.cb = Marshal.SizeOf(typeof(NativeMethods.DISPLAY_DEVICE));
                if (NativeMethods.EnumDisplayDevices(adapterDeviceName, index, ref displayInterface,
                        NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME))
                    devicePath = displayInterface.DeviceID;
                return dd.DeviceString;
            }
            return "";
        }

        // Makes the monitor with the given device name the primary display.
        // Uses the modern CCD API (SetDisplayConfig); the legacy
        // ChangeDisplaySettingsEx path validates the mode but refuses to commit a
        // primary/topology change on Windows 10/11 (returns DISP_CHANGE_FAILED).
        public static bool SetPrimary(string targetDeviceName)
        {
            var monitors = GetMonitors();

            MonitorInfo target = null;
            foreach (var m in monitors)
            {
                if (m.DeviceName == targetDeviceName)
                {
                    target = m;
                    break;
                }
            }
            if (target == null)
                return false;
            if (target.IsPrimary)
                return true;

            // The primary is the source at (0,0); CCD shifts every source so the
            // target (currently at target.X/target.Y) moves there.
            return CcdNative.SetPrimaryByPosition(target.X, target.Y);
        }

        public static MonitorInfo FindByIndex(int index)
        {
            foreach (var m in GetMonitors())
            {
                if (m.Index == index)
                    return m;
            }
            return null;
        }
    }
}
