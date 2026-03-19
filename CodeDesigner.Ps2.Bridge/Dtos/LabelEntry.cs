namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>One entry in the label/symbol database.</summary>
    public class LabelEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string  Address { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("label")]
        public string  Label   { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("comment")]
        public string? Comment { get; set; }
    }
}
