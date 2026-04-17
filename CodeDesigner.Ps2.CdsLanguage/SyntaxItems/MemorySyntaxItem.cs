namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>Kind of arithmetic/assignment used in a <c>mem[…]</c> statement.</summary>
    public enum CdsMemoryOperation
    {
        Assign,
        Add,
        Subtract,
        Multiply,
        Divide
    }

    /// <summary>
    /// The data width used by a <c>mem[…]</c> directive.
    /// Controls which MIPS load/store instructions are emitted.
    /// </summary>
    public enum CdsMemorySize
    {
        /// <summary>8-bit signed byte — uses <c>LB</c> / <c>SB</c>.</summary>
        Byte,
        /// <summary>16-bit signed half-word — uses <c>LH</c> / <c>SH</c>.</summary>
        Half,
        /// <summary>32-bit word (default) — uses <c>LW</c> / <c>SW</c>.</summary>
        Word,
        /// <summary>64-bit double-word — uses <c>LD</c> / <c>SD</c>.</summary>
        Doubleword
    }

    /// <summary>
    /// Represents a memory operation such as
    /// <c>mem[0x0100] t0 = 0x01</c> or <c>mem.h[0x0100] t0 += 5</c>.
    /// <para>
    /// The operand register acts as a base pointer; the hex address is the 16-bit signed offset
    /// in the resulting MIPS load/store instruction.
    /// </para>
    /// <para>
    /// Expands to 2–3 MIPS instructions stored in <see cref="Operations"/>.
    /// The optional size suffix (<c>.b</c>, <c>.h</c>, <c>.w</c>, <c>.d</c>) selects the
    /// data width; the default is <see cref="CdsMemorySize.Word"/>.
    /// </para>
    /// </summary>
    public class MemorySyntaxItem : CdsSyntaxItem
    {
        /// <summary>The assembly address of the first generated instruction.</summary>
        public uint Address { get; set; }

        /// <summary>The 16-bit signed memory offset used in the MIPS instruction (e.g., 0x0100).</summary>
        public uint MemoryOffset { get; set; }

        /// <summary>The base-pointer register name (e.g., <c>t0</c>).</summary>
        public string Register { get; set; } = string.Empty;

        /// <summary>The kind of operation to perform.</summary>
        public CdsMemoryOperation Operation { get; set; }

        /// <summary>The immediate operand value.</summary>
        public uint Operand { get; set; }

        /// <summary>
        /// The data width for the generated load/store instructions.
        /// Defaults to <see cref="CdsMemorySize.Word"/> for backward compatibility.
        /// </summary>
        public CdsMemorySize MemorySize { get; set; } = CdsMemorySize.Word;

        /// <summary>Generated MIPS instruction items (2 for assign, 3 for compound ops).</summary>
        public List<OperationSyntaxItem> Operations { get; set; } = new();
    }
}
