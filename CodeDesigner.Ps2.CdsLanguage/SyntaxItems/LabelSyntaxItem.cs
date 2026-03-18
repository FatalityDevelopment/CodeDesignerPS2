namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents a label definition (<c>name:</c>) that records the current address.
    /// </summary>
    public class LabelSyntaxItem : CdsSyntaxItem
    {
        /// <summary>The address the label resolves to.</summary>
        public uint Address { get; set; }

        /// <summary>The local (unqualified) label name as written in source.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The fully scope-qualified label name used for cross-scope references,
        /// e.g., <c>outer.inner.loop</c>. Equal to <see cref="Name"/> for global labels.
        /// </summary>
        public string FullName { get; set; } = string.Empty;
    }
}
