$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$editor = Join-Path $root 'BunnyGardenSaveEditor.ps1'
$tokens = $errors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile($editor, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw "PowerShell parser found $($errors.Count) error(s)." }

$source = Get-Content -Raw -LiteralPath $editor
foreach ($required in 'DeflateStream', 'BinaryFormatter', 'Copy-Item', 'File]::Replace', 'Read-back verification failed') {
    if ($source -notmatch [regex]::Escape($required)) { throw "Missing required safety or format behavior: $required" }
}
foreach ($forbidden in 'WMAC-Gen2', '0000000000000001', 'gho_', 'api_key', 'password=') {
    if ($source -match [regex]::Escape($forbidden)) { throw "Potential private data found in source: $forbidden" }
}
Write-Output 'PASS: public source parses and contains the expected format and write-safety checks.'
