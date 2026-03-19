namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>One disassembled instruction line returned by PINEDISASSEMBLE.</summary>
    public class PineDisassemblyLineInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }
}
