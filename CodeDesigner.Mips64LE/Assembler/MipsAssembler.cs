using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CodeDesigner.Mips64LE.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.Cpu.Instructions;
using CodeDesigner.Mips64LE.Cpu.Instructions.EE;

namespace CodeDesigner.Mips64LE
{
    /// <summary>
    /// MIPS64 Assembler - Converts MIPS assembly text to machine code
    /// </summary>
    public class MipsAssembler
    {
        private readonly Dictionary<string, InstructionInfo> _instructionMap;
        private readonly Dictionary<string, int> _registerMap;

        public MipsAssembler()
        {
            _instructionMap = new Dictionary<string, InstructionInfo>(StringComparer.OrdinalIgnoreCase);
            _registerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            InitializeRegisterMap();
            InitializeInstructionMap();
        }

        /// <summary>
        /// Maps register names to register numbers
        /// </summary>
        private void InitializeRegisterMap()
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
                _registerMap[$"${regNames[i]}"] = i;
                _registerMap[regNames[i]] = i; // Also support without $
                _registerMap[$"$r{i}"] = i; // Numeric form
                _registerMap[$"${i}"] = i; // Direct numeric form
            }
        }

        /// <summary>
        /// Initializes instruction map with all MIPS instructions
        /// </summary>
        private void InitializeInstructionMap()
        {
            // Arithmetic R-Type Instructions
            RegisterInstruction(new ADD());
            RegisterInstruction(new ADDU());
            RegisterInstruction(new DADD());
            RegisterInstruction(new DADDU());
            RegisterInstruction(new SUB());
            RegisterInstruction(new SUBU());
            RegisterInstruction(new DSUB());
            RegisterInstruction(new DSUBU());
            RegisterInstruction(new MULT());
            RegisterInstruction(new MULTU());
            RegisterInstruction(new DMULT());
            RegisterInstruction(new DMULTU());
            RegisterInstruction(new DIV());
            RegisterInstruction(new DIVU());
            RegisterInstruction(new DDIV());
            RegisterInstruction(new DDIVU());
            RegisterInstruction(new MFHI());
            RegisterInstruction(new MFLO());
            RegisterInstruction(new MTHI());
            RegisterInstruction(new MTLO());

            // Arithmetic I-Type Instructions
            RegisterInstruction(new ADDI());
            RegisterInstruction(new ADDIU());
            RegisterInstruction(new DADDI());
            RegisterInstruction(new DADDIU());

            // Logical Instructions
            RegisterInstruction(new AND());
            RegisterInstruction(new OR());
            RegisterInstruction(new XOR());
            RegisterInstruction(new NOR());
            RegisterInstruction(new ANDI());
            RegisterInstruction(new ORI());
            RegisterInstruction(new XORI());
            RegisterInstruction(new LUI());

            // Shift Instructions
            RegisterInstruction(new SLL());
            RegisterInstruction(new SRL());
            RegisterInstruction(new SRA());
            RegisterInstruction(new SLLV());
            RegisterInstruction(new SRLV());
            RegisterInstruction(new SRAV());
            RegisterInstruction(new DSLL());
            RegisterInstruction(new DSRL());
            RegisterInstruction(new DSRA());
            RegisterInstruction(new DSLLV());
            RegisterInstruction(new DSRLV());
            RegisterInstruction(new DSRAV());

            // Branch and Jump Instructions
            RegisterInstruction(new BEQ());
            RegisterInstruction(new BNE());
            RegisterInstruction(new BLEZ());
            RegisterInstruction(new BGTZ());
            RegisterInstruction(new BLTZ());
            RegisterInstruction(new BGEZ());
            RegisterInstruction(new BEQL());
            RegisterInstruction(new BNEL());
            RegisterInstruction(new J());
            RegisterInstruction(new JAL());
            RegisterInstruction(new JR());
            RegisterInstruction(new JALR());

            // Compare and Set Instructions
            RegisterInstruction(new SLT());
            RegisterInstruction(new SLTU());
            RegisterInstruction(new SLTI());
            RegisterInstruction(new SLTIU());

            // Load Instructions
            RegisterInstruction(new LB());
            RegisterInstruction(new LBU());
            RegisterInstruction(new LH());
            RegisterInstruction(new LHU());
            RegisterInstruction(new LW());
            RegisterInstruction(new LWU());
            RegisterInstruction(new LD());
            RegisterInstruction(new LWL());
            RegisterInstruction(new LWR());
            RegisterInstruction(new LDL());
            RegisterInstruction(new LDR());
            RegisterInstruction(new LQ());

            // Store Instructions
            RegisterInstruction(new SB());
            RegisterInstruction(new SH());
            RegisterInstruction(new SW());
            RegisterInstruction(new SD());
            RegisterInstruction(new SWL());
            RegisterInstruction(new SWR());
            RegisterInstruction(new SDL());
            RegisterInstruction(new SDR());
            RegisterInstruction(new SQ());

            // Special/Move Instructions
            RegisterInstruction(new MOVZ());
            RegisterInstruction(new MOVN());

            // System/Trap Instructions
            RegisterInstruction(new SYSCALL());
            RegisterInstruction(new BREAK());
            RegisterInstruction(new SYNC());
        }

        private void RegisterInstruction<TArgs>(Instruction<TArgs> instruction)
        {
            var info = new InstructionInfo
            {
                Name = instruction.Name,
                Opcode = instruction.Opcode,
                Funct = instruction.Funct,
                ArgType = instruction.ArgType,
                ArgNames = instruction.ArgNames,
                AsmFunc = instruction.Asm
            };
            _instructionMap[instruction.Name] = info;
        }

        /// <summary>
        /// Assembles a single line of MIPS assembly code into machine code
        /// </summary>
        /// <param name="line">Assembly instruction line (e.g., "add $t0, $t1, $t2")</param>
        /// <returns>32-bit encoded instruction</returns>
        public uint AssembleLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                throw new ArgumentException("Empty instruction line");

            // Remove comments
            int commentIndex = line.IndexOf('#');
            if (commentIndex >= 0)
                line = line.Substring(0, commentIndex);

            line = line.Trim();
            if (string.IsNullOrEmpty(line))
                throw new ArgumentException("Empty instruction after comment removal");

            // Parse instruction and operands
            var parts = Regex.Split(line, @"\s+", RegexOptions.None, TimeSpan.FromSeconds(1));
            if (parts.Length == 0)
                throw new ArgumentException("No instruction found");

            string mnemonic = parts[0].ToLower();
            if (!_instructionMap.TryGetValue(mnemonic, out var instrInfo))
                throw new ArgumentException($"Unknown instruction: {mnemonic}");

            // Parse operands (everything after the mnemonic)
            string operandStr = line.Substring(mnemonic.Length).Trim();
            int[] args = ParseOperands(operandStr, instrInfo.ArgType);

            // Use instruction's Asm method to encode
            return instrInfo.AsmFunc(args);
        }

        /// <summary>
        /// Assembles multiple lines of assembly code
        /// </summary>
        /// <param name="code">Multi-line assembly code</param>
        /// <returns>Array of encoded instructions</returns>
        public uint[] Assemble(string code)
        {
            var lines = code.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<uint>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue;

                result.Add(AssembleLine(trimmed));
            }

            return result.ToArray();
        }

        /// <summary>
        /// Parses operands based on instruction type
        /// </summary>
        private int[] ParseOperands(string operandStr, InstrArgType argType)
        {
            switch (argType)
            {
                case InstrArgType.R:
                    return ParseRTypeOperands(operandStr);
                case InstrArgType.I:
                    return ParseITypeOperands(operandStr);
                case InstrArgType.J:
                    return ParseJTypeOperands(operandStr);
                default:
                    throw new ArgumentException($"Unknown instruction type: {argType}");
            }
        }

        /// <summary>
        /// Parses R-type instruction operands (e.g., "add $t0, $t1, $t2")
        /// Returns: [rs, rt, rd, shamt] or subset depending on instruction
        /// </summary>
        private int[] ParseRTypeOperands(string operandStr)
        {
            var operands = SplitOperands(operandStr);
            
            // Handle different R-type formats
            if (operands.Count == 3)
            {
                // Standard 3-register format: rd, rs, rt (e.g., add $t0, $t1, $t2)
                int rd = ParseRegister(operands[0]);
                int rs = ParseRegister(operands[1]);
                int rt = ParseRegister(operands[2]);
                return new int[] { rs, rt, rd, 0 }; // shamt = 0 for most instructions
            }
            else if (operands.Count == 2)
            {
                // 2-register format: used for JR, JALR, MFHI, MFLO, MTHI, MTLO
                // Could be: rd, rs OR just rs depending on instruction
                int reg1 = ParseRegister(operands[0]);
                int reg2 = ParseRegister(operands[1]);
                return new int[] { reg2, 0, reg1, 0 }; // Depends on specific instruction
            }
            else if (operands.Count == 1)
            {
                // Single register: JR, MTHI, MTLO (uses rs)
                int reg = ParseRegister(operands[0]);
                return new int[] { reg, 0, 0, 0 };
            }
            else
            {
                throw new ArgumentException($"Invalid R-type operands: {operandStr}");
            }
        }

        /// <summary>
        /// Parses I-type instruction operands
        /// Returns: [rs, rt, immediate]
        /// </summary>
        private int[] ParseITypeOperands(string operandStr)
        {
            var operands = SplitOperands(operandStr);

            if (operands.Count == 3)
            {
                // Standard format: rt, rs, imm (e.g., addi $t0, $t1, 100)
                int rt = ParseRegister(operands[0]);
                int rs = ParseRegister(operands[1]);
                int imm = ParseImmediate(operands[2]);
                return new int[] { rs, rt, imm };
            }
            else if (operands.Count == 2)
            {
                // Load/Store format: rt, offset(rs) (e.g., lw $t0, 4($sp))
                var match = Regex.Match(operands[1], @"^(-?\d+)\((.+)\)$");
                if (match.Success)
                {
                    int rt = ParseRegister(operands[0]);
                    int offset = ParseImmediate(match.Groups[1].Value);
                    int rs = ParseRegister(match.Groups[2].Value);
                    return new int[] { rs, rt, offset };
                }
                else
                {
                    // Branch format: rs, rt, offset (simplified to 2 regs)
                    int reg1 = ParseRegister(operands[0]);
                    int reg2 = ParseRegister(operands[1]);
                    return new int[] { reg1, reg2, 0 };
                }
            }
            else if (operands.Count == 1)
            {
                // LUI format: rt, imm
                int rt = ParseRegister(operands[0]);
                return new int[] { 0, rt, 0 };
            }
            else
            {
                throw new ArgumentException($"Invalid I-type operands: {operandStr}");
            }
        }

        /// <summary>
        /// Parses J-type instruction operands
        /// Returns: [address]
        /// </summary>
        private int[] ParseJTypeOperands(string operandStr)
        {
            var operands = SplitOperands(operandStr);
            if (operands.Count != 1)
                throw new ArgumentException($"J-type instruction requires exactly one operand: {operandStr}");

            int address = ParseImmediate(operands[0]);
            return new int[] { address };
        }

        /// <summary>
        /// Splits operand string by commas, handling parentheses correctly
        /// </summary>
        private List<string> SplitOperands(string operandStr)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            int parenDepth = 0;

            foreach (char c in operandStr)
            {
                if (c == '(')
                {
                    parenDepth++;
                    current.Append(c);
                }
                else if (c == ')')
                {
                    parenDepth--;
                    current.Append(c);
                }
                else if (c == ',' && parenDepth == 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                else if (!char.IsWhiteSpace(c) || parenDepth > 0)
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
                result.Add(current.ToString().Trim());

            return result;
        }

        /// <summary>
        /// Parses a register operand
        /// </summary>
        private int ParseRegister(string reg)
        {
            reg = reg.Trim();
            if (_registerMap.TryGetValue(reg, out int regNum))
                return regNum;

            // Try parsing numeric form without $
            if (int.TryParse(reg, out regNum) && regNum >= 0 && regNum < 32)
                return regNum;

            throw new ArgumentException($"Invalid register: {reg}");
        }

        /// <summary>
        /// Parses an immediate value (supports decimal, hex, binary)
        /// </summary>
        private int ParseImmediate(string imm)
        {
            imm = imm.Trim();

            // Hexadecimal
            if (imm.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(imm.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hexValue))
                    return hexValue;
            }
            // Binary
            else if (imm.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return Convert.ToInt32(imm.Substring(2), 2);
                }
                catch
                {
                    throw new ArgumentException($"Invalid binary immediate: {imm}");
                }
            }
            // Decimal (including negative)
            else if (int.TryParse(imm, NumberStyles.Integer, CultureInfo.InvariantCulture, out int decValue))
            {
                return decValue;
            }

            throw new ArgumentException($"Invalid immediate value: {imm}");
        }

        /// <summary>
        /// Information about an instruction for assembly
        /// </summary>
        private class InstructionInfo
        {
            public string Name { get; set; } = string.Empty;
            public int Opcode { get; set; }
            public int? Funct { get; set; }
            public InstrArgType ArgType { get; set; }
            public string[] ArgNames { get; set; } = Array.Empty<string>();
            public Func<int[], uint> AsmFunc { get; set; } = _ => 0;
        }
    }
}
