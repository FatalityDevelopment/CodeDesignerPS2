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
            reg.Add(c => HandleGetGameCheats(c.GameSerial, c.CheatListId),                       "GETGAMECHEATS");
            reg.Add(c => HandleApplyCheats(c.Data),                                              "APPLYCHEATS");
            reg.Add(c => HandlePortCheats(c.Data, c.SourceDumpPath, c.TargetDumpPath),           "PORTCHEATS");
            reg.Add(c => HandleConvertCheat(c.Data, c.Format, c.Direction),                      "CONVERTCHEAT");
            reg.Add(c => HandleInjectCheatEngine(c.Data, c.HookAddress, c.EngineBase, c.CdsSource), "INJECTCHEATENGINE");
            reg.Add(c => HandleUpdateCheatEngine(c.Data, c.CdsSource),                           "UPDATECHEATENGINE");
            reg.Add(_ => HandleRemoveCheatEngine(),                                              "REMOVECHEATENGINE");
            reg.Add(_ => HandleCheatEngineStatus(),                                              "CHEATENGINESTATUS");
            reg.Add(c => HandleBuildCheatEngine(c.Data, c.HookAddress, c.EngineBase, c.CdsSource), "BUILDCHEATENGINE");
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
                // A previous injection still hooked (e.g. at another hook point) is removed first
                // so the game never jumps into a half-rewritten engine.
                if (_cdsCheatEngine?.IsInjected == true)
                    _cdsCheatEngine.Remove();

                IByteEditor byteEditor = new DebugClientByteEditor(_ctx.Client);
                uint baseAddr          = engineBase ?? 0x000C0000;
                var engine             = new CdsCheatEngine(byteEditor, baseAddr);

                if (!string.IsNullOrWhiteSpace(cdsSource))
                    engine.InjectFromCds(hookAddress.Value, cdsSource);
                else
                    engine.Inject(hookAddress.Value, cheatText!);
                _cdsCheatEngine = engine;

                return Responses.Json(new CommandResponse
                {
                    Success          = true,
                    Command          = "INJECTCHEATENGINE",
                    Data             = $"Cheat engine injected at 0x{baseAddr:X8}, hooked at 0x{hookAddress.Value:X8}",
                    CheatEngineState = EngineInfo(engine),
                });
            }
            catch (Exception ex) { return Responses.Error($"InjectCheatEngine error: {ex.Message}"); }
        }

        private string HandleCheatEngineStatus()
            => Responses.Json(new CommandResponse
            {
                Success          = true,
                Command          = "CHEATENGINESTATUS",
                CheatEngineState = _cdsCheatEngine != null
                    ? EngineInfo(_cdsCheatEngine)
                    : new CheatEngineInfo { Injected = false, EngineBase = 0x000C0000 },
            });

        /// <summary>
        /// BUILDCHEATENGINE — build the engine image (code + data + table) for export
        /// without touching memory. With a hook address and a live backend, the hooked
        /// instructions are read and embedded so the image only needs loading at its
        /// base plus <c>HookWord; nop</c> at the hook address.
        /// </summary>
        private string HandleBuildCheatEngine(string? cheatText, uint? hookAddress, uint? engineBase, string? cdsSource)
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
                if (hookAddress.HasValue && _ctx.Client != null)
                {
                    var editor = new DebugClientByteEditor(_ctx.Client);
                    word0 = BitConverter.ToUInt32(editor.Read((int)hookAddress.Value, 4), 0);
                    word1 = BitConverter.ToUInt32(editor.Read((int)hookAddress.Value + 4, 4), 0);
                    // While hooked here, memory holds the engine's own jump — use the saved originals.
                    if (_cdsCheatEngine?.IsInjected == true && _cdsCheatEngine.HookAddress == hookAddress.Value)
                        (word0, word1) = _cdsCheatEngine.OriginalInstructions;
                    CdsCheatEngine.ValidateHook(hookAddress.Value, word0, word1, baseAddr);
                    embedded = true;
                }
                else if (hookAddress.HasValue)
                {
                    warnings.Add("No live backend: the hooked instructions are not embedded (nops) — patch them before use.");
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
                        Injected     = _cdsCheatEngine?.IsInjected == true,
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

        private static CheatEngineInfo EngineInfo(CdsCheatEngine engine) => new()
        {
            Injected    = engine.IsInjected,
            EngineBase  = engine.EngineBase,
            HookAddress = engine.IsInjected ? engine.HookAddress : null,
            Entries     = engine.LastEntryCount,
            ImageSize   = CdsCheatEngine.CheatTableOffset + (engine.LastEntryCount + 1) * CdsCheatEngine.EntrySize,
            Warnings    = engine.LastWarnings.ToList(),
        };

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
                return Responses.Json(new CommandResponse { Success = true, Command = "UPDATECHEATENGINE", Message = "Cheat table updated", CheatEngineState = EngineInfo(_cdsCheatEngine) });
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
                return Responses.Json(new CommandResponse { Success = true, Command = "REMOVECHEATENGINE", Message = "Cheat engine removed and original code restored", CheatEngineState = EngineInfo(_cdsCheatEngine) });
            }
            catch (Exception ex) { return Responses.Error($"RemoveCheatEngine error: {ex.Message}"); }
        }
    }
}
