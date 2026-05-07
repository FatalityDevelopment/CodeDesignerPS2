using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class WatchpointTraceService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        private readonly RegisterService _registers;
        public WatchpointTraceService(IBridgeContext ctx, RegisterService registers)
        {
            _ctx = ctx;
            _registers = registers;
        }

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleTraceCalls(c.EndAddress, c.MaxInstructions ?? 10000), "TRACECALLS",  "TRACE_CALLS");
            reg.Add(c => HandleRunUntilPc(c.TargetPc,   c.TimeoutMs       ?? 10000), "RUNUNTILPC",  "RUN_UNTIL_PC");
        }

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
            if (_ctx.Client == null)
                return Responses.Error("Emulator not running — no debug _ctx.Client attached");
            if (endPc == null)
                return Responses.Error("EndAddress is required for TRACECALLS");

            if (maxInstructions <= 0) maxInstructions = 10_000;

            uint normalizedEnd = AddressTranslator.NormalizeBreakpointToPhysical(endPc.Value);
            var traces = new List<CallTraceEntry>();

            bool pendingCall   = false;
            uint pendingFromPc = 0;

            try
            {
                // Confirm we're paused before single-stepping.
                _ = _ctx.Client.GetProgramCounter(out bool initiallyPaused);
                if (!initiallyPaused)
                    return Responses.Error("Emulator must be paused before tracing calls");

                for (int i = 0; i < maxInstructions; i++)
                {
                    uint prePc           = _ctx.Client.GetProgramCounter();
                    uint normalizedPrePc = AddressTranslator.NormalizeBreakpointToPhysical(prePc);

                    if (normalizedPrePc == normalizedEnd)
                        break;

                    // Read the instruction word so we can classify it before stepping.
                    uint instrWord = 0;
                    try
                    {
                        byte[] insn = _ctx.Client.ReadBytes(prePc, 4);
                        if (insn.Length == 4)
                            instrWord = (uint)(insn[0] | (insn[1] << 8) | (insn[2] << 16) | (insn[3] << 24));
                    }
                    catch { /* skip unreadable */ }

                    uint opcode = instrWord >> 26;
                    uint rs     = (instrWord >> 21) & 0x1Fu;
                    bool isCall = opcode == 3                                          // JAL
                               || (opcode == 0 && (instrWord & 0x3Fu) == 9u && rs != 0u); // JALR

                    uint newPc = _ctx.Client.Step();

                    if (pendingCall)
                    {
                        traces.Add(new CallTraceEntry
                        {
                            From = AddressTranslator.NormalizePcToKseg0(pendingFromPc).ToString("X8"),
                            To   = AddressTranslator.NormalizePcToKseg0(newPc).ToString("X8"),
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
                return Responses.Error($"TraceCalls error: {ex.Message}");
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
            if (_ctx.Client == null)
                return Responses.Error("Emulator not running — no debug _ctx.Client attached");
            if (targetPc == null)
                return Responses.Error("TargetPc is required for RUNUNTILPC");

            if (timeoutMs <= 0) timeoutMs = 10_000;

            uint normalizedTarget = AddressTranslator.NormalizeBreakpointForClient(targetPc.Value);

            try
            {
                _ctx.Client.SetBreakpoint(normalizedTarget);
                _ctx.Client.Resume();

                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                bool paused = false;
                while (DateTime.UtcNow < deadline)
                {
                    _ctx.Client.GetProgramCounter(out paused);
                    if (paused) break;
                    Thread.Sleep(10);
                }

                _ctx.Client.ClearBreakpoint(normalizedTarget);

                if (!paused)
                {
                    _ctx.Client.Pause();
                    return Responses.Error(
                        $"Timeout: PC did not reach 0x{targetPc.Value:X8} within {timeoutMs} ms. " +
                        $"Current PC: 0x{AddressTranslator.NormalizePcToKseg0(_ctx.Client.GetProgramCounter()):X8}");
                }

                // Re-use RegisterService to return the full snapshot.
                return _registers.GetRegistersResponse();
            }
            catch (Exception ex)
            {
                return Responses.Error($"RunUntilPc error: {ex.Message}");
            }
        }
    }
}
