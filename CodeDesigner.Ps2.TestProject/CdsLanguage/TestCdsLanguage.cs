using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.CdsLanguage;
using CodeDesigner.Ps2.CdsLanguage.SyntaxItems;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>
    /// Tests for the CDS (Code Designer Script) language parser and compiler.
    /// Covers all syntax constructs including the new scope support.
    /// </summary>
    [TestClass]
    public class TestCdsLanguage
    {
        private static readonly CdsParser Parser = new();

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static List<CdsSyntaxItem> Parse(string src)
            => Parser.Parse(src.Trim());

        private static List<(uint Address, uint Word)> Compile(string src)
            => CdsCompiler.Compile(Parse(src));

        private static T FindItem<T>(List<CdsSyntaxItem> items, int index = 0)
            where T : CdsSyntaxItem
            => items.OfType<T>().Skip(index).First();

        // -----------------------------------------------------------------------
        // Comment tests
        // -----------------------------------------------------------------------

        [TestMethod]
        public void SingleLineComment_IsParsed()
        {
            var items = Parse("// hello world");
            var c = FindItem<CommentSyntaxItem>(items);
            Assert.AreEqual("hello world", c.Text);
            Assert.IsFalse(c.IsMultiLine);
        }

        [TestMethod]
        public void MultiLineComment_IsParsed()
        {
            var src = "/* line one\n   line two */";
            var items = Parse(src);
            var c = FindItem<CommentSyntaxItem>(items);
            Assert.IsTrue(c.IsMultiLine);
            StringAssert.Contains(c.Text, "line one");
        }

        // -----------------------------------------------------------------------
        // Address directive
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Address_Directive_SetsAddress()
        {
            var items = Parse("address $20200000");
            var a = FindItem<AddressSyntaxItem>(items);
            Assert.AreEqual(0x20200000u, a.Address);
        }

        [TestMethod]
        public void Address_ThenHexCode_HaveCorrectAddresses()
        {
            var src = @"
address $00200000
hexcode $DEADBEEF
hexcode $CAFEBABE
";
            var items = Parse(src);
            var hexItems = items.OfType<HexCodeSyntaxItem>().ToList();
            Assert.AreEqual(2, hexItems.Count);
            Assert.AreEqual(0x00200000u, hexItems[0].Address);
            Assert.AreEqual(0x00200004u, hexItems[1].Address);
        }

        // -----------------------------------------------------------------------
        // HexCode directive
        // -----------------------------------------------------------------------

        [TestMethod]
        public void HexCode_EmitsCorrectWord()
        {
            var items = Parse("address $00100000\nhexcode $DEADBEEF");
            var hex = FindItem<HexCodeSyntaxItem>(items);
            Assert.AreEqual(0xDEADBEEFu, hex.Value);
            Assert.AreEqual(0x00100000u, hex.Address);
        }

        [TestMethod]
        public void HexCode_CompiledPair_IsCorrect()
        {
            var pairs = Compile("address $00100000\nhexcode $AABBCCDD");
            Assert.AreEqual(1, pairs.Count);
            Assert.AreEqual(0x00100000u, pairs[0].Address);
            Assert.AreEqual(0xAABBCCDDu, pairs[0].Word);
        }

        // -----------------------------------------------------------------------
        // SetReg directive
        // -----------------------------------------------------------------------

        [TestMethod]
        public void SetReg_ExpandsToLuiAndAddiu()
        {
            var items = Parse("address $00200000\nsetreg t0, $ABCD1234");
            var sr = FindItem<SetRegSyntaxItem>(items);
            Assert.AreEqual(0x00200000u, sr.Address);
            Assert.AreEqual(0xABCD1234u, sr.Value);
            Assert.AreEqual("lui",   sr.LuiOp.Mnemonic);
            Assert.AreEqual("addiu", sr.AddiuOp.Mnemonic);
            Assert.AreEqual(0x00200000u, sr.LuiOp.Address);
            Assert.AreEqual(0x00200004u, sr.AddiuOp.Address);
        }

        [TestMethod]
        public void SetReg_Compiled_TwoPairs()
        {
            var pairs = Compile("address $00200000\nsetreg t0, $00010002");
            Assert.AreEqual(2, pairs.Count);
        }

        [TestMethod]
        public void SetReg_LuiEncoding_IsCorrect()
        {
            // setreg t0, $00010000  → lui t0, $0001 → MipsAssembler.Assemble("lui t0, $0001")
            var items = Parse("address $00200000\nsetreg t0, $00010000");
            var sr = FindItem<SetRegSyntaxItem>(items);
            uint expected = MipsAssembler.Assemble("lui t0, $0001");
            Assert.AreEqual(expected, sr.LuiOp.EncodedWord);
        }

        // -----------------------------------------------------------------------
        // String directive
        // -----------------------------------------------------------------------

        [TestMethod]
        public void String_EmitsNullTerminatedWords()
        {
            // "ABC\0" = 4 bytes → 1 word
            var items = Parse("address $00300000\nstring \"ABC\"");
            var str = FindItem<StringSyntaxItem>(items);
            Assert.AreEqual("ABC", str.Text);
            Assert.AreEqual(0x00300000u, str.Address);
            Assert.AreEqual(1, str.Words.Count);  // 4 bytes (A, B, C, \0) → 1 word
        }

        [TestMethod]
        public void String_LongerThan4Bytes_EmitsMultipleWords()
        {
            // "Hello\0" = 6 bytes, padded to 8 → 2 words
            var items = Parse("address $00300000\nstring \"Hello\"");
            var str = FindItem<StringSyntaxItem>(items);
            Assert.AreEqual(2, str.Words.Count);
        }

        [TestMethod]
        public void String_Compiled_ContainsCorrectWordCount()
        {
            // "Hi\0" = 3 bytes, padded to 4 → 1 word
            var pairs = Compile("address $00300000\nstring \"Hi\"");
            Assert.AreEqual(1, pairs.Count);
        }

        // -----------------------------------------------------------------------
        // Label definition
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Label_RecordsCurrentAddress()
        {
            var src = @"
address $00400000
hexcode $DEADBEEF
loop:
";
            var items = Parse(src);
            var label = FindItem<LabelSyntaxItem>(items);
            Assert.AreEqual("loop", label.Name);
            Assert.AreEqual(0x00400004u, label.Address);
        }

        [TestMethod]
        public void Label_FullName_EqualsNameForGlobalScope()
        {
            var items = Parse("address $00000000\nglobal_label:");
            var label = FindItem<LabelSyntaxItem>(items);
            Assert.AreEqual(label.Name, label.FullName);
        }

        // -----------------------------------------------------------------------
        // MIPS operation
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Operation_NOP_EncodesCorrectly()
        {
            var src = "address $00500000\nnop";
            var items = Parse(src);
            var op = FindItem<OperationSyntaxItem>(items);
            Assert.AreEqual(0x00000000u, op.EncodedWord);
            Assert.AreEqual(0x00500000u, op.Address);
        }

        [TestMethod]
        public void Operation_Add_EncodesCorrectly()
        {
            uint expected = MipsAssembler.Assemble("add $t0, $s0, $s1");
            var items = Parse("address $00500000\nadd $t0, $s0, $s1");
            var op = FindItem<OperationSyntaxItem>(items);
            Assert.AreEqual(expected, op.EncodedWord);
        }

        [TestMethod]
        public void Operation_AddrAdvancesBy4()
        {
            var items = Parse("address $00500000\nnop\nnop");
            var ops = items.OfType<OperationSyntaxItem>().ToList();
            Assert.AreEqual(0x00500000u, ops[0].Address);
            Assert.AreEqual(0x00500004u, ops[1].Address);
        }

        // -----------------------------------------------------------------------
        // Label reference in branch instruction
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Branch_WithLabelRef_EncodesCorrectOffset()
        {
            // Layout:
            //   $00500000: beq $t0, $t1, :target   (branch forward)
            //   $00500004: nop
            //   $00500008: nop
            //   $0050000C: target:
            var src = @"
address $00500000
beq $t0, $t1, :target
nop
nop
target:
";
            var items = Parse(src);
            var branch = items.OfType<OperationSyntaxItem>().First(o => o.Mnemonic == "beq");
            Assert.IsNotNull(branch.ResolvedLabel);
            Assert.AreEqual("target", branch.ResolvedLabel);

            // Verify the offset: (0x0050000C - (0x00500000 + 4)) / 4 = (0x0C - 4) / 4 = 8/4 = 2
            uint expectedEncoded = MipsAssembler.Assemble("beq $t0, $t1, $0002");
            Assert.AreEqual(expectedEncoded, branch.EncodedWord);
        }

        [TestMethod]
        public void Jump_WithLabelRef_EncodesCorrectTarget()
        {
            var src = @"
address $00500000
j :dest
dest:
";
            var items = Parse(src);
            var jumpOp = items.OfType<OperationSyntaxItem>().First(o => o.Mnemonic == "j");
            Assert.AreEqual("dest", jumpOp.ResolvedLabel);
            // dest is at 0x00500004 (byte address, word-aligned)
            uint expectedEncoded = MipsAssembler.Assemble("j $00500004");
            Assert.AreEqual(expectedEncoded, jumpOp.EncodedWord);
        }

        // -----------------------------------------------------------------------
        // Memory operation
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Mem_Assign_GeneratesTwoOps()
        {
            var items = Parse("address $00600000\nmem[0x0010] t0 = 0x05");
            var mem = FindItem<MemorySyntaxItem>(items);
            Assert.AreEqual(CdsMemoryOperation.Assign, mem.Operation);
            Assert.AreEqual(2, mem.Operations.Count);
        }

        [TestMethod]
        public void Mem_Add_GeneratesThreeOps()
        {
            var items = Parse("address $00600000\nmem[0x0010] t0 += 0x01");
            var mem = FindItem<MemorySyntaxItem>(items);
            Assert.AreEqual(CdsMemoryOperation.Add, mem.Operation);
            Assert.AreEqual(3, mem.Operations.Count);
        }

        [TestMethod]
        public void Mem_Subtract_GeneratesThreeOps()
        {
            var items = Parse("address $00600000\nmem[0x0010] t0 -= 0x01");
            var mem = FindItem<MemorySyntaxItem>(items);
            Assert.AreEqual(CdsMemoryOperation.Subtract, mem.Operation);
            Assert.AreEqual(3, mem.Operations.Count);
        }

        [TestMethod]
        public void Mem_Assign_AddressAdvancesBy8()
        {
            var src = "address $00600000\nmem[0x0010] t0 = 0x05\nhexcode $DEADBEEF";
            var items = Parse(src);
            var hex = FindItem<HexCodeSyntaxItem>(items);
            Assert.AreEqual(0x00600008u, hex.Address);
        }

        [TestMethod]
        public void Mem_Op_AddressAdvancesBy12()
        {
            var src = "address $00600000\nmem[0x0010] t0 += 0x01\nhexcode $DEADBEEF";
            var items = Parse(src);
            var hex = FindItem<HexCodeSyntaxItem>(items);
            Assert.AreEqual(0x0060000Cu, hex.Address);
        }

        [TestMethod]
        public void Mem_Assign_Compiled_TwoPairs()
        {
            var pairs = Compile("address $00600000\nmem[0x0010] t0 = 0x05");
            Assert.AreEqual(2, pairs.Count);
        }

        // -----------------------------------------------------------------------
        // Scope support
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Scope_IsParsedAsScopeSyntaxItem()
        {
            var src = @"
scope MyFunc {
    nop
}
";
            var items = Parse(src);
            var scope = FindItem<ScopeSyntaxItem>(items);
            Assert.AreEqual("MyFunc", scope.Name);
            Assert.AreEqual("myfunc", scope.FullPath);
        }

        [TestMethod]
        public void Scope_Body_ContainsItems()
        {
            var src = @"
address $00700000
scope MyFunc {
    nop
    nop
}
";
            var items = Parse(src);
            var scope = FindItem<ScopeSyntaxItem>(items);
            Assert.AreEqual(2, scope.Body.OfType<OperationSyntaxItem>().Count());
        }

        [TestMethod]
        public void Scope_AddressCounterContinues_AfterScope()
        {
            var src = @"
address $00700000
scope MyFunc {
    nop
    nop
}
hexcode $CAFECAFE
";
            var items = Parse(src);
            var hex = FindItem<HexCodeSyntaxItem>(items);
            Assert.AreEqual(0x00700008u, hex.Address);
        }

        [TestMethod]
        public void Scope_LabelFullName_IncludesScopePrefix()
        {
            var src = @"
address $00700000
scope MyFunc {
    loop:
}
";
            var items = Parse(src);
            var scope = FindItem<ScopeSyntaxItem>(items);
            var label = scope.Body.OfType<LabelSyntaxItem>().First();
            Assert.AreEqual("loop", label.Name);
            Assert.AreEqual("myfunc.loop", label.FullName);
        }

        [TestMethod]
        public void Scope_LabelRef_ResolvedFromInsideScope()
        {
            // beq inside scope references a local label
            var src = @"
address $00700000
scope MyFunc {
    beq $t0, $t1, :loop
    nop
    loop:
}
";
            var items = Parse(src);
            var scope = FindItem<ScopeSyntaxItem>(items);
            var branch = scope.Body.OfType<OperationSyntaxItem>().First(o => o.Mnemonic == "beq");
            Assert.AreEqual("myfunc.loop", branch.ResolvedLabel);
        }

        [TestMethod]
        public void Scope_LabelRef_FullyQualified_ResolvedFromOutside()
        {
            // jump from global scope to a label inside a scope using full name
            var src = @"
address $00700000
j :myfunc.entry
scope MyFunc {
    entry:
    nop
}
";
            var items = Parse(src);
            var jumpOp = items.OfType<OperationSyntaxItem>().First(o => o.Mnemonic == "j");
            Assert.AreEqual("myfunc.entry", jumpOp.ResolvedLabel);
        }

        [TestMethod]
        public void NestedScope_LabelFullName_IsDoublyQualified()
        {
            var src = @"
address $00800000
scope Outer {
    scope Inner {
        here:
        nop
    }
}
";
            var items = Parse(src);
            var outer = FindItem<ScopeSyntaxItem>(items);
            var inner = outer.Body.OfType<ScopeSyntaxItem>().First();
            var label = inner.Body.OfType<LabelSyntaxItem>().First();
            Assert.AreEqual("here", label.Name);
            Assert.AreEqual("outer.inner.here", label.FullName);
        }

        [TestMethod]
        public void NestedScope_LabelRef_ResolvedFromInnerScope()
        {
            var src = @"
address $00800000
scope Outer {
    scope Inner {
        beq $t0, $t1, :end
        nop
        end:
    }
}
";
            var items = Parse(src);
            var outer = FindItem<ScopeSyntaxItem>(items);
            var inner = outer.Body.OfType<ScopeSyntaxItem>().First();
            var branch = inner.Body.OfType<OperationSyntaxItem>().First(o => o.Mnemonic == "beq");
            Assert.AreEqual("outer.inner.end", branch.ResolvedLabel);
        }

        [TestMethod]
        public void NestedScope_LabelRef_ResolvedToParentScope()
        {
            // Branch inside Inner references a label defined in Outer (not Inner)
            // Resolution should widen to parent scope and find "outer.shared"
            var src = @"
address $00800000
scope Outer {
    shared:
    scope Inner {
        j :shared
    }
}
";
            var items = Parse(src);
            var outer = FindItem<ScopeSyntaxItem>(items);
            var inner = outer.Body.OfType<ScopeSyntaxItem>().First();
            var jump = inner.Body.OfType<OperationSyntaxItem>().First(o => o.Mnemonic == "j");
            // "shared" should be resolved to the parent scope label "outer.shared"
            Assert.AreEqual("outer.shared", jump.ResolvedLabel);
        }

        [TestMethod]
        public void Scope_Compiled_FlattensBody()
        {
            var src = @"
address $00700000
scope MyFunc {
    nop
    nop
}
";
            var pairs = Compile(src);
            Assert.AreEqual(2, pairs.Count);
            Assert.AreEqual(0x00700000u, pairs[0].Address);
            Assert.AreEqual(0x00700004u, pairs[1].Address);
        }

        [TestMethod]
        public void MultipleScopes_CanHaveSameLabelNames()
        {
            // Two scopes each define a label called "init" – they must be distinct
            var src = @"
address $00900000
scope FuncA {
    init:
    nop
}
scope FuncB {
    init:
    nop
}
";
            var items = Parse(src);
            var scopes = items.OfType<ScopeSyntaxItem>().ToList();
            Assert.AreEqual(2, scopes.Count);

            var labelA = scopes[0].Body.OfType<LabelSyntaxItem>().First();
            var labelB = scopes[1].Body.OfType<LabelSyntaxItem>().First();

            Assert.AreEqual("funca.init", labelA.FullName);
            Assert.AreEqual("funcb.init", labelB.FullName);
            Assert.AreNotEqual(labelA.FullName, labelB.FullName);
        }

        // -----------------------------------------------------------------------
        // Include directive
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Include_MissingFile_EmitsDiagnosticError()
        {
            // When the included file does not exist the preprocessor emits an error diagnostic.
            var parser = new CdsParser();
            parser.Parse("include \"sub.cds\"");
            var diags = parser.Diagnostics.ToList();
            Assert.IsTrue(diags.Any(d =>
                d.Severity == CdsDiagnosticSeverity.Error &&
                d.Message.Contains("sub.cds")));
        }

        // -----------------------------------------------------------------------
        // CdsFile helper (in-memory)
        // -----------------------------------------------------------------------

        [TestMethod]
        public void CdsFile_CompileSource_ReturnsExpectedPairs()
        {
            var file = new CdsFile("test.cds");
            var pairs = file.CompileSource("address $00100000\nhexcode $CAFEBABE");
            Assert.AreEqual(1, pairs.Count);
            Assert.AreEqual(0x00100000u, pairs[0].Address);
            Assert.AreEqual(0xCAFEBABEu, pairs[0].Word);
        }

        [TestMethod]
        public void CdsFile_ToCheatCode_FormatsCorrectly()
        {
            var pairs = new List<(uint, uint)> { (0x00200000, 0xDEADBEEF) };
            string output = CdsFile.ToCheatCode(pairs);
            StringAssert.Contains(output, "00200000 DEADBEEF");
        }

        // -----------------------------------------------------------------------
        // Full program smoke test
        // -----------------------------------------------------------------------

        [TestMethod]
        public void FullProgram_ParsesAndCompiles()
        {
            var src = @"
// CDS test program
address $00200000

scope Setup {
    // Load a value into t0
    setreg t0, $00000064
    start:
    nop
    nop
    j :start
}

address $00200020
hexcode $DEADBEEF
";
            var items = Parse(src);
            Assert.IsTrue(items.Count > 0);

            var pairs = CdsCompiler.Compile(items);
            // 2 (setreg) + 2 (nop) + 1 (j) + 1 (hexcode) = 6
            Assert.IsTrue(pairs.Count >= 5);
        }

        // -----------------------------------------------------------------------
        // P0: Diagnostics
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Diagnostics_UnresolvedLabel_EmitsWarning()
        {
            var parser = new CdsParser();
            parser.Parse("address $00100000\nbne $t0, $t1, :nonexistent");
            var diags = parser.Diagnostics.ToList();
            Assert.IsTrue(diags.Any(d =>
                d.Severity == CdsDiagnosticSeverity.Warning &&
                d.Message.Contains("nonexistent")));
        }

        [TestMethod]
        public void Diagnostics_ClearedBetweenParseCalls()
        {
            var parser = new CdsParser();
            parser.Parse("address $00100000\nbne $t0, $t1, :nonexistent");
            Assert.IsTrue(parser.Diagnostics.Count > 0);
            parser.Parse("address $00100000\nnop");
            Assert.AreEqual(0, parser.Diagnostics.Count);
        }

        [TestMethod]
        public void Diagnostics_BadInstruction_EmitsError()
        {
            var parser = new CdsParser();
            parser.Parse("address $00100000\nthis_is_not_an_instruction");
            var diags = parser.Diagnostics.ToList();
            Assert.IsTrue(diags.Any(d => d.Severity == CdsDiagnosticSeverity.Error));
        }

        [TestMethod]
        public void Diagnostics_ValidSource_HasNoDiagnostics()
        {
            var parser = new CdsParser();
            parser.Parse("address $00100000\nnop\nnop");
            Assert.AreEqual(0, parser.Diagnostics.Count);
        }

        [TestMethod]
        public void Diagnostics_LineNumber_IsCorrect()
        {
            var parser = new CdsParser();
            parser.Parse("address $00100000\nnop\nbne $t0, $t1, :missing");
            var diags = parser.Diagnostics.ToList();
            var w = diags.FirstOrDefault(d => d.Message.Contains("missing"));
            Assert.IsNotNull(w);
            Assert.AreEqual(3, w.LineNumber);
        }

        [TestMethod]
        public void Diagnostics_ColumnRange_IsPopulated()
        {
            var parser = new CdsParser();
            parser.Parse("address $00100000\nbne $t0, $t1, :missing");
            var diag = parser.Diagnostics.First(d => d.Message.Contains("missing"));
            Assert.IsTrue(diag.ColumnNumber > 0);
            Assert.IsTrue(diag.EndColumnNumber > diag.ColumnNumber);
        }

        [TestMethod]
        public void Diagnostics_MemDirective_WarnsAboutT9Clobber()
        {
            var parser = new CdsParser();
            parser.Parse("address $00100000\nmem[0x0010] t0 = 0x05");
            Assert.IsTrue(parser.Diagnostics.Any(d =>
                d.Severity == CdsDiagnosticSeverity.Warning &&
                d.Message.Contains("$t9")));
        }

        [TestMethod]
        public void Diagnostics_PointerDirective_WarnsAboutScratchRegisterClobber()
        {
            var parser = new CdsParser();
            parser.Parse("address $00100000\npointer $20347A50 + $0100 + $0048 = $00000063");
            Assert.IsTrue(parser.Diagnostics.Any(d =>
                d.Severity == CdsDiagnosticSeverity.Warning &&
                d.Message.Contains("$t8") &&
                d.Message.Contains("$t9")));
        }

        [TestMethod]
        public void Diagnostics_IfImmediateCondition_WarnsAboutScratchRegisterClobber()
        {
            var parser = new CdsParser();
            parser.Parse("address $00100000\nif $t0 == 0x05\n  nop\nendif");
            Assert.IsTrue(parser.Diagnostics.Any(d =>
                d.Severity == CdsDiagnosticSeverity.Warning &&
                d.Message.Contains("$t9")));
        }

        [TestMethod]
        public void Diagnostics_EmptyDelaySlot_EmitsWarning()
        {
            var parser = new CdsParser();
            parser.Parse("address $00100000\nj :target\ntarget:");
            Assert.IsTrue(parser.Diagnostics.Any(d =>
                d.Severity == CdsDiagnosticSeverity.Warning &&
                d.Message.Contains("delay slot")));
        }

        [TestMethod]
        public void Diagnostics_UnterminatedIf_EmitsError()
        {
            var parser = new CdsParser();
            parser.Parse("if $t0 == $zero\n  nop");
            Assert.IsTrue(parser.Diagnostics.Any(d =>
                d.Severity == CdsDiagnosticSeverity.Error &&
                d.Message.Contains("unterminated if block")));
        }

        [TestMethod]
        public void Diagnostics_MalformedLogDirective_EmitsError()
        {
            var parser = new CdsParser();
            parser.Parse("address $00100000\nlog hello");
            Assert.IsTrue(parser.Diagnostics.Any(d =>
                d.Severity == CdsDiagnosticSeverity.Error &&
                d.Message.Contains("malformed log/print directive")));
        }

        [TestMethod]
        public void Diagnostics_MalformedPointerDirective_EmitsError()
        {
            var parser = new CdsParser();
            parser.Parse("address $00100000\npointer $20347A50 = $00000063");
            Assert.IsTrue(parser.Diagnostics.Any(d =>
                d.Severity == CdsDiagnosticSeverity.Error &&
                d.Message.Contains("malformed pointer directive")));
        }

        [TestMethod]
        public void Diagnostics_UnterminatedTimerBlock_EmitsError()
        {
            var parser = new CdsParser();
            parser.Parse("timer 60\n  freeze word $00123456 = 00000001");
            Assert.IsTrue(parser.Diagnostics.Any(d =>
                d.Severity == CdsDiagnosticSeverity.Error &&
                d.Message.Contains("unterminated timer block")));
        }

        [TestMethod]
        public void Diagnostics_UnexpectedEndtimer_EmitsError()
        {
            var parser = new CdsParser();
            parser.Parse("endtimer");
            Assert.IsTrue(parser.Diagnostics.Any(d =>
                d.Severity == CdsDiagnosticSeverity.Error &&
                d.Message.Contains("unexpected 'endtimer'")));
        }

        [TestMethod]
        public void Diagnostics_TimerZeroInterval_EmitsWarning()
        {
            var parser = new CdsParser();
            parser.Parse("timer 0\n  freeze word $00123456 = 00000001\nendtimer");
            Assert.IsTrue(parser.Diagnostics.Any(d =>
                d.Severity == CdsDiagnosticSeverity.Warning &&
                d.Message.Contains("timer interval 0")));
        }

        // -----------------------------------------------------------------------
        // P0: const / alias directives (via CdsPreprocessor)
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Preprocessor_Const_SubstitutedInInstruction()
        {
            var src = @"
const MAXHP = $00000064
address $00100000
setreg t0, MAXHP
";
            var pairs = Compile(src);
            // setreg emits 2 words; value 0x00000064 → lui+addiu
            Assert.AreEqual(2, pairs.Count);
            // addiu $t0, $t0, 0x0064 should be present
            Assert.IsTrue(pairs.Any(p => (p.Word & 0xFFFF) == 0x0064));
        }

        [TestMethod]
        public void Preprocessor_Alias_SubstitutedInInstruction()
        {
            var src = @"
alias health = $t0
address $00100000
addiu health, health, $0001
";
            var pairs = Compile(src);
            Assert.AreEqual(1, pairs.Count);
            // addiu $t0, $t0, 1  →  opcode 0x09 (addiu), rs=$t0(8), rt=$t0(8), imm=1
            // 0x09_08_08_0001 = 0x25080001
            Assert.AreEqual(0x25080001u, pairs[0].Word);
        }

        [TestMethod]
        public void Preprocessor_Alias_MultipleRegisters()
        {
            var src = @"
alias player = $s0
alias count  = $t1
address $00100000
add player, player, count
";
            var pairs = Compile(src);
            Assert.AreEqual(1, pairs.Count);
            // add $s0, $s0, $t1  →  funct=0x20, rs=$s0(16), rt=$t1(9), rd=$s0(16)
            // 0x00_0A_90_20... let's just verify it's non-zero and non-0
            Assert.AreNotEqual(0u, pairs[0].Word);
        }

        [TestMethod]
        public void Preprocessor_ConstAndAlias_UsedTogether()
        {
            var src = @"
const BASE = $20100000
alias tmp = $t9
address $00100000
setreg tmp, BASE
";
            var pairs = Compile(src);
            // setreg should produce 2 words
            Assert.AreEqual(2, pairs.Count);
        }

        // -----------------------------------------------------------------------
        // P0: Sized memory operations (mem.b / mem.h / mem.w / mem.d)
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Mem_DefaultSize_IsWord()
        {
            var items = Parse("address $00100000\nmem[0x0100] s0 = 0x63");
            var mem = FindItem<MemorySyntaxItem>(items);
            Assert.AreEqual(CdsMemorySize.Word, mem.MemorySize);
        }

        [TestMethod]
        public void Mem_Byte_Size_HasSbInstruction()
        {
            var items = Parse("address $00100000\nmem.b[0x0100] s0 = 0x63");
            var mem = FindItem<MemorySyntaxItem>(items);
            Assert.AreEqual(CdsMemorySize.Byte, mem.MemorySize);
            // store op should use sb
            var storeOp = mem.Operations.Last();
            Assert.AreEqual("sb", storeOp.Mnemonic, StringComparer.OrdinalIgnoreCase);
        }

        [TestMethod]
        public void Mem_Half_Size_HasShInstruction()
        {
            var items = Parse("address $00100000\nmem.h[0x0100] s0 = 0x03E7");
            var mem = FindItem<MemorySyntaxItem>(items);
            Assert.AreEqual(CdsMemorySize.Half, mem.MemorySize);
            var storeOp = mem.Operations.Last();
            Assert.AreEqual("sh", storeOp.Mnemonic, StringComparer.OrdinalIgnoreCase);
        }

        [TestMethod]
        public void Mem_Word_ExplicitSuffix_SetsWordSize()
        {
            var items = Parse("address $00100000\nmem.w[0x0100] s0 = 0x01");
            var mem = FindItem<MemorySyntaxItem>(items);
            Assert.AreEqual(CdsMemorySize.Word, mem.MemorySize);
        }

        [TestMethod]
        public void Mem_Doubleword_Size_HasSdInstruction()
        {
            var items = Parse("address $00100000\nmem.d[0x0100] s0 = 0x0");
            var mem = FindItem<MemorySyntaxItem>(items);
            Assert.AreEqual(CdsMemorySize.Doubleword, mem.MemorySize);
            var storeOp = mem.Operations.Last();
            Assert.AreEqual("sd", storeOp.Mnemonic, StringComparer.OrdinalIgnoreCase);
        }

        [TestMethod]
        public void Mem_Byte_Assign_EmitsTwoOps()
        {
            var items = Parse("address $00100000\nmem.b[0x0100] s0 = 0x01");
            var mem = FindItem<MemorySyntaxItem>(items);
            Assert.AreEqual(2, mem.Operations.Count);  // addiu + sb
        }

        [TestMethod]
        public void Mem_Half_AddAssign_EmitsThreeOps()
        {
            var items = Parse("address $00100000\nmem.h[0x0100] s0 += 0x01");
            var mem = FindItem<MemorySyntaxItem>(items);
            Assert.AreEqual(3, mem.Operations.Count);  // lh + addiu + sh
            Assert.AreEqual("lh", mem.Operations[0].Mnemonic, StringComparer.OrdinalIgnoreCase);
        }

        [TestMethod]
        public void Mem_Byte_Compiled_CorrectAddressRange()
        {
            var pairs = Compile("address $00100000\nmem.b[0x0100] s0 = 0x01");
            // mem assign = 2 instructions at +0 and +4
            Assert.AreEqual(2, pairs.Count);
            Assert.AreEqual(0x00100000u, pairs[0].Address);
            Assert.AreEqual(0x00100004u, pairs[1].Address);
        }

        // -----------------------------------------------------------------------
        // P0: Typed memory loads (load[.b/.h/.w/.d])
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Load_DefaultSize_IsWord()
        {
            var items = Parse("address $00100000\nload t0, mem[0x0100] s0");
            var load = FindItem<LoadMemorySyntaxItem>(items);
            Assert.AreEqual(CdsMemorySize.Word, load.MemorySize);
            Assert.AreEqual("lw", load.Operation.Mnemonic, StringComparer.OrdinalIgnoreCase);
        }

        [TestMethod]
        public void Load_Half_Size_HasLhInstruction()
        {
            var items = Parse("address $00100000\nload.h t0, mem[0x0100] s0");
            var load = FindItem<LoadMemorySyntaxItem>(items);
            Assert.AreEqual(CdsMemorySize.Half, load.MemorySize);
            Assert.AreEqual("lh", load.Operation.Mnemonic, StringComparer.OrdinalIgnoreCase);
            Assert.AreEqual("t0", load.TargetRegister);
            Assert.AreEqual("s0", load.BaseRegister);
        }

        [TestMethod]
        public void Load_Doubleword_Size_HasLdInstruction()
        {
            var items = Parse("address $00100000\nload.d t1, mem[0x0120] s2");
            var load = FindItem<LoadMemorySyntaxItem>(items);
            Assert.AreEqual(CdsMemorySize.Doubleword, load.MemorySize);
            Assert.AreEqual("ld", load.Operation.Mnemonic, StringComparer.OrdinalIgnoreCase);
            Assert.AreEqual(0x0120u, load.MemoryOffset);
        }

        [TestMethod]
        public void Load_CompileAdvancesAddresses()
        {
            var pairs = Compile("address $00100000\nload.w t0, mem[0x0100] s0\nhexcode $DEADBEEF");
            Assert.AreEqual(2, pairs.Count);
            Assert.AreEqual(0x00100000u, pairs[0].Address);
            Assert.AreEqual(0x00100004u, pairs[1].Address);
            Assert.AreEqual(0xDEADBEEFu, pairs[1].Word);
        }

        // -----------------------------------------------------------------------
        // P0: Typed memory stores (store[.b/.h/.w/.d])
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Store_DefaultSize_IsWord()
        {
            var items = Parse("address $00100000\nstore t0, mem[0x0100] s0");
            var store = FindItem<StoreMemorySyntaxItem>(items);
            Assert.AreEqual(CdsMemorySize.Word, store.MemorySize);
            Assert.AreEqual("sw", store.Operation.Mnemonic, StringComparer.OrdinalIgnoreCase);
        }

        [TestMethod]
        public void Store_Byte_Size_HasSbInstruction()
        {
            var items = Parse("address $00100000\nstore.b t0, mem[0x0100] s0");
            var store = FindItem<StoreMemorySyntaxItem>(items);
            Assert.AreEqual(CdsMemorySize.Byte, store.MemorySize);
            Assert.AreEqual("sb", store.Operation.Mnemonic, StringComparer.OrdinalIgnoreCase);
            Assert.AreEqual("t0", store.SourceRegister);
            Assert.AreEqual("s0", store.BaseRegister);
        }

        [TestMethod]
        public void Store_Doubleword_Size_HasSdInstruction()
        {
            var items = Parse("address $00100000\nstore.d t1, mem[0x0120] s2");
            var store = FindItem<StoreMemorySyntaxItem>(items);
            Assert.AreEqual(CdsMemorySize.Doubleword, store.MemorySize);
            Assert.AreEqual("sd", store.Operation.Mnemonic, StringComparer.OrdinalIgnoreCase);
            Assert.AreEqual(0x0120u, store.MemoryOffset);
        }

        [TestMethod]
        public void Store_CompileAdvancesAddresses()
        {
            var pairs = Compile("address $00100000\nstore.h t0, mem[0x0100] s0\nhexcode $DEADBEEF");
            Assert.AreEqual(2, pairs.Count);
            Assert.AreEqual(0x00100000u, pairs[0].Address);
            Assert.AreEqual(0x00100004u, pairs[1].Address);
            Assert.AreEqual(0xDEADBEEFu, pairs[1].Word);
        }

        // -----------------------------------------------------------------------
        // P1: pointer directive
        // -----------------------------------------------------------------------

        [TestMethod]
        public void PointerDirective_IsParsedCorrectly()
        {
            var items = Parse("address $00100000\npointer $20347A50 + $0100 + $0048 = $000003E7");
            var pointer = FindItem<PointerSyntaxItem>(items);
            Assert.AreEqual(0x00100000u, pointer.Address);
            Assert.AreEqual(0x20347A50u, pointer.BaseAddress);
            CollectionAssert.AreEqual(new ushort[] { 0x0100, 0x0048 }, pointer.Offsets);
            Assert.AreEqual(0x000003E7u, pointer.Value);
        }

        [TestMethod]
        public void PointerDirective_EmitsExpectedInstructionCount()
        {
            var items = Parse("address $00100000\npointer $20347A50 + $0100 + $0048 = $00000063");
            var pointer = FindItem<PointerSyntaxItem>(items);
            Assert.AreEqual(7, pointer.Operations.Count);
            Assert.AreEqual("lw", pointer.Operations[2].Mnemonic, StringComparer.OrdinalIgnoreCase);
            Assert.AreEqual("lw", pointer.Operations[3].Mnemonic, StringComparer.OrdinalIgnoreCase);
            Assert.AreEqual("sw", pointer.Operations.Last().Mnemonic, StringComparer.OrdinalIgnoreCase);
        }

        [TestMethod]
        public void PointerDirective_CompileAdvancesAddresses()
        {
            var pairs = Compile("address $00100000\npointer $20347A50 + $0100 + $0048 = $00000063\nhexcode $DEADBEEF");
            Assert.AreEqual(8, pairs.Count);
            Assert.AreEqual(0x00100000u, pairs[0].Address);
            Assert.AreEqual(0x00100018u, pairs[6].Address);
            Assert.AreEqual(0x0010001Cu, pairs[7].Address);
            Assert.AreEqual(0xDEADBEEFu, pairs[7].Word);
        }

        // -----------------------------------------------------------------------
        // P1: assert sidecar
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Assert_CreatesAssertSyntaxItem()
        {
            var items = Parse("assert mem[$20010000] == $3C010020");
            var a = FindItem<AssertSyntaxItem>(items);
            Assert.AreEqual(0x20010000u, a.Address);
            Assert.AreEqual("==", a.Operator);
            Assert.AreEqual(0x3C010020u, a.ExpectedValue);
        }

        [TestMethod]
        public void Assert_NotEqual_ParsesOperator()
        {
            var items = Parse("assert mem[$20010000] != $00000000");
            var a = FindItem<AssertSyntaxItem>(items);
            Assert.AreEqual("!=", a.Operator);
        }

        [TestMethod]
        public void Assert_DoesNotAdvanceAddress()
        {
            // assert is a sidecar — no code emitted
            var items = Parse("address $00100000\nassert mem[$20010000] == $DEADBEEF\nhexcode $12345678");
            var hex = FindItem<HexCodeSyntaxItem>(items);
            Assert.AreEqual(0x00100000u, hex.Address);  // unchanged
        }

        [TestMethod]
        public void Assert_NotInCompilerOutput()
        {
            var pairs = Compile("address $00100000\nassert mem[$20010000] == $DEADBEEF\nhexcode $CAFEBABE");
            Assert.AreEqual(1, pairs.Count);
            Assert.AreEqual(0xCAFEBABEu, pairs[0].Word);
        }

        // -----------------------------------------------------------------------
        // P1: breakpoint sidecar
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Breakpoint_CreatesBreakpointSyntaxItem()
        {
            var items = Parse("address $20010000\nbreakpoint");
            var bp = FindItem<BreakpointSyntaxItem>(items);
            Assert.AreEqual(0x20010000u, bp.Address);
        }

        [TestMethod]
        public void Breakpoint_DoesNotAdvanceAddress()
        {
            var items = Parse("address $00100000\nbreakpoint\nhexcode $12345678");
            var hex = FindItem<HexCodeSyntaxItem>(items);
            Assert.AreEqual(0x00100000u, hex.Address);
        }

        [TestMethod]
        public void Breakpoint_AfterNop_RecordsCorrectAddress()
        {
            var items = Parse("address $00100000\nnop\nbreakpoint");
            var bp = FindItem<BreakpointSyntaxItem>(items);
            Assert.AreEqual(0x00100004u, bp.Address);
        }

        [TestMethod]
        public void Breakpoint_NotInCompilerOutput()
        {
            var pairs = Compile("address $00100000\nbreakpoint\nhexcode $DEADBEEF");
            Assert.AreEqual(1, pairs.Count);
        }

        // -----------------------------------------------------------------------
        // P1: watchpoint sidecar
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Watchpoint_Write_CreatesItem()
        {
            var items = Parse("watchpoint write $20347A50 4");
            var wp = FindItem<WatchpointSyntaxItem>(items);
            Assert.AreEqual(0x20347A50u, wp.Address);
            Assert.AreEqual(4u, wp.Size);
            Assert.AreEqual(WatchpointAccessType.Write, wp.AccessType);
        }

        [TestMethod]
        public void Watchpoint_Read_CreatesItem()
        {
            var items = Parse("watchpoint read $20347A50 2");
            var wp = FindItem<WatchpointSyntaxItem>(items);
            Assert.AreEqual(WatchpointAccessType.Read, wp.AccessType);
        }

        [TestMethod]
        public void Watchpoint_ReadWrite_CreatesItem()
        {
            var items = Parse("watchpoint rw $20347A50 1");
            var wp = FindItem<WatchpointSyntaxItem>(items);
            Assert.AreEqual(WatchpointAccessType.ReadWrite, wp.AccessType);
        }

        [TestMethod]
        public void Watchpoint_ReadWrite_LongForm()
        {
            var items = Parse("watchpoint readwrite $20347A50 4");
            var wp = FindItem<WatchpointSyntaxItem>(items);
            Assert.AreEqual(WatchpointAccessType.ReadWrite, wp.AccessType);
        }

        [TestMethod]
        public void Watchpoint_DoesNotAdvanceAddress()
        {
            var items = Parse("address $00100000\nwatchpoint write $20347A50 4\nhexcode $12345678");
            var hex = FindItem<HexCodeSyntaxItem>(items);
            Assert.AreEqual(0x00100000u, hex.Address);
        }

        // -----------------------------------------------------------------------
        // P2: CdsPreprocessor — if / else / endif
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Preprocessor_If_WithoutElse_BranchesToEnd()
        {
            var src = @"
address $00100000
if $t0 == $zero
  nop
endif
nop
";
            var pairs = Compile(src);
            // Expected: bne $t0,$zero,:end (1) + nop delay (1) + nop body (1) + nop after (1) = 4
            Assert.AreEqual(4, pairs.Count);
        }

        [TestMethod]
        public void Preprocessor_If_WithElse_EmitsJumpAndBothBodies()
        {
            var src = @"
address $00100000
if $t0 == $zero
  nop
else
  nop
  nop
endif
";
            // bne (1) + nop ds (1) + then nop (1) + j :end (1) + nop ds (1) + else nop (1) + else nop (1) = 7
            var pairs = Compile(src);
            Assert.AreEqual(7, pairs.Count);
        }

        [TestMethod]
        public void Preprocessor_If_NotEqual_Condition()
        {
            var src = @"
address $00100000
if $t0 != $zero
  nop
endif
";
            var pairs = Compile(src);
            Assert.AreEqual(3, pairs.Count);  // beq + nop + nop body
        }

        [TestMethod]
        public void Preprocessor_If_ImmediateCondition_UsesSetregThenBranch()
        {
            var src = @"
address $00100000
if $t0 == 0x05
  nop
endif
";
            var pairs = Compile(src);
            // setreg t9, $00000005 (2) + bne (1) + nop delay (1) + nop body (1) = 5
            Assert.AreEqual(5, pairs.Count);
        }

        [TestMethod]
        public void Preprocessor_NestedIf_Works()
        {
            var src = @"
address $00100000
if $t0 != $zero
  if $t1 != $zero
    nop
  endif
endif
";
            // outer beq (1) + outer nop ds (1) + inner beq (1) + inner nop ds (1) + nop body (1) = 5
            var pairs = Compile(src);
            Assert.AreEqual(5, pairs.Count);
        }

        [TestMethod]
        public void Preprocessor_If_LabelsAreUnique_ForMultipleBlocks()
        {
            var src = @"
address $00100000
if $t0 == $zero
  nop
endif
if $t1 == $zero
  nop
endif
";
            // Should parse without unresolved label errors
            var parser = new CdsParser();
            parser.Parse(src.Trim());
            var diags = parser.Diagnostics.ToList();
            Assert.IsFalse(diags.Any(d => d.Severity == CdsDiagnosticSeverity.Error));
        }

        // -----------------------------------------------------------------------
        // P2: CdsPreprocessor — while / endwhile
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Preprocessor_While_EmitsCorrectStructure()
        {
            var src = @"
address $00100000
while $t0 != $zero
  nop
endwhile
";
            // top_label(0) + beq(1) + nop ds(1) + nop body(1) + j :top(1) + nop ds(1) = 5
            var pairs = Compile(src);
            Assert.AreEqual(5, pairs.Count);
        }

        [TestMethod]
        public void Preprocessor_While_WithImmediateCondition()
        {
            var src = @"
address $00100000
while $t0 != 0x0A
  nop
endwhile
";
            // setreg (2) + beq (1) + nop ds (1) + nop body (1) + j (1) + nop ds (1) = 7
            var pairs = Compile(src);
            Assert.AreEqual(7, pairs.Count);
        }

        // -----------------------------------------------------------------------
        // P2: CdsPreprocessor — relational operators (<, >, <=, >=, unsigned)
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Preprocessor_If_LessThan_Register_EmitsSltThenBeq()
        {
            var src = @"
address $00100000
if $t0 < $t1
  nop
endif
";
            var pairs = Compile(src);
            // slt $t8,lhs,rhs (1) + beq $t8,$zero,:end (1) + nop ds (1) + nop body (1) = 4
            Assert.AreEqual(4, pairs.Count);
            // slt t8, $t0, $t1 = opcode SPECIAL(0), rs=t0(8)=01000, rt=t1(9)=01001, rd=t8(24)=11000, shamt=0, funct=SLT(42=0x2A)
            // 000000 01000 01001 11000 00000 101010
            // = 0x0109C02A
            Assert.AreEqual(0x0109C02Au, pairs[0].Word, "slt $t8, $t0, $t1");
            // beq $t8,$zero,:end (at +4 = next word) — offset = 1 (skip nop delay to body, then body is 1 more)
            // Actually offset points past the branch: body is pairs[2] and pairs[3], end is pairs[4] (doesn't exist)
            // beq t8,zero,offset: address 0x00100008, target 0x00100010 (past the nop body), delta = 0x8/4 = 2 words
            Assert.AreEqual(0x13000002u, pairs[1].Word, "beq $t8, $zero, :end (offset +2)");
        }

        [TestMethod]
        public void Preprocessor_If_GreaterThan_Register_EmitsSwappedSlt()
        {
            var src = @"
address $00100000
if $t0 > $t1
  nop
endif
";
            var pairs = Compile(src);
            // slt $t8,$t1,$t0 (swapped) + beq $t8,$zero,:end + nop + nop body = 4
            Assert.AreEqual(4, pairs.Count);
            // slt t8, $t1, $t0 = rs=t1(9)=01001, rt=t0(8)=01000
            // 000000 01001 01000 11000 00000 101010
            // = 0x0128C02A
            Assert.AreEqual(0x0128C02Au, pairs[0].Word, "slt $t8, $t1, $t0");
        }

        [TestMethod]
        public void Preprocessor_If_LessOrEqual_Register_EmitsSwappedSltThenBne()
        {
            var src = @"
address $00100000
if $t0 <= $t1
  nop
endif
";
            var pairs = Compile(src);
            // slt $t8,$t1,$t0 (swapped) + bne $t8,$zero,:end + nop + nop body = 4
            Assert.AreEqual(4, pairs.Count);
            // bne $t8, $zero, offset — opcode BNE(5=000101), rs=t8(24)=11000, rt=zero(0)=00000
            // (5<<26)|(24<<21)|(0<<16)|2 = 0x14000000|0x03000000|0|2 = 0x17000002
            Assert.AreEqual(0x17000002u, pairs[1].Word, "bne $t8, $zero, :end");
        }

        [TestMethod]
        public void Preprocessor_If_GreaterOrEqual_Register_EmitsSltThenBne()
        {
            var src = @"
address $00100000
if $t0 >= $t1
  nop
endif
";
            var pairs = Compile(src);
            // slt $t8,$t0,$t1 + bne $t8,$zero,:end + nop + nop body = 4
            Assert.AreEqual(4, pairs.Count);
            Assert.AreEqual(0x0109C02Au, pairs[0].Word, "slt $t8, $t0, $t1");
            Assert.AreEqual(0x17000002u, pairs[1].Word, "bne $t8, $zero, :end");
        }

        [TestMethod]
        public void Preprocessor_If_LessThan_Zero_EmitsBgez()
        {
            var src = @"
address $00100000
if $t0 < 0
  nop
endif
";
            var pairs = Compile(src);
            // bgez $t0, :end (1) + nop ds (1) + nop body (1) = 3
            Assert.AreEqual(3, pairs.Count);
            // bgez $t0, offset: opcode REGIMM(1=000001), rs=t0(8), rt=BGEZ(1)
            // (1<<26)|(8<<21)|(1<<16)|2 = 0x04000000|0x01000000|0x00010000|2 = 0x05010002
            Assert.AreEqual(0x05010002u, pairs[0].Word, "bgez $t0, :end");
        }

        [TestMethod]
        public void Preprocessor_If_GreaterThan_Zero_EmitsBlez()
        {
            var src = @"
address $00100000
if $t0 > 0
  nop
endif
";
            var pairs = Compile(src);
            Assert.AreEqual(3, pairs.Count);
            // blez $t0, offset: opcode BLEZ(6=000110), rs=t0(8)=01000, rt=0
            // 000110 01000 00000 00000000 00000010 = 0x19000002
            Assert.AreEqual(0x19000002u, pairs[0].Word, "blez $t0, :end");
        }

        [TestMethod]
        public void Preprocessor_If_LessThan_Immediate_MaterialisesViaT9()
        {
            var src = @"
address $00100000
if $t0 < 0x64
  nop
endif
";
            var pairs = Compile(src);
            // setreg t9,$00000064 (2: lui+ori) + slt (1) + beq (1) + nop (1) + body nop (1) = 6
            Assert.AreEqual(6, pairs.Count);
            // slt $t8, $t0, $t9  = rs=t0(8), rt=t9(25)=11001, rd=t8(24)=11000
            // 000000 01000 11001 11000 00000 101010
            // = 0x0119C02A
            Assert.AreEqual(0x0119C02Au, pairs[2].Word, "slt $t8, $t0, $t9");
        }

        [TestMethod]
        public void Preprocessor_If_UnsignedLessThan_UsesSltu()
        {
            var src = @"
address $00100000
if $t0 <u $t1
  nop
endif
";
            var pairs = Compile(src);
            Assert.AreEqual(4, pairs.Count);
            // sltu $t8, $t0, $t1: funct SLTU = 0x2B (43)
            // 000000 01000 01001 11000 00000 101011
            // = 0x0109C02B
            Assert.AreEqual(0x0109C02Bu, pairs[0].Word, "sltu $t8, $t0, $t1");
        }

        [TestMethod]
        public void Preprocessor_While_LessThan_Register_CorrectStructure()
        {
            var src = @"
address $00100000
while $t0 < $t1
  nop
endwhile
";
            var pairs = Compile(src);
            // top_label(0) + slt(1) + beq(1) + nop ds(1) + nop body(1) + j :top(1) + nop ds(1) = 6
            Assert.AreEqual(6, pairs.Count);
            Assert.AreEqual(0x0109C02Au, pairs[0].Word, "slt $t8, $t0, $t1");
        }

        // -----------------------------------------------------------------------
        // P2: CdsPreprocessor — repeat / endrepeat
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Preprocessor_Repeat_EmitsCounterAndLoop()
        {
            var src = @"
address $00100000
repeat 3
  nop
endrepeat
";
            // addiu $t9,$zero,3 (1) + top_label(0) + nop body(1) + addiu t9,-1 (1) + bne(1) + nop ds(1) = 5
            var pairs = Compile(src);
            Assert.AreEqual(5, pairs.Count);
        }

        [TestMethod]
        public void Preprocessor_Repeat_CounterIsSetCorrectly()
        {
            var src = @"
address $00100000
repeat 5
  nop
endrepeat
";
            var pairs = Compile(src);
            // addiu $t9, $zero, $0005 = 0x24190005
            // opcode=9(001001), rs=$zero(0)=00000, rt=$t9(25)=11001, imm=5=0000000000000101
            // 001001 00000 11001 0000000000000101 = 0x24190005
            Assert.AreEqual(0x24190005u, pairs[0].Word);
        }

        [TestMethod]
        public void Preprocessor_Repeat_CountOutOfRange_SkipsBlock()
        {
            // Count 0 and count > 32767 (0x7FFF) are invalid — emits a comment and skips the body
            var src0 = @"
address $00100000
repeat 0
  nop
endrepeat
";
            var pairsZero = Compile(src0);
            Assert.AreEqual(0, pairsZero.Count, "repeat 0 should emit no code");

            var srcOver = @"
address $00100000
repeat 32768
  nop
endrepeat
";
            var pairsOver = Compile(srcOver);
            Assert.AreEqual(0, pairsOver.Count, "repeat 32768 should emit no code (overflow)");
        }

        // -----------------------------------------------------------------------
        // P2: CdsPreprocessor — macro / endmacro
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Preprocessor_Macro_ExpandsAtCallSite()
        {
            var src = @"
macro Nop2()
  nop
  nop
endmacro
address $00100000
Nop2()
";
            var pairs = Compile(src);
            Assert.AreEqual(2, pairs.Count);
        }

        [TestMethod]
        public void Preprocessor_Macro_ParametersAreSubstituted()
        {
            var src = @"
macro LoadVal(dst, val)
  setreg dst, val
endmacro
address $00100000
LoadVal(t0, $00000064)
";
            var pairs = Compile(src);
            // setreg t0, $00000064 = 2 instructions
            Assert.AreEqual(2, pairs.Count);
            // lui $t0, 0 = 0x3C080000, addiu $t0,$t0,100 = 0x25080064
            Assert.AreEqual(0x3C080000u, pairs[0].Word);
            Assert.AreEqual(0x25080064u, pairs[1].Word);
        }

        [TestMethod]
        public void Preprocessor_Macro_WrongArgCount_EmitsDiagnostic()
        {
            var src = @"
macro Foo(a, b)
  nop
endmacro
address $00100000
Foo(t0)
";
            var parser = new CdsParser();
            parser.Parse(src.Trim());
            var diags = parser.Diagnostics.ToList();
            Assert.IsTrue(diags.Any(d =>
                d.Severity == CdsDiagnosticSeverity.Error &&
                d.Message.Contains("Foo")));
        }

        // -----------------------------------------------------------------------
        // CdsCompiler.GetSidecarCommands
        // -----------------------------------------------------------------------

        [TestMethod]
        public void GetSidecarCommands_ReturnsSidecarItems()
        {
            var items = Parse(
                "address $00100000\nassert mem[$20010000] == $DEADBEEF\nbreakpoint\nwatchpoint write $20347A50 4");
            var sidecars = CdsCompiler.GetSidecarCommands(items);
            Assert.AreEqual(3, sidecars.Count);
            Assert.IsInstanceOfType(sidecars[0], typeof(AssertSyntaxItem));
            Assert.IsInstanceOfType(sidecars[1], typeof(BreakpointSyntaxItem));
            Assert.IsInstanceOfType(sidecars[2], typeof(WatchpointSyntaxItem));
        }

        [TestMethod]
        public void LogDirective_IsParsedAsSidecar()
        {
            var items = Parse("address $00100000\nlog \"Hello from CDS\"");
            var log = FindItem<LogSyntaxItem>(items);
            Assert.AreEqual(0x00100000u, log.Address);
            Assert.AreEqual("log", log.Directive);
            Assert.AreEqual("Hello from CDS", log.MessageText);
        }

        [TestMethod]
        public void PrintDirective_IsParsedAsSidecar()
        {
            var items = Parse("address $00100000\nnop\nprint \"after nop\"");
            var log = FindItem<LogSyntaxItem>(items);
            Assert.AreEqual(0x00100004u, log.Address);
            Assert.AreEqual("print", log.Directive);
            Assert.AreEqual("after nop", log.MessageText);
        }

        [TestMethod]
        public void GetSidecarCommands_IncludesLogSidecarsInSourceOrder()
        {
            var items = Parse(
                "address $00100000\nlog \"one\"\nassert mem[$20010000] == $DEADBEEF\nbreakpoint\nprint \"two\"\nwatchpoint write $20347A50 4");
            var sidecars = CdsCompiler.GetSidecarCommands(items);
            Assert.AreEqual(5, sidecars.Count);
            Assert.IsInstanceOfType(sidecars[0], typeof(LogSyntaxItem));
            Assert.IsInstanceOfType(sidecars[1], typeof(AssertSyntaxItem));
            Assert.IsInstanceOfType(sidecars[2], typeof(BreakpointSyntaxItem));
            Assert.IsInstanceOfType(sidecars[3], typeof(LogSyntaxItem));
            Assert.IsInstanceOfType(sidecars[4], typeof(WatchpointSyntaxItem));
            Assert.AreEqual("one", ((LogSyntaxItem)sidecars[0]).MessageText);
            Assert.AreEqual("two", ((LogSyntaxItem)sidecars[3]).MessageText);
        }

        [TestMethod]
        public void GetSidecarCommands_ExcludesNonSidecarItems()
        {
            var items = Parse("address $00100000\nnop\nhexcode $DEADBEEF");
            var sidecars = CdsCompiler.GetSidecarCommands(items);
            Assert.AreEqual(0, sidecars.Count);
        }

        // -----------------------------------------------------------------------
        // P1: freeze directive
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Freeze_Byte_ParsedCorrectly()
        {
            var items = Parse("freeze byte $20347A50 = 0x63");
            var fr = FindItem<FreezeSyntaxItem>(items);
            Assert.AreEqual(CdsFreezeType.Byte, fr.FreezeType);
            Assert.AreEqual(0x20347A50u, fr.Address);
            Assert.AreEqual(0x63u, fr.Value);
        }

        [TestMethod]
        public void Freeze_Half_ParsedCorrectly()
        {
            var items = Parse("freeze half $20347A52 = 0x03E7");
            var fr = FindItem<FreezeSyntaxItem>(items);
            Assert.AreEqual(CdsFreezeType.Half, fr.FreezeType);
            Assert.AreEqual(0x20347A52u, fr.Address);
            Assert.AreEqual(0x03E7u, fr.Value);
        }

        [TestMethod]
        public void Freeze_Word_ParsedCorrectly()
        {
            var items = Parse("freeze word $20347A54 = FFFFFFFF");
            var fr = FindItem<FreezeSyntaxItem>(items);
            Assert.AreEqual(CdsFreezeType.Word, fr.FreezeType);
            Assert.AreEqual(0x20347A54u, fr.Address);
            Assert.AreEqual(0xFFFFFFFFu, fr.Value);
        }

        [TestMethod]
        public void Freeze_DoesNotEmitCodeWords()
        {
            var pairs = Compile("address $00100000\nfreeze byte $20347A50 = 0x63");
            Assert.AreEqual(0, pairs.Count);
        }

        [TestMethod]
        public void GetCheatTableEntries_FreezeByteEntry()
        {
            var items = Parse("freeze byte $20347A50 = 0x63");
            var entries = CdsCompiler.GetCheatTableEntries(items);
            Assert.AreEqual(1, entries.Count);
            // TypeAddr: type=0x00 (byte), addr lower 24 bits = 0x347A50
            Assert.AreEqual(0x00347A50u, entries[0].TypeAddr);
            Assert.AreEqual(0x63u, entries[0].Value);
        }

        [TestMethod]
        public void GetCheatTableEntries_FreezeHalfEntry()
        {
            var items = Parse("freeze half $20347A52 = 03E7");
            var entries = CdsCompiler.GetCheatTableEntries(items);
            Assert.AreEqual(1, entries.Count);
            // TypeAddr: type=0x10 (half), addr lower 24 bits = 0x347A52
            Assert.AreEqual(0x10347A52u, entries[0].TypeAddr);
            Assert.AreEqual(0x03E7u, entries[0].Value);
        }

        [TestMethod]
        public void GetCheatTableEntries_FreezeWordEntry()
        {
            var items = Parse("freeze word $00347A54 = DEADBEEF");
            var entries = CdsCompiler.GetCheatTableEntries(items);
            Assert.AreEqual(1, entries.Count);
            // TypeAddr: type=0x20 (word), addr lower 24 bits = 0x347A54
            Assert.AreEqual(0x20347A54u, entries[0].TypeAddr);
            Assert.AreEqual(0xDEADBEEFu, entries[0].Value);
        }

        [TestMethod]
        public void GetCheatTableEntries_MultipleFreezeEntries()
        {
            var src = @"
freeze byte $20347A50 = 63
freeze half $20347A52 = 03E7
freeze word $20347A54 = FFFFFFFF
";
            var items = Parse(src);
            var entries = CdsCompiler.GetCheatTableEntries(items);
            Assert.AreEqual(3, entries.Count);
        }

        // -----------------------------------------------------------------------
        // P1: cond/endcond directive
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Cond_ParsedCorrectly()
        {
            var src = @"
cond mem[$20347A50] == 0x0001
  freeze word $20347A54 = FF
endcond
";
            var items = Parse(src);
            var cd = FindItem<CondSyntaxItem>(items);
            Assert.AreEqual(0x20347A50u, cd.Address);
            Assert.AreEqual(0x0001u, cd.ConditionValue);
            Assert.AreEqual(1, cd.Body.Count);
            Assert.IsInstanceOfType(cd.Body[0], typeof(FreezeSyntaxItem));
        }

        [TestMethod]
        public void Cond_DoesNotEmitCodeWords()
        {
            var src = @"
cond mem[$20347A50] == 0x0001
  freeze word $20347A54 = FF
endcond
";
            var pairs = Compile(src);
            Assert.AreEqual(0, pairs.Count);
        }

        [TestMethod]
        public void GetCheatTableEntries_CondBlock()
        {
            var src = @"
cond mem[$20347A50] == 0001
  freeze word $20347A54 = FF
endcond
";
            var items = Parse(src);
            var entries = CdsCompiler.GetCheatTableEntries(items);
            // 1 condition entry (0xC0) + 1 freeze entry (0x20)
            Assert.AreEqual(2, entries.Count);
            // Condition entry
            Assert.AreEqual(0xC0347A50u, entries[0].TypeAddr);
            Assert.AreEqual(0x0001u,     entries[0].Value);
            // Freeze entry
            Assert.AreEqual(0x20347A54u, entries[1].TypeAddr);
            Assert.AreEqual(0xFFu,       entries[1].Value);
        }

        // -----------------------------------------------------------------------
        // P1: timer/endtimer directive
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Timer_ParsedCorrectly()
        {
            var src = @"
timer 60
  freeze word $20347A54 = FF
endtimer
";
            var items = Parse(src);
            var timer = FindItem<TimerSyntaxItem>(items);
            Assert.AreEqual(60u, timer.IntervalFrames);
            Assert.AreEqual(1, timer.Body.Count);
            Assert.IsInstanceOfType(timer.Body[0], typeof(FreezeSyntaxItem));
        }

        [TestMethod]
        public void Timer_DoesNotEmitCodeWords()
        {
            var src = @"
timer 60
  freeze word $20347A54 = FF
endtimer
";
            var pairs = Compile(src);
            Assert.AreEqual(0, pairs.Count);
        }

        [TestMethod]
        public void GetCheatTableEntries_TimerBlock()
        {
            var src = @"
timer 60
  freeze word $20347A54 = FF
endtimer
";
            var items = Parse(src);
            var entries = CdsCompiler.GetCheatTableEntries(items);
            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual(0xB0000001u, entries[0].TypeAddr);
            Assert.AreEqual(60u, entries[0].Value);
            Assert.AreEqual(0x20347A54u, entries[1].TypeAddr);
            Assert.AreEqual(0xFFu, entries[1].Value);
        }

        [TestMethod]
        public void GetCheatTableEntries_TimerWithNestedCond_CountsBodyEntries()
        {
            var src = @"
timer 30
  cond mem[$20347A50] == 0001
    freeze word $20347A54 = FF
  endcond
endtimer
";
            var items = Parse(src);
            var entries = CdsCompiler.GetCheatTableEntries(items);
            Assert.AreEqual(3, entries.Count);
            Assert.AreEqual(0xB0000002u, entries[0].TypeAddr);
            Assert.AreEqual(30u, entries[0].Value);
            Assert.AreEqual(0xC0347A50u, entries[1].TypeAddr);
            Assert.AreEqual(0x20347A54u, entries[2].TypeAddr);
        }

        [TestMethod]
        public void GetCheatTableEntries_MultipleTimerBlocks_PreserveOrder()
        {
            var src = @"
timer 30
  freeze word $00123456 = 00000001
endtimer
timer 60
  freeze half $00AABBCC = 0002
endtimer
";
            var items = Parse(src);
            var entries = CdsCompiler.GetCheatTableEntries(items);
            Assert.AreEqual(4, entries.Count);
            Assert.AreEqual(0xB0000001u, entries[0].TypeAddr);
            Assert.AreEqual(30u, entries[0].Value);
            Assert.AreEqual(0x20123456u, entries[1].TypeAddr);
            Assert.AreEqual(0xB0000001u, entries[2].TypeAddr);
            Assert.AreEqual(60u, entries[2].Value);
            Assert.AreEqual(0x10AABBCCu, entries[3].TypeAddr);
        }

        [TestMethod]
        public void GetCheatTableEntries_EmptyWhenNoFreezeOrCond()
        {
            var items = Parse("address $00100000\nnop\nhexcode $DEADBEEF");
            var entries = CdsCompiler.GetCheatTableEntries(items);
            Assert.AreEqual(0, entries.Count);
        }

        [TestMethod]
        public void Freeze_AndMipsCode_CanCoexist()
        {
            var src = @"
address $00100000
nop
freeze byte $20347A50 = 63
nop
";
            // The two nops emit code; the freeze does not
            var pairs = Compile(src);
            Assert.AreEqual(2, pairs.Count);

            var items = Parse(src);
            var entries = CdsCompiler.GetCheatTableEntries(items);
            Assert.AreEqual(1, entries.Count);
        }
    }
}
