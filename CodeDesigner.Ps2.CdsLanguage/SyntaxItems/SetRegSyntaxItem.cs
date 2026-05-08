namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents a <c>setreg reg, $XXXXXXXX</c> directive that loads a 32-bit immediate
    /// into a register via a <c>LUI</c> + <c>ORI</c> pair. <c>ORI</c> is used instead of
    /// <c>ADDIU</c> because its 16-bit immediate is zero-extended rather than sign-extended,
    /// so values whose lo16 has bit 15 set (e.g. <c>$80108000</c>) load correctly.
    /// </summary>
    public class SetRegSyntaxItem : CdsSyntaxItem
    {
        /// <summary>The address of the first generated instruction (<c>LUI</c>).</summary>
        public uint Address { get; set; }

        /// <summary>The target register name (e.g., <c>t0</c>).</summary>
        public string Register { get; set; } = string.Empty;

        /// <summary>The 32-bit value to load into the register.</summary>
        public uint Value { get; set; }

        /// <summary>The generated <c>LUI reg, hi16</c> instruction.</summary>
        public OperationSyntaxItem LuiOp { get; set; } = new();

        /// <summary>The generated <c>ORI reg, reg, lo16</c> instruction.</summary>
        public OperationSyntaxItem OriOp { get; set; } = new();
    }
}
