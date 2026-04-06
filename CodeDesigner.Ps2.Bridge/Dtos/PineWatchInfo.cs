namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Wire representation of a single PINE memory watchpoint returned by
    /// <c>PINELISTWATCHES</c>.
    /// </summary>
    public class PineWatchInfo
    {
        /// <summary>CPU the watchpoint applies to: "EE" or "IOP".</summary>
        public string Cpu   { get; set; } = string.Empty;
        /// <summary>Inclusive start address of the watched range (hex, e.g. "0x00200000").</summary>
        public string Start { get; set; } = string.Empty;
        /// <summary>Exclusive end address of the watched range (hex, e.g. "0x00200004").</summary>
        public string End   { get; set; } = string.Empty;
        /// <summary>
        /// Human-readable access condition: "read", "write", or "read+write".
        /// </summary>
        public string Cond  { get; set; } = string.Empty;
    }
}
