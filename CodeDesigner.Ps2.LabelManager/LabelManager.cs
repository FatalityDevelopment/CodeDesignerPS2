using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeDesigner.Ps2.LabelManager.Models;

namespace CodeDesigner.Ps2.LabelManager
{
    /// <summary>
    /// Manages per-game label sets and the built-in PS2 system label database.
    ///
    /// Label sets live on disk at:
    ///   <c>&lt;workspaceDir&gt;/snapshots/&lt;gameId&gt;/labels/&lt;setName&gt;.json</c>
    ///
    /// The built-in set is an embedded resource (<c>Resources/BuiltIn.json</c>)
    /// and is always merged into the result of <see cref="GetMergedLabels"/>.
    /// It cannot be modified at runtime.
    /// </summary>
    public class Ps2LabelManager
    {
        // ── Built-in ──────────────────────────────────────────────────────────

        private static readonly LabelSet _builtIn = LoadBuiltIn();

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        // ── State ─────────────────────────────────────────────────────────────

        /// <summary>Name of the currently active user set, or null when none.</summary>
        public string? ActiveSetName { get; private set; }

        /// <summary>Current workspace root directory sent by the client.</summary>
        private string? _workspaceDir;

        /// <summary>Current game identifier (ISO serial) sent by the client.</summary>
        private string? _gameId;

        // ── Construction / Built-in loading ──────────────────────────────────

        private static LabelSet LoadBuiltIn()
        {
            var asm  = Assembly.GetExecutingAssembly();
            // Embedded resource name: <RootNamespace>.Resources.BuiltIn.json
            var name = asm.GetManifestResourceNames()
                          .FirstOrDefault(n => n.EndsWith("BuiltIn.json", StringComparison.OrdinalIgnoreCase));

            if (name is null)
                return new LabelSet { SetName = "Built-in", IsBuiltIn = true };

            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            // The embedded file uses the legacy { "symbol": [...] } format (plain labels).
            // Parse it and convert to the unified LabelEntry model.
            using var doc = JsonDocument.Parse(json);
            var entries = new List<LabelEntry>();

            // Support both the legacy { symbol: [...] } format and the native { entries: [...] } format.
            if (doc.RootElement.TryGetProperty("entries", out var entriesEl))
            {
                entries = JsonSerializer.Deserialize<List<LabelEntry>>(entriesEl.GetRawText(), _jsonOpts) ?? [];
            }
            else if (doc.RootElement.TryGetProperty("symbol", out var symbolEl))
            {
                foreach (var sym in symbolEl.EnumerateArray())
                {
                    var addr    = sym.TryGetProperty("address", out var ap) ? ap.GetString() : null;
                    var label   = sym.TryGetProperty("label",   out var lp) ? lp.GetString() : null;
                    var comment = sym.TryGetProperty("comment", out var cp) ? cp.GetString() : null;

                    if (string.IsNullOrWhiteSpace(addr) || string.IsNullOrWhiteSpace(label))
                        continue;

                    entries.Add(new LabelEntry
                    {
                        Address = NormalizeAddress(addr),
                        Name    = label,
                        Comment = comment,
                    });
                }
            }

            return new LabelSet
            {
                SetName   = "Built-in",
                IsBuiltIn = true,
                Entries   = entries,
            };
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>The read-only built-in label set (PS2 system symbols).</summary>
        public LabelSet BuiltIn => _builtIn;

        /// <summary>
        /// Clears the active user set. The merged label list will contain only the
        /// built-in symbols until <see cref="SetActiveSet"/> is called again.
        /// </summary>
        public void ClearActiveSet()
        {
            ActiveSetName = null;
        }

        /// <summary>
        /// Set the current game context so that subsequent calls that omit
        /// workspaceDir / gameId can use these defaults.
        /// </summary>
        public void SetContext(string workspaceDir, string gameId)
        {
            _workspaceDir = workspaceDir;
            _gameId       = gameId;
        }

        /// <summary>
        /// Lists the names of all user-defined label sets stored on disk for the given game.
        /// </summary>
        public IReadOnlyList<string> GetSetNames(string workspaceDir, string gameId)
        {
            var dir = LabelsDir(workspaceDir, gameId);
            if (!Directory.Exists(dir)) return [];

            return Directory.GetFiles(dir, "*.json")
                            .Select(f => Path.GetFileNameWithoutExtension(f))
                            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                            .ToList();
        }

        /// <summary>Loads a specific label set from disk.</summary>
        public LabelSet? LoadSet(string workspaceDir, string gameId, string setName)
        {
            var path = SetPath(workspaceDir, gameId, setName);
            if (!File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                var set  = JsonSerializer.Deserialize<LabelSet>(json, _jsonOpts);
                if (set is null) return null;
                set.SetName = setName; // ensure consistent name
                return set;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LabelManager] Failed to load '{setName}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sets the active label set. Loads it from disk and stores context.
        /// Returns the loaded set, or null when the file does not exist.
        /// </summary>
        public LabelSet? SetActiveSet(string workspaceDir, string gameId, string setName)
        {
            SetContext(workspaceDir, gameId);
            var set = LoadSet(workspaceDir, gameId, setName);
            if (set is not null)
                ActiveSetName = setName;
            return set;
        }

        /// <summary>
        /// Creates a new empty label set on disk and returns it.
        /// Throws if the set already exists.
        /// </summary>
        public LabelSet CreateSet(string workspaceDir, string gameId, string setName)
        {
            var path = SetPath(workspaceDir, gameId, setName);
            if (File.Exists(path))
                throw new InvalidOperationException($"Label set '{setName}' already exists.");

            EnsureLabelsDir(workspaceDir, gameId);
            var set = new LabelSet { SetName = setName, GameId = gameId };
            SaveSet(set, workspaceDir, gameId);
            return set;
        }

        /// <summary>Deletes a label set from disk.</summary>
        public void DeleteSet(string workspaceDir, string gameId, string setName)
        {
            var path = SetPath(workspaceDir, gameId, setName);
            if (File.Exists(path))
                File.Delete(path);

            if (ActiveSetName == setName)
                ActiveSetName = null;
        }

        /// <summary>Saves a label set to disk.</summary>
        public void SaveSet(LabelSet set, string workspaceDir, string gameId)
        {
            EnsureLabelsDir(workspaceDir, gameId);
            var path = SetPath(workspaceDir, gameId, set.SetName);
            var json = JsonSerializer.Serialize(set, _jsonOpts);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Adds or updates an entry in the given set and saves.
        /// Looks up by address (normalised).
        /// </summary>
        public LabelSet AddOrUpdate(string workspaceDir, string gameId, string setName, LabelEntry entry)
        {
            var set = LoadSet(workspaceDir, gameId, setName) ?? new LabelSet { SetName = setName, GameId = gameId };
            entry.Address = NormalizeAddress(entry.Address);
            var idx = set.Entries.FindIndex(e =>
                NormalizeAddress(e.Address).Equals(entry.Address, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                set.Entries[idx] = entry;
            else
                set.Entries.Add(entry);

            set.Entries.Sort((a, b) => string.Compare(a.Address, b.Address, StringComparison.OrdinalIgnoreCase));
            SaveSet(set, workspaceDir, gameId);
            return set;
        }

        /// <summary>Removes an entry by address from the given set and saves.</summary>
        public LabelSet DeleteEntry(string workspaceDir, string gameId, string setName, string address)
        {
            var set = LoadSet(workspaceDir, gameId, setName);
            if (set is null) throw new InvalidOperationException($"Label set '{setName}' not found.");
            var norm = NormalizeAddress(address);
            set.Entries.RemoveAll(e => NormalizeAddress(e.Address).Equals(norm, StringComparison.OrdinalIgnoreCase));
            SaveSet(set, workspaceDir, gameId);
            return set;
        }

        /// <summary>
        /// Returns the merged label list: built-in entries PLUS all entries from the
        /// specified user set. User entries take priority on address collision.
        /// </summary>
        public List<LabelEntry> GetMergedLabels(string? workspaceDir = null, string? gameId = null, string? setName = null)
        {
            var dict = new Dictionary<string, LabelEntry>(StringComparer.OrdinalIgnoreCase);

            // Built-in first (lower priority)
            foreach (var e in _builtIn.Entries)
                dict[NormalizeAddress(e.Address)] = e;

            // User set overlays (higher priority)
            var ws  = workspaceDir ?? _workspaceDir;
            var gid = gameId ?? _gameId;
            var sn  = setName ?? ActiveSetName;
            if (ws is not null && gid is not null && sn is not null)
            {
                var set = LoadSet(ws, gid, sn);
                if (set is not null)
                {
                    foreach (var e in set.Entries)
                        dict[NormalizeAddress(e.Address)] = e;
                }
            }

            return [.. dict.Values.OrderBy(e => e.Address, StringComparer.OrdinalIgnoreCase)];
        }

        /// <summary>Exports a label set to JSON. Returns the raw JSON string.</summary>
        public string Export(string workspaceDir, string gameId, string setName)
        {
            var set = LoadSet(workspaceDir, gameId, setName);
            if (set is null) throw new InvalidOperationException($"Label set '{setName}' not found.");
            return JsonSerializer.Serialize(set, _jsonOpts);
        }

        /// <summary>
        /// Imports a label set from JSON, saves it to disk under <paramref name="setName"/>
        /// and returns the parsed set. If <paramref name="setName"/> is null, uses the
        /// setName field inside the JSON.
        /// </summary>
        public LabelSet Import(string json, string workspaceDir, string gameId, string? setName = null)
        {
            LabelSet set;

            // Try the native LabelSet format first.
            try
            {
                var parsed = JsonSerializer.Deserialize<LabelSet>(json, _jsonOpts);
                if (parsed is not null && parsed.Entries.Count > 0)
                {
                    set = parsed;
                    if (setName is not null)
                        set.SetName = setName;
                    set.GameId    = gameId;
                    set.IsBuiltIn = false;
                    SaveSet(set, workspaceDir, gameId);
                    return set;
                }
            }
            catch { /* fall through to legacy format */ }

            // Attempt to read the legacy { "symbol": [...] } format.
            using var doc = JsonDocument.Parse(json);
            var entries = new List<LabelEntry>();

            if (doc.RootElement.TryGetProperty("symbol", out var symbolEl))
            {
                foreach (var sym in symbolEl.EnumerateArray())
                {
                    var addr    = sym.TryGetProperty("address", out var ap) ? ap.GetString() : null;
                    var label   = sym.TryGetProperty("label",   out var lp) ? lp.GetString() : null;
                    var comment = sym.TryGetProperty("comment", out var cp) ? cp.GetString() : null;

                    if (string.IsNullOrWhiteSpace(addr) || string.IsNullOrWhiteSpace(label))
                        continue;

                    entries.Add(new LabelEntry
                    {
                        Address = NormalizeAddress(addr),
                        Name    = label,
                        Comment = comment,
                    });
                }
            }

            set = new LabelSet
            {
                SetName   = setName ?? "Imported",
                GameId    = gameId,
                IsBuiltIn = false,
                Entries   = entries,
            };
            SaveSet(set, workspaceDir, gameId);
            return set;
        }

        // ── Display helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Returns the canonical display text for a label entry.
        /// <list type="bullet">
        ///   <item><see cref="LabelType.Function"/> → <c>FNC_Name</c></item>
        ///   <item><see cref="LabelType.Array"/> / <see cref="LabelType.LinkedList"/> with an
        ///     index → <c>Name[index]</c></item>
        ///   <item>Everything else → <c>Name</c></item>
        /// </list>
        /// </summary>
        /// <param name="entry">The label entry to format.</param>
        /// <param name="elementIndex">Optional element/node index for array or linked-list entries.</param>
        public static string GetDisplayLabel(LabelEntry entry, int? elementIndex = null)
        {
            switch (entry.Type)
            {
                case LabelType.Function:
                    return $"FNC_{entry.Name}";

                case LabelType.Array:
                case LabelType.ArrayOfPointers:
                case LabelType.LinkedList:
                    return elementIndex.HasValue ? $"{entry.Name}[{elementIndex}]" : entry.Name;

                default:
                    return entry.Name;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string LabelsDir(string workspaceDir, string gameId) =>
            Path.Combine(workspaceDir, "snapshots", gameId, "labels");

        private static string SetPath(string workspaceDir, string gameId, string setName) =>
            Path.Combine(LabelsDir(workspaceDir, gameId), SanitiseFileName(setName) + ".json");

        private static void EnsureLabelsDir(string workspaceDir, string gameId)
        {
            var dir = LabelsDir(workspaceDir, gameId);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private static string SanitiseFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        internal static string NormalizeAddress(string raw)
        {
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                raw = raw[2..];
            if (uint.TryParse(raw, System.Globalization.NumberStyles.HexNumber, null, out uint v))
                return $"0x{v:X8}";
            return raw;
        }
    }
}
