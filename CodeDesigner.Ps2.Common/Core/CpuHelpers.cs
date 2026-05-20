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
        /// Format an offset for load/store instructions in PS2 MIPS assembler style
        /// Note: Negative offsets are displayed as 16-bit hex (e.g., -48 becomes $FFD0)
        /// This matches PS2 MIPS assembler convention where all immediates are shown in hex
        /// </summary>
        /// <param name="offset">Offset value</param>
        /// <returns>Formatted offset string (e.g., "$FFD0")</returns>
        public static string FormatOffset(int offset)
        {
            return $"${offset & 0xFFFF:X4}";
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

            if (cpu != null)
            {
                ulong baseLo = cpu.RegsLo[baseReg];
                ulong baseHi = cpu.RegsHi[baseReg];
                // $zero is always known. Any other register that is still 0 (lo+hi)
                // in the cloned analysis state is treated as unresolved.
                bool baseKnown = baseReg == 0 || baseLo != 0 || baseHi != 0;
                if (baseKnown)
                {
                    uint addr = (uint)((int)baseLo + offset);
                    return $"0x{addr:X8}";
                }
            }

            return $"{RegName(baseReg)} {sign} {absHex}";
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
        /// Format a GPR load analysis comment after the instruction has executed.
        /// </summary>
        public static string FormatGprLoadAnalysis(CpuState? cpu, int targetReg, int baseReg, int offset)
        {
            if (cpu == null) return string.Empty;
            string address = FormatMemoryReference(cpu, baseReg, offset);
            return $"{RegName(targetReg)} ← [{address}] = {FormatRegisterValueHex(cpu.RegsLo[targetReg])}";
        }

        /// <summary>
        /// Format a GPR store analysis comment using the source register value.
        /// </summary>
        public static string FormatGprStoreAnalysis(CpuState? cpu, int sourceReg, int baseReg, int offset)
        {
            if (cpu == null) return string.Empty;
            string address = FormatMemoryReference(cpu, baseReg, offset);
            return $"[{address}] ← {RegName(sourceReg)} = {FormatRegisterValueHex(cpu.RegsLo[sourceReg])}";
        }

        private static bool IsRegisterKnown(CpuState cpu, int reg)
        {
            return reg == 0 || cpu.RegsLo[reg] != 0 || cpu.RegsHi[reg] != 0;
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
