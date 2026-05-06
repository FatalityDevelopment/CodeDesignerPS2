using System;

namespace CodeDesigner.Ps2.EmulatorCore.Hardware
{
    /// <summary>
    /// Emulates one PS2 EE DMA channel (Dn).
    ///
    /// Each channel exposes seven memory-mapped 32-bit registers at its base address:
    ///   +0x00  Dn_CHCR  — channel control (direction / mode / start / TAG)
    ///   +0x10  Dn_MADR  — memory address register
    ///   +0x20  Dn_QWC   — quadword count (number of 16-byte units to transfer)
    ///   +0x30  Dn_TADR  — tag address register (chain-mode only)
    ///   +0x40  Dn_ASR0  — address stack register 0 (CALL/RET, chain mode)
    ///   +0x50  Dn_ASR1  — address stack register 1 (CALL/RET, chain mode)
    ///   +0x80  Dn_SADR  — scratchpad address register
    ///
    /// CHCR bit layout (reference: PCSX2 pcsx2/Dmac.h):
    ///   [0]     DIR  — direction (0 = RAM→FIFO, 1 = FIFO→RAM)
    ///   [1]     (reserved)
    ///   [3:2]   MOD  — mode (00 = normal, 01 = chain, 10 = interleave)
    ///   [5:4]   ASP  — address stack pointer (chain mode, managed by hardware)
    ///   [6]     TTE  — tag transfer enable (also send tag QW to device)
    ///   [7]     TIE  — tag interrupt enable (stop chain when IRQ bit set in tag)
    ///   [8]     STR  — start/busy flag; writing 1 starts DMA; auto-cleared when done
    ///   [31:16] TAG  — upper 16 bits of last-read DMA tag (bits [31:16] of tag word 0)
    ///
    /// Chain-mode DMA tag IDs (bits [26:24] of DMA tag word 0):
    ///   0 (REFE) — data at ADDR; next tag = TADR+16; terminate
    ///   1 (CNT)  — data at TADR+16; next tag = TADR+16+QWC*16
    ///   2 (NEXT) — data at TADR+16; next tag = ADDR
    ///   3 (REF)  — data at ADDR; next tag = TADR+16
    ///   4 (REFS) — data at ADDR (stall); next tag = TADR+16
    ///   5 (CALL) — data at TADR+16; push return addr to ASR[ASP], ASP++; next = ADDR
    ///   6 (RET)  — data at TADR+16; ASP--; next = ASR[ASP] (or terminate if ASP==0)
    ///   7 (END)  — data at TADR+16; terminate after transfer
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
        public const uint REG_ASR0 = 0x40u;
        public const uint REG_ASR1 = 0x50u;
        public const uint REG_SADR = 0x80u;

        // ── CHCR bit masks ────────────────────────────────────────────────────
        private const uint CHCR_DIR = 1u << 0;  // transfer direction
        private const uint CHCR_MOD = 3u << 2;  // transfer mode
        private const uint CHCR_ASP = 3u << 4;  // address stack pointer
        private const uint CHCR_TTE = 1u << 6;  // tag transfer enable
        private const uint CHCR_TIE = 1u << 7;  // tag interrupt enable
        private const uint CHCR_STR = 1u << 8;  // start/busy

        /// <summary>0-based channel index (0 = VIF0 … 9 = SPR1).</summary>
        public int ChannelId { get; }

        private uint _chcr;
        private uint _madr;
        private uint _qwc;
        private uint _tadr;
        private uint _asr0;
        private uint _asr1;
        private uint _sadr;

        /// <summary>Raised when a DMA transfer completes on this channel.</summary>
        public event EventHandler? TransferComplete;

        /// <summary>
        /// Optional consumer for data transferred from RAM to a device (DIR=0, RAM→FIFO).
        /// When set, transfers read each quadword from memory and pass it to
        /// this callback as a four-element <c>uint[]</c> (little-endian, x/y/z/w order).
        /// Used to feed VIF0 and VIF1 with their packet data during DMA transfers.
        /// </summary>
        public Action<uint[], int, int>? DataConsumer { get; set; }

        /// <summary>
        /// Optional provider for data transferred from a device to RAM (DIR=1, FIFO→RAM).
        /// When set, transfers call this once per 32-bit word instead of zero-filling.
        /// Used to drain the SIF0 FIFO (EE DMAC channel 5) into EE RAM.
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
            REG_ASR0 => _asr0,
            REG_ASR1 => _asr1,
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
                case REG_ASR0: _asr0 = value; break;
                case REG_ASR1: _asr1 = value; break;
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

        /// <summary>Address stack register 0 (chain mode CALL/RET).</summary>
        public uint Asr0 => _asr0;

        /// <summary>Address stack register 1 (chain mode CALL/RET).</summary>
        public uint Asr1 => _asr1;

        // ── Transfer execution ────────────────────────────────────────────────

        /// <summary>
        /// Execute a DMA transfer using <paramref name="bus"/> for memory access.
        /// <paramref name="sqwc"/> is the D_SQWC global register value used by interleave mode
        /// (bits [7:0] = skip count, bits [23:16] = transfer count per slice).
        ///
        /// <list type="bullet">
        ///   <item><term>MOD=0 (normal)</term>
        ///     <description>Moves <see cref="Qwc"/> quadwords between memory and an
        ///     implicit FIFO.</description></item>
        ///   <item><term>MOD=1 (chain)</term>
        ///     <description>Reads a linked list of DMA tags from TADR and processes
        ///     each segment until a terminating tag ID (REFE/END) or IRQ+TIE.
        ///     </description></item>
        ///   <item><term>MOD=2 (interleave)</term>
        ///     <description>Alternates between transferring TQWC quadwords and
        ///     skipping SQWC quadwords until all <see cref="Qwc"/> quadwords are done.
        ///     </description></item>
        /// </list>
        ///
        /// When the transfer completes, <see cref="TransferComplete"/> is raised so that
        /// the parent <see cref="DMAC"/> can update D_STAT.
        /// Does nothing when the channel is not running.
        /// </summary>
        internal void Execute(PS2MemoryBus bus, uint sqwc = 0u)
        {
            if (!IsRunning)
                return;

            switch (Mode)
            {
                case 0: ExecuteNormal(bus);             break;
                case 1: ExecuteChain(bus);              break;
                case 2: ExecuteInterleave(bus, sqwc);   break;
                default:
                    _chcr &= ~CHCR_STR;
                    TransferComplete?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        // ── Normal mode ───────────────────────────────────────────────────────

        private void ExecuteNormal(PS2MemoryBus bus)
        {
            uint addr  = _madr;
            uint count = _qwc;

            if (count == 0)
            {
                _chcr &= ~CHCR_STR;
                TransferComplete?.Invoke(this, EventArgs.Empty);
                return;
            }

            TransferQws(bus, addr, count, out addr);

            _madr  = addr;
            _qwc   = 0u;
            _chcr &= ~CHCR_STR;
            TransferComplete?.Invoke(this, EventArgs.Empty);
        }

        // ── Chain mode ────────────────────────────────────────────────────────

        /// <summary>
        /// Execute a chain-mode DMA transfer.  Reads DMA tags from TADR in a loop,
        /// processing each tag's data segment until a terminating condition is met.
        /// Reference: PCSX2 pcsx2/Dmac.cpp.
        /// </summary>
        private void ExecuteChain(PS2MemoryBus bus)
        {
            bool terminate = false;

            while (!terminate)
            {
                // Read the 64-bit DMA tag at TADR (stored as two 32-bit words).
                uint tagLo = bus.ReadWord(_tadr);       // [15:0]=QWC [23:16]=PCE [26:24]=ID [27]=IRQ
                uint tagHi = bus.ReadWord(_tadr + 4);   // [30:0]=ADDR [31]=SPR

                // Store bits [31:16] of tag word 0 in CHCR.TAG (bits [31:16] of CHCR).
                _chcr = (_chcr & 0x0000FFFFu) | (tagLo & 0xFFFF0000u);

                uint qwc  = tagLo & 0x0000FFFFu;
                uint id   = (tagLo >> 24) & 0x7u;
                bool irq  = (tagLo & (1u << 27)) != 0;
                uint addr = tagHi & 0x7FFFFFFFu;   // ADDR field (strip SPR bit)

                uint dataAddr;
                uint nextTadr;

                switch (id)
                {
                    case 0: // REFE — data at ADDR; terminate after
                        dataAddr  = addr;
                        nextTadr  = _tadr + 16u;
                        terminate = true;
                        break;

                    case 1: // CNT — data at TADR+16; next tag follows data
                        dataAddr = _tadr + 16u;
                        nextTadr = dataAddr + qwc * 16u;
                        break;

                    case 2: // NEXT — data at TADR+16; next tag at ADDR
                        dataAddr = _tadr + 16u;
                        nextTadr = addr;
                        break;

                    case 3: // REF — data at ADDR; next tag at TADR+16
                    case 4: // REFS — same as REF (stall-control variant)
                        dataAddr = addr;
                        nextTadr = _tadr + 16u;
                        break;

                    case 5: // CALL — data at TADR+16; push return addr; jump to ADDR
                    {
                        dataAddr = _tadr + 16u;
                        uint returnAddr = dataAddr + qwc * 16u;
                        int  asp = (int)((_chcr & CHCR_ASP) >> 4);
                        if (asp == 0) _asr0 = returnAddr;
                        else          _asr1 = returnAddr;
                        _chcr = (_chcr & ~CHCR_ASP) | ((uint)(asp + 1) << 4);
                        nextTadr = addr;
                        break;
                    }

                    case 6: // RET — data at TADR+16; pop return addr or terminate
                    {
                        dataAddr = _tadr + 16u;
                        int asp = (int)((_chcr & CHCR_ASP) >> 4);
                        if (asp > 0)
                        {
                            asp--;
                            _chcr    = (_chcr & ~CHCR_ASP) | ((uint)asp << 4);
                            nextTadr = (asp == 0) ? _asr0 : _asr1;
                        }
                        else
                        {
                            nextTadr  = _tadr + 16u;
                            terminate = true;
                        }
                        break;
                    }

                    default: // 7 (END) — data at TADR+16; terminate after
                        dataAddr  = _tadr + 16u;
                        nextTadr  = _tadr + 16u;
                        terminate = true;
                        break;
                }

                // If TTE=1, send the tag quadword (all 4 words at TADR) to the device.
                if ((_chcr & CHCR_TTE) != 0 && !DirectionToMemory && DataConsumer != null)
                {
                    uint[] tagQw =
                    {
                        tagLo,
                        tagHi,
                        bus.ReadWord(_tadr + 8),
                        bus.ReadWord(_tadr + 12)
                    };
                    DataConsumer(tagQw, 0, 4);
                }

                // Transfer the QWC data quadwords.
                if (qwc > 0)
                {
                    TransferQws(bus, dataAddr, qwc, out uint newAddr);
                    _madr = newAddr;
                }
                else
                {
                    _madr = dataAddr;
                }
                _qwc   = 0u;
                _tadr  = nextTadr;

                // TIE=1: stop chain when the tag's IRQ bit is set.
                if ((_chcr & CHCR_TIE) != 0 && irq)
                    terminate = true;
            }

            _chcr &= ~CHCR_STR;
            TransferComplete?.Invoke(this, EventArgs.Empty);
        }

        // ── Interleave mode ───────────────────────────────────────────────────

        /// <summary>
        /// Execute an interleave-mode DMA transfer.
        /// Alternates between transferring TQWC quadwords and skipping SQWC quadwords
        /// until all <see cref="Qwc"/> quadwords have been transferred.
        /// <paramref name="sqwc"/> = D_SQWC: bits [7:0] = SQWC (skip count),
        /// bits [23:16] = TQWC (transfer count per slice).
        /// Reference: PCSX2 pcsx2/Dmac.cpp.
        /// </summary>
        private void ExecuteInterleave(PS2MemoryBus bus, uint sqwc)
        {
            uint sqwcCount = sqwc & 0xFFu;           // quadwords to skip per slice
            uint tqwcCount = (sqwc >> 16) & 0xFFu;   // quadwords to transfer per slice

            if (tqwcCount == 0)
            {
                _qwc   = 0u;
                _chcr &= ~CHCR_STR;
                TransferComplete?.Invoke(this, EventArgs.Empty);
                return;
            }

            uint addr      = _madr;
            uint remaining = _qwc;

            while (remaining > 0)
            {
                uint count = remaining < tqwcCount ? remaining : tqwcCount;

                TransferQws(bus, addr, count, out uint newAddr);
                addr       = newAddr;
                remaining -= count;

                // Skip SQWC quadwords between transfer slices.
                if (remaining > 0)
                    addr += sqwcCount * 16u;
            }

            _madr  = addr;
            _qwc   = 0u;
            _chcr &= ~CHCR_STR;
            TransferComplete?.Invoke(this, EventArgs.Empty);
        }

        // ── Shared transfer helper ────────────────────────────────────────────

        /// <summary>
        /// Transfer <paramref name="count"/> quadwords starting at <paramref name="addr"/>
        /// using the channel's direction and DataConsumer/DataProvider callbacks.
        /// Returns the updated address in <paramref name="newAddr"/>.
        /// </summary>
        private void TransferQws(PS2MemoryBus bus, uint addr, uint count, out uint newAddr)
        {
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
                // read each quadword from memory and deliver to the consumer.
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

            newAddr = addr;
        }
    }
}
