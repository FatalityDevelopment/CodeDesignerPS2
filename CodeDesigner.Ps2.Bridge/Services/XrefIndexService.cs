using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.Common.CPU;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Cross-reference index: every place in a memory region that branches to,
    /// calls, jumps to, builds (<c>lui</c>+<c>addiu</c>/<c>ori</c>), loads from,
    /// stores to or holds a pointer to an address.
    ///
    /// <para>
    /// The region is read once and indexed, so "who references X" is a binary
    /// search instead of a 32 MB read per question, and the disassembler can show
    /// an incoming-reference count on every row. The snapshot is kept so ASCII /
    /// CRC32 pattern lookups (<see cref="ReferenceScanner"/>) run on it too.
    /// The index reflects memory when it was built; rebuild it after the game
    /// loads new code (XREFINDEX with <c>XrefRebuild</c>).
    /// </para>
    ///
    /// Commands:
    /// <list type="bullet">
    ///   <item><c>XREFINDEX</c> — build (in the background unless <c>XrefWait</c>) or report status.</item>
    ///   <item><c>XREFSTO</c> — references to <c>TargetAddress</c>, building the index first if needed.</item>
    /// </list>
    /// </summary>
    internal sealed class XrefIndexService : ICommandService
    {
        private const int MaxResults = 1000;

        internal enum XrefKind : byte { Call, Jump, Branch, RegisterJump, Address, Load, Store, Pointer }

        private readonly struct Xref
        {
            public readonly uint Target;
            public readonly uint From;
            public readonly XrefKind Kind;
            public Xref(uint target, uint from, XrefKind kind) { Target = target; From = from; Kind = kind; }
        }

        private sealed class RegionIndex
        {
            public required string Region { get; init; }
            public required uint Base { get; init; }
            public required byte[] Bytes { get; init; }
            public required Xref[] Refs { get; init; }   // sorted by Target, then From
            public required DateTime BuiltUtc { get; init; }
            /// <summary>Function entry points, ascending (normalized addresses).</summary>
            public uint[] FuncStarts { get; init; } = [];
            /// <summary>Last instruction of each function in <see cref="FuncStarts"/> (inclusive).</summary>
            public uint[] FuncEnds { get; init; } = [];
        }

        private readonly IBridgeContext _ctx;
        private readonly object _gate = new();
        private readonly Dictionary<string, RegionIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Task> _building = new(StringComparer.OrdinalIgnoreCase);

        public XrefIndexService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleIndex(c.Region, c.Address, c.Length, c.XrefRebuild == true, c.XrefWait == true), "XREFINDEX");
            reg.Add(c => HandleXrefsTo(c.TargetAddress, c.Region, c.ReferenceKinds), "XREFSTO", "XREFS_TO");
            reg.Add(c => HandleFunctionAt(c.TargetAddress ?? c.Address), "FUNCTIONAT", "FUNCTION_AT");
        }

        private string HandleFunctionAt(uint? address)
        {
            if (!address.HasValue)
                return Responses.Error("TargetAddress is required for FUNCTIONAT");
            if (!HasIndex)
                return Responses.Error("No cross-reference index yet — run XREFINDEX first");
            if (!TryGetFunction(address.Value, out uint start, out uint end))
                return Responses.Error($"0x{address.Value:X8} is not inside a detected function");

            var (callers, pointers) = CountTo(start);
            return JsonSerializer.Serialize(new CommandResponse
            {
                Success = true,
                Command = "FUNCTIONAT",
                Message = $"Function 0x{start:X8}-0x{end:X8} ({end - start + 4} bytes, {(end - start) / 4 + 1} instructions); "
                        + $"{callers} code reference(s), {pointers} data pointer(s) to its entry",
            });
        }

        // ── Public queries (used by the disassembler) ────────────────────────

        /// <summary>
        /// Incoming references to <paramref name="address"/> across every built index:
        /// code references (calls, jumps, branches, built addresses, loads, stores)
        /// and data pointers. Both zero when no index has been built.
        /// </summary>
        public (int Code, int Data) CountTo(uint address)
        {
            RegionIndex[] indexes;
            lock (_gate) indexes = _indexes.Values.ToArray();
            int code = 0, data = 0;
            uint target = Normalize(address, iopContext: false);
            foreach (var index in indexes)
            {
                var (lo, hi) = Range(index.Refs, target);
                for (int i = lo; i < hi; i++)
                {
                    if (index.Refs[i].Kind == XrefKind.Pointer) data++;
                    else code++;
                }
            }
            return (code, data);
        }

        /// <summary>True once at least one region has been indexed.</summary>
        public bool HasIndex { get { lock (_gate) return _indexes.Count > 0; } }

        // ── Commands ─────────────────────────────────────────────────────────

        private string HandleIndex(string? region, uint? address, int? length, bool rebuild, bool wait)
        {
            var (key, start, size) = ResolveRegion(region, address, length);
            if (_ctx.Client == null)
                return Status($"No live memory backend; {Describe()}");

            Task build = StartBuild(key, start, size, rebuild);
            if (wait)
            {
                try { build.Wait(); }
                catch (AggregateException ex) { return Responses.Error($"XREFINDEX failed: {ex.InnerException?.Message}"); }
            }
            return Status(Describe());
        }

        private string HandleXrefsTo(uint? targetAddress, string? region, List<string>? kinds)
        {
            if (!targetAddress.HasValue)
                return Responses.Error("TargetAddress is required for XREFSTO");
            if (_ctx.Client == null && !HasIndex)
                return Responses.Error("No live memory backend — start with --pcsx2 or STARTEMULATOR");

            try
            {
                // Make sure the region holding the target's referrers is indexed.
                var (key, start, size) = ResolveRegion(region ?? RegionFor(targetAddress.Value), null, null);
                RegionIndex? own;
                lock (_gate) _indexes.TryGetValue(key, out own);
                if (own == null && _ctx.Client != null)
                    StartBuild(key, start, size, rebuild: false).Wait();

                var scanKinds = ReferenceScanner.ParseKinds(kinds);
                RegionIndex[] indexes;
                lock (_gate) indexes = _indexes.Values.ToArray();

                var entries = new List<ReferenceEntry>();
                uint target = Normalize(targetAddress.Value, iopContext: false);
                foreach (var index in indexes)
                {
                    var (lo, hi) = Range(index.Refs, target);
                    for (int i = lo; i < hi && entries.Count < MaxResults; i++)
                    {
                        var x = index.Refs[i];
                        if (!Wanted(x.Kind, scanKinds)) continue;
                        entries.Add(ToEntry(index, x, targetAddress.Value));
                    }

                    // ASCII / CRC32 matches of the string at the target, from the snapshot.
                    if (scanKinds.HasFlag(ReferenceScanKinds.StringPattern))
                    {
                        var patterns = ReferenceScanner.Scan(new ReferenceScanInput
                        {
                            TargetAddress = targetAddress.Value,
                            BaseAddress = index.Base,
                            Bytes = index.Bytes,
                            Kinds = ReferenceScanKinds.StringPattern,
                        });
                        foreach (var p in patterns.Take(MaxResults - entries.Count))
                            entries.Add(ToEntry(p));
                    }
                }

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "XREFSTO",
                    References = entries,
                    Message = entries.Count >= MaxResults
                        ? $"First {MaxResults} reference(s) to 0x{targetAddress.Value:X8}"
                        : $"Found {entries.Count} reference(s) to 0x{targetAddress.Value:X8}",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"XREFSTO error: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        // ── Building ─────────────────────────────────────────────────────────

        private Task StartBuild(string key, uint start, int size, bool rebuild)
        {
            lock (_gate)
            {
                if (_building.TryGetValue(key, out var running)) return running;
                if (!rebuild && _indexes.ContainsKey(key)) return Task.CompletedTask;

                bool useIop = key.StartsWith("iop", StringComparison.OrdinalIgnoreCase);
                var task = Task.Run(() =>
                {
                    try
                    {
                        byte[] bytes = _ctx.Memory.ReadLiveBytesChunked(start, size, useIop);
                        var index = Build(key, start, bytes, useIop);
                        lock (_gate) _indexes[key] = index;
                        Console.WriteLine($"[Xref] Indexed {key}: {index.Refs.Length:N0} references");
                    }
                    finally
                    {
                        lock (_gate) _building.Remove(key);
                    }
                });
                _building[key] = task;
                return task;
            }
        }

        private static RegionIndex Build(string key, uint start, byte[] bytes, bool iop)
        {
            var refs = new List<Xref>(bytes.Length / 16);
            var known = new bool[32];
            var values = new uint[32];
            known[0] = true;
            bool resetAfterSlot = false;

            void Add(uint target, uint from, XrefKind kind)
            {
                uint t = Normalize(target, iop);
                if (IsPlausibleTarget(t, kind)) refs.Add(new Xref(t, from, kind));
            }

            for (int i = 0; i + 3 < bytes.Length; i += 4)
            {
                uint w = BitConverter.ToUInt32(bytes, i);
                uint pc = start + (uint)i;

                // Every aligned word that looks like an address is a possible pointer.
                if ((w & 3) == 0 && w != 0) Add(w, pc, XrefKind.Pointer);

                uint op = w >> 26;
                int rs = (int)((w >> 21) & 0x1F);
                int rt = (int)((w >> 16) & 0x1F);
                var usage = GprUsage.Of(w);

                // Static control flow.
                if (op is 0x02 or 0x03)
                {
                    Add(CpuHelpers.JumpTarget(pc, w), pc, op == 0x03 ? XrefKind.Call : XrefKind.Jump);
                }
                else if (op != 0x00 && usage.Flow != FlowKind.None)
                {
                    XrefKind kind = usage.Flow switch
                    {
                        FlowKind.Call => XrefKind.Call,
                        FlowKind.Jump => XrefKind.Jump,
                        _ => XrefKind.Branch,
                    };
                    Add(CpuHelpers.BranchTarget(pc, (int)(w & 0xFFFF)), pc, kind);
                }
                else if (op == 0x00 && ((w & 0x3F) is 0x08 or 0x09) && known[rs])
                {
                    Add(values[rs], pc, (w & 0x3F) == 0x09 ? XrefKind.Call : XrefKind.RegisterJump);
                }

                // Addresses built in registers, and the loads/stores that use them.
                bool handled = false;
                switch (op)
                {
                    case 0x0F: // lui
                        if (rt != 0) { known[rt] = true; values[rt] = (w & 0xFFFF) << 16; }
                        handled = true;
                        break;
                    case 0x09: case 0x19: case 0x0D: // addiu / daddiu / ori
                        if (rt != 0 && known[rs] && rs != 0)
                        {
                            uint v = op == 0x0D ? values[rs] | (w & 0xFFFF) : unchecked(values[rs] + (uint)(short)(w & 0xFFFF));
                            Add(v, pc, XrefKind.Address);
                            known[rt] = true;
                            values[rt] = v;
                            handled = true;
                        }
                        break;
                }

                if (IsMemoryOp(op) && known[rs] && rs != 0)
                {
                    uint ea = unchecked(values[rs] + (uint)(short)(w & 0xFFFF));
                    Add(ea, pc, IsStore(op) ? XrefKind.Store : XrefKind.Load);
                }

                if (!handled)
                {
                    for (int r = 1; r < 32; r++)
                        if ((usage.Writes & (1u << r)) != 0) known[r] = false;
                }

                // Registers don't carry over the end of a function (jr ra / j).
                if (resetAfterSlot)
                {
                    Array.Clear(known, 1, 31);
                    resetAfterSlot = false;
                }
                if (usage.Flow == FlowKind.Jump) resetAfterSlot = true;
            }

            var sorted = refs.ToArray();
            Array.Sort(sorted, (a, b) => a.Target != b.Target ? a.Target.CompareTo(b.Target) : a.From.CompareTo(b.From));
            var (funcStarts, funcEnds) = FindFunctions(start, bytes, iop, sorted);
            return new RegionIndex
            {
                Region = key, Base = start, Bytes = bytes, Refs = sorted, BuiltUtc = DateTime.UtcNow,
                FuncStarts = funcStarts, FuncEnds = funcEnds,
            };
        }

        private const uint JrRa = 0x03E00008u;

        /// <summary>
        /// Function boundaries in a region:
        /// <list type="bullet">
        ///   <item>every call target (jal / bal / jalr through a known register) is a start;</item>
        ///   <item>so is the first instruction after a <c>jr ra</c> + delay slot (skipping zero
        ///   padding) when it opens a stack frame (<c>addiu sp, sp, -N</c>) or a data pointer
        ///   points at it (functions only called through tables).</item>
        /// </list>
        /// A function ends at the last non-padding word before the next start.
        /// </summary>
        private static (uint[] starts, uint[] ends) FindFunctions(uint start, byte[] bytes, bool iop, Xref[] refs)
        {
            uint end = start + (uint)bytes.Length;
            uint Word(uint addr) => BitConverter.ToUInt32(bytes, (int)(addr - start));
            bool InRegion(uint a) => a >= start && a + 4 <= end && (a & 3) == 0;

            // Normalized ↔ region address: the region is indexed in its own form
            // (physical EE or the IOP mirror), which is what Normalize produces.
            var starts = new HashSet<uint>();
            var pointerTargets = new HashSet<uint>();
            foreach (var x in refs)
            {
                if (!InRegion(x.Target)) continue;
                if (x.Kind == XrefKind.Call) starts.Add(x.Target);
                else if (x.Kind == XrefKind.Pointer) pointerTargets.Add(x.Target);
            }

            for (uint a = start; a + 12 <= end; a += 4)
            {
                if (Word(a) != JrRa) continue;
                uint next = a + 8;
                while (next + 4 <= end && Word(next) == 0) next += 4;
                if (next + 4 > end) break;
                uint w = Word(next);
                bool opensFrame = (w & 0xFFFF0000u) == 0x27BD0000u && (short)(w & 0xFFFF) < 0;
                if (opensFrame || pointerTargets.Contains(next)) starts.Add(next);
            }

            var sortedStarts = starts.ToArray();
            Array.Sort(sortedStarts);
            var ends = new uint[sortedStarts.Length];
            for (int i = 0; i < sortedStarts.Length; i++)
            {
                uint limit = i + 1 < sortedStarts.Length ? sortedStarts[i + 1] : end;
                uint last = limit - 4;
                // Trailing alignment padding belongs to no function.
                while (last > sortedStarts[i] && Word(last) == 0 && Word(last - 4) != JrRa) last -= 4;
                ends[i] = last;
            }
            return (sortedStarts, ends);
        }

        /// <summary>Function boundaries found in <paramref name="bytes"/> (for tests).</summary>
        internal static IReadOnlyList<(uint Start, uint End)> FunctionsInBytes(uint start, byte[] bytes, bool iop = false)
        {
            var index = Build("test", start, bytes, iop);
            return index.FuncStarts.Zip(index.FuncEnds).ToList();
        }

        // ── Function queries (used by the disassembler) ──────────────────────

        /// <summary>
        /// The detected function containing <paramref name="address"/>, if any region's
        /// index covers it. <paramref name="start"/>/<paramref name="end"/> are inclusive
        /// instruction addresses in the panel's (normalized) form.
        /// </summary>
        public bool TryGetFunction(uint address, out uint start, out uint end)
        {
            start = end = 0;
            uint a = Normalize(address, iopContext: false);
            foreach (var index in Snapshot())
            {
                int i = Array.BinarySearch(index.FuncStarts, a);
                if (i < 0) i = ~i - 1;
                if (i < 0 || a > index.FuncEnds[i]) continue;
                start = index.FuncStarts[i];
                end = index.FuncEnds[i];
                return true;
            }
            return false;
        }

        /// <summary>True when a detected function begins at <paramref name="address"/>.</summary>
        public bool IsFunctionStart(uint address)
        {
            uint a = Normalize(address, iopContext: false);
            foreach (var index in Snapshot())
                if (Array.BinarySearch(index.FuncStarts, a) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Bytes <c>[address, address + length)</c> from the snapshot the index was built
        /// from, or null when no index holds the whole range. Lets the disassembler
        /// simulate a function from its start without another live read.
        /// </summary>
        public byte[]? TryGetSnapshotBytes(uint address, int length)
        {
            uint a = Normalize(address, iopContext: false);
            foreach (var index in Snapshot())
            {
                long offset = (long)a - index.Base;
                if (offset < 0 || offset + length > index.Bytes.Length) continue;
                var copy = new byte[length];
                Buffer.BlockCopy(index.Bytes, (int)offset, copy, 0, length);
                return copy;
            }
            return null;
        }

        private RegionIndex[] Snapshot()
        {
            lock (_gate) return _indexes.Values.ToArray();
        }

        /// <summary>Indexes <paramref name="bytes"/> at <paramref name="start"/> and returns every reference (for tests).</summary>
        internal static IReadOnlyList<(uint Target, uint From, XrefKind Kind)> IndexBytes(uint start, byte[] bytes, bool iop = false) =>
            Build("test", start, bytes, iop).Refs.Select(x => (x.Target, x.From, x.Kind)).ToList();

        private static bool IsMemoryOp(uint op) =>
            op is >= 0x1A and <= 0x1B or 0x1E or 0x1F or >= 0x20 and <= 0x2E or 0x31 or 0x36 or 0x37 or 0x39 or 0x3E or 0x3F;

        private static bool IsStore(uint op) =>
            op is 0x1F or >= 0x28 and <= 0x2E or 0x39 or 0x3E or 0x3F;

        // ── Address helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Folds aliases to one form: KSEG0/KSEG1 and the uncached EE mirrors to
        /// physical; IOP addresses seen from IOP code (0x000xxxxx) to the EE-side
        /// mirror 0x1C0xxxxx that the IOP RAM panel uses.
        /// </summary>
        internal static uint Normalize(uint address, bool iopContext)
        {
            uint a = address;
            if (a >= 0x80000000u && a < 0xC0000000u) a &= 0x1FFFFFFFu;
            else if (a >= 0x20000000u && a < 0x22000000u) a -= 0x20000000u;
            else if (a >= 0x30000000u && a < 0x32000000u) a -= 0x30000000u;
            if (iopContext && a < 0x00200000u) a |= 0x1C000000u;
            return a;
        }

        private static bool IsPlausibleTarget(uint t, XrefKind kind)
        {
            // Data pointers need a stricter floor: small integers are everywhere.
            uint eeFloor = kind == XrefKind.Pointer ? 0x00100000u : 0x00001000u;
            if (t >= eeFloor && t < 0x02000000u) return true;
            if (t >= 0x1C000000u + (kind == XrefKind.Pointer ? 0x10000u : 0u) && t < 0x1C200000u) return true; // IOP RAM
            if (t >= 0x1FC00000u && t < 0x20000000u) return true;                                              // BIOS
            if (t >= 0x70000000u && t < 0x70004000u) return true;                                              // scratchpad
            return false;
        }

        private static (int lo, int hi) Range(Xref[] refs, uint target)
        {
            int lo = 0, hi = refs.Length;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (refs[mid].Target < target) lo = mid + 1; else hi = mid; }
            int start = lo;
            hi = refs.Length;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (refs[mid].Target <= target) lo = mid + 1; else hi = mid; }
            return (start, lo);
        }

        private static string RegionFor(uint address)
        {
            uint a = Normalize(address, iopContext: false);
            if (a >= 0x1C000000u && a < 0x1C800000u) return "iop";
            if (a >= 0x1FC00000u && a < 0x20000000u) return "bios";
            return "ee";
        }

        /// <summary>Region name → (index key, start, size). Explicit address/length override the defaults.</summary>
        private static (string key, uint start, int size) ResolveRegion(string? region, uint? address, int? length)
        {
            string r = string.IsNullOrWhiteSpace(region) ? "ee" : region.Trim().ToLowerInvariant();
            (uint start, int size) = r switch
            {
                "iop"  => (0x1C000000u, 2 * 1024 * 1024),
                "bios" => (0x1FC00000u, 4 * 1024 * 1024),
                _      => (0x00000000u, 32 * 1024 * 1024),
            };
            if (address.HasValue) start = address.Value;
            if (length is > 0) size = length.Value;
            string kind = r == "iop" ? "iop" : "ee";
            return ($"{kind}:{start:X8}:{size:X}", start, size);
        }

        private static bool Wanted(XrefKind kind, ReferenceScanKinds kinds) => kind switch
        {
            XrefKind.Call or XrefKind.Jump or XrefKind.Branch or XrefKind.RegisterJump => kinds.HasFlag(ReferenceScanKinds.ControlFlow),
            XrefKind.Pointer => kinds.HasFlag(ReferenceScanKinds.Pointer),
            _ => kinds.HasFlag(ReferenceScanKinds.ResolvedAddress),
        };

        // ── Output ───────────────────────────────────────────────────────────

        private static ReferenceEntry ToEntry(RegionIndex index, Xref x, uint requested)
        {
            uint word = 0;
            long offset = (long)x.From - index.Base;
            if (offset >= 0 && offset + 4 <= index.Bytes.Length) word = BitConverter.ToUInt32(index.Bytes, (int)offset);

            (string kind, string label, string summary) = x.Kind switch
            {
                XrefKind.Call         => ("controlFlow", "Call", "Calls"),
                XrefKind.Jump         => ("controlFlow", "Jump", "Jumps to"),
                XrefKind.Branch       => ("controlFlow", "Branch", "Branches to"),
                XrefKind.RegisterJump => ("controlFlow", "Register Jump", "Jumps (register) to"),
                XrefKind.Address      => ("resolved", "Address", "Builds"),
                XrefKind.Load         => ("resolved", "Load", "Loads from"),
                XrefKind.Store        => ("resolved", "Store", "Stores to"),
                _                     => ("pointer", "Pointer", "Pointer to"),
            };

            string instruction = x.Kind == XrefKind.Pointer ? string.Empty : SafeDisassemble(word, x.From);
            return new ReferenceEntry
            {
                Address = $"{x.From:X8}",
                AddressValue = x.From,
                Kind = kind,
                KindLabel = label,
                TargetAddress = $"{requested:X8}",
                Instruction = instruction,
                Summary = $"{summary} 0x{x.Target:X8}",
                MatchedValue = x.Kind == XrefKind.Pointer ? $"0x{word:X8}" : $"0x{x.Target:X8}",
                MatchLength = x.Kind == XrefKind.Pointer ? 4 : 0,
                SourceAddresses = [$"{x.From:X8}"],
            };
        }

        private static ReferenceEntry ToEntry(ReferenceScanResult r) => new()
        {
            Address = $"{r.Address:X8}",
            AddressValue = r.Address,
            Kind = r.Kind,
            KindLabel = r.KindLabel,
            TargetAddress = $"{r.TargetAddress:X8}",
            Instruction = r.Instruction,
            Summary = r.Summary,
            Detail = r.Detail,
            MatchedValue = r.MatchedValue,
            MatchLength = r.MatchLength,
            SourceAddresses = r.SourceAddresses.Count > 0 ? r.SourceAddresses.Select(a => $"{a:X8}").ToList() : null,
        };

        private static string SafeDisassemble(uint word, uint pc)
        {
            try { return MipsDisassembler.Disassemble(word, pc); }
            catch { return $"0x{word:X8}"; }
        }

        private string Describe()
        {
            lock (_gate)
            {
                var parts = _indexes.Values.Select(i => $"{i.Region} {i.Refs.Length:N0} refs (built {i.BuiltUtc:HH:mm:ss} UTC)").ToList();
                parts.AddRange(_building.Keys.Select(k => $"{k} building"));
                return parts.Count == 0 ? "No cross-reference index" : string.Join("; ", parts);
            }
        }

        private static string Status(string message) => JsonSerializer.Serialize(new CommandResponse
        {
            Success = true,
            Command = "XREFINDEX",
            Message = message,
        });
    }
}
