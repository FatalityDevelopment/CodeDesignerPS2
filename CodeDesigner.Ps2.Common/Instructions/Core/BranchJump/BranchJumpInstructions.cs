using CodeDesigner.Ps2.Common.Cpu.InstrArgs;
using CodeDesigner.Ps2.Common.CPU;
using System;
using System.Collections.Generic;
using System.Text;

/*
| Instruction | Description |
|-------------|-------------|
| BEQ | Branch if equal |
| BNE | Branch if not equal |
| BLEZ | Branch if less than or equal to zero |
| BGTZ | Branch if greater than zero |
| BLTZ | Branch if less than zero |
| BGEZ | Branch if greater than or equal to zero |
| BEQL | Branch if equal (likely) |
| BNEL | Branch if not equal (likely) |
| J | Jump |
| JAL | Jump and link |
| JR | Jump register |
| JALR | Jump and link register |
*/
namespace CodeDesigner.Ps2.Common.Cpu.Instructions.EE
{
    // BEQ - Branch if equal
    public sealed class BEQ : Instruction<Args_I>
    {
        public BEQ()
        {
            Opcode = 0x04;
            Funct = null;
            Name = "beq";
            ArgType = InstrArgType.I;
            ArgNames = ["rs", "rt", "offset"];
            Equation = "if (rs == rt) PC = PC + 4 + (offset << 2)";
            Description = "Branch to target address if rs equals rt";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];

            if (rs == rt)
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
            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                int instrCount = offset / 4;
                string sign = instrCount >= 0 ? "+" : "";
                string arrow = instrCount >= 0 ? "▼" : "▲";
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}, ${target:X8} ({sign}{instrCount + 1}{arrow})";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (args[1] << 16) | (args[2] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return string.Empty;
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            uint target = (uint)((int)pc.Value + 4 + offset);
            if (cpu != null)
            {
                bool taken = cpu.RegsLo[args.Rs] == cpu.RegsLo[args.Rt];
                string loopHint = pc.HasValue && target < pc.Value ? " [loop]" : string.Empty;
                return $"→ 0x{target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]{loopHint}";
            }
            return $"→ 0x{target:X8}";
        }
    }

    // BNE - Branch if not equal
    public sealed class BNE : Instruction<Args_I>
    {
        public BNE()
        {
            Opcode = 0x05;
            Funct = null;
            Name = "bne";
            ArgType = InstrArgType.I;
            ArgNames = ["rs", "rt", "offset"];
            Equation = "if (rs != rt) PC = PC + 4 + (offset << 2)";
            Description = "Branch to target address if rs does not equal rt";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];

            if (rs != rt)
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
            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                int instrCount = offset / 4;
                string sign = instrCount >= 0 ? "+" : "";
                string arrow = instrCount >= 0 ? "▼" : "▲";
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}, ${target:X8} ({sign}{instrCount + 1}{arrow})";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (args[1] << 16) | (args[2] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return string.Empty;
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            uint target = (uint)((int)pc.Value + 4 + offset);
            if (cpu != null)
            {
                bool taken = cpu.RegsLo[args.Rs] != cpu.RegsLo[args.Rt];
                string loopHint = pc.HasValue && target < pc.Value ? " [loop]" : string.Empty;
                return $"→ 0x{target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]{loopHint}";
            }
            return $"→ 0x{target:X8}";
        }
    }

    // BLEZ - Branch if less than or equal to zero
    public sealed class BLEZ : Instruction<Args_I>
    {
        public BLEZ()
        {
            Opcode = 0x06;
            Funct = null;
            Name = "blez";
            ArgType = InstrArgType.I;
            ArgNames = ["rs", "offset"];
            Equation = "if (rs <= 0) PC = PC + 4 + (offset << 2)";
            Description = "Branch to target address if rs is less than or equal to zero";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];

            if (rs <= 0)
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
            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                int instrCount = offset / 4;
                string sign = instrCount >= 0 ? "+" : "";
                string arrow = instrCount >= 0 ? "▼" : "▲";
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, ${target:X8} ({sign}{instrCount + 1}{arrow})";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (args[2] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return string.Empty;
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            uint target = (uint)((int)pc.Value + 4 + offset);
            if (cpu != null)
            {
                bool taken = (long)cpu.RegsLo[args.Rs] <= 0;
                string loopHint = pc.HasValue && target < pc.Value ? " [loop]" : string.Empty;
                return $"→ 0x{target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]{loopHint}";
            }
            return $"→ 0x{target:X8}";
        }
    }

    // BGTZ - Branch if greater than zero
    public sealed class BGTZ : Instruction<Args_I>
    {
        public BGTZ()
        {
            Opcode = 0x07;
            Funct = null;
            Name = "bgtz";
            ArgType = InstrArgType.I;
            ArgNames = ["rs", "offset"];
            Equation = "if (rs > 0) PC = PC + 4 + (offset << 2)";
            Description = "Branch to target address if rs is greater than zero";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];

            if (rs > 0)
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
            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                int instrCount = offset / 4;
                string sign = instrCount >= 0 ? "+" : "";
                string arrow = instrCount >= 0 ? "▼" : "▲";
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, ${target:X8} ({sign}{instrCount + 1}{arrow})";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (args[2] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return string.Empty;
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            uint target = (uint)((int)pc.Value + 4 + offset);
            if (cpu != null)
            {
                bool taken = (long)cpu.RegsLo[args.Rs] > 0;
                string loopHint = pc.HasValue && target < pc.Value ? " [loop]" : string.Empty;
                return $"→ 0x{target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]{loopHint}";
            }
            return $"→ 0x{target:X8}";
        }
    }

    // BLTZ - Branch if less than zero
    public sealed class BLTZ : Instruction<Args_I>
    {
        public BLTZ()
        {
            Opcode = 0x01;
            Funct = null;
            Name = "bltz";
            ArgType = InstrArgType.I;
            ArgNames = ["rs", "offset"];
            Equation = "if (rs < 0) PC = PC + 4 + (offset << 2)";
            Description = "Branch to target address if rs is less than zero";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];

            if (rs < 0)
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
            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                int instrCount = offset / 4;
                string sign = instrCount >= 0 ? "+" : "";
                string arrow = instrCount >= 0 ? "▼" : "▲";
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, ${target:X8} ({sign}{instrCount + 1}{arrow})";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (0x00 << 16) | (args[2] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return string.Empty;
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            uint target = (uint)((int)pc.Value + 4 + offset);
            if (cpu != null)
            {
                bool taken = (long)cpu.RegsLo[args.Rs] < 0;
                string loopHint = pc.HasValue && target < pc.Value ? " [loop]" : string.Empty;
                return $"→ 0x{target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]{loopHint}";
            }
            return $"→ 0x{target:X8}";
        }
    }

    // BGEZ - Branch if greater than or equal to zero
    public sealed class BGEZ : Instruction<Args_I>
    {
        public BGEZ()
        {
            Opcode = 0x01;
            Funct = 0x01; // REGIMM sub-opcode: rt=1 distinguishes BGEZ from BLTZ (rt=0)
            Name = "bgez";
            ArgType = InstrArgType.I;
            ArgNames = ["rs", "offset"];
            Equation = "if (rs >= 0) PC = PC + 4 + (offset << 2)";
            Description = "Branch to target address if rs is greater than or equal to zero";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];

            if (rs >= 0)
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
            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                int instrCount = offset / 4;
                string sign = instrCount >= 0 ? "+" : "";
                string arrow = instrCount >= 0 ? "▼" : "▲";
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, ${target:X8} ({sign}{instrCount + 1}{arrow})";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (0x01 << 16) | (args[2] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return string.Empty;
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            uint target = (uint)((int)pc.Value + 4 + offset);
            if (cpu != null)
            {
                bool taken = (long)cpu.RegsLo[args.Rs] >= 0;
                string loopHint = pc.HasValue && target < pc.Value ? " [loop]" : string.Empty;
                return $"→ 0x{target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]{loopHint}";
            }
            return $"→ 0x{target:X8}";
        }
    }

    // BEQL - Branch if equal (likely)
    public sealed class BEQL : Instruction<Args_I>
    {
        public BEQL()
        {
            Opcode = 0x14;
            Funct = null;
            Name = "beql";
            ArgType = InstrArgType.I;
            ArgNames = ["rs", "rt", "offset"];
            Equation = "if (rs == rt) PC = PC + 4 + (offset << 2) else nullify delay slot";
            Description = "Branch if equal (likely) - nullifies delay slot if not taken";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];

            if (rs == rt)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                cpu.PC = (ulong)((long)cpu.PC + 4 + offset);
            }
            else
            {
                // Branch not taken - nullify delay slot
                cpu.PC += 8;
            }
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                int instrCount = offset / 4;
                string sign = instrCount >= 0 ? "+" : "";
                string arrow = instrCount >= 0 ? "▼" : "▲";
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}, ${target:X8} ({sign}{instrCount + 1}{arrow})";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (args[1] << 16) | (args[2] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return string.Empty;
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            uint target = (uint)((int)pc.Value + 4 + offset);
            if (cpu != null)
            {
                bool taken = cpu.RegsLo[args.Rs] == cpu.RegsLo[args.Rt];
                string loopHint = pc.HasValue && target < pc.Value ? " [loop]" : string.Empty;
                return $"→ 0x{target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]{loopHint}";
            }
            return $"→ 0x{target:X8}";
        }
    }

    // BNEL - Branch if not equal (likely)
    public sealed class BNEL : Instruction<Args_I>
    {
        public BNEL()
        {
            Opcode = 0x15;
            Funct = null;
            Name = "bnel";
            ArgType = InstrArgType.I;
            ArgNames = ["rs", "rt", "offset"];
            Equation = "if (rs != rt) PC = PC + 4 + (offset << 2) else nullify delay slot";
            Description = "Branch if not equal (likely) - nullifies delay slot if not taken";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            ulong rs = cpu.RegsLo[args.Rs];
            ulong rt = cpu.RegsLo[args.Rt];

            if (rs != rt)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                cpu.PC = (ulong)((long)cpu.PC + 4 + offset);
            }
            else
            {
                // Branch not taken - nullify delay slot
                cpu.PC += 8;
            }
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                int instrCount = offset / 4;
                string sign = instrCount >= 0 ? "+" : "";
                string arrow = instrCount >= 0 ? "▼" : "▲";
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}, ${target:X8} ({sign}{instrCount + 1}{arrow})";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (args[1] << 16) | (args[2] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return string.Empty;
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            uint target = (uint)((int)pc.Value + 4 + offset);
            if (cpu != null)
            {
                bool taken = cpu.RegsLo[args.Rs] != cpu.RegsLo[args.Rt];
                string loopHint = pc.HasValue && target < pc.Value ? " [loop]" : string.Empty;
                return $"→ 0x{target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]{loopHint}";
            }
            return $"→ 0x{target:X8}";
        }
    }

    // J - Jump
    public sealed class J : Instruction<Args_J>
    {
        public J()
        {
            Opcode = 0x02;
            Funct = null;
            Name = "j";
            ArgType = InstrArgType.J;
            ArgNames = ["target"];
            Equation = "PC = (PC & 0xF0000000) | (target << 2)";
            Description = "Unconditional jump to target address";
        }

        public override void Exec(CpuState cpu, Args_J args)
        {
            // Jump target address: upper 4 bits from PC+4, lower 28 bits from instruction
            ulong pcUpper = (cpu.PC + 4) & 0xFFFFFFFF_F0000000UL;
            ulong target = pcUpper | ((ulong)args.Addr << 2);
            cpu.PC = target;
        }

        public override string Disasm(Args_J args, uint? pc = null)
        {
            uint target = pc.HasValue ? CpuHelpers.JumpTarget(pc.Value, args.Addr) : args.Addr << 2;
            return $"{Name} ${target:X8}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] & 0x3FFFFFF));
        }

        public override string Analyze(Args_J args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return $"→ 0x{args.Addr << 2:X8}";
            uint target = CpuHelpers.JumpTarget(pc.Value, args.Addr);
            return $"→ 0x{target:X8}";
        }
    }

    // JAL - Jump and link
    public sealed class JAL : Instruction<Args_J>
    {
        public JAL()
        {
            Opcode = 0x03;
            Funct = null;
            Name = "jal";
            ArgType = InstrArgType.J;
            ArgNames = ["target"];
            Equation = "$ra = PC + 8; PC = (PC & 0xF0000000) | (target << 2)";
            Description = "Jump to target address and save return address in $ra";
        }

        public override void Exec(CpuState cpu, Args_J args)
        {
            // Save return address (PC+8) to $ra (register 31)
            cpu.RegsLo[31] = cpu.PC + 8;
            cpu.RegsHi[31] = 0;

            // Jump target address
            ulong pcUpper = (cpu.PC + 4) & 0xFFFFFFFF_F0000000UL;
            ulong target = pcUpper | ((ulong)args.Addr << 2);
            cpu.PC = target;
        }

        public override string Disasm(Args_J args, uint? pc = null)
        {
            uint target = pc.HasValue ? CpuHelpers.JumpTarget(pc.Value, args.Addr) : args.Addr << 2;
            return $"{Name} ${target:X8}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] & 0x3FFFFFF));
        }

        public override string Analyze(Args_J args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return $"→ 0x{args.Addr << 2:X8}";
            uint target = CpuHelpers.JumpTarget(pc.Value, args.Addr);
            if (cpu != null)
            {
                return CallAnalysisHelper.BuildCallComment(cpu, $"→ 0x{target:X8}");
            }
            return $"→ 0x{target:X8}";
        }
    }

    // JR - Jump register
    public sealed class JR : Instruction<Args_R>
    {
        public JR()
        {
            Opcode = 0x00;
            Funct = 0x08;
            Name = "jr";
            ArgType = InstrArgType.R;
            ArgNames = ["rs"];
            Equation = "PC = rs";
            Description = "Jump to address contained in register rs";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong target = cpu.RegsLo[args.Rs];
            cpu.PC = target;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rs)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | (Funct ?? 0x08));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu != null && CpuHelpers.IsRegisterKnown(cpu, args.Rs))
                return $"→ 0x{(uint)cpu.RegsLo[args.Rs]:X8}";
            return args.Rs == 31 ? "return" : $"→ {CpuHelpers.RegName(args.Rs)}";
        }
    }

    // JALR - Jump and link register
    public sealed class JALR : Instruction<Args_R>
    {
        public JALR()
        {
            Opcode = 0x00;
            Funct = 0x09;
            Name = "jalr";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs"];
            Equation = "rd = PC + 8; PC = rs";
            Description = "Jump to address in rs and save return address in rd";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong target = cpu.RegsLo[args.Rs];

            // Save return address to Rd (default $ra if Rd = 31)
            cpu.RegsLo[args.Rd] = cpu.PC + 8;
            cpu.RegsHi[args.Rd] = 0;

            cpu.PC = target;
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            if (args.Rd == 31)
            {
                return $"{Name} {CpuHelpers.RegName(args.Rs)}";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[0] << 21 | args[2] << 11 | (Funct ?? 0x09));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu != null)
            {
                string prefix = CpuHelpers.IsRegisterKnown(cpu, args.Rs)
                    ? $"→ 0x{(uint)cpu.RegsLo[args.Rs]:X8}"
                    : $"→ {CpuHelpers.RegName(args.Rs)}";
                return CallAnalysisHelper.BuildCallComment(cpu, prefix);
            }
            return $"→ {CpuHelpers.RegName(args.Rs)}";
        }
    }

    // BLTZAL - Branch if less than zero and link
    public sealed class BLTZAL : Instruction<Args_I>
    {
        public BLTZAL()
        {
            Opcode = 0x01;
            Funct = null;
            Name = "bltzal";
            ArgType = InstrArgType.I;
            ArgNames = ["rs", "offset"];
            Equation = "$ra = PC + 8; if (rs < 0) PC = PC + 4 + (offset << 2)";
            Description = "Branch to target address if rs is less than zero and link";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];
            
            // Save return address to $ra (register 31)
            cpu.RegsLo[31] = cpu.PC + 8;
            cpu.RegsHi[31] = 0;

            if (rs < 0)
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
            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                int instrCount = offset / 4;
                string sign = instrCount >= 0 ? "+" : "";
                string arrow = instrCount >= 0 ? "▼" : "▲";
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, ${target:X8} ({sign}{instrCount + 1}{arrow})";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (0x10 << 16) | (args[1] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return string.Empty;
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            uint target = (uint)((int)pc.Value + 4 + offset);
            if (cpu != null)
            {
                bool taken = (long)cpu.RegsLo[args.Rs] < 0;
                return $"→ 0x{target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]";
            }
            return $"→ 0x{target:X8}";
        }
    }

    // BGEZAL - Branch if greater than or equal to zero and link
    public sealed class BGEZAL : Instruction<Args_I>
    {
        public BGEZAL()
        {
            Opcode = 0x01;
            Funct = null;
            Name = "bgezal";
            ArgType = InstrArgType.I;
            ArgNames = ["rs", "offset"];
            Equation = "$ra = PC + 8; if (rs >= 0) PC = PC + 4 + (offset << 2)";
            Description = "Branch to target address if rs is greater than or equal to zero and link";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];
            
            // Save return address to $ra (register 31)
            cpu.RegsLo[31] = cpu.PC + 8;
            cpu.RegsHi[31] = 0;

            if (rs >= 0)
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
            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                int instrCount = offset / 4;
                string sign = instrCount >= 0 ? "+" : "";
                string arrow = instrCount >= 0 ? "▼" : "▲";
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, ${target:X8} ({sign}{instrCount + 1}{arrow})";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (0x11 << 16) | (args[1] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return string.Empty;
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            uint target = (uint)((int)pc.Value + 4 + offset);
            if (cpu != null)
            {
                bool taken = (long)cpu.RegsLo[args.Rs] >= 0;
                return $"→ 0x{target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]";
            }
            return $"→ 0x{target:X8}";
        }
    }

    // BLTZALL - Branch if less than zero and link (likely)
    public sealed class BLTZALL : Instruction<Args_I>
    {
        public BLTZALL()
        {
            Opcode = 0x01;
            Funct = null;
            Name = "bltzall";
            ArgType = InstrArgType.I;
            ArgNames = ["rs", "offset"];
            Equation = "$ra = PC + 8; if (rs < 0) PC = PC + 4 + (offset << 2)";
            Description = "Branch to target address if rs is less than zero and link (likely)";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];
            
            // Save return address to $ra (register 31)
            cpu.RegsLo[31] = cpu.PC + 8;
            cpu.RegsHi[31] = 0;

            if (rs < 0)
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
            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                int instrCount = offset / 4;
                string sign = instrCount >= 0 ? "+" : "";
                string arrow = instrCount >= 0 ? "▼" : "▲";
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, ${target:X8} ({sign}{instrCount + 1}{arrow})";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (0x12 << 16) | (args[1] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return string.Empty;
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            uint target = (uint)((int)pc.Value + 4 + offset);
            if (cpu != null)
            {
                bool taken = (long)cpu.RegsLo[args.Rs] < 0;
                return $"→ 0x{target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]";
            }
            return $"→ 0x{target:X8}";
        }
    }

    // BGEZALL - Branch if greater than or equal to zero and link (likely)
    public sealed class BGEZALL : Instruction<Args_I>
    {
        public BGEZALL()
        {
            Opcode = 0x01;
            Funct = null;
            Name = "bgezall";
            ArgType = InstrArgType.I;
            ArgNames = ["rs", "offset"];
            Equation = "$ra = PC + 8; if (rs >= 0) PC = PC + 4 + (offset << 2)";
            Description = "Branch to target address if rs is greater than or equal to zero and link (likely)";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            long rs = (long)cpu.RegsLo[args.Rs];
            
            // Save return address to $ra (register 31)
            cpu.RegsLo[31] = cpu.PC + 8;
            cpu.RegsHi[31] = 0;

            if (rs >= 0)
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
            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                int instrCount = offset / 4;
                string sign = instrCount >= 0 ? "+" : "";
                string arrow = instrCount >= 0 ? "▼" : "▲";
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, ${target:X8} ({sign}{instrCount + 1}{arrow})";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (0x13 << 16) | (args[1] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return string.Empty;
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            uint target = (uint)((int)pc.Value + 4 + offset);
            if (cpu != null)
            {
                bool taken = (long)cpu.RegsLo[args.Rs] >= 0;
                return $"→ 0x{target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]";
            }
            return $"→ 0x{target:X8}";
        }
    }

    // BLTZL - Branch if Less Than Zero Likely (REGIMM, rt=0x02)
    public sealed class BLTZL : Instruction<Args_I>
    {
        public BLTZL()
        {
            Opcode = 0x01;
            Funct = null;
            Name = "bltzl";
            ArgType = InstrArgType.I;
            ArgNames = ["rs", "offset"];
            Equation = "if (rs < 0) PC = PC + 4 + (offset << 2); else nullify";
            Description = "Branch if less than zero likely - nullifies delay slot if not taken";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            if ((long)cpu.RegsLo[args.Rs] < 0)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                cpu.PC = (ulong)((long)cpu.PC + 4 + offset);
            }
            else
            {
                cpu.PC += 8; // nullify delay slot
            }
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, ${target:X8}";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(0x01 << 26 | args[0] << 21 | 0x02 << 16 | args[1] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return string.Empty;
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            uint target = (uint)((int)pc.Value + 4 + offset);
            if (cpu != null)
            {
                bool taken = (long)cpu.RegsLo[args.Rs] < 0;
                string loopHint = pc.HasValue && target < pc.Value ? " [loop]" : string.Empty;
                return $"→ 0x{target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]{loopHint}";
            }
            return $"→ 0x{target:X8}";
        }
    }

    // BGEZL - Branch if Greater than or Equal Zero Likely (REGIMM, rt=0x03)
    public sealed class BGEZL : Instruction<Args_I>
    {
        public BGEZL()
        {
            Opcode = 0x01;
            Funct = null;
            Name = "bgezl";
            ArgType = InstrArgType.I;
            ArgNames = ["rs", "offset"];
            Equation = "if (rs >= 0) PC = PC + 4 + (offset << 2); else nullify";
            Description = "Branch if greater than or equal zero likely - nullifies delay slot if not taken";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            if ((long)cpu.RegsLo[args.Rs] >= 0)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                cpu.PC = (ulong)((long)cpu.PC + 4 + offset);
            }
            else
            {
                cpu.PC += 8; // nullify delay slot
            }
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, ${target:X8}";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(0x01 << 26 | args[0] << 21 | 0x03 << 16 | args[1] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return string.Empty;
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            uint target = (uint)((int)pc.Value + 4 + offset);
            if (cpu != null)
            {
                bool taken = (long)cpu.RegsLo[args.Rs] >= 0;
                string loopHint = pc.HasValue && target < pc.Value ? " [loop]" : string.Empty;
                return $"→ 0x{target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]{loopHint}";
            }
            return $"→ 0x{target:X8}";
        }
    }

    // BLEZL - Branch if Less than or Equal Zero Likely (opcode=0x16)
    public sealed class BLEZL : Instruction<Args_I>
    {
        public BLEZL()
        {
            Opcode = 0x16;
            Funct = null;
            Name = "blezl";
            ArgType = InstrArgType.I;
            ArgNames = ["rs", "offset"];
            Equation = "if (rs <= 0) PC = PC + 4 + (offset << 2); else nullify";
            Description = "Branch if less than or equal zero likely - nullifies delay slot if not taken";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            if ((long)cpu.RegsLo[args.Rs] <= 0)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                cpu.PC = (ulong)((long)cpu.PC + 4 + offset);
            }
            else
            {
                cpu.PC += 8; // nullify delay slot
            }
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, ${target:X8}";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(0x16 << 26 | args[0] << 21 | args[1] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return string.Empty;
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            uint target = (uint)((int)pc.Value + 4 + offset);
            if (cpu != null)
            {
                bool taken = (long)cpu.RegsLo[args.Rs] <= 0;
                string loopHint = pc.HasValue && target < pc.Value ? " [loop]" : string.Empty;
                return $"→ 0x{target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]{loopHint}";
            }
            return $"→ 0x{target:X8}";
        }
    }

    // BGTZL - Branch if Greater Than Zero Likely (opcode=0x17)
    public sealed class BGTZL : Instruction<Args_I>
    {
        public BGTZL()
        {
            Opcode = 0x17;
            Funct = null;
            Name = "bgtzl";
            ArgType = InstrArgType.I;
            ArgNames = ["rs", "offset"];
            Equation = "if (rs > 0) PC = PC + 4 + (offset << 2); else nullify";
            Description = "Branch if greater than zero likely - nullifies delay slot if not taken";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            if ((long)cpu.RegsLo[args.Rs] > 0)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                cpu.PC = (ulong)((long)cpu.PC + 4 + offset);
            }
            else
            {
                cpu.PC += 8; // nullify delay slot
            }
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, ${target:X8}";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(0x17 << 26 | args[0] << 21 | args[1] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            if (!pc.HasValue) return string.Empty;
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
            uint target = (uint)((int)pc.Value + 4 + offset);
            if (cpu != null)
            {
                bool taken = (long)cpu.RegsLo[args.Rs] > 0;
                string loopHint = pc.HasValue && target < pc.Value ? " [loop]" : string.Empty;
                return $"→ 0x{target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]{loopHint}";
            }
            return $"→ 0x{target:X8}";
        }
    }

    /// <summary>Helper for JAL/JALR call-site analysis comments.</summary>
    internal static class CallAnalysisHelper
    {
        /// <summary>
        /// Builds a call comment in the form
        /// <c>call &lt;prefix&gt; (a0 = 0x01FFE7F0 | a1 = 0x00000000 | …)</c>.
        /// Shows the known <c>$a0–$a3</c>; appends known, non-zero <c>$t0–$t7</c>
        /// registers. Unknown registers are left out rather than printed as 0.
        /// Every value is formatted as a 32-bit zero-padded hex word so it can be
        /// distinguished from arbitrary small constants in the comment text.
        /// </summary>
        internal static string BuildCallComment(CpuState cpu, string prefix)
        {
            var parts = new List<string>(12);
            for (int i = 4; i <= 7; i++)
            {
                if (CpuHelpers.IsRegisterKnown(cpu, i))
                    parts.Add($"{CpuHelpers.RegName(i)} = {CpuHelpers.FormatRegisterValueHex(cpu.RegsLo[i])}");
            }
            for (int i = 8; i <= 15; i++)
            {
                ulong val = cpu.RegsLo[i];
                if (val != 0 && CpuHelpers.IsRegisterKnown(cpu, i))
                    parts.Add($"{CpuHelpers.RegName(i)} = {CpuHelpers.FormatRegisterValueHex(val)}");
            }
            return parts.Count == 0 ? prefix : $"{prefix} ({string.Join(" | ", parts)})";
        }
    }
}