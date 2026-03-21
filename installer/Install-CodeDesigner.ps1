#Requires -Version 5.1
<#
.SYNOPSIS
    CodeDesigner installer — sets up the VS Code extension and emulator configuration.

.DESCRIPTION
    This script:
      1. Prompts for the CodeDesigner binary folder (the compiled CodeDesigner.Ps2.Bridge
         executable) and optionally the PCSX2 binary folder.
      2. Asks for the BIOS file path(s) and game ISO file/folder.
      3. Writes emu-settings.json into a user-chosen workspace folder so that VS Code
         picks it up immediately.
      4. Installs the CodeDesigner .vsix extension into VS Code.

    Run this script on Windows with PowerShell 5.1+ (or PowerShell 7+).

.EXAMPLE
    # Interactive installation
    .\Install-CodeDesigner.ps1

.EXAMPLE
    # Provide all paths up-front (unattended mode)
    .\Install-CodeDesigner.ps1 `
        -CodeDesignerBinDir "C:\Tools\CodeDesigner\bin" `
        -Pcsx2BinDir        "C:\Tools\pcsx2\bin" `
        -BiosPaths          @("C:\PS2\bios\SCPH-70012.bin") `
        -GamePaths          @("C:\PS2\isos\MySocomGame.iso") `
        -WorkspaceDir       "C:\MyProject" `
        -VsixPath           "C:\Tools\CodeDesigner\codedesigner.vsix"
#>
[CmdletBinding()]
param(
    # Path to the folder that contains CodeDesigner.Ps2.Bridge.exe (or the equivalent).
    [string]$CodeDesignerBinDir,

    # Path to the folder that contains the PCSX2 executable (optional).
    [string]$Pcsx2BinDir,

    # One or more BIOS file paths.
    [string[]]$BiosPaths,

    # One or more game ISO / ZIP paths (or a single directory whose contents are
    # enumerated for .iso and .zip files).
    [string[]]$GamePaths,

    # The workspace/project folder where emu-settings.json will be written.
    # Defaults to the current directory if omitted.
    [string]$WorkspaceDir,

    # Path to the .vsix file to install.  If omitted the script searches the
    # CodeDesignerBinDir (and its parent) for a .vsix file.
    [string]$VsixPath
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

function Find-VsixInTree {
    param([string]$StartDir)
    # Search up to 3 levels up from StartDir for a *.vsix file
    $dir = $StartDir
    for ($i = 0; $i -lt 4; $i++) {
        $found = Get-ChildItem -Path $dir -Filter '*.vsix' -ErrorAction SilentlyContinue |
                 Select-Object -First 1
        if ($found) { return $found.FullName }
        $parent = Split-Path $dir -Parent
        if (-not $parent -or $parent -eq $dir) { break }
        $dir = $parent
    }
    return $null
}

function Install-VsixExtension {
    param([string]$VsixFile)

    $codeCmds = @('code', 'code-insiders', 'codium')
    foreach ($cmd in $codeCmds) {
        $codePath = Get-Command $cmd -ErrorAction SilentlyContinue
        if ($codePath) {
            Write-Host "Installing extension using '$cmd'..." -ForegroundColor Yellow
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

# ── Step 1 — Workspace directory ─────────────────────────────────────────────

Write-Header 'Step 1 of 5 — Workspace / project folder'
Write-Host 'emu-settings.json will be placed in this folder so VS Code can find it.'
Write-Host ''

if (-not $WorkspaceDir) {
    $WorkspaceDir = Read-NonEmptyString `
        -Prompt 'Enter the workspace folder path' `
        -Default (Get-Location).Path
}
if (-not (Test-Path $WorkspaceDir -PathType Container)) {
    Write-Host "Creating directory: $WorkspaceDir" -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $WorkspaceDir -Force | Out-Null
}
Write-Host "Workspace: $WorkspaceDir" -ForegroundColor Green

# ── Step 2 — CodeDesigner binary folder ──────────────────────────────────────

Write-Header 'Step 2 of 5 — CodeDesigner binary folder'
Write-Host 'This is the folder that contains CodeDesigner.Ps2.Bridge.exe (the built console'
Write-Host 'application).  If you have not built the project yet, run Build-Artifacts.ps1 first.'
Write-Host ''

if (-not $CodeDesignerBinDir) {
    $CodeDesignerBinDir = Read-NonEmptyString `
        -Prompt 'Enter the path to the CodeDesigner bin folder'
}

$cdExeNames = @(
    'CodeDesigner.Ps2.Bridge.exe',
    'CodeDesigner.Ps2.exe',
    'CodeDesigner.Ps2.Bridge'
)
$cdExe = Find-Executable -Dir $CodeDesignerBinDir -Names $cdExeNames

if ($cdExe) {
    Write-Host "Found CodeDesigner executable: $cdExe" -ForegroundColor Green
} else {
    Write-Warning "Could not locate a CodeDesigner executable in '$CodeDesignerBinDir'."
    Write-Warning 'The emulators entry will still be written — verify the path manually.'
    $cdExe = Join-Path $CodeDesignerBinDir 'CodeDesigner.Ps2.Bridge.exe'
}

# ── Step 3 — PCSX2 binary folder (optional) ──────────────────────────────────

Write-Header 'Step 3 of 5 — PCSX2 binary folder (optional)'
Write-Host 'CodeDesigner can connect to PCSX2 via the PINE protocol.'
Write-Host 'Skip this step if you only want to use the built-in emulator.'
Write-Host ''

$includePcsx2 = Confirm-YesNo -Prompt 'Configure PCSX2 integration?' -DefaultYes $false

$pcsx2Exe = $null
if ($includePcsx2) {
    if (-not $Pcsx2BinDir) {
        $Pcsx2BinDir = Read-NonEmptyString `
            -Prompt 'Enter the path to the PCSX2 bin folder'
    }

    $pcsx2ExeNames = @(
        'pcsx2-qtx64.exe',
        'pcsx2-qtx64-dbg.exe',
        'pcsx2-qt.exe',
        'pcsx2.exe',
        'PCSX2.exe'
    )
    $pcsx2Exe = Find-Executable -Dir $Pcsx2BinDir -Names $pcsx2ExeNames

    if ($pcsx2Exe) {
        Write-Host "Found PCSX2 executable: $pcsx2Exe" -ForegroundColor Green
    } else {
        Write-Warning "Could not locate a PCSX2 executable in '$Pcsx2BinDir'."
        Write-Warning 'Verify that PCSX2 (with PINE server support) is installed in that folder.'
        $pcsx2Exe = Join-Path $Pcsx2BinDir 'pcsx2-qtx64.exe'
    }
}

# ── Step 4 — BIOS file(s) ────────────────────────────────────────────────────

Write-Header 'Step 4 of 5 — PS2 BIOS file(s)'
Write-Host 'CodeDesigner requires a PS2 BIOS dump to run the built-in emulator.'
Write-Host 'Common file names: SCPH-70012_BIOS_V12_USA_200.bin, scph10000.bin, etc.'
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
        $biosPath = Read-NonEmptyString -Prompt 'Enter the full path to a PS2 BIOS file'
        if (-not (Test-Path $biosPath -PathType Leaf)) {
            Write-Warning "File not found: $biosPath — adding anyway (verify path later)."
        }
        $biosName = Read-NonEmptyString `
            -Prompt 'Friendly name for this BIOS' `
            -Default ([System.IO.Path]::GetFileNameWithoutExtension($biosPath))
        $biosEntries.Add(@{ name = $biosName; path = $biosPath })
        Write-Host "Added BIOS: $biosPath" -ForegroundColor Green
    } while (Confirm-YesNo -Prompt 'Add another BIOS file?' -DefaultYes $false)
}

# ── Step 5 — Game ISO(s) ─────────────────────────────────────────────────────

Write-Header 'Step 5 of 5 — Game ISO / disc image(s)'
Write-Host 'Enter paths to your game .iso or .zip files, or a folder that contains them.'
Write-Host 'You can skip this step and add games later via the Settings tab in VS Code.'
Write-Host ''

$gameEntries = [System.Collections.Generic.List[hashtable]]::new()

if ($GamePaths -and $GamePaths.Count -gt 0) {
    foreach ($gp in $GamePaths) {
        if (Test-Path $gp -PathType Container) {
            # It's a directory — enumerate .iso and .zip files inside it
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
    $addGames = Confirm-YesNo -Prompt 'Do you want to add game ISO paths now?' -DefaultYes $true
    if ($addGames) {
        do {
            Write-Host 'You can enter a .iso/.zip file path, or a folder path to scan for games.'
            $gamePath = Read-NonEmptyString -Prompt 'Game path (file or folder)'
            if (Test-Path $gamePath -PathType Container) {
                $files = Get-ChildItem -Path $gamePath -Include '*.iso','*.zip' -File -ErrorAction SilentlyContinue
                if ($files.Count -eq 0) {
                    Write-Warning 'No .iso or .zip files found in that folder.'
                } else {
                    foreach ($f in $files) {
                        $gameName = [System.IO.Path]::GetFileNameWithoutExtension($f.FullName)
                        $gameEntries.Add(@{ name = $gameName; path = $f.FullName })
                        Write-Host "Added game: $($f.FullName)" -ForegroundColor Green
                    }
                }
            } elseif (Test-Path $gamePath -PathType Leaf) {
                $gameName = Read-NonEmptyString `
                    -Prompt 'Friendly name for this game' `
                    -Default ([System.IO.Path]::GetFileNameWithoutExtension($gamePath))
                $gameEntries.Add(@{ name = $gameName; path = $gamePath })
                Write-Host "Added game: $gamePath" -ForegroundColor Green
            } else {
                Write-Warning "Path not found: $gamePath — adding anyway."
                $gameName = Read-NonEmptyString `
                    -Prompt 'Friendly name for this game' `
                    -Default ([System.IO.Path]::GetFileNameWithoutExtension($gamePath))
                $gameEntries.Add(@{ name = $gameName; path = $gamePath })
            }
        } while (Confirm-YesNo -Prompt 'Add another game?' -DefaultYes $false)
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

if (-not $VsixPath) {
    # Try to find a .vsix near the CodeDesigner bin dir
    $VsixPath = Find-VsixInTree -StartDir $CodeDesignerBinDir
}
if (-not $VsixPath) {
    # Search the script's own directory and its parent
    $scriptDir = $PSScriptRoot
    $VsixPath = Find-VsixInTree -StartDir $scriptDir
}

if ($VsixPath -and (Test-Path $VsixPath -PathType Leaf)) {
    Write-Host "Using .vsix: $VsixPath" -ForegroundColor Cyan
    $installed = Install-VsixExtension -VsixFile $VsixPath
    if (-not $installed) {
        Write-Warning 'Automatic extension install failed.'
        Write-Warning "Please install the extension manually:"
        Write-Warning "  code --install-extension `"$VsixPath`""
    }
} else {
    Write-Warning 'No .vsix file was found.  Skipping extension installation.'
    Write-Host 'To install the extension manually, run:' -ForegroundColor Yellow
    Write-Host '  code --install-extension <path-to-codedesigner.vsix>' -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'To build the .vsix yourself, run Build-Artifacts.ps1 first.' -ForegroundColor Yellow
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
Write-Host '  2. The CodeDesigner sidebar panel will appear on the left.'
Write-Host '  3. Select an emulator, BIOS and game from the Settings tab.'
Write-Host '  4. Click Connect (or Run) to start a session.'
if ($includePcsx2 -and $pcsx2Exe) {
    Write-Host ''
    Write-Host 'PCSX2 notes:' -ForegroundColor Cyan
    Write-Host '  • Make sure PCSX2 is built with the CodeDesigner PINE server patch.'
    Write-Host '  • Start PCSX2 first, then click "Start PCSX2" in the Settings tab.'
    Write-Host '  • The PINE slot defaults to 28011; change it in emu-settings.json if needed.'
}
Write-Host ''
