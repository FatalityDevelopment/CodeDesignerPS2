using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using CodeDesigner.Ps2.Database.Entities;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── MCPCONFIG_LIST ────────────────────────────────────────────────────

        /// <summary>
        /// MCPCONFIG_LIST — return all MCP configuration entries (tool descriptions).
        /// </summary>
        private string HandleMcpConfigList()
        {
            if (Db is null)
                return CreateErrorResponse("Database is not available");

            try
            {
                var configs = Db.McpConfigs
                                .OrderBy(c => c.Key)
                                .AsNoTracking()
                                .ToList();

                var infos = configs.Select(c => new McpConfigInfo
                {
                    Id          = c.Id,
                    Key         = c.Key,
                    Value       = c.Value,
                    Description = c.Description,
                    UpdatedAt   = c.UpdatedAt.ToString("o"),
                }).ToList();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success    = true,
                    Command    = "MCPCONFIG_LIST",
                    McpConfigs = infos,
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"MCPCONFIG_LIST failed: {ex.Message}");
            }
        }

        // ── MCPCONFIG_UPDATE ──────────────────────────────────────────────────

        /// <summary>
        /// MCPCONFIG_UPDATE — update the value (tool description) for an existing
        /// MCP config entry identified by its key.
        /// </summary>
        private string HandleMcpConfigUpdate(string? key, string? value)
        {
            if (Db is null)
                return CreateErrorResponse("Database is not available");

            key = RequireParam(key, "McpConfigKey", out var e1);
            if (key is null) return CreateErrorResponse(e1!);

            try
            {
                var config = Db.McpConfigs.FirstOrDefault(c => c.Key == key);
                if (config is null)
                    return CreateErrorResponse($"MCP config key '{key}' not found.");

                config.Value     = value;
                config.UpdatedAt = DateTime.UtcNow;
                Db.SaveChanges();

                var info = new McpConfigInfo
                {
                    Id          = config.Id,
                    Key         = config.Key,
                    Value       = config.Value,
                    Description = config.Description,
                    UpdatedAt   = config.UpdatedAt.ToString("o"),
                };

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success   = true,
                    Command   = "MCPCONFIG_UPDATE",
                    McpConfig = info,
                    Message   = $"Updated tool description for '{key}'.",
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"MCPCONFIG_UPDATE failed: {ex.Message}");
            }
        }
    }
}
