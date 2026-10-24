using CodeDesigner.Ps2.Common.Cpu.InstrArgs;
using CodeDesigner.Ps2.Common.Cpu.Instructions;
using CodeDesigner.Ps2.Common.Cpu.Instructions.COP0;
using CodeDesigner.Ps2.Common.Cpu.Instructions.EE;
using CodeDesigner.Ps2.Common.Cpu.Instructions.MMI;
using CodeDesigner.Ps2.Common.Instructions.CoprocessorInstructions;
using CodeDesigner.Ps2.Common.Instructions.VectorUnitInstructions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeDesigner.Ps2.Common
{
    /// <summary>
    /// Assembles a single line of EE MIPS R5900 assembly text into a 32-bit instruction word.
    ///
    /// <para>Supported operand syntax:</para>
    /// <list type="bullet">
    ///   <item>GPRs: <c>$t0</c>, <c>t0</c>, <c>$8</c>, <c>8</c>, <c>$r8</c>, <c>r8</c>.</item>
    ///   <item>FPRs (COP1): <c>$f12</c> or <c>f12</c>. COP1 control registers: <c>$31</c> / <c>fcr31</c>.</item>
    ///   <item>COP0 registers: numeric (<c>$12</c>) or by name (<c>Status</c>, <c>Cause</c>, <c>EPC</c>, …).</item>
    ///   <item>VU0 registers (COP2): <c>vf1</c> / <c>$vf1</c>, <c>vi1</c> / <c>$vi1</c>.</item>
    ///   <item>Immediates: decimal (<c>16</c>, <c>-16</c>), <c>0x10</c>, or PS2-style <c>$0010</c>.
    ///   Values are limited to 32 bits (at most 8 hex digits); 16-bit fields accept
    ///   <c>-32768..65535</c> as well as sign-extended 32-bit forms such as <c>$FFFFFFF0</c>.</item>
    ///   <item>Memory operands: <c>offset(base)</c>, <c>(base)</c>, or <c>offset, base</c>.</item>
    /// </list>
    ///
    /// <para>
    /// Single-word pseudo-instructions (<c>move</c>, <c>not</c>, <c>neg</c>, <c>negu</c>, <c>b</c>,
    /// <c>beqz</c>, <c>bnez</c>, <c>li</c> with a 16-bit immediate, one-operand <c>jalr</c>,
    /// <c>sync.l</c> / <c>sync.p</c>, <c>lqc2</c> / <c>sqc2</c>) are rewritten here. Multi-word
    /// pseudo-instructions (<c>la</c>, 32-bit <c>li</c>, <c>blt</c>/<c>bgt</c>/<c>ble</c>/<c>bge</c>)
    /// are expanded by the CDS parser because they change the address counter.
    /// </para>
    /// </summary>
    public class MipsAssembler
    {
        private static readonly Dictionary<string, Instruction<Args_R>> RTypeInstructionsByName = new();
        private static readonly Dictionary<string, Instruction<Args_I>> ITypeInstructionsByName = new();
        private static readonly Dictionary<string, Instruction<Args_J>> JTypeInstructionsByName = new();

        // REGIMM (opcode 0x01) instructions: rt field selects the operation.
        private static readonly Dictionary<string, (Instruction<Args_I> Instr, int Rt)> RegimmInstructionsByName = new();

        // Coprocessor R-format instructions (opcodes 0x10 COP0, 0x11 COP1, 0x12 COP2):
        // rs selects the sub-operation / FPU format.
        private static readonly Dictionary<string, (Instruction<Args_R> Instr, int Rs)> CopInstructionsByName = new();

        // BCzF/BCzT/BCzFL/BCzTL: opcode 0x10-0x12, rs = 0x08, rt selects the condition.
        private static readonly Dictionary<string, (int Opcode, int Rt)> CopBranchesByName = new();

        // MMI sub-group instructions: opcode 0x1C, funct selects the group, shamt the operation.
        private static readonly Dictionary<string, (Instruction<Args_R> Instr, int Funct, int Shamt)> MmiSubInstructionsByName = new();

        private static readonly Dictionary<string, int> RegisterMap = new();

        // Mnemonic spellings accepted in addition to the instruction class names.
        private static readonly Dictionary<string, string> MnemonicAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["vlq"] = "lqc2",
            ["vsq"] = "sqc2",
            ["sync.l"] = "sync",
            ["c.f"] = "c.f.s",
            ["c.eq"] = "c.eq.s",
            ["c.lt"] = "c.lt.s",
            ["c.le"] = "c.le.s",
            ["cvt.w"] = "cvt.w.s",
        };

        // mult-family instructions whose rd operand is optional (EE three-operand forms).
        private static readonly HashSet<string> OptionalRdMultiply = new(StringComparer.OrdinalIgnoreCase)
        {
            "mult", "multu", "mult1", "multu1", "madd", "maddu", "madd1", "maddu1"
        };

        private static readonly Regex OffsetBaseRegex = new(@"^(.*?)\s*\(\s*([^()]+)\s*\)$", RegexOptions.Compiled);
        private static readonly Regex FprRegex = new(@"^\$?f(\d{1,2})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex FcrRegex = new(@"^\$?fcr(\d{1,2})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex VfRegex = new(@"^\$?vf(\d{1,2})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ViRegex = new(@"^\$?vi(\d{1,2})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex NumberRegex = new(@"^\$?(\d{1,2})$", RegexOptions.Compiled);

        static MipsAssembler()
        {
            InitializeInstructions();
            InitializeRegisterMap();
        }

        // -------------------------------------------------------------------------
        // Instruction tables
        // -------------------------------------------------------------------------

        private static void InitializeInstructions()
        {
            // ---- SPECIAL (opcode 0x00) ----
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
            RegisterR(new AND());
            RegisterR(new OR());
            RegisterR(new XOR());
            RegisterR(new NOR());
            RegisterR(new SLL());
            RegisterR(new SRL());
            RegisterR(new SRA());
            RegisterR(new DSLL());
            RegisterR(new DSRL());
            RegisterR(new DSRA());
            RegisterR(new DSLL32());
            RegisterR(new DSRL32());
            RegisterR(new DSRA32());
            RegisterR(new SLLV());
            RegisterR(new SRLV());
            RegisterR(new SRAV());
            RegisterR(new DSLLV());
            RegisterR(new DSRLV());
            RegisterR(new DSRAV());
            RegisterR(new SLT());
            RegisterR(new SLTU());
            RegisterR(new JR());
            RegisterR(new JALR());
            RegisterR(new MOVZ());
            RegisterR(new MOVN());
            RegisterR(new NOP());
            RegisterR(new SYNC());
            RegisterR(new MFSA());
            RegisterR(new MTSA());
            RegisterR(new SYSCALL());
            RegisterR(new BREAK());
            RegisterR(new TGE());
            RegisterR(new TGEU());
            RegisterR(new TLT());
            RegisterR(new TLTU());
            RegisterR(new TEQ());
            RegisterR(new TNE());

            // ---- I-type ----
            RegisterI(new ADDI());
            RegisterI(new ADDIU());
            RegisterI(new DADDI());
            RegisterI(new DADDIU());
            RegisterI(new ANDI());
            RegisterI(new ORI());
            RegisterI(new XORI());
            RegisterI(new SLTI());
            RegisterI(new SLTIU());
            RegisterI(new BEQ());
            RegisterI(new BNE());
            RegisterI(new BLEZ());
            RegisterI(new BGTZ());
            RegisterI(new BEQL());
            RegisterI(new BNEL());
            RegisterI(new BLEZL());
            RegisterI(new BGTZL());
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
            RegisterI(new SB());
            RegisterI(new SH());
            RegisterI(new SW());
            RegisterI(new SD());
            RegisterI(new SWL());
            RegisterI(new SWR());
            RegisterI(new SDL());
            RegisterI(new SDR());
            RegisterI(new SQ());
            RegisterI(new LUI());
            RegisterI(new CACHE());
            RegisterI(new PREF());

            // COP1 / COP2 loads and stores (ft / vft operand is a coprocessor register)
            RegisterI(new LWC1());
            RegisterI(new SWC1());
            RegisterI(new VLQ());   // lqc2
            RegisterI(new VSQ());   // sqc2

            // ---- REGIMM (opcode 0x01, rt = sub-opcode) ----
            RegisterRegimm(new BLTZ(), 0x00);
            RegisterRegimm(new BGEZ(), 0x01);
            RegisterRegimm(new BLTZL(), 0x02);
            RegisterRegimm(new BGEZL(), 0x03);
            RegisterRegimm(new TGEI(), 0x08);
            RegisterRegimm(new TGEIU(), 0x09);
            RegisterRegimm(new TLTI(), 0x0A);
            RegisterRegimm(new TLTIU(), 0x0B);
            RegisterRegimm(new TEQI(), 0x0C);
            RegisterRegimm(new TNEI(), 0x0E);
            RegisterRegimm(new BLTZAL(), 0x10);
            RegisterRegimm(new BGEZAL(), 0x11);
            RegisterRegimm(new BLTZALL(), 0x12);
            RegisterRegimm(new BGEZALL(), 0x13);
            RegisterRegimm(new MTSAB(), 0x18);
            RegisterRegimm(new MTSAH(), 0x19);

            // ---- J-type ----
            RegisterJ(new J());
            RegisterJ(new JAL());

            // ---- COP0 (opcode 0x10) ----
            RegisterCop(new MFC0(), 0x00);
            RegisterCop(new MTC0(), 0x04);
            RegisterCop(new TLBR(), 0x10);
            RegisterCop(new TLBWI(), 0x10);
            RegisterCop(new TLBWR(), 0x10);
            RegisterCop(new TLBP(), 0x10);
            RegisterCop(new RFE(), 0x10);
            RegisterCop(new ERET(), 0x10);
            RegisterCop(new EI(), 0x10);
            RegisterCop(new DI(), 0x10);

            // ---- COP1 (opcode 0x11) ----
            RegisterCop(new MFC1(), 0x00);
            RegisterCop(new CFC1(), 0x02);
            RegisterCop(new MTC1(), 0x04);
            RegisterCop(new CTC1(), 0x06);
            // Single-precision format (rs = 0x10)
            RegisterCop(new ADD_S(), 0x10);
            RegisterCop(new SUB_S(), 0x10);
            RegisterCop(new MUL_S(), 0x10);
            RegisterCop(new DIV_S(), 0x10);
            RegisterCop(new SQRT_S(), 0x10);
            RegisterCop(new ABS_S(), 0x10);
            RegisterCop(new MOV_S(), 0x10);
            RegisterCop(new NEG_S(), 0x10);
            RegisterCop(new RSQRT_S(), 0x10);
            RegisterCop(new ADDA_S(), 0x10);
            RegisterCop(new SUBA_S(), 0x10);
            RegisterCop(new MULA_S(), 0x10);
            RegisterCop(new MADD_S(), 0x10);
            RegisterCop(new MSUB_S(), 0x10);
            RegisterCop(new MADDA_S(), 0x10);
            RegisterCop(new MSUBA_S(), 0x10);
            RegisterCop(new CVT_W_S(), 0x10);
            RegisterCop(new MAX_S(), 0x10);
            RegisterCop(new MIN_S(), 0x10);
            RegisterCop(new C_F(), 0x10);
            RegisterCop(new C_EQ_S(), 0x10);
            RegisterCop(new C_LT_S(), 0x10);
            RegisterCop(new C_LE_S(), 0x10);
            // Word format (rs = 0x14)
            RegisterCop(new CVT_S_W(), 0x14);
            // Branch on FPU condition (rs = 0x08, rt = condition)
            for (int cop = 0; cop <= 2; cop++)
            {
                CopBranchesByName[$"bc{cop}f"]  = (0x10 + cop, 0);
                CopBranchesByName[$"bc{cop}t"]  = (0x10 + cop, 1);
                CopBranchesByName[$"bc{cop}fl"] = (0x10 + cop, 2);
                CopBranchesByName[$"bc{cop}tl"] = (0x10 + cop, 3);
            }

            // ---- COP2 register moves (opcode 0x12) ----
            RegisterCop(new QMFC2(), 0x01);
            RegisterCop(new CFC2(), 0x02);
            RegisterCop(new QMTC2(), 0x05);
            RegisterCop(new CTC2(), 0x06);

            // ---- MMI direct (opcode 0x1C, unique funct) ----
            RegisterR(new MADD());
            RegisterR(new MADDU());
            RegisterR(new PLZCW());
            RegisterR(new MFHI1());
            RegisterR(new MTHI1());
            RegisterR(new MFLO1());
            RegisterR(new MTLO1());
            RegisterR(new MULT1());
            RegisterR(new MULTU1());
            RegisterR(new DIV1());
            RegisterR(new DIVU1());
            RegisterR(new MADD1());
            RegisterR(new MADDU1());
            RegisterR(new PMFHL());
            RegisterR(new PMTHL());
            RegisterR(new PSLLH());
            RegisterR(new PSRLH());
            RegisterR(new PSRAH());
            RegisterR(new PSLLW());
            RegisterR(new PSRLW());
            RegisterR(new PSRAW());

            // ---- MMI0 (funct 0x08) ----
            RegisterMmiSub(new PADDW(), 0x08, 0x00);
            RegisterMmiSub(new PSUBW(), 0x08, 0x01);
            RegisterMmiSub(new PCGTW(), 0x08, 0x02);
            RegisterMmiSub(new PMAXW(), 0x08, 0x03);
            RegisterMmiSub(new PADDH(), 0x08, 0x04);
            RegisterMmiSub(new PSUBH(), 0x08, 0x05);
            RegisterMmiSub(new PCGTH(), 0x08, 0x06);
            RegisterMmiSub(new PMAXH(), 0x08, 0x07);
            RegisterMmiSub(new PADDB(), 0x08, 0x08);
            RegisterMmiSub(new PSUBB(), 0x08, 0x09);
            RegisterMmiSub(new PCGTB(), 0x08, 0x0A);
            RegisterMmiSub(new PADDSW(), 0x08, 0x10);
            RegisterMmiSub(new PSUBSW(), 0x08, 0x11);
            RegisterMmiSub(new PEXTLW(), 0x08, 0x12);
            RegisterMmiSub(new PPACW(), 0x08, 0x13);
            RegisterMmiSub(new PADDSH(), 0x08, 0x14);
            RegisterMmiSub(new PSUBSH(), 0x08, 0x15);
            RegisterMmiSub(new PEXTLH(), 0x08, 0x16);
            RegisterMmiSub(new PPACH(), 0x08, 0x17);
            RegisterMmiSub(new PADDSB(), 0x08, 0x18);
            RegisterMmiSub(new PSUBSB(), 0x08, 0x19);
            RegisterMmiSub(new PEXTLB(), 0x08, 0x1A);
            RegisterMmiSub(new PPACB(), 0x08, 0x1B);
            RegisterMmiSub(new PEXT5(), 0x08, 0x1E);
            RegisterMmiSub(new PPAC5(), 0x08, 0x1F);

            // ---- MMI1 (funct 0x28) ----
            RegisterMmiSub(new PABSW(), 0x28, 0x01);
            RegisterMmiSub(new PCEQW(), 0x28, 0x02);
            RegisterMmiSub(new PMINW(), 0x28, 0x03);
            RegisterMmiSub(new PADSBH(), 0x28, 0x04);
            RegisterMmiSub(new PABSH(), 0x28, 0x05);
            RegisterMmiSub(new PCEQH(), 0x28, 0x06);
            RegisterMmiSub(new PMINH(), 0x28, 0x07);
            RegisterMmiSub(new PCEQB(), 0x28, 0x0A);
            RegisterMmiSub(new PADDUW(), 0x28, 0x10);
            RegisterMmiSub(new PSUBUW(), 0x28, 0x11);
            RegisterMmiSub(new PEXTUW(), 0x28, 0x12);
            RegisterMmiSub(new PADDUH(), 0x28, 0x14);
            RegisterMmiSub(new PSUBUH(), 0x28, 0x15);
            RegisterMmiSub(new PEXTUH(), 0x28, 0x16);
            RegisterMmiSub(new PADDUB(), 0x28, 0x18);
            RegisterMmiSub(new PSUBUB(), 0x28, 0x19);
            RegisterMmiSub(new PEXTUB(), 0x28, 0x1A);
            RegisterMmiSub(new QFSRV(), 0x28, 0x1B);

            // ---- MMI2 (funct 0x09) ----
            RegisterMmiSub(new PMADDW(), 0x09, 0x00);
            RegisterMmiSub(new PSLLVW(), 0x09, 0x02);
            RegisterMmiSub(new PSRLVW(), 0x09, 0x03);
            RegisterMmiSub(new PMSUBW(), 0x09, 0x04);
            RegisterMmiSub(new PMFHI(), 0x09, 0x08);
            RegisterMmiSub(new PMFLO(), 0x09, 0x09);
            RegisterMmiSub(new PINTH(), 0x09, 0x0A);
            RegisterMmiSub(new PMULTW(), 0x09, 0x0C);
            RegisterMmiSub(new PDIVW(), 0x09, 0x0D);
            RegisterMmiSub(new PCPYLD(), 0x09, 0x0E);
            RegisterMmiSub(new PMADDH(), 0x09, 0x10);
            RegisterMmiSub(new PHMADH(), 0x09, 0x11);
            RegisterMmiSub(new PAND(), 0x09, 0x12);
            RegisterMmiSub(new PXOR(), 0x09, 0x13);
            RegisterMmiSub(new PMSUBH(), 0x09, 0x14);
            RegisterMmiSub(new PHMSBH(), 0x09, 0x15);
            RegisterMmiSub(new PEXEH(), 0x09, 0x1A);
            RegisterMmiSub(new PREVH(), 0x09, 0x1B);
            RegisterMmiSub(new PMULTH(), 0x09, 0x1C);
            RegisterMmiSub(new PDIVBW(), 0x09, 0x1D);
            RegisterMmiSub(new PEXEW(), 0x09, 0x1E);
            RegisterMmiSub(new PROT3W(), 0x09, 0x1F);

            // ---- MMI3 (funct 0x29) ----
            RegisterMmiSub(new PMADDUW(), 0x29, 0x00);
            RegisterMmiSub(new PSRAVW(), 0x29, 0x03);
            RegisterMmiSub(new PMTHI(), 0x29, 0x08);
            RegisterMmiSub(new PMTLO(), 0x29, 0x09);
            RegisterMmiSub(new PINTEH(), 0x29, 0x0A);
            RegisterMmiSub(new PMULTUW(), 0x29, 0x0C);
            RegisterMmiSub(new PDIVUW(), 0x29, 0x0D);
            RegisterMmiSub(new PCPYUD(), 0x29, 0x0E);
            RegisterMmiSub(new POR(), 0x29, 0x12);
            RegisterMmiSub(new PNOR(), 0x29, 0x13);
            RegisterMmiSub(new PEXCH(), 0x29, 0x1A);
            RegisterMmiSub(new PCPYH(), 0x29, 0x1B);
            RegisterMmiSub(new PEXCW(), 0x29, 0x1E);
        }

        private static void InitializeRegisterMap()
        {
            string[] regNames =
            {
                "zero", "at", "v0", "v1", "a0", "a1", "a2", "a3",
                "t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7",
                "s0", "s1", "s2", "s3", "s4", "s5", "s6", "s7",
                "t8", "t9", "k0", "k1", "gp", "sp", "fp", "ra"
            };

            for (int i = 0; i < regNames.Length; i++)
            {
                RegisterMap[regNames[i]] = i;
                RegisterMap[$"${regNames[i]}"] = i;
                RegisterMap[$"r{i}"] = i;
                RegisterMap[$"$r{i}"] = i;
                RegisterMap[$"{i}"] = i;
                RegisterMap[$"${i}"] = i;
            }
            // $s8 is the ABI alias of $fp.
            RegisterMap["s8"] = 30;
            RegisterMap["$s8"] = 30;
        }

        private static void RegisterR(Instruction<Args_R> instr) =>
            RTypeInstructionsByName[instr.Name.ToLowerInvariant()] = instr;

        private static void RegisterI(Instruction<Args_I> instr) =>
            ITypeInstructionsByName[instr.Name.ToLowerInvariant()] = instr;

        private static void RegisterJ(Instruction<Args_J> instr) =>
            JTypeInstructionsByName[instr.Name.ToLowerInvariant()] = instr;

        private static void RegisterRegimm(Instruction<Args_I> instr, int rt) =>
            RegimmInstructionsByName[instr.Name.ToLowerInvariant()] = (instr, rt);

        private static void RegisterCop(Instruction<Args_R> instr, int rs) =>
            CopInstructionsByName[instr.Name.ToLowerInvariant()] = (instr, rs);

        private static void RegisterMmiSub(Instruction<Args_R> instr, int funct, int shamt) =>
            MmiSubInstructionsByName[instr.Name.ToLowerInvariant()] = (instr, funct, shamt);

        /// <summary>
        /// True when <paramref name="mnemonic"/> (case-insensitive) is a mnemonic or
        /// pseudo-instruction this assembler can encode.
        /// </summary>
        public static bool IsKnownMnemonic(string mnemonic)
        {
            string m = mnemonic.Trim().ToLowerInvariant();
            if (MnemonicAliases.TryGetValue(m, out var alias)) m = alias;
            return RTypeInstructionsByName.ContainsKey(m)
                || ITypeInstructionsByName.ContainsKey(m)
                || JTypeInstructionsByName.ContainsKey(m)
                || RegimmInstructionsByName.ContainsKey(m)
                || CopInstructionsByName.ContainsKey(m)
                || CopBranchesByName.ContainsKey(m)
                || MmiSubInstructionsByName.ContainsKey(m)
                || Vu0Macro.IsMnemonic(m)
                || Cop2MoveSuffix.IsMatch(m)
                || Cop0SpecialMoves.ContainsKey(m)
                || m.StartsWith("pmfhl.") || m.StartsWith("pmthl.")
                || m is "move" or "not" or "neg" or "negu" or "bnez" or "beqz" or "bnezl" or "beqzl"
                     or "b" or "bal" or "li" or "sync.p";
        }

        // qmfc2.i / cfc2.ni / …: COP2 moves with the interlock bit spelled out.
        private static readonly Regex Cop2MoveSuffix = new(@"^(qmfc2|qmtc2|cfc2|ctc2)\.(i|ni)$", RegexOptions.Compiled);

        // EE debug / performance-counter moves: mnemonic -> (mtc0?, COP0 reg, sel bits 5:0).
        // mfps/mtps and mfpc/mtpc take a counter number as their second operand.
        private static readonly Dictionary<string, (bool ToCop0, int Reg, int Sel)> Cop0SpecialMoves = new()
        {
            ["mfbpc"] = (false, 24, 0), ["mfiab"] = (false, 24, 2), ["mfiabm"] = (false, 24, 3),
            ["mfdab"] = (false, 24, 4), ["mfdabm"] = (false, 24, 5), ["mfdvb"] = (false, 24, 6), ["mfdvbm"] = (false, 24, 7),
            ["mtbpc"] = (true, 24, 0),  ["mtiab"] = (true, 24, 2),  ["mtiabm"] = (true, 24, 3),
            ["mtdab"] = (true, 24, 4),  ["mtdabm"] = (true, 24, 5),  ["mtdvb"] = (true, 24, 6),  ["mtdvbm"] = (true, 24, 7),
            ["mfps"] = (false, 25, 0), ["mtps"] = (true, 25, 0),
            ["mfpc"] = (false, 25, 1), ["mtpc"] = (true, 25, 1),
        };

        // -------------------------------------------------------------------------
        // Operand parsing
        // -------------------------------------------------------------------------

        /// <summary>Parse a GPR name into its register number.</summary>
        private static int ParseRegister(string reg)
        {
            string normalized = reg.Trim().ToLowerInvariant();
            if (RegisterMap.TryGetValue(normalized, out int regNum))
                return regNum;
            throw new ArgumentException($"Unknown register: {reg}");
        }

        /// <summary>Parse an FPU register (<c>$f12</c>, <c>f12</c>) or a bare register number.</summary>
        private static int ParseFpuRegister(string reg)
        {
            string s = reg.Trim();
            var m = FprRegex.Match(s);
            if (m.Success) return ParseRegisterNumber(m.Groups[1].Value, reg, "FPU register");
            m = FcrRegex.Match(s);
            if (m.Success) return ParseRegisterNumber(m.Groups[1].Value, reg, "FPU control register");
            m = NumberRegex.Match(s);
            if (m.Success) return ParseRegisterNumber(m.Groups[1].Value, reg, "FPU register");
            throw new ArgumentException($"Unknown FPU register: {reg} (expected $f0-$f31)");
        }

        /// <summary>Parse a VU0 floating-point register (<c>vf1</c>, <c>$vf1</c>) or a bare number.</summary>
        private static int ParseVfRegister(string reg)
        {
            string s = reg.Trim();
            var m = VfRegex.Match(s);
            if (m.Success) return ParseRegisterNumber(m.Groups[1].Value, reg, "VU0 register");
            m = NumberRegex.Match(s);
            if (m.Success) return ParseRegisterNumber(m.Groups[1].Value, reg, "VU0 register");
            throw new ArgumentException($"Unknown VU0 register: {reg} (expected vf0-vf31)");
        }

        /// <summary>Parse a VU0 integer/control register (<c>vi1</c>, <c>$vi1</c>) or a bare number.</summary>
        private static int ParseViRegister(string reg)
        {
            string s = reg.Trim();
            var m = ViRegex.Match(s);
            if (m.Success) return ParseRegisterNumber(m.Groups[1].Value, reg, "VU0 control register");
            m = NumberRegex.Match(s);
            if (m.Success) return ParseRegisterNumber(m.Groups[1].Value, reg, "VU0 control register");
            throw new ArgumentException($"Unknown VU0 control register: {reg} (expected vi0-vi31)");
        }

        /// <summary>Parse a COP0 register by number (<c>$12</c>) or by name (<c>Status</c>).</summary>
        private static int ParseCop0Register(string reg)
        {
            string s = reg.Trim();
            var m = NumberRegex.Match(s);
            if (m.Success) return ParseRegisterNumber(m.Groups[1].Value, reg, "COP0 register");
            if (Cop0RegNames.TryParse(s, out int index)) return index;
            throw new ArgumentException($"Unknown COP0 register: {reg} (expected $0-$31 or a name such as Status)");
        }

        private static int ParseRegisterNumber(string digits, string original, string kind)
        {
            int n = int.Parse(digits, CultureInfo.InvariantCulture);
            if (n > 31)
                throw new ArgumentException($"{kind} out of range: {original} (expected 0-31)");
            return n;
        }

        /// <summary>
        /// Parse an immediate value. Supports decimal (optionally negative), <c>0x</c>-hex and
        /// PS2-style <c>$</c>-hex. Hex values are limited to 8 digits (32 bits).
        /// </summary>
        /// <exception cref="ArgumentException">The text is not a number or is wider than 32 bits.</exception>
        public static long ParseImmediate(string imm)
        {
            string s = imm.Trim();
            if (s.Length == 0)
                throw new ArgumentException("Missing immediate value");

            bool negative = false;
            if (s[0] == '-') { negative = true; s = s[1..]; }
            else if (s[0] == '+') { s = s[1..]; }

            long value;
            if (s.StartsWith("$", StringComparison.Ordinal))
                value = ParseHexDigits(s[1..], imm);
            else if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                value = ParseHexDigits(s[2..], imm);
            else if (!long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value))
                throw new ArgumentException($"Invalid immediate value '{imm}' (expected decimal, 0x-hex or $-hex)");

            if (!negative && value > uint.MaxValue)
                throw new ArgumentException($"Immediate value '{imm}' does not fit in 32 bits");
            if (negative && value > 0x80000000L)
                throw new ArgumentException($"Immediate value '{imm}' does not fit in 32 bits");

            return negative ? -value : value;
        }

        private static long ParseHexDigits(string digits, string original)
        {
            if (digits.Length == 0 || !digits.All(Uri.IsHexDigit))
                throw new ArgumentException($"Invalid hex immediate '{original}'");
            if (digits.Length > 8)
                throw new ArgumentException(
                    $"Immediate '{original}' has {digits.Length} hex digits; values must fit in 32 bits (at most 8 hex digits)");
            return Convert.ToInt64(digits, 16);
        }

        /// <summary>
        /// Parse a 16-bit immediate / offset field. Accepts <c>-32768..65535</c> and the
        /// sign-extended 32-bit spelling of negative values (e.g. <c>$FFFFFFF0</c>).
        /// </summary>
        private static int ParseImm16(string imm, string fieldName)
        {
            long value = ParseImmediate(imm);
            if (value >= 0xFFFF8000L && value <= 0xFFFFFFFFL)
                value -= 0x100000000L;
            if (value < short.MinValue || value > ushort.MaxValue)
                throw new ArgumentException(
                    $"{fieldName} '{imm}' does not fit in 16 bits (expected -32768..65535 / $0000..$FFFF)");
            return (int)value;
        }

        private static int ParseSmallField(string text, string fieldName, int max)
        {
            long value = ParseImmediate(text);
            if (value < 0 || value > max)
                throw new ArgumentException($"{fieldName} '{text}' out of range (expected 0..{max})");
            return (int)value;
        }

        /// <summary>
        /// Parse a memory operand from the operand list starting at <paramref name="index"/>.
        /// Accepts <c>offset(base)</c>, <c>(base)</c>, <c>offset, base</c> and <c>offset, (base)</c>.
        /// </summary>
        private static (int Offset, int Base) ParseMemoryOperand(string[] operands, int index, string instrName)
        {
            if (index >= operands.Length)
                throw new ArgumentException($"Expected a memory operand offset(base) for {instrName}");

            string first = operands[index].Trim();
            var m = OffsetBaseRegex.Match(first);
            if (m.Success)
            {
                string offsetText = m.Groups[1].Value.Trim();
                int offset = offsetText.Length == 0 ? 0 : ParseImm16(offsetText, "Offset");
                int baseReg = ParseRegister(m.Groups[2].Value);
                if (index + 1 != operands.Length)
                    throw new ArgumentException($"Unexpected extra operand after '{first}' for {instrName}");
                return (offset, baseReg);
            }

            if (index + 1 < operands.Length)
            {
                int offset = ParseImm16(first, "Offset");
                string baseText = operands[index + 1].Trim();
                if (baseText.StartsWith("(") && baseText.EndsWith(")"))
                    baseText = baseText[1..^1];
                int baseReg = ParseRegister(baseText);
                if (index + 2 != operands.Length)
                    throw new ArgumentException($"Unexpected extra operand for {instrName}");
                return (offset, baseReg);
            }

            throw new ArgumentException(
                $"Expected a memory operand offset(base) for {instrName}, got '{first}'");
        }

        // -------------------------------------------------------------------------
        // Public entry points
        // -------------------------------------------------------------------------

        /// <summary>
        /// Assemble a single line of assembly into a 32-bit instruction.
        /// </summary>
        /// <param name="asmLine">Assembly line (e.g. <c>add $t0, $s0, $s1</c> or <c>lwc1 $f12, $0010($sp)</c>).</param>
        /// <returns>Encoded 32-bit instruction.</returns>
        /// <exception cref="ArgumentException">The mnemonic is unknown or an operand is invalid.</exception>
        public static uint Assemble(string asmLine)
        {
            if (asmLine is null)
                throw new ArgumentException("Empty instruction");

            // Remove trailing comments (# ; //)
            string line = StripComment(asmLine);

            string[] parts = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                throw new ArgumentException("Empty instruction");

            string mnemonic = parts[0].ToLowerInvariant();
            string[] operands = parts.Skip(1).ToArray();

            if (MnemonicAliases.TryGetValue(mnemonic, out var alias))
                mnemonic = alias;

            // Single-instruction pseudo rewrites. Multi-instruction pseudos (la, 32-bit li,
            // blt/bgt/ble/bge) are handled by the CDS parser so they can advance the address counter.
            if (TryExpandSingleInstructionPseudo(mnemonic, operands, out string realMnemonic, out string[] realOperands))
            {
                mnemonic = realMnemonic;
                operands = realOperands;
            }

            // pmfhl.lw / pmfhl.uw / pmfhl.slw / pmfhl.lh / pmfhl.sh / pmthl.lw
            if (mnemonic.StartsWith("pmfhl.", StringComparison.Ordinal) ||
                mnemonic.StartsWith("pmthl.", StringComparison.Ordinal))
            {
                return AssemblePmfhl(mnemonic, operands);
            }

            if (RTypeInstructionsByName.TryGetValue(mnemonic, out var rInstr))
                return AssembleR(rInstr, operands);

            if (RegimmInstructionsByName.TryGetValue(mnemonic, out var regimm))
                return AssembleRegimm(regimm.Instr, regimm.Rt, operands);

            if (ITypeInstructionsByName.TryGetValue(mnemonic, out var iInstr))
                return AssembleI(iInstr, operands);

            if (JTypeInstructionsByName.TryGetValue(mnemonic, out var jInstr))
                return AssembleJ(jInstr, operands);

            if (CopBranchesByName.TryGetValue(mnemonic, out var copBranch))
            {
                if (operands.Length != 1)
                    throw new ArgumentException($"Expected 1 operand (offset) for {mnemonic}");
                int offset = ParseImm16(operands[0], "Branch offset");
                return MipsEncoder.EncodeI(copBranch.Opcode, 0x08, copBranch.Rt, offset);
            }

            // qmfc2.i / ctc2.ni …: bit 0 is the interlock flag.
            var cop2Move = Cop2MoveSuffix.Match(mnemonic);
            if (cop2Move.Success)
            {
                var (moveInstr, moveRs) = CopInstructionsByName[cop2Move.Groups[1].Value];
                uint word = AssembleCop(moveInstr, moveRs, operands);
                return cop2Move.Groups[2].Value == "i" ? word | 1u : word;
            }

            if (Cop0SpecialMoves.TryGetValue(mnemonic, out var special))
                return AssembleCop0SpecialMove(mnemonic, special.ToCop0, special.Reg, special.Sel, operands);

            if (CopInstructionsByName.TryGetValue(mnemonic, out var cop))
                return AssembleCop(cop.Instr, cop.Rs, operands);

            if (MmiSubInstructionsByName.TryGetValue(mnemonic, out var mmi))
                return AssembleMmiSub(mmi.Instr, mmi.Funct, mmi.Shamt, operands);

            if (Vu0Macro.IsMnemonic(mnemonic))
                return Vu0Macro.Assemble(mnemonic, operands);

            throw new ArgumentException($"Unknown instruction: {mnemonic}");
        }

        /// <summary>mfbpc/mtiab/… (one GPR) and mfps/mtps/mfpc/mtpc (GPR, counter).</summary>
        private static uint AssembleCop0SpecialMove(string mnemonic, bool toCop0, int reg, int sel, string[] operands)
        {
            int rt = ParseRegister(operands.Length > 0 ? operands[0] : throw new ArgumentException($"Expected a register for {mnemonic}"));
            if (reg == 25)
            {
                RequireOperands(mnemonic, operands, 2);
                int counter = ParseSmallField(operands[1], "Counter", sel == 0 ? 0 : 1);
                sel = sel == 0 ? 0 : (counter << 1) | 1;
            }
            else
            {
                RequireOperands(mnemonic, operands, 1);
            }
            return MipsEncoder.EncodeR(0x10, toCop0 ? 0x04 : 0x00, rt, reg, 0, sel);
        }

        /// <summary>
        /// Assemble a single line without throwing. Returns <see langword="false"/> and an
        /// error message when the line cannot be encoded.
        /// </summary>
        public static bool TryAssemble(string asmLine, out uint encoded, out string? error)
        {
            try
            {
                encoded = Assemble(asmLine);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                encoded = 0;
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Assemble multiple lines of assembly. Blank lines and lines starting with
        /// <c>#</c>, <c>;</c> or <c>//</c> are skipped.
        /// </summary>
        public static uint[] AssembleBlock(string[] asmLines)
        {
            var result = new List<uint>();
            foreach (var line in asmLines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) ||
                    trimmed.StartsWith("#") || trimmed.StartsWith(";") || trimmed.StartsWith("//"))
                {
                    continue;
                }
                result.Add(Assemble(trimmed));
            }
            return result.ToArray();
        }

        private static string StripComment(string line)
        {
            int cut = line.Length;
            int hash = line.IndexOf('#');
            if (hash >= 0 && hash < cut) cut = hash;
            int semi = line.IndexOf(';');
            if (semi >= 0 && semi < cut) cut = semi;
            int slashes = line.IndexOf("//", StringComparison.Ordinal);
            if (slashes >= 0 && slashes < cut) cut = slashes;
            return line[..cut];
        }

        // -------------------------------------------------------------------------
        // Pseudo-instructions
        // -------------------------------------------------------------------------

        private static bool TryExpandSingleInstructionPseudo(
            string mnemonic, string[] operands, out string realMnemonic, out string[] realOperands)
        {
            switch (mnemonic)
            {
                // move rd, rs  ->  addu rd, rs, $zero
                case "move":
                    RequireOperands(mnemonic, operands, 2);
                    realMnemonic = "addu";
                    realOperands = new[] { operands[0], operands[1], "$zero" };
                    return true;

                // not rd, rs  ->  nor rd, rs, $zero
                case "not":
                    RequireOperands(mnemonic, operands, 2);
                    realMnemonic = "nor";
                    realOperands = new[] { operands[0], operands[1], "$zero" };
                    return true;

                // neg rd, rs  ->  sub rd, $zero, rs
                case "neg":
                    RequireOperands(mnemonic, operands, 2);
                    realMnemonic = "sub";
                    realOperands = new[] { operands[0], "$zero", operands[1] };
                    return true;

                // negu rd, rs  ->  subu rd, $zero, rs
                case "negu":
                    RequireOperands(mnemonic, operands, 2);
                    realMnemonic = "subu";
                    realOperands = new[] { operands[0], "$zero", operands[1] };
                    return true;

                // bnez rs, offset  ->  bne rs, $zero, offset
                case "bnez":
                    RequireOperands(mnemonic, operands, 2);
                    realMnemonic = "bne";
                    realOperands = new[] { operands[0], "$zero", operands[1] };
                    return true;

                // beqz rs, offset  ->  beq rs, $zero, offset
                case "beqz":
                    RequireOperands(mnemonic, operands, 2);
                    realMnemonic = "beq";
                    realOperands = new[] { operands[0], "$zero", operands[1] };
                    return true;

                // bnezl / beqzl rs, offset  ->  bnel / beql rs, $zero, offset
                case "bnezl":
                case "beqzl":
                    RequireOperands(mnemonic, operands, 2);
                    realMnemonic = mnemonic == "bnezl" ? "bnel" : "beql";
                    realOperands = new[] { operands[0], "$zero", operands[1] };
                    return true;

                // b offset  ->  beq $zero, $zero, offset
                case "b":
                    RequireOperands(mnemonic, operands, 1);
                    realMnemonic = "beq";
                    realOperands = new[] { "$zero", "$zero", operands[0] };
                    return true;

                // bal offset  ->  bgezal $zero, offset
                case "bal":
                    RequireOperands(mnemonic, operands, 1);
                    realMnemonic = "bgezal";
                    realOperands = new[] { "$zero", operands[0] };
                    return true;

                // li rt, imm16  ->  addiu rt, $zero, imm (signed) or ori rt, $zero, imm (0x8000-0xFFFF)
                // 32-bit immediates are expanded to lui+ori by the CDS parser.
                case "li":
                {
                    RequireOperands(mnemonic, operands, 2);
                    long value = ParseImmediate(operands[1]);
                    if (value >= short.MinValue && value <= short.MaxValue)
                    {
                        realMnemonic = "addiu";
                        realOperands = new[] { operands[0], "$zero", operands[1] };
                        return true;
                    }
                    if (value >= 0 && value <= ushort.MaxValue)
                    {
                        realMnemonic = "ori";
                        realOperands = new[] { operands[0], "$zero", operands[1] };
                        return true;
                    }
                    throw new ArgumentException(
                        $"li immediate {operands[1]} does not fit in 16 bits; use `setreg` (or `li` inside a CDS script, which expands to lui+ori)");
                }

                // sync.p  ->  sync with stype 0x10
                case "sync.p":
                    if (operands.Length != 0)
                        throw new ArgumentException("Expected 0 operands for sync.p");
                    realMnemonic = "sync";
                    realOperands = new[] { "0x10" };
                    return true;

                default:
                    realMnemonic = mnemonic;
                    realOperands = operands;
                    return false;
            }
        }

        private static void RequireOperands(string mnemonic, string[] operands, int count)
        {
            if (operands.Length != count)
                throw new ArgumentException($"Expected {count} operand{(count == 1 ? "" : "s")} for {mnemonic}, got {operands.Length}");
        }

        // -------------------------------------------------------------------------
        // Encoders
        // -------------------------------------------------------------------------

        private static uint AssembleR(Instruction<Args_R> instr, string[] operands)
        {
            string[] argNames = instr.ArgNames;
            string name = instr.Name;
            int funct = instr.Funct ?? 0;

            if (Matches(argNames, "rd", "rs", "rt"))
            {
                // EE multiply forms: `mult1 rd, rs, rt` or the classic `mult1 rs, rt`.
                if (operands.Length == 2 && OptionalRdMultiply.Contains(name))
                {
                    int rs2 = ParseRegister(operands[0]);
                    int rt2 = ParseRegister(operands[1]);
                    return MipsEncoder.EncodeR(instr.Opcode, rs2, rt2, 0, 0, funct);
                }
                RequireOperands(name, operands, 3);
                int rd = ParseRegister(operands[0]);
                int rs = ParseRegister(operands[1]);
                int rt = ParseRegister(operands[2]);
                return MipsEncoder.EncodeR(instr.Opcode, rs, rt, rd, 0, funct);
            }

            if (Matches(argNames, "rd", "rt", "sa"))
            {
                RequireOperands(name, operands, 3);
                int rd = ParseRegister(operands[0]);
                int rt = ParseRegister(operands[1]);
                int sa = ParseSmallField(operands[2], "Shift amount", 31);
                return MipsEncoder.EncodeR(instr.Opcode, 0, rt, rd, sa, funct);
            }

            if (Matches(argNames, "rd", "rt", "rs"))
            {
                RequireOperands(name, operands, 3);
                int rd = ParseRegister(operands[0]);
                int rt = ParseRegister(operands[1]);
                int rs = ParseRegister(operands[2]);
                return MipsEncoder.EncodeR(instr.Opcode, rs, rt, rd, 0, funct);
            }

            if (Matches(argNames, "rs", "rt"))
            {
                // `mult rd, rs, rt` (EE three-operand form writes LO to rd as well).
                if (operands.Length == 3 && OptionalRdMultiply.Contains(name))
                {
                    int rd3 = ParseRegister(operands[0]);
                    int rs3 = ParseRegister(operands[1]);
                    int rt3 = ParseRegister(operands[2]);
                    return MipsEncoder.EncodeR(instr.Opcode, rs3, rt3, rd3, 0, funct);
                }
                RequireOperands(name, operands, 2);
                int rs = ParseRegister(operands[0]);
                int rt = ParseRegister(operands[1]);
                return MipsEncoder.EncodeR(instr.Opcode, rs, rt, 0, 0, funct);
            }

            if (Matches(argNames, "rd", "rs"))
            {
                // jalr rs  ->  jalr $ra, rs
                if (operands.Length == 1 && name.Equals("jalr", StringComparison.OrdinalIgnoreCase))
                {
                    int rs1 = ParseRegister(operands[0]);
                    return MipsEncoder.EncodeR(instr.Opcode, rs1, 0, 31, 0, funct);
                }
                RequireOperands(name, operands, 2);
                int rd = ParseRegister(operands[0]);
                int rs = ParseRegister(operands[1]);
                return MipsEncoder.EncodeR(instr.Opcode, rs, 0, rd, 0, funct);
            }

            if (Matches(argNames, "rd", "rt"))
            {
                RequireOperands(name, operands, 2);
                int rd = ParseRegister(operands[0]);
                int rt = ParseRegister(operands[1]);
                return MipsEncoder.EncodeR(instr.Opcode, 0, rt, rd, 0, funct);
            }

            if (Matches(argNames, "rd"))
            {
                RequireOperands(name, operands, 1);
                int rd = ParseRegister(operands[0]);
                return MipsEncoder.EncodeR(instr.Opcode, 0, 0, rd, 0, funct);
            }

            if (Matches(argNames, "rs"))
            {
                RequireOperands(name, operands, 1);
                int rs = ParseRegister(operands[0]);
                return MipsEncoder.EncodeR(instr.Opcode, rs, 0, 0, 0, funct);
            }

            if (argNames.Length == 0)
            {
                RequireOperands(name, operands, 0);
                return MipsEncoder.EncodeR(instr.Opcode, 0, 0, 0, 0, funct);
            }

            if (Matches(argNames, "code"))
            {
                // SYSCALL / BREAK: optional 20-bit code in bits 25-6
                if (operands.Length > 1)
                    throw new ArgumentException($"Expected at most 1 operand for {name}");
                uint code = operands.Length > 0 ? (uint)ParseSmallField(operands[0], "Code", 0xFFFFF) : 0;
                return (uint)((code << 6) | (uint)funct);
            }

            if (Matches(argNames, "stype"))
            {
                if (operands.Length > 1)
                    throw new ArgumentException($"Expected at most 1 operand for {name}");
                int stype = operands.Length > 0 ? ParseSmallField(operands[0], "Sync type", 31) : 0;
                return MipsEncoder.EncodeR(instr.Opcode, 0, 0, 0, stype, funct);
            }

            throw new NotImplementedException($"Unsupported R-type operand layout for {name}");
        }

        private static uint AssembleRegimm(Instruction<Args_I> instr, int rt, string[] operands)
        {
            // rs, offset  (branches)  /  rs, imm  (trap-immediate, mtsab, mtsah)
            RequireOperands(instr.Name, operands, 2);
            int rs = ParseRegister(operands[0]);
            int imm = ParseImm16(operands[1], "Immediate");
            return MipsEncoder.EncodeI(instr.Opcode, rs, rt, imm);
        }

        private static uint AssembleI(Instruction<Args_I> instr, string[] operands)
        {
            string[] argNames = instr.ArgNames;
            string name = instr.Name;

            if (Matches(argNames, "rt", "rs", "imm"))
            {
                RequireOperands(name, operands, 3);
                int rt = ParseRegister(operands[0]);
                int rs = ParseRegister(operands[1]);
                int imm = ParseImm16(operands[2], "Immediate");
                return MipsEncoder.EncodeI(instr.Opcode, rs, rt, imm);
            }

            if (argNames.Length == 3 && argNames[1] == "offset")
            {
                // rt, offset(rs)  — GPR loads/stores
                // ft, offset(base) — COP1 loads/stores ($f registers)
                // vft, offset(base) — COP2 lqc2/sqc2 (vf registers)
                // op, offset(base) — CACHE
                if (operands.Length < 2)
                    throw new ArgumentException($"Expected 2 operands for {name}: {argNames[0]}, offset(base)");

                int rt = argNames[0] switch
                {
                    "ft"  => ParseFpuRegister(operands[0]),
                    "vft" => ParseVfRegister(operands[0]),
                    "op"  => ParseSmallField(operands[0], "Cache operation", 31),
                    _     => ParseRegister(operands[0])
                };
                var (offset, baseReg) = ParseMemoryOperand(operands, 1, name);
                return MipsEncoder.EncodeI(instr.Opcode, baseReg, rt, offset);
            }

            if (Matches(argNames, "rs", "rt", "offset"))
            {
                RequireOperands(name, operands, 3);
                int rs = ParseRegister(operands[0]);
                int rt = ParseRegister(operands[1]);
                int offset = ParseImm16(operands[2], "Branch offset");
                return MipsEncoder.EncodeI(instr.Opcode, rs, rt, offset);
            }

            if (Matches(argNames, "rs", "offset"))
            {
                RequireOperands(name, operands, 2);
                int rs = ParseRegister(operands[0]);
                int offset = ParseImm16(operands[1], "Branch offset");
                // REGIMM instructions are dispatched through AssembleRegimm; the remaining
                // rs/offset instructions (blez, bgtz, blezl, bgtzl) use rt = 0.
                int rt = instr.Opcode == 0x01 ? instr.Funct ?? 0 : 0;
                return MipsEncoder.EncodeI(instr.Opcode, rs, rt, offset);
            }

            if (Matches(argNames, "rt", "imm"))
            {
                RequireOperands(name, operands, 2);
                int rt = ParseRegister(operands[0]);
                int imm = ParseImm16(operands[1], "Immediate");
                return MipsEncoder.EncodeI(instr.Opcode, 0, rt, imm);
            }

            throw new NotImplementedException($"Unsupported I-type operand layout for {name}");
        }

        private static uint AssembleJ(Instruction<Args_J> instr, string[] operands)
        {
            RequireOperands(instr.Name, operands, 1);

            long value = ParseImmediate(operands[0]);
            if (value < 0 || value > uint.MaxValue)
                throw new ArgumentException($"Jump target {operands[0]} must be a 32-bit address");
            uint target = (uint)value;

            if ((target & 0x3) != 0)
                throw new ArgumentException($"Jump target address 0x{target:X} must be word-aligned (divisible by 4)");

            // The 26-bit field holds bits [27:2] of the byte address.
            target = (target >> 2) & 0x03FFFFFF;
            return MipsEncoder.EncodeJ(instr.Opcode, target);
        }

        private static uint AssembleCop(Instruction<Args_R> instr, int rs, string[] operands)
        {
            string[] argNames = instr.ArgNames;
            string name = instr.Name;
            int funct = instr.Funct ?? 0;

            // Coprocessor register moves: GPR <-> coprocessor register (funct = 0)
            if (argNames.Length == 2 && (argNames[0] == "rt" || argNames[1] == "rt") &&
                !(argNames[0].StartsWith("f") && argNames[1].StartsWith("f")))
            {
                RequireOperands(name, operands, 2);
                int rtIndex = argNames[0] == "rt" ? 0 : 1;
                int copIndex = 1 - rtIndex;
                int rt = ParseRegister(operands[rtIndex]);
                int rd = ParseCoprocessorOperand(argNames[copIndex], operands[copIndex], instr.Opcode);
                return MipsEncoder.EncodeR(instr.Opcode, rs, rt, rd, 0, 0);
            }

            // FPU arithmetic: fd, fs, ft  → rt = ft, rd = fs, shamt = fd
            if (Matches(argNames, "fd", "fs", "ft"))
            {
                RequireOperands(name, operands, 3);
                int fd = ParseFpuRegister(operands[0]);
                int fs = ParseFpuRegister(operands[1]);
                int ft = ParseFpuRegister(operands[2]);
                return MipsEncoder.EncodeR(instr.Opcode, rs, ft, fs, fd, funct);
            }

            // FPU unary / convert: fd, fs
            if (Matches(argNames, "fd", "fs"))
            {
                RequireOperands(name, operands, 2);
                int fd = ParseFpuRegister(operands[0]);
                int fs = ParseFpuRegister(operands[1]);
                return MipsEncoder.EncodeR(instr.Opcode, rs, 0, fs, fd, funct);
            }

            // FPU compare / accumulate: fs, ft
            if (Matches(argNames, "fs", "ft"))
            {
                RequireOperands(name, operands, 2);
                int fs = ParseFpuRegister(operands[0]);
                int ft = ParseFpuRegister(operands[1]);
                return MipsEncoder.EncodeR(instr.Opcode, rs, ft, fs, 0, funct);
            }

            // COP0 CO operations without operands (eret, ei, di, tlbr, …)
            if (argNames.Length == 0)
            {
                RequireOperands(name, operands, 0);
                return MipsEncoder.EncodeR(instr.Opcode, rs, 0, 0, 0, funct);
            }

            throw new NotImplementedException($"Unsupported coprocessor operand layout for {name}");
        }

        private static int ParseCoprocessorOperand(string argName, string operand, int opcode) => argName switch
        {
            "fs"   => ParseFpuRegister(operand),
            "vfs"  => ParseVfRegister(operand),
            "vfd"  => ParseVfRegister(operand),
            "ctrl" => VU0Helpers.TryParseControlRegister(operand, out int ctrl) ? ctrl : ParseViRegister(operand),
            _      => opcode == 0x10 ? ParseCop0Register(operand) : ParseFpuRegister(operand)
        };

        private static uint AssembleMmiSub(Instruction<Args_R> instr, int funct, int shamt, string[] operands)
        {
            string[] argNames = instr.ArgNames;
            string name = instr.Name;
            const int opcode = 0x1C;

            if (Matches(argNames, "rd", "rs", "rt"))
            {
                RequireOperands(name, operands, 3);
                int rd = ParseRegister(operands[0]);
                int rs = ParseRegister(operands[1]);
                int rt = ParseRegister(operands[2]);
                return MipsEncoder.EncodeR(opcode, rs, rt, rd, shamt, funct);
            }

            if (Matches(argNames, "rd", "rt", "rs"))
            {
                RequireOperands(name, operands, 3);
                int rd = ParseRegister(operands[0]);
                int rt = ParseRegister(operands[1]);
                int rs = ParseRegister(operands[2]);
                return MipsEncoder.EncodeR(opcode, rs, rt, rd, shamt, funct);
            }

            if (Matches(argNames, "rd", "rs"))
            {
                RequireOperands(name, operands, 2);
                int rd = ParseRegister(operands[0]);
                int rs = ParseRegister(operands[1]);
                return MipsEncoder.EncodeR(opcode, rs, 0, rd, shamt, funct);
            }

            if (Matches(argNames, "rd", "rt"))
            {
                RequireOperands(name, operands, 2);
                int rd = ParseRegister(operands[0]);
                int rt = ParseRegister(operands[1]);
                return MipsEncoder.EncodeR(opcode, 0, rt, rd, shamt, funct);
            }

            if (Matches(argNames, "rs", "rt"))
            {
                RequireOperands(name, operands, 2);
                int rs = ParseRegister(operands[0]);
                int rt = ParseRegister(operands[1]);
                return MipsEncoder.EncodeR(opcode, rs, rt, 0, shamt, funct);
            }

            if (Matches(argNames, "rd"))
            {
                RequireOperands(name, operands, 1);
                int rd = ParseRegister(operands[0]);
                return MipsEncoder.EncodeR(opcode, 0, 0, rd, shamt, funct);
            }

            if (Matches(argNames, "rs"))
            {
                RequireOperands(name, operands, 1);
                int rs = ParseRegister(operands[0]);
                return MipsEncoder.EncodeR(opcode, rs, 0, 0, shamt, funct);
            }

            throw new NotImplementedException($"Unsupported MMI operand layout for {name}");
        }

        private static uint AssemblePmfhl(string mnemonic, string[] operands)
        {
            int dot = mnemonic.IndexOf('.');
            string baseName = mnemonic[..dot];
            string format = mnemonic[(dot + 1)..];

            int shamt = format switch
            {
                "lw"  => 0x00,
                "uw"  => 0x01,
                "slw" => 0x02,
                "lh"  => 0x03,
                "sh"  => 0x04,
                _ => throw new ArgumentException($"Unknown {baseName} format '.{format}' (expected .lw, .uw, .slw, .lh or .sh)")
            };

            RequireOperands(mnemonic, operands, 1);
            int reg = ParseRegister(operands[0]);
            if (baseName == "pmfhl")
                return MipsEncoder.EncodeR(0x1C, 0, 0, reg, shamt, 0x30);     // pmfhl rd
            if (baseName == "pmthl" && shamt == 0)
                return MipsEncoder.EncodeR(0x1C, reg, 0, 0, shamt, 0x31);     // pmthl rs
            throw new ArgumentException($"Unknown instruction: {mnemonic}");
        }

        private static bool Matches(string[] argNames, params string[] expected)
        {
            if (argNames.Length != expected.Length) return false;
            for (int i = 0; i < expected.Length; i++)
                if (!string.Equals(argNames[i], expected[i], StringComparison.Ordinal)) return false;
            return true;
        }
    }
}
