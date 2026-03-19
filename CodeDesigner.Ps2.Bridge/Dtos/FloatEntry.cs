namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>A plausible 32-bit IEEE-754 float found at a 4-byte aligned address.</summary>
    public class FloatEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public float  Value   { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("raw")]
        public string Raw     { get; set; } = string.Empty;
    }
}
