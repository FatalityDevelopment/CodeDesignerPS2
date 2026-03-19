namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>One match returned by the SEARCH command.</summary>
    public class SearchMatch
    {
        [System.Text.Json.Serialization.JsonPropertyName("instructionIndex")]
        public int InstructionIndex { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public uint Address { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("field")]
        public string Field { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("start")]
        public int Start { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("length")]
        public int Length { get; set; }
    }
}
