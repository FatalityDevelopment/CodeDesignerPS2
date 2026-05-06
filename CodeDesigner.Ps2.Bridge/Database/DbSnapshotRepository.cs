using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Bridge.Database
{
    /// <summary>
    /// Async persistence layer for snapshot metadata, backed by <see cref="CodeDesignerDbContext"/>.
    /// The snapshot binary files themselves remain on disk; the DB stores the metadata
    /// (title, path, game association) so they can be listed and looked up efficiently.
    /// </summary>
    internal sealed class DbSnapshotRepository
    {
        private readonly CodeDesignerDbContext _db;

        internal DbSnapshotRepository(CodeDesignerDbContext db) => _db = db;

        /// <summary>
        /// Returns all snapshots for the game identified by <paramref name="isoName"/>
        /// (matched against <see cref="GameDisc.IsoPath"/> endings or <see cref="Game.Title"/>).
        /// Returns an empty list when no matching game is found.
        /// </summary>
        internal async Task<List<Snapshot>> GetSnapshotsForGameAsync(string isoName)
        {
            var gameId = await ResolveGameIdAsync(isoName);
            if (gameId is null) return [];

            return await _db.Snapshots
                            .Where(s => s.GameId == gameId)
                            .OrderByDescending(s => s.SnapshotDateTime)
                            .ToListAsync();
        }

        /// <summary>
        /// Persists a new snapshot record.  If a record with the same
        /// <paramref name="snapshotPath"/> already exists the call is a no-op.
        /// </summary>
        internal async Task AddSnapshotAsync(string snapshotPath, string title, string? description, string? isoName)
        {
            bool exists = await _db.Snapshots.AnyAsync(s => s.SnapshotPath == snapshotPath);
            if (exists) return;

            int? gameId = null;
            if (!string.IsNullOrWhiteSpace(isoName))
                gameId = await ResolveGameIdAsync(isoName);

            _db.Snapshots.Add(new Snapshot
            {
                Title            = title,
                Description      = description,
                SnapshotPath     = snapshotPath,
                SnapshotDateTime = DateTime.UtcNow,
                GameId           = gameId,
            });

            await _db.SaveChangesAsync();
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Tries to find a <see cref="Game.Id"/> whose associated <see cref="GameDisc.IsoPath"/>
        /// ends with <paramref name="isoName"/> (case-insensitive).
        /// Falls back to matching <see cref="Game.Title"/>.
        /// Returns <c>null</c> when no match is found.
        /// </summary>
        private async Task<int?> ResolveGameIdAsync(string isoName)
        {
            var lowerName = isoName.ToLowerInvariant();

            // Primary: match by IsoPath suffix (e.g. "GoW.iso" or just "GoW").
            var disc = await _db.GameDiscs
                                .FirstOrDefaultAsync(d =>
                                    d.IsoPath != null &&
                                    (d.IsoPath.ToLower().EndsWith(lowerName) ||
                                     d.IsoPath.ToLower().EndsWith(lowerName + ".iso")));

            if (disc is not null) return disc.GameId;

            // Fallback: match by Game.Title.
            var game = await _db.Games
                                .FirstOrDefaultAsync(g =>
                                    g.Title.ToLower() == lowerName);

            return game?.Id;
        }
    }
}
