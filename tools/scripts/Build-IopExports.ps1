# Regenerates CodeDesigner.Ps2.LabelManager/Resources/IopModuleExports.json
# from PCSX2's machine-generated pcsx2/pcsx2/IopModuleNames.cpp.
#
# Run from the repo root:
#   pwsh tools/scripts/Build-IopExports.ps1

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$Source   = $null,
    [string]$Target   = $null
)

$ErrorActionPreference = 'Stop'

if (-not $Source) { $Source = Join-Path $RepoRoot 'pcsx2/pcsx2/IopModuleNames.cpp' }
if (-not $Target) { $Target = Join-Path $RepoRoot 'CodeDesigner.Ps2.LabelManager/Resources/IopModuleExports.json' }
$OverlayPath = Join-Path $RepoRoot 'tools/scripts/IopModuleExports.overlay.json'

if (-not (Test-Path $Source)) {
    throw "Source file not found: $Source"
}

$modules = [ordered]@{}
$current = $null

foreach ($line in Get-Content -LiteralPath $Source) {
    # MODULE(name) — same name may appear multiple times in the source;
    # merge into an existing module rather than overwriting.
    if ($line -match '^\s*MODULE\(\s*([A-Za-z0-9_]+)\s*\)\s*$') {
        $current = $matches[1]
        if (-not $modules.Contains($current)) {
            $modules[$current] = [ordered]@{}
        }
        continue
    }
    # END_MODULE
    if ($line -match '^\s*END_MODULE\s*$') {
        $current = $null
        continue
    }
    # EXPORT(index, name)
    if ($current -and ($line -match '^\s*EXPORT\(\s*([0-9]+)\s*,\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)\s*$')) {
        $index = [int]$matches[1]
        $name  = $matches[2]
        $modules[$current]["$index"] = $name
    }
}

# Merge in optional overlay (modules not covered by PCSX2, e.g. ps2sdk-only libs).
if (Test-Path $OverlayPath) {
    $overlay = Get-Content -LiteralPath $OverlayPath -Raw | ConvertFrom-Json
    foreach ($prop in $overlay.modules.PSObject.Properties) {
        $modName = $prop.Name
        if (-not $modules.Contains($modName)) { $modules[$modName] = [ordered]@{} }
        foreach ($entryProp in $prop.Value.PSObject.Properties) {
            $modules[$modName][$entryProp.Name] = [string]$entryProp.Value
        }
    }
    Write-Host "Merged overlay from $OverlayPath"
}

$payload = [ordered]@{
    '_generated_from' = 'pcsx2/pcsx2/IopModuleNames.cpp (+ overlay if present)'
    '_note'           = 'Do not edit by hand; run tools/scripts/Build-IopExports.ps1 to regenerate.'
    'modules'         = $modules
}

$json = $payload | ConvertTo-Json -Depth 8

# ConvertTo-Json emits UTF-16 by default through Out-File on PS5.1;
# write as UTF-8 (no BOM) so EmbeddedResource roundtrips cleanly.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($Target, $json, $utf8NoBom)

$moduleCount = $modules.Count
$exportCount = ($modules.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
Write-Host "Wrote $Target"
Write-Host ("  {0} modules, {1} exports" -f $moduleCount, $exportCount)
