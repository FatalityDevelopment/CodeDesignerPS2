using CodeDesigner.Mips64LE.Common.Cpu.InstrArgs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE.Common
{
    /// <summary>
    /// Decodes 32-bit MIPS instruction words into their component fields
    /// </summary>
    public class MipsDecoder
    {
        /// <summary>
        /// Decode a 32-bit instruction word into R-type arguments
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word</param>
        /// <returns>Decoded R-type arguments</returns>
        public static Args_R DecodeR(uint instruction)
        {
            int rs = (int)((instruction >> 21) & 0x1F);
            int rt = (int)((instruction >> 16) & 0x1F);
            int rd = (int)((instruction >> 11) & 0x1F);
            int shamt = (int)((instruction >> 6) & 0x1F);
            
            return new Args_R(rs, rt, rd, shamt);
        }

        /// <summary>
        /// Decode a 32-bit instruction word into I-type arguments
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word</param>
        /// <returns>Decoded I-type arguments</returns>
        public static Args_I DecodeI(uint instruction)
        {
            int rs = (int)((instruction >> 21) & 0x1F);
            int rt = (int)((instruction >> 16) & 0x1F);
            int imm = (int)(instruction & 0xFFFF);
            
            return new Args_I(rs, rt, imm);
        }

        /// <summary>
        /// Decode a 32-bit instruction word into J-type arguments
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word</param>
        /// <returns>Decoded J-type arguments</returns>
        public static Args_J DecodeJ(uint instruction)
        {
            uint addr = instruction & 0x03FFFFFF;
            
            return new Args_J(addr);
        }

        /// <summary>
        /// Extract the opcode field from a 32-bit instruction
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word</param>
        /// <returns>6-bit opcode</returns>
        public static int GetOpcode(uint instruction)
        {
            return (int)((instruction >> 26) & 0x3F);
        }

        /// <summary>
        /// Extract the funct field from a 32-bit instruction (for R-type)
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word</param>
        /// <returns>6-bit function code</returns>
        public static int GetFunct(uint instruction)
        {
            return (int)(instruction & 0x3F);
        }

        /// <summary>
        /// Extract the rs field from a 32-bit instruction
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word</param>
        /// <returns>5-bit source register</returns>
        public static int GetRs(uint instruction)
        {
            return (int)((instruction >> 21) & 0x1F);
        }

        /// <summary>
        /// Extract the rt field from a 32-bit instruction
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word</param>
        /// <returns>5-bit target register</returns>
        public static int GetRt(uint instruction)
        {
            return (int)((instruction >> 16) & 0x1F);
        }

        /// <summary>
        /// Extract the rd field from a 32-bit instruction
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word</param>
        /// <returns>5-bit destination register</returns>
        public static int GetRd(uint instruction)
        {
            return (int)((instruction >> 11) & 0x1F);
        }

        /// <summary>
        /// Extract the shamt field from a 32-bit instruction
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word</param>
        /// <returns>5-bit shift amount</returns>
        public static int GetShamt(uint instruction)
        {
            return (int)((instruction >> 6) & 0x1F);
        }

        /// <summary>
        /// Extract the immediate field from a 32-bit instruction
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word</param>
        /// <returns>16-bit immediate value</returns>
        public static int GetImmediate(uint instruction)
        {
            return (int)(instruction & 0xFFFF);
        }

        /// <summary>
        /// Extract the address field from a 32-bit instruction (J-type)
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word</param>
        /// <returns>26-bit address</returns>
        public static uint GetAddress(uint instruction)
        {
            return instruction & 0x03FFFFFF;
        }
    }
}
