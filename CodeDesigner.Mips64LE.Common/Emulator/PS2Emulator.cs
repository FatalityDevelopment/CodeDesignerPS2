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

        private bool _dbgDecompFirstFire = true;
        private bool _dbgFill2FirstFire = true;

        /// <summary>Debug: incremented each Step() for watchpoint tracing.</summary>
        [System.ThreadStatic]
        public static ulong DbgStepCount;
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
        /// Handles KUSEG TLB translation (when the TLB has been populated by the BIOS),
        /// KSEG0/KSEG1 fixed mapping (via the bus), and throws
        /// <see cref="TlbMissException"/> on a KUSEG TLB miss once TLB entries exist.
        ///
        /// For KSEG0/KSEG1 addresses the result is served through the simple i-cache
        /// (see <see cref="_iCacheValid"/>/<see cref="_iCacheData"/>).  This prevents a
        /// data-store loop (e.g. the BIOS kernel-init fill that writes 0x20 to all of
        /// RAM) from corrupting the executing instructions, matching the behaviour of the
        /// PS2's physically-tagged hardware i-cache.
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

                return _iCacheData[setIdx * ICacheLineWords + ICacheWordOff(physPc)];
            }

            // KSEG1 (0xA0000000–0xBFFFFFFF) is the non-cacheable mirror.
            // KSEG2/3 and scratchpad / pre-TLB KUSEG: bus handles translation.
            return Bus.ReadWord(virtualPc);
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
            DbgStepCount++;
            // ── COP0 Count increment + Count/Compare interrupt check ──────────
            // Count increments at HALF the EE CPU clock rate.
            // Reference: PCSX2 R5900.cpp  cpuRegs.CP0.n.Count += elapsed / 2.
            // We alternate a one-bit accumulator so IncrementCount(1) fires every
            // other Step(), correctly matching the PS2's half-rate Count behaviour
            // and ensuring the BIOS RDRAM timing calibration measures the right value.
            if ((_cop0CountAccum ^= 1) == 0)
                Cpu.COP0.IncrementCount(1);

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

            // Debug: when entering the decompressor loop, dump key registers.
            if (pc32 == 0x80010870u && _dbgDecompFirstFire)
            {
                _dbgDecompFirstFire = false;
                uint a1 = (uint)(Cpu.RegsLo[5] & 0xFFFFFFFFu);  // $a1
                uint s0 = (uint)(Cpu.RegsLo[16] & 0xFFFFFFFFu); // $s0
                uint s2 = (uint)(Cpu.RegsLo[18] & 0xFFFFFFFFu); // $s2
                System.Console.Error.WriteLine($"[DECOMP] first fire at step={DbgStepCount}: $a1=0x{a1:x8} $s0=0x{s0:x8} $s2=0x{s2:x8}");
                uint physA1 = PS2MemoryBus.TranslateAddress(a1);
                System.Console.Error.WriteLine($"[DECOMP] $a1 phys=0x{physA1:x8}  RAM[physA1]=0x{(physA1 < Bus.Ram.Length ? Bus.Ram[physA1] : 0xff):x2}  BiosRom={physA1 >= 0x1FC00000u}");
                System.Console.Error.WriteLine($"[DECOMP] table[0..3]: 0x{Bus.ReadWord(a1):x8} 0x{Bus.ReadWord(a1+4):x8}");
            }

            // Debug: trace first visit to second fill loop start (0x800108B4)
            if (pc32 == 0x800108B4u && _dbgFill2FirstFire)
            {
                _dbgFill2FirstFire = false;
                uint p108C4raw = (pc32 >= 0x800108B4u && 0x108C4u < (uint)Bus.Ram.Length) ? Bus.Ram[0x108C4] : 0u;
                System.Console.Error.WriteLine($"[FILL2] first visit at step={DbgStepCount} RAM[0x108C4]=0x{p108C4raw:x2} icacheSet35valid={_iCacheValid[(0x108C4/64)&0xFF]}");
                System.Console.Error.Write("[FILL2] instructions 0x800108A8..0x800108F0: ");
                for (uint _fa = 0x800108A8u; _fa <= 0x800108F0u; _fa += 4u)
                    System.Console.Error.Write($"0x{FetchWord(_fa):x8} ");
                System.Console.Error.WriteLine();
            }

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

            // Debug: temporarily trace all BNE hits that have a nearby backward target.
            bool _dbgTrace = (pc32 >= 0x80010850u && pc32 <= 0x800108F0u);
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
            if (_dbgTrace) {
                int _lbl = (int)(pc32 - loopStart) / 4;
                System.Console.Error.Write($"[FF] pc=0x{pc32:x8} BNE rs={bneRs} loopStart=0x{loopStart:x8} len={_lbl} ds=0x{dsWord:x8} body: ");
                for (int _di = 0; _di < _lbl && _di < 12; _di++)
                    System.Console.Error.Write($"0x{FetchWord(loopStart+(uint)(_di*4)):x8} ");
                System.Console.Error.WriteLine();
            }
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
            if (_dbgTrace) {
                System.Console.Error.Write($"[FF] loopBodyLen={loopBodyLen} body: ");
                for (int _di = 0; _di < loopBodyLen && _di < 12; _di++)
                    System.Console.Error.Write($"0x{FetchWord(loopStart+(uint)(_di*4)):x8} ");
                System.Console.Error.WriteLine();
            }
            if (loopBodyLen < 4 || loopBodyLen > 6) return false;

            uint w0 = FetchWord(loopStart);          // addu
            uint w1 = FetchWord(loopStart + 4u);     // addiu -1
            uint w2 = FetchWord(loopStart + 8u);     // sb
            uint w3 = FetchWord(loopStart + 12u);    // slt
            if (_dbgTrace) System.Console.Error.WriteLine($"[FF] w0=0x{w0:x8} w1=0x{w1:x8} w2=0x{w2:x8} w3=0x{w3:x8} loopBodyLen={loopBodyLen}");

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

            // Debug: print $ra and check if 0x800108C4 is covered.
            uint _dbgRa = (uint)(Cpu.RegsLo[31] & 0xFFFFFFFFu);
            System.Console.Error.WriteLine($"[PREFETCH] ascending fill at pc=0x{pc32:x8} ra=0x{_dbgRa:x8} sp=0x{(uint)(Cpu.RegsLo[29]&0xFFFFFFFFu):x8}");

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

            // Snapshot of the kernel header region [0 .. physStart) for full-
            // RAM fills.  physStart is the physical byte address of the first
            // store; the loop wraps around and writes this region last, so on
            // real hardware it is overwritten after the rest of RAM — but by
            // then the kernel code the CPU still needs is in the i-cache and
            // the data structures are in the d-cache.  We restore it so the
            // emulator behaves the same way.
            //
            // We save at least MinKernelPreservation bytes (256 KB) regardless
            // of physStart, which is enough to cover the EE kernel at 0x1000
            // even when the fill starts at physStart=0.
            const int MinKernelPreservation = 256 * 1024;
            byte[]? kernelSnapshot = null;
            int     kernelSnapLen  = 0;
            if (remaining >= PS2MemoryBus.MainRamSize)
            {
                // Preserve at least 256 KB, or physStart bytes if physStart is
                // larger — whichever is greater, capped at actual RAM size.
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
