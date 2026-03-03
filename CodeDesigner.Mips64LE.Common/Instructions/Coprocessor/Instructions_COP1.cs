using CodeDesigner.Mips64LE.Common.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.Common.Cpu.Instructions;
using CodeDesigner.Mips64LE.Common.CPU;
using CodeDesigner.Mips64LE.Common.CPU.COP1;
using System;

/*
| Instruction | Description |
|-------------|-------------|
| MFC1 | Move from COP1 (FPU) register to GPR |
| DMFC1 | Move from COP1 register to GPR (64-bit) |
| MTC1 | Move from GPR to COP1 (FPU) register |
| DMTC1 | Move from GPR to COP1 register (64-bit) |
| CFC1 | Move control from COP1 |
| CTC1 | Move control to COP1 |
| LWC1 | Load word to COP1 (FPU) register |
| LDC1 | Load doubleword to COP1 register |
| SWC1 | Store word from COP1 register |
| SDC1 | Store doubleword from COP1 register |
| ADD.S | Add single-precision floating point |
| SUB.S | Subtract single-precision floating point |
| MUL.S | Multiply single-precision floating point |
| DIV.S | Divide single-precision floating point |
| SQRT.S | Square root single-precision floating point |
| ABS.S | Absolute value single-precision |
| NEG.S | Negate single-precision |
| MOV.S | Move single-precision floating point |
| CVT.S.W | Convert word to single-precision |
| CVT.W.S | Convert single-precision to word |
| C.EQ.S | Compare equal single-precision |
| C.LT.S | Compare less than single-precision |
| C.LE.S | Compare less than or equal single-precision |
| BC1F | Branch on FPU false |
| BC1T | Branch on FPU true |
*/

namespace CodeDesigner.Mips64LE.Common.Instructions.CoprocessorInstructions
{
    // MFC1 - Move from COP1 register to GPR (32-bit)
    public sealed class MFC1 : Instruction<Args_R>
    {
        public MFC1()
        {
            Opcode = 0x11;
            Funct = 0x00;
            Name = "mfc1";
            ArgType = InstrArgType.R;
            ArgNames = ["rt", "fs"];
            Equation = "rt = FPR[fs]";
            Description = "Move from COP1 register to GPR - reads 32-bit float register as bits";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Get the float as bits
            uint bits = BitConverter.SingleToUInt32Bits(cpu.FPR[args.Rd]);
            
            // Sign-extend to 64-bit and store in GPR
            long signExtended = (int)bits;
            cpu.RegsLo[args.Rt] = (ulong)signExtended;
            cpu.RegsHi[args.Rt] = signExtended < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, $f{args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x00 << 21 | args[0] << 16 | args[1] << 11);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("fs", $"$f{args.Rd}");
            
            if (cpu != null)
            {
                float value = cpu.FPR[args.Rd];
                uint bits = BitConverter.SingleToUInt32Bits(value);
                return $"{eq} => FPR[{args.Rd}] = {value} (0x{bits:X8})";
            }
            return eq;
        }
    }

    // DMFC1 - Move from COP1 register to GPR (64-bit)
    public sealed class DMFC1 : Instruction<Args_R>
    {
        public DMFC1()
        {
            Opcode = 0x11;
            Funct = 0x00;
            Name = "dmfc1";
            ArgType = InstrArgType.R;
            ArgNames = ["rt", "fs"];
            Equation = "rt = FPR[fs] (64-bit)";
            Description = "Move from COP1 register to GPR (64-bit) - reads double-precision bits";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Treat two consecutive float registers as a double (fs and fs+1)
            // This is a simplified implementation; real FPU may handle differently
            uint bits = BitConverter.SingleToUInt32Bits(cpu.FPR[args.Rd]);
            cpu.RegsLo[args.Rt] = bits;
            cpu.RegsHi[args.Rt] = 0;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, $f{args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x01 << 21 | args[0] << 16 | args[1] << 11);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("fs", $"$f{args.Rd}");
            
            if (cpu != null)
            {
                uint bits = BitConverter.SingleToUInt32Bits(cpu.FPR[args.Rd]);
                return $"{eq} => FPR[{args.Rd}] bits = 0x{bits:X16}";
            }
            return eq;
        }
    }

    // MTC1 - Move from GPR to COP1 register (32-bit)
    public sealed class MTC1 : Instruction<Args_R>
    {
        public MTC1()
        {
            Opcode = 0x11;
            Funct = 0x00;
            Name = "mtc1";
            ArgType = InstrArgType.R;
            ArgNames = ["rt", "fs"];
            Equation = "FPR[fs] = rt[31:0]";
            Description = "Move from GPR to COP1 register - writes 32-bit value as float bits";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Write lower 32 bits of GPR to FPR as bits
            uint bits = (uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
            cpu.FPR[args.Rd] = BitConverter.UInt32BitsToSingle(bits);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, $f{args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x04 << 21 | args[0] << 16 | args[1] << 11);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("fs", $"$f{args.Rd}");
            
            if (cpu != null)
            {
                uint bits = (uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
                float value = BitConverter.UInt32BitsToSingle(bits);
                return $"{eq} => write 0x{bits:X8} ({value}) to FPR[{args.Rd}]";
            }
            return eq;
        }
    }

    // DMTC1 - Move from GPR to COP1 register (64-bit)
    public sealed class DMTC1 : Instruction<Args_R>
    {
        public DMTC1()
        {
            Opcode = 0x11;
            Funct = 0x00;
            Name = "dmtc1";
            ArgType = InstrArgType.R;
            ArgNames = ["rt", "fs"];
            Equation = "FPR[fs] = rt (64-bit)";
            Description = "Move from GPR to COP1 register (64-bit) - writes full 64-bit value";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Write 64-bit GPR value to FPR (simplified as single float from lower bits)
            uint bits = (uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
            cpu.FPR[args.Rd] = BitConverter.UInt32BitsToSingle(bits);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, $f{args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x05 << 21 | args[0] << 16 | args[1] << 11);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("fs", $"$f{args.Rd}");
            
            if (cpu != null)
            {
                ulong value = cpu.RegsLo[args.Rt];
                return $"{eq} => write 0x{value:X16} to FPR[{args.Rd}]";
            }
            return eq;
        }
    }

    // CFC1 - Move control from COP1
    public sealed class CFC1 : Instruction<Args_R>
    {
        public CFC1()
        {
            Opcode = 0x11;
            Funct = 0x00;
            Name = "cfc1";
            ArgType = InstrArgType.R;
            ArgNames = ["rt", "fs"];
            Equation = "rt = FCR[fs]";
            Description = "Move control from COP1 - reads FPU control/status register";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Read FPU control register using COP1State
            uint value = cpu.COP1.ReadControlRegister(args.Rd);
            
            // Sign-extend to 64-bit
            long signExtended = (int)value;
            cpu.RegsLo[args.Rt] = (ulong)signExtended;
            cpu.RegsHi[args.Rt] = signExtended < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, ${args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x02 << 21 | args[0] << 16 | args[1] << 11);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string regName = COP1State.GetControlRegisterName(args.Rd);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("fs", $"${args.Rd}");
            
            if (cpu != null)
            {
                uint value = cpu.COP1.ReadControlRegister(args.Rd);
                return $"{eq} => {regName} = 0x{value:X8}";
            }
            return $"{eq} => read {regName}";
        }
    }

    // CTC1 - Move control to COP1
    public sealed class CTC1 : Instruction<Args_R>
    {
        public CTC1()
        {
            Opcode = 0x11;
            Funct = 0x00;
            Name = "ctc1";
            ArgType = InstrArgType.R;
            ArgNames = ["rt", "fs"];
            Equation = "FCR[fs] = rt[31:0]";
            Description = "Move control to COP1 - writes to FPU control/status register";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Write to FPU control register using COP1State
            uint value = (uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
            cpu.COP1.WriteControlRegister(args.Rd, value);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, ${args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x06 << 21 | args[0] << 16 | args[1] << 11);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string regName = COP1State.GetControlRegisterName(args.Rd);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("fs", $"${args.Rd}");
            
            if (cpu != null)
            {
                uint value = (uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
                return $"{eq} => write 0x{value:X8} to {regName}";
            }
            return $"{eq} => write to {regName}";
        }
    }

    // LWC1 - Load word to COP1 register
    public sealed class LWC1 : Instruction<Args_I>
    {
        public LWC1()
        {
            Opcode = 0x31;
            Funct = null;
            Name = "lwc1";
            ArgType = InstrArgType.I;
            ArgNames = ["ft", "offset", "base"];
            Equation = "FPR[ft] = Memory[base + offset]";
            Description = "Load word to COP1 register - loads 32-bit float from memory";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            ulong addr = (ulong)((long)cpu.RegsLo[args.Rs] + offset);
            
            // Load 32-bit value from memory
            uint bits;
            if (cpu.Bus != null)
            {
                bits = cpu.Bus.ReadWord((uint)addr);
            }
            else if (cpu.IopBus != null)
            {
                bits = cpu.IopBus.ReadWord((uint)addr);
            }
            else if (addr + 3 < (ulong)cpu.Memory.Length)
            {
                bits = BitConverter.ToUInt32(cpu.Memory, (int)addr);
            }
            else return;
            cpu.FPR[args.Rt] = BitConverter.UInt32BitsToSingle(bits);
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} $f{args.Rt}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[2] << 21 | args[0] << 16 | args[1] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("ft", $"$f{args.Rt}")
                .Replace("base", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                ulong addr = (ulong)((long)cpu.RegsLo[args.Rs] + offset);
                return $"{eq} => load from addr 0x{addr:X16}";
            }
            return eq;
        }
    }

    // SWC1 - Store word from COP1 register
    public sealed class SWC1 : Instruction<Args_I>
    {
        public SWC1()
        {
            Opcode = 0x39;
            Funct = null;
            Name = "swc1";
            ArgType = InstrArgType.I;
            ArgNames = ["ft", "offset", "base"];
            Equation = "Memory[base + offset] = FPR[ft]";
            Description = "Store word from COP1 register - stores 32-bit float to memory";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            ulong addr = (ulong)((long)cpu.RegsLo[args.Rs] + offset);
            
            // Store 32-bit value to memory
            uint bits = BitConverter.SingleToUInt32Bits(cpu.FPR[args.Rt]);
            if (cpu.Bus != null)
            {
                cpu.Bus.WriteWord((uint)addr, bits);
            }
            else if (cpu.IopBus != null)
            {
                cpu.IopBus.WriteWord((uint)addr, bits);
            }
            else if (addr + 3 < (ulong)cpu.Memory.Length)
            {
                byte[] bytes = BitConverter.GetBytes(bits);
                Array.Copy(bytes, 0, cpu.Memory, (long)addr, 4);
            }
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} $f{args.Rt}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[2] << 21 | args[0] << 16 | args[1] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("ft", $"$f{args.Rt}")
                .Replace("base", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                ulong addr = (ulong)((long)cpu.RegsLo[args.Rs] + offset);
                float value = cpu.FPR[args.Rt];
                return $"{eq} => store {value} to addr 0x{addr:X16}";
            }
            return eq;
        }
    }

    // ADD.S - Add single-precision floating point
    public sealed class ADD_S : Instruction<Args_R>
    {
        public ADD_S()
        {
            Opcode = 0x11;
            Funct = 0x00;
            Name = "add.s";
            ArgType = InstrArgType.R;
            ArgNames = ["fd", "fs", "ft"];
            Equation = "fd = fs + ft";
            Description = "Add single-precision floating point";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // COP1 S-format: args.Rs=fmt(0x10), args.Rd=fs, args.Rt=ft, args.Shamt=fd
            float fs = cpu.FPR[args.Rd];
            float ft = cpu.FPR[args.Rt];
            if (float.IsSubnormal(fs)) fs = 0f;
            if (float.IsSubnormal(ft)) ft = 0f;
            float result = fs + ft;
            if (float.IsSubnormal(result)) result = 0f;
            cpu.FPR[args.Shamt] = Ps2Fpu.HandleResult(result, cpu.COP1);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} $f{args.Shamt}, $f{args.Rd}, $f{args.Rt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | Funct ?? 0x00);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("fd", $"$f{args.Shamt}")
                .Replace("fs", $"$f{args.Rd}")
                .Replace("ft", $"$f{args.Rt}");
            
            if (cpu != null)
            {
                float fs = cpu.FPR[args.Rd];
                float ft = cpu.FPR[args.Rt];
                return $"{eq} => {fs} + {ft} = {fs + ft}";
            }
            return eq;
        }
    }

    // SUB.S - Subtract single-precision floating point
    public sealed class SUB_S : Instruction<Args_R>
    {
        public SUB_S()
        {
            Opcode = 0x11;
            Funct = 0x01;
            Name = "sub.s";
            ArgType = InstrArgType.R;
            ArgNames = ["fd", "fs", "ft"];
            Equation = "fd = fs - ft";
            Description = "Subtract single-precision floating point";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // COP1 S-format: args.Rs=fmt(0x10), args.Rd=fs, args.Rt=ft, args.Shamt=fd
            float fs = cpu.FPR[args.Rd];
            float ft = cpu.FPR[args.Rt];
            if (float.IsSubnormal(fs)) fs = 0f;
            if (float.IsSubnormal(ft)) ft = 0f;
            float result = fs - ft;
            if (float.IsSubnormal(result)) result = 0f;
            cpu.FPR[args.Shamt] = Ps2Fpu.HandleResult(result, cpu.COP1);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} $f{args.Shamt}, $f{args.Rd}, $f{args.Rt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | Funct ?? 0x01);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("fd", $"$f{args.Shamt}")
                .Replace("fs", $"$f{args.Rd}")
                .Replace("ft", $"$f{args.Rt}");
            
            if (cpu != null)
            {
                float fs = cpu.FPR[args.Rd];
                float ft = cpu.FPR[args.Rt];
                return $"{eq} => {fs} - {ft} = {fs - ft}";
            }
            return eq;
        }
    }

    // MUL.S - Multiply single-precision floating point
    public sealed class MUL_S : Instruction<Args_R>
    {
        public MUL_S()
        {
            Opcode = 0x11;
            Funct = 0x02;
            Name = "mul.s";
            ArgType = InstrArgType.R;
            ArgNames = ["fd", "fs", "ft"];
            Equation = "fd = fs * ft";
            Description = "Multiply single-precision floating point";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // COP1 S-format: args.Rs=fmt(0x10), args.Rd=fs, args.Rt=ft, args.Shamt=fd
            float fs = cpu.FPR[args.Rd];
            float ft = cpu.FPR[args.Rt];
            if (float.IsSubnormal(fs)) fs = 0f;
            if (float.IsSubnormal(ft)) ft = 0f;
            float result = fs * ft;
            if (float.IsSubnormal(result)) result = 0f;
            cpu.FPR[args.Shamt] = Ps2Fpu.HandleResult(result, cpu.COP1);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} $f{args.Shamt}, $f{args.Rd}, $f{args.Rt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | Funct ?? 0x02);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("fd", $"$f{args.Shamt}")
                .Replace("fs", $"$f{args.Rd}")
                .Replace("ft", $"$f{args.Rt}");
            
            if (cpu != null)
            {
                float fs = cpu.FPR[args.Rd];
                float ft = cpu.FPR[args.Rt];
                return $"{eq} => {fs} * {ft} = {fs * ft}";
            }
            return eq;
        }
    }

    // DIV.S - Divide single-precision floating point
    public sealed class DIV_S : Instruction<Args_R>
    {
        public DIV_S()
        {
            Opcode = 0x11;
            Funct = 0x03;
            Name = "div.s";
            ArgType = InstrArgType.R;
            ArgNames = ["fd", "fs", "ft"];
            Equation = "fd = fs / ft";
            Description = "Divide single-precision floating point";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // COP1 S-format: args.Rs=fmt(0x10), args.Rd=fs, args.Rt=ft, args.Shamt=fd
            float fs = cpu.FPR[args.Rd];
            float ft = cpu.FPR[args.Rt];
            if (float.IsSubnormal(fs)) fs = 0f;
            if (float.IsSubnormal(ft)) ft = 0f;
            float result = fs / ft;
            if (float.IsSubnormal(result)) result = 0f;
            cpu.FPR[args.Shamt] = Ps2Fpu.HandleDivResult(fs, ft, result, cpu.COP1);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} $f{args.Shamt}, $f{args.Rd}, $f{args.Rt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | Funct ?? 0x03);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("fd", $"$f{args.Shamt}")
                .Replace("fs", $"$f{args.Rd}")
                .Replace("ft", $"$f{args.Rt}");
            
            if (cpu != null)
            {
                float fs = cpu.FPR[args.Rd];
                float ft = cpu.FPR[args.Rt];
                return $"{eq} => {fs} / {ft} = {fs / ft}";
            }
            return eq;
        }
    }

    // SQRT.S - Square root single-precision floating point
    public sealed class SQRT_S : Instruction<Args_R>
    {
        public SQRT_S()
        {
            Opcode = 0x11;
            Funct = 0x04;
            Name = "sqrt.s";
            ArgType = InstrArgType.R;
            ArgNames = ["fd", "fs"];
            Equation = "fd = sqrt(fs)";
            Description = "Square root single-precision floating point";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // COP1 S-format: args.Rs=fmt(0x10), args.Rd=fs, args.Shamt=fd
            float fs = cpu.FPR[args.Rd];
            if (float.IsSubnormal(fs)) fs = 0f;
            float result = MathF.Sqrt(fs);
            if (float.IsSubnormal(result)) result = 0f;
            cpu.FPR[args.Shamt] = Ps2Fpu.HandleSqrtResult(fs, result, cpu.COP1);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} $f{args.Shamt}, $f{args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 11 | args[0] << 6 | Funct ?? 0x04);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("fd", $"$f{args.Shamt}")
                .Replace("fs", $"$f{args.Rd}");
            
            if (cpu != null)
            {
                float fs = cpu.FPR[args.Rd];
                return $"{eq} => sqrt({fs}) = {MathF.Sqrt(fs)}";
            }
            return eq;
        }
    }

    // ABS.S - Absolute value single-precision
    public sealed class ABS_S : Instruction<Args_R>
    {
        public ABS_S()
        {
            Opcode = 0x11;
            Funct = 0x05;
            Name = "abs.s";
            ArgType = InstrArgType.R;
            ArgNames = ["fd", "fs"];
            Equation = "fd = abs(fs)";
            Description = "Absolute value single-precision floating point";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // COP1 S-format: args.Rs=fmt(0x10), args.Rd=fs, args.Shamt=fd
            float fs = cpu.FPR[args.Rd];
            if (float.IsSubnormal(fs)) fs = 0f;
            cpu.FPR[args.Shamt] = MathF.Abs(fs);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} $f{args.Shamt}, $f{args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 11 | args[0] << 6 | Funct ?? 0x05);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("fd", $"$f{args.Shamt}")
                .Replace("fs", $"$f{args.Rd}");
            
            if (cpu != null)
            {
                float fs = cpu.FPR[args.Rd];
                return $"{eq} => abs({fs}) = {MathF.Abs(fs)}";
            }
            return eq;
        }
    }

    // MOV.S - Move single-precision floating point
    public sealed class MOV_S : Instruction<Args_R>
    {
        public MOV_S()
        {
            Opcode = 0x11;
            Funct = 0x06;
            Name = "mov.s";
            ArgType = InstrArgType.R;
            ArgNames = ["fd", "fs"];
            Equation = "fd = fs";
            Description = "Move single-precision floating point";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // COP1 S-format: args.Rs=fmt(0x10), args.Rd=fs, args.Shamt=fd
            cpu.FPR[args.Shamt] = cpu.FPR[args.Rd];
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} $f{args.Shamt}, $f{args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 11 | args[0] << 6 | Funct ?? 0x06);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("fd", $"$f{args.Shamt}")
                .Replace("fs", $"$f{args.Rd}");
            
            if (cpu != null)
            {
                float fs = cpu.FPR[args.Rd];
                return $"{eq} => {fs}";
            }
            return eq;
        }
    }

    // NEG.S - Negate single-precision
    public sealed class NEG_S : Instruction<Args_R>
    {
        public NEG_S()
        {
            Opcode = 0x11;
            Funct = 0x07;
            Name = "neg.s";
            ArgType = InstrArgType.R;
            ArgNames = ["fd", "fs"];
            Equation = "fd = -fs";
            Description = "Negate single-precision floating point";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // COP1 S-format: args.Rs=fmt(0x10), args.Rd=fs, args.Shamt=fd
            float fs = cpu.FPR[args.Rd];
            if (float.IsSubnormal(fs)) fs = 0f;
            cpu.FPR[args.Shamt] = -fs;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} $f{args.Shamt}, $f{args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 11 | args[0] << 6 | Funct ?? 0x07);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("fd", $"$f{args.Shamt}")
                .Replace("fs", $"$f{args.Rd}");
            
            if (cpu != null)
            {
                float fs = cpu.FPR[args.Rd];
                return $"{eq} => -({fs}) = {-fs}";
            }
            return eq;
        }
    }

    // CVT.W.S - Convert single-precision to word
    public sealed class CVT_W_S : Instruction<Args_R>
    {
        public CVT_W_S()
        {
            Opcode = 0x11;
            Funct = 0x24;
            Name = "cvt.w.s";
            ArgType = InstrArgType.R;
            ArgNames = ["fd", "fs"];
            Equation = "fd = (int)fs";
            Description = "Convert single-precision to 32-bit signed integer";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // COP1 S-format: args.Rs=fmt(0x10), args.Rd=fs, args.Shamt=fd
            float fs = cpu.FPR[args.Rd];
            if (float.IsSubnormal(fs)) fs = 0f;
            int intValue = (int)fs;
            cpu.FPR[args.Shamt] = BitConverter.UInt32BitsToSingle((uint)intValue);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} $f{args.Shamt}, $f{args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 11 | args[0] << 6 | Funct ?? 0x24);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("fd", $"$f{args.Shamt}")
                .Replace("fs", $"$f{args.Rd}");
            
            if (cpu != null)
            {
                float fs = cpu.FPR[args.Rd];
                return $"{eq} => convert {fs} to {(int)fs}";
            }
            return eq;
        }
    }

    // CVT.S.W - Convert word to single-precision
    public sealed class CVT_S_W : Instruction<Args_R>
    {
        public CVT_S_W()
        {
            Opcode = 0x11;
            Funct = 0x20;
            Name = "cvt.s.w";
            ArgType = InstrArgType.R;
            ArgNames = ["fd", "fs"];
            Equation = "fd = (float)fs";
            Description = "Convert 32-bit signed integer to single-precision";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // COP1 W-format (fmt=0x14): args.Rd=fs, args.Shamt=fd
            int intValue = (int)BitConverter.SingleToUInt32Bits(cpu.FPR[args.Rd]);
            float result = (float)intValue;
            if (float.IsSubnormal(result)) result = 0f;
            cpu.FPR[args.Shamt] = result;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} $f{args.Shamt}, $f{args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x14 << 21 | args[1] << 11 | args[0] << 6 | Funct ?? 0x20);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("fd", $"$f{args.Shamt}")
                .Replace("fs", $"$f{args.Rd}");
            
            if (cpu != null)
            {
                int intValue = (int)BitConverter.SingleToUInt32Bits(cpu.FPR[args.Rd]);
                return $"{eq} => convert {intValue} to {(float)intValue}";
            }
            return eq;
        }
    }

    // C.F.S - Compare False (always sets CC=false)
    public sealed class C_F : Instruction<Args_R>
    {
        public C_F()
        {
            Opcode = 0x11;
            Funct = 0x30;
            Name = "c.f.s";
            ArgType = InstrArgType.R;
            ArgNames = ["fs", "ft"];
            Equation = "CC = false";
            Description = "Compare false single-precision - always clears condition code";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            cpu.COP1.SetConditionCode(false);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} $f{args.Rd}, $f{args.Rt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x30);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return $"c.f.s $f{args.Rd}, $f{args.Rt} => CC = false";
        }
    }

    // C.EQ.S - Compare equal single-precision
    public sealed class C_EQ_S : Instruction<Args_R>
    {
        public C_EQ_S()
        {
            Opcode = 0x11;
            Funct = 0x32;
            Name = "c.eq.s";
            ArgType = InstrArgType.R;
            ArgNames = ["fs", "ft"];
            Equation = "CC = (fs == ft)";
            Description = "Compare equal single-precision - sets condition code";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // COP1 S-format: args.Rd=fs, args.Rt=ft
            // PS2 EE FPU flushes denormals to zero before comparison.
            float fs = cpu.FPR[args.Rd]; if (float.IsSubnormal(fs)) fs = 0f;
            float ft = cpu.FPR[args.Rt]; if (float.IsSubnormal(ft)) ft = 0f;
            cpu.COP1.SetConditionCode(fs == ft);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} $f{args.Rd}, $f{args.Rt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x32);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("fs", $"$f{args.Rd}")
                .Replace("ft", $"$f{args.Rt}");
            
            if (cpu != null)
            {
                float fs = cpu.FPR[args.Rd];
                float ft = cpu.FPR[args.Rt];
                bool result = fs == ft;
                return $"{eq} => {fs} == {ft} = {result}";
            }
            return eq;
        }
    }

    // C.LT.S - Compare less than single-precision
    public sealed class C_LT_S : Instruction<Args_R>
    {
        public C_LT_S()
        {
            Opcode = 0x11;
            Funct = 0x34;
            Name = "c.lt.s";
            ArgType = InstrArgType.R;
            ArgNames = ["fs", "ft"];
            Equation = "CC = (fs < ft)";
            Description = "Compare less than single-precision - sets condition code";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // COP1 S-format: args.Rd=fs, args.Rt=ft
            // PS2 EE FPU flushes denormals to zero before comparison.
            float fs = cpu.FPR[args.Rd]; if (float.IsSubnormal(fs)) fs = 0f;
            float ft = cpu.FPR[args.Rt]; if (float.IsSubnormal(ft)) ft = 0f;
            cpu.COP1.SetConditionCode(fs < ft);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} $f{args.Rd}, $f{args.Rt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x34);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("fs", $"$f{args.Rd}")
                .Replace("ft", $"$f{args.Rt}");
            
            if (cpu != null)
            {
                float fs = cpu.FPR[args.Rd];
                float ft = cpu.FPR[args.Rt];
                bool result = fs < ft;
                return $"{eq} => {fs} < {ft} = {result}";
            }
            return eq;
        }
    }

    // C.LE.S - Compare less than or equal single-precision
    public sealed class C_LE_S : Instruction<Args_R>
    {
        public C_LE_S()
        {
            Opcode = 0x11;
            Funct = 0x36;
            Name = "c.le.s";
            ArgType = InstrArgType.R;
            ArgNames = ["fs", "ft"];
            Equation = "CC = (fs <= ft)";
            Description = "Compare less than or equal single-precision - sets condition code";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // COP1 S-format: args.Rd=fs, args.Rt=ft
            // PS2 EE FPU flushes denormals to zero before comparison.
            float fs = cpu.FPR[args.Rd]; if (float.IsSubnormal(fs)) fs = 0f;
            float ft = cpu.FPR[args.Rt]; if (float.IsSubnormal(ft)) ft = 0f;
            cpu.COP1.SetConditionCode(fs <= ft);
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} $f{args.Rd}, $f{args.Rt}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x36);
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("fs", $"$f{args.Rd}")
                .Replace("ft", $"$f{args.Rt}");
            
            if (cpu != null)
            {
                float fs = cpu.FPR[args.Rd];
                float ft = cpu.FPR[args.Rt];
                bool result = fs <= ft;
                return $"{eq} => {fs} <= {ft} = {result}";
            }
            return eq;
        }
    }

    // BC1F - Branch on FPU false
    public sealed class BC1F : Instruction<Args_I>
    {
        public BC1F()
        {
            Opcode = 0x11;
            Funct = null;
            Name = "bc1f";
            ArgType = InstrArgType.I;
            ArgNames = ["offset"];
            Equation = "if (!CC) PC = PC + 4 + (offset << 2)";
            Description = "Branch on FPU condition code false";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            if (!cpu.COP1.GetConditionCode())
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                cpu.PC = (ulong)((long)cpu.PC + 4 + offset);
            }
            else
            {
                cpu.PC += 4;
            }
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            if (pc.HasValue)
            {
                ulong target = (ulong)((long)pc.Value + 4 + offset);
                return $"{Name} 0x{target:X}";
            }
            return $"{Name} {offset}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x08 << 21 | (args[0] >> 2) & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            
            if (cpu != null && pc.HasValue)
            {
                bool cc = cpu.COP1.GetConditionCode();
                ulong target = (ulong)((long)pc.Value + 4 + offset);
                return $"{Equation} => CC={cc}, {(cc ? "no branch" : $"branch to 0x{target:X}")}";
            }
            return Equation;
        }
    }

    // BC1T - Branch on FPU true
    public sealed class BC1T : Instruction<Args_I>
    {
        public BC1T()
        {
            Opcode = 0x11;
            Funct = null;
            Name = "bc1t";
            ArgType = InstrArgType.I;
            ArgNames = ["offset"];
            Equation = "if (CC) PC = PC + 4 + (offset << 2)";
            Description = "Branch on FPU condition code true";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            if (cpu.COP1.GetConditionCode())
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                cpu.PC = (ulong)((long)cpu.PC + 4 + offset);
            }
            else
            {
                cpu.PC += 4;
            }
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            if (pc.HasValue)
            {
                ulong target = (ulong)((long)pc.Value + 4 + offset);
                return $"{Name} 0x{target:X}";
            }
            return $"{Name} {offset}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x08 << 21 | 0x01 << 16 | (args[0] >> 2) & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            
            if (cpu != null && pc.HasValue)
            {
                bool cc = cpu.COP1.GetConditionCode();
                ulong target = (ulong)((long)pc.Value + 4 + offset);
                return $"{Equation} => CC={cc}, {(cc ? $"branch to 0x{target:X}" : "no branch")}";
            }
            return Equation;
        }
    }

    // =========================================================================
    // PS2 EE COP1 extensions: accumulator ops, rounding, reciprocal sqrt,
    // max/min, load/store double, and likely branches
    // =========================================================================

    // RSQRT.S - Reciprocal Square Root (PS2 EE specific)
    public sealed class RSQRT_S : Instruction<Args_R>
    {
        public RSQRT_S()
        {
            Opcode = 0x11; Funct = 0x16; Name = "rsqrt.s";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fs", "ft"];
            Equation = "fd = fs / sqrt(|ft|)"; Description = "Reciprocal square root (PS2 EE): fd = fs / sqrt(|ft|)";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            float ft = MathF.Abs(cpu.FPR[args.Rt]);
            cpu.FPR[args.Rd] = ft != 0 ? cpu.FPR[args.Rs] / MathF.Sqrt(ft) : float.MaxValue;
        }

        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rd}, $f{args.Rs}, $f{args.Rt}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | Funct ?? 0x16);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fs", $"$f{args.Rs}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null)
            {
                float fs = cpu.FPR[args.Rs]; float ft = MathF.Abs(cpu.FPR[args.Rt]);
                return $"{eq} => {fs} / sqrt({ft}) = {(ft != 0 ? fs / MathF.Sqrt(ft) : float.MaxValue)}";
            }
            return eq;
        }
    }

    // ROUND.W.S - Round to Word
    public sealed class ROUND_W_S : Instruction<Args_R>
    {
        public ROUND_W_S()
        {
            Opcode = 0x11; Funct = 0x0C; Name = "round.w.s";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fs"];
            Equation = "fd = round(fs)"; Description = "Round single-precision to nearest integer word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // PS2 EE rounds using AwayFromZero (banker's rounding is not used on PS2)
            int r = (int)MathF.Round(cpu.FPR[args.Rs], MidpointRounding.AwayFromZero);
            cpu.FPR[args.Rd] = BitConverter.UInt32BitsToSingle((uint)r);
        }

        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rd}, $f{args.Rs}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 11 | args[0] << 6 | Funct ?? 0x0C);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fs", $"$f{args.Rs}");
            if (cpu != null) { float fs = cpu.FPR[args.Rs]; return $"{eq} => round({fs}) = {(int)MathF.Round(fs, MidpointRounding.AwayFromZero)}"; }
            return eq;
        }
    }

    // TRUNC.W.S - Truncate to Word
    public sealed class TRUNC_W_S : Instruction<Args_R>
    {
        public TRUNC_W_S()
        {
            Opcode = 0x11; Funct = 0x0D; Name = "trunc.w.s";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fs"];
            Equation = "fd = trunc(fs)"; Description = "Truncate single-precision to integer word (toward zero)";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int r = (int)MathF.Truncate(cpu.FPR[args.Rs]);
            cpu.FPR[args.Rd] = BitConverter.UInt32BitsToSingle((uint)r);
        }

        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rd}, $f{args.Rs}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 11 | args[0] << 6 | Funct ?? 0x0D);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fs", $"$f{args.Rs}");
            if (cpu != null) { float fs = cpu.FPR[args.Rs]; return $"{eq} => trunc({fs}) = {(int)MathF.Truncate(fs)}"; }
            return eq;
        }
    }

    // CEIL.W.S - Ceiling to Word
    public sealed class CEIL_W_S : Instruction<Args_R>
    {
        public CEIL_W_S()
        {
            Opcode = 0x11; Funct = 0x0E; Name = "ceil.w.s";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fs"];
            Equation = "fd = ceil(fs)"; Description = "Ceiling single-precision to integer word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int r = (int)MathF.Ceiling(cpu.FPR[args.Rs]);
            cpu.FPR[args.Rd] = BitConverter.UInt32BitsToSingle((uint)r);
        }

        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rd}, $f{args.Rs}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 11 | args[0] << 6 | Funct ?? 0x0E);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fs", $"$f{args.Rs}");
            if (cpu != null) { float fs = cpu.FPR[args.Rs]; return $"{eq} => ceil({fs}) = {(int)MathF.Ceiling(fs)}"; }
            return eq;
        }
    }

    // FLOOR.W.S - Floor to Word
    public sealed class FLOOR_W_S : Instruction<Args_R>
    {
        public FLOOR_W_S()
        {
            Opcode = 0x11; Funct = 0x0F; Name = "floor.w.s";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fs"];
            Equation = "fd = floor(fs)"; Description = "Floor single-precision to integer word";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            int r = (int)MathF.Floor(cpu.FPR[args.Rs]);
            cpu.FPR[args.Rd] = BitConverter.UInt32BitsToSingle((uint)r);
        }

        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rd}, $f{args.Rs}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 11 | args[0] << 6 | Funct ?? 0x0F);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fs", $"$f{args.Rs}");
            if (cpu != null) { float fs = cpu.FPR[args.Rs]; return $"{eq} => floor({fs}) = {(int)MathF.Floor(fs)}"; }
            return eq;
        }
    }

    // ADDA.S - Add to Accumulator (PS2 EE specific)
    public sealed class ADDA_S : Instruction<Args_R>
    {
        public ADDA_S()
        {
            Opcode = 0x11; Funct = 0x18; Name = "adda.s";
            ArgType = InstrArgType.R; ArgNames = ["fs", "ft"];
            Equation = "ACC = fs + ft"; Description = "Add single-precision to FPU accumulator (PS2 EE)";
        }

        public override void Exec(CpuState cpu, Args_R args) => cpu.FACC = cpu.FPR[args.Rs] + cpu.FPR[args.Rt];
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rs}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x18);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fs", $"$f{args.Rs}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { float fs = cpu.FPR[args.Rs]; float ft = cpu.FPR[args.Rt]; return $"{eq} => ACC = {fs} + {ft} = {fs + ft}"; }
            return eq;
        }
    }

    // SUBA.S - Subtract to Accumulator (PS2 EE specific)
    public sealed class SUBA_S : Instruction<Args_R>
    {
        public SUBA_S()
        {
            Opcode = 0x11; Funct = 0x19; Name = "suba.s";
            ArgType = InstrArgType.R; ArgNames = ["fs", "ft"];
            Equation = "ACC = fs - ft"; Description = "Subtract single-precision to FPU accumulator (PS2 EE)";
        }

        public override void Exec(CpuState cpu, Args_R args) => cpu.FACC = cpu.FPR[args.Rs] - cpu.FPR[args.Rt];
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rs}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x19);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fs", $"$f{args.Rs}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { float fs = cpu.FPR[args.Rs]; float ft = cpu.FPR[args.Rt]; return $"{eq} => ACC = {fs} - {ft} = {fs - ft}"; }
            return eq;
        }
    }

    // MULA.S - Multiply to Accumulator (PS2 EE specific)
    public sealed class MULA_S : Instruction<Args_R>
    {
        public MULA_S()
        {
            Opcode = 0x11; Funct = 0x1A; Name = "mula.s";
            ArgType = InstrArgType.R; ArgNames = ["fs", "ft"];
            Equation = "ACC = fs * ft"; Description = "Multiply single-precision to FPU accumulator (PS2 EE)";
        }

        public override void Exec(CpuState cpu, Args_R args) => cpu.FACC = cpu.FPR[args.Rs] * cpu.FPR[args.Rt];
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rs}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x1A);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fs", $"$f{args.Rs}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { float fs = cpu.FPR[args.Rs]; float ft = cpu.FPR[args.Rt]; return $"{eq} => ACC = {fs} * {ft} = {fs * ft}"; }
            return eq;
        }
    }

    // MADD.S - Multiply-Add using Accumulator (PS2 EE specific): fd = ACC + fs*ft
    public sealed class MADD_S : Instruction<Args_R>
    {
        public MADD_S()
        {
            Opcode = 0x11; Funct = 0x1C; Name = "madd.s";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fs", "ft"];
            Equation = "fd = ACC + fs * ft"; Description = "Multiply-Add: fd = ACC + fs*ft (PS2 EE FPU)";
        }

        public override void Exec(CpuState cpu, Args_R args)
            => cpu.FPR[args.Rd] = cpu.FACC + cpu.FPR[args.Rs] * cpu.FPR[args.Rt];

        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rd}, $f{args.Rs}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | Funct ?? 0x1C);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fs", $"$f{args.Rs}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { return $"{eq} => ACC({cpu.FACC}) + {cpu.FPR[args.Rs]} * {cpu.FPR[args.Rt]} = {cpu.FACC + cpu.FPR[args.Rs] * cpu.FPR[args.Rt]}"; }
            return eq;
        }
    }

    // MSUB.S - Multiply-Subtract using Accumulator (PS2 EE specific): fd = ACC - fs*ft
    public sealed class MSUB_S : Instruction<Args_R>
    {
        public MSUB_S()
        {
            Opcode = 0x11; Funct = 0x1D; Name = "msub.s";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fs", "ft"];
            Equation = "fd = ACC - fs * ft"; Description = "Multiply-Subtract: fd = ACC - fs*ft (PS2 EE FPU)";
        }

        public override void Exec(CpuState cpu, Args_R args)
            => cpu.FPR[args.Rd] = cpu.FACC - cpu.FPR[args.Rs] * cpu.FPR[args.Rt];

        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rd}, $f{args.Rs}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | Funct ?? 0x1D);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fs", $"$f{args.Rs}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { return $"{eq} => ACC({cpu.FACC}) - {cpu.FPR[args.Rs]} * {cpu.FPR[args.Rt]} = {cpu.FACC - cpu.FPR[args.Rs] * cpu.FPR[args.Rt]}"; }
            return eq;
        }
    }

    // MADDA.S - Multiply-Add to Accumulator (PS2 EE specific): ACC = ACC + fs*ft
    public sealed class MADDA_S : Instruction<Args_R>
    {
        public MADDA_S()
        {
            Opcode = 0x11; Funct = 0x1E; Name = "madda.s";
            ArgType = InstrArgType.R; ArgNames = ["fs", "ft"];
            Equation = "ACC = ACC + fs * ft"; Description = "Multiply-Add to Accumulator: ACC += fs*ft (PS2 EE FPU)";
        }

        public override void Exec(CpuState cpu, Args_R args) => cpu.FACC += cpu.FPR[args.Rs] * cpu.FPR[args.Rt];
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rs}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x1E);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fs", $"$f{args.Rs}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { return $"{eq} => ACC({cpu.FACC}) + {cpu.FPR[args.Rs]} * {cpu.FPR[args.Rt]} = {cpu.FACC + cpu.FPR[args.Rs] * cpu.FPR[args.Rt]}"; }
            return eq;
        }
    }

    // MSUBA.S - Multiply-Subtract from Accumulator (PS2 EE specific): ACC = ACC - fs*ft
    public sealed class MSUBA_S : Instruction<Args_R>
    {
        public MSUBA_S()
        {
            Opcode = 0x11; Funct = 0x1F; Name = "msuba.s";
            ArgType = InstrArgType.R; ArgNames = ["fs", "ft"];
            Equation = "ACC = ACC - fs * ft"; Description = "Multiply-Subtract from Accumulator: ACC -= fs*ft (PS2 EE FPU)";
        }

        public override void Exec(CpuState cpu, Args_R args) => cpu.FACC -= cpu.FPR[args.Rs] * cpu.FPR[args.Rt];
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rs}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x1F);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fs", $"$f{args.Rs}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { return $"{eq} => ACC({cpu.FACC}) - {cpu.FPR[args.Rs]} * {cpu.FPR[args.Rt]} = {cpu.FACC - cpu.FPR[args.Rs] * cpu.FPR[args.Rt]}"; }
            return eq;
        }
    }

    // MAX.S - Maximum single-precision (PS2 EE specific)
    public sealed class MAX_S : Instruction<Args_R>
    {
        public MAX_S()
        {
            Opcode = 0x11; Funct = 0x28; Name = "max.s";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fs", "ft"];
            Equation = "fd = max(fs, ft)"; Description = "Maximum single-precision floating point (PS2 EE)";
        }

        public override void Exec(CpuState cpu, Args_R args) => cpu.FPR[args.Rd] = MathF.Max(cpu.FPR[args.Rs], cpu.FPR[args.Rt]);
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rd}, $f{args.Rs}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | Funct ?? 0x28);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fs", $"$f{args.Rs}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { float fs = cpu.FPR[args.Rs]; float ft = cpu.FPR[args.Rt]; return $"{eq} => max({fs}, {ft}) = {MathF.Max(fs, ft)}"; }
            return eq;
        }
    }

    // MIN.S - Minimum single-precision (PS2 EE specific)
    public sealed class MIN_S : Instruction<Args_R>
    {
        public MIN_S()
        {
            Opcode = 0x11; Funct = 0x29; Name = "min.s";
            ArgType = InstrArgType.R; ArgNames = ["fd", "fs", "ft"];
            Equation = "fd = min(fs, ft)"; Description = "Minimum single-precision floating point (PS2 EE)";
        }

        public override void Exec(CpuState cpu, Args_R args) => cpu.FPR[args.Rd] = MathF.Min(cpu.FPR[args.Rs], cpu.FPR[args.Rt]);
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rd}, $f{args.Rs}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | Funct ?? 0x29);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Rd}").Replace("fs", $"$f{args.Rs}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { float fs = cpu.FPR[args.Rs]; float ft = cpu.FPR[args.Rt]; return $"{eq} => min({fs}, {ft}) = {MathF.Min(fs, ft)}"; }
            return eq;
        }
    }

    // LDC1 - Load Doubleword to COP1 register (opcode=0x35)
    public sealed class LDC1 : Instruction<Args_I>
    {
        public LDC1()
        {
            Opcode = 0x35; Funct = null; Name = "ldc1";
            ArgType = InstrArgType.I; ArgNames = ["ft", "offset", "base"];
            Equation = "FPR[ft] = Memory[base + offset] (64-bit)"; Description = "Load doubleword to COP1 register pair";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            ulong addr = (ulong)((long)cpu.RegsLo[args.Rs] + offset);
            if (cpu.Bus != null)
            {
                uint lo = cpu.Bus.ReadWord((uint)addr);
                uint hi = cpu.Bus.ReadWord((uint)addr + 4);
                cpu.FPR[args.Rt] = BitConverter.UInt32BitsToSingle(lo);
                if (args.Rt + 1 < cpu.FPR.Length) cpu.FPR[args.Rt + 1] = BitConverter.UInt32BitsToSingle(hi);
            }
            else if (cpu.IopBus != null)
            {
                uint lo = cpu.IopBus.ReadWord((uint)addr);
                uint hi = cpu.IopBus.ReadWord((uint)addr + 4);
                cpu.FPR[args.Rt] = BitConverter.UInt32BitsToSingle(lo);
                if (args.Rt + 1 < cpu.FPR.Length) cpu.FPR[args.Rt + 1] = BitConverter.UInt32BitsToSingle(hi);
            }
            else if (addr + 7 < (ulong)cpu.Memory.Length)
            {
                // Load 64-bit value; store lower 32 bits in FPR[ft], upper 32 in FPR[ft+1]
                uint lo = BitConverter.ToUInt32(cpu.Memory, (int)addr);
                uint hi = BitConverter.ToUInt32(cpu.Memory, (int)addr + 4);
                cpu.FPR[args.Rt] = BitConverter.UInt32BitsToSingle(lo);
                if (args.Rt + 1 < cpu.FPR.Length) cpu.FPR[args.Rt + 1] = BitConverter.UInt32BitsToSingle(hi);
            }
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} $f{args.Rt}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args) => (uint)(Opcode << 26 | args[2] << 21 | args[0] << 16 | args[1] & 0xFFFF);

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation.Replace("ft", $"$f{args.Rt}").Replace("base", CpuHelpers.RegName(args.Rs)).Replace("offset", offset.ToString());
            if (cpu != null) { ulong addr = (ulong)((long)cpu.RegsLo[args.Rs] + offset); return $"{eq} => addr=0x{addr:X16}"; }
            return eq;
        }
    }

    // SDC1 - Store Doubleword from COP1 register (opcode=0x3D)
    public sealed class SDC1 : Instruction<Args_I>
    {
        public SDC1()
        {
            Opcode = 0x3D; Funct = null; Name = "sdc1";
            ArgType = InstrArgType.I; ArgNames = ["ft", "offset", "base"];
            Equation = "Memory[base + offset] = FPR[ft] (64-bit)"; Description = "Store doubleword from COP1 register pair";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            ulong addr = (ulong)((long)cpu.RegsLo[args.Rs] + offset);
            uint lo = BitConverter.SingleToUInt32Bits(cpu.FPR[args.Rt]);
            uint hi = args.Rt + 1 < cpu.FPR.Length ? BitConverter.SingleToUInt32Bits(cpu.FPR[args.Rt + 1]) : 0;
            if (cpu.Bus != null)
            {
                cpu.Bus.WriteWord((uint)addr, lo);
                cpu.Bus.WriteWord((uint)addr + 4, hi);
            }
            else if (cpu.IopBus != null)
            {
                cpu.IopBus.WriteWord((uint)addr, lo);
                cpu.IopBus.WriteWord((uint)addr + 4, hi);
            }
            else if (addr + 7 < (ulong)cpu.Memory.Length)
            {
                byte[] buf = new byte[8];
                Buffer.BlockCopy(BitConverter.GetBytes(lo), 0, buf, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(hi), 0, buf, 4, 4);
                Buffer.BlockCopy(buf, 0, cpu.Memory, (int)addr, 8);
            }
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} $f{args.Rt}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args) => (uint)(Opcode << 26 | args[2] << 21 | args[0] << 16 | args[1] & 0xFFFF);

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation.Replace("ft", $"$f{args.Rt}").Replace("base", CpuHelpers.RegName(args.Rs)).Replace("offset", offset.ToString());
            if (cpu != null)
            {
                ulong addr = (ulong)((long)cpu.RegsLo[args.Rs] + offset);
                float val = cpu.FPR[args.Rt];
                return $"{eq} => store {val} to addr=0x{addr:X16}";
            }
            return eq;
        }
    }

    // BC1FL - Branch on FPU false (likely)
    public sealed class BC1FL : Instruction<Args_I>
    {
        public BC1FL()
        {
            Opcode = 0x11; Funct = null; Name = "bc1fl";
            ArgType = InstrArgType.I; ArgNames = ["offset"];
            Equation = "if (!CC) PC = PC + 4 + (offset << 2); else nullify"; Description = "Branch on FPU false (likely) - nullifies delay slot if not taken";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            if (!cpu.COP1.GetConditionCode())
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                cpu.PC = (ulong)((long)cpu.PC + 4 + offset);
            }
            // On PS2 hardware, the delay slot is nullified if not taken; emulated as NOP
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            if (pc.HasValue) { ulong target = (ulong)((long)pc.Value + 4 + offset); return $"{Name} 0x{target:X}"; }
            return $"{Name} {offset}";
        }

        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x08 << 21 | 0x02 << 16 | (args[0] >> 2) & 0xFFFF);

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            if (cpu != null && pc.HasValue)
            {
                bool cc = cpu.COP1.GetConditionCode();
                ulong target = (ulong)((long)pc.Value + 4 + offset);
                return $"{Equation} => CC={cc}, {(cc ? "no branch (delay nullified)" : $"branch to 0x{target:X}")}";
            }
            return Equation;
        }
    }

    // BC1TL - Branch on FPU true (likely)
    public sealed class BC1TL : Instruction<Args_I>
    {
        public BC1TL()
        {
            Opcode = 0x11; Funct = null; Name = "bc1tl";
            ArgType = InstrArgType.I; ArgNames = ["offset"];
            Equation = "if (CC) PC = PC + 4 + (offset << 2); else nullify"; Description = "Branch on FPU true (likely) - nullifies delay slot if not taken";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            if (cpu.COP1.GetConditionCode())
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                cpu.PC = (ulong)((long)cpu.PC + 4 + offset);
            }
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            if (pc.HasValue) { ulong target = (ulong)((long)pc.Value + 4 + offset); return $"{Name} 0x{target:X}"; }
            return $"{Name} {offset}";
        }

        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x08 << 21 | 0x03 << 16 | (args[0] >> 2) & 0xFFFF);

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            if (cpu != null && pc.HasValue)
            {
                bool cc = cpu.COP1.GetConditionCode();
                ulong target = (ulong)((long)pc.Value + 4 + offset);
                return $"{Equation} => CC={cc}, {(cc ? $"branch to 0x{target:X}" : "no branch (delay nullified)")}";
            }
            return Equation;
        }
    }

    /// <summary>
    /// Helper utilities for PS2 EE FPU arithmetic, matching PCSX2 FPU.cpp behaviour:
    /// overflow/div-by-zero results are clamped to ±MAX_FLOAT and the appropriate
    /// FCSR cause + sticky flag bits are set.
    /// Reference: PCSX2 pcsx2/FPU.cpp FPU_OVERFLOW / FPU_DIVZERO macros.
    /// </summary>
    internal static class Ps2Fpu
    {
        /// <summary>
        /// Clear Cause bits in FCSR, then check the result for overflow.
        /// If <paramref name="result"/> is ±infinity, clamp it and set
        /// Cause.Overflow + Flag.Overflow in FCSR.
        /// </summary>
        internal static float HandleResult(float result, COP1State fcsr)
        {
            fcsr.ClearCauseBits();
            if (float.IsInfinity(result))
            {
                fcsr.RaiseFpuFlag(FpuException.Overflow);
                return result > 0f ? float.MaxValue : -float.MaxValue;
            }
            return result;
        }

        /// <summary>
        /// Clear Cause bits, then check for division-by-zero or overflow.
        /// Handles the PS2 EE DIV.S behaviour: ft==0 with finite fs sets the
        /// DivisionByZero flag; ±infinity result sets the Overflow flag.
        /// </summary>
        internal static float HandleDivResult(float fs, float ft, float result, COP1State fcsr)
        {
            fcsr.ClearCauseBits();
            if (ft == 0f && !float.IsInfinity(fs) && !float.IsNaN(fs))
            {
                fcsr.RaiseFpuFlag(FpuException.DivisionByZero);
                return fs >= 0f ? float.MaxValue : -float.MaxValue;
            }
            if (float.IsInfinity(result))
            {
                fcsr.RaiseFpuFlag(FpuException.Overflow);
                return result > 0f ? float.MaxValue : -float.MaxValue;
            }
            return result;
        }

        /// <summary>
        /// Clear Cause bits, then check for invalid operation (NaN result, e.g.
        /// sqrt of negative).  PS2 EE does not produce NaN — the result is clamped
        /// to MaxPositive and InvalidOperation is flagged.
        /// </summary>
        internal static float HandleSqrtResult(float fs, float result, COP1State fcsr)
        {
            fcsr.ClearCauseBits();
            if (float.IsNaN(result))
            {
                fcsr.RaiseFpuFlag(FpuException.InvalidOperation);
                return float.MaxValue;
            }
            return result;
        }
    }
}
