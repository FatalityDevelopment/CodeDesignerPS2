using CodeDesigner.Ps2.Common.Cpu.InstrArgs;
using CodeDesigner.Ps2.Common.Cpu.Instructions;
using CodeDesigner.Ps2.Common.CPU;
using System;

namespace CodeDesigner.Ps2.Common.Instructions.CoprocessorInstructions
{
    /// <summary>
    /// BC0F/BC0T/BC0FL/BC0TL and BC2F/BC2T/BC2FL/BC2TL: branch on a coprocessor
    /// condition (COPz rs=0x08, rt = 0 false, 1 true, 2 false-likely, 3 true-likely).
    /// COP0's condition is the DMAC "all channels idle" line; COP2's is "VU0 busy".
    /// (BC1x live in Instructions_COP1.cs.)
    /// </summary>
    public sealed class CopBranch : Instruction<Args_I>
    {
        private readonly int _cop;
        private readonly bool _onTrue;
        private readonly bool _likely;

        public CopBranch(int cop, int rt)
        {
            _cop = cop;
            _onTrue = (rt & 1) != 0;
            _likely = (rt & 2) != 0;
            Opcode = 0x10 + cop;
            Funct = null;
            Name = $"bc{cop}{(_onTrue ? "t" : "f")}{(_likely ? "l" : "")}";
            ArgType = InstrArgType.I;
            ArgNames = ["offset"];
            Equation = $"if ({(_onTrue ? "" : "!")}CPCOND{cop}) PC = PC + 4 + (offset << 2)";
            Description = $"Branch on COP{cop} condition {(_onTrue ? "true" : "false")}{(_likely ? " likely" : "")}";
        }

        /// <summary>The coprocessor condition line. COP0's DMAC line isn't modelled and reads as false.</summary>
        private bool Condition(CpuState cpu) => _cop == 2 && cpu.VU0.IsRunning;

        public override void Exec(CpuState cpu, Args_I args)
        {
            if (Condition(cpu) == _onTrue)
                cpu.PC = CpuHelpers.BranchTarget((uint)cpu.PC, args.Imm);
            else
                cpu.PC += _likely ? 8u : 4u;
        }

        public override string Disasm(Args_I args, uint? pc = null)
            => pc.HasValue
                ? $"{Name} ${CpuHelpers.BranchTarget(pc.Value, args.Imm):X8}"
                : $"{Name} {CpuHelpers.FormatOffset(CpuHelpers.SignExtend16(args.Imm))}";

        public override uint Asm(int[] args) =>
            throw new NotSupportedException("Assemble coprocessor branches through MipsAssembler");

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
            => pc.HasValue ? $"→ 0x{CpuHelpers.BranchTarget(pc.Value, args.Imm):X8}" : Equation;
    }
}
