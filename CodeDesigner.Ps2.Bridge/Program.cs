using System.Text.Json;
using CodeDesigner.Ps2.Bridge.Services;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.Compression;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.DiscordPresence;
using CodeDesigner.Ps2.Recompiler;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Bridge
{
    class Program
    {
        private static readonly object RuntimeModeLock = new object();

        static async Task Main(string[] args)
        {
            Console.WriteLine(@"  ___         _     ___         _                   
 / __|___  __| |___|   \ ___ __(_)__ _ _ _  ___ _ _ 
| (__/ _ \/ _` / -_) |) / -_|_-< / _` | ' \/ -_) '_|
 \___\___/\__,_\___|___/\___/__/_\__, |_||_\___|_|  
                                 |___/              

");



            //using var apache = ApacheZdb.Open("C:\\Users\\tholu\\Desktop\\APACHE00.ZDB");
            //apache.DecryptFTScorePatch();
            //apache.DecryptZSealEtcPatch();

            //var zdb = ZdbArchive.Open("C:\\Users\\tholu\\Desktop\\M51.ZDB");
            //zdb.ExtractAll("C:\\Users\\tholu\\Desktop");

            
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

            if (HasFlag(args, "--pcsx2"))
            {
                await StartPcsx2BridgeServer(args);
                return;
            }

            await StartSocketServer();
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
                string host = string.IsNullOrWhiteSpace(command.Pcsx2Host)
                    ? "127.0.0.1"
                    : command.Pcsx2Host;
                int slot = command.Pcsx2Slot ?? PineClient.DefaultSlot;

                try
                {
                    var pine = new PineClient(host, slot);
                    var status = pine.GetStatus();

                    server.InternalClient?.Dispose();
                    server.InternalClient = null;

                    if (server.PineClient != null) server.PineClient.Dispose();
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

        // Default + override port for the InternalPineSocket hosted by
        // CodeDesigner.Ps2.Emulator.exe. The frontend spawns Emulator.exe
        // itself; Bridge only connects.
        private const string InternalEmulatorHostDefault = "127.0.0.1";

        /// <summary>
        /// STARTEMULATOR — connect to a CodeDesigner.Ps2.Emulator.exe instance
        /// that the front-end has already spawned. Bridge is purely a client of
        /// the InternalPineSocket; process lifecycle belongs to the front end
        /// (mirroring how the user launches PCSX2 themselves before STARTPCSX2).
        /// </summary>
        private static string HandleStartEmulatorMode(SocketServer server, CommandMessage command)
        {
            lock (RuntimeModeLock)
            {
                string host = string.IsNullOrWhiteSpace(command.InternalEmulatorHost)
                    ? InternalEmulatorHostDefault
                    : command.InternalEmulatorHost!;
                int port = command.InternalEmulatorPort ?? InternalEmulatorClient.DefaultPort;

                if (server.PineClient != null)
                {
                    server.PineClient.Dispose();
                    server.PineClient = null;
                }
                server.InternalClient?.Dispose();
                server.InternalClient = null;

                // Retry briefly so a near-simultaneous Emulator.exe startup
                // has time to bind its listener before we give up.
                InternalEmulatorClient? client = null;
                Exception? lastEx = null;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        client = new InternalEmulatorClient(host, port);
                        _ = client.GetStatus();   // probe
                        lastEx = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        client?.Dispose();
                        client = null;
                        lastEx = ex;
                        Thread.Sleep(200);
                    }
                }

                if (client == null)
                {
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = false,
                        Command = "STARTEMULATOR",
                        Error   = $"Could not connect to InternalPineSocket at {host}:{port} — " +
                                  $"is CodeDesigner.Ps2.Emulator.exe running? ({lastEx?.Message})"
                    });
                }

                server.InternalClient = client;
            }

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success = true,
                Command = "STARTEMULATOR",
                Message = "Built-in emulator mode started"
            });
        }

        /// <summary>
        /// STARTFILEMODE — load a PS2 ELF, BIN, or RAW binary into a *running*
        /// emulator's RAM via PINE writes, set PC to the entry point, and leave
        /// it paused. Reuses any existing InternalEmulatorClient (the bare
        /// emulator the front end just spawned, or a BIOS-mode emulator from a
        /// previous Run); never spawns or tears down processes itself.
        /// <para>
        /// This restores the pre-decoupling design: file mode never re-runs
        /// the BIOS/kernel boot sequence, it just drops the binary into RAM
        /// and points PC at it.
        /// </para>
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
                string host = string.IsNullOrWhiteSpace(command.InternalEmulatorHost)
                    ? InternalEmulatorHostDefault
                    : command.InternalEmulatorHost!;
                int port = command.InternalEmulatorPort ?? InternalEmulatorClient.DefaultPort;

                // PCSX2 PINE makes no sense in file mode — drop it.
                if (server.PineClient != null)
                {
                    server.PineClient.Dispose();
                    server.PineClient = null;
                }

                // Reuse the existing InternalClient when available so we don't
                // tear down a perfectly good connection. But the emulator
                // process behind it may have exited since we last talked to
                // it (user closed the terminal, --bare hit AddressInUse and
                // bailed, etc.). Probe before trusting; on probe failure the
                // pause/clear/write calls below would otherwise fail with a
                // confusing "Unable to connect to the remote server" because
                // SendReceive's auto-reconnect can't reach a dead process.
                InternalEmulatorClient? client = server.InternalClient;
                if (client != null)
                {
                    try { _ = client.GetStatus(); }
                    catch (Exception probeEx)
                    {
                        Console.WriteLine($"[File Mode] Existing InternalClient is dead ({probeEx.GetType().Name}: {probeEx.Message}); reconnecting.");
                        try { client.Dispose(); } catch { /* ignore */ }
                        server.InternalClient = null;
                        client = null;
                    }
                }

                if (client == null)
                {
                    Exception? lastEx = null;
                    for (int attempt = 0; attempt < 25; attempt++)
                    {
                        try
                        {
                            client = new InternalEmulatorClient(host, port);
                            _ = client.GetStatus();
                            lastEx = null;
                            break;
                        }
                        catch (Exception ex)
                        {
                            client?.Dispose();
                            client = null;
                            lastEx = ex;
                            Thread.Sleep(200);
                        }
                    }

                    if (client == null)
                    {
                        return JsonSerializer.Serialize(new CommandResponse
                        {
                            Success = false, Command = "STARTFILEMODE",
                            Error   = $"Could not connect to InternalPineSocket at {host}:{port} — " +
                                      $"is CodeDesigner.Ps2.Emulator.exe running (try --bare)? ({lastEx?.Message})"
                        });
                    }

                    server.InternalClient = client;
                }

                // ── Now load the file content into the running emulator. ──────
                try
                {
                    // Pause first so we're not racing the run-loop while writes
                    // land in RAM. Ignore failures — emulator may already be
                    // paused (bare mode starts paused).
                    try { client.Pause(); } catch { /* ignore */ }

                    // Clear any breakpoints inherited from a previous session;
                    // the binary's address space is fresh, old BPs are stale.
                    try { client.ClearAllBreakpoints(); } catch { /* ignore */ }

                    uint entryPoint;
                    string ext = Path.GetExtension(filePath).ToLowerInvariant();
                    if (ext == ".elf")
                    {
                        // Parse ELF locally and push each loadable segment via
                        // the bulk-write opcode (avoids shipping the entire
                        // 32 MB RAM image when only a few hundred KB are real).
                        entryPoint = ElfPineLoader.LoadElfViaClient(fileData, client);
                    }
                    else
                    {
                        uint loadAddress = command.Address ?? 0u;
                        client.WriteBytes(loadAddress, fileData);
                        entryPoint = loadAddress;
                    }

                    client.SetProgramCounter(entryPoint);

                    Console.WriteLine($"[File Mode] Loaded: {filePath}  entry=0x{entryPoint:X8}  size={fileData.Length:N0} bytes");

                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success  = true,
                        Command  = "STARTFILEMODE",
                        PC       = entryPoint,
                        Paused   = true,
                        Message  = $"File mode started; entry point 0x{entryPoint:X8}. Emulator is paused.",
                    });
                }
                catch (Exception ex)
                {
                    // Tee to the Bridge console: the JSON Error field is the
                    // only thing the front end sees and VS Code's output panel
                    // truncates long error strings. Surfacing the full type +
                    // stack here makes debugging tractable.
                    Console.WriteLine($"[File Mode] Load failed: {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);

                    // If the underlying socket died mid-load, drop the stale
                    // client so the next STARTFILEMODE attempt reconnects
                    // instead of immediately failing the same way.
                    if (ex is System.Net.Sockets.SocketException || ex is IOException)
                    {
                        try { client.Dispose(); } catch { /* ignore */ }
                        server.InternalClient = null;
                    }

                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = false, Command = "STARTFILEMODE",
                        Error   = $"File mode load failed ({ex.GetType().Name}): {ex.Message}"
                    });
                }
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

            StartDiscordPresence(cts.Token);

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

        /// <summary>
        /// Default Discord application (client) ID used for rich presence when the
        /// <c>DiscordPresence.ApplicationId</c> setting has not been overridden.
        /// </summary>
        private const string DefaultDiscordApplicationId = "1490548720293642381";

        /// <summary>
        /// Starts the Discord rich-presence worker. Uses the application ID from the
        /// <see cref="DiscordPresenceService.ApplicationIdKey"/> setting when present,
        /// otherwise falls back to <see cref="DefaultDiscordApplicationId"/>. The worker
        /// polls the database for the active game-research context and publishes it to
        /// Discord.
        /// </summary>
        private static void StartDiscordPresence(CancellationToken cancellationToken)
        {
            DbContextOptions<CodeDesignerDbContext> dbOptions =
                DatabaseInitialiser.BuildDbOptions(DatabasePaths.GetDefaultConnectionString());

            string? configuredId = null;
            try
            {
                using var db = new CodeDesignerDbContext(dbOptions);
                configuredId = db.Settings
                    .Where(s => s.Key == DiscordPresenceService.ApplicationIdKey)
                    .Select(s => s.Value)
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discord] Could not read the application ID setting ({ex.Message}); using the built-in default.");
            }

            string applicationId = string.IsNullOrWhiteSpace(configuredId)
                ? DefaultDiscordApplicationId
                : configuredId.Trim();

            Console.WriteLine($"[Discord] Rich presence enabled (application ID {applicationId}).");

            _ = Task.Run(async () =>
            {
                var dbContextFactory = new BridgeDbContextFactory(dbOptions);
                var provider = new DatabaseDiscordPresenceDataProvider(dbContextFactory);
                using var presenceClient = new DiscordPresenceClient(applicationId);
                var worker = new DiscordPresenceWorker(provider, presenceClient);
                try
                {
                    await worker.RunAsync(TimeSpan.FromSeconds(15), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Bridge is shutting down.
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Discord] Rich presence stopped: {ex.Message}");
                }
            }, cancellationToken);
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run -- --pcsx2 [--pcsx2-slot 28011] [--pcsx2-host 127.0.0.1] [--port 8765]");
            Console.WriteLine("  dotnet run -- --recompile --elf <game.elf> [--out recomp_out] [options]");
            Console.WriteLine("  dotnet run -- --graphics-info");
            Console.WriteLine("  dotnet run -- --help");
            Console.WriteLine();
            Console.WriteLine("Bridge is the control router. Run the runtime processes yourself:");
            Console.WriteLine("  CodeDesigner.Ps2.Emulator.exe --bios <bios.bin> [--disc <game.iso|zip>] [--port 28012]");
            Console.WriteLine("  CodeDesigner.Ps2.Emulator.exe --file <game.elf|bin> [--load-address 0xN] [--port 28012]");
            Console.WriteLine("  pcsx2.exe -batch <game.iso>   (with PINE enabled, default port 28011)");
            Console.WriteLine();
            Console.WriteLine("Default with no flags: starts control server on port 8765.");
            Console.WriteLine("Use STARTPCSX2 / STARTEMULATOR / STARTFILEMODE socket commands to attach.");
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
