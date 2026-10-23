using System;
using System.Collections.Generic;
using System.Globalization;
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
            reg.Add(c => HandleDisassemble(c.Data, c.Address, c.GameId, c.SkipDataFormatOverrides == true), "DISASSEMBLE");
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
        /// Handle DISASSEMBLE command.
        ///
        /// <para>
        /// The hex payload is a list of 4-byte words (one per source line, e.g.
        /// the contents of an ELF .text section as little-endian uint32s).  Each
        /// word produces one MIPS instruction row.
        /// </para>
        ///
        /// <para>
        /// When a <c>gameId</c> is supplied, any persisted
        /// <see cref="Database.Entities.DataFormatOverride"/> rows whose byte range
        /// intersects the requested window are applied: the affected words are
        /// re-emitted as one or more typed data rows (<c>.byte</c>, <c>.half</c>,
        /// <c>.word</c>, <c>.dword</c>, <c>.qword</c>) instead of being decoded as
        /// instructions.  Row stride therefore becomes variable — each row's
        /// length is reported in <see cref="InstructionResult.ByteLength"/>.
        /// </para>
        /// </summary>
        private string HandleDisassemble(string? hexCode, uint? startAddress = null, string? gameId = null, bool skipDataFormatOverrides = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hexCode))
                {
                    return Responses.Error("No hex code provided");
                }

                // ── Parse the hex payload back into a contiguous byte buffer ──
                var lines = hexCode.Split(new[] { '\n', '\r', ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                var words = new List<uint>(lines.Length);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                        continue;
                    string hexValue = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? trimmed.Substring(2)
                        : trimmed;
                    if (!uint.TryParse(hexValue, System.Globalization.NumberStyles.HexNumber, null, out uint w))
                    {
                        // Preserve the original behaviour of reporting a bad
                        // line as a failed row.  Use 0 as a placeholder so the
                        // byte buffer offsets remain aligned with the source.
                        words.Add(0);
                        continue;
                    }
                    words.Add(w);
                }

                // Convert to a byte buffer so we can read partial slices for
                // data-format rows below.  PS2 memory is little-endian.
                var byteBuf = new byte[words.Count * 4];
                for (int i = 0; i < words.Count; i++)
                {
                    uint w = words[i];
                    int bi = i * 4;
                    byteBuf[bi    ] = (byte) (w        & 0xFF);
                    byteBuf[bi + 1] = (byte)((w >>  8) & 0xFF);
                    byteBuf[bi + 2] = (byte)((w >> 16) & 0xFF);
                    byteBuf[bi + 3] = (byte)((w >> 24) & 0xFF);
                }

                uint baseAddr = startAddress ?? 0;
                long rangeStart = baseAddr;
                long rangeEnd   = baseAddr + (long)byteBuf.Length;

                // ── Pull overrides for the requested window ───────────────────
                IReadOnlyList<Database.Entities.DataFormatOverride> overrides = System.Array.Empty<Database.Entities.DataFormatOverride>();
                if (!skipDataFormatOverrides)
                {
                    try
                    {
                        // Query even when gameId is null/empty. The repository
                        // treats that as the disc-less scope used by file-only
                        // disassembly, so user-applied formats saved before a
                        // game serial is known are still persistent. When a game
                        // id is known, also include non-overlapping disc-less
                        // rows as a migration fallback for overrides created
                        // before the serial finished resolving.
                        var scoped = _ctx.DataFormatRepo
                            .GetInRangeAsync(gameId, rangeStart, rangeEnd)
                            .GetAwaiter().GetResult();

                        if (string.IsNullOrWhiteSpace(gameId))
                        {
                            overrides = scoped;
                        }
                        else
                        {
                            var fallback = _ctx.DataFormatRepo
                                .GetInRangeAsync(null, rangeStart, rangeEnd)
                                .GetAwaiter().GetResult();

                            var merged = new List<Database.Entities.DataFormatOverride>(scoped);
                            foreach (var candidate in fallback)
                            {
                                long candidateEnd = candidate.Address + candidate.Length;
                                bool overlapsScoped = scoped.Any(existing =>
                                    existing.Address < candidateEnd
                                    && existing.Address + existing.Length > candidate.Address);
                                if (!overlapsScoped) merged.Add(candidate);
                            }

                            overrides = merged.OrderBy(o => o.Address).ToList();
                        }
                    }
                    catch
                    {
                        // Treat repository failures as "no overrides" — the
                        // disassembler should still return instruction rows.
                        overrides = System.Array.Empty<Database.Entities.DataFormatOverride>();
                    }
                }

                // Per-page string auto-detect — kept as a discovery aid, but
                // emitted as byte rows (not a standalone .asciiz row) so the
                // display path is identical to a user-applied Byte
                // DataFormatOverride. The first row in the auto-detected span
                // carries the full ASCII content as its comment, matching the
                // user-override behaviour below.
                var autoStrings = ComputeAutoStringRanges(byteBuf, overrides, baseAddr);

                var results        = new List<InstructionResult>();
                var instructionList = new List<(uint instruction, uint address)>();

                // Resolve a memory bus for comment-column ASCII / pointer
                // annotations. Prefer the analysis-emulator mirror when one
                // is attached (no PINE traffic); fall back to the live client
                // (PINE or built-in) when no mirror is available so strings
                // and pointer chains still resolve in plain browsing mode.
                IDebugClient? hintClient = _ctx.AnalysisClient ?? _ctx.Client;
                CodeDesigner.Ps2.Common.Interfaces.IPs2MemoryBus? mirrorBus =
                    hintClient != null
                        ? new ByteEditorMemoryBusAdapter(new DebugClientByteEditor(hintClient))
                        : null;

                // Layer the disasm-page bytes on top of the mirror bus so the
                // analyzer can resolve strings/pointers that live within the
                // current page even when no mirror is attached. Out-of-page
                // reads still fall back to the mirror (when available); they
                // never reach PCSX2 PINE.
                CodeDesigner.Ps2.Common.Interfaces.IPs2MemoryBus analysisBus =
                    new PageBytesMemoryBus(byteBuf, baseAddr, mirrorBus);

                // Simulated loads can be many per page, so they only read the page
                // and the local analysis mirror — never the live PINE connection.
                CodeDesigner.Ps2.Common.Interfaces.IPs2MemoryBus loadBus =
                    new PageBytesMemoryBus(byteBuf, baseAddr,
                        _ctx.AnalysisClient != null
                            ? new ByteEditorMemoryBusAdapter(new DebugClientByteEditor(_ctx.AnalysisClient))
                            : null);

                // Walk the byte buffer.  For every offset, check whether an
                // override covers it; if so emit data rows, otherwise emit an
                // instruction row for the current 4-byte word.
                int byteIndex = 0;
                while (byteIndex < byteBuf.Length)
                {
                    uint rowAddr = (uint)(baseAddr + (uint)byteIndex);
                    var ovr = FindOverride(overrides, rowAddr);
                    if (ovr is not null)
                    {
                        int unit = Repositories.DbDataFormatRepository.ByteSizeOf(ovr.Kind);

                        // When a Byte override sits inside a printable-ASCII run that
                        // ends with a null, auto-extend the byte span until that null
                        // (plus the 4-byte alignment pad that follows). The override
                        // only has to mark the START of the string — the system fills
                        // in the rest so the user doesn't have to size the override to
                        // the exact string length, and every character of the string
                        // renders as a byte row instead of half-string + half-MIPS.
                        long ovrEnd = ovr.Address + ovr.Length;
                        bool extendedForString = false;
                        string? leadingStringComment = null;
                        if (ovr.Kind == Database.Entities.DataFormatKind.Byte
                            && rowAddr == unchecked((uint)ovr.Address))
                        {
                            int stringRunBytes = MeasureNullTerminatedAscii(byteBuf, byteIndex);
                            if (stringRunBytes > 0)
                            {
                                int padded   = (stringRunBytes + 3) & ~3;
                                int paddedEnd = byteIndex + padded;
                                bool paddingClean = paddedEnd <= byteBuf.Length;
                                for (int p = byteIndex + stringRunBytes; paddingClean && p < paddedEnd; p++)
                                {
                                    if (byteBuf[p] != 0x00) paddingClean = false;
                                }
                                int extendedBytes = paddingClean ? padded : stringRunBytes;
                                long stringEnd = baseAddr + (uint)byteIndex + (uint)extendedBytes;
                                if (stringEnd > ovrEnd)
                                {
                                    ovrEnd = stringEnd;
                                    extendedForString = true;
                                }

                                // ascii minus the trailing null
                                string asciiContent = System.Text.Encoding.ASCII
                                    .GetString(byteBuf, byteIndex, stringRunBytes - 1);
                                leadingStringComment = $"-> \"{EscapeCommentString(asciiContent)}\"";
                            }
                        }

                        // Emit one row per element of the (possibly extended) span,
                        // but cap at the actual buffer end to avoid reading past it.
                        bool firstRow = true;
                        while (byteIndex < byteBuf.Length && (baseAddr + (uint)byteIndex) < ovrEnd)
                        {
                            uint rAddr = (uint)(baseAddr + (uint)byteIndex);
                            int  rLen  = System.Math.Min(unit, byteBuf.Length - byteIndex);
                            if (rLen < unit) break; // not enough bytes for a full element

                            string asm     = FormatDataDirective(byteBuf, byteIndex, ovr.Kind, out string? hexStr);
                            string? cmt    = null;
                            if (ovr.Kind == Database.Entities.DataFormatKind.Word)
                            {
                                uint val = BitConverter.ToUInt32(byteBuf, byteIndex);
                                cmt = TryResolveInlineAsciiString(byteBuf, byteIndex, byteBuf.Length - byteIndex)
                                      ?? TryResolveInlineAsciiString(byteBuf, byteIndex, unit)
                                      ?? TryResolvePointerComment(val, analysisBus)
                                      ?? (LooksLikeRamPointer(val) ? $"-> 0x{val:X8}" : null);
                            }
                            else if (ovr.Kind == Database.Entities.DataFormatKind.Byte
                                     && firstRow && leadingStringComment is not null)
                            {
                                cmt = leadingStringComment;
                            }
                            else if (ovr.Kind == Database.Entities.DataFormatKind.Byte
                                     && firstRow && leadingStringComment is null
                                     && rAddr == unchecked((uint)ovr.Address))
                            {
                                // Byte override but not a string — keep the legacy
                                // "best-effort inline ASCII" hint scoped to the
                                // override's declared length.
                                int maxStringBytes = System.Math.Min(
                                    (int)System.Math.Min(ovr.Length, int.MaxValue),
                                    byteBuf.Length - byteIndex);
                                cmt = TryResolveInlineAsciiString(byteBuf, byteIndex, maxStringBytes);
                            }

                            results.Add(new InstructionResult
                            {
                                Address    = rAddr,
                                ByteLength = unit,
                                Hex        = hexStr,
                                Assembly   = asm,
                                Success    = true,
                                Comment    = cmt,
                                DataFormatKind = DataFormatService.KindToWire(ovr.Kind),
                                // Bytes appended by string-extension aren't in the
                                // persisted override; flag them so the webview won't
                                // expose "Clear formatting" for those tail rows.
                                AutoFormatted = extendedForString
                                    && (baseAddr + (uint)byteIndex + (uint)unit) > (ovr.Address + ovr.Length),
                            });

                            byteIndex += unit;
                            firstRow = false;
                        }
                        continue;
                    }

                    // ── Auto-detected string at this offset ───────────────────
                    // Emit byte rows for the printable-ASCII run + null
                    // terminator (and any trailing zero-pad needed to reach the
                    // next 4-byte boundary). The first row carries the resolved
                    // ASCII content as a comment so the user gets the same
                    // "-> \"...\"" annotation a user-applied Byte override
                    // would produce. AutoFormatted=true tells the webview these
                    // came from heuristic detection rather than a DB override.
                    if (autoStrings.TryGetValue(byteIndex, out var autoHit))
                    {
                        int totalBytes = autoHit.Length + 1; // include the null
                        int padded     = (totalBytes + 3) & ~3;
                        int paddedEnd  = byteIndex + padded;
                        bool paddingClean = paddedEnd <= byteBuf.Length;
                        for (int p = byteIndex + totalBytes; paddingClean && p < paddedEnd; p++)
                        {
                            if (byteBuf[p] != 0x00) paddingClean = false;
                        }
                        int consumed = paddingClean ? padded : totalBytes;
                        int spanEnd  = byteIndex + consumed;

                        string? firstComment = $"-> \"{EscapeCommentString(autoHit.Value)}\"";
                        while (byteIndex < spanEnd && byteIndex < byteBuf.Length)
                        {
                            uint rAddr = (uint)(baseAddr + (uint)byteIndex);
                            string asm = FormatDataDirective(byteBuf, byteIndex,
                                Database.Entities.DataFormatKind.Byte, out string? hexStr);

                            results.Add(new InstructionResult
                            {
                                Address       = rAddr,
                                ByteLength    = 1,
                                Hex           = hexStr,
                                Assembly      = asm,
                                Success       = true,
                                Comment       = firstComment,
                                DataFormatKind = DataFormatService.KindToWire(
                                    Database.Entities.DataFormatKind.Byte),
                                AutoFormatted = true,
                            });
                            firstComment = null;
                            byteIndex += 1;
                        }
                        continue;
                    }

                    // No override at this address.  Treat as a 4-byte instruction
                    // row — if we have fewer than 4 bytes left, or a string/override
                    // ended off a word boundary, emit byte rows until the next
                    // boundary so every following instruction decodes aligned.
                    if (byteIndex + 4 > byteBuf.Length || (rowAddr & 3u) != 0)
                    {
                        string asm   = FormatDataDirective(byteBuf, byteIndex, Database.Entities.DataFormatKind.Byte, out string? hexStr);
                        results.Add(new InstructionResult
                        {
                            Address    = rowAddr,
                            ByteLength = 1,
                            Hex        = hexStr,
                            Assembly   = asm,
                            Success    = true,
                        });
                        byteIndex += 1;
                        continue;
                    }

                    uint instruction = BitConverter.ToUInt32(byteBuf, byteIndex);
                    try
                    {
                        string disasm = MipsDisassembler.Disassemble(instruction, rowAddr);

                        // Treat ".word 0x..." (unknown opcode) the same way as
                        // before: surface "unknown" in the op column and move
                        // the original directive into the comment. When the word
                        // value passes the EE-pointer classifier (upper halfword
                        // non-zero + alignment + RAM/scratchpad range) we also
                        // append a target hint — label, ASCII string from the
                        // mirror bus, or raw address.
                        string assembly = disasm;
                        string? unknownComment = null;
                        if (disasm.StartsWith(".word ", StringComparison.Ordinal))
                        {
                            assembly = "unknown";
                            unknownComment = disasm.Substring(".word ".Length);

                            if (Ps2AddressClassifier.IsValidEePointer(instruction))
                            {
                                string? targetLabel = _ctx.Labels.TryGetValue(instruction, out var le)
                                    ? le.Label
                                    : null;
                                string? ptrHint = !string.IsNullOrEmpty(targetLabel)
                                    ? $"-> {targetLabel} (0x{instruction:X8})"
                                    : TryResolvePointerComment(instruction, analysisBus)
                                      ?? $"-> 0x{instruction:X8}";
                                unknownComment = string.IsNullOrEmpty(unknownComment)
                                    ? ptrHint
                                    : $"{unknownComment}  {ptrHint}";
                            }
                        }

                        results.Add(new InstructionResult
                        {
                            Address    = rowAddr,
                            ByteLength = 4,
                            Hex        = $"0x{instruction:X8}",
                            Assembly   = assembly,
                            Success    = true,
                            Comment    = unknownComment,
                        });
                        instructionList.Add((instruction, rowAddr));
                    }
                    catch (Exception ex)
                    {
                        results.Add(new InstructionResult
                        {
                            Address    = rowAddr,
                            ByteLength = 4,
                            Hex        = $"0x{instruction:X8}",
                            Error      = ex.Message,
                            Success    = false,
                        });
                    }

                    byteIndex += 4;
                }

                // State-aware analysis: when the live client is paused and the
                // paused PC is on this page, seed the analyzer with the mirrored
                // register snapshot from AnalysisStateService at that row. Rows
                // above the PC stay on blank state (the snapshot describes the
                // PC, not the code before it), and [TAKEN]/[NOT TAKEN] hints are
                // only shown from the PC to the next control-flow change.
                CodeDesigner.Ps2.Common.CPU.CpuState? seedCpu = null;
                uint? seedPc = null;
                bool includeStateHints = false;
                if (_ctx.AnalysisState is not null)
                {
                    try
                    {
                        _ctx.AnalysisState.RefreshFromLive(force: false);
                        var snap = _ctx.AnalysisState.TryAcquire();
                        if (snap is not null)
                        {
                            uint pc = ToPagePhysical(snap.PcAtMirror);
                            if (pc >= rangeStart && pc < rangeEnd)
                            {
                                seedCpu = snap.Cpu;
                                seedPc = pc;
                                includeStateHints = true;
                            }
                        }
                    }
                    catch
                    {
                        // Best-effort: a failed mirror falls back to blank-state
                        // analysis — the disassembly still renders.
                    }
                }

                // Run analyzer using the instruction's built-in Analyze() method.
                // State-specific hints (TAKEN / NOT TAKEN) are kept only when
                // seedCpu is populated; otherwise they'd be misleading against
                // a blank register file. Only MIPS-instruction rows participate
                // — data-format rows (.byte/.half/.word/...) are skipped so the
                // analyzer does not try to "execute" embedded data.
                // Lead-in: simulate the code just before this page (without
                // emitting rows for it) so register values set above the page
                // still resolve hints on it. With a function index that is the
                // whole function from its entry (from the index's snapshot, no
                // live read); otherwise the last 0x80 bytes.
                var analyzedList = instructionList;
                HashSet<uint>? functionStarts = null;
                if (instructionList.Count > 0)
                {
                    byte[]? leadIn = null;
                    uint leadStart = 0;
                    if (_ctx.Xrefs.TryGetFunction(baseAddr, out uint fnStart, out _) && fnStart < baseAddr)
                    {
                        leadStart = Math.Max(fnStart, baseAddr >= MaxFunctionLeadIn ? baseAddr - MaxFunctionLeadIn : 0u);
                        leadIn = _ctx.Xrefs.TryGetSnapshotBytes(leadStart, (int)(baseAddr - leadStart));
                    }
                    if (leadIn == null && hintClient != null && baseAddr >= LeadInBytes)
                    {
                        try
                        {
                            leadStart = baseAddr - LeadInBytes;
                            leadIn = new DebugClientByteEditor(hintClient).Read((int)leadStart, LeadInBytes);
                            if (leadIn.Length != LeadInBytes) leadIn = null;
                        }
                        catch
                        {
                            // Unreadable lead-in (region start, read failure): analyze the page alone.
                            leadIn = null;
                        }
                    }
                    if (leadIn != null)
                    {
                        analyzedList = new List<(uint instruction, uint address)>(instructionList.Count + leadIn.Length / 4);
                        for (int off = 0; off + 4 <= leadIn.Length; off += 4)
                            analyzedList.Add((BitConverter.ToUInt32(leadIn, off), leadStart + (uint)off));
                        analyzedList.AddRange(instructionList);
                    }

                    // Register state resets at every function entry on (or before) the page.
                    if (_ctx.Xrefs.HasIndex)
                    {
                        functionStarts = new HashSet<uint>();
                        foreach (var (_, a) in analyzedList)
                            if (_ctx.Xrefs.IsFunctionStart(a)) functionStarts.Add(a);
                    }
                }

                var comments = MipsAnalyzer.AnalyzeInstructions(
                    analyzedList,
                    startAddress ?? 0,
                    executeInstructions: true,
                    bus: analysisBus,
                    lookupLabel: addr => _ctx.Labels.TryGetValue(addr, out var e) ? e.Label : null,
                    includeStateHints: includeStateHints,
                    seedCpu: seedCpu,
                    seedPc: seedPc,
                    loadBus: loadBus,
                    functionStarts: functionStarts);

                // Apply comments and labels to results.  Look each row up by
                // address rather than by index because the row stride is now
                // variable (data-format rows can be smaller than 4 bytes).
                foreach (var row in results)
                {
                    if (!row.Success || row.Address is null) continue;
                    uint addr = row.Address.Value;

                    if (string.IsNullOrEmpty(row.Comment) && comments.TryGetValue(addr, out var aComment))
                    {
                        row.Comment = aComment;
                    }

                    if (_ctx.Xrefs.HasIndex)
                    {
                        var (code, data) = _ctx.Xrefs.CountTo(addr);
                        if (code > 0) row.XrefCount = code;
                        if (data > 0) row.DataRefCount = data;
                        if (functionStarts != null && functionStarts.Contains(addr)) row.FunctionStart = true;
                    }

                    if (_ctx.Labels.TryGetValue(addr, out var labelEntry))
                    {
                        row.Label = labelEntry.Label;
                        // A comment the user wrote comes first; the generated hint
                        // follows it instead of hiding it.
                        if (!string.IsNullOrWhiteSpace(labelEntry.Comment))
                        {
                            string userComment = labelEntry.Comment.Trim();
                            string? hint = row.Comment?.TrimStart(';', ' ');
                            row.Comment = string.IsNullOrEmpty(hint) || hint == userComment.TrimStart(';', ' ')
                                ? userComment
                                : $"{userComment} | {hint}";
                        }
                    }
                }

                // Static syscall annotation: when a `syscall` with code=0 is
                // preceded by an instruction that loads an immediate into $v1
                // (reg 3) — addiu/ori/li/lui+ori — resolve the syscall name
                // from PCSX2's EE BIOS table without needing the live register
                // file. This gives game code the same "syscall (FlushCache)"
                // annotation that paused-PC analysis already produces.
                AnnotateSyscallsFromPriorV1Load(results);

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

        // ─── Data-format helpers ───────────────────────────────────────────────

        /// <summary>
        /// Returns the override (if any) whose byte range contains
        /// <paramref name="address"/>.  Overrides are guaranteed non-overlapping
        /// by the repository, so a linear scan is sufficient and a single match
        /// is unambiguous.
        /// </summary>
        private static Database.Entities.DataFormatOverride? FindOverride(
            IReadOnlyList<Database.Entities.DataFormatOverride> overrides, uint address)
        {
            foreach (var o in overrides)
            {
                long start = o.Address;
                long end   = start + o.Length;
                if (address >= start && address < end) return o;
            }
            return null;
        }

        /// <summary>
        /// Formats <paramref name="buf"/> starting at <paramref name="index"/>
        /// as a typed data directive (<c>.byte</c>, <c>.half</c>, <c>.word</c>,
        /// <c>.dword</c>, <c>.qword</c>).  Reads PS2-native little-endian.
        /// Also returns a uniform <c>0xHEX</c> string in <paramref name="hex"/>
        /// for the bytes column.
        /// </summary>
        private static string FormatDataDirective(
            byte[] buf, int index, Database.Entities.DataFormatKind kind, out string hex)
        {
            switch (kind)
            {
                case Database.Entities.DataFormatKind.Byte:
                {
                    byte b = buf[index];
                    hex = $"0x{b:X2}";
                    return $".byte ${b:X2}";
                }
                case Database.Entities.DataFormatKind.Half:
                {
                    ushort v = BitConverter.ToUInt16(buf, index);
                    hex = $"0x{v:X4}";
                    return $".half ${v:X4}";
                }
                case Database.Entities.DataFormatKind.Word:
                {
                    uint v = BitConverter.ToUInt32(buf, index);
                    hex = $"0x{v:X8}";
                    return $".word 0x{v:X8}";
                }
                case Database.Entities.DataFormatKind.DWord:
                {
                    ulong v = BitConverter.ToUInt64(buf, index);
                    hex = $"0x{v:X16}";
                    return $".dword 0x{v:X16}";
                }
                case Database.Entities.DataFormatKind.QWord:
                {
                    ulong lo = BitConverter.ToUInt64(buf, index);
                    ulong hi = BitConverter.ToUInt64(buf, index + 8);
                    hex = $"0x{hi:X16}{lo:X16}";
                    return $".qword 0x{hi:X16}{lo:X16}";
                }
                case Database.Entities.DataFormatKind.Float:
                {
                    uint raw = BitConverter.ToUInt32(buf, index);
                    float v = BitConverter.ToSingle(buf, index);
                    hex = $"0x{raw:X8}";
                    return $".float {FormatFloatLiteral(v)}";
                }
                case Database.Entities.DataFormatKind.Double:
                {
                    ulong raw = BitConverter.ToUInt64(buf, index);
                    double v = BitConverter.ToDouble(buf, index);
                    hex = $"0x{raw:X16}";
                    return $".double {FormatDoubleLiteral(v)}";
                }
                default:
                {
                    byte b = buf[index];
                    hex = $"0x{b:X2}";
                    return $".byte ${b:X2}";
                }
            }
        }

        private static string FormatFloatLiteral(float value)
        {
            if (float.IsNaN(value)) return "NaN";
            if (float.IsPositiveInfinity(value)) return "Infinity";
            if (float.IsNegativeInfinity(value)) return "-Infinity";

            string text = value.ToString("G9", CultureInfo.InvariantCulture);
            return LooksIntegralLiteral(text) ? text + ".0" : text;
        }

        private static string FormatDoubleLiteral(double value)
        {
            if (double.IsNaN(value)) return "NaN";
            if (double.IsPositiveInfinity(value)) return "Infinity";
            if (double.IsNegativeInfinity(value)) return "-Infinity";

            string text = value.ToString("G17", CultureInfo.InvariantCulture);
            return LooksIntegralLiteral(text) ? text + ".0" : text;
        }

        private static bool LooksIntegralLiteral(string text) =>
            text.IndexOf('.') < 0
            && text.IndexOf('E') < 0
            && text.IndexOf('e') < 0;

        /// <summary>
        /// Minimum printable-ASCII run length the per-page auto-detector treats
        /// as a string. Higher than the user-facing string scanner's minimum
        /// (which is 4) because the auto-detector runs against MIPS code pages
        /// where short ASCII sequences can occur incidentally.
        /// </summary>
        private const int MinAutoStringLength = 6;

        /// <summary>
        /// Computes the set of auto-detect string ranges for the current
        /// disassembly page. A range is kept only when:
        ///   - The full string + null terminator fits inside <paramref name="byteBuf"/>.
        ///   - All bytes are printable ASCII (0x20–0x7E).
        ///   - Length is at least <see cref="MinAutoStringLength"/>.
        ///   - The byte immediately before the string is 0x00 (clean delimiter)
        ///     or the string starts at offset 0.
        ///   - No user <see cref="Database.Entities.DataFormatOverride"/> covers
        ///     any byte of the candidate range — overrides always win.
        /// </summary>
        private static Dictionary<int, StringScanner.Hit> ComputeAutoStringRanges(
            byte[] byteBuf,
            IReadOnlyList<Database.Entities.DataFormatOverride> overrides,
            uint baseAddr)
        {
            var byOffset = new Dictionary<int, StringScanner.Hit>();
            if (byteBuf.Length == 0) return byOffset;

            foreach (var hit in StringScanner.FindStrings(byteBuf, baseAddr, MinAutoStringLength))
            {
                if (hit.ByteOffset > 0 && byteBuf[hit.ByteOffset - 1] != 0x00) continue;

                int endExclusive = hit.ByteOffset + hit.Length + 1;
                if (endExclusive > byteBuf.Length) continue;

                long rangeStart = baseAddr + (uint)hit.ByteOffset;
                long rangeEnd   = baseAddr + (uint)endExclusive;
                bool overlaps = false;
                foreach (var ovr in overrides)
                {
                    long ovrEnd = ovr.Address + ovr.Length;
                    if (ovr.Address < rangeEnd && ovrEnd > rangeStart) { overlaps = true; break; }
                }
                if (overlaps) continue;

                byOffset[hit.ByteOffset] = hit;
            }
            return byOffset;
        }

        /// <summary>
        /// Returns the byte length of the printable-ASCII run starting at
        /// <paramref name="index"/>, including the null terminator. Returns 0
        /// when the run is shorter than 3 visible chars, when a non-printable
        /// byte appears before the null, or when no null is found within the
        /// buffer. Common whitespace (\t, \n, \r) counts as printable so PS2
        /// diagnostic strings like "invalid type\n\0" aren't aborted at the
        /// newline. Used by the byte-override path to auto-extend its span to
        /// cover the whole string a user gestured at.
        /// </summary>
        private static int MeasureNullTerminatedAscii(byte[] buf, int index)
        {
            if (index < 0 || index >= buf.Length) return 0;
            int i = index;
            while (i < buf.Length)
            {
                byte b = buf[i];
                if (b == 0)
                {
                    int len = i - index;
                    return len >= 3 ? (len + 1) : 0;
                }
                if (!StringScanner.IsAsciiStringChar(b)) return 0;
                i++;
            }
            return 0; // ran off the buffer without finding null
        }

        private static string? TryResolveInlineAsciiString(byte[] buf, int index, int maxBytes)
        {
            if (maxBytes <= 0 || index < 0 || index >= buf.Length) return null;

            int end = System.Math.Min(buf.Length, index + maxBytes);
            var sb = new System.Text.StringBuilder();
            for (int i = index; i < end; i++)
            {
                byte b = buf[i];
                if (b == 0)
                {
                    return sb.Length >= 3 ? $"-> \"{EscapeCommentString(sb.ToString())}\"" : null;
                }
                if (!StringScanner.IsAsciiStringChar(b)) return null;
                sb.Append((char)b);
            }

            return sb.Length >= 3 ? $"-> \"{EscapeCommentString(sb.ToString())}\"" : null;
        }

        /// <summary>
        /// When <paramref name="value"/> looks like a PS2 RAM pointer, returns
        /// a <c>-&gt; 0xADDRESS</c> comment and appends a printable ASCII string
        /// when one can be read from the supplied memory bus.
        ///
        /// <para>
        /// Plausible pointer windows:
        /// <list type="bullet">
        ///   <item><c>0x00100000–0x02000000</c> — physical EE RAM (32 MB,
        ///         skipping the first 1 MB of kernel reserved area).</item>
        ///   <item><c>0x80100000–0x82000000</c> — KSEG0 mirror of the same.</item>
        /// </list>
        /// </para>
        /// </summary>
        private static string? TryResolvePointerComment(
            uint value, CodeDesigner.Ps2.Common.Interfaces.IPs2MemoryBus? bus)
        {
            if (!LooksLikeRamPointer(value)) return null;
            string target = $"0x{value:X8}";
            if (bus is null) return $"-> {target}";

            try
            {
                const int MaxLen = 64;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < MaxLen; i++)
                {
                    byte b = bus.ReadByte((uint)(value + i));
                    if (b == 0)
                    {
                        // Require at least 3 printable chars to consider this a
                        // real string and not random data that happens to start
                        // with a zero byte.
                        if (sb.Length < 3) return $"-> {target}";
                        return $"-> {target} \"{EscapeCommentString(sb.ToString())}\"";
                    }
                    if (!StringScanner.IsAsciiStringChar(b)) return $"-> {target}";
                    sb.Append((char)b);
                }
                if (sb.Length >= 3)
                    return $"-> {target} \"{EscapeCommentString(sb.ToString())}...\"";
                return $"-> {target}";
            }
            catch
            {
                return $"-> {target}";
            }
        }

        private static string EscapeCommentString(string text) =>
            text.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>Bytes before a page that the analyzer simulates (not shown) to carry register state in.</summary>
        private const int LeadInBytes = 0x80;

        /// <summary>
        /// Longest stretch of a function above the page that is simulated. Real
        /// functions are far shorter; this only bounds a mis-detected one.
        /// </summary>
        private const uint MaxFunctionLeadIn = 0x4000;

        /// <summary>
        /// Maps a CPU address to the form live panels disassemble at: KSEG0/KSEG1
        /// aliases (0x80000000–0xBFFFFFFF) drop to their physical address.
        /// </summary>
        private static uint ToPagePhysical(uint address)
        {
            return address >= 0x80000000u && address < 0xC0000000u ? address & 0x1FFFFFFFu : address;
        }

        /// <summary>True when <paramref name="address"/> falls inside the EE RAM windows usable as a pointer target.</summary>
        private static bool LooksLikeRamPointer(uint address)
        {
            return (address >= 0x00100000u && address <  0x02000000u)
                || (address >= 0x80100000u && address <  0x82000000u);
        }

        // ──────────────────────────────────────────────────────────────────────
        // syscall annotation — static $v1-load detection
        // ──────────────────────────────────────────────────────────────────────

        // MIPS R5900 instruction layout (encoded in Hex like "0x00000000C"):
        //   syscall:                        opcode=000000, funct=001100  →  word & 0x3F == 0x0C with opcode=0
        //   addiu rt, rs, imm  (opcode 9):  bits 31:26 = 001001
        //   ori   rt, rs, imm  (opcode 13): bits 31:26 = 001101
        //   lui   rt, imm      (opcode 15): bits 31:26 = 001111
        //   addi  rt, rs, imm  (opcode 8):  bits 31:26 = 001000 (rarely used in PS2 code)
        //
        // The destination reg (rt) is bits 20:16. We're looking for rt == 3 ($v1).

        private const int RegV1 = 3;

        /// <summary>
        /// Walk <paramref name="results"/> looking for <c>syscall</c> rows whose
        /// embedded code field is zero (the common PS2 calling convention loads
        /// the call number into <c>$v1</c> via an addiu/ori immediately before
        /// the syscall). When the preceding non-delay instruction matches that
        /// pattern, resolve the immediate against the EE BIOS table and append
        /// the syscall name to the row's comment.
        /// </summary>
        private static void AnnotateSyscallsFromPriorV1Load(List<InstructionResult> results)
        {
            for (int i = 1; i < results.Count; i++)
            {
                var row = results[i];
                if (!row.Success) continue;
                if (row.ByteLength is not null and not 4) continue;       // skip data rows
                if (!TryParseHex(row.Hex, out uint instr)) continue;
                if (!IsSyscallInstruction(instr)) continue;

                // Use the embedded code field first when non-zero.
                uint embeddedCode = (instr >> 6) & 0xFFFFF;
                if (embeddedCode != 0)
                {
                    string? embeddedName = CodeDesigner.Ps2.Common.Disassembler.EeSyscallTable
                        .Resolve((int)embeddedCode);
                    if (embeddedName is not null && !ContainsName(row.Comment, embeddedName))
                        row.Comment = AppendSyscallName(row.Comment, embeddedCode, embeddedName);
                    continue;
                }

                // code == 0: look back for the most recent instr writing $v1.
                // Skip the branch-delay slot (typical pattern is `li $v1, N; syscall`,
                // so the immediate write is the row directly above).
                int? immediate = FindV1ImmediateBefore(results, i);
                if (immediate is null) continue;

                int num = immediate.Value;
                // Negative-call convention: PS2 kernel-mode entry uses -N.
                int callNum = num < 0 ? -num : num;
                string? name = CodeDesigner.Ps2.Common.Disassembler.EeSyscallTable.Resolve(callNum);
                if (name is null) continue;

                if (!ContainsName(row.Comment, name))
                    row.Comment = AppendSyscallName(row.Comment, (uint)callNum, name);
            }
        }

        private static bool IsSyscallInstruction(uint instr)
        {
            return (instr >> 26) == 0u && (instr & 0x3Fu) == 0x0Cu;
        }

        /// <summary>
        /// Computes the value <c>$v1</c> holds at <paramref name="syscallIndex"/> by
        /// walking forward over the few instruction rows before it. Handles
        /// <c>li</c> (addiu/ori from $zero), <c>lui</c>, and <c>lui</c> followed by
        /// <c>ori</c>/<c>addiu</c> (sign-extended add). Any other write to
        /// <c>$v1</c>, or a jump in the window, makes the value unknown.
        /// Data rows are skipped. Returns null when the value isn't known.
        /// </summary>
        private static int? FindV1ImmediateBefore(List<InstructionResult> results, int syscallIndex)
        {
            const int Window = 6; // instruction rows; li/lui+ori sit right above the syscall

            int start = syscallIndex;
            for (int seen = 0; start > 0 && seen < Window; )
            {
                start--;
                var r = results[start];
                if (r.Success && r.ByteLength is null or 4) seen++;
            }

            bool known = false;
            int v1 = 0;
            for (int k = start; k < syscallIndex; k++)
            {
                var r = results[k];
                if (!r.Success || r.ByteLength is not null and not 4) continue;
                if (!TryParseHex(r.Hex, out uint instr)) { known = false; continue; }

                var usage = CodeDesigner.Ps2.Common.GprUsage.Of(instr);
                if (usage.Flow == CodeDesigner.Ps2.Common.FlowKind.Jump) known = false;
                if ((usage.Writes & (1u << RegV1)) == 0) continue;

                uint op = instr >> 26;
                int  rs = (int)((instr >> 21) & 0x1F);
                int  imm = unchecked((short)(instr & 0xFFFF));
                switch (op)
                {
                    case 0x08: case 0x09: case 0x19:   // addi / addiu / daddiu
                        if (rs == 0) { v1 = imm; known = true; }
                        else if (rs == RegV1 && known) v1 += imm;
                        else known = false;
                        break;
                    case 0x0D:                         // ori
                        if (rs == 0) { v1 = imm & 0xFFFF; known = true; }
                        else if (rs == RegV1 && known) v1 |= imm & 0xFFFF;
                        else known = false;
                        break;
                    case 0x0F:                         // lui
                        v1 = (imm & 0xFFFF) << 16;
                        known = true;
                        break;
                    default:
                        known = false;
                        break;
                }
            }
            return known ? v1 : null;
        }

        private static bool TryParseHex(string? hex, out uint value)
        {
            value = 0;
            if (string.IsNullOrEmpty(hex)) return false;
            var trimmed = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? hex.AsSpan(2) : hex.AsSpan();
            return uint.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out value);
        }

        private static bool ContainsName(string? comment, string name) =>
            comment is not null && comment.Contains(name, StringComparison.Ordinal);

        private static string AppendSyscallName(string? existing, uint num, string name)
        {
            string addition = $"syscall #{num} ({name})";
            return string.IsNullOrEmpty(existing) ? addition : $"{existing}  {addition}";
        }
    }
}
