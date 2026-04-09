using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Async persistence layer for MCP Memory Manager entries, backed by <see cref="CodeDesignerDbContext"/>.
    /// Entries are stored as Markdown content in the <see cref="McpMemory"/> table.
    /// </summary>
    internal sealed class DbMcpMemoryRepository
    {
        private readonly CodeDesignerDbContext _db;

        internal DbMcpMemoryRepository(CodeDesignerDbContext db) => _db = db;

        /// <summary>
        /// Returns all memory entries, optionally filtered by <paramref name="gameId"/>.
        /// When <paramref name="gameId"/> is null or empty, all entries are returned.
        /// </summary>
        internal async Task<List<McpMemory>> ListAsync(string? gameId = null)
        {
            var query = _db.McpMemories.AsQueryable();

            if (!string.IsNullOrWhiteSpace(gameId))
                query = query.Where(m => m.GameId == gameId);

            return await query.OrderBy(m => m.Name).ToListAsync();
        }

        /// <summary>Returns the entry with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
        internal async Task<McpMemory?> GetAsync(int id)
            => await _db.McpMemories.FindAsync(id);

        /// <summary>Returns the entry with the given slug <paramref name="name"/>, or <c>null</c> if not found.</summary>
        internal async Task<McpMemory?> FindByNameAsync(string name)
            => await _db.McpMemories.FirstOrDefaultAsync(m => m.Name == name);

        /// <summary>
        /// Creates or overwrites a memory entry by slug name.
        /// This is an atomic upsert used by the Bridge socket's MCPMEMORY_UPSERT command
        /// so all MCP agent writes are serialized through the Bridge's shared
        /// <see cref="CodeDesignerDbContext"/>.
        /// </summary>
        internal async Task<McpMemory> UpsertAsync(string name, string content, string? gameId = null)
        {
            var existing = await _db.McpMemories.FirstOrDefaultAsync(m => m.Name == name);
            if (existing is null)
            {
                var entry = new McpMemory
                {
                    Name      = name,
                    Content   = content,
                    GameId    = string.IsNullOrWhiteSpace(gameId) ? null : gameId.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                _db.McpMemories.Add(entry);
                await _db.SaveChangesAsync();
                return entry;
            }

            existing.Content   = content;
            existing.UpdatedAt = DateTime.UtcNow;
            if (gameId is not null)
                existing.GameId = string.IsNullOrWhiteSpace(gameId) ? null : gameId.Trim();
            await _db.SaveChangesAsync();
            return existing;
        }

        /// <summary>
        /// Creates a new memory entry.
        /// Throws <see cref="InvalidOperationException"/> when a entry with the same
        /// <paramref name="name"/> already exists.
        /// </summary>
        internal async Task<McpMemory> AddAsync(string name, string content, string? gameId = null)
        {
            bool exists = await _db.McpMemories.AnyAsync(m => m.Name == name);
            if (exists)
                throw new InvalidOperationException($"A memory entry named '{name}' already exists.");

            var entry = new McpMemory
            {
                Name      = name,
                Content   = content,
                GameId    = string.IsNullOrWhiteSpace(gameId) ? null : gameId.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            _db.McpMemories.Add(entry);
            await _db.SaveChangesAsync();
            return entry;
        }

        /// <summary>
        /// Updates an existing memory entry's name and/or content.
        /// Pass <c>null</c> to leave a field unchanged.
        /// Throws <see cref="KeyNotFoundException"/> when the entry does not exist.
        /// </summary>
        internal async Task<McpMemory> UpdateAsync(int id, string? name = null, string? content = null, string? gameId = null)
        {
            var entry = await _db.McpMemories.FindAsync(id)
                ?? throw new KeyNotFoundException($"Memory entry {id} not found.");

            if (name is not null)
            {
                // Ensure the new name is not taken by another entry.
                bool taken = await _db.McpMemories.AnyAsync(m => m.Name == name && m.Id != id);
                if (taken)
                    throw new InvalidOperationException($"A memory entry named '{name}' already exists.");

                entry.Name = name;
            }

            if (content is not null)
                entry.Content = content;

            if (gameId is not null)
                entry.GameId = string.IsNullOrWhiteSpace(gameId) ? null : gameId.Trim();

            entry.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return entry;
        }

        /// <summary>
        /// Deletes the entry with the given <paramref name="id"/>.
        /// No-op when not found.
        /// </summary>
        internal async Task DeleteAsync(int id)
        {
            var entry = await _db.McpMemories.FindAsync(id);
            if (entry is null) return;
            _db.McpMemories.Remove(entry);
            await _db.SaveChangesAsync();
        }

        // ── Import / export ────────────────────────────────────────────────────

        /// <summary>
        /// Imports a Markdown file from disk.
        /// The file name (without extension) is used as the entry name unless
        /// a <c>name</c> YAML front-matter key is present.
        /// If an entry with the derived name already exists, it is updated.
        /// </summary>
        internal async Task<McpMemory> ImportFromFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            string raw = await File.ReadAllTextAsync(filePath, Encoding.UTF8);

            // Parse optional YAML front-matter block: --- ... ---
            string? parsedName    = null;
            string? parsedGameId  = null;
            string body           = raw;

            if (raw.StartsWith("---", StringComparison.Ordinal))
            {
                int end = raw.IndexOf("\n---", 3, StringComparison.Ordinal);
                if (end > 0)
                {
                    string front = raw.Substring(3, end - 3);
                    body = raw.Substring(end + 4).TrimStart('\n', '\r');

                    foreach (var line in front.Split('\n'))
                    {
                        var colon = line.IndexOf(':');
                        if (colon < 0) continue;
                        var key = line.Substring(0, colon).Trim().ToLowerInvariant();
                        var val = line.Substring(colon + 1).Trim().Trim('"', '\'');
                        if (key == "name")   parsedName   = val;
                        if (key == "gameid") parsedGameId = val;
                    }
                }
            }

            string entryName = parsedName ?? Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(entryName))
                entryName = "Imported";

            var existing = await _db.McpMemories.FirstOrDefaultAsync(m => m.Name == entryName);
            if (existing is not null)
            {
                existing.Content   = body;
                existing.UpdatedAt = DateTime.UtcNow;
                if (parsedGameId is not null)
                    existing.GameId = string.IsNullOrWhiteSpace(parsedGameId) ? null : parsedGameId;
                await _db.SaveChangesAsync();
                return existing;
            }

            return await AddAsync(entryName, body, parsedGameId);
        }

        /// <summary>
        /// Exports a memory entry to a Markdown file.
        /// YAML front-matter is prepended with name and optional gameId.
        /// </summary>
        internal async Task ExportToFileAsync(int id, string outputPath)
        {
            var entry = await _db.McpMemories.FindAsync(id)
                ?? throw new KeyNotFoundException($"Memory entry {id} not found.");

            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine($"name: \"{entry.Name}\"");
            if (!string.IsNullOrWhiteSpace(entry.GameId))
                sb.AppendLine($"gameId: \"{entry.GameId}\"");
            sb.AppendLine($"createdAt: \"{entry.CreatedAt:O}\"");
            sb.AppendLine($"updatedAt: \"{entry.UpdatedAt:O}\"");
            sb.AppendLine("---");
            sb.AppendLine();
            sb.Append(entry.Content);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
        }
    }
}
