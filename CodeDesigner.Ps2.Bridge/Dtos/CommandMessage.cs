using System.Collections.Generic;

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

        // SETWATCHPOINT / CLEARWATCHPOINT fields
        /// <summary>Watchpoint identifier for CLEARWATCHPOINT.</summary>
        public string? WatchpointId { get; set; }
        /// <summary>Watchpoint access type for SETWATCHPOINT: "write", "read", or "readwrite".</summary>
        public string? WatchpointType { get; set; }

        // TRACECALLS fields
        /// <summary>Maximum number of EE instructions to trace for TRACECALLS (default 10 000).</summary>
        public int? MaxInstructions { get; set; }

        // RUNUNTILPC fields
        /// <summary>Target EE virtual address for RUNUNTILPC.</summary>
        public uint? TargetPc { get; set; }
        /// <summary>Timeout in milliseconds for RUNUNTILPC (default 10 000).</summary>
        public int? TimeoutMs { get; set; }
    }
}
