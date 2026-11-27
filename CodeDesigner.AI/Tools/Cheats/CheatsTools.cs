using System.ComponentModel;
using CodeDesigner.AI.Abstractions;
using CodeDesigner.Ps2.CheatEngine;

namespace CodeDesigner.AI.Tools.Cheats;

// PS2 cheat tooling: apply / port / detect / convert / live engine inject.
// Commands match legacy CodeDesigner.Ps2.McpServer/Tools/Cheats.cs.

[CdTool("port_cheats", ToolDomain.Cheats, ToolRisk.ReadOnly,
    "Port cheats between game revisions by scanning two memory dumps for matching patterns. Risk: read-only.")]
public sealed class PortCheatsTool(IWorkspaceBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Multi-line cheat codes to port.")] string cheatCodes,
        [Description("Source EE RAM dump (.bin).")] string sourceDumpPath,
        [Description("Target EE RAM dump (.bin).")] string targetDumpPath,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PORTCHEATS", Data = cheatCodes, SourceDumpPath = sourceDumpPath, TargetDumpPath = targetDumpPath }, ct));
}

[CdTool("detect_cheat_format", ToolDomain.Cheats, ToolRisk.ReadOnly,
    "Detect the encryption format of a cheat-code block (offline). Risk: read-only.")]
public sealed class DetectCheatFormatTool
{
    [ToolEntryPoint] public Task<string> InvokeAsync(
        [Description("Multi-line cheat-code block.")] string codes)
    {
        if (string.IsNullOrWhiteSpace(codes)) return Task.FromResult(CdText.Error("no codes provided."));
        try { return Task.FromResult(CheatFormatDetector.Detect(codes).ToString()); }
        catch (Exception ex) { return Task.FromResult(CdText.Error(ex.Message)); }
    }
}

[CdTool("convert_cheat", ToolDomain.Cheats, ToolRisk.ReadOnly,
    "Convert cheat codes between raw and device-specific formats (CodeBreaker / GameShark / Action Replay). Offline. Risk: read-only.")]
public sealed class ConvertCheatTool
{
    [ToolEntryPoint] public Task<string> InvokeAsync(
        [Description("Multi-line cheat codes to convert.")] string codes,
        [Description("Direction: 'toRaw' or 'toEngine'.")] string direction,
        [Description("Target/source format. Required for toEngine; omit on toRaw to auto-detect. Values: Raw, ActionReplay1, ActionReplay2, ActionReplayMax, GameSharkV3, CodeBreakerV1, CodeBreakerV7.")] string? targetFormat = null)
    {
        if (string.IsNullOrWhiteSpace(codes)) return Task.FromResult(CdText.Error("no codes provided."));
        if (string.IsNullOrWhiteSpace(direction)) return Task.FromResult(CdText.Error("direction must be 'toRaw' or 'toEngine'."));
        try
        {
            string result;
            if (string.Equals(direction, "toRaw", StringComparison.OrdinalIgnoreCase))
            {
                ICheatCodeConverter source = string.IsNullOrWhiteSpace(targetFormat)
                    ? CheatFormatDetector.CreateConverter(CheatFormatDetector.Detect(codes))
                    : CheatFormatDetector.CreateConverter(ParseFormat(targetFormat));
                var raw = source.Decode(codes);
                result = new RawConverter().Encode(raw);
            }
            else if (string.Equals(direction, "toEngine", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(targetFormat))
                    return Task.FromResult(CdText.Error("targetFormat is required when direction is 'toEngine'."));
                var format = ParseFormat(targetFormat);
                var raw = new RawConverter().Decode(codes);
                result = CheatFormatDetector.CreateConverter(format).Encode(raw);
            }
            else return Task.FromResult(CdText.Error("direction must be 'toRaw' or 'toEngine'."));

            return Task.FromResult(string.IsNullOrWhiteSpace(result)
                ? "(no output — check that the input codes are in the expected format)"
                : result);
        }
        catch (Exception ex) { return Task.FromResult(CdText.Error(ex.Message)); }
    }

    private static CheatFormat ParseFormat(string name)
    {
        if (!Enum.TryParse<CheatFormat>(name, ignoreCase: true, out var fmt))
            throw new ArgumentException(
                $"Unknown cheat format '{name}'. Valid: Raw, ActionReplay1, ActionReplay2, ActionReplayMax, GameSharkV3, CodeBreakerV1, CodeBreakerV7.");
        return fmt;
    }
}

