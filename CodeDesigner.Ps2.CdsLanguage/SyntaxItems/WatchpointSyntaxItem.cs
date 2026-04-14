namespace CodeDesigner.Ps2.CdsLanguage.SyntaxItems
{
    /// <summary>Access-type filter for a memory watchpoint.</summary>
    public enum WatchpointAccessType
    {
        /// <summary>Break on writes only.</summary>
        Write,
        /// <summary>Break on reads only.</summary>
        Read,
        /// <summary>Break on reads and writes.</summary>
        ReadWrite
    }

    /// <summary>
    /// Represents a <c>watchpoint (write|read|rw) $ADDRESS SIZE</c> directive.
    /// <para>
    /// This is a <em>sidecar</em> item — it does not emit MIPS code but instructs the bridge
    /// server to install a memory watchpoint at the given address via a <c>PINEADDWATCH</c>
    /// command when the patch is applied.
    /// </para>
    /// </summary>
    public class WatchpointSyntaxItem : CdsSyntaxItem
    {
        /// <summary>The PS2 virtual address to watch.</summary>
        public uint Address { get; set; }

        /// <summary>Number of bytes to watch (typically 1, 2, or 4).</summary>
        public uint Size { get; set; }

        /// <summary>Whether to trigger on reads, writes, or both.</summary>
        public WatchpointAccessType AccessType { get; set; }
    }
}
