using System.Collections.Generic;
using CodeDesigner.Ps2.Bridge.Repositories;
using CodeDesigner.Ps2.Database;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Surface that command-handler services use to read/write Bridge state.
    /// <para>
    /// Replaces the partial-class super-class pattern — instead of growing
    /// <c>SocketServer</c> indefinitely with every new handler, services take
    /// an <see cref="IBridgeContext"/> in their constructor and operate
    /// against this small, well-defined interface. <see cref="SocketServer"/>
    /// is the canonical implementation; tests can supply a fake.
    /// </para>
    /// <para>
    /// Internal because some of the repository types it exposes are
    /// <c>internal</c> to the Bridge assembly.
    /// </para>
    /// </summary>
    internal interface IBridgeContext
    {
        // ── Active debug clients ─────────────────────────────────────────────
        /// <summary>PCSX2 PINE client — set when STARTPCSX2 attached.</summary>
        PineClient? PineClient { get; set; }

        /// <summary>Built-in emulator client — set when STARTEMULATOR attached.</summary>
        InternalEmulatorClient? InternalClient { get; set; }

        /// <summary>
        /// Second built-in-emulator client used purely as the analysis substrate.
        /// In PCSX2 mode this points at a separately-launched bare emulator that
        /// receives mirrored memory + registers at every breakpoint so the MIPS
        /// analyzer can produce state-aware comment-column hints. In built-in mode
        /// this is the same instance as <see cref="InternalClient"/>.
        /// Null when no analysis emulator is attached — analysis falls back to
        /// the existing blank-state path.
        /// </summary>
        InternalEmulatorClient? AnalysisClient { get; set; }

        /// <summary>Active client (PineClient ?? InternalClient).</summary>
        IDebugClient? Client { get; }

        /// <summary>Whether <see cref="Client"/> is non-null.</summary>
        bool HasDebugClient { get; }

        // ── Persistence ──────────────────────────────────────────────────────
        CodeDesignerDbContext Db { get; }
        DbLabelRepository LabelRepo { get; }
        DbSettingsRepository SettingsRepo { get; }
        DbMcpMemoryRepository? McpMemoryRepo { get; }
        DbMcpAgentRepository? McpAgentRepo { get; }
        DbMcpOrchestrationRepository? McpOrchestrationRepo { get; }
        DbMcpChatRepository? ChatRepo { get; }
        DbDataFormatRepository DataFormatRepo { get; }

        // ── Shared state used by multiple services ───────────────────────────
        /// <summary>EE-address → (label, comment) — populated by LabelService and read by many.</summary>
        Dictionary<uint, (string Label, string? Comment)> Labels { get; }

        /// <summary>Label manager — typed model behind the legacy dict.</summary>
        CodeDesigner.Ps2.LabelManager.Ps2LabelManager LabelMgr { get; }

        /// <summary>Re-derives <see cref="Labels"/> from <see cref="LabelMgr"/>'s active set.</summary>
        void SyncLegacyDictsFromLabelManager();

        /// <summary>Struct definitions (name → struct entry) — owned by StructService.</summary>
        Dictionary<string, StructEntry> Structs { get; }

        // ── Cross-service navigation history ─────────────────────────────────
        /// <summary>Record an EE address into the bounded nav-history log.</summary>
        void RecordNavEntry(uint address, string source);

        /// <summary>Snapshot of the nav-history (most recent last).</summary>
        IReadOnlyList<NavHistoryEntry> GetNavHistorySnapshot();

        /// <summary>Empty the nav-history log.</summary>
        void ClearNavHistory();

        // ── Peer service handles ─────────────────────────────────────────────
        // Set by <see cref="SocketServer"/> after constructing the service
        // instances. Other services can call into MemoryService for the
        // chunked debug-socket reader without copying its logic.
        MemoryService Memory { get; }

        /// <summary>
        /// Owns the analysis mirror — register snapshot cached at the last
        /// pause, plus memory mirror into <see cref="AnalysisClient"/> when one
        /// is attached. Disassembly + step handlers call this to seed
        /// <see cref="CodeDesigner.Ps2.Common.MipsAnalyzer"/> with real runtime
        /// state for accurate comment-column hints.
        /// </summary>
        AnalysisStateService AnalysisState { get; }

        /// <summary>
        /// Cross-reference index (who calls / branches to / loads from / points at
        /// an address). Disassembly reads per-row reference counts from it.
        /// </summary>
        XrefIndexService Xrefs { get; }
    }
}
