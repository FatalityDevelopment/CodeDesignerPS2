namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>A detected function entry point.</summary>
    public class FunctionEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("label")]
        public string Label   { get; set; } = string.Empty;
    }
}
