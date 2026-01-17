using CodeDesigner.Mips64LE.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.Cpu.Instructions;
using CodeDesigner.Mips64LE.Cpu.Instructions.EE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE
{
    /// <summary>
    /// Disassembles 32-bit MIPS instruction words into human-readable assembly text
    /// </summary>
    public class MipsDisassembler
    {
        private static readonly Dictionary<(int opcode, int? funct), Instruction<Args_R>> RTypeInstructions = new();
        private static readonly Dictionary<int, Instruction<Args_I>> ITypeInstructions = new();
        private static readonly Dictionary<int, Instruction<Args_J>> JTypeInstructions = new();

        static MipsDisassembler()
        {
            InitializeInstructions();
        }

        private static void InitializeInstructions()
        {
            // R-type instructions (opcode = 0x00)
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

        private static void RegisterR(Instruction<Args_R> instr)
        {
            RTypeInstructions[(instr.Opcode, instr.Funct)] = instr;
        }

        private static void RegisterI(Instruction<Args_I> instr)
        {
            ITypeInstructions[instr.Opcode] = instr;
        }

        private static void RegisterJ(Instruction<Args_J> instr)
        {
            JTypeInstructions[instr.Opcode] = instr;
        }

        /// <summary>
        /// Disassemble a 32-bit instruction word into assembly text
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word</param>
        /// <param name="pc">Optional program counter for branch/jump address calculation</param>
        /// <returns>Assembly text representation of the instruction</returns>
        public static string Disassemble(uint instruction, uint? pc = null)
        {
            int opcode = MipsDecoder.GetOpcode(instruction);

            // Check for R-type instructions (opcode = 0x00)
            if (opcode == 0x00)
            {
                int funct = MipsDecoder.GetFunct(instruction);
                if (RTypeInstructions.TryGetValue((opcode, funct), out var rInstr))
                {
                    var args = MipsDecoder.DecodeR(instruction);
                    return rInstr.Disasm(args, pc);
                }
                return $".word 0x{instruction:X8}  # Unknown R-type (funct=0x{funct:X2})";
            }

            // Check for J-type instructions
            if (JTypeInstructions.TryGetValue(opcode, out var jInstr))
            {
                var args = MipsDecoder.DecodeJ(instruction);
                return jInstr.Disasm(args, pc);
            }

            // Check for I-type instructions
            if (ITypeInstructions.TryGetValue(opcode, out var iInstr))
            {
                var args = MipsDecoder.DecodeI(instruction);
                return iInstr.Disasm(args, pc);
            }

            // Unknown instruction
            return $".word 0x{instruction:X8}  # Unknown (opcode=0x{opcode:X2})";
        }

        /// <summary>
        /// Disassemble multiple instructions from a byte array
        /// </summary>
        /// <param name="code">Byte array containing instruction words (little-endian)</param>
        /// <param name="startPc">Starting program counter</param>
        /// <returns>Array of disassembled instructions</returns>
        public static string[] DisassembleBlock(byte[] code, uint startPc = 0)
        {
            if (code.Length % 4 != 0)
            {
                throw new ArgumentException("Code length must be a multiple of 4 bytes");
            }

            var result = new List<string>();
            uint pc = startPc;

            for (int i = 0; i < code.Length; i += 4)
            {
                // Read little-endian 32-bit word
                uint instruction = BitConverter.ToUInt32(code, i);
                string disasm = Disassemble(instruction, pc);
                result.Add($"0x{pc:X8}: {disasm}");
                pc += 4;
            }

            return result.ToArray();
        }
    }
}
