namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>A non-zero 4-byte aligned 32-bit integer found in memory.</summary>
    public class IntegerEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public int    Value   { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("hex")]
        public string Hex     { get; set; } = string.Empty;
    }
}
