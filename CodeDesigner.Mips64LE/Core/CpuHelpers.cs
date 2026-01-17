using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE.CPU
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
