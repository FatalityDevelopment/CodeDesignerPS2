using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Command message format for socket communication
    /// </summary>
    public class CommandMessage
    {
        public string Command { get; set; } = string.Empty;
        public string? Data { get; set; }
        public uint? Address { get; set; }
        public int? Length { get; set; }
        public string? Region { get; set; }
        public string? OutputPath { get; set; }
        public string? InputPath { get; set; }
        public uint? BreakpointAddress { get; set; }
        public string? Label { get; set; }
        public string? Comment { get; set; }
        public string? Query { get; set; }
        public string? SearchMode { get; set; }
        // SETREG fields
        public string? RegisterName { get; set; }
        public int? RegisterIndex { get; set; }
        public string? RegisterValue { get; set; }
        // LISTDISCELFS / RECOMPILE fields
        public string? IsoPath { get; set; }
        public string? ElfName { get; set; }
        public string? OutputDirectory { get; set; }
        // CONVERTCHEAT fields
        public string? Format { get; set; }
        public string? Direction { get; set; }
        // PORTCHEATS fields
        public string? SourceDumpPath { get; set; }
        public string? TargetDumpPath { get; set; }
        // MC_LIST / MC_EXPORT / MC_IMPORT / MC_DELETE / MC_FORMAT / MC_READ_FILE fields
        public string? McPath { get; set; }
        public string? SaveName { get; set; }
        public string? McFileName { get; set; }
        public string? ExportDir { get; set; }
        public string? ImportDir { get; set; }
        public bool?   McOverwrite { get; set; }
        // HDD_LIST / HDD_LIST_FILES / HDD_EXPORT / HDD_IMPORT / HDD_DELETE / HDD_CREATE fields
        public string? HddPath { get; set; }
        public string? PartitionId { get; set; }
        public string? FileName { get; set; }
        public int?    HddSizeGb { get; set; }
        // Runtime mode control fields
        public string? Pcsx2Host { get; set; }
        public int? Pcsx2Slot { get; set; }
        public string? BiosPath { get; set; }
        public string? DiscPath { get; set; }
        public long? Steps { get; set; }
        // COMPILECDS — pre-resolved pointer-manager labels (dotted names like "seal.health")
        public List<LabelEntry>? Labels { get; set; }
        // New PINE extended command fields
        /// <summary>CPU selector for GETREGISTER/PINESETREG/LISTBP: 0=EE, 1=IOP, 0xFF=both.</summary>
        public byte? CpuSel { get; set; }
        /// <summary>Register category for GETREGISTER/PINESETREG.</summary>
        public byte? RegisterCategory { get; set; }
        /// <summary>Speed limiter mode for SETLIMITER: 0=Nominal, 1=Turbo, 2=Slomo, 3=Unlimited.</summary>
        public byte? LimiterMode { get; set; }
        /// <summary>Number of frames for FRAMEADVANCE.</summary>
        public byte? FrameCount { get; set; }
        /// <summary>Symbol name for GETSYMBOLBYNAME.</summary>
        public string? SymbolName { get; set; }
        /// <summary>File path for SAVESTATETOFILE / LOADSTATEFROMFILE.</summary>
        public string? FilePath { get; set; }
        /// <summary>Save-state slot index (0–9) for PINESAVESTATE / PINELOADSTATE.</summary>
        public byte? StateSlot { get; set; }
        /// <summary>Pagination offset for LISTFUNCTIONS / LISTGLOBALS.</summary>
        public uint? Offset { get; set; }
        /// <summary>Maximum result count for LISTFUNCTIONS / LISTGLOBALS.</summary>
        public ushort? MaxCount { get; set; }
        // SCANVALUE fields
        /// <summary>Scan comparison operator: "eq", "ne", "lt", "gt", "le", "ge".</summary>
        public string? Comparison { get; set; }
        /// <summary>Scan data type: "uint8","int8","uint16","int16","uint32","int32","uint64","int64","float32","float64".</summary>
        public string? DataType { get; set; }
        /// <summary>Value to scan for (decimal or 0x-prefixed hex string).</summary>
        public string? ScanValue { get; set; }
        /// <summary>Exclusive end address for SCANVALUE range scans.</summary>
        public uint? EndAddress { get; set; }
        /// <summary>Previous result addresses for SCANVALUE filter (refine) scans.</summary>
        public List<uint>? ScanAddresses { get; set; }
        // ADDSTRUCT / DELETESTRUCT / ADDSTRUCTMEMBER fields
        /// <summary>Struct name for ADDSTRUCT / DELETESTRUCT / ADDSTRUCTMEMBER.</summary>
        public string? StructName { get; set; }
        /// <summary>Struct base address (hex string, e.g. "0x80200000") for ADDSTRUCT.</summary>
        public string? StructBaseAddress { get; set; }
        /// <summary>Member byte offset for ADDSTRUCTMEMBER.</summary>
        public int? MemberOffset { get; set; }
        /// <summary>Member name for ADDSTRUCTMEMBER.</summary>
        public string? MemberName { get; set; }

        // RECORDNAV / GETADDRESSHISTORY fields
        /// <summary>Navigation source label (e.g. "user", "mcp", "navigate") for RECORDNAV.</summary>
        public string? NavSource { get; set; }
        /// <summary>Root directory to scan for snapshot-info.json files (GETADDRESSHISTORY).</summary>
        public string? SnapshotDir { get; set; }

        // READMEMREGION fields
        /// <summary>
        /// Optional byte pattern to search for within the memory region (READMEMREGION).
        /// Each element is an unsigned byte value (0–255).
        /// </summary>
        public List<int>? SearchPattern { get; set; }

        // PINEADDWATCH / PINEREMOVEWATCH / PINELISTWATCHES fields
        /// <summary>
        /// Condition bitmask for PINEADDWATCH: 0x01 = read, 0x02 = write, 0x03 = read+write.
        /// </summary>
        public byte? WatchCond { get; set; }

        // QUERYDB fields
        /// <summary>Read-only SELECT SQL statement to execute against the CodeDesigner SQLite database.</summary>
        public string? SqlQuery { get; set; }

        // TRACECALLS fields
        /// <summary>Maximum number of EE instructions to trace for TRACECALLS (default 10 000).</summary>
        public int? MaxInstructions { get; set; }

        // RUNUNTILPC fields
        /// <summary>Target EE virtual address for RUNUNTILPC.</summary>
        public uint? TargetPc { get; set; }
        /// <summary>Timeout in milliseconds for RUNUNTILPC (default 10 000).</summary>
        public int? TimeoutMs { get; set; }

        // Label Manager fields
        /// <summary>Workspace root directory for GETLABELSETS / CREATELABELSET etc.</summary>
        public string? WorkspaceDir { get; set; }
        /// <summary>Game identifier (ISO serial) for label-set commands.</summary>
        public string? GameId { get; set; }
        /// <summary>Label set name for GETLABELSET / SETACTIVELABELSET / CREATELABELSET etc.</summary>
        public string? SetName { get; set; }
        /// <summary>Unified label entry payload for ADDLABELENTRY.</summary>
        public LabelManagerEntry? LabelEntry { get; set; }
        /// <summary>
        /// String address for DELETELABELENTRY.
        /// Supports both real addresses ("0x80010000") and synthetic child addresses
        /// ("0x80010000:+0x0010").  Takes precedence over the numeric <see cref="Address"/> field.
        /// </summary>
        public string? LabelEntryAddress { get; set; }

        // Settings (GETSETTING / SETSETTING / GETALLSETTINGS / DELETESETTING) fields
        /// <summary>Setting key for GETSETTING / SETSETTING / DELETESETTING.</summary>
        public string? SettingKey { get; set; }
        /// <summary>Setting value for SETSETTING.</summary>
        public string? SettingValue { get; set; }
        /// <summary>Optional key prefix filter for GETALLSETTINGS (e.g. "emu." or "ui.").</summary>
        public string? SettingPrefix { get; set; }

        // GETSNAPSHOTS / SNAPSHOT fields
        /// <summary>ISO / game name for GETSNAPSHOTS and SNAPSHOT (used to link the snapshot to a game in the DB).</summary>
        public string? IsoName { get; set; }
        /// <summary>Human-readable title for SNAPSHOT (stored in DB metadata).</summary>
        public string? Title { get; set; }
        /// <summary>Optional free-text description for SNAPSHOT (stored in DB metadata).</summary>
        public string? Description { get; set; }
    }

    /// <summary>
    /// Unified label / struct / function / array / string entry used by the
    /// LabelManager socket commands.
    /// </summary>
    public class LabelManagerEntry
    {
        /// <summary>
        /// Virtual address as a hex string, e.g. "0x80010000".
        /// Root entries: real PS2 address.  Child entries: synthetic path key, e.g. "0x80010000:+0x0010".
        /// </summary>
        [JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;
        /// <summary>Address of the parent label in the same set. Null for root entries.</summary>
        [JsonPropertyName("parentAddress")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ParentAddress { get; set; }
        /// <summary>Byte offset from the dereferenced parent pointer. Only meaningful when parentAddress is set.</summary>
        [JsonPropertyName("offset")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Offset { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("comment")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Comment { get; set; }
        /// <summary>Optional data-type annotation, e.g. "u32", "float", "pointer".</summary>
        [JsonPropertyName("valueType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ValueType { get; set; }
        /// <summary>When true the entry is suppressed from the disassembler view and structs panel.</summary>
        [JsonPropertyName("hidden")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Hidden { get; set; }
        /// <summary>When true the live memory value at this address is polled and shown in the label manager table.</summary>
        [JsonPropertyName("dynamic")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Dynamic { get; set; }
        /// <summary>Semantic label type.</summary>
        [JsonPropertyName("type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Type { get; set; }
        // ── Function ─────────────────────────────────────────────────────────
        [JsonPropertyName("argRegs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? ArgRegs { get; set; }
        [JsonPropertyName("returnRegs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? ReturnRegs { get; set; }
        // ── Array ─────────────────────────────────────────────────────────────
        [JsonPropertyName("elementStride")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ElementStride { get; set; }
        [JsonPropertyName("elementCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ElementCount { get; set; }
        // ── LinkedList ────────────────────────────────────────────────────────
        [JsonPropertyName("nodeNextOffset")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? NodeNextOffset { get; set; }
        [JsonPropertyName("nodeSize")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? NodeSize { get; set; }
    }
}
