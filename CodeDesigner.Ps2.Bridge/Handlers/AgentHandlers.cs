using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── MCPAGENT_LIST ─────────────────────────────────────────────────────

        private string HandleMcpAgentList()
        {
            try
            {
                var agents = Db.McpAgents
                    .Include(a => a.Tasks.OrderBy(t => t.SortOrder))
                    .OrderBy(a => a.Id)
                    .ToList();

                var infos = agents.ConvertAll(ToMcpAgentDto);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "MCPAGENT_LIST",
                    McpAgents = infos,
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPAGENT_LIST error: {ex.Message}"); }
        }

        // ── MCPAGENT_GET ──────────────────────────────────────────────────────

        private string HandleMcpAgentGet(int? id)
        {
            if (id is null)
                return CreateErrorResponse("McpAgentId is required for MCPAGENT_GET");
            try
            {
                var agent = Db.McpAgents
                    .Include(a => a.Tasks.OrderBy(t => t.SortOrder))
                    .FirstOrDefault(a => a.Id == id.Value);

                if (agent is null)
                    return CreateErrorResponse($"MCP Agent {id} not found");

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "MCPAGENT_GET",
                    McpAgent = ToMcpAgentDto(agent),
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPAGENT_GET error: {ex.Message}"); }
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private static McpAgentInfo ToMcpAgentDto(McpAgent a) => new()
        {
            Id                 = a.Id,
            Name               = a.Name,
            IsAgentLead        = a.IsAgentLead,
            ConcurrentTasks    = a.ConcurrentTasks,
            Description        = a.Description,
            Rules              = a.Rules,
            Guidelines         = a.Guidelines,
            RecommendedWorkflow = a.RecommendedWorkflow,
            CreatedAt          = a.CreatedAt.ToString("O"),
            UpdatedAt          = a.UpdatedAt.ToString("O"),
            Tasks              = a.Tasks.Count == 0 ? null : a.Tasks.Select(t => new McpAgentTaskInfo
            {
                Id          = t.Id,
                Phase       = t.Phase.ToString(),
                SortOrder   = t.SortOrder,
                Description = t.Description,
            }).ToList(),
        };
    }
}
