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
    DirectEmulator, DirectEmulatorEvents, DirectFrame, DirectLine, DirectPause, DirectStatus, StopStream,
} from './directEmulator';
import protoText from './ps2debug.proto';

/** Default address of `ps2 ... --debug-server`. */
export const DEFAULT_PS2DEBUG_ADDRESS = '127.0.0.1:28100';
/** Largest message the server sends or takes (a whole main RAM read fits). */
const MAX_MESSAGE_BYTES = 64 << 20;
const CALL_DEADLINE_MS = 10_000;
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
        for (const name of ['Session', 'Execution', 'Breakpoints', 'Memory', 'Registers', 'Code', 'Media']) {
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
            const reply = await this.call<{ lines: DirectLine[] }>('Code', 'disassemble', { address: at, count: want });
            lines.push(...reply.lines);
            if (reply.lines.length < want) {
                break;
            }
            done += want;
        }
        return lines;
    }

    public async disassembleWords(address: number, words: number[]): Promise<DirectLine[]> {
        const lines: DirectLine[] = [];
        for (let done = 0; done < words.length; done += MAX_LINES) {
            const at = (address + 4 * done) >>> 0;
            const chunk = words.slice(done, done + MAX_LINES).map(w => w >>> 0);
            const reply = await this.call<{ lines: DirectLine[] }>('Code', 'disassemble', { address: at, words: chunk });
            lines.push(...reply.lines);
        }
        return lines;
    }
}
