using System;

namespace CodeDesigner.Ps2.EmulatorCore.Hardware
{
    /// <summary>
    /// Emulates a PS2 VIF (Vector Interface) unit — either VIF0 or VIF1.
    ///
    /// The VIF sits between the EE DMA controller and a Vector Unit (VU0 or VU1).
    /// It processes a stream of 32-bit VIFcode packets from its FIFO and:
    ///   • Unpacks vertex / matrix data into VU data memory (UNPACK command).
    ///   • Uploads micro-programs into VU micro memory (MPG command).
    ///   • Configures VIF operating registers (STCYCL, ITOP, STMOD, STMASK, STROW, STCOL, MARK).
    ///   • Starts/continues VU microprogram execution (MSCAL, MSCNT, MSCALF).
    ///   • Passes display-list data directly to the GIF (DIRECT / DIRECTHL, VIF1 only).
    ///
    /// Physical register base addresses:
    ///   VIF0: 0x10003800
    ///   VIF1: 0x10003C00
    ///
    /// Registers (16-byte aligned offsets from base):
    ///   +0x000  VIF_STAT  — status (read-only most bits)
    ///   +0x010  VIF_FBRST — FIFO flush / reset (write-only control)
    ///   +0x020  VIF_ERR   — error flags (R/W)
    ///   +0x030  VIF_MARK  — MARK value (R/W)
    ///   +0x040  VIF_CYCLE — cycle control: [7:0]=CL, [15:8]=WL
    ///   +0x050  VIF_MODE  — transfer mode (0=normal, 1=offset, 2=difference)
    ///   +0x060  VIF_NUM   — qwords remaining (read-only, decremented by UNPACK)
    ///   +0x070  VIF_MASK  — 32-bit write-mask register
    ///   +0x080  VIF_CODE  — copy of the most recent VIFcode (read-only)
    ///   +0x090  VIF_ITOPS — ITOP saved by software
    ///   +0x0A0  VIF_BASE  — (VIF1 only) double-buffer base address
    ///   +0x0B0  VIF_OFST  — (VIF1 only) double-buffer offset
    ///   +0x0C0  VIF_TOPS  — (VIF1 only) VU1 TOP save (read-only)
    ///   +0x0D0  VIF_ITOP  — ITOP current value (read-only)
    ///   +0x0E0  VIF_TOP   — (VIF1 only) VU1 TOP current value (read-only)
    ///
    /// UNPACK command formats (CMD bits [3:0] when CMD >= 0x60):
    ///   0x0 S-32  | 0x1 S-16  | 0x2 S-8
    ///   0x4 V2-32 | 0x5 V2-16 | 0x6 V2-8
    ///   0x8 V3-32 | 0x9 V3-16 | 0xA V3-8
    ///   0xC V4-32 | 0xD V4-16 | 0xE V4-8 | 0xF V4-5
    ///
    /// Reference: PCSX2 pcsx2/Vif.h, pcsx2/VifDma.cpp, pcsx2/Vif_Unpack.cpp;
    ///            PS2 EE Users Manual §4 (VIF chapter).
    /// </summary>
    public sealed class VifUnit
    {
        // ── Register offsets (byte offset from unit base, 16-byte aligned) ─────
        public const uint REG_STAT  = 0x000u;
        public const uint REG_FBRST = 0x010u;
        public const uint REG_ERR   = 0x020u;
        public const uint REG_MARK  = 0x030u;
        public const uint REG_CYCLE = 0x040u;
        public const uint REG_MODE  = 0x050u;
        public const uint REG_NUM   = 0x060u;
        public const uint REG_MASK  = 0x070u;
        public const uint REG_CODE  = 0x080u;
        public const uint REG_ITOPS = 0x090u;
        public const uint REG_BASE  = 0x0A0u;  // VIF1 only
        public const uint REG_OFST  = 0x0B0u;  // VIF1 only
        public const uint REG_TOPS  = 0x0C0u;  // VIF1 only (read-only)
        public const uint REG_ITOP  = 0x0D0u;  // read-only
        public const uint REG_TOP   = 0x0E0u;  // VIF1 only (read-only)

        // ── FBRST bit masks ───────────────────────────────────────────────────
        private const uint FBRST_RST = 1u << 0;  // soft reset
        private const uint FBRST_FBK = 1u << 1;  // force break
        private const uint FBRST_STP = 1u << 2;  // stop
        private const uint FBRST_STC = 1u << 3;  // stop clear

        // ── STAT bit masks ────────────────────────────────────────────────────
        private const uint STAT_VGW = 1u << 3;   // GIF wait (VIF1 only)
        private const uint STAT_MRK = 1u << 6;   // MARK interrupt
        private const uint STAT_DBF = 1u << 7;   // double-buffer flag (VIF1 only)
        private const uint STAT_VSS = 1u << 8;   // stall by STOP
        private const uint STAT_VFS = 1u << 9;   // stall by force break
        private const uint STAT_VIS = 1u << 10;  // invalid command stall
        private const uint STAT_INT = 1u << 11;  // interrupt
        private const uint STAT_ER0 = 1u << 12;  // transfer mode error
        private const uint STAT_ER1 = 1u << 13;  // VIF code error

        // ── UNPACK format codes (CMD[3:0] = (VN<<2)|VL when CMD >= 0x60) ──────
        private const int UNPACK_S_32  = 0x00;
        private const int UNPACK_S_16  = 0x01;
        private const int UNPACK_S_8   = 0x02;
        private const int UNPACK_V2_32 = 0x04;
        private const int UNPACK_V2_16 = 0x05;
        private const int UNPACK_V2_8  = 0x06;
        private const int UNPACK_V3_32 = 0x08;
        private const int UNPACK_V3_16 = 0x09;
        private const int UNPACK_V3_8  = 0x0A;
        private const int UNPACK_V4_32 = 0x0C;
        private const int UNPACK_V4_16 = 0x0D;
        private const int UNPACK_V4_8  = 0x0E;
        private const int UNPACK_V4_5  = 0x0F;

        // ── Register state ────────────────────────────────────────────────────
        private uint _stat;
        private uint _err;
        private uint _mark;
        private uint _cycle;    // [7:0]=CL, [15:8]=WL
        private uint _mode;
        private uint _num;
        private uint _mask;
        private uint _code;
        private uint _itops;
        private uint _baseAddr; // VIF1 only
        private uint _ofst;     // VIF1 only
        private uint _tops;     // VIF1 only, read-only
        private uint _itop;     // read-only
        private uint _top;      // VIF1 only, read-only

        // Row and column registers (set by STROW / STCOL; used for masked unpack fills)
        private readonly uint[] _row = new uint[4];
        private readonly uint[] _col = new uint[4];

        // ── Identity ──────────────────────────────────────────────────────────

        /// <summary>True for VIF1 (VU1 interface), false for VIF0 (VU0 interface).</summary>
        public bool IsVif1 { get; }

        // ── VU memory (written by UNPACK / MPG commands) ──────────────────────

        /// <summary>Target VU data memory.  Assign <c>VU0State.DataMem</c> for VIF0.</summary>
        public byte[] VuDataMem { get; set; }

        /// <summary>Target VU micro memory.  Assign <c>VU0State.MicroMem</c> for VIF0.</summary>
        public byte[] VuMicroMem { get; set; }

        /// <summary>
        /// GIF output for DIRECT/DIRECTHL commands (VIF1 only).
        /// When set, DIRECT payload qwords are forwarded to <see cref="GIF.Feed"/>.
        /// </summary>
        public GIF? GifOutput { get; set; }

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Raised when a MARK command is processed; carries the 16-bit MARK value.</summary>
        public event EventHandler<uint>? MarkInterrupt;

        /// <summary>
        /// Raised when MSCAL/MSCNT/MSCALF is processed; carries the start address (qword units).
        /// </summary>
        public event EventHandler<ushort>? MicroStart;

        // ── Constructor ───────────────────────────────────────────────────────

        public VifUnit(bool isVif1)
        {
            IsVif1 = isVif1;
            // Default internal buffers: 4 KB for VIF0 (VU0 size), 16 KB for VIF1 (VU1 size).
            // The caller may replace these with the actual VU state arrays.
            int dataSz  = isVif1 ? 16 * 1024 : 4 * 1024;
            int microSz = isVif1 ? 16 * 1024 : 4 * 1024;
            VuDataMem  = new byte[dataSz];
            VuMicroMem = new byte[microSz];
        }

        // ── Register I/O ──────────────────────────────────────────────────────

        /// <summary>
        /// Read a 32-bit VIF register.
        /// <paramref name="offset"/> is the byte offset from the unit's base address.
        /// Bits [7:4] select the register; lower 4 bits are ignored.
        /// </summary>
        public uint ReadRegister(uint offset)
        {
            uint reg = offset & 0x1F0u;  // 16-byte aligned, 5-bit field within 0x1F0
            return reg switch
            {
                REG_STAT  => _stat,
                REG_ERR   => _err,
                REG_MARK  => _mark,
                REG_CYCLE => _cycle,
                REG_MODE  => _mode,
                REG_NUM   => _num,
                REG_MASK  => _mask,
                REG_CODE  => _code,
                REG_ITOPS => _itops,
                REG_BASE  => IsVif1 ? _baseAddr : 0u,
                REG_OFST  => IsVif1 ? _ofst     : 0u,
                REG_TOPS  => IsVif1 ? _tops      : 0u,
                REG_ITOP  => _itop,
                REG_TOP   => IsVif1 ? _top       : 0u,
                _         => 0u
            };
        }

        /// <summary>
        /// Write a 32-bit VIF register.
        /// FBRST (write-only): RST soft-resets the unit; STC clears stall bits.
        /// STAT is read-only (writes ignored).
        /// </summary>
        public void WriteRegister(uint offset, uint value)
        {
            uint reg = offset & 0x1F0u;
            switch (reg)
            {
                case REG_FBRST:
                    if ((value & FBRST_RST) != 0) Reset();
                    if ((value & FBRST_FBK) != 0) _stat |= STAT_VFS;
                    if ((value & FBRST_STC) != 0) _stat &= ~(STAT_VSS | STAT_VFS);
                    break;
                case REG_ERR:
                    _err = value & 0x7u;
                    break;
                case REG_MARK:
                    _mark = value & 0xFFFFu;
                    break;
                case REG_CYCLE:
                    _cycle = value & 0xFFFFu;
                    break;
                case REG_MODE:
                    _mode = value & 0x3u;
                    break;
                case REG_MASK:
                    _mask = value;
                    break;
                case REG_ITOPS:
                    _itops = value & 0x3FFu;
                    break;
                case REG_BASE when IsVif1:
                    _baseAddr = value & 0x3FFu;
                    break;
                case REG_OFST when IsVif1:
                    _ofst = value & 0x3FFu;
                    break;
                // STAT, NUM, CODE, TOPS, ITOP, TOP are read-only — writes ignored.
            }
        }

        // ── VIF packet processing ─────────────────────────────────────────────

        /// <summary>
        /// Feed a stream of 32-bit words from the VIF FIFO.
        /// Each VIFcode word is followed immediately by its payload data (if any).
        /// Typically invoked by the DMAC when it executes a VIF DMA channel transfer.
        /// </summary>
        public void Feed(ReadOnlySpan<uint> words)
        {
            int i = 0;
            while (i < words.Length)
            {
                uint vifcode = words[i++];
                ProcessVifCode(vifcode, words, ref i);
            }
        }

        // ── Private: VIFcode dispatch ─────────────────────────────────────────

        private void ProcessVifCode(uint vifcode, ReadOnlySpan<uint> words, ref int pos)
        {
            _code = vifcode;
            uint cmd = (vifcode >> 24) & 0x7Fu;   // bits [30:24]
            uint num = (vifcode >> 16) & 0xFFu;    // bits [23:16]
            uint imm = vifcode & 0xFFFFu;           // bits [15:0]

            // Set STAT.INT when the VIFcode INT bit (bit 31) is set.
            if ((vifcode & 0x80000000u) != 0)
                _stat |= STAT_INT;

            if (cmd >= 0x60u)
            {
                ProcessUnpack(cmd, num, imm, words, ref pos);
                return;
            }

            switch (cmd)
            {
                case 0x00: break;  // NOP

                case 0x01:  // STCYCL — set cycle
                    _cycle = imm & 0xFFFFu;
                    break;

                case 0x02 when IsVif1:  // OFFSET (VIF1 only)
                    _ofst = imm & 0x3FFu;
                    // Toggle DBF; TOPS = BASE + (DBF ? OFST : 0)
                    _stat ^= STAT_DBF;
                    _tops = (_stat & STAT_DBF) != 0 ? (_baseAddr + _ofst) & 0x3FFu : _baseAddr & 0x3FFu;
                    _top  = _tops;
                    break;

                case 0x03 when IsVif1:  // BASE (VIF1 only)
                    _baseAddr = imm & 0x3FFu;
                    break;

                case 0x04:  // ITOP — set ITOPS (latched into ITOP at UNPACK end)
                    _itops = imm & 0x3FFu;
                    break;

                case 0x05:  // STMOD — set transfer mode
                    _mode = imm & 0x3u;
                    break;

                case 0x06:  // MSKPATH3 (VIF1 only) — stub
                    break;

                case 0x07:  // MARK — set MARK register and signal interrupt
                    _mark = imm & 0xFFFFu;
                    _stat |= STAT_MRK;
                    MarkInterrupt?.Invoke(this, _mark);
                    break;

                case 0x10:  // FLUSHE — wait for VU to finish current micro-task
                case 0x11:  // FLUSH  — wait for VU + PATH1 (VIF1 only)
                case 0x13:  // FLUSHA — wait for all paths (VIF1 only)
                    break;  // stub (VU micro-execution not yet running)

                case 0x14:  // MSCAL  — start VU microprogram at given address
                case 0x15:  // MSCALF — start VU microprogram (VIF1 only, after FLUSH)
                    MicroStart?.Invoke(this, (ushort)(imm & 0x3FFu));
                    break;

                case 0x17:  // MSCNT — continue VU microprogram (restart at PC=ITOP)
                    MicroStart?.Invoke(this, (ushort)(_itop & 0x3FFu));
                    break;

                case 0x20:  // STMASK — next word sets VIF_MASK
                    if (pos < words.Length)
                        _mask = words[pos++];
                    break;

                case 0x30:  // STROW — next 4 words set ROW[0..3]
                    for (int r = 0; r < 4 && pos < words.Length; r++)
                        _row[r] = words[pos++];
                    break;

                case 0x31:  // STCOL — next 4 words set COL[0..3]
                    for (int c = 0; c < 4 && pos < words.Length; c++)
                        _col[c] = words[pos++];
                    break;

                case 0x4A:  // MPG — upload microprogram to VU micro memory
                    ProcessMpg(num, imm, words, ref pos);
                    break;

                case 0x50:  // DIRECT    (VIF1 only) — pass qwords to GIF PATH2
                case 0x51:  // DIRECTHL  (VIF1 only) — same, but holds PATH3 until complete
                {
                    uint qwords = (imm == 0) ? 65536u : imm;
                    int  skip   = (int)(qwords * 4u);
                    int  end    = Math.Min(pos + skip, words.Length);
                    if (GifOutput != null)
                        GifOutput.Feed(words.Slice(pos, end - pos));
                    pos = end;
                    break;
                }

                default:
                    // Unknown / reserved command.
                    _stat |= STAT_VIS;
                    break;
            }
        }

        // ── UNPACK ────────────────────────────────────────────────────────────

        private void ProcessUnpack(uint cmd, uint num, uint imm,
                                   ReadOnlySpan<uint> words, ref int pos)
        {
            // CMD layout for UNPACK (cmd in 0x60..0x7F):
            //   cmd[4] = M  (apply write mask)
            //   cmd[3:2] = VN (extra components: 0→1, 1→2, 2→3, 3→4 components)
            //   cmd[1:0] = VL (bit width: 0→32, 1→16, 2→8, 3→5)
            //
            // IMMED layout:
            //   bit[15] = FLG (add TOPS to destination address; VIF1 only)
            //   bit[14] = USN (unsigned/zero-extend instead of sign-extend)
            //   bits[9:0] = ADDR (destination qword address in VU data memory)

            bool maskApply = (cmd & 0x10u) != 0;
            int  formatCode = (int)(cmd & 0x0Fu);  // (VN<<2)|VL
            bool usn  = (imm & 0x4000u) != 0;
            bool flg  = IsVif1 && (imm & 0x8000u) != 0;
            uint addr = imm & 0x3FFu;

            if (flg)
                addr = (addr + _tops) & 0x3FFu;

            uint numItems = (num == 0) ? 256u : num;
            _num = numItems;

            byte cl = (byte)(_cycle & 0xFFu);
            byte wl = (byte)((_cycle >> 8) & 0xFFu);

            // WL=0 or CL=0 → treat as normal (no cycle skipping/filling)
            bool normalCycle = (cl == 0 || wl == 0 || cl == wl);

            uint destQw   = addr;
            uint written  = 0;
            byte cyclePos = 0;

            while (written < numItems && pos < words.Length)
            {
                if (!normalCycle)
                {
                    if (cl < wl)
                    {
                        // Skip mode: after writing CL destination qwords, skip WL-CL.
                        if (cyclePos >= cl)
                        {
                            destQw++;
                            cyclePos++;
                            if (cyclePos >= wl) cyclePos = 0;
                            continue;
                        }
                    }
                    else if (cl > wl)
                    {
                        // Fill mode: after writing WL from source, write CL-WL from ROW.
                        if (cyclePos >= wl)
                        {
                            WriteQwordFromRow(destQw, maskApply);
                            destQw++;
                            cyclePos++;
                            if (cyclePos >= cl) cyclePos = 0;
                            // Filling does not consume input and does not count toward NUM
                            continue;
                        }
                    }
                }

                WriteUnpackQword(formatCode, usn, maskApply, destQw, words, ref pos);
                destQw++;
                written++;
                _num--;
                cyclePos++;
                if (!normalCycle && cyclePos >= (cl > wl ? (uint)cl : (uint)wl))
                    cyclePos = 0;
            }

            // Latch ITOPS into ITOP at the end of the transfer.
            _itop = _itops;
        }

        // ── Per-qword write ───────────────────────────────────────────────────

        private void WriteUnpackQword(int format, bool usn, bool maskApply,
                                      uint destQw, ReadOnlySpan<uint> src, ref int pos)
        {
            uint c0, c1, c2, c3;

            switch (format)
            {
                case UNPACK_S_32: // S-32: 1×32-bit, broadcast to all 4 components
                {
                    if (pos >= src.Length) return;
                    c0 = c1 = c2 = c3 = src[pos++];
                    break;
                }

                case UNPACK_S_16: // S-16: 1×16-bit (lower half of word), broadcast
                {
                    if (pos >= src.Length) return;
                    uint half = src[pos++] & 0xFFFFu;
                    if (!usn && (half & 0x8000u) != 0) half |= 0xFFFF0000u;
                    c0 = c1 = c2 = c3 = half;
                    break;
                }

                case UNPACK_S_8: // S-8: 1×8-bit (lowest byte of word), broadcast
                {
                    if (pos >= src.Length) return;
                    uint b = src[pos++] & 0xFFu;
                    if (!usn && (b & 0x80u) != 0) b |= 0xFFFFFF00u;
                    c0 = c1 = c2 = c3 = b;
                    break;
                }

                case UNPACK_V2_32: // V2-32: 2×32-bit; z=w=0
                {
                    if (pos + 2 > src.Length) return;
                    c0 = src[pos++];
                    c1 = src[pos++];
                    c2 = c3 = 0u;
                    break;
                }

                case UNPACK_V2_16: // V2-16: 2×16-bit packed in 1 word
                {
                    if (pos >= src.Length) return;
                    uint word = src[pos++];
                    c0 = word & 0xFFFFu;
                    c1 = (word >> 16) & 0xFFFFu;
                    if (!usn)
                    {
                        if ((c0 & 0x8000u) != 0) c0 |= 0xFFFF0000u;
                        if ((c1 & 0x8000u) != 0) c1 |= 0xFFFF0000u;
                    }
                    c2 = c3 = 0u;
                    break;
                }

                case UNPACK_V2_8: // V2-8: 2×8-bit in lower 2 bytes of word
                {
                    if (pos >= src.Length) return;
                    uint word = src[pos++];
                    c0 = word & 0xFFu;
                    c1 = (word >> 8) & 0xFFu;
                    if (!usn)
                    {
                        if ((c0 & 0x80u) != 0) c0 |= 0xFFFFFF00u;
                        if ((c1 & 0x80u) != 0) c1 |= 0xFFFFFF00u;
                    }
                    c2 = c3 = 0u;
                    break;
                }

                case UNPACK_V3_32: // V3-32: 3×32-bit; w=0
                {
                    if (pos + 3 > src.Length) return;
                    c0 = src[pos++];
                    c1 = src[pos++];
                    c2 = src[pos++];
                    c3 = 0u;
                    break;
                }

                case UNPACK_V3_16: // V3-16: 3×16-bit packed in 2 words (xy | z/pad)
                {
                    if (pos + 2 > src.Length) return;
                    uint w0 = src[pos++];
                    uint w1 = src[pos++];
                    c0 = w0 & 0xFFFFu;
                    c1 = (w0 >> 16) & 0xFFFFu;
                    c2 = w1 & 0xFFFFu;
                    if (!usn)
                    {
                        if ((c0 & 0x8000u) != 0) c0 |= 0xFFFF0000u;
                        if ((c1 & 0x8000u) != 0) c1 |= 0xFFFF0000u;
                        if ((c2 & 0x8000u) != 0) c2 |= 0xFFFF0000u;
                    }
                    c3 = 0u;
                    break;
                }

                case UNPACK_V3_8: // V3-8: 3×8-bit in lower 3 bytes of word
                {
                    if (pos >= src.Length) return;
                    uint word = src[pos++];
                    c0 = word & 0xFFu;
                    c1 = (word >> 8) & 0xFFu;
                    c2 = (word >> 16) & 0xFFu;
                    if (!usn)
                    {
                        if ((c0 & 0x80u) != 0) c0 |= 0xFFFFFF00u;
                        if ((c1 & 0x80u) != 0) c1 |= 0xFFFFFF00u;
                        if ((c2 & 0x80u) != 0) c2 |= 0xFFFFFF00u;
                    }
                    c3 = 0u;
                    break;
                }

                case UNPACK_V4_32: // V4-32: 4×32-bit (most common for vertex/matrix data)
                {
                    if (pos + 4 > src.Length) return;
                    c0 = src[pos++];
                    c1 = src[pos++];
                    c2 = src[pos++];
                    c3 = src[pos++];
                    break;
                }

                case UNPACK_V4_16: // V4-16: 4×16-bit packed in 2 words
                {
                    if (pos + 2 > src.Length) return;
                    uint w0 = src[pos++];
                    uint w1 = src[pos++];
                    c0 = w0 & 0xFFFFu;
                    c1 = (w0 >> 16) & 0xFFFFu;
                    c2 = w1 & 0xFFFFu;
                    c3 = (w1 >> 16) & 0xFFFFu;
                    if (!usn)
                    {
                        if ((c0 & 0x8000u) != 0) c0 |= 0xFFFF0000u;
                        if ((c1 & 0x8000u) != 0) c1 |= 0xFFFF0000u;
                        if ((c2 & 0x8000u) != 0) c2 |= 0xFFFF0000u;
                        if ((c3 & 0x8000u) != 0) c3 |= 0xFFFF0000u;
                    }
                    break;
                }

                case UNPACK_V4_8: // V4-8: 4×8-bit packed in 1 word
                {
                    if (pos >= src.Length) return;
                    uint word = src[pos++];
                    c0 = word & 0xFFu;
                    c1 = (word >> 8) & 0xFFu;
                    c2 = (word >> 16) & 0xFFu;
                    c3 = (word >> 24) & 0xFFu;
                    if (!usn)
                    {
                        if ((c0 & 0x80u) != 0) c0 |= 0xFFFFFF00u;
                        if ((c1 & 0x80u) != 0) c1 |= 0xFFFFFF00u;
                        if ((c2 & 0x80u) != 0) c2 |= 0xFFFFFF00u;
                        if ((c3 & 0x80u) != 0) c3 |= 0xFFFFFF00u;
                    }
                    break;
                }

                case UNPACK_V4_5: // V4-5: RGB5A1 — 5+5+5+1 bits packed in 1 word
                {
                    if (pos >= src.Length) return;
                    uint word = src[pos++];
                    c0 = (word)       & 0x1Fu;  // R (5 bits)
                    c1 = (word >> 5)  & 0x1Fu;  // G (5 bits)
                    c2 = (word >> 10) & 0x1Fu;  // B (5 bits)
                    c3 = (word >> 15) & 0x1u;   // A (1 bit)
                    // Always unsigned for V4-5
                    break;
                }

                default:
                    return;  // unknown format — skip
            }

            WriteComponents(destQw, maskApply, c0, c1, c2, c3);
        }

        // ── Fill mode: write ROW values into a destination qword ─────────────

        private void WriteQwordFromRow(uint destQw, bool maskApply)
        {
            WriteComponents(destQw, maskApply, _row[0], _row[1], _row[2], _row[3]);
        }

        // ── Write four 32-bit components to VU data memory ────────────────────

        private void WriteComponents(uint destQw, bool maskApply,
                                     uint c0, uint c1, uint c2, uint c3)
        {
            uint byteOffset = destQw * 16u;
            if (byteOffset + 16u > (uint)VuDataMem.Length)
                return;

            Span<uint> comp = stackalloc uint[4] { c0, c1, c2, c3 };

            for (int c = 0; c < 4; c++)
            {
                uint v = comp[c];

                if (maskApply && _mask != 0)
                {
                    // Mask field: 2 bits per component.
                    // Layout: bits [1:0]=comp0, bits [3:2]=comp1, bits [5:4]=comp2, bits [7:6]=comp3
                    uint maskBits = (_mask >> (c * 2)) & 0x3u;
                    switch (maskBits)
                    {
                        case 0: break;                 // 00 — write data as-is
                        case 1: continue;              // 01 — skip (do not write)
                        case 2: v = _row[c]; break;    // 10 — write ROW register
                        case 3: v = _col[c]; break;    // 11 — write COL register
                    }
                }

                uint ofs = byteOffset + (uint)(c * 4);
                VuDataMem[ofs + 0] = (byte)(v);
                VuDataMem[ofs + 1] = (byte)(v >> 8);
                VuDataMem[ofs + 2] = (byte)(v >> 16);
                VuDataMem[ofs + 3] = (byte)(v >> 24);
            }
        }

        // ── MPG — microprogram upload ─────────────────────────────────────────

        private void ProcessMpg(uint num, uint imm,
                                 ReadOnlySpan<uint> words, ref int pos)
        {
            // MPG uploads pairs of 32-bit instruction words (64-bit instructions)
            // into VU micro memory.
            // NUM = number of 64-bit instruction words to upload (0 = 256).
            // IMMED[7:0] = destination address in 64-bit (8-byte) units.
            uint destByte = (imm & 0xFFu) * 8u;
            uint count    = (num == 0) ? 256u : num;

            for (uint i = 0; i < count; i++)
            {
                if (pos + 2 > words.Length) break;

                uint lo = words[pos++];
                uint hi = words[pos++];

                uint dstOfs = destByte + i * 8u;
                if (dstOfs + 8u > (uint)VuMicroMem.Length) break;

                VuMicroMem[dstOfs + 0] = (byte)(lo);
                VuMicroMem[dstOfs + 1] = (byte)(lo >>  8);
                VuMicroMem[dstOfs + 2] = (byte)(lo >> 16);
                VuMicroMem[dstOfs + 3] = (byte)(lo >> 24);
                VuMicroMem[dstOfs + 4] = (byte)(hi);
                VuMicroMem[dstOfs + 5] = (byte)(hi >>  8);
                VuMicroMem[dstOfs + 6] = (byte)(hi >> 16);
                VuMicroMem[dstOfs + 7] = (byte)(hi >> 24);
            }
        }

        // ── Reset ─────────────────────────────────────────────────────────────

        /// <summary>Soft-reset all VIF registers (does not clear VU memory).</summary>
        public void Reset()
        {
            _stat     = 0u;
            _err      = 0u;
            _mark     = 0u;
            _cycle    = 0u;
            _mode     = 0u;
            _num      = 0u;
            _mask     = 0u;
            _code     = 0u;
            _itops    = 0u;
            _baseAddr = 0u;
            _ofst     = 0u;
            _tops     = 0u;
            _itop     = 0u;
            _top      = 0u;
            Array.Clear(_row);
            Array.Clear(_col);
        }

        // ── Public property accessors (for testing / external inspection) ─────

        /// <summary>Current VIF_STAT value.</summary>
        public uint Stat => _stat;

        /// <summary>VIF_CYCLE: [7:0]=CL, [15:8]=WL.</summary>
        public uint Cycle => _cycle;

        /// <summary>Cycle write-length (CL) field.</summary>
        public byte CycleCL => (byte)(_cycle & 0xFFu);

        /// <summary>Cycle working-length (WL) field.</summary>
        public byte CycleWL => (byte)((_cycle >> 8) & 0xFFu);

        /// <summary>Transfer mode (0=normal, 1=offset, 2=difference).</summary>
        public uint Mode => _mode;

        /// <summary>Write-mask register.</summary>
        public uint Mask => _mask;

        /// <summary>MARK register value.</summary>
        public uint Mark => _mark;

        /// <summary>ITOP current value (latched from ITOPS at end of UNPACK).</summary>
        public uint Itop => _itop;

        /// <summary>Row registers (set by STROW, used for masked fill).</summary>
        public uint[] Row => _row;

        /// <summary>Column registers (set by STCOL, used for masked fill).</summary>
        public uint[] Col => _col;
    }
}
