using System;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.Common.CPU;

namespace CodeDesigner.Ps2.TestProject.TestSupport
{
    /// <summary>
    /// An EE that runs code in <see cref="TestRamBus"/> with Code Designer's
    /// instruction implementations: fetch, execute, and branch delay slots. No
    /// exceptions, interrupts or hardware: enough to run small programs such as
    /// an injected cheat engine.
    /// </summary>
    public sealed class TestMachine
    {
        public CpuState Cpu { get; } = new();
        public TestRamBus Bus { get; } = new();

        public TestMachine()
        {
            Cpu.Memory = Bus.Ram;
            Cpu.Bus = Bus;
        }

        /// <summary>Writes <paramref name="words"/> to memory from <paramref name="address"/>.</summary>
        public void LoadProgram(uint address, uint[] words)
        {
            for (int i = 0; i < words.Length; i++) Bus.WriteWord(address + 4u * (uint)i, words[i]);
        }

        /// <summary>Runs one instruction (a taken branch together with its delay slot).</summary>
        public void Step()
        {
            ulong pc = Cpu.PC;
            uint word = Bus.ReadWord((uint)pc);
            Execute(word);
            if (Cpu.PC == pc && !IsBranchOrJump(word))
            {
                Cpu.PC = pc + 4;
                return;
            }
            // A not-taken likely branch skips its delay slot; a not-taken branch
            // falls through to it.
            if (Cpu.PC == pc + 4 || (Cpu.PC == pc + 8 && IsBranchLikely(word)))
                return;
            ulong target = Cpu.PC;
            Cpu.PC = pc + 4;
            Execute(Bus.ReadWord((uint)(pc + 4)));
            Cpu.PC = target;
        }

        private void Execute(uint word)
        {
            var (instruction, args) = MipsDisassembler.GetInstructionObject(word);
            if (instruction == null)
                throw new InvalidOperationException($"No instruction for 0x{word:X8} at 0x{Cpu.PC:X8}");
            instruction.ExecuteGeneric(Cpu, args!);
        }

        private static bool IsBranchLikely(uint word)
        {
            int op = (int)(word >> 26) & 0x3F;
            int rs = (int)(word >> 21) & 0x1F;
            int rt = (int)(word >> 16) & 0x1F;
            return op switch
            {
                0x14 or 0x15 or 0x16 or 0x17 => true,
                0x01 => rt is 0x02 or 0x03 or 0x12 or 0x13,
                0x11 => rs == 8 && rt is 0x02 or 0x03,
                _ => false,
            };
        }

        private static bool IsBranchOrJump(uint word)
        {
            int op = (int)(word >> 26) & 0x3F;
            return op switch
            {
                0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x14 or 0x15 or 0x16 or 0x17 => true,
                0x00 => (word & 0x3F) is 8 or 9,
                0x11 => ((word >> 21) & 0x1F) == 8,
                _ => false,
            };
        }
    }
}
