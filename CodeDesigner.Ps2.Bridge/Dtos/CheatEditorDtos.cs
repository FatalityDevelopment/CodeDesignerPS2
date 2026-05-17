using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Wire payload for a game that owns at least one cheat list — used by the
    /// cheats editor's game picker.
    /// </summary>
    public class CheatGameInfo
    {
        [JsonPropertyName("gameId")]
        public int GameId { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        /// <summary>A disc serial that cover art can be requested with, if any.</summary>
        [JsonPropertyName("coverSerial")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CoverSerial { get; set; }

        [JsonPropertyName("hasCover")]
        public bool HasCover { get; set; }

        /// <summary>Number of cheat lists ("builds") this game has.</summary>
        [JsonPropertyName("listCount")]
        public int ListCount { get; set; }
    }

    /// <summary>
    /// Wire payload for a single cheat list ("build") shown in the editor's
    /// left-hand build list.
    /// </summary>
    public class CheatListInfo
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("gameId")]
        public int GameId { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("cheatCount")]
        public int CheatCount { get; set; }

        [JsonPropertyName("region")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Region { get; set; }

        [JsonPropertyName("serial")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Serial { get; set; }

        [JsonPropertyName("crc")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Crc { get; set; }

        [JsonPropertyName("updatedAt")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? UpdatedAt { get; set; }
    }
}
