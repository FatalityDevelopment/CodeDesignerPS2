#Requires -Version 5.1
<#
.SYNOPSIS
    CodeDesigner end-user installer.

.DESCRIPTION
    Run this script from the CodeDesigner distributable package to get everything
    working in minutes.  No GitHub account, no source code, no compilers needed.

    The script auto-detects the CodeDesigner executable and .vsix extension that
    are bundled in the same folder — you only need to supply:

      • Where you want to keep your workspace/project files
      • The path to your PS2 BIOS dump
      • Optionally, the path(s) to your game ISO files

    It then:
      1. Writes emu-settings.json into your workspace folder.
      2. Installs the CodeDesigner VS Code extension (.vsix).

    Prerequisites
    ─────────────
    • Windows 10/11 with PowerShell 5.1+
    • Visual Studio Code  https://code.visualstudio.com/
    • .NET 8 Runtime      https://dotnet.microsoft.com/download/dotnet/8.0
      (any of: ".NET Runtime", ".NET Desktop Runtime", or "ASP.NET Core Runtime")

.PARAMETER WorkspaceDir
    Folder where emu-settings.json will be written.
    Defaults to a "CodeDesigner" folder in your Documents.

.PARAMETER BiosPaths
    One or more paths to PS2 BIOS dump files.

.PARAMETER GamePaths
    One or more paths to game .iso/.zip files, or a folder to scan.

.PARAMETER Pcsx2BinDir
    Path to an installed PCSX2 binary folder (optional).
    If the distributable package contains a PCSX2 folder, it is used automatically.

.EXAMPLE
    # Double-click or run from PowerShell — fully interactive
    .\Install-CodeDesigner.ps1

.EXAMPLE
    # Scripted / unattended install
    .\Install-CodeDesigner.ps1 `
        -WorkspaceDir "C:\MyProject" `
        -BiosPaths    @("C:\PS2\bios\SCPH-70012.bin") `
        -GamePaths    @("C:\PS2\isos")
#>
[CmdletBinding()]
param(
    [string]  $WorkspaceDir,
    [string[]]$BiosPaths,
    [string[]]$GamePaths,
    # Advanced: override the auto-detected CodeDesigner exe folder.
    [string]  $CodeDesignerBinDir,
    # Advanced: override the auto-detected .vsix path.
    [string]  $VsixPath,
    # Advanced: specify a PCSX2 bin folder (only needed when not bundled).
    [string]  $Pcsx2BinDir
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

function Find-Executable {
    param([string]$Dir, [string[]]$Names)
    foreach ($name in $Names) {
        $candidate = Join-Path $Dir $name
        if (Test-Path $candidate -PathType Leaf) { return $candidate }
    }
    return $null
}

function Install-VsixExtension {
    param([string]$VsixFile)
    $codeCmds = @('code', 'code-insiders', 'codium')
    foreach ($cmd in $codeCmds) {
        if (Get-Command $cmd -ErrorAction SilentlyContinue) {
            Write-Host "Installing extension via '$cmd'..." -ForegroundColor Yellow
            & $cmd --install-extension $VsixFile --force
            if ($LASTEXITCODE -eq 0) {
                Write-Host 'Extension installed successfully.' -ForegroundColor Green
                return $true
            }
            Write-Warning "'$cmd --install-extension' returned exit code $LASTEXITCODE."
        }
    }
    return $false
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
Write-Host 'CodeDesigner — PlayStation 2 Toolkit Installer' -ForegroundColor White
Write-Host 'Version 1.0' -ForegroundColor DarkGray
Write-Host ''

# ── Auto-detect bundled files ─────────────────────────────────────────────────
#
# When the developer sends you this package the layout is:
#
#   CodeDesignerSetup\
#       Install-CodeDesigner.ps1   ← this script
#       codedesigner.vsix
#       CodeDesigner\
#           CodeDesigner.Ps2.Bridge.exe   (+ supporting DLLs)
#       PCSX2\                            (optional)
#           pcsx2-qtx64.exe   (+ supporting DLLs)
#
# We resolve all paths relative to the folder this script lives in so the
# end user never has to type any binary paths.

$packageDir = $PSScriptRoot

# -- Locate CodeDesigner executable --
$cdExeNames = @(
    'CodeDesigner.Ps2.Bridge.exe',
    'CodeDesigner.Ps2.exe',
    'CodeDesigner.Ps2.Bridge'
)
$cdBinDir = if ($CodeDesignerBinDir) {
    $CodeDesignerBinDir
} else {
    Join-Path $packageDir 'CodeDesigner'
}
$cdExe = Find-Executable -Dir $cdBinDir -Names $cdExeNames

# -- Locate .vsix --
if (-not $VsixPath) {
    $candidate = Join-Path $packageDir 'codedesigner.vsix'
    if (Test-Path $candidate -PathType Leaf) { $VsixPath = $candidate }
}

# -- Check for bundled PCSX2 --
$bundledPcsx2Dir = Join-Path $packageDir 'PCSX2'
$hasBundledPcsx2 = Test-Path $bundledPcsx2Dir -PathType Container

# -- Announce what we found --
Write-Host 'Detected package layout:' -ForegroundColor Cyan
if ($cdExe -and (Test-Path $cdExe)) {
    Write-Host "  CodeDesigner : $cdExe" -ForegroundColor Green
} else {
    Write-Host "  CodeDesigner : not found in $cdBinDir" -ForegroundColor Red
}
if ($VsixPath -and (Test-Path $VsixPath)) {
    Write-Host "  Extension    : $VsixPath" -ForegroundColor Green
} else {
    Write-Host "  Extension    : .vsix not found in $packageDir" -ForegroundColor Yellow
}
if ($hasBundledPcsx2) {
    Write-Host "  PCSX2        : bundled in $bundledPcsx2Dir" -ForegroundColor Green
}
Write-Host ''

if (-not $cdExe -or -not (Test-Path $cdExe)) {
    Write-Warning "CodeDesigner executable not found in the expected location."
    Write-Warning "Expected: $cdBinDir\CodeDesigner.Ps2.Bridge.exe"
    Write-Warning "Make sure you extracted the full package before running this installer."
    $cdExe = Join-Path $cdBinDir 'CodeDesigner.Ps2.Bridge.exe'
}

# ── Step 1 — Workspace folder ─────────────────────────────────────────────────

Write-Header 'Step 1 of 3 — Workspace folder'
Write-Host 'This is the folder VS Code will open as your project.'
Write-Host 'emu-settings.json (emulator configuration) will be saved here.'
Write-Host ''

$defaultWorkspace = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'CodeDesigner'
if (-not $WorkspaceDir) {
    $WorkspaceDir = Read-NonEmptyString `
        -Prompt 'Enter the workspace folder path' `
        -Default $defaultWorkspace
}
if (-not (Test-Path $WorkspaceDir -PathType Container)) {
    Write-Host "Creating folder: $WorkspaceDir" -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $WorkspaceDir -Force | Out-Null
}
Write-Host "Workspace: $WorkspaceDir" -ForegroundColor Green

# ── Step 2 — PS2 BIOS file(s) ────────────────────────────────────────────────

Write-Header 'Step 2 of 3 — PS2 BIOS file'
Write-Host 'CodeDesigner needs a PS2 BIOS dump to run the built-in emulator.'
Write-Host 'This is a file you dump from your own PS2 console.'
Write-Host 'Common file names: SCPH-70012_BIOS_V12_USA_200.bin, scph10000.bin'
Write-Host ''

$biosEntries = [System.Collections.Generic.List[hashtable]]::new()

if ($BiosPaths -and $BiosPaths.Count -gt 0) {
    foreach ($bp in $BiosPaths) {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($bp)
        $biosEntries.Add(@{ name = $name; path = $bp })
        Write-Host "Added BIOS: $bp" -ForegroundColor Green
    }
} else {
    do {
        $biosPath = Read-NonEmptyString -Prompt 'Enter the full path to your PS2 BIOS file'
        if (-not (Test-Path $biosPath -PathType Leaf)) {
            Write-Warning "File not found: $biosPath"
            Write-Warning 'Check the path and try again, or press Enter to add it anyway.'
            $addAnyway = Confirm-YesNo -Prompt 'Add this path anyway?' -DefaultYes $true
            if (-not $addAnyway) { continue }
        }
        $defaultName = [System.IO.Path]::GetFileNameWithoutExtension($biosPath)
        $biosName = Read-NonEmptyString -Prompt 'Friendly name for this BIOS' -Default $defaultName
        $biosEntries.Add(@{ name = $biosName; path = $biosPath })
        Write-Host "Added: $biosPath" -ForegroundColor Green
    } while (Confirm-YesNo -Prompt 'Add another BIOS file?' -DefaultYes $false)
}

# ── Step 3 — Game ISO(s) ──────────────────────────────────────────────────────

Write-Header 'Step 3 of 3 — Game disc images (optional)'
Write-Host 'Enter paths to your game .iso or .zip files, or a folder that contains them.'
Write-Host 'You can also add or change games later from the Settings tab inside VS Code.'
Write-Host ''

$gameEntries = [System.Collections.Generic.List[hashtable]]::new()

if ($GamePaths -and $GamePaths.Count -gt 0) {
    foreach ($gp in $GamePaths) {
        if (Test-Path $gp -PathType Container) {
            $files = Get-ChildItem -Path $gp -Include '*.iso','*.zip' -File -ErrorAction SilentlyContinue
            foreach ($f in $files) {
                $gameName = [System.IO.Path]::GetFileNameWithoutExtension($f.FullName)
                $gameEntries.Add(@{ name = $gameName; path = $f.FullName })
                Write-Host "Added game: $($f.FullName)" -ForegroundColor Green
            }
        } else {
            $gameName = [System.IO.Path]::GetFileNameWithoutExtension($gp)
            $gameEntries.Add(@{ name = $gameName; path = $gp })
            Write-Host "Added game: $gp" -ForegroundColor Green
        }
    }
} else {
    $addGames = Confirm-YesNo -Prompt 'Do you want to add game disc images now?' -DefaultYes $true
    if ($addGames) {
        do {
            Write-Host 'Tip: enter a folder path to add all .iso/.zip files inside it.'
            $gamePath = Read-NonEmptyString -Prompt 'Game path (file or folder)'
            if (Test-Path $gamePath -PathType Container) {
                $files = Get-ChildItem -Path $gamePath -Include '*.iso','*.zip' -File -ErrorAction SilentlyContinue
                if ($files.Count -eq 0) {
                    Write-Warning 'No .iso or .zip files found in that folder.'
                } else {
                    foreach ($f in $files) {
                        $gameName = [System.IO.Path]::GetFileNameWithoutExtension($f.FullName)
                        $gameEntries.Add(@{ name = $gameName; path = $f.FullName })
                        Write-Host "Added: $($f.FullName)" -ForegroundColor Green
                    }
                }
            } else {
                if (-not (Test-Path $gamePath -PathType Leaf)) {
                    Write-Warning "Path not found: $gamePath — adding anyway."
                }
                $gameName = Read-NonEmptyString `
                    -Prompt 'Friendly name for this game' `
                    -Default ([System.IO.Path]::GetFileNameWithoutExtension($gamePath))
                $gameEntries.Add(@{ name = $gameName; path = $gamePath })
                Write-Host "Added: $gamePath" -ForegroundColor Green
            }
        } while (Confirm-YesNo -Prompt 'Add another game?' -DefaultYes $false)
    }
}

# ── PCSX2 (auto-configured from bundle or user-supplied) ─────────────────────

$pcsx2Exe = $null
$pcsx2ExeNames = @(
    'pcsx2-qtx64.exe',
    'pcsx2-qtx64-dbg.exe',
    'pcsx2-qt.exe',
    'pcsx2.exe',
    'PCSX2.exe'
)

if ($hasBundledPcsx2) {
    $pcsx2Exe = Find-Executable -Dir $bundledPcsx2Dir -Names $pcsx2ExeNames
    if ($pcsx2Exe) {
        Write-Host ''
        Write-Host "PCSX2 auto-configured from bundle: $pcsx2Exe" -ForegroundColor Green
    } else {
        Write-Warning "PCSX2 folder found but no executable located in: $bundledPcsx2Dir"
    }
} elseif ($Pcsx2BinDir) {
    $pcsx2Exe = Find-Executable -Dir $Pcsx2BinDir -Names $pcsx2ExeNames
    if (-not $pcsx2Exe) {
        Write-Warning "PCSX2 executable not found in '$Pcsx2BinDir'."
        $pcsx2Exe = Join-Path $Pcsx2BinDir 'pcsx2-qtx64.exe'
    } else {
        Write-Host "PCSX2 found: $pcsx2Exe" -ForegroundColor Green
    }
} else {
    # Ask only if not already handled
    Write-Host ''
    $addPcsx2 = Confirm-YesNo -Prompt 'Do you have PCSX2 installed and want to configure it?' -DefaultYes $false
    if ($addPcsx2) {
        $Pcsx2BinDir = Read-NonEmptyString -Prompt 'Enter the path to your PCSX2 folder'
        $pcsx2Exe = Find-Executable -Dir $Pcsx2BinDir -Names $pcsx2ExeNames
        if (-not $pcsx2Exe) {
            Write-Warning "Could not locate a PCSX2 executable in '$Pcsx2BinDir'."
            $pcsx2Exe = Join-Path $Pcsx2BinDir 'pcsx2-qtx64.exe'
        } else {
            Write-Host "PCSX2 found: $pcsx2Exe" -ForegroundColor Green
        }
    }
}

# ── Write emu-settings.json ───────────────────────────────────────────────────

Write-Header 'Writing emu-settings.json'

$emulatorsArray = [System.Collections.Generic.List[hashtable]]::new()
$emulatorsArray.Add(@{ type = 'code-designer'; path = $cdExe })
if ($pcsx2Exe) {
    $emulatorsArray.Add(@{ type = 'pcsx2'; path = $pcsx2Exe })
}

$settings = [ordered]@{
    bios      = $biosEntries.ToArray()
    games     = $gameEntries.ToArray()
    emulators = $emulatorsArray.ToArray()
}

$settingsJson = $settings | ConvertTo-Json -Depth 5
$settingsPath = Join-Path $WorkspaceDir 'emu-settings.json'

Set-Content -Path $settingsPath -Value $settingsJson -Encoding UTF8
Write-Host "Written: $settingsPath" -ForegroundColor Green
Write-Host ''
Write-Host $settingsJson -ForegroundColor DarkGray

# ── Install .vsix extension ───────────────────────────────────────────────────

Write-Header 'Installing VS Code extension'

if ($VsixPath -and (Test-Path $VsixPath -PathType Leaf)) {
    Write-Host "Extension file: $VsixPath" -ForegroundColor Cyan
    $installed = Install-VsixExtension -VsixFile $VsixPath
    if (-not $installed) {
        Write-Warning 'Automatic extension install failed.'
        Write-Host ''
        Write-Host 'To install manually, open a terminal and run:' -ForegroundColor Yellow
        Write-Host "  code --install-extension `"$VsixPath`"" -ForegroundColor Yellow
    }
} else {
    Write-Warning 'codedesigner.vsix was not found in the package folder.'
    Write-Host 'To install the extension manually, drag the .vsix onto VS Code' -ForegroundColor Yellow
    Write-Host 'or run: code --install-extension <path-to-codedesigner.vsix>' -ForegroundColor Yellow
}

# ── Done ──────────────────────────────────────────────────────────────────────

Write-Host ''
Write-Host ('─' * 60) -ForegroundColor Green
Write-Host '  Installation complete!' -ForegroundColor Green
Write-Host ('─' * 60) -ForegroundColor Green
Write-Host ''
Write-Host 'Next steps:' -ForegroundColor White
Write-Host '  1. Open VS Code in your workspace folder:'
Write-Host "       code `"$WorkspaceDir`""
Write-Host '  2. The CodeDesigner icon will appear in the VS Code sidebar.'
Write-Host '  3. In the Settings tab, select your BIOS and game, then click Connect.'
if ($pcsx2Exe) {
    Write-Host ''
    Write-Host 'PCSX2 notes:' -ForegroundColor Cyan
    Write-Host '  • Make sure PCSX2 was built with the CodeDesigner PINE server patch.'
    Write-Host '  • Start PCSX2, load your game, then click "Start PCSX2" in VS Code.'
    Write-Host '  • The PINE slot defaults to 28011; edit emu-settings.json to change it.'
}
Write-Host ''
