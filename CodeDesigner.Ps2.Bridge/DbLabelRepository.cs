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
    /// </summary>
    internal sealed class DbLabelRepository
    {
        private readonly CodeDesignerDbContext _db;

        internal DbLabelRepository(CodeDesignerDbContext db) => _db = db;

        // ── Query helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns the names of all user-defined label sets stored in the DB for <paramref name="gameId"/>.
        /// Built-in sets are excluded.
        /// </summary>
        internal async Task<IReadOnlyList<string>> GetSetNamesAsync(string gameId)
        {
            return await _db.LabelSets
                            .Where(ls => ls.GameId == gameId && !ls.IsBuiltIn)
                            .OrderBy(ls => ls.SetName)
                            .Select(ls => ls.SetName)
                            .ToListAsync();
        }

        /// <summary>
        /// Loads a label set by name. Returns <c>null</c> when not found.
        /// </summary>
        internal async Task<MgrSet?> LoadSetAsync(string gameId, string setName)
        {
            var entity = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                      .ThenInclude(l => l.Members)
                                  .FirstOrDefaultAsync(ls => ls.GameId == gameId && ls.SetName == setName);

            return entity is null ? null : DbMapper.ToMgrSet(entity);
        }

        // ── Mutating operations ────────────────────────────────────────────────

        /// <summary>
        /// Creates a new empty label set in the DB.
        /// Throws <see cref="InvalidOperationException"/> when the set already exists.
        /// </summary>
        internal async Task<MgrSet> CreateSetAsync(string gameId, string setName)
        {
            bool exists = await _db.LabelSets
                                   .AnyAsync(ls => ls.GameId == gameId && ls.SetName == setName);
            if (exists)
                throw new InvalidOperationException($"Label set '{setName}' already exists for game '{gameId}'.");

            var entity = new DbLabelSet
            {
                SetName   = setName,
                GameId    = gameId,
                IsBuiltIn = false,
            };

            _db.LabelSets.Add(entity);
            await _db.SaveChangesAsync();

            return DbMapper.ToMgrSet(entity);
        }

        /// <summary>
        /// Deletes a label set (and all its labels/members via cascade) from the DB.
        /// No-op when the set does not exist.
        /// </summary>
        internal async Task DeleteSetAsync(string gameId, string setName)
        {
            var entity = await _db.LabelSets
                                  .FirstOrDefaultAsync(ls => ls.GameId == gameId && ls.SetName == setName);
            if (entity is null) return;

            _db.LabelSets.Remove(entity);
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Upserts a full label set: creates the <see cref="DbLabelSet"/> row if missing,
        /// then replaces all its <see cref="DbLabel"/> rows with the entries supplied in
        /// <paramref name="set"/>.  Returns the updated set as a <see cref="MgrSet"/>.
        /// </summary>
        internal async Task<MgrSet> SaveSetAsync(MgrSet set, string gameId)
        {
            var entity = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                      .ThenInclude(l => l.Members)
                                  .FirstOrDefaultAsync(ls => ls.GameId == gameId && ls.SetName == set.SetName);

            if (entity is null)
            {
                entity = DbMapper.ToDbLabelSet(set, gameId);
                _db.LabelSets.Add(entity);
                await _db.SaveChangesAsync(); // obtain entity.Id
            }

            // Replace all labels — remove old, add new in a single transaction.
            _db.Labels.RemoveRange(entity.Labels);

            foreach (var entry in set.Entries)
            {
                var dbLabel = DbMapper.ToDbLabel(entry, entity.Id);
                _db.Labels.Add(dbLabel);
            }

            await _db.SaveChangesAsync();

            // Reload to return a fully hydrated set.
            var loaded = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                      .ThenInclude(l => l.Members)
                                  .FirstAsync(ls => ls.Id == entity.Id);

            return DbMapper.ToMgrSet(loaded);
        }

        /// <summary>
        /// Inserts or updates a single label entry within the named set.
        /// Creates the set if it does not already exist.
        /// </summary>
        internal async Task<MgrSet> AddOrUpdateEntryAsync(string gameId, string setName, MgrEntry entry)
        {
            var entity = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                      .ThenInclude(l => l.Members)
                                  .FirstOrDefaultAsync(ls => ls.GameId == gameId && ls.SetName == setName);

            if (entity is null)
            {
                entity = new DbLabelSet { SetName = setName, GameId = gameId, IsBuiltIn = false };
                _db.LabelSets.Add(entity);
                await _db.SaveChangesAsync();
            }

            var norm     = NormalizeAddress(entry.Address);
            var existing = entity.Labels.FirstOrDefault(l =>
                NormalizeAddress(l.Address)
                    .Equals(norm, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                _db.LabelMembers.RemoveRange(existing.Members);
                _db.Labels.Remove(existing);
                await _db.SaveChangesAsync();
            }

            var newLabel = DbMapper.ToDbLabel(entry, entity.Id);
            _db.Labels.Add(newLabel);
            await _db.SaveChangesAsync();

            var loaded = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                      .ThenInclude(l => l.Members)
                                  .FirstAsync(ls => ls.Id == entity.Id);

            return DbMapper.ToMgrSet(loaded);
        }

        /// <summary>
        /// Deletes a single label entry by address from the named set.
        /// Throws <see cref="InvalidOperationException"/> when the set does not exist.
        /// </summary>
        internal async Task<MgrSet> DeleteEntryAsync(string gameId, string setName, string address)
        {
            var entity = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                      .ThenInclude(l => l.Members)
                                  .FirstOrDefaultAsync(ls => ls.GameId == gameId && ls.SetName == setName)
                         ?? throw new InvalidOperationException($"Label set '{setName}' not found for game '{gameId}'.");

            var norm    = NormalizeAddress(address);
            var toRemove = entity.Labels
                                 .Where(l => NormalizeAddress(l.Address)
                                                 .Equals(norm, StringComparison.OrdinalIgnoreCase))
                                 .ToList();

            foreach (var l in toRemove)
            {
                _db.LabelMembers.RemoveRange(l.Members);
                _db.Labels.Remove(l);
            }

            await _db.SaveChangesAsync();

            var loaded = await _db.LabelSets
                                  .Include(ls => ls.Labels)
                                      .ThenInclude(l => l.Members)
                                  .FirstAsync(ls => ls.Id == entity.Id);

            return DbMapper.ToMgrSet(loaded);
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>Normalises an address string to canonical "0xXXXXXXXX" form.</summary>
        private static string NormalizeAddress(string raw)
        {
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                raw = raw[2..];
            if (uint.TryParse(raw, System.Globalization.NumberStyles.HexNumber, null, out uint v))
                return $"0x{v:X8}";
            return raw;
        }
    }
}
