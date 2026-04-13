using CodeDesigner.Ps2.CdsLanguage.SyntaxItems;

namespace CodeDesigner.Ps2.CdsLanguage
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

        /// <summary>
        /// Extract all <em>sidecar</em> items from the syntax tree.
        /// Sidecar items (<see cref="AssertSyntaxItem"/>, <see cref="BreakpointSyntaxItem"/>,
        /// <see cref="WatchpointSyntaxItem"/>, <see cref="LogSyntaxItem"/>) do not emit MIPS code but carry instructions
        /// for the bridge server (e.g., install breakpoints, verify memory before patching).
        /// </summary>
        /// <param name="items">Syntax items returned by <see cref="CdsParser.Parse"/>.</param>
        /// <returns>
        /// A flat list of sidecar items in source order, including those inside scope bodies.
        /// </returns>
        public static List<CdsSyntaxItem> GetSidecarCommands(IEnumerable<CdsSyntaxItem> items)
        {
            var result = new List<CdsSyntaxItem>();
            CollectSidecars(items, result);
            return result;
        }

        /// <summary>
        /// Flatten all <see cref="FreezeSyntaxItem"/> and <see cref="CondSyntaxItem"/> items
        /// in the syntax tree into a list of raw cheat-table entries compatible with
        /// <c>CodeDesigner.Ps2.CheatEngine.CdsCheatEngine</c>.
        ///
        /// <para>
        /// Each entry is a pair <c>(TypeAddr, Value)</c> where <c>TypeAddr</c> encodes the
        /// cheat type in the upper byte and the lower 24 bits of the target address in bits
        /// 23–0 — the same layout used by the engine's 8-byte table rows.
        /// </para>
        ///
        /// <para>
        /// A <see cref="CondSyntaxItem"/> emits a condition entry first (type 0xC0), then
        /// the entries for its body; this matches the engine's evaluation order.
        /// </para>
        /// </summary>
        public static List<(uint TypeAddr, uint Value)> GetCheatTableEntries(IEnumerable<CdsSyntaxItem> items)
        {
            var result = new List<(uint, uint)>();
            CollectCheatEntries(items, result);
            return result;
        }

        private static void CollectCheatEntries(IEnumerable<CdsSyntaxItem> items, List<(uint, uint)> result)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case FreezeSyntaxItem fr:
                    {
                        uint typeAddr = ((uint)fr.FreezeType << 24) | (fr.Address & 0x00FFFFFF);
                        result.Add((typeAddr, fr.Value));
                        break;
                    }

                    case CondSyntaxItem cd:
                    {
                        // Emit the condition entry (type 0xC0), then the body entries
                        uint typeAddr = ((uint)cd.FreezeType << 24) | (cd.Address & 0x00FFFFFF);
                        result.Add((typeAddr, cd.ConditionValue));
                        CollectCheatEntries(cd.Body, result);
                        break;
                    }

                    case ScopeSyntaxItem scope:
                        CollectCheatEntries(scope.Body, result);
                        break;
                }
            }
        }

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

                    // AssertSyntaxItem, BreakpointSyntaxItem, WatchpointSyntaxItem, LogSyntaxItem:
                    //   sidecar items — no code words emitted (see GetSidecarCommands).
                    // AddressSyntaxItem, LabelSyntaxItem, CommentSyntaxItem, IncludeSyntaxItem:
                    //   metadata — no words emitted.
                }
            }
        }

        private static void CollectSidecars(IEnumerable<CdsSyntaxItem> items, List<CdsSyntaxItem> result)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case AssertSyntaxItem:
                    case BreakpointSyntaxItem:
                    case WatchpointSyntaxItem:
                    case LogSyntaxItem:
                        result.Add(item);
                        break;

                    case ScopeSyntaxItem scope:
                        CollectSidecars(scope.Body, result);
                        break;
                }
            }
        }
    }
}
