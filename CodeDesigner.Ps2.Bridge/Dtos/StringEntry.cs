namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>A null-terminated ASCII string found in the binary.</summary>
    public class StringEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public string Value   { get; set; } = string.Empty;
    }
}
