/**
 * An emulator the extension talks to directly, without the Bridge: today
 * Ps2Emulator's debug server (`ps2 ... --debug-server`, see
 * ps2DebugClient.ts).
 *
 * BridgeClient hands its live emulator calls (memory, registers, execution,
 * breakpoints, disassembly) to a DirectEmulator when one is attached, so the
 * views keep calling BridgeClient and need no changes. This file has no
 * Node imports: BridgeClient is shared with the browser client.
 */
import type { RegisterDump } from '../bridgeClient';

/** Execution state, as BridgeClient.getPC reports it. */
export interface DirectStatus {
    paused: boolean;
    pc: number;
    /** Set while paused at a breakpoint: its address. */
    breakpointHitAddress?: number;
}

/** One disassembled instruction. */
export interface DirectLine {
    address: number;
    word: number;
    /** Mnemonic and operands, e.g. `addiu   t0, t0, 1`. */
    text: string;
    /** The symbol at this address ('' when none). */
    label: string;
    /** References to this address in the built indexes. */
    codeRefs: number;
    dataRefs: number;
    /** An index found a function starting here. */
    functionStart: boolean;
    /** A jump's or branch's target's symbol ('' when none). */
    targetLabel: string;
    /**
     * What the instruction does with the values it sees, from the
     * emulator simulating the code (`a0 = 0x00301234`, `→ Player_Update
     * (a0 = 0x00000001)`); '' when there is nothing to say or none was asked.
     */
    hint: string;
}

/** How much the emulator's state hints say (the disassembler's comment column). */
export type DirectHintLevel = 'full' | 'addresses' | 'off';

/** What a scan compares: an integer of 1, 2, 4 or 8 bytes, a float, a double, text or a byte pattern. */
export type DirectValueKind = 'int' | 'float' | 'double' | 'string' | 'bytes';

/** How a scan compares; the relative ones compare with the scan before. */
export type DirectCompare =
    | 'exact' | 'not' | 'greater' | 'less' | 'between' | 'unknown'
    | 'changed' | 'unchanged' | 'increased' | 'decreased' | 'increasedBy' | 'decreasedBy';

/** A first scan, run in the emulator. */
export interface DirectScanRequest {
    region: DirectRegion;
    /** Offsets in the region (EE: physical); `end` 0 for the region's end. */
    start: number;
    end: number;
    kind: DirectValueKind;
    /** Bytes of an integer. */
    size: number;
    signed: boolean;
    /** Integers typed as hex without `0x`. */
    hex: boolean;
    compare: DirectCompare;
    value: string;
    value2: string;
}

/** A scan session kept by the emulator. */
export interface DirectScanInfo {
    id: number;
    count: number;
    scans: number;
    canUndo: boolean;
    size: number;
    millis: number;
}

/** One candidate: where, and its bytes at the last scan (and now, when asked). */
export interface DirectScanResult {
    address: number;
    previous: Buffer;
    current?: Buffer;
}

/** A pointer chain: read at `base`, add `offsets[0]`, read there, ... the target's offset last. */
export interface DirectChain {
    base: number;
    offsets: number[];
    /** Where it leads now (from checkPointers); undefined when a read fails. */
    leadsTo?: number;
}

/** A thread as its kernel keeps it. */
export interface DirectThread {
    id: number;
    state: 'running' | 'ready' | 'waiting' | 'suspended' | 'waitingSuspended' | 'dormant' | 'other';
    /** Where it runs, or will resume. */
    pc: number;
    sp: number;
    ra: number;
    entry: number;
    priority: number;
}

/** A loaded IOP module. */
export interface DirectModule {
    name: string;
    /** Major and minor, a byte each. */
    version: number;
    entry: number;
    gp: number;
    text: number;
    textSize: number;
    dataSize: number;
    bssSize: number;
}

/** One call on a stack, innermost first. */
export interface DirectStackFrame {
    pc: number;
    sp: number;
    /** Where its function starts, when found. */
    entry?: number;
    stackSize: number;
    /** The symbol at the entry ('' when none). */
    function: string;
}

/** What a trace records. */
export interface DirectTraceSpec {
    coverage?: boolean;
    calls?: boolean;
    branches?: boolean;
    /** Stores (and loads) to log: EE address, bytes. */
    writes?: { address: number; length: number }[];
    reads?: { address: number; length: number }[];
    /** Stop by itself after this many fields. */
    fields?: number;
    limit?: number;
}

/** A trace's state. */
export interface DirectTraceInfo {
    id: number;
    running: boolean;
    instructions: number;
    records: number;
    covered: number;
}

/** One thing a trace saw. */
export type DirectTraceRecord =
    | { kind: 'call'; from: number; to: number; sp: number; args: number[]; indirect: boolean }
    | { kind: 'return'; from: number; to: number }
    | { kind: 'branch'; from: number; to: number; indirect: boolean }
    | { kind: 'instruction'; pc: number }
    | { kind: 'access'; pc: number; address: number; size: number; write: boolean; value: number };

/** A patch the emulator applies every field (raw cheat codes). */
export interface DirectPatch {
    name: string;
    enabled: boolean;
    /** Engine rows its codes became. */
    rows: number;
    /** Lines not translated (they do nothing), and lines ignored on purpose. */
    errors: string[];
    warnings: string[];
    /** Fields it was applied in. */
    applied: number;
}

/** A controller button, as the protocol names it. */
export type DirectPadButton =
    | 'l2' | 'r2' | 'l1' | 'r1' | 'triangle' | 'circle' | 'cross' | 'square'
    | 'select' | 'l3' | 'r3' | 'start' | 'up' | 'right' | 'down' | 'left';

/** The network adapter (DEV9) and its traffic. */
export interface DirectNetworkStatus {
    attached: boolean;
    mac: string;
    ip: string;
    txPackets: number;
    rxPackets: number;
    txBytes: number;
    rxBytes: number;
    capturing: boolean;
    queued: number;
    dropped: number;
}

/** A frame the adapter passed: sent by the PS2 (`tx`) or received. */
export interface DirectCapturedFrame {
    tx: boolean;
    timestampUs: number;
    data: Buffer;
    /** A line per layer, as the emulator's dissector writes them. */
    summary: string;
}

/** A code index region: main RAM or IOP RAM. */
export type DirectRegion = 'ee' | 'iop';

/** An index's state. */
export interface DirectIndexInfo {
    region: DirectRegion;
    ready: boolean;
    building: boolean;
    /** The field its snapshot was taken at. */
    field: number;
    refs: number;
    functions: number;
    tables: number;
    buildMillis: number;
}

/** The protocol's reference kinds. */
/** `observed`: a call or jump through a register that a trace saw land there. */
export type DirectRefKind = 'call' | 'jump' | 'branch' | 'registerJump' | 'address' | 'load' | 'store' | 'pointer' | 'case' | 'observed';

/** A reference to an address, from the index. */
export interface DirectRef {
    from: number;
    kind: DirectRefKind;
    /** The word at `from` and its disassembly. */
    word: number;
    text: string;
    /** The function holding `from`, and its symbol ('' when none). */
    function?: number;
    functionName: string;
}

/** A name for an address. Addresses are normalized (KSEG0/KSEG1 folded to physical). */
export interface DirectSymbol {
    address: number;
    name: string;
    size?: number;
    kind?: 'label' | 'function' | 'object';
    source?: string;
}

/** Why execution paused, pushed by the emulator. */
export interface DirectPause {
    reason: 'requested' | 'breakpoint' | 'watchpoint' | 'step' | 'runTo' | 'frameAdvance' | 'error' | 'unknown';
    pc: number;
    /** `error`: what went wrong. */
    error?: string;
}

/** A displayed frame, row-major RGBA8. */
export interface DirectFrame {
    width: number;
    height: number;
    rgba: Buffer;
    /** Video fields emulated so far. */
    field: number;
}

/** Stops a stream started by DirectEmulator.frames or .console. */
export type StopStream = () => void;

export interface DirectEmulatorEvents {
    /** Execution paused (breakpoint, step, request, …). */
    onPaused?(pause: DirectPause): void;
    onResumed?(): void;
    /** The connection was lost or closed. */
    onClosed?(): void;
}

export interface DirectEmulator {
    /** Where the emulator is, `host:port`. */
    readonly address: string;
    isConnected(): boolean;
    close(): void;

    /**
     * Reads `length` bytes. `region` is BridgeClient's: `ee` takes any EE
     * address (including the IOP RAM mirror at 0xBC000000), `iop` IOP
     * addresses. Fails if nothing is mapped at `address`; a read that runs
     * into unmapped memory returns the bytes before it.
     */
    readMemory(address: number, length: number, region: string): Promise<Buffer>;
    /** `length` bytes at each address, in one round trip; unmapped ones are left out. */
    readMemoryBatch(addresses: number[], length: number, region: string): Promise<Map<number, Buffer>>;
    writeMemory(address: number, data: Buffer, region: string): Promise<void>;

    getRegisters(): Promise<RegisterDump>;
    /** BridgeClient.setRegister's names: GPR, PC, HI, LO, HI1, LO1, COP0, FPR. */
    setRegister(name: string, index: number, value: string): Promise<void>;

    status(): Promise<DirectStatus>;
    pause(): Promise<void>;
    resume(): Promise<void>;
    /** One instruction (with its delay slot); stays paused. */
    stepInto(): Promise<DirectStatus>;
    /** Runs a call to its return; a pause event follows. */
    stepOver(): Promise<void>;
    /** Runs to the return address; a pause event follows. */
    stepOut(): Promise<void>;
    /** Resumes until the EE reaches `address`, and waits for it (up to `timeoutMs`). */
    runTo(address: number, timeoutMs: number): Promise<DirectStatus>;

    /** A breakpoint at an EE address; every alias of the instruction hits. */
    setBreakpoint(address: number): Promise<void>;
    /** Removes every breakpoint at `address`. */
    clearBreakpoint(address: number): Promise<void>;
    clearAllBreakpoints(): Promise<void>;

    /** `count` instructions read from memory at `address`; stops at unmapped memory. */
    disassemble(address: number, count: number): Promise<DirectLine[]>;
    /** Disassembles `words` as if they were at `address` onwards. */
    disassembleWords(address: number, words: number[], hints?: DirectHintLevel): Promise<DirectLine[]>;

    /** Indexes a region from a snapshot taken now; with `wait`, once it is built. */
    buildIndex(region: DirectRegion, wait: boolean): Promise<DirectIndexInfo>;
    indexStatus(): Promise<DirectIndexInfo[]>;
    /** References to `address` from every built index; `kinds` empty: all. */
    refsTo(address: number, kinds: DirectRefKind[], limit: number): Promise<{ target: number; total: number; refs: DirectRef[] }>;

    /** Names addresses under `source` (clearing its others first with `replace`); returns how many addresses are named in all. */
    setSymbols(source: string, symbols: DirectSymbol[], replace: boolean): Promise<number>;
    /** Removes `source`'s symbols at `addresses` (all of them when empty). */
    removeSymbols(source: string, addresses: number[]): Promise<number>;
    /** Every symbol (the one shown per address), in address order. */
    listSymbols(contains?: string): Promise<DirectSymbol[]>;

    /**
     * The displayed frame, at most `maxFps` times a second (a slow
     * receiver gets the latest frame, never a backlog). `onEnd` is called
     * once when the stream ends, with the error if it failed.
     */
    frames(maxFps: number, onFrame: (frame: DirectFrame) => void, onEnd: (error?: Error) => void): StopStream;
    /** Console text from the EE (`sio`) and the IOP (`printf`), as produced. */
    console(onText: (source: 'ee' | 'iop', text: string) => void): StopStream;

    /** A first scan: a session the emulator keeps (memory never leaves it). */
    newScan(request: DirectScanRequest): Promise<DirectScanInfo>;
    /** Narrows a session, comparing with memory now and at the scan before. */
    nextScan(id: number, compare: DirectCompare, value: string, value2: string, hex: boolean): Promise<DirectScanInfo>;
    /** Back to the candidates before the last next scan. */
    undoScan(id: number): Promise<DirectScanInfo>;
    /** Up to `limit` candidates from the `from`th. */
    scanResults(id: number, from: number, limit: number, current: boolean): Promise<DirectScanResult[]>;
    endScan(id: number): Promise<void>;
    /** Pointer chains from `[baseStart, baseEnd)` (both 0: the default) to an EE address. */
    findPointers(target: number, maxDepth: number, maxOffset: number, baseStart: number, baseEnd: number, limit: number):
        Promise<{ chains: DirectChain[]; limited: boolean; millis: number }>;
    /** Where chains lead now; with `target`, only those reaching it. */
    checkPointers(chains: DirectChain[], target?: number): Promise<DirectChain[]>;

    /** The EE kernel's threads. */
    eeThreads(): Promise<DirectThread[]>;
    /** The IOP's loaded modules. */
    iopModules(): Promise<DirectModule[]>;
    /** The calls the EE (or an EE thread) is in, innermost first. */
    stackWalk(thread?: number): Promise<DirectStackFrame[]>;

    /** Arms a trace (the EE is interpreted while it runs). */
    startTrace(spec: DirectTraceSpec): Promise<DirectTraceInfo>;
    traceStatus(id: number): Promise<DirectTraceInfo>;
    /** Disarms a trace; what it recorded stays until dropTrace. */
    stopTrace(id: number): Promise<DirectTraceInfo>;
    traceRecords(id: number, from: number, limit: number): Promise<{ total: number; records: DirectTraceRecord[] }>;
    /** Code a stopped trace saw run (less what `without` saw), as [start, bytes] ranges, and the indexed functions in it. */
    coverage(id: number, without?: number): Promise<{ ranges: [number, number][]; functions: number[] }>;
    dropTrace(id: number): Promise<void>;

    /** Adds or replaces (by name) a patch: raw cheat codes applied every field. */
    setPatch(name: string, codes: string, enabled: boolean): Promise<DirectPatch>;
    listPatches(): Promise<DirectPatch[]>;
    enablePatch(name: string, enabled: boolean): Promise<DirectPatch>;
    removePatch(name: string): Promise<void>;

    /** Holds buttons (and sticks: lx, ly, rx, ry) on a port until released. */
    padHold(port: number, buttons: DirectPadButton[], sticks?: [number, number, number, number]): Promise<void>;
    /** Presses buttons for some fields. */
    padPress(port: number, buttons: DirectPadButton[], fields: number): Promise<void>;
    padRelease(port: number): Promise<void>;
    /** What clients hold on a port, and whether a controller is there. */
    padGet(port: number): Promise<{ held: DirectPadButton[]; sticks?: [number, number, number, number]; connected: boolean }>;

    networkStatus(): Promise<DirectNetworkStatus>;
    /** Starts or stops capturing (direction 0 both, 1 sent, 2 received); stopping empties the queue. */
    setCapture(enable: boolean, direction: number, maxQueue: number): Promise<DirectNetworkStatus>;
    /** Filter type 0 none, 1 protocol (0 all, 1 TCP, 2 UDP, 3 ICMP, 4 ARP, 5 DNS, 6 DHCP), 2 port, 3 IPv4. */
    setCaptureFilter(type: number, protocol?: number, port?: number, ip?: string): Promise<void>;
    takeCaptured(max: number): Promise<DirectCapturedFrame[]>;
    /** Puts a frame on the wire: out from the PS2 (`tx`) or to it. */
    injectFrame(frame: Buffer, tx: boolean): Promise<boolean>;
}
