using CodeDesigner.Ps2.Common.Cpu.InstrArgs;
using CodeDesigner.Ps2.Common.Cpu.Instructions;
using CodeDesigner.Ps2.Common.CPU;
using System;

/*
PS2 EE Multimedia Instructions (MMI / SPECIAL2, opcode=0x1C)

Direct MMI instructions (opcode=0x1C, funct selects instruction):
| Instruction | Funct | Description |
|-------------|-------|-------------|
| MADD        | 0x00  | Multiply-Add (pipeline 0): HI:LO += rs*rt |
| MADDU       | 0x01  | Multiply-Add Unsigned |
| PLZCW       | 0x04  | Parallel Leading Zero/One Count Word |
| MFHI1       | 0x10  | Move from HI1 register |
| MTHI1       | 0x11  | Move to HI1 register |
| MFLO1       | 0x12  | Move from LO1 register |
| MTLO1       | 0x13  | Move to LO1 register |
| MULT1       | 0x18  | Multiply signed 32-bit (pipeline 1) |
| MULTU1      | 0x19  | Multiply unsigned 32-bit (pipeline 1) |
| DIV1        | 0x1A  | Divide signed 32-bit (pipeline 1) |
| DIVU1       | 0x1B  | Divide unsigned 32-bit (pipeline 1) |
| MADD1       | 0x20  | Multiply-Add (pipeline 1) |
| MADDU1      | 0x21  | Multiply-Add Unsigned (pipeline 1) |

MMI0 sub-group (funct=0x08, shamt selects sub-instruction):
| PADDW  | 0x00 | Parallel Add Word (4x32-bit) |
| PSUBW  | 0x01 | Parallel Subtract Word |
| PCGTW  | 0x02 | Parallel Compare Greater-Than Word |
| PMAXW  | 0x03 | Parallel Maximum Word |
| PADDH  | 0x04 | Parallel Add Halfword (8x16-bit) |
| PSUBH  | 0x05 | Parallel Subtract Halfword |
| PMAXH  | 0x07 | Parallel Maximum Halfword |
| PADDB  | 0x08 | Parallel Add Byte (16x8-bit) |
| PSUBB  | 0x09 | Parallel Subtract Byte |
| PEXTLW | 0x12 | Parallel Extend Lower Word |
| PPACW  | 0x13 | Parallel Pack to Word |
| PEXTLH | 0x16 | Parallel Extend Lower Halfword |
| PPACH  | 0x17 | Parallel Pack to Halfword |
| PEXTLB | 0x1A | Parallel Extend Lower Byte |
| PPACB  | 0x1B | Parallel Pack to Byte |

MMI1 sub-group (funct=0x28, shamt selects sub-instruction):
| PABSW  | 0x01 | Parallel Absolute Word |
| PCEQW  | 0x02 | Parallel Compare Equal Word |
| PMINW  | 0x03 | Parallel Minimum Word |
| PABSH  | 0x05 | Parallel Absolute Halfword |
| PCEQH  | 0x06 | Parallel Compare Equal Halfword |
| PMINH  | 0x07 | Parallel Minimum Halfword |
| PCEQB  | 0x0A | Parallel Compare Equal Byte |
| PADDUW | 0x10 | Parallel Add Unsigned Word (saturating) |
| PSUBUW | 0x11 | Parallel Subtract Unsigned Word (saturating) |
| PADDUH | 0x14 | Parallel Add Unsigned Halfword (saturating) |
| PSUBUH | 0x15 | Parallel Subtract Unsigned Halfword (saturating) |
| PADDUB | 0x18 | Parallel Add Unsigned Byte (saturating) |
| PSUBUB | 0x19 | Parallel Subtract Unsigned Byte (saturating) |

MMI2 sub-group (funct=0x09, shamt selects sub-instruction):
| PMFHI  | 0x08 | Parallel Move From HI registers |
| PMFLO  | 0x09 | Parallel Move From LO registers |
| PCPYLD | 0x0E | Parallel Copy Lower Doubleword |

MMI3 sub-group (funct=0x29, shamt selects sub-instruction):
| PMTHI  | 0x08 | Parallel Move To HI registers |
| PMTLO  | 0x09 | Parallel Move To LO registers |
| PCPYUD | 0x0E | Parallel Copy Upper Doubleword |
| PAND   | 0x12 | Parallel AND (128-bit) |
| POR    | 0x13 | Parallel OR (128-bit) |
| PCPYH  | 0x1B | Parallel Copy Halfword |
*/

namespace CodeDesigner.Ps2.Common.Cpu.Instructions.MMI
{
    /// <summary>Helper methods for 128-bit parallel register operations.</summary>
    internal static class MMIHelpers
    {
        // 128-bit register is stored as RegsLo (bits 63:0) and RegsHi (bits 127:64).

        // --- 32-bit word accessors (n=0..3) ---
        public static int GetW(CpuState cpu, int reg, int n)
            => n < 2 ? (int)(cpu.RegsLo[reg] >> (32 * n)) : (int)(cpu.RegsHi[reg] >> (32 * (n - 2)));

        public static uint GetWU(CpuState cpu, int reg, int n)
            => n < 2 ? (uint)(cpu.RegsLo[reg] >> (32 * n)) : (uint)(cpu.RegsHi[reg] >> (32 * (n - 2)));

        public static void SetW(CpuState cpu, int reg, int n, int val)
        {
            if (reg == 0) return;
            if (n < 2)
            {
                ulong mask = 0xFFFFFFFFUL << (32 * n);
                cpu.RegsLo[reg] = (cpu.RegsLo[reg] & ~mask) | ((ulong)(uint)val << (32 * n));
            }
            else
            {
                ulong mask = 0xFFFFFFFFUL << (32 * (n - 2));
                cpu.RegsHi[reg] = (cpu.RegsHi[reg] & ~mask) | ((ulong)(uint)val << (32 * (n - 2)));
            }
        }

        // --- 16-bit halfword accessors (n=0..7) ---
        public static short GetH(CpuState cpu, int reg, int n)
            => n < 4 ? (short)(cpu.RegsLo[reg] >> (16 * n)) : (short)(cpu.RegsHi[reg] >> (16 * (n - 4)));

        public static ushort GetHU(CpuState cpu, int reg, int n)
            => n < 4 ? (ushort)(cpu.RegsLo[reg] >> (16 * n)) : (ushort)(cpu.RegsHi[reg] >> (16 * (n - 4)));

        public static void SetH(CpuState cpu, int reg, int n, short val)
        {
            if (reg == 0) return;
            if (n < 4)
            {
                ulong mask = 0xFFFFUL << (16 * n);
                cpu.RegsLo[reg] = (cpu.RegsLo[reg] & ~mask) | ((ulong)(ushort)val << (16 * n));
            }
            else
            {
                ulong mask = 0xFFFFUL << (16 * (n - 4));
                cpu.RegsHi[reg] = (cpu.RegsHi[reg] & ~mask) | ((ulong)(ushort)val << (16 * (n - 4)));
            }
        }

        public static void SetHU(CpuState cpu, int reg, int n, ushort val) => SetH(cpu, reg, n, (short)val);

        // --- 8-bit byte accessors (n=0..15) ---
        public static byte GetB(CpuState cpu, int reg, int n)
            => n < 8 ? (byte)(cpu.RegsLo[reg] >> (8 * n)) : (byte)(cpu.RegsHi[reg] >> (8 * (n - 8)));

        public static sbyte GetBS(CpuState cpu, int reg, int n)
            => n < 8 ? (sbyte)(cpu.RegsLo[reg] >> (8 * n)) : (sbyte)(cpu.RegsHi[reg] >> (8 * (n - 8)));

        public static void SetB(CpuState cpu, int reg, int n, byte val)
        {
            if (reg == 0) return;
            if (n < 8)
            {
                ulong mask = 0xFFUL << (8 * n);
                cpu.RegsLo[reg] = (cpu.RegsLo[reg] & ~mask) | ((ulong)val << (8 * n));
            }
            else
            {
                ulong mask = 0xFFUL << (8 * (n - 8));
                cpu.RegsHi[reg] = (cpu.RegsHi[reg] & ~mask) | ((ulong)val << (8 * (n - 8)));
            }
        }

        // --- Saturation helpers ---
        public static uint SatUW(long v) => (uint)Math.Max(0L, Math.Min(0xFFFFFFFFL, v));
        public static ushort SatUH(int v) => (ushort)Math.Max(0, Math.Min(0xFFFF, v));
        public static byte SatUB(int v) => (byte)Math.Max(0, Math.Min(255, v));

        // Write full 128-bit result (lo64, hi64) to register rd
        public static void Write128(CpuState cpu, int reg, ulong lo64, ulong hi64)
        {
            if (reg == 0) return;
            cpu.RegsLo[reg] = lo64;
            cpu.RegsHi[reg] = hi64;
        }
    }

    // =========================================================================
    // Direct MMI instructions (opcode = 0x1C, unique funct)
    // =========================================================================

    // MADD - Multiply-Add (pipeline 0)
    public sealed class MADD : Instruction<Args_R>
    {
        public MADD()
        {
            Opcode = 0x1C; Funct = 0x00; Name = "madd";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "HI:LO = HI:LO + rs * rt";
            Description = "Multiply-Add: HI:LO += sign(rs[31:0]) * sign(rt[31:0]); rd = sign_ext(LO)";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int rs = (int)(cpu.RegsLo[args.Rs] & 0xFFFFFFFF);
            int rt = (int)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
            long prod = (long)rs * rt;
            ulong hiLo = (cpu.HI << 32) | (cpu.LO & 0xFFFFFFFF);
            long result = (long)hiLo + prod;
            cpu.LO = (uint)(ulong)result;
            cpu.HI = (uint)((ulong)result >> 32);
            if (args.Rd != 0)
            {
                long sext = (int)cpu.LO;
                cpu.RegsLo[args.Rd] = (ulong)sext;
                cpu.RegsHi[args.Rd] = sext < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x00);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu == null)
                return $"HI:LO += {CpuHelpers.RegName(args.Rs)} * {CpuHelpers.RegName(args.Rt)}";
            int rs = (int)(cpu.RegsLo[args.Rs] & 0xFFFFFFFF);
            int rt = (int)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
            ulong hiLo = (cpu.HI << 32) | (cpu.LO & 0xFFFFFFFF);
            return $"HI:LO = 0x{hiLo:X16} ({CpuHelpers.RegName(args.Rs)}=0x{(uint)rs:X8} * {CpuHelpers.RegName(args.Rt)}=0x{(uint)rt:X8})";
        }
    }

    // MADDU - Multiply-Add Unsigned (pipeline 0)
    public sealed class MADDU : Instruction<Args_R>
    {
        public MADDU()
        {
            Opcode = 0x1C; Funct = 0x01; Name = "maddu";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "HI:LO = HI:LO + rs * rt (unsigned)";
            Description = "Multiply-Add Unsigned: HI:LO += uint(rs[31:0]) * uint(rt[31:0]); rd = LO";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rs = cpu.RegsLo[args.Rs] & 0xFFFFFFFF;
            ulong rt = cpu.RegsLo[args.Rt] & 0xFFFFFFFF;
            ulong prod = rs * rt;
            ulong hiLo = (cpu.HI << 32) | (cpu.LO & 0xFFFFFFFF);
            ulong result = hiLo + prod;
            cpu.LO = (uint)result;
            cpu.HI = (uint)(result >> 32);
            if (args.Rd != 0) { cpu.RegsLo[args.Rd] = cpu.LO; cpu.RegsHi[args.Rd] = 0; }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x01);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu == null)
                return $"HI:LO += {CpuHelpers.RegName(args.Rs)} * {CpuHelpers.RegName(args.Rt)} (unsigned)";
            ulong rs = cpu.RegsLo[args.Rs] & 0xFFFFFFFF;
            ulong rt = cpu.RegsLo[args.Rt] & 0xFFFFFFFF;
            ulong hiLo = (cpu.HI << 32) | (cpu.LO & 0xFFFFFFFF);
            return $"HI:LO = 0x{hiLo:X16} ({CpuHelpers.RegName(args.Rs)}=0x{(uint)rs:X8} * {CpuHelpers.RegName(args.Rt)}=0x{(uint)rt:X8} unsigned)";
        }
    }

    // PLZCW - Parallel Leading Zero/One Count Word
    public sealed class PLZCW : Instruction<Args_R>
    {
        public PLZCW()
        {
            Opcode = 0x1C; Funct = 0x04; Name = "plzcw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs"];
            Equation = "rd = {plzcw(rs[63:32]), plzcw(rs[31:0])}";
            Description = "Parallel Leading Zero/One Count Word: count leading zeros (positive) or ones (negative) in each 32-bit word";
        }

        private static int PlzcwWord(int w)
        {
            if (w >= 0) return System.Numerics.BitOperations.LeadingZeroCount((uint)w) - 1;
            return System.Numerics.BitOperations.LeadingZeroCount((uint)~w) - 1;
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int w0 = (int)(cpu.RegsLo[args.Rs] & 0xFFFFFFFF);
            int w1 = (int)(cpu.RegsLo[args.Rs] >> 32);
            if (args.Rd != 0)
            {
                cpu.RegsLo[args.Rd] = ((ulong)(uint)PlzcwWord(w1) << 32) | (uint)PlzcwWord(w0);
                cpu.RegsHi[args.Rd] = 0;
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[0] << 11 | 0x04);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs));
            if (cpu != null)
            {
                int w0 = (int)(cpu.RegsLo[args.Rs] & 0xFFFFFFFF);
                int w1 = (int)(cpu.RegsLo[args.Rs] >> 32);
                return $"{eq} => plzcw({w0:X8})={PlzcwWord(w0)}, plzcw({w1:X8})={PlzcwWord(w1)}";
            }
            return eq;
        }
    }

    // MFHI1 - Move from HI1 register
    public sealed class MFHI1 : Instruction<Args_R>
    {
        public MFHI1()
        {
            Opcode = 0x1C; Funct = 0x10; Name = "mfhi1";
            ArgType = InstrArgType.R; ArgNames = ["rd"];
            Equation = "rd = HI1"; Description = "Move from HI1 register (pipeline 1)";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            if (args.Rd != 0) { long s = (int)cpu.HI1; cpu.RegsLo[args.Rd] = (ulong)s; cpu.RegsHi[args.Rd] = s < 0 ? ~0UL : 0UL; }
        }

        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} {CpuHelpers.RegName(args.Rd)}";
        public override uint Asm(int[] args) => (uint)(0x1C << 26 | args[0] << 11 | 0x10);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => cpu != null ? $"{Equation.Replace("rd", CpuHelpers.RegName(args.Rd))} => HI1 = 0x{cpu.HI1:X8}" : Equation.Replace("rd", CpuHelpers.RegName(args.Rd));
    }

    // MTHI1 - Move to HI1 register
    public sealed class MTHI1 : Instruction<Args_R>
    {
        public MTHI1()
        {
            Opcode = 0x1C; Funct = 0x11; Name = "mthi1";
            ArgType = InstrArgType.R; ArgNames = ["rs"];
            Equation = "HI1 = rs"; Description = "Move to HI1 register (pipeline 1)";
        }

        public override void Exec(CpuState cpu, Args_R args) => cpu.HI1 = (uint)(cpu.RegsLo[args.Rs] & 0xFFFFFFFF);
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} {CpuHelpers.RegName(args.Rs)}";
        public override uint Asm(int[] args) => (uint)(0x1C << 26 | args[0] << 21 | 0x11);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => cpu != null ? $"HI1 = {CpuHelpers.RegName(args.Rs)} = 0x{cpu.RegsLo[args.Rs] & 0xFFFFFFFF:X8}" : $"HI1 = {CpuHelpers.RegName(args.Rs)}";
    }

    // MFLO1 - Move from LO1 register
    public sealed class MFLO1 : Instruction<Args_R>
    {
        public MFLO1()
        {
            Opcode = 0x1C; Funct = 0x12; Name = "mflo1";
            ArgType = InstrArgType.R; ArgNames = ["rd"];
            Equation = "rd = LO1"; Description = "Move from LO1 register (pipeline 1)";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            if (args.Rd != 0) { long s = (int)cpu.LO1; cpu.RegsLo[args.Rd] = (ulong)s; cpu.RegsHi[args.Rd] = s < 0 ? ~0UL : 0UL; }
        }

        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} {CpuHelpers.RegName(args.Rd)}";
        public override uint Asm(int[] args) => (uint)(0x1C << 26 | args[0] << 11 | 0x12);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => cpu != null ? $"{Equation.Replace("rd", CpuHelpers.RegName(args.Rd))} => LO1 = 0x{cpu.LO1:X8}" : Equation.Replace("rd", CpuHelpers.RegName(args.Rd));
    }

    // MTLO1 - Move to LO1 register
    public sealed class MTLO1 : Instruction<Args_R>
    {
        public MTLO1()
        {
            Opcode = 0x1C; Funct = 0x13; Name = "mtlo1";
            ArgType = InstrArgType.R; ArgNames = ["rs"];
            Equation = "LO1 = rs"; Description = "Move to LO1 register (pipeline 1)";
        }

        public override void Exec(CpuState cpu, Args_R args) => cpu.LO1 = (uint)(cpu.RegsLo[args.Rs] & 0xFFFFFFFF);
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} {CpuHelpers.RegName(args.Rs)}";
        public override uint Asm(int[] args) => (uint)(0x1C << 26 | args[0] << 21 | 0x13);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => cpu != null ? $"LO1 = {CpuHelpers.RegName(args.Rs)} = 0x{cpu.RegsLo[args.Rs] & 0xFFFFFFFF:X8}" : $"LO1 = {CpuHelpers.RegName(args.Rs)}";
    }

    // MULT1 - Multiply signed 32-bit (pipeline 1)
    public sealed class MULT1 : Instruction<Args_R>
    {
        public MULT1()
        {
            Opcode = 0x1C; Funct = 0x18; Name = "mult1";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "HI1:LO1 = rs * rt (signed)"; Description = "Multiply signed 32-bit, result in HI1:LO1 (pipeline 1)";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int rs = (int)(cpu.RegsLo[args.Rs] & 0xFFFFFFFF);
            int rt = (int)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
            long result = (long)rs * rt;
            cpu.LO1 = (uint)result;
            cpu.HI1 = (uint)(result >> 32 & 0xFFFFFFFF);
            if (args.Rd != 0) { long s = (int)cpu.LO1; cpu.RegsLo[args.Rd] = (ulong)s; cpu.RegsHi[args.Rd] = s < 0 ? ~0UL : 0UL; }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x18);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
            if (cpu != null)
            {
                int rs = (int)(cpu.RegsLo[args.Rs] & 0xFFFFFFFF);
                int rt = (int)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
                return $"{eq} => {rs} * {rt} = {(long)rs * rt}";
            }
            return eq;
        }
    }

    // MULTU1 - Multiply unsigned 32-bit (pipeline 1)
    public sealed class MULTU1 : Instruction<Args_R>
    {
        public MULTU1()
        {
            Opcode = 0x1C; Funct = 0x19; Name = "multu1";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "HI1:LO1 = rs * rt (unsigned)"; Description = "Multiply unsigned 32-bit, result in HI1:LO1 (pipeline 1)";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rs = cpu.RegsLo[args.Rs] & 0xFFFFFFFF;
            ulong rt = cpu.RegsLo[args.Rt] & 0xFFFFFFFF;
            ulong result = rs * rt;
            cpu.LO1 = (uint)result;
            cpu.HI1 = (uint)(result >> 32);
            if (args.Rd != 0) { cpu.RegsLo[args.Rd] = cpu.LO1; cpu.RegsHi[args.Rd] = 0; }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x19);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
            if (cpu != null)
            {
                ulong rs = cpu.RegsLo[args.Rs] & 0xFFFFFFFF;
                ulong rt = cpu.RegsLo[args.Rt] & 0xFFFFFFFF;
                return $"{eq} => {rs} * {rt} = {rs * rt}";
            }
            return eq;
        }
    }

    // DIV1 - Divide signed 32-bit (pipeline 1)
    public sealed class DIV1 : Instruction<Args_R>
    {
        public DIV1()
        {
            Opcode = 0x1C; Funct = 0x1A; Name = "div1";
            ArgType = InstrArgType.R; ArgNames = ["rs", "rt"];
            Equation = "LO1 = rs / rt; HI1 = rs % rt"; Description = "Divide signed 32-bit, quotient in LO1, remainder in HI1";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int rs = (int)(cpu.RegsLo[args.Rs] & 0xFFFFFFFF);
            int rt = (int)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
            if (rt == 0) { cpu.LO1 = rs >= 0 ? 0xFFFFFFFF : 1; cpu.HI1 = (uint)rs; return; }
            if (rs == int.MinValue && rt == -1) { cpu.LO1 = unchecked((uint)int.MinValue); cpu.HI1 = 0; return; }
            cpu.LO1 = (uint)(rs / rt);
            cpu.HI1 = (uint)(rs % rt);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[0] << 21 | args[1] << 16 | 0x1A);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
            if (cpu != null)
            {
                int rs = (int)(cpu.RegsLo[args.Rs] & 0xFFFFFFFF);
                int rt = (int)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
                return rt != 0 ? $"{eq} => {rs} / {rt} = {rs / rt}, {rs} % {rt} = {rs % rt}" : $"{eq} => divide by zero";
            }
            return eq;
        }
    }

    // DIVU1 - Divide unsigned 32-bit (pipeline 1)
    public sealed class DIVU1 : Instruction<Args_R>
    {
        public DIVU1()
        {
            Opcode = 0x1C; Funct = 0x1B; Name = "divu1";
            ArgType = InstrArgType.R; ArgNames = ["rs", "rt"];
            Equation = "LO1 = rs / rt; HI1 = rs % rt (unsigned)"; Description = "Divide unsigned 32-bit, quotient in LO1, remainder in HI1";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            uint rs = (uint)(cpu.RegsLo[args.Rs] & 0xFFFFFFFF);
            uint rt = (uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
            if (rt == 0) { cpu.LO1 = 0xFFFFFFFF; cpu.HI1 = rs; return; }
            cpu.LO1 = rs / rt;
            cpu.HI1 = rs % rt;
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[0] << 21 | args[1] << 16 | 0x1B);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
            if (cpu != null)
            {
                uint rs = (uint)(cpu.RegsLo[args.Rs] & 0xFFFFFFFF);
                uint rt = (uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
                return rt != 0 ? $"{eq} => {rs} / {rt} = {rs / rt}" : $"{eq} => divide by zero";
            }
            return eq;
        }
    }

    // MADD1 - Multiply-Add (pipeline 1)
    public sealed class MADD1 : Instruction<Args_R>
    {
        public MADD1()
        {
            Opcode = 0x1C; Funct = 0x20; Name = "madd1";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "HI1:LO1 = HI1:LO1 + rs * rt"; Description = "Multiply-Add pipeline 1";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int rs = (int)(cpu.RegsLo[args.Rs] & 0xFFFFFFFF);
            int rt = (int)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
            long prod = (long)rs * rt;
            ulong hiLo = (cpu.HI1 << 32) | (cpu.LO1 & 0xFFFFFFFF);
            long result = (long)hiLo + prod;
            cpu.LO1 = (uint)(ulong)result;
            cpu.HI1 = (uint)((ulong)result >> 32);
            if (args.Rd != 0) { long s = (int)cpu.LO1; cpu.RegsLo[args.Rd] = (ulong)s; cpu.RegsHi[args.Rd] = s < 0 ? ~0UL : 0UL; }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x20);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu == null)
                return $"HI1:LO1 += {CpuHelpers.RegName(args.Rs)} * {CpuHelpers.RegName(args.Rt)}";
            int rs = (int)(cpu.RegsLo[args.Rs] & 0xFFFFFFFF);
            int rt = (int)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
            ulong hi1Lo1 = (cpu.HI1 << 32) | (cpu.LO1 & 0xFFFFFFFF);
            return $"HI1:LO1 = 0x{hi1Lo1:X16} ({CpuHelpers.RegName(args.Rs)}=0x{(uint)rs:X8} * {CpuHelpers.RegName(args.Rt)}=0x{(uint)rt:X8})";
        }
    }

    // MADDU1 - Multiply-Add Unsigned (pipeline 1)
    public sealed class MADDU1 : Instruction<Args_R>
    {
        public MADDU1()
        {
            Opcode = 0x1C; Funct = 0x21; Name = "maddu1";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "HI1:LO1 = HI1:LO1 + rs * rt (unsigned)"; Description = "Multiply-Add Unsigned pipeline 1";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rs = cpu.RegsLo[args.Rs] & 0xFFFFFFFF;
            ulong rt = cpu.RegsLo[args.Rt] & 0xFFFFFFFF;
            ulong prod = rs * rt;
            ulong hiLo = (cpu.HI1 << 32) | (cpu.LO1 & 0xFFFFFFFF);
            ulong result = hiLo + prod;
            cpu.LO1 = (uint)result;
            cpu.HI1 = (uint)(result >> 32);
            if (args.Rd != 0) { cpu.RegsLo[args.Rd] = cpu.LO1; cpu.RegsHi[args.Rd] = 0; }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x21);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu == null)
                return $"HI1:LO1 += {CpuHelpers.RegName(args.Rs)} * {CpuHelpers.RegName(args.Rt)} (unsigned)";
            ulong rs = cpu.RegsLo[args.Rs] & 0xFFFFFFFF;
            ulong rt = cpu.RegsLo[args.Rt] & 0xFFFFFFFF;
            ulong hi1Lo1 = (cpu.HI1 << 32) | (cpu.LO1 & 0xFFFFFFFF);
            return $"HI1:LO1 = 0x{hi1Lo1:X16} ({CpuHelpers.RegName(args.Rs)}=0x{(uint)rs:X8} * {CpuHelpers.RegName(args.Rt)}=0x{(uint)rt:X8} unsigned)";
        }
    }

    // =========================================================================
    // MMI0 sub-group (funct=0x08, shamt = sub-opcode)
    // =========================================================================

    // PADDW - Parallel Add Word (4 x 32-bit)
    public sealed class PADDW : Instruction<Args_R>
    {
        public PADDW()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "paddw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs + rt (4x32-bit parallel)"; Description = "Parallel Add Word: 4 independent 32-bit additions";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 4; i++) MMIHelpers.SetW(cpu, args.Rd, i, MMIHelpers.GetW(cpu, args.Rs, i) + MMIHelpers.GetW(cpu, args.Rt, i));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x00 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
            return eq;
        }
    }

    // PSUBW - Parallel Subtract Word (4 x 32-bit)
    public sealed class PSUBW : Instruction<Args_R>
    {
        public PSUBW()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "psubw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs - rt (4x32-bit parallel)"; Description = "Parallel Subtract Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 4; i++) MMIHelpers.SetW(cpu, args.Rd, i, MMIHelpers.GetW(cpu, args.Rs, i) - MMIHelpers.GetW(cpu, args.Rt, i));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x01 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
            return eq;
        }
    }

    // PCGTW - Parallel Compare Greater-Than Word
    public sealed class PCGTW : Instruction<Args_R>
    {
        public PCGTW()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "pcgtw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd[i] = rs[i] > rt[i] ? -1 : 0 (4x32-bit)"; Description = "Parallel Compare Greater-Than Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 4; i++) MMIHelpers.SetW(cpu, args.Rd, i, MMIHelpers.GetW(cpu, args.Rs, i) > MMIHelpers.GetW(cpu, args.Rt, i) ? -1 : 0);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x02 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PMAXW - Parallel Maximum Word (4 x 32-bit)
    public sealed class PMAXW : Instruction<Args_R>
    {
        public PMAXW()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "pmaxw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = max(rs, rt) (4x32-bit)"; Description = "Parallel Maximum Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 4; i++) MMIHelpers.SetW(cpu, args.Rd, i, Math.Max(MMIHelpers.GetW(cpu, args.Rs, i), MMIHelpers.GetW(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x03 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PADDH - Parallel Add Halfword (8 x 16-bit)
    public sealed class PADDH : Instruction<Args_R>
    {
        public PADDH()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "paddh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs + rt (8x16-bit parallel)"; Description = "Parallel Add Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 8; i++) MMIHelpers.SetH(cpu, args.Rd, i, (short)(MMIHelpers.GetH(cpu, args.Rs, i) + MMIHelpers.GetH(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x04 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PSUBH - Parallel Subtract Halfword (8 x 16-bit)
    public sealed class PSUBH : Instruction<Args_R>
    {
        public PSUBH()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "psubh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs - rt (8x16-bit parallel)"; Description = "Parallel Subtract Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 8; i++) MMIHelpers.SetH(cpu, args.Rd, i, (short)(MMIHelpers.GetH(cpu, args.Rs, i) - MMIHelpers.GetH(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x05 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PMAXH - Parallel Maximum Halfword (8 x 16-bit)
    public sealed class PMAXH : Instruction<Args_R>
    {
        public PMAXH()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "pmaxh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = max(rs, rt) (8x16-bit)"; Description = "Parallel Maximum Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 8; i++) MMIHelpers.SetH(cpu, args.Rd, i, (short)Math.Max(MMIHelpers.GetH(cpu, args.Rs, i), MMIHelpers.GetH(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x07 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PADDB - Parallel Add Byte (16 x 8-bit)
    public sealed class PADDB : Instruction<Args_R>
    {
        public PADDB()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "paddb";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs + rt (16x8-bit parallel)"; Description = "Parallel Add Byte";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 16; i++) MMIHelpers.SetB(cpu, args.Rd, i, (byte)(MMIHelpers.GetB(cpu, args.Rs, i) + MMIHelpers.GetB(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x08 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PSUBB - Parallel Subtract Byte (16 x 8-bit)
    public sealed class PSUBB : Instruction<Args_R>
    {
        public PSUBB()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "psubb";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs - rt (16x8-bit parallel)"; Description = "Parallel Subtract Byte";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 16; i++) MMIHelpers.SetB(cpu, args.Rd, i, (byte)(MMIHelpers.GetB(cpu, args.Rs, i) - MMIHelpers.GetB(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x09 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PEXTLW - Parallel Extend Lower Word
    // rd.W3=rs.W1, rd.W2=rt.W1, rd.W1=rs.W0, rd.W0=rt.W0
    public sealed class PEXTLW : Instruction<Args_R>
    {
        public PEXTLW()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "pextlw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = interleave(rs[63:0], rt[63:0]) into 4 words"; Description = "Parallel Extend Lower Word: interleaves lower 2 words of rs and rt";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            MMIHelpers.SetW(cpu, args.Rd, 0, MMIHelpers.GetW(cpu, args.Rt, 0));
            MMIHelpers.SetW(cpu, args.Rd, 1, MMIHelpers.GetW(cpu, args.Rs, 0));
            MMIHelpers.SetW(cpu, args.Rd, 2, MMIHelpers.GetW(cpu, args.Rt, 1));
            MMIHelpers.SetW(cpu, args.Rd, 3, MMIHelpers.GetW(cpu, args.Rs, 1));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x12 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PPACW - Parallel Pack to Word
    // rd = { rs.W2, rs.W0, rt.W2, rt.W0 } (picks even-indexed words)
    public sealed class PPACW : Instruction<Args_R>
    {
        public PPACW()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "ppacw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = pack even words from rs and rt"; Description = "Parallel Pack to Word: packs lower words of each 64-bit half";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            MMIHelpers.SetW(cpu, args.Rd, 0, MMIHelpers.GetW(cpu, args.Rt, 0));
            MMIHelpers.SetW(cpu, args.Rd, 1, MMIHelpers.GetW(cpu, args.Rt, 2));
            MMIHelpers.SetW(cpu, args.Rd, 2, MMIHelpers.GetW(cpu, args.Rs, 0));
            MMIHelpers.SetW(cpu, args.Rd, 3, MMIHelpers.GetW(cpu, args.Rs, 2));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x13 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PEXTLH - Parallel Extend Lower Halfword
    public sealed class PEXTLH : Instruction<Args_R>
    {
        public PEXTLH()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "pextlh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = interleave lower 4 halfwords of rs and rt"; Description = "Parallel Extend Lower Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 4; i++)
            {
                MMIHelpers.SetH(cpu, args.Rd, i * 2,     MMIHelpers.GetH(cpu, args.Rt, i));
                MMIHelpers.SetH(cpu, args.Rd, i * 2 + 1, MMIHelpers.GetH(cpu, args.Rs, i));
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x16 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PPACH - Parallel Pack to Halfword
    public sealed class PPACH : Instruction<Args_R>
    {
        public PPACH()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "ppach";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = pack even halfwords from rs and rt"; Description = "Parallel Pack to Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Takes even-indexed halfwords from rt (0,2,4,6) into rd[0..3]
            // and even-indexed halfwords from rs (0,2,4,6) into rd[4..7]
            for (int i = 0; i < 4; i++) MMIHelpers.SetH(cpu, args.Rd, i,     MMIHelpers.GetH(cpu, args.Rt, i * 2));
            for (int i = 0; i < 4; i++) MMIHelpers.SetH(cpu, args.Rd, 4 + i, MMIHelpers.GetH(cpu, args.Rs, i * 2));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x17 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PEXTLB - Parallel Extend Lower Byte
    public sealed class PEXTLB : Instruction<Args_R>
    {
        public PEXTLB()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "pextlb";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = interleave lower 8 bytes of rs and rt"; Description = "Parallel Extend Lower Byte";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 8; i++)
            {
                MMIHelpers.SetB(cpu, args.Rd, i * 2,     MMIHelpers.GetB(cpu, args.Rt, i));
                MMIHelpers.SetB(cpu, args.Rd, i * 2 + 1, MMIHelpers.GetB(cpu, args.Rs, i));
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x1A << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PPACB - Parallel Pack to Byte
    public sealed class PPACB : Instruction<Args_R>
    {
        public PPACB()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "ppacb";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = pack even bytes from rs and rt"; Description = "Parallel Pack to Byte";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 8; i++) MMIHelpers.SetB(cpu, args.Rd, i,     MMIHelpers.GetB(cpu, args.Rt, i * 2));
            for (int i = 0; i < 8; i++) MMIHelpers.SetB(cpu, args.Rd, 8 + i, MMIHelpers.GetB(cpu, args.Rs, i * 2));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x1B << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // =========================================================================
    // MMI1 sub-group (funct=0x28, shamt = sub-opcode)
    // =========================================================================

    // PABSW - Parallel Absolute Word
    public sealed class PABSW : Instruction<Args_R>
    {
        public PABSW()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "pabsw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt"];
            Equation = "rd = abs(rt) (4x32-bit)"; Description = "Parallel Absolute Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 4; i++)
            {
                int v = MMIHelpers.GetW(cpu, args.Rt, i);
                MMIHelpers.SetW(cpu, args.Rd, i, v == int.MinValue ? int.MaxValue : Math.Abs(v));
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[0] << 11 | 0x01 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PCEQW - Parallel Compare Equal Word
    public sealed class PCEQW : Instruction<Args_R>
    {
        public PCEQW()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "pceqw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd[i] = rs[i] == rt[i] ? -1 : 0 (4x32-bit)"; Description = "Parallel Compare Equal Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 4; i++) MMIHelpers.SetW(cpu, args.Rd, i, MMIHelpers.GetW(cpu, args.Rs, i) == MMIHelpers.GetW(cpu, args.Rt, i) ? -1 : 0);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x02 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PMINW - Parallel Minimum Word (4 x 32-bit)
    public sealed class PMINW : Instruction<Args_R>
    {
        public PMINW()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "pminw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = min(rs, rt) (4x32-bit)"; Description = "Parallel Minimum Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 4; i++) MMIHelpers.SetW(cpu, args.Rd, i, Math.Min(MMIHelpers.GetW(cpu, args.Rs, i), MMIHelpers.GetW(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x03 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PABSH - Parallel Absolute Halfword
    public sealed class PABSH : Instruction<Args_R>
    {
        public PABSH()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "pabsh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt"];
            Equation = "rd = abs(rt) (8x16-bit)"; Description = "Parallel Absolute Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 8; i++)
            {
                short v = MMIHelpers.GetH(cpu, args.Rt, i);
                MMIHelpers.SetH(cpu, args.Rd, i, v == short.MinValue ? short.MaxValue : (short)Math.Abs(v));
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[0] << 11 | 0x05 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PCEQH - Parallel Compare Equal Halfword
    public sealed class PCEQH : Instruction<Args_R>
    {
        public PCEQH()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "pceqh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd[i] = rs[i] == rt[i] ? -1 : 0 (8x16-bit)"; Description = "Parallel Compare Equal Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 8; i++) MMIHelpers.SetH(cpu, args.Rd, i, MMIHelpers.GetH(cpu, args.Rs, i) == MMIHelpers.GetH(cpu, args.Rt, i) ? (short)-1 : (short)0);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x06 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PMINH - Parallel Minimum Halfword (8 x 16-bit)
    public sealed class PMINH : Instruction<Args_R>
    {
        public PMINH()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "pminh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = min(rs, rt) (8x16-bit)"; Description = "Parallel Minimum Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 8; i++) MMIHelpers.SetH(cpu, args.Rd, i, (short)Math.Min(MMIHelpers.GetH(cpu, args.Rs, i), MMIHelpers.GetH(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x07 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PCEQB - Parallel Compare Equal Byte
    public sealed class PCEQB : Instruction<Args_R>
    {
        public PCEQB()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "pceqb";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd[i] = rs[i] == rt[i] ? 0xFF : 0 (16x8-bit)"; Description = "Parallel Compare Equal Byte";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 16; i++) MMIHelpers.SetB(cpu, args.Rd, i, MMIHelpers.GetB(cpu, args.Rs, i) == MMIHelpers.GetB(cpu, args.Rt, i) ? (byte)0xFF : (byte)0);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x0A << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PADDUW - Parallel Add Unsigned Word (saturating)
    public sealed class PADDUW : Instruction<Args_R>
    {
        public PADDUW()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "padduw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = sat(rs + rt, 0, 0xFFFFFFFF) (4x32-bit unsigned)"; Description = "Parallel Add Unsigned Word (saturating)";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 4; i++)
            {
                long sum = (long)MMIHelpers.GetWU(cpu, args.Rs, i) + MMIHelpers.GetWU(cpu, args.Rt, i);
                MMIHelpers.SetW(cpu, args.Rd, i, (int)MMIHelpers.SatUW(sum));
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x10 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PSUBUW - Parallel Subtract Unsigned Word (saturating)
    public sealed class PSUBUW : Instruction<Args_R>
    {
        public PSUBUW()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "psubuw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = sat(rs - rt, 0, 0xFFFFFFFF) (4x32-bit unsigned)"; Description = "Parallel Subtract Unsigned Word (saturating)";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 4; i++)
            {
                long diff = (long)MMIHelpers.GetWU(cpu, args.Rs, i) - MMIHelpers.GetWU(cpu, args.Rt, i);
                MMIHelpers.SetW(cpu, args.Rd, i, (int)MMIHelpers.SatUW(diff));
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x11 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PADDUH - Parallel Add Unsigned Halfword (saturating)
    public sealed class PADDUH : Instruction<Args_R>
    {
        public PADDUH()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "padduh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = sat(rs + rt, 0, 0xFFFF) (8x16-bit unsigned)"; Description = "Parallel Add Unsigned Halfword (saturating)";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 8; i++) MMIHelpers.SetHU(cpu, args.Rd, i, MMIHelpers.SatUH(MMIHelpers.GetHU(cpu, args.Rs, i) + MMIHelpers.GetHU(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x14 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PSUBUH - Parallel Subtract Unsigned Halfword (saturating)
    public sealed class PSUBUH : Instruction<Args_R>
    {
        public PSUBUH()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "psubuh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = sat(rs - rt, 0, 0xFFFF) (8x16-bit unsigned)"; Description = "Parallel Subtract Unsigned Halfword (saturating)";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 8; i++) MMIHelpers.SetHU(cpu, args.Rd, i, MMIHelpers.SatUH(MMIHelpers.GetHU(cpu, args.Rs, i) - MMIHelpers.GetHU(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x15 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PADDUB - Parallel Add Unsigned Byte (saturating)
    public sealed class PADDUB : Instruction<Args_R>
    {
        public PADDUB()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "paddub";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = sat(rs + rt, 0, 255) (16x8-bit unsigned)"; Description = "Parallel Add Unsigned Byte (saturating)";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 16; i++) MMIHelpers.SetB(cpu, args.Rd, i, MMIHelpers.SatUB(MMIHelpers.GetB(cpu, args.Rs, i) + MMIHelpers.GetB(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x18 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PSUBUB - Parallel Subtract Unsigned Byte (saturating)
    public sealed class PSUBUB : Instruction<Args_R>
    {
        public PSUBUB()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "psubub";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = sat(rs - rt, 0, 255) (16x8-bit unsigned)"; Description = "Parallel Subtract Unsigned Byte (saturating)";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 16; i++) MMIHelpers.SetB(cpu, args.Rd, i, MMIHelpers.SatUB(MMIHelpers.GetB(cpu, args.Rs, i) - MMIHelpers.GetB(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x19 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // =========================================================================
    // MMI2 sub-group (funct=0x09, shamt = sub-opcode)
    // =========================================================================

    // PMFHI - Parallel Move From HI registers: rd[127:64]=HI1, rd[63:0]=HI
    public sealed class PMFHI : Instruction<Args_R>
    {
        public PMFHI()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "pmfhi";
            ArgType = InstrArgType.R; ArgNames = ["rd"];
            Equation = "rd[127:64] = HI1; rd[63:0] = HI"; Description = "Parallel Move From HI: packs HI1 into upper and HI into lower of rd";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            if (args.Rd != 0) { cpu.RegsLo[args.Rd] = cpu.HI; cpu.RegsHi[args.Rd] = cpu.HI1; }
        }

        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} {CpuHelpers.RegName(args.Rd)}";
        public override uint Asm(int[] args) => (uint)(0x1C << 26 | args[0] << 11 | 0x08 << 6 | 0x09);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => cpu != null ? $"{Equation.Replace("rd", CpuHelpers.RegName(args.Rd))} => HI=0x{cpu.HI:X8}, HI1=0x{cpu.HI1:X8}" : Equation.Replace("rd", CpuHelpers.RegName(args.Rd));
    }

    // PMFLO - Parallel Move From LO registers: rd[127:64]=LO1, rd[63:0]=LO
    public sealed class PMFLO : Instruction<Args_R>
    {
        public PMFLO()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "pmflo";
            ArgType = InstrArgType.R; ArgNames = ["rd"];
            Equation = "rd[127:64] = LO1; rd[63:0] = LO"; Description = "Parallel Move From LO: packs LO1 into upper and LO into lower of rd";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            if (args.Rd != 0) { cpu.RegsLo[args.Rd] = cpu.LO; cpu.RegsHi[args.Rd] = cpu.LO1; }
        }

        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} {CpuHelpers.RegName(args.Rd)}";
        public override uint Asm(int[] args) => (uint)(0x1C << 26 | args[0] << 11 | 0x09 << 6 | 0x09);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => cpu != null ? $"{Equation.Replace("rd", CpuHelpers.RegName(args.Rd))} => LO=0x{cpu.LO:X8}, LO1=0x{cpu.LO1:X8}" : Equation.Replace("rd", CpuHelpers.RegName(args.Rd));
    }

    // PCPYLD - Parallel Copy Lower Doubleword: rd[127:64]=rs[63:0], rd[63:0]=rt[63:0]
    public sealed class PCPYLD : Instruction<Args_R>
    {
        public PCPYLD()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "pcpyld";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd[127:64]=rs[63:0]; rd[63:0]=rt[63:0]"; Description = "Parallel Copy Lower Doubleword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            if (args.Rd != 0) { cpu.RegsLo[args.Rd] = cpu.RegsLo[args.Rt]; cpu.RegsHi[args.Rd] = cpu.RegsLo[args.Rs]; }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x0E << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // =========================================================================
    // MMI3 sub-group (funct=0x29, shamt = sub-opcode)
    // =========================================================================

    // PMTHI - Parallel Move To HI: HI1=rs[127:64], HI=rs[63:0]
    public sealed class PMTHI : Instruction<Args_R>
    {
        public PMTHI()
        {
            Opcode = 0x1C; Funct = 0x29; Name = "pmthi";
            ArgType = InstrArgType.R; ArgNames = ["rs"];
            Equation = "HI1 = rs[127:64]; HI = rs[63:0]"; Description = "Parallel Move To HI registers";
        }

        public override void Exec(CpuState cpu, Args_R args) { cpu.HI = (uint)cpu.RegsLo[args.Rs]; cpu.HI1 = (uint)cpu.RegsHi[args.Rs]; }
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} {CpuHelpers.RegName(args.Rs)}";
        public override uint Asm(int[] args) => (uint)(0x1C << 26 | args[0] << 21 | 0x08 << 6 | 0x29);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => $"HI = {CpuHelpers.RegName(args.Rs)}[63:0], HI1 = {CpuHelpers.RegName(args.Rs)}[127:64]";
    }

    // PMTLO - Parallel Move To LO: LO1=rs[127:64], LO=rs[63:0]
    public sealed class PMTLO : Instruction<Args_R>
    {
        public PMTLO()
        {
            Opcode = 0x1C; Funct = 0x29; Name = "pmtlo";
            ArgType = InstrArgType.R; ArgNames = ["rs"];
            Equation = "LO1 = rs[127:64]; LO = rs[63:0]"; Description = "Parallel Move To LO registers";
        }

        public override void Exec(CpuState cpu, Args_R args) { cpu.LO = (uint)cpu.RegsLo[args.Rs]; cpu.LO1 = (uint)cpu.RegsHi[args.Rs]; }
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} {CpuHelpers.RegName(args.Rs)}";
        public override uint Asm(int[] args) => (uint)(0x1C << 26 | args[0] << 21 | 0x09 << 6 | 0x29);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => $"LO = {CpuHelpers.RegName(args.Rs)}[63:0], LO1 = {CpuHelpers.RegName(args.Rs)}[127:64]";
    }

    // PCPYUD - Parallel Copy Upper Doubleword: rd[63:0]=rt[127:64], rd[127:64]=rs[127:64]
    public sealed class PCPYUD : Instruction<Args_R>
    {
        public PCPYUD()
        {
            Opcode = 0x1C; Funct = 0x29; Name = "pcpyud";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd[127:64]=rs[127:64]; rd[63:0]=rt[127:64]"; Description = "Parallel Copy Upper Doubleword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            if (args.Rd != 0) { cpu.RegsLo[args.Rd] = cpu.RegsHi[args.Rt]; cpu.RegsHi[args.Rd] = cpu.RegsHi[args.Rs]; }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x0E << 6 | 0x29);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PAND - Parallel AND (128-bit)
    public sealed class PAND : Instruction<Args_R>
    {
        public PAND()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "pand";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs & rt (128-bit)"; Description = "Parallel AND: 128-bit bitwise AND";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            if (args.Rd != 0) { cpu.RegsLo[args.Rd] = cpu.RegsLo[args.Rs] & cpu.RegsLo[args.Rt]; cpu.RegsHi[args.Rd] = cpu.RegsHi[args.Rs] & cpu.RegsHi[args.Rt]; }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x12 << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
            if (cpu != null)
            {
                ulong lo = cpu.RegsLo[args.Rs] & cpu.RegsLo[args.Rt];
                ulong hi = cpu.RegsHi[args.Rs] & cpu.RegsHi[args.Rt];
                return $"{eq} => 0x{hi:X16}_{lo:X16}";
            }
            return eq;
        }
    }

    // POR - Parallel OR (128-bit)
    public sealed class POR : Instruction<Args_R>
    {
        public POR()
        {
            Opcode = 0x1C; Funct = 0x29; Name = "por";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs | rt (128-bit)"; Description = "Parallel OR: 128-bit bitwise OR";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            if (args.Rd != 0) { cpu.RegsLo[args.Rd] = cpu.RegsLo[args.Rs] | cpu.RegsLo[args.Rt]; cpu.RegsHi[args.Rd] = cpu.RegsHi[args.Rs] | cpu.RegsHi[args.Rt]; }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x12 << 6 | 0x29);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
            if (cpu != null)
            {
                ulong lo = cpu.RegsLo[args.Rs] | cpu.RegsLo[args.Rt];
                ulong hi = cpu.RegsHi[args.Rs] | cpu.RegsHi[args.Rt];
                return $"{eq} => 0x{hi:X16}_{lo:X16}";
            }
            return eq;
        }
    }

    // PCPYH - Parallel Copy Halfword: duplicates lowest halfword of each 64-bit half to all 4 halfwords
    public sealed class PCPYH : Instruction<Args_R>
    {
        public PCPYH()
        {
            Opcode = 0x1C; Funct = 0x29; Name = "pcpyh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt"];
            Equation = "rd = {rt[15:0] x4, rt[79:64] x4}"; Description = "Parallel Copy Halfword: copies bit[15:0] of each 64-bit half to all 4 halfwords of that half";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            if (args.Rd != 0)
            {
                ushort lo = (ushort)(cpu.RegsLo[args.Rt] & 0xFFFF);
                ushort hi = (ushort)(cpu.RegsHi[args.Rt] & 0xFFFF);
                ulong loVal = (ulong)lo | ((ulong)lo << 16) | ((ulong)lo << 32) | ((ulong)lo << 48);
                ulong hiVal = (ulong)hi | ((ulong)hi << 16) | ((ulong)hi << 32) | ((ulong)hi << 48);
                cpu.RegsLo[args.Rd] = loVal;
                cpu.RegsHi[args.Rd] = hiVal;
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 16 | args[0] << 11 | 0x1B << 6 | 0x29);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt));
            if (cpu != null)
            {
                ushort lo = (ushort)(cpu.RegsLo[args.Rt] & 0xFFFF);
                ushort hi = (ushort)(cpu.RegsHi[args.Rt] & 0xFFFF);
                return $"{eq} => lo_h={lo:X4}, hi_h={hi:X4}";
            }
            return eq;
        }
    }

    // =========================================================================
    // Direct MMI instructions: PMFHL, PMTHL, PSLLH, PSRLH, PSRAH, PSLLW, PSRLW, PSRAW
    // =========================================================================

    // PMFHL - Parallel Move From HI/LO (funct=0x30)
    public sealed class PMFHL : Instruction<Args_R>
    {
        public PMFHL()
        {
            Opcode = 0x1C; Funct = 0x30; Name = "pmfhl";
            ArgType = InstrArgType.R; ArgNames = ["rd"];
            Equation = "rd.lo = LO; rd.hi = HI";
            Description = "Parallel Move From HI/LO - copies HI/LO to rd (128-bit)";
        }

        private static readonly string[] Formats = ["lw", "uw", "slw", "lh", "sh"];

        // HI/LO are 128-bit on the EE: words 0-1 in HI/LO, words 2-3 in HI1/LO1.
        private static uint W(ulong lo64, ulong hi64, int i) => (uint)((i < 2 ? lo64 : hi64) >> (32 * (i & 1)));

        public override void Exec(CpuState cpu, Args_R args)
        {
            if (args.Rd == 0) return;
            uint[] lo = [W(cpu.LO, cpu.LO1, 0), W(cpu.LO, cpu.LO1, 1), W(cpu.LO, cpu.LO1, 2), W(cpu.LO, cpu.LO1, 3)];
            uint[] hi = [W(cpu.HI, cpu.HI1, 0), W(cpu.HI, cpu.HI1, 1), W(cpu.HI, cpu.HI1, 2), W(cpu.HI, cpu.HI1, 3)];
            uint[] r = new uint[4];
            switch (args.Shamt)
            {
                case 0: r = [lo[0], hi[0], lo[2], hi[2]]; break;                    // .lw
                case 1: r = [lo[1], hi[1], lo[3], hi[3]]; break;                    // .uw
                case 2:                                                             // .slw
                {
                    for (int i = 0; i < 2; i++)
                    {
                        long v = (long)(((ulong)hi[i * 2] << 32) | lo[i * 2]);
                        long s = Math.Clamp(v, int.MinValue, int.MaxValue);
                        r[i * 2] = (uint)s;
                        r[i * 2 + 1] = s < 0 ? 0xFFFFFFFFu : 0u;
                    }
                    break;
                }
                case 3:                                                             // .lh
                    for (int i = 0; i < 2; i++)
                    {
                        r[i * 2]     = (lo[i * 2] & 0xFFFF) | (lo[i * 2 + 1] << 16);
                        r[i * 2 + 1] = (hi[i * 2] & 0xFFFF) | (hi[i * 2 + 1] << 16);
                    }
                    break;
                case 4:                                                             // .sh
                {
                    static uint Sat(uint w) => (ushort)(short)Math.Clamp((int)w, short.MinValue, short.MaxValue);
                    for (int i = 0; i < 2; i++)
                    {
                        r[i * 2]     = Sat(lo[i * 2]) | (Sat(lo[i * 2 + 1]) << 16);
                        r[i * 2 + 1] = Sat(hi[i * 2]) | (Sat(hi[i * 2 + 1]) << 16);
                    }
                    break;
                }
                default: return;
            }
            cpu.RegsLo[args.Rd] = r[0] | ((ulong)r[1] << 32);
            cpu.RegsHi[args.Rd] = r[2] | ((ulong)r[3] << 32);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => args.Shamt < Formats.Length
                ? $"{Name}.{Formats[args.Shamt]} {CpuHelpers.RegName(args.Rd)}"
                : $"{Name} {CpuHelpers.RegName(args.Rd)}, {args.Shamt}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[0] << 11 | 0x30);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => $"{CpuHelpers.RegName(args.Rd)} = HI/LO ({(args.Shamt < Formats.Length ? Formats[args.Shamt] : "?")})";
    }

    // PMTHL - Parallel Move To HI/LO (funct=0x31)
    public sealed class PMTHL : Instruction<Args_R>
    {
        public PMTHL()
        {
            Opcode = 0x1C; Funct = 0x31; Name = "pmthl";
            ArgType = InstrArgType.R; ArgNames = ["rs"];
            Equation = "LO = rs.lo; HI = rs.hi";
            Description = "Parallel Move To HI/LO - copies rs (128-bit) to HI/LO";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // pmthl.lw: LO.w0 = rs.w0, HI.w0 = rs.w1, LO.w2 = rs.w2, HI.w2 = rs.w3.
            if (args.Shamt != 0) return;
            ulong lo = cpu.RegsLo[args.Rs], hi = cpu.RegsHi[args.Rs];
            cpu.LO  = (cpu.LO  & 0xFFFFFFFF_00000000UL) | (uint)lo;
            cpu.HI  = (cpu.HI  & 0xFFFFFFFF_00000000UL) | (uint)(lo >> 32);
            cpu.LO1 = (cpu.LO1 & 0xFFFFFFFF_00000000UL) | (uint)hi;
            cpu.HI1 = (cpu.HI1 & 0xFFFFFFFF_00000000UL) | (uint)(hi >> 32);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => args.Shamt == 0
                ? $"{Name}.lw {CpuHelpers.RegName(args.Rs)}"
                : $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Shamt}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[0] << 21 | 0x31);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rs", CpuHelpers.RegName(args.Rs));
            if (cpu != null) return $"{eq} => rs.lo=0x{cpu.RegsLo[args.Rs]:X16}, rs.hi=0x{cpu.RegsHi[args.Rs]:X16}";
            return eq;
        }
    }

    // PSLLH - Parallel Shift Left Logical Halfword (funct=0x34)
    public sealed class PSLLH : Instruction<Args_R>
    {
        public PSLLH()
        {
            Opcode = 0x1C; Funct = 0x34; Name = "psllh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt", "sa"];
            Equation = "rd = rt << sa (8x16-bit)";
            Description = "Parallel Shift Left Logical Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int sa = args.Shamt & 0xF;
            ulong lo = 0, hi = 0;
            for (int i = 0; i < 4; i++) lo |= (ulong)((ushort)((ushort)(cpu.RegsLo[args.Rt] >> (i * 16)) << sa)) << (i * 16);
            for (int i = 0; i < 4; i++) hi |= (ulong)((ushort)((ushort)(cpu.RegsHi[args.Rt] >> (i * 16)) << sa)) << (i * 16);
            MMIHelpers.Write128(cpu, args.Rd, lo, hi);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {args.Shamt & 0xF}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 16 | args[0] << 11 | args[2] << 6 | 0x34);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt)).Replace("sa", (args.Shamt & 0xF).ToString());
    }

    // PSRLH - Parallel Shift Right Logical Halfword (funct=0x36)
    public sealed class PSRLH : Instruction<Args_R>
    {
        public PSRLH()
        {
            Opcode = 0x1C; Funct = 0x36; Name = "psrlh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt", "sa"];
            Equation = "rd = rt >> sa (8x16-bit logical)";
            Description = "Parallel Shift Right Logical Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int sa = args.Shamt & 0xF;
            ulong lo = 0, hi = 0;
            for (int i = 0; i < 4; i++) lo |= (ulong)((ushort)((ushort)(cpu.RegsLo[args.Rt] >> (i * 16)) >> sa)) << (i * 16);
            for (int i = 0; i < 4; i++) hi |= (ulong)((ushort)((ushort)(cpu.RegsHi[args.Rt] >> (i * 16)) >> sa)) << (i * 16);
            MMIHelpers.Write128(cpu, args.Rd, lo, hi);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {args.Shamt & 0xF}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 16 | args[0] << 11 | args[2] << 6 | 0x36);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt)).Replace("sa", (args.Shamt & 0xF).ToString());
    }

    // PSRAH - Parallel Shift Right Arithmetic Halfword (funct=0x37)
    public sealed class PSRAH : Instruction<Args_R>
    {
        public PSRAH()
        {
            Opcode = 0x1C; Funct = 0x37; Name = "psrah";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt", "sa"];
            Equation = "rd = rt >>> sa (8x16-bit arithmetic)";
            Description = "Parallel Shift Right Arithmetic Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int sa = args.Shamt & 0xF;
            ulong lo = 0, hi = 0;
            for (int i = 0; i < 4; i++) lo |= (ulong)(ushort)((short)(cpu.RegsLo[args.Rt] >> (i * 16)) >> sa) << (i * 16);
            for (int i = 0; i < 4; i++) hi |= (ulong)(ushort)((short)(cpu.RegsHi[args.Rt] >> (i * 16)) >> sa) << (i * 16);
            MMIHelpers.Write128(cpu, args.Rd, lo, hi);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {args.Shamt & 0xF}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 16 | args[0] << 11 | args[2] << 6 | 0x37);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt)).Replace("sa", (args.Shamt & 0xF).ToString());
    }

    // PSLLW - Parallel Shift Left Logical Word (funct=0x3C)
    public sealed class PSLLW : Instruction<Args_R>
    {
        public PSLLW()
        {
            Opcode = 0x1C; Funct = 0x3C; Name = "psllw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt", "sa"];
            Equation = "rd = rt << sa (4x32-bit)";
            Description = "Parallel Shift Left Logical Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int sa = args.Shamt & 0x1F;
            MMIHelpers.Write128(cpu, args.Rd,
                ((cpu.RegsLo[args.Rt] & 0xFFFFFFFF) << sa) | (((cpu.RegsLo[args.Rt] >> 32) << sa) << 32),
                ((cpu.RegsHi[args.Rt] & 0xFFFFFFFF) << sa) | (((cpu.RegsHi[args.Rt] >> 32) << sa) << 32));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {args.Shamt & 0x1F}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 16 | args[0] << 11 | args[2] << 6 | 0x3C);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt)).Replace("sa", (args.Shamt & 0x1F).ToString());
    }

    // PSRLW - Parallel Shift Right Logical Word (funct=0x3E)
    public sealed class PSRLW : Instruction<Args_R>
    {
        public PSRLW()
        {
            Opcode = 0x1C; Funct = 0x3E; Name = "psrlw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt", "sa"];
            Equation = "rd = rt >> sa (4x32-bit logical)";
            Description = "Parallel Shift Right Logical Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int sa = args.Shamt & 0x1F;
            MMIHelpers.Write128(cpu, args.Rd,
                ((cpu.RegsLo[args.Rt] & 0xFFFFFFFF) >> sa) | (((cpu.RegsLo[args.Rt] >> 32) >> sa) << 32),
                ((cpu.RegsHi[args.Rt] & 0xFFFFFFFF) >> sa) | (((cpu.RegsHi[args.Rt] >> 32) >> sa) << 32));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {args.Shamt & 0x1F}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 16 | args[0] << 11 | args[2] << 6 | 0x3E);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt)).Replace("sa", (args.Shamt & 0x1F).ToString());
    }

    // PSRAW - Parallel Shift Right Arithmetic Word (funct=0x3F)
    public sealed class PSRAW : Instruction<Args_R>
    {
        public PSRAW()
        {
            Opcode = 0x1C; Funct = 0x3F; Name = "psraw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt", "sa"];
            Equation = "rd = rt >>> sa (4x32-bit arithmetic)";
            Description = "Parallel Shift Right Arithmetic Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int sa = args.Shamt & 0x1F;
            int w0 = (int)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF) >> sa;
            int w1 = (int)(cpu.RegsLo[args.Rt] >> 32) >> sa;
            int w2 = (int)(cpu.RegsHi[args.Rt] & 0xFFFFFFFF) >> sa;
            int w3 = (int)(cpu.RegsHi[args.Rt] >> 32) >> sa;
            MMIHelpers.Write128(cpu, args.Rd, (uint)w0 | ((ulong)(uint)w1 << 32), (uint)w2 | ((ulong)(uint)w3 << 32));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {args.Shamt & 0x1F}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 16 | args[0] << 11 | args[2] << 6 | 0x3F);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt)).Replace("sa", (args.Shamt & 0x1F).ToString());
    }

    // =========================================================================
    // MMI0 sub-instructions (funct=0x08, dispatched by shamt)
    // =========================================================================

    // Saturation helpers (private static)
    file static class SatHelpers
    {
        public static int Sat32(long v) => v > int.MaxValue ? int.MaxValue : v < int.MinValue ? int.MinValue : (int)v;
        public static short Sat16(int v) => v > short.MaxValue ? short.MaxValue : v < short.MinValue ? short.MinValue : (short)v;
        public static sbyte Sat8(int v) => v > sbyte.MaxValue ? sbyte.MaxValue : v < sbyte.MinValue ? sbyte.MinValue : (sbyte)v;
    }

    // PCGTH - Parallel Compare Greater Than Halfword (funct=0x08, shamt=0x06)
    public sealed class PCGTH : Instruction<Args_R>
    {
        public PCGTH()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "pcgth";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd[h] = (rs[h] > rt[h]) ? 0xFFFF : 0 (8x16-bit)";
            Description = "Parallel Compare Greater Than Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 8; i++) MMIHelpers.SetHU(cpu, args.Rd, i, MMIHelpers.GetH(cpu, args.Rs, i) > MMIHelpers.GetH(cpu, args.Rt, i) ? (ushort)0xFFFF : (ushort)0);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x06 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PCGTB - Parallel Compare Greater Than Byte (funct=0x08, shamt=0x0A)
    public sealed class PCGTB : Instruction<Args_R>
    {
        public PCGTB()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "pcgtb";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd[b] = (rs[b] > rt[b]) ? 0xFF : 0 (16x8-bit)";
            Description = "Parallel Compare Greater Than Byte";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 16; i++) MMIHelpers.SetB(cpu, args.Rd, i, (byte)(MMIHelpers.GetBS(cpu, args.Rs, i) > MMIHelpers.GetBS(cpu, args.Rt, i) ? 0xFF : 0));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x0A << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PADDSW - Parallel Add Signed Saturate Word (funct=0x08, shamt=0x10)
    public sealed class PADDSW : Instruction<Args_R>
    {
        public PADDSW()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "paddsw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = sat32(rs + rt) (4x32-bit signed saturating)";
            Description = "Parallel Add Signed Saturating Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 4; i++) MMIHelpers.SetW(cpu, args.Rd, i, SatHelpers.Sat32((long)MMIHelpers.GetW(cpu, args.Rs, i) + MMIHelpers.GetW(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x10 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PSUBSW - Parallel Subtract Signed Saturate Word (funct=0x08, shamt=0x11)
    public sealed class PSUBSW : Instruction<Args_R>
    {
        public PSUBSW()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "psubsw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = sat32(rs - rt) (4x32-bit signed saturating)";
            Description = "Parallel Subtract Signed Saturating Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 4; i++) MMIHelpers.SetW(cpu, args.Rd, i, SatHelpers.Sat32((long)MMIHelpers.GetW(cpu, args.Rs, i) - MMIHelpers.GetW(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x11 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PADDSH - Parallel Add Signed Saturate Halfword (funct=0x08, shamt=0x14)
    public sealed class PADDSH : Instruction<Args_R>
    {
        public PADDSH()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "paddsh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = sat16(rs + rt) (8x16-bit signed saturating)";
            Description = "Parallel Add Signed Saturating Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 8; i++) MMIHelpers.SetH(cpu, args.Rd, i, SatHelpers.Sat16(MMIHelpers.GetH(cpu, args.Rs, i) + MMIHelpers.GetH(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x14 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PSUBSH - Parallel Subtract Signed Saturate Halfword (funct=0x08, shamt=0x15)
    public sealed class PSUBSH : Instruction<Args_R>
    {
        public PSUBSH()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "psubsh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = sat16(rs - rt) (8x16-bit signed saturating)";
            Description = "Parallel Subtract Signed Saturating Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 8; i++) MMIHelpers.SetH(cpu, args.Rd, i, SatHelpers.Sat16(MMIHelpers.GetH(cpu, args.Rs, i) - MMIHelpers.GetH(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x15 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PADDSB - Parallel Add Signed Saturate Byte (funct=0x08, shamt=0x18)
    public sealed class PADDSB : Instruction<Args_R>
    {
        public PADDSB()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "paddsb";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = sat8(rs + rt) (16x8-bit signed saturating)";
            Description = "Parallel Add Signed Saturating Byte";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 16; i++) MMIHelpers.SetB(cpu, args.Rd, i, (byte)SatHelpers.Sat8(MMIHelpers.GetBS(cpu, args.Rs, i) + MMIHelpers.GetBS(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x18 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PSUBSB - Parallel Subtract Signed Saturate Byte (funct=0x08, shamt=0x19)
    public sealed class PSUBSB : Instruction<Args_R>
    {
        public PSUBSB()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "psubsb";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = sat8(rs - rt) (16x8-bit signed saturating)";
            Description = "Parallel Subtract Signed Saturating Byte";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 16; i++) MMIHelpers.SetB(cpu, args.Rd, i, (byte)SatHelpers.Sat8(MMIHelpers.GetBS(cpu, args.Rs, i) - MMIHelpers.GetBS(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x19 << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PEXT5 - Parallel Expand 5-bit (funct=0x08, shamt=0x1E): 1555 RGB to 8888
    public sealed class PEXT5 : Instruction<Args_R>
    {
        public PEXT5()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "pext5";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt"];
            Equation = "rd = expand5to8(rt) per 32-bit word";
            Description = "Parallel Expand 5-bit to 8-bit (1-5-5-5 RGB to 8-8-8-8)";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 4; i++)
            {
                uint src = MMIHelpers.GetWU(cpu, args.Rt, i);
                uint a = (src >> 15) & 1;
                uint b = (src >> 10) & 0x1F; uint g = (src >> 5) & 0x1F; uint r = src & 0x1F;
                uint dst = (a * 0xFF) << 24 | (b << 3 | b >> 2) << 16 | (g << 3 | g >> 2) << 8 | (r << 3 | r >> 2);
                MMIHelpers.SetW(cpu, args.Rd, i, (int)dst);
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 16 | args[0] << 11 | 0x1E << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PPAC5 - Parallel Pack to 5-bit (funct=0x08, shamt=0x1F): 8888 to 1555 RGB
    public sealed class PPAC5 : Instruction<Args_R>
    {
        public PPAC5()
        {
            Opcode = 0x1C; Funct = 0x08; Name = "ppac5";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt"];
            Equation = "rd = pack8to5(rt) per 32-bit word";
            Description = "Parallel Pack 8-bit to 5-bit (8-8-8-8 to 1-5-5-5 RGB)";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 4; i++)
            {
                uint src = MMIHelpers.GetWU(cpu, args.Rt, i);
                uint a = (src >> 31) & 1;
                uint b = (src >> 19) & 0x1F; uint g = (src >> 11) & 0x1F; uint r = (src >> 3) & 0x1F;
                uint dst = a << 15 | b << 10 | g << 5 | r;
                MMIHelpers.SetW(cpu, args.Rd, i, (int)dst);
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 16 | args[0] << 11 | 0x1F << 6 | 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // =========================================================================
    // MMI1 sub-instructions (funct=0x28, dispatched by shamt)
    // =========================================================================

    // PADSBH - Parallel Add/Subtract Bytes by Halfwords (funct=0x28, shamt=0x04)
    public sealed class PADSBH : Instruction<Args_R>
    {
        public PADSBH()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "padsbh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd: lower bytes add, upper halfwords subtract";
            Description = "Parallel Add Unsigned Bytes / Subtract Signed Halfwords";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Lower 8 bytes: unsigned add
            for (int i = 0; i < 8; i++) MMIHelpers.SetB(cpu, args.Rd, i, (byte)(MMIHelpers.GetB(cpu, args.Rs, i) + MMIHelpers.GetB(cpu, args.Rt, i)));
            // Upper halfwords (h4..h7 = bytes 8..15 as halfwords): signed subtract
            for (int i = 4; i < 8; i++) MMIHelpers.SetH(cpu, args.Rd, i, (short)(MMIHelpers.GetH(cpu, args.Rs, i) - MMIHelpers.GetH(cpu, args.Rt, i)));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x04 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PEXTUW - Parallel Extend Upper from Word (funct=0x28, shamt=0x12)
    public sealed class PEXTUW : Instruction<Args_R>
    {
        public PEXTUW()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "pextuw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = interleave upper words of rs and rt";
            Description = "Parallel Extend Upper from Word - interleaves upper 32-bit words";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // rd.W0=rt.W2, rd.W1=rs.W2, rd.W2=rt.W3, rd.W3=rs.W3
            MMIHelpers.SetW(cpu, args.Rd, 0, MMIHelpers.GetW(cpu, args.Rt, 2));
            MMIHelpers.SetW(cpu, args.Rd, 1, MMIHelpers.GetW(cpu, args.Rs, 2));
            MMIHelpers.SetW(cpu, args.Rd, 2, MMIHelpers.GetW(cpu, args.Rt, 3));
            MMIHelpers.SetW(cpu, args.Rd, 3, MMIHelpers.GetW(cpu, args.Rs, 3));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x12 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PEXTUH - Parallel Extend Upper from Halfword (funct=0x28, shamt=0x16)
    public sealed class PEXTUH : Instruction<Args_R>
    {
        public PEXTUH()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "pextuh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = interleave upper halfwords of rs and rt";
            Description = "Parallel Extend Upper from Halfword - interleaves upper 16-bit halfwords";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 4; i++)
            {
                MMIHelpers.SetH(cpu, args.Rd, i * 2,     MMIHelpers.GetH(cpu, args.Rt, i + 4));
                MMIHelpers.SetH(cpu, args.Rd, i * 2 + 1, MMIHelpers.GetH(cpu, args.Rs, i + 4));
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x16 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PEXTUB - Parallel Extend Upper from Byte (funct=0x28, shamt=0x1A)
    public sealed class PEXTUB : Instruction<Args_R>
    {
        public PEXTUB()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "pextub";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = interleave upper bytes of rs and rt";
            Description = "Parallel Extend Upper from Byte - interleaves upper 8-bit bytes";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 8; i++)
            {
                MMIHelpers.SetB(cpu, args.Rd, i * 2,     MMIHelpers.GetB(cpu, args.Rt, i + 8));
                MMIHelpers.SetB(cpu, args.Rd, i * 2 + 1, MMIHelpers.GetB(cpu, args.Rs, i + 8));
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x1A << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // QFSRV - Quadword Funnel Shift Right Variable (funct=0x28, shamt=0x1B)
    public sealed class QFSRV : Instruction<Args_R>
    {
        public QFSRV()
        {
            Opcode = 0x1C; Funct = 0x28; Name = "qfsrv";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = {rs:rt} >> SA (128-bit funnel shift)";
            Description = "Quadword Funnel Shift Right Variable - shifts {rs:rt} right by SA bits";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int sa = cpu.SA & 0x7F;
            if (sa == 0) { MMIHelpers.Write128(cpu, args.Rd, cpu.RegsLo[args.Rt], cpu.RegsHi[args.Rt]); return; }
            // 256-bit value: rs_hi:rs_lo:rt_hi:rt_lo, shift right by sa bits
            if (sa < 64)
            {
                ulong lo = (cpu.RegsLo[args.Rt] >> sa) | (cpu.RegsHi[args.Rt] << (64 - sa));
                ulong hi = (cpu.RegsHi[args.Rt] >> sa) | (cpu.RegsLo[args.Rs] << (64 - sa));
                MMIHelpers.Write128(cpu, args.Rd, lo, hi);
            }
            else
            {
                int s2 = sa - 64;
                ulong lo = s2 == 0 ? cpu.RegsHi[args.Rt] : (cpu.RegsHi[args.Rt] >> s2) | (cpu.RegsLo[args.Rs] << (64 - s2));
                ulong hi = s2 == 0 ? cpu.RegsLo[args.Rs] : (cpu.RegsLo[args.Rs] >> s2) | (cpu.RegsHi[args.Rs] << (64 - s2));
                MMIHelpers.Write128(cpu, args.Rd, lo, hi);
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x1B << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // =========================================================================
    // MMI2 sub-instructions (funct=0x09, dispatched by shamt)
    // =========================================================================

    // PMADDW - Parallel Multiply-Accumulate Word (funct=0x09, shamt=0x00)
    public sealed class PMADDW : Instruction<Args_R>
    {
        public PMADDW()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "pmaddw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "LO:HI += rs.W0*rt.W0; LO1:HI1 += rs.W2*rt.W2; rd=LO,LO1";
            Description = "Parallel Multiply-Accumulate Signed Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            long a0 = (long)((long)MMIHelpers.GetW(cpu, args.Rs, 0) * MMIHelpers.GetW(cpu, args.Rt, 0));
            long a2 = (long)((long)MMIHelpers.GetW(cpu, args.Rs, 2) * MMIHelpers.GetW(cpu, args.Rt, 2));
            long acc0 = (long)((cpu.HI << 32) | (cpu.LO & 0xFFFFFFFF)) + a0;
            long acc2 = (long)((cpu.HI1 << 32) | (cpu.LO1 & 0xFFFFFFFF)) + a2;
            cpu.LO = (ulong)(uint)acc0; cpu.HI = (ulong)(uint)((ulong)acc0 >> 32);
            cpu.LO1 = (ulong)(uint)acc2; cpu.HI1 = (ulong)(uint)((ulong)acc2 >> 32);
            if (args.Rd != 0) { MMIHelpers.SetW(cpu, args.Rd, 0, (int)cpu.LO); MMIHelpers.SetW(cpu, args.Rd, 2, (int)cpu.LO1); }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x00 << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PSLLVW - Parallel Shift Left Logical Variable Word (funct=0x09, shamt=0x02)
    public sealed class PSLLVW : Instruction<Args_R>
    {
        public PSLLVW()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "psllvw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt", "rs"];
            Equation = "rd.W = rt.W << rs.W[4:0] (2x32-bit)";
            Description = "Parallel Shift Left Logical Variable Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int sa0 = MMIHelpers.GetW(cpu, args.Rs, 0) & 0x1F;
            int sa1 = MMIHelpers.GetW(cpu, args.Rs, 1) & 0x1F;
            MMIHelpers.SetW(cpu, args.Rd, 0, MMIHelpers.GetW(cpu, args.Rt, 0) << sa0);
            MMIHelpers.SetW(cpu, args.Rd, 1, MMIHelpers.GetW(cpu, args.Rt, 1) << sa1);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {CpuHelpers.RegName(args.Rs)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[2] << 21 | args[1] << 16 | args[0] << 11 | 0x02 << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt)).Replace("rs", CpuHelpers.RegName(args.Rs));
    }

    // PSRLVW - Parallel Shift Right Logical Variable Word (funct=0x09, shamt=0x03)
    public sealed class PSRLVW : Instruction<Args_R>
    {
        public PSRLVW()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "psrlvw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt", "rs"];
            Equation = "rd.W = rt.W >> rs.W[4:0] (2x32-bit logical)";
            Description = "Parallel Shift Right Logical Variable Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int sa0 = MMIHelpers.GetW(cpu, args.Rs, 0) & 0x1F;
            int sa1 = MMIHelpers.GetW(cpu, args.Rs, 1) & 0x1F;
            MMIHelpers.SetW(cpu, args.Rd, 0, (int)(MMIHelpers.GetWU(cpu, args.Rt, 0) >> sa0));
            MMIHelpers.SetW(cpu, args.Rd, 1, (int)(MMIHelpers.GetWU(cpu, args.Rt, 1) >> sa1));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {CpuHelpers.RegName(args.Rs)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[2] << 21 | args[1] << 16 | args[0] << 11 | 0x03 << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt)).Replace("rs", CpuHelpers.RegName(args.Rs));
    }

    // PMSUBW - Parallel Multiply-Subtract Word (funct=0x09, shamt=0x04)
    public sealed class PMSUBW : Instruction<Args_R>
    {
        public PMSUBW()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "pmsubw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "LO:HI -= rs.W0*rt.W0; LO1:HI1 -= rs.W2*rt.W2";
            Description = "Parallel Multiply-Subtract Signed Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            long a0 = (long)((long)MMIHelpers.GetW(cpu, args.Rs, 0) * MMIHelpers.GetW(cpu, args.Rt, 0));
            long a2 = (long)((long)MMIHelpers.GetW(cpu, args.Rs, 2) * MMIHelpers.GetW(cpu, args.Rt, 2));
            long acc0 = (long)((cpu.HI << 32) | (cpu.LO & 0xFFFFFFFF)) - a0;
            long acc2 = (long)((cpu.HI1 << 32) | (cpu.LO1 & 0xFFFFFFFF)) - a2;
            cpu.LO = (ulong)(uint)acc0; cpu.HI = (ulong)(uint)((ulong)acc0 >> 32);
            cpu.LO1 = (ulong)(uint)acc2; cpu.HI1 = (ulong)(uint)((ulong)acc2 >> 32);
            if (args.Rd != 0) { MMIHelpers.SetW(cpu, args.Rd, 0, (int)cpu.LO); MMIHelpers.SetW(cpu, args.Rd, 2, (int)cpu.LO1); }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x04 << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PINTH - Parallel Interleave Halfword (funct=0x09, shamt=0x0A)
    public sealed class PINTH : Instruction<Args_R>
    {
        public PINTH()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "pinth";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = interleave(rs[H4..H7], rt[H0..H3])";
            Description = "Parallel Interleave Halfword - interleaves upper halfwords of rs with lower halfwords of rt";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // rd.H0=rt.H0, rd.H1=rs.H4, rd.H2=rt.H1, rd.H3=rs.H5, rd.H4=rt.H2, rd.H5=rs.H6, rd.H6=rt.H3, rd.H7=rs.H7
            MMIHelpers.SetH(cpu, args.Rd, 0, MMIHelpers.GetH(cpu, args.Rt, 0));
            MMIHelpers.SetH(cpu, args.Rd, 1, MMIHelpers.GetH(cpu, args.Rs, 4));
            MMIHelpers.SetH(cpu, args.Rd, 2, MMIHelpers.GetH(cpu, args.Rt, 1));
            MMIHelpers.SetH(cpu, args.Rd, 3, MMIHelpers.GetH(cpu, args.Rs, 5));
            MMIHelpers.SetH(cpu, args.Rd, 4, MMIHelpers.GetH(cpu, args.Rt, 2));
            MMIHelpers.SetH(cpu, args.Rd, 5, MMIHelpers.GetH(cpu, args.Rs, 6));
            MMIHelpers.SetH(cpu, args.Rd, 6, MMIHelpers.GetH(cpu, args.Rt, 3));
            MMIHelpers.SetH(cpu, args.Rd, 7, MMIHelpers.GetH(cpu, args.Rs, 7));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x0A << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PMULTW - Parallel Multiply Word (funct=0x09, shamt=0x0C)
    public sealed class PMULTW : Instruction<Args_R>
    {
        public PMULTW()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "pmultw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "LO=rs.W0*rt.W0; LO1=rs.W2*rt.W2; rd.W0=LO, rd.W2=LO1";
            Description = "Parallel Multiply Signed Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            long p0 = (long)MMIHelpers.GetW(cpu, args.Rs, 0) * MMIHelpers.GetW(cpu, args.Rt, 0);
            long p2 = (long)MMIHelpers.GetW(cpu, args.Rs, 2) * MMIHelpers.GetW(cpu, args.Rt, 2);
            cpu.LO = (ulong)(uint)p0; cpu.HI = (ulong)(uint)(p0 >> 32);
            cpu.LO1 = (ulong)(uint)p2; cpu.HI1 = (ulong)(uint)(p2 >> 32);
            if (args.Rd != 0) { MMIHelpers.SetW(cpu, args.Rd, 0, (int)cpu.LO); MMIHelpers.SetW(cpu, args.Rd, 2, (int)cpu.LO1); }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x0C << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PDIVW - Parallel Divide Word (funct=0x09, shamt=0x0D)
    public sealed class PDIVW : Instruction<Args_R>
    {
        public PDIVW()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "pdivw";
            ArgType = InstrArgType.R; ArgNames = ["rs", "rt"];
            Equation = "LO=rs.W0/rt.W0; HI=rs.W0%rt.W0; LO1=rs.W2/rt.W2; HI1=rs.W2%rt.W2";
            Description = "Parallel Divide Signed Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int d0 = MMIHelpers.GetW(cpu, args.Rt, 0); int d2 = MMIHelpers.GetW(cpu, args.Rt, 2);
            if (d0 != 0) { cpu.LO = (ulong)(uint)(MMIHelpers.GetW(cpu, args.Rs, 0) / d0); cpu.HI = (ulong)(uint)(MMIHelpers.GetW(cpu, args.Rs, 0) % d0); }
            if (d2 != 0) { cpu.LO1 = (ulong)(uint)(MMIHelpers.GetW(cpu, args.Rs, 2) / d2); cpu.HI1 = (ulong)(uint)(MMIHelpers.GetW(cpu, args.Rs, 2) % d2); }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[0] << 21 | args[1] << 16 | 0x0D << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PMADDH - Parallel Multiply-Accumulate Halfword (funct=0x09, shamt=0x10)
    public sealed class PMADDH : Instruction<Args_R>
    {
        public PMADDH()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "pmaddh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "For each 4 halfword pairs: LO/LO1 += rs.H * rt.H";
            Description = "Parallel Multiply-Accumulate Signed Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Accumulate products of 4 halfword pairs into LO/LO1 words
            long acc = (long)((cpu.HI << 32) | (cpu.LO & 0xFFFFFFFF));
            acc += (long)MMIHelpers.GetH(cpu, args.Rs, 0) * MMIHelpers.GetH(cpu, args.Rt, 0)
                 + (long)MMIHelpers.GetH(cpu, args.Rs, 2) * MMIHelpers.GetH(cpu, args.Rt, 2);
            cpu.LO = (ulong)(uint)acc; cpu.HI = (ulong)(uint)((ulong)acc >> 32);
            long acc1 = (long)((cpu.HI1 << 32) | (cpu.LO1 & 0xFFFFFFFF));
            acc1 += (long)MMIHelpers.GetH(cpu, args.Rs, 4) * MMIHelpers.GetH(cpu, args.Rt, 4)
                  + (long)MMIHelpers.GetH(cpu, args.Rs, 6) * MMIHelpers.GetH(cpu, args.Rt, 6);
            cpu.LO1 = (ulong)(uint)acc1; cpu.HI1 = (ulong)(uint)((ulong)acc1 >> 32);
            if (args.Rd != 0) { MMIHelpers.SetW(cpu, args.Rd, 0, (int)cpu.LO); MMIHelpers.SetW(cpu, args.Rd, 2, (int)cpu.LO1); }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x10 << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PHMADH - Parallel Horizontal Multiply-Accumulate Halfword (funct=0x09, shamt=0x11)
    public sealed class PHMADH : Instruction<Args_R>
    {
        public PHMADH()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "phmadh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "LO += rs.H0*rt.H0 + rs.H1*rt.H1; LO1 += rs.H4*rt.H4 + rs.H5*rt.H5";
            Description = "Parallel Horizontal Multiply-Accumulate Signed Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int p0 = MMIHelpers.GetH(cpu, args.Rs, 0) * MMIHelpers.GetH(cpu, args.Rt, 0) + MMIHelpers.GetH(cpu, args.Rs, 1) * MMIHelpers.GetH(cpu, args.Rt, 1);
            int p1 = MMIHelpers.GetH(cpu, args.Rs, 2) * MMIHelpers.GetH(cpu, args.Rt, 2) + MMIHelpers.GetH(cpu, args.Rs, 3) * MMIHelpers.GetH(cpu, args.Rt, 3);
            int p2 = MMIHelpers.GetH(cpu, args.Rs, 4) * MMIHelpers.GetH(cpu, args.Rt, 4) + MMIHelpers.GetH(cpu, args.Rs, 5) * MMIHelpers.GetH(cpu, args.Rt, 5);
            int p3 = MMIHelpers.GetH(cpu, args.Rs, 6) * MMIHelpers.GetH(cpu, args.Rt, 6) + MMIHelpers.GetH(cpu, args.Rs, 7) * MMIHelpers.GetH(cpu, args.Rt, 7);
            cpu.LO = (ulong)(uint)(MMIHelpers.GetW(cpu, args.Rt, 0)) + (ulong)(uint)p0; // simplified
            cpu.LO1 = (ulong)(uint)p2;
            if (args.Rd != 0) { MMIHelpers.SetW(cpu, args.Rd, 0, p0); MMIHelpers.SetW(cpu, args.Rd, 1, p1); MMIHelpers.SetW(cpu, args.Rd, 2, p2); MMIHelpers.SetW(cpu, args.Rd, 3, p3); }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x11 << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PXOR - Parallel XOR 128-bit (funct=0x09, shamt=0x13)
    public sealed class PXOR : Instruction<Args_R>
    {
        public PXOR()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "pxor";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = rs ^ rt (128-bit)";
            Description = "Parallel XOR: 128-bit bitwise XOR";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            if (args.Rd != 0) { cpu.RegsLo[args.Rd] = cpu.RegsLo[args.Rs] ^ cpu.RegsLo[args.Rt]; cpu.RegsHi[args.Rd] = cpu.RegsHi[args.Rs] ^ cpu.RegsHi[args.Rt]; }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x13 << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
            if (cpu != null) { ulong lo = cpu.RegsLo[args.Rs] ^ cpu.RegsLo[args.Rt]; ulong hi = cpu.RegsHi[args.Rs] ^ cpu.RegsHi[args.Rt]; return $"{eq} => 0x{hi:X16}_{lo:X16}"; }
            return eq;
        }
    }

    // PMSUBH - Parallel Multiply-Subtract Halfword (funct=0x09, shamt=0x14)
    public sealed class PMSUBH : Instruction<Args_R>
    {
        public PMSUBH()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "pmsubh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "LO/LO1 -= rs.H * rt.H products";
            Description = "Parallel Multiply-Subtract Signed Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            long acc = (long)((cpu.HI << 32) | (cpu.LO & 0xFFFFFFFF));
            acc -= (long)MMIHelpers.GetH(cpu, args.Rs, 0) * MMIHelpers.GetH(cpu, args.Rt, 0)
                 + (long)MMIHelpers.GetH(cpu, args.Rs, 2) * MMIHelpers.GetH(cpu, args.Rt, 2);
            cpu.LO = (ulong)(uint)acc; cpu.HI = (ulong)(uint)((ulong)acc >> 32);
            long acc1 = (long)((cpu.HI1 << 32) | (cpu.LO1 & 0xFFFFFFFF));
            acc1 -= (long)MMIHelpers.GetH(cpu, args.Rs, 4) * MMIHelpers.GetH(cpu, args.Rt, 4)
                  + (long)MMIHelpers.GetH(cpu, args.Rs, 6) * MMIHelpers.GetH(cpu, args.Rt, 6);
            cpu.LO1 = (ulong)(uint)acc1; cpu.HI1 = (ulong)(uint)((ulong)acc1 >> 32);
            if (args.Rd != 0) { MMIHelpers.SetW(cpu, args.Rd, 0, (int)cpu.LO); MMIHelpers.SetW(cpu, args.Rd, 2, (int)cpu.LO1); }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x14 << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PHMSBH - Parallel Horizontal Multiply-Subtract Halfword (funct=0x09, shamt=0x15)
    public sealed class PHMSBH : Instruction<Args_R>
    {
        public PHMSBH()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "phmsbh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "LO -= rs.H0*rt.H0 - rs.H1*rt.H1 (horizontal)";
            Description = "Parallel Horizontal Multiply-Subtract Signed Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int p0 = MMIHelpers.GetH(cpu, args.Rs, 0) * MMIHelpers.GetH(cpu, args.Rt, 0) - MMIHelpers.GetH(cpu, args.Rs, 1) * MMIHelpers.GetH(cpu, args.Rt, 1);
            int p1 = MMIHelpers.GetH(cpu, args.Rs, 2) * MMIHelpers.GetH(cpu, args.Rt, 2) - MMIHelpers.GetH(cpu, args.Rs, 3) * MMIHelpers.GetH(cpu, args.Rt, 3);
            int p2 = MMIHelpers.GetH(cpu, args.Rs, 4) * MMIHelpers.GetH(cpu, args.Rt, 4) - MMIHelpers.GetH(cpu, args.Rs, 5) * MMIHelpers.GetH(cpu, args.Rt, 5);
            int p3 = MMIHelpers.GetH(cpu, args.Rs, 6) * MMIHelpers.GetH(cpu, args.Rt, 6) - MMIHelpers.GetH(cpu, args.Rs, 7) * MMIHelpers.GetH(cpu, args.Rt, 7);
            if (args.Rd != 0) { MMIHelpers.SetW(cpu, args.Rd, 0, p0); MMIHelpers.SetW(cpu, args.Rd, 1, p1); MMIHelpers.SetW(cpu, args.Rd, 2, p2); MMIHelpers.SetW(cpu, args.Rd, 3, p3); }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x15 << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PEXEH - Exchange Halfwords within Words (funct=0x09, shamt=0x1A)
    public sealed class PEXEH : Instruction<Args_R>
    {
        public PEXEH()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "pexeh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt"];
            Equation = "rd: swap H0<->H1, H2<->H3, etc in each 32-bit word";
            Description = "Parallel Exchange Halfwords within Words";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // In each 32-bit word, swap the two 16-bit halfwords
            for (int i = 0; i < 4; i++)
            {
                short h0 = MMIHelpers.GetH(cpu, args.Rt, i * 2);
                short h1 = MMIHelpers.GetH(cpu, args.Rt, i * 2 + 1);
                MMIHelpers.SetH(cpu, args.Rd, i * 2, h1);
                MMIHelpers.SetH(cpu, args.Rd, i * 2 + 1, h0);
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 16 | args[0] << 11 | 0x1A << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PREVH - Reverse Halfwords (funct=0x09, shamt=0x1B)
    public sealed class PREVH : Instruction<Args_R>
    {
        public PREVH()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "prevh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt"];
            Equation = "rd: reverse all 4 halfwords in each 64-bit half";
            Description = "Parallel Reverse Halfwords";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Lower 64: reverse H0..H3
            short h0 = MMIHelpers.GetH(cpu, args.Rt, 0); short h1 = MMIHelpers.GetH(cpu, args.Rt, 1);
            short h2 = MMIHelpers.GetH(cpu, args.Rt, 2); short h3 = MMIHelpers.GetH(cpu, args.Rt, 3);
            MMIHelpers.SetH(cpu, args.Rd, 0, h3); MMIHelpers.SetH(cpu, args.Rd, 1, h2);
            MMIHelpers.SetH(cpu, args.Rd, 2, h1); MMIHelpers.SetH(cpu, args.Rd, 3, h0);
            // Upper 64: reverse H4..H7
            short h4 = MMIHelpers.GetH(cpu, args.Rt, 4); short h5 = MMIHelpers.GetH(cpu, args.Rt, 5);
            short h6 = MMIHelpers.GetH(cpu, args.Rt, 6); short h7 = MMIHelpers.GetH(cpu, args.Rt, 7);
            MMIHelpers.SetH(cpu, args.Rd, 4, h7); MMIHelpers.SetH(cpu, args.Rd, 5, h6);
            MMIHelpers.SetH(cpu, args.Rd, 6, h5); MMIHelpers.SetH(cpu, args.Rd, 7, h4);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 16 | args[0] << 11 | 0x1B << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PMULTH - Parallel Multiply Halfword (funct=0x09, shamt=0x1C)
    public sealed class PMULTH : Instruction<Args_R>
    {
        public PMULTH()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "pmulth";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "LO/LO1 = products of 4 halfword pairs";
            Description = "Parallel Multiply Signed Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int p0 = MMIHelpers.GetH(cpu, args.Rs, 0) * MMIHelpers.GetH(cpu, args.Rt, 0);
            int p1 = MMIHelpers.GetH(cpu, args.Rs, 2) * MMIHelpers.GetH(cpu, args.Rt, 2);
            int p2 = MMIHelpers.GetH(cpu, args.Rs, 4) * MMIHelpers.GetH(cpu, args.Rt, 4);
            int p3 = MMIHelpers.GetH(cpu, args.Rs, 6) * MMIHelpers.GetH(cpu, args.Rt, 6);
            cpu.LO = (ulong)(uint)p0 | ((ulong)(uint)p1 << 32);
            cpu.LO1 = (ulong)(uint)p2 | ((ulong)(uint)p3 << 32);
            if (args.Rd != 0) { MMIHelpers.SetW(cpu, args.Rd, 0, p0); MMIHelpers.SetW(cpu, args.Rd, 1, p1); MMIHelpers.SetW(cpu, args.Rd, 2, p2); MMIHelpers.SetW(cpu, args.Rd, 3, p3); }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x1C << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PDIVBW - Parallel Divide Broadcast Word (funct=0x09, shamt=0x1D)
    public sealed class PDIVBW : Instruction<Args_R>
    {
        public PDIVBW()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "pdivbw";
            ArgType = InstrArgType.R; ArgNames = ["rs", "rt"];
            Equation = "LO.H[0..3] = rs.H[i] / rt.W0; HI.H[0..3] = rs.H[i] % rt.W0";
            Description = "Parallel Divide Broadcast Word - divides 4 halfwords by one word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int div = MMIHelpers.GetW(cpu, args.Rt, 0);
            if (div == 0) return;
            for (int i = 0; i < 4; i++)
            {
                short h = MMIHelpers.GetH(cpu, args.Rs, i);
                MMIHelpers.SetH(cpu, 0, i, (short)(h / div)); // simplified: write to $0 (ignored)
            }
            // Write quotients to LO and remainders to HI
            cpu.LO = 0; cpu.HI = 0;
            for (int i = 0; i < 4; i++)
            {
                short h = MMIHelpers.GetH(cpu, args.Rs, i);
                if (i < 2) cpu.LO |= (ulong)(ushort)(short)(h / div) << (i * 16);
                if (i < 2) cpu.HI |= (ulong)(ushort)(short)(h % div) << (i * 16);
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[0] << 21 | args[1] << 16 | 0x1D << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PEXEW - Exchange Words within 64-bit chunks (funct=0x09, shamt=0x1E)
    public sealed class PEXEW : Instruction<Args_R>
    {
        public PEXEW()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "pexew";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt"];
            Equation = "rd.W0=rt.W2; rd.W1=rt.W3; rd.W2=rt.W0; rd.W3=rt.W1";
            Description = "Parallel Exchange Words - swaps W0<->W2 and W1<->W3";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int w0 = MMIHelpers.GetW(cpu, args.Rt, 0); int w1 = MMIHelpers.GetW(cpu, args.Rt, 1);
            int w2 = MMIHelpers.GetW(cpu, args.Rt, 2); int w3 = MMIHelpers.GetW(cpu, args.Rt, 3);
            MMIHelpers.SetW(cpu, args.Rd, 0, w2); MMIHelpers.SetW(cpu, args.Rd, 1, w3);
            MMIHelpers.SetW(cpu, args.Rd, 2, w0); MMIHelpers.SetW(cpu, args.Rd, 3, w1);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 16 | args[0] << 11 | 0x1E << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PROT3W - Rotate 3 Words (funct=0x09, shamt=0x1F)
    public sealed class PROT3W : Instruction<Args_R>
    {
        public PROT3W()
        {
            Opcode = 0x1C; Funct = 0x09; Name = "prot3w";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt"];
            Equation = "rd.W0=rt.W1; rd.W1=rt.W2; rd.W2=rt.W0; rd.W3=rt.W3";
            Description = "Parallel Rotate 3 Words";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int w0 = MMIHelpers.GetW(cpu, args.Rt, 0); int w1 = MMIHelpers.GetW(cpu, args.Rt, 1);
            int w2 = MMIHelpers.GetW(cpu, args.Rt, 2); int w3 = MMIHelpers.GetW(cpu, args.Rt, 3);
            MMIHelpers.SetW(cpu, args.Rd, 0, w1); MMIHelpers.SetW(cpu, args.Rd, 1, w2);
            MMIHelpers.SetW(cpu, args.Rd, 2, w0); MMIHelpers.SetW(cpu, args.Rd, 3, w3);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 16 | args[0] << 11 | 0x1F << 6 | 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // =========================================================================
    // MMI3 sub-instructions (funct=0x29, dispatched by shamt)
    // =========================================================================

    // PMADDUW - Parallel Multiply-Accumulate Unsigned Word (funct=0x29, shamt=0x00)
    public sealed class PMADDUW : Instruction<Args_R>
    {
        public PMADDUW()
        {
            Opcode = 0x1C; Funct = 0x29; Name = "pmadduw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "LO:HI += rs.W0*rt.W0 (unsigned); LO1:HI1 += rs.W2*rt.W2";
            Description = "Parallel Multiply-Accumulate Unsigned Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong a0 = (ulong)MMIHelpers.GetWU(cpu, args.Rs, 0) * MMIHelpers.GetWU(cpu, args.Rt, 0);
            ulong a2 = (ulong)MMIHelpers.GetWU(cpu, args.Rs, 2) * MMIHelpers.GetWU(cpu, args.Rt, 2);
            ulong acc0 = ((cpu.HI & 0xFFFFFFFF) << 32) | (cpu.LO & 0xFFFFFFFF);
            ulong acc2 = ((cpu.HI1 & 0xFFFFFFFF) << 32) | (cpu.LO1 & 0xFFFFFFFF);
            acc0 += a0; acc2 += a2;
            cpu.LO = acc0 & 0xFFFFFFFF; cpu.HI = acc0 >> 32;
            cpu.LO1 = acc2 & 0xFFFFFFFF; cpu.HI1 = acc2 >> 32;
            if (args.Rd != 0) { MMIHelpers.SetW(cpu, args.Rd, 0, (int)cpu.LO); MMIHelpers.SetW(cpu, args.Rd, 2, (int)cpu.LO1); }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x00 << 6 | 0x29);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PSRAVW - Parallel Shift Right Arithmetic Variable Word (funct=0x29, shamt=0x03)
    public sealed class PSRAVW : Instruction<Args_R>
    {
        public PSRAVW()
        {
            Opcode = 0x1C; Funct = 0x29; Name = "psravw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt", "rs"];
            Equation = "rd.W = rt.W >>> rs.W[4:0] (2x32-bit arithmetic)";
            Description = "Parallel Shift Right Arithmetic Variable Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int sa0 = MMIHelpers.GetW(cpu, args.Rs, 0) & 0x1F;
            int sa1 = MMIHelpers.GetW(cpu, args.Rs, 1) & 0x1F;
            MMIHelpers.SetW(cpu, args.Rd, 0, MMIHelpers.GetW(cpu, args.Rt, 0) >> sa0);
            MMIHelpers.SetW(cpu, args.Rd, 1, MMIHelpers.GetW(cpu, args.Rt, 1) >> sa1);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}, {CpuHelpers.RegName(args.Rs)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[2] << 21 | args[1] << 16 | args[0] << 11 | 0x03 << 6 | 0x29);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt)).Replace("rs", CpuHelpers.RegName(args.Rs));
    }

    // PINTEH - Parallel Interleave Even Halfwords (funct=0x29, shamt=0x0A)
    public sealed class PINTEH : Instruction<Args_R>
    {
        public PINTEH()
        {
            Opcode = 0x1C; Funct = 0x29; Name = "pinteh";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = interleave even halfwords of rs and rt";
            Description = "Parallel Interleave Even Halfwords";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Interleave even halfwords (indices 0, 2, 4, 6)
            MMIHelpers.SetH(cpu, args.Rd, 0, MMIHelpers.GetH(cpu, args.Rt, 0));
            MMIHelpers.SetH(cpu, args.Rd, 1, MMIHelpers.GetH(cpu, args.Rs, 0));
            MMIHelpers.SetH(cpu, args.Rd, 2, MMIHelpers.GetH(cpu, args.Rt, 2));
            MMIHelpers.SetH(cpu, args.Rd, 3, MMIHelpers.GetH(cpu, args.Rs, 2));
            MMIHelpers.SetH(cpu, args.Rd, 4, MMIHelpers.GetH(cpu, args.Rt, 4));
            MMIHelpers.SetH(cpu, args.Rd, 5, MMIHelpers.GetH(cpu, args.Rs, 4));
            MMIHelpers.SetH(cpu, args.Rd, 6, MMIHelpers.GetH(cpu, args.Rt, 6));
            MMIHelpers.SetH(cpu, args.Rd, 7, MMIHelpers.GetH(cpu, args.Rs, 6));
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x0A << 6 | 0x29);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PMULTUW - Parallel Multiply Unsigned Word (funct=0x29, shamt=0x0C)
    public sealed class PMULTUW : Instruction<Args_R>
    {
        public PMULTUW()
        {
            Opcode = 0x1C; Funct = 0x29; Name = "pmultuw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "LO=rs.W0*rt.W0 (unsigned); LO1=rs.W2*rt.W2";
            Description = "Parallel Multiply Unsigned Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong p0 = (ulong)MMIHelpers.GetWU(cpu, args.Rs, 0) * MMIHelpers.GetWU(cpu, args.Rt, 0);
            ulong p2 = (ulong)MMIHelpers.GetWU(cpu, args.Rs, 2) * MMIHelpers.GetWU(cpu, args.Rt, 2);
            cpu.LO = p0 & 0xFFFFFFFF; cpu.HI = p0 >> 32;
            cpu.LO1 = p2 & 0xFFFFFFFF; cpu.HI1 = p2 >> 32;
            if (args.Rd != 0) { MMIHelpers.SetW(cpu, args.Rd, 0, (int)cpu.LO); MMIHelpers.SetW(cpu, args.Rd, 2, (int)cpu.LO1); }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x0C << 6 | 0x29);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PDIVUW - Parallel Divide Unsigned Word (funct=0x29, shamt=0x0D)
    public sealed class PDIVUW : Instruction<Args_R>
    {
        public PDIVUW()
        {
            Opcode = 0x1C; Funct = 0x29; Name = "pdivuw";
            ArgType = InstrArgType.R; ArgNames = ["rs", "rt"];
            Equation = "LO=rs.W0/rt.W0 (unsigned); LO1=rs.W2/rt.W2";
            Description = "Parallel Divide Unsigned Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            uint d0 = MMIHelpers.GetWU(cpu, args.Rt, 0); uint d2 = MMIHelpers.GetWU(cpu, args.Rt, 2);
            if (d0 != 0) { cpu.LO = MMIHelpers.GetWU(cpu, args.Rs, 0) / d0; cpu.HI = MMIHelpers.GetWU(cpu, args.Rs, 0) % d0; }
            if (d2 != 0) { cpu.LO1 = MMIHelpers.GetWU(cpu, args.Rs, 2) / d2; cpu.HI1 = MMIHelpers.GetWU(cpu, args.Rs, 2) % d2; }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[0] << 21 | args[1] << 16 | 0x0D << 6 | 0x29);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PNOR - Parallel NOR 128-bit (funct=0x29, shamt=0x13)
    public sealed class PNOR : Instruction<Args_R>
    {
        public PNOR()
        {
            Opcode = 0x1C; Funct = 0x29; Name = "pnor";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs", "rt"];
            Equation = "rd = ~(rs | rt) (128-bit)";
            Description = "Parallel NOR: 128-bit bitwise NOR";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            if (args.Rd != 0) { cpu.RegsLo[args.Rd] = ~(cpu.RegsLo[args.Rs] | cpu.RegsLo[args.Rt]); cpu.RegsHi[args.Rd] = ~(cpu.RegsHi[args.Rs] | cpu.RegsHi[args.Rt]); }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x13 << 6 | 0x29);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
            if (cpu != null) { ulong lo = ~(cpu.RegsLo[args.Rs] | cpu.RegsLo[args.Rt]); ulong hi = ~(cpu.RegsHi[args.Rs] | cpu.RegsHi[args.Rt]); return $"{eq} => 0x{hi:X16}_{lo:X16}"; }
            return eq;
        }
    }

    // PEXCH - Exchange Halfwords within 64-bit chunks (funct=0x29, shamt=0x1A)
    public sealed class PEXCH : Instruction<Args_R>
    {
        public PEXCH()
        {
            Opcode = 0x1C; Funct = 0x29; Name = "pexch";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt"];
            Equation = "rd: swap H1<->H2 in each 64-bit half";
            Description = "Parallel Exchange Halfwords in 64-bit chunks";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Lower 64: swap H1 and H2
            short h0 = MMIHelpers.GetH(cpu, args.Rt, 0); short h1 = MMIHelpers.GetH(cpu, args.Rt, 1);
            short h2 = MMIHelpers.GetH(cpu, args.Rt, 2); short h3 = MMIHelpers.GetH(cpu, args.Rt, 3);
            MMIHelpers.SetH(cpu, args.Rd, 0, h0); MMIHelpers.SetH(cpu, args.Rd, 1, h2);
            MMIHelpers.SetH(cpu, args.Rd, 2, h1); MMIHelpers.SetH(cpu, args.Rd, 3, h3);
            // Upper 64: swap H5 and H6
            short h4 = MMIHelpers.GetH(cpu, args.Rt, 4); short h5 = MMIHelpers.GetH(cpu, args.Rt, 5);
            short h6 = MMIHelpers.GetH(cpu, args.Rt, 6); short h7 = MMIHelpers.GetH(cpu, args.Rt, 7);
            MMIHelpers.SetH(cpu, args.Rd, 4, h4); MMIHelpers.SetH(cpu, args.Rd, 5, h6);
            MMIHelpers.SetH(cpu, args.Rd, 6, h5); MMIHelpers.SetH(cpu, args.Rd, 7, h7);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 16 | args[0] << 11 | 0x1A << 6 | 0x29);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }

    // PEXCW - Exchange Words (funct=0x29, shamt=0x1E)
    public sealed class PEXCW : Instruction<Args_R>
    {
        public PEXCW()
        {
            Opcode = 0x1C; Funct = 0x29; Name = "pexcw";
            ArgType = InstrArgType.R; ArgNames = ["rd", "rt"];
            Equation = "rd.W0=rt.W0; rd.W1=rt.W2; rd.W2=rt.W1; rd.W3=rt.W3";
            Description = "Parallel Exchange Words - swaps W1 and W2";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int w0 = MMIHelpers.GetW(cpu, args.Rt, 0); int w1 = MMIHelpers.GetW(cpu, args.Rt, 1);
            int w2 = MMIHelpers.GetW(cpu, args.Rt, 2); int w3 = MMIHelpers.GetW(cpu, args.Rt, 3);
            MMIHelpers.SetW(cpu, args.Rd, 0, w0); MMIHelpers.SetW(cpu, args.Rd, 1, w2);
            MMIHelpers.SetW(cpu, args.Rd, 2, w1); MMIHelpers.SetW(cpu, args.Rd, 3, w3);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rt)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 16 | args[0] << 11 | 0x1E << 6 | 0x29);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rt", CpuHelpers.RegName(args.Rt));
    }
}
