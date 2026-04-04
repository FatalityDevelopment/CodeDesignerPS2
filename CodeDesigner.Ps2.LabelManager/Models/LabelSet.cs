using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.LabelManager.Models
{
    /// <summary>
    /// A named collection of <see cref="LabelEntry"/> records associated with a game.
    /// One game can have multiple label sets (e.g. different patches, different authors).
    /// </summary>
    public class LabelSet
    {
        /// <summary>Display name shown in the drop-down.</summary>
        [JsonPropertyName("setName")]
        public string SetName { get; set; } = string.Empty;

        /// <summary>Game serial / ID this set belongs to, or null for global sets.</summary>
        [JsonPropertyName("gameId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? GameId { get; set; }

        /// <summary>True for the built-in set that ships with CodeDesigner (read-only).</summary>
        [JsonPropertyName("isBuiltIn")]
        public bool IsBuiltIn { get; set; }

        [JsonPropertyName("entries")]
        public List<LabelEntry> Entries { get; set; } = [];
    }
}
