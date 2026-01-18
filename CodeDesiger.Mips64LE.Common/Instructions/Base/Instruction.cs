using CodeDesigner.Mips64LE.Common.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.Common.CPU;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE.Common.Cpu.Instructions
{
    // Generic base class for all instruction types
    public abstract class Instruction<TArgs>
    {
        public int Opcode { get; init; }
        public int? Funct { get; init; }

        public string Name { get; init; } = string.Empty;
        public InstrArgType ArgType { get; init; }

        public string[] ArgNames { get; init; } = Array.Empty<string>();
        public string Equation { get; init; } = string.Empty;
        public string? Description { get; init; }

        // Execution delegate
        public abstract void Exec(CpuState cpu, TArgs args);

        // Disassembly delegate
        public abstract string Disasm(TArgs args, uint? pc = null);

        // Assembly encoding
        public abstract uint Asm(int[] args);

        // Analysis helper
        public abstract string Analyze(TArgs args, CpuState? cpu = null, uint? pc = null);
    }
}
