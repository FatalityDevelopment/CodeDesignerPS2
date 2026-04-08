using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>Wire payload for a single MCP Agent profile returned by MCPAGENT_* commands.</summary>
    public class McpAgentInfo
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("isAgentLead")]
        public bool IsAgentLead { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; set; }

        [JsonPropertyName("status")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Status { get; set; }

        [JsonPropertyName("objective")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Objective { get; set; }

        [JsonPropertyName("rules")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Rules { get; set; }

        [JsonPropertyName("guidelines")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Guidelines { get; set; }

        [JsonPropertyName("recommendedWorkflow")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RecommendedWorkflow { get; set; }

        [JsonPropertyName("sessionBegin")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SessionBegin { get; set; }

        [JsonPropertyName("concurrentTasks")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ConcurrentTasks { get; set; }

        [JsonPropertyName("sessionEnd")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SessionEnd { get; set; }

        [JsonPropertyName("research")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Research { get; set; }

        [JsonPropertyName("createdAt")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("updatedAt")]
        public string UpdatedAt { get; set; } = string.Empty;
    }
}
