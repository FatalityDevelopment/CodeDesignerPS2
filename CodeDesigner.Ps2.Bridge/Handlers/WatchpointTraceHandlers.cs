using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── TRACECALLS ────────────────────────────────────────────────────────

        /// <summary>
        /// Handle TRACECALLS — steps the emulator instruction-by-instruction
        /// from the current PC until <paramref name="endPc"/> is reached (or
        /// <paramref name="maxInstructions"/> instructions execute) and records
        /// every JAL / JALR call site encountered along the way.
        ///
        /// Because MIPS has branch delay slots, a call is recorded one step
        /// after the JAL/JALR is detected: the delay-slot instruction executes
        /// first, and the new PC after that step is the resolved call target.
        ///
        /// The emulator must already be paused before calling this command.
        /// </summary>
        private string HandleTraceCalls(uint? endPc, int maxInstructions)
        {
            if (Client == null)
                return CreateErrorResponse("Emulator not running — no debug client attached");
            if (endPc == null)
                return CreateErrorResponse("EndAddress is required for TRACECALLS");

            if (maxInstructions <= 0) maxInstructions = 10_000;

            uint normalizedEnd = NormalizeBreakpointToPhysical(endPc.Value);
            var traces = new List<CallTraceEntry>();

            bool pendingCall   = false;
            uint pendingFromPc = 0;

            try
            {
                // Confirm we're paused before single-stepping.
                _ = Client.GetProgramCounter(out bool initiallyPaused);
                if (!initiallyPaused)
                    return CreateErrorResponse("Emulator must be paused before tracing calls");

                for (int i = 0; i < maxInstructions; i++)
                {
                    uint prePc           = Client.GetProgramCounter();
                    uint normalizedPrePc = NormalizeBreakpointToPhysical(prePc);

                    if (normalizedPrePc == normalizedEnd)
                        break;

                    // Read the instruction word so we can classify it before stepping.
                    uint instrWord = 0;
                    try
                    {
                        byte[] insn = Client.ReadBytes(prePc, 4);
                        if (insn.Length == 4)
                            instrWord = (uint)(insn[0] | (insn[1] << 8) | (insn[2] << 16) | (insn[3] << 24));
                    }
                    catch { /* skip unreadable */ }

                    uint opcode = instrWord >> 26;
                    uint rs     = (instrWord >> 21) & 0x1Fu;
                    bool isCall = opcode == 3                                          // JAL
                               || (opcode == 0 && (instrWord & 0x3Fu) == 9u && rs != 0u); // JALR

                    uint newPc = Client.Step();

                    if (pendingCall)
                    {
                        traces.Add(new CallTraceEntry
                        {
                            From = NormalizePcToKseg0(pendingFromPc).ToString("X8"),
                            To   = NormalizePcToKseg0(newPc).ToString("X8"),
                        });
                        pendingCall = false;
                    }

                    if (isCall)
                    {
                        pendingCall   = true;
                        pendingFromPc = prePc;
                    }
                }
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"TraceCalls error: {ex.Message}");
            }

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success     = true,
                Command     = "TRACECALLS",
                TracedCalls = traces,
                Message     = $"Traced {traces.Count} call(s) in up to {maxInstructions} instruction(s)",
            });
        }

        // ── RUNUNTILPC ────────────────────────────────────────────────────────

        /// <summary>
        /// Handle RUNUNTILPC — sets a temporary breakpoint at
        /// <paramref name="targetPc"/>, resumes the emulator, and blocks until
        /// the breakpoint fires or <paramref name="timeoutMs"/> milliseconds
        /// elapse. On success, removes the breakpoint and returns the full
        /// register state. On timeout, pauses the emulator, removes the
        /// breakpoint, and reports an error.
        /// </summary>
        private string HandleRunUntilPc(uint? targetPc, int timeoutMs)
        {
            if (Client == null)
                return CreateErrorResponse("Emulator not running — no debug client attached");
            if (targetPc == null)
                return CreateErrorResponse("TargetPc is required for RUNUNTILPC");

            if (timeoutMs <= 0) timeoutMs = 10_000;

            uint normalizedTarget = NormalizeBreakpointForClient(targetPc.Value);

            try
            {
                Client.SetBreakpoint(normalizedTarget);
                Client.Resume();

                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                bool paused = false;
                while (DateTime.UtcNow < deadline)
                {
                    Client.GetProgramCounter(out paused);
                    if (paused) break;
                    Thread.Sleep(10);
                }

                Client.ClearBreakpoint(normalizedTarget);

                if (!paused)
                {
                    Client.Pause();
                    return CreateErrorResponse(
                        $"Timeout: PC did not reach 0x{targetPc.Value:X8} within {timeoutMs} ms. " +
                        $"Current PC: 0x{NormalizePcToKseg0(Client.GetProgramCounter()):X8}");
                }

                return HandleGetRegisters();
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"RunUntilPc error: {ex.Message}");
            }
        }
    }
}
