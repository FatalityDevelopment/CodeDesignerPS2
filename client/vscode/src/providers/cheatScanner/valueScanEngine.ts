import { SocketClient } from '../../socketClient';

/**
 * Cheat Engine-style first scan / next scan engine for the Cheat Scanner page.
 *
 * First scans lean on the bridge where it can do the work server-side
 * (SCANVALUE for integer/float comparisons, SCANPATTERN for strings and byte
 * patterns). Anything that needs the previous value — unknown initial value,
 * changed/unchanged/increased/decreased, between — is compared here against
 * the bytes captured by the previous scan:
 *
 *  - dense mode keeps a full snapshot of the scanned range plus a per-slot
 *    candidate mask (used while the result set is still huge);
 *  - sparse mode keeps candidate addresses plus their previous raw bytes.
 */

export type ScanValueType = 'u8' | 'u16' | 'u32' | 'f32' | 'string' | 'bytes';
export type ScanMode =
    | 'exact' | 'not' | 'greater' | 'less' | 'between' | 'unknown'
    | 'changed' | 'unchanged' | 'increased' | 'decreased' | 'increasedBy' | 'decreasedBy';
export type ScanRegionTag = 'ee' | 'iop';

export interface ScanRequest {
    valueType: ScanValueType;
    mode:      ScanMode;
    value?:    string;
    value2?:   string;
    /** Integer values are typed as hex (no 0x needed). */
    hex?:      boolean;
    /** Integer values compare and display as signed. */
    signed?:   boolean;
    region:    ScanRegionTag;
    start:     number;
    end:       number;
}

export interface ScanRow {
    address:  number;
    previous: string;
    /** Raw previous bytes as hex — used for cheat-code export. */
    raw:      string;
}

export interface ScanSummary {
    count:     number;
    scanCount: number;
    valueType: ScanValueType;
    region:    ScanRegionTag;
    signed:    boolean;
    size:      number;
    canUndo:   boolean;
    message?:  string;
}

/** Result sets at or below this size are kept as address lists. */
const SPARSE_LIMIT = 1_000_000;
/** Up to this many addresses are re-read with READMEMBATCH; above it the span is read in one go. */
const BATCH_LIMIT = 2_000;
const BATCH_CHUNK = 500;
/** SCANVALUE stops at this many results (ValueScanService.MaxScanResults). */
const SCANVALUE_CAP = 100_000;
const REGION_READ_TIMEOUT_MS = 120_000;

const FIRST_SCAN_MODES: ReadonlySet<ScanMode> = new Set(['exact', 'not', 'greater', 'less', 'between', 'unknown']);
const NEEDS_VALUE: ReadonlySet<ScanMode> = new Set(['exact', 'not', 'greater', 'less', 'between', 'increasedBy', 'decreasedBy']);

interface Pattern { bytes: number[]; }

interface SessionState {
    valueType: ScanValueType;
    signed:    boolean;
    region:    ScanRegionTag;
    start:     number;
    end:       number;
    /** Bytes compared per candidate. */
    size:      number;
    /** Distance between candidate slots in dense mode. */
    step:      number;
    count:     number;
    scanCount: number;
    // dense
    snapshot?: Buffer;
    /** null = every slot is a candidate (unknown initial value). */
    mask?:     Uint8Array | null;
    // sparse
    addrs?:    Uint32Array;
    prev?:     Buffer;
}

/** Comparator over decoded numbers; `prev` is NaN when no previous value applies. */
type NumberTest = (cur: number, prev: number, curRaw: Buffer, curOff: number, prevRaw: Buffer | undefined, prevOff: number) => boolean;

export class ValueScanEngine {
    private _state: SessionState | null = null;
    private _undo: SessionState | null = null;

    constructor(private readonly socket: SocketClient) {}

    get hasSession(): boolean { return this._state !== null; }

    reset(): void {
        this._state = null;
        this._undo = null;
    }

    undo(): ScanSummary | null {
        if (!this._undo) { return this.summary(); }
        this._state = this._undo;
        this._undo = null;
        return this.summary('Reverted to the previous scan.');
    }

    summary(message?: string): ScanSummary | null {
        const s = this._state;
        if (!s) { return null; }
        return {
            count: s.count, scanCount: s.scanCount, valueType: s.valueType, region: s.region,
            signed: s.signed, size: s.size, canUndo: this._undo !== null, message,
        };
    }

    // ── First scan ───────────────────────────────────────────────────────────

    async firstScan(req: ScanRequest): Promise<ScanSummary> {
        if (!FIRST_SCAN_MODES.has(req.mode)) {
            throw new Error('That comparison needs a previous scan — run a first scan.');
        }
        if (req.end <= req.start) { throw new Error('End address must be greater than start address.'); }

        let state: SessionState;
        let message: string | undefined;
        if (req.valueType === 'string' || req.valueType === 'bytes') {
            state = await this._firstPatternScan(req);
        } else {
            const viaBridge = await this._firstBridgeScan(req);
            if (viaBridge && viaBridge !== 'capped') {
                state = viaBridge;
            } else {
                state = await this._firstDenseScan(req);
                if (viaBridge === 'capped') {
                    message = 'More than 100,000 bridge matches — compared a full snapshot instead.';
                }
            }
        }
        this._undo = null;
        this._state = state;
        return this.summary(message)!;
    }

    private async _firstPatternScan(req: ScanRequest): Promise<SessionState> {
        if (req.mode !== 'exact') { throw new Error('String and byte-pattern scans support "Exact value" only.'); }
        const pattern = parsePattern(req.valueType, req.value ?? '');
        const hits = await this.socket.scanPattern(req.start, req.end, pattern.bytes, req.region);
        const addrs = Uint32Array.from(hits.map(a => a >>> 0));
        const size = pattern.bytes.length;
        const prev = await this._readValues(addrs, size, req.region);
        return this._sparseFromRead(
            { valueType: req.valueType, signed: false, region: req.region, start: req.start, end: req.end, size, step: 1, count: 0, scanCount: 1 },
            addrs, prev, () => true);
    }

    /**
     * SCANVALUE for the comparisons it supports. null when the comparison needs a
     * host-side snapshot; 'capped' when the bridge hit its result cap.
     */
    private async _firstBridgeScan(req: ScanRequest): Promise<SessionState | 'capped' | null> {
        const cmp = ({ exact: 'eq', not: 'ne', greater: 'gt', less: 'lt' } as Record<string, string>)[req.mode];
        if (!cmp) { return null; }
        // Float equality goes through the host so it can round like Cheat Engine does.
        if (req.valueType === 'f32' && (req.mode === 'exact' || req.mode === 'not')) { return null; }

        const size = typeSize(req.valueType);
        const target = parseNumber(req.value, req.valueType, req.hex, req.signed);
        const dataType = bridgeDataType(req.valueType, !!req.signed);
        const { results } = await this.socket.scanValue(
            req.start, req.end, String(target), cmp, dataType, req.region);
        if (results.length >= SCANVALUE_CAP) { return 'capped'; }

        const addrs = new Uint32Array(results.length);
        const prev = Buffer.alloc(results.length * size);
        results.forEach((r, i) => {
            addrs[i] = parseInt(r.address, 16) >>> 0;
            encodeNumber(prev, i * size, Number(r.value), req.valueType, !!req.signed);
        });
        return {
            valueType: req.valueType, signed: !!req.signed, region: req.region, start: req.start, end: req.end,
            size, step: size, count: addrs.length, scanCount: 1, addrs, prev,
        };
    }

    private async _firstDenseScan(req: ScanRequest): Promise<SessionState> {
        const size = typeSize(req.valueType);
        const snapshot = await this._readRange(req.start, req.end, req.region);
        const base: SessionState = {
            valueType: req.valueType, signed: !!req.signed, region: req.region, start: req.start, end: req.end,
            size, step: size, count: 0, scanCount: 1,
        };
        const slots = Math.floor(snapshot.length / size);
        if (req.mode === 'unknown') {
            return { ...base, snapshot, mask: null, count: slots };
        }
        const test = this._numberTest(req);
        return this._denseResult(base, snapshot, undefined, null, test);
    }

    // ── Next scan ────────────────────────────────────────────────────────────

    async nextScan(req: ScanRequest): Promise<ScanSummary> {
        const s = this._state;
        if (!s) { return this.firstScan(req); }
        if (req.mode === 'unknown') { throw new Error('"Unknown initial value" is only a first-scan option.'); }

        let next: SessionState;
        const meta: SessionState = { ...s, scanCount: s.scanCount + 1, snapshot: undefined, mask: undefined, addrs: undefined, prev: undefined, count: 0 };
        if (s.valueType === 'string' || s.valueType === 'bytes') {
            next = await this._nextPatternScan(s, meta, req);
        } else {
            const test = this._numberTest({ ...req, valueType: s.valueType, signed: s.signed });
            if (s.addrs && s.prev) {
                const cur = await this._readValues(s.addrs, s.size, s.region);
                next = this._sparseFromRead(meta, s.addrs, cur, (i, curBuf, curOff) => {
                    const c = decodeNumber(curBuf, curOff, s.valueType, s.signed);
                    const p = decodeNumber(s.prev!, i * s.size, s.valueType, s.signed);
                    return test(c, p, curBuf, curOff, s.prev, i * s.size);
                });
            } else {
                const snapshot = await this._readRange(s.start, s.end, s.region);
                next = this._denseResult(meta, snapshot, s.snapshot, s.mask ?? null, test);
            }
        }
        this._undo = s;
        this._state = next;
        return this.summary()!;
    }

    private async _nextPatternScan(s: SessionState, meta: SessionState, req: ScanRequest): Promise<SessionState> {
        const addrs = s.addrs ?? new Uint32Array(0);
        const cur = await this._readValues(addrs, s.size, s.region);
        let test: (i: number, buf: Buffer, off: number) => boolean;
        if (req.mode === 'exact' || req.mode === 'not') {
            const pattern = parsePattern(s.valueType, req.value ?? '');
            if (pattern.bytes.length !== s.size) {
                throw new Error(`The new value must be ${s.size} byte(s) long, like the first scan.`);
            }
            const want = req.mode === 'exact';
            test = (_i, buf, off) => matchesPattern(buf, off, pattern) === want;
        } else if (req.mode === 'changed' || req.mode === 'unchanged') {
            const want = req.mode === 'unchanged';
            test = (i, buf, off) => buf.compare(s.prev!, i * s.size, (i + 1) * s.size, off, off + s.size) === 0 === want;
        } else {
            throw new Error('String and byte-pattern results can be refined by exact value, changed or unchanged.');
        }
        return this._sparseFromRead(meta, addrs, cur, test);
    }

    // ── Paging / live values ─────────────────────────────────────────────────

    page(offset: number, limit: number): ScanRow[] {
        const s = this._state;
        if (!s) { return []; }
        const rows: ScanRow[] = [];
        const push = (address: number, buf: Buffer, off: number) => {
            rows.push({
                address,
                previous: formatValue(buf, off, s.valueType, s.size, s.signed),
                raw: buf.subarray(off, off + s.size).toString('hex'),
            });
        };
        if (s.addrs && s.prev) {
            const end = Math.min(s.addrs.length, offset + limit);
            for (let i = offset; i < end; i++) { push(s.addrs[i], s.prev, i * s.size); }
            return rows;
        }
        if (!s.snapshot) { return rows; }
        const slots = Math.floor(s.snapshot.length / s.step);
        let seen = 0;
        for (let slot = 0; slot < slots && rows.length < limit; slot++) {
            if (s.mask && !s.mask[slot]) { continue; }
            if (seen++ < offset) { continue; }
            push((s.start + slot * s.step) >>> 0, s.snapshot, slot * s.step);
        }
        return rows;
    }

    /** Read the current value of each address, formatted like the result column. */
    async readCurrent(addresses: number[]): Promise<Record<string, { value: string; raw: string }>> {
        const s = this._state;
        const out: Record<string, { value: string; raw: string }> = {};
        if (!s || addresses.length === 0) { return out; }
        const addrs = Uint32Array.from(addresses.map(a => a >>> 0));
        const cur = await this._readValues(addrs, s.size, s.region);
        addrs.forEach((a, i) => {
            if (!cur.ok[i]) { return; }
            out[a.toString(16)] = {
                value: formatValue(cur.buf, i * s.size, s.valueType, s.size, s.signed),
                raw: cur.buf.subarray(i * s.size, (i + 1) * s.size).toString('hex'),
            };
        });
        return out;
    }

    /** Encode a user-typed value in the session's type, for WRITEMEM. */
    encodeForWrite(text: string, hex: boolean): string {
        const s = this._state;
        if (!s) { throw new Error('No scan session.'); }
        if (s.valueType === 'string' || s.valueType === 'bytes') {
            const pattern = parsePattern(s.valueType, text);
            if (pattern.bytes.some(b => b < 0)) { throw new Error('Wildcards cannot be written.'); }
            return Buffer.from(pattern.bytes).toString('hex');
        }
        const buf = Buffer.alloc(s.size);
        encodeNumber(buf, 0, parseNumber(text, s.valueType, hex, s.signed), s.valueType, s.signed);
        return buf.toString('hex');
    }

    get regionTag(): ScanRegionTag | null { return this._state?.region ?? null; }

    // ── Internals ────────────────────────────────────────────────────────────

    private _numberTest(req: ScanRequest): NumberTest {
        const type = req.valueType;
        const signed = !!req.signed;
        const a = NEEDS_VALUE.has(req.mode) ? parseNumber(req.value, type, req.hex, signed) : NaN;
        const eq = equality(type, req.value ?? '');
        switch (req.mode) {
            case 'exact':       return cur => eq(cur, a);
            case 'not':         return cur => !Number.isNaN(cur) && !eq(cur, a);
            case 'greater':     return cur => cur > a;
            case 'less':        return cur => cur < a;
            case 'between': {
                const b = parseNumber(req.value2, type, req.hex, signed);
                const lo = Math.min(a, b), hi = Math.max(a, b);
                return cur => cur >= lo && cur <= hi;
            }
            case 'changed':     return (_c, _p, cb, co, pb, po) => !!pb && cb.compare(pb, po, po + typeSize(type), co, co + typeSize(type)) !== 0;
            case 'unchanged':   return (_c, _p, cb, co, pb, po) => !!pb && cb.compare(pb, po, po + typeSize(type), co, co + typeSize(type)) === 0;
            case 'increased':   return (cur, prev) => cur > prev;
            case 'decreased':   return (cur, prev) => cur < prev;
            case 'increasedBy': return (cur, prev) => eq(cur - prev, a);
            case 'decreasedBy': return (cur, prev) => eq(prev - cur, a);
            default:            throw new Error(`Unsupported comparison "${req.mode}".`);
        }
    }

    /** Compare a fresh snapshot slot-by-slot; switches to sparse mode once the set is small enough. */
    private _denseResult(meta: SessionState, snapshot: Buffer, prevSnap: Buffer | undefined, prevMask: Uint8Array | null, test: NumberTest): SessionState {
        const { size, step, valueType, signed } = meta;
        const slots = Math.floor(snapshot.length / step);
        const mask = new Uint8Array(slots);
        let count = 0;
        for (let slot = 0; slot < slots; slot++) {
            if (prevMask && !prevMask[slot]) { continue; }
            const off = slot * step;
            if (off + size > snapshot.length) { break; }
            const cur = decodeNumber(snapshot, off, valueType, signed);
            const prev = prevSnap ? decodeNumber(prevSnap, off, valueType, signed) : NaN;
            if (test(cur, prev, snapshot, off, prevSnap, off)) { mask[slot] = 1; count++; }
        }
        if (count > SPARSE_LIMIT) {
            return { ...meta, snapshot, mask, count };
        }
        const addrs = new Uint32Array(count);
        const prev = Buffer.alloc(count * size);
        let n = 0;
        for (let slot = 0; slot < slots && n < count; slot++) {
            if (!mask[slot]) { continue; }
            addrs[n] = (meta.start + slot * step) >>> 0;
            snapshot.copy(prev, n * size, slot * step, slot * step + size);
            n++;
        }
        return { ...meta, addrs, prev, count };
    }

    private _sparseFromRead(
        meta: SessionState,
        addrs: Uint32Array,
        cur: { buf: Buffer; ok: Uint8Array },
        test: (i: number, buf: Buffer, off: number) => boolean,
    ): SessionState {
        const size = meta.size;
        const keep: number[] = [];
        for (let i = 0; i < addrs.length; i++) {
            if (cur.ok[i] && test(i, cur.buf, i * size)) { keep.push(i); }
        }
        const outAddrs = new Uint32Array(keep.length);
        const outPrev = Buffer.alloc(keep.length * size);
        keep.forEach((i, n) => {
            outAddrs[n] = addrs[i];
            cur.buf.copy(outPrev, n * size, i * size, (i + 1) * size);
        });
        return { ...meta, addrs: outAddrs, prev: outPrev, count: keep.length };
    }

    private async _readRange(start: number, end: number, region: ScanRegionTag): Promise<Buffer> {
        const { bytes } = await this.socket.readMemoryRegion(start, end, undefined, region, REGION_READ_TIMEOUT_MS);
        return Buffer.from(bytes, 'hex');
    }

    /** Re-read `size` bytes at every address: batched for small sets, one span read for large ones. */
    private async _readValues(addrs: Uint32Array, size: number, region: ScanRegionTag): Promise<{ buf: Buffer; ok: Uint8Array }> {
        const buf = Buffer.alloc(addrs.length * size);
        const ok = new Uint8Array(addrs.length);
        if (addrs.length === 0) { return { buf, ok }; }

        if (addrs.length <= BATCH_LIMIT) {
            for (let from = 0; from < addrs.length; from += BATCH_CHUNK) {
                const chunk = Array.from(addrs.subarray(from, from + BATCH_CHUNK));
                const got = await this.socket.readMemoryBatch(chunk, size, region);
                chunk.forEach((a, j) => {
                    const b = got.get(a >>> 0);
                    if (b && b.length >= size) { b.copy(buf, (from + j) * size, 0, size); ok[from + j] = 1; }
                });
            }
            return { buf, ok };
        }

        let lo = addrs[0], hi = addrs[0];
        for (const a of addrs) { if (a < lo) { lo = a; } if (a > hi) { hi = a; } }
        const span = await this._readRange(lo, hi + size, region);
        addrs.forEach((a, i) => {
            const off = a - lo;
            if (off + size <= span.length) { span.copy(buf, i * size, off, off + size); ok[i] = 1; }
        });
        return { buf, ok };
    }
}

// ── Value helpers ────────────────────────────────────────────────────────────

export function typeSize(type: ScanValueType): number {
    switch (type) {
        case 'u8':  return 1;
        case 'u16': return 2;
        default:    return 4;
    }
}

function bridgeDataType(type: ScanValueType, signed: boolean): string {
    switch (type) {
        case 'u8':  return signed ? 'int8' : 'uint8';
        case 'u16': return signed ? 'int16' : 'uint16';
        case 'f32': return 'float32';
        default:    return signed ? 'int32' : 'uint32';
    }
}

function parseNumber(text: string | undefined, type: ScanValueType, hex?: boolean, signed?: boolean): number {
    const t = (text ?? '').trim();
    if (!t) { throw new Error('Enter a value to scan for.'); }
    if (type === 'f32') {
        const f = Number(t);
        if (!Number.isFinite(f)) { throw new Error(`"${t}" is not a number.`); }
        return f;
    }
    let n: number;
    if (/^-?0x[0-9a-f]+$/i.test(t)) {
        n = t.startsWith('-') ? -parseInt(t.slice(3), 16) : parseInt(t.slice(2), 16);
    } else if (hex) {
        if (!/^[0-9a-f]+$/i.test(t)) { throw new Error(`"${t}" is not a hex value.`); }
        n = parseInt(t, 16);
    } else {
        if (!/^-?\d+$/.test(t)) { throw new Error(`"${t}" is not a whole number (tick Hex for hex input).`); }
        n = parseInt(t, 10);
    }
    const bits = typeSize(type) * 8;
    const max = 2 ** bits;
    if (n < -(max / 2) || n >= max) { throw new Error(`${t} does not fit in ${bits} bits.`); }
    // Keep the number in the session's signedness so comparisons line up with decoded memory.
    if (signed && n >= max / 2) { n -= max; }
    if (!signed && n < 0) { n += max; }
    return n;
}

/** Float equality rounds to the precision the user typed (Cheat Engine's "rounded" mode). */
function equality(type: ScanValueType, typed: string): (a: number, b: number) => boolean {
    if (type !== 'f32') { return (a, b) => a === b; }
    const dot = typed.trim().indexOf('.');
    const decimals = dot < 0 ? 0 : typed.trim().length - dot - 1;
    const tolerance = 0.5 * 10 ** -decimals;
    return (a, b) => Math.abs(a - b) < tolerance;
}

function decodeNumber(buf: Buffer, off: number, type: ScanValueType, signed: boolean): number {
    switch (type) {
        case 'u8':  return signed ? buf.readInt8(off) : buf.readUInt8(off);
        case 'u16': return signed ? buf.readInt16LE(off) : buf.readUInt16LE(off);
        case 'f32': return buf.readFloatLE(off);
        default:    return signed ? buf.readInt32LE(off) : buf.readUInt32LE(off);
    }
}

function encodeNumber(buf: Buffer, off: number, value: number, type: ScanValueType, signed: boolean): void {
    switch (type) {
        case 'u8':  signed ? buf.writeInt8(value, off) : buf.writeUInt8(value, off); break;
        case 'u16': signed ? buf.writeInt16LE(value, off) : buf.writeUInt16LE(value, off); break;
        case 'f32': buf.writeFloatLE(value, off); break;
        default:    signed ? buf.writeInt32LE(value, off) : buf.writeUInt32LE(value >>> 0, off); break;
    }
}

function formatValue(buf: Buffer, off: number, type: ScanValueType, size: number, signed: boolean): string {
    if (type === 'string') {
        return buf.subarray(off, off + size).toString('latin1').replace(/[^\x20-\x7e]/g, '.');
    }
    if (type === 'bytes') {
        return buf.subarray(off, off + size).toString('hex').toUpperCase().replace(/(..)(?=.)/g, '$1 ');
    }
    if (type === 'f32') {
        const f = buf.readFloatLE(off);
        return Number.isFinite(f) ? String(+f.toPrecision(7)) : String(f);
    }
    return String(decodeNumber(buf, off, type, signed));
}

/** Parse a string (as ASCII) or a byte pattern like "DE AD ?? EF" into SCANPATTERN bytes. */
function parsePattern(type: ScanValueType, text: string): Pattern {
    if (type === 'string') {
        if (!text) { throw new Error('Enter the text to search for.'); }
        return { bytes: Array.from(Buffer.from(text, 'latin1')) };
    }
    const compact = text.replace(/\s+/g, '');
    if (!compact) { throw new Error('Enter a byte pattern, e.g. "DE AD ?? EF".'); }
    if (compact.length % 2 !== 0) { throw new Error('Byte patterns need two hex digits per byte.'); }
    const bytes: number[] = [];
    for (let i = 0; i < compact.length; i += 2) {
        const pair = compact.slice(i, i + 2);
        if (pair === '??' || pair === '**') { bytes.push(-1); continue; }
        if (!/^[0-9a-f]{2}$/i.test(pair)) { throw new Error(`"${pair}" is not a hex byte (use ?? for a wildcard).`); }
        bytes.push(parseInt(pair, 16));
    }
    if (bytes.every(b => b < 0)) { throw new Error('A pattern cannot be all wildcards.'); }
    return { bytes };
}

function matchesPattern(buf: Buffer, off: number, pattern: Pattern): boolean {
    for (let j = 0; j < pattern.bytes.length; j++) {
        const want = pattern.bytes[j];
        if (want >= 0 && buf[off + j] !== want) { return false; }
    }
    return true;
}
