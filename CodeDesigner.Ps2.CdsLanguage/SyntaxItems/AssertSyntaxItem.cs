namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents an <c>assert mem[$ADDRESS] == $VALUE</c> directive.
    /// <para>
    /// This is a <em>sidecar</em> item — it does not emit any MIPS code but instructs the
    /// bridge server to verify that the given PS2 memory location contains the expected value
    /// before applying any patches.  If the assertion fails the injection is aborted.
    /// </para>
    /// </summary>
    public class AssertSyntaxItem : CdsSyntaxItem
    {
        /// <summary>The PS2 virtual address to read and compare.</summary>
        public uint Address { get; set; }

        /// <summary>
        /// The comparison operator as a string: <c>"=="</c> or <c>"!="</c>.
        /// </summary>
        public string Operator { get; set; } = "==";

        /// <summary>The 32-bit value that the memory location must match.</summary>
        public uint ExpectedValue { get; set; }
    }
}
