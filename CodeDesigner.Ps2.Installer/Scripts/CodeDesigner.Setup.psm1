# Shared helpers for the CodeDesigner setup scripts.
#
# Every script in this folder is non-interactive: all input comes from parameters so the
# installer app (which runs them without a console) and CI can drive them. Keep this file
# ASCII-only - Windows PowerShell 5.1 reads BOM-less scripts as cp1252.

Set-StrictMode -Version Latest

$script:RepoSlug  = 'FatalityDevelopment/CodeDesignerPS2'
$script:RepoUrl   = "https://github.com/$script:RepoSlug.git"
$script:UserAgent = 'CodeDesigner-Installer'

# winget exit codes that mean "nothing to do".
$script:WingetAlreadyInstalled = @(
    -1978335189,  # 0x8A15002B  APPINSTALLER_CLI_ERROR_UPDATE_NOT_APPLICABLE
    -1978335135   # 0x8A150061  APPINSTALLER_CLI_ERROR_PACKAGE_ALREADY_INSTALLED
)

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
# Invoke-WebRequest's progress bar slows downloads by an order of magnitude on 5.1.
$global:ProgressPreference = 'SilentlyContinue'

function Get-RepoSlug { $script:RepoSlug }
function Get-RepoUrl  { $script:RepoUrl }

# The directory the extension, Bridge and database tool all agree on
# (DatabasePaths.GetInstallDirectory / disasmPanelProvider.getInstallDirectory).
function Get-InstallDir { Join-Path $env:LOCALAPPDATA 'CodeDesigner' }

function Write-Step {
    param([string]$Text)
    Write-Host ''
    Write-Host "== $Text"
}

function Write-Ok   { param([string]$Text) Write-Host "   [ok] $Text" }
function Write-Info { param([string]$Text) Write-Host "        $Text" }

# Re-reads PATH from the registry so tools installed earlier in this run are found.
function Update-SessionPath {
    $machine = [Environment]::GetEnvironmentVariable('PATH', 'Machine')
    $user    = [Environment]::GetEnvironmentVariable('PATH', 'User')
    $env:PATH = (@($machine, $user) | Where-Object { $_ }) -join ';'
}

# Runs a native command, streaming its output to the host, and throws when it exits
# with a code outside $OkExitCodes. Output goes to Out-Host so callers' pipelines stay clean.
function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$Exe,
        [string[]]$Arguments = @(),
        [int[]]$OkExitCodes = @(0)
    )
    Write-Info "> $Exe $($Arguments -join ' ')"
    & $Exe @Arguments | Out-Host
    if ($OkExitCodes -notcontains $LASTEXITCODE) {
        throw "'$Exe' exited with code $LASTEXITCODE."
    }
}

function Get-WingetExe {
    $cmd = Get-Command winget -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidate = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe'
    if (Test-Path $candidate) { return $candidate }
    return $null
}

# Installs a winget package unless $Test already reports it present.
function Install-WingetPackage {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Test,
        [string]$ManualUrl
    )
    if (& $Test) {
        Write-Ok "$Name is already installed."
        return
    }
    $winget = Get-WingetExe
    if (-not $winget) {
        throw "$Name is missing and winget (App Installer) is not available. Install $Name from $ManualUrl, or install App Installer from the Microsoft Store, then run setup again."
    }
    Write-Info "Installing $Name ($Id) with winget..."
    Invoke-Native -Exe $winget -Arguments @(
        'install', '--id', $Id, '--exact', '--source', 'winget', '--silent',
        '--accept-package-agreements', '--accept-source-agreements', '--disable-interactivity'
    ) -OkExitCodes (@(0) + $script:WingetAlreadyInstalled)
    Update-SessionPath
    if (-not (& $Test)) {
        throw "$Name was installed but could not be found afterwards. Restart setup, or install it from $ManualUrl."
    }
    Write-Ok "$Name installed."
}

# ---- Prerequisite probes -------------------------------------------------------------

function Get-DotnetExe {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidate = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path $candidate) { return $candidate }
    return $null
}

function Test-DotnetRuntime8 {
    $dotnet = Get-DotnetExe
    if (-not $dotnet) { return $false }
    return [bool](& $dotnet --list-runtimes 2>$null | Where-Object { $_ -match '^Microsoft\.NETCore\.App 8\.' })
}

# Any SDK 8 or newer builds the net8.0 projects; running them still needs the 8 runtime.
function Test-DotnetSdk8 {
    $dotnet = Get-DotnetExe
    if (-not $dotnet) { return $false }
    $sdks = & $dotnet --list-sdks 2>$null | Where-Object { $_ -match '^(\d+)\.' -and [int]$Matches[1] -ge 8 }
    return ([bool]$sdks -and (Test-DotnetRuntime8))
}

function Test-Node20 {
    $node = Get-Command node -ErrorAction SilentlyContinue
    if (-not $node) { return $false }
    $version = (& $node.Source --version 2>$null) -replace '^v', ''
    return ($version -and [int]($version.Split('.')[0]) -ge 20)
}

function Test-Git { [bool](Get-Command git -ErrorAction SilentlyContinue) }

function Test-VcRedist {
    $key = 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64'
    $value = Get-ItemProperty -Path $key -Name Installed -ErrorAction SilentlyContinue
    return ($value -and $value.Installed -eq 1)
}

# VS Code's CLI: code.cmd on PATH, or the default per-user / per-machine location.
function Get-VsCodeCli {
    # Ask for the .cmd shims explicitly: plain 'code' can resolve to the GUI Code.exe.
    foreach ($name in 'code.cmd', 'code-insiders.cmd', 'codium.cmd') {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Microsoft VS Code\bin\code.cmd'),
        (Join-Path $env:ProgramFiles 'Microsoft VS Code\bin\code.cmd')
    )
    return $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

function Get-SqlLocalDbExe {
    $cmd = Get-Command sqllocaldb -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $root = Join-Path $env:ProgramFiles 'Microsoft SQL Server'
    if (-not (Test-Path $root)) { return $null }
    return Get-ChildItem -Path $root -Filter SqlLocalDB.exe -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}

# ---- SQL Server LocalDB --------------------------------------------------------------

# LocalDB has no winget package. Microsoft's SQL Server Express bootstrapper can download
# the standalone SqlLocalDB.msi (MEDIATYPE=LocalDB), which is then installed with msiexec.
function Install-LocalDb {
    if (Get-SqlLocalDbExe) {
        Write-Ok 'SQL Server LocalDB is already installed.'
        return
    }
    $work = Join-Path $env:TEMP 'CodeDesignerSetup\LocalDB'
    New-Item -ItemType Directory -Path $work -Force | Out-Null

    $bootstrapper = Join-Path $work 'SQL-SSEI-Expr.exe'
    Write-Info 'Downloading the SQL Server Express bootstrapper...'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://go.microsoft.com/fwlink/p/?linkid=2216019' -OutFile $bootstrapper -UserAgent $script:UserAgent

    Write-Info 'Downloading SqlLocalDB.msi...'
    $bootArgs = @('/ACTION=Download', "MEDIAPATH=$work", '/MEDIATYPE=LocalDB', '/QUIET')
    $proc = Start-ElevatedIfNeeded -FilePath $bootstrapper -ArgumentList $bootArgs
    if ($proc.ExitCode -ne 0) { throw "The SQL Server bootstrapper exited with code $($proc.ExitCode)." }

    $msi = Get-ChildItem -Path $work -Filter SqlLocalDB.msi -Recurse | Select-Object -First 1
    if (-not $msi) { throw "SqlLocalDB.msi was not found under $work after the download." }

    Write-Info 'Installing SQL Server LocalDB (Windows will ask for administrator approval)...'
    $proc = Start-Process -FilePath msiexec.exe -Verb RunAs -Wait -PassThru -ArgumentList @(
        '/i', "`"$($msi.FullName)`"", 'IACCEPTSQLLOCALDBLICENSETERMS=YES', '/qn', '/norestart')
    # 3010 = success, reboot required.
    if (@(0, 3010) -notcontains $proc.ExitCode) { throw "msiexec exited with code $($proc.ExitCode) installing LocalDB." }

    Update-SessionPath
    if (-not (Get-SqlLocalDbExe)) { throw 'LocalDB was installed but SqlLocalDB.exe could not be found.' }
    Write-Ok 'SQL Server LocalDB installed.'
}

# Starts a process normally; retries elevated when Windows reports it needs elevation.
function Start-ElevatedIfNeeded {
    param([string]$FilePath, [string[]]$ArgumentList)
    try {
        return Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -Wait -PassThru -ErrorAction Stop
    } catch [System.InvalidOperationException], [System.ComponentModel.Win32Exception] {
        Write-Info 'Administrator approval is required...'
        return Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -Verb RunAs -Wait -PassThru
    }
}

# Makes sure the MSSQLLocalDB instance exists and is running.
function Start-LocalDbInstance {
    $sqllocaldb = Get-SqlLocalDbExe
    if (-not $sqllocaldb) { throw 'SQL Server LocalDB is not installed. Run setup with prerequisites enabled.' }
    $info = & $sqllocaldb info MSSQLLocalDB 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        Write-Info 'Creating the MSSQLLocalDB instance...'
        Invoke-Native -Exe $sqllocaldb -Arguments @('create', 'MSSQLLocalDB', '-s')
    } elseif ($info -notmatch 'State:\s+Running') {
        Invoke-Native -Exe $sqllocaldb -Arguments @('start', 'MSSQLLocalDB')
    }
    Write-Ok 'MSSQLLocalDB is running.'
}

# ---- Running CodeDesigner processes --------------------------------------------------

# Files in the install directory are locked while the Bridge, emulator or MCP server run.
# Never stop them for the user (the Bridge belongs to the extension's terminal).
function Assert-NotRunningFrom {
    param([string]$Dir)
    $full = [IO.Path]::GetFullPath($Dir).TrimEnd('\') + '\'
    $running = Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($full, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
    }
    if ($running) {
        $names = ($running | Select-Object -ExpandProperty ProcessName -Unique) -join ', '
        throw "CodeDesigner is running from $Dir ($names). Close VS Code (or stop the CodeDesigner terminal) and run setup again."
    }
}

# ---- GitHub releases -----------------------------------------------------------------

# Returns the release object for $Tag, or the latest release when $Tag is empty/'latest'.
function Get-Release {
    param([string]$Tag)
    $api = "https://api.github.com/repos/$script:RepoSlug/releases"
    $url = if (-not $Tag -or $Tag -eq 'latest') { "$api/latest" } else { "$api/tags/$Tag" }
    try {
        return Invoke-RestMethod -UseBasicParsing -Uri $url -Headers @{ 'User-Agent' = $script:UserAgent; 'Accept' = 'application/vnd.github+json' }
    } catch {
        $status = $null
        if ($_.Exception.PSObject.Properties['Response'] -and $_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        if ($status -eq 404) {
            throw "github.com/$script:RepoSlug has no release '$(if ($Tag) { $Tag } else { 'latest' })' yet. Releases are published by pushing a v* tag."
        }
        throw "Could not read release '$(if ($Tag) { $Tag } else { 'latest' })' from github.com/$script:RepoSlug : $($_.Exception.Message)"
    }
}

# Downloads one named asset of $Release into $Dir and returns its path.
function Save-ReleaseAsset {
    param([Parameter(Mandatory)]$Release, [Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Dir)
    $asset = $Release.assets | Where-Object { $_.name -eq $Name } | Select-Object -First 1
    if (-not $asset) { throw "Release $($Release.tag_name) has no asset named $Name." }
    New-Item -ItemType Directory -Path $Dir -Force | Out-Null
    $dest = Join-Path $Dir $Name
    Write-Info ("Downloading {0} ({1:N1} MB)..." -f $Name, ($asset.size / 1MB))
    Invoke-WebRequest -UseBasicParsing -Uri $asset.browser_download_url -OutFile $dest -UserAgent $script:UserAgent
    return $dest
}

# Installs a .vsix into VS Code.
function Install-Vsix {
    param([Parameter(Mandatory)][string]$Path)
    $code = Get-VsCodeCli
    if (-not $code) { throw 'VS Code was not found. Install it from https://code.visualstudio.com/ and run setup again.' }
    Invoke-Native -Exe $code -Arguments @('--install-extension', $Path, '--force')
    Write-Ok 'VS Code extension installed.'
}

Export-ModuleMember -Function *
