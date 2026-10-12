using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MonitorSwitcher;

namespace Loupedeck.MonitorSwitcherPlugin
{
    internal sealed class MonitorMenu
    {
        private readonly Func<List<MonitorInfo>> _getMonitors;
        private readonly Func<string, bool> _setPrimary;
        private MonitorInfo[] _monitors = Array.Empty<MonitorInfo>();

        internal MonitorMenu(Func<List<MonitorInfo>> getMonitors, Func<string, bool> setPrimary)
        {
            _getMonitors = getMonitors;
            _setPrimary = setPrimary;
        }

        internal MonitorInfo[] Refresh()
        {
            var monitors = _getMonitors().ToArray();
            _monitors = monitors;
            return monitors;
        }

        internal static string Key(MonitorInfo monitor)
        {
            // GDI source names are reassigned when displays are disabled or reconnected.
            if (!string.IsNullOrEmpty(monitor.MonitorDevicePath))
                return "path-" + Encode(monitor.MonitorDevicePath);
            if (!string.IsNullOrEmpty(monitor.MonitorDeviceId))
                return "id-" + Encode(monitor.MonitorDeviceId);
            return Encode(monitor.DeviceName + "\0");
        }

        internal string DisplayName(string key)
        {
            var monitor = Resolve(key, _monitors, out var ambiguous);
            if (monitor == null)
                return ambiguous ? "Display identity ambiguous" : "Display disconnected";

            return string.Format("{0}\n{1} x {2}{3}",
                monitor.MenuLabel, monitor.Width, monitor.Height,
                monitor.IsPrimary ? "\n(Main)" : "");
        }

        internal MonitorInfo Find(string key)
        {
            return Resolve(key, _monitors, out _);
        }

        internal void Select(string key)
        {
            var selected = Resolve(key, _monitors, out var ambiguous);
            if (ambiguous)
                throw new InvalidOperationException("The saved display identity matches multiple monitors. Reassign this action from the current display list.");
            if (selected == null)
                throw new InvalidOperationException("This display is no longer in the menu. Reopen Main Monitor Switch.");

            // Revalidate the device, not its list index, after a hot-plug or reorder.
            var current = Resolve(key, _getMonitors(), out ambiguous);
            if (ambiguous)
                throw new InvalidOperationException("The saved display identity matches multiple monitors. Reassign this action from the current display list.");
            if (current == null)
                throw new InvalidOperationException("The selected display was disconnected. Reopen Main Monitor Switch.");
            if (!current.IsPrimary && !_setPrimary(current.DeviceName))
                throw new InvalidOperationException("Windows could not change the primary display. Reopen the menu and try again.");

            var updated = Resolve(key, Refresh(), out ambiguous);
            if (updated == null || ambiguous || !updated.IsPrimary)
                throw new InvalidOperationException("Windows did not keep the selected display as primary.");
        }

        private static string Encode(string value) =>
            Convert.ToHexString(Encoding.UTF8.GetBytes(value.ToUpperInvariant()));

        private static MonitorInfo Resolve(string key, IEnumerable<MonitorInfo> monitors, out bool ambiguous)
        {
            ambiguous = false;
            if (string.IsNullOrEmpty(key) || key.Length > 4096)
                return null;

            var pathKey = key.StartsWith("path-", StringComparison.Ordinal);
            var idKey = key.StartsWith("id-", StringComparison.Ordinal);
            string identity;
            try
            {
                identity = new UTF8Encoding(false, true).GetString(Convert.FromHexString(
                    pathKey ? key.Substring(5) : idKey ? key.Substring(3) : key));
            }
            catch (FormatException)
            {
                return null;
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
            if (string.IsNullOrEmpty(identity))
                return null;

            Func<MonitorInfo, bool> matches;
            if (pathKey)
                matches = m => string.Equals(m.MonitorDevicePath, identity, StringComparison.OrdinalIgnoreCase);
            else if (idKey)
                matches = m => string.Equals(m.MonitorDeviceId, identity, StringComparison.OrdinalIgnoreCase);
            else
            {
                // Keep the original hex("DISPLAY<n>\0device-id") bindings working.
                var separator = identity.IndexOf('\0');
                if (separator < 0)
                    return null;
                var sourceName = identity.Substring(0, separator);
                var deviceId = identity.Substring(separator + 1);
                matches = string.IsNullOrEmpty(deviceId)
                    ? m => string.IsNullOrEmpty(m.MonitorDeviceId) &&
                           string.Equals(m.DeviceName, sourceName, StringComparison.OrdinalIgnoreCase)
                    : m => string.Equals(m.MonitorDeviceId, deviceId, StringComparison.OrdinalIgnoreCase);
            }

            MonitorInfo result = null;
            foreach (var monitor in monitors.Where(matches))
            {
                if (result != null)
                {
                    ambiguous = true;
                    return null;
                }
                result = monitor;
            }
            return result;
        }
    }
}
