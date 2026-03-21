#Requires -Version 5.1
<#
.SYNOPSIS
    Build-Artifacts — compiles CodeDesigner and produces a distributable package.

.DESCRIPTION
    Developer-facing script.  Run this once from your development machine; the
    output is a single zip file (and an unzipped folder) you can send to anyone.

    What this script does
    ─────────────────────
    1. Builds the CodeDesigner .NET solution (dotnet build -c Release).
    2. Packages the VS Code extension (npm install + vsce package → .vsix).
    3. Optionally builds PCSX2 from source (requires CMake + MSVC/MinGW).
    4. Assembles a clean distributable folder:
          <OutputDir>\CodeDesignerSetup\
              Install-CodeDesigner.ps1    ← end-user installer
              codedesigner.vsix
              CodeDesigner\               ← Bridge exe + DLLs
              PCSX2\                      ← (optional) PCSX2 exe + DLLs
              README.txt
    5. Zips that folder to <OutputDir>\CodeDesignerSetup.zip for easy sharing.

    Prerequisites
    ─────────────
    • .NET 8 SDK  https://dotnet.microsoft.com/download
    • Node.js + npm  https://nodejs.org/
    • @vscode/vsce  (installed automatically via npx if absent)
    • For PCSX2: CMake + MSVC or MinGW

.PARAMETER RepoRoot
    Path to the CodeDesigner repository root.
    Defaults to the parent of the installer/ folder.

.PARAMETER Pcsx2RepoRoot
    Path to the PCSX2 source repository.  Leave empty to skip the PCSX2 build.

.PARAMETER OutputDir
    Where to place the distributable folder and zip.
    Defaults to "<RepoRoot>\dist".

.PARAMETER Configuration
    .NET build configuration: Debug or Release (default: Release).

.PARAMETER SkipDotnet
    Skip the dotnet build step (binary already built).

.PARAMETER SkipExtension
    Skip the VS Code extension build step.

.EXAMPLE
    # Interactive — prompts for everything
    .\Build-Artifacts.ps1

.EXAMPLE
    # Fully scripted (CI-friendly)
    .\Build-Artifacts.ps1 `
        -RepoRoot      "C:\Dev\CodeDesigner" `
        -Pcsx2RepoRoot "C:\Dev\pcsx2" `
        -OutputDir     "C:\Build\dist"
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$Pcsx2RepoRoot,
    [string]$OutputDir,
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipDotnet,
    [switch]$SkipExtension
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Helpers ──────────────────────────────────────────────────────────────────

function Write-Header {
    param([string]$Text)
    Write-Host ''
    Write-Host ('─' * 60) -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host ('─' * 60) -ForegroundColor Cyan
}

function Read-NonEmptyString {
    param(
        [string]$Prompt,
        [string]$Default = '',
        [switch]$AllowEmpty
    )
    $displayDefault = if ($Default) { " [$Default]" } else { '' }
    do {
        $raw = Read-Host "$Prompt$displayDefault"
        $value = if ($raw.Trim() -eq '' -and $Default) { $Default } else { $raw.Trim() }
        if ($AllowEmpty -or $value -ne '') { return $value }
        Write-Warning 'A value is required.'
    } while ($true)
}

function Confirm-YesNo {
    param([string]$Prompt, [bool]$DefaultYes = $true)
    $hint = if ($DefaultYes) { '[Y/n]' } else { '[y/N]' }
    $raw = Read-Host "$Prompt $hint"
    if ($raw.Trim() -eq '') { return $DefaultYes }
    return $raw.Trim() -match '^[Yy]'
}

function Require-Command {
    param([string]$Name, [string]$InstallHint = '')
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $cmd) {
        Write-Error "'$Name' not found.$(if ($InstallHint) { " $InstallHint" })"
    }
    return $cmd
}

# ── Banner ────────────────────────────────────────────────────────────────────

Clear-Host
Write-Host @'
  ___         _     ___         _                   
 / __|___  __| |___|   \ ___ __(_)__ _ _ _  ___ _ _ 
| (__/ _ \/ _` / -_) |) / -_|_-< / _` | ' \/ -_) '_|
 \___\___/\__,_\___|___/\___/__/_\__, |_||_\___|_|  
                                 |___/              

'@ -ForegroundColor Magenta
Write-Host 'CodeDesigner — Build Artifacts (developer)' -ForegroundColor White
Write-Host ''

# ── Step 1 — Repository root ─────────────────────────────────────────────────

Write-Header 'Step 1 — CodeDesigner repository root'

$scriptDir = $PSScriptRoot
$defaultRepo = Split-Path $scriptDir -Parent

if (-not $RepoRoot) {
    $RepoRoot = Read-NonEmptyString `
        -Prompt 'Path to the CodeDesigner repository root' `
        -Default $defaultRepo
}

$slnFile = Join-Path $RepoRoot 'CodeDesigner.Ps2.sln'
if (-not (Test-Path $slnFile)) { $slnFile = Join-Path $RepoRoot 'CodeDesigner.sln' }
if (-not (Test-Path $slnFile)) {
    Write-Error "CodeDesigner solution not found in '$RepoRoot'. Check the path and try again."
}
Write-Host "Repository: $RepoRoot" -ForegroundColor Green
Write-Host "Solution:   $slnFile"

# ── Step 2 — Output directory ─────────────────────────────────────────────────

Write-Header 'Step 2 — Output folder'

if (-not $OutputDir) {
    $defaultOut = Join-Path $RepoRoot 'dist'
    $OutputDir = Read-NonEmptyString `
        -Prompt 'Output folder for build artifacts' `
        -Default $defaultOut
}
if (-not (Test-Path $OutputDir -PathType Container)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}
Write-Host "Output: $OutputDir" -ForegroundColor Green

# Package staging area — everything ends up in CodeDesignerSetup\
$setupDir = Join-Path $OutputDir 'CodeDesignerSetup'
if (Test-Path $setupDir) { Remove-Item $setupDir -Recurse -Force }
New-Item -ItemType Directory -Path $setupDir -Force | Out-Null

# ── Step 3 — Optional PCSX2 source root ──────────────────────────────────────

Write-Header 'Step 3 — PCSX2 source repository (optional)'
Write-Host 'If you have PCSX2 (with the CodeDesigner PINE patch) already built,'
Write-Host 'enter its source or bin folder path to include it in the package.'
Write-Host 'Leave blank to skip — users can point to their own PCSX2 installation.'
Write-Host ''

if (-not $Pcsx2RepoRoot) {
    $buildPcsx2 = Confirm-YesNo -Prompt 'Bundle PCSX2 binaries in the package?' -DefaultYes $false
    if ($buildPcsx2) {
        $Pcsx2RepoRoot = Read-NonEmptyString -Prompt 'Path to the PCSX2 repository root'
    }
}
if ($Pcsx2RepoRoot -and -not (Test-Path $Pcsx2RepoRoot -PathType Container)) {
    Write-Warning "PCSX2 root '$Pcsx2RepoRoot' not found — skipping PCSX2."
    $Pcsx2RepoRoot = ''
}

# ── Step 4 — Build CodeDesigner ───────────────────────────────────────────────

if (-not $SkipDotnet) {
    Write-Header "Step 4 — Building CodeDesigner ($Configuration)"

    Require-Command 'dotnet' `
        -InstallHint 'Install .NET 8 SDK from https://dotnet.microsoft.com/download' | Out-Null

    Push-Location $RepoRoot
    try {
        Write-Host "dotnet build `"$slnFile`" -c $Configuration" -ForegroundColor Yellow
        dotnet build $slnFile -c $Configuration
        if ($LASTEXITCODE -ne 0) { Write-Error "dotnet build failed (exit $LASTEXITCODE)." }
        Write-Host 'Build succeeded.' -ForegroundColor Green
    } finally {
        Pop-Location
    }

    # Locate the Bridge bin output.
    # The main executable project is named CodeDesigner.Ps2.Bridge; fall back to
    # searching for any project that produces a bin\<Config>\net8.0 folder so that
    # minor project renames do not break the build.
    $bridgeCandidateNames = @('CodeDesigner.Ps2.Bridge', 'CodeDesigner.Ps2', 'CodeDesigner.Mips64LE')
    $bridgeBin = $null
    foreach ($candidateName in $bridgeCandidateNames) {
        $candidateBase = Join-Path $RepoRoot "$candidateName\bin\$Configuration"
        if (-not (Test-Path $candidateBase -PathType Container)) { continue }
        $found = Join-Path $candidateBase 'net8.0'
        if (-not (Test-Path $found -PathType Container)) {
            $found = Get-ChildItem -Path $candidateBase -Directory -ErrorAction SilentlyContinue |
                     Select-Object -ExpandProperty FullName -First 1
        }
        if ($found -and (Test-Path $found -PathType Container)) {
            $bridgeBin = $found
            break
        }
    }
    if ($bridgeBin) {
        $cdDest = Join-Path $setupDir 'CodeDesigner'
        Write-Host "Staging CodeDesigner binaries → $cdDest" -ForegroundColor Yellow
        Copy-Item -Path $bridgeBin -Destination $cdDest -Recurse -Force
        Write-Host 'Done.' -ForegroundColor Green
    } else {
        Write-Warning "Bridge bin folder not found under '$RepoRoot'; skipping binary staging."
        Write-Warning "Looked for: $($bridgeCandidateNames -join ', ')"
    }
} else {
    Write-Host 'Skipping .NET build (-SkipDotnet).' -ForegroundColor Yellow
}

# ── Step 5 — Build VS Code extension ─────────────────────────────────────────

if (-not $SkipExtension) {
    Write-Header 'Step 5 — Building VS Code extension'

    Require-Command 'node' -InstallHint 'Install Node.js from https://nodejs.org/' | Out-Null
    Require-Command 'npm'  -InstallHint 'Install Node.js from https://nodejs.org/' | Out-Null

    # Discover the VS Code extension directory — try common locations
    $extDirCandidates = @(
        (Join-Path $RepoRoot 'vscode\codedesigner'),
        (Join-Path $RepoRoot 'vscode/codedesigner'),
        (Join-Path $RepoRoot 'vscode\CodeDesigner'),
        (Join-Path $RepoRoot 'extensions\codedesigner')
    )
    $extDir = $extDirCandidates | Where-Object { Test-Path $_ -PathType Container } | Select-Object -First 1
    if (-not $extDir) {
        Write-Warning "VS Code extension directory not found; searched: $($extDirCandidates[0]), ..."
        Write-Warning 'Skipping extension build.'
    } else {
        Push-Location $extDir
        try {
            Write-Host 'npm install...' -ForegroundColor Yellow
            npm install
            if ($LASTEXITCODE -ne 0) { Write-Error 'npm install failed.' }

            Write-Host 'npm run package...' -ForegroundColor Yellow
            npm run package
            if ($LASTEXITCODE -ne 0) { Write-Error 'npm run package failed.' }

            $vsixOut = Join-Path $setupDir 'codedesigner.vsix'
            if (Get-Command 'vsce' -ErrorAction SilentlyContinue) {
                vsce package --out $vsixOut --no-yarn
            } else {
                Write-Host 'vsce not found; using npx @vscode/vsce...' -ForegroundColor Yellow
                npx --yes @vscode/vsce package --out $vsixOut --no-yarn
            }

            if ($LASTEXITCODE -eq 0) {
                Write-Host "Extension packaged: $vsixOut" -ForegroundColor Green
            } else {
                Write-Warning "vsce returned exit code $LASTEXITCODE."
            }
        } finally {
            Pop-Location
        }
    }
} else {
    Write-Host 'Skipping extension build (-SkipExtension).' -ForegroundColor Yellow
}

# ── Step 6 — Build and stage PCSX2 ───────────────────────────────────────────

if ($Pcsx2RepoRoot) {
    Write-Header 'Step 6 — Building PCSX2'

    $cmake = Get-Command 'cmake' -ErrorAction SilentlyContinue
    if (-not $cmake) {
        Write-Warning 'cmake not found — skipping PCSX2 build.'
        Write-Warning 'Install CMake from https://cmake.org/download/ and re-run if needed.'
    } else {
        $pcsx2Build = Join-Path $Pcsx2RepoRoot 'build'
        if (-not (Test-Path $pcsx2Build -PathType Container)) {
            New-Item -ItemType Directory -Path $pcsx2Build -Force | Out-Null
        }
        Push-Location $pcsx2Build
        try {
            Write-Host "cmake $Pcsx2RepoRoot -DCMAKE_BUILD_TYPE=$Configuration" -ForegroundColor Yellow
            cmake $Pcsx2RepoRoot -DCMAKE_BUILD_TYPE=$Configuration
            if ($LASTEXITCODE -ne 0) { Write-Error 'cmake configure failed.' }

            Write-Host 'cmake --build ...' -ForegroundColor Yellow
            cmake --build . --config $Configuration
            if ($LASTEXITCODE -ne 0) { Write-Error 'cmake build failed.' }

            $pcsx2BinSrc = Join-Path $pcsx2Build "bin\$Configuration"
            if (-not (Test-Path $pcsx2BinSrc -PathType Container)) {
                $pcsx2BinSrc = Join-Path $pcsx2Build 'bin'
            }
            if (Test-Path $pcsx2BinSrc -PathType Container) {
                $pcsx2Dest = Join-Path $setupDir 'PCSX2'
                Copy-Item -Path $pcsx2BinSrc -Destination $pcsx2Dest -Recurse -Force
                Write-Host "PCSX2 staged: $pcsx2Dest" -ForegroundColor Green
            } else {
                Write-Warning "PCSX2 bin folder not found; skipping staging."
            }
        } finally {
            Pop-Location
        }
    }
}

# ── Step 7 — Copy installer script + README ──────────────────────────────────

Write-Header 'Step 7 — Assembling distributable package'

# Copy Install-CodeDesigner.ps1 into the setup folder so users get everything in one zip
$installerSrc = Join-Path $scriptDir 'Install-CodeDesigner.ps1'
if (Test-Path $installerSrc -PathType Leaf) {
    Copy-Item -Path $installerSrc -Destination $setupDir -Force
    Write-Host "Copied installer script → $setupDir" -ForegroundColor Green
} else {
    Write-Warning "Install-CodeDesigner.ps1 not found at '$installerSrc'; skipping copy."
}

# Write a plain-text README for non-technical recipients
$readmeTxt = @"
CodeDesigner — PlayStation 2 Toolkit
======================================

QUICK START
-----------
1. Make sure you have Visual Studio Code installed.
   Download: https://code.visualstudio.com/

2. Make sure you have the .NET 8 Runtime installed.
   Download: https://dotnet.microsoft.com/download/dotnet/8.0
   (Choose "Windows" → ".NET Desktop Runtime")

3. Right-click Install-CodeDesigner.ps1 and choose
   "Run with PowerShell".

   If you see a security warning, click "Open" or run this
   in a PowerShell window first:
     Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

4. Follow the on-screen prompts.  You will be asked for:
   • A folder to use as your workspace
   • The path to your PS2 BIOS file
   • Optionally, paths to your game disc images (.iso / .zip)

5. Open VS Code in the workspace folder you chose.
   The CodeDesigner panel will appear in the sidebar.

CONTENTS
--------
  Install-CodeDesigner.ps1   End-user installer (run this)
  codedesigner.vsix          VS Code extension
  CodeDesigner\              Built CodeDesigner console app
  PCSX2\                     PCSX2 emulator with PINE patch (if included)

SUPPORT
-------
  https://github.com/TylerDev888/CodeDesigner
"@

Set-Content -Path (Join-Path $setupDir 'README.txt') -Value $readmeTxt -Encoding UTF8
Write-Host 'Written README.txt' -ForegroundColor Green

# ── Step 8 — Create distributable zip ────────────────────────────────────────

$zipPath = Join-Path $OutputDir 'CodeDesignerSetup.zip'
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Write-Host "Creating zip: $zipPath" -ForegroundColor Yellow
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($setupDir, $zipPath)
$zipSize = (Get-Item $zipPath).Length / 1MB
Write-Host ("Zip created: {0} ({1:F1} MB)" -f $zipPath, $zipSize) -ForegroundColor Green

# ── Done ──────────────────────────────────────────────────────────────────────

Write-Host ''
Write-Host ('─' * 60) -ForegroundColor Green
Write-Host '  Build complete!' -ForegroundColor Green
Write-Host ('─' * 60) -ForegroundColor Green
Write-Host ''
Write-Host 'Distributable package:' -ForegroundColor White
Write-Host "  Folder: $setupDir"
Write-Host "  Zip:    $zipPath"
Write-Host ''
Write-Host 'Send CodeDesignerSetup.zip to the user.' -ForegroundColor Cyan
Write-Host 'They extract the zip, right-click Install-CodeDesigner.ps1,' -ForegroundColor Cyan
Write-Host 'choose "Run with PowerShell", and follow the prompts.' -ForegroundColor Cyan
Write-Host ''
