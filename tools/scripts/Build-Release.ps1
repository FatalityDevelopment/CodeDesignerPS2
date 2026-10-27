# Builds the release assets that CodeDesignerInstaller.exe (Install-Release.ps1) downloads:
#
#   <OutputDir>\CodeDesigner.zip          Bridge, emulator, MCP server, database tool, AI, Multiplayer
#                                         (the %LOCALAPPDATA%\CodeDesigner layout, made by the
#                                         projects' own deploy targets pointed at a staging dir)
#   <OutputDir>\CheatCodeArchive.zip      CheatCodeArchive\Database\ from the submodule
#   <OutputDir>\codedesigner.vsix         VS Code extension
#   <OutputDir>\CodeDesignerInstaller.exe Self-contained installer, pinned to -Version
#   <OutputDir>\PCSX2.zip                 Only with -Pcsx2BinDir (CI zips the PCSX2 job's output itself)
#
# CI runs this on every push (.github/workflows/build.yml) and publishes the assets on v* tags.
# Run from the repo root:
#   pwsh tools/scripts/Build-Release.ps1 -Version v1.5.0

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$OutputDir,
    # Release tag the installer pins to. Empty = the installer downloads the latest release.
    [string]$Version = '',
    [string]$Pcsx2BinDir,
    [switch]$SkipExtension,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

if (-not $OutputDir) { $OutputDir = Join-Path $RepoRoot 'dist' }
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
$stage = Join-Path $OutputDir 'stage'

function Invoke-Checked {
    param([string]$Exe, [string[]]$Arguments)
    Write-Host "> $Exe $($Arguments -join ' ')"
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "'$Exe' exited with code $LASTEXITCODE." }
}

# Writes entries with '/' separators: ZipFile.CreateFromDirectory on Windows PowerShell 5.1
# (.NET Framework) stores backslashes, which other unzip tools treat as part of the file name.
function New-Zip {
    param([string]$Source, [string]$Zip, [bool]$IncludeBaseDirectory = $false)
    if (Test-Path $Zip) { Remove-Item $Zip -Force }
    $Source = [IO.Path]::GetFullPath($Source).TrimEnd('\')
    $root = if ($IncludeBaseDirectory) { Split-Path $Source -Parent } else { $Source }
    $archive = [IO.Compression.ZipFile]::Open($Zip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -Path $Source -File -Recurse -Force) {
            $entry = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally {
        $archive.Dispose()
    }
    Write-Host ("  {0} ({1:N1} MB)" -f (Split-Path $Zip -Leaf), ((Get-Item $Zip).Length / 1MB))
}

if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

# ---- CodeDesigner.zip --------------------------------------------------------------

Write-Host '== Building CodeDesigner.sln'
$installStage = Join-Path $stage 'CodeDesigner'
Invoke-Checked dotnet @(
    'build', (Join-Path $RepoRoot 'CodeDesigner.sln'), '-c', 'Release', '-nologo',
    '-p:SkipDatabaseInit=true', "-p:CodeDesignerInstallDir=$installStage"
)
if (-not (Test-Path (Join-Path $installStage 'CodeDesigner.Ps2.Bridge.exe'))) {
    throw "The build did not deploy CodeDesigner.Ps2.Bridge.exe to $installStage."
}
New-Zip -Source $installStage -Zip (Join-Path $OutputDir 'CodeDesigner.zip')

# ---- CheatCodeArchive.zip ----------------------------------------------------------

Write-Host '== Packaging the cheat archive'
$archiveDb = Join-Path $RepoRoot 'CheatCodeArchive\Database'
if (-not (Test-Path $archiveDb)) { throw "$archiveDb not found. Run: git submodule update --init CheatCodeArchive" }
# Keep the Database\ folder in the zip: Initialize-Database.ps1 passes its parent as --archive-root.
New-Zip -Source $archiveDb -Zip (Join-Path $OutputDir 'CheatCodeArchive.zip') -IncludeBaseDirectory $true

# ---- codedesigner.vsix -------------------------------------------------------------

if (-not $SkipExtension) {
    Write-Host '== Packaging the VS Code extension'
    Push-Location (Join-Path $RepoRoot 'vscode\codedesigner')
    try {
        Invoke-Checked npm @('ci', '--no-audit', '--no-fund')
        Invoke-Checked npx @('--yes', '@vscode/vsce', 'package', '--skip-license', '--out', (Join-Path $OutputDir 'codedesigner.vsix'))
    } finally {
        Pop-Location
    }
}

# ---- CodeDesignerInstaller.exe -----------------------------------------------------

if (-not $SkipInstaller) {
    Write-Host '== Publishing the installer'
    $installerOut = Join-Path $stage 'installer'
    Invoke-Checked dotnet @(
        'publish', (Join-Path $RepoRoot 'CodeDesigner.Ps2.Installer\CodeDesigner.Ps2.Installer.csproj'),
        '-c', 'Release', '-r', 'win-x64', '-nologo', "-p:ReleaseTag=$Version", '-o', $installerOut
    )
    Copy-Item (Join-Path $installerOut 'CodeDesignerInstaller.exe') $OutputDir
}

# ---- PCSX2.zip ---------------------------------------------------------------------

if ($Pcsx2BinDir) {
    Write-Host '== Packaging PCSX2'
    New-Zip -Source $Pcsx2BinDir -Zip (Join-Path $OutputDir 'PCSX2.zip')
}

Remove-Item $stage -Recurse -Force
Write-Host "== Release assets in $OutputDir"
Get-ChildItem $OutputDir | ForEach-Object { Write-Host ("  {0,-28} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)) }
