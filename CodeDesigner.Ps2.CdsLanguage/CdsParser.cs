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
    /// <para>
    /// Before the two-pass parse the source is run through <see cref="CdsPreprocessor"/>,
    /// which expands high-level constructs (<c>const</c>, <c>alias</c>, <c>macro</c>,
    /// <c>include</c>, <c>if</c>/<c>else</c>/<c>endif</c>, <c>while</c>/<c>endwhile</c>,
    /// <c>repeat</c>/<c>endrepeat</c>) into plain CDS.
    /// </para>
    ///
    /// <para><b>Language features (after preprocessing):</b></para>
    /// <list type="bullet">
    ///   <item><c>address $XXXXXXXX</c> – set current assembly address</item>
    ///   <item><c>hexcode $XXXXXXXX</c> – emit a raw 32-bit word</item>
    ///   <item><c>setreg reg, $XXXXXXXX</c> – load 32-bit immediate (LUI+ADDIU)</item>
    ///   <item><c>mem[0xOFFSET] reg = 0xVAL</c> – 32-bit memory write (default)</item>
    ///   <item><c>mem.b / mem.h / mem.w / mem.d</c> – sized memory operations (8/16/32/64-bit)</item>
    ///   <item><c>load[.b/.h/.w/.d] dst, mem[0xOFFSET] base</c> – typed memory loads</item>
    ///   <item><c>store[.b/.h/.w/.d] src, mem[0xOFFSET] base</c> – typed memory stores</item>
    ///   <item><c>pointer $BASE + $OFF1 [+ $OFF2 ...] = $VALUE</c> – pointer-chain write expansion</item>
    ///   <item><c>string "text"</c> – null-terminated ASCII string data</item>
    ///   <item><c>name:</c> – label definition</item>
    ///   <item><c>instr args :label</c> – MIPS instruction with optional label reference</item>
    ///   <item><c>// …</c> and <c>/* … */</c> – comments</item>
    ///   <item><c>scope Name { … }</c> – named scope block</item>
    ///   <item><c>assert mem[$ADDR] == $VAL</c> – sidecar injection-time assertion</item>
    ///   <item><c>breakpoint</c> – sidecar hardware breakpoint at current address</item>
    ///   <item><c>watchpoint (write|read|rw) $ADDR SIZE</c> – sidecar memory watchpoint</item>
    ///   <item><c>log "message"</c> / <c>print "message"</c> – sidecar compile/apply log message</item>
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
    /// <see cref="Diagnostics"/> after the call returns.
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
            new(@"^address\s+\$([0-9a-fA-F]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex HexCodeRegex =
            new(@"^hexcode\s+\$([0-9a-fA-F]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SetRegRegex =
            new(@"^setreg\s+\$?(\w+)\s*,\s*\$([0-9a-fA-F]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LabelDefRegex =
            new(@"^([a-zA-Z_][a-zA-Z0-9_]*):$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex StringRegex =
            new(@"^string\s+""(.*?)""$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Group 1: optional size suffix (b/h/w/d), Group 2: offset, Group 3: register,
        // Group 4: operator, Group 5: operand value.
        private static readonly Regex MemRegex =
            new(@"^mem(?:\.(b|h|w|d))?\[(0x[0-9A-Fa-f]+)\]\s+(\w+)\s*(\+=|-=|\*=|/=|=)\s*(0x[0-9A-Fa-f]+|\d+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LoadRegex =
            new(@"^load(?:\.(b|h|w|d))?\s+\$?(\w+)\s*,\s*mem\[(0x[0-9A-Fa-f]+)\]\s+\$?(\w+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex StoreRegex =
            new(@"^store(?:\.(b|h|w|d))?\s+\$?(\w+)\s*,\s*mem\[(0x[0-9A-Fa-f]+)\]\s+\$?(\w+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PointerRegex =
            new(@"^pointer\s+(.+?)\s*=\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ScopeRegex =
            new(@"^scope\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*\{$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // assert mem[$ADDR] == $VAL  (or !=)
        private static readonly Regex AssertRegex =
            new(@"^assert\s+mem\[\$([0-9a-fA-F]+)\]\s*(==|!=)\s*\$([0-9a-fA-F]+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // breakpoint
        private static readonly Regex BreakpointRegex =
            new(@"^breakpoint$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // watchpoint (write|read|rw|readwrite) $ADDR SIZE
        private static readonly Regex WatchpointRegex =
            new(@"^watchpoint\s+(write|read|rw|readwrite)\s+\$([0-9a-fA-F]+)\s+(\d+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // log "message" / print "message"
        private static readonly Regex LogRegex =
            new(@"^(log|print)\s+""(.*?)""$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LogPrefixRegex =
            new(@"^(log|print)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // freeze (byte|half|word) $ADDR = VALUE
        private static readonly Regex FreezeRegex =
            new(@"^freeze\s+(byte|half|word)\s+\$([0-9a-fA-F]+)\s*=\s*(?:0x)?([0-9a-fA-F]+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // cond mem[$ADDR] == VALUE
        private static readonly Regex CondRegex =
            new(@"^cond\s+mem\[\$([0-9a-fA-F]+)\]\s*==\s*(?:0x)?([0-9a-fA-F]+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // endcond
        private static readonly Regex EndCondRegex =
            new(@"^endcond$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // timer N
        private static readonly Regex TimerRegex =
            new(@"^timer\s+(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // endtimer
        private static readonly Regex EndTimerRegex =
            new(@"^endtimer$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        private static readonly HashSet<string> DelaySlotMnemonics = new(StringComparer.OrdinalIgnoreCase)
        {
            "j", "jal", "jr", "jalr",
            "beq",  "bne",  "blez",  "bgtz",  "bltz",  "bgez",
            "beql", "bnel", "blezl", "bgtzl", "bltzl", "bgezl",
            "bltzal", "bgezal", "bltzall", "bgezall",
            "bc1t", "bc1f", "bc1tl", "bc1fl"
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
            sourceCode = preprocessor.Process(sourceCode, basePath);
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
            // Tracks what opened each frame on itemsStack: "scope" or "cond"
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
                    itemsKindStack.Push("scope");
                    continue;
                }

                // ---- Scope close ----
                if (trimmed == "}")
                {
                    if (scopeStack.Count > 0)
                    {
                        scopeStack.Pop();
                        itemsStack.Pop();
                        if (itemsKindStack.Count > 0) itemsKindStack.Pop();
                    }
                    else
                    {
                        AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                            "unexpected '}' with no open scope", trimmed, "}");
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
                    string sizeSuffix = memMatch.Groups[1].Value;
                    uint offset  = Convert.ToUInt32(memMatch.Groups[2].Value, 16);
                    string reg   = memMatch.Groups[3].Value;
                    string opStr = memMatch.Groups[4].Value;
                    string operandStr = memMatch.Groups[5].Value;
                    uint operand = ParseUInt(operandStr);
                    var memOp  = ParseMemOp(opStr);
                    var memSz  = ParseMemSize(sizeSuffix);
                    uint instrCount = memOp == CdsMemoryOperation.Assign ? 2u : 3u;
                    var item = BuildMemOp(lineNumber, trimmed, offset, reg, memOp, operand, currentAddress, memSz);
                    current.Add(item);
                    AddDiagnostic(CdsDiagnosticSeverity.Warning, lineNumber,
                        "register $t9 is clobbered by mem[] expansion", trimmed, "mem", "CDS0003");
                    currentAddress += instrCount * 4;
                    continue;
                }

                // ---- Typed memory load ----
                var loadMatch = LoadRegex.Match(trimmed);
                if (loadMatch.Success)
                {
                    string sizeSuffix = loadMatch.Groups[1].Value;
                    string targetReg = loadMatch.Groups[2].Value;
                    uint offset = Convert.ToUInt32(loadMatch.Groups[3].Value, 16);
                    string baseReg = loadMatch.Groups[4].Value;
                    var memSz = ParseMemSize(sizeSuffix);
                    var item = BuildLoadOp(lineNumber, trimmed, targetReg, offset, baseReg, currentAddress, memSz);
                    current.Add(item);
                    currentAddress += 4;
                    continue;
                }

                // ---- Typed memory store ----
                var storeMatch = StoreRegex.Match(trimmed);
                if (storeMatch.Success)
                {
                    string sizeSuffix = storeMatch.Groups[1].Value;
                    string sourceReg = storeMatch.Groups[2].Value;
                    uint offset = Convert.ToUInt32(storeMatch.Groups[3].Value, 16);
                    string baseReg = storeMatch.Groups[4].Value;
                    var memSz = ParseMemSize(sizeSuffix);
                    var item = BuildStoreOp(lineNumber, trimmed, sourceReg, offset, baseReg, currentAddress, memSz);
                    current.Add(item);
                    currentAddress += 4;
                    continue;
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
                    continue;
                }
                if (trimmed.StartsWith("pointer", StringComparison.OrdinalIgnoreCase))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        "malformed pointer directive; expected pointer $BASE + $OFFSET [+ $OFFSET ...] = $VALUE",
                        trimmed, "pointer", "CDS0010");
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

                // ---- Assert sidecar ----
                var assertMatch = AssertRegex.Match(trimmed);
                if (assertMatch.Success)
                {
                    uint assertAddr  = Convert.ToUInt32(assertMatch.Groups[1].Value, 16);
                    string assertOp  = assertMatch.Groups[2].Value;
                    uint assertVal   = Convert.ToUInt32(assertMatch.Groups[3].Value, 16);
                    current.Add(new AssertSyntaxItem
                    {
                        LineNumber    = lineNumber,
                        LineText      = trimmed,
                        Address       = assertAddr,
                        Operator      = assertOp,
                        ExpectedValue = assertVal
                    });
                    // Sidecar — does not advance the address counter
                    continue;
                }

                // ---- Breakpoint sidecar ----
                if (BreakpointRegex.IsMatch(trimmed))
                {
                    current.Add(new BreakpointSyntaxItem
                    {
                        LineNumber = lineNumber,
                        LineText   = trimmed,
                        Address    = currentAddress
                    });
                    // Sidecar — does not advance the address counter
                    continue;
                }

                // ---- Watchpoint sidecar ----
                var watchMatch = WatchpointRegex.Match(trimmed);
                if (watchMatch.Success)
                {
                    string accessStr = watchMatch.Groups[1].Value;
                    uint watchAddr   = Convert.ToUInt32(watchMatch.Groups[2].Value, 16);
                    uint watchSize   = uint.Parse(watchMatch.Groups[3].Value);
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
                    continue;
                }

                // ---- Log/print sidecar ----
                var logMatch = LogRegex.Match(trimmed);
                if (logMatch.Success)
                {
                    string directive = logMatch.Groups[1].Value.ToLowerInvariant();
                    string messageText = logMatch.Groups[2].Value;
                    current.Add(new LogSyntaxItem
                    {
                        LineNumber  = lineNumber,
                        LineText    = trimmed,
                        Address     = currentAddress,
                        Directive   = directive,
                        MessageText = messageText
                    });
                    // Sidecar — does not advance the address counter
                    continue;
                }
                if (LogPrefixRegex.IsMatch(trimmed))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        "malformed log/print directive; expected log \"message\" or print \"message\"",
                        trimmed,
                        trimmed.StartsWith("print", StringComparison.OrdinalIgnoreCase) ? "print" : "log",
                        "CDS0008");
                    continue;
                }

                // ---- Freeze sidecar ----
                var freezeMatch = FreezeRegex.Match(trimmed);
                if (freezeMatch.Success)
                {
                    string sizeStr   = freezeMatch.Groups[1].Value.ToLowerInvariant();
                    uint freezeAddr  = Convert.ToUInt32(freezeMatch.Groups[2].Value, 16);
                    uint freezeVal   = Convert.ToUInt32(freezeMatch.Groups[3].Value, 16);
                    var freezeType = sizeStr switch
                    {
                        "half" => CdsFreezeType.Half,
                        "word" => CdsFreezeType.Word,
                        _      => CdsFreezeType.Byte
                    };
                    current.Add(new FreezeSyntaxItem
                    {
                        LineNumber = lineNumber,
                        LineText   = trimmed,
                        FreezeType = freezeType,
                        Address    = freezeAddr,
                        Value      = freezeVal
                    });
                    // Sidecar — does not advance the address counter
                    continue;
                }

                // ---- Cond block sidecar ----
                var condMatch = CondRegex.Match(trimmed);
                if (condMatch.Success)
                {
                    uint condAddr = Convert.ToUInt32(condMatch.Groups[1].Value, 16);
                    uint condVal  = Convert.ToUInt32(condMatch.Groups[2].Value, 16);
                    var condItem  = new CondSyntaxItem
                    {
                        LineNumber      = lineNumber,
                        LineText        = trimmed,
                        Address         = condAddr,
                        ConditionValue  = condVal
                    };
                    current.Add(condItem);
                    itemsStack.Push(condItem.Body);
                    itemsKindStack.Push("cond");
                    // Sidecar — does not advance the address counter
                    continue;
                }

                // ---- Timer block sidecar ----
                var timerMatch = TimerRegex.Match(trimmed);
                if (timerMatch.Success)
                {
                    uint intervalFrames = uint.Parse(timerMatch.Groups[1].Value);
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
                    continue;
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
                    continue;
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
                    continue;
                }
                TryParseOperation(trimmed, lineNumber, scopeStack, labelMap,
                                  ref currentAddress, current);
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
                    string opStr = memMatch.Groups[4].Value;
                    currentAddress += opStr == "=" ? 8u : 12u;
                    continue;
                }

                if (LoadRegex.IsMatch(t))
                {
                    currentAddress += 4;
                    continue;
                }

                if (StoreRegex.IsMatch(t))
                {
                    currentAddress += 4;
                    continue;
                }

                if (TryParsePointerDirective(t, out _, out var pointerOffsets, out _))
                {
                    currentAddress += (uint)(pointerOffsets.Count + 5) * 4;
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

                // Sidecar directives — consume no address space
                if (AssertRegex.IsMatch(t))     continue;
                if (BreakpointRegex.IsMatch(t)) continue;
                if (WatchpointRegex.IsMatch(t)) continue;
                if (FreezeRegex.IsMatch(t))     continue;
                if (CondRegex.IsMatch(t))       continue;
                if (EndCondRegex.IsMatch(t))    continue;
                if (TimerRegex.IsMatch(t))      continue;
                if (EndTimerRegex.IsMatch(t))   continue;

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
            catch (Exception ex)
            {
                AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                    $"assembly failed for '{assemblyLine}': {ex.Message}", line, mnemonic, "CDS0002");
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
            uint address,
            CdsMemorySize memSize = CdsMemorySize.Word)
        {
            string offsetStr  = $"${(ushort)memOffset:X4}";
            string operandStr = $"${(ushort)operand:X4}";

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
                uint load = TryAssemble($"{loadMnem} $t9, {offsetStr}(${reg})");
                ops.Add(new OperationSyntaxItem
                {
                    LineNumber = lineNumber, LineText = lineText,
                    Address = cur, EncodedWord = load, Mnemonic = loadMnem
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

            // Store result
            uint store = TryAssemble($"{storeMnem} $t9, {offsetStr}(${reg})");
            ops.Add(new OperationSyntaxItem
            {
                LineNumber = lineNumber, LineText = lineText,
                Address = cur, EncodedWord = store, Mnemonic = storeMnem
            });

            return new MemorySyntaxItem
            {
                LineNumber = lineNumber, LineText = lineText,
                Address = address, MemoryOffset = memOffset,
                Register = reg, Operation = op, Operand = operand,
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
            item.Operations.Add(loadBase.AddiuOp);
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
            item.Operations.Add(loadValue.AddiuOp);
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
            if (s.StartsWith("$", StringComparison.Ordinal))
                return Convert.ToUInt32(s[1..], 16);
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt32(s, 16);
            return uint.Parse(s);
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
                    case MemorySyntaxItem mem:
                        result.Add((mem, mem.Address));
                        break;
                    case LoadMemorySyntaxItem load:
                        result.Add((load, load.Address));
                        break;
                    case StoreMemorySyntaxItem store:
                        result.Add((store, store.Address));
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
