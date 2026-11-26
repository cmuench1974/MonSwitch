<#
.SYNOPSIS
    Erstellt MonSwitch (Release) nach artifacts\MonSwitch.

.PARAMETER Runtime
    Zielplattform: win-x64 (Standard) oder win-arm64.

.PARAMETER Aot
    Hintergrundprozess als Native AOT kompilieren (noch geringerer Speicherbedarf, keine .NET-Runtime nötig).
    Erfordert die Visual Studio Build Tools mit der Workload „Desktopentwicklung mit C++“.

.PARAMETER Zip
    Zusätzlich artifacts\MonSwitch-<Version>-<Runtime>.zip erstellen.

.EXAMPLE
    ./build.ps1 -Zip
#>
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$Aot,
    [switch]$Zip,
    [string]$Output = (Join-Path $PSScriptRoot 'artifacts')
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$version = (Select-Xml -Path (Join-Path $PSScriptRoot 'Directory.Build.props') -XPath '//Version').Node.InnerText
$dest = Join-Path $Output 'MonSwitch'
Write-Host "MonSwitch $version ($Runtime) -> $dest" -ForegroundColor Cyan

# Laufende Instanz aus dem Zielordner beenden, damit die Dateien überschrieben werden können.
$agentExe = Join-Path $dest 'MonSwitch.exe'
if (Test-Path $agentExe) {
    & $agentExe --exit
    Get-Process -Name 'MonSwitch', 'MonSwitch.Settings' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($dest, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { $_ | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue; if (-not $_.HasExited) { $_ | Stop-Process -Force } }
}
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }

$common = @('-c', 'Release', '-r', $Runtime, '-o', $dest, '-p:DebugType=none', '--nologo')

# Hintergrundprozess
if ($Aot) {
    dotnet publish src/MonSwitch/MonSwitch.csproj @common -p:PublishAot=true -p:OptimizationPreference=Size
} else {
    dotnet publish src/MonSwitch/MonSwitch.csproj @common --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true
}
if ($LASTEXITCODE -ne 0) { throw 'Publish des Hintergrundprozesses fehlgeschlagen.' }

# Einstellungen (WinForms)
dotnet publish src/MonSwitch.Settings/MonSwitch.Settings.csproj @common --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true
if ($LASTEXITCODE -ne 0) { throw 'Publish der Einstellungen fehlgeschlagen.' }

Get-ChildItem $dest | Format-Table Name, @{ n = 'Größe (KB)'; e = { [math]::Round($_.Length / 1KB) } } -AutoSize

if ($Zip) {
    $zipPath = Join-Path $Output "MonSwitch-$version-$Runtime.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $dest '*') -DestinationPath $zipPath
    Write-Host "ZIP: $zipPath" -ForegroundColor Green
}
