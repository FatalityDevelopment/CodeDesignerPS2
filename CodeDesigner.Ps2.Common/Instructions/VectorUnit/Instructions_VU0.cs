using CodeDesigner.Ps2.Common.Cpu.InstrArgs;
using CodeDesigner.Ps2.Common.Cpu.Instructions;
using CodeDesigner.Ps2.Common.CPU;
using CodeDesigner.Ps2.Common.VectorUnits.VU0;
using System;
using System.Numerics;

/*
COP2 (VU0) instructions that are not macro-mode arithmetic:

| Instruction | Encoding                         | Description |
|-------------|----------------------------------|-------------|
| LQC2        | opcode 0x36                      | Load quadword into a VF register |
| SQC2        | opcode 0x3E                      | Store quadword from a VF register |
| QMFC2(.I)   | COP2 rs=0x01, bit 0 = interlock  | VF register -> GPR (128-bit) |
| CFC2(.I)    | COP2 rs=0x02                     | VI / control register -> GPR |
| QMTC2(.I)   | COP2 rs=0x05                     | GPR (128-bit) -> VF register |
| CTC2(.I)    | COP2 rs=0x06                     | GPR -> VI / control register |

Macro-mode arithmetic (CO bit set) lives in Vu0Macro.cs; BC2x branches in CopBranch.cs.
*/

namespace CodeDesigner.Ps2.Common.Instructions.VectorUnitInstructions
{
    // Helper class for component masking
    public static class VU0Helpers
    {
        public static byte ParseMask(string mask)
        {
            byte result = 0;
            if (mask.Contains('x')) result |= 0x1;
            if (mask.Contains('y')) result |= 0x2;
            if (mask.Contains('z')) result |= 0x4;
            if (mask.Contains('w')) result |= 0x8;
            return result == 0 ? (byte)0xF : result; // Default to all components
        }

        public static string MaskToString(byte mask)
        {
            if (mask == 0xF) return "";
            string result = ".";
            if ((mask & 0x1) != 0) result += "x";
            if ((mask & 0x2) != 0) result += "y";
            if ((mask & 0x4) != 0) result += "z";
            if ((mask & 0x8) != 0) result += "w";
            return result;
        }

        /// <summary>
        /// Extract a single component from a Vector4.
        /// comp: 0=x, 1=y, 2=z, 3=w
        /// </summary>
        public static float GetComponent(Vector4 v, int comp) => comp switch
        {
            0 => v.X,
            1 => v.Y,
            2 => v.Z,
            _ => v.W,
        };

        /// <summary>
        /// Extract a single component from a Vector4 as raw IEEE-754 bits.
        /// comp: 0=x, 1=y, 2=z, 3=w
        /// </summary>
        public static uint GetComponentBits(Vector4 v, int comp)
            => BitConverter.SingleToUInt32Bits(GetComponent(v, comp));

        /// <summary>
        /// Advance the PS2 VU0 23-bit LFSR by one step and return the new state.
        ///
        /// The R register is stored in PCSX2/PS2 format: 0x3F800000 | mantissa_23_bits
        /// (always represents a float in [1.0, 2.0)).
        ///
        /// Algorithm from PCSX2 VUops.cpp AdvanceLFSR():
        ///   Feedback taps: bit 22 XOR bit 4 of the full 32-bit R register value.
        ///   The mantissa bits are left-shifted by 1 and bit 0 is set to the feedback.
        /// </summary>
        public static uint NextRng(uint state)
        {
            // state = 0x3F800000 | mantissa; taps are at bits 22 and 4 of the full value
            int x = (int)((state >> 4) & 1u);
            int y = (int)((state >> 22) & 1u);
            state <<= 1;
            state ^= (uint)(x ^ y);
            // Restore the IEEE exponent (0x3F800000) so R stays in [1.0, 2.0) float format
            return (state & 0x7FFFFFu) | 0x3F800000u;
        }

        /// <summary>
        /// Name of COP2 control register <paramref name="reg"/> as used by
        /// cfc2/ctc2: vi0-vi15, then Status, MAC, Clip, R, I, Q, TPC, CMSAR0, FBRST,
        /// VPU-STAT, CMSAR1. Unassigned slots print as viN.
        /// </summary>
        public static string ControlRegisterName(int reg) => reg switch
        {
            16 => "Status",
            17 => "MAC",
            18 => "Clip",
            20 => "R",
            21 => "I",
            22 => "Q",
            26 => "TPC",
            27 => "CMSAR0",
            28 => "FBRST",
            29 => "VPU-STAT",
            31 => "CMSAR1",
            _  => $"vi{reg}",
        };

        /// <summary>Parses a control register name or <c>viN</c>; returns false when unknown.</summary>
        public static bool TryParseControlRegister(string text, out int reg)
        {
            string t = text.Trim().TrimStart('$');
            for (int i = 16; i < 32; i++)
            {
                if (string.Equals(ControlRegisterName(i), t, StringComparison.OrdinalIgnoreCase))
                {
                    reg = i;
                    return true;
                }
            }
            if (t.StartsWith("vi", StringComparison.OrdinalIgnoreCase) && int.TryParse(t[2..], out reg) && reg is >= 0 and < 32)
                return true;
            reg = -1;
            return false;
        }

        /// <summary><c>.i</c> when the interlock bit (bit 0) of a COP2 move is set.</summary>
        public static string InterlockSuffix(Args_R args) => (args.Funct & 1) != 0 ? ".i" : string.Empty;

        internal static Vector4 FromQuad(ulong lo, ulong hi) => new(
            BitConverter.UInt32BitsToSingle((uint)lo), BitConverter.UInt32BitsToSingle((uint)(lo >> 32)),
            BitConverter.UInt32BitsToSingle((uint)hi), BitConverter.UInt32BitsToSingle((uint)(hi >> 32)));

        internal static (ulong lo, ulong hi) ToQuad(Vector4 v) => (
            BitConverter.SingleToUInt32Bits(v.X) | ((ulong)BitConverter.SingleToUInt32Bits(v.Y) << 32),
            BitConverter.SingleToUInt32Bits(v.Z) | ((ulong)BitConverter.SingleToUInt32Bits(v.W) << 32));
    }

    // LQC2 - Load Quadword to VF register (opcode 0x36)
    public sealed class VLQ : Instruction<Args_I>
    {
        public VLQ()
        {
            Opcode = 0x36;
            Funct = null;
            Name = "lqc2";
            ArgType = InstrArgType.I;
            ArgNames = ["vft", "offset", "base"];
            Equation = "VF[vft] = Memory128[base + offset]";
            Description = "Load Quadword to COP2 - loads a 128-bit vector from memory into a VF register";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + CpuHelpers.SignExtend16(args.Imm)) & ~0xFu;
            ulong lo, hi;
            if (cpu.Bus != null)
            {
                cpu.Bus.ReadQuadWord(addr, out lo, out hi);
            }
            else if (addr + 15 < (ulong)cpu.Memory.Length)
            {
                lo = BitConverter.ToUInt64(cpu.Memory, (int)addr);
                hi = BitConverter.ToUInt64(cpu.Memory, (int)addr + 8);
            }
            else return;
            cpu.VU0.WriteVF(args.Rt, VU0Helpers.FromQuad(lo, hi), 0xF);
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {VU0State.GetVFRegisterName(args.Rt)}, {CpuHelpers.FormatOffset(offset)}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[2] << 21 | args[0] << 16 | args[1] & 0xFFFF);

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string address = cpu != null
                ? CpuHelpers.FormatMemoryReference(cpu, args.Rs, offset)
                : CpuHelpers.FormatMemoryOffset(offset);
            return $"{VU0State.GetVFRegisterName(args.Rt)} ← [{address}]";
        }
    }

    // SQC2 - Store Quadword from VF register (opcode 0x3E)
    public sealed class VSQ : Instruction<Args_I>
    {
        public VSQ()
        {
            Opcode = 0x3E;
            Funct = null;
            Name = "sqc2";
            ArgType = InstrArgType.I;
            ArgNames = ["vft", "offset", "base"];
            Equation = "Memory128[base + offset] = VF[vft]";
            Description = "Store Quadword from COP2 - stores a VF register as a 128-bit vector";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + CpuHelpers.SignExtend16(args.Imm)) & ~0xFu;
            var (lo, hi) = VU0Helpers.ToQuad(cpu.VU0.ReadVF(args.Rt));
            if (cpu.Bus != null)
            {
                cpu.Bus.WriteQuadWord(addr, lo, hi);
            }
            else if (addr + 15 < (ulong)cpu.Memory.Length)
            {
                BitConverter.TryWriteBytes(cpu.Memory.AsSpan((int)addr, 8), lo);
                BitConverter.TryWriteBytes(cpu.Memory.AsSpan((int)addr + 8, 8), hi);
            }
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {VU0State.GetVFRegisterName(args.Rt)}, {CpuHelpers.FormatOffset(offset)}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[2] << 21 | args[0] << 16 | args[1] & 0xFFFF);

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string address = cpu != null
                ? CpuHelpers.FormatMemoryReference(cpu, args.Rs, offset)
                : CpuHelpers.FormatMemoryOffset(offset);
            return $"[{address}] ← {VU0State.GetVFRegisterName(args.Rt)}";
        }
    }

    // QMFC2 - Quadword Move From COP2 (COP2 rs=0x01): rt = GPR, rd = VF register
    public sealed class QMFC2 : Instruction<Args_R>
    {
        public QMFC2()
        {
            Opcode = 0x12; Funct = null; Name = "qmfc2";
            ArgType = InstrArgType.R; ArgNames = ["rt", "vfs"];
            Equation = "rt = vfs (128-bit)";
            Description = "Quadword Move From COP2 - copies a 128-bit VF register into a GPR";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            if (args.Rt == 0) return;
            var (lo, hi) = VU0Helpers.ToQuad(cpu.VU0.ReadVF(args.Rd));
            cpu.RegsLo[args.Rt] = lo;
            cpu.RegsHi[args.Rt] = hi;
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name}{VU0Helpers.InterlockSuffix(args)} {CpuHelpers.RegName(args.Rt)}, {VU0State.GetVFRegisterName(args.Rd)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x01 << 21 | args[0] << 16 | args[1] << 11);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => $"{CpuHelpers.RegName(args.Rt)} = {VU0State.GetVFRegisterName(args.Rd)}";
    }

    // CFC2 - Control From COP2 (COP2 rs=0x02): rt = GPR, rd = VI / control register
    public sealed class CFC2 : Instruction<Args_R>
    {
        public CFC2()
        {
            Opcode = 0x12; Funct = null; Name = "cfc2";
            ArgType = InstrArgType.R; ArgNames = ["rt", "ctrl"];
            Equation = "rt = ctrl";
            Description = "Control From COP2 - reads a VU0 integer or control register";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            if (args.Rt == 0) return;
            uint val = args.Rd switch
            {
                <= 15 => cpu.VU0.VI[args.Rd],
                16 => cpu.VU0.StatusFlags,
                17 => cpu.VU0.MACFlags,
                18 => cpu.VU0.ClipFlags,
                20 => cpu.VU0.RngState,
                21 => BitConverter.SingleToUInt32Bits(cpu.VU0.I),
                22 => BitConverter.SingleToUInt32Bits(cpu.VU0.Q),
                // VPU-STAT bit 0 = VU0 busy. VU1 is not emulated.
                29 => cpu.VU0.IsRunning ? 1u : 0u,
                _ => 0,
            };
            cpu.RegsLo[args.Rt] = (ulong)(int)val; // sign-extend 32-bit to 64-bit
            cpu.RegsHi[args.Rt] = val > 0x7FFFFFFFu ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name}{VU0Helpers.InterlockSuffix(args)} {CpuHelpers.RegName(args.Rt)}, {VU0Helpers.ControlRegisterName(args.Rd)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x02 << 21 | args[0] << 16 | args[1] << 11);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => $"{CpuHelpers.RegName(args.Rt)} = {VU0Helpers.ControlRegisterName(args.Rd)}";
    }

    // QMTC2 - Quadword Move To COP2 (COP2 rs=0x05): rt = GPR, rd = VF register
    public sealed class QMTC2 : Instruction<Args_R>
    {
        public QMTC2()
        {
            Opcode = 0x12; Funct = null; Name = "qmtc2";
            ArgType = InstrArgType.R; ArgNames = ["rt", "vfd"];
            Equation = "vfd = rt (128-bit)";
            Description = "Quadword Move To COP2 - copies a 128-bit GPR into a VF register";
        }

        public override void Exec(CpuState cpu, Args_R args)
            => cpu.VU0.WriteVF(args.Rd, VU0Helpers.FromQuad(cpu.RegsLo[args.Rt], cpu.RegsHi[args.Rt]));

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name}{VU0Helpers.InterlockSuffix(args)} {CpuHelpers.RegName(args.Rt)}, {VU0State.GetVFRegisterName(args.Rd)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x05 << 21 | args[0] << 16 | args[1] << 11);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => $"{VU0State.GetVFRegisterName(args.Rd)} = {CpuHelpers.RegName(args.Rt)}";
    }

    // CTC2 - Control To COP2 (COP2 rs=0x06): rt = GPR, rd = VI / control register
    public sealed class CTC2 : Instruction<Args_R>
    {
        public CTC2()
        {
            Opcode = 0x12; Funct = null; Name = "ctc2";
            ArgType = InstrArgType.R; ArgNames = ["rt", "ctrl"];
            Equation = "ctrl = rt";
            Description = "Control To COP2 - writes a VU0 integer or control register";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            uint val = (uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
            switch (args.Rd)
            {
                case <= 15: cpu.VU0.WriteVI(args.Rd, (ushort)val); break;
                case 16: cpu.VU0.StatusFlags = val; break;
                case 17: cpu.VU0.MACFlags = (ushort)val; break;
                case 18: cpu.VU0.ClipFlags = val; break;
                case 20: cpu.VU0.RngState = 0x3F800000u | (val & 0x7FFFFFu); break;
                case 21: cpu.VU0.I = BitConverter.UInt32BitsToSingle(val); break;
                case 22: cpu.VU0.Q = BitConverter.UInt32BitsToSingle(val); break;
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name}{VU0Helpers.InterlockSuffix(args)} {CpuHelpers.RegName(args.Rt)}, {VU0Helpers.ControlRegisterName(args.Rd)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x06 << 21 | args[0] << 16 | args[1] << 11);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => $"{VU0Helpers.ControlRegisterName(args.Rd)} = {CpuHelpers.RegName(args.Rt)}";
    }
}
