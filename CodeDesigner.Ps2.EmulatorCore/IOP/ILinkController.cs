using System;

namespace CodeDesigner.Ps2.EmulatorCore.IOP
{
    /// <summary>
    /// Emulates the PS2 IOP i.LINK (IEEE 1394 / FireWire) controller stub.
    ///
    /// The i.LINK interface is used for multi-PS2 LAN play in a small number of
    /// commercial titles.  This implementation provides the correct memory-mapped
    /// register window so that IOP BIOS initialisation and driver probing do not
    /// fault; full transfer semantics are not emulated.
    ///
    /// Memory-mapped at <see cref="RegBase"/> = 0x1F808400 in the IOP address
    /// space, across a <see cref="RegSize"/> = 0x200-byte window.
    ///
    /// Notable register offsets (32-bit, from <see cref="RegBase"/>):
    /// <list type="table">
    ///   <item><term>+0x00</term><description>Version / node ID register (R/O, returns <see cref="VERSION_ID"/>)</description></item>
    ///   <item><term>+0x04</term><description>Control register</description></item>
    ///   <item><term>+0x08</term><description>Interrupt status (W1C)</description></item>
    ///   <item><term>+0x0C</term><description>Interrupt mask</description></item>
    /// </list>
    ///
    /// Interrupt:
    ///   IOP INTC bit 18 is asserted when an i.LINK interrupt is pending.
    ///   The <see cref="InterruptRaised"/> event notifies the bus so it can set I_STAT bit 18.
    ///
    /// Reference: PCSX2 <c>pcsx2/IopHw.h</c> (HW_FW_START / HW_FW_END).
    /// </summary>
    public sealed class ILinkController
    {
        // ── Register base and window ──────────────────────────────────────────

        /// <summary>i.LINK controller base address in IOP address space.</summary>
        public const uint RegBase = 0x1F808400u;

        /// <summary>Size of the i.LINK register window (512 bytes).</summary>
        public const uint RegSize = 0x200u;

        // ── Register offsets ──────────────────────────────────────────────────

        private const uint OFF_VERSION    = 0x00u;  // R/O
        private const uint OFF_CTRL       = 0x04u;
        private const uint OFF_INT_STATUS = 0x08u;  // W1C
        private const uint OFF_INT_MASK   = 0x0Cu;

        // ── Default values ────────────────────────────────────────────────────

        /// <summary>
        /// Value returned by the version register at offset 0x00.
        /// The upper 16 bits identify the PS2 i.LINK chip vendor; the lower 16
        /// bits hold the revision.  This value is a placeholder that allows driver
        /// detection routines to identify the controller as present.
        /// </summary>
        public const uint VERSION_ID = 0x00000001u;

        // ── Register file ─────────────────────────────────────────────────────

        private readonly uint[] _regs = new uint[RegSize / 4];

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Raised when an i.LINK interrupt becomes pending (IntStatus &amp; IntMask
        /// transitions to non-zero).
        /// The IOP memory bus wires this event to IOP INTC bit 18.
        /// </summary>
        public event EventHandler? InterruptRaised;

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>Initialise the i.LINK controller with power-on reset values.</summary>
        public ILinkController()
        {
            Reset();
        }

        /// <summary>Apply power-on reset state.</summary>
        public void Reset()
        {
            Array.Clear(_regs, 0, _regs.Length);
            _regs[OFF_VERSION / 4] = VERSION_ID;
        }

        // ── Register access ───────────────────────────────────────────────────

        /// <summary>
        /// Read a 32-bit register at <paramref name="offset"/> bytes from
        /// <see cref="RegBase"/>.
        /// </summary>
        public uint ReadRegister(uint offset)
        {
            if (offset >= RegSize) return 0u;
            return _regs[offset / 4];
        }

        /// <summary>
        /// Write a 32-bit register at <paramref name="offset"/> bytes from
        /// <see cref="RegBase"/>.
        /// </summary>
        public void WriteRegister(uint offset, uint value)
        {
            if (offset >= RegSize) return;
            uint idx = offset / 4;
            switch (offset)
            {
                case OFF_VERSION:
                    // Read-only; ignore writes.
                    break;

                case OFF_INT_STATUS:
                    // W1C — clear the bits that are written as 1.
                    _regs[idx] &= ~value;
                    break;

                case OFF_INT_MASK:
                    _regs[idx] = value;
                    // If any interrupt is now both pending and enabled, fire event.
                    if ((_regs[OFF_INT_STATUS / 4] & _regs[OFF_INT_MASK / 4]) != 0)
                        InterruptRaised?.Invoke(this, EventArgs.Empty);
                    break;

                default:
                    _regs[idx] = value;
                    break;
            }
        }

        // ── Convenience properties ────────────────────────────────────────────

        /// <summary>Version register value (read-only, returns <see cref="VERSION_ID"/>).</summary>
        public uint Version   => _regs[OFF_VERSION / 4];

        /// <summary>Interrupt status register (W1C).</summary>
        public uint IntStatus => _regs[OFF_INT_STATUS / 4];

        /// <summary>Interrupt mask register.</summary>
        public uint IntMask   => _regs[OFF_INT_MASK / 4];
    }
}
