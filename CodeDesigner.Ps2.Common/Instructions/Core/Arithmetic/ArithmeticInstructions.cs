using CodeDesigner.Ps2.Common.Cpu.InstrArgs;
using CodeDesigner.Ps2.Common.CPU;
using System;
using System.Numerics;
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
namespace CodeDesigner.Ps2.Common.Cpu.Instructions.EE
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
            // PCSX2 R5900OpcodeImpl.cpp ADD: no overflow trap (matches EE real-world behavior;
            // no PS2 software relies on ADD overflow exceptions).
            int result = rs + rt;
            CpuHelpers.SetReg32(cpu, args.Rd, result);
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
            if (cpu == null) return string.Empty;
            int rsVal = CpuHelpers.GetReg32(cpu, args.Rs);
            int rtVal = CpuHelpers.GetReg32(cpu, args.Rt);
            return $"{CpuHelpers.RegName(args.Rd)} = 0x{(uint)(rsVal + rtVal):X8}";
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
            if (cpu == null) return string.Empty;
            uint rsVal = (uint)CpuHelpers.GetReg32(cpu, args.Rs);
            uint rtVal = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
            return $"{CpuHelpers.RegName(args.Rd)} = 0x{rsVal + rtVal:X8}";
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
            // PCSX2: no overflow trap.
            long result = rs + rt;
            cpu.RegsLo[args.Rd] = (ulong)result;
            cpu.RegsHi[args.Rd] = result < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
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
            if (cpu == null) return string.Empty;
            long rsVal = (long)cpu.RegsLo[args.Rs];
            long rtVal = (long)cpu.RegsLo[args.Rt];
            return $"{CpuHelpers.RegName(args.Rd)} = 0x{(ulong)(rsVal + rtVal):X16}";
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
            cpu.RegsHi[args.Rd] = ((long)result < 0) ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
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
            if (cpu == null) return string.Empty;
            ulong rsVal = cpu.RegsLo[args.Rs];
            ulong rtVal = cpu.RegsLo[args.Rt];
            return $"{CpuHelpers.RegName(args.Rd)} = 0x{rsVal + rtVal:X16}";
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
            // PCSX2: no overflow trap.
            int result = rs - rt;
            CpuHelpers.SetReg32(cpu, args.Rd, result);
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
            if (cpu == null) return string.Empty;
            int rsVal = CpuHelpers.GetReg32(cpu, args.Rs);
            int rtVal = CpuHelpers.GetReg32(cpu, args.Rt);
            return $"{CpuHelpers.RegName(args.Rd)} = 0x{(uint)(rsVal - rtVal):X8}";
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
            if (cpu == null) return string.Empty;
            uint rsVal = (uint)CpuHelpers.GetReg32(cpu, args.Rs);
            uint rtVal = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
            return $"{CpuHelpers.RegName(args.Rd)} = 0x{rsVal - rtVal:X8}";
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
            // PCSX2: no overflow trap.
            long result = rs - rt;
            cpu.RegsLo[args.Rd] = (ulong)result;
            cpu.RegsHi[args.Rd] = result < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
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
            if (cpu == null) return string.Empty;
            long rsVal = (long)cpu.RegsLo[args.Rs];
            long rtVal = (long)cpu.RegsLo[args.Rt];
            return $"{CpuHelpers.RegName(args.Rd)} = 0x{(ulong)(rsVal - rtVal):X16}";
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
            cpu.RegsHi[args.Rd] = ((long)result < 0) ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
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
            if (cpu == null) return string.Empty;
            ulong rsVal = cpu.RegsLo[args.Rs];
            ulong rtVal = cpu.RegsLo[args.Rt];
            return $"{CpuHelpers.RegName(args.Rd)} = 0x{rsVal - rtVal:X16}";
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
            // PCSX2 R5900OpcodeImpl.cpp ADDI: no overflow trap.
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
            if (cpu == null) return string.Empty;
            int rsVal = CpuHelpers.GetReg32(cpu, args.Rs);
            int imm = CpuHelpers.SignExtend16(args.Imm);
            return $"{CpuHelpers.RegName(args.Rt)} = 0x{(uint)(rsVal + imm):X8}";
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
            if (cpu == null) return string.Empty;
            int rsVal = CpuHelpers.GetReg32(cpu, args.Rs);
            int imm = CpuHelpers.SignExtend16(args.Imm);
            uint result = (uint)(rsVal + imm);

            // Detect stack frame allocation/deallocation: ADDIU $sp, $sp, ±N
            if (args.Rt == 29 && args.Rs == 29)
            {
                if (imm < 0)
                    return $"allocate {-imm}-byte stack frame → $sp = 0x{result:X8}";
                if (imm > 0)
                    return $"free {imm}-byte stack frame → $sp = 0x{result:X8}";
            }

            return $"{CpuHelpers.RegName(args.Rt)} = 0x{result:X8}";
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
            // PCSX2: no overflow trap.
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
            if (cpu == null) return string.Empty;
            long rsVal = (long)cpu.RegsLo[args.Rs];
            long imm = CpuHelpers.SignExtend16(args.Imm);
            return $"{CpuHelpers.RegName(args.Rt)} = 0x{(ulong)(rsVal + imm):X16}";
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
            if (cpu == null) return string.Empty;
            long rsVal = (long)cpu.RegsLo[args.Rs];
            long imm = CpuHelpers.SignExtend16(args.Imm);
            return $"{CpuHelpers.RegName(args.Rt)} = 0x{(ulong)(rsVal + imm):X16}";
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
            
            // EE sign-extends each 32-bit half to 64 bits when writing to HI/LO
            cpu.LO = (ulong)(long)(int)(result & 0xFFFFFFFF);
            cpu.HI = (ulong)(long)(int)(result >> 32);
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
            if (cpu == null) return string.Empty;
            int rs = CpuHelpers.GetReg32(cpu, args.Rs);
            int rt = CpuHelpers.GetReg32(cpu, args.Rt);
            long product = (long)rs * rt;
            uint lo = (uint)(product & 0xFFFFFFFF);
            uint hi = (uint)(product >> 32);
            return $"HI=0x{hi:X8}, LO=0x{lo:X8}";
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
            
            // EE sign-extends each 32-bit half to 64 bits when writing to HI/LO
            cpu.LO = (ulong)(long)(int)(result & 0xFFFFFFFF);
            cpu.HI = (ulong)(long)(int)(result >> 32);
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
            if (cpu == null) return string.Empty;
            uint rs = (uint)CpuHelpers.GetReg32(cpu, args.Rs);
            uint rt = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
            ulong product = (ulong)rs * rt;
            return $"HI=0x{(uint)(product >> 32):X8}, LO=0x{(uint)(product & 0xFFFFFFFF):X8}";
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
            
            // Use BigInteger to handle the long.MinValue case safely
            var product = (System.Numerics.BigInteger)rs * rt;
            cpu.LO = (ulong)(product & 0xFFFFFFFFFFFFFFFF);
            cpu.HI = (ulong)((product >> 64) & 0xFFFFFFFFFFFFFFFF);
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
            if (cpu == null) return string.Empty;
            long rs = (long)cpu.RegsLo[args.Rs];
            long rt = (long)cpu.RegsLo[args.Rt];
            var product = (System.Numerics.BigInteger)rs * rt;
            ulong lo = (ulong)(product & 0xFFFFFFFFFFFFFFFF);
            ulong hi = (ulong)((product >> 64) & 0xFFFFFFFFFFFFFFFF);
            return $"HI=0x{hi:X16}, LO=0x{lo:X16}";
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
            if (cpu == null) return string.Empty;
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];
            ulong high = Math.BigMul(rs, rt, out ulong low);
            return $"HI=0x{high:X16}, LO=0x{low:X16}";
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
                // EE: LO = 0xFFFFFFFF (-1) sign-extended if rs>=0, else 1; HI = sign-extended rs
                cpu.LO = rs >= 0 ? 0xFFFFFFFFFFFFFFFFUL : 1UL;
                cpu.HI = (ulong)(long)rs;
            }
            else if (rs == int.MinValue && rt == -1)
            {
                // Overflow case - EE sign-extends result
                cpu.LO = unchecked((ulong)(long)int.MinValue);
                cpu.HI = 0;
            }
            else
            {
                int quotient = rs / rt;
                int remainder = rs % rt;
                // EE sign-extends 32-bit results to 64-bit in HI/LO
                cpu.LO = (ulong)(long)quotient;
                cpu.HI = (ulong)(long)remainder;
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
            if (cpu == null) return string.Empty;
            int rs = CpuHelpers.GetReg32(cpu, args.Rs);
            int rt = CpuHelpers.GetReg32(cpu, args.Rt);
            if (rt == 0) return "LO=?, HI=?  (div by 0)";
            return $"LO=0x{(uint)(rs / rt):X8}, HI=0x{(uint)(rs % rt):X8}";
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
                // Division by zero - EE sign-extends 32-bit results
                cpu.LO = 0xFFFFFFFFFFFFFFFFUL; // -1 sign-extended
                cpu.HI = (ulong)(long)(int)rs;
            }
            else
            {
                uint quotient = rs / rt;
                uint remainder = rs % rt;
                // EE sign-extends 32-bit results to 64-bit in HI/LO
                cpu.LO = (ulong)(long)(int)quotient;
                cpu.HI = (ulong)(long)(int)remainder;
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
            if (cpu == null) return string.Empty;
            uint rs = (uint)CpuHelpers.GetReg32(cpu, args.Rs);
            uint rt = (uint)CpuHelpers.GetReg32(cpu, args.Rt);
            if (rt == 0) return "LO=?, HI=?  (div by 0)";
            return $"LO=0x{rs / rt:X8}, HI=0x{rs % rt:X8}";
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
            if (cpu == null) return string.Empty;
            long rs = (long)cpu.RegsLo[args.Rs];
            long rt = (long)cpu.RegsLo[args.Rt];
            if (rt == 0) return "LO=?, HI=?  (div by 0)";
            return $"LO=0x{(ulong)(rs / rt):X16}, HI=0x{(ulong)(rs % rt):X16}";
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
            if (cpu == null) return string.Empty;
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];
            if (rt == 0) return "LO=?, HI=?  (div by 0)";
            return $"LO=0x{rs / rt:X16}, HI=0x{rs % rt:X16}";
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
            cpu.RegsHi[args.Rd] = ((long)cpu.HI < 0) ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
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
            if (cpu == null) return string.Empty;
            return $"{CpuHelpers.RegName(args.Rd)} = HI=0x{(uint)cpu.HI:X8}";
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
            cpu.RegsHi[args.Rd] = ((long)cpu.LO < 0) ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
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
            if (cpu == null) return string.Empty;
            return $"{CpuHelpers.RegName(args.Rd)} = LO=0x{(uint)cpu.LO:X8}";
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
            if (cpu == null) return string.Empty;
            return $"HI = {CpuHelpers.RegName(args.Rs)}=0x{(uint)cpu.RegsLo[args.Rs]:X8}";
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
            if (cpu == null) return string.Empty;
            return $"LO = {CpuHelpers.RegName(args.Rs)}=0x{(uint)cpu.RegsLo[args.Rs]:X8}";
        }
    }
}