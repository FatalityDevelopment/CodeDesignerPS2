using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>Compact label-set search hit for MCP and other low-token clients.</summary>
    public class LabelSetSearchResultInfo
    {
        [JsonPropertyName("gameDiscId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? GameDiscId { get; set; }

        [JsonPropertyName("setName")]
        public string SetName { get; set; } = string.Empty;

        [JsonPropertyName("isBuiltIn")]
        public bool IsBuiltIn { get; set; }

        [JsonPropertyName("isSystem")]
        public bool IsSystem { get; set; }

        [JsonPropertyName("entryCount")]
        public int EntryCount { get; set; }

        [JsonPropertyName("matchingLabelCount")]
        public int MatchingLabelCount { get; set; }
    }

    /// <summary>Compact label search hit; fetch the full set when deeper structure is needed.</summary>
    public class LabelSearchResultInfo
    {
        [JsonPropertyName("gameDiscId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? GameDiscId { get; set; }

        [JsonPropertyName("setName")]
        public string SetName { get; set; } = string.Empty;

        [JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        [JsonPropertyName("parentAddress")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ParentAddress { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("comment")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Comment { get; set; }

        [JsonPropertyName("valueType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ValueType { get; set; }

        [JsonPropertyName("hidden")]
        public bool Hidden { get; set; }

        [JsonPropertyName("dynamic")]
        public bool Dynamic { get; set; }

        [JsonPropertyName("matchedFields")]
        public List<string> MatchedFields { get; set; } = [];
    }
}
