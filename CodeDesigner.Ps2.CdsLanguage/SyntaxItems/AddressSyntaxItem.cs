namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents an <c>address $XXXXXXXX</c> directive that sets the current assembly address.
    /// </summary>
    public class AddressSyntaxItem : CdsSyntaxItem
    {
        /// <summary>The address value specified by the directive.</summary>
        public uint Address { get; set; }
    }
}
