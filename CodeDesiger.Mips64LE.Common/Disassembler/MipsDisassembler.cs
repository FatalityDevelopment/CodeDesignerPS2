using CodeDesigner.Mips64LE.Common.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.Common.Cpu.Instructions;
using CodeDesigner.Mips64LE.Common.Cpu.Instructions.EE;
using CodeDesigner.Mips64LE.Common.Cpu.Instructions.COP0;
using CodeDesigner.Mips64LE.Common.Instructions.CoprocessorInstructions;
using CodeDesigner.Mips64LE.Common.Instructions.VectorUnitInstructions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE.Common
{
    /// <summary>
    /// Disassembles 32-bit MIPS instruction words into human-readable assembly text
    /// </summary>
    public class MipsDisassembler
    {
        private static readonly Dictionary<(int opcode, int? funct), Instruction<Args_R>> RTypeInstructions = new();
        private static readonly Dictionary<int, Instruction<Args_I>> ITypeInstructions = new();
        private static readonly Dictionary<int, Instruction<Args_J>> JTypeInstructions = new();
        private static readonly Dictionary<(int opcode, int rs, int? funct), Instruction<Args_R>> CopInstructions = new();
        private static readonly Dictionary<(int opcode, int rt), Instruction<Args_I>> RegimmInstructions = new();

        static MipsDisassembler()
        {
            InitializeInstructions();
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
            RegisterR(new DSLL32());
            RegisterR(new DSRL32());
            RegisterR(new DSRA32());

            // R-type compare instructions
            RegisterR(new SLT());
            RegisterR(new SLTU());

            // R-type branch/jump instructions
            RegisterR(new JR());
            RegisterR(new JALR());

            // R-type special instructions
            RegisterR(new MOVZ());
            RegisterR(new MOVN());
            // Note: NOP is not registered as it conflicts with SLL (both have opcode=0, funct=0)
            // NOP is just SLL $zero, $zero, 0 which will disassemble as "sll zero, zero, 0"
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
            RegisterI(new BEQL());
            RegisterI(new BNEL());

            // REGIMM branch instructions (opcode 0x01, differentiated by rt field)
            RegisterRegimm(new BLTZ(), 0x00);    // rt = 0x00
            RegisterRegimm(new BGEZ(), 0x01);    // rt = 0x01
            RegisterRegimm(new BLTZAL(), 0x10);  // rt = 0x10
            RegisterRegimm(new BGEZAL(), 0x11);  // rt = 0x11
            RegisterRegimm(new BLTZALL(), 0x12); // rt = 0x12
            RegisterRegimm(new BGEZALL(), 0x13); // rt = 0x13

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

            // COP0 instructions (opcode 0x10)
            RegisterCop(new MFC0(), 0x00);  // rs = 0x00
            RegisterCop(new DMFC0(), 0x01); // rs = 0x01
            RegisterCop(new CFC0(), 0x02);  // rs = 0x02
            RegisterCop(new MTC0(), 0x04);  // rs = 0x04
            RegisterCop(new DMTC0(), 0x05); // rs = 0x05
            RegisterCop(new CTC0(), 0x06);  // rs = 0x06
            // COP0 CO instructions (rs = 0x10)
            RegisterCop(new TLBR(), 0x10);   // rs = 0x10, funct = 0x01
            RegisterCop(new TLBWI(), 0x10);  // rs = 0x10, funct = 0x02
            RegisterCop(new TLBWR(), 0x10);  // rs = 0x10, funct = 0x06
            RegisterCop(new TLBP(), 0x10);   // rs = 0x10, funct = 0x08
            RegisterCop(new ERET(), 0x10);   // rs = 0x10, funct = 0x18

            // COP1 instructions (opcode 0x11)
            RegisterCop(new MFC1(), 0x00);   // rs = 0x00
            RegisterCop(new DMFC1(), 0x01);  // rs = 0x01
            RegisterCop(new CFC1(), 0x02);   // rs = 0x02
            RegisterCop(new MTC1(), 0x04);   // rs = 0x04
            RegisterCop(new DMTC1(), 0x05);  // rs = 0x05
            RegisterCop(new CTC1(), 0x06);   // rs = 0x06
            // COP1 compute instructions (rs = 0x10 for single-precision)
            RegisterCop(new ADD_S(), 0x10);  // rs = 0x10, funct = 0x00
            RegisterCop(new SUB_S(), 0x10);  // rs = 0x10, funct = 0x01
            RegisterCop(new MUL_S(), 0x10);  // rs = 0x10, funct = 0x02
            RegisterCop(new DIV_S(), 0x10);  // rs = 0x10, funct = 0x03
            RegisterCop(new SQRT_S(), 0x10); // rs = 0x10, funct = 0x04
            RegisterCop(new ABS_S(), 0x10);  // rs = 0x10, funct = 0x05
            RegisterCop(new MOV_S(), 0x10);  // rs = 0x10, funct = 0x06
            RegisterCop(new NEG_S(), 0x10);  // rs = 0x10, funct = 0x07
            RegisterCop(new CVT_S_W(), 0x14); // rs = 0x14, funct = 0x20
            RegisterCop(new CVT_W_S(), 0x10); // rs = 0x10, funct = 0x24
            RegisterCop(new C_EQ_S(), 0x10);  // rs = 0x10, funct = 0x32
            RegisterCop(new C_LT_S(), 0x10);  // rs = 0x10, funct = 0x3C
            RegisterCop(new C_LE_S(), 0x10);  // rs = 0x10, funct = 0x3E

            // COP2/VU0 instructions (opcode 0x12)
            RegisterCop(new VADD(), 0x10);   // rs varies, funct = 0x28
            RegisterCop(new VSUB(), 0x10);   // rs varies, funct = 0x29
            RegisterCop(new VMUL(), 0x10);   // rs varies, funct = 0x2A
            RegisterCop(new VMAX(), 0x10);   // rs varies, funct = 0x2B
            RegisterCop(new VMIN(), 0x10);   // rs varies, funct = 0x2C
            RegisterCop(new VMSUB(), 0x10);  // rs varies, funct = 0x2D
            RegisterCop(new VADDA(), 0x10);  // rs varies, funct = 0x2E
            RegisterCop(new VABS(), 0x10);   // rs varies, funct = 0x2F
            RegisterCop(new VMADD(), 0x10);  // rs varies, funct = 0x30
            RegisterCop(new VMULA(), 0x10);  // rs varies, funct = 0x31
            RegisterCop(new VFTOI0(), 0x10); // rs varies, funct = 0x3C
            RegisterCop(new VITOF0(), 0x10); // rs varies, funct = 0x3D

            // I-type COP1 load/store and branch instructions
            RegisterI(new LWC1());
            RegisterI(new SWC1());
            RegisterI(new BC1F());
            RegisterI(new BC1T());

            // I-type COP0 CACHE instruction
            RegisterI(new CACHE());
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

        private static void RegisterCop(Instruction<Args_R> instr, int rs)
        {
            CopInstructions[(instr.Opcode, rs, instr.Funct)] = instr;
        }

        private static void RegisterRegimm(Instruction<Args_I> instr, int rt)
        {
            RegimmInstructions[(instr.Opcode, rt)] = instr;
        }

        /// <summary>
        /// Disassemble a 32-bit instruction word into assembly text
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word</param>
        /// <param name="pc">Optional program counter for branch/jump address calculation</param>
        /// <returns>Assembly text representation of the instruction</returns>
        public static string Disassemble(uint instruction, uint? pc = null)
        {
            // Check for NOP (0x00000000) explicitly
            if (instruction == 0x00000000)
            {
                return "nop";
            }

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

            // Check for REGIMM instructions (opcode = 0x01)
            if (opcode == 0x01)
            {
                int rt = MipsDecoder.GetRt(instruction);
                if (RegimmInstructions.TryGetValue((opcode, rt), out var regimmInstr))
                {
                    var args = MipsDecoder.DecodeI(instruction);
                    return regimmInstr.Disasm(args, pc);
                }
                return $".word 0x{instruction:X8}  # Unknown REGIMM (rt=0x{rt:X2})";
            }

            // Check for coprocessor instructions (opcode = 0x10, 0x11, 0x12, 0x13)
            if (opcode >= 0x10 && opcode <= 0x13)
            {
                int rs = MipsDecoder.GetRs(instruction);
                int funct = MipsDecoder.GetFunct(instruction);
                
                // Try with funct first (for CO instructions and FPU ops)
                if (CopInstructions.TryGetValue((opcode, rs, funct), out var copInstr))
                {
                    var args = MipsDecoder.DecodeR(instruction);
                    return copInstr.Disasm(args, pc);
                }
                
                // Try without funct (for MFC/MTC type instructions)
                if (CopInstructions.TryGetValue((opcode, rs, null), out copInstr))
                {
                    var args = MipsDecoder.DecodeR(instruction);
                    return copInstr.Disasm(args, pc);
                }

                // Check if it might be a branch instruction (BC1F/BC1T)
                if (opcode == 0x11 && rs == 0x08)
                {
                    // This is handled by I-type instructions (BC1F/BC1T)
                    if (ITypeInstructions.TryGetValue(opcode, out var iInstr))
                    {
                        var args = MipsDecoder.DecodeI(instruction);
                        return iInstr.Disasm(args, pc);
                    }
                }

                return $".word 0x{instruction:X8}  # Unknown COP{opcode - 0x10} (rs=0x{rs:X2}, funct=0x{funct:X2})";
            }

            // Check for J-type instructions
            if (JTypeInstructions.TryGetValue(opcode, out var jInstr))
            {
                var args = MipsDecoder.DecodeJ(instruction);
                return jInstr.Disasm(args, pc);
            }

            // Check for I-type instructions
            if (ITypeInstructions.TryGetValue(opcode, out var iInstr2))
            {
                var args = MipsDecoder.DecodeI(instruction);
                return iInstr2.Disasm(args, pc);
            }

            // Unknown instruction
            return $".word 0x{instruction:X8}  # Unknown (opcode=0x{opcode:X2})";
        }

        /// <summary>
        /// Get the instruction object and decoded args for an instruction word
        /// This allows calling Exec() and Analyze() methods directly
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word</param>
        /// <returns>Tuple of instruction object and args, or (null, null) if unknown</returns>
        public static (IInstruction? instrObj, object? args) GetInstructionObject(uint instruction)
        {
            // Check for NOP (0x00000000) - return NOP instruction
            if (instruction == 0x00000000)
            {
                // NOP is registered as SLL with all zeros, but we can return SLL instruction
                int opcode = MipsDecoder.GetOpcode(instruction);
                int funct = MipsDecoder.GetFunct(instruction);
                if (RTypeInstructions.TryGetValue((opcode, funct), out var nopInstr))
                {
                    var args = MipsDecoder.DecodeR(instruction);
                    return (nopInstr, args);
                }
            }

            int op = MipsDecoder.GetOpcode(instruction);

            // Check for R-type instructions (opcode = 0x00)
            if (op == 0x00)
            {
                int funct = MipsDecoder.GetFunct(instruction);
                if (RTypeInstructions.TryGetValue((op, funct), out var rInstr))
                {
                    var args = MipsDecoder.DecodeR(instruction);
                    return (rInstr, args);
                }
                return (null, null);
            }

            // Check for REGIMM instructions (opcode = 0x01)
            if (op == 0x01)
            {
                int rt = MipsDecoder.GetRt(instruction);
                if (RegimmInstructions.TryGetValue((op, rt), out var regimmInstr))
                {
                    var args = MipsDecoder.DecodeI(instruction);
                    return (regimmInstr, args);
                }
                return (null, null);
            }

            // Check for coprocessor instructions (opcode = 0x10, 0x11, 0x12, 0x13)
            if (op >= 0x10 && op <= 0x13)
            {
                int rs = MipsDecoder.GetRs(instruction);
                int funct = MipsDecoder.GetFunct(instruction);
                
                // Try with funct first (for CO instructions and FPU ops)
                if (CopInstructions.TryGetValue((op, rs, funct), out var copInstr))
                {
                    var args = MipsDecoder.DecodeR(instruction);
                    return (copInstr, args);
                }
                
                // Try without funct (for MFC/MTC type instructions)
                if (CopInstructions.TryGetValue((op, rs, null), out copInstr))
                {
                    var args = MipsDecoder.DecodeR(instruction);
                    return (copInstr, args);
                }

                // Check if it might be a branch instruction (BC1F/BC1T)
                if (op == 0x11 && rs == 0x08)
                {
                    if (ITypeInstructions.TryGetValue(op, out var iInstr))
                    {
                        var args = MipsDecoder.DecodeI(instruction);
                        return (iInstr, args);
                    }
                }

                return (null, null);
            }

            // Check for J-type instructions
            if (JTypeInstructions.TryGetValue(op, out var jInstr))
            {
                var args = MipsDecoder.DecodeJ(instruction);
                return (jInstr, args);
            }

            // Check for I-type instructions
            if (ITypeInstructions.TryGetValue(op, out var iInstr2))
            {
                var args = MipsDecoder.DecodeI(instruction);
                return (iInstr2, args);
            }

            // Unknown instruction
            return (null, null);
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
