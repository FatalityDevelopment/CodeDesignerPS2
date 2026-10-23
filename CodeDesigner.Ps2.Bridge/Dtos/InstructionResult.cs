namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Result for individual instruction assembly/disassembly.
    ///
    /// <para>
    /// Disassembly results may represent either a single 4-byte MIPS instruction
    /// or a typed data row (.byte/.half/.word/.dword/.qword) produced by a
    /// <c>DataFormatOverride</c>.  In the latter case <see cref="ByteLength"/>
    /// reflects the row's size and <see cref="Address"/> is the row's start
    /// address — both of which are mandatory because the caller cannot derive
    /// them from a fixed 4-byte stride any more.
    /// </para>
    /// </summary>
    public class InstructionResult
    {
        public bool Success { get; set; }
        public string? Assembly { get; set; }
        public string? Hex { get; set; }
        public string? Error { get; set; }
        public string? Comment { get; set; }  // Added for analyzer-generated comments
        public string? Label { get; set; }    // Symbol label from ps2MemMap.json
        public int? LineNumber { get; set; }  // 1-based source line number

        /// <summary>
        /// Address of this row.  For instruction rows this matches the PC the
        /// caller passed in; for data-format rows it advances by
        /// <see cref="ByteLength"/> instead of the default 4.
        /// </summary>
        public uint? Address { get; set; }

        /// <summary>
        /// Byte length of this row.  4 for normal instruction rows;
        /// 1/2/4/8/16 for data-format rows (.byte/.half/.word/.dword/.qword).
        /// </summary>
        public int? ByteLength { get; set; }

        /// <summary>
        /// Wire data-format kind for rows produced by a persisted override.
        /// Null for normal instruction rows and best-effort fallback bytes.
        /// </summary>
        public string? DataFormatKind { get; set; }

        /// <summary>
        /// True for rows the disassembler auto-formatted (e.g. a printable-ASCII
        /// run rendered as byte rows by per-page string detection) without a
        /// persisted <c>DataFormatOverride</c>. The webview styles these the
        /// same way as override rows but should not surface "Clear formatting"
        /// since there is nothing to clear in the database.
        /// </summary>
        public bool? AutoFormatted { get; set; }

        /// <summary>
        /// Code references to this row (calls, jumps, branches, built addresses,
        /// loads, stores) from the cross-reference index. Null when no index
        /// has been built or nothing references the row.
        /// </summary>
        public int? XrefCount { get; set; }

        /// <summary>Data words that point at this row, from the cross-reference index.</summary>
        public int? DataRefCount { get; set; }

        /// <summary>True when a detected function begins at this row.</summary>
        public bool? FunctionStart { get; set; }
    }
}

