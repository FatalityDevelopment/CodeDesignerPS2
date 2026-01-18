using CodeDesigner.Mips64LE.Common.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.Common.CPU;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE.Common.Cpu.Instructions
{
    public sealed class Instruction_J : Instruction<Args_J>
    {
        public override void Exec(CpuState cpu, Args_J args)
        {
            // Example: J addr
            cpu.PC = cpu.PC & 0xF0000000 | args.Addr << 2;
        }

        public override string Disasm(Args_J args, uint? pc = null)
        {
            return $"{Name} 0x{args.Addr:X8}";
        }

        public override uint Asm(int[] args)
        {
            // args[0] = address
            return (uint)(Opcode << 26 | args[0] & 0x03FFFFFF);
        }

        public override string Analyze(Args_J args, CpuState? cpu = null, uint? pc = null)
        {
            return $"J-Type: {Disasm(args, pc)}";
        }
    }
}
