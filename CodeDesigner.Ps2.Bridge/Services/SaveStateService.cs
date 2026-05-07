using System;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class SaveStateService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public SaveStateService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleSaveStateFile(c.FilePath),    "SAVESTATETOFILE", "SAVESTATEFILE");
            reg.Add(c => HandleLoadStateFile(c.FilePath),    "LOADSTATEFROMFILE", "LOADSTATEFILE");
            reg.Add(_ => HandleReset(),                      "RESET");
            reg.Add(_ => HandleGetFps(),                     "GETFPS");
            reg.Add(c => HandleSetLimiterMode(c.LimiterMode),"SETLIMITER", "SETLIMITERMODE");
            reg.Add(c => HandleFrameAdvance(c.FrameCount),   "FRAMEADVANCE");
            reg.Add(_ => HandlePineVersion(),                "PINEVERSION");
            reg.Add(c => HandlePineSaveStateSlot(c.StateSlot), "PINESAVESTATE");
            reg.Add(c => HandlePineLoadStateSlot(c.StateSlot), "PINELOADSTATE");
        }

        private string HandleSaveStateFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return Responses.Error("FilePath is required for SAVESTATETOFILE");
            if (_ctx.PineClient != null)
            {
                try   { _ctx.PineClient!.SaveStateFile(filePath); return Responses.Success($"State saved to '{filePath}'"); }
                catch (Exception ex) { return Responses.Error($"PINE SaveStateFile failed: {ex.Message}"); }
            }
            return Responses.Error("SAVESTATETOFILE requires PINE mode; use SNAPSHOT for the internal emulator");
        }

        private string HandleLoadStateFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return Responses.Error("FilePath is required for LOADSTATEFROMFILE");
            if (_ctx.PineClient != null)
            {
                try
                {
                    bool ok = _ctx.PineClient!.LoadStateFile(filePath);
                    return ok
                        ? Responses.Success($"State loaded from '{filePath}'")
                        : Responses.Error($"PINE LoadStateFile failed for '{filePath}'");
                }
                catch (Exception ex) { return Responses.Error($"PINE LoadStateFile failed: {ex.Message}"); }
            }
            return Responses.Error("LOADSTATEFROMFILE requires PINE mode; use LOADSNAPSHOT for the internal emulator");
        }

        /// <summary>
        /// Reads the current running thread from CPU state and attempts to walk
        /// the PS2 EE OS thread control block list in EE RAM.  Each TCB is 128
        /// bytes; status byte at offset 0: 0x01=running, 0x02=ready, 0x04=wait,
        /// 0x08=suspend, 0x10=dormant.  The thread table is located by reading
        /// the kernel's internal data starting at physical 0x00000000.
        /// </summary>
        private string HandleReset()
        {
            if (_ctx.PineClient != null)
            {
                try   { _ctx.PineClient.Reset(); return Responses.Success("Reset"); }
                catch (Exception ex) { return Responses.Error($"PINE Reset failed: {ex.Message}"); }
            }
            if (_ctx.InternalClient != null)
            {
                try   { _ctx.InternalClient.RequestRestart(); return Responses.Success("Reset"); }
                catch (Exception ex) { return Responses.Error($"Internal reset failed: {ex.Message}"); }
            }
            return Responses.Error("Emulator not running — no debug client attached");
        }

        private string HandleGetFps()
        {
            if (_ctx.PineClient != null)
            {
                try
                {
                    float fps = _ctx.PineClient!.GetFPS();
                    return JsonSerializer.Serialize(new CommandResponse { Success = true, Command = "GETFPS", FPS = fps });
                }
                catch (Exception ex) { return Responses.Error($"PINE GetFPS failed: {ex.Message}"); }
            }
            return Responses.Error("GETFPS requires PINE mode");
        }

        private string HandleSetLimiterMode(byte? mode)
        {
            if (mode == null) return Responses.Error("LimiterMode is required (0=Nominal, 1=Turbo, 2=Slomo, 3=Unlimited)");
            if (_ctx.PineClient != null)
            {
                try   { _ctx.PineClient!.SetLimiterMode(mode.Value); return Responses.Success($"Limiter mode set to {mode}"); }
                catch (Exception ex) { return Responses.Error($"PINE SetLimiterMode failed: {ex.Message}"); }
            }
            return Responses.Error("SETLIMITER requires PINE mode");
        }

        private string HandleFrameAdvance(byte? numFrames)
        {
            if (numFrames == null || numFrames == 0) return Responses.Error("FrameCount is required and must be ≥ 1");
            if (_ctx.PineClient != null)
            {
                try   { _ctx.PineClient!.FrameAdvance(numFrames.Value); return Responses.Success($"Frame advance by {numFrames} initiated"); }
                catch (Exception ex) { return Responses.Error($"PINE FrameAdvance failed: {ex.Message}"); }
            }
            return Responses.Error("FRAMEADVANCE requires PINE mode");
        }

        private string HandlePineVersion()
        {
            if (_ctx.PineClient == null) return Responses.Error("PINEVERSION requires PINE mode");
            try
            {
                string ver = _ctx.PineClient!.GetVersion();
                return JsonSerializer.Serialize(new CommandResponse { Success = true, Command = "PINEVERSION", Message = ver, Data = ver });
            }
            catch (Exception ex) { return Responses.Error($"PINE GetVersion failed: {ex.Message}"); }
        }

        private string HandlePineSaveStateSlot(byte? slot)
        {
            if (slot == null) return Responses.Error("StateSlot is required (0–9)");
            if (_ctx.PineClient == null) return Responses.Error("PINESAVESTATE requires PINE mode");
            try
            {
                _ctx.PineClient!.SaveState(slot.Value);
                return Responses.Success($"State saved to slot {slot}");
            }
            catch (Exception ex) { return Responses.Error($"PINE SaveState slot {slot} failed: {ex.Message}"); }
        }

        private string HandlePineLoadStateSlot(byte? slot)
        {
            if (slot == null) return Responses.Error("StateSlot is required (0–9)");
            if (_ctx.PineClient == null) return Responses.Error("PINELOADSTATE requires PINE mode");
            try
            {
                _ctx.PineClient!.LoadState(slot.Value);
                return Responses.Success($"State loaded from slot {slot}");
            }
            catch (Exception ex) { return Responses.Error($"PINE LoadState slot {slot} failed: {ex.Message}"); }
        }
    }
}
