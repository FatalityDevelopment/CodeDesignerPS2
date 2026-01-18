using CodeDesigner.Mips64LE.Common.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.Common.CPU;
using System;

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
namespace CodeDesigner.Mips64LE.Common.Cpu.Instructions.EE
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
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}, ${target:X8}";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (args[1] << 16) | (args[2] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));

            if (cpu != null && pc.HasValue)
            {
                ulong rsVal = cpu.RegsLo[args.Rs];
                ulong rtVal = cpu.RegsLo[args.Rt];
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                bool taken = rsVal == rtVal;
                return $"{eq} => {CpuHelpers.RegName(args.Rs)}=0x{rsVal:X16}, {CpuHelpers.RegName(args.Rt)}=0x{rtVal:X16}, target=${target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]";
            }

            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                return $"{eq.Replace("PC + 4 + (offset << 2)", $"${target:X8}")}";
            }

            return eq;
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
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}, ${target:X8}";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (args[1] << 16) | (args[2] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));

            if (cpu != null && pc.HasValue)
            {
                ulong rsVal = cpu.RegsLo[args.Rs];
                ulong rtVal = cpu.RegsLo[args.Rt];
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                bool taken = rsVal != rtVal;
                return $"{eq} => {CpuHelpers.RegName(args.Rs)}=0x{rsVal:X16}, {CpuHelpers.RegName(args.Rt)}=0x{rtVal:X16}, target=${target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]";
            }

            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                return $"{eq.Replace("PC + 4 + (offset << 2)", $"${target:X8}")}";
            }

            return eq;
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
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, ${target:X8}";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (args[2] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rs", CpuHelpers.RegName(args.Rs));

            if (cpu != null && pc.HasValue)
            {
                long rsVal = (long)cpu.RegsLo[args.Rs];
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                bool taken = rsVal <= 0;
                return $"{eq} => {CpuHelpers.RegName(args.Rs)}={rsVal}, target=${target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]";
            }

            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                return $"{eq.Replace("PC + 4 + (offset << 2)", $"${target:X8}")}";
            }

            return eq;
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
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, ${target:X8}";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (args[2] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rs", CpuHelpers.RegName(args.Rs));

            if (cpu != null && pc.HasValue)
            {
                long rsVal = (long)cpu.RegsLo[args.Rs];
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                bool taken = rsVal > 0;
                return $"{eq} => {CpuHelpers.RegName(args.Rs)}={rsVal}, target=${target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]";
            }

            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                return $"{eq.Replace("PC + 4 + (offset << 2)", $"${target:X8}")}";
            }

            return eq;
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
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, ${target:X8}";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (0x00 << 16) | (args[2] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rs", CpuHelpers.RegName(args.Rs));

            if (cpu != null && pc.HasValue)
            {
                long rsVal = (long)cpu.RegsLo[args.Rs];
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                bool taken = rsVal < 0;
                return $"{eq} => {CpuHelpers.RegName(args.Rs)}={rsVal}, target=${target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]";
            }

            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                return $"{eq.Replace("PC + 4 + (offset << 2)", $"${target:X8}")}";
            }

            return eq;
        }
    }

    // BGEZ - Branch if greater than or equal to zero
    public sealed class BGEZ : Instruction<Args_I>
    {
        public BGEZ()
        {
            Opcode = 0x01;
            Funct = null;
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
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, ${target:X8}";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (0x01 << 16) | (args[2] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation.Replace("rs", CpuHelpers.RegName(args.Rs));

            if (cpu != null && pc.HasValue)
            {
                long rsVal = (long)cpu.RegsLo[args.Rs];
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                bool taken = rsVal >= 0;
                return $"{eq} => {CpuHelpers.RegName(args.Rs)}={rsVal}, target=${target:X8} [{(taken ? "TAKEN" : "NOT TAKEN")}]";
            }

            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                return $"{eq.Replace("PC + 4 + (offset << 2)", $"${target:X8}")}";
            }

            return eq;
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
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}, ${target:X8}";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (args[1] << 16) | (args[2] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));

            if (cpu != null && pc.HasValue)
            {
                ulong rsVal = cpu.RegsLo[args.Rs];
                ulong rtVal = cpu.RegsLo[args.Rt];
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                bool taken = rsVal == rtVal;
                return $"{eq} => {CpuHelpers.RegName(args.Rs)}=0x{rsVal:X16}, {CpuHelpers.RegName(args.Rt)}=0x{rtVal:X16}, target=${target:X8} [{(taken ? "TAKEN" : "NOT TAKEN (delay slot nullified)")}]";
            }

            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                return $"{eq.Replace("PC + 4 + (offset << 2)", $"${target:X8}")}";
            }

            return eq;
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
                return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}, ${target:X8}";
            }
            return $"{Name} {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}, {args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] << 21) | (args[1] << 16) | (args[2] & 0xFFFF));
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("rt", CpuHelpers.RegName(args.Rt));

            if (cpu != null && pc.HasValue)
            {
                ulong rsVal = cpu.RegsLo[args.Rs];
                ulong rtVal = cpu.RegsLo[args.Rt];
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                bool taken = rsVal != rtVal;
                return $"{eq} => {CpuHelpers.RegName(args.Rs)}=0x{rsVal:X16}, {CpuHelpers.RegName(args.Rt)}=0x{rtVal:X16}, target=${target:X8} [{(taken ? "TAKEN" : "NOT TAKEN (delay slot nullified)")}]";
            }

            if (pc.HasValue)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                uint target = (uint)((int)pc.Value + 4 + offset);
                return $"{eq.Replace("PC + 4 + (offset << 2)", $"${target:X8}")}";
            }

            return eq;
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
            uint target = args.Addr << 2;
            if (pc.HasValue)
            {
                uint pcUpper = (pc.Value + 4) & 0xF0000000;
                target = pcUpper | target;
            }
            return $"{Name} ${target:X8}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] & 0x3FFFFFF));
        }

        public override string Analyze(Args_J args, CpuState? cpu = null, uint? pc = null)
        {
            if (pc.HasValue)
            {
                uint pcUpper = (pc.Value + 4) & 0xF0000000;
                uint target = pcUpper | (args.Addr << 2);
                return $"{Equation.Replace("target", $"0x{args.Addr:X}")} => ${target:X8}";
            }

            return Equation.Replace("target", $"0x{args.Addr:X}");
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
            uint target = args.Addr << 2;
            if (pc.HasValue)
            {
                uint pcUpper = (pc.Value + 4) & 0xF0000000;
                target = pcUpper | target;
            }
            return $"{Name} ${target:X8}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)((Opcode << 26) | (args[0] & 0x3FFFFFF));
        }

        public override string Analyze(Args_J args, CpuState? cpu = null, uint? pc = null)
        {
            if (pc.HasValue)
            {
                uint pcUpper = (pc.Value + 4) & 0xF0000000;
                uint target = pcUpper | (args.Addr << 2);
                uint returnAddr = pc.Value + 8;
                return $"{Equation.Replace("target", $"0x{args.Addr:X}")} => target=${target:X8}, $ra=0x{returnAddr:X8}";
            }

            return Equation.Replace("target", $"0x{args.Addr:X}");
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
            string eq = Equation.Replace("rs", CpuHelpers.RegName(args.Rs));

            if (cpu != null)
            {
                ulong target = cpu.RegsLo[args.Rs];
                return $"{eq} => 0x{target:X16}";
            }

            return eq;
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
            string eq = Equation
                .Replace("rd", CpuHelpers.RegName(args.Rd))
                .Replace("rs", CpuHelpers.RegName(args.Rs));

            if (cpu != null && pc.HasValue)
            {
                ulong target = cpu.RegsLo[args.Rs];
                uint returnAddr = pc.Value + 8;
                return $"{eq} => target=0x{target:X16}, {CpuHelpers.RegName(args.Rd)}=0x{returnAddr:X8}";
            }

            return eq;
        }
    }
}