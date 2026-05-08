namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents an <c>la reg, :labelname</c> directive that loads the absolute address
    /// of a label into a register via a <c>LUI</c> + <c>ORI</c> pair.
    /// </summary>
    /// <remarks>
    /// Uses <c>ORI</c> rather than <c>ADDIU</c> so the low-16 immediate is treated as
    /// unsigned and addresses with bit 15 of the lower half set encode correctly.
    /// </remarks>
    public class LaSyntaxItem : CdsSyntaxItem
    {
        /// <summary>The address of the first generated instruction (<c>LUI</c>).</summary>
        public uint Address { get; set; }

        /// <summary>The target register name (e.g., <c>t0</c>).</summary>
        public string Register { get; set; } = string.Empty;

        /// <summary>The fully-qualified label that was resolved.</summary>
        public string ResolvedLabel { get; set; } = string.Empty;

        /// <summary>The 32-bit address the label resolved to.</summary>
        public uint Value { get; set; }

        /// <summary>The generated <c>LUI reg, hi16</c> instruction.</summary>
        public OperationSyntaxItem LuiOp { get; set; } = new();

        /// <summary>The generated <c>ORI reg, reg, lo16</c> instruction.</summary>
        public OperationSyntaxItem OriOp { get; set; } = new();
    }
}
