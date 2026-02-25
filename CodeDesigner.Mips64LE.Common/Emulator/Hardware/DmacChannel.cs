using System;

namespace CodeDesigner.Mips64LE.Common.Emulator.Hardware
{
    /// <summary>
    /// Emulates one PS2 EE DMA channel (Dn).
    ///
    /// Each channel exposes five memory-mapped 32-bit registers at its base address:
    ///   +0x00  Dn_CHCR  — channel control (direction / mode / start)
    ///   +0x10  Dn_MADR  — memory address register
    ///   +0x20  Dn_QWC   — quadword count (number of 16-byte units to transfer)
    ///   +0x30  Dn_TADR  — tag address register (chain-mode only)
    ///   +0x80  Dn_SADR  — scratchpad address register
    ///
    /// CHCR bit layout (reference: PCSX2 pcsx2/Dmac.h):
    ///   [0]    DIR  — direction (0 = RAM→FIFO, 1 = FIFO→RAM)
    ///   [1]    (reserved)
    ///   [3:2]  MOD  — mode (00 = normal, 01 = chain, 10 = interleave)
    ///   [5:4]  ASP  — address stack pointer (chain mode)
    ///   [6]    TTE  — tag transfer enable
    ///   [7]    TIE  — tag interrupt enable
    ///   [8]    STR  — start/busy flag; writing 1 starts DMA; auto-cleared when done
    ///
    /// Normal-mode transfers are executed synchronously by <see cref="Execute"/> when
    /// the <see cref="DMAC"/> detects STR=1 written to CHCR.  Chain and interleave
    /// modes are not yet implemented; they are accepted by the register write but
    /// execute as a no-op so BIOS probe code does not hang.
    ///
    /// Reference: PCSX2 pcsx2/Dmac.h, pcsx2/Dmac.cpp.
    /// </summary>
    public sealed class DmacChannel
    {
        // ── Register offsets (byte offset within the channel's I/O window) ───
        public const uint REG_CHCR = 0x00u;
        public const uint REG_MADR = 0x10u;
        public const uint REG_QWC  = 0x20u;
        public const uint REG_TADR = 0x30u;
        public const uint REG_SADR = 0x80u;

        // ── CHCR bit masks ────────────────────────────────────────────────────
        private const uint CHCR_DIR = 1u << 0;  // transfer direction
        private const uint CHCR_MOD = 3u << 2;  // transfer mode
        private const uint CHCR_STR = 1u << 8;  // start/busy

        /// <summary>0-based channel index (0 = VIF0 … 9 = SPR1).</summary>
        public int ChannelId { get; }

        private uint _chcr;
        private uint _madr;
        private uint _qwc;
        private uint _tadr;
        private uint _sadr;

        /// <summary>Raised when a normal-mode DMA transfer completes on this channel.</summary>
        public event EventHandler? TransferComplete;

        /// <summary>
        /// Optional consumer for data transferred from RAM to a device (DIR=0, RAM→FIFO).
        /// When set, normal-mode transfers read each quadword from memory and pass it to
        /// this callback as a four-element <c>uint[]</c> (little-endian, x/y/z/w order).
        /// Used to feed VIF0 and VIF1 with their packet data during DMA transfers.
        /// </summary>
        public Action<uint[], int, int>? DataConsumer { get; set; }

        /// <summary>
        /// Optional provider for data transferred from a device to RAM (DIR=1, FIFO→RAM).
        /// When set, normal-mode transfers call this once per 32-bit word instead of
        /// zero-filling.  Used to drain the SIF0 FIFO (EE DMAC channel 5) into EE RAM.
        /// </summary>
        public Func<uint>? DataProvider { get; set; }

        public DmacChannel(int id) => ChannelId = id;

        // ── Register access ───────────────────────────────────────────────────

        /// <summary>
        /// Read a 32-bit channel register at <paramref name="regOffset"/>.
        /// Unrecognised offsets return 0.
        /// </summary>
        public uint ReadRegister(uint regOffset) => regOffset switch
        {
            REG_CHCR => _chcr,
            REG_MADR => _madr,
            REG_QWC  => _qwc,
            REG_TADR => _tadr,
            REG_SADR => _sadr,
            _        => 0u
        };

        /// <summary>
        /// Write a 32-bit channel register.
        /// When CHCR is written with STR=1 the <see cref="DMAC"/> parent is expected
        /// to call <see cref="Execute"/> immediately afterwards to run the transfer.
        /// </summary>
        public void WriteRegister(uint regOffset, uint value)
        {
            switch (regOffset)
            {
                case REG_CHCR: _chcr = value; break;
                case REG_MADR: _madr = value; break;
                case REG_QWC:  _qwc  = value; break;
                case REG_TADR: _tadr = value; break;
                case REG_SADR: _sadr = value; break;
            }
        }

        // ── CHCR field accessors ──────────────────────────────────────────────

        /// <summary>Transfer direction: false = RAM→FIFO, true = FIFO→RAM.</summary>
        public bool DirectionToMemory => (_chcr & CHCR_DIR) != 0;

        /// <summary>Transfer mode: 0 = normal, 1 = chain, 2 = interleave.</summary>
        public int Mode => (int)((_chcr & CHCR_MOD) >> 2);

        /// <summary>True when the channel is active (STR bit is set).</summary>
        public bool IsRunning => (_chcr & CHCR_STR) != 0;

        /// <summary>Current value of MADR.</summary>
        public uint Madr => _madr;

        /// <summary>Current value of QWC.</summary>
        public uint Qwc  => _qwc;

        /// <summary>Tag address register (chain mode).</summary>
        public uint Tadr => _tadr;

        // ── Transfer execution ────────────────────────────────────────────────

        /// <summary>
        /// Execute a normal-mode DMA transfer: moves <see cref="Qwc"/> quadwords
        /// (16 bytes each) between memory and an implicit FIFO using
        /// <paramref name="bus"/> for all memory accesses.
        ///
        /// <list type="bullet">
        ///   <item><term>DIR=0 (RAM→FIFO)</term>
        ///     <description>Advance MADR by QWC×16; data is consumed from RAM.
        ///     </description></item>
        ///   <item><term>DIR=1 (FIFO→RAM)</term>
        ///     <description>Zero-fill QWC×16 bytes at MADR (no real FIFO data in
        ///     this emulator), then advance MADR.</description></item>
        /// </list>
        ///
        /// Chain and interleave modes (MOD≠0) are not yet implemented; the method
        /// clears STR immediately so BIOS probe loops do not spin forever.
        ///
        /// When the transfer completes (or STR is cleared), <see cref="TransferComplete"/>
        /// is raised so that the parent <see cref="DMAC"/> can update D_STAT.
        /// Does nothing when the channel is not running or QWC is 0 in normal mode.
        /// </summary>
        internal void Execute(PS2MemoryBus bus)
        {
            if (!IsRunning)
                return;

            if (Mode != 0)
            {
                // Chain / interleave mode: not yet fully implemented.
                // Clear STR to avoid busy-loops and fire completion so D_STAT is set.
                _chcr &= ~CHCR_STR;
                TransferComplete?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (_qwc == 0)
            {
                // Nothing to transfer; just clear STR.
                _chcr &= ~CHCR_STR;
                TransferComplete?.Invoke(this, EventArgs.Empty);
                return;
            }

            uint addr  = _madr;
            uint count = _qwc;

            if (DirectionToMemory)
            {
                // FIFO→RAM: read from DataProvider if wired, otherwise zero-fill.
                for (uint q = 0; q < count; q++)
                {
                    bus.WriteWord(addr,      DataProvider?.Invoke() ?? 0u);
                    bus.WriteWord(addr + 4,  DataProvider?.Invoke() ?? 0u);
                    bus.WriteWord(addr + 8,  DataProvider?.Invoke() ?? 0u);
                    bus.WriteWord(addr + 12, DataProvider?.Invoke() ?? 0u);
                    addr += 16u;
                }
            }
            else if (DataConsumer != null)
            {
                // RAM→FIFO with a registered device consumer (e.g. VIF):
                // read each quadword from memory as four 32-bit words and deliver them.
                uint[] qbuf = new uint[count * 4];
                for (uint q = 0; q < count; q++)
                {
                    qbuf[q * 4 + 0] = bus.ReadWord(addr);
                    qbuf[q * 4 + 1] = bus.ReadWord(addr + 4);
                    qbuf[q * 4 + 2] = bus.ReadWord(addr + 8);
                    qbuf[q * 4 + 3] = bus.ReadWord(addr + 12);
                    addr += 16u;
                }
                DataConsumer(qbuf, 0, (int)(count * 4));
            }
            else
            {
                // RAM→FIFO: no consumer registered — advance address only.
                addr += count * 16u;
            }

            _madr  = addr;
            _qwc   = 0u;
            _chcr &= ~CHCR_STR;
            TransferComplete?.Invoke(this, EventArgs.Empty);
        }
    }
}
