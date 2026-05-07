using System.Collections.Generic;
using CodeDesigner.Ps2.Bridge.Database;
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

        // ── Shared state used by multiple services ───────────────────────────
        /// <summary>EE-address → (label, comment) — populated by LabelService and read by many.</summary>
        Dictionary<uint, (string Label, string? Comment)> Labels { get; }

        /// <summary>Label manager — typed model behind the legacy dict.</summary>
        CodeDesigner.Ps2.LabelManager.Ps2LabelManager LabelMgr { get; }

        // ── Cross-service navigation history ─────────────────────────────────
        /// <summary>Record an EE address into the bounded nav-history log.</summary>
        void RecordNavEntry(uint address, string source);
    }
}
