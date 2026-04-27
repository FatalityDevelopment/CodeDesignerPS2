using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CodeDesigner.Ps2.Common.Interfaces;
using CodeDesigner.Ps2.Common.CPU;

namespace CodeDesigner.Ps2.Common
{
    /// <summary>
    /// Analyzes MIPS instructions using the built-in Analyze() method on each instruction class
    /// </summary>
    public class MipsAnalyzer
    {
        // Pre-compiled regex for stripping state-dependent branch hints.
        // Matches: " [TAKEN]", " [NOT TAKEN]", " [TAKEN] [loop]", " [NOT TAKEN] [loop]"
        private static readonly Regex s_stateHintsRegex = new(
            @"\s*\[(NOT )?TAKEN\](\s*\[loop\])?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // -----------------------------------------------------------------------
        // Memory-mapped I/O register name table (PS2 EE address space)
        // -----------------------------------------------------------------------
        private static readonly Dictionary<uint, string> s_mmioNames = new()
        {
            // INTC
            [0x1000F000] = "INTC_STAT",
            [0x1000F010] = "INTC_MASK",
            // EE Timers
            [0x10000000] = "T0_COUNT",
            [0x10000010] = "T0_MODE",
            [0x10000020] = "T0_COMP",
            [0x10000030] = "T0_HOLD",
            [0x10000800] = "T1_COUNT",
            [0x10000810] = "T1_MODE",
            [0x10000820] = "T1_COMP",
            [0x10000830] = "T1_HOLD",
            [0x10001000] = "T2_COUNT",
            [0x10001010] = "T2_MODE",
            [0x10001020] = "T2_COMP",
            [0x10001800] = "T3_COUNT",
            [0x10001810] = "T3_MODE",
            [0x10001820] = "T3_COMP",
            // GIF
            [0x10003000] = "GIF_CTRL",
            [0x10003010] = "GIF_MODE",
            [0x10003020] = "GIF_STAT",
            [0x10003040] = "GIF_TAG0",
            [0x10003050] = "GIF_TAG1",
            [0x10003060] = "GIF_TAG2",
            [0x10003070] = "GIF_TAG3",
            [0x10003080] = "GIF_CNT",
            [0x10003090] = "GIF_P3CNT",
            [0x100030A0] = "GIF_P3TAG",
            // VIF0
            [0x10003800] = "VIF0_STAT",
            [0x10003810] = "VIF0_FBRST",
            [0x10003820] = "VIF0_ERR",
            [0x10003830] = "VIF0_MARK",
            [0x10003840] = "VIF0_CYCLE",
            [0x10003850] = "VIF0_MODE",
            [0x10003860] = "VIF0_NUM",
            [0x10003870] = "VIF0_MASK",
            [0x10003880] = "VIF0_CODE",
            [0x10003890] = "VIF0_ITOPS",
            [0x100038D0] = "VIF0_ITOP",
            // VIF1
            [0x10003C00] = "VIF1_STAT",
            [0x10003C10] = "VIF1_FBRST",
            [0x10003C20] = "VIF1_ERR",
            [0x10003C30] = "VIF1_MARK",
            [0x10003C40] = "VIF1_CYCLE",
            [0x10003C50] = "VIF1_MODE",
            [0x10003C60] = "VIF1_NUM",
            [0x10003C70] = "VIF1_MASK",
            [0x10003C80] = "VIF1_CODE",
            [0x10003C90] = "VIF1_ITOPS",
            [0x10003CC0] = "VIF1_BASE",
            [0x10003CD0] = "VIF1_OFST",
            [0x10003CE0] = "VIF1_TOPS",
            [0x10003CF0] = "VIF1_ITOP",
            [0x10003D00] = "VIF1_TOP",
            // DMAC channels (first word = CHCR for each channel)
            [0x10008000] = "D0_CHCR",  // VIF0
            [0x10009000] = "D1_CHCR",  // VIF1
            [0x1000A000] = "D2_CHCR",  // GIF
            [0x1000B000] = "D3_CHCR",  // fromIPU
            [0x1000B400] = "D4_CHCR",  // toIPU
            [0x1000C000] = "D5_CHCR",  // SIF0
            [0x1000C400] = "D6_CHCR",  // SIF1
            [0x1000C800] = "D7_CHCR",  // SIF2
            [0x1000D000] = "D8_CHCR",  // fromSPR
            [0x1000D400] = "D9_CHCR",  // toSPR
            // DMAC global
            [0x1000E000] = "D_CTRL",
            [0x1000E010] = "D_STAT",
            [0x1000E020] = "D_PCR",
            [0x1000E030] = "D_SQWC",
            [0x1000E040] = "D_RBSR",
            [0x1000E050] = "D_RBOR",
            [0x1000E060] = "D_STADR",
            // SIF
            [0x1000F200] = "SIF_MSCOM",
            [0x1000F210] = "SIF_SMCOM",
            [0x1000F220] = "SIF_MSFLAG",
            [0x1000F230] = "SIF_SMFLAG",
            [0x1000F240] = "SIF_BD6",
            [0x1000F250] = "SIF_F250",
            [0x1000F260] = "SIF_F260",
            // GS privileged registers
            [0x12000000] = "GS_PMODE",
            [0x12000010] = "GS_SMODE1",
            [0x12000020] = "GS_SMODE2",
            [0x12000030] = "GS_SRFSH",
            [0x12000040] = "GS_SYNCH1",
            [0x12000050] = "GS_SYNCH2",
            [0x12000060] = "GS_SYNCV",
            [0x12000070] = "GS_DISPFB1",
            [0x12000080] = "GS_DISPLAY1",
            [0x12000090] = "GS_DISPFB2",
            [0x120000A0] = "GS_DISPLAY2",
            [0x120000B0] = "GS_EXTBUF",
            [0x120000C0] = "GS_EXTDATA",
            [0x120000D0] = "GS_EXTWRITE",
            [0x120000E0] = "GS_BGCOLOR",
            [0x12001000] = "GS_CSR",
            [0x12001010] = "GS_IMR",
            [0x12001040] = "GS_BUSDIR",
            [0x12001080] = "GS_SIGLBLID",
        };
        /// <summary>
        /// Analyze instructions and generate comments using the instruction's built-in Analyze() method
        /// </summary>
        /// <param name="instructions">List of instruction words and their addresses</param>
        /// <param name="startPc">Starting program counter</param>
        /// <param name="executeInstructions">If true, executes instructions to track register state</param>
        /// <param name="bus">Optional memory bus used to peek at branch/jump target addresses for string/pointer hints.</param>
        /// <param name="lookupLabel">Optional callback that returns a label name for a given address, or null if none.</param>
        /// <param name="includeStateHints">
        /// When <c>false</c> (the default for batch analysis), state-dependent branch hints
        /// such as <c>[TAKEN]</c> / <c>[NOT TAKEN]</c> are stripped from the output.  These
        /// hints are meaningless when the CPU state starts from zero (all registers zeroed),
        /// which is typical in the batch disassembly path.
        /// Pass <c>true</c> only when a real, live CPU snapshot is available.
        /// </param>
        /// <returns>Dictionary mapping instruction address to comment string</returns>
        public static Dictionary<uint, string> AnalyzeInstructions(
            IEnumerable<(uint instruction, uint address)> instructions, 
            uint startPc = 0,
            bool executeInstructions = true,
            IPs2MemoryBus? bus = null,
            Func<uint, string?>? lookupLabel = null,
            bool includeStateHints = false)
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

                        // Strip state-dependent branch hints when the caller opts out.
                        // [TAKEN] / [NOT TAKEN] are misleading when the CPU starts from a blank state.
                        if (!includeStateHints)
                            analysis = StripStateHints(analysis);

                        // Only add comment if analysis returned something meaningful
                        if (!string.IsNullOrWhiteSpace(analysis))
                        {
                            // Append label/string/pointer hint for branch/jump targets when available
                            if (target.HasValue)
                            {
                                string? hint = BuildTargetHint(target.Value, bus, lookupLabel);
                                if (hint != null)
                                    analysis += $" => {hint}";
                            }

                            // Append hints for any other addresses referenced in the comment
                            analysis = AppendAddressHints(analysis, bus, lookupLabel);

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
        /// <param name="bus">Optional memory bus used to peek at the branch/jump target for a string/pointer hint.</param>
        /// <param name="lookupLabel">Optional callback that returns a label name for a given address, or null if none.</param>
        /// <param name="includeStateHints">
        /// When <c>true</c> (default for single-step), state-dependent branch hints such as
        /// <c>[TAKEN]</c> / <c>[NOT TAKEN]</c> are preserved.  Pass <c>false</c> when only a
        /// blank/synthetic CPU state is available.
        /// </param>
        public static string AnalyzeSingleInstruction(uint instruction, uint pc, CpuState cpu,
            IPs2MemoryBus? bus = null,
            Func<uint, string?>? lookupLabel = null,
            bool includeStateHints = true)
        {
            try
            {
                var (instrObj, args) = MipsDisassembler.GetInstructionObject(instruction);
                if (instrObj == null || args == null)
                    return string.Empty;

                string analysis = instrObj.AnalyzeGeneric(args, cpu, pc);
                if (string.IsNullOrWhiteSpace(analysis))
                    return string.Empty;

                if (!includeStateHints)
                    analysis = StripStateHints(analysis);

                if (string.IsNullOrWhiteSpace(analysis))
                    return string.Empty;

                // Append label/string/pointer hint for branch/jump targets when bus or label lookup is available
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

                // Check for LUI+ADDIU or LUI+ORI patterns and append a label/string/pointer hint when available
                uint? pointerTarget = TryGetPointerLoadTarget(instruction, cpu);
                if (pointerTarget.HasValue && (lookupLabel != null || bus != null))
                {
                    string? hint = BuildTargetHint(pointerTarget.Value, bus, lookupLabel);
                    if (hint != null)
                        analysis += $" {hint}";
                    // No hint: the instruction's own Analyze() already shows the computed address
                }

                // Append hints (MMIO names, live values, string data, pointer chains) for any addresses in the comment
                analysis = AppendAddressHints(analysis, bus, lookupLabel);

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
        /// Scans <paramref name="analysis"/> for hex address patterns (<c>0x{8 hex}</c> and
        /// <c>[0x{8 hex}]</c>) and appends the first useful hint found. Priority:
        /// label lookup → MMIO register name → pointer chain / ASCII string peek → raw word value.
        /// Addresses that are already followed by a <c>"…"</c> hint are skipped.
        /// </summary>
        private static string AppendAddressHints(string analysis, IPs2MemoryBus? bus, Func<uint, string?>? lookupLabel)
        {
            // Regex matches both [0x1234ABCD] and bare 0x1234ABCD forms.
            // We only process addresses not already immediately followed by a quote.
            var matches = Regex.Matches(analysis, @"\[?0x([0-9A-Fa-f]{8})\]?");
            foreach (Match m in matches)
            {
                // Skip if this address is already immediately followed by an annotation quote
                int afterMatch = m.Index + m.Length;
                bool alreadyAnnotated = afterMatch + 1 < analysis.Length
                    && analysis[afterMatch] == ' '
                    && analysis[afterMatch + 1] == '"';
                if (alreadyAnnotated)
                    continue;

                uint addr = Convert.ToUInt32(m.Groups[1].Value, 16);

                // 1. Label lookup (highest priority — works even for 0x00000000)
                if (lookupLabel != null)
                {
                    string? label = lookupLabel(addr);
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        analysis += $" \"{label}\"";
                        return analysis;
                    }
                }

                // 2. MMIO register name (does not require a bus)
                if (s_mmioNames.TryGetValue(addr, out string? mmioName))
                {
                    analysis += $" [{mmioName}]";
                    return analysis;
                }

                if (bus == null)
                    continue;

                // 3. Pointer chain / string resolution for addresses in RAM-like regions
                if (IsValidDataAddress(addr))
                {
                    // Try direct string first (most common case; no type tag needed)
                    string? str = TryPeekStringAtAddress(bus, addr);
                    if (str != null)
                    {
                        string region = GetMemoryRegionName(addr) is string rn ? $" ({rn})" : string.Empty;
                        analysis += $" \"{str}\"{region}";
                        return analysis;
                    }

                    // Try pointer chain (up to 4 hops deep)
                    string? chain = TryResolvePointerChain(bus, addr, lookupLabel);
                    if (chain != null)
                    {
                        analysis += $" {chain}";
                        return analysis;
                    }
                }

                // 4. Raw word value (only for load addresses, denoted by [0x...] in the comment)
                if (m.Value.StartsWith('[') && IsValidDataAddress(addr))
                {
                    try
                    {
                        uint word = bus.ReadWord(addr);
                        string region = GetMemoryRegionName(addr) is string rn ? $" ({rn})" : string.Empty;
                        analysis += $" = 0x{word:X8}{region}";
                        return analysis;
                    }
                    catch { /* address unreadable */ }
                }
            }

            return analysis;
        }

        // -----------------------------------------------------------------------
        // Pointer chain resolution
        // -----------------------------------------------------------------------

        private const int MaxPointerDepth = 4;

        /// <summary>
        /// Follows a pointer chain starting at <paramref name="addr"/> up to
        /// <see cref="MaxPointerDepth"/> hops and returns a formatted hint string
        /// of the form <c>[PTR->STR] 0xAAAAAAAA -> "hello"</c>, or <c>null</c>
        /// when no useful chain is found.
        /// </summary>
        private static string? TryResolvePointerChain(IPs2MemoryBus bus, uint addr,
            Func<uint, string?>? lookupLabel)
        {
            var addressChain = new List<uint>();
            var typeChain    = new List<string>();

            uint current = addr;
            for (int depth = 0; depth < MaxPointerDepth; depth++)
            {
                if (!IsValidDataAddress(current))
                    break;

                // Check for label at this pointer address
                if (depth > 0 && lookupLabel != null)
                {
                    string? lbl = lookupLabel(current);
                    if (!string.IsNullOrWhiteSpace(lbl))
                    {
                        typeChain.Add("LBL");
                        addressChain.Add(current);
                        return FormatPointerChain(typeChain, addressChain, $"\"{lbl}\"");
                    }
                }

                // Check if current address contains a string
                string? str = TryPeekStringAtAddress(bus, current);
                if (str != null)
                {
                    typeChain.Add("STR");
                    addressChain.Add(current);
                    // When the very first address is already a string (depth == 0, no hops yet),
                    // AppendAddressHints will have already handled it via the direct-string check
                    // that runs before TryResolvePointerChain. Returning null here avoids a
                    // duplicate annotation in that case.
                    return typeChain.Count == 1
                        ? null
                        : FormatPointerChain(typeChain, addressChain, $"\"{str}\"");
                }

                // Try to read the word at current address and follow as pointer
                uint ptr;
                try { ptr = bus.ReadWord(current); }
                catch { break; }

                // Avoid infinite loops. Treat null pointers (0x00000000) as deliberate sentinels:
                // while address 0 is technically valid EE RAM, a word value of 0 almost always
                // indicates an uninitialised or null pointer rather than a real pointer into RAM.
                if (ptr == 0 || ptr == current || !IsValidDataAddress(ptr))
                    break;

                typeChain.Add("PTR");
                addressChain.Add(current);
                current = ptr;
            }

            return null;
        }

        /// <summary>
        /// Formats a resolved pointer chain into a human-readable hint string.
        /// Example: <c>[PTR->PTR->STR] 0xAAAA0000 -> 0xBBBB0000 -> "hello"</c>
        /// </summary>
        private static string FormatPointerChain(List<string> types, List<uint> addresses, string finalValue)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            sb.Append(string.Join("->", types));
            sb.Append("] ");

            // Show all intermediate address hops (the first address in the chain is the
            // one whose content is shown, so we include all addresses up to the last).
            for (int i = 0; i < addresses.Count - 1; i++)
            {
                sb.Append($"0x{addresses[i]:X8} -> ");
            }

            // Last address -> final value
            if (addresses.Count > 0)
                sb.Append($"0x{addresses[^1]:X8} -> ");

            sb.Append(finalValue);
            return sb.ToString();
        }

        // -----------------------------------------------------------------------
        // State-hint stripping
        // -----------------------------------------------------------------------

        /// <summary>
        /// Removes <c>[TAKEN]</c>, <c>[NOT TAKEN]</c> (and their <c>[loop]</c>
        /// suffix variants) from an analysis string produced by a branch instruction.
        /// These hints are only meaningful with a live CPU state; they are noise when
        /// registers start from zero.
        /// </summary>
        private static string StripStateHints(string analysis)
        {
            if (string.IsNullOrEmpty(analysis))
                return analysis;

            analysis = s_stateHintsRegex.Replace(analysis, string.Empty);
            return analysis.TrimEnd();
        }

        // -----------------------------------------------------------------------
        // Memory region awareness
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns a short human-readable name for the PS2 memory region containing
        /// <paramref name="addr"/>, or <c>null</c> when the address is not in a
        /// recognized named region.
        /// </summary>
        private static string? GetMemoryRegionName(uint addr)
        {
            // Strip KSEG0 / KSEG1 bits to get the physical equivalent
            uint phys = addr;
            if (addr >= 0x80000000u && addr < 0xC0000000u)
                phys = addr & 0x1FFFFFFFu;

            if (phys < 0x02000000u)             return "EE RAM";
            if (phys >= 0x1FC00000u && phys < 0x20000000u) return "BIOS ROM";
            if (addr >= 0x70000000u && addr < 0x70004000u) return "Scratchpad";
            if (phys >= 0x10000000u && phys < 0x10010000u) return "I/O Regs";
            if (phys >= 0x12000000u && phys < 0x12002000u) return "GS Regs";
            return null;
        }

        // -----------------------------------------------------------------------
        // Address range predicates
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns true when <paramref name="addr"/> is in a region that may be safely
        /// read as data (main RAM, BIOS, scratchpad). Excludes I/O register space.
        /// </summary>
        private static bool IsValidDataAddress(uint addr)
        {
            // Main RAM: 0x00000000–0x01FFFFFF (32 MB) and KSEG0/KSEG1 mirrors
            if (addr < 0x02000000u)
                return true;
            if (addr >= 0x80000000u && addr < 0x82000000u)
                return true;
            if (addr >= 0xA0000000u && addr < 0xA2000000u)
                return true;
            // Scratchpad: 0x70000000–0x70003FFF
            if (addr >= 0x70000000u && addr < 0x70004000u)
                return true;
            // BIOS ROM: 0x1FC00000–0x1FFFFFFF and mirrors
            if (addr >= 0x1FC00000u && addr < 0x20000000u)
                return true;
            if (addr >= 0xBFC00000u && addr < 0xC0000000u)
                return true;
            return false;
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
        /// by checking the label database first, then peeking at memory for a string or
        /// following a pointer chain.
        /// Returns <c>null</c> when no useful hint can be produced.
        /// </summary>
        private static string? BuildTargetHint(uint target, IPs2MemoryBus? bus, Func<uint, string?>? lookupLabel)
        {
            // 1. Prefer a symbolic label over a raw memory peek
            if (lookupLabel != null)
            {
                string? label = lookupLabel(target);
                if (!string.IsNullOrWhiteSpace(label))
                    return $"\"{label}\"";
            }

            if (bus == null || !IsValidDataAddress(target))
                return null;

            // 2. Direct string at target address
            string? str = TryPeekStringAtAddress(bus, target);
            if (str != null)
            {
                string region = GetMemoryRegionName(target) is string rn ? $" ({rn})" : string.Empty;
                return $"\"{str}\"{region}";
            }

            // 3. Pointer chain starting at target address
            return TryResolvePointerChain(bus, target, lookupLabel);
        }
    }
}

