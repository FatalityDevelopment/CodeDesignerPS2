namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>One local variable or parameter entry returned by GETLOCALS.</summary>
    public class PineLocalInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("storageType")]
        public string StorageType { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public int Value { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
