using CodeDesigner.Ps2.Common.Cpu.InstrArgs;
using CodeDesigner.Ps2.Common.Cpu.Instructions;
using CodeDesigner.Ps2.Common.CPU;
using CodeDesigner.Ps2.Common.CPU.COP1;
using System;

/*
| Instruction | Description |
|-------------|-------------|
| MFC1 | Move from COP1 (FPU) register to GPR |
| MTC1 | Move from GPR to COP1 (FPU) register |
| CFC1 | Move control from COP1 |
| CTC1 | Move control to COP1 |
| LWC1 | Load word to COP1 (FPU) register |
| SWC1 | Store word from COP1 register |
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

namespace CodeDesigner.Ps2.Common.Instructions.CoprocessorInstructions
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
            if (cpu != null && CpuHelpers.IsRegisterKnown(cpu, args.Rt))
            {
                uint bits = (uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
                return $"$f{args.Rd} ← {CpuHelpers.RegName(args.Rt)} = {CpuHelpers.FormatFloat(bits)}";
            }
            return $"$f{args.Rd} ← {CpuHelpers.RegName(args.Rt)}";
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
            return $"{Name} $f{args.Rt}, {CpuHelpers.FormatOffset(offset)}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[2] << 21 | args[0] << 16 | args[1] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            if (cpu != null)
            {
                // Analyzed before the load runs: read the value through the bus.
                string address = CpuHelpers.FormatMemoryReference(cpu, args.Rs, offset);
                if (cpu.Bus != null && CpuHelpers.TryGetEffectiveAddress(cpu, args.Rs, offset, out uint addr))
                {
                    try
                    {
                        uint bits = cpu.Bus.ReadWord(addr);
                        return $"$f{args.Rt} ← [{address}] = {CpuHelpers.FormatFloat(bits)}";
                    }
                    catch { /* unreadable — show the reference only */ }
                }
                return $"$f{args.Rt} ← [{address}]";
            }
            return $"$f{args.Rt} ← [{CpuHelpers.FormatMemoryOffset(offset)}]";
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
            return $"{Name} $f{args.Rt}, {CpuHelpers.FormatOffset(offset)}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[2] << 21 | args[0] << 16 | args[1] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            if (cpu != null)
            {
                string address = CpuHelpers.FormatMemoryReference(cpu, args.Rs, offset);
                float value = cpu.FPR[args.Rt];
                uint bits = BitConverter.SingleToUInt32Bits(value);
                return $"[{address}] ← $f{args.Rt} = 0x{bits:X8} ({value})";
            }
            return $"[{CpuHelpers.FormatMemoryOffset(offset)}] ← $f{args.Rt}";
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
                return $"{Name} ${(uint)target:X8}";
            }
            return $"{Name} {CpuHelpers.FormatSignedImmediate(args.Imm)}";
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
                return $"{Name} ${(uint)target:X8}";
            }
            return $"{Name} {CpuHelpers.FormatSignedImmediate(args.Imm)}";
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
            cpu.FPR[args.Shamt] = ft != 0 ? cpu.FPR[args.Rd] / MathF.Sqrt(ft) : float.MaxValue;
        }

        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Shamt}, $f{args.Rd}, $f{args.Rt}";

        public override uint Asm(int[] args)
            => (uint)(Opcode << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | Funct ?? 0x16);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Shamt}").Replace("fs", $"$f{args.Rd}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null)
            {
                float fs = cpu.FPR[args.Rd]; float ft = MathF.Abs(cpu.FPR[args.Rt]);
                return $"{eq} => {fs} / sqrt({ft}) = {(ft != 0 ? fs / MathF.Sqrt(ft) : float.MaxValue)}";
            }
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

        public override void Exec(CpuState cpu, Args_R args) => cpu.FACC = cpu.FPR[args.Rd] + cpu.FPR[args.Rt];
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rd}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x18);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fs", $"$f{args.Rd}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { float fs = cpu.FPR[args.Rd]; float ft = cpu.FPR[args.Rt]; return $"{eq} => ACC = {fs} + {ft} = {fs + ft}"; }
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

        public override void Exec(CpuState cpu, Args_R args) => cpu.FACC = cpu.FPR[args.Rd] - cpu.FPR[args.Rt];
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rd}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x19);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fs", $"$f{args.Rd}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { float fs = cpu.FPR[args.Rd]; float ft = cpu.FPR[args.Rt]; return $"{eq} => ACC = {fs} - {ft} = {fs - ft}"; }
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

        public override void Exec(CpuState cpu, Args_R args) => cpu.FACC = cpu.FPR[args.Rd] * cpu.FPR[args.Rt];
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rd}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x1A);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fs", $"$f{args.Rd}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { float fs = cpu.FPR[args.Rd]; float ft = cpu.FPR[args.Rt]; return $"{eq} => ACC = {fs} * {ft} = {fs * ft}"; }
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
            => cpu.FPR[args.Shamt] = cpu.FACC + cpu.FPR[args.Rd] * cpu.FPR[args.Rt];

        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Shamt}, $f{args.Rd}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | Funct ?? 0x1C);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Shamt}").Replace("fs", $"$f{args.Rd}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { return $"{eq} => ACC({cpu.FACC}) + {cpu.FPR[args.Rd]} * {cpu.FPR[args.Rt]} = {cpu.FACC + cpu.FPR[args.Rd] * cpu.FPR[args.Rt]}"; }
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
            => cpu.FPR[args.Shamt] = cpu.FACC - cpu.FPR[args.Rd] * cpu.FPR[args.Rt];

        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Shamt}, $f{args.Rd}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | Funct ?? 0x1D);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Shamt}").Replace("fs", $"$f{args.Rd}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { return $"{eq} => ACC({cpu.FACC}) - {cpu.FPR[args.Rd]} * {cpu.FPR[args.Rt]} = {cpu.FACC - cpu.FPR[args.Rd] * cpu.FPR[args.Rt]}"; }
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

        public override void Exec(CpuState cpu, Args_R args) => cpu.FACC += cpu.FPR[args.Rd] * cpu.FPR[args.Rt];
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rd}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x1E);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fs", $"$f{args.Rd}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { return $"{eq} => ACC({cpu.FACC}) + {cpu.FPR[args.Rd]} * {cpu.FPR[args.Rt]} = {cpu.FACC + cpu.FPR[args.Rd] * cpu.FPR[args.Rt]}"; }
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

        public override void Exec(CpuState cpu, Args_R args) => cpu.FACC -= cpu.FPR[args.Rd] * cpu.FPR[args.Rt];
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Rd}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[1] << 16 | args[0] << 11 | Funct ?? 0x1F);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fs", $"$f{args.Rd}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { return $"{eq} => ACC({cpu.FACC}) - {cpu.FPR[args.Rd]} * {cpu.FPR[args.Rt]} = {cpu.FACC - cpu.FPR[args.Rd] * cpu.FPR[args.Rt]}"; }
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

        public override void Exec(CpuState cpu, Args_R args) => cpu.FPR[args.Shamt] = MathF.Max(cpu.FPR[args.Rd], cpu.FPR[args.Rt]);
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Shamt}, $f{args.Rd}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | Funct ?? 0x28);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Shamt}").Replace("fs", $"$f{args.Rd}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { float fs = cpu.FPR[args.Rd]; float ft = cpu.FPR[args.Rt]; return $"{eq} => max({fs}, {ft}) = {MathF.Max(fs, ft)}"; }
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

        public override void Exec(CpuState cpu, Args_R args) => cpu.FPR[args.Shamt] = MathF.Min(cpu.FPR[args.Rd], cpu.FPR[args.Rt]);
        public override string Disasm(Args_R args, uint? pc = null) => $"{Name} $f{args.Shamt}, $f{args.Rd}, $f{args.Rt}";
        public override uint Asm(int[] args) => (uint)(Opcode << 26 | 0x10 << 21 | args[2] << 16 | args[1] << 11 | args[0] << 6 | Funct ?? 0x29);
        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("fd", $"$f{args.Shamt}").Replace("fs", $"$f{args.Rd}").Replace("ft", $"$f{args.Rt}");
            if (cpu != null) { float fs = cpu.FPR[args.Rd]; float ft = cpu.FPR[args.Rt]; return $"{eq} => min({fs}, {ft}) = {MathF.Min(fs, ft)}"; }
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
            if (pc.HasValue) { ulong target = (ulong)((long)pc.Value + 4 + offset); return $"{Name} ${(uint)target:X8}"; }
            return $"{Name} {CpuHelpers.FormatSignedImmediate(args.Imm)}";
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
            if (pc.HasValue) { ulong target = (ulong)((long)pc.Value + 4 + offset); return $"{Name} ${(uint)target:X8}"; }
            return $"{Name} {CpuHelpers.FormatSignedImmediate(args.Imm)}";
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
