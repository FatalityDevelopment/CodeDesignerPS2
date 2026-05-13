using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Bridge.Repositories
{
    /// <summary>
    /// Async persistence layer for <see cref="DataFormatOverride"/> rows.
    ///
    /// <para>
    /// Like <see cref="DbLabelRepository"/>, overrides are scoped to a specific
    /// <see cref="GameDisc"/>.  A <c>null</c> <c>gameId</c> falls back to a
    /// disc-less scope (used when no project is loaded — typically file-only
    /// disassembly), which is represented as <c>GameDiscId = null</c> in the
    /// table.
    /// </para>
    ///
    /// <para>
    /// Inserts are conflict-aware: <see cref="UpsertAsync"/> deletes any
    /// pre-existing rows whose byte ranges overlap the new span before saving,
    /// so the table can be read as a non-overlapping interval set.
    /// </para>
    /// </summary>
    internal sealed class DbDataFormatRepository
    {
        private readonly CodeDesignerDbContext _db;
        private readonly DbLabelRepository     _labelRepo;

        internal DbDataFormatRepository(CodeDesignerDbContext db, DbLabelRepository labelRepo)
        {
            _db        = db;
            _labelRepo = labelRepo;
        }

        // ── Byte size of each format kind ─────────────────────────────────────

        internal static int ByteSizeOf(DataFormatKind kind) => kind switch
        {
            DataFormatKind.Byte  => 1,
            DataFormatKind.Half  => 2,
            DataFormatKind.Word  => 4,
            DataFormatKind.DWord => 8,
            DataFormatKind.QWord => 16,
            _                    => 4,
        };

        // ── Queries ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns every override that intersects <c>[startAddress, endAddress)</c>
        /// for the disc identified by <paramref name="gameId"/>.  An empty/null
        /// game id returns disc-less overrides.
        /// </summary>
        internal async Task<IReadOnlyList<DataFormatOverride>> GetInRangeAsync(
            string? gameId, long startAddress, long endAddress)
        {
            int? discId = await ResolveDiscIdAsync(gameId);

            return await _db.DataFormatOverrides
                .Where(o => o.GameDiscId == discId)
                .Where(o => o.Address < endAddress)
                .Where(o => o.Address + o.Length > startAddress)
                .OrderBy(o => o.Address)
                .ToListAsync();
        }

        /// <summary>Returns every override for the disc, ordered by address.</summary>
        internal async Task<IReadOnlyList<DataFormatOverride>> GetAllAsync(string? gameId)
        {
            int? discId = await ResolveDiscIdAsync(gameId);

            return await _db.DataFormatOverrides
                .Where(o => o.GameDiscId == discId)
                .OrderBy(o => o.Address)
                .ToListAsync();
        }

        // ── Mutating ──────────────────────────────────────────────────────────

        /// <summary>
        /// Inserts a new override for <paramref name="gameId"/> spanning
        /// <paramref name="length"/> bytes starting at <paramref name="address"/>,
        /// rendered as <paramref name="kind"/>.  Any pre-existing overrides that
        /// overlap the new span are deleted first, so the resulting table
        /// continues to describe a non-overlapping interval set.
        /// </summary>
        internal async Task<DataFormatOverride> UpsertAsync(
            string? gameId, long address, int length, DataFormatKind kind)
        {
            if (length <= 0)
                throw new System.ArgumentException("length must be positive", nameof(length));

            int unit = ByteSizeOf(kind);
            if (length % unit != 0)
                throw new System.ArgumentException(
                    $"length ({length}) must be a multiple of {unit} for {kind}", nameof(length));

            int? discId = await ResolveDiscIdAsync(gameId);

            // Remove anything that touches the new span.
            long endExclusive = address + length;
            var overlapping = await _db.DataFormatOverrides
                .Where(o => o.GameDiscId == discId)
                .Where(o => o.Address < endExclusive)
                .Where(o => o.Address + o.Length > address)
                .ToListAsync();
            if (overlapping.Count > 0)
                _db.DataFormatOverrides.RemoveRange(overlapping);

            var entity = new DataFormatOverride
            {
                GameDiscId = discId,
                Address    = address,
                Length     = length,
                Kind       = kind,
                UpdatedAt  = System.DateTime.UtcNow,
            };
            _db.DataFormatOverrides.Add(entity);

            await _db.SaveChangesAsync();
            return entity;
        }

        /// <summary>
        /// Removes every override whose byte range intersects
        /// <c>[address, address + length)</c>.  Returns the number of rows
        /// deleted.
        /// </summary>
        internal async Task<int> ClearRangeAsync(string? gameId, long address, int length)
        {
            if (length <= 0) return 0;

            int? discId = await ResolveDiscIdAsync(gameId);
            long endExclusive = address + length;

            var overlapping = await _db.DataFormatOverrides
                .Where(o => o.GameDiscId == discId)
                .Where(o => o.Address < endExclusive)
                .Where(o => o.Address + o.Length > address)
                .ToListAsync();

            if (overlapping.Count == 0) return 0;

            _db.DataFormatOverrides.RemoveRange(overlapping);
            await _db.SaveChangesAsync();
            return overlapping.Count;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task<int?> ResolveDiscIdAsync(string? gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId)) return null;
            return await _labelRepo.ResolveGameDiscIdAsync(gameId);
        }
    }
}
