using System.Diagnostics;
using System.Text.Json;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.Compression;
using CodeDesigner.Ps2.EmulatorCore;
using CodeDesigner.Ps2.Recompiler;

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

            if (HasFlag(args, "--run") || HasFlag(args, "--file"))
            {
                // The built-in emulator now lives in CodeDesigner.Ps2.Emulator.
                // Forward these flags to that executable.
                Environment.ExitCode = LaunchInternalEmulator(args);
                return;
            }

            if (HasFlag(args, "--pcsx2"))
            {
                await StartPcsx2BridgeServer(args);
                return;
            }

            await StartSocketServer();
        }

        /// <summary>
        /// Spawn <c>CodeDesigner.Ps2.Emulator.exe</c> with the supplied args and
        /// wait for it to exit. Bridge no longer hosts the built-in emulator
        /// in-process — that lives in its own process now (analogous to PCSX2).
        /// </summary>
        private static int LaunchInternalEmulator(string[] args)
        {
            string baseDir = AppContext.BaseDirectory;
            string exe = Path.Combine(baseDir,
                OperatingSystem.IsWindows()
                    ? "CodeDesigner.Ps2.Emulator.exe"
                    : "CodeDesigner.Ps2.Emulator");
            if (!File.Exists(exe))
            {
                Console.Error.WriteLine($"Internal emulator binary not found at: {exe}");
                Console.Error.WriteLine("Build CodeDesigner.Ps2.Emulator and re-run, or invoke it directly.");
                return 1;
            }

            var psi = new ProcessStartInfo
            {
                FileName  = exe,
                UseShellExecute = false,
            };
            // Forward all our args except the dispatch flag itself; Emulator
            // expects to see --bios / --disc / --file / --steps / --port directly.
            foreach (var a in args)
                if (!a.Equals("--run", StringComparison.OrdinalIgnoreCase))
                    psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc == null) return 1;
            proc.WaitForExit();
            return proc.ExitCode;
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
                if (ActiveEmulatorProcess != null && !ActiveEmulatorProcess.HasExited)
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

        // Process handle for the spawned CodeDesigner.Ps2.Emulator.exe (built-in
        // emulator host). Replaces the in-process ActiveEmulatorTask of the previous
        // architecture — Bridge no longer hosts the emulator itself, it spawns it.
        private static Process? ActiveEmulatorProcess;

        private static string HandleStartEmulatorMode(SocketServer server, CommandMessage command)
        {
            lock (RuntimeModeLock)
            {
                if (ActiveEmulatorProcess != null && !ActiveEmulatorProcess.HasExited)
                {
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = true,
                        Command = "STARTEMULATOR",
                        Message = "Built-in emulator is already running"
                    });
                }

                var runArgs = new List<string>();
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
                int port = InternalEmulatorClient.DefaultPort;
                runArgs.Add("--port");
                runArgs.Add(port.ToString());

                if (server.PineClient != null)
                {
                    server.PineClient.Dispose();
                    server.PineClient = null;
                }
                server.InternalClient?.Dispose();
                server.InternalClient = null;
                server.Scheduler = null;

                try
                {
                    ActiveEmulatorProcess = SpawnEmulatorProcess(runArgs.ToArray());
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = false,
                        Command = "STARTEMULATOR",
                        Error   = $"Cannot spawn Emulator.exe: {ex.Message}"
                    });
                }

                if (ActiveEmulatorProcess == null)
                {
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = false,
                        Command = "STARTEMULATOR",
                        Error   = "Emulator.exe failed to start"
                    });
                }

                // Give the child a moment to bind its InternalPineSocket before
                // we attempt to connect.  Five attempts of 200 ms keeps total
                // wait under one second while comfortably covering JIT startup.
                InternalEmulatorClient? client = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    Thread.Sleep(200);
                    try
                    {
                        client = new InternalEmulatorClient("127.0.0.1", port);
                        _ = client.GetStatus();   // probes connection
                        break;
                    }
                    catch
                    {
                        client?.Dispose();
                        client = null;
                    }
                }

                if (client == null)
                {
                    try { ActiveEmulatorProcess.Kill(); } catch { /* ignore */ }
                    ActiveEmulatorProcess = null;
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = false,
                        Command = "STARTEMULATOR",
                        Error   = "Could not connect to Emulator.exe InternalPineSocket"
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
        /// Launch <c>CodeDesigner.Ps2.Emulator.exe</c> as a background child process.
        /// Returns the Process handle; the caller is responsible for kill/dispose.
        /// </summary>
        private static Process? SpawnEmulatorProcess(string[] args)
        {
            string baseDir = AppContext.BaseDirectory;
            string exe = Path.Combine(baseDir,
                OperatingSystem.IsWindows()
                    ? "CodeDesigner.Ps2.Emulator.exe"
                    : "CodeDesigner.Ps2.Emulator");
            if (!File.Exists(exe))
                throw new FileNotFoundException($"Internal emulator binary not found at: {exe}");

            var psi = new ProcessStartInfo
            {
                FileName        = exe,
                UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            return Process.Start(psi);
        }

        /// <summary>
        /// STARTFILEMODE runtime command — spawn <c>CodeDesigner.Ps2.Emulator.exe</c>
        /// in <c>--file</c> mode and connect to it via <see cref="InternalEmulatorClient"/>.
        /// The actual ELF/BIN load now happens in the child process; Bridge simply
        /// forwards the request and wires up the debug client.
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

            lock (RuntimeModeLock)
            {
                if (ActiveEmulatorProcess != null && !ActiveEmulatorProcess.HasExited)
                {
                    try { ActiveEmulatorProcess.Kill(); } catch { /* ignore */ }
                    ActiveEmulatorProcess = null;
                }

                if (server.PineClient != null)
                {
                    server.PineClient.Dispose();
                    server.PineClient = null;
                }
                server.InternalClient?.Dispose();
                server.InternalClient = null;
                server.Scheduler = null;

                int port = InternalEmulatorClient.DefaultPort;
                var runArgs = new List<string> { "--file", filePath, "--port", port.ToString() };
                if (command.Address.HasValue)
                {
                    runArgs.Add("--load-address");
                    runArgs.Add($"0x{command.Address.Value:X8}");
                }

                try
                {
                    ActiveEmulatorProcess = SpawnEmulatorProcess(runArgs.ToArray());
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = false, Command = "STARTFILEMODE",
                        Error   = $"Cannot spawn Emulator.exe: {ex.Message}"
                    });
                }

                InternalEmulatorClient? client = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    Thread.Sleep(200);
                    try
                    {
                        client = new InternalEmulatorClient("127.0.0.1", port);
                        _ = client.GetStatus();
                        break;
                    }
                    catch
                    {
                        client?.Dispose();
                        client = null;
                    }
                }

                if (client == null)
                {
                    try { ActiveEmulatorProcess?.Kill(); } catch { /* ignore */ }
                    ActiveEmulatorProcess = null;
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = false, Command = "STARTFILEMODE",
                        Error   = "Could not connect to Emulator.exe InternalPineSocket"
                    });
                }

                server.InternalClient = client;

                Console.WriteLine($"[File Mode] Spawned Emulator.exe with: {filePath}");

                uint entryPoint;
                try { entryPoint = client.GetProgramCounter(); }
                catch { entryPoint = command.Address ?? 0u; }

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
            Console.WriteLine("  dotnet run -- --pcsx2 [--pcsx2-slot 28011] [--pcsx2-host 127.0.0.1] [--port 8765]");
            Console.WriteLine("  dotnet run -- --recompile --elf <game.elf> [--out recomp_out] [options]");
            Console.WriteLine("  dotnet run -- --server");
            Console.WriteLine("  dotnet run -- --graphics-info");
            Console.WriteLine("  dotnet run -- --help");
            Console.WriteLine();
            Console.WriteLine("The built-in emulator now lives in CodeDesigner.Ps2.Emulator.exe.");
            Console.WriteLine("  Emulator.exe --bios <bios.bin> [--disc <game.iso|zip>] [--steps N] [--port 28012]");
            Console.WriteLine("  Emulator.exe --file <game.elf|bin> [--load-address 0xN] [--port 28012]");
            Console.WriteLine("Bridge --run / --file forward to that binary; --pcsx2 connects to PCSX2 PINE.");
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
