using CodeDesigner.Ps2.Common.Cpu.InstrArgs;
using CodeDesigner.Ps2.Common.CPU;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Ps2.Common.Cpu.Instructions
{
    // Non-generic base interface for dynamic invocation
    public interface IInstruction
    {
        void ExecuteGeneric(CpuState cpu, object args);
        string AnalyzeGeneric(object args, CpuState? cpu = null, uint? pc = null);
    }

    // Generic base class for all instruction types
    public abstract class Instruction<TArgs> : IInstruction
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
        
        // Generic wrappers for working with instructions without knowing the exact TArgs type
        public virtual void ExecuteGeneric(CpuState cpu, object args)
        {
            if (args is TArgs typedArgs)
            {
                Exec(cpu, typedArgs);
            }
        }
        
        public virtual string AnalyzeGeneric(object args, CpuState? cpu = null, uint? pc = null)
        {
            if (args is TArgs typedArgs)
            {
                return Analyze(typedArgs, cpu, pc);
            }
            return string.Empty;
        }
    }
}
