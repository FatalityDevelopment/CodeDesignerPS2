param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][string]$EmuLogPath,
    [Parameter(Mandatory = $true)][string]$PidPath,
    [bool]$Pcsx2Mode = $false,
    [string[]]$ArgList = @()
)

$ErrorActionPreference = 'Stop'

if (!(Test-Path -LiteralPath $ExePath)) {
    throw "Emulator executable not found: $ExePath"
}

$exeName = [System.IO.Path]::GetFileNameWithoutExtension($ExePath)
$isPcsx2Launch = $Pcsx2Mode -or ($exeName -match '(?i)pcsx2')

function Get-GenericArgList {
    param([string[]]$InputArgs)
    if (-not $InputArgs -or $InputArgs.Count -eq 0) { return @() }

    $separatorIndex = [Array]::IndexOf($InputArgs, '--')
    if ($separatorIndex -ge 0) {
        if ($separatorIndex -ge ($InputArgs.Count - 1)) { return @() }
        return @($InputArgs[($separatorIndex + 1)..($InputArgs.Count - 1)] | Where-Object { $_ })
    }

    $filtered = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $InputArgs.Count; $i++) {
        $arg = $InputArgs[$i]
        if (-not $arg) { continue }
        if ($arg -eq '-logfile') { $i++; continue }
        if ($arg -in @('-batch', '-earlyconsolelog', '-nogui')) { continue }
        $filtered.Add($arg)
    }
    return @($filtered)
}

# Ensure the output directories exist.
$targets = if ($isPcsx2Launch) { @($EmuLogPath, $PidPath) } else { @($PidPath) }
foreach ($target in $targets) {
    $dir = Split-Path -Parent $target
    if ($dir -and !(Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
}

# Drop a stale emu log so we only ever read the current run's output.
if ($isPcsx2Launch -and (Test-Path -LiteralPath $EmuLogPath)) {
    Remove-Item -LiteralPath $EmuLogPath -Force -ErrorAction SilentlyContinue
}

# The folder layout PCSX2 prints via EmuFolders::LoadConfig at startup. These
# lines are pulled out of the stream and rendered as a tidy block.
$DirNames = @(
    'BIOS', 'Snapshots', 'Savestates', 'MemoryCards', 'Logs', 'Cheats',
    'Patches', 'Covers', 'Game Settings', 'Resources', 'User Resources',
    'Cache', 'Textures', 'Input Profile', 'Video Dumping',
    'Debugger Layouts', 'Debugger Settings'
)
$DirPattern = '^(?:\[[^\]]*\]\s*)?(.+?) Directory:\s*(.+?)\s*$'

$script:dirs = [ordered]@{}
$script:dirBlockDone = $false

function Show-DirectoryBlock {
    if ($script:dirBlockDone) { return }
    $script:dirBlockDone = $true
    if ($script:dirs.Count -eq 0) { return }

    $labelWidth = ($script:dirs.Keys | Measure-Object -Property Length -Maximum).Maximum
    Write-Host ''
    Write-Host '  PCSX2 Directories' -ForegroundColor Cyan
    Write-Host ('  ' + ('-' * 64)) -ForegroundColor DarkCyan
    foreach ($name in $script:dirs.Keys) {
        Write-Host ('    ' + $name.PadRight($labelWidth) + '   ') -ForegroundColor Cyan -NoNewline
        Write-Host $script:dirs[$name] -ForegroundColor Gray
    }
    Write-Host ('  ' + ('-' * 64)) -ForegroundColor DarkCyan
    Write-Host ''
}

function Write-EmuLine {
    param([string]$Line)
    if ($Line.Length -eq 0) { Write-Host ''; return }

    $stamp = ''
    $body = $Line
    $m = [regex]::Match($Line, '^(\[[\d\s.:]+\])\s*(.*)$')
    if ($m.Success) {
        $stamp = $m.Groups[1].Value
        $body = $m.Groups[2].Value
    }

    $color = 'Gray'
    $low = $body.ToLowerInvariant()
    if ($low -match 'error|fail|fatal|exception') { $color = 'Red' }
    elseif ($low -match 'warn') { $color = 'Yellow' }

    if ($stamp) {
        Write-Host ($stamp + ' ') -ForegroundColor DarkGray -NoNewline
    }
    Write-Host $body -ForegroundColor $color
}

function Process-EmuLine {
    param([string]$Line)
    if (-not $script:dirBlockDone -and $Line -match $DirPattern -and ($DirNames -contains $matches[1].Trim())) {
        $name = $matches[1].Trim()
        $script:dirs[$name] = $matches[2].Trim()
        if ($name -eq 'Debugger Settings') { Show-DirectoryBlock }
        return
    }
    if (-not $script:dirBlockDone -and $script:dirs.Count -gt 0) {
        Show-DirectoryBlock
    }
    Write-EmuLine $Line
}

# Reads bytes appended to the emu log since $Offset, even while PCSX2 holds it open.
function Read-Appended {
    param([string]$Path, [long]$Offset)
    try {
        $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
        try {
            $len = $stream.Length
            if ($len -le $Offset) { return @{ Text = ''; Offset = $Offset } }
            $stream.Seek($Offset, 'Begin') | Out-Null
            $count = [int]($len - $Offset)
            $buffer = New-Object byte[] $count
            $read = $stream.Read($buffer, 0, $count)
            return @{ Text = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $read); Offset = ($Offset + $read) }
        }
        finally { $stream.Dispose() }
    }
    catch {
        return @{ Text = ''; Offset = $Offset }
    }
}

# Launch PCSX2 with its window hidden. Generic emulators are launched normally
# and skip the PCSX2 log-tailing flow.
$launchArgs = if ($isPcsx2Launch) { $ArgList } else { Get-GenericArgList -InputArgs $ArgList }
if ($launchArgs -and $launchArgs.Count -gt 0) {
    if ($isPcsx2Launch) {
        $proc = Start-Process -FilePath $ExePath -ArgumentList $launchArgs -WindowStyle Hidden -PassThru
    }
    else {
        $proc = Start-Process -FilePath $ExePath -ArgumentList $launchArgs -PassThru
    }
}
else {
    if ($isPcsx2Launch) {
        $proc = Start-Process -FilePath $ExePath -WindowStyle Hidden -PassThru
    }
    else {
        $proc = Start-Process -FilePath $ExePath -PassThru
    }
}
$proc.Id | Out-File -FilePath $PidPath -Encoding ascii -Force

Write-Host ''
if ($isPcsx2Launch) {
    Write-Host "  PCSX2 launched  (PID $($proc.Id))" -ForegroundColor DarkGray
}
else {
    Write-Host "  Emulator launched  (PID $($proc.Id))" -ForegroundColor DarkGray
    return
}

# Wait for PCSX2 to create the emu log.
$deadline = (Get-Date).AddSeconds(20)
while (!(Test-Path -LiteralPath $EmuLogPath) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 100
}
if (!(Test-Path -LiteralPath $EmuLogPath)) {
    Write-Host "  Timed out waiting for PCSX2 emu log." -ForegroundColor Red
    Write-Host "  Expected: $EmuLogPath" -ForegroundColor DarkGray
    return
}

# Tail the emu log into the terminal until PCSX2 exits.
$offset = [long]0
$pending = ''
$dirSeenAt = $null
while ($true) {
    $exited = $proc.HasExited
    $result = Read-Appended -Path $EmuLogPath -Offset $offset
    $offset = $result.Offset

    if ($result.Text.Length -gt 0) {
        $pending += $result.Text
        $cut = $pending.LastIndexOf("`n")
        if ($cut -ge 0) {
            $ready = $pending.Substring(0, $cut)
            $pending = $pending.Substring($cut + 1)
            foreach ($line in ($ready -split "`r?`n")) {
                Process-EmuLine $line
                if (-not $script:dirBlockDone -and $script:dirs.Count -gt 0) { $dirSeenAt = Get-Date }
            }
        }
    }

    # If the listing arrived but nothing has followed it yet, flush it anyway.
    if (-not $script:dirBlockDone -and $script:dirs.Count -gt 0 -and $dirSeenAt -and
        ((Get-Date) - $dirSeenAt).TotalSeconds -gt 2) {
        Show-DirectoryBlock
    }

    if ($exited) {
        if ($pending.Length -gt 0) { Process-EmuLine $pending; $pending = '' }
        Show-DirectoryBlock
        break
    }

    Start-Sleep -Milliseconds 250
}

Write-Host ''
Write-Host "  PCSX2 process exited (PID $($proc.Id))." -ForegroundColor DarkGray
Write-Host ''
