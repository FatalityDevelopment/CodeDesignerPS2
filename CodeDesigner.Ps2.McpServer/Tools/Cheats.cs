using System.ComponentModel;
using CodeDesigner.Ps2.CheatEngine;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // Cheat codes
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "apply_cheats")]
    [Description("Apply PS2 GameShark/Action Replay cheat codes to the live emulator memory. Parse and execute a block of cheat codes against the running game.")]
    public async Task<string> ApplyCheats(
        [Description("Multi-line GameShark/Action Replay cheat codes in raw format (e.g. '20123456 000003E8').")] string cheatCodes,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "APPLYCHEATS", Data = cheatCodes },
            cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "port_cheats")]
    [Description("Port PS2 GameShark cheat codes from one game revision to another by scanning two memory dumps for matching byte patterns and recalculating addresses.")]
    public async Task<string> PortCheats(
        [Description("Multi-line GameShark cheat codes to port (raw format).")] string cheatCodes,
        [Description("Absolute path to the source EE RAM dump (.bin file, typically EEmem.bin from a snapshot).")] string sourceDumpPath,
        [Description("Absolute path to the target EE RAM dump (.bin file) to port the codes into.")] string targetDumpPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "PORTCHEATS", Data = cheatCodes, SourceDumpPath = sourceDumpPath, TargetDumpPath = targetDumpPath },
            cancellationToken);

        return FormatPortCheats(json);
    }

    // -------------------------------------------------------------------------
    // Cheat code conversion (no emulator required — pure offline processing)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "detect_cheat_format")]
    [Description("Heuristically detect the encryption format of a block of PS2 cheat codes. Returns the most likely format (Raw, CodeBreaker, GameShark, etc.).")]
    public Task<string> DetectCheatFormat(
        [Description("Multi-line block of PS2 cheat codes whose format should be identified.")] string codes)
    {
        if (string.IsNullOrWhiteSpace(codes))
            return Task.FromResult("Error: no codes provided");

        try
        {
            var format = CheatFormatDetector.Detect(codes);
            return Task.FromResult(format.ToString());
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    [McpServerTool(Name = "convert_cheat")]
    [Description("Convert PS2 cheat codes between the unencrypted raw format and a device-specific encrypted format (CodeBreaker, GameShark, Action Replay).")]
    public Task<string> ConvertCheat(
        [Description("Multi-line block of PS2 cheat codes to convert.")] string codes,
        [Description("Conversion direction: 'toRaw' (decrypt to raw hex) or 'toEngine' (encrypt to device format).")] string direction,
        [Description("Target/source cheat engine format. Required for 'toEngine'. " +
                     "Omit or pass empty string for 'toRaw' to auto-detect. " +
                     "Valid values: Raw, ActionReplay1, ActionReplay2, ActionReplayMax, GameSharkV3, CodeBreakerV1, CodeBreakerV7.")] string? targetFormat = null)
    {
        if (string.IsNullOrWhiteSpace(codes))
            return Task.FromResult("Error: no codes provided");
        if (string.IsNullOrWhiteSpace(direction))
            return Task.FromResult("Error: direction must be 'toRaw' or 'toEngine'");

        try
        {
            string result;

            if (string.Equals(direction, "toRaw", StringComparison.OrdinalIgnoreCase))
            {
                ICheatCodeConverter sourceConverter = string.IsNullOrWhiteSpace(targetFormat)
                    ? CheatFormatDetector.CreateConverter(CheatFormatDetector.Detect(codes))
                    : CheatFormatDetector.CreateConverter(ParseFormat(targetFormat));

                var rawCodes = sourceConverter.Decode(codes);
                result = new RawConverter().Encode(rawCodes);
            }
            else if (string.Equals(direction, "toEngine", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(targetFormat))
                    return Task.FromResult("Error: targetFormat is required when direction is 'toEngine'");

                var format = ParseFormat(targetFormat);
                var rawCodes = new RawConverter().Decode(codes);
                result = CheatFormatDetector.CreateConverter(format).Encode(rawCodes);
            }
            else
            {
                return Task.FromResult("Error: direction must be 'toRaw' or 'toEngine'");
            }

            return Task.FromResult(string.IsNullOrWhiteSpace(result)
                ? "(no output — check that the input codes are in the expected format)"
                : result);
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }
}
