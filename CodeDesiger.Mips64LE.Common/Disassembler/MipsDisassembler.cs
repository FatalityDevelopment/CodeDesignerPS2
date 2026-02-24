using CodeDesigner.Mips64LE.Common.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.Common.Cpu.Instructions;
using CodeDesigner.Mips64LE.Common.Cpu.Instructions.EE;
using CodeDesigner.Mips64LE.Common.Cpu.Instructions.COP0;
using CodeDesigner.Mips64LE.Common.Cpu.Instructions.MMI;
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
        // BC1F/BC1T share opcode=0x11 with other COP1 instructions; rt distinguishes them
        private static readonly Dictionary<int, Instruction<Args_I>> Bc1BranchInstructions = new();
        // MMI sub-instructions (opcode=0x1C, funct=MMI group, shamt=sub-opcode)
        private static readonly Dictionary<(int funct, int shamt), Instruction<Args_R>> MmiSubInstructions = new();

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
            // Additional VU0 instructions
            RegisterCop(new VSUBA(), 0x10);   // funct = 0x32
            RegisterCop(new VOPMULA(), 0x10); // funct = 0x33
            RegisterCop(new VOPMSUB(), 0x10); // funct = 0x34
            RegisterCop(new VDIV(), 0x10);    // funct = 0x35
            RegisterCop(new VSQRT(), 0x10);   // funct = 0x36
            RegisterCop(new VRSQRT(), 0x10);  // funct = 0x37
            RegisterCop(new VMOVE(), 0x10);   // funct = 0x38
            RegisterCop(new VMR32(), 0x10);   // funct = 0x39
            RegisterCop(new VNOP(), 0x10);    // funct = 0x3A
            RegisterCop(new VCLIPW(), 0x10);  // funct = 0x3B

            // I-type COP1 load/store and branch instructions
            RegisterI(new LWC1());
            RegisterI(new SWC1());
            RegisterI(new LDC1());  // opcode = 0x35
            RegisterI(new SDC1());  // opcode = 0x3D
            // BC1F (rt=0) and BC1T (rt=1) share opcode=0x11 with other COP1 instructions;
            // register them in the dedicated rt-keyed dictionary so they can be distinguished
            Bc1BranchInstructions[0] = new BC1F();
            Bc1BranchInstructions[1] = new BC1T();
            Bc1BranchInstructions[2] = new BC1FL();
            Bc1BranchInstructions[3] = new BC1TL();

            // I-type COP2 load/store
            RegisterI(new VLQ());  // opcode = 0x36
            RegisterI(new VSQ());  // opcode = 0x3E

            // I-type COP0 CACHE instruction
            RegisterI(new CACHE());

            // Additional COP1 single-precision instructions (rs = 0x10)
            RegisterCop(new RSQRT_S(), 0x10);   // funct = 0x16
            RegisterCop(new ROUND_W_S(), 0x10); // funct = 0x0C
            RegisterCop(new TRUNC_W_S(), 0x10); // funct = 0x0D
            RegisterCop(new CEIL_W_S(), 0x10);  // funct = 0x0E
            RegisterCop(new FLOOR_W_S(), 0x10); // funct = 0x0F
            RegisterCop(new ADDA_S(), 0x10);    // funct = 0x18
            RegisterCop(new SUBA_S(), 0x10);    // funct = 0x19
            RegisterCop(new MULA_S(), 0x10);    // funct = 0x1A
            RegisterCop(new MADD_S(), 0x10);    // funct = 0x1C
            RegisterCop(new MSUB_S(), 0x10);    // funct = 0x1D
            RegisterCop(new MADDA_S(), 0x10);   // funct = 0x1E
            RegisterCop(new MSUBA_S(), 0x10);   // funct = 0x1F
            RegisterCop(new MAX_S(), 0x10);     // funct = 0x28
            RegisterCop(new MIN_S(), 0x10);     // funct = 0x29

            // MMI (EE Multimedia) direct instructions (opcode = 0x1C)
            RegisterR(new MADD());    // funct = 0x00
            RegisterR(new MADDU());   // funct = 0x01
            RegisterR(new PLZCW());   // funct = 0x04
            RegisterR(new MFHI1());   // funct = 0x10
            RegisterR(new MTHI1());   // funct = 0x11
            RegisterR(new MFLO1());   // funct = 0x12
            RegisterR(new MTLO1());   // funct = 0x13
            RegisterR(new MULT1());   // funct = 0x18
            RegisterR(new MULTU1());  // funct = 0x19
            RegisterR(new DIV1());    // funct = 0x1A
            RegisterR(new DIVU1());   // funct = 0x1B
            RegisterR(new MADD1());   // funct = 0x20
            RegisterR(new MADDU1());  // funct = 0x21

            // MMI0 sub-group (funct=0x08, shamt=sub-opcode)
            RegisterMmiSub(new PADDW(),  0x08, 0x00);
            RegisterMmiSub(new PSUBW(),  0x08, 0x01);
            RegisterMmiSub(new PCGTW(),  0x08, 0x02);
            RegisterMmiSub(new PMAXW(),  0x08, 0x03);
            RegisterMmiSub(new PADDH(),  0x08, 0x04);
            RegisterMmiSub(new PSUBH(),  0x08, 0x05);
            RegisterMmiSub(new PMAXH(),  0x08, 0x07);
            RegisterMmiSub(new PADDB(),  0x08, 0x08);
            RegisterMmiSub(new PSUBB(),  0x08, 0x09);
            RegisterMmiSub(new PEXTLW(), 0x08, 0x12);
            RegisterMmiSub(new PPACW(),  0x08, 0x13);
            RegisterMmiSub(new PEXTLH(), 0x08, 0x16);
            RegisterMmiSub(new PPACH(),  0x08, 0x17);
            RegisterMmiSub(new PEXTLB(), 0x08, 0x1A);
            RegisterMmiSub(new PPACB(),  0x08, 0x1B);

            // MMI1 sub-group (funct=0x28, shamt=sub-opcode)
            RegisterMmiSub(new PABSW(),  0x28, 0x01);
            RegisterMmiSub(new PCEQW(),  0x28, 0x02);
            RegisterMmiSub(new PMINW(),  0x28, 0x03);
            RegisterMmiSub(new PABSH(),  0x28, 0x05);
            RegisterMmiSub(new PCEQH(),  0x28, 0x06);
            RegisterMmiSub(new PMINH(),  0x28, 0x07);
            RegisterMmiSub(new PCEQB(),  0x28, 0x0A);
            RegisterMmiSub(new PADDUW(), 0x28, 0x10);
            RegisterMmiSub(new PSUBUW(), 0x28, 0x11);
            RegisterMmiSub(new PADDUH(), 0x28, 0x14);
            RegisterMmiSub(new PSUBUH(), 0x28, 0x15);
            RegisterMmiSub(new PADDUB(), 0x28, 0x18);
            RegisterMmiSub(new PSUBUB(), 0x28, 0x19);

            // MMI2 sub-group (funct=0x09, shamt=sub-opcode)
            RegisterMmiSub(new PMFHI(),  0x09, 0x08);
            RegisterMmiSub(new PMFLO(),  0x09, 0x09);
            RegisterMmiSub(new PCPYLD(), 0x09, 0x0E);

            // MMI3 sub-group (funct=0x29, shamt=sub-opcode)
            RegisterMmiSub(new PMTHI(),  0x29, 0x08);
            RegisterMmiSub(new PMTLO(),  0x29, 0x09);
            RegisterMmiSub(new PCPYUD(), 0x29, 0x0E);
            RegisterMmiSub(new PAND(),   0x09, 0x12);
            RegisterMmiSub(new POR(),    0x29, 0x12);
            RegisterMmiSub(new PCPYH(),  0x29, 0x1B);

            // New branch likely instructions (REGIMM)
            RegisterRegimm(new BLTZL(), 0x02);
            RegisterRegimm(new BGEZL(), 0x03);

            // Trap-on-immediate (REGIMM)
            RegisterRegimm(new TGEI(),  0x08);
            RegisterRegimm(new TGEIU(), 0x09);
            RegisterRegimm(new TLTI(),  0x0A);
            RegisterRegimm(new TLTIU(), 0x0B);
            RegisterRegimm(new TEQI(),  0x0C);
            RegisterRegimm(new TNEI(),  0x0E);

            // SA register move REGIMM
            RegisterRegimm(new MTSAB(), 0x18);
            RegisterRegimm(new MTSAH(), 0x19);

            // Branch likely I-type
            RegisterI(new BLEZL());  // opcode=0x16
            RegisterI(new BGTZL());  // opcode=0x17

            // SPECIAL SA instructions
            RegisterR(new MFSA());  // funct=0x28
            RegisterR(new MTSA());  // funct=0x29

            // COP0: EI, DI
            RegisterCop(new EI(), 0x10);  // funct=0x38
            RegisterCop(new DI(), 0x10);  // funct=0x39

            // COP1: Add C_F
            RegisterCop(new C_F(), 0x10);  // funct=0x30

            // COP2 register moves
            RegisterCop(new QMFC2(), 0x01);  // rs=0x01
            RegisterCop(new CFC2(),  0x02);  // rs=0x02
            RegisterCop(new QMTC2(), 0x05);  // rs=0x05
            RegisterCop(new CTC2(),  0x06);  // rs=0x06

            // VU0 additional float conversion
            RegisterCop(new VFTOI4(), 0x10);  // funct=0x3E
            RegisterCop(new VITOF4(), 0x10);  // funct=0x3F

            // MMI direct instructions: shift and move
            RegisterR(new PMFHL());   // funct=0x30
            RegisterR(new PMTHL());   // funct=0x31
            RegisterR(new PSLLH());   // funct=0x34
            RegisterR(new PSRLH());   // funct=0x36
            RegisterR(new PSRAH());   // funct=0x37
            RegisterR(new PSLLW());   // funct=0x3C
            RegisterR(new PSRLW());   // funct=0x3E
            RegisterR(new PSRAW());   // funct=0x3F

            // MMI0 new sub-instructions (funct=0x08)
            RegisterMmiSub(new PCGTH(),  0x08, 0x06);
            RegisterMmiSub(new PCGTB(),  0x08, 0x0A);
            RegisterMmiSub(new PADDSW(), 0x08, 0x10);
            RegisterMmiSub(new PSUBSW(), 0x08, 0x11);
            RegisterMmiSub(new PADDSH(), 0x08, 0x14);
            RegisterMmiSub(new PSUBSH(), 0x08, 0x15);
            RegisterMmiSub(new PADDSB(), 0x08, 0x18);
            RegisterMmiSub(new PSUBSB(), 0x08, 0x19);
            RegisterMmiSub(new PEXT5(),  0x08, 0x1E);
            RegisterMmiSub(new PPAC5(),  0x08, 0x1F);

            // MMI1 new sub-instructions (funct=0x28)
            RegisterMmiSub(new PADSBH(), 0x28, 0x04);
            RegisterMmiSub(new PEXTUW(), 0x28, 0x12);
            RegisterMmiSub(new PEXTUH(), 0x28, 0x16);
            RegisterMmiSub(new PEXTUB(), 0x28, 0x1A);
            RegisterMmiSub(new QFSRV(),  0x28, 0x1B);

            // MMI2 new sub-instructions (funct=0x09)
            RegisterMmiSub(new PMADDW(), 0x09, 0x00);
            RegisterMmiSub(new PSLLVW(), 0x09, 0x02);
            RegisterMmiSub(new PSRLVW(), 0x09, 0x03);
            RegisterMmiSub(new PMSUBW(), 0x09, 0x04);
            RegisterMmiSub(new PINTH(),  0x09, 0x0A);
            RegisterMmiSub(new PMULTW(), 0x09, 0x0C);
            RegisterMmiSub(new PDIVW(),  0x09, 0x0D);
            RegisterMmiSub(new PMADDH(), 0x09, 0x10);
            RegisterMmiSub(new PHMADH(), 0x09, 0x11);
            RegisterMmiSub(new PXOR(),   0x09, 0x13);
            RegisterMmiSub(new PMSUBH(), 0x09, 0x14);
            RegisterMmiSub(new PHMSBH(), 0x09, 0x15);
            RegisterMmiSub(new PEXEH(),  0x09, 0x1A);
            RegisterMmiSub(new PREVH(),  0x09, 0x1B);
            RegisterMmiSub(new PMULTH(), 0x09, 0x1C);
            RegisterMmiSub(new PDIVBW(), 0x09, 0x1D);
            RegisterMmiSub(new PEXEW(),  0x09, 0x1E);
            RegisterMmiSub(new PROT3W(), 0x09, 0x1F);

            // MMI3 new sub-instructions (funct=0x29)
            RegisterMmiSub(new PMADDUW(), 0x29, 0x00);
            RegisterMmiSub(new PSRAVW(),  0x29, 0x03);
            RegisterMmiSub(new PINTEH(),  0x29, 0x0A);
            RegisterMmiSub(new PMULTUW(), 0x29, 0x0C);
            RegisterMmiSub(new PDIVUW(),  0x29, 0x0D);
            RegisterMmiSub(new PNOR(),    0x29, 0x13);
            RegisterMmiSub(new PEXCH(),   0x29, 0x1A);
            RegisterMmiSub(new PEXCW(),   0x29, 0x1E);
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

        private static void RegisterMmiSub(Instruction<Args_R> instr, int funct, int shamt)
        {
            MmiSubInstructions[(funct, shamt)] = instr;
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

            // Check for MMI (EE Multimedia) instructions (opcode = 0x1C)
            if (opcode == 0x1C)
            {
                int funct = MipsDecoder.GetFunct(instruction);
                // Try direct MMI instruction (unique funct)
                if (RTypeInstructions.TryGetValue((opcode, funct), out var mmiInstr))
                {
                    var args = MipsDecoder.DecodeR(instruction);
                    return mmiInstr.Disasm(args, pc);
                }
                // MMI sub-groups use shamt as sub-opcode
                if (funct == 0x08 || funct == 0x09 || funct == 0x28 || funct == 0x29)
                {
                    int shamt = MipsDecoder.GetShamt(instruction);
                    if (MmiSubInstructions.TryGetValue((funct, shamt), out var mmiSubInstr))
                    {
                        var args = MipsDecoder.DecodeR(instruction);
                        return mmiSubInstr.Disasm(args, pc);
                    }
                }
                return $".word 0x{instruction:X8}  # Unknown MMI (funct=0x{funct:X2})";
            }

            // Check for coprocessor instructions (opcode = 0x10, 0x11, 0x12, 0x13)
            if (opcode >= 0x10 && opcode <= 0x13)            {
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
                    int rt = MipsDecoder.GetRt(instruction);
                    if (Bc1BranchInstructions.TryGetValue(rt, out var bc1Instr))
                    {
                        var args = MipsDecoder.DecodeI(instruction);
                        return bc1Instr.Disasm(args, pc);
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

            // Check for MMI (EE Multimedia) instructions (opcode = 0x1C)
            if (op == 0x1C)
            {
                int funct = MipsDecoder.GetFunct(instruction);
                if (RTypeInstructions.TryGetValue((op, funct), out var mmiInstr))
                {
                    var args = MipsDecoder.DecodeR(instruction);
                    return (mmiInstr, args);
                }
                if (funct == 0x08 || funct == 0x09 || funct == 0x28 || funct == 0x29)
                {
                    int shamt = MipsDecoder.GetShamt(instruction);
                    if (MmiSubInstructions.TryGetValue((funct, shamt), out var mmiSubInstr))
                    {
                        var args = MipsDecoder.DecodeR(instruction);
                        return (mmiSubInstr, args);
                    }
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
                    int rt = MipsDecoder.GetRt(instruction);
                    if (Bc1BranchInstructions.TryGetValue(rt, out var bc1Instr))
                    {
                        var args = MipsDecoder.DecodeI(instruction);
                        return (bc1Instr, args);
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
