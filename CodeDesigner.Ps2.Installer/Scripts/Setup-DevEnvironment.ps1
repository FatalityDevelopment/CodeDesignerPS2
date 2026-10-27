#Requires -Version 5.1
<#
.SYNOPSIS
    Sets up a CodeDesigner development checkout: clone, build, deploy, database, extension.

.DESCRIPTION
    1. Clones github.com/FatalityDevelopment/CodeDesignerPS2 with submodules into -RepoDir,
       or updates the submodules of an existing checkout.
    2. Builds CodeDesigner.sln. The build deploys the Bridge, emulator and MCP server to
       %LOCALAPPDATA%\CodeDesigner and migrates the LocalDB database, importing the
       CheatCodeArchive submodule (see docs/building.md).
    3. Builds the VS Code extension, packages it and installs the .vsix into VS Code.

    Run Install-Prerequisites.ps1 -Mode Developer first (the installer app does).
    PCSX2 is not built here; the installer downloads the prebuilt fork from the latest
    release, or see docs/pcsx2-building.md.

.EXAMPLE
    .\Setup-DevEnvironment.ps1 -RepoDir "$env:USERPROFILE\source\repos\CodeDesignerPS2"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RepoDir,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipBuild,
    [switch]$SkipExtension
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CodeDesigner.Setup.psm1') -Force
Update-SessionPath

# Resolve against the PowerShell location (GetFullPath would use the process working directory).
$RepoDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($RepoDir)

# ---- 1. Source --------------------------------------------------------------------

Write-Step 'Getting the source'
if (-not (Test-Git)) { throw 'Git was not found. Run Install-Prerequisites.ps1 -Mode Developer first.' }

if (Test-Path (Join-Path $RepoDir '.git')) {
    Write-Info "Using the existing checkout at $RepoDir."
    Invoke-Native -Exe git -Arguments @('-C', $RepoDir, 'submodule', 'update', '--init', '--recursive', '--jobs', '4')
} else {
    if ((Test-Path $RepoDir) -and (Get-ChildItem $RepoDir -Force | Select-Object -First 1)) {
        throw "$RepoDir exists, is not empty and is not a git checkout. Pick an empty or new folder."
    }
    Write-Info "Cloning $(Get-RepoUrl) (with the PCSX2 and CheatCodeArchive submodules, this takes a while)..."
    Invoke-Native -Exe git -Arguments @('clone', '--recurse-submodules', '--jobs', '4', (Get-RepoUrl), $RepoDir)
}
Write-Ok "Source ready at $RepoDir."

# ---- 2. .NET build ----------------------------------------------------------------

if (-not $SkipBuild) {
    Write-Step "Building CodeDesigner.sln ($Configuration)"
    $dotnet = Get-DotnetExe
    if (-not $dotnet -or -not (Test-DotnetSdk8)) { throw '.NET 8 SDK was not found. Run Install-Prerequisites.ps1 -Mode Developer first.' }

    Assert-NotRunningFrom -Dir (Get-InstallDir)
    Start-LocalDbInstance

    $archive = Join-Path $RepoDir 'CheatCodeArchive'
    Invoke-Native -Exe $dotnet -Arguments @(
        'build', (Join-Path $RepoDir 'CodeDesigner.sln'), '-c', $Configuration, "-p:CheatArchiveRoot=$archive", '-nologo'
    )
    Write-Ok "Built and deployed to $(Get-InstallDir)."
}

# ---- 3. VS Code extension ---------------------------------------------------------

if (-not $SkipExtension) {
    Write-Step 'Building the VS Code extension'
    if (-not (Test-Node20)) { throw 'Node.js 20+ was not found. Run Install-Prerequisites.ps1 -Mode Developer first.' }
    $extDir = Join-Path $RepoDir 'vscode\codedesigner'
    $vsix = Join-Path $RepoDir 'publish\codedesigner.vsix'
    New-Item -ItemType Directory -Path (Split-Path $vsix) -Force | Out-Null

    Push-Location $extDir
    try {
        Invoke-Native -Exe npm.cmd -Arguments @('ci', '--no-audit', '--no-fund')
        Invoke-Native -Exe npx.cmd -Arguments @('--yes', '@vscode/vsce', 'package', '--skip-license', '--out', $vsix)
    } finally {
        Pop-Location
    }
    Install-Vsix -Path $vsix
}

Write-Step 'Developer environment ready'
Write-Info "Open $RepoDir in VS Code. The CodeDesigner icon is in the activity bar."
Write-Info 'Rebuild with: dotnet build CodeDesigner.sln  (stop the Bridge first; see docs/building.md)'
