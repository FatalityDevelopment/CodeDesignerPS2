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
    /// Async persistence layer for MCP Orchestration profiles, backed by <see cref="CodeDesignerDbContext"/>.
    /// </summary>
    internal sealed class DbMcpOrchestrationRepository
    {
        private readonly CodeDesignerDbContext _db;

        internal DbMcpOrchestrationRepository(CodeDesignerDbContext db) => _db = db;

        /// <summary>Returns all orchestration profiles ordered by name.</summary>
        internal async Task<List<McpOrchestration>> ListAsync()
            => await _db.McpOrchestrations.OrderBy(o => o.Name).ToListAsync();

        /// <summary>Returns the profile with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
        internal async Task<McpOrchestration?> GetAsync(int id)
            => await _db.McpOrchestrations.FindAsync(id);

        /// <summary>Creates a new orchestration profile.</summary>
        internal async Task<McpOrchestration> AddAsync(
            string name,
            string? description,
            string? strategy,
            string? roles,
            string? workflow,
            string? rules,
            string? notes)
        {
            bool exists = await _db.McpOrchestrations.AnyAsync(o => o.Name == name);
            if (exists)
                throw new InvalidOperationException($"An orchestration profile named '{name}' already exists.");

            var profile = new McpOrchestration
            {
                Name        = name,
                Description = description,
                Strategy    = strategy,
                Roles       = roles,
                Workflow    = workflow,
                Rules       = rules,
                Notes       = notes,
                CreatedAt   = DateTime.UtcNow,
                UpdatedAt   = DateTime.UtcNow,
            };

            _db.McpOrchestrations.Add(profile);
            await _db.SaveChangesAsync();
            return profile;
        }

        /// <summary>
        /// Updates an existing orchestration profile.  Pass <c>null</c> to leave a field unchanged.
        /// </summary>
        internal async Task<McpOrchestration> UpdateAsync(
            int id,
            string? name,
            string? description,
            string? strategy,
            string? roles,
            string? workflow,
            string? rules,
            string? notes)
        {
            var profile = await _db.McpOrchestrations.FindAsync(id)
                ?? throw new KeyNotFoundException($"Orchestration profile {id} not found.");

            if (name is not null)
            {
                bool taken = await _db.McpOrchestrations.AnyAsync(o => o.Name == name && o.Id != id);
                if (taken)
                    throw new InvalidOperationException($"An orchestration profile named '{name}' already exists.");
                profile.Name = name;
            }

            if (description is not null) profile.Description = description;
            if (strategy    is not null) profile.Strategy    = strategy;
            if (roles       is not null) profile.Roles       = roles;
            if (workflow    is not null) profile.Workflow    = workflow;
            if (rules       is not null) profile.Rules       = rules;
            if (notes       is not null) profile.Notes       = notes;

            profile.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return profile;
        }

        /// <summary>Deletes the profile with the given <paramref name="id"/>. No-op when not found.</summary>
        internal async Task DeleteAsync(int id)
        {
            var profile = await _db.McpOrchestrations.FindAsync(id);
            if (profile is null) return;
            _db.McpOrchestrations.Remove(profile);
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Sets the orchestration with the given <paramref name="id"/> as active.
        /// Clears the <c>IsActive</c> flag on all other profiles first, ensuring only one is ever active.
        /// </summary>
        internal async Task<McpOrchestration> SetActiveAsync(int id)
        {
            var profile = await _db.McpOrchestrations.FindAsync(id)
                ?? throw new KeyNotFoundException($"Orchestration profile {id} not found.");

            // Clear all first, then activate the requested one.
            await _db.McpOrchestrations
                .Where(o => o.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.IsActive, false));

            profile.IsActive  = true;
            profile.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return profile;
        }

        // ── Import / export ────────────────────────────────────────────────────

        /// <summary>
        /// Imports an orchestration profile from a Markdown file.
        /// YAML front-matter is parsed for the name; the body becomes the profile's description.
        /// If a profile with the derived name already exists, it is updated.
        /// </summary>
        internal async Task<McpOrchestration> ImportFromFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            string raw = await File.ReadAllTextAsync(filePath, Encoding.UTF8);

            string? parsedName = null;
            string  body       = raw;

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
                        if (key == "name") parsedName = val;
                    }
                }
            }

            string profileName = parsedName ?? Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(profileName)) profileName = "Imported Orchestration";

            var existing = await _db.McpOrchestrations.FirstOrDefaultAsync(o => o.Name == profileName);
            if (existing is not null)
            {
                existing.Description = body;
                existing.UpdatedAt   = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return existing;
            }

            return await AddAsync(profileName, body, null, null, null, null, null);
        }

        /// <summary>Exports an orchestration profile to a Markdown file with YAML front-matter.</summary>
        internal async Task ExportToFileAsync(int id, string outputPath)
        {
            var profile = await _db.McpOrchestrations.FindAsync(id)
                ?? throw new KeyNotFoundException($"Orchestration profile {id} not found.");

            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine($"name: \"{profile.Name}\"");
            sb.AppendLine($"createdAt: \"{profile.CreatedAt:O}\"");
            sb.AppendLine($"updatedAt: \"{profile.UpdatedAt:O}\"");
            sb.AppendLine("---");
            sb.AppendLine();

            void AppendSection(string header, string? content)
            {
                if (string.IsNullOrWhiteSpace(content)) return;
                sb.AppendLine($"## {header}");
                sb.AppendLine();
                sb.AppendLine(content);
                sb.AppendLine();
            }

            AppendSection("Description", profile.Description);
            AppendSection("Strategy",    profile.Strategy);
            AppendSection("Roles",       profile.Roles);
            AppendSection("Workflow",    profile.Workflow);
            AppendSection("Rules",       profile.Rules);
            AppendSection("Notes",       profile.Notes);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
        }
    }
}
