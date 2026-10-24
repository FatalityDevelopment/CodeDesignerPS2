using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.Common.Disassembler;
using CodeDesigner.Ps2.EmulatorCore;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>
    /// Comment-column hints from <see cref="MipsAnalyzer.AnalyzeInstructions"/> on a
    /// blank (cold) state: values only for registers the analyzer saw being set,
    /// real row addresses for branch targets, loads described before they run.
    /// </summary>
    [TestClass]
    public class TestAnalyzerHints
    {
        private const int Zero = 0, V1 = 3, A0 = 4, A1 = 5, T0 = 8, Sp = 29;
        private const uint Base = 0x00100000;

        private static uint Lui(int rt, int imm)              => MipsEncoder.EncodeI(0x0F, 0, rt, imm);
        private static uint Addiu(int rt, int rs, int imm)    => MipsEncoder.EncodeI(0x09, rs, rt, imm);
        private static uint Ori(int rt, int rs, int imm)      => MipsEncoder.EncodeI(0x0D, rs, rt, imm);
        private static uint Lw(int rt, int imm, int rs)       => MipsEncoder.EncodeI(0x23, rs, rt, imm);
        private static uint Beq(int rs, int rt, int offset)   => MipsEncoder.EncodeI(0x04, rs, rt, offset);
        private static uint Jal(uint target)                  => MipsEncoder.EncodeJ(0x03, (target >> 2) & 0x03FFFFFF);
        private const uint Nop = 0;
        private const uint Syscall = 0x0000000C;

        /// <summary>Lays <paramref name="words"/> out from <see cref="Base"/>, 4 bytes apart.</summary>
        private static List<(uint, uint)> Rows(params uint[] words)
        {
            var rows = new List<(uint, uint)>();
            for (int i = 0; i < words.Length; i++) rows.Add((words[i], Base + (uint)(i * 4)));
            return rows;
        }

        private static string At(Dictionary<uint, string> comments, int row) =>
            comments.GetValueOrDefault(Base + (uint)(row * 4), string.Empty);

        [TestMethod]
        public void LuiAddiu_ShowsCombinedAddressOnce()
        {
            var c = MipsAnalyzer.AnalyzeInstructions(Rows(Lui(A0, 0x30), Addiu(A0, A0, 0x1234)));
            StringAssert.Contains(At(c, 1), "0x00301234");
        }

        [TestMethod]
        public void LuiAddiu_NegativeImmediateIsSignExtended()
        {
            var c = MipsAnalyzer.AnalyzeInstructions(Rows(Lui(A0, 0x30), Addiu(A0, A0, 0xFFF0)));
            StringAssert.Contains(At(c, 1), "0x002FFFF0");
        }

        [TestMethod]
        public void UnknownSource_ProducesNoValueHint()
        {
            var c = MipsAnalyzer.AnalyzeInstructions(Rows(Addiu(A1, A0, 4)));
            Assert.AreEqual(string.Empty, At(c, 0));
        }

        [TestMethod]
        public void StackFrame_IsDescribedWithoutKnownSp()
        {
            var c = MipsAnalyzer.AnalyzeInstructions(Rows(Addiu(Sp, Sp, 0xFFC0)));
            StringAssert.Contains(At(c, 0), "allocate 64-byte stack frame");
            Assert.IsFalse(At(c, 0).Contains("$sp ="), At(c, 0));
        }

        [TestMethod]
        public void BranchTarget_UsesRealRowAddressAfterDataGap()
        {
            // Row at Base, then a 16-byte data gap, then a branch at Base + 0x14.
            var rows = new List<(uint, uint)>
            {
                (Nop, Base),
                (Beq(Zero, Zero, 1), Base + 0x14),
                (Nop, Base + 0x18),
            };
            var c = MipsAnalyzer.AnalyzeInstructions(rows);
            StringAssert.Contains(c.GetValueOrDefault(Base + 0x14, string.Empty), "0x0010001C");
        }

        [TestMethod]
        public void Load_ShowsLoadedValueNotStaleRegister()
        {
            var bus = new PS2MemoryBus();
            bus.WriteWord(0x00301000, 0xCAFEBABE);
            var c = MipsAnalyzer.AnalyzeInstructions(
                Rows(Lui(A0, 0x30), Lw(A0, 0x1000, A0)), bus: bus);
            StringAssert.Contains(At(c, 1), "0xCAFEBABE");
        }

        [TestMethod]
        public void Syscall_ResolvesNameFromLuiOriIntoV1()
        {
            string? name = EeSyscallTable.Resolve(0x3C);
            Assert.IsNotNull(name);
            var c = MipsAnalyzer.AnalyzeInstructions(Rows(Lui(V1, 0), Ori(V1, V1, 0x3C), Syscall));
            StringAssert.Contains(At(c, 2), name);
        }

        [TestMethod]
        public void Call_ShowsArgumentSetInDelaySlot()
        {
            var c = MipsAnalyzer.AnalyzeInstructions(Rows(Jal(0x00200000), Addiu(A0, Zero, 5)));
            StringAssert.Contains(At(c, 0), "a0 = 0x00000005");
        }

        [TestMethod]
        public void HintLevel_AddressesKeepsPlacesAndDropsPlainValues()
        {
            var rows = Rows(Addiu(T0, Zero, 5), Lui(A0, 0x30), Addiu(A0, A0, 0x1234), Jal(0x00200000), Nop);
            var c = MipsAnalyzer.AnalyzeInstructions(rows, hintLevel: HintLevel.Addresses);
            Assert.AreEqual(string.Empty, At(c, 0), "t0 = 5 is a plain value");
            StringAssert.Contains(At(c, 2), "0x00301234");
            StringAssert.Contains(At(c, 3), "0x00200000");
        }

        [TestMethod]
        public void HintLevel_OffShowsNothingButStillSimulates()
        {
            var rows = Rows(Lui(A0, 0x30), Addiu(A0, A0, 0x1234));
            var c = MipsAnalyzer.AnalyzeInstructions(rows, hintLevel: HintLevel.Off);
            Assert.AreEqual(0, c.Count);
        }

        [TestMethod]
        public void Call_ForgetsCallerSavedRegisters()
        {
            var c = MipsAnalyzer.AnalyzeInstructions(
                Rows(Lui(A0, 0x10), Lui(16, 0x20), Jal(0x00200000), Nop, Addiu(A1, A0, 4), Addiu(T0, 16, 4)));
            Assert.AreEqual(string.Empty, At(c, 4), "a0 is caller-saved, unknown after the call");
            StringAssert.Contains(At(c, 5), "0x00200004", "s0 is callee-saved, still known");
        }
    }
}
