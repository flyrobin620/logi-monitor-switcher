# Maintaining and releasing

This guide is for contributors. Normal setup is covered by the [README](../README.md).

## Local build

Use Windows, .NET 10 SDK, and a current local Logi Plugin Service installation.
`global.json` accepts installed .NET 10 feature-band updates. No npm tools or
third-party NuGet packages are required by the projects.

```powershell
.\build.ps1
dotnet run --project .\tests\MonitorSwitcher.Tests\MonitorMenu.Tests.csproj -c Release
```

The build script accepts `-Dotnet` and `-PluginApiDir`. The local `setup.ps1`
reuses the SDK path recorded by the last build, without changing system `PATH`.
That machine-specific file stays under ignored `artifacts` and is excluded
from the wizard distribution archive.

CI covers the SDK-free tests and setup project only. It is not evidence that
the proprietary plugin host loaded the plugin successfully.

## Optional development linking

The official SDK supports a `.link` file in:

```text
%LOCALAPPDATA%\Logi\LogiPluginService\Plugins
```

Its content is the absolute path to this repository's `artifacts\plugin`
directory, including its `metadata` and `bin` subdirectories. Name it
`MonitorSwitcherPlugin.link`.

Use this **instead of**, not alongside, a packaged installation of the same
plugin. Restart the plugin host or use the official reload URI after rebuilding:

```text
loupedeck://plugin/MonitorSwitcher/reload
```

Do not move/delete the build directory while the link is installed. Do not
commit development links or change a user's existing installation as a build
side effect. The `MonitorSwitcher` plugin ID and command class names are
intentionally retained from the original project to preserve saved bindings;
installing both projects' plugin builds simultaneously would create a conflict.

See Logitech's [testing guide](https://logitech.github.io/actions-sdk-docs/csharp/plugin-development/testing-and-debugging-the-plugin/).

## Command-line wizard

```powershell
dotnet .\artifacts\setup\MonitorSwitcher.LogiSetup.dll --generate `
  'C:\path\to\Profiles\PROFILE-ID' 'C:\Exports\MainMonitorSwitch.lp5'

dotnet .\artifacts\setup\MonitorSwitcher.LogiSetup.dll --export-icons `
  'C:\Exports\MonitorSwitcherIcons'
```

`--generate` includes all currently active monitors and rejects counts outside
1–9. It rebuilds an existing `Main Monitor Switch` folder; if none exists, it
replaces root slot 1 **in the generated copy**. Use the GUI to choose a different
slot or subset of displays. Existing destination files are never overwritten.

`--export-icons` exports generic sample icons numbered 1–9 and the parent
icon/template. It does not query or export personal display identities.

Profile storage is currently:

```text
%LOCALAPPDATA%\Logi\LogiPluginService\Applications\Loupedeck72\
  <application>\ApplicationInfo.json
  <application>\Profiles\<profile-id>\ProfileInfo.json
  <application>\Profiles\<profile-id>\ActionIcons\*.ict
```

The system application is `@_defaultwin`. These paths and JSON type names are
internal host implementation details. Do not edit them while Options+ is
running: cached state may overwrite edits, and invalid references can damage
the profile. Test format changes with generated copies and keep the original.

## Validation before distribution

1. Run the regression checks, full solution build, and `build.ps1`.
2. Confirm each ZIP contains only the project's intended binaries, manifest,
   icons, and MIT license. `build.ps1` uses an explicit file allowlist.
3. If the official standalone Plugin Tool is available, run
   `logiplugintool verify .\dist\MonitorSwitcher_1.0.0.lplug4`, substituting the
   current manifest version. The host installation may not include a usable
   standalone pack/verify tool.
4. Load the plugin in a compatible Options+ installation. Check the picker and
   an existing saved menu; include legacy bindings in compatibility checks.
5. Generate/import a fresh `.lp5`, verify the parent icon and exact child count,
   and confirm the original profile is unchanged.
6. With the user's consent, switch between displays and back. Confirm taskbar/
   primary-display state, the star moving to the new main display, and refresh
   after an external Windows Settings change.
7. Test disconnect/reconnect and GDI source renumbering. An absent device must
   not fall through to whichever screen happens to occupy its old number.

Mock tests do not replace physical device checks. Read-only diagnostics never
call `SetDisplayConfig`.

## Publishing a release

- Update `version` in
  `src\MonitorSwitcher.Plugin\package\metadata\LoupedeckPackage.yaml`.
- Build on a trusted Windows machine with the supported host SDK.
- Inspect `dist` and verify the SHA-256 hashes in `SHA256SUMS.txt`.
- Tag the reviewed commit and publish release notes describing compatibility,
  changes, and any migration steps.
- Attach only the generated `.lplug4`, wizard ZIP, and checksum file.
- Never attach personal `.lp5` profiles, logs, `.link` files, debug symbols,
  credentials, or Logitech's proprietary `PluginApi.dll`.

These packages are not code-signed by Logitech and are not a Marketplace
submission. A GitHub source release and Logitech Marketplace approval are
separate processes.

## Contribution expectations

Keep changes focused, retain the MIT notice, and include regression coverage.
Treat device-interface identity as the binding key; human-readable names and
enumeration numbers are display data. Do not replace disconnected identities
with a “best guess.”

For profile-generator changes, preserve unrelated actions, generate fresh
profile/package IDs, reject unsupported input explicitly, and never write to
the source profile. Test both creating and rebuilding the native folder.

Report issues with sanitized reproduction steps and version information.
Profiles can contain personal paths and arbitrary user-defined macro content;
review and redact them before sharing.
