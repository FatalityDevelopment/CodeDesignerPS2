namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// The 4-byte value at a specific EE address as recorded in one memory snapshot.
    /// Returned by the GETADDRESSHISTORY command.
    /// </summary>
    public class AddressHistoryEntry
    {
        /// <summary>Human-readable name of the snapshot (from snapshot-info.json).</summary>
        public string SnapshotName { get; set; } = string.Empty;

        /// <summary>ISO 8601 UTC timestamp of when the snapshot was taken.</summary>
        public string Timestamp { get; set; } = string.Empty;

        /// <summary>
        /// 4-byte little-endian value at the requested address, zero-padded to 8
        /// hex digits, e.g. "00640000".
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// Disassembled instruction string, if the address falls in an executable
        /// region and disassembly was requested.  May be <c>null</c>.
        /// </summary>
        public string? Disasm { get; set; }
    }
}
