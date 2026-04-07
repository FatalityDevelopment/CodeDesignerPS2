using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using CodeDesigner.Ps2.MCManager;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.CheatEngine;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.Emulator;
using CodeDesigner.Ps2.Emulator.Hardware;
using CodeDesigner.Ps2.Emulator.IOP;
using Microsoft.EntityFrameworkCore;

using CheatPorterLib = CodeDesigner.Ps2.CheatEngine.CheatPorter;
using CodeDesigner.Ps2.LabelManager;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Socket server for communication with VSCode extension
    /// </summary>
    public partial class SocketServer : IDisposable
    {
        private TcpListener? _listener;
        private bool _isRunning;
        private readonly int _port;
        private CancellationTokenSource? _cancellationTokenSource;
        /// <summary>True when <see cref="Db"/> was created by this instance and must be disposed with it.</summary>
        private readonly bool _ownsDb;

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
            "SCANSTRINGS", "SCANFUNCTIONS", "SCANPOINTERS", "SCANFLOATS", "SCANINTEGERS",
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

        /// <summary>
        /// When set, the READMEM command reads live memory from this scheduler.
        /// </summary>
        public DualCpuScheduler? Scheduler { get; set; }

        /// <summary>
        /// When set, memory and debugger commands are routed through the PCSX2 PINE
        /// IPC socket instead of the internal emulator.  Requires PCSX2 to have PINE
        /// enabled (Settings → Advanced → Enable PINE).
        /// <para>
        /// Basic commands (READMEM, WRITEMEM, APPLYCHEATS, ASSEMBLE, DISASSEMBLE, …)
        /// work with the standard upstream PINE protocol.
        /// </para>
        /// <para>
        /// Debugger commands (PAUSE, RESUME, STEP, SETBP, GETPC, GETREGS) require
        /// the PINE protocol extension — see <c>PINE_EXTENSION_PROPOSAL.md</c> for
        /// the C++ implementation to submit as a PCSX2 pull request.
        /// </para>
        /// </summary>
        public PineClient? PineClient { get; set; }

        /// <summary>
        /// Returns <c>true</c> when the server should route memory operations
        /// through PINE (a <see cref="PineClient"/> is attached and no internal
        /// <see cref="Scheduler"/> is present).
        /// </summary>
        private bool IsPineMode => PineClient != null && Scheduler == null;

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
            uint display  = NormalizePcToKseg0(address);
            string addrStr = $"0x{display:X8}";

            // Try to find a label — check both the KSEG0 address and the
            // physical address (labels may be stored under either form).
            string? label = null;
            uint phys = NormalizeBreakpointToPhysical(address);
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

        /// <summary>
        /// Set to <c>true</c> by <see cref="HandleRestart"/> to ask the emulator
        /// run-loop (in Program.cs) to perform a full hardware reset: create a
        /// fresh <see cref="DualCpuScheduler"/>, re-load the BIOS and disc, and
        /// leave the emulator paused at the reset vector.  The run-loop clears this
        /// flag once the new scheduler is installed.
        /// </summary>
        public volatile bool PendingRestart;

        /// <summary>
        /// Signalled by the emulator run-loop once the new scheduler is fully installed
        /// after a restart.  <see cref="HandleRestart"/> waits on this gate so that it
        /// does not return "Restarted" to the client until <see cref="Scheduler"/> is
        /// the new instance — guaranteeing that subsequent SETBP and RESUME commands
        /// operate on the correct scheduler.
        /// </summary>
        private readonly ManualResetEventSlim _restartComplete = new ManualResetEventSlim(false);

        public event EventHandler<string>? MessageReceived;
        public event EventHandler<string>? ClientConnected;
        public event EventHandler? ClientDisconnected;
        /// <summary>
        /// Optional callback for runtime-mode commands handled by Program.cs.
        /// Used for STARTPCSX2/STARTEMULATOR style commands that need process-level control.
        /// </summary>
        public Func<CommandMessage, string>? RuntimeCommandHandler { get; set; }

        /// <summary>
        /// Called by the emulator run-loop after a restart is fully complete (new
        /// scheduler installed and paused at the reset vector).  Unblocks
        /// <see cref="HandleRestart"/> so it can return the "Restarted" response to
        /// the client only once SETBP/RESUME will target the correct scheduler.
        /// </summary>
        public void SignalRestartComplete() => _restartComplete.Set();

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

            // Migrate legacy flat-file data into the DB on first run (idempotent).
            MigrateLegacySettingsToDb();

            // Warm up the legacy _labels/_structs dictionaries from the DB so that
            // existing handlers continue to work without modification.
            LoadLabels();
            LoadStructs();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_ownsDb)
                Db.Dispose();
        }

        /// <summary>
        /// Creates a SQLite <see cref="CodeDesignerDbContext"/> pointing at the
        /// canonical application-data database path (used when the caller does not
        /// supply a context).
        /// </summary>
        private static CodeDesignerDbContext CreateLocalDbContext()
        {
            string dbPath = DatabasePaths.GetDefaultDbPath();
            var opts = new DbContextOptionsBuilder<CodeDesignerDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            var ctx = new CodeDesignerDbContext(opts);
            ctx.Database.Migrate();
            ctx.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            return ctx;
        }

        /// <summary>
        /// One-time migration: if <c>codedesigner-settings.json</c> is present and the
        /// <c>ui.selectedEmulator</c> key is not yet in the DB, import the file values as
        /// <see cref="CodeDesigner.Ps2.Database.Entities.Setting"/> rows, then rename the
        /// file to <c>.bak</c> so the migration is not repeated on the next startup.
        /// </summary>
        private void MigrateLegacySettingsToDb()
        {
            try
            {
                var searchPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "codedesigner-settings.json"),
                    Path.Combine(Directory.GetCurrentDirectory(),       "codedesigner-settings.json"),
                };

                foreach (var settingsPath in searchPaths)
                {
                    if (!File.Exists(settingsPath)) continue;

                    // Only migrate if the target keys are not already set.
                    bool alreadyMigrated = Db.Settings.AnyAsync(s => s.Key == "ui.selectedEmulator").GetAwaiter().GetResult();
                    if (alreadyMigrated) break;

                    try
                    {
                        var json  = File.ReadAllText(settingsPath);
                        using var doc = JsonDocument.Parse(json);
                        var root  = doc.RootElement;

                        void Upsert(string key, string? val)
                        {
                            if (string.IsNullOrWhiteSpace(val)) return;
                            SettingsRepo.SetAsync(key, val).GetAwaiter().GetResult();
                        }

                        if (root.TryGetProperty("selectedEmulator", out var e)) Upsert("ui.selectedEmulator", e.GetString());
                        if (root.TryGetProperty("selectedBios",     out var b)) Upsert("ui.selectedBios",     b.GetString());
                        if (root.TryGetProperty("selectedGame",     out var g)) Upsert("ui.selectedGame",     g.GetString());

                        File.Move(settingsPath, settingsPath + ".bak", overwrite: true);
                        Console.WriteLine($"[DB] Migrated user preferences from {settingsPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DB] Could not migrate {settingsPath}: {ex.Message}");
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Settings migration failed: {ex.Message}");
            }
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
                    case CodeDesigner.Ps2.LabelManager.Models.LabelType.Struct:
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

                    case CodeDesigner.Ps2.LabelManager.Models.LabelType.Function:
                    {
                        // Function: displayed as "FNC_Name". Arg/return regs embedded in comment.
                        string displayName = CodeDesigner.Ps2.LabelManager.Ps2LabelManager.GetDisplayLabel(entry);
                        string? comment    = BuildFunctionComment(entry, entry.Comment);
                        _labels[addr] = (displayName, comment);
                        break;
                    }

                    case CodeDesigner.Ps2.LabelManager.Models.LabelType.Array:
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

                    case CodeDesigner.Ps2.LabelManager.Models.LabelType.ArrayOfPointers:
                    case CodeDesigner.Ps2.LabelManager.Models.LabelType.LinkedList:
                    case CodeDesigner.Ps2.LabelManager.Models.LabelType.String:
                    case CodeDesigner.Ps2.LabelManager.Models.LabelType.Address:
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
        private static string? BuildFunctionComment(CodeDesigner.Ps2.LabelManager.Models.LabelEntry entry, string? baseComment)
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

        /// <summary>
        /// Load PS2 symbol labels from ps2MemMap.json.
        /// Searches the application base directory first, then the current working
        /// directory, so the file can be placed next to the built executable or in
        /// the directory from which the process was launched.
        /// </summary>
        private void LoadLabels()
        {
            var searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ps2MemMap.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "ps2MemMap.json"),
                // backward-compat: also check old filename
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ps2-labels.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "ps2-labels.json")
            };

            foreach (var labelsPath in searchPaths)
            {
                if (!File.Exists(labelsPath)) continue;
                try
                {
                    var json = File.ReadAllText(labelsPath);
                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("symbol", out var symbols)) continue;

                    foreach (var sym in symbols.EnumerateArray())
                    {
                        string? addrStr = sym.TryGetProperty("address", out var ap) ? ap.GetString() : null;
                        string? label   = sym.TryGetProperty("label",   out var lp) ? lp.GetString() : null;
                        string? comment = sym.TryGetProperty("comment", out var cp) ? cp.GetString() : null;

                        if (addrStr is null || label is null) continue;

                        // Support both "0x1000F000" and plain "268500992" formats
                        if (addrStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                            addrStr = addrStr[2..];

                        if (uint.TryParse(addrStr,
                                System.Globalization.NumberStyles.HexNumber,
                                null, out uint addr))
                        {
                            _labels[addr] = (label, comment);
                        }
                    }
                    Console.WriteLine($"[Labels] Loaded {_labels.Count} symbol(s) from {labelsPath}");
                    return; // use only the first file found
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Labels] Failed to load {labelsPath}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Load struct definitions from the "structs" array in ps2MemMap.json.
        /// Searches the same paths as <see cref="LoadLabels"/>.
        /// </summary>
        private void LoadStructs()
        {
            var searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ps2MemMap.json"),
                Path.Combine(Directory.GetCurrentDirectory(),       "ps2MemMap.json")
            };

            foreach (var path in searchPaths)
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("structs", out var structsEl))
                    {
                        Console.WriteLine($"[Structs] No 'structs' array in {path}; 0 struct(s) loaded");
                        return;
                    }

                    foreach (var s in structsEl.EnumerateArray())
                    {
                        var name    = s.TryGetProperty("name",        out var np) ? np.GetString() : null;
                        var baseAddr = s.TryGetProperty("baseAddress", out var bp) ? bp.GetString() : null;

                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(baseAddr)) continue;

                        var entry = new StructEntry { Name = name!, BaseAddress = baseAddr! };

                        if (s.TryGetProperty("members", out var membersEl))
                        {
                            foreach (var m in membersEl.EnumerateArray())
                            {
                                int  offset     = m.TryGetProperty("offset", out var op) ? op.GetInt32() : 0;
                                string? mName   = m.TryGetProperty("name",   out var mn) ? mn.GetString() : null;
                                if (!string.IsNullOrWhiteSpace(mName))
                                    entry.Members.Add(new StructMemberEntry { Offset = offset, Name = mName! });
                            }
                        }

                        _structs[name!] = entry;
                    }
                    Console.WriteLine($"[Structs] Loaded {_structs.Count} struct(s) from {path}");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Structs] Failed to load structs from {path}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Persist both label symbols and struct definitions to ps2MemMap.json.
        /// This is the single authoritative write path for the memory-map file so that
        /// <see cref="LabelHandlers.SaveLabels"/> and <see cref="StructHandlers.SaveStructs"/>
        /// always produce a consistent file without duplicating write logic.
        /// </summary>
        internal void SaveMemoryMap()
        {
            var searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ps2MemMap.json"),
                Path.Combine(Directory.GetCurrentDirectory(),       "ps2MemMap.json")
            };

            string savePath = searchPaths[0];
            foreach (var p in searchPaths)
            {
                if (File.Exists(p)) { savePath = p; break; }
            }

            var symbols = _labels
                .OrderBy(kv => kv.Key)
                .Select(kv => new LabelEntry
                {
                    Address = $"0x{kv.Key:X8}",
                    Label   = kv.Value.Label,
                    Comment = kv.Value.Comment
                })
                .ToList();

            var structs = _structs.Values
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var json = JsonSerializer.Serialize(
                new { symbol = symbols, structs },
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(savePath, json);
            Console.WriteLine($"[MemMap] Saved {_labels.Count} symbol(s) and {_structs.Count} struct(s) to {savePath}");
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
                    RecordNavEntry(command.Address.Value, "mcp");
                }

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
                        if (RuntimeCommandHandler == null)
                            return CreateErrorResponse("Runtime mode switching is not enabled in this server instance");
                        return RuntimeCommandHandler(command);

                    case "ASSEMBLE":
                        return HandleAssemble(command.Data);

                    case "COMPILECDS":
                        return HandleCompileCds(command.Data, command.Labels);
                    
                    case "DISASSEMBLE":
                        return HandleDisassemble(command.Data, command.Address);

                    case "READMEM":
                        return HandleReadMem(command.Address ?? 0, command.Length ?? 256, command.Region);

                    case "SNAPSHOT":
                        return HandleSnapshot(command.OutputPath);

                    case "LOADSNAPSHOT":
                        return HandleLoadSnapshot(command.InputPath);
                    
                    case "PAUSE":
                        return HandlePause();

                    case "RESUME":
                        return HandleResume();

                    case "STEP":
                        return HandleStep();

                    case "SETBP":
                        return HandleSetBreakpoint(command.BreakpointAddress);

                    case "CLEARBP":
                        return HandleClearBreakpoint(command.BreakpointAddress);

                    case "CLEARBP_ALL":
                        return HandleClearAllBreakpoints();

                    case "GETPC":
                        return HandleGetPC();

                    case "GETREGS":
                        return HandleGetRegisters();

                    case "SETREG":
                        return HandleSetRegister(command.RegisterName, command.RegisterIndex, command.RegisterValue);

                    case "WRITEMEM":
                        return HandleWriteMem(command.Address ?? 0, command.Data, command.Region);

                    case "LOADBIN":
                        return HandleLoadBin(command.InputPath, command.Address ?? 0, command.Region);

                    case "APPLYCHEATS":
                        return HandleApplyCheats(command.Data);

                    case "RESTART":
                        return HandleRestart();

                    case "GETLABELS":
                        return HandleGetLabels();

                    case "ADDLABEL":
                        return HandleAddLabel(command.Address, command.Label, command.Comment);

                    case "DELETELABEL":
                        return HandleDeleteLabel(command.Address);

                    case "GETSTRINGS":
                        return HandleGetStrings(command.Address ?? 0, command.Data);

                    case "GETFUNCTIONS":
                        return HandleGetFunctions(command.Address ?? 0, command.Data);

                    case "GETPOINTERS":
                        return HandleGetPointers(command.Address ?? 0, command.Data);

                    case "GETFLOATS":
                        return HandleGetFloats(command.Address ?? 0, command.Data);

                    case "GETINTEGERS":
                        return HandleGetIntegers(command.Address ?? 0, command.Data);

                    case "SCANSTRINGS":
                        return HandleScanStrings(command.Address ?? 0, command.Length ?? 0, command.Region);

                    case "SCANFUNCTIONS":
                        return HandleScanFunctions(command.Address ?? 0, command.Length ?? 0, command.Region);

                    case "SCANPOINTERS":
                        return HandleScanPointers(command.Address ?? 0, command.Length ?? 0, command.Region);

                    case "SCANFLOATS":
                        return HandleScanFloats(command.Address ?? 0, command.Length ?? 0, command.Region);

                    case "SCANINTEGERS":
                        return HandleScanIntegers(command.Address ?? 0, command.Length ?? 0, command.Region);

                    case "SEARCH":
                        return HandleSearch(command.Query ?? "", command.SearchMode ?? "text",
                                            command.Address ?? 0, command.Length ?? 0,
                                            command.Region, command.Data);

                    case "GETTHREADS":
                        return HandleGetThreads();

                    case "GETIOPTHREADS":
                    case "GET_IOP_THREADS":
                    case "IOPTHREADS":
                        return HandleGetIopThreads();

                    case "PINEDISASSEMBLE":
                    case "PINE_DISASSEMBLE":
                    case "PINE_DISASM":
                        return HandlePineDisassemble(command.CpuSel, command.Address, command.Length.HasValue ? (ushort?)command.Length.Value : null);

                    case "LISTDISCELFS":
                        return HandleListDiscElfs(command.IsoPath);

                    case "RECOMPILE":
                        return HandleRecompile(command.IsoPath, command.ElfName, command.OutputDirectory);

                    case "CONVERTCHEAT":
                        return HandleConvertCheat(command.Data, command.Format, command.Direction);

                    case "PORTCHEATS":
                        return HandlePortCheats(command.Data, command.SourceDumpPath, command.TargetDumpPath);

                    case "SCANVALUE":
                        return HandleScanValue(
                            command.Address    ?? 0,
                            command.EndAddress ?? 0x02000000u,
                            command.ScanValue,
                            command.Comparison,
                            command.DataType,
                            command.Region,
                            command.ScanAddresses);

                    case "GETSTRUCTS":
                        return HandleGetStructs();

                    case "ADDSTRUCT":
                        return HandleAddStruct(command.StructName, command.StructBaseAddress);

                    case "DELETESTRUCT":
                        return HandleDeleteStruct(command.StructName);

                    case "ADDSTRUCTMEMBER":
                        return HandleAddStructMember(command.StructName, command.MemberOffset, command.MemberName);

                    // ── Label Manager commands ────────────────────────────────────────
                    case "GETLABELSETS":
                        return HandleGetLabelSets(command.WorkspaceDir, command.GameId);

                    case "GETLABELSET":
                        return HandleGetLabelSet(command.WorkspaceDir, command.GameId, command.SetName);

                    case "SETACTIVELABELSET":
                        return HandleSetActiveLabelSet(command.WorkspaceDir, command.GameId, command.SetName);

                    case "CREATELABELSET":
                        return HandleCreateLabelSet(command.WorkspaceDir, command.GameId, command.SetName);

                    case "DELETELABELSET":
                        return HandleDeleteLabelSet(command.WorkspaceDir, command.GameId, command.SetName);

                    case "ADDLABELENTRY":
                        return HandleAddLabelEntry(command.WorkspaceDir, command.GameId, command.SetName, command.LabelEntry);

                    case "DELETELABELENTRY":
                        return HandleDeleteLabelEntry(command.WorkspaceDir, command.GameId, command.SetName, command.LabelEntryAddress, command.Address);

                    case "IMPORTLABELSET":
                        return HandleImportLabelSet(command.WorkspaceDir, command.GameId, command.SetName, command.Data);

                    case "EXPORTLABELSET":
                        return HandleExportLabelSet(command.WorkspaceDir, command.GameId, command.SetName);

                    case "RESOLVESTRINGS":
                        return HandleResolveStrings();

                    case "RESOLVELINKEDLIST":
                        if (command.Address is null)
                            return CreateErrorResponse("Address is required for RESOLVELINKEDLIST");
                        return HandleResolveLinkedList(command.Address.Value, command.MemberOffset ?? 0);

                    case "MC_LIST":
                        return HandleMcList(command.McPath);

                    case "MC_EXPORT":
                        return HandleMcExport(command.McPath, command.SaveName, command.ExportDir);

                    case "MC_IMPORT":
                        return HandleMcImport(command.McPath, command.ImportDir, command.McOverwrite ?? false);

                    case "MC_DELETE":
                        return HandleMcDelete(command.McPath, command.SaveName);

                    case "MC_FORMAT":
                        return HandleMcFormat(command.McPath);

                    case "MC_READ_FILE":
                    case "MC_READFILE":
                        return HandleMcReadFile(command.McPath, command.SaveName, command.McFileName);

                    case "HDD_LIST":
                        return HandleHddList(command.HddPath);

                    case "HDD_LIST_FILES":
                        return HandleHddListFiles(command.HddPath, command.PartitionId);

                    case "HDD_EXPORT":
                        return HandleHddExport(command.HddPath, command.PartitionId, command.FileName, command.ExportDir);

                    case "HDD_IMPORT":
                        return HandleHddImport(command.HddPath, command.PartitionId, command.ImportDir, command.McOverwrite ?? false);

                    case "HDD_DELETE":
                        return HandleHddDelete(command.HddPath, command.PartitionId);

                    case "HDD_CREATE":
                        return HandleHddCreate(command.HddPath, command.HddSizeGb ?? 40);

                    case "ISO_LIST_FILES":
                        return HandleIsoListFiles(command.IsoPath);

                    case "ISO_READ_FILE":
                        return HandleIsoReadFile(command.IsoPath, command.FilePath, command.OutputPath);

                    case "ISO_GET_INFO":
                        return HandleIsoGetInfo(command.IsoPath);

                    case "PING":
                        return CreateSuccessResponse("PONG");

                    case "PCSX2STATUS":
                        return HandlePcsx2Status();

                    case "PCSX2GAMEINFO":
                        return HandlePcsx2GameInfo();

                    case "RESET":
                        return HandleReset();

                    case "STEPINTO":
                    case "STEP_INTO":
                        return HandleStepInto();

                    case "STEPOVER":
                    case "STEP_OVER":
                        return HandleStepOver();

                    case "STEPOUT":
                    case "STEP_OUT":
                        return HandleStepOut();

                    case "GETFPS":
                    case "GET_FPS":
                        return HandleGetFps();

                    case "SETLIMITER":
                    case "SET_LIMITER":
                    case "SETLIMITERMODE":
                        return HandleSetLimiterMode(command.LimiterMode);

                    case "FRAMEADVANCE":
                    case "FRAME_ADVANCE":
                        return HandleFrameAdvance(command.FrameCount);

                    case "LISTBP":
                    case "LIST_BP":
                    case "LISTBREAKPOINTS":
                        return HandleListBreakpoints(command.CpuSel);

                    case "GETSYMBOL":
                    case "GET_SYMBOL":
                        return HandleGetSymbol(command.Address);

                    case "GETSYMBOLBYNAME":
                    case "GET_SYMBOL_BY_NAME":
                        return HandleGetSymbolByName(command.SymbolName);

                    case "GETSTACK":
                    case "GET_STACK":
                    case "CALLSTACK":
                        return HandleGetStack();

                    case "GETMODULES":
                    case "GET_MODULES":
                    case "IOPMODULES":
                        return HandleGetModules();

                    case "GETREGISTER":
                    case "GET_REGISTER":
                    case "PINEGETREGISTER":
                        return HandleGetRegister(command.CpuSel, command.RegisterCategory, command.RegisterIndex);

                    case "PINESETREG":
                    case "PINE_SETREG":
                    case "PINESETREGISTER":
                        return HandlePineSetRegister(command.CpuSel, command.RegisterCategory, command.RegisterIndex, command.RegisterValue);

                    case "LISTFUNCTIONS":
                    case "LIST_FUNCTIONS":
                    case "PINEFUNCTIONS":
                        return HandleListFunctions(command.Offset, command.MaxCount);

                    case "LISTGLOBALS":
                    case "LIST_GLOBALS":
                    case "PINEGLOBALS":
                        return HandleListGlobals(command.Offset, command.MaxCount);

                    case "GETLOCALS":
                    case "GET_LOCALS":
                        return HandleGetLocals(command.Address);

                    case "SAVESTATETOFILE":
                    case "SAVE_STATE_FILE":
                    case "SAVESTATEFILE":
                        return HandleSaveStateFile(command.FilePath);

                    case "LOADSTATEFROMFILE":
                    case "LOAD_STATE_FILE":
                    case "LOADSTATEFILE":
                        return HandleLoadStateFile(command.FilePath);

                    case "EXTRACTP2S":
                    case "EXTRACT_P2S":
                        return HandleExtractP2s(command.FilePath);

                    case "PINEVERSION":
                    case "PINE_VERSION":
                        return HandlePineVersion();

                    case "PINESAVESTATE":
                    case "PINE_SAVESTATE":
                    case "PINE_SAVE_STATE":
                        return HandlePineSaveStateSlot(command.StateSlot);

                    case "PINELOADSTATE":
                    case "PINE_LOADSTATE":
                    case "PINE_LOAD_STATE":
                        return HandlePineLoadStateSlot(command.StateSlot);

                    case "READMEMREGION":
                    case "READ_MEM_REGION":
                    case "READMEM_REGION":
                        return HandleReadMemRegion(
                            command.Address    ?? 0,
                            command.EndAddress ?? 0,
                            command.SearchPattern,
                            command.Region);

                    case "SCANPATTERN":
                    case "SCAN_PATTERN":
                        return HandleScanPattern(
                            command.Address    ?? 0,
                            command.EndAddress ?? 0,
                            command.SearchPattern,
                            command.Region);

                    case "TRACECALLS":
                    case "TRACE_CALLS":
                        return HandleTraceCalls(
                            command.EndAddress,
                            command.MaxInstructions ?? 10_000);

                    case "RUNUNTILPC":
                    case "RUN_UNTIL_PC":
                        return HandleRunUntilPc(command.TargetPc, command.TimeoutMs ?? 10_000);

                    // ── PINE hardware watchpoints ─────────────────────────────
                    case "PINEADDWATCH":
                    case "PINE_ADD_WATCH":
                        return HandlePineAddWatch(
                            command.CpuSel,
                            command.Address,
                            command.EndAddress,
                            command.WatchCond);

                    case "PINEREMOVEWATCH":
                    case "PINE_REMOVE_WATCH":
                        return HandlePineRemoveWatch(
                            command.CpuSel,
                            command.Address,
                            command.EndAddress);

                    case "PINELISTWATCHES":
                    case "PINE_LIST_WATCHES":
                        return HandlePineListWatches(command.CpuSel);

                    case "PINECLEARALLWATCHES":
                    case "PINE_CLEAR_ALL_WATCHES":
                        return HandlePineClearAllWatches();

                    case "PINESAVESCREENSHOT":
                    case "PINE_SAVE_SCREENSHOT":
                        return HandlePineSaveScreenshot();

                    // ── Database query (read-only) ────────────────────────────
                    case "QUERYDB":
                    case "QUERY_DB":
                        return HandleQueryDb(command.SqlQuery);

                    // ── Navigation history ────────────────────────────────────
                    case "GETNAVHISTORY":
                    case "GET_NAV_HISTORY":
                        return HandleGetNavHistory();

                    case "RECORDNAV":
                    case "RECORD_NAV":
                        return HandleRecordNav(command.Address, command.NavSource ?? "user");

                    case "CLEARNAVHISTORY":
                    case "CLEAR_NAV_HISTORY":
                        return HandleClearNavHistory();

                    // ── Navigate VS Code disasm view ──────────────────────────
                    case "NAVIGATETO":
                    case "NAVIGATE_TO":
                        return HandleNavigateTo(command.Address);

                    // ── Snapshot address history ──────────────────────────────
                    case "GETADDRESSHISTORY":
                    case "GET_ADDRESS_HISTORY":
                        return HandleGetAddressHistory(command.Address, command.SnapshotDir);

                    // ── Settings (DB-backed) ──────────────────────────────────
                    case "GETSETTING":
                    case "GET_SETTING":
                        return HandleGetSetting(command.SettingKey);

                    case "SETSETTING":
                    case "SET_SETTING":
                        return HandleSetSetting(command.SettingKey, command.SettingValue);

                    case "GETALLSETTINGS":
                    case "GET_ALL_SETTINGS":
                        return HandleGetAllSettings(command.SettingPrefix);

                    case "DELETESETTING":
                    case "DELETE_SETTING":
                        return HandleDeleteSetting(command.SettingKey);

                    // ── Snapshots (DB-backed) ─────────────────────────────────
                    case "GETSNAPSHOTS":
                    case "GET_SNAPSHOTS":
                        return HandleGetSnapshots(command.IsoName);

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
