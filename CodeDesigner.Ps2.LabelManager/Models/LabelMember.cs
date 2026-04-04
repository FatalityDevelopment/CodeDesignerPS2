using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.LabelManager.Models
{
    /// <summary>
    /// One member field within a label that acts as a struct pointer.
    /// Offset is the byte distance from the dereferenced pointer value.
    /// </summary>
    public class LabelMember
    {
        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
