namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents an <c>include "file.cds"</c> directive.
    /// </summary>
    public class IncludeSyntaxItem : CdsSyntaxItem
    {
        /// <summary>Full resolved path of the file to include.</summary>
        public string FilePath { get; set; } = string.Empty;
    }
}
