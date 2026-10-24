using CodeDesigner.Ps2.Common.Cpu.InstrArgs;
using CodeDesigner.Ps2.Common.CPU;
using System;

/*
| Instruction | Description |
|-------------|-------------|
| MOVZ | Move if zero |
| MOVN | Move if not zero |
| LUI | Load upper immediate |
| NOP | No operation |
| SYNC | Memory synchronization |
*/
namespace CodeDesigner.Ps2.Common.Cpu.Instructions.EE
{
    // MOVZ - Move if zero
    public sealed class MOVZ : Instruction<Args_R>
    {
        public MOVZ()
        {
            Opcode = 0x00;
            Funct = 0x0A;
            Name = "movz";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs", "rt"];
            Equation = "if (rt == 0) rd = rs";
            Description = "Move if zero - conditionally moves rs to rd if rt is zero";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rt = cpu.RegsLo[args.Rt];
            if (rt == 0)
            {
                cpu.RegsLo[args.Rd] = cpu.RegsLo[args.Rs];
                cpu.RegsHi[args.Rd] = cpu.RegsHi[args.Rs];
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[1] << 21 | args[2] << 16 | args[0] << 11 | (Funct ?? 0x0A));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu == null) return string.Empty;
            ulong rtVal = cpu.RegsLo[args.Rt];
            if (rtVal == 0)
            {
                uint rsVal = (uint)cpu.RegsLo[args.Rs];
                return $"{CpuHelpers.RegName(args.Rd)} ← {CpuHelpers.RegName(args.Rs)}=0x{rsVal:X8}  [MOVED]";
            }
            return $"skip  ({CpuHelpers.RegName(args.Rt)} ≠ 0)";
        }
    }

    // MOVN - Move if not zero
    public sealed class MOVN : Instruction<Args_R>
    {
        public MOVN()
        {
            Opcode = 0x00;
            Funct = 0x0B;
            Name = "movn";
            ArgType = InstrArgType.R;
            ArgNames = ["rd", "rs", "rt"];
            Equation = "if (rt != 0) rd = rs";
            Description = "Move if not zero - conditionally moves rs to rd if rt is not zero";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            ulong rt = cpu.RegsLo[args.Rt];
            if (rt != 0)
            {
                cpu.RegsLo[args.Rd] = cpu.RegsLo[args.Rs];
                cpu.RegsHi[args.Rd] = cpu.RegsHi[args.Rs];
            }
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rd)}, {CpuHelpers.RegName(args.Rs)}, {CpuHelpers.RegName(args.Rt)}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(args[1] << 21 | args[2] << 16 | args[0] << 11 | (Funct ?? 0x0B));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu == null) return string.Empty;
            ulong rtVal = cpu.RegsLo[args.Rt];
            if (rtVal != 0)
            {
                uint rsVal = (uint)cpu.RegsLo[args.Rs];
                return $"{CpuHelpers.RegName(args.Rd)} ← {CpuHelpers.RegName(args.Rs)}=0x{rsVal:X8}  [MOVED]";
            }
            return $"skip  ({CpuHelpers.RegName(args.Rt)} = 0)";
        }
    }

    // LUI - Load upper immediate
    public sealed class LUI : Instruction<Args_I>
    {
        public LUI()
        {
            Opcode = 0x0F;
            Funct = null;
            Name = "lui";
            ArgType = InstrArgType.I;
            ArgNames = ["rt", "imm"];
            Equation = "rt = imm << 16";
            Description = "Load upper immediate - loads 16-bit immediate into upper half of register";
        }

        public override void Exec(CpuState cpu, Args_I args)
        {
            // Sign-extend the result to 64-bit
            int result = (int)(args.Imm << 16);
            cpu.RegsLo[args.Rt] = (ulong)(long)result;
            cpu.RegsHi[args.Rt] = 0;
        }

        public override string Disasm(Args_I args, uint? pc = null)
        {
            return $"{Name} {CpuHelpers.RegName(args.Rt)}, ${args.Imm:X4}";
        }

        public override uint Asm(int[] args)
        {
            return (uint)(Opcode << 26 | args[0] << 16 | args[1] & 0xFFFF);
        }

        public override string Analyze(Args_I args, CpuState? cpu = null, uint? pc = null)
        {
            return $"{CpuHelpers.RegName(args.Rt)} = 0x{(uint)(args.Imm << 16):X8}";
        }
    }

    // NOP - No operation
    public sealed class NOP : Instruction<Args_R>
    {
        public NOP()
        {
            Opcode = 0x00;
            Funct = 0x00;
            Name = "nop";
            ArgType = InstrArgType.R;
            ArgNames = [];
            Equation = ""; // No operation
            Description = "No operation - does nothing (equivalent to SLL $zero, $zero, 0)";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Do nothing
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            return Name;
        }

        public override uint Asm(int[] args)
        {
            return 0x00000000; // All zeros
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return string.Empty;
        }
    }

    // SYNC - Memory synchronization
    public sealed class SYNC : Instruction<Args_R>
    {
        public SYNC()
        {
            Opcode = 0x00;
            Funct = 0x0F;
            Name = "sync";
            ArgType = InstrArgType.R;
            ArgNames = ["stype"];
            Equation = "sync_memory()";
            Description = "Memory synchronization - ensures all loads/stores are completed before proceeding";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            // Memory synchronization barrier
            // In a real implementation, this would ensure all pending memory operations
            // are completed before continuing execution
            // For emulation purposes, this is typically a no-op as operations are already sequential
            System.Threading.Thread.MemoryBarrier();
        }

        public override string Disasm(Args_R args, uint? pc = null)
        {
            // SYNC can have an optional stype field (bits 10-6)
            if (args.Shamt != 0)
            {
                return $"{Name} {args.Shamt}";
            }
            return Name;
        }

        public override uint Asm(int[] args)
        {
            // stype is in the shamt field (bits 10-6)
            int stype = args.Length > 0 ? args[0] : 0;
            return (uint)(stype << 6 | (Funct ?? 0x0F));
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            return string.Empty;
        }
    }

    // MFSA - Move From SA register (SPECIAL funct=0x28)
    public sealed class MFSA : Instruction<Args_R>
    {
        public MFSA()
        {
            Opcode = 0x00; Funct = 0x28; Name = "mfsa";
            ArgType = InstrArgType.R; ArgNames = ["rd"];
            Equation = "rd = SA";
            Description = "Move From SA register - reads the SA (shift amount) register";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            if (args.Rd != 0) cpu.RegsLo[args.Rd] = (ulong)cpu.SA;
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rd)}";

        public override uint Asm(int[] args)
            => (uint)(args[0] << 11 | 0x28);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu == null) return string.Empty;
            return $"{CpuHelpers.RegName(args.Rd)} = SA=0x{cpu.SA:X}";
        }
    }

    // MTSA - Move To SA register (SPECIAL funct=0x29)
    public sealed class MTSA : Instruction<Args_R>
    {
        public MTSA()
        {
            Opcode = 0x00; Funct = 0x29; Name = "mtsa";
            ArgType = InstrArgType.R; ArgNames = ["rs"];
            Equation = "SA = rs";
            Description = "Move To SA register - writes to the SA (shift amount) register";
        }

        public override void Exec(CpuState cpu, Args_R args)
        {
            cpu.SA = (int)cpu.RegsLo[args.Rs];
        }

        public override string Disasm(Args_R args, uint? pc = null)
            => $"{Name} {CpuHelpers.RegName(args.Rs)}";

        public override uint Asm(int[] args)
            => (uint)(args[0] << 21 | 0x29);

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            if (cpu == null) return string.Empty;
            return $"SA = {CpuHelpers.RegName(args.Rs)}=0x{(uint)cpu.RegsLo[args.Rs]:X8}";
        }
    }
}
