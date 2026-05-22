using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CodeDesigner.Ps2.LabelManager.IopExports;
using MgrEntry = CodeDesigner.Ps2.LabelManager.Models.LabelEntry;
using MgrType  = CodeDesigner.Ps2.LabelManager.Models.LabelType;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Turns PCSX2's runtime IOP module list into CodeDesigner labels. Produces one
    /// per-module label set named <c>IOP: &lt;modname&gt;</c>:
    /// <list type="bullet">
    ///   <item>Each entry in a module's <c>0x41C00000</c> export table becomes a
    ///         <see cref="MgrType.Function"/> label at the function address.</item>
    ///   <item>Each stub in an importing module's <c>0x41E00000</c> import table becomes
    ///         an <see cref="MgrType.Address"/> label at the stub address, sorted into
    ///         the *imported* library's set so calls and exports surface together.</item>
    /// </list>
    /// Function names are resolved via <see cref="IopExportsTable"/> (PCSX2's
    /// IopModuleNames.cpp + ps2sdk overlay, embedded in the LabelManager assembly).
    /// Idempotent: re-running over an unchanged module list produces no diffs.
    /// </summary>
    internal sealed class IopModuleLabelizer
    {
        // IRX import / export table markers from libkernel.h.
        private const uint ExportTableMarker = 0x41C00000;
        private const uint ImportTableMarker = 0x41E00000;

        // Cap how far we follow a stub / export list before bailing out — a defensive
        // bound, real tables stop well before this. 4 KiB of stubs = 512 entries, more
        // than any real IOP library has.
        private const int MaxTableBytes = 0x1000;

        private readonly IBridgeContext _ctx;

        public IopModuleLabelizer(IBridgeContext ctx) => _ctx = ctx;

        /// <summary>
        /// Scans every loaded IOP module and refreshes its <c>IOP: &lt;modname&gt;</c> set.
        /// Returns the number of modules processed and the total label count written.
        /// </summary>
        public async Task<(int Modules, int Labels)> RefreshAllAsync(string gameId)
        {
            if (_ctx.PineClient is null)
                throw new InvalidOperationException("Refreshing IOP labels requires PCSX2 (PINE) mode.");
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("gameId is required", nameof(gameId));

            var modules = _ctx.PineClient.GetModules();
            int totalLabels = 0;
            int touched = 0;

            // Group findings by *imported* library so a single library's set collects
            // exports (from its own .text) + every importing-module stub aimed at it.
            var bySet = new Dictionary<string, List<MgrEntry>>(StringComparer.OrdinalIgnoreCase);

            foreach (var m in modules)
            {
                if (m.TextSize == 0 || m.TextAddr == 0) continue;

                byte[] text;
                try
                {
                    // PINE Read32 reads EE-virtual memory; MemoryMap.Classify with the
                    // IOP hint routes the read through the EE VTLB mirror at 0xBC000000+.
                    var iop = MemoryMap.Classify(m.TextAddr, CpuKind.Iop);
                    text = _ctx.PineClient.ReadMemory(iop, (int)Math.Min(m.TextSize, 0x200000));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[IopLabelizer] skipping {m.Name} @ 0x{m.TextAddr:X8}: {ex.Message}");
                    continue; // module unmapped or partially loaded — skip.
                }

                int before = bySet.Values.Sum(l => l.Count);
                ScanModule(m, text, bySet);
                int produced = bySet.Values.Sum(l => l.Count) - before;
                Console.WriteLine($"[IopLabelizer]   {m.Name} (text=0x{m.TextAddr:X8}, {m.TextSize} bytes): produced {produced} label(s)");
                touched++;
            }

            // Flush per-set entries into the DB and disk-backed label manager.
            foreach (var (setName, entries) in bySet)
            {
                if (entries.Count == 0) continue;

                await _ctx.LabelRepo.EnsureSystemSetAsync(gameId, setName);
                var saved = await _ctx.LabelRepo.AddOrUpdateEntriesAsync(gameId, setName, entries);

                // Keep the file-backed LabelMgr in sync when a workspace exists, mirroring
                // LabelManagerService's PersistActiveSetToDisk behaviour.
                if (!string.IsNullOrWhiteSpace(_ctx.LabelMgr.WorkspaceDir))
                {
                    _ctx.LabelMgr.SaveSet(saved, _ctx.LabelMgr.WorkspaceDir, gameId);
                }

                totalLabels += entries.Count;
            }

            return (touched, totalLabels);
        }

        /// <summary>
        /// Same as <see cref="RefreshAllAsync"/> but limited to a single module name.
        /// Useful after a module-load delta when only one IRX is new.
        /// </summary>
        public async Task<(int Modules, int Labels)> RefreshModuleAsync(string gameId, string moduleName)
        {
            if (_ctx.PineClient is null)
                throw new InvalidOperationException("Refreshing IOP labels requires PCSX2 (PINE) mode.");

            var modules = _ctx.PineClient.GetModules()
                              .Where(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                              .ToList();
            if (modules.Count == 0) return (0, 0);

            var bySet = new Dictionary<string, List<MgrEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in modules)
            {
                if (m.TextSize == 0 || m.TextAddr == 0) continue;

                byte[] text;
                try
                {
                    // MemoryMap.Classify with the IOP hint converts a raw IOP physical
                    // address (e.g. 0x000A1200) into the EE VTLB mirror PINE needs
                    // (e.g. 0xBC0A1200) for the read.
                    var iop = MemoryMap.Classify(m.TextAddr, CpuKind.Iop);
                    text = _ctx.PineClient.ReadMemory(iop, (int)Math.Min(m.TextSize, 0x200000));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[IopLabelizer] skipping {m.Name} @ 0x{m.TextAddr:X8}: {ex.Message}");
                    continue;
                }

                ScanModule(m, text, bySet);
            }

            int totalLabels = 0;
            foreach (var (setName, entries) in bySet)
            {
                if (entries.Count == 0) continue;
                await _ctx.LabelRepo.EnsureSystemSetAsync(gameId, setName);
                var saved = await _ctx.LabelRepo.AddOrUpdateEntriesAsync(gameId, setName, entries);
                if (!string.IsNullOrWhiteSpace(_ctx.LabelMgr.WorkspaceDir))
                    _ctx.LabelMgr.SaveSet(saved, _ctx.LabelMgr.WorkspaceDir, gameId);
                totalLabels += entries.Count;
            }

            return (modules.Count, totalLabels);
        }

        // ── Per-module scanning ──────────────────────────────────────────────────

        private static void ScanModule(
            PineClient.IopModule m,
            byte[] text,
            Dictionary<string, List<MgrEntry>> bySet)
        {
            // Walk the text segment word-by-word looking for IRX table markers.
            // Both markers are 32-bit aligned in the standard layout.
            for (int off = 0; off + 4 <= text.Length; off += 4)
            {
                uint word = ReadU32(text, off);

                if (word == ExportTableMarker)
                {
                    // Export table belongs to *this* module — label functions inside m.
                    ParseExportTable(m, text, off, GetOrCreate(bySet, $"IOP: {m.Name}"));
                }
                else if (word == ImportTableMarker)
                {
                    // Import table — the following 8-byte name identifies the library
                    // being imported FROM. Stubs become labels in that library's set.
                    ParseImportTable(m, text, off, bySet);
                }
            }
        }

        /// <summary>
        /// Parse an IRX export table at <paramref name="markerOffset"/> within
        /// <paramref name="text"/>. Format:
        /// <code>
        ///   u32 magic       = 0x41C00000
        ///   u32 link        = 0
        ///   u16 version
        ///   u16 flags
        ///   char name[8]
        ///   void* fptr[N]   (NULL-terminated)
        /// </code>
        /// Function indices start at 0, so 0/1/2 (start/reinit?/shutdown) are the
        /// well-known reserved entries; everything else is resolved via IopExportsTable.
        /// </summary>
        private static void ParseExportTable(
            PineClient.IopModule m,
            byte[] text,
            int markerOffset,
            List<MgrEntry> sink)
        {
            // header is 20 bytes (4+4+2+2+8); function pointer array begins at +20.
            const int HeaderSize = 20;
            int ptrStart = markerOffset + HeaderSize;
            if (ptrStart + 4 > text.Length) return;

            for (int idx = 0, off = ptrStart;
                 off + 4 <= text.Length && (off - ptrStart) < MaxTableBytes;
                 idx++, off += 4)
            {
                uint fptr = ReadU32(text, off);
                if (fptr == 0) break; // null-terminated array

                string? funcName = idx switch
                {
                    0 => "start",
                    2 => "shutdown",
                    _ => IopExportsTable.Resolve(m.Name, idx),
                };
                if (funcName is null) continue;

                // The export-table function pointers are IOP-physical addresses
                // (e.g. 0x000A1200). Map them to the EE-physical IOP mirror form
                // (0x1C0A1200) — that's the form the IOP RAM live panel renders
                // and the form the rest of the stack routes goto/navigate by.
                uint labelAddr = MemoryMap.Classify(fptr, CpuKind.Iop).PhysicalAddress;

                sink.Add(new MgrEntry
                {
                    Address = $"0x{labelAddr:X8}",
                    Name    = funcName,
                    Type    = MgrType.Function,
                    Comment = $"{m.Name} export #{idx} (iop 0x{fptr:X8})",
                });
            }
        }

        /// <summary>
        /// Parse an IRX import table at <paramref name="markerOffset"/> within
        /// <paramref name="text"/>. Format:
        /// <code>
        ///   u32 magic       = 0x41E00000
        ///   u32 zero        = 0
        ///   u16 version
        ///   u16 flags
        ///   char name[8]    (imported library name)
        ///   u32 stubs[2 * N]  (jr-instr, addiu-instr pairs; terminated by 0,0)
        /// </code>
        /// Each stub's address (in IOP RAM) is <c>moduleTextAddr + stubOffsetInText</c>
        /// and is what game code actually jumps to.
        /// </summary>
        private static void ParseImportTable(
            PineClient.IopModule importing,
            byte[] text,
            int markerOffset,
            Dictionary<string, List<MgrEntry>> bySet)
        {
            const int HeaderSize = 20;
            if (markerOffset + HeaderSize > text.Length) return;

            // Library name is 8 bytes at marker+12.
            int nameOff = markerOffset + 12;
            int nameEnd = nameOff;
            while (nameEnd < nameOff + 8 && text[nameEnd] != 0) nameEnd++;
            string libName = Encoding.ASCII.GetString(text, nameOff, nameEnd - nameOff).Trim();
            if (string.IsNullOrEmpty(libName)) return;

            // Don't synthesise stubs for libraries we don't have export names for —
            // they would just produce noise like "imp_(unknown 5)".
            if (!IopExportsTable.HasModule(libName)) return;

            var sink = GetOrCreate(bySet, $"IOP: {libName}");

            // Each stub is 8 bytes = (jump-instr, addiu-instr).
            // The addiu has the form 0x24000000 | (index & 0xFFFF).
            int stubStart = markerOffset + HeaderSize;
            for (int off = stubStart;
                 off + 8 <= text.Length && (off - stubStart) < MaxTableBytes;
                 off += 8)
            {
                uint jumpInstr  = ReadU32(text, off);
                uint addiuInstr = ReadU32(text, off + 4);
                if (jumpInstr == 0 && addiuInstr == 0) break; // double-zero terminator

                // PCSX2 recognises a stub when (delayslot >> 16) == 0x2400 — see
                // pcsx2/R3000AInterpreter.cpp.
                if ((addiuInstr >> 16) != 0x2400) continue;

                int index = (int)(addiuInstr & 0xFFFF);
                string funcName = index switch
                {
                    0 => "start",
                    2 => "shutdown",
                    _ => IopExportsTable.Resolve(libName, index) ?? $"unk_{index}",
                };

                uint stubAddrIop = (uint)(importing.TextAddr + off);
                // Store at the EE-physical mirror form (0x1C0xxxxx) so goto routes
                // to the IOP RAM live panel and the panel's disasm rows line up.
                uint stubAddr = MemoryMap.Classify(stubAddrIop, CpuKind.Iop).PhysicalAddress;
                sink.Add(new MgrEntry
                {
                    Address = $"0x{stubAddr:X8}",
                    Name    = $"imp_{funcName}",
                    Type    = MgrType.Address,
                    Comment = $"{importing.Name} → {libName}::{funcName} (#{index}, iop 0x{stubAddrIop:X8})",
                });
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private static uint ReadU32(byte[] buf, int off) =>
            (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));

        private static List<MgrEntry> GetOrCreate(Dictionary<string, List<MgrEntry>> d, string key)
        {
            if (!d.TryGetValue(key, out var list))
            {
                list = new List<MgrEntry>();
                d[key] = list;
            }
            return list;
        }
    }
}
