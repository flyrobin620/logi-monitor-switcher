param([string]$Dotnet)

$ErrorActionPreference = 'Stop'
$wizard = Join-Path $PSScriptRoot 'artifacts\setup\MonitorSwitcher.LogiSetup.dll'
if (-not (Test-Path -LiteralPath $wizard)) {
    throw 'Run .\build.ps1 first, or build src\MonitorSwitcher.Setup\MonitorSwitcher.LogiSetup.csproj.'
}
if (-not $Dotnet) {
    $savedRuntime = Join-Path $PSScriptRoot 'artifacts\setup\dotnet-path.txt'
    $Dotnet = if (Test-Path -LiteralPath $savedRuntime) {
        (Get-Content -LiteralPath $savedRuntime -Raw).Trim()
    } else {
        'dotnet'
    }
}
& $Dotnet $wizard
if ($LASTEXITCODE -ne 0) {
    throw "Import wizard failed (exit code $LASTEXITCODE). The .NET 10 Desktop Runtime is required."
}
