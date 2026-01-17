using CodeDesigner.Mips64LE.Cpu.InstrArgs;
using System;

namespace CodeDesigner.Mips64LE
{
    class Program
    {
        static void Main(string[] args)
        {
            // Entry point for the MIPS64 Emotion Engine project
            Console.WriteLine("MIPS64 Emotion Engine - CodeDesigner");
            Console.WriteLine("VU0 (Vector Unit 0) implementation loaded.");
            Console.WriteLine();
            
            // Test MipsEncoder and MipsDecoder
            TestEncoderDecoder();

            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine();

            // Test MipsAssembler and MipsDisassembler
            TestAssemblerDisassembler();
        }

        static void TestEncoderDecoder()
        {
            Console.WriteLine("=== Testing MipsEncoder and MipsDecoder ===");
            Console.WriteLine();

            // Test R-type instruction: ADD $t0, $s0, $s1
            // opcode=0, rs=16 ($s0), rt=17 ($s1), rd=8 ($t0), shamt=0, funct=0x20
            Console.WriteLine("Test 1: R-type instruction (ADD $t0, $s0, $s1)");
            uint rInstr = MipsEncoder.EncodeR(0, 16, 17, 8, 0, 0x20);
            Console.WriteLine($"  Encoded: 0x{rInstr:X8}");
            
            var rArgs = MipsDecoder.DecodeR(rInstr);
            Console.WriteLine($"  Decoded: rs={rArgs.Rs}, rt={rArgs.Rt}, rd={rArgs.Rd}, shamt={rArgs.Shamt}");
            Console.WriteLine($"  Opcode: 0x{MipsDecoder.GetOpcode(rInstr):X2}, Funct: 0x{MipsDecoder.GetFunct(rInstr):X2}");
            Console.WriteLine();

            // Test I-type instruction: ADDI $t1, $t0, 0x1234
            // opcode=0x08, rs=8 ($t0), rt=9 ($t1), imm=0x1234
            Console.WriteLine("Test 2: I-type instruction (ADDI $t1, $t0, 0x1234)");
            uint iInstr = MipsEncoder.EncodeI(0x08, 8, 9, 0x1234);
            Console.WriteLine($"  Encoded: 0x{iInstr:X8}");
            
            var iArgs = MipsDecoder.DecodeI(iInstr);
            Console.WriteLine($"  Decoded: rs={iArgs.Rs}, rt={iArgs.Rt}, imm=0x{iArgs.Imm:X4}");
            Console.WriteLine($"  Opcode: 0x{MipsDecoder.GetOpcode(iInstr):X2}");
            Console.WriteLine();

            // Test J-type instruction: J 0x100000
            // opcode=0x02, addr=0x100000
            Console.WriteLine("Test 3: J-type instruction (J 0x100000)");
            uint jInstr = MipsEncoder.EncodeJ(0x02, 0x100000);
            Console.WriteLine($"  Encoded: 0x{jInstr:X8}");
            
            var jArgs = MipsDecoder.DecodeJ(jInstr);
            Console.WriteLine($"  Decoded: addr=0x{jArgs.Addr:X8}");
            Console.WriteLine($"  Opcode: 0x{MipsDecoder.GetOpcode(jInstr):X2}");
            Console.WriteLine();

            // Verify round-trip encoding/decoding
            Console.WriteLine("=== Round-trip Verification ===");
            bool rValid = VerifyRoundTripR(0, 10, 11, 12, 5, 0x21);
            bool iValid = VerifyRoundTripI(0x09, 5, 6, 0xABCD);
            bool jValid = VerifyRoundTripJ(0x03, 0x3FFFFFF);
            
            Console.WriteLine($"R-type round-trip: {(rValid ? "PASSED" : "FAILED")}");
            Console.WriteLine($"I-type round-trip: {(iValid ? "PASSED" : "FAILED")}");
            Console.WriteLine($"J-type round-trip: {(jValid ? "PASSED" : "FAILED")}");
            Console.WriteLine();
            
            if (rValid && iValid && jValid)
            {
                Console.WriteLine("✓ All tests PASSED!");
            }
            else
            {
                Console.WriteLine("✗ Some tests FAILED!");
            }
        }

        static bool VerifyRoundTripR(int opcode, int rs, int rt, int rd, int shamt, int funct)
        {
            uint encoded = MipsEncoder.EncodeR(opcode, rs, rt, rd, shamt, funct);
            var decoded = MipsDecoder.DecodeR(encoded);
            int decodedOpcode = MipsDecoder.GetOpcode(encoded);
            int decodedFunct = MipsDecoder.GetFunct(encoded);
            
            return decodedOpcode == opcode && decoded.Rs == rs && decoded.Rt == rt && 
                   decoded.Rd == rd && decoded.Shamt == shamt && decodedFunct == funct;
        }

        static bool VerifyRoundTripI(int opcode, int rs, int rt, int imm)
        {
            uint encoded = MipsEncoder.EncodeI(opcode, rs, rt, imm);
            var decoded = MipsDecoder.DecodeI(encoded);
            int decodedOpcode = MipsDecoder.GetOpcode(encoded);
            
            return decodedOpcode == opcode && decoded.Rs == rs && 
                   decoded.Rt == rt && decoded.Imm == (imm & 0xFFFF);
        }

        static bool VerifyRoundTripJ(int opcode, uint addr)
        {
            uint encoded = MipsEncoder.EncodeJ(opcode, addr);
            var decoded = MipsDecoder.DecodeJ(encoded);
            int decodedOpcode = MipsDecoder.GetOpcode(encoded);
            
            return decodedOpcode == opcode && decoded.Addr == (addr & 0x03FFFFFF);
        }

        static void TestAssemblerDisassembler()
        {
            Console.WriteLine("=== Testing MipsAssembler and MipsDisassembler ===");
            Console.WriteLine();

            // Test 1: R-type instructions
            Console.WriteLine("Test 1: R-type instructions");
            TestAsmDisasm("add $t0, $s0, $s1");
            TestAsmDisasm("addu $t1, $s2, $s3");
            TestAsmDisasm("sub $t2, $s4, $s5");
            TestAsmDisasm("mult $s0, $s1");
            TestAsmDisasm("div $t0, $t1");
            TestAsmDisasm("mfhi $t0");
            TestAsmDisasm("mflo $t1");
            Console.WriteLine();

            // Test 2: I-type instructions
            Console.WriteLine("Test 2: I-type instructions");
            TestAsmDisasm("addi $t0, $s0, 100");
            TestAsmDisasm("addiu $t1, $t0, 0x1234");
            TestAsmDisasm("daddi $t2, $s1, -50");
            TestAsmDisasm("beq $s0, $s1, 0x10");
            TestAsmDisasm("bne $t0, $t1, 0x20");
            Console.WriteLine();

            // Test 3: J-type instructions
            Console.WriteLine("Test 3: J-type instructions");
            TestAsmDisasm("j 0x400000");
            TestAsmDisasm("jal 0x500000");
            Console.WriteLine();

            // Test 4: Different register formats
            Console.WriteLine("Test 4: Different register formats");
            TestAsmDisasm("add $8, $16, $17");   // Numeric format
            TestAsmDisasm("add t0, s0, s1");      // Without $ prefix
            Console.WriteLine();

            // Test 5: Disassemble a block
            Console.WriteLine("Test 5: Disassemble a block of code");
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
            foreach (var line in disassembled)
            {
                Console.WriteLine($"  {line}");
            }
            Console.WriteLine();

            // Test 6: Round-trip assembly/disassembly
            Console.WriteLine("Test 6: Round-trip verification");
            bool success = true;
            string[] testInstructions = new[]
            {
                "add $t0, $s0, $s1",
                "addi $t1, $t0, 100",
                "j 0x400000"
            };

            foreach (var instr in testInstructions)
            {
                uint encoded = MipsAssembler.Assemble(instr);
                string disasm = MipsDisassembler.Disassemble(encoded);
                // Compare normalized forms (both should be lowercase, same format)
                string normalized = NormalizeAsm(instr);
                string disasmNorm = NormalizeAsm(disasm);
                
                bool match = normalized == disasmNorm;
                Console.WriteLine($"  {instr} => 0x{encoded:X8} => {disasm} [{(match ? "PASS" : "FAIL")}]");
                success &= match;
            }
            Console.WriteLine();

            if (success)
            {
                Console.WriteLine("✓ All assembler/disassembler tests PASSED!");
            }
            else
            {
                Console.WriteLine("✗ Some assembler/disassembler tests FAILED!");
            }
        }

        static void TestAsmDisasm(string asmLine)
        {
            try
            {
                uint encoded = MipsAssembler.Assemble(asmLine);
                string disasm = MipsDisassembler.Disassemble(encoded);
                Console.WriteLine($"  {asmLine,-30} => 0x{encoded:X8} => {disasm}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  {asmLine,-30} => ERROR: {ex.Message}");
            }
        }

        static string NormalizeAsm(string asm)
        {
            // Normalize assembly for comparison: lowercase, remove $ prefix, consistent spacing
            asm = asm.ToLowerInvariant().Replace("$", "").Replace(",", " ");
            // Split and rejoin to normalize whitespace
            var parts = asm.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }
    }
}
