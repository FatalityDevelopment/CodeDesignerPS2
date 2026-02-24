using CodeDesigner.Mips64LE.Common.CPU;
using System;

namespace CodeDesigner.Mips64LE.Common.Emulator
{
    /// <summary>
    /// PS2 Emotion Engine CPU emulator.
    ///
    /// Implements a MIPS fetch-decode-execute cycle using the instruction
    /// objects already defined in CodeDesigner.Mips64LE.Common.Instructions.
    ///
    /// PC management convention (matching the existing instruction implementations):
    ///   - Non-branch instructions do not modify cpu.PC; the emulator advances it +4.
    ///   - Branch/jump instructions write the destination (or pc+4 when not taken)
    ///     directly to cpu.PC during Exec().  The emulator detects this and uses the
    ///     instruction-set value verbatim.
    ///   - "Branch likely" not-taken instructions write pc+8 (skipping the delay slot);
    ///     the emulator uses that value directly as well.
    ///
    /// Note: MIPS branch delay slots are intentionally not emulated in this first-pass
    /// implementation. When a branch is taken the delay-slot instruction at pc+4 is
    /// skipped. This is sufficient for programs that do not rely on delay-slot
    /// side-effects.
    ///
    /// The <see cref="PS2MemoryBus"/> translates KSEG0/KSEG1 virtual addresses for
    /// instruction fetch.  Data load/store uses cpu.Memory which is wired to the bus
    /// main RAM, so programs residing in physical RAM (0x00000000-0x01FFFFFF) have
    /// correct load/store behaviour.
    /// </summary>
    public sealed class PS2Emulator
    {
        public CpuState Cpu  { get; }
        public PS2MemoryBus Bus { get; }

        /// <summary>Total instructions executed since last reset.</summary>
        public ulong InstructionCount { get; private set; }

        /// <summary>Raised when the CPU encounters an unrecognised instruction word.</summary>
        public event EventHandler<uint>? UnknownInstruction;

        /// <summary>Raised when a SYSCALL instruction is executed.</summary>
        public event EventHandler<uint>? SyscallRaised;

        public PS2Emulator(PS2MemoryBus? bus = null)
        {
            Bus = bus ?? new PS2MemoryBus();
            Cpu = new CpuState();

            // Point CpuState.Memory at the bus RAM so existing load/store
            // instruction implementations (which index cpu.Memory directly)
            // work for programs resident in main RAM (physical 0x00000000-
            // 0x01FFFFFF).  Instruction fetch uses Bus.ReadWord() which
            // handles KSEG0/KSEG1 virtual-to-physical translation.
            Cpu.Memory = Bus.Ram;

            Reset();
        }

        /// <summary>
        /// Reset the CPU to its power-on state.
        /// PC is set to 0xBFC00000 – the MIPS reset vector (KSEG1 mirror of
        /// the BIOS ROM base, matching PCSX2 behaviour).
        /// </summary>
        public void Reset()
        {
            Cpu.PC = 0xBFC00000UL;
            InstructionCount = 0;

            // $zero is always 0
            Cpu.RegsLo[0] = 0;
            Cpu.RegsHi[0] = 0;
        }

        /// <summary>
        /// Execute a single instruction at the current PC and advance PC.
        /// </summary>
        public void Step()
        {
            // Fetch: translate virtual address, then read the 32-bit instruction word.
            uint pc32 = (uint)(Cpu.PC & 0xFFFFFFFFUL);
            uint word = Bus.ReadWord(pc32);

            ulong pcBefore = Cpu.PC;

            // Decode & execute
            var (instrObj, instrArgs) = MipsDisassembler.GetInstructionObject(word);

            if (instrObj == null)
            {
                UnknownInstruction?.Invoke(this, word);
                Cpu.PC = pcBefore + 4;
                return;
            }

            instrObj.ExecuteGeneric(Cpu, instrArgs!);
            InstructionCount++;

            // If the instruction did not modify PC (non-branch case), advance by 4.
            // Branch/jump instructions update cpu.PC themselves (to target or pc+4).
            if (Cpu.PC == pcBefore)
                Cpu.PC = pcBefore + 4;
        }

        /// <summary>
        /// Run the CPU for at most <paramref name="maxInstructions"/> steps,
        /// or until a SYSCALL/BREAK is encountered.
        /// Returns the number of instructions actually executed.
        /// </summary>
        public ulong Run(ulong maxInstructions = ulong.MaxValue)
        {
            ulong executed = 0;
            while (executed < maxInstructions)
            {
                try
                {
                    Step();
                    executed++;
                }
                catch (Cpu.Instructions.EE.SystemCallException)
                {
                    SyscallRaised?.Invoke(this, (uint)Cpu.PC);
                    // Propagate so the caller can decide whether to continue
                    throw;
                }
                catch (Cpu.Instructions.EE.BreakpointException)
                {
                    throw;
                }
            }
            return executed;
        }

        /// <summary>
        /// Load a sequence of 32-bit instruction words into main RAM starting
        /// at the given virtual address (useful for unit tests).
        /// </summary>
        public void LoadProgram(uint vaddr, ReadOnlySpan<uint> instructions)
        {
            foreach (uint instr in instructions)
            {
                Bus.WriteWord(vaddr, instr);
                vaddr += 4;
            }
        }
    }
}
