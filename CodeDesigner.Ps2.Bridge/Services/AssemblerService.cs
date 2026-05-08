using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodeDesigner.Ps2.CheatEngine;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.CdsLanguage;
using CodeDesigner.Ps2.CdsLanguage.SyntaxItems;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class AssemblerService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public AssemblerService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleAssemble(c.Data),                                "ASSEMBLE");
            reg.Add(c => HandleCompileCds(c.Data, c.Labels),                    "COMPILECDS");
            reg.Add(c => HandleValidateCds(c.Data, c.Labels),                   "VALIDATECDS");
            reg.Add(c => HandleCdsPatch(c.Data, c.Labels),                      "CDSPATCH");
            reg.Add(c => HandleDisassemble(c.Data, c.Address),                  "DISASSEMBLE");
        }


        /// <summary>
        /// Handle COMPILECDS command — compiles a CDS (Code Designer Script) source text
        /// into PS2 GameShark / Action Replay cheat codes.
        ///
        /// <para>
        /// The full CDS language is supported, including all preprocessor features
        /// (const/alias/macro/include/if/else/endif/while/endwhile/repeat/endrepeat),
        /// mem.b/h/w/d typed memory writes, scope blocks, sidecar directives
         /// (assert, breakpoint, watchpoint, log/print), and freeze/cond cheat entries.
        /// </para>
        ///
        /// <para>
        /// External label names (from the server label registry and the pointer-manager)
        /// can be injected via <paramref name="externalLabels"/> so that CDS source can
        /// reference them in branch/jump targets and setreg operands.
        /// </para>
        ///
        /// <para>
        /// Sidecar commands are extracted from the compiled output and returned in the
        /// <c>Sidecars</c> field of the response:
        /// </para>
        /// <list type="bullet">
        ///   <item><c>assert</c> — checked against live memory when a backend is available;
        ///     a failed assertion aborts code output and reports an error.</item>
        ///   <item><c>breakpoint</c> — returned as metadata for the caller to install.</item>
         ///   <item><c>watchpoint</c> — returned as metadata for the caller to install.</item>
         ///   <item><c>log</c>/<c>print</c> — returned as metadata for the caller to surface.</item>
        /// </list>
        ///
        /// <para>
        /// Each emitted (address, word) pair is formatted as a GameShark type-2 code:
        /// <c>2AAAAAAA VVVVVVVV</c>
        /// </para>
        /// </summary>
        private string HandleCompileCds(string? sourceCode, List<LabelEntry>? externalLabels = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceCode))
                    return Responses.Error("No source code provided");

                // ── Build external label dictionary ──────────────────────────────────
                // Priority (lowest to highest): pointer-manager labels → server labels → CDS source.
                var extDict = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

                // Pointer-manager labels (pre-resolved dotted names like "seal.health")
                if (externalLabels != null)
                {
                    foreach (var entry in externalLabels)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Label) ||
                            string.IsNullOrWhiteSpace(entry.Address)) continue;
                        var addrStr = entry.Address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            ? entry.Address[2..]
                            : entry.Address;
                        if (uint.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber,
                                null, out uint addr))
                            extDict[entry.Label] = addr;
                    }
                }

                // Server-registered labels (breakpoints, user labels, etc.)
                foreach (var kv in _ctx.Labels)
                    extDict[kv.Value.Label] = kv.Key;

                // ── Parse ────────────────────────────────────────────────────────────
                var parser = new CdsParser();
                var items  = parser.Parse(sourceCode, basePath: "", externalLabels: extDict);

                // ── Build results list ───────────────────────────────────────────────
                var results = new List<InstructionResult>();

                // Diagnostics from the preprocessor / parser.
                // Preserve all structured diagnostics on the response, but only surface
                // hard errors as failed instruction results so warnings do not make
                // COMPILECDS look like a failed compile in existing clients.
                foreach (var diag in parser.Diagnostics)
                {
                    if (diag.Severity != CdsDiagnosticSeverity.Error) continue;
                    results.Add(new InstructionResult
                    {
                        Success    = false,
                        Assembly   = string.Empty,
                        Error      = diag.Message,
                        LineNumber = diag.LineNumber
                    });
                }

                // ── Process sidecar commands ─────────────────────────────────────────
                var sidecars       = CdsCompiler.GetSidecarCommands(items);
                var sidecarResults = new List<CdsSidecarResult>();
                bool assertFailed  = false;

                foreach (var sc in sidecars)
                {
                    switch (sc)
                    {
                        case AssertSyntaxItem assert:
                        {
                            var sidecar = CreateSidecarResult(assert);

                            uint? actual = TryReadWordFromLiveMemory(assert.Address);
                            if (actual.HasValue)
                            {
                                sidecar.ActualValue = actual;
                                bool pass = string.Equals(assert.Operator, "!=",
                                    StringComparison.Ordinal)
                                    ? actual.Value != assert.ExpectedValue
                                    : actual.Value == assert.ExpectedValue;
                                sidecar.AssertPassed = pass;

                                if (!pass)
                                {
                                    assertFailed = true;
                                    results.Add(new InstructionResult
                                    {
                                        Success = false,
                                        Error   = $"Assert failed at 0x{assert.Address:X8}: " +
                                                  $"expected {assert.Operator} 0x{assert.ExpectedValue:X8}, " +
                                                  $"got 0x{actual.Value:X8}"
                                    });
                                }
                            }

                            sidecarResults.Add(sidecar);
                            break;
                        }

                        case BreakpointSyntaxItem:
                        case WatchpointSyntaxItem:
                        case LogSyntaxItem:
                            sidecarResults.Add(CreateSidecarResult(sc));
                            break;
                    }
                }

                // If any assert failed against live memory, return without code words.
                if (assertFailed)
                {
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success  = false,
                        Command  = "COMPILECDS",
                        Results  = results,
                        Sidecars = sidecarResults.Count > 0 ? sidecarResults : null,
                        Diagnostics = parser.Diagnostics.Count > 0 ? parser.Diagnostics.ToList() : null
                    });
                }

                // Walk the compiled (address, word) pairs and format as GameShark codes
                var compiled = CdsCompiler.Compile(items);
                // Build a lookup from item LineNumber/LineText using the item tree
                // so we can annotate each code with the originating source line.
                var lineByAddress = BuildLineAnnotations(items);

                foreach (var (addr, word) in compiled)
                {
                    string lineText = string.Empty;
                    int? lineNum = null;
                    if (lineByAddress.TryGetValue(addr, out var annotation))
                    {
                        lineText = annotation.lineText;
                        lineNum  = annotation.lineNumber;
                    }
                    results.Add(new InstructionResult
                    {
                        Success    = true,
                        Assembly   = lineText,
                        Hex        = FormatGameSharkCode(addr, word),
                        LineNumber = lineNum
                    });
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "COMPILECDS",
                    Results  = results,
                    Sidecars = sidecarResults.Count > 0 ? sidecarResults : null,
                    Diagnostics = parser.Diagnostics.Count > 0 ? parser.Diagnostics.ToList() : null
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"Compile error: {ex.Message}");
            }
        }

        /// <summary>
        /// Try to read a 32-bit word from live PS2 memory at the given virtual address.
        /// Returns <see langword="null"/> when no live backend is connected or the read fails.
        /// </summary>
        private uint? TryReadWordFromLiveMemory(uint address)
        {
            try
            {
                if (_ctx.Client != null)
                {
                    uint clientAddr = address >= 0x80000000u && address <= 0xBFFFFFFFu
                        ? address
                        : (address < 0x20000000u ? address | 0x80000000u : address);
                    byte[] bytes = _ctx.Client.ReadBytes(clientAddr, 4);
                    if (bytes.Length == 4)
                        return System.BitConverter.ToUInt32(bytes, 0);
                }
            }
            catch { /* swallow — no live backend or address out of range */ }
            return null;
        }

        /// <summary>
        /// Handle CDSPATCH command — compile a CDS source, check assert sidecars against live
        /// memory, apply the generated GameShark codes to RAM, and then install any
        /// <c>breakpoint</c> / <c>watchpoint</c> sidecar commands.
        ///
        /// <para>
        /// This is the atomic "compile + apply" variant of the two-step
        /// <c>COMPILECDS</c> → <c>APPLYCHEATS</c> workflow, useful for MCP agents and
        /// automation scripts that want a single round-trip.
        /// </para>
        ///
        /// <para>
        /// On assert failure the command returns <c>Success=false</c> and aborts without
        /// writing any memory or installing any breakpoints or watchpoints.
        /// </para>
        /// </summary>
        private string HandleCdsPatch(string? sourceCode, List<LabelEntry>? externalLabels = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceCode))
                    return Responses.Error("No source code provided");

                if (_ctx.Client == null)
                    return Responses.Error("No live memory backend — start with --pcsx2 or STARTEMULATOR");

                // ── Build external label dictionary ──────────────────────────────────
                var extDict = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
                if (externalLabels != null)
                {
                    foreach (var entry in externalLabels)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Label) ||
                            string.IsNullOrWhiteSpace(entry.Address)) continue;
                        var addrStr = entry.Address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            ? entry.Address[2..]
                            : entry.Address;
                        if (uint.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber,
                                null, out uint addr))
                            extDict[entry.Label] = addr;
                    }
                }
                foreach (var kv in _ctx.Labels)
                    extDict[kv.Value.Label] = kv.Key;

                // ── Parse ────────────────────────────────────────────────────────────
                var parser = new CdsParser();
                var items  = parser.Parse(sourceCode, basePath: "", externalLabels: extDict);

                // Collect parser diagnostics as error results (informational — returned in response)
                var errorResults = new List<InstructionResult>();
                foreach (var diag in parser.Diagnostics)
                {
                    errorResults.Add(new InstructionResult
                    {
                        Success    = false,
                        Assembly   = string.Empty,
                        Error      = diag.Message,
                        LineNumber = diag.LineNumber
                    });
                }

                // ── Check assert sidecars ────────────────────────────────────────────
                var sidecars       = CdsCompiler.GetSidecarCommands(items);
                var sidecarResults = new List<CdsSidecarResult>();

                foreach (var sc in sidecars)
                {
                    if (sc is not AssertSyntaxItem assert)
                    {
                        if (sc is LogSyntaxItem log)
                            sidecarResults.Add(CreateSidecarResult(log));
                        continue;
                    }

                    var sidecar = CreateSidecarResult(assert);

                    uint? actual = TryReadWordFromLiveMemory(assert.Address);
                    if (actual.HasValue)
                    {
                        sidecar.ActualValue = actual;
                        bool pass = string.Equals(assert.Operator, "!=", StringComparison.Ordinal)
                            ? actual.Value != assert.ExpectedValue
                            : actual.Value == assert.ExpectedValue;
                        sidecar.AssertPassed = pass;

                        if (!pass)
                        {
                            sidecarResults.Add(sidecar);
                            return JsonSerializer.Serialize(new CommandResponse
                            {
                                Success  = false,
                                Command  = "CDSPATCH",
                                Error    = $"Assert failed at 0x{assert.Address:X8}: " +
                                           $"expected {assert.Operator} 0x{assert.ExpectedValue:X8}, " +
                                           $"got 0x{actual.Value:X8}",
                                Results  = errorResults.Count > 0 ? errorResults : null,
                                Sidecars = sidecarResults
                            });
                        }
                    }

                    sidecarResults.Add(sidecar);
                }

                if (errorResults.Count > 0)
                {
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success  = false,
                        Command  = "CDSPATCH",
                        Error    = $"{errorResults.Count} CDS parse error(s) — patch not applied",
                        Results  = errorResults,
                        Sidecars = sidecarResults.Count > 0 ? sidecarResults : null
                    });
                }

                // ── Build GameShark codes and apply them ─────────────────────────────
                var compiled   = CdsCompiler.Compile(items);
                var codeLines  = new List<string>(compiled.Count);
                foreach (var (addr, word) in compiled)
                    codeLines.Add(FormatGameSharkCode(addr, word));

                var cheatText = string.Join('\n', codeLines);

                IByteEditor byteEditor = new DebugClientByteEditor(_ctx.Client!);
                var engine    = new CheatEngine.CheatEngine(byteEditor);
                var cheatList = new CheatList(cheatText);
                engine.PatchMemory(cheatList);

                // ── Install remaining sidecars (breakpoints / watchpoints) ────────────
                var installed = new List<string>();
                var skipped   = new List<string>();

                foreach (var sc in sidecars)
                {
                    switch (sc)
                    {
                        case BreakpointSyntaxItem bp:
                            try
                            {
                                SetBreakpointInternal(bp.Address);
                                sidecarResults.Add(CreateSidecarResult(bp));
                                installed.Add($"breakpoint @ 0x{bp.Address:X8}");
                            }
                            catch (Exception ex)
                            {
                                skipped.Add($"breakpoint @ 0x{bp.Address:X8}: {ex.Message}");
                            }
                            break;

                        case WatchpointSyntaxItem wp:
                            try
                            {
                                SetWatchpointInternal(wp.Address, wp.Size, wp.AccessType);
                                sidecarResults.Add(CreateSidecarResult(wp));
                                installed.Add($"watchpoint {wp.AccessType} @ 0x{wp.Address:X8} ({wp.Size} bytes)");
                            }
                            catch (Exception ex)
                            {
                                skipped.Add($"watchpoint @ 0x{wp.Address:X8}: {ex.Message}");
                            }
                            break;
                    }
                }

                // ── Build summary message ────────────────────────────────────────────
                var sb = new System.Text.StringBuilder();
                sb.Append($"Patch applied — {codeLines.Count} code(s) written");
                if (installed.Count > 0)
                    sb.Append($", {installed.Count} sidecar(s) installed");
                if (skipped.Count > 0)
                    sb.Append($", {skipped.Count} sidecar(s) skipped (see Sidecars)");

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "CDSPATCH",
                    Message  = sb.ToString(),
                    Sidecars = sidecarResults.Count > 0 ? sidecarResults : null
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"CDSPATCH error: {ex.Message}");
            }
        }

        private static CdsSidecarResult CreateSidecarResult(CdsSyntaxItem sidecar) =>
            sidecar switch
            {
                AssertSyntaxItem assert => new CdsSidecarResult
                {
                    Kind          = "assert",
                    Address       = assert.Address,
                    Operator      = assert.Operator,
                    ExpectedValue = assert.ExpectedValue,
                },
                BreakpointSyntaxItem bp => new CdsSidecarResult
                {
                    Kind    = "breakpoint",
                    Address = bp.Address
                },
                WatchpointSyntaxItem wp => new CdsSidecarResult
                {
                    Kind       = "watchpoint",
                    Address    = wp.Address,
                    WatchSize  = wp.Size,
                    AccessType = wp.AccessType.ToString().ToLowerInvariant()
                },
                LogSyntaxItem log => new CdsSidecarResult
                {
                    Kind        = "log",
                    Address     = log.Address,
                    Directive   = log.Directive,
                    MessageText = log.MessageText
                },
                _ => throw new ArgumentException($"Unsupported CDS sidecar type: {sidecar.GetType().Name}", nameof(sidecar))
            };

        /// <summary>
        /// Handle VALIDATECDS command — parse a CDS source without assembling or emitting any
        /// codes.  Returns all diagnostics (errors and warnings) produced by the preprocessor
        /// and parser, or a success message if no issues were found.
        /// </summary>
        private string HandleValidateCds(string? sourceCode, List<LabelEntry>? externalLabels = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceCode))
                    return Responses.Error("No source code provided");

                // ── Build external label dictionary ──────────────────────────────
                var extDict = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
                if (externalLabels != null)
                {
                    foreach (var entry in externalLabels)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Label) ||
                            string.IsNullOrWhiteSpace(entry.Address)) continue;
                        var addrStr = entry.Address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            ? entry.Address[2..]
                            : entry.Address;
                        if (uint.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber,
                                null, out uint addr))
                            extDict[entry.Label] = addr;
                    }
                }
                foreach (var kv in _ctx.Labels)
                    extDict[kv.Value.Label] = kv.Key;

                // ── Parse (no compile step) ──────────────────────────────────────
                var parser = new CdsParser();
                parser.Parse(sourceCode, basePath: "", externalLabels: extDict);

                var diagnostics = parser.Diagnostics.ToList();
                var results = new List<InstructionResult>();
                foreach (var diag in diagnostics)
                {
                    results.Add(new InstructionResult
                    {
                        Success    = diag.Severity != CdsDiagnosticSeverity.Error,
                        Error      = diag.Severity == CdsDiagnosticSeverity.Error ? diag.Message : null,
                        Assembly   = diag.Severity == CdsDiagnosticSeverity.Warning ? diag.Message : null,
                        LineNumber = diag.LineNumber
                    });
                }

                bool hasErrors   = diagnostics.Any(d => d.Severity == CdsDiagnosticSeverity.Error);
                bool hasWarnings = diagnostics.Any(d => d.Severity == CdsDiagnosticSeverity.Warning);

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = !hasErrors,
                    Command  = "VALIDATECDS",
                    Message  = hasErrors
                        ? $"Validation failed — {diagnostics.Count(d => d.Severity == CdsDiagnosticSeverity.Error)} error(s), {diagnostics.Count(d => d.Severity == CdsDiagnosticSeverity.Warning)} warning(s)"
                        : hasWarnings
                            ? $"Validation passed with {diagnostics.Count(d => d.Severity == CdsDiagnosticSeverity.Warning)} warning(s)"
                            : "Validation passed — no errors or warnings",
                    Results  = results.Count > 0 ? results : null,
                    Diagnostics = diagnostics.Count > 0 ? diagnostics : null
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"VALIDATECDS error: {ex.Message}");
            }
        }

        /// <summary>
        /// Install a software breakpoint at the given EE virtual address through
        /// the active <see cref="IDebugClient"/> (_ctx.PineClient or InternalEmulatorClient).
        /// </summary>
        private void SetBreakpointInternal(uint address)
        {
            if (_ctx.Client == null)
                throw new InvalidOperationException("No live backend for breakpoint installation");
            _ctx.Client.SetBreakpoint(AddressTranslator.NormalizeBreakpointForClient(address));
        }

        /// <summary>
        /// Install a hardware watchpoint at the given EE virtual address range.
        /// In PINE mode delegates to <see cref="_ctx.PineClient.AddWatch"/>; PINE-only feature.
        /// </summary>
        private void SetWatchpointInternal(uint address, uint size, WatchpointAccessType accessType)
        {
            if (_ctx.PineClient == null)
                throw new InvalidOperationException("Watchpoints via CDSPATCH require PINE mode (connect to PCSX2 first)");
            if (_ctx.PineClient == null)
                throw new InvalidOperationException("PINE _ctx.Client not connected");

            uint cond = accessType switch
            {
                WatchpointAccessType.Read      => 1u,
                WatchpointAccessType.ReadWrite => 3u,
                _                              => 2u   // Write
            };
            uint physStart = address & 0x1FFFFFFF;
            // Guard against physStart + size wrapping past the 29-bit physical address space
            if (size > 0x1FFFFFFF - physStart)
                throw new ArgumentOutOfRangeException(nameof(size),
                    $"Watchpoint range overflow: physStart=0x{physStart:X8} + size={size} exceeds physical address space");
            uint physEnd   = physStart + size;
            _ctx.PineClient.AddWatch(cpu: 0, physStart, physEnd, (byte)cond);
        }

        /// <summary>
        /// Build a flat dictionary mapping compiled output address → (lineNumber, lineText)
        /// by walking the syntax item tree recursively.  When multiple items map to the same
        /// address (shouldn't happen but is safe), the first one wins.
        /// </summary>
        private static Dictionary<uint, (int lineNumber, string lineText)> BuildLineAnnotations(
            IEnumerable<CdsSyntaxItem> items)
        {
            var dict = new Dictionary<uint, (int, string)>();
            CollectAnnotations(items, dict);
            return dict;

            static void CollectAnnotations(
                IEnumerable<CdsSyntaxItem> src,
                Dictionary<uint, (int, string)> d)
            {
                foreach (var item in src)
                {
                    switch (item)
                    {
                        case OperationSyntaxItem op:
                            d.TryAdd(op.Address, (op.LineNumber, op.LineText));
                            break;

                        case HexCodeSyntaxItem hex:
                            d.TryAdd(hex.Address, (hex.LineNumber, hex.LineText));
                            break;

                        case SetRegSyntaxItem sr:
                            d.TryAdd(sr.LuiOp.Address, (sr.LineNumber, sr.LineText));
                            d.TryAdd(sr.OriOp.Address, (sr.LineNumber, sr.LineText));
                            break;

                        case MemorySyntaxItem mem:
                            foreach (var op in mem.Operations)
                                d.TryAdd(op.Address, (mem.LineNumber, mem.LineText));
                            break;

                        case LoadMemorySyntaxItem load:
                            d.TryAdd(load.Operation.Address, (load.LineNumber, load.LineText));
                            break;

                        case StoreMemorySyntaxItem store:
                            d.TryAdd(store.Operation.Address, (store.LineNumber, store.LineText));
                            break;

                        case StringSyntaxItem str:
                            foreach (var w in str.Words)
                                d.TryAdd(w.Address, (str.LineNumber, str.LineText));
                            break;

                        case ScopeSyntaxItem scope:
                            CollectAnnotations(scope.Body, d);
                            break;
                    }
                }
            }
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
                    return Responses.Error("No assembly code provided");
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
                return Responses.Error($"Assemble error: {ex.Message}");
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
                    return Responses.Error("No hex code provided");
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

                // Resolve a live memory bus so the analyzer can peek at strings
                // and pointer chains. Wraps the active debug _ctx.Client through the
                // ByteEditor → IPs2MemoryBus adapter chain. When no backend is
                // available (file-only mode) the bus remains null and the
                // analyzer falls back to label/MMIO-name hints only.
                CodeDesigner.Ps2.Common.Interfaces.IPs2MemoryBus? analysisBus =
                    _ctx.Client != null
                        ? new ByteEditorMemoryBusAdapter(new DebugClientByteEditor(_ctx.Client))
                        : null;

                // Run analyzer using the instruction's built-in Analyze() method.
                // State-specific hints (TAKEN / NOT TAKEN) are suppressed here because
                // the batch analyzer starts from a blank CPU state.
                var comments = MipsAnalyzer.AnalyzeInstructions(
                    instructionList, 
                    startAddress ?? 0,
                    executeInstructions: true,  // Execute instructions to track register state
                    bus: analysisBus,
                    lookupLabel: addr => _ctx.Labels.TryGetValue(addr, out var e) ? e.Label : null,
                    includeStateHints: false);

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
                        if (_ctx.Labels.TryGetValue(addr, out var labelEntry))
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
                return Responses.Error($"Disassemble error: {ex.Message}");
            }
        }

        /// <summary>Format a (address, word) pair as a GameShark type-2 code.</summary>
        private static string FormatGameSharkCode(uint address, uint value)
        {
            // Type-2 prefix (0x20000000) ORed with the low 28 bits of the target address.
            uint codeAddr = 0x20000000u | (address & 0x0FFFFFFFu);
            return $"{codeAddr:X8} {value:X8}";
        }
    }
}
