using System;

namespace CodeDesigner.Ps2.EmulatorCore.Hardware
{
    /// <summary>
    /// Emulates the PS2 EE Interrupt Controller (INTC).
    ///
    /// The INTC routes interrupt requests from EE peripherals (timers, VBlank,
    /// VIF, DMA, etc.) to the EE CPU.  Software polls or handles interrupts by
    /// reading/writing two 32-bit registers:
    ///
    ///   0x1000F000  INTC_STAT — interrupt status register
    ///                 • A bit is set (1) when the corresponding interrupt fires.
    ///                 • Writing a 1 to a bit <em>clears</em> that pending bit
    ///                   (write-1-to-clear semantics, matching PS2 hardware).
    ///
    ///   0x1000F010  INTC_MASK — interrupt mask register
    ///                 • A bit being 1 <em>enables</em> the interrupt line.
    ///                 • Writing a 1 to a bit <em>toggles</em> that mask bit
    ///                   (write-1-to-toggle semantics, matching PS2 hardware).
    ///
    /// Interrupt bit assignments (INTC_STAT / INTC_MASK):
    ///   Bit  0  GS
    ///   Bit  1  SBUS
    ///   Bit  2  VBlank-start
    ///   Bit  3  VBlank-end
    ///   Bit  4  VIF0
    ///   Bit  5  VIF1
    ///   Bit  6  VU0
    ///   Bit  7  VU1
    ///   Bit  8  IPU
    ///   Bit  9  Timer 0
    ///   Bit 10  Timer 1
    ///   Bit 11  Timer 2
    ///   Bit 12  Timer 3
    ///   Bit 13  SFIFO
    ///   Bit 14  VU0 Watchdog
    ///
    /// Reference: PCSX2 pcsx2/Gif.h, pcsx2/R5900.cpp INTC_* defines.
    /// </summary>
    public sealed class INTC
    {
        // ── Register offsets (within the INTC's I/O window) ──────────────────
        public const uint REG_STAT = 0x0000u; // 0x1000F000
        public const uint REG_MASK = 0x0010u; // 0x1000F010

        // ── Known interrupt bit indices ───────────────────────────────────────
        public const int INT_GS         = 0;
        public const int INT_SBUS       = 1;
        public const int INT_VBLANK_S   = 2;
        public const int INT_VBLANK_E   = 3;
        public const int INT_VIF0       = 4;
        public const int INT_VIF1       = 5;
        public const int INT_VU0        = 6;
        public const int INT_VU1        = 7;
        public const int INT_IPU        = 8;
        public const int INT_TIMER0     = 9;
        public const int INT_TIMER1     = 10;
        public const int INT_TIMER2     = 11;
        public const int INT_TIMER3     = 12;
        public const int INT_SFIFO      = 13;
        public const int INT_VU0_WD     = 14;

        private uint _stat;
        private uint _mask;

        /// <summary>Raw INTC_STAT register value (pending interrupts).</summary>
        public uint Stat => _stat;

        /// <summary>Raw INTC_MASK register value (enabled interrupt lines).</summary>
        public uint Mask => _mask;

        /// <summary>
        /// Returns true when at least one interrupt is both pending (STAT bit set)
        /// and enabled (MASK bit set), meaning the CPU should be interrupted.
        /// </summary>
        public bool IsInterruptPending => (_stat & _mask) != 0;

        // ── Register I/O ──────────────────────────────────────────────────────

        /// <summary>
        /// Read a 32-bit INTC register.
        /// <paramref name="offset"/> is the byte offset from 0x1000F000
        /// (0x0000 = INTC_STAT, 0x0010 = INTC_MASK).
        /// </summary>
        public uint ReadRegister(uint offset)
        {
            return (offset & 0x10u) == 0u ? _stat : _mask;
        }

        /// <summary>
        /// Write a 32-bit INTC register.
        /// INTC_STAT uses write-1-to-clear; INTC_MASK uses write-1-to-toggle.
        /// </summary>
        public void WriteRegister(uint offset, uint value)
        {
            if ((offset & 0x10u) == 0u)
                _stat &= ~value;   // write-1-to-clear
            else
                _mask ^= value;    // write-1-to-toggle
        }

        // ── Interrupt raising ─────────────────────────────────────────────────

        /// <summary>
        /// Assert interrupt line <paramref name="bit"/> (0–31).
        /// Sets the corresponding bit in INTC_STAT.
        /// </summary>
        public void RaiseInterrupt(int bit)
        {
            if ((uint)bit < 32u)
                _stat |= 1u << bit;
        }

        /// <summary>Assert the VBlank-start interrupt (bit 2).</summary>
        public void RaiseVBlankStart() => RaiseInterrupt(INT_VBLANK_S);

        /// <summary>Assert the VBlank-end interrupt (bit 3).</summary>
        public void RaiseVBlankEnd()   => RaiseInterrupt(INT_VBLANK_E);
    }
}
