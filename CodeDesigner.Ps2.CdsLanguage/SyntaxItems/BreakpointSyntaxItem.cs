namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents a <c>breakpoint</c> directive.
    /// <para>
    /// This is a <em>sidecar</em> item — it does not emit MIPS code but records the current
    /// assembly address so that the bridge server can install a hardware breakpoint there
    /// via a <c>SETBP</c> command when the patch is applied.
    /// </para>
    /// </summary>
    public class BreakpointSyntaxItem : CdsSyntaxItem
    {
        /// <summary>
        /// The PS2 virtual address at which the breakpoint should be installed.
        /// Set to the value of the address counter at the point the directive appears.
        /// </summary>
        public uint Address { get; set; }
    }
}
