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

        [TestMethod]
        public void IopCode_MapsTargetsIntoTheMirror()
        {
            uint iopBase = 0x1C010000;
            var refs = IndexBytes(iopBase, Words(MipsAssembler.Assemble("jal 0x00012340"), 0), iop: true).ToList();
            CollectionAssert.Contains(refs, (0x1C012340u, iopBase, XrefKind.Call));
        }
    }
}
