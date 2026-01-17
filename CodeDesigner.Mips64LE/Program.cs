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

            // Test MipsAssembler
            TestMipsAssembler();
        }

        static void TestMipsAssembler()
        {
            Console.WriteLine("=== Testing MIPS Assembler ===");
            
            var assembler = new MipsAssembler();

            // Test various instruction types
            var testInstructions = new[]
            {
                "add $t0, $t1, $t2",        // R-type: basic arithmetic
                "addu $s0, $s1, $s2",       // R-type: unsigned add
                "sub $t3, $t4, $t5",        // R-type: subtraction
                "and $t0, $t1, $t2",        // R-type: logical AND
                "or $t0, $t1, $t2",         // R-type: logical OR
                "sll $t0, $t1, 4",          // R-type: shift left logical with immediate
                "srl $t2, $t3, 8",          // R-type: shift right logical with immediate
                "addi $t0, $t1, 100",       // I-type: add immediate
                "ori $t0, $t1, 0xFF",       // I-type: OR immediate (hex)
                "lui $t0, 0x1234",          // I-type: load upper immediate
                "lw $t0, 4($sp)",           // I-type: load word
                "sw $t0, 8($sp)",           // I-type: store word
                "lw $t0, 0x10($sp)",        // I-type: load with hex offset
                "beq $t0, $t1, 16",         // I-type: branch if equal
                "bne $t0, $t1, -4",         // I-type: branch if not equal
                "j 0x1000",                 // J-type: jump
                "jal 0x2000",               // J-type: jump and link
                "slt $t0, $t1, $t2",        // R-type: set less than
                "slti $t0, $t1, 50",        // I-type: set less than immediate
            };

            foreach (var instruction in testInstructions)
            {
                try
                {
                    uint encoded = assembler.AssembleLine(instruction);
                    Console.WriteLine($"[OK] {instruction,-30} => 0x{encoded:X8}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] {instruction,-30} => {ex.Message}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== Testing Multi-line Assembly ===");
            
            string multiLineCode = @"
                # Simple program to add two numbers
                addi $t0, $zero, 10    # Load 10 into $t0
                addi $t1, $zero, 20    # Load 20 into $t1
                add $t2, $t0, $t1      # Add them together
                sw $t2, 0($sp)         # Store result
            ";

            try
            {
                uint[] encoded = assembler.Assemble(multiLineCode);
                Console.WriteLine($"Assembled {encoded.Length} instructions:");
                for (int i = 0; i < encoded.Length; i++)
                {
                    Console.WriteLine($"  [{i}] 0x{encoded[i]:X8}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Multi-line assembly failed: {ex.Message}");
            }

            Console.WriteLine();
            Console.WriteLine("=== Assembler Test Complete ===");
        }
    }
}
