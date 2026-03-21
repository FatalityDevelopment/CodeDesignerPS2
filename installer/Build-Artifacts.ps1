#Requires -Version 5.1
<#
.SYNOPSIS
    Build-Artifacts — builds CodeDesigner and packages the VS Code extension into a .vsix.

.DESCRIPTION
    This developer-facing helper script:
      1. Prompts for (or accepts as parameters) the CodeDesigner repository root and the
         PCSX2 repository root.
      2. Builds the CodeDesigner .NET solution in Release mode.
      3. Builds the VS Code extension (npm install + vsce package → .vsix).
      4. Optionally builds the PCSX2 solution if a PCSX2 source directory is provided.
      5. Copies the resulting artifacts to a chosen output folder so that
         Install-CodeDesigner.ps1 can be run against it.

    Prerequisites
    ─────────────
    • .NET 8 SDK  (https://dotnet.microsoft.com/download)
    • Node.js + npm  (https://nodejs.org/)
    • @vscode/vsce  (installed automatically via npx if missing)
    • For PCSX2: CMake + MSVC or MinGW build chain

.PARAMETER RepoRoot
    Path to the CodeDesigner repository root.  Defaults to the parent directory of
    the installer/ folder (i.e. the directory that contains CodeDesigner.Ps2.sln).

.PARAMETER Pcsx2RepoRoot
    Path to the PCSX2 source repository root.  Leave empty to skip PCSX2 build.

.PARAMETER OutputDir
    Destination folder for all artifacts.  Defaults to "<RepoRoot>/dist".

.PARAMETER Configuration
    .NET build configuration: Debug or Release (default: Release).

.PARAMETER SkipDotnet
    Skip the dotnet build step (useful when the binary is already built).

.PARAMETER SkipExtension
    Skip the VS Code extension build step.

.EXAMPLE
    # Interactive — prompts for everything
    .\Build-Artifacts.ps1

.EXAMPLE
    # Fully specified
    .\Build-Artifacts.ps1 `
        -RepoRoot       "C:\Dev\CodeDesigner" `
        -Pcsx2RepoRoot  "C:\Dev\pcsx2" `
        -OutputDir      "C:\Build\CodeDesignerDist"
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
        Write-Error "'$Name' is not installed or not on PATH.$(if ($InstallHint) { " $InstallHint" })"
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
Write-Host 'CodeDesigner — Build Artifacts' -ForegroundColor White
Write-Host ''

# ── Step 1 — Resolve repository root ─────────────────────────────────────────

Write-Header 'Step 1 — CodeDesigner repository root'

# Default: the directory one level above the installer/ folder (repo root)
$scriptDir = $PSScriptRoot
$defaultRepo = Split-Path $scriptDir -Parent

if (-not $RepoRoot) {
    $RepoRoot = Read-NonEmptyString `
        -Prompt 'Enter the path to the CodeDesigner repository root' `
        -Default $defaultRepo
}

$slnFile = Join-Path $RepoRoot 'CodeDesigner.Ps2.sln'
if (-not (Test-Path $slnFile)) {
    # Try the legacy solution name
    $slnFile = Join-Path $RepoRoot 'CodeDesigner.sln'
}
if (-not (Test-Path $slnFile)) {
    Write-Error "Could not find CodeDesigner.Ps2.sln (or CodeDesigner.sln) in '$RepoRoot'."
}
Write-Host "Repository root: $RepoRoot" -ForegroundColor Green
Write-Host "Solution file:   $slnFile"

# ── Step 2 — Output directory ─────────────────────────────────────────────────

Write-Header 'Step 2 — Output / distribution folder'

if (-not $OutputDir) {
    $defaultOut = Join-Path $RepoRoot 'dist'
    $OutputDir = Read-NonEmptyString `
        -Prompt 'Enter the output folder for build artifacts' `
        -Default $defaultOut
}
if (-not (Test-Path $OutputDir -PathType Container)) {
    Write-Host "Creating output folder: $OutputDir" -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}
Write-Host "Output folder: $OutputDir" -ForegroundColor Green

# ── Step 3 — Optional PCSX2 source root ──────────────────────────────────────

Write-Header 'Step 3 — PCSX2 source repository (optional)'
Write-Host 'If you have a local copy of PCSX2 built with the CodeDesigner PINE patch,'
Write-Host 'enter its path here so the artifacts can be bundled together.'
Write-Host ''

if (-not $Pcsx2RepoRoot) {
    $buildPcsx2 = Confirm-YesNo -Prompt 'Include PCSX2 build step?' -DefaultYes $false
    if ($buildPcsx2) {
        $Pcsx2RepoRoot = Read-NonEmptyString -Prompt 'Enter the path to the PCSX2 repository root'
    }
}

if ($Pcsx2RepoRoot -and -not (Test-Path $Pcsx2RepoRoot -PathType Container)) {
    Write-Warning "PCSX2 source root '$Pcsx2RepoRoot' was not found — skipping PCSX2 build."
    $Pcsx2RepoRoot = ''
}

# ── Step 4 — Build CodeDesigner .NET solution ─────────────────────────────────

if (-not $SkipDotnet) {
    Write-Header "Step 4 — Building CodeDesigner ($Configuration)"

    Require-Command 'dotnet' -InstallHint 'Install .NET 8 SDK from https://dotnet.microsoft.com/download' | Out-Null

    Push-Location $RepoRoot
    try {
        Write-Host "dotnet build `"$slnFile`" -c $Configuration" -ForegroundColor Yellow
        dotnet build $slnFile -c $Configuration
        if ($LASTEXITCODE -ne 0) {
            Write-Error "dotnet build failed with exit code $LASTEXITCODE."
        }
        Write-Host 'dotnet build succeeded.' -ForegroundColor Green
    } finally {
        Pop-Location
    }

    # Copy Bridge binaries to OutputDir\CodeDesigner
    $cdBridgeBin = Join-Path $RepoRoot "CodeDesigner.Ps2.Bridge\bin\$Configuration\net8.0"
    if (-not (Test-Path $cdBridgeBin -PathType Container)) {
        # Some SDK target folders omit the RID; fall back to first match
        $cdBridgeBin = Get-ChildItem -Path (Join-Path $RepoRoot "CodeDesigner.Ps2.Bridge\bin\$Configuration") `
                        -Directory -ErrorAction SilentlyContinue |
                       Select-Object -ExpandProperty FullName -First 1
    }
    if ($cdBridgeBin -and (Test-Path $cdBridgeBin -PathType Container)) {
        $cdOut = Join-Path $OutputDir 'CodeDesigner'
        Write-Host "Copying CodeDesigner binaries to: $cdOut" -ForegroundColor Yellow
        Copy-Item -Path $cdBridgeBin -Destination $cdOut -Recurse -Force
        Write-Host 'Copy complete.' -ForegroundColor Green
    } else {
        Write-Warning "Bridge bin folder not found at '$cdBridgeBin'; skipping binary copy."
    }
} else {
    Write-Host 'Skipping .NET build (--SkipDotnet specified).' -ForegroundColor Yellow
}

# ── Step 5 — Build VS Code extension (.vsix) ─────────────────────────────────

if (-not $SkipExtension) {
    Write-Header 'Step 5 — Building VS Code extension'

    Require-Command 'node' -InstallHint 'Install Node.js from https://nodejs.org/' | Out-Null
    Require-Command 'npm'  -InstallHint 'Install Node.js from https://nodejs.org/' | Out-Null

    $extDir = Join-Path $RepoRoot 'vscode\codedesigner'
    if (-not (Test-Path $extDir -PathType Container)) {
        Write-Warning "VS Code extension directory not found at '$extDir'; skipping extension build."
    } else {
        Push-Location $extDir
        try {
            Write-Host 'npm install...' -ForegroundColor Yellow
            npm install
            if ($LASTEXITCODE -ne 0) { Write-Error "npm install failed." }

            Write-Host 'npm run package...' -ForegroundColor Yellow
            npm run package
            if ($LASTEXITCODE -ne 0) { Write-Error "npm run package failed." }

            # Package into a .vsix using vsce
            $vsceCmd = Get-Command 'vsce' -ErrorAction SilentlyContinue
            if (-not $vsceCmd) {
                Write-Host 'vsce not found; using npx @vscode/vsce...' -ForegroundColor Yellow
                npx --yes @vscode/vsce package --out "$OutputDir\codedesigner.vsix" --no-yarn
            } else {
                vsce package --out "$OutputDir\codedesigner.vsix" --no-yarn
            }

            if ($LASTEXITCODE -eq 0) {
                Write-Host "Extension packaged: $OutputDir\codedesigner.vsix" -ForegroundColor Green
            } else {
                Write-Warning "vsce package returned exit code $LASTEXITCODE — .vsix may not have been created."
            }
        } finally {
            Pop-Location
        }
    }
} else {
    Write-Host 'Skipping extension build (--SkipExtension specified).' -ForegroundColor Yellow
}

# ── Step 6 — Optional PCSX2 build ────────────────────────────────────────────

if ($Pcsx2RepoRoot) {
    Write-Header 'Step 6 — Building PCSX2 (with CodeDesigner PINE patch)'

    $cmake = Get-Command 'cmake' -ErrorAction SilentlyContinue
    if (-not $cmake) {
        Write-Warning 'cmake not found on PATH — skipping PCSX2 build.'
        Write-Warning 'Install CMake from https://cmake.org/download/ and re-run if needed.'
    } else {
        $pcsx2BuildDir = Join-Path $Pcsx2RepoRoot 'build'
        if (-not (Test-Path $pcsx2BuildDir -PathType Container)) {
            New-Item -ItemType Directory -Path $pcsx2BuildDir -Force | Out-Null
        }

        Push-Location $pcsx2BuildDir
        try {
            Write-Host "cmake configure: $Pcsx2RepoRoot" -ForegroundColor Yellow
            cmake $Pcsx2RepoRoot -DCMAKE_BUILD_TYPE=$Configuration
            if ($LASTEXITCODE -ne 0) { Write-Error "cmake configure failed." }

            Write-Host 'cmake --build ...' -ForegroundColor Yellow
            cmake --build . --config $Configuration
            if ($LASTEXITCODE -ne 0) { Write-Error "cmake build failed." }

            # Copy PCSX2 binaries to OutputDir\PCSX2
            $pcsx2BinSrc = Join-Path $pcsx2BuildDir "bin\$Configuration"
            if (-not (Test-Path $pcsx2BinSrc -PathType Container)) {
                $pcsx2BinSrc = Join-Path $pcsx2BuildDir 'bin'
            }
            if (Test-Path $pcsx2BinSrc -PathType Container) {
                $pcsx2Out = Join-Path $OutputDir 'PCSX2'
                Copy-Item -Path $pcsx2BinSrc -Destination $pcsx2Out -Recurse -Force
                Write-Host "PCSX2 binaries copied to: $pcsx2Out" -ForegroundColor Green
            } else {
                Write-Warning "PCSX2 bin output not found at '$pcsx2BinSrc'."
            }
        } finally {
            Pop-Location
        }
    }
} else {
    if (-not $SkipDotnet -or -not $SkipExtension) {
        Write-Host ''
        Write-Host 'PCSX2 build skipped.' -ForegroundColor DarkGray
    }
}

# ── Summary ───────────────────────────────────────────────────────────────────

Write-Host ''
Write-Host ('─' * 60) -ForegroundColor Green
Write-Host '  Build complete!' -ForegroundColor Green
Write-Host ('─' * 60) -ForegroundColor Green
Write-Host ''
Write-Host "Artefacts folder: $OutputDir" -ForegroundColor White
Write-Host ''

$vsixFile = Join-Path $OutputDir 'codedesigner.vsix'
$cdBinOut = Join-Path $OutputDir 'CodeDesigner'

Write-Host 'To install for end users, run:' -ForegroundColor Cyan
Write-Host ''
Write-Host "  .\Install-CodeDesigner.ps1 ``" -ForegroundColor Yellow
Write-Host "      -CodeDesignerBinDir `"$cdBinOut`" ``" -ForegroundColor Yellow
if ($Pcsx2RepoRoot) {
    $pcsx2Out = Join-Path $OutputDir 'PCSX2'
    Write-Host "      -Pcsx2BinDir       `"$pcsx2Out`" ``" -ForegroundColor Yellow
}
Write-Host "      -VsixPath           `"$vsixFile`"" -ForegroundColor Yellow
Write-Host ''
