using System;
using System.Text.Json;
using CodeDesigner.Ps2.Common.CPU;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Owns the analysis mirror — the bridge's bridge between the live debug
    /// client and the MIPS analyzer.
    /// <para>
    /// At every pause-state transition (or when <c>REFRESHANALYSIS</c> is called
    /// explicitly), the service reads memory + EE registers from the live debug
    /// client and, when an analysis emulator is attached, also pushes them into
    /// that second process. Either way, the EE register file is cached as a
    /// <see cref="CpuState"/> the analyzer can clone via <see cref="TryAcquire"/>
    /// — that's what lets <c>HandleDisassemble</c> turn <c>[TAKEN]</c>/<c>[NOT
    /// TAKEN]</c> and concrete pointer hints back on.
    /// </para>
    /// </summary>
    internal sealed class AnalysisStateService : ICommandService
    {
        // Mirror is considered fresh within this window. Multiple webview panels
        // re-disassembling on the same pause hit the cache instead of paying for
        // a fresh 38 MB mirror each time.
        private static readonly TimeSpan FreshnessWindow = TimeSpan.FromMilliseconds(250);

        // Region list mirrors EmulatorControlService.HandleSnapshot — keep them
        // in sync if the canonical snapshot ever adds / drops a region.
        private static readonly (uint Base, int Size, bool UseIop)[] MirrorRegions = new[]
        {
            (Base: 0x00000000u,                       Size: (int)AddressTranslator.EeRamSize,        UseIop: false),
            (Base: AddressTranslator.BiosRomPhysBase, Size: (int)AddressTranslator.BiosRomSize,      UseIop: false),
            (Base: 0x70000000u,                       Size: (int)AddressTranslator.EeScratchpadSize, UseIop: false),
            (Base: AddressTranslator.EeIopMirrorBase, Size: (int)AddressTranslator.IopRamSize,       UseIop: true),
        };

        private readonly IBridgeContext _ctx;
        private readonly object _gate = new object();

        private CpuState? _cpu;
        private uint      _pcAtMirror;
        private DateTime  _mirroredAtUtc;
        private volatile bool _mirrorValid;

        public AnalysisStateService(IBridgeContext ctx) => _ctx = ctx;

        /// <summary>True when a valid snapshot is cached and ready for the analyzer.</summary>
        public bool IsMirrorValid => _mirrorValid;

        /// <summary>EE PC at the moment the cached snapshot was captured.</summary>
        public uint PcAtMirror => _pcAtMirror;

        public void Register(CommandRegistry reg)
        {
            reg.Add(_ => HandleRefresh(), "REFRESHANALYSIS", "REFRESH_ANALYSIS");
        }

        // ── Public surface ────────────────────────────────────────────────────

        /// <summary>
        /// Read memory + registers from the live client and (when an analysis
        /// emulator is attached) push them into it. Caches an immutable register
        /// snapshot regardless of whether memory mirroring happened.
        /// </summary>
        /// <param name="force">
        /// When <c>false</c>, reuses the cached snapshot if it was refreshed
        /// within the last 250 ms. Pass <c>true</c> after STEP — register state
        /// has guaranteed-changed and the cache must rebuild.
        /// </param>
        public void RefreshFromLive(bool force)
        {
            lock (_gate)
            {
                if (_ctx.Client is null)
                {
                    _mirrorValid = false;
                    return;
                }

                // The mirror is only meaningful when execution is paused — a
                // running-state register file changes every cycle. Skip cleanly
                // when running so callers don't pay for a 38 MB read that
                // would already be stale by the time it lands.
                uint livePc;
                try
                {
                    livePc = _ctx.Client.GetProgramCounter(out bool paused);
                    if (!paused)
                    {
                        _mirrorValid = false;
                        return;
                    }
                }
                catch
                {
                    _mirrorValid = false;
                    return;
                }

                // Still paused at the same PC: nothing has executed, so the cached
                // mirror is current. Re-mirroring here used to copy ~38 MB on every
                // disassembly refresh while paused.
                if (!force && _mirrorValid && livePc == _pcAtMirror)
                {
                    return;
                }
                if (!force && _mirrorValid &&
                    (DateTime.UtcNow - _mirroredAtUtc) < FreshnessWindow)
                {
                    return;
                }

                bool sameProcess = ReferenceEquals(_ctx.Client, _ctx.AnalysisClient);

                // 1) Memory mirror — only worth doing when the live and analysis
                //    clients are distinct processes (PCSX2 mode with a bare
                //    analysis emulator attached). In built-in mode the live
                //    process IS the analysis substrate, so memory is already
                //    coherent.
                if (!sameProcess && _ctx.AnalysisClient is not null)
                {
                    foreach (var (baseAddr, size, useIop) in MirrorRegions)
                    {
                        try
                        {
                            uint clientAddr = AddressTranslator.ToClientAddress(baseAddr, useIop);
                            byte[] bytes    = _ctx.Client.ReadBytes(clientAddr, size);
                            _ctx.AnalysisClient.WriteBytes(clientAddr, bytes);
                        }
                        catch
                        {
                            // Best-effort: an IOP-mirror or BIOS read that fails
                            // on one backend shouldn't prevent the rest of the
                            // mirror from succeeding.
                        }
                    }
                }

                // 2) Register snapshot — succeeds whether or not the memory
                //    mirror ran, so the analyzer always gets a seed CpuState in
                //    paused state.
                CpuState? cpu = TryReadLiveRegisters();
                if (cpu is null)
                {
                    _mirrorValid = false;
                    return;
                }

                // 3) Push registers into the analysis emulator so future
                //    opcode-level analysis sees the same starting state.
                if (!sameProcess && _ctx.AnalysisClient is not null)
                {
                    try { _ctx.AnalysisClient.SetEeRegisters(cpu); }
                    catch { /* best-effort */ }
                }

                _cpu           = cpu;
                _pcAtMirror    = (uint)(cpu.PC & 0xFFFFFFFFUL);
                _mirroredAtUtc = DateTime.UtcNow;
                _mirrorValid   = true;
            }
        }

        /// <summary>
        /// Clone the cached register snapshot for the MIPS analyzer. Returns
        /// <c>null</c> when no mirror is valid (no client attached, or a fetch
        /// failed). The clone is safe to mutate — the cached state is not.
        /// </summary>
        public AnalysisSnapshot? TryAcquire()
        {
            lock (_gate)
            {
                if (!_mirrorValid || _cpu is null) return null;
                return new AnalysisSnapshot(_cpu.CreateAnalysisSnapshot(), _pcAtMirror, _mirroredAtUtc);
            }
        }

        /// <summary>Mark the cache stale — call from <c>HandleResume</c>.</summary>
        public void Invalidate()
        {
            lock (_gate)
            {
                _mirrorValid = false;
            }
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private CpuState? TryReadLiveRegisters()
        {
            // PCSX2 live → PINE GETREGS.
            if (_ctx.PineClient is not null && ReferenceEquals(_ctx.Client, _ctx.PineClient))
            {
                try
                {
                    var rs = _ctx.PineClient.GetRegisters();
                    return PineRegistersToCpuState(rs);
                }
                catch { return null; }
            }
            // Built-in live → InternalPineSocket GETREGS (opcode 0x1A).
            if (_ctx.InternalClient is not null && ReferenceEquals(_ctx.Client, _ctx.InternalClient))
            {
                try { return _ctx.InternalClient.GetEeRegisters(); }
                catch { return null; }
            }
            return null;
        }

        private static CpuState PineRegistersToCpuState(PineClient.RegisterSnapshot rs)
        {
            var cpu = new CpuState();
            for (int i = 0; i < 32; i++)
            {
                cpu.RegsLo[i] = ((ulong)rs.GprHi32[i]   << 32) | rs.GprLo32[i];
                cpu.RegsHi[i] = ((ulong)rs.GprUpHi32[i] << 32) | rs.GprUpLo32[i];
            }
            cpu.PC  = rs.PC;
            cpu.HI  = rs.HI;
            cpu.LO  = rs.LO;
            cpu.HI1 = rs.HI1;
            cpu.LO1 = rs.LO1;
            cpu.SA  = (int)rs.SA;
            return cpu;
        }

        private string HandleRefresh()
        {
            try
            {
                RefreshFromLive(force: true);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "REFRESHANALYSIS",
                    PC      = _mirrorValid ? _pcAtMirror : (uint?)null,
                    Message = _mirrorValid
                        ? $"Analysis mirror refreshed (PC = 0x{_pcAtMirror:X8})"
                        : "No live debug client attached — mirror skipped",
                });
            }
            catch (Exception ex)
            {
                return Responses.Error("REFRESHANALYSIS failed: " + ex.Message);
            }
        }
    }

    /// <summary>Immutable register snapshot returned by <see cref="AnalysisStateService.TryAcquire"/>.</summary>
    internal sealed class AnalysisSnapshot
    {
        public CpuState  Cpu           { get; }
        public uint      PcAtMirror    { get; }
        public DateTime  MirroredAtUtc { get; }

        public AnalysisSnapshot(CpuState cpu, uint pc, DateTime utc)
        {
            Cpu           = cpu;
            PcAtMirror    = pc;
            MirroredAtUtc = utc;
        }
    }
}
