using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using CodeDesigner.Ps2.Database.Entities;
using CodeDesigner.Ps2.Bridge.Repositories;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Mirrors PCSX2's published game-compatibility list into the local database
    /// and serves it to the Compatibility Browser webview.
    ///
    /// COMPAT_SYNC  — downloads PCSX2's data.json and upserts Game/GameDisc rows.
    /// COMPAT_LIST  — returns every disc that has a compatibility status.
    /// COMPAT_COVER — lazily downloads and caches a game's cover art by serial.
    /// </summary>
    internal sealed class CompatService : ICommandService
    {
        /// <summary>PCSX2's official compatibility list (one entry per disc).</summary>
        private const string CompatDataUrl =
            "https://raw.githubusercontent.com/PCSX2/pcsx2-net-www/main/static/data/compat/data.json";

        /// <summary>
        /// Community cover-art repository keyed by disc serial — the source PCSX2
        /// itself recommends for box art. {0} is the normalised serial.
        /// </summary>
        private const string CoverUrlFormat =
            "https://raw.githubusercontent.com/xlenore/ps2-covers/main/covers/default/{0}.jpg";

        private static readonly HttpClient Http = CreateHttpClient();

        private readonly IBridgeContext _ctx;
        public CompatService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(_ => HandleList(),              "COMPAT_LIST",  "COMPATLIST");
            reg.Add(_ => HandleSync(),              "COMPAT_SYNC",  "COMPATSYNC");
            reg.Add(c => HandleCover(c.GameSerial), "COMPAT_COVER", "COMPATCOVER");
        }

        private static HttpClient CreateHttpClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CodeDesigner-Ps2-Bridge");
            return http;
        }

        // ── COMPAT_LIST ───────────────────────────────────────────────────────

        /// <summary>
        /// COMPAT_LIST — returns every GameDisc that carries a PCSX2 compatibility
        /// status, ordered by title. The cover-art bytes are deliberately NOT
        /// materialised here; only a <c>hasCover</c> flag is returned.
        /// </summary>
        private string HandleList()
        {
            if (_ctx.Db == null)
                return Responses.Error("Database is not available");

            try
            {
                // Project in SQL without pulling the CoverArt byte[] column.
                var raw = _ctx.Db.GameDiscs
                    .Where(d => d.CompatStatus != null)
                    .Select(d => new
                    {
                        d.Id,
                        d.GameId,
                        Title  = d.Game.Title,
                        d.Serial,
                        d.CRC,
                        Region = d.GameRegion.Name,
                        d.CompatStatus,
                        d.LastTestedVersion,
                        d.LastTestedDate,
                        d.WikiLink,
                        d.ForumLink,
                        HasCover = d.Game.CoverArt != null,
                    })
                    .ToList();

                var games = raw
                    .Select(r => new CompatGameInfo
                    {
                        DiscId            = r.Id,
                        GameId            = r.GameId,
                        Title             = r.Title,
                        Serial            = r.Serial,
                        Crc               = r.CRC,
                        Region            = r.Region,
                        Status            = r.CompatStatus,
                        LastTestedVersion = r.LastTestedVersion,
                        LastTestedDate    = r.LastTestedDate?.ToString("yyyy-MM-dd"),
                        WikiLink          = r.WikiLink,
                        ForumLink         = r.ForumLink,
                        HasCover          = r.HasCover,
                    })
                    .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success     = true,
                    Command     = "COMPAT_LIST",
                    CompatGames = games,
                });
            }
            catch (Exception ex) { return Responses.Error($"COMPAT_LIST error: {ex.Message}"); }
        }

        // ── COMPAT_SYNC ───────────────────────────────────────────────────────

        /// <summary>
        /// COMPAT_SYNC — downloads PCSX2's compatibility list and upserts the
        /// Game / GameDisc / GameRegion rows. Existing discs are matched by serial
        /// and only written when a field actually changed.
        /// </summary>
        private string HandleSync()
        {
            if (_ctx.Db == null)
                return Responses.Error("Database is not available");

            List<CompatEntry>? entries;
            try
            {
                var json = Http.GetStringAsync(CompatDataUrl).GetAwaiter().GetResult();
                entries  = JsonSerializer.Deserialize<List<CompatEntry>>(json);
            }
            catch (Exception ex)
            {
                return Responses.Error($"COMPAT_SYNC download failed: {ex.Message}");
            }

            if (entries == null || entries.Count == 0)
                return Responses.Error("COMPAT_SYNC: PCSX2 returned no compatibility entries");

            var db = _ctx.Db;

            // Pre-load the existing rows into lookup tables so the upsert loop
            // does not issue a query per entry. Games are projected to id/title
            // only so the (potentially large) CoverArt blobs are never loaded.
            var regions = db.GameRegions.ToList()
                .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var gameIdByTitle = db.Games
                .Select(g => new { g.Id, g.Title })
                .ToList()
                .GroupBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

            // Games created during this sync — referenced by navigation property
            // so EF assigns their ids on SaveChanges.
            var newGames = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);

            var discs = db.GameDiscs.ToList()
                .Where(d => !string.IsNullOrWhiteSpace(d.Serial))
                .GroupBy(d => DbLabelRepository.NormalizeSerial(d.Serial!), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            int added = 0, updated = 0, skipped = 0;

            foreach (var e in entries)
            {
                if (string.IsNullOrWhiteSpace(e.Serial) || string.IsNullOrWhiteSpace(e.Title))
                {
                    skipped++;
                    continue;
                }

                var title      = WebUtility.HtmlDecode(e.Title!.Trim());
                var serial     = DbLabelRepository.NormalizeSerial(e.Serial!);
                var regionName = MapRegion(e.Region);

                if (!regions.TryGetValue(regionName, out var region))
                {
                    region = new GameRegion { Name = regionName };
                    db.GameRegions.Add(region);
                    regions[regionName] = region;
                }

                // Resolve the parent game: an existing row (referenced by FK id)
                // or a game created during this sync (referenced by navigation
                // property so EF assigns its id on SaveChanges).
                gameIdByTitle.TryGetValue(title, out var existingGameId);
                Game? newGame = null;
                if (existingGameId == 0 && !newGames.TryGetValue(title, out newGame))
                {
                    newGame = new Game { Title = title };
                    db.Games.Add(newGame);
                    newGames[title] = newGame;
                }

                var crc    = string.IsNullOrWhiteSpace(e.Crc)    ? null : e.Crc!.Trim().ToUpperInvariant();
                var status = string.IsNullOrWhiteSpace(e.Status) ? null : e.Status!.Trim();
                var wiki   = string.IsNullOrWhiteSpace(e.WikiLink)  ? null : WebUtility.HtmlDecode(e.WikiLink!.Trim());
                var forum  = string.IsNullOrWhiteSpace(e.ForumLink) ? null : WebUtility.HtmlDecode(e.ForumLink!.Trim());
                DateTime? testedDate =
                    DateTime.TryParse(e.LastTestedDate, out var dt) ? dt : null;

                if (!discs.TryGetValue(serial, out var disc))
                {
                    disc = new GameDisc
                    {
                        Serial            = serial,
                        GameRegion        = region,
                        CRC               = crc,
                        CompatStatus      = status,
                        LastTestedVersion = e.LastTestedVersion,
                        LastTestedDate    = testedDate,
                        WikiLink          = wiki,
                        ForumLink         = forum,
                    };
                    if (newGame != null) { disc.Game = newGame; }
                    else                 { disc.GameId = existingGameId; }
                    db.GameDiscs.Add(disc);
                    discs[serial] = disc;
                    added++;
                }
                else
                {
                    bool changed = false;
                    if (crc != null && disc.CRC != crc)               { disc.CRC = crc; changed = true; }
                    if (disc.CompatStatus != status)                  { disc.CompatStatus = status; changed = true; }
                    if (disc.LastTestedVersion != e.LastTestedVersion) { disc.LastTestedVersion = e.LastTestedVersion; changed = true; }
                    if (disc.LastTestedDate != testedDate)            { disc.LastTestedDate = testedDate; changed = true; }
                    if (disc.WikiLink != wiki)                        { disc.WikiLink = wiki; changed = true; }
                    if (disc.ForumLink != forum)                      { disc.ForumLink = forum; changed = true; }
                    if (changed) updated++;
                }
            }

            try { db.SaveChanges(); }
            catch (Exception ex)
            {
                return Responses.Error($"COMPAT_SYNC save failed: {ex.Message}");
            }

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success = true,
                Command = "COMPAT_SYNC",
                Message = $"Synced {entries.Count} entries — {added} added, {updated} updated, {skipped} skipped.",
            });
        }

        // ── COMPAT_COVER ──────────────────────────────────────────────────────

        /// <summary>
        /// COMPAT_COVER — returns a game's cover art as a base64 string. The image
        /// is downloaded from the community cover repository on first request and
        /// cached in <see cref="Game.CoverArt"/> for all subsequent requests.
        /// </summary>
        private string HandleCover(string? serial)
        {
            if (_ctx.Db == null)
                return Responses.Error("Database is not available");
            if (string.IsNullOrWhiteSpace(serial))
                return Responses.Error("GameSerial is required for COMPAT_COVER");

            var normalized = DbLabelRepository.NormalizeSerial(serial);

            var gameId = _ctx.Db.GameDiscs
                .Where(d => d.Serial != null &&
                            d.Serial.Replace("_", "-").ToUpper() == normalized)
                .Select(d => (int?)d.GameId)
                .FirstOrDefault();

            if (gameId == null)
                return Responses.Error($"No game disc found for serial '{normalized}'");

            // Read just the CoverArt column, untracked, so the blob is not
            // pinned in the change tracker for the lifetime of the session.
            var cover = _ctx.Db.Games
                .AsNoTracking()
                .Where(g => g.Id == gameId.Value)
                .Select(g => g.CoverArt)
                .FirstOrDefault();

            if (cover == null || cover.Length == 0)
            {
                try
                {
                    var url  = string.Format(CoverUrlFormat, normalized);
                    var resp = Http.GetAsync(url).GetAwaiter().GetResult();
                    if (resp.IsSuccessStatusCode)
                    {
                        cover = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                        _ctx.Db.Games
                            .Where(g => g.Id == gameId.Value)
                            .ExecuteUpdate(s => s.SetProperty(g => g.CoverArt, cover));
                    }
                }
                catch
                {
                    // Network failure — fall through and report "no cover".
                }
            }

            if (cover == null || cover.Length == 0)
            {
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "COMPAT_COVER",
                    Message = "no-cover",
                });
            }

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success = true,
                Command = "COMPAT_COVER",
                Data    = Convert.ToBase64String(cover),
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Maps a PCSX2 region code (e.g. "us", "eu", "ja") to a GameRegion name.</summary>
        private static string MapRegion(string? code) =>
            (code ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "us"                                              => "NTSC-U",
                "ja" or "jp"                                      => "NTSC-J",
                "kr"                                              => "NTSC-K",
                "ch" or "cn"                                      => "NTSC-C",
                "eu" or "de" or "fr" or "es" or "it" or "nl"
                    or "pt" or "ru" or "au" or "uk" or "gb"       => "PAL",
                _                                                 => "Unknown",
            };

        /// <summary>Raw shape of a single entry in PCSX2's compatibility data.json.</summary>
        private sealed class CompatEntry
        {
            [JsonPropertyName("title")]               public string? Title { get; set; }
            [JsonPropertyName("serial")]              public string? Serial { get; set; }
            [JsonPropertyName("crc")]                 public string? Crc { get; set; }
            [JsonPropertyName("region")]              public string? Region { get; set; }
            [JsonPropertyName("status")]              public string? Status { get; set; }
            [JsonPropertyName("last_tested_version")] public string? LastTestedVersion { get; set; }
            [JsonPropertyName("last_tested_date")]    public string? LastTestedDate { get; set; }
            [JsonPropertyName("wiki_link")]           public string? WikiLink { get; set; }
            [JsonPropertyName("forum_link")]          public string? ForumLink { get; set; }
        }
    }
}
