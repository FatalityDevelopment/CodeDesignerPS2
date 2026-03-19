namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// A single address-value match returned by the SCANVALUE command.
    /// </summary>
    public class ScanValueResult
    {
        /// <summary>Memory address as a hex string, e.g. "0x00100000".</summary>
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;
        /// <summary>Value read at that address, formatted as a decimal or float string.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;
    }
}
