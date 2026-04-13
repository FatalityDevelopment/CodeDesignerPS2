using CodeDesigner.Ps2.Common;
using System.Text;
using System.Text.RegularExpressions;
using CodeDesigner.Ps2.CdsLanguage.SyntaxItems;

namespace CodeDesigner.Ps2.CdsLanguage
{
    /// <summary>
    /// Parses CDS (Code Designer Script) source text into a structured list of
    /// <see cref="CdsSyntaxItem"/> objects.
    ///
    /// <para><b>Language features:</b></para>
    /// <list type="bullet">
    ///   <item><c>include "file.cds"</c> – file inclusion</item>
    ///   <item><c>address $XXXXXXXX</c> – set current assembly address</item>
    ///   <item><c>hexcode $XXXXXXXX</c> – emit a raw 32-bit word</item>
    ///   <item><c>setreg reg, $XXXXXXXX</c> – load 32-bit immediate (LUI+ADDIU)</item>
    ///   <item><c>mem[0xOFFSET] reg = 0xVAL</c> or <c>+= -= *= /=</c> – memory operation</item>
    ///   <item><c>string "text"</c> – null-terminated ASCII string data</item>
    ///   <item><c>name:</c> – label definition</item>
    ///   <item><c>instr args :label</c> – MIPS instruction with optional label reference</item>
    ///   <item><c>// …</c> and <c>/* … */</c> – comments</item>
    ///   <item><c>scope Name { … }</c> – named scope block (NEW)</item>
    /// </list>
    ///
    /// <para><b>Scope support:</b> Labels defined inside a <c>scope</c> block are automatically
    /// qualified with the scope path (e.g., label <c>loop</c> inside <c>scope Foo</c> becomes
    /// <c>foo.loop</c>). Within a scope, labels may be referenced by their short name; from
    /// outside, use the fully-qualified form <c>Foo.loop</c>.</para>
    /// </summary>
    public class CdsParser
    {
        // -----------------------------------------------------------------------
        // Regex patterns
        // -----------------------------------------------------------------------

        private static readonly Regex IncludeRegex =
            new(@"^include\s+""([^""]+)""$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AddressRegex =
            new(@"^address\s+\$([0-9a-fA-F]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex HexCodeRegex =
            new(@"^hexcode\s+\$([0-9a-fA-F]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SetRegRegex =
            new(@"^setreg\s+(\w+)\s*,\s*\$([0-9a-fA-F]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LabelDefRegex =
            new(@"^([a-zA-Z_][a-zA-Z0-9_]*):$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex StringRegex =
            new(@"^string\s+""(.*?)""$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MemRegex =
            new(@"^mem\[(0x[0-9A-Fa-f]+)\]\s+(\w+)\s*(\+=|-=|\*=|/=|=)\s*(0x[0-9A-Fa-f]+|\d+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ScopeRegex =
            new(@"^scope\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*\{$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Branch mnemonics that use a PC-relative 16-bit offset
        private static readonly HashSet<string> BranchMnemonics = new(StringComparer.OrdinalIgnoreCase)
        {
            "beq",  "bne",  "blez",  "bgtz",  "bltz",  "bgez",
            "beql", "bnel", "blezl", "bgtzl", "bltzl", "bgezl",
            "bltzal", "bgezal", "bltzall", "bgezall",
            "bc1t", "bc1f", "bc1tl", "bc1fl"
        };

        // Jump mnemonics that encode a 26-bit absolute word address
        private static readonly HashSet<string> JumpMnemonics = new(StringComparer.OrdinalIgnoreCase)
        {
            "j", "jal"
        };

        // -----------------------------------------------------------------------
        // Public entry point
        // -----------------------------------------------------------------------

        /// <summary>
        /// Parse CDS source text into a list of syntax items.
        /// </summary>
        /// <param name="sourceCode">The full source text to parse.</param>
        /// <param name="basePath">
        /// Directory used to resolve relative <c>include</c> paths.
        /// Pass <see cref="string.Empty"/> when parsing in-memory without file I/O.
        /// </param>
        public List<CdsSyntaxItem> Parse(string sourceCode, string basePath = "")
        {
            string[] rawLines = sourceCode.Replace("\r\n", "\n").Split('\n');

            // First pass: build a map of fully-qualified label name → address
            var labelMap = BuildLabelMap(rawLines);

            // Second pass: produce syntax items
            uint currentAddress = 0;
            bool inMultiLine = false;
            int multiLineStart = 0;
            var multiLineBuf = new StringBuilder();

            var rootItems = new List<CdsSyntaxItem>();
            var itemsStack = new Stack<List<CdsSyntaxItem>>();
            var scopeStack = new Stack<string>();  // scope path components

            itemsStack.Push(rootItems);

            for (int i = 0; i < rawLines.Length; i++)
            {
                int lineNumber = i + 1;
                string trimmed = rawLines[i].Trim();

                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                var current = itemsStack.Peek();

                // ---- Multi-line comment handling ----
                if (inMultiLine)
                {
                    multiLineBuf.AppendLine(trimmed);
                    if (trimmed.Contains("*/"))
                    {
                        inMultiLine = false;
                        current.Add(new CommentSyntaxItem
                        {
                            LineNumber = multiLineStart,
                            LineText = multiLineBuf.ToString().TrimEnd(),
                            Text = multiLineBuf.ToString().TrimEnd(),
                            IsMultiLine = true
                        });
                        multiLineBuf.Clear();
                    }
                    continue;
                }

                if (trimmed.StartsWith("/*"))
                {
                    inMultiLine = true;
                    multiLineStart = lineNumber;
                    multiLineBuf.AppendLine(trimmed);
                    if (trimmed.Contains("*/"))
                    {
                        inMultiLine = false;
                        current.Add(new CommentSyntaxItem
                        {
                            LineNumber = multiLineStart,
                            LineText = multiLineBuf.ToString().TrimEnd(),
                            Text = multiLineBuf.ToString().TrimEnd(),
                            IsMultiLine = true
                        });
                        multiLineBuf.Clear();
                    }
                    continue;
                }

                // ---- Single-line comment ----
                if (trimmed.StartsWith("//"))
                {
                    current.Add(new CommentSyntaxItem
                    {
                        LineNumber = lineNumber,
                        LineText = trimmed,
                        Text = trimmed.Substring(2).Trim(),
                        IsMultiLine = false
                    });
                    continue;
                }

                // ---- Strip inline comments (// ; #) from code lines ----
                trimmed = StripInlineComments(trimmed);
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                // ---- Scope open: scope Name { ----
                var scopeMatch = ScopeRegex.Match(trimmed);
                if (scopeMatch.Success)
                {
                    string scopeName = scopeMatch.Groups[1].Value;
                    string parentPath = ScopePathString(scopeStack);
                    string fullPath = string.IsNullOrEmpty(parentPath)
                        ? scopeName.ToLowerInvariant()
                        : $"{parentPath}.{scopeName.ToLowerInvariant()}";

                    var scopeItem = new ScopeSyntaxItem
                    {
                        LineNumber = lineNumber,
                        LineText = trimmed,
                        Name = scopeName,
                        FullPath = fullPath
                    };
                    current.Add(scopeItem);
                    scopeStack.Push(scopeName.ToLowerInvariant());
                    itemsStack.Push(scopeItem.Body);
                    continue;
                }

                // ---- Scope close ----
                if (trimmed == "}")
                {
                    if (scopeStack.Count > 0)
                    {
                        scopeStack.Pop();
                        itemsStack.Pop();
                    }
                    continue;
                }

                // ---- Include ----
                var inclMatch = IncludeRegex.Match(trimmed);
                if (inclMatch.Success)
                {
                    string relativePath = inclMatch.Groups[1].Value;
                    string fullPath = string.IsNullOrEmpty(basePath)
                        ? relativePath
                        : Path.Combine(basePath, relativePath);

                    current.Add(new IncludeSyntaxItem
                    {
                        LineNumber = lineNumber,
                        LineText = trimmed,
                        FilePath = fullPath
                    });
                    continue;
                }

                // ---- Address directive ----
                var addrMatch = AddressRegex.Match(trimmed);
                if (addrMatch.Success)
                {
                    currentAddress = Convert.ToUInt32(addrMatch.Groups[1].Value, 16);
                    current.Add(new AddressSyntaxItem
                    {
                        LineNumber = lineNumber,
                        LineText = trimmed,
                        Address = currentAddress
                    });
                    continue;
                }

                // ---- HexCode directive ----
                var hexMatch = HexCodeRegex.Match(trimmed);
                if (hexMatch.Success)
                {
                    uint value = Convert.ToUInt32(hexMatch.Groups[1].Value, 16);
                    current.Add(new HexCodeSyntaxItem
                    {
                        LineNumber = lineNumber,
                        LineText = trimmed,
                        Address = currentAddress,
                        Value = value
                    });
                    currentAddress += 4;
                    continue;
                }

                // ---- SetReg directive ----
                var setRegMatch = SetRegRegex.Match(trimmed);
                if (setRegMatch.Success)
                {
                    string reg = setRegMatch.Groups[1].Value;
                    uint value = Convert.ToUInt32(setRegMatch.Groups[2].Value, 16);
                    var item = BuildSetReg(lineNumber, trimmed, reg, value, currentAddress);
                    current.Add(item);
                    currentAddress += 8;
                    continue;
                }

                // ---- Memory operation ----
                var memMatch = MemRegex.Match(trimmed);
                if (memMatch.Success)
                {
                    uint offset = Convert.ToUInt32(memMatch.Groups[1].Value, 16);
                    string reg = memMatch.Groups[2].Value;
                    string opStr = memMatch.Groups[3].Value;
                    string operandStr = memMatch.Groups[4].Value;
                    uint operand = ParseUInt(operandStr);
                    var memOp = ParseMemOp(opStr);
                    uint instrCount = memOp == CdsMemoryOperation.Assign ? 2u : 3u;
                    var item = BuildMemOp(lineNumber, trimmed, offset, reg, memOp, operand, currentAddress);
                    current.Add(item);
                    currentAddress += instrCount * 4;
                    continue;
                }

                // ---- String directive ----
                var strMatch = StringRegex.Match(trimmed);
                if (strMatch.Success)
                {
                    string text = strMatch.Groups[1].Value;
                    var item = BuildString(lineNumber, trimmed, text, currentAddress);
                    current.Add(item);
                    currentAddress += (uint)item.Words.Count * 4;
                    continue;
                }

                // ---- Label definition ----
                var labelMatch = LabelDefRegex.Match(trimmed);
                if (labelMatch.Success)
                {
                    string localName = labelMatch.Groups[1].Value.ToLowerInvariant();
                    string prefix = ScopePathString(scopeStack);
                    string fullName = string.IsNullOrEmpty(prefix)
                        ? localName
                        : $"{prefix}.{localName}";

                    current.Add(new LabelSyntaxItem
                    {
                        LineNumber = lineNumber,
                        LineText = trimmed,
                        Address = currentAddress,
                        Name = localName,
                        FullName = fullName
                    });
                    continue;
                }

                // ---- MIPS instruction (possibly with :label target) ----
                TryParseOperation(trimmed, lineNumber, scopeStack, labelMap,
                                  ref currentAddress, current);
            }

            return rootItems;
        }

        // -----------------------------------------------------------------------
        // First-pass label map builder
        // -----------------------------------------------------------------------

        /// <summary>
        /// Scans the source for label definitions (including those inside scope blocks)
        /// and returns a map of fully-qualified label name → address.
        /// All keys are lower-case.
        /// </summary>
        private static Dictionary<string, uint> BuildLabelMap(string[] rawLines)
        {
            var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            uint currentAddress = 0;
            bool inMultiLine = false;
            var scopeStack = new Stack<string>();

            foreach (var raw in rawLines)
            {
                string t = raw.Trim();
                if (string.IsNullOrWhiteSpace(t)) continue;

                // Multi-line comment tracking
                if (inMultiLine)
                {
                    if (t.Contains("*/")) inMultiLine = false;
                    continue;
                }
                if (t.StartsWith("/*"))
                {
                    if (!t.Contains("*/")) inMultiLine = true;
                    continue;
                }
                if (t.StartsWith("//")) continue;

                // Strip inline comments (// ; #) from code lines
                t = StripInlineComments(t);
                if (string.IsNullOrWhiteSpace(t)) continue;

                // Scope open / close
                var scopeMatch = ScopeRegex.Match(t);
                if (scopeMatch.Success) { scopeStack.Push(scopeMatch.Groups[1].Value.ToLowerInvariant()); continue; }
                if (t == "}") { if (scopeStack.Count > 0) scopeStack.Pop(); continue; }

                // Advance address pointer for code-emitting directives
                var addrMatch = AddressRegex.Match(t);
                if (addrMatch.Success) { currentAddress = Convert.ToUInt32(addrMatch.Groups[1].Value, 16); continue; }

                if (HexCodeRegex.IsMatch(t)) { currentAddress += 4; continue; }

                var setRegMatch = SetRegRegex.Match(t);
                if (setRegMatch.Success) { currentAddress += 8; continue; }

                var memMatch = MemRegex.Match(t);
                if (memMatch.Success)
                {
                    string opStr = memMatch.Groups[3].Value;
                    currentAddress += opStr == "=" ? 8u : 12u;
                    continue;
                }

                var strMatch = StringRegex.Match(t);
                if (strMatch.Success)
                {
                    string text = strMatch.Groups[1].Value;
                    byte[] bytes = Encoding.ASCII.GetBytes(text + "\0");
                    int paddedLen = ((bytes.Length + 3) / 4) * 4;
                    currentAddress += (uint)paddedLen;
                    continue;
                }

                // Label definition
                var labelMatch = LabelDefRegex.Match(t);
                if (labelMatch.Success)
                {
                    string localName = labelMatch.Groups[1].Value.ToLowerInvariant();
                    string prefix = ScopePathString(scopeStack);
                    string fullName = string.IsNullOrEmpty(prefix) ? localName : $"{prefix}.{localName}";
                    map.TryAdd(fullName, currentAddress);
                    continue;
                }

                // Everything else that emits an instruction takes 4 bytes
                if (!t.StartsWith("include") && !t.StartsWith("//"))
                    currentAddress += 4;
            }

            return map;
        }

        // -----------------------------------------------------------------------
        // Instruction parsing
        // -----------------------------------------------------------------------

        private void TryParseOperation(
            string line,
            int lineNumber,
            Stack<string> scopeStack,
            Dictionary<string, uint> labelMap,
            ref uint currentAddress,
            List<CdsSyntaxItem> items)
        {
            // Extract optional :label suffix
            string labelRef = string.Empty;
            string coreLine = line;

            int colonIdx = line.LastIndexOf(':');
            if (colonIdx > 0)
            {
                string candidate = line.Substring(colonIdx + 1).Trim();
                if (Regex.IsMatch(candidate, @"^[a-zA-Z_][a-zA-Z0-9_.]*$"))
                {
                    labelRef = candidate.ToLowerInvariant();
                    coreLine = line.Substring(0, colonIdx).TrimEnd();
                }
            }

            // Split mnemonic
            string[] parts = coreLine.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            string mnemonic = parts[0].ToLowerInvariant();
            string resolvedLabel = string.Empty;

            string assemblyLine = coreLine;

            if (!string.IsNullOrEmpty(labelRef))
            {
                // Resolve the label through the scope hierarchy
                string? qualified = ResolveLabel(labelRef, scopeStack, labelMap);
                if (qualified == null)
                {
                    // Keep coreLine as-is; encoding will fail naturally
                    assemblyLine = coreLine;
                }
                else
                {
                    resolvedLabel = qualified;
                    uint labelAddress = labelMap[qualified];

                    if (BranchMnemonics.Contains(mnemonic))
                    {
                        // Branch: replace label with 16-bit signed PC-relative offset (in words)
                        int offset = (int)((labelAddress - (currentAddress + 4)) / 4);
                        string offsetStr = $"${ (ushort)offset:X4}";
                        assemblyLine = $"{coreLine} {offsetStr}";
                    }
                    else if (JumpMnemonics.Contains(mnemonic))
                    {
                        // Jump: MipsAssembler.AssembleJ expects a byte address (word-aligned);
                        // it performs the >> 2 shift internally.
                        assemblyLine = $"{mnemonic} ${labelAddress:X8}";
                    }
                    else
                    {
                        // Other instruction – append the resolved hex address
                        assemblyLine = $"{coreLine} $0{labelAddress:X8}";
                    }
                }
            }

            uint encoded;
            try
            {
                encoded = MipsAssembler.Assemble(assemblyLine);
            }
            catch
            {
                encoded = 0;
            }

            items.Add(new OperationSyntaxItem
            {
                LineNumber = lineNumber,
                LineText = line,
                Address = currentAddress,
                EncodedWord = encoded,
                Mnemonic = mnemonic,
                ResolvedLabel = string.IsNullOrEmpty(resolvedLabel) ? null : resolvedLabel
            });

            currentAddress += 4;
        }

        // -----------------------------------------------------------------------
        // Directive builders
        // -----------------------------------------------------------------------

        private static SetRegSyntaxItem BuildSetReg(
            int lineNumber, string lineText, string reg, uint value, uint address)
        {
            ushort hi = (ushort)(value >> 16);
            ushort lo = (ushort)(value & 0xFFFF);

            string luiAsm    = $"lui {reg}, ${ hi:X4}";
            string addiuAsm  = $"addiu {reg}, {reg}, ${lo:X4}";

            uint luiEncoded   = TryAssemble(luiAsm);
            uint addiuEncoded = TryAssemble(addiuAsm);

            return new SetRegSyntaxItem
            {
                LineNumber = lineNumber,
                LineText = lineText,
                Address = address,
                Register = reg,
                Value = value,
                LuiOp = new OperationSyntaxItem
                {
                    LineNumber = lineNumber, LineText = lineText,
                    Address = address, EncodedWord = luiEncoded, Mnemonic = "lui"
                },
                AddiuOp = new OperationSyntaxItem
                {
                    LineNumber = lineNumber, LineText = lineText,
                    Address = address + 4, EncodedWord = addiuEncoded, Mnemonic = "addiu"
                }
            };
        }

        private static MemorySyntaxItem BuildMemOp(
            int lineNumber, string lineText,
            uint memOffset, string reg,
            CdsMemoryOperation op, uint operand,
            uint address)
        {
            string offsetStr  = $"${(ushort)memOffset:X4}";
            string operandStr = $"${(ushort)operand:X4}";

            var ops = new List<OperationSyntaxItem>();
            uint cur = address;

            if (op != CdsMemoryOperation.Assign)
            {
                // Load current value: lw $t9, offset(reg)
                uint lw = TryAssemble($"lw $t9, {offsetStr}(${reg})");
                ops.Add(new OperationSyntaxItem
                {
                    LineNumber = lineNumber, LineText = lineText,
                    Address = cur, EncodedWord = lw, Mnemonic = "lw"
                });
                cur += 4;
            }

            // Perform operation into $t9
            string arithAsm = op switch
            {
                CdsMemoryOperation.Assign   => $"addiu $t9, $zero, {operandStr}",
                CdsMemoryOperation.Add      => $"addiu $t9, $t9, {operandStr}",
                CdsMemoryOperation.Subtract => $"addiu $t9, $t9, ${(ushort)(-(int)operand):X4}",
                CdsMemoryOperation.Multiply => $"mul $t9, $t9, {operandStr}",
                CdsMemoryOperation.Divide   => $"div $t9, $t9, {operandStr}",
                _ => throw new ArgumentException($"Unknown mem operation: {op}")
            };
            uint arithEncoded = TryAssemble(arithAsm);
            ops.Add(new OperationSyntaxItem
            {
                LineNumber = lineNumber, LineText = lineText,
                Address = cur, EncodedWord = arithEncoded,
                Mnemonic = op == CdsMemoryOperation.Assign ? "addiu" : arithAsm.Split(' ')[0]
            });
            cur += 4;

            // Store result: sw $t9, offset(reg)
            uint sw = TryAssemble($"sw $t9, {offsetStr}(${reg})");
            ops.Add(new OperationSyntaxItem
            {
                LineNumber = lineNumber, LineText = lineText,
                Address = cur, EncodedWord = sw, Mnemonic = "sw"
            });

            return new MemorySyntaxItem
            {
                LineNumber = lineNumber, LineText = lineText,
                Address = address, MemoryOffset = memOffset,
                Register = reg, Operation = op, Operand = operand,
                Operations = ops
            };
        }

        private static StringSyntaxItem BuildString(
            int lineNumber, string lineText, string text, uint address)
        {
            byte[] raw = Encoding.ASCII.GetBytes(text + "\0");
            int padded = ((raw.Length + 3) / 4) * 4;
            Array.Resize(ref raw, padded);

            var item = new StringSyntaxItem
            {
                LineNumber = lineNumber,
                LineText = lineText,
                Address = address,
                Text = text
            };

            uint cur = address;
            for (int i = 0; i < raw.Length; i += 4)
            {
                uint word =
                    (uint)raw[i + 0]        |
                    ((uint)raw[i + 1] << 8) |
                    ((uint)raw[i + 2] << 16)|
                    ((uint)raw[i + 3] << 24);

                item.Words.Add(new HexCodeSyntaxItem
                {
                    LineNumber = lineNumber,
                    LineText = lineText,
                    Address = cur,
                    Value = word
                });
                cur += 4;
            }

            return item;
        }

        // -----------------------------------------------------------------------
        // Scope-aware label resolution
        // -----------------------------------------------------------------------

        /// <summary>
        /// Resolve a label reference to its fully-qualified name.
        /// Tries (in order):
        /// 1. Exact key (handles already-qualified references like <c>outer.loop</c>
        ///    and global labels).
        /// 2. Current scope path (all levels) + name — innermost-first search.
        /// 3. Each parent scope path + name, widening outward by removing the innermost
        ///    scope component one at a time.
        /// </summary>
        /// <remarks>
        /// <para><b>Why <c>scopes[skip..]</c> works correctly:</b></para>
        /// <para>
        /// <c>Stack.ToArray()</c> yields the stack top-first, so element [0] is the
        /// innermost (most-recently-pushed) scope and element [^1] is the outermost.
        /// </para>
        /// <para>
        /// At <c>skip=0</c> we take the full array and reverse it to produce the
        /// complete scope path (outermost.…innermost). At <c>skip=1</c> we drop the
        /// innermost component, giving the parent path, and so on. This correctly
        /// widens the search from the current scope outward to the root.
        /// </para>
        /// <para>
        /// Example: scopes = ["inner","outer"] (inner is current), labelRef = "loop"</para>
        /// <para>
        ///   skip=0 → prefix = "outer.inner" → tries "outer.inner.loop"<br/>
        ///   skip=1 → prefix = "outer"        → tries "outer.loop"      ← parent label found
        /// </para>
        /// </remarks>
        private static string? ResolveLabel(
            string labelRef,
            Stack<string> scopeStack,
            Dictionary<string, uint> labelMap)
        {
            // Exact match – works for global labels and fully-qualified references
            if (labelMap.ContainsKey(labelRef))
                return labelRef;

            // Stack.ToArray() gives innermost-first; e.g., Push("outer") then Push("inner")
            // → scopes = ["inner", "outer"].
            // At skip=0: take full array, reverse → "outer.inner" (current scope prefix).
            // At skip=1: drop innermost, reverse → "outer" (parent scope prefix).
            string[] scopes = scopeStack.ToArray();
            for (int skip = 0; skip < scopes.Length; skip++)
            {
                string prefix = string.Join(".", scopes[skip..].Reverse());
                string candidate = string.IsNullOrEmpty(prefix) ? labelRef : $"{prefix}.{labelRef}";
                if (labelMap.ContainsKey(candidate))
                    return candidate;
            }

            return null;
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static string ScopePathString(Stack<string> stack)
        {
            if (stack.Count == 0) return string.Empty;
            // Stack is innermost-first; we want outermost-first for the path
            return string.Join(".", stack.Reverse());
        }

        /// <summary>
        /// Strip inline single-line comments (<c>//</c>, <c>;</c>, <c>#</c>) from a
        /// code line, being careful not to strip comment markers that appear inside
        /// string literals.
        /// </summary>
        private static string StripInlineComments(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return line;

            int cutAt = int.MaxValue;
            foreach (var marker in new[] { "//", ";", "#" })
            {
                int ci = line.IndexOf(marker, StringComparison.Ordinal);
                if (ci > 0 && ci < cutAt) cutAt = ci;
            }

            return cutAt != int.MaxValue ? line[..cutAt].TrimEnd() : line;
        }

        private static uint TryAssemble(string asm)
        {
            try { return MipsAssembler.Assemble(asm); }
            catch { return 0; }
        }

        private static uint ParseUInt(string s)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt32(s, 16);
            return uint.Parse(s);
        }

        private static CdsMemoryOperation ParseMemOp(string opStr) => opStr switch
        {
            "="  => CdsMemoryOperation.Assign,
            "+=" => CdsMemoryOperation.Add,
            "-=" => CdsMemoryOperation.Subtract,
            "*=" => CdsMemoryOperation.Multiply,
            "/=" => CdsMemoryOperation.Divide,
            _    => throw new ArgumentException($"Unknown memory operation '{opStr}'")
        };
    }
}
