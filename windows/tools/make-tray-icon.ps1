# Generates windows/DMShot/Resources/TrayIcon.ico from the modern DM Screenshot
# BrandDesign source. Requires Node.js and rsvg-convert (librsvg) on PATH.
# Run: pwsh -File windows/tools/make-tray-icon.ps1

$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'make-tray-icon.mjs'
node $script
