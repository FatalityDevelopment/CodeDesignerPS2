using System;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class SteppingService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public SteppingService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(_ => HandleStepInto(),                "STEPINTO");
            reg.Add(_ => HandleStepOver(),                "STEPOVER");
            reg.Add(_ => HandleStepOut(),                 "STEPOUT");
            reg.Add(c => HandleGetSymbol(c.Address),      "GETSYMBOL");
        }

        // Single-step fallback for non-PCSX2 mode — the StepInto/Over/Out PINE
        // opcodes are PCSX2-specific; the built-in emulator just runs one
        // instruction.
        private string HandleStep()
        {
            if (_ctx.Client == null)
                return Responses.Error("Emulator not running — no debug client attached");
            try
            {
                uint pc = _ctx.Client.Step();
                return Responses.Json(new CommandResponse
                {
                    Success = true, Command = "STEP", Paused = true,
                    PC = AddressTranslator.NormalizePcToKseg0(pc),
                });
            }
            catch (Exception ex) { return Responses.Error($"Step error: {ex.Message}"); }
        }

        private string HandleStepInto()
        {
            if (_ctx.PineClient != null)
            {
                try   { _ctx.PineClient!.StepInto(); return Responses.Success("StepInto initiated"); }
                catch (Exception ex) { return Responses.Error($"PINE StepInto failed: {ex.Message}"); }
            }
            // Non-PINE fallback: behave like a single step (same as STEP command).
            return HandleStep();
        }

        private string HandleStepOver()
        {
            if (_ctx.PineClient != null)
            {
                try   { _ctx.PineClient!.StepOver(); return Responses.Success("StepOver initiated"); }
                catch (Exception ex) { return Responses.Error($"PINE StepOver failed: {ex.Message}"); }
            }
            // Non-PINE fallback: behave like a single step (same as STEP command).
            return HandleStep();
        }

        private string HandleStepOut()
        {
            if (_ctx.PineClient != null)
            {
                try   { _ctx.PineClient!.StepOut(); return Responses.Success("StepOut initiated"); }
                catch (Exception ex) { return Responses.Error($"PINE StepOut failed: {ex.Message}"); }
            }
            // Non-PINE fallback: behave like a single step (same as STEP command).
            return HandleStep();
        }

        private string HandleGetSymbol(uint? address)
        {
            if (address == null) return Responses.Error("Address is required for GETSYMBOL");
            if (_ctx.PineClient != null)
            {
                try
                {
                    var sym = _ctx.PineClient!.GetSymbol(address.Value);
                    if (sym == null) return Responses.Error($"No symbol at 0x{address.Value:X8}");
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success       = true,
                        Command       = "GETSYMBOL",
                        SymbolAddress = $"0x{sym.Value.StartAddress:X8}",
                        SymbolName    = sym.Value.Name,
                    });
                }
                catch (Exception ex) { return Responses.Error($"PINE GetSymbol failed: {ex.Message}"); }
            }
            return Responses.Error("GETSYMBOL requires PINE mode");
        }
    }
}
