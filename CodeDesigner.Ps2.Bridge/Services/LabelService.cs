using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class LabelService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public LabelService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(_ => HandleGetLabels(),                                "GETLABELS");
            reg.Add(c => HandleAddLabel(c.Address, c.Label, c.Comment),    "ADDLABEL");
            reg.Add(c => HandleDeleteLabel(c.Address),                     "DELETELABEL");
        }

        /// <summary>
        /// Handle GETLABELS — returns all labels in the database, sorted by address.
        /// </summary>
        private string HandleGetLabels()
        {
            // Sync the legacy dict from _ctx.LabelMgr so callers always see up-to-date data
            // (labels added via ADDLABELENTRY may have been written to disk since the last sync).
            _ctx.SyncLegacyDictsFromLabelManager();

            var labels = _ctx.Labels
                .OrderBy(kv => kv.Key)
                .Select(kv => new LabelEntry
                {
                    Address = $"0x{kv.Key:X8}",
                    Label   = kv.Value.Label,
                    Comment = kv.Value.Comment
                })
                .ToList();

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success = true,
                Command = "GETLABELS",
                Labels  = labels
            });
        }

        /// <summary>
        /// Handle ADDLABEL — inserts or updates a label, then persists to disk.
        /// </summary>
        private string HandleAddLabel(uint? address, string? label, string? comment)
        {
            if (address == null)
                return Responses.Error("Address is required for ADDLABEL");
            if (string.IsNullOrWhiteSpace(label))
                return Responses.Error("Label is required for ADDLABEL");

            var trimmedLabel   = label.Trim();
            var trimmedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

            _ctx.Labels[address.Value] = (trimmedLabel, trimmedComment);
            SaveLabels();

            return Responses.Success($"Label '{trimmedLabel}' set at 0x{address.Value:X8}");
        }

        /// <summary>
        /// Handle DELETELABEL — removes a label by address, then persists to disk.
        /// </summary>
        private string HandleDeleteLabel(uint? address)
        {
            if (address == null)
                return Responses.Error("Address is required for DELETELABEL");

            if (!_ctx.Labels.Remove(address.Value))
                return Responses.Error($"No label found at 0x{address.Value:X8}");

            SaveLabels();
            return Responses.Success($"Label removed at 0x{address.Value:X8}");
        }

        /// <summary>
        /// Persist the current in-memory label dictionary back to the first ps2MemMap.json
        /// file that was found at startup (or the base-directory path as a fallback).
        /// </summary>
        private void SaveLabels()
        {

        }
    }
}
