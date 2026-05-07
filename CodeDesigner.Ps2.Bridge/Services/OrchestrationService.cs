using System;
using System.Text.Json;
using CodeDesigner.Ps2.Database.Entities;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class OrchestrationService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public OrchestrationService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(_ => HandleMcpOrchestrationList(),                                "MCPORCHESTRATION_LIST",      "MCPORCHESTRATIONLIST");
            reg.Add(c => HandleMcpOrchestrationGet(c.McpOrchestrationId),             "MCPORCHESTRATION_GET",       "MCPORCHESTRATIONGET");
            reg.Add(c => HandleMcpOrchestrationAdd(c),                                "MCPORCHESTRATION_ADD",       "MCPORCHESTRATIONADD");
            reg.Add(c => HandleMcpOrchestrationUpdate(c),                             "MCPORCHESTRATION_UPDATE",    "MCPORCHESTRATIONUPDATE");
            reg.Add(c => HandleMcpOrchestrationDelete(c.McpOrchestrationId),          "MCPORCHESTRATION_DELETE",    "MCPORCHESTRATIONDELETE");
            reg.Add(c => HandleMcpOrchestrationImport(c.FilePath),                    "MCPORCHESTRATION_IMPORT",    "MCPORCHESTRATIONIMPORT");
            reg.Add(c => HandleMcpOrchestrationExport(c.McpOrchestrationId, c.OutputPath), "MCPORCHESTRATION_EXPORT", "MCPORCHESTRATIONEXPORT");
            reg.Add(c => HandleMcpOrchestrationSetActive(c.McpOrchestrationId),       "MCPORCHESTRATION_SETACTIVE", "MCPORCHESTRATIONSETACTIVE");
        }

        // ── MCPORCHESTRATION_LIST ────────────────────────────────────────────

        private string HandleMcpOrchestrationList()
        {
            if (_ctx.McpOrchestrationRepo is null)
                return Responses.Error("MCP Orchestration repository is not available");
            try
            {
                var profiles = _ctx.McpOrchestrationRepo.ListAsync().GetAwaiter().GetResult();
                var infos    = profiles.ConvertAll(ToMcpOrchestrationDto);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success              = true,
                    Command              = "MCPORCHESTRATION_LIST",
                    McpOrchestrations    = infos,
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPORCHESTRATION_LIST error: {ex.Message}"); }
        }

        // ── MCPORCHESTRATION_GET ─────────────────────────────────────────────

        private string HandleMcpOrchestrationGet(int? id)
        {
            if (_ctx.McpOrchestrationRepo is null)
                return Responses.Error("MCP Orchestration repository is not available");
            if (id is null)
                return Responses.Error("McpOrchestrationId is required for MCPORCHESTRATION_GET");
            try
            {
                var profile = _ctx.McpOrchestrationRepo.GetAsync(id.Value).GetAwaiter().GetResult();
                if (profile is null)
                    return Responses.Error($"Orchestration profile {id} not found.");
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success             = true,
                    Command             = "MCPORCHESTRATION_GET",
                    McpOrchestration    = ToMcpOrchestrationDto(profile),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPORCHESTRATION_GET error: {ex.Message}"); }
        }

        // ── MCPORCHESTRATION_ADD ─────────────────────────────────────────────

        private string HandleMcpOrchestrationAdd(CommandMessage cmd)
        {
            if (_ctx.McpOrchestrationRepo is null)
                return Responses.Error("MCP Orchestration repository is not available");
            if (string.IsNullOrWhiteSpace(cmd.McpOrchestrationName))
                return Responses.Error("McpOrchestrationName is required for MCPORCHESTRATION_ADD");
            try
            {
                var profile = _ctx.McpOrchestrationRepo.AddAsync(
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
            catch (Exception ex) { return Responses.Error($"MCPORCHESTRATION_ADD error: {ex.Message}"); }
        }

        // ── MCPORCHESTRATION_UPDATE ──────────────────────────────────────────

        private string HandleMcpOrchestrationUpdate(CommandMessage cmd)
        {
            if (_ctx.McpOrchestrationRepo is null)
                return Responses.Error("MCP Orchestration repository is not available");
            if (cmd.McpOrchestrationId is null)
                return Responses.Error("McpOrchestrationId is required for MCPORCHESTRATION_UPDATE");
            try
            {
                var profile = _ctx.McpOrchestrationRepo.UpdateAsync(
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
            catch (Exception ex) { return Responses.Error($"MCPORCHESTRATION_UPDATE error: {ex.Message}"); }
        }

        // ── MCPORCHESTRATION_DELETE ──────────────────────────────────────────

        private string HandleMcpOrchestrationDelete(int? id)
        {
            if (_ctx.McpOrchestrationRepo is null)
                return Responses.Error("MCP Orchestration repository is not available");
            if (id is null)
                return Responses.Error("McpOrchestrationId is required for MCPORCHESTRATION_DELETE");
            try
            {
                _ctx.McpOrchestrationRepo.DeleteAsync(id.Value).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "MCPORCHESTRATION_DELETE",
                    Message = $"Orchestration profile {id} deleted.",
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPORCHESTRATION_DELETE error: {ex.Message}"); }
        }

        // ── MCPORCHESTRATION_IMPORT ──────────────────────────────────────────

        private string HandleMcpOrchestrationImport(string? filePath)
        {
            if (_ctx.McpOrchestrationRepo is null)
                return Responses.Error("MCP Orchestration repository is not available");
            if (string.IsNullOrWhiteSpace(filePath))
                return Responses.Error("FilePath is required for MCPORCHESTRATION_IMPORT");
            try
            {
                var profile = _ctx.McpOrchestrationRepo.ImportFromFileAsync(filePath!).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success             = true,
                    Command             = "MCPORCHESTRATION_IMPORT",
                    Message             = $"Imported orchestration profile '{profile.Name}'.",
                    McpOrchestration    = ToMcpOrchestrationDto(profile),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPORCHESTRATION_IMPORT error: {ex.Message}"); }
        }

        // ── MCPORCHESTRATION_EXPORT ──────────────────────────────────────────

        private string HandleMcpOrchestrationExport(int? id, string? outputPath)
        {
            if (_ctx.McpOrchestrationRepo is null)
                return Responses.Error("MCP Orchestration repository is not available");
            if (id is null)
                return Responses.Error("McpOrchestrationId is required for MCPORCHESTRATION_EXPORT");
            if (string.IsNullOrWhiteSpace(outputPath))
                return Responses.Error("OutputPath is required for MCPORCHESTRATION_EXPORT");
            try
            {
                _ctx.McpOrchestrationRepo.ExportToFileAsync(id.Value, outputPath!).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "MCPORCHESTRATION_EXPORT",
                    Message = $"Orchestration profile {id} exported to '{outputPath}'.",
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPORCHESTRATION_EXPORT error: {ex.Message}"); }
        }

        // ── MCPORCHESTRATION_SETACTIVE ───────────────────────────────────────

        private string HandleMcpOrchestrationSetActive(int? id)
        {
            if (_ctx.McpOrchestrationRepo is null)
                return Responses.Error("MCP Orchestration repository is not available");
            if (id is null)
                return Responses.Error("McpOrchestrationId is required for MCPORCHESTRATION_SETACTIVE");
            try
            {
                var profile = _ctx.McpOrchestrationRepo.SetActiveAsync(id.Value).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success             = true,
                    Command             = "MCPORCHESTRATION_SETACTIVE",
                    Message             = $"Orchestration profile '{profile.Name}' is now active.",
                    McpOrchestration    = ToMcpOrchestrationDto(profile),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPORCHESTRATION_SETACTIVE error: {ex.Message}"); }
        }

        // ── DTO mapper ────────────────────────────────────────────────────────

        private static McpOrchestrationInfo ToMcpOrchestrationDto(McpOrchestration o) => new McpOrchestrationInfo
        {
            Id          = o.Id,
            IsActive    = o.IsActive,
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
