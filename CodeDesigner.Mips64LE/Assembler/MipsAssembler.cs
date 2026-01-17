using CodeDesigner.Mips64LE.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.Cpu.Instructions;
using CodeDesigner.Mips64LE.Cpu.Instructions.EE;
using CodeDesigner.Mips64LE.CPU;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE
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
            // R-type instructions
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

            // I-type instructions
            RegisterI(new ADDI());
            RegisterI(new ADDIU());
            RegisterI(new DADDI());
            RegisterI(new DADDIU());
            RegisterI(new BEQ());
            RegisterI(new BNE());

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
        /// Parse an immediate value (supports decimal, hex with 0x prefix, and negative values)
        /// </summary>
        private static int ParseImmediate(string imm)
        {
            string normalized = imm.Trim();
            
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
            else if (argNames.Length == 2 && argNames[0] == "rs" && argNames[1] == "rt")
            {
                // Format: rs, rt (e.g., MULT $s0, $s1)
                if (operands.Length != 2)
                    throw new ArgumentException($"Expected 2 operands for {instr.Name}");
                
                int rs = ParseRegister(operands[0]);
                int rt = ParseRegister(operands[1]);
                
                return MipsEncoder.EncodeR(instr.Opcode, rs, rt, 0, 0, instr.Funct ?? 0);
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
                // Format: rs (e.g., MTHI $s0, MTLO $s0)
                if (operands.Length != 1)
                    throw new ArgumentException($"Expected 1 operand for {instr.Name}");
                
                int rs = ParseRegister(operands[0]);
                
                return MipsEncoder.EncodeR(instr.Opcode, rs, 0, 0, 0, instr.Funct ?? 0);
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

            throw new NotImplementedException($"Unsupported I-type format for {instr.Name}");
        }

        private static uint AssembleJ(Instruction<Args_J> instr, string[] operands)
        {
            // Format: target (e.g., J 0x100000)
            if (operands.Length != 1)
                throw new ArgumentException($"Expected 1 operand for {instr.Name}");
            
            uint target = (uint)ParseImmediate(operands[0]);
            
            // If target looks like a full address, extract the 26-bit field
            if (target > 0x03FFFFFF)
            {
                target = (target >> 2) & 0x03FFFFFF;
            }
            
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
