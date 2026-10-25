using CodeDesigner.Ps2.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CodeDesigner.Ps2.CdsLanguage.SyntaxItems;

namespace CodeDesigner.Ps2.CdsLanguage
{
    /// <summary>
    /// Parses CDS (Code Designer Script) source text into a structured list of
    /// <see cref="CdsSyntaxItem"/> objects.
    ///
    /// <para>
    /// Before the two-pass parse the source is run through <see cref="CdsPreprocessor"/>,
    /// which expands high-level constructs (<c>const</c>, <c>alias</c>, <c>macro</c>,
    /// <c>include</c>, <c>if</c>/<c>else</c>/<c>endif</c>, <c>while</c>/<c>endwhile</c>,
    /// <c>repeat</c>/<c>endrepeat</c>, <c>function</c>) into plain CDS.
    /// </para>
    ///
    /// <para><b>Language features (after preprocessing):</b></para>
    /// <list type="bullet">
    ///   <item><c>address $XXXXXXXX</c> – set current assembly address (<c>$hex</c>, <c>0xhex</c> or bare hex)</item>
    ///   <item><c>hexcode $XXXXXXXX</c> – emit a raw 32-bit word</item>
    ///   <item><c>setreg reg, $XXXXXXXX</c> – load 32-bit immediate (LUI+ORI)</item>
    ///   <item><c>li reg, value</c> – load immediate; one word when it fits 16 bits, otherwise LUI+ORI</item>
    ///   <item><c>la reg, :label</c> – load the address of a label (LUI+ORI)</item>
    ///   <item><c>mem[0xOFFSET] reg = 0xVAL</c> – memory write through a base register (default 32-bit)</item>
    ///   <item><c>mem.b / mem.h / mem.w / mem.d</c> – sized memory operations (8/16/32/64-bit)</item>
    ///   <item><c>load[.b/.h/.w/.d] dst, mem[0xOFFSET] base</c> – typed memory loads</item>
    ///   <item><c>store[.b/.h/.w/.d] src, mem[0xOFFSET] base</c> – typed memory stores</item>
    ///   <item><c>pointer $BASE + $OFF1 [+ $OFF2 ...] = $VALUE</c> – pointer-chain write expansion</item>
    ///   <item><c>string "text"</c> – null-terminated ASCII string data</item>
    ///   <item><c>name:</c> – label definition</item>
    ///   <item><c>instr args :label</c> – MIPS instruction with a label operand: branches get a
    ///   PC-relative offset, <c>j</c>/<c>jal</c> the absolute target, <c>lui</c> the upper 16 bits and
    ///   every other immediate instruction the lower 16 bits. <c>hi(:label)</c> / <c>lo(:label)</c>
    ///   select a half explicitly.</item>
    ///   <item><c>blt/bgt/ble/bge[u] rs, rt, :label</c> – two-word branch pseudo-instructions (use <c>$at</c>)</item>
    ///   <item><c>// …</c>, <c>; …</c>, <c># …</c> and <c>/* … */</c> – comments</item>
    ///   <item><c>scope Name { … }</c> – named scope block</item>
    ///   <item><c>assert mem[$ADDR] == $VAL</c> – sidecar injection-time assertion</item>
    ///   <item><c>breakpoint [$ADDR]</c> – sidecar hardware breakpoint (defaults to the current address)</item>
    ///   <item><c>watchpoint [write|read|rw] $ADDR [SIZE]</c> – sidecar memory watchpoint</item>
    ///   <item><c>log "message"</c> / <c>print "message"</c> – sidecar compile/apply log message</item>
    ///   <item><c>freeze [byte|half|word] $ADDR = VALUE</c> – cheat-engine freeze entry (default word)</item>
    ///   <item><c>cond mem[$ADDR] == VALUE</c> / <c>endcond</c> – cheat-engine conditional block</item>
    ///   <item><c>timer N</c> / <c>endtimer</c> – cheat-engine timer block gated by frame interval</item>
    /// </list>
    ///
    /// <para><b>Scope support:</b> Labels defined inside a <c>scope</c> block are automatically
    /// qualified with the scope path (e.g., label <c>loop</c> inside <c>scope Foo</c> becomes
    /// <c>foo.loop</c>). Within a scope, labels may be referenced by their short name; from
    /// outside, use the fully-qualified form <c>Foo.loop</c>.</para>
    ///
    /// <para>
    /// Diagnostics (errors and warnings) produced during parse are available via
    /// <see cref="Diagnostics"/> after the call returns. A line with an error never aborts the
    /// parse: it emits a diagnostic and (for instructions) a zero word so addresses stay in sync.
    /// </para>
    /// </summary>
    public class CdsParser
    {
        // -----------------------------------------------------------------------
        // Regex patterns
        // -----------------------------------------------------------------------

        private static readonly Regex IncludeRegex =
            new(@"^include\s+""([^""]+)""$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AddressRegex =
            new(@"^address\s+(\S+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex HexCodeRegex =
            new(@"^hexcode\s+(\S+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Raw cheat code pair: "20123456 000003E8" (8 hex digits, whitespace, 8 hex digits)
        private static readonly Regex RawCodeRegex =
            new(@"^([0-9a-f]{8})\s+([0-9a-f]{8})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SetRegRegex =
            new(@"^setreg\s+\$?(\w+)\s*,\s*(\S+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // li $reg, value — 16-bit values assemble to one word, larger values to lui+ori
        private static readonly Regex LiRegex =
            new(@"^li\s+\$?(\w+)\s*,\s*(\S+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // la $reg, :labelname (or la $reg, labelname, or la $reg, $address) — lui+ori
        private static readonly Regex LaRegex =
            new(@"^la\s+\$?(\w+)\s*,\s*:?([a-zA-Z_$0-9][a-zA-Z0-9_.]*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LabelDefRegex =
            new(@"^:?([a-zA-Z_][a-zA-Z0-9_]*):$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex StringRegex =
            new(@"^string\s+""(.*?)""$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Group 1: optional size suffix (b/h/w/d), Group 2: offset, Group 3: register,
        // Group 4: operator, Group 5: operand value.
        private static readonly Regex MemRegex =
            new(@"^mem(?:\.(b|h|w|d))?\[\s*(\S+?)\s*\]\s+\$?(\w+)\s*(\+=|-=|\*=|/=|=)\s*(\S+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LoadRegex =
            new(@"^load(?:\.(b|h|w|d))?\s+\$?(\w+)\s*,\s*mem\[\s*(\S+?)\s*\]\s+\$?(\w+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex StoreRegex =
            new(@"^store(?:\.(b|h|w|d))?\s+\$?(\w+)\s*,\s*mem\[\s*(\S+?)\s*\]\s+\$?(\w+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PointerRegex =
            new(@"^pointer\s+(.+?)\s*=\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ScopeRegex =
            new(@"^scope\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*\{$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // assert mem[$ADDR] == $VAL  |  assert $ADDR == $VAL   (or !=)
        private static readonly Regex AssertRegex =
            new(@"^assert\s+(?:mem\[\s*(\S+?)\s*\]|(\S+))\s*(==|!=)\s*(\S+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // breakpoint [$ADDR]
        private static readonly Regex BreakpointRegex =
            new(@"^breakpoint(?:\s+(\S+))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // watchpoint [write|read|rw|readwrite] [$ADDR [SIZE]]
        private static readonly Regex WatchpointRegex =
            new(@"^watchpoint(?:\s+(write|read|rw|readwrite))?(?:\s+(\S+))?(?:\s+(\d+))?$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // log "message" / print "message"
        private static readonly Regex LogRegex =
            new(@"^(log|print)\s+""(.*?)""$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LogPrefixRegex =
            new(@"^(log|print)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // freeze [byte|half|word] $ADDR = VALUE   (size defaults to word)
        private static readonly Regex FreezeRegex =
            new(@"^freeze(?:\s+(byte|half|word))?\s+(\S+)\s*=\s*(\S+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // cond mem[$ADDR] == VALUE  |  cond $ADDR == VALUE
        private static readonly Regex CondRegex =
            new(@"^cond\s+(?:mem\[\s*(\S+?)\s*\]|(\S+))\s*==\s*(\S+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EndCondRegex =
            new(@"^endcond$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TimerRegex =
            new(@"^timer\s+(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EndTimerRegex =
            new(@"^endtimer$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // hi(:label) / lo(:label) / %hi(label) / %lo(label) operand forms
        private static readonly Regex HiLoRegex =
            new(@"%?\b(hi|lo)\(\s*:?([a-zA-Z_][a-zA-Z0-9_.]*)\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LabelRefCandidateRegex =
            new(@"^[a-zA-Z_][a-zA-Z0-9_.]*$", RegexOptions.Compiled);

        // Branch mnemonics that use a PC-relative 16-bit offset
        private static readonly HashSet<string> BranchMnemonics = new(StringComparer.OrdinalIgnoreCase)
        {
            "beq",  "bne",  "blez",  "bgtz",  "bltz",  "bgez",
            "beql", "bnel", "blezl", "bgtzl", "bltzl", "bgezl",
            "bltzal", "bgezal", "bltzall", "bgezall",
            "bc1t", "bc1f", "bc1tl", "bc1fl",
            "b", "beqz", "bnez"
        };

        // Two-word branch pseudo-instructions: slt/sltu $at + beq/bne $at, $zero
        private static readonly HashSet<string> BranchPseudoMnemonics = new(StringComparer.OrdinalIgnoreCase)
        {
            "blt", "bgt", "ble", "bge", "bltu", "bgtu", "bleu", "bgeu"
        };

        // Jump mnemonics that encode a 26-bit absolute word address
        private static readonly HashSet<string> JumpMnemonics = new(StringComparer.OrdinalIgnoreCase)
        {
            "j", "jal"
        };

        // Instructions whose immediate is sign-extended: a label low half >= 0x8000 needs a
        // compensated high half, so warn when it is paired with a plain `lui :label`.
        private static readonly HashSet<string> SignExtendingImmediateMnemonics = new(StringComparer.OrdinalIgnoreCase)
        {
            "addi", "addiu", "daddi", "daddiu", "slti", "sltiu",
            "lb", "lbu", "lh", "lhu", "lw", "lwu", "ld", "lq", "lwl", "lwr", "ldl", "ldr",
            "sb", "sh", "sw", "sd", "sq", "swl", "swr", "sdl", "sdr", "lwc1", "swc1", "ldc1", "sdc1"
        };

        private static readonly HashSet<string> DelaySlotMnemonics = new(StringComparer.OrdinalIgnoreCase)
        {
            "j", "jal", "jr", "jalr",
            "beq",  "bne",  "blez",  "bgtz",  "bltz",  "bgez",
            "beql", "bnel", "blezl", "bgtzl", "bltzl", "bgezl",
            "bltzal", "bgezal", "bltzall", "bgezall",
            "bc1t", "bc1f", "bc1tl", "bc1fl",
            "b", "beqz", "bnez"
        };

        // -----------------------------------------------------------------------
        // Diagnostics
        // -----------------------------------------------------------------------

        private readonly List<CdsDiagnostic> _diagnostics = new();

        /// <summary>
        /// Diagnostics (errors and warnings) produced during the most recent
        /// <see cref="Parse"/> call.  Cleared at the start of every <see cref="Parse"/> call.
        /// </summary>
        public IReadOnlyList<CdsDiagnostic> Diagnostics => _diagnostics;

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
        /// <param name="externalLabels">
        /// Optional dictionary of pre-resolved label name → virtual address pairs injected
        /// before the two-pass parse.  Useful when the caller has labels from an external
        /// source (e.g., pointer-manager or server-registered labels) that CDS source should
        /// be able to reference.  Labels defined inside the CDS source take precedence on
        /// name conflicts.
        /// </param>
        public List<CdsSyntaxItem> Parse(string sourceCode, string basePath = "",
            IReadOnlyDictionary<string, uint>? externalLabels = null)
        {
            _diagnostics.Clear();

            // Pre-process high-level constructs (const/alias/macro/include/if/while/repeat)
            // before the two-pass parse.
            var preprocessor = new CdsPreprocessor();
            sourceCode = preprocessor.Process(sourceCode ?? string.Empty, basePath);
            _diagnostics.AddRange(preprocessor.Diagnostics);

            string[] rawLines = sourceCode.Replace("\r\n", "\n").Split('\n');

            // First pass: build a map of fully-qualified label name → address.
            // Seed with external labels first so source-defined labels take precedence.
            var labelMap = BuildLabelMap(rawLines, externalLabels);

            // Second pass: produce syntax items
            uint currentAddress = 0;
            bool inMultiLine = false;
            int multiLineStart = 0;
            var multiLineBuf = new StringBuilder();

            var rootItems = new List<CdsSyntaxItem>();
            var itemsStack = new Stack<List<CdsSyntaxItem>>();
            var scopeStack = new Stack<string>();  // scope path components
            // Tracks what opened each frame on itemsStack: "scope", "cond" or "timer"
            var itemsKindStack = new Stack<string>();

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
                // A line that only closes a block comment ("*/") is treated as a comment too, so
                // prose such as "block comments use /* and */" inside a comment does not leak code.
                if (trimmed.StartsWith("//") || trimmed.StartsWith(";") || trimmed.StartsWith("#") ||
                    trimmed.StartsWith("*/"))
                {
                    current.Add(new CommentSyntaxItem
                    {
                        LineNumber = lineNumber,
                        LineText = trimmed,
                        Text = trimmed.TrimStart('/', ';', '#', '*').Trim(),
                        IsMultiLine = false
                    });
                    continue;
                }

                // ---- Strip inline comments (// ; #) from code lines ----
                trimmed = StripInlineComments(trimmed);
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                try
                {
                    ParseLine(trimmed, lineNumber, basePath, labelMap,
                              ref currentAddress, itemsStack, itemsKindStack, scopeStack);
                }
                catch (Exception ex)
                {
                    // A malformed line must never abort the whole compile.
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"could not parse line: {ex.Message}", trimmed, null, "CDS0099");
                }
            }

            if (inMultiLine)
            {
                AddDiagnostic(CdsDiagnosticSeverity.Error, multiLineStart,
                    "unterminated multi-line comment", multiLineBuf.ToString().TrimEnd(), "/*");
            }

            while (scopeStack.Count > 0)
            {
                string scopeName = scopeStack.Pop();
                AddDiagnostic(CdsDiagnosticSeverity.Error, rawLines.Length > 0 ? rawLines.Length : 1,
                    $"unterminated scope '{scopeName}' — missing '}}'", rawLines.LastOrDefault() ?? string.Empty,
                    scopeName, "CDS0004");
            }

            if (itemsKindStack.Any(k => k == "cond"))
            {
                AddDiagnostic(CdsDiagnosticSeverity.Error, rawLines.Length > 0 ? rawLines.Length : 1,
                    "unterminated cond block — missing 'endcond'", rawLines.LastOrDefault() ?? string.Empty,
                    "cond", "CDS0005");
            }
            if (itemsKindStack.Any(k => k == "timer"))
            {
                AddDiagnostic(CdsDiagnosticSeverity.Error, rawLines.Length > 0 ? rawLines.Length : 1,
                    "unterminated timer block — missing 'endtimer'", rawLines.LastOrDefault() ?? string.Empty,
                    "timer", "CDS0011");
            }

            AddDelaySlotDiagnostics(rootItems);

            return rootItems;
        }

        // -----------------------------------------------------------------------
        // Second pass: one line
        // -----------------------------------------------------------------------

        private void ParseLine(
            string trimmed,
            int lineNumber,
            string basePath,
            Dictionary<string, uint> labelMap,
            ref uint currentAddress,
            Stack<List<CdsSyntaxItem>> itemsStack,
            Stack<string> itemsKindStack,
            Stack<string> scopeStack)
        {
            var current = itemsStack.Peek();

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
                itemsKindStack.Push("scope");
                return;
            }

            // ---- Scope close ----
            if (trimmed == "}")
            {
                if (scopeStack.Count > 0 && itemsKindStack.Count > 0 && itemsKindStack.Peek() == "scope")
                {
                    scopeStack.Pop();
                    itemsStack.Pop();
                    itemsKindStack.Pop();
                }
                else if (scopeStack.Count > 0)
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"'}}' closes a scope but a '{itemsKindStack.Peek()}' block is still open", trimmed, "}");
                }
                else
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        "unexpected '}' with no open scope", trimmed, "}");
                }
                return;
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
                return;
            }

            // ---- Address directive ----
            var addrMatch = AddressRegex.Match(trimmed);
            if (addrMatch.Success)
            {
                if (TryParseHex32(addrMatch.Groups[1].Value, out uint addr, out string? err))
                {
                    currentAddress = addr;
                    if ((addr & 0x3) != 0)
                    {
                        AddDiagnostic(CdsDiagnosticSeverity.Warning, lineNumber,
                            $"address ${addr:X8} is not word-aligned; instructions must start on a 4-byte boundary",
                            trimmed, addrMatch.Groups[1].Value, "CDS0015");
                    }
                    current.Add(new AddressSyntaxItem
                    {
                        LineNumber = lineNumber,
                        LineText = trimmed,
                        Address = currentAddress
                    });
                }
                else
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"invalid address: {err}", trimmed, addrMatch.Groups[1].Value, "CDS0014");
                }
                return;
            }

            // ---- Raw cheat code pair (passed through to the code list) ----
            var rawMatch = RawCodeRegex.Match(trimmed);
            if (rawMatch.Success)
            {
                current.Add(new RawCodeSyntaxItem
                {
                    LineNumber = lineNumber,
                    LineText   = trimmed,
                    CodeWord   = Convert.ToUInt32(rawMatch.Groups[1].Value, 16),
                    ValueWord  = Convert.ToUInt32(rawMatch.Groups[2].Value, 16),
                });
                // Not a word at the current address — does not advance the address counter
                return;
            }

            // ---- HexCode directive ----
            var hexMatch = HexCodeRegex.Match(trimmed);
            if (hexMatch.Success)
            {
                if (TryParseHex32(hexMatch.Groups[1].Value, out uint value, out string? err))
                {
                    current.Add(new HexCodeSyntaxItem
                    {
                        LineNumber = lineNumber,
                        LineText = trimmed,
                        Address = currentAddress,
                        Value = value
                    });
                }
                else
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"invalid hexcode value: {err}", trimmed, hexMatch.Groups[1].Value, "CDS0014");
                }
                currentAddress += 4;
                return;
            }

            // ---- SetReg directive ----
            var setRegMatch = SetRegRegex.Match(trimmed);
            if (setRegMatch.Success)
            {
                string rawReg = setRegMatch.Groups[1].Value;
                string? reg = NormalizeDirectiveGpr(rawReg, lineNumber, trimmed);
                if (reg is not null)
                {
                    if (TryParseValue32(setRegMatch.Groups[2].Value, out uint value, out string? err))
                    {
                        current.Add(BuildSetReg(lineNumber, trimmed, reg, value, currentAddress));
                    }
                    else
                    {
                        AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                            $"invalid setreg value: {err}", trimmed, setRegMatch.Groups[2].Value, "CDS0014");
                    }
                }
                currentAddress += 8;
                return;
            }

            // ---- li directive (1 or 2 words depending on the value) ----
            var liMatch = LiRegex.Match(trimmed);
            if (liMatch.Success)
            {
                string? reg = NormalizeDirectiveGpr(liMatch.Groups[1].Value, lineNumber, trimmed);
                string valueText = liMatch.Groups[2].Value;
                if (!TryParseSignedValue32(valueText, out long value, out string? err))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"invalid li value: {err}", trimmed, valueText, "CDS0014");
                    currentAddress += 4;
                    return;
                }

                uint words = LiWordCount(value);
                if (reg is not null)
                {
                    if (words == 1)
                    {
                        string asm = value < 0 || value <= short.MaxValue
                            ? $"addiu ${reg}, $zero, {value}"
                            : $"ori ${reg}, $zero, ${value:X4}";
                        current.Add(new OperationSyntaxItem
                        {
                            LineNumber = lineNumber, LineText = trimmed,
                            Address = currentAddress, EncodedWord = TryAssemble(asm),
                            Mnemonic = asm.Split(' ')[0]
                        });
                    }
                    else
                    {
                        current.Add(BuildSetReg(lineNumber, trimmed, reg, (uint)value, currentAddress));
                    }
                }
                currentAddress += words * 4;
                return;
            }

            // ---- Load-address directive (la $reg, :label | la $reg, $address) ----
            var laMatch = LaRegex.Match(trimmed);
            if (laMatch.Success)
            {
                string rawReg = laMatch.Groups[1].Value;
                string? reg = NormalizeDirectiveGpr(rawReg, lineNumber, trimmed);
                string labelRef = laMatch.Groups[2].Value;
                if (reg is not null)
                {
                    if (IsNumericToken(labelRef))
                    {
                        if (TryParseHex32(labelRef, out uint addr, out string? err))
                            current.Add(BuildSetReg(lineNumber, trimmed, reg, addr, currentAddress));
                        else
                            AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                                $"invalid la address: {err}", trimmed, labelRef, "CDS0014");
                    }
                    else
                    {
                        labelRef = labelRef.ToLowerInvariant();
                        string? qualified = ResolveLabel(labelRef, scopeStack, labelMap);
                        if (qualified == null)
                        {
                            AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                                $"unresolved label reference ':{labelRef}' in '{trimmed}'", trimmed,
                                $":{labelRef}", "CDS0012");
                            // Emit a placeholder that decodes to (lui+ori) of address 0 so downstream
                            // address tracking stays in sync.
                            current.Add(BuildLa(lineNumber, trimmed, reg, labelRef, 0u, currentAddress));
                        }
                        else
                        {
                            uint addr = labelMap[qualified];
                            current.Add(BuildLa(lineNumber, trimmed, reg, qualified, addr, currentAddress));
                        }
                    }
                }
                currentAddress += 8;
                return;
            }

            // ---- Memory operation ----
            var memMatch = MemRegex.Match(trimmed);
            if (memMatch.Success)
            {
                string sizeSuffix = memMatch.Groups[1].Value;
                string offsetText = memMatch.Groups[2].Value;
                string rawReg = memMatch.Groups[3].Value;
                string opStr = memMatch.Groups[4].Value;
                string operandStr = memMatch.Groups[5].Value;

                if (!TryParseOffset16(offsetText, out uint offset, out string? offErr))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"invalid mem[] offset: {offErr}", trimmed, offsetText, "CDS0014");
                    return;
                }
                if (!TryParseSignedValue32(operandStr, out long operand, out string? opErr))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"invalid mem[] operand: {opErr}", trimmed, operandStr, "CDS0014");
                    return;
                }

                var memOp = ParseMemOp(opStr);
                var memSz = ParseMemSize(sizeSuffix);
                uint instrCount = MemOpWordCount(memOp, operand);

                string? reg = NormalizeDirectiveGpr(rawReg, lineNumber, trimmed);
                if (reg is not null)
                {
                    var item = BuildMemOp(lineNumber, trimmed, offset, reg, memOp, operand, currentAddress, memSz);
                    current.Add(item);
                    AddDiagnostic(CdsDiagnosticSeverity.Warning, lineNumber,
                        MemOpUsesAt(memOp, operand)
                            ? "registers $t9 and $at are clobbered by mem[] expansion"
                            : "register $t9 is clobbered by mem[] expansion",
                        trimmed, "mem", "CDS0003");
                }
                currentAddress += instrCount * 4;
                return;
            }

            // ---- Typed memory load ----
            var loadMatch = LoadRegex.Match(trimmed);
            if (loadMatch.Success)
            {
                string sizeSuffix = loadMatch.Groups[1].Value;
                string? targetReg = NormalizeDirectiveGpr(loadMatch.Groups[2].Value, lineNumber, trimmed);
                string? baseReg = NormalizeDirectiveGpr(loadMatch.Groups[4].Value, lineNumber, trimmed);
                if (!TryParseOffset16(loadMatch.Groups[3].Value, out uint offset, out string? err))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"invalid load offset: {err}", trimmed, loadMatch.Groups[3].Value, "CDS0014");
                }
                else if (targetReg is not null && baseReg is not null)
                {
                    var memSz = ParseMemSize(sizeSuffix);
                    current.Add(BuildLoadOp(lineNumber, trimmed, targetReg, offset, baseReg, currentAddress, memSz));
                }
                currentAddress += 4;
                return;
            }

            // ---- Typed memory store ----
            var storeMatch = StoreRegex.Match(trimmed);
            if (storeMatch.Success)
            {
                string sizeSuffix = storeMatch.Groups[1].Value;
                string? sourceReg = NormalizeDirectiveGpr(storeMatch.Groups[2].Value, lineNumber, trimmed);
                string? baseReg = NormalizeDirectiveGpr(storeMatch.Groups[4].Value, lineNumber, trimmed);
                if (!TryParseOffset16(storeMatch.Groups[3].Value, out uint offset, out string? err))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"invalid store offset: {err}", trimmed, storeMatch.Groups[3].Value, "CDS0014");
                }
                else if (sourceReg is not null && baseReg is not null)
                {
                    var memSz = ParseMemSize(sizeSuffix);
                    current.Add(BuildStoreOp(lineNumber, trimmed, sourceReg, offset, baseReg, currentAddress, memSz));
                }
                currentAddress += 4;
                return;
            }

            // ---- Pointer-chain directive ----
            if (TryParsePointerDirective(trimmed, out uint pointerBase, out var pointerOffsets, out uint pointerValue))
            {
                var item = BuildPointer(lineNumber, trimmed, pointerBase, pointerOffsets, pointerValue, currentAddress);
                current.Add(item);
                AddDiagnostic(CdsDiagnosticSeverity.Warning, lineNumber,
                    "pointer directive clobbers scratch registers $t8 and $t9",
                    trimmed, "pointer", "CDS0009");
                currentAddress += (uint)item.Operations.Count * 4;
                return;
            }
            if (trimmed.StartsWith("pointer", StringComparison.OrdinalIgnoreCase))
            {
                AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                    "malformed pointer directive; expected pointer $BASE + $OFFSET [+ $OFFSET ...] = $VALUE",
                    trimmed, "pointer", "CDS0010");
                return;
            }

            // ---- String directive ----
            var strMatch = StringRegex.Match(trimmed);
            if (strMatch.Success)
            {
                string text = strMatch.Groups[1].Value;
                var item = BuildString(lineNumber, trimmed, text, currentAddress);
                current.Add(item);
                currentAddress += (uint)item.Words.Count * 4;
                return;
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

                if (labelMap.TryGetValue(fullName, out uint mapped) && mapped != currentAddress)
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"label '{fullName}' is defined more than once (references resolve to the last definition at ${mapped:X8})",
                        trimmed, localName, "CDS0021");
                }

                current.Add(new LabelSyntaxItem
                {
                    LineNumber = lineNumber,
                    LineText = trimmed,
                    Address = currentAddress,
                    Name = localName,
                    FullName = fullName
                });
                return;
            }

            // ---- Assert sidecar ----
            var assertMatch = AssertRegex.Match(trimmed);
            if (assertMatch.Success)
            {
                string addrText = assertMatch.Groups[1].Success ? assertMatch.Groups[1].Value : assertMatch.Groups[2].Value;
                string valText = assertMatch.Groups[4].Value;
                if (!TryParseHex32(addrText, out uint assertAddr, out string? err1))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"invalid assert address: {err1}", trimmed, addrText, "CDS0014");
                    return;
                }
                if (!TryParseHex32(valText, out uint assertVal, out string? err2))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"invalid assert value: {err2}", trimmed, valText, "CDS0014");
                    return;
                }
                current.Add(new AssertSyntaxItem
                {
                    LineNumber    = lineNumber,
                    LineText      = trimmed,
                    Address       = assertAddr,
                    Operator      = assertMatch.Groups[3].Value,
                    ExpectedValue = assertVal
                });
                // Sidecar — does not advance the address counter
                return;
            }

            // ---- Breakpoint sidecar ----
            var bpMatch = BreakpointRegex.Match(trimmed);
            if (bpMatch.Success)
            {
                uint bpAddr = currentAddress;
                if (bpMatch.Groups[1].Success &&
                    !TryParseHex32(bpMatch.Groups[1].Value, out bpAddr, out string? err))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"invalid breakpoint address: {err}", trimmed, bpMatch.Groups[1].Value, "CDS0014");
                    return;
                }
                current.Add(new BreakpointSyntaxItem
                {
                    LineNumber = lineNumber,
                    LineText   = trimmed,
                    Address    = bpAddr
                });
                // Sidecar — does not advance the address counter
                return;
            }

            // ---- Watchpoint sidecar ----
            var watchMatch = WatchpointRegex.Match(trimmed);
            if (watchMatch.Success)
            {
                string accessStr = watchMatch.Groups[1].Success ? watchMatch.Groups[1].Value : "write";
                uint watchAddr = currentAddress;
                if (watchMatch.Groups[2].Success &&
                    !TryParseHex32(watchMatch.Groups[2].Value, out watchAddr, out string? err))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"invalid watchpoint address: {err}", trimmed, watchMatch.Groups[2].Value, "CDS0014");
                    return;
                }
                uint watchSize = 4;
                if (watchMatch.Groups[3].Success &&
                    (!uint.TryParse(watchMatch.Groups[3].Value, out watchSize) || watchSize == 0))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"invalid watchpoint size '{watchMatch.Groups[3].Value}' (expected 1, 2, 4 or 8)",
                        trimmed, watchMatch.Groups[3].Value, "CDS0014");
                    return;
                }
                var accessType = accessStr.ToLowerInvariant() switch
                {
                    "read"      => WatchpointAccessType.Read,
                    "rw"        => WatchpointAccessType.ReadWrite,
                    "readwrite" => WatchpointAccessType.ReadWrite,
                    _           => WatchpointAccessType.Write
                };
                current.Add(new WatchpointSyntaxItem
                {
                    LineNumber = lineNumber,
                    LineText   = trimmed,
                    Address    = watchAddr,
                    Size       = watchSize,
                    AccessType = accessType
                });
                // Sidecar — does not advance the address counter
                return;
            }

            // ---- Log/print sidecar ----
            var logMatch = LogRegex.Match(trimmed);
            if (logMatch.Success)
            {
                current.Add(new LogSyntaxItem
                {
                    LineNumber  = lineNumber,
                    LineText    = trimmed,
                    Address     = currentAddress,
                    Directive   = logMatch.Groups[1].Value.ToLowerInvariant(),
                    MessageText = logMatch.Groups[2].Value
                });
                // Sidecar — does not advance the address counter
                return;
            }
            if (LogPrefixRegex.IsMatch(trimmed))
            {
                AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                    "malformed log/print directive; expected log \"message\" or print \"message\"",
                    trimmed,
                    trimmed.StartsWith("print", StringComparison.OrdinalIgnoreCase) ? "print" : "log",
                    "CDS0008");
                return;
            }

            // ---- Freeze sidecar ----
            var freezeMatch = FreezeRegex.Match(trimmed);
            if (freezeMatch.Success)
            {
                string sizeStr = freezeMatch.Groups[1].Success ? freezeMatch.Groups[1].Value.ToLowerInvariant() : "word";
                if (!TryParseHex32(freezeMatch.Groups[2].Value, out uint freezeAddr, out string? err1))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"invalid freeze address: {err1}", trimmed, freezeMatch.Groups[2].Value, "CDS0014");
                    return;
                }
                if (!TryParseHex32(freezeMatch.Groups[3].Value, out uint freezeVal, out string? err2))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"invalid freeze value: {err2}", trimmed, freezeMatch.Groups[3].Value, "CDS0014");
                    return;
                }
                var freezeType = sizeStr switch
                {
                    "byte" => CdsFreezeType.Byte,
                    "half" => CdsFreezeType.Half,
                    _      => CdsFreezeType.Word
                };
                uint maxValue = freezeType switch
                {
                    CdsFreezeType.Byte => 0xFF,
                    CdsFreezeType.Half => 0xFFFF,
                    _ => uint.MaxValue
                };
                if (freezeVal > maxValue)
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Warning, lineNumber,
                        $"freeze {sizeStr} value ${freezeVal:X} exceeds the {sizeStr} range and will be truncated",
                        trimmed, freezeMatch.Groups[3].Value, "CDS0016");
                }
                current.Add(new FreezeSyntaxItem
                {
                    LineNumber = lineNumber,
                    LineText   = trimmed,
                    FreezeType = freezeType,
                    Address    = freezeAddr,
                    Value      = freezeVal
                });
                // Sidecar — does not advance the address counter
                return;
            }

            // ---- Cond block sidecar ----
            var condMatch = CondRegex.Match(trimmed);
            if (condMatch.Success)
            {
                string addrText = condMatch.Groups[1].Success ? condMatch.Groups[1].Value : condMatch.Groups[2].Value;
                if (!TryParseHex32(addrText, out uint condAddr, out string? err1))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"invalid cond address: {err1}", trimmed, addrText, "CDS0014");
                    return;
                }
                if (!TryParseHex32(condMatch.Groups[3].Value, out uint condVal, out string? err2))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"invalid cond value: {err2}", trimmed, condMatch.Groups[3].Value, "CDS0014");
                    return;
                }
                var condItem = new CondSyntaxItem
                {
                    LineNumber     = lineNumber,
                    LineText       = trimmed,
                    Address        = condAddr,
                    ConditionValue = condVal
                };
                current.Add(condItem);
                itemsStack.Push(condItem.Body);
                itemsKindStack.Push("cond");
                // Sidecar — does not advance the address counter
                return;
            }
            if (trimmed.StartsWith("cond ", StringComparison.OrdinalIgnoreCase))
            {
                AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                    "malformed cond directive; expected cond mem[$ADDR] == VALUE (only == is supported)",
                    trimmed, "cond", "CDS0017");
                return;
            }

            // ---- Timer block sidecar ----
            var timerMatch = TimerRegex.Match(trimmed);
            if (timerMatch.Success)
            {
                if (!uint.TryParse(timerMatch.Groups[1].Value, out uint intervalFrames))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"timer interval '{timerMatch.Groups[1].Value}' is out of range", trimmed, "timer", "CDS0014");
                    return;
                }
                if (intervalFrames == 0)
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Warning, lineNumber,
                        "timer interval 0 will never execute its body", trimmed, "timer", "CDS0012");
                }
                var timerItem = new TimerSyntaxItem
                {
                    LineNumber = lineNumber,
                    LineText = trimmed,
                    IntervalFrames = intervalFrames
                };
                current.Add(timerItem);
                itemsStack.Push(timerItem.Body);
                itemsKindStack.Push("timer");
                return;
            }

            // ---- Endcond ----
            if (EndCondRegex.IsMatch(trimmed))
            {
                if (itemsKindStack.Count > 0 && itemsKindStack.Peek() == "cond")
                {
                    itemsStack.Pop();
                    itemsKindStack.Pop();
                }
                else
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        "unexpected 'endcond' with no open cond block", trimmed, "endcond");
                }
                return;
            }

            // ---- Endtimer ----
            if (EndTimerRegex.IsMatch(trimmed))
            {
                if (itemsKindStack.Count > 0 && itemsKindStack.Peek() == "timer")
                {
                    itemsStack.Pop();
                    itemsKindStack.Pop();
                }
                else
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        "unexpected 'endtimer' with no open timer block", trimmed, "endtimer");
                }
                return;
            }

            TryParseOperation(trimmed, lineNumber, scopeStack, labelMap, ref currentAddress, current);
        }

        // -----------------------------------------------------------------------
        // First-pass label map builder
        // -----------------------------------------------------------------------

        /// <summary>
        /// Scans the source for label definitions (including those inside scope blocks)
        /// and returns a map of fully-qualified label name → address.
        /// All keys are lower-case.
        /// </summary>
        private static Dictionary<string, uint> BuildLabelMap(
            string[] rawLines,
            IReadOnlyDictionary<string, uint>? externalLabels = null)
        {
            // Seed with external labels first — source-defined labels will overwrite on collision.
            var map = externalLabels != null
                ? new Dictionary<string, uint>(externalLabels, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
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
                if (t.StartsWith("//") || t.StartsWith(";") || t.StartsWith("#") || t.StartsWith("*/")) continue;

                // Strip inline comments (// ; #) from code lines
                t = StripInlineComments(t);
                if (string.IsNullOrWhiteSpace(t)) continue;

                // Scope open / close
                var scopeMatch = ScopeRegex.Match(t);
                if (scopeMatch.Success) { scopeStack.Push(scopeMatch.Groups[1].Value.ToLowerInvariant()); continue; }
                if (t == "}") { if (scopeStack.Count > 0) scopeStack.Pop(); continue; }

                var addrMatch = AddressRegex.Match(t);
                if (addrMatch.Success)
                {
                    if (TryParseHex32(addrMatch.Groups[1].Value, out uint addr, out _))
                        currentAddress = addr;
                    continue;
                }

                // Label definition
                var labelMatch = LabelDefRegex.Match(t);
                if (labelMatch.Success)
                {
                    string localName = labelMatch.Groups[1].Value.ToLowerInvariant();
                    string prefix = ScopePathString(scopeStack);
                    string fullName = string.IsNullOrEmpty(prefix) ? localName : $"{prefix}.{localName}";
                    map[fullName] = currentAddress;
                    continue;
                }

                currentAddress += MeasureLine(t);
            }

            return map;
        }

        /// <summary>
        /// Number of bytes a (comment-stripped, non-label, non-address) line occupies.
        /// Mirrors the address increments of the second pass so label addresses line up.
        /// </summary>
        private static uint MeasureLine(string t)
        {
            if (IncludeRegex.IsMatch(t)) return 0;
            if (RawCodeRegex.IsMatch(t)) return 0;
            if (HexCodeRegex.IsMatch(t)) return 4;
            if (SetRegRegex.IsMatch(t)) return 8;

            var liMatch = LiRegex.Match(t);
            if (liMatch.Success)
            {
                return TryParseSignedValue32(liMatch.Groups[2].Value, out long value, out _)
                    ? LiWordCount(value) * 4
                    : 4;
            }

            if (LaRegex.IsMatch(t)) return 8;

            var memMatch = MemRegex.Match(t);
            if (memMatch.Success)
            {
                if (!TryParseOffset16(memMatch.Groups[2].Value, out _, out _) ||
                    !TryParseSignedValue32(memMatch.Groups[5].Value, out long operand, out _))
                    return 0;
                return MemOpWordCount(ParseMemOp(memMatch.Groups[4].Value), operand) * 4;
            }

            var loadMatch = LoadRegex.Match(t);
            if (loadMatch.Success)
                return TryParseOffset16(loadMatch.Groups[3].Value, out _, out _) ? 4u : 0u;

            var storeMatch = StoreRegex.Match(t);
            if (storeMatch.Success)
                return TryParseOffset16(storeMatch.Groups[3].Value, out _, out _) ? 4u : 0u;

            if (TryParsePointerDirective(t, out _, out var pointerOffsets, out _))
                return (uint)(pointerOffsets.Count + 5) * 4;
            if (t.StartsWith("pointer", StringComparison.OrdinalIgnoreCase))
                return 0;

            var strMatch = StringRegex.Match(t);
            if (strMatch.Success)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(strMatch.Groups[1].Value + "\0");
                return (uint)(((bytes.Length + 3) / 4) * 4);
            }

            // Sidecar directives — consume no address space
            if (AssertRegex.IsMatch(t))     return 0;
            if (BreakpointRegex.IsMatch(t)) return 0;
            if (WatchpointRegex.IsMatch(t)) return 0;
            if (LogPrefixRegex.IsMatch(t))  return 0;
            if (FreezeRegex.IsMatch(t))     return 0;
            if (CondRegex.IsMatch(t))       return 0;
            if (t.StartsWith("cond ", StringComparison.OrdinalIgnoreCase)) return 0;
            if (EndCondRegex.IsMatch(t))    return 0;
            if (TimerRegex.IsMatch(t))      return 0;
            if (EndTimerRegex.IsMatch(t))   return 0;

            // Two-word branch pseudo-instructions
            string mnemonic = FirstToken(t);
            if (BranchPseudoMnemonics.Contains(mnemonic)) return 8;

            // Everything else that emits an instruction takes 4 bytes
            return 4;
        }

        private static string FirstToken(string line)
        {
            int end = 0;
            while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] != ',') end++;
            return line[..end].ToLowerInvariant();
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
            // Resolve explicit hi(:label) / lo(:label) operands first.
            string resolvedLine = ResolveHiLoOperands(line, lineNumber, scopeStack, labelMap);

            // Extract optional :label suffix
            string labelRef = string.Empty;
            string coreLine = resolvedLine;
            bool labelWasCommaOperand = false;

            int colonIdx = resolvedLine.LastIndexOf(':');
            if (colonIdx > 0)
            {
                string candidate = resolvedLine.Substring(colonIdx + 1).Trim();
                if (LabelRefCandidateRegex.IsMatch(candidate))
                {
                    labelRef = candidate.ToLowerInvariant();
                    coreLine = resolvedLine.Substring(0, colonIdx).TrimEnd();
                    labelWasCommaOperand = coreLine.EndsWith(",", StringComparison.Ordinal);
                    if (labelWasCommaOperand)
                        coreLine = coreLine.TrimEnd(',').TrimEnd();
                }
            }

            string[] parts = coreLine.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            string mnemonic = parts[0].ToLowerInvariant();

            if (BranchPseudoMnemonics.Contains(mnemonic))
            {
                ParseBranchPseudo(line, lineNumber, mnemonic, parts, labelRef, scopeStack, labelMap,
                                  ref currentAddress, items);
                return;
            }

            string resolvedLabel = string.Empty;
            string assemblyLine = coreLine;

            if (!string.IsNullOrEmpty(labelRef))
            {
                // Resolve the label through the scope hierarchy
                string? qualified = ResolveLabel(labelRef, scopeStack, labelMap);
                if (qualified == null)
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Warning, lineNumber,
                        $"unresolved label reference ':{labelRef}' in '{line}'", line, $":{labelRef}", "CDS0001");
                    // Keep coreLine as-is; encoding will fail naturally
                    assemblyLine = coreLine;
                }
                else
                {
                    resolvedLabel = qualified;
                    uint labelAddress = labelMap[qualified];

                    if (BranchMnemonics.Contains(mnemonic))
                    {
                        if (TryComputeBranchOffset(labelAddress, currentAddress, lineNumber, line, labelRef, out ushort offset))
                        {
                            assemblyLine = AppendResolvedLabelOperand(coreLine, $"${offset:X4}", labelWasCommaOperand);
                        }
                    }
                    else if (JumpMnemonics.Contains(mnemonic))
                    {
                        // Jump: MipsAssembler.AssembleJ expects a byte address (word-aligned);
                        // it performs the >> 2 shift internally.
                        // j/jal keep the upper 4 bits of PC+4. On the PS2 the user segment
                        // (0x0xxxxxxx), KSEG0 (0x8xxxxxxx) and KSEG1 (0xAxxxxxxx) alias the
                        // same physical RAM, so only flag targets that land somewhere else.
                        uint effective = ((currentAddress + 4) & 0xF0000000u) | (labelAddress & 0x0FFFFFFFu);
                        if ((effective & 0x1FFFFFFFu) != (labelAddress & 0x1FFFFFFFu))
                        {
                            AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                                $"jump target ':{labelRef}' (${labelAddress:X8}) is outside the 256 MB region reachable from ${currentAddress:X8} (the jump would land at ${effective:X8})",
                                line, $":{labelRef}", "CDS0019");
                        }
                        assemblyLine = $"{mnemonic} ${labelAddress:X8}";
                    }
                    else if (mnemonic == "lui")
                    {
                        // lui reg, :label → upper 16 bits of the label address
                        assemblyLine = AppendResolvedLabelOperand(coreLine, $"${labelAddress >> 16:X4}", labelWasCommaOperand);
                    }
                    else
                    {
                        // Any other immediate instruction → lower 16 bits of the label address
                        ushort lo = (ushort)(labelAddress & 0xFFFF);
                        if (lo >= 0x8000 && SignExtendingImmediateMnemonics.Contains(mnemonic))
                        {
                            AddDiagnostic(CdsDiagnosticSeverity.Warning, lineNumber,
                                $"low half of ':{labelRef}' (${lo:X4}) is sign-extended by {mnemonic}; pair it with lui $(hi+1) or use ori / la instead",
                                line, $":{labelRef}", "CDS0018");
                        }
                        assemblyLine = AppendResolvedLabelOperand(coreLine, $"${lo:X4}", labelWasCommaOperand);
                    }
                }
            }

            uint encoded;
            if (!MipsAssembler.TryAssemble(assemblyLine, out encoded, out string? error))
            {
                AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                    $"assembly failed for '{assemblyLine}': {error}", line, mnemonic, "CDS0002");
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

        /// <summary>
        /// Expand <c>blt/bgt/ble/bge[u] rs, rt, :label</c> into
        /// <c>slt[u] $at, a, b</c> + <c>beq/bne $at, $zero, offset</c>.
        /// </summary>
        private void ParseBranchPseudo(
            string line, int lineNumber, string mnemonic, string[] parts, string labelRef,
            Stack<string> scopeStack, Dictionary<string, uint> labelMap,
            ref uint currentAddress, List<CdsSyntaxItem> items)
        {
            // parts: mnemonic, rs, rt [, offset when no label was used]
            string? offsetText = null;
            if (parts.Length == 4) offsetText = parts[3];
            else if (parts.Length != 3 || string.IsNullOrEmpty(labelRef))
            {
                AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                    $"expected '{mnemonic} rs, rt, :label'", line, mnemonic, "CDS0002");
                EmitZeroWords(items, lineNumber, line, mnemonic, ref currentAddress, 2);
                return;
            }

            string? rs = NormalizeDirectiveGpr(parts[1], lineNumber, line);
            string? rt = NormalizeDirectiveGpr(parts[2], lineNumber, line);
            if (rs is null || rt is null)
            {
                EmitZeroWords(items, lineNumber, line, mnemonic, ref currentAddress, 2);
                return;
            }

            bool unsigned = mnemonic.EndsWith("u", StringComparison.Ordinal);
            string baseOp = unsigned ? mnemonic[..^1] : mnemonic;
            string sltMnem = unsigned ? "sltu" : "slt";
            // blt a,b: slt at,a,b ; bne     bge a,b: slt at,a,b ; beq
            // bgt a,b: slt at,b,a ; bne     ble a,b: slt at,b,a ; beq
            bool swap = baseOp is "bgt" or "ble";
            string branchMnem = baseOp is "blt" or "bgt" ? "bne" : "beq";
            string a = swap ? rt : rs;
            string b = swap ? rs : rt;

            string sltAsm = $"{sltMnem} $at, ${a}, ${b}";
            uint sltWord = TryAssemble(sltAsm);
            uint branchAddress = currentAddress + 4;

            string? resolvedLabel = null;
            string branchOperand;
            if (!string.IsNullOrEmpty(labelRef))
            {
                string? qualified = ResolveLabel(labelRef, scopeStack, labelMap);
                if (qualified == null)
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"unresolved label reference ':{labelRef}' in '{line}'", line, $":{labelRef}", "CDS0001");
                    branchOperand = "$0000";
                }
                else
                {
                    resolvedLabel = qualified;
                    TryComputeBranchOffset(labelMap[qualified], branchAddress, lineNumber, line, labelRef, out ushort offset);
                    branchOperand = $"${offset:X4}";
                }
            }
            else
            {
                branchOperand = offsetText!;
            }

            string branchAsm = $"{branchMnem} $at, $zero, {branchOperand}";
            if (!MipsAssembler.TryAssemble(branchAsm, out uint branchWord, out string? error))
            {
                AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                    $"assembly failed for '{branchAsm}': {error}", line, mnemonic, "CDS0002");
                branchWord = 0;
            }

            AddDiagnostic(CdsDiagnosticSeverity.Warning, lineNumber,
                $"register $at is clobbered by {mnemonic} expansion", line, mnemonic, "CDS0020");

            items.Add(new OperationSyntaxItem
            {
                LineNumber = lineNumber, LineText = line,
                Address = currentAddress, EncodedWord = sltWord, Mnemonic = sltMnem
            });
            items.Add(new OperationSyntaxItem
            {
                LineNumber = lineNumber, LineText = line,
                Address = branchAddress, EncodedWord = branchWord, Mnemonic = branchMnem,
                ResolvedLabel = resolvedLabel
            });
            currentAddress += 8;
        }

        private static void EmitZeroWords(List<CdsSyntaxItem> items, int lineNumber, string line, string mnemonic,
            ref uint currentAddress, int count)
        {
            for (int i = 0; i < count; i++)
            {
                items.Add(new OperationSyntaxItem
                {
                    LineNumber = lineNumber, LineText = line,
                    Address = currentAddress, EncodedWord = 0, Mnemonic = mnemonic
                });
                currentAddress += 4;
            }
        }

        /// <summary>
        /// Compute the signed 16-bit word offset of a branch at <paramref name="branchAddress"/>
        /// to <paramref name="targetAddress"/>, diagnosing misaligned or out-of-range targets.
        /// </summary>
        private bool TryComputeBranchOffset(uint targetAddress, uint branchAddress, int lineNumber, string line,
            string labelRef, out ushort offset)
        {
            long delta = (long)targetAddress - (long)(branchAddress + 4);
            if ((delta & 0x3) != 0)
            {
                AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                    $"branch target ':{labelRef}' (${targetAddress:X8}) is not word-aligned", line, $":{labelRef}", "CDS0019");
                offset = 0;
                return false;
            }
            long words = delta / 4;
            if (words < short.MinValue || words > short.MaxValue)
            {
                AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                    $"branch target ':{labelRef}' (${targetAddress:X8}) is out of range for a 16-bit branch offset (±128 KB); use j/jal instead",
                    line, $":{labelRef}", "CDS0019");
                offset = 0;
                return false;
            }
            offset = (ushort)(short)words;
            return true;
        }

        /// <summary>Replace <c>hi(:label)</c> / <c>lo(:label)</c> operands with their 16-bit halves.</summary>
        private string ResolveHiLoOperands(string line, int lineNumber, Stack<string> scopeStack, Dictionary<string, uint> labelMap)
        {
            if (!line.Contains('(')) return line;
            return HiLoRegex.Replace(line, m =>
            {
                string which = m.Groups[1].Value.ToLowerInvariant();
                string labelRef = m.Groups[2].Value.ToLowerInvariant();
                string? qualified = ResolveLabel(labelRef, scopeStack, labelMap);
                if (qualified == null)
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"unresolved label reference ':{labelRef}' in '{line}'", line, m.Value, "CDS0001");
                    return "$0000";
                }
                uint addr = labelMap[qualified];
                return which == "hi" ? $"${addr >> 16:X4}" : $"${addr & 0xFFFF:X4}";
            });
        }

        private static string AppendResolvedLabelOperand(string coreLine, string operand, bool labelWasCommaOperand)
            => labelWasCommaOperand ? $"{coreLine}, {operand}" : $"{coreLine} {operand}";

        private string? NormalizeDirectiveGpr(string token, int lineNumber, string lineText)
        {
            string? normalized = CdsRegisters.NormalizeGpr(token);
            if (normalized is not null) return normalized.TrimStart('$');

            AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                $"unrecognized GPR register '{token}'", lineText, token, "CDS0013");
            return null;
        }

        // -----------------------------------------------------------------------
        // Directive builders
        // -----------------------------------------------------------------------

        private static SetRegSyntaxItem BuildSetReg(
            int lineNumber, string lineText, string reg, uint value, uint address)
        {
            ushort hi = (ushort)(value >> 16);
            ushort lo = (ushort)(value & 0xFFFF);

            string luiAsm = $"lui {reg}, ${ hi:X4}";
            string oriAsm = $"ori {reg}, {reg}, ${lo:X4}";

            uint luiEncoded = TryAssemble(luiAsm);
            uint oriEncoded = TryAssemble(oriAsm);

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
                OriOp = new OperationSyntaxItem
                {
                    LineNumber = lineNumber, LineText = lineText,
                    Address = address + 4, EncodedWord = oriEncoded, Mnemonic = "ori"
                }
            };
        }

        // Build the lui+ori pair for the `la reg, :label` directive. ori is used (instead of
        // addiu) so the low-16 immediate is interpreted as unsigned — addresses with bit 15
        // of the lower half set encode correctly without a hi-half compensation.
        private static LaSyntaxItem BuildLa(
            int lineNumber, string lineText, string reg, string resolvedLabel,
            uint value, uint address)
        {
            ushort hi = (ushort)(value >> 16);
            ushort lo = (ushort)(value & 0xFFFF);

            string luiAsm = $"lui {reg}, ${ hi:X4}";
            string oriAsm = $"ori {reg}, {reg}, ${lo:X4}";

            uint luiEncoded = TryAssemble(luiAsm);
            uint oriEncoded = TryAssemble(oriAsm);

            return new LaSyntaxItem
            {
                LineNumber = lineNumber,
                LineText = lineText,
                Address = address,
                Register = reg,
                ResolvedLabel = resolvedLabel,
                Value = value,
                LuiOp = new OperationSyntaxItem
                {
                    LineNumber = lineNumber, LineText = lineText,
                    Address = address, EncodedWord = luiEncoded, Mnemonic = "lui"
                },
                OriOp = new OperationSyntaxItem
                {
                    LineNumber = lineNumber, LineText = lineText,
                    Address = address + 4, EncodedWord = oriEncoded, Mnemonic = "ori"
                }
            };
        }

        /// <summary>Number of words `li` needs for <paramref name="value"/> (1 or 2).</summary>
        private static uint LiWordCount(long value) =>
            value >= short.MinValue && value <= ushort.MaxValue ? 1u : 2u;

        /// <summary>True when the immediate fits a sign-extended 16-bit field.</summary>
        private static bool FitsSigned16(long value) => value >= short.MinValue && value <= short.MaxValue;

        /// <summary>Number of words needed to materialise <paramref name="value"/> in a register.</summary>
        private static uint LoadImmediateWordCount(long value) =>
            FitsSigned16(value) || (value >= 0 && value <= ushort.MaxValue) ? 1u : 2u;

        /// <summary>True when the mem[] expansion needs <c>$at</c> as a second scratch register.</summary>
        private static bool MemOpUsesAt(CdsMemoryOperation op, long operand) => op switch
        {
            CdsMemoryOperation.Assign   => false,
            CdsMemoryOperation.Add      => !FitsSigned16(operand),
            CdsMemoryOperation.Subtract => !FitsSigned16(-operand),
            _                           => true
        };

        /// <summary>Total number of instruction words emitted for a mem[] operation.</summary>
        private static uint MemOpWordCount(CdsMemoryOperation op, long operand)
        {
            uint load = op == CdsMemoryOperation.Assign ? 0u : 1u;
            uint arith = op switch
            {
                CdsMemoryOperation.Assign   => LoadImmediateWordCount(operand),
                CdsMemoryOperation.Add      => FitsSigned16(operand) ? 1u : LoadImmediateWordCount(operand) + 1,
                CdsMemoryOperation.Subtract => FitsSigned16(-operand) ? 1u : LoadImmediateWordCount(operand) + 1,
                _                           => LoadImmediateWordCount(operand) + 2   // mult/div + mflo
            };
            return load + arith + 1;   // + store
        }

        /// <summary>Emit 1–2 instructions that load <paramref name="value"/> into <paramref name="reg"/>.</summary>
        private static void EmitLoadImmediate(List<OperationSyntaxItem> ops, int lineNumber, string lineText,
            string reg, long value, ref uint cur)
        {
            if (FitsSigned16(value))
            {
                AddOp(ops, lineNumber, lineText, ref cur, "addiu", $"addiu {reg}, $zero, {value}");
            }
            else if (value >= 0 && value <= ushort.MaxValue)
            {
                AddOp(ops, lineNumber, lineText, ref cur, "ori", $"ori {reg}, $zero, ${value:X4}");
            }
            else
            {
                uint u = (uint)value;
                AddOp(ops, lineNumber, lineText, ref cur, "lui", $"lui {reg}, ${u >> 16:X4}");
                AddOp(ops, lineNumber, lineText, ref cur, "ori", $"ori {reg}, {reg}, ${u & 0xFFFF:X4}");
            }
        }

        private static void AddOp(List<OperationSyntaxItem> ops, int lineNumber, string lineText,
            ref uint cur, string mnemonic, string asm)
        {
            ops.Add(new OperationSyntaxItem
            {
                LineNumber = lineNumber, LineText = lineText,
                Address = cur, EncodedWord = TryAssemble(asm), Mnemonic = mnemonic
            });
            cur += 4;
        }

        private static MemorySyntaxItem BuildMemOp(
            int lineNumber, string lineText,
            uint memOffset, string reg,
            CdsMemoryOperation op, long operand,
            uint address,
            CdsMemorySize memSize = CdsMemorySize.Word)
        {
            string offsetStr = $"${(ushort)memOffset:X4}";

            // Select load/store mnemonics based on data width
            var (loadMnem, storeMnem) = memSize switch
            {
                CdsMemorySize.Byte       => ("lb",  "sb"),
                CdsMemorySize.Half       => ("lh",  "sh"),
                CdsMemorySize.Doubleword => ("ld",  "sd"),
                _                        => ("lw",  "sw")
            };

            var ops = new List<OperationSyntaxItem>();
            uint cur = address;

            if (op != CdsMemoryOperation.Assign)
            {
                // Load current value
                AddOp(ops, lineNumber, lineText, ref cur, loadMnem, $"{loadMnem} $t9, {offsetStr}(${reg})");
            }

            switch (op)
            {
                case CdsMemoryOperation.Assign:
                    EmitLoadImmediate(ops, lineNumber, lineText, "$t9", operand, ref cur);
                    break;

                case CdsMemoryOperation.Add:
                    if (FitsSigned16(operand))
                    {
                        AddOp(ops, lineNumber, lineText, ref cur, "addiu", $"addiu $t9, $t9, {operand}");
                    }
                    else
                    {
                        EmitLoadImmediate(ops, lineNumber, lineText, "$at", operand, ref cur);
                        AddOp(ops, lineNumber, lineText, ref cur, "addu", "addu $t9, $t9, $at");
                    }
                    break;

                case CdsMemoryOperation.Subtract:
                    if (FitsSigned16(-operand))
                    {
                        AddOp(ops, lineNumber, lineText, ref cur, "addiu", $"addiu $t9, $t9, {-operand}");
                    }
                    else
                    {
                        EmitLoadImmediate(ops, lineNumber, lineText, "$at", operand, ref cur);
                        AddOp(ops, lineNumber, lineText, ref cur, "subu", "subu $t9, $t9, $at");
                    }
                    break;

                case CdsMemoryOperation.Multiply:
                    // The EE has no multiply-immediate: materialise the operand, then mult + mflo.
                    EmitLoadImmediate(ops, lineNumber, lineText, "$at", operand, ref cur);
                    AddOp(ops, lineNumber, lineText, ref cur, "mult", "mult $t9, $at");
                    AddOp(ops, lineNumber, lineText, ref cur, "mflo", "mflo $t9");
                    break;

                case CdsMemoryOperation.Divide:
                    EmitLoadImmediate(ops, lineNumber, lineText, "$at", operand, ref cur);
                    AddOp(ops, lineNumber, lineText, ref cur, "div", "div $t9, $at");
                    AddOp(ops, lineNumber, lineText, ref cur, "mflo", "mflo $t9");
                    break;

                default:
                    throw new ArgumentException($"Unknown mem operation: {op}");
            }

            // Store result
            AddOp(ops, lineNumber, lineText, ref cur, storeMnem, $"{storeMnem} $t9, {offsetStr}(${reg})");

            return new MemorySyntaxItem
            {
                LineNumber = lineNumber, LineText = lineText,
                Address = address, MemoryOffset = memOffset,
                Register = reg, Operation = op, Operand = unchecked((uint)operand),
                MemorySize = memSize,
                Operations = ops
            };
        }

        private static LoadMemorySyntaxItem BuildLoadOp(
            int lineNumber, string lineText,
            string targetReg, uint memOffset, string baseReg,
            uint address,
            CdsMemorySize memSize = CdsMemorySize.Word)
        {
            string offsetStr = $"${(ushort)memOffset:X4}";
            string loadMnem = memSize switch
            {
                CdsMemorySize.Byte       => "lb",
                CdsMemorySize.Half       => "lh",
                CdsMemorySize.Doubleword => "ld",
                _                        => "lw"
            };

            uint encoded = TryAssemble($"{loadMnem} ${targetReg}, {offsetStr}(${baseReg})");
            return new LoadMemorySyntaxItem
            {
                LineNumber = lineNumber,
                LineText = lineText,
                Address = address,
                TargetRegister = targetReg,
                BaseRegister = baseReg,
                MemoryOffset = memOffset,
                MemorySize = memSize,
                Operation = new OperationSyntaxItem
                {
                    LineNumber = lineNumber,
                    LineText = lineText,
                    Address = address,
                    EncodedWord = encoded,
                    Mnemonic = loadMnem
                }
            };
        }

        private static StoreMemorySyntaxItem BuildStoreOp(
            int lineNumber, string lineText,
            string sourceReg, uint memOffset, string baseReg,
            uint address,
            CdsMemorySize memSize = CdsMemorySize.Word)
        {
            string offsetStr = $"${(ushort)memOffset:X4}";
            string storeMnem = memSize switch
            {
                CdsMemorySize.Byte       => "sb",
                CdsMemorySize.Half       => "sh",
                CdsMemorySize.Doubleword => "sd",
                _                        => "sw"
            };

            uint encoded = TryAssemble($"{storeMnem} ${sourceReg}, {offsetStr}(${baseReg})");
            return new StoreMemorySyntaxItem
            {
                LineNumber = lineNumber,
                LineText = lineText,
                Address = address,
                SourceRegister = sourceReg,
                BaseRegister = baseReg,
                MemoryOffset = memOffset,
                MemorySize = memSize,
                Operation = new OperationSyntaxItem
                {
                    LineNumber = lineNumber,
                    LineText = lineText,
                    Address = address,
                    EncodedWord = encoded,
                    Mnemonic = storeMnem
                }
            };
        }

        private static PointerSyntaxItem BuildPointer(
            int lineNumber, string lineText,
            uint baseAddress, IReadOnlyList<ushort> offsets, uint value,
            uint address)
        {
            var item = new PointerSyntaxItem
            {
                LineNumber  = lineNumber,
                LineText    = lineText,
                Address     = address,
                BaseAddress = baseAddress,
                Offsets     = offsets.ToList(),
                Value       = value
            };

            uint cur = address;

            var loadBase = BuildSetReg(lineNumber, lineText, "t8", baseAddress, cur);
            item.Operations.Add(loadBase.LuiOp);
            item.Operations.Add(loadBase.OriOp);
            cur += 8;

            item.Operations.Add(new OperationSyntaxItem
            {
                LineNumber = lineNumber,
                LineText   = lineText,
                Address    = cur,
                EncodedWord = TryAssemble("lw t8, $0000(t8)"),
                Mnemonic   = "lw"
            });
            cur += 4;

            for (int i = 0; i < offsets.Count - 1; i++)
            {
                string offsetStr = $"${offsets[i]:X4}";
                item.Operations.Add(new OperationSyntaxItem
                {
                    LineNumber = lineNumber,
                    LineText   = lineText,
                    Address    = cur,
                    EncodedWord = TryAssemble($"lw t8, {offsetStr}(t8)"),
                    Mnemonic   = "lw"
                });
                cur += 4;
            }

            var loadValue = BuildSetReg(lineNumber, lineText, "t9", value, cur);
            item.Operations.Add(loadValue.LuiOp);
            item.Operations.Add(loadValue.OriOp);
            cur += 8;

            string finalOffsetStr = $"${offsets[^1]:X4}";
            item.Operations.Add(new OperationSyntaxItem
            {
                LineNumber = lineNumber,
                LineText   = lineText,
                Address    = cur,
                EncodedWord = TryAssemble($"sw t9, {finalOffsetStr}(t8)"),
                Mnemonic   = "sw"
            });

            return item;
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
        /// code line. Comment markers inside string literals are left alone.
        /// </summary>
        internal static string StripInlineComments(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return line;

            bool inString = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }
                if (inString) continue;
                if (i == 0) continue;   // a marker at column 0 is a full-line comment, handled by the caller
                if (c == ';' || c == '#' || (c == '/' && i + 1 < line.Length && line[i + 1] == '/'))
                    return line[..i].TrimEnd();
            }
            return line;
        }

        private static uint TryAssemble(string asm)
        {
            return MipsAssembler.TryAssemble(asm, out uint word, out _) ? word : 0;
        }

        private static bool IsNumericToken(string token) =>
            token.StartsWith("$", StringComparison.Ordinal)
            || token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            || (token.Length > 0 && char.IsDigit(token[0]));

        /// <summary>
        /// Parse a hexadecimal address / value: <c>$XXXXXXXX</c>, <c>0xXXXXXXXX</c> or bare hex.
        /// Values wider than 32 bits (more than 8 hex digits) are rejected with a clear message.
        /// </summary>
        internal static bool TryParseHex32(string token, out uint value, out string? error)
        {
            value = 0;
            error = null;
            string s = token.Trim();
            if (s.StartsWith("$", StringComparison.Ordinal)) s = s[1..];
            else if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];

            if (s.Length == 0 || !s.All(Uri.IsHexDigit))
            {
                error = $"'{token}' is not a hexadecimal value (expected $XXXXXXXX or 0xXXXXXXXX)";
                return false;
            }
            if (s.Length > 8)
            {
                error = $"'{token}' has {s.Length} hex digits; addresses and values must fit in 32 bits (at most 8 hex digits)";
                return false;
            }
            value = Convert.ToUInt32(s, 16);
            return true;
        }

        /// <summary>
        /// Parse a 32-bit value where <c>$</c>/<c>0x</c> mean hex and a bare number is decimal
        /// (optionally negative). Used by <c>setreg</c>, <c>li</c> and <c>mem[]</c> operands.
        /// </summary>
        internal static bool TryParseSignedValue32(string token, out long value, out string? error)
        {
            value = 0;
            error = null;
            string s = token.Trim();
            bool negative = false;
            if (s.StartsWith("-", StringComparison.Ordinal)) { negative = true; s = s[1..]; }
            else if (s.StartsWith("+", StringComparison.Ordinal)) { s = s[1..]; }

            if (s.StartsWith("$", StringComparison.Ordinal) || s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseHex32(s, out uint hex, out error)) return false;
                value = negative ? -(long)hex : hex;
            }
            else if (!long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value))
            {
                error = $"'{token}' is not a number (expected decimal, 0x-hex or $-hex)";
                return false;
            }
            else if (value > uint.MaxValue)
            {
                error = $"'{token}' does not fit in 32 bits";
                return false;
            }
            else if (negative)
            {
                value = -value;
            }

            if (value < int.MinValue)
            {
                error = $"'{token}' does not fit in 32 bits";
                return false;
            }
            return true;
        }

        private static bool TryParseValue32(string token, out uint value, out string? error)
        {
            value = 0;
            if (!TryParseSignedValue32(token, out long signed, out error)) return false;
            value = unchecked((uint)signed);
            return true;
        }

        /// <summary>Parse a mem[] / load / store offset: <c>0x10</c>, <c>$10</c> or decimal, up to 16 bits.</summary>
        internal static bool TryParseOffset16(string token, out uint value, out string? error)
        {
            value = 0;
            if (!TryParseSignedValue32(token, out long signed, out error)) return false;
            if (signed < short.MinValue || signed > ushort.MaxValue)
            {
                error = $"'{token}' does not fit in a 16-bit offset (expected -32768..65535)";
                return false;
            }
            value = (uint)(signed & 0xFFFF);
            return true;
        }

        private static uint ParseUInt(string s)
        {
            if (s.StartsWith("$", StringComparison.Ordinal))
                return Convert.ToUInt32(s[1..], 16);
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt32(s, 16);
            return uint.Parse(s, CultureInfo.InvariantCulture);
        }

        private static bool TryParsePointerDirective(
            string line,
            out uint baseAddress,
            out List<ushort> offsets,
            out uint value)
        {
            baseAddress = 0;
            offsets = new List<ushort>();
            value = 0;

            var match = PointerRegex.Match(line);
            if (!match.Success)
                return false;

            string chainText = match.Groups[1].Value.Trim();
            string valueText = match.Groups[2].Value.Trim();
            var chainParts = chainText.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (chainParts.Length < 2)
                return false;

            try
            {
                baseAddress = ParseUInt(chainParts[0]);

                for (int i = 1; i < chainParts.Length; i++)
                {
                    uint parsedOffset = ParseUInt(chainParts[i]);
                    if (parsedOffset > ushort.MaxValue)
                        return false;
                    offsets.Add((ushort)parsedOffset);
                }

                value = ParseUInt(valueText);
                return offsets.Count > 0;
            }
            catch
            {
                offsets.Clear();
                return false;
            }
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

        private static CdsMemorySize ParseMemSize(string suffix) =>
            suffix.ToLowerInvariant() switch
            {
                "b" => CdsMemorySize.Byte,
                "h" => CdsMemorySize.Half,
                "d" => CdsMemorySize.Doubleword,
                _   => CdsMemorySize.Word   // "w" or empty (default)
            };

        private void AddDiagnostic(
            CdsDiagnosticSeverity severity,
            int lineNumber,
            string message,
            string lineText,
            string? token = null,
            string? code = null)
        {
            int startColumn = 1;
            int endColumn = Math.Max(2, lineText.Length + 1);

            if (!string.IsNullOrEmpty(token))
            {
                int tokenIndex = lineText.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                if (tokenIndex >= 0)
                {
                    startColumn = tokenIndex + 1;
                    endColumn = Math.Max(startColumn + 1, startColumn + token.Length);
                }
            }

            _diagnostics.Add(new CdsDiagnostic
            {
                Severity        = severity,
                LineNumber      = Math.Max(1, lineNumber),
                ColumnNumber    = startColumn,
                EndLineNumber   = Math.Max(1, lineNumber),
                EndColumnNumber = endColumn,
                Message         = message,
                Code            = code,
                SourceLine      = lineText
            });
        }

        private void AddDelaySlotDiagnostics(IEnumerable<CdsSyntaxItem> items)
        {
            var executableItems = new List<(CdsSyntaxItem Item, uint Address)>();
            CollectExecutableItems(items, executableItems);

            for (int i = 0; i < executableItems.Count; i++)
            {
                if (executableItems[i].Item is not OperationSyntaxItem op ||
                    !DelaySlotMnemonics.Contains(op.Mnemonic))
                {
                    continue;
                }

                var next = i + 1 < executableItems.Count
                    ? executableItems[i + 1]
                    : ((CdsSyntaxItem Item, uint Address)?)null;

                if (!next.HasValue || next.Value.Address != op.Address + 4)
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Warning, op.LineNumber,
                        $"instruction '{op.Mnemonic}' has an empty branch delay slot (NOP recommended)",
                        op.LineText, op.Mnemonic, "CDS0006");
                    continue;
                }

                if (next.Value.Item is OperationSyntaxItem nextOp &&
                    DelaySlotMnemonics.Contains(nextOp.Mnemonic))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Warning, op.LineNumber,
                        $"instruction '{op.Mnemonic}' has an unsafe delay slot containing control-flow instruction '{nextOp.Mnemonic}'",
                        op.LineText, op.Mnemonic, "CDS0007");
                }
            }
        }

        private static void CollectExecutableItems(
            IEnumerable<CdsSyntaxItem> items,
            List<(CdsSyntaxItem Item, uint Address)> result)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case OperationSyntaxItem op:
                        result.Add((op, op.Address));
                        break;
                    case HexCodeSyntaxItem hex:
                        result.Add((hex, hex.Address));
                        break;
                    case SetRegSyntaxItem setReg:
                        result.Add((setReg, setReg.Address));
                        break;
                    case LaSyntaxItem la:
                        result.Add((la, la.Address));
                        break;
                    case MemorySyntaxItem mem:
                        result.Add((mem, mem.Address));
                        break;
                    case LoadMemorySyntaxItem load:
                        result.Add((load, load.Address));
                        break;
                    case StoreMemorySyntaxItem store:
                        result.Add((store, store.Address));
                        break;
                    case PointerSyntaxItem pointer:
                        result.Add((pointer, pointer.Address));
                        break;
                    case StringSyntaxItem str:
                        result.Add((str, str.Address));
                        break;
                    case ScopeSyntaxItem scope:
                        CollectExecutableItems(scope.Body, result);
                        break;
                }
            }
        }
    }
}
