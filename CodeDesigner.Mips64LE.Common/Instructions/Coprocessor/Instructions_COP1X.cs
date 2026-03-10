using CodeDesigner.Mips64LE.Common.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.Common.Cpu.Instructions;
using CodeDesigner.Mips64LE.Common.CPU;
using System;

/*
| Instruction | Description |
|-------------|-------------|
| LWXC1       | Load Word Indexed to FPR (FPR[fd] = Mem[GPR[base]+GPR[index]]) |
| LDXC1       | Load Doubleword Indexed to FPR |
| LUXC1       | Load Unaligned Word Indexed to FPR |
| SWXC1       | Store Word Indexed from FPR (Mem[GPR[base]+GPR[index]] = FPR[fs]) |
| SDXC1       | Store Doubleword Indexed from FPR |
| SUXC1       | Store Unaligned Word Indexed from FPR |
| PREFX       | Prefetch Indexed (hint, treated as NOP) |
| MADD.S      | Fused Multiply-Add Single: FPR[fd] = FPR[fr]*FPR[fs] + FPR[ft] |
| MADD.D      | Fused Multiply-Add Double (PS2: single-precision) |
| MSUB.S      | Fused Multiply-Subtract Single: FPR[fd] = FPR[fr]*FPR[fs] - FPR[ft] |
| MSUB.D      | Fused Multiply-Subtract Double (PS2: single-precision) |
| NMADD.S     | Negative Fused Multiply-Add Single: FPR[fd] = -(FPR[fr]*FPR[fs] + FPR[ft]) |
| NMADD.D     | Negative Fused Multiply-Add Double (PS2: single-precision) |
| NMSUB.S     | Negative Fused Multiply-Subtract Single: FPR[fd] = -(FPR[fr]*FPR[fs] - FPR[ft]) |
| NMSUB.D     | Negative Fused Multiply-Subtract Double (PS2: single-precision) |
*/

namespace CodeDesigner.Mips64LE.Common.Instructions.CoprocessorInstructions
{
    // COP1X instruction encoding (opcode = 0x13):
    //   bits[25:21] = base/fr  (Args_R.Rs)
    //   bits[20:16] = index/ft (Args_R.Rt)
    //   bits[15:11] = fd       (Args_R.Rd)
    //   bits[10:6]  = fs       (Args_R.Shamt)
    //   bits[5:0]   = funct    (instruction selector)

    // LWXC1 - Load Word Indexed to FPR (funct = 0x00)
    public sealed class LWXC1 : Instruction<Args_R>
    {
        public LWXC1()
        {
            Opcode = 0x13; Funct = 0x00; Name = "lwxc1";
            ArgType = InstrArgType.R; ArgNames = ["fd", "index", "base"];
            Equation = "FPR[fd] = Mem[base + index]";
            Description = "Load Word Indexed to FPR: loads 32-bit float from GPR[base]+GPR[index]";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong addr = cpu.RegsLo[args.Rs] + cpu.RegsLo[args.Rt];
            uint bits = Cop1XHelpers.ReadWord(cpu, addr);
            cpu.FPR[args.Rd] = BitConverter.UInt32BitsToSingle(bits);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} $f{args.Rd}, {CpuHelpers.RegName(args.Rt)}({CpuHelpers.RegName(args.Rs)})";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[2] << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x00);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("base", CpuHelpers.RegName(args.Rs)).Replace("index", CpuHelpers.RegName(args.Rt));
            if (cpu != null) { ulong addr = cpu.RegsLo[args.Rs] + cpu.RegsLo[args.Rt]; return $"{eq} => addr=0x{addr:X16}"; }
            return eq;
        }
    }

    // LDXC1 - Load Doubleword Indexed to FPR (funct = 0x01)
    public sealed class LDXC1 : Instruction<Args_R>
    {
        public LDXC1()
        {
            Opcode = 0x13; Funct = 0x01; Name = "ldxc1";
            ArgType = InstrArgType.R; ArgNames = ["fd", "index", "base"];
            Equation = "FPR[fd] = Mem64[base + index]";
            Description = "Load Doubleword Indexed to FPR: loads 64-bit value from GPR[base]+GPR[index]";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong addr = cpu.RegsLo[args.Rs] + cpu.RegsLo[args.Rt];
            uint lo = Cop1XHelpers.ReadWord(cpu, addr);
            uint hi = Cop1XHelpers.ReadWord(cpu, addr + 4);
            cpu.FPR[args.Rd] = BitConverter.UInt32BitsToSingle(lo);
            if (args.Rd + 1 < cpu.FPR.Length)
                cpu.FPR[args.Rd + 1] = BitConverter.UInt32BitsToSingle(hi);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} $f{args.Rd}, {CpuHelpers.RegName(args.Rt)}({CpuHelpers.RegName(args.Rs)})";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[2] << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x01);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("base", CpuHelpers.RegName(args.Rs)).Replace("index", CpuHelpers.RegName(args.Rt));
            if (cpu != null) { ulong addr = cpu.RegsLo[args.Rs] + cpu.RegsLo[args.Rt]; return $"{eq} => addr=0x{addr:X16}"; }
            return eq;
        }
    }

    // LUXC1 - Load Unaligned Word Indexed to FPR (funct = 0x05)
    public sealed class LUXC1 : Instruction<Args_R>
    {
        public LUXC1()
        {
            Opcode = 0x13; Funct = 0x05; Name = "luxc1";
            ArgType = InstrArgType.R; ArgNames = ["fd", "index", "base"];
            Equation = "FPR[fd] = Mem[base + index] (unaligned)";
            Description = "Load Unaligned Word Indexed to FPR: loads 32-bit float from aligned GPR[base]+GPR[index]";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Effective address is aligned to word boundary
            ulong addr = (cpu.RegsLo[args.Rs] + cpu.RegsLo[args.Rt]) & ~3UL;
            uint bits = Cop1XHelpers.ReadWord(cpu, addr);
            cpu.FPR[args.Rd] = BitConverter.UInt32BitsToSingle(bits);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} $f{args.Rd}, {CpuHelpers.RegName(args.Rt)}({CpuHelpers.RegName(args.Rs)})";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[2] << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x05);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("base", CpuHelpers.RegName(args.Rs)).Replace("index", CpuHelpers.RegName(args.Rt));
            if (cpu != null) { ulong addr = (cpu.RegsLo[args.Rs] + cpu.RegsLo[args.Rt]) & ~3UL; return $"{eq} => addr=0x{addr:X16}"; }
            return eq;
        }
    }

    // SWXC1 - Store Word Indexed from FPR (funct = 0x08)
    public sealed class SWXC1 : Instruction<Args_R>
    {
        public SWXC1()
        {
            Opcode = 0x13; Funct = 0x08; Name = "swxc1";
            ArgType = InstrArgType.R; ArgNames = ["fs", "index", "base"];
            Equation = "Mem[base + index] = FPR[fs]";
            Description = "Store Word Indexed from FPR: stores 32-bit float to GPR[base]+GPR[index]";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong addr = cpu.RegsLo[args.Rs] + cpu.RegsLo[args.Rt];
            uint bits = BitConverter.SingleToUInt32Bits(cpu.FPR[args.Shamt]);
            Cop1XHelpers.WriteWord(cpu, addr, bits);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} $f{args.Shamt}, {CpuHelpers.RegName(args.Rt)}({CpuHelpers.RegName(args.Rs)})";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[2] << 21 | args[1] << 16 | args[0] << 6 | Funct ?? 0x08);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fs", $"$f{args.Shamt}").Replace("base", CpuHelpers.RegName(args.Rs)).Replace("index", CpuHelpers.RegName(args.Rt));
            if (cpu != null) { ulong addr = cpu.RegsLo[args.Rs] + cpu.RegsLo[args.Rt]; return $"{eq} => addr=0x{addr:X16}, val={cpu.FPR[args.Shamt]}"; }
            return eq;
        }
    }

    // SDXC1 - Store Doubleword Indexed from FPR (funct = 0x09)
    public sealed class SDXC1 : Instruction<Args_R>
    {
        public SDXC1()
        {
            Opcode = 0x13; Funct = 0x09; Name = "sdxc1";
            ArgType = InstrArgType.R; ArgNames = ["fs", "index", "base"];
            Equation = "Mem64[base + index] = FPR[fs]";
            Description = "Store Doubleword Indexed from FPR: stores 64-bit value to GPR[base]+GPR[index]";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong addr = cpu.RegsLo[args.Rs] + cpu.RegsLo[args.Rt];
            uint lo = BitConverter.SingleToUInt32Bits(cpu.FPR[args.Shamt]);
            uint hi = args.Shamt + 1 < cpu.FPR.Length ? BitConverter.SingleToUInt32Bits(cpu.FPR[args.Shamt + 1]) : 0u;
            Cop1XHelpers.WriteWord(cpu, addr, lo);
            Cop1XHelpers.WriteWord(cpu, addr + 4, hi);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} $f{args.Shamt}, {CpuHelpers.RegName(args.Rt)}({CpuHelpers.RegName(args.Rs)})";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[2] << 21 | args[1] << 16 | args[0] << 6 | Funct ?? 0x09);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fs", $"$f{args.Shamt}").Replace("base", CpuHelpers.RegName(args.Rs)).Replace("index", CpuHelpers.RegName(args.Rt));
            if (cpu != null) { ulong addr = cpu.RegsLo[args.Rs] + cpu.RegsLo[args.Rt]; return $"{eq} => addr=0x{addr:X16}, val={cpu.FPR[args.Shamt]}"; }
            return eq;
        }
    }

    // SUXC1 - Store Unaligned Word Indexed from FPR (funct = 0x0D)
    public sealed class SUXC1 : Instruction<Args_R>
    {
        public SUXC1()
        {
            Opcode = 0x13; Funct = 0x0D; Name = "suxc1";
            ArgType = InstrArgType.R; ArgNames = ["fs", "index", "base"];
            Equation = "Mem[base + index] = FPR[fs] (unaligned)";
            Description = "Store Unaligned Word Indexed from FPR: stores to aligned GPR[base]+GPR[index]";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Effective address is aligned to word boundary
            ulong addr = (cpu.RegsLo[args.Rs] + cpu.RegsLo[args.Rt]) & ~3UL;
            uint bits = BitConverter.SingleToUInt32Bits(cpu.FPR[args.Shamt]);
            Cop1XHelpers.WriteWord(cpu, addr, bits);
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} $f{args.Shamt}, {CpuHelpers.RegName(args.Rt)}({CpuHelpers.RegName(args.Rs)})";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[2] << 21 | args[1] << 16 | args[0] << 6 | Funct ?? 0x0D);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fs", $"$f{args.Shamt}").Replace("base", CpuHelpers.RegName(args.Rs)).Replace("index", CpuHelpers.RegName(args.Rt));
            if (cpu != null) { ulong addr = (cpu.RegsLo[args.Rs] + cpu.RegsLo[args.Rt]) & ~3UL; return $"{eq} => addr=0x{addr:X16}, val={cpu.FPR[args.Shamt]}"; }
            return eq;
        }
    }

    // PREFX - Prefetch Indexed (funct = 0x0F) — hint instruction, treated as NOP
    public sealed class PREFX : Instruction<Args_R>
    {
        public PREFX()
        {
            Opcode = 0x13; Funct = 0x0F; Name = "prefx";
            ArgType = InstrArgType.R; ArgNames = ["hint", "index", "base"];
            Equation = "hint prefetch Mem[base + index]";
            Description = "Prefetch Indexed: memory prefetch hint, no-op in emulation";
        }

        public override void Exec(CpuState cpu, Args_R args) { /* prefetch hint — no-op */ }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {args.Rd}, {CpuHelpers.RegName(args.Rt)}({CpuHelpers.RegName(args.Rs)})";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[2] << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x0F);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("base", CpuHelpers.RegName(args.Rs)).Replace("index", CpuHelpers.RegName(args.Rt));
            if (cpu != null) { ulong addr = cpu.RegsLo[args.Rs] + cpu.RegsLo[args.Rt]; return $"{eq} => addr=0x{addr:X16} (hint only)"; }
            return eq;
        }
    }

    // MADD.S - Fused Multiply-Add Single (funct = 0x20)
    // COP1X encoding: fd = fr*fs + ft (no accumulator; different from COP1 MADD.S)
    //   fr = args.Rs, ft = args.Rt, fd = args.Rd, fs = args.Shamt
    public sealed class COP1X_MADD_S : Instruction<Args_R>
    {
        public COP1X_MADD_S()
        {
            Opcode = 0x13; Funct = 0x20; Name = "madd.s";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fr", "fs", "ft"];
            Equation = "fd = fr * fs + ft";
            Description = "Fused Multiply-Add Single: FPR[fd] = FPR[fr]*FPR[fs] + FPR[ft]";
        }

        public override void Exec(CpuState cpu, Args_R args)
            => cpu.FPR[args.Rd] = cpu.FPR[args.Rs] * cpu.FPR[args.Shamt] + cpu.FPR[args.Rt];

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} $f{args.Rd}, $f{args.Rs}, $f{args.Shamt}, $f{args.Rt}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[1] << 21 | args[3] << 16 | args[0] << 11 | args[2] << 6 | Funct ?? 0x20);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fr", $"$f{args.Rs}").Replace("fs", $"$f{args.Shamt}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) return $"{eq} => {cpu.FPR[args.Rs]}*{cpu.FPR[args.Shamt]}+{cpu.FPR[args.Rt]} = {cpu.FPR[args.Rs] * cpu.FPR[args.Shamt] + cpu.FPR[args.Rt]}";
            return eq;
        }
    }

    // MADD.D - Fused Multiply-Add Double (funct = 0x21) — PS2 EE uses single-precision
    public sealed class COP1X_MADD_D : Instruction<Args_R>
    {
        public COP1X_MADD_D()
        {
            Opcode = 0x13; Funct = 0x21; Name = "madd.d";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fr", "fs", "ft"];
            Equation = "fd = fr * fs + ft";
            Description = "Fused Multiply-Add Double: FPR[fd] = FPR[fr]*FPR[fs] + FPR[ft] (PS2: single-precision)";
        }

        public override void Exec(CpuState cpu, Args_R args)
            => cpu.FPR[args.Rd] = cpu.FPR[args.Rs] * cpu.FPR[args.Shamt] + cpu.FPR[args.Rt];

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} $f{args.Rd}, $f{args.Rs}, $f{args.Shamt}, $f{args.Rt}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[1] << 21 | args[3] << 16 | args[0] << 11 | args[2] << 6 | Funct ?? 0x21);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fr", $"$f{args.Rs}").Replace("fs", $"$f{args.Shamt}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) return $"{eq} => {cpu.FPR[args.Rs]}*{cpu.FPR[args.Shamt]}+{cpu.FPR[args.Rt]} = {cpu.FPR[args.Rs] * cpu.FPR[args.Shamt] + cpu.FPR[args.Rt]}";
            return eq;
        }
    }

    // MSUB.S - Fused Multiply-Subtract Single (funct = 0x28)
    // fd = fr*fs - ft
    public sealed class COP1X_MSUB_S : Instruction<Args_R>
    {
        public COP1X_MSUB_S()
        {
            Opcode = 0x13; Funct = 0x28; Name = "msub.s";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fr", "fs", "ft"];
            Equation = "fd = fr * fs - ft";
            Description = "Fused Multiply-Subtract Single: FPR[fd] = FPR[fr]*FPR[fs] - FPR[ft]";
        }

        public override void Exec(CpuState cpu, Args_R args)
            => cpu.FPR[args.Rd] = cpu.FPR[args.Rs] * cpu.FPR[args.Shamt] - cpu.FPR[args.Rt];

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} $f{args.Rd}, $f{args.Rs}, $f{args.Shamt}, $f{args.Rt}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[1] << 21 | args[3] << 16 | args[0] << 11 | args[2] << 6 | Funct ?? 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fr", $"$f{args.Rs}").Replace("fs", $"$f{args.Shamt}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) return $"{eq} => {cpu.FPR[args.Rs]}*{cpu.FPR[args.Shamt]}-{cpu.FPR[args.Rt]} = {cpu.FPR[args.Rs] * cpu.FPR[args.Shamt] - cpu.FPR[args.Rt]}";
            return eq;
        }
    }

    // MSUB.D - Fused Multiply-Subtract Double (funct = 0x29)
    public sealed class COP1X_MSUB_D : Instruction<Args_R>
    {
        public COP1X_MSUB_D()
        {
            Opcode = 0x13; Funct = 0x29; Name = "msub.d";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fr", "fs", "ft"];
            Equation = "fd = fr * fs - ft";
            Description = "Fused Multiply-Subtract Double: FPR[fd] = FPR[fr]*FPR[fs] - FPR[ft] (PS2: single-precision)";
        }

        public override void Exec(CpuState cpu, Args_R args)
            => cpu.FPR[args.Rd] = cpu.FPR[args.Rs] * cpu.FPR[args.Shamt] - cpu.FPR[args.Rt];

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} $f{args.Rd}, $f{args.Rs}, $f{args.Shamt}, $f{args.Rt}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[1] << 21 | args[3] << 16 | args[0] << 11 | args[2] << 6 | Funct ?? 0x29);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fr", $"$f{args.Rs}").Replace("fs", $"$f{args.Shamt}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) return $"{eq} => {cpu.FPR[args.Rs]}*{cpu.FPR[args.Shamt]}-{cpu.FPR[args.Rt]} = {cpu.FPR[args.Rs] * cpu.FPR[args.Shamt] - cpu.FPR[args.Rt]}";
            return eq;
        }
    }

    // NMADD.S - Negative Fused Multiply-Add Single (funct = 0x30)
    // fd = -(fr*fs + ft)
    public sealed class COP1X_NMADD_S : Instruction<Args_R>
    {
        public COP1X_NMADD_S()
        {
            Opcode = 0x13; Funct = 0x30; Name = "nmadd.s";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fr", "fs", "ft"];
            Equation = "fd = -(fr * fs + ft)";
            Description = "Negative Fused Multiply-Add Single: FPR[fd] = -(FPR[fr]*FPR[fs] + FPR[ft])";
        }

        public override void Exec(CpuState cpu, Args_R args)
            => cpu.FPR[args.Rd] = -(cpu.FPR[args.Rs] * cpu.FPR[args.Shamt] + cpu.FPR[args.Rt]);

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} $f{args.Rd}, $f{args.Rs}, $f{args.Shamt}, $f{args.Rt}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[1] << 21 | args[3] << 16 | args[0] << 11 | args[2] << 6 | Funct ?? 0x30);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fr", $"$f{args.Rs}").Replace("fs", $"$f{args.Shamt}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) return $"{eq} => -({cpu.FPR[args.Rs]}*{cpu.FPR[args.Shamt]}+{cpu.FPR[args.Rt]}) = {-(cpu.FPR[args.Rs] * cpu.FPR[args.Shamt] + cpu.FPR[args.Rt])}";
            return eq;
        }
    }

    // NMADD.D - Negative Fused Multiply-Add Double (funct = 0x31)
    public sealed class COP1X_NMADD_D : Instruction<Args_R>
    {
        public COP1X_NMADD_D()
        {
            Opcode = 0x13; Funct = 0x31; Name = "nmadd.d";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fr", "fs", "ft"];
            Equation = "fd = -(fr * fs + ft)";
            Description = "Negative Fused Multiply-Add Double: FPR[fd] = -(FPR[fr]*FPR[fs] + FPR[ft]) (PS2: single-precision)";
        }

        public override void Exec(CpuState cpu, Args_R args)
            => cpu.FPR[args.Rd] = -(cpu.FPR[args.Rs] * cpu.FPR[args.Shamt] + cpu.FPR[args.Rt]);

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} $f{args.Rd}, $f{args.Rs}, $f{args.Shamt}, $f{args.Rt}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[1] << 21 | args[3] << 16 | args[0] << 11 | args[2] << 6 | Funct ?? 0x31);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fr", $"$f{args.Rs}").Replace("fs", $"$f{args.Shamt}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) return $"{eq} => -({cpu.FPR[args.Rs]}*{cpu.FPR[args.Shamt]}+{cpu.FPR[args.Rt]}) = {-(cpu.FPR[args.Rs] * cpu.FPR[args.Shamt] + cpu.FPR[args.Rt])}";
            return eq;
        }
    }

    // NMSUB.S - Negative Fused Multiply-Subtract Single (funct = 0x38)
    // fd = -(fr*fs - ft)
    public sealed class COP1X_NMSUB_S : Instruction<Args_R>
    {
        public COP1X_NMSUB_S()
        {
            Opcode = 0x13; Funct = 0x38; Name = "nmsub.s";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fr", "fs", "ft"];
            Equation = "fd = -(fr * fs - ft)";
            Description = "Negative Fused Multiply-Subtract Single: FPR[fd] = -(FPR[fr]*FPR[fs] - FPR[ft])";
        }

        public override void Exec(CpuState cpu, Args_R args)
            => cpu.FPR[args.Rd] = -(cpu.FPR[args.Rs] * cpu.FPR[args.Shamt] - cpu.FPR[args.Rt]);

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} $f{args.Rd}, $f{args.Rs}, $f{args.Shamt}, $f{args.Rt}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[1] << 21 | args[3] << 16 | args[0] << 11 | args[2] << 6 | Funct ?? 0x38);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fr", $"$f{args.Rs}").Replace("fs", $"$f{args.Shamt}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) return $"{eq} => -({cpu.FPR[args.Rs]}*{cpu.FPR[args.Shamt]}-{cpu.FPR[args.Rt]}) = {-(cpu.FPR[args.Rs] * cpu.FPR[args.Shamt] - cpu.FPR[args.Rt])}";
            return eq;
        }
    }

    // NMSUB.D - Negative Fused Multiply-Subtract Double (funct = 0x39)
    public sealed class COP1X_NMSUB_D : Instruction<Args_R>
    {
        public COP1X_NMSUB_D()
        {
            Opcode = 0x13; Funct = 0x39; Name = "nmsub.d";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fr", "fs", "ft"];
            Equation = "fd = -(fr * fs - ft)";
            Description = "Negative Fused Multiply-Subtract Double: FPR[fd] = -(FPR[fr]*FPR[fs] - FPR[ft]) (PS2: single-precision)";
        }

        public override void Exec(CpuState cpu, Args_R args)
            => cpu.FPR[args.Rd] = -(cpu.FPR[args.Rs] * cpu.FPR[args.Shamt] - cpu.FPR[args.Rt]);

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} $f{args.Rd}, $f{args.Rs}, $f{args.Shamt}, $f{args.Rt}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | args[1] << 21 | args[3] << 16 | args[0] << 11 | args[2] << 6 | Funct ?? 0x39);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fr", $"$f{args.Rs}").Replace("fs", $"$f{args.Shamt}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) return $"{eq} => -({cpu.FPR[args.Rs]}*{cpu.FPR[args.Shamt]}-{cpu.FPR[args.Rt]}) = {-(cpu.FPR[args.Rs] * cpu.FPR[args.Shamt] - cpu.FPR[args.Rt])}";
            return eq;
        }
    }

    // Shared memory access helpers for indexed FPR load/store
    internal static class Cop1XHelpers
    {
        internal static uint ReadWord(CpuState cpu, ulong addr)
        {
            if (cpu.Bus != null) return cpu.Bus.ReadWord((uint)addr);
            if (cpu.IopBus != null) return cpu.IopBus.ReadWord((uint)addr);
            if (addr + 3 < (ulong)cpu.Memory.Length) return BitConverter.ToUInt32(cpu.Memory, (int)addr);
            return 0;
        }

        internal static void WriteWord(CpuState cpu, ulong addr, uint value)
        {
            if (cpu.Bus != null) { cpu.Bus.WriteWord((uint)addr, value); return; }
            if (cpu.IopBus != null) { cpu.IopBus.WriteWord((uint)addr, value); return; }
            if (addr + 3 < (ulong)cpu.Memory.Length)
            {
                byte[] bytes = BitConverter.GetBytes(value);
                Array.Copy(bytes, 0, cpu.Memory, (long)addr, 4);
            }
        }
    }
}
