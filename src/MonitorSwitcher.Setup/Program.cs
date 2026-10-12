using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using MonitorSwitcher;

namespace Loupedeck.MonitorSwitcherPlugin
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "--export-icons")
                {
                    Directory.CreateDirectory(args[1]);
                    File.WriteAllText(Path.Combine(args[1], "MainMonitorSwitch.svg"), MonitorIcons.ParentSvg);
                    File.WriteAllText(Path.Combine(args[1], "MainMonitorSwitch.ict"), ProfileGenerator.ParentIconTemplate());
                    for (var index = 1; index <= 9; index++)
                    {
                        File.WriteAllText(Path.Combine(args[1], "Monitor" + index + ".svg"), MonitorIcons.Render(index, false));
                        File.WriteAllText(Path.Combine(args[1], "Monitor" + index + "-Main.svg"), MonitorIcons.Render(index, true));
                    }
                    return 0;
                }
                if (args.Length == 3 && args[0] == "--generate")
                {
                    ProfileGenerator.Export(args[1], args[2], MonitorService.GetMonitors(), 0);
                    return 0;
                }
                if (args.Length != 0)
                    throw new ArgumentException("Usage: --generate <source-profile-directory> <output.lp5> or --export-icons <directory>");

                ApplicationConfiguration.Initialize();
                Application.Run(new SetupForm());
                return 0;
            }
            catch (Exception ex)
            {
                if (args.Length == 0)
                    MessageBox.Show(ex.Message, "Monitor Switcher setup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else
                    Console.Error.WriteLine(ex);
                return 1;
            }
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly ComboBox _profiles = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly CheckedListBox _monitors = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        private readonly NumericUpDown _slot = new NumericUpDown { Minimum = 1, Maximum = 8, Value = 1, Dock = DockStyle.Left };
        private readonly List<string> _paths = new List<string>();
        private List<MonitorInfo> _snapshot;

        internal SetupForm()
        {
            Text = "Main Monitor Switch - Import wizard";
            ClientSize = new Size(740, 490);
            MinimumSize = new Size(680, 460);
            StartPosition = FormStartPosition.CenterScreen;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 8 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { AutoSize = true, Text = "Source Actions Ring profile (the original is never changed):" });
            layout.Controls.Add(_profiles);
            layout.Controls.Add(new Label { AutoSize = true, Text = "Displays to include in the second-level menu (1-9):", Margin = new Padding(0, 16, 0, 6) });
            layout.Controls.Add(_monitors);
            layout.Controls.Add(new Label { AutoSize = true, Text = "If this profile has no Main Monitor Switch folder, replace this root slot in the COPY:" });
            layout.Controls.Add(_slot);
            layout.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(680, 0), Margin = new Padding(0, 14, 0, 10),
                Text = "Generates a separate .lp5 profile with the selected displays and a monitor-switch icon. Import it using Options+ > profile menu > Import profile. Display hot-plug changes require regenerating/importing; main-monitor icons update automatically. The copy includes your existing actions and may contain personal paths: keep it local."
            });
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var export = new Button { Text = "Generate import file...", AutoSize = true };
            var refresh = new Button { Text = "Refresh displays", AutoSize = true };
            export.Click += Export;
            refresh.Click += delegate { RefreshDisplays(); };
            buttons.Controls.Add(export);
            buttons.Controls.Add(refresh);
            layout.Controls.Add(buttons);
            Controls.Add(layout);

            if (!Directory.Exists(ProfileGenerator.ProfilesRoot))
                throw new DirectoryNotFoundException("No Actions Ring profiles were found. Configure Actions Ring in Options+ first.");
            foreach (var path in Directory.EnumerateFiles(ProfileGenerator.ProfilesRoot, "ProfileInfo.json", SearchOption.AllDirectories))
            {
                var profile = JsonNode.Parse(File.ReadAllText(path));
                if (profile?["deviceType"]?.GetValue<string>() != "Loupedeck72")
                    continue;
                _paths.Add(Path.GetDirectoryName(path));
                _profiles.Items.Add(profile["displayName"]?.GetValue<string>() + " (" + profile["applicationName"]?.GetValue<string>() + ")");
            }
            if (_paths.Count == 0)
                throw new InvalidOperationException("No supported Actions Ring profiles were found.");
            _profiles.SelectedIndex = 0;
            RefreshDisplays();
        }

        private void RefreshDisplays()
        {
            _snapshot = MonitorService.GetMonitors();
            _monitors.Items.Clear();
            foreach (var monitor in _snapshot)
                _monitors.Items.Add(monitor.MenuLabel +
                    " - " + monitor.Width + " x " + monitor.Height + (monitor.IsPrimary ? " (Main)" : ""), true);
        }

        private void Export(object sender, EventArgs e)
        {
            try
            {
                var selected = _monitors.CheckedIndices.Cast<int>().Select(i => _snapshot[i]).ToArray();
                var current = MonitorService.GetMonitors().Select(MonitorMenu.Key).ToHashSet();
                if (selected.Any(m => !current.Contains(MonitorMenu.Key(m))))
                    throw new InvalidOperationException("A selected display disconnected. Refresh displays and try again.");
                using var dialog = new SaveFileDialog
                {
                    Filter = "Logi Actions Ring profile (*.lp5)|*.lp5",
                    FileName = "MainMonitorSwitch-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".lp5"
                };
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                ProfileGenerator.Export(_paths[_profiles.SelectedIndex], dialog.FileName, selected, (int)_slot.Value - 1);
                MessageBox.Show(this, "Created:\n" + dialog.FileName +
                    "\n\nImport this file from the profile menu in Options+. Your current profile has not been modified.",
                    "Import file ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Cannot generate profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
