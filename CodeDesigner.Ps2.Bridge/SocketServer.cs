using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.Database;
using Microsoft.EntityFrameworkCore;
using CodeDesigner.Ps2.LabelManager;
using CodeDesigner.Ps2.Bridge.Repositories;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Socket server for communication with VSCode extension
    /// </summary>
    public partial class SocketServer : IDisposable, Services.IBridgeContext
    {
        // ── Service dispatcher ───────────────────────────────────────────────
        // Per-domain command handlers register with this registry at startup;
        // ProcessCommand asks the dispatcher first and only falls back to the
        // legacy switch for handlers that have not been migrated yet.
        private readonly Services.CommandDispatcher _dispatcher;

        // ── IBridgeContext explicit implementations ──────────────────────────
        // SocketServer's existing fields stay as `internal`; we expose them
        // through the IBridgeContext surface using explicit impls so services
        // get a clean read/write view without forcing public visibility.
        Dictionary<uint, (string Label, string? Comment)> Services.IBridgeContext.Labels => _labels;
        Dictionary<string, StructEntry>                   Services.IBridgeContext.Structs => _structs;
        CodeDesigner.Ps2.Database.CodeDesignerDbContext   Services.IBridgeContext.Db                  => Db;
        Repositories.DbLabelRepository                    Services.IBridgeContext.LabelRepo           => LabelRepo;
        Repositories.DbSettingsRepository                 Services.IBridgeContext.SettingsRepo        => SettingsRepo;
        Repositories.DbMcpMemoryRepository?               Services.IBridgeContext.McpMemoryRepo       => McpMemoryRepo;
        Repositories.DbMcpAgentRepository?                Services.IBridgeContext.McpAgentRepo        => McpAgentRepo;
        Repositories.DbMcpOrchestrationRepository?        Services.IBridgeContext.McpOrchestrationRepo => McpOrchestrationRepo;
        Repositories.DbDataFormatRepository               Services.IBridgeContext.DataFormatRepo      => DataFormatRepo;
        CodeDesigner.Ps2.LabelManager.Ps2LabelManager     Services.IBridgeContext.LabelMgr            => LabelMgr;
        void Services.IBridgeContext.RecordNavEntry(uint address, string source) => RecordNavEntry(address, source);
        void Services.IBridgeContext.SyncLegacyDictsFromLabelManager() => SyncLegacyDictsFromLabelManager();
        IReadOnlyList<NavHistoryEntry> Services.IBridgeContext.GetNavHistorySnapshot()
        {
            lock (_navHistoryLock) return new List<NavHistoryEntry>(_navHistory);
        }
        void Services.IBridgeContext.ClearNavHistory()
        {
            lock (_navHistoryLock) _navHistory.Clear();
        }
        private TcpListener? _listener;
        private bool _isRunning;
        private readonly int _port;
        private CancellationTokenSource? _cancellationTokenSource;
        /// <summary>True when <see cref="Db"/> was created by this instance and must be disposed with it.</summary>
        private readonly bool _ownsDb;

        /// <summary>
        /// Dedicated binary fan-out for GS framebuffer streaming. Owns its own
        /// inbound socket to PCSX2's GSStreamServer and a separate outbound
        /// listener for webview/MCP subscribers. Independent from this class's
        /// JSON command socket so 60fps frame traffic cannot back up the
        /// JSON-RPC channel.
        /// </summary>
        private readonly GSStreamForwarder _gsStreamForwarder;

        /// <summary>
        /// Port the GS frame fan-out listens on for binary subscribers (the
        /// webview's high-rate path). 8765 is the JSON channel; 8766 is this.
        /// </summary>
        public int GsStreamListenerPort => _gsStreamForwarder.ListenerPort;

        /// <summary>
        /// Streams of clients that have subscribed to AI activity push notifications.
        /// </summary>
        private readonly List<NetworkStream> _activitySubscribers = new();
        private readonly object _subscribersLock = new();

        /// <summary>
        /// Commands that carry a target <see cref="CommandMessage.Address"/> and are
        /// interesting enough to push as an AI-activity notification.
        /// </summary>
        private static readonly HashSet<string> _activityAddressCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "READMEM", "WRITEMEM", "LOADBIN", "DISASSEMBLE",
            "SETBP", "CLEARBP",
            "SCANSTRINGS", "SCANFUNCTIONS", "SCANPOINTERS", "SCANFLOATS", "SCANINTEGERS", "SCANREFERENCES",
            "SCANVALUE",
            "NAVIGATETO",
        };

        /// <summary>
        /// Symbol/label database loaded from ps2MemMap.json at construction time.
        /// Key = virtual address (uint), Value = (Label, optional Comment).
        /// </summary>
        private readonly Dictionary<uint, (string Label, string? Comment)> _labels = new();

        /// <summary>
        /// User-defined struct definitions loaded from ps2MemMap.json at construction time.
        /// Key = struct name (case-sensitive).
        /// </summary>
        private readonly Dictionary<string, StructEntry> _structs = new(StringComparer.Ordinal);

        /// <summary>Central label manager: manages per-game label sets and the built-in symbols.</summary>
        internal readonly Ps2LabelManager LabelMgr = new();

        // ── Database ──────────────────────────────────────────────────────────
        /// <summary>Entity Framework database context shared across all repositories.</summary>
        internal CodeDesignerDbContext Db { get; }

        /// <summary>Persistence layer for label sets backed by the database.</summary>
        internal DbLabelRepository LabelRepo { get; }

        /// <summary>Persistence layer for application settings backed by the database.</summary>
        internal DbSettingsRepository SettingsRepo { get; }

        /// <summary>Persistence layer for MCP Memory Manager entries backed by the database.</summary>
        internal DbMcpMemoryRepository? McpMemoryRepo { get; }

        /// <summary>Persistence layer for MCP Agent profiles backed by the database.</summary>
        internal DbMcpAgentRepository? McpAgentRepo { get; }

        /// <summary>Persistence layer for MCP Orchestration profiles backed by the database.</summary>
        internal DbMcpOrchestrationRepository? McpOrchestrationRepo { get; }

        /// <summary>Persistence layer for disassembler data-format overrides.</summary>
        internal DbDataFormatRepository DataFormatRepo { get; }

        /// <summary>
        /// PCSX2 PINE client. Set in <c>--pcsx2</c> mode or after STARTPCSX2.
        /// Runs the PCSX2-specific PINE protocol (game metadata, OS thread
        /// inspection, etc.) on top of the cross-runtime <see cref="IDebugClient"/>
        /// surface.
        ///
        /// <para>
        /// Setting this also notifies <see cref="GSStreamForwarder"/> so it can
        /// open / drop the inbound GS-frame connection in lockstep with the
        /// PCSX2 attachment.
        /// </para>
        /// </summary>
        private PineClient? _pineClient;
        public PineClient? PineClient
        {
            get => _pineClient;
            set
            {
                _pineClient = value;
                if (value != null)
                    _gsStreamForwarder.AttachPineClient(value);
                else
                    _gsStreamForwarder.DetachPineClient();
            }
        }

        /// <summary>
        /// Built-in emulator client. Set after STARTEMULATOR / STARTFILEMODE
        /// spawns <c>CodeDesigner.Ps2.Emulator.exe</c>. Speaks PINE-shaped IPC
        /// to the emulator's <c>InternalPineSocket</c> on TCP 28012 — the peer
        /// of <see cref="PineClient"/> for the non-PCSX2 path.
        /// </summary>
        public InternalEmulatorClient? InternalClient { get; set; }

        /// <summary>
        /// Second built-in-emulator client used as the analysis substrate.
        /// See <see cref="IBridgeContext.AnalysisClient"/> for the contract.
        /// </summary>
        public InternalEmulatorClient? AnalysisClient { get; set; }

        /// <summary>
        /// Active <see cref="IDebugClient"/> for the current runtime mode —
        /// either <see cref="PineClient"/> (PCSX2 PINE) or
        /// <see cref="InternalClient"/> (built-in emulator). Returns <c>null</c>
        /// when neither client is attached. Handlers always go through this.
        /// </summary>
        public IDebugClient? Client => (IDebugClient?)PineClient ?? InternalClient;

        /// <summary>Whether any debug client is currently attached.</summary>
        public bool HasDebugClient => Client != null;

        /// <summary>
        /// Convenience for handlers that branch on PCSX2 mode specifically
        /// (e.g. PINE-only opcodes like NETGETSTATUS, GETMODULES). Prefer
        /// routing through <see cref="Client"/> when the operation is
        /// supported on both runtimes.
        /// </summary>
        private bool IsPineMode => PineClient != null;

        // ── Navigation history ────────────────────────────────────────────────
        // A bounded circular log of recently visited EE addresses, populated
        // automatically from address-bearing socket commands and from explicit
        // RECORDNAV calls sent by the VS Code extension when the user navigates
        // the disasm editor.

        private const int MaxNavHistory = 200;
        private readonly List<NavHistoryEntry> _navHistory     = new();
        private readonly object                _navHistoryLock = new();

        /// <summary>
        /// Record an address in the navigation history.  If the same address is
        /// already present it is moved to the end (most-recent position).
        /// Resolves the optional label from the loaded symbols dictionary.
        /// </summary>
        internal void RecordNavEntry(uint address, string source)
        {
            // Normalise to KSEG0 for a canonical display address.
            uint display  = Services.AddressTranslator.NormalizePcToKseg0(address);
            string addrStr = $"0x{display:X8}";

            // Try to find a label — check both the KSEG0 address and the
            // physical address (labels may be stored under either form).
            string? label = null;
            uint phys = Services.AddressTranslator.NormalizeBreakpointToPhysical(address);
            if (_labels.TryGetValue(display, out var l1)) label = l1.Label;
            else if (_labels.TryGetValue(phys, out var l2)) label = l2.Label;

            var entry = new NavHistoryEntry
            {
                Address   = addrStr,
                Label     = label,
                Source    = source,
                Timestamp = DateTime.UtcNow.ToString("o"),
            };

            lock (_navHistoryLock)
            {
                _navHistory.RemoveAll(e => e.Address == addrStr);
                _navHistory.Add(entry);
                if (_navHistory.Count > MaxNavHistory)
                    _navHistory.RemoveAt(0);
            }
        }

        // Restart used to be coordinated through PendingRestart + _restartComplete
        // back when the scheduler ran in-process. With the emulator now in its
        // own process, RESET is a fire-and-forget call to the active client
        // (PineClient.Reset() or InternalEmulatorClient.RequestRestart()).

        public event EventHandler<string>? MessageReceived;
        public event EventHandler<string>? ClientConnected;
        public event EventHandler? ClientDisconnected;
        /// <summary>
        /// Optional callback for runtime-mode commands handled by Program.cs.
        /// Used for STARTPCSX2/STARTEMULATOR style commands that need process-level control.
        /// </summary>
        public Func<CommandMessage, string>? RuntimeCommandHandler { get; set; }

        /// <summary>
        /// No-op retained for binary compatibility with code that may still call
        /// it. The old in-process restart coordination is gone — restarts are
        /// now driven through the active <see cref="IDebugClient"/>.
        /// </summary>
        public void SignalRestartComplete() { /* no-op */ }

        public SocketServer(int port = 8765)
            : this(port, CreateLocalDbContext(), ownsDb: true)
        {
        }

        /// <summary>
        /// Constructor used when the host application (e.g. MCP server) already has a
        /// configured <see cref="CodeDesignerDbContext"/> and wants to share it.
        /// The caller remains responsible for disposing <paramref name="db"/>.
        /// </summary>
        public SocketServer(int port, CodeDesignerDbContext db)
            : this(port, db, ownsDb: false)
        {
        }

        private SocketServer(int port, CodeDesignerDbContext db, bool ownsDb)
        {
            _port       = port;

            _ownsDb     = ownsDb;
            Db          = db;
            LabelRepo   = new DbLabelRepository(db);
            SettingsRepo = new DbSettingsRepository(db);
            McpMemoryRepo = new DbMcpMemoryRepository(db);
            McpAgentRepo  = new DbMcpAgentRepository(db);
            McpOrchestrationRepo = new DbMcpOrchestrationRepository(db);
            DataFormatRepo = new DbDataFormatRepository(db, LabelRepo);

            // Constructed before the service list because GsStreamService takes
            // a reference to it. The listener doesn't actually bind until Start().
            _gsStreamForwarder = new GSStreamForwarder();

            // Build the per-domain command services and the dispatcher that
            // routes commands to them. New services should be added to the
            // list below — see ICommandService for the migration recipe.
            Services.IBridgeContext ctx = this;
            // MemoryService and RegisterService are constructed first so peer
            // services that depend on them (e.g. WatchpointTraceService) can
            // be wired up via constructor injection.
            _memoryService        = new Services.MemoryService(ctx);
            _registerService      = new Services.RegisterService(ctx);
            _analysisStateService = new Services.AnalysisStateService(ctx);
            _dispatcher = new Services.CommandDispatcher(new Services.ICommandService[]
            {
                _memoryService,
                _registerService,
                _analysisStateService,
                new Services.ExecutionService(ctx),
                new Services.CheatsService(ctx),
                new Services.NetworkService(ctx),
                new Services.PadService(ctx),
                new Services.PineWatchService(ctx),
                new Services.SaveStateService(ctx),
                new Services.SteppingService(ctx),
                new Services.QueryDbService(ctx),
                new Services.NavHistoryService(ctx),
                new Services.SettingsService(ctx),
                new Services.StructService(ctx),
                new Services.SnapshotsService(ctx),
                new Services.MemoryCardService(ctx),
                new Services.ZipperFileFormatsService(),
                new Services.IsoService(ctx),
                new Services.LabelService(ctx),
                new Services.McpConfigService(ctx),
                new Services.AgentService(ctx),
                new Services.OrchestrationService(ctx),
                new Services.MemoryEntryService(ctx),
                new Services.SnapshotHistoryService(ctx),
                new Services.SymbolDatabaseService(ctx),
                new Services.GameDiscService(ctx),
                new Services.DiscordPresenceService(ctx),
                new Services.CompatService(ctx),
                new Services.CheatEditorService(ctx),
                new Services.HddService(ctx),
                new Services.ProcessStateService(ctx),
                new Services.WatchpointTraceService(ctx, _registerService),
                new Services.ValueScanService(ctx),
                new Services.BreakpointDisassemblyService(ctx),
                new Services.LabelManagerService(ctx),
                new Services.AssemblerService(ctx),
                new Services.CodeAnalysisService(ctx),
                new Services.VisualizerService(ctx),
                new Services.EmulatorControlService(ctx),
                new Services.DataFormatService(ctx),
                new Services.GsStreamService(ctx, _gsStreamForwarder),
            });
        }

        private readonly Services.MemoryService         _memoryService;
        private readonly Services.RegisterService       _registerService;
        private readonly Services.AnalysisStateService  _analysisStateService;
        Services.MemoryService        Services.IBridgeContext.Memory        => _memoryService;
        Services.AnalysisStateService Services.IBridgeContext.AnalysisState => _analysisStateService;

        /// <inheritdoc/>
        public void Dispose()
        {
            try { _gsStreamForwarder.Dispose(); } catch { /* swallow */ }
            if (_ownsDb)
                Db.Dispose();
        }

        /// <summary>
        /// Creates a <see cref="CodeDesignerDbContext"/> connected to the
        /// canonical SQL Server LocalDB instance (used when the caller does not
        /// supply a context).
        /// </summary>
        private static CodeDesignerDbContext CreateLocalDbContext()
        {
            string connectionString = DatabasePaths.GetDefaultConnectionString();
            var opts = DatabaseInitialiser.BuildDbOptions(connectionString);
            var ctx = new CodeDesignerDbContext(opts);
            ctx.Database.Migrate();
            return ctx;
        }

        /// <summary>
        /// Rebuild the legacy <c>_labels</c> and <c>_structs</c> dictionaries from the
        /// LabelManager's merged view (built-in + active user set).  This keeps all
        /// existing handlers working without modification while the new LabelManager
        /// becomes the single source of truth.
        /// </summary>
        internal void SyncLegacyDictsFromLabelManager()
        {
            _labels.Clear();
            _structs.Clear();

            var allEntries = LabelMgr.GetMergedLabels().ToList();

            // Build a lookup: parentAddress → list of child entries (not hidden).
            var childrenByParent = new Dictionary<string, List<CodeDesigner.Ps2.LabelManager.Models.LabelEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in allEntries)
            {
                if (e.ParentAddress == null || e.Hidden) continue;
                if (!childrenByParent.TryGetValue(e.ParentAddress, out var list))
                    childrenByParent[e.ParentAddress] = list = [];
                list.Add(e);
            }

            foreach (var entry in allEntries)
            {
                // Skip child entries and hidden entries (children are attached to their parent struct below).
                if (entry.ParentAddress != null) continue;
                if (entry.Hidden) continue;

                // Parse address
                var addrStr = entry.Address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? entry.Address[2..] : entry.Address;
                if (!uint.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber, null, out uint addr))
                    continue;

                switch (entry.Type)
                {
                    case LabelManager.Models.LabelType.Struct:
                    {
                        // Struct entry: goes in _structs (keyed by name) AND in _labels (pointer addr).
                        _labels[addr] = (entry.Name, entry.Comment);
                        var se = new StructEntry
                        {
                            Name        = entry.Name,
                            BaseAddress = entry.Address,
                        };
                        // Children of this struct are now full label entries with ParentAddress = entry.Address.
                        if (childrenByParent.TryGetValue(entry.Address, out var children))
                        {
                            foreach (var m in children.OrderBy(c => c.Offset ?? 0))
                                se.Members.Add(new StructMemberEntry { Offset = m.Offset ?? 0, Name = m.Name });
                        }
                        _structs[entry.Name] = se;
                        break;
                    }

                    case LabelManager.Models.LabelType.Function:
                    {
                        // Function: displayed as "FNC_Name". Arg/return regs embedded in comment.
                        string displayName = Ps2LabelManager.GetDisplayLabel(entry);
                        string? comment    = BuildFunctionComment(entry, entry.Comment);
                        _labels[addr] = (displayName, comment);
                        break;
                    }

                    case LabelManager.Models.LabelType.Array:
                    {
                        // Expand to Name[0], Name[1], … when stride + count are known.
                        _labels[addr] = (entry.Name, entry.Comment);
                        if (entry.ElementStride.HasValue && entry.ElementStride.Value > 0
                            && entry.ElementCount.HasValue && entry.ElementCount.Value > 0)
                        {
                            uint stride = (uint)entry.ElementStride.Value;
                            int  count  = entry.ElementCount.Value;
                            for (int i = 0; i < count; i++)
                            {
                                uint elemAddr = addr + stride * (uint)i;
                                _labels[elemAddr] = ($"{entry.Name}[{i}]", null);
                            }
                        }
                        break;
                    }

                    case LabelManager.Models.LabelType.ArrayOfPointers:
                    case LabelManager.Models.LabelType.LinkedList:
                    case LabelManager.Models.LabelType.String:
                    case LabelManager.Models.LabelType.Address:
                    default:
                    {
                        // Plain address label (or types requiring runtime resolution).
                        _labels[addr] = (entry.Name, entry.Comment);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Builds an extended comment for a <see cref="LabelType.Function"/> entry that
        /// includes arg/return register annotations when present.
        /// </summary>
        private static string? BuildFunctionComment(LabelManager.Models.LabelEntry entry, string? baseComment)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(baseComment))
                parts.Add(baseComment!);
            if (entry.ArgRegs is { Count: > 0 })
                parts.Add("args: " + string.Join(", ", entry.ArgRegs));
            if (entry.ReturnRegs is { Count: > 0 })
                parts.Add("ret: " + string.Join(", ", entry.ReturnRegs));
            return parts.Count > 0 ? string.Join(" | ", parts) : null;
        }

        public async Task StartAsync()
        {
            if (_isRunning)
            {
                Console.WriteLine("Server is already running.");
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _isRunning = true;

            // Bring up the GS frame fan-out alongside the JSON channel. Safe to
            // start unconditionally — it just listens; nothing flows until a
            // PineClient is attached and a subscriber connects.
            _gsStreamForwarder.Start();

            Console.WriteLine($"Socket server started on port {_port}");
            Console.WriteLine("Waiting for VSCode extension to connect...");

            try
            {
                while (_isRunning && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cancellationTokenSource.Token);
                    Console.WriteLine("Client connected!");
                    ClientConnected?.Invoke(this, client.Client.RemoteEndPoint?.ToString() ?? "Unknown");

                    // Handle client in a separate task
                    _ = Task.Run(() => HandleClientAsync(client, _cancellationTokenSource.Token));
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Server stopped.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Server error: {ex.Message}");
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                var stream = client.GetStream();
                var buffer = new byte[8192]; // Increased buffer size
                var messageBuffer = new StringBuilder();
                bool isSubscriber = false;

                try
                {
                    while (!cancellationToken.IsCancellationRequested && client.Connected)
                    {
                        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                        if (bytesRead == 0)
                        {
                            // Client disconnected
                            break;
                        }

                        var chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        messageBuffer.Append(chunk);

                        // Optimization: commands are newline-delimited.  A complete message
                        // can only be present if the latest chunk contains '\n'.  Skipping
                        // ToString() for partial chunks avoids O(n²) string allocations when
                        // receiving large payloads (e.g. file data sent with SEARCH commands).
                        if (chunk.IndexOf('\n') < 0)
                            continue;

                        // Process complete messages (delimited by newline)
                        string bufferContent = messageBuffer.ToString();
                        int newlineIndex;
                        
                        while ((newlineIndex = bufferContent.IndexOf('\n')) != -1)
                        {
                            string message = bufferContent.Substring(0, newlineIndex).Trim();
                            bufferContent = bufferContent.Substring(newlineIndex + 1);
                            messageBuffer.Clear();
                            messageBuffer.Append(bufferContent);

                            if (!string.IsNullOrEmpty(message))
                            {
                                MessageReceived?.Invoke(this, message);

                                // Handle SUBSCRIBE_ACTIVITY without going through ProcessCommand.
                                if (IsSubscribeActivityCommand(message))
                                {
                                    if (!isSubscriber)
                                    {
                                        lock (_subscribersLock) { _activitySubscribers.Add(stream); }
                                        isSubscriber = true;
                                    }
                                    var ackBytes = Encoding.UTF8.GetBytes(
                                        CreateSuccessResponse("Subscribed to AI activity notifications") + "\n");
                                    await stream.WriteAsync(ackBytes, 0, ackBytes.Length, cancellationToken);
                                    continue;
                                }

                                // Process command and generate response
                                var response = ProcessCommand(message);
                                var responseBytes = Encoding.UTF8.GetBytes(response + "\n");
                                await stream.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken);

                                // Push an activity notification to subscribed clients (e.g. the
                                // VSCode extension) when the command originates from a non-subscriber
                                // (e.g. the MCP server).  This lets the editor follow the AI's work.
                                if (!isSubscriber)
                                {
                                    _ = Task.Run(() => TryPushActivityNotification(message, response, stream));
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Client error: {ex.Message}");
                }
                finally
                {
                    if (isSubscriber)
                    {
                        lock (_subscribersLock) { _activitySubscribers.Remove(stream); }
                    }
                    Console.WriteLine("Client disconnected.");
                    ClientDisconnected?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Returns <c>true</c> when the raw JSON message is a SUBSCRIBE_ACTIVITY command.
        /// Uses a lightweight property lookup to avoid a full deserialization round-trip.
        /// </summary>
        private static bool IsSubscribeActivityCommand(string message)
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                if (doc.RootElement.TryGetProperty("Command", out var cmd) ||
                    doc.RootElement.TryGetProperty("command", out cmd))
                {
                    return string.Equals(cmd.GetString(), "SUBSCRIBE_ACTIVITY", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { /* malformed JSON — not a subscribe command */ }
            return false;
        }

        /// <summary>
        /// Parses <paramref name="requestMessage"/> to extract a target address and, if the
        /// command is one of the interesting address-based operations, pushes a JSON
        /// <c>{"type":"ai_activity",...}</c> notification to every subscribed stream
        /// except <paramref name="requesterStream"/> (so VSCode's own reads never echo back).
        /// </summary>
        private async Task TryPushActivityNotification(string requestMessage, string responseMessage, NetworkStream requesterStream)
        {
            try
            {
                List<NetworkStream> subscribers;
                lock (_subscribersLock) { subscribers = new List<NetworkStream>(_activitySubscribers); }
                if (subscribers.Count == 0) { return; }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var command = JsonSerializer.Deserialize<CommandMessage>(requestMessage, options);
                if (command == null) { return; }

                string? notificationJson = null;
                var cmdUpper = command.Command?.ToUpperInvariant();

                if (cmdUpper != null && command.Address.HasValue && _activityAddressCommands.Contains(cmdUpper))
                {
                    notificationJson = JsonSerializer.Serialize(new
                    {
                        type    = "ai_activity",
                        command = command.Command,
                        address = command.Address.Value,
                        region  = command.Region ?? "ee",
                    });
                }
                else if (cmdUpper == "GETPC")
                {
                    // Extract the PC from the response's PC field.
                    var resp = JsonSerializer.Deserialize<CommandResponse>(responseMessage, options);
                    if (resp?.Success == true && resp.PC.HasValue)
                    {
                        notificationJson = JsonSerializer.Serialize(new
                        {
                            type    = "ai_activity",
                            command = "GETPC",
                            address = resp.PC.Value,
                            region  = "ee",
                        });
                    }
                }
                else if (cmdUpper == "SESSION_START")
                {
                    // Notify subscribers that an AI session has started so the
                    // front end can automatically enable Follow AI mode.
                    notificationJson = JsonSerializer.Serialize(new
                    {
                        type = "ai_session_start",
                    });
                }
                else if (cmdUpper is "ADDLABELENTRY" or "ADDLABELENTRIES" or "CREATELABELSET" or "DELETELABELENTRY" or "DELETELABELSET")
                {
                    // Notify subscribers that the label set has changed so the
                    // front end can refresh the label manager UI.
                    notificationJson = JsonSerializer.Serialize(new
                    {
                        type    = "ai_label_update",
                        command = command.Command,
                        setName = command.SetName,
                        gameId  = command.GameId,
                    });
                }

                if (notificationJson == null) { return; }

                var bytes = Encoding.UTF8.GetBytes(notificationJson + "\n");
                foreach (var sub in subscribers)
                {
                    if (ReferenceEquals(sub, requesterStream)) { continue; }
                    try { await sub.WriteAsync(bytes, 0, bytes.Length); }
                    catch { /* subscriber disconnected; removal handled by its own HandleClientAsync */ }
                }
            }
            catch { /* ignore any notification errors */ }
        }

        /// <summary>
        /// Process incoming command and return response
        /// </summary>
        private string ProcessCommand(string message)
        {
            try
            {
                // Parse JSON command with case-insensitive property names
                var options = new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                };
                var command = JsonSerializer.Deserialize<CommandMessage>(message, options);
                
                if (command == null || string.IsNullOrEmpty(command.Command))
                {
                    return CreateErrorResponse("Invalid command format");
                }

                // Auto-record navigation history for any address-bearing command.
                var cmdNorm = command.Command.ToUpperInvariant();
                if (command.Address.HasValue &&
                    _activityAddressCommands.Contains(cmdNorm) &&
                    cmdNorm != "NAVIGATETO") // NAVIGATETO records explicitly in its handler
                {
                    var navSource = string.IsNullOrWhiteSpace(command.NavSource)
                        ? "mcp"
                        : command.NavSource!;
                    if (!string.Equals(navSource, "user", StringComparison.OrdinalIgnoreCase))
                    {
                        RecordNavEntry(command.Address.Value, navSource);
                    }
                }

                // Try the per-domain command dispatcher first. Returns null
                // when no migrated service owns this command — falls through
                // to the legacy switch for handlers still in partial classes.
                var dispatched = _dispatcher.TryDispatch(command);
                if (dispatched != null) return dispatched;

                switch (command.Command.ToUpperInvariant())
                {
                    case "STARTPCSX2":
                    case "START_PCSX2":
                    case "STARTPCSX2MODE":
                    case "START_PCSX2_MODE":
                    case "STARTEMULATOR":
                    case "START_EMULATOR":
                    case "STARTBUILTINEMULATOR":
                    case "START_BUILTIN_EMULATOR":
                    case "STARTEMULATORMODE":
                    case "START_EMULATOR_MODE":
                    case "STARTFILEMODE":
                    case "START_FILE_MODE":
                    case "STARTANALYSISEMU":
                    case "START_ANALYSIS_EMU":
                    case "STARTANALYSISEMULATOR":
                    case "START_ANALYSIS_EMULATOR":
                        if (RuntimeCommandHandler == null)
                            return CreateErrorResponse("Runtime mode switching is not enabled in this server instance");
                        return RuntimeCommandHandler(command);

                    // All command-specific cases below have been migrated into
                    // Services/* — see ICommandService for the recipe. The
                    // dispatcher above handles them. Aliases that don't match
                    // a registered name are bounced via DispatchAlias().
                    // GETTHREADS, GETIOPTHREADS → ProcessStateService (migrated)
                    case "GET_IOP_THREADS":
                    case "IOPTHREADS":         return DispatchAlias(command, "GETIOPTHREADS");

                    // PINEDISASSEMBLE → BreakpointDisassemblyService (migrated)
                    case "PINE_DISASSEMBLE":
                    case "PINE_DISASM":        return DispatchAlias(command, "PINEDISASSEMBLE");

                    case "SCAN_REFERENCES":    return DispatchAlias(command, "SCANREFERENCES");

                    // CONVERTCHEAT, PORTCHEATS → CheatsService (migrated)

                    // SCANVALUE → ValueScanService (migrated)

                    // Structs → StructService (migrated)

                    // Label-manager commands → LabelManagerService (migrated)

                    // Memory-card → MemoryCardService (migrated)

                    // HDD → HddService (migrated)

                    // ISO → IsoService (migrated)

                    case "PING":
                        return CreateSuccessResponse("PONG");

                    // PCSX2STATUS, PCSX2GAMEINFO → EmulatorControlService (migrated)

                    // RESET, STEPINTO/OVER/OUT, GETFPS, SETLIMITER, FRAMEADVANCE
                    // → SaveStateService / SteppingService (migrated). Aliases:
                    case "STEP_INTO":   return DispatchAlias(command, "STEPINTO");
                    case "STEP_OVER":   return DispatchAlias(command, "STEPOVER");
                    case "STEP_OUT":    return DispatchAlias(command, "STEPOUT");
                    case "GET_FPS":     return DispatchAlias(command, "GETFPS");
                    case "SET_LIMITER": return DispatchAlias(command, "SETLIMITER");
                    case "FRAME_ADVANCE": return DispatchAlias(command, "FRAMEADVANCE");

                    // LISTBP / LISTBREAKPOINTS → BreakpointDisassemblyService (migrated)
                    case "LIST_BP":
                    case "LISTBREAKPOINTS": return DispatchAlias(command, "LISTBP");

                    // GETSYMBOL → SteppingService (migrated)
                    case "GET_SYMBOL": return DispatchAlias(command, "GETSYMBOL");

                    // GETSYMBOLBYNAME, LISTFUNCTIONS, LISTGLOBALS, GETLOCALS → SymbolDatabaseService (migrated)
                    case "GET_SYMBOL_BY_NAME": return DispatchAlias(command, "GETSYMBOLBYNAME");

                    // GETSTACK/GETMODULES → ProcessStateService; GETREGISTER/PINESETREG → RegisterService (migrated)
                    case "GET_STACK":
                    case "CALLSTACK":         return DispatchAlias(command, "GETSTACK");
                    case "GET_MODULES":
                    case "IOPMODULES":        return DispatchAlias(command, "GETMODULES");
                    case "GET_REGISTER":
                    case "PINEGETREGISTER":   return DispatchAlias(command, "GETREGISTER");
                    case "PINE_SETREG":
                    case "PINESETREGISTER":   return DispatchAlias(command, "PINESETREG");

                    // SaveState slot/file ops → SaveStateService (migrated)
                    case "SAVE_STATE_FILE":  return DispatchAlias(command, "SAVESTATETOFILE");
                    case "LOAD_STATE_FILE":  return DispatchAlias(command, "LOADSTATEFROMFILE");
                    case "PINE_VERSION":     return DispatchAlias(command, "PINEVERSION");
                    case "PINE_SAVESTATE":
                    case "PINE_SAVE_STATE":  return DispatchAlias(command, "PINESAVESTATE");
                    case "PINE_LOADSTATE":
                    case "PINE_LOAD_STATE":  return DispatchAlias(command, "PINELOADSTATE");

                    // EXTRACTP2S → EmulatorControlService (migrated)
                    case "EXTRACT_P2S": return DispatchAlias(command, "EXTRACTP2S");

                    // READMEMREGION, SCANPATTERN → MemoryService (migrated). The
                    // alias forms (READ_MEM_REGION, SCAN_PATTERN) are still
                    // routed here for backwards compatibility — they redirect
                    // through the dispatcher with the canonical name.
                    case "READ_MEM_REGION":
                    case "READMEM_REGION":
                        return _dispatcher.TryDispatch(new CommandMessage
                        {
                            Command       = "READMEMREGION",
                            Address       = command.Address,
                            EndAddress    = command.EndAddress,
                            SearchPattern = command.SearchPattern,
                            Region        = command.Region,
                        }) ?? CreateErrorResponse("READMEMREGION not registered");

                    case "SCAN_PATTERN":
                        return _dispatcher.TryDispatch(new CommandMessage
                        {
                            Command       = "SCANPATTERN",
                            Address       = command.Address,
                            EndAddress    = command.EndAddress,
                            SearchPattern = command.SearchPattern,
                            Region        = command.Region,
                        }) ?? CreateErrorResponse("SCANPATTERN not registered");

                    // TRACECALLS / RUNUNTILPC → WatchpointTraceService (migrated)
                    case "TRACE_CALLS":   return DispatchAlias(command, "TRACECALLS");
                    case "RUN_UNTIL_PC":  return DispatchAlias(command, "RUNUNTILPC");


                    // PINE hardware watchpoints → PineWatchService (migrated)
                    case "PINE_ADD_WATCH":         return DispatchAlias(command, "PINEADDWATCH");
                    case "PINE_REMOVE_WATCH":      return DispatchAlias(command, "PINEREMOVEWATCH");
                    case "PINE_LIST_WATCHES":      return DispatchAlias(command, "PINELISTWATCHES");
                    case "PINE_CLEAR_ALL_WATCHES": return DispatchAlias(command, "PINECLEARALLWATCHES");
                    case "PINE_SAVE_SCREENSHOT":   return DispatchAlias(command, "PINESAVESCREENSHOT");

                    // Pad / controller input → PadService (migrated)
                    case "PAD_GET_STATE":  return DispatchAlias(command, "PADGETSTATE");
                    case "PAD_SET_BUTTON": return DispatchAlias(command, "PADSETBUTTON");
                    case "PAD_SET_ANALOG": return DispatchAlias(command, "PADSETANALOG");
                    case "PAD_SET_STATE":  return DispatchAlias(command, "PADSETSTATE");
                    case "PAD_GET_TYPE":   return DispatchAlias(command, "PADGETTYPE");

                    // DEV9 network bridge → NetworkService (migrated). Aliases
                    // (NET_GET_STATUS / NET_CAPTURE / etc.) bounce through the
                    // dispatcher with the canonical name.
                    case "NET_GET_STATUS":   return DispatchAlias(command, "NETGETSTATUS");
                    case "NET_CAPTURE":      return DispatchAlias(command, "NETCAPTURE");
                    case "NET_RECV_CAPTURE": return DispatchAlias(command, "NETRECVCAPTURE");
                    case "NET_INJECT":       return DispatchAlias(command, "NETINJECT");
                    case "NET_INJECT_TX":    return DispatchAlias(command, "NETINJECTTX");
                    case "NET_DISASSEMBLE":  return DispatchAlias(command, "NETDISASSEMBLE");
                    case "NET_SET_FILTER":   return DispatchAlias(command, "NETSETFILTER");
                    case "NET_GET_STATS":    return DispatchAlias(command, "NETGETSTATS");

                    // QUERYDB / NAV history / Settings / Snapshots / Memory-card
                    // → migrated to dedicated services. Aliases bounce through.
                    case "QUERY_DB":            return DispatchAlias(command, "QUERYDB");
                    case "GET_NAV_HISTORY":     return DispatchAlias(command, "GETNAVHISTORY");
                    case "RECORD_NAV":          return DispatchAlias(command, "RECORDNAV");
                    case "CLEAR_NAV_HISTORY":   return DispatchAlias(command, "CLEARNAVHISTORY");
                    case "NAVIGATE_TO":         return DispatchAlias(command, "NAVIGATETO");
                    case "GET_SETTING":         return DispatchAlias(command, "GETSETTING");
                    case "SET_SETTING":         return DispatchAlias(command, "SETSETTING");
                    case "GET_ALL_SETTINGS":    return DispatchAlias(command, "GETALLSETTINGS");
                    case "DELETE_SETTING":      return DispatchAlias(command, "DELETESETTING");

                    // Game-disc + Snapshot-address-history → migrated
                    case "UPDATE_GAME_DISC":   return DispatchAlias(command, "UPDATEGAMEDISC");
                    case "GET_ADDRESS_HISTORY": return DispatchAlias(command, "GETADDRESSHISTORY");

                    // MCP Memory/Agent/Orchestration → MemoryEntryService /
                    // AgentService / OrchestrationService (migrated)

                    // MCP Config → McpConfigService (migrated)

                    // ── AI session lifecycle ───────────────────────────────────────────
                    case "SESSION_START":
                        return CreateSuccessResponse("AI session started");

                    // ADDLABELENTRIES → LabelManagerService (migrated)

                    default:
                        // Echo for unknown commands
                        return $"Echo: {message}";
                }
            }
            catch (JsonException ex)
            {
                // Return a proper JSON error response so clients don't get an
                // unparseable "Echo:" reply that hides the real failure.
                return CreateErrorResponse($"Invalid command JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Error processing command: {ex.Message}");
            }
        }
        /// <summary>
        /// Re-dispatch a command under a different (canonical) name. Used by
        /// the legacy switch to forward snake_case aliases to the registered
        /// canonical command name without duplicating handler logic.
        /// </summary>
        private string DispatchAlias(CommandMessage original, string canonicalName)
        {
            // Mutate the existing message's Command in-place so all the
            // typed payload fields carry through unchanged.
            original.Command = canonicalName;
            return _dispatcher.TryDispatch(original)
                ?? CreateErrorResponse($"{canonicalName} is not registered with the dispatcher");
        }

        private string CreateSuccessResponse(string message)
        {
            var response = new CommandResponse
            {
                Success = true,
                Message = message
            };
            return JsonSerializer.Serialize(response);
        }

        private string CreateErrorResponse(string error)
        {
            var response = new CommandResponse
            {
                Success = false,
                Error = error
            };
            return JsonSerializer.Serialize(response);
        }

        public async Task SendMessageAsync(TcpClient client, string message)
        {
            if (client.Connected)
            {
                var stream = client.GetStream();
                var bytes = Encoding.UTF8.GetBytes(message);
                await stream.WriteAsync(bytes, 0, bytes.Length);
            }
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            _cancellationTokenSource?.Cancel();
            _listener?.Stop();
            Console.WriteLine("Server stopped.");
        }
    }
}
