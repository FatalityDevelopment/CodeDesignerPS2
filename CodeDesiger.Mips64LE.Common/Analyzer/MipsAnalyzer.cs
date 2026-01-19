using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeDesigner.Mips64LE.Common
{
    /// <summary>
    /// Analyzes disassembled MIPS instructions to track register values and identify pointer references
    /// </summary>
    public class MipsAnalyzer
    {
        /// <summary>
        /// Represents a pointer reference found during analysis
        /// </summary>
        public class PointerRef
        {
            public uint InstrAddr { get; set; }         // Address of the instruction making the reference
            public string Kind { get; set; } = "";      // Type: "load", "store", "jump", "call"
            public string BaseReg { get; set; } = "";   // Base register used (e.g., "gp", "sp")
            public uint BaseValue { get; set; }         // Resolved value of base register
            public int Offset { get; set; }             // Offset from base (signed)
            public uint TargetAddr { get; set; }        // Final calculated address
            public int? Size { get; set; }              // Size of access in bytes
            public string? Type { get; set; }           // Specific instruction type
            public bool IsFunction { get; set; }        // Whether target is likely a function
            public string? DataType { get; set; }       // Inferred data type
        }

        /// <summary>
        /// Analyze disassembled instructions to generate comments about pointer references
        /// </summary>
        /// <param name="rows">Array of disassembled instruction data with addr, op, args fields</param>
        /// <returns>Dictionary mapping instruction address to comment string</returns>
        public static Dictionary<uint, string> AnalyzeAndGenerateComments(IEnumerable<DisassembledRow> rows)
        {
            var comments = new Dictionary<uint, string>();
            var regs = new Dictionary<string, uint>();
            var rowList = rows.ToList();

            foreach (var row in rowList)
            {
                var op = row.Op.ToLower();
                var args = row.Args.Split(',').Select(a => a.Trim()).ToArray();

                // LUI - Load Upper Immediate
                if (op == "lui" && args.Length >= 2)
                {
                    var rt = args[0];
                    var imm = ParseImm(args[1]);
                    if (imm.HasValue)
                    {
                        regs[rt] = (uint)(imm.Value << 16);
                    }
                    continue;
                }

                // ADDIU/ADDI - Add Immediate
                if ((op == "addiu" || op == "addi") && args.Length >= 3)
                {
                    var rt = args[0];
                    var rs = args[1];
                    var imm = ParseImm(args[2]);

                    if (imm.HasValue && regs.TryGetValue(rs, out var baseValue))
                    {
                        var signedImm = GetSignedImm(imm.Value);
                        var result = (uint)(baseValue + signedImm);
                        regs[rt] = result;

                        // Generate comment for address calculation
                        comments[row.Addr] = $"; ptr [0x{result:X8}]";
                    }
                    else
                    {
                        regs.Remove(rt);
                    }
                    continue;
                }

                // ORI - OR Immediate
                if (op == "ori" && args.Length >= 3)
                {
                    var rt = args[0];
                    var rs = args[1];
                    var imm = ParseImm(args[2]);

                    if (imm.HasValue && regs.TryGetValue(rs, out var baseValue))
                    {
                        var result = baseValue | (uint)imm.Value;
                        regs[rt] = result;

                        // Generate comment for address calculation
                        comments[row.Addr] = $"; ptr [0x{result:X8}]";
                    }
                    else
                    {
                        regs.Remove(rt);
                    }
                    continue;
                }

                // ADDU/ADD - Add Unsigned
                if ((op == "addu" || op == "add") && args.Length >= 3)
                {
                    var rd = args[0];
                    var rs = args[1];
                    var rt = args[2];

                    if (regs.TryGetValue(rs, out var a) && regs.TryGetValue(rt, out var b))
                    {
                        var result = a + b;
                        regs[rd] = result;
                        comments[row.Addr] = $"; ptr [0x{result:X8}]";
                    }
                    else
                    {
                        regs.Remove(rd);
                    }
                    continue;
                }

                // Memory operations with offset(base) format
                var memArg = args.FirstOrDefault(a => a.Contains("(") && a.Contains(")"));
                if (memArg != null)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(memArg, @"\$([0-9A-Fa-f]+)\((\w+)\)");
                    if (match.Success)
                    {
                        var offsetStr = match.Groups[1].Value;
                        var baseReg = match.Groups[2].Value;
                        var offset = ParseImm("$" + offsetStr);

                        if (offset.HasValue && regs.TryGetValue(baseReg, out var baseValue))
                        {
                            var signedOffset = GetSignedImm(offset.Value);
                            var targetAddr = (uint)(baseValue + signedOffset);

                            // Determine if it's likely a function
                            var isFunction = IsLikelyFunction(targetAddr, rowList);

                            if (isFunction)
                            {
                                comments[row.Addr] = $"; ptr to function at 0x{targetAddr:X8}";
                            }
                            else
                            {
                                comments[row.Addr] = $"; ptr [0x{targetAddr:X8}]";
                            }
                        }
                    }
                    continue;
                }

                // JAL - Jump and Link (function call)
                if (op == "jal" && args.Length >= 1)
                {
                    var targetMatch = System.Text.RegularExpressions.Regex.Match(args[0], @"\$([0-9A-Fa-f]+)");
                    if (targetMatch.Success)
                    {
                        var targetAddrStr = targetMatch.Groups[1].Value;
                        if (uint.TryParse(targetAddrStr, System.Globalization.NumberStyles.HexNumber, null, out var targetAddr))
                        {
                            comments[row.Addr] = $"; call 0x{targetAddr:X8}";
                        }
                    }
                    continue;
                }

                // Clear destination register for most other operations
                if (args.Length > 0 && !op.StartsWith("b") && op != "j" && op != "jal")
                {
                    var destReg = args[0];
                    if (string.IsNullOrEmpty(memArg) && !string.IsNullOrEmpty(destReg) && !destReg.Contains("("))
                    {
                        var handledOps = new[] { "lui", "addiu", "addi", "addu", "add", "ori", "nop", "syscall", "break", "sync", "jr", "jalr" };
                        if (!handledOps.Contains(op))
                        {
                            regs.Remove(destReg);
                        }
                    }
                }
            }

            return comments;
        }

        /// <summary>
        /// Parse hexadecimal immediate value from string (e.g., "$1234" -> 0x1234)
        /// </summary>
        private static int? ParseImm(string s)
        {
            var cleaned = s.Replace("$", "").Trim();
            if (int.TryParse(cleaned, System.Globalization.NumberStyles.HexNumber, null, out var result))
            {
                return result;
            }
            return null;
        }

        /// <summary>
        /// Convert 16-bit value to signed integer
        /// </summary>
        private static int GetSignedImm(int val)
        {
            return (short)(val & 0xFFFF);
        }

        /// <summary>
        /// Check if an address is likely a function entry point
        /// </summary>
        private static bool IsLikelyFunction(uint addr, List<DisassembledRow> rows)
        {
            var targetRow = rows.FirstOrDefault(r => r.Addr == addr);
            if (targetRow == null) return false;

            var op = targetRow.Op.ToLower();
            var args = targetRow.Args.ToLower();

            // Stack frame allocation: addiu sp, sp, -X
            if (op == "addiu" && args.Contains("sp, sp") && args.Contains("-"))
            {
                return true;
            }

            // Save return address: sw ra, X(sp)
            if (op == "sw" && args.Contains("ra") && args.Contains("sp"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Represents a disassembled instruction row with address, opcode, and arguments
        /// </summary>
        public class DisassembledRow
        {
            public uint Addr { get; set; }
            public string Op { get; set; } = "";
            public string Args { get; set; } = "";
        }
    }
}
