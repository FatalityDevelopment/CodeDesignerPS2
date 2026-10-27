#Requires -Version 5.1
<#
.SYNOPSIS
    Creates or migrates the CodeDesigner database and imports the cheat archive.

.DESCRIPTION
    Starts the MSSQLLocalDB instance (creating it if needed), then runs
    CodeDesigner.Ps2.Database.exe from the install directory, which applies EF Core
    migrations, seeds the default data and, when the archive is present, bulk-imports
    the CheatCodeArchive. Running it again is safe.

.EXAMPLE
    .\Initialize-Database.ps1
    .\Initialize-Database.ps1 -ArchiveRoot C:\src\CodeDesignerPS2\CheatCodeArchive
#>
[CmdletBinding()]
param(
    # Folder containing the archive's Database\ sub-folder. Defaults to <InstallDir>\CheatCodeArchive.
    [string]$ArchiveRoot,
    # Defaults to CODEDESIGNER_CONNECTION_STRING, then (localdb)\MSSQLLocalDB.
    [string]$ConnectionString
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CodeDesigner.Setup.psm1') -Force

$installDir = Get-InstallDir
$dbExe = Join-Path $installDir 'CodeDesigner.Ps2.Database.exe'
if (-not $ArchiveRoot) { $ArchiveRoot = Join-Path $installDir 'CheatCodeArchive' }

Write-Step 'Starting SQL Server LocalDB'
if (-not $ConnectionString -and -not $env:CODEDESIGNER_CONNECTION_STRING) {
    Start-LocalDbInstance
} else {
    Write-Info 'Using a custom connection string; not touching LocalDB.'
}

Write-Step 'Migrating the database'
if (-not (Test-Path $dbExe)) {
    throw "$dbExe was not found. Install CodeDesigner first (Install-Release.ps1 or a source build)."
}

$dbArgs = @()
if ($ConnectionString) { $dbArgs += @('--connection-string', $ConnectionString) }
if (Test-Path (Join-Path $ArchiveRoot 'Database')) {
    Write-Info "Importing cheats from $ArchiveRoot (the first import takes a few minutes)."
    $dbArgs += @('--archive-root', $ArchiveRoot)
} else {
    Write-Info "No cheat archive at $ArchiveRoot; skipping the cheat import."
}

Invoke-Native -Exe $dbExe -Arguments $dbArgs

Write-Step 'Database ready'
