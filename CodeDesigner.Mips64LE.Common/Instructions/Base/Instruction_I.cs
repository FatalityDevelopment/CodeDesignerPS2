using CodeDesigner.Mips64LE.Common.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.Common.CPU;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE.Common.Cpu.Instructions
{
    public sealed class Instruction_I : Instruction<Args_I>
    {
        public override void Exec(CpuState cpu, Args_I args)
        {
            // Example: ADDI rt, rs, imm
            long result = (long)cpu.RegsLo[args.Rs] + args.Imm;
            CpuHelpers.SetReg32(cpu, args.Rt, (int)result);
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {CpuHelpers.RegName(args.Rs)}, ${args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            // args: rs, rt, imm
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            return $"I-Type: {Disasm(args, pc)}";
        }
    }
}
