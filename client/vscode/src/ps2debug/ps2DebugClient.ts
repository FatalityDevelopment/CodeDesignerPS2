/**
 * A client of Ps2Emulator's debug server (`ps2 bios|elf ... --debug-server
 * <port>`): the ps2debug gRPC protocol (ps2debug.proto, vendored from the
 * emulator), behind the DirectEmulator interface BridgeClient uses.
 *
 * The schema is parsed at run time (protobufjs) from the bundled .proto, so
 * there is no code generation step. Events (pauses, resumes) are pushed by
 * the server over one stream; nothing here polls.
 */
import * as grpc from '@grpc/grpc-js';
import * as protoLoader from '@grpc/proto-loader';
import * as protobuf from 'protobufjs';
import type { RegisterDump } from '../bridgeClient';
import { classifyAddress, MemoryRegion } from '../shared/memoryRegions';
import type {
    DirectChain, DirectCompare, DirectEmulator, DirectEmulatorEvents, DirectFrame, DirectHintLevel, DirectIndexInfo, DirectLine,
    DirectPause, DirectRef, DirectScanInfo, DirectScanRequest, DirectScanResult, DirectThread, DirectModule, DirectStackFrame,
    DirectTraceSpec, DirectTraceInfo, DirectTraceRecord,
    DirectRefKind, DirectRegion, DirectStatus, DirectSymbol, StopStream,
} from './directEmulator';
import protoText from './ps2debug.proto';

/** Default address of `ps2 ... --debug-server`. */
export const DEFAULT_PS2DEBUG_ADDRESS = '127.0.0.1:28100';
/** Largest message the server sends or takes (a whole main RAM read fits). */
const MAX_MESSAGE_BYTES = 64 << 20;
const CALL_DEADLINE_MS = 10_000;
/** Scans and pointer scans over all of main RAM, on slow machines. */
const SCAN_DEADLINE_MS = 60_000;
const CONNECT_DEADLINE_MS = 3_000;
/** Lines one Disassemble returns at most. */
const MAX_LINES = 4096;

const MASK64 = (1n << 64n) - 1n;

// Address spaces (enum AddressSpace).
type Space = 'EE_VIRTUAL' | 'EE_RAM' | 'SCRATCHPAD' | 'BIOS' | 'IOP_RAM'
    | 'VU0_CODE' | 'VU0_DATA' | 'VU1_CODE' | 'VU1_DATA' | 'GS_LOCAL';

interface Location { space: Space; offset: number }

/** uint64 fields arrive as decimal strings (`longs: String`). */
type U64 = string;
interface U128 { lo: U64; hi: U64 }
interface Vector { x: number; y: number; z: number; w: number }

interface ReadReply { data: Buffer; unmappedAt?: U64 }
interface EeRegisters {
    pc: number; gpr: U128[]; hi: U128; lo: U128; sa: number; cop0: number[];
    fpr: number[]; fpuAcc: number; fcr0: number; fcr31: number;
    vu0Vf: Vector[]; vu0Vi: number[]; vu0Acc: Vector; inDelaySlot: boolean; cycles: U64;
}
interface StatusReply { paused: boolean; eePc: number; eeCycles: U64; armed: boolean }
interface BreakpointInfo { id: number; address: number }
interface PointList { breakpoints: BreakpointInfo[] }
interface PausedWire { reason: string; pc: number; error: string }
interface EventWire { kind?: 'paused' | 'resumed' | 'breakpointLogged' | 'watchLogged'; paused?: PausedWire }

/** A unary call of a grpc-js client, typed loosely (the schema is loaded at run time). */
type Unary = (
    request: object,
    metadata: grpc.Metadata,
    options: grpc.CallOptions,
    callback: (error: grpc.ServiceError | null, reply: unknown) => void,
) => grpc.ClientUnaryCall;

type Service = grpc.Client & Record<string, Unary>;

const REF_KINDS: Record<string, DirectRefKind> = {
    REF_CALL: 'call',
    REF_JUMP: 'jump',
    REF_BRANCH: 'branch',
    REF_REGISTER_JUMP: 'registerJump',
    REF_ADDRESS: 'address',
    REF_LOAD: 'load',
    REF_STORE: 'store',
    REF_POINTER: 'pointer',
    REF_CASE: 'case',
    REF_OBSERVED: 'observed',
};

const SYMBOL_KINDS: Record<string, NonNullable<DirectSymbol['kind']>> = {
    SYMBOL_LABEL: 'label',
    SYMBOL_FUNCTION: 'function',
    SYMBOL_OBJECT: 'object',
};

/** Symbols one List call asks for. */
const SYMBOL_PAGE = 65_536;

interface LineWire {
    address: number; word: number; text: string; label: string; codeRefs: number; dataRefs: number;
    functionStart: boolean; targetLabel: string; hint: string;
}

interface ScanInfoWire { id: number; count: U64; scans: number; canUndo: boolean; size: number; millis: number }
interface ScanResultWire { address: U64; previous: Buffer; current: Buffer }
interface ChainWire { base: number; offsets: number[]; leadsTo?: number }

const COMPARES: Record<DirectCompare, string> = {
    exact: 'COMPARE_EXACT', not: 'COMPARE_NOT', greater: 'COMPARE_GREATER', less: 'COMPARE_LESS',
    between: 'COMPARE_BETWEEN', unknown: 'COMPARE_UNKNOWN', changed: 'COMPARE_CHANGED',
    unchanged: 'COMPARE_UNCHANGED', increased: 'COMPARE_INCREASED', decreased: 'COMPARE_DECREASED',
    increasedBy: 'COMPARE_INCREASED_BY', decreasedBy: 'COMPARE_DECREASED_BY',
};

const VALUE_KINDS: Record<DirectScanRequest['kind'], string> = {
    int: 'VALUE_INT', float: 'VALUE_FLOAT', double: 'VALUE_DOUBLE', string: 'VALUE_STRING', bytes: 'VALUE_BYTES',
};

function scanInfoFromWire(i: ScanInfoWire): DirectScanInfo {
    return { id: i.id, count: Number(i.count), scans: i.scans, canUndo: i.canUndo, size: i.size, millis: i.millis };
}

function chainFromWire(c: ChainWire): DirectChain {
    return { base: c.base >>> 0, offsets: c.offsets.map(o => o >>> 0), leadsTo: c.leadsTo === undefined || c.leadsTo === null ? undefined : c.leadsTo >>> 0 };
}

interface ThreadWire { id: number; state: string; pc: number; sp: number; ra: number; entry: number; priority: number }
interface TraceInfoWire { id: number; running: boolean; instructions: U64; records: U64; covered: U64 }
interface TraceRecordWire {
    call?: { from: number; to: number; sp: number; args: number[]; indirect: boolean };
    return?: { from: number; to: number };
    branch?: { from: number; to: number; indirect: boolean };
    instruction?: number;
    access?: { pc: number; address: number; size: number; write: boolean; value?: U128 };
}

const THREAD_STATES: Record<string, DirectThread['state']> = {
    THREAD_RUNNING: 'running', THREAD_READY: 'ready', THREAD_WAITING: 'waiting', THREAD_SUSPENDED: 'suspended',
    THREAD_WAITING_SUSPENDED: 'waitingSuspended', THREAD_DORMANT: 'dormant',
};

function traceInfoFromWire(i: TraceInfoWire): DirectTraceInfo {
    return { id: i.id, running: i.running, instructions: Number(i.instructions), records: Number(i.records), covered: Number(i.covered) };
}

function traceRecordFromWire(r: TraceRecordWire): DirectTraceRecord {
    if (r.call) { return { kind: 'call', from: r.call.from >>> 0, to: r.call.to >>> 0, sp: r.call.sp >>> 0, args: r.call.args.map(a => a >>> 0), indirect: r.call.indirect }; }
    if (r.return) { return { kind: 'return', from: r.return.from >>> 0, to: r.return.to >>> 0 }; }
    if (r.branch) { return { kind: 'branch', from: r.branch.from >>> 0, to: r.branch.to >>> 0, indirect: r.branch.indirect }; }
    if (r.access) {
        return {
            kind: 'access', pc: r.access.pc >>> 0, address: r.access.address >>> 0, size: r.access.size,
            write: r.access.write, value: Number(r.access.value?.lo ?? 0),
        };
    }
    return { kind: 'instruction', pc: (r.instruction ?? 0) >>> 0 };
}

const HINT_LEVELS: Record<DirectHintLevel, string> = {
    full: 'HINTS_FULL',
    addresses: 'HINTS_ADDRESSES',
    off: 'HINTS_NONE',
};

interface IndexInfoWire {
    region: string; ready: boolean; building: boolean; field: U64; refs: U64; functions: number;
    tables: number; buildMillis: number;
}

interface SymbolWire { address: number; name: string; size: number; kind: string; source: string }

function lineFromWire(l: LineWire): DirectLine {
    return {
        address: l.address >>> 0, word: l.word >>> 0, text: l.text, label: l.label, codeRefs: l.codeRefs,
        dataRefs: l.dataRefs, functionStart: l.functionStart, targetLabel: l.targetLabel, hint: l.hint ?? '',
    };
}

function indexInfoFromWire(i: IndexInfoWire): DirectIndexInfo {
    return {
        region: i.region === 'INDEX_IOP_RAM' ? 'iop' : 'ee', ready: i.ready, building: i.building,
        field: Number(i.field), refs: Number(i.refs), functions: i.functions, tables: i.tables,
        buildMillis: i.buildMillis,
    };
}

function regionToWire(region: DirectRegion): string {
    return region === 'iop' ? 'INDEX_IOP_RAM' : 'INDEX_EE_RAM';
}

const PAUSE_REASONS: Record<string, DirectPause['reason']> = {
    REQUESTED: 'requested',
    BREAKPOINT: 'breakpoint',
    WATCHPOINT: 'watchpoint',
    STEP: 'step',
    RUN_TO: 'runTo',
    FRAME_ADVANCE: 'frameAdvance',
    ERROR: 'error',
};

function loadServices(): Record<string, grpc.ServiceClientConstructor> {
    const root = protobuf.parse(protoText, { keepCase: false }).root;
    const definition = protoLoader.fromJSON(root.toJSON(), {
        keepCase: false,
        longs: String,
        enums: String,
        defaults: true,
        oneofs: true,
    });
    const loaded = grpc.loadPackageDefinition(definition) as unknown as {
        ps2debug: { v1: Record<string, grpc.ServiceClientConstructor> };
    };
    return loaded.ps2debug.v1;
}

/** Splits a 64-bit (or wider) value into the protocol's U128 halves. */
function u128(value: bigint): U128 {
    return { lo: (value & MASK64).toString(), hi: ((value >> 64n) & MASK64).toString() };
}

function lo32(text: U64): number {
    return Number(BigInt(text) & 0xFFFF_FFFFn);
}

function hi32(text: U64): number {
    return Number((BigInt(text) >> 32n) & 0xFFFF_FFFFn);
}

/** A register value as SETREG takes it: decimal or `0x` hex, possibly negative. */
function parseValue(text: string): bigint {
    const trimmed = text.trim();
    const negative = trimmed.startsWith('-');
    const digits = negative ? trimmed.slice(1) : trimmed;
    const value = BigInt(/^0x/i.test(digits) ? digits : digits.replace(/^\+/, ''));
    return negative ? (-value) & MASK64 : value;
}

function hex(value: number): string {
    return `0x${(value >>> 0).toString(16).padStart(8, '0')}`;
}

/** Where BridgeClient's `(address, region)` lives in the protocol's address spaces. */
export function locate(address: number, region: string): Location {
    const v = address >>> 0;
    if (region.toLowerCase() === 'iop') {
        const phys = (v >= 0x80000000 && v < 0xC0000000) ? (v & 0x1FFFFFFF) : v;
        if (phys >= 0x1FC00000 && phys < 0x20000000) {
            return { space: 'BIOS', offset: phys - 0x1FC00000 };
        }
        return { space: 'IOP_RAM', offset: phys % (8 << 20) };
    }
    const info = classifyAddress(v);
    const phys = info.physicalAddress >>> 0;
    switch (info.region) {
        case MemoryRegion.IopRam:
            return { space: 'IOP_RAM', offset: (phys - 0x1C000000) >>> 0 };
        case MemoryRegion.EeScratchpad:
            return { space: 'SCRATCHPAD', offset: v - 0x70000000 };
        case MemoryRegion.Vu0Code:
            return { space: 'VU0_CODE', offset: phys - 0x11000000 };
        case MemoryRegion.Vu0Data:
            return { space: 'VU0_DATA', offset: phys - 0x11004000 };
        case MemoryRegion.Vu1Code:
            return { space: 'VU1_CODE', offset: phys - 0x11008000 };
        case MemoryRegion.Vu1Data:
            return { space: 'VU1_DATA', offset: phys - 0x1100C000 };
        default:
            return { space: 'EE_VIRTUAL', offset: v };
    }
}

export class Ps2DebugClient implements DirectEmulator {
    private readonly services: Record<string, Service> = {};
    private events: grpc.ClientReadableStream<EventWire> | null = null;
    private connected = false;
    private clientId = 0;
    /** The last pause, until the next resume. */
    private lastPause: DirectPause | null = null;
    private pauseWaiters: Array<(pause: DirectPause) => void> = [];

    /** Open server streams (frames, console), cancelled on close. */
    private readonly streams = new Set<grpc.ClientReadableStream<unknown>>();

    private constructor(
        public readonly address: string,
        private readonly handlers: DirectEmulatorEvents,
    ) {
        const constructors = loadServices();
        const options = {
            'grpc.max_receive_message_length': MAX_MESSAGE_BYTES,
            'grpc.max_send_message_length': MAX_MESSAGE_BYTES,
        };
        const credentials = grpc.credentials.createInsecure();
        for (const name of ['Session', 'Execution', 'Breakpoints', 'Memory', 'Registers', 'Code', 'Media', 'Symbols', 'Index', 'Search', 'Snapshots', 'Trace', 'Os']) {
            this.services[name] = new constructors[name](address, credentials, options) as unknown as Service;
        }
    }

    /**
     * Connects to the debug server at `address` (`host:port`): says hello,
     * then follows its events. Fails if no server answers within a few
     * seconds.
     */
    public static async connect(address: string, handlers: DirectEmulatorEvents = {}): Promise<Ps2DebugClient> {
        const client = new Ps2DebugClient(address, handlers);
        try {
            const hello = await client.call<{ clientId: number; protocolVersion: number }>(
                'Session', 'hello', { clientName: 'CodeDesigner' }, CONNECT_DEADLINE_MS,
            );
            client.clientId = hello.clientId;
            client.connected = true;
            client.followEvents();
            const status = await client.status();
            if (status.paused) {
                client.lastPause = { reason: 'unknown', pc: status.pc };
            }
        } catch (error) {
            client.close();
            throw new Error(`no Ps2Emulator debug server at ${address} (${(error as Error).message}); `
                + `start it with \`ps2 bios|elf ... --debug-server ${address}\``);
        }
        return client;
    }

    public isConnected(): boolean {
        return this.connected;
    }

    public close(): void {
        const wasConnected = this.connected;
        this.connected = false;
        this.events?.cancel();
        this.events = null;
        for (const stream of this.streams) {
            stream.cancel();
        }
        this.streams.clear();
        for (const service of Object.values(this.services)) {
            service.close();
        }
        if (wasConnected) {
            this.handlers.onClosed?.();
        }
    }

    private call<T>(service: string, method: string, request: object, deadlineMs = CALL_DEADLINE_MS): Promise<T> {
        const client = this.services[service];
        return new Promise<T>((resolve, reject) => {
            const options = { deadline: new Date(Date.now() + deadlineMs) };
            client[method](request, new grpc.Metadata(), options, (error, reply) => {
                if (error) {
                    reject(new Error(error.details || error.message));
                } else {
                    resolve(reply as T);
                }
            });
        });
    }

    /** Opens a server stream on `Media`, tracked so close() cancels it. */
    private openStream<T>(method: string, request: object): grpc.ClientReadableStream<T> {
        const media = this.services['Media'] as unknown as Record<string, (r: object) => grpc.ClientReadableStream<T>>;
        const stream = media[method](request);
        this.streams.add(stream as grpc.ClientReadableStream<unknown>);
        stream.on('close', () => this.streams.delete(stream as grpc.ClientReadableStream<unknown>));
        return stream;
    }

    public frames(maxFps: number, onFrame: (frame: DirectFrame) => void, onEnd: (error?: Error) => void): StopStream {
        interface FrameWire { width: number; height: number; rgba: Buffer; field: U64 }
        const stream = this.openStream<FrameWire>('frames', { maxFps });
        let ended = false;
        const end = (error?: Error) => {
            if (!ended) {
                ended = true;
                onEnd(error);
            }
        };
        stream.on('data', (f: FrameWire) => onFrame({
            width: f.width, height: f.height, rgba: Buffer.from(f.rgba), field: Number(f.field),
        }));
        stream.on('error', (error: grpc.ServiceError) => {
            end(error.code === grpc.status.CANCELLED ? undefined : new Error(error.details || error.message));
        });
        stream.on('close', () => end());
        return () => stream.cancel();
    }

    public console(onText: (source: 'ee' | 'iop', text: string) => void): StopStream {
        interface TextWire { source: string; text: Buffer }
        const stream = this.openStream<TextWire>('console', {});
        stream.on('data', (t: TextWire) => onText(t.source === 'IOP' ? 'iop' : 'ee', Buffer.from(t.text).toString('latin1')));
        stream.on('error', () => { /* ends with the connection; 'close' follows */ });
        return () => stream.cancel();
    }

    private followEvents(): void {
        const session = this.services['Session'] as unknown as {
            events(request: object): grpc.ClientReadableStream<EventWire>;
        };
        const stream = session.events({});
        this.events = stream;
        stream.on('data', (event: EventWire) => this.onEvent(event));
        stream.on('error', () => { /* 'close' follows; cancelling also lands here */ });
        stream.on('close', () => {
            if (this.events === stream && this.connected) {
                this.close();
            }
        });
    }

    private onEvent(event: EventWire): void {
        if (event.kind === 'paused' && event.paused) {
            const pause: DirectPause = {
                reason: PAUSE_REASONS[event.paused.reason] ?? 'unknown',
                pc: event.paused.pc >>> 0,
                error: event.paused.error || undefined,
            };
            this.lastPause = pause;
            const waiters = this.pauseWaiters;
            this.pauseWaiters = [];
            waiters.forEach(resolve => resolve(pause));
            this.handlers.onPaused?.(pause);
        } else if (event.kind === 'resumed') {
            this.lastPause = null;
            this.handlers.onResumed?.();
        }
    }

    // ── memory ──────────────────────────────────────────────────────────

    public async readMemory(address: number, length: number, region: string): Promise<Buffer> {
        const { space, offset } = locate(address, region);
        const reply = await this.call<ReadReply>('Memory', 'read', { space, offset, length });
        if (reply.data.length === 0 && length > 0) {
            throw new Error(`nothing mapped at ${hex(address)}`);
        }
        return Buffer.from(reply.data);
    }

    public async readMemoryBatch(addresses: number[], length: number, region: string): Promise<Map<number, Buffer>> {
        const reads = addresses.map(a => ({ ...locate(a, region), length }));
        const reply = await this.call<{ replies: ReadReply[] }>('Memory', 'readMany', { reads });
        const out = new Map<number, Buffer>();
        reply.replies.forEach((r, k) => {
            if (r.data.length > 0) {
                out.set(addresses[k] >>> 0, Buffer.from(r.data));
            }
        });
        return out;
    }

    public async writeMemory(address: number, data: Buffer, region: string): Promise<void> {
        const { space, offset } = locate(address, region);
        const reply = await this.call<{ written: U64; unmappedAt?: U64 }>('Memory', 'write', { space, offset, data });
        if (reply.unmappedAt !== undefined && reply.unmappedAt !== null) {
            throw new Error(`wrote ${reply.written} of ${data.length} bytes: nothing mapped at ${hex(address + Number(reply.written))}`);
        }
    }

    // ── registers ───────────────────────────────────────────────────────

    public async getRegisters(): Promise<RegisterDump> {
        const r = await this.call<EeRegisters>('Registers', 'ee', {});
        const vf = (pick: (v: Vector) => number) => {
            const out = new Array(32).fill(0);
            r.vu0Vf.forEach((v, i) => { out[i] = pick(v) >>> 0; });
            return out;
        };
        const vi = new Array(16).fill(0);
        r.vu0Vi.forEach((v, i) => { vi[i] = v; });
        const acc = r.vu0Acc ?? { x: 0, y: 0, z: 0, w: 0 };
        return {
            gpr: r.gpr.map(g => lo32(g.lo)),
            gprLo32: r.gpr.map(g => lo32(g.lo)),
            gprHi32: r.gpr.map(g => hi32(g.lo)),
            gprUpLo32: r.gpr.map(g => lo32(g.hi)),
            gprUpHi32: r.gpr.map(g => hi32(g.hi)),
            pc: r.pc >>> 0,
            hi: lo32(r.hi.lo),
            lo: lo32(r.lo.lo),
            hi1: lo32(r.hi.hi),
            lo1: lo32(r.lo.hi),
            sa: r.sa,
            cop0Status: r.cop0[12] ?? 0,
            cop0Cause: r.cop0[13] ?? 0,
            cop0Epc: r.cop0[14] ?? 0,
            cop0Count: r.cop0[9] ?? 0,
            cop0Regs: r.cop0.map(v => v >>> 0),
            fpr: r.fpr.map(v => v >>> 0),
            fprAcc: r.fpuAcc >>> 0,
            fcr0: r.fcr0 >>> 0,
            fcr31: r.fcr31 >>> 0,
            vu0fX: vf(v => v.x),
            vu0fY: vf(v => v.y),
            vu0fZ: vf(v => v.z),
            vu0fW: vf(v => v.w),
            vu0AccX: acc.x >>> 0,
            vu0AccY: acc.y >>> 0,
            vu0AccZ: acc.z >>> 0,
            vu0AccW: acc.w >>> 0,
            // Protocol v1 has the 16 integer registers but not Q, P, I or the flags.
            vu0Q: 0,
            vu0P: 0,
            vu0I: 0,
            vu0i: vi,
            vu0Status: 0,
            vu0Mac: 0,
            vu0Clip: 0,
            // GS privileged registers are not in protocol v1.
            gsPriv: new Array(19).fill(0),
        };
    }

    public async setRegister(name: string, index: number, value: string): Promise<void> {
        const v = parseValue(value);
        const set = (file: string, i: number, full: bigint) =>
            this.call<object>('Registers', 'set', { file, index: i, value: u128(full) });
        switch (name.toUpperCase()) {
            case 'PC':
                await set('EE_PC', 0, v);
                return;
            case 'GPR':
            case 'HI':
            case 'LO':
            case 'HI1':
            case 'LO1': {
                // These set 64 bits of a 128-bit register: keep the other half.
                const r = await this.call<EeRegisters>('Registers', 'ee', {});
                const upper = name.toUpperCase().endsWith('1');
                const [file, i, current] =
                    name.toUpperCase() === 'GPR' ? ['EE_GPR', index, r.gpr[index]]
                        : name.toUpperCase().startsWith('HI') ? ['EE_HI', 0, r.hi]
                            : ['EE_LO', 0, r.lo];
                if (!current) {
                    throw new Error(`no register ${name}[${index}]`);
                }
                const lo = upper ? BigInt(current.lo) : v & MASK64;
                const hi = upper ? v & MASK64 : BigInt(current.hi);
                await set(file, i, (hi << 64n) | lo);
                return;
            }
            case 'COP0':
                await set('EE_COP0', index, v);
                return;
            case 'FPR':
                await set('EE_FPR', index, v);
                return;
            case 'FCR31':
                await set('EE_FCR31', 0, v);
                return;
            default:
                throw new Error(`register ${name} cannot be set over the ps2debug protocol`);
        }
    }

    // ── execution ───────────────────────────────────────────────────────

    public async status(): Promise<DirectStatus> {
        const s = await this.call<StatusReply>('Session', 'status', {});
        const atBreakpoint = s.paused && this.lastPause?.reason === 'breakpoint';
        return {
            paused: s.paused,
            pc: s.eePc >>> 0,
            breakpointHitAddress: atBreakpoint ? this.lastPause?.pc : undefined,
        };
    }

    public async pause(): Promise<void> {
        await this.call<object>('Execution', 'pause', { client: this.clientId });
    }

    public async resume(): Promise<void> {
        await this.call<object>('Execution', 'resume', {});
    }

    public async stepInto(): Promise<DirectStatus> {
        await this.call<object>('Execution', 'step', { kind: 'INTO' });
        return this.status();
    }

    public async stepOver(): Promise<void> {
        await this.call<object>('Execution', 'step', { kind: 'OVER' });
    }

    public async stepOut(): Promise<void> {
        await this.call<object>('Execution', 'step', { kind: 'OUT' });
    }

    public async runTo(address: number, timeoutMs: number): Promise<DirectStatus> {
        const paused = this.nextPause(timeoutMs);
        await this.call<object>('Execution', 'runTo', { address: address >>> 0 });
        await paused;
        return this.status();
    }

    /** The next pause event, or a timeout error after `timeoutMs`. */
    private nextPause(timeoutMs: number): Promise<DirectPause> {
        return new Promise<DirectPause>((resolve, reject) => {
            const timer = setTimeout(() => {
                this.pauseWaiters = this.pauseWaiters.filter(w => w !== waiter);
                reject(new Error(`the emulator did not stop within ${timeoutMs} ms`));
            }, timeoutMs);
            const waiter = (pause: DirectPause) => {
                clearTimeout(timer);
                resolve(pause);
            };
            this.pauseWaiters.push(waiter);
        });
    }

    // ── breakpoints ─────────────────────────────────────────────────────

    public async setBreakpoint(address: number): Promise<void> {
        await this.call<{ id: number }>('Breakpoints', 'setBreakpoint', {
            client: this.clientId,
            address: address >>> 0,
            action: 'ACTION_BREAK',
        });
    }

    public async clearBreakpoint(address: number): Promise<void> {
        // Breakpoints hit at every alias of an instruction, so match aliases.
        const physical = (a: number) => (a >>> 0) & 0x1FFFFFFF;
        await this.removeBreakpoints(b => physical(b.address) === physical(address));
    }

    public async clearAllBreakpoints(): Promise<void> {
        await this.removeBreakpoints(() => true);
    }

    private async removeBreakpoints(which: (b: BreakpointInfo) => boolean): Promise<void> {
        const list = await this.call<PointList>('Breakpoints', 'list', {});
        for (const b of list.breakpoints.filter(which)) {
            await this.call<object>('Breakpoints', 'remove', { id: b.id });
        }
    }

    // ── code ────────────────────────────────────────────────────────────

    public async disassemble(address: number, count: number): Promise<DirectLine[]> {
        const lines: DirectLine[] = [];
        for (let done = 0; done < count;) {
            const at = (address + 4 * done) >>> 0;
            const want = Math.min(MAX_LINES, count - done);
            const reply = await this.call<{ lines: LineWire[] }>('Code', 'disassemble', { address: at, count: want });
            lines.push(...reply.lines.map(lineFromWire));
            if (reply.lines.length < want) {
                break;
            }
            done += want;
        }
        return lines;
    }

    public async disassembleWords(address: number, words: number[], hints: DirectHintLevel = 'off'): Promise<DirectLine[]> {
        const lines: DirectLine[] = [];
        for (let done = 0; done < words.length; done += MAX_LINES) {
            const at = (address + 4 * done) >>> 0;
            const chunk = words.slice(done, done + MAX_LINES).map(w => w >>> 0);
            const reply = await this.call<{ lines: LineWire[] }>(
                'Code', 'disassemble', { address: at, words: chunk, hints: HINT_LEVELS[hints] },
            );
            lines.push(...reply.lines.map(lineFromWire));
        }
        return lines;
    }

    // ── search ──────────────────────────────────────────────────────────

    public async newScan(r: DirectScanRequest): Promise<DirectScanInfo> {
        const info = await this.call<ScanInfoWire>('Search', 'newScan', {
            space: r.region === 'iop' ? 'IOP_RAM' : 'EE_RAM', start: r.start, end: r.end, kind: VALUE_KINDS[r.kind],
            size: r.size, signed: r.signed, hex: r.hex, compare: COMPARES[r.compare], value: r.value, value2: r.value2,
        }, SCAN_DEADLINE_MS);
        return scanInfoFromWire(info);
    }

    public async nextScan(id: number, compare: DirectCompare, value: string, value2: string, hex: boolean): Promise<DirectScanInfo> {
        const info = await this.call<ScanInfoWire>(
            'Search', 'nextScan', { id, compare: COMPARES[compare], value, value2, hex }, SCAN_DEADLINE_MS,
        );
        return scanInfoFromWire(info);
    }

    public async undoScan(id: number): Promise<DirectScanInfo> {
        return scanInfoFromWire(await this.call<ScanInfoWire>('Search', 'undoScan', { id }));
    }

    public async scanResults(id: number, from: number, limit: number, current: boolean): Promise<DirectScanResult[]> {
        const page = await this.call<{ results: ScanResultWire[] }>('Search', 'scanResults', { id, from, limit, current });
        return page.results.map(r => ({
            address: Number(r.address), previous: r.previous, current: current && r.current.length ? r.current : undefined,
        }));
    }

    public async endScan(id: number): Promise<void> {
        await this.call<object>('Search', 'endScan', { id });
    }

    public async findPointers(target: number, maxDepth: number, maxOffset: number, baseStart: number, baseEnd: number, limit: number):
        Promise<{ chains: DirectChain[]; limited: boolean; millis: number }> {
        const reply = await this.call<{ chains: ChainWire[]; limited: boolean; millis: number }>('Search', 'findPointers', {
            target: target >>> 0, maxDepth, maxOffset, baseStart, baseEnd, limit,
        }, SCAN_DEADLINE_MS);
        return { chains: reply.chains.map(chainFromWire), limited: reply.limited, millis: reply.millis };
    }

    public async checkPointers(chains: DirectChain[], target?: number): Promise<DirectChain[]> {
        const request: Record<string, unknown> = { chains: chains.map(c => ({ base: c.base, offsets: c.offsets })) };
        if (target !== undefined) { request['target'] = target >>> 0; }
        const reply = await this.call<{ chains: ChainWire[] }>('Search', 'checkPointers', request);
        return reply.chains.map(chainFromWire);
    }

    // ── os ──────────────────────────────────────────────────────────────

    public async eeThreads(): Promise<DirectThread[]> {
        const reply = await this.call<{ threads: ThreadWire[] }>('Os', 'eeThreads', {});
        return reply.threads.map(t => ({
            id: t.id, state: THREAD_STATES[t.state] ?? 'other', pc: t.pc >>> 0, sp: t.sp >>> 0, ra: t.ra >>> 0,
            entry: t.entry >>> 0, priority: t.priority,
        }));
    }

    public async iopModules(): Promise<DirectModule[]> {
        const reply = await this.call<{ modules: DirectModule[] }>('Os', 'iopModules', {});
        return reply.modules;
    }

    public async stackWalk(thread?: number): Promise<DirectStackFrame[]> {
        const request: Record<string, unknown> = {};
        if (thread !== undefined) { request['thread'] = thread; }
        const reply = await this.call<{ frames: { pc: number; sp: number; entry?: number; stackSize: number; function: string }[] }>(
            'Os', 'stackWalk', request,
        );
        return reply.frames.map(f => ({
            pc: f.pc >>> 0, sp: f.sp >>> 0, entry: f.entry === undefined || f.entry === null ? undefined : f.entry >>> 0,
            stackSize: f.stackSize, function: f.function,
        }));
    }

    // ── trace ───────────────────────────────────────────────────────────

    public async startTrace(spec: DirectTraceSpec): Promise<DirectTraceInfo> {
        const filters = (list: { address: number; length: number }[] | undefined, write: boolean) =>
            (list ?? []).map(f => ({ address: f.address >>> 0, length: f.length, reads: !write, writes: write }));
        const info = await this.call<TraceInfoWire>('Trace', 'start', {
            client: this.clientId, coverage: !!spec.coverage, calls: !!spec.calls, branches: !!spec.branches,
            accesses: [...filters(spec.writes, true), ...filters(spec.reads, false)],
            fields: spec.fields ?? 0, limit: spec.limit ?? 0,
        });
        return traceInfoFromWire(info);
    }

    public async traceStatus(id: number): Promise<DirectTraceInfo> {
        return traceInfoFromWire(await this.call<TraceInfoWire>('Trace', 'status', { id }));
    }

    public async stopTrace(id: number): Promise<DirectTraceInfo> {
        return traceInfoFromWire(await this.call<TraceInfoWire>('Trace', 'stop', { id }));
    }

    public async traceRecords(id: number, from: number, limit: number): Promise<{ total: number; records: DirectTraceRecord[] }> {
        const page = await this.call<{ total: U64; records: TraceRecordWire[] }>('Trace', 'records', { id, from, limit });
        return { total: Number(page.total), records: page.records.map(traceRecordFromWire) };
    }

    public async coverage(id: number, without?: number): Promise<{ ranges: [number, number][]; functions: number[] }> {
        const request: Record<string, unknown> = { id };
        if (without !== undefined) { request['without'] = without; }
        const reply = await this.call<{ ranges: { start: number; length: number }[]; functions: number[] }>(
            'Trace', 'coverage', request, SCAN_DEADLINE_MS,
        );
        return { ranges: reply.ranges.map(r => [r.start >>> 0, r.length]), functions: reply.functions.map(f => f >>> 0) };
    }

    public async dropTrace(id: number): Promise<void> {
        await this.call<object>('Trace', 'drop', { id });
    }

    // ── index ───────────────────────────────────────────────────────────

    public async buildIndex(region: DirectRegion, wait: boolean): Promise<DirectIndexInfo> {
        // A 32 MiB index builds in well under a second; leave room for slow machines.
        const info = await this.call<IndexInfoWire>(
            'Index', 'build', { region: regionToWire(region), wait }, wait ? 120_000 : CALL_DEADLINE_MS,
        );
        return indexInfoFromWire(info);
    }

    public async indexStatus(): Promise<DirectIndexInfo[]> {
        const reply = await this.call<{ regions: IndexInfoWire[] }>('Index', 'status', {});
        return reply.regions.map(indexInfoFromWire);
    }

    public async refsTo(address: number, kinds: DirectRefKind[], limit: number)
        : Promise<{ target: number; total: number; refs: DirectRef[] }> {
        const wireKinds = Object.entries(REF_KINDS).filter(([, k]) => kinds.includes(k)).map(([wire]) => wire);
        interface RefWire { from: number; kind: string; word: number; text: string; function?: number; functionName: string }
        const reply = await this.call<{ target: number; total: number; refs: RefWire[] }>(
            'Index', 'refsTo', { address: address >>> 0, kinds: wireKinds, limit },
        );
        return {
            target: reply.target >>> 0,
            total: reply.total,
            refs: reply.refs.map(r => ({
                from: r.from >>> 0,
                kind: REF_KINDS[r.kind] ?? 'pointer',
                word: r.word >>> 0,
                text: r.text,
                function: r.function === undefined || r.function === null ? undefined : r.function >>> 0,
                functionName: r.functionName,
            })),
        };
    }

    // ── symbols ─────────────────────────────────────────────────────────

    public async setSymbols(source: string, symbols: DirectSymbol[], replace: boolean): Promise<number> {
        const kindToWire = (kind: DirectSymbol['kind']) =>
            Object.entries(SYMBOL_KINDS).find(([, k]) => k === kind)?.[0] ?? 'SYMBOL_LABEL';
        const reply = await this.call<{ total: number }>('Symbols', 'set', {
            source,
            replace,
            symbols: symbols.map(s => ({ address: s.address >>> 0, name: s.name, size: s.size ?? 0, kind: kindToWire(s.kind) })),
        });
        return reply.total;
    }

    public async removeSymbols(source: string, addresses: number[]): Promise<number> {
        const reply = await this.call<{ total: number }>('Symbols', 'remove', { source, addresses: addresses.map(a => a >>> 0) });
        return reply.total;
    }

    public async listSymbols(contains = ''): Promise<DirectSymbol[]> {
        const out: DirectSymbol[] = [];
        let from: number | undefined = 0;
        while (from !== undefined) {
            const page: { symbols: SymbolWire[]; next?: number | null } =
                await this.call('Symbols', 'list', { from, limit: SYMBOL_PAGE, contains });
            for (const s of page.symbols) {
                out.push({ address: s.address >>> 0, name: s.name, size: s.size, kind: SYMBOL_KINDS[s.kind] ?? 'label', source: s.source });
            }
            const next = page.next === undefined || page.next === null ? undefined : page.next >>> 0;
            // A page always moves forward; anything else ends the listing.
            from = next !== undefined && next > from && page.symbols.length > 0 ? next : undefined;
        }
        return out;
    }
}
