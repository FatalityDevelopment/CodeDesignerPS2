using System;
using CodeDesigner.Ps2.Common.Interfaces;

namespace CodeDesigner.Ps2.EmulatorCore.Hardware
{
    /// <summary>
    /// Emulates the PS2 GIF (Graphics Interface) controller.
    ///
    /// The GIF sits between the EE and the GS (Graphics Synthesizer) and
    /// processes three data paths:
    ///   PATH1 — from VU1 via the XGKICK instruction (highest priority)
    ///   PATH2 — from VIF1 via the DIRECT/DIRECTHL VIF commands
    ///   PATH3 — from the DMAC channel 2 (lowest priority)
    ///
    /// Each path delivers a stream of 128-bit quadwords that begin with a GIFtag
    /// followed by NLOOP × data packets.  The GIF decodes each tag and forwards
    /// register write commands to the attached <see cref="GS"/> instance.
    ///
    /// GIFtag structure (128 bits = 4 × uint32):
    ///   [14: 0]   NLOOP — number of data packets (loops)
    ///   [15]      EOP   — end of packet (last tag in the transfer)
    ///   [45:16]   (reserved)
    ///   [46]      PRE   — if set, write PRIM register before the data loop
    ///   [57:47]   PRIM  — primitive descriptor written if PRE=1
    ///   [59:58]   FLG   — transfer mode (0=PACKED, 1=REGLIST, 2/3=IMAGE)
    ///   [63:60]   NREG  — number of register descriptors (0 = 16)
    ///   [127:64]  REGS  — up to 16 × 4-bit GS register IDs (NREG entries used)
    ///
    /// Transfer modes:
    ///   PACKED  (FLG=0): NLOOP × NREG × 128-bit qwords, one qword per register.
    ///   REGLIST (FLG=1): NLOOP × ⌈NREG/2⌉ qwords, two 64-bit values per qword.
    ///   IMAGE   (FLG=2/3): NLOOP × 128-bit qwords of raw pixel data → GS HWREG.
    ///
    /// Memory-mapped registers (physical 0x10003000 + offset):
    ///   +0x00  GIF_CTRL  — reset (bit 0) and pause (bit 3) control
    ///   +0x10  GIF_MODE  — PATH3 mask (bit 0), intermittent mode (bit 2)
    ///   +0x20  GIF_STAT  — status (read-only; limited subset implemented)
    ///   +0x40  GIF_TAG0  — current GIFtag [31: 0] (read-only)
    ///   +0x50  GIF_TAG1  — current GIFtag [63:32] (read-only)
    ///   +0x60  GIF_TAG2  — current GIFtag [95:64] (read-only)
    ///   +0x70  GIF_TAG3  — current GIFtag [127:96] (read-only)
    ///   +0x80  GIF_CNT   — remaining qwords in current burst (read-only)
    ///   +0x90  GIF_P3CNT — PATH3 remaining qwords (read-only)
    ///   +0xA0  GIF_P3TAG — PATH3 first tag word (read-only)
    ///
    /// Reference: PS2 EE User's Manual §4.2 (GIF); PCSX2 pcsx2/Gif.h, pcsx2/Gif.cpp.
    /// </summary>
    public sealed class GIF : IGifDataSink
    {
        // ── Register offsets (byte offset from GIF physical base 0x10003000) ───
        public const uint REG_CTRL  = 0x00u;
        public const uint REG_MODE  = 0x10u;
        public const uint REG_STAT  = 0x20u;
        public const uint REG_TAG0  = 0x40u;
        public const uint REG_TAG1  = 0x50u;
        public const uint REG_TAG2  = 0x60u;
        public const uint REG_TAG3  = 0x70u;
        public const uint REG_CNT   = 0x80u;
        public const uint REG_P3CNT = 0x90u;
        public const uint REG_P3TAG = 0xA0u;

        // ── CTRL bit masks ────────────────────────────────────────────────────
        private const uint CTRL_RST = 1u << 0;  // soft reset GIF
        private const uint CTRL_PSE = 1u << 3;  // pause GIF

        // ── MODE bit masks ────────────────────────────────────────────────────
        private const uint MODE_M3R = 1u << 0;  // mask PATH3 transfers
        private const uint MODE_IMT = 1u << 2;  // intermittent transfer mode

        // ── STAT bit masks ────────────────────────────────────────────────────
        private const uint STAT_M3P  = 1u << 0;   // PATH3 masked by MASK register
        private const uint STAT_M3R  = 1u << 1;   // PATH3 masked by RGBAQ register
        private const uint STAT_IMT  = 1u << 2;   // intermittent mode active
        private const uint STAT_PSE  = 1u << 3;   // pause active
        private const uint STAT_IP3  = 1u << 5;   // PATH3 transfer in progress
        private const uint STAT_P3Q  = 1u << 6;   // PATH3 data queued
        private const uint STAT_P2Q  = 1u << 7;   // PATH2 data queued
        private const uint STAT_P1Q  = 1u << 8;   // PATH1 data queued
        private const uint STAT_OPH  = 1u << 9;   // output path held
        private const uint STAT_APATH_SHIFT = 10; // active path field [11:10]
        private const uint STAT_DIR  = 1u << 12;  // direction
        private const uint STAT_FQC_SHIFT = 24;   // FIFO qword count [28:24]

        // ── GIFtag FLG field values ───────────────────────────────────────────
        private const int FLG_PACKED  = 0;
        private const int FLG_REGLIST = 1;
        private const int FLG_IMAGE   = 2;

        // ── Register state ────────────────────────────────────────────────────
        private uint _ctrl;
        private uint _mode;

        // Current GIFtag (last decoded) — exposed as TAG0/TAG1/TAG2/TAG3.
        private uint _tag0, _tag1, _tag2, _tag3;

        // Remaining qwords in the current active burst.
        private uint _cnt;

        // PATH3 remaining qwords + first tag word.
        private uint _p3cnt;
        private uint _p3tag;

        /// <summary>The GS instance that receives register writes from GIFtag processing.</summary>
        public GS? Gs { get; set; }

        // ── Register access ───────────────────────────────────────────────────

        /// <summary>Read a 32-bit GIF register at the given byte offset from the GIF base.</summary>
        public uint ReadRegister(uint offset)
        {
            return (offset & 0xF0u) switch
            {
                0x00u => _ctrl,
                0x10u => _mode,
                0x20u => BuildStat(),
                0x40u => _tag0,
                0x50u => _tag1,
                0x60u => _tag2,
                0x70u => _tag3,
                0x80u => _cnt,
                0x90u => _p3cnt,
                0xA0u => _p3tag,
                _     => 0u
            };
        }

        /// <summary>Write a 32-bit GIF register at the given byte offset from the GIF base.</summary>
        public void WriteRegister(uint offset, uint value)
        {
            switch (offset & 0xF0u)
            {
                case 0x00u:
                    // CTRL: RST clears all state; PSE is latched.
                    if ((value & CTRL_RST) != 0)
                        Reset();
                    else
                        _ctrl = value & CTRL_PSE; // only PSE bit is preserved after write
                    break;

                case 0x10u:
                    _mode = value & (MODE_M3R | MODE_IMT);
                    break;

                // All other registers are read-only; writes are silently ignored.
            }
        }

        // ── Data feeds (from PATH1/PATH2/PATH3) ───────────────────────────────

        /// <summary>
        /// Feed a stream of 32-bit words from PATH3 (DMAC channel 2) or PATH2
        /// (VIF1 DIRECT/DIRECTHL command) into the GIF for processing.
        ///
        /// Each call delivers a complete transfer burst: one or more consecutive
        /// GIFtags followed immediately by their associated data qwords.
        /// The words must be aligned to 128-bit (4 × uint32) boundaries.
        /// </summary>
        public void Feed(ReadOnlySpan<uint> words)
        {
            if (Gs == null) return;
            int pos = 0;
            while (pos + 4 <= words.Length)
                pos = ProcessTag(words, pos);
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        private void Reset()
        {
            _ctrl  = 0u;
            _mode  = 0u;
            _tag0  = 0u; _tag1 = 0u; _tag2 = 0u; _tag3 = 0u;
            _cnt   = 0u;
            _p3cnt = 0u;
            _p3tag = 0u;
        }

        private uint BuildStat()
        {
            uint s = 0u;
            if ((_mode & MODE_M3R) != 0) s |= STAT_M3P;
            if ((_mode & MODE_IMT) != 0) s |= STAT_IMT;
            if ((_ctrl & CTRL_PSE) != 0) s |= STAT_PSE;
            return s;
        }

        /// <summary>
        /// Decode a GIFtag starting at <paramref name="pos"/> in <paramref name="words"/>,
        /// consume all associated data qwords, and return the new position.
        /// </summary>
        private int ProcessTag(ReadOnlySpan<uint> words, int pos)
        {
            if (pos + 4 > words.Length) return words.Length;

            // ── Decode GIFtag fields ──────────────────────────────────────────
            uint w0 = words[pos];     // bits [31: 0]
            uint w1 = words[pos + 1]; // bits [63:32]
            uint w2 = words[pos + 2]; // bits [95:64]  = REGS[31: 0]
            uint w3 = words[pos + 3]; // bits [127:96] = REGS[63:32]
            pos += 4;

            uint nloop  = w0 & 0x7FFFu;              // bits [14:0]
            // EOP = (w0 >> 15) & 1 — noted but not needed for minimal emulation
            bool pre    = ((w1 >> 14) & 1u) != 0;   // bit 46
            uint prim   = (w1 >> 15) & 0x7FFu;      // bits [57:47]
            int  flg    = (int)((w1 >> 26) & 3u);   // bits [59:58]
            int  nreg   = (int)((w1 >> 28) & 0xFu); // bits [63:60]
            if (nreg == 0) nreg = 16;

            ulong regs = w2 | ((ulong)w3 << 32);    // bits [127:64]

            // Latch current tag into read-only TAG registers.
            _tag0 = w0; _tag1 = w1; _tag2 = w2; _tag3 = w3;

            // ── PRE — write PRIM register before processing data ──────────────
            if (pre && Gs != null)
                Gs.WriteInternal(GS.REG_PRIM, prim);

            if (nloop == 0) return pos;

            // ── Dispatch by FLG ───────────────────────────────────────────────
            return flg switch
            {
                FLG_PACKED  => ProcessPacked(words, pos, nloop, nreg, regs),
                FLG_REGLIST => ProcessReglist(words, pos, nloop, nreg, regs),
                _           => ProcessImage(words, pos, nloop),   // IMAGE (2 or 3)
            };
        }

        /// <summary>
        /// PACKED mode: NLOOP × NREG qwords.  Each 128-bit qword maps to one GS register.
        /// The register ID cycles through the REGS descriptor array.
        /// </summary>
        private int ProcessPacked(ReadOnlySpan<uint> words, int pos, uint nloop, int nreg, ulong regs)
        {
            for (uint l = 0; l < nloop; l++)
            {
                for (int r = 0; r < nreg; r++)
                {
                    if (pos + 4 > words.Length) return pos;

                    byte regId = (byte)((regs >> (r * 4)) & 0xFu);
                    ulong lo = words[pos] | ((ulong)words[pos + 1] << 32);
                    ulong hi = words[pos + 2] | ((ulong)words[pos + 3] << 32);
                    pos += 4;

                    Gs!.WritePacked(regId, lo, hi);
                }
            }
            return pos;
        }

        /// <summary>
        /// REGLIST mode: NLOOP × ⌈NREG/2⌉ qwords.
        /// Each 128-bit qword holds two consecutive 64-bit GS register values.
        /// When NREG is odd the high half of the last qword in each loop is padding.
        /// </summary>
        private int ProcessReglist(ReadOnlySpan<uint> words, int pos, uint nloop, int nreg, ulong regs)
        {
            for (uint l = 0; l < nloop; l++)
            {
                for (int r = 0; r < nreg; r += 2)
                {
                    if (pos + 4 > words.Length) return pos;

                    byte regA = (byte)((regs >> (r * 4)) & 0xFu);
                    ulong qlo = words[pos] | ((ulong)words[pos + 1] << 32);
                    ulong qhi = words[pos + 2] | ((ulong)words[pos + 3] << 32);
                    pos += 4;

                    Gs!.WriteInternal(regA, qlo);

                    if (r + 1 < nreg)
                    {
                        byte regB = (byte)((regs >> ((r + 1) * 4)) & 0xFu);
                        Gs.WriteInternal(regB, qhi);
                    }
                    // Odd-NREG: qhi is padding, silently discard.
                }
            }
            return pos;
        }

        /// <summary>
        /// IMAGE mode: NLOOP qwords of raw pixel data forwarded to GS HWREG (0x54).
        /// Each 128-bit qword is split into two 64-bit HWREG writes.
        /// </summary>
        private int ProcessImage(ReadOnlySpan<uint> words, int pos, uint nloop)
        {
            for (uint l = 0; l < nloop; l++)
            {
                if (pos + 4 > words.Length) return pos;

                ulong lo = words[pos] | ((ulong)words[pos + 1] << 32);
                ulong hi = words[pos + 2] | ((ulong)words[pos + 3] << 32);
                pos += 4;

                Gs!.WriteInternal(GS.REG_HWREG, lo);
                Gs.WriteInternal(GS.REG_HWREG, hi);
            }
            return pos;
        }
    }
}
