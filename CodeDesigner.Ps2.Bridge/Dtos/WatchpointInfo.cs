namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Summary of a single active memory watchpoint, returned by the LISTWATCHPOINTS command.
    /// </summary>
    public class WatchpointInfo
    {
        /// <summary>Unique watchpoint identifier (e.g. "wp-0").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Virtual EE address as an 8-digit hex string (e.g. "01E7EA60").</summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>Size in bytes of the watched range.</summary>
        public int Size { get; set; }

        /// <summary>Access type: "write", "read", or "readwrite".</summary>
        public string Type { get; set; } = string.Empty;
    }
}
