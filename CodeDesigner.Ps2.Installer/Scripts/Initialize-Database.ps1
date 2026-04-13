#Requires -Version 5.1
<#
.SYNOPSIS
    Initialises the CodeDesigner database and imports the CheatCodeArchive.

.DESCRIPTION
    1. Ensures the SQL Server LocalDB instance (MSSQLLocalDB) is running.
    2. Runs CodeDesigner.Ps2.Database.exe to:
         a. Apply EF Core migrations (create / update the CodeDesigner database).
         b. Seed default configuration data.
         c. Bulk-import all cheat codes from the CheatCodeArchive.
    3. Prints a short summary when done.

.PARAMETER DbExePath
    Path to CodeDesigner.Ps2.Database.exe.
    Defaults to the 'CodeDesigner' sub-folder inside %LOCALAPPDATA%\CodeDesigner.

.PARAMETER ArchiveRoot
    Path to a CheatCodeArchive checkout whose Database\ sub-folder will be imported.
    Defaults to the 'CheatCodeArchive' sub-folder inside %LOCALAPPDATA%\CodeDesigner.

.PARAMETER ConnectionString
    Override the SQL Server LocalDB connection string.
    Defaults to: Server=(localdb)\MSSQLLocalDB;Database=CodeDesigner;Trusted_Connection=True;

.EXAMPLE
    # Fully automatic — uses paths from Download-And-Install.ps1
    .\Initialize-Database.ps1

.EXAMPLE
    # Custom paths
    .\Initialize-Database.ps1 `
        -DbExePath    "C:\MyBuild\CodeDesigner.Ps2.Database.exe" `
        -ArchiveRoot  "C:\Projects\CheatCodeArchive"
#>
[CmdletBinding()]
param(
    [string] $DbExePath,
    [string] $ArchiveRoot,
    [string] $ConnectionString
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Defaults ──────────────────────────────────────────────────────────────────

$InstallDir = Join-Path $env:LOCALAPPDATA 'CodeDesigner'

if (-not $DbExePath) {
    $DbExePath = Join-Path $InstallDir 'CodeDesigner\CodeDesigner.Ps2.Database.exe'
}

if (-not $ArchiveRoot) {
    $ArchiveRoot = Join-Path $InstallDir 'CheatCodeArchive'
}

if (-not $ConnectionString) {
    $ConnectionString = 'Server=(localdb)\MSSQLLocalDB;Database=CodeDesigner;Trusted_Connection=True;MultipleActiveResultSets=True;'
}

# ── Helpers ───────────────────────────────────────────────────────────────────

function Write-Header {
    param([string]$Text)
    Write-Host ''
    Write-Host ('─' * 60) -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host ('─' * 60) -ForegroundColor Cyan
}

function Invoke-Exe {
    param([string]$Exe, [string[]]$Arguments)
    Write-Host "  > $Exe $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Process exited with code $LASTEXITCODE: $Exe"
    }
}

# ── Banner ────────────────────────────────────────────────────────────────────

Write-Host ''
Write-Host '  CodeDesigner — Database Initialisation' -ForegroundColor Magenta
Write-Host ''

# ── Step 1 — Validate paths ───────────────────────────────────────────────────

Write-Header 'Step 1 of 3 — Validating paths'

if (-not (Test-Path $DbExePath -PathType Leaf)) {
    Write-Error @"
CodeDesigner.Ps2.Database.exe not found at:
  $DbExePath

Run Download-And-Install.ps1 first, or supply -DbExePath.
"@
    exit 1
}
Write-Host "  DB tool  : $DbExePath" -ForegroundColor Green

if (-not (Test-Path $ArchiveRoot -PathType Container)) {
    Write-Error @"
CheatCodeArchive folder not found at:
  $ArchiveRoot

Either run Download-And-Install.ps1 (if the archive ships with the release),
clone the archive manually, or supply -ArchiveRoot.
"@
    exit 1
}
Write-Host "  Archive  : $ArchiveRoot" -ForegroundColor Green
Write-Host "  DB conn  : $ConnectionString" -ForegroundColor DarkGray

# ── Step 2 — Ensure LocalDB instance is running ───────────────────────────────

Write-Header 'Step 2 of 3 — Starting SQL Server LocalDB'

$sqllocaldb = Get-Command sqllocaldb -ErrorAction SilentlyContinue
if (-not $sqllocaldb) {
    Write-Warning 'sqllocaldb.exe not found in PATH. SQL Server LocalDB may not be installed.'
    Write-Warning 'Install it via: choco install sql-server-localdb  (or rerun Download-And-Install.ps1)'
} else {
    $instanceInfo = & sqllocaldb info MSSQLLocalDB 2>&1
    if ($instanceInfo -match 'State:\s+Stopped') {
        Write-Host '  Starting MSSQLLocalDB instance...' -ForegroundColor Yellow
        & sqllocaldb start MSSQLLocalDB | Out-Null
    }
    $instanceInfo = & sqllocaldb info MSSQLLocalDB 2>&1
    if ($instanceInfo -match 'State:\s+Running') {
        Write-Host '  MSSQLLocalDB is running.' -ForegroundColor Green
    } else {
        Write-Warning "  MSSQLLocalDB state could not be confirmed:`n$instanceInfo"
    }
}

# ── Step 3 — Run database tool ────────────────────────────────────────────────

Write-Header 'Step 3 of 3 — Migrating database and importing cheats'

Write-Host '  Running migrations and seeding config...' -ForegroundColor Yellow
Invoke-Exe -Exe $DbExePath -Arguments @(
    '--connection-string', $ConnectionString,
    '--archive-root',      $ArchiveRoot
)

Write-Host '  Database initialised and cheats imported.' -ForegroundColor Green

# ── Summary ───────────────────────────────────────────────────────────────────

Write-Host ''
Write-Host ('─' * 60) -ForegroundColor Cyan
Write-Host '  Done!' -ForegroundColor Green
Write-Host ('─' * 60) -ForegroundColor Cyan
Write-Host ''
Write-Host '  The CodeDesigner database is ready.'
Write-Host "  Connection : $ConnectionString"
Write-Host ''
Write-Host '  Next: launch CodeDesigner.Ps2.Bridge.exe and open VS Code.'
Write-Host ''
