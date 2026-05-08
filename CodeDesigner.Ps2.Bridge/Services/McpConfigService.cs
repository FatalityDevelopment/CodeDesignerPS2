using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using CodeDesigner.Ps2.Database.Entities;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class McpConfigService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public McpConfigService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(_ => HandleMcpConfigList(),                            "MCPCONFIG_LIST", "MCPCONFIGLIST");
            reg.Add(c => HandleMcpConfigUpdate(c.McpConfigKey, c.McpConfigValue), "MCPCONFIG_UPDATE", "MCPCONFIGUPDATE");
        }

        // ── MCPCONFIG_LIST ────────────────────────────────────────────────────

        /// <summary>
        /// MCPCONFIG_LIST — return all MCP configuration entries (tool descriptions).
        /// </summary>
        private string HandleMcpConfigList()
        {
            if (_ctx.Db is null)
                return Responses.Error("Database is not available");

            try
            {
                var configs = _ctx.Db.McpConfigs
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
                return Responses.Error($"MCPCONFIG_LIST failed: {ex.Message}");
            }
        }

        // ── MCPCONFIG_UPDATE ──────────────────────────────────────────────────

        /// <summary>
        /// MCPCONFIG_UPDATE — update the value (tool description) for an existing
        /// MCP config entry identified by its key.
        /// </summary>
        private string HandleMcpConfigUpdate(string? key, string? value)
        {
            if (_ctx.Db is null)
                return Responses.Error("Database is not available");

            if (string.IsNullOrWhiteSpace(key))
                return Responses.Error("McpConfigKey is required");
            key = key.Trim();

            try
            {
                var config = _ctx.Db.McpConfigs.FirstOrDefault(c => c.Key == key);
                if (config is null)
                    return Responses.Error($"MCP config key '{key}' not found.");

                config.Value     = value;
                config.UpdatedAt = DateTime.UtcNow;
                _ctx.Db.SaveChanges();

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
                return Responses.Error($"MCPCONFIG_UPDATE failed: {ex.Message}");
            }
        }
    }
}
