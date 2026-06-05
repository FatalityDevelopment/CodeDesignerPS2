using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Wire payload for one rendered message in a chat session. <see cref="BlocksJson"/>
    /// is an opaque JSON array following the spec in <c>docs/ai-chat-templates.md</c>;
    /// the Bridge never parses it.
    /// </summary>
    public class McpChatMessageInfo
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("sessionId")]
        public int SessionId { get; set; }

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "user";

        [JsonPropertyName("agentId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? AgentId { get; set; }

        [JsonPropertyName("agentDisplay")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? AgentDisplay { get; set; }

        [JsonPropertyName("role")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Role { get; set; }

        /// <summary>JSON array of blocks (text / tool-call / code / …).</summary>
        [JsonPropertyName("blocksJson")]
        public string BlocksJson { get; set; } = "[]";

        [JsonPropertyName("tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Tokens { get; set; }

        [JsonPropertyName("latencyMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? LatencyMs { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = "done";

        [JsonPropertyName("createdAt")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("updatedAt")]
        public string UpdatedAt { get; set; } = string.Empty;
    }
}
