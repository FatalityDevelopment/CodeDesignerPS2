namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents a <c>string "text"</c> directive that embeds a null-terminated ASCII string,
    /// padded to 4-byte alignment, as a sequence of raw 32-bit words.
    /// </summary>
    public class StringSyntaxItem : CdsSyntaxItem
    {
        /// <summary>The address at which the first word is emitted.</summary>
        public uint Address { get; set; }

        /// <summary>The string literal (without quotes or null terminator).</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// The 32-bit words produced from the null-terminated, 4-byte-aligned byte sequence.
        /// </summary>
        public List<HexCodeSyntaxItem> Words { get; set; } = new();
    }
}
