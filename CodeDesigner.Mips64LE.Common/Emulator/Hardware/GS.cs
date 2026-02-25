using System;

namespace CodeDesigner.Mips64LE.Common.Emulator.Hardware
{
    /// <summary>
    /// Minimal emulation of the PS2 Graphics Synthesizer (GS).
    ///
    /// The GS receives rendering commands from the GIF via GIFtag data packets and
    /// maintains a bank of 128 internal registers (each 64-bit) plus a set of
    /// privileged configuration registers at physical address 0x12000000.
    ///
    /// Internal registers (address 0x00–0x7F) are written by the GIF in
    /// PACKED, REGLIST, or IMAGE transfer modes.  Key ones include:
    ///   0x00 PRIM      — primitive type and draw settings
    ///   0x01 RGBAQ     — vertex colour (R/G/B/A/Q)
    ///   0x02 ST        — S/T texture coordinates (float)
    ///   0x03 UV        — U/V texture coordinates (fixed)
    ///   0x04 XYZF2     — vertex coordinate + fog (primitive kick)
    ///   0x05 XYZ2      — vertex coordinate (primitive kick, no fog)
    ///   0x06 TEX0_1    — texture buffer base/settings context 1
    ///   0x07 TEX0_2    — texture buffer base/settings context 2
    ///   0x3F TEXFLUSH  — flush texture cache
    ///   0x54 HWREG     — local memory write port (IMAGE mode)
    ///   0x5C SCISSOR_1 — scissoring region context 1
    ///   0x5D SCISSOR_2 — scissoring region context 2
    ///   0x63 TEST_1    — pixel test settings context 1
    ///   0x64 TEST_2    — pixel test settings context 2
    ///   0x68 FRAME_1   — framebuffer settings context 1
    ///   0x69 FRAME_2   — framebuffer settings context 2
    ///   0x6A ZBUF_1    — Z-buffer settings context 1
    ///   0x6B ZBUF_2    — Z-buffer settings context 2
    ///   0x6C BITBLTBUF — texture transfer source/dest addresses
    ///   0x6D TRXPOS    — texture transfer start position
    ///   0x6E TRXREG    — texture transfer region size
    ///   0x6F TRXDIR    — texture transfer direction (triggers start)
    ///
    /// Privileged registers (physical 0x12000000 + offset) control display output:
    ///   +0x000  PMODE    — display circuit mode
    ///   +0x010  SMODE1   — sync mode 1 (clock)
    ///   +0x020  SMODE2   — sync mode 2 (interlace/progressive)
    ///   +0x030  SRFSH    — DRAM refresh
    ///   +0x040  SYNCH1   — sync horizontal 1
    ///   +0x050  SYNCH2   — sync horizontal 2
    ///   +0x060  SYNCV    — sync vertical
    ///   +0x070  DISPFB1  — display framebuffer 1
    ///   +0x080  DISPLAY1 — display 1 characteristics
    ///   +0x090  DISPFB2  — display framebuffer 2
    ///   +0x0A0  DISPLAY2 — display 2 characteristics
    ///   +0x0B0  EXTBUF   — external input buffer
    ///   +0x0C0  EXTDATA  — external input data
    ///   +0x0D0  EXTWRITE — external input write
    ///   +0x0E0  BGCOLOR  — background colour
    ///   +0x1000 CSR      — GS status/control
    ///   +0x1010 IMR      — interrupt mask
    ///   +0x1040 BUSDIR   — bus direction
    ///   +0x1080 SIGLBLID — signal / label ID
    ///
    /// Reference: PS2 EE User's Manual §7; PCSX2 pcsx2/GS/GSState.cpp.
    /// </summary>
    public sealed class GS
    {
        // ── Internal register addresses ────────────────────────────────────────
        public const byte REG_PRIM      = 0x00;
        public const byte REG_RGBAQ     = 0x01;
        public const byte REG_ST        = 0x02;
        public const byte REG_UV        = 0x03;
        public const byte REG_XYZF2     = 0x04;
        public const byte REG_XYZ2      = 0x05;
        public const byte REG_TEX0_1    = 0x06;
        public const byte REG_TEX0_2    = 0x07;
        public const byte REG_CLAMP_1   = 0x08;
        public const byte REG_CLAMP_2   = 0x09;
        public const byte REG_FOG       = 0x0A;
        public const byte REG_XYZF3     = 0x0C;
        public const byte REG_XYZ3      = 0x0D;
        public const byte REG_AD        = 0x0E;  // A+D: address embedded in data
        public const byte REG_NOP       = 0x0F;
        public const byte REG_TEXFLUSH  = 0x3F;
        public const byte REG_HWREG     = 0x54;  // local memory write port (IMAGE mode)
        public const byte REG_SCISSOR_1 = 0x5C;
        public const byte REG_SCISSOR_2 = 0x5D;
        public const byte REG_ALPHA_1   = 0x5E;
        public const byte REG_ALPHA_2   = 0x5F;
        public const byte REG_TEST_1    = 0x63;
        public const byte REG_TEST_2    = 0x64;
        public const byte REG_FRAME_1   = 0x68;
        public const byte REG_FRAME_2   = 0x69;
        public const byte REG_ZBUF_1    = 0x6A;
        public const byte REG_ZBUF_2    = 0x6B;
        public const byte REG_BITBLTBUF = 0x6C;
        public const byte REG_TRXPOS    = 0x6D;
        public const byte REG_TRXREG    = 0x6E;
        public const byte REG_TRXDIR    = 0x6F;

        // ── Privileged register offsets (from physical 0x12000000) ─────────────
        public const uint PRIV_PMODE    = 0x000u;
        public const uint PRIV_SMODE1   = 0x010u;
        public const uint PRIV_SMODE2   = 0x020u;
        public const uint PRIV_SRFSH    = 0x030u;
        public const uint PRIV_SYNCH1   = 0x040u;
        public const uint PRIV_SYNCH2   = 0x050u;
        public const uint PRIV_SYNCV    = 0x060u;
        public const uint PRIV_DISPFB1  = 0x070u;
        public const uint PRIV_DISPLAY1 = 0x080u;
        public const uint PRIV_DISPFB2  = 0x090u;
        public const uint PRIV_DISPLAY2 = 0x0A0u;
        public const uint PRIV_EXTBUF   = 0x0B0u;
        public const uint PRIV_EXTDATA  = 0x0C0u;
        public const uint PRIV_EXTWRITE = 0x0D0u;
        public const uint PRIV_BGCOLOR  = 0x0E0u;
        public const uint PRIV_CSR      = 0x1000u;
        public const uint PRIV_IMR      = 0x1010u;
        public const uint PRIV_BUSDIR   = 0x1040u;
        public const uint PRIV_SIGLBLID = 0x1080u;

        // ── Internal register storage (128 × 64-bit) ──────────────────────────
        private readonly ulong[] _internalRegs = new ulong[128];

        // ── Privileged register storage ────────────────────────────────────────
        private ulong _pmode;
        private ulong _smode1;
        private ulong _smode2;
        private ulong _srfsh;
        private ulong _synch1;
        private ulong _synch2;
        private ulong _syncv;
        private ulong _dispfb1;
        private ulong _display1;
        private ulong _dispfb2;
        private ulong _display2;
        private ulong _extbuf;
        private ulong _extdata;
        private ulong _extwrite;
        private ulong _bgcolor;
        private ulong _csr;
        private ulong _imr;
        private ulong _busdir;
        private ulong _siglblid;

        // ── Internal register access ──────────────────────────────────────────

        /// <summary>Read a 64-bit internal GS register by its address (0x00–0x7F).</summary>
        public ulong ReadInternal(byte addr)
        {
            if (addr >= 128) return 0ul;
            return _internalRegs[addr];
        }

        /// <summary>
        /// Write a 64-bit internal GS register directly (REGLIST mode or IMAGE mode).
        /// Address 0x0F (NOP) is silently ignored.
        /// </summary>
        public void WriteInternal(byte addr, ulong value)
        {
            if (addr >= 128 || addr == REG_NOP) return;
            _internalRegs[addr] = value;
        }

        /// <summary>
        /// Write a PACKED-mode GS register (128-bit data qword).
        /// For A+D (0x0E) the destination register address is embedded in <paramref name="hi"/>
        /// bits [6:0]; the 64-bit value is <paramref name="lo"/>.
        /// All other registers: <paramref name="lo"/> is stored directly.
        /// </summary>
        public void WritePacked(byte regId, ulong lo, ulong hi)
        {
            if (regId == REG_AD)
            {
                // A+D: bits [70:64] of the 128-bit qword = hi[6:0] = dest register address.
                byte destReg = (byte)(hi & 0x7Fu);
                if (destReg < 128 && destReg != REG_NOP)
                    _internalRegs[destReg] = lo;
            }
            else if (regId < 128 && regId != REG_NOP)
            {
                _internalRegs[regId] = lo;
            }
        }

        // ── Privileged register 32-bit bus access ─────────────────────────────

        /// <summary>
        /// Read a 32-bit word from a GS privileged register.
        /// <paramref name="offset"/> is the byte offset from the GS privileged base (0x12000000).
        /// Each privileged register is 64-bit; the low or high 32-bit half is selected by bit 2
        /// of the offset.
        /// </summary>
        public uint ReadPriv32(uint offset)
        {
            ulong val = ReadPriv64(offset & ~0xFu);
            return (offset & 4u) == 0u ? (uint)val : (uint)(val >> 32);
        }

        /// <summary>Write a 32-bit word to a GS privileged register (see <see cref="ReadPriv32"/>).</summary>
        public void WritePriv32(uint offset, uint value)
        {
            uint aligned = offset & ~0xFu;
            ulong current = ReadPriv64(aligned);
            if ((offset & 4u) == 0u)
                current = (current & 0xFFFF_FFFF_0000_0000ul) | value;
            else
                current = (current & 0x0000_0000_FFFF_FFFFul) | ((ulong)value << 32);
            WritePriv64(aligned, current);
        }

        /// <summary>Read a 64-bit GS privileged register by offset.</summary>
        public ulong ReadPriv64(uint offset)
        {
            // Align to 16-byte boundary (each privileged register is at a 0x10-aligned offset).
            uint key = offset & ~0xFu;
            return key switch
            {
                PRIV_PMODE    => _pmode,
                PRIV_SMODE1   => _smode1,
                PRIV_SMODE2   => _smode2,
                PRIV_SRFSH    => _srfsh,
                PRIV_SYNCH1   => _synch1,
                PRIV_SYNCH2   => _synch2,
                PRIV_SYNCV    => _syncv,
                PRIV_DISPFB1  => _dispfb1,
                PRIV_DISPLAY1 => _display1,
                PRIV_DISPFB2  => _dispfb2,
                PRIV_DISPLAY2 => _display2,
                PRIV_EXTBUF   => _extbuf,
                PRIV_EXTDATA  => _extdata,
                PRIV_EXTWRITE => _extwrite,
                PRIV_BGCOLOR  => _bgcolor,
                PRIV_CSR      => _csr,
                PRIV_IMR      => _imr,
                PRIV_BUSDIR   => _busdir,
                PRIV_SIGLBLID => _siglblid,
                _             => 0ul
            };
        }

        /// <summary>Write a 64-bit GS privileged register by offset.</summary>
        public void WritePriv64(uint offset, ulong value)
        {
            uint key = offset & ~0xFu;
            switch (key)
            {
                case PRIV_PMODE:    _pmode    = value; break;
                case PRIV_SMODE1:   _smode1   = value; break;
                case PRIV_SMODE2:   _smode2   = value; break;
                case PRIV_SRFSH:    _srfsh    = value; break;
                case PRIV_SYNCH1:   _synch1   = value; break;
                case PRIV_SYNCH2:   _synch2   = value; break;
                case PRIV_SYNCV:    _syncv    = value; break;
                case PRIV_DISPFB1:  _dispfb1  = value; break;
                case PRIV_DISPLAY1: _display1 = value; break;
                case PRIV_DISPFB2:  _dispfb2  = value; break;
                case PRIV_DISPLAY2: _display2 = value; break;
                case PRIV_EXTBUF:   _extbuf   = value; break;
                case PRIV_EXTDATA:  _extdata  = value; break;
                case PRIV_EXTWRITE: _extwrite = value; break;
                case PRIV_BGCOLOR:  _bgcolor  = value; break;
                case PRIV_CSR:      _csr      = value; break;
                case PRIV_IMR:      _imr      = value; break;
                case PRIV_BUSDIR:   _busdir   = value; break;
                case PRIV_SIGLBLID: _siglblid = value; break;
            }
        }
    }
}
