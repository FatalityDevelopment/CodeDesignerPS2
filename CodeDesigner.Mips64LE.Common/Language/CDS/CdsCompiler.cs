using CodeDesigner.Mips64LE.Common.Language.CDS.SyntaxItems;

namespace CodeDesigner.Mips64LE.Common.Language.CDS
{
    /// <summary>
    /// Compiles a list of <see cref="CdsSyntaxItem"/> objects produced by <see cref="CdsParser"/>
    /// into a flat list of <c>(address, word)</c> pairs representing the raw 32-bit memory
    /// writes that make up the assembled output.
    /// </summary>
    public static class CdsCompiler
    {
        /// <summary>
        /// Compile the syntax-item tree into an ordered sequence of (address, word) pairs.
        /// Scope bodies are flattened recursively.
        /// </summary>
        /// <param name="items">Syntax items returned by <see cref="CdsParser.Parse"/>.</param>
        /// <returns>
        /// A list of <c>(uint Address, uint Word)</c> tuples, one per 32-bit memory location
        /// that should be written.  Items are returned in the order they appear in the source.
        /// </returns>
        public static List<(uint Address, uint Word)> Compile(IEnumerable<CdsSyntaxItem> items)
        {
            var result = new List<(uint, uint)>();
            Collect(items, result);
            return result;
        }

        // -----------------------------------------------------------------------
        // Recursive collector
        // -----------------------------------------------------------------------

        private static void Collect(IEnumerable<CdsSyntaxItem> items, List<(uint, uint)> result)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case OperationSyntaxItem op:
                        result.Add((op.Address, op.EncodedWord));
                        break;

                    case HexCodeSyntaxItem hex:
                        result.Add((hex.Address, hex.Value));
                        break;

                    case SetRegSyntaxItem setReg:
                        result.Add((setReg.LuiOp.Address,   setReg.LuiOp.EncodedWord));
                        result.Add((setReg.AddiuOp.Address, setReg.AddiuOp.EncodedWord));
                        break;

                    case MemorySyntaxItem mem:
                        foreach (var op in mem.Operations)
                            result.Add((op.Address, op.EncodedWord));
                        break;

                    case StringSyntaxItem str:
                        foreach (var word in str.Words)
                            result.Add((word.Address, word.Value));
                        break;

                    case ScopeSyntaxItem scope:
                        // Recurse into scope body so nested items are included
                        Collect(scope.Body, result);
                        break;

                    // AddressSyntaxItem, LabelSyntaxItem, CommentSyntaxItem, IncludeSyntaxItem:
                    // these are metadata; no words are emitted directly
                }
            }
        }
    }
}
