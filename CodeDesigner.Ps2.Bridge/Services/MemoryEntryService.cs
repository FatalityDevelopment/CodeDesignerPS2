using System;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class MemoryEntryService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public MemoryEntryService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleMcpMemoryList(c.GameId),                                                 "MCPMEMORY_LIST", "MCPMEMORYLIST");
            reg.Add(c => HandleMcpMemoryGet(c.McpMemoryId),                                             "MCPMEMORY_GET", "MCPMEMORYGET");
            reg.Add(c => HandleMcpMemoryAdd(c.McpMemoryName, c.McpMemoryContent, c.GameId, c.McpMemoryAgentId), "MCPMEMORY_ADD", "MCPMEMORYADD");
            reg.Add(c => HandleMcpMemoryUpdate(c.McpMemoryId, c.McpMemoryName, c.McpMemoryContent, c.GameId, c.McpMemoryAgentId), "MCPMEMORY_UPDATE", "MCPMEMORYUPDATE");
            reg.Add(c => HandleMcpMemoryDelete(c.McpMemoryId),                                          "MCPMEMORY_DELETE", "MCPMEMORYDELETE");
            reg.Add(c => HandleMcpMemoryImport(c.FilePath),                                             "MCPMEMORY_IMPORT", "MCPMEMORYIMPORT");
            reg.Add(c => HandleMcpMemoryExport(c.McpMemoryId, c.OutputPath),                            "MCPMEMORY_EXPORT", "MCPMEMORYEXPORT");
            reg.Add(c => HandleMcpMemoryDeleteByName(c.McpMemoryName),                                  "MCPMEMORY_DELETE_BY_NAME", "MCPMEMORYDELETEBYNAME");
            reg.Add(c => HandleMcpMemoryUpsert(c.McpMemoryName, c.McpMemoryContent, c.McpMemoryAgentId),"MCPMEMORY_UPSERT", "MCPMEMORYUPSERT");
            reg.Add(c => HandleMcpMemoryListForAgent(c.McpMemoryAgentId),                               "MCPMEMORY_LIST_FOR_AGENT", "MCPMEMORYLISTFORAGENT");
            reg.Add(_ => HandleMcpMemoryListShared(),                                                   "MCPMEMORY_LIST_SHARED", "MCPMEMORYLISTSHARED");
        }

        // ── MCPMEMORY_LIST ────────────────────────────────────────────────────

        private string HandleMcpMemoryList(string? gameId)
        {
            if (_ctx.McpMemoryRepo is null)
                return Responses.Error("MCP Memory repository is not available");
            try
            {
                var entries = _ctx.McpMemoryRepo.ListAsync(gameId).GetAwaiter().GetResult();
                var infos = entries.ConvertAll(e => new McpMemoryInfo
                {
                    Id        = e.Id,
                    Name      = e.Name,
                    Content   = e.Content,
                    GameId    = e.GameId,
                    AgentId   = e.AgentId,
                    CreatedAt = e.CreatedAt.ToString("O"),
                    UpdatedAt = e.UpdatedAt.ToString("O"),
                });
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success    = true,
                    Command    = "MCPMEMORY_LIST",
                    McpMemories = infos,
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPMEMORY_LIST error: {ex.Message}"); }
        }

        // ── MCPMEMORY_GET ─────────────────────────────────────────────────────

        private string HandleMcpMemoryGet(int? id)
        {
            if (_ctx.McpMemoryRepo is null)
                return Responses.Error("MCP Memory repository is not available");
            if (id is null)
                return Responses.Error("McpMemoryId is required for MCPMEMORY_GET");
            try
            {
                var entry = _ctx.McpMemoryRepo.GetAsync(id.Value).GetAwaiter().GetResult();
                if (entry is null)
                    return Responses.Error($"MCP Memory entry {id} not found");

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "MCPMEMORY_GET",
                    McpMemory = ToMcpMemoryDto(entry),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPMEMORY_GET error: {ex.Message}"); }
        }

        // ── MCPMEMORY_ADD ─────────────────────────────────────────────────────

        private string HandleMcpMemoryAdd(string? name, string? content, string? gameId, int? agentId)
        {
            if (_ctx.McpMemoryRepo is null)
                return Responses.Error("MCP Memory repository is not available");
            if (string.IsNullOrWhiteSpace(name))
                return Responses.Error("McpMemoryName is required for MCPMEMORY_ADD");
            try
            {
                var entry = _ctx.McpMemoryRepo.AddAsync(name, content ?? string.Empty, gameId, agentId).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "MCPMEMORY_ADD",
                    Message   = $"Created MCP Memory entry '{entry.Name}'",
                    McpMemory = ToMcpMemoryDto(entry),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPMEMORY_ADD error: {ex.Message}"); }
        }

        // ── MCPMEMORY_UPDATE ──────────────────────────────────────────────────

        private string HandleMcpMemoryUpdate(int? id, string? name, string? content, string? gameId, int? agentId)
        {
            if (_ctx.McpMemoryRepo is null)
                return Responses.Error("MCP Memory repository is not available");
            if (id is null)
                return Responses.Error("McpMemoryId is required for MCPMEMORY_UPDATE");
            try
            {
                var entry = _ctx.McpMemoryRepo.UpdateAsync(id.Value, name, content, gameId, agentId).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "MCPMEMORY_UPDATE",
                    Message   = $"Updated MCP Memory entry '{entry.Name}'",
                    McpMemory = ToMcpMemoryDto(entry),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPMEMORY_UPDATE error: {ex.Message}"); }
        }

        // ── MCPMEMORY_DELETE ──────────────────────────────────────────────────

        private string HandleMcpMemoryDelete(int? id)
        {
            if (_ctx.McpMemoryRepo is null)
                return Responses.Error("MCP Memory repository is not available");
            if (id is null)
                return Responses.Error("McpMemoryId is required for MCPMEMORY_DELETE");
            try
            {
                _ctx.McpMemoryRepo.DeleteAsync(id.Value).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "MCPMEMORY_DELETE",
                    Message = $"Deleted MCP Memory entry {id}",
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPMEMORY_DELETE error: {ex.Message}"); }
        }

        // ── MCPMEMORY_IMPORT ──────────────────────────────────────────────────

        private string HandleMcpMemoryImport(string? filePath)
        {
            if (_ctx.McpMemoryRepo is null)
                return Responses.Error("MCP Memory repository is not available");
            if (string.IsNullOrWhiteSpace(filePath))
                return Responses.Error("FilePath is required for MCPMEMORY_IMPORT");
            try
            {
                var entry = _ctx.McpMemoryRepo.ImportFromFileAsync(filePath).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "MCPMEMORY_IMPORT",
                    Message   = $"Imported MCP Memory entry '{entry.Name}'",
                    McpMemory = ToMcpMemoryDto(entry),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPMEMORY_IMPORT error: {ex.Message}"); }
        }

        // ── MCPMEMORY_EXPORT ──────────────────────────────────────────────────

        private string HandleMcpMemoryExport(int? id, string? outputPath)
        {
            if (_ctx.McpMemoryRepo is null)
                return Responses.Error("MCP Memory repository is not available");
            if (id is null)
                return Responses.Error("McpMemoryId is required for MCPMEMORY_EXPORT");
            if (string.IsNullOrWhiteSpace(outputPath))
                return Responses.Error("OutputPath is required for MCPMEMORY_EXPORT");
            try
            {
                _ctx.McpMemoryRepo.ExportToFileAsync(id.Value, outputPath).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "MCPMEMORY_EXPORT",
                    Message = $"Exported MCP Memory entry {id} to {outputPath}",
                    Data    = outputPath,
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPMEMORY_EXPORT error: {ex.Message}"); }
        }

        // ── MCPMEMORY_DELETE_BY_NAME ──────────────────────────────────────────

        private string HandleMcpMemoryDeleteByName(string? name)
        {
            if (_ctx.McpMemoryRepo is null)
                return Responses.Error("MCP Memory repository is not available");
            if (string.IsNullOrWhiteSpace(name))
                return Responses.Error("McpMemoryName is required for MCPMEMORY_DELETE_BY_NAME");
            try
            {
                var entry = _ctx.McpMemoryRepo.FindByNameAsync(name!).GetAwaiter().GetResult();
                if (entry is null)
                {
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = true,
                        Command = "MCPMEMORY_DELETE_BY_NAME",
                        Message = $"No MCP Memory entry named '{name}' found.",
                        Data    = "false",
                    });
                }

                _ctx.McpMemoryRepo.DeleteAsync(entry.Id).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "MCPMEMORY_DELETE_BY_NAME",
                    Message = $"Deleted MCP Memory entry '{name}'",
                    Data    = "true",
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPMEMORY_DELETE_BY_NAME error: {ex.Message}"); }
        }

        // ── MCPMEMORY_UPSERT ──────────────────────────────────────────────────

        /// <summary>
        /// MCPMEMORY_UPSERT — creates or overwrites a memory entry by slug name.
        /// This is the write path used by MCP agent processes so all writes are
        /// serialized through the Bridge's single <see cref="CodeDesignerDbContext"/>,
        /// eliminating cross-process SQLite write conflicts.
        /// </summary>
        private string HandleMcpMemoryUpsert(string? name, string? content, int? agentId)
        {
            if (_ctx.McpMemoryRepo is null)
                return Responses.Error("MCP Memory repository is not available");
            if (string.IsNullOrWhiteSpace(name))
                return Responses.Error("McpMemoryName is required for MCPMEMORY_UPSERT");
            try
            {
                var entry = _ctx.McpMemoryRepo.UpsertAsync(name!, content ?? string.Empty, null, agentId).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "MCPMEMORY_UPSERT",
                    Message   = $"Upserted MCP Memory entry '{entry.Name}'",
                    McpMemory = ToMcpMemoryDto(entry),
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPMEMORY_UPSERT error: {ex.Message}"); }
        }

        // ── MCPMEMORY_LIST_FOR_AGENT ──────────────────────────────────────────

        private string HandleMcpMemoryListForAgent(int? agentId)
        {
            if (_ctx.McpMemoryRepo is null)
                return Responses.Error("MCP Memory repository is not available");
            if (agentId is null)
                return Responses.Error("McpMemoryAgentId is required for MCPMEMORY_LIST_FOR_AGENT");
            try
            {
                var entries = _ctx.McpMemoryRepo.ListForAgentAsync(agentId.Value).GetAwaiter().GetResult();
                var infos = entries.ConvertAll(ToMcpMemoryDto);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success     = true,
                    Command     = "MCPMEMORY_LIST_FOR_AGENT",
                    McpMemories = infos,
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPMEMORY_LIST_FOR_AGENT error: {ex.Message}"); }
        }

        // ── MCPMEMORY_LIST_SHARED ─────────────────────────────────────────────

        private string HandleMcpMemoryListShared()
        {
            if (_ctx.McpMemoryRepo is null)
                return Responses.Error("MCP Memory repository is not available");
            try
            {
                var entries = _ctx.McpMemoryRepo.ListSharedAsync().GetAwaiter().GetResult();
                var infos = entries.ConvertAll(ToMcpMemoryDto);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success     = true,
                    Command     = "MCPMEMORY_LIST_SHARED",
                    McpMemories = infos,
                });
            }
            catch (Exception ex) { return Responses.Error($"MCPMEMORY_LIST_SHARED error: {ex.Message}"); }
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private static McpMemoryInfo ToMcpMemoryDto(CodeDesigner.Ps2.Database.Entities.McpMemory e) => new()
        {
            Id        = e.Id,
            Name      = e.Name,
            Content   = e.Content,
            GameId    = e.GameId,
            AgentId   = e.AgentId,
            CreatedAt = e.CreatedAt.ToString("O"),
            UpdatedAt = e.UpdatedAt.ToString("O"),
        };
    }
}
