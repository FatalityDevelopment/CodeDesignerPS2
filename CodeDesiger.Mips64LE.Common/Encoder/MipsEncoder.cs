using CodeDesigner.Mips64LE.Cpu.InstrArgs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE
{
    /// <summary>
    /// Encodes MIPS instruction fields into 32-bit instruction words
    /// </summary>
    public class MipsEncoder
    {
        /// <summary>
        /// Encode an R-type instruction
        /// </summary>
        /// <param name="opcode">6-bit opcode</param>
        /// <param name="rs">5-bit source register</param>
        /// <param name="rt">5-bit target register</param>
        /// <param name="rd">5-bit destination register</param>
        /// <param name="shamt">5-bit shift amount</param>
        /// <param name="funct">6-bit function code</param>
        /// <returns>32-bit encoded instruction</returns>
        public static uint EncodeR(int opcode, int rs, int rt, int rd, int shamt, int funct)
        {
            return (uint)((opcode & 0x3F) << 26 | 
                          (rs & 0x1F) << 21 | 
                          (rt & 0x1F) << 16 | 
                          (rd & 0x1F) << 11 | 
                          (shamt & 0x1F) << 6 | 
                          (funct & 0x3F));
        }

        /// <summary>
        /// Encode an R-type instruction from Args_R structure
        /// </summary>
        /// <param name="opcode">6-bit opcode</param>
        /// <param name="args">R-type arguments</param>
        /// <param name="funct">6-bit function code</param>
        /// <returns>32-bit encoded instruction</returns>
        public static uint EncodeR(int opcode, Args_R args, int funct)
        {
            return EncodeR(opcode, args.Rs, args.Rt, args.Rd, args.Shamt, funct);
        }

        /// <summary>
        /// Encode an I-type instruction
        /// </summary>
        /// <param name="opcode">6-bit opcode</param>
        /// <param name="rs">5-bit source register</param>
        /// <param name="rt">5-bit target register</param>
        /// <param name="immediate">16-bit immediate value</param>
        /// <returns>32-bit encoded instruction</returns>
        public static uint EncodeI(int opcode, int rs, int rt, int immediate)
        {
            return (uint)((opcode & 0x3F) << 26 | 
                          (rs & 0x1F) << 21 | 
                          (rt & 0x1F) << 16 | 
                          (immediate & 0xFFFF));
        }

        /// <summary>
        /// Encode an I-type instruction from Args_I structure
        /// </summary>
        /// <param name="opcode">6-bit opcode</param>
        /// <param name="args">I-type arguments</param>
        /// <returns>32-bit encoded instruction</returns>
        public static uint EncodeI(int opcode, Args_I args)
        {
            return EncodeI(opcode, args.Rs, args.Rt, args.Imm);
        }

        /// <summary>
        /// Encode a J-type instruction
        /// </summary>
        /// <param name="opcode">6-bit opcode</param>
        /// <param name="address">26-bit address</param>
        /// <returns>32-bit encoded instruction</returns>
        public static uint EncodeJ(int opcode, uint address)
        {
            return (uint)(((uint)opcode & 0x3F) << 26 | (address & 0x03FFFFFF));
        }

        /// <summary>
        /// Encode a J-type instruction from Args_J structure
        /// </summary>
        /// <param name="opcode">6-bit opcode</param>
        /// <param name="args">J-type arguments</param>
        /// <returns>32-bit encoded instruction</returns>
        public static uint EncodeJ(int opcode, Args_J args)
        {
            return EncodeJ(opcode, args.Addr);
        }
    }
}
