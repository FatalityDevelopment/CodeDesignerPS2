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
            if (discPath != null)
            {
                Console.WriteLine($"Loading disc: {discPath}");
                byte[] isoData;
                try
                {
                    isoData = File.ReadAllBytes(discPath);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error reading disc image: {ex.Message}");
                    return;
                }

                try
                {
                    uint entryPoint = GameLoader.BootFromDisc(isoData, scheduler.EE);
                    Console.WriteLine($"Game loaded. ELF entry point: 0x{entryPoint:X8}");
                }
                catch (GameLoadException ex)
                {
                    Console.Error.WriteLine($"Error loading game: {ex.Message}");
                    return;
                }
            }
            else
            {
                Console.WriteLine("No disc image provided — running BIOS only.");
            }

            // ── Subscribe to emulator events ──────────────────────────────────
            scheduler.EE.UnknownInstruction += (_, opcode) =>
                Console.WriteLine($"[EE] Unknown instruction: 0x{opcode:X8} at PC=0x{scheduler.EE.Cpu.PC:X8}");

            scheduler.EE.SyscallRaised += (_, code) =>
                Console.WriteLine($"[EE] SYSCALL 0x{code:X}");

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

            const int SliceSize     = 256;           // EE steps per scheduler call
            const int ReportEvery   = 1_000_000;     // print stats every N EE steps
            long      totalSteps    = 0;
            long      nextReport    = ReportEvery;
            var       startTime     = DateTime.UtcNow;

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
                        Console.WriteLine($"[Stats] EE steps: {totalSteps:N0}  IOP steps: {scheduler.TotalIopSteps:N0}  " +
                                          $"EE PC: 0x{scheduler.EE.Cpu.PC:X8}  Speed: {mips:F2} MIPS");
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
            Console.WriteLine($"Final EE PC: 0x{scheduler.EE.Cpu.PC:X8}");
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
    }
}
