using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using CodeDesigner.Ps2.Emulator;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── SETWATCHPOINT ─────────────────────────────────────────────────────

        /// <summary>
        /// Handle SETWATCHPOINT command — registers a memory write watchpoint on a
        /// range of EE RAM.
        /// <para>
        /// In PINE mode a software watchpoint is implemented: the current memory
        /// contents are snapshotted and compared on subsequent GETWATCHPOINTHIT /
        /// RUNUNTILWATCHPOINT calls; no PCSX2 changes are required.
        /// </para>
        /// <para>
        /// In internal-emulator mode the scheduler's hardware watchpoint fires
        /// synchronously inside EE.Step() and pauses execution immediately.
        /// </para>
        /// </summary>
        /// <param name="address">Start virtual EE address of the range to watch.</param>
        /// <param name="size">Number of bytes to watch (≥ 1, ≤ 256).</param>
        /// <param name="watchpointType">"write" (default), "read", or "readwrite".</param>
        private string HandleSetWatchpoint(uint? address, int? size, string? watchpointType)
        {
            if (address == null)
                return CreateErrorResponse("Address is required for SETWATCHPOINT");

            int watchSize = Math.Clamp(size ?? 4, 1, 256);

            // ── PINE mode: software watchpoint via memory snapshot ────────────
            if (IsPineMode)
            {
                if (PineClient == null)
                    return CreateErrorResponse("PINE client is not connected");

                byte[] snapshot;
                try { snapshot = PineClient.ReadBytes(address.Value, watchSize); }
                catch (Exception ex)
                {
                    return CreateErrorResponse(
                        $"Failed to read memory at 0x{address.Value:X8} for watchpoint snapshot: {ex.Message}");
                }

                string id;
                lock (_pineWatchpointLock)
                {
                    id = $"wp-{_pineWatchpointCounter++}";
                    _pineWatchpoints.Add(new PineSoftwareWatchpoint
                    {
                        Id             = id,
                        VirtualAddress = address.Value,
                        Size           = watchSize,
                        Snapshot       = snapshot,
                    });
                }

                var pineResp = new CommandResponse
                {
                    Success      = true,
                    Command      = "SETWATCHPOINT",
                    WatchpointId = id,
                    Message      = $"Watchpoint {id} set at 0x{address.Value:X8} ({watchSize} byte(s), write) — polling mode (PINE)",
                };
                return JsonSerializer.Serialize(pineResp);
            }

            // ── Internal emulator mode ────────────────────────────────────────
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            WatchpointType type = WatchpointType.Write;
            if (!string.IsNullOrEmpty(watchpointType))
            {
                type = watchpointType.Trim().ToLowerInvariant() switch
                {
                    "read"      => WatchpointType.Read,
                    "readwrite" => WatchpointType.ReadWrite,
                    _           => WatchpointType.Write,
                };
            }

            if (type != WatchpointType.Write)
                return CreateErrorResponse("Only write watchpoints are currently supported for the internal emulator");

            uint physStart = NormalizeBreakpointToPhysical(address.Value);
            uint physEnd   = physStart + (uint)watchSize;

            string emulId = Scheduler.NextWatchpointId();
            var wp = new MemoryWatchpoint
            {
                Id             = emulId,
                VirtualAddress = address.Value,
                PhysStart      = physStart,
                PhysEnd        = physEnd,
                Size           = watchSize,
                Type           = type,
            };
            Scheduler.WriteWatchpoints.Add(wp);

            var response = new CommandResponse
            {
                Success      = true,
                Command      = "SETWATCHPOINT",
                WatchpointId = emulId,
                Message      = $"Watchpoint {emulId} set at 0x{address.Value:X8} ({watchSize} byte(s), write)",
            };
            return JsonSerializer.Serialize(response);
        }

        // ── CLEARWATCHPOINT ───────────────────────────────────────────────────

        /// <summary>
        /// Handle CLEARWATCHPOINT command — removes the watchpoint with the given ID.
        /// </summary>
        private string HandleClearWatchpoint(string? watchpointId)
        {
            if (string.IsNullOrWhiteSpace(watchpointId))
                return CreateErrorResponse("WatchpointId is required for CLEARWATCHPOINT");

            if (IsPineMode)
            {
                int removed;
                lock (_pineWatchpointLock)
                    removed = _pineWatchpoints.RemoveAll(w => w.Id == watchpointId);
                return removed > 0
                    ? CreateSuccessResponse($"Watchpoint {watchpointId} cleared")
                    : CreateErrorResponse($"No watchpoint found with ID '{watchpointId}'");
            }

            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            var list = Scheduler.WriteWatchpoints;
            int removedEmul = list.RemoveAll(w => w.Id == watchpointId);
            return removedEmul > 0
                ? CreateSuccessResponse($"Watchpoint {watchpointId} cleared")
                : CreateErrorResponse($"No watchpoint found with ID '{watchpointId}'");
        }

        // ── CLEARALLWATCHPOINTS ───────────────────────────────────────────────

        /// <summary>
        /// Handle CLEARALLWATCHPOINTS command — removes every registered watchpoint.
        /// </summary>
        private string HandleClearAllWatchpoints()
        {
            if (IsPineMode)
            {
                int count;
                lock (_pineWatchpointLock)
                {
                    count = _pineWatchpoints.Count;
                    _pineWatchpoints.Clear();
                }
                return CreateSuccessResponse($"Cleared {count} watchpoint(s)");
            }

            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            int emulCount = Scheduler.WriteWatchpoints.Count;
            Scheduler.WriteWatchpoints.Clear();
            return CreateSuccessResponse($"Cleared {emulCount} watchpoint(s)");
        }

        // ── LISTWATCHPOINTS ───────────────────────────────────────────────────

        /// <summary>
        /// Handle LISTWATCHPOINTS command — returns all active watchpoints.
        /// </summary>
        private string HandleListWatchpoints()
        {
            if (IsPineMode)
            {
                List<WatchpointInfo> pineInfos;
                lock (_pineWatchpointLock)
                {
                    pineInfos = _pineWatchpoints.Select(w => new WatchpointInfo
                    {
                        Id      = w.Id,
                        Address = w.VirtualAddress.ToString("X8"),
                        Size    = w.Size,
                        Type    = "write",
                    }).ToList();
                }

                var pineResp = new CommandResponse
                {
                    Success    = true,
                    Command    = "LISTWATCHPOINTS",
                    Watchpoints = pineInfos,
                };
                return JsonSerializer.Serialize(pineResp);
            }

            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            var infos = new List<WatchpointInfo>();
            foreach (var wp in Scheduler.WriteWatchpoints)
            {
                infos.Add(new WatchpointInfo
                {
                    Id      = wp.Id,
                    Address = wp.VirtualAddress.ToString("X8"),
                    Size    = wp.Size,
                    Type    = wp.Type.ToString().ToLowerInvariant(),
                });
            }

            var response = new CommandResponse
            {
                Success    = true,
                Command    = "LISTWATCHPOINTS",
                Watchpoints = infos,
            };
            return JsonSerializer.Serialize(response);
        }

        // ── GETWATCHPOINTHIT ─────────────────────────────────────────────────

        /// <summary>
        /// Handle GETWATCHPOINTHIT command.
        /// <para>
        /// In PINE mode this is a non-blocking poll: it reads the current memory at
        /// every registered watchpoint address and compares it against the last
        /// snapshot.  The first changed watchpoint is returned; its snapshot is
        /// updated so repeated calls keep advancing.  The PC is sampled at the
        /// moment the change is detected (approximate — may be several instructions
        /// after the actual write).
        /// </para>
        /// <para>
        /// In internal-emulator mode it returns <see cref="DualCpuScheduler.LastWatchpointHit"/>
        /// which is set synchronously inside EE.Step().
        /// </para>
        /// </summary>
        private string HandleGetWatchpointHit()
        {
            // ── PINE mode: poll all watched addresses for changes ─────────────
            if (IsPineMode)
            {
                if (PineClient == null)
                    return CreateErrorResponse("PINE client is not connected");

                List<PineSoftwareWatchpoint> wps;
                lock (_pineWatchpointLock)
                    wps = new List<PineSoftwareWatchpoint>(_pineWatchpoints);

                if (wps.Count == 0)
                    return CreateErrorResponse("No watchpoints registered — use SETWATCHPOINT first");

                foreach (var wp in wps)
                {
                    byte[] current;
                    try { current = PineClient.ReadBytes(wp.VirtualAddress, wp.Size); }
                    catch { continue; }

                    if (current.SequenceEqual(wp.Snapshot))
                        continue;

                    // Value changed — sample approximate PC and update snapshot.
                    uint pc = 0;
                    try { pc = PineClient.GetProgramCounter(out _); }
                    catch { /* best-effort */ }

                    // Reference assignment is atomic; no lock needed to update the snapshot.
                    wp.Snapshot = current;

                    uint normalizedPc = NormalizePcToKseg0(pc);
                    var pineResp = new CommandResponse
                    {
                        Success              = true,
                        Command              = "GETWATCHPOINTHIT",
                        WatchpointId         = wp.Id,
                        WatchpointHitAddress = wp.VirtualAddress,
                        WatchpointHitPC      = normalizedPc,
                        WatchpointHitId      = wp.Id,
                        Message              = $"Watchpoint {wp.Id} fired: value changed at " +
                                              $"0x{wp.VirtualAddress:X8}, approximate PC: 0x{normalizedPc:X8}",
                    };
                    return JsonSerializer.Serialize(pineResp);
                }

                return CreateErrorResponse("No watchpoint has fired since the last check");
            }

            // ── Internal emulator mode ────────────────────────────────────────
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            var hit = Scheduler.LastWatchpointHit;
            if (hit == null)
                return CreateErrorResponse("No watchpoint has fired since the last resume");

            uint normalizedEmulPc = NormalizePcToKseg0(hit.PC);
            var response = new CommandResponse
            {
                Success              = true,
                Command              = "GETWATCHPOINTHIT",
                WatchpointId         = hit.WatchpointId,
                WatchpointHitAddress = hit.PhysAddress,
                WatchpointHitPC      = normalizedEmulPc,
                WatchpointHitId      = hit.WatchpointId,
                Message = $"Watchpoint {hit.WatchpointId} fired: write of {hit.Size} byte(s) " +
                          $"at 0x{hit.PhysAddress:X8} by instruction at 0x{normalizedEmulPc:X8}",
            };
            return JsonSerializer.Serialize(response);
        }

        // ── RUNUNTILWATCHPOINT ────────────────────────────────────────────────

        /// <summary>
        /// Handle RUNUNTILWATCHPOINT command — resumes PCSX2, polls all registered
        /// software watchpoints until any watched memory range changes, then pauses
        /// PCSX2 and returns the watchpoint hit details.
        /// <para>
        /// This is the PINE-mode equivalent of the internal emulator's automatic
        /// pause on hardware watchpoint fire.  Because PCSX2's PINE protocol does
        /// not expose hardware watchpoints we implement the detection via memory
        /// polling; the PC returned is approximate (sampled at the moment the change
        /// is detected, which may be a few instructions after the actual write).
        /// </para>
        /// <para>
        /// At least one watchpoint must be registered via SETWATCHPOINT before
        /// calling this command.
        /// </para>
        /// </summary>
        /// <param name="timeoutMs">Maximum milliseconds to wait before giving up.</param>
        private string HandleRunUntilWatchpoint(int timeoutMs)
        {
            if (!IsPineMode)
                return CreateErrorResponse(
                    "RUNUNTILWATCHPOINT is only supported in PINE mode; " +
                    "with the internal emulator the scheduler pauses automatically when a watchpoint fires.");
            if (PineClient == null)
                return CreateErrorResponse("PINE client is not connected");

            if (timeoutMs <= 0) timeoutMs = 15_000;

            List<PineSoftwareWatchpoint> wps;
            lock (_pineWatchpointLock)
                wps = new List<PineSoftwareWatchpoint>(_pineWatchpoints);

            if (wps.Count == 0)
                return CreateErrorResponse("No watchpoints registered — use SETWATCHPOINT first");

            try
            {
                PineClient.Resume();

                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    foreach (var wp in wps)
                    {
                        byte[] current;
                        try { current = PineClient.ReadBytes(wp.VirtualAddress, wp.Size); }
                        catch { continue; }

                        if (current.SequenceEqual(wp.Snapshot))
                            continue;

                        // Hit — pause PCSX2 and sample PC.
                        try { PineClient.Pause(); } catch { /* best-effort */ }

                        uint pc = 0;
                        try { pc = PineClient.GetProgramCounter(out _); }
                        catch { /* best-effort */ }

                        // Reference assignment is atomic; no lock needed to update the snapshot.
                        wp.Snapshot = current;

                        uint normalizedPc = NormalizePcToKseg0(pc);
                        var hitResp = new CommandResponse
                        {
                            Success              = true,
                            Command              = "RUNUNTILWATCHPOINT",
                            WatchpointId         = wp.Id,
                            WatchpointHitAddress = wp.VirtualAddress,
                            WatchpointHitPC      = normalizedPc,
                            WatchpointHitId      = wp.Id,
                            Message              = $"Watchpoint {wp.Id} fired: value changed at " +
                                                  $"0x{wp.VirtualAddress:X8}, approximate PC: 0x{normalizedPc:X8}",
                        };
                        return JsonSerializer.Serialize(hitResp);
                    }

                    Thread.Sleep(10);
                }

                // Timeout — pause so the caller can inspect state.
                try { PineClient.Pause(); } catch { /* best-effort */ }
                return CreateErrorResponse(
                    $"Timeout: no watchpoint fired within {timeoutMs} ms");
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"RUNUNTILWATCHPOINT error: {ex.Message}");
            }
        }

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
