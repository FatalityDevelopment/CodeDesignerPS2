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
            long      totalSteps    = 0;
            long      nextReport    = ReportEvery;
            var       startTime     = DateTime.UtcNow;
            uint      lastEePc      = uint.MaxValue;
            uint      lastIopPc     = uint.MaxValue;
            int       spinCount     = 0;
            int       iopSpinCount  = 0;
            bool      eeLeftBios    = false;   // true once the EE PC first enters RAM

            try
            {
                while (!cts.Token.IsCancellationRequested && totalSteps < maxSteps)
                {
                    int slice = (int)Math.Min(SliceSize, maxSteps - totalSteps);
                    scheduler.RunSlice(slice);
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

                        // Track EE spin-loops: PC stuck at same address across consecutive
                        // report intervals.  Matches PCSX2's "EE is spinning" detection.
                        if (eePc32 == lastEePc)
                        {
                            spinCount++;
                            if (spinCount == 1)
                            {
                                Console.WriteLine($"*** EE is spinning at 0x{eePc32:x8} [{GetPcRegion(eePc32)}] ***");
                                // Dump full interrupt / SIF / IOP state the first time we detect a spin
                                // so we can see immediately why the EE is not taking the interrupt.
                                DumpInterruptState(scheduler);
                            }
                            else if (spinCount % SpinReportInterval == 0)
                            {
                                // Compact reminder every 5M EE steps while still spinning
                                uint intcSt = scheduler.EE.Bus.Intc.Stat;
                                uint intcMk = scheduler.EE.Bus.Intc.Mask;
                                uint eeStatus = scheduler.EE.Cpu.COP0.ReadRegister32(12);
                                uint gifStat = scheduler.EE.Bus.ReadWord(0xB0003020u);
                                Console.WriteLine(
                                    $"  [spin@{totalSteps:N0}] " +
                                    $"INTC STAT=0x{intcSt:x8} MASK=0x{intcMk:x8} " +
                                    $"COP0.Status=0x{eeStatus:x8} " +
                                    $"SIF.F240=0x{scheduler.EE.Bus.Sif.F240:x8} " +
                                    $"GIF_STAT=0x{gifStat:x8}");
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
                            if (iopSpinCount == 1)
                            {
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
                kernelWord0 = sched.EE.Bus.ReadWord(0x80001000u);
                kernelWord1 = sched.EE.Bus.ReadWord(0x80001004u);
                kernelWord2 = sched.EE.Bus.ReadWord(0x80001008u);
                kernelWord3 = sched.EE.Bus.ReadWord(0x8000100Cu);
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
            ulong eeA0 = (ulong)sched.EE.Cpu.RegsLo[4];
            ulong eeSp = (ulong)sched.EE.Cpu.RegsLo[29];
            ulong eeRa = (ulong)sched.EE.Cpu.RegsLo[31];
            Console.WriteLine(
                $"  EE  v0=0x{eeV0:x16}  a0=0x{eeA0:x16}");
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
                $"pending={sched.IOP.Bus.IsInterruptPending}");

            // ── IOP key registers (v0=$2, a0=$4, sp=$29, ra=$31) ─────────────
            uint iopV0 = sched.IOP.State.ReadReg(2);
            uint iopA0 = sched.IOP.State.ReadReg(4);
            uint iopSp = sched.IOP.State.ReadReg(29);
            uint iopRa = sched.IOP.State.ReadReg(31);
            Console.WriteLine(
                $"  IOP v0=0x{iopV0:x8}  a0=0x{iopA0:x8}  sp=0x{iopSp:x8}  ra=0x{iopRa:x8}");

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
            // Show 4 instructions before the EE PC, the PC instruction, and 1 after,
            // so we can immediately see which loop body the EE is stuck in.
            uint eePc = (uint)(sched.EE.Cpu.PC & 0xFFFFFFFFUL);
            Console.WriteLine($"  EE context around pc=0x{eePc:x8}:");
            for (int offset = -16; offset <= 4; offset += 4)
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

            // ── IOP context disassembly ───────────────────────────────────────
            // Show 2 instructions before the IOP PC, the PC instruction, and 1 after.
            uint iopPc = sched.IOP.State.PC;
            Console.WriteLine($"  IOP context around pc=0x{iopPc:x8}:");
            for (int offset = -8; offset <= 4; offset += 4)
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
