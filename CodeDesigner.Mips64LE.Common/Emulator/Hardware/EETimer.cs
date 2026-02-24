using System;

namespace CodeDesigner.Mips64LE.Common.Emulator.Hardware
{
    /// <summary>
    /// Emulates one of the four PS2 EE hardware countdown timers (T0–T3).
    ///
    /// Each timer exposes three 32-bit memory-mapped registers at base+0x00,
    /// base+0x10 and base+0x20 (plus base+0x30 for T0/T1 hold).
    ///
    /// Register layout (PCSX2 source: pcsx2/IopCounters.h / TimerSystem.cpp):
    ///   +0x00  TN_COUNT  — current counter value (32-bit, wraps to 0 on overflow)
    ///   +0x10  TN_MODE   — mode register (see <see cref="TimerMode"/> below)
    ///   +0x20  TN_COMP   — compare target value
    ///   +0x30  TN_HOLD   — hold register (T0/T1 only, <see cref="HasHold"/>)
    ///
    /// Clock dividers selected by TN_MODE.CLKS[1:0]:
    ///   00 → bus clock / 2   (~37.5 MHz, use ~2 cycles per tick)
    ///   01 → bus clock / 16
    ///   10 → bus clock / 256
    ///   11 → external (H-BLANK for T0/T1; SPU2 for T2/T3 — treated as /256 here)
    ///
    /// Reference: PCSX2 pcsx2/IopCounters.cpp, pcsx2/R5900.cpp clock_ratio comments.
    /// </summary>
    public sealed class EETimer
    {
        // ── Register offsets ─────────────────────────────────────────────────
        public const uint REG_COUNT = 0x00;
        public const uint REG_MODE  = 0x10;
        public const uint REG_COMP  = 0x20;
        public const uint REG_HOLD  = 0x30;

        // ── TN_MODE bit positions ─────────────────────────────────────────────
        private const uint MODE_CLKS  = 0x0003u; // [1:0]  clock divider select
        private const uint MODE_GATE  = 0x0004u; // [2]    gate enable
        private const uint MODE_GATS  = 0x0008u; // [3]    gate select
        private const uint MODE_GATM  = 0x0030u; // [5:4]  gate mode
        private const uint MODE_ZRET  = 0x0040u; // [6]    zero return on compare
        private const uint MODE_CUE   = 0x0080u; // [7]    count-up enable
        private const uint MODE_CMPE  = 0x0100u; // [8]    compare interrupt enable
        private const uint MODE_OVFE  = 0x0200u; // [9]    overflow interrupt enable
        private const uint MODE_EQUF  = 0x0400u; // [10]   equal flag (cleared on read)
        private const uint MODE_OVFF  = 0x0800u; // [11]   overflow flag (cleared on read)

        // Clock dividers indexed by CLKS[1:0].  The PS2 EE bus runs at ~75 MHz;
        // dividers below give the number of bus-clock cycles per timer increment.
        private static readonly uint[] ClkDividers = { 2, 16, 256, 256 };

        /// <summary>True for T0 and T1 which have a hold register.</summary>
        public bool HasHold { get; }

        /// <summary>Timer index (0–3), used for diagnostics.</summary>
        public int Index { get; }

        // ── Register state ────────────────────────────────────────────────────
        private uint _count;
        private uint _mode;
        private uint _comp;
        private uint _hold;

        /// <summary>Fractional cycle accumulator (sub-divider remainder).</summary>
        private uint _cycleAccum;

        /// <summary>
        /// Fires when a compare match interrupt is pending (CMPE set and EQUF cleared→set).
        /// </summary>
        public event EventHandler? CompareInterruptPending;

        /// <summary>
        /// Fires when an overflow interrupt is pending (OVFE set and OVFF cleared→set).
        /// </summary>
        public event EventHandler? OverflowInterruptPending;

        public EETimer(int index)
        {
            Index   = index;
            HasHold = index == 0 || index == 1;
        }

        // ── Register I/O ──────────────────────────────────────────────────────

        /// <summary>
        /// Read a 32-bit timer register.  <paramref name="offset"/> is the byte
        /// offset within the timer's 0x800-byte window (0x00, 0x10, 0x20, 0x30).
        /// Reading TN_MODE clears the EQUF and OVFF sticky flags.
        /// </summary>
        public uint ReadRegister(uint offset)
        {
            switch (offset & 0x30u)
            {
                case REG_COUNT: return _count;
                case REG_MODE:
                {
                    uint val = _mode;
                    // Sticky flags are cleared on read (PS2 EE behaviour)
                    _mode &= ~(MODE_EQUF | MODE_OVFF);
                    return val;
                }
                case REG_COMP: return _comp;
                case REG_HOLD: return HasHold ? _hold : 0u;
                default:       return 0u;
            }
        }

        /// <summary>
        /// Write a 32-bit timer register.
        /// Writing TN_COUNT resets the counter to zero.
        /// Writing TN_MODE preserves the sticky EQUF/OVFF hardware flags; the
        /// software-writable fields (CLKS through OVFE) are updated normally.
        /// </summary>
        public void WriteRegister(uint offset, uint value)
        {
            switch (offset & 0x30u)
            {
                case REG_COUNT:
                    _count      = 0; // Writing resets the counter
                    _cycleAccum = 0;
                    break;
                case REG_MODE:
                    // Preserve sticky flags; update configuration bits
                    _mode = (value & ~(MODE_EQUF | MODE_OVFF))
                          | (_mode  &  (MODE_EQUF | MODE_OVFF));
                    _cycleAccum = 0;
                    break;
                case REG_COMP:
                    _comp = value;
                    break;
                case REG_HOLD:
                    if (HasHold) _hold = value;
                    break;
            }
        }

        // ── Tick ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Advance the timer by <paramref name="busCycles"/> bus-clock cycles.
        /// Fires <see cref="CompareInterruptPending"/> and/or
        /// <see cref="OverflowInterruptPending"/> as appropriate.
        /// </summary>
        public void Tick(uint busCycles)
        {
            // Only count when count-up enable (CUE) is set
            if ((_mode & MODE_CUE) == 0)
                return;

            uint div = ClkDividers[_mode & MODE_CLKS];

            _cycleAccum += busCycles;
            if (_cycleAccum < div)
                return;

            uint increments  = _cycleAccum / div;
            _cycleAccum     %= div;

            for (uint i = 0; i < increments; i++)
            {
                _count++;

                // Compare match
                if (_count == _comp && (_mode & MODE_CMPE) != 0)
                {
                    if ((_mode & MODE_EQUF) == 0)
                    {
                        _mode |= MODE_EQUF;
                        CompareInterruptPending?.Invoke(this, EventArgs.Empty);
                    }
                    if ((_mode & MODE_ZRET) != 0)
                        _count = 0;
                }

                // Overflow (count wraps from 0xFFFF_FFFF → 0)
                if (_count == 0 && (_mode & MODE_OVFE) != 0)
                {
                    if ((_mode & MODE_OVFF) == 0)
                    {
                        _mode |= MODE_OVFF;
                        OverflowInterruptPending?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Current raw value of TN_COUNT.</summary>
        public uint Count => _count;

        /// <summary>Current raw value of TN_MODE.</summary>
        public uint Mode => _mode;

        /// <summary>Current raw value of TN_COMP.</summary>
        public uint Compare => _comp;

        /// <summary>True when count-up enable (CUE) bit is set.</summary>
        public bool IsEnabled => (_mode & MODE_CUE) != 0;
    }
}
