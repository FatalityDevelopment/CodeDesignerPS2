namespace CodeDesigner.Mips64LE.Common.Language.CDS.SyntaxItems
{
    /// <summary>
    /// Represents a <c>hexcode $XXXXXXXX</c> directive that emits a raw 32-bit word.
    /// </summary>
    public class HexCodeSyntaxItem : CdsSyntaxItem
    {
        /// <summary>The address at which this word is emitted.</summary>
        public uint Address { get; set; }

        /// <summary>The raw 32-bit value to emit.</summary>
        public uint Value { get; set; }
    }
}
