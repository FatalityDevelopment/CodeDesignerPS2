using System.IO.Compression;
using System.Text.Json;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.Emulator;
using CodeDesigner.Ps2.Recompiler;

namespace CodeDesigner.Ps2.Bridge
{
    class Program
    {
        private static readonly object RuntimeModeLock = new object();
        private static Task? ActiveEmulatorTask;

        static async Task Main(string[] args)
        {
            Console.WriteLine(@"  ___         _     ___         _                   
 / __|___  __| |___|   \ ___ __(_)__ _ _ _  ___ _ _ 
| (__/ _ \/ _` / -_) |) / -_|_-< / _` | ' \/ -_) '_|
 \___\___/\__,_\___|___/\___/__/_\__, |_||_\___|_|  
                                 |___/              

");
            Console.WriteLine("Version 1.00 - PlayStation 2 Toolkit");
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

            if (HasFlag(args, "--recompile"))
            {
                await RunRecompiler(args);
                return;
            }

            if (HasFlag(args, "--run"))
            {
                await RunEmulator(args);
                return;
            }

            if (HasFlag(args, "--file"))
            {
                await RunFileMode(args);
                return;
            }

            if (HasFlag(args, "--pcsx2"))
            {
                await StartPcsx2BridgeServer(args);
                return;
            }

            await StartSocketServer();
        }

        private static async Task RunEmulator(string[] args, SocketServer? attachedServer = null)
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

            if (discPath != null && discPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
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
                return;
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
                return;
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
                    return;
                }
            }
            else
            {
                Console.WriteLine("No disc image provided - running BIOS only.");
            }

            var debugSession = new BuiltInEmulatorDebugSession(scheduler);
            debugSession.Attach();

            bool ownsEmbeddedServer = attachedServer == null;
            var embeddedServer = attachedServer ?? new SocketServer(8765);
            Task serverTask = Task.CompletedTask;

            if (embeddedServer.PineClient != null)
            {
                embeddedServer.PineClient.Dispose();
                embeddedServer.PineClient = null;
            }

            embeddedServer.Scheduler = scheduler;

            if (ownsEmbeddedServer)
            {
                embeddedServer.ClientConnected += (_, ep) => Console.WriteLine($"[Server] Client connected: {ep}");
                embeddedServer.ClientDisconnected += (_, _) => Console.WriteLine("[Server] Client disconnected");
                serverTask = Task.Run(() => embeddedServer.StartAsync());
            }

            Console.WriteLine("Emulator starting at reset vector - paused, waiting for client.");
            scheduler.Paused = true;

            Console.WriteLine();
            Console.WriteLine("Emulator ready. Waiting for client to resume...");
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

            const int SliceSize = 256;
            long totalSteps = 0;
            var startTime = DateTime.UtcNow;

            try
            {
                while (!cts.Token.IsCancellationRequested && totalSteps < maxSteps)
                {
                    if (embeddedServer.PendingRestart)
                    {
                        embeddedServer.PendingRestart = false;
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
                        embeddedServer.Scheduler = newScheduler;
                        scheduler.Paused = true;
                        embeddedServer.SignalRestartComplete();

                        totalSteps = 0;
                        startTime = DateTime.UtcNow;
                        debugSession.ResetState();

                        Console.WriteLine("[Restart] Emulator reinitialised - paused at reset vector, waiting for client to resume.");
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
            Console.WriteLine();
            Console.WriteLine($"Emulator stopped after {totalSteps:N0} EE steps ({totalElapsed:F2}s).");
            debugSession.PrintFinalPcSummary();

            if (ownsEmbeddedServer)
            {
                embeddedServer.Stop();
                await serverTask.ConfigureAwait(false);
            }
            else
            {
                embeddedServer.Scheduler = null;
            }

            isoStream?.Dispose();
        }

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

        private static async Task StartPcsx2BridgeServer(string[] args)
        {
            string host = GetArg(args, "--pcsx2-host") ?? "127.0.0.1";
            int slot = PineClient.DefaultSlot;
            if (GetArg(args, "--pcsx2-slot") is string slotStr && int.TryParse(slotStr, out int parsed))
                slot = parsed;

            int serverPort = 8765;
            if (GetArg(args, "--port") is string portStr && int.TryParse(portStr, out int pp))
                serverPort = pp;

            Console.WriteLine($"PCSX2 PINE bridge mode - connecting to {host}:{slot}...");
            var pine = new PineClient(host, slot);

            try
            {
                var status = pine.GetStatus();
                Console.WriteLine($"PCSX2 PINE connected - emulator status: {status}");
                if (status != PineClient.EmuStatus.Shutdown)
                {
                    string gameTitle = pine.GetGameTitle();
                    string gameId = pine.GetGameID();
                    if (!string.IsNullOrEmpty(gameId))
                        Console.WriteLine($"Running game: {gameTitle} ({gameId})");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Cannot connect to PCSX2 PINE on {host}:{slot}: {ex.Message}");
                Console.Error.WriteLine("Make sure PCSX2 is running and PINE is enabled under Settings -> Advanced.");
                return;
            }

            Console.WriteLine($"Starting bridge server on port {serverPort}...");
            var server = new SocketServer(serverPort) { PineClient = pine };
            server.ClientConnected += (_, ep) => Console.WriteLine($"[Bridge] VS Code connected: {ep}");
            server.ClientDisconnected += (_, _) => Console.WriteLine("[Bridge] VS Code disconnected");

            var serverTask = server.StartAsync();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("\nShutting down bridge...");
                server.Stop();
                pine.Dispose();
            };

            Console.WriteLine("Bridge is running. Press Ctrl+C to stop.");
            try
            {
                await Task.Delay(-1, cts.Token);
            }
            catch (TaskCanceledException)
            {
                // Ctrl+C
            }

            await serverTask;
        }

        /// <summary>
        /// Mode 2 — file mode: load a PS2 ELF32 executable into a bare emulator (no BIOS,
        /// no disc), start the socket server paused at the ELF entry point, and wait for
        /// the VS Code client to connect and send debugger commands.
        /// </summary>
        private static async Task RunFileMode(string[] args, SocketServer? attachedServer = null)
        {
            string? elfPath = GetArg(args, "--file");

            if (string.IsNullOrWhiteSpace(elfPath))
            {
                Console.Error.WriteLine("Error: --file requires an ELF path, e.g. --file game.elf");
                return;
            }

            if (!File.Exists(elfPath))
            {
                Console.Error.WriteLine($"Error: ELF file not found: {elfPath}");
                return;
            }

            byte[] elfData;
            try { elfData = File.ReadAllBytes(elfPath); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error reading ELF file: {ex.Message}");
                return;
            }

            var scheduler = new DualCpuScheduler();
            uint entryPoint;
            try
            {
                entryPoint = ElfLoader.LoadIntoEmulator(elfData, scheduler.EE);
            }
            catch (ElfLoadException ex)
            {
                Console.Error.WriteLine($"Error loading ELF: {ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading ELF: {ex.Message}");
                return;
            }

            scheduler.Paused = true;
            Console.WriteLine($"ELF loaded: {elfPath}");
            Console.WriteLine($"Entry point: 0x{entryPoint:X8}");
            Console.WriteLine("Emulator is paused in file mode. Connect VS Code to debug.");

            bool ownsEmbeddedServer = attachedServer == null;
            var embeddedServer = attachedServer ?? new SocketServer(8765);
            Task serverTask = Task.CompletedTask;

            if (embeddedServer.PineClient != null)
            {
                embeddedServer.PineClient.Dispose();
                embeddedServer.PineClient = null;
            }

            embeddedServer.Scheduler = scheduler;

            if (ownsEmbeddedServer)
            {
                embeddedServer.ClientConnected += (_, ep) => Console.WriteLine($"[File Mode] Client connected: {ep}");
                embeddedServer.ClientDisconnected += (_, _) => Console.WriteLine("[File Mode] Client disconnected");
                serverTask = Task.Run(() => embeddedServer.StartAsync());
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("\nStopping file mode emulator...");
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

            if (ownsEmbeddedServer)
            {
                embeddedServer.Stop();
                await serverTask.ConfigureAwait(false);
            }
            else
            {
                embeddedServer.Scheduler = null;
            }
        }

        private static Task RunRecompiler(string[] args)
        {
            string? elfPath = GetArg(args, "--elf");
            string outDir = GetArg(args, "--out") ?? "recomp_out";
            bool single = HasFlag(args, "--single-file");
            string? ghidra = GetArg(args, "--ghidra");
            bool patchCop0 = HasFlag(args, "--patch-cop0");

            if (elfPath == null)
            {
                Console.Error.WriteLine("Error: --elf <path> is required for --recompile mode.");
                return Task.CompletedTask;
            }

            if (!File.Exists(elfPath))
            {
                Console.Error.WriteLine($"Error: ELF file not found: {elfPath}");
                return Task.CompletedTask;
            }

            var stubs = new List<string>();
            var skips = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--stub" && i + 1 < args.Length)
                    stubs.Add(args[++i]);
                if (args[i] == "--skip" && i + 1 < args.Length)
                    skips.Add(args[++i]);
            }

            var config = new RecompilerConfig
            {
                InputElf = elfPath,
                OutputDirectory = outDir,
                SingleFileOutput = single,
                GhidraFunctionMap = ghidra,
                PatchCop0 = patchCop0,
                Stubs = stubs,
                Skips = skips,
            };

            Console.WriteLine($"Recompiling: {elfPath}");
            Console.WriteLine($"Output:      {outDir}");

            byte[] elfData;
            try
            {
                elfData = File.ReadAllBytes(elfPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error reading ELF: {ex.Message}");
                return Task.CompletedTask;
            }

            try
            {
                var recompiler = new Ps2StaticRecompiler(config);
                var result = recompiler.Recompile(elfData, msg => Console.WriteLine($"  {msg}"));

                Console.WriteLine();
                Console.WriteLine("Recompilation complete.");
                Console.WriteLine($"  Functions : {result.Functions.Count}");
                Console.WriteLine($"  Files     : {result.WrittenFiles.Count}");
                foreach (var f in result.WrittenFiles)
                    Console.WriteLine($"    {f}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Recompilation failed: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        private static string HandleRuntimeCommand(SocketServer server, CommandMessage command)
        {
            string normalized = command.Command.ToUpperInvariant();
            return normalized switch
            {
                "STARTPCSX2" or "START_PCSX2" or "STARTPCSX2MODE" or "START_PCSX2_MODE"
                    => HandleStartPcsx2Mode(server, command),

                "STARTEMULATOR" or "START_EMULATOR" or "STARTBUILTINEMULATOR" or "START_BUILTIN_EMULATOR"
                or "STARTEMULATORMODE" or "START_EMULATOR_MODE"
                    => HandleStartEmulatorMode(server, command),

                "STARTFILEMODE" or "START_FILE_MODE"
                    => HandleStartFileMode(server, command),

                _ => JsonSerializer.Serialize(new CommandResponse
                {
                    Success = false,
                    Command = command.Command,
                    Error = "Unsupported runtime mode command"
                })
            };
        }

        private static string HandleStartPcsx2Mode(SocketServer server, CommandMessage command)
        {
            lock (RuntimeModeLock)
            {
                if (ActiveEmulatorTask != null && !ActiveEmulatorTask.IsCompleted)
                {
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = false,
                        Command = "STARTPCSX2",
                        Error = "Built-in emulator is already running. Stop it before switching to PCSX2 mode."
                    });
                }

                string host = string.IsNullOrWhiteSpace(command.Pcsx2Host)
                    ? "127.0.0.1"
                    : command.Pcsx2Host;
                int slot = command.Pcsx2Slot ?? PineClient.DefaultSlot;

                try
                {
                    var pine = new PineClient(host, slot);
                    var status = pine.GetStatus();

                    server.Scheduler = null;
                    if (server.PineClient != null)
                        server.PineClient.Dispose();
                    server.PineClient = pine;

                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = true,
                        Command = "STARTPCSX2",
                        Message = $"PCSX2 mode started ({host}:{slot}) - status: {status}"
                    });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = false,
                        Command = "STARTPCSX2",
                        Error = $"Cannot connect to PCSX2 PINE on {host}:{slot}: {ex.Message}"
                    });
                }
            }
        }

        // Maximum seconds to wait for RunEmulator to initialise the scheduler after
        // STARTEMULATOR is received.  BIOS loading takes < 500 ms on typical hardware;
        // 30 s covers pathologically slow I/O without risking a TypeScript client timeout.
        private const int EmulatorStartTimeoutSeconds = 30;

        private static string HandleStartEmulatorMode(SocketServer server, CommandMessage command)
        {
            // Capture the newly-started task reference outside the lock so we can poll
            // it after releasing RuntimeModeLock (avoids holding the lock while sleeping).
            Task startTask;

            lock (RuntimeModeLock)
            {
                if (ActiveEmulatorTask != null && !ActiveEmulatorTask.IsCompleted)
                {
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = true,
                        Command = "STARTEMULATOR",
                        Message = "Built-in emulator is already running"
                    });
                }

                var runArgs = new List<string> { "--run" };
                if (!string.IsNullOrWhiteSpace(command.BiosPath))
                {
                    runArgs.Add("--bios");
                    runArgs.Add(command.BiosPath!);
                }
                if (!string.IsNullOrWhiteSpace(command.DiscPath))
                {
                    runArgs.Add("--disc");
                    runArgs.Add(command.DiscPath!);
                }
                if (command.Steps.HasValue && command.Steps.Value > 0)
                {
                    runArgs.Add("--steps");
                    runArgs.Add(command.Steps.Value.ToString());
                }

                if (server.PineClient != null)
                {
                    server.PineClient.Dispose();
                    server.PineClient = null;
                }

                // Clear any stale scheduler from a previous run so the wait loop below
                // detects when the newly-started RunEmulator task sets the new one.
                server.Scheduler = null;

                ActiveEmulatorTask = Task.Run(async () =>
                {
                    try
                    {
                        await RunEmulator(runArgs.ToArray(), server);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[Runtime] Emulator task crashed: {ex.Message}");
                    }
                });

                startTask = ActiveEmulatorTask;
            } // release RuntimeModeLock before blocking so STARTPCSX2/STARTEMULATOR from
              // other clients aren't held up while we wait for BIOS loading.

            // Wait until RunEmulator has set the scheduler (BIOS loaded, emulator paused
            // at the reset vector) before returning success.  Without this wait, TypeScript
            // immediately sends RESUME which arrives while Scheduler is still null, causing
            // "Emulator not running — no scheduler attached" and leaving the emulator
            // permanently paused.  ProcessCommand is synchronous so Thread.Sleep is used.
            var waitStart = DateTime.UtcNow;
            while (server.Scheduler == null
                   && !startTask.IsCompleted
                   && (DateTime.UtcNow - waitStart).TotalSeconds < EmulatorStartTimeoutSeconds)
            {
                Thread.Sleep(50);
            }

            if (server.Scheduler == null)
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = false,
                    Command = "STARTEMULATOR",
                    Error = "Emulator failed to start — check BIOS path and disc image."
                });

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success = true,
                Command = "STARTEMULATOR",
                Message = "Built-in emulator mode started"
            });
        }

        /// <summary>
        /// STARTFILEMODE runtime command — load a PS2 ELF32, BIN, or RAW binary into
        /// a bare emulator (no BIOS, no disc), leave it paused at the entry point.
        /// Supported file types:
        ///   .elf / .ELF  — PS2 ELF32 executable (entry point from ELF header)
        ///   .bin / .raw  — flat binary loaded at <see cref="CommandMessage.Address"/>
        ///                  (defaults to 0x00000000); PC set to load address
        /// </summary>
        private static string HandleStartFileMode(SocketServer server, CommandMessage command)
        {
            string? filePath = command.InputPath;
            if (string.IsNullOrWhiteSpace(filePath))
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = false, Command = "STARTFILEMODE",
                    Error = "InputPath is required for STARTFILEMODE"
                });

            if (!File.Exists(filePath))
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = false, Command = "STARTFILEMODE",
                    Error = $"File not found: {filePath}"
                });

            byte[] fileData;
            try { fileData = File.ReadAllBytes(filePath); }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = false, Command = "STARTFILEMODE",
                    Error = $"Cannot read file: {ex.Message}"
                });
            }

            lock (RuntimeModeLock)
            {
                // Stop any running emulator task by replacing the scheduler; the
                // run-loop will exit naturally when the task next checks for the
                // cancellation token or detects the scheduler swap.
                if (ActiveEmulatorTask != null && !ActiveEmulatorTask.IsCompleted)
                    ActiveEmulatorTask = null;

                if (server.PineClient != null)
                {
                    server.PineClient.Dispose();
                    server.PineClient = null;
                }

                var scheduler = new DualCpuScheduler();
                uint entryPoint;

                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext == ".elf")
                {
                    try { entryPoint = ElfLoader.LoadIntoEmulator(fileData, scheduler.EE); }
                    catch (ElfLoadException ex)
                    {
                        return JsonSerializer.Serialize(new CommandResponse
                        {
                            Success = false, Command = "STARTFILEMODE",
                            Error = $"ELF load error: {ex.Message}"
                        });
                    }
                }
                else
                {
                    // .bin / .raw — flat binary; load at the requested address (default 0)
                    uint loadAddress = command.Address ?? 0u;
                    var bus = scheduler.EE.Bus;
                    for (int i = 0; i < fileData.Length; i++)
                        bus.WriteByte(loadAddress + (uint)i, fileData[i]);
                    entryPoint = loadAddress;
                    scheduler.EE.Cpu.PC = entryPoint;
                }

                scheduler.Paused = true;
                server.Scheduler = scheduler;

                Console.WriteLine($"[File Mode] Loaded: {filePath}  entry=0x{entryPoint:X8}");

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "STARTFILEMODE",
                    PC       = entryPoint,
                    Paused   = true,
                    Message  = $"File mode started; entry point 0x{entryPoint:X8}. Emulator is paused.",
                });
            }
        }

        private static async Task StartSocketServer()
        {
            const int FrontendServerPort = 8765;
            Console.WriteLine($"Starting control socket server on port {FrontendServerPort}...");
            var server = new SocketServer(FrontendServerPort);
            server.RuntimeCommandHandler = command => HandleRuntimeCommand(server, command);

            //server.MessageReceived += (_, message) =>
            //    Console.WriteLine($"[Server] Received: {message[..Math.Min(100, message.Length)]}...");
            server.ClientConnected += (_, endpoint) =>
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
            Console.WriteLine("Runtime commands:");
            Console.WriteLine("  STARTPCSX2  (optional: pcsx2Host, pcsx2Slot)");
            Console.WriteLine("  STARTEMULATOR (optional: biosPath, discPath, steps)");

            try
            {
                await Task.Delay(-1, cts.Token);
            }
            catch (TaskCanceledException)
            {
                // Expected on Ctrl+C
            }

            await serverTask;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run -- --run [--bios <bios.bin>] [--disc <game.iso>] [--steps N]");
            Console.WriteLine("  dotnet run -- --pcsx2 [--pcsx2-slot 28011] [--pcsx2-host 127.0.0.1] [--port 8765]");
            Console.WriteLine("  dotnet run -- --recompile --elf <game.elf> [--out recomp_out] [options]");
            Console.WriteLine("  dotnet run -- --server");
            Console.WriteLine("  dotnet run -- --graphics-info");
            Console.WriteLine("  dotnet run -- --help");
            Console.WriteLine();
            Console.WriteLine("Default with no flags: starts control server on port 8765.");
            Console.WriteLine("Use STARTPCSX2 / STARTEMULATOR socket commands to select runtime mode.");
        }

        private static void PrintGraphicsInfo()
        {
            Console.WriteLine("=== Adding a Graphics Display Window ===");
            Console.WriteLine("Use Silk.NET.Windowing + Silk.NET.OpenGL and render GS.GetFrameBuffer() at 50/60 FPS.");
            Console.WriteLine("Map pad input to scheduler.Pad0/Pad1 and call scheduler.PollPads() each frame.");
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
    }
}
