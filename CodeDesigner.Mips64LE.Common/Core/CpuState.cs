using CodeDesigner.Mips64LE.Common.CPU.COP0;
using CodeDesigner.Mips64LE.Common.CPU.COP1;
using CodeDesigner.Mips64LE.Common.VectorUnits.VU0;
using CodeDesigner.Mips64LE.Common.VectorUnits.VU1;
using System;

namespace CodeDesigner.Mips64LE.Common.CPU
{
    public sealed class CpuState
    {
        // 128-bit registers (stored as pairs of 64-bit values)
        // regsLo = lower 64 bits
        // regsHi = upper 64 bits (for EE-specific 128-bit ops)
        public ulong[] RegsLo { get; } = new ulong[32];
        public ulong[] RegsHi { get; } = new ulong[32];

        // Program Counter
        public ulong PC { get; set; }

        // Multiply / Divide results (pipeline 0)
        public ulong HI { get; set; }
        public ulong LO { get; set; }

        // Multiply / Divide results (pipeline 1) - PS2 specific
        public ulong HI1 { get; set; }
        public ulong LO1 { get; set; }

        // Shift Amount register (PS2 specific)
        public int SA { get; set; }

        // COP1 FPU Accumulator register (PS2 EE specific, used by ADDA.S/MADD.S/etc.)
        public float FACC { get; set; }

        // Main memory
        public byte[] Memory { get; set; } = Array.Empty<byte>();

        // FPU registers (32 x 32-bit)
        public float[] FPR { get; } = new float[32];

        // COP0 - System Control Coprocessor
        public COP0State COP0 { get; } = new COP0State();

        // COP1 - Floating Point Coprocessor
        public COP1State COP1 { get; } = new COP1State();

        // VU0 - Vector Unit 0 (COP2 in Macro Mode)
        public VU0State VU0 { get; } = new VU0State();

        // VU1 - Vector Unit 1 (standalone microprogram execution)
        public VU1State VU1 { get; } = new VU1State();

        public CpuState()
        {
            // Initialize $zero register
            RegsLo[0] = 0;
            RegsHi[0] = 0;
        }
    }
}
