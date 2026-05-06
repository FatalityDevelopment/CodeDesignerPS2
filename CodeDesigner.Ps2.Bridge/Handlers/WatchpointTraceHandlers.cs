using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using CodeDesigner.Ps2.EmulatorCore;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── TRACECALLS ────────────────────────────────────────────────────────

        /// <summary>
        /// Handle TRACECALLS command — steps the emulator instruction-by-instruction
        /// from the current PC until <paramref name="endPc"/> is reached (or
        /// <paramref name="maxInstructions"/> instructions have been executed) and
        /// records every JAL / JALR call site encountered along the way.
        ///
        /// Because MIPS has branch delay slots, a call is recorded one step after
        /// the JAL/JALR instruction is detected: the delay-slot instruction executes
        /// first, and the new PC after that step is the resolved call target.
        ///
        /// The emulator must already be paused before calling this command.
        /// </summary>
        /// <param name="endPc">
        /// Target EE virtual address at which tracing stops.  Normalised to
        /// physical for the PC comparison so KSEG0/KSEG1 aliases match.
        /// </param>
        /// <param name="maxInstructions">
        /// Upper bound on instructions executed (safety guard against infinite loops).
        /// Defaults to 10 000.
        /// </param>
        private string HandleTraceCalls(uint? endPc, int maxInstructions)
        {
            if (IsPineMode)
                return CreateErrorResponse("TRACECALLS is not supported in PINE mode");
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            if (!Scheduler.Paused)
                return CreateErrorResponse("Emulator must be paused before tracing calls");
            if (endPc == null)
                return CreateErrorResponse("EndAddress is required for TRACECALLS");

            if (maxInstructions <= 0) maxInstructions = 10_000;

            uint normalizedEnd = NormalizeBreakpointToPhysical(endPc.Value);
            var traces = new List<CallTraceEntry>();

            // pendingCall: true when the previous instruction was JAL/JALR and the
            // delay slot has not yet executed; the call target is resolved after the
            // delay-slot step completes.
            bool pendingCall   = false;
            uint pendingFromPc = 0;

            try
            {
                for (int i = 0; i < maxInstructions; i++)
                {
                    uint prePc           = (uint)(Scheduler.EE.Cpu.PC & 0xFFFFFFFFUL);
                    uint normalizedPrePc = NormalizeBreakpointToPhysical(prePc);

                    // Stop when we reach the target PC.
                    if (normalizedPrePc == normalizedEnd)
                        break;

                    // Read the instruction word before stepping so we can classify it.
                    uint instrWord = 0;
                    try { instrWord = Scheduler.EE.Bus.ReadWord(prePc); } catch { /* skip unreadable */ }

                    uint opcode = instrWord >> 26;
                    // JAL (opcode 3): always a call.
                    // JALR (opcode 0, funct 9): require rs != $zero so we don't match
                    // the degenerate encoding where rs=0 (target=0, not a real call).
                    uint rs = (instrWord >> 21) & 0x1Fu;
                    bool isCall = opcode == 3                                          // JAL
                               || (opcode == 0 && (instrWord & 0x3Fu) == 9u && rs != 0u); // JALR

                    // Execute one EE instruction (stays paused).
                    Scheduler.StepOne();

                    // If the previous instruction was a call, the delay slot just ran;
                    // the current PC is now the call target.
                    if (pendingCall)
                    {
                        uint callTarget = NormalizePcToKseg0((uint)(Scheduler.EE.Cpu.PC & 0xFFFFFFFFUL));
                        traces.Add(new CallTraceEntry
                        {
                            From = NormalizePcToKseg0(pendingFromPc).ToString("X8"),
                            To   = callTarget.ToString("X8"),
                        });
                        pendingCall = false;
                    }

                    if (isCall)
                    {
                        pendingCall   = true;
                        pendingFromPc = prePc;
                    }

                    // Stop early if another breakpoint or watchpoint fired.
                    if (Scheduler.LastBreakpointHit.HasValue || Scheduler.LastWatchpointHit != null)
                        break;
                }
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"TraceCalls error: {ex.Message}");
            }

            var response = new CommandResponse
            {
                Success     = true,
                Command     = "TRACECALLS",
                TracedCalls = traces,
                Message     = $"Traced {traces.Count} call(s) in up to {maxInstructions} instruction(s)",
            };
            return JsonSerializer.Serialize(response);
        }

        // ── RUNUNTILPC ────────────────────────────────────────────────────────

        /// <summary>
        /// Handle RUNUNTILPC command — sets a temporary breakpoint at
        /// <paramref name="targetPc"/>, resumes the emulator, and blocks until the
        /// breakpoint fires or <paramref name="timeoutMs"/> milliseconds elapse.
        /// On success, removes the breakpoint and returns the full register state.
        /// On timeout, pauses the emulator, removes the breakpoint, and returns an
        /// error so the caller can inspect the state at the point of timeout.
        /// </summary>
        private string HandleRunUntilPc(uint? targetPc, int timeoutMs)
        {
            if (IsPineMode)
                return HandleRunUntilPcPine(targetPc, timeoutMs);
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            if (targetPc == null)
                return CreateErrorResponse("TargetPc is required for RUNUNTILPC");

            if (timeoutMs <= 0) timeoutMs = 10_000;

            uint normalizedTarget = NormalizeBreakpointToPhysical(targetPc.Value);

            // If the emulator is already paused at a previous breakpoint, step over
            // it first (same logic as RESUME) so we don't immediately re-fire.
            if (Scheduler.LastBreakpointHit.HasValue)
            {
                uint bpAddr = Scheduler.LastBreakpointHit.Value;
                if (bpAddr != normalizedTarget)
                {
                    Scheduler.Breakpoints.Remove(bpAddr);
                    Scheduler.StepOne();
                    Scheduler.Breakpoints.Add(bpAddr);
                }
            }

            // Register the temporary breakpoint and resume.
            bool wasAlreadyPresent = Scheduler.Breakpoints.Contains(normalizedTarget);
            Scheduler.Breakpoints.Add(normalizedTarget);
            Scheduler.Paused = false; // clears LastBreakpointHit and LastWatchpointHit

            // Poll until paused (breakpoint or watchpoint fired) or timeout.
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!Scheduler.Paused && DateTime.UtcNow < deadline)
                Thread.Sleep(10);

            if (!Scheduler.Paused)
            {
                // Timeout: pause the emulator and report the failure.
                Scheduler.Paused = true;
                if (!wasAlreadyPresent)
                    Scheduler.Breakpoints.Remove(normalizedTarget);
                return CreateErrorResponse(
                    $"Timeout: PC did not reach 0x{targetPc.Value:X8} within {timeoutMs} ms. " +
                    $"Current PC: 0x{NormalizePcToKseg0((uint)(Scheduler.EE.Cpu.PC & 0xFFFFFFFFUL)):X8}");
            }

            // Arrived — clean up the temporary breakpoint (unless it was pre-existing).
            if (!wasAlreadyPresent)
                Scheduler.Breakpoints.Remove(normalizedTarget);

            // Return full register state so the caller gets everything atomically.
            return HandleGetRegisters();
        }

        /// <summary>
        /// PINE-mode implementation of RUNUNTILPC.
        /// Sets a breakpoint in PCSX2, resumes, and polls GetProgramCounter until
        /// the emulator pauses or the timeout expires.
        /// </summary>
        private string HandleRunUntilPcPine(uint? targetPc, int timeoutMs)
        {
            if (PineClient == null)
                return CreateErrorResponse("PINE client is not connected");
            if (targetPc == null)
                return CreateErrorResponse("TargetPc is required for RUNUNTILPC");

            if (timeoutMs <= 0) timeoutMs = 10_000;

            uint normalizedTarget = NormalizePcToKseg0(targetPc.Value);

            try
            {
                PineClient.SetBreakpoint(normalizedTarget);
                PineClient.Resume();

                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                bool paused = false;
                while (DateTime.UtcNow < deadline)
                {
                    PineClient.GetProgramCounter(out paused);
                    if (paused) break;
                    Thread.Sleep(10);
                }

                PineClient.ClearBreakpoint(normalizedTarget);

                if (!paused)
                    return CreateErrorResponse(
                        $"Timeout: PC did not reach 0x{targetPc.Value:X8} within {timeoutMs} ms");

                return HandleGetRegisters();
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"RUNUNTILPC (PINE) error: {ex.Message}");
            }
        }
    }
}
