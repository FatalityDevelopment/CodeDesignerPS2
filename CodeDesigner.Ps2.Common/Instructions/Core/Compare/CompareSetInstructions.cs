using CodeDesigner.Ps2.Common.Cpu.InstrArgs;
using CodeDesigner.Ps2.Common.CPU;
using System;

/*
| Instruction | Description |
|-------------|-------------|
| SLT | Set if less than (signed) |
| SLTU | Set if less than (unsigned) |
| SLTI | Set if less than immediate (signed) |
| SLTIU | Set if less than immediate (unsigned) |
 */
namespace CodeDesigner.Ps2.Common.Cpu.Instructions.EE
{
    // SLT - Set if less than (signed)
    public sealed class SLT : Instruction<Args_R>
    {
        public SLT()
        {
            Opcode = 0x00;
            Funct = 0x2A;
            Name = "slt";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = (rs < rt) ? 1 : 0";
            Description = "Set if less than (signed) - sets rd to 1 if rs < rt, else 0";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];
            long rt = (long)cpu.RegsLo[args.Rt];
            
            cpu.RegsLo[args.Rd] = (rs < rt) ? 1UL : 0UL;
            cpu.RegsHi[args.Rd] = 0;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | args[2] << 11 | (Funct ?? 0x2A));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu == null) return string.Empty;
            long rsVal = (long)cpu.RegsLo[args.Rs];
            long rtVal = (long)cpu.RegsLo[args.Rt];
            int result = rsVal < rtVal ? 1 : 0;
            return $"{CpuHelpers.RegName(args.Rd)} = {result}  ({CpuHelpers.RegName(args.Rs)} < {CpuHelpers.RegName(args.Rt)})";
        }
    }

    // SLTU - Set if less than (unsigned)
    public sealed class SLTU : Instruction<Args_R>
    {
        public SLTU()
        {
            Opcode = 0x00;
            Funct = 0x2B;
            Name = "sltu";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = (rs < rt) ? 1 : 0 (unsigned)";
            Description = "Set if less than (unsigned) - sets rd to 1 if rs < rt (unsigned), else 0";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];
            
            cpu.RegsLo[args.Rd] = (rs < rt) ? 1UL : 0UL;
            cpu.RegsHi[args.Rd] = 0;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[1] << 16 | args[2] << 11 | (Funct ?? 0x2B));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu == null) return string.Empty;
            ulong rsVal = cpu.RegsLo[args.Rs];
            ulong rtVal = cpu.RegsLo[args.Rt];
            int result = rsVal < rtVal ? 1 : 0;
            return $"{CpuHelpers.RegName(args.Rd)} = {result}  ({CpuHelpers.RegName(args.Rs)} < {CpuHelpers.RegName(args.Rt)})";
        }
    }

    // SLTI - Set if less than immediate (signed)
    public sealed class SLTI : Instruction<Args_I>
    {
        public SLTI()
        {
            Opcode = 0x0A;
            Funct = null;
            Name = "slti";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "rs", "imm"];
            Equation = "rt = (rs < SignExt(imm)) ? 1 : 0";
            Description = "Set if less than immediate (signed) - sets rt to 1 if rs < imm, else 0";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];
            long imm = CpuHelpers.SignExtend16(args.Imm);
            
            cpu.RegsLo[args.Rt] = (rs < imm) ? 1UL : 0UL;
            cpu.RegsHi[args.Rt] = 0;
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.FormatSignedImmediate(args.Imm)}";
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
            int result = rsVal < imm ? 1 : 0;
            return $"{CpuHelpers.RegName(args.Rt)} = {result}  ({CpuHelpers.RegName(args.Rs)} < {imm})";
        }
    }

    // SLTIU - Set if less than immediate (unsigned)
    public sealed class SLTIU : Instruction<Args_I>
    {
        public SLTIU()
        {
            Opcode = 0x0B;
            Funct = null;
            Name = "sltiu";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "rs", "imm"];
            Equation = "rt = (rs < SignExt(imm)) ? 1 : 0 (unsigned)";
            Description = "Set if less than immediate (unsigned) - sets rt to 1 if rs < imm (unsigned), else 0";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            // Sign-extend immediate, then treat as unsigned for comparison
            ulong imm = (ulong)(long)CpuHelpers.SignExtend16(args.Imm);
            
            cpu.RegsLo[args.Rt] = (rs < imm) ? 1UL : 0UL;
            cpu.RegsHi[args.Rt] = 0;
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.FormatSignedImmediate(args.Imm)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu == null) return string.Empty;
            ulong rsVal = cpu.RegsLo[args.Rs];
            ulong imm = (ulong)(long)CpuHelpers.SignExtend16(args.Imm);
            int result = rsVal < imm ? 1 : 0;
            return $"{CpuHelpers.RegName(args.Rt)} = {result}  ({CpuHelpers.RegName(args.Rs)} < 0x{imm:X8})";
        }
    }
}
