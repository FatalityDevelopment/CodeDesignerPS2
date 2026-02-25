using System;
using System.Numerics;
using System.Runtime.InteropServices;
using CodeDesigner.Mips64LE.Common.Emulator.Hardware;

namespace CodeDesigner.Mips64LE.Common.VectorUnits.VU1
{
    /// <summary>
    /// Executes VU1 microprograms instruction-by-instruction.
    ///
    /// Each VU instruction occupies 8 bytes:
    ///   bytes [0..3] = lower 32-bit word  (integer/memory/branch side)
    ///   bytes [4..7] = upper 32-bit word  (float arithmetic side)
    ///
    /// Both halves are nominally dual-issued; here they are emulated sequentially
    /// (lower first so stores are visible to the upper arithmetic word).
    ///
    /// The E bit (bit 31 of the upper word) marks the last instruction of the
    /// microprogram.  Execution stops after the E-bit instruction completes.
    /// </summary>
    public static class VU1Executor
    {
        private const int MaxSteps = 1_000_000;

        // ── Public entry points ───────────────────────────────────────────────

        /// <summary>
        /// Run the VU1 microprogram until the E-bit instruction is reached or
        /// <see cref="MaxSteps"/> instructions have been executed (safety guard).
        /// </summary>
        /// <param name="state">VU1 state (must already have IsRunning=true and PC set).</param>
        /// <param name="gif">GIF instance to receive XGKICK data (may be null).</param>
        public static void RunProgram(VU1State state, GIF? gif)
        {
            int guard = MaxSteps;
            while (state.IsRunning && guard-- > 0)
                Step(state, gif);
        }

        /// <summary>Execute a single VU1 instruction pair (8 bytes).</summary>
        public static void Step(VU1State state, GIF? gif)
        {
            int pc = state.PC;
            if (pc + 8 > state.MicroMem.Length)
            {
                state.Stop();
                return;
            }

            // Fetch lower and upper 32-bit words (little-endian).
            uint lower = (uint)(state.MicroMem[pc]
                              | (state.MicroMem[pc + 1] << 8)
                              | (state.MicroMem[pc + 2] << 16)
                              | (state.MicroMem[pc + 3] << 24));
            uint upper = (uint)(state.MicroMem[pc + 4]
                              | (state.MicroMem[pc + 5] << 8)
                              | (state.MicroMem[pc + 6] << 16)
                              | (state.MicroMem[pc + 7] << 24));

            bool eFlag = (upper & 0x80000000u) != 0;

            // Advance PC before execution (branch/jump may overwrite it).
            state.PC = (ushort)(pc + 8);

            // Execute lower word first (memory/integer side).
            bool branched = ExecuteLower(state, lower, gif);

            // Execute upper word (float arithmetic side).
            ExecuteUpper(state, upper);

            if (eFlag)
                state.Stop();

            // If a branch was taken the lower handler already updated state.PC.
            _ = branched;
        }

        // ── Upper instruction (float arithmetic) ─────────────────────────────

        private static void ExecuteUpper(VU1State s, uint upper)
        {
            int opcode = (int)((upper >> 25) & 0x3F);
            int dest   = (int)((upper >> 21) & 0x0F);
            int ft     = (int)((upper >> 16) & 0x1F);
            int fs     = (int)((upper >> 11) & 0x1F);
            int fd     = (int)((upper >>  6) & 0x1F);
            int bc     = (int)(upper & 0x3);
            byte mask  = (byte)dest;

            switch (opcode)
            {
                case 0x00: // ADDbc  fd = fs + ft_bc
                    WriteVFMasked(s, fd, s.VF[fs] + BroadcastV(s.VF[ft], bc), mask); break;
                case 0x01: // SUBbc  fd = fs - ft_bc
                    WriteVFMasked(s, fd, s.VF[fs] - BroadcastV(s.VF[ft], bc), mask); break;
                case 0x02: // MADDbc fd = ACC + fs * ft_bc
                    WriteVFMasked(s, fd, s.ACC + s.VF[fs] * BroadcastV(s.VF[ft], bc), mask); break;
                case 0x03: // MSUBbc fd = ACC - fs * ft_bc
                    WriteVFMasked(s, fd, s.ACC - s.VF[fs] * BroadcastV(s.VF[ft], bc), mask); break;
                case 0x04: // MAXbc
                    WriteVFMasked(s, fd, VecMax(s.VF[fs], BroadcastV(s.VF[ft], bc)), mask); break;
                case 0x05: // MINIbc
                    WriteVFMasked(s, fd, VecMin(s.VF[fs], BroadcastV(s.VF[ft], bc)), mask); break;
                case 0x06: // MULbc  fd = fs * ft_bc
                    WriteVFMasked(s, fd, s.VF[fs] * BroadcastV(s.VF[ft], bc), mask); break;
                case 0x07: // MULq   fd = fs * Q
                    WriteVFMasked(s, fd, s.VF[fs] * new Vector4(s.Q), mask); break;
                case 0x08: // MAXi   fd = max(fs, I)
                    WriteVFMasked(s, fd, VecMax(s.VF[fs], new Vector4(s.I)), mask); break;
                case 0x09: // MULi   fd = fs * I
                    WriteVFMasked(s, fd, s.VF[fs] * new Vector4(s.I), mask); break;
                case 0x0A: // MINIi  fd = min(fs, I)
                    WriteVFMasked(s, fd, VecMin(s.VF[fs], new Vector4(s.I)), mask); break;
                case 0x0B: // ADDq   fd = fs + Q
                    WriteVFMasked(s, fd, s.VF[fs] + new Vector4(s.Q), mask); break;
                case 0x0C: // MADDq  fd = ACC + Q * fs
                    WriteVFMasked(s, fd, s.ACC + new Vector4(s.Q) * s.VF[fs], mask); break;
                case 0x0D: // ADDi   fd = fs + I
                    WriteVFMasked(s, fd, s.VF[fs] + new Vector4(s.I), mask); break;
                case 0x0E: // MADDi  fd = ACC + I * fs
                    WriteVFMasked(s, fd, s.ACC + new Vector4(s.I) * s.VF[fs], mask); break;
                case 0x0F: // SUBq   fd = fs - Q
                    WriteVFMasked(s, fd, s.VF[fs] - new Vector4(s.Q), mask); break;
                case 0x10: // MSUBq  fd = ACC - Q * fs
                    WriteVFMasked(s, fd, s.ACC - new Vector4(s.Q) * s.VF[fs], mask); break;
                case 0x11: // SUBi   fd = fs - I
                    WriteVFMasked(s, fd, s.VF[fs] - new Vector4(s.I), mask); break;
                case 0x12: // MSUBi  fd = ACC - I * fs
                    WriteVFMasked(s, fd, s.ACC - new Vector4(s.I) * s.VF[fs], mask); break;
                case 0x13: // ADD    fd = fs + ft
                    WriteVFMasked(s, fd, s.VF[fs] + s.VF[ft], mask); break;
                case 0x14: // MADD   fd = ACC + ft * fs
                    WriteVFMasked(s, fd, s.ACC + s.VF[ft] * s.VF[fs], mask); break;
                case 0x15: // MUL    fd = fs * ft
                    WriteVFMasked(s, fd, s.VF[fs] * s.VF[ft], mask); break;
                case 0x16: // MAX    fd = max(fs, ft)
                    WriteVFMasked(s, fd, VecMax(s.VF[fs], s.VF[ft]), mask); break;
                case 0x17: // SUB    fd = fs - ft
                    WriteVFMasked(s, fd, s.VF[fs] - s.VF[ft], mask); break;
                case 0x18: // MSUB   fd = ACC - ft * fs
                    WriteVFMasked(s, fd, s.ACC - s.VF[ft] * s.VF[fs], mask); break;
                case 0x19: // OPMSUB fd.xyz = ACC.xyz - (fs × ft)
                {
                    Vector4 cross = OuterProduct(s.VF[fs], s.VF[ft]);
                    Vector4 res = s.ACC - cross;
                    WriteVFMasked(s, fd, res, (byte)(mask & 0x7)); // xyz only
                    break;
                }
                case 0x1A: // MINI   fd = min(fs, ft)
                    WriteVFMasked(s, fd, VecMin(s.VF[fs], s.VF[ft]), mask); break;
                case 0x1B: // OPMULA  ACC.xyz = fs × ft
                {
                    Vector4 cross = OuterProduct(s.VF[fs], s.VF[ft]);
                    s.WriteACC(cross, (byte)(mask & 0x7));
                    break;
                }
                case 0x1C: // Upper special — funct in fd field
                    ExecuteUpperSpecial(s, fd, fs, ft, mask, upper); break;
                // 0x1D, 0x1E, 0x1F: treat as NOP
                default: break;
            }
        }

        private static void ExecuteUpperSpecial(VU1State s, int funct, int fs, int ft, byte mask, uint upper)
        {
            switch (funct)
            {
                case 0x00: // ADDA   ACC = fs + ft
                    s.WriteACC(s.VF[fs] + s.VF[ft], mask); break;
                case 0x01: // SUBA   ACC = fs - ft
                    s.WriteACC(s.VF[fs] - s.VF[ft], mask); break;
                case 0x02: // MADDA  ACC = ACC + fs * ft
                    s.WriteACC(s.ACC + s.VF[fs] * s.VF[ft], mask); break;
                case 0x03: // MSUBA  ACC = ACC - fs * ft
                    s.WriteACC(s.ACC - s.VF[fs] * s.VF[ft], mask); break;
                case 0x04: // ITOF0  fd = (float)(int)fs
                    WriteVFMasked(s, (int)((upper >> 6) & 0x1F),
                        IntBitsToItof(s.VF[fs], 1.0f), mask); break;
                case 0x05: // ITOF4  fd = float(fs) / 16
                    WriteVFMasked(s, (int)((upper >> 6) & 0x1F),
                        IntBitsToItof(s.VF[fs], 16.0f), mask); break;
                case 0x06: // ITOF12
                    WriteVFMasked(s, (int)((upper >> 6) & 0x1F),
                        IntBitsToItof(s.VF[fs], 4096.0f), mask); break;
                case 0x07: // ITOF15
                    WriteVFMasked(s, (int)((upper >> 6) & 0x1F),
                        IntBitsToItof(s.VF[fs], 32768.0f), mask); break;
                case 0x08: // FTOI0  fd = (int)fs
                    WriteVFRaw(s, (int)((upper >> 6) & 0x1F),
                        FtoiVec(s.VF[fs], 1.0f), mask); break;
                case 0x09: // FTOI4
                    WriteVFRaw(s, (int)((upper >> 6) & 0x1F),
                        FtoiVec(s.VF[fs], 16.0f), mask); break;
                case 0x0A: // FTOI12
                    WriteVFRaw(s, (int)((upper >> 6) & 0x1F),
                        FtoiVec(s.VF[fs], 4096.0f), mask); break;
                case 0x0B: // FTOI15
                    WriteVFRaw(s, (int)((upper >> 6) & 0x1F),
                        FtoiVec(s.VF[fs], 32768.0f), mask); break;
                case 0x0C: // MULA   ACC = fs * ft
                    s.WriteACC(s.VF[fs] * s.VF[ft], mask); break;
                case 0x0D: // ABS    fd = |fs|
                    WriteVFMasked(s, (int)((upper >> 6) & 0x1F),
                        Vector4.Abs(s.VF[fs]), mask); break;
                case 0x0E: // MAXA   ACC = max(ACC, fs)
                    s.WriteACC(VecMax(s.ACC, s.VF[fs]), mask); break;
                case 0x0F: // MINIA  ACC = min(ACC, fs)
                    s.WriteACC(VecMin(s.ACC, s.VF[fs]), mask); break;
                case 0x10: // CLIP
                    DoClip(s, fs, ft); break;
                // All other functs (including 0x33 = NOP) → NOP
                default: break;
            }
        }

        // ── Lower instruction (memory / integer / branch) ─────────────────────

        /// <summary>Returns true if a branch was taken (PC was modified).</summary>
        private static bool ExecuteLower(VU1State s, uint lower, GIF? gif)
        {
            // LOI: bit 31 set → store entire lower word as float immediate in I register.
            if ((lower & 0x80000000u) != 0)
            {
                s.I = UintToFloat(lower & 0x7FFFFFFFu); // bit31 cleared per PS2 spec
                return false;
            }

            int opcode = (int)((lower >> 25) & 0x3F);
            int dest   = (int)((lower >> 21) & 0x0F);
            int ft     = (int)((lower >> 16) & 0x1F);
            int fs     = (int)((lower >> 11) & 0x1F);
            int fd     = (int)((lower >>  6) & 0x1F);
            int funct  = (int)(lower & 0x3F);
            byte mask  = (byte)dest;

            switch (opcode)
            {
                case 0x00: // LQ  ft = DataMem[(vi[is] + imm11) * 16]
                {
                    int imm11 = SignExt11((fd << 6) | funct);
                    int addr  = ((s.VI[fs] + imm11) & 0xFFFF) * 16;
                    s.WriteVF(ft, ReadQword(s.DataMem, addr), mask);
                    return false;
                }
                case 0x01: // SQ  DataMem[(vi[it] + imm11) * 16] = vf[fs]
                {
                    int imm11 = SignExt11((fd << 6) | funct);
                    int addr  = ((s.VI[ft] + imm11) & 0xFFFF) * 16;
                    WriteQword(s.DataMem, addr, s.VF[fs], mask);
                    return false;
                }
                case 0x04: // ILW  it = int from data mem qword at component
                {
                    int imm11 = SignExt11((fd << 6) | funct);
                    int qaddr = ((s.VI[fs] + imm11) & 0xFFFF) * 16;
                    int comp  = ComponentIndex(dest);
                    int boff  = qaddr + comp * 4;
                    if (boff + 2 <= s.DataMem.Length)
                        s.WriteVI(ft, (ushort)(s.DataMem[boff] | (s.DataMem[boff + 1] << 8)));
                    return false;
                }
                case 0x05: // ISW  DataMem[...].component = vi[is]
                {
                    int imm11 = SignExt11((fd << 6) | funct);
                    int qaddr = ((s.VI[ft] + imm11) & 0xFFFF) * 16;
                    int comp  = ComponentIndex(dest);
                    int boff  = qaddr + comp * 4;
                    if (boff + 2 <= s.DataMem.Length)
                    {
                        ushort v = s.VI[fs];
                        s.DataMem[boff]     = (byte)v;
                        s.DataMem[boff + 1] = (byte)(v >> 8);
                    }
                    return false;
                }
                case 0x08: // IADDIU  it = is + imm15 (sign-extended)
                {
                    int imm15 = SignExt15((fd << 6) | funct);
                    s.WriteVI(ft, (ushort)(s.VI[fs] + imm15));
                    return false;
                }
                case 0x10: // FCEQ  vi01 = (CLIP == imm24) ? 1 : 0
                {
                    uint imm24 = lower & 0x00FFFFFFu;
                    s.WriteVI(1, (ushort)((s.ClipFlags == imm24) ? 1 : 0));
                    return false;
                }
                case 0x11: // FCSET CLIP = imm24
                    s.ClipFlags = lower & 0x00FFFFFFu;
                    return false;
                case 0x12: // FCAND vi01 = (CLIP & imm24) != 0
                {
                    uint imm24 = lower & 0x00FFFFFFu;
                    s.WriteVI(1, (ushort)(((s.ClipFlags & imm24) != 0) ? 1 : 0));
                    return false;
                }
                case 0x13: // FCOR  vi01 = (CLIP | imm24) == 0xFFFFFF
                {
                    uint imm24 = lower & 0x00FFFFFFu;
                    s.WriteVI(1, (ushort)(((s.ClipFlags | imm24) == 0xFFFFFFu) ? 1 : 0));
                    return false;
                }
                case 0x14: // FSEQ  vi01 = (STATUS == imm12)
                {
                    uint imm12 = lower & 0xFFFu;
                    s.WriteVI(1, (ushort)((s.StatusFlags == imm12) ? 1 : 0));
                    return false;
                }
                case 0x15: // FSSET STATUS = imm12
                    s.StatusFlags = lower & 0xFFFu;
                    return false;
                case 0x16: // FSAND vi01 = STATUS & imm12
                {
                    uint imm12 = lower & 0xFFFu;
                    s.WriteVI(1, (ushort)(s.StatusFlags & imm12));
                    return false;
                }
                case 0x17: // FSOR  vi01 = STATUS | imm12
                {
                    uint imm12 = lower & 0xFFFu;
                    s.WriteVI(1, (ushort)(s.StatusFlags | imm12));
                    return false;
                }
                case 0x18: // FMEQ  vi[it] = (MAC == vi[is]) ? 1 : 0
                    s.WriteVI(ft, (ushort)((s.MACFlags == s.VI[fs]) ? 1 : 0));
                    return false;
                case 0x1A: // FMAND vi[it] = MAC & vi[is]
                    s.WriteVI(ft, (ushort)(s.MACFlags & s.VI[fs]));
                    return false;
                case 0x1C: // FMOR  vi[it] = MAC | vi[is]
                    s.WriteVI(ft, (ushort)(s.MACFlags | s.VI[fs]));
                    return false;

                // Branches: imm11 = bits[10:0] of lower word
                case 0x20: // B   PC += sext(imm11) * 8
                {
                    int imm11 = SignExt11((int)(lower & 0x7FFu));
                    s.PC = (ushort)(s.PC + imm11 * 8);
                    return true;
                }
                case 0x21: // BAL  vi[it] = (PC/8); PC += sext(imm11)*8
                {
                    int imm11 = SignExt11((int)(lower & 0x7FFu));
                    s.WriteVI(ft, (ushort)(s.PC / 8));
                    s.PC = (ushort)(s.PC + imm11 * 8);
                    return true;
                }
                case 0x22: // IBEQ if vi[it]==vi[is] PC += imm11*8
                {
                    int imm11 = SignExt11((int)(lower & 0x7FFu));
                    if (s.VI[ft] == s.VI[fs]) { s.PC = (ushort)(s.PC + imm11 * 8); return true; }
                    return false;
                }
                case 0x23: // IBNE
                {
                    int imm11 = SignExt11((int)(lower & 0x7FFu));
                    if (s.VI[ft] != s.VI[fs]) { s.PC = (ushort)(s.PC + imm11 * 8); return true; }
                    return false;
                }
                case 0x24: // IBLTZ
                {
                    int imm11 = SignExt11((int)(lower & 0x7FFu));
                    if ((short)s.VI[fs] < 0) { s.PC = (ushort)(s.PC + imm11 * 8); return true; }
                    return false;
                }
                case 0x25: // IBGTZ
                {
                    int imm11 = SignExt11((int)(lower & 0x7FFu));
                    if ((short)s.VI[fs] > 0) { s.PC = (ushort)(s.PC + imm11 * 8); return true; }
                    return false;
                }
                case 0x26: // IBLEZ
                {
                    int imm11 = SignExt11((int)(lower & 0x7FFu));
                    if ((short)s.VI[fs] <= 0) { s.PC = (ushort)(s.PC + imm11 * 8); return true; }
                    return false;
                }
                case 0x27: // IBGEZ
                {
                    int imm11 = SignExt11((int)(lower & 0x7FFu));
                    if ((short)s.VI[fs] >= 0) { s.PC = (ushort)(s.PC + imm11 * 8); return true; }
                    return false;
                }
                case 0x28: // JALR  vi[it] = PC/8; PC = vi[is]*8
                    s.WriteVI(ft, (ushort)(s.PC / 8));
                    s.PC = (ushort)(s.VI[fs] * 8);
                    return true;
                case 0x29: // JR    PC = vi[is]*8
                    s.PC = (ushort)(s.VI[fs] * 8);
                    return true;

                case 0x1B: // LOWEROP special — funct selects sub-operation
                    return ExecuteLowerSpecial(s, lower, fs, ft, fd, funct, mask, gif);

                default: return false; // NOP for unrecognised opcodes
            }
        }

        private static bool ExecuteLowerSpecial(VU1State s, uint lower,
            int fs, int ft, int fd, int funct, byte mask, GIF? gif)
        {
            switch (funct)
            {
                case 0x30: // IADD  id = is + it
                    s.WriteVI(fd, (ushort)(s.VI[fs] + s.VI[ft])); break;
                case 0x31: // ISUB  id = is - it
                    s.WriteVI(fd, (ushort)(s.VI[fs] - s.VI[ft])); break;
                case 0x32: // IADDI id = is + imm5 (FT field as signed 5-bit)
                {
                    int imm5 = (ft & 0xF) | ((ft & 0x10) != 0 ? ~0xF : 0);
                    s.WriteVI(fd, (ushort)(s.VI[fs] + imm5));
                    break;
                }
                case 0x34: // IAND  id = is & it
                    s.WriteVI(fd, (ushort)(s.VI[fs] & s.VI[ft])); break;
                case 0x35: // IOR   id = is | it
                    s.WriteVI(fd, (ushort)(s.VI[fs] | s.VI[ft])); break;

                case 0x2C: // MOVE  ft = fs (component-masked float copy)
                    s.WriteVF(ft, s.VF[fs], mask); break;
                case 0x2D: // MR32  ft.xyzw = fs.wxyz (rotate)
                {
                    Vector4 src = s.VF[fs];
                    s.WriteVF(ft, new Vector4(src.W, src.X, src.Y, src.Z), mask);
                    break;
                }
                case 0x24: // WAITQ (no-op in synchronous emulator)
                case 0x28: // WAITP (no-op)
                    break;

                case 0x33: // XGKICK — feed VU1 data memory to GIF (PATH1)
                    DoXGKick(s, fs, gif);
                    break;

                // ── EFU / P-register operations ────────────────────────────
                case 0x18: // ESQRT   P = sqrt(fs.x)
                    s.P = MathF.Sqrt(MathF.Abs(s.VF[fs].X)); break;
                case 0x19: // ERSQRT  P = 1/sqrt(fs.x)
                {
                    float v = MathF.Abs(s.VF[fs].X);
                    s.P = v > 0f ? 1.0f / MathF.Sqrt(v) : float.MaxValue;
                    break;
                }
                case 0x1A: // ERCPR   P = 1/fs.x
                {
                    float v = s.VF[fs].X;
                    s.P = v != 0f ? 1.0f / v : float.MaxValue;
                    break;
                }
                case 0x1B: // ESUM    P = fs.x+fs.y+fs.z+fs.w
                {
                    Vector4 v = s.VF[fs];
                    s.P = v.X + v.Y + v.Z + v.W;
                    break;
                }
                case 0x1C: // EACOS   P = acos(fs.x)
                    s.P = MathF.Acos(Math.Clamp(s.VF[fs].X, -1.0f, 1.0f)); break;
                case 0x1D: // EATAN   P = atan(fs.x)
                    s.P = MathF.Atan(s.VF[fs].X); break;
                case 0x1E: // EATAN2  P = atan2(fs.y, fs.x)
                    s.P = MathF.Atan2(s.VF[fs].Y, s.VF[fs].X); break;
                case 0x1F: // ERLENG  P = 1/sqrt(x^2+y^2+z^2)
                {
                    Vector4 v = s.VF[fs];
                    float len2 = v.X * v.X + v.Y * v.Y + v.Z * v.Z;
                    s.P = len2 > 0f ? 1.0f / MathF.Sqrt(len2) : float.MaxValue;
                    break;
                }
                case 0x20: // ESADD   P = fs.x^2+fs.y^2+fs.z^2
                {
                    Vector4 v = s.VF[fs];
                    s.P = v.X * v.X + v.Y * v.Y + v.Z * v.Z;
                    break;
                }
                case 0x21: // ERSADD  P = 1/(x^2+y^2+z^2)
                {
                    Vector4 v = s.VF[fs];
                    float sum = v.X * v.X + v.Y * v.Y + v.Z * v.Z;
                    s.P = sum > 0f ? 1.0f / sum : float.MaxValue;
                    break;
                }
                case 0x22: // ELENG   P = sqrt(x^2+y^2+z^2)
                {
                    Vector4 v = s.VF[fs];
                    s.P = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
                    break;
                }
                case 0x23: // ERLENG  (duplicate 0x1F)
                    goto case 0x1F;
                case 0x3B: // MFP     fd = P
                case 0x25: // MFP (alias)
                    s.WriteVF(fd, new Vector4(s.P), mask);
                    break;

                // ── Q-register (DIV/SQRT/RSQRT) ───────────────────────────
                case 0x38: // DIV    Q = fs_bc / ft_bc
                {
                    int bc = (int)((lower >> 0) & 0x3); // actually bits from lower word
                    // For DIV encoding: fs_comp in bits[22:21] of lower? 
                    // Use the simple approach: fs.x / ft.x (most common)
                    int fsComp = (int)((lower >> 21) & 0x3);
                    int ftComp = (int)((lower >> 23) & 0x3);
                    float num = GetComponent(s.VF[fs], fsComp);
                    float den = GetComponent(s.VF[ft], ftComp);
                    s.Q = den != 0f ? num / den : (num >= 0f ? float.MaxValue : float.MinValue);
                    break;
                }
                case 0x39: // SQRT   Q = sqrt(ft.x)
                    s.Q = MathF.Sqrt(MathF.Abs(s.VF[ft].X)); break;
                case 0x3A: // RSQRT  Q = 1/sqrt(ft.x)
                {
                    float v = MathF.Abs(s.VF[ft].X);
                    s.Q = v > 0f ? 1.0f / MathF.Sqrt(v) : float.MaxValue;
                    break;
                }

                // ── RNG operations ─────────────────────────────────────────
                case 0x3C: // RNEXT  advance RNG, write to VF[fd]
                    RngAdvance(s);
                    s.WriteVF(fd, new Vector4(UintToFloat(s.RngState)), mask);
                    break;
                case 0x3D: // RGET   VF[fd] = current RNG
                case 0x27: // alias
                    s.WriteVF(fd, new Vector4(UintToFloat(s.RngState)), mask);
                    break;
                case 0x3E: // RINIT  RNG = FS component
                    s.RngState = 0x3F800000u | (FloatToUint(s.VF[fs].X) & 0x7FFFFFu);
                    break;
                case 0x3F: // RXOR   RNG ^= FS component (mantissa bits)
                    s.RngState ^= FloatToUint(s.VF[fs].X) & 0x7FFFFFu;
                    s.RngState = 0x3F800000u | (s.RngState & 0x7FFFFFu);
                    break;

                default: break; // NOP
            }
            return false;
        }

        // ── XGKICK ────────────────────────────────────────────────────────────

        private static void DoXGKick(VU1State s, int isReg, GIF? gif)
        {
            if (gif == null) return;
            int startByte = s.VI[isReg] * 16;
            if (startByte >= s.DataMem.Length) return;

            // Feed the raw data memory starting at startByte as uint32 words.
            // The GIF will consume exactly as much as the embedded GIFtag(s) require.
            ReadOnlySpan<byte> byteSpan = new ReadOnlySpan<byte>(s.DataMem, startByte, s.DataMem.Length - startByte);
            ReadOnlySpan<uint> wordSpan = MemoryMarshal.Cast<byte, uint>(byteSpan);
            gif.Feed(wordSpan);
        }

        // ── CLIP instruction ─────────────────────────────────────────────────

        private static void DoClip(VU1State s, int fs, int ft)
        {
            float w = MathF.Abs(s.VF[ft].W);
            Vector4 v = s.VF[fs];
            // Shift existing 18-bit clip history left by 6 bits.
            uint flags = (s.ClipFlags << 6) & 0x00FFFFFFu;
            if ( v.X >  w) flags |= 0x01;
            if ( v.X < -w) flags |= 0x02;
            if ( v.Y >  w) flags |= 0x04;
            if ( v.Y < -w) flags |= 0x08;
            if ( v.Z >  w) flags |= 0x10;
            if ( v.Z < -w) flags |= 0x20;
            s.ClipFlags = flags;
        }

        // ── Helper utilities ──────────────────────────────────────────────────

        /// <summary>Broadcast a single component of v to all four lanes.</summary>
        private static Vector4 BroadcastV(Vector4 v, int bc) => bc switch
        {
            0 => new Vector4(v.X),
            1 => new Vector4(v.Y),
            2 => new Vector4(v.Z),
            _ => new Vector4(v.W),
        };

        private static Vector4 VecMax(Vector4 a, Vector4 b)
            => new Vector4(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y),
                           MathF.Max(a.Z, b.Z), MathF.Max(a.W, b.W));

        private static Vector4 VecMin(Vector4 a, Vector4 b)
            => new Vector4(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y),
                           MathF.Min(a.Z, b.Z), MathF.Min(a.W, b.W));

        /// <summary>Cross product treating X,Y,Z as 3D vector (W ignored on both).</summary>
        private static Vector4 OuterProduct(Vector4 a, Vector4 b)
            => new Vector4(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X,
                0.0f);

        private static void WriteVFMasked(VU1State s, int reg, Vector4 val, byte mask)
            => s.WriteVF(reg, val, mask);

        /// <summary>Write VF register without triggering denormal flush (for FTOI results stored as raw bits).</summary>
        private static void WriteVFRaw(VU1State s, int reg, Vector4 val, byte mask)
        {
            if (reg == 0) return;
            Vector4 cur = s.VF[reg];
            s.VF[reg] = new Vector4(
                (mask & 0x1) != 0 ? val.X : cur.X,
                (mask & 0x2) != 0 ? val.Y : cur.Y,
                (mask & 0x4) != 0 ? val.Z : cur.Z,
                (mask & 0x8) != 0 ? val.W : cur.W);
        }

        /// <summary>ITOF: reinterpret vf register integer bits as signed int, divide by scale.</summary>
        private static Vector4 IntBitsToItof(Vector4 v, float scale)
            => new Vector4(
                (int)FloatToUint(v.X) / scale,
                (int)FloatToUint(v.Y) / scale,
                (int)FloatToUint(v.Z) / scale,
                (int)FloatToUint(v.W) / scale);

        /// <summary>FTOI: multiply by scale, truncate to int, reinterpret as float bits.</summary>
        private static Vector4 FtoiVec(Vector4 v, float scale)
            => new Vector4(
                UintToFloat((uint)(int)(v.X * scale)),
                UintToFloat((uint)(int)(v.Y * scale)),
                UintToFloat((uint)(int)(v.Z * scale)),
                UintToFloat((uint)(int)(v.W * scale)));

        private static int ComponentIndex(int destBits)
        {
            // Return the index (0=x,1=y,2=z,3=w) of the lowest set bit in destBits.
            for (int i = 0; i < 4; i++)
                if ((destBits & (1 << i)) != 0) return i;
            return 0;
        }

        private static float GetComponent(Vector4 v, int comp) => comp switch
        {
            0 => v.X, 1 => v.Y, 2 => v.Z, _ => v.W
        };

        // ── Data memory I/O ───────────────────────────────────────────────────

        private static Vector4 ReadQword(byte[] mem, int byteAddr)
        {
            if (byteAddr < 0 || byteAddr + 16 > mem.Length) return Vector4.Zero;
            float x = UintToFloat(ReadU32(mem, byteAddr + 0));
            float y = UintToFloat(ReadU32(mem, byteAddr + 4));
            float z = UintToFloat(ReadU32(mem, byteAddr + 8));
            float w = UintToFloat(ReadU32(mem, byteAddr + 12));
            return new Vector4(x, y, z, w);
        }

        private static void WriteQword(byte[] mem, int byteAddr, Vector4 v, byte mask)
        {
            if (byteAddr < 0 || byteAddr + 16 > mem.Length) return;
            if ((mask & 0x1) != 0) WriteU32(mem, byteAddr +  0, FloatToUint(v.X));
            if ((mask & 0x2) != 0) WriteU32(mem, byteAddr +  4, FloatToUint(v.Y));
            if ((mask & 0x4) != 0) WriteU32(mem, byteAddr +  8, FloatToUint(v.Z));
            if ((mask & 0x8) != 0) WriteU32(mem, byteAddr + 12, FloatToUint(v.W));
        }

        private static uint ReadU32(byte[] m, int off)
            => (uint)(m[off] | (m[off + 1] << 8) | (m[off + 2] << 16) | (m[off + 3] << 24));

        private static void WriteU32(byte[] m, int off, uint v)
        {
            m[off]     = (byte)v;
            m[off + 1] = (byte)(v >>  8);
            m[off + 2] = (byte)(v >> 16);
            m[off + 3] = (byte)(v >> 24);
        }

        // ── RNG ───────────────────────────────────────────────────────────────

        private static void RngAdvance(VU1State s)
        {
            // PS2 VU LFSR: 23-bit feedback polynomial x^23 + x^22 + x^21 + x^16 + 1
            uint r = s.RngState & 0x7FFFFFu;
            uint bit = ((r >> 22) ^ (r >> 21) ^ (r >> 20) ^ (r >> 15)) & 1u;
            r = ((r << 1) | bit) & 0x7FFFFFu;
            s.RngState = 0x3F800000u | r;
        }

        // ── Sign-extension helpers ────────────────────────────────────────────

        private static int SignExt11(int v) => ((v & 0x400) != 0) ? (v | unchecked((int)0xFFFFF800)) : (v & 0x7FF);
        private static int SignExt15(int v) => ((v & 0x4000) != 0) ? (v | unchecked((int)0xFFFF8000)) : (v & 0x7FFF);

        // ── Float / uint reinterpret ──────────────────────────────────────────

        private static float UintToFloat(uint u)
            => BitConverter.Int32BitsToSingle((int)u);

        private static uint FloatToUint(float f)
            => (uint)BitConverter.SingleToInt32Bits(f);
    }
}
