namespace CodeDesigner.Mips64LE.Common.Language.CDS.SyntaxItems
{
    /// <summary>
    /// Represents a single MIPS instruction emitted by the CDS assembler.
    /// </summary>
    public class OperationSyntaxItem : CdsSyntaxItem
    {
        /// <summary>The address at which this instruction is placed.</summary>
        public uint Address { get; set; }

        /// <summary>The 32-bit encoded instruction word.</summary>
        public uint EncodedWord { get; set; }

        /// <summary>The instruction mnemonic (e.g., <c>add</c>, <c>beq</c>).</summary>
        public string Mnemonic { get; set; } = string.Empty;

        /// <summary>
        /// The fully-qualified label name that was resolved for this instruction's target,
        /// or <see langword="null"/> if no label reference was used.
        /// </summary>
        public string? ResolvedLabel { get; set; }
    }
}
