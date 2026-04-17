using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CodeDesigner.Ps2.CheatEngine;
using CodeDesigner.Ps2.Emulator;
using CheatPorterLib = CodeDesigner.Ps2.CheatEngine.CheatPorter;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        /// <summary>
        /// Handle APPLYCHEATS command — parses PS2 GameShark/Action Replay cheat codes
        /// from the supplied text and writes them into the live emulator RAM.
        /// In PINE mode, patches are written to PCSX2 memory via the PINE IPC socket.
        /// </summary>
        private string HandleApplyCheats(string? cheatText)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
            if (string.IsNullOrWhiteSpace(cheatText))
                return CreateErrorResponse("No cheat data provided");

            try
            {
                IByteEditor byteEditor = IsPineMode
                    ? (IByteEditor)new PinePcsx2ByteEditor(PineClient!)
                    : new PS2ByteEditor(Scheduler!);
                var engine = new CodeDesigner.Ps2.CheatEngine.CheatEngine(byteEditor);
                var cheatList = new CheatList(cheatText);
                engine.PatchMemory(cheatList);
                return CreateSuccessResponse("Cheats applied successfully");
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ApplyCheats error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle PORTCHEATS command — delegates to <see cref="CodeDesigner.Ps2.CheatEngine.CheatPorter"/>
        /// to port GameShark cheat codes from one EE-RAM dump to another.
        /// </summary>
        private string HandlePortCheats(string? cheatText, string? sourcePath, string? targetPath)
        {
            if (string.IsNullOrWhiteSpace(cheatText))
                return CreateErrorResponse("No cheat data provided");
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return CreateErrorResponse($"Source dump not found: {sourcePath}");
            if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
                return CreateErrorResponse($"Target dump not found: {targetPath}");

            byte[] sourceData;
            byte[] targetData;
            try
            {
                sourceData = File.ReadAllBytes(sourcePath);
                targetData = File.ReadAllBytes(targetPath);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Failed to read dump files: {ex.Message}");
            }

            // Parse cheat blocks using the existing infrastructure.
            var cheatList = new CheatList(cheatText);
            List<CheatBlock> blocks;
            try { blocks = cheatList.Parse(); }
            catch (Exception ex) { return CreateErrorResponse($"Failed to parse cheat codes: {ex.Message}"); }

            var logLines = new List<string>();
            logLines.Add($"[PORTCHEATS] Source: {sourcePath}");
            logLines.Add($"[PORTCHEATS] Target: {targetPath}");
            logLines.Add(string.Empty);

            var allResults = CheatPorterLib.Port(sourceData, targetData, blocks, logLines);
            int foundCount = allResults.Count(r => r.Found);

            var response = new CommandResponse
            {
                Success          = true,
                Command          = "PORTCHEATS",
                Data             = string.Join("\n", logLines),
                PortCheatResults = allResults,
            };
            return JsonSerializer.Serialize(response);
        }

        /// <summary>
        /// Handle CONVERTCHEAT command — converts PS2 cheat codes between raw format
        /// and a device-specific encrypted format using the CheatConverter module.
        /// </summary>
        /// <param name="data">Multi-line cheat code text to convert.</param>
        /// <param name="format">
        /// Target cheat engine format name. Recognised values (case-insensitive):
        /// Raw, ActionReplay1, ActionReplay2, ActionReplayMax, GameSharkV3, CodeBreakerV1, CodeBreakerV7.
        /// </param>
        /// <param name="direction">
        /// Conversion direction: "toRaw" decrypts device codes → raw hex pairs;
        /// "toEngine" encrypts raw hex pairs → device format.
        /// </param>
        private string HandleConvertCheat(string? data, string? format, string? direction)
        {
            if (string.IsNullOrWhiteSpace(data))
                return CreateErrorResponse("No cheat data provided");

            try
            {
                // Parse the target format (default to auto-detect when not specified)
                CheatFormat targetFormat = CheatFormat.Raw;
                if (!string.IsNullOrWhiteSpace(format))
                {
                    if (!Enum.TryParse<CheatFormat>(format, ignoreCase: true, out targetFormat))
                        return CreateErrorResponse($"Unknown cheat format '{format}'. Valid values: Raw, ActionReplay1, ActionReplay2, ActionReplayMax, GameSharkV3, CodeBreakerV1, CodeBreakerV7");
                }

                string result;
                if (string.Equals(direction, "toRaw", StringComparison.OrdinalIgnoreCase))
                {
                    // Auto-detect source format and decode to raw, or use specified format
                    ICheatCodeConverter converter = string.IsNullOrWhiteSpace(format)
                        ? CheatFormatDetector.CreateConverter(CheatFormatDetector.Detect(data))
                        : CheatFormatDetector.CreateConverter(targetFormat);

                    var rawCodes = converter.Decode(data);
                    var rawConverter = new RawConverter();
                    result = rawConverter.Encode(rawCodes);
                }
                else
                {
                    // "toEngine": decode from raw then encode to target format
                    var rawConverter = new RawConverter();
                    var rawCodes = rawConverter.Decode(data);
                    var targetConverter = CheatFormatDetector.CreateConverter(targetFormat);
                    result = targetConverter.Encode(rawCodes);
                }

                var response = new CommandResponse { Success = true, Command = "CONVERTCHEAT", Data = result };
                return JsonSerializer.Serialize(response);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ConvertCheat error: {ex.Message}");
            }
        }
    }
}
