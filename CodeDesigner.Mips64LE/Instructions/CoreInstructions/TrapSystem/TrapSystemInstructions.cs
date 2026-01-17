using CodeDesigner.Mips64LE.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.CPU;
using System;

/*
| Instruction | Description |
|-------------|-------------|
| SYSCALL | System call exception |
| BREAK | Breakpoint exception |
| TGE | Trap if greater or equal |
| TGEU | Trap if greater or equal unsigned |
| TLT | Trap if less than |
| TLTU | Trap if less than unsigned |
| TEQ | Trap if equal |
| TNE | Trap if not equal |
 */
namespace CodeDesigner.Mips64LE.Cpu.Instructions.EE
{
    // SYSCALL - System call exception
    public sealed class SYSCALL : Instruction<Args_R>
    {
        public SYSCALL()
        {
            Opcode = 0x00;
            Funct = 0x0C;
            Name = "syscall";
            ArgType = InstrArgType.R;
            ArgNames = ["code"];
            Equation = "exception(SystemCall)";
            Description = "System call - triggers a system call exception";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Trigger system call exception
            // In a full implementation, this would:
            // 1. Save current PC to EPC (Exception Program Counter)
            // 2. Set Cause register with exception code
            // 3. Jump to exception handler
            // For now, throw an exception to indicate system call
            throw new SystemCallException($"SYSCALL with code 0x{args.Rs:X}");
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            // SYSCALL has an optional code field in bits 25-6
            if (args.Rs != 0 || args.Rt != 0 || args.Rd != 0 || args.Shamt != 0)
            {
                uint code = ((uint)args.Rs << 6) | ((uint)args.Rt << 11) | ((uint)args.Rd << 16) | ((uint)args.Shamt << 21);
                return $"{Name} 0x{code:X}";
            }
            return Name;
        }

        public override uint Asm(int[] args)
        {
            uint code = args.Length > 0 ? (uint)args[0] : 0;
            return (uint)((code << 6) | (Funct ?? 0x0C));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return "System call exception - transfers control to OS";
        }
    }

    // BREAK - Breakpoint exception
    public sealed class BREAK : Instruction<Args_R>
    {
        public BREAK()
        {
            Opcode = 0x00;
            Funct = 0x0D;
            Name = "break";
            ArgType = InstrArgType.R;
            ArgNames = ["code"];
            Equation = "exception(Breakpoint)";
            Description = "Breakpoint - triggers a breakpoint exception for debugging";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Trigger breakpoint exception
            // Similar to SYSCALL but for debugging purposes
            throw new BreakpointException($"BREAK with code 0x{args.Rs:X}");
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            // BREAK has an optional code field in bits 25-6
            if (args.Rs != 0 || args.Rt != 0 || args.Rd != 0 || args.Shamt != 0)
            {
                uint code = ((uint)args.Rs << 6) | ((uint)args.Rt << 11) | ((uint)args.Rd << 16) | ((uint)args.Shamt << 21);
                return $"{Name} 0x{code:X}";
            }
            return Name;
        }

        public override uint Asm(int[] args)
        {
            uint code = args.Length > 0 ? (uint)args[0] : 0;
            return (uint)((code << 6) | (Funct ?? 0x0D));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return "Breakpoint exception - for debugging purposes";
        }
    }

    // TGE - Trap if greater or equal
    public sealed class TGE : Instruction<Args_R>
    {
        public TGE()
        {
            Opcode = 0x00;
            Funct = 0x30;
            Name = "tge";
            ArgType = InstrArgType.R;
            ArgNames = ["rs", "rt"];
            Equation = "if (rs >= rt) trap()";
            Description = "Trap if greater or equal (signed) - triggers trap exception if rs >= rt";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];
            long rt = (long)cpu.RegsLo[args.Rt];
            
            if (rs >= rt)
            {
                throw new TrapException($"TGE: {rs} >= {rt}");
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | (Funct ?? 0x30));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));
            
            if (cpu != null)
            {
                long rsVal = (long)cpu.RegsLo[args.Rs];
                long rtVal = (long)cpu.RegsLo[args.Rt];
                bool willTrap = rsVal >= rtVal;
                return $"{eq} => {rsVal} >= {rtVal} = {willTrap} (trap: {willTrap})";
            }
            return eq;
        }
    }

    // TGEU - Trap if greater or equal unsigned
    public sealed class TGEU : Instruction<Args_R>
    {
        public TGEU()
        {
            Opcode = 0x00;
            Funct = 0x31;
            Name = "tgeu";
            ArgType = InstrArgType.R;
            ArgNames = ["rs", "rt"];
            Equation = "if (rs >= rt) trap() (unsigned)";
            Description = "Trap if greater or equal (unsigned) - triggers trap exception if rs >= rt";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];
            
            if (rs >= rt)
            {
                throw new TrapException($"TGEU: 0x{rs:X16} >= 0x{rt:X16}");
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | (Funct ?? 0x31));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));
            
            if (cpu != null)
            {
                ulong rsVal = cpu.RegsLo[args.Rs];
                ulong rtVal = cpu.RegsLo[args.Rt];
                bool willTrap = rsVal >= rtVal;
                return $"{eq} => 0x{rsVal:X16} >= 0x{rtVal:X16} = {willTrap} (trap: {willTrap})";
            }
            return eq;
        }
    }

    // TLT - Trap if less than
    public sealed class TLT : Instruction<Args_R>
    {
        public TLT()
        {
            Opcode = 0x00;
            Funct = 0x32;
            Name = "tlt";
            ArgType = InstrArgType.R;
            ArgNames = ["rs", "rt"];
            Equation = "if (rs < rt) trap()";
            Description = "Trap if less than (signed) - triggers trap exception if rs < rt";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];
            long rt = (long)cpu.RegsLo[args.Rt];
            
            if (rs < rt)
            {
                throw new TrapException($"TLT: {rs} < {rt}");
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | (Funct ?? 0x32));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));
            
            if (cpu != null)
            {
                long rsVal = (long)cpu.RegsLo[args.Rs];
                long rtVal = (long)cpu.RegsLo[args.Rt];
                bool willTrap = rsVal < rtVal;
                return $"{eq} => {rsVal} < {rtVal} = {willTrap} (trap: {willTrap})";
            }
            return eq;
        }
    }

    // TLTU - Trap if less than unsigned
    public sealed class TLTU : Instruction<Args_R>
    {
        public TLTU()
        {
            Opcode = 0x00;
            Funct = 0x33;
            Name = "tltu";
            ArgType = InstrArgType.R;
            ArgNames = ["rs", "rt"];
            Equation = "if (rs < rt) trap() (unsigned)";
            Description = "Trap if less than (unsigned) - triggers trap exception if rs < rt";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];
            
            if (rs < rt)
            {
                throw new TrapException($"TLTU: 0x{rs:X16} < 0x{rt:X16}");
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | (Funct ?? 0x33));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));
            
            if (cpu != null)
            {
                ulong rsVal = cpu.RegsLo[args.Rs];
                ulong rtVal = cpu.RegsLo[args.Rt];
                bool willTrap = rsVal < rtVal;
                return $"{eq} => 0x{rsVal:X16} < 0x{rtVal:X16} = {willTrap} (trap: {willTrap})";
            }
            return eq;
        }
    }

    // TEQ - Trap if equal
    public sealed class TEQ : Instruction<Args_R>
    {
        public TEQ()
        {
            Opcode = 0x00;
            Funct = 0x34;
            Name = "teq";
            ArgType = InstrArgType.R;
            ArgNames = ["rs", "rt"];
            Equation = "if (rs == rt) trap()";
            Description = "Trap if equal - triggers trap exception if rs == rt";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];
            
            if (rs == rt)
            {
                throw new TrapException($"TEQ: 0x{rs:X16} == 0x{rt:X16}");
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | (Funct ?? 0x34));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));
            
            if (cpu != null)
            {
                ulong rsVal = cpu.RegsLo[args.Rs];
                ulong rtVal = cpu.RegsLo[args.Rt];
                bool willTrap = rsVal == rtVal;
                return $"{eq} => 0x{rsVal:X16} == 0x{rtVal:X16} = {willTrap} (trap: {willTrap})";
            }
            return eq;
        }
    }

    // TNE - Trap if not equal
    public sealed class TNE : Instruction<Args_R>
    {
        public TNE()
        {
            Opcode = 0x00;
            Funct = 0x36;
            Name = "tne";
            ArgType = InstrArgType.R;
            ArgNames = ["rs", "rt"];
            Equation = "if (rs != rt) trap()";
            Description = "Trap if not equal - triggers trap exception if rs != rt";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];
            
            if (rs != rt)
            {
                throw new TrapException($"TNE: 0x{rs:X16} != 0x{rt:X16}");
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | (Funct ?? 0x36));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));
            
            if (cpu != null)
            {
                ulong rsVal = cpu.RegsLo[args.Rs];
                ulong rtVal = cpu.RegsLo[args.Rt];
                bool willTrap = rsVal != rtVal;
                return $"{eq} => 0x{rsVal:X16} != 0x{rtVal:X16} = {willTrap} (trap: {willTrap})";
            }
            return eq;
        }
    }
}
