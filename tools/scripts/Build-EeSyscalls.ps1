# Regenerates CodeDesigner.Ps2.LabelManager/Resources/EeSyscalls.json
# from PCSX2's R5900::bios[256] table.
#
# Run from the repo root:
#   pwsh tools/scripts/Build-EeSyscalls.ps1

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$Source   = $null,
    [string]$Target   = $null
)

$ErrorActionPreference = 'Stop'

if (-not $Source) { $Source = Join-Path $RepoRoot 'pcsx2/pcsx2/R5900OpcodeImpl.cpp' }
if (-not $Target) { $Target = Join-Path $RepoRoot 'CodeDesigner.Ps2.Common/Resources/EeSyscalls.json' }

if (-not (Test-Path $Source)) { throw "Source file not found: $Source" }

$content = Get-Content -LiteralPath $Source -Raw

# Capture the table body between `bios[256]=` and the closing brace.
if ($content -notmatch 'bios\[256\]\s*=\s*\{(?<body>[^}]+)\}') {
    throw "Could not locate R5900::bios[256] table in $Source"
}
$body = $Matches['body']

# Strip C++ comments (//... to end of line) — the table uses `//0x##` markers
# between groups which would otherwise read as bogus entries.
$body = [Regex]::Replace($body, '//[^\r\n]*', '')

# Extract every quoted identifier in declaration order.
$names = [Regex]::Matches($body, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }

$syscalls = [ordered]@{}
for ($i = 0; $i -lt $names.Count; $i++) {
    $syscalls[("{0}" -f $i)] = $names[$i]
}

$payload = [ordered]@{
    '_generated_from' = 'pcsx2/pcsx2/R5900OpcodeImpl.cpp (R5900::bios)'
    '_note'           = 'Do not edit by hand; run tools/scripts/Build-EeSyscalls.ps1 to regenerate. Names mirror PCSX2 BIOS HLE; RFU### are reserved/unused slots.'
    'syscalls'        = $syscalls
}

$json = $payload | ConvertTo-Json -Depth 4
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($Target, $json, $utf8NoBom)

Write-Host "Wrote $Target"
Write-Host ("  {0} syscall names extracted" -f $names.Count)
