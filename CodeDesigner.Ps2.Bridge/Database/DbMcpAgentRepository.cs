using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Bridge.Database
{
    /// <summary>
    /// Async persistence layer for MCP Agent profiles, backed by <see cref="CodeDesignerDbContext"/>.
    /// </summary>
    internal sealed class DbMcpAgentRepository
    {
        private readonly CodeDesignerDbContext _db;

        internal DbMcpAgentRepository(CodeDesignerDbContext db) => _db = db;

        /// <summary>Returns all agent profiles ordered by name.</summary>
        internal async Task<List<McpAgent>> ListAsync()
            => await _db.McpAgents.OrderBy(a => a.Name).ToListAsync();

        /// <summary>Returns the agent with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
        internal async Task<McpAgent?> GetAsync(int id)
            => await _db.McpAgents.FindAsync(id);

        /// <summary>Creates a new agent profile.</summary>
        internal async Task<McpAgent> AddAsync(
            string name,
            string? description,
            bool isAgentLead,
            string? status,
            string? objective,
            string? rules,
            string? guidelines,
            string? recommendedWorkflow,
            string? sessionBegin,
            string? concurrentTasks,
            string? sessionEnd,
            string? research)
        {
            bool exists = await _db.McpAgents.AnyAsync(a => a.Name == name);
            if (exists)
                throw new InvalidOperationException($"An agent named '{name}' already exists.");

            var agent = new McpAgent
            {
                Name                = name,
                Description         = description,
                IsAgentLead         = isAgentLead,
                Status              = status,
                Objective           = objective,
                Rules               = rules,
                Guidelines          = guidelines,
                RecommendedWorkflow = recommendedWorkflow,
                SessionBegin        = sessionBegin,
                ConcurrentTasks     = concurrentTasks,
                SessionEnd          = sessionEnd,
                Research            = research,
                CreatedAt           = DateTime.UtcNow,
                UpdatedAt           = DateTime.UtcNow,
            };

            _db.McpAgents.Add(agent);
            await _db.SaveChangesAsync();
            return agent;
        }

        /// <summary>
        /// Updates an existing agent profile.  Pass <c>null</c> to leave a field unchanged.
        /// </summary>
        internal async Task<McpAgent> UpdateAsync(
            int id,
            string? name,
            string? description,
            bool? isAgentLead,
            string? status,
            string? objective,
            string? rules,
            string? guidelines,
            string? recommendedWorkflow,
            string? sessionBegin,
            string? concurrentTasks,
            string? sessionEnd,
            string? research)
        {
            var agent = await _db.McpAgents.FindAsync(id)
                ?? throw new KeyNotFoundException($"Agent {id} not found.");

            if (name is not null)
            {
                bool taken = await _db.McpAgents.AnyAsync(a => a.Name == name && a.Id != id);
                if (taken)
                    throw new InvalidOperationException($"An agent named '{name}' already exists.");
                agent.Name = name;
            }

            if (description         is not null) agent.Description         = description;
            if (isAgentLead         is not null) agent.IsAgentLead         = isAgentLead.Value;
            if (status              is not null) agent.Status              = status;
            if (objective           is not null) agent.Objective           = objective;
            if (rules               is not null) agent.Rules               = rules;
            if (guidelines          is not null) agent.Guidelines          = guidelines;
            if (recommendedWorkflow is not null) agent.RecommendedWorkflow = recommendedWorkflow;
            if (sessionBegin        is not null) agent.SessionBegin        = sessionBegin;
            if (concurrentTasks     is not null) agent.ConcurrentTasks     = concurrentTasks;
            if (sessionEnd          is not null) agent.SessionEnd          = sessionEnd;
            if (research            is not null) agent.Research            = research;

            agent.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return agent;
        }

        /// <summary>Deletes the agent with the given <paramref name="id"/>. No-op when not found.</summary>
        internal async Task DeleteAsync(int id)
        {
            var agent = await _db.McpAgents.FindAsync(id);
            if (agent is null) return;
            _db.McpAgents.Remove(agent);
            await _db.SaveChangesAsync();
        }

        // ── Import / export ────────────────────────────────────────────────────

        /// <summary>
        /// Imports an agent profile from a Markdown file.
        /// YAML front-matter is parsed for metadata; the body becomes the agent's description.
        /// If an agent with the derived name already exists, it is updated.
        /// </summary>
        internal async Task<McpAgent> ImportFromFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            string raw = await File.ReadAllTextAsync(filePath, Encoding.UTF8);

            string? parsedName    = null;
            string? parsedStatus  = null;
            bool    parsedIsLead  = false;
            string  body          = raw;

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
                        if (key == "name")    parsedName   = val;
                        if (key == "status")  parsedStatus = val;
                        if (key == "islead")  parsedIsLead = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }

            string agentName = parsedName ?? Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(agentName)) agentName = "Imported Agent";

            var existing = await _db.McpAgents.FirstOrDefaultAsync(a => a.Name == agentName);
            if (existing is not null)
            {
                existing.Description = body;
                existing.Status      = parsedStatus ?? existing.Status;
                existing.IsAgentLead = parsedIsLead;
                existing.UpdatedAt   = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return existing;
            }

            return await AddAsync(agentName, body, parsedIsLead, parsedStatus,
                null, null, null, null, null, null, null, null);
        }

        /// <summary>Exports an agent profile to a Markdown file with YAML front-matter.</summary>
        internal async Task ExportToFileAsync(int id, string outputPath)
        {
            var agent = await _db.McpAgents.FindAsync(id)
                ?? throw new KeyNotFoundException($"Agent {id} not found.");

            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine($"name: \"{agent.Name}\"");
            if (!string.IsNullOrWhiteSpace(agent.Status))
                sb.AppendLine($"status: \"{agent.Status}\"");
            sb.AppendLine($"isLead: {agent.IsAgentLead.ToString().ToLower()}");
            sb.AppendLine($"createdAt: \"{agent.CreatedAt:O}\"");
            sb.AppendLine($"updatedAt: \"{agent.UpdatedAt:O}\"");
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

            AppendSection("Description",          agent.Description);
            AppendSection("Objective",             agent.Objective);
            AppendSection("Rules",                 agent.Rules);
            AppendSection("Guidelines",            agent.Guidelines);
            AppendSection("Session Begin",         agent.SessionBegin);
            AppendSection("Concurrent Tasks",      agent.ConcurrentTasks);
            AppendSection("Session End",           agent.SessionEnd);
            AppendSection("Recommended Workflow",  agent.RecommendedWorkflow);
            AppendSection("Research",              agent.Research);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
        }
    }
}
