using CodeDesigner.Mips64LE.Common.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.Common.CPU;
using System;
/*
| Instruction | Description |
|-------------|-------------|
| ADD | Add signed 32-bit integers (overflow trap) |
| ADDU | Add unsigned 32-bit integers |
| DADD | Add signed 64-bit integers (overflow trap) |
| DADDU | Add unsigned 64-bit integers |
| SUB | Subtract signed 32-bit integers |
| SUBU | Subtract unsigned 32-bit integers |
| DSUB | Subtract signed 64-bit integers |
| DSUBU | Subtract unsigned 64-bit integers |
| ADDI | Add immediate (signed, overflow trap) |
| ADDIU | Add immediate unsigned |
| DADDI | Add immediate 64-bit (overflow trap) |
| DADDIU | Add immediate 64-bit unsigned |
| MULT | Multiply signed 32-bit |
| MULTU | Multiply unsigned 32-bit |
| DMULT | Multiply signed 64-bit |
| DMULTU | Multiply unsigned 64-bit |
| DIV | Divide signed 32-bit |
| DIVU | Divide unsigned 32-bit |
| DDIV | Divide signed 64-bit |
| DDIVU | Divide unsigned 64-bit |
| MFHI | Move from HI register |
| MFLO | Move from LO register |
| MTHI | Move to HI register |
| MTLO | Move to LO register |
 */
namespace CodeDesigner.Mips64LE.Common.Cpu.Instructions.EE
{
    // ADD - Add signed 32-bit integers (overflow trap)
    public sealed class ADD : Instruction<Args_R>
    {
        public ADD()
        {
            Opcode = 0x00;
            Funct = 0x20;
            Name = "add";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs + rt";
            Description = "Add signed 32-bit integers with overflow trap";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int rs = CpuHelpers.GetReg32(cpu, args.Rs);
            int rt = CpuHelpers.GetReg32(cpu, args.Rt);
            
            try
            {
                int result = checked(rs + rt);
                CpuHelpers.SetReg32(cpu, args.Rd, result);
            }
            catch (OverflowException)
            {
                // TODO: Trigger overflow exception
                throw;
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | args[2] << 11 | (Funct ?? 0x20));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));
            
            if (cpu != null)
            {
                int rsVal = CpuHelpers.GetReg32(cpu, args.Rs);
                int rtVal = CpuHelpers.GetReg32(cpu, args.Rt);
                return $"{eq} => {rsVal} + {rtVal} = {rsVal + rtVal}";
            }
            return eq;
        }
    }

    // ADDU - Add unsigned 32-bit integers
    public sealed class ADDU : Instruction<Args_R>
    {
        public ADDU()
        {
            Opcode = 0x00;
            Funct = 0x21;
            Name = "addu";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs + rt";
            Description = "Add unsigned 32-bit integers without overflow trap";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            uint rs = (uint)CpuHelpers.GetReg32(cpu, args.Rs);
            uint rt = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
            uint result = rs + rt;
            CpuHelpers.SetReg32(cpu, args.Rd, (int)result);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | args[2] << 11 | (Funct ?? 0x21));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));
            
            if (cpu != null)
            {
                uint rsVal = (uint)CpuHelpers.GetReg32(cpu, args.Rs);
                uint rtVal = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
                return $"{eq} => 0x{rsVal:X8} + 0x{rtVal:X8} = 0x{rsVal + rtVal:X8}";
            }
            return eq;
        }
    }

    // DADD - Add signed 64-bit integers (overflow trap)
    public sealed class DADD : Instruction<Args_R>
    {
        public DADD()
        {
            Opcode = 0x00;
            Funct = 0x2C;
            Name = "dadd";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs + rt";
            Description = "Add signed 64-bit integers with overflow trap";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];
            long rt = (long)cpu.RegsLo[args.Rt];
            
            try
            {
                long result = checked(rs + rt);
                cpu.RegsLo[args.Rd] = (ulong)result;
                cpu.RegsHi[args.Rd] = 0;
            }
            catch (OverflowException)
            {
                // TODO: Trigger overflow exception
                throw;
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | args[2] << 11 | (Funct ?? 0x2C));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));
            
            if (cpu != null)
            {
                long rsVal = (long)cpu.RegsLo[args.Rs];
                long rtVal = (long)cpu.RegsLo[args.Rt];
                return $"{eq} => {rsVal} + {rtVal} = {rsVal + rtVal}";
            }
            return eq;
        }
    }

    // DADDU - Add unsigned 64-bit integers
    public sealed class DADDU : Instruction<Args_R>
    {
        public DADDU()
        {
            Opcode = 0x00;
            Funct = 0x2D;
            Name = "daddu";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs + rt";
            Description = "Add unsigned 64-bit integers without overflow trap";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];
            ulong result = rs + rt;
            cpu.RegsLo[args.Rd] = result;
            cpu.RegsHi[args.Rd] = 0;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | args[2] << 11 | (Funct ?? 0x2D));
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
                return $"{eq} => 0x{rsVal:X16} + 0x{rtVal:X16} = 0x{rsVal + rtVal:X16}";
            }
            return eq;
        }
    }

    // SUB - Subtract signed 32-bit integers
    public sealed class SUB : Instruction<Args_R>
    {
        public SUB()
        {
            Opcode = 0x00;
            Funct = 0x22;
            Name = "sub";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs - rt";
            Description = "Subtract signed 32-bit integers with overflow trap";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int rs = CpuHelpers.GetReg32(cpu, args.Rs);
            int rt = CpuHelpers.GetReg32(cpu, args.Rt);
            
            try
            {
                int result = checked(rs - rt);
                CpuHelpers.SetReg32(cpu, args.Rd, result);
            }
            catch (OverflowException)
            {
                // TODO: Trigger overflow exception
                throw;
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | args[2] << 11 | (Funct ?? 0x22));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));
            
            if (cpu != null)
            {
                int rsVal = CpuHelpers.GetReg32(cpu, args.Rs);
                int rtVal = CpuHelpers.GetReg32(cpu, args.Rt);
                return $"{eq} => {rsVal} - {rtVal} = {rsVal - rtVal}";
            }
            return eq;
        }
    }

    // SUBU - Subtract unsigned 32-bit integers
    public sealed class SUBU : Instruction<Args_R>
    {
        public SUBU()
        {
            Opcode = 0x00;
            Funct = 0x23;
            Name = "subu";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs - rt";
            Description = "Subtract unsigned 32-bit integers without overflow trap";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            uint rs = (uint)CpuHelpers.GetReg32(cpu, args.Rs);
            uint rt = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
            uint result = rs - rt;
            CpuHelpers.SetReg32(cpu, args.Rd, (int)result);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | args[2] << 11 | (Funct ?? 0x23));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));
            
            if (cpu != null)
            {
                uint rsVal = (uint)CpuHelpers.GetReg32(cpu, args.Rs);
                uint rtVal = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
                return $"{eq} => 0x{rsVal:X8} - 0x{rtVal:X8} = 0x{rsVal - rtVal:X8}";
            }
            return eq;
        }
    }

    // DSUB - Subtract signed 64-bit integers
    public sealed class DSUB : Instruction<Args_R>
    {
        public DSUB()
        {
            Opcode = 0x00;
            Funct = 0x2E;
            Name = "dsub";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs - rt";
            Description = "Subtract signed 64-bit integers with overflow trap";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];
            long rt = (long)cpu.RegsLo[args.Rt];
            
            try
            {
                long result = checked(rs - rt);
                cpu.RegsLo[args.Rd] = (ulong)result;
                cpu.RegsHi[args.Rd] = 0;
            }
            catch (OverflowException)
            {
                // TODO: Trigger overflow exception
                throw;
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | args[2] << 11 | (Funct ?? 0x2E));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));
            
            if (cpu != null)
            {
                long rsVal = (long)cpu.RegsLo[args.Rs];
                long rtVal = (long)cpu.RegsLo[args.Rt];
                return $"{eq} => {rsVal} - {rtVal} = {rsVal - rtVal}";
            }
            return eq;
        }
    }

    // DSUBU - Subtract unsigned 64-bit integers
    public sealed class DSUBU : Instruction<Args_R>
    {
        public DSUBU()
        {
            Opcode = 0x00;
            Funct = 0x2F;
            Name = "dsubu";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs - rt";
            Description = "Subtract unsigned 64-bit integers without overflow trap";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];
            ulong result = rs - rt;
            cpu.RegsLo[args.Rd] = result;
            cpu.RegsHi[args.Rd] = 0;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | args[2] << 11 | (Funct ?? 0x2F));
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
                return $"{eq} => 0x{rsVal:X16} - 0x{rtVal:X16} = 0x{rsVal - rtVal:X16}";
            }
            return eq;
        }
    }

    // ADDI - Add immediate (signed, overflow trap)
    public sealed class ADDI : Instruction<Args_I>
    {
        public ADDI()
        {
            Opcode = 0x08;
            Funct = null;
            Name = "addi";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "rs", "imm"];
            Equation = "rt = rs + SignExt(imm)";
            Description = "Add signed immediate with overflow trap";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int rs = CpuHelpers.GetReg32(cpu, args.Rs);
            int imm = CpuHelpers.SignExtend16(args.Imm);
            
            try
            {
                int result = checked(rs + imm);
                CpuHelpers.SetReg32(cpu, args.Rt, result);
            }
            catch (OverflowException)
            {
                // TODO: Trigger overflow exception
                throw;
            }
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
            int imm = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("SignExt(imm)", $"{imm}");
            
            if (cpu != null)
            {
                int rsVal = CpuHelpers.GetReg32(cpu, args.Rs);
                return $"{eq} => {rsVal} + {imm} = {rsVal + imm}";
            }
            return eq;
        }
    }

    // ADDIU - Add immediate unsigned
    public sealed class ADDIU : Instruction<Args_I>
    {
        public ADDIU()
        {
            Opcode = 0x09;
            Funct = null;
            Name = "addiu";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "rs", "imm"];
            Equation = "rt = rs + SignExt(imm)";
            Description = "Add unsigned immediate without overflow trap";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int rs = CpuHelpers.GetReg32(cpu, args.Rs);
            int imm = CpuHelpers.SignExtend16(args.Imm);
            int result = rs + imm;
            CpuHelpers.SetReg32(cpu, args.Rt, result);
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
            return $"I-Type: {Disasm(args, pc)} [Unsigned add immediate, no overflow]";
        }
    }

    // DADDI - Add immediate 64-bit (overflow trap)
    public sealed class DADDI : Instruction<Args_I>
    {
        public DADDI()
        {
            Opcode = 0x18;
            Funct = null;
            Name = "daddi";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "rs", "imm"];
            Equation = "rt = rs + SignExt(imm)";
            Description = "Add immediate 64-bit with overflow trap";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];
            long imm = CpuHelpers.SignExtend16(args.Imm);
            
            try
            {
                long result = checked(rs + imm);
                cpu.RegsLo[args.Rt] = (ulong)result;
                cpu.RegsHi[args.Rt] = 0;
            }
            catch (OverflowException)
            {
                // TODO: Trigger overflow exception
                throw;
            }
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
            return $"I-Type: {Disasm(args, pc)} [64-bit signed add immediate with overflow trap]";
        }
    }

    // DADDIU - Add immediate 64-bit unsigned
    public sealed class DADDIU : Instruction<Args_I>
    {
        public DADDIU()
        {
            Opcode = 0x19;
            Funct = null;
            Name = "daddiu";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "rs", "imm"];
            Equation = "rt = rs + SignExt(imm)";
            Description = "Add immediate 64-bit unsigned without overflow trap";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];
            long imm = CpuHelpers.SignExtend16(args.Imm);
            long result = rs + imm;
            cpu.RegsLo[args.Rt] = (ulong)result;
            cpu.RegsHi[args.Rt] = 0;
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
            return $"I-Type: {Disasm(args, pc)} [64-bit unsigned add immediate, no overflow]";
        }
    }

    // MULT - Multiply signed 32-bit
    public sealed class MULT : Instruction<Args_R>
    {
        public MULT()
        {
            Opcode = 0x00;
            Funct = 0x18;
            Name = "mult";
            ArgType = InstrArgType.R;
            ArgNames = ["rs", "rt"];
            Equation = "HI:LO = rs * rt";
            Description = "Multiply signed 32-bit, result in HI:LO";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int rs = CpuHelpers.GetReg32(cpu, args.Rs);
            int rt = CpuHelpers.GetReg32(cpu, args.Rt);
            long result = rs * (long)rt;
            
            cpu.LO = (uint)(result & 0xFFFFFFFF);
            cpu.HI = (uint)(result >> 32 & 0xFFFFFFFF);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | (Funct ?? 0x18));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return $"R-Type: {Disasm(args, pc)} [32-bit signed multiply, result in HI:LO]";
        }
    }

    // MULTU - Multiply unsigned 32-bit
    public sealed class MULTU : Instruction<Args_R>
    {
        public MULTU()
        {
            Opcode = 0x00;
            Funct = 0x19;
            Name = "multu";
            ArgType = InstrArgType.R;
            ArgNames = ["rs", "rt"];
            Equation = "HI:LO = rs * rt";
            Description = "Multiply unsigned 32-bit, result in HI:LO";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            uint rs = (uint)CpuHelpers.GetReg32(cpu, args.Rs);
            uint rt = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
            ulong result = rs * (ulong)rt;
            
            cpu.LO = (uint)(result & 0xFFFFFFFF);
            cpu.HI = (uint)(result >> 32 & 0xFFFFFFFF);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | (Funct ?? 0x19));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return $"R-Type: {Disasm(args, pc)} [32-bit unsigned multiply, result in HI:LO]";
        }
    }

    // DMULT - Multiply signed 64-bit
    public sealed class DMULT : Instruction<Args_R>
    {
        public DMULT()
        {
            Opcode = 0x00;
            Funct = 0x1C;
            Name = "dmult";
            ArgType = InstrArgType.R;
            ArgNames = ["rs", "rt"];
            Equation = "HI:LO = rs * rt";
            Description = "Multiply signed 64-bit, result in HI:LO";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];
            long rt = (long)cpu.RegsLo[args.Rt];
            
            // 64-bit x 64-bit = 128-bit result
            ulong absRs = (ulong)Math.Abs(rs);
            ulong absRt = (ulong)Math.Abs(rt);
            bool negative = rs < 0 ^ rt < 0;
            
            // Multiply as unsigned
            ulong low = absRs * absRt;
            ulong high = Math.BigMul(absRs, absRt, out _);
            
            if (negative)
            {
                // Two's complement negation for 128-bit
                low = ~low + 1;
                high = ~high;
                if (low == 0) high++;
            }
            
            cpu.LO = low;
            cpu.HI = high;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | (Funct ?? 0x1C));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return $"R-Type: {Disasm(args, pc)} [64-bit signed multiply, result in HI:LO]";
        }
    }

    // DMULTU - Multiply unsigned 64-bit
    public sealed class DMULTU : Instruction<Args_R>
    {
        public DMULTU()
        {
            Opcode = 0x00;
            Funct = 0x1D;
            Name = "dmultu";
            ArgType = InstrArgType.R;
            ArgNames = ["rs", "rt"];
            Equation = "HI:LO = rs * rt";
            Description = "Multiply unsigned 64-bit, result in HI:LO";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];
            
            ulong high = Math.BigMul(rs, rt, out ulong low);
            
            cpu.LO = low;
            cpu.HI = high;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | (Funct ?? 0x1D));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return $"R-Type: {Disasm(args, pc)} [64-bit unsigned multiply, result in HI:LO]";
        }
    }

    // DIV - Divide signed 32-bit
    public sealed class DIV : Instruction<Args_R>
    {
        public DIV()
        {
            Opcode = 0x00;
            Funct = 0x1A;
            Name = "div";
            ArgType = InstrArgType.R;
            ArgNames = ["rs", "rt"];
            Equation = "LO = rs / rt; HI = rs % rt";
            Description = "Divide signed 32-bit, quotient in LO, remainder in HI";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int rs = CpuHelpers.GetReg32(cpu, args.Rs);
            int rt = CpuHelpers.GetReg32(cpu, args.Rt);
            
            if (rt == 0)
            {
                // Division by zero - undefined behavior on MIPS
                cpu.LO = rs >= 0 ? 0xFFFFFFFF : 1;
                cpu.HI = (uint)rs;
            }
            else if (rs == int.MinValue && rt == -1)
            {
                // Overflow case
                cpu.LO = unchecked((uint)int.MinValue);
                cpu.HI = 0;
            }
            else
            {
                int quotient = rs / rt;
                int remainder = rs % rt;
                cpu.LO = (uint)quotient;
                cpu.HI = (uint)remainder;
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | (Funct ?? 0x1A));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return $"R-Type: {Disasm(args, pc)} [32-bit signed divide, quotient in LO, remainder in HI]";
        }
    }

    // DIVU - Divide unsigned 32-bit
    public sealed class DIVU : Instruction<Args_R>
    {
        public DIVU()
        {
            Opcode = 0x00;
            Funct = 0x1B;
            Name = "divu";
            ArgType = InstrArgType.R;
            ArgNames = ["rs", "rt"];
            Equation = "LO = rs / rt; HI = rs % rt";
            Description = "Divide unsigned 32-bit, quotient in LO, remainder in HI";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            uint rs = (uint)CpuHelpers.GetReg32(cpu, args.Rs);
            uint rt = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
            
            if (rt == 0)
            {
                // Division by zero
                cpu.LO = 0xFFFFFFFF;
                cpu.HI = rs;
            }
            else
            {
                uint quotient = rs / rt;
                uint remainder = rs % rt;
                cpu.LO = quotient;
                cpu.HI = remainder;
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | (Funct ?? 0x1B));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return $"R-Type: {Disasm(args, pc)} [32-bit unsigned divide, quotient in LO, remainder in HI]";
        }
    }

    // DDIV - Divide signed 64-bit
    public sealed class DDIV : Instruction<Args_R>
    {
        public DDIV()
        {
            Opcode = 0x00;
            Funct = 0x1E;
            Name = "ddiv";
            ArgType = InstrArgType.R;
            ArgNames = ["rs", "rt"];
            Equation = "LO = rs / rt; HI = rs % rt";
            Description = "Divide signed 64-bit, quotient in LO, remainder in HI";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];
            long rt = (long)cpu.RegsLo[args.Rt];
            
            if (rt == 0)
            {
                // Division by zero
                cpu.LO = rs >= 0 ? 0xFFFFFFFFFFFFFFFF : 1;
                cpu.HI = (ulong)rs;
            }
            else if (rs == long.MinValue && rt == -1)
            {
                // Overflow case
                cpu.LO = unchecked((ulong)long.MinValue);
                cpu.HI = 0;
            }
            else
            {
                long quotient = rs / rt;
                long remainder = rs % rt;
                cpu.LO = (ulong)quotient;
                cpu.HI = (ulong)remainder;
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | (Funct ?? 0x1E));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return $"R-Type: {Disasm(args, pc)} [64-bit signed divide, quotient in LO, remainder in HI]";
        }
    }

    // DDIVU - Divide unsigned 64-bit
    public sealed class DDIVU : Instruction<Args_R>
    {
        public DDIVU()
        {
            Opcode = 0x00;
            Funct = 0x1F;
            Name = "ddivu";
            ArgType = InstrArgType.R;
            ArgNames = ["rs", "rt"];
            Equation = "LO = rs / rt; HI = rs % rt";
            Description = "Divide unsigned 64-bit, quotient in LO, remainder in HI";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];
            
            if (rt == 0)
            {
                // Division by zero
                cpu.LO = 0xFFFFFFFFFFFFFFFF;
                cpu.HI = rs;
            }
            else
            {
                ulong quotient = rs / rt;
                ulong remainder = rs % rt;
                cpu.LO = quotient;
                cpu.HI = remainder;
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | (Funct ?? 0x1F));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return $"R-Type: {Disasm(args, pc)} [64-bit unsigned divide, quotient in LO, remainder in HI]";
        }
    }

    // MFHI - Move from HI register
    public sealed class MFHI : Instruction<Args_R>
    {
        public MFHI()
        {
            Opcode = 0x00;
            Funct = 0x10;
            Name = "mfhi";
            ArgType = InstrArgType.R;
            ArgNames = ["rd"];
            Equation = "rd = HI";
            Description = "Move from HI register";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            cpu.RegsLo[args.Rd] = cpu.HI;
            cpu.RegsHi[args.Rd] = 0;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[2] << 11 | (Funct ?? 0x10));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return $"R-Type: {Disasm(args, pc)} [Move from HI register]";
        }
    }

    // MFLO - Move from LO register
    public sealed class MFLO : Instruction<Args_R>
    {
        public MFLO()
        {
            Opcode = 0x00;
            Funct = 0x12;
            Name = "mflo";
            ArgType = InstrArgType.R;
            ArgNames = ["rd"];
            Equation = "rd = LO";
            Description = "Move from LO register";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            cpu.RegsLo[args.Rd] = cpu.LO;
            cpu.RegsHi[args.Rd] = 0;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[2] << 11 | (Funct ?? 0x12));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return $"R-Type: {Disasm(args, pc)} [Move from LO register]";
        }
    }

    // MTHI - Move to HI register
    public sealed class MTHI : Instruction<Args_R>
    {
        public MTHI()
        {
            Opcode = 0x00;
            Funct = 0x11;
            Name = "mthi";
            ArgType = InstrArgType.R;
            ArgNames = ["rs"];
            Equation = "HI = rs";
            Description = "Move to HI register";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            cpu.HI = cpu.RegsLo[args.Rs];
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | (Funct ?? 0x11));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return $"R-Type: {Disasm(args, pc)} [Move to HI register]";
        }
    }

    // MTLO - Move to LO register
    public sealed class MTLO : Instruction<Args_R>
    {
        public MTLO()
        {
            Opcode = 0x00;
            Funct = 0x13;
            Name = "mtlo";
            ArgType = InstrArgType.R;
            ArgNames = ["rs"];
            Equation = "LO = rs";
            Description = "Move to LO register";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            cpu.LO = cpu.RegsLo[args.Rs];
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | (Funct ?? 0x13));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return $"R-Type: {Disasm(args, pc)} [Move to LO register]";
        }
    }
}