namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents a <c>log "message"</c> or <c>print "message"</c> directive.
    /// <para>
    /// This is a <em>sidecar</em> item — it does not emit MIPS code but carries a textual
    /// message for tooling to surface during CDS compile/apply workflows.
    /// </para>
    /// </summary>
    public sealed class LogSyntaxItem : CdsSyntaxItem
    {
        /// <summary>
        /// The PS2 virtual address associated with the directive, captured from the current
        /// assembly address when the directive appears.
        /// </summary>
        public uint Address { get; set; }

        /// <summary>The normalized directive keyword, e.g. <c>log</c> or <c>print</c>.</summary>
        public string Directive { get; set; } = "log";

        /// <summary>The message text to surface.</summary>
        public string MessageText { get; set; } = string.Empty;
    }
}
