using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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

        /// <summary>Matches a label reference inside an instruction operand: ":Name".</summary>
        private static readonly System.Text.RegularExpressions.Regex CdsLabelRefRegex =
            new(@":([A-Za-z_][A-Za-z0-9_]*)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Matches an absolute-address label reference: "@Name".
        /// Replaced with the raw hex address of the label (e.g. 0x80010018),
        /// suitable for use with jump instructions or absolute address operands.
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex CdsAbsLabelRefRegex =
            new(@"@([A-Za-z_][A-Za-z0-9_]*)",
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
                        return HandleCompileCds(command.Data);
                    
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
        private string HandleCompileCds(string? sourceCode)
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

                        results.Add(new InstructionResult
                        {
                            Hex = $"0x{instruction:X8}",
                            Assembly = disasm,
                            Success = true
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
                    (Label: "IOP RAM",      File: "IOPmem.bin",    Bytes: Scheduler.IOP.Bus.Ram,       Base: 0x00000000u),
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
                    (File: "IOPmem.bin",    Label: "IOP RAM",      Bytes: Scheduler.IOP.Bus.Ram,       MaxSize: Scheduler.IOP.Bus.Ram.Length,   Base: 0x00000000u),
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
                    uint physAddr = IopMemoryBus.TranslateAddress(vaddr);
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
        /// Open an ISO-9660 disc image, walk all files, and return a list of every
        /// ELF or IRX file found.  Also reads SYSTEM.CNF to mark the BOOT2
        /// executable with <see cref="DiscElfEntry.IsBootElf"/> = true.
        /// </summary>
        private string HandleListDiscElfs(string? isoPath)
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                return CreateErrorResponse("IsoPath is required.");

            if (!File.Exists(isoPath))
                return CreateErrorResponse($"ISO file not found: {isoPath}");

            try
            {
                using var stream = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
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
                var elfFiles = new List<DiscElfEntry>();
                foreach (var (discPath, entry) in cdvd.EnumerateFiles())
                {
                    string nameUpper = System.IO.Path.GetFileNameWithoutExtension(
                        entry.Name.TrimEnd(';', '1', '2', '3', '4', '5', '6', '7', '8', '9', '0')
                            .TrimEnd(';')).ToUpperInvariant();
                    string extRaw = System.IO.Path.GetExtension(entry.Name);
                    // Strip version suffix from extension, e.g. ".ELF;1" → ".ELF"
                    int semiExt = extRaw.IndexOf(';');
                    string ext = (semiExt >= 0 ? extRaw[..semiExt] : extRaw).ToUpperInvariant();

                    if (ext != ".ELF" && ext != ".IRX")
                        continue;

                    string bareName = System.IO.Path.GetFileName(discPath);
                    int semiBar = bareName.IndexOf(';');
                    string bareNameClean = semiBar >= 0 ? bareName[..semiBar] : bareName;

                    bool isBootElf = bootElfName != null &&
                        bareNameClean.Equals(bootElfName, StringComparison.OrdinalIgnoreCase);

                    elfFiles.Add(new DiscElfEntry
                    {
                        Path      = discPath,
                        Name      = bareNameClean,
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
        /// <param name="isoPath">Path to the ISO-9660 disc image.</param>
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
                using var stream = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
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
