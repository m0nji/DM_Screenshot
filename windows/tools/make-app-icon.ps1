# Generates windows/DMShot/Resources/AppIcon.ico from the shared DM BrandDesign
# SVG source used by macOS. Requires Node.js and rsvg-convert (librsvg) on PATH.
# Run: pwsh -File windows/tools/make-app-icon.ps1

$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'make-app-icon.mjs'
node $script
