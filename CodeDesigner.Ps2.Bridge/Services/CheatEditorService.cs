using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.Database.Entities;
using CodeDesigner.Ps2.CheatEngine;
// Both namespaces declare Cheat / CheatList; alias the database entities so the
// editor code is unambiguous while still using CheatFileFormat from CheatEngine.
using DbCheat = CodeDesigner.Ps2.Database.Entities.Cheat;
using DbCheatList = CodeDesigner.Ps2.Database.Entities.CheatList;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Backs the cheats editor webview: lists games that own cheat lists,
    /// loads / saves a cheat list as editable text, and runs the bulk importer.
    /// </summary>
    internal sealed class CheatEditorService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public CheatEditorService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(_ => HandleGames(),                              "CHEATEDITOR_GAMES",  "CHEATEDITORGAMES");
            reg.Add(c => HandleLists(c.CheatGameId),                 "CHEATEDITOR_LISTS",  "CHEATEDITORLISTS");
            reg.Add(c => HandleGet(c.CheatListId),                   "CHEATEDITOR_GET",    "CHEATEDITORGET");
            reg.Add(c => HandleSave(c.CheatListId, c.Data),          "CHEATEDITOR_SAVE",   "CHEATEDITORSAVE");
            reg.Add(c => HandleCreate(c.CheatGameId, c.GameTitle),   "CHEATEDITOR_CREATE", "CHEATEDITORCREATE");
            reg.Add(c => HandleDelete(c.CheatListId),                "CHEATEDITOR_DELETE", "CHEATEDITORDELETE");
            reg.Add(c => HandleImport(c.FilePath),                   "CHEATEDITOR_IMPORT", "CHEATEDITORIMPORT");
        }

        // ── CHEATEDITOR_GAMES ─────────────────────────────────────────────────

        private string HandleGames()
        {
            if (_ctx.Db == null)
                return Responses.Error("Database is not available");
            try
            {
                var raw = _ctx.Db.Games
                    .Where(g => g.CheatLists.Any())
                    .Select(g => new
                    {
                        g.Id,
                        g.Title,
                        HasCover    = g.CoverArt != null,
                        ListCount   = g.CheatLists.Count,
                        CoverSerial = g.Discs
                            .Where(d => d.Serial != null)
                            .Select(d => d.Serial)
                            .FirstOrDefault(),
                    })
                    .ToList();

                var games = raw
                    .Select(g => new CheatGameInfo
                    {
                        GameId      = g.Id,
                        Title       = g.Title,
                        HasCover    = g.HasCover,
                        ListCount   = g.ListCount,
                        CoverSerial = g.CoverSerial,
                    })
                    .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success          = true,
                    Command          = "CHEATEDITOR_GAMES",
                    CheatEditorGames = games,
                });
            }
            catch (Exception ex) { return Responses.Error($"CHEATEDITOR_GAMES error: {ex.Message}"); }
        }

        // ── CHEATEDITOR_LISTS ─────────────────────────────────────────────────

        private string HandleLists(int? gameId)
        {
            if (_ctx.Db == null)
                return Responses.Error("Database is not available");
            if (gameId == null)
                return Responses.Error("CheatGameId is required for CHEATEDITOR_LISTS");
            try
            {
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success          = true,
                    Command          = "CHEATEDITOR_LISTS",
                    CheatEditorLists = ListsForGame(gameId.Value),
                });
            }
            catch (Exception ex) { return Responses.Error($"CHEATEDITOR_LISTS error: {ex.Message}"); }
        }

        private List<CheatListInfo> ListsForGame(int gameId)
        {
            var raw = _ctx.Db!.CheatLists
                .Where(cl => cl.GameId == gameId)
                .Select(cl => new
                {
                    cl.Id,
                    cl.Title,
                    cl.UpdatedAt,
                    CheatCount = cl.Cheats.Count,
                    Region     = cl.GameDisc != null ? cl.GameDisc.GameRegion.Name : null,
                    Serial     = cl.GameDisc != null ? cl.GameDisc.Serial : null,
                    Crc        = cl.GameDisc != null ? cl.GameDisc.CRC : null,
                })
                .ToList();

            return raw
                .Select(cl => new CheatListInfo
                {
                    Id         = cl.Id,
                    GameId     = gameId,
                    Title      = cl.Title,
                    CheatCount = cl.CheatCount,
                    Region     = cl.Region,
                    Serial     = cl.Serial,
                    Crc        = cl.Crc,
                    UpdatedAt  = cl.UpdatedAt.ToString("yyyy-MM-dd"),
                })
                .OrderBy(cl => cl.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ── CHEATEDITOR_GET ───────────────────────────────────────────────────

        private string HandleGet(int? cheatListId)
        {
            if (_ctx.Db == null)
                return Responses.Error("Database is not available");
            if (cheatListId == null)
                return Responses.Error("CheatListId is required for CHEATEDITOR_GET");
            try
            {
                var cheats = _ctx.Db.Cheats
                    .Where(c => c.CheatListId == cheatListId.Value)
                    .OrderBy(c => c.SortOrder)
                    .Select(c => new
                    {
                        c.Label,
                        c.Section,
                        Lines = c.CheatLines.OrderBy(l => l.SortOrder).Select(l => l.Code).ToList(),
                    })
                    .ToList();

                var entries = cheats
                    .Select(c => new CheatFileEntry
                    {
                        Label     = c.Label,
                        Section   = c.Section,
                        CodeLines = c.Lines,
                    })
                    .ToList();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "CHEATEDITOR_GET",
                    Data    = CheatFileFormat.Serialize(entries),
                });
            }
            catch (Exception ex) { return Responses.Error($"CHEATEDITOR_GET error: {ex.Message}"); }
        }

        // ── CHEATEDITOR_SAVE ──────────────────────────────────────────────────

        private string HandleSave(int? cheatListId, string? text)
        {
            if (_ctx.Db == null)
                return Responses.Error("Database is not available");
            if (cheatListId == null)
                return Responses.Error("CheatListId is required for CHEATEDITOR_SAVE");

            var parse = CheatFileFormat.Parse(text);
            if (!parse.IsValid)
                return Responses.Error("Invalid cheat file:\n" + string.Join("\n", parse.Errors));

            try
            {
                var list = _ctx.Db.CheatLists
                    .Include(cl => cl.Cheats)
                    .ThenInclude(c => c.CheatLines)
                    .FirstOrDefault(cl => cl.Id == cheatListId.Value);
                if (list == null)
                    return Responses.Error($"Cheat list {cheatListId} not found.");

                // Carry over metadata (author / enabled / notes) the text format
                // does not represent, matched on section + label.
                var oldMeta = list.Cheats
                    .GroupBy(c => MetaKey(c.Section, c.Label))
                    .ToDictionary(g => g.Key, g => g.First());

                _ctx.Db.Cheats.RemoveRange(list.Cheats);
                list.Cheats.Clear();

                int sortOrder = 0;
                foreach (var entry in parse.Entries)
                {
                    var cheat = new DbCheat
                    {
                        Label     = entry.Label,
                        Section   = entry.Section,
                        SortOrder = sortOrder++,
                    };
                    if (oldMeta.TryGetValue(MetaKey(entry.Section, entry.Label), out var prev))
                    {
                        cheat.Author      = prev.Author;
                        cheat.Description = prev.Description;
                        cheat.IsEnabled   = prev.IsEnabled;
                    }

                    int lineOrder = 0;
                    foreach (var code in entry.CodeLines)
                        cheat.CheatLines.Add(new CheatLine { Code = code, SortOrder = lineOrder++ });

                    list.Cheats.Add(cheat);
                }

                list.UpdatedAt = DateTime.UtcNow;
                _ctx.Db.SaveChanges();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "CHEATEDITOR_SAVE",
                    Message = $"Saved {parse.Entries.Count} cheat(s).",
                });
            }
            catch (Exception ex) { return Responses.Error($"CHEATEDITOR_SAVE error: {ex.Message}"); }
        }

        // ── CHEATEDITOR_CREATE ────────────────────────────────────────────────

        private string HandleCreate(int? gameId, string? title)
        {
            if (_ctx.Db == null)
                return Responses.Error("Database is not available");
            if (gameId == null)
                return Responses.Error("CheatGameId is required for CHEATEDITOR_CREATE");

            try
            {
                var game = _ctx.Db.Games.FirstOrDefault(g => g.Id == gameId.Value);
                if (game == null)
                    return Responses.Error($"Game {gameId} not found.");

                var list = new DbCheatList
                {
                    GameId    = game.Id,
                    Title     = string.IsNullOrWhiteSpace(title) ? "New Cheat List" : title!.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                _ctx.Db.CheatLists.Add(list);
                _ctx.Db.SaveChanges();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success         = true,
                    Command         = "CHEATEDITOR_CREATE",
                    CheatEditorList = new CheatListInfo
                    {
                        Id         = list.Id,
                        GameId     = game.Id,
                        Title      = list.Title,
                        CheatCount = 0,
                        UpdatedAt  = list.UpdatedAt.ToString("yyyy-MM-dd"),
                    },
                });
            }
            catch (Exception ex) { return Responses.Error($"CHEATEDITOR_CREATE error: {ex.Message}"); }
        }

        // ── CHEATEDITOR_DELETE ────────────────────────────────────────────────

        private string HandleDelete(int? cheatListId)
        {
            if (_ctx.Db == null)
                return Responses.Error("Database is not available");
            if (cheatListId == null)
                return Responses.Error("CheatListId is required for CHEATEDITOR_DELETE");
            try
            {
                var list = _ctx.Db.CheatLists.FirstOrDefault(cl => cl.Id == cheatListId.Value);
                if (list == null)
                    return Responses.Error($"Cheat list {cheatListId} not found.");

                _ctx.Db.CheatLists.Remove(list);
                _ctx.Db.SaveChanges();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "CHEATEDITOR_DELETE",
                    Message = "Cheat list deleted.",
                });
            }
            catch (Exception ex) { return Responses.Error($"CHEATEDITOR_DELETE error: {ex.Message}"); }
        }

        // ── CHEATEDITOR_IMPORT ────────────────────────────────────────────────

        private string HandleImport(string? archiveRoot)
        {
            if (_ctx.Db == null)
                return Responses.Error("Database is not available");
            if (string.IsNullOrWhiteSpace(archiveRoot))
                return Responses.Error("FilePath (archive root) is required for CHEATEDITOR_IMPORT");
            if (!Directory.Exists(archiveRoot))
                return Responses.Error($"Archive folder not found: {archiveRoot}");

            try
            {
                var parser = new BulkCheatParser(_ctx.Db, archiveRoot!);
                parser.RunAsync().GetAwaiter().GetResult();

                var lists  = _ctx.Db.CheatLists.Count();
                var cheats = _ctx.Db.Cheats.Count();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "CHEATEDITOR_IMPORT",
                    Message = $"Bulk import complete — {lists} cheat list(s), {cheats} cheat(s) in the database.",
                });
            }
            catch (Exception ex) { return Responses.Error($"CHEATEDITOR_IMPORT error: {ex.Message}"); }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string MetaKey(string? section, string label) =>
            (section ?? string.Empty).ToLowerInvariant() + "|" + label.ToLowerInvariant();
    }
}
