using CodeDesigner.Mips64LE.Cpu.InstrArgs;
using CodeDesigner.Mips64LE.CPU;
using System;

/*
| Instruction | Description |
|-------------|-------------|
| MFC0 | Move from COP0 register to GPR |
| MTC0 | Move from GPR to COP0 register |
| DMFC0 | Move from COP0 register to GPR (64-bit) |
| DMTC0 | Move from GPR to COP0 register (64-bit) |
| CFC0 | Move control from COP0 |
| CTC0 | Move control to COP0 |
| ERET | Return from exception |
| TLBP | Probe TLB for matching entry |
| TLBR | Read indexed TLB entry |
| TLBWI | Write indexed TLB entry |
| TLBWR | Write random TLB entry |
| CACHE | Cache operation |
*/
namespace CodeDesigner.Mips64LE.Cpu.Instructions.COP0
{
    // MFC0 - Move from COP0 register to GPR (32-bit)
    public sealed class MFC0 : Instruction<RArgs>
    {
        public MFC0()
        {
            Opcode = 0x10;
            Funct = 0x00;
            Name = "mfc0";
            ArgType = InstrArgType.R;
            ArgNames = ["rt", "rd"];
            Equation = "rt = COP0[rd]";
            Description = "Move from COP0 register to GPR - reads 32-bit COP0 register";
        }

        public override void Exec(CpuState cpu, RArgs args)
        {
            // Read from COP0 register (rd specifies which COP0 register)
            uint value = cpu.COP0.ReadRegister32(args.Rd);
            
            // Sign-extend to 64-bit and store in GPR
            long signExtended = (int)value;
            cpu.RegsLo[args.Rt] = (ulong)signExtended;
            cpu.RegsHi[args.Rt] = signExtended < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
        }

        public override string Disasm(RArgs args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, ${args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x00 << 21 | args[0] << 16 | args[1] << 11);
        }

        public override string Analyze(RArgs args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rd", $"${args.Rd}");
            
            if (cpu != null)
            {
                uint value = cpu.COP0.ReadRegister32(args.Rd);
                return $"{eq} => COP0[{args.Rd}] = 0x{value:X8}";
            }
            return eq;
        }
    }

    // MTC0 - Move from GPR to COP0 register (32-bit)
    public sealed class MTC0 : Instruction<RArgs>
    {
        public MTC0()
        {
            Opcode = 0x10;
            Funct = 0x00;
            Name = "mtc0";
            ArgType = InstrArgType.R;
            ArgNames = ["rt", "rd"];
            Equation = "COP0[rd] = rt[31:0]";
            Description = "Move from GPR to COP0 register - writes 32-bit value to COP0";
        }

        public override void Exec(CpuState cpu, RArgs args)
        {
            // Write lower 32 bits of GPR to COP0 register
            uint value = (uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
            cpu.COP0.WriteRegister32(args.Rd, value);
        }

        public override string Disasm(RArgs args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, ${args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x04 << 21 | args[0] << 16 | args[1] << 11);
        }

        public override string Analyze(RArgs args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rd", $"${args.Rd}");
            
            if (cpu != null)
            {
                uint value = (uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
                return $"{eq} => write 0x{value:X8} to COP0[{args.Rd}]";
            }
            return eq;
        }
    }

    // DMFC0 - Move from COP0 register to GPR (64-bit)
    public sealed class DMFC0 : Instruction<RArgs>
    {
        public DMFC0()
        {
            Opcode = 0x10;
            Funct = 0x00;
            Name = "dmfc0";
            ArgType = InstrArgType.R;
            ArgNames = ["rt", "rd"];
            Equation = "rt = COP0[rd] (64-bit)";
            Description = "Move from COP0 register to GPR (64-bit) - reads full 64-bit COP0 register";
        }

        public override void Exec(CpuState cpu, RArgs args)
        {
            // Read 64-bit value from COP0 register
            ulong value = cpu.COP0.ReadRegister64(args.Rd);
            cpu.RegsLo[args.Rt] = value;
            cpu.RegsHi[args.Rt] = 0;
        }

        public override string Disasm(RArgs args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, ${args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x01 << 21 | args[0] << 16 | args[1] << 11);
        }

        public override string Analyze(RArgs args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rd", $"${args.Rd}");
            
            if (cpu != null)
            {
                ulong value = cpu.COP0.ReadRegister64(args.Rd);
                return $"{eq} => COP0[{args.Rd}] = 0x{value:X16}";
            }
            return eq;
        }
    }

    // DMTC0 - Move from GPR to COP0 register (64-bit)
    public sealed class DMTC0 : Instruction<RArgs>
    {
        public DMTC0()
        {
            Opcode = 0x10;
            Funct = 0x00;
            Name = "dmtc0";
            ArgType = InstrArgType.R;
            ArgNames = ["rt", "rd"];
            Equation = "COP0[rd] = rt (64-bit)";
            Description = "Move from GPR to COP0 register (64-bit) - writes full 64-bit value";
        }

        public override void Exec(CpuState cpu, RArgs args)
        {
            // Write 64-bit GPR value to COP0 register
            ulong value = cpu.RegsLo[args.Rt];
            cpu.COP0.WriteRegister64(args.Rd, value);
        }

        public override string Disasm(RArgs args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, ${args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x05 << 21 | args[0] << 16 | args[1] << 11);
        }

        public override string Analyze(RArgs args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rd", $"${args.Rd}");
            
            if (cpu != null)
            {
                ulong value = cpu.RegsLo[args.Rt];
                return $"{eq} => write 0x{value:X16} to COP0[{args.Rd}]";
            }
            return eq;
        }
    }

    // CFC0 - Move control from COP0
    public sealed class CFC0 : Instruction<RArgs>
    {
        public CFC0()
        {
            Opcode = 0x10;
            Funct = 0x00;
            Name = "cfc0";
            ArgType = InstrArgType.R;
            ArgNames = ["rt", "rd"];
            Equation = "rt = COP0_Control[rd]";
            Description = "Move control from COP0 - reads COP0 control register";
        }

        public override void Exec(CpuState cpu, RArgs args)
        {
            // Read from COP0 control register
            uint value = cpu.COP0.ReadControlRegister(args.Rd);
            
            // Sign-extend to 64-bit
            long signExtended = (int)value;
            cpu.RegsLo[args.Rt] = (ulong)signExtended;
            cpu.RegsHi[args.Rt] = signExtended < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
        }

        public override string Disasm(RArgs args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, ${args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x02 << 21 | args[0] << 16 | args[1] << 11);
        }

        public override string Analyze(RArgs args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rd", $"${args.Rd}");
            
            if (cpu != null)
            {
                uint value = cpu.COP0.ReadControlRegister(args.Rd);
                return $"{eq} => COP0_Control[{args.Rd}] = 0x{value:X8}";
            }
            return eq;
        }
    }

    // CTC0 - Move control to COP0
    public sealed class CTC0 : Instruction<RArgs>
    {
        public CTC0()
        {
            Opcode = 0x10;
            Funct = 0x00;
            Name = "ctc0";
            ArgType = InstrArgType.R;
            ArgNames = ["rt", "rd"];
            Equation = "COP0_Control[rd] = rt[31:0]";
            Description = "Move control to COP0 - writes to COP0 control register";
        }

        public override void Exec(CpuState cpu, RArgs args)
        {
            // Write to COP0 control register
            uint value = (uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
            cpu.COP0.WriteControlRegister(args.Rd, value);
        }

        public override string Disasm(RArgs args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, ${args.Rd}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x06 << 21 | args[0] << 16 | args[1] << 11);
        }

        public override string Analyze(RArgs args, CpuState? cpu = null, uint? pc = null)
        {
            string eq = Equation
                .Replace("rt", CpuHelpers.RegName(args.Rt))
                .Replace("rd", $"${args.Rd}");
            
            if (cpu != null)
            {
                uint value = (uint)(cpu.RegsLo[args.Rt] & 0xFFFFFFFF);
                return $"{eq} => write 0x{value:X8} to COP0_Control[{args.Rd}]";
            }
            return eq;
        }
    }

    // ERET - Return from exception
    public sealed class ERET : Instruction<RArgs>
    {
        public ERET()
        {
            Opcode = 0x10;
            Funct = 0x18;
            Name = "eret";
            ArgType = InstrArgType.R;
            ArgNames = [];
            Equation = "PC = EPC; clear_exception_mode()";
            Description = "Return from exception - restores PC from EPC and clears exception state";
        }

        public override void Exec(CpuState cpu, RArgs args)
        {
            // Read EPC (Exception Program Counter) - COP0 register 14
            ulong epc = cpu.COP0.ReadRegister64(14);
            cpu.PC = epc;
            
            // Clear EXL bit in Status register (bit 1)
            uint status = cpu.COP0.ReadRegister32(12);
            status &= ~(1u << 1); // Clear EXL bit
            cpu.COP0.WriteRegister32(12, status);
        }

        public override string Disasm(RArgs args, uint? pc = null)
        {
            return Name;
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | Funct);
        }

        public override string Analyze(RArgs args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu != null)
            {
                ulong epc = cpu.COP0.ReadRegister64(14);
                return $"{Equation} => return to PC = 0x{epc:X16}";
            }
            return "Return from exception, restore PC from EPC";
        }
    }

    // TLBP - Probe TLB for matching entry
    public sealed class TLBP : Instruction<RArgs>
    {
        public TLBP()
        {
            Opcode = 0x10;
            Funct = 0x08;
            Name = "tlbp";
            ArgType = InstrArgType.R;
            ArgNames = [];
            Equation = "Index = TLB_Probe(EntryHi)";
            Description = "Probe TLB for matching entry - searches TLB for entry matching EntryHi";
        }

        public override void Exec(CpuState cpu, RArgs args)
        {
            // Probe TLB using EntryHi register (COP0 register 10)
            ulong entryHi = cpu.COP0.ReadRegister64(10);
            int index = cpu.COP0.TLB.Probe(entryHi);
            
            // Write result to Index register (COP0 register 0)
            // If not found, set bit 31 (probe failure)
            uint indexValue = index < 0 ? 0x80000000u : (uint)index;
            cpu.COP0.WriteRegister32(0, indexValue);
        }

        public override string Disasm(RArgs args, uint? pc = null)
        {
            return Name;
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | Funct);
        }

        public override string Analyze(RArgs args, CpuState? cpu = null, uint? pc = null)
        {
            return "Probe TLB for matching entry based on EntryHi";
        }
    }

    // TLBR - Read indexed TLB entry
    public sealed class TLBR : Instruction<RArgs>
    {
        public TLBR()
        {
            Opcode = 0x10;
            Funct = 0x01;
            Name = "tlbr";
            ArgType = InstrArgType.R;
            ArgNames = [];
            Equation = "Read TLB[Index] -> EntryHi, EntryLo0, EntryLo1, PageMask";
            Description = "Read indexed TLB entry - loads TLB entry into COP0 registers";
        }

        public override void Exec(CpuState cpu, RArgs args)
        {
            // Read Index register (COP0 register 0)
            uint index = cpu.COP0.ReadRegister32(0);
            
            // Read TLB entry at index
            var entry = cpu.COP0.TLB.ReadEntry((int)index);
            
            // Write to COP0 registers
            cpu.COP0.WriteRegister64(10, entry.EntryHi);  // EntryHi
            cpu.COP0.WriteRegister64(2, entry.EntryLo0);  // EntryLo0
            cpu.COP0.WriteRegister64(3, entry.EntryLo1);  // EntryLo1
            cpu.COP0.WriteRegister32(5, entry.PageMask);  // PageMask
        }

        public override string Disasm(RArgs args, uint? pc = null)
        {
            return Name;
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | Funct);
        }

        public override string Analyze(RArgs args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu != null)
            {
                uint index = cpu.COP0.ReadRegister32(0);
                return $"{Equation} => read TLB entry {index}";
            }
            return "Read indexed TLB entry into COP0 registers";
        }
    }

    // TLBWI - Write indexed TLB entry
    public sealed class TLBWI : Instruction<RArgs>
    {
        public TLBWI()
        {
            Opcode = 0x10;
            Funct = 0x02;
            Name = "tlbwi";
            ArgType = InstrArgType.R;
            ArgNames = [];
            Equation = "TLB[Index] = EntryHi, EntryLo0, EntryLo1, PageMask";
            Description = "Write indexed TLB entry - writes COP0 registers to TLB entry";
        }

        public override void Exec(CpuState cpu, RArgs args)
        {
            // Read Index register (COP0 register 0)
            uint index = cpu.COP0.ReadRegister32(0);
            
            // Read COP0 registers
            ulong entryHi = cpu.COP0.ReadRegister64(10);   // EntryHi
            ulong entryLo0 = cpu.COP0.ReadRegister64(2);   // EntryLo0
            ulong entryLo1 = cpu.COP0.ReadRegister64(3);   // EntryLo1
            uint pageMask = cpu.COP0.ReadRegister32(5);    // PageMask
            
            // Write to TLB entry
            cpu.COP0.TLB.WriteEntry((int)index, entryHi, entryLo0, entryLo1, pageMask);
        }

        public override string Disasm(RArgs args, uint? pc = null)
        {
            return Name;
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | Funct);
        }

        public override string Analyze(RArgs args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu != null)
            {
                uint index = cpu.COP0.ReadRegister32(0);
                return $"{Equation} => write to TLB entry {index}";
            }
            return "Write indexed TLB entry from COP0 registers";
        }
    }

    // TLBWR - Write random TLB entry
    public sealed class TLBWR : Instruction<RArgs>
    {
        public TLBWR()
        {
            Opcode = 0x10;
            Funct = 0x06;
            Name = "tlbwr";
            ArgType = InstrArgType.R;
            ArgNames = [];
            Equation = "TLB[Random] = EntryHi, EntryLo0, EntryLo1, PageMask";
            Description = "Write random TLB entry - writes COP0 registers to random TLB entry";
        }

        public override void Exec(CpuState cpu, RArgs args)
        {
            // Read Random register (COP0 register 1)
            uint random = cpu.COP0.ReadRegister32(1);
            
            // Read COP0 registers
            ulong entryHi = cpu.COP0.ReadRegister64(10);   // EntryHi
            ulong entryLo0 = cpu.COP0.ReadRegister64(2);   // EntryLo0
            ulong entryLo1 = cpu.COP0.ReadRegister64(3);   // EntryLo1
            uint pageMask = cpu.COP0.ReadRegister32(5);    // PageMask
            
            // Write to TLB entry at random index
            cpu.COP0.TLB.WriteEntry((int)random, entryHi, entryLo0, entryLo1, pageMask);
        }

        public override string Disasm(RArgs args, uint? pc = null)
        {
            return Name;
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | Funct);
        }

        public override string Analyze(RArgs args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu != null)
            {
                uint random = cpu.COP0.ReadRegister32(1);
                return $"{Equation} => write to TLB entry {random} (random)";
            }
            return "Write random TLB entry from COP0 registers";
        }
    }

    // CACHE - Cache operation
    public sealed class CACHE : Instruction<IArgs>
    {
        public CACHE()
        {
            Opcode = 0x2F;
            Funct = null;
            Name = "cache";
            ArgType = InstrArgType.I;
            ArgNames = ["op", "offset", "base"];
            Equation = "cache_op(op, base + offset)";
            Description = "Cache operation - performs cache maintenance operation";
        }

        public override void Exec(CpuState cpu, IArgs args)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
            uint op = (uint)args.Rt; // Operation code in rt field
            
            // Perform cache operation
            cpu.COP0.Cache.PerformOperation(op, addr);
        }

        public override string Disasm(IArgs args, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            return $"{Name} 0x{args.Rt:X}, {offset}({CpuHelpers.RegName(args.Rs)})";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[2] << 21 | args[0] << 16 | args[1] & 0xFFFF);
        }

        public override string Analyze(IArgs args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm);
            string eq = Equation
                .Replace("op", $"0x{args.Rt:X}")
                .Replace("base", CpuHelpers.RegName(args.Rs))
                .Replace("offset", offset.ToString());
            
            if (cpu != null)
            {
                uint addr = (uint)((int)cpu.RegsLo[args.Rs] + offset);
                return $"{eq} => addr = 0x{addr:X8}";
            }
            return eq;
        }
    }
    // Add these classes to your existing COP0Instructions.cs file

    // DI - Disable Interrupts
    public sealed class DI : Instruction<RArgs>
    {
        public DI()
        {
            Opcode = 0x10;
            Funct = 0x00;
            Name = "di";
            ArgType = InstrArgType.R;
            ArgNames = ["rt"];
            Equation = "rt = Status; Status.IE = 0";
            Description = "Disable Interrupts - clears IE bit in Status register and returns previous Status";
        }

        public override void Exec(CpuState cpu, RArgs args)
        {
            // Read current Status register (COP0 register 12)
            uint status = cpu.COP0.ReadRegister32(12);

            // Store previous status in rt (if rt != 0)
            if (args.Rt != 0)
            {
                long signExtended = (int)status;
                cpu.RegsLo[args.Rt] = (ulong)signExtended;
                cpu.RegsHi[args.Rt] = signExtended < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
            }

            // Clear IE bit (bit 0)
            status &= ~(1u << 0);
            cpu.COP0.WriteRegister32(12, status);
        }

        public override string Disasm(RArgs args, uint? pc = null)
        {
            if (args.Rt != 0)
                return $"{Name} {CpuHelpers.RegName(args.Rt)}";
            return Name;
        }

        public override uint Asm(int[] args)
        {
            int rt = args.Length > 0 ? args[0] : 0;
            return (uint)(Opcode << 26 | 0x0B << 21 | rt << 16 | 0x0C << 11);
        }

        public override string Analyze(RArgs args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu != null)
            {
                uint status = cpu.COP0.ReadRegister32(12);
                bool ieEnabled = (status & 0x1) != 0;
                return $"{Equation} => IE was {ieEnabled}, now disabled";
            }
            return "Disable interrupts, return previous Status";
        }
    }

    // EI - Enable Interrupts
    public sealed class EI : Instruction<RArgs>
    {
        public EI()
        {
            Opcode = 0x10;
            Funct = 0x00;
            Name = "ei";
            ArgType = InstrArgType.R;
            ArgNames = ["rt"];
            Equation = "rt = Status; Status.IE = 1";
            Description = "Enable Interrupts - sets IE bit in Status register and returns previous Status";
        }

        public override void Exec(CpuState cpu, RArgs args)
        {
            // Read current Status register (COP0 register 12)
            uint status = cpu.COP0.ReadRegister32(12);

            // Store previous status in rt (if rt != 0)
            if (args.Rt != 0)
            {
                long signExtended = (int)status;
                cpu.RegsLo[args.Rt] = (ulong)signExtended;
                cpu.RegsHi[args.Rt] = signExtended < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
            }

            // Set IE bit (bit 0)
            status |= (1u << 0);
            cpu.COP0.WriteRegister32(12, status);
        }

        public override string Disasm(RArgs args, uint? pc = null)
        {
            if (args.Rt != 0)
                return $"{Name} {CpuHelpers.RegName(args.Rt)}";
            return Name;
        }

        public override uint Asm(int[] args)
        {
            int rt = args.Length > 0 ? args[0] : 0;
            return (uint)(Opcode << 26 | 0x0B << 21 | rt << 16 | 0x14 << 11);
        }

        public override string Analyze(RArgs args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu != null)
            {
                uint status = cpu.COP0.ReadRegister32(12);
                bool ieEnabled = (status & 0x1) != 0;
                return $"{Equation} => IE was {ieEnabled}, now enabled";
            }
            return "Enable interrupts, return previous Status";
        }
    }

    // WAIT - Wait for interrupt (PS2 EE specific)
    public sealed class WAIT : Instruction<RArgs>
    {
        public WAIT()
        {
            Opcode = 0x10;
            Funct = 0x20;
            Name = "wait";
            ArgType = InstrArgType.R;
            ArgNames = [];
            Equation = "wait_for_interrupt()";
            Description = "Wait for interrupt - enters low-power mode until interrupt occurs";
        }

        public override void Exec(CpuState cpu, RArgs args)
        {
            // In a full implementation, this would:
            // 1. Check if interrupts are enabled
            // 2. Enter low-power/idle state
            // 3. Wait for interrupt to occur
            // 4. Resume execution after interrupt is serviced

            // For emulation, this is typically a hint that the CPU should
            // yield execution until an interrupt is pending

            // Check if interrupts are enabled
            uint status = cpu.COP0.ReadRegister32(12);
            bool ieEnabled = (status & 0x1) != 0;

            if (!ieEnabled)
            {
                // If interrupts disabled, this is effectively a NOP or may cause exception
                // PS2 behavior: continues execution
                return;
            }

            // Signal that CPU is waiting (implementation-specific)
            // This would be handled by the emulator's main loop
        }

        public override string Disasm(RArgs args, uint? pc = null)
        {
            return Name;
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | Funct);
        }

        public override string Analyze(RArgs args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu != null)
            {
                uint status = cpu.COP0.ReadRegister32(12);
                bool ieEnabled = (status & 0x1) != 0;
                return $"{Equation} => IE={ieEnabled}, {(ieEnabled ? "will wait" : "continues")}";
            }
            return "Wait for interrupt (low-power mode)";
        }
    }

    // DERET - Debug Exception Return
    public sealed class DERET : Instruction<RArgs>
    {
        public DERET()
        {
            Opcode = 0x10;
            Funct = 0x1F;
            Name = "deret";
            ArgType = InstrArgType.R;
            ArgNames = [];
            Equation = "PC = DEPC; clear_debug_mode()";
            Description = "Debug Exception Return - returns from debug exception, restores PC from DEPC";
        }

        public override void Exec(CpuState cpu, RArgs args)
        {
            // Read DEPC (Debug Exception Program Counter) - COP0 register 23
            ulong depc = cpu.COP0.ReadRegister64(23);
            cpu.PC = depc;

            // Clear DM bit in Debug register (bit 30 of register 22)
            uint debug = cpu.COP0.ReadRegister32(22);
            debug &= ~(1u << 30); // Clear DM (Debug Mode) bit
            cpu.COP0.WriteRegister32(22, debug);
        }

        public override string Disasm(RArgs args, uint? pc = null)
        {
            return Name;
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | 0x10 << 21 | Funct);
        }

        public override string Analyze(RArgs args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu != null)
            {
                ulong depc = cpu.COP0.ReadRegister64(23);
                return $"{Equation} => return to PC = 0x{depc:X16}";
            }
            return "Return from debug exception, restore PC from DEPC";
        }
    }

    // BC0F - Branch on COP0 false (rarely used, included for completeness)
    public sealed class BC0F : Instruction<IArgs>
    {
        public BC0F()
        {
            Opcode = 0x10;
            Funct = null;
            Name = "bc0f";
            ArgType = InstrArgType.I;
            ArgNames = ["offset"];
            Equation = "if (!COP0_CC) PC = PC + 4 + (offset << 2)";
            Description = "Branch on COP0 condition false (rarely used)";
        }

        public override void Exec(CpuState cpu, IArgs args)
        {
            // COP0 doesn't typically have a condition code like COP1
            // This instruction is rarely used on MIPS64/PS2
            // For completeness, assume a condition bit exists

            // Read Status register and check a condition bit (implementation-specific)
            uint status = cpu.COP0.ReadRegister32(12);
            bool condition = (status & (1u << 2)) != 0; // Arbitrary condition bit

            if (!condition)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                cpu.PC = (ulong)((long)cpu.PC + 4 + offset);
            }
        }

        public override string Disasm(IArgs args, uint? pc = null)
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

        public override string Analyze(IArgs args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;

            if (cpu != null && pc.HasValue)
            {
                uint status = cpu.COP0.ReadRegister32(12);
                bool condition = (status & (1u << 2)) != 0;
                ulong target = (ulong)((long)pc.Value + 4 + offset);
                return $"{Equation} => CC={condition}, {(condition ? "no branch" : $"branch to 0x{target:X}")}";
            }
            return Equation;
        }
    }

    // BC0T - Branch on COP0 true (rarely used, included for completeness)
    public sealed class BC0T : Instruction<IArgs>
    {
        public BC0T()
        {
            Opcode = 0x10;
            Funct = null;
            Name = "bc0t";
            ArgType = InstrArgType.I;
            ArgNames = ["offset"];
            Equation = "if (COP0_CC) PC = PC + 4 + (offset << 2)";
            Description = "Branch on COP0 condition true (rarely used)";
        }

        public override void Exec(CpuState cpu, IArgs args)
        {
            // COP0 doesn't typically have a condition code like COP1
            // This instruction is rarely used on MIPS64/PS2

            // Read Status register and check a condition bit (implementation-specific)
            uint status = cpu.COP0.ReadRegister32(12);
            bool condition = (status & (1u << 2)) != 0; // Arbitrary condition bit

            if (condition)
            {
                int offset = CpuHelpers.SignExtend16(args.Imm) << 2;
                cpu.PC = (ulong)((long)cpu.PC + 4 + offset);
            }
        }

        public override string Disasm(IArgs args, uint? pc = null)
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

        public override string Analyze(IArgs args, CpuState? cpu = null, uint? pc = null)
        {
            int offset = CpuHelpers.SignExtend16(args.Imm) << 2;

            if (cpu != null && pc.HasValue)
            {
                uint status = cpu.COP0.ReadRegister32(12);
                bool condition = (status & (1u << 2)) != 0;
                ulong target = (ulong)((long)pc.Value + 4 + offset);
                return $"{Equation} => CC={condition}, {(condition ? $"branch to 0x{target:X}" : "no branch")}";
            }
            return Equation;
        }
    }
}
