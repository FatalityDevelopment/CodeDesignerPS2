namespace CodeDesigner.Ps2.CdsLanguage
{
    /// <summary>
    /// Cheat-table entry type used by <see cref="SyntaxItems.FreezeSyntaxItem"/> and
    /// <see cref="SyntaxItems.CondSyntaxItem"/>.
    ///
    /// <para>
    /// Values match the GameShark / CDS cheat-engine type byte (upper byte of the first
    /// word in each 8-byte cheat-table entry).
    /// </para>
    /// </summary>
    public enum CdsFreezeType : uint
    {
        /// <summary>8-bit write — GameShark type 0x00.</summary>
        Byte = 0x00,

        /// <summary>16-bit write — GameShark type 0x10.</summary>
        Half = 0x10,

        /// <summary>32-bit write — GameShark type 0x20.</summary>
        Word = 0x20,

        /// <summary>32-bit conditional — GameShark type 0xC0.  Body entries execute only when condition is true.</summary>
        Condition32 = 0xC0,
    }
}
