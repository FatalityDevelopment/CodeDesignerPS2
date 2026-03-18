using System;

namespace CodeDesigner.Ps2.Emulator.IOP
{
    /// <summary>
    /// Emulates one IOP DMA channel.
    ///
    /// Each channel exposes three memory-mapped 32-bit registers:
    ///   +0x00  D_MADR  — memory address register (word-aligned)
    ///   +0x04  D_BCR   — block count/size register
    ///                      Burst mode (SYNC=0): BCR[15:0] = word count (0 ≡ 0x10000 words)
    ///   +0x08  D_CHCR  — channel control register
    ///
    /// CHCR bit layout (IOP / R3000A DMA, reference: PCSX2 pcsx2/IopDma.h):
    ///   [0]    DIR     — direction (0 = RAM→device/FIFO, 1 = device/FIFO→RAM)
    ///   [1]    COPC    — chopping DMA enable
    ///   [9:8]  SYNC    — sync mode (00 = burst/manual, 01 = slice, 10 = linked-list)
    ///   [24]   STR     — start/busy (write 1 to start; hardware clears when done)
    ///   [28]   TRIGGER — force-trigger (for slice mode)
    ///
    /// Normal (burst/manual) transfers are executed synchronously by
    /// <see cref="Execute"/> as soon as STR=1 is detected.  Slice and linked-list
    /// modes are accepted by the register write but execute as a no-op so BIOS probe
    /// code does not hang.
    ///
    /// Reference: PCSX2 pcsx2/IopDma.h, pcsx2/IopDma.cpp; PS1 Technical Reference Manual.
    /// </summary>
    public sealed class IopDmacChannel
    {
        // ── Register offsets within the channel's 0x10-byte I/O window ─────────
        public const uint REG_MADR = 0x00u;
        public const uint REG_BCR  = 0x04u;
        public const uint REG_CHCR = 0x08u;

        // ── CHCR bit masks ────────────────────────────────────────────────────
        private const uint CHCR_DIR     = 1u << 0;   // transfer direction
        private const uint CHCR_SYNC    = 3u << 8;   // sync mode bits [9:8]
        private const uint CHCR_STR     = 1u << 24;  // start / busy
        private const uint CHCR_TRIGGER = 1u << 28;  // force trigger

        /// <summary>0-based channel index (0 = MDECin … 12 = cdrom).</summary>
        public int ChannelId { get; }

        private uint _madr;
        private uint _bcr;
        private uint _chcr;

        /// <summary>Raised when a DMA transfer completes on this channel.</summary>
        public event EventHandler? TransferComplete;

        /// <summary>
        /// Optional callback invoked once per word during a device→RAM burst transfer.
        /// When <c>null</c>, the channel zero-fills the destination region (default
        /// behaviour when no device FIFO is wired to the channel).
        /// Set by <see cref="IopMemoryBus"/> to feed CDVD sector data into channel 3.
        /// </summary>
        public Func<uint>? DeviceDataProvider { get; set; }

        /// <summary>
        /// Optional callback invoked once per word during a RAM→device burst transfer.
        /// Receives each 32-bit word read from IOP RAM so the device can store it.
        /// When <c>null</c>, RAM→device transfers advance MADR without forwarding data.
        /// Set by <see cref="IopMemoryBus"/> to stream IOP RAM audio data to SPU2
        /// on channels 4 (SPU1) and 7 (SPU2).
        /// </summary>
        public Action<uint>? DeviceDataConsumer { get; set; }

        public IopDmacChannel(int id) => ChannelId = id;

        // ── Register access ───────────────────────────────────────────────────

        /// <summary>Read a 32-bit register at <paramref name="regOffset"/> (0x00/0x04/0x08).</summary>
        public uint ReadRegister(uint regOffset) => regOffset switch
        {
            REG_MADR => _madr,
            REG_BCR  => _bcr,
            REG_CHCR => _chcr,
            _        => 0u
        };

        /// <summary>Write a 32-bit register at <paramref name="regOffset"/>.</summary>
        public void WriteRegister(uint regOffset, uint value)
        {
            switch (regOffset)
            {
                case REG_MADR: _madr = value; break;
                case REG_BCR:  _bcr  = value; break;
                case REG_CHCR: _chcr = value; break;
            }
        }

        // ── CHCR field accessors ──────────────────────────────────────────────

        /// <summary>True when the channel is active (STR=1).</summary>
        public bool IsRunning => (_chcr & CHCR_STR) != 0;

        /// <summary>Transfer direction: false = RAM→device, true = device→RAM.</summary>
        public bool DirectionToMemory => (_chcr & CHCR_DIR) != 0;

        /// <summary>Sync mode: 0 = burst, 1 = slice, 2 = linked-list.</summary>
        public int SyncMode => (int)((_chcr & CHCR_SYNC) >> 8);

        /// <summary>Current MADR value.</summary>
        public uint Madr => _madr;

        /// <summary>Current BCR value.</summary>
        public uint Bcr  => _bcr;

        // ── Transfer execution ────────────────────────────────────────────────

        /// <summary>
        /// Execute a burst-mode DMA transfer.  BCR[15:0] gives the word count
        /// (0 is treated as 0x10000 words).  For linked-list mode (SYNC=2) a
        /// GPU-style linked list is traversed: each node header is a 32-bit word
        /// whose upper 8 bits are the data word count and lower 24 bits are the
        /// address of the next node (0xFFFFFF = end of list).
        ///
        /// <list type="bullet">
        ///   <item><term>DIR=0 (RAM→device)</term>
        ///     <description>Advance MADR by wordCount×4; data is consumed from RAM.
        ///     </description></item>
        ///   <item><term>DIR=1 (device→RAM)</term>
        ///     <description>Zero-fill wordCount×4 bytes at MADR (no real device FIFO
        ///     data in this emulator), then advance MADR.</description></item>
        /// </list>
        ///
        /// Slice mode (SYNC=1) is not yet fully implemented;
        /// the method clears STR immediately so BIOS probe loops do not spin forever.
        ///
        /// <see cref="TransferComplete"/> is raised whenever STR is cleared so
        /// the parent <see cref="IopDmac"/> can update DICR/DICR2.
        /// Does nothing when the channel is not running.
        /// </summary>
        internal void Execute(IopMemoryBus bus)
        {
            if (!IsRunning)
                return;

            switch (SyncMode)
            {
                case 0: ExecuteBurst(bus);      break;
                case 2: ExecuteLinkedList(bus); break;
                default:
                    // Slice mode: not yet implemented; clear STR to avoid spin-loops.
                    _chcr &= ~CHCR_STR;
                    TransferComplete?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        private void ExecuteBurst(IopMemoryBus bus)
        {
            // Burst mode: BCR[15:0] = word count; 0 means 0x10000 words.
            uint wordCount = _bcr & 0xFFFFu;
            if (wordCount == 0) wordCount = 0x10000u;

            uint addr = _madr;

            if (DirectionToMemory)
            {
                // Device→RAM: pull data from the device's FIFO via the callback,
                // or zero-fill if no device is wired to this channel.
                for (uint w = 0; w < wordCount; w++)
                {
                    uint data = DeviceDataProvider?.Invoke() ?? 0u;
                    bus.WriteWord(addr, data);
                    addr += 4u;
                }
            }
            else
            {
                // RAM→Device: read from IOP RAM and forward each word to the
                // device consumer (e.g. SPU2 audio RAM) if one is wired up.
                for (uint w = 0; w < wordCount; w++)
                {
                    uint data = bus.ReadWord(addr);
                    DeviceDataConsumer?.Invoke(data);
                    addr += 4u;
                }
            }

            _madr  = addr;
            _bcr  &= 0xFFFF0000u; // clear the word-count half; preserve block-count
            _chcr &= ~CHCR_STR;
            TransferComplete?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Execute a linked-list DMA transfer (SYNC=2).
        /// Used by IOP GPU (channel 2) and MDEC (channel 4).
        /// Each node: bits[31:24] = data word count, bits[23:0] = address of next node.
        /// 0xFFFFFF in the address field marks the end of the list.
        /// Reference: PCSX2 pcsx2/IopDma.cpp.
        /// </summary>
        private void ExecuteLinkedList(IopMemoryBus bus)
        {
            const uint EndMarker = 0x00FFFFFFu;
            uint addr = _madr;

            while (true)
            {
                uint header    = bus.ReadWord(addr);
                uint wordCount = (header >> 24) & 0xFFu;
                uint nextAddr  = header & 0x00FFFFFFu;

                addr += 4u; // advance past header

                // Forward data words to the device consumer.
                for (uint w = 0; w < wordCount; w++)
                {
                    uint data = bus.ReadWord(addr);
                    DeviceDataConsumer?.Invoke(data);
                    addr += 4u;
                }

                if (nextAddr == EndMarker)
                    break;

                addr = nextAddr;
            }

            _madr  = addr;
            _chcr &= ~CHCR_STR;
            TransferComplete?.Invoke(this, EventArgs.Empty);
        }
    }
}
