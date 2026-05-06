using System;

namespace CodeDesigner.Ps2.EmulatorCore.IOP
{
    /// <summary>
    /// Emulates one IOP hardware timer (T0–T5).
    ///
    /// The IOP has six 16-bit count-up timers split into two banks:
    ///   T0–T2  base 0x1F801100  (stride 0x10)
    ///   T3–T5  base 0x1F801480  (stride 0x10)
    ///
    /// Register layout (each timer, 0x10-byte window):
    ///   +0x00  TN_COUNT   — 16-bit counter (stored as 32-bit; upper bits ignored)
    ///   +0x04  TN_MODE    — mode/control register
    ///   +0x08  TN_TARGET  — 16-bit compare target
    ///
    /// TN_MODE bits (PS1/IOP hardware spec, no$psx.de docs):
    ///   [0-1] CLKS — clock selection (0=system clock, see bit 8)
    ///   [2]   GATE — gate enable
    ///   [3]   ZRET — reset counter to 0 on compare match
    ///   [4]   CMPE — compare interrupt enable (IRQ when COUNT = TARGET)
    ///   [5]   OVFE — overflow interrupt enable (IRQ when COUNT = 0xFFFF)
    ///   [6]   IRQR — IRQ repeat mode (0=one-shot, 1=fire repeatedly)
    ///   [7]   IRQT — IRQ toggle mode (0=pulse, 1=toggle EQUF on each IRQ)
    ///   [8]   ALT  — alternate clock source (T2: sysclock÷8; T3–T5: external)
    ///   [10]  EQUF — compare-match flag (cleared on TN_MODE read)
    ///   [11]  OVFF — overflow flag     (cleared on TN_MODE read)
    ///
    /// IMPORTANT: There is NO count-up enable bit in the PS1/IOP timer.
    /// The timer ALWAYS counts from its selected clock source (PCSX2-verified).
    /// The former MODE_CUE (bit 7) guard has been removed; bit 7 is IRQT.
    ///
    /// INTC bits wired by <see cref="IopEmulator"/>:
    ///   T0 → I_STAT bit 4,  T1 → bit 5,  T2 → bit 6
    ///   T3 → I_STAT bit 14, T4 → bit 15, T5 → bit 16
    ///
    /// Reference: PCSX2 pcsx2/IopCounters.cpp, pcsx2/IopCounters.h; no$psx.de timer docs.
    /// </summary>
    public sealed class IopTimer
    {
        // ── Register offsets ─────────────────────────────────────────────────
        public const uint REG_COUNT  = 0x00u;
        public const uint REG_MODE   = 0x04u;
        public const uint REG_TARGET = 0x08u;

        // ── TN_MODE bit masks ────────────────────────────────────────────────
        private const uint MODE_ZRET = 0x0008u; // [3]  reset count to 0 on compare match (PCSX2 Iop_Count_ResetOnCmp)
        private const uint MODE_CMPE = 0x0010u; // [4]  compare interrupt enable
        private const uint MODE_OVFE = 0x0020u; // [5]  overflow interrupt enable
        private const uint MODE_IRQR = 0x0040u; // [6]  IRQ repeat mode (0=one-shot, 1=fire on every match)
        private const uint MODE_ALT  = 0x0100u; // [8]  alternate clock source
        private const uint MODE_EQUF = 0x0400u; // [10] compare flag (cleared-on-read)
        private const uint MODE_OVFF = 0x0800u; // [11] overflow flag  (cleared-on-read)

        // IOP sysclock dividers.  The IOP bus runs at ~36.864 MHz.
        // T2 alternate source = sysclock÷8; T3–T5 alternate source treated as ÷8.
        private static readonly uint[] NormalDividers = { 1, 1, 1, 1, 1, 1 };
        private static readonly uint   AltDivider     = 8u;

        /// <summary>Timer index 0–5.</summary>
        public int Index { get; }

        // ── Register state ────────────────────────────────────────────────────
        private uint _count;   // 16-bit counter
        private uint _mode;
        private uint _target;  // 16-bit target

        private uint _cycleAccum;

        /// <summary>Fires when a compare-match interrupt occurs (CMPE enabled, EQUF newly set).</summary>
        public event EventHandler? CompareInterruptPending;

        /// <summary>Fires when an overflow interrupt occurs (OVFE enabled, OVFF newly set).</summary>
        public event EventHandler? OverflowInterruptPending;

        public IopTimer(int index)
        {
            Index = index;
        }

        // ── Register I/O ─────────────────────────────────────────────────────

        /// <summary>
        /// Read a register.  <paramref name="offset"/> is the byte offset within
        /// the timer's 0x10-byte window (0x00, 0x04, or 0x08).
        /// Reading TN_MODE clears the EQUF and OVFF sticky flags.
        /// </summary>
        public uint ReadRegister(uint offset)
        {
            switch (offset & 0x0Cu)  // align to 4-byte slot
            {
                case REG_COUNT:
                    return _count & 0xFFFFu;
                case REG_MODE:
                {
                    uint val = _mode;
                    _mode &= ~(MODE_EQUF | MODE_OVFF);  // sticky flags cleared on read
                    return val;
                }
                case REG_TARGET:
                    return _target & 0xFFFFu;
                default:
                    return 0u;
            }
        }

        /// <summary>
        /// Write a register.
        /// Writing TN_COUNT resets the counter and cycle accumulator to zero.
        /// Writing TN_MODE preserves the sticky EQUF/OVFF flags.
        /// </summary>
        public void WriteRegister(uint offset, uint value)
        {
            switch (offset & 0x0Cu)
            {
                case REG_COUNT:
                    _count      = 0u;
                    _cycleAccum = 0u;
                    break;
                case REG_MODE:
                    _mode = (value & ~(MODE_EQUF | MODE_OVFF))
                          | (_mode  &  (MODE_EQUF | MODE_OVFF));
                    _cycleAccum = 0u;
                    break;
                case REG_TARGET:
                    _target = value & 0xFFFFu;
                    break;
            }
        }

        // ── Tick ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Advance the timer by <paramref name="sysclockCycles"/> IOP sysclock cycles.
        /// Fires <see cref="CompareInterruptPending"/> and/or
        /// <see cref="OverflowInterruptPending"/> when conditions are met.
        ///
        /// The IOP timer hardware has no count-enable bit: the timer ALWAYS counts
        /// from its selected clock source (system clock or alternate).  The former
        /// MODE_CUE (bit 7) guard is removed; bit 7 is the IRQ toggle mode (IRQT),
        /// not a count-enable.  Reference: no$psx.de timer documentation.
        /// </summary>
        public void Tick(uint sysclockCycles)
        {
            uint div = (_mode & MODE_ALT) != 0 ? AltDivider : NormalDividers[Index];

            _cycleAccum += sysclockCycles;
            if (_cycleAccum < div)
                return;

            uint increments = _cycleAccum / div;
            _cycleAccum    %= div;

            for (uint i = 0; i < increments; i++)
            {
                _count = (_count + 1u) & 0xFFFFu;

                // Compare match
                if (_target != 0u && _count == _target)
                {
                    if ((_mode & MODE_CMPE) != 0)
                    {
                        // In repeat mode (IRQR=1) the interrupt fires on every match.
                        // In one-shot mode (IRQR=0) fire only when EQUF is clear.
                        if ((_mode & MODE_IRQR) != 0 || (_mode & MODE_EQUF) == 0)
                        {
                            _mode |= MODE_EQUF;
                            CompareInterruptPending?.Invoke(this, EventArgs.Empty);
                        }
                    }
                    // ZRET: reset counter to 0 on compare match so the timer fires
                    // at the target period rather than after a full 65536-tick wrap.
                    // Reference: PCSX2 IopCounters.h Iop_Count_ResetOnCmp (bit 3).
                    if ((_mode & MODE_ZRET) != 0)
                        _count = 0u;
                }

                // Overflow (counter wrapped from 0xFFFF → 0)
                if (_count == 0u && (_mode & MODE_OVFE) != 0)
                {
                    // In repeat mode fire on every overflow; one-shot only when OVFF clear.
                    if ((_mode & MODE_IRQR) != 0 || (_mode & MODE_OVFF) == 0)
                    {
                        _mode |= MODE_OVFF;
                        OverflowInterruptPending?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>Current raw counter value (16-bit).</summary>
        public uint Count  => _count & 0xFFFFu;

        /// <summary>Current raw TN_MODE value.</summary>
        public uint Mode   => _mode;

        /// <summary>Current raw target value (16-bit).</summary>
        public uint Target => _target & 0xFFFFu;

        /// <summary>
        /// True when compare or overflow interrupts are enabled (CMPE or OVFE bits set).
        /// The IOP timer always counts; this property reflects whether it can generate interrupts.
        /// </summary>
        public bool IsEnabled => (_mode & (MODE_CMPE | MODE_OVFE)) != 0;
    }
}
