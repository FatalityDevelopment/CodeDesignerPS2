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
            reg.Add(c => HandleSections(c.CheatListId, c.Query),     "CHEATEDITOR_SECTIONS", "CHEATEDITORSECTIONS");
            reg.Add(c => HandleGetSection(c.CheatListId, c.CheatSection, c.Query, c.MaxCount),
                                                                      "CHEATEDITOR_GET_SECTION", "CHEATEDITORGETSECTION");
            reg.Add(c => HandleSearch(c.CheatGameId, c.CheatListId, c.CheatSection, c.Query, c.MaxCount),
                                                                      "CHEATEDITOR_SEARCH", "CHEATEDITORSEARCH");
            reg.Add(c => HandleSave(c.CheatListId, c.Data),          "CHEATEDITOR_SAVE",   "CHEATEDITORSAVE");
            reg.Add(c => HandleCreate(c.CheatGameId, c.GameTitle),   "CHEATEDITOR_CREATE", "CHEATEDITORCREATE");
            reg.Add(c => HandleDelete(c.CheatListId),                "CHEATEDITOR_DELETE", "CHEATEDITORDELETE");
            reg.Add(c => HandleImport(c.FilePath, c.GameSerial),     "CHEATEDITOR_IMPORT", "CHEATEDITORIMPORT");
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

        // ── CHEATEDITOR_SECTIONS ──────────────────────────────────────────────

        private const string DefaultSection = "Others";

        private string HandleSections(int? cheatListId, string? query)
        {
            if (_ctx.Db == null)
                return Responses.Error("Database is not available");
            if (cheatListId == null)
                return Responses.Error("CheatListId is required for CHEATEDITOR_SECTIONS");

            try
            {
                var q = query?.Trim();
                var rows = _ctx.Db.Cheats
                    .AsNoTracking()
                    .Where(c => c.CheatListId == cheatListId.Value)
                    .OrderBy(c => c.SortOrder)
                    .Select(c => new
                    {
                        c.Label,
                        c.Section,
                        c.SortOrder,
                        CodeLineCount = c.CheatLines.Count,
                    })
                    .ToList();

                var sections = rows
                    .GroupBy(c => NormalizeSection(c.Section), StringComparer.OrdinalIgnoreCase)
                    .Select(g => new CheatSectionInfo
                    {
                        ListId          = cheatListId.Value,
                        Section         = g.Key,
                        CheatCount      = g.Count(),
                        CodeLineCount   = g.Sum(c => c.CodeLineCount),
                        FirstCheatLabel = g.OrderBy(c => c.SortOrder).Select(c => c.Label).FirstOrDefault(),
                    })
                    .Where(s => string.IsNullOrWhiteSpace(q) ||
                                s.Section.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(s => rows.FindIndex(c => string.Equals(NormalizeSection(c.Section), s.Section, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success             = true,
                    Command             = "CHEATEDITOR_SECTIONS",
                    CheatEditorSections = sections,
                });
            }
            catch (Exception ex) { return Responses.Error($"CHEATEDITOR_SECTIONS error: {ex.Message}"); }
        }

        // ── CHEATEDITOR_GET_SECTION ───────────────────────────────────────────

        private string HandleGetSection(int? cheatListId, string? section, string? query, ushort? maxCount)
        {
            if (_ctx.Db == null)
                return Responses.Error("Database is not available");
            if (cheatListId == null)
                return Responses.Error("CheatListId is required for CHEATEDITOR_GET_SECTION");
            if (string.IsNullOrWhiteSpace(section))
                return Responses.Error("CheatSection is required for CHEATEDITOR_GET_SECTION");

            try
            {
                int take = ClampTake(maxCount, 200);
                var targetSection = NormalizeSection(section);
                var q = query?.Trim();

                var cheats = _ctx.Db.Cheats
                    .AsNoTracking()
                    .Where(c => c.CheatListId == cheatListId.Value)
                    .OrderBy(c => c.SortOrder)
                    .Select(c => new
                    {
                        c.Label,
                        c.Section,
                        c.Description,
                        Lines = c.CheatLines.OrderBy(l => l.SortOrder).Select(l => l.Code).ToList(),
                    })
                    .ToList()
                    .Where(c => string.Equals(NormalizeSection(c.Section), targetSection, StringComparison.OrdinalIgnoreCase))
                    .Where(c => string.IsNullOrWhiteSpace(q) ||
                                c.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                (c.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                c.Lines.Any(line => line.Contains(q, StringComparison.OrdinalIgnoreCase)))
                    .Take(take)
                    .Select(c => new CheatFileEntry
                    {
                        Label     = c.Label,
                        Section   = NormalizeSection(c.Section),
                        CodeLines = c.Lines,
                    })
                    .ToList();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "CHEATEDITOR_GET_SECTION",
                    Data    = CheatFileFormat.Serialize(cheats),
                });
            }
            catch (Exception ex) { return Responses.Error($"CHEATEDITOR_GET_SECTION error: {ex.Message}"); }
        }

        // ── CHEATEDITOR_SEARCH ────────────────────────────────────────────────

        private string HandleSearch(int? gameId, int? cheatListId, string? section, string? query, ushort? maxCount)
        {
            if (_ctx.Db == null)
                return Responses.Error("Database is not available");
            if (string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(section))
                return Responses.Error("Query or CheatSection is required for CHEATEDITOR_SEARCH");

            try
            {
                int take = ClampTake(maxCount, 50);
                var q = query?.Trim();
                var sectionFilter = string.IsNullOrWhiteSpace(section) ? null : NormalizeSection(section);

                var cheatsQuery = _ctx.Db.Cheats
                    .AsNoTracking()
                    .Where(c => cheatListId == null || c.CheatListId == cheatListId.Value)
                    .Where(c => gameId == null || c.CheatList.GameId == gameId.Value);

                if (sectionFilter != null)
                {
                    var sectionLower = sectionFilter.ToLowerInvariant();
                    cheatsQuery = cheatsQuery.Where(c =>
                        (string.IsNullOrEmpty(c.Section) ? DefaultSection : c.Section).ToLower() == sectionLower);
                }

                if (!string.IsNullOrWhiteSpace(q))
                {
                    var qLower = q.ToLowerInvariant();
                    cheatsQuery = cheatsQuery.Where(c =>
                        c.Label.ToLower().Contains(qLower) ||
                        (c.Section != null && c.Section.ToLower().Contains(qLower)) ||
                        (c.Description != null && c.Description.ToLower().Contains(qLower)) ||
                        c.CheatLines.Any(l => l.Code.ToLower().Contains(qLower)));
                }

                var rows = cheatsQuery
                    .OrderBy(c => c.CheatList.Game!.Title)
                    .ThenBy(c => c.CheatList.Title)
                    .ThenBy(c => c.SortOrder)
                    .Take(take)
                    .Select(c => new
                    {
                        CheatId       = c.Id,
                        c.Label,
                        c.Section,
                        c.Description,
                        c.CheatListId,
                        ListTitle     = c.CheatList.Title,
                        GameId        = c.CheatList.GameId,
                        GameTitle     = c.CheatList.Game != null ? c.CheatList.Game.Title : null,
                        CodeLineCount = c.CheatLines.Count,
                        CodeLines     = c.CheatLines.OrderBy(l => l.SortOrder).Select(l => l.Code).ToList(),
                    })
                    .ToList()
                    .Select(c =>
                    {
                        var matched = string.IsNullOrWhiteSpace(q)
                            ? new List<string>()
                            : MatchedFields(c.Label, c.Description, NormalizeSection(c.Section), c.CodeLines, q);
                        if (sectionFilter != null && !matched.Contains("section"))
                            matched.Add("section");

                        return new CheatSearchResultInfo
                        {
                            GameId        = c.GameId,
                            GameTitle     = c.GameTitle,
                            ListId        = c.CheatListId,
                            ListTitle     = c.ListTitle,
                            CheatId       = c.CheatId,
                            Label         = c.Label,
                            Section       = NormalizeSection(c.Section),
                            CodeLineCount = c.CodeLineCount,
                            MatchedFields = matched,
                        };
                    })
                    .ToList();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success                  = true,
                    Command                  = "CHEATEDITOR_SEARCH",
                    CheatEditorSearchResults = rows,
                });
            }
            catch (Exception ex) { return Responses.Error($"CHEATEDITOR_SEARCH error: {ex.Message}"); }
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
                        c.Description,
                        c.Author,
                        Lines = c.CheatLines.OrderBy(l => l.SortOrder).Select(l => l.Code).ToList(),
                    })
                    .ToList();

                var entries = cheats
                    .Select(c => new CheatFileEntry
                    {
                        Label       = c.Label,
                        Section     = c.Section,
                        Description = c.Description,
                        Author      = c.Author,
                        CodeLines   = c.Lines,
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

                // Carry over any metadata omitted by older text, matched on section + label.
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
                        Label       = entry.Label,
                        Section     = entry.Section,
                        Description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description.Trim(),
                        Author      = string.IsNullOrWhiteSpace(entry.Author) ? null : entry.Author.Trim(),
                        SortOrder   = sortOrder++,
                    };
                    if (oldMeta.TryGetValue(MetaKey(entry.Section, entry.Label), out var prev))
                    {
                        cheat.Author      ??= prev.Author;
                        cheat.Description ??= prev.Description;
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

        /// <summary>
        /// Import the cheat repository's <c>Database/*.txt</c> files into cheat lists. With
        /// <paramref name="gameSerial"/>, only that game's files are imported; lists attach to
        /// the game that already owns a disc with the serial.
        /// </summary>
        private string HandleImport(string? archiveRoot, string? gameSerial)
        {
            if (_ctx.Db == null)
                return Responses.Error("Database is not available");
            if (string.IsNullOrWhiteSpace(archiveRoot))
                return Responses.Error("FilePath (archive root) is required for CHEATEDITOR_IMPORT");
            if (!Directory.Exists(archiveRoot))
                return Responses.Error($"Archive folder not found: {archiveRoot}");

            try
            {
                var parser = new BulkCheatParser(_ctx.Db, archiveRoot!, gameSerial);
                var result = parser.RunAsync().GetAwaiter().GetResult();

                var skipped = result.FilesSkipped > 0
                    ? $" Skipped {result.FilesSkipped} file(s); see the bridge console for details."
                    : string.Empty;
                var message = string.IsNullOrWhiteSpace(gameSerial)
                    ? $"Bulk import complete — imported {result.FilesImported}/{result.FilesProcessed} file(s), {result.CheatLists} cheat list(s), {result.Cheats} cheat(s) in the database.{skipped}"
                    : $"Imported {result.FilesImported} repository file(s) for {gameSerial} into cheat lists.{skipped}";
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "CHEATEDITOR_IMPORT",
                    Message = message,
                });
            }
            catch (Exception ex) { return Responses.Error($"CHEATEDITOR_IMPORT error: {ex.Message}"); }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string NormalizeSection(string? section)
            => string.IsNullOrWhiteSpace(section) ? DefaultSection : section.Trim();

        private static int ClampTake(ushort? maxCount, int fallback)
            => Math.Clamp(maxCount is { } m && m > 0 ? m : fallback, 1, 500);

        private static List<string> MatchedFields(
            string label, string? description, string section, IReadOnlyList<string> codeLines, string? query)
        {
            var matches = new List<string>();
            if (string.IsNullOrWhiteSpace(query))
                return matches;

            if (label.Contains(query, StringComparison.OrdinalIgnoreCase))
                matches.Add("label");
            if (section.Contains(query, StringComparison.OrdinalIgnoreCase))
                matches.Add("section");
            if (description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                matches.Add("description");
            if (codeLines.Any(line => line.Contains(query, StringComparison.OrdinalIgnoreCase)))
                matches.Add("code");

            return matches;
        }

        private static string MetaKey(string? section, string label) =>
            (section ?? string.Empty).ToLowerInvariant() + "|" + label.ToLowerInvariant();
    }
}
