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
    }
}
