using System;

namespace CodeDesigner.Mips64LE.Common.Emulator.IOP
{
    /// <summary>
    /// Emulates the PS2 SPU2 (Sound Processing Unit 2).
    ///
    /// Memory-mapped at <see cref="RegBase"/> = 0x1F900000 in the IOP address space
    /// across a 0x800-byte window covering both cores.
    ///
    /// The SPU2 contains two independent audio cores (Core 0 and Core 1), each with
    /// 24 voices, for a total of 48 simultaneous sound channels.
    ///
    /// Register layout (16-bit access, byte offsets from <see cref="RegBase"/>):
    /// <list type="table">
    ///   <item><term>+0x000–+0x17F</term><description>Core 0 voice registers (24 × 0x10 bytes)</description></item>
    ///   <item><term>+0x180–+0x3FF</term><description>Core 0 global registers</description></item>
    ///   <item><term>+0x400–+0x57F</term><description>Core 1 voice registers</description></item>
    ///   <item><term>+0x580–+0x7FF</term><description>Core 1 global registers</description></item>
    /// </list>
    ///
    /// Per-voice register offsets (within each voice's 0x10-byte block):
    /// <list type="table">
    ///   <item><term>+0x00</term><description>VOLL  — Volume Left (R/W)</description></item>
    ///   <item><term>+0x02</term><description>VOLR  — Volume Right (R/W)</description></item>
    ///   <item><term>+0x04</term><description>PITCH — Pitch / sample rate (R/W)</description></item>
    ///   <item><term>+0x06</term><description>ADSR1 — Attack/Decay/Sustain/Release upper (R/W)</description></item>
    ///   <item><term>+0x08</term><description>ADSR2 — Attack/Decay/Sustain/Release lower (R/W)</description></item>
    ///   <item><term>+0x0A</term><description>ENVX  — Current envelope value (read-only)</description></item>
    ///   <item><term>+0x0C</term><description>VOLXL — Current left volume (read-only)</description></item>
    ///   <item><term>+0x0E</term><description>VOLXR — Current right volume (read-only)</description></item>
    /// </list>
    ///
    /// Notable per-core global registers (offset from core base, e.g. Core 0 at +0x000):
    /// <list type="table">
    ///   <item><term>+0x198</term><description>MMIX   — Mix flags</description></item>
    ///   <item><term>+0x19A</term><description>ATTR   — Core attribute/control</description></item>
    ///   <item><term>+0x1A0</term><description>KON0   — Key On voices 0–15 (W) starts voices</description></item>
    ///   <item><term>+0x1A2</term><description>KON1   — Key On voices 16–23 (W)</description></item>
    ///   <item><term>+0x1A4</term><description>KOF0   — Key Off voices 0–15 (W) stops voices</description></item>
    ///   <item><term>+0x1A6</term><description>KOF1   — Key Off voices 16–23 (W)</description></item>
    ///   <item><term>+0x1A8</term><description>TSA_HI — Transfer Start Address high halfword (R/W)</description></item>
    ///   <item><term>+0x1AA</term><description>TSA_LO — Transfer Start Address low halfword (R/W)</description></item>
    ///   <item><term>+0x1AC</term><description>SD     — Sound Data port (W) writes to SPU2 audio RAM</description></item>
    ///   <item><term>+0x1B0</term><description>MVOLL  — Master Volume Left</description></item>
    ///   <item><term>+0x1B2</term><description>MVOLR  — Master Volume Right</description></item>
    /// </list>
    ///
    /// ADPCM audio encoding:
    ///   Each 16-byte block = 1 header byte (shift|filter) + 1 flag byte + 14 data bytes.
    ///   14 data bytes contain 28 signed 4-bit nibbles decoded to 16-bit PCM.
    ///   See <see cref="DecodeAdpcmBlock"/> for the decoder.
    ///
    /// DMA:
    ///   IOP DMAC channel 4 (SPU1) feeds Core 0; channel 7 (SPU2) feeds Core 1.
    ///   Data is written via <see cref="WriteDmaData"/> into <see cref="AudioRam"/>
    ///   starting at the core's TSA (transfer start address, in halfword units).
    ///
    /// Reference: PS2 Technical Reference Manual; PCSX2 pcsx2/SPU2/; No$ PS2 docs.
    /// </summary>
    public sealed class Spu2
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>SPU2 register base address in the IOP address space.</summary>
        public const uint RegBase = 0x1F900000u;

        /// <summary>Size of the SPU2 register window (0x800 bytes, two cores).</summary>
        public const uint RegSize = 0x800u;

        /// <summary>Number of audio cores in the SPU2.</summary>
        public const int CoreCount = 2;

        /// <summary>Number of voices per core.</summary>
        public const int VoicesPerCore = 24;

        /// <summary>Total voice count across both cores (48).</summary>
        public const int TotalVoices = CoreCount * VoicesPerCore;

        /// <summary>Size of SPU2 internal audio RAM in bytes (2 MB).</summary>
        public const int RamSize = 2 * 1024 * 1024;

        // Each core occupies a 0x400-byte window within the register space.
        private const uint CoreStride  = 0x400u;

        // Within each core's 0x400-byte window, voices occupy offsets 0–0x17F
        // (24 voices × 0x10 bytes); global registers start at 0x180.
        private const uint GlobalsOff  = 0x180u;

        // ── Global register offsets (relative to GlobalsOff within each core) ─

        private const uint OFF_PMON0   = 0x000u; // abs core+0x180
        private const uint OFF_PMON1   = 0x002u;
        private const uint OFF_NON0    = 0x004u;
        private const uint OFF_NON1    = 0x006u;
        private const uint OFF_VMIXL0  = 0x008u;
        private const uint OFF_VMIXL1  = 0x00Au;
        private const uint OFF_VMIXEL0 = 0x00Cu;
        private const uint OFF_VMIXEL1 = 0x00Eu;
        private const uint OFF_VMIXR0  = 0x010u;
        private const uint OFF_VMIXR1  = 0x012u;
        private const uint OFF_VMIXER0 = 0x014u;
        private const uint OFF_VMIXER1 = 0x016u;
        private const uint OFF_MMIX    = 0x018u; // abs core+0x198
        private const uint OFF_ATTR    = 0x01Au; // abs core+0x19A
        private const uint OFF_IRQA_HI = 0x01Cu; // abs core+0x19C
        private const uint OFF_IRQA_LO = 0x01Eu; // abs core+0x19E
        private const uint OFF_KON0    = 0x020u; // abs core+0x1A0 — key-on voices  0–15
        private const uint OFF_KON1    = 0x022u; // abs core+0x1A2 — key-on voices 16–23
        private const uint OFF_KOF0    = 0x024u; // abs core+0x1A4 — key-off voices  0–15
        private const uint OFF_KOF1    = 0x026u; // abs core+0x1A6 — key-off voices 16–23
        private const uint OFF_TSA_HI  = 0x028u; // abs core+0x1A8 — TSA high halfword
        private const uint OFF_TSA_LO  = 0x02Au; // abs core+0x1AA — TSA low halfword
        private const uint OFF_SD      = 0x02Cu; // abs core+0x1AC — sound data (DMA port)
        private const uint OFF_MVOLL   = 0x030u; // abs core+0x1B0 — master volume left
        private const uint OFF_MVOLR   = 0x032u; // abs core+0x1B2 — master volume right
        private const uint OFF_EVOLL   = 0x034u; // abs core+0x1B4
        private const uint OFF_EVOLR   = 0x036u;
        private const uint OFF_AVOLL   = 0x038u;
        private const uint OFF_AVOLR   = 0x03Au;
        private const uint OFF_BVOLL   = 0x03Cu;
        private const uint OFF_BVOLR   = 0x03Eu;
        private const uint OFF_MVOLXL  = 0x040u; // abs core+0x1C0 — current master vol (RO)
        private const uint OFF_MVOLXR  = 0x042u;
        private const uint OFF_ESA_HI  = 0x044u;
        private const uint OFF_ESA_LO  = 0x046u;

        // ── Per-voice register offsets (within each voice's 0x10-byte block) ──

        /// <summary>Voice register offset: Volume Left.</summary>
        public const uint VREG_VOLL  = 0x00u;
        /// <summary>Voice register offset: Volume Right.</summary>
        public const uint VREG_VOLR  = 0x02u;
        /// <summary>Voice register offset: Pitch.</summary>
        public const uint VREG_PITCH = 0x04u;
        /// <summary>Voice register offset: ADSR1.</summary>
        public const uint VREG_ADSR1 = 0x06u;
        /// <summary>Voice register offset: ADSR2.</summary>
        public const uint VREG_ADSR2 = 0x08u;
        /// <summary>Voice register offset: ENVX (read-only, current envelope).</summary>
        public const uint VREG_ENVX  = 0x0Au;
        /// <summary>Voice register offset: VOLXL (read-only, current left volume).</summary>
        public const uint VREG_VOLXL = 0x0Cu;
        /// <summary>Voice register offset: VOLXR (read-only, current right volume).</summary>
        public const uint VREG_VOLXR = 0x0Eu;

        // ── SPU2 internal audio RAM ────────────────────────────────────────────

        /// <summary>
        /// SPU2 internal audio RAM (2 MB).  ADPCM sample data and PCM buffers for
        /// all 48 voices live here.  DMA transfers write into this array via
        /// <see cref="WriteDmaData"/>.
        /// </summary>
        public readonly byte[] AudioRam = new byte[RamSize];

        // ── State ─────────────────────────────────────────────────────────────

        // Per-voice state (2 cores × 24 voices).
        private readonly Spu2Voice[,] _voices = new Spu2Voice[CoreCount, VoicesPerCore];

        // Flat 16-bit register file.  Indexed by (byte_offset >> 1).
        // Covers the full 0x800-byte / 0x400-halfword SPU2 register window.
        private readonly ushort[] _regs = new ushort[RegSize / 2];

        // Transfer start addresses per core (in halfword units, updated by TSA writes).
        private readonly uint[] _tsAddr = new uint[CoreCount];

        // ── Constructor ───────────────────────────────────────────────────────

        public Spu2()
        {
            for (int c = 0; c < CoreCount; c++)
                for (int v = 0; v < VoicesPerCore; v++)
                    _voices[c, v] = new Spu2Voice();
        }

        // ── Voice accessor ────────────────────────────────────────────────────

        /// <summary>
        /// Returns the <see cref="Spu2Voice"/> state object for the given core and voice index.
        /// </summary>
        public Spu2Voice GetVoice(int core, int voice) => _voices[core, voice];

        // ── 16-bit register I/O ───────────────────────────────────────────────

        /// <summary>
        /// Read a 16-bit SPU2 register at <paramref name="offset"/> (byte offset from
        /// <see cref="RegBase"/>).
        /// </summary>
        public ushort ReadHalf(uint offset)
        {
            if (offset >= RegSize) return 0;

            int  core    = (int)(offset / CoreStride);   // 0 or 1
            uint coreOff = offset % CoreStride;          // offset within this core

            if (coreOff < GlobalsOff) // voice registers
            {
                int  voice = (int)(coreOff / 0x10u);
                uint vreg  = coreOff % 0x10u;
                if ((uint)voice < (uint)VoicesPerCore)
                    return ReadVoiceReg(core, voice, vreg);
                return 0;
            }

            // Global register — read from flat array
            return _regs[offset / 2u];
        }

        /// <summary>
        /// Write a 16-bit SPU2 register at <paramref name="offset"/>.
        /// KON/KOF writes have side-effects on voice IsOn state; TSA writes update
        /// the transfer address; SD writes append a halfword to audio RAM.
        /// </summary>
        public void WriteHalf(uint offset, ushort value)
        {
            if (offset >= RegSize) return;

            int  core    = (int)(offset / CoreStride);
            uint coreOff = offset % CoreStride;

            if (coreOff < GlobalsOff) // voice registers
            {
                int  voice = (int)(coreOff / 0x10u);
                uint vreg  = coreOff % 0x10u;
                if ((uint)voice < (uint)VoicesPerCore)
                    WriteVoiceReg(core, voice, vreg, value);
                return;
            }

            // Store in flat register file
            _regs[offset / 2u] = value;

            // Handle side-effects for special global registers
            uint globalRelOff = coreOff - GlobalsOff;
            HandleGlobalWrite(core, globalRelOff, value);
        }

        /// <summary>
        /// Read a 32-bit SPU2 register (two consecutive 16-bit registers, little-endian).
        /// </summary>
        public uint ReadWord(uint offset)
            => (uint)(ReadHalf(offset) | (ReadHalf(offset + 2u) << 16));

        /// <summary>
        /// Write a 32-bit SPU2 register (two consecutive 16-bit writes, little-endian).
        /// </summary>
        public void WriteWord(uint offset, uint value)
        {
            WriteHalf(offset,      (ushort)value);
            WriteHalf(offset + 2u, (ushort)(value >> 16));
        }

        // ── Voice register helpers ────────────────────────────────────────────

        private ushort ReadVoiceReg(int core, int voice, uint vreg)
        {
            var v = _voices[core, voice];
            return vreg switch
            {
                VREG_VOLL  => v.VolL,
                VREG_VOLR  => v.VolR,
                VREG_PITCH => v.Pitch,
                VREG_ADSR1 => v.Adsr1,
                VREG_ADSR2 => v.Adsr2,
                VREG_ENVX  => v.Envx,
                VREG_VOLXL => v.VolxL,
                VREG_VOLXR => v.VolxR,
                _          => 0
            };
        }

        private void WriteVoiceReg(int core, int voice, uint vreg, ushort value)
        {
            var v = _voices[core, voice];
            switch (vreg)
            {
                case VREG_VOLL:  v.VolL  = value; break;
                case VREG_VOLR:  v.VolR  = value; break;
                case VREG_PITCH: v.Pitch = value; break;
                case VREG_ADSR1: v.Adsr1 = value; break;
                case VREG_ADSR2: v.Adsr2 = value; break;
                // VREG_ENVX / VREG_VOLXL / VREG_VOLXR are hardware-updated, read-only from CPU
            }
        }

        // ── Global register side-effects ──────────────────────────────────────

        private void HandleGlobalWrite(int core, uint relOff, ushort value)
        {
            switch (relOff)
            {
                case OFF_KON0: // key-on voices 0–15
                    for (int v = 0; v < 16; v++)
                    {
                        if ((value & (1u << v)) != 0)
                        {
                            _voices[core, v].IsOn = true;
                            _voices[core, v].AdpcmPrev1 = 0;
                            _voices[core, v].AdpcmPrev2 = 0;
                        }
                    }
                    break;

                case OFF_KON1: // key-on voices 16–23
                    for (int v = 0; v < 8; v++)
                    {
                        if ((value & (1u << v)) != 0)
                        {
                            _voices[core, 16 + v].IsOn = true;
                            _voices[core, 16 + v].AdpcmPrev1 = 0;
                            _voices[core, 16 + v].AdpcmPrev2 = 0;
                        }
                    }
                    break;

                case OFF_KOF0: // key-off voices 0–15
                    for (int v = 0; v < 16; v++)
                        if ((value & (1u << v)) != 0)
                            _voices[core, v].IsOn = false;
                    break;

                case OFF_KOF1: // key-off voices 16–23
                    for (int v = 0; v < 8; v++)
                        if ((value & (1u << v)) != 0)
                            _voices[core, 16 + v].IsOn = false;
                    break;

                case OFF_TSA_HI:
                    _tsAddr[core] = (_tsAddr[core] & 0x0000FFFFu) | ((uint)value << 16);
                    break;

                case OFF_TSA_LO:
                    _tsAddr[core] = (_tsAddr[core] & 0xFFFF0000u) | value;
                    break;

                case OFF_SD:
                    // Direct CPU write to the sound data port: store one halfword.
                    WriteDmaHalf(core, value);
                    break;
            }
        }

        // ── DMA data port ─────────────────────────────────────────────────────

        /// <summary>
        /// Write a 32-bit word of audio sample data from an IOP DMA transfer into
        /// SPU2 audio RAM at the current transfer address for <paramref name="core"/>.
        /// Called by the IOP DMAC when audio data is streamed from IOP RAM to the SPU2.
        /// </summary>
        public void WriteDmaData(int core, uint value)
        {
            WriteDmaHalf(core, (ushort)value);
            WriteDmaHalf(core, (ushort)(value >> 16));
        }

        /// <summary>Write a 16-bit halfword to SPU2 audio RAM and advance the TSA.</summary>
        private void WriteDmaHalf(int core, ushort value)
        {
            if ((uint)core >= (uint)CoreCount) return;

            // TSA is in halfword (2-byte) units.
            uint byteAddr = _tsAddr[core] * 2u;
            if (byteAddr + 1u < (uint)RamSize)
            {
                AudioRam[byteAddr]     = (byte)value;
                AudioRam[byteAddr + 1] = (byte)(value >> 8);
            }
            _tsAddr[core]++;
        }

        /// <summary>
        /// Return the current transfer start address for <paramref name="core"/>
        /// (in halfword units, as stored in TSA_HI:TSA_LO).
        /// </summary>
        public uint GetTransferAddr(int core)
            => (uint)core < (uint)CoreCount ? _tsAddr[core] : 0u;

        // ── ADPCM decoder ─────────────────────────────────────────────────────
        //
        // PS2 ADPCM (VAG format) coefficients.  Each filter selects a pair of
        // predictor weights (f1, f2) applied to the two previously decoded samples.
        // Integer approximation: actual weights are f1/64 and f2/64.

        private static readonly int[] FilterPos = { 0, 60, 115, 98, 122 };
        private static readonly int[] FilterNeg = { 0,  0, -52, -55, -60 };

        /// <summary>
        /// Decode one 16-byte PS2 ADPCM block into 28 signed 16-bit PCM samples.
        ///
        /// Block layout:
        /// <list type="bullet">
        ///   <item>Byte 0: header — bits [3:0] = shift factor, bits [6:4] = filter index (0–4)</item>
        ///   <item>Byte 1: flags  — bit 0 = loop-end, bit 1 = loop, bit 2 = repeat</item>
        ///   <item>Bytes 2–15: 14 data bytes, each holding two 4-bit signed nibbles
        ///         (low nibble first) for 28 samples total</item>
        /// </list>
        /// </summary>
        /// <param name="block">Source ADPCM block (must be at least 16 bytes).</param>
        /// <param name="pcm">Destination span (must have at least 28 elements).</param>
        /// <param name="prev1">Decoder history: previous sample s[−1] (updated in place).</param>
        /// <param name="prev2">Decoder history: previous sample s[−2] (updated in place).</param>
        /// <returns><c>true</c> when flag bit 0 (loop-end) is set in the block.</returns>
        public static bool DecodeAdpcmBlock(
            ReadOnlySpan<byte> block,
            Span<short>        pcm,
            ref int            prev1,
            ref int            prev2)
        {
            int header  = block[0];
            int shift   = header & 0x0F;
            int filt    = (header >> 4) & 0x07;
            if (filt > 4) filt = 4;

            bool loopEnd = (block[1] & 0x01) != 0;

            int f1     = FilterPos[filt];
            int f2     = FilterNeg[filt];
            int outIdx = 0;

            for (int i = 2; i < 16 && outIdx < 28; i++)
            {
                byte b = block[i];
                DecodeSample(b & 0x0F,        shift, f1, f2, ref prev1, ref prev2, pcm, ref outIdx);
                DecodeSample((b >> 4) & 0x0F, shift, f1, f2, ref prev1, ref prev2, pcm, ref outIdx);
            }

            return loopEnd;
        }

        private static void DecodeSample(
            int        nibble,
            int        shift,
            int        f1,
            int        f2,
            ref int    prev1,
            ref int    prev2,
            Span<short> pcm,
            ref int    idx)
        {
            if (nibble >= 8) nibble -= 16; // sign-extend 4-bit to int
            int raw = nibble << (12 - shift);
            int s   = raw + (prev1 * f1 + prev2 * f2 + 32) / 64;
            if (s < -32768) s = -32768;
            else if (s > 32767) s = 32767;
            pcm[idx++] = (short)s;
            prev2 = prev1;
            prev1 = s;
        }
    }

    // ── Per-voice state ────────────────────────────────────────────────────────

    /// <summary>
    /// Per-voice state for one SPU2 audio channel.
    /// </summary>
    public sealed class Spu2Voice
    {
        /// <summary>Volume Left register (VOLL).</summary>
        public ushort VolL  { get; set; }

        /// <summary>Volume Right register (VOLR).</summary>
        public ushort VolR  { get; set; }

        /// <summary>Pitch / sample rate register (PITCH).</summary>
        public ushort Pitch { get; set; }

        /// <summary>ADSR1 — Attack/Decay/Sustain/Release upper word.</summary>
        public ushort Adsr1 { get; set; }

        /// <summary>ADSR2 — Attack/Decay/Sustain/Release lower word.</summary>
        public ushort Adsr2 { get; set; }

        /// <summary>ENVX — Current envelope value (hardware-updated, read-only from CPU).</summary>
        public ushort Envx  { get; set; }

        /// <summary>VOLXL — Current left volume (hardware-updated, read-only from CPU).</summary>
        public ushort VolxL { get; set; }

        /// <summary>VOLXR — Current right volume (hardware-updated, read-only from CPU).</summary>
        public ushort VolxR { get; set; }

        /// <summary>
        /// True while this voice is active (KON received and KOF not yet written).
        /// </summary>
        public bool IsOn { get; set; }

        /// <summary>ADPCM decoder history: previous decoded sample s[−1].</summary>
        public int AdpcmPrev1 { get; set; }

        /// <summary>ADPCM decoder history: sample before prev1, s[−2].</summary>
        public int AdpcmPrev2 { get; set; }

        /// <summary>Start address of this voice's ADPCM sample data in SPU2 audio RAM (halfword units).</summary>
        public uint StartAddr { get; set; }

        /// <summary>Loop address in SPU2 audio RAM (halfword units).</summary>
        public uint LoopAddr { get; set; }

        /// <summary>Current read address in SPU2 audio RAM (halfword units).</summary>
        public uint CurrentAddr { get; set; }
    }
}
