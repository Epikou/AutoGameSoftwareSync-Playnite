# To be run by the user, with Playnite CLOSED. Copies the build output into Playnite's Extensions folder.
param([string]$Configuration = "Debug")
$ErrorActionPreference = "Stop"
$id  = "AutoGameSoftwareSync_Playnite_Plugin"
$src = Join-Path $PSScriptRoot "..\src\AutoGameSoftwareSync.PlayniteExtension\bin\$Configuration\net462"
$dst = Join-Path $env:APPDATA "Playnite\Extensions\$id"

if (Get-Process -Name "Playnite.DesktopApp","Playnite.FullscreenApp" -ErrorAction SilentlyContinue) {
    Write-Host "Playnite is running. Close it completely, then run this script again." -ForegroundColor Red; exit 1
}
if (-not (Test-Path (Join-Path $src "AutoGameSoftwareSync.PlayniteExtension.dll"))) {
    Write-Host "Build output not found: run 'dotnet build' first ($src)." -ForegroundColor Red; exit 1
}
if (Test-Path $dst) {
    $bak = "$dst.bak_" + (Get-Date -Format "yyyyMMdd_HHmmss")
    Copy-Item $dst $bak -Recurse
    Write-Host "Existing folder backed up to $bak"
}
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item (Join-Path $src "AutoGameSoftwareSync.PlayniteExtension.dll") $dst -Force
Copy-Item (Join-Path $src "extension.yaml") $dst -Force
Write-Host "Installed to $dst. Start Playnite."
