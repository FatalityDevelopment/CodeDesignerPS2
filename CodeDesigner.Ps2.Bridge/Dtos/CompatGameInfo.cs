using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Wire payload for a single PCSX2 compatibility entry (one game disc)
    /// returned by the COMPAT_* commands.
    /// </summary>
    public class CompatGameInfo
    {
        /// <summary>GameDisc row ID.</summary>
        [JsonPropertyName("discId")]
        public int DiscId { get; set; }

        /// <summary>Parent Game row ID — multiple discs may share one game.</summary>
        [JsonPropertyName("gameId")]
        public int GameId { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("serial")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Serial { get; set; }

        [JsonPropertyName("crc")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Crc { get; set; }

        [JsonPropertyName("region")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Region { get; set; }

        [JsonPropertyName("status")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Status { get; set; }

        [JsonPropertyName("lastTestedVersion")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LastTestedVersion { get; set; }

        /// <summary>Last-tested date as an ISO-8601 string, or null.</summary>
        [JsonPropertyName("lastTestedDate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LastTestedDate { get; set; }

        [JsonPropertyName("wikiLink")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? WikiLink { get; set; }

        [JsonPropertyName("forumLink")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ForumLink { get; set; }

        /// <summary>True when cover art for this game has already been cached.</summary>
        [JsonPropertyName("hasCover")]
        public bool HasCover { get; set; }
    }
}
