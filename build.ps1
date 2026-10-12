param(
    [string]$Dotnet = 'dotnet',
    [string]$PluginApiDir = "$env:ProgramW6432\Logi\LogiPluginService"
)

$ErrorActionPreference = 'Stop'
$Dotnet = (Get-Command $Dotnet -CommandType Application -ErrorAction Stop).Source
$pluginProject = Join-Path $PSScriptRoot 'src\MonitorSwitcher.Plugin\MonitorSwitcherPlugin.csproj'
$setupProject = Join-Path $PSScriptRoot 'src\MonitorSwitcher.Setup\MonitorSwitcher.LogiSetup.csproj'
$manifest = Join-Path $PSScriptRoot 'src\MonitorSwitcher.Plugin\package\metadata\LoupedeckPackage.yaml'
$versionLine = @(Get-Content -LiteralPath $manifest | Where-Object { $_ -match '^version:\s*(\d+\.\d+\.\d+)\s*$' })
if ($versionLine.Count -ne 1) {
    throw 'The plugin manifest must contain one major.minor.patch version.'
}
$version = ($versionLine[0] -split ':', 2)[1].Trim()
foreach ($project in @($pluginProject, $setupProject)) {
    & $Dotnet build $project -c Release "-p:PluginApiDir=$PluginApiDir" --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for $project (exit code $LASTEXITCODE). .NET SDK 10 and Logi Plugin Service are required."
    }
}

$plugin = Join-Path $PSScriptRoot 'artifacts\plugin'
$setup = Join-Path $PSScriptRoot 'artifacts\setup'
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
Set-Content -LiteralPath (Join-Path $setup 'dotnet-path.txt') -Value $Dotnet -Encoding UTF8
& $Dotnet (Join-Path $setup 'MonitorSwitcher.LogiSetup.dll') --export-icons (Join-Path $plugin 'icons')
if ($LASTEXITCODE -ne 0) {
    throw "Icon export failed (exit code $LASTEXITCODE)."
}

Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 256, 256
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(33, 150, 243))
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 8
try {
    $graphics.Clear([System.Drawing.Color]::FromArgb(22, 28, 38))
    $graphics.FillRectangle($brush, 32, 42, 192, 128)
    $graphics.DrawRectangle($pen, 32, 42, 192, 128)
    $graphics.DrawLine($pen, 128, 174, 128, 208)
    $graphics.DrawLine($pen, 84, 210, 172, 210)
    $bitmap.Save((Join-Path $plugin 'metadata\Icon256x256.png'), [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $pen.Dispose()
    $brush.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
function New-DistributionArchive([string]$Source, [string]$Destination, [string[]]$Files) {
    $stream = [System.IO.File]::Open($Destination, [System.IO.FileMode]::Create)
    $archive = New-Object System.IO.Compression.ZipArchive $stream, ([System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in $Files) {
            $file = Join-Path $Source $relative
            if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
                throw "Missing distribution file: $file"
            }
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file, $relative.Replace('\', '/')) | Out-Null
        }
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $PSScriptRoot 'LICENSE'), 'LICENSE') | Out-Null
    }
    finally {
        $archive.Dispose()
        $stream.Dispose()
    }
}

$package = Join-Path $dist "MonitorSwitcher_$version.lplug4"
$wizard = Join-Path $dist "MonitorSwitcher-Setup_$version.zip"
$pluginFiles = @(
    'bin\MonitorSwitcherPlugin.dll',
    'bin\MonitorSwitcherPlugin.deps.json',
    'metadata\LoupedeckPackage.yaml',
    'metadata\Icon256x256.png',
    'icons\MainMonitorSwitch.svg',
    'icons\MainMonitorSwitch.ict'
)
foreach ($index in 1..9) {
    $pluginFiles += "icons\Monitor$index.svg", "icons\Monitor$index-Main.svg"
}
$setupFiles = @(
    'MonitorSwitcher.LogiSetup.exe',
    'MonitorSwitcher.LogiSetup.dll',
    'MonitorSwitcher.LogiSetup.deps.json',
    'MonitorSwitcher.LogiSetup.runtimeconfig.json'
)
New-DistributionArchive $plugin $package $pluginFiles
New-DistributionArchive $setup $wizard $setupFiles
Get-FileHash -LiteralPath $package, $wizard -Algorithm SHA256 |
    ForEach-Object { '{0}  {1}' -f $_.Hash.ToLowerInvariant(), (Split-Path -Leaf $_.Path) } |
    Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII
Write-Host "Plugin: $package"
Write-Host "Import wizard: $wizard"
Write-Host 'Build does not install anything, modify your Actions Ring, or change display settings.'
