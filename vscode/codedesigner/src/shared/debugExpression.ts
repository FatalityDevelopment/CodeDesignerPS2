// ═══════════════════════════════════════════════════════════════════════════
// Debugger expressions
// ═══════════════════════════════════════════════════════════════════════════
//
// A small expression language shared by breakpoint conditions, log-point
// messages and the memory watch list:
//
//   $a0 == 0x10 && u8[$sp+8] != 0      registers, memory, C operators
//   [[0x0049A2C0]+0x10]+4              pointer chains ([x] = u32 at x)
//   f32[$a1+0x30] > 1.5                typed reads: u8 u16 u32 s8 s16 s32 f32
//   playerBase + 0x1044                label names (via resolveSymbol)
//
// Registers may be written with or without the '$' (a0, $a0, r4, pc, hi, lo,
// sa, f0-f31). Values are JS numbers; integer operators work on the unsigned
// 32-bit value.

export interface DebugRegisterSnapshot {
    pc?: number;
    hi?: number;
    lo?: number;
    sa?: number;
    gpr?: number[];
    gprLo32?: number[];
    fpr?: number[];
}

export interface DebugExprContext {
    registers?: DebugRegisterSnapshot | null;
    /** Reads `length` bytes at `address` (EE address space). */
    readMemory?: (address: number, length: number) => Promise<Uint8Array>;
    /** Resolves a bare identifier that is not a register (e.g. a label name). */
    resolveSymbol?: (name: string) => number | undefined;
}

export type MemoryReadType = 'u8' | 'u16' | 'u32' | 's8' | 's16' | 's32' | 'f32';

const GPR_NAMES = [
    'zero', 'at', 'v0', 'v1', 'a0', 'a1', 'a2', 'a3',
    't0', 't1', 't2', 't3', 't4', 't5', 't6', 't7',
    's0', 's1', 's2', 's3', 's4', 's5', 's6', 's7',
    't8', 't9', 'k0', 'k1', 'gp', 'sp', 'fp', 'ra',
];

const READ_SIZES: Record<MemoryReadType, number> = {
    u8: 1, s8: 1, u16: 2, s16: 2, u32: 4, s32: 4, f32: 4,
};

// ── AST ──────────────────────────────────────────────────────────────────────

type Node =
    | { kind: 'num'; value: number }
    | { kind: 'ident'; name: string }
    | { kind: 'unary'; op: string; operand: Node }
    | { kind: 'binary'; op: string; left: Node; right: Node }
    | { kind: 'mem'; type: MemoryReadType; address: Node };

// ── Tokenizer ────────────────────────────────────────────────────────────────

interface Token { type: 'num' | 'ident' | 'op' | 'end'; text: string; value?: number; pos: number }

const OPERATORS = ['||', '&&', '==', '!=', '<=', '>=', '<<', '>>', '|', '^', '&', '<', '>',
    '+', '-', '*', '/', '%', '!', '~', '(', ')', '[', ']'];

function tokenize(src: string): Token[] {
    const tokens: Token[] = [];
    let i = 0;
    while (i < src.length) {
        const ch = src[i];
        if (/\s/.test(ch)) { i++; continue; }
        const start = i;
        if (/[0-9.]/.test(ch)) {
            let m = /^0x[0-9a-f]+/i.exec(src.slice(i));
            if (m) {
                tokens.push({ type: 'num', text: m[0], value: parseInt(m[0], 16), pos: start });
                i += m[0].length;
                continue;
            }
            m = /^(\d+\.\d*|\.\d+|\d+)(e[+-]?\d+)?/i.exec(src.slice(i));
            if (m) {
                tokens.push({ type: 'num', text: m[0], value: Number(m[0]), pos: start });
                i += m[0].length;
                continue;
            }
        }
        if (/[$A-Za-z_@]/.test(ch)) {
            const m = /^[$@]?[A-Za-z_][A-Za-z0-9_.]*/.exec(src.slice(i));
            if (m) {
                tokens.push({ type: 'ident', text: m[0], pos: start });
                i += m[0].length;
                continue;
            }
        }
        const op = OPERATORS.find(o => src.startsWith(o, i));
        if (op) {
            tokens.push({ type: 'op', text: op, pos: start });
            i += op.length;
            continue;
        }
        throw new Error(`Unexpected '${ch}' at column ${i + 1}`);
    }
    tokens.push({ type: 'end', text: '', pos: src.length });
    return tokens;
}

// ── Parser (precedence climbing, C precedence) ──────────────────────────────

const BINARY_PRECEDENCE: Record<string, number> = {
    '||': 1, '&&': 2, '|': 3, '^': 4, '&': 5,
    '==': 6, '!=': 6, '<': 7, '<=': 7, '>': 7, '>=': 7,
    '<<': 8, '>>': 8, '+': 9, '-': 9, '*': 10, '/': 10, '%': 10,
};

class Parser {
    private pos = 0;
    constructor(private readonly tokens: Token[]) {}

    parse(): Node {
        const node = this.parseBinary(1);
        const t = this.peek();
        if (t.type !== 'end') { throw new Error(`Unexpected '${t.text}' at column ${t.pos + 1}`); }
        return node;
    }

    private peek(): Token { return this.tokens[this.pos]; }
    private next(): Token { return this.tokens[this.pos++]; }

    private expect(text: string): void {
        const t = this.next();
        if (t.text !== text) {
            throw new Error(t.type === 'end' ? `Missing '${text}'` : `Expected '${text}' at column ${t.pos + 1}`);
        }
    }

    private parseBinary(minPrec: number): Node {
        let left = this.parseUnary();
        for (;;) {
            const t = this.peek();
            const prec = t.type === 'op' ? BINARY_PRECEDENCE[t.text] : undefined;
            if (prec === undefined || prec < minPrec) { return left; }
            this.next();
            const right = this.parseBinary(prec + 1);
            left = { kind: 'binary', op: t.text, left, right };
        }
    }

    private parseUnary(): Node {
        const t = this.peek();
        if (t.type === 'op' && (t.text === '-' || t.text === '!' || t.text === '~' || t.text === '+')) {
            this.next();
            return { kind: 'unary', op: t.text, operand: this.parseUnary() };
        }
        return this.parsePrimary();
    }

    private parsePrimary(): Node {
        const t = this.next();
        if (t.type === 'num') { return { kind: 'num', value: t.value! }; }
        if (t.type === 'op' && t.text === '(') {
            const inner = this.parseBinary(1);
            this.expect(')');
            return inner;
        }
        if (t.type === 'op' && t.text === '[') {
            const address = this.parseBinary(1);
            this.expect(']');
            return { kind: 'mem', type: 'u32', address };
        }
        if (t.type === 'ident') {
            const lower = t.text.toLowerCase();
            if (lower in READ_SIZES && this.peek().text === '[') {
                this.next();
                const address = this.parseBinary(1);
                this.expect(']');
                return { kind: 'mem', type: lower as MemoryReadType, address };
            }
            return { kind: 'ident', name: t.text };
        }
        throw new Error(t.type === 'end' ? 'Expression is incomplete' : `Unexpected '${t.text}' at column ${t.pos + 1}`);
    }
}

// ── Public API ───────────────────────────────────────────────────────────────

export class DebugExpression {
    private constructor(readonly source: string, private readonly root: Node) {}

    /** Parses `source`; throws an Error with a readable message on bad syntax. */
    static parse(source: string): DebugExpression {
        return new DebugExpression(source, new Parser(tokenize(source)).parse());
    }

    /** Returns the syntax error message for `source`, or undefined when valid. */
    static validate(source: string): string | undefined {
        try { DebugExpression.parse(source); return undefined; }
        catch (err: any) { return err?.message ?? String(err); }
    }

    /** True when evaluating needs a memory read. */
    get readsMemory(): boolean {
        const walk = (n: Node): boolean =>
            n.kind === 'mem' ||
            (n.kind === 'unary' && walk(n.operand)) ||
            (n.kind === 'binary' && (walk(n.left) || walk(n.right)));
        return walk(this.root);
    }

    evaluate(ctx: DebugExprContext): Promise<number> {
        return evalNode(this.root, ctx);
    }
}

/** Reads one typed value at `address`. */
export async function readTypedValue(ctx: DebugExprContext, type: MemoryReadType, address: number): Promise<number> {
    if (!ctx.readMemory) { throw new Error('Memory is not available'); }
    const bytes = await ctx.readMemory(address >>> 0, READ_SIZES[type]);
    if (bytes.length < READ_SIZES[type]) { throw new Error(`Cannot read 0x${(address >>> 0).toString(16).toUpperCase()}`); }
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    switch (type) {
        case 'u8':  return view.getUint8(0);
        case 's8':  return view.getInt8(0);
        case 'u16': return view.getUint16(0, true);
        case 's16': return view.getInt16(0, true);
        case 'u32': return view.getUint32(0, true);
        case 's32': return view.getInt32(0, true);
        case 'f32': return view.getFloat32(0, true);
    }
}

function registerValue(name: string, regs: DebugRegisterSnapshot): number | undefined {
    const n = name.replace(/^\$/, '').toLowerCase();
    if (n === 'pc') { return (regs.pc ?? 0) >>> 0; }
    if (n === 'hi') { return (regs.hi ?? 0) >>> 0; }
    if (n === 'lo') { return (regs.lo ?? 0) >>> 0; }
    if (n === 'sa') { return (regs.sa ?? 0) >>> 0; }
    const gpr = regs.gprLo32 ?? regs.gpr ?? [];
    let idx = GPR_NAMES.indexOf(n === 's8' ? 'fp' : n);
    if (idx < 0) {
        const m = /^r(\d{1,2})$/.exec(n);
        if (m && Number(m[1]) < 32) { idx = Number(m[1]); }
    }
    if (idx >= 0) { return (gpr[idx] ?? 0) >>> 0; }
    const f = /^f(\d{1,2})$/.exec(n);
    if (f && Number(f[1]) < 32) {
        const bits = (regs.fpr ?? [])[Number(f[1])] ?? 0;
        const view = new DataView(new ArrayBuffer(4));
        view.setUint32(0, bits >>> 0, true);
        return view.getFloat32(0, true);
    }
    return undefined;
}

const toU32 = (v: number) => Number.isFinite(v) ? (Math.trunc(v) >>> 0) : 0;

async function evalNode(node: Node, ctx: DebugExprContext): Promise<number> {
    switch (node.kind) {
        case 'num':
            return node.value;
        case 'ident': {
            const bare = node.name.replace(/^[$@]/, '');
            if (!node.name.startsWith('@') && ctx.registers) {
                const reg = registerValue(node.name, ctx.registers);
                if (reg !== undefined) { return reg; }
            }
            const sym = ctx.resolveSymbol?.(bare);
            if (sym !== undefined) { return sym >>> 0; }
            if (!ctx.registers && registerValue(node.name, {}) !== undefined) {
                throw new Error(`Register ${node.name} needs a paused CPU`);
            }
            throw new Error(`Unknown name '${node.name}'`);
        }
        case 'mem':
            return readTypedValue(ctx, node.type, toU32(await evalNode(node.address, ctx)));
        case 'unary': {
            const v = await evalNode(node.operand, ctx);
            switch (node.op) {
                case '-': return -v;
                case '+': return v;
                case '!': return v ? 0 : 1;
                case '~': return (~toU32(v)) >>> 0;
            }
            throw new Error(`Bad operator ${node.op}`);
        }
        case 'binary': {
            if (node.op === '&&') { return (await evalNode(node.left, ctx)) ? ((await evalNode(node.right, ctx)) ? 1 : 0) : 0; }
            if (node.op === '||') { return (await evalNode(node.left, ctx)) ? 1 : ((await evalNode(node.right, ctx)) ? 1 : 0); }
            const a = await evalNode(node.left, ctx);
            const b = await evalNode(node.right, ctx);
            const ints = Number.isInteger(a) && Number.isInteger(b);
            switch (node.op) {
                case '+': return ints ? ((a + b) >>> 0) : a + b;
                case '-': return ints ? ((a - b) >>> 0) : a - b;
                case '*': return ints ? Number((BigInt(toU32(a)) * BigInt(toU32(b))) & 0xFFFFFFFFn) : a * b;
                case '/': if (b === 0) { throw new Error('Division by zero'); } return ints ? Math.trunc(a / b) >>> 0 : a / b;
                case '%': if (b === 0) { throw new Error('Division by zero'); } return ints ? (a % b) >>> 0 : a % b;
                case '<<': return (toU32(a) << (toU32(b) & 31)) >>> 0;
                case '>>': return toU32(a) >>> (toU32(b) & 31);
                case '&': return (toU32(a) & toU32(b)) >>> 0;
                case '|': return (toU32(a) | toU32(b)) >>> 0;
                case '^': return (toU32(a) ^ toU32(b)) >>> 0;
                case '==': return a === b ? 1 : 0;
                case '!=': return a !== b ? 1 : 0;
                case '<': return a < b ? 1 : 0;
                case '<=': return a <= b ? 1 : 0;
                case '>': return a > b ? 1 : 0;
                case '>=': return a >= b ? 1 : 0;
            }
            throw new Error(`Bad operator ${node.op}`);
        }
    }
}

// ── Hit conditions & log messages ───────────────────────────────────────────

/**
 * Validates a hit-count condition: "5" / ">=5" (stop from the 5th hit on),
 * "==5" (only the 5th), ">5", "%5" (every 5th). Empty means always.
 */
export function validateHitCondition(text: string): string | undefined {
    const t = text.trim();
    if (!t) { return undefined; }
    return /^(>=|==|>|%)?\s*\d+$/.test(t) ? undefined : 'Use N, >=N, ==N, >N or %N';
}

/** Whether the `hits`-th hit (1-based) satisfies the hit condition. */
export function hitConditionMet(text: string, hits: number): boolean {
    const m = /^(>=|==|>|%)?\s*(\d+)$/.exec(text.trim());
    if (!m) { return true; }
    const n = Number(m[2]);
    switch (m[1]) {
        case '==': return hits === n;
        case '>':  return hits > n;
        case '%':  return n > 0 && hits % n === 0;
        default:   return hits >= n;
    }
}

/** Expands `{expr}` and `{expr,fmt}` (fmt = x, d, f) in a log-point message. */
export async function formatLogMessage(template: string, ctx: DebugExprContext): Promise<string> {
    const parts = template.split(/(\{[^{}]*\})/);
    const out: string[] = [];
    for (const part of parts) {
        if (!part.startsWith('{') || !part.endsWith('}')) { out.push(part); continue; }
        const [expr, fmt] = part.slice(1, -1).split(',').map(s => s.trim());
        try {
            const v = await DebugExpression.parse(expr).evaluate(ctx);
            out.push(formatNumber(v, fmt));
        } catch (err: any) {
            out.push(`<${err?.message ?? err}>`);
        }
    }
    return out.join('');
}

export function formatNumber(v: number, fmt?: string): string {
    if (fmt === 'd') { return String(Number.isInteger(v) ? (v | 0) : v); }
    if (fmt === 'f' || !Number.isInteger(v)) { return String(v); }
    return '0x' + (v >>> 0).toString(16).toUpperCase().padStart(8, '0');
}
