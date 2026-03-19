using System;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        private string HandleStepInto()
        {
            if (IsPineMode)
            {
                try   { PineClient!.StepInto(); return CreateSuccessResponse("StepInto initiated"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE StepInto failed: {ex.Message}"); }
            }
            // Non-PINE fallback: behave like a single step (same as STEP command).
            return HandleStep();
        }

        private string HandleStepOver()
        {
            if (IsPineMode)
            {
                try   { PineClient!.StepOver(); return CreateSuccessResponse("StepOver initiated"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE StepOver failed: {ex.Message}"); }
            }
            // Non-PINE fallback: behave like a single step (same as STEP command).
            return HandleStep();
        }

        private string HandleStepOut()
        {
            if (IsPineMode)
            {
                try   { PineClient!.StepOut(); return CreateSuccessResponse("StepOut initiated"); }
                catch (Exception ex) { return CreateErrorResponse($"PINE StepOut failed: {ex.Message}"); }
            }
            // Non-PINE fallback: behave like a single step (same as STEP command).
            return HandleStep();
        }

        private string HandleGetSymbol(uint? address)
        {
            if (address == null) return CreateErrorResponse("Address is required for GETSYMBOL");
            if (IsPineMode)
            {
                try
                {
                    var sym = PineClient!.GetSymbol(address.Value);
                    if (sym == null) return CreateErrorResponse($"No symbol at 0x{address.Value:X8}");
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success       = true,
                        Command       = "GETSYMBOL",
                        SymbolAddress = $"0x{sym.Value.StartAddress:X8}",
                        SymbolName    = sym.Value.Name,
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE GetSymbol failed: {ex.Message}"); }
            }
            return CreateErrorResponse("GETSYMBOL requires PINE mode");
        }
    }
}
