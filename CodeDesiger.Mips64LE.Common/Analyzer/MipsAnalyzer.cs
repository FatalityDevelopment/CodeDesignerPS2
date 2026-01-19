using System;
using System.Collections.Generic;
using System.Linq;
using CodeDesigner.Mips64LE.Common.CPU;

namespace CodeDesigner.Mips64LE.Common
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
        /// <returns>Dictionary mapping instruction address to comment string</returns>
        public static Dictionary<uint, string> AnalyzeInstructions(
            IEnumerable<(uint instruction, uint address)> instructions, 
            uint startPc = 0,
            bool executeInstructions = true)
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
    }
}
