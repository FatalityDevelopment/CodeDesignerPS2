param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][string]$LogPath,
    [Parameter(Mandatory = $true)][string]$ErrPath,
    [Parameter(Mandatory = $true)][string]$PidPath,
    [string[]]$ArgList = @()
)

$ErrorActionPreference = 'Stop'

if (!(Test-Path -LiteralPath $ExePath)) {
    throw "PCSX2 executable not found: $ExePath"
}

$logDir = Split-Path -Parent $LogPath
if ($logDir -and !(Test-Path -LiteralPath $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

$pidDir = Split-Path -Parent $PidPath
if ($pidDir -and !(Test-Path -LiteralPath $pidDir)) {
    New-Item -ItemType Directory -Path $pidDir -Force | Out-Null
}

$proc = Start-Process -FilePath $ExePath `
    -ArgumentList $ArgList `
    -WindowStyle Hidden `
    -RedirectStandardOutput $LogPath `
    -RedirectStandardError $ErrPath `
    -PassThru

$proc.Id | Out-File -FilePath $PidPath -Encoding ascii -Force
Write-Output "PCSX2 started hidden (PID $($proc.Id))."
