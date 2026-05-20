using System;
using System.Collections.Generic;
using CodeDesigner.Ps2.Common;

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
            try
            {
                // Drop the cached analysis snapshot — registers and memory will
                // both diverge as soon as execution resumes, so any cached
                // CpuState becomes meaningless.
                _ctx.AnalysisState?.Invalidate();
                _ctx.Client.Resume();
                return Responses.Success("Resumed");
            }
            catch (Exception ex) { return Responses.Error($"Resume error: {ex.Message}"); }
        }

        private string HandleStep()
        {
            if (_ctx.Client == null)
                return Responses.Error("Emulator not running — no debug client attached");
            try
            {
                uint newPc        = _ctx.Client.Step();
                uint normalizedPc = AddressTranslator.NormalizePcToKseg0(newPc);

                // Best-effort: refresh the analysis mirror (registers definitely
                // changed) and compute a per-row hint for the new PC. The
                // webview's existing _applyAnalysisUpdates path picks the
                // dictionary up and patches that single row's comment column —
                // no surrounding rows touched.
                Dictionary<string, string>? analysis = null;
                if (_ctx.AnalysisState is not null)
                {
                    try
                    {
                        _ctx.AnalysisState.RefreshFromLive(force: true);
                        var snap = _ctx.AnalysisState.TryAcquire();
                        if (snap is not null)
                        {
                            byte[] iBytes = _ctx.Client.ReadBytes(normalizedPc, 4);
                            if (iBytes is { Length: >= 4 })
                            {
                                uint instr = BitConverter.ToUInt32(iBytes, 0);
                                // Use the *mirrored* analysis bus, not the live PCSX2
                                // PINE bus — comment-column hints must never dereference
                                // arbitrary PCSX2 addresses.
                                var analysisBus = _ctx.AnalysisClient != null
                                    ? new ByteEditorMemoryBusAdapter(new DebugClientByteEditor(_ctx.AnalysisClient))
                                    : null;
                                string hint = MipsAnalyzer.AnalyzeSingleInstruction(
                                    instr, normalizedPc, snap.Cpu,
                                    bus: analysisBus,
                                    lookupLabel: a => _ctx.Labels.TryGetValue(a, out var e) ? e.Label : null,
                                    includeStateHints: true);
                                if (!string.IsNullOrEmpty(hint))
                                    analysis = new() { [normalizedPc.ToString("X8")] = hint };
                            }
                        }
                    }
                    catch
                    {
                        // Analysis is best-effort — never block STEP on it.
                    }
                }

                return Responses.Json(new CommandResponse
                {
                    Success             = true,
                    Command             = "STEP",
                    Paused              = true,
                    PC                  = normalizedPc,
                    InstructionAnalysis = analysis,
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
