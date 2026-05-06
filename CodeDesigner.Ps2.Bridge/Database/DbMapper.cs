using System.Collections.Generic;
using System.Text.Json;

using CodeDesigner.Ps2.Database.Entities;

using MgrEntry = CodeDesigner.Ps2.LabelManager.Models.LabelEntry;
using MgrSet = CodeDesigner.Ps2.LabelManager.Models.LabelSet;
using MgrType = CodeDesigner.Ps2.LabelManager.Models.LabelType;

using DbLabel = CodeDesigner.Ps2.Database.Entities.Label;
using DbLabelSet = CodeDesigner.Ps2.Database.Entities.LabelSet;
using DbLabelType = CodeDesigner.Ps2.Database.Entities.LabelType;

namespace CodeDesigner.Ps2.Bridge.Database
{
    /// <summary>
    /// Maps between <c>CodeDesigner.Ps2.LabelManager</c> in-memory models and
    /// <c>CodeDesigner.Ps2.Database</c> entity types.
    ///
    /// Labels are recursive: child labels have <see cref="DbLabel.ParentId"/> set and their
    /// address is a synthetic path key, e.g. "0x80010000:+0x0010".
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
        /// <paramref name="labelSetId"/> must be the persisted ID of the owning set.
        /// <paramref name="parentId"/> is set when this entry is a child of another label.
        /// </summary>
        internal static DbLabel ToDbLabel(MgrEntry src, int labelSetId, int? parentId = null)
        {
            string? argJson    = src.ArgRegs    is { Count: > 0 } ? JsonSerializer.Serialize(src.ArgRegs,    _jsonOpts) : null;
            string? returnJson = src.ReturnRegs is { Count: > 0 } ? JsonSerializer.Serialize(src.ReturnRegs, _jsonOpts) : null;

            return new DbLabel
            {
                LabelSetId     = labelSetId,
                ParentId       = parentId,
                Offset         = src.Offset,
                Address        = src.Address,
                Name           = src.Name,
                Comment        = src.Comment,
                ValueType      = src.ValueType,
                Hidden         = src.Hidden,
                Dynamic        = src.Dynamic,
                Type           = ToDbLabelType(src.Type),
                ArgRegsJson    = argJson,
                ReturnRegsJson = returnJson,
                ElementStride  = src.ElementStride,
                ElementCount   = src.ElementCount,
                NodeNextOffset = src.NodeNextOffset,
                NodeSize       = src.NodeSize,
            };
        }

        /// <summary>
        /// Converts a DB <see cref="DbLabel"/> to a <see cref="MgrEntry"/>.
        /// The <paramref name="parentAddress"/> is the parent label's address string,
        /// used to populate <see cref="MgrEntry.ParentAddress"/> for child entries.
        /// </summary>
        internal static MgrEntry ToMgrEntry(DbLabel src, string? parentAddress = null)
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
                ParentAddress  = parentAddress,
                Offset         = src.Offset,
                Name           = src.Name,
                Comment        = src.Comment,
                ValueType      = src.ValueType,
                Hidden         = src.Hidden,
                Dynamic        = src.Dynamic,
                Type           = ToMgrLabelType(src.Type),
                ArgRegs        = argRegs,
                ReturnRegs     = returnRegs,
                ElementStride  = src.ElementStride,
                ElementCount   = src.ElementCount,
                NodeNextOffset = src.NodeNextOffset,
                NodeSize       = src.NodeSize,
            };
        }

        // ── LabelSet ───────────────────────────────────────────────────────────

        internal static DbLabelSet ToDbLabelSet(MgrSet src, int? gameDiscId, string? gameId = null) => new()
        {
            SetName    = src.SetName,
            GameDiscId = gameDiscId,
            GameId     = gameId ?? src.GameId,
            IsBuiltIn  = src.IsBuiltIn,
        };

        /// <summary>
        /// Converts a <see cref="DbLabelSet"/> (with fully loaded Labels including their
        /// Children) to a <see cref="MgrSet"/>.  The returned set contains a flat list of
        /// all entries (roots + children at all depths), each with <see cref="MgrEntry.ParentAddress"/>
        /// set for non-root entries.
        /// </summary>
        internal static MgrSet ToMgrSet(DbLabelSet src)
        {
            var entries = new List<MgrEntry>();
            // Build address lookup so children can reference their parent's address.
            var addressById = new Dictionary<int, string>();
            foreach (var lbl in src.Labels)
                addressById[lbl.Id] = lbl.Address;

            foreach (var lbl in src.Labels)
            {
                string? parentAddress = lbl.ParentId.HasValue && addressById.TryGetValue(lbl.ParentId.Value, out var pa) ? pa : null;
                entries.Add(ToMgrEntry(lbl, parentAddress));
            }

            return new MgrSet
            {
                SetName   = src.SetName,
                // Surface the disc's serial as the GameId so callers can resolve it.
                GameId    = src.GameDisc?.Serial ?? src.GameDiscId?.ToString() ?? src.GameId,
                IsBuiltIn = src.IsBuiltIn,
                Entries   = entries,
            };
        }
    }
}
