using System;

namespace CodeDesigner.Mips64LE.Common.Emulator.Hardware
{
    /// <summary>
    /// Emulates the PS2 EE DMA Controller (DMAC).
    ///
    /// The DMAC manages ten independent DMA channels and a set of global control
    /// registers.  Software programs a channel by writing MADR, QWC, and finally
    /// CHCR with STR=1; the DMAC then performs the transfer without CPU involvement.
    ///
    /// Channel base addresses (physical):
    ///   0x10008000  CH0 VIF0
    ///   0x10009000  CH1 VIF1
    ///   0x1000A000  CH2 GIF
    ///   0x1000B000  CH3 IPU0 (fromIPU)
    ///   0x1000B400  CH4 IPU1 (toIPU)
    ///   0x1000C000  CH5 SIF0
    ///   0x1000C400  CH6 SIF1
    ///   0x1000C800  CH7 SIF2
    ///   0x1000D000  CH8 SPR0 (fromSPR)
    ///   0x1000D400  CH9 SPR1 (toSPR)
    ///
    /// Global registers (base 0x1000E000):
    ///   +0x00  D_CTRL  — global enable and stall control
    ///   +0x10  D_STAT  — per-channel interrupt status / enable
    ///   +0x20  D_PCR   — priority control
    ///   +0x30  D_SQWC  — interleave size
    ///   +0x40  D_RBSR  — ring-buffer size
    ///   +0x50  D_RBOR  — ring-buffer offset
    ///   +0x60  D_STADR — stall address
    ///
    /// D_STAT bit layout (reference: PCSX2 pcsx2/Dmac.h):
    ///   Bits  [9:0]   — per-channel interrupt pending (W1C)
    ///   Bit   [15]    — bus error interrupt status (W1C)
    ///   Bits [25:16]  — per-channel interrupt enable (toggle on write)
    ///   Bit   [31]    — master error interrupt status (W1C)
    ///
    /// When a channel completes and its D_STAT enable bit ([16+ch]) is set the
    /// <see cref="ChannelTransferComplete"/> event is raised.  Callers (e.g.
    /// <see cref="PS2Emulator"/>) subscribe to this event to deliver the interrupt
    /// to the CPU.
    ///
    /// Reference: PCSX2 pcsx2/Dmac.h, pcsx2/Dmac.cpp.
    /// </summary>
    public sealed class DMAC
    {
        // ── Global register offsets (from 0x1000E000) ─────────────────────────
        public const uint REG_CTRL  = 0x00u;
        public const uint REG_STAT  = 0x10u;
        public const uint REG_PCR   = 0x20u;
        public const uint REG_SQWC  = 0x30u;
        public const uint REG_RBSR  = 0x40u;
        public const uint REG_RBOR  = 0x50u;
        public const uint REG_STADR = 0x60u;

        // ── Channel physical base addresses ───────────────────────────────────
        private static readonly uint[] ChannelBases =
        {
            0x10008000u,  // 0: VIF0
            0x10009000u,  // 1: VIF1
            0x1000A000u,  // 2: GIF
            0x1000B000u,  // 3: IPU0 (fromIPU)
            0x1000B400u,  // 4: IPU1  (toIPU)
            0x1000C000u,  // 5: SIF0
            0x1000C400u,  // 6: SIF1
            0x1000C800u,  // 7: SIF2
            0x1000D000u,  // 8: SPR0 (fromSPR)
            0x1000D400u,  // 9: SPR1  (toSPR)
        };

        /// <summary>Total number of DMA channels.</summary>
        public const int ChannelCount = 10;

        /// <summary>The ten DMA channels (index 0–9).</summary>
        public DmacChannel[] Channels { get; } = new DmacChannel[ChannelCount];

        // ── Global register state ─────────────────────────────────────────────
        private uint _ctrl;
        private uint _stat;
        private uint _pcr;
        private uint _sqwc;
        private uint _rbsr;
        private uint _rbor;
        private uint _stadr;

        /// <summary>D_CTRL register value.</summary>
        public uint Ctrl => _ctrl;

        /// <summary>D_STAT register (pending and enable bits for all channels).</summary>
        public uint Stat => _stat;

        /// <summary>
        /// Raised when a DMA channel completes a transfer and its D_STAT enable bit
        /// is set.  The event argument is the 0-based channel index (0–9).
        /// </summary>
        public event EventHandler<int>? ChannelTransferComplete;

        // ─────────────────────────────────────────────────────────────────────

        public DMAC()
        {
            for (int i = 0; i < ChannelCount; i++)
            {
                var ch = new DmacChannel(i);
                Channels[i] = ch;
                int idx = i; // capture for closure
                ch.TransferComplete += (_, _) => OnChannelComplete(idx);
            }
        }

        // ── Channel address decoding ──────────────────────────────────────────

        /// <summary>
        /// Try to decode a physical address into a channel index and register offset.
        /// Each channel occupies a 0x100-byte window at its base address.
        /// Returns <c>true</c> and sets <paramref name="channelIndex"/> /
        /// <paramref name="regOffset"/> when the address belongs to a channel.
        /// </summary>
        public static bool TryDecodeChannelAddress(uint paddr,
            out int channelIndex, out uint regOffset)
        {
            for (int i = 0; i < ChannelBases.Length; i++)
            {
                uint chBase = ChannelBases[i];
                if (paddr >= chBase && paddr < chBase + 0x100u)
                {
                    channelIndex = i;
                    regOffset    = paddr - chBase;
                    return true;
                }
            }
            channelIndex = -1;
            regOffset    = 0u;
            return false;
        }

        /// <summary>
        /// Returns true when <paramref name="paddr"/> lies in the global DMAC
        /// register window (0x1000E000–0x1000E0FF).
        /// </summary>
        public static bool IsGlobalRegion(uint paddr)
            => paddr >= 0x1000E000u && paddr < 0x1000E100u;

        // ── Global register I/O ───────────────────────────────────────────────

        /// <summary>Read a 32-bit global DMAC register.</summary>
        public uint ReadGlobal(uint offset)
        {
            return (offset & 0xF0u) switch
            {
                0x00u => _ctrl,
                0x10u => _stat,
                0x20u => _pcr,
                0x30u => _sqwc,
                0x40u => _rbsr,
                0x50u => _rbor,
                0x60u => _stadr,
                _     => 0u
            };
        }

        /// <summary>
        /// Write a 32-bit global DMAC register.
        /// D_STAT lower 16 bits: write-1-to-clear (pending bits).
        /// D_STAT upper 16 bits: write-1-to-toggle (enable bits).
        /// </summary>
        public void WriteGlobal(uint offset, uint value)
        {
            switch (offset & 0xF0u)
            {
                case 0x00u: _ctrl  = value; break;
                case 0x10u:
                    // Lower 16 bits are W1C (channel interrupt pending);
                    // upper 16 bits are toggle (channel interrupt enable).
                    _stat = (_stat & ~(value & 0x0000FFFFu))   // W1C lower half
                          ^ (value & 0xFFFF0000u);              // toggle upper half
                    break;
                case 0x20u: _pcr   = value; break;
                case 0x30u: _sqwc  = value; break;
                case 0x40u: _rbsr  = value; break;
                case 0x50u: _rbor  = value; break;
                case 0x60u: _stadr = value; break;
            }
        }

        // ── DMA execution ─────────────────────────────────────────────────────

        /// <summary>
        /// Execute any pending transfer on channel <paramref name="channelIndex"/>
        /// using <paramref name="bus"/> for memory access.  This should be called
        /// by <see cref="PS2MemoryBus"/> immediately after CHCR is written with
        /// STR=1.
        /// </summary>
        public void ExecuteChannel(int channelIndex, PS2MemoryBus bus)
        {
            if ((uint)channelIndex >= (uint)ChannelCount) return;
            Channels[channelIndex].Execute(bus);
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        private void OnChannelComplete(int idx)
        {
            // Always set the D_STAT pending bit.
            _stat |= 1u << idx;

            // Only raise the event when the corresponding enable bit is also set.
            bool enabled = (_stat & (1u << (idx + 16))) != 0;
            if (enabled)
                ChannelTransferComplete?.Invoke(this, idx);
        }
    }
}
