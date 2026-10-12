using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MonitorSwitcher;

namespace Loupedeck.MonitorSwitcherPlugin
{
    public sealed class SetMainMonitorCommand : PluginDynamicCommand
    {
        private readonly object _sync = new object();
        private readonly MonitorMenu _menu = new MonitorMenu(MonitorService.GetMonitors, MonitorService.SetPrimary);
        private readonly HashSet<string> _knownParameters = new HashSet<string>(StringComparer.Ordinal);
        private Timer _timer;
        private bool _loaded;
        private string _fingerprint;
        private string _error;

        public SetMainMonitorCommand()
            : base(DeviceType.LoupedeckExtendedFamily)
        {
            foreach (var monitor in _menu.Refresh())
            {
                var key = MonitorMenu.Key(monitor);
                _knownParameters.Add(key);
                AddParameter(key, _menu.DisplayName(key).Replace("\n", " | "), "Main Monitor Switch");
            }
        }

        protected override bool OnLoad()
        {
            lock (_sync)
            {
                _loaded = true;
                Plugin.Log.Info("Starting monitor action refresh.");
                var profiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Logi", "LogiPluginService", "Applications", "Loupedeck72");
                _knownParameters.UnionWith(SavedMonitorBindings.Read(profiles, message =>
                {
                    Plugin.Log.Warning(message);
                    Plugin.OnPluginStatusChanged(PluginStatus.Warning, message);
                }));
                RefreshMonitors();
                _timer = new Timer(OnTimer, null, 3000, 3000);
            }
            return true;
        }

        protected override bool OnUnload()
        {
            lock (_sync)
            {
                _loaded = false;
                _timer?.Dispose();
                _timer = null;
            }
            return true;
        }

        private void OnTimer(object state)
        {
            lock (_sync)
            {
                if (_loaded)
                    RefreshMonitors();
            }
        }

        private void RefreshMonitors()
        {
            try
            {
                var monitors = _menu.Refresh();
                var fingerprint = string.Join("\0", monitors.Select(m =>
                    MonitorMenu.Key(m) + ":" + _menu.DisplayName(MonitorMenu.Key(m))));
                if (_fingerprint != fingerprint)
                {
                    RemoveAllParameters();
                    var currentKeys = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var monitor in monitors)
                    {
                        var key = MonitorMenu.Key(monitor);
                        currentKeys.Add(key);
                        _knownParameters.Add(key);
                        AddParameter(key, _menu.DisplayName(key).Replace("\n", " | "), "Main Monitor Switch");
                    }
                    foreach (var key in _knownParameters.Except(currentKeys))
                        AddParameter(key, _menu.DisplayName(key).Replace("\n", " | "), "Main Monitor Switch").SetHidden();
                    _fingerprint = fingerprint;
                    ParametersChanged();
                    // Saved profiles can still use legacy parameters after a GDI renumber.
                    foreach (var key in _knownParameters.ToArray())
                        ActionImageChanged(key);
                }
                if (monitors.Length == 0)
                    ReportError("No active displays were found.");
                else if (_error == "No active displays were found.")
                    ClearError();
            }
            catch (Exception ex)
            {
                ReportError("Cannot enumerate displays: " + ex.Message, ex);
            }
        }

        protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize)
        {
            lock (_sync)
            {
                RememberParameter(actionParameter);
                return _menu.DisplayName(actionParameter);
            }
        }

        protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
        {
            lock (_sync)
            {
                RememberParameter(actionParameter);
                var monitor = _menu.Find(actionParameter);
                return BitmapImage.FromSvg(MonitorIcons.Render(monitor?.Index, monitor?.IsPrimary == true));
            }
        }

        protected override void RunCommand(string actionParameter)
        {
            lock (_sync)
            {
                RememberParameter(actionParameter);
                try
                {
                    RefreshMonitors();
                    _menu.Select(actionParameter);
                    ClearError();
                    Plugin.Log.Info("Primary display changed: " + _menu.DisplayName(actionParameter));
                    RefreshMonitors();
                }
                catch (Exception ex)
                {
                    ReportError(ex.Message, ex);
                }
            }
        }

        private void RememberParameter(string parameter)
        {
            if (!string.IsNullOrEmpty(parameter) && parameter.Length <= 4096)
                _knownParameters.Add(parameter);
        }

        private void ClearError()
        {
            _error = null;
            Plugin.OnPluginStatusChanged(PluginStatus.Normal, null);
        }

        private void ReportError(string message, Exception exception = null)
        {
            if (_error == message)
                return;
            _error = message;
            if (exception == null)
                Plugin.Log.Error(message);
            else
                Plugin.Log.Error(exception, message);
            Plugin.OnPluginStatusChanged(PluginStatus.Error, message);
        }
    }
}
