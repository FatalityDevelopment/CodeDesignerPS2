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

        // Physical base addresses
        private const uint MainRamBase    = 0x00000000u;
        private const uint BiosRomBase    = 0x1FC00000u;
        private const uint ScratchpadBase = 0x70000000u;
        private const uint IoBase         = 0x10000000u;
        private const uint GsPrivBase     = 0x12000000u;

        // Hardware I/O base addresses (physical)
        private const uint TimerBase  = 0x10000000u;  // T0 base; each timer is 0x800 apart
        private const uint Vif0Base   = 0x10003800u;  // VIF0 registers
        private const uint Vif1Base   = 0x10003C00u;  // VIF1 registers
        private const uint IntcBase   = 0x1000F000u;  // INTC_STAT / INTC_MASK
        private const uint SifBase    = 0x1000F200u;  // SIF registers

        private readonly byte[] _ram        = new byte[MainRamSize];
        private readonly byte[] _bios       = new byte[BiosRomSize];
        private readonly byte[] _scratchpad = new byte[ScratchpadSize];
        private readonly byte[] _io         = new byte[IoRegisterSize];
        private readonly byte[] _gsPriv     = new byte[GsPrivRegSize];

        /// <summary>The four EE hardware timers (T0–T3).</summary>
        public EETimer[] Timers { get; } = { new EETimer(0), new EETimer(1), new EETimer(2), new EETimer(3) };

        /// <summary>The EE interrupt controller.</summary>
        public INTC Intc { get; } = new INTC();

        /// <summary>The EE DMA controller.</summary>
        public DMAC Dmac { get; } = new DMAC();

        /// <summary>The sub-bus interface (EE ↔ IOP communication).</summary>
        public SIF Sif { get; } = new SIF();

        /// <summary>VIF0 — Vector Interface for VU0 (physical base 0x10003800).</summary>
        public VifUnit Vif0 { get; } = new VifUnit(isVif1: false);

        /// <summary>VIF1 — Vector Interface for VU1 (physical base 0x10003C00).</summary>
        public VifUnit Vif1 { get; } = new VifUnit(isVif1: true);

        /// <summary>Exposes main RAM so callers can bulk-load programs.</summary>
        public byte[] Ram => _ram;

        /// <summary>Exposes BIOS ROM so callers can load a BIOS image.</summary>
        public byte[] Bios => _bios;

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
            (byte[] region, uint offset) = MapPhysical(paddr);
            return region[offset];
        }

        public void WriteByte(uint vaddr, byte value)
        {
            uint paddr = TranslateAddress(vaddr);
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
            if (paddr >= Vif0Base && paddr < Vif0Base + 0x200u)
                return Vif0.ReadRegister(paddr - Vif0Base);
            if (paddr >= Vif1Base && paddr < Vif1Base + 0x200u)
                return Vif1.ReadRegister(paddr - Vif1Base);
            if (paddr >= IntcBase && paddr < IntcBase + 0x20u)
                return Intc.ReadRegister(paddr - IntcBase);
            if (paddr >= SifBase && paddr < SifBase + 0x70u)
                return Sif.ReadRegister(paddr - SifBase);

            // Hardware I/O: DMAC channels (0x10008000-0x1000DFFF) and global regs (0x1000E000-0x1000E0FF)
            if (DMAC.TryDecodeChannelAddress(paddr, out int chIdx, out uint chReg))
                return Dmac.Channels[chIdx].ReadRegister(chReg);
            if (DMAC.IsGlobalRegion(paddr))
                return Dmac.ReadGlobal(paddr - 0x1000E000u);

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
            if (paddr >= SifBase && paddr < SifBase + 0x70u)
            {
                Sif.WriteRegister(paddr - SifBase, value);
                return;
            }

            // Hardware I/O: DMAC channel registers — execute transfer if CHCR.STR is set.
            if (DMAC.TryDecodeChannelAddress(paddr, out int chIdx, out uint chReg))
            {
                Dmac.Channels[chIdx].WriteRegister(chReg, value);
                if (chReg == DmacChannel.REG_CHCR && Dmac.Channels[chIdx].IsRunning)
                    Dmac.ExecuteChannel(chIdx, this);
                return;
            }
            if (DMAC.IsGlobalRegion(paddr))
            {
                Dmac.WriteGlobal(paddr - 0x1000E000u, value);
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

        private (byte[] region, uint offset) MapPhysical(uint paddr)
        {
            if (paddr >= ScratchpadBase && paddr < ScratchpadBase + ScratchpadSize)
                return (_scratchpad, paddr - ScratchpadBase);

            if (paddr >= GsPrivBase && paddr < GsPrivBase + GsPrivRegSize)
                return (_gsPriv, paddr - GsPrivBase);

            if (paddr >= IoBase && paddr < IoBase + IoRegisterSize)
                return (_io, paddr - IoBase);

            if (paddr >= BiosRomBase && paddr < BiosRomBase + BiosRomSize)
                return (_bios, paddr - BiosRomBase);

            if (paddr < MainRamSize)
                return (_ram, paddr - MainRamBase);

            // Unmapped address – raise a bus error (a full emulator would
            // route this through the exception handler instead).
            throw new InvalidOperationException($"Unmapped physical address: 0x{paddr:X8}");
        }

        private (byte[] region, uint offset, bool readOnly) MapPhysicalWrite(uint paddr)
        {
            if (paddr >= ScratchpadBase && paddr < ScratchpadBase + ScratchpadSize)
                return (_scratchpad, paddr - ScratchpadBase, false);

            if (paddr >= GsPrivBase && paddr < GsPrivBase + GsPrivRegSize)
                return (_gsPriv, paddr - GsPrivBase, false);

            if (paddr >= IoBase && paddr < IoBase + IoRegisterSize)
                return (_io, paddr - IoBase, false);

            if (paddr >= BiosRomBase && paddr < BiosRomBase + BiosRomSize)
                return (_bios, 0, true); // read-only

            if (paddr < MainRamSize)
                return (_ram, paddr - MainRamBase, false);

            throw new InvalidOperationException($"Unmapped physical address (write): 0x{paddr:X8}");
        }
    }
}
