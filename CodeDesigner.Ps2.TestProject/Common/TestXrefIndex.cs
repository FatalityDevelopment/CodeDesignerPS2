using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Bridge.Services;
using CodeDesigner.Ps2.Common;
using static CodeDesigner.Ps2.Bridge.Services.XrefIndexService;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>The bridge's cross-reference index builder.</summary>
    [TestClass]
    public class TestXrefIndex
    {
        private const uint Base = 0x00100000;

        private static byte[] Words(params uint[] words)
        {
            var bytes = new byte[words.Length * 4];
            for (int i = 0; i < words.Length; i++) BitConverter.GetBytes(words[i]).CopyTo(bytes, i * 4);
            return bytes;
        }

        private static List<(uint Target, uint From, XrefKind Kind)> Index(params uint[] words) =>
            IndexBytes(Base, Words(words)).ToList();

        [TestMethod]
        public void Jal_IsACallToItsTarget()
        {
            var refs = Index(MipsAssembler.Assemble("jal 0x00200000"), 0);
            CollectionAssert.Contains(refs, (0x00200000u, Base, XrefKind.Call));
        }

        [TestMethod]
        public void Branch_TargetUsesRowAddress()
        {
            // beq zero, zero, +2 at Base+4 → Base + 4 + 4 + 8
            var refs = Index(0, MipsAssembler.Assemble("beq $zero, $zero, 2"), 0);
            CollectionAssert.Contains(refs, (Base + 0x10, Base + 4, XrefKind.Jump));
        }

        [TestMethod]
        public void LuiAddiu_AndLoad_AreIndexed()
        {
            var refs = Index(
                MipsAssembler.Assemble("lui $a0, 0x0030"),
                MipsAssembler.Assemble("addiu $a0, $a0, -16"),
                MipsAssembler.Assemble("lw $v0, 8($a0)"),
                MipsAssembler.Assemble("sw $v0, 0x1234($a0)"));
            CollectionAssert.Contains(refs, (0x002FFFF0u, Base + 4, XrefKind.Address));
            CollectionAssert.Contains(refs, (0x002FFFF8u, Base + 8, XrefKind.Load));
            CollectionAssert.Contains(refs, (0x00301224u, Base + 12, XrefKind.Store));
        }

        [TestMethod]
        public void Kseg0Pointer_FoldsToPhysical()
        {
            var refs = Index(0x80123450u);
            CollectionAssert.Contains(refs, (0x00123450u, Base, XrefKind.Pointer));
        }

        [TestMethod]
        public void SmallIntegers_AreNotPointers()
        {
            var refs = Index(0x00000400u, 0x00001000u);
            Assert.IsFalse(refs.Any(r => r.Kind == XrefKind.Pointer));
        }

        [TestMethod]
        public void RegisterOverwrite_StopsTracking()
        {
            var refs = Index(
                MipsAssembler.Assemble("lui $a0, 0x0030"),
                MipsAssembler.Assemble("move $a0, $a1"),
                MipsAssembler.Assemble("lw $v0, 8($a0)"));
            Assert.IsFalse(refs.Any(r => r.Kind == XrefKind.Load));
        }

        private const uint JrRa = 0x03E00008;
        private static uint A(string line) => MipsAssembler.Assemble(line);

        [TestMethod]
        public void Functions_FromCallTargetsAndPrologues()
        {
            // 0x100000: caller            jal 0x100010; nop; jr ra; nop
            // 0x100010: callee (called)   addiu sp,-16; jr ra; addiu sp,16; nop (padding)
            // 0x100020: uncalled function opening a frame after jr ra + padding
            var found = XrefIndexService.FunctionsInBytes(Base, Words(
                A("jal 0x00100010"), 0, JrRa, 0,
                A("addiu $sp, $sp, -16"), JrRa, A("addiu $sp, $sp, 16"), 0,
                A("addiu $sp, $sp, -32"), JrRa, A("addiu $sp, $sp, 32")));
            CollectionAssert.Contains(found.ToList(), (Base + 0x10, Base + 0x18));   // padding nop trimmed
            CollectionAssert.Contains(found.ToList(), (Base + 0x20, Base + 0x28));
        }

        [TestMethod]
        public void SwitchTable_IsFoundWithItsBoundAndCases()
        {
            // 0x100000: sltiu at, a0, 3 ; beq at, zero, +... ; sll t0, a0, 2 ; lui t1, 0x10
            //           addu t1, t1, t0 ; lw t1, 0x40(t1) ; jr t1 ; nop
            // 0x100040: table of 3 case targets (in this code)
            var words = new uint[0x14];
            words[0] = A("sltiu $at, $a0, 3");
            words[1] = A("beq $at, $zero, 10");
            words[2] = A("sll $t0, $a0, 2");
            words[3] = A("lui $t1, 0x0010");
            words[4] = A("addu $t1, $t1, $t0");
            words[5] = A("lw $t1, 0x40($t1)");
            words[6] = A("jr $t1");
            words[0x10] = Base + 0x20;
            words[0x11] = Base + 0x24;
            words[0x12] = Base + 0x28;
            words[0x13] = Base + 0x2C;   // beyond the sltiu bound: not part of the table

            var tables = XrefIndexService.TablesInBytes(Base, Words(words));
            CollectionAssert.Contains(tables.ToList(), new DataTable(Base + 0x40, 3, DataTableKind.Jump));
            var refs = IndexBytes(Base, Words(words));
            CollectionAssert.Contains(refs.ToList(), (Base + 0x24, Base + 0x44, XrefKind.Case));
        }

        [TestMethod]
        public void PointerTable_NeedsAReferenceToItsStart()
        {
            uint[] table = [0x00200000, 0x00200010, 0x00200020, 0x00200030];
            var unreferenced = XrefIndexService.TablesInBytes(Base, Words([0, .. table]));
            Assert.AreEqual(0, unreferenced.Count, "no code points at it");

            uint[] words = [A("lui $a0, 0x0010"), A("addiu $a0, $a0, 0x10"), JrRa, 0, .. table];
            var referenced = XrefIndexService.TablesInBytes(Base, Words(words));
            CollectionAssert.Contains(referenced.ToList(), new DataTable(Base + 0x10, 4, DataTableKind.Pointer));
        }

        [TestMethod]
        public void FloatTable_StartsAtAnLwc1Target()
        {
            var words = new uint[]
            {
                A("lui $a0, 0x0010"), A("lwc1 $f0, 0x10($a0)"), JrRa, 0,
                BitConverter.SingleToUInt32Bits(1.5f), BitConverter.SingleToUInt32Bits(-2f), 0, 0x7FC00000,
            };
            var tables = XrefIndexService.TablesInBytes(Base, Words(words));
            CollectionAssert.Contains(tables.ToList(), new DataTable(Base + 0x10, 3, DataTableKind.Float));
        }

        [TestMethod]
        public void Analyzer_ResetsStateAtFunctionStart()
        {
            // a0 is set in the previous function; the next function must not see it.
            var rows = new List<(uint, uint)>
            {
                (A("lui $a0, 0x0030"), Base),
                (A("addiu $a1, $a0, 4"), Base + 4),        // same function: known
                (A("addiu $a1, $a0, 4"), Base + 8),        // new function: unknown
            };
            var comments = MipsAnalyzer.AnalyzeInstructions(rows, functionStarts: new HashSet<uint> { Base + 8 });
            StringAssert.Contains(comments.GetValueOrDefault(Base + 4, ""), "0x00300004");
            Assert.AreEqual("", comments.GetValueOrDefault(Base + 8, ""));
        }

        [TestMethod]
        public void IopCode_MapsTargetsIntoTheMirror()
        {
            uint iopBase = 0x1C010000;
            var refs = IndexBytes(iopBase, Words(MipsAssembler.Assemble("jal 0x00012340"), 0), iop: true).ToList();
            CollectionAssert.Contains(refs, (0x1C012340u, iopBase, XrefKind.Call));
        }
    }
}
