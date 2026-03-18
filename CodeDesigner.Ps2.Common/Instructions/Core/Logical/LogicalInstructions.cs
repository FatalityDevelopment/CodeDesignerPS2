using CodeDesigner.Ps2.Common.Cpu.InstrArgs;
using CodeDesigner.Ps2.Common.CPU;
using System;

/* 
| Instruction | Description |
|-------------|-------------|
| AND | Bitwise AND |
| OR | Bitwise OR |
| XOR | Bitwise XOR |
| NOR | Bitwise NOR |
| ANDI | AND immediate |
| ORI | OR immediate |
| XORI | XOR immediate |
*/
namespace CodeDesigner.Ps2.Common.Cpu.Instructions.EE
{
    // AND - Bitwise AND
    public sealed class AND : Instruction<Args_R>
    {
        public AND()
        {
            Opcode = 0x00;
            Funct = 0x24;
            Name = "and";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs & rt";
            Description = "Bitwise AND operation";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];
            cpu.RegsLo[args.Rd] = rs & rt;
            cpu.RegsHi[args.Rd] = cpu.RegsHi[args.Rs] & cpu.RegsHi[args.Rt];
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | args[2] << 11 | (Funct ?? 0x24));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));
            
            if (cpu != null)
            {
                ulong rsVal = cpu.RegsLo[args.Rs];
                ulong rtVal = cpu.RegsLo[args.Rt];
                return $"{eq} => 0x{rsVal:X8} & 0x{rtVal:X8} = 0x{rsVal & rtVal:X8}";
            }
            return eq;
        }
    }

    // OR - Bitwise OR
    public sealed class OR : Instruction<Args_R>
    {
        public OR()
        {
            Opcode = 0x00;
            Funct = 0x25;
            Name = "or";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs | rt";
            Description = "Bitwise OR operation";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];
            cpu.RegsLo[args.Rd] = rs | rt;
            cpu.RegsHi[args.Rd] = cpu.RegsHi[args.Rs] | cpu.RegsHi[args.Rt];
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | args[2] << 11 | (Funct ?? 0x25));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));
            
            if (cpu != null)
            {
                ulong rsVal = cpu.RegsLo[args.Rs];
                ulong rtVal = cpu.RegsLo[args.Rt];
                return $"{eq} => 0x{rsVal:X8} | 0x{rtVal:X8} = 0x{rsVal | rtVal:X8}";
            }
            return eq;
        }
    }

    // XOR - Bitwise XOR
    public sealed class XOR : Instruction<Args_R>
    {
        public XOR()
        {
            Opcode = 0x00;
            Funct = 0x26;
            Name = "xor";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs ^ rt";
            Description = "Bitwise XOR (exclusive OR) operation";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];
            cpu.RegsLo[args.Rd] = rs ^ rt;
            cpu.RegsHi[args.Rd] = cpu.RegsHi[args.Rs] ^ cpu.RegsHi[args.Rt];
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | args[2] << 11 | (Funct ?? 0x26));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));
            
            if (cpu != null)
            {
                ulong rsVal = cpu.RegsLo[args.Rs];
                ulong rtVal = cpu.RegsLo[args.Rt];
                return $"{eq} => 0x{rsVal:X8} ^ 0x{rtVal:X8} = 0x{rsVal ^ rtVal:X8}";
            }
            return eq;
        }
    }

    // NOR - Bitwise NOR
    public sealed class NOR : Instruction<Args_R>
    {
        public NOR()
        {
            Opcode = 0x00;
            Funct = 0x27;
            Name = "nor";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = ~(rs | rt)";
            Description = "Bitwise NOR (NOT OR) operation";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];
            cpu.RegsLo[args.Rd] = ~(rs | rt);
            cpu.RegsHi[args.Rd] = ~(cpu.RegsHi[args.Rs] | cpu.RegsHi[args.Rt]);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | args[2] << 11 | (Funct ?? 0x27));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));
            
            if (cpu != null)
            {
                ulong rsVal = cpu.RegsLo[args.Rs];
                ulong rtVal = cpu.RegsLo[args.Rt];
                return $"{eq} => ~(0x{rsVal:X8} | 0x{rtVal:X8}) = 0x{~(rsVal | rtVal):X8}";
            }
            return eq;
        }
    }

    // ANDI - AND immediate
    public sealed class ANDI : Instruction<Args_I>
    {
        public ANDI()
        {
            Opcode = 0x0C;
            Funct = null;
            Name = "andi";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "rs", "imm"];
            Equation = "rt = rs & ZeroExt(imm)";
            Description = "Bitwise AND with zero-extended immediate";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong imm = (ushort)args.Imm; // Zero-extend immediate
            cpu.RegsLo[args.Rt] = rs & imm;
            cpu.RegsHi[args.Rt] = 0; // Upper 64 bits cleared for immediate operations
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {CpuHelpers.RegName(args.Rs)}, ${args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            ushort imm = (ushort)args.Imm;
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("ZeroExt(imm)", $"0x{imm:X4}");
            
            if (cpu != null)
            {
                ulong rsVal = cpu.RegsLo[args.Rs];
                return $"{eq} => 0x{rsVal:X8} & 0x{imm:X4} = 0x{rsVal & imm:X8}";
            }
            return eq;
        }
    }

    // ORI - OR immediate
    public sealed class ORI : Instruction<Args_I>
    {
        public ORI()
        {
            Opcode = 0x0D;
            Funct = null;
            Name = "ori";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "rs", "imm"];
            Equation = "rt = rs | ZeroExt(imm)";
            Description = "Bitwise OR with zero-extended immediate";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong imm = (ushort)args.Imm; // Zero-extend immediate
            cpu.RegsLo[args.Rt] = rs | imm;
            cpu.RegsHi[args.Rt] = cpu.RegsHi[args.Rs]; // Preserve upper 64 bits
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            // Format immediate with $ prefix and 4-digit hex
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {CpuHelpers.RegName(args.Rs)}, ${args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            ushort imm = (ushort)args.Imm;
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("ZeroExt(imm)", $"0x{imm:X4}");
            
            if (cpu != null)
            {
                ulong rsVal = cpu.RegsLo[args.Rs];
                return $"{eq} => 0x{rsVal:X8} | 0x{imm:X4} = 0x{rsVal | imm:X8}";
            }
            return eq;
        }
    }

    // XORI - XOR immediate
    public sealed class XORI : Instruction<Args_I>
    {
        public XORI()
        {
            Opcode = 0x0E;
            Funct = null;
            Name = "xori";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "rs", "imm"];
            Equation = "rt = rs ^ ZeroExt(imm)";
            Description = "Bitwise XOR with zero-extended immediate";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong imm = (ushort)args.Imm; // Zero-extend immediate
            cpu.RegsLo[args.Rt] = rs ^ imm;
            cpu.RegsHi[args.Rt] = cpu.RegsHi[args.Rs]; // Preserve upper 64 bits
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {CpuHelpers.RegName(args.Rs)}, ${args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            ushort imm = (ushort)args.Imm;
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("ZeroExt(imm)", $"0x{imm:X4}");
            
            if (cpu != null)
            {
                ulong rsVal = cpu.RegsLo[args.Rs];
                return $"{eq} => 0x{rsVal:X8} ^ 0x{imm:X4} = 0x{rsVal ^ imm:X8}";
            }
            return eq;
        }
    }
}
