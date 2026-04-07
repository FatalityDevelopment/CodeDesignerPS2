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

        /// <summary>
        /// Optional data-type annotation for this field, e.g. "u32", "float", "pointer".
        /// Stored as a free-form string so new types can be added without schema changes.
        /// </summary>
        [JsonPropertyName("valueType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ValueType { get; set; }

        /// <summary>
        /// When <c>true</c> the live memory value at the resolved address for this
        /// member is polled and shown in the label manager table (mirrors
        /// <see cref="LabelEntry.Dynamic"/> for top-level entries).
        /// </summary>
        [JsonPropertyName("dynamic")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Dynamic { get; set; }

        /// <summary>
        /// When <c>true</c> the member is hidden from the label manager table and the
        /// struct member display in the disassembly view (mirrors
        /// <see cref="LabelEntry.Hidden"/> for top-level entries).
        /// </summary>
        [JsonPropertyName("hidden")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Hidden { get; set; }
    }
}
