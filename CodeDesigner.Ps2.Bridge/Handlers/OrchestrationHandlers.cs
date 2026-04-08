using System;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── MCPORCHESTRATION_LIST ────────────────────────────────────────────

        private string HandleMcpOrchestrationList()
        {
            if (McpOrchestrationRepo is null)
                return CreateErrorResponse("MCP Orchestration repository is not available");
            try
            {
                var profiles = McpOrchestrationRepo.ListAsync().GetAwaiter().GetResult();
                var infos    = profiles.ConvertAll(ToMcpOrchestrationDto);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success              = true,
                    Command              = "MCPORCHESTRATION_LIST",
                    McpOrchestrations    = infos,
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPORCHESTRATION_LIST error: {ex.Message}"); }
        }

        // ── MCPORCHESTRATION_GET ─────────────────────────────────────────────

        private string HandleMcpOrchestrationGet(int? id)
        {
            if (McpOrchestrationRepo is null)
                return CreateErrorResponse("MCP Orchestration repository is not available");
            if (id is null)
                return CreateErrorResponse("McpOrchestrationId is required for MCPORCHESTRATION_GET");
            try
            {
                var profile = McpOrchestrationRepo.GetAsync(id.Value).GetAwaiter().GetResult();
                if (profile is null)
                    return CreateErrorResponse($"Orchestration profile {id} not found.");
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success             = true,
                    Command             = "MCPORCHESTRATION_GET",
                    McpOrchestration    = ToMcpOrchestrationDto(profile),
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPORCHESTRATION_GET error: {ex.Message}"); }
        }

        // ── MCPORCHESTRATION_ADD ─────────────────────────────────────────────

        private string HandleMcpOrchestrationAdd(CommandMessage cmd)
        {
            if (McpOrchestrationRepo is null)
                return CreateErrorResponse("MCP Orchestration repository is not available");
            if (string.IsNullOrWhiteSpace(cmd.McpOrchestrationName))
                return CreateErrorResponse("McpOrchestrationName is required for MCPORCHESTRATION_ADD");
            try
            {
                var profile = McpOrchestrationRepo.AddAsync(
                    cmd.McpOrchestrationName!,
                    cmd.McpOrchestrationDescription,
                    cmd.McpOrchestrationStrategy,
                    cmd.McpOrchestrationRoles,
                    cmd.McpOrchestrationWorkflow,
                    cmd.McpOrchestrationRules,
                    cmd.McpOrchestrationNotes
                ).GetAwaiter().GetResult();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success             = true,
                    Command             = "MCPORCHESTRATION_ADD",
                    Message             = $"Orchestration profile '{profile.Name}' created.",
                    McpOrchestration    = ToMcpOrchestrationDto(profile),
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPORCHESTRATION_ADD error: {ex.Message}"); }
        }

        // ── MCPORCHESTRATION_UPDATE ──────────────────────────────────────────

        private string HandleMcpOrchestrationUpdate(CommandMessage cmd)
        {
            if (McpOrchestrationRepo is null)
                return CreateErrorResponse("MCP Orchestration repository is not available");
            if (cmd.McpOrchestrationId is null)
                return CreateErrorResponse("McpOrchestrationId is required for MCPORCHESTRATION_UPDATE");
            try
            {
                var profile = McpOrchestrationRepo.UpdateAsync(
                    cmd.McpOrchestrationId.Value,
                    cmd.McpOrchestrationName,
                    cmd.McpOrchestrationDescription,
                    cmd.McpOrchestrationStrategy,
                    cmd.McpOrchestrationRoles,
                    cmd.McpOrchestrationWorkflow,
                    cmd.McpOrchestrationRules,
                    cmd.McpOrchestrationNotes
                ).GetAwaiter().GetResult();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success             = true,
                    Command             = "MCPORCHESTRATION_UPDATE",
                    Message             = $"Orchestration profile '{profile.Name}' updated.",
                    McpOrchestration    = ToMcpOrchestrationDto(profile),
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPORCHESTRATION_UPDATE error: {ex.Message}"); }
        }

        // ── MCPORCHESTRATION_DELETE ──────────────────────────────────────────

        private string HandleMcpOrchestrationDelete(int? id)
        {
            if (McpOrchestrationRepo is null)
                return CreateErrorResponse("MCP Orchestration repository is not available");
            if (id is null)
                return CreateErrorResponse("McpOrchestrationId is required for MCPORCHESTRATION_DELETE");
            try
            {
                McpOrchestrationRepo.DeleteAsync(id.Value).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "MCPORCHESTRATION_DELETE",
                    Message = $"Orchestration profile {id} deleted.",
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPORCHESTRATION_DELETE error: {ex.Message}"); }
        }

        // ── MCPORCHESTRATION_IMPORT ──────────────────────────────────────────

        private string HandleMcpOrchestrationImport(string? filePath)
        {
            if (McpOrchestrationRepo is null)
                return CreateErrorResponse("MCP Orchestration repository is not available");
            if (string.IsNullOrWhiteSpace(filePath))
                return CreateErrorResponse("FilePath is required for MCPORCHESTRATION_IMPORT");
            try
            {
                var profile = McpOrchestrationRepo.ImportFromFileAsync(filePath!).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success             = true,
                    Command             = "MCPORCHESTRATION_IMPORT",
                    Message             = $"Imported orchestration profile '{profile.Name}'.",
                    McpOrchestration    = ToMcpOrchestrationDto(profile),
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPORCHESTRATION_IMPORT error: {ex.Message}"); }
        }

        // ── MCPORCHESTRATION_EXPORT ──────────────────────────────────────────

        private string HandleMcpOrchestrationExport(int? id, string? outputPath)
        {
            if (McpOrchestrationRepo is null)
                return CreateErrorResponse("MCP Orchestration repository is not available");
            if (id is null)
                return CreateErrorResponse("McpOrchestrationId is required for MCPORCHESTRATION_EXPORT");
            if (string.IsNullOrWhiteSpace(outputPath))
                return CreateErrorResponse("OutputPath is required for MCPORCHESTRATION_EXPORT");
            try
            {
                McpOrchestrationRepo.ExportToFileAsync(id.Value, outputPath!).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "MCPORCHESTRATION_EXPORT",
                    Message = $"Orchestration profile {id} exported to '{outputPath}'.",
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPORCHESTRATION_EXPORT error: {ex.Message}"); }
        }

        // ── DTO mapper ────────────────────────────────────────────────────────

        private static McpOrchestrationInfo ToMcpOrchestrationDto(Database.Entities.McpOrchestration o) => new McpOrchestrationInfo
        {
            Id          = o.Id,
            Name        = o.Name,
            Description = o.Description,
            Strategy    = o.Strategy,
            Roles       = o.Roles,
            Workflow    = o.Workflow,
            Rules       = o.Rules,
            Notes       = o.Notes,
            CreatedAt   = o.CreatedAt.ToString("O"),
            UpdatedAt   = o.UpdatedAt.ToString("O"),
        };
    }
}
