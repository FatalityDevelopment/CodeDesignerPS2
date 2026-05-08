using CodeDesigner.Ps2.Common;
using System;
using CodeDesigner.Ps2.Common.CPU;
using CodeDesigner.Ps2.Common.CPU.COP0;
using CodeDesigner.Ps2.Common.Cpu.Instructions.EE;
using CodeDesigner.Ps2.EmulatorCore.Hardware;

namespace CodeDesigner.Ps2.EmulatorCore.IOP
{
    /// <summary>
    /// PS2 IOP (I/O Processor) emulator — MIPS R3000A running at ~36.864 MHz.
    ///
    /// The IOP is a strict 32-bit MIPS I processor.  It shares the same instruction
    /// encoding as the EE R5900 for the common MIPS I subset (ALU, load/store,
    /// branch/jump, MULT/DIV, SYSCALL, BREAK), so this emulator reuses
    /// <see cref="MipsDisassembler.GetInstructionObject"/> for decoding.  EE-specific
    /// instructions (COP2, MMI, EE-wide shifts) that the IOP does not have will be
    /// treated as unknown instruction words.
    ///
    /// Execution model (matches EE for the MIPS I subset):
    ///   • Branch delay slots: a taken branch executes the instruction at PC+4 before
    ///     transferring control (identical to the EE emulator).
    ///   • Branch-likely instructions: when not taken, the delay slot is nullified.
    ///   • SYSCALL / BREAK: routed through the IOP COP0 exception dispatcher (saves
    ///     EPC, sets SR.EXL, redirects to 0xBFC00180 when BEV=1 or 0x80000080 when
    ///     BEV=0).
    ///
    /// CpuState bridge:
    ///   The IOP uses its own <see cref="IopState"/> for architectural state but
    ///   bridges into a throwaway <see cref="CpuState"/> for each instruction
    ///   execution so that the existing EE instruction implementations work without
    ///   modification.  After each instruction the 32-bit GPR values are written
    ///   back to <see cref="IopState"/> from the bridge CpuState.
    ///
    /// SIF integration:
    ///   The IOP can communicate with the EE via the shared <see cref="SIF"/>
    ///   instance accessible through <see cref="IopMemoryBus.SharedSif"/>.
    ///
    /// Reference: PCSX2 pcsx2/IopDma.h, pcsx2/R3000A.cpp, PS2 Technical Reference.
    /// </summary>
    public sealed class IopEmulator
    {
        public IopState  State { get; }
        public IopMemoryBus Bus { get; }

        /// <summary>Total instructions executed since last reset.</summary>
        public ulong InstructionCount { get; private set; }

        /// <summary>Raised when an unrecognised instruction word is fetched.</summary>
        public event EventHandler<uint>? UnknownInstruction;

        /// <summary>Raised when a SYSCALL instruction executes.</summary>
        public event EventHandler<uint>? SyscallRaised;

        /// <summary>
        /// Raised when the IOP INTC has a pending interrupt
        /// (I_STAT &amp; I_MASK != 0).  The argument is the I_STAT value.
        /// </summary>
        public event EventHandler<uint>? InterruptPending;

        /// <summary>
        /// Optional callback invoked after every IOP instruction step.
        /// Receives the current <see cref="IopState"/> (PC already advanced to
        /// the next instruction).  Useful for per-step debugging / tracing.
        /// </summary>
        public Action<IopState>? StepHook { get; set; }

        // ── Bridge CpuState used for instruction execution ────────────────────
        //
        // We create one bridge CpuState and reuse it for every instruction.
        // Before each call we copy the IOP registers into the bridge; after
        // each call we copy them back.  Memory is wired to the IOP RAM array.
        private readonly CpuState _bridge;

        public IopEmulator(IopMemoryBus? bus = null)
        {
            Bus    = bus ?? new IopMemoryBus();
            State  = new IopState();
            _bridge = new CpuState
            {
                Memory = Bus.Ram,
                IopBus = Bus
            };
            WireTimerInterrupts();
            WireDmacInterrupts();

            // Wire EE→IOP SBUS interrupt: when the EE sets bits in MSFLAG to signal
            // the IOP, raise IOP INTC bit 1 (SBUS from EE) so the IOP takes an interrupt.
            // Reference: PCSX2 pcsx2/Hw.cpp hwWrite32 SBUS_MSFLAG → iopIntcIrq(1).
            if (Bus.SharedSif != null)
                Bus.SharedSif.EeSignaledIop += (_, _) => Bus.RaiseInterrupt(1);

            Reset();
        }

        /// <summary>
        /// Convenience constructor that wires the shared SIF from an existing
        /// <see cref="PS2MemoryBus"/> so that EE ↔ IOP communication works.
        /// </summary>
        public IopEmulator(PS2MemoryBus eeBus) : this(new IopMemoryBus { SharedSif = eeBus.Sif }) { }

        /// <summary>Reset both the IOP state and the bridge CPU state.</summary>
        public void Reset()
        {
            State.Reset();
            InstructionCount = 0;
            SyncBridgeFromIop();
            // IOP is an R3000A (PRId = 0x1F, which is < 0x59).
            // The shared BIOS ROM at 0xBFC00000 reads COP0 PRId and uses SLTI to
            // distinguish EE (R5900, PRId = 0x2E20 ≥ 0x59) from IOP (R3000A,
            // PRId < 0x59) so each processor jumps to its own init path.
            // Without this the IOP inherits the bridge's default EE PRId (0x2E20)
            // and mistakenly runs the EE boot path at 0xBFC00800 instead of the
            // IOP boot path at 0xBFC02000, corrupting shared hardware state and
            // preventing the kernel from loading.
            // Reference: PCSX2 R3000A.cpp — IOP PRID = 0x0000001F.
            _bridge.COP0.WriteRegister32(15, 0x0000001Fu);
        }

        /// <summary>
        /// Execute one R3000A instruction at the current PC, honouring branch
        /// delay slots and routing SYSCALL/BREAK through the IOP COP0 dispatcher.
        /// If <see cref="StepHook"/> is set, it is called after every completed step.
        /// </summary>
        public void Step()
        {
            StepCore();
            StepHook?.Invoke(State);
        }

        private void StepCore()
        {
            // ── IOP (R3000A) hardware interrupt check ─────────────────────────
            // Keep Cause.IP2 (bit 10) in sync with IOP INTC pending state, then
            // deliver the interrupt if I_CTRL is enabled, IEc=1, and any masked
            // IP bit is pending.
            // Reference: PCSX2 pcsx2/R3000A.cpp iopEventTest:
            //   if ((psxHu32(HW_ICTRL) != 0) && (I_STAT & I_MASK) && (SR & 0xFE01) >= 0x401)
            //     psxException(0, 0);
            if (Bus.IsInterruptPending)
                State.Cause |= 0x400u;   // IP2 — IOP INTC pending
            else
                State.Cause &= ~0x400u;  // clear IP2 when INTC is quiet

            if (Bus.ICtrl != 0u                                    // I_CTRL master-enable (per PCSX2 iopEventTest HW_ICTRL != 0)
                && (State.SR & 0x1u) != 0                        // SR.IEc = 1 (interrupts enabled)
                && (State.Cause & State.SR & 0xFC00u) != 0)      // any IP bit that's also masked
            {
                DispatchIopException(ExceptionCode.Interrupt, State.PC, inDelaySlot: false);
                InstructionCount++;
                return;
            }

            uint pc = State.PC;
            uint word = Bus.ReadWord(pc);
            Bus.DebugCurrentPc = pc;  // for I_MASK write traces

            // Sync bridge GPRs from IOP state before decoding
            SyncBridgeFromIop();
            _bridge.PC = pc;

            var (instrObj, instrArgs) = MipsDisassembler.GetInstructionObject(word);
            if (instrObj == null)
            {
                UnknownInstruction?.Invoke(this, word);
                State.PC = pc + 4u;
                return;
            }

            ulong pcBefore = _bridge.PC;

            // Execute with IOP exception routing
            _bridge.ExceptionSignal = CpuExceptionSignal.None;
            try
            {
                instrObj.ExecuteGeneric(_bridge, instrArgs!);
            }
            catch (OverflowException)
            {
                DispatchIopException(ExceptionCode.Overflow, pc, inDelaySlot: false);
                InstructionCount++;
                return;
            }

            if (_bridge.ExceptionSignal != CpuExceptionSignal.None)
            {
                switch (_bridge.ExceptionSignal)
                {
                    case CpuExceptionSignal.Trap:
                        DispatchIopException(ExceptionCode.Trap, pc, inDelaySlot: false);
                        InstructionCount++;
                        return;
                    case CpuExceptionSignal.Syscall:
                        DispatchIopException(ExceptionCode.Syscall, pc, inDelaySlot: false);
                        InstructionCount++;
                        SyscallRaised?.Invoke(this, pc);
                        return;
                    case CpuExceptionSignal.Breakpoint:
                        DispatchIopException(ExceptionCode.Breakpoint, pc, inDelaySlot: false);
                        InstructionCount++;
                        throw new BreakpointException("BREAK");
                }
            }

            InstructionCount++;

            // Increment IOP COP0 Count (reg 9) each instruction so that BIOS code
            // using mfc0/mtc0 on reg 9 for clock-ratio measurement gets a real value.
            _bridge.COP0.IncrementCount(1);

            if (_bridge.PC == pcBefore && !IsBranchOrJumpInstruction(word))
            {
                // Non-branch: just advance
                State.PC = (uint)(pcBefore + 4u);
                SyncIopFromBridge();
                return;
            }

            // Branch-likely not taken: nullify delay slot
            if (_bridge.PC == pcBefore + 8 && IsBranchLikelyInstruction(word))
            {
                State.PC = (uint)_bridge.PC;
                SyncIopFromBridge();
                return;
            }

            ulong branchTarget = _bridge.PC;

            if (branchTarget != pcBefore + 4)
            {
                // Branch taken: execute delay slot first
                uint dsPc   = (uint)(pcBefore + 4u);
                uint dsWord = Bus.ReadWord(dsPc);

                _bridge.PC = dsPc;
                var (dsObj, dsArgs) = MipsDisassembler.GetInstructionObject(dsWord);
                if (dsObj != null)
                {
                    _bridge.ExceptionSignal = CpuExceptionSignal.None;
                    try
                    {
                        dsObj.ExecuteGeneric(_bridge, dsArgs!);
                        InstructionCount++;
                    }
                    catch (OverflowException)
                    {
                        DispatchIopException(ExceptionCode.Overflow, dsPc, inDelaySlot: true);
                        InstructionCount++;
                        return;
                    }

                    if (_bridge.ExceptionSignal != CpuExceptionSignal.None)
                    {
                        switch (_bridge.ExceptionSignal)
                        {
                            case CpuExceptionSignal.Trap:
                                DispatchIopException(ExceptionCode.Trap, dsPc, inDelaySlot: true);
                                InstructionCount++;
                                return;
                            case CpuExceptionSignal.Syscall:
                                DispatchIopException(ExceptionCode.Syscall, dsPc, inDelaySlot: true);
                                InstructionCount++;
                                SyscallRaised?.Invoke(this, dsPc);
                                return;
                            case CpuExceptionSignal.Breakpoint:
                                DispatchIopException(ExceptionCode.Breakpoint, dsPc, inDelaySlot: true);
                                InstructionCount++;
                                throw new BreakpointException("BREAK");
                        }
                    }
                }
                else
                {
                    UnknownInstruction?.Invoke(this, dsWord);
                }

                State.PC = (uint)branchTarget;
                SyncIopFromBridge();
            }
            else
            {
                // Branch not taken — delay slot runs on next Step()
                State.PC = (uint)_bridge.PC;
                SyncIopFromBridge();
            }
        }

        /// <summary>
        /// Run for at most <paramref name="maxInstructions"/> steps.
        /// Stops early if a BREAK instruction is executed (re-throws
        /// <see cref="BreakpointException"/>).
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
                catch (BreakpointException)
                {
                    throw;
                }
            }
            return executed;
        }

        /// <summary>
        /// Tick IOP hardware by <paramref name="cycles"/> IOP sysclock cycles.
        /// Advances all six timers and raises <see cref="InterruptPending"/> when
        /// I_STAT &amp; I_MASK != 0.
        /// </summary>
        public void TickHardware(uint cycles = 1)
        {
            for (int i = 0; i < 6; i++)
                Bus.Timers[i].Tick(cycles);

            if (Bus.IsInterruptPending)
                InterruptPending?.Invoke(this, Bus.IStat);
        }

        // ── Timer → INTC wiring ───────────────────────────────────────────────
        //
        // INTC bits (PCSX2 IopCounters.cpp):
        //   T0 → bit 4, T1 → bit 5, T2 → bit 6
        //   T3 → bit 14, T4 → bit 15, T5 → bit 16

        private void WireTimerInterrupts()
        {
            int[] bits = { 4, 5, 6, 14, 15, 16 };
            for (int i = 0; i < 6; i++)
            {
                int intcBit = bits[i];
                Bus.Timers[i].CompareInterruptPending  += (_, _) => Bus.RaiseInterrupt(intcBit);
                Bus.Timers[i].OverflowInterruptPending += (_, _) => Bus.RaiseInterrupt(intcBit);
            }
        }

        // ── DMAC → INTC wiring ────────────────────────────────────────────────
        //
        // IOP INTC bit 3 is the DMA interrupt (PCSX2 IopBios.cpp / IopDma.cpp).

        private void WireDmacInterrupts()
        {
            Bus.Dmac.ChannelTransferComplete += (_, _) => Bus.RaiseInterrupt(3);
        }

        /// <summary>
        /// Load a sequence of 32-bit instruction words into IOP RAM starting at
        /// <paramref name="addr"/> (useful for unit tests).
        /// </summary>
        public void LoadProgram(uint addr, ReadOnlySpan<uint> instructions)
        {
            foreach (uint instr in instructions)
            {
                WriteWordToRam(addr, instr);
                addr += 4u;
            }
        }

        // ── IOP COP0 exception dispatcher ────────────────────────────────────

        private void DispatchIopException(ExceptionCode code, uint epc, bool inDelaySlot)
        {
            const uint BEV0Vector = 0x80000080u;
            const uint BEV1Vector = 0xBFC00180u;

            uint sr    = State.SR;
            uint cause = State.Cause;

            // R3000A exception entry: push the IE/KU status stack.
            // {IEo,KUo} ← {IEp,KUp}; {IEp,KUp} ← {IEc,KUc}; IEc=0; KUc=0
            // (kernel mode entered, interrupts disabled at current level)
            // Reference: PCSX2 pcsx2/R3000A.cpp psxException
            sr = (sr & ~0x3Fu) | ((sr & 0x0Fu) << 2);

            if (inDelaySlot)
            {
                State.EPC  = epc - 4u;
                cause |= 0x80000000u;  // Cause.BD
            }
            else
            {
                State.EPC  = epc;
                cause &= ~0x80000000u;
            }

            cause = (cause & ~0x7Cu) | ((uint)code << 2);
            State.Cause = cause;
            State.SR    = sr;

            uint vector = (sr & IopState.SR_BEV) != 0 ? BEV1Vector : BEV0Vector;
            State.PC   = vector;
            _bridge.PC = vector;

            // Mirror the updated COP0 state into the bridge so that the SyncIopFromBridge()
            // call in exception-catch paths reads back the exception-entry values and does
            // not revert State.SR/Cause/EPC to their pre-exception values.
            _bridge.COP0.WriteRegister32(12, State.SR);
            _bridge.COP0.WriteRegister32(13, State.Cause);
            _bridge.COP0.WriteRegister32(14, State.EPC);
        }

        // ── Bridge sync helpers ───────────────────────────────────────────────

        private void SyncBridgeFromIop()
        {
            // IOP (R3000A) is a 32-bit processor.  Its registers hold 32-bit values
            // that must be SIGN-EXTENDED to 64 bits when loaded into the 64-bit EE
            // bridge CpuState.  Without sign-extension, negative 32-bit values such
            // as 0xFFFFFFFF (= −1) appear as large positive 64-bit numbers in the
            // bridge, causing signed branch conditions (bgez, bltz, blez, bgtz, slt
            // etc.) to misbehave — the canonical symptom is an infinite backward-
            // copy loop inside memmove at IOP boot (bgez loops forever because −1
            // appears as +4 294 967 295, which satisfies the "≥ 0" condition).
            for (int i = 1; i < 32; i++)
                _bridge.RegsLo[i] = (ulong)(long)(int)State.ReadReg(i);
            _bridge.RegsLo[0] = 0;
            _bridge.HI = (ulong)(long)(int)State.HI;
            _bridge.LO = (ulong)(long)(int)State.LO;
            _bridge.PC = State.PC;
            // Sync IOP COP0 (SR/Cause/EPC) so MFC0/MTC0 in running code see the right values.
            _bridge.COP0.WriteRegister32(12, State.SR);
            _bridge.COP0.WriteRegister32(13, State.Cause);
            _bridge.COP0.WriteRegister32(14, State.EPC);
        }

        private void SyncIopFromBridge()
        {
            for (int i = 1; i < 32; i++)
                State.WriteReg(i, (uint)(_bridge.RegsLo[i] & 0xFFFFFFFFu));
            State.HI = (uint)(_bridge.HI & 0xFFFFFFFFu);
            State.LO = (uint)(_bridge.LO & 0xFFFFFFFFu);
            // PC is set explicitly in Step(); don't overwrite with bridge PC here
            // Sync COP0 back: MTC0 instructions (e.g. BIOS writing SR) go to the bridge;
            // pull them back so DispatchIopException and the interrupt check see the update.
            State.SR    = _bridge.COP0.ReadRegister32(12);
            State.Cause = _bridge.COP0.ReadRegister32(13);
            State.EPC   = _bridge.COP0.ReadRegister32(14);
            // SR bit 16 = IsC (Isolate Cache): when set the CPU routes data writes
            // to the I-cache instead of main memory.  Propagate to the bus so that
            // WriteByte/WriteWord suppress RAM writes during I-cache flush loops.
            Bus.IsCacheIsolated = (State.SR & 0x10000u) != 0;
        }

        // ── Branch-likely detection (same criteria as EE emulator) ───────────

        private static bool IsBranchLikelyInstruction(uint word)
        {
            int opcode = (int)(word >> 26) & 0x3F;
            switch (opcode)
            {
                case 0x14: case 0x15: case 0x16: case 0x17:
                    return true;
                case 0x01:
                {
                    int rt = (int)(word >> 16) & 0x1F;
                    return rt == 0x02 || rt == 0x03 || rt == 0x12 || rt == 0x13;
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns true if <paramref name="word"/> is any branch or jump instruction
        /// (J, JAL, JR, JALR, BEQ/BNE/BLEZ/BGTZ and their -L variants, REGIMM branches).
        /// Used to distinguish a jump-to-self from a true non-branch instruction.
        /// </summary>
        private static bool IsBranchOrJumpInstruction(uint word)
        {
            int op = (int)(word >> 26) & 0x3F;
            switch (op)
            {
                case 0x02: case 0x03:  // J, JAL
                    return true;
                case 0x04: case 0x05: case 0x06: case 0x07:   // BEQ, BNE, BLEZ, BGTZ
                case 0x14: case 0x15: case 0x16: case 0x17:   // BEQL, BNEL, BLEZL, BGTZL
                    return true;
                case 0x01:  // REGIMM: all sub-codes are branches
                    return true;
                case 0x00:  // SPECIAL: JR (funct=8), JALR (funct=9)
                    return (word & 0x3F) == 8 || (word & 0x3F) == 9;
                default:
                    return false;
            }
        }

        // ── Low-level RAM write helper ────────────────────────────────────────

        private void WriteWordToRam(uint addr, uint value)
        {
            if (addr + 3u < (uint)IopMemoryBus.RamSize)
            {
                Bus.Ram[addr]     = (byte)value;
                Bus.Ram[addr + 1] = (byte)(value >> 8);
                Bus.Ram[addr + 2] = (byte)(value >> 16);
                Bus.Ram[addr + 3] = (byte)(value >> 24);
            }
        }
    }
}
