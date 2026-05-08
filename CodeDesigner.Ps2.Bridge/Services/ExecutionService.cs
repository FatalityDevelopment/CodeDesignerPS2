using System;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Pause / Resume / Step and breakpoint management. All commands route
    /// through the active <see cref="IDebugClient"/>.
    /// </summary>
    internal sealed class ExecutionService : ICommandService
    {
        private readonly IBridgeContext _ctx;

        public ExecutionService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(_ => HandlePause(),                                    "PAUSE");
            reg.Add(_ => HandleResume(),                                   "RESUME");
            reg.Add(_ => HandleStep(),                                     "STEP");
            reg.Add(c => HandleSetBreakpoint(c.BreakpointAddress),         "SETBP");
            reg.Add(c => HandleClearBreakpoint(c.BreakpointAddress),       "CLEARBP");
            reg.Add(_ => HandleClearAllBreakpoints(),                      "CLEARBP_ALL");
            reg.Add(_ => HandleGetPC(),                                    "GETPC");
        }

        // ── Handlers ─────────────────────────────────────────────────────────

        private string HandlePause()
        {
            if (_ctx.Client == null)
                return Responses.Error("Emulator not running — no debug client attached");
            try   { _ctx.Client.Pause(); return Responses.Success("Paused"); }
            catch (Exception ex) { return Responses.Error($"Pause error: {ex.Message}"); }
        }

        private string HandleResume()
        {
            if (_ctx.Client == null)
                return Responses.Error("Emulator not running — no debug client attached");
            try   { _ctx.Client.Resume(); return Responses.Success("Resumed"); }
            catch (Exception ex) { return Responses.Error($"Resume error: {ex.Message}"); }
        }

        private string HandleStep()
        {
            if (_ctx.Client == null)
                return Responses.Error("Emulator not running — no debug client attached");
            try
            {
                uint newPc = _ctx.Client.Step();
                return Responses.Json(new CommandResponse
                {
                    Success = true,
                    Command = "STEP",
                    Paused  = true,
                    PC      = AddressTranslator.NormalizePcToKseg0(newPc),
                });
            }
            catch (Exception ex) { return Responses.Error($"Step error: {ex.Message}"); }
        }

        private string HandleSetBreakpoint(uint? address)
        {
            if (address == null) return Responses.Error("BreakpointAddress is required for SETBP");
            if (_ctx.Client == null) return Responses.Error("Emulator not running — no debug client attached");
            uint normalized = AddressTranslator.NormalizeBreakpointForClient(address.Value);
            try   { _ctx.Client.SetBreakpoint(normalized); return Responses.Success($"Breakpoint set at 0x{normalized:X8}"); }
            catch (Exception ex) { return Responses.Error($"SetBreakpoint error: {ex.Message}"); }
        }

        private string HandleClearBreakpoint(uint? address)
        {
            if (address == null) return Responses.Error("BreakpointAddress is required for CLEARBP");
            if (_ctx.Client == null) return Responses.Error("Emulator not running — no debug client attached");
            uint normalized = AddressTranslator.NormalizeBreakpointForClient(address.Value);
            try   { _ctx.Client.ClearBreakpoint(normalized); return Responses.Success($"Breakpoint cleared at 0x{normalized:X8}"); }
            catch (Exception ex) { return Responses.Error($"ClearBreakpoint error: {ex.Message}"); }
        }

        private string HandleClearAllBreakpoints()
        {
            if (_ctx.Client == null) return Responses.Error("Emulator not running — no debug client attached");
            try   { _ctx.Client.ClearAllBreakpoints(); return Responses.Success("All breakpoints cleared"); }
            catch (Exception ex) { return Responses.Error($"ClearAllBreakpoints error: {ex.Message}"); }
        }

        private string HandleGetPC()
        {
            if (_ctx.Client == null)
                return Responses.Error("Emulator not running — no debug client attached");
            try
            {
                uint pc = _ctx.Client.GetProgramCounter(out bool paused);
                return Responses.Json(new CommandResponse
                {
                    Success = true,
                    Command = "GETPC",
                    Paused  = paused,
                    PC      = AddressTranslator.NormalizePcToKseg0(pc),
                });
            }
            catch (Exception ex) { return Responses.Error($"GetPC error: {ex.Message}"); }
        }
    }
}
