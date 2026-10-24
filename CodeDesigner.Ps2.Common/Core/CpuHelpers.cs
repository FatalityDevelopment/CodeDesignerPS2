using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Ps2.Common.CPU
{
    public static class CpuHelpers
    {
        private static readonly string[] RegNames =
        {
            "zero", "at", "v0", "v1", "a0", "a1", "a2", "a3",
            "t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7",
            "s0", "s1", "s2", "s3", "s4", "s5", "s6", "s7",
            "t8", "t9", "k0", "k1", "gp", "sp", "fp", "ra"
        };

        /// <summary>
        /// Get the symbolic name of a general-purpose register.
        /// </summary>
        public static string RegName(int r)
        {
            if (r >= 0 && r < RegNames.Length)
                return RegNames[r];
            return $"r{r}";
        }

        /// <summary>
        /// Format an immediate value in PS2 MIPS assembler style (with $ prefix)
        /// </summary>
        /// <param name="imm">Immediate value (16-bit)</param>
        /// <returns>Formatted immediate string (e.g., "$FFD0")</returns>
        public static string FormatImmediate(int imm)
        {
            return $"${imm & 0xFFFF:X4}";
        }

        /// <summary>
        /// Format a load/store offset in PS2 <c>$</c>-hex style, signed:
        /// -48 becomes <c>-$0030</c>, 16 becomes <c>$0010</c>.
        /// </summary>
        /// <param name="offset">Offset value (already sign-extended or raw 16-bit)</param>
        public static string FormatOffset(int offset)
        {
            return FormatSignedImmediate(offset);
        }

        /// <summary>
        /// Format a sign-extended 16-bit immediate (addiu, slti, offsets) in <c>$</c>-hex:
        /// <c>-$0010</c> / <c>$0010</c>. The assembler accepts the same spelling.
        /// </summary>
        public static string FormatSignedImmediate(int imm16)
        {
            int v = SignExtend16(imm16);
            return v < 0 ? $"-${-v:X4}" : $"${v:X4}";
        }

        /// <summary>
        /// Format a load/store analysis memory reference.
        /// <para>
        /// When the base register holds a known runtime value, returns the resolved
        /// 32-bit address (<c>0xXXXXXXXX</c>) so the comment line up with PCSX2's
        /// live state.  Otherwise returns <c>"baseReg + 0xNNNN"</c> — never the bare
        /// offset, which would make a 16-bit displacement look like an address.
        /// The <c>$zero</c> base register is treated as known (its value is always 0).
        /// </para>
        /// </summary>
        public static string FormatMemoryReference(CpuState? cpu, int baseReg, int offset)
        {
            ushort disp = (ushort)(offset & 0xFFFF);
            string sign = (short)disp < 0 ? "-" : "+";
            string absHex = $"0x{(uint)System.Math.Abs((short)disp):X4}";

            if (TryGetEffectiveAddress(cpu, baseReg, offset, out uint addr))
            {
                return $"0x{addr:X8}";
            }

            return $"{RegName(baseReg)} {sign} {absHex}";
        }

        /// <summary>
        /// Resolves <c>base + offset</c> when the base register's value is known.
        /// </summary>
        public static bool TryGetEffectiveAddress(CpuState? cpu, int baseReg, int offset, out uint addr)
        {
            addr = 0;
            if (cpu == null || !IsRegisterKnown(cpu, baseReg)) return false;
            addr = (uint)((int)cpu.RegsLo[baseReg] + offset);
            return true;
        }

        /// <summary>
        /// Format an indexed load/store reference, resolving only when both source
        /// registers carry known values in the analysis state.
        /// </summary>
        public static string FormatIndexedMemoryReference(CpuState? cpu, int baseReg, int indexReg, bool alignWord = false)
        {
            if (cpu != null && IsRegisterKnown(cpu, baseReg) && IsRegisterKnown(cpu, indexReg))
            {
                ulong addr = cpu.RegsLo[baseReg] + cpu.RegsLo[indexReg];
                if (alignWord)
                {
                    addr &= ~3UL;
                }

                return $"0x{(uint)addr:X8}";
            }

            string reference = $"{RegName(baseReg)} + {RegName(indexReg)}";
            return alignWord ? $"({reference}) & ~3" : reference;
        }

        /// <summary>
        /// Format a GPR load analysis comment from the state <em>before</em> the
        /// load executes. The loaded value is read through <see cref="CpuState.Bus"/>
        /// when the address is known and a bus is attached; otherwise only the
        /// address is shown. <paramref name="size"/> is the access width in bytes
        /// (1, 2, 4 or 8); pass 0 for partial loads (lwl/lwr/ldl/ldr/lq) whose
        /// result can't be shown as one value.
        /// </summary>
        public static string FormatGprLoadAnalysis(CpuState? cpu, int targetReg, int baseReg, int offset,
            int size = 4, bool signed = true)
        {
            if (cpu == null) return string.Empty;
            string address = FormatMemoryReference(cpu, baseReg, offset);
            string text = $"{RegName(targetReg)} ← [{address}]";
            if (size > 0 && cpu.Bus != null && TryGetEffectiveAddress(cpu, baseReg, offset, out uint addr))
            {
                try
                {
                    ulong value = size switch
                    {
                        1 => signed ? (ulong)(long)(sbyte)cpu.Bus.ReadByte(addr) : cpu.Bus.ReadByte(addr),
                        2 => signed ? (ulong)(long)(short)cpu.Bus.ReadHalf(addr) : cpu.Bus.ReadHalf(addr),
                        4 => signed ? (ulong)(long)(int)cpu.Bus.ReadWord(addr) : cpu.Bus.ReadWord(addr),
                        _ => cpu.Bus.ReadDoubleWord(addr),
                    };
                    text += size == 8 ? $" = 0x{value:X16}" : $" = {FormatRegisterValueHex(value)}";
                }
                catch { /* unreadable address — show the reference only */ }
            }
            return text;
        }

        /// <summary>
        /// Format a GPR store analysis comment using the source register value.
        /// The value is omitted when the source register is unknown.
        /// </summary>
        public static string FormatGprStoreAnalysis(CpuState? cpu, int sourceReg, int baseReg, int offset)
        {
            if (cpu == null) return string.Empty;
            string address = FormatMemoryReference(cpu, baseReg, offset);
            return IsRegisterKnown(cpu, sourceReg)
                ? $"[{address}] ← {RegName(sourceReg)} = {FormatRegisterValueHex(cpu.RegsLo[sourceReg])}"
                : $"[{address}] ← {RegName(sourceReg)}";
        }

        /// <summary>
        /// True when the analysis state knows <paramref name="reg"/>'s value
        /// (<see cref="CpuState.GprKnown"/>). <c>$zero</c> is always known.
        /// </summary>
        public static bool IsRegisterKnown(CpuState cpu, int reg)
        {
            return reg == 0 || ((cpu.GprKnown >> reg) & 1u) != 0;
        }

        /// <summary>
        /// Format a 16-bit load/store displacement for analysis comments.
        /// </summary>
        public static string FormatMemoryOffset(int offset)
        {
            return $"0x{offset & 0xFFFF:X4}";
        }

        /// <summary>
        /// Format a 64-bit register value as a 32-bit zero-padded hex word for
        /// comment annotations (e.g. call argument dumps).
        /// </summary>
        public static string FormatRegisterValueHex(ulong value)
        {
            return $"0x{(uint)value:X8}";
        }

        /// <summary>
        /// Target of a <c>j</c>/<c>jal</c> at <paramref name="pc"/>: the upper four bits
        /// of <c>pc + 4</c> with the 26-bit index shifted in. IOP code viewed through
        /// the EE mirror (0x1C000000–0x1C7FFFFF) really runs at 0x00000000, so its
        /// targets are mapped back into the mirror instead of into EE hardware space.
        /// </summary>
        public static uint JumpTarget(uint pc, uint index26)
        {
            uint low = (index26 & 0x03FFFFFFu) << 2;
            if (pc >= 0x1C000000u && pc < 0x1C800000u)
                return 0x1C000000u | (low & 0x007FFFFFu);
            return ((pc + 4) & 0xF0000000u) | low;
        }

        /// <summary>
        /// The 20-bit code field (bits 25:6) of <c>syscall</c>/<c>break</c>,
        /// reassembled from the R-type fields it overlaps.
        /// </summary>
        public static uint CodeField(CodeDesigner.Ps2.Common.Cpu.InstrArgs.Args_R args)
        {
            return ((uint)args.Rs << 15) | ((uint)args.Rt << 10) | ((uint)args.Rd << 5) | (uint)args.Shamt;
        }

        /// <summary>
        /// Target of a PC-relative branch at <paramref name="pc"/>.
        /// </summary>
        public static uint BranchTarget(uint pc, int imm16)
        {
            return (uint)(pc + 4 + (SignExtend16(imm16) << 2));
        }

        /// <summary>
        /// Formats raw IEEE-754 single bits as a float hint, e.g. <c>1.5f (0x3FC00000)</c>.
        /// </summary>
        public static string FormatFloat(uint bits)
        {
            float f = BitConverter.UInt32BitsToSingle(bits);
            string text = float.IsFinite(f)
                ? f.ToString("0.0######", System.Globalization.CultureInfo.InvariantCulture) + "f"
                : f.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return $"{text} (0x{bits:X8})";
        }

        /// <summary>
        /// Sign-extend a 16-bit value to 32-bit integer.
        /// </summary>
        public static int SignExtend16(int val)
        {
            return (short)(val & 0xFFFF); // Cast to short automatically sign-extends
        }

        /// <summary>
        /// Sign-extend an 8-bit value to 32-bit integer.
        /// </summary>
        public static int SignExtend8(int val)
        {
            return (sbyte)(val & 0xFF); // Cast to sbyte automatically sign-extends
        }

        /// <summary>
        /// Get the lower 32 bits of a 128-bit register.
        /// </summary>
        public static int GetReg32(CpuState cpu, int reg)
        {
            // Mask lower 32 bits
            ulong val = cpu.RegsLo[reg] & 0xFFFFFFFFUL;
            return (int)val;
        }

        /// <summary>
        /// Set a 32-bit value in a 128-bit register, sign-extended to 64-bit.
        /// </summary>
        public static void SetReg32(CpuState cpu, int reg, int val)
        {
            if (reg == 0) return; // $zero is always 0

            long signedVal = val; // Sign-extend to 64-bit
            cpu.RegsLo[reg] = (ulong)signedVal;
            cpu.RegsHi[reg] = signedVal < 0 ? 0xFFFFFFFFFFFFFFFFUL : 0UL;
        }
    }
}
