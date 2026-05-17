using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Database;

/// <summary>
/// Walks the <c>Database/MEMORY-PS2-*/</c> folders inside the CheatCodeArchive
/// checkout and upserts all PS2 cheat data into the database.
/// </summary>
/// <remarks>
/// CMP file format reference:
/// <code>
///   ^1 = HASH: XXXXXXXX          ← CRC / hash (optional)
///   ^2 = GameID: SLUS_202.65     ← disc serial (optional)
///   ^3 = NAME: Game Title        ← game name
///   !Section Name:               ← category header
///   +Cheat Name                  ← cheat entry
///   %Credits: Author             ← author (optional, follows the cheat name)
///   $XXXXXXXX YYYYYYYY           ← code line (address + data)
///   !!                           ← end of section
/// </code>
/// </remarks>
public class BulkCheatParser
{
    // PS2 sub-folders to process inside Database/.
    private static readonly string[] Ps2Folders =
    [
        "MEMORY-PS2-CODEBREAKER",
        "MEMORY-PS2-GSHI-DB",
        "MEMORY-PS2-CMP",
        "MEMORY-PS2-XPLODER-DB-[EU]",
    ];

    // Friendly source label keyed on folder name (prefix match).
    private static readonly Dictionary<string, string> SourceLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MEMORY-PS2-CODEBREAKER"]     = "CodeBreaker",
        ["MEMORY-PS2-GSHI-DB"]         = "GSHI",
        ["MEMORY-PS2-CMP"]             = "CMP",
        ["MEMORY-PS2-XPLODER-DB-[EU]"] = "Xploder (EU)",
    };

    // Region in filename: e.g. "(NTSC-U)", "(Pal-M6)", "(PAL)"
    private static readonly Regex RegionInFilename =
        new(@"\((?<region>[A-Za-z]+-[A-Za-z0-9]+)\)", RegexOptions.Compiled);

    // Header lines: "^N = KEY: value"
    private static readonly Regex HeaderLine =
        new(@"^\^[0-9]+\s*=\s*(?<key>[A-Za-z]+):\s*(?<value>.+)$", RegexOptions.Compiled);

    // Code lines: "$XXXXXXXX YYYYYYYY"
    private static readonly Regex CodeLine =
        new(@"^\$(?<code>[0-9A-Fa-f]{8}\s+[0-9A-Fa-f]{8})\s*$", RegexOptions.Compiled);

    private readonly CodeDesignerDbContext _db;
    private readonly string _archiveRoot;

    /// <param name="db">Database context to write into.</param>
    /// <param name="archiveRoot">
    /// Absolute path to the root of the CheatCodeArchive checkout
    /// (the directory that contains the <c>Database/</c> folder).
    /// </param>
    public BulkCheatParser(CodeDesignerDbContext db, string archiveRoot)
    {
        _db = db;
        _archiveRoot = archiveRoot;
    }

    /// <summary>
    /// Run the full import. Existing records matched by disc + source title are
    /// replaced; new records are inserted.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        var dbRoot = Path.Combine(_archiveRoot, "Database");
        if (!Directory.Exists(dbRoot))
            throw new DirectoryNotFoundException(
                $"CheatCodeArchive Database folder not found: {dbRoot}");

        // Pre-load lookup caches to avoid per-row DB queries.
        var regionCache = await _db.GameRegions
            .ToDictionaryAsync(r => r.Name, StringComparer.OrdinalIgnoreCase, ct);
        var gameCache = await _db.Games
            .ToDictionaryAsync(g => g.Title, StringComparer.OrdinalIgnoreCase, ct);

        // Pre-load disc caches keyed by (game title + serial) and (game title + region name).
        var discsBySerial = new Dictionary<string, GameDisc>(StringComparer.OrdinalIgnoreCase);
        var discsByRegion = new Dictionary<string, GameDisc>(StringComparer.OrdinalIgnoreCase);
        foreach (var disc in await _db.GameDiscs
                     .Include(d => d.Game)
                     .Include(d => d.GameRegion)
                     .ToListAsync(ct))
        {
            if (disc.Serial != null)
                discsBySerial[$"{disc.Game.Title}||{disc.Serial}"] = disc;
            discsByRegion[$"{disc.Game.Title}||{disc.GameRegion.Name}"] = disc;
        }

        // Pre-load cheat list cache (no cheats included — they are bulk-deleted on re-import).
        var cheatListCache = await _db.CheatLists
            .ToDictionaryAsync(cl => (cl.GameDiscId, cl.Title), ct);

        // Tracks cheat lists staged but not yet saved in the current folder pass.
        // Key: "{gameTitle}||{listTitle}" — used to avoid duplicates within a folder.
        var pendingCheatLists = new Dictionary<string, CheatList>(StringComparer.OrdinalIgnoreCase);

        foreach (var folderName in Ps2Folders)
        {
            var folderPath = Path.Combine(dbRoot, folderName);
            if (!Directory.Exists(folderPath))
                continue;

            var source = SourceLabels.TryGetValue(folderName, out var lbl) ? lbl : folderName;

            pendingCheatLists.Clear();

            foreach (var filePath in Directory.EnumerateFiles(folderPath, "*.txt"))
            {
                try
                {
                    await ProcessFileAsync(
                        filePath, source,
                        regionCache, gameCache,
                        discsBySerial, discsByRegion,
                        cheatListCache, pendingCheatLists,
                        ct);
                }
                catch (Exception ex)
                {
                    // Log and continue — a bad file must not abort the whole import.
                    Console.Error.WriteLine(
                        $"[BulkCheatParser] Skipping {filePath}: {ex.Message}");
                }
            }

            await _db.SaveChangesAsync(ct);

            // Merge newly persisted cheat lists into the main cache so that
            // subsequent folders can find them by their now-assigned disc Id.
            foreach (var cl in pendingCheatLists.Values)
                cheatListCache[(cl.GameDiscId, cl.Title)] = cl;
        }
    }

    // ── File processing ──────────────────────────────────────────────────────

    private async Task ProcessFileAsync(
        string filePath,
        string source,
        Dictionary<string, GameRegion> regionCache,
        Dictionary<string, Game> gameCache,
        Dictionary<string, GameDisc> discsBySerial,
        Dictionary<string, GameDisc> discsByRegion,
        Dictionary<(int? discId, string title), CheatList> cheatListCache,
        Dictionary<string, CheatList> pendingCheatLists,
        CancellationToken ct)
    {
        var parsed = ParseFile(filePath);
        if (string.IsNullOrWhiteSpace(parsed.Name))
            return;

        // Resolve region.
        var regionName = ResolveRegionName(
            parsed.RegionHint ?? RegionFromFilename(Path.GetFileName(filePath)),
            parsed.Serial);
        var region = GetOrCreate(regionCache, regionName, () =>
        {
            var r = new GameRegion { Name = regionName };
            _db.GameRegions.Add(r);
            return r;
        });

        // Resolve game.
        var game = GetOrCreate(gameCache, parsed.Name, () =>
        {
            var g = new Game { Title = parsed.Name };
            _db.Games.Add(g);
            return g;
        });

        // Resolve disc via in-memory cache (no DB hit).
        var disc = ResolveDisc(game, region, parsed.Serial, parsed.Hash, discsBySerial, discsByRegion);

        // CheatList title encodes the source so multiple databases don't collide.
        var listTitle = $"[{source}] {parsed.Name} [{regionName}]";

        CheatList cheatList;

        // 1. Check the in-folder pending cache first so two files that resolve to
        //    the same disc+title within one folder don't create duplicate lists.
        var pendingKey = $"{game.Title}||{listTitle}";
        if (pendingCheatLists.TryGetValue(pendingKey, out var pendingList))
        {
            cheatList = pendingList;
            cheatList.UpdatedAt = DateTime.UtcNow;
            // Detach the previously staged cheats so they are not double-inserted.
            // ToList() snapshots the collection before we clear it on the next line.
            foreach (var c in cheatList.Cheats.ToList())
            {
                foreach (var line in c.CheatLines)
                    _db.Entry(line).State = EntityState.Detached;
                _db.Entry(c).State = EntityState.Detached;
            }
            cheatList.Cheats.Clear();
        }
        // 2. Check the pre-loaded DB cache (only valid for discs that already have an Id).
        else if (disc.Id > 0 && cheatListCache.TryGetValue((disc.Id, listTitle), out var existingList))
        {
            cheatList = existingList;
            cheatList.UpdatedAt = DateTime.UtcNow;
            // Bulk-delete existing cheats without loading them into memory.
            // CheatLines are removed first so the delete is explicit and order-safe
            // even though the Cheat->CheatLine FK cascade would also remove them.
            var cheatIds = await _db.Cheats
                .Where(c => c.CheatListId == cheatList.Id)
                .Select(c => c.Id)
                .ToListAsync(ct);
            if (cheatIds.Count > 0)
            {
                await _db.CheatLines
                    .Where(cl => cheatIds.Contains(cl.CheatId))
                    .ExecuteDeleteAsync(ct);
                await _db.Cheats
                    .Where(c => c.CheatListId == cheatList.Id)
                    .ExecuteDeleteAsync(ct);
            }
        }
        // 3. No match anywhere — create a brand-new list.
        else
        {
            cheatList = new CheatList
            {
                Title = listTitle,
                Game = game,
                GameDisc = disc,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            _db.CheatLists.Add(cheatList);
            pendingCheatLists[pendingKey] = cheatList;
        }

        int sortOrder = 0;
        foreach (var entry in parsed.Cheats)
        {
            var cheat = new Cheat
            {
                Label = entry.Name,
                Author = string.IsNullOrWhiteSpace(entry.Author) ? null : entry.Author,
                Section = string.IsNullOrWhiteSpace(entry.Section) ? null : entry.Section,
                IsEnabled = false,
                SortOrder = sortOrder++,
            };

            int lineOrder = 0;
            foreach (var code in entry.Codes)
                cheat.CheatLines.Add(new CheatLine
                {
                    Code = code.ToUpperInvariant(),
                    SortOrder = lineOrder++,
                });

            cheatList.Cheats.Add(cheat);
        }
    }

    private GameDisc ResolveDisc(
        Game game,
        GameRegion region,
        string? serial,
        string? hash,
        Dictionary<string, GameDisc> discsBySerial,
        Dictionary<string, GameDisc> discsByRegion)
    {
        GameDisc? disc = null;

        if (!string.IsNullOrWhiteSpace(serial))
            discsBySerial.TryGetValue($"{game.Title}||{serial}", out disc);

        disc ??= discsByRegion.GetValueOrDefault($"{game.Title}||{region.Name}");

        if (disc is null)
        {
            disc = new GameDisc
            {
                Game = game,
                GameRegion = region,
                Serial = string.IsNullOrWhiteSpace(serial) ? null : serial,
                CRC = string.IsNullOrWhiteSpace(hash) ? null : hash.ToUpperInvariant(),
            };
            _db.GameDiscs.Add(disc);
            // Cache the new disc so subsequent files within this import run
            // for the same game + serial/region resolve to the same object.
            if (disc.Serial != null)
                discsBySerial[$"{game.Title}||{disc.Serial}"] = disc;
            discsByRegion[$"{game.Title}||{region.Name}"] = disc;
        }
        else
        {
            // Fill in missing fields if we now have them.
            if (disc.CRC is null && !string.IsNullOrWhiteSpace(hash))
                disc.CRC = hash.ToUpperInvariant();
            if (disc.Serial is null && !string.IsNullOrWhiteSpace(serial))
            {
                disc.Serial = serial;
                discsBySerial[$"{game.Title}||{serial}"] = disc;
            }
        }

        return disc;
    }

    // ── CMP plain-text parser ────────────────────────────────────────────────

    private static ParsedFile ParseFile(string path)
    {
        var result = new ParsedFile();
        ParsedCheat? current = null;
        string? currentSection = null;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.TrimEnd();

            // Skip blank lines and comment lines.
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('\\'))
                continue;

            // Header: ^N = KEY: value
            if (line.StartsWith('^'))
            {
                var m = HeaderLine.Match(line);
                if (m.Success)
                {
                    var key = m.Groups["key"].Value.ToUpperInvariant();
                    var val = m.Groups["value"].Value.Trim();
                    switch (key)
                    {
                        case "NAME":   result.Name = val; break;
                        case "GAMEID": result.Serial = NormalizeSerial(val); break;
                        case "HASH":   result.Hash = val; break;
                    }
                }
                continue;
            }

            // Section header: !Section Name:
            if (line.StartsWith('!'))
            {
                CommitCheat(result, current);
                current = null;
                currentSection = line.Length > 1 ? line[1..].TrimEnd(':').Trim() : null;
                continue;
            }

            // Section end: !!
            if (line == "!!")
            {
                CommitCheat(result, current);
                current = null;
                currentSection = null;
                continue;
            }

            // Cheat entry: +Cheat Name
            if (line.StartsWith('+'))
            {
                CommitCheat(result, current);
                current = new ParsedCheat
                {
                    Name = line[1..].Trim(),
                    Section = currentSection,
                };
                continue;
            }

            if (current is null)
                continue;

            // Author: %Credits: name  or  %Credit: name
            if (line.StartsWith('%'))
            {
                var colon = line.IndexOf(':');
                if (colon >= 0)
                {
                    var author = line[(colon + 1)..].Trim();
                    current.Author = string.IsNullOrEmpty(current.Author)
                        ? author
                        : current.Author + ", " + author;
                }
                continue;
            }

            // Code line: $XXXXXXXX YYYYYYYY
            if (line.StartsWith('$'))
            {
                var m = CodeLine.Match(line);
                if (m.Success)
                    current.Codes.Add(m.Groups["code"].Value);
            }
        }

        CommitCheat(result, current);
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void CommitCheat(ParsedFile file, ParsedCheat? cheat)
    {
        if (cheat is not null && !string.IsNullOrWhiteSpace(cheat.Name))
            file.Cheats.Add(cheat);
    }

    /// <summary>
    /// Derive a normalised region name from filename, then fall back to the
    /// serial prefix if no region was explicit in the filename.
    /// </summary>
    private static string RegionFromFilename(string fileName)
    {
        var m = RegionInFilename.Match(fileName);
        if (!m.Success)
            return string.Empty;

        return NormalizeRegion(m.Groups["region"].Value);
    }

    private static string ResolveRegionName(string fromFile, string? serial)
    {
        if (!string.IsNullOrWhiteSpace(fromFile))
            return fromFile;

        if (!string.IsNullOrWhiteSpace(serial))
        {
            var prefix = serial.Length >= 4 ? serial[..4].ToUpperInvariant() : string.Empty;
            return prefix switch
            {
                "SLUS" or "SCUS" or "NPUS" => "NTSC-U",
                "SLPS" or "SCPS" or "SLPM" or "SCPM" => "NTSC-J",
                "SLES" or "SCES" or "SCED" or "SLED" => "PAL",
                _ => "Unknown",
            };
        }

        return "Unknown";
    }

    private static string NormalizeRegion(string raw)
    {
        // Strip market sub-code: "PAL-M6" → "PAL", "Ntsc-U" → "NTSC-U"
        var upper = raw.ToUpperInvariant();
        if (upper.StartsWith("NTSC-U"))  return "NTSC-U";
        if (upper.StartsWith("NTSC-J"))  return "NTSC-J";
        if (upper.StartsWith("NTSC"))    return "NTSC-U";
        if (upper.StartsWith("PAL"))     return "PAL";
        return raw;
    }

    /// <summary>
    /// Normalises serial numbers to a consistent "SLUS-20265" hyphen format,
    /// removing embedded dots from the underscore variant "SLUS_202.65".
    /// </summary>
    private static string NormalizeSerial(string raw)
    {
        // "SLUS_202.65" → "SLUS-20265"
        var s = raw.Replace('_', '-').Replace(".", string.Empty).Trim().ToUpperInvariant();
        return s;
    }

    private static TEntity GetOrCreate<TEntity>(
        Dictionary<string, TEntity> cache,
        string key,
        Func<TEntity> factory)
    {
        if (cache.TryGetValue(key, out var existing))
            return existing;

        var created = factory();
        cache[key] = created;
        return created;
    }

    // ── Internal DTOs ─────────────────────────────────────────────────────────

    private sealed class ParsedFile
    {
        public string? Name        { get; set; }
        public string? Serial      { get; set; }
        public string? Hash        { get; set; }
        public string? RegionHint  { get; set; }
        public List<ParsedCheat> Cheats { get; } = [];
    }

    private sealed class ParsedCheat
    {
        public string Name     { get; set; } = string.Empty;
        public string? Author  { get; set; }
        public string? Section { get; set; }
        public List<string> Codes { get; } = [];
    }
}

