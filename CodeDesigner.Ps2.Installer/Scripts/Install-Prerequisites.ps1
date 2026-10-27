#Requires -Version 5.1
<#
.SYNOPSIS
    Installs what CodeDesigner needs on this machine, skipping anything already present.

.DESCRIPTION
    User mode      : .NET 8 Runtime, VS Code, SQL Server LocalDB.
    Developer mode : .NET 8 SDK, Node.js LTS, Git, VS Code, SQL Server LocalDB.
    Both modes also install the VC++ runtime that PCSX2 needs, unless -SkipVcRedist.

    Packages come from winget; LocalDB comes from Microsoft's SQL Server Express bootstrapper
    because it has no winget package. Windows asks for administrator approval where an
    installer needs it.

.EXAMPLE
    .\Install-Prerequisites.ps1 -Mode Developer
#>
[CmdletBinding()]
param(
    [ValidateSet('User', 'Developer')]
    [string]$Mode = 'User',
    # Skip the VC++ runtime (only PCSX2 needs it).
    [switch]$SkipVcRedist
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CodeDesigner.Setup.psm1') -Force

Write-Step "Checking prerequisites ($Mode)"

if ($Mode -eq 'Developer') {
    Install-WingetPackage -Id 'Microsoft.DotNet.SDK.8' -Name '.NET SDK (8 or newer) and .NET 8 runtime' -Test { Test-DotnetSdk8 } `
        -ManualUrl 'https://dotnet.microsoft.com/download/dotnet/8.0'
    Install-WingetPackage -Id 'OpenJS.NodeJS.LTS' -Name 'Node.js 20+' -Test { Test-Node20 } `
        -ManualUrl 'https://nodejs.org/'
    Install-WingetPackage -Id 'Git.Git' -Name 'Git' -Test { Test-Git } `
        -ManualUrl 'https://git-scm.com/download/win'
} else {
    Install-WingetPackage -Id 'Microsoft.DotNet.Runtime.8' -Name '.NET 8 Runtime' -Test { Test-DotnetRuntime8 } `
        -ManualUrl 'https://dotnet.microsoft.com/download/dotnet/8.0'
}

if (-not $SkipVcRedist) {
    Install-WingetPackage -Id 'Microsoft.VCRedist.2015+.x64' -Name 'Visual C++ runtime (x64)' -Test { Test-VcRedist } `
        -ManualUrl 'https://aka.ms/vs/17/release/vc_redist.x64.exe'
}

Install-WingetPackage -Id 'Microsoft.VisualStudioCode' -Name 'VS Code' -Test { [bool](Get-VsCodeCli) } `
    -ManualUrl 'https://code.visualstudio.com/'

Install-LocalDb

Write-Step 'Prerequisites ready'
