using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.CdsLanguage;
using CodeDesigner.Ps2.CdsLanguage.SyntaxItems;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>
    /// Tests for the language extensions added alongside the StdLib audit:
    ///  - Single-instruction MIPS pseudo-instructions in <see cref="MipsAssembler"/>.
    ///  - <c>la $reg, :label</c> directive.
    ///  - <c>extern function</c> declarations + call-site expansion.
    ///  - Smoke compile of every <c>StdLib/*.cds</c> file.
    /// </summary>
    [TestClass]
    public class TestCdsLanguageExtensions
    {
        private static readonly CdsParser Parser = new();

        private static List<CdsSyntaxItem> Parse(string src) => Parser.Parse(src.Trim());
        private static List<(uint Address, uint Word)> Compile(string src) => CdsCompiler.Compile(Parse(src));

        // ------------------------------------------------------------------
        // Single-instruction pseudo-instruction tests (MipsAssembler layer)
        // ------------------------------------------------------------------

        [TestMethod]
        public void Pseudo_Move_EquivalentToAdduWithZero()
        {
            uint expected = MipsAssembler.Assemble("addu $t0, $t1, $zero");
            uint actual   = MipsAssembler.Assemble("move $t0, $t1");
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Pseudo_Not_EquivalentToNorWithZero()
        {
            uint expected = MipsAssembler.Assemble("nor $t0, $t1, $zero");
            uint actual   = MipsAssembler.Assemble("not $t0, $t1");
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Pseudo_Negu_EquivalentToSubuFromZero()
        {
            uint expected = MipsAssembler.Assemble("subu $t0, $zero, $t1");
            uint actual   = MipsAssembler.Assemble("negu $t0, $t1");
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Pseudo_Bnez_EquivalentToBneAgainstZero()
        {
            uint expected = MipsAssembler.Assemble("bne $t0, $zero, 4");
            uint actual   = MipsAssembler.Assemble("bnez $t0, 4");
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Pseudo_Beqz_EquivalentToBeqAgainstZero()
        {
            uint expected = MipsAssembler.Assemble("beq $t0, $zero, 4");
            uint actual   = MipsAssembler.Assemble("beqz $t0, 4");
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Pseudo_B_EquivalentToBeqZeroZero()
        {
            uint expected = MipsAssembler.Assemble("beq $zero, $zero, 8");
            uint actual   = MipsAssembler.Assemble("b 8");
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Pseudo_Li_EquivalentToAddiuFromZero()
        {
            uint expected = MipsAssembler.Assemble("addiu $t0, $zero, 100");
            uint actual   = MipsAssembler.Assemble("li $t0, 100");
            Assert.AreEqual(expected, actual);
        }

        // ------------------------------------------------------------------
        // `la $reg, :label` directive
        // ------------------------------------------------------------------

        [TestMethod]
        public void La_ResolvesLabelToLuiAndOriPair()
        {
            var src = @"
address $00200000
target:
hexcode $DEADBEEF
la $t0, :target
";
            var items = Parse(src);
            var la = items.OfType<LaSyntaxItem>().Single();
            Assert.AreEqual("target", la.ResolvedLabel);
            Assert.AreEqual(0x00200000u, la.Value);
            Assert.AreEqual("lui", la.LuiOp.Mnemonic);
            Assert.AreEqual("ori", la.OriOp.Mnemonic);
        }

        [TestMethod]
        public void La_AdvancesAddressByEight()
        {
            var src = @"
address $00200000
target:
hexcode $DEADBEEF
la $t0, :target
follow:
";
            var items = Parse(src);
            var labels = items.OfType<LabelSyntaxItem>().ToList();
            // target at 0x200000, hexcode pushes 4, la pushes 8 → follow at 0x20000C
            Assert.AreEqual(0x0020000Cu, labels.Single(l => l.Name == "follow").Address);
        }

        [TestMethod]
        public void La_CompiledPair_EncodesLuiOriOfResolvedAddress()
        {
            // Place target at 0x80101040, then `la $t0, :target` should produce
            // lui  $t0, $8010  and  ori $t0, $t0, $1040.
            var src = @"
address $80101040
target:
nop
la $t0, :target
";
            var pairs = Compile(src);
            // pairs[0] = nop, pairs[1] = lui, pairs[2] = ori
            Assert.AreEqual(MipsAssembler.Assemble("lui $t0, $8010"), pairs[1].Word);
            Assert.AreEqual(MipsAssembler.Assemble("ori $t0, $t0, $1040"), pairs[2].Word);
        }

        // ------------------------------------------------------------------
        // `extern function` declarations + call-site expansion
        // ------------------------------------------------------------------

        [TestMethod]
        public void ExternFunction_CallExpandsToArgLoadsAndJal()
        {
            var src = @"
extern function str_copy(dst, src) at :lib_strcpy
address $00200000
str_copy(t0, t1)
lib_strcpy:
nop
";
            var pairs = Compile(src);
            // Expected: addu $a0, $t0, $zero / addu $a1, $t1, $zero / jal :lib_strcpy / nop / nop
            Assert.AreEqual(5, pairs.Count);
            Assert.AreEqual(MipsAssembler.Assemble("addu $a0, $t0, $zero"), pairs[0].Word);
            Assert.AreEqual(MipsAssembler.Assemble("addu $a1, $t1, $zero"), pairs[1].Word);
            // pairs[2] = jal — the encoded immediate is the target address >> 2; we just check the opcode here.
            Assert.AreEqual(0xCu, pairs[2].Word >> 26, "third word should be a jal (opcode 0x03 << 26 = 0x0C000000)");
            Assert.AreEqual(MipsAssembler.Assemble("nop"), pairs[3].Word);
        }

        [TestMethod]
        public void ExternFunction_DeclarationLinesAreStripped()
        {
            // The declaration itself must not leak into emitted code.
            var src = @"
extern function noop_fn(a) at :nf_label
address $00200000
nf_label:
jr $ra
nop
";
            var pairs = Compile(src);
            Assert.AreEqual(2, pairs.Count); // jr + nop only
        }

        [TestMethod]
        public void ExternFunction_SkipsMoveWhenArgAlreadyInTargetRegister()
        {
            var src = @"
extern function f(x, y) at :f_label
address $00200000
f(a0, a1)
f_label:
nop
";
            var pairs = Compile(src);
            // Both args already in their targets → no addu needed; just jal + nop + nop.
            Assert.AreEqual(3, pairs.Count);
            Assert.AreEqual(0xCu, pairs[0].Word >> 26, "first word should be jal");
        }

        [TestMethod]
        public void ExternFunction_BreaksRegisterCycleWithAt()
        {
            // Calling f(a1, a0) requires a swap; expander should route through $at.
            var src = @"
extern function f(x, y) at :f_label
address $00200000
f(a1, a0)
f_label:
nop
";
            var pairs = Compile(src);
            // Expected sequence:
            //   addu $at, $a1, $zero   // save $a1
            //   addu $a0, $at, $zero   // a0 = old a1
            //   addu $a1, $a0, $zero   // (rewritten? no — a0 was clobbered, so this would now read new a0)
            // Actually our cycle-break rewrites: any move whose source is the cycled reg now reads from $at.
            // Sequence becomes:
            //   addu $at, $a1, $zero
            //   addu $a1, $a0, $zero    (target a1, source a0 unchanged)
            //   addu $a0, $at, $zero    (target a0, source rewritten to $at)
            // Then jal + nop + nop.
            Assert.AreEqual(5, pairs.Count);
            Assert.AreEqual(MipsAssembler.Assemble("addu $at, $a1, $zero"), pairs[0].Word);
        }

        [TestMethod]
        public void ExternFunction_ArityMismatchIsDiagnosed()
        {
            var diagnostics = new List<CdsDiagnostic>();
            var parser = new CdsParser();
            // Two-arg declaration but called with one arg.
            var src = "extern function f(x, y) at :flbl\naddress $00200000\nf(t0)\nflbl:\nnop\n";
            parser.Parse(src);
            Assert.IsTrue(parser.Diagnostics.Any(d =>
                d.Severity == CdsDiagnosticSeverity.Error
                && d.Message.Contains("expects 2 argument", StringComparison.OrdinalIgnoreCase)));
        }


        [TestMethod]
        public void ExternFunction_NumericAndRRegisterArgs_NormalizeToAbiNames()
        {
            var src = @"
extern function f(x, y) at :f_label
address $00200000
f($8, r9)
f_label:
nop
";
            var pairs = Compile(src);
            Assert.AreEqual(MipsAssembler.Assemble("addu $a0, $t0, $zero"), pairs[0].Word);
            Assert.AreEqual(MipsAssembler.Assemble("addu $a1, $t1, $zero"), pairs[1].Word);
        }

        [TestMethod]
        public void ExternFunction_OutOfRangeNumericRegister_IsDiagnosedBeforeAssembly()
        {
            var parser = new CdsParser();
            var src = @"
extern function f(x) at :f_label
address $00200000
f($99)
f_label:
nop
";
            parser.Parse(src);
            Assert.IsTrue(parser.Diagnostics.Any(d =>
                d.Severity == CdsDiagnosticSeverity.Error &&
                d.Code == "CDS2004"));
        }

        [TestMethod]
        public void ExternFunction_DispatchPriority_MacrosBeforeExterns()
        {
            // A macro and an extern function with the same name → macro wins.
            var src = @"
macro shared(x)
addiu $t0, $zero, 7
endmacro
extern function shared(x) at :unused_label
address $00200000
shared(a0)
unused_label:
nop
";
            var pairs = Compile(src);
            // Macro expansion: just the addiu, not a jal.
            Assert.AreEqual(2, pairs.Count); // addiu + nop (label target)
            Assert.AreEqual(MipsAssembler.Assemble("addiu $t0, $zero, 7"), pairs[0].Word);
        }

        // ------------------------------------------------------------------
        // StdLib smoke test — every .cds file must parse + compile cleanly.
        // ------------------------------------------------------------------

        [TestMethod]
        public void StdLib_AllFiles_CompileWithoutErrors()
        {
            string stdLibDir = LocateStdLibDir();
            Assert.IsTrue(Directory.Exists(stdLibDir),
                $"StdLib directory not found at {stdLibDir}");

            var files = Directory.GetFiles(stdLibDir, "*.cds")
                .Where(f =>
                {
                    var n = Path.GetFileName(f).ToLowerInvariant();
                    // Files containing `include "StdLib/..."` need parent-of-StdLib basePath
                    // and are tested separately.
                    return n != "tests.cds"
                        && n != "stdlib.cds"
                        && n != "example_function.cds";
                })
                .ToList();

            Assert.IsTrue(files.Count > 0, "Expected at least one StdLib file");

            foreach (var file in files)
            {
                var parser = new CdsParser();
                string src = File.ReadAllText(file);
                parser.Parse(src, basePath: stdLibDir);
                var errors = parser.Diagnostics
                    .Where(d => d.Severity == CdsDiagnosticSeverity.Error)
                    .ToList();
                Assert.AreEqual(0, errors.Count,
                    $"{Path.GetFileName(file)} produced {errors.Count} error(s): "
                    + string.Join(" | ", errors.Select(e => $"line {e.LineNumber}: {e.Message}")));
            }
        }

        [TestMethod]
        public void StdLib_TestsFile_CompilesWithExternFunctionCalls()
        {
            string stdLibDir = LocateStdLibDir();
            string testsPath = Path.Combine(stdLibDir, "tests.cds");
            if (!File.Exists(testsPath)) Assert.Inconclusive("tests.cds missing");

            var parser = new CdsParser();
            string src = File.ReadAllText(testsPath);
            parser.Parse(src, basePath: stdLibDir);

            var errors = parser.Diagnostics
                .Where(d => d.Severity == CdsDiagnosticSeverity.Error)
                .ToList();
            Assert.AreEqual(0, errors.Count,
                "tests.cds errors: " + string.Join(" | ",
                    errors.Select(e => $"line {e.LineNumber}: {e.Message}")));
        }

        // ------------------------------------------------------------------
        // End-to-end test for the user's example
        // ------------------------------------------------------------------

        [TestMethod]
        public void EndToEnd_StrCopyCallSiteFromUserExample()
        {
            // Models the user's intended call-site syntax. Uses a stub label
            // for `lib_strcpy` so we don't have to pull in the whole stdlib.
            var src = @"
extern function str_copy(dst, src) at :lib_strcpy
address $200a0000
lui t0, $0000
addiu t0, t0, $0000
lui t1, $0000
addiu t1, t1, $0010
str_copy(t0, t1)
lib_strcpy:
jr $ra
nop
";
            var parser = new CdsParser();
            parser.Parse(src);
            var errors = parser.Diagnostics
                .Where(d => d.Severity == CdsDiagnosticSeverity.Error)
                .ToList();
            Assert.AreEqual(0, errors.Count,
                "User example errors: " + string.Join(" | ",
                    errors.Select(e => $"line {e.LineNumber}: {e.Message}")));

            var pairs = Compile(src);
            // 4 setup + (addu $a0, $t0, 0; addu $a1, $t1, 0; jal; nop) + (jr $ra; nop) = 10
            Assert.AreEqual(10, pairs.Count);
            Assert.AreEqual(MipsAssembler.Assemble("addu $a0, $t0, $zero"), pairs[4].Word);
            Assert.AreEqual(MipsAssembler.Assemble("addu $a1, $t1, $zero"), pairs[5].Word);
        }



        [TestMethod]
        public void La_AllowsBareLabelReferenceForStdLibCompatibility()
        {
            var src = @"
address $00200000
target:
nop
la $t0, target
";
            var parser = new CdsParser();
            var items = parser.Parse(src);
            var errors = parser.Diagnostics
                .Where(d => d.Severity == CdsDiagnosticSeverity.Error)
                .ToList();
            Assert.AreEqual(0, errors.Count,
                "errors: " + string.Join(" | ", errors.Select(e => $"line {e.LineNumber}: {e.Message}")));

            var la = items.OfType<LaSyntaxItem>().Single();
            Assert.AreEqual("target", la.ResolvedLabel);
            Assert.AreEqual(0x00200000u, la.Value);
        }

        [TestMethod]
        public void Operation_CommaLabelOperand_ResolvesBranchTarget()
        {
            var src = @"
address $00200000
beq $a0, $zero, :done
nop
done:
nop
";
            var parser = new CdsParser();
            var items = parser.Parse(src);
            var errors = parser.Diagnostics
                .Where(d => d.Severity == CdsDiagnosticSeverity.Error)
                .ToList();
            Assert.AreEqual(0, errors.Count,
                "errors: " + string.Join(" | ", errors.Select(e => $"line {e.LineNumber}: {e.Message}")));

            var branch = items.OfType<OperationSyntaxItem>().First();
            Assert.AreEqual("done", branch.ResolvedLabel);
            Assert.AreEqual(MipsAssembler.Assemble("beq $a0, $zero, $0001"), branch.EncodedWord);
        }


        [TestMethod]
        public void Operation_BareCommaLabelOperand_ResolvesBranchTarget()
        {
            var src = @"
address $00200000
beq $a0, $zero, done
nop
done:
nop
";
            var parser = new CdsParser();
            var items = parser.Parse(src);
            var errors = parser.Diagnostics
                .Where(d => d.Severity == CdsDiagnosticSeverity.Error)
                .ToList();
            Assert.AreEqual(0, errors.Count,
                "errors: " + string.Join(" | ", errors.Select(e => $"line {e.LineNumber}: {e.Message}")));

            var branch = items.OfType<OperationSyntaxItem>().First();
            Assert.AreEqual("done", branch.ResolvedLabel);
            Assert.AreEqual(MipsAssembler.Assemble("beq $a0, $zero, $0001"), branch.EncodedWord);
        }

        [TestMethod]
        public void Operation_UnresolvedColonBranch_DoesNotEmitMissingOperandAssemblyError()
        {
            var src = @"
address $00200000
beq $a0, $zero, :missing_label
nop
";
            var parser = new CdsParser();
            parser.Parse(src);

            var diagnostics = parser.Diagnostics.ToList();
            Assert.IsTrue(diagnostics.Any(d => d.Code == "CDS0001"),
                "Expected unresolved-label diagnostic");
            Assert.IsFalse(diagnostics.Any(d => d.Code == "CDS0002"
                && d.Message.Contains("Expected 3 operands", StringComparison.OrdinalIgnoreCase)),
                "Unresolved labels should not also produce a missing-operand assembler error");
        }

        [TestMethod]
        public void StdLib_Allocator_UsesLaResolvedHeapStateStorage()
        {
            string stdLibDir = LocateStdLibDir();
            string allocatorPath = Path.Combine(stdLibDir, "allocator.cds");
            if (!File.Exists(allocatorPath)) Assert.Inconclusive("allocator.cds missing");

            var parser = new CdsParser();
            parser.Parse(File.ReadAllText(allocatorPath), basePath: stdLibDir);
            var errors = parser.Diagnostics
                .Where(d => d.Severity == CdsDiagnosticSeverity.Error)
                .ToList();
            Assert.AreEqual(0, errors.Count,
                "allocator.cds errors: " + string.Join(" | ",
                    errors.Select(e => $"line {e.LineNumber}: {e.Message}")));
        }

        // ------------------------------------------------------------------
        // Native function definitions: `function NAME(p1, p2) { body }`
        // ------------------------------------------------------------------

        [TestMethod]
        public void FunctionDef_LeafFunction_HasNoFrame()
        {
            // No callee-saved registers used, no jal — leaf function emits only
            // the body, then jr $ra + nop.
            var src = @"
address $00200000
function leaf() {
    nop
}
";
            var pairs = Compile(src);
            Assert.AreEqual(3, pairs.Count);
            Assert.AreEqual(MipsAssembler.Assemble("nop"),    pairs[0].Word);
            Assert.AreEqual(MipsAssembler.Assemble("jr $ra"), pairs[1].Word);
            Assert.AreEqual(MipsAssembler.Assemble("nop"),    pairs[2].Word);
        }

        [TestMethod]
        public void FunctionDef_TouchingS0_SavesS0InFrame()
        {
            var src = @"
address $00200000
function uses_s0() {
    addiu $s0, $zero, 1
}
";
            var pairs = Compile(src);
            // Frame size is 16 (1 reg * 4 = 4, rounded up to 16).
            Assert.AreEqual(MipsAssembler.Assemble("addiu $sp, $sp, -16"), pairs[0].Word);
            Assert.AreEqual(MipsAssembler.Assemble("sw $s0, 0($sp)"),     pairs[1].Word);
            Assert.AreEqual(MipsAssembler.Assemble("addiu $s0, $zero, 1"), pairs[2].Word);
            Assert.AreEqual(MipsAssembler.Assemble("lw $s0, 0($sp)"),     pairs[3].Word);
            Assert.AreEqual(MipsAssembler.Assemble("addiu $sp, $sp, 16"), pairs[4].Word);
            Assert.AreEqual(MipsAssembler.Assemble("jr $ra"),             pairs[5].Word);
        }

        [TestMethod]
        public void FunctionDef_BodyHasJal_SavesRa()
        {
            var src = @"
address $00200000
function calls_other() {
    jal :other
    nop
}
other:
jr $ra
nop
";
            var pairs = Compile(src);
            Assert.AreEqual(MipsAssembler.Assemble("addiu $sp, $sp, -16"), pairs[0].Word);
            Assert.AreEqual(MipsAssembler.Assemble("sw $ra, 0($sp)"),     pairs[1].Word);
            // pairs[2] = jal, pairs[3] = nop (delay slot)
            Assert.AreEqual(0x03u, pairs[2].Word >> 26);
            Assert.AreEqual(MipsAssembler.Assemble("lw $ra, 0($sp)"),     pairs[4].Word);
            Assert.AreEqual(MipsAssembler.Assemble("addiu $sp, $sp, 16"), pairs[5].Word);
            Assert.AreEqual(MipsAssembler.Assemble("jr $ra"),             pairs[6].Word);
        }


        [TestMethod]
        public void FunctionDef_BareCalleeSavedRegister_IsSavedInFrame()
        {
            var src = @"
address $00200000
function uses_bare_s0() {
    addiu s0, zero, 1
}
";
            var pairs = Compile(src);
            Assert.AreEqual(MipsAssembler.Assemble("addiu $sp, $sp, -16"), pairs[0].Word);
            Assert.AreEqual(MipsAssembler.Assemble("sw $s0, 0($sp)"), pairs[1].Word);
            Assert.AreEqual(MipsAssembler.Assemble("lw $s0, 0($sp)"), pairs[3].Word);
        }

        [TestMethod]
        public void FunctionDef_NumericCalleeSavedRegister_IsSavedAsCanonicalAbiName()
        {
            var src = @"
address $00200000
function uses_numeric_s0() {
    addiu $16, $zero, 1
}
";
            var pairs = Compile(src);
            Assert.AreEqual(MipsAssembler.Assemble("sw $s0, 0($sp)"), pairs[1].Word);
            Assert.AreEqual(MipsAssembler.Assemble("lw $s0, 0($sp)"), pairs[3].Word);
        }


        [TestMethod]
        public void FunctionDef_BareImmediateMatchingSavedRegisterIndex_DoesNotCreateFrame()
        {
            var src = @"
address $00200000
function immediate_only() {
    addiu $t0, $zero, 16
}
";
            var pairs = Compile(src);
            Assert.AreEqual(MipsAssembler.Assemble("addiu $t0, $zero, 16"), pairs[0].Word);
            Assert.AreEqual(MipsAssembler.Assemble("jr $ra"), pairs[1].Word);
        }

        [TestMethod]
        public void FunctionDef_FrameSize_RoundsUpTo16ByteMultiple()
        {
            // 6 saved regs * 4 bytes = 24 → rounded up to 32 per PS2 EE ABI.
            var src = @"
address $00200000
function many_locals() {
    jal :other
    nop
    addu $s0, $a0, $zero
    addu $s1, $a1, $zero
    addu $s2, $s0, $s1
    addu $s3, $s2, $s2
    addu $s4, $s3, $s3
}
other:
jr $ra
nop
";
            var pre = new CdsPreprocessor();
            string expanded = pre.Process(src);
            StringAssert.Contains(expanded, "addiu $sp, $sp, -32");
            StringAssert.Contains(expanded, "addiu $sp, $sp, 32");
        }

        [TestMethod]
        public void FunctionDef_ParametersAliasToArgRegisters()
        {
            var src = @"
address $00200000
function add_two(x, y) {
    addu $v0, x, y
}
";
            var pairs = Compile(src);
            Assert.AreEqual(MipsAssembler.Assemble("addu $v0, $a0, $a1"), pairs[0].Word);
        }

        [TestMethod]
        public void FunctionDef_CallableFromSameDocument()
        {
            var src = @"
address $00200000
function double_it(n) {
    addu $v0, n, n
}
double_it(t0)
";
            var pairs = Compile(src);
            // Function body (3 words) + caller (3 words: addu, jal, nop)
            Assert.AreEqual(6, pairs.Count);
            Assert.AreEqual(MipsAssembler.Assemble("addu $v0, $a0, $a0"),    pairs[0].Word);
            Assert.AreEqual(MipsAssembler.Assemble("addu $a0, $t0, $zero"), pairs[3].Word);
        }

        [TestMethod]
        public void FunctionDef_ForwardCall_BeforeDefinition_Resolves()
        {
            var src = @"
address $00200000
my_fn(t0)
function my_fn(n) {
    addu $v0, n, n
}
";
            var parser = new CdsParser();
            parser.Parse(src);
            var errors = parser.Diagnostics
                .Where(d => d.Severity == CdsDiagnosticSeverity.Error)
                .ToList();
            Assert.AreEqual(0, errors.Count,
                "errors: " + string.Join(" | ",
                    errors.Select(e => $"line {e.LineNumber}: {e.Message}")));
        }

        [TestMethod]
        public void FunctionDef_TooManyParameters_IsError()
        {
            var src = @"
address $00200000
function five(a, b, c, d, e) {
    nop
}
";
            var parser = new CdsParser();
            parser.Parse(src);
            Assert.IsTrue(parser.Diagnostics.Any(d =>
                d.Severity == CdsDiagnosticSeverity.Error
                && d.Message.Contains("at most 4", StringComparison.OrdinalIgnoreCase)));
        }

        // ------------------------------------------------------------------
        // Utility
        // ------------------------------------------------------------------

        /// <summary>
        /// The StdLib folder ships next to the CdsLanguage source. Walk up from the
        /// test binary's directory until we find the repo root (CodeDesigner.Ps2.CdsLanguage).
        /// </summary>
        private static string LocateStdLibDir()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                string candidate = Path.Combine(dir, "CodeDesigner.Ps2.CdsLanguage", "StdLib");
                if (Directory.Exists(candidate)) return candidate;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            // Fall through to a path that doesn't exist so the assertion in the caller fires.
            return Path.Combine(AppContext.BaseDirectory, "CodeDesigner.Ps2.CdsLanguage", "StdLib");
        }
    }
}
