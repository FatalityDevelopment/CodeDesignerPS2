namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>
    /// Represents a typed memory load such as
    /// <c>load.w t0, mem[0x0100] s0</c>.
    /// </summary>
    public class LoadMemorySyntaxItem : CdsSyntaxItem
    {
        /// <summary>The assembly address of the generated load instruction.</summary>
        public uint Address { get; set; }

        /// <summary>The destination register name.</summary>
        public string TargetRegister { get; set; } = string.Empty;

        /// <summary>The base-pointer register name.</summary>
        public string BaseRegister { get; set; } = string.Empty;

        /// <summary>The 16-bit signed memory offset used in the generated load.</summary>
        public uint MemoryOffset { get; set; }

        /// <summary>The data width used for the generated load.</summary>
        public CdsMemorySize MemorySize { get; set; } = CdsMemorySize.Word;

        /// <summary>The generated load instruction.</summary>
        public OperationSyntaxItem Operation { get; set; } = new();
    }
}
