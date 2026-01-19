using CodeDesigner.Mips64LE.Common;

namespace CodeDesigner.Mips64LE
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Entry point for the MIPS64 Emotion Engine project
            Console.WriteLine("MIPS64 Emotion Engine - CodeDesigner");
            Console.WriteLine();
            
            // Check if --server flag is passed
            bool startServer = args.Length > 0 && args[0] == "--server";

            if (startServer)
            {
                // Start socket server for VSCode extension communication
                Console.WriteLine("Starting socket server mode...");
                var server = new SocketServer(8765);
                
                server.MessageReceived += (sender, message) =>
                {
                    Console.WriteLine($"[Server] Received message: {message}");
                };

                server.ClientConnected += (sender, endpoint) =>
                {
                    Console.WriteLine($"[Server] Client connected: {endpoint}");
                };

                server.ClientDisconnected += (sender, e) =>
                {
                    Console.WriteLine("[Server] Client disconnected");
                };

                var serverTask = server.StartAsync();
                
                Console.WriteLine("Press 'q' to quit the server...");
                while (Console.ReadKey(true).Key != ConsoleKey.Q)
                {
                    // Wait for quit command
                }

                server.Stop();
                await serverTask;
            }
            else
            {
                // Run tests as before
                TestEncoderDecoder();

                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine();

                TestAssemblerDisassembler();
                
                Console.WriteLine();
                Console.WriteLine("To start the socket server, run with --server flag");
            }
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
            Console.WriteLine("Test 1: R-type arithmetic instructions");
            TestAsmDisasm("add $t0, $s0, $s1");
            TestAsmDisasm("addu $t1, $s2, $s3");
            TestAsmDisasm("sub $t2, $s4, $s5");
            TestAsmDisasm("mult $s0, $s1");
            TestAsmDisasm("div $t0, $t1");
            TestAsmDisasm("mfhi $t0");
            TestAsmDisasm("mflo $t1");
            Console.WriteLine();

            // Test 1b: R-type logical instructions
            Console.WriteLine("Test 1b: R-type logical instructions");
            TestAsmDisasm("and $t0, $s0, $s1");
            TestAsmDisasm("or $t1, $s2, $s3");
            TestAsmDisasm("xor $t2, $s4, $s5");
            TestAsmDisasm("nor $t3, $s6, $s7");
            Console.WriteLine();

            // Test 1c: R-type shift instructions
            Console.WriteLine("Test 1c: R-type shift instructions");
            TestAsmDisasm("sll $t0, $s0, 5");
            TestAsmDisasm("srl $t1, $s1, 3");
            TestAsmDisasm("sra $t2, $s2, 8");
            TestAsmDisasm("sllv $t3, $s3, $s4");
            Console.WriteLine();

            // Test 1d: R-type compare and jump instructions
            Console.WriteLine("Test 1d: R-type compare/jump instructions");
            TestAsmDisasm("slt $t0, $s0, $s1");
            TestAsmDisasm("sltu $t1, $s2, $s3");
            TestAsmDisasm("jr $ra");
            TestAsmDisasm("jalr $t0, $ra");  // rd, rs format
            Console.WriteLine();

            // Test 2: I-type instructions
            Console.WriteLine("Test 2: I-type arithmetic instructions");
            TestAsmDisasm("addi $t0, $s0, 100");
            TestAsmDisasm("addiu $t1, $t0, 0x1234");
            TestAsmDisasm("daddi $t2, $s1, -50");
            Console.WriteLine();

            // Test 2b: I-type logical instructions
            Console.WriteLine("Test 2b: I-type logical instructions");
            TestAsmDisasm("andi $t0, $s0, 0xFF");
            TestAsmDisasm("ori $t1, $s1, 0x1000");
            TestAsmDisasm("xori $t2, $s2, 0xABCD");
            Console.WriteLine();

            // Test 2c: I-type branch instructions
            Console.WriteLine("Test 2c: I-type branch instructions");
            TestAsmDisasm("beq $s0, $s1, 0x10");
            TestAsmDisasm("bne $t0, $t1, 0x20");
            TestAsmDisasm("blez $s0, 0x30");
            TestAsmDisasm("bgtz $s1, 0x40");
            Console.WriteLine();

            // Test 2d: I-type load/store instructions  
            Console.WriteLine("Test 2d: I-type load/store instructions");
            TestAsmDisasm("lw $t0, 100, $s0");  // rt, offset, rs format
            TestAsmDisasm("sw $t1, 200, $s1");
            TestAsmDisasm("lb $t2, 4, $s2");
            TestAsmDisasm("sb $t3, 8, $s3");
            Console.WriteLine();

            // Test 2e: I-type special instructions
            Console.WriteLine("Test 2e: I-type special instructions");
            TestAsmDisasm("lui $t0, 0x1234");
            TestAsmDisasm("slti $t1, $s0, 100");
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
