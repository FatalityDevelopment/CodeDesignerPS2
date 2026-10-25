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

public sealed class BulkCheatImportResult
{
    public int FilesProcessed { get; set; }
    public int FilesImported { get; set; }
    public int FilesSkipped { get; set; }
    public int CheatLists { get; set; }
    public int Cheats { get; set; }
}

/// <summary>
/// Walks the <c>Database/</c> folders inside the CheatCodeArchive checkout and
/// upserts all PS2 cheat data into the database.
/// </summary>
public class BulkCheatParser
{
    private const string DefaultSection = "Others";

    private static readonly Dictionary<string, string> SourceLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MEMORY-PS2-CODEBREAKER"]     = "CodeBreaker",
        ["MEMORY-PS2-GSHI-DB"]         = "GSHI",
        ["MEMORY-PS2-CMP"]             = "CMP",
        ["MEMORY-PS2-XPLODER-DB-[EU]"] = "Xploder (EU)",
    };

    private static readonly Regex RegionInFilename =
        new(@"\((?<region>[A-Za-z]+(?:-[A-Za-z0-9]+)?)\)", RegexOptions.Compiled);

    private static readonly Regex Serial =
        new(@"\b(?<serial>S(?:L|C|I|P|C)?(?:US|ES|PS|PM|ED|CM|CD|EC|EA|UC|JA|KS)[-_ ]?\d{3}[\._ -]?\d{2})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HeaderLine =
        new(@"^(?:\^[0-9]+\s*=\s*)?(?<key>NAME|GAME|TITLE|GAMEID|GAME ID|SERIAL|HASH|CRC|REGION)\s*[:=]\s*(?<value>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CmpCodeLine =
        new(@"^\$\s*(?<code>[0-9A-Fa-f?*]{8}\s+[0-9A-Fa-f?*]{8}(?:\s+[0-9A-Fa-f?*]{1,8})*)\s*$",
            RegexOptions.Compiled);

    private static readonly Regex PlainCodeLine =
        new(@"^(?<code>[0-9A-Fa-f?*]{8}(?:\s+[0-9A-Fa-f?*]{1,8}){1,3})\s*$",
            RegexOptions.Compiled);

    private static readonly Regex SectionMarker =
        new(@"^\[--SectionStart:(?<name>.*)--\]$", RegexOptions.Compiled);

    private static readonly Regex BracketLine =
        new(@"^\[(?<label>.+)\]$", RegexOptions.Compiled);

    private readonly CodeDesignerDbContext _db;
    private readonly string _archiveRoot;
    /// <summary>Normalised serial (e.g. "SCUS-97275") to import only that game's files; null imports everything.</summary>
    private readonly string? _serialFilter;

    public BulkCheatParser(CodeDesignerDbContext db, string archiveRoot, string? serialFilter = null)
    {
        _db = db;
        _archiveRoot = archiveRoot;
        _serialFilter = string.IsNullOrWhiteSpace(serialFilter) ? null : NormalizeSerial(serialFilter);
    }

    /// <summary>
    /// Run the full import. Existing records matched by game + source-list title
    /// are replaced; new records are inserted.
    /// </summary>
    public async Task<BulkCheatImportResult> RunAsync(CancellationToken ct = default)
    {
        var dbRoot = ResolveDatabaseRoot(_archiveRoot);
        var caches = await LoadCachesAsync(ct);
        var pendingCheatLists = new Dictionary<string, CheatList>(StringComparer.OrdinalIgnoreCase);
        var result = new BulkCheatImportResult();

        foreach (var filePath in Directory.EnumerateFiles(dbRoot, "*.txt", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            // With a serial filter, skip files whose name carries a different serial
            // without parsing them (names without one are checked by their header).
            if (_serialFilter != null)
            {
                var nameSerial = Serial.Match(Path.GetFileName(filePath));
                if (nameSerial.Success && !string.Equals(NormalizeSerial(nameSerial.Groups["serial"].Value), _serialFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            result.FilesProcessed++;
            pendingCheatLists.Clear();

            try
            {
                await ProcessFileAsync(
                    dbRoot, filePath,
                    caches.RegionCache, caches.GameCache,
                    caches.DiscsBySerial, caches.DiscsByRegion,
                    caches.CheatListCache, pendingCheatLists,
                    caches.GamesBySerial,
                    ct);

                if (pendingCheatLists.Count == 0)
                    continue;

                await _db.SaveChangesAsync(ct);
                foreach (var (key, cl) in pendingCheatLists)
                    caches.CheatListCache[key] = cl;
                result.FilesImported++;
            }
            catch (Exception ex)
            {
                result.FilesSkipped++;
                Console.Error.WriteLine($"[BulkCheatParser] Skipping {filePath}: {FormatException(ex)}");

                // A failed SaveChanges leaves the current DbContext graph in an
                // unknown state. Clear it and rebuild caches from already-committed
                // rows so one bad archive file never poisons the rest of the import.
                _db.ChangeTracker.Clear();
                caches = await LoadCachesAsync(ct);
            }
        }

        result.CheatLists = await _db.CheatLists.CountAsync(ct);
        result.Cheats = await _db.Cheats.CountAsync(ct);
        return result;
    }

    private async Task<ImportCaches> LoadCachesAsync(CancellationToken ct)
    {
        var regionCache = await _db.GameRegions
            .ToDictionaryAsync(r => r.Name, StringComparer.OrdinalIgnoreCase, ct);
        var gameCache = (await _db.Games.ToListAsync(ct))
            .GroupBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var discsBySerial = new Dictionary<string, GameDisc>(StringComparer.OrdinalIgnoreCase);
        var discsByRegion = new Dictionary<string, GameDisc>(StringComparer.OrdinalIgnoreCase);
        var gamesBySerial = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
        foreach (var disc in await _db.GameDiscs.Include(d => d.Game).Include(d => d.GameRegion).OrderBy(d => d.GameId).ToListAsync(ct))
        {
            if (disc.Serial != null)
            {
                discsBySerial[$"{disc.Game.Title}||{disc.Serial}"] = disc;
                // The oldest game with the serial wins — that's the one the user works with
                // (created by the label manager / cheats editor before any import).
                gamesBySerial.TryAdd(disc.Serial, disc.Game);
            }
            discsByRegion[$"{disc.Game.Title}||{disc.GameRegion.Name}"] = disc;
        }

        var cheatListCache = (await _db.CheatLists
                .Include(cl => cl.Game)
                .Where(cl => cl.Game != null)
                .ToListAsync(ct))
            .GroupBy(cl => $"{cl.Game!.Title}||{cl.Title}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return new ImportCaches(regionCache, gameCache, discsBySerial, discsByRegion, cheatListCache, gamesBySerial);
    }

    private static string ResolveDatabaseRoot(string archiveRoot)
    {
        if (Directory.Exists(Path.Combine(archiveRoot, "Database")))
            return Path.Combine(archiveRoot, "Database");
        if (string.Equals(Path.GetFileName(archiveRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                "Database", StringComparison.OrdinalIgnoreCase) && Directory.Exists(archiveRoot))
            return archiveRoot;
        throw new DirectoryNotFoundException($"CheatCodeArchive Database folder not found under: {archiveRoot}");
    }

    private async Task ProcessFileAsync(
        string dbRoot,
        string filePath,
        Dictionary<string, GameRegion> regionCache,
        Dictionary<string, Game> gameCache,
        Dictionary<string, GameDisc> discsBySerial,
        Dictionary<string, GameDisc> discsByRegion,
        Dictionary<string, CheatList> cheatListCache,
        Dictionary<string, CheatList> pendingCheatLists,
        Dictionary<string, Game> gamesBySerial,
        CancellationToken ct)
    {
        var parsed = ParseFile(filePath);
        if (string.IsNullOrWhiteSpace(parsed.Name) || parsed.Cheats.Count == 0)
            return;
        if (_serialFilter != null && !string.Equals(parsed.Serial, _serialFilter, StringComparison.OrdinalIgnoreCase))
            return;

        var regionName = Truncate(ResolveRegionName(parsed.RegionHint ?? RegionFromFilename(Path.GetFileName(filePath)), parsed.Serial), 64);
        var region = GetOrCreate(regionCache, regionName, () =>
        {
            var r = new GameRegion { Name = regionName };
            _db.GameRegions.Add(r);
            return r;
        });

        // Attach to the game that already owns a disc with this serial (the one the cheats
        // editor shows), so imported lists sit next to the user's own lists; only games
        // without a known disc are matched or created by the file's title.
        var gameTitle = CleanTitle(parsed.Name!);
        Game game;
        if (!string.IsNullOrWhiteSpace(parsed.Serial) && gamesBySerial.TryGetValue(parsed.Serial, out var owner))
        {
            game = owner;
        }
        else
        {
            game = GetOrCreate(gameCache, gameTitle, () =>
            {
                var g = new Game { Title = gameTitle };
                _db.Games.Add(g);
                return g;
            });
            if (!string.IsNullOrWhiteSpace(parsed.Serial))
                gamesBySerial[parsed.Serial] = game;
        }

        var disc = ResolveDisc(game, region, parsed.Serial, parsed.Hash, discsBySerial, discsByRegion);
        var source = SourceFor(dbRoot, filePath);
        var listTitle = BuildListTitle(source, game.Title, regionName);
        var listKey = $"{game.Title}||{listTitle}";

        // A list imported before this rule lives under a title-named duplicate game:
        // move it onto the serial's game instead of importing a second copy.
        var legacyKey = $"{gameTitle}||{BuildListTitle(source, gameTitle, regionName)}";
        if (!string.Equals(game.Title, gameTitle, StringComparison.OrdinalIgnoreCase)
            && !pendingCheatLists.ContainsKey(listKey)
            && !cheatListCache.ContainsKey(listKey)
            && cheatListCache.Remove(legacyKey, out var legacyList))
        {
            legacyList.Title = listTitle;
            cheatListCache[listKey] = legacyList;
        }

        CheatList cheatList;
        if (pendingCheatLists.TryGetValue(listKey, out var pendingList))
        {
            cheatList = pendingList;
            cheatList.UpdatedAt = DateTime.UtcNow;
            foreach (var c in cheatList.Cheats.ToList())
            {
                foreach (var line in c.CheatLines)
                    _db.Entry(line).State = EntityState.Detached;
                _db.Entry(c).State = EntityState.Detached;
            }
            cheatList.Cheats.Clear();
        }
        else if (cheatListCache.TryGetValue(listKey, out var existingList))
        {
            cheatList = existingList;
            cheatList.Game = game;
            cheatList.GameDisc = disc;
            cheatList.UpdatedAt = DateTime.UtcNow;
            var oldCheats = await _db.Cheats
                .Include(c => c.CheatLines)
                .Where(c => c.CheatListId == cheatList.Id)
                .ToListAsync(ct);
            if (oldCheats.Count > 0)
                _db.Cheats.RemoveRange(oldCheats);
            pendingCheatLists[listKey] = cheatList;
        }
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
            pendingCheatLists[listKey] = cheatList;
        }

        int sortOrder = 0;
        foreach (var entry in parsed.Cheats)
        {
            var cheat = new Cheat
            {
                Label = entry.Name,
                Author = string.IsNullOrWhiteSpace(entry.Author) ? null : Truncate(entry.Author, 256),
                Description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description.Trim(),
                Section = string.IsNullOrWhiteSpace(entry.Section) ? DefaultSection : Truncate(entry.Section, 256),
                IsEnabled = false,
                SortOrder = sortOrder++,
            };

            int lineOrder = 0;
            foreach (var code in entry.Codes.Distinct(StringComparer.OrdinalIgnoreCase))
                cheat.CheatLines.Add(new CheatLine { Code = code.ToUpperInvariant(), SortOrder = lineOrder++ });

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
                Serial = string.IsNullOrWhiteSpace(serial) ? null : Truncate(serial, 32),
                CRC = string.IsNullOrWhiteSpace(hash) ? null : Truncate(hash.ToUpperInvariant(), 16),
            };
            _db.GameDiscs.Add(disc);
            if (disc.Serial != null)
                discsBySerial[$"{game.Title}||{disc.Serial}"] = disc;
            discsByRegion[$"{game.Title}||{region.Name}"] = disc;
        }
        else
        {
            if (disc.CRC is null && !string.IsNullOrWhiteSpace(hash))
                disc.CRC = Truncate(hash.ToUpperInvariant(), 16);
            if (disc.Serial is null && !string.IsNullOrWhiteSpace(serial))
            {
                disc.Serial = Truncate(serial, 32);
                discsBySerial[$"{game.Title}||{disc.Serial}"] = disc;
            }
        }
        return disc;
    }

    private static ParsedFile ParseFile(string path)
    {
        var result = new ParsedFile { Name = TitleFromPath(path) };
        ParsedCheat? current = null;
        string currentSection = DefaultSection;
        var pendingDescription = new List<string>();
        string? pendingName = null;
        var generated = 1;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (TryApplyHeader(line, result))
                continue;

            if (TryReadComment(line, out var comment))
            {
                ApplyComment(comment, current, pendingDescription);
                continue;
            }

            var section = TryReadSection(line);
            if (section is not null)
            {
                CommitCheat(result, current);
                current = null;
                pendingName = null;
                currentSection = section.Length == 0 ? DefaultSection : section;
                pendingDescription.Clear();
                continue;
            }

            if (line == "!!")
            {
                CommitCheat(result, current);
                current = null;
                currentSection = DefaultSection;
                pendingName = null;
                pendingDescription.Clear();
                continue;
            }

            if (TryReadCheatName(line, out var explicitName))
            {
                CommitCheat(result, current);
                current = NewCheat(explicitName, currentSection, pendingDescription);
                pendingDescription.Clear();
                pendingName = null;
                continue;
            }

            if (TryReadAuthor(line, out var author))
            {
                if (current is not null)
                    current.Author = Append(current.Author, author);
                else
                    pendingDescription.Add($"Author: {author}");
                continue;
            }

            if (TryReadCode(line, out var code))
            {
                if (current is null)
                {
                    current = NewCheat(pendingName ?? $"Code {generated++}", currentSection, pendingDescription);
                    pendingDescription.Clear();
                    pendingName = null;
                }
                current.Codes.Add(code);
                continue;
            }

            if (current is not null && current.Codes.Count > 0)
            {
                CommitCheat(result, current);
                current = NewCheat(line, currentSection, pendingDescription);
                pendingDescription.Clear();
                pendingName = null;
            }
            else if (current is not null)
            {
                current.Description = Append(current.Description, line, Environment.NewLine);
            }
            else
            {
                pendingName = line;
            }
        }

        CommitCheat(result, current);
        return result;
    }

    private static bool TryApplyHeader(string line, ParsedFile result)
    {
        var m = HeaderLine.Match(line.TrimStart('^'));
        if (!m.Success)
            m = HeaderLine.Match(line);
        if (!m.Success)
        {
            var sm = Serial.Match(line);
            if (sm.Success && result.Serial is null)
                result.Serial = NormalizeSerial(sm.Groups["serial"].Value);
            return false;
        }

        var key = m.Groups["key"].Value.Replace(" ", string.Empty).ToUpperInvariant();
        var val = m.Groups["value"].Value.Trim();
        switch (key)
        {
            case "NAME":
            case "GAME":
            case "TITLE": result.Name = val; break;
            case "GAMEID":
            case "SERIAL": result.Serial = NormalizeSerial(val); break;
            case "HASH":
            case "CRC": result.Hash = Regex.Replace(val, @"[^0-9A-Fa-f]", string.Empty); break;
            case "REGION": result.RegionHint = NormalizeRegion(val); break;
        }
        return true;
    }

    private static bool TryReadComment(string line, out string comment)
    {
        comment = string.Empty;
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
            comment = trimmed[2..].Trim();
        else if (trimmed.StartsWith('\\') || trimmed.StartsWith(';'))
            comment = trimmed[1..].Trim();
        else if (trimmed.StartsWith('#'))
            comment = trimmed[1..].Trim();
        return comment.Length > 0;
    }

    private static void ApplyComment(string comment, ParsedCheat? current, List<string> pendingDescription)
    {
        if (TryReadAuthor(comment, out var author))
        {
            if (current is not null)
                current.Author = Append(current.Author, author);
            else
                pendingDescription.Add($"Author: {author}");
            return;
        }

        if (comment.StartsWith("Description:", StringComparison.OrdinalIgnoreCase))
            comment = comment[12..].Trim();

        if (current is not null)
            current.Description = Append(current.Description, comment, Environment.NewLine);
        else
            pendingDescription.Add(comment);
    }

    private static string? TryReadSection(string line)
    {
        var sectionMatch = SectionMarker.Match(line);
        if (sectionMatch.Success)
            return sectionMatch.Groups["name"].Value.Trim();

        if (line.StartsWith('!') && line != "!!")
            return line[1..].TrimEnd(':').Trim();

        if (line.EndsWith(':') && !HeaderLine.IsMatch(line) && !PlainCodeLine.IsMatch(line))
            return line.TrimEnd(':').Trim();

        return null;
    }

    private static bool TryReadCheatName(string line, out string name)
    {
        name = string.Empty;
        if (line.StartsWith('+'))
        {
            name = line[1..].Trim();
            return name.Length > 0;
        }

        var bracket = BracketLine.Match(line);
        if (bracket.Success && !Serial.IsMatch(line))
        {
            name = bracket.Groups["label"].Value.Trim();
            return name.Length > 0;
        }

        return false;
    }

    private static bool TryReadAuthor(string line, out string author)
    {
        author = string.Empty;
        var m = Regex.Match(line, @"^(?:%\s*)?(?:Credits?|Author|By)\s*[:=]\s*(?<author>.+)$", RegexOptions.IgnoreCase);
        if (!m.Success)
            return false;
        author = m.Groups["author"].Value.Trim();
        return author.Length > 0;
    }

    private static bool TryReadCode(string line, out string code)
    {
        code = string.Empty;
        var stripped = StripInlineComment(line).Trim();
        var cmp = CmpCodeLine.Match(stripped);
        var plain = cmp.Success ? cmp : PlainCodeLine.Match(stripped);
        if (!plain.Success)
            return false;
        code = Regex.Replace(plain.Groups["code"].Value, @"\s+", " ").ToUpperInvariant();
        return code.Length <= 32;
    }

    private static string StripInlineComment(string line)
    {
        var idx = line.IndexOf("//", StringComparison.Ordinal);
        return idx < 0 ? line : line[..idx];
    }

    private static ParsedCheat NewCheat(string name, string section, List<string> pendingDescription)
    {
        var cheat = new ParsedCheat
        {
            Name = CleanCheatName(name),
            Section = string.IsNullOrWhiteSpace(section) ? DefaultSection : section,
        };
        foreach (var note in pendingDescription.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            if (TryReadAuthor(note, out var author))
                cheat.Author = Append(cheat.Author, author);
            else
                cheat.Description = Append(cheat.Description, note, Environment.NewLine);
        }
        return cheat;
    }

    private static void CommitCheat(ParsedFile file, ParsedCheat? cheat)
    {
        if (cheat is not null && !string.IsNullOrWhiteSpace(cheat.Name) && cheat.Codes.Count > 0)
            file.Cheats.Add(cheat);
    }

    private static string BuildListTitle(string source, string gameTitle, string regionName)
    {
        var prefix = $"[{source}] ";
        var suffix = $" [{regionName}]";
        var maxGameTitleLength = Math.Max(1, 512 - prefix.Length - suffix.Length);
        return prefix + Truncate(gameTitle, maxGameTitleLength) + suffix;
    }

    private static string FormatException(Exception ex)
    {
        var messages = new List<string>();
        for (var cur = ex; cur is not null; cur = cur.InnerException)
            messages.Add(cur.Message);
        return string.Join(" -> ", messages);
    }

    private static string SourceFor(string dbRoot, string filePath)
    {
        var rel = Path.GetRelativePath(dbRoot, filePath);
        var top = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).FirstOrDefault() ?? "Archive";
        return SourceLabels.TryGetValue(top, out var label) ? label : top;
    }

    private static string TitleFromPath(string path)
    {
        var title = Path.GetFileNameWithoutExtension(path);
        if (title.EndsWith(".CMP", StringComparison.OrdinalIgnoreCase))
            title = title[..^4];
        title = RegionInFilename.Replace(title, string.Empty);
        title = Serial.Replace(title, string.Empty);
        title = Regex.Replace(title, @"\s+", " ").Trim(' ', '-', '_');
        return string.IsNullOrWhiteSpace(title) ? "Unknown Game" : title;
    }

    private static string CleanTitle(string title)
    {
        title = Regex.Replace(title, @"\s+", " ").Trim();
        return Truncate(title, 512);
    }

    private static string CleanCheatName(string name)
    {
        name = Regex.Replace(name, @"\s+", " ").Trim();
        return Truncate(string.IsNullOrWhiteSpace(name) ? "Untitled Cheat" : name, 512);
    }

    private static string RegionFromFilename(string fileName)
    {
        var m = RegionInFilename.Match(fileName);
        return m.Success ? NormalizeRegion(m.Groups["region"].Value) : string.Empty;
    }

    private static string ResolveRegionName(string? fromFile, string? serial)
    {
        if (!string.IsNullOrWhiteSpace(fromFile))
            return NormalizeRegion(fromFile);
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
        var upper = raw.Trim().ToUpperInvariant();
        if (upper.StartsWith("NTSC-U")) return "NTSC-U";
        if (upper.StartsWith("NTSC-J")) return "NTSC-J";
        if (upper.StartsWith("NTSC")) return "NTSC-U";
        if (upper.StartsWith("PAL")) return "PAL";
        return string.IsNullOrWhiteSpace(raw) ? "Unknown" : raw.Trim();
    }

    private static string NormalizeSerial(string raw)
    {
        var match = Serial.Match(raw);
        var s = match.Success ? match.Groups["serial"].Value : raw;
        s = s.Replace('_', '-').Replace(".", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
        if (s.Length >= 8 && s[4] != '-')
            s = s.Insert(4, "-");
        return s;
    }

    private static string? Append(string? left, string right, string separator = ", ")
        => string.IsNullOrWhiteSpace(left) ? right.Trim() : left + separator + right.Trim();

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static TEntity GetOrCreate<TEntity>(Dictionary<string, TEntity> cache, string key, Func<TEntity> factory)
    {
        if (cache.TryGetValue(key, out var existing))
            return existing;
        var created = factory();
        cache[key] = created;
        return created;
    }

    private sealed record ImportCaches(
        Dictionary<string, GameRegion> RegionCache,
        Dictionary<string, Game> GameCache,
        Dictionary<string, GameDisc> DiscsBySerial,
        Dictionary<string, GameDisc> DiscsByRegion,
        Dictionary<string, CheatList> CheatListCache,
        Dictionary<string, Game> GamesBySerial);

    private sealed class ParsedFile
    {
        public string? Name { get; set; }
        public string? Serial { get; set; }
        public string? Hash { get; set; }
        public string? RegionHint { get; set; }
        public List<ParsedCheat> Cheats { get; } = [];
    }

    private sealed class ParsedCheat
    {
        public string Name { get; set; } = string.Empty;
        public string? Author { get; set; }
        public string? Description { get; set; }
        public string? Section { get; set; }
        public List<string> Codes { get; } = [];
    }
}
