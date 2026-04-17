using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CodeDesigner.Ps2.Common.Interfaces;
using CodeDesigner.Ps2.Common.CPU;

namespace CodeDesigner.Ps2.Common
{
    /// <summary>
    /// Analyzes MIPS instructions using the built-in Analyze() method on each instruction class
    /// </summary>
    public class MipsAnalyzer
    {
        /// <summary>
        /// Analyze instructions and generate comments using the instruction's built-in Analyze() method
        /// </summary>
        /// <param name="instructions">List of instruction words and their addresses</param>
        /// <param name="startPc">Starting program counter</param>
        /// <param name="executeInstructions">If true, executes instructions to track register state</param>
        /// <param name="bus">Optional memory bus used to peek at branch/jump target addresses for string hints.</param>
        /// <param name="lookupLabel">Optional callback that returns a label name for a given address, or null if none.</param>
        /// <returns>Dictionary mapping instruction address to comment string</returns>
        public static Dictionary<uint, string> AnalyzeInstructions(
            IEnumerable<(uint instruction, uint address)> instructions, 
            uint startPc = 0,
            bool executeInstructions = true,
            IPs2MemoryBus? bus = null,
            Func<uint, string?>? lookupLabel = null)
        {
            var comments = new Dictionary<uint, string>();
            
            // Create CPU state for tracking register values during execution
            CpuState? cpu = executeInstructions ? new CpuState() : null;
            
            uint currentPc = startPc;
            
            foreach (var (instruction, address) in instructions)
            {
                try
                {
                    // Get the instruction object and analyze it
                    var (instrObj, args) = MipsDisassembler.GetInstructionObject(instruction);
                    
                    if (instrObj != null && args != null)
                    {
                        // Compute branch/jump target before execution (register state is still pre-exec)
                        uint? target = (bus != null || lookupLabel != null)
                            ? TryGetBranchJumpTarget(instruction, currentPc, cpu)
                            : null;

                        // Execute the instruction to update CPU state (if enabled)
                        if (cpu != null && executeInstructions)
                        {
                            try
                            {
                                instrObj.ExecuteGeneric(cpu, args);
                            }
                            catch
                            {
                                // Silently ignore execution errors (e.g., syscall, break, etc.)
                            }
                        }
                        
                        // Call the instruction's Analyze method to get analysis comment
                        string analysis = instrObj.AnalyzeGeneric(args, cpu, currentPc);
                        
                        // Only add comment if analysis returned something meaningful
                        if (!string.IsNullOrWhiteSpace(analysis))
                        {
                            // Append label/string hint for branch/jump targets when available
                            if (target.HasValue)
                            {
                                string? hint = BuildTargetHint(target.Value, bus, lookupLabel);
                                if (hint != null)
                                    analysis += $" => {hint}";
                            }

                            comments[address] = $"; {analysis}";
                        }
                    }
                }
                catch
                {
                    // Skip instructions that fail to analyze
                }
                
                currentPc += 4;
            }

            return comments;
        }

        /// <summary>
        /// Analyze a single instruction using the supplied (pre-execution) CPU state.
        /// Returns the analysis comment string, or an empty string when no analysis
        /// is available for the given instruction.
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word.</param>
        /// <param name="pc">The virtual address of the instruction.</param>
        /// <param name="cpu">CPU state captured immediately before the instruction executed.</param>
        /// <param name="bus">Optional memory bus used to peek at the branch/jump target for a string hint.</param>
        /// <param name="lookupLabel">Optional callback that returns a label name for a given address, or null if none.</param>
        public static string AnalyzeSingleInstruction(uint instruction, uint pc, CpuState cpu,
            IPs2MemoryBus? bus = null,
            Func<uint, string?>? lookupLabel = null)
        {
            try
            {
                var (instrObj, args) = MipsDisassembler.GetInstructionObject(instruction);
                if (instrObj == null || args == null)
                    return string.Empty;

                string analysis = instrObj.AnalyzeGeneric(args, cpu, pc);
                if (string.IsNullOrWhiteSpace(analysis))
                    return string.Empty;

                // Append label/string hint for branch/jump targets when bus or label lookup is available
                if (bus != null || lookupLabel != null)
                {
                    uint? target = TryGetBranchJumpTarget(instruction, pc, cpu);
                    if (target.HasValue)
                    {
                        string? hint = BuildTargetHint(target.Value, bus, lookupLabel);
                        if (hint != null)
                            analysis += $" => {hint}";
                    }
                }

                // Check for LUI+ADDIU or LUI+ORI patterns and append pointer target hint
                uint? pointerTarget = TryGetPointerLoadTarget(instruction, cpu);
                if (pointerTarget.HasValue && (lookupLabel != null || bus != null))
                {
                    string? hint = BuildTargetHint(pointerTarget.Value, bus, lookupLabel);
                    if (hint != null)
                        analysis += $" → 0x{pointerTarget.Value:X8} {hint}";
                    else
                        analysis += $" → 0x{pointerTarget.Value:X8}";
                }

                return $"; {analysis}";
            }
            catch
            {
                return string.Empty;
            }
        }

        // -----------------------------------------------------------------------
        // Branch / jump target resolution helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Attempts to detect LUI+ADDIU or LUI+ORI pointer load patterns.
        /// When an ADDIU or ORI instruction uses a register that was recently loaded
        /// with LUI, returns the combined 32-bit address.
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word (ADDIU or ORI).</param>
        /// <param name="cpu">CPU state with register values from prior LUI.</param>
        /// <returns>The combined pointer address, or null if not a pointer load pattern.</returns>
        private static uint? TryGetPointerLoadTarget(uint instruction, CpuState? cpu)
        {
            if (cpu == null) return null;

            uint opcode = (instruction >> 26) & 0x3Fu;
            
            // ADDIU = opcode 0x09, ORI = opcode 0x0D
            if (opcode != 0x09u && opcode != 0x0Du)
                return null;

            uint rs = (instruction >> 21) & 0x1Fu;  // source register
            uint rt = (instruction >> 16) & 0x1Fu;  // target register
            ushort imm = (ushort)(instruction & 0xFFFFu);

            // For ADDIU: addr = (rs upper 16 bits) + sign_extend(imm)
            // For ORI:   addr = (rs upper 16 bits) | imm
            // Check if rs contains a value that looks like it came from LUI
            // (upper 16 bits set, lower 16 bits zero or near-zero)
            ulong rsValue = cpu.RegsLo[rs];
            uint rsUpper = (uint)(rsValue >> 16) & 0xFFFFu;
            uint rsLower = (uint)rsValue & 0xFFFFu;

            // LUI sets upper 16 bits and zeros lower 16 bits
            // We accept a small tolerance for the lower bits (≤ 0x100) to handle
            // cases where there may have been a small offset added
            if (rsUpper == 0 || rsLower > 0x100)
                return null;

            if (opcode == 0x09u) // ADDIU
            {
                // Sign-extend the immediate for ADDIU
                int offset = (short)imm;
                uint addr = (uint)((long)rsValue + offset);
                return addr;
            }
            else // ORI = 0x0D
            {
                // OR the immediate (zero-extend)
                uint addr = (uint)rsValue | imm;
                return addr;
            }
        }

        /// <summary>
        /// Attempts to compute the target address of a branch or jump instruction.
        /// Returns <c>null</c> when the instruction is not a branch/jump or the
        /// target cannot be determined without a CPU state.
        /// </summary>
        /// <param name="instruction">The 32-bit instruction word.</param>
        /// <param name="pc">The virtual PC of the instruction.</param>
        /// <param name="cpu">Optional CPU state used to resolve register-based targets (JR/JALR).</param>
        public static uint? TryGetBranchJumpTarget(uint instruction, uint pc, CpuState? cpu)
        {
            uint opcode = (instruction >> 26) & 0x3Fu;

            switch (opcode)
            {
                case 0x02: // J
                case 0x03: // JAL
                {
                    uint addr = instruction & 0x03FFFFFFu;
                    return ((pc + 4) & 0xF0000000u) | (addr << 2);
                }

                case 0x04: // BEQ
                case 0x05: // BNE
                case 0x06: // BLEZ
                case 0x07: // BGTZ
                case 0x14: // BEQL
                case 0x15: // BNEL
                case 0x16: // BLEZL
                case 0x17: // BGTZL
                {
                    int offset = (short)(instruction & 0xFFFFu);
                    return (uint)(pc + 4 + (offset << 2));
                }

                case 0x01: // REGIMM
                {
                    uint rt = (instruction >> 16) & 0x1Fu;
                    // BLTZ=0x00, BGEZ=0x01, BLTZL=0x02, BGEZL=0x03
                    // BLTZAL=0x10, BGEZAL=0x11, BLTZALL=0x12, BGEZALL=0x13
                    bool isBranch = rt <= 0x03u || (rt >= 0x10u && rt <= 0x13u);
                    if (isBranch)
                    {
                        int offset = (short)(instruction & 0xFFFFu);
                        return (uint)(pc + 4 + (offset << 2));
                    }
                    break;
                }

                case 0x00: // SPECIAL
                {
                    uint funct = instruction & 0x3Fu;
                    if ((funct == 0x08u || funct == 0x09u) && cpu != null) // JR / JALR
                    {
                        uint rs = (instruction >> 21) & 0x1Fu;
                        return (uint)cpu.RegsLo[rs];
                    }
                    break;
                }
            }

            return null;
        }

        /// <summary>
        /// Tries to read a null-terminated printable ASCII string from
        /// <paramref name="address"/> in <paramref name="bus"/>.
        /// Returns <c>null</c> when the content is not a recognizable string.
        /// </summary>
        private static string? TryPeekStringAtAddress(IPs2MemoryBus bus, uint address)
        {
            const int MaxLen = 64; // reasonable upper bound for typical in-game string literals
            const int MinLen = 2;  // exclude trivial single-character matches
            try
            {
                var sb = new StringBuilder(MaxLen);
                uint cursor = address;
                for (int i = 0; i < MaxLen; i++, cursor++)
                {
                    byte b = bus.ReadByte(cursor);
                    if (b == 0) break;
                    if (b < 0x20 || b > 0x7E) return null; // non-printable byte — not a string
                    sb.Append((char)b);
                }
                return sb.Length >= MinLen ? sb.ToString() : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Builds a human-readable hint for a branch/jump <paramref name="target"/> address
        /// by checking the label database first, then peeking at memory for a string.
        /// Returns <c>null</c> when no useful hint can be produced.
        /// </summary>
        private static string? BuildTargetHint(uint target, IPs2MemoryBus? bus, Func<uint, string?>? lookupLabel)
        {
            // Prefer a symbolic label over a raw memory peek
            if (lookupLabel != null)
            {
                string? label = lookupLabel(target);
                if (!string.IsNullOrWhiteSpace(label))
                    return $"\"{label}\"";
            }

            // Fall back to peeking at the target address for a null-terminated string
            if (bus != null)
            {
                string? str = TryPeekStringAtAddress(bus, target);
                if (str != null)
                    return $"\"{str}\"";
            }

            return null;
        }
    }
}
