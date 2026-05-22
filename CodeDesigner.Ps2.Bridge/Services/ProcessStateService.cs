using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class ProcessStateService : ICommandService
    {
        private readonly IBridgeContext _ctx;

        /// <summary>
        /// Snapshot of the most recent IOP module list (as reported by PCSX2) keyed by
        /// module name → text address. Used to detect new modules between GETMODULES
        /// polls so the IOP labelizer only re-scans when a fresh IRX was loaded.
        /// </summary>
        private Dictionary<string, uint>? _lastModuleSnapshot;

        public ProcessStateService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(_ => HandleGetThreads(),    "GETTHREADS");
            reg.Add(_ => HandleGetIopThreads(), "GETIOPTHREADS", "GET_IOP_THREADS", "IOPTHREADS");
            reg.Add(_ => HandleGetModules(),    "GETMODULES",    "GET_MODULES",     "IOPMODULES");
            reg.Add(_ => HandleGetStack(),      "GETSTACK",      "GET_STACK",       "CALLSTACK");
        }

        // EE main RAM size — 32 MB. Used to bound the TCB scan.
        private const uint EeMainRamSize = 32 * 1024 * 1024;

        /// <summary>
        /// Handle GETTHREADS — return current EE threads. PCSX2 PINE mode uses
        /// the native <c>GetEEThreads()</c> opcode. Built-in mode walks the EE
        /// kernel's TCB pool in RAM via the active <see cref="IDebugClient"/>.
        /// </summary>
        private string HandleGetThreads()
        {
            if (_ctx.PineClient != null)
            {
                try
                {
                    var pineThreads = _ctx.PineClient.GetEEThreads();
                    var entries = pineThreads.Select(t => new ThreadEntry
                    {
                        Id       = (int)t.TID,
                        Status   = StatusName(t.Status),
                        Pc       = t.PC,
                        Sp       = t.StackTop,
                        Priority = (int)t.Priority,
                        EntryPc  = t.EntryPoint,
                    }).ToList();
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = true,
                        Command = "GETTHREADS",
                        Threads = entries,
                    });
                }
                catch (Exception ex)
                {
                    return Responses.Error($"PINE GETTHREADS failed: {ex.Message}");
                }
            }

            if (_ctx.Client == null)
                return Responses.Error("Emulator not running — no debug _ctx.Client attached");

            // ── Walk the PS2 EE OS TCB pool via the debug _ctx.Client ─────────────
            // Same algorithm as the previous in-process version, but reads
            // memory through _ctx.Client.ReadBytes() so it works for both PCSX2 and
            // the built-in InternalPineSocket.

            const uint TcbSize     = 128u;
            const uint MaxTcbScan  = 256u;
            const uint TcbPoolBase = 0x00000100u;
            const uint TcbPoolKseg0 = TcbPoolBase | 0x80000000u;

            uint scanBytes = MaxTcbScan * TcbSize;
            byte[] poolBytes;
            try { poolBytes = _ctx.Client.ReadBytes(TcbPoolKseg0, (int)scanBytes); }
            catch { poolBytes = Array.Empty<byte>(); }

            uint currentPc;
            try { currentPc = AddressTranslator.NormalizePcToKseg0(_ctx.Client.GetProgramCounter()); }
            catch { currentPc = 0; }

            // SP comes from $sp (GPR 29). Without GETREGS support yet we can
            // only fall back to the running TCB's PC/Sp from the table itself.
            uint currentSp = 0;

            var threads = new List<ThreadEntry>();
            bool foundAny = false;

            for (uint i = 0; i < MaxTcbScan; i++)
            {
                uint physBase = TcbPoolBase + i * TcbSize;
                if (physBase + TcbSize > EeMainRamSize)
                    break;
                int slotOffset = (int)(physBase - TcbPoolBase);
                if (slotOffset + 0x20 > poolBytes.Length)
                    break;

                uint statusRaw = ReadU32(poolBytes, slotOffset + 0x00);
                uint entry     = ReadU32(poolBytes, slotOffset + 0x04);
                uint savedSp   = ReadU32(poolBytes, slotOffset + 0x08);
                uint curPri    = ReadU32(poolBytes, slotOffset + 0x1C);

                if (statusRaw == 0 || (statusRaw & ~0x1Fu) != 0 ||
                    (statusRaw & (statusRaw - 1)) != 0)
                    continue;

                if (entry != 0 &&
                    !(entry >= 0x00100000u || entry >= 0x80000000u))
                    continue;

                bool isRunning = statusRaw == 0x01;
                uint tcbPc = isRunning ? currentPc : AddressTranslator.NormalizePcToKseg0(entry);
                uint tcbSp = isRunning ? currentSp : savedSp;

                threads.Add(new ThreadEntry
                {
                    Id       = (int)i,
                    Status   = StatusName(statusRaw),
                    Pc       = tcbPc,
                    Sp       = tcbSp,
                    Priority = (int)curPri,
                    EntryPc  = AddressTranslator.NormalizePcToKseg0(entry),
                });
                foundAny = true;
            }

            if (!foundAny)
            {
                threads.Add(new ThreadEntry
                {
                    Id       = 0,
                    Status   = "Running",
                    Pc       = currentPc,
                    Sp       = currentSp,
                    Priority = 0,
                    EntryPc  = currentPc,
                });
            }

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success = true,
                Command = "GETTHREADS",
                Threads = threads,
            });
        }

        /// <summary>Handle GETIOPTHREADS — PCSX2 PINE only.</summary>
        private string HandleGetIopThreads()
        {
            if (_ctx.PineClient == null)
                return Responses.Error("GETIOPTHREADS requires PCSX2 PINE mode");
            try
            {
                var pineThreads = _ctx.PineClient.GetIOPThreads();
                var entries = pineThreads.Select(t => new ThreadEntry
                {
                    Id       = (int)t.TID,
                    Status   = StatusName(t.Status),
                    Pc       = t.PC,
                    Sp       = t.StackTop,
                    Priority = (int)t.Priority,
                    EntryPc  = t.EntryPoint,
                }).ToList();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "GETIOPTHREADS",
                    Threads = entries,
                });
            }
            catch (Exception ex) { return Responses.Error($"PINE GETIOPTHREADS failed: {ex.Message}"); }
        }

        /// <summary>Handle GETMODULES — PCSX2 PINE only.</summary>
        private string HandleGetModules()
        {
            if (_ctx.PineClient == null)
                return Responses.Error("GETMODULES requires PCSX2 PINE mode");
            try
            {
                var modules = _ctx.PineClient.GetModules();
                var entries = modules.Select(m => new IopModuleInfo
                {
                    Name      = m.Name,
                    Version   = m.Version,
                    TextAddr  = $"0x{m.TextAddr:X8}",
                    Entry     = $"0x{m.Entry:X8}",
                    GP        = $"0x{m.GP:X8}",
                    TextSize  = m.TextSize,
                    DataSize  = m.DataSize,
                    BssSize   = m.BssSize,
                }).ToList();

                MaybeRefreshIopLabels(modules);

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "GETMODULES",
                    Modules = entries,
                });
            }
            catch (Exception ex) { return Responses.Error($"PINE GetModules failed: {ex.Message}"); }
        }

        /// <summary>
        /// Diff the current module list against the last snapshot and, for any newly
        /// loaded IRX, fire a background refresh against the IOP labelizer so the
        /// "IOP: &lt;modname&gt;" label sets stay current. Falls back to PCSX2's reported
        /// game serial when the user hasn't opened a label set yet (which is when
        /// <see cref="Ps2LabelManager.GameId"/> first gets populated).
        /// </summary>
        private void MaybeRefreshIopLabels(IReadOnlyList<PineClient.IopModule> modules)
        {
            string? gameId = _ctx.LabelMgr.GameId;

            // Fall back to PCSX2's serial when the LabelMgr context hasn't been seeded
            // yet (user opened PCSX2 but hasn't touched the Label Manager). Without
            // this fallback the labelizer would silently never run on a fresh boot.
            if (string.IsNullOrWhiteSpace(gameId) && _ctx.PineClient is not null)
            {
                try
                {
                    var serial = _ctx.PineClient.GetGameID();
                    if (!string.IsNullOrWhiteSpace(serial))
                        gameId = serial.Trim();
                }
                catch { /* PINE serial unavailable — fall through and skip this tick */ }
            }

            if (string.IsNullOrWhiteSpace(gameId))
            {
                CaptureSnapshot(modules);
                Console.WriteLine($"[IopLabelizer] skipping refresh — no game serial available yet (modules={modules.Count})");
                return;
            }

            var current = modules.ToDictionary(m => m.Name, m => m.TextAddr, StringComparer.OrdinalIgnoreCase);

            // New module set on first call → label everything once.
            if (_lastModuleSnapshot is null)
            {
                _lastModuleSnapshot = current;
                Console.WriteLine($"[IopLabelizer] initial refresh for gameId='{gameId}' — {modules.Count} module(s)");
                FireAndForgetRefresh(gameId, moduleName: null);
                return;
            }

            // Otherwise refresh per-module only for additions / address changes.
            var newOrMoved = current
                .Where(kv => !_lastModuleSnapshot.TryGetValue(kv.Key, out var prev) || prev != kv.Value)
                .Select(kv => kv.Key)
                .ToList();

            _lastModuleSnapshot = current;

            if (newOrMoved.Count > 0)
                Console.WriteLine($"[IopLabelizer] delta refresh for gameId='{gameId}': {string.Join(", ", newOrMoved)}");

            foreach (var name in newOrMoved)
                FireAndForgetRefresh(gameId, name);
        }

        private void CaptureSnapshot(IReadOnlyList<PineClient.IopModule> modules)
        {
            _lastModuleSnapshot = modules.ToDictionary(m => m.Name, m => m.TextAddr, StringComparer.OrdinalIgnoreCase);
        }

        private void FireAndForgetRefresh(string gameId, string? moduleName)
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var labelizer = new IopModuleLabelizer(_ctx);
                    (int Modules, int Labels) result = moduleName is null
                        ? await labelizer.RefreshAllAsync(gameId)
                        : await labelizer.RefreshModuleAsync(gameId, moduleName);
                    _ctx.SyncLegacyDictsFromLabelManager();
                    Console.WriteLine($"[IopLabelizer] {(moduleName ?? "all")}: scanned {result.Modules} module(s), wrote {result.Labels} label(s)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[IopLabelizer] background refresh failed: {ex}");
                }
            });
        }

        /// <summary>Handle GETSTACK — PCSX2 PINE only.</summary>
        private string HandleGetStack()
        {
            if (_ctx.PineClient == null)
                return Responses.Error("GETSTACK requires PCSX2 PINE mode");
            try
            {
                var frames = _ctx.PineClient.GetStack();
                var entries = frames.Select(f => new PineStackFrameInfo
                {
                    Entry     = $"0x{f.Entry:X8}",
                    PC        = $"0x{f.PC:X8}",
                    SP        = $"0x{f.SP:X8}",
                    StackSize = f.StackSize,
                }).ToList();
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success     = true,
                    Command     = "GETSTACK",
                    StackFrames = entries,
                });
            }
            catch (Exception ex) { return Responses.Error($"PINE GetStack failed: {ex.Message}"); }
        }

        private static string StatusName(uint status) => status switch
        {
            0x01 => "Running",
            0x02 => "Ready",
            0x04 => "Waiting",
            0x08 => "Suspended",
            0x10 => "Dormant",
            _    => $"0x{status:X2}",
        };

        private static uint ReadU32(byte[] buf, int offset)
        {
            if (offset + 4 > buf.Length) return 0;
            return (uint)(buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24));
        }
    }
}
