using System.IO.Compression;
using CodeDesigner.Ps2.EmulatorCore;
using CodeDesigner.Ps2.EmulatorCore.IOP;

namespace CodeDesigner.Ps2.Emulator
{
    /// <summary>
    /// CodeDesigner.Ps2.Emulator — standalone console host for the built-in PS2 emulator.
    ///
    /// This is the home of the in-process emulator that used to live inside
    /// CodeDesigner.Ps2.Bridge. Bridge keeps the PCSX2 PINE bridge mode; this
    /// project hosts the original built-in emulator as its own process so the two
    /// modes are clearly separated.
    ///
    /// CLI:
    ///   Emulator.exe --bios &lt;bios.bin&gt; [--disc &lt;game.iso|game.zip&gt;] [--steps N]
    ///   Emulator.exe --file &lt;game.elf|game.bin&gt; [--load-address 0xN]
    ///   Emulator.exe [--port 28012]   ← runs idle waiting for InternalPineSocket commands
    ///
    /// The emulator exposes an <see cref="InternalPineSocket"/> on the configured
    /// port (default 28012) that mimics the PCSX2 PINE wire protocol. Bridge
    /// connects to it via <c>InternalEmulatorClient</c> the same way it talks to
    /// PCSX2 via <c>PineClient</c>.
    /// </summary>
    public static class Program
    {
        public const int DefaultInternalPort = 28012;

        public static async Task<int> Main(string[] args)
        {
            Console.WriteLine("CodeDesigner.Ps2.Emulator — built-in PS2 emulator host");

            if (HasFlag(args, "--help") || HasFlag(args, "-h"))
            {
                PrintHelp();
                return 0;
            }

            int port = DefaultInternalPort;
            if (GetArg(args, "--port") is string portStr && int.TryParse(portStr, out int parsedPort))
                port = parsedPort;

            // File mode — load an ELF/BIN/RAW directly into a bare emulator.
            string? filePath = GetArg(args, "--file");
            if (filePath != null)
                return await RunFileMode(filePath, args, port);

            // Bare mode — no BIOS, no file. Just open InternalPineSocket and
            // wait for Bridge to drive the emulator. Used by the file-mode
            // flow: Bridge writes the binary into RAM and sets PC via PINE.
            if (HasFlag(args, "--bare"))
                return await RunBareMode(port);

            // Otherwise BIOS+disc mode (BIOS may be defaulted, disc is optional).
            return await RunEmulator(args, port);
        }

        // ── Bare mode (idle, awaiting PINE commands) ──────────────────────────────

        /// <summary>
        /// Spin up a paused, BIOS-less, file-less emulator that just hosts
        /// <see cref="InternalPineSocket"/>. Bridge drives state transitions
        /// (write memory, set PC, resume) over PINE — used by File→Open of
        /// raw/ELF/BIN binaries which need stepping but no boot sequence.
        /// </summary>
        private static async Task<int> RunBareMode(int port)
        {
            var scheduler = new DualCpuScheduler();
            scheduler.Paused = true;

            using var pine = new InternalPineSocket(scheduler, port);
            pine.Start();
            Console.WriteLine($"InternalPineSocket listening on port {port}. Bare mode — emulator paused; awaiting commands.");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("\nStopping bare-mode emulator...");
            };

            const int SliceSize = 256;
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    if (scheduler.Paused)
                    {
                        await scheduler.WaitForResumeAsync(cts.Token).ConfigureAwait(false);
                        continue;
                    }

                    try { scheduler.RunSlice(SliceSize); }
                    catch (Common.Cpu.Instructions.EE.BreakpointException)
                    {
                        uint bpPc = (uint)(scheduler.EE.Cpu.PC & 0xFFFFFFFFUL);
                        Console.WriteLine($"[BREAK] pc=0x{bpPc:x8}");
                    }

                    scheduler.TickHardware((uint)SliceSize);
                }
            }
            catch (OperationCanceledException) { }

            return 0;
        }

        // ── BIOS+disc mode ────────────────────────────────────────────────────────

        /// <summary>
        /// Boot the built-in emulator with a BIOS image and (optionally) a disc image.
        /// Mirrors what <c>Bridge --run</c> used to do.
        /// </summary>
        private static async Task<int> RunEmulator(string[] args, int port)
        {
            string? biosPath = GetArg(args, "--bios");
            string? discPath = GetArg(args, "--disc");
            string? stepsStr = GetArg(args, "--steps");
            biosPath ??= DefaultBiosPath();

            if (!File.Exists(biosPath))
            {
                Console.Error.WriteLine($"Error: BIOS file not found: {biosPath}");
                return 1;
            }

            if (discPath != null && discPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                discPath = ExtractIsoFromZip(discPath);
                if (discPath == null)
                    return 1;
            }

            if (discPath != null && !File.Exists(discPath))
            {
                Console.Error.WriteLine($"Error: Disc image not found: {discPath}");
                return 1;
            }

            long maxSteps = stepsStr != null && long.TryParse(stepsStr, out long parsed)
                ? parsed
                : long.MaxValue;

            Console.WriteLine($"Loading BIOS: {biosPath}");
            byte[] biosData;
            try
            {
                biosData = File.ReadAllBytes(biosPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error reading BIOS file: {ex.Message}");
                return 1;
            }

            var scheduler = new DualCpuScheduler();
            try
            {
                var biosInfo = BiosLoader.Load(biosData, scheduler);
                Console.WriteLine($"BIOS loaded: {biosInfo}");
            }
            catch (BiosLoadException ex)
            {
                Console.Error.WriteLine($"Error loading BIOS: {ex.Message}");
                return 1;
            }

            FileStream? isoStream = null;
            if (discPath != null)
            {
                Console.WriteLine($"Loading disc: {discPath}");
                try
                {
                    isoStream = new FileStream(discPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    string bootPath = GameLoader.InsertDisc(isoStream, scheduler);
                    if (bootPath.Length > 0)
                        Console.WriteLine($"Disc inserted. BIOS will boot: {bootPath}");
                    else
                        Console.WriteLine("Disc inserted. BIOS will boot the game.");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is GameLoadException)
                {
                    isoStream?.Dispose();
                    Console.Error.WriteLine($"Error loading game disc: {ex.Message}");
                    return 1;
                }
            }
            else
            {
                Console.WriteLine("No disc image provided - running BIOS only.");
            }

            var debugSession = new BuiltInEmulatorDebugSession(scheduler);
            debugSession.Attach();

            using var pine = new InternalPineSocket(scheduler, port);
            pine.Start();
            Console.WriteLine($"InternalPineSocket listening on port {port}.");
            Console.WriteLine("Emulator starting at reset vector - paused, waiting for client.");
            scheduler.Paused = true;

            if (maxSteps != long.MaxValue)
                Console.WriteLine($"Step limit: {maxSteps:N0} EE steps.");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("\nStopping emulator...");
            };

            const int SliceSize = 256;
            long totalSteps = 0;
            var startTime = DateTime.UtcNow;

            try
            {
                while (!cts.Token.IsCancellationRequested && totalSteps < maxSteps)
                {
                    if (pine.PendingRestart)
                    {
                        pine.ClearPendingRestart();
                        Console.WriteLine("[Restart] Reinitialising emulator...");

                        var savedBreakpoints = new HashSet<uint>(scheduler.Breakpoints);
                        var newScheduler = new DualCpuScheduler();
                        BiosLoader.Load(biosData, newScheduler);

                        if (isoStream != null)
                        {
                            isoStream.Seek(0, SeekOrigin.Begin);
                            string bootPath = GameLoader.InsertDisc(isoStream, newScheduler);
                            if (bootPath.Length > 0)
                                Console.WriteLine($"[Restart] Disc inserted. BIOS will boot: {bootPath}");
                        }

                        foreach (var bp in savedBreakpoints)
                            newScheduler.Breakpoints.Add(bp);

                        scheduler = newScheduler;
                        debugSession.OnSchedulerReplaced(newScheduler);
                        pine.SwapScheduler(newScheduler);
                        scheduler.Paused = true;

                        totalSteps = 0;
                        startTime = DateTime.UtcNow;
                        debugSession.ResetState();

                        Console.WriteLine("[Restart] Emulator reinitialised - paused at reset vector.");
                        continue;
                    }

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
                    catch (Common.Cpu.Instructions.EE.BreakpointException)
                    {
                        uint bpPc = (uint)(scheduler.EE.Cpu.PC & 0xFFFFFFFFUL);
                        Console.WriteLine($"[BREAK -> COP0 vector 0x{bpPc:x8}, continuing]");
                    }

                    scheduler.TickHardware((uint)slice);
                    totalSteps += slice;
                    debugSession.TryReport(totalSteps, startTime);
                }
            }
            catch (OperationCanceledException)
            {
                // Ctrl+C
            }

            double totalElapsed = (DateTime.UtcNow - startTime).TotalSeconds;
            Console.WriteLine($"Emulator stopped after {totalSteps:N0} EE steps ({totalElapsed:F2}s).");
            debugSession.PrintFinalPcSummary();

            isoStream?.Dispose();
            return 0;
        }

        // ── File mode (ELF / BIN) ─────────────────────────────────────────────────

        /// <summary>
        /// Load a PS2 ELF32, BIN or RAW binary into a bare emulator (no BIOS, no disc),
        /// pause at the entry point and run the InternalPineSocket. This is the file-mode
        /// behaviour that used to live in Bridge's STARTFILEMODE handler.
        /// </summary>
        private static async Task<int> RunFileMode(string filePath, string[] args, int port)
        {
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"Error: File not found: {filePath}");
                return 1;
            }

            byte[] fileData;
            try { fileData = File.ReadAllBytes(filePath); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error reading file: {ex.Message}");
                return 1;
            }

            var scheduler = new DualCpuScheduler();
            uint entryPoint;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".elf")
            {
                try
                {
                    entryPoint = ElfLoader.LoadIntoEmulator(fileData, scheduler.EE);
                }
                catch (ElfLoadException ex)
                {
                    Console.Error.WriteLine($"ELF load error: {ex.Message}");
                    return 1;
                }
            }
            else
            {
                uint loadAddress = 0u;
                if (GetArg(args, "--load-address") is string addrStr)
                {
                    string trimmed = addrStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? addrStr[2..] : addrStr;
                    if (uint.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out uint p))
                        loadAddress = p;
                }
                var bus = scheduler.EE.Bus;
                for (int i = 0; i < fileData.Length; i++)
                    bus.WriteByte(loadAddress + (uint)i, fileData[i]);
                entryPoint = loadAddress;
                scheduler.EE.Cpu.PC = entryPoint;
            }

            scheduler.Paused = true;
            Console.WriteLine($"File loaded: {filePath}");
            Console.WriteLine($"Entry point: 0x{entryPoint:X8}");

            using var pine = new InternalPineSocket(scheduler, port);
            pine.Start();
            Console.WriteLine($"InternalPineSocket listening on port {port}. Emulator paused; awaiting commands.");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("\nStopping file-mode emulator...");
            };

            const int SliceSize = 256;
            long totalSteps = 0;

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    if (scheduler.Paused)
                    {
                        await scheduler.WaitForResumeAsync(cts.Token).ConfigureAwait(false);
                        continue;
                    }

                    try { scheduler.RunSlice(SliceSize); }
                    catch (Common.Cpu.Instructions.EE.BreakpointException)
                    {
                        uint bpPc = (uint)(scheduler.EE.Cpu.PC & 0xFFFFFFFFUL);
                        Console.WriteLine($"[BREAK] pc=0x{bpPc:x8}");
                    }

                    scheduler.TickHardware((uint)SliceSize);
                    totalSteps += SliceSize;
                }
            }
            catch (OperationCanceledException) { }

            Console.WriteLine($"File mode stopped after {totalSteps:N0} EE steps.");
            return 0;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Extract the first .iso/.bin from a ZIP archive into the temp folder
        /// and return its path. Caches the extraction by size + timestamp.
        /// </summary>
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

                string safeFileName = Path.GetFileName(entry.Name);
                if (string.IsNullOrEmpty(safeFileName))
                {
                    Console.Error.WriteLine("Error: ZIP entry has no valid filename.");
                    return null;
                }

                string tempPath = Path.Combine(Path.GetTempPath(), safeFileName);
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

        private static string DefaultBiosPath()
        {
            string baseDir = AppContext.BaseDirectory;
            string repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            return Path.Combine(repoRoot,
                "CodeDesigner.Ps2.Common", "Bios", "SCPH-70012_BIOS_V12_USA_200.bin");
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

        private static void PrintHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  Emulator.exe [--bios <bios.bin>] [--disc <game.iso|zip>] [--steps N] [--port 28012]");
            Console.WriteLine("  Emulator.exe --file <game.elf|bin> [--load-address 0xN] [--port 28012]");
            Console.WriteLine("  Emulator.exe --bare [--port 28012]   ← idle: Bridge drives via PINE (file-mode loader)");
            Console.WriteLine();
            Console.WriteLine("The emulator hosts an InternalPineSocket on --port (default 28012)");
            Console.WriteLine("which Bridge connects to in built-in mode (analogous to PCSX2 PINE on 28011).");
        }
    }
}
