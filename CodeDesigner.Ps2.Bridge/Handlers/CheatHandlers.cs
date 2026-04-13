using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CodeDesigner.Ps2.CheatEngine;
using CodeDesigner.Ps2.Emulator;
using Microsoft.EntityFrameworkCore;
using CheatPorterLib = CodeDesigner.Ps2.CheatEngine.CheatPorter;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── CDS Cheat Engine instance (persistent across commands) ────────────
        private CdsCheatEngine? _cdsCheatEngine;
        /// <summary>
        /// Handle GETGAMECHEATS command — returns cheat entries from the database for the
        /// game identified by <paramref name="serial"/>.  The serial is normalised before
        /// lookup (SCUS_97130 → SCUS-97130).  Returns the first matching CheatList's
        /// cheats together with the game title and list title so the frontend can display
        /// them without reading from the local YAML archive.
        /// </summary>
        private string HandleGetGameCheats(string? serial)
        {
            if (Db == null)
                return CreateErrorResponse("Database is not available");
            if (string.IsNullOrWhiteSpace(serial))
                return CreateErrorResponse("GameSerial is required for GETGAMECHEATS");

            var normalized = DbLabelRepository.NormalizeSerial(serial);

            // Resolve the GameDisc row by serial.
            var disc = Db.GameDiscs
                .Include(d => d.Game)
                .Include(d => d.CheatLists!)
                    .ThenInclude(cl => cl.Cheats)
                        .ThenInclude(c => c.CheatLines)
                .FirstOrDefault(d =>
                    d.Serial != null &&
                    d.Serial.Replace("_", "-").ToUpper() == normalized);

            if (disc == null)
            {
                // No disc found — return empty success so the frontend falls back gracefully.
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success        = true,
                    Command        = "GETGAMECHEATS",
                    GameCheats     = [],
                    GameTitle      = null,
                    CheatListTitle = null,
                });
            }

            // Pick the first associated CheatList (typically only one per disc).
            var cheatList = disc.CheatLists?.OrderBy(cl => cl.Id).FirstOrDefault();

            var cheats = cheatList?.Cheats
                .OrderBy(c => c.SortOrder)
                .Select(c => new CheatInfo
                {
                    Name   = c.Label,
                    Author = string.IsNullOrWhiteSpace(c.Author) ? null : c.Author,
                    Notes  = string.IsNullOrWhiteSpace(c.Description) ? null : c.Description,
                    Codes  = c.CheatLines
                               .OrderBy(l => l.SortOrder)
                               .Select(l => l.Code)
                               .ToList(),
                })
                .ToList() ?? [];

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success        = true,
                Command        = "GETGAMECHEATS",
                GameCheats     = cheats,
                GameTitle      = disc.Game?.Title,
                CheatListTitle = cheatList?.Title,
            });
        }

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

        // ────────────────────────────────────────────────────────────────────────
        //  CDS Cheat Engine — persistent hook-based cheat injection
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Handle INJECTCHEATENGINE command — compiles the CDS cheat engine template,
        /// writes it into EE RAM, populates the cheat table, and hooks the game at
        /// the specified hook address.  The engine runs every frame, continuously
        /// re-applying cheat values so they persist against the running CPU.
        /// </summary>
        private string HandleInjectCheatEngine(string? cheatText, uint? hookAddress, uint? engineBase)
        {
            if (Scheduler == null && PineClient == null)
                return CreateErrorResponse("No live memory backend — start with --run or --pcsx2");
            if (string.IsNullOrWhiteSpace(cheatText))
                return CreateErrorResponse("No cheat data provided");
            if (!hookAddress.HasValue)
                return CreateErrorResponse("HookAddress is required for INJECTCHEATENGINE");

            try
            {
                IByteEditor byteEditor = IsPineMode
                    ? (IByteEditor)new PinePcsx2ByteEditor(PineClient!)
                    : new PS2ByteEditor(Scheduler!);

                uint baseAddr = engineBase ?? 0x000C0000;
                _cdsCheatEngine = new CdsCheatEngine(byteEditor, baseAddr);
                _cdsCheatEngine.Inject(hookAddress.Value, cheatText);

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "INJECTCHEATENGINE",
                    Data    = $"Cheat engine injected at 0x{baseAddr:X8}, hooked at 0x{hookAddress.Value:X8}"
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"InjectCheatEngine error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle UPDATECHEATENGINE command — updates the cheat table in-place without
        /// re-hooking.  The engine picks up the new values on its next iteration.
        /// </summary>
        private string HandleUpdateCheatEngine(string? cheatText)
        {
            if (_cdsCheatEngine == null || !_cdsCheatEngine.IsInjected)
                return CreateErrorResponse("Cheat engine is not injected. Use INJECTCHEATENGINE first.");
            if (string.IsNullOrWhiteSpace(cheatText))
                return CreateErrorResponse("No cheat data provided");

            try
            {
                _cdsCheatEngine.UpdateCheatTable(cheatText);
                return CreateSuccessResponse("Cheat table updated");
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"UpdateCheatEngine error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle REMOVECHEATENGINE command — removes the hook and restores the
        /// original instructions at the hook point.
        /// </summary>
        private string HandleRemoveCheatEngine()
        {
            if (_cdsCheatEngine == null || !_cdsCheatEngine.IsInjected)
                return CreateErrorResponse("Cheat engine is not injected.");

            try
            {
                _cdsCheatEngine.Remove();
                return CreateSuccessResponse("Cheat engine removed and original code restored");
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"RemoveCheatEngine error: {ex.Message}");
            }
        }
    }
}
