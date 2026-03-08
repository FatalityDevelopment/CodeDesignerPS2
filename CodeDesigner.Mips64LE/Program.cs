using System.IO.Compression;
using CodeDesigner.Mips64LE.Common;
using CodeDesigner.Mips64LE.Common.Emulator;
using CodeDesigner.Mips64LE.Common.Emulator.IOP;

namespace CodeDesigner.Mips64LE
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("MIPS64 Emotion Engine - PlayStation 2 Toolkit");
            Console.WriteLine();

            if (HasFlag(args, "--help"))
            {
                PrintHelp();
                return;
            }

            if (HasFlag(args, "--graphics-info"))
            {
                PrintGraphicsInfo();
                return;
            }

            if (HasFlag(args, "--run"))
            {
                await RunEmulator(args);
                return;
            }

            // --server (or default legacy path): start the socket server for the VSCode extension
            await StartSocketServer();
        }

        // ── Emulator run mode ─────────────────────────────────────────────────

        /// <summary>
        /// Load a PS2 BIOS and optional disc image, then run the scheduler loop
        /// until the user presses Ctrl+C or the optional step limit is reached.
        ///
        /// Usage:
        ///   dotnet run -- --run [--bios SCPH-70012.bin] [--disc game.iso] [--steps 1000000]
        /// </summary>
        private static async Task RunEmulator(string[] args)
        {
            string? biosPath = GetArg(args, "--bios");
            string? discPath = GetArg(args, "--disc");
            string? stepsStr = GetArg(args, "--steps");

            biosPath ??= DefaultBiosPath();

            if (!File.Exists(biosPath))
            {
                Console.Error.WriteLine($"Error: BIOS file not found: {biosPath}");
                return;
            }

            // ── ZIP extraction (if disc is a .zip archive) ────────────────────
            // Extract the first ISO/BIN entry to the system temp directory.
            // The extracted file is cached so subsequent runs skip re-extraction.
            if (discPath != null &&
                discPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                discPath = ExtractIsoFromZip(discPath);
                if (discPath == null)
                    return;
            }

            if (discPath != null && !File.Exists(discPath))
            {
                Console.Error.WriteLine($"Error: Disc image not found: {discPath}");
                return;
            }

            long maxSteps = stepsStr != null && long.TryParse(stepsStr, out long parsed) ? parsed : long.MaxValue;

            // ── Load BIOS ─────────────────────────────────────────────────────
            Console.WriteLine($"Loading BIOS: {biosPath}");
            byte[] biosData;
            try
            {
                biosData = File.ReadAllBytes(biosPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error reading BIOS file: {ex.Message}");
                return;
            }

            var scheduler = new DualCpuScheduler();

            BiosInfo biosInfo;
            try
            {
                biosInfo = BiosLoader.Load(biosData, scheduler);
                Console.WriteLine($"BIOS loaded: {biosInfo}");
            }
            catch (BiosLoadException ex)
            {
                Console.Error.WriteLine($"Error loading BIOS: {ex.Message}");
                return;
            }

            // ── Load disc / game (optional) ───────────────────────────────────
            // Keep a reference to the disc stream so it can be disposed after
            // the run loop ends.  The IOP CDVD driver reads sectors on demand,
            // so the stream must stay open for the lifetime of the emulator.
            FileStream? isoStream = null;
            if (discPath != null)
            {
                Console.WriteLine($"Loading disc: {discPath}");
                try
                {
                    isoStream = new FileStream(
                        discPath, FileMode.Open, FileAccess.Read, FileShare.Read);

                    // Mount the disc in the IOP CDVD drive and let the BIOS boot
                    // the game naturally from 0xBFC00000.  Do NOT load the ELF
                    // directly: the BIOS ROM stubs at 0xBFC00480–0xBFC005EC are
                    // permanent infinite-loop placeholders that are only safe to
                    // hit during the brief startup window before the BIOS copies
                    // the real kernel to RAM at 0x80000080 and clears BEV.
                    // Loading the game ELF with CPU.PC set directly would bypass
                    // that kernel setup and cause every SYSCALL to spin forever.
                    // Reference: PCSX2 native-BIOS boot sequence.
                    string bootPath = GameLoader.InsertDisc(isoStream, scheduler);
                    if (bootPath.Length > 0)
                        Console.WriteLine($"Disc inserted. BIOS will boot: {bootPath}");
                    else
                        Console.WriteLine("Disc inserted. BIOS will boot the game.");
                }
                catch (IOException ex)
                {
                    isoStream?.Dispose();
                    Console.Error.WriteLine($"Error reading disc image: {ex.Message}");
                    return;
                }
                catch (UnauthorizedAccessException ex)
                {
                    isoStream?.Dispose();
                    Console.Error.WriteLine($"Error reading disc image: {ex.Message}");
                    return;
                }
                catch (GameLoadException ex)
                {
                    isoStream?.Dispose();
                    Console.Error.WriteLine($"Error loading game: {ex.Message}");
                    return;
                }
            }
            else
            {
                Console.WriteLine("No disc image provided — running BIOS only.");
            }

            // ── Subscribe to emulator events ──────────────────────────────────
            // Format matches PCSX2's Console.Error / BIOS_LOG output style.
            scheduler.EE.UnknownInstruction += (_, opcode) =>
            {
                uint eepc = (uint)(scheduler.EE.Cpu.PC & 0xFFFFFFFFUL);
                Console.WriteLine($"EE: Unknown opcode 0x{opcode:x8}  pc=0x{eepc:x8}");
            };

            // PCSX2 resolves the syscall name from $v1 (GPR[3] & 0xFF),
            // logged as:  HLE bios[code]: FunctionName  (pc=0xXXXXXXXX)
            scheduler.EE.SyscallRaised += (_, pc) =>
            {
                uint v1  = (uint)(scheduler.EE.Cpu.RegsLo[3] & 0xFF);
                string fn = BiosCallName((int)v1);
                Console.WriteLine($"EE: HLE bios[0x{v1:x}]: {fn}  (pc=0x{pc:x8})");
            };

            // ── Start socket server alongside the emulator ────────────────────
            // The VSCode extension connects on port 8765 for DISASSEMBLE / READMEM /
            // SNAPSHOT commands while the emulator is running.
            var embeddedServer = new SocketServer(8765) { Scheduler = scheduler };
            embeddedServer.ClientConnected  += (_, ep) => Console.WriteLine($"[Server] Client connected: {ep}");
            embeddedServer.ClientDisconnected += (_, _) => Console.WriteLine("[Server] Client disconnected");
            var serverTask = Task.Run(() => embeddedServer.StartAsync());

            // ── Run loop ──────────────────────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine("Emulator running. Press Ctrl+C to stop.");
            if (maxSteps != long.MaxValue)
                Console.WriteLine($"Step limit: {maxSteps:N0} EE steps.");
            Console.WriteLine();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("\nStopping emulator...");
            };

            const int SliceSize          = 256;           // EE steps per scheduler call
            const int ReportEvery        = 1_000_000;     // print stats every N EE steps
            const int SpinReportInterval = 5;             // compact spin reminder every N report periods
            const int MaxSpinReminders   = 3;             // max compact reminders per spin PC before silencing
            long      totalSteps    = 0;
            long      nextReport    = ReportEvery;
            var       startTime     = DateTime.UtcNow;
            uint      lastEePc      = uint.MaxValue;
            uint      lastIopPc     = uint.MaxValue;
            int       spinCount     = 0;
            int       iopSpinCount  = 0;
            bool      eeLeftBios    = false;   // true once the EE PC first enters RAM
            bool      iopBootDumped    = false; // true once IOP boot zone disasm dumped
            bool      iopLeftBootZone  = false; // true once IOP PC > 0x400
            bool      eeSyscallDetected = false; // true once EE kernel begins syscall setup
            bool      eeInterruptsEnabled = false; // true once EE IE+EIE both 1 for first time
            uint      lastEeRegion  = uint.MaxValue; // track EE region changes
            // Track PCs that have already had a full DumpInterruptState to prevent repeated dumps
            // when the EE/IOP happens to land on the same instruction across multiple report intervals
            // (e.g. a normal loop that is not a deadlock).
            var eeSpinDumpedPcs  = new System.Collections.Generic.HashSet<uint>();
            var iopSpinDumpedPcs = new System.Collections.Generic.HashSet<uint>();
            // Track compact reminder counts per spin PC so they don't repeat indefinitely.
            var eeSpinReminderCounts = new System.Collections.Generic.Dictionary<uint, int>();

            // ── IOP per-step register capture ─────────────────────────────────
            // Fire once when IOP transitions from BIOS ROM into IOP RAM, and once
            // for every unique IOP RAM PC in the first 5 MB of IOP RAM that we
            // haven't seen before — giving us a call-trace of the early boot.
            // Also dump ALL 32 GPRs whenever the memmove entry (0x2820) is hit.
            uint      iopRamFirstPc      = uint.MaxValue;
            bool      iopRamEntryDumped  = false;
            int       iopMemmoveCallCount = 0;
            int       iopICtrlPollCount   = 0;   // suppress spam after first N prints
            var       iopSeenPcs         = new System.Collections.Generic.HashSet<uint>();

            scheduler.IOP.StepHook = state =>
            {
                uint pc = state.PC;

                // Capture register state once when IOP first leaves BIOS ROM
                if (!iopRamEntryDumped && pc < 0x1FC00000u && pc >= 0x800u)
                {
                    iopRamEntryDumped = true;
                    iopRamFirstPc     = pc;
                    Console.WriteLine($"*** IOP first entered IOP RAM at PC=0x{pc:x8} — dumping all 32 GPRs ***");
                    DumpAllIopRegs(state);
                }

                // Trace unique IOP RAM PCs (first 128 KB) during early boot to build
                // a call-flow trace; stop tracing once we've logged 64 distinct PCs.
                if (iopRamEntryDumped && iopMemmoveCallCount == 0
                    && pc < 0x20000u && iopSeenPcs.Count < 64 && iopSeenPcs.Add(pc))
                {
                    string[] rn = {"zero","at","v0","v1","a0","a1","a2","a3",
                                    "t0","t1","t2","t3","t4","t5","t6","t7",
                                    "s0","s1","s2","s3","s4","s5","s6","s7",
                                    "t8","t9","k0","k1","gp","sp","fp","ra"};
                    var sb = new System.Text.StringBuilder();
                    sb.Append($"  IOP trace PC=0x{pc:x8}  ");
                    for (int ri = 0; ri < 32; ri++)
                        if (state.ReadReg(ri) != 0)
                            sb.Append($"{rn[ri]}=0x{state.ReadReg(ri):x8} ");
                    Console.WriteLine(sb.ToString());
                }

                // Full register dump at every memmove entry (IOP RAM 0x2820), first 20 calls
                if (pc == 0x2820u && iopMemmoveCallCount < 20)
                {
                    iopMemmoveCallCount++;
                    uint a2 = state.ReadReg(6);
                    // Print compact one-liner for small counts, full dump for large/suspicious counts
                    if (a2 > 0x1000u)
                    {
                        Console.WriteLine($"*** IOP memmove call #{iopMemmoveCallCount} (LARGE COUNT!) PC=0x2820 ***");
                        DumpAllIopRegs(state);
                    }
                    else
                    {
                        Console.WriteLine(
                            $"  IOP memmove call #{iopMemmoveCallCount}: a0=0x{state.ReadReg(4):x8} a1=0x{state.ReadReg(5):x8}" +
                            $" a2=0x{a2:x8} a3=0x{state.ReadReg(7):x8} ra=0x{state.ReadReg(31):x8}");
                    }
                    // On the first memmove call, also dump IOP RAM around the call site,
                    // the memmove function, and scan all IOP RAM for other jal-to-memmove sites.
                    if (iopMemmoveCallCount == 1)
                    {
                        Console.WriteLine("  IOP RAM 0x1820-0x18FC (wide context around both call sites):");
                        for (uint addr = 0x1820u; addr <= 0x18FCu; addr += 4)
                        {
                            uint iw = 0;
                            try { iw = scheduler.IOP.Bus.ReadWord(addr); } catch { }
                            string dis = MipsDisassembler.Disassemble(iw, addr);
                            string mark = (addr == 0x18A4u || addr == 0x18E8u) ? " ← jal memmove" :
                                          (addr == 0x18A8u || addr == 0x18ECu) ? " ← delay slot" : "";
                            Console.WriteLine($"    IOP RAM[0x{addr:x4}] = 0x{iw:x8}  {dis}{mark}");
                        }
                        Console.WriteLine("  IOP RAM 0x2800-0x2870:");
                        for (uint addr = 0x2800u; addr <= 0x2870u; addr += 4)
                        {
                            uint iw = 0;
                            try { iw = scheduler.IOP.Bus.ReadWord(addr); } catch { }
                            string dis = MipsDisassembler.Disassemble(iw, addr);
                            Console.WriteLine($"    IOP RAM[0x{addr:x4}] = 0x{iw:x8}  {dis}");
                        }
                        // Scan all IOP RAM (up to workspace boundary 0x20000) for jal to memmove
                        Console.WriteLine("  Scanning IOP RAM 0x800-0x20000 for ALL jal targets 0x2810/0x2820:");
                        for (uint scanAddr = 0x800u; scanAddr < 0x20000u; scanAddr += 4)
                        {
                            try
                            {
                                uint iw = scheduler.IOP.Bus.ReadWord(scanAddr);
                                if (iw == 0x0C000A04u || iw == 0x0C000A08u)
                                {
                                    string dis = MipsDisassembler.Disassemble(iw, scanAddr);
                                    Console.WriteLine($"    IOP RAM[0x{scanAddr:x4}] = 0x{iw:x8}  {dis}  ← jal to memmove");
                                    for (uint ctx = scanAddr > 8u ? scanAddr-8u : 0u; ctx <= scanAddr+8u; ctx += 4)
                                    {
                                        if (ctx == scanAddr) continue;
                                        try { uint ciw = scheduler.IOP.Bus.ReadWord(ctx);
                                              Console.WriteLine($"      ctx[0x{ctx:x4}] = 0x{ciw:x8}  {MipsDisassembler.Disassemble(ciw, ctx)}"); }
                                        catch { }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }

                // Capture ALL calls to 0x2810 with full details (no count cap)
                if (pc == 0x2810u)
                {
                    uint a0 = state.ReadReg(4); uint a1 = state.ReadReg(5);
                    uint a2 = state.ReadReg(6); uint a3 = state.ReadReg(7);
                    uint ra31 = state.ReadReg(31);
                    if (a2 > 0x1000u)
                    {
                        Console.WriteLine($"*** IOP 0x2810 call (LARGE COUNT a2=0x{a2:x8}) a0=0x{a0:x8} a1=0x{a1:x8} a3=0x{a3:x8} ra=0x{ra31:x8} ***");
                        DumpAllIopRegs(state);
                    }
                    else
                    {
                        Console.WriteLine(
                            $"  IOP 0x2810 call: a0=0x{a0:x8} a1=0x{a1:x8}" +
                            $" a2=0x{a2:x8} a3=0x{a3:x8} ra=0x{ra31:x8}");
                    }
                }

                // Trace the load of fp[0x50] (potential export count) at 0x18C4
                // This is the lw a2,0x50(fp) instruction that feeds the second memmove call.
                if (pc == 0x18C4u)
                {
                    uint fp30 = state.ReadReg(30);
                    uint val50 = 0;
                    try { val50 = scheduler.IOP.Bus.ReadWord(fp30 + 0x50u); } catch { }
                    uint val54 = 0;
                    try { val54 = scheduler.IOP.Bus.ReadWord(fp30 + 0x54u); } catch { }
                    Console.WriteLine($"  IOP 0x18C4 (lw a2,0x50(fp)): fp=0x{fp30:x8}  fp[0x50]=0x{val50:x8}(count?)  fp[0x54]=0x{val54:x8}(src?)");
                }

                // Trace the very first entries to the I_CTRL polling function (0x4078) and
                // capture what value is in I_CTRL at that moment.  Limit to 10 prints to
                // avoid flooding the console once the interrupt handler runs at normal rate.
                if (pc == 0x4078u && iopICtrlPollCount < 10)
                {
                    iopICtrlPollCount++;
                    uint iCtrl = scheduler.IOP.Bus.ICtrl;  // non-destructive read via property
                    uint iStat = scheduler.IOP.Bus.IStat;
                    uint iMask = scheduler.IOP.Bus.IMask;
                    uint iopSr = state.SR;
                    Console.WriteLine($"  IOP 0x4078 (I_CTRL poll #{iopICtrlPollCount}): I_CTRL=0x{iCtrl:x8} I_STAT=0x{iStat:x8} I_MASK=0x{iMask:x8} SR=0x{iopSr:x8} a0=0x{state.ReadReg(4):x8} ra=0x{state.ReadReg(31):x8}");
                    if (iopICtrlPollCount == 1)
                    {
                        // On first hit, dump the IOP RAM at 0x4070-0x40C0 to understand the event dispatch code.
                        Console.WriteLine("  IOP event-dispatch code at 0x4070-0x40C0:");
                        for (uint dumpAddr = 0x4070u; dumpAddr < 0x40C4u; dumpAddr += 4)
                        {
                            try
                            {
                                uint iw = scheduler.IOP.Bus.ReadWord(dumpAddr);
                                string dis = MipsDisassembler.Disassemble(iw, dumpAddr);
                                string marker = dumpAddr == 0x4078u ? " <-- ENTRY" : "";
                                Console.WriteLine($"    0x{dumpAddr:x8}: 0x{iw:x8}  {dis}{marker}");
                            }
                            catch { }
                        }
                    }
                    if (iopICtrlPollCount == 10)
                        Console.WriteLine("  IOP 0x4078: further I_CTRL poll traces suppressed.");
                }

                // One-shot: when the IOP first enters the WaitSema loop (0x16C3C), dump
                // the INTC state and IOP s0/s1 so we can see what condition is being waited on.
                if (pc == 0x16C3Cu && !iopSeenPcs.Contains(0x16C3Cu))
                {
                    iopSeenPcs.Add(0x16C3Cu);
                    uint iCtrlW = scheduler.IOP.Bus.ICtrl;
                    uint iStatW = scheduler.IOP.Bus.IStat;
                    uint iMaskW = scheduler.IOP.Bus.IMask;
                    uint s0 = state.ReadReg(16);
                    uint s1 = state.ReadReg(17);
                    Console.WriteLine($"  *** IOP first hit WaitSema loop (0x16C3C): s0=0x{s0:x8} s1=0x{s1:x8} I_CTRL=0x{iCtrlW:x8} I_STAT=0x{iStatW:x8} I_MASK=0x{iMaskW:x8} SR=0x{state.SR:x8} ***");
                    // Also dump the region around 0x16C3C and the WaitIntr/WaitSema called after
                    foreach (var (dumpStart, dumpEnd, label) in new (uint, uint, string)[]
                    {
                        (0x00016C00u, 0x00016D70u, "16C00-16D70"),
                        (0x000108C0u, 0x000108F0u, "108C0-108F0"),
                        (0x00017778u, 0x000177A0u, "17778-177A0"),
                        (0x000178D8u, 0x00017900u, "178D8-17900"),
                        (0x00016ABCu, 0x00016AECu, "16ABC-16AEC"),
                    })
                    {
                        Console.WriteLine($"  IOP RAM disasm [{label}]:");
                        for (uint da = dumpStart; da < dumpEnd; da += 4)
                        {
                            try
                            {
                                uint iw3 = scheduler.IOP.Bus.ReadWord(da);
                                string dis3 = MipsDisassembler.Disassemble(iw3, da);
                                string mark3 = (da == pc) ? " <-- PC" : "";
                                Console.WriteLine($"    0x{da:X8}: 0x{iw3:X8}  {dis3}{mark3}");
                            }
                            catch { }
                        }
                    }
                }

                // Trace ALL unique IOP PCs in the module range 0x10000-0x1FFFF
                // to map the complete SIFMAN execution path after the 0x16C3C loop.
                if (pc >= 0x00010000u && pc <= 0x0001FFFFu && !iopSeenPcs.Contains(pc ^ 0x55AA0000u))
                {
                    iopSeenPcs.Add(pc ^ 0x55AA0000u);
                    uint iStatT = scheduler.IOP.Bus.IStat;
                    uint iMaskT = scheduler.IOP.Bus.IMask;
                    uint a0T = state.ReadReg(4);
                    uint raT = state.ReadReg(31);
                    Console.WriteLine($"  [SIFMAN-module] PC=0x{pc:X8}  a0=0x{a0T:X8} ra=0x{raT:X8}  I_MASK=0x{iMaskT:X5} I_STAT=0x{iStatT:X5}");
                    // One-shot dump of SIFMAN dispatch loop at 0x1AE30 when first visited
                    if (pc == 0x0001AE30u && !iopSeenPcs.Contains(0xAE300000u))
                    {
                        iopSeenPcs.Add(0xAE300000u);
                        Console.WriteLine("  SIFMAN dispatch loop disasm [0x1AE30-0x1AF00]:");
                        for (uint da = 0x0001AE30u; da < 0x0001AF04u; da += 4)
                        {
                            try
                            {
                                uint iw = scheduler.IOP.Bus.ReadWord(da);
                                string ds = MipsDisassembler.Disassemble(iw, da);
                                Console.WriteLine($"    0x{da:X8}: 0x{iw:X8}  {ds}");
                            }
                            catch { }
                        }
                        // Also dump SIFMAN SIF0 state [0x2260C-0x22700]
                        Console.WriteLine("  SIFMAN SIF state [0x22700-0x22800]:");
                        for (uint da = 0x00022700u; da < 0x00022804u; da += 4)
                        {
                            try
                            {
                                uint iw = scheduler.IOP.Bus.ReadWord(da);
                                if (iw != 0) Console.WriteLine($"    [0x{da:X8}] = 0x{iw:X8}");
                            }
                            catch { }
                        }
                    }
                    // One-shot dump of SIFMAN's CDVDman callback init code at 0x1CE30
                    if (pc == 0x0001CE30u && !iopSeenPcs.Contains(0xCE300000u))
                    {
                        iopSeenPcs.Add(0xCE300000u);
                        Console.WriteLine("  SIFMAN CDVDman callback-init disasm [0x1CE30-0x1CF40]:");
                        for (uint da = 0x0001CE30u; da < 0x0001CF44u; da += 4)
                        {
                            try
                            {
                                uint iw = scheduler.IOP.Bus.ReadWord(da);
                                string ds = MipsDisassembler.Disassemble(iw, da);
                                Console.WriteLine($"    0x{da:X8}: 0x{iw:X8}  {ds}");
                            }
                            catch { }
                        }
                        // Dump SIFMAN data area [0x2260C-0x22660] which should have callback ptrs
                        Console.WriteLine("  SIFMAN data area [0x2260C-0x22660]:");
                        for (uint da = 0x0002260Cu; da < 0x00022664u; da += 4)
                        {
                            try
                            {
                                uint iw = scheduler.IOP.Bus.ReadWord(da);
                                Console.WriteLine($"    [0x{da:X8}] = 0x{iw:X8}");
                            }
                            catch { }
                        }
                        // Also dump SIFMAN data area [0x38A60-0x38A80]
                        Console.WriteLine("  SIFMAN ISR struct [0x38A60-0x38A80]:");
                        for (uint da = 0x00038A60u; da < 0x00038A80u; da += 4)
                        {
                            try
                            {
                                uint iw = scheduler.IOP.Bus.ReadWord(da);
                                Console.WriteLine($"    [0x{da:X8}] = 0x{iw:X8}");
                            }
                            catch { }
                        }
                    }
                    // One-shot dump of the CDVD ISR at 0x1F620-0x1F730 when we first enter 0x1F620
                    if (pc == 0x0001F620u && !iopSeenPcs.Contains(0xF6200000u))
                    {
                        iopSeenPcs.Add(0xF6200000u);
                        Console.WriteLine("  SIFMAN CDVD ISR disasm [0x1F620-0x1F820]:");
                        for (uint da = 0x0001F620u; da < 0x0001F824u; da += 4)
                        {
                            try
                            {
                                uint iw = scheduler.IOP.Bus.ReadWord(da);
                                string ds = MipsDisassembler.Disassemble(iw, da);
                                Console.WriteLine($"    0x{da:X8}: 0x{iw:X8}  {ds}");
                            }
                            catch { }
                        }
                        // Also dump CDVDman data at 0x11800 (CDVDman structure)
                        Console.WriteLine("  CDVDman struct [0x11800-0x11840]:");
                        for (uint da = 0x00011800u; da < 0x00011840u; da += 4)
                        {
                            try
                            {
                                uint iw = scheduler.IOP.Bus.ReadWord(da);
                                Console.WriteLine($"    [0x{da:X8}] = 0x{iw:X8}");
                            }
                            catch { }
                        }
                    }
                    // Every time SIFMAN ISR fires (0x1F620), dump a0 struct and key SIFMAN data
                    if (pc == 0x0001F620u)
                    {
                        uint sA0 = state.ReadReg(4);
                        try
                        {
                            uint fcode = scheduler.IOP.Bus.ReadWord(sA0 + 0x0Cu);
                            uint cbk1 = scheduler.IOP.Bus.ReadWord(0x00022650u);
                            uint cbk2 = scheduler.IOP.Bus.ReadWord(0x00022620u);
                            uint sA0_4 = scheduler.IOP.Bus.ReadWord(sA0 + 0x04u);
                            Console.WriteLine($"  [SIFMAN-ISR entry] a0=0x{sA0:X8}  s0[4]=0x{sA0_4:X8}  s0[C]=0x{fcode:X8}  [0x22650]=0x{cbk1:X8}  [0x22620]=0x{cbk2:X8}");
                        }
                        catch { }
                    }
                }

                // Trace unique IOP PCs in 0x8000-0xA7FF (IOP BIOS upper kernel area,
                // where SIFMAN's real function implementations live, e.g. 0x90BC).
                if (pc >= 0x00008000u && pc <= 0x0000A7FFu && !iopSeenPcs.Contains(pc ^ 0xAAAA0000u))
                {
                    iopSeenPcs.Add(pc ^ 0xAAAA0000u);
                    uint iStatT2 = scheduler.IOP.Bus.IStat;
                    uint iMaskT2 = scheduler.IOP.Bus.IMask;
                    uint a0T2 = state.ReadReg(4);
                    uint raT2 = state.ReadReg(31);
                    uint v0T2 = state.ReadReg(2);
                    Console.WriteLine($"  [BIOS-upper] PC=0x{pc:X8}  a0=0x{a0T2:X8} v0=0x{v0T2:X8} ra=0x{raT2:X8}  I_MASK=0x{iMaskT2:X5}");
                    // Dump 0x90B0-0x9200 when first entering 0x90BC (jal $00010DC4 target)
                    if (pc == 0x000090BCu && !iopSeenPcs.Contains(0x90BC0000u))
                    {
                        iopSeenPcs.Add(0x90BC0000u);
                        Console.WriteLine("  BIOS-upper disc-write func [0x90B0-0x9280]:");
                        for (uint da = 0x000090B0u; da < 0x00009284u; da += 4)
                        {
                            try
                            {
                                uint iw3 = scheduler.IOP.Bus.ReadWord(da);
                                string dis3 = MipsDisassembler.Disassemble(iw3, da);
                                string mk = (da == pc) ? " <-- PC" : "";
                                Console.WriteLine($"    0x{da:X8}: 0x{iw3:X8}  {dis3}{mk}");
                            }
                            catch { /* skip */ }
                        }
                    }
                    // Dump 0x86C0-0x8E80 when first entering 0x86D0 (SIFMAN disc-read func)
                    if (pc == 0x000086D0u && !iopSeenPcs.Contains(0x86D00000u))
                    {
                        iopSeenPcs.Add(0x86D00000u);
                        Console.WriteLine("  BIOS-upper SIFMAN disc-read func [0x86C0-0x8E80]:");
                        for (uint da = 0x000086C0u; da < 0x00008E84u; da += 4)
                        {
                            try
                            {
                                uint iw3 = scheduler.IOP.Bus.ReadWord(da);
                                string dis3 = MipsDisassembler.Disassemble(iw3, da);
                                Console.WriteLine($"    0x{da:X8}: 0x{iw3:X8}  {dis3}");
                            }
                            catch { /* skip */ }
                        }
                    }
                }

                // Trace all calls to EnableInterrupt (0x00004188) to see what bits are enabled and from where
                if (pc == 0x00004188u)
                {
                    uint a0en = state.ReadReg(4);
                    uint raEn = state.ReadReg(31);
                    Console.WriteLine($"  [EnableInterrupt] a0={a0en} (bit=0x{1u << (int)(a0en & 31):X}) ra=0x{raEn:X8}  I_MASK_before=0x{scheduler.IOP.Bus.IMask:X8}");
                }

                // Log writes to SIFMAN callback addresses [0x22650], [0x2264C], [0x22658], [0x22620], [0x38A6C]
                // These are the key SIFMAN ISR callback pointers we need to track when they get set
                if (pc >= 0x00004000u && pc <= 0x0001FFFFu)
                {
                    // Check if this instruction is a store word to any tracked address
                    try
                    {
                        uint instr = scheduler.IOP.Bus.ReadWord(pc);
                        uint op = instr >> 26;
                        if (op == 0x2Bu) // SW
                        {
                            int rs = (int)((instr >> 21) & 0x1F);
                            short imm = (short)(instr & 0xFFFF);
                            uint ea = (uint)((int)state.ReadReg(rs) + imm);
                            if (ea == 0x00022650u || ea == 0x0002264Cu || ea == 0x00022654u || ea == 0x00022658u || ea == 0x00022620u || ea == 0x00038A6Cu)
                            {
                                int rt = (int)((instr >> 16) & 0x1F);
                                uint val = state.ReadReg(rt);
                                Console.WriteLine($"  *** SIFMAN callback WRITE: [0x{ea:X8}] = 0x{val:X8}  PC=0x{pc:X8} ra=0x{state.ReadReg(31):X8}");
                            }
                        }
                    }
                    catch { }
                }

                // One-shot: trace unique IOP PCs in the CDVDman range 0xA800-0xCFFE
                // to show the complete CDVDman execution path after S-commands finish.
                if (pc >= 0x0000A800u && pc <= 0x0000CFFEu && !iopSeenPcs.Contains(pc | 0x80000000u))
                {
                    iopSeenPcs.Add(pc | 0x80000000u);
                    uint iStat = scheduler.IOP.Bus.IStat;
                    uint iMask = scheduler.IOP.Bus.IMask;
                    string iSel = $"I_MASK=0x{iMask:X5} I_STAT=0x{iStat:X5}";
                    Console.WriteLine($"  [CDVDman] PC=0x{pc:X8}  a0=0x{state.ReadReg(4):X8} a1=0x{state.ReadReg(5):X8} v0=0x{state.ReadReg(2):X8} ra=0x{state.ReadReg(31):X8}  {iSel}");
                    // Dump disasm when we first reach 0xAB30 (CDVDman is fully loaded by then)
                    if (pc == 0x0000AB30u && !iopSeenPcs.Contains(0xAB308000u))
                    {
                        iopSeenPcs.Add(0xAB308000u);
                        Console.WriteLine("  CDVDman loaded code dump [0xAA80-0xAD60]:");
                        for (uint da = 0x0000AA80u; da < 0x0000AD64u; da += 4)
                        {
                            try
                            {
                                uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                                string dis2 = MipsDisassembler.Disassemble(iw2, da);
                                string mark = (da == pc) ? " <-- PC" : "";
                                Console.WriteLine($"    0x{da:X8}: 0x{iw2:X8}  {dis2}{mark}");
                            }
                            catch { /* skip unloaded address */ }
                        }
                        // Also dump 0xB000-0xB280 (CDVDman init main function)
                        Console.WriteLine("  CDVDman init main [0xAFC0-0xB600]:");
                        for (uint da = 0x0000AFC0u; da < 0x0000B604u; da += 4)
                        {
                            try
                            {
                                uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                                string dis2 = MipsDisassembler.Disassemble(iw2, da);
                                Console.WriteLine($"    0x{da:X8}: 0x{iw2:X8}  {dis2}");
                            }
                            catch { /* skip unloaded address */ }
                        }
                    }
                    // Dump CDVDman callback-registration code when first reaching 0xB970
                    if (pc == 0x0000B970u && !iopSeenPcs.Contains(0xB9708000u))
                    {
                        iopSeenPcs.Add(0xB9708000u);
                        Console.WriteLine("  CDVDman callback-reg code [0xB960-0xBB50]:");
                        for (uint da = 0x0000B960u; da < 0x0000BB54u; da += 4)
                        {
                            try
                            {
                                uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                                string dis2 = MipsDisassembler.Disassemble(iw2, da);
                                string mark = (da == pc) ? " <-- PC" : "";
                                Console.WriteLine($"    0x{da:X8}: 0x{iw2:X8}  {dis2}{mark}");
                            }
                            catch { }
                        }
                        // Also dump CDVDman data struct at 0x11800
                        Console.WriteLine("  CDVDman struct [0x11800-0x11870]:");
                        for (uint da = 0x00011800u; da < 0x00011874u; da += 4)
                        {
                            try { Console.WriteLine($"    [0x{da:X8}] = 0x{scheduler.IOP.Bus.ReadWord(da):X8}"); } catch { }
                        }
                    }
                    // Dump disc-read handler when first reaching 0xAE10 area
                    if (pc == 0x0000AE10u && !iopSeenPcs.Contains(0xAE108000u))
                    {
                        iopSeenPcs.Add(0xAE108000u);
                        Console.WriteLine("  CDVDman disc-read handler dump [0xAE00-0xAF40]:");
                        for (uint da = 0x0000AE00u; da < 0x0000AF44u; da += 4)
                        {
                            try
                            {
                                uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                                string dis2 = MipsDisassembler.Disassemble(iw2, da);
                                string mark = (da == pc) ? " <-- PC" : "";
                                Console.WriteLine($"    0x{da:X8}: 0x{iw2:X8}  {dis2}{mark}");
                            }
                            catch { /* skip unloaded address */ }
                        }
                        // Dump the missing range AD64-AE00 and AF44-B500
                        Console.WriteLine("  CDVDman gap [0xAD64-0xAE00]:");
                        for (uint da = 0x0000AD64u; da < 0x0000AE04u; da += 4)
                        {
                            try
                            {
                                uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                                string dis2 = MipsDisassembler.Disassemble(iw2, da);
                                Console.WriteLine($"    0x{da:X8}: 0x{iw2:X8}  {dis2}");
                            }
                            catch { /* skip unloaded address */ }
                        }
                        Console.WriteLine("  CDVDman gap [0xAF44-0xB500]:");
                        for (uint da = 0x0000AF44u; da < 0x0000B504u; da += 4)
                        {
                            try
                            {
                                uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                                string dis2 = MipsDisassembler.Disassemble(iw2, da);
                                Console.WriteLine($"    0x{da:X8}: 0x{iw2:X8}  {dis2}");
                            }
                            catch { /* skip unloaded address */ }
                        }
                        Console.WriteLine("  CDVDman code [0xB500-0xC200]:");
                        for (uint da = 0x0000B500u; da < 0x0000C204u; da += 4)
                        {
                            try
                            {
                                uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                                string dis2 = MipsDisassembler.Disassemble(iw2, da);
                                Console.WriteLine($"    0x{da:X8}: 0x{iw2:X8}  {dis2}");
                            }
                            catch { /* skip unloaded address */ }
                        }
                        // Dump SIFMAN disc-read functions at 0x10DC4 and key functions
                        Console.WriteLine("  SIFMAN disc funcs [0x10D44-0x10F00]:");
                        for (uint da = 0x00010D44u; da < 0x00010F04u; da += 4)
                        {
                            try
                            {
                                uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                                string dis2 = MipsDisassembler.Disassemble(iw2, da);
                                Console.WriteLine($"    0x{da:X8}: 0x{iw2:X8}  {dis2}");
                            }
                            catch { /* skip unloaded address */ }
                        }
                        // Dump CDVDman 0x108F8 (the queue/signal function)
                        Console.WriteLine("  CDVDman queue/signal funcs [0x108C0-0x10930]:");
                        for (uint da = 0x000108C0u; da < 0x00010934u; da += 4)
                        {
                            try
                            {
                                uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                                string dis2 = MipsDisassembler.Disassemble(iw2, da);
                                Console.WriteLine($"    0x{da:X8}: 0x{iw2:X8}  {dis2}");
                            }
                            catch { /* skip unloaded address */ }
                        }
                        // Dump SIFMAN 0x163D8 (disc-read dispatcher called from CDVDman AE28)
                        Console.WriteLine("  SIFMAN disc-read dispatcher [0x162B0-0x16960]:");
                        for (uint da = 0x000162B0u; da < 0x00016964u; da += 4)
                        {
                            try
                            {
                                uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                                string dis2 = MipsDisassembler.Disassemble(iw2, da);
                                Console.WriteLine($"    0x{da:X8}: 0x{iw2:X8}  {dis2}");
                            }
                            catch { /* skip unloaded address */ }
                        }
                        // Also dump SIFMAN 0x162E0 data (the 'SIFMAN func struct' used at 86D0)
                        Console.WriteLine("  SIFMAN 0x162E0 struct/func:");
                        for (uint da = 0x000162E0u; da < 0x00016310u; da += 4)
                        {
                            try
                            {
                                uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                                string dis2 = MipsDisassembler.Disassemble(iw2, da);
                                Console.WriteLine($"    [0x{da:X8}] = 0x{iw2:X8}  {dis2}");
                            }
                            catch { /* skip */ }
                        }
                        // Dump CDVDman interface struct at 0x10E88 (a0 passed to 0x163D8)
                        Console.WriteLine("  CDVDman iface struct at 0x10E88:");
                        for (uint da = 0x00010E80u; da < 0x00010EE0u; da += 4)
                        {
                            try
                            {
                                uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                                Console.WriteLine($"    [0x{da:X8}] = 0x{iw2:X8}");
                            }
                            catch { /* skip */ }
                        }
                    }
                }

                // One-shot: when SIFMAN 0x10D98 is called from CDVDman (ra=0xAE30),
                // dump all regs and the struct at a0 to see what disc-read dispatch sees.
                if (pc == 0x00010D98u && state.ReadReg(31) == 0x0000AE30u && !iopSeenPcs.Contains(0xD98AE30u))
                {
                    iopSeenPcs.Add(0xD98AE30u);
                    uint sifA0 = state.ReadReg(4);
                    uint sifA1 = state.ReadReg(5);
                    Console.WriteLine($"  *** SIFMAN disc-read call (0x10D98->0x163D8): a0=0x{sifA0:X8} a1=0x{sifA1:X8} I_MASK=0x{scheduler.IOP.Bus.IMask:X8}");
                    // Dump the struct pointed to by a0
                    Console.WriteLine($"  Struct at a0=0x{sifA0:X8}:");
                    for (uint da = sifA0; da < sifA0 + 0x40u; da += 4)
                    {
                        try
                        {
                            uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                            Console.WriteLine($"    [0x{da:X8}+{da - sifA0:D2}] = 0x{iw2:X8}");
                        }
                        catch { break; }
                    }
                }

                // Trace when ra = 0x0000bc6c (the call at 0x0000bc68 leading to WaitSema-Y)
                // Fires on any instruction where ra has been set to this specific return address.
                if (state.ReadReg(31) == 0x0000bc6cu && !iopSeenPcs.Contains(0xBC6CB86Cu))
                {
                    iopSeenPcs.Add(0xBC6CB86Cu);
                    Console.WriteLine($"  *** ra==0x0000bc6c detected at PC=0x{pc:x8} (about to call WaitSema-Y) ***");
                    Console.WriteLine($"      I_MASK=0x{scheduler.IOP.Bus.IMask:X8}  I_STAT=0x{scheduler.IOP.Bus.IStat:X8}  a0=0x{state.ReadReg(4):X8}");
                    Console.WriteLine("      IOP RAM disasm [0x0000bb80-0x0000bd00]:");
                    for (uint da = 0x0000bb80u; da < 0x0000bd04u; da += 4)
                    {
                        try
                        {
                            uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                            string dis2 = MipsDisassembler.Disassemble(iw2, da);
                            string mark = (da == pc) ? " <-- PC" : "";
                            Console.WriteLine($"        0x{da:X8}: 0x{iw2:X8}  {dis2}{mark}");
                        }
                        catch { /* bus read may fail for unmapped/unloaded IOP RAM regions */ }
                    }
                }

                // One-shot: dump IOP RAM around 0x0000bc60 (the second WaitSema area)
                // when the IOP first visits any address in that region.
                if (pc >= 0x0000bba0u && pc <= 0x0000bce0u && !iopSeenPcs.Contains(0xBBBBBBBBu))
                {
                    iopSeenPcs.Add(0xBBBBBBBBu);
                    Console.WriteLine($"  *** IOP first entered WaitSema-Y region at PC=0x{pc:x8} ***");
                    Console.WriteLine($"      I_MASK=0x{scheduler.IOP.Bus.IMask:X8}  I_STAT=0x{scheduler.IOP.Bus.IStat:X8}  a0=0x{state.ReadReg(4):X8}  ra=0x{state.ReadReg(31):X8}");
                    Console.WriteLine("      IOP RAM disasm [0x0000bb80-0x0000bcf0]:");
                    for (uint da = 0x0000bb80u; da < 0x0000bcf4u; da += 4)
                    {
                        try
                        {
                            uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                            string dis2 = MipsDisassembler.Disassemble(iw2, da);
                            string mark = (da == pc) ? " <-- PC" : "";
                            Console.WriteLine($"        0x{da:X8}: 0x{iw2:X8}  {dis2}{mark}");
                        }
                        catch { /* skip unloaded address */ }
                    }
                }

                // One-shot: dump IOP RAM around the VBlank-init area to find SBUS enable
                if (pc >= 0x000120A0u && pc <= 0x000120F0u && !iopSeenPcs.Contains(0x12005555u))
                {
                    iopSeenPcs.Add(0x12005555u);
                    Console.WriteLine($"  *** IOP first entered VBlank-init area at PC=0x{pc:x8} ***");
                    Console.WriteLine("      IOP RAM disasm [0x00011FC0-0x00012140]:");
                    for (uint da = 0x00011FC0u; da < 0x00012144u; da += 4)
                    {
                        try
                        {
                            uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                            string dis2 = MipsDisassembler.Disassemble(iw2, da);
                            string mark = (da == pc) ? " <-- PC" : "";
                            Console.WriteLine($"        0x{da:X8}: 0x{iw2:X8}  {dis2}{mark}");
                        }
                        catch { /* skip unloaded address */ }
                    }
                }

                // One-shot: dump IOP RAM around 0x00004190-0x00004270 (I_MASK init function at 0x41DC)
                if (pc >= 0x00004190u && pc <= 0x00004270u && !iopSeenPcs.Contains(0x41414141u))
                {
                    iopSeenPcs.Add(0x41414141u);
                    Console.WriteLine($"  *** IOP first entered I_MASK-init region at PC=0x{pc:x8} ***");
                    Console.WriteLine($"      I_MASK=0x{scheduler.IOP.Bus.IMask:X8}  I_STAT=0x{scheduler.IOP.Bus.IStat:X8}");
                    Console.WriteLine("      IOP RAM disasm [0x00004170-0x00004280]:");
                    for (uint da = 0x00004170u; da < 0x00004284u; da += 4)
                    {
                        try
                        {
                            uint iw2 = scheduler.IOP.Bus.ReadWord(da);
                            string dis2 = MipsDisassembler.Disassemble(iw2, da);
                            string mark = (da == pc) ? " <-- PC" : "";
                            Console.WriteLine($"        0x{da:X8}: 0x{iw2:X8}  {dis2}{mark}");
                        }
                        catch { /* skip unloaded address */ }
                    }

                    // Also scan all IOP RAM for jal 0x4188 instructions (0x0C001062)
                    Console.WriteLine("  Scanning IOP RAM for all jal 0x4188 calls:");
                    const uint jalEnableIntr = 0x0C001062u; // jal 0x00004188
                    for (uint sa = 0u; sa < 0x80000u; sa += 4)
                    {
                        try
                        {
                            uint iws = scheduler.IOP.Bus.ReadWord(sa);
                            if (iws == jalEnableIntr)
                            {
                                // Print the instruction 2 before (usually "addiu a0, ...")
                                uint prev1 = 0, prev2 = 0;
                                try { prev2 = scheduler.IOP.Bus.ReadWord(sa - 8); } catch { /* skip */ }
                                try { prev1 = scheduler.IOP.Bus.ReadWord(sa - 4); } catch { /* skip */ }
                                uint next1 = 0;
                                try { next1 = scheduler.IOP.Bus.ReadWord(sa + 4); } catch { /* skip */ }
                                Console.WriteLine($"    jal 0x4188 at 0x{sa:X8}: prev2={MipsDisassembler.Disassemble(prev2, sa-8)}  prev={MipsDisassembler.Disassemble(prev1, sa-4)}  delay={MipsDisassembler.Disassemble(next1, sa+4)}");
                            }
                        }
                        catch { /* skip unloaded IOP RAM region */ }
                    }
                }
            };

            try
            {
                while (!cts.Token.IsCancellationRequested && totalSteps < maxSteps)
                {
                    // When paused, wait efficiently for a resume signal rather than busy-polling.
                    if (scheduler.Paused)
                    {
                        await scheduler.WaitForResumeAsync(cts.Token).ConfigureAwait(false);
                        continue;
                    }

                    int slice = (int)Math.Min(SliceSize, maxSteps - totalSteps);
                    try
                    {
                        scheduler.RunSlice(slice);
                    }
                    catch (CodeDesigner.Mips64LE.Common.Cpu.Instructions.EE.BreakpointException)
                    {
                        // BREAK is dispatched through COP0 by Step() (EPC saved,
                        // Status.EXL=1, Cause.ExcCode=Breakpoint) and then re-thrown.
                        // In --run mode we want execution to continue from the exception
                        // vector (already set in Cpu.PC by COP0ExceptionDispatcher).
                        uint bpPc = (uint)(scheduler.EE.Cpu.PC & 0xFFFFFFFFUL);
                        Console.WriteLine($"[BREAK → COP0 vector 0x{bpPc:x8}, continuing]");
                    }
                    scheduler.TickHardware((uint)slice);
                    totalSteps += slice;

                    if (totalSteps >= nextReport)
                    {
                        double elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                        double mips    = elapsed > 0 ? totalSteps / elapsed / 1_000_000.0 : 0;

                        uint eePc32 = (uint)(scheduler.EE.Cpu.PC & 0xFFFFFFFFUL);
                        uint iopPc  = scheduler.IOP.State.PC;

                        // Detect when the EE PC first enters RAM (leaves BIOS ROM).
                        // Physical < 0x02000000 = EE main RAM (mapped via KSEG0/KSEG1).
                        // This confirms the BIOS successfully loaded and jumped to the
                        // EE kernel at 0x80001000.
                        if (!eeLeftBios)
                        {
                            uint eePhys = PS2MemoryBus.TranslateAddress(eePc32);
                            if (eePhys < 0x02000000u)
                            {
                                eeLeftBios = true;
                                Console.WriteLine($"*** EE left BIOS ROM — now executing from RAM at 0x{eePc32:x8} (cycle:{totalSteps:N0}) ***");
                            }
                        }

                        // Detect when EE moves to a significantly different code region
                        // (top 8 bits of PC change = different 16 MB region).
                        uint eePcRegion = eePc32 & 0xFF000000u;
                        if (eeLeftBios && eePcRegion != lastEeRegion)
                        {
                            lastEeRegion = eePcRegion;
                            string eeInsn0 = DisassembleAt(scheduler.EE.Bus, eePc32);
                            Console.WriteLine($"*** EE moved to new region 0x{eePc32:x8} (cycle:{totalSteps:N0}) {eeInsn0} ***");
                        }

                        // Detect first time EE enters exception/syscall mode (EXL=1).
                        if (!eeSyscallDetected && eeLeftBios)
                        {
                            uint eeStatus = scheduler.EE.Cpu.COP0.ReadRegister32(12);
                            if ((eeStatus & 0x2u) != 0)  // EXL=1
                            {
                                eeSyscallDetected = true;
                                Console.WriteLine($"*** EE entered exception mode (EXL=1) at 0x{eePc32:x8} (cycle:{totalSteps:N0}) COP0.Status=0x{eeStatus:x8} ***");
                            }
                        }

                        // Detect when EE re-enables interrupts (IE=1, EIE=1, EXL=0, ERL=0).
                        if (!eeInterruptsEnabled && eeLeftBios)
                        {
                            uint eeStatus = scheduler.EE.Cpu.COP0.ReadRegister32(12);
                            if ((eeStatus & 0x10007u) == 0x10001u)  // IE=1, EXL=0, ERL=0, EIE=1
                            {
                                eeInterruptsEnabled = true;
                                Console.WriteLine($"*** EE interrupts ENABLED (IE=1,EIE=1) at 0x{eePc32:x8} (cycle:{totalSteps:N0}) Status=0x{eeStatus:x8} ***");
                                DumpInterruptState(scheduler);
                            }
                        }

                        // Dump IOP boot zone disassembly on first entry.
                        if (!iopBootDumped && iopPc < 0x1000u && iopPc >= 0x100u)
                        {
                            iopBootDumped = true;
                            Console.WriteLine($"*** IOP entered boot zone 0x{iopPc:x8} (IOP cycle:{scheduler.TotalIopSteps:N0}) — dumping boot zone disasm ***");
                            // Dump the full IOP boot zone including the exception stub at 0x80
                            DumpIopRamRegion(scheduler.IOP.Bus, 0x0u, 0x200u);
                        }

                        // Detect when IOP first leaves the boot zone (PC > 0x400).
                        if (!iopLeftBootZone && iopPc > 0x400u && iopPc < 0x1FC00000u)
                        {
                            iopLeftBootZone = true;
                            string iopInsn0 = DisassembleIopAt(scheduler.IOP.Bus, iopPc);
                            Console.WriteLine($"*** IOP left boot zone — now at 0x{iopPc:x8} (IOP cycle:{scheduler.TotalIopSteps:N0}) {iopInsn0} ***");
                            DumpInterruptState(scheduler);
                        }

                        // Track EE spin-loops: PC stuck at same address across consecutive
                        // report intervals.  Matches PCSX2's "EE is spinning" detection.
                        if (eePc32 == lastEePc)
                        {
                            spinCount++;
                            if (spinCount == 1 && !eeSpinDumpedPcs.Contains(eePc32))
                            {
                                // First time we detect a spin at this PC — dump full state once.
                                eeSpinDumpedPcs.Add(eePc32);
                                Console.WriteLine($"*** EE is spinning at 0x{eePc32:x8} [{GetPcRegion(eePc32)}] ***");
                                // Dump full interrupt / SIF / IOP state the first time we detect a spin
                                // so we can see immediately why the EE is not taking the interrupt.
                                DumpInterruptState(scheduler);
                            }
                            else if (spinCount % SpinReportInterval == 0)
                            {
                                // Compact reminder while still spinning — capped at MaxSpinReminders
                                // per PC so a persistent spin does not flood the console.
                                eeSpinReminderCounts.TryGetValue(eePc32, out int remCount);
                                if (remCount < MaxSpinReminders)
                                {
                                    eeSpinReminderCounts[eePc32] = remCount + 1;
                                    uint intcSt = scheduler.EE.Bus.Intc.Stat;
                                    uint intcMk = scheduler.EE.Bus.Intc.Mask;
                                    uint eeStatus = scheduler.EE.Cpu.COP0.ReadRegister32(12);
                                    uint gifStat = 0;
                                    try { gifStat = scheduler.EE.Bus.ReadWord(0xB0003020u); } catch { }
                                    ulong eeV1 = (ulong)scheduler.EE.Cpu.RegsLo[3];
                                    ulong eeS1 = (ulong)scheduler.EE.Cpu.RegsLo[17];
                                    Console.WriteLine(
                                        $"  [spin@{totalSteps:N0}] " +
                                        $"INTC STAT=0x{intcSt:x8} MASK=0x{intcMk:x8} " +
                                        $"COP0.Status=0x{eeStatus:x8} " +
                                        $"SIF.F240=0x{scheduler.EE.Bus.Sif.F240:x8} " +
                                        $"v1=0x{eeV1:x16} s1=0x{eeS1:x16}");
                                    if (remCount + 1 == MaxSpinReminders)
                                        Console.WriteLine($"  [spin@0x{eePc32:x8}] further spin reminders suppressed.");
                                }
                            }
                        }
                        else
                        {
                            spinCount  = 0;
                            lastEePc   = eePc32;
                        }

                        // Track IOP spin-loops independently.
                        if (iopPc == lastIopPc)
                        {
                            iopSpinCount++;
                            if (iopSpinCount == 1 && !iopSpinDumpedPcs.Contains(iopPc))
                            {
                                // First time we detect a spin at this IOP PC — dump full state once.
                                iopSpinDumpedPcs.Add(iopPc);
                                Console.WriteLine($"*** IOP is spinning at 0x{iopPc:x8} [{GetPcRegion(iopPc)}] ***");
                                DumpInterruptState(scheduler);
                            }
                        }
                        else
                        {
                            iopSpinCount = 0;
                            lastIopPc    = iopPc;
                        }

                        string eeInsn  = DisassembleAt(scheduler.EE.Bus, eePc32);
                        string iopInsn = DisassembleIopAt(scheduler.IOP.Bus, iopPc);

                        // Format mirrors PCSX2's EE/IOP trace log prefix:
                        //   (EE pc:0xbfc001a8, cycle:N) <disasm>
                        //   (IOP pc:0xbfc00120, cycle:N) <disasm>
                        Console.WriteLine($"(EE  pc:0x{eePc32:x8} [{GetPcRegion(eePc32)}], cycle:{totalSteps:N0}) {eeInsn}");
                        Console.WriteLine($"(IOP pc:0x{iopPc:x8} [{GetPcRegion(iopPc)}], cycle:{scheduler.TotalIopSteps:N0}) {iopInsn}");
                        // Periodic IOP INTC state snapshot (helps diagnose persistent WaitSema loops)
                        {
                            uint iopICtrl = scheduler.IOP.Bus.ICtrl;
                            uint iopIStat = scheduler.IOP.Bus.IStat;
                            uint iopIMask = scheduler.IOP.Bus.IMask;
                            Console.WriteLine($"     IOP INTC: I_STAT=0x{iopIStat:x8} I_MASK=0x{iopIMask:x8} I_CTRL=0x{iopICtrl:x8} pending={(iopIStat & iopIMask) != 0}");
                        }
                        Console.WriteLine($"     ~{mips:F2} MIPS");
                        Console.WriteLine();

                        nextReport += ReportEvery;
                    }
                }

                await Task.CompletedTask; // satisfy async signature
            }
            catch (OperationCanceledException) { /* Ctrl+C */ }

            double totalElapsed = (DateTime.UtcNow - startTime).TotalSeconds;
            Console.WriteLine();
            Console.WriteLine($"Emulator stopped after {totalSteps:N0} EE steps " +
                              $"({totalElapsed:F2}s).");
            uint finalEePc = (uint)(scheduler.EE.Cpu.PC & 0xFFFFFFFFUL);
            Console.WriteLine($"(EE  pc:0x{finalEePc:x8} [{GetPcRegion(finalEePc)}]) {DisassembleAt(scheduler.EE.Bus, finalEePc)}");
            Console.WriteLine($"(IOP pc:0x{scheduler.IOP.State.PC:x8} [{GetPcRegion(scheduler.IOP.State.PC)}])");

            embeddedServer.Stop();
            await serverTask.ConfigureAwait(false);

            isoStream?.Dispose();
        }

        // ── ZIP extraction ─────────────────────────────────────────────────────

        /// <summary>
        /// Extract the first ISO or BIN entry from a ZIP archive to the system temp
        /// directory.  The extracted file is cached: if a file with the same name and
        /// size already exists it is reused without re-extracting.
        /// </summary>
        /// <param name="zipPath">Path to the ZIP archive.</param>
        /// <returns>
        /// Absolute path to the extracted disc image, or <c>null</c> on failure.
        /// </returns>
        private static string? ExtractIsoFromZip(string zipPath)
        {
            Console.WriteLine($"Opening ZIP archive: {zipPath}");
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);

                var entry = archive.Entries.FirstOrDefault(e =>
                    e.Name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) ||
                    e.Name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    Console.Error.WriteLine("Error: No ISO/BIN disc image found inside the ZIP archive.");
                    return null;
                }

                // Sanitize the entry name to prevent path traversal attacks:
                // use only the final filename component, never trust the full entry path.
                string safeFileName = Path.GetFileName(entry.Name);
                if (string.IsNullOrEmpty(safeFileName))
                {
                    Console.Error.WriteLine("Error: ZIP entry has no valid filename.");
                    return null;
                }

                string tempPath = Path.Combine(Path.GetTempPath(), safeFileName);

                // Reuse a cached extraction when the file is already present,
                // has the expected uncompressed size, and the source ZIP has not
                // been modified since the extraction was last written.
                if (File.Exists(tempPath) &&
                    new FileInfo(tempPath).Length == entry.Length &&
                    File.GetLastWriteTimeUtc(tempPath) >= File.GetLastWriteTimeUtc(zipPath))
                {
                    Console.WriteLine($"Using cached extraction: {tempPath}");
                    return tempPath;
                }

                Console.WriteLine($"Extracting '{safeFileName}' ({entry.Length / 1_048_576.0:F0} MB) to temp folder...");
                entry.ExtractToFile(tempPath, overwrite: true);
                Console.WriteLine("Extraction complete.");
                return tempPath;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error extracting ZIP archive: {ex.Message}");
                return null;
            }
        }

        // ── Socket server mode ────────────────────────────────────────────────

        private static async Task StartSocketServer()
        {
            Console.WriteLine("Starting socket server on port 8765...");
            var server = new SocketServer(8765);

            server.MessageReceived += (_, message) =>
                Console.WriteLine($"[Server] Received: {message[..Math.Min(100, message.Length)]}...");

            server.ClientConnected  += (_, endpoint) =>
                Console.WriteLine($"[Server] Client connected: {endpoint}");

            server.ClientDisconnected += (_, _) =>
                Console.WriteLine("[Server] Client disconnected");

            var serverTask = server.StartAsync();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("\nShutting down server...");
                server.Stop();
            };

            Console.WriteLine("Server is running. Press Ctrl+C to stop.");

            try
            {
                await Task.Delay(-1, cts.Token);
            }
            catch (TaskCanceledException) { /* Expected on Ctrl+C */ }

            await serverTask;
        }

        // ── Help & documentation ──────────────────────────────────────────────

        private static void PrintHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run -- --run [--bios <bios.bin>] [--disc <game.iso>] [--steps N]");
            Console.WriteLine("      Run the PS2 emulator with a real BIOS (and optionally a game disc).");
            Console.WriteLine();
            Console.WriteLine("  dotnet run -- --server");
            Console.WriteLine("      Start the TCP socket server for the VSCode extension (port 8765).");
            Console.WriteLine();
            Console.WriteLine("  dotnet run -- --graphics-info");
            Console.WriteLine("      Print information about adding a graphical display window.");
            Console.WriteLine();
            Console.WriteLine("  dotnet run -- --help");
            Console.WriteLine("      Show this help message.");
            Console.WriteLine();
            Console.WriteLine("Options for --run:");
            Console.WriteLine("  --bios <path>   Path to a PS2 BIOS ROM (4 MB .bin file). Optional;");
            Console.WriteLine("                  defaults to SCPH-70012_BIOS_V12_USA_200.bin in the repo.");
            Console.WriteLine("  --disc <path>   Path to a PS2 ISO-9660 game disc image. Optional.");
            Console.WriteLine("  --steps N       Stop after N EE instruction steps (default: unlimited).");
        }

        /// <summary>
        /// Print a concise technical description of what it would take to add a
        /// real-time graphical display window to this emulator.
        ///
        /// The PS2 GS (Graphics Synthesizer) emulation in GS.cs already renders
        /// all primitives into a 4 MB VRAM buffer and exposes a
        /// GetFrameBuffer(circuit) method that returns an ARGB32 pixel array.
        /// To display that buffer in a window, the following steps are needed:
        ///
        ///   1. Add a windowing / OpenGL dependency (e.g. Silk.NET, OpenTK, or SDL2-CS).
        ///   2. Open a window sized to the GS display output (typically 640×240 NTSC
        ///      or 640×256 PAL, upscaled as desired).
        ///   3. Each video frame (~16.7 ms for NTSC 60 Hz, ~20 ms for PAL 50 Hz):
        ///        a. Run the scheduler for one frame's worth of EE cycles
        ///           (~4,734,240 EE cycles for NTSC).
        ///        b. Call scheduler.EE.Bus.Gs.GetFrameBuffer() to obtain the pixel array.
        ///        c. Upload pixels to a GPU texture (glTexSubImage2D or equivalent).
        ///        d. Draw a full-screen quad with that texture.
        ///        e. Swap buffers (present the frame).
        ///   4. Process window events (resize, close) and pump pad input via
        ///      scheduler.Pad0 / scheduler.Pad1 each frame.
        ///   5. Optionally mix SPU2 audio samples (Spu2.MixSamples) and write them
        ///      to an audio output stream (e.g. OpenAL or SDL2 audio callback).
        ///
        /// Recommended library: Silk.NET (NuGet: Silk.NET.OpenGL, Silk.NET.Windowing)
        ///   — cross-platform, .NET-native, actively maintained.
        /// </summary>
        private static void PrintGraphicsInfo()
        {
            Console.WriteLine("=== Adding a Graphics Display Window ===");
            Console.WriteLine();
            Console.WriteLine("The GS (Graphics Synthesizer) emulation already renders all PS2 primitives");
            Console.WriteLine("into a 4 MB VRAM buffer.  GetFrameBuffer() on GS.cs returns an ARGB32 pixel");
            Console.WriteLine("array ready to be displayed.  What is still needed:");
            Console.WriteLine();
            Console.WriteLine("STEP 1 — Add a windowing library");
            Console.WriteLine("  Recommended: Silk.NET (cross-platform, .NET-native)");
            Console.WriteLine("    dotnet add CodeDesigner.Mips64LE package Silk.NET.OpenGL");
            Console.WriteLine("    dotnet add CodeDesigner.Mips64LE package Silk.NET.Windowing");
            Console.WriteLine("  Alternatives: OpenTK 4.x, SDL2-CS");
            Console.WriteLine();
            Console.WriteLine("STEP 2 — Create a window sized to the GS display output");
            Console.WriteLine("  Typical PS2 output: 640×240 (NTSC progressive) or 640×256 (PAL).");
            Console.WriteLine("  Higher-resolution modes: 1280×224, 512×448 (interlaced).");
            Console.WriteLine("  Create an OpenGL context and a 640×480 (or upscaled) window.");
            Console.WriteLine();
            Console.WriteLine("STEP 3 — Implement the render loop (~60 FPS for NTSC, ~50 FPS for PAL)");
            Console.WriteLine("  Each frame:");
            Console.WriteLine("    a) Run ~4,734,240 EE cycles (NTSC) via scheduler.RunSlice() calls.");
            Console.WriteLine("    b) Call scheduler.EE.Bus.Gs.GetFrameBuffer() for the ARGB32 pixel array.");
            Console.WriteLine("    c) Upload pixels: glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, w, h,");
            Console.WriteLine("                                        GL_BGRA, GL_UNSIGNED_BYTE, pixels)");
            Console.WriteLine("    d) Draw a full-screen quad textured with that texture.");
            Console.WriteLine("    e) SwapBuffers / Present.");
            Console.WriteLine();
            Console.WriteLine("STEP 4 — Controller input");
            Console.WriteLine("  Map keyboard/gamepad events to scheduler.Pad0 / scheduler.Pad1 fields");
            Console.WriteLine("  (Buttons, RightStickX, RightStickY, LeftStickX, LeftStickY).");
            Console.WriteLine("  Call scheduler.PollPads() once per frame to push state into IOP RAM.");
            Console.WriteLine();
            Console.WriteLine("STEP 5 — Audio (optional)");
            Console.WriteLine("  Call scheduler.IOP.Bus.Spu2.MixSamples(count) to get stereo PCM samples,");
            Console.WriteLine("  then write them to an OpenAL or SDL2 audio stream at 48 kHz.");
            Console.WriteLine();
            Console.WriteLine("Estimated effort: ~500-800 lines of new C# across a new GraphicsWindow.cs");
            Console.WriteLine("(renderer) and updated Program.cs (window host + game loop).");
            Console.WriteLine();
            Console.WriteLine("Reference implementations to study:");
            Console.WriteLine("  • PCSX2 pcsx2/GS/Renderers/OpenGL/GSDeviceOGL.cpp");
            Console.WriteLine("  • PCSX2 pcsx2/Frontend/MainWindow.cpp (game loop / vsync timing)");
        }

        // ── PCSX2-style interrupt diagnostics ────────────────────────────────

        /// <summary>
        /// Dump EE and IOP interrupt state in a PCSX2-style format so that the
        /// root cause of a spin loop (EE waiting for interrupt that never arrives)
        /// can be identified quickly.  Emits one block per call.
        /// </summary>
        private static void DumpInterruptState(DualCpuScheduler sched)
        {
            // ── EE COP0 ──────────────────────────────────────────────────────
            uint eeStatus = sched.EE.Cpu.COP0.ReadRegister32(12);
            uint eeCause  = sched.EE.Cpu.COP0.ReadRegister32(13);
            bool eeIE    = (eeStatus & 0x00001u) != 0;   // global IE
            bool eeEXL   = (eeStatus & 0x00002u) != 0;   // exception level
            bool eeERL   = (eeStatus & 0x00004u) != 0;   // error level
            bool eeEIE   = (eeStatus & 0x10000u) != 0;   // PS2 extended IE (must be 1)
            bool eeIM2   = (eeStatus & 0x00400u) != 0;   // IM2 → INTC interrupt mask
            bool eeIM7   = (eeStatus & 0x08000u) != 0;   // IM7 → timer interrupt mask
            bool eeIP2   = (eeCause  & 0x00400u) != 0;   // IP2 (INTC pending in Cause)
            bool eeIP7   = (eeCause  & 0x08000u) != 0;   // IP7 (timer pending in Cause)

            uint intcStat = sched.EE.Bus.Intc.Stat;
            uint intcMask = sched.EE.Bus.Intc.Mask;

            // ── SIF ──────────────────────────────────────────────────────────
            uint sifF240   = sched.EE.Bus.Sif.F240;
            uint sifMsCom  = sched.EE.Bus.Sif.MsCom;
            uint sifSmCom  = sched.EE.Bus.Sif.SmCom;
            uint sifMsFlag = sched.EE.Bus.Sif.MsFlag;
            uint sifSmFlag = sched.EE.Bus.Sif.SmFlag;

            // ── IOP R3000A ───────────────────────────────────────────────────
            uint iopSR    = sched.IOP.State.SR;
            uint iopCause = sched.IOP.State.Cause;
            bool iopIEc   = (iopSR    & 0x001u) != 0;   // IEc — current IE
            bool iopBEV   = (iopSR    & 0x00400000u) != 0;  // BEV — bootstrap exception vectors
            bool iopIM2   = (iopSR    & 0x400u) != 0;   // IM2
            bool iopIP2   = (iopCause & 0x400u) != 0;   // IP2 (INTC pending)
            uint iopIStat = sched.IOP.Bus.IStat;
            uint iopIMask = sched.IOP.Bus.IMask;

            // ── GIF_STAT (physical 0x10003020) ───────────────────────────────
            uint gifStat = 0;
            try { gifStat = sched.EE.Bus.ReadWord(0xB0003020u); } catch { }

            // ── EE kernel at RAM 0x80001000 (physical 0x00001000) ────────────
            // Shows whether the BIOS successfully loaded the EE kernel into RAM.
            // If all zeros the kernel copy failed and the jr to 0x80001000 ran NOPs.
            uint kernelWord0 = 0, kernelWord1 = 0, kernelWord2 = 0, kernelWord3 = 0;
            try
            {
                // Read directly from Bus.Ram (bypasses i-cache) to show real RAM contents.
                var ram = sched.EE.Bus.Ram;
                if (ram.Length > 0x100F)
                {
                    kernelWord0 = (uint)(ram[0x1000] | (ram[0x1001]<<8) | (ram[0x1002]<<16) | (ram[0x1003]<<24));
                    kernelWord1 = (uint)(ram[0x1004] | (ram[0x1005]<<8) | (ram[0x1006]<<16) | (ram[0x1007]<<24));
                    kernelWord2 = (uint)(ram[0x1008] | (ram[0x1009]<<8) | (ram[0x100A]<<16) | (ram[0x100B]<<24));
                    kernelWord3 = (uint)(ram[0x100C] | (ram[0x100D]<<8) | (ram[0x100E]<<16) | (ram[0x100F]<<24));
                }
            }
            catch { }
            bool kernelLoaded = (kernelWord0 | kernelWord1 | kernelWord2 | kernelWord3) != 0;

            Console.WriteLine("=== interrupt diagnostics ===");
            Console.WriteLine(
                $"  EE  COP0.Status: 0x{eeStatus:x8}  " +
                $"IE={B(eeIE)} EXL={B(eeEXL)} ERL={B(eeERL)} EIE={B(eeEIE)} " +
                $"IM2={B(eeIM2)} IM7={B(eeIM7)}");
            Console.WriteLine(
                $"  EE  COP0.Cause:  0x{eeCause:x8}  IP2={B(eeIP2)} IP7={B(eeIP7)}");
            Console.WriteLine(
                $"  EE  INTC_STAT:   0x{intcStat:x8}  " +
                $"INTC_MASK: 0x{intcMask:x8}  " +
                $"pending={sched.EE.Bus.Intc.IsInterruptPending}");

            // ── EE key registers (v0=$2, a0=$4, sp=$29, ra=$31) ──────────────
            // These reveal where in the boot sequence the EE thread is blocked:
            //   v0 = return value / syscall number
            //   a0 = first argument to the current call
            //   sp = stack pointer (helps identify which thread is active)
            //   ra = return address (shows the call site that spun)
            ulong eeV0 = (ulong)sched.EE.Cpu.RegsLo[2];
            ulong eeV1 = (ulong)sched.EE.Cpu.RegsLo[3];   // v1 — loop counter
            ulong eeA0 = (ulong)sched.EE.Cpu.RegsLo[4];
            ulong eeS0 = (ulong)sched.EE.Cpu.RegsLo[16];  // s0 — BSS clear ptr
            ulong eeS1 = (ulong)sched.EE.Cpu.RegsLo[17];  // s1 — loop limit
            ulong eeS5 = (ulong)sched.EE.Cpu.RegsLo[21];  // s5 — memset index
            ulong eeS6 = (ulong)sched.EE.Cpu.RegsLo[22];  // s6 — memset base
            ulong eeSp = (ulong)sched.EE.Cpu.RegsLo[29];
            ulong eeRa = (ulong)sched.EE.Cpu.RegsLo[31];
            Console.WriteLine(
                $"  EE  v0=0x{eeV0:x16}  v1=0x{eeV1:x16}  a0=0x{eeA0:x16}");
            Console.WriteLine(
                $"  EE  s0=0x{eeS0:x16}  s1=0x{eeS1:x16}");
            Console.WriteLine(
                $"  EE  s5=0x{eeS5:x16}  s6=0x{eeS6:x16}");
            Console.WriteLine(
                $"  EE  sp=0x{eeSp:x16}  ra=0x{eeRa:x16}");

            Console.WriteLine(
                $"  SIF F240:0x{sifF240:x8}  " +
                $"MSCOM:0x{sifMsCom:x8}  SMCOM:0x{sifSmCom:x8}  " +
                $"MSFLAG:0x{sifMsFlag:x8}  SMFLAG:0x{sifSmFlag:x8}");
            Console.WriteLine(
                $"  IOP SR:    0x{iopSR:x8}  IEc={B(iopIEc)} BEV={B(iopBEV)} IM2={B(iopIM2)}");
            Console.WriteLine(
                $"  IOP Cause: 0x{iopCause:x8}  IP2={B(iopIP2)}");
            Console.WriteLine(
                $"  IOP I_STAT: 0x{iopIStat:x8}  " +
                $"I_MASK: 0x{iopIMask:x8}  " +
                $"I_CTRL: 0x{sched.IOP.Bus.ICtrl:x8}  " +
                $"pending={sched.IOP.Bus.IsInterruptPending}");

            // ── IOP key registers (v0=$2, a0=$4, sp=$29, ra=$31) ─────────────
            uint iopV0 = sched.IOP.State.ReadReg(2);
            uint iopV1 = sched.IOP.State.ReadReg(3);
            uint iopA0 = sched.IOP.State.ReadReg(4);
            uint iopA1 = sched.IOP.State.ReadReg(5);
            uint iopA2 = sched.IOP.State.ReadReg(6);
            uint iopA3 = sched.IOP.State.ReadReg(7);
            uint iopFp = sched.IOP.State.ReadReg(30);  // fp = $30
            uint iopSp = sched.IOP.State.ReadReg(29);
            uint iopRa = sched.IOP.State.ReadReg(31);
            Console.WriteLine(
                $"  IOP v0=0x{iopV0:x8}  v1=0x{iopV1:x8}  a0=0x{iopA0:x8}  a1=0x{iopA1:x8}");
            Console.WriteLine(
                $"  IOP a2=0x{iopA2:x8}  a3=0x{iopA3:x8}  fp=0x{iopFp:x8}  sp=0x{iopSp:x8}  ra=0x{iopRa:x8}");

            // Dump the IOP loop flag at fp+0x58 (= IOP RAM[fp+0x58]) which the
            // boot loop at 0x124 polls.  Non-zero means the IOP is waiting.
            if (iopFp != 0 && iopFp < (uint)IopMemoryBus.RamSize)
            {
                uint iopLoopFlagAddr = iopFp + 0x58u;
                uint iopLoopFlag = 0;
                try { iopLoopFlag = sched.IOP.Bus.ReadWord(iopLoopFlagAddr); } catch { }
                Console.WriteLine(
                    $"  IOP RAM[fp+0x58=0x{iopLoopFlagAddr:x8}] = 0x{iopLoopFlag:x8}  " +
                    $"(boot loop exits when this is 0)");
            }

            // ── GIF_STAT ──────────────────────────────────────────────────────
            // FQC = bits[27:24] = GIF FIFO queue count; loop exits when FQC == 0.
            // VU1BUSY = bit 1, PSEGEND = bit 0.
            Console.WriteLine(
                $"  GIF_STAT: 0x{gifStat:x8}  FQC={(gifStat >> 24) & 0xF}  VU1BUSY={(gifStat >> 1) & 1}  APATH={(gifStat >> 2) & 3}");

            // ── EE kernel at 0x80001000 ───────────────────────────────────────
            // Non-zero means the BIOS kernel loader put code there; all-zero means
            // the jr to 0x80001000 would have run NOPs (kernel load failed).
            Console.WriteLine(
                $"  EE RAM[0x80001000]: 0x{kernelWord0:x8} 0x{kernelWord1:x8} " +
                $"0x{kernelWord2:x8} 0x{kernelWord3:x8}" +
                $"  [{(kernelLoaded ? "KERNEL LOADED" : "ALL ZEROS — kernel not loaded!")}]");

            // ── EE context disassembly ────────────────────────────────────────
            // Show 6 instructions before the EE PC, the PC instruction, and 4 after,
            // so we can immediately see which loop body the EE is stuck in.
            uint eePc = (uint)(sched.EE.Cpu.PC & 0xFFFFFFFFUL);
            Console.WriteLine($"  EE context around pc=0x{eePc:x8}:");
            for (int offset = -24; offset <= 16; offset += 4)
            {
                uint addr = (uint)(eePc + offset);
                string arrow = offset == 0 ? " <-- PC" : "";
                try
                {
                    uint iword = sched.EE.Bus.ReadWord(addr);
                    string dis = MipsDisassembler.Disassemble(iword, addr);
                    Console.WriteLine($"    0x{addr:x8}: {dis}{arrow}");
                }
                catch
                {
                    Console.WriteLine($"    0x{addr:x8}: ??{arrow}");
                }
            }

            // ── EE caller context (return address) ───────────────────────────
            // When EE is in a small inner function called many times, show the
            // code at $ra (the caller's context) to reveal the outer loop structure.
            ulong eeRaForCtx = (ulong)sched.EE.Cpu.RegsLo[31];
            uint eeRa32 = (uint)(eeRaForCtx & 0xFFFFFFFFUL);
            if (eeRa32 >= 0x80000000u && eeRa32 < 0x90000000u && eeRa32 != eePc)
            {
                Console.WriteLine($"  EE caller context around ra=0x{eeRa32:x8}:");
                for (int offset = -24; offset <= 96; offset += 4)
                {
                    uint addr = (uint)(eeRa32 + offset);
                    string arrow = offset == 0 ? " <-- ra" : "";
                    try
                    {
                        uint iword = sched.EE.Bus.ReadWord(addr);
                        string dis = MipsDisassembler.Disassemble(iword, addr);
                        Console.WriteLine($"    0x{addr:x8}: {dis}{arrow}");
                    }
                    catch
                    {
                        Console.WriteLine($"    0x{addr:x8}: ??{arrow}");
                    }
                }
            }

            // ── EE outer-loop context (BEQL back-target at 0x80010530) ───────
            // Dump the start of the outer loop to understand the full copy structure.
            DumpEeRamRegion(sched.EE.Bus, 0x80010530u, 0x80010570u, "outer-loop start");
            // Also dump function prologue of the called copy function
            DumpEeRamRegion(sched.EE.Bus, 0x800103B0u, 0x800103D0u, "copy-func prologue");
            // Dump the SIF/SMFLAG processing loop (FAC8 → FBB0 → FBC0)
            DumpEeRamRegion(sched.EE.Bus, 0x8000FAC8u, 0x8000FC00u, "SMFLAG-reader");
            // Dump the EE EELOAD/SIF command region
            DumpEeRamRegion(sched.EE.Bus, 0x8000FC00u, 0x8000FF00u, "SIF-cmd-region");
            DumpEeRamRegion(sched.EE.Bus, 0x80012500u, 0x80012800u, "EELOAD-region");
            // Dump SIF data structure key fields
            try
            {
                uint sifFC8 = sched.EE.Bus.ReadWord(0x80023FC8u);
                uint sifFD0 = sched.EE.Bus.ReadWord(0x80023FD0u);
                uint sifFD4 = sched.EE.Bus.ReadWord(0x80023FD4u);
                uint sifFD8 = sched.EE.Bus.ReadWord(0x80023FD8u);
                uint sifFCC = sched.EE.Bus.ReadWord(0x80023FCCu);
                uint sif4124 = sched.EE.Bus.ReadWord(0x80024124u);
                Console.WriteLine($"  EE SIF struct: [3FC8]={sifFC8:X8} [3FCC]={sifFCC:X8} [3FD0]={sifFD0:X8} [3FD4]={sifFD4:X8} [3FD8]={sifFD8:X8}");
                Console.WriteLine($"  EE SIF struct: [4124]={sif4124:X8} (loop cond)");
            }
            catch { }
            // Dump key registers for outer loop diagnosis
            {
                ulong s1 = (ulong)sched.EE.Cpu.RegsLo[17];
                ulong s2 = (ulong)sched.EE.Cpu.RegsLo[18];
                ulong s3 = (ulong)sched.EE.Cpu.RegsLo[19];
                ulong s4 = (ulong)sched.EE.Cpu.RegsLo[20];
                ulong s5 = (ulong)sched.EE.Cpu.RegsLo[21];
                ulong s6 = (ulong)sched.EE.Cpu.RegsLo[22];
                ulong a1 = (ulong)sched.EE.Cpu.RegsLo[5];
                ulong a2 = (ulong)sched.EE.Cpu.RegsLo[6];
                ulong a3 = (ulong)sched.EE.Cpu.RegsLo[7];
                Console.WriteLine($"  EE outer-loop regs: s1=0x{s1:x16} s2=0x{s2:x16} s3=0x{s3:x16}");
                Console.WriteLine($"  EE outer-loop regs: s4=0x{s4:x16} s5=0x{s5:x16} s6=0x{s6:x16}");
                Console.WriteLine($"  EE inner-func regs: a1=0x{a1:x16} a2=0x{a2:x16} a3=0x{a3:x16}");
                // Read stack values to understand outer loop bound
                ulong sp = (ulong)sched.EE.Cpu.RegsLo[29];
                uint sp32 = (uint)(sp & 0xFFFFFFFFUL);
                uint stackBound = 0, stackBound0 = 0, stackBound1 = 0, stackBound2 = 0;
                try { stackBound  = sched.EE.Bus.ReadWord(sp32 + 0x84u); } catch { }
                try { stackBound0 = sched.EE.Bus.ReadWord(sp32 + 0x7Cu); } catch { }
                try { stackBound1 = sched.EE.Bus.ReadWord(sp32 + 0x80u); } catch { }
                try { stackBound2 = sched.EE.Bus.ReadWord(sp32 + 0x88u); } catch { }
                Console.WriteLine($"  EE stack[sp+0x7C]=0x{stackBound0:x8} [sp+0x80]=0x{stackBound1:x8} [sp+0x84]=0x{stackBound:x8} [sp+0x88]=0x{stackBound2:x8}");
                // Dump RAM around s2 (module table ptr) to see what the table looks like
                uint s2_32 = (uint)(s2 & 0xFFFFFFFFUL);
                if (s2_32 >= 0x80000000u && s2_32 < 0x90000000u)
                {
                    Console.WriteLine($"  EE RAM at s2=0x{s2_32:x8} (module table):");
                    for (int i = -4; i <= 12; i++)
                    {
                        uint addr = (uint)(s2_32 + i * 4);
                        try
                        {
                            uint val = sched.EE.Bus.ReadWord(addr);
                            Console.WriteLine($"    [0x{addr:x8}] = 0x{val:x8}");
                        }
                        catch { }
                    }
                }
            }

            // ── IOP context disassembly ───────────────────────────────────────
            // Show 6 instructions before the IOP PC, the PC instruction, and 7 after.
            // This wider window ensures the full loop body (e.g., 0x124–0x14c) is
            // visible regardless of which instruction the PC is currently at.
            uint iopPc = sched.IOP.State.PC;
            Console.WriteLine($"  IOP context around pc=0x{iopPc:x8}:");
            for (int offset = -24; offset <= 28; offset += 4)
            {
                uint addr = (uint)(iopPc + offset);
                string arrow = offset == 0 ? " <-- PC" : "";
                try
                {
                    uint iword = sched.IOP.Bus.ReadWord(addr);
                    string dis = MipsDisassembler.Disassemble(iword, addr);
                    Console.WriteLine($"    0x{addr:x8}: {dis}{arrow}");
                }
                catch
                {
                    Console.WriteLine($"    0x{addr:x8}: ??{arrow}");
                }
            }

            // ── CDVD state ────────────────────────────────────────────────────
            // Shows whether the BIOS has issued any disc read commands and whether
            // the CDVD interrupt has fired.  Key to diagnosing a bootloop where the
            // BIOS stalls waiting for the disc to respond.
            var cdvd = sched.IOP.Bus.Cdvd;
            if (cdvd != null)
            {
                byte cdvdIstat  = cdvd.ReadRegister(0x08); // INTR_STAT (W1C)
                byte cdvdStatus = cdvd.ReadRegister(0x09); // drive status
                byte cdvdCrt    = cdvd.ReadRegister(0x0B); // current disc type
                Console.WriteLine(
                    $"  CDVD DiscType:0x{cdvd.DiscType:x2}  HasDisc:{cdvd.HasDisc}  " +
                    $"ISTAT:0x{cdvdIstat:x2}  STATUS:0x{cdvdStatus:x2}  CRT:0x{cdvdCrt:x2}");
            }
            else
            {
                Console.WriteLine("  CDVD (no disc mounted)");
            }

            Console.WriteLine("=============================");
        }

        /// <summary>1 or 0 string for a boolean flag, matching PCSX2 log style.</summary>
        private static string B(bool v) => v ? "1" : "0";

        /// <summary>
        /// Dump IOP RAM as disassembly from <paramref name="startAddr"/> to
        /// <paramref name="endAddr"/> (exclusive).  Used to reveal the full structure of
        /// IOP boot-zone loops.
        /// </summary>
        private static void DumpIopRamRegion(IopMemoryBus bus, uint startAddr, uint endAddr)
        {
            Console.WriteLine($"  IOP RAM disasm 0x{startAddr:x8}–0x{endAddr:x8}:");
            for (uint addr = startAddr; addr < endAddr; addr += 4)
            {
                try
                {
                    uint iword = bus.ReadWord(addr);
                    string dis = MipsDisassembler.Disassemble(iword, addr);
                    Console.WriteLine($"    0x{addr:x8}: 0x{iword:x8}  {dis}");
                }
                catch
                {
                    Console.WriteLine($"    0x{addr:x8}: ??");
                }
            }
        }

        /// <summary>
        /// Dump EE bus memory as disassembly from <paramref name="startAddr"/> to
        /// <paramref name="endAddr"/> (exclusive).  Used to reveal loop structures.
        /// </summary>
        private static void DumpEeRamRegion(PS2MemoryBus bus, uint startAddr, uint endAddr, string label)
        {
            Console.WriteLine($"  EE RAM disasm [{label}] 0x{startAddr:x8}–0x{endAddr:x8}:");
            for (uint addr = startAddr; addr < endAddr; addr += 4)
            {
                try
                {
                    uint iword = bus.ReadWord(addr);
                    string dis = MipsDisassembler.Disassemble(iword, addr);
                    Console.WriteLine($"    0x{addr:x8}: 0x{iword:x8}  {dis}");
                }
                catch
                {
                    Console.WriteLine($"    0x{addr:x8}: ??");
                }
            }
        }

        /// <summary>
        /// Print all 32 IOP general-purpose registers in a compact 4-column layout.
        /// </summary>
        private static void DumpAllIopRegs(CodeDesigner.Mips64LE.Common.Emulator.IOP.IopState state)
        {
            string[] rn = {"zero","at  ","v0  ","v1  ","a0  ","a1  ","a2  ","a3  ",
                            "t0  ","t1  ","t2  ","t3  ","t4  ","t5  ","t6  ","t7  ",
                            "s0  ","s1  ","s2  ","s3  ","s4  ","s5  ","s6  ","s7  ",
                            "t8  ","t9  ","k0  ","k1  ","gp  ","sp  ","fp  ","ra  "};
            for (int i = 0; i < 32; i += 4)
            {
                Console.WriteLine(
                    $"  ${i:D2} {rn[i]}=0x{state.ReadReg(i):x8}  " +
                    $"${i+1:D2} {rn[i+1]}=0x{state.ReadReg(i+1):x8}  " +
                    $"${i+2:D2} {rn[i+2]}=0x{state.ReadReg(i+2):x8}  " +
                    $"${i+3:D2} {rn[i+3]}=0x{state.ReadReg(i+3):x8}");
            }
            Console.WriteLine($"  HI=0x{state.HI:x8}  LO=0x{state.LO:x8}  PC=0x{state.PC:x8}  SR=0x{state.SR:x8}");
        }



        /// <summary>
        /// Safely disassemble one IOP instruction from its virtual PC.
        /// Returns a placeholder on any bus access failure.
        /// </summary>
        private static string DisassembleIopAt(IopMemoryBus bus, uint pc)
        {
            try
            {
                uint word = bus.ReadWord(pc);
                return MipsDisassembler.Disassemble(word, pc);
            }
            catch
            {
                return "??";
            }
        }

        // ── Argument helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the path to the bundled USA BIOS (SCPH-70012) by walking up from
        /// the application's base directory to the repository root, mirroring the
        /// pattern used in the test project.
        /// </summary>
        private static string DefaultBiosPath()
        {
            string baseDir  = AppContext.BaseDirectory;
            string repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            return Path.Combine(repoRoot,
                "CodeDesigner.Mips64LE.Common", "Bios", "SCPH-70012_BIOS_V12_USA_200.bin");
        }

        private static bool HasFlag(string[] args, string flag) =>
            args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

        private static string? GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        // ── PCSX2-style debug helpers ─────────────────────────────────────────

        /// <summary>
        /// Returns a short region label for the given virtual address, matching
        /// the notation used in PCSX2's memory-map comments.
        /// </summary>
        private static string GetPcRegion(uint vaddr)
        {
            uint phys = PS2MemoryBus.TranslateAddress(vaddr);

            // BIOS ROM: physical 0x1FC00000–0x1FFFFFFF
            if (phys >= 0x1FC00000u && phys < 0x20000000u)
                return (vaddr & 0xC0000000u) == 0xA0000000u ? "KSEG1/BIOS" : "KSEG0/BIOS";

            // Main RAM: physical 0x00000000–0x01FFFFFF
            if (phys < 0x02000000u)
                return "RAM";

            // Scratchpad: physical 0x70000000–0x70003FFF
            if (vaddr >= 0x70000000u && vaddr < 0x70004000u)
                return "SPad";

            // Hardware I/O registers
            if (phys >= 0x10000000u && phys < 0x10010000u)
                return "I/O";

            // GS privileged registers
            if (phys >= 0x12000000u && phys < 0x12002000u)
                return "GS";

            return "?";
        }

        /// <summary>
        /// Safely disassemble the instruction word at <paramref name="pc"/> using
        /// the PS2 memory bus.  Returns a placeholder on any access failure.
        /// </summary>
        private static string DisassembleAt(PS2MemoryBus bus, uint pc)
        {
            try
            {
                uint word = bus.ReadWord(pc);
                return MipsDisassembler.Disassemble(word, pc);
            }
            catch
            {
                return "??";
            }
        }

        /// <summary>
        /// Resolve a PS2 BIOS syscall number to its function name, exactly as
        /// PCSX2's <c>R5900::bios[]</c> table in R5900OpcodeImpl.cpp does.
        /// The code is taken from GPR $v1 (register 3) byte 0.
        /// </summary>
        private static string BiosCallName(int code)
        {
            // Table copied verbatim from PCSX2 pcsx2/R5900OpcodeImpl.cpp
            // R5900::bios[256].  Index = v1 & 0xFF.
            string[] bios =
            {
                // 0x00
                "RFU000_FullReset", "ResetEE",             "SetGsCrt",             "RFU003",
                "Exit",             "RFU005",               "LoadExecPS2",          "ExecPS2",
                "RFU008",           "RFU009",               "AddSbusIntcHandler",   "RemoveSbusIntcHandler",
                "Interrupt2Iop",    "SetVTLBRefillHandler", "SetVCommonHandler",    "SetVInterruptHandler",
                // 0x10
                "AddIntcHandler",   "RemoveIntcHandler",    "AddDmacHandler",       "RemoveDmacHandler",
                "_EnableIntc",      "_DisableIntc",         "_EnableDmac",          "_DisableDmac",
                "_SetAlarm",        "_ReleaseAlarm",        "_iEnableIntc",         "_iDisableIntc",
                "_iEnableDmac",     "_iDisableDmac",        "_iSetAlarm",           "_iReleaseAlarm",
                // 0x20
                "CreateThread",          "DeleteThread",      "StartThread",              "ExitThread",
                "ExitDeleteThread",      "TerminateThread",   "iTerminateThread",         "DisableDispatchThread",
                "EnableDispatchThread",  "ChangeThreadPriority", "iChangeThreadPriority", "RotateThreadReadyQueue",
                "iRotateThreadReadyQueue","ReleaseWaitThread","iReleaseWaitThread",        "GetThreadId",
                // 0x30
                "ReferThreadStatus","iReferThreadStatus",   "SleepThread",          "WakeupThread",
                "_iWakeupThread",   "CancelWakeupThread",   "iCancelWakeupThread",  "SuspendThread",
                "iSuspendThread",   "ResumeThread",         "iResumeThread",        "JoinThread",
                "RFU060",           "RFU061",               "EndOfHeap",            "RFU063",
                // 0x40
                "CreateSema",       "DeleteSema",           "SignalSema",           "iSignalSema",
                "WaitSema",         "PollSema",             "iPollSema",            "ReferSemaStatus",
                "iReferSemaStatus", "RFU073",               "SetOsdConfigParam",    "GetOsdConfigParam",
                "GetGsHParam",      "GetGsVParam",          "SetGsHParam",          "SetGsVParam",
                // 0x50
                "RFU080_CreateEventFlag",  "RFU081_DeleteEventFlag",
                "RFU082_SetEventFlag",     "RFU083_iSetEventFlag",
                "RFU084_ClearEventFlag",   "RFU085_iClearEventFlag",
                "RFU086_WaitEventFlag",    "RFU087_PollEventFlag",
                "RFU088_iPollEventFlag",   "RFU089_ReferEventFlagStatus",
                "RFU090_iReferEventFlagStatus", "RFU091_GetEntryAddress",
                "EnableIntcHandler_iEnableIntcHandler",
                "DisableIntcHandler_iDisableIntcHandler",
                "EnableDmacHandler_iEnableDmacHandler",
                "DisableDmacHandler_iDisableDmacHandler",
                // 0x60
                "KSeg0",            "EnableCache",          "DisableCache",         "GetCop0",
                "FlushCache",       "RFU101",               "CpuConfig",            "iGetCop0",
                "iFlushCache",      "RFU105",               "iCpuConfig",           "sceSifStopDma",
                "SetCPUTimerHandler","SetCPUTimer",          "SetOsdConfigParam2",   "GetOsdConfigParam2",
                // 0x70
                "GsGetIMR_iGsGetIMR",               "GsGetIMR_iGsPutIMR",   "SetPgifHandler",               "SetVSyncFlag",
                "RFU116",                           "print",                "sceSifDmaStat_isceSifDmaStat", "sceSifSetDma_isceSifSetDma",
                "sceSifSetDChain_isceSifSetDChain", "sceSifSetReg",         "sceSifGetReg",                 "ExecOSD",
                "Deci2Call",                        "PSMode",               "MachineType",                  "GetMemorySize",
            };

            int idx = code & 0xFF;
            return idx < bios.Length ? bios[idx] : $"syscall_{idx:x2}";
        }
    }
}
