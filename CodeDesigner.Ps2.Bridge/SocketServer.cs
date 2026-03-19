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
using CodeDesigner.Ps2.CheatConverter;
using CodeDesigner.Ps2.MemoryCardManager;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.CheatEngine;
using CodeDesigner.Ps2.Emulator;
using CodeDesigner.Ps2.Emulator.Hardware;
using CodeDesigner.Ps2.Emulator.IOP;
using CodeDesigner.Ps2.CheatPorter;
using CheatPorterLib = CodeDesigner.Ps2.CheatPorter.CheatPorter;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Socket server for communication with VSCode extension
    /// </summary>
    public class SocketServer
    {
        private TcpListener? _listener;
        private bool _isRunning;
        private readonly int _port;
        private CancellationTokenSource? _cancellationTokenSource;

        // ── Compiled regexes for CDS compilation (avoid per-line recompilation) ──
        /// <summary>Matches a bare label definition line: "Name:"  (identifier + colon, optional trailing whitespace).</summary>
        private static readonly System.Text.RegularExpressions.Regex CdsLabelDefRegex =
            new(@"^([A-Za-z_][A-Za-z0-9_]*):\s*$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Matches a label reference inside an instruction operand: ":Name" or ":scope.name".
        /// Dots are allowed to support pointer-manager scoped labels (e.g. <c>:seal.health</c>).
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex CdsLabelRefRegex =
            new(@":([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Matches an absolute-address label reference: "@Name" or "@scope.name".
        /// Dots are allowed to support pointer-manager scoped labels (e.g. <c>@seal.health</c>).
        /// Replaced with the raw hex address of the label (e.g. 0x80010018),
        /// suitable for use with jump instructions or absolute address operands.
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex CdsAbsLabelRefRegex =
            new(@"@([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Symbol/label database loaded from ps2MemMap.json at construction time.
        /// Key = virtual address (uint), Value = (Label, optional Comment).
        /// </summary>
        private readonly Dictionary<uint, (string Label, string? Comment)> _labels = new();

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
        {
            _port = port;
            LoadLabels();
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

                                // Process command and generate response
                                var response = ProcessCommand(message);
                                var responseBytes = Encoding.UTF8.GetBytes(response + "\n");
                                await stream.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken);
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
                    Console.WriteLine("Client disconnected.");
                    ClientDisconnected?.Invoke(this, EventArgs.Empty);
                }
            }
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

                    case "LISTDISCELFS":
                        return HandleListDiscElfs(command.IsoPath);

                    case "RECOMPILE":
                        return HandleRecompile(command.IsoPath, command.ElfName, command.OutputDirectory);

                    case "CONVERTCHEAT":
                        return HandleConvertCheat(command.Data, command.Format, command.Direction);

                    case "PORTCHEATS":
                        return HandlePortCheats(command.Data, command.SourceDumpPath, command.TargetDumpPath);

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

                    default:
                        // Echo for unknown commands
                        return $"Echo: {message}";
                }
            }
            catch (JsonException)
            {
                // If not JSON, just echo back
                return $"Echo: {message}";
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Error processing command: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle COMPILECDS command — compiles a .cds (Code Designer Script) assembly file
        /// into PS2 GameShark / Action Replay cheat codes.
        ///
        /// Supported directives:
        ///   address 0xXXXXXXXX  or  address $XXXXXXXX  — set the current target address.
        ///   hexcode 0xXXXXXXXX  or  hexcode $XXXXXXXX  — emit a raw 32-bit word.
        ///   print   "string"                            — ignored (no instruction output).
        ///
        /// Supported pseudo-instruction:
        ///   setreg  reg, value   — expands to lui reg, upper16 + ori reg, reg, lower16.
        ///
        /// Label definitions (Name:) and branch label references (:Name) are supported
        /// via a two-pass approach.
        ///
        /// Registers may be written with or without the $ prefix ($v0 or v0).
        /// Numeric immediates may be decimal (1), 0x-hex (0x0001), or $-hex ($0001).
        ///
        /// Each instruction is emitted as a GameShark type-2 code: 2AAAAAAA VVVVVVVV
        /// </summary>
        private string HandleCompileCds(string? sourceCode, List<LabelEntry>? externalLabels = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceCode))
                {
                    return CreateErrorResponse("No source code provided");
                }

                // Split preserving original line numbers (1-based).
                var rawLines = sourceCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                // ── Pass 1: collect label → address mapping ───────────────────────
                // Pre-populate with all emulator/server labels so CDS source can use them
                // directly. CDS-defined labels (Name:) take precedence if names collide.
                var labelAddresses = new Dictionary<string, uint>(StringComparer.Ordinal);
                foreach (var kv in _labels)
                    labelAddresses[kv.Value.Label] = kv.Key;

                // Merge pointer-manager labels (pre-resolved by the frontend).
                // These carry dotted scope names (e.g. "seal.health") and are injected
                // here so that CDS source can reference them as :seal.health or @seal.health.
                // Server labels take precedence on name conflicts.
                if (externalLabels != null)
                {
                    foreach (var entry in externalLabels)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Label) || string.IsNullOrWhiteSpace(entry.Address))
                            continue;
                        var addrStr = entry.Address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            ? entry.Address[2..]
                            : entry.Address;
                        if (uint.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber,
                                null, out uint addr) && !labelAddresses.ContainsKey(entry.Label))
                            labelAddresses[entry.Label] = addr;
                    }
                }

                uint scanAddress   = 0;
                bool scanInBlock   = false;

                foreach (var rawLine in rawLines)
                {
                    var raw = rawLine.Trim();

                    // Block comment tracking (/* ... */)
                    if (scanInBlock)
                    {
                        if (raw.Contains("*/")) scanInBlock = false;
                        continue;
                    }
                    if (raw.StartsWith("/*", StringComparison.Ordinal))
                    {
                        if (!raw.Contains("*/")) scanInBlock = true;
                        continue;
                    }

                    var scan = StripCdsComments(raw);
                    if (string.IsNullOrWhiteSpace(scan)) { continue; }

                    if (scan.StartsWith("address", StringComparison.OrdinalIgnoreCase))
                    {
                        var p = scan.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length >= 2)
                        {
                            if (TryParseCdsUInt32(p[1], out uint addr))
                                scanAddress = addr;
                            else if (labelAddresses.TryGetValue(StripLabelPrefix(p[1]), out addr))
                                scanAddress = addr;
                        }
                        continue;
                    }
                    if (scan.StartsWith("hexcode", StringComparison.OrdinalIgnoreCase)) { scanAddress += 4; continue; }
                    if (scan.StartsWith("print",   StringComparison.OrdinalIgnoreCase)) { continue; }
                    if (scan.StartsWith("setreg",  StringComparison.OrdinalIgnoreCase)) { scanAddress += 8; continue; }

                    // Label definition: "Name:" at start of line (identifier + colon, optional trailing ws)
                    var labelMatch = CdsLabelDefRegex.Match(scan);
                    if (labelMatch.Success)
                    {
                        labelAddresses[labelMatch.Groups[1].Value] = scanAddress;
                        continue;
                    }

                    // Everything else is a regular instruction (4 bytes)
                    scanAddress += 4;
                }

                // ── Pass 2: assemble ──────────────────────────────────────────────
                var results        = new List<InstructionResult>();
                uint currentAddress = 0;
                bool inBlockComment = false;

                for (int i = 0; i < rawLines.Length; i++)
                {
                    int lineNumber = i + 1;
                    var raw2 = rawLines[i].Trim();

                    // Block comment tracking (/* ... */)
                    if (inBlockComment)
                    {
                        if (raw2.Contains("*/")) inBlockComment = false;
                        continue;
                    }
                    if (raw2.StartsWith("/*", StringComparison.Ordinal))
                    {
                        if (!raw2.Contains("*/")) inBlockComment = true;
                        continue;
                    }

                    var trimmed    = StripCdsComments(raw2);
                    if (string.IsNullOrWhiteSpace(trimmed)) { continue; }

                    // ── address directive ────────────────────────────────────────────
                    if (trimmed.StartsWith("address", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            var addrToken = StripLabelPrefix(parts[1]);
                            if (TryParseCdsUInt32(parts[1], out uint addr))
                            {
                                currentAddress = addr;
                            }
                            else if (labelAddresses.TryGetValue(addrToken, out addr))
                            {
                                currentAddress = addr;
                            }
                            else
                            {
                                results.Add(new InstructionResult
                                {
                                    Assembly   = trimmed,
                                    Error      = $"Invalid address value '{parts[1]}'",
                                    Success    = false,
                                    LineNumber = lineNumber
                                });
                            }
                        }
                        else
                        {
                            results.Add(new InstructionResult
                            {
                                Assembly   = trimmed,
                                Error      = "address directive requires a value (e.g. address 0x20123456 or address EE_BIOS_PUTS)",
                                Success    = false,
                                LineNumber = lineNumber
                            });
                        }
                        continue;
                    }

                    // ── hexcode directive (raw 32-bit word) ──────────────────────────
                    if (trimmed.StartsWith("hexcode", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            if (TryParseCdsUInt32(parts[1], out uint raw))
                            {
                                results.Add(new InstructionResult
                                {
                                    Assembly   = trimmed,
                                    Hex        = FormatGameSharkCode(currentAddress, raw),
                                    Success    = true,
                                    LineNumber = lineNumber
                                });
                                currentAddress += 4;
                            }
                            else
                            {
                                results.Add(new InstructionResult
                                {
                                    Assembly   = trimmed,
                                    Error      = $"Invalid hex value '{parts[1]}'",
                                    Success    = false,
                                    LineNumber = lineNumber
                                });
                            }
                        }
                        else
                        {
                            results.Add(new InstructionResult
                            {
                                Assembly   = trimmed,
                                Error      = "hexcode directive requires a value (e.g. hexcode 0xABCD1234 or hexcode $ABCD1234)",
                                Success    = false,
                                LineNumber = lineNumber
                            });
                        }
                        continue;
                    }

                    // ── print directive — no instruction output ──────────────────────
                    if (trimmed.StartsWith("print", StringComparison.OrdinalIgnoreCase)) { continue; }

                    // ── label definition — no instruction output ─────────────────────
                    if (CdsLabelDefRegex.IsMatch(trimmed)) { continue; }

                    // ── setreg pseudo-instruction ────────────────────────────────────
                    // setreg reg, value  →  lui reg, upper16 ; ori reg, reg, lower16
                    if (trimmed.StartsWith("setreg", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 3)
                        {
                            results.Add(new InstructionResult
                            {
                                Assembly   = trimmed,
                                Error      = "setreg requires two operands: setreg reg, value",
                                Success    = false,
                                LineNumber = lineNumber
                            });
                            continue;
                        }

                        string regName = parts[1];
                        string valStr  = parts[2];

                        if (!TryParseCdsUInt32(valStr, out uint setVal) &&
                            !labelAddresses.TryGetValue(StripLabelPrefix(valStr), out setVal))
                        {
                            results.Add(new InstructionResult
                            {
                                Assembly   = trimmed,
                                Error      = $"Invalid setreg value '{valStr}' — expected a number or a known label",
                                Success    = false,
                                LineNumber = lineNumber
                            });
                            continue;
                        }

                        uint upper16 = (setVal >> 16) & 0xFFFF;
                        uint lower16 = setVal & 0xFFFF;

                        try
                        {
                            // lui reg, upper16
                            uint luiEncoded = MipsAssembler.Assemble($"lui {regName}, 0x{upper16:X4}");
                            results.Add(new InstructionResult
                            {
                                Assembly   = $"lui {regName}, 0x{upper16:X4}",
                                Hex        = FormatGameSharkCode(currentAddress, luiEncoded),
                                Success    = true,
                                LineNumber = lineNumber
                            });
                            currentAddress += 4;

                            // ori reg, reg, lower16
                            uint oriEncoded = MipsAssembler.Assemble($"ori {regName}, {regName}, 0x{lower16:X4}");
                            results.Add(new InstructionResult
                            {
                                Assembly   = $"ori {regName}, {regName}, 0x{lower16:X4}",
                                Hex        = FormatGameSharkCode(currentAddress, oriEncoded),
                                Success    = true,
                                LineNumber = lineNumber
                            });
                            currentAddress += 4;
                        }
                        catch (Exception ex)
                        {
                            results.Add(new InstructionResult
                            {
                                Assembly   = trimmed,
                                Error      = ex.Message,
                                Success    = false,
                                LineNumber = lineNumber
                            });
                        }
                        continue;
                    }

                    // ── MIPS instruction (resolve label references first) ─────────────
                    try
                    {
                        // Determine if this is a J-type jump instruction (j/jal) vs a branch.
                        // Jumps encode an absolute 26-bit word address; branches use a
                        // PC-relative 16-bit word offset.
                        string instMnemonic = trimmed
                            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0]
                            .ToLowerInvariant();
                        bool isJumpInstr = instMnemonic == "j" || instMnemonic == "jal";

                        // Replace :LabelName with the computed target value.
                        // • Jumps (j/jal)   → absolute byte address (0xXXXXXXXX)
                        // • Branches (beq…) → PC-relative word offset (signed integer)
                        string resolved = CdsLabelRefRegex.Replace(
                            trimmed,
                            m =>
                            {
                                var name = m.Groups[1].Value;
                                if (!labelAddresses.TryGetValue(name, out uint labelAddr))
                                    throw new ArgumentException(
                                        $"Undefined label '{name}' in '{trimmed}' (line {lineNumber})");
                                if (isJumpInstr)
                                    return $"0x{labelAddr:X8}";
                                // Branch offset = (label_addr - (current_addr + 4)) / 4
                                // Use long arithmetic to avoid overflow when addresses exceed 0x7FFFFFFF
                                long branchOff = ((long)labelAddr - (long)(currentAddress + 4)) / 4;
                                return ((int)branchOff).ToString();
                            });

                        // Replace @LabelName with the raw absolute hex address.
                        // Use this for jump/call targets and any operand that requires an
                        // absolute address (e.g. j @EE_BIOS_PUTS, jal @InitHeap,
                        // setreg $a0, @EE_SCRATCHPAD).
                        resolved = CdsAbsLabelRefRegex.Replace(
                            resolved,
                            m =>
                            {
                                var name = m.Groups[1].Value;
                                if (!labelAddresses.TryGetValue(name, out uint labelAddr))
                                    throw new ArgumentException(
                                        $"Undefined label '@{name}' in '{trimmed}' (line {lineNumber})");
                                return $"0x{labelAddr:X8}";
                            });

                        uint encoded  = MipsAssembler.Assemble(resolved);
                        results.Add(new InstructionResult
                        {
                            Assembly   = trimmed,
                            Hex        = FormatGameSharkCode(currentAddress, encoded),
                            Success    = true,
                            LineNumber = lineNumber
                        });
                        currentAddress += 4;
                    }
                    catch (Exception ex)
                    {
                        results.Add(new InstructionResult
                        {
                            Assembly   = trimmed,
                            Error      = ex.Message,
                            Success    = false,
                            LineNumber = lineNumber
                        });
                    }
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "COMPILECDS",
                    Results = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Compile error: {ex.Message}");
            }
        }

        /// <summary>
        /// Strip inline comments from a CDS source line, finding the earliest
        /// occurrence of //, ;, or # and cutting from there.
        /// Lines that are entirely a comment (start with the marker) are left empty.
        /// </summary>
        private static string StripCdsComments(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) { return line; }

            // Skip entirely-comment lines fast
            if (line.StartsWith("//", StringComparison.Ordinal)
                || line.StartsWith(";",  StringComparison.Ordinal)
                || line.StartsWith("#",  StringComparison.Ordinal))
            {
                return string.Empty;
            }

            int cutAt = int.MaxValue;
            foreach (var marker in new[] { "//", ";", "#" })
            {
                int ci = line.IndexOf(marker, StringComparison.Ordinal);
                if (ci > 0 && ci < cutAt) { cutAt = ci; }
            }

            return cutAt != int.MaxValue ? line[..cutAt].TrimEnd() : line;
        }

        /// <summary>
        /// Strip an optional leading '@' from a CDS label token, so both
        /// "EE_BIOS_PUTS" and "@EE_BIOS_PUTS" resolve to the same label name.
        /// </summary>
        private static string StripLabelPrefix(string token) =>
            token.Length > 0 && token[0] == '@' ? token[1..] : token;

        /// <summary>
        /// Parse a CDS numeric literal that may be prefixed with $, 0x, or neither.
        ///   $XXXX  → hexadecimal (CDS / PS2 assembler style)
        ///   0xXXXX → hexadecimal
        ///   DDDDD  → decimal
        /// Returns false if the string cannot be parsed.
        /// </summary>
        private static bool TryParseCdsUInt32(string s, out uint value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) { return false; }
            s = s.Trim();
            try
            {
                if (s.StartsWith("$", StringComparison.Ordinal))
                {
                    value = Convert.ToUInt32(s[1..], 16);
                    return true;
                }
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    value = Convert.ToUInt32(s, 16);
                    return true;
                }
                value = uint.Parse(s);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Format a 32-bit address/value pair as a PS2 GameShark type-2 (32-bit write) code.
        /// Format: 2AAAAAAA VVVVVVVV  (address low 28 bits, value 32 bits).
        /// </summary>
        private static string FormatGameSharkCode(uint address, uint value)
        {
            // Type-2 prefix (0x20000000) ORed with the low 28 bits of the target address.
            uint codeAddr = 0x20000000u | (address & 0x0FFFFFFFu);
            return $"{codeAddr:X8} {value:X8}";
        }

        /// <summary>
        /// Handle ASSEMBLE command
        /// </summary>
        private string HandleAssemble(string? asmCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(asmCode))
                {
                    return CreateErrorResponse("No assembly code provided");
                }

                // Split into lines and assemble each
                var lines = asmCode.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                var results = new List<InstructionResult>();

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    
                    // Skip empty lines and comments
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    {
                        continue;
                    }

                    try
                    {
                        uint encoded = MipsAssembler.Assemble(trimmed);
                        results.Add(new InstructionResult
                        {
                            Assembly = trimmed,
                            Hex = $"0x{encoded:X8}",
                            Success = true
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new InstructionResult
                        {
                            Assembly = trimmed,
                            Error = ex.Message,
                            Success = false
                        });
                    }
                }

                var response = new CommandResponse
                {
                    Success = true,
                    Command = "ASSEMBLE",
                    Results = results
                };

                return JsonSerializer.Serialize(response);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Assemble error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle DISASSEMBLE command
        /// </summary>
        private string HandleDisassemble(string? hexCode, uint? startAddress = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hexCode))
                {
                    return CreateErrorResponse("No hex code provided");
                }

                // Split into lines and disassemble each hex value
                var lines = hexCode.Split(new[] { '\n', '\r', ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                var results = new List<InstructionResult>();
                var instructionList = new List<(uint instruction, uint address)>();
                
                // Track current PC for branch calculations
                uint currentPc = startAddress ?? 0;

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    
                    // Skip empty lines and comments
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    {
                        continue;
                    }

                    try
                    {
                        // Parse hex value (support 0x prefix or raw hex)
                        string hexValue = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) 
                            ? trimmed.Substring(2) 
                            : trimmed;
                        
                        uint instruction = Convert.ToUInt32(hexValue, 16);
                        // Pass PC to disassembler for proper branch target calculation
                        string disasm = MipsDisassembler.Disassemble(instruction, currentPc);

                        // For unknown instructions the disassembler returns ".word 0xXXXXXXXX  # Unknown ..."
                        // Per the problem statement, the instruction column should just say "unknown"
                        // and the details should be placed in the comment column instead.
                        string assembly = disasm;
                        string? unknownComment = null;
                        if (disasm.StartsWith(".word ", StringComparison.Ordinal))
                        {
                            assembly = "unknown";
                            unknownComment = disasm.Substring(".word ".Length);
                        }

                        results.Add(new InstructionResult
                        {
                            Hex = $"0x{instruction:X8}",
                            Assembly = assembly,
                            Success = true,
                            Comment = unknownComment
                        });
                        
                        // Store instruction for analysis
                        instructionList.Add((instruction, currentPc));
                        
                        // Increment PC by 4 bytes (one instruction)
                        currentPc += 4;
                    }
                    catch (Exception ex)
                    {
                        results.Add(new InstructionResult
                        {
                            Hex = trimmed,
                            Error = ex.Message,
                            Success = false
                        });
                        
                        // Still increment PC even on error to maintain alignment
                        currentPc += 4;
                    }
                }

                // Run analyzer using the instruction's built-in Analyze() method
                var comments = MipsAnalyzer.AnalyzeInstructions(
                    instructionList, 
                    startAddress ?? 0,
                    executeInstructions: true,  // Execute instructions to track register state
                    lookupLabel: addr => _labels.TryGetValue(addr, out var e) ? e.Label : null);

                // Apply comments and labels to results
                for (int i = 0; i < results.Count; i++)
                {
                    if (results[i].Success && instructionList.Count > i)
                    {
                        var addr = instructionList[i].address;
                        if (comments.TryGetValue(addr, out var comment))
                        {
                            results[i].Comment = comment;
                        }

                        // Apply label from the database
                        if (_labels.TryGetValue(addr, out var labelEntry))
                        {
                            results[i].Label = labelEntry.Label;
                            // Use the label's comment only when the analyzer produced nothing
                            if (string.IsNullOrEmpty(results[i].Comment) && labelEntry.Comment != null)
                                results[i].Comment = labelEntry.Comment;
                        }
                    }
                }

                var response = new CommandResponse
                {
                    Success = true,
                    Command = "DISASSEMBLE",
                    Results = results
                };

                return JsonSerializer.Serialize(response);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Disassemble error: {ex.Message}");
            }
        }

        private const int MaxReadMemLength = 65536;

        // Thresholds used by SCANFLOATS / GETFLOATS to filter plausible game values.
        private const float FloatScanMinAbs = 0.001f;
        private const float FloatScanMaxAbs = 1_000_000f;

        // ── Address-translation constants for HandleReadMem / scan helpers ────

        // EE: uncached direct-mapped user window (PS2 kernel TLB maps RAM here).
        // 0x20000000–0x21FFFFFF → physical EE RAM 0x00000000–0x01FFFFFF.
        // EeUncachedRamEnd (0x22000000) is the exclusive upper bound.
        private const uint EeUncachedRamBase = 0x20000000u;
        private const uint EeUncachedRamEnd  = 0x22000000u;   // exclusive

        // IOP: the EE VTLB exposes IOP RAM at 0x1C000000–0x1DFFFFFF (PCSX2 vtlb.cpp).
        // 0x1C000000–0x1DFFFFFF → physical IOP RAM 0x00000000–0x001FFFFF (wrap at RamSize).
        // EeIopMirrorEnd (0x1E000000) is the exclusive upper bound.
        private const uint EeIopMirrorBase = 0x1C000000u;
        private const uint EeIopMirrorEnd  = 0x1E000000u;     // exclusive

        /// <summary>
        /// Translate EE addresses from the UI/bridge into a PINE-friendly virtual address.
        /// The VSCode UI uses physical bases for EE RAM/BIOS, while PCSX2 PINE expects
        /// EE virtual addresses. We map physical and uncached mirrors into KSEG0,
        /// while IOP mirror addresses are mapped to KSEG1 to avoid PCSX2 TLB lookups.
        /// </summary>
        private static uint TranslatePineAddress(uint address, bool useIop)
        {
            // IOP mirror: map to KSEG1 (0xBC000000+) to bypass TLB in PCSX2.
            // Reading the IOP region as a user-space virtual address triggers TLB exceptions
            // in PCSX2. KSEG1 (0xA0000000–0xBFFFFFFF) bypasses TLB entirely:
            //   virtual 0xBC000000 = physical 0x1C000000 = EE IOP mirror base.
            // Addresses may arrive in two formats; both are normalised to the same IOP offset:
            //   • EE mirror format: 0x1C000000+offset → offset = address - EeIopMirrorBase
            //   • Direct IOP physical: 0x00000000+offset → offset = address
            // Both yield the same KSEG1 result: 0xBC000000 + iopOffset.
            if (useIop || (address >= EeIopMirrorBase && address < EeIopMirrorEnd))
            {
                uint iopOffset = address >= EeIopMirrorBase ? address - EeIopMirrorBase : address;
                return 0xBC000000u + iopOffset;
            }

            // Preserve scratchpad address (hardware-fixed EE VTLB page, no TLB needed).
            if (address >= 0x70000000u &&
                address < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                return address;

            // If already KSEG0/KSEG1, keep it.
            if (address >= 0x80000000u && address <= 0xBFFFFFFFu)
                return address;

            // Uncached user mirror → physical.
            if (address >= EeUncachedRamBase && address < EeUncachedRamEnd)
                address -= EeUncachedRamBase;

            // Physical RAM/BIOS → KSEG0 cached alias for PINE.
            if (address < 0x20000000u)
                return address | 0x80000000u;

            return address;
        }

        /// <summary>
        /// Handle READMEM command — reads raw bytes from the live emulator memory.
        /// In PINE mode the bytes are read from PCSX2 via the PINE IPC socket.
        /// </summary>
        /// <param name="address">Start virtual address (EE or IOP address space).</param>
        /// <param name="length">Number of bytes to read (capped at MaxReadMemLength).</param>
        /// <param name="region">"ee" for EE bus, "iop" for IOP bus (IOP only in internal mode).</param>
        private string HandleReadMem(uint address, int length, string? region)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");

            if (length <= 0 || length > MaxReadMemLength)
                return CreateErrorResponse($"Invalid length {length} — must be 1–{MaxReadMemLength} bytes");

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);

            try
            {
                byte[] bytes;

                if (IsPineMode)
                {
                    // ── PINE path: forward read to PCSX2 ─────────────────────
                    // PINE addresses the EE virtual memory map directly.
                    // IOP region is not separately accessible; callers should use
                    // EE physical addresses in that case.
                    uint pineAddr = TranslatePineAddress(address, useIop);
                    bytes = PineClient!.ReadBytes(pineAddr, length);
                }
                else
                {
                    // ── Internal emulator path ────────────────────────────────
                    bytes = new byte[length];

                    for (int i = 0; i < length; i++)
                    {
                        uint vaddr = address + (uint)i;

                        if (useIop)
                        {
                            uint physAddr = IopMemoryBus.TranslateAddress(vaddr);
                            if (physAddr >= EeIopMirrorBase && physAddr < EeIopMirrorEnd)
                                physAddr -= EeIopMirrorBase;
                            bytes[i] = physAddr < (uint)IopMemoryBus.RamSize
                                ? Scheduler!.IOP.Bus.Ram[physAddr]
                                : (byte)0;
                        }
                        else
                        {
                            uint physAddr;
                            if (vaddr >= 0x80000000u && vaddr < 0xC0000000u)
                                physAddr = vaddr & 0x1FFFFFFFu;
                            else if (vaddr >= EeUncachedRamBase && vaddr < EeUncachedRamEnd)
                                physAddr = vaddr - EeUncachedRamBase;
                            else
                                physAddr = vaddr;

                            if (physAddr < (uint)PS2MemoryBus.MainRamSize)
                                bytes[i] = Scheduler!.EE.Bus.Ram[physAddr];
                            else if (physAddr >= PS2MemoryBus.BiosRomPhysBase &&
                                     physAddr <  PS2MemoryBus.BiosRomPhysBase + (uint)PS2MemoryBus.BiosRomSize)
                                bytes[i] = Scheduler!.EE.Bus.Bios[physAddr - PS2MemoryBus.BiosRomPhysBase];
                            else if (vaddr >= 0x70000000u &&
                                     vaddr <  0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                                bytes[i] = Scheduler!.EE.Bus.Scratchpad[vaddr - 0x70000000u];
                            else
                                bytes[i] = 0;
                        }
                    }
                }

                var response = new CommandResponse
                {
                    Success = true,
                    Command = "READMEM",
                    Data    = Convert.ToBase64String(bytes)
                };
                return JsonSerializer.Serialize(response);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ReadMem error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle SNAPSHOT command — dumps all memory regions to .bin files in the
        /// given output directory and returns the list of written files.
        /// </summary>
        private string HandleSnapshot(string? outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                return CreateErrorResponse("OutputPath is required for SNAPSHOT");
            if (IsPineMode)
            {
                try
                {
                    Directory.CreateDirectory(outputPath);
                    var regions = new[]
                    {
                        (Label: "EE Main RAM",  File: "EEmem.bin",     Base: 0x00000000u,                 Size: (int)PS2MemoryBus.MainRamSize,    UseIop: false),
                        (Label: "BIOS ROM",     File: "BIOSrom.bin",   Base: PS2MemoryBus.BiosRomPhysBase, Size: (int)PS2MemoryBus.BiosRomSize,    UseIop: false),
                        (Label: "Scratchpad",   File: "Scratchpad.bin",Base: 0x70000000u,                 Size: (int)PS2MemoryBus.ScratchpadSize, UseIop: false),
                        (Label: "IOP RAM",      File: "IOPmem.bin",    Base: EeIopMirrorBase,             Size: (int)IopMemoryBus.RamSize,        UseIop: true),
                    };
                    var written = new List<SnapshotRegion>();
                    foreach (var (label, file, baseAddr, size, useIop) in regions)
                    {
                        uint pineAddr = TranslatePineAddress(baseAddr, useIop);
                        byte[] bytes  = PineClient!.ReadBytes(pineAddr, size);
                        string filePath = Path.Combine(outputPath, file);
                        File.WriteAllBytes(filePath, bytes);
                        written.Add(new SnapshotRegion
                        {
                            Label       = label,
                            File        = filePath,
                            BaseAddress = baseAddr,
                            Size        = bytes.Length,
                        });
                    }
                    var pineResp = new CommandResponse
                    {
                        Success = true,
                        Command = "SNAPSHOT",
                        Regions = written,
                    };
                    return JsonSerializer.Serialize(pineResp);
                }
                catch (Exception ex)
                {
                    return CreateErrorResponse("PINE Snapshot error: " + ex.Message);
                }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running - no scheduler attached");
            try
            {
                Directory.CreateDirectory(outputPath);
                var regions = new[]
                {
                    (Label: "EE Main RAM",  File: "EEmem.bin",    Bytes: Scheduler.EE.Bus.Ram,        Base: 0x00000000u),
                    (Label: "BIOS ROM",     File: "BIOSrom.bin",   Bytes: Scheduler.EE.Bus.Bios,       Base: PS2MemoryBus.BiosRomPhysBase),
                    (Label: "Scratchpad",   File: "Scratchpad.bin",Bytes: Scheduler.EE.Bus.Scratchpad, Base: 0x70000000u),
                    (Label: "IOP RAM",      File: "IOPmem.bin",    Bytes: Scheduler.IOP.Bus.Ram,       Base: EeIopMirrorBase),
                };
                var written = new List<SnapshotRegion>();
                foreach (var (label, file, bytes, baseAddr) in regions)
                {
                    string filePath = Path.Combine(outputPath, file);
                    File.WriteAllBytes(filePath, bytes);
                    written.Add(new SnapshotRegion
                    {
                        Label       = label,
                        File        = filePath,
                        BaseAddress = baseAddr,
                        Size        = bytes.Length,
                    });
                }
                var response = new CommandResponse
                {
                    Success = true,
                    Command = "SNAPSHOT",
                    Regions = written,
                };
                return JsonSerializer.Serialize(response);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse("Snapshot error: " + ex.Message);
            }
        }

        /// <summary>
        /// Handle LOADSNAPSHOT command — restores all memory regions from .bin files
        /// previously written by a SNAPSHOT command.  The emulator must be paused
        /// before calling this; if it is not paused it will be paused automatically
        /// and left paused after the restore completes.
        /// </summary>
        private string HandleLoadSnapshot(string? inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                return CreateErrorResponse("InputPath is required for LOADSNAPSHOT");
            if (!Directory.Exists(inputPath))
                return CreateErrorResponse("Snapshot directory not found: " + inputPath);
            if (IsPineMode)
            {
                try
                {
                    // Pause the emulator while we swap memory so it does not race with execution.
                    try { PineClient!.Pause(); } catch { /* ignore pause errors */ }
                    var regions = new[]
                    {
                        (File: "EEmem.bin",     Label: "EE Main RAM",  MaxSize: (int)PS2MemoryBus.MainRamSize,    Base: 0x00000000u,                 UseIop: false),
                        (File: "BIOSrom.bin",   Label: "BIOS ROM",     MaxSize: (int)PS2MemoryBus.BiosRomSize,    Base: PS2MemoryBus.BiosRomPhysBase, UseIop: false),
                        (File: "Scratchpad.bin",Label: "Scratchpad",   MaxSize: (int)PS2MemoryBus.ScratchpadSize, Base: 0x70000000u,                 UseIop: false),
                        (File: "IOPmem.bin",    Label: "IOP RAM",      MaxSize: (int)IopMemoryBus.RamSize,        Base: EeIopMirrorBase,             UseIop: true),
                    };
                    var loaded = new List<SnapshotRegion>();
                    foreach (var (file, label, maxSize, baseAddr, useIop) in regions)
                    {
                        string filePath = Path.Combine(inputPath, file);
                        if (!File.Exists(filePath))
                            continue;
                        byte[] src = File.ReadAllBytes(filePath);
                        int count  = Math.Min(src.Length, maxSize);
                        if (count <= 0)
                            continue;
                        if (count != src.Length)
                            Array.Resize(ref src, count);
                        uint pineAddr = TranslatePineAddress(baseAddr, useIop);
                        PineClient!.WriteBytes(pineAddr, src);
                        loaded.Add(new SnapshotRegion
                        {
                            Label       = label,
                            File        = filePath,
                            BaseAddress = baseAddr,
                            Size        = count,
                        });
                    }
                    var pineResp = new CommandResponse
                    {
                        Success = true,
                        Command = "LOADSNAPSHOT",
                        Regions = loaded,
                    };
                    return JsonSerializer.Serialize(pineResp);
                }
                catch (Exception ex)
                {
                    return CreateErrorResponse("PINE LoadSnapshot error: " + ex.Message);
                }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running - no scheduler attached");
            // Pause the emulator while we swap memory so it does not race with
            // the run-loop reading/writing the same arrays.
            bool wasPaused = Scheduler.Paused;
            Scheduler.Paused = true;
            try
            {
                var regions = new[]
                {
                    (File: "EEmem.bin",     Label: "EE Main RAM",  Bytes: Scheduler.EE.Bus.Ram,        MaxSize: PS2MemoryBus.MainRamSize,        Base: 0x00000000u),
                    (File: "BIOSrom.bin",   Label: "BIOS ROM",     Bytes: Scheduler.EE.Bus.Bios,       MaxSize: Scheduler.EE.Bus.Bios.Length,   Base: PS2MemoryBus.BiosRomPhysBase),
                    (File: "Scratchpad.bin",Label: "Scratchpad",   Bytes: Scheduler.EE.Bus.Scratchpad, MaxSize: PS2MemoryBus.ScratchpadSize,     Base: 0x70000000u),
                    (File: "IOPmem.bin",    Label: "IOP RAM",      Bytes: Scheduler.IOP.Bus.Ram,       MaxSize: Scheduler.IOP.Bus.Ram.Length,   Base: EeIopMirrorBase),
                };
                var loaded = new List<SnapshotRegion>();
                foreach (var (file, label, dest, maxSize, baseAddr) in regions)
                {
                    string filePath = Path.Combine(inputPath, file);
                    if (!File.Exists(filePath))
                        continue;
                    byte[] src = File.ReadAllBytes(filePath);
                    int count = Math.Min(src.Length, maxSize);
                    Array.Copy(src, dest, count);
                    loaded.Add(new SnapshotRegion
                    {
                        Label       = label,
                        File        = filePath,
                        BaseAddress = baseAddr,
                        Size        = count,
                    });
                }
                // Leave emulator paused so the caller can inspect state or resume
                // explicitly.
                var response = new CommandResponse
                {
                    Success = true,
                    Command = "LOADSNAPSHOT",
                    Regions = loaded,
                };
                return JsonSerializer.Serialize(response);
            }
            catch (Exception ex)
            {
                if (!wasPaused) Scheduler.Paused = false;
                return CreateErrorResponse("LoadSnapshot error: " + ex.Message);
            }
        }private string HandlePause()
        {
            if (IsPineMode)
            {
                try   { PineClient!.Pause(); return CreateSuccessResponse("Paused"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE Pause error: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            Scheduler.Paused = true;
            return CreateSuccessResponse("Paused");
        }

        /// <summary>
        /// Handle RESUME command — resumes emulator execution.
        /// In PINE mode, resumes PCSX2 via the extended PINE protocol (MsgResume).
        /// </summary>
        private string HandleResume()
        {
            if (IsPineMode)
            {
                try   { PineClient!.Resume(); return CreateSuccessResponse("Resumed"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE Resume error: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            // If the emulator is paused at a breakpoint, execute the breakpoint
            // instruction once before releasing the run gate.  This advances PC
            // past the breakpoint address so the same breakpoint does not
            // immediately re-fire the moment execution resumes.
            bool wasAtBreakpoint = Scheduler.LastBreakpointHit.HasValue;
            if (wasAtBreakpoint)
                Scheduler.StepOne();

            Scheduler.Paused = false;
            return CreateSuccessResponse("Resumed");
        }

        /// <summary>
        /// Handle STEP command — executes exactly one EE instruction while
        /// keeping the emulator paused, then returns the new PC state.
        /// In PINE mode, steps PCSX2 via the extended PINE protocol (MsgStep).
        /// </summary>
        private string HandleStep()
        {
            if (IsPineMode)
            {
                try
                {
                    uint newPc = PineClient!.Step();
                    var pine_resp = new CommandResponse
                    {
                        Success = true,
                        Command = "STEP",
                        Paused  = true,
                        PC      = newPc,
                    };
                    return JsonSerializer.Serialize(pine_resp);
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE Step error: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            if (!Scheduler.Paused)
                return CreateErrorResponse("Emulator must be paused to single-step");

            // Capture the PC and instruction word *before* stepping so we can
            // analyze the instruction with the actual pre-execution register state.
            uint prePc = (uint)(Scheduler.EE.Cpu.PC & 0xFFFFFFFFUL);
            uint instrWord = 0;
            string? analysisComment = null;
            try
            {
                instrWord = Scheduler.EE.Bus.ReadWord(prePc);
                // Snapshot register state before execution; Bus/COP0 are not needed for analysis.
                var cpuSnap = Scheduler.EE.Cpu.CreateAnalysisSnapshot();
                analysisComment = MipsAnalyzer.AnalyzeSingleInstruction(instrWord, prePc, cpuSnap,
                    Scheduler.EE.Bus,
                    addr => _labels.TryGetValue(addr, out var e) ? e.Label : null);
            }
            catch { /* If we can't read the instruction, skip analysis */ }

            Scheduler.StepOne();

            // Build the response with the PC update and the live analysis.
            uint pc = NormalizePcToKseg0((uint)(Scheduler.EE.Cpu.PC & 0xFFFFFFFFUL));
            uint? bpHit = Scheduler.LastBreakpointHit.HasValue
                ? NormalizePcToKseg0(Scheduler.LastBreakpointHit.Value)
                : (uint?)null;
            var response = new CommandResponse
            {
                Success = true,
                Command = "STEP",
                Paused  = Scheduler.Paused,
                PC      = pc,
                BreakpointHitAddress = bpHit,
            };

            // Attach analysis if we produced a non-empty comment.
            if (!string.IsNullOrEmpty(analysisComment))
            {
                uint normalizedPrePc = NormalizePcToKseg0(prePc);
                response.InstructionAnalysis = new Dictionary<string, string>
                {
                    [normalizedPrePc.ToString("X8")] = analysisComment
                };
            }

            return JsonSerializer.Serialize(response);
        }

        /// <summary>
        /// Normalize a virtual EE address to its KSEG0 representation.
        /// KSEG1 (0xA0000000–0xBFFFFFFF, uncached) is remapped to the equivalent
        /// KSEG0 address (0x80000000–0x9FFFFFFF, cached) so that all PC values
        /// presented to the client are consistent and use the 0x8xxxxxxx /
        /// 0x9xxxxxxx form regardless of which segment the CPU is executing from.
        /// </summary>
        private static uint NormalizePcToKseg0(uint vaddr)
        {
            // KSEG1: bits[31:29] == 101 (0xA0000000–0xBFFFFFFF)
            if ((vaddr & 0xE0000000u) == 0xA0000000u)
                return (vaddr & 0x1FFFFFFFu) | 0x80000000u;
            return vaddr;
        }

        /// <summary>
        /// Normalize a virtual EE address to its physical representation for
        /// breakpoint storage.  KSEG0 (0x80000000–0x9FFFFFFF) and KSEG1
        /// (0xA0000000–0xBFFFFFFF) both strip the top 3 bits to yield the
        /// physical address.  Physical addresses (from the live disassembly panels
        /// which display EE Main RAM at 0x00000000 and BIOS ROM at 0x1FC00000) and
        /// KUSEG user-mode addresses pass through unchanged.
        /// Using physical addresses as the canonical form ensures that a breakpoint
        /// set from the disassembly panel fires correctly regardless of which
        /// virtual alias the CPU uses at runtime.
        /// </summary>
        private static uint NormalizeBreakpointToPhysical(uint vaddr)
        {
            // KSEG0 (0x80000000–0x9FFFFFFF) or KSEG1 (0xA0000000–0xBFFFFFFF)
            if (vaddr >= 0x80000000u && vaddr <= 0xBFFFFFFFu)
                return vaddr & 0x1FFFFFFFu;
            return vaddr;
        }

        /// <summary>
        /// Handle SETBP command — adds a breakpoint at the given EE address.
        /// In PINE mode, registers the breakpoint via the extended PINE protocol (MsgSetBreakpoint).
        /// </summary>
        private string HandleSetBreakpoint(uint? address)
        {
            if (address == null)
                return CreateErrorResponse("BreakpointAddress is required for SETBP");
            uint normalized = NormalizePcToKseg0(address.Value);
            if (IsPineMode)
            {
                try   { PineClient!.SetBreakpoint(normalized); return CreateSuccessResponse($"Breakpoint set at 0x{normalized:X8}"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE SetBreakpoint error: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            
            normalized = NormalizeBreakpointToPhysical(address.Value);
            Scheduler.Breakpoints.Add(normalized);
            return CreateSuccessResponse($"Breakpoint set at 0x{normalized:X8}");
        }

        /// <summary>
        /// Handle CLEARBP command — removes the breakpoint at the given EE address.
        /// In PINE mode, clears the breakpoint via the extended PINE protocol (MsgClearBreakpoint).
        /// </summary>
        private string HandleClearBreakpoint(uint? address)
        {
            if (address == null)
                return CreateErrorResponse("BreakpointAddress is required for CLEARBP");
            uint normalized = NormalizePcToKseg0(address.Value);
            if (IsPineMode)
            {
                try   { PineClient!.ClearBreakpoint(normalized); return CreateSuccessResponse($"Breakpoint cleared at 0x{normalized:X8}"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE ClearBreakpoint error: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            normalized = NormalizeBreakpointToPhysical(address.Value);
            Scheduler.Breakpoints.Remove(normalized);
            return CreateSuccessResponse($"Breakpoint cleared at 0x{normalized:X8}");
        }

        /// <summary>
        /// Handle CLEARBP_ALL command — removes all breakpoints.
        /// In PINE mode, clears all breakpoints via the extended PINE protocol (MsgClearAllBreakpoints).
        /// </summary>
        private string HandleClearAllBreakpoints()
        {
            if (IsPineMode)
            {
                try   { PineClient!.ClearAllBreakpoints(); return CreateSuccessResponse("All breakpoints cleared"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE ClearAllBreakpoints error: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            Scheduler.Breakpoints.Clear();
            return CreateSuccessResponse("All breakpoints cleared");
        }

        /// <summary>
        /// Handle GETPC command — returns current EE PC and paused/breakpoint state.
        /// In PINE mode, reads PC from PCSX2 via the extended PINE protocol (MsgGetProgramCounter).
        /// The PC is normalized to KSEG0 (0x80000000–0x9FFFFFFF) so the client
        /// always sees a consistent address regardless of whether the CPU executed
        /// the instruction via the KSEG0 or KSEG1 virtual-address window.
        /// </summary>
        private string HandleGetPC()
        {
            if (IsPineMode)
            {
                try
                {
                    uint pc = PineClient!.GetProgramCounter(out bool paused);
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = true,
                        Command = "GETPC",
                        Paused  = paused,
                        PC      = pc,
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE GetPC error: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            uint normalizedPc = NormalizePcToKseg0((uint)(Scheduler.EE.Cpu.PC & 0xFFFFFFFFUL));
            uint? bpHit = Scheduler.LastBreakpointHit.HasValue
                ? NormalizePcToKseg0(Scheduler.LastBreakpointHit.Value)
                : (uint?)null;
            var response = new CommandResponse
            {
                Success = true,
                Command = "GETPC",
                Paused  = Scheduler.Paused,
                PC      = normalizedPc,
                BreakpointHitAddress = bpHit,
            };
            return JsonSerializer.Serialize(response);
        }

        /// <summary>
        /// Handle GETREGS command — returns the full EE CPU register state including
        /// GPR, all 32 COP0 registers, FPR/FCR (COP1), VU0 float/integer registers,
        /// and key GS privileged registers.
        /// In PINE mode, returns the register snapshot from PCSX2 via the extended
        /// PINE protocol (MsgGetRegisters — GPRs, PC, HI, LO only).
        /// </summary>
        private string HandleGetRegisters()
        {
            if (IsPineMode)
            {
                try
                {
                    var snap = PineClient!.GetRegisters();
                    var pineGpr     = snap.Gpr.ToList();
                    var pineGprLo32 = snap.GprLo32.ToList();
                    var pineGprHi32 = snap.GprHi32.ToList();
                    var pineGprUpLo = snap.GprUpLo32.ToList();
                    var pineGprUpHi = snap.GprUpHi32.ToList();
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success   = true,
                        Command   = "GETREGS",
                        GPR       = pineGpr,
                        GPR_Lo32  = pineGprLo32,
                        GPR_Hi32  = pineGprHi32,
                        GPR_UpLo32 = pineGprUpLo,
                        GPR_UpHi32 = pineGprUpHi,
                        GPR_PC    = snap.PC,
                        GPR_HI    = snap.HI,
                        GPR_LO    = snap.LO,
                        GPR_HI1   = snap.HI1,
                        GPR_LO1   = snap.LO1,
                        GPR_SA    = (int)snap.SA,
                        COP0_Status = snap.Cop0.Length > 12 ? snap.Cop0[12] : 0,
                        COP0_Cause  = snap.Cop0.Length > 13 ? snap.Cop0[13] : 0,
                        COP0_EPC    = snap.Cop0.Length > 14 ? snap.Cop0[14] : 0,
                        COP0_Count  = snap.Cop0.Length > 9  ? snap.Cop0[9]  : 0,
                        COP0_Regs   = snap.Cop0.Select(v => (ulong)v).ToList(),
                        FPR         = snap.Fpr.ToList(),
                        FPR_ACC     = snap.FprAcc,
                        FCR0        = snap.Fcr0,
                        FCR31       = snap.Fcr31,
                        VU0F_X      = snap.Vu0fX.ToList(),
                        VU0F_Y      = snap.Vu0fY.ToList(),
                        VU0F_Z      = snap.Vu0fZ.ToList(),
                        VU0F_W      = snap.Vu0fW.ToList(),
                        VU0F_ACC_X  = snap.Vu0AccX,
                        VU0F_ACC_Y  = snap.Vu0AccY,
                        VU0F_ACC_Z  = snap.Vu0AccZ,
                        VU0F_ACC_W  = snap.Vu0AccW,
                        VU0F_Q      = snap.Vu0Q,
                        VU0F_P      = snap.Vu0P,
                        VU0F_I      = snap.Vu0I,
                        VU0I        = snap.Vu0i.ToList(),
                        VU0_Status  = snap.Vu0i.Length > 16 ? snap.Vu0i[16] : 0,
                        VU0_MAC     = (ushort)(snap.Vu0i.Length > 17 ? snap.Vu0i[17] : 0),
                        VU0_Clip    = snap.Vu0i.Length > 18 ? snap.Vu0i[18] : 0,
                        GS_Priv     = snap.GsPriv.ToList(),
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE GetRegisters error: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            var cpu  = Scheduler.EE.Cpu;
            var cop0 = cpu.COP0;
            var cop1 = cpu.COP1;
            var vu0  = cpu.VU0;
            var gs   = Scheduler.EE.Bus.Gs;

            // GPR (32 × 64-bit lower lane)
            var gpr = new List<ulong>(32);
            for (int i = 0; i < 32; i++)
                gpr.Add(cpu.RegsLo[i]);

            // GPR precise 32-bit arrays (avoids JavaScript floating-point precision loss
            // when representing 64-bit values > Number.MAX_SAFE_INTEGER).
            // Each 128-bit GPR is split into four uint32 fields:
            //   Lo32 / Hi32  = lower and upper halves of RegsLo[i]  (integer lane)
            //   UpLo32 / UpHi32 = lower and upper halves of RegsHi[i] (upper 64-bit lane)
            var gprLo32  = new List<uint>(32);
            var gprHi32  = new List<uint>(32);
            var gprUpLo32 = new List<uint>(32);
            var gprUpHi32 = new List<uint>(32);
            for (int i = 0; i < 32; i++)
            {
                gprLo32.Add((uint)(cpu.RegsLo[i] & 0xFFFFFFFFUL));
                gprHi32.Add((uint)(cpu.RegsLo[i] >> 32));
                gprUpLo32.Add((uint)(cpu.RegsHi[i] & 0xFFFFFFFFUL));
                gprUpHi32.Add((uint)(cpu.RegsHi[i] >> 32));
            }

            // COP0 – all 32 registers (read as 64-bit to preserve high bits where relevant)
            var cop0Regs = new List<ulong>(32);
            for (int i = 0; i < 32; i++)
                cop0Regs.Add(cop0.ReadRegister64(i));

            // COP1 FPR – 32 × 32-bit floats, transmitted as raw IEEE-754 bit patterns (uint)
            // so the client can display them as both float and hex without precision loss.
            var fpr = new List<uint>(32);
            for (int i = 0; i < 32; i++)
                fpr.Add(BitConverter.SingleToUInt32Bits(cpu.FPR[i]));
            uint faccBits = BitConverter.SingleToUInt32Bits(cpu.FACC);

            // FCR – only FCR0 (implementation) and FCR31 (FCSR) are architecturally
            // defined on the PS2 EE; the rest are reserved/unused.
            uint fcr0  = cop1.ReadControlRegister(0);
            uint fcr31 = cop1.ReadControlRegister(31);

            // VU0 float registers – 32 × 4-component (x,y,z,w), each component as uint bits
            var vu0fX = new List<uint>(32);
            var vu0fY = new List<uint>(32);
            var vu0fZ = new List<uint>(32);
            var vu0fW = new List<uint>(32);
            for (int i = 0; i < 32; i++)
            {
                var v = vu0.VF[i];
                vu0fX.Add(BitConverter.SingleToUInt32Bits(v.X));
                vu0fY.Add(BitConverter.SingleToUInt32Bits(v.Y));
                vu0fZ.Add(BitConverter.SingleToUInt32Bits(v.Z));
                vu0fW.Add(BitConverter.SingleToUInt32Bits(v.W));
            }
            // VU0 ACC register
            uint vu0accX = BitConverter.SingleToUInt32Bits(vu0.ACC.X);
            uint vu0accY = BitConverter.SingleToUInt32Bits(vu0.ACC.Y);
            uint vu0accZ = BitConverter.SingleToUInt32Bits(vu0.ACC.Z);
            uint vu0accW = BitConverter.SingleToUInt32Bits(vu0.ACC.W);
            uint vu0Q = BitConverter.SingleToUInt32Bits(vu0.Q);
            uint vu0P = BitConverter.SingleToUInt32Bits(vu0.P);
            uint vu0I = BitConverter.SingleToUInt32Bits(vu0.I);

            // VU0 integer registers (vi00–vi15) plus the 16 COP2 control registers (vi16–vi31).
            // This matches PCSX2's COP2_REG_CTL layout:
            //   [0-15]  vi00–vi15  (integer registers, 16-bit each)
            //   [16]    Status     vu0.StatusFlags
            //   [17]    MACflag    vu0.MACFlags
            //   [18]    ClipFlag   vu0.ClipFlags
            //   [19]    c2c19      (reserved, always 0)
            //   [20]    R          vu0.RngState (LFSR state, 0x3F800000 | mantissa)
            //   [21]    I          vu0.I (as raw IEEE-754 bits)
            //   [22]    Q          vu0.Q (as raw IEEE-754 bits)
            //   [23-25] c2c23–c2c25 (reserved, always 0)
            //   [26]    TPC        vu0.PC (trigger program counter)
            //   [27]    CMSAR0     0 (not implemented)
            //   [28]    FBRST      0 (not implemented)
            //   [29]    VPU-STAT   IsRunning ? 1 : 0
            //   [30-31] c2c30–CMSAR1 (reserved, always 0)
            var vu0i = new List<uint>(32);
            for (int i = 0; i < 16; i++)
                vu0i.Add(vu0.VI[i]);
            vu0i.Add(vu0.StatusFlags);                                       // [16] Status
            vu0i.Add(vu0.MACFlags);                                          // [17] MACflag
            vu0i.Add(vu0.ClipFlags);                                         // [18] ClipFlag
            vu0i.Add(0);                                                     // [19] c2c19
            vu0i.Add(vu0.RngState);                                          // [20] R (LFSR state)
            vu0i.Add(BitConverter.SingleToUInt32Bits(vu0.I));                // [21] I
            vu0i.Add(BitConverter.SingleToUInt32Bits(vu0.Q));                // [22] Q
            vu0i.Add(0); vu0i.Add(0); vu0i.Add(0);                         // [23-25] c2c23–c2c25
            vu0i.Add(vu0.PC);                                                // [26] TPC
            vu0i.Add(0); vu0i.Add(0);                                        // [27-28] CMSAR0/FBRST
            vu0i.Add(vu0.IsRunning ? 1u : 0u);                              // [29] VPU-STAT
            vu0i.Add(0); vu0i.Add(0);                                        // [30-31] c2c30/CMSAR1

            // GS privileged registers
            var gsPriv = new List<ulong>
            {
                gs.ReadPriv64(GS.PRIV_PMODE),
                gs.ReadPriv64(GS.PRIV_SMODE1),
                gs.ReadPriv64(GS.PRIV_SMODE2),
                gs.ReadPriv64(GS.PRIV_SRFSH),
                gs.ReadPriv64(GS.PRIV_SYNCH1),
                gs.ReadPriv64(GS.PRIV_SYNCH2),
                gs.ReadPriv64(GS.PRIV_SYNCV),
                gs.ReadPriv64(GS.PRIV_DISPFB1),
                gs.ReadPriv64(GS.PRIV_DISPLAY1),
                gs.ReadPriv64(GS.PRIV_DISPFB2),
                gs.ReadPriv64(GS.PRIV_DISPLAY2),
                gs.ReadPriv64(GS.PRIV_EXTBUF),
                gs.ReadPriv64(GS.PRIV_EXTDATA),
                gs.ReadPriv64(GS.PRIV_EXTWRITE),
                gs.ReadPriv64(GS.PRIV_BGCOLOR),
                gs.ReadPriv64(GS.PRIV_CSR),
                gs.ReadPriv64(GS.PRIV_IMR),
                gs.ReadPriv64(GS.PRIV_BUSDIR),
                gs.ReadPriv64(GS.PRIV_SIGLBLID),
            };

            var response = new CommandResponse
            {
                Success  = true,
                Command  = "GETREGS",
                GPR      = gpr,
                GPR_Lo32  = gprLo32,
                GPR_Hi32  = gprHi32,
                GPR_UpLo32 = gprUpLo32,
                GPR_UpHi32 = gprUpHi32,
                GPR_PC   = NormalizePcToKseg0((uint)(cpu.PC & 0xFFFFFFFFUL)),
                GPR_HI   = cpu.HI,
                GPR_LO   = cpu.LO,
                GPR_HI1  = cpu.HI1,
                GPR_LO1  = cpu.LO1,
                GPR_SA   = cpu.SA,
                // Legacy COP0 fields (kept for backward compatibility)
                COP0_Status = cop0.ReadRegister32(12),
                COP0_Cause  = cop0.ReadRegister32(13),
                COP0_EPC    = cop0.ReadRegister64(14),
                COP0_Count  = cop0.ReadRegister32(9),
                // Full register sets
                COP0_Regs   = cop0Regs,
                FPR         = fpr,
                FPR_ACC     = faccBits,
                FCR0        = fcr0,
                FCR31       = fcr31,
                VU0F_X      = vu0fX,
                VU0F_Y      = vu0fY,
                VU0F_Z      = vu0fZ,
                VU0F_W      = vu0fW,
                VU0F_ACC_X  = vu0accX,
                VU0F_ACC_Y  = vu0accY,
                VU0F_ACC_Z  = vu0accZ,
                VU0F_ACC_W  = vu0accW,
                VU0F_Q      = vu0Q,
                VU0F_P      = vu0P,
                VU0F_I      = vu0I,
                VU0I        = vu0i,
                VU0_Status  = vu0.StatusFlags,
                VU0_MAC     = vu0.MACFlags,
                VU0_Clip    = vu0.ClipFlags,
                GS_Priv     = gsPriv,
            };
            return JsonSerializer.Serialize(response);
        }

        // ── PINE-specific commands ────────────────────────────────────────────

        /// <summary>
        /// Handle PCSX2STATUS — queries the PCSX2 emulator status via PINE (MsgStatus).
        /// Returns Running, Paused, or Shutdown.
        /// </summary>
        private string HandlePcsx2Status()
        {
            if (PineClient == null)
                return CreateErrorResponse("PCSX2STATUS requires --pcsx2 mode");
            try
            {
                var status = PineClient.GetStatus();
                return CreateSuccessResponse(status.ToString());
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"PINE Status error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle PCSX2GAMEINFO — queries the running game title, serial ID, UUID,
        /// and version from PCSX2 via PINE (MsgTitle, MsgID, MsgUUID, MsgGameVersion).
        /// </summary>
        private string HandlePcsx2GameInfo()
        {
            if (PineClient == null)
                return CreateErrorResponse("PCSX2GAMEINFO requires --pcsx2 mode");
            try
            {
                string title   = PineClient.GetGameTitle();
                string id      = PineClient.GetGameID();
                string uuid    = PineClient.GetGameUUID();
                string version = PineClient.GetGameVersion();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "PCSX2GAMEINFO",
                    Message = $"Title={title} ID={id} UUID={uuid} Version={version}",
                    Data    = JsonSerializer.Serialize(new
                    {
                        Title   = title,
                        ID      = id,
                        UUID    = uuid,
                        Version = version,
                    })
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"PINE GameInfo error: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the current running thread from CPU state and attempts to walk
        /// the PS2 EE OS thread control block list in EE RAM.  Each TCB is 128
        /// bytes; status byte at offset 0: 0x01=running, 0x02=ready, 0x04=wait,
        /// 0x08=suspend, 0x10=dormant.  The thread table is located by reading
        /// the kernel's internal data starting at physical 0x00000000.
        /// </summary>
        private string HandleReset()
        {
            if (IsPineMode)
            {
                try   { PineClient!.Reset(); return CreateSuccessResponse("Reset"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE Reset failed: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            Scheduler.Reset();
            return CreateSuccessResponse("Reset");
        }

        private string HandleStepInto()
        {
            if (IsPineMode)
            {
                try   { PineClient!.StepInto(); return CreateSuccessResponse("StepInto initiated"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE StepInto failed: {ex.Message}"); }
            }
            // Non-PINE fallback: behave like a single step (same as STEP command).
            return HandleStep();
        }

        private string HandleStepOver()
        {
            if (IsPineMode)
            {
                try   { PineClient!.StepOver(); return CreateSuccessResponse("StepOver initiated"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE StepOver failed: {ex.Message}"); }
            }
            // Non-PINE fallback: behave like a single step (same as STEP command).
            return HandleStep();
        }

        private string HandleStepOut()
        {
            if (IsPineMode)
            {
                try   { PineClient!.StepOut(); return CreateSuccessResponse("StepOut initiated"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE StepOut failed: {ex.Message}"); }
            }
            // Non-PINE fallback: behave like a single step (same as STEP command).
            return HandleStep();
        }

        private string HandleGetFps()
        {
            if (IsPineMode)
            {
                try
                {
                    float fps = PineClient!.GetFPS();
                    return JsonSerializer.Serialize(new CommandResponse { Success = true, Command = "GETFPS", FPS = fps });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE GetFPS failed: {ex.Message}"); }
            }
            return CreateErrorResponse("GETFPS requires PINE mode");
        }

        private string HandleSetLimiterMode(byte? mode)
        {
            if (mode == null) return CreateErrorResponse("LimiterMode is required (0=Nominal, 1=Turbo, 2=Slomo, 3=Unlimited)");
            if (IsPineMode)
            {
                try   { PineClient!.SetLimiterMode(mode.Value); return CreateSuccessResponse($"Limiter mode set to {mode}"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE SetLimiterMode failed: {ex.Message}"); }
            }
            return CreateErrorResponse("SETLIMITER requires PINE mode");
        }

        private string HandleFrameAdvance(byte? numFrames)
        {
            if (numFrames == null || numFrames == 0) return CreateErrorResponse("FrameCount is required and must be ≥ 1");
            if (IsPineMode)
            {
                try   { PineClient!.FrameAdvance(numFrames.Value); return CreateSuccessResponse($"Frame advance by {numFrames} initiated"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE FrameAdvance failed: {ex.Message}"); }
            }
            return CreateErrorResponse("FRAMEADVANCE requires PINE mode");
        }

        private string HandleListBreakpoints(byte? cpuSel)
        {
            if (IsPineMode)
            {
                try
                {
                    var bps = PineClient!.ListBreakpoints(cpuSel ?? 0xFF);
                    var entries = bps.Select(b => new PineBreakpointInfo
                    {
                        Address = $"0x{b.Address:X8}",
                        Enabled = b.Enabled,
                        Cpu     = b.Cpu == 0 ? "EE" : "IOP",
                    }).ToList();
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success     = true,
                        Command     = "LISTBP",
                        Breakpoints = entries,
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE ListBreakpoints failed: {ex.Message}"); }
            }
            // Internal emulator — just use the existing breakpoint set
            return CreateErrorResponse("LISTBP requires PINE mode (use SETBP / CLEARBP for the internal emulator)");
        }

        private string HandleGetSymbol(uint? address)
        {
            if (address == null) return CreateErrorResponse("Address is required for GETSYMBOL");
            if (IsPineMode)
            {
                try
                {
                    var sym = PineClient!.GetSymbol(address.Value);
                    if (sym == null) return CreateErrorResponse($"No symbol at 0x{address.Value:X8}");
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success       = true,
                        Command       = "GETSYMBOL",
                        SymbolAddress = $"0x{sym.Value.StartAddress:X8}",
                        SymbolName    = sym.Value.Name,
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE GetSymbol failed: {ex.Message}"); }
            }
            return CreateErrorResponse("GETSYMBOL requires PINE mode");
        }

        private string HandleGetSymbolByName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return CreateErrorResponse("SymbolName is required for GETSYMBOLBYNAME");
            if (IsPineMode)
            {
                try
                {
                    var sym = PineClient!.GetSymbolByName(name);
                    if (sym == null) return CreateErrorResponse($"Symbol '{name}' not found");
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success       = true,
                        Command       = "GETSYMBOLBYNAME",
                        SymbolAddress = $"0x{sym.Value.Address:X8}",
                        SymbolName    = name,
                        SymbolSize    = sym.Value.Size,
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE GetSymbolByName failed: {ex.Message}"); }
            }
            return CreateErrorResponse("GETSYMBOLBYNAME requires PINE mode");
        }

        private string HandleGetStack()
        {
            if (IsPineMode)
            {
                try
                {
                    var frames = PineClient!.GetStack();
                    var entries = frames.Select(f => new PineStackFrameInfo
                    {
                        Entry     = $"0x{f.Entry:X8}",
                        PC        = $"0x{f.PC:X8}",
                        SP        = $"0x{f.SP:X8}",
                        StackSize = f.StackSize,
                    }).ToList();
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success     = true,
                        Command     = "GETSTACK",
                        StackFrames = entries,
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE GetStack failed: {ex.Message}"); }
            }
            return CreateErrorResponse("GETSTACK requires PINE mode");
        }

        private string HandleGetModules()
        {
            if (IsPineMode)
            {
                try
                {
                    var modules = PineClient!.GetModules();
                    var entries = modules.Select(m => new IopModuleInfo
                    {
                        Name      = m.Name,
                        Version   = m.Version,
                        TextAddr  = $"0x{m.TextAddr:X8}",
                        Entry     = $"0x{m.Entry:X8}",
                        GP        = $"0x{m.GP:X8}",
                        TextSize  = m.TextSize,
                        DataSize  = m.DataSize,
                        BssSize   = m.BssSize,
                    }).ToList();
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = true,
                        Command = "GETMODULES",
                        Modules = entries,
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE GetModules failed: {ex.Message}"); }
            }
            return CreateErrorResponse("GETMODULES requires PINE mode");
        }

        private string HandleGetRegister(byte? cpuSel, byte? cat, int? regIndex)
        {
            if (cpuSel == null) return CreateErrorResponse("CpuSel is required for GETREGISTER (0=EE, 1=IOP)");
            if (cat == null)    return CreateErrorResponse("RegisterCategory is required for GETREGISTER");
            if (regIndex == null) return CreateErrorResponse("RegisterIndex is required for GETREGISTER");
            if (IsPineMode)
            {
                try
                {
                    byte[] val = PineClient!.GetRegister(cpuSel.Value, cat.Value, (byte)regIndex.Value);
                    string hex = BitConverter.ToString(val).Replace("-", "");
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success       = true,
                        Command       = "GETREGISTER",
                        RegisterValue = hex,
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE GetRegister failed: {ex.Message}"); }
            }
            return CreateErrorResponse("GETREGISTER requires PINE mode");
        }

        private string HandlePineSetRegister(byte? cpuSel, byte? cat, int? regIndex, string? regValue)
        {
            if (cpuSel == null) return CreateErrorResponse("CpuSel is required for PINESETREG (0=EE, 1=IOP)");
            if (cat == null)    return CreateErrorResponse("RegisterCategory is required for PINESETREG");
            if (regIndex == null) return CreateErrorResponse("RegisterIndex is required for PINESETREG");
            if (string.IsNullOrEmpty(regValue)) return CreateErrorResponse("RegisterValue (32 hex chars = 128-bit LE) is required for PINESETREG");
            if (!IsPineMode) return CreateErrorResponse("PINESETREG requires PINE mode; use SETREG for the internal emulator");

            // Accept 32 hex chars (128-bit) or up to 16 hex chars (64-bit, zero-extended)
            string hex = regValue.Replace("0x", "").Replace("0X", "").Trim();
            if (hex.Length > 32 || hex.Length == 0 || hex.Length % 2 != 0)
                return CreateErrorResponse("RegisterValue must be an even-length hex string of up to 32 characters (128 bits)");
            byte[] val128 = new byte[16];
            byte[] parsed = new byte[hex.Length / 2];
            for (int i = 0; i < parsed.Length; i++)
                parsed[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            // Copy little-endian (pad high bytes with zero)
            Buffer.BlockCopy(parsed, 0, val128, 0, parsed.Length);

            try
            {
                PineClient!.SetRegister(cpuSel.Value, cat.Value, (byte)regIndex.Value, val128);
                return CreateSuccessResponse($"Register set");
            }
            catch (Exception ex) { return CreateErrorResponse($"PINE SetRegister failed: {ex.Message}"); }
        }

        private string HandleListFunctions(uint? offset, ushort? maxCount)
        {
            if (IsPineMode)
            {
                try
                {
                    var (total, symbols) = PineClient!.ListFunctions(offset ?? 0, maxCount ?? 256);
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success       = true,
                        Command       = "LISTFUNCTIONS",
                        TotalSymbols  = total,
                        PineSymbols   = symbols.Select(s => new PineSymbolInfo
                        {
                            Address = $"0x{s.Address:X8}",
                            Size    = s.Size,
                            Name    = s.Name,
                        }).ToList(),
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE ListFunctions failed: {ex.Message}"); }
            }
            return CreateErrorResponse("LISTFUNCTIONS requires PINE mode");
        }

        private string HandleListGlobals(uint? offset, ushort? maxCount)
        {
            if (IsPineMode)
            {
                try
                {
                    var (total, symbols) = PineClient!.ListGlobals(offset ?? 0, maxCount ?? 256);
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success       = true,
                        Command       = "LISTGLOBALS",
                        TotalSymbols  = total,
                        PineSymbols   = symbols.Select(s => new PineSymbolInfo
                        {
                            Address = $"0x{s.Address:X8}",
                            Size    = s.Size,
                            Name    = s.Name,
                        }).ToList(),
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE ListGlobals failed: {ex.Message}"); }
            }
            return CreateErrorResponse("LISTGLOBALS requires PINE mode");
        }

        private string HandleGetLocals(uint? address)
        {
            if (address == null) return CreateErrorResponse("Address is required for GETLOCALS");
            if (IsPineMode)
            {
                try
                {
                    var locals = PineClient!.GetLocals(address.Value);
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success    = true,
                        Command    = "GETLOCALS",
                        LocalVars  = locals.Select(l => new PineLocalInfo
                        {
                            StorageType = l.StorageType switch { 0 => "global", 1 => "register", 2 => "stack", _ => $"0x{l.StorageType:X2}" },
                            Value       = l.Value,
                            Name        = l.Name,
                        }).ToList(),
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE GetLocals failed: {ex.Message}"); }
            }
            return CreateErrorResponse("GETLOCALS requires PINE mode");
        }

        private string HandleSaveStateFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return CreateErrorResponse("FilePath is required for SAVESTATETOFILE");
            if (IsPineMode)
            {
                try   { PineClient!.SaveStateFile(filePath); return CreateSuccessResponse($"State saved to '{filePath}'"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE SaveStateFile failed: {ex.Message}"); }
            }
            return CreateErrorResponse("SAVESTATETOFILE requires PINE mode; use SNAPSHOT for the internal emulator");
        }

        private string HandleLoadStateFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return CreateErrorResponse("FilePath is required for LOADSTATEFROMFILE");
            if (IsPineMode)
            {
                try
                {
                    bool ok = PineClient!.LoadStateFile(filePath);
                    return ok
                        ? CreateSuccessResponse($"State loaded from '{filePath}'")
                        : CreateErrorResponse($"PINE LoadStateFile failed for '{filePath}'");
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE LoadStateFile failed: {ex.Message}"); }
            }
            return CreateErrorResponse("LOADSTATEFROMFILE requires PINE mode; use LOADSNAPSHOT for the internal emulator");
        }

        /// <summary>
        /// Extract memory dump files from a PCSX2 .p2s save-state archive to a
        /// companion directory (<paramref name="filePath"/>.extracted/).  Reuses the same ZipFile
        /// pattern as <see cref="OpenDiscStream"/>.  Writes snapshot-info.json so
        /// the history reader can treat the extracted folder as a normal snapshot.
        /// Returns the extracted directory path in Message and the region list in
        /// Regions.  Subsequent calls reuse the cached extraction if the .p2s file
        /// has not changed.
        /// </summary>
        private string HandleExtractP2s(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return CreateErrorResponse("FilePath is required for EXTRACTP2S");

            if (!File.Exists(filePath))
                return CreateErrorResponse($"File not found: {filePath}");

            try
            {
                string extractDir = filePath + ".extracted";
                string infoPath   = Path.Combine(extractDir, "snapshot-info.json");

                // Return cached extraction if already done and the .p2s hasn't changed.
                if (File.Exists(infoPath) &&
                    File.GetLastWriteTimeUtc(infoPath) >= File.GetLastWriteTimeUtc(filePath))
                {
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = true,
                        Command = "EXTRACTP2S",
                        Message = extractDir,
                    });
                }

                Directory.CreateDirectory(extractDir);

                // Memory entries: the name inside the .p2s zip → output filename + region metadata.
                // Output filenames match the convention used by HandleSnapshot so that the
                // snapshot history reader can process both internal and PCSX2 snapshots.
                var memEntries = new[]
                {
                    (ZipName: "eeMemory.bin",  OutFile: "EEmem.bin",  Label: "EE Main RAM", Base: 0x00000000u),
                    (ZipName: "iopMemory.bin", OutFile: "IOPmem.bin", Label: "IOP RAM",     Base: EeIopMirrorBase),
                };

                var regions  = new List<SnapshotRegion>();
                using var archive = ZipFile.OpenRead(filePath);

                foreach (var mem in memEntries)
                {
                    var entry = archive.Entries.FirstOrDefault(e =>
                        string.Equals(e.Name, mem.ZipName, StringComparison.OrdinalIgnoreCase));

                    if (entry == null) { continue; }

                    string outPath = Path.Combine(extractDir, mem.OutFile);
                    entry.ExtractToFile(outPath, overwrite: true);

                    regions.Add(new SnapshotRegion
                    {
                        Label       = mem.Label,
                        File        = mem.OutFile,
                        BaseAddress = mem.Base,
                        Size        = (int)new FileInfo(outPath).Length,
                    });
                }

                if (regions.Count == 0)
                    return CreateErrorResponse("No memory dump files (eeMemory.bin, iopMemory.bin) found in the .p2s archive");

                // Write snapshot-info.json so the TypeScript history reader can use the
                // extracted directory exactly like an internal-emulator snapshot folder.
                string snapName = Path.GetFileNameWithoutExtension(filePath);
                var info = new
                {
                    name      = snapName,
                    timestamp = DateTime.UtcNow.ToString("o"),
                    isoName   = string.Empty,
                    regions   = regions.Select(r => new
                    {
                        Label       = r.Label,
                        File        = r.File,
                        BaseAddress = r.BaseAddress,
                        Size        = r.Size,
                    }),
                };
                File.WriteAllText(infoPath,
                    JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "EXTRACTP2S",
                    Message = extractDir,
                    Regions = regions,
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"EXTRACTP2S failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the current running thread from CPU state and attempts to walk
        /// the PS2 EE OS thread control block list in EE RAM.  Each TCB is 128
        /// bytes; status byte at offset 0: 0x01=running, 0x02=ready, 0x04=wait,
        /// 0x08=suspend, 0x10=dormant.  The thread table is located by reading
        /// the kernel's internal data starting at physical 0x00000000.
        /// </summary>
        private string HandleGetThreads()
        {
            if (IsPineMode)
            {
                try
                {
                    var pineThreads = PineClient!.GetEEThreads();
                    var pineThreadEntries = pineThreads.Select(t => new ThreadEntry
                    {
                        Id       = (int)t.TID,
                        Status   = t.Status switch
                        {
                            1 => "Running",
                            2 => "Ready",
                            4 => "Waiting",
                            8 => "Suspended",
                            16 => "Dormant",
                            _ => $"0x{t.Status:X2}",
                        },
                        Pc       = t.PC,
                        Sp       = t.StackTop,
                        Priority = (int)t.Priority,
                        EntryPc  = t.EntryPoint,
                    }).ToList();
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = true,
                        Command = "GETTHREADS",
                        Threads = pineThreadEntries,
                    });
                }
                catch (Exception ex)
                {
                    return CreateErrorResponse($"PINE GETTHREADS failed: {ex.Message}");
                }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            var cpu     = Scheduler.EE.Cpu;
            var bus     = Scheduler.EE.Bus;
            var threads = new List<ThreadEntry>();

            uint currentPc = NormalizePcToKseg0((uint)(cpu.PC & 0xFFFFFFFFUL));
            uint currentSp = (uint)(cpu.RegsLo[29] & 0xFFFFFFFFUL);

            // ── Walk the PS2 EE OS thread control block list ─────────────────
            // The EE kernel places its thread pool at physical RAM.
            // Structure per TCB (128 bytes):
            //   +0x00  u32 status       (0x01=THS_RUN, 0x02=THS_READY,
            //                            0x04=THS_WAIT, 0x08=THS_SUSPEND,
            //                            0x10=THS_DORMANT)
            //   +0x04  u32 entry        (original entry PC)
            //   +0x08  u32 currentSp    (saved $sp when not running)
            //   +0x0C  u32 stackTop     (stack base)
            //   +0x10  u32 stackSize
            //   +0x14  u32 gp
            //   +0x18  u32 initPriority
            //   +0x1C  u32 priority
            //   +0x20  u32 waitType
            //   +0x24  u32 waitId
            //   +0x28  u32 wakeupCount
            //   +0x2C  u32 attr
            //   +0x30  u32 option
            //   +0x70  u64 savedPc      (saved EPC for non-running threads)
            //
            // The kernel exports a thread count at a well-known location.
            // We scan the first portion of EE RAM to find valid TCBs.

            const uint TcbSize      = 128u;
            const uint MaxTcbScan   = 256u;  // Cap scan to 256 threads
            // Typical EE OS thread pool base (physical).
            // The BIOS places the TCB array at approximately 0x00000100,
            // immediately after the 256-byte kernel exception vector area.
            const uint TcbPoolBase  = 0x00000100u;

            bool foundAny = false;
            for (uint i = 0; i < MaxTcbScan; i++)
            {
                uint physBase = TcbPoolBase + i * TcbSize;
                if (physBase + TcbSize > (uint)PS2MemoryBus.MainRamSize)
                    break;

                uint statusRaw = ReadEEWord(bus, physBase + 0x00);
                uint entry     = ReadEEWord(bus, physBase + 0x04);
                uint savedSp   = ReadEEWord(bus, physBase + 0x08);
                uint initPri   = ReadEEWord(bus, physBase + 0x18);
                uint curPri    = ReadEEWord(bus, physBase + 0x1C);

                // Valid status values are a single power-of-two bit in [0x01..0x10].
                // Zero means the slot is unallocated; anything else is garbage.
                if (statusRaw == 0 || (statusRaw & ~0x1Fu) != 0 ||
                    (statusRaw & (statusRaw - 1)) != 0)
                    continue;

                // Entry must be a plausible kernel/user code address or zero (idle)
                if (entry != 0 &&
                    !(entry >= 0x00100000u || entry >= 0x80000000u))
                    continue;

                string status = statusRaw switch
                {
                    0x01 => "Running",
                    0x02 => "Ready",
                    0x04 => "Waiting",
                    0x08 => "Suspended",
                    0x10 => "Dormant",
                    _    => $"0x{statusRaw:X2}",
                };

                // For the running thread use the live CPU PC/SP.
                bool isRunning = statusRaw == 0x01;
                uint tcbPc = isRunning ? currentPc : NormalizePcToKseg0(entry);
                uint tcbSp = isRunning ? currentSp : savedSp;

                threads.Add(new ThreadEntry
                {
                    Id       = (int)i,
                    Status   = status,
                    Pc       = tcbPc,
                    Sp       = tcbSp,
                    Priority = (int)curPri,
                    EntryPc  = NormalizePcToKseg0(entry),
                });
                foundAny = true;
            }

            // If the TCB scan found nothing (emulator not yet booted into OS, or
            // BIOS layout different), synthesise one entry from the live CPU state.
            if (!foundAny)
            {
                threads.Add(new ThreadEntry
                {
                    Id       = 0,
                    Status   = Scheduler.Paused ? "Suspended" : "Running",
                    Pc       = currentPc,
                    Sp       = currentSp,
                    Priority = 0,
                    EntryPc  = currentPc,
                });
            }

            var response = new CommandResponse
            {
                Success = true,
                Command = "GETTHREADS",
                Threads = threads,
            };
            return JsonSerializer.Serialize(response);
        }

        /// <summary>Read a little-endian 32-bit word from the EE physical RAM.</summary>
        private static uint ReadEEWord(PS2MemoryBus bus, uint physAddr)
        {
            if (physAddr + 4 > (uint)PS2MemoryBus.MainRamSize) return 0;
            return (uint)(bus.Ram[physAddr]
                        | (bus.Ram[physAddr + 1] << 8)
                        | (bus.Ram[physAddr + 2] << 16)
                        | (bus.Ram[physAddr + 3] << 24));
        }

        /// <summary>
        /// Handle SETREG command — writes a new value to a named EE register.
        /// <para>Supported register names: GPR (index 1-31), PC, HI, LO, HI1, LO1, SA, COP0 (index 0-31), FPR (index 0-31).</para>
        /// <para>Values may be supplied as decimal integers or as 0x-prefixed hexadecimal strings.</para>
        /// </summary>
        private string HandleSetRegister(string? regName, int? regIndex, string? regValue)
        {
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            if (string.IsNullOrWhiteSpace(regName) || string.IsNullOrWhiteSpace(regValue))
                return CreateErrorResponse("Missing parameters");

            if (!TryParseUInt64(regValue!, out ulong value))
                return CreateErrorResponse("Invalid register value — expected decimal or 0x-prefixed hex");

            var cpu = Scheduler.EE.Cpu;
            switch (regName!.ToUpperInvariant())
            {
                case "GPR":
                    if (regIndex == null || regIndex < 1 || regIndex > 31)
                        return CreateErrorResponse("GPR index must be 1–31 ($zero is read-only)");
                    cpu.RegsLo[regIndex.Value] = value;
                    // Writing via the integer (lower) lane always clears the upper lane
                    // so the visible 128-bit value matches the written 64-bit value.
                    cpu.RegsHi[regIndex.Value] = 0;
                    break;
                case "PC":
                    cpu.PC = value;
                    break;
                case "HI":  cpu.HI  = value; break;
                case "LO":  cpu.LO  = value; break;
                case "HI1": cpu.HI1 = value; break;
                case "LO1": cpu.LO1 = value; break;
                case "SA":  cpu.SA  = (int)(uint)value; break;
                case "COP0":
                    if (regIndex == null || regIndex < 0 || regIndex > 31)
                        return CreateErrorResponse("COP0 index must be 0–31");
                    cpu.COP0.WriteRegister64(regIndex.Value, value);
                    break;
                case "FPR":
                    if (regIndex == null || regIndex < 0 || regIndex > 31)
                        return CreateErrorResponse("FPR index must be 0–31");
                    cpu.FPR[regIndex.Value] = BitConverter.UInt32BitsToSingle((uint)value);
                    break;
                case "FCR":
                    // PS2 EE FPU only has FCR0 (FIR, read-only) and FCR31 (FCSR).
                    if (regIndex == 31)
                        cpu.COP1.WriteControlRegister(31, (uint)value);
                    else
                        return CreateErrorResponse("FCR: only FCR31 (FCSR) is writable on the PS2 EE");
                    break;
                // VU0 integer registers (vi00–vi15) — PCSX2 category EECAT_VU0I index 0-15
                case "VU0I":
                    if (regIndex == null || regIndex < 0 || regIndex > 15)
                        return CreateErrorResponse("VU0I index must be 0–15");
                    if (regIndex > 0) // vi0 is read-only (hardwired to 0)
                        cpu.VU0.VI[regIndex.Value] = (ushort)(value & 0xFFFF);
                    break;
                // VU0 float registers (vf00–vf31) — PCSX2 category EECAT_VU0F
                // Value is the X component raw IEEE-754 bits; Y/Z/W remain unchanged.
                case "VU0F":
                    if (regIndex == null || regIndex < 0 || regIndex > 31)
                        return CreateErrorResponse("VU0F index must be 0–31");
                    if (regIndex > 0) // vf0 is read-only (hardwired)
                    {
                        var vf = cpu.VU0.VF[regIndex.Value];
                        cpu.VU0.VF[regIndex.Value] = new System.Numerics.Vector4(
                            BitConverter.UInt32BitsToSingle((uint)value),
                            vf.Y, vf.Z, vf.W);
                    }
                    break;
                default:
                    return CreateErrorResponse(
                        $"Unknown register type '{regName}'. " +
                        "Supported: GPR, PC, HI, LO, HI1, LO1, SA, COP0, FPR, FCR, VU0I, VU0F");
            }
            return CreateSuccessResponse("Register updated");
        }

        /// <summary>
        /// Parse a register value string that may be decimal or 0x-prefixed hex.
        /// </summary>
        private static bool TryParseUInt64(string s, out ulong value)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return ulong.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber,
                                      System.Globalization.CultureInfo.InvariantCulture, out value);
            return ulong.TryParse(s, System.Globalization.NumberStyles.None,
                                  System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Handle WRITEMEM command — write arbitrary bytes to live EE or IOP memory.
        /// The bytes are supplied as a hex string (spaces, commas and "0x" prefixes are ignored).
        /// In PINE mode, writes are forwarded to PCSX2 via the PINE IPC socket.
        /// </summary>
        private string HandleWriteMem(uint address, string? hexData, string? region)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
            if (string.IsNullOrWhiteSpace(hexData))
                return CreateErrorResponse("No data provided");

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);

            byte[] bytes;
            try
            {
                var cleaned = hexData
                    .Replace(" ", "").Replace(",", "")
                    .Replace("0x", "").Replace("0X", "");
                if (cleaned.Length % 2 != 0) cleaned = "0" + cleaned;
                bytes = Convert.FromHexString(cleaned);
            }
            catch (FormatException)
            {
                return CreateErrorResponse("Invalid hex data — expected pairs of hex digits");
            }

            if (IsPineMode)
            {
                try
                {
                    uint pineAddr = TranslatePineAddress(address, useIop);
                    PineClient!.WriteBytes(pineAddr, bytes);
                    return CreateSuccessResponse($"Wrote {bytes.Length} byte(s) to 0x{address:X8}");
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE WriteMem error: {ex.Message}"); }
            }

            for (int i = 0; i < bytes.Length; i++)
            {
                uint vaddr = address + (uint)i;
                if (useIop)
                {
                    // Supported address windows for IOP writes:
                    //   0x00000000–0x003FFFFF  physical IOP RAM (direct)
                    //   0x80000000–0xBFFFFFFF  KSEG0/KSEG1 — strip top 3 bits
                    //   0x1C000000–0x1DFFFFFF  EE VTLB mirror of IOP RAM
                    // The IOP RAM panel uses region='iop' and sends addresses starting
                    // at 0x1C000000, so the EeIopMirrorBase subtraction is essential.
                    uint physAddr = IopMemoryBus.TranslateAddress(vaddr);
                    if (physAddr >= EeIopMirrorBase && physAddr < EeIopMirrorEnd)
                        physAddr -= EeIopMirrorBase;
                    if (physAddr < (uint)IopMemoryBus.RamSize)
                        Scheduler!.IOP.Bus.Ram[physAddr] = bytes[i];
                }
                else
                {
                    uint physAddr;
                    if (vaddr >= 0x80000000u && vaddr < 0xC0000000u)
                        physAddr = vaddr & 0x1FFFFFFFu;
                    else if (vaddr >= EeUncachedRamBase && vaddr < EeUncachedRamEnd)
                        physAddr = vaddr - EeUncachedRamBase;
                    else
                        physAddr = vaddr;

                    if (physAddr < (uint)PS2MemoryBus.MainRamSize)
                        Scheduler!.EE.Bus.Ram[physAddr] = bytes[i];
                    else if (vaddr >= 0x70000000u && vaddr < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                        Scheduler!.EE.Bus.Scratchpad[vaddr - 0x70000000u] = bytes[i];
                }
            }
            return CreateSuccessResponse($"Wrote {bytes.Length} byte(s) to 0x{address:X8}");
        }

        /// <summary>
        /// Handle LOADBIN command — read a binary file from disk and copy it directly
        /// into live emulator memory at the specified address.  This is more efficient
        /// than WRITEMEM for large files because the file data is never transferred over
        /// the socket; the server reads it locally from <paramref name="filePath"/>.
        /// </summary>
        private string HandleLoadBin(string? filePath, uint address, string? region)
        {
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            if (string.IsNullOrWhiteSpace(filePath))
                return CreateErrorResponse("InputPath is required for LOADBIN");
            if (!File.Exists(filePath))
                return CreateErrorResponse($"File not found: {filePath}");

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Could not read file: {ex.Message}");
            }

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
            if (useIop)
            {
                // Translate the start address and bulk-copy when the entire range fits in IOP RAM.
                uint startPhys = IopMemoryBus.TranslateAddress(address);
                if (startPhys >= EeIopMirrorBase && startPhys < EeIopMirrorEnd)
                    startPhys -= EeIopMirrorBase;

                if (startPhys + (uint)bytes.Length <= (uint)IopMemoryBus.RamSize)
                {
                    Array.Copy(bytes, 0, Scheduler.IOP.Bus.Ram, (int)startPhys, bytes.Length);
                }
                else
                {
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        uint vaddr    = address + (uint)i;
                        uint physAddr = IopMemoryBus.TranslateAddress(vaddr);
                        if (physAddr >= EeIopMirrorBase && physAddr < EeIopMirrorEnd)
                            physAddr -= EeIopMirrorBase;
                        if (physAddr < (uint)IopMemoryBus.RamSize)
                            Scheduler.IOP.Bus.Ram[physAddr] = bytes[i];
                    }
                }
            }
            else
            {
                // Translate the EE start address and bulk-copy when the entire range fits in Main RAM.
                uint startPhys;
                if (address >= 0x80000000u && address < 0xC0000000u)
                    startPhys = address & 0x1FFFFFFFu;
                else if (address >= EeUncachedRamBase && address < EeUncachedRamEnd)
                    startPhys = address - EeUncachedRamBase;
                else
                    startPhys = address;

                if (startPhys + (uint)bytes.Length <= (uint)PS2MemoryBus.MainRamSize)
                {
                    Array.Copy(bytes, 0, Scheduler.EE.Bus.Ram, (int)startPhys, bytes.Length);
                }
                else
                {
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        uint vaddr = address + (uint)i;
                        uint physAddr;
                        if (vaddr >= 0x80000000u && vaddr < 0xC0000000u)
                            physAddr = vaddr & 0x1FFFFFFFu;
                        else if (vaddr >= EeUncachedRamBase && vaddr < EeUncachedRamEnd)
                            physAddr = vaddr - EeUncachedRamBase;
                        else
                            physAddr = vaddr;

                        if (physAddr < (uint)PS2MemoryBus.MainRamSize)
                            Scheduler.EE.Bus.Ram[physAddr] = bytes[i];
                        else if (vaddr >= 0x70000000u && vaddr < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                            Scheduler.EE.Bus.Scratchpad[vaddr - 0x70000000u] = bytes[i];
                    }
                }
            }
            return CreateSuccessResponse($"Loaded {bytes.Length} byte(s) from {Path.GetFileName(filePath)} to 0x{address:X8}");
        }

        /// <summary>
        /// Handle APPLYCHEATS command — parses PS2 GameShark/Action Replay cheat codes
        /// from the supplied text and writes them into the live emulator RAM.
        /// In PINE mode, patches are written to PCSX2 memory via the PINE IPC socket.
        /// </summary>
        private string HandleApplyCheats(string? cheatText)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
            if (string.IsNullOrWhiteSpace(cheatText))
                return CreateErrorResponse("No cheat data provided");

            try
            {
                IByteEditor byteEditor = IsPineMode
                    ? (IByteEditor)new PinePcsx2ByteEditor(PineClient!)
                    : new PS2ByteEditor(Scheduler!);
                var engine = new CodeDesigner.Ps2.CheatEngine.CheatEngine(byteEditor);
                var cheatList = new CheatList(cheatText);
                engine.PatchMemory(cheatList);
                return CreateSuccessResponse("Cheats applied successfully");
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ApplyCheats error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle PORTCHEATS command — delegates to <see cref="CodeDesigner.Ps2.CheatPorter.CheatPorter"/>
        /// to port GameShark cheat codes from one EE-RAM dump to another.
        /// </summary>
        private string HandlePortCheats(string? cheatText, string? sourcePath, string? targetPath)
        {
            if (string.IsNullOrWhiteSpace(cheatText))
                return CreateErrorResponse("No cheat data provided");
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return CreateErrorResponse($"Source dump not found: {sourcePath}");
            if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
                return CreateErrorResponse($"Target dump not found: {targetPath}");

            byte[] sourceData;
            byte[] targetData;
            try
            {
                sourceData = File.ReadAllBytes(sourcePath);
                targetData = File.ReadAllBytes(targetPath);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Failed to read dump files: {ex.Message}");
            }

            // Parse cheat blocks using the existing infrastructure.
            var cheatList = new CheatList(cheatText);
            List<CheatBlock> blocks;
            try { blocks = cheatList.Parse(); }
            catch (Exception ex) { return CreateErrorResponse($"Failed to parse cheat codes: {ex.Message}"); }

            var logLines = new List<string>();
            logLines.Add($"[PORTCHEATS] Source: {sourcePath}");
            logLines.Add($"[PORTCHEATS] Target: {targetPath}");
            logLines.Add(string.Empty);

            var allResults = CheatPorterLib.Port(sourceData, targetData, blocks, logLines);
            int foundCount = allResults.Count(r => r.Found);

            var response = new CommandResponse
            {
                Success          = true,
                Command          = "PORTCHEATS",
                Data             = string.Join("\n", logLines),
                PortCheatResults = allResults,
            };
            return JsonSerializer.Serialize(response);
        }


        // Pointer-scanner helpers (shared by HandleScanPointers + HandleGetPointers)
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Maximum byte distance between a LUI instruction and its paired
        /// load/store/addiu in <see cref="BuildCodeReferencedAddresses"/>.
        /// 128 bytes (32 instructions) covers essentially all real MIPS compiler output.
        /// </summary>
        private const int MaxLuiWindow = 128;

        /// <summary>
        /// Minimum physical address a pointer must target to be considered valid.
        /// The first 64 KB of EE physical RAM is the exception-vector / BIOS-stub area;
        /// game data and heap allocations never live that low, so small constants such as
        /// 0x00000001 that accidentally match the address range are rejected.
        /// </summary>
        private const uint MinValidPointerPhysAddr = 0x00010000u;

        /// <summary>
        /// Tests whether <paramref name="val"/> is a plausible EE virtual address:
        /// user-mode or KSEG0/KSEG1 main RAM (physical 0x10000–32 MB), or EE scratchpad
        /// (0x70000000–0x70003FFF).
        ///
        /// Three filters are applied in order:
        /// <list type="bullet">
        ///   <item><description>Zero is always rejected.</description></item>
        ///   <item><description>
        ///     The value must be 4-byte aligned — valid MIPS code and data pointers are
        ///     always word-aligned; unaligned values are random constants, not pointers.
        ///   </description></item>
        ///   <item><description>
        ///     The corresponding physical address must be at or above
        ///     <see cref="MinValidPointerPhysAddr"/> (0x00010000).  The first 64 KB is
        ///     the EE exception-vector / BIOS-stub area; genuine heap/data pointers never
        ///     reside that low.  This eliminates the large class of false positives caused
        ///     by small integer constants (e.g. 0x00000001) whose numeric value falls
        ///     inside the 32 MB main-RAM range.
        ///   </description></item>
        /// </list>
        /// </summary>
        private static bool IsValidEePointerValue(uint val)
        {
            if (val == 0) return false;

            // Pointers are always 4-byte aligned in MIPS — reject unaligned values.
            if ((val & 3) != 0) return false;

            uint phys = val >= 0x80000000u && val < 0xC0000000u ? val & 0x1FFFFFFFu : val;

            // Reject the exception-vector / BIOS-stub area (physical < 0x00010000).
            if (phys < MinValidPointerPhysAddr) return false;

            return phys < (uint)PS2MemoryBus.MainRamSize ||
                   (val >= 0x70000000u && val < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize);
        }

        /// <summary>
        /// Normalises <paramref name="vaddr"/> to its EE physical equivalent and adds it
        /// to <paramref name="set"/>.  KSEG0/KSEG1 addresses (0x80000000–0xBFFFFFFF) are
        /// stripped to physical via <c>&amp; 0x1FFFFFFF</c>; scratchpad virtual addresses
        /// (0x70000000–0x70003FFF) are stored as-is; non-RAM addresses are silently ignored.
        /// Zero is never added.
        /// </summary>
        private static void AddEePhysAddr(HashSet<uint> set, uint vaddr)
        {
            if (vaddr == 0) return;
            uint phys = vaddr >= 0x80000000u && vaddr < 0xC0000000u ? vaddr & 0x1FFFFFFFu : vaddr;
            if (phys < (uint)PS2MemoryBus.MainRamSize)
                set.Add(phys);
            else if (vaddr >= 0x70000000u && vaddr < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                set.Add(vaddr); // scratchpad: keep virtual address
        }

        /// <summary>
        /// Scans <paramref name="codeBytes"/> (loaded at virtual base
        /// <paramref name="baseVAddr"/>) for all EE addresses referenced by MIPS code,
        /// and returns them as a set of normalised physical addresses.
        ///
        /// Three sources of references are collected in a single O(n) forward pass:
        /// <list type="bullet">
        ///   <item><description>
        ///     <b>Function entry points</b> — every <c>ADDIU $sp,$sp,-N</c> prologue.
        ///   </description></item>
        ///   <item><description>
        ///     <b>JAL targets</b> — the absolute target of every <c>JAL</c> instruction.
        ///   </description></item>
        ///   <item><description>
        ///     <b>LUI + I-type pairs</b> — every <c>LUI $rt, high</c> followed within
        ///     <see cref="MaxLuiWindow"/> bytes by a <c>LW/SW/ADDIU $rx, $rt, low</c>
        ///     that uses <c>$rt</c> as its base; the combined address
        ///     <c>(high &lt;&lt; 16) + sign_extend(low)</c> is recorded.
        ///   </description></item>
        /// </list>
        ///
        /// Per-register LUI state is invalidated whenever the register is overwritten,
        /// preventing stale LUI values from producing false address matches.
        /// </summary>
        private static HashSet<uint> BuildCodeReferencedAddresses(byte[] codeBytes, uint baseVAddr)
        {
            var set     = new HashSet<uint>();
            int len     = codeBytes.Length;

            // Per-register LUI tracking: luiSlot[r] = byte offset of the most recent
            // LUI that wrote $r (-1 means none); luiImm[r] = that LUI's imm16.
            var luiSlot = new int[32];
            var luiImm  = new uint[32];
            Array.Fill(luiSlot, -1);

            for (int i = 0; i + 3 < len; i += 4)
            {
                uint instr  = BitConverter.ToUInt32(codeBytes, i);
                uint opcode = (instr >> 26) & 0x3Fu;
                uint rs     = (instr >> 21) & 0x1Fu;
                uint rt     = (instr >> 16) & 0x1Fu;

                // ── LUI: rt ← imm << 16 ──────────────────────────────────────
                if (opcode == 0x0Fu)
                {
                    if (rt != 0) { luiSlot[rt] = i; luiImm[rt] = instr & 0xFFFFu; }
                    continue;
                }

                // ── JAL: call absolute target; clobbers $ra ───────────────────
                if (opcode == 0x03u)
                {
                    uint pc     = baseVAddr + (uint)i;
                    uint target = ((instr & 0x03FFFFFFu) << 2) | (pc & 0xF0000000u);
                    AddEePhysAddr(set, target);
                    luiSlot[31] = -1; // JAL writes $ra
                    continue;
                }

                // ── ADDIU: rt ← rs + sign_ext(imm) ──────────────────────────
                if (opcode == 0x09u)
                {
                    short addOff = (short)(instr & 0xFFFFu);
                    if (rs == 29u && rt == 29u && addOff < 0)
                    {
                        // ADDIU $sp, $sp, -N  →  function prologue
                        AddEePhysAddr(set, baseVAddr + (uint)i);
                    }
                    else if (rs != 0 && luiSlot[rs] >= 0 && i - luiSlot[rs] <= MaxLuiWindow)
                    {
                        // LUI $rs, high  +  ADDIU $rt, $rs, low  →  address reference
                        AddEePhysAddr(set, unchecked((luiImm[rs] << 16) + (uint)addOff));
                    }
                    if (rt != 0) luiSlot[rt] = -1; // ADDIU writes $rt
                    continue;
                }

                // ── Loads (0x20–0x27): rt ← mem[rs + off] ────────────────────
                if (opcode >= 0x20u && opcode <= 0x27u)
                {
                    if (rs != 0 && luiSlot[rs] >= 0 && i - luiSlot[rs] <= MaxLuiWindow)
                    {
                        short loadOff = (short)(instr & 0xFFFFu);
                        AddEePhysAddr(set, unchecked((luiImm[rs] << 16) + (uint)loadOff));
                    }
                    if (rt != 0) luiSlot[rt] = -1; // load writes $rt
                    continue;
                }

                // ── Stores (0x28–0x2F): mem[rs + off] ← rt ───────────────────
                if (opcode >= 0x28u && opcode <= 0x2Fu)
                {
                    if (rs != 0 && luiSlot[rs] >= 0 && i - luiSlot[rs] <= MaxLuiWindow)
                    {
                        short storeOff = (short)(instr & 0xFFFFu);
                        AddEePhysAddr(set, unchecked((luiImm[rs] << 16) + (uint)storeOff));
                    }
                    // Stores do not write to any register — no invalidation.
                    continue;
                }

                // ── Register-write invalidation for everything else ───────────
                // SPECIAL (0x00) R-type instructions write $rd.
                if (opcode == 0x00u)
                {
                    uint rd = (instr >> 11) & 0x1Fu;
                    if (rd != 0) luiSlot[rd] = -1;
                    continue;
                }

                // Branch opcodes (REGIMM, BEQ/BNE/BLEZ/BGTZ, BEQL/BNEL/BLEZL/BGTZL),
                // plain J, and the EE MMI group (0x1C, R-type) do not write $rt.
                // Every other I-type opcode is assumed to write $rt — invalidate.
                if (opcode != 0x01u             // REGIMM (branches)
                 && opcode != 0x02u             // J
                 && opcode != 0x04u && opcode != 0x05u  // BEQ / BNE
                 && opcode != 0x06u && opcode != 0x07u  // BLEZ / BGTZ
                 && opcode != 0x14u && opcode != 0x15u  // BEQL / BNEL
                 && opcode != 0x16u && opcode != 0x17u  // BLEZL / BGTZL
                 && opcode != 0x1Cu)            // MMI (handled as R-type above via rd)
                {
                    if (rt != 0) luiSlot[rt] = -1;
                }
            }

            return set;
        }

        /// <summary>
        /// Handle CONVERTCHEAT command — converts PS2 cheat codes between raw format
        /// and a device-specific encrypted format using the CheatConverter module.
        /// </summary>
        /// <param name="data">Multi-line cheat code text to convert.</param>
        /// <param name="format">
        /// Target cheat engine format name. Recognised values (case-insensitive):
        /// Raw, ActionReplay1, ActionReplay2, ActionReplayMax, GameSharkV3, CodeBreakerV1, CodeBreakerV7.
        /// </param>
        /// <param name="direction">
        /// Conversion direction: "toRaw" decrypts device codes → raw hex pairs;
        /// "toEngine" encrypts raw hex pairs → device format.
        /// </param>
        private string HandleConvertCheat(string? data, string? format, string? direction)
        {
            if (string.IsNullOrWhiteSpace(data))
                return CreateErrorResponse("No cheat data provided");

            try
            {
                // Parse the target format (default to auto-detect when not specified)
                CheatFormat targetFormat = CheatFormat.Raw;
                if (!string.IsNullOrWhiteSpace(format))
                {
                    if (!Enum.TryParse<CheatFormat>(format, ignoreCase: true, out targetFormat))
                        return CreateErrorResponse($"Unknown cheat format '{format}'. Valid values: Raw, ActionReplay1, ActionReplay2, ActionReplayMax, GameSharkV3, CodeBreakerV1, CodeBreakerV7");
                }

                string result;
                if (string.Equals(direction, "toRaw", StringComparison.OrdinalIgnoreCase))
                {
                    // Auto-detect source format and decode to raw, or use specified format
                    ICheatCodeConverter converter = string.IsNullOrWhiteSpace(format)
                        ? CheatFormatDetector.CreateConverter(CheatFormatDetector.Detect(data))
                        : CheatFormatDetector.CreateConverter(targetFormat);

                    var rawCodes = converter.Decode(data);
                    var rawConverter = new RawConverter();
                    result = rawConverter.Encode(rawCodes);
                }
                else
                {
                    // "toEngine": decode from raw then encode to target format
                    var rawConverter = new RawConverter();
                    var rawCodes = rawConverter.Decode(data);
                    var targetConverter = CheatFormatDetector.CreateConverter(targetFormat);
                    result = targetConverter.Encode(rawCodes);
                }

                var response = new CommandResponse { Success = true, Command = "CONVERTCHEAT", Data = result };
                return JsonSerializer.Serialize(response);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ConvertCheat error: {ex.Message}");
            }
        }


        // ── Memory Card handlers ────────────────────────────────────────────────

        private string HandleMcList(string? mcPath)
        {
            if (string.IsNullOrWhiteSpace(mcPath))
                return CreateErrorResponse("McPath is required for MC_LIST");
            if (!File.Exists(mcPath))
                return CreateErrorResponse($"Memory card file not found: {mcPath}");
            try
            {
                using var mc = Ps2MemoryCard.Open(mcPath);
                var saves = mc.ListSaves();
                var infos = saves.Select(s => new McSaveInfo
                {
                    Name       = s.Name,
                    FileCount  = s.FileCount,
                    Created    = s.Created.ToString("yyyy-MM-dd HH:mm:ss"),
                    Modified   = s.Modified.ToString("yyyy-MM-dd HH:mm:ss"),
                    TotalBytes = s.TotalBytes,
                    Files      = s.Files.Select(f => new McFileInfo { Name = f.Name, Size = f.Size }).ToList(),
                }).ToList();
                var resp = new CommandResponse { Success = true, Command = "MC_LIST", McSaves = infos };
                return JsonSerializer.Serialize(resp);
            }
            catch (Exception ex) { return CreateErrorResponse($"MC_LIST error: {ex.Message}"); }
        }

        private string HandleMcExport(string? mcPath, string? saveName, string? exportDir)
        {
            if (string.IsNullOrWhiteSpace(mcPath))   return CreateErrorResponse("McPath is required");
            if (string.IsNullOrWhiteSpace(saveName))  return CreateErrorResponse("SaveName is required");
            if (string.IsNullOrWhiteSpace(exportDir)) return CreateErrorResponse("ExportDir is required");
            if (!File.Exists(mcPath)) return CreateErrorResponse($"Memory card file not found: {mcPath}");
            try
            {
                string outPath = Path.Combine(exportDir, saveName);
                using var mc = Ps2MemoryCard.Open(mcPath);
                mc.ExportSave(saveName, outPath);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true, Command = "MC_EXPORT",
                    Message = $"Exported '{saveName}' to {outPath}",
                    Data    = outPath,
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MC_EXPORT error: {ex.Message}"); }
        }

        private string HandleMcImport(string? mcPath, string? importDir, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(mcPath))   return CreateErrorResponse("McPath is required");
            if (string.IsNullOrWhiteSpace(importDir)) return CreateErrorResponse("ImportDir is required");
            if (!File.Exists(mcPath)) return CreateErrorResponse($"Memory card file not found: {mcPath}");
            if (!Directory.Exists(importDir)) return CreateErrorResponse($"Import directory not found: {importDir}");
            try
            {
                using var mc = Ps2MemoryCard.Open(mcPath);
                mc.ImportSave(importDir, overwrite);
                string saveName = Path.GetFileName(importDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true, Command = "MC_IMPORT",
                    Message = $"Imported '{saveName}' from {importDir}",
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MC_IMPORT error: {ex.Message}"); }
        }

        private string HandleMcDelete(string? mcPath, string? saveName)
        {
            if (string.IsNullOrWhiteSpace(mcPath))  return CreateErrorResponse("McPath is required");
            if (string.IsNullOrWhiteSpace(saveName)) return CreateErrorResponse("SaveName is required");
            if (!File.Exists(mcPath)) return CreateErrorResponse($"Memory card file not found: {mcPath}");
            try
            {
                using var mc = Ps2MemoryCard.Open(mcPath);
                mc.DeleteSave(saveName);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true, Command = "MC_DELETE",
                    Message = $"Deleted save '{saveName}'",
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MC_DELETE error: {ex.Message}"); }
        }

        private string HandleMcFormat(string? mcPath)
        {
            if (string.IsNullOrWhiteSpace(mcPath)) return CreateErrorResponse("McPath is required");
            try
            {
                Ps2MemoryCard.Create(mcPath);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true, Command = "MC_FORMAT",
                    Message = $"Created new 8 MB memory card at {mcPath}",
                    Data    = mcPath,
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MC_FORMAT error: {ex.Message}"); }
        }


        /// full hardware reset (equivalent to killing and restarting the process)
        /// without dropping the socket connection.  The run-loop will re-create
        /// the scheduler, reload the BIOS and disc image, preserve existing
        /// breakpoints, and leave the emulator paused at the reset vector.
        /// </summary>
        private string HandleRestart()
        {
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            // Arm the gate BEFORE setting the flag so the run-loop can't signal it
            // before we start waiting.
            _restartComplete.Reset();

            // Signal the run-loop to reinitialise everything from scratch.
            PendingRestart = true;

            // Wake the run-loop from WaitForResumeAsync (in case it was already paused)
            // so it processes PendingRestart immediately instead of waiting for a
            // manual Resume from the client.  The run-loop checks PendingRestart
            // before calling RunSlice(), so no instructions execute on the old
            // scheduler after this unblock.
            Scheduler.Paused = false;

            // Block this socket-handler thread until the run-loop has fully replaced
            // the scheduler.  Without this wait, SETBP/RESUME commands that arrive
            // immediately after the "Restarted" response would target the old scheduler
            // — RESUME would unpause the old instance while the new scheduler's Paused
            // flag stayed true, leaving the emulator frozen forever.
            _restartComplete.Wait();

            return CreateSuccessResponse("Restarted");
        }

        /// <summary>
        /// Handle GETLABELS — returns all labels in the database, sorted by address.
        /// </summary>
        private string HandleGetLabels()
        {
            var labels = _labels
                .OrderBy(kv => kv.Key)
                .Select(kv => new LabelEntry
                {
                    Address = $"0x{kv.Key:X8}",
                    Label   = kv.Value.Label,
                    Comment = kv.Value.Comment
                })
                .ToList();

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success = true,
                Command = "GETLABELS",
                Labels  = labels
            });
        }

        /// <summary>
        /// Handle ADDLABEL — inserts or updates a label, then persists to disk.
        /// </summary>
        private string HandleAddLabel(uint? address, string? label, string? comment)
        {
            if (address == null)
                return CreateErrorResponse("Address is required for ADDLABEL");
            if (string.IsNullOrWhiteSpace(label))
                return CreateErrorResponse("Label is required for ADDLABEL");

            var trimmedLabel   = label.Trim();
            var trimmedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

            _labels[address.Value] = (trimmedLabel, trimmedComment);
            SaveLabels();

            return CreateSuccessResponse($"Label '{trimmedLabel}' set at 0x{address.Value:X8}");
        }

        /// <summary>
        /// Handle DELETELABEL — removes a label by address, then persists to disk.
        /// </summary>
        private string HandleDeleteLabel(uint? address)
        {
            if (address == null)
                return CreateErrorResponse("Address is required for DELETELABEL");

            if (!_labels.Remove(address.Value))
                return CreateErrorResponse($"No label found at 0x{address.Value:X8}");

            SaveLabels();
            return CreateSuccessResponse($"Label removed at 0x{address.Value:X8}");
        }

        /// <summary>
        /// Handle GETSTRINGS — scans base64-encoded binary data for null-terminated
        /// printable ASCII strings of at least MinStringLength characters.
        /// </summary>
        private string HandleGetStrings(uint startAddress, string? base64Data)
        {
            if (string.IsNullOrEmpty(base64Data))
                return CreateErrorResponse("Data (base64 binary) is required for GETSTRINGS");

            try
            {
                var bytes   = Convert.FromBase64String(base64Data);
                var results = new List<StringEntry>();
                const int MinLength = 4;

                int i = 0;
                while (i < bytes.Length)
                {
                    if (bytes[i] >= 0x20 && bytes[i] <= 0x7E)
                    {
                        int start = i;
                        while (i < bytes.Length && bytes[i] >= 0x20 && bytes[i] <= 0x7E)
                            i++;

                        // Must be null-terminated and meet minimum length
                        if (i < bytes.Length && bytes[i] == 0x00 && (i - start) >= MinLength)
                        {
                            results.Add(new StringEntry
                            {
                                Address = $"0x{(startAddress + (uint)start):X8}",
                                Value   = Encoding.ASCII.GetString(bytes, start, i - start)
                            });
                        }
                        i++; // step past the null / non-printable byte
                    }
                    else
                    {
                        i++;
                    }
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "GETSTRINGS",
                    Strings = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"GetStrings error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle GETFUNCTIONS — scans base64-encoded binary data for MIPS function
        /// entry points.  Detects two patterns:
        ///   1. ADDIU $sp, $sp, -N  (stack-frame prologue, opcode 9, rs=rt=29, imm less than 0)
        ///   2. Targets of JAL instructions within the same binary block.
        /// Auto-labels each candidate "FNC_0xADDRESS", but respects any user-defined
        /// label already in the database.
        /// </summary>
        private string HandleGetFunctions(uint startAddress, string? base64Data)
        {
            if (string.IsNullOrEmpty(base64Data))
                return CreateErrorResponse("Data (base64 binary) is required for GETFUNCTIONS");

            try
            {
                var bytes     = Convert.FromBase64String(base64Data);
                var candidates = new HashSet<uint>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    // Read little-endian 32-bit instruction word
                    uint instr  = BitConverter.ToUInt32(bytes, i);
                    uint opcode = (instr >> 26) & 0x3Fu;

                    if (opcode == 0x09u) // ADDIU
                    {
                        uint  rs  = (instr >> 21) & 0x1Fu;
                        uint  rt  = (instr >> 16) & 0x1Fu;
                        short imm = (short)(instr & 0xFFFFu);

                        // ADDIU $sp, $sp, -N  →  function prologue
                        if (rs == 29u && rt == 29u && imm < 0)
                            candidates.Add(startAddress + (uint)i);
                    }
                    else if (opcode == 0x03u) // JAL
                    {
                        uint pc         = startAddress + (uint)i;
                        uint target     = ((instr & 0x03FFFFFFu) << 2) | (pc & 0xF0000000u);

                        // Only include targets that fall inside the scanned block
                        if (target >= startAddress && target < startAddress + (uint)bytes.Length)
                            candidates.Add(target);
                    }
                }

                var functions = candidates
                    .OrderBy(addr => addr)
                    .Select(addr => new FunctionEntry
                    {
                        Address = $"0x{addr:X8}",
                        Label   = _labels.TryGetValue(addr, out var entry)
                                      ? entry.Label
                                      : $"FNC_0x{addr:X8}"
                    })
                    .ToList();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "GETFUNCTIONS",
                    Functions = functions
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"GetFunctions error: {ex.Message}");
            }
        }

        // ── File-binary helpers (base64 data, no live emulator required) ─────

        /// <summary>
        /// Handle GETPOINTERS — scans base64-encoded binary data for 4-byte aligned
        /// values that are confirmed EE memory pointers.
        ///
        /// Three filters are applied in sequence:
        ///   1. The candidate value must be non-zero, 4-byte aligned, and have a physical
        ///      address at or above 0x00010000 (see <see cref="IsValidEePointerValue"/>).
        ///   2. The candidate value must fall in a valid EE virtual address range
        ///      (user-mode main RAM, KSEG0, KSEG1, or scratchpad).
        ///   3. The candidate value must be referenced by MIPS code.  Code references
        ///      are sourced from live EE main RAM when a scheduler is running (most
        ///      accurate), or from the binary data itself when no emulator is attached.
        ///      In the latter case the filter is only applied if the binary contains
        ///      at least 10 <c>ADDIU $sp,$sp,-N</c> function prologues — fewer than
        ///      that suggests the binary is a pure-data slice with no useful code.
        /// </summary>
        private string HandleGetPointers(uint startAddress, string? base64Data)
        {
            if (string.IsNullOrEmpty(base64Data))
                return CreateErrorResponse("Data (base64 binary) is required for GETPOINTERS");

            try
            {
                var bytes = Convert.FromBase64String(base64Data);

                // Build the set of all EE addresses referenced by MIPS code.
                //   • Live emulator running: scan the full physical EE main RAM for the
                //     most reliable results (same source as HandleScanPointers).
                //   • No emulator: scan the binary itself, but only trust the result
                //     when it contains enough function prologues to confirm it is code.
                //     A pure-data binary (< 10 prologues) would produce spurious entries
                //     from accidental instruction bit-patterns, so we fall back to the
                //     address-range check alone in that case.
                HashSet<uint> codeRefs;
                if (Scheduler != null)
                {
                    codeRefs = BuildCodeReferencedAddresses(Scheduler.EE.Bus.Ram, 0x00000000u);
                }
                else
                {
                    int prologueCount = 0;
                    for (int k = 0; k + 3 < bytes.Length; k += 4)
                    {
                        uint w = BitConverter.ToUInt32(bytes, k);
                        // ADDIU $sp, $sp, -N  (opcode=9, rs=rt=29, imm<0)
                        if ((w >> 26) == 0x09u && ((w >> 21) & 0x1Fu) == 29u &&
                            ((w >> 16) & 0x1Fu) == 29u && (short)(w & 0xFFFF) < 0)
                            prologueCount++;
                    }
                    codeRefs = prologueCount >= 10
                        ? BuildCodeReferencedAddresses(bytes, startAddress)
                        : new HashSet<uint>();
                }

                var results = new List<PointerEntry>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint val = BitConverter.ToUInt32(bytes, i);

                    // Filter 1: plausible EE address range.
                    if (!IsValidEePointerValue(val)) continue;

                    // Filter 2: must be referenced by MIPS code.
                    // Skip this filter when no code was found (pure-data binary or early boot).
                    uint physVal = val >= 0x80000000u && val < 0xC0000000u ? val & 0x1FFFFFFFu : val;
                    if (codeRefs.Count > 0 && !codeRefs.Contains(physVal)) continue;

                    results.Add(new PointerEntry
                    {
                        Address  = $"0x{(startAddress + (uint)i):X8}",
                        PointsTo = $"0x{val:X8}"
                    });
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "GETPOINTERS",
                    Pointers = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"GetPointers error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle GETFLOATS — scans base64-encoded binary data for 4-byte aligned
        /// plausible IEEE-754 single-precision floats.
        /// </summary>
        private string HandleGetFloats(uint startAddress, string? base64Data)
        {
            if (string.IsNullOrEmpty(base64Data))
                return CreateErrorResponse("Data (base64 binary) is required for GETFLOATS");

            try
            {
                var bytes   = Convert.FromBase64String(base64Data);
                var results = new List<FloatEntry>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint bits = BitConverter.ToUInt32(bytes, i);
                    float f   = BitConverter.Int32BitsToSingle((int)bits);

                    if (float.IsNaN(f) || float.IsInfinity(f))
                        continue;

                    uint exp = (bits >> 23) & 0xFFu;
                    if (exp == 0u)
                        continue;

                    float absVal = MathF.Abs(f);
                    if (absVal < FloatScanMinAbs || absVal > FloatScanMaxAbs)
                        continue;

                    results.Add(new FloatEntry
                    {
                        Address = $"0x{(startAddress + (uint)i):X8}",
                        Value   = f,
                        Raw     = $"0x{bits:X8}"
                    });
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "GETFLOATS",
                    Floats  = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"GetFloats error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle GETINTEGERS — scans base64-encoded binary data for non-zero
        /// 4-byte aligned 32-bit signed integers.
        /// </summary>
        private string HandleGetIntegers(uint startAddress, string? base64Data)
        {
            if (string.IsNullOrEmpty(base64Data))
                return CreateErrorResponse("Data (base64 binary) is required for GETINTEGERS");

            try
            {
                var bytes   = Convert.FromBase64String(base64Data);
                var results = new List<IntegerEntry>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint raw = BitConverter.ToUInt32(bytes, i);
                    if (raw == 0x00000000u || raw == 0xFFFFFFFFu)
                        continue;

                    int val = (int)raw;
                    results.Add(new IntegerEntry
                    {
                        Address = $"0x{(startAddress + (uint)i):X8}",
                        Value   = val,
                        Hex     = $"0x{raw:X8}"
                    });
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "GETINTEGERS",
                    Integers = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"GetIntegers error: {ex.Message}");
            }
        }

        // ── Live-memory helpers (no MaxReadMemLength cap) ─────────────────────

        /// <summary>
        /// Read <paramref name="length"/> bytes starting at <paramref name="startAddress"/>
        /// directly from the live emulator RAM arrays.  In PINE mode the bytes are
        /// fetched from PCSX2 via the PINE IPC socket.
        /// Used by SCANSTRINGS, SCANFUNCTIONS, and SEARCH so they can scan whole
        /// regions without the 64 KB cap that READMEM enforces.
        /// </summary>
        private byte[] ReadLiveBytes(uint startAddress, int length, bool useIop)
        {
            if (IsPineMode)
            {
                uint pineAddr = TranslatePineAddress(startAddress, useIop);
                return PineClient!.ReadBytes(pineAddr, length);
            }

            var bytes = new byte[length];
            for (int i = 0; i < length; i++)
            {
                uint vaddr = startAddress + (uint)i;
                if (useIop)
                {
                    uint physAddr = IopMemoryBus.TranslateAddress(vaddr);
                    if (physAddr >= EeIopMirrorBase && physAddr < EeIopMirrorEnd)
                        physAddr -= EeIopMirrorBase;
                    bytes[i] = physAddr < (uint)IopMemoryBus.RamSize
                        ? Scheduler!.IOP.Bus.Ram[physAddr]
                        : (byte)0;
                }
                else
                {
                    uint physAddr;
                    if (vaddr >= 0x80000000u && vaddr < 0xC0000000u)
                        physAddr = vaddr & 0x1FFFFFFFu;
                    else if (vaddr >= EeUncachedRamBase && vaddr < EeUncachedRamEnd)
                        physAddr = vaddr - EeUncachedRamBase;
                    else
                        physAddr = vaddr;

                    if (physAddr < (uint)PS2MemoryBus.MainRamSize)
                        bytes[i] = Scheduler!.EE.Bus.Ram[physAddr];
                    else if (physAddr >= PS2MemoryBus.BiosRomPhysBase &&
                             physAddr <  PS2MemoryBus.BiosRomPhysBase + (uint)PS2MemoryBus.BiosRomSize)
                        bytes[i] = Scheduler!.EE.Bus.Bios[physAddr - PS2MemoryBus.BiosRomPhysBase];
                    else if (vaddr >= 0x70000000u && vaddr < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                        bytes[i] = Scheduler!.EE.Bus.Scratchpad[vaddr - 0x70000000u];
                    else
                        bytes[i] = 0;
                }
            }
            return bytes;
        }

        /// <summary>
        /// Handle SCANSTRINGS — reads <paramref name="length"/> bytes of live emulator
        /// memory starting at <paramref name="address"/> and scans for null-terminated
        /// printable ASCII strings of at least 4 characters.
        /// </summary>
        private string HandleScanStrings(uint address, int length, string? region)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
            if (length <= 0)
                return CreateErrorResponse("Length is required for SCANSTRINGS");

            try
            {
                bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
                var bytes   = ReadLiveBytes(address, length, useIop);
                var results = new List<StringEntry>();
                const int MinLength = 4;

                int i = 0;
                while (i < bytes.Length)
                {
                    if (bytes[i] >= 0x20 && bytes[i] <= 0x7E)
                    {
                        int start = i;
                        while (i < bytes.Length && bytes[i] >= 0x20 && bytes[i] <= 0x7E)
                            i++;
                        if (i < bytes.Length && bytes[i] == 0x00 && (i - start) >= MinLength)
                        {
                            results.Add(new StringEntry
                            {
                                Address = $"0x{(address + (uint)start):X8}",
                                Value   = Encoding.ASCII.GetString(bytes, start, i - start)
                            });
                        }
                        i++;
                    }
                    else
                    {
                        i++;
                    }
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "SCANSTRINGS",
                    Strings = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ScanStrings error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle SCANFUNCTIONS — reads <paramref name="length"/> bytes of live emulator
        /// memory and detects MIPS function entry points via ADDIU $sp,$sp,-N prologues
        /// and JAL targets.
        /// </summary>
        private string HandleScanFunctions(uint address, int length, string? region)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
            if (length <= 0)
                return CreateErrorResponse("Length is required for SCANFUNCTIONS");

            try
            {
                bool useIop  = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
                var bytes      = ReadLiveBytes(address, length, useIop);
                var candidates = new HashSet<uint>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint instr  = BitConverter.ToUInt32(bytes, i);
                    uint opcode = (instr >> 26) & 0x3Fu;

                    if (opcode == 0x09u)
                    {
                        uint  rs  = (instr >> 21) & 0x1Fu;
                        uint  rt  = (instr >> 16) & 0x1Fu;
                        short imm = (short)(instr & 0xFFFFu);
                        if (rs == 29u && rt == 29u && imm < 0)
                            candidates.Add(address + (uint)i);
                    }
                    else if (opcode == 0x03u)
                    {
                        uint pc     = address + (uint)i;
                        uint target = ((instr & 0x03FFFFFFu) << 2) | (pc & 0xF0000000u);
                        if (target >= address && target < address + (uint)bytes.Length)
                            candidates.Add(target);
                    }
                }

                var functions = candidates
                    .OrderBy(addr => addr)
                    .Select(addr => new FunctionEntry
                    {
                        Address = $"0x{addr:X8}",
                        Label   = _labels.TryGetValue(addr, out var entry)
                                      ? entry.Label
                                      : $"FNC_0x{addr:X8}"
                    })
                    .ToList();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "SCANFUNCTIONS",
                    Functions = functions
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ScanFunctions error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle SCANPOINTERS — reads <paramref name="length"/> bytes of live emulator
        /// memory starting at <paramref name="address"/> and returns 4-byte aligned values
        /// that are confirmed EE memory pointers.
        ///
        /// Three filters are applied in sequence:
        ///   1. The candidate value must be non-zero, 4-byte aligned, and have a physical
        ///      address at or above 0x00010000 (see <see cref="IsValidEePointerValue"/>).
        ///   2. The candidate value must fall in a valid EE virtual address range
        ///      (user-mode main RAM, KSEG0, KSEG1, or scratchpad).
        ///   3. The candidate value must be referenced by MIPS code in EE main RAM —
        ///      i.e. some function accesses that address via a LUI + load/store/addiu
        ///      pair, JAL target, or ADDIU $sp,$sp,-N prologue.
        ///
        /// The scan area is unrestricted: <paramref name="address"/> may point to any
        /// region (main RAM, BIOS, scratchpad, IOP, etc.).  The pointer VALUE check is
        /// always performed against EE address ranges so the tool works correctly
        /// regardless of which region is being scanned.
        ///
        /// If no code references are found in EE main RAM (e.g. early boot before any
        /// game code has loaded), filter 3 is skipped and only the address-range checks
        /// apply, preserving the original behaviour.
        /// </summary>
        private string HandleScanPointers(uint address, int length, string? region)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
            if (length <= 0)
                return CreateErrorResponse("Length is required for SCANPOINTERS");

            try
            {
                bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
                var bytes   = ReadLiveBytes(address, length, useIop);

                // Build the set of all EE addresses referenced by MIPS code.
                // We always use EE main RAM as the code source regardless of the scan
                // region so that the filter works when scanning BIOS, scratchpad, IOP, etc.
                // In PINE mode we don't have direct access to the full EE RAM array;
                // skip the code-reference filter to avoid a huge IPC read.
                HashSet<uint> codeRefs = Scheduler != null
                    ? BuildCodeReferencedAddresses(Scheduler.EE.Bus.Ram, 0x00000000u)
                    : new HashSet<uint>();

                var results = new List<PointerEntry>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint val = BitConverter.ToUInt32(bytes, i);

                    // Filter 1: plausible EE address range.
                    if (!IsValidEePointerValue(val)) continue;

                    // Filter 2: must be referenced by MIPS code in EE main RAM.
                    // Skip this filter when no code has been found yet (early boot).
                    uint physVal = val >= 0x80000000u && val < 0xC0000000u ? val & 0x1FFFFFFFu : val;
                    if (codeRefs.Count > 0 && !codeRefs.Contains(physVal)) continue;

                    results.Add(new PointerEntry
                    {
                        Address  = $"0x{(address + (uint)i):X8}",
                        PointsTo = $"0x{val:X8}"
                    });
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "SCANPOINTERS",
                    Pointers = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ScanPointers error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle SCANFLOATS — reads <paramref name="length"/> bytes of live emulator
        /// memory and returns 4-byte aligned IEEE-754 single-precision floats whose
        /// absolute value falls in the range [0.001, 1 000 000] (NaN and ±Inf are
        /// excluded, as are subnormals and the PS2 zero-flush value).
        /// </summary>
        private string HandleScanFloats(uint address, int length, string? region)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
            if (length <= 0)
                return CreateErrorResponse("Length is required for SCANFLOATS");

            try
            {
                bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
                var bytes   = ReadLiveBytes(address, length, useIop);
                var results = new List<FloatEntry>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint bits = BitConverter.ToUInt32(bytes, i);
                    float f   = BitConverter.Int32BitsToSingle((int)bits);

                    if (float.IsNaN(f) || float.IsInfinity(f))
                        continue;

                    // Skip subnormals (exponent bits == 0) — PS2 hardware flushes these to 0.
                    uint exp = (bits >> 23) & 0xFFu;
                    if (exp == 0u)
                        continue;

                    float absVal = MathF.Abs(f);
                    if (absVal < FloatScanMinAbs || absVal > FloatScanMaxAbs)
                        continue;

                    results.Add(new FloatEntry
                    {
                        Address = $"0x{(address + (uint)i):X8}",
                        Value   = f,
                        Raw     = $"0x{bits:X8}"
                    });
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "SCANFLOATS",
                    Floats  = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ScanFloats error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle SCANINTEGERS — reads <paramref name="length"/> bytes of live emulator
        /// memory and returns every non-zero 4-byte aligned 32-bit signed integer whose
        /// absolute value is at most 0x7FFFFFFF.  Values of 0x00000000 and 0xFFFFFFFF
        /// (common fill/sentinel patterns) are excluded to reduce noise.
        /// </summary>
        private string HandleScanIntegers(uint address, int length, string? region)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
            if (length <= 0)
                return CreateErrorResponse("Length is required for SCANINTEGERS");

            try
            {
                bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
                var bytes   = ReadLiveBytes(address, length, useIop);
                var results = new List<IntegerEntry>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint raw = BitConverter.ToUInt32(bytes, i);
                    // Skip common fill/sentinel values.
                    if (raw == 0x00000000u || raw == 0xFFFFFFFFu)
                        continue;

                    int val = (int)raw;
                    results.Add(new IntegerEntry
                    {
                        Address = $"0x{(address + (uint)i):X8}",
                        Value   = val,
                        Hex     = $"0x{raw:X8}"
                    });
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "SCANINTEGERS",
                    Integers = results
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ScanIntegers error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle SEARCH — searches bytes for a query string using the given mode.
        /// Bytes are taken from base64-encoded <c>Data</c> when present, or read
        /// directly from live emulator memory using <c>Address</c>, <c>Length</c>
        /// and <c>Region</c>.
        /// Modes: "text" (case-insensitive substring), "bytes" (hex pattern),
        ///        "string" (raw byte match), "regex" (case-insensitive regex).
        /// </summary>
        private string HandleSearch(string query, string mode, uint startAddress, int length,
                                    string? region, string? base64Data)
        {
            if (string.IsNullOrEmpty(query))
                return CreateErrorResponse("Query is required for SEARCH");

            try
            {
                byte[] bytes;
                if (!string.IsNullOrEmpty(base64Data))
                {
                    bytes = Convert.FromBase64String(base64Data);
                }
                else
                {
                    if (Scheduler == null && PineClient == null)
                        return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
                    if (length <= 0)
                        return CreateErrorResponse("Length is required when searching live memory");
                    bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
                    bytes = ReadLiveBytes(startAddress, length, useIop);
                }

                var matches = new List<SearchMatch>();

                if (mode == "bytes")
                {
                    var pattern = ParseBytePattern(query);
                    if (pattern == null)
                        return CreateErrorResponse("Invalid byte pattern");

                    for (int i = 0; i <= bytes.Length - pattern.Length; i++)
                    {
                        bool found = true;
                        for (int j = 0; j < pattern.Length; j++)
                            if (bytes[i + j] != pattern[j]) { found = false; break; }
                        if (found)
                        {
                            matches.Add(new SearchMatch
                            {
                                InstructionIndex = i / 4,
                                Address          = startAddress + (uint)i,
                                Field            = "bytes",
                                Start            = 0,
                                Length           = pattern.Length * 3 - 1
                            });
                            i += pattern.Length - 1;
                        }
                    }
                }
                else if (mode == "string")
                {
                    var strBytes = Encoding.UTF8.GetBytes(query);
                    for (int i = 0; i <= bytes.Length - strBytes.Length; i++)
                    {
                        bool found = true;
                        for (int j = 0; j < strBytes.Length; j++)
                            if (bytes[i + j] != strBytes[j]) { found = false; break; }
                        if (found)
                        {
                            matches.Add(new SearchMatch
                            {
                                InstructionIndex = i / 4,
                                Address          = startAddress + (uint)i,
                                Field            = "bytes",
                                Start            = 0,
                                Length           = strBytes.Length * 3 - 1
                            });
                            i += strBytes.Length - 1;
                        }
                    }
                }
                else
                {
                    // text or regex: disassemble each instruction and search its fields
                    bool useRegex = mode == "regex";
                    Regex? rx = null;
                    if (useRegex)
                    {
                        try { rx = new Regex(query, RegexOptions.IgnoreCase); }
                        catch { return CreateErrorResponse("Invalid regular expression"); }
                    }
                    string queryLower = query.ToLowerInvariant();

                    for (int i = 0; i + 3 < bytes.Length; i += 4)
                    {
                        uint   word = BitConverter.ToUInt32(bytes, i);
                        uint   addr = startAddress + (uint)i;
                        int    idx  = i / 4;

                        string addrStr  = $"{addr:X8}";
                        string bytesStr = $"{bytes[i+3]:X2}{bytes[i+2]:X2}{bytes[i+1]:X2}{bytes[i]:X2}";
                        string disasm   = MipsDisassembler.Disassemble(word, addr);
                        int    sp       = disasm.IndexOf(' ');
                        string op       = sp < 0 ? disasm : disasm[..sp];
                        string args     = sp < 0 ? "" : disasm[(sp + 1)..].Trim();
                        bool   hasLabel = _labels.TryGetValue(addr, out var le);
                        string label    = hasLabel ? le!.Label         : "";
                        string comment  = hasLabel ? (le!.Comment ?? "") : "";

                        (string name, string text)[] fields =
                        {
                            ("addr",    addrStr),
                            ("bytes",   bytesStr),
                            ("label",   label),
                            ("op",      op),
                            ("args",    args),
                            ("comment", comment),
                        };

                        foreach (var (fieldName, fieldText) in fields)
                        {
                            if (useRegex && rx != null)
                            {
                                foreach (Match m in rx.Matches(fieldText))
                                    matches.Add(new SearchMatch
                                    {
                                        InstructionIndex = idx, Address = addr,
                                        Field = fieldName, Start = m.Index, Length = m.Length
                                    });
                            }
                            else
                            {
                                string fl = fieldText.ToLowerInvariant();
                                int    fi = 0;
                                while ((fi = fl.IndexOf(queryLower, fi, StringComparison.Ordinal)) >= 0)
                                {
                                    matches.Add(new SearchMatch
                                    {
                                        InstructionIndex = idx, Address = addr,
                                        Field = fieldName, Start = fi, Length = query.Length
                                    });
                                    fi += query.Length;
                                }
                            }
                        }
                    }
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "SEARCH",
                    Matches = matches
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Search error: {ex.Message}");
            }
        }

        /// <summary>Parse a whitespace/comma-separated hex byte pattern string.</summary>
        private static byte[]? ParseBytePattern(string pattern)
        {
            var cleaned = pattern
                .Replace("0x", "", StringComparison.OrdinalIgnoreCase)
                .Replace(",", " ").Trim();
            if (string.IsNullOrEmpty(cleaned)) return null;
            var parts  = cleaned.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<byte>();
            foreach (var p in parts)
            {
                if (!Regex.IsMatch(p, @"^[0-9a-fA-F]{1,2}$")) return null;
                result.Add(Convert.ToByte(p, 16));
            }
            return result.Count > 0 ? result.ToArray() : null;
        }

        /// <summary>
        /// Persist the current in-memory label dictionary back to the first ps2MemMap.json
        /// file that was found at startup (or the base-directory path as a fallback).
        /// </summary>
        private void SaveLabels()
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

            var json = JsonSerializer.Serialize(new { symbol = symbols },
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(savePath, json);
            Console.WriteLine($"[Labels] Saved {_labels.Count} symbol(s) to {savePath}");
        }

        // ── LISTDISCELFS ─────────────────────────────────────────────────────────

        /// <summary>
        /// Open a disc image stream from either a plain ISO/BIN path or a ZIP archive
        /// that contains an ISO or BIN entry.  The caller owns the returned stream and
        /// must dispose it.  Throws <see cref="InvalidOperationException"/> when the
        /// file cannot be opened as a disc image.
        /// </summary>
        private static Stream OpenDiscStream(string path)
        {
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = ZipFile.OpenRead(path);
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.Name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) ||
                    e.Name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                    throw new InvalidOperationException("No ISO or BIN disc image found inside the ZIP archive.");

                if (entry.Length > (long)int.MaxValue)
                    throw new InvalidOperationException(
                        $"Disc image inside ZIP is too large ({entry.Length / 1_048_576.0:F0} MB). " +
                        "Extract the ISO to disk and select it directly.");

                // Read the compressed entry into a MemoryStream so the archive can be
                // closed while the caller continues to use the stream.
                var ms = new MemoryStream((int)entry.Length);
                using (var entryStream = entry.Open())
                    entryStream.CopyTo(ms);
                ms.Position = 0;
                return ms;
            }

            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        /// <summary>
        /// Open an ISO-9660 disc image, walk all files, and return a list of every
        /// ELF or IRX file found.  Also reads SYSTEM.CNF to mark the BOOT2
        /// executable with <see cref="DiscElfEntry.IsBootElf"/> = true.
        /// The <paramref name="isoPath"/> may be a plain ISO/BIN file or a ZIP
        /// archive containing an ISO/BIN entry.
        /// </summary>
        private string HandleListDiscElfs(string? isoPath)
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                return CreateErrorResponse("IsoPath is required.");

            if (!File.Exists(isoPath))
                return CreateErrorResponse($"ISO file not found: {isoPath}");

            try
            {
                using var stream = OpenDiscStream(isoPath);
                var cdvd = new CodeDesigner.Ps2.Emulator.IOP.Cdvd();
                cdvd.LoadFromStream(stream);

                // ── Read SYSTEM.CNF to discover the BOOT2 executable ──────────
                string? bootElfName = null;
                var cnfEntry = cdvd.FindFile("SYSTEM.CNF");
                if (cnfEntry != null)
                {
                    byte[] cnfBytes = cdvd.ReadFile(cnfEntry.Value);
                    string cnfText  = System.Text.Encoding.Latin1.GetString(cnfBytes);
                    string? rawBoot = CodeDesigner.Ps2.Emulator.GameLoader.ParseSystemCnf(cnfText);
                    if (rawBoot != null)
                    {
                        // Normalise to bare filename (strip path and version suffix).
                        int slash = rawBoot.LastIndexOfAny(new[] { '/', '\\' });
                        bootElfName = slash >= 0 ? rawBoot[(slash + 1)..] : rawBoot;
                        int semi = bootElfName.IndexOf(';');
                        if (semi >= 0) bootElfName = bootElfName[..semi];
                    }
                }

                // ── Walk all files looking for ELF / IRX ─────────────────────
                // Helper: strip the ISO-9660 version suffix (";1", ";2", …).
                static string StripVersion(string n)
                {
                    int s = n.IndexOf(';');
                    return s >= 0 ? n[..s] : n;
                }

                // PS2 disc-ID pattern: 4 uppercase letters + '_' + 3 digits + '.' + 2 digits
                // e.g. SCUS_971.98, SLUS_210.40, SLES_123.45
                var discIdPattern = new System.Text.RegularExpressions.Regex(
                    @"^[A-Z]{4}_\d{3}\.\d{2}$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                var elfFiles = new List<DiscElfEntry>();
                foreach (var (discPath, entry) in cdvd.EnumerateFiles())
                {
                    string cleanName = StripVersion(entry.Name);
                    string ext       = System.IO.Path.GetExtension(cleanName).ToUpperInvariant();

                    // Include: explicit .ELF / .IRX files, or PS2 disc-ID executables
                    // (e.g. SCUS_971.98 has no .ELF extension but is a valid EE ELF).
                    bool isElf   = ext == ".ELF" || ext == ".IRX";
                    bool isDiscId = discIdPattern.IsMatch(cleanName);
                    if (!isElf && !isDiscId)
                        continue;

                    bool isBootElf = bootElfName != null &&
                        cleanName.Equals(bootElfName, StringComparison.OrdinalIgnoreCase);

                    elfFiles.Add(new DiscElfEntry
                    {
                        Path      = discPath,
                        Name      = cleanName,
                        Size      = entry.Size,
                        IsBootElf = isBootElf,
                    });
                }

                // Sort: BOOT2 first, then by path.
                elfFiles.Sort((a, b) =>
                {
                    if (a.IsBootElf != b.IsBootElf) return a.IsBootElf ? -1 : 1;
                    return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
                });

                var response = new CommandResponse
                {
                    Success  = true,
                    Message  = $"Found {elfFiles.Count} ELF/IRX file(s).",
                    ElfFiles = elfFiles,
                    BootElf  = bootElfName,
                };
                return JsonSerializer.Serialize(response);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"LISTDISCELFS failed: {ex.Message}");
            }
        }

        // ── RECOMPILE ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Extract a named ELF from a disc image and run the static recompiler on it.
        /// </summary>
        /// <param name="isoPath">
        /// Path to the ISO-9660 disc image (plain ISO/BIN or a ZIP archive containing one).
        /// </param>
        /// <param name="elfName">
        /// Name of the ELF file on the disc (e.g. "SCUS_971.98" or "/SCUS_971.98;1").
        /// When <c>null</c> or empty, the BOOT2 executable from SYSTEM.CNF is used.
        /// </param>
        /// <param name="outputDirectory">
        /// Directory where the generated C++ files will be written.
        /// Defaults to <c>recomp_out</c> in the current directory when <c>null</c>.
        /// </param>
        private string HandleRecompile(string? isoPath, string? elfName, string? outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                return CreateErrorResponse("IsoPath is required.");

            if (!File.Exists(isoPath))
                return CreateErrorResponse($"ISO file not found: {isoPath}");

            try
            {
                using var stream = OpenDiscStream(isoPath);
                var cdvd = new CodeDesigner.Ps2.Emulator.IOP.Cdvd();
                cdvd.LoadFromStream(stream);

                // ── Resolve the ELF path ──────────────────────────────────────
                string resolvedElfName = elfName?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(resolvedElfName))
                {
                    // Fall back to BOOT2 from SYSTEM.CNF.
                    var cnfEntry = cdvd.FindFile("SYSTEM.CNF");
                    if (cnfEntry == null)
                        return CreateErrorResponse("SYSTEM.CNF not found on disc.");

                    byte[] cnfBytes = cdvd.ReadFile(cnfEntry.Value);
                    string cnfText  = System.Text.Encoding.Latin1.GetString(cnfBytes);
                    resolvedElfName = CodeDesigner.Ps2.Emulator.GameLoader.ParseSystemCnf(cnfText)
                        ?? string.Empty;

                    if (string.IsNullOrEmpty(resolvedElfName))
                        return CreateErrorResponse("BOOT2 entry not found in SYSTEM.CNF.");
                }

                // ── Find and read the ELF ─────────────────────────────────────
                var entry = cdvd.FindFile(resolvedElfName);
                if (entry == null)
                    return CreateErrorResponse($"ELF not found on disc: {resolvedElfName}");

                byte[] elfData = cdvd.ReadFile(entry.Value);
                if (elfData.Length == 0)
                    return CreateErrorResponse($"ELF is empty: {resolvedElfName}");

                // ── Run the recompiler ────────────────────────────────────────
                string outDir = string.IsNullOrWhiteSpace(outputDirectory) ? "recomp_out" : outputDirectory;

                var cfg = new CodeDesigner.Ps2.Recompiler.RecompilerConfig
                {
                    OutputDirectory  = outDir,
                    SingleFileOutput = true,
                    PatchSyscalls    = true,
                    PatchCache       = true,
                };

                var recompiler = new CodeDesigner.Ps2.Recompiler.Ps2StaticRecompiler(cfg);
                var progressLog = new List<string>();
                var result = recompiler.Recompile(elfData.AsSpan(), msg => progressLog.Add(msg));

                var response = new CommandResponse
                {
                    Success       = true,
                    Message       = $"Recompiled {result.Functions.Count} function(s) into {outDir}.",
                    FunctionCount = result.Functions.Count,
                    WrittenFiles  = result.WrittenFiles,
                    BootElf       = resolvedElfName,
                };
                return JsonSerializer.Serialize(response);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"RECOMPILE failed: {ex.Message}");
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

    /// <summary>
    /// Command message format for socket communication
    /// </summary>
    public class CommandMessage
    {
        public string Command { get; set; } = string.Empty;
        public string? Data { get; set; }
        public uint? Address { get; set; }
        public int? Length { get; set; }
        public string? Region { get; set; }
        public string? OutputPath { get; set; }
        public string? InputPath { get; set; }
        public uint? BreakpointAddress { get; set; }
        public string? Label { get; set; }
        public string? Comment { get; set; }
        public string? Query { get; set; }
        public string? SearchMode { get; set; }
        // SETREG fields
        public string? RegisterName { get; set; }
        public int? RegisterIndex { get; set; }
        public string? RegisterValue { get; set; }
        // LISTDISCELFS / RECOMPILE fields
        public string? IsoPath { get; set; }
        public string? ElfName { get; set; }
        public string? OutputDirectory { get; set; }
        // CONVERTCHEAT fields
        public string? Format { get; set; }
        public string? Direction { get; set; }
        // PORTCHEATS fields
        public string? SourceDumpPath { get; set; }
        public string? TargetDumpPath { get; set; }
        // MC_LIST / MC_EXPORT / MC_IMPORT / MC_DELETE / MC_FORMAT fields
        public string? McPath { get; set; }
        public string? SaveName { get; set; }
        public string? ExportDir { get; set; }
        public string? ImportDir { get; set; }
        public bool?   McOverwrite { get; set; }
        // Runtime mode control fields
        public string? Pcsx2Host { get; set; }
        public int? Pcsx2Slot { get; set; }
        public string? BiosPath { get; set; }
        public string? DiscPath { get; set; }
        public long? Steps { get; set; }
        // COMPILECDS — pre-resolved pointer-manager labels (dotted names like "seal.health")
        public List<LabelEntry>? Labels { get; set; }
        // New PINE extended command fields
        /// <summary>CPU selector for GETREGISTER/PINESETREG/LISTBP: 0=EE, 1=IOP, 0xFF=both.</summary>
        public byte? CpuSel { get; set; }
        /// <summary>Register category for GETREGISTER/PINESETREG.</summary>
        public byte? RegisterCategory { get; set; }
        /// <summary>Speed limiter mode for SETLIMITER: 0=Nominal, 1=Turbo, 2=Slomo, 3=Unlimited.</summary>
        public byte? LimiterMode { get; set; }
        /// <summary>Number of frames for FRAMEADVANCE.</summary>
        public byte? FrameCount { get; set; }
        /// <summary>Symbol name for GETSYMBOLBYNAME.</summary>
        public string? SymbolName { get; set; }
        /// <summary>File path for SAVESTATETOFILE / LOADSTATEFROMFILE.</summary>
        public string? FilePath { get; set; }
        /// <summary>Pagination offset for LISTFUNCTIONS / LISTGLOBALS.</summary>
        public uint? Offset { get; set; }
        /// <summary>Maximum result count for LISTFUNCTIONS / LISTGLOBALS.</summary>
        public ushort? MaxCount { get; set; }
    }

    /// <summary>
    /// Response format for socket communication
    /// </summary>
    public class CommandResponse
    {
        public bool Success { get; set; }
        public string? Command { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
        public string? Data { get; set; }
        public List<InstructionResult>? Results { get; set; }
        public List<SnapshotRegion>? Regions { get; set; }
        public bool? Paused { get; set; }
        public uint? PC { get; set; }
        public uint? BreakpointHitAddress { get; set; }

        // GETREGS fields
        public List<ulong>? GPR { get; set; }
        // Precise 32-bit halves of each 128-bit GPR (avoids JS floating-point loss for 64-bit values).
        // Lower integer lane: Lo32 = bits[31:0], Hi32 = bits[63:32] of RegsLo[i]
        // Upper SIMD lane:   UpLo32 = bits[31:0], UpHi32 = bits[63:32] of RegsHi[i]
        public List<uint>? GPR_Lo32 { get; set; }
        public List<uint>? GPR_Hi32 { get; set; }
        public List<uint>? GPR_UpLo32 { get; set; }
        public List<uint>? GPR_UpHi32 { get; set; }
        public ulong? GPR_PC { get; set; }
        public ulong? GPR_HI { get; set; }
        public ulong? GPR_LO { get; set; }
        public ulong? GPR_HI1 { get; set; }
        public ulong? GPR_LO1 { get; set; }
        public int? GPR_SA { get; set; }
        // Legacy COP0 convenience fields (kept for backward compatibility)
        public uint? COP0_Status { get; set; }
        public uint? COP0_Cause { get; set; }
        public ulong? COP0_EPC { get; set; }
        public uint? COP0_Count { get; set; }
        // Full COP0 register file (32 × 64-bit)
        public List<ulong>? COP0_Regs { get; set; }
        // COP1 FPR (32 × 32-bit, as raw IEEE-754 uint bit patterns)
        public List<uint>? FPR { get; set; }
        public uint? FPR_ACC { get; set; }
        // COP1 FCR control registers
        public uint? FCR0 { get; set; }
        public uint? FCR31 { get; set; }
        // VU0 float registers (32 × vec4, each component as raw IEEE-754 uint)
        public List<uint>? VU0F_X { get; set; }
        public List<uint>? VU0F_Y { get; set; }
        public List<uint>? VU0F_Z { get; set; }
        public List<uint>? VU0F_W { get; set; }
        // VU0 ACC accumulator (vec4)
        public uint? VU0F_ACC_X { get; set; }
        public uint? VU0F_ACC_Y { get; set; }
        public uint? VU0F_ACC_Z { get; set; }
        public uint? VU0F_ACC_W { get; set; }
        // VU0 Q/P/I special float registers
        public uint? VU0F_Q { get; set; }
        public uint? VU0F_P { get; set; }
        public uint? VU0F_I { get; set; }
        // VU0 integer registers (16 × 16-bit, stored as uint)
        public List<uint>? VU0I { get; set; }
        // VU0 flag registers
        public uint? VU0_Status { get; set; }
        public ushort? VU0_MAC { get; set; }
        public uint? VU0_Clip { get; set; }
        // GS privileged registers (PMODE, SMODE1/2, SRFSH, SYNCH1/2, SYNCV,
        //   DISPFB1/2, DISPLAY1/2, EXTBUF/DATA/WRITE, BGCOLOR, CSR, IMR, BUSDIR, SIGLBLID)
        public List<ulong>? GS_Priv { get; set; }
        // Symbol panel results
        public List<LabelEntry>?    Labels    { get; set; }
        public List<StringEntry>?   Strings   { get; set; }
        public List<FunctionEntry>? Functions { get; set; }
        public List<PointerEntry>?  Pointers  { get; set; }
        public List<FloatEntry>?    Floats    { get; set; }
        public List<IntegerEntry>?  Integers  { get; set; }
        public List<SearchMatch>?   Matches   { get; set; }
        // GETTHREADS
        public List<ThreadEntry>?   Threads   { get; set; }

        /// <summary>
        /// Per-address analysis comments produced when an instruction executes with
        /// the real register state (keyed by hex address string, e.g. "80001234").
        /// Populated by STEP to let the frontend update the analysis column
        /// without reloading the full disassembly page.
        /// </summary>
        public Dictionary<string, string>? InstructionAnalysis { get; set; }

        // LISTDISCELFS fields
        public List<DiscElfEntry>? ElfFiles { get; set; }
        public string? BootElf { get; set; }

        // RECOMPILE fields
        public int? FunctionCount { get; set; }
        public List<string>? WrittenFiles { get; set; }

        // PORTCHEATS fields
        public List<PortCheatResult>? PortCheatResults { get; set; }
        // MC_LIST field
        public List<McSaveInfo>? McSaves { get; set; }
        // New PINE extended command response fields
        public float? FPS { get; set; }
        public string? SymbolAddress { get; set; }
        public string? SymbolName { get; set; }
        public uint? SymbolSize { get; set; }
        public string? RegisterValue { get; set; }
        public uint? TotalSymbols { get; set; }
        public List<PineBreakpointInfo>?  Breakpoints  { get; set; }
        public List<PineStackFrameInfo>?  StackFrames  { get; set; }
        public List<IopModuleInfo>?       Modules      { get; set; }
        public List<PineSymbolInfo>?      PineSymbols  { get; set; }
        public List<PineLocalInfo>?       LocalVars    { get; set; }
    }

    /// <summary>Metadata for a single PS2 save-game directory returned by MC_LIST.</summary>
    public class McSaveInfo
    {
        public string Name       { get; set; } = string.Empty;
        public uint   FileCount  { get; set; }
        public string Created    { get; set; } = string.Empty;
        public string Modified   { get; set; } = string.Empty;
        public long   TotalBytes { get; set; }
        public List<McFileInfo> Files { get; set; } = [];
    }

    /// <summary>A single file entry within a PS2 save directory.</summary>
    public class McFileInfo
    {
        public string Name { get; set; } = string.Empty;
        public long   Size { get; set; }
    }

    /// <summary>
    /// Describes one memory region written by a SNAPSHOT command.
    /// </summary>
    public class SnapshotRegion
    {
        public string Label { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public uint BaseAddress { get; set; }
        public int Size { get; set; }
    }

    /// <summary>
    /// Describes one ELF (or IRX) file found on a disc image, returned by LISTDISCELFS.
    /// </summary>
    public class DiscElfEntry
    {
        /// <summary>Absolute disc path, e.g. "/SCUS_971.98;1" or "/MODULES/CDVD.IRX;1".</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>Filename without the path prefix or version suffix, e.g. "SCUS_971.98".</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>File size in bytes.</summary>
        public uint Size { get; set; }

        /// <summary>True when this is the BOOT2 executable listed in SYSTEM.CNF.</summary>
        public bool IsBootElf { get; set; }
    }

    /// <summary>
    /// Result for individual instruction assembly/disassembly
    /// </summary>
    public class InstructionResult
    {
        public bool Success { get; set; }
        public string? Assembly { get; set; }
        public string? Hex { get; set; }
        public string? Error { get; set; }
        public string? Comment { get; set; }  // Added for analyzer-generated comments
        public string? Label { get; set; }    // Symbol label from ps2MemMap.json
        public int? LineNumber { get; set; }  // 1-based source line number
    }

    /// <summary>One entry in the label/symbol database.</summary>
    public class LabelEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string  Address { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("label")]
        public string  Label   { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("comment")]
        public string? Comment { get; set; }
    }

    /// <summary>A null-terminated ASCII string found in the binary.</summary>
    public class StringEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public string Value   { get; set; } = string.Empty;
    }

    /// <summary>A detected function entry point.</summary>
    public class FunctionEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("label")]
        public string Label   { get; set; } = string.Empty;
    }

    /// <summary>A 4-byte aligned value that looks like a valid EE/IOP memory pointer.</summary>
    public class PointerEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address  { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("pointsTo")]
        public string PointsTo { get; set; } = string.Empty;
    }

    /// <summary>A plausible 32-bit IEEE-754 float found at a 4-byte aligned address.</summary>
    public class FloatEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public float  Value   { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("raw")]
        public string Raw     { get; set; } = string.Empty;
    }

    /// <summary>A non-zero 4-byte aligned 32-bit integer found in memory.</summary>
    public class IntegerEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public int    Value   { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("hex")]
        public string Hex     { get; set; } = string.Empty;
    }

    /// <summary>One match returned by the SEARCH command.</summary>
    public class SearchMatch
    {
        [System.Text.Json.Serialization.JsonPropertyName("instructionIndex")]
        public int InstructionIndex { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public uint Address { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("field")]
        public string Field { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("start")]
        public int Start { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("length")]
        public int Length { get; set; }
    }

    /// <summary>One EE OS thread entry returned by GETTHREADS.</summary>
    public class ThreadEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("pc")]
        public uint Pc { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("sp")]
        public uint Sp { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("priority")]
        public int Priority { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("entryPc")]
        public uint EntryPc { get; set; }
    }

    /// <summary>One breakpoint entry returned by LISTBP.</summary>
    public class PineBreakpointInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("cpu")]
        public string Cpu { get; set; } = string.Empty;
    }

    /// <summary>One call-stack frame returned by GETSTACK.</summary>
    public class PineStackFrameInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("entry")]
        public string Entry { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("pc")]
        public string PC { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("sp")]
        public string SP { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("stackSize")]
        public uint StackSize { get; set; }
    }

    /// <summary>One IOP module entry returned by GETMODULES.</summary>
    public class IopModuleInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("version")]
        public ushort Version { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("textAddr")]
        public string TextAddr { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("entry")]
        public string Entry { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("gp")]
        public string GP { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("textSize")]
        public uint TextSize { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dataSize")]
        public uint DataSize { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("bssSize")]
        public uint BssSize { get; set; }
    }

    /// <summary>One EE symbol entry returned by LISTFUNCTIONS / LISTGLOBALS.</summary>
    public class PineSymbolInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("size")]
        public uint Size { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>One local variable or parameter entry returned by GETLOCALS.</summary>
    public class PineLocalInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("storageType")]
        public string StorageType { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public int Value { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}




