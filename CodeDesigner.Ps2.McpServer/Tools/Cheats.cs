using System.ComponentModel;
using CodeDesigner.Ps2.CheatEngine;
using CodeDesigner.Ps2.McpServer;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // Cheat codes
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "apply_cheats")]
    [DynamicDescription("apply_cheats_desc")]
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
    [DynamicDescription("port_cheats_desc")]
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
    [DynamicDescription("detect_cheat_format_desc")]
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
    [DynamicDescription("convert_cheat_desc")]
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

    // -------------------------------------------------------------------------
    // CDS cheat engine (live injection into running emulator)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "inject_cheat_engine")]
    [DynamicDescription("inject_cheat_engine_desc")]
    [Description("Inject the CDS MIPS cheat engine into the running PS2 emulator and activate a set of cheats. " +
                 "The engine is written into EE RAM and hooked at a stable call site in game code so it runs every frame. " +
                 "Supply cheats as a CDS source block (preferred) or as raw GameShark codes.")]
    public async Task<string> InjectCheatEngine(
        [Description("Virtual address (hex, e.g. '0x001A3BC0') in game code where the engine hook is installed. " +
                     "Choose a frequently-called function such as a frame-update routine.")] string hookAddress,
        [Description("CDS source code defining the cheats to activate (freeze/cond blocks). " +
                     "Takes precedence over cheatCodes when both are supplied.")] string? cdsSource = null,
        [Description("Raw GameShark/Action Replay cheat codes to activate (used when cdsSource is not provided).")] string? cheatCodes = null,
        [Description("Optional base address (hex) in EE RAM where the engine binary is installed. " +
                     "Defaults to 0x000C0000 if omitted.")] string? engineBase = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hookAddress))
            return "Error: hookAddress is required";
        if (string.IsNullOrWhiteSpace(cdsSource) && string.IsNullOrWhiteSpace(cheatCodes))
            return "Error: provide cdsSource or cheatCodes";

        uint? parsedHook    = TryParseAddress(hookAddress);
        uint? parsedBase    = TryParseAddress(engineBase);

        if (parsedHook is null)
            return $"Error: cannot parse hookAddress '{hookAddress}'";

        var json = await bridge.SendAsync(
            new
            {
                Command       = "INJECTCHEATENGINE",
                Data          = cheatCodes,
                CdsSource     = cdsSource,
                HookAddress   = parsedHook,
                EngineBase    = parsedBase
            },
            cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "update_cheat_engine")]
    [DynamicDescription("update_cheat_engine_desc")]
    [Description("Update the active cheat table in the already-injected CDS cheat engine without re-hooking. " +
                 "The engine picks up the new values on its next iteration. " +
                 "inject_cheat_engine must have been called first.")]
    public async Task<string> UpdateCheatEngine(
        [Description("CDS source code defining the updated cheats (freeze/cond blocks). " +
                     "Takes precedence over cheatCodes when both are supplied.")] string? cdsSource = null,
        [Description("Raw GameShark/Action Replay cheat codes to write into the table (used when cdsSource is not provided).")] string? cheatCodes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cdsSource) && string.IsNullOrWhiteSpace(cheatCodes))
            return "Error: provide cdsSource or cheatCodes";

        var json = await bridge.SendAsync(
            new { Command = "UPDATECHEATENGINE", Data = cheatCodes, CdsSource = cdsSource },
            cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "remove_cheat_engine")]
    [DynamicDescription("remove_cheat_engine_desc")]
    [Description("Remove the injected CDS cheat engine from the running PS2 emulator, restoring the original instructions at the hook point. " +
                 "inject_cheat_engine must have been called first.")]
    public async Task<string> RemoveCheatEngine(CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "REMOVECHEATENGINE" },
            cancellationToken);

        return SimpleMessage(json);
    }
}
