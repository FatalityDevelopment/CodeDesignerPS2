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
}
