using System;

namespace CodeDesigner.Ps2.Emulator.IOP
{
    /// <summary>
    /// Emulates the PS2 IOP DMA Controller (IOP DMAC).
    ///
    /// The IOP DMAC manages 13 independent DMA channels and two sets of global
    /// control registers.  Channels 0–6 are inherited from the original PS1 DMA
    /// controller; channels 7–12 are PS2 additions.
    ///
    /// Channel assignments:
    ///   0  MDECin   0x1F801080  (to MDEC)
    ///   1  MDECout  0x1F801090  (from MDEC)
    ///   2  GPU      0x1F8010A0
    ///   3  CDVD     0x1F8010B0
    ///   4  SPU1     0x1F8010C0
    ///   5  PIO      0x1F8010D0
    ///   6  OTC      0x1F8010E0
    ///   7  SPU2     0x1F801500
    ///   8  DEV9     0x1F801510
    ///   9  SIF0     0x1F801520  (IOP→EE)
    ///   10 SIF1     0x1F801530  (EE→IOP)
    ///   11 SIF2     0x1F801540
    ///   12 cdrom    0x1F801550
    ///
    /// Global registers:
    ///   Channels 0–6:
    ///     0x1F8010F0  DPCR  — DMA Priority Control
    ///     0x1F8010F4  DICR  — DMA Interrupt Control
    ///   Channels 7–12:
    ///     0x1F801570  DPCR2 — DMA Priority Control 2
    ///     0x1F801574  DICR2 — DMA Interrupt Control 2
    ///
    /// DICR / DICR2 bit layout:
    ///   [6:0]    IQN   — force-interrupt bits (write; one per channel)
    ///   [14:7]   —     reserved
    ///   [15]     BusErr — bus error IRQ flag
    ///   [22:16]  IEN   — per-channel IRQ enable (one per channel)
    ///   [23]     MIE   — master IRQ enable
    ///   [30:24]  IQF   — per-channel IRQ flags (W1C — write 1 to clear)
    ///   [31]     MIRQ  — master IRQ signal; set when (IEN &amp; IQF) != 0 and MIE=1
    ///
    /// When a channel completes a transfer, the corresponding IQF bit is set in
    /// DICR/DICR2.  If the matching IEN bit and MIE are both set, the
    /// <see cref="ChannelTransferComplete"/> event is raised so callers (e.g.
    /// <see cref="IopEmulator"/>) can assert INTC bit 3 (IOP DMA interrupt).
    ///
    /// Reference: PCSX2 pcsx2/IopDma.h, pcsx2/IopDma.cpp;
    ///            PS1 Technical Reference Manual.
    /// </summary>
    public sealed class IopDmac
    {
        // ── Channel counts and addresses ──────────────────────────────────────

        /// <summary>Total number of IOP DMA channels.</summary>
        public const int ChannelCount = 13;

        // Channels 0–6 start at 0x1F801080, stride 0x10.
        // Channels 7–12 start at 0x1F801500, stride 0x10.
        private static readonly uint[] ChannelBases = new uint[ChannelCount];

        static IopDmac()
        {
            for (int i = 0; i < 7; i++)
                ChannelBases[i] = 0x1F801080u + (uint)(i * 0x10);
            for (int i = 7; i < 13; i++)
                ChannelBases[i] = 0x1F801500u + (uint)((i - 7) * 0x10);
        }

        // Each channel's I/O window is 0x10 bytes (3 registers × 4 bytes each).
        private const uint ChannelWindowSize = 0x10u;

        // ── Global register addresses ─────────────────────────────────────────

        /// <summary>Address of DPCR (DMA Priority Control, channels 0–6).</summary>
        public const uint AddrDpcr  = 0x1F8010F0u;

        /// <summary>Address of DICR (DMA Interrupt Control, channels 0–6).</summary>
        public const uint AddrDicr  = 0x1F8010F4u;

        /// <summary>Address of DPCR2 (DMA Priority Control 2, channels 7–12).</summary>
        public const uint AddrDpcr2 = 0x1F801570u;

        /// <summary>Address of DICR2 (DMA Interrupt Control 2, channels 7–12).</summary>
        public const uint AddrDicr2 = 0x1F801574u;

        // ── DICR bit masks ────────────────────────────────────────────────────
        private const uint DICR_MASTER_ENABLE = 1u << 23;  // MIE — master IRQ enable
        private const uint DICR_MASTER_FLAG   = 1u << 31;  // MIRQ — master IRQ signal
        private const uint DICR_IEN_SHIFT     = 16;        // per-channel enable bits [22:16]
        private const uint DICR_IQF_SHIFT     = 24;        // per-channel flag bits  [30:24]
        private const uint DICR_IEN_MASK      = 0x007Fu;   // 7 channels per DICR
        private const uint DICR_IQF_MASK      = 0x7Fu;

        // ── Channels ─────────────────────────────────────────────────────────

        /// <summary>The 13 IOP DMA channels (indices 0–12).</summary>
        public IopDmacChannel[] Channels { get; } = new IopDmacChannel[ChannelCount];

        // ── Global register state ─────────────────────────────────────────────
        private uint _dpcr;
        private uint _dicr;
        private uint _dpcr2;
        private uint _dicr2;

        /// <summary>DPCR register value (channels 0–6 priority control).</summary>
        public uint Dpcr  => _dpcr;

        /// <summary>DICR register value (channels 0–6 interrupt control).</summary>
        public uint Dicr  => _dicr;

        /// <summary>DPCR2 register value (channels 7–12 priority control).</summary>
        public uint Dpcr2 => _dpcr2;

        /// <summary>DICR2 register value (channels 7–12 interrupt control).</summary>
        public uint Dicr2 => _dicr2;

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Raised when a DMA channel completes a transfer and both its per-channel
        /// IRQ enable bit (IEN) and the master IRQ enable (MIE) are set in
        /// DICR/DICR2.  The event argument is the 0-based channel index (0–12).
        /// </summary>
        public event EventHandler<int>? ChannelTransferComplete;

        // ─────────────────────────────────────────────────────────────────────

        public IopDmac()
        {
            for (int i = 0; i < ChannelCount; i++)
            {
                var ch = new IopDmacChannel(i);
                Channels[i] = ch;
                int idx = i; // capture for closure
                ch.TransferComplete += (_, _) => OnChannelComplete(idx);
            }
        }

        // ── Address decoding ──────────────────────────────────────────────────

        /// <summary>
        /// Try to decode a physical address into a channel index and register offset.
        /// Returns <c>true</c> when the address falls within a channel's 0x10-byte
        /// window and sets <paramref name="channelIndex"/> / <paramref name="regOffset"/>.
        /// </summary>
        public static bool TryDecodeChannelAddress(uint addr,
            out int channelIndex, out uint regOffset)
        {
            for (int i = 0; i < ChannelCount; i++)
            {
                uint chBase = ChannelBases[i];
                if (addr >= chBase && addr < chBase + ChannelWindowSize)
                {
                    channelIndex = i;
                    regOffset    = addr - chBase;
                    return true;
                }
            }
            channelIndex = -1;
            regOffset    = 0u;
            return false;
        }

        /// <summary>
        /// Returns true when <paramref name="addr"/> is one of the four global
        /// DMAC control register addresses (DPCR, DICR, DPCR2, DICR2).
        /// </summary>
        public static bool IsGlobalRegister(uint addr)
            => addr == AddrDpcr || addr == AddrDicr
            || addr == AddrDpcr2 || addr == AddrDicr2;

        // ── Global register I/O ───────────────────────────────────────────────

        /// <summary>Read one of the four global DMAC registers by address.</summary>
        public uint ReadGlobal(uint addr)
        {
            if (addr == AddrDpcr)  return _dpcr;
            if (addr == AddrDicr)  return _dicr;
            if (addr == AddrDpcr2) return _dpcr2;
            if (addr == AddrDicr2) return _dicr2;
            return 0u;
        }

        /// <summary>
        /// Write one of the four global DMAC registers by address.
        ///
        /// For DICR/DICR2: bits [30:24] (IQF, per-channel IRQ flags) are W1C —
        /// writing a 1 clears the corresponding flag.  All other bits are plain R/W.
        /// </summary>
        public void WriteGlobal(uint addr, uint value)
        {
            if (addr == AddrDpcr)  { _dpcr  = value;               return; }
            if (addr == AddrDpcr2) { _dpcr2 = value;               return; }
            if (addr == AddrDicr)  { WriteDicr(ref _dicr,  value); return; }
            if (addr == AddrDicr2) { WriteDicr(ref _dicr2, value); }
        }

        // ── DMA execution ─────────────────────────────────────────────────────

        /// <summary>
        /// Execute any pending transfer on channel <paramref name="channelIndex"/>
        /// using <paramref name="bus"/> for memory access.  This should be called
        /// by <see cref="IopMemoryBus"/> immediately after CHCR is written with STR=1.
        /// </summary>
        public void ExecuteChannel(int channelIndex, IopMemoryBus bus)
        {
            if ((uint)channelIndex >= (uint)ChannelCount) return;
            Channels[channelIndex].Execute(bus);
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        private void OnChannelComplete(int idx)
        {
            // Select DICR vs DICR2 and the local bit index.
            bool isLow    = idx < 7;
            int  localIdx = isLow ? idx : idx - 7;

            ref uint dicr = ref (isLow ? ref _dicr : ref _dicr2);

            // Set the IRQ flag bit for this channel.
            dicr |= 1u << (int)(DICR_IQF_SHIFT + localIdx);

            // Update the computed master-flag bit.
            UpdateMasterFlag(ref dicr);

            // Raise the event only when both per-channel IEN and master MIE are set.
            bool ien = (dicr & (1u << (int)(DICR_IEN_SHIFT + localIdx))) != 0;
            bool mie = (dicr & DICR_MASTER_ENABLE) != 0;
            if (ien && mie)
                ChannelTransferComplete?.Invoke(this, idx);
        }

        /// <summary>
        /// Apply a write to a DICR/DICR2 register.
        /// Bits [30:24] (IQF) are W1C: writing a 1 clears the flag.
        /// All other bits are plain R/W.
        /// </summary>
        private static void WriteDicr(ref uint dicr, uint value)
        {
            // Current IRQ flags (bits [30:24]).
            uint currentFlags = (dicr >> (int)DICR_IQF_SHIFT) & DICR_IQF_MASK;
            // Bits written as 1 in value[30:24] clear the corresponding flag.
            uint clearMask    = (value >> (int)DICR_IQF_SHIFT) & DICR_IQF_MASK;
            uint newFlags     = currentFlags & ~clearMask;

            // Bits [23:0] are plain R/W; write the value directly.
            dicr = (value & 0x00FFFFFFu) | (newFlags << (int)DICR_IQF_SHIFT);
            UpdateMasterFlag(ref dicr);
        }

        /// <summary>
        /// Recompute the master IRQ flag bit (bit 31) from the current IEN, IQF,
        /// and MIE fields.  Bit 31 is set when MIE=1 and at least one channel has
        /// both IEN and IQF set.
        /// </summary>
        private static void UpdateMasterFlag(ref uint dicr)
        {
            uint ien = (dicr >> (int)DICR_IEN_SHIFT) & DICR_IEN_MASK;
            uint iqf = (dicr >> (int)DICR_IQF_SHIFT) & DICR_IQF_MASK;
            bool mie = (dicr & DICR_MASTER_ENABLE) != 0;

            if (mie && (ien & iqf) != 0)
                dicr |= DICR_MASTER_FLAG;
            else
                dicr &= ~DICR_MASTER_FLAG;
        }
    }
}
