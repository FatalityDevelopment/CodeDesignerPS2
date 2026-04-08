using System;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── MCPAGENT_LIST ─────────────────────────────────────────────────────

        private string HandleMcpAgentList()
        {
            if (McpAgentRepo is null)
                return CreateErrorResponse("MCP Agent repository is not available");
            try
            {
                var agents = McpAgentRepo.ListAsync().GetAwaiter().GetResult();
                var infos  = agents.ConvertAll(ToMcpAgentDto);
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
            if (McpAgentRepo is null)
                return CreateErrorResponse("MCP Agent repository is not available");
            if (id is null)
                return CreateErrorResponse("McpAgentId is required for MCPAGENT_GET");
            try
            {
                var agent = McpAgentRepo.GetAsync(id.Value).GetAwaiter().GetResult();
                if (agent is null)
                    return CreateErrorResponse($"Agent {id} not found.");
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "MCPAGENT_GET",
                    McpAgent = ToMcpAgentDto(agent),
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPAGENT_GET error: {ex.Message}"); }
        }

        // ── MCPAGENT_ADD ──────────────────────────────────────────────────────

        private string HandleMcpAgentAdd(CommandMessage cmd)
        {
            if (McpAgentRepo is null)
                return CreateErrorResponse("MCP Agent repository is not available");
            if (string.IsNullOrWhiteSpace(cmd.McpAgentName))
                return CreateErrorResponse("McpAgentName is required for MCPAGENT_ADD");
            try
            {
                var agent = McpAgentRepo.AddAsync(
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
            catch (Exception ex) { return CreateErrorResponse($"MCPAGENT_ADD error: {ex.Message}"); }
        }

        // ── MCPAGENT_UPDATE ───────────────────────────────────────────────────

        private string HandleMcpAgentUpdate(CommandMessage cmd)
        {
            if (McpAgentRepo is null)
                return CreateErrorResponse("MCP Agent repository is not available");
            if (cmd.McpAgentId is null)
                return CreateErrorResponse("McpAgentId is required for MCPAGENT_UPDATE");
            try
            {
                var agent = McpAgentRepo.UpdateAsync(
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
            catch (Exception ex) { return CreateErrorResponse($"MCPAGENT_UPDATE error: {ex.Message}"); }
        }

        // ── MCPAGENT_DELETE ───────────────────────────────────────────────────

        private string HandleMcpAgentDelete(int? id)
        {
            if (McpAgentRepo is null)
                return CreateErrorResponse("MCP Agent repository is not available");
            if (id is null)
                return CreateErrorResponse("McpAgentId is required for MCPAGENT_DELETE");
            try
            {
                McpAgentRepo.DeleteAsync(id.Value).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "MCPAGENT_DELETE",
                    Message = $"Agent {id} deleted.",
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPAGENT_DELETE error: {ex.Message}"); }
        }

        // ── MCPAGENT_IMPORT ───────────────────────────────────────────────────

        private string HandleMcpAgentImport(string? filePath)
        {
            if (McpAgentRepo is null)
                return CreateErrorResponse("MCP Agent repository is not available");
            if (string.IsNullOrWhiteSpace(filePath))
                return CreateErrorResponse("FilePath is required for MCPAGENT_IMPORT");
            try
            {
                var agent = McpAgentRepo.ImportFromFileAsync(filePath!).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "MCPAGENT_IMPORT",
                    Message  = $"Imported agent '{agent.Name}'.",
                    McpAgent = ToMcpAgentDto(agent),
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPAGENT_IMPORT error: {ex.Message}"); }
        }

        // ── MCPAGENT_EXPORT ───────────────────────────────────────────────────

        private string HandleMcpAgentExport(int? id, string? outputPath)
        {
            if (McpAgentRepo is null)
                return CreateErrorResponse("MCP Agent repository is not available");
            if (id is null)
                return CreateErrorResponse("McpAgentId is required for MCPAGENT_EXPORT");
            if (string.IsNullOrWhiteSpace(outputPath))
                return CreateErrorResponse("OutputPath is required for MCPAGENT_EXPORT");
            try
            {
                McpAgentRepo.ExportToFileAsync(id.Value, outputPath!).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "MCPAGENT_EXPORT",
                    Message = $"Agent {id} exported to '{outputPath}'.",
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPAGENT_EXPORT error: {ex.Message}"); }
        }

        // ── DTO mapper ────────────────────────────────────────────────────────

        private static McpAgentInfo ToMcpAgentDto(Database.Entities.McpAgent a) => new McpAgentInfo
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
