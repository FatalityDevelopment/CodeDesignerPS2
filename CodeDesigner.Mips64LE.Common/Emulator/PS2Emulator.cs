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

        // Temporary debug tracing for byte-copy fast-forward diagnosis.
        private int _byteCopyDebugCount;
        private int _byteCopyLargeDebugCount;

        // One-shot flag: set after TryRepairEeKernelSifData() has run once.
        // The repair patches kernel data fields that were corrupted by the BIOS
        // bulk-fill loop and never restored because the kernel zeroing pass uses
        // TLB-mapped virtual addresses that the fast-forward cannot resolve.
        private bool _eeKernelSifDataFixed;

        // ── Instruction cache (i-cache): 16 KB, 256 sets, 64-byte lines ────────────
        // The R5900 has a 16 KB physically-tagged, direct-mapped i-cache with
        // 64-byte lines (16 × 32-bit instructions per line).  This matches the
        // hardware exactly and is critical for correctness:
        //
        //   • On a cache miss, the ENTIRE 64-byte line is loaded from physical RAM.
        //     When the fill-loop's delay-slot instruction (0x80010A00) is first
        //     fetched, the hardware loads the whole line 0x80010A00-0x80010A3F,
        //     proactively caching the post-loop code at 0x80010A04-0x80010A3C before
        //     the fill loop has had a chance to overwrite those addresses.
        //
        //   • After the bulk-fill fast-forward overwrites all of RAM with 0x20, the
        //     cached line is still valid, so 0x80010A04 is served from cache (correct
        //     original instruction) rather than from RAM (garbage 0x20202020).
        //
        //   • This models PCSX2's behaviour where the JIT compiles the entire block
        //     (including post-loop code) before the fill runs, rather than relying on
        //     a "lazy-per-instruction" cache populated only on first use.
        //
        // Address decomposition (physical PC):
        //   bits [31:14] = tag       (18 bits)
        //   bits [13:6]  = set index (8 bits  → 256 sets)
        //   bits [5:2]   = word-in-line offset (4 bits → 16 words)
        //   bits [1:0]   = byte offset (always 0 for 32-bit fetches)
        //
        // Only KSEG0 (0x80000000–0x9FFFFFFF) fetches are cached; KSEG1
        // (0xA0000000–0xBFFFFFFF) is the uncacheable mirror of the same
        // physical memory, matching hardware.
        //
        // Reference: R5900 User's Manual §4, PCSX2 Cache.h/Cache.cpp.
        private const int  ICacheSets       = 256;            // 256 direct-mapped sets
        private const int  ICacheLineWords  = 16;             // 16 words × 4 B = 64 B per line
        private readonly bool[] _iCacheValid = new bool[ICacheSets];
        private readonly uint[] _iCacheTags  = new uint[ICacheSets];          // physPc >> 14
        // Flat array for data: set i, word j → _iCacheData[i * ICacheLineWords + j]
        private readonly uint[] _iCacheData  = new uint[ICacheSets * ICacheLineWords];

        // Line index = bits [13:6] of physical address (8 bits).
        private static int  ICacheSetIdx(uint physPc)  => (int)((physPc >> 6) & 0xFFu);
        // Word offset within line = bits [5:2] (4 bits, 0-15).
        private static int  ICacheWordOff(uint physPc) => (int)((physPc >> 2) & 0xFu);
        // Tag = bits [31:14].
        private static uint ICacheTag(uint physPc)     => physPc >> 14;

        private void ICacheClear() => Array.Clear(_iCacheValid, 0, ICacheSets);

        // ── TLB miss state (replaces TlbMissException throw/catch) ───────────────
        // TryFetchWord sets _pendingTlbMissVA instead of throwing, allowing Step()
        // to handle instruction-fetch TLB misses (both the primary PC fetch and the
        // delay-slot fetch for taken branches) without C# exception creation overhead.
        private uint _pendingTlbMissVA;

        /// <summary>
        /// Handle a TLB miss: update COP0 BadVAddr/EntryHi/Context and dispatch
        /// the appropriate COP0 exception.  Returns true so the caller can
        /// <c>return</c> immediately after calling this helper.
        /// </summary>
        private bool DispatchTlbMiss(uint va, bool isStore, ulong epc, bool inDelaySlot)
        {
            ExceptionCode tlbCode = isStore ? ExceptionCode.TlbStore : ExceptionCode.TlbLoad;
            Cpu.COP0.WriteRegister32(8, va);   // BadVAddr
            Cpu.COP0.WriteRegister64(10,
                (Cpu.COP0.ReadRegister64(10) & 0xFFu) | ((ulong)(va & 0xFFFFE000u)));
            // Context: preserve PTEBase [31:23], update BadVPN2 [22:4]
            Cpu.COP0.WriteRegister32(4,
                (Cpu.COP0.ReadRegister32(4) & 0xFF800000u) | ((va >> 9) & 0x007FFFF0u));
            COP0ExceptionDispatcher.Dispatch(Cpu, tlbCode, epc, inDelaySlot);
            InstructionCount++;
            return true;
        }

        /// <summary>
        /// Invalidate the i-cache line that covers <paramref name="vaddr"/>.
        /// Called by the CACHE IXIN handler via
        /// <see cref="CPU.COP0.CacheState.ICacheInvalidateLine"/>.
        ///
        /// IXIN invalidates by line index (bits [13:6] of the address), matching
        /// hardware and PCSX2's CACHE case 0x07.  Only KSEG0 lines are cached.
        /// </summary>
        private void ICacheInvalidateLine(uint vaddr)
        {
            // Only KSEG0 (0x80000000–0x9FFFFFFF) is cacheable.
            if ((vaddr & 0xE0000000u) != 0x80000000u) return;
            uint physPc  = vaddr & 0x1FFFFFFFu;
            int  setIdx  = ICacheSetIdx(physPc);
            if (_iCacheValid[setIdx] && _iCacheTags[setIdx] == ICacheTag(physPc))
                _iCacheValid[setIdx] = false;
        }

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

        /// <summary>
        /// Raised at VBlank-start (NTSC scan line 235).
        /// The IOP subscribes to this to receive the equivalent of PCSX2's
        /// <c>psxVBlankStart()</c> → <c>iopIntcIrq(0)</c>.
        /// </summary>
        public event EventHandler? VBlankStarted;

        /// <summary>
        /// Raised at VBlank-end (frame roll-over, scan line 0).
        /// The IOP subscribes to this to receive the equivalent of PCSX2's
        /// <c>psxVBlankEnd()</c> → <c>iopIntcIrq(11)</c> (EvBlank).
        /// </summary>
        public event EventHandler? VBlankEnded;

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

            // Wire i-cache invalidation callbacks so the CACHE IXIN instruction
            // actually flushes our interpreter i-cache.
            // PCSX2 Cache.cpp case 0x07 (IXIN): "Not Implemented as we do not have
            // instruction cache" — they rely on the JIT's page-protection SMC handler.
            // For our pure interpreter, we implement IXIN correctly.
            Cpu.COP0.Cache.ICacheInvalidateLine = ICacheInvalidateLine;
            Cpu.COP0.Cache.ICacheInvalidateAll  = ICacheClear;

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
        /// Returns false (and sets <see cref="_pendingTlbMissVA"/>)
        /// on a KUSEG TLB miss instead of throwing, avoiding C# exception overhead.
        /// </summary>
        private bool TryFetchWord(uint virtualPc, out uint word)
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
                {
                    word = Bus.ReadWord(paddr.Value);
                    return true;
                }
                _pendingTlbMissVA = virtualPc;
                word = 0;
                return false;
            }

            // KSEG0 (0x80000000–0x9FFFFFFF): cacheable — goes through the i-cache.
            // Bits [31:29] == 100 selects KSEG0.
            if ((virtualPc & 0xE0000000u) == 0x80000000u)
            {
                uint physPc  = virtualPc & 0x1FFFFFFFu;
                int  setIdx  = ICacheSetIdx(physPc);
                uint tag     = ICacheTag(physPc);

                if (!_iCacheValid[setIdx] || _iCacheTags[setIdx] != tag)
                {
                    // Cache miss: load the entire 64-byte line from physical RAM.
                    // This mirrors the hardware's line-fill behaviour: when any word
                    // in a line is first fetched, all 16 words are loaded at once.
                    // Crucially, this loads the post-loop code (e.g. 0x80010A04)
                    // when the delay slot at the aligned start of its line is fetched.
                    uint lineBase = physPc & ~0x3Fu; // align to 64-byte boundary
                    int  dataBase = setIdx * ICacheLineWords;
                    int  ramLen   = Bus.Ram.Length;
                    for (int i = 0; i < ICacheLineWords; i++)
                    {
                        uint p = lineBase + (uint)(i << 2);
                        _iCacheData[dataBase + i] = (p + 3 < (uint)ramLen)
                            ? (uint)(Bus.Ram[p] | (Bus.Ram[p + 1] << 8)
                                               | (Bus.Ram[p + 2] << 16)
                                               | (Bus.Ram[p + 3] << 24))
                            : Bus.ReadWord(0x80000000u | p); // non-RAM KSEG0 region
                    }
                    _iCacheTags[setIdx]  = tag;
                    _iCacheValid[setIdx] = true;
                }

                word = _iCacheData[setIdx * ICacheLineWords + ICacheWordOff(physPc)];
                return true;
            }

            // KSEG1 (0xA0000000–0xBFFFFFFF) is the non-cacheable mirror.
            // KSEG2/3 and scratchpad / pre-TLB KUSEG: bus handles translation.
            word = Bus.ReadWord(virtualPc);
            return true;
        }

        /// <summary>
        /// Convenience wrapper around <see cref="TryFetchWord"/> used exclusively by
        /// the fast-forward helpers (<see cref="TryFastForwardByteFill"/>,
        /// <see cref="TryFastForwardBnelByteFill"/>, and the i-cache pre-fetch in
        /// <see cref="ICachePreFetchLine"/>).  All of these helpers guard themselves
        /// with an <c>(pc32 &amp; 0xC0000000) == 0x80000000</c> check, ensuring that only
        /// KSEG0/KSEG1 addresses are ever passed here.  Neither KSEG region uses the
        /// software TLB, so a TLB miss is structurally impossible and the bool return
        /// value of <see cref="TryFetchWord"/> is safely discarded.
        /// </summary>
        private uint FetchWord(uint virtualPc)
        {
            TryFetchWord(virtualPc, out uint w);
            return w;
        }

        public void Reset()
        {
            Cpu.PC = 0xBFC00000UL;
            InstructionCount = 0;
            _cop0CountAccum = 0;

            // Clear the i-cache so that stale entries from a previous run cannot
            // survive a reset (matches hardware power-on i-cache flush behaviour).
            ICacheClear();

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

            // The Random register decrements every clock cycle, cycling from
            // (TLBEntries-1) down to Wired.  TLBWR uses this register to select
            // a replacement slot.  Without this decrement every TLBWR would
            // always write to the same slot (initial value 47), causing
            // previously-installed entries to be evicted on every TLB miss.
            Cpu.COP0.UpdateRandom();

            // ── Bulk byte-fill loop fast-forward ─────────────────────────────
            // When the EE is executing a tight byte-fill loop with a large remaining
            // iteration count, collapse all remaining iterations into a single
            // Array.Fill / Span.Fill call.  This turns a ~535 million-instruction
            // BIOS init loop into a microsecond operation.
            //
            // We allow the fast-forward regardless of EXL/IE state.  The BIOS runs
            // two fill passes: the first with EXL=1 (interrupts masked by exception
            // mode), the second with EXL=0, IE=1 (interrupts enabled).  Both covers
            // all of RAM; timer interrupts that would have fired during the second pass
            // are benign (they only advance counters) and the BIOS tolerates their
            // absence because it recalibrates after the fill completes.
            // See TryFastForwardByteFill for the pattern detection logic.
            if (TryFastForwardByteFill())
                return;

            if (TryFastForwardBnelByteFill())
                return;

            if (TryFastForwardQuadZeroFill())
                return;

            if (TryFastForwardByteCopy())
                return;

            if (TryFastForwardLinearSearch())
                return;

            if (TryFastForwardModuleInstallLoop())
                return;

            if (TryFastForwardBiosPuts())
                return;

            TryRepairEeKernelSifData();

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

            if (!TryFetchWord(pc32, out uint word))
            {
                // Instruction-fetch TLB miss → ExcCode=TlbLoad (2).
                // Update BadVAddr, EntryHi.VPN2, and Context.BadVPN2 to let the
                // TLB refill handler at 0x80000000 find the correct page-table entry.
                // Reference: PCSX2 R5900OpcodeImpl.cpp cpuException.
                DispatchTlbMiss(_pendingTlbMissVA, isStore: false, Cpu.PC, inDelaySlot: false);
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

            // Execute — clear the exception signal then run the instruction.
            // Signal-raising instructions (SYSCALL, BREAK, trap variants) set
            // cpu.ExceptionSignal instead of throwing, avoiding C# exception overhead.
            // OverflowException is still used by checked arithmetic in ADD/SUB/ADDI.
            Cpu.ExceptionSignal = CpuExceptionSignal.None;
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

            // Check exception signal set by the instruction.
            if (Cpu.ExceptionSignal != CpuExceptionSignal.None)
            {
                switch (Cpu.ExceptionSignal)
                {
                    case CpuExceptionSignal.Syscall:
                        COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.Syscall, pcBefore, inDelaySlot: false);
                        InstructionCount++;
                        SyscallRaised?.Invoke(this, (uint)pcBefore);
                        return;
                    case CpuExceptionSignal.Breakpoint:
                        COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.Breakpoint, pcBefore, inDelaySlot: false);
                        InstructionCount++;
                        throw new Cpu.Instructions.EE.BreakpointException("BREAK");
                    case CpuExceptionSignal.Trap:
                        COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.Trap, pcBefore, inDelaySlot: false);
                        InstructionCount++;
                        return;
                }
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
                if (!TryFetchWord(dsPc32, out uint dsWord))
                {
                    DispatchTlbMiss(_pendingTlbMissVA, isStore: false, pcBefore + 4, inDelaySlot: true);
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
                    Cpu.ExceptionSignal = CpuExceptionSignal.None;
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

                    if (Cpu.ExceptionSignal != CpuExceptionSignal.None)
                    {
                        switch (Cpu.ExceptionSignal)
                        {
                            case CpuExceptionSignal.Syscall:
                                COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.Syscall, pcBefore + 4, inDelaySlot: true);
                                InstructionCount++;
                                SyscallRaised?.Invoke(this, (uint)(pcBefore + 4));
                                return;
                            case CpuExceptionSignal.Breakpoint:
                                COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.Breakpoint, pcBefore + 4, inDelaySlot: true);
                                InstructionCount++;
                                throw new Cpu.Instructions.EE.BreakpointException("BREAK");
                            case CpuExceptionSignal.Trap:
                                COP0ExceptionDispatcher.Dispatch(Cpu, ExceptionCode.Trap, pcBefore + 4, inDelaySlot: true);
                                InstructionCount++;
                                return;
                        }
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

        // ── Bulk byte-fill loop fast-forward ────────────────────────────────────

        /// <summary>
        /// Minimum remaining iterations required before the fast-forward triggers.
        /// Loops shorter than this threshold are cheaper to execute normally.
        /// </summary>
        private const long BulkFillMinIterations = 50_000L;

        /// <summary>
        /// Detect and fast-forward a MIPS tight byte-fill loop of the form:
        /// <code>
        ///   loop:
        ///     addu   addrReg,  baseReg,  offReg      ; address = base + offset
        ///     addiu  cntReg,   cntReg,  -1           ; count--
        ///     sb     fillReg,  0(addrReg)            ; mem[address] = fill_byte
        ///     slt    cmpReg,   thrReg,   cntReg      ; cmpReg = (threshold &lt; count)
        ///     [nop / other non-branch]               ; optional padding
        ///     bne    cmpReg,   $zero,   loop         ; branch back while cmpReg != 0
        ///     addiu  offReg,   offReg,  ±1           ; delay slot: offset++ (ascending)
        ///                                            ;          or offset-- (descending)
        /// </code>
        ///
        /// Both ascending (+1 delay slot) and descending (-1 delay slot) variants are
        /// detected.  This pattern appears in the PS2 BIOS kernel-init routine that fills
        /// approximately 512 MB of virtual address space with 0x20 (space) or 0x30 before
        /// reloading the kernel from ROM.  On the real PS2 the i-cache protects the
        /// executing instructions; in the emulator without an i-cache the loop would
        /// overwrite its own instructions after ~15 M iterations, turning the
        /// counter-decrement into a NOP and making the loop infinite.
        ///
        /// When the pattern is detected and the remaining iteration count exceeds
        /// <see cref="BulkFillMinIterations"/>, all remaining iterations are collapsed
        /// into a single <see cref="System.Array.Fill{T}"/> call on the main RAM
        /// (correct because the fill always wraps through the full 32 MB at least
        /// once), the CPU registers are updated to match the post-loop state, and
        /// the PC is advanced past the loop.
        ///
        /// Returns <c>true</c> if the fast-forward was applied and <see cref="Step"/>
        /// should return immediately; <c>false</c> if the pattern was not recognised.
        /// </summary>
        private bool TryFastForwardByteFill()
        {
            uint pc32 = (uint)(Cpu.PC & 0xFFFFFFFFUL);

            // Only accelerate KSEG0/KSEG1 code (fixed-mapped kernel region).
            if ((pc32 & 0xC0000000u) != 0x80000000u) return false;

            // ── Step 1: verify the BNE instruction at pc32 ──────────────────
            // BNE format: opcode=5, rs=cmpReg, rt=$zero (0), imm=signed offset.
            //
            // IMPORTANT: we use FetchWord (i-cache) rather than Bus.ReadWord (raw RAM)
            // for ALL instruction reads in this detector.  The fill loop overwrites RAM
            // with the fill byte as it runs; by the time the descending variant reaches
            // its own BNE at pc32, the RAM word there already reads back as 0x30303030.
            // The i-cache was populated with the real BNE encoding when the CPU first
            // fetched that line, and it is not invalidated by stores, so FetchWord
            // returns the original (pre-fill) instruction word even after RAM is corrupted.
            // This matches the behaviour of real PS2 hardware, where the i-cache
            // shields the executing code from its own store loop.
            uint bneWord = FetchWord(pc32);
            if ((bneWord >> 26) != 5u) return false;          // must be BNE
            int bneRs = (int)((bneWord >> 21) & 0x1Fu);       // compare register
            if (((bneWord >> 16) & 0x1Fu) != 0u) return false; // rt must be $zero

            // Compute the branch target (loop start).
            // MIPS: target = (pc32 + 4) + sign_extend(imm) × 4
            int  branchImm = (short)(bneWord & 0xFFFFu);
            uint loopStart = (uint)((int)(pc32 + 4u) + branchImm * 4);

            // Sanity: loop start must be before pc32 and within a tight 80-byte window.
            if (loopStart >= pc32 || pc32 - loopStart > 80u) return false;

            // ── Step 2: verify the delay slot (ADDIU offReg, offReg, ±1) ────
            // The delay slot increments (+1, ascending fill) or decrements (-1,
            // descending fill) the offset register each iteration.
            // Use FetchWord (i-cache) — the delay-slot word in RAM may already be
            // the fill byte by the time we get here.
            uint dsWord = FetchWord(pc32 + 4u);
            if ((dsWord >> 26) != 9u) return false;           // must be ADDIU
            int dsRs = (int)((dsWord >> 21) & 0x1Fu);
            int dsRt = (int)((dsWord >> 16) & 0x1Fu);
            if (dsRs != dsRt) return false;                   // addiu rt, rt, imm (rs==rt)
            short dsImm = (short)(dsWord & 0xFFFFu);
            if (dsImm != 1 && dsImm != -1) return false;     // imm must be +1 or -1
            bool ascending = dsImm == 1;                      // true = ascending, false = descending
            int offReg = dsRt;                                 // offset/address register

            // ── Step 3: read the loop-body instructions ──────────────────────
            // Accept 4 to 6 instructions between loopStart and pc32.
            // Use FetchWord (i-cache) for the same reason as above.
            int loopBodyLen = (int)(pc32 - loopStart) / 4;
            if (loopBodyLen < 4 || loopBodyLen > 6) return false;

            uint w0 = FetchWord(loopStart);          // addu
            uint w1 = FetchWord(loopStart + 4u);     // addiu -1
            uint w2 = FetchWord(loopStart + 8u);     // sb
            uint w3 = FetchWord(loopStart + 12u);    // slt

            // w0: ADDU addrReg, baseReg, offReg  (SPECIAL funct=0x21)
            if ((w0 >> 26) != 0u || (w0 & 0x3Fu) != 0x21u) return false;
            int addrReg = (int)((w0 >> 11) & 0x1Fu); // rd (computed address)
            int baseReg = (int)((w0 >> 21) & 0x1Fu); // rs (base register)
            int w0Rt    = (int)((w0 >> 16) & 0x1Fu); // rt (offset register)
            if (w0Rt != offReg) return false;         // addu's rt must match delay-slot rt

            // w1: ADDIU cntReg, cntReg, -1  (opcode=9, rs==rt, imm=0xFFFF=-1)
            if ((w1 >> 26) != 9u) return false;
            int cntRs = (int)((w1 >> 21) & 0x1Fu);
            int cntRt = (int)((w1 >> 16) & 0x1Fu);
            if (cntRs != cntRt) return false;
            if ((short)(w1 & 0xFFFFu) != -1) return false;
            int cntReg = cntRt;

            // w2: SB fillReg, 0(addrReg)  (opcode=0x28, rs=addrReg, imm=0)
            if ((w2 >> 26) != 0x28u) return false;
            if ((int)((w2 >> 21) & 0x1Fu) != addrReg) return false;
            if ((short)(w2 & 0xFFFFu) != 0) return false; // offset must be 0
            int fillReg = (int)((w2 >> 16) & 0x1Fu);

            // w3: SLT cmpReg, thrReg, cntReg  (SPECIAL funct=0x2A)
            if ((w3 >> 26) != 0u || (w3 & 0x3Fu) != 0x2Au) return false;
            int sltRd = (int)((w3 >> 11) & 0x1Fu);
            int sltRt = (int)((w3 >> 16) & 0x1Fu);
            if (sltRd != bneRs) return false;   // SLT result must be what BNE checks
            if (sltRt != cntReg) return false;  // SLT rt must be the counter
            int thrReg = (int)((w3 >> 21) & 0x1Fu); // threshold register

            // ── Step 4: extract register values ─────────────────────────────
            // Treat all registers as signed 32-bit (matching 32-bit addiu semantics).
            int cntVal  = (int)(Cpu.RegsLo[cntReg]  & 0xFFFFFFFFu);
            int thrVal  = (int)(Cpu.RegsLo[thrReg]  & 0xFFFFFFFFu);
            int offVal  = (int)(Cpu.RegsLo[offReg]  & 0xFFFFFFFFu);
            int baseVal = (int)(Cpu.RegsLo[baseReg] & 0xFFFFFFFFu);

            // Remaining iterations: the loop runs while cntVal > thrVal (signed 32-bit).
            // Each iteration decrements cntVal once; exits when cntVal reaches thrVal.
            long remaining = (long)cntVal - (long)thrVal;
            if (remaining <= BulkFillMinIterations) return false;

            // ── Step 5: comprehensive i-cache warm-up before bulk-fill ─────────────────
            //
            // ROOT CAUSE:
            //   TryFastForwardByteFill() collapses 537 M BIOS fill iterations into
            //   Array.Fill().  On real hardware the i-cache is ALREADY populated with
            //   every instruction in the call chain, because the CPU fetched each one
            //   on the way in to the fill loop.  Our fast-forward skips those fetches,
            //   so after Array.Fill the i-cache is sparse and every cache miss returns
            //   the garbage fill-byte (0x20202020 = "addi $zero,$at,0x2020").
            //
            // HOW PCSX2 HANDLES THIS:
            //   PCSX2's JIT (EErec) compiles MIPS blocks to native code before they
            //   run.  When the fill loop executes, every block in the call chain
            //   (epilogue, caller, caller's caller, exception handler) was already
            //   compiled.  The fill overwrites the MIPS RAM but the compiled x86 code
            //   stays valid in the JIT cache until a CACHE IXIN or SMC fault evicts it.
            //   PCSX2's pure interpreter does NOT fast-forward — it runs all 537 M
            //   iterations at ~1 MIPS, which is why PCSX2 requires the JIT for the
            //   BIOS to boot in reasonable time.
            //
            // OUR EQUIVALENT (no JIT):
            //   Before overwriting RAM, explicitly load every 64-byte i-cache line
            //   that post-fill execution will need.  This exactly reproduces the
            //   "naturally-populated" i-cache state that real hardware has when it
            //   reaches the fill loop.  We cover four categories:
            //
            //   (a) Fill function epilogue  — 8 lines (512 B) starting at pc32+4.
            //       These are the instructions executed immediately after BNE exits.
            //
            //   (b) Direct return address   — 16 lines (1 KB) around the $ra register.
            //       After the epilogue, "jr $ra" jumps here.  Without this, the first
            //       instruction the CPU sees after returning is 0x20202020.
            //
            //   (c) Call-stack chain        — scan 2 KB of the stack ($sp) for
            //       word-aligned KSEG0 addresses that look like saved $ra values;
            //       pre-fetch 16 lines (1 KB) around each (up to 32 candidates).
            //       This covers the entire active call chain (grandcaller, great-
            //       grandcaller, etc.) so every function that returns after the fill
            //       executes from the i-cache rather than from corrupted RAM.
            //
            //   (d) All callee-saved GPRs   — pre-fetch 8 lines each around any GPR
            //       value that looks like a KSEG0 code address.  Covers cases where
            //       a saved $ra was kept in a register instead of on the stack.
            //
            //   (e) BEV=0 exception handler — 16 lines (1 KB) at 0x80000080.
            //       If a corrupted instruction after the fill triggers an exception,
            //       the handler must be in cache so the CPU can recover.
            //
            // This matches PCSX2 JIT semantics: every block in the call chain is
            // "compiled" (cached) before the fill runs.

            // Local helpers.

            // Load one 64-byte KSEG0 i-cache line through FetchWord.
            // Non-KSEG0 addresses are silently ignored (they bypass the i-cache).
            void PrefetchLine(uint vaddr)
            {
                if ((vaddr & 0xE0000000u) == 0x80000000u)
                    FetchWord(vaddr & ~0x3Fu);      // align to 64-byte boundary, then fetch
            }

            // Pre-fetch nLines consecutive 64-byte lines starting at the line
            // that contains vaddr.
            void PrefetchRegion(uint vaddr, int nLines)
            {
                uint lineBase = vaddr & ~0x3Fu;
                for (int li = 0; li < nLines; li++)
                    PrefetchLine(lineBase + (uint)(li << 6));
            }

            // (a) Fill function epilogue: 8 lines past the BNE / delay slot.
            PrefetchRegion(pc32 + 4, 8);

            // (b) Direct return address ($ra = register 31): 16 lines (1 KB).
            uint raVaddr = (uint)(Cpu.RegsLo[31] & 0xFFFFFFFFu);
            PrefetchRegion(raVaddr, 16);

            // (c) Call-stack chain: scan 2 KB of the stack for saved return addresses
            //     and pre-fetch 16 lines (1 KB) around each (up to 32 candidates).
            //     We check both KSEG0 (0x80xxxxxx) and KSEG1 (0xA0xxxxxx / 0xBFxxxxxx)
            //     addresses so that BIOS ROM return addresses are also captured.
            uint spVaddr = (uint)(Cpu.RegsLo[29] & 0xFFFFFFFFu);
            uint physSp  = PS2MemoryBus.TranslateAddress(spVaddr)
                           & (uint)(PS2MemoryBus.MainRamSize - 1);
            int  nSavedRa = 0;
            for (uint so = 0; so <= 2048u && nSavedRa < 32; so += 4u)
            {
                uint pa = physSp + so;
                if (pa + 3 >= (uint)Bus.Ram.Length) break;
                uint stackWord = (uint)(Bus.Ram[pa]
                                      | (Bus.Ram[pa + 1] << 8)
                                      | (Bus.Ram[pa + 2] << 16)
                                      | (Bus.Ram[pa + 3] << 24));
                // A saved $ra candidate: KSEG0 (0x80xxxxxx), word-aligned, non-zero.
                if ((stackWord & 0xE0000003u) == 0x80000000u && stackWord != 0x80000000u)
                {
                    PrefetchRegion(stackWord, 16);
                    nSavedRa++;
                }
            }

            // (d) All callee-saved GPRs ($s0–$s7, $fp, plus $a0–$a3, $t0–$t9):
            //     any register holding a KSEG0 code address is likely a saved RA.
            for (int ri = 0; ri < 32; ri++)
            {
                uint gprVal = (uint)(Cpu.RegsLo[ri] & 0xFFFFFFFFu);
                if ((gprVal & 0xE0000003u) == 0x80000000u && gprVal != 0x80000000u)
                    PrefetchRegion(gprVal, 8);
            }

            // (e) BEV=0 general exception handler: 16 lines (1 KB) at 0x80000080.
            //     Pre-fetch unconditionally so any exception after the fill can be
            //     handled by code in the i-cache rather than RAM (which is all 0x20).
            PrefetchRegion(0x80000080u, 16);

            // ── Step 6: perform the memory fill ─────────────────────────────
            // Ascending fill writes:  (baseVal+offVal+1), (baseVal+offVal+2), ..., (baseVal+offVal+remaining)
            // Descending fill writes: (baseVal+offVal-1), (baseVal+offVal-2), ..., (baseVal+offVal-remaining)
            // All translated via KSEG0/KSEG1 → physical RAM (32 MB, mirrored).
            // When remaining >= 32 MB (typical: ~535 M ≫ 32 MB), every byte in
            // main RAM is overwritten regardless of direction; a single Array.Fill covers all cases.
            byte fillByte = (byte)(Cpu.RegsLo[fillReg] & 0xFFu);
            // First store address depends on fill direction.
            uint startVaddr = ascending
                ? (uint)(baseVal + offVal + 1)   // ascending: base + offset + 1
                : (uint)(baseVal + offVal - 1);  // descending: base + offset - 1
            uint physStart  = PS2MemoryBus.TranslateAddress(startVaddr)
                              & (uint)(PS2MemoryBus.MainRamSize - 1);

            // ── Data-cache preservation ──────────────────────────────────────
            // The real EE has an 8 KB write-back data cache.  During the fill
            // loop the store address advances through all of RAM, but recently-
            // accessed regions stay resident in the d-cache and are NOT
            // overwritten from the cache's perspective.
            //
            // Our emulator has no d-cache, so Array.Fill corrupts every byte of
            // RAM including the active call stack and the EE kernel data area.
            // We compensate by saving and restoring two regions:
            //
            //   (A) Stack window  (sp – 64 … sp + 2048):
            //       Preserves saved return addresses, frame pointers and other
            //       spilled values the call chain restores after the fill.
            //       Only saved when $sp is a valid KSEG0/KSEG1 address.
            //
            //   (B) Kernel header (physical 0 … physStart – 1):
            //       When the fill covers all 32 MB (remaining ≥ MainRamSize)
            //       the fill loop wraps around and overwrites the region that
            //       precedes the natural start address.  On real hardware those
            //       bytes are written LAST, and any hot d-cache lines there
            //       remain valid until the loop reads them back.  More
            //       importantly, the EE kernel code and data segment at
            //       physical 0x1000–physStart live here and are relied upon
            //       by the post-fill call chain.  Restoring them matches the
            //       observed PCSX2 behaviour where the kernel survives the fill.
            const int StackSaveWindow = 2048 + 64;
            uint spVirt32 = (uint)(Cpu.RegsLo[29] & 0xFFFFFFFFu);
            bool hasValidStack = (spVirt32 & 0xE0000000u) == 0x80000000u  // KSEG0
                              || (spVirt32 & 0xE0000000u) == 0xA0000000u; // KSEG1
            byte[]? stackSnapshot = null;
            uint    stackSaveBase = 0;
            int     stackSaveLen  = 0;
            if (hasValidStack)
            {
                uint physSpSave = PS2MemoryBus.TranslateAddress(spVirt32)
                                  & (uint)(PS2MemoryBus.MainRamSize - 1);
                stackSaveBase = physSpSave >= 64u ? physSpSave - 64u : 0u;
                stackSaveLen  = (int)Math.Min(
                                    (uint)StackSaveWindow,
                                    (uint)Bus.Ram.Length - stackSaveBase);
                stackSnapshot = new byte[stackSaveLen];
                Bus.Ram.AsSpan((int)stackSaveBase, stackSaveLen).CopyTo(stackSnapshot);
            }

            // Snapshot of the kernel header region [0 .. kernelSnapLen) to
            // protect recently-accessed data from being clobbered by the fill.
            //
            // On real hardware the EE has an 8 KB write-back data cache (d-cache).
            // During a byte-fill loop the CPU issues sequential stores through the
            // d-cache.  Any kernel code or data that was fetched/read shortly
            // before the fill started is still resident in the d-cache and is
            // therefore NOT overwritten from the program's perspective — reads after
            // the fill still see the pre-fill (d-cached) values.  Without a d-cache
            // simulation this emulator would clobber that data.
            //
            // We compensate by saving a snapshot of the first 1 MB (the EE
            // kernel header + module table + data area) whenever:
            //   (a) The fill covers the entire 32 MB (wraps around and writes
            //       the region [0 .. physStart) last), OR
            //   (b) The fill is partial but its range overlaps the kernel area
            //       (physical [0, 1 MB)), meaning physStart < 1 MB for ascending
            //       fills; the fill starts inside the kernel area and the
            //       module-count field at 0x242E0 would otherwise be clobbered
            //       by fill bytes, causing the module-init loop to iterate 537 M
            //       times.
            //
            // 1 MB is chosen to cover the EE kernel module-descriptor table
            // which starts at physical 0x40000 (just past the old 256 KB limit).
            // Without preserving that region the null terminator of the table is
            // wiped out by the fill loop, causing the outer module-init loop at
            // 0x80010530 to run indefinitely.
            //
            // After the bulk fill we restore this snapshot so that subsequent
            // kernel reads see the d-cached (pre-fill) values.
            const int MinKernelPreservation = 1 * 1024 * 1024;
            byte[]? kernelSnapshot = null;
            int     kernelSnapLen  = 0;
            if (remaining >= PS2MemoryBus.MainRamSize)
            {
                // Full fill: preserve at least 1 MB, or physStart bytes if
                // physStart is larger — whichever is greater, capped at RAM size.
                kernelSnapLen  = (int)Math.Min(
                                     Math.Max(physStart, (uint)MinKernelPreservation),
                                     (uint)Bus.Ram.Length);
                kernelSnapshot = new byte[kernelSnapLen];
                Bus.Ram.AsSpan(0, kernelSnapLen).CopyTo(kernelSnapshot);
            }


            if (remaining >= PS2MemoryBus.MainRamSize)
            {
                // Fill covers the entire 32 MB RAM (multiple wrap-arounds).
                Array.Fill(Bus.Ram, fillByte);
            }
            else
            {
                uint count = (uint)remaining;
                if (ascending)
                {
                    // physStart is the lowest address written (first iteration).
                    // Fill upward: [physStart .. physStart+count-1].
                    uint end = physStart + count;
                    if (end <= (uint)PS2MemoryBus.MainRamSize)
                    {
                        // Contiguous range, no wrap.
                        Bus.Ram.AsSpan((int)physStart, (int)count).Fill(fillByte);
                    }
                    else
                    {
                        // Range wraps around the 32 MB upper boundary.
                        int firstChunk = PS2MemoryBus.MainRamSize - (int)physStart;
                        Bus.Ram.AsSpan((int)physStart, firstChunk).Fill(fillByte);
                        Bus.Ram.AsSpan(0, (int)(count - (uint)firstChunk)).Fill(fillByte);
                    }
                }
                else
                {
                    // Descending: physStart is the highest address written (first iteration).
                    // The last address written is physStart - count + 1 (wrapping if needed).
                    // The resulting SET of bytes is the same as filling upward from the low end,
                    // so we compute the low end and issue a single contiguous Span.Fill.
                    if (physStart + 1u >= count)
                    {
                        // No wrap past address 0.
                        uint physLow = physStart - count + 1u;
                        Bus.Ram.AsSpan((int)physLow, (int)count).Fill(fillByte);
                    }
                    else
                    {
                        // Range wraps below address 0 (into the top of the 32 MB space).
                        int lowChunk  = (int)(physStart + 1u); // bytes [0..physStart]
                        int highChunk = (int)(count - (uint)lowChunk);
                        Bus.Ram.AsSpan(0, lowChunk).Fill(fillByte);
                        Bus.Ram.AsSpan(PS2MemoryBus.MainRamSize - highChunk, highChunk).Fill(fillByte);
                    }
                }
            }

            // Restore the stack window so that saved return addresses remain
            // correct — matching the d-cache behaviour of real hardware.
            if (stackSnapshot != null)
                stackSnapshot.CopyTo(Bus.Ram.AsSpan((int)stackSaveBase, stackSaveLen));

            // Restore the kernel header region so that post-fill code can load
            // function pointers and data structures from RAM as on real hardware.
            if (kernelSnapshot != null)
                kernelSnapshot.CopyTo(Bus.Ram.AsSpan(0, kernelSnapLen));

            // ── Step 7: update CPU registers to post-loop state ──────────────
            // After the last iteration (BNE not taken, delay slot still fires):
            //   cntReg = thrVal
            //   offReg = offVal + remaining + 1  (ascending: +1 from final delay slot)
            //           or offVal - remaining - 1  (descending: -1 from final delay slot)
            //   addrReg: overwritten by SLT → 0 (only if addrReg == sltRd)
            //   sltRd   = 0           (SLT result: threshold < threshold = false)
            //   PC = pc32 + 8         (past BNE + delay slot)
            long finalOffDelta = ascending ? (remaining + 1L) : -(remaining + 1L);
            CpuHelpers.SetReg32(Cpu, cntReg, thrVal);
            CpuHelpers.SetReg32(Cpu, offReg, (int)(offVal + finalOffDelta));
            CpuHelpers.SetReg32(Cpu, sltRd,  0);
            // If addrReg and sltRd are different registers, addrReg holds the final
            // address from the last addu (base + final offset).  Write it for accuracy.
            if (addrReg != sltRd)
            {
                long finalOff = ascending ? (long)(offVal + remaining) : (long)(offVal - remaining);
                CpuHelpers.SetReg32(Cpu, addrReg, (int)(uint)(baseVal + (int)finalOff));
            }

            Cpu.PC = pc32 + 8u;

            // Count each collapsed iteration as one instruction for statistics.
            InstructionCount += (ulong)remaining;
            return true;
        }


        /// <summary>
        /// Attempts to fast-forward a BIOS BNEL byte-fill loop that uses an ascending
        /// counter and a stack-loaded bound.  The recognised pattern is:
        /// <code>
        ///   loop:
        ///     addiu  cntReg,   cntReg,   +1           ; cnt++
        ///     sb     fillReg,  0(addrReg)             ; mem[addrReg] = fill_byte
        ///     lw     tmpReg,   spImm($sp)             ; tmpReg = bound (from stack)
        ///     slt    cmpReg,   cntReg,  tmpReg        ; cmpReg = (cnt &lt; bound)
        ///     [optional nop]
        ///     bnel   cmpReg,   $zero,   loop          ; loop while cmpReg != 0
        ///     addiu  addrReg,  addrReg, -1            ; delay slot: addr-- (nullified when not taken)
        /// </code>
        ///
        /// BNEL nullifies the delay slot when the branch is not taken (final iteration),
        /// so addrReg decrements <c>remaining</c> times total (once per taken branch),
        /// unlike BNE which always fires its delay slot.
        ///
        /// This pattern appears in the PS2 BIOS after the main ASCII-fill loop and fills
        /// the kernel data area with 0x30.  The fill range overlaps the EE kernel at
        /// physical 0x1000; the kernel is preserved via snapshot save/restore exactly as
        /// in <see cref="TryFastForwardByteFill"/>.
        /// </summary>
        private bool TryFastForwardBnelByteFill()
        {
            uint pc32 = (uint)(Cpu.PC & 0xFFFFFFFFUL);

            // Only accelerate KSEG0/KSEG1 code (fixed-mapped kernel region).
            if ((pc32 & 0xC0000000u) != 0x80000000u) return false;

            // ── Step 1: verify BNEL instruction at pc32 ───────────────────────
            // BNEL opcode = 0x15, rs = cmpReg, rt = $zero, imm = signed offset.
            // Uses FetchWord (i-cache) so that RAM corruption by previous fill
            // iterations does not interfere with pattern detection.
            uint bnelWord = FetchWord(pc32);
            if ((bnelWord >> 26) != 0x15u) return false;          // must be BNEL
            int bneRs = (int)((bnelWord >> 21) & 0x1Fu);
            if (((bnelWord >> 16) & 0x1Fu) != 0u) return false;   // rt must be $zero

            int  branchImm = (short)(bnelWord & 0xFFFFu);
            uint loopStart = (uint)((int)(pc32 + 4u) + branchImm * 4);
            if (loopStart >= pc32 || pc32 - loopStart > 80u) return false;

            // ── Step 2: verify delay slot (ADDIU addrReg, addrReg, -1) ────────
            // BNEL nullifies this on the last (non-taken) iteration.
            uint dsWord = FetchWord(pc32 + 4u);
            if ((dsWord >> 26) != 9u) return false;             // must be ADDIU
            int dsRs = (int)((dsWord >> 21) & 0x1Fu);
            int dsRt = (int)((dsWord >> 16) & 0x1Fu);
            if (dsRs != dsRt) return false;                     // rs == rt
            if ((short)(dsWord & 0xFFFFu) != -1) return false; // imm must be -1
            int addrReg = dsRt;

            // ── Step 3: read and validate loop body (4–6 instructions) ────────
            int loopBodyLen = (int)(pc32 - loopStart) / 4;
            if (loopBodyLen < 4 || loopBodyLen > 6) return false;

            uint w0 = FetchWord(loopStart);
            uint w1 = FetchWord(loopStart + 4u);
            uint w2 = FetchWord(loopStart + 8u);
            uint w3 = FetchWord(loopStart + 12u);

            // w0: ADDIU cntReg, cntReg, +1  (opcode=9, rs==rt, imm=+1)
            if ((w0 >> 26) != 9u) return false;
            int w0Rs = (int)((w0 >> 21) & 0x1Fu);
            int w0Rt = (int)((w0 >> 16) & 0x1Fu);
            if (w0Rs != w0Rt) return false;
            if ((short)(w0 & 0xFFFFu) != 1) return false;
            int cntReg = w0Rt;

            // w1: SB fillReg, 0(addrReg)  (opcode=0x28, base=addrReg, imm=0)
            if ((w1 >> 26) != 0x28u) return false;
            if ((int)((w1 >> 21) & 0x1Fu) != addrReg) return false;
            if ((short)(w1 & 0xFFFFu) != 0) return false;
            int fillReg = (int)((w1 >> 16) & 0x1Fu);

            // w2: LW boundTmpReg, spImm($sp)  (opcode=0x23, base=$sp=29)
            if ((w2 >> 26) != 0x23u) return false;
            if ((int)((w2 >> 21) & 0x1Fu) != 29) return false; // base must be $sp
            int  boundTmpReg = (int)((w2 >> 16) & 0x1Fu);
            short spImm      = (short)(w2 & 0xFFFFu);

            // w3: SLT cmpReg, cntReg, boundTmpReg  (SPECIAL funct=0x2A)
            if ((w3 >> 26) != 0u || (w3 & 0x3Fu) != 0x2Au) return false;
            int sltRd = (int)((w3 >> 11) & 0x1Fu);
            if ((int)((w3 >> 21) & 0x1Fu) != cntReg)      return false; // rs = cntReg
            if ((int)((w3 >> 16) & 0x1Fu) != boundTmpReg) return false; // rt = boundTmpReg
            if (sltRd != bneRs) return false; // SLT result must be what BNEL checks

            // ── Step 4: extract values and compute remaining iterations ────────
            int cntVal  = (int)(Cpu.RegsLo[cntReg]  & 0xFFFFFFFFu);
            int addrVal = (int)(Cpu.RegsLo[addrReg] & 0xFFFFFFFFu);

            // Load bound from the stack (same address the LW reads each iteration).
            uint spVal      = (uint)(Cpu.RegsLo[29] & 0xFFFFFFFFu);
            uint boundVaddr = (uint)((int)spVal + spImm);
            int  boundVal   = (int)Bus.ReadWord(boundVaddr);

            // Remaining: loop runs while (cntVal < boundVal) after body increments cntVal.
            // At pc32, the body has just run (cntVal already incremented for this iteration).
            long remaining = (long)boundVal - (long)cntVal;
            if (remaining <= BulkFillMinIterations) return false;

            // ── Step 5: comprehensive i-cache warm-up before bulk-fill ─────────
            // Same logic as TryFastForwardByteFill: pre-load every cache line that
            // post-fill execution will need, including the BNEL loop body itself.
            void PrefetchLine(uint vaddr)
            {
                if ((vaddr & 0xE0000000u) == 0x80000000u)
                    FetchWord(vaddr & ~0x3Fu);
            }
            void PrefetchRegion(uint vaddr, int nLines)
            {
                uint lineBase = vaddr & ~0x3Fu;
                for (int li = 0; li < nLines; li++)
                    PrefetchLine(lineBase + (uint)(li << 6));
            }

            PrefetchRegion(loopStart, 2);         // loop body (covers loopStart through pc32+4)
            PrefetchRegion(pc32 + 4, 8);          // epilogue past BNEL + delay slot
            PrefetchRegion((uint)(Cpu.RegsLo[31] & 0xFFFFFFFFu), 16); // $ra
            uint physSp2 = PS2MemoryBus.TranslateAddress((uint)(Cpu.RegsLo[29] & 0xFFFFFFFFu))
                           & (uint)(PS2MemoryBus.MainRamSize - 1);
            int nSavedRa2 = 0;
            for (uint so = 0u; so <= 2048u && nSavedRa2 < 32; so += 4u)
            {
                uint pa = physSp2 + so;
                if (pa + 3u >= (uint)Bus.Ram.Length) break;
                uint sw = (uint)(Bus.Ram[pa] | (Bus.Ram[pa+1]<<8)
                                             | (Bus.Ram[pa+2]<<16)
                                             | (Bus.Ram[pa+3]<<24));
                if ((sw & 0xE0000003u) == 0x80000000u && sw != 0x80000000u)
                { PrefetchRegion(sw, 16); nSavedRa2++; }
            }
            for (int ri = 0; ri < 32; ri++)
            {
                uint gv = (uint)(Cpu.RegsLo[ri] & 0xFFFFFFFFu);
                if ((gv & 0xE0000003u) == 0x80000000u && gv != 0x80000000u)
                    PrefetchRegion(gv, 8);
            }
            PrefetchRegion(0x80000080u, 16); // BEV=0 exception handler

            // ── Step 6: perform the fill with stack/kernel snapshot save/restore ─
            // From pc32, the remaining 'remaining' taken BNELs each:
            //   - fire the delay slot (addrReg--)
            //   - execute the body: cntReg++; SB at addrReg; LW bound; SLT
            // So the stores go to addrVal-1, addrVal-2, ..., addrVal-remaining.
            // (addrVal was already stored to by the current iteration's SB before pc32.)
            byte fillByte = (byte)(Cpu.RegsLo[fillReg] & 0xFFu);
            // physStart = physical address of the first store from pc32 (= addrVal-1).
            uint physStart = PS2MemoryBus.TranslateAddress((uint)(addrVal - 1))
                             & (uint)(PS2MemoryBus.MainRamSize - 1);

            // Stack snapshot.
            const int StackSaveWindow = 2048 + 64;
            uint spVirt32 = (uint)(Cpu.RegsLo[29] & 0xFFFFFFFFu);
            bool hasValidStack = (spVirt32 & 0xE0000000u) == 0x80000000u
                              || (spVirt32 & 0xE0000000u) == 0xA0000000u;
            byte[]? stackSnapshot = null;
            uint    stackSaveBase = 0;
            int     stackSaveLen  = 0;
            if (hasValidStack)
            {
                uint physSpSave = PS2MemoryBus.TranslateAddress(spVirt32)
                                  & (uint)(PS2MemoryBus.MainRamSize - 1);
                stackSaveBase = physSpSave >= 64u ? physSpSave - 64u : 0u;
                stackSaveLen  = (int)Math.Min(
                                    (uint)StackSaveWindow,
                                    (uint)Bus.Ram.Length - stackSaveBase);
                stackSnapshot = new byte[stackSaveLen];
                Bus.Ram.AsSpan((int)stackSaveBase, stackSaveLen).CopyTo(stackSnapshot);
            }

            // Kernel snapshot: the BNEL fill descends from addrVal-1 potentially into
            // the EE kernel area (physical < 1 MB).  Save a snapshot so the kernel
            // module table (starting at physical 0x40000) and other kernel data
            // survive the fill, matching the real EE's d-cache behaviour.
            const int MinKernelPreservation = 1 * 1024 * 1024;
            byte[]? kernelSnapshot = null;
            int     kernelSnapLen  = 0;
            long physLow = (long)physStart - remaining + 1L;
            if (remaining >= PS2MemoryBus.MainRamSize || physLow < MinKernelPreservation)
            {
                kernelSnapLen  = (int)Math.Min(
                                     Math.Max(physStart, (uint)MinKernelPreservation),
                                     (uint)Bus.Ram.Length);
                kernelSnapshot = new byte[kernelSnapLen];
                Bus.Ram.AsSpan(0, kernelSnapLen).CopyTo(kernelSnapshot);
            }

            // Execute the fill: descending from physStart for 'remaining' bytes.
            {
                uint count = (uint)remaining;
                if (remaining >= PS2MemoryBus.MainRamSize)
                {
                    Array.Fill(Bus.Ram, fillByte);
                }
                else if (physStart + 1u >= count)
                {
                    Bus.Ram.AsSpan((int)(physStart - count + 1u), (int)count).Fill(fillByte);
                }
                else
                {
                    int lowChunk  = (int)(physStart + 1u);
                    int highChunk = (int)(count - (uint)lowChunk);
                    Bus.Ram.AsSpan(0, lowChunk).Fill(fillByte);
                    Bus.Ram.AsSpan(PS2MemoryBus.MainRamSize - highChunk, highChunk).Fill(fillByte);
                }
            }

            if (stackSnapshot != null)
                stackSnapshot.CopyTo(Bus.Ram.AsSpan((int)stackSaveBase, stackSaveLen));
            if (kernelSnapshot != null)
                kernelSnapshot.CopyTo(Bus.Ram.AsSpan(0, kernelSnapLen));

            // ── Step 7: update CPU registers to post-loop state ──────────────
            // BNEL semantics: delay slot fires for every taken branch ('remaining' times),
            // and is nullified for the single not-taken exit.
            //   cntReg  = boundVal          (incremented 'remaining' more times in body)
            //   addrReg = addrVal - remaining  (decremented 'remaining' times by delay slot)
            //   sltRd   = 0                 (final SLT: cnt >= bound → false)
            //   PC      = pc32 + 8          (past BNEL + delay slot)
            CpuHelpers.SetReg32(Cpu, cntReg,  boundVal);
            CpuHelpers.SetReg32(Cpu, addrReg, (int)(addrVal - remaining));
            CpuHelpers.SetReg32(Cpu, sltRd,   0);
            Cpu.PC = pc32 + 8u;

            InstructionCount += (ulong)remaining;
            return true;
        }

        // ── Byte-copy loop fast-forward ──────────────────────────────────────────

        /// <summary>
        /// Detect and fast-forward a MIPS quadword zero-fill loop of the form:
        /// <code>
        ///   preamble (before loop):
        ///     por    cmpReg, $zero, $zero     ; cmpReg = 0 (entry setup)
        ///
        ///   loop:  (loopStart)
        ///     sq     cmpReg,  imm(addrReg)    ; store 16 zero bytes
        ///     addiu  addrReg, addrReg, 0x10   ; addrReg += 16
        ///     sltu   cmpReg,  addrReg, limReg ; cmpReg = (addrReg &lt; limReg)
        ///     [nop]*                           ; zero or more NOPs
        ///     bne    cmpReg,  $zero,  loop    ; loop if addrReg &lt; limReg
        ///     por    cmpReg,  $zero,  $zero   ; delay slot: cmpReg = 0 (zeroed before sq)
        /// </code>
        ///
        /// The delay slot <c>por cmpReg, $zero, $zero</c> zeroes <c>cmpReg</c> before
        /// it is used by <c>sq</c> on the next iteration, so every store writes 128
        /// bits of zero.  This pattern appears in the PS2 BIOS kernel-init routine
        /// that zeroes all 32 MB of main RAM (virtual 0x80000000–0x81FFFFFF) 16 bytes
        /// at a time before loading the EE kernel from ROM.
        ///
        /// When the remaining iteration count (in units of 16-byte quadwords) exceeds
        /// <see cref="BulkFillMinIterations"/>, all remaining stores are collapsed into
        /// a single <see cref="System.Array.Clear"/> call, with the same
        /// stack/kernel-snapshot save-and-restore strategy used by
        /// <see cref="TryFastForwardByteFill"/> to preserve live data that the
        /// emulated d-cache would otherwise protect.
        /// </summary>
        private bool TryFastForwardQuadZeroFill()
        {
            uint pc32 = (uint)(Cpu.PC & 0xFFFFFFFFUL);

            // Only accelerate KSEG0/KSEG1 code (fixed-mapped kernel region).
            if ((pc32 & 0xC0000000u) != 0x80000000u) return false;

            // ── Step 1: verify BNE at pc32 ─────────────────────────────────────
            // BNE opcode=5; rt must be $zero; branch must be backward.
            uint bneWord = FetchWord(pc32);
            if ((bneWord >> 26) != 5u) return false;
            int bneRs = (int)((bneWord >> 21) & 0x1Fu);       // cmpReg
            if (((bneWord >> 16) & 0x1Fu) != 0u) return false; // rt must be $zero
            int  branchImm = (short)(bneWord & 0xFFFFu);
            uint loopStart = (uint)((int)(pc32 + 4u) + branchImm * 4);
            if (loopStart >= pc32 || pc32 - loopStart > 32u) return false; // max 8 body insns

            // ── Step 2: verify delay slot is POR cmpReg, $zero, $zero ─────────
            // POR encoding: opcode=0x1C (MMI), funct=0x29 (MMI3), shamt=0x12 (POR),
            //               rs=0, rt=0; rd=cmpReg.
            uint dsWord = FetchWord(pc32 + 4u);
            if ((dsWord >> 26) != 0x1Cu) return false;         // MMI opcode
            if ((dsWord & 0x3Fu) != 0x29u) return false;       // MMI3/POR funct
            if (((dsWord >> 6) & 0x1Fu) != 0x12u) return false; // POR sub-opcode
            if (((dsWord >> 21) & 0x1Fu) != 0u) return false;  // rs = $zero
            if (((dsWord >> 16) & 0x1Fu) != 0u) return false;  // rt = $zero
            if ((int)((dsWord >> 11) & 0x1Fu) != bneRs) return false; // rd = cmpReg

            // ── Step 3: read and validate loop body ─────────────────────────────
            // Require exactly 3–8 instructions between loopStart and pc32.
            int loopBodyLen = (int)(pc32 - loopStart) / 4;
            if (loopBodyLen < 3 || loopBodyLen > 8) return false;

            uint w0 = FetchWord(loopStart);           // sq
            uint w1 = FetchWord(loopStart + 4u);      // addiu
            uint w2 = FetchWord(loopStart + 8u);      // sltu

            // w0: SQ cmpReg, imm(addrReg)  (opcode=0x1F)
            if ((w0 >> 26) != 0x1Fu) return false;
            int sqBase = (int)((w0 >> 21) & 0x1Fu);   // addrReg
            int sqRt   = (int)((w0 >> 16) & 0x1Fu);   // stored register = cmpReg
            if (sqRt != bneRs) return false;           // must store cmpReg (zeroed by delay slot)
            short sqImm = (short)(w0 & 0xFFFFu);

            // w1: ADDIU addrReg, addrReg, 0x10  (opcode=9, rs==rt, imm=+16)
            if ((w1 >> 26) != 9u) return false;
            int addRs = (int)((w1 >> 21) & 0x1Fu);
            int addRt = (int)((w1 >> 16) & 0x1Fu);
            if (addRs != addRt) return false;                     // in-place addiu
            if ((short)(w1 & 0xFFFFu) != 16) return false;       // imm must be +16
            int addrReg = addRt;
            if (sqBase != addrReg) return false;                  // sq base = addiu reg

            // w2: SLTU cmpReg, addrReg, limReg  (SPECIAL, funct=0x2B)
            if ((w2 >> 26) != 0u) return false;                   // SPECIAL
            if ((w2 & 0x3Fu) != 0x2Bu) return false;             // SLTU
            int sltuRd = (int)((w2 >> 11) & 0x1Fu);              // result
            int sltuRs = (int)((w2 >> 21) & 0x1Fu);              // checked address reg
            int sltuRt = (int)((w2 >> 16) & 0x1Fu);              // limit reg
            if (sltuRd != bneRs) return false;                    // SLTU result drives BNE
            if (sltuRs != addrReg) return false;                  // SLTU rs = addrReg
            int limReg = sltuRt;

            // Remaining instructions (if any) up to pc32 must be NOPs.
            for (int i = 3; i < loopBodyLen; i++)
            {
                if (FetchWord(loopStart + (uint)(i * 4)) != 0u) return false;
            }

            // ── Step 4: extract register values and compute remaining iters ────
            uint addrVal = (uint)(Cpu.RegsLo[addrReg] & 0xFFFFFFFFu);
            uint limVal  = (uint)(Cpu.RegsLo[limReg]  & 0xFFFFFFFFu);

            // Loop runs while addrVal < limVal (unsigned 32-bit).
            // Each iteration increments addrVal by 16.
            if (addrVal >= limVal) return false;
            long remaining = ((long)limVal - (long)addrVal) / 16L;
            if (remaining <= BulkFillMinIterations) return false;

            // ── Step 5: comprehensive i-cache warm-up ─────────────────────────
            void PrefetchLine(uint vaddr)
            {
                if ((vaddr & 0xE0000000u) == 0x80000000u)
                    FetchWord(vaddr & ~0x3Fu);
            }
            void PrefetchRegion(uint vaddr, int nLines)
            {
                uint lineBase = vaddr & ~0x3Fu;
                for (int li = 0; li < nLines; li++)
                    PrefetchLine(lineBase + (uint)(li << 6));
            }

            PrefetchRegion(loopStart, 2);
            PrefetchRegion(pc32 + 4, 8);
            PrefetchRegion((uint)(Cpu.RegsLo[31] & 0xFFFFFFFFu), 16); // $ra

            uint physSpQ = PS2MemoryBus.TranslateAddress((uint)(Cpu.RegsLo[29] & 0xFFFFFFFFu))
                           & (uint)(PS2MemoryBus.MainRamSize - 1);
            int nSavedRaQ = 0;
            for (uint so = 0u; so <= 2048u && nSavedRaQ < 32; so += 4u)
            {
                uint pa = physSpQ + so;
                if (pa + 3u >= (uint)Bus.Ram.Length) break;
                uint sw = (uint)(Bus.Ram[pa] | (Bus.Ram[pa + 1] << 8)
                                             | (Bus.Ram[pa + 2] << 16)
                                             | (Bus.Ram[pa + 3] << 24));
                if ((sw & 0xE0000003u) == 0x80000000u && sw != 0x80000000u)
                { PrefetchRegion(sw, 16); nSavedRaQ++; }
            }
            for (int ri = 0; ri < 32; ri++)
            {
                uint gv = (uint)(Cpu.RegsLo[ri] & 0xFFFFFFFFu);
                if ((gv & 0xE0000003u) == 0x80000000u && gv != 0x80000000u)
                    PrefetchRegion(gv, 8);
            }
            PrefetchRegion(0x80000080u, 16); // BEV=0 exception handler

            // ── Step 6: save stack and kernel snapshots ────────────────────────
            const int StackSaveWindowQ = 2048 + 64;
            uint spVirt32Q = (uint)(Cpu.RegsLo[29] & 0xFFFFFFFFu);
            bool hasValidStackQ = (spVirt32Q & 0xE0000000u) == 0x80000000u
                                || (spVirt32Q & 0xE0000000u) == 0xA0000000u;
            byte[]? stackSnapshotQ = null;
            uint    stackSaveBaseQ = 0;
            int     stackSaveLenQ  = 0;
            if (hasValidStackQ)
            {
                uint physSpSaveQ = PS2MemoryBus.TranslateAddress(spVirt32Q)
                                   & (uint)(PS2MemoryBus.MainRamSize - 1);
                stackSaveBaseQ = physSpSaveQ >= 64u ? physSpSaveQ - 64u : 0u;
                stackSaveLenQ  = (int)Math.Min(
                                     (uint)StackSaveWindowQ,
                                     (uint)Bus.Ram.Length - stackSaveBaseQ);
                stackSnapshotQ = new byte[stackSaveLenQ];
                Bus.Ram.AsSpan((int)stackSaveBaseQ, stackSaveLenQ).CopyTo(stackSnapshotQ);
            }

            // Physical address of the first store (addrVal + sqImm).
            uint physStartQ = PS2MemoryBus.TranslateAddress((uint)((int)addrVal + sqImm))
                              & (uint)(PS2MemoryBus.MainRamSize - 1);

            // Debug: track physical 0x242E0

            const int MinKernelPreservationQ = 1 * 1024 * 1024;
            byte[]? kernelSnapshotQ = null;
            int     kernelSnapLenQ  = 0;
            long totalBytes = remaining * 16L;
            if (totalBytes >= PS2MemoryBus.MainRamSize || physStartQ < MinKernelPreservationQ)
            {
                kernelSnapLenQ  = (int)Math.Min(
                                      Math.Max(physStartQ, (uint)MinKernelPreservationQ),
                                      (uint)Bus.Ram.Length);
                kernelSnapshotQ = new byte[kernelSnapLenQ];
                Bus.Ram.AsSpan(0, kernelSnapLenQ).CopyTo(kernelSnapshotQ);
            }

            // ── Step 7: perform the zero fill ─────────────────────────────────
            // The loop fills [addrVal+sqImm, limVal+sqImm) with zeros, 16 bytes/iter.
            uint physEndQ = PS2MemoryBus.TranslateAddress((uint)((int)limVal + sqImm))
                            & (uint)(PS2MemoryBus.MainRamSize - 1);

            if (totalBytes >= PS2MemoryBus.MainRamSize)
            {
                Array.Clear(Bus.Ram);
            }
            else if (physStartQ <= physEndQ)
            {
                // Contiguous range, no wrap.
                int fillLen = (int)(physEndQ - physStartQ);
                if (fillLen > 0)
                    Bus.Ram.AsSpan((int)physStartQ, fillLen).Clear();
            }
            else
            {
                // Wraps around the 32 MB boundary.
                Bus.Ram.AsSpan((int)physStartQ, PS2MemoryBus.MainRamSize - (int)physStartQ).Clear();
                Bus.Ram.AsSpan(0, (int)physEndQ).Clear();
            }

            // Restore snapshots.
            if (stackSnapshotQ != null)
                stackSnapshotQ.CopyTo(Bus.Ram.AsSpan((int)stackSaveBaseQ, stackSaveLenQ));
            if (kernelSnapshotQ != null)
                kernelSnapshotQ.CopyTo(Bus.Ram.AsSpan(0, kernelSnapLenQ));

            // ── Step 8: update CPU registers to post-loop state ───────────────
            // After the last BNE not taken + delay slot fired:
            //   addrReg = limVal
            //   cmpReg  = 0 (SLTU result 0, and delay slot por also zeroes it)
            //   PC      = pc32 + 8
            CpuHelpers.SetReg32(Cpu, addrReg, (int)limVal);
            // cmpReg (= bneRs) gets zeroed by delay slot por; set both halves to 0.
            if (bneRs != 0) { Cpu.RegsLo[bneRs] = 0UL; Cpu.RegsHi[bneRs] = 0UL; }
            Cpu.PC = pc32 + 8u;

            InstructionCount += (ulong)remaining;
            return true;
        }


        /// <summary>
        /// Fast-forward the PS2 BIOS kernel linear-search loop.
        ///
        /// Pattern detected at <c>pc32</c> (a BNE instruction):
        /// <code>
        ///   [pc-4]:  lw    entryReg,  0(ptrReg)          ; load table entry
        ///   [pc]:    bne   entryReg,  targetReg, +k       ; skip if no match
        ///   [pc+4]:  addiu cntReg,    cntReg,    +1       ; match: count++
        ///   [pc+8]:  jr    ra                             ; return immediately
        ///   [pc+12]: addiu entryReg,  $zero,     +1       ; delay slot: rv = 1
        ///   [pc+4k]: sltu  entryReg,  cntReg,  maxReg    ; bounds-check
        ///   [pc+4k+4]: bne entryReg,  $zero, [pc-4]      ; loop-back
        ///   [pc+4k+8]: addiu ptrReg,  ptrReg,    +8      ; advance (delay slot)
        /// </code>
        ///
        /// On real PS2 hardware the search target is always present in the table
        /// (small valid module table), so the first match terminates the loop
        /// quickly.  With corrupted BIOS data the table can be 537 million entries
        /// deep and the target never appears, creating an infinite loop that never
        /// exits (the loop condition is <c>cntReg &lt; maxReg</c> where
        /// <c>cntReg</c> only increments on a match, so it stays at 0 forever).
        ///
        /// This fast-forward scans the table directly in C# (up to
        /// <see cref="LinearSearchMaxEntries"/> entries), then either returns the
        /// found-entry result or fakes a not-found return so execution can continue.
        /// </summary>
        private bool TryFastForwardLinearSearch()
        {
            uint pc32 = (uint)(Cpu.PC & 0xFFFFFFFFUL);
            if ((pc32 & 0xC0000000u) != 0x80000000u) return false;

            // ── Step 1: BNE entryReg, targetReg, +k (forward skip) ──────────────
            uint w0 = FetchWord(pc32);
            if ((w0 >> 26) != 5u) return false;        // opcode must be BNE
            int   entryReg  = (int)((w0 >> 21) & 0x1Fu);
            int   targetReg = (int)((w0 >> 16) & 0x1Fu);
            short bneImm    = (short)(w0 & 0xFFFFu);
            if (targetReg == 0)  return false;          // BNE vs zero → different fast-forward
            if (bneImm    <= 0)  return false;          // must branch forward (skip match path)

            // ── Step 2: ADDIU cntReg, cntReg, +1 ────────────────────────────────
            uint w1 = FetchWord(pc32 + 4u);
            if ((w1 >> 26) != 9u) return false;
            int cntReg = (int)((w1 >> 16) & 0x1Fu);
            if ((int)((w1 >> 21) & 0x1Fu) != cntReg) return false;  // rs == rt
            if ((short)(w1 & 0xFFFFu) != 1)          return false;  // imm = +1

            // ── Step 3: JR ra ────────────────────────────────────────────────────
            uint w2 = FetchWord(pc32 + 8u);
            if ((w2 >> 26) != 0u || (w2 & 0x3Fu) != 8u)  return false;  // SPECIAL/JR
            if ((int)((w2 >> 21) & 0x1Fu) != 31)          return false;  // rs must be $ra

            // ── Step 4: ADDIU entryReg, $zero, +1 (delay slot of JR) ────────────
            uint w3 = FetchWord(pc32 + 12u);
            if ((w3 >> 26) != 9u)                         return false;
            if ((int)((w3 >> 21) & 0x1Fu) != 0)           return false;  // rs = $zero
            if ((int)((w3 >> 16) & 0x1Fu) != entryReg)   return false;  // rt = entryReg
            if ((short)(w3 & 0xFFFFu) != 1)              return false;  // imm = +1

            // BNE skip target: the SLTU instruction
            uint sltPC = (uint)((int)(pc32 + 4u) + (int)bneImm * 4);

            // ── Step 5: SLTU entryReg, cntReg, maxReg ───────────────────────────
            uint w4 = FetchWord(sltPC);
            if ((w4 >> 26) != 0u || (w4 & 0x3Fu) != 0x2Bu) return false;  // SLTU
            if ((int)((w4 >> 11) & 0x1Fu) != entryReg)      return false;  // rd
            if ((int)((w4 >> 21) & 0x1Fu) != cntReg)        return false;  // rs
            int maxReg = (int)((w4 >> 16) & 0x1Fu);

            // ── Step 6: BNE entryReg, $zero, backward (loop-back) ───────────────
            uint w5 = FetchWord(sltPC + 4u);
            if ((w5 >> 26) != 5u)                            return false;
            if ((int)((w5 >> 21) & 0x1Fu) != entryReg)      return false;
            if ((int)((w5 >> 16) & 0x1Fu) != 0)             return false;  // rt = $zero
            short loopImm = (short)(w5 & 0xFFFFu);
            if (loopImm >= 0) return false;  // must be backward
            uint loopTop = (uint)((int)(sltPC + 8u) + (int)loopImm * 4);
            if (loopTop != pc32 - 4u) return false;  // loop-top must be LW before current BNE

            // ── Step 7: ADDIU ptrReg, ptrReg, +8 (delay slot of loop BNE) ───────
            uint w6 = FetchWord(sltPC + 8u);
            if ((w6 >> 26) != 9u) return false;
            int ptrReg = (int)((w6 >> 16) & 0x1Fu);
            if ((int)((w6 >> 21) & 0x1Fu) != ptrReg)  return false;  // rs == rt
            if ((short)(w6 & 0xFFFFu) != 8)           return false;  // imm = +8

            // ── Step 8: LW entryReg, 0(ptrReg) at loop-top ──────────────────────
            uint lwWord = FetchWord(loopTop);
            if ((lwWord >> 26) != 0x23u)                     return false;  // LW
            if ((int)((lwWord >> 16) & 0x1Fu) != entryReg)  return false;  // rt
            if ((int)((lwWord >> 21) & 0x1Fu) != ptrReg)    return false;  // base
            if ((short)(lwWord & 0xFFFFu) != 0)             return false;  // offset = 0

            // ── Pattern matched.  Extract loop state. ────────────────────────────
            uint cntVal    = (uint)(Cpu.RegsLo[cntReg]    & 0xFFFFFFFFu);
            uint maxVal    = (uint)(Cpu.RegsLo[maxReg]    & 0xFFFFFFFFu);
            uint ptrVirt   = (uint)(Cpu.RegsLo[ptrReg]    & 0xFFFFFFFFu);
            uint targetVal = (uint)(Cpu.RegsLo[targetReg] & 0xFFFFFFFFu);

            // cntReg only increments on a match (which immediately returns via jr ra).
            // So when we reach this BNE, cntVal is always 0 for the "not found so far"
            // path.  The loop exit condition cntVal >= maxVal can never trigger unless
            // a match is found and the caller re-enters.  Treat remaining as the cap.
            uint remaining = maxVal - cntVal;
            if (remaining < BulkFillMinIterations) return false;

            // We are at the BNE: the entry at ptrVirt has already failed.
            // Scan entries at ptrVirt+8, ptrVirt+16, … up to the cap.
            const uint LinearSearchMaxEntries = 65536u;
            uint entriesToScan = Math.Min(remaining - 1u, LinearSearchMaxEntries);

            bool found    = false;
            uint foundPtr = 0u;
            for (uint i = 0; i < entriesToScan; i++)
            {
                uint entryVirt = ptrVirt + 8u + i * 8u;
                uint entryPhys = PS2MemoryBus.TranslateAddress(entryVirt);
                uint entryWord;
                if (entryPhys + 4u <= (uint)Bus.Ram.Length)
                    entryWord = System.Runtime.InteropServices.MemoryMarshal
                                    .Read<uint>(Bus.Ram.AsSpan((int)entryPhys, 4));
                else
                    entryWord = Bus.ReadWord(entryVirt);

                if (entryWord == targetVal)
                {
                    found    = true;
                    foundPtr = entryVirt;
                    break;
                }
            }

            if (found)
            {
                // Advance pointer to the matching entry and simulate:
                //   addiu cntReg, cntReg, +1  ; count the match
                //   jr ra                      ; return to caller
                //   addiu entryReg, $zero, +1  ; delay slot: return value = 1
                Cpu.RegsLo[ptrReg]   = (ulong)(int)foundPtr;
                Cpu.RegsLo[cntReg]   = (ulong)((int)cntVal + 1);
                Cpu.RegsLo[entryReg] = 1u;
                Cpu.PC               = Cpu.RegsLo[31];  // jr ra
            }
            else
            {
                // Target not found within the scan range.  On real PS2 hardware this
                // branch does not occur (target always present in a valid table).
                // With corrupted BIOS data (table size = 537 M, all fill bytes) the
                // search would loop forever.  Return "not found" so the caller can
                // proceed and eventually exit the outer module-init loop.
                Cpu.RegsLo[entryReg] = 0u;               // return value = 0 (not found)
                Cpu.PC               = Cpu.RegsLo[31];   // jr ra
            }

            InstructionCount += entriesToScan;
            return true;
        }


        /// <summary>
        /// Fast-forward the PS2 BIOS outer module-install loop when its loop bound
        /// is corrupted by fill bytes, causing it to iterate hundreds of millions of
        /// times instead of the expected small number.
        ///
        /// Two variants are recognised:
        ///
        /// <b>BNE variant</b> (pc32 = BNE opcode 5):
        /// <code>
        ///   [pc-16]: lw    bndReg,  imm(baseReg)   ; reload bound from RAM
        ///   [pc-12]: addiu cntReg,  cntReg,  +1    ; counter++
        ///   [pc-8]:  addiu ptr0Reg, ptr0Reg, +12   ; advance primary pointer
        ///   [pc-4]:  sltu  bndReg,  cntReg, bndReg ; bndReg = (cnt &lt; bound)
        ///   [pc]:    bne   bndReg,  $zero,  loop   ; loop while bndReg != 0
        ///   [pc+4]:  addiu ptr2Reg, ptr2Reg, +12   ; delay slot: advance secondary ptr
        /// </code>
        /// State synthesised: cntReg=bound, ptr0Reg += remaining×12,
        /// ptr2Reg += (remaining+1)×12, bndReg=0, PC=pc+8.
        ///
        /// <b>BNEL variant</b> (pc32 = BNEL opcode 0x15):
        /// <code>
        ///   [pc-8]:  lw    bndReg,  imm(baseReg)   ; reload bound from RAM
        ///   [pc-4]:  sltu  bndReg,  cntReg, bndReg ; bndReg = (cnt &lt; bound)
        ///   [pc]:    bnel  bndReg,  $zero,  loop   ; loop while bndReg != 0 (likely)
        ///   [pc+4]:  &lt;delay slot — nullified on exit&gt;
        /// </code>
        /// In this variant the counter (<c>cntReg</c>, e.g. $s1) is incremented
        /// inside a JAL delay slot deeper in the loop body and is not visible at the
        /// BNEL site.  State synthesised: cntReg=bound, bndReg=0, PC=pc+8.
        /// (Other registers modified by JAL delay slots are left unchanged because
        /// they are restored from the stack immediately after the BNEL loop exits.)
        ///
        /// Both variants fire only when the bound loaded from RAM exceeds
        /// <see cref="ModuleInstallBoundThreshold"/> (64 K).  A valid PS2 BIOS
        /// module table has at most a few hundred entries; a bound of 537 M (fill
        /// bytes 0x20202020) or 2.15 G (a stale kernel pointer) indicates corrupted
        /// kernel data.
        /// </summary>
        private bool TryFastForwardModuleInstallLoop()
        {
            uint pc32 = (uint)(Cpu.PC & 0xFFFFFFFFUL);

            // Only fire for KSEG0/KSEG1 code.
            if ((pc32 & 0xC0000000u) != 0x80000000u) return false;

            // ── Common: BNE (opcode 5) or BNEL (opcode 0x15) ────────────────
            uint brWord = FetchWord(pc32);
            uint brOpcode = brWord >> 26;
            bool isBnel = (brOpcode == 0x15u);
            if (!isBnel && brOpcode != 5u) return false;            // must be BNE or BNEL
            int bndReg = (int)((brWord >> 21) & 0x1Fu);
            if (((brWord >> 16) & 0x1Fu) != 0u) return false;      // rt must be $zero
            int branchImm = (short)(brWord & 0xFFFFu);
            if (branchImm >= 0) return false;                       // must be backward
            if (bndReg == 0) return false;

            // ── SLTU at pc-4: sltu bndReg, cntReg, bndReg ───────────────────
            // rd == rt == bndReg (bound is both input and overwritten by result).
            uint sltuWord = FetchWord(pc32 - 4u);
            if ((sltuWord >> 26) != 0u) return false;                        // SPECIAL
            if ((sltuWord & 0x3Fu) != 0x2Bu) return false;                   // SLTU
            if ((int)((sltuWord >> 11) & 0x1Fu) != bndReg) return false;     // rd = bndReg
            if ((int)((sltuWord >> 16) & 0x1Fu) != bndReg) return false;     // rt = bndReg (source)
            int cntReg = (int)((sltuWord >> 21) & 0x1Fu);
            if (cntReg == bndReg) return false;

            // ── Guard: bndReg must currently be 1 (loop continues) ──────────
            if ((Cpu.RegsLo[bndReg] & 0xFFFFFFFFUL) == 0UL) return false;

            int  baseReg;
            short boundImm;

            if (isBnel)
            {
                // ── BNEL variant: LW bndReg, imm(baseReg) at pc-8 ──────────
                uint lwWord = FetchWord(pc32 - 8u);
                if ((lwWord >> 26) != 0x23u) return false;                   // LW
                if ((int)((lwWord >> 16) & 0x1Fu) != bndReg) return false;  // rt = bndReg
                baseReg  = (int)((lwWord >> 21) & 0x1Fu);
                boundImm = (short)(lwWord & 0xFFFFu);
            }
            else
            {
                // ── BNE variant: additional checks at pc-8, pc-12, pc-16 ───
                // pc+4 (delay slot): ADDIU ptr2Reg, ptr2Reg, +12
                uint dsWord = FetchWord(pc32 + 4u);
                if ((dsWord >> 26) != 9u) return false;
                int dsRs = (int)((dsWord >> 21) & 0x1Fu);
                int dsRt = (int)((dsWord >> 16) & 0x1Fu);
                if (dsRs != dsRt) return false;
                if ((short)(dsWord & 0xFFFFu) != 12) return false;

                // pc-8: ADDIU ptr0Reg, ptr0Reg, +12
                uint add0Word = FetchWord(pc32 - 8u);
                if ((add0Word >> 26) != 9u) return false;
                int add0Rs = (int)((add0Word >> 21) & 0x1Fu);
                int add0Rt = (int)((add0Word >> 16) & 0x1Fu);
                if (add0Rs != add0Rt) return false;
                if ((short)(add0Word & 0xFFFFu) != 12) return false;

                // pc-12: ADDIU cntReg, cntReg, +1
                uint addCntWord = FetchWord(pc32 - 12u);
                if ((addCntWord >> 26) != 9u) return false;
                if ((int)((addCntWord >> 21) & 0x1Fu) != cntReg) return false;
                if ((int)((addCntWord >> 16) & 0x1Fu) != cntReg) return false;
                if ((short)(addCntWord & 0xFFFFu) != 1) return false;

                // pc-16: LW bndReg, imm(baseReg)
                uint lwWord = FetchWord(pc32 - 16u);
                if ((lwWord >> 26) != 0x23u) return false;
                if ((int)((lwWord >> 16) & 0x1Fu) != bndReg) return false;
                baseReg  = (int)((lwWord >> 21) & 0x1Fu);
                boundImm = (short)(lwWord & 0xFFFFu);
            }

            // ── Read bound from RAM and check threshold ──────────────────────
            uint baseVal   = (uint)(Cpu.RegsLo[baseReg] & 0xFFFFFFFFu);
            uint boundAddr = (uint)((int)baseVal + boundImm);
            uint bound     = Bus.ReadWord(boundAddr);
            if (bound <= ModuleInstallBoundThreshold) return false;

            uint s1Val = (uint)(Cpu.RegsLo[cntReg] & 0xFFFFFFFFu);
            if (s1Val >= bound) return false;
            long remaining = (long)bound - (long)s1Val;
            if (remaining <= 0) return false;

            // ── Synthesise final loop state ───────────────────────────────────
            CpuHelpers.SetReg32(Cpu, cntReg, (int)(s1Val + (uint)remaining)); // = bound
            CpuHelpers.SetReg32(Cpu, bndReg, 0);                               // SLTU = 0
            Cpu.PC = pc32 + 8u;                                                 // past branch + delay slot

            if (!isBnel)
            {
                // BNE variant: also advance the pointer registers that are
                // explicitly incremented at the loop back-edge (pc-8 and pc+4).
                // For BNEL the pointer increment lives inside a JAL delay slot;
                // those registers are restored from the stack after the loop, so
                // advancing them here is unnecessary and could introduce rounding
                // errors for the partially-executed iteration already in flight.
                uint dsWord = FetchWord(pc32 + 4u);
                int ptr2Reg = (int)((dsWord >> 16) & 0x1Fu);
                uint add0Word = FetchWord(pc32 - 8u);
                int ptr0Reg = (int)((add0Word >> 16) & 0x1Fu);
                uint ptr0Val = (uint)(Cpu.RegsLo[ptr0Reg] & 0xFFFFFFFFu);
                uint ptr2Val = (uint)(Cpu.RegsLo[ptr2Reg] & 0xFFFFFFFFu);
                CpuHelpers.SetReg32(Cpu, ptr0Reg, (int)(ptr0Val + (uint)remaining * 12u));
                CpuHelpers.SetReg32(Cpu, ptr2Reg, (int)(ptr2Val + (uint)(remaining + 1L) * 12u));
            }

            InstructionCount += (ulong)remaining * 10u;
            return true;
        }

        /// <summary>Minimum bound value before the module-install loop is fast-forwarded.</summary>
        private const uint ModuleInstallBoundThreshold = 65536u;

        /// <summary>
        /// One-shot repair of EE kernel data fields that are stuck at the BIOS fill-pattern
        /// (0x20202020) because the kernel's TLB-mapped zeroing pass cannot be fast-forwarded.
        ///
        /// Background
        /// ----------
        /// The BIOS bulk-fill loop at 0x800109FC writes 0x20 over the entire 32 MB of EE RAM
        /// (physical 0x00000000–0x01FFFFFF), including the kernel data area at physical
        /// 0x20000–0x5FFFF.  After decompressing the kernel into RAM the BIOS runs a zeroing
        /// pass over this region using TLB-mapped virtual addresses (KSEG2, 0xC0000000+).
        /// Our <see cref="TryFastForwardByteCopy"/> fast-forward cannot resolve those virtual
        /// addresses and marks the entire copy as a no-op (fullSkip), so the fill-pattern
        /// persists in physical RAM.
        ///
        /// Effect
        /// ------
        /// Key SIFMAN fields loaded by the EE kernel poll loop at 0x80012670 include:
        ///   physical 0x23FD4  — SIF0 pending-packet counter (should be 0 after init)
        ///   physical 0x242F8  — module-list pointer field   (should be 0 after init)
        ///   physical 0x24100  — per-module state word       (should be 0 after init)
        ///   physical 0x24124  — outer-loop exit flag        (should be 0 after init)
        ///
        /// With these stuck at 0x20202020 the EE spins forever, never advancing the SIF
        /// handshake, leaving the IOP trapped at its idle self-jump 0x0000ae94.
        ///
        /// Fix
        /// ---
        /// When the EE first enters the handshake loop (PC in 0x80012600–0x80012800) and
        /// the sentinel field at physical 0x23FD4 still holds the fill-pattern, we scan
        /// the entire kernel data window (physical 0x20000–0x5FFFF) and zero every 4-byte
        /// aligned word that still holds exactly 0x20202020.  This matches what the real
        /// kernel zeroing pass would have done, without touching any word that the BIOS or
        /// kernel has legitimately written.
        ///
        /// The flag <see cref="_eeKernelSifDataFixed"/> ensures this runs exactly once.
        /// </summary>
        private void TryRepairEeKernelSifData()
        {
            if (_eeKernelSifDataFixed)
                return;

            // Only act when the EE is in the SIF handshake area.
            uint pc32 = (uint)(Cpu.PC & 0xFFFFFFFFUL);
            if (pc32 < 0x80012600u || pc32 > 0x80012800u)
                return;

            // Check the sentinel field (physical 0x23FD4 = virtual 0x800023FD4 in KSEG0).
            const uint SentinelPhys = 0x23FD4u;
            if (SentinelPhys + 3u >= (uint)Bus.Ram.Length)
                return;

            uint sentinel = (uint)(Bus.Ram[SentinelPhys]
                                 | (Bus.Ram[SentinelPhys + 1] << 8)
                                 | (Bus.Ram[SentinelPhys + 2] << 16)
                                 | (Bus.Ram[SentinelPhys + 3] << 24));
            if (sentinel != 0x20202020u)
            {
                // Kernel already zeroed the data (TLB worked or data was written legitimately).
                _eeKernelSifDataFixed = true;
                return;
            }

            // Zero every 4-byte aligned word in the kernel data area that still holds the
            // exact fill-pattern 0x20202020.  Words written legitimately by the BIOS or
            // kernel to non-zero values are left untouched.
            const int KernelDataStart = 0x20000;
            const int KernelDataEnd   = 0x60000;
            for (int pa = KernelDataStart; pa < KernelDataEnd && pa + 3 < Bus.Ram.Length; pa += 4)
            {
                if (Bus.Ram[pa]     == 0x20
                 && Bus.Ram[pa + 1] == 0x20
                 && Bus.Ram[pa + 2] == 0x20
                 && Bus.Ram[pa + 3] == 0x20)
                {
                    Bus.Ram[pa]     = 0;
                    Bus.Ram[pa + 1] = 0;
                    Bus.Ram[pa + 2] = 0;
                    Bus.Ram[pa + 3] = 0;
                }
            }

            _eeKernelSifDataFixed = true;
        }


        ///   loop:
        ///     lbu    tmpReg,  0(srcReg)          ; load byte from source
        ///     addiu  cntReg,  cntReg,  -1         ; count--
        ///     addiu  srcReg,  srcReg,  +1         ; src++
        ///     sb     tmpReg,  0(dstReg)           ; store byte to destination
        ///     nop
        ///     bne    cntReg,  sentReg,  loop      ; loop while cntReg != sentReg
        ///     addiu  dstReg,  dstReg,  +1         ; delay slot: dst++
        /// </code>
        ///
        /// This pattern appears in the EE kernel at 0x800103e0 immediately after
        /// the BIOS completes its kernel-decompression fill loops and jumps into RAM.
        /// The loop copies a large data segment (up to ~20 MB) byte-by-byte between
        /// two TLB-mapped KSEG2 virtual addresses, which is extremely slow without
        /// fast-forwarding.
        ///
        /// Because both source and destination may be in KSEG2 (virtual addresses
        /// starting at 0xC0000000), a direct TranslateAddress call is not sufficient.
        /// This fast-forward uses <see cref="PhysAddrAndPageRemaining"/> to resolve
        /// each virtual page via the EE TLB (or KSEG0/1 direct mapping), then copies
        /// data directly in physical RAM in page-aligned chunks.  If any TLB lookup
        /// fails, the fast-forward is silently skipped so normal execution resumes.
        /// </summary>
        private bool TryFastForwardByteCopy()
        {
            // ── Step 1: PC must be in KSEG0/KSEG1 and at a BNE (opcode=5) ────
            // Only run in KSEG0/1 — FetchWord would throw TlbMissException for
            // TLB-mapped PCs, which the caller expects to propagate from Step().
            uint pc32 = (uint)(Cpu.PC & 0xFFFFFFFFUL);
            if ((pc32 & 0xC0000000u) != 0x80000000u) return false;
            uint bneWord = FetchWord(pc32);
            if ((bneWord >> 26) != 5u) return false;           // BNE opcode = 5

            // Temporary debug: trace first 5 calls at 0x800103F4 (the byte-copy BNE)
            bool dbg = (pc32 == 0x800103F4u && _byteCopyDebugCount < 5);
            if (dbg) _byteCopyDebugCount++;

            // ── Step 2: Branch must be backward ──────────────────────────────
            int branchOffset = (short)(bneWord & 0xFFFFu);
            if (branchOffset >= 0) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL branchOffset={branchOffset}>=0"); return false; }
            uint loopStart = (uint)((long)(pc32 + 4) + (long)branchOffset * 4L);

            // ── Step 3: Loop body must be exactly 5 instructions ─────────────
            int loopLen = (int)(pc32 - loopStart) / 4;
            if (loopLen != 5) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL loopLen={loopLen}!=5 loopStart=0x{loopStart:x8}"); return false; }

            // Extract BNE register fields: rs=cntReg, rt=sentReg
            int cntReg = (int)((bneWord >> 21) & 0x1Fu);
            int sentReg = (int)((bneWord >> 16) & 0x1Fu);
            if (cntReg == 0 || sentReg == cntReg) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL cntReg={cntReg} sentReg={sentReg}"); return false; }

            // ── Step 4: Validate delay slot — ADDIU dstReg, dstReg, +1 ──────
            uint dsWord = FetchWord(pc32 + 4u);
            if ((dsWord >> 26) != 9u) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL dsWord opcode={(dsWord>>26)} 0x{dsWord:x8}"); return false; }            // ADDIU
            int dsBase = (int)((dsWord >> 21) & 0x1Fu);
            int dsDst  = (int)((dsWord >> 16) & 0x1Fu);
            if (dsBase != dsDst) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL dsBase={dsBase}!=dsDst={dsDst}"); return false; }                 // rs == rt (in-place)
            if ((short)(dsWord & 0xFFFFu) != 1) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL ds imm={(short)(dsWord&0xFFFF)} 0x{dsWord:x8}"); return false; } // imm == +1
            int dstReg = dsDst;

            // ── Step 5: Validate loop body instructions ───────────────────────
            // w0: LBU tmpReg, 0(srcReg)
            uint w0 = FetchWord(loopStart);
            if ((w0 >> 26) != 0x24u) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL w0 opcode={(w0>>26)} 0x{w0:x8}"); return false; }            // LBU opcode = 0x24
            if ((short)(w0 & 0xFFFFu) != 0) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL w0 offset={(short)(w0&0xFFFF)}"); return false; }     // offset must be 0
            int srcReg  = (int)((w0 >> 21) & 0x1Fu);          // base register
            int tmpReg  = (int)((w0 >> 16) & 0x1Fu);          // destination of LBU

            // w1: ADDIU cntReg, cntReg, -1
            uint w1 = FetchWord(loopStart + 4u);
            if ((w1 >> 26) != 9u) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL w1 opcode 0x{w1:x8}"); return false; }
            if ((int)((w1 >> 21) & 0x1Fu) != cntReg) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL w1 rs={(w1>>21)&0x1F}!=cntReg={cntReg} 0x{w1:x8}"); return false; }
            if ((int)((w1 >> 16) & 0x1Fu) != cntReg) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL w1 rt={(w1>>16)&0x1F}!=cntReg={cntReg} 0x{w1:x8}"); return false; }
            if ((short)(w1 & 0xFFFFu) != -1) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL w1 imm={(short)(w1&0xFFFF)} 0x{w1:x8}"); return false; }

            // w2: ADDIU srcReg, srcReg, +1
            uint w2 = FetchWord(loopStart + 8u);
            if ((w2 >> 26) != 9u) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL w2 opcode 0x{w2:x8}"); return false; }
            if ((int)((w2 >> 21) & 0x1Fu) != srcReg) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL w2 rs={(w2>>21)&0x1F}!=srcReg={srcReg} 0x{w2:x8}"); return false; }
            if ((int)((w2 >> 16) & 0x1Fu) != srcReg) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL w2 rt={(w2>>16)&0x1F}!=srcReg={srcReg} 0x{w2:x8}"); return false; }
            if ((short)(w2 & 0xFFFFu) != 1) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL w2 imm={(short)(w2&0xFFFF)} 0x{w2:x8}"); return false; }

            // w3: SB tmpReg, 0(dstReg)
            uint w3 = FetchWord(loopStart + 12u);
            if ((w3 >> 26) != 0x28u) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL w3 opcode={(w3>>26)} 0x{w3:x8}"); return false; }            // SB opcode = 0x28
            if ((int)((w3 >> 21) & 0x1Fu) != dstReg) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL w3 base={(w3>>21)&0x1F}!=dstReg={dstReg} 0x{w3:x8}"); return false; }  // base = dstReg
            if ((short)(w3 & 0xFFFFu) != 0) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL w3 offset={(short)(w3&0xFFFF)}"); return false; }     // offset must be 0
            if ((int)((w3 >> 16) & 0x1Fu) != tmpReg) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL w3 data={(w3>>16)&0x1F}!=tmpReg={tmpReg} 0x{w3:x8}"); return false; }  // data = tmpReg

            // w4: NOP
            uint w4 = FetchWord(loopStart + 16u);
            if (w4 != 0u) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL w4=0x{w4:x8} (not NOP)"); return false; }

            // ── Step 6: Compute remaining iterations ─────────────────────────
            // Loop exits when cntReg == sentReg.  cntReg decrements each iteration.
            // remaining = (signed)(cntReg - sentReg) iterations left.
            int cntVal  = (int)(Cpu.RegsLo[cntReg]  & 0xFFFFFFFFu);
            int sentVal = (int)(Cpu.RegsLo[sentReg] & 0xFFFFFFFFu);
            long remaining = (long)cntVal - (long)sentVal;
            if (remaining <= BulkFillMinIterations) { if (dbg) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}] FAIL remaining={remaining}<=min cntVal={cntVal} sentVal={sentVal}"); return false; }

            // Also trace the first large-remaining case
            bool dbgLarge = (pc32 == 0x800103F4u && remaining > BulkFillMinIterations && _byteCopyLargeDebugCount < 3);
            if (dbgLarge) _byteCopyLargeDebugCount++;

            if (dbg || dbgLarge) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}/{_byteCopyLargeDebugCount}] PASS all checks: pc=0x{pc32:x8} remaining={remaining} cntReg={cntReg} sentReg={sentReg} srcReg={srcReg} dstReg={dstReg} tmpReg={tmpReg}");

            // ── Step 7: Translate src/dst and perform the copy ───────────────
            byte asid = (byte)(Cpu.COP0.ReadRegister32(10) & 0xFFu);
            ulong curSrc = Cpu.RegsLo[srcReg] & 0xFFFFFFFFUL;
            ulong curDst = Cpu.RegsLo[dstReg] & 0xFFFFFFFFUL;

            long srcRem = PhysAddrAndPageRemaining((uint)curSrc, asid, out uint srcPhys);
            long dstRem = PhysAddrAndPageRemaining((uint)curDst, asid, out uint dstPhys);

            if (dbg || dbgLarge) Console.WriteLine($"[ByteCopy dbg#{_byteCopyDebugCount}/{_byteCopyLargeDebugCount}] srcReg={srcReg} curSrc=0x{curSrc:x8} srcRem={srcRem} srcPhys=0x{srcPhys:x8}; dstReg={dstReg} curDst=0x{curDst:x8} dstRem={dstRem} dstPhys=0x{dstPhys:x8}");

            long chunkLen = 0;
            bool fullSkip = false;

            if (srcRem >= 0 && dstRem >= 0)
            {
                // Both addresses translated to physical RAM.  Copy one page-chunk.
                // chunkLen is bounded by srcRem/dstRem (bytes remaining in the current
                // physical page from each pointer), so it never exceeds the RAM array
                // even when 'remaining' is larger than the space left in RAM from
                // srcPhys or dstPhys.  The excess iterations are handled on the next
                // TryFastForwardByteCopy call (partial-copy path below).
                chunkLen = Math.Min(remaining, Math.Min(srcRem, dstRem));
            }
            else if (srcRem < 0 && dstRem < 0)
            {
                // TLB lookup failed for both src and dst.  Check whether the bus
                // maps these addresses to an unmapped region (reads return 0, writes
                // discarded).  KSEG2 virtual addresses (0xC0000000+) pass through
                // TranslateAddress unchanged; the physical value 0xC1xxxxxx is beyond
                // the main RAM window so MapPhysical returns the dummy zero buffer.
                // In that case the entire loop is a memory no-op: skip it.
                // Exclude BIOS ROM (0x1FC00000-0x1FFFFFFF) which IS readable.
                uint srcRaw = PS2MemoryBus.TranslateAddress((uint)curSrc);
                uint dstRaw = PS2MemoryBus.TranslateAddress((uint)curDst);
                bool srcUnmapped = srcRaw >= PS2MemoryBus.MainRamMirrorEnd && !IsBiosRom(srcRaw);
                bool dstUnmapped = dstRaw >= PS2MemoryBus.MainRamMirrorEnd && !IsBiosRom(dstRaw);
                if (srcUnmapped && dstUnmapped)
                {
                    // Both are in unmapped physical space: reads return 0, writes discarded.
                    chunkLen = remaining;
                    fullSkip = true;
                }
                else
                    return false;
            }
            else if (srcRem >= 0 && dstRem < 0)
            {
                // Source in physical RAM, dst TLB miss.
                // If dst translates to unmapped physical space (bus silently discards
                // writes), the copy is a no-op from memory's perspective.
                uint dstRaw = PS2MemoryBus.TranslateAddress((uint)curDst);
                if (dstRaw >= PS2MemoryBus.MainRamMirrorEnd && !IsBiosRom(dstRaw))
                {
                    chunkLen = remaining;
                    fullSkip = true;
                }
                else
                    return false;
            }
            else if (srcRem < 0 && dstRem >= 0)
            {
                // src TLB miss.
                uint srcRaw = PS2MemoryBus.TranslateAddress((uint)curSrc);
                if (IsBiosRom(srcRaw))
                {
                    // BIOS ROM → RAM kernel-copy path.
                    uint biosOff = srcRaw - PS2MemoryBus.BiosRomPhysBase;
                    chunkLen = Math.Min(remaining, dstRem);
                    chunkLen = Math.Min(chunkLen, (long)(Bus.Bios.Length - biosOff));
                    if (chunkLen <= 0) return false;
                    if (dstPhys + chunkLen > Bus.Ram.Length) return false;
                    Bus.Bios.AsSpan((int)biosOff, (int)chunkLen)
                             .CopyTo(Bus.Ram.AsSpan((int)dstPhys, (int)chunkLen));
                    curSrc += (ulong)chunkLen;
                    curDst += (ulong)chunkLen;
                    goto commitRegisters;
                }
                else if (srcRaw >= PS2MemoryBus.MainRamMirrorEnd)
                {
                    // Unmapped src: the bus dummy buffer returns 0 for every read,
                    // which would make this a zero-fill of the dst RAM region.
                    // However, for large copies the dst virtual address wraps around
                    // via the bus's 32 MB mirror mask (vaddr & 0x01FFFFFF), which
                    // would overwrite kernel code at physical 0x00000000 when the
                    // virtual address crosses the 32 MB boundary.  On real hardware
                    // the TLB would map these large virtual addresses to non-existent
                    // physical pages (writes discarded).  To match that behaviour and
                    // avoid corrupting in-flight kernel code, treat the full copy as
                    // a skip: advance all pointers/counter without touching RAM.
                    chunkLen = remaining;
                    fullSkip = true;
                }
                else
                    return false;  // src is in RAM but TLB-mapped (should not happen for fast-forward)
            }

            if (!fullSkip)
            {
                if (srcPhys + chunkLen > Bus.Ram.Length) return false;
                if (dstPhys + chunkLen > Bus.Ram.Length) return false;
                Bus.Ram.AsSpan((int)srcPhys, (int)chunkLen)
                        .CopyTo(Bus.Ram.AsSpan((int)dstPhys, (int)chunkLen));
            }

            curSrc += (ulong)chunkLen;
            curDst += (ulong)chunkLen;

            commitRegisters:
            // ── Step 8: Update CPU registers ─────────────────────────────────
            // Each fast-forwarded iteration: cntReg--, srcReg++, dstReg++ (delay slot).
            long newCntVal = (long)cntVal - chunkLen;
            CpuHelpers.SetReg32(Cpu, srcReg, (int)(uint)curSrc);
            CpuHelpers.SetReg32(Cpu, dstReg, (int)(uint)curDst);

            if (chunkLen == remaining)
            {
                // Full copy done — cntReg reaches sentVal, BNE not taken, exit loop.
                CpuHelpers.SetReg32(Cpu, cntReg, sentVal);
                if (!fullSkip)
                {
                    long lastR = PhysAddrAndPageRemaining((uint)(curDst - 1), asid, out uint lastPhys);
                    if (lastR >= 0 && lastPhys < Bus.Ram.Length)
                        CpuHelpers.SetReg32(Cpu, tmpReg, Bus.Ram[lastPhys]);
                }
                // For fullSkip (unmapped src), last byte read was 0 (dummy buffer).
                // tmpReg already has the value from the last executed iteration; don't change.
                Cpu.PC = pc32 + 8u;   // past BNE + delay slot
            }
            else
            {
                // Partial: continue loop from top of loop body.
                CpuHelpers.SetReg32(Cpu, cntReg, (int)newCntVal);
                Cpu.PC = loopStart;
            }

            InstructionCount += (ulong)chunkLen;
            return true;
        }

        // ── BIOS serial-output (puts) loop fast-forward ─────────────────────────

        /// <summary>
        /// Minimum string length (in bytes) before the BIOS puts fast-forward triggers.
        /// Very short strings (a single newline, etc.) are cheaper to execute normally.
        /// </summary>
        private const int BiosPutsMinLen = 8;

        /// <summary>
        /// Detect and fast-forward the BIOS null-terminated string output loop.
        ///
        /// The PS2 BIOS uses a tight loop to print strings to the EE serial port
        /// (SBUS_F180 / physical 0x1000F180).  Each iteration loads one byte from
        /// the string pointer, calls the serial putchar function via a function pointer,
        /// and advances the pointer.  The loop exits when the loaded byte is zero.
        ///
        /// Pattern detected (BNEL variant, the form used by the PS2 BIOS):
        /// <code>
        ///   loop_start:
        ///     lui    funcPtrHi, ...                ; load function-pointer upper
        ///     lw     funcPtrReg, offset(funcPtrHi) ; load function pointer
        ///     jalr   funcPtrReg                    ; call putchar(charReg)
        ///     daddu  a0, charReg, zero             ; delay slot: set up argument
        ///     lb     charReg, 0(ptrReg)            ; load next character from string
        ///     bnel   charReg, $zero, loop_start    ; loop while charReg != 0
        ///     addiu  ptrReg, ptrReg, 1             ; delay slot: advance pointer
        /// </code>
        ///
        /// When detected and the remaining string length exceeds
        /// <see cref="BiosPutsMinLen"/>, all remaining characters are skipped:
        /// <paramref name="ptrReg"/> is advanced to the null terminator, charReg is
        /// set to 0, and the PC is set past the loop.  This is safe because the only
        /// side-effect of the putchar function is writing to SBUS_F180 (the EE serial
        /// TX data register), which our emulator does not use for display.  SBUS_F130
        /// (TX status) is always read as 0, so the inner TX-ready poll loop exits
        /// immediately regardless.
        ///
        /// Strings may reside in either main RAM or BIOS ROM; both regions are
        /// scanned correctly.
        ///
        /// Returns <c>true</c> if the fast-forward was applied and
        /// <see cref="Step"/> should return immediately.
        /// </summary>
        private bool TryFastForwardBiosPuts()
        {
            uint pc32 = (uint)(Cpu.PC & 0xFFFFFFFFUL);

            // Only accelerate KSEG0/KSEG1 code (fixed-mapped kernel region).
            if ((pc32 & 0xC0000000u) != 0x80000000u) return false;

            // ── Step 1: BNEL at pc32  (opcode = 0x15, rt = $zero) ────────────
            // Use FetchWord (i-cache) so that any in-flight fills that happen to
            // overlap this code region return the correct instruction word.
            uint bnelWord = FetchWord(pc32);
            if ((bnelWord >> 26) != 0x15u) return false;               // must be BNEL
            if (((bnelWord >> 16) & 0x1Fu) != 0u) return false;        // rt must be $zero
            int charReg = (int)((bnelWord >> 21) & 0x1Fu);             // register checked != 0

            // ── Step 2: Branch must be backward ──────────────────────────────
            int branchImm = (short)(bnelWord & 0xFFFFu);
            if (branchImm >= 0) return false;                           // loop must be backward
            uint loopStart = (uint)((int)(pc32 + 4u) + branchImm * 4);
            if (loopStart >= pc32) return false;
            uint loopBodyLen = (pc32 - loopStart) / 4u;                // # instructions before BNEL

            // ── Step 3: Delay slot — pointer advance detection ───────────────
            // Two loop variants are in use by the PS2 BIOS puts implementation:
            //
            //   Pattern A — ADDIU ptrReg, ptrReg, +1 in the BNEL delay slot.
            //     (classic null-terminated string walk, pointer advanced after null check)
            //
            //   Pattern B — LB argReg, 0(ptrReg) in the BNEL delay slot, with
            //     ADDIU ptrReg, ptrReg, +1 inside the loop body (typically the
            //     JALR delay slot).  The loop pre-loads the next character as the
            //     argument for the upcoming putchar call.
            //
            // In both patterns the pointer still advances exactly +1 per iteration
            // and the fast-forward logic (steps 6–8) is identical.  Pattern B is
            // distinguished from arbitrary LB-in-delay-slot loops by requiring that
            // an in-place ADDIU ptrReg++ exists somewhere in the loop body.
            uint dsWord = FetchWord(pc32 + 4u);
            int ptrReg;
            if ((dsWord >> 26) == 9u)
            {
                // Pattern A: ADDIU ptrReg, ptrReg, +1 in the BNEL delay slot.
                int dsRs = (int)((dsWord >> 21) & 0x1Fu);
                int dsRt = (int)((dsWord >> 16) & 0x1Fu);
                if (dsRs != dsRt) return false;                        // rs == rt (in-place)
                if ((short)(dsWord & 0xFFFFu) != 1) return false;     // imm must be +1
                ptrReg = dsRt;
            }
            else if ((dsWord >> 26) == 0x20u)
            {
                // Pattern B: LB argReg, 0(ptrReg) in the BNEL delay slot.
                // The base register of the LB is the pointer register.
                if ((short)(dsWord & 0xFFFFu) != 0) return false;     // LB offset must be 0
                ptrReg = (int)((dsWord >> 21) & 0x1Fu);               // base = ptrReg
                // Verify ADDIU ptrReg, ptrReg, +1 is present in the loop body.
                // This guards against false-positives on non-string LB loops.
                bool foundAddiu = false;
                for (uint off = 0u; off < loopBodyLen; off++)
                {
                    uint w = FetchWord(loopStart + off * 4u);
                    if ((w >> 26) == 9u                                // ADDIU
                        && (int)((w >> 21) & 0x1Fu) == ptrReg         // rs = ptrReg
                        && (int)((w >> 16) & 0x1Fu) == ptrReg         // rt = ptrReg
                        && (short)(w & 0xFFFFu) == 1)                 // imm = +1
                    { foundAddiu = true; break; }
                }
                if (!foundAddiu) return false;
            }
            else
            {
                return false;
            }

            // ── Step 4: Loop body must contain a function call (JALR or JAL) ─
            // This distinguishes the serial putchar loop from simpler byte-scan loops.
            bool hasCall = false;
            for (uint off = 0u; off < loopBodyLen - 1u; off++)        // skip the LB (last instr)
            {
                uint w     = FetchWord(loopStart + off * 4u);
                uint op    = w >> 26;
                uint funct = w & 0x3Fu;
                if (op == 0x03u || (op == 0u && funct == 0x09u))      // JAL or JALR
                {
                    hasCall = true;
                    break;
                }
            }
            if (!hasCall) return false;

            // ── Step 5: Instruction immediately before BNEL — LB charReg, 0(ptrReg) ─
            if (pc32 < 4u) return false;
            uint lbWord = FetchWord(pc32 - 4u);
            if ((lbWord >> 26) != 0x20u) return false;                // LB opcode = 0x20
            if ((int)((lbWord >> 21) & 0x1Fu) != ptrReg) return false; // base = ptrReg
            if ((int)((lbWord >> 16) & 0x1Fu) != charReg) return false; // dest = charReg
            if ((short)(lbWord & 0xFFFFu) != 0) return false;         // offset must be 0

            // ── Step 6: Read the string pointer and determine its memory region ─
            uint ptrVal = (uint)(Cpu.RegsLo[ptrReg] & 0xFFFFFFFFu);

            // The pointer must be in KSEG0 or KSEG1 (kernel virtual memory).
            if ((ptrVal & 0x80000000u) == 0u) return false;
            uint physAddr = PS2MemoryBus.TranslateAddress(ptrVal);

            // Select the backing byte array for the scan.  Strings may live in
            // main RAM (physical 0x00000000–0x0FFFFFFF) or in BIOS ROM
            // (physical 0x1FC00000–0x1FFFFFFF).  The old code used a blanket
            // "(& MainRamSize-1)" mask that silently wrapped ROM addresses into
            // a wrong RAM offset, producing an empty or garbage scan result.
            byte[] strRegion;
            uint   strOffset;
            if (physAddr < 0x10000000u)
            {
                strOffset = physAddr & (uint)(PS2MemoryBus.MainRamSize - 1);  // mirror wrap
                strRegion = Bus.Ram;
            }
            else if (physAddr >= PS2MemoryBus.BiosRomPhysBase
                     && physAddr < PS2MemoryBus.BiosRomPhysBase + (uint)PS2MemoryBus.BiosRomSize)
            {
                strOffset = physAddr - PS2MemoryBus.BiosRomPhysBase;
                strRegion = Bus.Bios;
            }
            else
            {
                return false;  // unknown region — do not fast-forward
            }
            if (strOffset >= (uint)strRegion.Length) return false;

            // ── Step 7: Measure string length ────────────────────────────────
            // Count non-null bytes starting from strOffset up to a safety cap.
            const int MaxScan = 1_000_000;
            int len = 0;
            while (strOffset + (uint)len < (uint)strRegion.Length
                   && len < MaxScan
                   && strRegion[strOffset + (uint)len] != 0)
            {
                len++;
            }

            // Only fast-forward non-trivial strings.
            if (len < BiosPutsMinLen) return false;

            // ── Step 8: Fast-forward — skip entire string ─────────────────────
            // Simulate all 'len' loop iterations plus the final null check:
            //   ptrReg → ptrReg + len  (null terminator position)
            //   charReg → 0            (null byte that ended the loop)
            //   PC → pc32 + 8         (past BNEL + delay slot; delay slot nullified)
            uint newPtr = ptrVal + (uint)len;
            // Sign-extend to 64 bits the same way ADDIU would (PS2 EE is MIPS64).
            Cpu.RegsLo[ptrReg] = (ulong)(long)(int)newPtr;
            Cpu.RegsLo[charReg] = 0uL;
            Cpu.PC = pc32 + 8u;    // past BNEL + delay slot; pointer increment is included in ptrReg advance above

            // Count the skipped iterations so InstructionCount stays consistent.
            InstructionCount += (ulong)len;
            return true;
        }

        /// <summary>
        /// Translate a virtual address to its physical address, handling both the
        /// fixed KSEG0/1 mapping (strip top 3 bits) and TLB-mapped regions
        /// (KUSEG, KSEG2, KSEG3).
        /// </summary>
        /// <param name="virt">32-bit virtual address.</param>
        /// <param name="asid">Current ASID from COP0 EntryHi.</param>
        /// <param name="physAddr">Physical address if translation succeeded; otherwise 0.</param>
        /// <returns>
        /// Number of bytes remaining in the current TLB/physical page starting from
        /// <paramref name="virt"/>.  Returns -1 if translation fails (TLB miss or
        /// page marked invalid).
        /// </returns>
        private long PhysAddrAndPageRemaining(uint virt, byte asid, out uint physAddr)
        {
            // KSEG0 (0x80000000-0x9FFFFFFF) and KSEG1 (0xA0000000-0xBFFFFFFF)
            // both strip the top 3 bits.  Treat the entire 512 MB window as one
            // "page" (remaining = physical RAM size from that offset).
            if ((virt & 0xC0000000u) == 0x80000000u)
            {
                physAddr = virt & 0x1FFFFFFFu;
                long rem = (long)Bus.Ram.Length - physAddr;
                return rem > 0 ? rem : -1L;
            }

            // For all other regions, first check the bus physical mapping.
            // PS2MemoryBus.TranslateAddress() strips KSEG0/1 bits but passes KUSEG
            // and KSEG2 addresses through unchanged.  If the result falls in the
            // main RAM window [0, MainRamMirrorEnd), the bus serves the access
            // from RAM without any TLB lookup — exactly how the instruction
            // implementations (LBU, SB, …) work.
            uint busPhys = PS2MemoryBus.TranslateAddress(virt);
            if (busPhys < PS2MemoryBus.MainRamMirrorEnd)
            {
                // Apply the same 32 MB power-of-two wrap used by MapPhysical.
                physAddr = busPhys & (uint)(PS2MemoryBus.MainRamSize - 1);
                long rem  = (long)Bus.Ram.Length - physAddr;
                return rem > 0 ? rem : -1L;
            }

            // TLB-mapped region: search the 48-entry EE TLB.
            // Sign-extend the 32-bit virtual address to 64 bits: addresses with
            // bit 31 set (KSEG2/3: 0xC0000000–0xFFFFFFFF) are sign-extended when
            // the kernel writes them to COP0 EntryHi via MTC0, so TLB entries for
            // these regions are stored with the top 32 bits all set (0xFFFFFFFF…).
            var tlb = Cpu.COP0.TLB;
            ulong va  = (ulong)(long)(int)virt;  // sign-extend 32→64 bits
            ulong vpn = va & 0xFFFFFFFFFFFFE000UL;

            for (int i = 0; i < TLBState.NumTLBEntries; i++)
            {
                TLBEntry entry = tlb.ReadEntry(i);
                if (!entry.Valid) continue;

                ulong eVpn  = entry.EntryHi & 0xFFFFFFFFFFFFE000UL;
                byte  eAsid = (byte)(entry.EntryHi & 0xFFu);
                bool  global = ((entry.EntryLo0 & 1u) != 0) && ((entry.EntryLo1 & 1u) != 0);

                // Apply the page mask to align both VPNs before comparison.
                // PageMask covers the page-size bits, so mask them out of the VPN.
                uint pageMask  = entry.PageMask;
                ulong maskFull = ~(ulong)(pageMask | 0x1FFFu);
                if ((eVpn & maskFull) != (vpn & maskFull)) continue;
                if (!global && eAsid != asid)              continue;

                uint pageSize = (pageMask | 0x1FFFu) + 1u;  // size of one page
                bool odd = ((virt & pageSize) != 0);

                ulong lo = odd ? entry.EntryLo1 : entry.EntryLo0;
                if ((lo & 0x2u) == 0) { physAddr = 0; return -1L; } // V bit clear

                ulong pfn    = (lo >> 6) & 0xFFFFFUL;
                uint  offset = virt & (pageSize - 1u);
                physAddr = (uint)((pfn << 12) | offset);
                return (long)(pageSize - offset);            // bytes left in page
            }

            physAddr = 0;
            return -1L;  // no matching TLB entry
        }

        /// <summary>
        /// Returns true if <paramref name="physAddr"/> falls within the BIOS ROM
        /// physical window (0x1FC00000–0x1FFFFFFF).
        /// </summary>
        private static bool IsBiosRom(uint physAddr)
            => physAddr >= PS2MemoryBus.BiosRomPhysBase
            && physAddr <  PS2MemoryBus.BiosRomPhysBase + PS2MemoryBus.BiosRomSize;

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
                VBlankStarted?.Invoke(this, EventArgs.Empty);  // IOP VBlank start (bit 0)
            }

            if (_vSyncCycles >= NtscFrameCycles)
            {
                _vSyncCycles -= NtscFrameCycles;
                if (_inVBlank)
                {
                    _inVBlank = false;
                    Bus.Intc.RaiseVBlankEnd();  // INTC bit 3
                    VBlankEnded?.Invoke(this, EventArgs.Empty);  // IOP EvBlank (bit 11)
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
