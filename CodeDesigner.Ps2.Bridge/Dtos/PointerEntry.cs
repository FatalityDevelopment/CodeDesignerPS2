namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>A 4-byte aligned value that looks like a valid EE/IOP memory pointer.</summary>
    public class PointerEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address  { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("pointsTo")]
        public string PointsTo { get; set; } = string.Empty;
    }
}
