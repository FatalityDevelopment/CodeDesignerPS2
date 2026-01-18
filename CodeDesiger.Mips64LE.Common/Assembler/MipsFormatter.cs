using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CodeDesigner.Mips64LE.Common
{
    /// <summary>
    /// Utility class for formatting MIPS assembly and machine code in various formats
    /// </summary>
    public static class MipsFormatter
    {
        /// <summary>
        /// Format a single instruction as "ADDRESS HEXCODE" (e.g., "200A0000 27BDFFD0")
        /// </summary>
        /// <param name="address">Instruction address</param>
        /// <param name="instruction">32-bit instruction word</param>
        /// <returns>Formatted string with address and hex code</returns>
        public static string FormatHexDump(uint address, uint instruction)
        {
            return $"{address:X8} {instruction:X8}";
        }

        /// <summary>
        /// Format a block of instructions as hex dump
        /// </summary>
        /// <param name="startAddress">Starting address</param>
        /// <param name="instructions">Array of 32-bit instruction words</param>
        /// <returns>Array of formatted hex dump lines</returns>
        public static string[] FormatHexDump(uint startAddress, uint[] instructions)
        {
            var result = new List<string>();
            uint address = startAddress;
            
            foreach (var instr in instructions)
            {
                result.Add(FormatHexDump(address, instr));
                address += 4;
            }
            
            return result.ToArray();
        }

        /// <summary>
        /// Parse a hex dump line (e.g., "200A0000 27BDFFD0") into address and instruction
        /// </summary>
        /// <param name="line">Hex dump line</param>
        /// <returns>Tuple of (address, instruction)</returns>
        public static (uint address, uint instruction) ParseHexDump(string line)
        {
            var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length != 2)
            {
                throw new ArgumentException($"Invalid hex dump format: {line}");
            }
            
            uint address = Convert.ToUInt32(parts[0], 16);
            uint instruction = Convert.ToUInt32(parts[1], 16);
            
            return (address, instruction);
        }

        /// <summary>
        /// Parse multiple hex dump lines
        /// </summary>
        /// <param name="lines">Array of hex dump lines</param>
        /// <returns>Array of (address, instruction) tuples</returns>
        public static (uint address, uint instruction)[] ParseHexDump(string[] lines)
        {
            var result = new List<(uint, uint)>();
            
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("#"))
                {
                    result.Add(ParseHexDump(trimmed));
                }
            }
            
            return result.ToArray();
        }

        /// <summary>
        /// Disassemble hex dump lines into assembly
        /// </summary>
        /// <param name="lines">Hex dump lines</param>
        /// <returns>Array of disassembled instructions with addresses</returns>
        public static string[] DisassembleHexDump(string[] lines)
        {
            var result = new List<string>();
            
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                {
                    continue;
                }
                
                var (address, instruction) = ParseHexDump(trimmed);
                string disasm = MipsDisassembler.Disassemble(instruction, address);
                result.Add($"{address:X8} {instruction:X8}  {disasm}");
            }
            
            return result.ToArray();
        }

        /// <summary>
        /// Assemble instructions and format as hex dump
        /// </summary>
        /// <param name="startAddress">Starting address</param>
        /// <param name="asmLines">Assembly source lines</param>
        /// <returns>Array of hex dump lines</returns>
        public static string[] AssembleToHexDump(uint startAddress, string[] asmLines)
        {
            var instructions = MipsAssembler.AssembleBlock(asmLines);
            return FormatHexDump(startAddress, instructions);
        }
    }
}
