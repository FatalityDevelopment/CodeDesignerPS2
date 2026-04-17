namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents a pointer-chain write directive such as
    /// <c>pointer $20347A50 + $0100 + $0048 = $000003E7</c>.
    /// <para>
    /// The directive expands into MIPS instructions that:
    /// </para>
    /// <list type="number">
    ///   <item>load the base pointer from <see cref="BaseAddress"/></item>
    ///   <item>follow each intermediate offset in <see cref="Offsets"/></item>
    ///   <item>store <see cref="Value"/> at the final offset</item>
    /// </list>
    /// </summary>
    public sealed class PointerSyntaxItem : CdsSyntaxItem
    {
        /// <summary>The assembly address of the first generated instruction.</summary>
        public uint Address { get; set; }

        /// <summary>Absolute PS2 virtual address holding the first pointer in the chain.</summary>
        public uint BaseAddress { get; set; }

        /// <summary>
        /// Pointer-chain offsets. All offsets except the final one are dereferenced with
        /// <c>lw scratch, offset(scratch)</c>; the final offset is used by the terminating store.
        /// </summary>
        public List<ushort> Offsets { get; set; } = new();

        /// <summary>The 32-bit value written through the resolved pointer chain.</summary>
        public uint Value { get; set; }

        /// <summary>Generated MIPS instructions for the full pointer write expansion.</summary>
        public List<OperationSyntaxItem> Operations { get; set; } = new();
    }
}
