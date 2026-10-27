#Requires -Version 5.1
<#
.SYNOPSIS
    Downloads a CodeDesigner GitHub release and installs it into %LOCALAPPDATA%\CodeDesigner.

.DESCRIPTION
    Release assets (built by .github/workflows/build.yml on v* tags):
      CodeDesigner.zip      Bridge, emulator, MCP server, database tool  -> <InstallDir>\
      PCSX2.zip             CodeDesigner's PCSX2 fork                    -> <InstallDir>\pcsx2\
      CheatCodeArchive.zip  Cheat database source                        -> <InstallDir>\CheatCodeArchive\
      codedesigner.vsix     VS Code extension                            -> installed into VS Code

    Files are extracted over the existing install, so settings and data in the install
    directory are kept.

.EXAMPLE
    .\Install-Release.ps1                       # latest release, everything
    .\Install-Release.ps1 -Tag v1.5.0 -Components Pcsx2
#>
[CmdletBinding()]
param(
    # Release tag, or 'latest'.
    [string]$Tag = 'latest',
    # Any of CodeDesigner, Pcsx2, CheatArchive, Extension. A single comma-separated string
    # is accepted too, because powershell.exe -File cannot pass arrays.
    [string[]]$Components = @('CodeDesigner', 'Pcsx2', 'CheatArchive', 'Extension')
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CodeDesigner.Setup.psm1') -Force

$known = @('CodeDesigner', 'Pcsx2', 'CheatArchive', 'Extension')
$Components = @($Components | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$unknown = @($Components | Where-Object { $known -notcontains $_ })
if ($unknown) { throw "Unknown component(s): $($unknown -join ', '). Valid: $($known -join ', ')." }

$installDir = Get-InstallDir
$downloadDir = Join-Path $env:TEMP 'CodeDesignerSetup\release'

Write-Step "Reading release '$Tag' from github.com/$(Get-RepoSlug)"
$release = Get-Release -Tag $Tag
Write-Ok "Found $($release.tag_name) ($($release.name))."

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Assert-NotRunningFrom -Dir $installDir

$targets = [ordered]@{
    CodeDesigner = @{ Asset = 'CodeDesigner.zip';     Dest = $installDir }
    Pcsx2        = @{ Asset = 'PCSX2.zip';            Dest = (Join-Path $installDir 'pcsx2') }
    CheatArchive = @{ Asset = 'CheatCodeArchive.zip'; Dest = (Join-Path $installDir 'CheatCodeArchive') }
}

foreach ($component in $targets.Keys) {
    if ($Components -notcontains $component) { continue }
    $target = $targets[$component]
    Write-Step "Installing $component"
    $zip = Save-ReleaseAsset -Release $release -Name $target.Asset -Dir $downloadDir
    Write-Info "Extracting to $($target.Dest)..."
    Expand-Archive -Path $zip -DestinationPath $target.Dest -Force
    Remove-Item $zip -Force
    Write-Ok "$component installed to $($target.Dest)."
}

if ($Components -contains 'Extension') {
    Write-Step 'Installing the VS Code extension'
    $vsix = Save-ReleaseAsset -Release $release -Name 'codedesigner.vsix' -Dir $downloadDir
    Install-Vsix -Path $vsix
}

if ($Components -contains 'CodeDesigner') {
    Set-Content -Path (Join-Path $installDir 'version.txt') -Value $release.tag_name -Encoding ASCII
}

Write-Step "CodeDesigner $($release.tag_name) installed"
