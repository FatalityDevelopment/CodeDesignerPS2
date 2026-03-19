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
        // MC_LIST / MC_EXPORT / MC_IMPORT / MC_DELETE / MC_FORMAT fields
        public string? McPath { get; set; }
        public string? SaveName { get; set; }
        public string? ExportDir { get; set; }
        public string? ImportDir { get; set; }
        public bool?   McOverwrite { get; set; }
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
    }
}
