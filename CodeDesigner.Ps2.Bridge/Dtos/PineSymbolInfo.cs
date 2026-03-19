namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>One EE symbol entry returned by LISTFUNCTIONS / LISTGLOBALS.</summary>
    public class PineSymbolInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("size")]
        public uint Size { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
