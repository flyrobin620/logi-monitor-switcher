using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Loupedeck.MonitorSwitcherPlugin
{
    internal static class SavedMonitorBindings
    {
        internal const string ActionPrefix = "$MonitorSwitcher___Loupedeck.MonitorSwitcherPlugin.SetMainMonitorCommand___";

        internal static HashSet<string> Read(string profilesRoot, Action<string> reportError)
        {
            var parameters = new HashSet<string>(StringComparer.Ordinal);
            if (!Directory.Exists(profilesRoot))
                return parameters;

            try
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    IgnoreInaccessible = false
                };
                foreach (var file in Directory.EnumerateFiles(profilesRoot, "ProfileInfo.json", options))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(File.ReadAllText(file));
                        Collect(document.RootElement, parameters);
                    }
                    catch (JsonException ex)
                    {
                        reportError("Cannot read saved monitor bindings in " + file + ": " + ex.Message);
                    }
                    catch (IOException ex)
                    {
                        reportError("Cannot read saved monitor bindings in " + file + ": " + ex.Message);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        reportError("Cannot read saved monitor bindings in " + file + ": " + ex.Message);
                    }
                }
            }
            catch (IOException ex)
            {
                reportError("Cannot enumerate Actions Ring profiles: " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                reportError("Cannot enumerate Actions Ring profiles: " + ex.Message);
            }
            return parameters;
        }

        private static void Collect(JsonElement node, HashSet<string> parameters)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in node.EnumerateArray())
                    Collect(item, parameters);
            }
            else if (node.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in node.EnumerateObject())
                {
                    if (property.Name == "pressAction" && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var action = property.Value.GetString();
                        if (action.StartsWith(ActionPrefix, StringComparison.Ordinal))
                        {
                            var parameter = action.Substring(ActionPrefix.Length);
                            if (parameter.Length > 0 && parameter.Length <= 4096)
                                parameters.Add(parameter);
                        }
                    }
                    else
                        Collect(property.Value, parameters);
                }
            }
        }
    }
}
