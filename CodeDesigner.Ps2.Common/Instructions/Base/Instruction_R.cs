using CodeDesigner.Ps2.Common.Cpu.InstrArgs;
using CodeDesigner.Ps2.Common.CPU;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Ps2.Common.Cpu.Instructions
{
    public sealed class Instruction_R : Instruction<Args_R>
    {
        public override void Exec(CpuState cpu, Args_R args)
        {
            // Example: ADD rd, rs, rt
            long result = (long)cpu.RegsLo[args.Rs] + (long)cpu.RegsLo[args.Rt];
            CpuHelpers.SetReg32(cpu, args.Rd, (int)result);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            // Example: encode opcode/funct for R-type
            // args[0] = rs, args[1] = rt, args[2] = rd, args[3] = shamt
            return (uint)(args[0] << 21 | args[1] << 16 | args[2] << 11 | args[3] << 6 | (Funct ?? 0));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return $"R-Type: {Disasm(args, pc)}";
        }
    }
}
