using System;
using System.Text.Json;
using CodeDesigner.Ps2.Database.Entities;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class AgentService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public AgentService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(_ => HandleMcpAgentList(),                                "MCPAGENT_LIST",   "MCPAGENTLIST");
            reg.Add(c => HandleMcpAgentGet(c.McpAgentId),                     "MCPAGENT_GET",    "MCPAGENTGET");
            reg.Add(c => HandleMcpAgentAdd(c),                                "MCPAGENT_ADD",    "MCPAGENTADD");
            reg.Add(c => HandleMcpAgentUpdate(c),                             "MCPAGENT_UPDATE", "MCPAGENTUPDATE");
            reg.Add(c => HandleMcpAgentDelete(c.McpAgentId),                  "MCPAGENT_DELETE", "MCPAGENTDELETE");
            reg.Add(c => HandleMcpAgentImport(c.FilePath),                    "MCPAGENT_IMPORT", "MCPAGENTIMPORT");
            reg.Add(c => HandleMcpAgentExport(c.McpAgentId, c.OutputPath),    "MCPAGENT_EXPORT", "MCPAGENTEXPORT");
        }

        // ── MCPAGENT_LIST ─────────────────────────────────────────────────────

        private string HandleMcpAgentList()
        {
            if (_ctx.McpAgentRepo is null)
                return Responses.Error("MCP Agent repository is not available");
            try
            {
                var agents = _ctx.McpAgentRepo.ListAsync().GetAwaiter().GetResult();
                // Use an explicit lambda to avoid generic type inference issues with method groups.
                var infos  = agents.ConvertAll(a => ToMcpAgentDto(a));
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "MCPAGENT_LIST",
                    McpAgents = infos,
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPAGENT_LIST error: {ex.Message}"); }
        }

        // ── MCPAGENT_GET ──────────────────────────────────────────────────────

        private string HandleMcpAgentGet(int? id)
        {
            if (_ctx.McpAgentRepo is null)
                return Responses.Error("MCP Agent repository is not available");
            if (id is null)
                return Responses.Error("McpAgentId is required for MCPAGENT_GET");
            try
            {
                var agent = _ctx.McpAgentRepo.GetAsync(id.Value).GetAwaiter().GetResult();
                if (agent is null)
                    return Responses.Error($"Agent {id} not found.");
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "MCPAGENT_GET",
                    McpAgent = ToMcpAgentDto(agent),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPAGENT_GET error: {ex.Message}"); }
        }

        // ── MCPAGENT_ADD ──────────────────────────────────────────────────────

        private string HandleMcpAgentAdd(CommandMessage cmd)
        {
            if (_ctx.McpAgentRepo is null)
                return Responses.Error("MCP Agent repository is not available");
            if (string.IsNullOrWhiteSpace(cmd.McpAgentName))
                return Responses.Error("McpAgentName is required for MCPAGENT_ADD");
            try
            {
                var agent = _ctx.McpAgentRepo.AddAsync(
                    cmd.McpAgentName!,
                    cmd.McpAgentDescription,
                    cmd.McpAgentIsLead ?? false,
                    cmd.McpAgentStatus,
                    cmd.McpAgentObjective,
                    cmd.McpAgentRules,
                    cmd.McpAgentGuidelines,
                    cmd.McpAgentRecommendedWorkflow,
                    cmd.McpAgentSessionBegin,
                    cmd.McpAgentConcurrentTasks,
                    cmd.McpAgentSessionEnd,
                    cmd.McpAgentResearch
                ).GetAwaiter().GetResult();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "MCPAGENT_ADD",
                    Message  = $"Agent '{agent.Name}' created.",
                    McpAgent = ToMcpAgentDto(agent),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPAGENT_ADD error: {ex.Message}"); }
        }

        // ── MCPAGENT_UPDATE ───────────────────────────────────────────────────

        private string HandleMcpAgentUpdate(CommandMessage cmd)
        {
            if (_ctx.McpAgentRepo is null)
                return Responses.Error("MCP Agent repository is not available");
            if (cmd.McpAgentId is null)
                return Responses.Error("McpAgentId is required for MCPAGENT_UPDATE");
            try
            {
                var agent = _ctx.McpAgentRepo.UpdateAsync(
                    cmd.McpAgentId.Value,
                    cmd.McpAgentName,
                    cmd.McpAgentDescription,
                    cmd.McpAgentIsLead,
                    cmd.McpAgentStatus,
                    cmd.McpAgentObjective,
                    cmd.McpAgentRules,
                    cmd.McpAgentGuidelines,
                    cmd.McpAgentRecommendedWorkflow,
                    cmd.McpAgentSessionBegin,
                    cmd.McpAgentConcurrentTasks,
                    cmd.McpAgentSessionEnd,
                    cmd.McpAgentResearch
                ).GetAwaiter().GetResult();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "MCPAGENT_UPDATE",
                    Message  = $"Agent '{agent.Name}' updated.",
                    McpAgent = ToMcpAgentDto(agent),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPAGENT_UPDATE error: {ex.Message}"); }
        }

        // ── MCPAGENT_DELETE ───────────────────────────────────────────────────

        private string HandleMcpAgentDelete(int? id)
        {
            if (_ctx.McpAgentRepo is null)
                return Responses.Error("MCP Agent repository is not available");
            if (id is null)
                return Responses.Error("McpAgentId is required for MCPAGENT_DELETE");
            try
            {
                _ctx.McpAgentRepo.DeleteAsync(id.Value).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "MCPAGENT_DELETE",
                    Message = $"Agent {id} deleted.",
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPAGENT_DELETE error: {ex.Message}"); }
        }

        // ── MCPAGENT_IMPORT ───────────────────────────────────────────────────

        private string HandleMcpAgentImport(string? filePath)
        {
            if (_ctx.McpAgentRepo is null)
                return Responses.Error("MCP Agent repository is not available");
            if (string.IsNullOrWhiteSpace(filePath))
                return Responses.Error("FilePath is required for MCPAGENT_IMPORT");
            try
            {
                var agent = _ctx.McpAgentRepo.ImportFromFileAsync(filePath!).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "MCPAGENT_IMPORT",
                    Message  = $"Imported agent '{agent.Name}'.",
                    McpAgent = ToMcpAgentDto(agent),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPAGENT_IMPORT error: {ex.Message}"); }
        }

        // ── MCPAGENT_EXPORT ───────────────────────────────────────────────────

        private string HandleMcpAgentExport(int? id, string? outputPath)
        {
            if (_ctx.McpAgentRepo is null)
                return Responses.Error("MCP Agent repository is not available");
            if (id is null)
                return Responses.Error("McpAgentId is required for MCPAGENT_EXPORT");
            if (string.IsNullOrWhiteSpace(outputPath))
                return Responses.Error("OutputPath is required for MCPAGENT_EXPORT");
            try
            {
                _ctx.McpAgentRepo.ExportToFileAsync(id.Value, outputPath!).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "MCPAGENT_EXPORT",
                    Message = $"Agent {id} exported to '{outputPath}'.",
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPAGENT_EXPORT error: {ex.Message}"); }
        }

        // ── DTO mapper ────────────────────────────────────────────────────────

        private static McpAgentInfo ToMcpAgentDto(McpAgent a) => new McpAgentInfo
        {
            Id                  = a.Id,
            IsAgentLead         = a.IsAgentLead,
            Name                = a.Name,
            Description         = a.Description,
            Status              = a.Status,
            Objective           = a.Objective,
            Rules               = a.Rules,
            Guidelines          = a.Guidelines,
            RecommendedWorkflow = a.RecommendedWorkflow,
            SessionBegin        = a.SessionBegin,
            ConcurrentTasks     = a.ConcurrentTasks,
            SessionEnd          = a.SessionEnd,
            Research            = a.Research,
            CreatedAt           = a.CreatedAt.ToString("O"),
            UpdatedAt           = a.UpdatedAt.ToString("O"),
        };
    }
}
