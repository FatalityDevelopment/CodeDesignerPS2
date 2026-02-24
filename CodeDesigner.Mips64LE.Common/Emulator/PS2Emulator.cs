using CodeDesigner.Mips64LE.Common.CPU;
using CodeDesigner.Mips64LE.Common.CPU.COP0;
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
    /// Branch delay slots:
    ///   MIPS requires that the instruction immediately following every branch or jump
    ///   (the "delay slot") always executes before the CPU transfers control.  This
    ///   emulator implements the full delay-slot pipeline:
    ///     - Branch/jump taken  → execute delay slot at pc+4, then jump to target.
    ///     - Branch not taken   → delay slot instruction executes naturally as the
    ///                            next sequential instruction (pc advances to pc+4).
    ///     - Branch likely not taken → delay slot is nullified (pc+8 skips it).
    ///
    /// COP0 exceptions:
    ///   SYSCALL, BREAK, arithmetic overflow and trap instructions no longer throw
    ///   raw C# exceptions out of Step().  Instead they are routed through
    ///   <see cref="COP0ExceptionDispatcher"/> which saves the faulting PC to EPC,
    ///   sets Status.EXL, writes the exception code to Cause, and redirects the
    ///   program counter to the appropriate exception vector.
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

        /// <summary>
        /// Raised when the INTC reports a pending interrupt (INTC_STAT &amp; INTC_MASK != 0).
        /// The event argument is the INTC_STAT value at the time of detection.
        /// A full emulator would route this into the MIPS interrupt exception handler;
        /// here it is surfaced as an event so callers can respond as needed.
        /// </summary>
        public event EventHandler<uint>? InterruptPending;

        /// <summary>
        /// Raised when a DMAC channel completes a transfer and its D_STAT enable bit
        /// is set.  The event argument is the 0-based channel index (0–9).
        /// </summary>
        public event EventHandler<int>? DmaChannelComplete;

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

            // Wire timer interrupt events to INTC
            WireTimerInterrupts();

            // Wire DMAC channel-complete events
            WireDmacEvents();

            Reset();
        }

        /// <summary>
        /// Subscribe each timer's compare/overflow events to the INTC so that
        /// timer interrupts automatically set the corresponding INTC_STAT bits.
        /// </summary>
        private void WireTimerInterrupts()
        {
            for (int i = 0; i < Bus.Timers.Length; i++)
            {
                int timerIndex = i; // capture for closure
                Bus.Timers[i].CompareInterruptPending  += (_, _) =>
                    Bus.Intc.RaiseInterrupt(Hardware.INTC.INT_TIMER0 + timerIndex);
                Bus.Timers[i].OverflowInterruptPending += (_, _) =>
                    Bus.Intc.RaiseInterrupt(Hardware.INTC.INT_TIMER0 + timerIndex);
            }
        }

        /// <summary>
        /// Subscribe DMAC channel-complete events and forward them through the
        /// <see cref="DmaChannelComplete"/> event so callers can act on DMA
        /// completion (e.g. raise an INTC interrupt or wake a waiting thread).
        /// </summary>
        private void WireDmacEvents()
        {
            Bus.Dmac.ChannelTransferComplete += (_, chIdx) =>
                DmaChannelComplete?.Invoke(this, chIdx);
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
        /// Execute a single instruction at the current PC, honouring the MIPS branch
        /// delay slot and routing CPU exceptions through the COP0 dispatcher.
        ///
        /// Branch delay slot rules implemented here:
        ///   • Branch/jump taken       → execute the delay-slot instruction at pc+4,
        ///                               then transfer to the branch target.
        ///   • Branch not taken        → delay slot executes as the natural next
        ///                               instruction (pc advances to pc+4 and the
        ///                               delay slot runs on the following Step() call).
        ///   • Branch-likely not taken → delay slot is nullified; pc is already at
        ///                               pc+8 as set by the instruction.
        ///
        /// COP0 exception routing:
        ///   OverflowException, TrapException, SystemCallException and
        ///   BreakpointException are caught and forwarded to
        ///   <see cref="COP0ExceptionDispatcher.Dispatch"/>, which saves EPC,
        ///   updates Status/Cause and redirects cpu.PC to the exception vector.
        /// </summary>
        public void Step()
        {
            // Fetch: translate virtual address, then read the 32-bit instruction word.
            uint pc32 = (uint)(Cpu.PC & 0xFFFFFFFFUL);
            uint word = Bus.ReadWord(pc32);

            ulong pcBefore = Cpu.PC;

            // Decode
            var (instrObj, instrArgs) = MipsDisassembler.GetInstructionObject(word);

            if (instrObj == null)
            {
                UnknownInstruction?.Invoke(this, word);
                Cpu.PC = pcBefore + 4;
                return;
            }

            // Execute with COP0 exception routing.
            try
            {
                instrObj.ExecuteGeneric(Cpu, instrArgs!);
            }
            catch (OverflowException)
            {
                COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.Overflow, pcBefore, inDelaySlot: false);
                InstructionCount++;
                return;
            }
            catch (Cpu.Instructions.EE.TrapException)
            {
                COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.Trap, pcBefore, inDelaySlot: false);
                InstructionCount++;
                return;
            }
            catch (Cpu.Instructions.EE.SystemCallException)
            {
                COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.Syscall, pcBefore, inDelaySlot: false);
                InstructionCount++;
                SyscallRaised?.Invoke(this, (uint)pcBefore);
                return;
            }
            catch (Cpu.Instructions.EE.BreakpointException)
            {
                // BREAK is routed through COP0 (EPC, Status.EXL, Cause.ExcCode=9)
                // and then re-thrown so that debugger-aware callers (e.g. Run()) can
                // halt execution.  All other exception types are fully consumed here.
                COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.Breakpoint, pcBefore, inDelaySlot: false);
                InstructionCount++;
                throw;
            }

            InstructionCount++;

            // ── PC advancement and branch delay slot ──────────────────────────────

            if (Cpu.PC == pcBefore)
            {
                // Non-branch instruction: advance PC by 4 — no delay slot.
                Cpu.PC = pcBefore + 4;
                return;
            }

            // Detect branch-likely not-taken: these instructions set PC=pcBefore+8
            // to nullify the delay slot.  We must check the instruction opcode here
            // because a regular taken branch can also produce pc==pcBefore+8 (e.g.
            // BEQ with offset=+1 taken).
            if (Cpu.PC == pcBefore + 8 && IsBranchLikelyInstruction(word))
            {
                // Branch-likely not taken: delay slot has been nullified.
                // PC is already at pcBefore+8 (set by the instruction).
                return;
            }

            ulong branchTarget = Cpu.PC;

            if (branchTarget != pcBefore + 4)
            {
                // Branch/jump taken: execute the delay-slot instruction at pc+4
                // before transferring to the target.
                uint dsPc32 = (uint)((pcBefore + 4) & 0xFFFFFFFFUL);
                uint dsWord = Bus.ReadWord(dsPc32);

                // Restore PC to the delay-slot address before execution so that
                // any PC-relative computation inside the delay slot is correct.
                Cpu.PC = pcBefore + 4;

                var (dsObj, dsArgs) = MipsDisassembler.GetInstructionObject(dsWord);
                if (dsObj != null)
                {
                    // Exceptions raised by a delay-slot instruction are also routed
                    // through COP0, with inDelaySlot=true so EPC points to the branch
                    // instruction (pcBefore) and Cause.BD is set.
                    try
                    {
                        dsObj.ExecuteGeneric(Cpu, dsArgs!);
                        InstructionCount++;
                    }
                    catch (OverflowException)
                    {
                        COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.Overflow, pcBefore + 4, inDelaySlot: true);
                        InstructionCount++;
                        return;
                    }
                    catch (Cpu.Instructions.EE.TrapException)
                    {
                        COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.Trap, pcBefore + 4, inDelaySlot: true);
                        InstructionCount++;
                        return;
                    }
                    catch (Cpu.Instructions.EE.SystemCallException)
                    {
                        COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.Syscall, pcBefore + 4, inDelaySlot: true);
                        InstructionCount++;
                        SyscallRaised?.Invoke(this, (uint)(pcBefore + 4));
                        return;
                    }
                    catch (Cpu.Instructions.EE.BreakpointException)
                    {
                        COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.Breakpoint, pcBefore + 4, inDelaySlot: true);
                        InstructionCount++;
                        throw;
                    }
                }
                else
                {
                    UnknownInstruction?.Invoke(this, dsWord);
                }

                // Transfer to the branch target regardless of what the delay-slot
                // instruction did to cpu.PC.
                Cpu.PC = branchTarget;
            }
            // else: branchTarget == pcBefore+4 (branch not taken).
            // The delay slot at pcBefore+4 will run as the next sequential Step().
        }

        /// <summary>
        /// Returns true if <paramref name="word"/> encodes a "branch-likely"
        /// instruction (BxxL / BCxL family).  Branch-likely instructions set
        /// cpu.PC = pc+8 when not taken in order to nullify the delay slot; they
        /// must be identified so that a regular taken branch to pc+8 is not
        /// mistaken for a nullified delay slot.
        /// </summary>
        private static bool IsBranchLikelyInstruction(uint word)
        {
            int opcode = (int)(word >> 26) & 0x3F;

            switch (opcode)
            {
                // BEQL=0x14, BNEL=0x15, BLEZL=0x16, BGTZL=0x17
                case 0x14: case 0x15: case 0x16: case 0x17:
                    return true;

                // REGIMM group (opcode=0x01): rt field selects sub-opcode.
                // BLTZL=0x02, BGEZL=0x03, BLTZALL=0x12, BGEZALL=0x13
                case 0x01:
                {
                    int rt = (int)(word >> 16) & 0x1F;
                    return rt == 0x02 || rt == 0x03 || rt == 0x12 || rt == 0x13;
                }

                // COP1 branch-likely: opcode=0x11, rs=8 (BC sub-group),
                // rt=0x02 (BC1FL) or rt=0x03 (BC1TL)
                case 0x11:
                {
                    int rs = (int)(word >> 21) & 0x1F;
                    int rt = (int)(word >> 16) & 0x1F;
                    return rs == 8 && (rt == 0x02 || rt == 0x03);
                }

                default:
                    return false;
            }
        }

        /// <summary>
        /// Run the CPU for at most <paramref name="maxInstructions"/> steps,
        /// stopping early if a BREAK instruction is executed (which re-throws
        /// <see cref="Cpu.Instructions.EE.BreakpointException"/> out of Step()).
        /// SYSCALL exceptions are handled internally by Step() via COP0 exception
        /// dispatch and do not propagate here.
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
                catch (Cpu.Instructions.EE.BreakpointException)
                {
                    throw;
                }
            }
            return executed;
        }

        /// <summary>
        /// Advance all hardware peripherals (timers, INTC) by
        /// <paramref name="busCycles"/> EE bus-clock cycles (~75 MHz).
        ///
        /// Call this periodically between calls to <see cref="Step"/> or
        /// <see cref="Run"/> to keep timer counts accurate.  A typical usage
        /// pattern is to tick hardware once per instruction (passing 1–4 cycles)
        /// or once per basic block.
        ///
        /// If any timer fires an interrupt whose corresponding INTC_MASK bit is
        /// set, <see cref="InterruptPending"/> is raised with the current
        /// INTC_STAT value.
        /// </summary>
        public void TickHardware(uint busCycles = 1)
        {
            foreach (var timer in Bus.Timers)
                timer.Tick(busCycles);

            if (Bus.Intc.IsInterruptPending)
                InterruptPending?.Invoke(this, Bus.Intc.Stat);
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
