using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MonitorSwitcher
{
    // Modern "Connecting and Configuring Displays" (CCD) API. On Windows 10/11
    // the legacy ChangeDisplaySettingsEx path validates a mode but refuses to
    // commit a primary/topology change (returns DISP_CHANGE_FAILED). QueryDisplayConfig
    // + SetDisplayConfig is the reliable way to move the primary monitor.
    //
    // The primary display is, by definition, the source positioned at (0,0). To
    // make a monitor primary we shift every source so the target lands at (0,0).
    internal static class CcdNative
    {
        private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        private const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
        private const uint SDC_APPLY = 0x00000080;
        private const uint SDC_SAVE_TO_DATABASE = 0x00000200;
        private const uint SDC_ALLOW_CHANGES = 0x00000400;
        private const uint DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE = 1;
        private const int ERROR_SUCCESS = 0;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;
        private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

        public static Dictionary<string, string> GetMonitorNames()
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                uint numPaths, numModes;
                int rc = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out numPaths, out numModes);
                if (rc != ERROR_SUCCESS)
                {
                    Trace.TraceWarning("Cannot query monitor names: GetDisplayConfigBufferSizes returned {0}.", rc);
                    return names;
                }

                var paths = new DISPLAYCONFIG_PATH_INFO[numPaths];
                var modes = new DISPLAYCONFIG_MODE_INFO[numModes];
                rc = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPaths, paths, ref numModes, modes, IntPtr.Zero);
                // Hot-plug can change the required buffer size between these two calls.
                if (rc == ERROR_INSUFFICIENT_BUFFER)
                    continue;
                if (rc != ERROR_SUCCESS)
                {
                    Trace.TraceWarning("Cannot query monitor names: QueryDisplayConfig returned {0}.", rc);
                    return names;
                }

                for (int i = 0; i < numPaths; i++)
                {
                    var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    target.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                    target.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME));
                    target.header.adapterId = paths[i].targetInfo.adapterId;
                    target.header.id = paths[i].targetInfo.id;
                    rc = DisplayConfigGetDeviceInfo(ref target);
                    if (rc != ERROR_SUCCESS)
                    {
                        Trace.TraceWarning("Cannot query monitor name for target {0}: DisplayConfigGetDeviceInfo returned {1}.",
                            target.header.id, rc);
                        continue;
                    }

                    // Virtual/forced outputs need not expose a hardware name.
                    if (!string.IsNullOrWhiteSpace(target.monitorDevicePath) &&
                        !string.IsNullOrWhiteSpace(target.monitorFriendlyDeviceName))
                        names[target.monitorDevicePath] = target.monitorFriendlyDeviceName.Trim();
                }
                return names;
            }
            Trace.TraceWarning("Cannot query monitor names: display topology kept changing during enumeration.");
            return names;
        }

        // Makes the source currently located at (targetX, targetY) the primary
        // display. The coordinates come from the GDI enumeration and match the
        // CCD source positions for a DPI-aware process.
        public static bool SetPrimaryByPosition(int targetX, int targetY)
        {
            uint numPaths, numModes;
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out numPaths, out numModes) != ERROR_SUCCESS)
                return false;

            var paths = new DISPLAYCONFIG_PATH_INFO[numPaths];
            var modes = new DISPLAYCONFIG_MODE_INFO[numModes];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPaths, paths,
                    ref numModes, modes, IntPtr.Zero) != ERROR_SUCCESS)
                return false;

            bool found = false;
            for (int i = 0; i < numModes; i++)
            {
                if (modes[i].infoType != DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
                    continue;
                if (modes[i].sourceMode.positionX == targetX && modes[i].sourceMode.positionY == targetY)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
                return false;

            // Already primary? Nothing to do.
            if (targetX == 0 && targetY == 0)
                return true;

            for (int i = 0; i < numModes; i++)
            {
                if (modes[i].infoType != DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
                    continue;
                modes[i].sourceMode.positionX -= targetX;
                modes[i].sourceMode.positionY -= targetY;
            }

            int rc = SetDisplayConfig(numPaths, paths, numModes, modes,
                SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_SAVE_TO_DATABASE | SDC_ALLOW_CHANGES);
            return rc == ERROR_SUCCESS;
        }

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME request);

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint numPaths,
            [In, Out] DISPLAYCONFIG_PATH_INFO[] paths, ref uint numModes,
            [In, Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(uint numPaths,
            [In] DISPLAYCONFIG_PATH_INFO[] paths, uint numModes,
            [In] DISPLAYCONFIG_MODE_INFO[] modes, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public uint type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint flags;
            public uint outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string monitorDevicePath;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_SOURCE_INFO
        { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId; public uint id; public uint modeInfoIdx;
            public uint outputTechnology; public uint rotation; public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate; public uint scanLineOrdering;
            public int targetAvailable; public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_SOURCE_MODE
        {
            public uint width; public uint height; public uint pixelFormat;
            public int positionX; public int positionY;
        }

        // 64-byte record: 16-byte header + 48-byte union (only sourceMode used here).
        [StructLayout(LayoutKind.Explicit, Size = 64)]
        private struct DISPLAYCONFIG_MODE_INFO
        {
            [FieldOffset(0)] public uint infoType;
            [FieldOffset(4)] public uint id;
            [FieldOffset(8)] public LUID adapterId;
            [FieldOffset(16)] public DISPLAYCONFIG_SOURCE_MODE sourceMode;
        }
    }
}
