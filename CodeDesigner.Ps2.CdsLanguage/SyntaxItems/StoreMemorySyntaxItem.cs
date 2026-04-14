namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents a typed memory store such as
    /// <c>store.w t0, mem[0x0100] s0</c>.
    /// </summary>
    public class StoreMemorySyntaxItem : CdsSyntaxItem
    {
        /// <summary>The assembly address of the generated store instruction.</summary>
        public uint Address { get; set; }

        /// <summary>The source register name.</summary>
        public string SourceRegister { get; set; } = string.Empty;

        /// <summary>The base-pointer register name.</summary>
        public string BaseRegister { get; set; } = string.Empty;

        /// <summary>The 16-bit signed memory offset used in the generated store.</summary>
        public uint MemoryOffset { get; set; }

        /// <summary>The data width used for the generated store.</summary>
        public CdsMemorySize MemorySize { get; set; } = CdsMemorySize.Word;

        /// <summary>The generated store instruction.</summary>
        public OperationSyntaxItem Operation { get; set; } = new();
    }
}
