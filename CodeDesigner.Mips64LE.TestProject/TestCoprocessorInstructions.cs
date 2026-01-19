using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Mips64LE.Common;

namespace CodeDesigner.Mips64LE.TestProject
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
            Assert.IsTrue(MipsDisassembler.Disassemble(0x40C88000).Contains("ctc0"), "CTC0 should be recognized");
            
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
            // Test VU0 vector arithmetic
            Assert.IsTrue(MipsDisassembler.Disassemble(0x4A000028).Contains("vadd"), "VADD should be recognized");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x4A000029).Contains("vsub"), "VSUB should be recognized");
            Assert.IsTrue(MipsDisassembler.Disassemble(0x4A00002A).Contains("vmul"), "VMUL should be recognized");
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
            Assert.IsTrue(MipsDisassembler.Disassemble(0x04110010).Contains("bgezal"), "BGEZAL should be recognized");
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
