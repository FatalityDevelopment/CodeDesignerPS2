using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Async persistence layer for application settings, backed by <see cref="CodeDesignerDbContext"/>.
    /// Settings are stored as key/value pairs in the <see cref="Setting"/> table.
    /// </summary>
    internal sealed class DbSettingsRepository
    {
        private readonly CodeDesignerDbContext _db;

        internal DbSettingsRepository(CodeDesignerDbContext db) => _db = db;

        /// <summary>Returns the value for <paramref name="key"/>, or <c>null</c> when not found.</summary>
        internal async Task<string?> GetAsync(string key)
        {
            var setting = await _db.Settings.FindAsync(key);
            return setting?.Value;
        }

        /// <summary>
        /// Returns all settings whose key starts with <paramref name="prefix"/>.
        /// When <paramref name="prefix"/> is null or empty every setting is returned.
        /// </summary>
        internal async Task<Dictionary<string, string?>> GetAllAsync(string? prefix = null)
        {
            var query = _db.Settings.AsQueryable();

            if (!string.IsNullOrEmpty(prefix))
                query = query.Where(s => s.Key.StartsWith(prefix));

            return await query.ToDictionaryAsync(s => s.Key, s => s.Value);
        }

        /// <summary>Inserts or updates a setting.</summary>
        internal async Task SetAsync(string key, string? value, string? description = null)
        {
            var existing = await _db.Settings.FindAsync(key);
            if (existing is null)
            {
                _db.Settings.Add(new Setting
                {
                    Key         = key,
                    Value       = value,
                    Description = description,
                    UpdatedAt   = DateTime.UtcNow,
                });
            }
            else
            {
                existing.Value     = value;
                existing.UpdatedAt = DateTime.UtcNow;
                if (description is not null)
                    existing.Description = description;
            }

            await _db.SaveChangesAsync();
        }

        /// <summary>Deletes the setting with the given key. No-op when not found.</summary>
        internal async Task DeleteAsync(string key)
        {
            var existing = await _db.Settings.FindAsync(key);
            if (existing is null) return;
            _db.Settings.Remove(existing);
            await _db.SaveChangesAsync();
        }
    }
}
