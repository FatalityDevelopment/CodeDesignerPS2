using System;
using System.Collections.Generic;

namespace CodeDesigner.Mips64LE.Recompiler
{
    /// <summary>
    /// Translates individual MIPS R5900 (PS2 EE) instruction words into their
    /// C++ equivalent expressions for static recompiler output.
    ///
    /// Generated C++ assumes:
    /// <list type="bullet">
    ///   <item><c>Ps2Context* ctx</c>  — CPU register file</item>
    ///   <item><c>Ps2Runtime* runtime</c> — memory and dispatch helpers</item>
    /// </list>
    ///
    /// GPRs are accessed as <c>ctx->r[N]</c>.  Writes to $zero (r[0]) are suppressed.
    /// Branch/jump targets inside the current function use <c>goto</c> labels;
    /// cross-function calls use the <c>RECOMP_CALL</c> / <c>RECOMP_JUMP</c> macros.
    /// </summary>
    public static class InstructionTranslator
    {
        // ── ABI register names ─────────────────────────────────────────────────

        private static readonly string[] AbiNames =
        {
            "zero","at","v0","v1","a0","a1","a2","a3",
            "t0","t1","t2","t3","t4","t5","t6","t7",
            "s0","s1","s2","s3","s4","s5","s6","s7",
            "t8","t9","k0","k1","gp","sp","fp","ra"
        };

        /// <summary>Returns the ABI name for a GPR index (0–31).</summary>
        public static string RegName(int r) =>
            (uint)r < (uint)AbiNames.Length ? AbiNames[r] : $"r{r}";

        // ── C++ expression helpers ─────────────────────────────────────────────

        /// <summary>C++ expression to read GPR <paramref name="r"/>.</summary>
        private static string R(int r) => r == 0 ? "0ULL" : $"ctx->r[{r}]";

        /// <summary>C++ statement assigning <paramref name="expr"/> to GPR <paramref name="r"/>.</summary>
        private static string W(int r, string expr) =>
            r == 0 ? $"/* $zero ignored: {expr} */" : $"ctx->r[{r}] = {expr};";

        /// <summary>Sign-extend a 16-bit immediate to a C++ int64 literal.</summary>
        private static string Imm16(int imm)
        {
            int se = (short)(imm & 0xFFFF);
            return se >= 0
                ? $"INT64_C(0x{se:X})"
                : $"INT64_C(-0x{(uint)(-se):X})";
        }

        /// <summary>Unsigned 16-bit immediate as a C++ literal.</summary>
        private static string UIm16(int imm) => $"UINT64_C(0x{imm & 0xFFFF:X})";

        // ── Instruction field extraction ───────────────────────────────────────

        private static int  Opcode(uint w) => (int)((w >> 26) & 0x3F);
        private static int  Rs(uint w)     => (int)((w >> 21) & 0x1F);
        private static int  Rt(uint w)     => (int)((w >> 16) & 0x1F);
        private static int  Rd(uint w)     => (int)((w >> 11) & 0x1F);
        private static int  Shamt(uint w)  => (int)((w >> 6)  & 0x1F);
        private static int  Funct(uint w)  => (int)(w & 0x3F);
        private static int  Imm(uint w)    => (int)(w & 0xFFFF);
        private static uint Target(uint w) => w & 0x03FFFFFF;

        // ── Address computation ────────────────────────────────────────────────

        private static uint BranchTarget(uint pc, int imm)
        {
            int se = (short)(imm & 0xFFFF);
            return (uint)((long)pc + 4 + ((long)se << 2));
        }

        private static uint JumpTarget(uint pc, uint target) =>
            (pc & 0xF0000000) | (target << 2);

        private static string LabelName(uint addr) => $"label_{addr:X8}";

        // ── Public entry point ─────────────────────────────────────────────────

        /// <summary>
        /// Translate a single instruction word at <paramref name="pc"/> into one or
        /// more C++ statement strings (without trailing newlines).
        ///
        /// <paramref name="localLabels"/> is the set of all addresses within the current
        /// function that are branch/jump targets — these use <c>goto</c> labels.
        ///
        /// <paramref name="delaySlotWord"/> is the instruction word at PC+4.  When a
        /// branch or jump is detected the delay slot is emitted inline before the
        /// control-flow statement (MIPS branch delay slot semantics).
        /// </summary>
        public static List<string> Translate(
            uint word,
            uint pc,
            uint delaySlotWord,
            HashSet<uint> localLabels,
            RecompilerConfig config)
        {
            var lines = new List<string>();

            if (word == 0x00000000)
            {
                lines.Add("/* nop */");
                return lines;
            }

            int opcode = Opcode(word);

            switch (opcode)
            {
                case 0x00: TranslateSpecial(word, pc, delaySlotWord, localLabels, config, lines); break;
                case 0x01: TranslateRegimm(word, pc, delaySlotWord, localLabels, config, lines); break;

                case 0x02: // J
                {
                    uint dest = JumpTarget(pc, Target(word));
                    EmitDelaySlot(delaySlotWord, pc + 4, localLabels, config, lines);
                    if (localLabels.Contains(dest))
                        lines.Add($"goto {LabelName(dest)};");
                    else
                        lines.Add($"RECOMP_JUMP(0x{dest:X8}U, ctx, runtime);");
                    break;
                }
                case 0x03: // JAL
                {
                    uint dest = JumpTarget(pc, Target(word));
                    lines.Add($"ctx->r[31] = UINT64_C(0x{pc + 8:X8}); /* $ra */");
                    EmitDelaySlot(delaySlotWord, pc + 4, localLabels, config, lines);
                    lines.Add($"RECOMP_CALL(0x{dest:X8}U, ctx, runtime);");
                    break;
                }

                case 0x10: // COP0
                    if (config.PatchCop0)
                        lines.Add($"/* COP0 patched to nop: 0x{word:X8} */");
                    else
                        TranslateCop0(word, lines);
                    break;

                case 0x11: // COP1
                    TranslateCop1(word, pc, delaySlotWord, localLabels, config, lines);
                    break;

                case 0x12: // COP2 / VU0 macro mode
                    lines.Add($"/* COP2/VU0 macro: 0x{word:X8} */ runtime->vu0Macro(0x{word:X8}U, ctx);");
                    break;

                case 0x1C: // MMI
                    TranslateMmi(word, lines);
                    break;

                default:
                    TranslateIType(word, pc, delaySlotWord, localLabels, config, lines);
                    break;
            }

            return lines;
        }

        // ── SPECIAL (opcode 0x00) ──────────────────────────────────────────────

        private static void TranslateSpecial(
            uint word, uint pc,
            uint dsWord, HashSet<uint> local, RecompilerConfig cfg,
            List<string> lines)
        {
            int funct = Funct(word);
            int rs    = Rs(word);
            int rt    = Rt(word);
            int rd    = Rd(word);
            int shamt = Shamt(word);

            switch (funct)
            {
                // Arithmetic
                case 0x20: lines.Add(W(rd, $"SE32((int32_t)({R(rs)} + {R(rt)}))")); break;  // ADD
                case 0x21: lines.Add(W(rd, $"SE32((uint32_t)({R(rs)} + {R(rt)}))")); break; // ADDU
                case 0x22: lines.Add(W(rd, $"SE32((int32_t)({R(rs)} - {R(rt)}))")); break;  // SUB
                case 0x23: lines.Add(W(rd, $"SE32((uint32_t)({R(rs)} - {R(rt)}))")); break; // SUBU
                case 0x2C: lines.Add(W(rd, $"{R(rs)} + {R(rt)}")); break;                   // DADD
                case 0x2D: lines.Add(W(rd, $"{R(rs)} + {R(rt)}")); break;                   // DADDU
                case 0x2E: lines.Add(W(rd, $"{R(rs)} - {R(rt)}")); break;                   // DSUB
                case 0x2F: lines.Add(W(rd, $"{R(rs)} - {R(rt)}")); break;                   // DSUBU

                // Logical
                case 0x24: lines.Add(W(rd, $"{R(rs)} & {R(rt)}")); break;      // AND
                case 0x25: lines.Add(W(rd, $"{R(rs)} | {R(rt)}")); break;      // OR
                case 0x26: lines.Add(W(rd, $"{R(rs)} ^ {R(rt)}")); break;      // XOR
                case 0x27: lines.Add(W(rd, $"~({R(rs)} | {R(rt)})")); break;   // NOR

                // Shift constant
                case 0x00: lines.Add(W(rd, $"SE32((uint32_t){R(rt)} << {shamt})")); break;   // SLL
                case 0x02: lines.Add(W(rd, $"SE32((uint32_t){R(rt)} >> {shamt})")); break;   // SRL
                case 0x03: lines.Add(W(rd, $"SE32((int32_t){R(rt)} >> {shamt})")); break;    // SRA
                case 0x38: lines.Add(W(rd, $"{R(rt)} << {shamt}")); break;                   // DSLL
                case 0x3A: lines.Add(W(rd, $"(uint64_t){R(rt)} >> {shamt}")); break;         // DSRL
                case 0x3B: lines.Add(W(rd, $"(int64_t){R(rt)} >> {shamt}")); break;          // DSRA
                case 0x3C: lines.Add(W(rd, $"{R(rt)} << ({shamt} + 32)")); break;            // DSLL32
                case 0x3E: lines.Add(W(rd, $"(uint64_t){R(rt)} >> ({shamt} + 32)")); break;  // DSRL32
                case 0x3F: lines.Add(W(rd, $"(int64_t){R(rt)} >> ({shamt} + 32)")); break;   // DSRA32

                // Shift variable
                case 0x04: lines.Add(W(rd, $"SE32((uint32_t){R(rt)} << ({R(rs)} & 0x1F))")); break;  // SLLV
                case 0x06: lines.Add(W(rd, $"SE32((uint32_t){R(rt)} >> ({R(rs)} & 0x1F))")); break;  // SRLV
                case 0x07: lines.Add(W(rd, $"SE32((int32_t){R(rt)} >> ({R(rs)} & 0x1F))")); break;   // SRAV
                case 0x14: lines.Add(W(rd, $"{R(rt)} << ({R(rs)} & 0x3F)")); break;                  // DSLLV
                case 0x16: lines.Add(W(rd, $"(uint64_t){R(rt)} >> ({R(rs)} & 0x3F)")); break;        // DSRLV
                case 0x17: lines.Add(W(rd, $"(int64_t){R(rt)} >> ({R(rs)} & 0x3F)")); break;         // DSRAV

                // Compare
                case 0x2A: lines.Add(W(rd, $"((int64_t){R(rs)} < (int64_t){R(rt)}) ? 1ULL : 0ULL")); break; // SLT
                case 0x2B: lines.Add(W(rd, $"({R(rs)} < {R(rt)}) ? 1ULL : 0ULL")); break;                   // SLTU

                // Multiply / Divide (PS2 EE: 32-bit result sign-extended to 64 bits)
                case 0x18: // MULT
                    lines.Add($"{{ int64_t _r = (int64_t)(int32_t){R(rs)} * (int64_t)(int32_t){R(rt)}; ctx->lo = SE32((int32_t)_r); ctx->hi = SE32((int32_t)(_r >> 32)); {W(rd, "ctx->lo")} }}");
                    break;
                case 0x19: // MULTU
                    lines.Add($"{{ uint64_t _r = (uint64_t)(uint32_t){R(rs)} * (uint64_t)(uint32_t){R(rt)}; ctx->lo = SE32((int32_t)_r); ctx->hi = SE32((int32_t)(_r >> 32)); {W(rd, "ctx->lo")} }}");
                    break;
                case 0x1C: // DMULT
                    lines.Add($"{{ __int128 _r = (__int128)(int64_t){R(rs)} * (__int128)(int64_t){R(rt)}; ctx->lo = (uint64_t)_r; ctx->hi = (uint64_t)(_r >> 64); }}");
                    break;
                case 0x1D: // DMULTU
                    lines.Add($"{{ unsigned __int128 _r = (unsigned __int128){R(rs)} * (unsigned __int128){R(rt)}; ctx->lo = (uint64_t)_r; ctx->hi = (uint64_t)(_r >> 64); }}");
                    break;
                case 0x1A: // DIV
                    lines.Add($"if ({R(rt)} != 0) {{ ctx->lo = SE32((int32_t)((int32_t){R(rs)} / (int32_t){R(rt)})); ctx->hi = SE32((int32_t)((int32_t){R(rs)} % (int32_t){R(rt)})); }}");
                    break;
                case 0x1B: // DIVU
                    lines.Add($"if ({R(rt)} != 0) {{ ctx->lo = SE32((uint32_t){R(rs)} / (uint32_t){R(rt)}); ctx->hi = SE32((uint32_t){R(rs)} % (uint32_t){R(rt)}); }}");
                    break;
                case 0x1E: // DDIV
                    lines.Add($"if ({R(rt)} != 0) {{ ctx->lo = (uint64_t)((int64_t){R(rs)} / (int64_t){R(rt)}); ctx->hi = (uint64_t)((int64_t){R(rs)} % (int64_t){R(rt)}); }}");
                    break;
                case 0x1F: // DDIVU
                    lines.Add($"if ({R(rt)} != 0) {{ ctx->lo = {R(rs)} / {R(rt)}; ctx->hi = {R(rs)} % {R(rt)}; }}");
                    break;

                // HI / LO moves
                case 0x10: lines.Add(W(rd, "ctx->hi")); break;           // MFHI
                case 0x11: lines.Add($"ctx->hi = {R(rs)};"); break;      // MTHI
                case 0x12: lines.Add(W(rd, "ctx->lo")); break;           // MFLO
                case 0x13: lines.Add($"ctx->lo = {R(rs)};"); break;      // MTLO

                // PS2-specific pipeline-1 HI/LO (MMI group, funct 0x10-0x13 re-used)
                // Note: actual MFHI1/MFLO1 are dispatched via MMI (opcode 0x1C, funct 0x10/0x12)

                // Conditional moves
                case 0x0A: lines.Add($"if ({R(rt)} == 0) {{ {W(rd, R(rs))} }}"); break; // MOVZ
                case 0x0B: lines.Add($"if ({R(rt)} != 0) {{ {W(rd, R(rs))} }}"); break; // MOVN

                // SYSCALL / BREAK
                case 0x0C:
                    lines.Add($"runtime->handleSyscall((0x{word:X8}U >> 6) & 0xFFFFFU, ctx); /* SYSCALL */");
                    break;
                case 0x0D:
                    lines.Add($"runtime->handleBreak((0x{word:X8}U >> 6) & 0xFFFFFU, ctx); /* BREAK */");
                    break;

                // SYNC
                case 0x0F: lines.Add("/* sync */"); break;

                // JR / JALR
                case 0x08: // JR
                    EmitDelaySlot(dsWord, pc + 4, local, cfg, lines);
                    lines.Add($"RECOMP_JUMP_REG({R(rs)}, ctx, runtime);");
                    break;
                case 0x09: // JALR
                    lines.Add($"{W(rd, $"UINT64_C(0x{pc + 8:X8})")} /* $ra */");
                    EmitDelaySlot(dsWord, pc + 4, local, cfg, lines);
                    lines.Add($"RECOMP_JUMP_REG({R(rs)}, ctx, runtime);");
                    break;

                // EE SA register (MFSA=0x28, MTSA=0x29)
                case 0x28: lines.Add($"ctx->sa = (uint32_t)({R(rd)} & 0xF); /* MFSA */"); break;
                case 0x29: lines.Add($"ctx->sa = (uint32_t)({R(rs)} & 0xF); /* MTSA */"); break;

                // Traps
                case 0x30: lines.Add($"if ((int64_t){R(rs)} < (int64_t){R(rt)}) runtime->handleTrap(ctx); /* TLT */"); break;
                case 0x31: lines.Add($"if ({R(rs)} < {R(rt)}) runtime->handleTrap(ctx); /* TLTU */"); break;
                case 0x32: lines.Add($"if ({R(rs)} == {R(rt)}) runtime->handleTrap(ctx); /* TEQ */"); break;
                case 0x34: lines.Add($"if ({R(rs)} != {R(rt)}) runtime->handleTrap(ctx); /* TNE */"); break;
                case 0x36: lines.Add($"if ((int64_t){R(rs)} >= (int64_t){R(rt)}) runtime->handleTrap(ctx); /* TGE */"); break;
                case 0x37: lines.Add($"if ({R(rs)} >= {R(rt)}) runtime->handleTrap(ctx); /* TGEU */"); break;

                default:
                    lines.Add($"/* SPECIAL funct=0x{funct:X2}: 0x{word:X8} */ runtime->unhandled(0x{word:X8}U, ctx);");
                    break;
            }
        }

        // ── REGIMM (opcode 0x01) ───────────────────────────────────────────────

        private static void TranslateRegimm(
            uint word, uint pc,
            uint dsWord, HashSet<uint> local, RecompilerConfig cfg,
            List<string> lines)
        {
            int rt  = Rt(word);
            int rs  = Rs(word);
            int imm = Imm(word);
            uint tgt = BranchTarget(pc, imm);

            switch (rt)
            {
                case 0x00: case 0x02: // BLTZ / BLTZL
                    EmitDelaySlot(dsWord, pc + 4, local, cfg, lines);
                    EmitBranch($"(int64_t){R(rs)} < 0", tgt, local, lines);
                    return;
                case 0x01: case 0x03: // BGEZ / BGEZL
                    EmitDelaySlot(dsWord, pc + 4, local, cfg, lines);
                    EmitBranch($"(int64_t){R(rs)} >= 0", tgt, local, lines);
                    return;
                case 0x10: case 0x12: // BLTZAL / BLTZALL
                    lines.Add($"ctx->r[31] = UINT64_C(0x{pc + 8:X8});");
                    EmitDelaySlot(dsWord, pc + 4, local, cfg, lines);
                    EmitBranch($"(int64_t){R(rs)} < 0", tgt, local, lines);
                    return;
                case 0x11: case 0x13: // BGEZAL / BGEZALL
                    lines.Add($"ctx->r[31] = UINT64_C(0x{pc + 8:X8});");
                    EmitDelaySlot(dsWord, pc + 4, local, cfg, lines);
                    EmitBranch($"(int64_t){R(rs)} >= 0", tgt, local, lines);
                    return;
                // Trap-on-immediate
                case 0x08: lines.Add($"if ((int64_t){R(rs)} < (int64_t){Imm16(imm)}) runtime->handleTrap(ctx); /* TLTI */"); return;
                case 0x09: lines.Add($"if ({R(rs)} < (uint64_t){UIm16(imm)}) runtime->handleTrap(ctx); /* TLTIU */"); return;
                case 0x0A: lines.Add($"if ({R(rs)} == {Imm16(imm)}) runtime->handleTrap(ctx); /* TEQI */"); return;
                case 0x0C: lines.Add($"if ({R(rs)} != {Imm16(imm)}) runtime->handleTrap(ctx); /* TNEI */"); return;
                case 0x06: lines.Add($"if ((int64_t){R(rs)} >= (int64_t){Imm16(imm)}) runtime->handleTrap(ctx); /* TGEI */"); return;
                case 0x07: lines.Add($"if ({R(rs)} >= (uint64_t){UIm16(imm)}) runtime->handleTrap(ctx); /* TGEIU */"); return;
                default:
                    lines.Add($"/* REGIMM rt=0x{rt:X2}: 0x{word:X8} */ runtime->unhandled(0x{word:X8}U, ctx);");
                    return;
            }
        }

        // ── I-type ────────────────────────────────────────────────────────────

        private static void TranslateIType(
            uint word, uint pc,
            uint dsWord, HashSet<uint> local, RecompilerConfig cfg,
            List<string> lines)
        {
            int opcode = Opcode(word);
            int rs     = Rs(word);
            int rt     = Rt(word);
            int imm    = Imm(word);

            switch (opcode)
            {
                // Arithmetic immediate
                case 0x08: lines.Add(W(rt, $"SE32((int32_t)({R(rs)} + {Imm16(imm)}))")); break;   // ADDI
                case 0x09: lines.Add(W(rt, $"SE32((int32_t)({R(rs)} + {Imm16(imm)}))")); break;   // ADDIU
                case 0x18: lines.Add(W(rt, $"{R(rs)} + {Imm16(imm)}")); break;                     // DADDI
                case 0x19: lines.Add(W(rt, $"{R(rs)} + {Imm16(imm)}")); break;                     // DADDIU

                // Logical immediate
                case 0x0C: lines.Add(W(rt, $"{R(rs)} & {UIm16(imm)}")); break;  // ANDI
                case 0x0D: lines.Add(W(rt, $"{R(rs)} | {UIm16(imm)}")); break;  // ORI
                case 0x0E: lines.Add(W(rt, $"{R(rs)} ^ {UIm16(imm)}")); break;  // XORI

                // Compare immediate
                case 0x0A: lines.Add(W(rt, $"((int64_t){R(rs)} < (int64_t){Imm16(imm)}) ? 1ULL : 0ULL")); break; // SLTI
                case 0x0B: lines.Add(W(rt, $"({R(rs)} < {UIm16(imm)}) ? 1ULL : 0ULL")); break;                   // SLTIU

                // LUI
                case 0x0F: lines.Add(W(rt, $"SE32((int32_t)((uint32_t)0x{(imm & 0xFFFF):X4}U << 16))")); break;

                // Loads
                case 0x20: lines.Add(W(rt, $"(int64_t)(int8_t)runtime->read8((uint32_t)({R(rs)} + {Imm16(imm)}))")); break;   // LB
                case 0x21: lines.Add(W(rt, $"(int64_t)(int16_t)runtime->read16((uint32_t)({R(rs)} + {Imm16(imm)}))")); break; // LH
                case 0x23: lines.Add(W(rt, $"SE32((int32_t)runtime->read32((uint32_t)({R(rs)} + {Imm16(imm)})))")); break;    // LW
                case 0x24: lines.Add(W(rt, $"(uint64_t)runtime->read8((uint32_t)({R(rs)} + {Imm16(imm)}))")); break;          // LBU
                case 0x25: lines.Add(W(rt, $"(uint64_t)runtime->read16((uint32_t)({R(rs)} + {Imm16(imm)}))")); break;         // LHU
                case 0x27: lines.Add(W(rt, $"(uint64_t)runtime->read32((uint32_t)({R(rs)} + {Imm16(imm)}))")); break;         // LWU
                case 0x37: lines.Add(W(rt, $"runtime->read64((uint32_t)({R(rs)} + {Imm16(imm)}))")); break;                   // LD
                case 0x1E: lines.Add($"runtime->read128((uint32_t)({R(rs)} + {Imm16(imm)}), &ctx->q[{rt}]); /* LQ */"); break; // LQ

                // Unaligned loads
                case 0x22: lines.Add($"{{ {W(rt, $"runtime->lwl((uint32_t)({R(rs)} + {Imm16(imm)}), (uint32_t){R(rt)})")} }} /* LWL */"); break;
                case 0x26: lines.Add($"{{ {W(rt, $"runtime->lwr((uint32_t)({R(rs)} + {Imm16(imm)}), (uint32_t){R(rt)})")} }} /* LWR */"); break;

                // Stores
                case 0x28: lines.Add($"runtime->write8((uint32_t)({R(rs)} + {Imm16(imm)}), (uint8_t){R(rt)}); /* SB */"); break;
                case 0x29: lines.Add($"runtime->write16((uint32_t)({R(rs)} + {Imm16(imm)}), (uint16_t){R(rt)}); /* SH */"); break;
                case 0x2B: lines.Add($"runtime->write32((uint32_t)({R(rs)} + {Imm16(imm)}), (uint32_t){R(rt)}); /* SW */"); break;
                case 0x3F: lines.Add($"runtime->write64((uint32_t)({R(rs)} + {Imm16(imm)}), {R(rt)}); /* SD */"); break;
                case 0x1F: lines.Add($"runtime->write128((uint32_t)({R(rs)} + {Imm16(imm)}), &ctx->q[{rt}]); /* SQ */"); break;

                // Unaligned stores
                case 0x2A: lines.Add($"runtime->swl((uint32_t)({R(rs)} + {Imm16(imm)}), (uint32_t){R(rt)}); /* SWL */"); break;
                case 0x2E: lines.Add($"runtime->swr((uint32_t)({R(rs)} + {Imm16(imm)}), (uint32_t){R(rt)}); /* SWR */"); break;

                // FPU load/store
                case 0x31: lines.Add($"ctx->f[{rt}] = runtime->read32f((uint32_t)({R(rs)} + {Imm16(imm)})); /* LWC1 */"); break;
                case 0x39: lines.Add($"runtime->write32f((uint32_t)({R(rs)} + {Imm16(imm)}), ctx->f[{rt}]); /* SWC1 */"); break;

                // CACHE
                case 0x2F:
                    if (cfg.PatchCache)
                        lines.Add($"/* CACHE op=0x{rt:X2} patched to nop */");
                    else
                        lines.Add($"runtime->cacheOp(0x{rt:X2}U, (uint32_t)({R(rs)} + {Imm16(imm)}));");
                    break;

                // Branches (with delay slot)
                case 0x04: case 0x14: // BEQ / BEQL
                {
                    uint t = BranchTarget(pc, imm);
                    EmitDelaySlot(dsWord, pc + 4, local, cfg, lines);
                    EmitBranch($"{R(rs)} == {R(rt)}", t, local, lines);
                    break;
                }
                case 0x05: case 0x15: // BNE / BNEL
                {
                    uint t = BranchTarget(pc, imm);
                    EmitDelaySlot(dsWord, pc + 4, local, cfg, lines);
                    EmitBranch($"{R(rs)} != {R(rt)}", t, local, lines);
                    break;
                }
                case 0x06: case 0x16: // BLEZ / BLEZL
                {
                    uint t = BranchTarget(pc, imm);
                    EmitDelaySlot(dsWord, pc + 4, local, cfg, lines);
                    EmitBranch($"(int64_t){R(rs)} <= 0", t, local, lines);
                    break;
                }
                case 0x07: case 0x17: // BGTZ / BGTZL
                {
                    uint t = BranchTarget(pc, imm);
                    EmitDelaySlot(dsWord, pc + 4, local, cfg, lines);
                    EmitBranch($"(int64_t){R(rs)} > 0", t, local, lines);
                    break;
                }

                default:
                    lines.Add($"/* opcode=0x{opcode:X2}: 0x{word:X8} */ runtime->unhandled(0x{word:X8}U, ctx);");
                    break;
            }
        }

        // ── COP0 ──────────────────────────────────────────────────────────────

        private static void TranslateCop0(uint word, List<string> lines)
        {
            int rs    = Rs(word);
            int rt    = Rt(word);
            int rd    = Rd(word);

            switch (rs)
            {
                case 0x00: lines.Add(W(rt, $"(uint64_t)ctx->cop0[{rd}]")); break;    // MFC0
                case 0x04: lines.Add($"ctx->cop0[{rd}] = (uint32_t){R(rt)};"); break; // MTC0
                case 0x01: lines.Add(W(rt, $"ctx->cop0[{rd}]")); break;              // DMFC0
                case 0x05: lines.Add($"ctx->cop0[{rd}] = {R(rt)};"); break;          // DMTC0
                case 0x10: // CO — TLB / ERET
                {
                    int funct = Funct(word);
                    switch (funct)
                    {
                        case 0x18: lines.Add("runtime->handleEret(ctx); /* ERET */"); break;
                        case 0x01: lines.Add("/* TLBR */ runtime->tlbRead(ctx);"); break;
                        case 0x02: lines.Add("/* TLBWI */ runtime->tlbWriteIndex(ctx);"); break;
                        case 0x06: lines.Add("/* TLBWR */ runtime->tlbWriteRandom(ctx);"); break;
                        case 0x08: lines.Add("/* TLBP */ runtime->tlbProbe(ctx);"); break;
                        default: lines.Add($"/* COP0 CO funct=0x{funct:X2}: 0x{word:X8} */"); break;
                    }
                    break;
                }
                default:
                    lines.Add($"/* COP0 rs=0x{rs:X2}: 0x{word:X8} */");
                    break;
            }
        }

        // ── COP1 / FPU ────────────────────────────────────────────────────────

        private static void TranslateCop1(
            uint word, uint pc,
            uint dsWord, HashSet<uint> local, RecompilerConfig cfg,
            List<string> lines)
        {
            int rs    = Rs(word);   // fmt / sub-opcode
            int rt    = Rt(word);   // ft / branch cc
            int rd    = Rd(word);   // fs
            int shamt = Shamt(word); // fd
            int funct = Funct(word);

            // Sub-opcode in rs field
            switch (rs)
            {
                case 0x00: lines.Add(W(rt, $"(int64_t)(int32_t)PS2_FPR_AS_INT(ctx->f[{rd}])")); return; // MFC1
                case 0x04: lines.Add($"ctx->f[{rd}] = PS2_INT_AS_FPR((uint32_t){R(rt)});"); return;      // MTC1
                case 0x02: lines.Add(W(rt, $"(uint64_t)ctx->fcr[{rd}]")); return;                        // CFC1
                case 0x06: lines.Add($"ctx->fcr[{rd}] = (uint32_t){R(rt)};"); return;                    // CTC1
                case 0x08: // BC1F/BC1T/BC1FL/BC1TL
                {
                    uint tgt = BranchTarget(pc, Imm(word));
                    string cond = (rt & 1) == 0
                        ? "!(ctx->fcr[31] & 0x800000U)"
                        : "(ctx->fcr[31] & 0x800000U)";
                    EmitDelaySlot(dsWord, pc + 4, local, cfg, lines);
                    EmitBranch(cond, tgt, local, lines);
                    return;
                }
                case 0x10: break; // S-format — handled below
                default:
                    lines.Add($"/* COP1 rs=0x{rs:X2}: 0x{word:X8} */ runtime->unhandled(0x{word:X8}U, ctx);");
                    return;
            }

            // COP1.S (fmt=S, rs=0x10): fd=shamt, fs=rd, ft=rt
            switch (funct)
            {
                case 0x00: lines.Add($"ctx->f[{shamt}] = PS2_FLUSH(ctx->f[{rd}] + ctx->f[{rt}]);"); break;    // ADD.S
                case 0x01: lines.Add($"ctx->f[{shamt}] = PS2_FLUSH(ctx->f[{rd}] - ctx->f[{rt}]);"); break;    // SUB.S
                case 0x02: lines.Add($"ctx->f[{shamt}] = PS2_FLUSH(ctx->f[{rd}] * ctx->f[{rt}]);"); break;    // MUL.S
                case 0x03: lines.Add($"ctx->f[{shamt}] = PS2_FLUSH(ctx->f[{rd}] / ctx->f[{rt}]);"); break;    // DIV.S
                case 0x04: lines.Add($"ctx->f[{shamt}] = sqrtf(PS2_FLUSH(ctx->f[{rd}]));"); break;            // SQRT.S
                case 0x05: lines.Add($"ctx->f[{shamt}] = fabsf(ctx->f[{rd}]);"); break;                       // ABS.S
                case 0x06: lines.Add($"ctx->f[{shamt}] = ctx->f[{rd}];"); break;                              // MOV.S
                case 0x07: lines.Add($"ctx->f[{shamt}] = -ctx->f[{rd}];"); break;                             // NEG.S
                case 0x18: lines.Add($"ctx->facc = PS2_FLUSH(ctx->f[{rd}] + ctx->f[{rt}]);"); break;          // ADDA.S
                case 0x19: lines.Add($"ctx->facc = PS2_FLUSH(ctx->f[{rd}] - ctx->f[{rt}]);"); break;          // SUBA.S
                case 0x1A: lines.Add($"ctx->facc = PS2_FLUSH(ctx->f[{rd}] * ctx->f[{rt}]);"); break;          // MULA.S
                case 0x1C: lines.Add($"ctx->f[{shamt}] = PS2_FLUSH(ctx->facc + ctx->f[{rd}] * ctx->f[{rt}]);"); break; // MADD.S
                case 0x1D: lines.Add($"ctx->f[{shamt}] = PS2_FLUSH(ctx->facc - ctx->f[{rd}] * ctx->f[{rt}]);"); break; // MSUB.S
                case 0x1E: lines.Add($"ctx->facc = PS2_FLUSH(ctx->facc + ctx->f[{rd}] * ctx->f[{rt}]);"); break;       // MADDA.S
                case 0x1F: lines.Add($"ctx->facc = PS2_FLUSH(ctx->facc - ctx->f[{rd}] * ctx->f[{rt}]);"); break;       // MSUBA.S
                case 0x24: lines.Add(W(rt, $"(int64_t)(int32_t)PS2_FTOI(ctx->f[{rd}])")); break;   // CVT.W.S
                case 0x28: lines.Add(W(rt, $"(int64_t)(int64_t)ctx->f[{rd}]")); break;              // CVT.L.S
                case 0x20: lines.Add($"ctx->f[{shamt}] = (float)(int32_t){R(rd)};"); break;         // CVT.S.W (rd = GPR)
                // Compare (PS2 EE funct codes: C.EQ=0x32, C.LT=0x34, C.LE=0x36)
                case 0x32: lines.Add($"ctx->fcr[31] = (ctx->fcr[31] & ~0x800000U) | (ctx->f[{rd}] == ctx->f[{rt}] ? 0x800000U : 0U); /* C.EQ.S */"); break;
                case 0x34: lines.Add($"ctx->fcr[31] = (ctx->fcr[31] & ~0x800000U) | (ctx->f[{rd}] < ctx->f[{rt}] ? 0x800000U : 0U); /* C.LT.S */"); break;
                case 0x36: lines.Add($"ctx->fcr[31] = (ctx->fcr[31] & ~0x800000U) | (ctx->f[{rd}] <= ctx->f[{rt}] ? 0x800000U : 0U); /* C.LE.S */"); break;
                default:
                    lines.Add($"/* COP1.S funct=0x{funct:X2}: 0x{word:X8} */ runtime->unhandled(0x{word:X8}U, ctx);");
                    break;
            }
        }

        // ── MMI (EE Multimedia Instructions, opcode 0x1C) ─────────────────────

        private static void TranslateMmi(uint word, List<string> lines)
        {
            int funct = Funct(word);
            int rs    = Rs(word);
            int rt    = Rt(word);
            int rd    = Rd(word);
            int shamt = Shamt(word);

            switch (funct)
            {
                case 0x00: // MADD
                    lines.Add($"{{ int64_t _p=(int64_t)(int32_t){R(rs)}*(int64_t)(int32_t){R(rt)}; int64_t _a=(int64_t)((ctx->hi<<32)|(ctx->lo&0xFFFFFFFFULL)); _a+=_p; ctx->lo=SE32((int32_t)_a); ctx->hi=SE32((int32_t)(_a>>32)); {W(rd, "ctx->lo")} }}");
                    break;
                case 0x01: // MADDU
                    lines.Add($"{{ uint64_t _p=(uint64_t)(uint32_t){R(rs)}*(uint64_t)(uint32_t){R(rt)}; uint64_t _a=(ctx->hi<<32)|(ctx->lo&0xFFFFFFFFULL); _a+=_p; ctx->lo=SE32((int32_t)_a); ctx->hi=SE32((int32_t)(_a>>32)); {W(rd, "ctx->lo")} }}");
                    break;
                case 0x04: // PLZCW
                    lines.Add($"{{ uint32_t _h=(uint32_t)({R(rs)}>>32),_l=(uint32_t){R(rs)}; {W(rd, "((uint64_t)(__builtin_clz(_h^(_h>>1))-1)<<32)|(uint64_t)(__builtin_clz(_l^(_l>>1))-1)")} }}");
                    break;
                case 0x08: TranslateMmi0(word, lines); break;  // MMI0 sub-group
                case 0x09: TranslateMmi2(word, lines); break;  // MMI2 sub-group
                case 0x10: lines.Add(W(rd, "ctx->hi1")); break;           // MFHI1
                case 0x11: lines.Add($"ctx->hi1 = {R(rs)};"); break;      // MTHI1
                case 0x12: lines.Add(W(rd, "ctx->lo1")); break;           // MFLO1
                case 0x13: lines.Add($"ctx->lo1 = {R(rs)};"); break;      // MTLO1
                case 0x18: // MULT1
                    lines.Add($"{{ int64_t _r=(int64_t)(int32_t){R(rs)}*(int64_t)(int32_t){R(rt)}; ctx->lo1=SE32((int32_t)_r); ctx->hi1=SE32((int32_t)(_r>>32)); {W(rd, "ctx->lo1")} }}");
                    break;
                case 0x19: // MULTU1
                    lines.Add($"{{ uint64_t _r=(uint64_t)(uint32_t){R(rs)}*(uint64_t)(uint32_t){R(rt)}; ctx->lo1=SE32((int32_t)_r); ctx->hi1=SE32((int32_t)(_r>>32)); {W(rd, "ctx->lo1")} }}");
                    break;
                case 0x1A: // DIV1
                    lines.Add($"if ({R(rt)}!=0){{ ctx->lo1=SE32((int32_t)((int32_t){R(rs)}/(int32_t){R(rt)})); ctx->hi1=SE32((int32_t)((int32_t){R(rs)}%(int32_t){R(rt)})); }}");
                    break;
                case 0x1B: // DIVU1
                    lines.Add($"if ({R(rt)}!=0){{ ctx->lo1=SE32((uint32_t){R(rs)}/(uint32_t){R(rt)}); ctx->hi1=SE32((uint32_t){R(rs)}%(uint32_t){R(rt)}); }}");
                    break;
                case 0x20: // MADD1
                    lines.Add($"{{ int64_t _p=(int64_t)(int32_t){R(rs)}*(int64_t)(int32_t){R(rt)}; int64_t _a=(int64_t)((ctx->hi1<<32)|(ctx->lo1&0xFFFFFFFFULL)); _a+=_p; ctx->lo1=SE32((int32_t)_a); ctx->hi1=SE32((int32_t)(_a>>32)); {W(rd, "ctx->lo1")} }}");
                    break;
                case 0x21: // MADDU1
                    lines.Add($"{{ uint64_t _p=(uint64_t)(uint32_t){R(rs)}*(uint64_t)(uint32_t){R(rt)}; uint64_t _a=(ctx->hi1<<32)|(ctx->lo1&0xFFFFFFFFULL); _a+=_p; ctx->lo1=SE32((int32_t)_a); ctx->hi1=SE32((int32_t)(_a>>32)); {W(rd, "ctx->lo1")} }}");
                    break;
                case 0x28: TranslateMmi1(word, lines); break;  // MMI1 sub-group
                case 0x29: TranslateMmi3(word, lines); break;  // MMI3 sub-group
                default:
                    lines.Add($"/* MMI funct=0x{funct:X2}: 0x{word:X8} */ runtime->unhandled(0x{word:X8}U, ctx);");
                    break;
            }
        }

        private static void TranslateMmi0(uint word, List<string> lines)
        {
            int shamt = Shamt(word), rs = Rs(word), rt = Rt(word), rd = Rd(word);
            switch (shamt)
            {
                case 0x00: lines.Add($"{W(rd, $"PS2_PADDW({R(rs)},{R(rt)})")} /* PADDW */"); break;
                case 0x01: lines.Add($"{W(rd, $"PS2_PSUBW({R(rs)},{R(rt)})")} /* PSUBW */"); break;
                case 0x04: lines.Add($"{W(rd, $"PS2_PADDH({R(rs)},{R(rt)})")} /* PADDH */"); break;
                case 0x05: lines.Add($"{W(rd, $"PS2_PSUBH({R(rs)},{R(rt)})")} /* PSUBH */"); break;
                case 0x08: lines.Add($"{W(rd, $"PS2_PADDB({R(rs)},{R(rt)})")} /* PADDB */"); break;
                case 0x09: lines.Add($"{W(rd, $"PS2_PSUBB({R(rs)},{R(rt)})")} /* PSUBB */"); break;
                case 0x10: lines.Add($"{W(rd, $"PS2_PADDUW({R(rs)},{R(rt)})")} /* PADDUW */"); break;
                case 0x14: lines.Add($"{W(rd, $"PS2_PADDUH({R(rs)},{R(rt)})")} /* PADDUH */"); break;
                case 0x18: lines.Add($"{W(rd, $"PS2_PADDUB({R(rs)},{R(rt)})")} /* PADDUB */"); break;
                default: lines.Add($"/* MMI0 shamt=0x{shamt:X2}: 0x{word:X8} */ runtime->unhandled(0x{word:X8}U, ctx);"); break;
            }
        }

        private static void TranslateMmi1(uint word, List<string> lines)
        {
            int shamt = Shamt(word), rs = Rs(word), rt = Rt(word), rd = Rd(word);
            switch (shamt)
            {
                case 0x01: lines.Add($"{W(rd, $"PS2_PABSW({R(rt)})")} /* PABSW */"); break;
                case 0x05: lines.Add($"{W(rd, $"PS2_PABSH({R(rt)})")} /* PABSH */"); break;
                case 0x10: lines.Add($"{W(rd, $"PS2_PEXTUW({R(rs)},{R(rt)})")} /* PEXTUW */"); break;
                case 0x12: lines.Add($"{W(rd, $"PS2_PEXTLW({R(rs)},{R(rt)})")} /* PEXTLW */"); break;
                case 0x14: lines.Add($"{W(rd, $"PS2_PEXTUH({R(rs)},{R(rt)})")} /* PEXTUH */"); break;
                case 0x16: lines.Add($"{W(rd, $"PS2_PEXTLH({R(rs)},{R(rt)})")} /* PEXTLH */"); break;
                case 0x18: lines.Add($"{W(rd, $"PS2_PEXTUB({R(rs)},{R(rt)})")} /* PEXTUB */"); break;
                case 0x1A: lines.Add($"{W(rd, $"PS2_PEXTLB({R(rs)},{R(rt)})")} /* PEXTLB */"); break;
                default: lines.Add($"/* MMI1 shamt=0x{shamt:X2}: 0x{word:X8} */ runtime->unhandled(0x{word:X8}U, ctx);"); break;
            }
        }

        private static void TranslateMmi2(uint word, List<string> lines)
        {
            int shamt = Shamt(word), rs = Rs(word), rt = Rt(word), rd = Rd(word);
            switch (shamt)
            {
                case 0x12: lines.Add($"{W(rd, $"{R(rs)} & {R(rt)}")} /* PAND */"); break;
                case 0x13: lines.Add($"{W(rd, $"{R(rs)} ^ {R(rt)}")} /* PXOR */"); break;
                case 0x08: lines.Add($"{W(rd, $"PS2_PMFHI(ctx->hi)")} /* PMFHI */"); break;
                case 0x09: lines.Add($"{W(rd, $"PS2_PMFLO(ctx->lo)")} /* PMFLO */"); break;
                case 0x0A: lines.Add($"PS2_PMTHI({R(rs)}, &ctx->hi); /* PMTHI */"); break;
                case 0x0B: lines.Add($"PS2_PMTLO({R(rs)}, &ctx->lo); /* PMTLO */"); break;
                default: lines.Add($"/* MMI2 shamt=0x{shamt:X2}: 0x{word:X8} */ runtime->unhandled(0x{word:X8}U, ctx);"); break;
            }
        }

        private static void TranslateMmi3(uint word, List<string> lines)
        {
            int shamt = Shamt(word), rs = Rs(word), rt = Rt(word), rd = Rd(word);
            switch (shamt)
            {
                case 0x12: lines.Add($"{W(rd, $"{R(rs)} | {R(rt)}")} /* POR */"); break;
                case 0x13: lines.Add($"{W(rd, $"~({R(rs)} | {R(rt)})")} /* PNOR */"); break;
                case 0x1B: lines.Add($"{W(rd, $"PS2_PCPYUD({R(rs)},{R(rt)})")} /* PCPYUD */"); break;
                case 0x0E: lines.Add($"{W(rd, $"PS2_PCPYLD({R(rs)},{R(rt)})")} /* PCPYLD */"); break;
                case 0x0D: lines.Add($"{W(rd, $"PS2_PCPYH({R(rt)})")} /* PCPYH */"); break;
                default: lines.Add($"/* MMI3 shamt=0x{shamt:X2}: 0x{word:X8} */ runtime->unhandled(0x{word:X8}U, ctx);"); break;
            }
        }

        // ── Branch / jump helpers ──────────────────────────────────────────────

        private static void EmitBranch(string cond, uint target, HashSet<uint> local, List<string> lines)
        {
            if (local.Contains(target))
                lines.Add($"if ({cond}) goto {LabelName(target)};");
            else
                lines.Add($"if ({cond}) {{ RECOMP_JUMP(0x{target:X8}U, ctx, runtime); }}");
        }

        // ── Delay slot helper ──────────────────────────────────────────────────

        private static void EmitDelaySlot(
            uint word, uint pc,
            HashSet<uint> local, RecompilerConfig? cfg,
            List<string> lines)
        {
            if (word == 0x00000000) return;
            lines.Add($"/* delay slot @ 0x{pc:X8} */");
            var ds = Translate(word, pc, 0u, local, cfg ?? new RecompilerConfig());
            lines.AddRange(ds);
        }

        // ── Label collection (first pass) ──────────────────────────────────────

        /// <summary>
        /// First pass over a function's instruction list: collect all virtual addresses
        /// that are branch/jump targets within the function, so they can be emitted as
        /// C++ <c>goto</c> labels.
        /// </summary>
        public static HashSet<uint> CollectLocalLabels(
            IReadOnlyList<(uint address, uint word)> instructions)
        {
            if (instructions.Count == 0) return new HashSet<uint>();

            uint funcStart = instructions[0].address;
            uint funcEnd   = instructions[^1].address + 4;
            var  labels    = new HashSet<uint>();

            foreach (var (addr, word) in instructions)
            {
                int opcode = Opcode(word);

                // J / JAL
                if (opcode == 0x02 || opcode == 0x03)
                {
                    uint t = JumpTarget(addr, Target(word));
                    if (t >= funcStart && t < funcEnd) labels.Add(t);
                    continue;
                }

                // REGIMM branches
                if (opcode == 0x01)
                {
                    int rt = Rt(word);
                    if (rt <= 0x13)
                    {
                        uint t = BranchTarget(addr, Imm(word));
                        if (t >= funcStart && t < funcEnd) labels.Add(t);
                    }
                    continue;
                }

                // I-type branches (0x04–0x07, 0x14–0x17)
                if ((opcode >= 0x04 && opcode <= 0x07) || (opcode >= 0x14 && opcode <= 0x17))
                {
                    uint t = BranchTarget(addr, Imm(word));
                    if (t >= funcStart && t < funcEnd) labels.Add(t);
                    continue;
                }

                // COP1 branches (opcode == 0x11, rs == 0x08)
                if (opcode == 0x11 && Rs(word) == 0x08)
                {
                    uint t = BranchTarget(addr, Imm(word));
                    if (t >= funcStart && t < funcEnd) labels.Add(t);
                }
            }

            return labels;
        }
    }
}
