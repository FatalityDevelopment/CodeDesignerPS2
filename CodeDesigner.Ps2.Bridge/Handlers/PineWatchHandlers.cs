using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── PINEADDWATCH ──────────────────────────────────────────────────────

        /// <summary>
        /// Handle PINEADDWATCH — registers a hardware memory watchpoint in PCSX2 via
        /// the PINE MsgAddWatch (0x2E) opcode.
        /// <para>
        /// Backed by CBreakPoints MemCheck (MEMCHECK_BREAK). JIT-only; no effect
        /// under the interpreter or HLE.
        /// </para>
        /// </summary>
        private string HandlePineAddWatch(byte? cpuSel, uint? start, uint? end, byte? cond)
        {
            if (!IsPineMode)
                return CreateErrorResponse("PINEADDWATCH requires PINE mode (connect to PCSX2 first)");
            if (start == null)
                return CreateErrorResponse("Address (start) is required for PINEADDWATCH");
            if (end == null)
                return CreateErrorResponse("EndAddress (end, exclusive) is required for PINEADDWATCH");
            if (start >= end)
                return CreateErrorResponse("Start address must be less than end address for PINEADDWATCH");

            byte cpu     = cpuSel ?? 0;
            byte condVal = cond   ?? 0x03; // default: read+write

            if (condVal == 0 || condVal > 0x03)
                return CreateErrorResponse("WatchCond must be 0x01 (read), 0x02 (write), or 0x03 (read+write)");

            try
            {
                PineClient!.AddWatch(cpu, start.Value, end.Value, condVal);
                string condStr = condVal switch
                {
                    0x01 => "read",
                    0x02 => "write",
                    _    => "read+write",
                };
                string cpuStr = cpu == 0 ? "EE" : "IOP";
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "PINEADDWATCH",
                    Message = $"Watch added on {cpuStr} 0x{start.Value:X8}–0x{end.Value:X8} ({condStr})",
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"PINE AddWatch failed: {ex.Message}");
            }
        }

        // ── PINEREMOVEWATCH ───────────────────────────────────────────────────

        /// <summary>
        /// Handle PINEREMOVEWATCH — removes a hardware memory watchpoint from PCSX2 via
        /// the PINE MsgRemoveWatch (0x2F) opcode.
        /// Returns IPC_FAIL if no matching range is found on the specified CPU.
        /// </summary>
        private string HandlePineRemoveWatch(byte? cpuSel, uint? start, uint? end)
        {
            if (!IsPineMode)
                return CreateErrorResponse("PINEREMOVEWATCH requires PINE mode (connect to PCSX2 first)");
            if (start == null)
                return CreateErrorResponse("Address (start) is required for PINEREMOVEWATCH");
            if (end == null)
                return CreateErrorResponse("EndAddress (end, exclusive) is required for PINEREMOVEWATCH");

            byte cpu = cpuSel ?? 0;

            try
            {
                bool removed = PineClient!.RemoveWatch(cpu, start.Value, end.Value);
                if (!removed)
                    return CreateErrorResponse(
                        $"No watchpoint found on {(cpu == 0 ? "EE" : "IOP")} at 0x{start.Value:X8}–0x{end.Value:X8}");

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "PINEREMOVEWATCH",
                    Message = $"Watch removed on {(cpu == 0 ? "EE" : "IOP")} 0x{start.Value:X8}–0x{end.Value:X8}",
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"PINE RemoveWatch failed: {ex.Message}");
            }
        }

        // ── PINELISTWATCHES ───────────────────────────────────────────────────

        /// <summary>
        /// Handle PINELISTWATCHES — lists all active hardware memory watchpoints via
        /// the PINE MsgListWatches (0x30) opcode.
        /// </summary>
        private string HandlePineListWatches(byte? cpuSel)
        {
            if (!IsPineMode)
                return CreateErrorResponse("PINELISTWATCHES requires PINE mode (connect to PCSX2 first)");

            byte sel = cpuSel ?? 0xFF;

            try
            {
                var watches = PineClient!.ListWatches(sel);
                var infos = watches.Select(w => new PineWatchInfo
                {
                    Cpu   = w.Cpu == 0 ? "EE" : "IOP",
                    Start = $"0x{w.Start:X8}",
                    End   = $"0x{w.End:X8}",
                    Cond  = w.Cond switch
                    {
                        0x01 => "read",
                        0x02 => "write",
                        _    => "read+write",
                    },
                }).ToList();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success     = true,
                    Command     = "PINELISTWATCHES",
                    PineWatches = infos,
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"PINE ListWatches failed: {ex.Message}");
            }
        }

        // ── PINECLEARALLWATCHES ───────────────────────────────────────────────

        /// <summary>
        /// Handle PINECLEARALLWATCHES — removes every active hardware memory watchpoint
        /// on all CPUs via the PINE MsgClearAllWatches (0x31) opcode.
        /// </summary>
        private string HandlePineClearAllWatches()
        {
            if (!IsPineMode)
                return CreateErrorResponse("PINECLEARALLWATCHES requires PINE mode (connect to PCSX2 first)");

            try
            {
                PineClient!.ClearAllWatches();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "PINECLEARALLWATCHES",
                    Message = "All PINE hardware watchpoints cleared",
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"PINE ClearAllWatches failed: {ex.Message}");
            }
        }

        // ── PINESAVESNAPSHOT ──────────────────────────────────────────────────

        /// <summary>
        /// Handle PINESAVESNAPSHOT — triggers a save-state snapshot in PCSX2 via
        /// the PINE MsgSaveSnapshot (0x32) opcode.
        /// </summary>
        private string HandlePineSaveSnapshot()
        {
            if (!IsPineMode)
                return CreateErrorResponse("PINESAVESNAPSHOT requires PINE mode (connect to PCSX2 first)");

            try
            {
                PineClient!.SaveSnapshot();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "PINESAVESNAPSHOT",
                    Message = "Snapshot triggered in PCSX2",
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"PINE SaveSnapshot failed: {ex.Message}");
            }
        }
    }
}
