namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>One IOP module entry returned by GETMODULES.</summary>
    public class IopModuleInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("version")]
        public ushort Version { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("textAddr")]
        public string TextAddr { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("entry")]
        public string Entry { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("gp")]
        public string GP { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("textSize")]
        public uint TextSize { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dataSize")]
        public uint DataSize { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("bssSize")]
        public uint BssSize { get; set; }
    }
}
