using CodeDesigner.Mips64LE.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.CPU;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE.Cpu.Instructions
{
    public sealed class CopInstruction : Instruction<CopArgs>
    {
        public int Coprocessor { get; init; } // 0 = COP0, 1 = COP1, etc.

        public override void Exec(CpuState cpu, CopArgs args)
        {
            // Example: MFC0 rt, rd
            if (Coprocessor == 0)
            {
                CpuHelpers.SetReg32(cpu, args.Rt, (int)cpu.COP0.ReadRegister32(args.Rd));
            }
            else
            {
                throw new NotImplementedException($"COP{Coprocessor} not implemented");
            }
        }

        public override string Disasm(CopArgs args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            // args: rt, rd
            return (uint)(Opcode << 26 | args[0] << 16 | args[1] << 11 | (Funct ?? 0));
        }

        public override string Analyze(CopArgs args, CpuState? cpu = null, uint? pc = null)
        {
            return $"COP{Coprocessor}: {Disasm(args, pc)}";
        }
    }
}
