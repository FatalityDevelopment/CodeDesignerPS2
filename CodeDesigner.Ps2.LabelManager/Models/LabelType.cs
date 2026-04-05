using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.LabelManager.Models
{
    /// <summary>
    /// Describes the semantic type of a <see cref="LabelEntry"/>.
    /// The type controls how the entry is expanded in the disassembly view and
    /// what extra metadata fields are relevant.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LabelType
    {
        /// <summary>
        /// Plain address marker. The label name appears at the address in the
        /// disassembly view. This is the default when no type is specified.
        /// </summary>
        Address = 0,

        /// <summary>
        /// Struct pointer. The address holds a 32-bit pointer whose dereferenced
        /// value is the struct base. Each <see cref="LabelEntry.Members"/> field
        /// describes a named byte-offset within the struct.
        /// </summary>
        Struct = 1,

        /// <summary>
        /// Function entry point. Displayed as <c>FNC_Name</c> in the disassembly
        /// view. Optional <see cref="LabelEntry.ArgRegs"/> and
        /// <see cref="LabelEntry.ReturnRegs"/> annotate the calling convention.
        /// </summary>
        Function = 2,

        /// <summary>
        /// Pointer to a contiguous array. The label is expanded to
        /// <c>Name[0]</c>, <c>Name[1]</c>, … for each element address derived
        /// from <see cref="LabelEntry.ElementStride"/> and
        /// <see cref="LabelEntry.ElementCount"/>.
        /// </summary>
        Array = 3,

        /// <summary>
        /// Pointer to an array of pointers. The base address is labelled;
        /// individual element pointers are not expanded statically.
        /// </summary>
        ArrayOfPointers = 4,

        /// <summary>
        /// Linked list. The base address is the first node. Runtime traversal
        /// via RESOLVELINKEDLIST walks the chain using
        /// <see cref="LabelEntry.NodeNextOffset"/> and
        /// <see cref="LabelEntry.NodeSize"/> to assign indexed labels to each node.
        /// </summary>
        LinkedList = 5,

        /// <summary>
        /// Pointer to a null-terminated ASCII string in memory. Runtime
        /// resolution via RESOLVESTRINGS reads the content and displays it
        /// alongside the label name (PINE / PCSX2 only).
        /// </summary>
        String = 6,
    }
}
