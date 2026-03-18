using CodeDesigner.Mips64LE.Common.Interfaces;
using CodeDesigner.Mips64LE.Common.CPU.COP0;
using CodeDesigner.Mips64LE.Common.CPU.COP1;
using CodeDesigner.Mips64LE.Common.VectorUnits.VU0;
using CodeDesigner.Mips64LE.Common.VectorUnits.VU1;
using System;

namespace CodeDesigner.Mips64LE.Common.CPU
{
    /// <summary>
    /// Signal set by instruction <c>Exec()</c> methods to indicate a CPU-level
    /// exception without throwing a C# exception.  The emulator checks this field
    /// after every <c>ExecuteGeneric()</c> call and dispatches through COP0.
    /// </summary>
    public enum CpuExceptionSignal : byte
    {
        None       = 0,
        Syscall    = 1,
        Breakpoint = 2,
        Trap       = 3,
    }

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

        // CPU exception signal – set by instruction Exec() instead of throwing
        // a C# exception, then consumed by the emulator's Step() loop.
        // Always cleared to None before each instruction is executed.
        public CpuExceptionSignal ExceptionSignal;

        // Main memory (direct array, used when Bus is null)
        public byte[] Memory { get; set; } = Array.Empty<byte>();

        // Memory bus – when set, all load/store instructions route through this
        // for full virtual-address translation and I/O register dispatch.
        public IPs2MemoryBus? Bus { get; set; }

        // IOP memory bus – when set (IOP bridge path), load/store instructions
        // route through this for IOP address translation and I/O dispatch.
        public IIopMemoryBus? IopBus { get; set; }

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

        /// <summary>
        /// Creates a lightweight snapshot of the current register state suitable for
        /// analysis (Analyze calls).  Only GPR, FPR, HI/LO/SA and the FPU accumulator
        /// are copied; the memory bus, COP0, VU0/VU1 wiring is intentionally left empty
        /// because Analyze() implementations never access those fields.
        /// </summary>
        public CpuState CreateAnalysisSnapshot()
        {
            var snap = new CpuState();
            Array.Copy(RegsLo, snap.RegsLo, 32);
            Array.Copy(RegsHi, snap.RegsHi, 32);
            snap.HI   = HI;
            snap.LO   = LO;
            snap.HI1  = HI1;
            snap.LO1  = LO1;
            snap.SA   = SA;
            snap.FACC = FACC;
            Array.Copy(FPR, snap.FPR, 32);
            return snap;
        }
    }
}
