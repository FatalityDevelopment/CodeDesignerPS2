using System.Text.Json;
using CodeDesigner.Ps2.Bridge.Services;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.DiscordBot;
using CodeDesigner.Ps2.DiscordPresence;
using CodeDesigner.Ps2.Recompiler;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Bridge
{
    class Program
    {
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

            if (HasFlag(args, "--recompile"))
            {
                await RunRecompiler(args);
                return;
            }

            await StartSocketServer();
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

        private static async Task StartSocketServer()
        {
            const int FrontendServerPort = 8765;
            Console.WriteLine($"Starting control socket server on port {FrontendServerPort}...");
            var server = new SocketServer(FrontendServerPort);

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
            StartDiscordBot(cts.Token);

            Console.WriteLine("Server is running. Press Ctrl+C to stop.");

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
        /// Starts the Discord rich-presence loop: <see cref="DiscordPresenceWorker"/>
        /// reads the session keys clients write into the database (see
        /// <see cref="DiscordPresenceService"/>) and pushes the rich presence to
        /// Discord, showing an idle status when none is set.
        /// The application ID comes from the <see cref="DiscordPresenceService.ApplicationIdKey"/>
        /// setting when present, otherwise <see cref="DefaultDiscordApplicationId"/>.
        /// </summary>
        private static void StartDiscordPresence(CancellationToken cancellationToken)
        {
            DbContextOptions<CodeDesignerDbContext> dbOptions =
                DatabaseInitialiser.BuildDbOptions(DatabasePaths.GetDefaultConnectionString());
            var dbContextFactory = new BridgeDbContextFactory(dbOptions);

            string? configuredId = null;
            try
            {
                using var db = dbContextFactory.CreateDbContext();
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

            // Worker — reads the DB session keys and pushes rich presence to Discord.
            _ = Task.Run(async () =>
            {
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

        /// <summary>
        /// Starts the Discord bot loop: <see cref="DiscordBotWorker"/> polls the
        /// AiActivityLogs table (written by <see cref="Services.AiActivityRecorder"/>)
        /// and posts each new AI activity entry to a Discord channel. It idles until
        /// a bot token + channel ID or a webhook URL is configured — see
        /// <see cref="DatabaseAiActivityProvider"/> for the settings keys.
        /// </summary>
        private static void StartDiscordBot(CancellationToken cancellationToken)
        {
            var dbContextFactory = new BridgeDbContextFactory(
                DatabaseInitialiser.BuildDbOptions(DatabasePaths.GetDefaultConnectionString()));

            _ = Task.Run(async () =>
            {
                using var discord = new DiscordRestClient();
                var worker = new DiscordBotWorker(new DatabaseAiActivityProvider(dbContextFactory), discord);
                try
                {
                    await worker.RunAsync(TimeSpan.FromSeconds(3), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Bridge is shutting down.
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DiscordBot] Stopped: {ex.Message}");
                }
            }, cancellationToken);
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run -- --recompile --elf <game.elf> [--out recomp_out] [options]");
            Console.WriteLine("  dotnet run -- --help");
            Console.WriteLine();
            Console.WriteLine("With no flags: the workspace service (labels, cheats, project data, file formats)");
            Console.WriteLine("on port 8765. The emulator is Ps2Emulator; clients connect to its debug server.");
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
