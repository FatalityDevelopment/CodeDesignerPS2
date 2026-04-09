using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CodeDesigner.Ps2.Database;
using Microsoft.EntityFrameworkCore;

using MgrEntry = CodeDesigner.Ps2.LabelManager.Models.LabelEntry;
using MgrSet   = CodeDesigner.Ps2.LabelManager.Models.LabelSet;

using DbLabel    = CodeDesigner.Ps2.Database.Entities.Label;
using DbLabelSet = CodeDesigner.Ps2.Database.Entities.LabelSet;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Async persistence layer for label sets, backed by <see cref="CodeDesignerDbContext"/>.
    /// All methods accept and return <c>CodeDesigner.Ps2.LabelManager</c> model types so
    /// that handler code never touches database entity types directly — <see cref="DbMapper"/>
    /// handles all conversions internally.
    ///
    /// Labels are recursive: child labels have <see cref="DbLabel.ParentId"/> set and
    /// a synthetic path address (e.g. "0x80010000:+0x0010").
    /// </summary>
    internal sealed class DbLabelRepository
    {
        private readonly CodeDesignerDbContext _db;

        internal DbLabelRepository(CodeDesignerDbContext db) => _db = db;

        // ── GameId resolution ──────────────────────────────────────────────────

        /// <summary>
        /// Resolves an arbitrary game identifier to the string representation of
        /// <see cref="CodeDesigner.Ps2.Database.Entities.Game.Id"/> stored in
        /// <see cref="CodeDesigner.Ps2.Database.Entities.LabelSet.GameId"/>.
        ///
        /// Resolution order:
        /// <list type="number">
        ///   <item>Integer string (direct Games.Id) — returned as-is.</item>
        ///   <item>PS2 serial (e.g. "SCUS-97130" or "SCUS_97130") — normalised to
        ///         dash form, matched against GameDiscs.Serial.</item>
        ///   <item>IsoPath suffix — matched against GameDiscs.IsoPath ending.</item>
        ///   <item>Game.Title exact match.</item>
        ///   <item>Fallback — input returned as-is (backward compat).</item>
        /// </list>
        /// </summary>
        internal async Task<string> ResolveGameIdAsync(string gameId)
        {
            // 1. Already a numeric Games.Id.
            if (int.TryParse(gameId.Trim(), out _))
                return gameId.Trim();

            // 2. Serial: normalise (uppercase + replace _ with -) and look up GameDiscs.
            var normalizedSerial = NormalizeSerial(gameId);
            var discBySerial = await _db.GameDiscs
                                        .FirstOrDefaultAsync(d =>
                                            d.Serial != null &&
                                            d.Serial.Replace("_", "-").ToUpperInvariant() == normalizedSerial);
            if (discBySerial is not null)
                return discBySerial.GameId.ToString();

            // 3. IsoPath suffix (covers "GoW", "GoW.iso", full path).
            var lower = gameId.ToLowerInvariant().TrimEnd('/').TrimEnd('\\');
            var discByPath = await _db.GameDiscs
                                      .FirstOrDefaultAsync(d =>
                                          d.IsoPath != null &&
                                          (d.IsoPath.ToLower().EndsWith(lower) ||
                                           d.IsoPath.ToLower().EndsWith(lower + ".iso")));
            if (discByPath is not null)
                return discByPath.GameId.ToString();

            // 4. Game.Title exact match.
            var gameByTitle = await _db.Games
                                       .FirstOrDefaultAsync(g => g.Title.ToLower() == lower);
            if (gameByTitle is not null)
                return gameByTitle.Id.ToString();

            // 5. Fall back — use as-is (preserves backward compatibility).
            return gameId;
        }

        /// <summary>
        /// Normalises a PS2 serial to uppercase dash form, e.g.
        /// "scus_97130" → "SCUS-97130".
        /// </summary>
        internal static string NormalizeSerial(string serial) =>
            serial.Trim().ToUpperInvariant().Replace('_', '-');

        // ── Query helpers ──────────────────────────────────────────────────────

        internal async Task<IReadOnlyList<string>> GetSetNamesAsync(string gameId)
        {
            var resolvedId = await ResolveGameIdAsync(gameId);
            return await _db.LabelSets
                            .Where(ls => ls.GameId == resolvedId && !ls.IsBuiltIn)
                            .OrderBy(ls => ls.SetName)
                            .Select(ls => ls.SetName)
                            .ToListAsync();
        }

        internal async Task<MgrSet?> LoadSetAsync(string gameId, string setName)
        {
            var resolvedId = await ResolveGameIdAsync(gameId);
            var entity = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .FirstOrDefaultAsync(ls => ls.GameId == resolvedId && ls.SetName == setName);

            return entity is null ? null : DbMapper.ToMgrSet(entity);
        }

        // ── Mutating operations ────────────────────────────────────────────────

        internal async Task<MgrSet> CreateSetAsync(string gameId, string setName)
        {
            var resolvedId = await ResolveGameIdAsync(gameId);
            bool exists = await _db.LabelSets
                                   .AnyAsync(ls => ls.GameId == resolvedId && ls.SetName == setName);
            if (exists)
                throw new InvalidOperationException($"Label set '{setName}' already exists for game '{resolvedId}'.");

            var entity = new DbLabelSet
            {
                SetName   = setName,
                GameId    = resolvedId,
                IsBuiltIn = false,
            };

            _db.LabelSets.Add(entity);
            await _db.SaveChangesAsync();

            return DbMapper.ToMgrSet(entity);
        }

        internal async Task DeleteSetAsync(string gameId, string setName)
        {
            var resolvedId = await ResolveGameIdAsync(gameId);
            var entity = await _db.LabelSets
                                  .FirstOrDefaultAsync(ls => ls.GameId == resolvedId && ls.SetName == setName);
            if (entity is null) return;

            _db.LabelSets.Remove(entity);
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Upserts a full label set: replaces all its <see cref="DbLabel"/> rows with the
        /// entries supplied in <paramref name="set"/>.
        /// Entries are inserted in two passes: roots first (to get their IDs), then
        /// children (to set <see cref="DbLabel.ParentId"/>).
        /// </summary>
        internal async Task<MgrSet> SaveSetAsync(MgrSet set, string gameId)
        {
            var entity = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .FirstOrDefaultAsync(ls => ls.GameId == gameId && ls.SetName == set.SetName);

            if (entity is null)
            {
                entity = DbMapper.ToDbLabelSet(set, gameId);
                _db.LabelSets.Add(entity);
                await _db.SaveChangesAsync();
            }

            // Remove all existing labels (cascade deletes children automatically).
            _db.Labels.RemoveRange(entity.Labels);
            await _db.SaveChangesAsync();

            // Partition: roots vs children (any depth).
            var roots    = set.Entries.Where(e => e.ParentAddress == null).ToList();
            var children = set.Entries.Where(e => e.ParentAddress != null).ToList();

            // Insert roots, collect address → id mapping for parent resolution.
            var addressToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                var dbLabel = DbMapper.ToDbLabel(root, entity.Id);
                _db.Labels.Add(dbLabel);
                await _db.SaveChangesAsync();
                addressToId[root.Address] = dbLabel.Id;
            }

            // Insert children depth-by-depth until all are resolved.
            // (BFS over multiple rounds handles arbitrary nesting depth.)
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
                        deferred.Add(child); // parent not yet inserted
                    }
                }
                foreach (var d in deferred) remaining.Enqueue(d);
            }

            // Reload fully.
            var loaded = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .FirstAsync(ls => ls.Id == entity.Id);
            return DbMapper.ToMgrSet(loaded);
        }

        /// <summary>
        /// Inserts or updates a single label entry within the named set.
        /// For child entries, the parent is located by <see cref="MgrEntry.ParentAddress"/>.
        /// </summary>
        internal async Task<MgrSet> AddOrUpdateEntryAsync(string gameId, string setName, MgrEntry entry)
        {
            var entity = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .FirstOrDefaultAsync(ls => ls.GameId == gameId && ls.SetName == setName);

            if (entity is null)
            {
                entity = new DbLabelSet { SetName = setName, GameId = gameId, IsBuiltIn = false };
                _db.LabelSets.Add(entity);
                await _db.SaveChangesAsync();
            }

            // Resolve parent (for child entries).
            int? parentId = null;
            if (entry.ParentAddress != null)
            {
                var normParent = NormalizeAddress(entry.ParentAddress);
                var parent = entity.Labels.FirstOrDefault(l =>
                    NormalizeAddress(l.Address).Equals(normParent, StringComparison.OrdinalIgnoreCase));
                if (parent is null)
                    return DbMapper.ToMgrSet(entity); // parent not found – ignore silently

                parentId = parent.Id;
            }

            var norm     = NormalizeAddress(entry.Address);
            var existing = entity.Labels.FirstOrDefault(l =>
                NormalizeAddress(l.Address).Equals(norm, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                // Update in-place to preserve insertion order.
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
                                  .FirstAsync(ls => ls.Id == entity.Id);
            return DbMapper.ToMgrSet(loaded);
        }

        /// <summary>
        /// Deletes a single label entry by address from the named set.
        /// Children of the deleted label are removed automatically via cascade.
        /// </summary>
        internal async Task<MgrSet> DeleteEntryAsync(string gameId, string setName, string address)
        {
            var entity = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .FirstOrDefaultAsync(ls => ls.GameId == gameId && ls.SetName == setName)
                         ?? throw new InvalidOperationException($"Label set '{setName}' not found for game '{gameId}'.");

            var norm     = NormalizeAddress(address);
            var toRemove = entity.Labels
                                 .Where(l => NormalizeAddress(l.Address)
                                                 .Equals(norm, StringComparison.OrdinalIgnoreCase))
                                 .ToList();

            _db.Labels.RemoveRange(toRemove);
            await _db.SaveChangesAsync();

            var loaded = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                  .FirstAsync(ls => ls.Id == entity.Id);
            return DbMapper.ToMgrSet(loaded);
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private static string NormalizeAddress(string raw)
        {
            // Synthetic child addresses (e.g. "0x80010000:+0x0010") are returned as-is
            // after normalising the real-address prefix.
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
