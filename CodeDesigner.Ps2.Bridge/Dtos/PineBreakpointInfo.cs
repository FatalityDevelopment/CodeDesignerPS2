namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>One breakpoint entry returned by LISTBP.</summary>
    public class PineBreakpointInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("cpu")]
        public string Cpu { get; set; } = string.Empty;
    }
}
