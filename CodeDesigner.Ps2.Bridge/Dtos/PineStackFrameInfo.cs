namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>One call-stack frame returned by GETSTACK.</summary>
    public class PineStackFrameInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("entry")]
        public string Entry { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("pc")]
        public string PC { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("sp")]
        public string SP { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("stackSize")]
        public uint StackSize { get; set; }
    }
}
