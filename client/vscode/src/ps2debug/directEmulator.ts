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
export type DirectRefKind = 'call' | 'jump' | 'branch' | 'registerJump' | 'address' | 'load' | 'store' | 'pointer' | 'case';

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
    disassembleWords(address: number, words: number[]): Promise<DirectLine[]>;

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
}
