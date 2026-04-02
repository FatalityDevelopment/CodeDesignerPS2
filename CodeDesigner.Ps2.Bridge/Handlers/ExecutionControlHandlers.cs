using System;
using System.Collections.Generic;
using System.Text.Json;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.Emulator;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        private string HandlePause()
        {
            if (IsPineMode)
            {
                try   { PineClient!.Pause(); return CreateSuccessResponse("Paused"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE Pause error: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            Scheduler.Paused = true;
            return CreateSuccessResponse("Paused");
        }

        /// <summary>
        /// Handle RESUME command — resumes emulator execution.
        /// In PINE mode, resumes PCSX2 via the extended PINE protocol (MsgResume).
        /// </summary>
        private string HandleResume()
        {
            if (IsPineMode)
            {
                try   { PineClient!.Resume(); return CreateSuccessResponse("Resumed"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE Resume error: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            // If the emulator is paused at a breakpoint, execute the breakpoint
            // instruction once before releasing the run gate.  This advances PC
            // past the breakpoint address so the same breakpoint does not
            // immediately re-fire the moment execution resumes.
            bool wasAtBreakpoint = Scheduler.LastBreakpointHit.HasValue;
            if (wasAtBreakpoint)
            {
                Scheduler.StepOne();
                // Clear the last breakpoint hit so we don't keep re-triggering
                Scheduler.LastBreakpointHit = null;
            }

            Scheduler.Paused = false;
            return CreateSuccessResponse("Resumed");
        }

        /// <summary>
        /// Handle STEP command — executes exactly one EE instruction while
        /// keeping the emulator paused, then returns the new PC state.
        /// In PINE mode, steps PCSX2 via the extended PINE protocol (MsgStep).
        /// </summary>
        private string HandleStep()
        {
            if (IsPineMode)
            {
                try
                {
                    uint newPc = PineClient!.Step();
                    var pine_resp = new CommandResponse
                    {
                        Success = true,
                        Command = "STEP",
                        Paused  = true,
                        PC      = newPc,
                    };
                    return JsonSerializer.Serialize(pine_resp);
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE Step error: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            if (!Scheduler.Paused)
                return CreateErrorResponse("Emulator must be paused to single-step");

            // Capture the PC and instruction word *before* stepping so we can
            // analyze the instruction with the actual pre-execution register state.
            uint prePc = (uint)(Scheduler.EE.Cpu.PC & 0xFFFFFFFFUL);
            uint instrWord = 0;
            string? analysisComment = null;
            try
            {
                instrWord = Scheduler.EE.Bus.ReadWord(prePc);
                // Snapshot register state before execution; Bus/COP0 are not needed for analysis.
                var cpuSnap = Scheduler.EE.Cpu.CreateAnalysisSnapshot();
                analysisComment = MipsAnalyzer.AnalyzeSingleInstruction(instrWord, prePc, cpuSnap,
                    Scheduler.EE.Bus,
                    addr => _labels.TryGetValue(addr, out var e) ? e.Label : null);
            }
            catch { /* If we can't read the instruction, skip analysis */ }

            Scheduler.StepOne();

            // Build the response with the PC update and the live analysis.
            uint pc = NormalizePcToKseg0((uint)(Scheduler.EE.Cpu.PC & 0xFFFFFFFFUL));
            uint? bpHit = Scheduler.LastBreakpointHit.HasValue
                ? NormalizePcToKseg0(Scheduler.LastBreakpointHit.Value)
                : (uint?)null;
            var response = new CommandResponse
            {
                Success = true,
                Command = "STEP",
                Paused  = Scheduler.Paused,
                PC      = pc,
                BreakpointHitAddress = bpHit,
            };

            // Attach analysis if we produced a non-empty comment.
            if (!string.IsNullOrEmpty(analysisComment))
            {
                uint normalizedPrePc = NormalizePcToKseg0(prePc);
                response.InstructionAnalysis = new Dictionary<string, string>
                {
                    [normalizedPrePc.ToString("X8")] = analysisComment
                };
            }

            return JsonSerializer.Serialize(response);
        }

        /// <summary>
        /// Normalize a virtual EE address to its KSEG0 representation.
        /// KSEG1 (0xA0000000–0xBFFFFFFF, uncached) is remapped to the equivalent
        /// KSEG0 address (0x80000000–0x9FFFFFFF, cached) so that all PC values
        /// presented to the client are consistent and use the 0x8xxxxxxx /
        /// 0x9xxxxxxx form regardless of which segment the CPU is executing from.
        /// </summary>
        private static uint NormalizePcToKseg0(uint vaddr)
        {
            // KSEG1: bits[31:29] == 101 (0xA0000000–0xBFFFFFFF)
            if ((vaddr & 0xE0000000u) == 0xA0000000u)
                return (vaddr & 0x1FFFFFFFu) | 0x80000000u;
            return vaddr;
        }

        /// <summary>
        /// Normalize a virtual EE address to its physical representation for
        /// breakpoint storage.  KSEG0 (0x80000000–0x9FFFFFFF) and KSEG1
        /// (0xA0000000–0xBFFFFFFF) both strip the top 3 bits to yield the
        /// physical address.  Physical addresses (from the live disassembly panels
        /// which display EE Main RAM at 0x00000000 and BIOS ROM at 0x1FC00000) and
        /// KUSEG user-mode addresses pass through unchanged.
        /// Using physical addresses as the canonical form ensures that a breakpoint
        /// set from the disassembly panel fires correctly regardless of which
        /// virtual alias the CPU uses at runtime.
        /// </summary>
        private static uint NormalizeBreakpointToPhysical(uint vaddr)
        {
            // KSEG0 (0x80000000–0x9FFFFFFF) or KSEG1 (0xA0000000–0xBFFFFFFF)
            if (vaddr >= 0x80000000u && vaddr <= 0xBFFFFFFFu)
                return vaddr & 0x1FFFFFFFu;
            return vaddr;
        }

        /// <summary>
        /// Handle SETBP command — adds a breakpoint at the given EE address.
        /// In PINE mode, registers the breakpoint via the extended PINE protocol (MsgSetBreakpoint).
        /// </summary>
        private string HandleSetBreakpoint(uint? address)
        {
            if (address == null)
                return CreateErrorResponse("BreakpointAddress is required for SETBP");
            uint normalized = NormalizePcToKseg0(address.Value);
            if (IsPineMode)
            {
                try   { PineClient!.SetBreakpoint(normalized); return CreateSuccessResponse($"Breakpoint set at 0x{normalized:X8}"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE SetBreakpoint error: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            
            normalized = NormalizeBreakpointToPhysical(address.Value);
            Scheduler.Breakpoints.Add(normalized);
            return CreateSuccessResponse($"Breakpoint set at 0x{normalized:X8}");
        }

        /// <summary>
        /// Handle CLEARBP command — removes the breakpoint at the given EE address.
        /// In PINE mode, clears the breakpoint via the extended PINE protocol (MsgClearBreakpoint).
        /// </summary>
        private string HandleClearBreakpoint(uint? address)
        {
            if (address == null)
                return CreateErrorResponse("BreakpointAddress is required for CLEARBP");
            uint normalized = NormalizePcToKseg0(address.Value);
            if (IsPineMode)
            {
                try   { PineClient!.ClearBreakpoint(normalized); return CreateSuccessResponse($"Breakpoint cleared at 0x{normalized:X8}"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE ClearBreakpoint error: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            normalized = NormalizeBreakpointToPhysical(address.Value);
            Scheduler.Breakpoints.Remove(normalized);
            return CreateSuccessResponse($"Breakpoint cleared at 0x{normalized:X8}");
        }

        /// <summary>
        /// Handle CLEARBP_ALL command — removes all breakpoints.
        /// In PINE mode, clears all breakpoints via the extended PINE protocol (MsgClearAllBreakpoints).
        /// </summary>
        private string HandleClearAllBreakpoints()
        {
            if (IsPineMode)
            {
                try   { PineClient!.ClearAllBreakpoints(); return CreateSuccessResponse("All breakpoints cleared"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE ClearAllBreakpoints error: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            Scheduler.Breakpoints.Clear();
            return CreateSuccessResponse("All breakpoints cleared");
        }

        /// <summary>
        /// Handle GETPC command — returns current EE PC and paused/breakpoint state.
        /// In PINE mode, reads PC from PCSX2 via the extended PINE protocol (MsgGetProgramCounter).
        /// The PC is normalized to KSEG0 (0x80000000–0x9FFFFFFF) so the client
        /// always sees a consistent address regardless of whether the CPU executed
        /// the instruction via the KSEG0 or KSEG1 virtual-address window.
        /// </summary>
        private string HandleGetPC()
        {
            if (IsPineMode)
            {
                try
                {
                    uint pc = PineClient!.GetProgramCounter(out bool paused);
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = true,
                        Command = "GETPC",
                        Paused  = paused,
                        PC      = pc,
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE GetPC error: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            uint normalizedPc = NormalizePcToKseg0((uint)(Scheduler.EE.Cpu.PC & 0xFFFFFFFFUL));
            uint? bpHit = Scheduler.LastBreakpointHit.HasValue
                ? NormalizePcToKseg0(Scheduler.LastBreakpointHit.Value)
                : (uint?)null;
            var response = new CommandResponse
            {
                Success = true,
                Command = "GETPC",
                Paused  = Scheduler.Paused,
                PC      = normalizedPc,
                BreakpointHitAddress = bpHit,
            };
            return JsonSerializer.Serialize(response);
        }
    }
}
