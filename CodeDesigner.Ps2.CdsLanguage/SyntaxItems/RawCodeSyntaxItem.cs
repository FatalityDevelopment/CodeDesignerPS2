namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents a raw cheat code line written straight into CDS source, e.g.
    /// <code>
    /// 20123456 000003E8
    /// </code>
    /// The pair is passed through unchanged by <see cref="CdsCompiler.CompileToCodes"/>.
    /// It is not a memory word at the current address, so it does not advance the
    /// address counter and is ignored by <see cref="CdsCompiler.Compile"/>.
    /// </summary>
    public sealed class RawCodeSyntaxItem : CdsSyntaxItem
    {
        /// <summary>First word of the code (type nibble + address, or a code-specific header).</summary>
        public uint CodeWord { get; init; }

        /// <summary>Second word of the code (value or code-specific operand).</summary>
        public uint ValueWord { get; init; }
    }
}
