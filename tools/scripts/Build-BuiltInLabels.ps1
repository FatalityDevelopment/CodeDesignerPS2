# Regenerates CodeDesigner.Ps2.LabelManager/Resources/BuiltIn.json
# from PCSX2's Hw.h + IopHw.h (hardware register names) plus a hand-curated
# header table (memory regions, exception vectors, BIOS entry points that
# don't live in any PCSX2 enum).
#
# Run from the repo root:
#   pwsh tools/scripts/Build-BuiltInLabels.ps1

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$Target   = $null
)

$ErrorActionPreference = 'Stop'

if (-not $Target) { $Target = Join-Path $RepoRoot 'CodeDesigner.Ps2.LabelManager/Resources/BuiltIn.json' }
$HwH    = Join-Path $RepoRoot 'pcsx2/pcsx2/Hw.h'
$IopHwH = Join-Path $RepoRoot 'pcsx2/pcsx2/IopHw.h'

if (-not (Test-Path $HwH))    { throw "Source file not found: $HwH" }
if (-not (Test-Path $IopHwH)) { throw "Source file not found: $IopHwH" }

# ── Helpers ────────────────────────────────────────────────────────────────

function Mine-RegisterEntries {
    param([string]$Path, [string]$DefaultComment)
    $content = Get-Content -LiteralPath $Path -Raw

    # Strip C++ //... comments so trailing notes on each line don't bleed in.
    $stripped = [Regex]::Replace($content, '//[^\r\n]*', '')

    $entries = New-Object System.Collections.Generic.List[object]
    # Match either `NAME = 0x1xxxxxxx,` or `#define NAME 0x1xxxxxxx`.
    $rx = [Regex]'(?im)(?:^\s*|\W)(?:#define\s+|\b)([A-Za-z_][A-Za-z0-9_]*)\s*(?:=|\s)\s*(0x1[0-9A-Fa-f]{6,7})\b'
    foreach ($m in $rx.Matches($stripped)) {
        $name = $m.Groups[1].Value
        $addr = $m.Groups[2].Value
        # Skip enum-marker pairs like *_START / *_END which are bounds, not regs.
        if ($name -match '_(START|END|BASE_BEGIN)$') { continue }
        # Strip HW_ prefix on IOP names so the result reads consistently.
        $cleanName = $name -replace '^(HW_|HWx_)', 'IOP_'
        $cleanName = $cleanName -replace '^IOP_(?=PS1|SIO|SSBUS|RAM|ISTAT|IMASK|ICTRL|ICFG|DEV9|DMA|CDR|FW|USB|SPU|RCNT|IREG)', 'IOP_'
        # If we transformed away the prefix entirely, drop the leading IOP_ that's redundant.
        if ($Path -like '*IopHw.h*' -and $cleanName -notlike 'IOP_*') { $cleanName = 'IOP_' + $cleanName }
        $entries.Add([pscustomobject]@{
            AddressHex = ([uint32]$addr)
            Name       = $cleanName
            Comment    = $DefaultComment
        })
    }
    return $entries
}

# ── Curated header (memory regions, vectors, BIOS entries) ─────────────────
# These don't appear in any PCSX2 hw-register enum, so we keep a hand-written
# list. Same data as the old BuiltIn.json but explicitly tagged.

$curated = @(
    # EE physical memory map
    @{Addr='0x00000000'; Name='EE_RAM_BASE';        Comment='EE RAM base (physical)';                                Section='EE Memory'}
    @{Addr='0x00008000'; Name='EE_KERNEL_BASE';     Comment='EE kernel / OS loaded here by BIOS';                    Section='EE Memory'}
    @{Addr='0x01FFFFFF'; Name='EE_RAM_END';         Comment='EE RAM top (32 MB, physical)';                          Section='EE Memory'}
    @{Addr='0x20000000'; Name='EE_RAM_UNCACHED';    Comment='EE uncached direct-mapped window for Main RAM';         Section='EE Memory'}
    @{Addr='0x30000000'; Name='EE_RAM_ACCEL';       Comment='EE uncached + accelerated mirror for Main RAM';         Section='EE Memory'}

    @{Addr='0x70000000'; Name='EE_SCRATCHPAD';      Comment='EE Scratchpad RAM (16 KB)';                             Section='EE Memory'}
    @{Addr='0x70003FFF'; Name='EE_SCRATCHPAD_END';  Comment='EE Scratchpad RAM end';                                 Section='EE Memory'}

    # IOP RAM mirrors into EE space
    @{Addr='0x1A000000'; Name='EE_IOP_RAM_MIRROR';  Comment='EE physical mirror of IOP RAM (2 MB wrap)';             Section='EE Memory'}
    @{Addr='0x1C000000'; Name='EE_IOP_VTLB_MIRROR'; Comment='EE VTLB mirror of IOP RAM (PCSX2 vtlb.cpp)';             Section='EE Memory'}

    # Exception vectors
    @{Addr='0x80000000'; Name='EE_TLB_REFILL_BEV0'; Comment='EE TLB refill exception (Status.BEV=0)';                Section='EE Exception Vectors'}
    @{Addr='0x80000080'; Name='EE_EXCEPTION_BEV0';  Comment='EE general exception vector (Status.BEV=0)';            Section='EE Exception Vectors'}
    @{Addr='0x80000100'; Name='EE_PERFCNT_BEV0';    Comment='EE performance-counter overflow exception (BEV=0)';     Section='EE Exception Vectors'}
    @{Addr='0x80000180'; Name='EE_DEBUG_BEV0';      Comment='EE debug exception vector (BEV=0)';                     Section='EE Exception Vectors'}
    @{Addr='0xBFC00000'; Name='BIOS_RESET';         Comment='Reset / NMI vector (KSEG1 mirror of BIOS ROM)';          Section='EE Exception Vectors'}
    @{Addr='0xBFC00100'; Name='BIOS_TLB_REFILL_BEV1';Comment='TLB refill exception (Status.BEV=1)';                  Section='EE Exception Vectors'}
    @{Addr='0xBFC00180'; Name='BIOS_EXCEPTION_BEV1';Comment='General exception vector (Status.BEV=1)';               Section='EE Exception Vectors'}
    @{Addr='0xBFC00200'; Name='BIOS_PERFCNT_BEV1';  Comment='Performance-counter overflow (Status.BEV=1)';           Section='EE Exception Vectors'}

    # BIOS code anchor points (loosely BIOS-version-dependent but useful)
    @{Addr='0x80010000'; Name='EE_BIOS_ENTRY';      Comment='BIOS code entry point (KSEG0 mirror)';                  Section='EE BIOS'}
    @{Addr='0x80010018'; Name='EE_BIOS_PUTS';       Comment='BIOS: null-terminated string print (fast-forwarded)';    Section='EE BIOS'}
    @{Addr='0x800108B0'; Name='EE_BIOS_FILL_LOOP';  Comment='BIOS: BNEL byte-fill loop (fast-forwarded)';            Section='EE BIOS'}
    @{Addr='0x800109FC'; Name='EE_MODULE_INSTALL';  Comment='BIOS: outer module-install loop';                       Section='EE BIOS'}
    @{Addr='0x80012978'; Name='EE_VU0_READY_POLL';  Comment='BIOS: VU0-ready poll loop';                             Section='EE BIOS'}

    # ROM regions
    @{Addr='0x1FC00000'; Name='BIOS_ROM_BASE';      Comment='BIOS ROM (ROM0) base - 4 MB';                            Section='BIOS ROM'}
    @{Addr='0x1FFFFFFF'; Name='BIOS_ROM_END';       Comment='BIOS ROM (ROM0) end';                                    Section='BIOS ROM'}
    @{Addr='0x1E000000'; Name='ROM1_BASE';          Comment='ROM1 (DVD player firmware) - 4 MB';                       Section='BIOS ROM'}
    @{Addr='0x1E3FFFFF'; Name='ROM1_END';           Comment='ROM1 end';                                                Section='BIOS ROM'}
    @{Addr='0x1E400000'; Name='ROM2_BASE';          Comment='ROM2 (Chinese-region BIOS extension) - 4 MB';             Section='BIOS ROM'}
    @{Addr='0x1E7FFFFF'; Name='ROM2_END';           Comment='ROM2 end';                                                Section='BIOS ROM'}

    # IOP-side memory map
    @{Addr='0x1F800000'; Name='IOP_SCRATCH';        Comment='IOP scratch pad (Dcache mode)';                          Section='IOP Memory'}
    @{Addr='0x1D000000'; Name='SIF_IOP_MIRROR_BASE';Comment='SIF IOP-side register mirror base (SIF_F260 default)';   Section='IOP Memory'}
    @{Addr='0x1D000020'; Name='IOP_SIF_MSCOM';      Comment='IOP-side SIF: MSCOM mirror';                             Section='IOP Memory'}
    @{Addr='0x1D000030'; Name='IOP_SIF_SMCOM';      Comment='IOP-side SIF: SMCOM mirror';                             Section='IOP Memory'}
    @{Addr='0x1D000040'; Name='IOP_SIF_MSFLAG';     Comment='IOP-side SIF: MSFLAG mirror';                            Section='IOP Memory'}
    @{Addr='0x1D000060'; Name='IOP_SIF_F240';       Comment='IOP-side SIF: F240 status mirror';                       Section='IOP Memory'}

    # MCH
    @{Addr='0x1000F430'; Name='MCH_RICM';           Comment='MCH RDRAM init command';                                 Section='EE MCH'}
    @{Addr='0x1000F440'; Name='MCH_DRD';            Comment='MCH RDRAM data read';                                    Section='EE MCH'}
    @{Addr='0x1000F520'; Name='MCH_RDRAM_CFG';      Comment='MCH RDRAM config readback (returns 0x1201)';             Section='EE MCH'}

    # CDVD command block (0x1F402000-0x1F402FFF) - PCSX2 names from CDVD.cpp
    @{Addr='0x1F402004'; Name='CDVD_NCMD_STATUS';   Comment='CDVD - NCmd status (bit 7 = busy)';                       Section='IOP CDVD'}
    @{Addr='0x1F402005'; Name='CDVD_ERROR';         Comment='CDVD - error code';                                       Section='IOP CDVD'}
    @{Addr='0x1F402006'; Name='CDVD_HWINFO';        Comment='CDVD - hardware info / interrupt reason';                 Section='IOP CDVD'}
    @{Addr='0x1F402007'; Name='CDVD_NCMD_DATA';     Comment='CDVD - NCmd parameter FIFO';                              Section='IOP CDVD'}
    @{Addr='0x1F402008'; Name='CDVD_DRIVE_STATUS';  Comment='CDVD - drive status (PAUSED/SEEKING/READING/...)';          Section='IOP CDVD'}
    @{Addr='0x1F402009'; Name='CDVD_TRAY_STATUS';   Comment='CDVD - tray status (bit0=closed, bit1=disc-changed)';     Section='IOP CDVD'}
    @{Addr='0x1F40200A'; Name='CDVD_CURRENT_LBA';   Comment='CDVD - current sector address (LBA)';                     Section='IOP CDVD'}
    @{Addr='0x1F402013'; Name='CDVD_DISC_TYPE';     Comment='CDVD - detected disc type (NONE/CD/DVD/...)';               Section='IOP CDVD'}
    @{Addr='0x1F402014'; Name='CDVD_DRIVE_MODE';    Comment='CDVD - drive mode (read speed, single/double)';            Section='IOP CDVD'}
    @{Addr='0x1F402015'; Name='CDVD_RDY_STATUS';    Comment='CDVD - ready-status bits';                                Section='IOP CDVD'}
    @{Addr='0x1F402016'; Name='CDVD_SCMD';          Comment='CDVD - SCmd command register';                            Section='IOP CDVD'}
    @{Addr='0x1F402017'; Name='CDVD_SCMD_STATUS';   Comment='CDVD - SCmd status (bit 7 = busy)';                       Section='IOP CDVD'}
    @{Addr='0x1F402018'; Name='CDVD_SCMD_DATA';     Comment='CDVD - SCmd data port';                                   Section='IOP CDVD'}
    @{Addr='0x1F402000'; Name='CDVD_NCMD';          Comment='CDVD - NCmd command register (NCMD_READ_CD=0x06)';        Section='IOP CDVD'}

    # IOP POST register - used by IRX bootstrap to signal boot stage
    @{Addr='0x1F802070'; Name='IOP_POST';           Comment='IOP POST byte (debug 7-seg / boot-stage indicator)';      Section='IOP misc'}
)

# ── Read PCSX2 hw registers ────────────────────────────────────────────────

function Strip-Comments([string]$s) { [Regex]::Replace($s, '//[^\r\n]*', '') }

function Section-For([string]$name, [string]$addrHex) {
    $a = [uint32]$addrHex
    # Order matters: most-specific patterns first.
    if ($name -match '^RCNT')                       { return 'EE Timers' }
    if ($name -match '^IPU_')                       { return 'EE IPU' }
    if ($name -match '^GIF_')                       { return 'EE GIF' }
    if ($name -match '^VIF[01]_')                   { return ($name.Substring(0,4) + ' control') }
    if ($name -match '^D_|^DMA_CH|^SQWC|^STADR|^RBSR|^RBOR') { return 'EE DMAC' }
    if ($name -match '^INTC_')                      { return 'EE INTC' }
    if ($name -match '^SBUS_')                      { return 'EE SBUS' }
    if ($name -match '^SIF_')                       { return 'EE SIF' }
    if ($name -match '^GS_')                        { return 'GS privileged regs' }
    if ($a -ge 0x12000000 -and $a -lt 0x13000000)   { return 'GS privileged regs' }
    if ($name -match '^IOP_DMA')                    { return 'IOP DMAC' }
    if ($name -match '^IOP_T\d')                    { return 'IOP Timers' }
    if ($name -match '^IOP_SIO2')                   { return 'IOP SIO2' }
    if ($name -match '^IOP_SIO_')                   { return 'IOP SIO0' }
    if ($name -match '^IOP_SSBUS')                  { return 'IOP SSBUS' }
    if ($name -match '^IOP_I(STAT|MASK|CTRL|CFG)')  { return 'IOP INTC' }
    if ($name -match '^IOP_USB|HW_USB')             { return 'IOP USB' }
    if ($name -match '^IOP_FW|HW_FW')               { return 'IOP FireWire' }
    if ($name -match '^IOP_SPU')                    { return 'IOP SPU2' }
    if ($name -match '^IOP_DEV9')                   { return 'IOP DEV9' }
    if ($name -match '^IOP_CDR')                    { return 'IOP CD-ROM (PS1)' }
    if ($name -match '^IOP_PS1_GPU')                { return 'IOP PS1 GPU' }
    if ($name -match '^IOP_RAM_SIZE')               { return 'IOP Memory' }
    if ($a -ge 0x1F402000 -and $a -lt 0x1F403000)   { return 'IOP CDVD' }
    return 'IOP misc'
}

$commentOverrides = @{
    # EE DMAC channels - give the channel a memorable name (PCSX2 enum is just D0/D1/...)
    'D0_CHCR' = 'EE DMAC CH0 (VIF0) - channel control';        'D0_MADR' = 'EE DMAC CH0 (VIF0) - memory address'
    'D0_QWC'  = 'EE DMAC CH0 (VIF0) - quadword count';         'D0_TADR' = 'EE DMAC CH0 (VIF0) - tag address'
    'D0_ASR0' = 'EE DMAC CH0 (VIF0) - alt source 0';           'D0_ASR1' = 'EE DMAC CH0 (VIF0) - alt source 1'
    'D1_CHCR' = 'EE DMAC CH1 (VIF1) - channel control';        'D1_MADR' = 'EE DMAC CH1 (VIF1) - memory address'
    'D1_QWC'  = 'EE DMAC CH1 (VIF1) - quadword count';         'D1_TADR' = 'EE DMAC CH1 (VIF1) - tag address'
    'D1_ASR0' = 'EE DMAC CH1 (VIF1) - alt source 0';           'D1_ASR1' = 'EE DMAC CH1 (VIF1) - alt source 1'
    'D2_CHCR' = 'EE DMAC CH2 (GIF) - channel control';         'D2_MADR' = 'EE DMAC CH2 (GIF) - memory address'
    'D2_QWC'  = 'EE DMAC CH2 (GIF) - quadword count';          'D2_TADR' = 'EE DMAC CH2 (GIF) - tag address'
    'D2_ASR0' = 'EE DMAC CH2 (GIF) - alt source 0';            'D2_ASR1' = 'EE DMAC CH2 (GIF) - alt source 1'
    'D3_CHCR' = 'EE DMAC CH3 (IPU_FROM) - channel control';    'D3_MADR' = 'EE DMAC CH3 (IPU_FROM) - memory address'
    'D3_QWC'  = 'EE DMAC CH3 (IPU_FROM) - quadword count'
    'D4_CHCR' = 'EE DMAC CH4 (IPU_TO) - channel control';      'D4_MADR' = 'EE DMAC CH4 (IPU_TO) - memory address'
    'D4_QWC'  = 'EE DMAC CH4 (IPU_TO) - quadword count';       'D4_TADR' = 'EE DMAC CH4 (IPU_TO) - tag address'
    'D5_CHCR' = 'EE DMAC CH5 (SIF0) - channel control';        'D5_MADR' = 'EE DMAC CH5 (SIF0) - memory address'
    'D5_QWC'  = 'EE DMAC CH5 (SIF0) - quadword count'
    'D6_CHCR' = 'EE DMAC CH6 (SIF1) - channel control';        'D6_MADR' = 'EE DMAC CH6 (SIF1) - memory address'
    'D6_QWC'  = 'EE DMAC CH6 (SIF1) - quadword count';         'D6_TADR' = 'EE DMAC CH6 (SIF1) - tag address'
    'D7_CHCR' = 'EE DMAC CH7 (SIF2) - channel control';        'D7_MADR' = 'EE DMAC CH7 (SIF2) - memory address'
    'D7_QWC'  = 'EE DMAC CH7 (SIF2) - quadword count'
    'D8_CHCR' = 'EE DMAC CH8 (SPR_FROM) - channel control';    'D8_MADR' = 'EE DMAC CH8 (SPR_FROM) - memory address'
    'D8_QWC'  = 'EE DMAC CH8 (SPR_FROM) - quadword count';     'D8_SADR' = 'EE DMAC CH8 (SPR_FROM) - SPR address'
    'D9_CHCR' = 'EE DMAC CH9 (SPR_TO) - channel control';      'D9_MADR' = 'EE DMAC CH9 (SPR_TO) - memory address'
    'D9_QWC'  = 'EE DMAC CH9 (SPR_TO) - quadword count';       'D9_TADR' = 'EE DMAC CH9 (SPR_TO) - tag address'
    'D9_SADR' = 'EE DMAC CH9 (SPR_TO) - SPR address'
    # EE DMAC globals
    'D_CTRL'   = 'EE DMAC - global control'
    'D_STAT'   = 'EE DMAC - global status (channel IRQ status/mask)'
    'D_PCR'    = 'EE DMAC - priority control'
    'D_SQWC'   = 'EE DMAC - skip QWC'
    'D_RBSR'   = 'EE DMAC - ring-buffer size'
    'D_RBOR'   = 'EE DMAC - ring-buffer offset'
    'D_STADR'  = 'EE DMAC - stall address'
    # EE INTC
    'INTC_STAT' = 'EE INTC - interrupt status (write 1 to clear)'
    'INTC_MASK' = 'EE INTC - interrupt mask (write 1 to toggle)'
    # EE SIF
    'SIF_MSCOM'  = 'SIF EE->IOP command register'
    'SIF_SMCOM'  = 'SIF IOP->EE command register'
    'SIF_MSFLAG' = 'SIF EE->IOP flag (bit 16 = SIFMAN start)'
    'SIF_SMFLAG' = 'SIF IOP->EE flag (bit 30 = handshake done)'
    # EE Timers
    'RCNT0_COUNT' = 'EE Timer 0 - counter (alias T0)';      'RCNT0_MODE'  = 'EE Timer 0 - mode (EQUF/OVFF flags)'
    'RCNT0_TARGET'= 'EE Timer 0 - compare target';          'RCNT0_HOLD'  = 'EE Timer 0 - hold (latched value)'
    'RCNT1_COUNT' = 'EE Timer 1 - counter (alias T1)';      'RCNT1_MODE'  = 'EE Timer 1 - mode'
    'RCNT1_TARGET'= 'EE Timer 1 - compare target';          'RCNT1_HOLD'  = 'EE Timer 1 - hold'
    'RCNT2_COUNT' = 'EE Timer 2 - counter (alias T2)';      'RCNT2_MODE'  = 'EE Timer 2 - mode'
    'RCNT2_TARGET'= 'EE Timer 2 - compare target'
    'RCNT3_COUNT' = 'EE Timer 3 - counter (alias T3)';      'RCNT3_MODE'  = 'EE Timer 3 - mode'
    'RCNT3_TARGET'= 'EE Timer 3 - compare target'
    # EE GS priv
    'GS_PMODE'    = 'GS - PMODE (output circuit selector)'
    'GS_SMODE1'   = 'GS - SMODE1 (sync mode 1)'
    'GS_SMODE2'   = 'GS - SMODE2 (sync mode 2)'
    'GS_SRFSH'    = 'GS - SRFSH (SDRAM refresh)'
    'GS_SYNCH1'   = 'GS - SYNCH1 (horizontal sync)'
    'GS_SYNCH2'   = 'GS - SYNCH2'
    'GS_SYNCV'    = 'GS - SYNCV (vertical sync)'
    'GS_DISPFB1'  = 'GS - DISPFB1 (display buffer 1)'
    'GS_DISPLAY1' = 'GS - DISPLAY1 (output circuit 1)'
    'GS_DISPFB2'  = 'GS - DISPFB2 (display buffer 2)'
    'GS_DISPLAY2' = 'GS - DISPLAY2 (output circuit 2)'
    'GS_EXTBUF'   = 'GS - EXTBUF (external input buffer)'
    'GS_EXTDATA'  = 'GS - EXTDATA'
    'GS_EXTWRITE' = 'GS - EXTWRITE'
    'GS_BGCOLOR'  = 'GS - BGCOLOR (background color)'
    'GS_CSR'      = 'GS - CSR (status / signal / finish / interrupt)'
    'GS_IMR'      = 'GS - IMR (interrupt mask)'
    'GS_BUSDIR'   = 'GS - BUSDIR (local bus direction)'
    'GS_SIGLBLID' = 'GS - SIGLBLID (signal/label ID)'
}

function Comment-For([string]$name) {
    if ($commentOverrides.ContainsKey($name)) { return $commentOverrides[$name] }
    if ($name -match '^(\w+?)_(\w+)$') {
        return ("{0} - {1}" -f $matches[1], $matches[2])
    }
    return $name
}

$rxLine = [Regex]'(?im)^\s*(?:#define\s+|)([A-Za-z_][A-Za-z0-9_]*)\s*(?:=|\s+)\s*(0x1[0-9A-Fa-f]{7})\b'

$mined = New-Object System.Collections.Generic.List[object]
foreach ($file in @($HwH, $IopHwH)) {
    $isIop = $file -like '*IopHw.h*'
    $stripped = Strip-Comments (Get-Content -LiteralPath $file -Raw)
    foreach ($m in $rxLine.Matches($stripped)) {
        $name = $m.Groups[1].Value
        $addr = $m.Groups[2].Value
        # Skip bounds-only constants
        if ($name -match '_(START|END)$') { continue }
        if ($name -match '^MAX_|^NUM_')   { continue }
        # Drop the HW_/HWx_ prefix on IOP names for legibility, then re-add IOP_
        # so it's clear which CPU's bus the register lives on.
        $clean = $name
        if ($isIop) {
            $clean = ($name -replace '^HWx?_', '')
            if ($clean -notlike 'IOP_*') { $clean = 'IOP_' + $clean }
        }
        $mined.Add([pscustomobject]@{
            Addr    = $addr
            Name    = $clean
            Section = (Section-For $clean $addr)
            Comment = (Comment-For $clean)
        })
    }
}

# Merge curated + mined; dedupe by address (curated wins).
$byAddr = [ordered]@{}
foreach ($c in $curated) {
    $byAddr[$c.Addr.ToLower()] = [pscustomobject]@{
        Addr = $c.Addr; Name = $c.Name; Section = $c.Section; Comment = $c.Comment
    }
}
foreach ($m in $mined) {
    $k = $m.Addr.ToLower()
    if (-not $byAddr.Contains($k)) {
        $byAddr[$k] = $m
    }
}

# Sort by address.
$all = $byAddr.Values | Sort-Object { [uint32]$_.Addr }

# Group by section in section order.
$sectionOrder = @(
    'EE Memory', 'EE Exception Vectors', 'EE BIOS', 'BIOS ROM',
    'EE Timers', 'EE IPU', 'EE GIF', 'VIF0 control', 'VIF1 control',
    'EE DMAC', 'EE INTC', 'EE SBUS', 'EE SIF', 'EE MCH',
    'GS privileged regs',
    'IOP Memory',
    'IOP SSBUS', 'IOP SIO0', 'IOP INTC', 'IOP DMAC', 'IOP Timers',
    'IOP USB', 'IOP FireWire', 'IOP SPU2', 'IOP DEV9', 'IOP CD-ROM (PS1)',
    'IOP PS1 GPU', 'IOP CDVD', 'IOP SIO2', 'IOP misc'
)

$entries = New-Object System.Collections.Generic.List[object]
$inserted = New-Object System.Collections.Generic.HashSet[string]
foreach ($sec in $sectionOrder) {
    $group = @($all | Where-Object { $_.Section -eq $sec })
    if ($group.Count -eq 0) { continue }
    foreach ($e in $group) {
        if ($inserted.Add($e.Addr.ToLower())) {
            $entries.Add([ordered]@{
                address = $e.Addr
                name    = $e.Name
                comment = $e.Comment
                type    = 'Address'
            })
        }
    }
}
# Anything not yet inserted (unknown section) goes at the end.
foreach ($e in $all) {
    if ($inserted.Add($e.Addr.ToLower())) {
        $entries.Add([ordered]@{
            address = $e.Addr
            name    = $e.Name
            comment = $e.Comment
            type    = 'Address'
        })
    }
}

$payload = [ordered]@{
    setName   = 'Built-in'
    isBuiltIn = $true
    isSystem  = $true
    entries   = $entries
}

$json = $payload | ConvertTo-Json -Depth 6
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($Target, $json, $utf8NoBom)

Write-Host "Wrote $Target"
Write-Host ("  {0} labels across {1} sections" -f $entries.Count, $sectionOrder.Count)
