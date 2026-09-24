[CmdletBinding()]
param([string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'stage'
$zip = Join-Path $dist "BunnyGardenSaveEditor-v$Version-windows.zip"
New-Item -ItemType Directory -Force -Path $stage | Out-Null
foreach ($name in 'BunnyGardenSaveEditor.ps1', 'Launch-BunnyGardenSaveEditor.cmd', 'README.md', 'LICENSE') {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $stage $name) -Force
}
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $stage -File | Select-Object -ExpandProperty FullName) -DestinationPath $zip -Force
Remove-Item -LiteralPath $stage -Recurse -Force
Get-FileHash -LiteralPath $zip -Algorithm SHA256
