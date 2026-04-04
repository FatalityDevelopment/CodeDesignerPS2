using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.LabelManager.Models
{
    /// <summary>
    /// A unified label / struct entry.
    /// When <see cref="Members"/> is empty this is a plain address label.
    /// When <see cref="Members"/> is non-empty the entry acts as a struct pointer:
    /// the address holds a 32-bit pointer whose dereferenced value is the struct base,
    /// and each member describes a named field at a byte offset from that base.
    /// </summary>
    public class LabelEntry
    {
        /// <summary>Virtual address as a hex string, e.g. "0x80010000".</summary>
        [JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        /// <summary>Human-readable name used in the disassembly view and CDS source.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Optional description shown as a tooltip.</summary>
        [JsonPropertyName("comment")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Comment { get; set; }

        /// <summary>
        /// Struct member fields. Empty list = plain label; non-empty = struct pointer.
        /// </summary>
        [JsonPropertyName("members")]
        public List<LabelMember> Members { get; set; } = [];
    }
}
