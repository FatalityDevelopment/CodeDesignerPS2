using System;

namespace CodeDesigner.Ps2.EmulatorCore.IOP
{
    /// <summary>
    /// Emulates the PS2 IOP USB OHCI host controller.
    ///
    /// The USB host controller is an OHCI-compliant implementation that supports two
    /// downstream ports.  It is used by the <c>USBD.IRX</c> IOP module to provide
    /// USB keyboard, mouse, and other HID device support.
    ///
    /// Memory-mapped at <see cref="RegBase"/> = 0x1F801600 in the IOP address space,
    /// across a 0x100-byte window.
    ///
    /// OHCI register layout (32-bit, byte offsets from <see cref="RegBase"/>):
    /// <list type="table">
    ///   <item><term>+0x00</term><description>HcRevision       — OHCI version (R/O = 0x10)</description></item>
    ///   <item><term>+0x04</term><description>HcControl        — operating mode / control state</description></item>
    ///   <item><term>+0x08</term><description>HcCommandStatus  — software reset / list owners</description></item>
    ///   <item><term>+0x0C</term><description>HcInterruptStatus — interrupt cause flags (W1C)</description></item>
    ///   <item><term>+0x10</term><description>HcInterruptEnable — interrupt enable bits</description></item>
    ///   <item><term>+0x14</term><description>HcInterruptDisable — writing clears bits in HcInterruptEnable</description></item>
    ///   <item><term>+0x18</term><description>HcHCCA           — Host Controller Communications Area pointer</description></item>
    ///   <item><term>+0x1C</term><description>HcPeriodCurrentED — current periodic endpoint descriptor</description></item>
    ///   <item><term>+0x20</term><description>HcControlHeadED  — head of control endpoint descriptor list</description></item>
    ///   <item><term>+0x24</term><description>HcControlCurrentED — current control endpoint descriptor</description></item>
    ///   <item><term>+0x28</term><description>HcBulkHeadED     — head of bulk endpoint descriptor list</description></item>
    ///   <item><term>+0x2C</term><description>HcBulkCurrentED  — current bulk endpoint descriptor</description></item>
    ///   <item><term>+0x30</term><description>HcDoneHead       — last completed transfer descriptor</description></item>
    ///   <item><term>+0x34</term><description>HcFmInterval     — frame length (default 0x2EDF = 11999 bit times)</description></item>
    ///   <item><term>+0x38</term><description>HcFmRemaining    — current frame bit-time counter (R/O)</description></item>
    ///   <item><term>+0x3C</term><description>HcFmNumber       — current frame number</description></item>
    ///   <item><term>+0x40</term><description>HcPeriodicStart  — periodic scheduling threshold</description></item>
    ///   <item><term>+0x44</term><description>HcLSThreshold    — low-speed threshold</description></item>
    ///   <item><term>+0x48</term><description>HcRhDescriptorA  — root hub descriptor A (NDP=2, NOCP=1)</description></item>
    ///   <item><term>+0x4C</term><description>HcRhDescriptorB  — root hub descriptor B</description></item>
    ///   <item><term>+0x50</term><description>HcRhStatus       — root hub status</description></item>
    ///   <item><term>+0x54</term><description>HcRhPortStatus[0] — port 0 status</description></item>
    ///   <item><term>+0x58</term><description>HcRhPortStatus[1] — port 1 status</description></item>
    /// </list>
    ///
    /// Interrupt:
    ///   IOP INTC bit 22 is asserted when a USB interrupt is pending and enabled.
    ///   The <see cref="InterruptRaised"/> event notifies the bus so it can set I_STAT bit 22.
    ///
    /// Reference: PCSX2 <c>pcsx2/USB/USB.cpp</c>, <c>pcsx2/USB/qemu-usb/hcd-ohci.cpp</c>.
    /// </summary>
    public sealed class UsbController
    {
        // ── Register base and window ──────────────────────────────────────────

        /// <summary>USB OHCI base address in IOP address space.</summary>
        public const uint RegBase = 0x1F801600u;

        /// <summary>Size of the USB OHCI register window (256 bytes).</summary>
        public const uint RegSize = 0x100u;

        // ── OHCI register word indices (offset / 4) ───────────────────────────

        private const int IDX_REVISION        = 0x00 / 4;  // R/O
        private const int IDX_CONTROL         = 0x04 / 4;
        private const int IDX_CMD_STATUS      = 0x08 / 4;
        private const int IDX_INT_STATUS      = 0x0C / 4;  // W1C
        private const int IDX_INT_ENABLE      = 0x10 / 4;
        private const int IDX_INT_DISABLE     = 0x14 / 4;  // W/O — clears INT_ENABLE bits
        private const int IDX_HCCA            = 0x18 / 4;
        private const int IDX_PERIOD_CURR_ED  = 0x1C / 4;
        private const int IDX_CTRL_HEAD_ED    = 0x20 / 4;
        private const int IDX_CTRL_CURR_ED    = 0x24 / 4;
        private const int IDX_BULK_HEAD_ED    = 0x28 / 4;
        private const int IDX_BULK_CURR_ED    = 0x2C / 4;
        private const int IDX_DONE_HEAD       = 0x30 / 4;
        private const int IDX_FM_INTERVAL     = 0x34 / 4;
        private const int IDX_FM_REMAINING    = 0x38 / 4;  // R/O (approximated as 0)
        private const int IDX_FM_NUMBER       = 0x3C / 4;
        private const int IDX_PERIODIC_START  = 0x40 / 4;
        private const int IDX_LS_THRESHOLD    = 0x44 / 4;
        private const int IDX_RH_DESC_A       = 0x48 / 4;
        private const int IDX_RH_DESC_B       = 0x4C / 4;
        private const int IDX_RH_STATUS       = 0x50 / 4;
        private const int IDX_RH_PORT_STAT_0  = 0x54 / 4;
        private const int IDX_RH_PORT_STAT_1  = 0x58 / 4;

        // ── OHCI constant values ──────────────────────────────────────────────

        /// <summary>OHCI revision 1.0 as reported in HcRevision.</summary>
        public const uint OHCI_REVISION = 0x10u;

        /// <summary>
        /// Default HcFmInterval value: frame interval = 11999 bit times (0x2EDF).
        /// Upper 16 bits (FSMPS) default to zero; driver fills them in during init.
        /// </summary>
        public const uint OHCI_FM_INTERVAL_DEFAULT = 0x2EDFu;

        /// <summary>
        /// HcRhDescriptorA reset value: NDP=2 (two ports), NOCP=1 (no over-current
        /// protection).  Matches the PS2's two USB ports (front panel).
        /// </summary>
        public const uint OHCI_RH_DESC_A_DEFAULT = 0x00001002u; // NOCP(bit12)=1, NDP=2

        // ── Register file ─────────────────────────────────────────────────────

        private readonly uint[] _regs = new uint[RegSize / 4];

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Raised when a USB interrupt becomes pending (HcInterruptStatus &amp;
        /// HcInterruptEnable transitions from zero to non-zero after a write).
        /// The IOP memory bus wires this event to IOP INTC bit 22.
        /// </summary>
        public event EventHandler? InterruptRaised;

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>Initialise the OHCI controller with power-on reset values.</summary>
        public UsbController()
        {
            Reset();
        }

        /// <summary>
        /// Apply the OHCI power-on reset state.  Equivalent to writing
        /// HcCommandStatus.HCR and waiting for the controller to self-clear it.
        /// </summary>
        public void Reset()
        {
            Array.Clear(_regs, 0, _regs.Length);
            _regs[IDX_REVISION]    = OHCI_REVISION;
            _regs[IDX_FM_INTERVAL] = OHCI_FM_INTERVAL_DEFAULT;
            _regs[IDX_RH_DESC_A]   = OHCI_RH_DESC_A_DEFAULT;
        }

        // ── Register access ───────────────────────────────────────────────────

        /// <summary>Read a 32-bit OHCI register at <paramref name="offset"/> bytes from <see cref="RegBase"/>.</summary>
        public uint ReadRegister(uint offset)
        {
            if (offset >= RegSize) return 0u;
            int idx = (int)(offset / 4);
            // HcInterruptDisable is write-only: reads return 0
            if (idx == IDX_INT_DISABLE) return 0u;
            return _regs[idx];
        }

        /// <summary>Write a 32-bit OHCI register at <paramref name="offset"/> bytes from <see cref="RegBase"/>.</summary>
        public void WriteRegister(uint offset, uint value)
        {
            if (offset >= RegSize) return;
            int idx = (int)(offset / 4);
            switch (idx)
            {
                case IDX_REVISION:
                    // Read-only; ignore writes.
                    break;

                case IDX_INT_STATUS:
                    // W1C — clear the bits that are written as 1.
                    _regs[idx] &= ~value;
                    break;

                case IDX_INT_DISABLE:
                    // Writing 1 bits clears the corresponding INT_ENABLE bits.
                    _regs[IDX_INT_ENABLE] &= ~value;
                    break;

                case IDX_INT_ENABLE:
                    _regs[idx] |= value;
                    // If any interrupt is now both pending and enabled, fire event.
                    if ((_regs[IDX_INT_STATUS] & _regs[IDX_INT_ENABLE]) != 0)
                        InterruptRaised?.Invoke(this, EventArgs.Empty);
                    break;

                case IDX_FM_REMAINING:
                    // Read-only; ignore writes.
                    break;

                default:
                    _regs[idx] = value;
                    break;
            }
        }

        // ── Convenience properties ────────────────────────────────────────────

        /// <summary>HcRevision register value (always <see cref="OHCI_REVISION"/>).</summary>
        public uint Revision      => _regs[IDX_REVISION];

        /// <summary>HcRhDescriptorA: number of downstream ports (bits 7:0).</summary>
        public int  NumPorts      => (int)(_regs[IDX_RH_DESC_A] & 0xFFu);

        /// <summary>HcFmInterval: frame interval in bit times (bits 13:0).</summary>
        public uint FmInterval    => _regs[IDX_FM_INTERVAL] & 0x3FFFu;

        /// <summary>HcInterruptStatus: pending interrupt flags.</summary>
        public uint IntStatus     => _regs[IDX_INT_STATUS];

        /// <summary>HcInterruptEnable: enabled interrupt mask.</summary>
        public uint IntEnable     => _regs[IDX_INT_ENABLE];
    }
}
