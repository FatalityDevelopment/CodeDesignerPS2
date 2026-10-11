using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.CdsLanguage;
using CodeDesigner.Ps2.CdsLanguage.SyntaxItems;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>
    /// Regression tests for the CDS compiler / MIPS assembler issues reported from the field:
    /// 9-digit address and immediate values, coprocessor instructions (lwc1 / swc1 / mtc1 / …)
    /// that could not be assembled, inconsistent lwc1 / swc1 disassembly, label operands on
    /// non-branch instructions, broken <c>mem[] *=</c> / <c>/=</c> expansions and the
    /// directive spellings used by the shipped reference scripts.
    /// </summary>
    [TestClass]
    public class TestCdsLanguageFixes
    {
        private static List<CdsSyntaxItem> Parse(string src, out List<CdsDiagnostic> diagnostics)
        {
            var parser = new CdsParser();
            var items = parser.Parse(src.Trim());
            diagnostics = parser.Diagnostics.ToList();
            return items;
        }

        private static List<(uint Address, uint Word)> CompileClean(string src)
        {
            var items = Parse(src, out var diags);
            var errors = diags.Where(d => d.Severity == CdsDiagnosticSeverity.Error).ToList();
            Assert.AreEqual(0, errors.Count,
                "unexpected errors: " + string.Join(" | ", errors.Select(e => $"line {e.LineNumber}: {e.Message}")));
            return CdsCompiler.Compile(items);
        }

        private static void AssertAsm(string asm, uint expected)
        {
            uint actual = MipsAssembler.Assemble(asm);
            Assert.AreEqual(expected, actual, $"'{asm}' expected 0x{expected:X8} got 0x{actual:X8}");
        }

        private static void AssertAsmThrows(string asm, string messagePart)
        {
            try
            {
                uint word = MipsAssembler.Assemble(asm);
                Assert.Fail($"'{asm}' should have been rejected but assembled to 0x{word:X8}");
            }
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, messagePart);
            }
        }

        // ------------------------------------------------------------------
        // Assembler: COP1 (FPU)
        // ------------------------------------------------------------------

        [TestMethod]
        public void Asm_Lwc1_Swc1_MatchHandAssembledWords()
        {
            // The SOCOM scripts carried these as `hexcode` because the assembler rejected them.
            AssertAsm("swc1 $f12, 16($sp)", 0xE7AC0010);
            AssertAsm("lwc1 $f12, 16($sp)", 0xC7AC0010);
            AssertAsm("lwc1 $f12, $0010($sp)", 0xC7AC0010);
            AssertAsm("lwc1 f12, 0x10(sp)", 0xC7AC0010);
            AssertAsm("lwc1 $f0, -16($sp)", 0xC7A0FFF0);
            AssertAsm("ldc1 $f0, 0($t0)", 0xD5000000);
            AssertAsm("sdc1 $f0, 0($t0)", 0xF5000000);
        }

        [TestMethod]
        public void Asm_FpuRegisterMoves()
        {
            AssertAsm("mtc1 $t0, $f2", 0x44881000);
            AssertAsm("mfc1 $t0, $f2", 0x44081000);
            AssertAsm("cfc1 $t0, $31", 0x4448F800);
            AssertAsm("ctc1 $t0, fcr31", 0x44C8F800);
        }

        [TestMethod]
        public void Asm_FpuArithmeticAndConversion()
        {
            AssertAsm("add.s $f0, $f1, $f2", 0x46020800);
            AssertAsm("sub.s $f3, $f1, $f2", 0x460208C1);
            AssertAsm("mul.s $f4, $f4, $f5", 0x46052102);
            AssertAsm("div.s $f0, $f1, $f2", 0x46020803);
            AssertAsm("mov.s $f1, $f2", 0x46001046);
            AssertAsm("cvt.s.w $f0, $f1", 0x46800820);
            AssertAsm("cvt.w.s $f0, $f1", 0x46000824);
            AssertAsm("c.lt.s $f0, $f1", 0x46010034);
            AssertAsm("c.eq.s $f0, $f1", 0x46010032);
            AssertAsm("c.le.s $f0, $f1", 0x46010036);
        }

        [TestMethod]
        public void Asm_FpuBranches()
        {
            AssertAsm("bc1t $0004", 0x45010004);
            AssertAsm("bc1f 4", 0x45000004);
            AssertAsm("bc1tl 4", 0x45030004);
            AssertAsm("bc1fl 4", 0x45020004);
        }

        // ------------------------------------------------------------------
        // Assembler: COP0, REGIMM, branch-likely, 64-bit shifts, MMI, COP2
        // ------------------------------------------------------------------

        [TestMethod]
        public void Asm_Cop0()
        {
            AssertAsm("mfc0 $t0, Status", 0x40086000);
            AssertAsm("mtc0 $t0, $12", 0x40886000);
            AssertAsm("eret", 0x42000018);
            AssertAsm("ei", 0x42000038);
            AssertAsm("di", 0x42000039);
            AssertAsm("tlbwi", 0x42000002);
            AssertAsm("cache 0x14, 0($t0)", 0xBD140000);
        }

        [TestMethod]
        public void Asm_RegimmAndBranchLikely()
        {
            AssertAsm("bltz $t0, 4", 0x05000004);
            AssertAsm("bgez $t0, 4", 0x05010004);
            AssertAsm("bltzl $t0, 4", 0x05020004);
            AssertAsm("bgezl $t0, 4", 0x05030004);
            AssertAsm("bltzal $t0, 4", 0x05100004);
            AssertAsm("bgezal $t0, 4", 0x05110004);
            AssertAsm("blezl $t0, 4", 0x59000004);
            AssertAsm("bgtzl $t0, 4", 0x5D000004);
            AssertAsm("tgei $t0, 1", 0x05080001);
            AssertAsm("mtsab $t0, 4", 0x05180004);
        }

        [TestMethod]
        public void Asm_Shift32AndMultiplyForms()
        {
            AssertAsm("dsll32 $t0, $t1, 4", 0x0009413C);
            AssertAsm("dsra32 $t0, $t1, 31", 0x000947FF);
            AssertAsm("mult $v0, $t0, $t1", 0x01091018);   // EE three-operand form
            AssertAsm("madd $t0, $t1", 0x71090000);
            AssertAsm("madd $v0, $t0, $t1", 0x71091000);
            AssertAsm("neg $t0, $t1", 0x00094022);
        }

        [TestMethod]
        public void Asm_Mmi()
        {
            AssertAsm("mult1 $t0, $t1", 0x71090018);
            AssertAsm("mflo1 $t0", 0x70004012);
            AssertAsm("mfhi1 $t0", 0x70004010);
            AssertAsm("pand $t0, $t1, $t2", 0x712A4489);
            AssertAsm("por $t0, $t1, $t2", 0x712A44A9);
            AssertAsm("pxor $t0, $t1, $t2", 0x712A44C9);
            AssertAsm("pnor $t0, $t1, $t2", 0x712A44E9);
            AssertAsm("paddw $t0, $t1, $t2", 0x712A4008);
            AssertAsm("pmfhl.lw $t0", 0x70004030);
            AssertAsm("pmfhl.uw $t0", 0x70004070);
            AssertAsm("lq $t0, $0010($sp)", 0x7BA80010);
            AssertAsm("sq $t0, $0010($sp)", 0x7FA80010);
        }

        [TestMethod]
        public void Asm_Cop2LoadsAndMoves()
        {
            AssertAsm("lqc2 vf1, 0($t0)", 0xD9010000);
            AssertAsm("sqc2 vf1, 16($t0)", 0xF9010010);
            AssertAsm("qmfc2 $t0, vf1", 0x48280800);
            AssertAsm("cfc2 $t0, vi1", 0x48480800);
        }

        [TestMethod]
        public void Asm_JalrSingleOperandAndSync()
        {
            AssertAsm("jalr $t9", 0x0320F809);
            AssertAsm("jalr $ra, $t9", 0x0320F809);
            AssertAsm("jalr $v0", 0x0040F809);
            AssertAsm("sync", 0x0000000F);
            AssertAsm("sync.p", 0x0000040F);
        }

        // ------------------------------------------------------------------
        // Assembler: immediates
        // ------------------------------------------------------------------

        [TestMethod]
        public void Asm_NineHexDigitImmediate_IsRejectedWithClearMessage()
        {
            // This used to surface as "Value was either too large or too small for an Int32".
            AssertAsmThrows("lui $t0, $0000D0800", "9 hex digits");
            AssertAsmThrows("j $100000000", "9 hex digits");
        }

        [TestMethod]
        public void Asm_ImmediateOutOf16BitRange_IsRejected()
        {
            // Previously silently truncated to 0x2345.
            AssertAsmThrows("addiu $t0, $zero, $12345", "16 bits");
            AssertAsmThrows("lui $t0, 0x10000", "16 bits");
            AssertAsmThrows("sll $t0, $t1, 32", "0..31");
        }

        [TestMethod]
        public void Asm_SignExtended32BitSpelling_IsAcceptedForNegativeImmediates()
        {
            Assert.AreEqual(MipsAssembler.Assemble("addiu $sp, $sp, -16"),
                            MipsAssembler.Assemble("addiu $sp, $sp, $FFFFFFF0"));
            Assert.AreEqual(0x27BDFFF0u, MipsAssembler.Assemble("addiu $sp, $sp, -16"));
        }

        [TestMethod]
        public void Asm_Li_PicksAddiuOrOri()
        {
            AssertAsm("li $t0, -1", 0x2408FFFF);
            AssertAsm("li $t0, 0xFFFF", 0x3408FFFF);
            AssertAsm("li $t0, 100", 0x24080064);
        }

        [TestMethod]
        public void Asm_WrongOperandCountOrRegisterKind_HasHelpfulMessage()
        {
            AssertAsmThrows("add $t0, $s0", "Expected 3 operands for add, got 2");
            AssertAsmThrows("lwc1 $t0, 0($sp)", "Unknown FPU register");
            AssertAsmThrows("frobnicate $t0", "Unknown instruction");
        }

        [TestMethod]
        public void Asm_TryAssemble_DoesNotThrow()
        {
            Assert.IsTrue(MipsAssembler.TryAssemble("nop", out uint word, out string? error));
            Assert.AreEqual(0u, word);
            Assert.IsNull(error);

            Assert.IsFalse(MipsAssembler.TryAssemble("bogus", out _, out error));
            Assert.IsNotNull(error);
        }

        // ------------------------------------------------------------------
        // Disassembler: coprocessor loads/stores use the same operand format as lw/sw
        // ------------------------------------------------------------------

        [TestMethod]
        public void Disasm_Lwc1Swc1_UseHexOffsetLikeOtherLoads()
        {
            Assert.AreEqual("lwc1 $f12, $0010(sp)", MipsDisassembler.Disassemble(0xC7AC0010));
            Assert.AreEqual("swc1 $f12, $0010(sp)", MipsDisassembler.Disassemble(0xE7AC0010));
            Assert.AreEqual("lwc1 $f0, $FFF0(sp)", MipsDisassembler.Disassemble(0xC7A0FFF0));
            Assert.AreEqual("lw t0, $0010(sp)", MipsDisassembler.Disassemble(0x8FA80010));
        }

        [TestMethod]
        public void Disasm_Asm_RoundTrip_CoprocessorAndMmiWords()
        {
            uint[] words =
            {
                0xC7AC0010, 0xE7AC0010, 0xC7A0FFF0, 0xD5000000, 0xF5000000,  // lwc1 swc1 ldc1 sdc1
                0x44881000, 0x44081000, 0x4448F800, 0x44C8F800,              // mtc1 mfc1 cfc1 ctc1
                0x46020800, 0x460208C1, 0x46052102, 0x46020803, 0x46001046,  // add.s sub.s mul.s div.s mov.s
                0x46800820, 0x46000824, 0x46010034, 0x46010032,              // cvt.s.w cvt.w.s c.lt.s c.eq.s
                0x40086000, 0x40886000, 0x42000018, 0x42000038,              // mfc0 mtc0 eret ei
                0x05100004, 0x05020004, 0x59000004, 0x5D000004,              // bltzal bltzl blezl bgtzl
                0x0009413C, 0x71090018, 0x70004012, 0x712A4489, 0x712A44A9,  // dsll32 mult1 mflo1 pand por
                0x7BA80010, 0x7FA80010, 0xBD140000, 0x0320F809,              // lq sq cache jalr
                0xD9010000, 0xF9010010,                                      // lqc2 sqc2
            };

            foreach (uint word in words)
            {
                string text = MipsDisassembler.Disassemble(word);
                Assert.IsTrue(MipsAssembler.TryAssemble(text, out uint back, out string? error),
                    $"0x{word:X8} disassembled to '{text}' which did not assemble: {error}");
                Assert.AreEqual(word, back, $"round trip of 0x{word:X8} via '{text}'");
            }
        }

        // ------------------------------------------------------------------
        // Parser: addresses and values
        // ------------------------------------------------------------------

        [TestMethod]
        public void Parser_NineDigitAddress_IsDiagnosedNotThrown()
        {
            var items = Parse("address $020100000\nnop", out var diags);
            var error = diags.FirstOrDefault(d => d.Severity == CdsDiagnosticSeverity.Error);
            Assert.IsNotNull(error, "a 9-digit address must produce an error diagnostic");
            Assert.AreEqual("CDS0014", error.Code);
            StringAssert.Contains(error.Message, "9 hex digits");
            Assert.AreEqual(1, error.LineNumber);
            // The rest of the file still parses.
            Assert.AreEqual(1, items.OfType<OperationSyntaxItem>().Count());
        }

        [TestMethod]
        public void Parser_NineDigitValues_InOtherDirectives_AreDiagnosed()
        {
            string[] lines =
            {
                "hexcode $1DEADBEEF",
                "setreg $t0, $100000000",
                "assert mem[$120010000] == $0",
                "freeze word $120347A54 = FF",
                "cond mem[$120347A50] == 1\nendcond",
                "watchpoint write $120347A50 4",
            };
            foreach (var line in lines)
            {
                Parse("address $00100000\n" + line, out var diags);
                Assert.IsTrue(diags.Any(d => d.Severity == CdsDiagnosticSeverity.Error && d.Code == "CDS0014"),
                    $"expected a CDS0014 error for '{line}', got: {string.Join(" | ", diags.Select(d => d.Message))}");
            }
        }

        [TestMethod]
        public void Parser_AddressAcceptsDollarHexAndBareForms()
        {
            foreach (var form in new[] { "$00100000", "0x00100000", "00100000" })
            {
                var items = Parse($"address {form}\nnop", out var diags);
                Assert.AreEqual(0, diags.Count(d => d.Severity == CdsDiagnosticSeverity.Error), form);
                Assert.AreEqual(0x00100000u, items.OfType<OperationSyntaxItem>().Single().Address, form);
            }
        }

        [TestMethod]
        public void Parser_LabelOperand_OnNonBranchInstruction_NoLongerBuildsNineDigitImmediate()
        {
            var src = @"
address $00100000
lui   $t0, :data
ori   $t0, $t0, :data
addiu $t1, $zero, lo(:data)
lui   $t2, hi(:data)
address $00AB1230
data:
hexcode $00000000
";
            var pairs = CompileClean(src);
            Assert.AreEqual(MipsAssembler.Assemble("lui $t0, $00AB"), pairs[0].Word);
            Assert.AreEqual(MipsAssembler.Assemble("ori $t0, $t0, $1230"), pairs[1].Word);
            Assert.AreEqual(MipsAssembler.Assemble("addiu $t1, $zero, $1230"), pairs[2].Word);
            Assert.AreEqual(MipsAssembler.Assemble("lui $t2, $00AB"), pairs[3].Word);
        }

        [TestMethod]
        public void Parser_SignExtendedLowHalf_WithAddiu_Warns()
        {
            var src = @"
address $00100000
lui   $t0, :data
addiu $t0, $t0, :data
address $00AB9230
data:
hexcode $00000000
";
            Parse(src, out var diags);
            Assert.IsTrue(diags.Any(d => d.Code == "CDS0018" && d.Severity == CdsDiagnosticSeverity.Warning));
        }

        // ------------------------------------------------------------------
        // Parser: the SOCOM main-loop stub compiles with real lwc1/swc1
        // ------------------------------------------------------------------

        [TestMethod]
        public void Parser_SocomCallStub_CompilesWithFpuLoadStore()
        {
            var src = @"
address $000D0800
call_stub:
    lui   $t0, $000D
    lw    $t1, $06F0($t0)
    beq   $t1, $zero, :call_done
    nop
    addiu $sp, $sp, -64
    sd    $ra, $0000($sp)
    sd    $a0, $0008($sp)
    swc1  $f12, 16($sp)
    sd    $s0, $0018($sp)
    ld    $s0, $0018($sp)
    lwc1  $f12, 16($sp)
    ld    $a0, $0008($sp)
    ld    $ra, $0000($sp)
    addiu $sp, $sp, 64
call_done:
    lw    $t9, $0000($a0)
    lw    $t9, $001C($t9)
    jalr  $t9
    nop
";
            var pairs = CompileClean(src);
            Assert.AreEqual(0xE7AC0010u, pairs[7].Word, "swc1 $f12, 16($sp)");
            Assert.AreEqual(0xC7AC0010u, pairs[10].Word, "lwc1 $f12, 16($sp)");
            Assert.AreEqual(0x0320F809u, pairs[16].Word, "jalr $t9");
            Assert.IsFalse(pairs.Skip(4).Take(12).Any(p => p.Word == 0), "no instruction may silently assemble to 0");
        }

        // ------------------------------------------------------------------
        // Parser: mem[] arithmetic, li, blt family
        // ------------------------------------------------------------------

        [TestMethod]
        public void Parser_MemMultiply_ExpandsToMultAndMflo()
        {
            var items = Parse("address $00100000\nmem[0x10] t0 *= 3\nafter:", out _);
            var mem = items.OfType<MemorySyntaxItem>().Single();
            CollectionAssert.AreEqual(new[] { "lw", "addiu", "mult", "mflo", "sw" },
                mem.Operations.Select(o => o.Mnemonic).ToArray());
            Assert.AreEqual(MipsAssembler.Assemble("mult $t9, $at"), mem.Operations[2].EncodedWord);
            Assert.AreEqual(MipsAssembler.Assemble("mflo $t9"), mem.Operations[3].EncodedWord);
            Assert.IsFalse(mem.Operations.Any(o => o.EncodedWord == 0), "mem[] *= used to emit zero words");
            Assert.AreEqual(0x00100014u, items.OfType<LabelSyntaxItem>().Single().Address);
        }

        [TestMethod]
        public void Parser_MemDivide_ExpandsToDivAndMflo()
        {
            var items = Parse("address $00100000\nmem.h[0x10] t0 /= 2\nafter:", out _);
            var mem = items.OfType<MemorySyntaxItem>().Single();
            CollectionAssert.AreEqual(new[] { "lh", "addiu", "div", "mflo", "sh" },
                mem.Operations.Select(o => o.Mnemonic).ToArray());
            Assert.AreEqual(0x00100014u, items.OfType<LabelSyntaxItem>().Single().Address);
        }

        [TestMethod]
        public void Parser_MemAssign32Bit_UsesLuiOri()
        {
            var items = Parse("address $00100000\nmem[0x10] t0 = 0x12345678\nafter:", out _);
            var mem = items.OfType<MemorySyntaxItem>().Single();
            CollectionAssert.AreEqual(new[] { "lui", "ori", "sw" }, mem.Operations.Select(o => o.Mnemonic).ToArray());
            Assert.AreEqual(MipsAssembler.Assemble("lui $t9, $1234"), mem.Operations[0].EncodedWord);
            Assert.AreEqual(MipsAssembler.Assemble("ori $t9, $t9, $5678"), mem.Operations[1].EncodedWord);
            Assert.AreEqual(0x0010000Cu, items.OfType<LabelSyntaxItem>().Single().Address);
        }

        [TestMethod]
        public void Parser_Li_IsOneWordWhenSmallAndTwoWordsWhenLarge()
        {
            var small = Parse("address $00100000\nli $t0, 5\nafter:", out var d1);
            Assert.AreEqual(0, d1.Count(d => d.Severity == CdsDiagnosticSeverity.Error));
            var op = small.OfType<OperationSyntaxItem>().Single();
            Assert.AreEqual(MipsAssembler.Assemble("addiu $t0, $zero, 5"), op.EncodedWord);
            Assert.AreEqual(0x00100004u, small.OfType<LabelSyntaxItem>().Single().Address);

            var large = Parse("address $00100000\nli $t0, 0x12345678\nafter:", out var d2);
            Assert.AreEqual(0, d2.Count(d => d.Severity == CdsDiagnosticSeverity.Error));
            var sr = large.OfType<SetRegSyntaxItem>().Single();
            Assert.AreEqual(0x12345678u, sr.Value);
            Assert.AreEqual(0x00100008u, large.OfType<LabelSyntaxItem>().Single().Address);

            var unsigned = Parse("address $00100000\nli $t0, 0xFFFF", out _);
            Assert.AreEqual("ori", unsigned.OfType<OperationSyntaxItem>().Single().Mnemonic);
        }

        [TestMethod]
        public void Parser_BranchPseudo_ExpandsToSltAndBranch()
        {
            var src = @"
address $00100000
blt $t0, $t1, :target
nop
nop
target:
";
            var items = Parse(src, out var diags);
            Assert.AreEqual(0, diags.Count(d => d.Severity == CdsDiagnosticSeverity.Error));
            var ops = items.OfType<OperationSyntaxItem>().ToList();
            Assert.AreEqual("slt", ops[0].Mnemonic);
            Assert.AreEqual(MipsAssembler.Assemble("slt $at, $t0, $t1"), ops[0].EncodedWord);
            Assert.AreEqual("bne", ops[1].Mnemonic);
            Assert.AreEqual(0x00100004u, ops[1].Address);
            // target = 0x00100010; branch at 0x00100004 → (0x10 - 0x08) / 4 = 2
            Assert.AreEqual(MipsAssembler.Assemble("bne $at, $zero, $0002"), ops[1].EncodedWord);
            Assert.AreEqual(0x00100010u, items.OfType<LabelSyntaxItem>().Single().Address);
        }

        [TestMethod]
        public void Parser_BranchPseudo_Variants()
        {
            static (uint slt, uint br) Expand(string mnemonic)
            {
                var items = new CdsParser().Parse($"address $00100000\n{mnemonic} $t0, $t1, :t\nnop\nt:");
                var ops = items.OfType<OperationSyntaxItem>().ToList();
                return (ops[0].EncodedWord, ops[1].EncodedWord);
            }

            Assert.AreEqual((MipsAssembler.Assemble("slt $at, $t1, $t0"), MipsAssembler.Assemble("bne $at, $zero, 1")), Expand("bgt"));
            Assert.AreEqual((MipsAssembler.Assemble("slt $at, $t1, $t0"), MipsAssembler.Assemble("beq $at, $zero, 1")), Expand("ble"));
            Assert.AreEqual((MipsAssembler.Assemble("slt $at, $t0, $t1"), MipsAssembler.Assemble("beq $at, $zero, 1")), Expand("bge"));
            Assert.AreEqual((MipsAssembler.Assemble("sltu $at, $t0, $t1"), MipsAssembler.Assemble("bne $at, $zero, 1")), Expand("bltu"));
        }

        // ------------------------------------------------------------------
        // Parser: lenient directive spellings used by the reference scripts
        // ------------------------------------------------------------------

        [TestMethod]
        public void Parser_FreezeWithoutSize_DefaultsToWord()
        {
            var items = Parse("freeze $20123456 = 9999", out var diags);
            Assert.AreEqual(0, diags.Count(d => d.Severity == CdsDiagnosticSeverity.Error));
            var fr = items.OfType<FreezeSyntaxItem>().Single();
            Assert.AreEqual(CdsFreezeType.Word, fr.FreezeType);
            Assert.AreEqual(0x20123456u, fr.Address);
            Assert.AreEqual(0x9999u, fr.Value);
        }

        [TestMethod]
        public void Parser_CondAndAssert_AcceptBareAddress()
        {
            var items = Parse("cond $80001234 == 1\n  freeze word $20123456 = 1\nendcond\nassert $20123456 == 0xDEADBEEF", out var diags);
            Assert.AreEqual(0, diags.Count(d => d.Severity == CdsDiagnosticSeverity.Error));
            var cond = items.OfType<CondSyntaxItem>().Single();
            Assert.AreEqual(0x80001234u, cond.Address);
            Assert.AreEqual(1u, cond.ConditionValue);
            Assert.AreEqual(1, cond.Body.Count);
            var assert = items.OfType<AssertSyntaxItem>().Single();
            Assert.AreEqual(0x20123456u, assert.Address);
            Assert.AreEqual(0xDEADBEEFu, assert.ExpectedValue);
        }

        [TestMethod]
        public void Parser_BreakpointAndWatchpoint_AcceptExplicitAddress()
        {
            var items = Parse("address $00100000\nbreakpoint $20100000\nwatchpoint $20200000\nwatchpoint read $20200010 2", out var diags);
            Assert.AreEqual(0, diags.Count(d => d.Severity == CdsDiagnosticSeverity.Error));
            Assert.AreEqual(0x20100000u, items.OfType<BreakpointSyntaxItem>().Single().Address);
            var wps = items.OfType<WatchpointSyntaxItem>().ToList();
            Assert.AreEqual(0x20200000u, wps[0].Address);
            Assert.AreEqual(4u, wps[0].Size);
            Assert.AreEqual(WatchpointAccessType.Write, wps[0].AccessType);
            Assert.AreEqual(WatchpointAccessType.Read, wps[1].AccessType);
            Assert.AreEqual(2u, wps[1].Size);
        }

        [TestMethod]
        public void Parser_JalrSingleOperand_InScript()
        {
            var pairs = CompileClean("address $00100000\njalr $t9\nnop");
            Assert.AreEqual(0x0320F809u, pairs[0].Word);
        }

        [TestMethod]
        public void Parser_LaWithNumericAddress_ActsLikeSetreg()
        {
            var items = Parse("address $00100000\nla $t0, $00123456", out var diags);
            Assert.AreEqual(0, diags.Count(d => d.Severity == CdsDiagnosticSeverity.Error));
            Assert.AreEqual(0x00123456u, items.OfType<SetRegSyntaxItem>().Single().Value);
        }

        [TestMethod]
        public void Parser_CommentMarkersInsideStrings_AreKept()
        {
            var items = Parse("address $00100000\nstring \"a;b // c\"\nlog \"x; y\"", out var diags);
            Assert.AreEqual(0, diags.Count(d => d.Severity == CdsDiagnosticSeverity.Error));
            Assert.AreEqual("a;b // c", items.OfType<StringSyntaxItem>().Single().Text);
            Assert.AreEqual("x; y", items.OfType<LogSyntaxItem>().Single().MessageText);
        }

        [TestMethod]
        public void Parser_HashAndSemicolonFullLineComments_DoNotError()
        {
            Parse("# a comment\n; another\naddress $00100000\nnop ; trailing", out var diags);
            Assert.AreEqual(0, diags.Count, string.Join(" | ", diags.Select(d => d.Message)));
        }

        [TestMethod]
        public void Parser_DuplicateLabel_IsAnError()
        {
            Parse("address $00100000\nloop:\nnop\nloop:\nnop", out var diags);
            Assert.IsTrue(diags.Any(d => d.Severity == CdsDiagnosticSeverity.Error && d.Code == "CDS0021"));
        }

        [TestMethod]
        public void Parser_BranchOutOfRange_IsAnError()
        {
            Parse("address $00100000\nbeq $t0, $t1, :far\nnop\naddress $00200000\nfar:\nnop", out var diags);
            Assert.IsTrue(diags.Any(d => d.Severity == CdsDiagnosticSeverity.Error && d.Code == "CDS0019"));
        }

        [TestMethod]
        public void Parser_JumpBetweenKseg0AndUserSegment_IsAllowed()
        {
            // 0x00000838 and 0x80000838 are the same physical RAM on the PS2.
            Parse("address $80101000\nj :lib\nnop\naddress $00000838\nlib:\njr $ra\nnop", out var diags);
            Assert.AreEqual(0, diags.Count(d => d.Severity == CdsDiagnosticSeverity.Error),
                string.Join(" | ", diags.Select(d => d.Message)));
        }

        [TestMethod]
        public void Parser_JumpToDifferentPhysicalRegion_IsAnError()
        {
            Parse("address $00100000\nj :bios\nnop\naddress $1FC00000\nbios:\nnop", out var diags);
            Assert.IsTrue(diags.Any(d => d.Severity == CdsDiagnosticSeverity.Error && d.Code == "CDS0019"));
        }

        [TestMethod]
        public void Parser_LaAfterBranch_IsNotReportedAsEmptyDelaySlot()
        {
            Parse("address $00100000\nbeq $t0, $t1, :t\nla $t2, :t\nt:\nnop", out var diags);
            Assert.IsFalse(diags.Any(d => d.Code == "CDS0006"));
        }

        // ------------------------------------------------------------------
        // Preprocessor: const / alias / macro spellings, compile-time if
        // ------------------------------------------------------------------

        [TestMethod]
        public void Preprocessor_ConstAndAlias_WithoutEqualsSign()
        {
            var pairs = CompileClean("const VALUE 5\nalias counter $t0\naddress $00100000\nli counter, VALUE");
            Assert.AreEqual(1, pairs.Count);
            Assert.AreEqual(MipsAssembler.Assemble("addiu $t0, $zero, 5"), pairs[0].Word);
        }

        [TestMethod]
        public void Preprocessor_MacroWithoutParentheses()
        {
            var src = @"
macro set_reg reg, val
  addiu reg, $zero, val
endmacro
address $00100000
set_reg $v0, 7
";
            var pairs = CompileClean(src);
            Assert.AreEqual(1, pairs.Count);
            Assert.AreEqual(MipsAssembler.Assemble("addiu $v0, $zero, 7"), pairs[0].Word);
        }

        [TestMethod]
        public void Preprocessor_ConstantIf_IsEvaluatedAtCompileTime()
        {
            string Script(int debug) => $@"
const DEBUG {debug}
const MAX_HP 9999
address $00100000
if DEBUG
  nop
else
  nop
  nop
endif
if MAX_HP > 500
  nop
endif
";
            // DEBUG=1: then-branch (1 nop) + MAX_HP body (1 nop); no branch instructions emitted.
            var on = CompileClean(Script(1));
            Assert.AreEqual(2, on.Count);
            Assert.IsTrue(on.All(p => p.Word == 0));

            // DEBUG=0: else-branch (2 nops) + MAX_HP body (1 nop).
            var off = CompileClean(Script(0));
            Assert.AreEqual(3, off.Count);
        }

        [TestMethod]
        public void Preprocessor_IncludeIsInlinedOnlyOnce()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cds-include-once-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "lib.cds"), "lib_fn:\n  jr $ra\n  nop\n");
                File.WriteAllText(Path.Combine(dir, "mid.cds"), "include \"lib.cds\"\n");
                var parser = new CdsParser();
                var items = parser.Parse("address $00100000\ninclude \"mid.cds\"\ninclude \"lib.cds\"\njal :lib_fn\nnop", dir);
                var errors = parser.Diagnostics.Where(d => d.Severity == CdsDiagnosticSeverity.Error).ToList();
                Assert.AreEqual(0, errors.Count, string.Join(" | ", errors.Select(e => e.Message)));
                // lib_fn defined once: jr, nop, jal, nop
                Assert.AreEqual(4, CdsCompiler.Compile(items).Count);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // ------------------------------------------------------------------
        // Shipped scripts compile
        // ------------------------------------------------------------------

        [TestMethod]
        public void ShippedScripts_StdLibAndMods_CompileWithoutErrors()
        {
            string root = LocateRepoRoot();
            var files = new List<string>();
            files.AddRange(Directory.GetFiles(Path.Combine(root, "CodeDesigner.Ps2.CdsLanguage", "StdLib"), "*.cds"));
            string mods = Path.Combine(root, "Mods");
            if (Directory.Exists(mods))
                files.AddRange(Directory.GetFiles(mods, "*.cds", SearchOption.AllDirectories));

            var failures = new List<string>();
            foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
            {
                var parser = new CdsParser();
                string basePath = Path.GetDirectoryName(file)!;
                // Files that include "StdLib/..." resolve relative to the project directory.
                string src = File.ReadAllText(file);
                if (System.Text.RegularExpressions.Regex.IsMatch(src, @"^\s*include\s+""StdLib/", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    basePath = Path.GetDirectoryName(basePath)!;
                parser.Parse(src, basePath);
                var errors = parser.Diagnostics.Where(d => d.Severity == CdsDiagnosticSeverity.Error).ToList();
                if (errors.Count > 0)
                    failures.Add($"{Path.GetRelativePath(root, file)}: " +
                                 string.Join(" | ", errors.Take(5).Select(e => $"line {e.LineNumber}: {e.Message}")));
            }

            Assert.AreEqual(0, failures.Count, "\n" + string.Join("\n", failures));
        }

        private static string LocateRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "CodeDesigner.Ps2.CdsLanguage", "StdLib"))) return dir;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return AppContext.BaseDirectory;
        }
    }
}
