using CodeDesigner.Ps2.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestNewFormat
    {
        #region Hex Dump Format Tests

        [TestMethod]
        public void TestFormatHexDump()
        {
            uint addr = 0x200A0038;
            uint instr = 0x27BDFFD0;
            
            string formatted = MipsFormatter.FormatHexDump(addr, instr);
            
            Assert.AreEqual("200A0038 27BDFFD0", formatted);
        }

        [TestMethod]
        public void TestParseHexDump()
        {
            string line = "200A0038 27BDFFD0";
            
            var (addr, instr) = MipsFormatter.ParseHexDump(line);
            
            Assert.AreEqual(0x200A0038u, addr);
            Assert.AreEqual(0x27BDFFD0u, instr);
        }

        [TestMethod]
        public void TestParseHexDumpBlock()
        {
            string[] lines = new[]
            {
                "200A0038 27BDFFD0",
                "200A003C AFBF0000",
                "# This is a comment",
                "",
                "200A0040 3C08000A"
            };
            
            var parsed = MipsFormatter.ParseHexDump(lines);
            
            Assert.AreEqual(3, parsed.Length);
            Assert.AreEqual(0x200A0038u, parsed[0].address);
            Assert.AreEqual(0x27BDFFD0u, parsed[0].instruction);
        }

        #endregion

        #region Immediate Format Tests

        [TestMethod]
        public void TestImmediateFormatWithDollarPrefix()
        {
            // Immediates use the $ prefix; sign-extended ones print signed.
            uint instr = 0x27BDFFD0;  // addiu sp, sp, -$0030
            string disasm = MipsDisassembler.Disassemble(instr);

            Assert.AreEqual("addiu sp, sp, -$0030", disasm);
            Assert.AreEqual(instr, MipsAssembler.Assemble(disasm), "signed spelling must re-assemble");
        }

        [TestMethod]
        public void TestLoadStoreOffsetFormat()
        {
            // Test load/store instructions use $offset(base) format
            uint instr = 0xAFBF0000;  // sw ra, $0000(sp)
            string disasm = MipsDisassembler.Disassemble(instr);
            
            Assert.IsTrue(disasm.Contains("$0000(sp)"), $"Expected $0000(sp) in disassembly but got: {disasm}");
        }

        [TestMethod]
        public void TestLUIFormat()
        {
            // Test LUI uses $ prefix
            uint instr = 0x3C08000A;  // lui t0, $000A
            string disasm = MipsDisassembler.Disassemble(instr);
            
            Assert.IsTrue(disasm.Contains("$000A"), $"Expected $000A in disassembly but got: {disasm}");
        }

        [TestMethod]
        public void TestORIFormat()
        {
            // Test ORI uses $ prefix with 4-digit hex
            uint instr = 0x35080000;  // ori t0, t0, $0000
            string disasm = MipsDisassembler.Disassemble(instr);
            
            Assert.IsTrue(disasm.Contains("$0000"), $"Expected $0000 in disassembly but got: {disasm}");
        }

        #endregion

        #region Assembler Tests with New Format

        [TestMethod]
        public void TestAssembleWithDollarPrefix()
        {
            // Test assembling with $ prefix for immediates
            uint encoded = MipsAssembler.Assemble("addiu sp, sp, $FFD0");
            
            Assert.AreEqual(0x27BDFFD0u, encoded);
        }

        [TestMethod]
        public void TestAssembleLoadStoreWithOffsetFormat()
        {
            // Test assembling load/store with offset(base) format
            uint encoded1 = MipsAssembler.Assemble("sw ra, $0000(sp)");
            uint encoded2 = MipsAssembler.Assemble("lw t0, $0064(s0)");
            
            Assert.AreEqual(0xAFBF0000u, encoded1);
            Assert.AreEqual(0x8E080064u, encoded2);
        }

        [TestMethod]
        public void TestAssembleWithBothFormats()
        {
            // Test that both 0x and $ prefix work
            uint encoded1 = MipsAssembler.Assemble("addiu t0, zero, 0x1234");
            uint encoded2 = MipsAssembler.Assemble("addiu t0, zero, $1234");
            
            Assert.AreEqual(encoded1, encoded2, "Both 0x and $ prefix should produce same encoding");
        }

        #endregion

        #region Problem Statement Sample Tests

        [TestMethod]
        public void TestSampleInstructionsFromProblemStatement()
        {
            // Test actual instructions from the problem statement
            string[] hexDump = new[]
            {
                "200A0038 27BDFFD0",
                "200A003C AFBF0000",
                "200A0040 3C08000A",
                "200A0044 35080000",
                "200A0048 24090004",
                "200A004C 00094880",
                "200A0050 01094020",
                "200A0054 8D080000"
            };
            
            var disassembled = MipsFormatter.DisassembleHexDump(hexDump);
            
            Assert.AreEqual(8, disassembled.Length);
            
            // Verify first instruction: addiu sp, sp, $FFD0
            Assert.IsTrue(disassembled[0].Contains("200A0038"));
            Assert.IsTrue(disassembled[0].Contains("27BDFFD0"));
            Assert.IsTrue(disassembled[0].Contains("addiu"));
            Assert.IsTrue(disassembled[0].Contains("sp"));
            Assert.IsTrue(disassembled[0].Contains("-$0030"));
        }

        [TestMethod]
        public void TestRoundTripWithNewFormat()
        {
            // Test round-trip: assemble -> disassemble with new format
            string[] originalAsm = new[]
            {
                "addiu sp, sp, $FFD0",
                "sw ra, $0000(sp)",
                "lui t0, $000A",
                "ori t0, t0, $0000",
                "addiu t1, zero, $0004"
            };
            
            uint address = 0x200A0038;
            
            foreach (var line in originalAsm)
            {
                uint encoded = MipsAssembler.Assemble(line);
                string disasm = MipsDisassembler.Disassemble(encoded, address);
                
                // Verify the disassembly contains the key parts
                Assert.IsTrue(disasm.Length > 0, $"Disassembly should not be empty for: {line}");
                
                // Re-assemble the disassembled instruction
                try
                {
                    uint reencoded = MipsAssembler.Assemble(disasm);
                    Assert.AreEqual(encoded, reencoded, $"Round-trip failed for: {line}");
                }
                catch (Exception ex)
                {
                    Assert.Fail($"Failed to re-assemble '{disasm}' from '{line}': {ex.Message}");
                }
                
                address += 4;
            }
        }

        #endregion

        #region Comprehensive Format Tests

        [TestMethod]
        public void TestAllITypeImmediatesUseDollarPrefix()
        {
            // Test various I-type instructions
            var testCases = new[]
            {
                new { asmLine = "addiu t0, t1, $1234", mnemonic = "addiu", expected = "$1234" },
                new { asmLine = "addi t1, t0, $0064", mnemonic = "addi", expected = "$0064" },
                new { asmLine = "andi t2, t1, $00FF", mnemonic = "andi", expected = "$00FF" },
                new { asmLine = "ori t3, t2, $1000", mnemonic = "ori", expected = "$1000" },
                new { asmLine = "xori t4, t3, $ABCD", mnemonic = "xori", expected = "$ABCD" },
                new { asmLine = "slti t5, t4, $0100", mnemonic = "slti", expected = "$0100" }
            };
            
            foreach (var testCase in testCases)
            {
                uint encoded = MipsAssembler.Assemble(testCase.asmLine);
                string disasm = MipsDisassembler.Disassemble(encoded);
                
                Assert.IsTrue(disasm.Contains(testCase.mnemonic), $"Expected {testCase.mnemonic} in: {disasm}");
                Assert.IsTrue(disasm.Contains("$"), $"Expected $ prefix in: {disasm}");
            }
        }

        [TestMethod]
        public void TestAllLoadStoreUseDollarPrefixOffset()
        {
            // Test various load/store instructions
            string[] testCases = new[]
            {
                "lw t0, $0100(s0)",
                "sw t1, $0200(s1)",
                "lb t2, $0004(s2)",
                "sb t3, $0008(s3)",
                "lh t4, $0010(s4)",
                "sh t5, $0020(s5)"
            };
            
            foreach (var asmLine in testCases)
            {
                uint encoded = MipsAssembler.Assemble(asmLine);
                string disasm = MipsDisassembler.Disassemble(encoded);
                
                Assert.IsTrue(disasm.Contains("("), $"Expected (base) format in: {disasm}");
                Assert.IsTrue(disasm.Contains("$"), $"Expected $ prefix in offset: {disasm}");
            }
        }

        #endregion
    }
}
