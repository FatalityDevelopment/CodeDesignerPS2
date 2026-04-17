using System;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── MCPMEMORY_LIST ────────────────────────────────────────────────────

        private string HandleMcpMemoryList(string? gameId)
        {
            if (McpMemoryRepo is null)
                return CreateErrorResponse("MCP Memory repository is not available");
            try
            {
                var entries = McpMemoryRepo.ListAsync(gameId).GetAwaiter().GetResult();
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
            catch (Exception ex) { return CreateErrorResponse($"MCPMEMORY_LIST error: {ex.Message}"); }
        }

        // ── MCPMEMORY_GET ─────────────────────────────────────────────────────

        private string HandleMcpMemoryGet(int? id)
        {
            if (McpMemoryRepo is null)
                return CreateErrorResponse("MCP Memory repository is not available");
            if (id is null)
                return CreateErrorResponse("McpMemoryId is required for MCPMEMORY_GET");
            try
            {
                var entry = McpMemoryRepo.GetAsync(id.Value).GetAwaiter().GetResult();
                if (entry is null)
                    return CreateErrorResponse($"MCP Memory entry {id} not found");

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "MCPMEMORY_GET",
                    McpMemory = ToMcpMemoryDto(entry),
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPMEMORY_GET error: {ex.Message}"); }
        }

        // ── MCPMEMORY_ADD ─────────────────────────────────────────────────────

        private string HandleMcpMemoryAdd(string? name, string? content, string? gameId, int? agentId)
        {
            if (McpMemoryRepo is null)
                return CreateErrorResponse("MCP Memory repository is not available");
            if (string.IsNullOrWhiteSpace(name))
                return CreateErrorResponse("McpMemoryName is required for MCPMEMORY_ADD");
            try
            {
                var entry = McpMemoryRepo.AddAsync(name, content ?? string.Empty, gameId, agentId).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "MCPMEMORY_ADD",
                    Message   = $"Created MCP Memory entry '{entry.Name}'",
                    McpMemory = ToMcpMemoryDto(entry),
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPMEMORY_ADD error: {ex.Message}"); }
        }

        // ── MCPMEMORY_UPDATE ──────────────────────────────────────────────────

        private string HandleMcpMemoryUpdate(int? id, string? name, string? content, string? gameId, int? agentId)
        {
            if (McpMemoryRepo is null)
                return CreateErrorResponse("MCP Memory repository is not available");
            if (id is null)
                return CreateErrorResponse("McpMemoryId is required for MCPMEMORY_UPDATE");
            try
            {
                var entry = McpMemoryRepo.UpdateAsync(id.Value, name, content, gameId, agentId).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "MCPMEMORY_UPDATE",
                    Message   = $"Updated MCP Memory entry '{entry.Name}'",
                    McpMemory = ToMcpMemoryDto(entry),
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPMEMORY_UPDATE error: {ex.Message}"); }
        }

        // ── MCPMEMORY_DELETE ──────────────────────────────────────────────────

        private string HandleMcpMemoryDelete(int? id)
        {
            if (McpMemoryRepo is null)
                return CreateErrorResponse("MCP Memory repository is not available");
            if (id is null)
                return CreateErrorResponse("McpMemoryId is required for MCPMEMORY_DELETE");
            try
            {
                McpMemoryRepo.DeleteAsync(id.Value).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "MCPMEMORY_DELETE",
                    Message = $"Deleted MCP Memory entry {id}",
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPMEMORY_DELETE error: {ex.Message}"); }
        }

        // ── MCPMEMORY_IMPORT ──────────────────────────────────────────────────

        private string HandleMcpMemoryImport(string? filePath)
        {
            if (McpMemoryRepo is null)
                return CreateErrorResponse("MCP Memory repository is not available");
            if (string.IsNullOrWhiteSpace(filePath))
                return CreateErrorResponse("FilePath is required for MCPMEMORY_IMPORT");
            try
            {
                var entry = McpMemoryRepo.ImportFromFileAsync(filePath).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "MCPMEMORY_IMPORT",
                    Message   = $"Imported MCP Memory entry '{entry.Name}'",
                    McpMemory = ToMcpMemoryDto(entry),
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPMEMORY_IMPORT error: {ex.Message}"); }
        }

        // ── MCPMEMORY_EXPORT ──────────────────────────────────────────────────

        private string HandleMcpMemoryExport(int? id, string? outputPath)
        {
            if (McpMemoryRepo is null)
                return CreateErrorResponse("MCP Memory repository is not available");
            if (id is null)
                return CreateErrorResponse("McpMemoryId is required for MCPMEMORY_EXPORT");
            if (string.IsNullOrWhiteSpace(outputPath))
                return CreateErrorResponse("OutputPath is required for MCPMEMORY_EXPORT");
            try
            {
                McpMemoryRepo.ExportToFileAsync(id.Value, outputPath).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "MCPMEMORY_EXPORT",
                    Message = $"Exported MCP Memory entry {id} to {outputPath}",
                    Data    = outputPath,
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPMEMORY_EXPORT error: {ex.Message}"); }
        }

        // ── MCPMEMORY_DELETE_BY_NAME ──────────────────────────────────────────

        private string HandleMcpMemoryDeleteByName(string? name)
        {
            if (McpMemoryRepo is null)
                return CreateErrorResponse("MCP Memory repository is not available");
            if (string.IsNullOrWhiteSpace(name))
                return CreateErrorResponse("McpMemoryName is required for MCPMEMORY_DELETE_BY_NAME");
            try
            {
                var entry = McpMemoryRepo.FindByNameAsync(name!).GetAwaiter().GetResult();
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

                McpMemoryRepo.DeleteAsync(entry.Id).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "MCPMEMORY_DELETE_BY_NAME",
                    Message = $"Deleted MCP Memory entry '{name}'",
                    Data    = "true",
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPMEMORY_DELETE_BY_NAME error: {ex.Message}"); }
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
            if (McpMemoryRepo is null)
                return CreateErrorResponse("MCP Memory repository is not available");
            if (string.IsNullOrWhiteSpace(name))
                return CreateErrorResponse("McpMemoryName is required for MCPMEMORY_UPSERT");
            try
            {
                var entry = McpMemoryRepo.UpsertAsync(name!, content ?? string.Empty, null, agentId).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "MCPMEMORY_UPSERT",
                    Message   = $"Upserted MCP Memory entry '{entry.Name}'",
                    McpMemory = ToMcpMemoryDto(entry),
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPMEMORY_UPSERT error: {ex.Message}"); }
        }

        // ── MCPMEMORY_LIST_FOR_AGENT ──────────────────────────────────────────

        private string HandleMcpMemoryListForAgent(int? agentId)
        {
            if (McpMemoryRepo is null)
                return CreateErrorResponse("MCP Memory repository is not available");
            if (agentId is null)
                return CreateErrorResponse("McpMemoryAgentId is required for MCPMEMORY_LIST_FOR_AGENT");
            try
            {
                var entries = McpMemoryRepo.ListForAgentAsync(agentId.Value).GetAwaiter().GetResult();
                var infos = entries.ConvertAll(ToMcpMemoryDto);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success     = true,
                    Command     = "MCPMEMORY_LIST_FOR_AGENT",
                    McpMemories = infos,
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPMEMORY_LIST_FOR_AGENT error: {ex.Message}"); }
        }

        // ── MCPMEMORY_LIST_SHARED ─────────────────────────────────────────────

        private string HandleMcpMemoryListShared()
        {
            if (McpMemoryRepo is null)
                return CreateErrorResponse("MCP Memory repository is not available");
            try
            {
                var entries = McpMemoryRepo.ListSharedAsync().GetAwaiter().GetResult();
                var infos = entries.ConvertAll(ToMcpMemoryDto);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success     = true,
                    Command     = "MCPMEMORY_LIST_SHARED",
                    McpMemories = infos,
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"MCPMEMORY_LIST_SHARED error: {ex.Message}"); }
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
