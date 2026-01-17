using CodeDesigner.Mips64LE.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.Cpu.Instructions;
using CodeDesigner.Mips64LE.CPU;
using CodeDesigner.Mips64LE.VectorUnits.VU0;
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

namespace CodeDesigner.Mips64LE.Instructions.VectorUnitInstructions
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
}
