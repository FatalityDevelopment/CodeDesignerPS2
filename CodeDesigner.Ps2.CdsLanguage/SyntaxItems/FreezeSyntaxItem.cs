namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents a <c>freeze</c> directive that continuously re-writes a memory location
    /// every game frame via the CDS cheat engine.
    ///
    /// <para>Syntax:</para>
    /// <code>
    /// freeze byte $20347A50 = 0x63           // 8-bit  → CdsFreezeType.Byte  (0x00)
    /// freeze half $20347A52 = 0x03E7         // 16-bit → CdsFreezeType.Half  (0x10)
    /// freeze word $20347A54 = 0xFFFFFFFF     // 32-bit → CdsFreezeType.Word  (0x20)
    /// </code>
    ///
    /// <para>
    /// A <see cref="FreezeSyntaxItem"/> is a <em>sidecar</em> item: it does not emit MIPS
    /// instructions.  Use <see cref="CdsCompiler.GetCheatTableEntries"/> to convert all
    /// freeze / cond items in a parsed tree into a list of <c>(TypeAddr, Value)</c> tuples
    /// compatible with <c>CodeDesigner.Ps2.CheatEngine.CdsCheatEngine</c>.
    /// </para>
    /// </summary>
    public sealed class FreezeSyntaxItem : CdsSyntaxItem
    {
        /// <summary>Cheat type: Byte (0x00), Half (0x10), or Word (0x20).</summary>
        public CdsFreezeType FreezeType { get; init; }

        /// <summary>Target PS2 virtual address (lower 24 bits are used by the engine).</summary>
        public uint Address { get; init; }

        /// <summary>Value to freeze at <see cref="Address"/>.</summary>
        public uint Value { get; init; }
    }
}
