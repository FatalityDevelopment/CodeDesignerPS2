import * as fs from 'fs';
import * as path from 'path';
import { encodePng } from './utils/png';
import type { DirectEmulator, DirectHintLevel, DirectPadButton, DirectRef, DirectRefKind, DirectSpace } from './ps2debug/directEmulator';

/** The pad button mask (active-low), as PS2 pads report it: Select at bit 0 up to Square at bit 15. */
/** What a snapshot holds: each physical memory, as the files Code Designer has always written. */
const SNAPSHOT_REGIONS: { label: string; file: string; space: DirectSpace; base: number; size: number }[] = [
    { label: 'EE RAM',     file: 'EEmem.bin',      space: 'EE_RAM',     base: 0x00000000, size: 32 << 20 },
    { label: 'BIOS ROM',   file: 'BIOSrom.bin',    space: 'BIOS',       base: 0x1FC00000, size: 4 << 20 },
    { label: 'Scratchpad', file: 'Scratchpad.bin', space: 'SCRATCHPAD', base: 0x70000000, size: 16 << 10 },
    { label: 'IOP RAM',    file: 'IOPmem.bin',     space: 'IOP_RAM',    base: 0x1C000000, size: 2 << 20 },
];

/** Addresses (from `base`) where `pattern` occurs in `data`; -1 in the pattern matches any byte. */
export function findPattern(data: Buffer, pattern: number[], base: number): number[] {
    const matches: number[] = [];
    if (pattern.length === 0) { return matches; }
    for (let at = 0; at + pattern.length <= data.length; at++) {
        let k = 0;
        while (k < pattern.length && (pattern[k] < 0 || data[at + k] === pattern[k])) { k++; }
        if (k === pattern.length) { matches.push((base + at) >>> 0); }
    }
    return matches;
}

const PINE_BUTTONS: DirectPadButton[] = [
    'select', 'l3', 'r3', 'start', 'up', 'right', 'down', 'left',
    'l2', 'r2', 'l1', 'r1', 'triangle', 'circle', 'cross', 'square',
];

/** The patch the persistent cheat engine becomes with a direct emulator. */
const DIRECT_CHEAT_ENGINE = 'Code Designer cheat engine';

/** The setting the disassembler keeps its comment-column hint level in. */
const HINT_LEVEL_SETTING = 'disasm.hintLevel';

function isHintLevel(value: string): value is DirectHintLevel {
    return value === 'full' || value === 'addresses' || value === 'off';
}

/** The source labels pushed into a directly connected emulator are kept under. */
const DIRECT_LABEL_SOURCE = 'codedesigner';

/** Which index reference kinds each of the Bridge's xref kind groups covers. */
const DIRECT_KIND_GROUPS: Record<string, DirectRefKind[]> = {
    controlFlow: ['call', 'jump', 'branch', 'registerJump', 'case', 'observed'],
    resolved: ['address', 'load', 'store'],
    pointer: ['pointer'],
};

/** The Bridge's names for a reference kind: group, label, summary verb. */
const DIRECT_KIND_TEXT: Record<DirectRefKind, [string, string, string]> = {
    call: ['controlFlow', 'Call', 'Calls'],
    jump: ['controlFlow', 'Jump', 'Jumps to'],
    branch: ['controlFlow', 'Branch', 'Branches to'],
    registerJump: ['controlFlow', 'Register Jump', 'Jumps (register) to'],
    address: ['resolved', 'Address', 'Builds'],
    load: ['resolved', 'Load', 'Loads from'],
    store: ['resolved', 'Store', 'Stores to'],
    case: ['controlFlow', 'Switch case', 'Switch table entry for'],
    pointer: ['pointer', 'Pointer', 'Pointer to'],
    observed: ['controlFlow', 'Observed call', 'Was seen calling'],
};

/**
 * The index kinds the Bridge's kind filter asks for: `[]` for all, `null`
 * when it asks only for kinds the index has no counterpart for (string
 * patterns).
 */
function directKinds(kinds?: string[]): DirectRefKind[] | null {
    if (!kinds || kinds.length === 0) { return []; }
    const wanted = new Set<DirectRefKind>();
    for (const raw of kinds) {
        const k = raw.trim().toLowerCase();
        const group = k.startsWith('control') || k.startsWith('branch') ? 'controlFlow'
            : k.startsWith('resolved') || k.startsWith('address') ? 'resolved'
                : k.startsWith('pointer') ? 'pointer' : undefined;
        if (group) { DIRECT_KIND_GROUPS[group].forEach(x => wanted.add(x)); }
    }
    return wanted.size > 0 ? [...wanted] : null;
}

function hex8(value: number): string {
    return (value >>> 0).toString(16).toUpperCase().padStart(8, '0');
}

/** A direct emulator's reference, as the Bridge reports one. */
function referenceFromDirect(r: DirectRef, target: number, requested: number): ReferenceEntry {
    const [kind, kindLabel, summary] = DIRECT_KIND_TEXT[r.kind];
    const dataWord = r.kind === 'pointer' || r.kind === 'case';
    return {
        address: hex8(r.from),
        addressValue: r.from,
        kind,
        kindLabel,
        targetAddress: hex8(requested),
        instruction: dataWord ? '' : r.text,
        summary: `${summary} 0x${hex8(target)}`,
        detail: r.functionName ? `in ${r.functionName}` : undefined,
        matchedValue: dataWord ? `0x${hex8(r.word)}` : `0x${hex8(target)}`,
        matchLength: dataWord ? 4 : 0,
        sourceAddresses: [hex8(r.from)],
    };
}

/**
 * How a BridgeClient reaches the Bridge. The extension uses TCP (socketClient.ts); a browser page uses a WebSocket.
 * Both carry the same newline-delimited JSON, so everything above the transport is shared.
 */
export interface BridgeTransportEvents {
    onData(chunk: string): void;
    onClose(): void;
}

export interface BridgeTransport {
    open(events: BridgeTransportEvents): Promise<void>;
    write(text: string): Promise<void>;
    close(): void;
}

/**
 * Command message format for socket communication
 */
interface CommandMessage {
    Command: string;
    Data?: string;
    Address?: number;
    Length?: number;
    Region?: string;
    OutputPath?: string;
    InputPath?: string;
    Label?: string;
    Comment?: string;
    Query?: string;
    SearchMode?: string;
    TargetAddress?: number;
    ReferenceKinds?: string[];
    // LISTDISCELFS / RECOMPILE fields
    IsoPath?: string;
    ElfName?: string;
    OutputDirectory?: string;
    // CONVERTCHEAT fields
    Format?: string;
    Direction?: string;
    // PORTCHEATS fields
    SourceDumpPath?: string;
    TargetDumpPath?: string;
    // COMPILECDS — pre-resolved pointer-manager labels (e.g. { address:'0x80100000', label:'seal.health' })
    Labels?: LabelEntry[];
    // MC_* fields
    McPath?: string;
    SaveName?: string;
    McFileName?: string;
    ExportDir?: string;
    ImportDir?: string;
    McOverwrite?: boolean;
    // FileFormats/Zipper fields
    ZipperEntryIndex?: number;
    ZipperSection?: string;
    ZipperPreloadToken?: string;
    SaveMode?: string;
    SaveTitle?: string;
    SaveTitleLength?: number;
    SigningKeyPath?: string;
    // HDD_* fields
    HddPath?: string;
    PartitionId?: string;
    FileName?: string;
    HddFileSource?: string;
    HddSizeGb?: number;
    // Save-state file path (SAVESTATETOFILE / LOADSTATEFROMFILE)
    FilePath?: string;
    // SCANVALUE fields
    Comparison?: string;
    DataType?: string;
    ScanValue?: string;
    EndAddress?: number;
    ScanAddresses?: number[];
    Addresses?: number[];
    // ADDSTRUCT / DELETESTRUCT / ADDSTRUCTMEMBER fields
    StructName?: string;
    StructBaseAddress?: string;
    MemberOffset?: number;
    MemberName?: string;
    // CDS cheat engine (BUILDCHEATENGINE)
    HookWords?: number[];
    HookAddress?: number;
    EngineBase?: number;
    // Navigation-history source for address-bearing commands.
    NavSource?: string;
    // GETADDRESSHISTORY fields
    SnapshotDir?: string;
    // Label Manager fields
    WorkspaceDir?: string;
    GameId?: string;
    SetName?: string;
    NewSetName?: string;
    LabelEntry?: { address: string; name: string; comment?: string; parentAddress?: string; offset?: number; valueType?: string };
    /** String address for DELETELABELENTRY (supports synthetic child paths like "0x80010000:+0x0010"). */
    LabelEntryAddress?: string;
    // Settings (GETSETTING / SETSETTING / GETALLSETTINGS / DELETESETTING)
    SettingKey?: string;
    SettingValue?: string;
    SettingPrefix?: string;
    // GETSNAPSHOTS
    IsoName?: string;
    // Memory Manager (MCPMEMORY_LIST / MCPMEMORY_GET / MCPMEMORY_ADD / MCPMEMORY_UPDATE / MCPMEMORY_DELETE / MCPMEMORY_IMPORT / MCPMEMORY_EXPORT)
    McpMemoryId?: number;
    McpMemoryName?: string;
    McpMemoryContent?: string;
    /** Optional agent ID — when set the memory is scoped to that agent; null/omitted means shared. */
    McpMemoryAgentId?: number;
    // Agent Profile (MCPAGENT_LIST / MCPAGENT_GET / MCPAGENT_ADD / MCPAGENT_UPDATE / MCPAGENT_DELETE / MCPAGENT_IMPORT / MCPAGENT_EXPORT)
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
    // Orchestration Profile (MCPORCHESTRATION_LIST / MCPORCHESTRATION_GET / MCPORCHESTRATION_ADD / MCPORCHESTRATION_UPDATE / MCPORCHESTRATION_DELETE / MCPORCHESTRATION_IMPORT / MCPORCHESTRATION_EXPORT)
    McpOrchestrationId?: number;
    McpOrchestrationName?: string;
    McpOrchestrationDescription?: string;
    McpOrchestrationStrategy?: string;
    McpOrchestrationRoles?: string;
    McpOrchestrationWorkflow?: string;
    McpOrchestrationRules?: string;
    McpOrchestrationNotes?: string;
    // MCP Config (MCPCONFIG_LIST / MCPCONFIG_UPDATE)
    McpConfigKey?: string;
    McpConfigValue?: string;
    // MCP Chat tab (MCPCHAT_*) — see docs/ai-chat-templates.md
    McpChatSessionId?: number;
    McpChatTitle?: string;
    McpChatOrchestrationId?: number;
    McpChatGameSerial?: string;
    McpChatGameTag?: string;
    McpChatMessageId?: number;
    McpChatKind?: 'user' | 'agent' | 'system';
    McpChatAgentId?: number;
    McpChatAgentDisplay?: string;
    McpChatRole?: string;
    McpChatBlocksJson?: string;
    McpChatTokens?: number;
    McpChatLatencyMs?: number;
    McpChatStatus?: 'streaming' | 'done' | 'error';
    McpChatUserText?: string;
    // Game Disc (UPDATEGAMEDISC)
    GameSerial?: string;
    GameCrc?: string;
    GameTitle?: string;
    // Cheats editor (CHEATEDITOR_*)
    CheatGameId?: number;
    CheatListId?: number;
    // Network frames (NETBUILDFRAME / NETDISASSEMBLE / NETDISSECTMANY / NETBUILDMESSAGE)
    NetFrameData?: string;
    /** NETBUILDFRAME: new TCP/UDP payload (base64) for the NetFrameData template. */
    NetPayloadData?: string;
    /** NETBUILDFRAME: recompute IPv4/TCP/UDP lengths and checksums. */
    NetFixFrame?: boolean;
    /** NETDISSECTMANY: frames to dissect in one call. */
    NetFrames?: Array<{ data: string; direction: number; timestampUs: number }>;
    /** NETBUILDMESSAGE: catalog protocol, message id, field values, wrapping RT type and APP target. */
    NetMessageProtocol?: string;
    NetMessageId?: number;
    NetMessageValues?: Record<string, string>;
    NetRtType?: number;
    NetAppTarget?: number;
    // Data-format overrides (GETDATAFORMATS / SETDATAFORMAT / CLEARDATAFORMAT)
    /** One of "byte" | "half" | "word" | "dword" | "qword" | "float" | "double" for SETDATAFORMAT. */
    DataFormatKind?: string;
    /** Batch override payload for SETDATAFORMATS. */
    DataFormatOverrides?: Array<Omit<DataFormatOverrideInfo, 'id'>>;
    /** DISASSEMBLE-only: ignore persisted data-format overrides for stable 4-byte page probes. */
    SkipDataFormatOverrides?: boolean;
    // Asset sessions (ASSET_*), shared with the assets_* MCP tools.
    AssetSession?: string;
    AssetArchive?: string;
    AssetName?: string;
    AssetJson?: string;
}

/** Fields an asset editor may send with an ASSET_* command. */
export interface AssetCommand {
    Command: string;
    InputPath?: string;
    AssetSession?: string;
    AssetArchive?: string;
    AssetName?: string;
    AssetJson?: string;
}

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
 * A structured reference to an address found by SCANREFERENCES.
 */
export interface ReferenceEntry {
    address: string;
    addressValue: number;
    kind: string;
    kindLabel: string;
    targetAddress: string;
    instruction: string;
    summary: string;
    detail?: string;
    matchedValue?: string;
    matchLength?: number;
    sourceAddresses?: string[];
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
 * Labels can be recursive: a child label has parentAddress + offset set instead
 * of a fixed PS2 address.  The tree is reconstructed client-side from the flat
 * list in LabelSetPayload.entries.
 */
export interface LabelManagerEntry {
    /** Real PS2 address for root labels; synthetic path key for child labels. */
    address:  string;
    /** Address of the parent label within the same set. Undefined for root labels. */
    parentAddress?: string;
    /** Byte offset from the dereferenced parent pointer. Only meaningful when parentAddress is set. */
    offset?: number;
    name:     string;
    comment?: string;
    /** Optional data-type annotation, e.g. "u32", "float", "pointer". */
    valueType?: string;
    /** When true, the entry is hidden from the disassembler view and structs panel. */
    hidden?:  boolean;
    /** When true, the live memory value at this address is polled and shown in the table. */
    dynamic?: boolean;
    /** Semantic label type. */
    type?:    LabelType;
    // Function
    argRegs?:    string[];
    returnRegs?: string[];
    // Array
    elementStride?: number;
    elementCount?:  number;
    // LinkedList
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

/** A member label within a {@link LabelGroupInfo}. */
export interface LabelGroupMemberInfo {
    id:           number;
    labelId:      number;
    labelAddress: string;
    labelName:    string;
    labelType:    string;
    role?:        string;
    sortOrder:    number;
}

/** A cross-cutting label group ("objectmap") — a class, module, subsystem or vtable. */
export interface LabelGroupInfo {
    id:       number;
    name:     string;
    kind:     string;
    comment?: string;
    color?:   string;
    members:  LabelGroupMemberInfo[];
}

/** One caller→callee edge in a call-graph subgraph. */
export interface CallGraphEdge {
    from:       string;
    to:         string;
    kind:       string;
    argRegs?:    string[];
    returnRegs?: string[];
}

/** Resolved metadata for one call-graph node. */
export interface CallGraphNodeInfo {
    address:    string;
    label?:     string;
    argRegs?:    string[];
    returnRegs?: string[];
}

/** A call-graph subgraph around a focus address. */
export interface CallGraphData {
    focus:     string;
    depth:     number;
    direction: string;
    nodes:     string[];
    nodeInfo?: CallGraphNodeInfo[];
    edges:     CallGraphEdge[];
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
    // GPR (lower 32 bits only — use gprLo32/gprHi32 for full 64-bit precision)
    gpr: number[];
    // 128-bit GPR precise representation as four uint32 arrays
    gprLo32: number[];    // bits[31:0]  of RegsLo[i]
    gprHi32: number[];    // bits[63:32] of RegsLo[i]
    gprUpLo32: number[];  // bits[31:0]  of RegsHi[i] (upper 64-bit lane)
    gprUpHi32: number[];  // bits[63:32] of RegsHi[i]
    pc: number;
    hi: number;
    lo: number;
    hi1: number;
    lo1: number;
    sa: number;
    // COP0 legacy convenience fields
    cop0Status: number;
    cop0Cause: number;
    cop0Epc: number;
    cop0Count: number;
    // COP0 full register file (32 entries)
    cop0Regs: number[];
    // FPR – 32 × raw IEEE-754 uint bit patterns; use rawBitsToFloat() to display
    fpr: number[];
    fprAcc: number;
    // FCR
    fcr0: number;
    fcr31: number;
    // VU0 float registers – 32 × 4 components, each raw IEEE-754 uint
    vu0fX: number[];
    vu0fY: number[];
    vu0fZ: number[];
    vu0fW: number[];
    // VU0 ACC / special
    vu0AccX: number;
    vu0AccY: number;
    vu0AccZ: number;
    vu0AccW: number;
    vu0Q: number;
    vu0P: number;
    vu0I: number;
    // VU0 integer registers (16 entries)
    vu0i: number[];
    // VU0 flags
    vu0Status: number;
    vu0Mac: number;
    vu0Clip: number;
    // GS privileged registers (19 entries – see order in HandleGetRegisters)
    gsPriv: number[];
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
    // GETREGS fields
    GPR?: number[];
    // Precise 32-bit halves of each 128-bit GPR (no floating-point precision loss)
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
    // Symbol panel results
    Labels?:    LabelEntry[];
    Strings?:   StringEntry[];
    Functions?: FunctionEntry[];
    Pointers?:  PointerEntry[];
    References?: ReferenceEntry[];
    Floats?:    FloatEntry[];
    Integers?:  IntegerEntry[];
    Matches?:   SearchMatch[];
    // GETTHREADS
    Threads?: ThreadEntry[];
    // GETMODULES
    Modules?: ModuleEntry[];
    // Per-address analysis comments produced when an instruction executes.
    // Keyed by hex address string (e.g. "80001234"), value is the analysis comment.
    InstructionAnalysis?: Record<string, string>;
    // LISTDISCELFS fields
    ElfFiles?: DiscElfEntry[];
    BootElf?: string;
    // RECOMPILE fields
    FunctionCount?: number;
    WrittenFiles?: string[];
    // PORTCHEATS fields
    PortCheatResults?: Array<Record<string, unknown>>;
    // MC_LIST fields
    McSaves?: McSaveInfo[];
    // HDD_LIST / HDD_LIST_FILES fields
    HddPartitions?: HddPartitionInfo[];
    HddFiles?: HddFileEntryInfo[];
    // SCANVALUE results
    ScanResults?: ScanValueResult[];
    // GETSTRUCTS results
    Structs?: StructEntry[];
    // READMEMREGION results
    MatchAddresses?: number[];
    ReadMemBatch?: Record<string, string>;
    // MCPMEMORY_LIST / MCPMEMORY_ADD / MCPMEMORY_GET / MCPMEMORY_UPDATE results
    McpMemories?: McpMemoryInfo[];
    McpMemory?: McpMemoryInfo;
    // MCPAGENT_LIST / MCPAGENT_ADD / MCPAGENT_GET / MCPAGENT_UPDATE results
    McpAgents?: McpAgentInfo[];
    McpAgent?: McpAgentInfo;
    // MCPORCHESTRATION_LIST / MCPORCHESTRATION_ADD / MCPORCHESTRATION_GET / MCPORCHESTRATION_UPDATE results
    McpOrchestrations?: McpOrchestrationInfo[];
    McpOrchestration?: McpOrchestrationInfo;
    // MCPCHAT_* results — chat sessions and rendered messages for the AI control panel's Chat tab.
    McpChatSessions?: McpChatSessionInfo[];
    McpChatSession?: McpChatSessionInfo;
    McpChatMessages?: McpChatMessageInfo[];
    McpChatMessage?: McpChatMessageInfo;
    // GETGAMECHEATS results
    GameCheats?: GameCheatInfo[];
    GameTitle?: string;
    SelectedCheatListId?: number;
    ArchiveGameId?: number;
    GameCheatLists?: GameCheatListInfo[];
    // CDS cheat engine (INJECT / UPDATE / REMOVE / STATUS / BUILD CHEATENGINE)
    CheatEngineState?: CheatEngineInfo;
    // COMPAT_LIST results
    CompatGames?: CompatGameInfo[];
    // COMPAT_DETAILS result
    CompatGame?: CompatGameInfo;
    // CHEATEDITOR_* results
    CheatEditorGames?: CheatGameInfo[];
    CheatEditorLists?: CheatListInfo[];
    CheatEditorList?: CheatListInfo;
    CheatListTitle?: string;
    // PADGETSTATE result
    PadState?: PadStateInfo;
    // NETDISSECTMANY / NETDISASSEMBLE results
    CapturedPackets?: CapturedPacketInfo[];
    ParsedPacketRows?: ParsedPacketRow[];
    DissectionText?: string;
    // NETBUILDFRAME / NETBUILDMESSAGE / NETCATALOG results
    NetFrame?: string;
    NetMessageData?: string;
    NetCatalog?: NetCatalogEntry[];
    // COMPILECDS sidecar results (assert / breakpoint / watchpoint directives)
    Sidecars?: CdsSidecarResult[];
    // Structured CDS diagnostics from VALIDATECDS / COMPILECDS.
    Diagnostics?: CdsDiagnostic[];
    // GETDATAFORMATS / SETDATAFORMAT results
    DataFormatOverrides?: DataFormatOverrideInfo[];
}

/**
 * A single cheat entry returned by GETGAMECHEATS.
 */
export interface GameCheatInfo {
    name:     string;
    author?:  string;
    notes?:   string;
    codes:    string[];
    /** Section (folder) within its cheat list. */
    section?: string;
}

/** One of a game's cheat lists in the cheat archive (GETGAMECHEATS). */
export interface GameCheatListInfo {
    id:         number;
    title:      string;
    cheatCount: number;
    /** The list belongs to this exact disc (serial) rather than the game as a whole. */
    forDisc:    boolean;
}

/**
 * State of the bridge's CDS cheat engine (CheatEngineInfo in CommandResponse.cs).
 */
export interface CheatEngineInfo {
    injected:     boolean;
    engineBase:   number;
    hookAddress?: number;
    /** Table rows written or built. */
    entries:      number;
    /** Bytes occupied from engineBase (code + data + table). */
    imageSize:    number;
    /** BUILDCHEATENGINE: the `j engine` word to place at the hook address. */
    hookWord?:    number;
    /** BUILDCHEATENGINE: whether the hooked instructions were read live and embedded. */
    hookEmbedded?: boolean;
    warnings:     string[];
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

export interface ZipperEntryInfo {
    index: number;
    name: string;
    path: string;
    size: number;
    kind: string;
    extension?: string;
    type?: number;
}

export interface ZipperSaveRecordInfo {
    fieldName: string;
    type: number;
    flags: number;
    count: number;
    valOffset: number;
    tableOffset: number;
    strOffset: number;
}

export interface ZipperInspectResult {
    format: string;
    role: 'archive' | 'structured-container' | 'game-save' | 'apache-patch-archive' | string;
    path: string;
    fileName: string;
    size: number;
    summary: string;
    canExtract: boolean;
    entries: ZipperEntryInfo[];
    apache?: {
        hasFtScore: boolean;
        hasZSealEtc: boolean;
    } | null;
    zed?: {
        rootValue: number;
        stringBasePointer: number;
        headerWord5C: number;
        dataAlignment: number;
    } | null;
    save?: {
        title: string;
        decryptedSize: number;
        header: {
            saveType: number;
            slotIndex: number;
            dataSize: number;
            moduleCount: number;
            field5C: number;
            field60: number;
        };
        modules: string[];
        records: ZipperSaveRecordInfo[];
    } | null;
}

export interface ZipperOperationResult {
    outputDirectory?: string;
    outputPath?: string;
    writtenFiles: string[];
    count: number;
    section?: string;
    buildVersion?: string;
    publicModulusHex?: string;
    generatedNewKey?: boolean;
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
    Source?:     'pfs' | 'header' | string;
}

/**
 * A watchpoint returned by listWatches().
 */
export interface WatchInfo {
    /** CPU the watchpoint applies to: "EE" or "IOP". */
    Cpu:   string;
    /** Inclusive start address as a hex string (e.g. "0x00200000"). */
    Start: string;
    /** Exclusive end address as a hex string (e.g. "0x00200004"). */
    End:   string;
    /** Access condition: "read", "write", or "read+write". */
    Cond:  string;
}

/**
 * A single JAL/JALR call-site record returned by traceCalls().
 * From = address of the call instruction (KSEG0 hex string).
 * To   = resolved call target after the delay slot (KSEG0 hex string).
 */
export interface CallTraceEntry {
    From: string;
    To:   string;
}

/**
 * Full pad state snapshot returned by PADGETSTATE.
 */
export interface PadStateInfo {
    /** PS2 button bitmask; a bit that is clear means the button is pressed. */
    buttons: number;
    /** Left stick X axis (0–255, 0x7F = centred). */
    lx: number;
    /** Left stick Y axis (0–255, 0x7F = centred). */
    ly: number;
    /** Right stick X axis (0–255, 0x7F = centred). */
    rx: number;
    /** Right stick Y axis (0–255, 0x7F = centred). */
    ry: number;
    /** 16 pressure values (0–255) in PadDualshock2::Inputs order. */
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
    /** Agent database ID when the memory is agent-specific; undefined/null means shared. */
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
 * A single PCSX2 compatibility entry (one game disc) returned by COMPAT_LIST.
 */
export interface CompatGameInfo {
    discId:             number;
    gameId:             number;
    title:              string;
    serial?:            string;
    crc?:               string;
    region?:            string;
    status?:            string;
    lastTestedVersion?: string;
    lastTestedDate?:    string;
    wikiLink?:          string;
    forumLink?:         string;
    hasCover:           boolean;
}

/**
 * A game that owns at least one cheat list — used by the cheats editor picker.
 */
export interface CheatGameInfo {
    gameId:       number;
    title:        string;
    coverSerial?: string;
    hasCover:     boolean;
    listCount:    number;
}

/**
 * A single cheat list ("build") shown in the cheats editor.
 */
export interface CheatListInfo {
    id:         number;
    gameId:     number;
    title:      string;
    cheatCount: number;
    region?:    string;
    serial?:    string;
    crc?:       string;
    updatedAt?: string;
}

/**
 * One persistent chat thread shown in the AI control panel's Chat tab sidebar.
 */
export interface McpChatSessionInfo {
    id:                 number;
    title:              string;
    orchestrationId?:   number;
    orchestrationName?: string;
    gameSerial?:        string;
    gameTag?:           string;
    messageCount:       number;
    createdAt:          string;
    updatedAt:          string;
}

/**
 * One rendered message in a chat session. <c>blocksJson</c> is an opaque JSON
 * array following the contract documented in <c>docs/ai-chat-templates.md</c>;
 * the host parses it once on receipt and hands the typed blocks to the
 * Angular renderer.
 */
export interface McpChatMessageInfo {
    id:            number;
    sessionId:     number;
    kind:          'user' | 'agent' | 'system';
    agentId?:      number;
    agentDisplay?: string;
    role?:         string;
    blocksJson:    string;
    tokens?:       number;
    latencyMs?:    number;
    status:        'streaming' | 'done' | 'error';
    createdAt:     string;
    updatedAt:     string;
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

/**
 * Result for individual instruction assembly/disassembly.
 *
 * Rows produced by a {@link DataFormatOverrideInfo} carry their own
 * {@link Address} and {@link ByteLength} because row stride is no longer a
 * fixed 4 bytes: a `.byte` row is 1 byte, a `.dword` row is 8, etc.
 */
interface InstructionResult {
    Success: boolean;
    Assembly?: string;
    Hex?: string;
    Error?: string;
    Comment?: string;
    Label?: string;
    LineNumber?: number;
    /** Row start address (unsigned 32-bit). Present when emitted by the new variable-stride disassembly path. */
    Address?: number;
    /** Row byte length (1/2/4/8/16). Defaults to 4 when absent. */
    ByteLength?: number;
    /** Data-format kind when this row came from a persisted override. */
    DataFormatKind?: DataFormatKind;
    /**
     * True for rows the disassembler auto-rendered as data (e.g. `.asciiz`
     * produced by per-page string detection) without a persisted override.
     * Styled like an override row, but not user-editable as one.
     */
    AutoFormatted?: boolean;
    /** Code references to this row from the bridge's cross-reference index. */
    XrefCount?: number;
    /** Data words pointing at this row, from the cross-reference index. */
    DataRefCount?: number;
    /** True when a detected function begins at this row. */
    FunctionStart?: boolean;
}

export type DataFormatKind = 'byte' | 'half' | 'word' | 'dword' | 'qword' | 'float' | 'double';

/**
 * Wire payload for a single disassembler data-format override returned by
 * GETDATAFORMATS / SETDATAFORMAT.
 */
export interface DataFormatOverrideInfo {
    id: number;
    address: number;
    length: number;
    kind: DataFormatKind;
}

/** Number of bytes per element for a given data-format kind. */
export function dataFormatKindByteSize(kind: DataFormatOverrideInfo['kind']): number {
    switch (kind) {
        case 'byte':  return 1;
        case 'half':  return 2;
        case 'word':  return 4;
        case 'dword': return 8;
        case 'qword': return 16;
        case 'float': return 4;
        case 'double': return 8;
        default:      return 4;
    }
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
 * Sidecar items (assert, breakpoint, watchpoint, log) do not emit MIPS code words
 * but carry instructions for the bridge to act on alongside the patch.
 */
export interface CdsSidecarResult {
    /** Type: "assert", "breakpoint", "watchpoint", or "log". */
    Kind: string;
    /** PS2 virtual address referenced by the sidecar directive. */
    Address: number;
    // Assert fields
    /** Comparison operator: "==" or "!=". Present for assert sidecars. */
    Operator?: string;
    /** Expected memory value for assert. */
    ExpectedValue?: number;
    /** Actual value read from Ps2Emulator's memory (unset when it does not run). */
    ActualValue?: number;
    /** true = passed, false = failed, undefined = not checked. */
    AssertPassed?: boolean;
    // Watchpoint fields
    /** Number of bytes to watch (typically 1, 2, or 4). */
    WatchSize?: number;
    /** Access type: "read", "write", or "readwrite". */
    AccessType?: string;
    // Log/print fields
    /** Original CDS directive keyword: "log" or "print". */
    Directive?: string;
    /** Message text to surface for log/print sidecars. */
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
    /** Absolute disc path, e.g. "/SCUS_971.98;1" or "/MODULES/CDVD.IRX;1". */
    Path: string;
    /** Filename without path or version suffix, e.g. "SCUS_971.98". */
    Name: string;
    /** File size in bytes. */
    Size: number;
    /** True when this is the BOOT2 executable listed in SYSTEM.CNF. */
    IsBootElf: boolean;
}

/**
 * Result for a single cheat code line produced by PORTCHEATS.
 */
export interface PortCheatResult {
    /** Zero-based index of the cheat block; use this to distinguish blocks that share the same label. */
    blockIndex: number;
    /** Zero-based position of this line within its block. */
    lineIndex: number;
    /** The #Name label of the cheat block this line belongs to. */
    blockName: string;
    /** Any non-label, non-code description text found in the cheat block. Null when absent. */
    blockText: string | null;
    /** Original GameShark code line, e.g. "20123456 000003E8". */
    originalCode: string;
    /** Ported code line with the updated address, e.g. "20124ABC 000003E8". Null when not found. */
    newCode: string | null;
    /** Whether a match was found in the target dump. */
    found: boolean;
    /** Description of the method used, e.g. "HexPattern-48". */
    method: string;
    /** 0–100 confidence score. */
    confidence: number;
    /** True when more than one candidate address was found. */
    ambiguous: boolean;
    /** All candidate new code lines when ambiguous. */
    candidates: string[] | null;
    /** Source address the line was ported from; null for lines without one (values, offsets). */
    sourceAddress: number | null;
    /** Target address chosen for sourceAddress; null when not found or not applicable. */
    newAddress: number | null;
    /** How the line's code type was handled, e.g. "Pointer offsets — copied unchanged". */
    note: string | null;
}

/**
 * Result returned by the RECOMPILE socket command.
 */
export interface RecompileResult {
    /** Number of functions that were recompiled. */
    functionCount: number;
    /** Absolute paths of all files written by the recompiler. */
    writtenFiles: string[];
    /** The ELF path (or name) that was recompiled. */
    bootElf: string;
}

/**
 * Pushed unsolicited by the server when an MCP client performs an address-based
 * operation (READMEM, WRITEMEM, DISASSEMBLE, GETPC, …).  Used by the VSCode
 * extension to implement "Follow AI" mode — navigate the disassembly cursor to
 * the address the AI is currently working with.
 */
export interface AiActivityEvent {
    type:    'ai_activity';
    command: string;
    address: number;
    region:  string;
}

/**
 * Pushed unsolicited by the server when the MCP client calls session_start.
 * Used by the VSCode extension to automatically enable "Follow AI" mode.
 */
export interface AiSessionStartEvent {
    type: 'ai_session_start';
}

/**
 * Pushed unsolicited by the server when the MCP client modifies labels
 * (ADDLABELENTRY, ADDLABELENTRIES, CREATELABELSET, DELETELABELENTRY, DELETELABELSET).
 * Used by the VSCode extension to refresh the label manager UI and navigate to the
 * label set the AI is working with.
 */
export interface AiLabelUpdateEvent {
    type:     'ai_label_update';
    command:  string;
    setName?: string;
    gameId?:  string;
}

/**
 * Pushed unsolicited by the server when the MCP client runs a packet-capture
 * command (NETCAPTURE start/stop, NETRECVCAPTURE drain). Used by the packet
 * viewer + AI activity panel to surface live AI packet operations.
 */
export interface AiPacketEvent {
    type:       'ai_packet';
    command:    string;
    direction?: number;
    enable?:    boolean;
}

/**
 * Pushed unsolicited by the server for EVERY MCP tool the AI runs. The
 * CodeDesigner.AI server emits one AI_TOOL_CALL per invocation; the bridge
 * re-broadcasts it inside this envelope. Drives the full call log in the AI
 * Activity panel (tool name + arguments + result preview), which the user
 * uses for debugging and later automation.
 */
export interface AiToolCallEvent {
    type:    'ai_tool_call';
    payload: {
        tool:       string;
        args:       string;   // JSON string of the tool arguments
        result:     string;   // truncated result preview
        ok:         boolean;
        elapsedMs?: number;
    } | null;
}

/**
 * Client for the C# server: typed commands, queueing and push events over any BridgeTransport.
 */
export class BridgeClient {
    /**
     * An emulator connected directly (Ps2Emulator's debug server), if any.
     * While one is attached, the live emulator calls (memory, registers,
     * execution, breakpoints, disassembly) go to it instead of the Bridge;
     * everything else still needs the Bridge.
     */
    private direct: DirectEmulator | null = null;
    /** What this client holds on each pad of a direct emulator. */
    private directPads: { buttons: DirectPadButton[]; sticks: [number, number, number, number] }[] = [];
    /** Packets taken from the direct emulator since capture started, for row numbers. */
    private directCaptured = 0;
    /** When the first captured packet was taken, for the time column. */
    private directCaptureStartUs: number | undefined;

    private async directPadHold(
        direct: DirectEmulator, port: number, buttons: DirectPadButton[], sticks: [number, number, number, number],
    ): Promise<void> {
        this.directPads[port] = { buttons, sticks };
        const resting = sticks.every(v => v === 0x7F);
        if (buttons.length === 0 && resting) {
            await direct.padRelease(port);
        } else {
            await direct.padHold(port, buttons, resting ? undefined : sticks);
        }
    }

    /** The cheat engine's state as a direct emulator's patch has it. */
    private directCheatInfo(patch: { enabled: boolean; rows: number; errors: string[]; warnings: string[] } | undefined): CheatEngineInfo {
        return {
            injected: !!patch?.enabled,
            engineBase: 0,
            entries: patch?.rows ?? 0,
            imageSize: 0,
            warnings: patch ? [...patch.errors, ...patch.warnings] : [],
        };
    }
    /** The comment-column hint level for disassembly from a direct emulator. */
    private directHintLevel: DirectHintLevel = 'full';
    private transportOpen = false;
    private connected: boolean = false;
    private wasConnected: boolean = false;
    private messageBuffer: string = '';
    private messageHandlers: Array<(message: string) => void> = [];
    private connectionHandlers: Array<() => void> = [];
    private disconnectionHandlers: Array<() => void> = [];
    /**
     * Handlers registered via onActivityNotification() — invoked when the server
     * pushes an unsolicited AI-activity event (type === "ai_activity").
     */
    private _activityHandlers: Array<(event: AiActivityEvent) => void> = [];
    /**
     * Handlers registered via onSessionStart() — invoked when the server
     * pushes an unsolicited ai_session_start event.
     */
    private _sessionStartHandlers: Array<(event: AiSessionStartEvent) => void> = [];
    /**
     * Handlers registered via onLabelUpdate() — invoked when the server
     * pushes an unsolicited ai_label_update event.
     */
    private _labelUpdateHandlers: Array<(event: AiLabelUpdateEvent) => void> = [];
    /**
     * Handlers registered via onPacketActivity() — invoked when the server
     * pushes an unsolicited ai_packet event.
     */
    private _packetHandlers: Array<(event: AiPacketEvent) => void> = [];
    /**
     * Handlers registered via onToolCall() — invoked when the server pushes an
     * unsolicited ai_tool_call event (one per MCP tool the AI runs).
     */
    private _toolCallHandlers: Array<(event: AiToolCallEvent) => void> = [];
    /**
     * FIFO queue of one-time response handlers added by sendCommand().
     * Using a dedicated queue (with shift()) avoids the Array.forEach +
     * splice-during-iteration bug: when forEach iterates [A, B, C] and A
     * splices itself out at index 0, forEach advances to index 1 which is
     * now C (B shifted to 0), so B is skipped and C receives A's response.
     * With shift(), each incoming message pops exactly one handler off the
     * front of the queue in FIFO order — matching the order commands were sent.
     */
    private _pendingResponses: Array<(message: string) => void> = [];
    /**
     * If a connection attempt is in progress, this holds the shared Promise so
     * concurrent callers (e.g. extension auto-connect + connectSocketAndOpenViews)
     * both await the same attempt rather than creating a second socket that
     * destroys the first one's listeners and leaves its Promise permanently pending.
     */
    private _connectingPromise: Promise<void> | null = null;
    private _interactiveQueue: Array<() => Promise<void>> = [];
    private _backgroundQueue: Array<() => Promise<void>> = [];
    private _queuePumping = false;

    constructor(private readonly transport: BridgeTransport) {}

    /**
     * Connect to the C# server
     */
    public connect(): Promise<void> {
        if (this.connected) {
            console.log('Already connected to server');
            return Promise.resolve();
        }

        // If a connection attempt is already in progress, return the shared
        // promise so concurrent callers wait on the same attempt rather than
        // each spawning a new socket that would destroy the other's listeners
        // and leave its Promise permanently pending.
        if (this._connectingPromise) {
            console.log('[SOCKET] Connection already in progress, sharing existing attempt');
            return this._connectingPromise;
        }

        // Close any previous connection that may still be open (e.g. a
        // failed prior connection attempt that was never cleaned up).
        // This prevents a stale close from resetting state after
        // the new connection has opened successfully.
        if (this.transportOpen) {
            this.transport.close();
            this.transportOpen = false;
        }

        // Reset the message buffer so that any partial JSON left over from
        // a previous (dropped) connection cannot combine with data from the
        // new connection and form a garbled/incorrect response message.
        this.messageBuffer = '';

        this._connectingPromise = this.transport.open({
            onData: chunk => this.handleData(chunk),
            onClose: () => this.handleClose(),
        }).then(() => {
            console.log('[SOCKET] Connected to C# server');
            this.transportOpen = true;
            this.connected = true;
            this.wasConnected = true;
            this.connectionHandlers.forEach(handler => handler());
        }).finally(() => {
            this._connectingPromise = null;
        });

        return this._connectingPromise;
    }

    private handleData(chunk: string): void {
        console.log(`[SOCKET] Received chunk (${chunk.length} chars)`);

        // Add to buffer
        this.messageBuffer += chunk;

        // Process complete messages (delimited by newline)
        let newlineIndex: number;
        while ((newlineIndex = this.messageBuffer.indexOf('\n')) !== -1) {
            const message = this.messageBuffer.substring(0, newlineIndex).trim();
            this.messageBuffer = this.messageBuffer.substring(newlineIndex + 1);

            if (message.length > 0) {
                console.log(`[SOCKET] Complete message received (${message.length} chars): ${message.substring(0, 100)}...`);

                // Detect server-pushed notifications (have a "type" field but no
                // "Success" field — all CommandResponse objects carry "Success").
                // These must NOT consume a pending FIFO response slot.
                let parsed: Record<string, unknown> | null = null;
                try { parsed = JSON.parse(message); } catch { /* not JSON */ }
                if (parsed && typeof parsed['type'] === 'string' && !('Success' in parsed)) {
                    if (parsed['type'] === 'ai_activity') {
                        const evt = parsed as unknown as AiActivityEvent;
                        this._activityHandlers.forEach(h => h(evt));
                    } else if (parsed['type'] === 'ai_session_start') {
                        const evt = parsed as unknown as AiSessionStartEvent;
                        this._sessionStartHandlers.forEach(h => h(evt));
                    } else if (parsed['type'] === 'ai_label_update') {
                        const evt = parsed as unknown as AiLabelUpdateEvent;
                        this._labelUpdateHandlers.forEach(h => h(evt));
                        // Labels the AI edits reach a directly connected emulator too.
                        void this.syncLabelsToDirect();
                    } else if (parsed['type'] === 'ai_packet') {
                        const evt = parsed as unknown as AiPacketEvent;
                        this._packetHandlers.forEach(h => h(evt));
                    } else if (parsed['type'] === 'ai_tool_call') {
                        const evt = parsed as unknown as AiToolCallEvent;
                        this._toolCallHandlers.forEach(h => h(evt));
                    }
                    this.messageHandlers.forEach(handler => handler(message));
                    continue;
                }

                // Dispatch to the first pending one-time response handler (FIFO).
                // shift() is safe here: it removes the element before calling it,
                // so there is no splice-during-iteration issue.
                const pending = this._pendingResponses.shift();
                if (pending) {
                    pending(message);
                }
                // Also dispatch to any persistent broadcast handlers.
                this.messageHandlers.forEach(handler => handler(message));
            }
        }
    }

    private handleClose(): void {
        console.log('[SOCKET] Connection closed');
        const wasActuallyConnected = this.wasConnected;
        this.connected = false;
        this.wasConnected = false;
        this.transportOpen = false;
        // Cancel any in-flight requests: they will never receive a response
        // now that the connection has been dropped.  This mirrors what
        // disconnect() does for intentional disconnects.
        const stale = this._pendingResponses.splice(0);
        for (const handler of stale) {
            try { handler('{"Success":false,"Error":"Connection closed"}'); } catch { /* ignore */ }
        }
        // Only call disconnect handlers if we were actually connected
        if (wasActuallyConnected) {
            this.disconnectionHandlers.forEach(handler => handler());
        }
    }

    /**
     * Send a message to the C# server
     */
    public sendMessage(message: string): Promise<void> {
        if (!this.connected) {
            return Promise.reject(new Error('Not connected to server'));
        }
        return this.transport.write(message + '\n').then(() => {
            console.log(`[SOCKET] Sent: ${message}`);
        }, (error: Error) => {
            console.error(`[SOCKET] Error sending message: ${error.message}`);
            throw error;
        });
    }

    /**
     * Disconnect from the server
     */
    public disconnect(): void {
        if (this.transportOpen) {
            this.transport.close();
            this.transportOpen = false;
        }
        this.connected = false;
        this.wasConnected = false;
        // Cancel any in-flight requests that will never receive a response.
        const stale = this._pendingResponses.splice(0);
        for (const handler of stale) {
            try { handler('{"Success":false,"Error":"Disconnected"}'); } catch { /* ignore */ }
        }
        console.log('[SOCKET] Disconnected from server');
    }

    /**
     * Register a message handler
     */
    public onMessage(handler: (message: string) => void): void {
        this.messageHandlers.push(handler);
    }

    /**
     * Register a connection handler
     */
    public onConnected(handler: () => void): void {
        this.connectionHandlers.push(handler);
    }

    /**
     * Register a disconnection handler
     */
    public onDisconnected(handler: () => void): void {
        this.disconnectionHandlers.push(handler);
    }

    /**
     * Register a handler for server-pushed AI-activity notifications.
     * Called when the server broadcasts an unsolicited ai_activity event
     * (e.g. because the MCP AI assistant performed a memory read or write).
     */
    public onActivityNotification(handler: (event: AiActivityEvent) => void): void {
        this._activityHandlers.push(handler);
    }

    /**
     * Register a handler for server-pushed AI session-start notifications.
     * Called when the MCP server calls session_start, signalling that an AI
     * session is beginning and Follow AI mode should be enabled.
     */
    public onSessionStart(handler: (event: AiSessionStartEvent) => void): void {
        this._sessionStartHandlers.push(handler);
    }

    /**
     * Register a handler for server-pushed AI label-update notifications.
     * Called when the MCP server modifies labels (add/delete entry, create/delete set).
     * The front end can use this to refresh the label manager and navigate to the
     * label set the AI is working with.
     */
    public onLabelUpdate(handler: (event: AiLabelUpdateEvent) => void): void {
        this._labelUpdateHandlers.push(handler);
    }

    /**
     * Register a handler for server-pushed AI packet-capture notifications
     * (NETCAPTURE / NETRECVCAPTURE). Delivered only to subscribed streams
     * (call subscribeActivity()).
     */
    public onPacketActivity(handler: (event: AiPacketEvent) => void): void {
        this._packetHandlers.push(handler);
    }

    /**
     * Register a handler for server-pushed AI tool-call notifications — one per
     * MCP tool the AI runs, carrying the tool name, arguments, and a result
     * preview. Delivered only to subscribed streams (call subscribeActivity()).
     */
    public onToolCall(handler: (event: AiToolCallEvent) => void): void {
        this._toolCallHandlers.push(handler);
    }

    /**
     * Send a SUBSCRIBE_ACTIVITY command to opt-in to AI-activity push notifications.
     * After calling this, the server will push {"type":"ai_activity",...} messages
     * whenever another client (e.g. the MCP server) performs an address-based operation.
     */
    public async subscribeActivity(): Promise<void> {
        if (!this.connected) {
            return; // AI activity comes from the Bridge only.
        }
        try {
            await this.sendCommand({ Command: 'SUBSCRIBE_ACTIVITY' });
        } catch (err) {
            console.warn('[SOCKET] subscribeActivity failed:', err);
        }
    }

    /**
     * Check if connected: to the Bridge, or directly to an emulator.
     */
    public isConnected(): boolean {
        return this.connected || this.isDirect();
    }

    /** Whether the Bridge itself is connected (not just a direct emulator). */
    public isBridgeConnected(): boolean {
        return this.connected;
    }

    /** Whether live emulator calls go to a directly connected emulator. */
    public isDirect(): boolean {
        return !!this.direct && this.direct.isConnected();
    }

    /** The directly connected emulator, if any. */
    public getDirect(): DirectEmulator | null {
        return this.isDirect() ? this.direct : null;
    }

    /**
     * The emulator for a live call: Ps2Emulator, connected directly. The
     * Bridge serves the workspace only, so without it there is no emulator.
     */
    public requireDirect(): DirectEmulator {
        const direct = this.getDirect();
        if (!direct) {
            throw new Error('Ps2Emulator is not connected: Run it from the panel, or use "CodeDesigner: Connect to Ps2Emulator".');
        }
        return direct;
    }

    /**
     * Sends live emulator calls to `emulator` from now on, and tells the
     * connection handlers (views refresh as on a Bridge connection).
     */
    public attachDirect(emulator: DirectEmulator): void {
        this.direct?.close();
        this.direct = emulator;
        this.connectionHandlers.forEach(handler => handler());
        void this.syncLabelsToDirect();
    }

    /**
     * Pushes the Bridge's labels into the directly connected emulator, so its
     * disassembly, xrefs and function lists show them. Runs on attach and
     * whenever labels change; does nothing unless both are connected.
     */
    public async syncLabelsToDirect(): Promise<void> {
        const direct = this.getDirect();
        if (!direct || !this.connected) {
            return;
        }
        try {
            const labels = await this.bridgeGetLabels();
            const symbols = labels
                .map(l => ({ address: parseInt(l.address, 16) >>> 0, name: l.label }))
                .filter(s => !Number.isNaN(s.address) && s.name);
            await direct.setSymbols(DIRECT_LABEL_SOURCE, symbols, true);
        } catch (err) {
            console.warn('[DIRECT] pushing labels to the emulator failed:', err);
        }
    }

    /**
     * Closes the direct emulator connection; live calls go to the Bridge
     * again. Tells the disconnection handlers unless the Bridge is still up.
     */
    public detachDirect(): void {
        const direct = this.direct;
        if (!direct) {
            return;
        }
        this.direct = null;
        direct.close();
        if (!this.connected) {
            this.disconnectionHandlers.forEach(handler => handler());
        }
    }

    /**
     * Send a command with no typed wrapper and wait for its response.
     */
    public sendRawCommand(command: Record<string, unknown> & { Command: string }, timeoutMs?: number): Promise<CommandResponse> {
        return this.sendCommand(command as unknown as CommandMessage, timeoutMs);
    }

    /**
     * Send a command and wait for response
     */
    private sendCommand(command: CommandMessage, timeoutMs: number = 10000): Promise<CommandResponse> {
        return this.enqueueCommand(this.withDefaultNavSource(command), timeoutMs);
    }

    private withDefaultNavSource(command: CommandMessage): CommandMessage {
        if (command.Address === undefined || command.NavSource !== undefined) {
            return command;
        }

        // The bridge auto-records address-bearing commands in navigation
        // history. Commands sent by this client originate from the VS Code UI,
        // so mark them as user activity unless the caller explicitly says
        // otherwise.
        return { ...command, NavSource: 'user' };
    }

    private enqueueCommand(command: CommandMessage, timeoutMs: number): Promise<CommandResponse> {
        return new Promise((resolve, reject) => {
            const run = async () => {
                try {
                    const r = await this.sendCommandNow(command, timeoutMs);
                    resolve(r);
                } catch (e) {
                    reject(e);
                }
            };
            const prio = this.commandPriority(command.Command);
            if (prio === 'interactive') this._interactiveQueue.push(run);
            else this._backgroundQueue.push(run);
            void this.pumpQueue();
        });
    }

    private async pumpQueue(): Promise<void> {
        if (this._queuePumping) return;
        this._queuePumping = true;
        try {
            while (this._interactiveQueue.length > 0 || this._backgroundQueue.length > 0) {
                const next = this._interactiveQueue.shift() ?? this._backgroundQueue.shift();
                if (!next) break;
                await next();
            }
        } finally {
            this._queuePumping = false;
        }
    }

    private commandPriority(command: string): 'interactive' | 'background' {
        const c = command.toUpperCase();
        if (
            c === 'PAUSE' || c === 'RESUME' || c === 'STEP' || c === 'STEPINTO' || c === 'STEPOVER' || c === 'STEPOUT' ||
            c === 'SETBP' || c === 'CLEARBP' || c === 'CLEARBP_ALL' || c === 'GETPC' || c === 'GETSTATUS' ||
            c === 'READMEM' || c === 'READMEMBATCH' || c === 'GETREGS'
        ) return 'interactive';
        return 'background';
    }

    private sendCommandNow(command: CommandMessage, timeoutMs: number = 10000): Promise<CommandResponse> {
        return new Promise((resolve, reject) => {
            if (!this.connected) {
                reject(new Error('Not connected to server'));
                return;
            }

            let responseReceived = false;

            // Set up one-time response handler
            const responseHandler = (message: string) => {
                if (responseReceived) return;
                responseReceived = true;
                clearTimeout(timeout);

                try {
                    // Strip "Echo: " prefix if present
                    let cleanMessage = message;
                    const echoPrefix = 'Echo: ';
                    if (cleanMessage.startsWith(echoPrefix)) {
                        cleanMessage = cleanMessage.substring(echoPrefix.length);
                        console.log('[SOCKET] Stripped Echo prefix from response');
                    }
                    
                    // Try to parse JSON
                    const response: CommandResponse = JSON.parse(cleanMessage);
                    console.log('[SOCKET] Successfully parsed response:', response.Success ? 'SUCCESS' : 'FAILED');
                    resolve(response);
                } catch (error) {
                    const errorMsg = error instanceof Error ? error.message : String(error);
                    console.error('[SOCKET] Failed to parse response:', errorMsg);
                    console.error('[SOCKET] Message length:', message.length);
                    console.error('[SOCKET] First 200 chars:', message.substring(0, 200));
                    console.error('[SOCKET] Last 200 chars:', message.substring(Math.max(0, message.length - 200)));
                    reject(new Error(`Failed to parse response: ${errorMsg}`));
                }
            };

            // Enqueue handler — must be done before writing to the socket so
            // there is no window where the response arrives before the handler
            // is registered.
            this._pendingResponses.push(responseHandler);

            // Set timeout
            const timeout = setTimeout(() => {
                if (!responseReceived) {
                    responseReceived = true;
                    // Remove the handler from the pending queue
                    const index = this._pendingResponses.indexOf(responseHandler);
                    if (index > -1) {
                        this._pendingResponses.splice(index, 1);
                    }
                    reject(new Error('Command timeout'));
                }
            }, timeoutMs);

            // Send command
            const commandStr = JSON.stringify(command) + '\n';
            console.log(`[SOCKET] Sending command: ${commandStr.trim()}`);
            this.transport.write(commandStr).catch((error: Error) => {
                clearTimeout(timeout);
                responseReceived = true;
                // Remove the handler from the pending queue
                const index = this._pendingResponses.indexOf(responseHandler);
                if (index > -1) {
                    this._pendingResponses.splice(index, 1);
                }
                reject(error);
            });
        });
    }

    /**
     * Compile a .cds (Code Designer Script) assembly file.
     * Forwards the raw source text to the backend COMPILECDS command.
     * @param labels Optional pre-resolved pointer-manager labels (e.g. from PointerManagerProvider.getResolvedLabels()).
     *               These are injected into the compiler so CDS source can reference them as `:seal.health` or `@seal.health`.
     */
    public async compileCds(sourceCode: string, labels?: LabelEntry[]): Promise<{ results: InstructionResult[]; sidecars: CdsSidecarResult[] }> {
        const command: CommandMessage = {
            Command: 'COMPILECDS',
            Data: sourceCode
        };
        if (labels && labels.length > 0) {
            command.Labels = labels;
        }

        const response = await this.sendCommand(command);

        // Assert failures return Success=false but still populate Results (with error entries)
        // and Sidecars (with assert metadata). Only throw when no Results are present — that
        // indicates a hard protocol or parse error with nothing for the caller to inspect.
        if (!response.Success && !response.Results) {
            throw new Error(response.Error || 'Compilation failed');
        }

        const results = response.Results ?? [];
        const sidecars = response.Sidecars ?? [];
        // Asserts are checked against Ps2Emulator's memory, when it runs: a failed one
        // aborts the code output, as it always has.
        const direct = this.getDirect();
        if (direct) {
            const failures: InstructionResult[] = [];
            for (const sc of sidecars.filter(s => s.Kind === 'assert' && s.ExpectedValue !== undefined)) {
                try {
                    const actual = (await direct.readMemory(sc.Address >>> 0, 4, 'ee')).readUInt32LE(0);
                    sc.ActualValue = actual;
                    sc.AssertPassed = sc.Operator === '!=' ? actual !== sc.ExpectedValue : actual === sc.ExpectedValue;
                    if (!sc.AssertPassed) {
                        const hex = (n: number) => `0x${(n >>> 0).toString(16).toUpperCase().padStart(8, '0')}`;
                        failures.push({ Success: false, Error: `Assert failed at ${hex(sc.Address)}: expected ${sc.Operator ?? '=='} ${hex(sc.ExpectedValue!)}, got ${hex(actual)}` } as InstructionResult);
                    }
                } catch { /* unreadable: left unchecked */ }
            }
            if (failures.length > 0) {
                return { results: [...results.filter(r => !r.Success), ...failures], sidecars };
            }
        }
        return { results, sidecars };
    }

    public async validateCds(sourceCode: string, labels?: LabelEntry[]): Promise<{ success: boolean; message?: string; diagnostics: CdsDiagnostic[] }> {
        const command: CommandMessage = {
            Command: 'VALIDATECDS',
            Data: sourceCode
        };
        if (labels && labels.length > 0) {
            command.Labels = labels;
        }

        const response = await this.sendCommand(command);
        return {
            success: response.Success,
            message: response.Message || response.Error,
            diagnostics: response.Diagnostics ?? [],
        };
    }

    /**
     * Assemble MIPS assembly code to hex
     */
    public async assemble(asmCode: string): Promise<InstructionResult[]> {
        const command: CommandMessage = {
            Command: 'ASSEMBLE',
            Data: asmCode
        };

        const response = await this.sendCommand(command);
        
        if (!response.Success) {
            throw new Error(response.Error || 'Assembly failed');
        }

        return response.Results || [];
    }

    /**
     * Disassemble hex code to MIPS assembly.
     *
     * @param hexCode - Hex instruction codes to disassemble (space/comma separated)
     * @param startAddress - Optional starting address for branch calculation
     * @param gameId - Optional game/disc identifier so the bridge can apply
     *                 any persisted data-format overrides for the range.
     */
    public async disassemble(hexCode: string, startAddress?: number, gameId?: string, skipDataFormatOverrides = false): Promise<InstructionResult[]> {
        const direct = this.getDirect();
        if (direct) {
            // Disassembly from the emulator, with its state hints in the
            // comment column. Data-format overrides need the Bridge
            // (workspace service) and are left out.
            const words = hexCode.trim().split(/\s+/).filter(Boolean).map(w => parseInt(w, 16) >>> 0);
            const lines = await direct.disassembleWords(startAddress ?? 0, words, this.directHintLevel);
            return lines.map(line => ({
                Success: true,
                Assembly: line.text,
                Hex: line.word.toString(16).toUpperCase().padStart(8, '0'),
                Address: line.address >>> 0,
                ByteLength: 4,
                Label: line.label || undefined,
                Comment: line.hint ? `; ${line.hint}` : line.targetLabel ? `-> ${line.targetLabel}` : undefined,
                XrefCount: line.codeRefs || undefined,
                DataRefCount: line.dataRefs || undefined,
                FunctionStart: line.functionStart || undefined,
            }));
        }
        const command: CommandMessage = {
            Command: 'DISASSEMBLE',
            Data: hexCode
        };

        // Include address if provided
        if (startAddress !== undefined) {
            command.Address = startAddress;
        }
        if (gameId) {
            command.GameId = gameId;
        }
        if (skipDataFormatOverrides) {
            command.SkipDataFormatOverrides = true;
        }

        const response = await this.sendCommand(command);

        if (!response.Success) {
            throw new Error(response.Error || 'Disassembly failed');
        }

        return response.Results || [];
    }

    /**
     * Fetch the persisted data-format overrides whose byte range intersects
     * `[startAddress, endAddress)` for the given game.  Pass `undefined` for
     * `gameId` (or omit both addresses) to fetch all overrides.
     */
    public async getDataFormats(
        gameId?: string,
        startAddress?: number,
        endAddress?: number,
    ): Promise<DataFormatOverrideInfo[]> {
        const command: CommandMessage = { Command: 'GETDATAFORMATS' };
        if (gameId) command.GameId = gameId;
        if (startAddress !== undefined) command.Address = startAddress;
        if (endAddress !== undefined) command.EndAddress = endAddress;

        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'GETDATAFORMATS failed');
        }
        return response.DataFormatOverrides ?? [];
    }

    /**
     * Persist a new override for the disassembler so the span
     * `[address, address + length)` is rendered as the given typed directive
     * (`.byte`/`.half`/`.word`/`.dword`/`.qword`/`.float`/`.double`).  Any existing overrides that
     * overlap are replaced atomically.
     */
    public async setDataFormat(
        address: number,
        length: number,
        kind: DataFormatOverrideInfo['kind'],
        gameId?: string,
    ): Promise<DataFormatOverrideInfo | undefined> {
        const command: CommandMessage = {
            Command: 'SETDATAFORMAT',
            Address: address,
            Length: length,
            DataFormatKind: kind,
        };
        if (gameId) command.GameId = gameId;

        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'SETDATAFORMAT failed');
        }
        return response.DataFormatOverrides?.[0];
    }

    /**
     * Persist multiple data-format overrides in one bridge/database round trip.
     */
    public async setDataFormats(
        overrides: Array<Pick<DataFormatOverrideInfo, 'address' | 'length' | 'kind'>>,
        gameId?: string,
    ): Promise<DataFormatOverrideInfo[]> {
        const command: CommandMessage = {
            Command: 'SETDATAFORMATS',
            DataFormatOverrides: overrides,
        };
        if (gameId) command.GameId = gameId;

        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'SETDATAFORMATS failed');
        }
        return response.DataFormatOverrides ?? [];
    }

    /**
     * Remove every override whose byte range intersects
     * `[address, address + length)`, restoring default 4-byte instruction
     * decoding.  Returns the number of rows deleted.
     */
    public async clearDataFormat(
        address: number,
        length: number,
        gameId?: string,
    ): Promise<void> {
        const command: CommandMessage = {
            Command: 'CLEARDATAFORMAT',
            Address: address,
            Length: length,
        };
        if (gameId) command.GameId = gameId;

        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'CLEARDATAFORMAT failed');
        }
    }

    /**
     * Read raw bytes from the live emulator memory.
     * @param address  - Start virtual address (EE or IOP space)
     * @param length   - Number of bytes to read (1–65536 per call)
     * @param region   - "ee" (default) or "iop"
     */
    public async readMemory(address: number, length: number, region: string = 'ee'): Promise<Buffer> {
        const direct = this.requireDirect();
        return direct.readMemory(address, length, region);
    }

    /**
     * Read fixed-size slices from many addresses in one bridge command.
     * Returns a map keyed by uppercase 8-digit hex address (no 0x prefix).
     */
    public async readMemoryBatch(
        addresses: number[],
        length: number,
        region: string = 'ee',
    ): Promise<Map<number, Buffer>> {
        if (addresses.length === 0) return new Map();
        const direct = this.requireDirect();
        return direct.readMemoryBatch(addresses, length, region);
    }

    /**
     * Reads a contiguous region of memory from Ps2Emulator in one call, optionally finding a
     * byte pattern in it (-1 matches any byte).
     *
     * @param start          - Inclusive start address.
     * @param end            - Exclusive end address (must be > start).
     * @param searchPattern  - Optional byte pattern to search for.
     * @param region         - "ee" (default) or "iop".
     * @returns              - The bytes as hex and the absolute addresses of matches.
     */
    public async readMemoryRegion(
        start: number,
        end: number,
        searchPattern?: number[],
        region: string = 'ee',
    ): Promise<{ bytes: string; matches: number[] }> {
        const data = await this.readLive(start, end - start, region);
        return { bytes: data.toString('hex'), matches: searchPattern ? findPattern(data, searchPattern, start) : [] };
    }

    /** `length` bytes of live memory from Ps2Emulator, for analysis in the workspace service. */
    private async readLive(address: number, length: number, region: string = 'ee'): Promise<Buffer> {
        if (length <= 0) { throw new Error('the range is empty'); }
        return this.requireDirect().readMemory(address >>> 0, length, region);
    }

    /**
     * Finds a byte pattern in live memory.
     * @param start    Inclusive start address.
     * @param end      Exclusive end address.
     * @param pattern  Byte values 0–255; -1 is a wildcard.
     * @param region   "ee" (default) or "iop".
     * @returns        Absolute addresses of every match.
     */
    public async scanPattern(start: number, end: number, pattern: number[], region: string = 'ee'): Promise<number[]> {
        return findPattern(await this.readLive(start, end - start, region), pattern, start);
    }

    // ── Watchpoints (Ps2Emulator) ──────────────────────────────────────────────

    /** Watches EE accesses to [start, end): `cond` 1 = read, 2 = write, 3 = both (default). */
    public async addWatch(start: number, end: number, cond: number = 3): Promise<void> {
        const access = cond === 1 ? 'read' : cond === 2 ? 'write' : 'read+write';
        await this.requireDirect().setWatchpoint(start, end, access);
    }

    /** Removes the watchpoints on exactly [start, end). */
    public async removeWatch(start: number, end: number): Promise<void> {
        await this.requireDirect().clearWatchpoints({ start, end });
    }

    public async clearAllWatches(): Promise<void> {
        await this.requireDirect().clearWatchpoints();
    }

    public async listWatches(): Promise<WatchInfo[]> {
        const hex = (n: number) => `0x${(n >>> 0).toString(16).toUpperCase().padStart(8, '0')}`;
        return (await this.requireDirect().listWatchpoints())
            .map(w => ({ Cpu: 'EE', Start: hex(w.start), End: hex(w.end), Cond: w.access }));
    }

    /**
     * Resume the emulator and block (server-side) until the EE PC reaches
     * targetPc.  Returns the full register dump at the hit point.
     *
     * @param targetPc  - EE virtual address to run to.
     * @param timeoutMs - Timeout in milliseconds (default 10 000).
     * @returns         - Full register dump (same shape as getRegisters()).
     */
    public async runUntilPc(targetPc: number, timeoutMs: number = 10_000): Promise<RegisterDump> {
        const direct = this.requireDirect();
        await direct.runTo(targetPc, timeoutMs);
        return direct.getRegisters();
    }

    /**
     * Takes a snapshot: writes Ps2Emulator's memory (EE RAM, BIOS, scratchpad, IOP RAM) to .bin
     * files in outputPath, with the displayed frame as Screenshot.png, and records it in the
     * workspace. Returns the regions written.
     */
    public async snapshot(outputPath: string, isoName?: string, title?: string, description?: string): Promise<SnapshotRegion[]> {
        const direct = this.requireDirect();
        fs.mkdirSync(outputPath, { recursive: true });
        const regions: SnapshotRegion[] = [];
        for (const r of SNAPSHOT_REGIONS) {
            const bytes = await direct.readSpace(r.space, 0, r.size);
            fs.writeFileSync(path.join(outputPath, r.file), bytes);
            regions.push({ Label: r.label, File: r.file, BaseAddress: r.base, Size: bytes.length });
        }
        try {
            const frame = await direct.screenshot();
            fs.writeFileSync(path.join(outputPath, 'Screenshot.png'), encodePng(frame.width, frame.height, frame.rgba));
        } catch { /* nothing displayed yet: a snapshot without a picture */ }
        await this.registerSnapshot(outputPath, regions, isoName, title, description);
        return regions;
    }

    /** Records a snapshot directory in the workspace (snapshot-info.json and the database). */
    public async registerSnapshot(outputPath: string, regions: SnapshotRegion[], isoName?: string, title?: string, description?: string): Promise<void> {
        const response = await this.sendCommand({
            Command: 'REGISTERSNAPSHOT', OutputPath: outputPath, Regions: regions, IsoName: isoName, Title: title, Description: description,
        } as CommandMessage);
        if (!response.Success) {
            throw new Error(response.Error || 'RegisterSnapshot failed');
        }
    }

    /**
     * Restores a snapshot's RAM (EE RAM, scratchpad, IOP RAM) into Ps2Emulator, paused first; it
     * stays paused. The BIOS is read-only and left alone. Returns the regions restored.
     */
    public async loadSnapshot(inputPath: string): Promise<SnapshotRegion[]> {
        const direct = this.requireDirect();
        await direct.pause();
        const regions: SnapshotRegion[] = [];
        for (const r of SNAPSHOT_REGIONS.filter(r => r.space !== 'BIOS')) {
            const file = path.join(inputPath, r.file);
            if (!fs.existsSync(file)) { continue; }
            const bytes = fs.readFileSync(file).subarray(0, r.size);
            await direct.writeSpace(r.space, 0, bytes);
            regions.push({ Label: r.label, File: file, BaseAddress: r.base, Size: bytes.length });
        }
        return regions;
    }

    /**
     * Set the EE program counter to a specific virtual address.
     * The emulator must be paused; this does not resume execution.
     */
    public async setPC(address: number): Promise<void> {
        await this.setRegister('PC', 0, `0x${address.toString(16).toUpperCase().padStart(8, '0')}`);
    }

    /**
     * Pause emulator execution
     */
    public async pause(): Promise<void> {
        const direct = this.requireDirect();
        return direct.pause();
    }

    /**
     * Resume emulator execution
     */
    public async resume(): Promise<void> {
        const direct = this.requireDirect();
        return direct.resume();
    }

    /**
     * Set a breakpoint at the given EE virtual address
     */
    public async setBreakpoint(address: number): Promise<void> {
        const direct = this.requireDirect();
        return direct.setBreakpoint(address);
    }

    /**
     * Clear the breakpoint at the given EE virtual address
     */
    public async clearBreakpoint(address: number): Promise<void> {
        const direct = this.requireDirect();
        return direct.clearBreakpoint(address);
    }

    /**
     * Clear all breakpoints
     */
    public async clearAllBreakpoints(): Promise<void> {
        const direct = this.requireDirect();
        return direct.clearAllBreakpoints();
    }

    /**
     * Get the current EE PC and paused/breakpoint state.
     */
    public async getPC(): Promise<{
        paused: boolean;
        pc: number;
        breakpointHitAddress?: number;
    }> {
        return this.requireDirect().status();
    }

    /**
     * Execute exactly one EE instruction while keeping the emulator paused.
     * Returns the new PC / paused / breakpoint state and any per-address
     * analysis comments produced by executing the instruction with the real
     * register state.
     */
    public async step(): Promise<{ paused: boolean; pc: number; breakpointHitAddress?: number; instructionAnalysis?: Record<string, string> }> {
        return this.requireDirect().stepInto();
    }

    /**
     * Step into (non-blocking). Emulator will run until the next pause/breakpoint.
     */
    public async stepInto(): Promise<void> {
        const direct = this.requireDirect();
        await direct.stepInto();
        return;
    }

    /**
     * Step over (non-blocking). Emulator will run until the next pause/breakpoint.
     */
    public async stepOver(): Promise<void> {
        const direct = this.requireDirect();
        return direct.stepOver();
    }

    /**
     * Step out (non-blocking). Emulator will run until the next pause/breakpoint.
     */
    public async stepOut(): Promise<void> {
        const direct = this.requireDirect();
        return direct.stepOut();
    }

    /** The frame Ps2Emulator displays now (also while paused). */
    public async screenshot(): Promise<{ width: number; height: number; rgba: Buffer }> {
        return this.requireDirect().screenshot();
    }

    /**
     * Read the full input state of a PS2 controller pad.
     * Returns buttons bitmask, analog stick positions, and 16 pressure values.
     */
    public async padGetState(padSlot: number = 0): Promise<PadStateInfo> {
        const direct = this.requireDirect();
        const state = await direct.padGet(padSlot);
        const buttons = PINE_BUTTONS.reduce((mask, b, bit) => state.held.includes(b) ? mask & ~(1 << bit) : mask, 0xFFFF);
        const [lx, ly, rx, ry] = state.sticks ?? [0x7F, 0x7F, 0x7F, 0x7F];
        return { buttons, lx, ly, rx, ry, pressures: new Array(16).fill(0) };
    }

    /**
     * Set analog stick positions.
     * Values are 0–255 with 0x7F = centred.
     */
    public async padSetAnalog(padSlot: number, lx: number = 0x7F, ly: number = 0x7F, rx: number = 0x7F, ry: number = 0x7F): Promise<void> {
        const direct = this.requireDirect();
        const held = this.directPads[padSlot]?.buttons ?? [];
        await this.directPadHold(direct, padSlot, held, [lx, ly, rx, ry]);
        return;
    }

    /**
     * Replace the complete input state of a pad.
     * pressures must be 16 bytes in PadDualshock2::Inputs order; pressure > 0 means pressed.
     */
    public async padSetState(padSlot: number, lx: number = 0x7F, ly: number = 0x7F, rx: number = 0x7F, ry: number = 0x7F, pressures: number[] = new Array(16).fill(0), buttons: number = 0xFFFF): Promise<void> {
        if (pressures.length !== 16) {
            throw new Error('padSetState: pressures must be exactly 16 elements');
        }
        const direct = this.requireDirect();
        const held = PINE_BUTTONS.filter((_, bit) => (buttons & (1 << bit)) === 0);
        await this.directPadHold(direct, padSlot, held, [lx, ly, rx, ry]);
        return;
    }

    /**
     * Query the controller type installed in a pad slot.
     * Returns the numeric controller type (0=NotConnected, 1=DualShock2, 2=Guitar, etc.).
     */
    public async padGetType(padSlot: number = 0): Promise<number> {
        const direct = this.requireDirect();
        return (await direct.padGet(padSlot)).connected ? 1 : 0;
    }

    /**
     * Get a full CPU register dump from the running emulator
     */
    public async getRegisters(): Promise<RegisterDump> {
        const direct = this.requireDirect();
        return direct.getRegisters();
    }

    /**
     * Write raw bytes to live EE or IOP memory.
     * @param address  Target virtual address
     * @param hexBytes Byte data as a hex string (e.g. "24020005" or "24 02 00 05")
     * @param region   'ee' (default) or 'iop'
     */
    public async writeMemory(address: number, hexBytes: string, region: string = 'ee'): Promise<void> {
        const direct = this.requireDirect();
        const digits = hexBytes.replace(/0x/gi, '').replace(/[^0-9a-f]/gi, '');
        if (digits.length % 2 !== 0) {
            throw new Error(`WriteMemory: odd number of hex digits in "${hexBytes}"`);
        }
        return direct.writeMemory(address, Buffer.from(digits, 'hex'), region);
    }

    /** Writes a file's bytes into Ps2Emulator's memory at `address`. */
    public async loadBinFromFile(filePath: string, address: number, region: string = 'ee'): Promise<void> {
        await this.requireDirect().writeMemory(address >>> 0, fs.readFileSync(filePath), region);
    }

    /**
     * Update a single EE CPU register.
     * @param name   Register type: 'GPR', 'PC', 'HI', 'LO', 'HI1', 'LO1', 'SA', 'COP0', 'FPR'
     * @param index  Register index (0-31 for GPR/COP0/FPR; ignored for PC/HI/LO/…)
     * @param value  New value as a decimal string or "0x"-prefixed hex string
     */
    public async setRegister(name: string, index: number, value: string): Promise<void> {
        const direct = this.requireDirect();
        return direct.setRegister(name, index, value);
    }

    /**
     * Fetch all labels from the server's symbol database.
     */
    public async getLabels(): Promise<LabelEntry[]> {
        const direct = this.getDirect();
        if (direct && !this.connected) {
            // The emulator's symbols: ELF names and labels pushed to it.
            const symbols = await direct.listSymbols();
            return symbols.map(s => ({ address: `0x${hex8(s.address)}`, label: s.name }));
        }
        return this.bridgeGetLabels();
    }

    /** GETLABELS from the Bridge itself. */
    private async bridgeGetLabels(): Promise<LabelEntry[]> {
        const response = await this.sendCommand({ Command: 'GETLABELS' });
        if (!response.Success) {
            throw new Error(response.Error || 'GetLabels failed');
        }
        return response.Labels ?? [];
    }

    /**
     * Add or update a label in the server's symbol database.
     * The server persists the change to ps2MemMap.json immediately.
     */
    public async addLabel(address: number, label: string, comment?: string): Promise<void> {
        const direct = this.getDirect();
        if (direct && !this.connected) {
            // Kept in the emulator only; it is not saved without the Bridge.
            await direct.setSymbols(DIRECT_LABEL_SOURCE, [{ address, name: label }], false);
            return;
        }
        const cmd: CommandMessage = { Command: 'ADDLABEL', Address: address, Label: label };
        if (comment) { cmd.Comment = comment; }
        const response = await this.sendCommand(cmd);
        if (!response.Success) {
            throw new Error(response.Error || 'AddLabel failed');
        }
        void this.syncLabelsToDirect();
    }

    /**
     * Delete a label from the server's symbol database by address.
     */
    public async deleteLabel(address: number): Promise<void> {
        const direct = this.getDirect();
        if (direct && !this.connected) {
            await direct.removeSymbols(DIRECT_LABEL_SOURCE, [address]);
            return;
        }
        const response = await this.sendCommand({ Command: 'DELETELABEL', Address: address });
        if (!response.Success) {
            throw new Error(response.Error || 'DeleteLabel failed');
        }
        void this.syncLabelsToDirect();
    }

    /**
     * Scan binary data for null-terminated ASCII strings (min 4 chars).
     * @param binaryData  Raw binary buffer to scan.
     * @param startAddress Virtual address of the first byte in the buffer.
     */
    public async getStrings(binaryData: Buffer, startAddress: number): Promise<StringEntry[]> {
        const response = await this.sendCommand(
            { Command: 'GETSTRINGS', Address: startAddress, Data: binaryData.toString('base64') },
            60000   // large binary may take a few seconds
        );
        if (!response.Success) {
            throw new Error(response.Error || 'GetStrings failed');
        }
        return response.Strings ?? [];
    }

    /**
     * Scan binary data for MIPS function entry points.
     * Detects ADDIU $sp,$sp,-N prologues and JAL targets.
     * @param binaryData  Raw binary buffer to scan.
     * @param startAddress Virtual address of the first byte in the buffer.
     */
    public async getFunctions(binaryData: Buffer, startAddress: number): Promise<FunctionEntry[]> {
        const response = await this.sendCommand(
            { Command: 'GETFUNCTIONS', Address: startAddress, Data: binaryData.toString('base64') },
            60000
        );
        if (!response.Success) {
            throw new Error(response.Error || 'GetFunctions failed');
        }
        return response.Functions ?? [];
    }

    /**
     * Scan binary data for 4-byte aligned values that look like valid EE memory pointers.
     * @param binaryData  Raw binary buffer to scan.
     * @param startAddress Virtual address of the first byte in the buffer.
     */
    public async getPointers(binaryData: Buffer, startAddress: number): Promise<PointerEntry[]> {
        const response = await this.sendCommand(
            { Command: 'GETPOINTERS', Address: startAddress, Data: binaryData.toString('base64') },
            60000
        );
        if (!response.Success) {
            throw new Error(response.Error || 'GetPointers failed');
        }
        return response.Pointers ?? [];
    }

    /**
     * Scan binary data for plausible 32-bit IEEE-754 float values.
     * @param binaryData  Raw binary buffer to scan.
     * @param startAddress Virtual address of the first byte in the buffer.
     */
    public async getFloats(binaryData: Buffer, startAddress: number): Promise<FloatEntry[]> {
        const response = await this.sendCommand(
            { Command: 'GETFLOATS', Address: startAddress, Data: binaryData.toString('base64') },
            60000
        );
        if (!response.Success) {
            throw new Error(response.Error || 'GetFloats failed');
        }
        return response.Floats ?? [];
    }

    /**
     * Scan binary data for non-zero 4-byte aligned 32-bit signed integers.
     * @param binaryData  Raw binary buffer to scan.
     * @param startAddress Virtual address of the first byte in the buffer.
     */
    public async getIntegers(binaryData: Buffer, startAddress: number): Promise<IntegerEntry[]> {
        const response = await this.sendCommand(
            { Command: 'GETINTEGERS', Address: startAddress, Data: binaryData.toString('base64') },
            60000
        );
        if (!response.Success) {
            throw new Error(response.Error || 'GetIntegers failed');
        }
        return response.Integers ?? [];
    }

    /** Strings in a range of Ps2Emulator's memory, found by the workspace service. */
    public async scanLiveStrings(address: number, length: number, region: string): Promise<StringEntry[]> {
        return this.getStrings(await this.readLive(address, length, region), address);
    }

    /** Functions in a range of Ps2Emulator's memory, found by the workspace service. */
    public async scanLiveFunctions(address: number, length: number, region: string): Promise<FunctionEntry[]> {
        return this.getFunctions(await this.readLive(address, length, region), address);
    }

    /** Pointers in a range of Ps2Emulator's memory, found by the workspace service. */
    public async scanLivePointers(address: number, length: number, region: string): Promise<PointerEntry[]> {
        return this.getPointers(await this.readLive(address, length, region), address);
    }

    /** Floats in a range of Ps2Emulator's memory, found by the workspace service. */
    public async scanLiveFloats(address: number, length: number, region: string): Promise<FloatEntry[]> {
        return this.getFloats(await this.readLive(address, length, region), address);
    }

    /** Integers in a range of Ps2Emulator's memory, found by the workspace service. */
    public async scanLiveIntegers(address: number, length: number, region: string): Promise<IntegerEntry[]> {
        return this.getIntegers(await this.readLive(address, length, region), address);
    }

    /**
     * Scan live memory for addresses whose value matches a comparison against a target value.
     * On first scan (no previousAddresses) the entire [startAddress, endAddress) range is searched.
     * On subsequent scans pass previousAddresses to refine (filter) the existing result set.
     *
     * @param startAddress    Inclusive start of the scan range.
     * @param endAddress      Exclusive end of the scan range.
     * @param value           Value to compare (decimal or 0x-prefixed hex).
     * @param comparison      "eq" | "ne" | "lt" | "gt" | "le" | "ge"
     * @param dataType        "uint8"|"int8"|"uint16"|"int16"|"uint32"|"int32"|"uint64"|"int64"|"float32"|"float64"
     * @param region          "ee" or "iop".
     * @param previousAddresses  Previous result addresses for refinement scans.
     */
    public async scanValue(
        startAddress: number,
        endAddress: number,
        value: string,
        comparison: string,
        dataType: string,
        region: string,
        previousAddresses?: number[]
    ): Promise<{ results: ScanValueResult[]; message?: string }> {
        const command: CommandMessage = {
            Command:       'SCANVALUE',
            Address:       startAddress,
            EndAddress:    endAddress,
            ScanValue:     value,
            Comparison:    comparison,
            DataType:      dataType,
            Region:        region,
            ScanAddresses: previousAddresses,
        };
        const response = await this.sendCommand(command, 300000);
        if (!response.Success) {
            throw new Error(
                response.Error
                    || `ScanValue failed (value="${value}", type=${dataType}, range=0x${startAddress.toString(16)}-0x${endAddress.toString(16)})`
            );
        }
        return { results: response.ScanResults ?? [], message: response.Message ?? undefined };
    }

    public async search(
        address: number,
        length: number,
        query: string,
        mode: string,
        region?: string,
        fileData?: Buffer
    ): Promise<SearchMatch[]> {
        const data = fileData ?? await this.readLive(address, length, region);
        const command: CommandMessage = {
            Command:    'SEARCH',
            Address:    address,
            Length:     data.length,
            Query:      query,
            SearchMode: mode,
            Region:     region,
            Data:       data.toString('base64'),
        };
        const response = await this.sendCommand(command, 120000);
        if (!response.Success) {
            throw new Error(response.Error || 'Search failed');
        }
        return response.Matches ?? [];
    }

    public async scanReferences(
        address: number,
        length: number,
        targetAddress: number,
        kinds: string[],
        region?: string,
        fileData?: Buffer
    ): Promise<ReferenceEntry[]> {
        const data = fileData ?? await this.readLive(address, length, region);
        const command: CommandMessage = {
            Command: 'SCANREFERENCES',
            Address: address,
            Length: data.length,
            TargetAddress: targetAddress,
            ReferenceKinds: kinds,
            Region: region,
            Data: data.toString('base64'),
        };
        const response = await this.sendCommand(command, 120000);
        if (!response.Success) {
            throw new Error(response.Error || 'ScanReferences failed');
        }
        return response.References ?? [];
    }

    /**
     * Build (or rebuild) the bridge's cross-reference index for a region
     * ('ee', 'iop' or 'bios'). Returns once the build has started unless
     * `wait` is set. The message describes the indexes the bridge holds.
     */
    public async buildXrefIndex(region: string, rebuild = false, wait = false): Promise<string> {
        const direct = this.requireDirect();
        const which = region?.toLowerCase().startsWith('iop') ? 'iop' : 'ee';
        const status = (await direct.indexStatus()).find(i => i.region === which);
        if (status?.ready && !rebuild) {
            return `index ready: ${status.refs} references, ${status.functions} functions, ${status.tables} tables`;
        }
        const info = await direct.buildIndex(which, wait);
        return info.ready && wait
            ? `indexed in ${info.buildMillis} ms: ${info.refs} references, ${info.functions} functions, ${info.tables} tables`
            : 'building';
    }

    /**
     * References to `targetAddress` from the cross-reference index (building it
     * first if needed): calls, jumps, branches, built addresses, loads, stores,
     * pointers, and ASCII/CRC32 matches when `kinds` asks for them.
     */
    public async xrefsTo(targetAddress: number, kinds?: string[], region?: string): Promise<ReferenceEntry[]> {
        const direct = this.requireDirect();
        // Like the Bridge, build the index first when there is none.
        if (!(await direct.indexStatus()).some(i => i.ready)) {
            await direct.buildIndex(region?.toLowerCase().startsWith('iop') ? 'iop' : 'ee', true);
        }
        const wanted = directKinds(kinds);
        if (wanted === null) {
            return [];
        }
        const reply = await direct.refsTo(targetAddress, wanted, 65_536);
        return reply.refs.map(r => referenceFromDirect(r, reply.target, targetAddress >>> 0));
    }

    /**
     * Get EE OS thread list from the emulator.
     */
    public async getThreads(): Promise<ThreadEntry[]> {
        const direct = this.requireDirect();
        const threads = await direct.eeThreads();
        return threads.map(t => ({ id: t.id, status: t.state, pc: t.pc, sp: t.sp, priority: t.priority, entryPc: t.entry }));
    }

    /**
     * Get the list of loaded IOP modules.
     */
    public async getModules(): Promise<ModuleEntry[]> {
        const direct = this.requireDirect();
        const hex = (n: number) => `0x${(n >>> 0).toString(16).toUpperCase().padStart(8, '0')}`;
        const modules = await direct.iopModules();
        return modules.map(m => ({
            name: m.name, version: m.version, textAddr: hex(m.text), entry: hex(m.entry), gp: hex(m.gp),
            textSize: m.textSize, dataSize: m.dataSize, bssSize: m.bssSize,
        }));
    }

    /**
     * Ping the server
     */
    public async ping(): Promise<string> {
        const command: CommandMessage = {
            Command: 'PING'
        };

        const response = await this.sendCommand(command);
        
        if (!response.Success) {
            throw new Error(response.Error || 'Ping failed');
        }

        return response.Message || 'PONG';
    }

    /**
     * Apply PS2 GameShark/Action Replay cheat codes to live emulator memory.
     * @param cheatText  Multi-line cheat code text (blocks separated by blank lines).
     */
    public async applyCheats(cheatText: string): Promise<void> {
        const direct = this.requireDirect();
        // Once: a patch taken away after its first field.
        const name = 'Code Designer (applied once)';
        await direct.setPatch(name, cheatText, true);
        for (let i = 0; i < 100; i++) {
            const patch = (await direct.listPatches()).find(p => p.name === name);
            if (!patch || patch.applied > 0) { break; }
            await new Promise(r => setTimeout(r, 20));
        }
        await direct.removePatch(name).catch(() => { /* already gone */ });
        return;
    }

    /**
     * INJECTCHEATENGINE — write the persistent cheat engine and hook it at `hookAddress`
     * so `cheatText` (raw codes) is re-applied every time the hook runs. A previous
     * injection is removed first.
     */
    public async injectCheatEngine(cheatText: string, hookAddress: number, engineBase?: number): Promise<CheatEngineInfo> {
        const direct = this.requireDirect();
        // The emulator applies the codes itself every field: no hook, no engine in RAM.
        return this.directCheatInfo(await direct.setPatch(DIRECT_CHEAT_ENGINE, cheatText, true));
    }

    /** UPDATECHEATENGINE — replace the injected engine's table without re-hooking. */
    public async updateCheatEngine(cheatText: string): Promise<CheatEngineInfo> {
        const direct = this.requireDirect();
        return this.directCheatInfo(await direct.setPatch(DIRECT_CHEAT_ENGINE, cheatText, true));
    }

    /** REMOVECHEATENGINE — restore the hooked instructions. */
    public async removeCheatEngine(): Promise<CheatEngineInfo> {
        const direct = this.requireDirect();
        await direct.removePatch(DIRECT_CHEAT_ENGINE).catch(() => { /* not set */ });
        return this.directCheatInfo(undefined);
    }

    /** CHEATENGINESTATUS — whether the engine is injected, and where. */
    public async cheatEngineStatus(): Promise<CheatEngineInfo> {
        const direct = this.requireDirect();
        const patch = (await direct.listPatches()).find(p => p.name === DIRECT_CHEAT_ENGINE);
        return this.directCheatInfo(patch);
    }

    /**
     * BUILDCHEATENGINE — build the engine image (code + data + table) for export
     * without writing memory. With `hookAddress` and a live backend, the hooked
     * instructions are embedded.
     */
    public async buildCheatEngine(cheatText: string, hookAddress?: number, engineBase?: number): Promise<{ image: Buffer; info: CheatEngineInfo }> {
        // The hooked instructions, from Ps2Emulator when it runs, are embedded in the image.
        const direct = this.getDirect();
        let hookWords: number[] | undefined;
        if (direct && hookAddress !== undefined) {
            const bytes = await direct.readMemory(hookAddress >>> 0, 8, 'ee');
            hookWords = [bytes.readUInt32LE(0), bytes.readUInt32LE(4)];
        }
        const response = await this.sendCommand({
            Command:     'BUILDCHEATENGINE',
            Data:        cheatText,
            HookAddress: hookAddress !== undefined ? hookAddress >>> 0 : undefined,
            EngineBase:  engineBase !== undefined ? engineBase >>> 0 : undefined,
            HookWords:   hookWords,
        } as CommandMessage, 60000);
        if (!response.Success || !response.CheatEngineState || !response.Data) {
            throw new Error(response.Error || 'BuildCheatEngine failed');
        }
        return { image: Buffer.from(response.Data, 'base64'), info: response.CheatEngineState };
    }

    /**
     * GETGAMECHEATS — the cheat archive's cheats for a game by its PS2 serial.
     * Returns every cheat list for the disc and its game, plus the cheats of
     * `cheatListId` (or of the first non-empty list). Empty when the archive has
     * nothing for the serial.
     * @param serial       PS2 disc serial, e.g. "SCUS-97275".
     * @param cheatListId  Which of the game's cheat lists to return.
     */
    public async getGameCheats(serial: string, cheatListId?: number): Promise<{
        cheats: GameCheatInfo[];
        gameTitle: string | null;
        cheatListTitle: string | null;
        cheatListId: number | null;
        gameId: number | null;
        lists: GameCheatListInfo[];
    }> {
        const response = await this.sendCommand({
            Command:     'GETGAMECHEATS',
            GameSerial:  serial,
            CheatListId: cheatListId,
        });
        if (!response.Success) {
            throw new Error(response.Error || 'GETGAMECHEATS failed');
        }
        return {
            cheats:         (response.GameCheats ?? []) as GameCheatInfo[],
            gameTitle:      response.GameTitle      ?? null,
            cheatListTitle: response.CheatListTitle ?? null,
            cheatListId:    response.SelectedCheatListId ?? null,
            gameId:         response.ArchiveGameId ?? null,
            lists:          response.GameCheatLists ?? [],
        };
    }

    /**
     * Convert PS2 cheat codes between raw and a device-specific encrypted format.
     * @param codes      Multi-line cheat code text to convert.
     * @param direction  'toRaw' — decrypt device codes → raw hex pairs;
     *                   'toEngine' — encrypt raw hex pairs → device format.
     * @param format     Cheat engine format name (Raw, ActionReplay1, ActionReplay2,
     *                   ActionReplayMax, GameSharkV3, CodeBreakerV1, CodeBreakerV7).
     *                   Required for 'toEngine'; omit for auto-detect on 'toRaw'.
     * @returns The converted cheat codes as a multi-line string.
     */
    public async convertCheat(codes: string, direction: 'toRaw' | 'toEngine', format?: string): Promise<string> {
        const command: CommandMessage = {
            Command:   'CONVERTCHEAT',
            Data:      codes,
            Direction: direction,
            Format:    format,
        };
        const response = await this.sendCommand(command);
        if (!response.Success) {
            throw new Error(response.Error || 'ConvertCheat failed');
        }
        return response.Data ?? '';
    }

    /**
     * Port a list of raw GameShark cheat codes from a source EE-RAM dump to a target
     * EE-RAM dump by scanning for hex patterns around each cheat address.
     *
     * @param cheatText  Raw GameShark codes (optional #Name headers).
     * @param sourcePath Absolute path to the source (r0001) raw EE-RAM dump file.
     * @param targetPath Absolute path to the target (r0002) raw EE-RAM dump file.
     * @returns Tuple of structured per-code results and a detailed log string.
     */
    public async portCheats(
        cheatText:  string,
        sourcePath: string,
        targetPath: string,
    ): Promise<{ results: PortCheatResult[]; log: string }> {
        const command: CommandMessage = {
            Command:        'PORTCHEATS',
            Data:           cheatText,
            SourceDumpPath: sourcePath,
            TargetDumpPath: targetPath,
        };
        const response = await this.sendCommand(command, 600_000);
        if (!response.Success) {
            throw new Error(response.Error || 'PortCheats failed');
        }
        const raw = response.PortCheatResults as Array<Record<string, unknown>> ?? [];
        const results: PortCheatResult[] = raw.map(r => ({
            blockIndex:   (r['BlockIndex']   as number)  ?? 0,
            lineIndex:    (r['LineIndex']    as number)  ?? 0,
            blockName:    (r['BlockName']    as string)  ?? '',
            blockText:    (r['BlockText']    as string | null) ?? null,
            originalCode: (r['OriginalCode'] as string)  ?? '',
            newCode:      (r['NewCode']      as string | null) ?? null,
            found:        (r['Found']        as boolean) ?? false,
            method:       (r['Method']       as string)  ?? '',
            confidence:   (r['Confidence']   as number)  ?? 0,
            ambiguous:    (r['Ambiguous']    as boolean) ?? false,
            candidates:   (r['Candidates']   as string[] | null) ?? null,
            sourceAddress: (r['SourceAddress'] as number | null) ?? null,
            newAddress:    (r['NewAddress']    as number | null) ?? null,
            note:          (r['Note']          as string | null) ?? null,
        }));
        return { results, log: response.Data ?? '' };
    }

    /**
     * List all ELF and IRX files found on a PS2 disc image.
     * Also identifies which file is the BOOT2 main executable from SYSTEM.CNF.
     *
     * @param isoPath Absolute path to the ISO-9660 disc image.
     */
    public async listDiscElfs(isoPath: string): Promise<{ elfFiles: DiscElfEntry[]; bootElf: string }> {
        const command: CommandMessage = {
            Command: 'LISTDISCELFS',
            IsoPath: isoPath,
        };
        const response = await this.sendCommand(command, 30_000);
        if (!response.Success) {
            throw new Error(response.Error || 'LISTDISCELFS failed');
        }
        return {
            elfFiles: response.ElfFiles ?? [],
            bootElf:  response.BootElf  ?? '',
        };
    }

    /**
     * ISO_GET_INFO — returns the boot-ELF path declared in a disc image's
     * SYSTEM.CNF (e.g. "cdrom0:\\SLUS_216.06;1"), used to derive the serial.
     * Returns '' when no SYSTEM.CNF / BOOT2 entry is present.
     */
    public async isoBootElf(isoPath: string): Promise<string> {
        const response = await this.sendCommand(
            { Command: 'ISO_GET_INFO', IsoPath: isoPath }, 30_000);
        if (!response.Success) { throw new Error(response.Error || 'ISO_GET_INFO failed'); }
        return response.BootElf ?? '';
    }

    /**
     * Extract a named ELF from a PS2 disc image and run the static recompiler on it,
     * generating C++ source files in the specified output directory.
     *
     * @param isoPath         Absolute path to the ISO-9660 disc image.
     * @param elfName         Name of the ELF on the disc (e.g. "SCUS_971.98").
     *                        Pass an empty string to use the BOOT2 executable from SYSTEM.CNF.
     * @param outputDirectory Directory where the generated C++ files will be written.
     */
    public async recompile(
        isoPath: string,
        elfName: string,
        outputDirectory: string,
    ): Promise<RecompileResult> {
        const command: CommandMessage = {
            Command:         'RECOMPILE',
            IsoPath:         isoPath,
            ElfName:         elfName,
            OutputDirectory: outputDirectory,
        };
        // Recompilation can take a while for large game ELFs — allow up to 5 minutes.
        const response = await this.sendCommand(command, 300_000);
        if (!response.Success) {
            throw new Error(response.Error || 'RECOMPILE failed');
        }
        return {
            functionCount: response.FunctionCount ?? 0,
            writtenFiles:  response.WrittenFiles  ?? [],
            bootElf:       response.BootElf       ?? elfName,
        };
    }

    // ── Memory Card Manager ──────────────────────────────────────────────────

    /**
     * List all save-game directories on a PS2 memory card image.
     * @param mcPath Absolute path to the .ps2 memory card file.
     */
    public async mcList(mcPath: string): Promise<McSaveInfo[]> {
        const response = await this.sendCommand({ Command: 'MC_LIST', McPath: mcPath });
        if (!response.Success) {
            throw new Error(response.Error || 'MC_LIST failed');
        }
        return response.McSaves ?? [];
    }

    /**
     * Export a save-game directory from a PS2 memory card image to a host directory.
     * @param mcPath    Absolute path to the .ps2 memory card file.
     * @param saveName  Name of the save directory (e.g. "BASLUS-20386SPEOPS").
     * @param exportDir Absolute host directory where the save folder will be written.
     */
    public async mcExport(mcPath: string, saveName: string, exportDir: string): Promise<string> {
        const response = await this.sendCommand(
            { Command: 'MC_EXPORT', McPath: mcPath, SaveName: saveName, ExportDir: exportDir }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'MC_EXPORT failed');
        }
        return response.Data ?? exportDir;
    }

    /**
     * Import a save-game directory from the host into a PS2 memory card image.
     * @param mcPath    Absolute path to the .ps2 memory card file.
     * @param importDir Absolute path to the save-game folder on the host.
     * @param overwrite Whether to overwrite an existing save with the same name.
     */
    public async mcImport(mcPath: string, importDir: string, overwrite: boolean): Promise<void> {
        const response = await this.sendCommand(
            { Command: 'MC_IMPORT', McPath: mcPath, ImportDir: importDir, McOverwrite: overwrite }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'MC_IMPORT failed');
        }
    }

    /**
     * Delete a save-game directory from a PS2 memory card image.
     * @param mcPath   Absolute path to the .ps2 memory card file.
     * @param saveName Name of the save directory to delete.
     */
    public async mcDelete(mcPath: string, saveName: string): Promise<void> {
        const response = await this.sendCommand(
            { Command: 'MC_DELETE', McPath: mcPath, SaveName: saveName }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'MC_DELETE failed');
        }
    }

    /**
     * Create a new blank 8 MB PS2 memory card image at the given path.
     * @param mcPath Absolute path for the new .ps2 file.
     */
    public async mcFormat(mcPath: string): Promise<void> {
        const response = await this.sendCommand(
            { Command: 'MC_FORMAT', McPath: mcPath }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'MC_FORMAT failed');
        }
    }

    /**
     * Read a raw file from a save-game directory inside a PS2 memory card image.
     * @param mcPath Absolute path to the .ps2 memory card file.
     * @param saveName Name of the save directory.
     * @param fileName Name of the file within that save directory.
     */
    public async mcReadFile(mcPath: string, saveName: string, fileName: string): Promise<string> {
        const response = await this.sendCommand(
            { Command: 'MC_READ_FILE', McPath: mcPath, SaveName: saveName, McFileName: fileName }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'MC_READ_FILE failed');
        }
        return response.Data ?? '';
    }

    // FileFormats / Zipper workbench

    public async zipperInspect(inputPath: string): Promise<ZipperInspectResult> {
        const response = await this.sendCommand({ Command: 'ZIPPER_INSPECT', InputPath: inputPath }, 30_000);
        if (!response.Success) {
            throw new Error(response.Error || 'ZIPPER_INSPECT failed');
        }
        return JSON.parse(response.Data ?? '{}') as ZipperInspectResult;
    }

    public async zipperExtractAll(inputPath: string, outputDirectory: string): Promise<ZipperOperationResult> {
        const response = await this.sendCommand(
            { Command: 'ZIPPER_EXTRACT_ALL', InputPath: inputPath, OutputDirectory: outputDirectory },
            120_000,
        );
        if (!response.Success) {
            throw new Error(response.Error || 'ZIPPER_EXTRACT_ALL failed');
        }
        return JSON.parse(response.Data ?? '{}') as ZipperOperationResult;
    }

    public async zipperExtractEntry(inputPath: string, entryIndex: number, outputPath: string): Promise<ZipperOperationResult> {
        const response = await this.sendCommand(
            { Command: 'ZIPPER_EXTRACT_ENTRY', InputPath: inputPath, ZipperEntryIndex: entryIndex, OutputPath: outputPath },
            60_000,
        );
        if (!response.Success) {
            throw new Error(response.Error || 'ZIPPER_EXTRACT_ENTRY failed');
        }
        return JSON.parse(response.Data ?? '{}') as ZipperOperationResult;
    }

    public async zipperReadNode(inputPath: string, entryIndex: number): Promise<string> {
        const response = await this.sendCommand(
            { Command: 'ZIPPER_READ_NODE', InputPath: inputPath, ZipperEntryIndex: entryIndex },
            60_000,
        );
        if (!response.Success) {
            throw new Error(response.Error || 'ZIPPER_READ_NODE failed');
        }
        return response.Data ?? '';
    }

    public async zipperInspectRdr(inputPath: string, entrySection?: string): Promise<{
        sourcePath: string;
        entry?: string;
        version: number;
        stringPoolSize: number;
        atomCount: number;
        root: unknown;
        fallback?: boolean;
        parseError?: string;
    }> {
        const response = await this.sendCommand(
            { Command: 'ZIPPER_INSPECT_RDR', InputPath: inputPath, ZipperSection: entrySection },
            60_000,
        );
        if (!response.Success) {
            throw new Error(response.Error || 'ZIPPER_INSPECT_RDR failed');
        }
        return JSON.parse(response.Data ?? '{}');
    }

    // Parse an .rdr from base64 bytes already in the webview's memory. Used
    // when an .rdr lives inside a ZAR we've already parsed client-side, so
    // we don't have to round-trip through the backend's ZDB drill-down.
    public async zipperInspectRdrBytes(base64: string, entryName?: string): Promise<{
        sourcePath: string;
        entry?: string;
        version: number;
        stringPoolSize: number;
        atomCount: number;
        root: unknown;
        fallback?: boolean;
        parseError?: string;
    }> {
        const response = await this.sendCommand(
            { Command: 'ZIPPER_INSPECT_RDR_BYTES', Data: base64, ZipperSection: entryName },
            60_000,
        );
        if (!response.Success) {
            throw new Error(response.Error || 'ZIPPER_INSPECT_RDR_BYTES failed');
        }
        return JSON.parse(response.Data ?? '{}');
    }

    // AIMAPS.MPS — returns the parsed AI map manifest. Mirrors the HTML map
    // viewer's parseAiMaps. Displayed as a table by the zipper workbench.
    public async zipperInspectMps(inputPath: string): Promise<{
        sourcePath: string;
        declaredCount: number;
        foundCount: number;
        fileSize: number;
        maps: Array<{
            offset: number;
            name: string;
            width: number;
            height: number;
            minX: number; minY: number; minZ: number;
            maxX: number; maxY: number; maxZ: number;
            scaleX: number; scaleZ: number;
        }>;
    }> {
        const response = await this.sendCommand(
            { Command: 'ZIPPER_INSPECT_MPS', InputPath: inputPath },
            60_000,
        );
        if (!response.Success) {
            throw new Error(response.Error || 'ZIPPER_INSPECT_MPS failed');
        }
        return JSON.parse(response.Data ?? '{}');
    }


    public async zipperPreloadMapPackage(inputPath: string): Promise<{ preloadToken: string; resourceCount: number; totalBytes: number; cacheHit: boolean; }> {
        const response = await this.sendCommand(
            { Command: 'ZIPPER_PRELOAD_MAP_PACKAGE', InputPath: inputPath },
            120_000,
        );
        if (!response.Success) {
            throw new Error(response.Error || 'ZIPPER_PRELOAD_MAP_PACKAGE failed');
        }
        return JSON.parse(response.Data ?? '{}') as { preloadToken: string; resourceCount: number; totalBytes: number; cacheHit: boolean; };
    }

    public async zipperGetPreloadedResource(preloadToken: string, query: string): Promise<{ key: string; source: string; bytes: number; data: string; }> {
        const response = await this.sendCommand(
            { Command: 'ZIPPER_GET_PRELOADED_RESOURCE', ZipperPreloadToken: preloadToken, Query: query },
            60_000,
        );
        if (!response.Success) {
            throw new Error(response.Error || 'ZIPPER_GET_PRELOADED_RESOURCE failed');
        }
        return JSON.parse(response.Data ?? '{}') as { key: string; source: string; bytes: number; data: string; };
    }

    public async zipperReleasePreload(preloadToken: string): Promise<ZipperOperationResult> {
        const response = await this.sendCommand(
            { Command: 'ZIPPER_RELEASE_PRELOAD', ZipperPreloadToken: preloadToken },
            30_000,
        );
        if (!response.Success) {
            throw new Error(response.Error || 'ZIPPER_RELEASE_PRELOAD failed');
        }
        return JSON.parse(response.Data ?? '{}') as ZipperOperationResult;
    }

    /**
     * Runs one ASSET_* command (asset sessions: the editing surface shared by the editor webviews and the
     * assets_* MCP tools) and returns its parsed Data. Other commands are refused so an editor webview can't
     * reach the rest of the bridge through this.
     */
    public async assetCommand(command: AssetCommand, timeoutMs: number = 60_000): Promise<unknown> {
        if (!/^ASSET_[A-Z_]+$/.test(command.Command)) {
            throw new Error(`Not an asset command: ${command.Command}`);
        }
        const response = await this.sendCommand({
            Command: command.Command,
            InputPath: command.InputPath,
            AssetSession: command.AssetSession,
            AssetArchive: command.AssetArchive,
            AssetName: command.AssetName,
            AssetJson: command.AssetJson,
        }, timeoutMs);
        if (!response.Success) {
            throw new Error(response.Error || `${command.Command} failed`);
        }
        return JSON.parse(response.Data ?? 'null');
    }

    public async zipperReadAllNodes(inputPath: string): Promise<Array<{ index: number; name: string; type: number; data: string }>> {
        const response = await this.sendCommand(
            { Command: 'ZIPPER_READ_ALL_NODES', InputPath: inputPath },
            60_000,
        );
        if (!response.Success) {
            throw new Error(response.Error || 'ZIPPER_READ_ALL_NODES failed');
        }
        return JSON.parse(response.Data ?? '[]') as Array<{ index: number; name: string; type: number; data: string }>;
    }

    public async zipperSaveExportDecrypted(inputPath: string, outputPath: string): Promise<ZipperOperationResult> {
        const response = await this.sendCommand(
            { Command: 'ZIPPER_SAVE_EXPORT_DECRYPTED', InputPath: inputPath, OutputPath: outputPath },
            60_000,
        );
        if (!response.Success) {
            throw new Error(response.Error || 'ZIPPER_SAVE_EXPORT_DECRYPTED failed');
        }
        return JSON.parse(response.Data ?? '{}') as ZipperOperationResult;
    }

    public async zipperSaveCraft(
        inputPath: string,
        outputDirectory: string,
        mode: 'active' | 'overflow' | 'extreme' | 'all',
        title?: string,
        titleLength?: number,
    ): Promise<ZipperOperationResult> {
        const response = await this.sendCommand(
            {
                Command: 'ZIPPER_SAVE_CRAFT',
                InputPath: inputPath,
                OutputDirectory: outputDirectory,
                SaveMode: mode,
                SaveTitle: title,
                SaveTitleLength: titleLength,
            },
            60_000,
        );
        if (!response.Success) {
            throw new Error(response.Error || 'ZIPPER_SAVE_CRAFT failed');
        }
        return JSON.parse(response.Data ?? '{}') as ZipperOperationResult;
    }

    public async apacheDecryptSection(
        inputPath: string,
        outputDirectory: string,
        section: 'ftscore' | 'zsealetc',
    ): Promise<ZipperOperationResult> {
        const response = await this.sendCommand(
            { Command: 'APACHE_DECRYPT_SECTION', InputPath: inputPath, OutputDirectory: outputDirectory, ZipperSection: section },
            120_000,
        );
        if (!response.Success) {
            throw new Error(response.Error || 'APACHE_DECRYPT_SECTION failed');
        }
        return JSON.parse(response.Data ?? '{}') as ZipperOperationResult;
    }

    public async apacheBuildPatch(
        originalZdbPath: string,
        ftScorePlaintextPath: string,
        outputDirectory: string,
        signingKeyPath?: string,
    ): Promise<ZipperOperationResult> {
        const response = await this.sendCommand(
            {
                Command: 'APACHE_BUILD_PATCH',
                InputPath: originalZdbPath,
                FilePath: ftScorePlaintextPath,
                OutputDirectory: outputDirectory,
                SigningKeyPath: signingKeyPath,
            },
            300_000,
        );
        if (!response.Success) {
            throw new Error(response.Error || 'APACHE_BUILD_PATCH failed');
        }
        return JSON.parse(response.Data ?? '{}') as ZipperOperationResult;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HDD Image Manager
    // ═══════════════════════════════════════════════════════════════════════

    /**
     * List all partitions on a PS2 HDD raw image.
     * @param hddPath Absolute path to the HDD.raw file.
     */
    public async hddList(hddPath: string): Promise<HddPartitionInfo[]> {
        const response = await this.sendCommand({ Command: 'HDD_LIST', HddPath: hddPath });
        if (!response.Success) {
            throw new Error(response.Error || 'HDD_LIST failed');
        }
        return response.HddPartitions ?? [];
    }

    /**
     * List files within a specific partition on a PS2 HDD image.
     * @param hddPath     Absolute path to the HDD.raw file.
     * @param partitionId Partition ID (e.g. "__common", "__system").
     */
    public async hddListFiles(hddPath: string, partitionId: string): Promise<HddFileEntryInfo[]> {
        const response = await this.sendCommand(
            { Command: 'HDD_LIST_FILES', HddPath: hddPath, PartitionId: partitionId }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'HDD_LIST_FILES failed');
        }
        return response.HddFiles ?? [];
    }

    /**
     * Read a file from an HDD partition and return base64 bytes.
     * @param hddPath     Absolute path to the HDD.raw file.
     * @param partitionId Partition ID.
     * @param fileName    Root-level file name to read.
     */
    public async hddReadFile(hddPath: string, partitionId: string, fileName: string, source?: string): Promise<string> {
        const response = await this.sendCommand(
            { Command: 'HDD_READ_FILE', HddPath: hddPath, PartitionId: partitionId, FileName: fileName, HddFileSource: source }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'HDD_READ_FILE failed');
        }
        return response.Data ?? '';
    }

    /**
     * Export a file from an HDD partition to the host filesystem.
     * @param hddPath     Absolute path to the HDD.raw file.
     * @param partitionId Partition ID.
     * @param fileName    Name of the file to export.
     * @param exportDir   Destination directory on the host.
     */
    public async hddExport(hddPath: string, partitionId: string, fileName: string, exportDir: string, source?: string): Promise<string> {
        const response = await this.sendCommand(
            { Command: 'HDD_EXPORT', HddPath: hddPath, PartitionId: partitionId, FileName: fileName, ExportDir: exportDir, HddFileSource: source }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'HDD_EXPORT failed');
        }
        return response.Data ?? exportDir;
    }

    /**
     * Import a file from the host into an HDD partition.
     * @param hddPath     Absolute path to the HDD.raw file.
     * @param partitionId Partition ID.
     * @param importDir   Path to the file/directory to import.
     * @param overwrite   Whether to overwrite existing files.
     */
    public async hddImport(hddPath: string, partitionId: string, importDir: string, overwrite: boolean): Promise<void> {
        const response = await this.sendCommand(
            { Command: 'HDD_IMPORT', HddPath: hddPath, PartitionId: partitionId, ImportDir: importDir, McOverwrite: overwrite }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'HDD_IMPORT failed');
        }
    }

    /**
     * Delete a partition from an HDD image.
     * @param hddPath     Absolute path to the HDD.raw file.
     * @param partitionId Partition ID to delete.
     */
    public async hddDelete(hddPath: string, partitionId: string): Promise<void> {
        const response = await this.sendCommand(
            { Command: 'HDD_DELETE', HddPath: hddPath, PartitionId: partitionId }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'HDD_DELETE failed');
        }
    }

    /**
     * Create a new blank PS2 HDD raw image.
     * @param hddPath Absolute path for the new HDD.raw file.
     * @param sizeGb  Size in GB (default 40).
     */
    public async hddCreate(hddPath: string, sizeGb: number = 40): Promise<void> {
        const response = await this.sendCommand(
            { Command: 'HDD_CREATE', HddPath: hddPath, HddSizeGb: sizeGb }
        );
        if (!response.Success) {
            throw new Error(response.Error || 'HDD_CREATE failed');
        }
    }

    /**
     * Fetch all struct definitions from the server.
     */
    public async getStructs(): Promise<StructEntry[]> {
        const response = await this.sendCommand({ Command: 'GETSTRUCTS' });
        if (!response.Success) {
            throw new Error(response.Error || 'GETSTRUCTS failed');
        }
        return response.Structs ?? [];
    }

    /**
     * Add or update a struct definition (name + base address).
     * @param name        Unique struct name.
     * @param baseAddress Base address as a hex string (e.g. "0x80200000").
     */
    public async addStruct(name: string, baseAddress: string): Promise<void> {
        const response = await this.sendCommand({
            Command: 'ADDSTRUCT',
            StructName: name,
            StructBaseAddress: baseAddress,
        });
        if (!response.Success) {
            throw new Error(response.Error || 'ADDSTRUCT failed');
        }
    }

    /**
     * Delete a struct definition by name.
     */
    public async deleteStruct(name: string): Promise<void> {
        const response = await this.sendCommand({ Command: 'DELETESTRUCT', StructName: name });
        if (!response.Success) {
            throw new Error(response.Error || 'DELETESTRUCT failed');
        }
    }

    /**
     * Add or update a member field in an existing struct.
     * @param structName   Name of the parent struct.
     * @param offset       Byte offset of the member within the struct.
     * @param memberName   Human-readable name for the member.
     */
    public async addStructMember(structName: string, offset: number, memberName: string): Promise<void> {
        const response = await this.sendCommand({
            Command: 'ADDSTRUCTMEMBER',
            StructName: structName,
            MemberOffset: offset,
            MemberName: memberName,
        });
        if (!response.Success) {
            throw new Error(response.Error || 'ADDSTRUCTMEMBER failed');
        }
    }

    // ── Navigation history ────────────────────────────────────────────────────

    /**
     * Record a navigation event in the server-side history.
     * Called by the VS Code extension whenever the user navigates in the disasm editor.
     * @param address  EE virtual address navigated to.
     * @param source   'user' for VS Code user action, 'mcp' for AI-driven navigation.
     */
    public async recordNav(address: number, source: 'user' | 'mcp' = 'user'): Promise<void> {
        try {
            await this.sendCommand({ Command: 'RECORDNAV', Address: address, NavSource: source });
        } catch { /* non-fatal */ }
    }

    /**
     * Fetch the server-side navigation history.
     */
    public async getNavHistory(): Promise<NavHistoryEntry[]> {
        const response = await this.sendCommand({ Command: 'GETNAVHISTORY' });
        if (!response.Success) {
            throw new Error(response.Error || 'GETNAVHISTORY failed');
        }
        return (response as any).NavHistory ?? [];
    }

    /**
     * Clear the server-side navigation history.
     */
    public async clearNavHistory(): Promise<void> {
        const response = await this.sendCommand({ Command: 'CLEARNAVHISTORY' });
        if (!response.Success) {
            throw new Error(response.Error || 'CLEARNAVHISTORY failed');
        }
    }

    /**
     * Tell the VS Code disasm editor to navigate to the given address.
     * The server pushes an ai_activity notification; Follow AI mode in the
     * VS Code extension automatically scrolls the disasm view.
     */
    public async navigateTo(address: number): Promise<void> {
        const response = await this.sendCommand({ Command: 'NAVIGATETO', Address: address });
        if (!response.Success) {
            throw new Error(response.Error || 'NAVIGATETO failed');
        }
    }

    /**
     * Read historical 4-byte values at `address` across all snapshots in `snapshotDir`.
     * @param address      EE virtual address to look up.
     * @param snapshotDir  Root directory to scan for snapshot-info.json files.
     */
    public async getAddressSnapshotHistory(
        address: number,
        snapshotDir: string,
    ): Promise<AddressHistoryEntry[]> {
        const response = await this.sendCommand({
            Command: 'GETADDRESSHISTORY',
            Address: address,
            SnapshotDir: snapshotDir,
        });
        if (!response.Success) {
            throw new Error(response.Error || 'GETADDRESSHISTORY failed');
        }
        return (response as any).AddressHistory ?? [];
    }

    // ── Label Manager ─────────────────────────────────────────────────────────

    /** List all label set names for a game (always includes "Built-in"). */
    public async getLabelSets(workspaceDir: string, gameId: string): Promise<string[]> {
        const response = await this.sendCommand({ Command: 'GETLABELSETS', WorkspaceDir: workspaceDir, GameId: gameId });
        if (!response.Success) { throw new Error(response.Error || 'GETLABELSETS failed'); }
        return (response as any).LabelSets ?? [];
    }

    /** List all label set summaries (name + isBuiltIn + isSystem) for a game. */
    public async getLabelSetSummaries(workspaceDir: string, gameId: string)
        : Promise<{ setName: string; isBuiltIn: boolean; isSystem: boolean }[]> {
        const response = await this.sendCommand({ Command: 'GETLABELSETS', WorkspaceDir: workspaceDir, GameId: gameId });
        if (!response.Success) { throw new Error(response.Error || 'GETLABELSETS failed'); }
        return (response as any).LabelSetSummaries ?? [];
    }

    /** Return the full contents of a label set. Pass "Built-in" for the built-in set. */
    public async getLabelSet(workspaceDir: string, gameId: string, setName: string): Promise<LabelSetPayload> {
        const response = await this.sendCommand({ Command: 'GETLABELSET', WorkspaceDir: workspaceDir, GameId: gameId, SetName: setName });
        if (!response.Success) { throw new Error(response.Error || 'GETLABELSET failed'); }
        return (response as any).LabelSet as LabelSetPayload;
    }

    /** Make a label set active; server reloads _labels/_structs from merged result. */
    public async setActiveLabelSet(workspaceDir: string, gameId: string, setName: string): Promise<LabelSetPayload> {
        const response = await this.sendCommand({ Command: 'SETACTIVELABELSET', WorkspaceDir: workspaceDir, GameId: gameId, SetName: setName });
        if (!response.Success) { throw new Error(response.Error || 'SETACTIVELABELSET failed'); }
        return (response as any).LabelSet as LabelSetPayload;
    }

    /** Create a new empty label set. */
    public async createLabelSet(workspaceDir: string, gameId: string, setName: string): Promise<LabelSetPayload> {
        const response = await this.sendCommand({ Command: 'CREATELABELSET', WorkspaceDir: workspaceDir, GameId: gameId, SetName: setName });
        if (!response.Success) { throw new Error(response.Error || 'CREATELABELSET failed'); }
        return (response as any).LabelSet as LabelSetPayload;
    }

    /** Delete a label set from disk. */
    public async deleteLabelSet(workspaceDir: string, gameId: string, setName: string): Promise<void> {
        const response = await this.sendCommand({ Command: 'DELETELABELSET', WorkspaceDir: workspaceDir, GameId: gameId, SetName: setName });
        if (!response.Success) { throw new Error(response.Error || 'DELETELABELSET failed'); }
    }

    /** Add or update a label entry in the given set. */
    public async addLabelEntry(workspaceDir: string, gameId: string, setName: string, entry: LabelManagerEntry): Promise<LabelSetPayload> {
        const response = await this.sendCommand({ Command: 'ADDLABELENTRY', WorkspaceDir: workspaceDir, GameId: gameId, SetName: setName, LabelEntry: entry as any });
        if (!response.Success) { throw new Error(response.Error || 'ADDLABELENTRY failed'); }
        return (response as any).LabelSet as LabelSetPayload;
    }

    /** Delete a label entry by address from the given set. Accepts both real addresses ("0x80010000") and synthetic child addresses ("0x80010000:+0x0010"). */
    public async deleteLabelEntry(workspaceDir: string, gameId: string, setName: string, address: string): Promise<LabelSetPayload> {
        const response = await this.sendCommand({ Command: 'DELETELABELENTRY', WorkspaceDir: workspaceDir, GameId: gameId, SetName: setName, LabelEntryAddress: address });
        if (!response.Success) { throw new Error(response.Error || 'DELETELABELENTRY failed'); }
        return (response as any).LabelSet as LabelSetPayload;
    }

    /** Import a label set from a JSON string. Returns the imported set. */
    public async importLabelSet(workspaceDir: string, gameId: string, setName: string, json: string): Promise<LabelSetPayload> {
        const response = await this.sendCommand({ Command: 'IMPORTLABELSET', WorkspaceDir: workspaceDir, GameId: gameId, SetName: setName, Data: json });
        if (!response.Success) { throw new Error(response.Error || 'IMPORTLABELSET failed'); }
        return (response as any).LabelSet as LabelSetPayload;
    }

    /** Export a label set; the server returns the JSON string in Data. */
    public async exportLabelSet(workspaceDir: string, gameId: string, setName: string): Promise<string> {
        const response = await this.sendCommand({ Command: 'EXPORTLABELSET', WorkspaceDir: workspaceDir, GameId: gameId, SetName: setName });
        if (!response.Success) { throw new Error(response.Error || 'EXPORTLABELSET failed'); }
        return (response as any).Data ?? '';
    }

    /** Rename a label set. Returns the updated set payload with the new name. */
    public async renameLabelSet(workspaceDir: string, gameId: string, oldName: string, newName: string): Promise<LabelSetPayload> {
        const response = await this.sendCommand({ Command: 'RENAMELABELSET', WorkspaceDir: workspaceDir, GameId: gameId, SetName: oldName, NewSetName: newName });
        if (!response.Success) { throw new Error(response.Error || 'RENAMELABELSET failed'); }
        return (response as any).LabelSet as LabelSetPayload;
    }

    // ── Structure Visualizer ─────────────────────────────────────────────────

    /** List all label groups ("objectmap") and their members for a label set. */
    public async getLabelGroups(gameId: string, setName: string): Promise<LabelGroupInfo[]> {
        const response = await this.sendCommand({ Command: 'GETLABELGROUPS', Data: JSON.stringify({ gameId, setName }) });
        if (!response.Success) { throw new Error(response.Error || 'GETLABELGROUPS failed'); }
        return JSON.parse((response as any).Data ?? '{}').groups ?? [];
    }

    /** Create or update a label group. */
    public async addLabelGroup(gameId: string, setName: string, name: string, kind: string, comment?: string, color?: string): Promise<void> {
        const response = await this.sendCommand({ Command: 'ADDLABELGROUP', Data: JSON.stringify({ gameId, setName, name, kind, comment, color }) });
        if (!response.Success) { throw new Error(response.Error || 'ADDLABELGROUP failed'); }
    }

    /** Delete a label group (the member labels themselves are kept). */
    public async deleteLabelGroup(gameId: string, setName: string, groupName: string): Promise<void> {
        const response = await this.sendCommand({ Command: 'DELETELABELGROUP', Data: JSON.stringify({ gameId, setName, groupName }) });
        if (!response.Success) { throw new Error(response.Error || 'DELETELABELGROUP failed'); }
    }

    /** Add a label (by address) to a label group. */
    public async addLabelGroupMember(gameId: string, setName: string, groupName: string, labelAddress: string, role?: string, sortOrder?: number): Promise<void> {
        const response = await this.sendCommand({ Command: 'ADDLABELGROUPMEMBER', Data: JSON.stringify({ gameId, setName, groupName, labelAddress, role, sortOrder }) });
        if (!response.Success) { throw new Error(response.Error || 'ADDLABELGROUPMEMBER failed'); }
    }

    /** Remove a label (by address) from a label group. */
    public async deleteLabelGroupMember(gameId: string, setName: string, groupName: string, labelAddress: string): Promise<void> {
        const response = await this.sendCommand({ Command: 'DELETELABELGROUPMEMBER', Data: JSON.stringify({ gameId, setName, groupName, labelAddress }) });
        if (!response.Success) { throw new Error(response.Error || 'DELETELABELGROUPMEMBER failed'); }
    }

    /** Scans a code range of Ps2Emulator's memory for calls into the workspace's call graph. */
    public async scanCallGraph(gameId: string, address: string, length: number, region?: string): Promise<{ totalEdges: number; directEdges: number; indirectEdges: number }> {
        const bytes = await this.readLive(parseInt(address, 16) >>> 0, length, region ?? 'ee');
        const response = await this.sendCommand({ Command: 'SCANCALLGRAPH', Data: JSON.stringify({ gameId, address, length, region, bytes: bytes.toString('base64') }) });
        if (!response.Success) { throw new Error(response.Error || 'SCANCALLGRAPH failed'); }
        return JSON.parse((response as any).Data ?? '{}');
    }

    /** Return the call-graph subgraph around a focus address. */
    public async getCallGraph(gameId: string, address: string, depth: number, direction: string, setName?: string): Promise<CallGraphData> {
        const response = await this.sendCommand({ Command: 'GETCALLGRAPH', Data: JSON.stringify({ gameId, address, depth, direction, setName }) });
        if (!response.Success) { throw new Error(response.Error || 'GETCALLGRAPH failed'); }
        return JSON.parse((response as any).Data ?? '{}');
    }

    // ── MCP Config (tool descriptions) ───────────────────────────────────────

    /** Return all MCP config entries (tool descriptions). */
    public async mcpConfigList(): Promise<McpConfigEntry[]> {
        const response = await this.sendCommand({ Command: 'MCPCONFIG_LIST' });
        if (!response.Success) { throw new Error(response.Error || 'MCPCONFIG_LIST failed'); }
        return (response as any).McpConfigs ?? [];
    }

    /** Update the value (tool description) for the given config key. */
    public async mcpConfigUpdate(key: string, value: string): Promise<McpConfigEntry> {
        const response = await this.sendCommand({ Command: 'MCPCONFIG_UPDATE', McpConfigKey: key, McpConfigValue: value });
        if (!response.Success) { throw new Error(response.Error || 'MCPCONFIG_UPDATE failed'); }
        return (response as any).McpConfig as McpConfigEntry;
    }

    /**
     * UPDATEGAMEDISC — upsert an IsoPath and/or CRC for a GameDisc row
     * identified by its serial (e.g. "SCUS-97275").
     * Creates the Game, GameRegion, and GameDisc rows automatically when missing.
     * Both isoPath, crc, and gameTitle are optional; only non-null values are used.
     * Returns the resolved GameDisc.Id so callers can use it as the
     * stable label-manager gameId (insulated from serial / IsoPath churn).
     */
    public async updateGameDisc(serial: string, isoPath?: string, crc?: string, gameTitle?: string): Promise<number | undefined> {
        const response = await this.sendCommand({
            Command:    'UPDATEGAMEDISC',
            GameSerial: serial,
            IsoPath:    isoPath,
            GameCrc:    crc,
            GameTitle:  gameTitle,
        });
        if (!response.Success || !response.Data) { return undefined; }
        try {
            const parsed = JSON.parse(response.Data) as { GameDiscId?: number };
            return parsed.GameDiscId;
        } catch {
            return undefined;
        }
    }

    // ── DB-backed settings ───────────────────────────────────────────────────

    /**
     * GETSETTING — retrieves a single application setting from the database.
     * Returns `null` when the key is not present.
     */
    public async getSetting(key: string): Promise<string | null> {
        if (key === HINT_LEVEL_SETTING && this.direct && !this.connected) {
            return this.directHintLevel;
        }
        const response = await this.sendCommand({ Command: 'GETSETTING', SettingKey: key });
        if (!response.Success) { throw new Error(response.Error || 'GETSETTING failed'); }
        return response.Message ?? null;
    }

    /**
     * SETSETTING — inserts or updates an application setting in the database.
     */
    public async setSetting(key: string, value: string): Promise<void> {
        if (key === HINT_LEVEL_SETTING && isHintLevel(value)) {
            // The emulator's hints follow it too.
            this.directHintLevel = value;
            if (this.direct && !this.connected) {
                return;
            }
        }
        const response = await this.sendCommand({ Command: 'SETSETTING', SettingKey: key, SettingValue: value });
        if (!response.Success) { throw new Error(response.Error || 'SETSETTING failed'); }
    }

    /**
     * GETALLSETTINGS — returns all settings (optionally filtered by key prefix)
     * as a `Record<string, string>` map.
     *
     * @param prefix  Optional key prefix to filter by (e.g. `"emu."` or `"ui."`).
     */
    public async getAllSettings(prefix?: string): Promise<Record<string, string>> {
        const response = await this.sendCommand({ Command: 'GETALLSETTINGS', SettingPrefix: prefix });
        if (!response.Success) { throw new Error(response.Error || 'GETALLSETTINGS failed'); }
        try { return JSON.parse((response as any).Data ?? '{}') as Record<string, string>; }
        catch { return {}; }
    }

    /**
     * DELETESETTING — removes a setting from the database.
     */
    public async deleteSetting(key: string): Promise<void> {
        const response = await this.sendCommand({ Command: 'DELETESETTING', SettingKey: key });
        if (!response.Success) { throw new Error(response.Error || 'DELETESETTING failed'); }
    }

    // ── DB-backed snapshots ──────────────────────────────────────────────────

    /**
     * GETSNAPSHOTS — returns snapshot metadata records from the database for
     * the game identified by `isoName`.
     */
    public async getSnapshots(isoName: string): Promise<SnapshotInfo[]> {
        const response = await this.sendCommand({ Command: 'GETSNAPSHOTS', IsoName: isoName });
        if (!response.Success) { throw new Error(response.Error || 'GETSNAPSHOTS failed'); }
        return (response as any).Snapshots ?? [];
    }

    // ── MCP Memory Manager ────────────────────────────────────────────────────

    /**
     * MCPMEMORY_LIST — returns all MCP memory entries, optionally filtered by gameId.
     */
    public async mcpMemoryList(gameId?: string): Promise<McpMemoryInfo[]> {
        const response = await this.sendCommand({ Command: 'MCPMEMORY_LIST', GameId: gameId });
        if (!response.Success) { throw new Error(response.Error || 'MCPMEMORY_LIST failed'); }
        return response.McpMemories ?? [];
    }

    /**
     * MCPMEMORY_LIST_FOR_AGENT — returns all memory entries scoped to the given agent.
     */
    public async mcpMemoryListForAgent(agentId: number): Promise<McpMemoryInfo[]> {
        const response = await this.sendCommand({ Command: 'MCPMEMORY_LIST_FOR_AGENT', McpMemoryAgentId: agentId });
        if (!response.Success) { throw new Error(response.Error || 'MCPMEMORY_LIST_FOR_AGENT failed'); }
        return response.McpMemories ?? [];
    }

    /**
     * MCPMEMORY_LIST_SHARED — returns all shared memory entries (not scoped to any agent).
     */
    public async mcpMemoryListShared(): Promise<McpMemoryInfo[]> {
        const response = await this.sendCommand({ Command: 'MCPMEMORY_LIST_SHARED' });
        if (!response.Success) { throw new Error(response.Error || 'MCPMEMORY_LIST_SHARED failed'); }
        return response.McpMemories ?? [];
    }

    /**
     * MCPMEMORY_GET — returns a single MCP memory entry by id.
     */
    public async mcpMemoryGet(id: number): Promise<McpMemoryInfo> {
        const response = await this.sendCommand({ Command: 'MCPMEMORY_GET', McpMemoryId: id });
        if (!response.Success) { throw new Error(response.Error || 'MCPMEMORY_GET failed'); }
        return response.McpMemory!;
    }

    /**
     * MCPMEMORY_ADD — creates a new MCP memory entry.
     * @param agentId When provided the entry is scoped to that agent; omit for a shared entry.
     */
    public async mcpMemoryAdd(name: string, content: string, gameId?: string, agentId?: number): Promise<McpMemoryInfo> {
        const response = await this.sendCommand({ Command: 'MCPMEMORY_ADD', McpMemoryName: name, McpMemoryContent: content, GameId: gameId, McpMemoryAgentId: agentId });
        if (!response.Success) { throw new Error(response.Error || 'MCPMEMORY_ADD failed'); }
        return response.McpMemory!;
    }

    /**
     * MCPMEMORY_UPDATE — updates name and/or content of an existing MCP memory entry.
     * Pass undefined to leave a field unchanged.
     * @param agentId Pass 0 to clear the agent association (make shared); omit to leave unchanged.
     */
    public async mcpMemoryUpdate(id: number, name?: string, content?: string, gameId?: string, agentId?: number): Promise<McpMemoryInfo> {
        const response = await this.sendCommand({ Command: 'MCPMEMORY_UPDATE', McpMemoryId: id, McpMemoryName: name, McpMemoryContent: content, GameId: gameId, McpMemoryAgentId: agentId });
        if (!response.Success) { throw new Error(response.Error || 'MCPMEMORY_UPDATE failed'); }
        return response.McpMemory!;
    }

    /**
     * MCPMEMORY_DELETE — deletes a MCP memory entry by id.
     */
    public async mcpMemoryDelete(id: number): Promise<void> {
        const response = await this.sendCommand({ Command: 'MCPMEMORY_DELETE', McpMemoryId: id });
        if (!response.Success) { throw new Error(response.Error || 'MCPMEMORY_DELETE failed'); }
    }

    /**
     * MCPMEMORY_IMPORT — imports a Markdown file from disk into the MCP memory database.
     * The filename (without extension) becomes the entry name unless the file
     * contains a YAML front-matter `name:` key.
     */
    public async mcpMemoryImport(filePath: string): Promise<McpMemoryInfo> {
        const response = await this.sendCommand({ Command: 'MCPMEMORY_IMPORT', FilePath: filePath });
        if (!response.Success) { throw new Error(response.Error || 'MCPMEMORY_IMPORT failed'); }
        return response.McpMemory!;
    }

    /**
     * MCPMEMORY_EXPORT — exports a MCP memory entry to a Markdown file.
     */
    public async mcpMemoryExport(id: number, outputPath: string): Promise<void> {
        const response = await this.sendCommand({ Command: 'MCPMEMORY_EXPORT', McpMemoryId: id, OutputPath: outputPath });
        if (!response.Success) { throw new Error(response.Error || 'MCPMEMORY_EXPORT failed'); }
    }

    // ── PCSX2 Compatibility List ─────────────────────────────────────────────

    /**
     * COMPAT_LIST — returns every game disc that carries a PCSX2 compatibility
     * status. Returns an empty array when the database has not been synced yet.
     */
    public async compatList(): Promise<CompatGameInfo[]> {
        const response = await this.sendCommand({ Command: 'COMPAT_LIST' }, 30000);
        if (!response.Success) { throw new Error(response.Error || 'COMPAT_LIST failed'); }
        return response.CompatGames ?? [];
    }

    /**
     * COMPAT_SYNC — downloads PCSX2's published compatibility list and upserts
     * the local database. Returns the human-readable summary message.
     */
    public async compatSync(): Promise<string> {
        const response = await this.sendCommand({ Command: 'COMPAT_SYNC' }, 180000);
        if (!response.Success) { throw new Error(response.Error || 'COMPAT_SYNC failed'); }
        return response.Message ?? 'Compatibility list synced.';
    }

    /**
     * COMPAT_COVER — returns a game's cover art as a base64 string, downloading
     * and caching it on first request. Returns null when no cover is available.
     */
    public async compatGetCover(serial: string): Promise<string | null> {
        const response = await this.sendCommand({ Command: 'COMPAT_COVER', GameSerial: serial }, 30000);
        if (!response.Success) { throw new Error(response.Error || 'COMPAT_COVER failed'); }
        return response.Data ?? null;
    }

    /**
     * COMPAT_DETAILS — returns the compatibility entry for a single game looked
     * up by disc serial (preferred) or title. Null when the game is not in the
     * local compatibility database.
     */
    public async compatDetails(opts: { serial?: string; title?: string }): Promise<CompatGameInfo | null> {
        const response = await this.sendCommand({
            Command: 'COMPAT_DETAILS', GameSerial: opts.serial, GameTitle: opts.title,
        });
        if (!response.Success) { throw new Error(response.Error || 'COMPAT_DETAILS failed'); }
        return response.CompatGame ?? null;
    }
    // ── Cheats Editor ────────────────────────────────────────────────────────

    /** CHEATEDITOR_GAMES — returns every game that owns at least one cheat list. */
    public async cheatEditorGames(): Promise<CheatGameInfo[]> {
        const response = await this.sendCommand({ Command: 'CHEATEDITOR_GAMES' }, 30000);
        if (!response.Success) { throw new Error(response.Error || 'CHEATEDITOR_GAMES failed'); }
        return response.CheatEditorGames ?? [];
    }

    /** CHEATEDITOR_LISTS — returns the cheat lists ("builds") for a game. */
    public async cheatEditorLists(gameId: number): Promise<CheatListInfo[]> {
        const response = await this.sendCommand({ Command: 'CHEATEDITOR_LISTS', CheatGameId: gameId });
        if (!response.Success) { throw new Error(response.Error || 'CHEATEDITOR_LISTS failed'); }
        return response.CheatEditorLists ?? [];
    }

    /** CHEATEDITOR_GET — returns a cheat list serialized as editable text. */
    public async cheatEditorGet(cheatListId: number): Promise<string> {
        const response = await this.sendCommand({ Command: 'CHEATEDITOR_GET', CheatListId: cheatListId });
        if (!response.Success) { throw new Error(response.Error || 'CHEATEDITOR_GET failed'); }
        return response.Data ?? '';
    }

    /** CHEATEDITOR_SAVE — parses and persists edited cheat-list text. */
    public async cheatEditorSave(cheatListId: number, text: string): Promise<string> {
        const response = await this.sendCommand(
            { Command: 'CHEATEDITOR_SAVE', CheatListId: cheatListId, Data: text }, 30000);
        if (!response.Success) { throw new Error(response.Error || 'CHEATEDITOR_SAVE failed'); }
        return response.Message ?? 'Saved.';
    }

    /** CHEATEDITOR_CREATE — creates a new empty cheat list for a game. */
    public async cheatEditorCreate(gameId: number, title: string): Promise<CheatListInfo> {
        const response = await this.sendCommand(
            { Command: 'CHEATEDITOR_CREATE', CheatGameId: gameId, GameTitle: title });
        if (!response.Success) { throw new Error(response.Error || 'CHEATEDITOR_CREATE failed'); }
        return response.CheatEditorList!;
    }

    /** CHEATEDITOR_DELETE — deletes a cheat list and its cheats. */
    public async cheatEditorDelete(cheatListId: number): Promise<void> {
        const response = await this.sendCommand({ Command: 'CHEATEDITOR_DELETE', CheatListId: cheatListId });
        if (!response.Success) { throw new Error(response.Error || 'CHEATEDITOR_DELETE failed'); }
    }

    /**
     * CHEATEDITOR_IMPORT — imports the cheat repository's text files into cheat lists.
     * With `gameSerial`, only that game's files are imported.
     */
    public async cheatEditorImport(archiveRoot: string, gameSerial?: string): Promise<string> {
        const response = await this.sendCommand(
            { Command: 'CHEATEDITOR_IMPORT', FilePath: archiveRoot, GameSerial: gameSerial }, 3600000);
        if (!response.Success) { throw new Error(response.Error || 'CHEATEDITOR_IMPORT failed'); }
        return response.Message ?? 'Import complete.';
    }

    // ── Agent Profile ────────────────────────────────────────────────────────

    /**
     * MCPAGENT_LIST — returns all agent profiles.
     */
    public async mcpAgentList(): Promise<McpAgentInfo[]> {
        const response = await this.sendCommand({ Command: 'MCPAGENT_LIST' });
        if (!response.Success) { throw new Error(response.Error || 'MCPAGENT_LIST failed'); }
        return response.McpAgents ?? [];
    }

    /**
     * MCPAGENT_GET — returns a single agent profile by id.
     */
    public async mcpAgentGet(id: number): Promise<McpAgentInfo> {
        const response = await this.sendCommand({ Command: 'MCPAGENT_GET', McpAgentId: id });
        if (!response.Success) { throw new Error(response.Error || 'MCPAGENT_GET failed'); }
        return response.McpAgent!;
    }

    /**
     * MCPAGENT_ADD — creates a new agent profile.
     */
    public async mcpAgentAdd(agent: Omit<McpAgentInfo, 'id' | 'createdAt' | 'updatedAt'>): Promise<McpAgentInfo> {
        const response = await this.sendCommand({
            Command: 'MCPAGENT_ADD',
            McpAgentName:                agent.name,
            McpAgentDescription:         agent.description,
            McpAgentIsLead:              agent.isAgentLead,
            McpAgentStatus:              agent.status,
            McpAgentObjective:           agent.objective,
            McpAgentRules:               agent.rules,
            McpAgentGuidelines:          agent.guidelines,
            McpAgentRecommendedWorkflow: agent.recommendedWorkflow,
            McpAgentSessionBegin:        agent.sessionBegin,
            McpAgentConcurrentTasks:     agent.concurrentTasks,
            McpAgentSessionEnd:          agent.sessionEnd,
            McpAgentResearch:            agent.research,
        });
        if (!response.Success) { throw new Error(response.Error || 'MCPAGENT_ADD failed'); }
        return response.McpAgent!;
    }

    /**
     * MCPAGENT_UPDATE — updates an existing agent profile.
     */
    public async mcpAgentUpdate(id: number, fields: Partial<Omit<McpAgentInfo, 'id' | 'createdAt' | 'updatedAt'>>): Promise<McpAgentInfo> {
        const response = await this.sendCommand({
            Command: 'MCPAGENT_UPDATE',
            McpAgentId:                  id,
            McpAgentName:                fields.name,
            McpAgentDescription:         fields.description,
            McpAgentIsLead:              fields.isAgentLead,
            McpAgentStatus:              fields.status,
            McpAgentObjective:           fields.objective,
            McpAgentRules:               fields.rules,
            McpAgentGuidelines:          fields.guidelines,
            McpAgentRecommendedWorkflow: fields.recommendedWorkflow,
            McpAgentSessionBegin:        fields.sessionBegin,
            McpAgentConcurrentTasks:     fields.concurrentTasks,
            McpAgentSessionEnd:          fields.sessionEnd,
            McpAgentResearch:            fields.research,
        });
        if (!response.Success) { throw new Error(response.Error || 'MCPAGENT_UPDATE failed'); }
        return response.McpAgent!;
    }

    /**
     * MCPAGENT_DELETE — deletes an agent profile by id.
     */
    public async mcpAgentDelete(id: number): Promise<void> {
        const response = await this.sendCommand({ Command: 'MCPAGENT_DELETE', McpAgentId: id });
        if (!response.Success) { throw new Error(response.Error || 'MCPAGENT_DELETE failed'); }
    }

    /**
     * MCPAGENT_IMPORT — imports an agent profile from a Markdown file.
     */
    public async mcpAgentImport(filePath: string): Promise<McpAgentInfo> {
        const response = await this.sendCommand({ Command: 'MCPAGENT_IMPORT', FilePath: filePath });
        if (!response.Success) { throw new Error(response.Error || 'MCPAGENT_IMPORT failed'); }
        return response.McpAgent!;
    }

    /**
     * MCPAGENT_EXPORT — exports an agent profile to a Markdown file.
     */
    public async mcpAgentExport(id: number, outputPath: string): Promise<void> {
        const response = await this.sendCommand({ Command: 'MCPAGENT_EXPORT', McpAgentId: id, OutputPath: outputPath });
        if (!response.Success) { throw new Error(response.Error || 'MCPAGENT_EXPORT failed'); }
    }

    // ── Orchestration Profile ────────────────────────────────────────────────

    /**
     * MCPORCHESTRATION_LIST — returns all orchestration profiles.
     */
    public async mcpOrchestrationList(): Promise<McpOrchestrationInfo[]> {
        const response = await this.sendCommand({ Command: 'MCPORCHESTRATION_LIST' });
        if (!response.Success) { throw new Error(response.Error || 'MCPORCHESTRATION_LIST failed'); }
        return response.McpOrchestrations ?? [];
    }

    /**
     * MCPORCHESTRATION_GET — returns a single orchestration profile by id.
     */
    public async mcpOrchestrationGet(id: number): Promise<McpOrchestrationInfo> {
        const response = await this.sendCommand({ Command: 'MCPORCHESTRATION_GET', McpOrchestrationId: id });
        if (!response.Success) { throw new Error(response.Error || 'MCPORCHESTRATION_GET failed'); }
        return response.McpOrchestration!;
    }

    /**
     * MCPORCHESTRATION_ADD — creates a new orchestration profile.
     */
    public async mcpOrchestrationAdd(profile: Omit<McpOrchestrationInfo, 'id' | 'createdAt' | 'updatedAt'>): Promise<McpOrchestrationInfo> {
        const response = await this.sendCommand({
            Command:                      'MCPORCHESTRATION_ADD',
            McpOrchestrationName:         profile.name,
            McpOrchestrationDescription:  profile.description,
            McpOrchestrationStrategy:     profile.strategy,
            McpOrchestrationRoles:        profile.roles,
            McpOrchestrationWorkflow:     profile.workflow,
            McpOrchestrationRules:        profile.rules,
            McpOrchestrationNotes:        profile.notes,
        });
        if (!response.Success) { throw new Error(response.Error || 'MCPORCHESTRATION_ADD failed'); }
        return response.McpOrchestration!;
    }

    /**
     * MCPORCHESTRATION_UPDATE — updates an existing orchestration profile.
     */
    public async mcpOrchestrationUpdate(id: number, fields: Partial<Omit<McpOrchestrationInfo, 'id' | 'createdAt' | 'updatedAt'>>): Promise<McpOrchestrationInfo> {
        const response = await this.sendCommand({
            Command:                      'MCPORCHESTRATION_UPDATE',
            McpOrchestrationId:           id,
            McpOrchestrationName:         fields.name,
            McpOrchestrationDescription:  fields.description,
            McpOrchestrationStrategy:     fields.strategy,
            McpOrchestrationRoles:        fields.roles,
            McpOrchestrationWorkflow:     fields.workflow,
            McpOrchestrationRules:        fields.rules,
            McpOrchestrationNotes:        fields.notes,
        });
        if (!response.Success) { throw new Error(response.Error || 'MCPORCHESTRATION_UPDATE failed'); }
        return response.McpOrchestration!;
    }

    /**
     * MCPORCHESTRATION_DELETE — deletes an orchestration profile by id.
     */
    public async mcpOrchestrationDelete(id: number): Promise<void> {
        const response = await this.sendCommand({ Command: 'MCPORCHESTRATION_DELETE', McpOrchestrationId: id });
        if (!response.Success) { throw new Error(response.Error || 'MCPORCHESTRATION_DELETE failed'); }
    }

    /**
     * MCPORCHESTRATION_SETACTIVE — sets the active orchestration profile.
     */
    public async mcpOrchestrationSetActive(id: number): Promise<McpOrchestrationInfo> {
        const response = await this.sendCommand({ Command: 'MCPORCHESTRATION_SETACTIVE', McpOrchestrationId: id });
        if (!response.Success) { throw new Error(response.Error || 'MCPORCHESTRATION_SETACTIVE failed'); }
        return response.McpOrchestration!;
    }

    /**
     * MCPORCHESTRATION_DEACTIVATE — clears the active flag on all orchestration
     * profiles, leaving none active (single-agent mode).
     */
    public async mcpOrchestrationDeactivate(): Promise<void> {
        const response = await this.sendCommand({ Command: 'MCPORCHESTRATION_DEACTIVATE' });
        if (!response.Success) { throw new Error(response.Error || 'MCPORCHESTRATION_DEACTIVATE failed'); }
    }

    /**
     * MCPORCHESTRATION_IMPORT — imports an orchestration profile from a Markdown file.
     */
    public async mcpOrchestrationImport(filePath: string): Promise<McpOrchestrationInfo> {
        const response = await this.sendCommand({ Command: 'MCPORCHESTRATION_IMPORT', FilePath: filePath });
        if (!response.Success) { throw new Error(response.Error || 'MCPORCHESTRATION_IMPORT failed'); }
        return response.McpOrchestration!;
    }

    /**
     * MCPORCHESTRATION_EXPORT — exports an orchestration profile to a Markdown file.
     */
    public async mcpOrchestrationExport(id: number, outputPath: string): Promise<void> {
        const response = await this.sendCommand({ Command: 'MCPORCHESTRATION_EXPORT', McpOrchestrationId: id, OutputPath: outputPath });
        if (!response.Success) { throw new Error(response.Error || 'MCPORCHESTRATION_EXPORT failed'); }
    }

    // ── Chat tab (MCPCHAT_*) ─────────────────────────────────────────────────
    // Backs the AI control panel's Chat tab. See docs/ai-chat-templates.md for
    // the message-envelope contract that drives the renderer.

    /** MCPCHAT_SESSION_LIST — returns every chat session, newest activity first. */
    public async mcpChatSessionList(): Promise<McpChatSessionInfo[]> {
        const r = await this.sendCommand({ Command: 'MCPCHAT_SESSION_LIST' });
        if (!r.Success) { throw new Error(r.Error || 'MCPCHAT_SESSION_LIST failed'); }
        return r.McpChatSessions ?? [];
    }

    /** MCPCHAT_SESSION_GET — returns a session plus its full message list. */
    public async mcpChatSessionGet(id: number): Promise<{ session: McpChatSessionInfo; messages: McpChatMessageInfo[] }> {
        const r = await this.sendCommand({ Command: 'MCPCHAT_SESSION_GET', McpChatSessionId: id });
        if (!r.Success) { throw new Error(r.Error || 'MCPCHAT_SESSION_GET failed'); }
        if (!r.McpChatSession) { throw new Error(`Chat session ${id} not found.`); }
        return { session: r.McpChatSession, messages: r.McpChatMessages ?? [] };
    }

    /** MCPCHAT_SESSION_CREATE — opens a new chat thread. */
    public async mcpChatSessionCreate(args: {
        title:            string;
        orchestrationId?: number;
        gameSerial?:      string;
        gameTag?:         string;
    }): Promise<McpChatSessionInfo> {
        const r = await this.sendCommand({
            Command:                 'MCPCHAT_SESSION_CREATE',
            McpChatTitle:            args.title,
            McpChatOrchestrationId:  args.orchestrationId,
            McpChatGameSerial:       args.gameSerial,
            McpChatGameTag:          args.gameTag,
        });
        if (!r.Success) { throw new Error(r.Error || 'MCPCHAT_SESSION_CREATE failed'); }
        return r.McpChatSession!;
    }

    /** MCPCHAT_SESSION_RENAME — renames a session. */
    public async mcpChatSessionRename(id: number, title: string): Promise<McpChatSessionInfo> {
        const r = await this.sendCommand({ Command: 'MCPCHAT_SESSION_RENAME', McpChatSessionId: id, McpChatTitle: title });
        if (!r.Success) { throw new Error(r.Error || 'MCPCHAT_SESSION_RENAME failed'); }
        return r.McpChatSession!;
    }

    /** MCPCHAT_SESSION_DELETE — cascade-deletes a session and its messages. */
    public async mcpChatSessionDelete(id: number): Promise<void> {
        const r = await this.sendCommand({ Command: 'MCPCHAT_SESSION_DELETE', McpChatSessionId: id });
        if (!r.Success) { throw new Error(r.Error || 'MCPCHAT_SESSION_DELETE failed'); }
    }

    /** MCPCHAT_MESSAGE_LIST — returns the messages in a session, chronologically. */
    public async mcpChatMessageList(sessionId: number): Promise<McpChatMessageInfo[]> {
        const r = await this.sendCommand({ Command: 'MCPCHAT_MESSAGE_LIST', McpChatSessionId: sessionId });
        if (!r.Success) { throw new Error(r.Error || 'MCPCHAT_MESSAGE_LIST failed'); }
        return r.McpChatMessages ?? [];
    }

    /**
     * MCPCHAT_MESSAGE_APPEND — writes one message into a session. Used both by
     * MCPCHAT_SEND (server-side) and directly by the host when persisting a
     * locally-streamed reply.
     */
    public async mcpChatMessageAppend(args: {
        sessionId:     number;
        kind:          'user' | 'agent' | 'system';
        agentId?:      number;
        agentDisplay?: string;
        role?:         string;
        blocksJson:    string;
        tokens?:       number;
        latencyMs?:    number;
        status?:       'streaming' | 'done' | 'error';
    }): Promise<McpChatMessageInfo> {
        const r = await this.sendCommand({
            Command:             'MCPCHAT_MESSAGE_APPEND',
            McpChatSessionId:    args.sessionId,
            McpChatKind:         args.kind,
            McpChatAgentId:      args.agentId,
            McpChatAgentDisplay: args.agentDisplay,
            McpChatRole:         args.role,
            McpChatBlocksJson:   args.blocksJson,
            McpChatTokens:       args.tokens,
            McpChatLatencyMs:    args.latencyMs,
            McpChatStatus:       args.status,
        });
        if (!r.Success) { throw new Error(r.Error || 'MCPCHAT_MESSAGE_APPEND failed'); }
        return r.McpChatMessage!;
    }

    /**
     * MCPCHAT_MESSAGE_UPDATE — patches the block payload + meta of an existing
     * message. Pass <c>undefined</c> to leave a field unchanged.
     */
    public async mcpChatMessageUpdate(args: {
        id:          number;
        blocksJson?: string;
        tokens?:     number;
        latencyMs?:  number;
        status?:     'streaming' | 'done' | 'error';
    }): Promise<McpChatMessageInfo> {
        const r = await this.sendCommand({
            Command:           'MCPCHAT_MESSAGE_UPDATE',
            McpChatMessageId:  args.id,
            McpChatBlocksJson: args.blocksJson,
            McpChatTokens:     args.tokens,
            McpChatLatencyMs:  args.latencyMs,
            McpChatStatus:     args.status,
        });
        if (!r.Success) { throw new Error(r.Error || 'MCPCHAT_MESSAGE_UPDATE failed'); }
        return r.McpChatMessage!;
    }

    /** MCPCHAT_MESSAGE_DELETE — removes one message. */
    public async mcpChatMessageDelete(id: number): Promise<void> {
        const r = await this.sendCommand({ Command: 'MCPCHAT_MESSAGE_DELETE', McpChatMessageId: id });
        if (!r.Success) { throw new Error(r.Error || 'MCPCHAT_MESSAGE_DELETE failed'); }
    }

    /** MCPCHAT_MESSAGE_CLEAR — drops every message in a session, keeping the row. */
    public async mcpChatMessageClear(sessionId: number): Promise<void> {
        const r = await this.sendCommand({ Command: 'MCPCHAT_MESSAGE_CLEAR', McpChatSessionId: sessionId });
        if (!r.Success) { throw new Error(r.Error || 'MCPCHAT_MESSAGE_CLEAR failed'); }
    }

    /**
     * MCPCHAT_SEND — submits a user prompt. The server persists the user
     * message, runs the orchestrator pipeline, and returns the rows the
     * webview should append. Currently the orchestrator is a placeholder
     * stub; the wire shape (and streaming via MESSAGE_UPDATE) is final.
     */
    public async mcpChatSend(sessionId: number, text: string): Promise<McpChatMessageInfo[]> {
        const r = await this.sendCommand({
            Command:          'MCPCHAT_SEND',
            McpChatSessionId: sessionId,
            McpChatUserText:  text,
        }, 60_000);
        if (!r.Success) { throw new Error(r.Error || 'MCPCHAT_SEND failed'); }
        return r.McpChatMessages ?? [];
    }

    // ── DEV9 Network commands ────────────────────────────────────────────────

    /** NETGETSTATUS — queries the DEV9 network adapter status. */
    public async netGetStatus(): Promise<NetStatusInfo> {
        const direct = this.requireDirect();
        const s = await direct.networkStatus();
        return { enabled: s.attached, adapterType: s.attached ? 1 : 0, ps2Mac: s.mac.toUpperCase(), ps2IP: s.ip };
    }

    /**
     * NETCAPTURE — enables or disables packet capture.
     * @param enable Whether to enable (true) or disable (false) packet capture.
     * @param direction Capture direction filter: 0 = both, 1 = TX only, 2 = RX only.
     * @param maxQueue Maximum ring-buffer size (0 = server default of 1000).
     */
    public async netCapture(enable: boolean, direction?: number, maxQueue?: number): Promise<void> {
        const direct = this.requireDirect();
        this.directCaptured = 0;
        this.directCaptureStartUs = undefined;
        await direct.setCapture(enable, direction ?? 0, maxQueue || 1000);
    }

    /** NETRECVCAPTURE — drains captured packets from the ring buffer. */
    public async netRecvCapture(maxPackets?: number): Promise<{ packets: CapturedPacketInfo[]; parsedRows: ParsedPacketRow[] }> {
        const direct = this.requireDirect();
        const frames = await direct.takeCaptured(maxPackets ?? 0);
        if (frames.length === 0) { return { packets: [], parsedRows: [] }; }
        this.directCaptureStartUs ??= frames[0].timestampUs;
        const start = this.directCaptureStartUs;
        const time = (us: number) => ((us - start) / 1e6).toFixed(6);
        if (this.connected) {
            // The workspace service's dissector: SCERT/Medius layers, server names.
            const dissected = await this.netDissectMany(frames.map(f => ({
                data: f.data.toString('base64'), direction: f.tx ? 0 : 1, timestampUs: f.timestampUs,
            })));
            dissected.parsedRows.forEach((row, k) => {
                row.no = ++this.directCaptured;
                row.time = time(frames[k].timestampUs);
            });
            return dissected;
        }
        // Without it, rows from the emulator's own dissector lines.
        const packets = frames.map(f => ({
            direction: f.tx ? 0 : 1, directionName: f.tx ? 'TX' : 'RX', timestampUs: f.timestampUs,
            length: f.data.length, data: f.data.toString('base64'),
        }));
        const parsedRows = frames.map(f => ({ ...packetRow(++this.directCaptured, f.tx, f.timestampUs, f.data.length, f.summary), time: time(f.timestampUs) }));
        return { packets, parsedRows };
    }

    /**
     * NETDISSECTMANY — dissect a batch of frames (e.g. a loaded pcap) in one call. Returns the
     * same packet + row shapes as NETRECVCAPTURE.
     */
    public async netDissectMany(frames: Array<{ data: string; direction: number; timestampUs: number }>): Promise<{ packets: CapturedPacketInfo[]; parsedRows: ParsedPacketRow[] }> {
        const r = await this.sendCommand({ Command: 'NETDISSECTMANY', NetFrames: frames } as CommandMessage);
        if (!r.Success) { throw new Error(r.Error || 'NETDISSECTMANY failed'); }
        return { packets: r.CapturedPackets ?? [], parsedRows: r.ParsedPacketRows ?? [] };
    }

    /**
     * Puts a frame on Ps2Emulator's network adapter: into the PS2 (RX) or out through it (`tx`).
     * The options (payload swap, length and checksum fixes) are applied by the workspace
     * service's NETBUILDFRAME first. Returns the frame that was sent.
     */
    public async netInject(frameBase64: string, tx: boolean, options: NetFrameOptions = {}): Promise<{ ok: boolean; frame: string; message: string }> {
        const direct = this.requireDirect();
        let frame = frameBase64;
        if (options.payload || options.fix) {
            frame = (await this.netBuildFrame(frameBase64, { payload: options.payload, fix: options.fix })).frame;
        }
        const ok = await direct.injectFrame(Buffer.from(frame, 'base64'), tx);
        return { ok, frame, message: ok ? '' : 'the network adapter is not attached (start Ps2Emulator with --network)' };
    }

    /** NETBUILDFRAME — apply the injection edits without sending; returns the frame and its dissection. */
    public async netBuildFrame(frameBase64: string, options: NetFrameOptions = {}): Promise<{ frame: string; layers: DissectedLayer[]; row?: ParsedPacketRow; message: string }> {
        const r = await this.sendCommand({
            Command: 'NETBUILDFRAME',
            NetFrameData: frameBase64,
            NetPayloadData: options.payload,
            NetFixFrame: options.fix,
        } as CommandMessage);
        if (!r.Success) { throw new Error(r.Error || 'NETBUILDFRAME failed'); }
        return {
            frame:   r.NetFrame ?? frameBase64,
            layers:  (r as { DissectionLayers?: DissectedLayer[] }).DissectionLayers ?? [],
            row:     (r as { ParsedPacketRow?: ParsedPacketRow }).ParsedPacketRow,
            message: r.Message ?? '',
        };
    }

    /**
     * NETBUILDMESSAGE — encode one catalog message from field values into a plaintext SCERT
     * record (base64), ready to use as a frame payload.
     */
    public async netBuildMessage(protocol: string, id: number, values: Record<string, string>, rtType?: number, target?: number): Promise<{ data: string; message: string }> {
        const r = await this.sendCommand({
            Command: 'NETBUILDMESSAGE',
            NetMessageProtocol: protocol,
            NetMessageId: id,
            NetMessageValues: values,
            NetRtType: rtType,
            NetAppTarget: target,
        } as CommandMessage);
        if (!r.Success) { throw new Error(r.Error || 'NETBUILDMESSAGE failed'); }
        return { data: r.NetMessageData ?? '', message: r.Message ?? '' };
    }

    /** NETCATALOG — every message the multiplayer server defines, with field layouts. */
    public async netCatalog(): Promise<NetCatalogEntry[]> {
        const r = await this.sendCommand({ Command: 'NETCATALOG' });
        if (!r.Success) { throw new Error(r.Error || 'NETCATALOG failed'); }
        return r.NetCatalog ?? [];
    }

    /**
     * NETDISASSEMBLE — dissect a raw Ethernet frame against the canonical bridge
     * dissector. Returns the flat human-readable text, the structured layer
     * tree, and a pre-built ParsedPacketRow for table hydration.
     */
    public async netDisassemble(frameBase64: string): Promise<{
        text: string;
        layers: DissectedLayer[];
        row?: ParsedPacketRow;
    }> {
        const r = await this.sendCommand({
            Command: 'NETDISASSEMBLE',
            NetFrameData: frameBase64,
        } as CommandMessage);
        if (!r.Success) { throw new Error(r.Error || 'NETDISASSEMBLE failed'); }
        return {
            text:   r.DissectionText   ?? '',
            layers: (r as { DissectionLayers?: DissectedLayer[] }).DissectionLayers ?? [],
            row:    (r as { ParsedPacketRow?: ParsedPacketRow }).ParsedPacketRow,
        };
    }

    /** NETGETSTATS — returns TX/RX counters and capture queue statistics. */
    public async netGetStats(): Promise<NetStatsInfo> {
        const direct = this.requireDirect();
        const s = await direct.networkStatus();
        return {
            txPackets: s.txPackets, rxPackets: s.rxPackets, txBytes: s.txBytes, rxBytes: s.rxBytes,
            captureQueueSize: s.queued, droppedCount: s.dropped,
        };
    }

    /**
     * NETSETFILTER — sets the capture filter (protocol/port/IP).
     * @param filterType Filter type: 0 = none, 1 = protocol, 2 = port, 3 = IP.
     * @param protocol Protocol filter (type=1): 0=all, 1=TCP, 2=UDP, 3=ICMP, 4=ARP, 5=DNS, 6=DHCP.
     * @param port Port number to filter on (type=2).
     * @param ip IPv4 address as dotted-quad string to filter on (type=3).
     */
    public async netSetFilter(filterType: number, protocol?: number, port?: number, ip?: string): Promise<void> {
        const direct = this.requireDirect();
        await direct.setCaptureFilter(filterType, protocol, port, ip);
        return;
    }
}

/**
 * A packet table row from the emulator's dissector lines (`[IPv4] src=.. dst=..`, `[UDP] src=..
 * dst=..`, ...): addresses and ports, the innermost protocol, and that layer's details.
 */
function packetRow(no: number, tx: boolean, timestampUs: number, len: number, summary: string): ParsedPacketRow {
    const row: ParsedPacketRow = {
        no, time: (timestampUs / 1e6).toFixed(6), dir: tx ? 'TX' : 'RX', server: '', src: '', dst: '', proto: 'Ethernet', len, info: '',
    };
    let srcIp = '', dstIp = '';
    for (const line of summary.split('\n')) {
        const m = /^\[([^\]]+)\]\s*(.*)$/.exec(line.trim());
        if (!m) { continue; }
        const [, layer, rest] = m;
        const field = (name: string) => new RegExp(`(?:^|\\s)${name}=(\\S+)`).exec(rest)?.[1] ?? '';
        if (layer === 'Ethernet') {
            row.src = field('src'); row.dst = field('dst'); row.info = rest;
        } else if (layer === 'IPv4') {
            srcIp = field('src'); dstIp = field('dst');
            row.src = srcIp; row.dst = dstIp; row.proto = 'IPv4'; row.info = rest;
        } else if (layer === 'TCP' || layer === 'UDP') {
            row.src = `${srcIp}:${field('src')}`; row.dst = `${dstIp}:${field('dst')}`; row.proto = layer;
            row.info = rest.replace(/^src=\S+ dst=\S+\s*/, '');
        } else {
            row.proto = layer.startsWith('Unknown') ? 'Ethernet' : layer;
            row.info = layer.startsWith('Unknown') ? `${layer} ${rest}` : rest;
        }
    }
    return row;
}

/** Snapshot metadata entry returned by GETSNAPSHOTS. */
export interface SnapshotInfo {
    id: number;
    title: string;
    description?: string;
    snapshotPath: string;
    snapshotDateTime: string;
}

// ── Network / DEV9 types ─────────────────────────────────────────────────────

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
    /** Pre-computed layer tree from the bridge dissector (populated by NETRECVCAPTURE). */
    layers?: DissectedLayer[];
}

/** Wire payload for a single dissected field returned alongside NETDISASSEMBLE / NETRECVCAPTURE. */
export interface DissectedField {
    name: string;
    value: string;
}

/** Wire payload for one protocol layer (Ethernet / IPv4 / TCP / SCERT / Medius / ...). */
export interface DissectedLayer {
    label: string;
    color: string;
    fields: DissectedField[];
    offset: number;
    length: number;
}

/** Payload / fix-up options applied (by NETBUILDFRAME) before a frame is injected. */
export interface NetFrameOptions {
    /** New TCP/UDP payload (base64); lengths and checksums are recomputed. */
    payload?: string;
    /** Recompute IPv4/TCP/UDP lengths and checksums after hand edits. */
    fix?: boolean;
}

/** One field of a NETCATALOG message (see MessageCatalog.cs). */
export interface NetCatalogField {
    name: string;
    /** uint, int, bool, f32, vec3, string, bytes, cstr or hex. */
    type: string;
    offset: number;
    length: number;
    options?: string[] | null;
    auto?: string | null;
    optional?: boolean;
}

/** One message the multiplayer server defines, returned by NETCATALOG. */
export interface NetCatalogEntry {
    /** SCERT, Lobby, LobbyExt, MGCL, DME, Game or Object. */
    protocol: string;
    cls: number;
    id: number;
    name: string;
    description: string;
    size: number | null;
    fields: NetCatalogField[];
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
