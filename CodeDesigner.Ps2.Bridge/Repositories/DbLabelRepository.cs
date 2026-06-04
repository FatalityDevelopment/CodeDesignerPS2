using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CodeDesigner.Ps2.Database;
using Microsoft.EntityFrameworkCore;

using MgrEntry = CodeDesigner.Ps2.LabelManager.Models.LabelEntry;
using MgrSet = CodeDesigner.Ps2.LabelManager.Models.LabelSet;

using DbLabel = CodeDesigner.Ps2.Database.Entities.Label;
using DbLabelSet = CodeDesigner.Ps2.Database.Entities.LabelSet;

namespace CodeDesigner.Ps2.Bridge.Repositories
{
    /// <summary>
    /// Async persistence layer for label sets, backed by <see cref="CodeDesignerDbContext"/>.
    /// All public methods accept a <paramref name="gameId"/> string (serial, IsoPath suffix,
    /// or Game.Title) that is resolved to a <see cref="Ps2.Database.Entities.GameDisc.Id"/>
    /// before any DB query, so that labels are always scoped to the exact disc release.
    /// </summary>
    internal sealed class DbLabelRepository
    {
        private readonly CodeDesignerDbContext _db;

        internal DbLabelRepository(CodeDesignerDbContext db) => _db = db;

        // ── GameDisc resolution ────────────────────────────────────────────────

        /// <summary>
        /// Resolves an arbitrary game identifier string to a <c>GameDisc.Id</c>.
        /// Resolution order:
        /// <list type="number">
        ///   <item>Direct integer — treated as <c>GameDisc.Id</c> if a matching disc exists.</item>
        ///   <item>PS2 serial ("SCUS-97130" or "SCUS_97130") — normalised and matched against
        ///         <c>GameDiscs.Serial</c>.</item>
        ///   <item>IsoPath suffix — matched against the end of <c>GameDiscs.IsoPath</c>.</item>
        ///   <item>Game.Title exact match — returns the first disc for that game.</item>
        /// </list>
        /// Returns <c>null</c> when no disc can be found.
        /// </summary>
        internal async Task<int?> ResolveGameDiscIdAsync(string identifier)
        {
            identifier = identifier.Trim();

            // 1. Numeric: treat as GameDisc.Id directly.
            if (int.TryParse(identifier, out int discId) && discId > 0)
            {
                bool exists = await _db.GameDiscs.AnyAsync(d => d.Id == discId);
                if (exists) return discId;
            }

            // 2. PS2 serial: normalise (uppercase, _ → -) and match GameDiscs.Serial.
            var normalized = NormalizeSerial(identifier);
            var discBySerial = await _db.GameDiscs
                                        .FirstOrDefaultAsync(d =>
                                            d.Serial != null &&
                                            d.Serial.Replace("_", "-").ToUpper() == normalized);
            if (discBySerial is not null) return discBySerial.Id;

            // 3. IsoPath suffix (covers bare name, name + ".iso", or full path).
            var lower = identifier.ToLowerInvariant().TrimEnd('/').TrimEnd('\\');
            var discByPath = await _db.GameDiscs
                                      .FirstOrDefaultAsync(d =>
                                          d.IsoPath != null &&
                                          (d.IsoPath.ToLower().EndsWith(lower) ||
                                           d.IsoPath.ToLower().EndsWith(lower + ".iso")));
            if (discByPath is not null) return discByPath.Id;

            // 4. Game.Title exact match → first disc for that game.
            var game = await _db.Games
                                .Include(g => g.Discs)
                                .FirstOrDefaultAsync(g => g.Title.ToLower() == lower);
            return game?.Discs.Count > 0 ? game.Discs[0].Id : (int?)null;
        }

        /// <summary>Normalises a PS2 serial to uppercase dash form, e.g. "scus_97130" → "SCUS-97130".</summary>
        internal static string NormalizeSerial(string serial) =>
            serial.Trim().ToUpperInvariant().Replace('_', '-');

        // ── Query helpers ──────────────────────────────────────────────────────

        internal async Task<IReadOnlyList<string>> GetSetNamesAsync(string gameId)
        {
            var discId = await ResolveGameDiscIdAsync(gameId);
            if (discId is null) return [];

            return await _db.LabelSets
                            .Where(ls => ls.GameDiscId == discId && !ls.IsBuiltIn)
                            .Select(ls => ls.SetName)
                            .Distinct()
                            .OrderBy(n => n)
                            .ToListAsync();
        }

        /// <summary>
        /// Returns lightweight metadata for every label set scoped to the resolved disc,
        /// in the same order <see cref="GetSetNamesAsync"/> returns names. Used by
        /// GETLABELSETS so the webview can style system sets distinctly without round-tripping
        /// each set individually.
        /// </summary>
        internal async Task<IReadOnlyList<(string SetName, bool IsBuiltIn, bool IsSystem)>> GetSetSummariesAsync(string gameId)
        {
            var discId = await ResolveGameDiscIdAsync(gameId);
            if (discId is null) return [];

            var rows = await _db.LabelSets
                                .Where(ls => ls.GameDiscId == discId && !ls.IsBuiltIn)
                                .Select(ls => new { ls.SetName, ls.IsBuiltIn, ls.IsSystem })
                                .OrderBy(r => r.SetName)
                                .ToListAsync();

            return rows.Select(r => (r.SetName, r.IsBuiltIn, r.IsSystem)).ToList();
        }

        /// <summary>
        /// Creates (or returns the existing) label set with <paramref name="setName"/> for
        /// <paramref name="gameId"/>, marked as a CodeDesigner-system set. Idempotent —
        /// re-flips the flag on an existing set so reloads converge to the right shape.
        /// </summary>
        internal async Task<MgrSet> EnsureSystemSetAsync(string gameId, string setName)
        {
            var discId = await ResolveGameDiscIdAsync(gameId);
            if (discId is null)
                throw new InvalidOperationException(
                    $"No GameDisc found for '{gameId}'. Ensure the disc is registered in the database.");

            var entity = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .Include(ls => ls.GameDisc)
                                  .FirstOrDefaultAsync(ls => ls.GameDiscId == discId && ls.SetName == setName);

            if (entity is null)
            {
                entity = new DbLabelSet
                {
                    SetName    = setName,
                    GameDiscId = discId,
                    GameId     = gameId,
                    IsBuiltIn  = false,
                    IsSystem   = true,
                };
                _db.LabelSets.Add(entity);
                await _db.SaveChangesAsync();
                await _db.Entry(entity).Reference(e => e.GameDisc).LoadAsync();
            }
            else if (!entity.IsSystem)
            {
                entity.IsSystem = true;
                await _db.SaveChangesAsync();
            }

            return DbMapper.ToMgrSet(entity);
        }

        internal async Task<MgrSet?> LoadSetAsync(string gameId, string setName)
        {
            var discId = await ResolveGameDiscIdAsync(gameId);
            if (discId is null) return null;

            var entity = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .Include(ls => ls.GameDisc)
                                  .FirstOrDefaultAsync(ls => ls.GameDiscId == discId && ls.SetName == setName);

            return entity is null ? null : DbMapper.ToMgrSet(entity);
        }

        // ── Mutating operations ────────────────────────────────────────────────

        internal async Task<MgrSet> CreateSetAsync(string gameId, string setName)
        {
            var discId = await ResolveGameDiscIdAsync(gameId);
            if (discId is null)
                throw new InvalidOperationException(
                    $"No GameDisc found for '{gameId}'. Ensure the disc is registered in the database.");

            bool exists = await _db.LabelSets
                                   .AnyAsync(ls => ls.GameDiscId == discId && ls.SetName == setName);
            if (exists)
                throw new InvalidOperationException(
                    $"Label set '{setName}' already exists for disc {discId}.");

            var entity = new DbLabelSet
            {
                SetName    = setName,
                GameDiscId = discId,
                GameId     = await ResolveCanonicalGameIdAsync(gameId),
                IsBuiltIn  = false,
            };

            _db.LabelSets.Add(entity);
            await _db.SaveChangesAsync();

            // Reload with navigation so ToMgrSet can surface the serial.
            await _db.Entry(entity).Reference(e => e.GameDisc).LoadAsync();
            return DbMapper.ToMgrSet(entity);
        }

        internal async Task DeleteSetAsync(string gameId, string setName)
        {
            var discId = await ResolveGameDiscIdAsync(gameId);
            if (discId is null) return;

            var entity = await _db.LabelSets
                                  .FirstOrDefaultAsync(ls => ls.GameDiscId == discId && ls.SetName == setName);
            if (entity is null) return;

            _db.LabelSets.Remove(entity);
            await _db.SaveChangesAsync();
        }

        internal async Task<MgrSet> RenameSetAsync(string gameId, string oldName, string newName)
        {
            var discId = await ResolveGameDiscIdAsync(gameId);
            if (discId is null)
                throw new InvalidOperationException($"No GameDisc found for '{gameId}'.");

            bool nameExists = await _db.LabelSets
                                       .AnyAsync(ls => ls.GameDiscId == discId && ls.SetName == newName);
            if (nameExists)
                throw new InvalidOperationException($"Label set '{newName}' already exists for game '{gameId}'.");

            var entity = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .Include(ls => ls.GameDisc)
                                  .FirstOrDefaultAsync(ls => ls.GameDiscId == discId && ls.SetName == oldName)
                         ?? throw new InvalidOperationException($"Label set '{oldName}' not found for game '{gameId}'.");

            if (entity.IsBuiltIn)
                throw new InvalidOperationException("The built-in label set cannot be renamed.");

            entity.SetName = newName;
            await _db.SaveChangesAsync();
            return DbMapper.ToMgrSet(entity);
        }

        /// <summary>
        /// Upserts a full label set: replaces all its Label rows with the entries supplied.
        /// Two-pass insertion: roots first (to capture their IDs), then children.
        /// </summary>
        internal async Task<MgrSet> SaveSetAsync(MgrSet set, string gameId)
        {
            var discId = await ResolveGameDiscIdAsync(gameId);
            if (discId is null)
                throw new InvalidOperationException($"No GameDisc found for '{gameId}'.");

            var entity = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .FirstOrDefaultAsync(ls => ls.GameDiscId == discId && ls.SetName == set.SetName);

            if (entity is null)
            {
                entity = new DbLabelSet { SetName = set.SetName, GameDiscId = discId, GameId = gameId, IsBuiltIn = false };
                _db.LabelSets.Add(entity);
                await _db.SaveChangesAsync();
            }

            _db.Labels.RemoveRange(entity.Labels);
            await _db.SaveChangesAsync();

            var roots    = set.Entries.Where(e => e.ParentAddress == null).ToList();
            var children = set.Entries.Where(e => e.ParentAddress != null).ToList();

            var addressToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                var dbLabel = DbMapper.ToDbLabel(root, entity.Id);
                _db.Labels.Add(dbLabel);
                await _db.SaveChangesAsync();
                addressToId[root.Address] = dbLabel.Id;
            }

            var remaining = new Queue<MgrEntry>(children);
            int maxPasses = children.Count + 1;
            while (remaining.Count > 0 && maxPasses-- > 0)
            {
                var deferred = new List<MgrEntry>();
                while (remaining.Count > 0)
                {
                    var child = remaining.Dequeue();
                    if (child.ParentAddress != null && addressToId.TryGetValue(child.ParentAddress, out int parentId))
                    {
                        var dbChild = DbMapper.ToDbLabel(child, entity.Id, parentId);
                        _db.Labels.Add(dbChild);
                        await _db.SaveChangesAsync();
                        addressToId[child.Address] = dbChild.Id;
                    }
                    else
                    {
                        deferred.Add(child);
                    }
                }
                foreach (var d in deferred) remaining.Enqueue(d);
            }

            var loaded = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .Include(ls => ls.GameDisc)
                                  .FirstAsync(ls => ls.Id == entity.Id);
            return DbMapper.ToMgrSet(loaded);
        }

        /// <summary>Inserts or updates a single label entry within the named set.</summary>
        internal async Task<MgrSet> AddOrUpdateEntryAsync(string gameId, string setName, MgrEntry entry)
        {
            var discId = await ResolveGameDiscIdAsync(gameId);
            if (discId is null)
                throw new InvalidOperationException($"No GameDisc found for '{gameId}'.");

            var entity = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .Include(ls => ls.GameDisc)
                                  .FirstOrDefaultAsync(ls => ls.GameDiscId == discId && ls.SetName == setName);

            if (entity is null)
            {
                entity = new DbLabelSet { SetName = setName, GameDiscId = discId, GameId = gameId, IsBuiltIn = false };
                _db.LabelSets.Add(entity);
                await _db.SaveChangesAsync();
            }

            int? parentId = null;
            if (entry.ParentAddress != null)
            {
                var normParent = NormalizeAddress(entry.ParentAddress);
                var parent = entity.Labels.FirstOrDefault(l =>
                    NormalizeAddress(l.Address).Equals(normParent, StringComparison.OrdinalIgnoreCase));
                if (parent is null)
                    return DbMapper.ToMgrSet(entity);
                parentId = parent.Id;
            }

            var norm     = NormalizeAddress(entry.Address);
            var existing = entity.Labels.FirstOrDefault(l =>
                NormalizeAddress(l.Address).Equals(norm, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                existing.Name           = entry.Name;
                existing.Comment        = entry.Comment;
                existing.ValueType      = entry.ValueType;
                existing.Hidden         = entry.Hidden;
                existing.Dynamic        = entry.Dynamic;
                existing.Type           = DbMapper.ToDbLabelType(entry.Type);
                existing.ParentId       = parentId;
                existing.Offset         = entry.Offset;
                existing.ArgRegsJson    = entry.ArgRegs    is { Count: > 0 } ? System.Text.Json.JsonSerializer.Serialize(entry.ArgRegs)    : null;
                existing.ReturnRegsJson = entry.ReturnRegs is { Count: > 0 } ? System.Text.Json.JsonSerializer.Serialize(entry.ReturnRegs) : null;
                existing.ElementStride  = entry.ElementStride;
                existing.ElementCount   = entry.ElementCount;
                existing.NodeNextOffset = entry.NodeNextOffset;
                existing.NodeSize       = entry.NodeSize;
                await _db.SaveChangesAsync();
            }
            else
            {
                var newLabel = DbMapper.ToDbLabel(entry, entity.Id, parentId);
                _db.Labels.Add(newLabel);
                await _db.SaveChangesAsync();
            }

            var loaded = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .Include(ls => ls.GameDisc)
                                  .FirstAsync(ls => ls.Id == entity.Id);
            return DbMapper.ToMgrSet(loaded);
        }

        /// <summary>
        /// Batch add-or-update multiple label entries in a single set, saving changes
        /// once at the end for efficiency.
        /// </summary>
        internal async Task<MgrSet> AddOrUpdateEntriesAsync(string gameId, string setName, IReadOnlyList<MgrEntry> entries)
        {
            if (entries.Count == 0)
                throw new ArgumentException("entries must not be empty", nameof(entries));

            var discId = await ResolveGameDiscIdAsync(gameId);
            if (discId is null)
                throw new InvalidOperationException($"No GameDisc found for '{gameId}'.");

            var entity = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .Include(ls => ls.GameDisc)
                                  .FirstOrDefaultAsync(ls => ls.GameDiscId == discId && ls.SetName == setName);

            if (entity is null)
            {
                entity = new DbLabelSet { SetName = setName, GameDiscId = discId, GameId = gameId, IsBuiltIn = false };
                _db.LabelSets.Add(entity);
                await _db.SaveChangesAsync();
                // Re-load so entity.Labels is tracked.
                entity = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .Include(ls => ls.GameDisc)
                                  .FirstAsync(ls => ls.Id == entity.Id);
            }

            foreach (var entry in entries)
            {
                int? parentId = null;
                if (entry.ParentAddress != null)
                {
                    var normParent = NormalizeAddress(entry.ParentAddress);
                    var parent = entity.Labels.FirstOrDefault(l =>
                        NormalizeAddress(l.Address).Equals(normParent, StringComparison.OrdinalIgnoreCase));
                    if (parent is null)
                    {
                        Console.WriteLine($"[LabelRepo] Skipped entry '{entry.Name}' at {entry.Address}: parent {entry.ParentAddress} not found in set '{setName}'.");
                        continue;
                    }
                    parentId = parent.Id;
                }

                var norm = NormalizeAddress(entry.Address);
                var existing = entity.Labels.FirstOrDefault(l =>
                    NormalizeAddress(l.Address).Equals(norm, StringComparison.OrdinalIgnoreCase));

                if (existing is not null)
                {
                    existing.Name           = entry.Name;
                    existing.Comment        = entry.Comment;
                    existing.ValueType      = entry.ValueType;
                    existing.Hidden         = entry.Hidden;
                    existing.Dynamic        = entry.Dynamic;
                    existing.Type           = DbMapper.ToDbLabelType(entry.Type);
                    existing.ParentId       = parentId;
                    existing.Offset         = entry.Offset;
                    existing.ArgRegsJson    = entry.ArgRegs    is { Count: > 0 } ? System.Text.Json.JsonSerializer.Serialize(entry.ArgRegs)    : null;
                    existing.ReturnRegsJson = entry.ReturnRegs is { Count: > 0 } ? System.Text.Json.JsonSerializer.Serialize(entry.ReturnRegs) : null;
                    existing.ElementStride  = entry.ElementStride;
                    existing.ElementCount   = entry.ElementCount;
                    existing.NodeNextOffset = entry.NodeNextOffset;
                    existing.NodeSize       = entry.NodeSize;
                }
                else
                {
                    var newLabel = DbMapper.ToDbLabel(entry, entity.Id, parentId);
                    _db.Labels.Add(newLabel);
                    // Add to entity.Labels so subsequent entries in the batch can
                    // reference this one as a parent by address.
                    entity.Labels.Add(newLabel);
                }
            }

            await _db.SaveChangesAsync();

            var loaded = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .Include(ls => ls.GameDisc)
                                  .FirstAsync(ls => ls.Id == entity.Id);
            return DbMapper.ToMgrSet(loaded);
        }

        /// <summary>Deletes a single label entry by address. Children are cascade-deleted.</summary>
        internal async Task<MgrSet> DeleteEntryAsync(string gameId, string setName, string address)
        {
            var discId = await ResolveGameDiscIdAsync(gameId);
            if (discId is null)
                throw new InvalidOperationException($"No GameDisc found for '{gameId}'.");

            var entity = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .Include(ls => ls.GameDisc)
                                  .FirstOrDefaultAsync(ls => ls.GameDiscId == discId && ls.SetName == setName)
                         ?? throw new InvalidOperationException($"Label set '{setName}' not found.");

            var norm     = NormalizeAddress(address);
            var toRemove = entity.Labels
                                 .Where(l => NormalizeAddress(l.Address)
                                                 .Equals(norm, StringComparison.OrdinalIgnoreCase))
                                 .ToList();

            _db.Labels.RemoveRange(toRemove);
            await _db.SaveChangesAsync();

            var loaded = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .Include(ls => ls.GameDisc)
                                  .FirstAsync(ls => ls.Id == entity.Id);
            return DbMapper.ToMgrSet(loaded);
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private static string NormalizeAddress(string raw)
        {
            if (raw.Contains(':'))
            {
                int colon = raw.IndexOf(':');
                return NormalizeAddress(raw[..colon]) + raw[colon..];
            }
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                raw = raw[2..];
            if (uint.TryParse(raw, System.Globalization.NumberStyles.HexNumber, null, out uint v))
                return $"0x{v:X8}";
            return raw;
        }
    }
}
