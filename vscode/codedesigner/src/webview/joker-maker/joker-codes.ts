// Joker (button-activator) code helpers: value encoding, decoding, and
// finding a game's joker address from its archive cheats.

export type ButtonName =
    | 'l2' | 'l1' | 'r2' | 'r1'
    | 'up' | 'down' | 'left' | 'right'
    | 'select' | 'start'
    | 'triangle' | 'square' | 'circle' | 'cross'
    | 'l3' | 'r3';

/** PS2 pad word bits (active-high view; the game reads them active-low). */
export const JOKER_MASK: Record<ButtonName, number> = {
    select: 0x0001, l3: 0x0002, r3: 0x0004, start: 0x0008,
    up: 0x0010, right: 0x0020, down: 0x0040, left: 0x0080,
    l2: 0x0100, r2: 0x0200, l1: 0x0400, r1: 0x0800,
    triangle: 0x1000, circle: 0x2000, cross: 0x4000, square: 0x8000,
};

/** Display order: shoulders, d-pad, face, centre, sticks. */
export const BUTTON_ORDER: readonly ButtonName[] = [
    'l1', 'l2', 'r1', 'r2',
    'up', 'down', 'left', 'right',
    'triangle', 'circle', 'cross', 'square',
    'select', 'start', 'l3', 'r3',
];

export const BUTTON_LABEL: Record<ButtonName, string> = {
    l1: 'L1', l2: 'L2', r1: 'R1', r2: 'R2',
    up: 'Up', down: 'Down', left: 'Left', right: 'Right',
    triangle: 'Triangle', circle: 'Circle', cross: 'X', square: 'Square',
    select: 'Select', start: 'Start', l3: 'L3', r3: 'R3',
};

/** How the game's pad word stores a press. */
export type Polarity = 'low' | 'high';

export interface JokerEncoding {
    polarity: Polarity;
    /** Pad word stored big-endian (byte-swapped) in this game. */
    swap: boolean;
}

/** Highest physical EE RAM address a code line can target. */
const MAX_EE_ADDRESS = 0x01FFFFFF;

const swap16 = (v: number) => ((v & 0xFF) << 8) | ((v >> 8) & 0xFF);

export function sortButtons(names: Iterable<ButtonName>): ButtonName[] {
    const set = new Set(names);
    return BUTTON_ORDER.filter(n => set.has(n));
}

export function comboLabel(names: readonly ButtonName[]): string {
    return names.length ? sortButtons(names).map(n => BUTTON_LABEL[n]).join(' + ') : '(none)';
}

/** Buttons -> the 16-bit value a joker compare line checks for. */
export function encodeJoker(names: readonly ButtonName[], enc: JokerEncoding): number {
    let bits = 0;
    for (const n of names) { bits |= JOKER_MASK[n]; }
    let v = enc.polarity === 'low' ? (~bits & 0xFFFF) : bits;
    if (enc.swap) { v = swap16(v); }
    return v;
}

/** 16-bit joker value -> buttons it stands for. */
export function decodeJoker(value: number, enc: JokerEncoding): ButtonName[] {
    let v = value & 0xFFFF;
    if (enc.swap) { v = swap16(v); }
    const bits = enc.polarity === 'low' ? (~v & 0xFFFF) : v;
    return BUTTON_ORDER.filter(n => (bits & JOKER_MASK[n]) !== 0);
}

/** Buttons pressed in a raw PS2 (active-low) pad word, as PADGETSTATE returns it. */
export function buttonsFromPadWord(word: number): ButtonName[] {
    return BUTTON_ORDER.filter(n => (word & JOKER_MASK[n]) === 0);
}

export const hex = (v: number, digits: number) =>
    (v >>> 0).toString(16).toUpperCase().padStart(digits, '0');

// Archive analysis

export interface JokerUse {
    cheat: string;
    value: number;
}

export interface JokerCandidate {
    /** Physical EE address (25-bit). */
    address: number;
    uses:    JokerUse[];
    /** Polarity most of this address's values agree with. */
    polarity: Polarity;
}

/** One `AAAAAAAA VVVVVVVV` code line. */
const CODE_LINE = /^\s*([0-9a-f]{8})\s+([0-9a-f]{8})\s*$/i;

/**
 * Collect 16-bit "equal" compare lines (D-type single-line and E-type
 * multi-line) and rank their addresses as joker candidates. A value only
 * counts if it looks like a button combo: a handful of bits set (active-high)
 * or a handful clear (active-low). Ordinary game-state compares like
 * `== 0x0003` fall out because they rarely share one address with many cheats.
 */
export function findJokerCandidates(cheats: ReadonlyArray<{ name: string; codes: readonly string[] }>): JokerCandidate[] {
    const byAddr = new Map<number, JokerUse[]>();
    const add = (address: number, value: number, cheat: string) => {
        if (address > MAX_EE_ADDRESS) { return; }
        let list = byAddr.get(address);
        if (!list) { byAddr.set(address, list = []); }
        if (!list.some(u => u.cheat === cheat && u.value === value)) { list.push({ cheat, value }); }
    };

    for (const cheat of cheats) {
        for (const line of cheat.codes) {
            const m = CODE_LINE.exec(line);
            if (!m) { continue; }
            const a = parseInt(m[1], 16) >>> 0;
            const b = parseInt(m[2], 16) >>> 0;
            const op = a >>> 28;
            if (op === 0xD && (b >>> 16) === 0) {
                // Daaaaaaa 0000vvvv: 16-bit equal, gates the next line.
                add(a & MAX_EE_ADDRESS, b & 0xFFFF, cheat.name);
            } else if (op === 0xE && ((a >>> 24) & 0xF) === 0 && (b >>> 28) === 0) {
                // E0nnvvvv 0aaaaaaa: 16-bit equal, gates the next nn lines.
                add(b & MAX_EE_ADDRESS, a & 0xFFFF, cheat.name);
            }
        }
    }

    const out: JokerCandidate[] = [];
    for (const [address, uses] of byAddr) {
        let low = 0, high = 0;
        for (const u of uses) {
            const bits = popcount(u.value);
            if (bits >= 12 && bits < 16) { low++; }
            else if (bits >= 1 && bits <= 4) { high++; }
        }
        if (low + high === 0) { continue; }
        out.push({ address, uses, polarity: low >= high ? 'low' : 'high' });
    }
    out.sort((x, y) => y.uses.length - x.uses.length || x.address - y.address);
    return out;
}

function popcount(v: number): number {
    let c = 0;
    for (let x = v & 0xFFFF; x; x &= x - 1) { c++; }
    return c;
}

/** Parse a user-typed address: `0x...`, an EE mirror (`20...`), or a `D0...` line prefix. */
export function parseAddress(text: string): number | null {
    const t = text.trim().replace(/^0x/i, '').split(/\s+/)[0] ?? '';
    if (!/^[0-9a-f]{1,8}$/i.test(t)) { return null; }
    return (parseInt(t, 16) >>> 0) & MAX_EE_ADDRESS;
}

// Code output

export type WrapFormat = 'auto' | 'each';

/** Pull valid `AAAAAAAA VVVVVVVV` lines out of free text (comments and blanks dropped). */
export function parseCodeLines(text: string): string[] {
    const out: string[] = [];
    for (const raw of text.split(/\r?\n/)) {
        const m = CODE_LINE.exec(raw.replace(/\/\/.*$|#.*$/, ''));
        if (m) { out.push(`${m[1].toUpperCase()} ${m[2].toUpperCase()}`); }
    }
    return out;
}

/**
 * Build the activator. No body: the bare D line. One body line, or `each`:
 * a D line before every line. Otherwise one E line gating the whole body.
 */
export function buildJokerCode(address: number, value: number, body: readonly string[], format: WrapFormat): string[] {
    const d = `D${hex(address, 7)} 0000${hex(value, 4)}`;
    if (body.length === 0) { return [d]; }
    if (format === 'each' || body.length === 1) {
        return body.flatMap(l => [d, l]);
    }
    const lines: string[] = [];
    // E0nn holds at most 255 gated lines; chain blocks for longer bodies.
    for (let i = 0; i < body.length; i += 0xFF) {
        const chunk = body.slice(i, i + 0xFF);
        lines.push(`E0${hex(chunk.length, 2)}${hex(value, 4)} 0${hex(address, 7)}`, ...chunk);
    }
    return lines;
}
