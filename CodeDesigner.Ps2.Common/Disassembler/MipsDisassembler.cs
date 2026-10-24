using CodeDesigner.Ps2.Common.Cpu.InstrArgs;
using CodeDesigner.Ps2.Common.Cpu.Instructions;
using CodeDesigner.Ps2.Common.Cpu.Instructions.EE;
using CodeDesigner.Ps2.Common.Cpu.Instructions.COP0;
using CodeDesigner.Ps2.Common.Cpu.Instructions.MMI;
using CodeDesigner.Ps2.Common.CPU;
using CodeDesigner.Ps2.Common.Instructions.CoprocessorInstructions;
using CodeDesigner.Ps2.Common.Instructions.VectorUnitInstructions;
using System;
using System.Collections.Generic;

namespace CodeDesigner.Ps2.Common
{
    /// <summary>
    /// Disassembles 32-bit R5900 (PS2 Emotion Engine) instruction words into
    /// human-readable assembly text. Opcodes the EE doesn't implement (MIPS IV
    /// COP1X, 64-bit multiply/divide, double-precision FPU, …) decode as
    /// <c>.word</c>, so data words don't masquerade as code.
    /// </summary>
    public class MipsDisassembler
    {
        private static readonly Dictionary<(int opcode, int? funct), Instruction<Args_R>> RTypeInstructions = new();
        private static readonly Dictionary<int, Instruction<Args_I>> ITypeInstructions = new();
        private static readonly Dictionary<int, Instruction<Args_J>> JTypeInstructions = new();
        private static readonly Dictionary<(int opcode, int rs, int? funct), Instruction<Args_R>> CopInstructions = new();
        private static readonly Dictionary<(int opcode, int rt), Instruction<Args_I>> RegimmInstructions = new();
        // BCzF/BCzT(L): COPz rs=0x08, rt selects the condition and likely bit
        private static readonly Dictionary<(int opcode, int rt), Instruction<Args_I>> CopBranchInstructions = new();
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
            RegisterR(new DIV());
            RegisterR(new DIVU());
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
            // Note: NOP is not registered as it conflicts with SLL (both have opcode=0, funct=0);
            // the all-zero word is special-cased as "nop".
            RegisterR(new SYNC());
            RegisterR(new MFSA());  // funct=0x28
            RegisterR(new MTSA());  // funct=0x29

            // R-type trap instructions
            RegisterR(new SYSCALL());
            RegisterR(new BREAK());
            RegisterR(new TGE());
            RegisterR(new TGEU());
            RegisterR(new TLT());
            RegisterR(new TLTU());
            RegisterR(new TEQ());
            RegisterR(new TNE());

            // I-type arithmetic / logical / compare
            RegisterI(new ADDI());
            RegisterI(new ADDIU());
            RegisterI(new DADDI());
            RegisterI(new DADDIU());
            RegisterI(new ANDI());
            RegisterI(new ORI());
            RegisterI(new XORI());
            RegisterI(new SLTI());
            RegisterI(new SLTIU());
            RegisterI(new LUI());

            // I-type branch instructions
            RegisterI(new BEQ());
            RegisterI(new BNE());
            RegisterI(new BLEZ());
            RegisterI(new BGTZ());
            RegisterI(new BEQL());
            RegisterI(new BNEL());
            RegisterI(new BLEZL());
            RegisterI(new BGTZL());

            // REGIMM (opcode 0x01, differentiated by rt field)
            RegisterRegimm(new BLTZ(),    0x00);
            RegisterRegimm(new BGEZ(),    0x01);
            RegisterRegimm(new BLTZL(),   0x02);
            RegisterRegimm(new BGEZL(),   0x03);
            RegisterRegimm(new TGEI(),    0x08);
            RegisterRegimm(new TGEIU(),   0x09);
            RegisterRegimm(new TLTI(),    0x0A);
            RegisterRegimm(new TLTIU(),   0x0B);
            RegisterRegimm(new TEQI(),    0x0C);
            RegisterRegimm(new TNEI(),    0x0E);
            RegisterRegimm(new BLTZAL(),  0x10);
            RegisterRegimm(new BGEZAL(),  0x11);
            RegisterRegimm(new BLTZALL(), 0x12);
            RegisterRegimm(new BGEZALL(), 0x13);
            RegisterRegimm(new MTSAB(),   0x18);
            RegisterRegimm(new MTSAH(),   0x19);

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

            // I-type coprocessor load/store, cache and prefetch
            RegisterI(new LWC1());   // opcode 0x31
            RegisterI(new SWC1());   // opcode 0x39
            RegisterI(new VLQ());    // lqc2, opcode 0x36
            RegisterI(new VSQ());    // sqc2, opcode 0x3E
            RegisterI(new CACHE());  // opcode 0x2F
            RegisterI(new PREF());   // opcode 0x33

            // J-type instructions
            RegisterJ(new J());
            RegisterJ(new JAL());

            // COP0 instructions (opcode 0x10)
            RegisterCop(new MFC0(), 0x00);
            RegisterCop(new MTC0(), 0x04);
            RegisterCop(new TLBR(),  0x10);  // funct = 0x01
            RegisterCop(new TLBWI(), 0x10);  // funct = 0x02
            RegisterCop(new TLBWR(), 0x10);  // funct = 0x06
            RegisterCop(new TLBP(),  0x10);  // funct = 0x08
            RegisterCop(new RFE(),   0x10);  // funct = 0x10 (R3000A — IOP code)
            RegisterCop(new ERET(),  0x10);  // funct = 0x18
            RegisterCop(new EI(),    0x10);  // funct = 0x38
            RegisterCop(new DI(),    0x10);  // funct = 0x39

            // COP1 instructions (opcode 0x11)
            RegisterCop(new MFC1(), 0x00);
            RegisterCop(new CFC1(), 0x02);
            RegisterCop(new MTC1(), 0x04);
            RegisterCop(new CTC1(), 0x06);
            // COP1.S (rs = 0x10)
            RegisterCop(new ADD_S(),   0x10);  // funct = 0x00
            RegisterCop(new SUB_S(),   0x10);  // funct = 0x01
            RegisterCop(new MUL_S(),   0x10);  // funct = 0x02
            RegisterCop(new DIV_S(),   0x10);  // funct = 0x03
            RegisterCop(new SQRT_S(),  0x10);  // funct = 0x04
            RegisterCop(new ABS_S(),   0x10);  // funct = 0x05
            RegisterCop(new MOV_S(),   0x10);  // funct = 0x06
            RegisterCop(new NEG_S(),   0x10);  // funct = 0x07
            RegisterCop(new RSQRT_S(), 0x10);  // funct = 0x16
            RegisterCop(new ADDA_S(),  0x10);  // funct = 0x18
            RegisterCop(new SUBA_S(),  0x10);  // funct = 0x19
            RegisterCop(new MULA_S(),  0x10);  // funct = 0x1A
            RegisterCop(new MADD_S(),  0x10);  // funct = 0x1C
            RegisterCop(new MSUB_S(),  0x10);  // funct = 0x1D
            RegisterCop(new MADDA_S(), 0x10);  // funct = 0x1E
            RegisterCop(new MSUBA_S(), 0x10);  // funct = 0x1F
            RegisterCop(new CVT_W_S(), 0x10);  // funct = 0x24
            RegisterCop(new MAX_S(),   0x10);  // funct = 0x28
            RegisterCop(new MIN_S(),   0x10);  // funct = 0x29
            RegisterCop(new C_F(),     0x10);  // funct = 0x30
            RegisterCop(new C_EQ_S(),  0x10);  // funct = 0x32
            RegisterCop(new C_LT_S(),  0x10);  // funct = 0x34
            RegisterCop(new C_LE_S(),  0x10);  // funct = 0x36
            // COP1.W (rs = 0x14)
            RegisterCop(new CVT_S_W(), 0x14);  // funct = 0x20

            // COP2 register moves (opcode 0x12); macro-mode ops dispatch through Vu0Macro
            RegisterCop(new QMFC2(), 0x01);
            RegisterCop(new CFC2(),  0x02);
            RegisterCop(new QMTC2(), 0x05);
            RegisterCop(new CTC2(),  0x06);

            // BCzF / BCzT / BCzFL / BCzTL
            CopBranchInstructions[(0x11, 0)] = new BC1F();
            CopBranchInstructions[(0x11, 1)] = new BC1T();
            CopBranchInstructions[(0x11, 2)] = new BC1FL();
            CopBranchInstructions[(0x11, 3)] = new BC1TL();
            for (int rt = 0; rt < 4; rt++)
            {
                CopBranchInstructions[(0x10, rt)] = new CopBranch(0, rt);
                CopBranchInstructions[(0x12, rt)] = new CopBranch(2, rt);
            }

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
            RegisterR(new PMFHL());   // funct = 0x30
            RegisterR(new PMTHL());   // funct = 0x31
            RegisterR(new PSLLH());   // funct = 0x34
            RegisterR(new PSRLH());   // funct = 0x36
            RegisterR(new PSRAH());   // funct = 0x37
            RegisterR(new PSLLW());   // funct = 0x3C
            RegisterR(new PSRLW());   // funct = 0x3E
            RegisterR(new PSRAW());   // funct = 0x3F

            // MMI0 sub-group (funct=0x08, shamt=sub-opcode)
            RegisterMmiSub(new PADDW(),  0x08, 0x00);
            RegisterMmiSub(new PSUBW(),  0x08, 0x01);
            RegisterMmiSub(new PCGTW(),  0x08, 0x02);
            RegisterMmiSub(new PMAXW(),  0x08, 0x03);
            RegisterMmiSub(new PADDH(),  0x08, 0x04);
            RegisterMmiSub(new PSUBH(),  0x08, 0x05);
            RegisterMmiSub(new PCGTH(),  0x08, 0x06);
            RegisterMmiSub(new PMAXH(),  0x08, 0x07);
            RegisterMmiSub(new PADDB(),  0x08, 0x08);
            RegisterMmiSub(new PSUBB(),  0x08, 0x09);
            RegisterMmiSub(new PCGTB(),  0x08, 0x0A);
            RegisterMmiSub(new PADDSW(), 0x08, 0x10);
            RegisterMmiSub(new PSUBSW(), 0x08, 0x11);
            RegisterMmiSub(new PEXTLW(), 0x08, 0x12);
            RegisterMmiSub(new PPACW(),  0x08, 0x13);
            RegisterMmiSub(new PADDSH(), 0x08, 0x14);
            RegisterMmiSub(new PSUBSH(), 0x08, 0x15);
            RegisterMmiSub(new PEXTLH(), 0x08, 0x16);
            RegisterMmiSub(new PPACH(),  0x08, 0x17);
            RegisterMmiSub(new PADDSB(), 0x08, 0x18);
            RegisterMmiSub(new PSUBSB(), 0x08, 0x19);
            RegisterMmiSub(new PEXTLB(), 0x08, 0x1A);
            RegisterMmiSub(new PPACB(),  0x08, 0x1B);
            RegisterMmiSub(new PEXT5(),  0x08, 0x1E);
            RegisterMmiSub(new PPAC5(),  0x08, 0x1F);

            // MMI1 sub-group (funct=0x28)
            RegisterMmiSub(new PABSW(),  0x28, 0x01);
            RegisterMmiSub(new PCEQW(),  0x28, 0x02);
            RegisterMmiSub(new PMINW(),  0x28, 0x03);
            RegisterMmiSub(new PADSBH(), 0x28, 0x04);
            RegisterMmiSub(new PABSH(),  0x28, 0x05);
            RegisterMmiSub(new PCEQH(),  0x28, 0x06);
            RegisterMmiSub(new PMINH(),  0x28, 0x07);
            RegisterMmiSub(new PCEQB(),  0x28, 0x0A);
            RegisterMmiSub(new PADDUW(), 0x28, 0x10);
            RegisterMmiSub(new PSUBUW(), 0x28, 0x11);
            RegisterMmiSub(new PEXTUW(), 0x28, 0x12);
            RegisterMmiSub(new PADDUH(), 0x28, 0x14);
            RegisterMmiSub(new PSUBUH(), 0x28, 0x15);
            RegisterMmiSub(new PEXTUH(), 0x28, 0x16);
            RegisterMmiSub(new PADDUB(), 0x28, 0x18);
            RegisterMmiSub(new PSUBUB(), 0x28, 0x19);
            RegisterMmiSub(new PEXTUB(), 0x28, 0x1A);
            RegisterMmiSub(new QFSRV(),  0x28, 0x1B);

            // MMI2 sub-group (funct=0x09)
            RegisterMmiSub(new PMADDW(), 0x09, 0x00);
            RegisterMmiSub(new PSLLVW(), 0x09, 0x02);
            RegisterMmiSub(new PSRLVW(), 0x09, 0x03);
            RegisterMmiSub(new PMSUBW(), 0x09, 0x04);
            RegisterMmiSub(new PMFHI(),  0x09, 0x08);
            RegisterMmiSub(new PMFLO(),  0x09, 0x09);
            RegisterMmiSub(new PINTH(),  0x09, 0x0A);
            RegisterMmiSub(new PMULTW(), 0x09, 0x0C);
            RegisterMmiSub(new PDIVW(),  0x09, 0x0D);
            RegisterMmiSub(new PCPYLD(), 0x09, 0x0E);
            RegisterMmiSub(new PMADDH(), 0x09, 0x10);
            RegisterMmiSub(new PHMADH(), 0x09, 0x11);
            RegisterMmiSub(new PAND(),   0x09, 0x12);
            RegisterMmiSub(new PXOR(),   0x09, 0x13);
            RegisterMmiSub(new PMSUBH(), 0x09, 0x14);
            RegisterMmiSub(new PHMSBH(), 0x09, 0x15);
            RegisterMmiSub(new PEXEH(),  0x09, 0x1A);
            RegisterMmiSub(new PREVH(),  0x09, 0x1B);
            RegisterMmiSub(new PMULTH(), 0x09, 0x1C);
            RegisterMmiSub(new PDIVBW(), 0x09, 0x1D);
            RegisterMmiSub(new PEXEW(),  0x09, 0x1E);
            RegisterMmiSub(new PROT3W(), 0x09, 0x1F);

            // MMI3 sub-group (funct=0x29)
            RegisterMmiSub(new PMADDUW(), 0x29, 0x00);
            RegisterMmiSub(new PSRAVW(),  0x29, 0x03);
            RegisterMmiSub(new PMTHI(),   0x29, 0x08);
            RegisterMmiSub(new PMTLO(),   0x29, 0x09);
            RegisterMmiSub(new PINTEH(),  0x29, 0x0A);
            RegisterMmiSub(new PMULTUW(), 0x29, 0x0C);
            RegisterMmiSub(new PDIVUW(),  0x29, 0x0D);
            RegisterMmiSub(new PCPYUD(),  0x29, 0x0E);
            RegisterMmiSub(new POR(),     0x29, 0x12);
            RegisterMmiSub(new PNOR(),    0x29, 0x13);
            RegisterMmiSub(new PEXCH(),   0x29, 0x1A);
            RegisterMmiSub(new PCPYH(),   0x29, 0x1B);
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
        /// The one decode path behind <see cref="Disassemble"/> and
        /// <see cref="GetInstructionObject"/>. Returns the instruction and its decoded
        /// args, or (null, null, reason) for an unassigned encoding.
        /// </summary>
        private static (IInstruction? instr, object? args, string unknown) Resolve(uint instruction)
        {
            int opcode = MipsDecoder.GetOpcode(instruction);
            int funct  = MipsDecoder.GetFunct(instruction);
            int rs     = MipsDecoder.GetRs(instruction);
            int rt     = MipsDecoder.GetRt(instruction);

            switch (opcode)
            {
                case 0x00:
                    return RTypeInstructions.TryGetValue((opcode, funct), out var r)
                        ? (r, MipsDecoder.DecodeR(instruction), "")
                        : (null, null, $"Unknown R-type (funct=0x{funct:X2})");

                case 0x01:
                    return RegimmInstructions.TryGetValue((opcode, rt), out var regimm)
                        ? (regimm, MipsDecoder.DecodeI(instruction), "")
                        : (null, null, $"Unknown REGIMM (rt=0x{rt:X2})");

                case 0x1C:
                {
                    // pmfhl formats are .lw/.uw/.slw/.lh/.sh (sa 0-4); pmthl only has .lw.
                    int sa = MipsDecoder.GetShamt(instruction);
                    if ((funct == 0x30 && sa > 4) || (funct == 0x31 && sa != 0))
                        return (null, null, $"Unknown MMI format (funct=0x{funct:X2}, sa={sa})");
                    if (RTypeInstructions.TryGetValue((opcode, funct), out var mmi))
                        return (mmi, MipsDecoder.DecodeR(instruction), "");
                    if (funct is 0x08 or 0x09 or 0x28 or 0x29 &&
                        MmiSubInstructions.TryGetValue((funct, MipsDecoder.GetShamt(instruction)), out var sub))
                        return (sub, MipsDecoder.DecodeR(instruction), "");
                    return (null, null, $"Unknown MMI (funct=0x{funct:X2})");
                }

                case 0x10:
                case 0x11:
                case 0x12:
                {
                    if (opcode == 0x12 && (rs & 0x10) != 0)
                    {
                        var vu = Vu0Macro.Lookup(instruction);
                        return vu != null
                            ? (vu, MipsDecoder.DecodeR(instruction), "")
                            : (null, null, $"Unknown COP2 macro (funct=0x{funct:X2})");
                    }
                    if (rs == 0x08)
                    {
                        return CopBranchInstructions.TryGetValue((opcode, rt), out var bc)
                            ? (bc, MipsDecoder.DecodeI(instruction), "")
                            : (null, null, $"Unknown COP{opcode - 0x10} branch (rt=0x{rt:X2})");
                    }
                    if (CopInstructions.TryGetValue((opcode, rs, funct), out var cop) ||
                        CopInstructions.TryGetValue((opcode, rs, null), out cop))
                        return (cop, MipsDecoder.DecodeR(instruction), "");
                    return (null, null, $"Unknown COP{opcode - 0x10} (rs=0x{rs:X2}, funct=0x{funct:X2})");
                }
            }

            if (JTypeInstructions.TryGetValue(opcode, out var j))
                return (j, MipsDecoder.DecodeJ(instruction), "");
            if (ITypeInstructions.TryGetValue(opcode, out var i))
                return (i, MipsDecoder.DecodeI(instruction), "");
            return (null, null, $"Unknown (opcode=0x{opcode:X2})");
        }

        /// <summary>
        /// Disassemble a 32-bit instruction word into assembly text
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word</param>
        /// <param name="pc">Optional program counter for branch/jump address calculation</param>
        /// <returns>Assembly text representation of the instruction</returns>
        public static string Disassemble(uint instruction, uint? pc = null)
        {
            if (instruction == 0x00000000)
            {
                return "nop";
            }

            var (instr, args, unknown) = Resolve(instruction);
            if (instr == null || args == null)
            {
                return $".word 0x{instruction:X8}  # {unknown}";
            }

            string text = instr.DisasmGeneric(args, pc);
            return PseudoOp(instruction, text) ?? text;
        }

        /// <summary>
        /// Common assembler pseudo-instructions shown in place of the real encoding:
        /// move, li, not, neg/negu, b, bal, beqz/bnez(l), sync.p. Returns null when
        /// <paramref name="word"/> isn't one. Every form is accepted by
        /// <see cref="MipsAssembler"/>.
        /// </summary>
        private static string? PseudoOp(uint word, string text)
        {
            int op    = (int)(word >> 26);
            int rs    = (int)((word >> 21) & 0x1F);
            int rt    = (int)((word >> 16) & 0x1F);
            int rd    = (int)((word >> 11) & 0x1F);
            int sa    = (int)((word >> 6) & 0x1F);
            int funct = (int)(word & 0x3F);
            static string R(int r) => CpuHelpers.RegName(r);

            switch (op)
            {
                case 0x00:
                    switch (funct)
                    {
                        case 0x21:                         // addu rd, rs, $zero (what `move` assembles to)
                            // or/daddu moves stay as written: daddu keeps all 64 bits, so
                            // folding them into `move` would change the re-assembled word.
                            return rt == 0 ? $"move {R(rd)}, {R(rs)}" : null;
                        case 0x27:                         // nor rd, rs, $zero
                            return rt == 0 ? $"not {R(rd)}, {R(rs)}" : null;
                        case 0x22:                         // sub rd, $zero, rt
                            return rs == 0 ? $"neg {R(rd)}, {R(rt)}" : null;
                        case 0x23:                         // subu rd, $zero, rt
                            return rs == 0 ? $"negu {R(rd)}, {R(rt)}" : null;
                        case 0x0F:                         // sync stype
                            return sa == 0x10 ? "sync.p" : null;
                    }
                    return null;

                case 0x09:                                 // addiu rt, $zero, imm
                    return rs == 0 ? $"li {R(rt)}, {CpuHelpers.FormatSignedImmediate((int)(word & 0xFFFF))}" : null;
                case 0x0D:                                 // ori rt, $zero, imm
                    return rs == 0 ? $"li {R(rt)}, ${word & 0xFFFF:X4}" : null;

                case 0x04:                                 // beq
                case 0x05:                                 // bne
                case 0x14:                                 // beql
                case 0x15:                                 // bnel
                {
                    string real = $"{text.Split(' ')[0]} {R(rs)}, {R(rt)}, ";
                    if (!text.StartsWith(real, StringComparison.Ordinal)) return null;
                    string target = text[real.Length..];
                    if (op == 0x04 && rs == 0 && rt == 0) return $"b {target}";
                    if (rt != 0) return null;
                    string name = op switch { 0x04 => "beqz", 0x05 => "bnez", 0x14 => "beqzl", _ => "bnezl" };
                    return $"{name} {R(rs)}, {target}";
                }

                case 0x01:                                 // bgezal $zero = bal
                {
                    string real = "bgezal zero, ";
                    return rt == 0x11 && rs == 0 && text.StartsWith(real, StringComparison.Ordinal)
                        ? $"bal {text[real.Length..]}"
                        : null;
                }
            }
            return null;
        }

        /// <summary>
        /// Get the instruction object and decoded args for an instruction word
        /// This allows calling Exec() and Analyze() methods directly
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word</param>
        /// <returns>Tuple of instruction object and args, or (null, null) if unknown</returns>
        public static (IInstruction? instrObj, object? args) GetInstructionObject(uint instruction)
        {
            var (instr, args, _) = Resolve(instruction);
            return (instr, args);
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
