using System;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        /// <summary>
        /// Handle PAUSE — pauses the EE via the active <see cref="IDebugClient"/>.
        /// </summary>
        private string HandlePause()
        {
            if (Client == null)
                return CreateErrorResponse("Emulator not running — no debug client attached");
            try   { Client.Pause(); return CreateSuccessResponse("Paused"); }
            catch (Exception ex) { return CreateErrorResponse($"Pause error: {ex.Message}"); }
        }

        /// <summary>
        /// Handle RESUME — resumes EE execution. Clients (both PCSX2 PINE and
        /// the built-in InternalPineSocket) handle the "skip current breakpoint"
        /// behaviour server-side so the BP doesn't immediately re-fire.
        /// </summary>
        private string HandleResume()
        {
            if (Client == null)
                return CreateErrorResponse("Emulator not running — no debug client attached");
            try   { Client.Resume(); return CreateSuccessResponse("Resumed"); }
            catch (Exception ex) { return CreateErrorResponse($"Resume error: {ex.Message}"); }
        }

        /// <summary>
        /// Handle STEP — executes one EE instruction and returns the new PC.
        /// </summary>
        private string HandleStep()
        {
            if (Client == null)
                return CreateErrorResponse("Emulator not running — no debug client attached");
            try
            {
                uint newPc = Client.Step();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "STEP",
                    Paused  = true,
                    PC      = NormalizePcToKseg0(newPc),
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"Step error: {ex.Message}"); }
        }

        /// <summary>
        /// Normalize a virtual EE address to its KSEG0 representation so PCs
        /// returned to clients always use the 0x8xxxxxxx / 0x9xxxxxxx form.
        /// </summary>
        private static uint NormalizePcToKseg0(uint vaddr)
        {
            if ((vaddr & 0xE0000000u) == 0xA0000000u)
                return (vaddr & 0x1FFFFFFFu) | 0x80000000u;
            return vaddr;
        }

        /// <summary>
        /// Normalize an address to its KSEG0 alias for breakpoint storage.
        /// PCSX2 PINE expects KSEG0/KSEG1 virtual addresses; InternalPineSocket
        /// also accepts KSEG0 (and translates to physical internally on the
        /// EmulatorCore side). Sending KSEG0 keeps both routes uniform.
        /// </summary>
        private static uint NormalizeBreakpointForClient(uint vaddr)
        {
            if (vaddr >= 0x80000000u && vaddr <= 0xBFFFFFFFu) return vaddr;
            if (vaddr < 0x20000000u) return vaddr | 0x80000000u;
            return vaddr;
        }

        /// <summary>
        /// Normalize an EE address to its physical form (strip KSEG0/KSEG1
        /// segment bits). Retained as a utility for handlers that store
        /// breakpoints in physical form.
        /// </summary>
        private static uint NormalizeBreakpointToPhysical(uint vaddr)
        {
            if (vaddr >= 0x80000000u && vaddr <= 0xBFFFFFFFu)
                return vaddr & 0x1FFFFFFFu;
            return vaddr;
        }

        /// <summary>
        /// Handle SETBP — adds a breakpoint at the given EE address.
        /// </summary>
        private string HandleSetBreakpoint(uint? address)
        {
            if (address == null)
                return CreateErrorResponse("BreakpointAddress is required for SETBP");
            if (Client == null)
                return CreateErrorResponse("Emulator not running — no debug client attached");
            uint normalized = NormalizeBreakpointForClient(address.Value);
            try   { Client.SetBreakpoint(normalized); return CreateSuccessResponse($"Breakpoint set at 0x{normalized:X8}"); }
            catch (Exception ex) { return CreateErrorResponse($"SetBreakpoint error: {ex.Message}"); }
        }

        /// <summary>
        /// Handle CLEARBP — removes the breakpoint at the given EE address.
        /// </summary>
        private string HandleClearBreakpoint(uint? address)
        {
            if (address == null)
                return CreateErrorResponse("BreakpointAddress is required for CLEARBP");
            if (Client == null)
                return CreateErrorResponse("Emulator not running — no debug client attached");
            uint normalized = NormalizeBreakpointForClient(address.Value);
            try   { Client.ClearBreakpoint(normalized); return CreateSuccessResponse($"Breakpoint cleared at 0x{normalized:X8}"); }
            catch (Exception ex) { return CreateErrorResponse($"ClearBreakpoint error: {ex.Message}"); }
        }

        /// <summary>
        /// Handle CLEARBP_ALL — removes all breakpoints.
        /// </summary>
        private string HandleClearAllBreakpoints()
        {
            if (Client == null)
                return CreateErrorResponse("Emulator not running — no debug client attached");
            try   { Client.ClearAllBreakpoints(); return CreateSuccessResponse("All breakpoints cleared"); }
            catch (Exception ex) { return CreateErrorResponse($"ClearAllBreakpoints error: {ex.Message}"); }
        }

        /// <summary>
        /// Handle GETPC — returns current EE PC and paused state. The PC is
        /// normalized to KSEG0 so the client sees a consistent address
        /// regardless of which segment the CPU is executing from.
        /// </summary>
        private string HandleGetPC()
        {
            if (Client == null)
                return CreateErrorResponse("Emulator not running — no debug client attached");
            try
            {
                uint pc = Client.GetProgramCounter(out bool paused);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "GETPC",
                    Paused  = paused,
                    PC      = NormalizePcToKseg0(pc),
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"GetPC error: {ex.Message}"); }
        }
    }
}
