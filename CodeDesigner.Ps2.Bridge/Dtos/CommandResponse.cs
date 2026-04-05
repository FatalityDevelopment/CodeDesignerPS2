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

        // SETWATCHPOINT result
        /// <summary>Unique ID assigned to the newly created watchpoint.</summary>
        public string? WatchpointId { get; set; }

        // LISTWATCHPOINTS result
        /// <summary>List of currently active memory watchpoints.</summary>
        public List<WatchpointInfo>? Watchpoints { get; set; }

        // Watchpoint hit info — included in GETPC and GETWATCHPOINTHIT responses
        /// <summary>Physical EE address that triggered the last watchpoint, or <c>null</c>.</summary>
        public uint? WatchpointHitAddress { get; set; }
        /// <summary>EE PC of the instruction that triggered the last watchpoint, or <c>null</c>.</summary>
        public uint? WatchpointHitPC { get; set; }
        /// <summary>ID of the watchpoint that last fired, or <c>null</c>.</summary>
        public string? WatchpointHitId { get; set; }

        // TRACECALLS result
        /// <summary>
        /// Ordered list of JAL/JALR call sites recorded during a TRACECALLS run.
        /// Each entry contains the caller address (<c>From</c>) and the resolved
        /// call target (<c>To</c>) after the branch delay slot has executed.
        /// </summary>
        public List<CallTraceEntry>? TracedCalls { get; set; }

        // Label Manager results
        /// <summary>List of label set names for GETLABELSETS.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? LabelSets { get; set; }
        /// <summary>Full label set payload for GETLABELSET / SETACTIVELABELSET / CREATELABELSET etc.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public LabelSetPayload? LabelSet { get; set; }
    }

    /// <summary>Wire payload for a complete label set returned by label-manager commands.</summary>
    public class LabelSetPayload
    {
        [System.Text.Json.Serialization.JsonPropertyName("setName")]
        public string SetName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("gameId")]
        public string? GameId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("isBuiltIn")]
        public bool IsBuiltIn { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("entries")]
        public List<LabelManagerEntry> Entries { get; set; } = [];
    }
}
