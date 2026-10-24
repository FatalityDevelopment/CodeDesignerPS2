using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Common;

namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestCoprocessorInstructions
    {
        [TestMethod]
        public void Test_COP0_Instructions()
        {
            // Test COP0 move instructions
            Assert.IsTrue(MipsDisassembler.Disassemble(0x40088000).Contains("mfc0"), "MFC0 should be recognized");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x40888000).Contains("mtc0"), "MTC0 should be recognized");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x40C88000).StartsWith(".word"), "CTC0 is not an EE instruction");
            
            // Test COP0 TLB instructions
            Assert.IsTrue(MipsDisassembler.Disassemble(0x42000001).Contains("tlbr"), "TLBR should be recognized");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x42000002).Contains("tlbwi"), "TLBWI should be recognized");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x42000006).Contains("tlbwr"), "TLBWR should be recognized");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x42000008).Contains("tlbp"), "TLBP should be recognized");
            
            // Test ERET
            Assert.IsTrue(MipsDisassembler.Disassemble(0x42000018).Contains("eret"), "ERET should be recognized");
        }

        [TestMethod]
        public void Test_COP1_Instructions()
        {
            // Test COP1 move instructions
            Assert.IsTrue(MipsDisassembler.Disassemble(0x44088000).Contains("mfc1"), "MFC1 should be recognized");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x44888000).Contains("mtc1"), "MTC1 should be recognized");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x44C88000).Contains("ctc1"), "CTC1 should be recognized");
            
            // Test COP1 floating-point arithmetic
            Assert.IsTrue(MipsDisassembler.Disassemble(0x46010800).Contains("add.s"), "ADD.S should be recognized");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x46010801).Contains("sub.s"), "SUB.S should be recognized");
            
            // Test COP1 load/store
            Assert.IsTrue(MipsDisassembler.Disassemble(0xC7A80064).Contains("lwc1"), "LWC1 should be recognized");
        }

        [TestMethod]
        public void Test_VU0_Instructions()
        {
            // VU0 macro SPECIAL1 functs 0x28-0x2A are vadd, vmadd, vmul (PCSX2 table order)
            Assert.AreEqual("vadd.xyzw vf0, vf0, vf0", MipsDisassembler.Disassemble(0x4BE00028), "VADD should be recognized");
            Assert.AreEqual("vmadd.xyzw vf0, vf0, vf0", MipsDisassembler.Disassemble(0x4BE00029), "VMADD should be recognized");
            Assert.AreEqual("vmul.xyzw vf0, vf0, vf0", MipsDisassembler.Disassemble(0x4BE0002A), "VMUL should be recognized");
        }

        [TestMethod]
        public void Test_Extended_Shift_Instructions()
        {
            // Test 32-bit extended shift instructions
            Assert.IsTrue(MipsDisassembler.Disassemble(0x00094FFC).Contains("dsll32"), "DSLL32 should be recognized");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x00094FFE).Contains("dsrl32"), "DSRL32 should be recognized");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x00094FFF).Contains("dsra32"), "DSRA32 should be recognized");
        }

        [TestMethod]
        public void Test_Branch_And_Link_Instructions()
        {
            // Test REGIMM branch-and-link instructions
            Assert.IsTrue(MipsDisassembler.Disassemble(0x04100010).Contains("bltzal"), "BLTZAL should be recognized");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x04110010).StartsWith("bal "), "BGEZAL $zero should show as bal");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x04910010).Contains("bgezal"), "BGEZAL should be recognized");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x04120010).Contains("bltzall"), "BLTZALL should be recognized");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x04130010).Contains("bgezall"), "BGEZALL should be recognized");
        }

        [TestMethod]
        public void Test_Backward_Compatibility()
        {
            // Ensure existing instructions still work
            Assert.IsTrue(MipsDisassembler.Disassemble(0x02114020).Contains("add"), "ADD should still work");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x21090064).Contains("addi"), "ADDI should still work");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x08100000).Contains("j"), "J should still work");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x11090010).Contains("beq"), "BEQ should still work");
        }
    }
}
