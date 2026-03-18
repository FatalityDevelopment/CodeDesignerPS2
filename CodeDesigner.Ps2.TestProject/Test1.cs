using CodeDesigner.Ps2.Common;

namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public sealed class Test1
    {
        #region Encoder/Decoder Tests

        [TestMethod]
        public void TestRTypeEncoding()
        {
            // Test R-type instruction: ADD $t0, $s0, $s1
            // opcode=0, rs=16 ($s0), rt=17 ($s1), rd=8 ($t0), shamt=0, funct=0x20
            uint encoded = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x20);
            
            Assert.AreEqual(0x02114020u, encoded, "R-type encoding failed");
        }

        [TestMethod]
        public void TestRTypeDecoding()
        {
            // Decode the ADD instruction
            uint instruction = 0x02114020u; // ADD $t0, $s0, $s1
            var decoded = MipsDecoder.DecodeR(instruction);
            
            Assert.AreEqual(16, decoded.Rs, "Rs register mismatch");
            Assert.AreEqual(17, decoded.Rt, "Rt register mismatch");
            Assert.AreEqual(8, decoded.Rd, "Rd register mismatch");
            Assert.AreEqual(0, decoded.Shamt, "Shamt mismatch");
            Assert.AreEqual(0x00, MipsDecoder.GetOpcode(instruction), "Opcode mismatch");
            Assert.AreEqual(0x20, MipsDecoder.GetFunct(instruction), "Funct mismatch");
        }

        [TestMethod]
        public void TestITypeEncoding()
        {
            // Test I-type instruction: ADDI $t1, $t0, 0x1234
            // opcode=0x08, rs=8 ($t0), rt=9 ($t1), imm=0x1234
            uint encoded = MipsEncoder.EncodeI(0x08, 8, 9, 0x1234);
            
            Assert.AreEqual(0x21091234u, encoded, "I-type encoding failed");
        }

        [TestMethod]
        public void TestITypeDecoding()
        {
            // Decode ADDI instruction
            uint instruction = 0x21091234u; // ADDI $t1, $t0, 0x1234
            var decoded = MipsDecoder.DecodeI(instruction);
            
            Assert.AreEqual(8, decoded.Rs, "Rs register mismatch");
            Assert.AreEqual(9, decoded.Rt, "Rt register mismatch");
            Assert.AreEqual(0x1234, decoded.Imm, "Immediate value mismatch");
            Assert.AreEqual(0x08, MipsDecoder.GetOpcode(instruction), "Opcode mismatch");
        }

        [TestMethod]
        public void TestJTypeEncoding()
        {
            // Test J-type instruction: J 0x100000
            // opcode=0x02, addr=0x100000
            uint encoded = MipsEncoder.EncodeJ(0x02, 0x100000);
            
            Assert.AreEqual(0x08100000u, encoded, "J-type encoding failed");
        }

        [TestMethod]
        public void TestJTypeDecoding()
        {
            // Decode J instruction
            uint instruction = 0x08100000u; // J 0x100000
            var decoded = MipsDecoder.DecodeJ(instruction);
            
            Assert.AreEqual(0x100000u, decoded.Addr, "Address mismatch");
            Assert.AreEqual(0x02, MipsDecoder.GetOpcode(instruction), "Opcode mismatch");
        }

        [TestMethod]
        public void TestRTypeRoundTrip()
        {
            // Test round-trip encoding/decoding for R-type
            int opcode = 0;
            int rs = 10;
            int rt = 11;
            int rd = 12;
            int shamt = 5;
            int funct = 0x21;

            uint encoded = MipsEncoder.EncodeR(opcode, rs, rt, rd, shamt, funct);
            var decoded = MipsDecoder.DecodeR(encoded);
            int decodedOpcode = MipsDecoder.GetOpcode(encoded);
            int decodedFunct = MipsDecoder.GetFunct(encoded);

            Assert.AreEqual(opcode, decodedOpcode, "Opcode mismatch in round-trip");
            Assert.AreEqual(rs, decoded.Rs, "Rs mismatch in round-trip");
            Assert.AreEqual(rt, decoded.Rt, "Rt mismatch in round-trip");
            Assert.AreEqual(rd, decoded.Rd, "Rd mismatch in round-trip");
            Assert.AreEqual(shamt, decoded.Shamt, "Shamt mismatch in round-trip");
            Assert.AreEqual(funct, decodedFunct, "Funct mismatch in round-trip");
        }

        [TestMethod]
        public void TestITypeRoundTrip()
        {
            // Test round-trip encoding/decoding for I-type
            int opcode = 0x09;
            int rs = 5;
            int rt = 6;
            int imm = 0xABCD;

            uint encoded = MipsEncoder.EncodeI(opcode, rs, rt, imm);
            var decoded = MipsDecoder.DecodeI(encoded);
            int decodedOpcode = MipsDecoder.GetOpcode(encoded);

            Assert.AreEqual(opcode, decodedOpcode, "Opcode mismatch in round-trip");
            Assert.AreEqual(rs, decoded.Rs, "Rs mismatch in round-trip");
            Assert.AreEqual(rt, decoded.Rt, "Rt mismatch in round-trip");
            Assert.AreEqual(imm & 0xFFFF, decoded.Imm, "Immediate mismatch in round-trip");
        }

        [TestMethod]
        public void TestJTypeRoundTrip()
        {
            // Test round-trip encoding/decoding for J-type
            int opcode = 0x03;
            uint addr = 0x3FFFFFF;

            uint encoded = MipsEncoder.EncodeJ(opcode, addr);
            var decoded = MipsDecoder.DecodeJ(encoded);
            int decodedOpcode = MipsDecoder.GetOpcode(encoded);

            Assert.AreEqual(opcode, decodedOpcode, "Opcode mismatch in round-trip");
            Assert.AreEqual(addr & 0x03FFFFFF, decoded.Addr, "Address mismatch in round-trip");
        }

        #endregion

        #region Assembler Tests

        [TestMethod]
        public void TestAssembleRTypeArithmetic()
        {
            // Test various R-type arithmetic instructions
            Assert.AreEqual(0x02114020u, MipsAssembler.Assemble("add $t0, $s0, $s1"));
            Assert.AreEqual(0x02534821u, MipsAssembler.Assemble("addu $t1, $s2, $s3"));
            Assert.AreEqual(0x02955022u, MipsAssembler.Assemble("sub $t2, $s4, $s5"));
        }

        [TestMethod]
        public void TestAssembleRTypeLogical()
        {
            // Test R-type logical instructions
            Assert.AreEqual(0x02114024u, MipsAssembler.Assemble("and $t0, $s0, $s1"));
            Assert.AreEqual(0x02534825u, MipsAssembler.Assemble("or $t1, $s2, $s3"));
            Assert.AreEqual(0x02955026u, MipsAssembler.Assemble("xor $t2, $s4, $s5"));
            Assert.AreEqual(0x02D75827u, MipsAssembler.Assemble("nor $t3, $s6, $s7"));
        }

        [TestMethod]
        public void TestAssembleRTypeShift()
        {
            // Test R-type shift instructions
            AssertAssembly("sll $t0, $s0, 5");
            AssertAssembly("srl $t1, $s1, 3");
            AssertAssembly("sra $t2, $s2, 8");
            AssertAssembly("sllv $t3, $s3, $s4");
        }

        [TestMethod]
        public void TestAssembleRTypeCompare()
        {
            // Test R-type compare and jump instructions
            AssertAssembly("slt $t0, $s0, $s1");
            AssertAssembly("sltu $t1, $s2, $s3");
            AssertAssembly("jr $ra");
        }

        [TestMethod]
        public void TestAssembleRTypeMultDiv()
        {
            // Test multiply/divide instructions
            AssertAssembly("mult $s0, $s1");
            AssertAssembly("div $t0, $t1");
            AssertAssembly("mfhi $t0");
            AssertAssembly("mflo $t1");
        }

        [TestMethod]
        public void TestAssembleITypeArithmetic()
        {
            // Test I-type arithmetic instructions
            AssertAssembly("addi $t0, $s0, 100");
            AssertAssembly("addiu $t1, $t0, 0x1234");
            AssertAssembly("daddi $t2, $s1, -50");
        }

        [TestMethod]
        public void TestAssembleITypeLogical()
        {
            // Test I-type logical instructions
            AssertAssembly("andi $t0, $s0, 0xFF");
            AssertAssembly("ori $t1, $s1, 0x1000");
            AssertAssembly("xori $t2, $s2, 0xABCD");
        }

        [TestMethod]
        public void TestAssembleITypeBranch()
        {
            // Test I-type branch instructions
            AssertAssembly("beq $s0, $s1, 0x10");
            AssertAssembly("bne $t0, $t1, 0x20");
            AssertAssembly("blez $s0, 0x30");
            AssertAssembly("bgtz $s1, 0x40");
        }

        [TestMethod]
        public void TestAssembleITypeLoadStore()
        {
            // Test I-type load/store instructions
            AssertAssembly("lw $t0, 100, $s0");
            AssertAssembly("sw $t1, 200, $s1");
            AssertAssembly("lb $t2, 4, $s2");
            AssertAssembly("sb $t3, 8, $s3");
        }

        [TestMethod]
        public void TestAssembleITypeSpecial()
        {
            // Test I-type special instructions
            AssertAssembly("lui $t0, 0x1234");
            AssertAssembly("slti $t1, $s0, 100");
        }

        [TestMethod]
        public void TestAssembleJType()
        {
            // Test J-type instructions
            AssertAssembly("j 0x400000");
            AssertAssembly("jal 0x500000");
        }

        [TestMethod]
        public void TestAssembleWithDifferentRegisterFormats()
        {
            // Test with numeric register format
            uint encoded1 = MipsAssembler.Assemble("add $8, $16, $17");
            uint encoded2 = MipsAssembler.Assemble("add $t0, $s0, $s1");
            Assert.AreEqual(encoded2, encoded1, "Numeric and named registers should produce same encoding");

            // Test without $ prefix
            uint encoded3 = MipsAssembler.Assemble("add t0, s0, s1");
            Assert.AreEqual(encoded2, encoded3, "Registers with and without $ should produce same encoding");
        }

        [TestMethod]
        public void TestAssembleWithComments()
        {
            // Test that comments are properly ignored
            uint encoded1 = MipsAssembler.Assemble("add $t0, $s0, $s1");
            uint encoded2 = MipsAssembler.Assemble("add $t0, $s0, $s1 # This is a comment");
            Assert.AreEqual(encoded1, encoded2, "Comments should be ignored");
        }

        #endregion

        #region Disassembler Tests

        [TestMethod]
        public void TestDisassembleRType()
        {
            string disasm = MipsDisassembler.Disassemble(0x02114020u);
            Assert.IsTrue(disasm.Contains("add"), "Should disassemble to add instruction");
            Assert.IsTrue(disasm.Contains("t0") || disasm.Contains("$t0"), "Should contain t0 register");
        }

        [TestMethod]
        public void TestDisassembleIType()
        {
            string disasm = MipsDisassembler.Disassemble(0x21091234u);
            Assert.IsTrue(disasm.Contains("addi"), "Should disassemble to addi instruction");
        }

        [TestMethod]
        public void TestDisassembleJType()
        {
            string disasm = MipsDisassembler.Disassemble(0x08100000u);
            Assert.IsTrue(disasm.Contains("j"), "Should disassemble to j instruction");
        }

        [TestMethod]
        public void TestDisassembleBlock()
        {
            byte[] codeBlock = new byte[]
            {
                // add $t0, $s0, $s1 (0x02114020)
                0x20, 0x40, 0x11, 0x02,
                // addi $t1, $t0, 0x64 (0x21090064)
                0x64, 0x00, 0x09, 0x21,
                // beq $t0, $t1, 0x10 (0x11090010)
                0x10, 0x00, 0x09, 0x11,
                // j 0x100000 (0x08100000)
                0x00, 0x00, 0x10, 0x08
            };

            string[] disassembled = MipsDisassembler.DisassembleBlock(codeBlock, 0x80000000);

            Assert.AreEqual(4, disassembled.Length, "Should have 4 instructions");
            Assert.IsTrue(disassembled[0].Contains("add"), "First instruction should be add");
            Assert.IsTrue(disassembled[1].Contains("addi"), "Second instruction should be addi");
            Assert.IsTrue(disassembled[2].Contains("beq") || disassembled[2].Contains("bne"), "Third instruction should be branch");
            Assert.IsTrue(disassembled[3].Contains("j"), "Fourth instruction should be j");
        }

        #endregion

        #region Round-Trip Tests

        [TestMethod]
        public void TestRoundTripSimpleInstructions()
        {
            string[] instructions = new[]
            {
                "add $t0, $s0, $s1",
                "addi $t1, $t0, 100",
                "j 0x400000"
            };

            foreach (var instr in instructions)
            {
                uint encoded = MipsAssembler.Assemble(instr);
                string disasm = MipsDisassembler.Disassemble(encoded);

                // Normalize both for comparison
                string normalized = NormalizeAsm(instr);
                string disasmNorm = NormalizeAsm(disasm);

                Assert.AreEqual(normalized, disasmNorm, 
                    $"Round-trip failed for: {instr} => 0x{encoded:X8} => {disasm}");
            }
        }

        [TestMethod]
        public void TestRoundTripAllRTypeArithmetic()
        {
            string[] instructions = new[]
            {
                "add $t0, $s0, $s1",
                "addu $t1, $s2, $s3",
                "sub $t2, $s4, $s5",
                "subu $t3, $s6, $s7"
            };

            foreach (var instr in instructions)
            {
                VerifyRoundTrip(instr);
            }
        }

        [TestMethod]
        public void TestRoundTripAllITypeArithmetic()
        {
            string[] instructions = new[]
            {
                "addi $t0, $s0, 100",
                "addiu $t1, $t0, 200",
                "andi $t2, $s1, 0xFF",
                "ori $t3, $s2, 0x100"
            };

            foreach (var instr in instructions)
            {
                VerifyRoundTrip(instr);
            }
        }

        #endregion

        #region Helper Methods

        private void AssertAssembly(string asmLine)
        {
            try
            {
                uint encoded = MipsAssembler.Assemble(asmLine);
                Assert.IsTrue(encoded > 0 || asmLine.ToLower().Contains("nop"), 
                    $"Failed to assemble: {asmLine}");
            }
            catch (Exception ex)
            {
                Assert.Fail($"Exception assembling '{asmLine}': {ex.Message}");
            }
        }

        private void VerifyRoundTrip(string instruction)
        {
            uint encoded = MipsAssembler.Assemble(instruction);
            string disasm = MipsDisassembler.Disassemble(encoded);

            string normalized = NormalizeAsm(instruction);
            string disasmNorm = NormalizeAsm(disasm);

            Assert.AreEqual(normalized, disasmNorm,
                $"Round-trip failed:\n  Input:  {instruction}\n  Encoded: 0x{encoded:X8}\n  Disasm:  {disasm}");
        }

        private static string NormalizeAsm(string asm)
        {
            // Normalize assembly for comparison: lowercase, remove $ prefix from registers, consistent spacing
            asm = asm.ToLowerInvariant().Replace(",", " ");
            // Remove comments
            int commentIndex = asm.IndexOf('#');
            if (commentIndex >= 0)
            {
                asm = asm.Substring(0, commentIndex);
            }
            // Split and rejoin to normalize whitespace
            var parts = asm.Split(new[] { ' ', '\t', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Register names (used to avoid treating them as hex)
            var registerNames = new System.Collections.Generic.HashSet<string>
            {
                "zero", "at", "v0", "v1", "a0", "a1", "a2", "a3",
                "t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7",
                "s0", "s1", "s2", "s3", "s4", "s5", "s6", "s7",
                "t8", "t9", "k0", "k1", "gp", "sp", "fp", "ra"
            };
            
            // Normalize hex/decimal immediates: convert all numeric values to consistent format
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                
                // Skip if it's a register name (without or with $ prefix)
                string partWithoutDollar = part.TrimStart('$');
                if (registerNames.Contains(partWithoutDollar))
                {
                    parts[i] = partWithoutDollar; // Remove $ from registers
                    continue;
                }
                
                // Remove $ prefix for immediates
                bool hadDollarPrefix = false;
                if (part.StartsWith("$"))
                {
                    part = part.Substring(1);
                    hadDollarPrefix = true;
                }
                
                // Try to parse as a number (hex or decimal)
                if (part.StartsWith("0x"))
                {
                    // It's hex with 0x prefix
                    if (int.TryParse(part.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int hexValue))
                    {
                        if (hexValue < 0x10000)
                            parts[i] = $"0x{hexValue:x4}";
                        else
                            parts[i] = $"0x{hexValue:x8}";
                    }
                }
                else if (hadDollarPrefix && int.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out int hexValue))
                {
                    // It had $ prefix, so it's a hex value
                    if (hexValue < 0x10000)
                        parts[i] = $"0x{hexValue:x4}";
                    else
                        parts[i] = $"0x{hexValue:x8}";
                }
                else if (int.TryParse(part, out int decValue))
                {
                    // It's a decimal - convert to normalized hex format
                    if (decValue >= 0 && decValue < 0x10000)
                        parts[i] = $"0x{decValue:x4}";
                    else if (decValue >= 0)
                        parts[i] = $"0x{decValue:x8}";
                }
            }
            
            return string.Join(" ", parts);
        }

        #endregion

        #region Edge Case Tests

        [TestMethod]
        public void TestEncodeWithMaxValues()
        {
            // Test encoding with maximum field values
            uint encoded = MipsEncoder.EncodeR(0x3F, 0x1F, 0x1F, 0x1F, 0x1F, 0x3F);
            Assert.IsTrue(encoded > 0, "Should encode with max values");
        }

        [TestMethod]
        public void TestEncodeWithZeroValues()
        {
            // Test encoding with all zero values
            uint encoded = MipsEncoder.EncodeR(0, 0, 0, 0, 0, 0);
            Assert.AreEqual(0u, encoded, "All zeros should produce zero encoding");
        }

        [TestMethod]
        public void TestNegativeImmediate()
        {
            // Test negative immediate values
            uint encoded = MipsAssembler.Assemble("addi $t0, $s0, -50");
            Assert.IsTrue(encoded > 0, "Should handle negative immediate");
        }

        [TestMethod]
        public void TestHexImmediate()
        {
            // Test hexadecimal immediate values
            uint encoded1 = MipsAssembler.Assemble("addi $t0, $s0, 0x100");
            uint encoded2 = MipsAssembler.Assemble("addi $t0, $s0, 256");
            Assert.AreEqual(encoded1, encoded2, "Hex and decimal should produce same result");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TestInvalidInstruction()
        {
            // Should throw exception for invalid instruction
            MipsAssembler.Assemble("invalid_instruction $t0, $s0");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TestInvalidRegister()
        {
            // Should throw exception for invalid register
            MipsAssembler.Assemble("add $invalid, $s0, $s1");
        }

        #endregion

        #region Block Assembly Tests

        [TestMethod]
        public void TestAssembleBlock()
        {
            string[] asmLines = new[]
            {
                "add $t0, $s0, $s1",
                "addi $t1, $t0, 100",
                "# This is a comment",
                "",
                "j 0x400000"
            };

            uint[] encoded = MipsAssembler.AssembleBlock(asmLines);
            
            Assert.AreEqual(3, encoded.Length, "Should skip comments and empty lines");
            Assert.IsTrue(encoded[0] > 0, "First instruction should be encoded");
            Assert.IsTrue(encoded[1] > 0, "Second instruction should be encoded");
            Assert.IsTrue(encoded[2] > 0, "Third instruction should be encoded");
        }

        #endregion
    }
}
