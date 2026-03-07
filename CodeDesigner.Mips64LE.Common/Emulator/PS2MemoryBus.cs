using System;
using CodeDesigner.Mips64LE.Common.Emulator.Hardware;

namespace CodeDesigner.Mips64LE.Common.Emulator
{
    /// <summary>
    /// PS2 Emotion Engine memory bus.
    /// Maps virtual addresses to the physical regions present in the PS2 hardware,
    /// following the same layout used by PCSX2.
    ///
    /// Physical map (subset relevant to EE):
    ///   0x00000000 - 0x01FFFFFF  Main RAM (32 MB)
    ///   0x1FC00000 - 0x1FFFFFFF  BIOS ROM  (4 MB, read-only)
    ///   0x70000000 - 0x70003FFF  Scratchpad RAM (16 KB)
    ///   0x10000000 - 0x1000FFFF  EE I/O registers
    ///     0x10000000 - 0x10001FFF  EE Timers T0–T3 (0x800 bytes each)
    ///     0x10008000 - 0x1000DFFF  DMAC channel registers (10 channels)
    ///     0x1000E000 - 0x1000E0FF  DMAC global registers (D_CTRL/D_STAT/…)
    ///     0x1000F000 - 0x1000F01F  INTC (interrupt controller)
    ///     0x1000F100 - 0x1000F1FF  SBUS serial UART (reads return 0, writes discarded)
    ///     0x1000F200 - 0x1000F26F  SIF (sub-bus interface)
    ///   0x12000000 - 0x12001FFF  GS privileged registers
    ///
    /// MIPS KSEG0/KSEG1 unmapping (strips top 3 bits when bit31 is set):
    ///   0x80000000 - 0x9FFFFFFF  (KSEG0 cached)   -> physical 0x00000000 - 0x1FFFFFFF
    ///   0xA0000000 - 0xBFFFFFFF  (KSEG1 uncached) -> physical 0x00000000 - 0x1FFFFFFF
    /// </summary>
    public sealed class PS2MemoryBus
    {
        // Region sizes
        public const int MainRamSize       = 32 * 1024 * 1024; // 32 MB
        public const int BiosRomSize       =  4 * 1024 * 1024; //  4 MB
        public const int ScratchpadSize    = 16 * 1024;         // 16 KB
        public const int IoRegisterSize    = 64 * 1024;         // 64 KB
        public const int GsPrivRegSize     =  8 * 1024;         //  8 KB

        /// <summary>
        /// Upper limit of the mirrored main RAM window.
        /// Physical addresses in [0x00000000, MainRamMirrorEnd) are served from the
        /// 32 MB RAM array (with power-of-two wrap-around mirror).  Any address at or
        /// above this threshold falls into the I/O or reserved region.
        /// </summary>
        public const uint MainRamMirrorEnd = 0x10000000u;

        /// <summary>Physical base address of the BIOS ROM window.</summary>
        public const uint BiosRomPhysBase  = 0x1FC00000u;

        // Physical base addresses
        private const uint MainRamBase    = 0x00000000u;
        private const uint BiosRomBase    = 0x1FC00000u;
        private const uint ScratchpadBase = 0x70000000u;
        private const uint IoBase         = 0x10000000u;
        private const uint GsPrivBase     = 0x12000000u;

        // Hardware I/O base addresses (physical)
        private const uint TimerBase  = 0x10000000u;  // T0 base; each timer is 0x800 apart
        private const uint GifBase    = 0x10003000u;  // GIF registers
        private const uint Vif0Base   = 0x10003800u;  // VIF0 registers
        private const uint Vif1Base   = 0x10003C00u;  // VIF1 registers
        private const uint IntcBase   = 0x1000F000u;  // INTC_STAT / INTC_MASK
        private const uint SifBase    = 0x1000F200u;  // SIF registers
        private const uint MchRicmAddr = 0x1000F430u; // RDRAM init control
        private const uint MchDrdAddr  = 0x1000F440u; // RDRAM data register

        // EE SBUS serial UART registers (0x1000F100–0x1000F1FF).
        // These control the EE-side serial transmitter/receiver used by the BIOS for debug output.
        // SBUS_F130 (0x1000F130) is the TX-status register; the BIOS polls it waiting for the
        // TX-busy bit (bits 15:12 == 0x8) to clear before writing each byte to SBUS_F180.
        // We model the serial port as always-ready (TX completes instantly) by returning 0 for
        // all reads in this range.  Writes are discarded so that BIOS initialisation values
        // (which may include a TX-busy bit) do not persist in the _io[] array and cause the
        // poll loop to spin indefinitely.
        // Reference: PCSX2 HwRead.cpp SBUS_F130 = psHu32(SBUS_F130) (0 at reset).
        private const uint SbusSerialBase = 0x1000F100u;
        private const uint SbusSerialEnd  = 0x1000F200u; // exclusive upper bound

        // EE sub-bus mirror of IOP RAM (0x1A000000–0x1BFFFFFF → IOP RAM).
        // The EE kernel polls physical 0x1A000006 for IOP ready flags, and the IOP
        // writes those flags to IOP RAM[6] during its boot sequence.
        // The 2 MB wrap mask reflects the real IOP hardware RAM size (2 MB physical)
        // visible on this EE sub-bus mirror — the extra 2 MB in IopRam are for the
        // IOP BIOS stack frame and are not exposed to the EE over this path.
        // Reference: PCSX2 vtlb.cpp / HwRead.cpp — EE 0x1A000000 sub-bus → psxM.
        private const uint IopRamMirrorBase = 0x1A000000u;
        private const uint IopRamMirrorEnd  = 0x1C000000u; // exclusive upper bound of this window
        private const uint IopRamMask       = 0x001FFFFFu; // 2 MB physical IOP RAM wrap

        // Mechacon disc-type register (shared IOP/EE physical address space).
        // The EE BIOS disc-detection code reads byte at 0x1F803204 via KSEG1
        // (0xBF803204) to determine the disc family inserted in the drive.
        // Reference: PCSX2 pcsx2/IopHw.cpp mechacon register reads.
        private const uint MecanonDiscTypeAddr = 0x1F803204u;

        private readonly byte[] _ram        = new byte[MainRamSize];
        private readonly byte[] _bios       = new byte[BiosRomSize];
        private readonly byte[] _scratchpad = new byte[ScratchpadSize];
        private readonly byte[] _io         = new byte[IoRegisterSize];

        // RDRAM memory controller (MCH) state — emulates PS2 RDRAM init handshake.
        // Reference: PCSX2 Hw.cpp / PS2Tek RDRAM_Init.html
        private uint _mchRicm;          // last MCH_RICM write (bit31 always 0)
        private uint _mchDrd;           // last MCH_DRD write (used for INIT SRP check)
        private int  _rdramSdevid;      // count of RDRAM devices initialised (0–2)
        /// <summary>The four EE hardware timers (T0–T3).</summary>
        public EETimer[] Timers { get; } = { new EETimer(0), new EETimer(1), new EETimer(2), new EETimer(3) };

        /// <summary>The EE interrupt controller.</summary>
        public INTC Intc { get; } = new INTC();

        /// <summary>The EE DMA controller.</summary>
        public DMAC Dmac { get; } = new DMAC();

        /// <summary>The sub-bus interface (EE ↔ IOP communication).</summary>
        public SIF Sif { get; } = new SIF();

        /// <summary>GIF — Graphics Interface (physical base 0x10003000).</summary>
        public GIF Gif { get; } = new GIF();

        /// <summary>VIF0 — Vector Interface for VU0 (physical base 0x10003800).</summary>
        public VifUnit Vif0 { get; } = new VifUnit(isVif1: false);

        /// <summary>VIF1 — Vector Interface for VU1 (physical base 0x10003C00).</summary>
        public VifUnit Vif1 { get; } = new VifUnit(isVif1: true);

        /// <summary>GS — Graphics Synthesizer (privileged registers at 0x12000000).</summary>
        public GS Gs { get; } = new GS();

        /// <summary>
        /// Optional reference to the CDVD drive.  When set, byte reads from the
        /// Mechacon disc-type register (physical 0x1F803204) return the current
        /// disc type so the EE BIOS can detect the inserted disc — the same
        /// register that <see cref="IopMemoryBus"/> exposes to the IOP BIOS.
        /// </summary>
        public IOP.Cdvd? Cdvd { get; set; }

        /// <summary>Exposes main RAM so callers can bulk-load programs.</summary>
        public byte[] Ram => _ram;

        /// <summary>Exposes BIOS ROM so callers can load a BIOS image.</summary>
        public byte[] Bios => _bios;

        /// <summary>
        /// Optional reference to IOP RAM, shared with the IOP bus.
        /// When set, EE physical addresses 0x1A000000–0x1BFFFFFF are transparently
        /// routed to this array (with a 2 MB wrap mask matching real hardware).
        /// This is the EE sub-bus mirror of IOP RAM that the EE kernel uses for
        /// EE↔IOP handshake: e.g. the EE polls physical 0x1A000006 until the IOP
        /// BIOS writes a non-zero ready flag there.
        /// Reference: PCSX2 vtlb.cpp — 0x1C000000 (and 0x1A000000 sub-bus) → psxM.
        /// </summary>
        public byte[]? IopRam { get; set; }

        /// <summary>
        /// Exposes the EE scratchpad RAM (16 KB, physical 0x70000000–0x70003FFF).
        /// Shared with the IOP via <see cref="IopMemoryBus.SharedScratchpad"/> so that
        /// the IOP can use 0x70000000 as its bootstrap stack and IPC area, exactly as
        /// the real PS2 BIOS does during the EE–IOP SIF handshake.
        /// Reference: PCSX2 psxMem.cpp — IOP bus maps 0x70000000 to EE scratchpad.
        /// </summary>
        public byte[] Scratchpad => _scratchpad;

        public PS2MemoryBus()
        {
            // Wire GIF to GS so GIFtag processing forwards register writes.
            Gif.Gs = Gs;
        }

        // -----------------------------------------------------------------------
        // Address translation
        // -----------------------------------------------------------------------

        /// <summary>
        /// Translate a virtual EE address to its physical equivalent.
        /// Handles KSEG0 / KSEG1 windows and the scratchpad mapping.
        /// </summary>
        public static uint TranslateAddress(uint vaddr)
        {
            // Scratchpad is mapped at 0x70000000 in the virtual space and is
            // not covered by the KSEG masks below.
            if (vaddr >= ScratchpadBase && vaddr < ScratchpadBase + ScratchpadSize)
                return vaddr; // treat as identity

            // KSEG0 (0x80000000-0x9FFFFFFF, bits[31:29]=100) and
            // KSEG1 (0xA0000000-0xBFFFFFFF, bits[31:29]=101) both strip the
            // top 3 bits to yield the physical address.
            // Checking bits[31:30] == 10 (i.e. 0x80000000-0xBFFFFFFF) covers both.
            if ((vaddr & 0xC0000000u) == 0x80000000u)
                return vaddr & 0x1FFFFFFFu;

            // Everything else is returned as-is (user-space, KSEG2, etc.)
            return vaddr;
        }

        // -----------------------------------------------------------------------
        // Byte-level read/write (building blocks)
        // -----------------------------------------------------------------------

        public byte ReadByte(uint vaddr)
        {
            uint paddr = TranslateAddress(vaddr);
            // Mechacon disc-type register: EE BIOS disc-detection reads this byte.
            if (paddr == MecanonDiscTypeAddr)
                return Cdvd?.DiscType ?? IOP.Cdvd.DISC_NO_DISC;
            // IOP RAM sub-bus mirror: EE polls IOP RAM[6] for IOP ready flag.
            if (paddr >= IopRamMirrorBase && paddr < IopRamMirrorEnd && IopRam != null)
                return IopRam[(paddr - IopRamMirrorBase) & IopRamMask];
            (byte[] region, uint offset) = MapPhysical(paddr);
            return region[offset];
        }

        public void WriteByte(uint vaddr, byte value)
        {
            uint paddr = TranslateAddress(vaddr);
            // IOP RAM sub-bus mirror: EE writes visible to IOP RAM.
            if (paddr >= IopRamMirrorBase && paddr < IopRamMirrorEnd && IopRam != null)
            {
                IopRam[(paddr - IopRamMirrorBase) & IopRamMask] = value;
                return;
            }
            (byte[] region, uint offset, bool ro) = MapPhysicalWrite(paddr);
            if (!ro) region[offset] = value;
        }

        // -----------------------------------------------------------------------
        // 16-bit little-endian
        // -----------------------------------------------------------------------

        public ushort ReadHalf(uint vaddr)
        {
            return (ushort)(ReadByte(vaddr) | (ReadByte(vaddr + 1) << 8));
        }

        public void WriteHalf(uint vaddr, ushort value)
        {
            WriteByte(vaddr,     (byte)(value));
            WriteByte(vaddr + 1, (byte)(value >> 8));
        }

        // -----------------------------------------------------------------------
        // 32-bit little-endian
        // -----------------------------------------------------------------------

        public uint ReadWord(uint vaddr)
        {
            uint paddr = TranslateAddress(vaddr);

            // Hardware I/O: timers and INTC
            if (paddr >= TimerBase && paddr < TimerBase + 0x2000u)
                return ReadTimerWord(paddr);
            if (paddr >= GifBase && paddr < GifBase + 0x100u)
                return Gif.ReadRegister(paddr - GifBase);
            if (paddr >= Vif0Base && paddr < Vif0Base + 0x200u)
                return Vif0.ReadRegister(paddr - Vif0Base);
            if (paddr >= Vif1Base && paddr < Vif1Base + 0x200u)
                return Vif1.ReadRegister(paddr - Vif1Base);
            if (paddr >= IntcBase && paddr < IntcBase + 0x20u)
                return Intc.ReadRegister(paddr - IntcBase);
            // SBUS serial UART registers (0x1000F100-0x1000F1FF): always return 0.
            // SBUS_F130 is the TX-status register; returning 0 signals "TX ready" so the
            // BIOS poll loop exits immediately instead of spinning on a stale init value.
            if (paddr >= SbusSerialBase && paddr < SbusSerialEnd) return 0u;
            if (paddr >= SifBase && paddr < SifBase + 0x70u)
                return Sif.ReadRegister(paddr - SifBase);

            // MCH RDRAM controller (0x1000F430 / 0x1000F440)
            if (paddr == MchRicmAddr) return 0u;
            if (paddr == MchDrdAddr)  return ReadMchDrd();
            // All other MCH registers (0x1000F400–0x1000F4FF) always read as 0.
            // Notably 0x1000F410 is written with 0x80000000 (busy bit) by the BIOS
            // RDRAM init routine and then polled until bit31 clears.  Returning 0
            // here emulates "operation immediately complete", matching PCSX2 HwRead.cpp.
            if (paddr >= 0x1000F400u && paddr < 0x1000F500u) return 0u;

            // RDRAM configuration readback register (0x1000F520).
            // After the BIOS completes the MCH RDRAM init handshake it polls this
            // register waiting for a known device-type code.  The BIOS table
            // (BIOS ROM 0x9FC43C80) lists accepted values; 0x1201 is the standard
            // 32 MB (2 × 16 MB RDRAM) PS2 configuration.
            // Reference: PCSX2 HwRead.cpp — reads from this range after MCH init.
            if (paddr == 0x1000F520u) return 0x1201u;

            // Hardware I/O: DMAC channels (0x10008000-0x1000DFFF) and global regs (0x1000E000-0x1000E0FF)
            if (DMAC.TryDecodeChannelAddress(paddr, out int chIdx, out uint chReg))
                return Dmac.Channels[chIdx].ReadRegister(chReg);
            if (DMAC.IsGlobalRegion(paddr))
                return Dmac.ReadGlobal(paddr - 0x1000E000u);

            // GS privileged registers (0x12000000–0x12001FFF)
            if (paddr >= GsPrivBase && paddr < GsPrivBase + GsPrivRegSize)
                return Gs.ReadPriv32(paddr - GsPrivBase);

            // IOP RAM sub-bus mirror (0x1A000000–0x1BFFFFFF → IOP RAM).
            if (paddr >= IopRamMirrorBase && paddr < IopRamMirrorEnd && IopRam != null)
            {
                uint off = (paddr - IopRamMirrorBase) & IopRamMask;
                return (uint)(IopRam[off]
                            | (IopRam[off + 1] << 8)
                            | (IopRam[off + 2] << 16)
                            | (IopRam[off + 3] << 24));
            }

            (byte[] region, uint offset) = MapPhysical(paddr);
            // Fast path if all 4 bytes fit in the same region
            return (uint)(region[offset]
                        | (region[offset + 1] << 8)
                        | (region[offset + 2] << 16)
                        | (region[offset + 3] << 24));
        }

        public void WriteWord(uint vaddr, uint value)
        {
            uint paddr = TranslateAddress(vaddr);

            // Hardware I/O: timers and INTC
            if (paddr >= TimerBase && paddr < TimerBase + 0x2000u)
            {
                WriteTimerWord(paddr, value);
                return;
            }
            if (paddr >= GifBase && paddr < GifBase + 0x100u)
            {
                Gif.WriteRegister(paddr - GifBase, value);
                return;
            }
            if (paddr >= Vif0Base && paddr < Vif0Base + 0x200u)
            {
                Vif0.WriteRegister(paddr - Vif0Base, value);
                return;
            }
            if (paddr >= Vif1Base && paddr < Vif1Base + 0x200u)
            {
                Vif1.WriteRegister(paddr - Vif1Base, value);
                return;
            }
            if (paddr >= IntcBase && paddr < IntcBase + 0x20u)
            {
                Intc.WriteRegister(paddr - IntcBase, value);
                return;
            }
            // SBUS serial UART registers (0x1000F100-0x1000F1FF): discard writes.
            // Prevents BIOS serial-port init values (e.g. TX-busy control bits) from
            // persisting in the _io[] array where they would cause the TX-status poll
            // loop (BIOS putchar at SBUS_F130) to spin indefinitely.
            if (paddr >= SbusSerialBase && paddr < SbusSerialEnd) return;
            if (paddr >= SifBase && paddr < SifBase + 0x70u)
            {
                Sif.WriteRegister(paddr - SifBase, value);
                return;
            }

            // MCH RDRAM controller (0x1000F430 / 0x1000F440)
            if (paddr == MchRicmAddr)
            {
                // Clear bit31 (busy flag) before storing; reset sdevid on INIT command (SA=0x21,
                // SOP=1) when SIO-repeater bit (MCH_DRD bit7) is clear — matches PCSX2 HwWrite.cpp.
                if (((value >> 16) & 0xFFFu) == 0x21 && ((value >> 6) & 0xFu) == 1 && ((_mchDrd >> 7) & 1u) == 0)
                    _rdramSdevid = 0;
                _mchRicm = value & ~0x80000000u;
                return;
            }
            if (paddr == MchDrdAddr) { _mchDrd = value; return; } // store for SRP check
            // Writes to other MCH registers (0x1000F400–0x1000F4FF) are silently
            // discarded so the _io[] array never retains a busy-bit value that would
            // cause subsequent reads to spin forever (matches PCSX2 HwWrite.cpp).
            if (paddr >= 0x1000F400u && paddr < 0x1000F500u) return;

            // Hardware I/O: DMAC channel registers — execute transfer if CHCR.STR is set.
            if (DMAC.TryDecodeChannelAddress(paddr, out int chIdx, out uint chReg))
            {
                Dmac.Channels[chIdx].WriteRegister(chReg, value);
                if (chReg == DmacChannel.REG_CHCR && Dmac.Channels[chIdx].IsRunning)
                {
                    // CH5 = SIF0 receive (IOP→EE direction).  Defer execution when the
                    // SIF0 FIFO is empty; DualCpuScheduler will trigger it after IOP CH9
                    // delivers data.  Mirrors the CH10 deferral in IopMemoryBus.
                    bool sif0Empty = chIdx == 5 && Sif.Sif0Count == 0;
                    if (!sif0Empty)
                        Dmac.ExecuteChannel(chIdx, this);
                }
                return;
            }
            if (DMAC.IsGlobalRegion(paddr))
            {
                Dmac.WriteGlobal(paddr - 0x1000E000u, value);
                return;
            }

            // GS privileged registers (0x12000000–0x12001FFF)
            if (paddr >= GsPrivBase && paddr < GsPrivBase + GsPrivRegSize)
            {
                Gs.WritePriv32(paddr - GsPrivBase, value);
                return;
            }

            // IOP RAM sub-bus mirror (0x1A000000–0x1BFFFFFF → IOP RAM).
            if (paddr >= IopRamMirrorBase && paddr < IopRamMirrorEnd && IopRam != null)
            {
                uint off = (paddr - IopRamMirrorBase) & IopRamMask;
                IopRam[off]     = (byte)(value);
                IopRam[off + 1] = (byte)(value >> 8);
                IopRam[off + 2] = (byte)(value >> 16);
                IopRam[off + 3] = (byte)(value >> 24);
                return;
            }

            (byte[] region, uint offset, bool ro) = MapPhysicalWrite(paddr);
            if (!ro)
            {
                region[offset]     = (byte)(value);
                region[offset + 1] = (byte)(value >> 8);
                region[offset + 2] = (byte)(value >> 16);
                region[offset + 3] = (byte)(value >> 24);
            }
        }

        // -----------------------------------------------------------------------
        // MCH (RDRAM memory controller) helper
        // -----------------------------------------------------------------------

        /// <summary>
        /// Emulates MCH_DRD (0x1000F440) reads.
        /// The returned value is derived from the last MCH_RICM command written to 0x1000F430.
        /// When SA=0x21/SOP=0, each of the 2 PS2 RDRAM devices responds with 0x1F in turn.
        /// Reference: PCSX2 Hw.cpp, PS2Tek RDRAM_Init.html
        /// </summary>
        private uint ReadMchDrd()
        {
            uint sop = (_mchRicm >> 6) & 0xFu;
            uint sa  = (_mchRicm >> 16) & 0xFFFu;
            if (sop == 0)
            {
                switch (sa)
                {
                    case 0x21:
                        if (_rdramSdevid < 2) { _rdramSdevid++; return 0x1Fu; }
                        return 0u;
                    case 0x23: return 0x0D0Du;
                    case 0x24: return 0x0090u;
                    case 0x40: return _mchRicm & 0x1Fu;
                }
            }
            return 0u;
        }

        // -----------------------------------------------------------------------
        // 64-bit little-endian
        // -----------------------------------------------------------------------

        public ulong ReadDoubleWord(uint vaddr)
        {
            return ReadWord(vaddr) | ((ulong)ReadWord(vaddr + 4) << 32);
        }

        public void WriteDoubleWord(uint vaddr, ulong value)
        {
            WriteWord(vaddr,     (uint)(value));
            WriteWord(vaddr + 4, (uint)(value >> 32));
        }

        // -----------------------------------------------------------------------
        // 128-bit (quad-word) little-endian
        // -----------------------------------------------------------------------

        public void ReadQuadWord(uint vaddr, out ulong lo, out ulong hi)
        {
            lo = ReadDoubleWord(vaddr);
            hi = ReadDoubleWord(vaddr + 8);
        }

        public void WriteQuadWord(uint vaddr, ulong lo, ulong hi)
        {
            WriteDoubleWord(vaddr,     lo);
            WriteDoubleWord(vaddr + 8, hi);
        }

        // -----------------------------------------------------------------------
        // Hardware I/O helpers (timers and INTC)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Decode a physical address in the timer region (0x10000000–0x10001FFF)
        /// into a timer index (0–3) and per-timer register offset.
        /// Each timer occupies a 0x800-byte window.
        /// </summary>
        private static (int timerIndex, uint regOffset) DecodeTimerAddress(uint paddr)
        {
            uint offset = paddr - TimerBase;
            int  index  = (int)(offset >> 11); // divide by 0x800
            uint reg    = offset & 0x30u;       // bits [5:4] select register within timer window
            return (index, reg);
        }

        private uint ReadTimerWord(uint paddr)
        {
            var (idx, reg) = DecodeTimerAddress(paddr);
            if ((uint)idx >= (uint)Timers.Length) return 0u;
            return Timers[idx].ReadRegister(reg);
        }

        private void WriteTimerWord(uint paddr, uint value)
        {
            var (idx, reg) = DecodeTimerAddress(paddr);
            if ((uint)idx >= (uint)Timers.Length) return;
            Timers[idx].WriteRegister(reg, value);
        }

        // -----------------------------------------------------------------------
        // Private helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Small zero-filled dummy buffer returned for reads from reserved or
        /// unmapped physical regions.  Writes routed here are discarded (readOnly).
        /// This matches PCSX2 VTLB behaviour:
        ///   [0x14000000–0x1FC00000) → reserved (ignored writes, arbitrary reads)
        ///   [0x20000000+)           → should be BUS ERROR; we silently return 0
        ///                             until a COP0 exception path is wired up.
        /// </summary>
        private static readonly byte[] _unmappedDummy = new byte[16];

        private (byte[] region, uint offset) MapPhysical(uint paddr)
        {
            if (paddr >= ScratchpadBase && paddr < ScratchpadBase + ScratchpadSize)
                return (_scratchpad, paddr - ScratchpadBase);

            if (paddr >= IoBase && paddr < IoBase + IoRegisterSize)
                return (_io, paddr - IoBase);

            if (paddr >= BiosRomBase && paddr < BiosRomBase + BiosRomSize)
                return (_bios, paddr - BiosRomBase);

            // Main RAM: 32 MB at physical 0x00000000.  The EE bus mirrors this
            // across the full 256 MB [0x00000000–0x0FFFFFFF) RAM window
            // (PCSX2 vtlb.cpp: "[0000 0000,1000 0000) -> Ram (mirrored ?)").
            if (paddr < 0x10000000u)
                return (_ram, paddr & (uint)(MainRamSize - 1));

            // Reserved / unmapped physical region.
            // PCSX2 defines [0x14000000–0x1FC00000) as reserved (ignored writes,
            // arbitrary reads) and [0x20000000+) as BUS ERROR territory.
            // Return a zero-filled dummy buffer so games probing unimplemented
            // hardware registers do not crash the emulator.
            return (_unmappedDummy, 0);
        }

        private (byte[] region, uint offset, bool readOnly) MapPhysicalWrite(uint paddr)
        {
            if (paddr >= ScratchpadBase && paddr < ScratchpadBase + ScratchpadSize)
                return (_scratchpad, paddr - ScratchpadBase, false);

            if (paddr >= IoBase && paddr < IoBase + IoRegisterSize)
                return (_io, paddr - IoBase, false);

            if (paddr >= BiosRomBase && paddr < BiosRomBase + BiosRomSize)
                return (_bios, 0, true); // read-only

            // Main RAM with mirroring (matches PCSX2 RAM window).
            if (paddr < 0x10000000u)
                return (_ram, paddr & (uint)(MainRamSize - 1), false);

            // Reserved / unmapped – silently discard the write (matches PCSX2 behaviour).
            return (_unmappedDummy, 0, true);
        }
    }
}
