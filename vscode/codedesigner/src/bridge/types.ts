/**
 * Command message format for socket communication
 */
export interface CommandMessage {
    Command: string;
    Data?: string;
    Address?: number;
    Length?: number;
    Region?: string;
    OutputPath?: string;
    InputPath?: string;
    BreakpointAddress?: number;
    Label?: string;
    Comment?: string;
    Query?: string;
    SearchMode?: string;
    RegisterName?: string;
    RegisterIndex?: number;
    RegisterValue?: string;
    IsoPath?: string;
    ElfName?: string;
    OutputDirectory?: string;
    BiosPath?: string;
    DiscPath?: string;
    Steps?: number;
    Pcsx2Host?: string;
    Pcsx2Slot?: number;
    Format?: string;
    Direction?: string;
    SourceDumpPath?: string;
    TargetDumpPath?: string;
    Labels?: LabelEntry[];
    McPath?: string;
    SaveName?: string;
    ExportDir?: string;
    ImportDir?: string;
    McOverwrite?: boolean;
    HddPath?: string;
    PartitionId?: string;
    FileName?: string;
    HddSizeGb?: number;
    FilePath?: string;
    Comparison?: string;
    DataType?: string;
    ScanValue?: string;
    EndAddress?: number;
    ScanAddresses?: number[];
    StructName?: string;
    StructBaseAddress?: string;
    MemberOffset?: number;
    MemberName?: string;
    SearchPattern?: number[];
    /** CPU selector: 0 = EE, 1 = IOP, 0xFF = both. */
    CpuSel?: number;
    /** Condition bitmask for PINEADDWATCH: 1 = read, 2 = write, 3 = read+write. */
    WatchCond?: number;
    MaxInstructions?: number;
    TargetPc?: number;
    TimeoutMs?: number;
    NavSource?: string;
    SnapshotDir?: string;
    WorkspaceDir?: string;
    GameId?: string;
    SetName?: string;
    NewSetName?: string;
    LabelEntry?: { address: string; name: string; comment?: string; parentAddress?: string; offset?: number; valueType?: string };
    /** String address for DELETELABELENTRY (supports synthetic child paths like "0x80010000:+0x0010"). */
    LabelEntryAddress?: string;
    SettingKey?: string;
    SettingValue?: string;
    SettingPrefix?: string;
    IsoName?: string;
    McpMemoryId?: number;
    McpMemoryName?: string;
    McpMemoryContent?: string;
    /** Optional agent ID — when set the memory is scoped to that agent; null/omitted means shared. */
    McpMemoryAgentId?: number;
    McpAgentId?: number;
    McpAgentName?: string;
    McpAgentDescription?: string;
    McpAgentIsLead?: boolean;
    McpAgentStatus?: string;
    McpAgentObjective?: string;
    McpAgentRules?: string;
    McpAgentGuidelines?: string;
    McpAgentRecommendedWorkflow?: string;
    McpAgentSessionBegin?: string;
    McpAgentConcurrentTasks?: string;
    McpAgentSessionEnd?: string;
    McpAgentResearch?: string;
    McpOrchestrationId?: number;
    McpOrchestrationName?: string;
    McpOrchestrationDescription?: string;
    McpOrchestrationStrategy?: string;
    McpOrchestrationRoles?: string;
    McpOrchestrationWorkflow?: string;
    McpOrchestrationRules?: string;
    McpOrchestrationNotes?: string;
    McpConfigKey?: string;
    McpConfigValue?: string;
    GameSerial?: string;
    GameCrc?: string;
    GameTitle?: string;
    /** Unified pad slot index (0–7). */
    PadSlot?: number;
    /** Button index in PadDualshock2::Inputs (0–25) for PADSETBUTTON. */
    PadButtonIndex?: number;
    /** Normalized button value (0.0–1.0) for PADSETBUTTON. */
    PadButtonValue?: number;
    /** Left stick X for PADSETANALOG / PADSETSTATE (0–255, 0x7F = centred). */
    PadLx?: number;
    /** Left stick Y for PADSETANALOG / PADSETSTATE (0–255, 0x7F = centred). */
    PadLy?: number;
    /** Right stick X for PADSETANALOG / PADSETSTATE (0–255, 0x7F = centred). */
    PadRx?: number;
    /** Right stick Y for PADSETANALOG / PADSETSTATE (0–255, 0x7F = centred). */
    PadRy?: number;
    /** PS2 button bitmask for PADSETSTATE (reserved; button state derived from pressures). */
    PadButtons?: number;
    /** 16 pressure bytes for PADSETSTATE (in PadDualshock2::Inputs order). */
    PadPressures?: number[];
    NetCaptureEnable?: boolean;
    NetCaptureDirection?: number;
    NetCaptureMaxQueue?: number;
    NetMaxPackets?: number;
    NetFrameData?: string;
    NetFilterType?: number;
    NetFilterProtocol?: number;
    NetFilterPort?: number;
    NetFilterIP?: string;
}

/**
 * Response format for socket communication
 */
export interface CommandResponse {
    Success: boolean;
    Command?: string;
    Message?: string;
    Error?: string;
    Data?: string;
    Results?: InstructionResult[];
    Regions?: SnapshotRegion[];
    Paused?: boolean;
    PC?: number;
    BreakpointHitAddress?: number;
    GPR?: number[];
    GPR_Lo32?: number[];
    GPR_Hi32?: number[];
    GPR_UpLo32?: number[];
    GPR_UpHi32?: number[];
    GPR_PC?: number;
    GPR_HI?: number;
    GPR_LO?: number;
    GPR_HI1?: number;
    GPR_LO1?: number;
    GPR_SA?: number;
    COP0_Status?: number;
    COP0_Cause?: number;
    COP0_EPC?: number;
    COP0_Count?: number;
    COP0_Regs?: number[];
    FPR?: number[];
    FPR_ACC?: number;
    FCR0?: number;
    FCR31?: number;
    VU0F_X?: number[];
    VU0F_Y?: number[];
    VU0F_Z?: number[];
    VU0F_W?: number[];
    VU0F_ACC_X?: number;
    VU0F_ACC_Y?: number;
    VU0F_ACC_Z?: number;
    VU0F_ACC_W?: number;
    VU0F_Q?: number;
    VU0F_P?: number;
    VU0F_I?: number;
    VU0I?: number[];
    VU0_Status?: number;
    VU0_MAC?: number;
    VU0_Clip?: number;
    GS_Priv?: number[];
    Labels?: LabelEntry[];
    Strings?: StringEntry[];
    Functions?: FunctionEntry[];
    Pointers?: PointerEntry[];
    Floats?: FloatEntry[];
    Integers?: IntegerEntry[];
    Matches?: SearchMatch[];
    Threads?: ThreadEntry[];
    Modules?: ModuleEntry[];
    InstructionAnalysis?: Record<string, string>;
    ElfFiles?: DiscElfEntry[];
    BootElf?: string;
    FunctionCount?: number;
    WrittenFiles?: string[];
    PortCheatResults?: Array<Record<string, unknown>>;
    McSaves?: McSaveInfo[];
    HddPartitions?: HddPartitionInfo[];
    HddFiles?: HddFileEntryInfo[];
    ScanResults?: ScanValueResult[];
    Structs?: StructEntry[];
    MatchAddresses?: number[];
    PineWatches?: PineWatchInfo[];
    TracedCalls?: CallTraceEntry[];
    McpMemories?: McpMemoryInfo[];
    McpMemory?: McpMemoryInfo;
    McpAgents?: McpAgentInfo[];
    McpAgent?: McpAgentInfo;
    McpOrchestrations?: McpOrchestrationInfo[];
    McpOrchestration?: McpOrchestrationInfo;
    GameCheats?: GameCheatInfo[];
    GameTitle?: string;
    CheatListTitle?: string;
    PadState?: PadStateInfo;
    PadControllerType?: number;
    NetStatus?: NetStatusInfo;
    NetStats?: NetStatsInfo;
    CapturedPackets?: CapturedPacketInfo[];
    ParsedPacketRows?: ParsedPacketRow[];
    DissectionText?: string;
    InjectionSuccess?: boolean;
    Sidecars?: CdsSidecarResult[];
    Diagnostics?: CdsDiagnostic[];
}

/**
 * Result for individual instruction assembly/disassembly
 */
export interface InstructionResult {
    Success: boolean;
    Assembly?: string;
    Hex?: string;
    Error?: string;
    Comment?: string;
    Label?: string;
    LineNumber?: number;
}

// ── Exported domain types ──────────────────────────────────────────────────

/**
 * One entry in the symbol / label database.
 */
export interface LabelEntry {
    address: string;
    label:   string;
    comment?: string;
}

/**
 * A null-terminated ASCII string found in the binary.
 */
export interface StringEntry {
    address: string;
    value:   string;
}

/**
 * A detected function entry point.
 */
export interface FunctionEntry {
    address: string;
    label:   string;
}

/**
 * A 4-byte aligned value that looks like a valid EE or IOP memory pointer.
 */
export interface PointerEntry {
    address:  string;
    pointsTo: string;
}

/**
 * A plausible 32-bit IEEE-754 float found at a 4-byte aligned address.
 */
export interface FloatEntry {
    address: string;
    value:   number;
    raw:     string;
}

/**
 * A non-zero 4-byte aligned 32-bit signed integer found in memory.
 */
export interface IntegerEntry {
    address: string;
    value:   number;
    hex:     string;
}

/**
 * One result entry returned by the SCANVALUE command.
 */
export interface ScanValueResult {
    address: string;
    value:   string;
}

/**
 * One member field inside a struct definition.
 */
export interface StructMemberEntry {
    offset: number;
    name:   string;
}

/**
 * A user-defined struct with a name, base address, and optional member list.
 */
export interface StructEntry {
    name:        string;
    baseAddress: string;
    members:     StructMemberEntry[];
}

// ── Label Manager (unified label/struct) ──────────────────────────────────────

/** One member of a label that acts as a struct pointer. */
export type LabelType = 'Address' | 'Struct' | 'Function' | 'Array' | 'LinkedList' | 'String' | 'Pointer' | 'Float' | 'Value' | 'Hex';

/**
 * A unified label entry covering all supported label types.
 */
export interface LabelManagerEntry {
    /** Real PS2 address for root labels; synthetic path key for child labels. */
    address:  string;
    parentAddress?: string;
    offset?: number;
    name:     string;
    comment?: string;
    valueType?: string;
    hidden?:  boolean;
    dynamic?: boolean;
    type?:    LabelType;
    argRegs?:    string[];
    returnRegs?: string[];
    elementStride?: number;
    elementCount?:  number;
    nodeNextOffset?: number;
    nodeSize?:       number;
}

/** A named collection of LabelManagerEntry records for a specific game. */
export interface LabelSetPayload {
    setName:      string;
    gameDiscId?:  string;
    isBuiltIn:    boolean;
    entries:      LabelManagerEntry[];
}

/**
 * One entry in the server-side navigation history.
 */
export interface NavHistoryEntry {
    Address:   string;
    Label?:    string;
    Source:    string;
    Timestamp: string;
}

/**
 * One historical 4-byte value at an address, read from a saved snapshot.
 */
export interface AddressHistoryEntry {
    SnapshotName: string;
    Timestamp:    string;
    Value:        string;
    Disasm?:      string;
}

/**
 * One search match returned by the SEARCH command.
 */
export interface SearchMatch {
    instructionIndex: number;
    address:          number;
    field:            string;
    start:            number;
    length:           number;
}

/**
 * Register dump returned by GETREGS
 */
export interface RegisterDump {
    gpr: number[];
    gprLo32: number[];
    gprHi32: number[];
    gprUpLo32: number[];
    gprUpHi32: number[];
    pc: number;
    hi: number;
    lo: number;
    hi1: number;
    lo1: number;
    sa: number;
    cop0Status: number;
    cop0Cause: number;
    cop0Epc: number;
    cop0Count: number;
    cop0Regs: number[];
    fpr: number[];
    fprAcc: number;
    fcr0: number;
    fcr31: number;
    vu0fX: number[];
    vu0fY: number[];
    vu0fZ: number[];
    vu0fW: number[];
    vu0AccX: number;
    vu0AccY: number;
    vu0AccZ: number;
    vu0AccW: number;
    vu0Q: number;
    vu0P: number;
    vu0I: number;
    vu0i: number[];
    vu0Status: number;
    vu0Mac: number;
    vu0Clip: number;
    gsPriv: number[];
}

/**
 * A single cheat entry returned by GETGAMECHEATS.
 */
export interface GameCheatInfo {
    name:    string;
    author?: string;
    notes?:  string;
    codes:   string[];
}

/**
 * One save-game directory entry returned by MC_LIST.
 */
export interface McSaveInfo {
    Name:       string;
    FileCount:  number;
    Created:    string;
    Modified:   string;
    TotalBytes: number;
    Files:      McFileInfo[];
}

/**
 * One file within a PS2 save-game directory.
 */
export interface McFileInfo {
    Name: string;
    Size: number;
}

/**
 * One partition entry returned by HDD_LIST.
 */
export interface HddPartitionInfo {
    Id:          string;
    Name:        string;
    StartSector: number;
    TotalBytes:  number;
    TypeName:    string;
    FileCount:   number;
    Created:     string;
    Files:       HddFileEntryInfo[];
}

/**
 * One file entry within a PS2 HDD partition.
 */
export interface HddFileEntryInfo {
    Name:        string;
    Size:        number;
    IsDirectory: boolean;
    Modified:    string;
}

/**
 * A PINE hardware memory watchpoint returned by listPineWatches().
 */
export interface PineWatchInfo {
    Cpu:   string;
    Start: string;
    End:   string;
    Cond:  string;
}

/**
 * A single JAL/JALR call-site record returned by traceCalls().
 */
export interface CallTraceEntry {
    From: string;
    To:   string;
}

/**
 * Full pad state snapshot returned by PADGETSTATE.
 */
export interface PadStateInfo {
    buttons: number;
    lx: number;
    ly: number;
    rx: number;
    ry: number;
    pressures: number[];
}

/**
 * A MCP Memory Manager entry stored in the database as Markdown.
 */
export interface McpMemoryInfo {
    id:         number;
    name:       string;
    content:    string;
    gameId?:    string;
    agentId?:   number;
    createdAt:  string;
    updatedAt:  string;
}

/**
 * A MCP Agent profile stored in the database.
 */
export interface McpAgentInfo {
    id:                  number;
    isAgentLead:         boolean;
    name:                string;
    description?:        string;
    status?:             string;
    objective?:          string;
    rules?:              string;
    guidelines?:         string;
    recommendedWorkflow?: string;
    sessionBegin?:       string;
    concurrentTasks?:    string;
    sessionEnd?:         string;
    research?:           string;
    createdAt:           string;
    updatedAt:           string;
}

/**
 * A MCP Orchestration profile stored in the database.
 */
export interface McpOrchestrationInfo {
    id:           number;
    isActive?:    boolean;
    name:         string;
    description?: string;
    strategy?:    string;
    roles?:       string;
    workflow?:    string;
    rules?:       string;
    notes?:       string;
    createdAt:    string;
    updatedAt:    string;
}

export interface McpConfigEntry {
    id:           number;
    key:          string;
    value?:       string;
    description?: string;
    updatedAt:    string;
}

export interface CdsDiagnostic {
    Severity: 'Info' | 'Warning' | 'Error' | string;
    LineNumber: number;
    ColumnNumber?: number;
    EndLineNumber?: number;
    EndColumnNumber?: number;
    Code?: string;
    Message: string;
    SourceLine?: string;
}

/**
 * One sidecar command from a compiled CDS source file.
 */
export interface CdsSidecarResult {
    Kind: string;
    Address: number;
    Operator?: string;
    ExpectedValue?: number;
    ActualValue?: number;
    AssertPassed?: boolean;
    WatchSize?: number;
    AccessType?: string;
    Directive?: string;
    MessageText?: string;
}

/**
 * One memory region written by a SNAPSHOT command.
 */
export interface SnapshotRegion {
    Label: string;
    File: string;
    BaseAddress: number;
    Size: number;
}

/**
 * One EE OS thread entry returned by GETTHREADS.
 */
export interface ThreadEntry {
    id: number;
    status: string;
    pc: number;
    sp: number;
    priority: number;
    entryPc: number;
}

/**
 * One IOP module entry returned by GETMODULES.
 */
export interface ModuleEntry {
    name:     string;
    version:  number;
    textAddr: string;
    entry:    string;
    gp:       string;
    textSize: number;
    dataSize: number;
    bssSize:  number;
}

/**
 * One ELF or IRX file found on a PS2 disc image.
 */
export interface DiscElfEntry {
    Path: string;
    Name: string;
    Size: number;
    IsBootElf: boolean;
}

/**
 * Result for a single cheat code line produced by PORTCHEATS.
 */
export interface PortCheatResult {
    blockIndex: number;
    blockName: string;
    blockText: string | null;
    originalCode: string;
    newCode: string | null;
    found: boolean;
    method: string;
    confidence: number;
    ambiguous: boolean;
    candidates: string[] | null;
}

/**
 * Result returned by the RECOMPILE socket command.
 */
export interface RecompileResult {
    functionCount: number;
    writtenFiles: string[];
    bootElf: string;
}

/**
 * Pushed unsolicited by the server when an MCP client performs an address-based operation.
 */
export interface AiActivityEvent {
    type:    'ai_activity';
    command: string;
    address: number;
    region:  string;
}

/**
 * Pushed unsolicited by the server when the MCP client calls session_start.
 */
export interface AiSessionStartEvent {
    type: 'ai_session_start';
}

/**
 * Pushed unsolicited by the server when the MCP client modifies labels.
 */
export interface AiLabelUpdateEvent {
    type:     'ai_label_update';
    command:  string;
    setName?: string;
    gameId?:  string;
}

/** Snapshot metadata entry returned by GETSNAPSHOTS. */
export interface SnapshotInfo {
    id: number;
    title: string;
    description?: string;
    snapshotPath: string;
    snapshotDateTime: string;
}

/** Network adapter status returned by NETGETSTATUS. */
export interface NetStatusInfo {
    enabled: boolean;
    adapterType: number;
    ps2Mac: string;
    ps2IP: string;
}

/** Network statistics returned by NETGETSTATS. */
export interface NetStatsInfo {
    txPackets: number;
    rxPackets: number;
    txBytes: number;
    rxBytes: number;
    captureQueueSize: number;
    droppedCount: number;
}

/** A single captured packet returned by NETRECVCAPTURE. */
export interface CapturedPacketInfo {
    direction: number;
    directionName: string;
    timestampUs: number;
    length: number;
    data: string;
}

/** A pre-parsed packet table row returned alongside NETRECVCAPTURE. */
export interface ParsedPacketRow {
    no: number;
    time: string;
    dir: string;
    server: string;
    src: string;
    dst: string;
    proto: string;
    len: number;
    info: string;
}
