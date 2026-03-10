using CodeDesigner.Mips64LE.Common;
using CodeDesigner.Mips64LE.Common.Language.CDS;
using CodeDesigner.Mips64LE.Common.Language.CDS.SyntaxItems;

namespace CodeDesigner.Mips64LE.TestProject
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
        // Include directive (just checks the item is created; no file I/O)
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Include_Creates_IncludeSyntaxItem()
        {
            var items = Parse("include \"sub.cds\"");
            var inc = FindItem<IncludeSyntaxItem>(items);
            StringAssert.EndsWith(inc.FilePath, "sub.cds");
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
    }
}
