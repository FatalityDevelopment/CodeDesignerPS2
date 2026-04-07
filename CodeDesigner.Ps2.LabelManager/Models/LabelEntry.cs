using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.LabelManager.Models
{
    /// <summary>
    /// A unified label entry. Every entry may optionally have a <see cref="ParentAddress"/>
    /// + <see cref="Offset"/>, which makes the whole label tree truly recursive:
    /// children are just label entries whose identity is anchored to their parent rather
    /// than to a fixed PS2 virtual address.
    ///
    /// The <see cref="Type"/> field controls which extra metadata fields are relevant and
    /// how the entry is expanded in the disassembly view.
    /// </summary>
    public class LabelEntry
    {
        /// <summary>
        /// Virtual address as a hex string, e.g. "0x80010000".
        /// For root (top-level) entries this is the real PS2 EE virtual address.
        /// For child entries this holds a synthetic path key (e.g. "0x80010000:+0x0010")
        /// that uniquely identifies the entry within the set; the actual runtime address
        /// is resolved by following the parent pointer chain at query time.
        /// </summary>
        [JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        // ── Parent / child relationship ────────────────────────────────────────

        /// <summary>
        /// Address of the parent label within the same set.
        /// <c>null</c> for root (top-level) entries.
        /// </summary>
        [JsonPropertyName("parentAddress")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ParentAddress { get; set; }

        /// <summary>
        /// Byte offset from the value that the parent pointer holds.
        /// Only meaningful when <see cref="ParentAddress"/> is set.
        /// </summary>
        [JsonPropertyName("offset")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Offset { get; set; }

        // ── Core annotation fields ─────────────────────────────────────────────

        /// <summary>Human-readable name used in the disassembly view and CDS source.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Optional description shown as a tooltip.</summary>
        [JsonPropertyName("comment")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Comment { get; set; }

        /// <summary>
        /// Optional data-type annotation for this field, e.g. "u32", "float", "pointer".
        /// Applies to both root entries and children.
        /// </summary>
        [JsonPropertyName("valueType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ValueType { get; set; }

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
