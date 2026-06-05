namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A conversation in the AI control panel's Chat tab — the persistent thread
/// the user sees in the webview. One <see cref="McpChatSession"/> owns an
/// ordered list of <see cref="McpChatMessage"/> rows.
///
/// Distinct from <see cref="McpAgentSession"/>, which is the *runtime* lifetime
/// of a sub-agent. A chat session can spawn many agent sessions while it runs;
/// only the chat session persists across restarts as a visible thread.
/// </summary>
public class McpChatSession
{
    public int Id { get; set; }

    /// <summary>Display title shown in the chat sidebar.</summary>
    public string Title { get; set; } = "New chat";

    /// <summary>Optional orchestration profile that drives this chat (the "squad").</summary>
    public int? McpOrchestrationId { get; set; }
    public McpOrchestration? McpOrchestration { get; set; }

    /// <summary>Optional context chip: PS2 game serial / disc serial (e.g. "SCUS-97232").</summary>
    public string? GameSerial { get; set; }

    /// <summary>Optional context chip: free-form game CRC or short id (e.g. "D7CFDCCF").</summary>
    public string? GameTag { get; set; }

    /// <summary>Inverse navigation property: messages in this chat, ordered by creation.</summary>
    public List<McpChatMessage> Messages { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
