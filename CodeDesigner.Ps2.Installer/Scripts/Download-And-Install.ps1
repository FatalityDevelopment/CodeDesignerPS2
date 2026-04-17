#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Downloads CodeDesigner + PCSX2 releases and installs SQL Server LocalDB.

.DESCRIPTION
    1. Downloads CodeDesigner.zip and PCSX2.zip from the GitHub release.
    2. Extracts both into C:\Users\<you>\AppData\Local\CodeDesigner.
    3. Installs Chocolatey if not present, then installs sql-server-localdb
       (the latest version available in the Chocolatey feed).

.EXAMPLE
    # Run from an elevated PowerShell prompt
    .\Download-And-Install.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Constants ─────────────────────────────────────────────────────────────────

$InstallDir   = Join-Path $env:LOCALAPPDATA 'CodeDesigner'
$TempDir      = Join-Path $env:TEMP 'CodeDesignerSetup'

$Downloads = @(
    @{
        Url      = 'https://github.com/TylerDev888/CodeDesigner/releases/download/v1.0.0/CodeDesigner.zip'
        FileName = 'CodeDesigner.zip'
        SubDir   = 'CodeDesigner'
    },
    @{
        Url      = 'https://github.com/TylerDev888/CodeDesigner/releases/download/v1.0.0/PCSX2.zip'
        FileName = 'PCSX2.zip'
        SubDir   = 'PCSX2'
    }
)

# ── Helpers ───────────────────────────────────────────────────────────────────

function Write-Header {
    param([string]$Text)
    Write-Host ''
    Write-Host ('─' * 60) -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host ('─' * 60) -ForegroundColor Cyan
}

function Get-FileWithProgress {
    param([string]$Url, [string]$Dest)
    Write-Host "  Downloading: $([System.IO.Path]::GetFileName($Dest))" -ForegroundColor Yellow
    $wc = [System.Net.WebClient]::new()
    $wc.Headers.Add('User-Agent', 'CodeDesigner-Installer/1.0')
    try {
        $wc.DownloadFile($Url, $Dest)
    }
    finally {
        $wc.Dispose()
    }
    $sizeMB = [math]::Round((Get-Item $Dest).Length / 1MB, 1)
    Write-Host "  Saved $sizeMB MB -> $Dest" -ForegroundColor Green
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
Write-Host 'CodeDesigner — Download & Setup' -ForegroundColor White
Write-Host "Install location: $InstallDir" -ForegroundColor DarkGray
Write-Host ''

# ── Prepare directories ───────────────────────────────────────────────────────

Write-Header 'Step 1 of 3 — Preparing directories'

foreach ($dir in @($InstallDir, $TempDir)) {
    if (-not (Test-Path $dir -PathType Container)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "  Created: $dir" -ForegroundColor Green
    } else {
        Write-Host "  Exists:  $dir" -ForegroundColor DarkGray
    }
}

# ── Download & extract ZIPs ───────────────────────────────────────────────────

Write-Header 'Step 2 of 3 — Downloading and extracting packages'

Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($pkg in $Downloads) {
    $zipPath  = Join-Path $TempDir $pkg.FileName
    $destPath = Join-Path $InstallDir $pkg.SubDir

    # Download
    if (Test-Path $zipPath -PathType Leaf) {
        Write-Host "  Already downloaded: $($pkg.FileName) (skipping)" -ForegroundColor DarkGray
    } else {
        Get-FileWithProgress -Url $pkg.Url -Dest $zipPath
    }

    # Extract — wipe existing subfolder first so re-runs are idempotent
    if (Test-Path $destPath -PathType Container) {
        Write-Host "  Removing existing: $destPath" -ForegroundColor Yellow
        Remove-Item $destPath -Recurse -Force
    }

    Write-Host "  Extracting $($pkg.FileName) -> $destPath" -ForegroundColor Yellow
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $destPath)
    Write-Host "  Done: $destPath" -ForegroundColor Green
}

# ── Chocolatey + SQL Server LocalDB ──────────────────────────────────────────

Write-Header 'Step 3 of 3 — Installing SQL Server LocalDB via Chocolatey'

# Install Chocolatey if not present
if (-not (Get-Command choco -ErrorAction SilentlyContinue)) {
    Write-Host '  Chocolatey not found — installing...' -ForegroundColor Yellow
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
    $chocoScript = (New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1')
    Invoke-Expression $chocoScript
    # Reload PATH so 'choco' is available in this session
    $env:PATH = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' +
                [System.Environment]::GetEnvironmentVariable('PATH', 'User')
    Write-Host '  Chocolatey installed.' -ForegroundColor Green
} else {
    Write-Host '  Chocolatey already installed.' -ForegroundColor DarkGray
}

# Install SQL Server LocalDB (latest stable)
Write-Host '  Installing sql-server-localdb (latest)...' -ForegroundColor Yellow
choco install sql-server-localdb --yes --no-progress
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Chocolatey returned exit code $LASTEXITCODE. SQL Server LocalDB may not have installed correctly."
} else {
    Write-Host '  SQL Server LocalDB installed successfully.' -ForegroundColor Green
}

# ── Summary ───────────────────────────────────────────────────────────────────

Write-Host ''
Write-Host ('─' * 60) -ForegroundColor Cyan
Write-Host '  Setup complete!' -ForegroundColor Green
Write-Host ('─' * 60) -ForegroundColor Cyan
Write-Host ''
Write-Host "  CodeDesigner : $InstallDir\CodeDesigner"
Write-Host "  PCSX2        : $InstallDir\CodeDesigner\pcsx2"
