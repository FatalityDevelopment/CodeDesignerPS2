using System.Collections.Generic;
using CodeDesigner.Ps2.CheatEngine;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Response format for socket communication
    /// </summary>
    public class CommandResponse
    {
        public bool Success { get; set; }
        public string? Command { get; set; }
        public string? Message { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? Error { get; set; }
        public string? Data { get; set; }
        public List<InstructionResult>? Results { get; set; }
        public List<SnapshotRegion>? Regions { get; set; }
        public bool? Paused { get; set; }
        public uint? PC { get; set; }
        public uint? BreakpointHitAddress { get; set; }

        // GETREGS fields
        public List<ulong>? GPR { get; set; }
        // Precise 32-bit halves of each 128-bit GPR (avoids JS floating-point loss for 64-bit values).
        // Lower integer lane: Lo32 = bits[31:0], Hi32 = bits[63:32] of RegsLo[i]
        // Upper SIMD lane:   UpLo32 = bits[31:0], UpHi32 = bits[63:32] of RegsHi[i]
        public List<uint>? GPR_Lo32 { get; set; }
        public List<uint>? GPR_Hi32 { get; set; }
        public List<uint>? GPR_UpLo32 { get; set; }
        public List<uint>? GPR_UpHi32 { get; set; }
        public ulong? GPR_PC { get; set; }
        public ulong? GPR_HI { get; set; }
        public ulong? GPR_LO { get; set; }
        public ulong? GPR_HI1 { get; set; }
        public ulong? GPR_LO1 { get; set; }
        public int? GPR_SA { get; set; }
        // Legacy COP0 convenience fields (kept for backward compatibility)
        public uint? COP0_Status { get; set; }
        public uint? COP0_Cause { get; set; }
        public ulong? COP0_EPC { get; set; }
        public uint? COP0_Count { get; set; }
        // Full COP0 register file (32 × 64-bit)
        public List<ulong>? COP0_Regs { get; set; }
        // COP1 FPR (32 × 32-bit, as raw IEEE-754 uint bit patterns)
        public List<uint>? FPR { get; set; }
        public uint? FPR_ACC { get; set; }
        // COP1 FCR control registers
        public uint? FCR0 { get; set; }
        public uint? FCR31 { get; set; }
        // VU0 float registers (32 × vec4, each component as raw IEEE-754 uint)
        public List<uint>? VU0F_X { get; set; }
        public List<uint>? VU0F_Y { get; set; }
        public List<uint>? VU0F_Z { get; set; }
        public List<uint>? VU0F_W { get; set; }
        // VU0 ACC accumulator (vec4)
        public uint? VU0F_ACC_X { get; set; }
        public uint? VU0F_ACC_Y { get; set; }
        public uint? VU0F_ACC_Z { get; set; }
        public uint? VU0F_ACC_W { get; set; }
        // VU0 Q/P/I special float registers
        public uint? VU0F_Q { get; set; }
        public uint? VU0F_P { get; set; }
        public uint? VU0F_I { get; set; }
        // VU0 integer registers (16 × 16-bit, stored as uint)
        public List<uint>? VU0I { get; set; }
        // VU0 flag registers
        public uint? VU0_Status { get; set; }
        public ushort? VU0_MAC { get; set; }
        public uint? VU0_Clip { get; set; }
        // GS privileged registers (PMODE, SMODE1/2, SRFSH, SYNCH1/2, SYNCV,
        //   DISPFB1/2, DISPLAY1/2, EXTBUF/DATA/WRITE, BGCOLOR, CSR, IMR, BUSDIR, SIGLBLID)
        public List<ulong>? GS_Priv { get; set; }
        // Symbol panel results
        public List<LabelEntry>?    Labels    { get; set; }
        public List<StringEntry>?   Strings   { get; set; }
        public List<FunctionEntry>? Functions { get; set; }
        public List<PointerEntry>?  Pointers  { get; set; }
        public List<FloatEntry>?    Floats    { get; set; }
        public List<IntegerEntry>?  Integers  { get; set; }
        public List<SearchMatch>?   Matches   { get; set; }
        // GETTHREADS
        public List<ThreadEntry>?   Threads   { get; set; }

        /// <summary>
        /// Per-address analysis comments produced when an instruction executes with
        /// the real register state (keyed by hex address string, e.g. "80001234").
        /// Populated by STEP to let the frontend update the analysis column
        /// without reloading the full disassembly page.
        /// </summary>
        public Dictionary<string, string>? InstructionAnalysis { get; set; }

        // LISTDISCELFS fields
        public List<DiscElfEntry>? ElfFiles { get; set; }
        public string? BootElf { get; set; }

        // RECOMPILE fields
        public int? FunctionCount { get; set; }
        public List<string>? WrittenFiles { get; set; }

        // PORTCHEATS fields
        public List<PortCheatResult>? PortCheatResults { get; set; }
        // MC_LIST field
        public List<McSaveInfo>? McSaves { get; set; }
        // HDD_LIST / HDD_LIST_FILES fields
        public List<HddPartitionInfo>? HddPartitions { get; set; }
        public List<HddFileEntryInfo>? HddFiles { get; set; }
        // ISO_LIST_FILES / ISO_GET_INFO fields
        public List<IsoFileEntryInfo>? IsoFiles { get; set; }
        public long? IsoSectorCount { get; set; }
        public bool? IsoIsRawImage { get; set; }
        // SCANVALUE results
        public List<ScanValueResult>? ScanResults { get; set; }
        // GETSTRUCTS results
        public List<StructEntry>? Structs { get; set; }
        // New PINE extended command response fields
        public float? FPS { get; set; }
        public string? SymbolAddress { get; set; }
        public string? SymbolName { get; set; }
        public uint? SymbolSize { get; set; }
        public string? RegisterValue { get; set; }
        public uint? TotalSymbols { get; set; }
        public List<PineBreakpointInfo>?  Breakpoints  { get; set; }
        public List<PineStackFrameInfo>?  StackFrames  { get; set; }
        public List<IopModuleInfo>?       Modules      { get; set; }
        public List<PineSymbolInfo>?      PineSymbols  { get; set; }
        public List<PineDisassemblyLineInfo>? DisassemblyLines { get; set; }
        public List<PineLocalInfo>?       LocalVars    { get; set; }
        /// <summary>List of PINE hardware watchpoints returned by PINELISTWATCHES.</summary>
        public List<PineWatchInfo>?       PineWatches  { get; set; }

        // GETNAVHISTORY results
        public List<NavHistoryEntry>? NavHistory { get; set; }

        // GETADDRESSHISTORY results
        public List<AddressHistoryEntry>? AddressHistory { get; set; }

        // READMEMREGION results
        /// <summary>
        /// Absolute EE virtual addresses where the requested <c>SearchPattern</c>
        /// was found within the scanned region.  <c>null</c> when no pattern was
        /// supplied or no matches were found.
        /// </summary>
        public List<uint>? MatchAddresses { get; set; }

        // TRACECALLS result
        /// <summary>
        /// Ordered list of JAL/JALR call sites recorded during a TRACECALLS run.
        /// Each entry contains the caller address (<c>From</c>) and the resolved
        /// call target (<c>To</c>) after the branch delay slot has executed.
        /// </summary>
        public List<CallTraceEntry>? TracedCalls { get; set; }

        // Memory Manager results (MCPMEMORY_LIST / MCPMEMORY_GET / MCPMEMORY_ADD / MCPMEMORY_UPDATE)
        /// <summary>List of MCP memory entries returned by MCPMEMORY_LIST.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<McpMemoryInfo>? McpMemories { get; set; }
        /// <summary>Single MCP memory entry returned by MCPMEMORY_GET / MCPMEMORY_ADD / MCPMEMORY_UPDATE / MCPMEMORY_IMPORT.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public McpMemoryInfo? McpMemory { get; set; }

        // Agent Profile results (MCPAGENT_LIST / MCPAGENT_GET / MCPAGENT_ADD / MCPAGENT_UPDATE)
        /// <summary>List of MCP agent profiles returned by MCPAGENT_LIST.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<McpAgentInfo>? McpAgents { get; set; }
        /// <summary>Single MCP agent profile returned by MCPAGENT_GET / MCPAGENT_ADD / MCPAGENT_UPDATE / MCPAGENT_IMPORT.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public McpAgentInfo? McpAgent { get; set; }

        // Orchestration Profile results (MCPORCHESTRATION_LIST / MCPORCHESTRATION_GET / MCPORCHESTRATION_ADD / MCPORCHESTRATION_UPDATE)
        /// <summary>List of MCP orchestration profiles returned by MCPORCHESTRATION_LIST.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<McpOrchestrationInfo>? McpOrchestrations { get; set; }
        /// <summary>Single MCP orchestration profile returned by MCPORCHESTRATION_GET / MCPORCHESTRATION_ADD / MCPORCHESTRATION_UPDATE / MCPORCHESTRATION_IMPORT.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public McpOrchestrationInfo? McpOrchestration { get; set; }

        // Label Manager results
        /// <summary>List of label set names for GETLABELSETS.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? LabelSets { get; set; }
        /// <summary>Full label set payload for GETLABELSET / SETACTIVELABELSET / CREATELABELSET etc.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public LabelSetPayload? LabelSet { get; set; }

        // MCP Config results (MCPCONFIG_LIST / MCPCONFIG_UPDATE)
        /// <summary>List of MCP config entries returned by MCPCONFIG_LIST.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<McpConfigInfo>? McpConfigs { get; set; }
        /// <summary>Single MCP config entry returned by MCPCONFIG_UPDATE.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public McpConfigInfo? McpConfig { get; set; }

        // GETSNAPSHOTS results
        /// <summary>List of snapshot metadata entries returned by GETSNAPSHOTS.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<SnapshotInfo>? Snapshots { get; set; }

        // GETGAMECHEATS results
        /// <summary>List of cheat entries returned by GETGAMECHEATS.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<CheatInfo>? GameCheats { get; set; }
        /// <summary>Game title returned alongside cheats by GETGAMECHEATS.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? GameTitle { get; set; }
        /// <summary>Cheat list title returned alongside cheats by GETGAMECHEATS.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? CheatListTitle { get; set; }

        // QUERYDB results
        /// <summary>
        /// Rows returned by a QUERYDB SELECT statement.
        /// Each row is a dictionary of column name → value (string, long, double, or null).
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<Dictionary<string, object?>>? DbRows { get; set; }

        /// <summary>Number of rows returned by the last QUERYDB statement.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public int? DbRowCount { get; set; }

        // Pad / controller input results (PADGETSTATE / PADGETTYPE)
        /// <summary>Full pad state returned by PADGETSTATE.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public PadStateInfo? PadState { get; set; }
        /// <summary>Controller type value returned by PADGETTYPE (0=NotConnected, 1=DualShock2, etc.).</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public byte? PadControllerType { get; set; }

        // DEV9 network bridge results (NETGETSTATUS / NETGETSTATS / NETRECVCAPTURE / NETDISASSEMBLE)
        /// <summary>Network adapter status returned by NETGETSTATUS.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public NetStatusInfo? NetStatus { get; set; }
        /// <summary>Network statistics returned by NETGETSTATS.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public NetStatsInfo? NetStats { get; set; }
        /// <summary>Captured packets returned by NETRECVCAPTURE.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<CapturedPacketInfo>? CapturedPackets { get; set; }
        /// <summary>Packet dissection text returned by NETDISASSEMBLE.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? DissectionText { get; set; }
        /// <summary>Injection success flag returned by NETINJECT / NETINJECTTX.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public bool? InjectionSuccess { get; set; }
    }

    /// <summary>Snapshot metadata wire payload returned by GETSNAPSHOTS.</summary>
    public class SnapshotInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("description")]
        public string? Description { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("snapshotPath")]
        public string SnapshotPath { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("snapshotDateTime")]
        public string SnapshotDateTime { get; set; } = string.Empty;
    }

    /// <summary>Wire payload for a complete label set returned by label-manager commands.</summary>
    public class LabelSetPayload
    {
        [System.Text.Json.Serialization.JsonPropertyName("setName")]
        public string SetName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("gameDiscId")]
        public string? GameDiscId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("isBuiltIn")]
        public bool IsBuiltIn { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("entries")]
        public List<LabelManagerEntry> Entries { get; set; } = [];
    }

    /// <summary>Wire payload for a single MCP config entry returned by MCPCONFIG_LIST / MCPCONFIG_UPDATE.</summary>
    public class McpConfigInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public string? Value { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("description")]
        public string? Description { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("updatedAt")]
        public string UpdatedAt { get; set; } = string.Empty;
    }

    /// <summary>Wire payload for a single cheat entry returned by GETGAMECHEATS.</summary>
    public class CheatInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("author")]
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? Author { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("notes")]
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? Notes { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("codes")]
        public List<string> Codes { get; set; } = [];
    }

    /// <summary>Wire payload for a pad state snapshot returned by PADGETSTATE.</summary>
    public class PadStateInfo
    {
        /// <summary>PS2 button bitmask; a bit that is <b>clear</b> means pressed.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("buttons")]
        public uint Buttons { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("lx")]
        public byte Lx { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ly")]
        public byte Ly { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("rx")]
        public byte Rx { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ry")]
        public byte Ry { get; set; }
        /// <summary>16 pressure values (0–255) in PadDualshock2::Inputs order.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("pressures")]
        public byte[] Pressures { get; set; } = [];
    }

    /// <summary>Wire payload for network adapter status returned by NETGETSTATUS.</summary>
    public class NetStatusInfo
    {
        /// <summary>True when EthEnable is set and the adapter has been opened.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("enabled")]
        public bool Enabled { get; set; }
        /// <summary>Adapter type (0=Unset, 1=PCAP_Bridged, 2=PCAP_Switched, 3=TAP, 4=Sockets).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("adapterType")]
        public byte AdapterType { get; set; }
        /// <summary>PS2 MAC address as colon-separated hex string.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("ps2Mac")]
        public string PS2Mac { get; set; } = string.Empty;
        /// <summary>Last known PS2 IPv4 address as dotted decimal string.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("ps2IP")]
        public string PS2IP { get; set; } = string.Empty;
    }

    /// <summary>Wire payload for network statistics returned by NETGETSTATS.</summary>
    public class NetStatsInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("txPackets")]
        public uint TxPackets { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("rxPackets")]
        public uint RxPackets { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("txBytes")]
        public ulong TxBytes { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("rxBytes")]
        public ulong RxBytes { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("captureQueueSize")]
        public ushort CaptureQueueSize { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("droppedCount")]
        public uint DroppedCount { get; set; }
    }

    /// <summary>Wire payload for a single captured packet returned by NETRECVCAPTURE.</summary>
    public class CapturedPacketInfo
    {
        /// <summary>0 = TX (PS2→Host), 1 = RX (Host→PS2).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("direction")]
        public byte Direction { get; set; }
        /// <summary>Direction as human-readable string.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("directionName")]
        public string DirectionName { get; set; } = string.Empty;
        /// <summary>Monotonic timestamp in microseconds.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("timestampUs")]
        public ulong TimestampUs { get; set; }
        /// <summary>Length of the raw Ethernet frame in bytes.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("length")]
        public int Length { get; set; }
        /// <summary>Raw Ethernet frame bytes as base64-encoded string.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("data")]
        public string Data { get; set; } = string.Empty;
    }
}
