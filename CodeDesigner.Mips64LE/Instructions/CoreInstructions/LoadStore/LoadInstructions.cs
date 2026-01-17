using CodeDesigner.Mips64LE.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.CPU;
using System;

/*
| Instruction | Description |
|-------------|-------------|
| LB | Load byte (signed) |
| LBU | Load byte (unsigned) |
| LH | Load halfword (signed) |
| LHU | Load halfword (unsigned) |
| LW | Load word |
| LWU | Load word unsigned |
| LD | Load doubleword |
| LWL | Load word left |
| LWR | Load word right |
| LDL | Load doubleword left |
| LDR | Load doubleword right |
| LQ | Load quadword (EE extension) |
*/
namespace CodeDesigner.Mips64LE.Cpu.Instructions.EE
{
    // LB - Load byte (signed)
    public sealed class LB : Instruction<Args_I>
    {
        public LB()
        {
            Opcode = 0x20;
            Funct = null;
            Name = "lb";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "rt = SignExt(Memory[rs + offset])";
            Description = "Load byte (signed) - loads 8-bit value and sign-extends to 128-bit";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            
            sbyte value = (sbyte)cpu.Memory[addr];
            long signExtended = value; // Sign-extend to 64-bit
            cpu.RegsLo[args.Rt] = (ulong)signExtended;
            cpu.RegsHi[args.Rt] = signExtended < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                return $"{eq} => addr = 0x{addr:X8}";
            }
            return eq;
        }
    }

    // LBU - Load byte (unsigned)
    public sealed class LBU : Instruction<Args_I>
    {
        public LBU()
        {
            Opcode = 0x24;
            Funct = null;
            Name = "lbu";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "rt = ZeroExt(Memory[rs + offset])";
            Description = "Load byte (unsigned) - loads 8-bit value and zero-extends to 128-bit";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            
            byte value = cpu.Memory[addr];
            cpu.RegsLo[args.Rt] = value; // Zero-extend to 64-bit
            cpu.RegsHi[args.Rt] = 0;
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                return $"{eq} => addr = 0x{addr:X8}";
            }
            return eq;
        }
    }

    // LH - Load halfword (signed)
    public sealed class LH : Instruction<Args_I>
    {
        public LH()
        {
            Opcode = 0x21;
            Funct = null;
            Name = "lh";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "rt = SignExt(Memory16[rs + offset])";
            Description = "Load halfword (signed) - loads 16-bit value and sign-extends to 128-bit";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            
            // Little-endian load
            short value = (short)(cpu.Memory[addr] | (cpu.Memory[addr + 1] << 8));
            long signExtended = value; // Sign-extend to 64-bit
            cpu.RegsLo[args.Rt] = (ulong)signExtended;
            cpu.RegsHi[args.Rt] = signExtended < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                return $"{eq} => addr = 0x{addr:X8}";
            }
            return eq;
        }
    }

    // LHU - Load halfword (unsigned)
    public sealed class LHU : Instruction<Args_I>
    {
        public LHU()
        {
            Opcode = 0x25;
            Funct = null;
            Name = "lhu";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "rt = ZeroExt(Memory16[rs + offset])";
            Description = "Load halfword (unsigned) - loads 16-bit value and zero-extends to 128-bit";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            
            // Little-endian load
            ushort value = (ushort)(cpu.Memory[addr] | (cpu.Memory[addr + 1] << 8));
            cpu.RegsLo[args.Rt] = value; // Zero-extend to 64-bit
            cpu.RegsHi[args.Rt] = 0;
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                return $"{eq} => addr = 0x{addr:X8}";
            }
            return eq;
        }
    }

    // LW - Load word (signed)
    public sealed class LW : Instruction<Args_I>
    {
        public LW()
        {
            Opcode = 0x23;
            Funct = null;
            Name = "lw";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "rt = SignExt(Memory32[rs + offset])";
            Description = "Load word - loads 32-bit value and sign-extends to 128-bit";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            
            // Little-endian load
            int value = (int)(cpu.Memory[addr] | 
                             (cpu.Memory[addr + 1] << 8) | 
                             (cpu.Memory[addr + 2] << 16) | 
                             (cpu.Memory[addr + 3] << 24));
            
            long signExtended = value; // Sign-extend to 64-bit
            cpu.RegsLo[args.Rt] = (ulong)signExtended;
            cpu.RegsHi[args.Rt] = signExtended < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                return $"{eq} => addr = 0x{addr:X8}";
            }
            return eq;
        }
    }

    // LWU - Load word unsigned
    public sealed class LWU : Instruction<Args_I>
    {
        public LWU()
        {
            Opcode = 0x27;
            Funct = null;
            Name = "lwu";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "rt = ZeroExt(Memory32[rs + offset])";
            Description = "Load word unsigned - loads 32-bit value and zero-extends to 128-bit";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            
            // Little-endian load
            uint value = (uint)(cpu.Memory[addr] | 
                               (cpu.Memory[addr + 1] << 8) | 
                               (cpu.Memory[addr + 2] << 16) | 
                               (cpu.Memory[addr + 3] << 24));
            
            cpu.RegsLo[args.Rt] = value; // Zero-extend to 64-bit
            cpu.RegsHi[args.Rt] = 0;
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                return $"{eq} => addr = 0x{addr:X8}";
            }
            return eq;
        }
    }

    // LD - Load doubleword
    public sealed class LD : Instruction<Args_I>
    {
        public LD()
        {
            Opcode = 0x37;
            Funct = null;
            Name = "ld";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "rt = Memory64[rs + offset]";
            Description = "Load doubleword - loads 64-bit value";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            
            // Little-endian load (64-bit)
            ulong value = cpu.Memory[addr] |
                         ((ulong)cpu.Memory[addr + 1] << 8) |
                         ((ulong)cpu.Memory[addr + 2] << 16) |
                         ((ulong)cpu.Memory[addr + 3] << 24) |
                         ((ulong)cpu.Memory[addr + 4] << 32) |
                         ((ulong)cpu.Memory[addr + 5] << 40) |
                         ((ulong)cpu.Memory[addr + 6] << 48) |
                         ((ulong)cpu.Memory[addr + 7] << 56);
            
            cpu.RegsLo[args.Rt] = value;
            cpu.RegsHi[args.Rt] = 0;
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                return $"{eq} => addr = 0x{addr:X8}";
            }
            return eq;
        }
    }

    // LWL - Load word left
    public sealed class LWL : Instruction<Args_I>
    {
        public LWL()
        {
            Opcode = 0x22;
            Funct = null;
            Name = "lwl";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "rt = merge_left(rt, Memory32[rs + offset])";
            Description = "Load word left - loads unaligned word (left portion)";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            uint alignedAddr = addr & ~3u; // Align to 4-byte boundary
            int shift = (int)((addr & 3) * 8);
            
            // Load word from aligned address
            uint word = (uint)(cpu.Memory[alignedAddr] |
                              (cpu.Memory[alignedAddr + 1] << 8) |
                              (cpu.Memory[alignedAddr + 2] << 16) |
                              (cpu.Memory[alignedAddr + 3] << 24));
            
            // Merge with existing register value
            uint mask = 0xFFFFFFFFu << shift;
            uint regValue = (uint)cpu.RegsLo[args.Rt];
            uint result = (regValue & ~mask) | (word << shift);
            
            // Sign-extend to 64-bit
            long signExtended = (int)result;
            cpu.RegsLo[args.Rt] = (ulong)signExtended;
            cpu.RegsHi[args.Rt] = signExtended < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                return $"{eq} => addr = 0x{addr:X8} (unaligned)";
            }
            return eq;
        }
    }

    // LWR - Load word right
    public sealed class LWR : Instruction<Args_I>
    {
        public LWR()
        {
            Opcode = 0x26;
            Funct = null;
            Name = "lwr";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "rt = merge_right(rt, Memory32[rs + offset])";
            Description = "Load word right - loads unaligned word (right portion)";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            uint alignedAddr = addr & ~3u; // Align to 4-byte boundary
            int shift = (int)((3 - (addr & 3)) * 8);
            
            // Load word from aligned address
            uint word = (uint)(cpu.Memory[alignedAddr] |
                              (cpu.Memory[alignedAddr + 1] << 8) |
                              (cpu.Memory[alignedAddr + 2] << 16) |
                              (cpu.Memory[alignedAddr + 3] << 24));
            
            // Merge with existing register value
            uint mask = 0xFFFFFFFFu >> shift;
            uint regValue = (uint)cpu.RegsLo[args.Rt];
            uint result = (regValue & ~mask) | (word >> shift);
            
            // Sign-extend to 64-bit
            long signExtended = (int)result;
            cpu.RegsLo[args.Rt] = (ulong)signExtended;
            cpu.RegsHi[args.Rt] = signExtended < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                return $"{eq} => addr = 0x{addr:X8} (unaligned)";
            }
            return eq;
        }
    }

    // LDL - Load doubleword left
    public sealed class LDL : Instruction<Args_I>
    {
        public LDL()
        {
            Opcode = 0x1A;
            Funct = null;
            Name = "ldl";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "rt = merge_left(rt, Memory64[rs + offset])";
            Description = "Load doubleword left - loads unaligned doubleword (left portion)";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            uint alignedAddr = addr & ~7u; // Align to 8-byte boundary
            int shift = (int)((addr & 7) * 8);
            
            // Load doubleword from aligned address
            ulong dword = cpu.Memory[alignedAddr] |
                         ((ulong)cpu.Memory[alignedAddr + 1] << 8) |
                         ((ulong)cpu.Memory[alignedAddr + 2] << 16) |
                         ((ulong)cpu.Memory[alignedAddr + 3] << 24) |
                         ((ulong)cpu.Memory[alignedAddr + 4] << 32) |
                         ((ulong)cpu.Memory[alignedAddr + 5] << 40) |
                         ((ulong)cpu.Memory[alignedAddr + 6] << 48) |
                         ((ulong)cpu.Memory[alignedAddr + 7] << 56);
            
            // Merge with existing register value
            ulong mask = 0xFFFFFFFFFFFFFFFFul << shift;
            ulong regValue = cpu.RegsLo[args.Rt];
            cpu.RegsLo[args.Rt] = (regValue & ~mask) | (dword << shift);
            cpu.RegsHi[args.Rt] = 0;
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                return $"{eq} => addr = 0x{addr:X8} (unaligned)";
            }
            return eq;
        }
    }

    // LDR - Load doubleword right
    public sealed class LDR : Instruction<Args_I>
    {
        public LDR()
        {
            Opcode = 0x1B;
            Funct = null;
            Name = "ldr";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "rt = merge_right(rt, Memory64[rs + offset])";
            Description = "Load doubleword right - loads unaligned doubleword (right portion)";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            uint alignedAddr = addr & ~7u; // Align to 8-byte boundary
            int shift = (int)((7 - (addr & 7)) * 8);
            
            // Load doubleword from aligned address
            ulong dword = cpu.Memory[alignedAddr] |
                         ((ulong)cpu.Memory[alignedAddr + 1] << 8) |
                         ((ulong)cpu.Memory[alignedAddr + 2] << 16) |
                         ((ulong)cpu.Memory[alignedAddr + 3] << 24) |
                         ((ulong)cpu.Memory[alignedAddr + 4] << 32) |
                         ((ulong)cpu.Memory[alignedAddr + 5] << 40) |
                         ((ulong)cpu.Memory[alignedAddr + 6] << 48) |
                         ((ulong)cpu.Memory[alignedAddr + 7] << 56);
            
            // Merge with existing register value
            ulong mask = 0xFFFFFFFFFFFFFFFFul >> shift;
            ulong regValue = cpu.RegsLo[args.Rt];
            cpu.RegsLo[args.Rt] = (regValue & ~mask) | (dword >> shift);
            cpu.RegsHi[args.Rt] = 0;
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                return $"{eq} => addr = 0x{addr:X8} (unaligned)";
            }
            return eq;
        }
    }

    // LQ - Load quadword (EE extension - 128-bit)
    public sealed class LQ : Instruction<Args_I>
    {
        public LQ()
        {
            Opcode = 0x1E;
            Funct = null;
            Name = "lq";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "rt = Memory128[rs + offset]";
            Description = "Load quadword - loads 128-bit value (EE extension)";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            
            // Load 128-bit value (must be 16-byte aligned on real hardware)
            // Lower 64 bits
            ulong lo = cpu.Memory[addr] |
                      ((ulong)cpu.Memory[addr + 1] << 8) |
                      ((ulong)cpu.Memory[addr + 2] << 16) |
                      ((ulong)cpu.Memory[addr + 3] << 24) |
                      ((ulong)cpu.Memory[addr + 4] << 32) |
                      ((ulong)cpu.Memory[addr + 5] << 40) |
                      ((ulong)cpu.Memory[addr + 6] << 48) |
                      ((ulong)cpu.Memory[addr + 7] << 56);
            
            // Upper 64 bits
            ulong hi = cpu.Memory[addr + 8] |
                      ((ulong)cpu.Memory[addr + 9] << 8) |
                      ((ulong)cpu.Memory[addr + 10] << 16) |
                      ((ulong)cpu.Memory[addr + 11] << 24) |
                      ((ulong)cpu.Memory[addr + 12] << 32) |
                      ((ulong)cpu.Memory[addr + 13] << 40) |
                      ((ulong)cpu.Memory[addr + 14] << 48) |
                      ((ulong)cpu.Memory[addr + 15] << 56);
            
            cpu.RegsLo[args.Rt] = lo;
            cpu.RegsHi[args.Rt] = hi;
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                return $"{eq} => addr = 0x{addr:X8} (16-byte aligned)";
            }
            return eq;
        }
    }
}
