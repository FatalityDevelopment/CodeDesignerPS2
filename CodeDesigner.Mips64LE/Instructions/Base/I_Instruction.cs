using CodeDesigner.Mips64LE.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.CPU;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE.Cpu.Instructions
{
    public sealed class IInstruction : Instruction<IArgs>
    {
        public override void Exec(CpuState cpu, IArgs args)
        {
            // Example: ADDI rt, rs, imm
            long result = (long)cpu.RegsLo[args.Rs] + args.Imm;
            CpuHelpers.SetReg32(cpu, args.Rt, (int)result);
        }

        public override string Disasm(IArgs args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {CpuHelpers.RegName(args.Rs)}, 0x{args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            // args: rs, rt, imm
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(IArgs args, CpuState? cpu = null, uint? pc = null)
        {
            return $"I-Type: {Disasm(args, pc)}";
        }
    }
}
