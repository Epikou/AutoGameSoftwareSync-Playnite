# To be run by the user, with Playnite CLOSED. Removes the extension folder (a backup copy is kept).
$ErrorActionPreference = "Stop"
$id  = "AutoGameSoftwareSync_Playnite_Plugin"
$dst = Join-Path $env:APPDATA "Playnite\Extensions\$id"
if (Get-Process -Name "Playnite.DesktopApp","Playnite.FullscreenApp" -ErrorAction SilentlyContinue) {
    Write-Host "Playnite is running. Close it completely, then run this script again." -ForegroundColor Red; exit 1
}
if (-not (Test-Path $dst)) { Write-Host "Nothing to uninstall."; exit 0 }
$bak = "$dst.removed_" + (Get-Date -Format "yyyyMMdd_HHmmss")
Move-Item $dst $bak
Write-Host "Extension moved to $bak (delete it manually if you want). Tip: before uninstalling, untick 'Write the installed games list' in the extension settings (removes playnite-library.json) and use the menu 'AGSS : remove all added items' in Playnite."
