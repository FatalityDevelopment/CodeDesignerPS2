namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Base class for all CDS language syntax items produced by the parser.
    /// </summary>
    public abstract class CdsSyntaxItem
    {
        /// <summary>Source line number (1-based).</summary>
        public int LineNumber { get; set; }

        /// <summary>Original source text of the line that produced this item.</summary>
        public string LineText { get; set; } = string.Empty;
    }
}
