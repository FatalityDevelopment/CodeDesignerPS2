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
using CodeDesigner.Mips64LE.CheatConverter;
using CodeDesigner.Mips64LE.Common;
using CodeDesigner.Mips64LE.Common.CheatEngine;
using CodeDesigner.Mips64LE.Common.Emulator;
using CodeDesigner.Mips64LE.Common.Emulator.Hardware;
using CodeDesigner.Mips64LE.Common.Emulator.IOP;

namespace CodeDesigner.Mips64LE
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

                    case "PING":
                        return CreateSuccessResponse("PONG");
                    
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
        /// Handle READMEM command — reads raw bytes from the live emulator memory.
        /// </summary>
        /// <param name="address">Start virtual address (EE or IOP address space).</param>
        /// <param name="length">Number of bytes to read (capped at MaxReadMemLength).</param>
        /// <param name="region">"ee" for EE bus, "iop" for IOP bus.</param>
        private string HandleReadMem(uint address, int length, string? region)
        {
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            if (length <= 0 || length > MaxReadMemLength)
                return CreateErrorResponse($"Invalid length {length} — must be 1–{MaxReadMemLength} bytes");

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);

            try
            {
                var bytes = new byte[length];

                for (int i = 0; i < length; i++)
                {
                    uint vaddr = address + (uint)i;

                    if (useIop)
                    {
                        // Translate IOP virtual address to a physical RAM offset.
                        // Supported windows:
                        //   0x00000000–0x003FFFFF  physical IOP RAM (direct)
                        //   0x80000000–0xBFFFFFFF  KSEG0/KSEG1 — strip top 3 bits
                        //   0x1C000000–0x1DFFFFFF  EE VTLB mirror of IOP RAM
                        uint physAddr = IopMemoryBus.TranslateAddress(vaddr);
                        if (physAddr >= EeIopMirrorBase && physAddr < EeIopMirrorEnd)
                            physAddr -= EeIopMirrorBase;

                        bytes[i] = physAddr < (uint)IopMemoryBus.RamSize
                            ? Scheduler.IOP.Bus.Ram[physAddr]
                            : (byte)0;
                    }
                    else
                    {
                        // EE: translate virtual address to physical.
                        // Supported windows:
                        //   0x80000000–0xBFFFFFFF  KSEG0/KSEG1 — strip top 3 bits
                        //   0x20000000–0x21FFFFFF  uncached user mirror of EE main RAM
                        //   0x70000000             scratchpad (handled below by vaddr)
                        uint physAddr;
                        if (vaddr >= 0x80000000u && vaddr < 0xC0000000u)
                            physAddr = vaddr & 0x1FFFFFFFu;
                        else if (vaddr >= EeUncachedRamBase && vaddr < EeUncachedRamEnd)
                            physAddr = vaddr - EeUncachedRamBase;
                        else
                            physAddr = vaddr;

                        if (physAddr < (uint)PS2MemoryBus.MainRamSize)
                            bytes[i] = Scheduler.EE.Bus.Ram[physAddr];
                        else if (physAddr >= PS2MemoryBus.BiosRomPhysBase &&
                                 physAddr <  PS2MemoryBus.BiosRomPhysBase + (uint)PS2MemoryBus.BiosRomSize)
                            bytes[i] = Scheduler.EE.Bus.Bios[physAddr - PS2MemoryBus.BiosRomPhysBase];
                        else if (vaddr >= 0x70000000u &&
                                 vaddr <  0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                            bytes[i] = Scheduler.EE.Bus.Scratchpad[vaddr - 0x70000000u];
                        else
                            bytes[i] = 0;
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
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            if (string.IsNullOrWhiteSpace(outputPath))
                return CreateErrorResponse("OutputPath is required for SNAPSHOT");

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
                return CreateErrorResponse($"Snapshot error: {ex.Message}");
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
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            if (string.IsNullOrWhiteSpace(inputPath))
                return CreateErrorResponse("InputPath is required for LOADSNAPSHOT");

            if (!Directory.Exists(inputPath))
                return CreateErrorResponse($"Snapshot directory not found: {inputPath}");

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
                return CreateErrorResponse($"LoadSnapshot error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle PAUSE command — halts emulator execution.
        /// </summary>
        private string HandlePause()
        {
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            Scheduler.Paused = true;
            return CreateSuccessResponse("Paused");
        }

        /// <summary>
        /// Handle RESUME command — resumes emulator execution.
        /// </summary>
        private string HandleResume()
        {
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            // If the emulator is paused at a breakpoint, execute the breakpoint
            // instruction once before releasing the run gate.  This advances PC
            // past the breakpoint address so the same breakpoint does not
            // immediately re-fire the moment execution resumes.
            // Snapshot the flag while the scheduler is paused (run-loop thread is
            // blocked on WaitForResumeAsync, so no concurrent write to LastBreakpointHit).
            bool wasAtBreakpoint = Scheduler.LastBreakpointHit.HasValue;
            if (wasAtBreakpoint)
                Scheduler.StepOne();

            Scheduler.Paused = false;
            return CreateSuccessResponse("Resumed");
        }

        /// <summary>
        /// Handle STEP command — executes exactly one EE instruction while
        /// keeping the emulator paused, then returns the new PC state.
        /// </summary>
        private string HandleStep()
        {
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
        /// </summary>
        private string HandleSetBreakpoint(uint? address)
        {
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            if (address == null)
                return CreateErrorResponse("BreakpointAddress is required for SETBP");
            uint normalized = NormalizeBreakpointToPhysical(address.Value);
            Scheduler.Breakpoints.Add(normalized);
            return CreateSuccessResponse($"Breakpoint set at 0x{normalized:X8}");
        }

        /// <summary>
        /// Handle CLEARBP command — removes the breakpoint at the given EE address.
        /// </summary>
        private string HandleClearBreakpoint(uint? address)
        {
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            if (address == null)
                return CreateErrorResponse("BreakpointAddress is required for CLEARBP");
            uint normalized = NormalizeBreakpointToPhysical(address.Value);
            Scheduler.Breakpoints.Remove(normalized);
            return CreateSuccessResponse($"Breakpoint cleared at 0x{normalized:X8}");
        }

        /// <summary>
        /// Handle CLEARBP_ALL command — removes all breakpoints.
        /// </summary>
        private string HandleClearAllBreakpoints()
        {
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            Scheduler.Breakpoints.Clear();
            return CreateSuccessResponse("All breakpoints cleared");
        }

        /// <summary>
        /// Handle GETPC command — returns current EE PC and paused/breakpoint state.
        /// The PC is normalized to KSEG0 (0x80000000–0x9FFFFFFF) so the client
        /// always sees a consistent address regardless of whether the CPU executed
        /// the instruction via the KSEG0 or KSEG1 virtual-address window.
        /// </summary>
        private string HandleGetPC()
        {
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            uint pc = NormalizePcToKseg0((uint)(Scheduler.EE.Cpu.PC & 0xFFFFFFFFUL));
            uint? bpHit = Scheduler.LastBreakpointHit.HasValue
                ? NormalizePcToKseg0(Scheduler.LastBreakpointHit.Value)
                : (uint?)null;
            var response = new CommandResponse
            {
                Success = true,
                Command = "GETPC",
                Paused  = Scheduler.Paused,
                PC      = pc,
                BreakpointHitAddress = bpHit,
            };
            return JsonSerializer.Serialize(response);
        }

        /// <summary>
        /// Handle GETREGS command — returns the full EE CPU register state including
        /// GPR, all 32 COP0 registers, FPR/FCR (COP1), VU0 float/integer registers,
        /// and key GS privileged registers.
        /// </summary>
        private string HandleGetRegisters()
        {
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

        /// <summary>
        /// Handle GETTHREADS command — returns EE OS thread information.
        /// Reads the current running thread from CPU state and attempts to walk
        /// the PS2 EE OS thread control block list in EE RAM.  Each TCB is 128
        /// bytes; status byte at offset 0: 0x01=running, 0x02=ready, 0x04=wait,
        /// 0x08=suspend, 0x10=dormant.  The thread table is located by reading
        /// the kernel's internal data starting at physical 0x00000000.
        /// </summary>
        private string HandleGetThreads()
        {
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
        /// </summary>
        private string HandleWriteMem(uint address, string? hexData, string? region)
        {
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            if (string.IsNullOrWhiteSpace(hexData))
                return CreateErrorResponse("No data provided");

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

            bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
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
                        Scheduler.IOP.Bus.Ram[physAddr] = bytes[i];
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
                        Scheduler.EE.Bus.Ram[physAddr] = bytes[i];
                    else if (vaddr >= 0x70000000u && vaddr < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize)
                        Scheduler.EE.Bus.Scratchpad[vaddr - 0x70000000u] = bytes[i];
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
        /// </summary>
        private string HandleApplyCheats(string? cheatText)
        {
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            if (string.IsNullOrWhiteSpace(cheatText))
                return CreateErrorResponse("No cheat data provided");

            try
            {
                var byteEditor = new PS2ByteEditor(Scheduler);
                var engine = new CodeDesigner.Mips64LE.Common.CheatEngine.CheatEngine(byteEditor);
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
        /// Handle PORTCHEATS command — scans two raw EE-RAM dump files and attempts to
        /// relocate a list of raw GameShark cheat codes from the source revision to the
        /// target revision using hex-pattern matching.
        ///
        /// Strategy: for each cheat address, extract a context window of bytes immediately
        /// BEFORE the target address from the source dump, then search the target dump for
        /// that same byte sequence.  The position where it matches, plus the window size,
        /// gives the new cheat address.  Three window sizes are attempted in order (48, 32,
        /// 16 bytes) so that a wider — and therefore more reliable — pattern is preferred.
        ///
        /// When the simple window approach fails or yields too many matches, three additional
        /// strategies are tried in order:
        ///   2. MIPS code-reference fingerprint: find LUI+load/store instruction sequences
        ///      that reference the cheat address, mask out the address-specific immediate
        ///      bytes, and search the target for the masked function fingerprint.
        ///   3. Extended anchor search: scan backwards up to 512 bytes for the nearest
        ///      non-trivial (non-zero) byte sequence, use it as an anchor, filter out
        ///      "static" matches (same position in both dumps), and pick from the remaining
        ///      dynamic candidates.
        ///   4. Pointer scan: search the source dump for 32-bit pointers that point to the
        ///      cheat address or its structural header, then follow those same pointer
        ///      locations in the target dump to derive the new address.
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
            List<CodeDesigner.Mips64LE.Common.CheatEngine.CheatBlock> blocks;
            try { blocks = cheatList.Parse(); }
            catch (Exception ex) { return CreateErrorResponse($"Failed to parse cheat codes: {ex.Message}"); }

            var allResults = new List<PortCheatResult>();
            var logLines   = new List<string>();
            logLines.Add($"[PORTCHEATS] Source: {sourcePath}");
            logLines.Add($"[PORTCHEATS] Target: {targetPath}");
            logLines.Add(string.Empty);

            // Window sizes to try, largest first (most reliable → least).
            int[] windowSizes = { 48, 32, 16 };

            foreach (var block in blocks)
            {
                string blockName = block.Label ?? string.Empty;
                logLines.Add($"--- {(string.IsNullOrWhiteSpace(blockName) ? "(unnamed block)" : blockName)} ---");

                foreach (var cheat in block.Cheats)
                {
                    // Physical address in the dump file: CheatBlock.Parse stores the lower
                    // 24 bits of the GameShark address field (e.g. 0x123456 for "20123456").
                    int physAddr   = cheat.Address;
                    string typeHex = ((int)cheat.CheatType).ToString("X2");
                    // Reconstruct the value hex (big-endian as written in the cheat file).
                    byte[] valBytes = (byte[])cheat.Data.Clone();
                    Array.Reverse(valBytes);
                    string valueHex = BitConverter.ToString(valBytes).Replace("-", "");
                    string origCode = $"{typeHex}{physAddr:X6} {valueHex}";

                    logLines.Add($"  Code: {origCode}  (physical 0x{physAddr:X6})");

                    var result = new PortCheatResult
                    {
                        BlockName    = blockName,
                        OriginalCode = origCode,
                    };

                    // ── Strategy 1: simple before-window (existing) ──────────────────────
                    bool foundAny = false;
                    foreach (int windowSize in windowSizes)
                    {
                        int patternStart = physAddr - windowSize;
                        if (patternStart < 0 || physAddr >= sourceData.Length)
                        {
                            logLines.Add($"    [S1 {windowSize}B window] Address out of range — skipping");
                            continue;
                        }

                        int actualWindow = physAddr - patternStart;
                        if (actualWindow <= 0) continue;

                        byte[] pattern = new byte[actualWindow];
                        Array.Copy(sourceData, patternStart, pattern, 0, actualWindow);

                        // Skip windows that are all-zero — they match everywhere.
                        bool allZero = pattern.All(b => b == 0);
                        if (allZero)
                        {
                            logLines.Add($"    [S1 {windowSize}B window] All-zero pattern — skipping");
                            continue;
                        }

                        logLines.Add($"    [S1] Trying HexPattern-{actualWindow}: scanning {targetData.Length / 1024 / 1024} MB…");
                        var matches = FindPattern(targetData, pattern);

                        if (matches.Count == 0)
                        {
                            logLines.Add($"    [S1] HexPattern-{actualWindow}: no match");
                            continue;
                        }

                        // Filter out static matches (same bytes at same position in source too).
                        var dynamicMatches = matches
                            .Where(m => m + actualWindow <= sourceData.Length &&
                                        !sourceData.AsSpan(m, actualWindow).SequenceEqual(pattern))
                            .ToList();

                        var usefulMatches = dynamicMatches.Count > 0 ? dynamicMatches : matches;

                        foundAny = true;
                        bool ambiguous = usefulMatches.Count > 1;
                        int  bestMatch = usefulMatches[0] + actualWindow;

                        string newAddrHex = $"{typeHex}{bestMatch:X6}";
                        string newCode    = $"{newAddrHex} {valueHex}";

                        result.Found      = true;
                        result.NewCode    = newCode;
                        result.Method     = $"HexPattern-{actualWindow}" + (dynamicMatches.Count > 0 ? "-filtered" : "");
                        result.Confidence = ambiguous ? Math.Max(20, 100 - (usefulMatches.Count - 1) * 25) : 100;
                        result.Ambiguous  = ambiguous;

                        if (ambiguous)
                        {
                            result.Candidates = usefulMatches
                                .Take(8)
                                .Select(m => $"{typeHex}{(m + actualWindow):X6} {valueHex}")
                                .ToList();
                            logLines.Add($"    [S1] HexPattern-{actualWindow}: {usefulMatches.Count} candidates — best: 0x{bestMatch:X6}");
                            foreach (var m in usefulMatches.Take(8))
                                logLines.Add($"      candidate 0x{m + actualWindow:X6}");
                        }
                        else
                        {
                            logLines.Add($"    [S1] HexPattern-{actualWindow}: unique match → 0x{bestMatch:X6}  ✓");
                        }

                        break; // Stop at the first window size that gives a result.
                    }

                    // ── Strategy 2: MIPS code-reference fingerprint ──────────────────────
                    if (!foundAny || result.Confidence < 100)
                    {
                        logLines.Add($"    [S2] Trying MIPS code-reference fingerprint…");
                        var codeRefs = FindMipsCodeReferences(sourceData, physAddr, logLines);
                        if (codeRefs.Count > 0)
                        {
                            logLines.Add($"    [S2] Found {codeRefs.Count} code reference(s) — building fingerprints");
                            foreach (var cref in codeRefs)
                            {
                                // Build a masked fingerprint centred on the LUI instruction.
                                int fpBefore  = Math.Min(cref.LuiPos, 32);
                                int fpAfter   = Math.Min(sourceData.Length - cref.RefPos, 16);
                                int fpStart   = cref.LuiPos - fpBefore;
                                int fpLen     = (cref.RefPos + fpAfter) - fpStart;
                                if (fpLen <= 0 || fpStart + fpLen > sourceData.Length) continue;

                                byte[] fp   = new byte[fpLen];
                                byte[] mask = new byte[fpLen];
                                Array.Copy(sourceData, fpStart, fp, 0, fpLen);
                                Array.Fill(mask, (byte)0xFF);

                                // Mask the load/store offset bytes (bits [15:0] = bytes 0–1 in LE).
                                int refInFp = cref.RefPos - fpStart;
                                mask[refInFp]     = 0;
                                mask[refInFp + 1] = 0;
                                // Also mask the LUI immediate (bytes 0–1) so it still matches
                                // even when the high-word happens to differ.
                                int luiInFp = cref.LuiPos - fpStart;
                                mask[luiInFp]     = 0;
                                mask[luiInFp + 1] = 0;

                                var fpMatches = FindMaskedPattern(targetData, fp, mask, step: 4);
                                if (fpMatches.Count == 0)
                                {
                                    logLines.Add($"    [S2] Fingerprint-{fpLen}B: no match");
                                    continue;
                                }

                                var candidates = new List<int>();
                                foreach (int fm in fpMatches)
                                {
                                    // Decode the new load offset from the matched target code.
                                    int matchRefPos = fm + refInFp;
                                    if (matchRefPos + 2 > targetData.Length) continue;
                                    int newLow16 = targetData[matchRefPos] | (targetData[matchRefPos + 1] << 8);

                                    // Decode LUI immediate from the matched target code.
                                    int matchLuiPos = fm + luiInFp;
                                    if (matchLuiPos + 4 > targetData.Length) continue;
                                    int newLuiImm = targetData[matchLuiPos] | (targetData[matchLuiPos + 1] << 8);

                                    // Reconstruct: (LUI_imm << 16) + sign_extend(low16)
                                    int newAddr = (newLuiImm << 16) + (short)newLow16;
                                    if (newAddr > 0 && newAddr < targetData.Length)
                                        candidates.Add(newAddr);
                                }

                                if (candidates.Count == 0) continue;

                                bool ambig   = candidates.Count > 1;
                                int  best    = candidates[0];
                                string nc    = $"{typeHex}{best:X6} {valueHex}";
                                int   conf   = ambig ? Math.Max(40, 100 - (candidates.Count - 1) * 30) : 100;

                                // Prefer this result if it's better than what we have.
                                if (!foundAny || conf > result.Confidence)
                                {
                                    foundAny          = true;
                                    result.Found      = true;
                                    result.NewCode    = nc;
                                    result.Method     = $"CodeRef-{fpLen}B";
                                    result.Confidence = conf;
                                    result.Ambiguous  = ambig;
                                    result.Candidates = ambig
                                        ? candidates.Take(8).Select(a => $"{typeHex}{a:X6} {valueHex}").ToList()
                                        : null;

                                    if (ambig)
                                        logLines.Add($"    [S2] Fingerprint-{fpLen}B: {candidates.Count} candidates — best 0x{best:X6}");
                                    else
                                        logLines.Add($"    [S2] Fingerprint-{fpLen}B: unique match → 0x{best:X6}  ✓");
                                }

                                if (result.Confidence == 100) break;
                            }
                        }
                        else
                        {
                            logLines.Add($"    [S2] No direct MIPS code references found");
                        }
                    }

                    // ── Strategy 3: extended anchor search ──────────────────────────────
                    if (!foundAny || result.Confidence < 100)
                    {
                        logLines.Add($"    [S3] Trying extended anchor search (up to 512B scan-back)…");
                        var anchorCandidates = FindAnchorCandidates(sourceData, targetData, physAddr, logLines);

                        if (anchorCandidates.Count > 0)
                        {
                            int  best = anchorCandidates[0].Address;
                            bool ambig = anchorCandidates.Count > 1;
                            int  conf  = anchorCandidates[0].Confidence;
                            string nc  = $"{typeHex}{best:X6} {valueHex}";

                            if (!foundAny || conf > result.Confidence)
                            {
                                foundAny          = true;
                                result.Found      = true;
                                result.NewCode    = nc;
                                result.Method     = anchorCandidates[0].Method;
                                result.Confidence = conf;
                                result.Ambiguous  = ambig;
                                result.Candidates = ambig
                                    ? anchorCandidates.Take(8).Select(c => $"{typeHex}{c.Address:X6} {valueHex}").ToList()
                                    : null;

                                if (ambig)
                                    logLines.Add($"    [S3] {anchorCandidates.Count} candidates — best 0x{best:X6} (confidence {conf}%)");
                                else
                                    logLines.Add($"    [S3] Unique anchor match → 0x{best:X6}  ✓");
                            }
                        }
                        else
                        {
                            logLines.Add($"    [S3] No anchor match found");
                        }
                    }

                    // ── Strategy 4: pointer scan ─────────────────────────────────────────
                    if (!foundAny || result.Confidence < 100)
                    {
                        logLines.Add($"    [S4] Trying pointer scan…");
                        var ptrCandidates = FindViaPointerScan(sourceData, targetData, physAddr, logLines);

                        if (ptrCandidates.Count > 0)
                        {
                            int  best = ptrCandidates[0].Address;
                            bool ambig = ptrCandidates.Count > 1;
                            int  conf  = ptrCandidates[0].Confidence;
                            string nc  = $"{typeHex}{best:X6} {valueHex}";

                            if (!foundAny || conf > result.Confidence)
                            {
                                foundAny          = true;
                                result.Found      = true;
                                result.NewCode    = nc;
                                result.Method     = ptrCandidates[0].Method;
                                result.Confidence = conf;
                                result.Ambiguous  = ambig;
                                result.Candidates = ambig
                                    ? ptrCandidates.Take(8).Select(c => $"{typeHex}{c.Address:X6} {valueHex}").ToList()
                                    : null;

                                if (ambig)
                                    logLines.Add($"    [S4] {ptrCandidates.Count} pointer candidates — best 0x{best:X6} (confidence {conf}%)");
                                else
                                    logLines.Add($"    [S4] Unique pointer match → 0x{best:X6}  ✓");
                            }
                        }
                        else
                        {
                            logLines.Add($"    [S4] No pointer match found");
                        }
                    }

                    if (!foundAny)
                    {
                        result.Method = "No match — all strategies exhausted";
                        logLines.Add($"    Result: NOT FOUND ✗");
                    }
                    else
                    {
                        logLines.Add($"    Result: {result.NewCode}  (confidence {result.Confidence}%, method: {result.Method})");
                    }

                    allResults.Add(result);
                }

                logLines.Add(string.Empty);
            }

            int foundCount = allResults.Count(r => r.Found);
            logLines.Add($"[PORTCHEATS] Complete: {foundCount}/{allResults.Count} codes ported.");

            var response = new CommandResponse
            {
                Success          = true,
                Command          = "PORTCHEATS",
                Data             = string.Join("\n", logLines),
                PortCheatResults = allResults,
            };
            return JsonSerializer.Serialize(response);
        }

        // ════════════════════════════════════════════════════════════════════════
        // PORTCHEATS helper types
        // ════════════════════════════════════════════════════════════════════════

        private sealed class PortCheatCandidate
        {
            public int    Address    { get; set; }
            public int    Confidence { get; set; }
            public string Method     { get; set; } = string.Empty;
        }

        private sealed class MipsCodeRef
        {
            /// <summary>Byte position of the LUI instruction in the source dump.</summary>
            public int LuiPos { get; set; }
            /// <summary>Byte position of the load/store/addiu that uses the address offset.</summary>
            public int RefPos { get; set; }
        }

        // ════════════════════════════════════════════════════════════════════════
        // Strategy 2: MIPS code-reference fingerprint
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Scans <paramref name="sourceData"/> for MIPS LUI + load/store/addiu instruction
        /// pairs that together reference <paramref name="physAddr"/>.  All standard PS2 EE
        /// virtual address spaces are tried (user, KSEG0, KSEG1).
        /// </summary>
        private static List<MipsCodeRef> FindMipsCodeReferences(
            byte[] sourceData, int physAddr, IList<string> logLines)
        {
            var refs = new List<MipsCodeRef>();

            // Three PS2 EE virtual address spaces:
            //   0x00000000 – user mode (application code, mapped 1:1 to physical)
            //   0x80000000 – KSEG0 (kernel cached, physical = vaddr & 0x1FFFFFFF)
            //   0xA0000000 – KSEG1 (kernel uncached, physical = vaddr & 0x1FFFFFFF)
            // Many games reference EE RAM through KSEG0; device drivers use KSEG1.
            int[] vbases = { 0x00000000, unchecked((int)0x80000000), unchecked((int)0xA0000000) };

            foreach (int vbase in vbases)
            {
                uint vaddr  = unchecked((uint)(vbase + physAddr));
                int  low16  = (int)(vaddr & 0xFFFF);
                int  high16 = (int)((vaddr >> 16) & 0xFFFF);
                // MIPS assemblers correct for the sign-extension applied by load/store
                // immediates: if the low 16 bits would be interpreted as negative (≥ 0x8000),
                // the assembler increments the LUI immediate by 1 so that the runtime sum
                // (LUI_result + sign_extend(imm)) still equals the intended address.
                int luiImm = (high16 + (low16 >= 0x8000 ? 1 : 0)) & 0xFFFF;

                byte luiLo = (byte)(luiImm & 0xFF);
                byte luiHi = (byte)((luiImm >> 8) & 0xFF);
                byte offLo = (byte)(low16 & 0xFF);
                byte offHi = (byte)((low16 >> 8) & 0xFF);

                // Scan 4-byte-aligned positions for LUI instructions.
                for (int i = 0; i <= sourceData.Length - 4; i += 4)
                {
                    // LUI in little-endian: [imm_lo][imm_hi][reg][0x3C]
                    if (sourceData[i] != luiLo || sourceData[i + 1] != luiHi || sourceData[i + 3] != 0x3C)
                        continue;

                    // Look forward up to 256 bytes for a load/store/addiu using the offset.
                    int limit = Math.Min(sourceData.Length - 4, i + 256);
                    for (int j = i + 4; j <= limit; j += 4)
                    {
                        if (sourceData[j] != offLo || sourceData[j + 1] != offHi) continue;

                        // In little-endian MIPS encoding byte[3] is the most-significant byte
                        // of the 32-bit instruction word; the opcode occupies its top 6 bits
                        // (bits [31:26] of the word), so opcode = byte[3] >> 2.
                        // We accept I-type immediate instructions: ALU immediates (0x08–0x0F),
                        // loads (0x20–0x27), and stores (0x28–0x2F).
                        int op = sourceData[j + 3] >> 2;
                        if ((op >= 0x08 && op <= 0x0F) || (op >= 0x20 && op <= 0x2F))
                        {
                            refs.Add(new MipsCodeRef { LuiPos = i, RefPos = j });
                            logLines.Add($"    [S2]   Found: LUI@0x{i:X} + ref@0x{j:X} (vbase=0x{(uint)vbase:X8})");
                        }
                        break;
                    }

                    if (refs.Count >= 8) break; // Enough candidates.
                }

                if (refs.Count >= 8) break;
            }

            return refs;
        }

        // ════════════════════════════════════════════════════════════════════════
        // Strategy 3: extended anchor search
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Scans backwards from <paramref name="physAddr"/> up to 512 bytes to find the
        /// nearest meaningful (non-trivially-zero) byte run, then uses that run as an anchor
        /// pattern to locate the address in the target dump.  "Static" matches — those that
        /// appear at the same position in both dumps — are filtered out, leaving only the
        /// candidates that reflect the actual code-relocation between versions.
        /// When multiple dynamic candidates remain they are ranked by proximity to the
        /// source address.
        /// </summary>
        private static List<PortCheatCandidate> FindAnchorCandidates(
            byte[] sourceData, byte[] targetData, int physAddr, IList<string> logLines)
        {
            if (physAddr <= 0 || physAddr >= sourceData.Length)
                return new List<PortCheatCandidate>();

            const int MaxScanBack    = 512;
            const int MinPatternLen  = 8;    // Anchor must have at least this many bytes.
            const int MinNonZeroBytes = 4;   // Pattern must contain at least this many non-zero bytes.

            // Try progressively larger scan-back distances.
            for (int scanBack = MinPatternLen; scanBack <= MaxScanBack; scanBack += 4)
            {
                int anchorStart = physAddr - scanBack;
                if (anchorStart < 0) break;

                byte[] pattern = new byte[scanBack];
                Array.Copy(sourceData, anchorStart, pattern, 0, scanBack);

                // Count non-zero bytes — skip trivial all-zero windows.
                int nonZeroCount = pattern.Count(b => b != 0);
                if (nonZeroCount < MinNonZeroBytes) continue;

                var rawMatches = FindPattern(targetData, pattern);
                if (rawMatches.Count == 0) continue;

                // Filter static matches: positions where the source also has the same bytes.
                var dynamicMatches = rawMatches
                    .Where(m => m + scanBack <= sourceData.Length &&
                                !sourceData.AsSpan(m, scanBack).SequenceEqual(pattern.AsSpan()))
                    .ToList();

                // If all matches are static skip this window; the non-zero bytes are shared
                // verbatim between versions (e.g. static library data) and not useful for
                // locating the relocated address.
                if (dynamicMatches.Count == 0) continue;

                // Sort dynamic candidates by distance from the source address so that
                // the closest relocation is ranked first.  Use CompareTo on the absolute
                // distances to avoid integer-overflow when the dump is large.
                dynamicMatches.Sort((a, b) =>
                    Math.Abs((a + scanBack) - physAddr).CompareTo(Math.Abs((b + scanBack) - physAddr)));

                int conf = dynamicMatches.Count == 1
                    ? 100
                    : Math.Max(20, 100 - (dynamicMatches.Count - 1) * 25);

                logLines.Add($"    [S3] AnchorSearch-{scanBack}B: {dynamicMatches.Count} dynamic candidate(s)");

                return dynamicMatches
                    .Take(8)
                    .Select(m => new PortCheatCandidate
                    {
                        Address    = m + scanBack,
                        Confidence = conf,
                        Method     = $"AnchorSearch-{scanBack}B",
                    })
                    .ToList();
            }

            return new List<PortCheatCandidate>();
        }

        // ════════════════════════════════════════════════════════════════════════
        // Strategy 4: pointer scan
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Searches the source dump for 32-bit little-endian pointers that reference
        /// <paramref name="physAddr"/> directly or its structural header (physAddr minus a
        /// small fixed offset found by scanning backwards).  For each such pointer location
        /// the same 4-byte slot is read from the target dump; the resulting pointer value
        /// plus the same header-relative offset gives the candidate target address.
        ///
        /// This strategy is highly reliable when the two dumps share a table or array of
        /// per-version pointers at a stable location (e.g. a module's pad-port table).
        /// </summary>
        private static List<PortCheatCandidate> FindViaPointerScan(
            byte[] sourceData, byte[] targetData, int physAddr, IList<string> logLines)
        {
            var candidates = new List<PortCheatCandidate>();

            // Build a set of candidate "reference values" to look for as pointers:
            // the address itself plus several header offsets found by scanning backwards
            // for the nearest non-zero 4-byte-aligned value.
            var refValues = new List<(int refVal, int headerOffset)>();
            refValues.Add((physAddr, 0)); // direct pointer to the address

            // Scan backwards for non-zero 4-byte values within 64 bytes.
            for (int back = 4; back <= 64; back += 4)
            {
                int scanPos = physAddr - back;
                if (scanPos < 0 || scanPos + 4 > sourceData.Length) break;
                int word = sourceData[scanPos]
                         | (sourceData[scanPos + 1] << 8)
                         | (sourceData[scanPos + 2] << 16)
                         | (sourceData[scanPos + 3] << 24);
                if (word != 0)
                {
                    // The non-zero word at physAddr-back could be the structure header.
                    // Record a pointer to that header position.
                    refValues.Add((scanPos, back));
                    if (refValues.Count >= 4) break; // enough anchors
                }
            }

            var seen = new HashSet<int>();

            foreach (var (refVal, headerOffset) in refValues)
            {
                // Build the 4-byte LE representation of the pointer value.
                byte[] needle = new byte[4];
                needle[0] = (byte)( refVal        & 0xFF);
                needle[1] = (byte)((refVal >>  8) & 0xFF);
                needle[2] = (byte)((refVal >> 16) & 0xFF);
                needle[3] = (byte)((refVal >> 24) & 0xFF);

                // Search source dump for this pointer (not necessarily 4-byte aligned).
                var ptrLocs = FindPattern(sourceData, needle);
                if (ptrLocs.Count == 0) continue;
                if (ptrLocs.Count > 32) continue; // too common — unreliable

                logLines.Add($"    [S4] Found {ptrLocs.Count} pointer(s) to 0x{refVal:X} (hdrOffset={headerOffset})");

                foreach (int loc in ptrLocs)
                {
                    if (loc + 4 > targetData.Length) continue;

                    // Read the pointer value at the same location in the target dump.
                    int tgtPtr = targetData[loc]
                               | (targetData[loc + 1] << 8)
                               | (targetData[loc + 2] << 16)
                               | (targetData[loc + 3] << 24);

                    if (tgtPtr == 0) continue;

                    // Derive candidate target address.
                    int candidateAddr = tgtPtr + headerOffset;
                    if (candidateAddr <= 0 || candidateAddr >= targetData.Length) continue;
                    if (!seen.Add(candidateAddr)) continue;

                    logLines.Add($"    [S4]   ptr@0x{loc:X}: src=0x{refVal:X} → tgt=0x{tgtPtr:X} → addr=0x{candidateAddr:X}");
                    candidates.Add(new PortCheatCandidate
                    {
                        Address    = candidateAddr,
                        Confidence = ptrLocs.Count == 1 ? 100 : Math.Max(30, 100 - (ptrLocs.Count - 1) * 10),
                        Method     = $"PtrScan-hdr{headerOffset}",
                    });
                }
            }

            // Sort by confidence descending, then by proximity to source address.
            candidates.Sort((a, b) =>
            {
                int cmpConf = b.Confidence.CompareTo(a.Confidence);
                if (cmpConf != 0) return cmpConf;
                return Math.Abs(a.Address - physAddr).CompareTo(Math.Abs(b.Address - physAddr));
            });

            return candidates;
        }

        // ════════════════════════════════════════════════════════════════════════
        // Low-level pattern search helpers
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Searches <paramref name="haystack"/> for every occurrence of
        /// <paramref name="needle"/> using a simple sliding-window scan and returns
        /// the starting byte offset of each match.  Suitable for the dump sizes
        /// encountered in practice (≤ 32 MB); performance is acceptable because the
        /// inner comparison exits early on the first mismatch.
        /// </summary>
        private static List<int> FindPattern(byte[] haystack, byte[] needle)
        {
            var results = new List<int>();
            if (needle.Length == 0 || needle.Length > haystack.Length) return results;

            int limit = haystack.Length - needle.Length;
            byte first = needle[0];

            for (int i = 0; i <= limit; i++)
            {
                if (haystack[i] != first) continue;
                bool match = true;
                for (int j = 1; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) results.Add(i);
            }
            return results;
        }

        /// <summary>
        /// Searches <paramref name="haystack"/> for every occurrence of
        /// <paramref name="pattern"/> where the corresponding <paramref name="mask"/>
        /// byte is 0xFF (must match exactly) or 0x00 (wildcard — any byte accepted).
        /// </summary>
        /// <param name="step">Alignment step for the outer loop (4 for 4-byte-aligned code).</param>
        private static List<int> FindMaskedPattern(byte[] haystack, byte[] pattern, byte[] mask, int step = 1)
        {
            var results = new List<int>();
            if (pattern.Length == 0 || pattern.Length > haystack.Length) return results;

            // Find the first non-zero mask byte for a fast pre-filter.
            int firstMaskIdx = Array.FindIndex(mask, m => m != 0);
            if (firstMaskIdx < 0) return results; // All wildcards — useless.

            byte firstMaskVal = pattern[firstMaskIdx];
            int  limit        = haystack.Length - pattern.Length;

            for (int i = 0; i <= limit; i += step)
            {
                if (haystack[i + firstMaskIdx] != firstMaskVal) continue;
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (mask[j] != 0 && haystack[i + j] != pattern[j]) { match = false; break; }
                }
                if (match) results.Add(i);
            }
            return results;
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
        /// values that look like valid EE memory pointers.
        /// </summary>
        private string HandleGetPointers(uint startAddress, string? base64Data)
        {
            if (string.IsNullOrEmpty(base64Data))
                return CreateErrorResponse("Data (base64 binary) is required for GETPOINTERS");

            try
            {
                var bytes   = Convert.FromBase64String(base64Data);
                var results = new List<PointerEntry>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint val = BitConverter.ToUInt32(bytes, i);
                    uint physEquiv = val;
                    if (val >= 0x80000000u && val < 0xC0000000u)
                        physEquiv = val & 0x1FFFFFFFu;
                    bool isValid =
                        physEquiv < (uint)PS2MemoryBus.MainRamSize ||
                        (val >= 0x70000000u && val < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize);

                    if (isValid)
                    {
                        results.Add(new PointerEntry
                        {
                            Address  = $"0x{(startAddress + (uint)i):X8}",
                            PointsTo = $"0x{val:X8}"
                        });
                    }
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
        /// directly from the live emulator RAM arrays.  Used by SCANSTRINGS,
        /// SCANFUNCTIONS, and SEARCH so they can scan whole regions without the
        /// 64 KB cap that READMEM enforces.
        /// </summary>
        private byte[] ReadLiveBytes(uint startAddress, int length, bool useIop)
        {
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
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
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
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
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
        /// memory and returns 4-byte aligned values that look like valid EE or IOP
        /// memory pointers.  For EE scans the valid target ranges are:
        /// main RAM (physical 0x00000000–0x01FFFFFF), KSEG0 (0x80000000–0x81FFFFFF),
        /// KSEG1 (0xA0000000–0xA1FFFFFF), and scratchpad (0x70000000–0x70003FFF).
        /// For IOP scans the valid range is IOP RAM (0x00000000–0x001FFFFF).
        /// </summary>
        private string HandleScanPointers(uint address, int length, string? region)
        {
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            if (length <= 0)
                return CreateErrorResponse("Length is required for SCANPOINTERS");

            try
            {
                bool useIop = string.Equals(region, "iop", StringComparison.OrdinalIgnoreCase);
                var bytes   = ReadLiveBytes(address, length, useIop);
                var results = new List<PointerEntry>();

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint val = BitConverter.ToUInt32(bytes, i);
                    bool isValid;
                    if (useIop)
                    {
                        // IOP: plain RAM range
                        isValid = val < (uint)IopMemoryBus.RamSize;
                    }
                    else
                    {
                        // EE: main RAM, KSEG0 mirror, KSEG1 mirror, or scratchpad
                        uint physEquiv = val;
                        if (val >= 0x80000000u && val < 0xC0000000u)
                            physEquiv = val & 0x1FFFFFFFu;
                        isValid =
                            physEquiv < (uint)PS2MemoryBus.MainRamSize ||
                            (val >= 0x70000000u && val < 0x70000000u + (uint)PS2MemoryBus.ScratchpadSize);
                    }

                    if (isValid)
                    {
                        results.Add(new PointerEntry
                        {
                            Address  = $"0x{(address + (uint)i):X8}",
                            PointsTo = $"0x{val:X8}"
                        });
                    }
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
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
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
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
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
                    if (Scheduler == null)
                        return CreateErrorResponse("Emulator not running — no scheduler attached");
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
                var cdvd = new CodeDesigner.Mips64LE.Common.Emulator.IOP.Cdvd();
                cdvd.LoadFromStream(stream);

                // ── Read SYSTEM.CNF to discover the BOOT2 executable ──────────
                string? bootElfName = null;
                var cnfEntry = cdvd.FindFile("SYSTEM.CNF");
                if (cnfEntry != null)
                {
                    byte[] cnfBytes = cdvd.ReadFile(cnfEntry.Value);
                    string cnfText  = System.Text.Encoding.Latin1.GetString(cnfBytes);
                    string? rawBoot = CodeDesigner.Mips64LE.Common.Emulator.GameLoader.ParseSystemCnf(cnfText);
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
                var cdvd = new CodeDesigner.Mips64LE.Common.Emulator.IOP.Cdvd();
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
                    resolvedElfName = CodeDesigner.Mips64LE.Common.Emulator.GameLoader.ParseSystemCnf(cnfText)
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

                var cfg = new CodeDesigner.Mips64LE.Recompiler.RecompilerConfig
                {
                    OutputDirectory  = outDir,
                    SingleFileOutput = true,
                    PatchSyscalls    = true,
                    PatchCache       = true,
                };

                var recompiler = new CodeDesigner.Mips64LE.Recompiler.Ps2StaticRecompiler(cfg);
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
        // COMPILECDS — pre-resolved pointer-manager labels (dotted names like "seal.health")
        public List<LabelEntry>? Labels { get; set; }
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
    }

    /// <summary>
    /// Result for a single cheat code line produced by PORTCHEATS.
    /// </summary>
    public class PortCheatResult
    {
        /// <summary>The #Name label of the cheat block this line belongs to.</summary>
        public string BlockName { get; set; } = string.Empty;

        /// <summary>Original GameShark code line, e.g. "20123456 000003E8".</summary>
        public string OriginalCode { get; set; } = string.Empty;

        /// <summary>Ported code line with the updated address, e.g. "20124ABC 000003E8". Null when not found.</summary>
        public string? NewCode { get; set; }

        /// <summary>Whether a match was found in the target dump.</summary>
        public bool Found { get; set; }

        /// <summary>Description of the method and window size used, e.g. "HexPattern-48".</summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>0–100 confidence score. 100 = unique match; lower when multiple candidates were found.</summary>
        public int Confidence { get; set; }

        /// <summary>True when more than one candidate address was found in the target dump.</summary>
        public bool Ambiguous { get; set; }

        /// <summary>All candidate new code lines when Ambiguous is true.</summary>
        public List<string>? Candidates { get; set; }
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
}
