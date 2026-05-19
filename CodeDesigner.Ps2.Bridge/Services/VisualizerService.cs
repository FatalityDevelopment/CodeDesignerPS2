using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Backs the Structure Visualizer webview. Two responsibilities:
    /// <list type="bullet">
    ///   <item><b>Label groups</b> — the cross-cutting "objectmap": classes,
    ///         modules, subsystems and vtables that collect scattered labels.</item>
    ///   <item><b>Call graph</b> — a persisted set of caller→callee edges scanned
    ///         from <c>JAL</c>/<c>JALR</c> instructions, queried back as a subgraph
    ///         around a focus address ("call chain back to a hook").</item>
    /// </list>
    /// All commands take their parameters as a JSON object in the message
    /// <see cref="CommandMessage.Data"/> field and return their results as a JSON
    /// object in <see cref="CommandResponse.Data"/>.
    /// </summary>
    internal sealed class VisualizerService : ICommandService
    {
        // EE main RAM is 32 MB; used to sanity-check JAL targets.
        private const long EeRamSize = 32L * 1024 * 1024;
        private const int  MaxScanLength = 32 * 1024 * 1024;

        /// <summary>In-memory call edge used while walking the graph.</summary>
        private readonly record struct Cg(long Caller, long Callee, CallEdgeKind Kind);

        private readonly IBridgeContext _ctx;
        public VisualizerService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleGetLabelGroups(c.Data),         "GETLABELGROUPS");
            reg.Add(c => HandleAddLabelGroup(c.Data),          "ADDLABELGROUP");
            reg.Add(c => HandleDeleteLabelGroup(c.Data),       "DELETELABELGROUP");
            reg.Add(c => HandleAddLabelGroupMember(c.Data),    "ADDLABELGROUPMEMBER");
            reg.Add(c => HandleDeleteLabelGroupMember(c.Data), "DELETELABELGROUPMEMBER");
            reg.Add(c => HandleScanCallGraph(c.Data),          "SCANCALLGRAPH");
            reg.Add(c => HandleGetCallGraph(c.Data),           "GETCALLGRAPH");
        }

        // ── Label groups ─────────────────────────────────────────────────────

        private string HandleGetLabelGroups(string? data)
        {
            if (!TryParse(data, out var doc)) return Responses.Error("GETLABELGROUPS requires a JSON Data object.");
            using (doc)
            {
                var root = doc.RootElement;
                var set  = ResolveSet(Str(root, "gameId"), Str(root, "setName"));
                if (set is null) return Responses.Error("Label set not found for the given game / set name.");

                var groups   = _ctx.Db.LabelGroups.Where(g => g.LabelSetId == set.Id).ToList();
                var groupIds = groups.Select(g => g.Id).ToList();
                var members  = _ctx.Db.LabelGroupMembers
                    .Where(m => groupIds.Contains(m.LabelGroupId))
                    .Join(_ctx.Db.Labels, m => m.LabelId, l => l.Id, (m, l) => new { m, l })
                    .ToList();

                var payload = new
                {
                    groups = groups.Select(g => new
                    {
                        id      = g.Id,
                        name    = g.Name,
                        kind    = g.Kind.ToString(),
                        comment = g.Comment,
                        color   = g.Color,
                        members = members
                            .Where(x => x.m.LabelGroupId == g.Id)
                            .OrderBy(x => x.m.SortOrder)
                            .Select(x => new
                            {
                                id           = x.m.Id,
                                labelId      = x.l.Id,
                                labelAddress = x.l.Address,
                                labelName    = x.l.Name,
                                labelType    = x.l.Type.ToString(),
                                role         = x.m.Role,
                                sortOrder    = x.m.SortOrder,
                            })
                            .ToList(),
                    }).ToList(),
                };

                return JsonResponse("GETLABELGROUPS", payload);
            }
        }

        private string HandleAddLabelGroup(string? data)
        {
            if (!TryParse(data, out var doc)) return Responses.Error("ADDLABELGROUP requires a JSON Data object.");
            using (doc)
            {
                var root = doc.RootElement;
                var name = Str(root, "name")?.Trim();
                if (string.IsNullOrWhiteSpace(name)) return Responses.Error("'name' is required for ADDLABELGROUP.");

                var set = ResolveSet(Str(root, "gameId"), Str(root, "setName"));
                if (set is null) return Responses.Error("Label set not found for the given game / set name.");

                var group = _ctx.Db.LabelGroups.FirstOrDefault(g => g.LabelSetId == set.Id && g.Name == name);
                if (group is null)
                {
                    group = new LabelGroup { LabelSetId = set.Id, Name = name };
                    _ctx.Db.LabelGroups.Add(group);
                }
                group.Kind    = ParseGroupKind(Str(root, "kind"));
                group.Comment = Str(root, "comment");
                group.Color   = Str(root, "color");
                _ctx.Db.SaveChanges();

                return JsonResponse("ADDLABELGROUP", new { id = group.Id, name = group.Name });
            }
        }

        private string HandleDeleteLabelGroup(string? data)
        {
            if (!TryParse(data, out var doc)) return Responses.Error("DELETELABELGROUP requires a JSON Data object.");
            using (doc)
            {
                var root  = doc.RootElement;
                var group = ResolveGroup(root);
                if (group is null) return Responses.Error("Label group not found.");

                _ctx.Db.LabelGroups.Remove(group);
                _ctx.Db.SaveChanges();
                return Responses.Success($"Label group '{group.Name}' removed.");
            }
        }

        private string HandleAddLabelGroupMember(string? data)
        {
            if (!TryParse(data, out var doc)) return Responses.Error("ADDLABELGROUPMEMBER requires a JSON Data object.");
            using (doc)
            {
                var root  = doc.RootElement;
                var group = ResolveGroup(root);
                if (group is null) return Responses.Error("Label group not found.");

                var addr = Str(root, "labelAddress");
                if (string.IsNullOrWhiteSpace(addr)) return Responses.Error("'labelAddress' is required.");
                var norm = NormalizeAddr(addr);

                var label = _ctx.Db.Labels.FirstOrDefault(l => l.LabelSetId == group.LabelSetId && l.Address == norm);
                if (label is null) return Responses.Error($"No label at {norm} in this set.");

                var existing = _ctx.Db.LabelGroupMembers
                    .FirstOrDefault(m => m.LabelGroupId == group.Id && m.LabelId == label.Id);
                if (existing is null)
                {
                    existing = new LabelGroupMember { LabelGroupId = group.Id, LabelId = label.Id };
                    _ctx.Db.LabelGroupMembers.Add(existing);
                }
                existing.Role      = Str(root, "role");
                existing.SortOrder = Int(root, "sortOrder") ?? 0;
                _ctx.Db.SaveChanges();

                return Responses.Success($"Label '{label.Name}' added to group '{group.Name}'.");
            }
        }

        private string HandleDeleteLabelGroupMember(string? data)
        {
            if (!TryParse(data, out var doc)) return Responses.Error("DELETELABELGROUPMEMBER requires a JSON Data object.");
            using (doc)
            {
                var root  = doc.RootElement;
                var group = ResolveGroup(root);
                if (group is null) return Responses.Error("Label group not found.");

                var addr = Str(root, "labelAddress");
                if (string.IsNullOrWhiteSpace(addr)) return Responses.Error("'labelAddress' is required.");
                var norm = NormalizeAddr(addr);

                var member = _ctx.Db.LabelGroupMembers
                    .Where(m => m.LabelGroupId == group.Id)
                    .Join(_ctx.Db.Labels, m => m.LabelId, l => l.Id, (m, l) => new { m, l })
                    .FirstOrDefault(x => x.l.Address == norm);
                if (member is null) return Responses.Error($"No member at {norm} in group '{group.Name}'.");

                _ctx.Db.LabelGroupMembers.Remove(member.m);
                _ctx.Db.SaveChanges();
                return Responses.Success($"Member {norm} removed from group '{group.Name}'.");
            }
        }

        // ── Call graph ───────────────────────────────────────────────────────

        private string HandleScanCallGraph(string? data)
        {
            if (!TryParse(data, out var doc)) return Responses.Error("SCANCALLGRAPH requires a JSON Data object.");
            using (doc)
            {
                var root = doc.RootElement;
                if (_ctx.Client is null)
                    return Responses.Error("No live memory backend — start with --run or --pcsx2.");

                var baseAddr = ParseAddr(Str(root, "address"));
                int length   = Int(root, "length") ?? 0;
                if (baseAddr is null) return Responses.Error("'address' (code segment start) is required.");
                if (length <= 0 || length > MaxScanLength)
                    return Responses.Error($"'length' must be between 1 and {MaxScanLength}.");

                bool useIop = string.Equals(Str(root, "region"), "iop", StringComparison.OrdinalIgnoreCase);
                int? discId = ResolveDisc(Str(root, "gameId"));

                byte[] bytes;
                try { bytes = _ctx.Memory.ReadLiveBytesChunked(baseAddr.Value, length, useIop); }
                catch (Exception ex) { return Responses.Error($"Memory read failed: {ex.Message}"); }

                var edges = new List<CallEdge>();
                var now   = DateTime.UtcNow;
                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    uint instr  = BitConverter.ToUInt32(bytes, i);
                    uint opcode = (instr >> 26) & 0x3Fu;
                    uint pc     = baseAddr.Value + (uint)i;

                    // JAL — direct call, target encoded in the instruction.
                    if (opcode == 0x03u)
                    {
                        uint target = ((instr & 0x03FFFFFFu) << 2) | (pc & 0xF0000000u);
                        if (IsRamAddr(target))
                            edges.Add(new CallEdge
                            {
                                GameDiscId    = discId,
                                CallerAddress = pc,
                                CalleeAddress = target,
                                Kind          = CallEdgeKind.Direct,
                                UpdatedAt     = now,
                            });
                    }
                    // SPECIAL / JALR — indirect call; target is register-held.
                    else if (opcode == 0x00u && (instr & 0x3Fu) == 0x09u)
                    {
                        edges.Add(new CallEdge
                        {
                            GameDiscId    = discId,
                            CallerAddress = pc,
                            CalleeAddress = 0,
                            Kind          = CallEdgeKind.Indirect,
                            UpdatedAt     = now,
                        });
                    }
                }

                // Replace the disc's edge set so a rescan is idempotent.
                var stale = _ctx.Db.CallEdges.Where(e => e.GameDiscId == discId).ToList();
                if (stale.Count > 0) _ctx.Db.CallEdges.RemoveRange(stale);
                _ctx.Db.CallEdges.AddRange(edges);
                _ctx.Db.SaveChanges();

                int direct   = edges.Count(e => e.Kind == CallEdgeKind.Direct);
                int indirect = edges.Count - direct;
                return JsonResponse("SCANCALLGRAPH", new
                {
                    totalEdges    = edges.Count,
                    directEdges   = direct,
                    indirectEdges = indirect,
                });
            }
        }

        private string HandleGetCallGraph(string? data)
        {
            if (!TryParse(data, out var doc)) return Responses.Error("GETCALLGRAPH requires a JSON Data object.");
            using (doc)
            {
                var root  = doc.RootElement;
                var focus = ParseAddr(Str(root, "address"));
                if (focus is null) return Responses.Error("'address' (focus function) is required.");

                int    depth     = Math.Clamp(Int(root, "depth") ?? 3, 1, 12);
                string direction = (Str(root, "direction") ?? "both").ToLowerInvariant();
                int?   discId    = ResolveDisc(Str(root, "gameId"));

                var all = _ctx.Db.CallEdges
                    .Where(e => e.GameDiscId == discId && e.CalleeAddress != 0)
                    .Select(e => new { e.CallerAddress, e.CalleeAddress, e.Kind })
                    .ToList()
                    .Select(e => new Cg(e.CallerAddress, e.CalleeAddress, e.Kind))
                    .ToList();

                var forward = all.GroupBy(e => e.Caller)
                                 .ToDictionary(g => g.Key, g => g.ToList());
                var reverse = all.GroupBy(e => e.Callee)
                                 .ToDictionary(g => g.Key, g => g.ToList());

                var nodes      = new HashSet<long> { focus.Value };
                var edgeSet    = new HashSet<(long, long)>();
                var resultEdge = new List<(long From, long To, CallEdgeKind Kind)>();

                if (direction is "callees" or "both")
                    Walk(focus.Value, depth, forward, isForward: true,  nodes, edgeSet, resultEdge);
                if (direction is "callers" or "both")
                    Walk(focus.Value, depth, reverse, isForward: false, nodes, edgeSet, resultEdge);

                var payload = new
                {
                    focus     = Hex(focus.Value),
                    depth,
                    direction,
                    nodes     = nodes.OrderBy(n => n).Select(Hex).ToList(),
                    edges     = resultEdge.Select(e => new
                    {
                        from = Hex(e.From),
                        to   = Hex(e.To),
                        kind = e.Kind.ToString(),
                    }).ToList(),
                };
                return JsonResponse("GETCALLGRAPH", payload);
            }
        }

        /// <summary>Breadth-first walk over the call graph up to <paramref name="depth"/> hops.</summary>
        private static void Walk(
            long start, int depth,
            Dictionary<long, List<Cg>> adjacency, bool isForward,
            HashSet<long> nodes, HashSet<(long, long)> edgeSet,
            List<(long, long, CallEdgeKind)> edges)
        {
            var frontier = new Queue<(long Addr, int Hop)>();
            frontier.Enqueue((start, 0));
            var seen = new HashSet<long> { start };

            while (frontier.Count > 0)
            {
                var (addr, hop) = frontier.Dequeue();
                if (hop >= depth) continue;
                if (!adjacency.TryGetValue(addr, out var neighbours)) continue;

                foreach (var n in neighbours)
                {
                    long caller = n.Caller;
                    long callee = n.Callee;
                    var  kind   = n.Kind;
                    long next   = isForward ? callee : caller;

                    nodes.Add(next);
                    if (edgeSet.Add((caller, callee)))
                        edges.Add((caller, callee, kind));

                    if (seen.Add(next))
                        frontier.Enqueue((next, hop + 1));
                }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private LabelGroup? ResolveGroup(JsonElement root)
        {
            int? id = Int(root, "groupId");
            if (id is > 0)
                return _ctx.Db.LabelGroups.FirstOrDefault(g => g.Id == id);

            var name = Str(root, "groupName") ?? Str(root, "name");
            if (string.IsNullOrWhiteSpace(name)) return null;
            var set = ResolveSet(Str(root, "gameId"), Str(root, "setName"));
            return set is null
                ? null
                : _ctx.Db.LabelGroups.FirstOrDefault(g => g.LabelSetId == set.Id && g.Name == name);
        }

        private LabelSet? ResolveSet(string? gameId, string? setName)
        {
            if (string.IsNullOrWhiteSpace(setName)) return null;
            int? discId = ResolveDisc(gameId);
            return _ctx.Db.LabelSets.FirstOrDefault(ls => ls.GameDiscId == discId && ls.SetName == setName);
        }

        private int? ResolveDisc(string? gameId)
            => string.IsNullOrWhiteSpace(gameId)
                ? null
                : _ctx.LabelRepo.ResolveGameDiscIdAsync(gameId).GetAwaiter().GetResult();

        private static bool IsRamAddr(uint vaddr)
        {
            uint phys = vaddr >= 0x80000000u && vaddr < 0xC0000000u ? vaddr & 0x1FFFFFFFu : vaddr;
            return phys < EeRamSize;
        }

        private static string Hex(long addr) => $"0x{(uint)addr:X8}";

        private static LabelGroupKind ParseGroupKind(string? s)
            => Enum.TryParse<LabelGroupKind>(s, ignoreCase: true, out var k) ? k : LabelGroupKind.Class;

        private static string NormalizeAddr(string raw)
        {
            raw = raw.Trim();
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) raw = raw[2..];
            return uint.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)
                ? $"0x{v:X8}"
                : raw;
        }

        private static uint? ParseAddr(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim();
            bool hex = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (hex) raw = raw[2..];
            return uint.TryParse(raw, hex ? NumberStyles.HexNumber : NumberStyles.Integer,
                                 CultureInfo.InvariantCulture, out uint v)
                ? v
                : (uint?)null;
        }

        private static bool TryParse(string? data, out JsonDocument doc)
        {
            doc = null!;
            if (string.IsNullOrWhiteSpace(data)) return false;
            try
            {
                var parsed = JsonDocument.Parse(data);
                if (parsed.RootElement.ValueKind != JsonValueKind.Object) { parsed.Dispose(); return false; }
                doc = parsed;
                return true;
            }
            catch { return false; }
        }

        private static string? Str(JsonElement o, string key)
            => o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static int? Int(JsonElement o, string key)
            => o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

        private static string JsonResponse(string command, object payload)
            => Responses.Json(new CommandResponse
            {
                Success = true,
                Command = command,
                Data    = JsonSerializer.Serialize(payload),
            });
    }
}
