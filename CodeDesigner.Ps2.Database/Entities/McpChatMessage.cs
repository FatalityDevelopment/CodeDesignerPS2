namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// One rendered message in a <see cref="McpChatSession"/>. The visible payload
/// (the orchestrator's dispatch card, a sub-agent's tool-call card, a CDS code
/// block, etc.) is stored as opaque JSON in <see cref="BlocksJson"/> matching
/// the block contract documented in <c>docs/ai-chat-templates.md</c>.
///
/// We keep the blocks as a JSON column rather than as a separate normalised
/// table because:
///   1. The chat renderer always loads a whole message at once.
///   2. Blocks are append-only during streaming and patched only by index, so
///      relational integrity for individual blocks buys us nothing.
///   3. The model emits the wire format directly — round-tripping through a
///      relational schema would force lossy conversions for every new block kind.
/// </summary>
public class McpChatMessage
{
    public int Id { get; set; }

    public int McpChatSessionId { get; set; }
    public McpChatSession McpChatSession { get; set; } = null!;

    /// <summary>One of "user", "agent", "system". Drives the renderer's chrome.</summary>
    public string Kind { get; set; } = "user";

    /// <summary>Optional FK to the agent persona that authored this message.</summary>
    public int? McpAgentId { get; set; }
    public McpAgent? McpAgent { get; set; }

    /// <summary>
    /// Snapshot of the agent display name at write time (e.g. "[Fatality] Raiden").
    /// Kept alongside <see cref="McpAgentId"/> so the chat still renders if the agent
    /// profile is later renamed or deleted.
    /// </summary>
    public string? AgentDisplay { get; set; }

    /// <summary>Snapshot of the role badge text (e.g. "ORCHESTRATOR", "MEMORY SCANNER").</summary>
    public string? Role { get; set; }

    /// <summary>JSON-serialised <c>Block[]</c> per the chat-templates spec.</summary>
    public string BlocksJson { get; set; } = "[]";

    /// <summary>Token count surfaced in the message header. Null until known.</summary>
    public int? Tokens { get; set; }

    /// <summary>Wall-clock latency (ms) surfaced in the message header. Null until known.</summary>
    public int? LatencyMs { get; set; }

    /// <summary>One of "streaming", "done", "error". Drives the header dot.</summary>
    public string Status { get; set; } = "done";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
