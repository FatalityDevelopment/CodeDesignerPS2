using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class PineWatchService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public PineWatchService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandlePineAddWatch(c.CpuSel, c.Address, c.EndAddress, c.WatchCond), "PINEADDWATCH");
            reg.Add(c => HandlePineRemoveWatch(c.CpuSel, c.Address, c.EndAddress),           "PINEREMOVEWATCH");
            reg.Add(c => HandlePineListWatches(c.CpuSel),                                    "PINELISTWATCHES");
            reg.Add(_ => HandlePineClearAllWatches(),                                        "PINECLEARALLWATCHES");
            reg.Add(_ => HandlePineSaveScreenshot(),                                         "PINESAVESCREENSHOT");
            reg.Add(c => HandlePineAudio(c.AudioMuted, "PINESETAUDIOMUTED"),                 "PINESETAUDIOMUTED");
            reg.Add(_ => HandlePineAudio(null, "PINEGETAUDIOSTATE"),                         "PINEGETAUDIOSTATE");
        }

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
            if (_ctx.PineClient == null)
                return Responses.Error("PINEADDWATCH requires PINE mode (connect to PCSX2 first)");
            if (start == null)
                return Responses.Error("Address (start) is required for PINEADDWATCH");
            if (end == null)
                return Responses.Error("EndAddress (end, exclusive) is required for PINEADDWATCH");
            if (start >= end)
                return Responses.Error("Start address must be less than end address for PINEADDWATCH");

            byte cpu     = cpuSel ?? 0;
            byte condVal = cond   ?? 0x03; // default: read+write

            if (condVal == 0 || condVal > 0x03)
                return Responses.Error("WatchCond must be 0x01 (read), 0x02 (write), or 0x03 (read+write)");

            try
            {
                _ctx.PineClient!.AddWatch(cpu, start.Value, end.Value, condVal);
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
                return Responses.Error($"PINE AddWatch failed: {ex.Message}");
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
            if (_ctx.PineClient == null)
                return Responses.Error("PINEREMOVEWATCH requires PINE mode (connect to PCSX2 first)");
            if (start == null)
                return Responses.Error("Address (start) is required for PINEREMOVEWATCH");
            if (end == null)
                return Responses.Error("EndAddress (end, exclusive) is required for PINEREMOVEWATCH");

            byte cpu = cpuSel ?? 0;

            try
            {
                bool removed = _ctx.PineClient!.RemoveWatch(cpu, start.Value, end.Value);
                if (!removed)
                    return Responses.Error(
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
                return Responses.Error($"PINE RemoveWatch failed: {ex.Message}");
            }
        }

        // ── PINELISTWATCHES ───────────────────────────────────────────────────

        /// <summary>
        /// Handle PINELISTWATCHES — lists all active hardware memory watchpoints via
        /// the PINE MsgListWatches (0x30) opcode.
        /// </summary>
        private string HandlePineListWatches(byte? cpuSel)
        {
            if (_ctx.PineClient == null)
                return Responses.Error("PINELISTWATCHES requires PINE mode (connect to PCSX2 first)");

            byte sel = cpuSel ?? 0xFF;

            try
            {
                var watches = _ctx.PineClient!.ListWatches(sel);
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
                return Responses.Error($"PINE ListWatches failed: {ex.Message}");
            }
        }

        // ── PINECLEARALLWATCHES ───────────────────────────────────────────────

        /// <summary>
        /// Handle PINECLEARALLWATCHES — removes every active hardware memory watchpoint
        /// on all CPUs via the PINE MsgClearAllWatches (0x31) opcode.
        /// </summary>
        private string HandlePineClearAllWatches()
        {
            if (_ctx.PineClient == null)
                return Responses.Error("PINECLEARALLWATCHES requires PINE mode (connect to PCSX2 first)");

            try
            {
                _ctx.PineClient!.ClearAllWatches();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "PINECLEARALLWATCHES",
                    Message = "All PINE hardware watchpoints cleared",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE ClearAllWatches failed: {ex.Message}");
            }
        }

        // ── PINESAVESCREENSHOT ────────────────────────────────────────────────

        /// <summary>
        /// Handle PINESAVESCREENSHOT — triggers a screenshot save in PCSX2 via
        /// the PINE MsgSaveScreenshot (0x32) opcode.
        /// </summary>
        private string HandlePineSaveScreenshot()
        {
            if (_ctx.PineClient == null)
                return Responses.Error("PINESAVESCREENSHOT requires PINE mode (connect to PCSX2 first)");

            try
            {
                _ctx.PineClient!.SaveScreenshot();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "PINESAVESCREENSHOT",
                    Message = "Screenshot saved in PCSX2",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE SaveScreenshot failed: {ex.Message}");
            }
        }

        // ── PINESETAUDIOMUTED / PINEGETAUDIOSTATE ─────────────────────────────

        /// <summary>
        /// Handle PINESETAUDIOMUTED (AudioMuted required) and PINEGETAUDIOSTATE —
        /// mute/unmute or query PCSX2's audio output via the PINE MsgSetAudioMuted
        /// (0x45) / MsgGetAudioState (0x46) opcodes. Both reply with the resulting
        /// state, since PCSX2's "Mute All Sound" setting can veto an unmute.
        /// </summary>
        private string HandlePineAudio(bool? muted, string command)
        {
            if (_ctx.PineClient == null)
                return Responses.Error($"{command} requires PINE mode (connect to PCSX2 first)");
            if (command == "PINESETAUDIOMUTED" && muted == null)
                return Responses.Error("AudioMuted is required for PINESETAUDIOMUTED");

            try
            {
                var state = muted is bool m
                    ? _ctx.PineClient!.SetAudioMuted(m)
                    : _ctx.PineClient!.GetAudioState();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success              = true,
                    Command              = command,
                    AudioMuted           = state.Muted,
                    AudioMutedInSettings = state.MutedInSettings,
                    Message              = state.Muted
                        ? (state.MutedInSettings ? "Audio muted (Mute All Sound is on in PCSX2 settings)" : "Audio muted")
                        : "Audio on",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE audio command failed: {ex.Message} (needs the CodeDesigner PCSX2 build with opcodes 0x45/0x46)");
            }
        }
    }
}
