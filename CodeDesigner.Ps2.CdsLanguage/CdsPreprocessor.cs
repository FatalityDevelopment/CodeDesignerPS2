using System.Text;
using System.Text.RegularExpressions;

namespace CodeDesigner.Ps2.CdsLanguage
{
    /// <summary>
    /// Source-level text transformer that expands high-level CDS constructs into plain CDS
    /// before the two-pass <see cref="CdsParser"/> sees the source.
    ///
    /// <para><b>Features handled:</b></para>
    /// <list type="bullet">
    ///   <item><c>const NAME = VALUE</c> — named constants substituted in subsequent lines.</item>
    ///   <item><c>alias NAME = $REG</c> — register-name aliases substituted in subsequent lines.</item>
    ///   <item><c>macro NAME(p1, p2, …) … endmacro</c> — parameterised macro definition and call expansion.</item>
    ///   <item><c>include "file.cds"</c> — reads the file, preprocesses it recursively, and inlines the result.</item>
    ///   <item><c>if COND … else … endif</c> — expands to branch instructions and auto-generated labels.</item>
    ///   <item><c>while COND … endwhile</c> — expands to a top-tested loop with branch + jump.</item>
    ///   <item><c>repeat N … endrepeat</c> — expands to a counter-driven loop using <c>$t9</c>.</item>
    /// </list>
    ///
    /// <para><b>Condition syntax</b> (for <c>if</c> and <c>while</c>):</para>
    /// <list type="bullet">
    ///   <item><c>$reg == $reg2</c> / <c>$reg != $reg2</c> — register equality comparison.</item>
    ///   <item><c>$reg == 0</c> / <c>$reg != 0</c> — compare with zero.</item>
    ///   <item><c>$reg == 0xVALUE</c> / <c>$reg != 0xVALUE</c> — compare with immediate (uses <c>$t9</c> as scratch).</item>
    ///   <item><c>$reg &lt; $reg2</c> / <c>$reg &gt; $reg2</c> / <c>$reg &lt;= $reg2</c> / <c>$reg &gt;= $reg2</c> — signed relational comparison (expands via <c>slt</c> + conditional branch; uses <c>$t8</c> as scratch).</item>
    ///   <item><c>$reg &lt;u $reg2</c> / <c>$reg &gt;u $reg2</c> / <c>$reg &lt;=u $reg2</c> / <c>$reg &gt;=u $reg2</c> — unsigned relational comparison (uses <c>sltu</c>).</item>
    /// </list>
    ///
    /// <para>
    /// Auto-generated labels use the prefix <c>__cds_</c> followed by a monotonically
    /// increasing counter so they never clash with user-defined labels.
    /// </para>
    ///
    /// <para>
    /// Diagnostics produced during preprocessing are accumulated in <see cref="Diagnostics"/>
    /// and should be merged with the parser's own diagnostics by the caller.
    /// </para>
    /// </summary>
    public sealed class CdsPreprocessor
    {
        // -----------------------------------------------------------------------
        // Regex patterns
        // -----------------------------------------------------------------------

        private static readonly Regex ConstRegex =
            new(@"^const\s+([A-Za-z_]\w*)\s*=\s*(.+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AliasRegex =
            new(@"^alias\s+([A-Za-z_]\w*)\s*=\s*(\$?\w+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MacroDefRegex =
            new(@"^macro\s+([A-Za-z_]\w*)\s*\(([^)]*)\)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MacroCallRegex =
            new(@"^([A-Za-z_]\w*)\s*\(([^)]*)\)$",
                RegexOptions.Compiled);

        // extern function NAME(p1, p2, ...) at :label_name
        private static readonly Regex ExternFunctionRegex =
            new(@"^extern\s+function\s+([A-Za-z_]\w*)\s*\(\s*([^)]*)\s*\)\s+at\s+:([a-zA-Z_][a-zA-Z0-9_.]*)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // function NAME(p1, p2, ...) {       — opens a function definition block
        private static readonly Regex FunctionDefRegex =
            new(@"^function\s+([A-Za-z_]\w*)\s*\(\s*([^)]*)\s*\)\s*\{$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Function-call site: NAME(arg1, arg2, ...) — same shape as macro call;
        // dispatch order in the main loop disambiguates macros from extern functions.
        private static readonly Regex FunctionCallRegex = MacroCallRegex;

        // Callee-saved register detection is done through CdsRegisters so bare
        // names (s0), ABI names ($s0), numeric forms ($16), and rN aliases ($r16)
        // stay in sync with the parser and VS Code .cds grammar.

        // Detect any jal/jalr in a body — means $ra is live across the call and must be saved.
        private static readonly Regex CallInBodyRegex =
            new(@"\b(jal|jalr)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex IncludeRegex =
            new(@"^include\s+""([^""]+)""$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex IfRegex =
            new(@"^if\s+(\$?\w+)\s*(==|!=|<=u|>=u|<u|>u|<=|>=|<|>)\s*(\$?\w+|0x[0-9a-fA-F]+|\d+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex WhileRegex =
            new(@"^while\s+(\$?\w+)\s*(==|!=|<=u|>=u|<u|>u|<=|>=|<|>)\s*(\$?\w+|0x[0-9a-fA-F]+|\d+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RepeatRegex =
            new(@"^repeat\s+(\d+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // -----------------------------------------------------------------------
        // Nested-block open / close detection
        // -----------------------------------------------------------------------

        private static readonly Regex BlockOpenRegex =
            new(@"^(if|while|repeat)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static bool IsBlockClose(string t) =>
            string.Equals(t, "endif",     StringComparison.OrdinalIgnoreCase)
         || string.Equals(t, "endwhile",  StringComparison.OrdinalIgnoreCase)
         || string.Equals(t, "endrepeat", StringComparison.OrdinalIgnoreCase);

        // -----------------------------------------------------------------------
        // State
        // -----------------------------------------------------------------------

        private readonly List<CdsDiagnostic> _diagnostics = new();
        private int _counter;

        /// <summary>
        /// Diagnostics produced during the most recent <see cref="Process"/> call.
        /// </summary>
        public IReadOnlyList<CdsDiagnostic> Diagnostics => _diagnostics;

        // -----------------------------------------------------------------------
        // Public entry point
        // -----------------------------------------------------------------------

        // Stack of extern-function tables for nested Process calls. The outermost
        // call's table is at the bottom; recursive calls (from `include`, `if`/`while`
        // bodies, function-definition expansion) inherit the parent's bindings so
        // that function-call dispatch sees declarations made in the enclosing file.
        private readonly Stack<Dictionary<string, ExternFunction>> _externScopeStack = new();

        /// <summary>
        /// Preprocess <paramref name="source"/> and return the transformed source that
        /// can be passed to <see cref="CdsParser.Parse"/>.
        /// </summary>
        /// <param name="source">Raw CDS source text.</param>
        /// <param name="basePath">
        /// Directory used to resolve <c>include</c> paths.
        /// Pass <see cref="string.Empty"/> for in-memory sources.
        /// </param>
        /// <param name="depth">Recursion depth guard for nested includes (internal use).</param>
        public string Process(string source, string basePath = "", int depth = 0)
        {
            if (depth == 0)
            {
                _diagnostics.Clear();
                _counter = 0;
                _externScopeStack.Clear();
            }

            if (depth > 32)
            {
                _diagnostics.Add(new CdsDiagnostic
                {
                    Severity = CdsDiagnosticSeverity.Error,
                    LineNumber = 0,
                    Message = "include nesting depth exceeded (possible cycle)"
                });
                return string.Empty;
            }

            var lines = source.Replace("\r\n", "\n").Split('\n').ToList();

            // First sub-pass: collect macro definitions (they may appear anywhere in the file
            // so we gather them all before processing calls).
            var macros = CollectMacros(lines);

            // Collect extern-function declarations. Unlike macros, these traverse
            // include boundaries so a stdlib header file can declare functions used
            // by source that includes it. We also inherit any bindings made by an
            // outer Process scope (e.g., function-body expansion sees the enclosing
            // file's externs).
            var externFunctions = CollectExternFunctions(lines, basePath, depth);
            if (_externScopeStack.Count > 0)
            {
                foreach (var (k, v) in _externScopeStack.Peek())
                    if (!externFunctions.ContainsKey(k))
                        externFunctions[k] = v;
            }

            // Native function definitions. Each is registered as a callable extern
            // function (target label = function name) so call sites resolve from
            // anywhere in the document — including before the definition appears.
            var localFunctions = CollectFunctionDefs(lines);
            foreach (var (name, fnDef) in localFunctions)
            {
                externFunctions[name] = new ExternFunction(name, fnDef.Parameters, name);
            }

            _externScopeStack.Push(externFunctions);

            var consts  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var output  = new List<string>();

            int i = 0;
            while (i < lines.Count)
            {
                int lineNumber = i + 1;
                string raw     = lines[i];
                string trimmed = raw.Trim();

                i++;

                // Skip empty lines — preserve them for line-number fidelity
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    output.Add(raw);
                    continue;
                }

                // Pass through full-line comments and multi-line comment markers
                // (the parser handles them); we still need to strip inline comments
                // for our own keyword matching below.
                if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*/"))
                {
                    output.Add(raw);
                    continue;
                }

                // Strip inline comment for keyword matching only (we re-use `raw` when emitting)
                string clean = StripInlineComment(trimmed);

                // ---- const NAME = VALUE ----
                var constMatch = ConstRegex.Match(clean);
                if (constMatch.Success)
                {
                    string name  = constMatch.Groups[1].Value;
                    string value = constMatch.Groups[2].Value.Trim();
                    consts[name] = value;
                    // Do not emit this line — it is consumed by the preprocessor
                    continue;
                }

                // ---- alias NAME = $REG ----
                var aliasMatch = AliasRegex.Match(clean);
                if (aliasMatch.Success)
                {
                    string name = aliasMatch.Groups[1].Value;
                    string reg  = aliasMatch.Groups[2].Value;
                    aliases[name] = reg;
                    continue;
                }

                // ---- macro NAME(...) ... endmacro — skip the definition body here ----
                var macroDefMatch = MacroDefRegex.Match(clean);
                if (macroDefMatch.Success)
                {
                    // Consume lines until endmacro so they are not re-emitted
                    while (i < lines.Count &&
                           !string.Equals(lines[i].Trim(), "endmacro", StringComparison.OrdinalIgnoreCase))
                    {
                        i++;
                    }
                    if (i < lines.Count) i++; // consume the 'endmacro' line itself
                    continue;
                }

                // ---- extern function declaration — already collected, strip from output ----
                if (ExternFunctionRegex.IsMatch(clean))
                {
                    continue;
                }

                // ---- function NAME(...) { body } — expand in place to label + prologue + body + epilogue ----
                var funcDefMatch = FunctionDefRegex.Match(clean);
                if (funcDefMatch.Success)
                {
                    string fnName = funcDefMatch.Groups[1].Value;
                    if (!localFunctions.TryGetValue(fnName, out var fnDef))
                    {
                        // Should not happen — collected pass found the same regex — but guard anyway.
                        AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                            $"internal: function '{fnName}' missing from collection table",
                            raw, fnName, "CDS2010");
                        // Skip past the body so we don't emit it raw.
                        SkipToMatchingBrace(lines, ref i);
                        continue;
                    }

                    // Skip the body lines in this scan — CollectFunctionDefs already
                    // captured them. Advance `i` past the closing brace.
                    SkipToMatchingBrace(lines, ref i);

                    if (fnDef.Parameters.Length > 4)
                    {
                        AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                            $"function '{fnName}' has {fnDef.Parameters.Length} parameters; the calling convention supports at most 4",
                            raw, fnName, "CDS2011");
                        continue;
                    }

                    string expanded = ExpandFunctionDefinition(fnDef);
                    string recursed = Process(expanded, basePath, depth + 1);
                    output.Add(recursed);
                    continue;
                }

                // ---- include "file.cds" ----
                var inclMatch = IncludeRegex.Match(clean);
                if (inclMatch.Success)
                {
                    string rel  = inclMatch.Groups[1].Value;
                    string full = string.IsNullOrEmpty(basePath)
                        ? rel
                        : Path.Combine(basePath, rel);

                    if (!File.Exists(full))
                    {
                        _diagnostics.Add(new CdsDiagnostic
                        {
                            Severity   = CdsDiagnosticSeverity.Error,
                            LineNumber = lineNumber,
                            Message    = $"include file not found: '{full}'"
                        });
                    }
                    else
                    {
                        string includedSource = File.ReadAllText(full);
                        string includedBasePath = Path.GetDirectoryName(Path.GetFullPath(full)) ?? string.Empty;
                        string expanded = Process(includedSource, includedBasePath, depth + 1);
                        output.Add(expanded);
                    }
                    continue;
                }

                // Apply const/alias substitutions to the current (clean) line before
                // checking for other keywords and before emitting.
                clean = ApplySubstitutions(clean, consts, aliases);

                // ---- if COND ----
                var ifMatch = IfRegex.Match(clean);
                if (ifMatch.Success)
                {
                    string lhs = ifMatch.Groups[1].Value;
                    string op  = ifMatch.Groups[2].Value;
                    string rhs = ifMatch.Groups[3].Value;

                    var (thenLines, elseLines, consumed, terminated) = CollectBlock(lines, i);
                    i += consumed;

                    if (!terminated)
                    {
                        AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                            "unterminated if block — missing 'endif'", raw, "if", "CDS1001");
                        continue;
                    }

                    int id = _counter++;
                    string endLabel  = $"__cds_if_{id}_end";
                    string elseLabel = $"__cds_if_{id}_else";

                    bool hasElse = elseLines != null;
                    string branchTarget = hasElse ? elseLabel : endLabel;

                    // Emit inverted branch (branch when condition is FALSE)
                    EmitBranch(output, lhs, op, rhs, branchTarget, lineNumber, clean);

                    // Recursively preprocess the then-body
                    string thenExpanded = Process(
                        string.Join("\n", thenLines), basePath, depth + 1);
                    output.Add(thenExpanded);

                    if (hasElse)
                    {
                        // Jump over else body
                        output.Add($"j :{endLabel}");
                        output.Add("nop");
                        output.Add($"{elseLabel}:");
                        string elseExpanded = Process(
                            string.Join("\n", elseLines!), basePath, depth + 1);
                        output.Add(elseExpanded);
                    }

                    output.Add($"{endLabel}:");
                    continue;
                }

                // ---- while COND ----
                var whileMatch = WhileRegex.Match(clean);
                if (whileMatch.Success)
                {
                    string lhs = whileMatch.Groups[1].Value;
                    string op  = whileMatch.Groups[2].Value;
                    string rhs = whileMatch.Groups[3].Value;

                    var (bodyLines, _, consumed, terminated) = CollectBlock(lines, i);
                    i += consumed;

                    if (!terminated)
                    {
                        AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                            "unterminated while block — missing 'endwhile'", raw, "while", "CDS1002");
                        continue;
                    }

                    int id = _counter++;
                    string topLabel = $"__cds_while_{id}_top";
                    string endLabel = $"__cds_while_{id}_end";

                    output.Add($"{topLabel}:");
                    EmitBranch(output, lhs, op, rhs, endLabel, lineNumber, clean);

                    string bodyExpanded = Process(
                        string.Join("\n", bodyLines), basePath, depth + 1);
                    output.Add(bodyExpanded);

                    output.Add($"j :{topLabel}");
                    output.Add("nop");
                    output.Add($"{endLabel}:");
                    continue;
                }

                // ---- repeat N ----
                var repeatMatch = RepeatRegex.Match(clean);
                if (repeatMatch.Success)
                {
                    uint count = uint.Parse(repeatMatch.Groups[1].Value);

                    var (bodyLines, _, consumed, terminated) = CollectBlock(lines, i);
                    i += consumed;

                    if (!terminated)
                    {
                        AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                            "unterminated repeat block — missing 'endrepeat'", raw, "repeat", "CDS1003");
                        continue;
                    }

                    int id = _counter++;
                    string topLabel = $"__cds_repeat_{id}_top";

                    // addiu immediate is a signed 16-bit field; values 1–0x7FFF are safe.
                    // Values 0x8000–0xFFFF would be interpreted as negative and loop forever.
                    if (count == 0 || count > 0x7FFF)
                    {
                        output.Add($"// repeat count {count} is out of range (must be 1–32767); block skipped");
                        continue;
                    }

                    AddDiagnostic(CdsDiagnosticSeverity.Warning, lineNumber,
                        "register $t9 is clobbered by repeat expansion", clean, "$t9", "CDS1004");

                    // Use $t9 as the iteration counter (documented; same scratch reg as mem[] ops)
                    output.Add($"addiu $t9, $zero, ${count:X}");
                    output.Add($"{topLabel}:");

                    string bodyExpanded = Process(
                        string.Join("\n", bodyLines), basePath, depth + 1);
                    output.Add(bodyExpanded);

                    output.Add($"addiu $t9, $t9, $FFFF");   // $t9 -= 1 (signed 16-bit -1)
                    output.Add($"bne $t9, $zero, :{topLabel}");
                    output.Add("nop");
                    continue;
                }

                if (string.Equals(clean, "else", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(clean, "endif", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(clean, "endwhile", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(clean, "endrepeat", StringComparison.OrdinalIgnoreCase))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"unexpected block terminator '{clean}'", raw, clean, "CDS1005");
                    continue;
                }

                // ---- macro call: MacroName(arg1, arg2, …) ----
                var macroCallMatch = MacroCallRegex.Match(clean);
                if (macroCallMatch.Success)
                {
                    string macroName = macroCallMatch.Groups[1].Value;
                    if (macros.TryGetValue(macroName, out var macroDef))
                    {
                        string argsRaw = macroCallMatch.Groups[2].Value;
                        string[] args  = SplitArgs(argsRaw);

                        if (args.Length != macroDef.Parameters.Length)
                        {
                            _diagnostics.Add(new CdsDiagnostic
                            {
                                Severity   = CdsDiagnosticSeverity.Error,
                                LineNumber = lineNumber,
                                Message    = $"macro '{macroName}' expects {macroDef.Parameters.Length} argument(s) but got {args.Length}"
                            });
                        }
                        else
                        {
                            string expanded = ExpandMacro(macroDef, args);
                            string recursed = Process(expanded, basePath, depth + 1);
                            output.Add(recursed);
                        }
                        continue;
                    }

                    // Not a macro — try extern-function call dispatch
                    if (externFunctions.TryGetValue(macroName, out var fn))
                    {
                        string argsRaw = macroCallMatch.Groups[2].Value;
                        string[] args  = SplitArgs(argsRaw);

                        if (args.Length != fn.Parameters.Length)
                        {
                            AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                                $"function '{fn.Name}' expects {fn.Parameters.Length} argument(s) but got {args.Length}",
                                raw, fn.Name, "CDS2002");
                            continue;
                        }

                        string expanded = ExpandExternFunctionCall(fn, args, lineNumber, raw);
                        output.Add(expanded);
                        continue;
                    }
                    // Not a known macro or extern function — fall through and emit as-is
                }

                // ---- Normal line: emit with substitutions applied ----
                // Reconstruct with original indentation but substituted content
                string indent = raw.Length > raw.TrimStart().Length
                    ? raw[..(raw.Length - raw.TrimStart().Length)]
                    : string.Empty;
                output.Add(indent + clean);
            }

            _externScopeStack.Pop();
            return string.Join("\n", output);
        }

        // -----------------------------------------------------------------------
        // Macro collection (first sub-pass)
        // -----------------------------------------------------------------------

        private static Dictionary<string, MacroDef> CollectMacros(IList<string> lines)
        {
            var macros = new Dictionary<string, MacroDef>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            while (i < lines.Count)
            {
                string clean = StripInlineComment(lines[i].Trim());
                var m = MacroDefRegex.Match(clean);
                if (m.Success)
                {
                    string name = m.Groups[1].Value;
                    string[] parms = SplitArgs(m.Groups[2].Value);
                    i++;
                    var body = new List<string>();
                    while (i < lines.Count &&
                           !string.Equals(lines[i].Trim(), "endmacro", StringComparison.OrdinalIgnoreCase))
                    {
                        body.Add(lines[i]);
                        i++;
                    }
                    macros[name] = new MacroDef(name, parms, body);
                }
                i++;
            }
            return macros;
        }

        private static string ExpandMacro(MacroDef macro, string[] args)
        {
            var sb = new StringBuilder();
            foreach (var line in macro.Body)
            {
                string expanded = line;
                for (int p = 0; p < macro.Parameters.Length; p++)
                    expanded = ReplaceWholeWord(expanded, macro.Parameters[p], args[p]);
                sb.AppendLine(expanded);
            }
            return sb.ToString();
        }

        // -----------------------------------------------------------------------
        // Block collection (if / while / repeat body)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Collect lines belonging to the body of an <c>if</c>, <c>while</c>, or <c>repeat</c>
        /// block that starts immediately at <paramref name="from"/> (the line <em>after</em>
        /// the opening keyword has already been consumed by the caller).
        /// </summary>
        /// <returns>
        /// A tuple of:
        /// <list type="bullet">
        ///   <item><c>thenLines</c> — lines before an <c>else</c> (or the entire body if no else).</item>
        ///   <item><c>elseLines</c> — lines after an <c>else</c>, or <see langword="null"/>.</item>
        ///   <item><c>consumed</c> — number of source lines consumed (caller advances by this).</item>
        /// </list>
        /// </returns>
        private (List<string> thenLines, List<string>? elseLines, int consumed, bool terminated)
            CollectBlock(IList<string> lines, int from)
        {
            var thenLines = new List<string>();
            List<string>? elseLines = null;
            bool inElse = false;
            int depth = 0;
            int i = from;
            bool terminated = false;

            while (i < lines.Count)
            {
                string clean = StripInlineComment(lines[i].Trim());

                if (BlockOpenRegex.IsMatch(clean))
                    depth++;

                if (IsBlockClose(clean))
                {
                    if (depth == 0)
                    {
                        // This is our matching close
                        i++;
                        terminated = true;
                        break;
                    }
                    depth--;
                }

                if (string.Equals(clean, "else", StringComparison.OrdinalIgnoreCase) && depth == 0)
                {
                    inElse = true;
                    elseLines = new List<string>();
                    i++;
                    continue;
                }

                if (inElse)
                    elseLines!.Add(lines[i]);
                else
                    thenLines.Add(lines[i]);

                i++;
            }

            return (thenLines, elseLines, i - from, terminated);
        }

        // -----------------------------------------------------------------------
        // Branch emission helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Emit the MIPS instructions that branch to <paramref name="targetLabel"/> when
        /// the condition is <em>false</em> (inverted), followed by a delay-slot NOP.
        /// </summary>
        /// <remarks>
        /// <para>Equality operators (== / !=) expand to a single <c>beq</c> / <c>bne</c>.</para>
        /// <para>
        /// Relational operators (&lt;, &gt;, &lt;=, &gt;=) expand via <c>slt</c> (signed) or
        /// <c>sltu</c> (unsigned, suffix <c>u</c>) plus a conditional branch.
        /// Register <c>$t8</c> is used as the SLT result scratch; register <c>$t9</c> is
        /// used to materialise large immediates.
        /// </para>
        /// <para>Comparisons against zero emit specialised MIPS branch instructions
        /// (<c>bltz</c>, <c>bgtz</c>, <c>blez</c>, <c>bgez</c>) instead of SLT sequences.</para>
        /// </remarks>
        private void EmitBranch(
            List<string> output,
            string lhs, string op, string rhs,
            string targetLabel,
            int lineNumber,
            string lineText)
        {
            bool isZero = rhs is "0" or "0x0" or "0x00" or "0x0000" or "0x00000000"
                       || string.Equals(rhs, "$zero", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(rhs, "zero",  StringComparison.OrdinalIgnoreCase);

            bool isReg = rhs.StartsWith("$") && !TryParseImmediate(rhs, out _);

            // ── Equality operators — single beq / bne ─────────────────────────────
            if (op is "==" or "!=")
            {
                // Invert: branch when condition is FALSE
                string branchMnem = op == "==" ? "bne" : "beq";
                if (isZero)
                {
                    output.Add($"{branchMnem} {lhs}, $zero, :{targetLabel}");
                    output.Add("nop");
                }
                else if (isReg)
                {
                    output.Add($"{branchMnem} {lhs}, {rhs}, :{targetLabel}");
                    output.Add("nop");
                }
                else
                {
                    if (!TryParseImmediate(rhs, out uint immVal))
                    {
                        AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                            $"cannot parse immediate '{rhs}' in condition", lineText, rhs, "CDS1006");
                        return;
                    }
                    AddDiagnostic(CdsDiagnosticSeverity.Warning, lineNumber,
                        "register $t9 is clobbered by condition expansion", lineText, "$t9", "CDS1007");
                    output.Add($"setreg t9, ${immVal:X8}");
                    output.Add($"{branchMnem} {lhs}, $t9, :{targetLabel}");
                    output.Add("nop");
                }
                return;
            }

            // ── Relational operators — expand via slt / sltu ──────────────────────
            // Strip trailing 'u' to get the base operator; determine signed vs unsigned
            bool isUnsigned = op.EndsWith("u", StringComparison.OrdinalIgnoreCase);
            string baseOp   = isUnsigned ? op[..^1] : op;   // "<", ">", "<=", ">="
            string sltMnem  = isUnsigned ? "sltu" : "slt";

            // Special case: comparisons against zero use dedicated MIPS branches
            // (bltz/bgtz/blez/bgez) which do not require SLT and work regardless of sign.
            if (isZero && !isUnsigned)
            {
                // We branch to target when condition is FALSE (inverted logic):
                //   if $t0 <  0  → body runs when $t0 < 0;  skip (branch) when $t0 >= 0 → bgez
                //   if $t0 >  0  → skip when $t0 <= 0 → blez
                //   if $t0 <= 0  → skip when $t0 > 0  → bgtz
                //   if $t0 >= 0  → skip when $t0 < 0  → bltz
                string zBranch = baseOp switch
                {
                    "<"  => "bgez",
                    ">"  => "blez",
                    "<=" => "bgtz",
                    _    => "bltz"   // ">="
                };
                output.Add($"{zBranch} {lhs}, :{targetLabel}");
                output.Add("nop");
                return;
            }

            // General relational: compute SLT into $t8, then beq/bne $t8, $zero.
            //
            // Truth table (branch = skip body = condition is FALSE):
            //
            //   Cond       SLT form                Branch when FALSE
            //   ─────────  ─────────────────────── ─────────────────────────────────────
            //   lhs <  rhs  slt $t8, lhs, rhs = 1  beq $t8, $zero (skip when NOT less)
            //   lhs >  rhs  slt $t8, rhs, lhs = 1  beq $t8, $zero (skip when NOT greater)
            //   lhs <= rhs  slt $t8, rhs, lhs = 1  bne $t8, $zero (skip when rhs < lhs)
            //   lhs >= rhs  slt $t8, lhs, rhs = 1  bne $t8, $zero (skip when lhs < rhs)
            //
            // rhsFirst=true  →  slt $t8, <rhs>, <lhs>  (operands swapped)
            // rhsFirst=false →  slt $t8, <lhs>, <rhs>
            bool rhsFirst  = baseOp is ">" or "<=";
            string sltBranch = baseOp is "<" or ">" ? "beq" : "bne";

            if (isZero)
            {
                // Unsigned compare with zero: sltu x, reg, $zero is always 0.
                // For simplicity, materialise zero in $t9 and fall through to the
                // register-comparison path below.
                AddDiagnostic(CdsDiagnosticSeverity.Warning, lineNumber,
                    "register $t9 is clobbered by condition expansion", lineText, "$t9", "CDS1007");
                output.Add($"setreg t9, $00000000");
            }

            if (isReg || isZero)
            {
                AddDiagnostic(CdsDiagnosticSeverity.Warning, lineNumber,
                    "register $t8 is clobbered by condition expansion", lineText, "$t8", "CDS1008");
                string rhsReg = isZero ? "$t9" : rhs;
                string a = rhsFirst ? rhsReg : lhs;
                string b = rhsFirst ? lhs     : rhsReg;
                output.Add($"{sltMnem} $t8, {a}, {b}");
                output.Add($"{sltBranch} $t8, $zero, :{targetLabel}");
                output.Add("nop");
            }
            else
            {
                // Immediate RHS: materialise into $t9 first
                if (!TryParseImmediate(rhs, out uint immVal))
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"cannot parse immediate '{rhs}' in condition", lineText, rhs, "CDS1006");
                    return;
                }
                AddDiagnostic(CdsDiagnosticSeverity.Warning, lineNumber,
                    "register $t9 is clobbered by condition expansion", lineText, "$t9", "CDS1007");
                AddDiagnostic(CdsDiagnosticSeverity.Warning, lineNumber,
                    "register $t8 is clobbered by condition expansion", lineText, "$t8", "CDS1008");
                output.Add($"setreg t9, ${immVal:X8}");
                string a = rhsFirst ? "$t9" : lhs;
                string b = rhsFirst ? lhs   : "$t9";
                output.Add($"{sltMnem} $t8, {a}, {b}");
                output.Add($"{sltBranch} $t8, $zero, :{targetLabel}");
                output.Add("nop");
            }
        }

        // -----------------------------------------------------------------------
        // Substitution helpers
        // -----------------------------------------------------------------------

        private static string ApplySubstitutions(
            string line,
            Dictionary<string, string> consts,
            Dictionary<string, string> aliases)
        {
            foreach (var (name, value) in consts)
                line = ReplaceWholeWord(line, name, value);
            foreach (var (name, reg) in aliases)
                line = ReplaceWholeWord(line, name, reg);
            return line;
        }

        // -----------------------------------------------------------------------
        // Utility
        // -----------------------------------------------------------------------

        private static string StripInlineComment(string line)
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

        private static string[] SplitArgs(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
                return Array.Empty<string>();
            return args.Split(',').Select(a => a.Trim()).ToArray();
        }

        private static string ReplaceWholeWord(string source, string word, string replacement)
        {
            // Use word-boundary regex so "t0" doesn't replace inside "t01"
            return Regex.Replace(
                source,
                $@"\b{Regex.Escape(word)}\b",
                replacement);
        }

        private static bool TryParseImmediate(string s, out uint value)
        {
            // Strip leading '$' (CDS hex prefix)
            string src = s.StartsWith("$") ? s[1..] : s;
            if (src.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                try { value = Convert.ToUInt32(src, 16); return true; }
                catch { value = 0; return false; }
            }
            if (uint.TryParse(src, out value)) return true;
            // Pure hex string (no 0x prefix, but all hex digits)
            try { value = Convert.ToUInt32(src, 16); return true; }
            catch { value = 0; return false; }
        }

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

        // -----------------------------------------------------------------------
        // Extern-function collection and call-site expansion
        // -----------------------------------------------------------------------

        private static Dictionary<string, ExternFunction> CollectExternFunctions(
            IList<string> lines, string basePath, int depth)
        {
            var functions = new Dictionary<string, ExternFunction>(StringComparer.OrdinalIgnoreCase);
            CollectExternFunctionsInto(lines, basePath, depth, functions, new HashSet<string>());
            return functions;
        }

        private static void CollectExternFunctionsInto(
            IList<string> lines, string basePath, int depth,
            Dictionary<string, ExternFunction> sink, HashSet<string> visited)
        {
            if (depth > 32) return;

            foreach (var raw in lines)
            {
                string clean = StripInlineComment(raw.Trim());

                // Direct declaration in this file
                var m = ExternFunctionRegex.Match(clean);
                if (m.Success)
                {
                    string name = m.Groups[1].Value;
                    string[] parms = SplitArgs(m.Groups[2].Value);
                    string targetLabel = m.Groups[3].Value.ToLowerInvariant();
                    sink[name] = new ExternFunction(name, parms, targetLabel);
                    continue;
                }

                // Recurse into includes so declarations transit across `include "..."`.
                var inclMatch = IncludeRegex.Match(clean);
                if (!inclMatch.Success) continue;

                string rel = inclMatch.Groups[1].Value;
                string full = string.IsNullOrEmpty(basePath) ? rel : Path.Combine(basePath, rel);

                string normalised;
                try { normalised = Path.GetFullPath(full); }
                catch { continue; }

                if (!File.Exists(normalised)) continue;
                if (!visited.Add(normalised)) continue;

                string includedSource = File.ReadAllText(normalised);
                var includedLines = includedSource.Replace("\r\n", "\n").Split('\n');
                string includedBasePath = Path.GetDirectoryName(normalised) ?? string.Empty;
                CollectExternFunctionsInto(includedLines, includedBasePath, depth + 1, sink, visited);
            }
        }

        /// <summary>
        /// Expand a call site like <c>str_copy(t0, t1)</c> into the calling-convention
        /// register loads followed by <c>jal :targetLabel</c> + delay-slot <c>nop</c>.
        ///
        /// <para>Loads place each argument into <c>$a0..$a3</c>; if the argument is
        /// already in the destination register the move is skipped. Cross-arg conflicts
        /// (e.g., calling <c>f($a1, $a0)</c>) are resolved by routing one source through
        /// <c>$at</c>.</para>
        ///
        /// <para>Maximum four arguments — beyond that requires stack-based passing,
        /// which this layer does not implement. Caller is responsible for saving
        /// <c>$ra</c> if it itself uses a return-address.</para>
        /// </summary>
        private string ExpandExternFunctionCall(
            ExternFunction fn, string[] args, int lineNumber, string lineText)
        {
            if (fn.Parameters.Length > 4)
            {
                AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                    $"function '{fn.Name}' has {fn.Parameters.Length} parameters; the calling convention supports at most 4",
                    lineText, fn.Name, "CDS2003");
                return string.Empty;
            }

            // Build (target, source) pairs for the moves we need to emit. Skip identity moves.
            // Sources/targets are normalised to "$xN" form so cycle-detection is by string equality.
            var moves = new List<(string Target, string Source)>();
            for (int idx = 0; idx < args.Length; idx++)
            {
                string target = $"$a{idx}";
                string? source = NormaliseRegister(args[idx]);
                if (source is null)
                {
                    AddDiagnostic(CdsDiagnosticSeverity.Error, lineNumber,
                        $"argument {idx + 1} to '{fn.Name}' is not a recognised register: '{args[idx]}'",
                        lineText, args[idx], "CDS2004");
                    return string.Empty;
                }
                if (!string.Equals(target, source, StringComparison.OrdinalIgnoreCase))
                    moves.Add((target, source));
            }

            var sb = new StringBuilder();

            // Resolve moves with a simple iterative algorithm: emit any move whose target
            // register is not the source of any remaining move; if stuck (a cycle exists),
            // break the cycle by saving one source into $at.
            while (moves.Count > 0)
            {
                int safeIndex = moves.FindIndex(m =>
                    !moves.Any(other => other != m
                        && string.Equals(other.Source, m.Target, StringComparison.OrdinalIgnoreCase)));

                if (safeIndex >= 0)
                {
                    var move = moves[safeIndex];
                    sb.AppendLine($"addu {move.Target}, {move.Source}, $zero");
                    moves.RemoveAt(safeIndex);
                    continue;
                }

                // Cycle: save the first move's source into $at, then rewrite any other
                // moves that read from that register to read from $at instead.
                var first = moves[0];
                sb.AppendLine($"addu $at, {first.Source}, $zero");
                for (int j = 0; j < moves.Count; j++)
                {
                    if (string.Equals(moves[j].Source, first.Source, StringComparison.OrdinalIgnoreCase))
                        moves[j] = (moves[j].Target, "$at");
                }
            }

            sb.AppendLine($"jal :{fn.TargetLabel}");
            sb.AppendLine("nop");
            return sb.ToString();
        }

        /// <summary>
        /// Normalise a register reference to canonical <c>$name</c> form (e.g. <c>t0</c> →
        /// <c>$t0</c>). Returns <see langword="null"/> if the token is not a register.
        /// </summary>
        private static string? NormaliseRegister(string token)
            => CdsRegisters.NormalizeGpr(token);

        // -----------------------------------------------------------------------
        // Native function definition collection + expansion
        // -----------------------------------------------------------------------

        /// <summary>
        /// First sub-pass: walk the lines and collect <c>function NAME(...) { body }</c>
        /// definitions. Body lines are stored verbatim — they get parameter substitution,
        /// prologue, and epilogue applied at expansion time.
        /// </summary>
        private static Dictionary<string, FunctionDef> CollectFunctionDefs(IList<string> lines)
        {
            var functions = new Dictionary<string, FunctionDef>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            while (i < lines.Count)
            {
                string clean = StripInlineComment(lines[i].Trim());
                var m = FunctionDefRegex.Match(clean);
                if (!m.Success) { i++; continue; }

                string name = m.Groups[1].Value;
                string[] parms = SplitArgs(m.Groups[2].Value);
                i++;

                var body = new List<string>();
                int braceDepth = 1;
                while (i < lines.Count && braceDepth > 0)
                {
                    string lineTrim = lines[i].Trim();
                    // Count braces on the line — usually one per line, but be safe.
                    foreach (char ch in StripInlineComment(lineTrim))
                    {
                        if (ch == '{') braceDepth++;
                        else if (ch == '}') braceDepth--;
                    }
                    if (braceDepth > 0) body.Add(lines[i]);
                    i++;
                }
                functions[name] = new FunctionDef(name, parms, body);
            }
            return functions;
        }

        /// <summary>
        /// Advance <paramref name="i"/> past the body of a brace-delimited block whose
        /// opening <c>{</c> was on the line just consumed by the caller. After the call,
        /// <paramref name="i"/> points to the line after the matching <c>}</c>.
        /// </summary>
        private static void SkipToMatchingBrace(IList<string> lines, ref int i)
        {
            int braceDepth = 1;
            while (i < lines.Count && braceDepth > 0)
            {
                foreach (char ch in StripInlineComment(lines[i].Trim()))
                {
                    if (ch == '{') braceDepth++;
                    else if (ch == '}') braceDepth--;
                }
                i++;
            }
        }

        /// <summary>
        /// Expand a native function definition into the equivalent CDS source.
        /// <para>
        /// The expansion is:
        /// <code>
        ///   NAME:
        ///   addiu $sp, $sp, -frameSize
        ///   sw $ra, 0($sp)        // if body contains a jal/jalr
        ///   sw $sN, off($sp)      // for each $sN/$fp written to in body
        ///   ...body, with parameter names substituted for $a0..$a3...
        ///   lw $ra, 0($sp)
        ///   lw $sN, off($sp)
        ///   addiu $sp, $sp, frameSize
        ///   jr $ra
        ///   nop
        /// </code>
        /// </para>
        /// </summary>
        private static string ExpandFunctionDefinition(FunctionDef fn)
        {
            // Substitute parameter names → $aN before scanning so the alias regs
            // don't accidentally trip the saved-register detector.
            var substitutedBody = new List<string>(fn.Body.Count);
            foreach (var line in fn.Body)
            {
                string s = line;
                for (int p = 0; p < fn.Parameters.Length; p++)
                    s = ReplaceWholeWord(s, fn.Parameters[p], $"$a{p}");
                substitutedBody.Add(s);
            }

            // Collect callee-saved registers touched in the body.
            var savedRegs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            bool needsRa = false;
            foreach (var line in substitutedBody)
            {
                string text = StripInlineComment(line);
                if (CallInBodyRegex.IsMatch(text)) needsRa = true;
                foreach (var (_, index) in CdsRegisters.EnumerateGprTokens(text))
                {
                    if (CdsRegisters.IsCalleeSavedGprIndex(index))
                        savedRegs.Add("$" + CdsRegisters.CanonicalGprName(index));
                }
            }

            // Build the final list: $ra first if needed, then $s0..$s7, $fp.
            var orderedSaves = new List<string>();
            if (needsRa) orderedSaves.Add("$ra");
            orderedSaves.AddRange(savedRegs.OrderBy(r =>
                r == "$fp" ? 100 : int.Parse(r.Substring(2))));

            int frameSize = 0;
            if (orderedSaves.Count > 0)
            {
                // Round (count * 4) up to a 16-byte multiple per the PS2 EE ABI.
                int needed = orderedSaves.Count * 4;
                frameSize = (needed + 15) & ~15;
            }

            var sb = new StringBuilder();
            sb.AppendLine("// --- function definition: " + fn.Name + " ---");
            sb.AppendLine(fn.Name + ":");

            if (frameSize > 0)
            {
                sb.AppendLine($"addiu $sp, $sp, -{frameSize}");
                for (int idx = 0; idx < orderedSaves.Count; idx++)
                    sb.AppendLine($"sw {orderedSaves[idx]}, {idx * 4}($sp)");
            }

            foreach (var line in substitutedBody)
                sb.AppendLine(line);

            if (frameSize > 0)
            {
                for (int idx = 0; idx < orderedSaves.Count; idx++)
                    sb.AppendLine($"lw {orderedSaves[idx]}, {idx * 4}($sp)");
                sb.AppendLine($"addiu $sp, $sp, {frameSize}");
            }
            sb.AppendLine("jr $ra");
            sb.AppendLine("nop");
            sb.AppendLine("// --- end function: " + fn.Name + " ---");
            return sb.ToString();
        }

        // -----------------------------------------------------------------------
        // Inner types
        // -----------------------------------------------------------------------

        private sealed record MacroDef(string Name, string[] Parameters, List<string> Body);

        /// <summary>
        /// An <c>extern function NAME(p1, p2, ...) at :label</c> declaration.
        /// </summary>
        private sealed record ExternFunction(string Name, string[] Parameters, string TargetLabel);

        /// <summary>
        /// A native <c>function NAME(p1, p2, ...) { body }</c> definition collected
        /// during the first sub-pass.
        /// </summary>
        private sealed record FunctionDef(string Name, string[] Parameters, List<string> Body);
    }
}
