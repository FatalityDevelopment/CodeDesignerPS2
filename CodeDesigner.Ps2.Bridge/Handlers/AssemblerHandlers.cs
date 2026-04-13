using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.CdsLanguage;
using CodeDesigner.Ps2.CdsLanguage.SyntaxItems;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {

        /// <summary>
        /// Handle COMPILECDS command — compiles a CDS (Code Designer Script) source text
        /// into PS2 GameShark / Action Replay cheat codes.
        ///
        /// <para>
        /// The full CDS language is supported, including all preprocessor features
        /// (const/alias/macro/include/if/else/endif/while/endwhile/repeat/endrepeat),
        /// mem.b/h/w/d typed memory writes, scope blocks, sidecar directives
        /// (assert, breakpoint, watchpoint), and freeze/cond cheat entries.
        /// </para>
        ///
        /// <para>
        /// External label names (from the server label registry and the pointer-manager)
        /// can be injected via <paramref name="externalLabels"/> so that CDS source can
        /// reference them in branch/jump targets and setreg operands.
        /// </para>
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
                    return CreateErrorResponse("No source code provided");

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
                foreach (var kv in _labels)
                    extDict[kv.Value.Label] = kv.Key;

                // ── Parse ────────────────────────────────────────────────────────────
                var parser = new CdsParser();
                var items  = parser.Parse(sourceCode, basePath: "", externalLabels: extDict);

                // ── Build results list ───────────────────────────────────────────────
                var results = new List<InstructionResult>();

                // Diagnostics from the preprocessor / parser → error results
                foreach (var diag in parser.Diagnostics)
                {
                    results.Add(new InstructionResult
                    {
                        Success    = false,
                        Assembly   = diag.SourceText,
                        Error      = diag.Message,
                        LineNumber = diag.LineNumber
                    });
                }

                // Walk the compiled (address, word) pairs and format as GameShark codes
                var compiled = CdsCompiler.Compile(items);
                // Build a lookup from item LineNumber/LineText using the item tree
                // so we can annotate each code with the originating source line.
                var lineByAddress = BuildLineAnnotations(items);

                foreach (var (addr, word) in compiled)
                {
                    lineByAddress.TryGetValue(addr, out var annotation);
                    results.Add(new InstructionResult
                    {
                        Success    = true,
                        Assembly   = annotation?.lineText ?? string.Empty,
                        Hex        = FormatGameSharkCode(addr, word),
                        LineNumber = annotation?.lineNumber
                    });
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
                            d.TryAdd(sr.LuiOp.Address,   (sr.LineNumber, sr.LineText));
                            d.TryAdd(sr.AddiuOp.Address, (sr.LineNumber, sr.LineText));
                            break;

                        case MemorySyntaxItem mem:
                            foreach (var op in mem.Operations)
                                d.TryAdd(op.Address, (mem.LineNumber, mem.LineText));
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

        /// <summary>Format a (address, word) pair as a GameShark type-2 code.</summary>
        private static string FormatGameSharkCode(uint address, uint value)
        {
            // Type-2 prefix (0x20000000) ORed with the low 28 bits of the target address.
            uint codeAddr = 0x20000000u | (address & 0x0FFFFFFFu);
            return $"{codeAddr:X8} {value:X8}";
        }
    }
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
    }
}
