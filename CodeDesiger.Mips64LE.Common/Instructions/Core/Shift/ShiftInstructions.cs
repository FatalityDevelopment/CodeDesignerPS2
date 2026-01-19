using CodeDesigner.Mips64LE.Common.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.Common.CPU;
using System;

/*
| SLL | Shift left logical |
| SRL | Shift right logical |
| SRA | Shift right arithmetic |
| DSLL | Shift left logical (64-bit) |
| DSRL | Shift right logical (64-bit) |
| DSRA | Shift right arithmetic (64-bit) |
| SLLV | Shift left logical variable |
| SRLV | Shift right logical variable |
| SRAV | Shift right arithmetic variable |
| DSLLV | Shift left logical variable (64-bit) |
| DSRLV | Shift right logical variable (64-bit) |
| DSRAV | Shift right arithmetic variable (64-bit) |
 */
namespace CodeDesigner.Mips64LE.Common.Cpu.Instructions.EE
{
    // SLL - Shift left logical
    public sealed class SLL : Instruction<Args_R>
    {
        public SLL()
        {
            Opcode = 0x00;
            Funct = 0x00;
            Name = "sll";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rt", "sa"];
            Equation = "rd = rt << sa";
            Description = "Shift left logical 32-bit";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            uint rt = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
            int sa = args.Shamt;
            uint result = rt << sa;
            CpuHelpers.SetReg32(cpu, args.Rd, (int)result);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {args.Shamt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[1] << 16 | args[0] << 11 | args[2] << 6 | (Funct ?? 0x00));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            // Check if this is actually a NOP (all fields are zero)
            if (args.Rd == 0 && args.Rt == 0 && args.Shamt == 0)
            {
                return "No operation";
            }
            
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("sa", args.Shamt.ToString());
            
            if (cpu != null)
            {
                uint rtVal = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
                return $"{eq} => 0x{rtVal:X8} << {args.Shamt} = 0x{rtVal << args.Shamt:X8}";
            }
            return eq;
        }
    }

    // SRL - Shift right logical
    public sealed class SRL : Instruction<Args_R>
    {
        public SRL()
        {
            Opcode = 0x00;
            Funct = 0x02;
            Name = "srl";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rt", "sa"];
            Equation = "rd = rt >> sa";
            Description = "Shift right logical 32-bit";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            uint rt = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
            int sa = args.Shamt;
            uint result = rt >> sa;
            CpuHelpers.SetReg32(cpu, args.Rd, (int)result);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {args.Shamt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[1] << 16 | args[0] << 11 | args[2] << 6 | (Funct ?? 0x02));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("sa", args.Shamt.ToString());
            
            if (cpu != null)
            {
                uint rtVal = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
                return $"{eq} => 0x{rtVal:X8} >> {args.Shamt} = 0x{rtVal >> args.Shamt:X8}";
            }
            return eq;
        }
    }

    // SRA - Shift right arithmetic
    public sealed class SRA : Instruction<Args_R>
    {
        public SRA()
        {
            Opcode = 0x00;
            Funct = 0x03;
            Name = "sra";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rt", "sa"];
            Equation = "rd = rt >> sa (arithmetic)";
            Description = "Shift right arithmetic 32-bit";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int rt = CpuHelpers.GetReg32(cpu, args.Rt);
            int sa = args.Shamt;
            int result = rt >> sa;
            CpuHelpers.SetReg32(cpu, args.Rd, result);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {args.Shamt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[1] << 16 | args[0] << 11 | args[2] << 6 | (Funct ?? 0x03));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("sa", args.Shamt.ToString());
            
            if (cpu != null)
            {
                int rtVal = CpuHelpers.GetReg32(cpu, args.Rt);
                return $"{eq} => {rtVal} >> {args.Shamt} = {rtVal >> args.Shamt}";
            }
            return eq;
        }
    }

    // DSLL - Shift left logical (64-bit)
    public sealed class DSLL : Instruction<Args_R>
    {
        public DSLL()
        {
            Opcode = 0x00;
            Funct = 0x38;
            Name = "dsll";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rt", "sa"];
            Equation = "rd = rt << sa";
            Description = "Shift left logical 64-bit";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rt = cpu.RegsLo[args.Rt];
            int sa = args.Shamt;
            ulong result = rt << sa;
            cpu.RegsLo[args.Rd] = result;
            cpu.RegsHi[args.Rd] = 0;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {args.Shamt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[1] << 16 | args[0] << 11 | args[2] << 6 | (Funct ?? 0x38));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("sa", args.Shamt.ToString());
            
            if (cpu != null)
            {
                ulong rtVal = cpu.RegsLo[args.Rt];
                return $"{eq} => 0x{rtVal:X16} << {args.Shamt} = 0x{rtVal << args.Shamt:X16}";
            }
            return eq;
        }
    }

    // DSRL - Shift right logical (64-bit)
    public sealed class DSRL : Instruction<Args_R>
    {
        public DSRL()
        {
            Opcode = 0x00;
            Funct = 0x3A;
            Name = "dsrl";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rt", "sa"];
            Equation = "rd = rt >> sa";
            Description = "Shift right logical 64-bit";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rt = cpu.RegsLo[args.Rt];
            int sa = args.Shamt;
            ulong result = rt >> sa;
            cpu.RegsLo[args.Rd] = result;
            cpu.RegsHi[args.Rd] = 0;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {args.Shamt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[1] << 16 | args[0] << 11 | args[2] << 6 | (Funct ?? 0x3A));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("sa", args.Shamt.ToString());
            
            if (cpu != null)
            {
                ulong rtVal = cpu.RegsLo[args.Rt];
                return $"{eq} => 0x{rtVal:X16} >> {args.Shamt} = 0x{rtVal >> args.Shamt:X16}";
            }
            return eq;
        }
    }

    // DSRA - Shift right arithmetic (64-bit)
    public sealed class DSRA : Instruction<Args_R>
    {
        public DSRA()
        {
            Opcode = 0x00;
            Funct = 0x3B;
            Name = "dsra";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rt", "sa"];
            Equation = "rd = rt >> sa (arithmetic)";
            Description = "Shift right arithmetic 64-bit";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            long rt = (long)cpu.RegsLo[args.Rt];
            int sa = args.Shamt;
            long result = rt >> sa;
            cpu.RegsLo[args.Rd] = (ulong)result;
            cpu.RegsHi[args.Rd] = 0;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {args.Shamt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[1] << 16 | args[0] << 11 | args[2] << 6 | (Funct ?? 0x3B));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("sa", args.Shamt.ToString());
            
            if (cpu != null)
            {
                long rtVal = (long)cpu.RegsLo[args.Rt];
                return $"{eq} => {rtVal} >> {args.Shamt} = {rtVal >> args.Shamt}";
            }
            return eq;
        }
    }

    // SLLV - Shift left logical variable
    public sealed class SLLV : Instruction<Args_R>
    {
        public SLLV()
        {
            Opcode = 0x00;
            Funct = 0x04;
            Name = "sllv";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rt", "rs"];
            Equation = "rd = rt << rs[4:0]";
            Description = "Shift left logical variable 32-bit";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            uint rt = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
            int rs = CpuHelpers.GetReg32(cpu, args.Rs);
            int sa = rs & 0x1F; // Use only lower 5 bits
            uint result = rt << sa;
            CpuHelpers.SetReg32(cpu, args.Rd, (int)result);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {CpuHelpers.RegName(args.Rs)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[2] << 21 | args[1] << 16 | args[0] << 11 | (Funct ?? 0x04));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs));
            
            if (cpu != null)
            {
                uint rtVal = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
                int rsVal = CpuHelpers.GetReg32(cpu, args.Rs);
                int sa = rsVal & 0x1F;
                return $"{eq} => 0x{rtVal:X8} << {sa} = 0x{rtVal << sa:X8}";
            }
            return eq;
        }
    }

    // SRLV - Shift right logical variable
    public sealed class SRLV : Instruction<Args_R>
    {
        public SRLV()
        {
            Opcode = 0x00;
            Funct = 0x06;
            Name = "srlv";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rt", "rs"];
            Equation = "rd = rt >> rs[4:0]";
            Description = "Shift right logical variable 32-bit";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            uint rt = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
            int rs = CpuHelpers.GetReg32(cpu, args.Rs);
            int sa = rs & 0x1F; // Use only lower 5 bits
            uint result = rt >> sa;
            CpuHelpers.SetReg32(cpu, args.Rd, (int)result);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {CpuHelpers.RegName(args.Rs)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[2] << 21 | args[1] << 16 | args[0] << 11 | (Funct ?? 0x06));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs));
            
            if (cpu != null)
            {
                uint rtVal = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
                int rsVal = CpuHelpers.GetReg32(cpu, args.Rs);
                int sa = rsVal & 0x1F;
                return $"{eq} => 0x{rtVal:X8} >> {sa} = 0x{rtVal >> sa:X8}";
            }
            return eq;
        }
    }

    // SRAV - Shift right arithmetic variable
    public sealed class SRAV : Instruction<Args_R>
    {
        public SRAV()
        {
            Opcode = 0x00;
            Funct = 0x07;
            Name = "srav";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rt", "rs"];
            Equation = "rd = rt >> rs[4:0] (arithmetic)";
            Description = "Shift right arithmetic variable 32-bit";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int rt = CpuHelpers.GetReg32(cpu, args.Rt);
            int rs = CpuHelpers.GetReg32(cpu, args.Rs);
            int sa = rs & 0x1F; // Use only lower 5 bits
            int result = rt >> sa;
            CpuHelpers.SetReg32(cpu, args.Rd, result);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {CpuHelpers.RegName(args.Rs)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[2] << 21 | args[1] << 16 | args[0] << 11 | (Funct ?? 0x07));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs));
            
            if (cpu != null)
            {
                int rtVal = CpuHelpers.GetReg32(cpu, args.Rt);
                int rsVal = CpuHelpers.GetReg32(cpu, args.Rs);
                int sa = rsVal & 0x1F;
                return $"{eq} => {rtVal} >> {sa} = {rtVal >> sa}";
            }
            return eq;
        }
    }

    // DSLLV - Shift left logical variable (64-bit)
    public sealed class DSLLV : Instruction<Args_R>
    {
        public DSLLV()
        {
            Opcode = 0x00;
            Funct = 0x14;
            Name = "dsllv";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rt", "rs"];
            Equation = "rd = rt << rs[5:0]";
            Description = "Shift left logical variable 64-bit";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rt = cpu.RegsLo[args.Rt];
            int rs = (int)cpu.RegsLo[args.Rs];
            int sa = rs & 0x3F; // Use only lower 6 bits
            ulong result = rt << sa;
            cpu.RegsLo[args.Rd] = result;
            cpu.RegsHi[args.Rd] = 0;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {CpuHelpers.RegName(args.Rs)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[2] << 21 | args[1] << 16 | args[0] << 11 | (Funct ?? 0x14));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs));
            
            if (cpu != null)
            {
                ulong rtVal = cpu.RegsLo[args.Rt];
                int rsVal = (int)cpu.RegsLo[args.Rs];
                int sa = rsVal & 0x3F;
                return $"{eq} => 0x{rtVal:X16} << {sa} = 0x{rtVal << sa:X16}";
            }
            return eq;
        }
    }

    // DSRLV - Shift right logical variable (64-bit)
    public sealed class DSRLV : Instruction<Args_R>
    {
        public DSRLV()
        {
            Opcode = 0x00;
            Funct = 0x16;
            Name = "dsrlv";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rt", "rs"];
            Equation = "rd = rt >> rs[5:0]";
            Description = "Shift right logical variable 64-bit";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rt = cpu.RegsLo[args.Rt];
            int rs = (int)cpu.RegsLo[args.Rs];
            int sa = rs & 0x3F; // Use only lower 6 bits
            ulong result = rt >> sa;
            cpu.RegsLo[args.Rd] = result;
            cpu.RegsHi[args.Rd] = 0;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {CpuHelpers.RegName(args.Rs)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[2] << 21 | args[1] << 16 | args[0] << 11 | (Funct ?? 0x16));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs));
            
            if (cpu != null)
            {
                ulong rtVal = cpu.RegsLo[args.Rt];
                int rsVal = (int)cpu.RegsLo[args.Rs];
                int sa = rsVal & 0x3F;
                return $"{eq} => 0x{rtVal:X16} >> {sa} = 0x{rtVal >> sa:X16}";
            }
            return eq;
        }
    }

    // DSRAV - Shift right arithmetic variable (64-bit)
    public sealed class DSRAV : Instruction<Args_R>
    {
        public DSRAV()
        {
            Opcode = 0x00;
            Funct = 0x17;
            Name = "dsrav";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rt", "rs"];
            Equation = "rd = rt >> rs[5:0] (arithmetic)";
            Description = "Shift right arithmetic variable 64-bit";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            long rt = (long)cpu.RegsLo[args.Rt];
            int rs = (int)cpu.RegsLo[args.Rs];
            int sa = rs & 0x3F; // Use only lower 6 bits
            long result = rt >> sa;
            cpu.RegsLo[args.Rd] = (ulong)result;
            cpu.RegsHi[args.Rd] = 0;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {CpuHelpers.RegName(args.Rs)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[2] << 21 | args[1] << 16 | args[0] << 11 | (Funct ?? 0x17));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs));
            
            if (cpu != null)
            {
                long rtVal = (long)cpu.RegsLo[args.Rt];
                int rsVal = (int)cpu.RegsLo[args.Rs];
                int sa = rsVal & 0x3F;
                return $"{eq} => {rtVal} >> {sa} = {rtVal >> sa}";
            }
            return eq;
        }
    }

    // DSLL32 - Shift left logical (64-bit) + 32
    public sealed class DSLL32 : Instruction<Args_R>
    {
        public DSLL32()
        {
            Opcode = 0x00;
            Funct = 0x3C;
            Name = "dsll32";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rt", "sa"];
            Equation = "rd = rt << (sa + 32)";
            Description = "Shift left logical 64-bit with sa + 32";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rt = cpu.RegsLo[args.Rt];
            int sa = args.Shamt + 32;
            ulong result = rt << sa;
            cpu.RegsLo[args.Rd] = result;
            cpu.RegsHi[args.Rd] = 0;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {args.Shamt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[1] << 16 | args[0] << 11 | args[2] << 6 | (Funct ?? 0x3C));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("sa", args.Shamt.ToString());
            
            if (cpu != null)
            {
                ulong rtVal = cpu.RegsLo[args.Rt];
                int sa = args.Shamt + 32;
                return $"{eq} => 0x{rtVal:X16} << {sa} = 0x{rtVal << sa:X16}";
            }
            return eq;
        }
    }

    // DSRL32 - Shift right logical (64-bit) + 32
    public sealed class DSRL32 : Instruction<Args_R>
    {
        public DSRL32()
        {
            Opcode = 0x00;
            Funct = 0x3E;
            Name = "dsrl32";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rt", "sa"];
            Equation = "rd = rt >> (sa + 32)";
            Description = "Shift right logical 64-bit with sa + 32";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rt = cpu.RegsLo[args.Rt];
            int sa = args.Shamt + 32;
            ulong result = rt >> sa;
            cpu.RegsLo[args.Rd] = result;
            cpu.RegsHi[args.Rd] = 0;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {args.Shamt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[1] << 16 | args[0] << 11 | args[2] << 6 | (Funct ?? 0x3E));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("sa", args.Shamt.ToString());
            
            if (cpu != null)
            {
                ulong rtVal = cpu.RegsLo[args.Rt];
                int sa = args.Shamt + 32;
                return $"{eq} => 0x{rtVal:X16} >> {sa} = 0x{rtVal >> sa:X16}";
            }
            return eq;
        }
    }

    // DSRA32 - Shift right arithmetic (64-bit) + 32
    public sealed class DSRA32 : Instruction<Args_R>
    {
        public DSRA32()
        {
            Opcode = 0x00;
            Funct = 0x3F;
            Name = "dsra32";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rt", "sa"];
            Equation = "rd = rt >> (sa + 32) (arithmetic)";
            Description = "Shift right arithmetic 64-bit with sa + 32";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            long rt = (long)cpu.RegsLo[args.Rt];
            int sa = args.Shamt + 32;
            long result = rt >> sa;
            cpu.RegsLo[args.Rd] = (ulong)result;
            cpu.RegsHi[args.Rd] = 0;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {args.Shamt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[1] << 16 | args[0] << 11 | args[2] << 6 | (Funct ?? 0x3F));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("sa", args.Shamt.ToString());
            
            if (cpu != null)
            {
                long rtVal = (long)cpu.RegsLo[args.Rt];
                int sa = args.Shamt + 32;
                return $"{eq} => {rtVal} >> {sa} = {rtVal >> sa}";
            }
            return eq;
        }
    }
}
