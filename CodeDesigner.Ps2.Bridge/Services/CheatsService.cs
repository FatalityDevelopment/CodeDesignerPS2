using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CodeDesigner.Ps2.Bridge.Repositories;
using CodeDesigner.Ps2.CheatEngine;
using Microsoft.EntityFrameworkCore;
using CheatPorterLib = CodeDesigner.Ps2.CheatEngine.CheatPorter;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Cheat-related commands: looking up cheats from the DB, applying GameShark
    /// patches to live memory, porting cheats between dumps, format conversion,
    /// and the persistent CDS Cheat Engine (INJECT/UPDATE/REMOVE).
    /// <para>
    /// Demonstrates a stateful service — the CDS cheat engine instance lives
    /// across commands as a private field, scoped to the service rather than
    /// the SocketServer god-class.
    /// </para>
    /// </summary>
    internal sealed class CheatsService : ICommandService
    {
        private readonly IBridgeContext _ctx;

        // CDS Cheat Engine instance (persistent across INJECT / UPDATE / REMOVE).
        private CdsCheatEngine? _cdsCheatEngine;

        public CheatsService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleGetGameCheats(c.GameSerial),                                      "GETGAMECHEATS");
            reg.Add(c => HandleApplyCheats(c.Data),                                              "APPLYCHEATS");
            reg.Add(c => HandlePortCheats(c.Data, c.SourceDumpPath, c.TargetDumpPath),           "PORTCHEATS");
            reg.Add(c => HandleConvertCheat(c.Data, c.Format, c.Direction),                      "CONVERTCHEAT");
            reg.Add(c => HandleInjectCheatEngine(c.Data, c.HookAddress, c.EngineBase, c.CdsSource), "INJECTCHEATENGINE");
            reg.Add(c => HandleUpdateCheatEngine(c.Data, c.CdsSource),                           "UPDATECHEATENGINE");
            reg.Add(_ => HandleRemoveCheatEngine(),                                              "REMOVECHEATENGINE");
        }

        // ── GETGAMECHEATS ────────────────────────────────────────────────────

        private string HandleGetGameCheats(string? serial)
        {
            if (_ctx.Db == null) return Responses.Error("Database is not available");
            if (string.IsNullOrWhiteSpace(serial)) return Responses.Error("GameSerial is required for GETGAMECHEATS");

            var normalized = DbLabelRepository.NormalizeSerial(serial);

            var disc = _ctx.Db.GameDiscs
                .Include(d => d.Game)
                .Include(d => d.CheatLists!)
                    .ThenInclude(cl => cl.Cheats)
                        .ThenInclude(c => c.CheatLines)
                .FirstOrDefault(d =>
                    d.Serial != null &&
                    d.Serial.Replace("_", "-").ToUpper() == normalized);

            if (disc == null)
            {
                return Responses.Json(new CommandResponse
                {
                    Success        = true,
                    Command        = "GETGAMECHEATS",
                    GameCheats     = [],
                    GameTitle      = null,
                    CheatListTitle = null,
                });
            }

            var cheatList = disc.CheatLists?.OrderBy(cl => cl.Id).FirstOrDefault();
            var cheats = cheatList?.Cheats
                .OrderBy(c => c.SortOrder)
                .Select(c => new CheatInfo
                {
                    Name   = c.Label,
                    Author = string.IsNullOrWhiteSpace(c.Author) ? null : c.Author,
                    Notes  = string.IsNullOrWhiteSpace(c.Description) ? null : c.Description,
                    Codes  = c.CheatLines.OrderBy(l => l.SortOrder).Select(l => l.Code).ToList(),
                })
                .ToList() ?? [];

            return Responses.Json(new CommandResponse
            {
                Success        = true,
                Command        = "GETGAMECHEATS",
                GameCheats     = cheats,
                GameTitle      = disc.Game?.Title,
                CheatListTitle = cheatList?.Title,
            });
        }

        // ── APPLYCHEATS ──────────────────────────────────────────────────────

        private string HandleApplyCheats(string? cheatText)
        {
            if (_ctx.Client == null)
                return Responses.Error("No live memory backend — start with --pcsx2 or STARTEMULATOR");
            if (string.IsNullOrWhiteSpace(cheatText))
                return Responses.Error("No cheat data provided");

            try
            {
                var byteEditor = new DebugClientByteEditor(_ctx.Client);
                var engine     = new CodeDesigner.Ps2.CheatEngine.CheatEngine(byteEditor);
                var cheatList  = new CheatList(cheatText);
                engine.PatchMemory(cheatList);
                return Responses.Success("Cheats applied successfully");
            }
            catch (Exception ex) { return Responses.Error($"ApplyCheats error: {ex.Message}"); }
        }

        // ── PORTCHEATS ───────────────────────────────────────────────────────

        private string HandlePortCheats(string? cheatText, string? sourcePath, string? targetPath)
        {
            if (string.IsNullOrWhiteSpace(cheatText))
                return Responses.Error("No cheat data provided");
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return Responses.Error($"Source dump not found: {sourcePath}");
            if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
                return Responses.Error($"Target dump not found: {targetPath}");

            byte[] sourceData;
            byte[] targetData;
            try
            {
                sourceData = File.ReadAllBytes(sourcePath);
                targetData = File.ReadAllBytes(targetPath);
            }
            catch (Exception ex) { return Responses.Error($"Failed to read dump files: {ex.Message}"); }

            var cheatList = new CheatList(cheatText);
            List<CheatBlock> blocks;
            try { blocks = cheatList.Parse(); }
            catch (Exception ex) { return Responses.Error($"Failed to parse cheat codes: {ex.Message}"); }

            var logLines = new List<string>
            {
                $"[PORTCHEATS] Source: {sourcePath}",
                $"[PORTCHEATS] Target: {targetPath}",
                string.Empty,
            };
            var allResults = CheatPorterLib.Port(sourceData, targetData, blocks, logLines);

            return Responses.Json(new CommandResponse
            {
                Success          = true,
                Command          = "PORTCHEATS",
                Data             = string.Join("\n", logLines),
                PortCheatResults = allResults,
            });
        }

        // ── CONVERTCHEAT ─────────────────────────────────────────────────────

        private string HandleConvertCheat(string? data, string? format, string? direction)
        {
            if (string.IsNullOrWhiteSpace(data))
                return Responses.Error("No cheat data provided");

            try
            {
                CheatFormat targetFormat = CheatFormat.Raw;
                if (!string.IsNullOrWhiteSpace(format))
                {
                    if (!Enum.TryParse<CheatFormat>(format, ignoreCase: true, out targetFormat))
                        return Responses.Error($"Unknown cheat format '{format}'. Valid values: Raw, ActionReplay1, ActionReplay2, ActionReplayMax, GameSharkV3, CodeBreakerV1, CodeBreakerV7");
                }

                string result;
                if (string.Equals(direction, "toRaw", StringComparison.OrdinalIgnoreCase))
                {
                    ICheatCodeConverter converter = string.IsNullOrWhiteSpace(format)
                        ? CheatFormatDetector.CreateConverter(CheatFormatDetector.Detect(data))
                        : CheatFormatDetector.CreateConverter(targetFormat);

                    var rawCodes     = converter.Decode(data);
                    var rawConverter = new RawConverter();
                    result           = rawConverter.Encode(rawCodes);
                }
                else
                {
                    var rawConverter   = new RawConverter();
                    var rawCodes       = rawConverter.Decode(data);
                    var targetConverter = CheatFormatDetector.CreateConverter(targetFormat);
                    result             = targetConverter.Encode(rawCodes);
                }

                return Responses.Json(new CommandResponse
                {
                    Success = true,
                    Command = "CONVERTCHEAT",
                    Data    = result,
                });
            }
            catch (Exception ex) { return Responses.Error($"ConvertCheat error: {ex.Message}"); }
        }

        // ── CDS Cheat Engine — INJECT / UPDATE / REMOVE ─────────────────────

        private string HandleInjectCheatEngine(string? cheatText, uint? hookAddress, uint? engineBase, string? cdsSource)
        {
            if (_ctx.Client == null)
                return Responses.Error("No live memory backend — start with --pcsx2 or STARTEMULATOR");
            if (string.IsNullOrWhiteSpace(cheatText) && string.IsNullOrWhiteSpace(cdsSource))
                return Responses.Error("No cheat data provided");
            if (!hookAddress.HasValue)
                return Responses.Error("HookAddress is required for INJECTCHEATENGINE");

            try
            {
                IByteEditor byteEditor = new DebugClientByteEditor(_ctx.Client);
                uint baseAddr          = engineBase ?? 0x000C0000;
                _cdsCheatEngine        = new CdsCheatEngine(byteEditor, baseAddr);

                if (!string.IsNullOrWhiteSpace(cdsSource))
                    _cdsCheatEngine.InjectFromCds(hookAddress.Value, cdsSource);
                else
                    _cdsCheatEngine.Inject(hookAddress.Value, cheatText!);

                return Responses.Json(new CommandResponse
                {
                    Success = true,
                    Command = "INJECTCHEATENGINE",
                    Data    = $"Cheat engine injected at 0x{baseAddr:X8}, hooked at 0x{hookAddress.Value:X8}",
                });
            }
            catch (Exception ex) { return Responses.Error($"InjectCheatEngine error: {ex.Message}"); }
        }

        private string HandleUpdateCheatEngine(string? cheatText, string? cdsSource)
        {
            if (_cdsCheatEngine == null || !_cdsCheatEngine.IsInjected)
                return Responses.Error("Cheat engine is not injected. Use INJECTCHEATENGINE first.");
            if (string.IsNullOrWhiteSpace(cheatText) && string.IsNullOrWhiteSpace(cdsSource))
                return Responses.Error("No cheat data provided");

            try
            {
                if (!string.IsNullOrWhiteSpace(cdsSource))
                    _cdsCheatEngine.UpdateCheatTableFromCds(cdsSource);
                else
                    _cdsCheatEngine.UpdateCheatTable(cheatText!);
                return Responses.Success("Cheat table updated");
            }
            catch (Exception ex) { return Responses.Error($"UpdateCheatEngine error: {ex.Message}"); }
        }

        private string HandleRemoveCheatEngine()
        {
            if (_cdsCheatEngine == null || !_cdsCheatEngine.IsInjected)
                return Responses.Error("Cheat engine is not injected.");

            try
            {
                _cdsCheatEngine.Remove();
                return Responses.Success("Cheat engine removed and original code restored");
            }
            catch (Exception ex) { return Responses.Error($"RemoveCheatEngine error: {ex.Message}"); }
        }
    }
}
