using System;
using CodeDesigner.Mips64LE.Common.Emulator.Hardware;

namespace CodeDesigner.Mips64LE.Common.Emulator.IOP
{
    /// <summary>
    /// Memory bus for the PS2 IOP (I/O Processor).
    ///
    /// Physical memory map (relevant subset):
    ///   0x00000000 – 0x001FFFFF  IOP main RAM (2 MB)
    ///   0x1F800000 – 0x1F8003FF  Scratchpad (1 KB)
    ///   0x1F801070 – 0x1F801077  IOP INTC (I_STAT / I_MASK)
    ///   0x1F801100 – 0x1F801170  IOP Timers 0–2 (counter/mode/target, 0x10 stride)
    ///   0x1D000000 – 0x1D00006F  SIF registers (EE side mapped into IOP address space)
    ///   0x1FC00000 – 0x1FFFFFFF  BIOS ROM (4 MB, shared with EE, read-only from IOP)
    ///
    /// SIF integration:
    ///   The IOP sees the same SIF registers as the EE, albeit with reversed
    ///   semantics on the flag registers (IOP writes to SMFLAG to signal the EE,
    ///   and writes to MSFLAG to acknowledge EE signals).  This bus optionally
    ///   holds a reference to the shared <see cref="SIF"/> instance so that
    ///   reads/writes at 0x1D000000 work across both processors.
    ///
    /// Reference: PCSX2 pcsx2/IopDma.h, pcsx2/IopMem.cpp.
    /// </summary>
    public sealed class IopMemoryBus
    {
        // ── Region sizes ──────────────────────────────────────────────────────
        public const int RamSize       = 2 * 1024 * 1024;  // 2 MB
        public const int ScratchpadSize = 1024;             // 1 KB
        public const int BiosRomSize   = 4 * 1024 * 1024;  // 4 MB

        // ── Physical base addresses ───────────────────────────────────────────
        private const uint RamBase       = 0x00000000u;
        private const uint ScratchpadBase = 0x1F800000u;
        private const uint IntcBase      = 0x1F801070u;
        private const uint TimerBase     = 0x1F801100u;
        private const uint SifMirrorBase = 0x1D000000u;
        private const uint BiosBase      = 0x1FC00000u;

        private readonly byte[] _ram        = new byte[RamSize];
        private readonly byte[] _scratchpad = new byte[ScratchpadSize];
        private readonly byte[] _bios       = new byte[BiosRomSize];

        // IOP INTC registers: I_STAT (offset 0) and I_MASK (offset 4)
        private uint _iStat;
        private uint _iMask;

        // IOP timers (3 simple countdown timers — counter/mode/target)
        private readonly uint[] _timerCount  = new uint[3];
        private readonly uint[] _timerMode   = new uint[3];
        private readonly uint[] _timerTarget = new uint[3];

        /// <summary>Exposes IOP RAM for bulk loading (e.g. from SIF DMA).</summary>
        public byte[] Ram => _ram;

        /// <summary>Exposes BIOS ROM for loading a BIOS image (shared with EE).</summary>
        public byte[] Bios => _bios;

        /// <summary>
        /// Optional reference to the shared SIF instance.  When set, reads/writes
        /// to the SIF mirror region (0x1D000000) are forwarded to this object.
        /// </summary>
        public SIF? SharedSif { get; set; }

        // ── IOP INTC accessors ────────────────────────────────────────────────

        /// <summary>IOP I_STAT register (interrupt status, W1C).</summary>
        public uint IStat => _iStat;

        /// <summary>IOP I_MASK register (interrupt mask, plain R/W).</summary>
        public uint IMask => _iMask;

        /// <summary>True when any interrupt is both pending and enabled.</summary>
        public bool IsInterruptPending => (_iStat & _iMask) != 0;

        /// <summary>Assert an IOP interrupt line (sets the corresponding I_STAT bit).</summary>
        public void RaiseInterrupt(int bit)
        {
            if ((uint)bit < 32u) _iStat |= 1u << bit;
        }

        // ── 32-bit read/write (main interface) ────────────────────────────────

        /// <summary>Read a 32-bit little-endian word from the IOP address space.</summary>
        public uint ReadWord(uint addr)
        {
            // IOP INTC
            if (addr == IntcBase)      return _iStat;
            if (addr == IntcBase + 4u) return _iMask;

            // IOP timers (0x1F801100 – 0x1F80116F)
            if (addr >= TimerBase && addr < TimerBase + 0x70u)
                return ReadTimerWord(addr);

            // SIF mirror
            if (SharedSif != null && addr >= SifMirrorBase && addr < SifMirrorBase + 0x70u)
                return ReadSifMirror(addr - SifMirrorBase);

            var (region, offset) = Map(addr);
            return ReadWordFromRegion(region, offset);
        }

        /// <summary>Write a 32-bit little-endian word to the IOP address space.</summary>
        public void WriteWord(uint addr, uint value)
        {
            // IOP INTC
            if (addr == IntcBase)      { _iStat &= ~value; return; }  // W1C
            if (addr == IntcBase + 4u) { _iMask  = value;  return; }

            // IOP timers
            if (addr >= TimerBase && addr < TimerBase + 0x70u)
            {
                WriteTimerWord(addr, value);
                return;
            }

            // SIF mirror
            if (SharedSif != null && addr >= SifMirrorBase && addr < SifMirrorBase + 0x70u)
            {
                WriteSifMirror(addr - SifMirrorBase, value);
                return;
            }

            var (region, offset, ro) = MapWrite(addr);
            if (!ro) WriteWordToRegion(region, offset, value);
        }

        // ── 8-bit and 16-bit helpers ──────────────────────────────────────────

        public byte ReadByte(uint addr)
        {
            var (region, offset) = Map(addr);
            return region[offset];
        }

        public void WriteByte(uint addr, byte value)
        {
            var (region, offset, ro) = MapWrite(addr);
            if (!ro) region[offset] = value;
        }

        public ushort ReadHalf(uint addr)
            => (ushort)(ReadByte(addr) | (ReadByte(addr + 1) << 8));

        public void WriteHalf(uint addr, ushort value)
        {
            WriteByte(addr,     (byte)value);
            WriteByte(addr + 1, (byte)(value >> 8));
        }

        // ── Timer helpers ─────────────────────────────────────────────────────

        // Each IOP timer occupies 3 × 4-byte slots at TimerBase + timer*0x10 + reg*4
        // reg 0 = counter, reg 1 = mode, reg 2 = target

        private (int idx, int reg) DecodeTimerAddr(uint addr)
        {
            uint off = addr - TimerBase;
            int idx = (int)(off >> 4);            // each timer is 0x10 bytes
            int reg = (int)((off & 0xFu) >> 2);  // register within timer
            return (idx, reg);
        }

        private uint ReadTimerWord(uint addr)
        {
            var (idx, reg) = DecodeTimerAddr(addr);
            if ((uint)idx >= 3u) return 0u;
            return reg switch
            {
                0 => _timerCount[idx],
                1 => _timerMode[idx],
                2 => _timerTarget[idx],
                _ => 0u
            };
        }

        private void WriteTimerWord(uint addr, uint value)
        {
            var (idx, reg) = DecodeTimerAddr(addr);
            if ((uint)idx >= 3u) return;
            switch (reg)
            {
                case 0: _timerCount[idx]  = value; break;
                case 1: _timerMode[idx]   = value; break;
                case 2: _timerTarget[idx] = value; break;
            }
        }

        // ── SIF mirror helpers ────────────────────────────────────────────────
        //
        // The IOP's SIF mirror at 0x1D000000 maps to the same registers as the
        // EE's SIF window at 0x1000F200, but with swapped flag semantics:
        //   IOP offset 0x00 → SMCOM (IOP reads what EE wrote to MSCOM)
        //   IOP offset 0x10 → MSCOM (IOP writes commands the EE reads as SMCOM)
        //   IOP offset 0x20 → SMFLAG (IOP writes to set flags EE sees in SMFLAG)
        //   IOP offset 0x30 → MSFLAG (IOP writes to clear flags it previously set)
        //   IOP offset 0x40 → F240
        //   IOP offset 0x50 → F250
        //   IOP offset 0x60 → F260
        //
        // For simplicity we forward all IOP SIF accesses directly to the shared SIF
        // object using IOP-side semantics.

        private uint ReadSifMirror(uint relOffset)
        {
            // IOP reads from the perspective of the IOP:
            //   IOP:0x00 → same SMCOM the EE writes
            //   IOP:0x10 → same MSCOM the IOP writes
            // Map to EE SIF register offsets (same layout, just swapped COM roles)
            return SharedSif?.ReadRegister(relOffset) ?? 0u;
        }

        private void WriteSifMirror(uint relOffset, uint value)
        {
            SharedSif?.WriteRegister(relOffset, value);
        }

        // ── Region mapping ────────────────────────────────────────────────────

        private (byte[] region, uint offset) Map(uint addr)
        {
            if (addr < RamSize)
                return (_ram, addr);
            if (addr >= ScratchpadBase && addr < ScratchpadBase + ScratchpadSize)
                return (_scratchpad, addr - ScratchpadBase);
            if (addr >= BiosBase && addr < BiosBase + BiosRomSize)
                return (_bios, addr - BiosBase);

            // Unmapped — return a zero slice from scratchpad (read as 0)
            return (_scratchpad, 0u);
        }

        private (byte[] region, uint offset, bool readOnly) MapWrite(uint addr)
        {
            if (addr < RamSize)
                return (_ram, addr, false);
            if (addr >= ScratchpadBase && addr < ScratchpadBase + ScratchpadSize)
                return (_scratchpad, addr - ScratchpadBase, false);
            if (addr >= BiosBase && addr < BiosBase + BiosRomSize)
                return (_bios, 0, true); // ROM: read-only

            return (_scratchpad, 0u, true); // unmapped writes silently ignored
        }

        // ── Low-level helpers ─────────────────────────────────────────────────

        private static uint ReadWordFromRegion(byte[] region, uint offset)
        {
            if (offset + 3 >= (uint)region.Length) return 0u;
            return (uint)(region[offset]
                        | (region[offset + 1] << 8)
                        | (region[offset + 2] << 16)
                        | (region[offset + 3] << 24));
        }

        private static void WriteWordToRegion(byte[] region, uint offset, uint value)
        {
            if (offset + 3 >= (uint)region.Length) return;
            region[offset]     = (byte)value;
            region[offset + 1] = (byte)(value >> 8);
            region[offset + 2] = (byte)(value >> 16);
            region[offset + 3] = (byte)(value >> 24);
        }
    }
}
