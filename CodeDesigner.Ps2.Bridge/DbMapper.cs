using System.Collections.Generic;
using System.Text.Json;

using CodeDesigner.Ps2.Database.Entities;

using MgrEntry  = CodeDesigner.Ps2.LabelManager.Models.LabelEntry;
using MgrMember = CodeDesigner.Ps2.LabelManager.Models.LabelMember;
using MgrSet    = CodeDesigner.Ps2.LabelManager.Models.LabelSet;
using MgrType   = CodeDesigner.Ps2.LabelManager.Models.LabelType;

using DbLabel       = CodeDesigner.Ps2.Database.Entities.Label;
using DbLabelMember = CodeDesigner.Ps2.Database.Entities.LabelMember;
using DbLabelSet    = CodeDesigner.Ps2.Database.Entities.LabelSet;
using DbLabelType   = CodeDesigner.Ps2.Database.Entities.LabelType;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Maps between <c>CodeDesigner.Ps2.LabelManager</c> in-memory models and
    /// <c>CodeDesigner.Ps2.Database</c> entity types.
    ///
    /// Socket-wire DTOs (<see cref="LabelEntry"/>, <see cref="StructEntry"/>,
    /// <see cref="SnapshotRegion"/> …) are kept completely separate and are
    /// handled by <c>LabelManagerHandlers.cs</c> alone.
    /// </summary>
    internal static class DbMapper
    {
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNamingPolicy = null,
        };

        // ── LabelType ──────────────────────────────────────────────────────────

        internal static DbLabelType ToDbLabelType(MgrType t) => (DbLabelType)(int)t;
        internal static MgrType     ToMgrLabelType(DbLabelType t) => (MgrType)(int)t;

        // ── Label ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Converts a <see cref="MgrEntry"/> to a new <see cref="DbLabel"/>.
        /// The <paramref name="labelSetId"/> must be the persisted ID of the owning <see cref="DbLabelSet"/>.
        /// </summary>
        internal static DbLabel ToDbLabel(MgrEntry src, int labelSetId)
        {
            string? argJson    = src.ArgRegs    is { Count: > 0 } ? JsonSerializer.Serialize(src.ArgRegs,    _jsonOpts) : null;
            string? returnJson = src.ReturnRegs is { Count: > 0 } ? JsonSerializer.Serialize(src.ReturnRegs, _jsonOpts) : null;

            return new DbLabel
            {
                LabelSetId     = labelSetId,
                Address        = src.Address,
                Name           = src.Name,
                Comment        = src.Comment,
                Hidden         = src.Hidden,
                Type           = ToDbLabelType(src.Type),
                ArgRegsJson    = argJson,
                ReturnRegsJson = returnJson,
                ElementStride  = src.ElementStride,
                ElementCount   = src.ElementCount,
                NodeNextOffset = src.NodeNextOffset,
                NodeSize       = src.NodeSize,
                Members        = [.. src.Members.Select(ToDbLabelMember)],
            };
        }

        internal static DbLabelMember ToDbLabelMember(MgrMember src) => new()
        {
            Offset  = src.Offset,
            Name    = src.Name,
            Dynamic = src.Dynamic,
        };

        /// <summary>Converts a DB <see cref="DbLabel"/> (with loaded Members) to a <see cref="MgrEntry"/>.</summary>
        internal static MgrEntry ToMgrEntry(DbLabel src)
        {
            List<string>? argRegs    = null;
            List<string>? returnRegs = null;

            if (!string.IsNullOrWhiteSpace(src.ArgRegsJson))
            {
                try { argRegs = JsonSerializer.Deserialize<List<string>>(src.ArgRegsJson, _jsonOpts); }
                catch { /* ignore malformed JSON */ }
            }

            if (!string.IsNullOrWhiteSpace(src.ReturnRegsJson))
            {
                try { returnRegs = JsonSerializer.Deserialize<List<string>>(src.ReturnRegsJson, _jsonOpts); }
                catch { /* ignore malformed JSON */ }
            }

            return new MgrEntry
            {
                Address        = src.Address,
                Name           = src.Name,
                Comment        = src.Comment,
                Hidden         = src.Hidden,
                Type           = ToMgrLabelType(src.Type),
                ArgRegs        = argRegs,
                ReturnRegs     = returnRegs,
                ElementStride  = src.ElementStride,
                ElementCount   = src.ElementCount,
                NodeNextOffset = src.NodeNextOffset,
                NodeSize       = src.NodeSize,
                Members        = [.. src.Members.Select(ToMgrMember)],
            };
        }

        internal static MgrMember ToMgrMember(DbLabelMember src) => new()
        {
            Offset  = src.Offset,
            Name    = src.Name,
            Dynamic = src.Dynamic,
        };

        // ── LabelSet ───────────────────────────────────────────────────────────

        /// <summary>
        /// Converts a <see cref="MgrSet"/> to a new <see cref="DbLabelSet"/>.
        /// The returned entity has no persisted <c>Id</c>; the caller is responsible
        /// for inserting it into the database.
        /// </summary>
        internal static DbLabelSet ToDbLabelSet(MgrSet src, string gameId) => new()
        {
            SetName   = src.SetName,
            GameId    = gameId,
            IsBuiltIn = src.IsBuiltIn,
        };

        /// <summary>
        /// Converts a <see cref="DbLabelSet"/> (with loaded Labels and their Members) to a <see cref="MgrSet"/>.
        /// </summary>
        internal static MgrSet ToMgrSet(DbLabelSet src) => new()
        {
            SetName   = src.SetName,
            GameId    = src.GameId,
            IsBuiltIn = src.IsBuiltIn,
            Entries   = [.. src.Labels.Select(ToMgrEntry)],
        };
    }
}
