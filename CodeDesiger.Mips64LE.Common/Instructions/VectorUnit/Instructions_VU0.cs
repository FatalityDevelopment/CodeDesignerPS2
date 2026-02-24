using CodeDesigner.Mips64LE.Common.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.Common.Cpu.Instructions;
using CodeDesigner.Mips64LE.Common.CPU;
using CodeDesigner.Mips64LE.Common.VectorUnits.VU0;
using System;
using System.Numerics;

/*
VU0 COP2 Instructions (Macro Mode)
VU0 operates as COP2 when used in Macro Mode from the EE CPU

Vector Arithmetic Instructions:
| Instruction | Description |
|-------------|-------------|
| VADD    | Vector add |
| VSUB    | Vector subtract |
| VMUL    | Vector multiply |
| VMADD   | Vector multiply-add (using ACC) |
| VMSUB   | Vector multiply-subtract (using ACC) |
| VMAX    | Vector maximum |
| VMIN    | Vector minimum |
| VABS    | Vector absolute value |

Vector Accumulator Instructions:
| VADDA   | Vector add to accumulator |
| VMULA   | Vector multiply to accumulator |

Conversion Instructions:
| VFTOI   | Float to integer conversion |
| VITOF   | Integer to float conversion |

Integer Instructions:
| VIADD   | Vector integer add |
| VISUB   | Vector integer subtract |
| VIADDI  | Vector integer add immediate |

Load/Store Instructions:
| VLQ     | Vector load quadword |
| VSQ     | Vector store quadword |

Note: Component masking (.x, .y, .z, .w, .xyz, etc.) is supported
*/

namespace CodeDesigner.Mips64LE.Common.Instructions.VectorUnitInstructions
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
        /// Advance the PS2 VU0 23-bit LFSR by one step and return the new state.
        /// Feedback taps: bits 22, 17, 11, 6.
        /// </summary>
        public static uint NextRng(uint state)
        {
            uint feedback = ((state >> 22) ^ (state >> 17) ^ (state >> 11) ^ (state >> 6)) & 1u;
            return ((state >> 1) | (feedback << 22)) & 0x7FFFFFu;
        }
    }

    // VADD - Vector Add
    public sealed class VADD : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VADD()
        {
            Opcode = 0x12; // COP2
            Funct = 0x28;
            Name = "vadd";
            ArgType = InstrArgType.R;
            ArgNames = ["vfd", "vfs", "vft"];
            Equation = "vfd = vfs + vft";
            Description = "Vector add - adds two vector registers component-wise";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 vt = cpu.VU0.ReadVF(args.Rt);
            Vector4 result = vs + vt;
            cpu.VU0.WriteVF(args.Rd, result, _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name}{VU0Helpers.MaskToString(_mask)} {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rs)}, {VU0State.GetVFRegisterName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | Funct ?? 0x28);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("vfd", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rs))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rs);
                Vector4 vt = cpu.VU0.ReadVF(args.Rt);
                Vector4 result = vs + vt;
                return $"{eq} => {vs} + {vt} = {result}";
            }
            return eq;
        }
    }

    // VSUB - Vector Subtract
    public sealed class VSUB : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VSUB()
        {
            Opcode = 0x12; // COP2
            Funct = 0x29;
            Name = "vsub";
            ArgType = InstrArgType.R;
            ArgNames = ["vfd", "vfs", "vft"];
            Equation = "vfd = vfs - vft";
            Description = "Vector subtract - subtracts two vector registers component-wise";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 vt = cpu.VU0.ReadVF(args.Rt);
            Vector4 result = vs - vt;
            cpu.VU0.WriteVF(args.Rd, result, _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name}{VU0Helpers.MaskToString(_mask)} {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rs)}, {VU0State.GetVFRegisterName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | Funct ?? 0x29);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("vfd", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rs))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rs);
                Vector4 vt = cpu.VU0.ReadVF(args.Rt);
                Vector4 result = vs - vt;
                return $"{eq} => {vs} - {vt} = {result}";
            }
            return eq;
        }
    }

    // VMUL - Vector Multiply
    public sealed class VMUL : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VMUL()
        {
            Opcode = 0x12; // COP2
            Funct = 0x2A;
            Name = "vmul";
            ArgType = InstrArgType.R;
            ArgNames = ["vfd", "vfs", "vft"];
            Equation = "vfd = vfs * vft";
            Description = "Vector multiply - multiplies two vector registers component-wise";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 vt = cpu.VU0.ReadVF(args.Rt);
            Vector4 result = vs * vt;
            cpu.VU0.WriteVF(args.Rd, result, _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name}{VU0Helpers.MaskToString(_mask)} {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rs)}, {VU0State.GetVFRegisterName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | Funct ?? 0x2A);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("vfd", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rs))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rs);
                Vector4 vt = cpu.VU0.ReadVF(args.Rt);
                Vector4 result = vs * vt;
                return $"{eq} => {vs} * {vt} = {result}";
            }
            return eq;
        }
    }

    // VMADD - Vector Multiply-Add (using ACC)
    public sealed class VMADD : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VMADD()
        {
            Opcode = 0x12; // COP2
            Funct = 0x30;
            Name = "vmadd";
            ArgType = InstrArgType.R;
            ArgNames = ["vfd", "vfs", "vft"];
            Equation = "vfd = ACC + (vfs * vft)";
            Description = "Vector multiply-add - multiplies vectors and adds accumulator";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 vt = cpu.VU0.ReadVF(args.Rt);
            Vector4 acc = cpu.VU0.ReadACC();
            Vector4 result = acc + vs * vt;
            cpu.VU0.WriteVF(args.Rd, result, _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name}{VU0Helpers.MaskToString(_mask)} {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rs)}, {VU0State.GetVFRegisterName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x30);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("vfd", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rs))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rs);
                Vector4 vt = cpu.VU0.ReadVF(args.Rt);
                Vector4 acc = cpu.VU0.ReadACC();
                Vector4 result = acc + vs * vt;
                return $"{eq} => ACC({acc}) + ({vs} * {vt}) = {result}";
            }
            return eq;
        }
    }

    // VMSUB - Vector Multiply-Subtract (using ACC)
    public sealed class VMSUB : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VMSUB()
        {
            Opcode = 0x12; // COP2
            Funct = 0x2D;
            Name = "vmsub";
            ArgType = InstrArgType.R;
            ArgNames = ["vfd", "vfs", "vft"];
            Equation = "vfd = ACC - (vfs * vft)";
            Description = "Vector multiply-subtract - multiplies vectors and subtracts from accumulator";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 vt = cpu.VU0.ReadVF(args.Rt);
            Vector4 acc = cpu.VU0.ReadACC();
            Vector4 result = acc - vs * vt;
            cpu.VU0.WriteVF(args.Rd, result, _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name}{VU0Helpers.MaskToString(_mask)} {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rs)}, {VU0State.GetVFRegisterName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | Funct ?? 0x2D);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("vfd", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rs))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rs);
                Vector4 vt = cpu.VU0.ReadVF(args.Rt);
                Vector4 acc = cpu.VU0.ReadACC();
                Vector4 result = acc - vs * vt;
                return $"{eq} => ACC({acc}) - ({vs} * {vt}) = {result}";
            }
            return eq;
        }
    }

    // VMAX - Vector Maximum
    public sealed class VMAX : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VMAX()
        {
            Opcode = 0x12; // COP2
            Funct = 0x2B;
            Name = "vmax";
            ArgType = InstrArgType.R;
            ArgNames = ["vfd", "vfs", "vft"];
            Equation = "vfd = max(vfs, vft)";
            Description = "Vector maximum - selects maximum component-wise";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 vt = cpu.VU0.ReadVF(args.Rt);
            Vector4 result = Vector4.Max(vs, vt);
            cpu.VU0.WriteVF(args.Rd, result, _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name}{VU0Helpers.MaskToString(_mask)} {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rs)}, {VU0State.GetVFRegisterName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | Funct ?? 0x2B);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("vfd", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rs))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rs);
                Vector4 vt = cpu.VU0.ReadVF(args.Rt);
                Vector4 result = Vector4.Max(vs, vt);
                return $"{eq} => max({vs}, {vt}) = {result}";
            }
            return eq;
        }
    }

    // VMIN - Vector Minimum
    public sealed class VMIN : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VMIN()
        {
            Opcode = 0x12; // COP2
            Funct = 0x2C;
            Name = "vmin";
            ArgType = InstrArgType.R;
            ArgNames = ["vfd", "vfs", "vft"];
            Equation = "vfd = min(vfs, vft)";
            Description = "Vector minimum - selects minimum component-wise";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 vt = cpu.VU0.ReadVF(args.Rt);
            Vector4 result = Vector4.Min(vs, vt);
            cpu.VU0.WriteVF(args.Rd, result, _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name}{VU0Helpers.MaskToString(_mask)} {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rs)}, {VU0State.GetVFRegisterName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | Funct ?? 0x2C);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("vfd", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rs))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rs);
                Vector4 vt = cpu.VU0.ReadVF(args.Rt);
                Vector4 result = Vector4.Min(vs, vt);
                return $"{eq} => min({vs}, {vt}) = {result}";
            }
            return eq;
        }
    }

    // VABS - Vector Absolute Value
    public sealed class VABS : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VABS()
        {
            Opcode = 0x12; // COP2
            Funct = 0x2F;
            Name = "vabs";
            ArgType = InstrArgType.R;
            ArgNames = ["vfd", "vfs"];
            Equation = "vfd = abs(vfs)";
            Description = "Vector absolute value - computes absolute value component-wise";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 result = Vector4.Abs(vs);
            cpu.VU0.WriteVF(args.Rd, result, _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name}{VU0Helpers.MaskToString(_mask)} {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rs)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[1] << 21 | args[0] << 11 | Funct ?? 0x2F);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("vfd", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rs));
            
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rs);
                Vector4 result = Vector4.Abs(vs);
                return $"{eq} => abs({vs}) = {result}";
            }
            return eq;
        }
    }

    // VADDA - Vector Add to Accumulator
    public sealed class VADDA : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VADDA()
        {
            Opcode = 0x12; // COP2
            Funct = 0x2E;
            Name = "vadda";
            ArgType = InstrArgType.R;
            ArgNames = ["vfs", "vft"];
            Equation = "ACC = vfs + vft";
            Description = "Vector add to accumulator - stores result in ACC";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 vt = cpu.VU0.ReadVF(args.Rt);
            Vector4 result = vs + vt;
            cpu.VU0.WriteACC(result, _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name}{VU0Helpers.MaskToString(_mask)} ACC, {VU0State.GetVFRegisterName(args.Rs)}, {VU0State.GetVFRegisterName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | Funct ?? 0x2E);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rs))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rs);
                Vector4 vt = cpu.VU0.ReadVF(args.Rt);
                Vector4 result = vs + vt;
                return $"{eq} => {vs} + {vt} = {result}";
            }
            return eq;
        }
    }

    // VMULA - Vector Multiply to Accumulator
    public sealed class VMULA : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VMULA()
        {
            Opcode = 0x12; // COP2
            Funct = 0x31;
            Name = "vmula";
            ArgType = InstrArgType.R;
            ArgNames = ["vfs", "vft"];
            Equation = "ACC = vfs * vft";
            Description = "Vector multiply to accumulator - stores result in ACC";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 vt = cpu.VU0.ReadVF(args.Rt);
            Vector4 result = vs * vt;
            cpu.VU0.WriteACC(result, _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name}{VU0Helpers.MaskToString(_mask)} ACC, {VU0State.GetVFRegisterName(args.Rs)}, {VU0State.GetVFRegisterName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | Funct ?? 0x31);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rs))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rs);
                Vector4 vt = cpu.VU0.ReadVF(args.Rt);
                Vector4 result = vs * vt;
                return $"{eq} => {vs} * {vt} = {result}";
            }
            return eq;
        }
    }

    // VFTOI - Float to Integer Conversion
    public sealed class VFTOI0 : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VFTOI0()
        {
            Opcode = 0x12; // COP2
            Funct = 0x3C;
            Name = "vftoi0";
            ArgType = InstrArgType.R;
            ArgNames = ["vfd", "vfs"];
            Equation = "vfd = (int)vfs";
            Description = "Float to integer conversion - converts each component to integer";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 result = new Vector4(
                (int)vs.X,
                (int)vs.Y,
                (int)vs.Z,
                (int)vs.W
            );
            cpu.VU0.WriteVF(args.Rd, result, _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name}{VU0Helpers.MaskToString(_mask)} {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rs)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[1] << 21 | args[0] << 11 | Funct ?? 0x3C);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("vfd", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rs));
            
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rs);
                return $"{eq} => convert {vs} to integers";
            }
            return eq;
        }
    }

    // VITOF - Integer to Float Conversion
    public sealed class VITOF0 : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VITOF0()
        {
            Opcode = 0x12; // COP2
            Funct = 0x3D;
            Name = "vitof0";
            ArgType = InstrArgType.R;
            ArgNames = ["vfd", "vfs"];
            Equation = "vfd = (float)vfs";
            Description = "Integer to float conversion - converts each component to float";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            // Reinterpret the float bits as integers and convert to float
            int ix = BitConverter.SingleToInt32Bits(vs.X);
            int iy = BitConverter.SingleToInt32Bits(vs.Y);
            int iz = BitConverter.SingleToInt32Bits(vs.Z);
            int iw = BitConverter.SingleToInt32Bits(vs.W);
            
            Vector4 result = new Vector4(
                ix,
                iy,
                iz,
                iw
            );
            cpu.VU0.WriteVF(args.Rd, result, _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name}{VU0Helpers.MaskToString(_mask)} {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rs)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[1] << 21 | args[0] << 11 | Funct ?? 0x3D);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("vfd", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rs));
            
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rs);
                return $"{eq} => convert {vs} to floats";
            }
            return eq;
        }
    }

    // VSUBA - Vector Subtract to Accumulator
    public sealed class VSUBA : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VSUBA()
        {
            Opcode = 0x12; // COP2
            Funct = 0x32;
            Name = "vsuba";
            ArgType = InstrArgType.R;
            ArgNames = ["vfs", "vft"];
            Equation = "ACC = vfs - vft";
            Description = "Vector subtract to accumulator - stores vfs - vft in ACC";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 vt = cpu.VU0.ReadVF(args.Rt);
            cpu.VU0.WriteACC(vs - vt, _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name}{VU0Helpers.MaskToString(_mask)} ACC, {VU0State.GetVFRegisterName(args.Rs)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | Funct ?? 0x32);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfs", VU0State.GetVFRegisterName(args.Rs)).Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            if (cpu != null) { Vector4 vs = cpu.VU0.ReadVF(args.Rs); Vector4 vt = cpu.VU0.ReadVF(args.Rt); return $"{eq} => {vs} - {vt} = {vs - vt}"; }
            return eq;
        }
    }

    // VOPMULA - Vector Outer Product Multiply to Accumulator
    // ACC.xyz = vfs.yzx * vft.zxy
    public sealed class VOPMULA : Instruction<Args_R>
    {
        public VOPMULA()
        {
            Opcode = 0x12; // COP2
            Funct = 0x33;
            Name = "vopmula";
            ArgType = InstrArgType.R;
            ArgNames = ["vfs", "vft"];
            Equation = "ACC.xyz = vfs.yzx * vft.zxy";
            Description = "Vector outer product multiply to accumulator";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 vt = cpu.VU0.ReadVF(args.Rt);
            Vector4 result = new Vector4(vs.Y * vt.Z, vs.Z * vt.X, vs.X * vt.Y, cpu.VU0.ReadACC().W);
            cpu.VU0.WriteACC(result, 0x7); // mask xyz only
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name}.xyz ACC, {VU0State.GetVFRegisterName(args.Rs)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | Funct ?? 0x33);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfs", VU0State.GetVFRegisterName(args.Rs)).Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            if (cpu != null) { Vector4 vs = cpu.VU0.ReadVF(args.Rs); Vector4 vt = cpu.VU0.ReadVF(args.Rt); return $"{eq} => ({vs.Y * vt.Z}, {vs.Z * vt.X}, {vs.X * vt.Y})"; }
            return eq;
        }
    }

    // VOPMSUB - Vector Outer Product Multiply-Subtract
    // vfd.xyz = ACC.xyz - vfs.yzx * vft.zxy
    public sealed class VOPMSUB : Instruction<Args_R>
    {
        public VOPMSUB()
        {
            Opcode = 0x12; // COP2
            Funct = 0x34;
            Name = "vopmsub";
            ArgType = InstrArgType.R;
            ArgNames = ["vfd", "vfs", "vft"];
            Equation = "vfd.xyz = ACC.xyz - vfs.yzx * vft.zxy";
            Description = "Vector outer product multiply-subtract";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 vt = cpu.VU0.ReadVF(args.Rt);
            Vector4 acc = cpu.VU0.ReadACC();
            Vector4 result = new Vector4(acc.X - vs.Y * vt.Z, acc.Y - vs.Z * vt.X, acc.Z - vs.X * vt.Y, 0);
            cpu.VU0.WriteVF(args.Rd, result, 0x7); // mask xyz only
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name}.xyz {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rs)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | Funct ?? 0x34);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfd", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rs)).Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            return eq;
        }
    }

    // VDIV - Vector Divide (result stored in Q register)
    public sealed class VDIV : Instruction<Args_R>
    {
        public VDIV()
        {
            Opcode = 0x12; // COP2
            Funct = 0x35;
            Name = "vdiv";
            ArgType = InstrArgType.R;
            ArgNames = ["Q", "vfs", "vft"];
            Equation = "Q = vfs[comp] / vft[comp]";
            Description = "Vector divide - divides one VF component by another, result in Q";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // PS2 VU0: on divide by zero, result is float.MaxValue (max magnitude positive)
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 vt = cpu.VU0.ReadVF(args.Rt);
            cpu.VU0.Q = vt.X != 0 ? vs.X / vt.X : float.MaxValue;
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} Q, {VU0State.GetVFRegisterName(args.Rs)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[1] << 21 | args[2] << 16 | Funct ?? 0x35);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfs", VU0State.GetVFRegisterName(args.Rs)).Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            if (cpu != null) { Vector4 vs = cpu.VU0.ReadVF(args.Rs); Vector4 vt = cpu.VU0.ReadVF(args.Rt); return $"{eq} => Q = {vs.X} / {vt.X} = {(vt.X != 0 ? vs.X / vt.X : float.MaxValue)}"; }
            return eq;
        }
    }

    // VSQRT - Vector Square Root (result stored in Q register)
    public sealed class VSQRT : Instruction<Args_R>
    {
        public VSQRT()
        {
            Opcode = 0x12; // COP2
            Funct = 0x36;
            Name = "vsqrt";
            ArgType = InstrArgType.R;
            ArgNames = ["Q", "vft"];
            Equation = "Q = sqrt(|vft[comp]|)";
            Description = "Vector square root - computes sqrt of VF component, result in Q";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vt = cpu.VU0.ReadVF(args.Rt);
            cpu.VU0.Q = MathF.Sqrt(MathF.Abs(vt.X));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} Q, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[1] << 16 | Funct ?? 0x36);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            if (cpu != null) { float v = cpu.VU0.ReadVF(args.Rt).X; return $"{eq} => Q = sqrt({MathF.Abs(v)}) = {MathF.Sqrt(MathF.Abs(v))}"; }
            return eq;
        }
    }

    // VRSQRT - Vector Reciprocal Square Root (result stored in Q register)
    public sealed class VRSQRT : Instruction<Args_R>
    {
        public VRSQRT()
        {
            Opcode = 0x12; // COP2
            Funct = 0x37;
            Name = "vrsqrt";
            ArgType = InstrArgType.R;
            ArgNames = ["Q", "vfs", "vft"];
            Equation = "Q = vfs[comp] / sqrt(|vft[comp]|)";
            Description = "Vector reciprocal square root - result in Q";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 vt = cpu.VU0.ReadVF(args.Rt);
            float sqr = MathF.Sqrt(MathF.Abs(vt.X));
            cpu.VU0.Q = sqr != 0 ? vs.X / sqr : float.MaxValue;
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} Q, {VU0State.GetVFRegisterName(args.Rs)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[1] << 21 | args[2] << 16 | Funct ?? 0x37);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfs", VU0State.GetVFRegisterName(args.Rs)).Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            return eq;
        }
    }

    // VMOVE - Vector Move register
    public sealed class VMOVE : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VMOVE()
        {
            Opcode = 0x12; // COP2
            Funct = 0x38;
            Name = "vmove";
            ArgType = InstrArgType.R;
            ArgNames = ["vfd", "vfs"];
            Equation = "vfd = vfs";
            Description = "Vector move - copies VF register";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            cpu.VU0.WriteVF(args.Rd, vs, _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name}{VU0Helpers.MaskToString(_mask)} {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rs)}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[1] << 21 | args[0] << 11 | Funct ?? 0x38);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfd", VU0State.GetVFRegisterName(args.Rd)).Replace("vfs", VU0State.GetVFRegisterName(args.Rs));
            if (cpu != null) { Vector4 vs = cpu.VU0.ReadVF(args.Rs); return $"{eq} => {vs}"; }
            return eq;
        }
    }

    // VMR32 - Vector Move and Rotate by 32 (rotate xyzw to wxyz)
    public sealed class VMR32 : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VMR32()
        {
            Opcode = 0x12; // COP2
            Funct = 0x39;
            Name = "vmr32";
            ArgType = InstrArgType.R;
            ArgNames = ["vfd", "vfs"];
            Equation = "vfd.xyzw = vfs.wxyz";
            Description = "Vector move and rotate by 32 bits: rotates XYZW to WXYZ";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            // Rotate: new xyzw = old wxyz
            cpu.VU0.WriteVF(args.Rd, new Vector4(vs.W, vs.X, vs.Y, vs.Z), _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name}{VU0Helpers.MaskToString(_mask)} {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rs)}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[1] << 21 | args[0] << 11 | Funct ?? 0x39);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfd", VU0State.GetVFRegisterName(args.Rd)).Replace("vfs", VU0State.GetVFRegisterName(args.Rs));
            if (cpu != null) { Vector4 vs = cpu.VU0.ReadVF(args.Rs); return $"{eq} => ({vs.W}, {vs.X}, {vs.Y}, {vs.Z})"; }
            return eq;
        }
    }

    // VNOP - Vector No Operation
    public sealed class VNOP : Instruction<Args_R>
    {
        public VNOP()
        {
            Opcode = 0x12; // COP2
            Funct = 0x3A;
            Name = "vnop";
            ArgType = InstrArgType.R;
            ArgNames = [];
            Equation = "";
            Description = "Vector no operation";
        }

        public override void Exec(CpuState cpu, Args_R args) { }
        public override string Disasm(Args_R args, uint? pc = null) => Name;
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | Funct ?? 0x3A);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null) => "Vector no operation";
    }

    // VCLIPW - Vector Clip W
    // Tests |vfs.xyz| < vft.w for each component; updates ClipFlags
    public sealed class VCLIPW : Instruction<Args_R>
    {
        public VCLIPW()
        {
            Opcode = 0x12; // COP2
            Funct = 0x3B;
            Name = "vclipw";
            ArgType = InstrArgType.R;
            ArgNames = ["vfs", "vft"];
            Equation = "test |vfs.xyz| < |vft.w|; update ClipFlags";
            Description = "Vector clip W: tests each XYZ component against W component, updates clip flags";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            float w = MathF.Abs(cpu.VU0.ReadVF(args.Rt).W);
            // Update clip flags: bits [0]=+x, [1]=-x, [2]=+y, [3]=-y, [4]=+z, [5]=-z
            uint flags = 0;
            if (vs.X >  w) flags |= 0x01;
            if (vs.X < -w) flags |= 0x02;
            if (vs.Y >  w) flags |= 0x04;
            if (vs.Y < -w) flags |= 0x08;
            if (vs.Z >  w) flags |= 0x10;
            if (vs.Z < -w) flags |= 0x20;
            // Shift existing flags left by 6 and insert new flags
            cpu.VU0.ClipFlags = ((cpu.VU0.ClipFlags << 6) | flags) & 0x00FFFFFF;
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {VU0State.GetVFRegisterName(args.Rs)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | Funct ?? 0x3B);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfs", VU0State.GetVFRegisterName(args.Rs)).Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rs);
                float w = MathF.Abs(cpu.VU0.ReadVF(args.Rt).W);
                return $"{eq} => w={w}, xyz=({vs.X},{vs.Y},{vs.Z})";
            }
            return eq;
        }
    }

    // VLQ - Load Quadword to VF register (LQC2, opcode=0x36)
    public sealed class VLQ : Instruction<Args_I>
    {
        public VLQ()
        {
            Opcode = 0x36; // LQC2
            Funct = null;
            Name = "vlq";
            ArgType = InstrArgType.I;
            ArgNames = ["vft", "offset", "base"];
            Equation = "VF[vft] = Memory[base + offset]";
            Description = "Load Quadword to VF register (LQC2) - loads 128-bit vector from memory";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            ulong addr = (ulong)((long)cpu.RegsLo[args.Rs] + offset);
            if (addr + 15 < (ulong)cpu.Memory.Length)
            {
                float x = BitConverter.ToSingle(cpu.Memory, (int)addr);
                float y = BitConverter.ToSingle(cpu.Memory, (int)addr + 4);
                float z = BitConverter.ToSingle(cpu.Memory, (int)addr + 8);
                float w = BitConverter.ToSingle(cpu.Memory, (int)addr + 12);
                cpu.VU0.WriteVF(args.Rt, new System.Numerics.Vector4(x, y, z, w), 0xF);
            }
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {VU0State.GetVFRegisterName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[2] << 21 | args[0] << 16 | args[1] & 0xFFFF);

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation.Replace("vft", VU0State.GetVFRegisterName(args.Rt))
                .Replace("base", CpuHelpers.RegName(args.Rs)).Replace("offset", offset.ToString());
            if (cpu != null)
            {
                ulong addr = (ulong)((long)cpu.RegsLo[args.Rs] + offset);
                return $"{eq} => addr = 0x{addr:X16}";
            }
            return eq;
        }
    }

    // VSQ - Store Quadword from VF register (SQC2, opcode=0x3E)
    public sealed class VSQ : Instruction<Args_I>
    {
        public VSQ()
        {
            Opcode = 0x3E; // SQC2
            Funct = null;
            Name = "vsq";
            ArgType = InstrArgType.I;
            ArgNames = ["vft", "offset", "base"];
            Equation = "Memory[base + offset] = VF[vft]";
            Description = "Store Quadword from VF register (SQC2) - stores 128-bit vector to memory";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            ulong addr = (ulong)((long)cpu.RegsLo[args.Rs] + offset);
            if (addr + 15 < (ulong)cpu.Memory.Length)
            {
                System.Numerics.Vector4 v = cpu.VU0.ReadVF(args.Rt);
                byte[] buf = new byte[16];
                Buffer.BlockCopy(BitConverter.GetBytes(v.X), 0, buf, 0,  4);
                Buffer.BlockCopy(BitConverter.GetBytes(v.Y), 0, buf, 4,  4);
                Buffer.BlockCopy(BitConverter.GetBytes(v.Z), 0, buf, 8,  4);
                Buffer.BlockCopy(BitConverter.GetBytes(v.W), 0, buf, 12, 4);
                Buffer.BlockCopy(buf, 0, cpu.Memory, (int)addr, 16);
            }
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {VU0State.GetVFRegisterName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[2] << 21 | args[0] << 16 | args[1] & 0xFFFF);

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation.Replace("vft", VU0State.GetVFRegisterName(args.Rt))
                .Replace("base", CpuHelpers.RegName(args.Rs)).Replace("offset", offset.ToString());
            if (cpu != null)
            {
                ulong addr = (ulong)((long)cpu.RegsLo[args.Rs] + offset);
                System.Numerics.Vector4 v = cpu.VU0.ReadVF(args.Rt);
                return $"{eq} => addr=0x{addr:X16}, value={v}";
            }
            return eq;
        }
    }

    // QMFC2 - Quadword Move From COP2 (COP2 rs=0x01, opcode=0x12)
    public sealed class QMFC2 : Instruction<Args_R>
    {
        public QMFC2()
        {
            Opcode = 0x12; Funct = 0x00; Name = "qmfc2";
            ArgType = InstrArgType.R; ArgNames = ["rt", "vfs"];
            Equation = "GPR[rt] = VF[vfs] (128-bit move to two 64-bit GPRs)";
            Description = "Quadword Move From COP2 - moves 128-bit VF register to GPR pair";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 v = cpu.VU0.ReadVF(args.Rt);
            cpu.RegsLo[args.Rd] = (ulong)BitConverter.SingleToUInt32Bits(v.X) | ((ulong)BitConverter.SingleToUInt32Bits(v.Y) << 32);
            cpu.RegsHi[args.Rd] = (ulong)BitConverter.SingleToUInt32Bits(v.Z) | ((ulong)BitConverter.SingleToUInt32Bits(v.W) << 32);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x01 << 21 | args[1] << 16 | args[0] << 11);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rt", CpuHelpers.RegName(args.Rd)).Replace("vfs", VU0State.GetVFRegisterName(args.Rt));
            if (cpu != null) { Vector4 v = cpu.VU0.ReadVF(args.Rt); return $"{eq} => {v}"; }
            return eq;
        }
    }

    // CFC2 - Control From COP2 (COP2 rs=0x02, opcode=0x12)
    public sealed class CFC2 : Instruction<Args_R>
    {
        public CFC2()
        {
            Opcode = 0x12; Funct = 0x00; Name = "cfc2";
            ArgType = InstrArgType.R; ArgNames = ["rt", "ctrl"];
            Equation = "GPR[rt] = VU0_ControlReg[ctrl]";
            Description = "Control From COP2 - reads VU0 control register";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Read control register (simplified: return status flags for known regs)
            uint val = args.Rt switch {
                16 => cpu.VU0.StatusFlags,
                17 => cpu.VU0.MACFlags,
                18 => cpu.VU0.ClipFlags,
                _ => 0
            };
            cpu.RegsLo[args.Rd] = (ulong)(int)val; // sign-extend 32-bit
            cpu.RegsHi[args.Rd] = val > 0x7FFFFFFF ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, vi{args.Rt}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x02 << 21 | args[1] << 16 | args[0] << 11);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rt", CpuHelpers.RegName(args.Rd)).Replace("ctrl", $"vi{args.Rt}");
    }

    // QMTC2 - Quadword Move To COP2 (COP2 rs=0x05, opcode=0x12)
    public sealed class QMTC2 : Instruction<Args_R>
    {
        public QMTC2()
        {
            Opcode = 0x12; Funct = 0x00; Name = "qmtc2";
            ArgType = InstrArgType.R; ArgNames = ["vfd", "rt"];
            Equation = "VF[vfd] = GPR[rt] (128-bit move from two 64-bit GPRs)";
            Description = "Quadword Move To COP2 - moves GPR pair to 128-bit VF register";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            float x = BitConverter.UInt32BitsToSingle((uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF));
            float y = BitConverter.UInt32BitsToSingle((uint)(cpu.RegsLo[args.Rt] >> 32));
            float z = BitConverter.UInt32BitsToSingle((uint)(cpu.RegsHi[args.Rt] & 0xFFFFFFFF));
            float w = BitConverter.UInt32BitsToSingle((uint)(cpu.RegsHi[args.Rt] >> 32));
            cpu.VU0.WriteVF(args.Rd, new Vector4(x, y, z, w));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {VU0State.GetVFRegisterName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x05 << 21 | args[1] << 16 | args[0] << 11);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("vfd", VU0State.GetVFRegisterName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // CTC2 - Control To COP2 (COP2 rs=0x06, opcode=0x12)
    public sealed class CTC2 : Instruction<Args_R>
    {
        public CTC2()
        {
            Opcode = 0x12; Funct = 0x00; Name = "ctc2";
            ArgType = InstrArgType.R; ArgNames = ["ctrl", "rt"];
            Equation = "VU0_ControlReg[ctrl] = GPR[rt]";
            Description = "Control To COP2 - writes VU0 control register";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            uint val = (uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
            switch (args.Rd)
            {
                case 16: cpu.VU0.StatusFlags = val; break;
                case 17: cpu.VU0.MACFlags = (ushort)val; break;
                case 18: cpu.VU0.ClipFlags = val; break;
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} vi{args.Rd}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x06 << 21 | args[1] << 16 | args[0] << 11);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("ctrl", $"vi{args.Rd}").Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // VFTOI4 - VU Float to Int with factor 2^4 (COP2 rs=0x10, funct=0x3E - overrides VFTOI0 at 0x3C? No, different funct)
    // Actually this is funct=0x3E (VFTOI4 is at 0x3E in the COP2 table per EE spec)
    public sealed class VFTOI4 : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VFTOI4()
        {
            Opcode = 0x12; Funct = 0x3E; Name = "vftoi4";
            ArgType = InstrArgType.R; ArgNames = ["vfd", "vfs"];
            Equation = "vfd = (int)(vfs * 16.0)";
            Description = "VU Float To Integer with factor 2^4 - converts float to fixed-point integer";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            Vector4 result = new Vector4(
                (int)(vs.X * 16.0f),
                (int)(vs.Y * 16.0f),
                (int)(vs.Z * 16.0f),
                (int)(vs.W * 16.0f)
            );
            cpu.VU0.WriteVF(args.Rd, result, _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name}{VU0Helpers.MaskToString(_mask)} {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rs)}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[1] << 21 | args[0] << 11 | Funct ?? 0x3E);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfd", VU0State.GetVFRegisterName(args.Rd)).Replace("vfs", VU0State.GetVFRegisterName(args.Rs));
            if (cpu != null) { Vector4 vs = cpu.VU0.ReadVF(args.Rs); return $"{eq} => {vs} * 16"; }
            return eq;
        }
    }

    // VITOF4 - VU Int to Float with factor 2^-4 (COP2 rs=0x10, funct=0x3F)
    public sealed class VITOF4 : Instruction<Args_R>
    {
        private byte _mask = 0xF;

        public VITOF4()
        {
            Opcode = 0x12; Funct = 0x3F; Name = "vitof4";
            ArgType = InstrArgType.R; ArgNames = ["vfd", "vfs"];
            Equation = "vfd = (float)vfs / 16.0";
            Description = "VU Integer To Float with factor 2^-4 - converts fixed-point integer to float";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rs);
            int ix = BitConverter.SingleToInt32Bits(vs.X);
            int iy = BitConverter.SingleToInt32Bits(vs.Y);
            int iz = BitConverter.SingleToInt32Bits(vs.Z);
            int iw = BitConverter.SingleToInt32Bits(vs.W);
            Vector4 result = new Vector4(ix / 16.0f, iy / 16.0f, iz / 16.0f, iw / 16.0f);
            cpu.VU0.WriteVF(args.Rd, result, _mask);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name}{VU0Helpers.MaskToString(_mask)} {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rs)}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[1] << 21 | args[0] << 11 | Funct ?? 0x3F);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfd", VU0State.GetVFRegisterName(args.Rd)).Replace("vfs", VU0State.GetVFRegisterName(args.Rs));
            if (cpu != null) { Vector4 vs = cpu.VU0.ReadVF(args.Rs); return $"{eq} => {vs} / 16"; }
            return eq;
        }
    }


    // =====================================================================
    // VU0 Component-Broadcast Variants (COP2 SPECIAL1, functs 0x00-0x27)
    //
    // Instruction field mapping for COP2 SPECIAL1 (opcode=0x12, rs=0x10):
    //   args.Rs    = 0x10  (format type indicator, not a register)
    //   args.Rt    = vft   (bits[20:16] - broadcast source register)
    //   args.Rd    = vfs   (bits[15:11] - first source register)
    //   args.Shamt = vfd   (bits[10:6]  - destination register)
    //
    // Each operation broadcasts one scalar component of vft before operating:
    //   vaddx: vfd = vfs + {vft.x, vft.x, vft.x, vft.x}
    //   vaddy: vfd = vfs + {vft.y, vft.y, vft.y, vft.y}   ... etc.
    //
    // Encoding: funct bits [1:0] = bc component (00=x, 01=y, 10=z, 11=w)
    //           funct bits [5:2] = operation (0=VADD .. 9=VSUBA)
    //
    // Operation table:
    //   op 0 (0x00-0x03): VADDx/y/z/w   -> vfd = vfs + bc(vft)
    //   op 1 (0x04-0x07): VSUBx/y/z/w   -> vfd = vfs - bc(vft)
    //   op 2 (0x08-0x0B): VMULx/y/z/w   -> vfd = vfs * bc(vft)
    //   op 3 (0x0C-0x0F): VMADDx/y/z/w  -> vfd = ACC + vfs * bc(vft)
    //   op 4 (0x10-0x13): VMSUBx/y/z/w  -> vfd = ACC - vfs * bc(vft)
    //   op 5 (0x14-0x17): VMAXx/y/z/w   -> vfd = max(vfs, bc(vft))
    //   op 6 (0x18-0x1B): VMINx/y/z/w   -> vfd = min(vfs, bc(vft))
    //   op 7 (0x1C-0x1F): VMULAx/y/z/w  -> ACC = vfs * bc(vft)
    //   op 8 (0x20-0x23): VADDAx/y/z/w  -> ACC = vfs + bc(vft)
    //   op 9 (0x24-0x27): VSUBAx/y/z/w  -> ACC = vfs - bc(vft)
    // =====================================================================

    // VADDx/y/z/w - Vector Add with Broadcast Component (functs 0x00-0x03)
    public sealed class VADDBc : Instruction<Args_R>
    {
        private static readonly char[] _compChar = ['x', 'y', 'z', 'w'];
        private readonly int _bc;

        public VADDBc(int bc)
        {
            _bc = bc;
            Opcode = 0x12; Funct = bc; // 0x00-0x03
            Name = "vadd" + _compChar[bc];
            ArgType = InstrArgType.R; ArgNames = ["vfd", "vfs", "vft"];
            Equation = $"vfd = vfs + vft.{_compChar[bc]}";
            Description = $"Vector add broadcast {_compChar[bc]}: vfs + broadcast(vft.{_compChar[bc]})";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // args.Rd = vfs source; args.Rt = vft source; args.Shamt = vfd destination
            Vector4 vs = cpu.VU0.ReadVF(args.Rd);
            float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
            cpu.VU0.WriteVF(args.Shamt, vs + new Vector4(c), 0xF);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {VU0State.GetVFRegisterName(args.Shamt)}, {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            // args[0]=vfd, args[1]=vfs, args[2]=vft
            => (uint)(0x12 << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | _bc);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfd", VU0State.GetVFRegisterName(args.Shamt))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rd);
                float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
                return $"{eq} => {vs} + {c}";
            }
            return eq;
        }
    }

    // VSUBx/y/z/w - Vector Subtract with Broadcast Component (functs 0x04-0x07)
    public sealed class VSUBBc : Instruction<Args_R>
    {
        private static readonly char[] _compChar = ['x', 'y', 'z', 'w'];
        private readonly int _bc;

        public VSUBBc(int bc)
        {
            _bc = bc;
            Opcode = 0x12; Funct = 0x04 + bc; // 0x04-0x07
            Name = "vsub" + _compChar[bc];
            ArgType = InstrArgType.R; ArgNames = ["vfd", "vfs", "vft"];
            Equation = $"vfd = vfs - vft.{_compChar[bc]}";
            Description = $"Vector subtract broadcast {_compChar[bc]}: vfs - broadcast(vft.{_compChar[bc]})";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rd);
            float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
            cpu.VU0.WriteVF(args.Shamt, vs - new Vector4(c), 0xF);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {VU0State.GetVFRegisterName(args.Shamt)}, {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | (0x04 + _bc));

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfd", VU0State.GetVFRegisterName(args.Shamt))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rd);
                float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
                return $"{eq} => {vs} - {c}";
            }
            return eq;
        }
    }

    // VMULx/y/z/w - Vector Multiply with Broadcast Component (functs 0x08-0x0B)
    public sealed class VMULBc : Instruction<Args_R>
    {
        private static readonly char[] _compChar = ['x', 'y', 'z', 'w'];
        private readonly int _bc;

        public VMULBc(int bc)
        {
            _bc = bc;
            Opcode = 0x12; Funct = 0x08 + bc; // 0x08-0x0B
            Name = "vmul" + _compChar[bc];
            ArgType = InstrArgType.R; ArgNames = ["vfd", "vfs", "vft"];
            Equation = $"vfd = vfs * vft.{_compChar[bc]}";
            Description = $"Vector multiply broadcast {_compChar[bc]}: vfs * broadcast(vft.{_compChar[bc]})";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rd);
            float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
            cpu.VU0.WriteVF(args.Shamt, vs * new Vector4(c), 0xF);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {VU0State.GetVFRegisterName(args.Shamt)}, {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | (0x08 + _bc));

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfd", VU0State.GetVFRegisterName(args.Shamt))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rd);
                float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
                return $"{eq} => {vs} * {c}";
            }
            return eq;
        }
    }

    // VMADDx/y/z/w - Vector Multiply-Add with Broadcast Component (functs 0x0C-0x0F)
    public sealed class VMADDBc : Instruction<Args_R>
    {
        private static readonly char[] _compChar = ['x', 'y', 'z', 'w'];
        private readonly int _bc;

        public VMADDBc(int bc)
        {
            _bc = bc;
            Opcode = 0x12; Funct = 0x0C + bc; // 0x0C-0x0F
            Name = "vmadd" + _compChar[bc];
            ArgType = InstrArgType.R; ArgNames = ["vfd", "vfs", "vft"];
            Equation = $"vfd = ACC + vfs * vft.{_compChar[bc]}";
            Description = $"Vector multiply-add broadcast {_compChar[bc]}: ACC + vfs * broadcast(vft.{_compChar[bc]})";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rd);
            float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
            Vector4 acc = cpu.VU0.ReadACC();
            cpu.VU0.WriteVF(args.Shamt, acc + vs * new Vector4(c), 0xF);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {VU0State.GetVFRegisterName(args.Shamt)}, {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | (0x0C + _bc));

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfd", VU0State.GetVFRegisterName(args.Shamt))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rd);
                float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
                Vector4 acc = cpu.VU0.ReadACC();
                return $"{eq} => ACC({acc}) + {vs} * {c}";
            }
            return eq;
        }
    }

    // VMSUBx/y/z/w - Vector Multiply-Subtract with Broadcast Component (functs 0x10-0x13)
    public sealed class VMSUBBc : Instruction<Args_R>
    {
        private static readonly char[] _compChar = ['x', 'y', 'z', 'w'];
        private readonly int _bc;

        public VMSUBBc(int bc)
        {
            _bc = bc;
            Opcode = 0x12; Funct = 0x10 + bc; // 0x10-0x13
            Name = "vmsub" + _compChar[bc];
            ArgType = InstrArgType.R; ArgNames = ["vfd", "vfs", "vft"];
            Equation = $"vfd = ACC - vfs * vft.{_compChar[bc]}";
            Description = $"Vector multiply-subtract broadcast {_compChar[bc]}: ACC - vfs * broadcast(vft.{_compChar[bc]})";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rd);
            float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
            Vector4 acc = cpu.VU0.ReadACC();
            cpu.VU0.WriteVF(args.Shamt, acc - vs * new Vector4(c), 0xF);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {VU0State.GetVFRegisterName(args.Shamt)}, {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | (0x10 + _bc));

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfd", VU0State.GetVFRegisterName(args.Shamt))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rd);
                float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
                Vector4 acc = cpu.VU0.ReadACC();
                return $"{eq} => ACC({acc}) - {vs} * {c}";
            }
            return eq;
        }
    }

    // VMAXx/y/z/w - Vector Maximum with Broadcast Component (functs 0x14-0x17)
    public sealed class VMAXBc : Instruction<Args_R>
    {
        private static readonly char[] _compChar = ['x', 'y', 'z', 'w'];
        private readonly int _bc;

        public VMAXBc(int bc)
        {
            _bc = bc;
            Opcode = 0x12; Funct = 0x14 + bc; // 0x14-0x17
            Name = "vmax" + _compChar[bc];
            ArgType = InstrArgType.R; ArgNames = ["vfd", "vfs", "vft"];
            Equation = $"vfd = max(vfs, vft.{_compChar[bc]})";
            Description = $"Vector max broadcast {_compChar[bc]}: component-wise max(vfs, broadcast(vft.{_compChar[bc]}))";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rd);
            float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
            cpu.VU0.WriteVF(args.Shamt, Vector4.Max(vs, new Vector4(c)), 0xF);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {VU0State.GetVFRegisterName(args.Shamt)}, {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | (0x14 + _bc));

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfd", VU0State.GetVFRegisterName(args.Shamt))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rd);
                float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
                return $"{eq} => max({vs}, {c})";
            }
            return eq;
        }
    }

    // VMINx/y/z/w - Vector Minimum with Broadcast Component (functs 0x18-0x1B)
    public sealed class VMINBc : Instruction<Args_R>
    {
        private static readonly char[] _compChar = ['x', 'y', 'z', 'w'];
        private readonly int _bc;

        public VMINBc(int bc)
        {
            _bc = bc;
            Opcode = 0x12; Funct = 0x18 + bc; // 0x18-0x1B
            Name = "vmin" + _compChar[bc];
            ArgType = InstrArgType.R; ArgNames = ["vfd", "vfs", "vft"];
            Equation = $"vfd = min(vfs, vft.{_compChar[bc]})";
            Description = $"Vector min broadcast {_compChar[bc]}: component-wise min(vfs, broadcast(vft.{_compChar[bc]}))";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rd);
            float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
            cpu.VU0.WriteVF(args.Shamt, Vector4.Min(vs, new Vector4(c)), 0xF);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {VU0State.GetVFRegisterName(args.Shamt)}, {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | (0x18 + _bc));

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfd", VU0State.GetVFRegisterName(args.Shamt))
                .Replace("vfs", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rd);
                float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
                return $"{eq} => min({vs}, {c})";
            }
            return eq;
        }
    }

    // VMULAx/y/z/w - Vector Multiply to ACC with Broadcast Component (functs 0x1C-0x1F)
    public sealed class VMULABc : Instruction<Args_R>
    {
        private static readonly char[] _compChar = ['x', 'y', 'z', 'w'];
        private readonly int _bc;

        public VMULABc(int bc)
        {
            _bc = bc;
            Opcode = 0x12; Funct = 0x1C + bc; // 0x1C-0x1F
            Name = "vmula" + _compChar[bc];
            ArgType = InstrArgType.R; ArgNames = ["vfs", "vft"];
            Equation = $"ACC = vfs * vft.{_compChar[bc]}";
            Description = $"Vector multiply broadcast {_compChar[bc]} to ACC: vfs * broadcast(vft.{_compChar[bc]})";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // For ACC-writing variants: args.Rd = vfs, args.Rt = vft
            Vector4 vs = cpu.VU0.ReadVF(args.Rd);
            float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
            cpu.VU0.WriteACC(vs * new Vector4(c), 0xF);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} ACC, {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            // args[0]=vfs, args[1]=vft
            => (uint)(0x12 << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | (0x1C + _bc));

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfs", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rd);
                float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
                return $"{eq} => {vs} * {c}";
            }
            return eq;
        }
    }

    // VADDAx/y/z/w - Vector Add to ACC with Broadcast Component (functs 0x20-0x23)
    public sealed class VADDABc : Instruction<Args_R>
    {
        private static readonly char[] _compChar = ['x', 'y', 'z', 'w'];
        private readonly int _bc;

        public VADDABc(int bc)
        {
            _bc = bc;
            Opcode = 0x12; Funct = 0x20 + bc; // 0x20-0x23
            Name = "vadda" + _compChar[bc];
            ArgType = InstrArgType.R; ArgNames = ["vfs", "vft"];
            Equation = $"ACC = vfs + vft.{_compChar[bc]}";
            Description = $"Vector add broadcast {_compChar[bc]} to ACC: vfs + broadcast(vft.{_compChar[bc]})";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rd);
            float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
            cpu.VU0.WriteACC(vs + new Vector4(c), 0xF);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} ACC, {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | (0x20 + _bc));

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfs", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rd);
                float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
                return $"{eq} => {vs} + {c}";
            }
            return eq;
        }
    }

    // VSUBAx/y/z/w - Vector Subtract to ACC with Broadcast Component (functs 0x24-0x27)
    public sealed class VSUBABc : Instruction<Args_R>
    {
        private static readonly char[] _compChar = ['x', 'y', 'z', 'w'];
        private readonly int _bc;

        public VSUBABc(int bc)
        {
            _bc = bc;
            Opcode = 0x12; Funct = 0x24 + bc; // 0x24-0x27
            Name = "vsuba" + _compChar[bc];
            ArgType = InstrArgType.R; ArgNames = ["vfs", "vft"];
            Equation = $"ACC = vfs - vft.{_compChar[bc]}";
            Description = $"Vector subtract broadcast {_compChar[bc]} to ACC: vfs - broadcast(vft.{_compChar[bc]})";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            Vector4 vs = cpu.VU0.ReadVF(args.Rd);
            float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
            cpu.VU0.WriteACC(vs - new Vector4(c), 0xF);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} ACC, {VU0State.GetVFRegisterName(args.Rd)}, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | (0x24 + _bc));

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("vfs", VU0State.GetVFRegisterName(args.Rd))
                .Replace("vft", VU0State.GetVFRegisterName(args.Rt));
            if (cpu != null)
            {
                Vector4 vs = cpu.VU0.ReadVF(args.Rd);
                float c = VU0Helpers.GetComponent(cpu.VU0.ReadVF(args.Rt), _bc);
                return $"{eq} => {vs} - {c}";
            }
            return eq;
        }
    }

    // =====================================================================
    // VU0 Micro-Mode Trigger Instructions
    // =====================================================================

    // VCALLMS - Vector Call Micro Subroutine (COP2 rs=0x18)
    // Starts VU0 micro-mode execution at the specified immediate address.
    // The 15-bit entry address is encoded in instruction bits [20:6]:
    //   args.Rt = bits[20:16], args.Rd = bits[15:11], args.Shamt = bits[10:6]
    //   byte_address = ((args.Rt<<10)|(args.Rd<<5)|args.Shamt) * 8
    public sealed class VCALLMS : Instruction<Args_R>
    {
        public VCALLMS()
        {
            Opcode = 0x12; Funct = null; // rs=0x18 distinguishes this instruction
            Name = "vcallms";
            ArgType = InstrArgType.R; ArgNames = ["imm15"];
            Equation = "VU0.Start(imm15 * 8)";
            Description = "Call Micro Subroutine: start VU0 micro-mode at address imm15*8";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Reconstruct the 15-bit address from Rt, Rd, Shamt fields
            uint entry = (uint)((args.Rt << 10) | (args.Rd << 5) | args.Shamt);
            cpu.VU0.Start((ushort)(entry * 8));
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            uint entry = (uint)((args.Rt << 10) | (args.Rd << 5) | args.Shamt);
            return $"{Name} 0x{entry:X4}";
        }

        public override uint Asm(int[] args)
        {
            // args[0] = 15-bit address; encode into Rt:Rd:Shamt fields
            uint addr = (uint)args[0];
            uint rt = (addr >> 10) & 0x1F;
            uint rd = (addr >> 5) & 0x1F;
            uint shamt = addr & 0x1F;
            return (uint)(0x12 << 26 | 0x18 << 21 | rt << 16 | rd << 11 | shamt << 6);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            uint entry = (uint)((args.Rt << 10) | (args.Rd << 5) | args.Shamt);
            return $"VU0.Start(0x{entry * 8:X4}) [entry=0x{entry:X4}]";
        }
    }

    // VCALLMSR - Vector Call Micro Subroutine Register (COP2 rs=0x19)
    // Starts VU0 micro-mode execution at the address stored in a VI register.
    // The VI register index is encoded in the Rd field (bits[15:11]).
    public sealed class VCALLMSR : Instruction<Args_R>
    {
        public VCALLMSR()
        {
            Opcode = 0x12; Funct = null; // rs=0x19 distinguishes this instruction
            Name = "vcallmsr";
            ArgType = InstrArgType.R; ArgNames = ["vis"];
            Equation = "VU0.Start(VI[vis] * 8)";
            Description = "Call Micro Subroutine Register: start VU0 micro-mode at VI[vis]*8";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ushort addr = cpu.VU0.ReadVI(args.Rd & 0xF);
            cpu.VU0.Start((ushort)(addr * 8));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} vi{args.Rd & 0xF}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x19 << 21 | (args[0] & 0xF) << 11);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu != null)
            {
                ushort addr = cpu.VU0.ReadVI(args.Rd & 0xF);
                return $"VU0.Start(0x{addr * 8:X4}) [vi{args.Rd & 0xF}=0x{addr:X4}]";
            }
            return $"VU0.Start(VI[vi{args.Rd & 0xF}] * 8)";
        }
    }

    // =====================================================================
    // VU0 LFSR Pseudo-Random Number Generator Instructions
    // (COP2 SPECIAL2: opcode=0x12, rs=0x1E)
    //
    // The R register holds a 23-bit LFSR state (RngState in VU0State).
    // Feedback polynomial taps: bits 22, 17, 11, 6.
    //
    // Field mapping for SPECIAL2 instructions:
    //   args.Rs    = 0x1E  (SPECIAL2 indicator)
    //   args.Rt    = fs    (bits[20:16] - source VF register for VRINIT/VRXOR)
    //   args.Rd    = fd    (bits[15:11] - destination VF register for VRNEXT/VRGET)
    // =====================================================================

    // VRNEXT - Vector Random Next (COP2 rs=0x1E, funct=0x1C)
    // Advances the LFSR and writes the new value to the destination VF register.
    public sealed class VRNEXT : Instruction<Args_R>
    {
        public VRNEXT()
        {
            Opcode = 0x12; Funct = 0x1C;
            Name = "vrnext";
            ArgType = InstrArgType.R; ArgNames = ["vfd"];
            Equation = "R = NextRng(R); vfd = R";
            Description = "Advance LFSR and write next pseudo-random value to vfd (bits[15:11])";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            cpu.VU0.RngState = VU0Helpers.NextRng(cpu.VU0.RngState);
            float rval = BitConverter.UInt32BitsToSingle(cpu.VU0.RngState & 0x7FFFFFu);
            cpu.VU0.WriteVF(args.Rd, new Vector4(rval), 0xF);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {VU0State.GetVFRegisterName(args.Rd)}, R";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x1E << 21 | args[0] << 11 | 0x1C);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu != null)
            {
                uint next = VU0Helpers.NextRng(cpu.VU0.RngState);
                return $"R = 0x{next:X6}; {VU0State.GetVFRegisterName(args.Rd)} = R";
            }
            return Equation.Replace("vfd", VU0State.GetVFRegisterName(args.Rd));
        }
    }

    // VRGET - Vector Random Get (COP2 rs=0x1E, funct=0x1D)
    // Reads the current LFSR value into the destination VF register without advancing.
    public sealed class VRGET : Instruction<Args_R>
    {
        public VRGET()
        {
            Opcode = 0x12; Funct = 0x1D;
            Name = "vrget";
            ArgType = InstrArgType.R; ArgNames = ["vfd"];
            Equation = "vfd = R";
            Description = "Get current LFSR value into vfd (bits[15:11]) without advancing R";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            float rval = BitConverter.UInt32BitsToSingle(cpu.VU0.RngState & 0x7FFFFFu);
            cpu.VU0.WriteVF(args.Rd, new Vector4(rval), 0xF);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {VU0State.GetVFRegisterName(args.Rd)}, R";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x1E << 21 | args[0] << 11 | 0x1D);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu != null)
                return $"{VU0State.GetVFRegisterName(args.Rd)} = R = 0x{cpu.VU0.RngState:X6}";
            return Equation.Replace("vfd", VU0State.GetVFRegisterName(args.Rd));
        }
    }

    // VRINIT - Vector Random Init (COP2 rs=0x1E, funct=0x1E)
    // Initializes the LFSR from the integer bit pattern of the vfs register (bits[20:16]).
    public sealed class VRINIT : Instruction<Args_R>
    {
        public VRINIT()
        {
            Opcode = 0x12; Funct = 0x1E;
            Name = "vrinit";
            ArgType = InstrArgType.R; ArgNames = ["vfs"];
            Equation = "R = (uint)vfs.w & 0x7FFFFF";
            Description = "Initialize LFSR from integer bits of vfs.w (bits[20:16])";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Source register is in args.Rt (bits[20:16])
            uint bits = BitConverter.SingleToUInt32Bits(cpu.VU0.ReadVF(args.Rt).W);
            cpu.VU0.RngState = bits & 0x7FFFFFu;
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} R, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x1E << 21 | args[0] << 16 | 0x1E);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu != null)
            {
                uint bits = BitConverter.SingleToUInt32Bits(cpu.VU0.ReadVF(args.Rt).W);
                return $"R = 0x{bits & 0x7FFFFFu:X6} (from {VU0State.GetVFRegisterName(args.Rt)}.w bits)";
            }
            return Equation.Replace("vfs", VU0State.GetVFRegisterName(args.Rt));
        }
    }

    // VRXOR - Vector Random XOR (COP2 rs=0x1E, funct=0x1F)
    // XORs the LFSR with the integer bit pattern of the vfs register (bits[20:16]).
    public sealed class VRXOR : Instruction<Args_R>
    {
        public VRXOR()
        {
            Opcode = 0x12; Funct = 0x1F;
            Name = "vrxor";
            ArgType = InstrArgType.R; ArgNames = ["vfs"];
            Equation = "R = R ^ ((uint)vfs.w & 0x7FFFFF)";
            Description = "XOR LFSR with integer bits of vfs.w (bits[20:16])";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Source register is in args.Rt (bits[20:16])
            uint bits = BitConverter.SingleToUInt32Bits(cpu.VU0.ReadVF(args.Rt).W);
            cpu.VU0.RngState ^= (bits & 0x7FFFFFu);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} R, {VU0State.GetVFRegisterName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x12 << 26 | 0x1E << 21 | args[0] << 16 | 0x1F);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu != null)
            {
                uint bits = BitConverter.SingleToUInt32Bits(cpu.VU0.ReadVF(args.Rt).W);
                uint newState = cpu.VU0.RngState ^ (bits & 0x7FFFFFu);
                return $"R = 0x{cpu.VU0.RngState:X6} ^ 0x{bits & 0x7FFFFFu:X6} = 0x{newState:X6}";
            }
            return Equation.Replace("vfs", VU0State.GetVFRegisterName(args.Rt));
        }
    }
}
