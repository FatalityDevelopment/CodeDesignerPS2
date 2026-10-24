import {
    DebugExpression,
    DebugExprContext,
    formatLogMessage,
    hitConditionMet,
    validateHitCondition,
} from '../../shared/debugExpression';

/**
 * One execution breakpoint as the user configured it. Disabled breakpoints
 * stay in the list but are not armed in the emulator.
 */
export interface BreakpointInfo {
    address: number;
    enabled: boolean;
    /** Expression that must be non-zero for the breakpoint to stop (empty = always). */
    condition: string;
    /** Hit-count condition: "N", ">=N", "==N", ">N" or "%N" (empty = always). */
    hitCondition: string;
    /** When set, the breakpoint is a log point: it prints this message and continues. */
    logMessage: string;
    /** Hits that passed the condition since the breakpoint was set or reset. */
    hits: number;
    /** Syntax or evaluation error from the condition, hit condition or message. */
    error?: string;
}

export type BreakpointPatch = Partial<Pick<BreakpointInfo, 'enabled' | 'condition' | 'hitCondition' | 'logMessage'>>;

/** What the debugger should do with a pause at a breakpoint. */
export type HitDecision =
    | { action: 'stop' }
    | { action: 'continue'; log?: string };

const toPhys = (addr: number): number => {
    const v = addr >>> 0;
    if (v >= 0x80000000 && v <= 0xBFFFFFFF) { return (v & 0x1FFFFFFF) >>> 0; }
    if (v >= 0x20000000 && v <= 0x21FFFFFF) { return (v - 0x20000000) >>> 0; }
    if (v >= 0x30000000 && v <= 0x31FFFFFF) { return (v - 0x30000000) >>> 0; }
    return v;
};

/**
 * Breakpoint list kept by the extension host. The emulator only knows plain
 * execute breakpoints, so conditions, hit counts and log points are applied
 * here when a pause at a breakpoint is reported.
 */
export class BreakpointManager {
    private readonly _items = new Map<number, BreakpointInfo>();
    /** Address of the one-shot "Run to Cursor" breakpoint, if armed. */
    private _runToAddress: number | undefined;

    list(): BreakpointInfo[] {
        return [...this._items.values()].sort((a, b) => toPhys(a.address) - toPhys(b.address));
    }

    get(address: number): BreakpointInfo | undefined {
        return this._items.get(address >>> 0) ?? this.findByPhys(address);
    }

    findByPhys(address: number): BreakpointInfo | undefined {
        const phys = toPhys(address);
        for (const bp of this._items.values()) {
            if (toPhys(bp.address) === phys) { return bp; }
        }
        return undefined;
    }

    /** Addresses that should be armed in the emulator. */
    armedAddresses(): Set<number> {
        const set = new Set<number>();
        for (const bp of this._items.values()) {
            if (bp.enabled) { set.add(bp.address); }
        }
        if (this._runToAddress !== undefined) { set.add(this._runToAddress); }
        return set;
    }

    /** Adds a breakpoint (enabled, unconditional) unless one exists at the address. */
    add(address: number, patch: BreakpointPatch = {}): BreakpointInfo {
        const existing = this.get(address);
        if (existing) { return this.update(existing.address, patch) ?? existing; }
        const bp: BreakpointInfo = {
            address: address >>> 0,
            enabled: true,
            condition: '',
            hitCondition: '',
            logMessage: '',
            hits: 0,
        };
        this._items.set(bp.address, bp);
        return this.update(bp.address, patch) ?? bp;
    }

    remove(address: number): BreakpointInfo | undefined {
        const bp = this.get(address);
        if (bp) { this._items.delete(bp.address); }
        return bp;
    }

    clear(): void {
        this._items.clear();
    }

    update(address: number, patch: BreakpointPatch): BreakpointInfo | undefined {
        const bp = this.get(address);
        if (!bp) { return undefined; }
        if (patch.enabled !== undefined) { bp.enabled = patch.enabled; }
        if (patch.condition !== undefined) { bp.condition = patch.condition.trim(); }
        if (patch.hitCondition !== undefined) { bp.hitCondition = patch.hitCondition.trim(); }
        if (patch.logMessage !== undefined) { bp.logMessage = patch.logMessage; }
        if (patch.condition !== undefined || patch.hitCondition !== undefined) { bp.hits = 0; }
        bp.error = BreakpointManager._validate(bp);
        return bp;
    }

    resetHits(address?: number): void {
        for (const bp of this._items.values()) {
            if (address === undefined || toPhys(bp.address) === toPhys(address)) { bp.hits = 0; }
        }
    }

    // ── Run to cursor ───────────────────────────────────────────────────────

    get runToAddress(): number | undefined { return this._runToAddress; }

    setRunTo(address: number | undefined): void {
        this._runToAddress = address === undefined ? undefined : address >>> 0;
    }

    /** True when a run-to address is armed and has no user breakpoint of its own. */
    runToNeedsClear(): boolean {
        if (this._runToAddress === undefined) { return false; }
        const bp = this.findByPhys(this._runToAddress);
        return !bp || !bp.enabled;
    }

    // ── Hit handling ────────────────────────────────────────────────────────

    /**
     * Decide whether a pause at `hitAddress` should stop or continue, and
     * count the hit. Evaluation errors stop (with `error` set) so a typo in a
     * condition never runs past a breakpoint silently.
     */
    async decide(hitAddress: number, ctx: DebugExprContext): Promise<HitDecision> {
        const bp = this.findByPhys(hitAddress);
        if (!bp || !bp.enabled) { return { action: 'stop' }; }

        if (bp.condition) {
            try {
                const value = await DebugExpression.parse(bp.condition).evaluate(ctx);
                bp.error = undefined;
                if (!value) { return { action: 'continue' }; }
            } catch (err: any) {
                bp.error = `Condition: ${err?.message ?? err}`;
                return { action: 'stop' };
            }
        }

        bp.hits++;
        if (bp.hitCondition && !hitConditionMet(bp.hitCondition, bp.hits)) {
            return { action: 'continue' };
        }

        if (bp.logMessage) {
            const text = await formatLogMessage(bp.logMessage, ctx);
            return { action: 'continue', log: text };
        }
        return { action: 'stop' };
    }

    /** True when stopping at this address needs anything beyond a plain break. */
    needsEvaluation(hitAddress: number): boolean {
        const bp = this.findByPhys(hitAddress);
        return !!bp && bp.enabled && (!!bp.condition || !!bp.hitCondition || !!bp.logMessage);
    }

    private static _validate(bp: BreakpointInfo): string | undefined {
        if (bp.condition) {
            const err = DebugExpression.validate(bp.condition);
            if (err) { return `Condition: ${err}`; }
        }
        const hitErr = validateHitCondition(bp.hitCondition);
        if (hitErr) { return `Hit count: ${hitErr}`; }
        return undefined;
    }
}
