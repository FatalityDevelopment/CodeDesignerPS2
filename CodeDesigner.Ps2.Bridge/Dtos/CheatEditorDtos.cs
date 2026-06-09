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

    /// <summary>Compact summary of a section inside a cheat list, for low-token browsing.</summary>
    public class CheatSectionInfo
    {
        [JsonPropertyName("listId")]
        public int ListId { get; set; }

        [JsonPropertyName("section")]
        public string Section { get; set; } = string.Empty;

        [JsonPropertyName("cheatCount")]
        public int CheatCount { get; set; }

        [JsonPropertyName("codeLineCount")]
        public int CodeLineCount { get; set; }

        [JsonPropertyName("firstCheatLabel")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FirstCheatLabel { get; set; }
    }

    /// <summary>Compact cheat search hit returned to MCP clients before fetching full code bodies.</summary>
    public class CheatSearchResultInfo
    {
        [JsonPropertyName("gameId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? GameId { get; set; }

        [JsonPropertyName("gameTitle")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? GameTitle { get; set; }

        [JsonPropertyName("listId")]
        public int ListId { get; set; }

        [JsonPropertyName("listTitle")]
        public string ListTitle { get; set; } = string.Empty;

        [JsonPropertyName("cheatId")]
        public int CheatId { get; set; }

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("section")]
        public string Section { get; set; } = string.Empty;

        [JsonPropertyName("codeLineCount")]
        public int CodeLineCount { get; set; }

        [JsonPropertyName("matchedFields")]
        public List<string> MatchedFields { get; set; } = [];
    }
}
