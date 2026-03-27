using System.Collections.Generic;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>One member field inside a struct definition.</summary>
    public class StructMemberEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("offset")]
        public int Offset { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>A user-defined struct with a name, base address, and optional member list.</summary>
    public class StructEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("baseAddress")]
        public string BaseAddress { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("members")]
        public List<StructMemberEntry> Members { get; set; } = new();
    }
}
