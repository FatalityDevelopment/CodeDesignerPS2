namespace CodeDesigner.Mips64LE.Common.Language.CDS.SyntaxItems
{
    /// <summary>
    /// Represents a <c>setreg reg, $XXXXXXXX</c> directive that loads a 32-bit immediate
    /// into a register via a <c>LUI</c> + <c>ADDIU</c> pair.
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

        /// <summary>The generated <c>ADDIU reg, reg, lo16</c> instruction.</summary>
        public OperationSyntaxItem AddiuOp { get; set; } = new();
    }
}
