using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.LabelManager.Models
{
    /// <summary>
    /// A unified label entry. The <see cref="Type"/> field controls which extra
    /// metadata fields are relevant and how the entry is expanded in the disassembly view.
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
        /// When <c>true</c> the entry is hidden from the disassembler view and the
        /// structs panel.
        /// </summary>
        [JsonPropertyName("hidden")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Hidden { get; set; }

        /// <summary>
        /// When <c>true</c> the live memory value at this address is polled and shown
        /// in the label manager table.
        /// </summary>
        [JsonPropertyName("dynamic")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Dynamic { get; set; }

        /// <summary>Semantic type of this entry. Defaults to <see cref="LabelType.Address"/>.</summary>
        [JsonPropertyName("type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public LabelType Type { get; set; } = LabelType.Address;

        // ── Type = Struct ─────────────────────────────────────────────────────

        /// <summary>Struct member fields (used when <see cref="Type"/> is <see cref="LabelType.Struct"/>).</summary>
        [JsonPropertyName("members")]
        public List<LabelMember> Members { get; set; } = [];

        // ── Type = Function ───────────────────────────────────────────────────

        /// <summary>Named argument registers, e.g. <c>["a0=playerPtr", "a1=flags"]</c>.</summary>
        [JsonPropertyName("argRegs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? ArgRegs { get; set; }

        /// <summary>Named return registers, e.g. <c>["v0=result"]</c>.</summary>
        [JsonPropertyName("returnRegs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? ReturnRegs { get; set; }

        // ── Type = Array ──────────────────────────────────────────────────────

        /// <summary>Byte stride between consecutive array elements.</summary>
        [JsonPropertyName("elementStride")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ElementStride { get; set; }

        /// <summary>Number of elements (null = unknown / dynamic).</summary>
        [JsonPropertyName("elementCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ElementCount { get; set; }

        // ── Type = LinkedList ─────────────────────────────────────────────────

        /// <summary>Byte offset within each node where the next-node pointer lives.</summary>
        [JsonPropertyName("nodeNextOffset")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? NodeNextOffset { get; set; }

        /// <summary>Total size in bytes of one node (informational).</summary>
        [JsonPropertyName("nodeSize")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? NodeSize { get; set; }
    }
}
