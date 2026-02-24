using System;
using CodeDesigner.Mips64LE.Common.CPU;
using CodeDesigner.Mips64LE.Common.CPU.COP0;
using CodeDesigner.Mips64LE.Common.Cpu.Instructions.EE;
using CodeDesigner.Mips64LE.Common.Emulator.Hardware;

namespace CodeDesigner.Mips64LE.Common.Emulator.IOP
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
                Memory = Bus.Ram
            };
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
        }

        /// <summary>
        /// Execute one R3000A instruction at the current PC, honouring branch
        /// delay slots and routing SYSCALL/BREAK through the IOP COP0 dispatcher.
        /// </summary>
        public void Step()
        {
            uint pc = State.PC;
            uint word = Bus.ReadWord(pc);

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
            try
            {
                instrObj.ExecuteGeneric(_bridge, instrArgs!);
            }
            catch (OverflowException)
            {
                DispatchIopException(ExceptionCode.Overflow, pc, inDelaySlot: false);
                SyncIopFromBridge();
                InstructionCount++;
                return;
            }
            catch (TrapException)
            {
                DispatchIopException(ExceptionCode.Trap, pc, inDelaySlot: false);
                SyncIopFromBridge();
                InstructionCount++;
                return;
            }
            catch (SystemCallException)
            {
                DispatchIopException(ExceptionCode.Syscall, pc, inDelaySlot: false);
                SyncIopFromBridge();
                InstructionCount++;
                SyscallRaised?.Invoke(this, pc);
                return;
            }
            catch (BreakpointException)
            {
                DispatchIopException(ExceptionCode.Breakpoint, pc, inDelaySlot: false);
                SyncIopFromBridge();
                InstructionCount++;
                throw;
            }

            InstructionCount++;

            // ── PC advancement + branch delay slot ────────────────────────────

            if (_bridge.PC == pcBefore)
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
                    try
                    {
                        dsObj.ExecuteGeneric(_bridge, dsArgs!);
                        InstructionCount++;
                    }
                    catch (OverflowException)
                    {
                        DispatchIopException(ExceptionCode.Overflow, dsPc, inDelaySlot: true);
                        SyncIopFromBridge();
                        InstructionCount++;
                        return;
                    }
                    catch (TrapException)
                    {
                        DispatchIopException(ExceptionCode.Trap, dsPc, inDelaySlot: true);
                        SyncIopFromBridge();
                        InstructionCount++;
                        return;
                    }
                    catch (SystemCallException)
                    {
                        DispatchIopException(ExceptionCode.Syscall, dsPc, inDelaySlot: true);
                        SyncIopFromBridge();
                        InstructionCount++;
                        SyscallRaised?.Invoke(this, dsPc);
                        return;
                    }
                    catch (BreakpointException)
                    {
                        DispatchIopException(ExceptionCode.Breakpoint, dsPc, inDelaySlot: true);
                        SyncIopFromBridge();
                        InstructionCount++;
                        throw;
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
        /// Tick IOP hardware (check INTC) by <paramref name="cycles"/> cycles.
        /// Raises <see cref="InterruptPending"/> when I_STAT &amp; I_MASK != 0.
        /// </summary>
        public void TickHardware(uint cycles = 1)
        {
            // Future: advance IOP timers here when IopTimers are implemented.
            _ = cycles;

            if (Bus.IsInterruptPending)
                InterruptPending?.Invoke(this, Bus.IStat);
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

            bool alreadyInException = (sr & 0x02u) != 0; // SR.EXL

            if (!alreadyInException)
            {
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
                sr |= 0x02u; // set EXL
                State.SR = sr;
            }
            else
            {
                cause = (cause & ~0x7Cu) | ((uint)code << 2);
                State.Cause = cause;
            }

            uint vector = State.Bev ? BEV1Vector : BEV0Vector;
            State.PC   = vector;
            _bridge.PC = vector;
        }

        // ── Bridge sync helpers ───────────────────────────────────────────────

        private void SyncBridgeFromIop()
        {
            for (int i = 1; i < 32; i++)
                _bridge.RegsLo[i] = State.ReadReg(i);
            _bridge.RegsLo[0] = 0;
            _bridge.HI = State.HI;
            _bridge.LO = State.LO;
            _bridge.PC = State.PC;
        }

        private void SyncIopFromBridge()
        {
            for (int i = 1; i < 32; i++)
                State.WriteReg(i, (uint)(_bridge.RegsLo[i] & 0xFFFFFFFFu));
            State.HI = (uint)(_bridge.HI & 0xFFFFFFFFu);
            State.LO = (uint)(_bridge.LO & 0xFFFFFFFFu);
            // PC is set explicitly in Step(); don't overwrite with bridge PC here
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
