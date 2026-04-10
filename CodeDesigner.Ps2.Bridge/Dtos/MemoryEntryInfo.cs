using System;
using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>Wire payload for a single MCP Memory Manager entry returned by MCPMEMORY_* commands.</summary>
    public class McpMemoryInfo
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("gameId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? GameId { get; set; }

        /// <summary>
        /// FK to the agent that owns this memory.
        /// Null means the memory is shared across all agents.
        /// </summary>
        [JsonPropertyName("agentId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? AgentId { get; set; }

        [JsonPropertyName("createdAt")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("updatedAt")]
        public string UpdatedAt { get; set; } = string.Empty;
    }
}
