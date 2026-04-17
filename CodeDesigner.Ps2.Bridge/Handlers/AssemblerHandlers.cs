using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodeDesigner.Ps2.Common;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── Compiled regexes for CDS compilation (avoid per-line recompilation) ──
        /// <summary>Matches a bare label definition line: "Name:"  (identifier + colon, optional trailing whitespace).</summary>
        private static readonly Regex CdsLabelDefRegex =
            new(@"^([A-Za-z_][A-Za-z0-9_]*):\s*$",
                RegexOptions.Compiled);

        /// <summary>
        /// Matches a label reference inside an instruction operand: ":Name" or ":scope.name".
        /// Dots are allowed to support pointer-manager scoped labels (e.g. <c>:seal.health</c>).
        /// </summary>
        private static readonly Regex CdsLabelRefRegex =
            new(@":([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)",
                RegexOptions.Compiled);

        /// <summary>
        /// Matches an absolute-address label reference: "@Name" or "@scope.name".
        /// Dots are allowed to support pointer-manager scoped labels (e.g. <c>@seal.health</c>).
        /// Replaced with the raw hex address of the label (e.g. 0x80010018),
        /// suitable for use with jump instructions or absolute address operands.
        /// </summary>
        private static readonly Regex CdsAbsLabelRefRegex =
            new(@"@([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)",
                RegexOptions.Compiled);

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
    }
}
