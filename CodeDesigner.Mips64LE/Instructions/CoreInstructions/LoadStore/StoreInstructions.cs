using CodeDesigner.Mips64LE.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.CPU;
using System;

/*
| Instruction | Description |
|-------------|-------------|
| SB | Store byte |
| SH | Store halfword |
| SW | Store word |
| SD | Store doubleword |
| SWL | Store word left |
| SWR | Store word right |
| SDL | Store doubleword left |
| SDR | Store doubleword right |
| SQ | Store quadword (EE extension) |
*/
namespace CodeDesigner.Mips64LE.Cpu.Instructions.EE
{
    // SB - Store byte
    public sealed class SB : Instruction<IArgs>
    {
        public SB()
        {
            Opcode = 0x28;
            Funct = null;
            Name = "sb";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "Memory[rs + offset] = rt[7:0]";
            Description = "Store byte - stores lower 8 bits of rt to memory";
        }

        public override void Exec(CpuState cpu, IArgs args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            
            byte value = (byte)(cpu.RegsLo[args.Rt] & 0xFF);
            cpu.Memory[addr] = value;
        }

        public override string Disasm(IArgs args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(IArgs args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                byte value = (byte)(cpu.RegsLo[args.Rt] & 0xFF);
                return $"{eq} => addr = 0x{addr:X8}, value = 0x{value:X2}";
            }
            return eq;
        }
    }

    // SH - Store halfword
    public sealed class SH : Instruction<IArgs>
    {
        public SH()
        {
            Opcode = 0x29;
            Funct = null;
            Name = "sh";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "Memory16[rs + offset] = rt[15:0]";
            Description = "Store halfword - stores lower 16 bits of rt to memory";
        }

        public override void Exec(CpuState cpu, IArgs args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            
            ushort value = (ushort)(cpu.RegsLo[args.Rt] & 0xFFFF);
            
            // Little-endian store
            cpu.Memory[addr] = (byte)(value & 0xFF);
            cpu.Memory[addr + 1] = (byte)((value >> 8) & 0xFF);
        }

        public override string Disasm(IArgs args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(IArgs args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                ushort value = (ushort)(cpu.RegsLo[args.Rt] & 0xFFFF);
                return $"{eq} => addr = 0x{addr:X8}, value = 0x{value:X4}";
            }
            return eq;
        }
    }

    // SW - Store word
    public sealed class SW : Instruction<IArgs>
    {
        public SW()
        {
            Opcode = 0x2B;
            Funct = null;
            Name = "sw";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "Memory32[rs + offset] = rt[31:0]";
            Description = "Store word - stores lower 32 bits of rt to memory";
        }

        public override void Exec(CpuState cpu, IArgs args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            
            uint value = (uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
            
            // Little-endian store
            cpu.Memory[addr] = (byte)(value & 0xFF);
            cpu.Memory[addr + 1] = (byte)((value >> 8) & 0xFF);
            cpu.Memory[addr + 2] = (byte)((value >> 16) & 0xFF);
            cpu.Memory[addr + 3] = (byte)((value >> 24) & 0xFF);
        }

        public override string Disasm(IArgs args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(IArgs args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                uint value = (uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
                return $"{eq} => addr = 0x{addr:X8}, value = 0x{value:X8}";
            }
            return eq;
        }
    }

    // SD - Store doubleword
    public sealed class SD : Instruction<IArgs>
    {
        public SD()
        {
            Opcode = 0x3F;
            Funct = null;
            Name = "sd";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "Memory64[rs + offset] = rt[63:0]";
            Description = "Store doubleword - stores 64 bits of rt to memory";
        }

        public override void Exec(CpuState cpu, IArgs args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            
            ulong value = cpu.RegsLo[args.Rt];
            
            // Little-endian store (64-bit)
            cpu.Memory[addr] = (byte)(value & 0xFF);
            cpu.Memory[addr + 1] = (byte)((value >> 8) & 0xFF);
            cpu.Memory[addr + 2] = (byte)((value >> 16) & 0xFF);
            cpu.Memory[addr + 3] = (byte)((value >> 24) & 0xFF);
            cpu.Memory[addr + 4] = (byte)((value >> 32) & 0xFF);
            cpu.Memory[addr + 5] = (byte)((value >> 40) & 0xFF);
            cpu.Memory[addr + 6] = (byte)((value >> 48) & 0xFF);
            cpu.Memory[addr + 7] = (byte)((value >> 56) & 0xFF);
        }

        public override string Disasm(IArgs args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(IArgs args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                ulong value = cpu.RegsLo[args.Rt];
                return $"{eq} => addr = 0x{addr:X8}, value = 0x{value:X16}";
            }
            return eq;
        }
    }

    // SWL - Store word left
    public sealed class SWL : Instruction<IArgs>
    {
        public SWL()
        {
            Opcode = 0x2A;
            Funct = null;
            Name = "swl";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "Memory[rs + offset] = merge_left(Memory, rt)";
            Description = "Store word left - stores unaligned word (left portion)";
        }

        public override void Exec(CpuState cpu, IArgs args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            uint alignedAddr = addr & ~3u; // Align to 4-byte boundary
            int shift = (int)((addr & 3) * 8);
            
            uint value = (uint)cpu.RegsLo[args.Rt];
            
            // Read existing word
            uint existingWord = (uint)(cpu.Memory[alignedAddr] |
                                      (cpu.Memory[alignedAddr + 1] << 8) |
                                      (cpu.Memory[alignedAddr + 2] << 16) |
                                      (cpu.Memory[alignedAddr + 3] << 24));
            
            // Merge and write back
            uint mask = 0xFFFFFFFFu << shift;
            uint result = (existingWord & ~mask) | (value >> (32 - shift * 8));
            
            cpu.Memory[alignedAddr] = (byte)(result & 0xFF);
            cpu.Memory[alignedAddr + 1] = (byte)((result >> 8) & 0xFF);
            cpu.Memory[alignedAddr + 2] = (byte)((result >> 16) & 0xFF);
            cpu.Memory[alignedAddr + 3] = (byte)((result >> 24) & 0xFF);
        }

        public override string Disasm(IArgs args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(IArgs args, CpuState? cpu = null, uint? pc = null)
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

    // SWR - Store word right
    public sealed class SWR : Instruction<IArgs>
    {
        public SWR()
        {
            Opcode = 0x2E;
            Funct = null;
            Name = "swr";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "Memory[rs + offset] = merge_right(Memory, rt)";
            Description = "Store word right - stores unaligned word (right portion)";
        }

        public override void Exec(CpuState cpu, IArgs args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            uint alignedAddr = addr & ~3u; // Align to 4-byte boundary
            int shift = (int)((3 - (addr & 3)) * 8);
            
            uint value = (uint)cpu.RegsLo[args.Rt];
            
            // Read existing word
            uint existingWord = (uint)(cpu.Memory[alignedAddr] |
                                      (cpu.Memory[alignedAddr + 1] << 8) |
                                      (cpu.Memory[alignedAddr + 2] << 16) |
                                      (cpu.Memory[alignedAddr + 3] << 24));
            
            // Merge and write back
            uint mask = 0xFFFFFFFFu >> shift;
            uint result = (existingWord & ~mask) | (value << (32 - shift * 8));
            
            cpu.Memory[alignedAddr] = (byte)(result & 0xFF);
            cpu.Memory[alignedAddr + 1] = (byte)((result >> 8) & 0xFF);
            cpu.Memory[alignedAddr + 2] = (byte)((result >> 16) & 0xFF);
            cpu.Memory[alignedAddr + 3] = (byte)((result >> 24) & 0xFF);
        }

        public override string Disasm(IArgs args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(IArgs args, CpuState? cpu = null, uint? pc = null)
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

    // SDL - Store doubleword left
    public sealed class SDL : Instruction<IArgs>
    {
        public SDL()
        {
            Opcode = 0x2C;
            Funct = null;
            Name = "sdl";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "Memory[rs + offset] = merge_left(Memory, rt)";
            Description = "Store doubleword left - stores unaligned doubleword (left portion)";
        }

        public override void Exec(CpuState cpu, IArgs args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            uint alignedAddr = addr & ~7u; // Align to 8-byte boundary
            int shift = (int)((addr & 7) * 8);
            
            ulong value = cpu.RegsLo[args.Rt];
            
            // Read existing doubleword
            ulong existingDword = cpu.Memory[alignedAddr] |
                                 ((ulong)cpu.Memory[alignedAddr + 1] << 8) |
                                 ((ulong)cpu.Memory[alignedAddr + 2] << 16) |
                                 ((ulong)cpu.Memory[alignedAddr + 3] << 24) |
                                 ((ulong)cpu.Memory[alignedAddr + 4] << 32) |
                                 ((ulong)cpu.Memory[alignedAddr + 5] << 40) |
                                 ((ulong)cpu.Memory[alignedAddr + 6] << 48) |
                                 ((ulong)cpu.Memory[alignedAddr + 7] << 56);
            
            // Merge and write back
            ulong mask = 0xFFFFFFFFFFFFFFFFul << shift;
            ulong result = (existingDword & ~mask) | (value >> (64 - shift * 8));
            
            cpu.Memory[alignedAddr] = (byte)(result & 0xFF);
            cpu.Memory[alignedAddr + 1] = (byte)((result >> 8) & 0xFF);
            cpu.Memory[alignedAddr + 2] = (byte)((result >> 16) & 0xFF);
            cpu.Memory[alignedAddr + 3] = (byte)((result >> 24) & 0xFF);
            cpu.Memory[alignedAddr + 4] = (byte)((result >> 32) & 0xFF);
            cpu.Memory[alignedAddr + 5] = (byte)((result >> 40) & 0xFF);
            cpu.Memory[alignedAddr + 6] = (byte)((result >> 48) & 0xFF);
            cpu.Memory[alignedAddr + 7] = (byte)((result >> 56) & 0xFF);
        }

        public override string Disasm(IArgs args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(IArgs args, CpuState? cpu = null, uint? pc = null)
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

    // SDR - Store doubleword right
    public sealed class SDR : Instruction<IArgs>
    {
        public SDR()
        {
            Opcode = 0x2D;
            Funct = null;
            Name = "sdr";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "Memory[rs + offset] = merge_right(Memory, rt)";
            Description = "Store doubleword right - stores unaligned doubleword (right portion)";
        }

        public override void Exec(CpuState cpu, IArgs args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            uint alignedAddr = addr & ~7u; // Align to 8-byte boundary
            int shift = (int)((7 - (addr & 7)) * 8);
            
            ulong value = cpu.RegsLo[args.Rt];
            
            // Read existing doubleword
            ulong existingDword = cpu.Memory[alignedAddr] |
                                 ((ulong)cpu.Memory[alignedAddr + 1] << 8) |
                                 ((ulong)cpu.Memory[alignedAddr + 2] << 16) |
                                 ((ulong)cpu.Memory[alignedAddr + 3] << 24) |
                                 ((ulong)cpu.Memory[alignedAddr + 4] << 32) |
                                 ((ulong)cpu.Memory[alignedAddr + 5] << 40) |
                                 ((ulong)cpu.Memory[alignedAddr + 6] << 48) |
                                 ((ulong)cpu.Memory[alignedAddr + 7] << 56);
            
            // Merge and write back
            ulong mask = 0xFFFFFFFFFFFFFFFFul >> shift;
            ulong result = (existingDword & ~mask) | (value << (64 - shift * 8));
            
            cpu.Memory[alignedAddr] = (byte)(result & 0xFF);
            cpu.Memory[alignedAddr + 1] = (byte)((result >> 8) & 0xFF);
            cpu.Memory[alignedAddr + 2] = (byte)((result >> 16) & 0xFF);
            cpu.Memory[alignedAddr + 3] = (byte)((result >> 24) & 0xFF);
            cpu.Memory[alignedAddr + 4] = (byte)((result >> 32) & 0xFF);
            cpu.Memory[alignedAddr + 5] = (byte)((result >> 40) & 0xFF);
            cpu.Memory[alignedAddr + 6] = (byte)((result >> 48) & 0xFF);
            cpu.Memory[alignedAddr + 7] = (byte)((result >> 56) & 0xFF);
        }

        public override string Disasm(IArgs args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(IArgs args, CpuState? cpu = null, uint? pc = null)
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

    // SQ - Store quadword (EE extension - 128-bit)
    public sealed class SQ : Instruction<IArgs>
    {
        public SQ()
        {
            Opcode = 0x1F;
            Funct = null;
            Name = "sq";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "offset", "rs"];
            Equation = "Memory128[rs + offset] = rt";
            Description = "Store quadword - stores 128-bit value (EE extension)";
        }

        public override void Exec(CpuState cpu, IArgs args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            
            // Store 128-bit value (must be 16-byte aligned on real hardware)
            ulong lo = cpu.RegsLo[args.Rt];
            ulong hi = cpu.RegsHi[args.Rt];
            
            // Store lower 64 bits
            cpu.Memory[addr] = (byte)(lo & 0xFF);
            cpu.Memory[addr + 1] = (byte)((lo >> 8) & 0xFF);
            cpu.Memory[addr + 2] = (byte)((lo >> 16) & 0xFF);
            cpu.Memory[addr + 3] = (byte)((lo >> 24) & 0xFF);
            cpu.Memory[addr + 4] = (byte)((lo >> 32) & 0xFF);
            cpu.Memory[addr + 5] = (byte)((lo >> 40) & 0xFF);
            cpu.Memory[addr + 6] = (byte)((lo >> 48) & 0xFF);
            cpu.Memory[addr + 7] = (byte)((lo >> 56) & 0xFF);
            
            // Store upper 64 bits
            cpu.Memory[addr + 8] = (byte)(hi & 0xFF);
            cpu.Memory[addr + 9] = (byte)((hi >> 8) & 0xFF);
            cpu.Memory[addr + 10] = (byte)((hi >> 16) & 0xFF);
            cpu.Memory[addr + 11] = (byte)((hi >> 24) & 0xFF);
            cpu.Memory[addr + 12] = (byte)((hi >> 32) & 0xFF);
            cpu.Memory[addr + 13] = (byte)((hi >> 40) & 0xFF);
            cpu.Memory[addr + 14] = (byte)((hi >> 48) & 0xFF);
            cpu.Memory[addr + 15] = (byte)((hi >> 56) & 0xFF);
        }

        public override string Disasm(IArgs args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 21 | args[1] << 16 | args[2] & 0xFFFF);
        }

        public override string Analyze(IArgs args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rs", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                ulong lo = cpu.RegsLo[args.Rt];
                ulong hi = cpu.RegsHi[args.Rt];
                return $"{eq} => addr = 0x{addr:X8} (16-byte aligned), value = 0x{hi:X16}{lo:X16}";
            }
            return eq;
        }
    }
}
