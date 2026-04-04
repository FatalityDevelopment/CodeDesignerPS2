namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// One entry in the server-side navigation history.
    /// Populated automatically from address-bearing socket commands (READMEM,
    /// DISASSEMBLE, SETBP, …) and from explicit RECORDNAV calls pushed by the
    /// VS Code extension when the user navigates in the disasm editor.
    /// </summary>
    public class NavHistoryEntry
    {
        /// <summary>Canonical KSEG0 virtual address, e.g. "0x8054ABCD".</summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>Label from ps2MemMap.json, or <c>null</c> if none.</summary>
        public string? Label { get; set; }

        /// <summary>
        /// Source of the navigation: "user" (VS Code user action),
        /// "mcp" (AI socket command), or "navigate" (explicit navigate_to call).
        /// </summary>
        public string Source { get; set; } = "mcp";

        /// <summary>ISO 8601 UTC timestamp of when the navigation was recorded.</summary>
        public string Timestamp { get; set; } = string.Empty;
    }
}
