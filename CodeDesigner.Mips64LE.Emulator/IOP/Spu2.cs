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
    /// Voice address registers (PVCA, per PCSX2 layout, at core+0x1C0 + voice*12):
    /// <list type="table">
    ///   <item><term>+0x00</term><description>StartA_HI — Start Address high halfword (R/W)</description></item>
    ///   <item><term>+0x02</term><description>StartA_LO — Start Address low halfword (R/W)</description></item>
    ///   <item><term>+0x04</term><description>LoopA_HI  — Loop Address high halfword (R/W)</description></item>
    ///   <item><term>+0x06</term><description>LoopA_LO  — Loop Address low halfword (R/W)</description></item>
    ///   <item><term>+0x08</term><description>NextA_HI  — Current Address high halfword (R)</description></item>
    ///   <item><term>+0x0A</term><description>NextA_LO  — Current Address low halfword (R)</description></item>
    /// </list>
    ///
    /// Notable per-core global registers (offset from core base):
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
    ///   <item><term>+0x1B0</term><description>MVOLL  — Master Volume Left (legacy offset)</description></item>
    ///   <item><term>+0x1B2</term><description>MVOLR  — Master Volume Right (legacy offset)</description></item>
    ///   <item><term>+0x2E0</term><description>ESA_HI — Effects Start Address high halfword (R/W)</description></item>
    ///   <item><term>+0x2E2</term><description>ESA_LO — Effects Start Address low halfword (R/W)</description></item>
    ///   <item><term>+0x33C</term><description>EEA_HI — Effects End Address high halfword (R/W)</description></item>
    ///   <item><term>+0x33E</term><description>EEA_LO — Effects End Address low halfword (R/W)</description></item>
    /// </list>
    ///
    /// Reverb volume / coefficient registers ("weird area", absolute SPU2 window offsets):
    ///   Core 0 at abs 0x760, Core 1 at abs 0x788 (stride 0x28 bytes per core):
    ///   +0x00=MVOLL, +0x02=MVOLR, +0x04=FXVOLL, +0x06=FXVOLR,
    ///   +0x14=IIR_VOL, +0x16=COMB1_VOL, +0x18=COMB2_VOL, +0x1A=COMB3_VOL,
    ///   +0x1C=COMB4_VOL, +0x1E=WALL_VOL, +0x20=APF1_VOL, +0x22=APF2_VOL,
    ///   +0x24=IN_COEF_L, +0x26=IN_COEF_R.
    ///   Reference: PCSX2 pcsx2/SPU2/RegTable.cpp ("weird area" comments).
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
    /// Mixing:
    ///   <see cref="MixSamples"/> generates interleaved stereo 16-bit PCM at 48 kHz.
    ///   All 48 voices are mixed through ADSR envelopes and voice volumes, then
    ///   per-core reverb is applied using the SPU2 reverb algorithm (identical to
    ///   PCSX2 pcsx2/SPU2/Reverb.cpp), and finally per-core master volumes are applied.
    ///   Reference: PCSX2 pcsx2/SPU2/Mixer.cpp, Reverb.cpp, ADSR.cpp.
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
        private const uint CoreStride = 0x400u;

        // Within each core's 0x400-byte window, voices occupy offsets 0–0x17F
        // (24 voices × 0x10 bytes); global registers start at 0x180.
        private const uint GlobalsOff = 0x180u;

        // ── Global register offsets (relative to GlobalsOff within each core) ─

        private const uint OFF_PMON0  = 0x000u;
        private const uint OFF_PMON1  = 0x002u;
        private const uint OFF_NON0   = 0x004u;
        private const uint OFF_NON1   = 0x006u;
        private const uint OFF_VMIXL0 = 0x008u;
        private const uint OFF_VMIXL1 = 0x00Au;
        private const uint OFF_VMIXEL0= 0x00Cu;
        private const uint OFF_VMIXEL1= 0x00Eu;
        private const uint OFF_VMIXR0 = 0x010u;
        private const uint OFF_VMIXR1 = 0x012u;
        private const uint OFF_VMIXER0= 0x014u;
        private const uint OFF_VMIXER1= 0x016u;
        private const uint OFF_MMIX   = 0x018u; // abs core+0x198
        private const uint OFF_ATTR   = 0x01Au; // abs core+0x19A
        private const uint OFF_IRQA_HI= 0x01Cu;
        private const uint OFF_IRQA_LO= 0x01Eu;
        private const uint OFF_KON0   = 0x020u; // abs core+0x1A0 — key-on voices  0–15
        private const uint OFF_KON1   = 0x022u; // abs core+0x1A2 — key-on voices 16–23
        private const uint OFF_KOF0   = 0x024u; // abs core+0x1A4 — key-off voices  0–15
        private const uint OFF_KOF1   = 0x026u; // abs core+0x1A6 — key-off voices 16–23
        private const uint OFF_TSA_HI = 0x028u; // abs core+0x1A8 — TSA high halfword
        private const uint OFF_TSA_LO = 0x02Au; // abs core+0x1AA — TSA low halfword
        private const uint OFF_SD     = 0x02Cu; // abs core+0x1AC — sound data (DMA port)
        private const uint OFF_MVOLL  = 0x030u; // abs core+0x1B0 — master volume left (legacy)
        private const uint OFF_MVOLR  = 0x032u; // abs core+0x1B2 — master volume right (legacy)
        private const uint OFF_EVOLL  = 0x034u;
        private const uint OFF_EVOLR  = 0x036u;
        private const uint OFF_AVOLL  = 0x038u;
        private const uint OFF_AVOLR  = 0x03Au;
        private const uint OFF_BVOLL  = 0x03Cu;
        private const uint OFF_BVOLR  = 0x03Eu;

        // ── Voice address (PVCA) register area ────────────────────────────────
        // Per PCSX2 RegTable.cpp: voice N of core C at abs C*0x400 + 0x1C0 + N*12.
        // Six halfwords per voice: SA_HI, SA_LO, LoopA_HI, LoopA_LO, NextA_HI, NextA_LO.
        private const uint VoiceAddrBase   = 0x1C0u;  // absolute core-relative offset
        private const uint VoiceAddrStride = 12u;      // bytes per voice entry (6 halfwords)
        private const uint VA_SA_HI    = 0u;           // StartA bits [31:16]
        private const uint VA_SA_LO    = 2u;           // StartA bits [15:0]
        private const uint VA_LOOPA_HI = 4u;           // LoopA bits [31:16]
        private const uint VA_LOOPA_LO = 6u;           // LoopA bits [15:0]
        private const uint VA_NEXTA_HI = 8u;           // NextA bits [31:16] (read-only)
        private const uint VA_NEXTA_LO = 10u;          // NextA bits [15:0]  (read-only)

        // ── Effects / reverb address registers ────────────────────────────────
        // ESA and EEA follow the PVCA area (24 voices × 12 bytes = 0x120, so PVCA ends at 0x2E0).
        private const uint EffectsStartOff = 0x2E0u;  // core-relative; HI then LO halfword
        private const uint EffectsEndOff   = 0x33Cu;  // core-relative; HI then LO halfword

        // Reverb spatial parameter registers start at core+0x2E4 (after EffectsStartA).
        // Each parameter is 32-bit stored as two consecutive halfwords (HI first, then LO).
        // Order matches PCSX2 RegTable.cpp PREVB_REG entries.
        private const uint RevbParamBase = 0x2E4u;
        private const int RIDX_APF1_SIZE  =  0;
        private const int RIDX_APF2_SIZE  =  1;
        private const int RIDX_SAME_L_DST =  2;
        private const int RIDX_SAME_R_DST =  3;
        private const int RIDX_COMB1_L_SRC=  4;
        private const int RIDX_COMB1_R_SRC=  5;
        private const int RIDX_COMB2_L_SRC=  6;
        private const int RIDX_COMB2_R_SRC=  7;
        private const int RIDX_SAME_L_SRC =  8;
        private const int RIDX_SAME_R_SRC =  9;
        private const int RIDX_DIFF_L_DST = 10;
        private const int RIDX_DIFF_R_DST = 11;
        private const int RIDX_COMB3_L_SRC= 12;
        private const int RIDX_COMB3_R_SRC= 13;
        private const int RIDX_COMB4_L_SRC= 14;
        private const int RIDX_COMB4_R_SRC= 15;
        private const int RIDX_DIFF_L_SRC = 16;
        private const int RIDX_DIFF_R_SRC = 17;
        private const int RIDX_APF1_L_DST = 18;
        private const int RIDX_APF1_R_DST = 19;
        private const int RIDX_APF2_L_DST = 20;
        private const int RIDX_APF2_R_DST = 21;

        // ── "Weird area" — master volumes and reverb coefficient volumes ───────
        // Absolute SPU2 window offsets (not per-core), per PCSX2 RegTable.cpp.
        // Core 0 starts at 0x760, Core 1 at 0x788 (stride 0x28 = 40 bytes).
        private const uint WeirdAreaBase   = 0x760u;
        private const uint WeirdAreaStride = 0x28u;
        private const uint WA_MVOLL     = 0x00u;
        private const uint WA_MVOLR     = 0x02u;
        private const uint WA_FXVOLL    = 0x04u;
        private const uint WA_FXVOLR    = 0x06u;
        private const uint WA_IIR_VOL   = 0x14u;
        private const uint WA_COMB1_VOL = 0x16u;
        private const uint WA_COMB2_VOL = 0x18u;
        private const uint WA_COMB3_VOL = 0x1Au;
        private const uint WA_COMB4_VOL = 0x1Cu;
        private const uint WA_WALL_VOL  = 0x1Eu;
        private const uint WA_APF1_VOL  = 0x20u;
        private const uint WA_APF2_VOL  = 0x22u;
        private const uint WA_IN_COEF_L = 0x24u;
        private const uint WA_IN_COEF_R = 0x26u;

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

        private readonly Spu2Voice[,] _voices = new Spu2Voice[CoreCount, VoicesPerCore];

        // Flat 16-bit register file.  Indexed by (byte_offset_from_RegBase >> 1).
        private readonly ushort[] _regs = new ushort[RegSize / 2];

        // Transfer start addresses per core (in halfword units, updated by TSA writes).
        private readonly uint[] _tsAddr = new uint[CoreCount];

        // Effects (reverb) buffer start/end per core (in halfword units).
        private readonly uint[] _effectsStartA = new uint[CoreCount];
        private readonly uint[] _effectsEndA   = new uint[CoreCount];

        // Reverb cycle counter per core (alternates L/R channel processing).
        private readonly uint[] _revbCycle = new uint[CoreCount];

        // Cached last-L reverb output per core (used to pair L and R output).
        private readonly int[] _revbLastL = new int[CoreCount];

        // ── Constructor ───────────────────────────────────────────────────────

        public Spu2()
        {
            for (int c = 0; c < CoreCount; c++)
                for (int v = 0; v < VoicesPerCore; v++)
                    _voices[c, v] = new Spu2Voice();
        }

        // ── Voice accessor ────────────────────────────────────────────────────

        /// <summary>Returns the <see cref="Spu2Voice"/> for the given core and voice index.</summary>
        public Spu2Voice GetVoice(int core, int voice) => _voices[core, voice];

        // ── 16-bit register I/O ───────────────────────────────────────────────

        /// <summary>
        /// Read a 16-bit SPU2 register at <paramref name="offset"/> (byte offset from
        /// <see cref="RegBase"/>).
        /// </summary>
        public ushort ReadHalf(uint offset)
        {
            if (offset >= RegSize) return 0;

            int  core    = (int)(offset / CoreStride);
            uint coreOff = offset % CoreStride;

            if (coreOff < GlobalsOff)
            {
                int  voice = (int)(coreOff / 0x10u);
                uint vreg  = coreOff % 0x10u;
                if ((uint)voice < (uint)VoicesPerCore)
                    return ReadVoiceReg(core, voice, vreg);
                return 0;
            }

            // PVCA voice address area: NextA sub-fields return live CurrentAddr.
            if (coreOff >= VoiceAddrBase &&
                coreOff <  VoiceAddrBase + VoicesPerCore * VoiceAddrStride)
            {
                uint voiceOff = coreOff - VoiceAddrBase;
                int  v        = (int)(voiceOff / VoiceAddrStride);
                uint subOff   = voiceOff % VoiceAddrStride;
                if ((uint)v < (uint)VoicesPerCore &&
                    (subOff == VA_NEXTA_HI || subOff == VA_NEXTA_LO))
                {
                    uint cur = _voices[core, v].CurrentAddr;
                    return subOff == VA_NEXTA_HI
                        ? (ushort)(cur >> 16)
                        : (ushort)(cur & 0xFFFF);
                }
            }

            return _regs[offset / 2u];
        }

        /// <summary>
        /// Write a 16-bit SPU2 register at <paramref name="offset"/>.
        /// KON/KOF have side-effects on voice state; SA/LoopA registers update voice
        /// address fields; TSA/SD handle DMA; ESA/EEA update reverb buffer bounds.
        /// </summary>
        public void WriteHalf(uint offset, ushort value)
        {
            if (offset >= RegSize) return;

            int  core    = (int)(offset / CoreStride);
            uint coreOff = offset % CoreStride;

            if (coreOff < GlobalsOff)
            {
                int  voice = (int)(coreOff / 0x10u);
                uint vreg  = coreOff % 0x10u;
                if ((uint)voice < (uint)VoicesPerCore)
                    WriteVoiceReg(core, voice, vreg, value);
                return;
            }

            _regs[offset / 2u] = value;
            uint globalRelOff = coreOff - GlobalsOff;
            HandleGlobalWrite(core, coreOff, globalRelOff, value);
        }

        /// <summary>Read a 32-bit SPU2 register (two 16-bit reads, little-endian).</summary>
        public uint ReadWord(uint offset)
            => (uint)(ReadHalf(offset) | (ReadHalf(offset + 2u) << 16));

        /// <summary>Write a 32-bit SPU2 register (two 16-bit writes, little-endian).</summary>
        public void WriteWord(uint offset, uint value)
        {
            WriteHalf(offset,      (ushort)value);
            WriteHalf(offset + 2u, (ushort)(value >> 16));
        }

        // ── Voice register helpers ─────────────────────────────────────────────

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
                VREG_ENVX  => (ushort)v.AdsrVol,
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
            }
        }

        // ── Global register side-effects ──────────────────────────────────────

        private void HandleGlobalWrite(int core, uint coreOff, uint relOff, ushort value)
        {
            // ── PVCA voice address area (core+0x1C0 to core+0x2DF) ─────────────
            if (coreOff >= VoiceAddrBase &&
                coreOff <  VoiceAddrBase + VoicesPerCore * VoiceAddrStride)
            {
                uint voiceOff = coreOff - VoiceAddrBase;
                int  v        = (int)(voiceOff / VoiceAddrStride);
                uint subOff   = voiceOff % VoiceAddrStride;
                if ((uint)v < (uint)VoicesPerCore)
                {
                    var voice = _voices[core, v];
                    switch (subOff)
                    {
                        case VA_SA_HI:
                            voice.StartAddr = (voice.StartAddr & 0x0000FFFFu) | ((uint)value << 16);
                            break;
                        case VA_SA_LO:
                            voice.StartAddr = (voice.StartAddr & 0xFFFF0000u) | value;
                            break;
                        case VA_LOOPA_HI:
                            voice.LoopAddr = (voice.LoopAddr & 0x0000FFFFu) | ((uint)value << 16);
                            break;
                        case VA_LOOPA_LO:
                            voice.LoopAddr = (voice.LoopAddr & 0xFFFF0000u) | value;
                            break;
                        // VA_NEXTA_HI / VA_NEXTA_LO are read-only; write ignored.
                    }
                }
                return;
            }

            // ── Effects start/end address registers ────────────────────────────
            if (coreOff == EffectsStartOff)
            { _effectsStartA[core] = (_effectsStartA[core] & 0x0000FFFFu) | ((uint)value << 16); return; }
            if (coreOff == EffectsStartOff + 2u)
            { _effectsStartA[core] = (_effectsStartA[core] & 0xFFFF0000u) | value; return; }
            if (coreOff == EffectsEndOff)
            { _effectsEndA[core] = (_effectsEndA[core] & 0x0000FFFFu) | ((uint)value << 16); return; }
            if (coreOff == EffectsEndOff + 2u)
            { _effectsEndA[core] = (_effectsEndA[core] & 0xFFFF0000u) | value; return; }

            // ── Legacy special registers ────────────────────────────────────────
            switch (relOff)
            {
                case OFF_KON0:
                    for (int v = 0; v < 16; v++)
                        if ((value & (1u << v)) != 0) KeyOn(core, v);
                    break;

                case OFF_KON1:
                    for (int v = 0; v < 8; v++)
                        if ((value & (1u << v)) != 0) KeyOn(core, 16 + v);
                    break;

                case OFF_KOF0:
                    for (int v = 0; v < 16; v++)
                        if ((value & (1u << v)) != 0) KeyOff(core, v);
                    break;

                case OFF_KOF1:
                    for (int v = 0; v < 8; v++)
                        if ((value & (1u << v)) != 0) KeyOff(core, 16 + v);
                    break;

                case OFF_TSA_HI:
                    _tsAddr[core] = (_tsAddr[core] & 0x0000FFFFu) | ((uint)value << 16);
                    break;

                case OFF_TSA_LO:
                    _tsAddr[core] = (_tsAddr[core] & 0xFFFF0000u) | value;
                    break;

                case OFF_SD:
                    WriteDmaHalf(core, value);
                    break;
            }
        }

        // ── Key-on / key-off helpers ──────────────────────────────────────────

        private void KeyOn(int core, int v)
        {
            var voice = _voices[core, v];
            voice.IsOn        = true;
            voice.AdpcmPrev1  = 0;
            voice.AdpcmPrev2  = 0;
            voice.CurrentAddr = voice.StartAddr;
            voice.DecIdx      = 28;    // force decode of first block
            voice.PitchFrac   = 0;
            voice.AdsrVol     = 0;
            voice.AdsrPhase   = 0;     // Attack
            voice.AdsrCounter = 0;
        }

        private void KeyOff(int core, int v)
        {
            var voice = _voices[core, v];
            if (voice.IsOn && voice.AdsrPhase < 3)
                voice.AdsrPhase = 3; // Release
            voice.IsOn = false;
        }

        // ── DMA data port ─────────────────────────────────────────────────────

        /// <summary>
        /// Write a 32-bit word of audio sample data from IOP DMA into SPU2 audio RAM
        /// at the current transfer address for <paramref name="core"/>.
        /// </summary>
        public void WriteDmaData(int core, uint value)
        {
            WriteDmaHalf(core, (ushort)value);
            WriteDmaHalf(core, (ushort)(value >> 16));
        }

        private void WriteDmaHalf(int core, ushort value)
        {
            if ((uint)core >= (uint)CoreCount) return;
            uint byteAddr = _tsAddr[core] * 2u;
            if (byteAddr + 1u < (uint)RamSize)
            {
                AudioRam[byteAddr]     = (byte)value;
                AudioRam[byteAddr + 1] = (byte)(value >> 8);
            }
            _tsAddr[core]++;
        }

        /// <summary>Returns the current TSA for <paramref name="core"/> (in halfword units).</summary>
        public uint GetTransferAddr(int core)
            => (uint)core < (uint)CoreCount ? _tsAddr[core] : 0u;

        /// <summary>Returns the Effects Start Address for <paramref name="core"/> (halfword units).</summary>
        public uint GetEffectsStartA(int core)
            => (uint)core < (uint)CoreCount ? _effectsStartA[core] : 0u;

        /// <summary>Returns the Effects End Address for <paramref name="core"/> (halfword units).</summary>
        public uint GetEffectsEndA(int core)
            => (uint)core < (uint)CoreCount ? _effectsEndA[core] : 0u;

        // ── Audio mixer ───────────────────────────────────────────────────────

        /// <summary>
        /// Mix <paramref name="sampleCount"/> stereo samples at 48 kHz and return them
        /// as an interleaved 16-bit PCM array (L, R, L, R, …).
        ///
        /// For each output sample:
        ///  1. All active voices across both cores are advanced by their PITCH,
        ///     ADPCM-decoded as needed, and ADSR-enveloped.
        ///  2. Per-core voice outputs are summed.
        ///  3. Per-core reverb (the PS2 SPU2 reverb algorithm) is applied.
        ///  4. Per-core master volumes scale the dry+wet sum.
        ///  5. Both cores contribute to the final stereo output.
        ///
        /// Reference: PCSX2 pcsx2/SPU2/Mixer.cpp <c>spu2Mix()</c>.
        /// </summary>
        public short[] MixSamples(int sampleCount)
        {
            var output = new short[sampleCount * 2];

            for (int s = 0; s < sampleCount; s++)
            {
                int outL = 0, outR = 0;

                for (int c = 0; c < CoreCount; c++)
                {
                    int coreL = 0, coreR = 0;

                    for (int v = 0; v < VoicesPerCore; v++)
                    {
                        var voice = _voices[c, v];
                        if (!voice.IsOn) continue;

                        short sample = GetNextVoiceSample(voice);
                        int env = voice.AdsrVol;
                        int envSample = (sample * env) >> 15;

                        coreL += (envSample * (short)voice.VolL) >> 15;
                        coreR += (envSample * (short)voice.VolR) >> 15;
                    }

                    var (rvL, rvR) = DoReverb(c, coreL, coreR);

                    int mVolL = GetMasterVol(c, false);
                    int mVolR = GetMasterVol(c, true);

                    outL += ApplyVol(coreL + rvL, mVolL);
                    outR += ApplyVol(coreR + rvR, mVolR);
                }

                output[s * 2]     = ClampS16(outL);
                output[s * 2 + 1] = ClampS16(outR);
            }

            return output;
        }

        // ── Voice sample generation ───────────────────────────────────────────

        private short GetNextVoiceSample(Spu2Voice v)
        {
            // Pitch = 0x1000 → consume one ADPCM sample per output sample (48 kHz 1:1).
            v.PitchFrac += v.Pitch;

            while (v.PitchFrac >= 0x1000u)
            {
                v.PitchFrac -= 0x1000u;

                v.DecIdx++;
                if (v.DecIdx >= 28)
                    DecodeNextBlock(v);

                StepAdsr(v);
            }

            return v.IsOn && v.DecIdx < 28 ? v.DecBuf[v.DecIdx] : (short)0;
        }

        private void DecodeNextBlock(Spu2Voice v)
        {
            uint byteAddr = v.CurrentAddr << 1; // halfword → byte address
            if (byteAddr + 16 > (uint)AudioRam.Length)
            {
                v.IsOn = false;
                return;
            }

            byte flags = AudioRam[byteAddr + 1];

            int p1 = v.AdpcmPrev1;
            int p2 = v.AdpcmPrev2;
            DecodeAdpcmBlock(
                new ReadOnlySpan<byte>(AudioRam, (int)byteAddr, 16),
                v.DecBuf,
                ref p1, ref p2);
            v.AdpcmPrev1 = p1;
            v.AdpcmPrev2 = p2;

            // LOOP_START (bit 2): update loop address.
            if ((flags & 0x04) != 0)
                v.LoopAddr = v.CurrentAddr;

            v.CurrentAddr += 8u; // 16 bytes = 8 halfwords
            v.DecIdx = 0;

            // LOOP_END (bit 0): either loop back or stop.
            if ((flags & 0x01) != 0)
            {
                if ((flags & 0x02) != 0)
                {
                    v.CurrentAddr = v.LoopAddr;
                    v.AdpcmPrev1 = 0;
                    v.AdpcmPrev2 = 0;
                }
                else
                {
                    v.IsOn = false;
                    v.AdsrPhase = 4;
                }
            }
        }

        // ── ADSR envelope ─────────────────────────────────────────────────────
        //
        // Implements the PS2 SPU2 ADSR algorithm from PCSX2 ADSR.cpp V_ADSR::Calculate().
        // ADSR1 bits: [15]=AttackMode, [14:10]=AttackShift, [9:8]=AttackStep,
        //             [7:4]=DecayShift, [3:0]=SustainLevel.
        // ADSR2 bits: [15]=SustainDir, [14]=SustainMode, [13:8]=SustainShift,
        //             [7:5]=SustainStep, [4]=ReleaseMode, [3:0]=ReleaseShift.

        private static void StepAdsr(Spu2Voice v)
        {
            if (v.AdsrPhase >= 4) return; // stopped

            int adsr1 = v.Adsr1;
            int adsr2 = v.Adsr2;

            int shift, step, target;
            bool decr, exp;

            switch (v.AdsrPhase)
            {
                case 0: // Attack
                    shift  = (adsr1 >> 10) & 0x1F;
                    step   = 7 - ((adsr1 >> 8) & 0x3);
                    decr   = false;
                    exp    = (adsr1 >> 15) != 0;
                    target = 0x7FFF;
                    break;
                case 1: // Decay (always exponential)
                    shift  = (adsr1 >> 4) & 0xF;
                    step   = -8;
                    decr   = true;
                    exp    = true;
                    target = ((adsr1 & 0xF) + 1) << 11;
                    break;
                case 2: // Sustain
                    shift  = (adsr2 >> 8) & 0x3F;
                    step   = 7 - ((adsr2 >> 5) & 0x7);
                    decr   = (adsr2 >> 15) != 0;
                    exp    = (adsr2 >> 14) != 0;
                    if (decr) step = ~step;
                    target = 0;
                    break;
                case 3: // Release
                    shift  = adsr2 & 0xF;
                    step   = -8;
                    decr   = true;
                    exp    = ((adsr2 >> 4) & 1) != 0;
                    target = 0;
                    break;
                default:
                    return;
            }

            int counterInc = 0x8000 >> Math.Max(0, shift - 11);
            int levelInc   = step << Math.Max(0, 11 - shift);

            if (exp)
            {
                if (!decr && v.AdsrVol > 0x6000)
                    counterInc >>= 2;
                if (decr)
                    levelInc = (short)((levelInc * v.AdsrVol) >> 15);
            }

            counterInc = Math.Max(1, counterInc);
            v.AdsrCounter += (uint)counterInc;

            if (v.AdsrCounter >= 0x8000u)
            {
                v.AdsrCounter = 0;
                v.AdsrVol = Math.Clamp(v.AdsrVol + levelInc, 0, 0x7FFF);
            }

            // Phase transitions
            if (v.AdsrPhase == 0 && !decr && v.AdsrVol >= target)
                v.AdsrPhase = 1;
            else if (v.AdsrPhase == 1 && decr && v.AdsrVol <= target)
                v.AdsrPhase = 2;
            else if (v.AdsrPhase == 3 && v.AdsrVol <= 0)
            {
                v.AdsrVol   = 0;
                v.AdsrPhase = 4;
                v.IsOn      = false;
            }
        }

        // ── Reverb ────────────────────────────────────────────────────────────
        //
        // PS2 SPU2 reverb algorithm.  Reference: PCSX2 pcsx2/SPU2/Reverb.cpp.
        // Uses a circular buffer in AudioRam[ESA..EEA] as a delay line.
        // Processes L and R channels on alternating cycles; stereo output is
        // accumulated over the two half-cycles.

        private (int outL, int outR) DoReverb(int core, int inL, int inR)
        {
            uint esA = _effectsStartA[core];
            uint eeA = _effectsEndA[core];
            if (esA >= eeA) return (0, 0);

            bool R = (_revbCycle[core] & 1u) != 0;

            int sameSrc = GetRevbParam(core, R ? RIDX_SAME_R_SRC : RIDX_SAME_L_SRC);
            int sameDst = GetRevbParam(core, R ? RIDX_SAME_R_DST : RIDX_SAME_L_DST);
            int diffSrc = GetRevbParam(core, R ? RIDX_DIFF_L_SRC : RIDX_DIFF_R_SRC);
            int diffDst = GetRevbParam(core, R ? RIDX_DIFF_R_DST : RIDX_DIFF_L_DST);
            int c1Src   = GetRevbParam(core, R ? RIDX_COMB1_R_SRC : RIDX_COMB1_L_SRC);
            int c2Src   = GetRevbParam(core, R ? RIDX_COMB2_R_SRC : RIDX_COMB2_L_SRC);
            int c3Src   = GetRevbParam(core, R ? RIDX_COMB3_R_SRC : RIDX_COMB3_L_SRC);
            int c4Src   = GetRevbParam(core, R ? RIDX_COMB4_R_SRC : RIDX_COMB4_L_SRC);
            int apf1sz  = GetRevbParam(core, RIDX_APF1_SIZE);
            int apf1Dst = GetRevbParam(core, R ? RIDX_APF1_R_DST : RIDX_APF1_L_DST);
            int apf2sz  = GetRevbParam(core, RIDX_APF2_SIZE);
            int apf2Dst = GetRevbParam(core, R ? RIDX_APF2_R_DST : RIDX_APF2_L_DST);

            short iirVol   = GetRevbCoef(core, WA_IIR_VOL);
            short wallVol  = GetRevbCoef(core, WA_WALL_VOL);
            short c1Vol    = GetRevbCoef(core, WA_COMB1_VOL);
            short c2Vol    = GetRevbCoef(core, WA_COMB2_VOL);
            short c3Vol    = GetRevbCoef(core, WA_COMB3_VOL);
            short c4Vol    = GetRevbCoef(core, WA_COMB4_VOL);
            short apf1Vol  = GetRevbCoef(core, WA_APF1_VOL);
            short apf2Vol  = GetRevbCoef(core, WA_APF2_VOL);
            short inCoef   = GetRevbCoef(core, R ? WA_IN_COEF_R : WA_IN_COEF_L);
            short fxVolL   = GetRevbCoef(core, WA_FXVOLL);
            short fxVolR   = GetRevbCoef(core, WA_FXVOLR);

            uint sameSrcA = RevbIdx(core, sameSrc);
            uint sameDstA = RevbIdx(core, sameDst);
            uint samePrvA = RevbIdx(core, sameDst - 1);
            uint diffSrcA = RevbIdx(core, diffSrc);
            uint diffDstA = RevbIdx(core, diffDst);
            uint diffPrvA = RevbIdx(core, diffDst - 1);
            uint c1A      = RevbIdx(core, c1Src);
            uint c2A      = RevbIdx(core, c2Src);
            uint c3A      = RevbIdx(core, c3Src);
            uint c4A      = RevbIdx(core, c4Src);
            uint apf1SrcA = RevbIdx(core, apf1Dst - apf1sz);
            uint apf1DstA = RevbIdx(core, apf1Dst);
            uint apf2SrcA = RevbIdx(core, apf2Dst - apf2sz);
            uint apf2DstA = RevbIdx(core, apf2Dst);

            int input = ClampS16(R ? inR : inL);
            int inp   = MulVol(inCoef, input);

            int same = MulVol(iirVol,
                          inp + MulVol(wallVol, RevbRead(sameSrcA)) - RevbRead(samePrvA))
                       + RevbRead(samePrvA);
            int diff = MulVol(iirVol,
                          inp + MulVol(wallVol, RevbRead(diffSrcA)) - RevbRead(diffPrvA))
                       + RevbRead(diffPrvA);

            int comb = MulVol(c1Vol, RevbRead(c1A))
                     + MulVol(c2Vol, RevbRead(c2A))
                     + MulVol(c3Vol, RevbRead(c3A))
                     + MulVol(c4Vol, RevbRead(c4A));

            int apf1In  = comb - MulVol(apf1Vol, RevbRead(apf1SrcA));
            int apf1Out = RevbRead(apf1SrcA) + MulVol(apf1Vol, apf1In);
            int apf2In  = apf1Out - MulVol(apf2Vol, RevbRead(apf2SrcA));
            int apf2Out = RevbRead(apf2SrcA) + MulVol(apf2Vol, apf2In);

            RevbWrite(sameDstA, ClampS16(same));
            RevbWrite(diffDstA, ClampS16(diff));
            RevbWrite(apf1DstA, ClampS16(apf1In));
            RevbWrite(apf2DstA, ClampS16(apf2In));

            int outSample = ClampS16(apf2Out);
            _revbCycle[core]++;

            if (!R)
            {
                _revbLastL[core] = MulVol(fxVolL, outSample);
                return (0, 0);
            }
            else
            {
                return (_revbLastL[core], MulVol(fxVolR, outSample));
            }
        }

        private uint RevbIdx(int core, int offset)
        {
            uint esA = _effectsStartA[core];
            uint eeA = (_effectsEndA[core] | 0xFFFFu);
            uint len = eeA - esA + 1u;
            uint x   = (uint)((_revbCycle[core] >> 1) + offset) % len + esA;
            return x & 0xFFFFFu;
        }

        private short RevbRead(uint addr)
        {
            uint b = (addr & 0xFFFFFu) << 1;
            if (b + 1u < (uint)AudioRam.Length)
                return (short)(AudioRam[b] | (AudioRam[b + 1] << 8));
            return 0;
        }

        private void RevbWrite(uint addr, short val)
        {
            uint b = (addr & 0xFFFFFu) << 1;
            if (b + 1u < (uint)AudioRam.Length)
            {
                AudioRam[b]     = (byte)(ushort)val;
                AudioRam[b + 1] = (byte)((ushort)val >> 8);
            }
        }

        private int GetRevbParam(int core, int ridx)
        {
            uint absOff = (uint)core * CoreStride + RevbParamBase + (uint)(ridx * 4);
            uint hi = _regs[absOff / 2u];
            uint lo = _regs[(absOff + 2u) / 2u];
            return (int)((hi << 16) | lo);
        }

        private short GetRevbCoef(int core, uint waOffset)
        {
            uint absOff = WeirdAreaBase + (uint)core * WeirdAreaStride + waOffset;
            return (short)_regs[absOff / 2u];
        }

        // ── Volume helpers ────────────────────────────────────────────────────

        private int GetMasterVol(int core, bool right)
        {
            // Legacy location (core+0x1B0/0x1B2) — used by most PS2 BIOS/games.
            uint legOff = (uint)core * CoreStride + GlobalsOff + (right ? OFF_MVOLR : OFF_MVOLL);
            short legVol = (short)_regs[legOff / 2u];
            if (legVol != 0) return legVol;

            // PCSX2-compatible "weird area" location (abs 0x760/0x762 + core*0x28).
            uint waOff = WeirdAreaBase + (uint)core * WeirdAreaStride + (right ? WA_MVOLR : WA_MVOLL);
            return (short)_regs[waOff / 2u];
        }

        private static int ApplyVol(int data, int vol) => (data * vol) >> 15;

        private static int MulVol(int x, int y) => (x * y) >> 15;

        private static short ClampS16(int v)
            => v < -32768 ? (short)-32768 : v > 32767 ? (short)32767 : (short)v;

        // ── ADPCM decoder ─────────────────────────────────────────────────────
        //
        // PS2 ADPCM (VAG format) coefficients.  Each filter selects a pair of
        // predictor weights (f1, f2) applied to the two previously decoded samples.

        private static readonly int[] FilterPos = { 0, 60, 115, 98, 122 };
        private static readonly int[] FilterNeg = { 0,  0, -52, -55, -60 };

        /// <summary>
        /// Decode one 16-byte PS2 ADPCM block into 28 signed 16-bit PCM samples.
        ///
        /// Block layout:
        /// <list type="bullet">
        ///   <item>Byte 0: header — bits [3:0] = shift factor, bits [6:4] = filter index (0–4)</item>
        ///   <item>Byte 1: flags  — bit 0 = loop-end, bit 1 = loop, bit 2 = loop-start</item>
        ///   <item>Bytes 2–15: 14 data bytes, each holding two 4-bit signed nibbles
        ///         (low nibble first) for 28 samples total</item>
        /// </list>
        /// </summary>
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
            int         nibble,
            int         shift,
            int         f1,
            int         f2,
            ref int     prev1,
            ref int     prev2,
            Span<short> pcm,
            ref int     idx)
        {
            if (nibble >= 8) nibble -= 16;
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
    /// Includes register-backed fields, ADPCM decoder state, ADSR envelope state,
    /// and pitch counter for the audio mixer.
    /// </summary>
    public sealed class Spu2Voice
    {
        // ── Register-backed fields ─────────────────────────────────────────────

        /// <summary>Volume Left register (VOLL).</summary>
        public ushort VolL  { get; set; }

        /// <summary>Volume Right register (VOLR).</summary>
        public ushort VolR  { get; set; }

        /// <summary>Pitch / sample rate register (PITCH). 0x1000 = 48 kHz output rate.</summary>
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

        /// <summary>True while this voice is active (KON received, KOF not yet written).</summary>
        public bool IsOn { get; set; }

        // ── Address fields (from PVCA registers) ──────────────────────────────

        /// <summary>Start address of ADPCM data in SPU2 audio RAM (halfword units).</summary>
        public uint StartAddr { get; set; }

        /// <summary>Loop address in SPU2 audio RAM (halfword units).</summary>
        public uint LoopAddr { get; set; }

        /// <summary>Current read address in SPU2 audio RAM (halfword units).</summary>
        public uint CurrentAddr { get; set; }

        // ── ADPCM decoder state ───────────────────────────────────────────────

        /// <summary>ADPCM decoder history: previous decoded sample s[−1].</summary>
        public int AdpcmPrev1 { get; set; }

        /// <summary>ADPCM decoder history: sample before prev1, s[−2].</summary>
        public int AdpcmPrev2 { get; set; }

        /// <summary>Decoded PCM samples for the current 16-byte ADPCM block (28 samples).</summary>
        public short[] DecBuf { get; } = new short[28];

        /// <summary>
        /// Current read position within <see cref="DecBuf"/>.
        /// Initialised to 28 so the first call triggers a block decode.
        /// </summary>
        public int DecIdx { get; set; } = 28;

        // ── Pitch interpolation ───────────────────────────────────────────────

        /// <summary>
        /// Fractional pitch accumulator.  Incremented by <see cref="Pitch"/> each output
        /// sample; when it reaches 0x1000 a new ADPCM source sample is consumed.
        /// </summary>
        public uint PitchFrac { get; set; }

        // ── ADSR envelope ─────────────────────────────────────────────────────

        /// <summary>
        /// Current ADSR envelope volume (0–0x7FFF).  Multiplied into the decoded PCM
        /// sample before voice volume scaling is applied.
        /// </summary>
        public int AdsrVol { get; set; }

        /// <summary>
        /// ADSR phase: 0 = Attack, 1 = Decay, 2 = Sustain, 3 = Release, 4 = Stopped.
        /// </summary>
        public int AdsrPhase { get; set; }

        /// <summary>Internal ADSR rate counter (0–0x7FFF before triggering a level step).</summary>
        public uint AdsrCounter { get; set; }
    }
}
