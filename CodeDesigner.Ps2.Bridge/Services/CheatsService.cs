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

        public CheatsService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleGetGameCheats(c.GameSerial, c.CheatListId),                       "GETGAMECHEATS");
            reg.Add(c => HandlePortCheats(c.Data, c.SourceDumpPath, c.TargetDumpPath),           "PORTCHEATS");
            reg.Add(c => HandleConvertCheat(c.Data, c.Format, c.Direction),                      "CONVERTCHEAT");
            reg.Add(c => HandleBuildCheatEngine(c.Data, c.HookAddress, c.EngineBase, c.CdsSource, c.HookWords), "BUILDCHEATENGINE");
        }

        // ── GETGAMECHEATS ────────────────────────────────────────────────────

        /// <summary>
        /// GETGAMECHEATS — the archive's cheats for a disc. Returns every cheat list for the
        /// disc and its game (<see cref="CommandResponse.GameCheatLists"/>) plus the cheats of
        /// <paramref name="cheatListId"/>, or of the first non-empty list when none is given.
        /// </summary>
        private string HandleGetGameCheats(string? serial, int? cheatListId)
        {
            if (_ctx.Db == null) return Responses.Error("Database is not available");
            if (string.IsNullOrWhiteSpace(serial)) return Responses.Error("GameSerial is required for GETGAMECHEATS");

            var normalized = DbLabelRepository.NormalizeSerial(serial);

            // Several disc rows can share a serial (the cheat importer and the label
            // manager's game upsert each create one under their own title), so gather
            // lists across all of them.
            var discs = _ctx.Db.GameDiscs
                .Include(d => d.Game)
                .Where(d =>
                    d.Serial != null &&
                    d.Serial.Replace("_", "-").ToUpper() == normalized)
                .ToList();
            var discIds = discs.Select(d => d.Id).ToList();
            var gameIds = discs.Select(d => d.GameId).Distinct().ToList();

            if (discs.Count == 0)
            {
                return Responses.Json(new CommandResponse
                {
                    Success        = true,
                    Command        = "GETGAMECHEATS",
                    GameCheats     = [],
                    GameTitle      = null,
                    CheatListTitle = null,
                    GameCheatLists = [],
                });
            }

            // Lists made for the game in the cheats editor (game-level, e.g. r0001 / r0004)
            // come first, then lists imported for the disc from the cheat repository.
            var listRows = _ctx.Db.CheatLists
                .Where(cl => (cl.GameDiscId != null && discIds.Contains(cl.GameDiscId.Value))
                          || (cl.GameDiscId == null && cl.GameId != null && gameIds.Contains(cl.GameId.Value)))
                .Select(cl => new { cl.Id, cl.Title, cl.GameDiscId, cl.GameId, CheatCount = cl.Cheats.Count })
                .ToList();
            var lists = listRows
                .Select(cl => new GameCheatListInfo
                {
                    Id         = cl.Id,
                    Title      = cl.Title,
                    CheatCount = cl.CheatCount,
                    ForDisc    = cl.GameDiscId != null,
                })
                .OrderBy(cl => cl.ForDisc)
                .ThenBy(cl => cl.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            // Title/game id from the disc that owns the lists (the imported one), else the first.
            var ownerGameId = listRows.Select(cl => cl.GameDiscId != null
                    ? discs.First(d => d.Id == cl.GameDiscId).GameId
                    : cl.GameId)
                .FirstOrDefault(id => id != null);
            var disc = discs.FirstOrDefault(d => d.GameId == ownerGameId) ?? discs[0];

            var chosen = (cheatListId.HasValue ? lists.FirstOrDefault(l => l.Id == cheatListId.Value) : null)
                         ?? lists.FirstOrDefault(l => l.CheatCount > 0)
                         ?? lists.FirstOrDefault();
            var cheatList = chosen == null ? null : _ctx.Db.CheatLists
                .Include(cl => cl.Cheats)
                    .ThenInclude(c => c.CheatLines)
                .FirstOrDefault(cl => cl.Id == chosen.Id);
            var cheats = cheatList?.Cheats
                .OrderBy(c => c.SortOrder)
                .Select(c => new CheatInfo
                {
                    Name   = c.Label,
                    Author = string.IsNullOrWhiteSpace(c.Author) ? null : c.Author,
                    Notes  = string.IsNullOrWhiteSpace(c.Description) ? null : c.Description,
                    Codes  = c.CheatLines.OrderBy(l => l.SortOrder).Select(l => l.Code).ToList(),
                    Section = string.IsNullOrWhiteSpace(c.Section) ? null : c.Section,
                })
                .ToList() ?? [];

            return Responses.Json(new CommandResponse
            {
                Success        = true,
                Command        = "GETGAMECHEATS",
                GameCheats          = cheats,
                GameTitle           = disc.Game?.Title,
                CheatListTitle      = cheatList?.Title,
                SelectedCheatListId = cheatList?.Id,
                ArchiveGameId       = disc.GameId,
                GameCheatLists      = lists,
            });
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

        /// <summary>
        /// BUILDCHEATENGINE — build the engine image (code + data + table) for export.
        /// With a hook address and the two instructions there (<paramref name="hookWords"/>,
        /// read from the emulator by the client), they are embedded so the image only
        /// needs loading at its base plus <c>HookWord; nop</c> at the hook address.
        /// </summary>
        private string HandleBuildCheatEngine(string? cheatText, uint? hookAddress, uint? engineBase, string? cdsSource, List<uint>? hookWords)
        {
            if (string.IsNullOrWhiteSpace(cheatText) && string.IsNullOrWhiteSpace(cdsSource))
                return Responses.Error("No cheat data provided");

            try
            {
                uint baseAddr = engineBase ?? 0x000C0000;
                string codes  = !string.IsNullOrWhiteSpace(cdsSource)
                    ? CdsCheatEngine.CompileCdsToCodeText(cdsSource)
                    : cheatText!;
                var translation = RawCodeTranslator.Translate(codes);
                if (!translation.IsValid)
                    return Responses.Error("Cheat codes not supported by the engine: " + string.Join("; ", translation.Errors));

                var warnings = new List<string>(translation.Warnings);
                uint word0 = 0, word1 = 0;
                bool embedded = false;
                if (hookAddress.HasValue && hookWords is { Count: 2 })
                {
                    (word0, word1) = (hookWords[0], hookWords[1]);
                    CdsCheatEngine.ValidateHook(hookAddress.Value, word0, word1, baseAddr);
                    embedded = true;
                }
                else if (hookAddress.HasValue)
                {
                    warnings.Add("The hooked instructions are not known (no emulator running): they are not embedded (nops) — patch them before use.");
                }
                else
                {
                    warnings.Add("No hook address: the image's return jump is a nop — patch it and the hook before use.");
                }

                byte[] image = CdsCheatEngine.BuildImage(translation.Entries, baseAddr, hookAddress, word0, word1);

                return Responses.Json(new CommandResponse
                {
                    Success          = true,
                    Command          = "BUILDCHEATENGINE",
                    Data             = Convert.ToBase64String(image),
                    CheatEngineState = new CheatEngineInfo
                    {
                        Injected     = false,
                        EngineBase   = baseAddr,
                        HookAddress  = hookAddress,
                        Entries      = translation.Entries.Count,
                        ImageSize    = image.Length,
                        HookWord     = CdsCheatEngine.EncodeJ(baseAddr),
                        HookEmbedded = embedded,
                        Warnings     = warnings,
                    },
                });
            }
            catch (Exception ex) { return Responses.Error($"BuildCheatEngine error: {ex.Message}"); }
        }
    }
}
