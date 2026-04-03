using System;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        private string HandleSaveStateFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return CreateErrorResponse("FilePath is required for SAVESTATETOFILE");
            if (IsPineMode)
            {
                try   { PineClient!.SaveStateFile(filePath); return CreateSuccessResponse($"State saved to '{filePath}'"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE SaveStateFile failed: {ex.Message}"); }
            }
            return CreateErrorResponse("SAVESTATETOFILE requires PINE mode; use SNAPSHOT for the internal emulator");
        }

        private string HandleLoadStateFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return CreateErrorResponse("FilePath is required for LOADSTATEFROMFILE");
            if (IsPineMode)
            {
                try
                {
                    bool ok = PineClient!.LoadStateFile(filePath);
                    return ok
                        ? CreateSuccessResponse($"State loaded from '{filePath}'")
                        : CreateErrorResponse($"PINE LoadStateFile failed for '{filePath}'");
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE LoadStateFile failed: {ex.Message}"); }
            }
            return CreateErrorResponse("LOADSTATEFROMFILE requires PINE mode; use LOADSNAPSHOT for the internal emulator");
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
            if (IsPineMode)
            {
                try   { PineClient!.Reset(); return CreateSuccessResponse("Reset"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE Reset failed: {ex.Message}"); }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");
            Scheduler.Reset();
            return CreateSuccessResponse("Reset");
        }

        private string HandleGetFps()
        {
            if (IsPineMode)
            {
                try
                {
                    float fps = PineClient!.GetFPS();
                    return JsonSerializer.Serialize(new CommandResponse { Success = true, Command = "GETFPS", FPS = fps });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE GetFPS failed: {ex.Message}"); }
            }
            return CreateErrorResponse("GETFPS requires PINE mode");
        }

        private string HandleSetLimiterMode(byte? mode)
        {
            if (mode == null) return CreateErrorResponse("LimiterMode is required (0=Nominal, 1=Turbo, 2=Slomo, 3=Unlimited)");
            if (IsPineMode)
            {
                try   { PineClient!.SetLimiterMode(mode.Value); return CreateSuccessResponse($"Limiter mode set to {mode}"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE SetLimiterMode failed: {ex.Message}"); }
            }
            return CreateErrorResponse("SETLIMITER requires PINE mode");
        }

        private string HandleFrameAdvance(byte? numFrames)
        {
            if (numFrames == null || numFrames == 0) return CreateErrorResponse("FrameCount is required and must be ≥ 1");
            if (IsPineMode)
            {
                try   { PineClient!.FrameAdvance(numFrames.Value); return CreateSuccessResponse($"Frame advance by {numFrames} initiated"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE FrameAdvance failed: {ex.Message}"); }
            }
            return CreateErrorResponse("FRAMEADVANCE requires PINE mode");
        }

        private string HandlePineVersion()
        {
            if (!IsPineMode) return CreateErrorResponse("PINEVERSION requires PINE mode");
            try
            {
                string ver = PineClient!.GetVersion();
                return JsonSerializer.Serialize(new CommandResponse { Success = true, Command = "PINEVERSION", Message = ver, Data = ver });
            }
            catch (Exception ex) { return CreateErrorResponse($"PINE GetVersion failed: {ex.Message}"); }
        }

        private string HandlePineSaveStateSlot(byte? slot)
        {
            if (slot == null) return CreateErrorResponse("StateSlot is required (0–9)");
            if (!IsPineMode) return CreateErrorResponse("PINESAVESTATE requires PINE mode");
            try
            {
                PineClient!.SaveState(slot.Value);
                return CreateSuccessResponse($"State saved to slot {slot}");
            }
            catch (Exception ex) { return CreateErrorResponse($"PINE SaveState slot {slot} failed: {ex.Message}"); }
        }

        private string HandlePineLoadStateSlot(byte? slot)
        {
            if (slot == null) return CreateErrorResponse("StateSlot is required (0–9)");
            if (!IsPineMode) return CreateErrorResponse("PINELOADSTATE requires PINE mode");
            try
            {
                PineClient!.LoadState(slot.Value);
                return CreateSuccessResponse($"State loaded from slot {slot}");
            }
            catch (Exception ex) { return CreateErrorResponse($"PINE LoadState slot {slot} failed: {ex.Message}"); }
        }
    }
}
