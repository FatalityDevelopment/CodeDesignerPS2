using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── GETSETTING ────────────────────────────────────────────────────────

        /// <summary>
        /// Handle GETSETTING — retrieves a single application setting from the database.
        /// </summary>
        /// <param name="key">Setting key (e.g. "ui.selectedGame").</param>
        private string HandleGetSetting(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return CreateErrorResponse("Key is required for GETSETTING");

            try
            {
                var value = SettingsRepo.GetAsync(key.Trim()).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "GETSETTING",
                    Message  = value,
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"GETSETTING failed: {ex.Message}");
            }
        }

        // ── SETSETTING ────────────────────────────────────────────────────────

        /// <summary>
        /// Handle SETSETTING — inserts or updates a setting in the database.
        /// </summary>
        /// <param name="key">Setting key.</param>
        /// <param name="value">Value to store. Pass null to clear.</param>
        private string HandleSetSetting(string? key, string? value)
        {
            if (string.IsNullOrWhiteSpace(key))
                return CreateErrorResponse("Key is required for SETSETTING");

            try
            {
                SettingsRepo.SetAsync(key.Trim(), value).GetAwaiter().GetResult();
                return CreateSuccessResponse($"Setting '{key}' saved.");
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"SETSETTING failed: {ex.Message}");
            }
        }

        // ── GETALLSETTINGS ────────────────────────────────────────────────────

        /// <summary>
        /// Handle GETALLSETTINGS — returns all settings (optionally filtered by key prefix)
        /// as a JSON map of key → value pairs.
        /// </summary>
        /// <param name="prefix">Optional key prefix filter (e.g. "emu." or "ui.").</param>
        private string HandleGetAllSettings(string? prefix)
        {
            try
            {
                var settings = SettingsRepo.GetAllAsync(prefix?.Trim()).GetAwaiter().GetResult();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Command  = "GETALLSETTINGS",
                    Data     = JsonSerializer.Serialize(settings),
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"GETALLSETTINGS failed: {ex.Message}");
            }
        }

        // ── DELETESETTING ─────────────────────────────────────────────────────

        /// <summary>
        /// Handle DELETESETTING — removes a setting from the database.
        /// </summary>
        /// <param name="key">Setting key to delete.</param>
        private string HandleDeleteSetting(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return CreateErrorResponse("Key is required for DELETESETTING");

            try
            {
                SettingsRepo.DeleteAsync(key.Trim()).GetAwaiter().GetResult();
                return CreateSuccessResponse($"Setting '{key}' deleted.");
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"DELETESETTING failed: {ex.Message}");
            }
        }
    }
}
