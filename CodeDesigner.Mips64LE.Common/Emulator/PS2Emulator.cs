using CodeDesigner.Mips64LE.Common.CPU;
using CodeDesigner.Mips64LE.Common.CPU.COP0;
using CodeDesigner.Mips64LE.Common.Emulator.Hardware;
using CodeDesigner.Mips64LE.Common.VectorUnits.VU0;
using CodeDesigner.Mips64LE.Common.VectorUnits.VU1;
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

        // ── VBlank / VSync timing constants (NTSC 60 Hz, 147.456 MHz EE bus) ────
        // PS2 NTSC: 147,456,000 bus cycles per second / 60 Hz = 2,457,600 per frame.
        // VBlank starts at scan line 235 of 262.5 (~89.6% through the frame).
        // Reference: PCSX2 pcsx2/R5900.cpp GetVsyncFrequency / ps2/IopCounters.cpp
        public const uint NtscFrameCycles      = 2_457_600u;
        public const uint NtscVBlankStartCycle = 2_201_856u; // 235 × 9369 cycles/line

        private uint _vSyncCycles;
        private bool _inVBlank;

        // COP0 Count increments at half the EE CPU clock rate (PCSX2 R5900.cpp:
        // "cpuRegs.CP0.n.Count += elapsed / 2").  We track a one-bit accumulator
        // so that IncrementCount(1) is called every other EE instruction.
        private int _cop0CountAccum;

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

            // Wire the full memory bus into CpuState so that load/store
            // instructions can route through it for KSEG0/KSEG1 address
            // translation and hardware I/O register dispatch.
            Cpu.Bus = Bus;

            // Wire VIF0 to VU0 data/micro memory so UNPACK and MPG write directly
            // into the VU0 register state that COP2 macro-mode instructions read.
            // Also handle MSCAL/MSCNT → run VU0 microprogram.
            Bus.Vif0.VuDataMem  = Cpu.VU0.DataMem;
            Bus.Vif0.VuMicroMem = Cpu.VU0.MicroMem;
            Bus.Vif0.MicroStart += (_, addr) =>
            {
                Cpu.VU0.Start((ushort)(addr * 8));  // addr in qwords → byte offset
                VU0Executor.RunProgram(Cpu.VU0);
            };

            // Wire VIF1 to VU1 data/micro memory and handle MSCAL → run VU1 program.
            Bus.Vif1.VuDataMem  = Cpu.VU1.DataMem;
            Bus.Vif1.VuMicroMem = Cpu.VU1.MicroMem;
            Bus.Vif1.MicroStart += (_, addr) =>
            {
                Cpu.VU1.Start((ushort)(addr * 8));  // addr in qwords → byte offset
                VU1Executor.RunProgram(Cpu.VU1, Bus.Gif);
            };

            // Wire VIF1 DIRECT/DIRECTHL output to GIF (PATH2).
            Bus.Vif1.GifOutput = Bus.Gif;

            // Wire DMAC channels 0 (VIF0), 1 (VIF1), and 2 (GIF) so that RAM→FIFO
            // transfers are delivered to the corresponding unit's packet processor.
            Bus.Dmac.Channels[0].DataConsumer = (buf, start, len) =>
                Bus.Vif0.Feed(new ReadOnlySpan<uint>(buf, start, len));
            Bus.Dmac.Channels[1].DataConsumer = (buf, start, len) =>
                Bus.Vif1.Feed(new ReadOnlySpan<uint>(buf, start, len));
            Bus.Dmac.Channels[2].DataConsumer = (buf, start, len) =>
                Bus.Gif.Feed(new ReadOnlySpan<uint>(buf, start, len));

            // Wire EE DMAC CH5 (SIF0, FIFO→RAM): EE receives data from IOP via SIF0 FIFO.
            Bus.Dmac.Channels[5].DataProvider = () => Bus.Sif.ReadSif0();

            // Wire EE DMAC CH6 (SIF1, RAM→FIFO): EE sends data to IOP via SIF1 FIFO.
            Bus.Dmac.Channels[6].DataConsumer = (buf, start, len) =>
            {
                for (int i = start; i < start + len; i++)
                    Bus.Sif.WriteSif1(buf[i]);
            };

            // Wire timer interrupt events to INTC
            WireTimerInterrupts();

            // Wire DMAC channel-complete events
            WireDmacEvents();

            // Wire SIF IOP→EE signal to INTC SBUS interrupt (bit 1).
            // When the IOP sets bits in SMFLAG (via WriteIopRegister), the EE must
            // be interrupted so it can process the IOP response and exit any spin loop.
            Bus.Sif.IopSignaledEe += (_, _) => Bus.Intc.RaiseInterrupt(INTC.INT_SBUS);

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
        /// <summary>
        /// Fetch the instruction word at the given virtual PC.
        /// Handles KUSEG TLB translation (when the TLB has been populated by the BIOS),
        /// KSEG0/KSEG1 fixed mapping (via the bus), and throws
        /// <see cref="TlbMissException"/> on a KUSEG TLB miss once TLB entries exist.
        /// </summary>
        private uint FetchWord(uint virtualPc)
        {
            // KUSEG (0x00000000–0x6FFFFFFF): consult the software TLB when it has been
            // populated (i.e. the BIOS has written at least one entry via TLBWI/TLBWR).
            // Before any entries are written the bus identity-maps KUSEG, preserving
            // backward-compatibility for unit tests and early-boot code.
            if ((virtualPc & 0xC0000000u) == 0u && virtualPc < 0x70000000u
                && Cpu.COP0.TLB.HasAnyValidEntry)
            {
                byte asid = (byte)(Cpu.COP0.ReadRegister64(10) & 0xFFu); // EntryHi[7:0]
                uint? paddr = Cpu.COP0.TLB.Translate(virtualPc, asid);
                if (paddr.HasValue)
                    return Bus.ReadWord(paddr.Value);
                throw new TlbMissException(virtualPc, isStore: false);
            }
            // KSEG0/KSEG1/scratchpad (and pre-TLB KUSEG): bus handles the translation.
            return Bus.ReadWord(virtualPc);
        }

        public void Reset()
        {
            Cpu.PC = 0xBFC00000UL;
            InstructionCount = 0;
            _cop0CountAccum = 0;

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
        ///
        /// Hardware interrupt delivery:
        ///   Before each instruction, if the INTC reports a pending interrupt and
        ///   the CPU is ready to accept it (IE=1, EXL=0, ERL=0, EIE=1, IM2=1) a
        ///   hardware-interrupt exception (ExcCode=0) is dispatched via COP0 and
        ///   the instruction fetch is skipped for that Step().
        /// </summary>
        public void Step()
        {
            // ── COP0 Count increment + Count/Compare interrupt check ──────────
            // Count increments at HALF the EE CPU clock rate.
            // Reference: PCSX2 R5900.cpp  cpuRegs.CP0.n.Count += elapsed / 2.
            // We alternate a one-bit accumulator so IncrementCount(1) fires every
            // other Step(), correctly matching the PS2's half-rate Count behaviour
            // and ensuring the BIOS RDRAM timing calibration measures the right value.
            if ((_cop0CountAccum ^= 1) == 0)
                Cpu.COP0.IncrementCount(1);

            // ── Hardware interrupt check ──────────────────────────────────────
            // Deliver a pending INTC interrupt before executing the next instruction
            // when the CPU is ready to accept it.
            if (Bus.Intc.IsInterruptPending || Cpu.COP0.IsTimerInterruptPending)
            {
                // PS2 EE interrupt enable condition (PCSX2 R5900.cpp):
                //   Status must have IE(b0)=1, EXL(b1)=0, ERL(b2)=0, EIE(b16)=1
                //   (mask 0x10007, required value 0x10001)
                uint st = Cpu.COP0.ReadRegister32(12);
                if ((st & 0x10007u) == 0x10001u)
                {
                    bool takeInterrupt = false;

                    // INTC interrupt is wired to IP2 (bit 10 in Cause/Status).
                    // Status.IM2 (bit 10) must be set.
                    if (Bus.Intc.IsInterruptPending && (st & 0x400u) != 0)
                    {
                        // Set Cause.IP2 (bit 10) to reflect the pending INTC interrupt.
                        uint cause = Cpu.COP0.ReadRegister32(13);
                        cause |= 0x400u;
                        Cpu.COP0.WriteRegister32(13, cause);
                        takeInterrupt = true;
                    }

                    // Count/Compare timer interrupt uses IP7 (bit 15 in Cause/Status).
                    // IP7 is already set in Cause by IncrementCount(); check Status.IM7.
                    if (Cpu.COP0.IsTimerInterruptPending && (st & 0x8000u) != 0)
                    {
                        takeInterrupt = true;
                    }

                    if (takeInterrupt)
                    {
                        COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.Interrupt, Cpu.PC, inDelaySlot: false);
                        InstructionCount++;
                        return;
                    }
                }
            }

            // Fetch: translate virtual address, then read the 32-bit instruction word.
            // For KUSEG addresses (below 0x80000000) the software-managed TLB is consulted
            // when no fixed mapping applies.  KSEG0/KSEG1 continue to use the bus's
            // built-in static address translation.
            uint pc32 = (uint)(Cpu.PC & 0xFFFFFFFFUL);
            uint word;
            try
            {
                word = FetchWord(pc32);
            }
            catch (TlbMissException tlbFetch)
            {
                // Instruction-fetch TLB miss → ExcCode=TlbLoad (2).
                Cpu.COP0.WriteRegister32(8, tlbFetch.VirtualAddress);  // BadVAddr
                Cpu.COP0.WriteRegister64(10,
                    (Cpu.COP0.ReadRegister64(10) & 0xFFu) | ((ulong)(tlbFetch.VirtualAddress & 0xFFFFE000u)));
                COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.TlbLoad, Cpu.PC, inDelaySlot: false);
                InstructionCount++;
                return;
            }

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
            catch (TlbMissException tlbEx)
            {
                // Data load/store TLB miss.  Update BadVAddr and EntryHi.VPN2, then
                // dispatch the appropriate COP0 exception.
                ExceptionCode tlbCode = tlbEx.IsStore ? ExceptionCode.TlbStore : ExceptionCode.TlbLoad;
                Cpu.COP0.WriteRegister32(8, tlbEx.VirtualAddress);  // BadVAddr
                Cpu.COP0.WriteRegister64(10,
                    (Cpu.COP0.ReadRegister64(10) & 0xFFu) | ((ulong)(tlbEx.VirtualAddress & 0xFFFFE000u)));
                COP0ExceptionDispatcher.Dispatch(Cpu, tlbCode, pcBefore, inDelaySlot: false);
                InstructionCount++;
                return;
            }

            InstructionCount++;

            // ── PC advancement and branch delay slot ──────────────────────────────

            if (Cpu.PC == pcBefore && !IsBranchOrJumpInstruction(word))
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
                uint dsWord;
                try
                {
                    dsWord = FetchWord(dsPc32);
                }
                catch (TlbMissException tlbDs)
                {
                    Cpu.COP0.WriteRegister32(8, tlbDs.VirtualAddress);
                    Cpu.COP0.WriteRegister64(10,
                        (Cpu.COP0.ReadRegister64(10) & 0xFFu) | ((ulong)(tlbDs.VirtualAddress & 0xFFFFE000u)));
                    COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.TlbLoad, pcBefore + 4, inDelaySlot: true);
                    InstructionCount++;
                    return;
                }

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
        /// Returns true if <paramref name="word"/> is any branch or jump instruction
        /// (including J, JAL, JR, JALR, BEQ/BNE/BLEZ/BGTZ and their -L variants,
        /// REGIMM branches, ERET, and COP1 branches).
        ///
        /// Used to distinguish a jump-to-self (where the instruction sets PC to the
        /// same value as pcBefore) from a true non-branch instruction that leaves
        /// PC unchanged.
        /// </summary>
        private static bool IsBranchOrJumpInstruction(uint word)
        {
            int op = (int)(word >> 26) & 0x3F;
            switch (op)
            {
                // J, JAL
                case 0x02: case 0x03:
                    return true;

                // BEQ, BNE, BLEZ, BGTZ (and their -L variants 0x14-0x17)
                case 0x04: case 0x05: case 0x06: case 0x07:
                case 0x14: case 0x15: case 0x16: case 0x17:
                    return true;

                // REGIMM: all rt sub-codes are branches (BLTZ, BGEZ, BLTZAL, BGEZAL, *L variants)
                case 0x01:
                    return true;

                // SPECIAL: JR (funct=8), JALR (funct=9)
                case 0x00:
                {
                    int funct = (int)(word & 0x3F);
                    return funct == 8 || funct == 9;
                }

                // COP0: ERET (rs=0x10=CO, funct=0x18)
                case 0x10:
                {
                    int rs    = (int)(word >> 21) & 0x1F;
                    int funct = (int)(word & 0x3F);
                    return rs == 0x10 && funct == 0x18;
                }

                // COP1: branch instructions (rs=8 = BC sub-group)
                case 0x11:
                {
                    int rs = (int)(word >> 21) & 0x1F;
                    return rs == 8;
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

            // ── VBlank / VSync generation (NTSC 60 Hz) ───────────────────────
            // Accumulate EE bus cycles and fire VBlank-start and VBlank-end at the
            // correct frame positions.
            //
            //  VBlank-start (INTC bit 2, GS_CSR bit 3):
            //    Fires after 235 of 262.5 scan lines (~89.6% through the frame).
            //    • Sets GS_CSR bit 3 so GS-CSR polling code (e.g. the vSync sub
            //      that ORs bit 3 to clear it, then polls until it's set again)
            //      unblocks on the next VSync.
            //    • Raises INTC VBLANK_S so BIOS/games using INTC-based interrupt
            //      handlers break out of their wait loops.
            //
            //  VBlank-end (INTC bit 3):
            //    Fires at the start of the new active period (frame rollover).
            uint vPrev = _vSyncCycles;
            _vSyncCycles += busCycles;

            if (!_inVBlank
                && vPrev < NtscVBlankStartCycle
                && _vSyncCycles >= NtscVBlankStartCycle)
            {
                _inVBlank = true;
                Bus.Gs.RaiseVSync();           // GS_CSR bit 3 for polling code
                Bus.Intc.RaiseVBlankStart();   // INTC bit 2 for interrupt-based code
            }

            if (_vSyncCycles >= NtscFrameCycles)
            {
                _vSyncCycles -= NtscFrameCycles;
                if (_inVBlank)
                {
                    _inVBlank = false;
                    Bus.Intc.RaiseVBlankEnd();  // INTC bit 3
                }
            }

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
