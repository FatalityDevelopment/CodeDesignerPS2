using CodeDesigner.Mips64LE.Common.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.Common.Cpu.Instructions;
using CodeDesigner.Mips64LE.Common.CPU;
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

namespace CodeDesigner.Mips64LE.Common.Cpu.Instructions.MMI
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
            string eq = Equation.Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
            if (cpu != null)
            {
                int rs = (int)(cpu.RegsLo[args.Rs] & 0xFFFFFFFF);
                int rt = (int)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
                return $"{eq} => {rs} * {rt} = {(long)rs * rt}";
            }
            return eq;
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
            string eq = Equation.Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
            if (cpu != null)
            {
                ulong rs = cpu.RegsLo[args.Rs] & 0xFFFFFFFF;
                ulong rt = cpu.RegsLo[args.Rt] & 0xFFFFFFFF;
                return $"{eq} => {rs} * {rt} = {rs * rt}";
            }
            return eq;
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
            string eq = Equation.Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
            return eq;
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
            string eq = Equation.Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs)).Replace("rt", CpuHelpers.RegName(args.Rt));
            return eq;
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
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs"];
            Equation = "rd = abs(rs) (4x32-bit)"; Description = "Parallel Absolute Word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 4; i++)
            {
                int v = MMIHelpers.GetW(cpu, args.Rs, i);
                MMIHelpers.SetW(cpu, args.Rd, i, v == int.MinValue ? int.MaxValue : Math.Abs(v));
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[0] << 11 | 0x01 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs));
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
            ArgType = InstrArgType.R; ArgNames = ["rd", "rs"];
            Equation = "rd = abs(rs) (8x16-bit)"; Description = "Parallel Absolute Halfword";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            for (int i = 0; i < 8; i++)
            {
                short v = MMIHelpers.GetH(cpu, args.Rs, i);
                MMIHelpers.SetH(cpu, args.Rd, i, v == short.MinValue ? short.MaxValue : (short)Math.Abs(v));
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}";

        public override uint Asm(int[] args)
            => (uint)(0x1C << 26 | args[1] << 21 | args[0] << 11 | 0x05 << 6 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
            => Equation.Replace("rd", CpuHelpers.RegName(args.Rd)).Replace("rs", CpuHelpers.RegName(args.Rs));
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
            Opcode = 0x1C; Funct = 0x29; Name = "pand";
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
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x12 << 6 | 0x29);

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
            => (uint)(0x1C << 26 | args[1] << 21 | args[2] << 16 | args[0] << 11 | 0x13 << 6 | 0x29);

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
}
