namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents a single-line (<c>// …</c>) or multi-line (<c>/* … */</c>) comment.
    /// </summary>
    public class CommentSyntaxItem : CdsSyntaxItem
    {
        /// <summary>Comment text with leading markers stripped.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>True when the comment spans more than one source line.</summary>
        public bool IsMultiLine { get; set; }
    }
}
