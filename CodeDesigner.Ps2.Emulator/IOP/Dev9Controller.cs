using System;

namespace CodeDesigner.Ps2.Emulator.IOP
{
    /// <summary>
    /// Emulates the PS2 DEV9 parallel-port expansion interface.
    ///
    /// The DEV9 hardware connects an expansion card (PS2 Network Adapter or HDD
    /// add-on) to the IOP via a parallel I/O port.  The SPEED ASIC on the card
    /// provides a Subnet-mapping (SMAP) 10/100 Ethernet controller and an ATA
    /// interface for the optional hard-disk drive.
    ///
    /// Two address regions are emulated:
    /// <list type="number">
    ///   <item>
    ///     <term>IOP hardware register</term>
    ///     <description>
    ///       <see cref="IopRegBase"/> = 0x1F80146E — the 16-bit DEV9 hardware
    ///       identification (HW_DEV9_DATA) register in the IOP address space.
    ///       Returns 0 when no expansion device is present.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>Expansion bus (SPEED ASIC registers)</term>
    ///     <description>
    ///       <see cref="ExpBase"/> = 0x10000000 — 64-KB window for the SPEED ASIC
    ///       register file, shared by SMAP, ATA, and flash interfaces.  The IOP
    ///       accesses these registers as normal memory at physical addresses
    ///       0x10000000–0x1000FFFF.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// Key SPEED ASIC expansion-bus registers (16-bit, byte offsets from <see cref="ExpBase"/>):
    /// <list type="table">
    ///   <item><term>+0x00</term><description>SPD_R_REV_1 — capabilities (bit 0 = SMAP, bit 1 = ATA)</description></item>
    ///   <item><term>+0x02</term><description>SPD_R_REV_2 — revision 2</description></item>
    ///   <item><term>+0x04</term><description>SPD_R_REV_3 — revision 3</description></item>
    ///   <item><term>+0x28</term><description>SPD_R_INTR_STAT — interrupt status (W1C)</description></item>
    ///   <item><term>+0x2A</term><description>SPD_R_INTR_MASK — interrupt enable mask</description></item>
    ///   <item><term>+0x32</term><description>SPD_R_XFR_CTRL  — transfer control</description></item>
    ///   <item><term>+0x38</term><description>SPD_R_DBUF_STAT — data buffer status</description></item>
    ///   <item><term>+0x64</term><description>SPD_R_IF_CTRL   — interface control</description></item>
    /// </list>
    ///
    /// IOP DMA:
    ///   IOP DMAC channel 8 (DEV9 DMA) is wired to this controller via
    ///   <see cref="IopMemoryBus"/> when the <see cref="Dev9Controller"/> property
    ///   is assigned.
    ///
    /// Interrupt:
    ///   IOP INTC bit 13 is asserted when a DEV9 interrupt is pending.
    ///   The <see cref="InterruptRaised"/> event notifies the bus so it can set
    ///   I_STAT bit 13.
    ///
    /// Reference: PCSX2 <c>pcsx2/DEV9/DEV9.h</c>, <c>pcsx2/DEV9/DEV9.cpp</c>,
    ///            <c>pcsx2/IopMem.cpp</c> (<c>DEV9read8/16/32</c>).
    /// </summary>
    public sealed class Dev9Controller
    {
        // ── IOP hardware register ─────────────────────────────────────────────

        /// <summary>
        /// Address of the 16-bit DEV9 hardware identification register
        /// (HW_DEV9_DATA) in the IOP address space.
        /// </summary>
        public const uint IopRegBase = 0x1F80146Eu;

        /// <summary>Size of the IOP-side DEV9 register (2 bytes).</summary>
        public const uint IopRegSize = 2u;

        // ── Expansion bus ─────────────────────────────────────────────────────

        /// <summary>
        /// Base address of the DEV9 expansion bus (SPEED ASIC register file)
        /// in the IOP physical address space (SPD_REGBASE).
        /// </summary>
        public const uint ExpBase = 0x10000000u;

        /// <summary>Size of the expansion bus register window (64 KB).</summary>
        public const uint ExpSize = 0x10000u;

        // ── SPEED ASIC capability bits ────────────────────────────────────────

        /// <summary>SPD_CAPS_SMAP: SMAP Ethernet controller present (SPD_R_REV_1 bit 0).</summary>
        public const ushort SPD_CAPS_SMAP = 1 << 0;

        /// <summary>SPD_CAPS_ATA: ATA hard-disk interface present (SPD_R_REV_1 bit 1).</summary>
        public const ushort SPD_CAPS_ATA  = 1 << 1;

        // ── SPEED ASIC register offsets (from ExpBase, halfword-aligned) ──────

        private const uint SPD_OFF_REV_1     = 0x00u;
        private const uint SPD_OFF_REV_2     = 0x02u;
        private const uint SPD_OFF_REV_3     = 0x04u;
        private const uint SPD_OFF_INTR_STAT = 0x28u;  // W1C
        private const uint SPD_OFF_INTR_MASK = 0x2Au;

        // ── Register files ────────────────────────────────────────────────────

        private readonly byte[] _expRegs = new byte[ExpSize];

        /// <summary>
        /// Value of the IOP hardware identification register (HW_DEV9_DATA).
        /// Returns 0 when no expansion card is physically present; drivers
        /// interpret a non-zero value as a specific hardware revision.
        /// </summary>
        public ushort IopData { get; set; } = 0;

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Raised when a DEV9 interrupt becomes pending (IntrStat &amp; IntrMask
        /// transitions to non-zero after a write to either register).
        /// The IOP memory bus wires this event to IOP INTC bit 13.
        /// </summary>
        public event EventHandler? InterruptRaised;

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>Initialise the DEV9 controller with power-on reset values.</summary>
        public Dev9Controller()
        {
            Reset();
        }

        /// <summary>Apply power-on reset state.</summary>
        public void Reset()
        {
            Array.Clear(_expRegs, 0, _expRegs.Length);
            IopData = 0;
            // Advertise SMAP + ATA capabilities at SPD_R_REV_1.
            WriteExpHalf(SPD_OFF_REV_1, SPD_CAPS_SMAP | SPD_CAPS_ATA);
        }

        // ── Expansion bus access ──────────────────────────────────────────────

        /// <summary>
        /// Read a 32-bit value from the expansion register file at
        /// <paramref name="offset"/> bytes from <see cref="ExpBase"/>.
        /// </summary>
        public uint ReadExpWord(uint offset)
        {
            offset &= 0xFFFFu;
            if (offset + 3 >= ExpSize) return 0u;
            return (uint)( _expRegs[offset]
                         | (_expRegs[offset + 1] << 8)
                         | (_expRegs[offset + 2] << 16)
                         | (_expRegs[offset + 3] << 24));
        }

        /// <summary>
        /// Read a 16-bit value from the expansion register file at
        /// <paramref name="offset"/> bytes from <see cref="ExpBase"/>.
        /// </summary>
        public ushort ReadExpHalf(uint offset)
        {
            offset &= 0xFFFFu;
            if (offset + 1 >= ExpSize) return 0;
            return (ushort)(_expRegs[offset] | (_expRegs[offset + 1] << 8));
        }

        /// <summary>
        /// Read a byte from the expansion register file at
        /// <paramref name="offset"/> bytes from <see cref="ExpBase"/>.
        /// </summary>
        public byte ReadExpByte(uint offset)
        {
            offset &= 0xFFFFu;
            return offset < ExpSize ? _expRegs[offset] : (byte)0;
        }

        /// <summary>
        /// Write a 32-bit value to the expansion register file at
        /// <paramref name="offset"/> bytes from <see cref="ExpBase"/>.
        /// </summary>
        public void WriteExpWord(uint offset, uint value)
        {
            offset &= 0xFFFFu;
            if (offset + 3 >= ExpSize) return;
            WriteExpHalf(offset,     (ushort)value);
            WriteExpHalf(offset + 2, (ushort)(value >> 16));
        }

        /// <summary>
        /// Write a 16-bit value to the expansion register file at
        /// <paramref name="offset"/> bytes from <see cref="ExpBase"/>.
        /// </summary>
        public void WriteExpHalf(uint offset, ushort value)
        {
            offset &= 0xFFFFu;
            if (offset + 1 >= ExpSize) return;

            if (offset == SPD_OFF_INTR_STAT)
            {
                // W1C — clear only the bits written as 1.
                ushort cur = ReadExpHalf(offset);
                cur &= (ushort)~value;
                _expRegs[offset]     = (byte)cur;
                _expRegs[offset + 1] = (byte)(cur >> 8);
                return;
            }

            _expRegs[offset]     = (byte)value;
            _expRegs[offset + 1] = (byte)(value >> 8);

            // After writing INTR_MASK, check if any interrupt is now pending + enabled.
            if (offset == SPD_OFF_INTR_MASK)
            {
                ushort stat = ReadExpHalf(SPD_OFF_INTR_STAT);
                ushort mask = ReadExpHalf(SPD_OFF_INTR_MASK);
                if ((stat & mask) != 0)
                    InterruptRaised?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Write a byte to the expansion register file at
        /// <paramref name="offset"/> bytes from <see cref="ExpBase"/>.
        /// </summary>
        public void WriteExpByte(uint offset, byte value)
        {
            offset &= 0xFFFFu;
            if (offset >= ExpSize) return;
            // Use read-modify-write via halfword path to honour W1C semantics.
            uint hOff   = offset & ~1u;
            ushort old  = ReadExpHalf(hOff);
            ushort nov  = (offset & 1u) == 0u
                ? (ushort)((old & 0xFF00u) | value)
                : (ushort)((old & 0x00FFu) | ((uint)value << 8));
            WriteExpHalf(hOff, nov);
        }

        // ── Convenience properties ────────────────────────────────────────────

        /// <summary>SPD capabilities register (SPD_R_REV_1): SMAP and ATA flags.</summary>
        public ushort Capabilities => ReadExpHalf(SPD_OFF_REV_1);

        /// <summary>SPD interrupt status register (SPD_R_INTR_STAT, W1C).</summary>
        public ushort IntrStat     => ReadExpHalf(SPD_OFF_INTR_STAT);

        /// <summary>SPD interrupt mask register (SPD_R_INTR_MASK).</summary>
        public ushort IntrMask     => ReadExpHalf(SPD_OFF_INTR_MASK);
    }
}
