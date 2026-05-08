using CodeDesigner.Ps2.Common.Cpu.InstrArgs;
using CodeDesigner.Ps2.Common.Cpu.Instructions;
using CodeDesigner.Ps2.Common.Cpu.Instructions.EE;
using CodeDesigner.Ps2.Common.CPU;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CodeDesigner.Ps2.Common
{
    /// <summary>
    /// Assembles MIPS assembly text into 32-bit instruction words
    /// </summary>
    public class MipsAssembler
    {
        private static readonly Dictionary<string, Instruction<Args_R>> RTypeInstructionsByName = new();
        private static readonly Dictionary<string, Instruction<Args_I>> ITypeInstructionsByName = new();
        private static readonly Dictionary<string, Instruction<Args_J>> JTypeInstructionsByName = new();
        private static readonly Dictionary<string, int> RegisterMap = new();

        static MipsAssembler()
        {
            InitializeInstructions();
            InitializeRegisterMap();
        }

        private static void InitializeInstructions()
        {
            // R-type arithmetic instructions (opcode = 0x00)
            RegisterR(new ADD());
            RegisterR(new ADDU());
            RegisterR(new DADD());
            RegisterR(new DADDU());
            RegisterR(new SUB());
            RegisterR(new SUBU());
            RegisterR(new DSUB());
            RegisterR(new DSUBU());
            RegisterR(new MULT());
            RegisterR(new MULTU());
            RegisterR(new DMULT());
            RegisterR(new DMULTU());
            RegisterR(new DIV());
            RegisterR(new DIVU());
            RegisterR(new DDIV());
            RegisterR(new DDIVU());
            RegisterR(new MFHI());
            RegisterR(new MFLO());
            RegisterR(new MTHI());
            RegisterR(new MTLO());

            // R-type logical instructions
            RegisterR(new AND());
            RegisterR(new OR());
            RegisterR(new XOR());
            RegisterR(new NOR());

            // R-type shift instructions
            RegisterR(new SLL());
            RegisterR(new SRL());
            RegisterR(new SRA());
            RegisterR(new DSLL());
            RegisterR(new DSRL());
            RegisterR(new DSRA());
            RegisterR(new SLLV());
            RegisterR(new SRLV());
            RegisterR(new SRAV());
            RegisterR(new DSLLV());
            RegisterR(new DSRLV());
            RegisterR(new DSRAV());

            // R-type compare instructions
            RegisterR(new SLT());
            RegisterR(new SLTU());

            // R-type branch/jump instructions
            RegisterR(new JR());
            RegisterR(new JALR());

            // R-type special instructions
            RegisterR(new MOVZ());
            RegisterR(new MOVN());
            RegisterR(new NOP());  // Still support assembling NOP
            RegisterR(new SYNC());

            // R-type trap instructions
            RegisterR(new SYSCALL());
            RegisterR(new BREAK());
            RegisterR(new TGE());
            RegisterR(new TGEU());
            RegisterR(new TLT());
            RegisterR(new TLTU());
            RegisterR(new TEQ());
            RegisterR(new TNE());

            // I-type arithmetic instructions
            RegisterI(new ADDI());
            RegisterI(new ADDIU());
            RegisterI(new DADDI());
            RegisterI(new DADDIU());

            // I-type logical instructions
            RegisterI(new ANDI());
            RegisterI(new ORI());
            RegisterI(new XORI());

            // I-type compare instructions
            RegisterI(new SLTI());
            RegisterI(new SLTIU());

            // I-type branch instructions
            RegisterI(new BEQ());
            RegisterI(new BNE());
            RegisterI(new BLEZ());
            RegisterI(new BGTZ());
            RegisterI(new BLTZ());
            RegisterI(new BGEZ());
            RegisterI(new BEQL());
            RegisterI(new BNEL());

            // I-type load instructions
            RegisterI(new LB());
            RegisterI(new LBU());
            RegisterI(new LH());
            RegisterI(new LHU());
            RegisterI(new LW());
            RegisterI(new LWU());
            RegisterI(new LD());
            RegisterI(new LWL());
            RegisterI(new LWR());
            RegisterI(new LDL());
            RegisterI(new LDR());
            RegisterI(new LQ());

            // I-type store instructions
            RegisterI(new SB());
            RegisterI(new SH());
            RegisterI(new SW());
            RegisterI(new SD());
            RegisterI(new SWL());
            RegisterI(new SWR());
            RegisterI(new SDL());
            RegisterI(new SDR());
            RegisterI(new SQ());

            // I-type special instructions
            RegisterI(new LUI());

            // J-type instructions
            RegisterJ(new J());
            RegisterJ(new JAL());
        }

        private static void InitializeRegisterMap()
        {
            // Standard MIPS register names
            string[] regNames = {
                "zero", "at", "v0", "v1", "a0", "a1", "a2", "a3",
                "t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7",
                "s0", "s1", "s2", "s3", "s4", "s5", "s6", "s7",
                "t8", "t9", "k0", "k1", "gp", "sp", "fp", "ra"
            };

            for (int i = 0; i < regNames.Length; i++)
            {
                RegisterMap[regNames[i]] = i;
                RegisterMap[$"${regNames[i]}"] = i; // Support $ prefix
                RegisterMap[$"r{i}"] = i;            // Support r0-r31
                RegisterMap[$"$r{i}"] = i;           // Support $r0-$r31
                RegisterMap[$"{i}"] = i;             // Support numeric form
                RegisterMap[$"${i}"] = i;            // Support $0-$31
            }
        }

        private static void RegisterR(Instruction<Args_R> instr)
        {
            RTypeInstructionsByName[instr.Name.ToLowerInvariant()] = instr;
        }

        private static void RegisterI(Instruction<Args_I> instr)
        {
            ITypeInstructionsByName[instr.Name.ToLowerInvariant()] = instr;
        }

        private static void RegisterJ(Instruction<Args_J> instr)
        {
            JTypeInstructionsByName[instr.Name.ToLowerInvariant()] = instr;
        }

        /// <summary>
        /// Parse a register name into its numeric value
        /// </summary>
        private static int ParseRegister(string reg)
        {
            string normalized = reg.Trim().ToLowerInvariant();
            
            if (RegisterMap.TryGetValue(normalized, out int regNum))
            {
                return regNum;
            }

            throw new ArgumentException($"Unknown register: {reg}");
        }

        /// <summary>
        /// Parse an immediate value (supports decimal, hex with 0x or $ prefix, and negative values)
        /// </summary>
        private static int ParseImmediate(string imm)
        {
            string normalized = imm.Trim();
            
            // Support $ prefix for hex (PS2 MIPS assembler style)
            if (normalized.StartsWith("$"))
            {
                return Convert.ToInt32(normalized.Substring(1), 16);
            }
            
            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToInt32(normalized, 16);
            }
            
            return int.Parse(normalized);
        }

        /// <summary>
        /// Assemble a single line of assembly into a 32-bit instruction
        /// </summary>
        /// <param name="asmLine">Assembly line (e.g., "add $t0, $s0, $s1")</param>
        /// <returns>Encoded 32-bit instruction</returns>
        public static uint Assemble(string asmLine)
        {
            // Remove comments
            int commentIndex = asmLine.IndexOf('#');
            if (commentIndex >= 0)
            {
                asmLine = asmLine.Substring(0, commentIndex);
            }

            // Split by whitespace and commas
            string[] parts = asmLine.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                throw new ArgumentException("Empty instruction");
            }

            string mnemonic = parts[0].ToLowerInvariant();
            string[] operands = parts.Skip(1).ToArray();

            // Single-instruction pseudo rewrites: rewrite to a real mnemonic + operands
            // before dispatch. Multi-instruction pseudos (la, blt, bgt, ble, bge) are
            // handled at the parser level so they can advance the address counter.
            if (TryExpandSingleInstructionPseudo(mnemonic, operands, out string realMnemonic, out string[] realOperands))
            {
                mnemonic = realMnemonic;
                operands = realOperands;
            }

            // Try R-type
            if (RTypeInstructionsByName.TryGetValue(mnemonic, out var rInstr))
            {
                return AssembleR(rInstr, operands);
            }

            // Try I-type
            if (ITypeInstructionsByName.TryGetValue(mnemonic, out var iInstr))
            {
                return AssembleI(iInstr, operands);
            }

            // Try J-type
            if (JTypeInstructionsByName.TryGetValue(mnemonic, out var jInstr))
            {
                return AssembleJ(jInstr, operands);
            }

            throw new ArgumentException($"Unknown instruction: {mnemonic}");
        }

        /// <summary>
        /// Try to rewrite a one-instruction pseudo into its underlying real mnemonic + operands.
        /// Returns true and populates <paramref name="realMnemonic"/>/<paramref name="realOperands"/>
        /// when a rewrite applies; otherwise returns false and the caller dispatches the original
        /// mnemonic.
        /// </summary>
        private static bool TryExpandSingleInstructionPseudo(
            string mnemonic, string[] operands, out string realMnemonic, out string[] realOperands)
        {
            switch (mnemonic)
            {
                // move rd, rs  ->  addu rd, rs, $zero
                case "move":
                    if (operands.Length != 2)
                        throw new ArgumentException("Expected 2 operands for move");
                    realMnemonic = "addu";
                    realOperands = new[] { operands[0], operands[1], "$zero" };
                    return true;

                // not rd, rs  ->  nor rd, rs, $zero
                case "not":
                    if (operands.Length != 2)
                        throw new ArgumentException("Expected 2 operands for not");
                    realMnemonic = "nor";
                    realOperands = new[] { operands[0], operands[1], "$zero" };
                    return true;

                // negu rd, rs  ->  subu rd, $zero, rs
                case "negu":
                    if (operands.Length != 2)
                        throw new ArgumentException("Expected 2 operands for negu");
                    realMnemonic = "subu";
                    realOperands = new[] { operands[0], "$zero", operands[1] };
                    return true;

                // bnez rs, label  ->  bne rs, $zero, label
                case "bnez":
                    if (operands.Length != 2)
                        throw new ArgumentException("Expected 2 operands for bnez");
                    realMnemonic = "bne";
                    realOperands = new[] { operands[0], "$zero", operands[1] };
                    return true;

                // beqz rs, label  ->  beq rs, $zero, label
                case "beqz":
                    if (operands.Length != 2)
                        throw new ArgumentException("Expected 2 operands for beqz");
                    realMnemonic = "beq";
                    realOperands = new[] { operands[0], "$zero", operands[1] };
                    return true;

                // b label  ->  beq $zero, $zero, label
                case "b":
                    if (operands.Length != 1)
                        throw new ArgumentException("Expected 1 operand for b");
                    realMnemonic = "beq";
                    realOperands = new[] { "$zero", "$zero", operands[0] };
                    return true;

                // li rt, imm (16-bit signed)  ->  addiu rt, $zero, imm
                // Callers that need a 32-bit immediate should use the CDS `setreg` directive.
                case "li":
                    if (operands.Length != 2)
                        throw new ArgumentException("Expected 2 operands for li");
                    int liImm = ParseImmediate(operands[1]);
                    if (liImm < short.MinValue || liImm > 0xFFFF)
                        throw new ArgumentException(
                            $"li immediate {operands[1]} does not fit in 16 bits; use `setreg` for 32-bit values");
                    realMnemonic = "addiu";
                    realOperands = new[] { operands[0], "$zero", operands[1] };
                    return true;

                default:
                    realMnemonic = mnemonic;
                    realOperands = operands;
                    return false;
            }
        }

        private static uint AssembleR(Instruction<Args_R> instr, string[] operands)
        {
            // Handle different R-type formats based on ArgNames
            string[] argNames = instr.ArgNames;

            if (argNames.Length == 3 && argNames[0] == "rd" && argNames[1] == "rs" && argNames[2] == "rt")
            {
                // Format: rd, rs, rt (e.g., ADD $t0, $s0, $s1)
                if (operands.Length != 3)
                    throw new ArgumentException($"Expected 3 operands for {instr.Name}");
                
                int rd = ParseRegister(operands[0]);
                int rs = ParseRegister(operands[1]);
                int rt = ParseRegister(operands[2]);
                
                return MipsEncoder.EncodeR(instr.Opcode, rs, rt, rd, 0, instr.Funct ?? 0);
            }
            else if (argNames.Length == 3 && argNames[0] == "rd" && argNames[1] == "rt" && argNames[2] == "sa")
            {
                // Format: rd, rt, sa (e.g., SLL $t0, $s0, 5)
                if (operands.Length != 3)
                    throw new ArgumentException($"Expected 3 operands for {instr.Name}");
                
                int rd = ParseRegister(operands[0]);
                int rt = ParseRegister(operands[1]);
                int sa = ParseImmediate(operands[2]);
                
                return MipsEncoder.EncodeR(instr.Opcode, 0, rt, rd, sa, instr.Funct ?? 0);
            }
            else if (argNames.Length == 3 && argNames[0] == "rd" && argNames[1] == "rt" && argNames[2] == "rs")
            {
                // Format: rd, rt, rs (e.g., SLLV $t0, $s0, $s1 - variable shift)
                if (operands.Length != 3)
                    throw new ArgumentException($"Expected 3 operands for {instr.Name}");
                
                int rd = ParseRegister(operands[0]);
                int rt = ParseRegister(operands[1]);
                int rs = ParseRegister(operands[2]);
                
                return MipsEncoder.EncodeR(instr.Opcode, rs, rt, rd, 0, instr.Funct ?? 0);
            }
            else if (argNames.Length == 2 && argNames[0] == "rs" && argNames[1] == "rt")
            {
                // Format: rs, rt (e.g., MULT $s0, $s1)
                if (operands.Length != 2)
                    throw new ArgumentException($"Expected 2 operands for {instr.Name}");
                
                int rs = ParseRegister(operands[0]);
                int rt = ParseRegister(operands[1]);
                
                return MipsEncoder.EncodeR(instr.Opcode, rs, rt, 0, 0, instr.Funct ?? 0);
            }
            else if (argNames.Length == 2 && argNames[0] == "rd" && argNames[1] == "rs")
            {
                // Format: rd, rs (e.g., JALR $t0, $ra)
                if (operands.Length != 2)
                    throw new ArgumentException($"Expected 2 operands for {instr.Name}");
                
                int rd = ParseRegister(operands[0]);
                int rs = ParseRegister(operands[1]);
                
                return MipsEncoder.EncodeR(instr.Opcode, rs, 0, rd, 0, instr.Funct ?? 0);
            }
            else if (argNames.Length == 1 && argNames[0] == "rd")
            {
                // Format: rd (e.g., MFHI $t0, MFLO $t0)
                if (operands.Length != 1)
                    throw new ArgumentException($"Expected 1 operand for {instr.Name}");
                
                int rd = ParseRegister(operands[0]);
                
                return MipsEncoder.EncodeR(instr.Opcode, 0, 0, rd, 0, instr.Funct ?? 0);
            }
            else if (argNames.Length == 1 && argNames[0] == "rs")
            {
                // Format: rs (e.g., MTHI $s0, MTLO $s0, JR $ra)
                if (operands.Length != 1)
                    throw new ArgumentException($"Expected 1 operand for {instr.Name}");
                
                int rs = ParseRegister(operands[0]);
                
                return MipsEncoder.EncodeR(instr.Opcode, rs, 0, 0, 0, instr.Funct ?? 0);
            }
            else if (argNames.Length == 0)
            {
                // Format: no operands (e.g., NOP, SYNC, SYSCALL)
                if (operands.Length != 0)
                    throw new ArgumentException($"Expected 0 operands for {instr.Name}");
                
                return MipsEncoder.EncodeR(instr.Opcode, 0, 0, 0, 0, instr.Funct ?? 0);
            }
            else if (argNames.Length == 1 && argNames[0] == "code")
            {
                // Format: optional code (e.g., SYSCALL, BREAK) - code embedded in bits 25-6
                uint code = operands.Length > 0 ? (uint)ParseImmediate(operands[0]) : 0;
                return (uint)((code << 6) | (uint)(instr.Funct ?? 0));
            }
            else if (argNames.Length == 1 && argNames[0] == "stype")
            {
                // Format: optional stype (e.g., SYNC) - stype in shamt field (bits 10-6)
                int stype = operands.Length > 0 ? ParseImmediate(operands[0]) : 0;
                return MipsEncoder.EncodeR(instr.Opcode, 0, 0, 0, stype, instr.Funct ?? 0);
            }

            throw new NotImplementedException($"Unsupported R-type format for {instr.Name}");
        }

        private static uint AssembleI(Instruction<Args_I> instr, string[] operands)
        {
            // Handle different I-type formats
            string[] argNames = instr.ArgNames;

            if (argNames.Length == 3 && argNames[0] == "rt" && argNames[1] == "rs" && argNames[2] == "imm")
            {
                // Format: rt, rs, imm (e.g., ADDI $t0, $s0, 100)
                if (operands.Length != 3)
                    throw new ArgumentException($"Expected 3 operands for {instr.Name}");
                
                int rt = ParseRegister(operands[0]);
                int rs = ParseRegister(operands[1]);
                int imm = ParseImmediate(operands[2]);
                
                return MipsEncoder.EncodeI(instr.Opcode, rs, rt, imm);
            }
            else if (argNames.Length == 3 && argNames[0] == "rt" && argNames[1] == "offset" && argNames[2] == "rs")
            {
                // Format: rt, offset(rs) (e.g., LW $t0, 100($s0) or LW $t0, $100($s0))
                // Support both formats:
                // 1. Three separate operands: rt, offset, rs
                // 2. Two operands where second is offset(rs)
                
                if (operands.Length == 2)
                {
                    // Parse offset(rs) format
                    string offsetBaseStr = operands[1];
                    var match = Regex.Match(offsetBaseStr, @"^(.+?)\((.+)\)$");
                    if (match.Success)
                    {
                        int rt = ParseRegister(operands[0]);
                        int offset = ParseImmediate(match.Groups[1].Value);
                        int rs = ParseRegister(match.Groups[2].Value);
                        return MipsEncoder.EncodeI(instr.Opcode, rs, rt, offset);
                    }
                }
                else if (operands.Length == 3)
                {
                    // Three separate operands format
                    int rt = ParseRegister(operands[0]);
                    int offset = ParseImmediate(operands[1]);
                    int rs = ParseRegister(operands[2]);
                    return MipsEncoder.EncodeI(instr.Opcode, rs, rt, offset);
                }
                
                throw new ArgumentException($"Expected 2 or 3 operands for {instr.Name}");
            }
            else if (argNames.Length == 3 && argNames[0] == "rs" && argNames[1] == "rt" && argNames[2] == "offset")
            {
                // Format: rs, rt, offset (e.g., BEQ $s0, $s1, 0x1000)
                if (operands.Length != 3)
                    throw new ArgumentException($"Expected 3 operands for {instr.Name}");
                
                int rs = ParseRegister(operands[0]);
                int rt = ParseRegister(operands[1]);
                int offset = ParseImmediate(operands[2]);
                
                return MipsEncoder.EncodeI(instr.Opcode, rs, rt, offset);
            }
            else if (argNames.Length == 2 && argNames[0] == "rs" && argNames[1] == "offset")
            {
                // Format: rs, offset (e.g., BLEZ $s0, 0x1000)
                // For REGIMM instructions (opcode=0x01), Funct encodes the rt sub-opcode
                // e.g. BGEZ has Funct=0x01 so rt=1, while BLTZ has Funct=null so rt=0
                if (operands.Length != 2)
                    throw new ArgumentException($"Expected 2 operands for {instr.Name}");
                
                int rs = ParseRegister(operands[0]);
                int offset = ParseImmediate(operands[1]);
                int rt = instr.Funct ?? 0;
                
                return MipsEncoder.EncodeI(instr.Opcode, rs, rt, offset);
            }
            else if (argNames.Length == 2 && argNames[0] == "rt" && argNames[1] == "imm")
            {
                // Format: rt, imm (e.g., LUI $t0, 0x1234)
                if (operands.Length != 2)
                    throw new ArgumentException($"Expected 2 operands for {instr.Name}");
                
                int rt = ParseRegister(operands[0]);
                int imm = ParseImmediate(operands[1]);
                
                return MipsEncoder.EncodeI(instr.Opcode, 0, rt, imm);
            }

            throw new NotImplementedException($"Unsupported I-type format for {instr.Name}");
        }

        private static uint AssembleJ(Instruction<Args_J> instr, string[] operands)
        {
            // Format: target (e.g., J 0x100000)
            if (operands.Length != 1)
                throw new ArgumentException($"Expected 1 operand for {instr.Name}");
            
            uint target = (uint)ParseImmediate(operands[0]);
            
            // Validate that the target address is word-aligned (divisible by 4)
            if ((target & 0x3) != 0)
            {
                throw new ArgumentException($"Jump target address 0x{target:X} must be word-aligned (divisible by 4)");
            }
            
            // J-type instructions store word addresses (address >> 2)
            // The 26-bit field represents bits [27:2] of the full 32-bit address
            target = (target >> 2) & 0x03FFFFFF;
            
            return MipsEncoder.EncodeJ(instr.Opcode, target);
        }

        /// <summary>
        /// Assemble multiple lines of assembly
        /// </summary>
        /// <param name="asmLines">Array of assembly lines</param>
        /// <returns>Array of encoded 32-bit instructions</returns>
        public static uint[] AssembleBlock(string[] asmLines)
        {
            var result = new List<uint>();

            foreach (var line in asmLines)
            {
                string trimmed = line.Trim();
                
                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                {
                    continue;
                }

                result.Add(Assemble(trimmed));
            }

            return result.ToArray();
        }
    }
}
