import {
    Component,
    ChangeDetectionStrategy,
    ViewEncapsulation,
    computed,
    input,
    output,
    signal,
} from '@angular/core';
import { IconComponent } from '../shared/icon.component';
import {
    APP_RT_TYPES,
    CapturedPacket,
    CatalogEntry,
    CatalogField,
    DissectedLayer,
    PacketViewerAction,
    ParsedPacketRow,
    b64ToBytes,
    bytesToB64,
    bytesToHex,
    hexToBytes,
} from './types';

type Direction = 'rx' | 'tx';

interface Preview {
    frame: string;
    layers: DissectedLayer[];
    row?: ParsedPacketRow;
    message: string;
}

interface LogEntry {
    time: string;
    ok: boolean;
    text: string;
}

/** Saved composer settings (see PacketViewerPageComponent's webview state). */
export interface ComposerSettings {
    direction?: Direction;
    fix?: boolean;
    tcpContinue?: boolean;
    protocol?: string;
}

/** Protocols the message builder offers (Object classes aren't messages). */
const BUILD_PROTOCOLS = ['Game', 'DME', 'Lobby', 'LobbyExt', 'MGCL', 'SCERT'];

/**
 * Packet composer: edit a frame (from a captured packet or pasted hex), build a server message
 * from the catalog into it, preview the bridge's dissection, and inject it into the PS2 (RX) or
 * send it from the PS2 (TX) through NETINJECT / NETINJECTTX.
 */
@Component({
    selector: 'app-packet-composer',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    encapsulation: ViewEncapsulation.None,
    imports: [IconComponent],
    template: `
<section class="pv-card pv-composer">
    <div class="pv-card-head">
        <span class="pv-section-title">Composer</span>
        <span class="pv-count">{{ frameInfo() }}</span>
        <span class="pv-head-actions">
            <button class="lm-tool-btn" [disabled]="!selected()" title="Copy the selected packet into the composer"
                    (click)="loadSelected()">Load selected</button>
            <button class="pv-icon-btn" title="Close the composer" (click)="closed.emit()"><app-icon name="x" [size]="13" /></button>
        </span>
    </div>

    <div class="pv-composer-body">
        <!-- ── Direction + options ─────────────────────────────────────── -->
        <div class="pv-row">
            <div class="pv-seg" role="radiogroup" aria-label="Direction">
                <button [class.pv-seg-on]="direction() === 'rx'" (click)="setDirection('rx')"
                        title="NETINJECT: the frame arrives on the PS2's network input, as if the server sent it">To PS2</button>
                <button [class.pv-seg-on]="direction() === 'tx'" (click)="setDirection('tx')"
                        title="NETINJECTTX: the frame leaves through the DEV9 adapter, as if the PS2 sent it">From PS2</button>
            </div>
            <label class="pv-check" title="Recompute the IPv4 length/checksum and the TCP/UDP length/checksum">
                <input type="checkbox" [checked]="fix()" (change)="fix.set(asInput($event).checked); changed()" /> Fix checksums
            </label>
            <label class="pv-check" title="Rewrite TCP seq/ack to follow the live connection (needs a running capture of it)">
                <input type="checkbox" [checked]="tcpContinue()" (change)="tcpContinue.set(asInput($event).checked); changed()" /> Continue TCP flow
            </label>
        </div>

        <!-- ── Frame hex ──────────────────────────────────────────────── -->
        <div class="pv-field-label">Frame (hex)</div>
        <textarea class="pv-hex-input" spellcheck="false" [class.pv-invalid]="hexError()"
                  placeholder="Select a packet and click Load selected, or paste an Ethernet frame as hex"
                  [value]="frameHex()" (input)="frameHex.set(asTextarea($event).value); changed()"></textarea>
        @if (hexError()) {
            <div class="pv-note pv-note-err">{{ hexError() }}</div>
        }

        <!-- ── Preview ────────────────────────────────────────────────── -->
        @if (preview(); as p) {
            <div class="pv-preview">
                <div class="pv-preview-head">
                    @if (p.row) {
                        <span class="pv-proto">{{ p.row.proto }}</span>
                        @if (p.row.server) { <span class="pv-server">{{ p.row.server }}</span> }
                        <span class="pv-preview-info" [title]="p.row.info">{{ p.row.info }}</span>
                    }
                </div>
                <div class="pv-preview-layers">
                    @for (l of p.layers; track $index) {
                        <span class="pv-chip"><span class="pv-tree-dot" [style.background]="l.color"></span>{{ l.label }}</span>
                    }
                </div>
            </div>
        } @else if (previewError()) {
            <div class="pv-note pv-note-err">{{ previewError() }}</div>
        }
        @if (tcpWarning()) {
            <div class="pv-note">{{ tcpWarning() }}</div>
        }

        <!-- ── Message builder ────────────────────────────────────────── -->
        <details class="pv-builder" [open]="builderOpen()" (toggle)="builderOpen.set(asDetails($event).open)">
            <summary><span class="pv-section-title">Build a message</span>
                <span class="pv-count">{{ catalog().length ? catalog().length + ' server messages' : 'catalog not loaded' }}</span></summary>
            @if (!catalog().length) {
                <div class="pv-note">The bridge hasn't sent the message catalog yet.
                    <button class="lm-row-btn lm-goto-btn" (click)="action.emit({ type: 'getCatalog' })">Retry</button></div>
            } @else {
                <div class="pv-row">
                    <select class="pv-select" [value]="protocol()" (change)="setProtocol(asSelect($event).value)">
                        @for (p of protocols; track p) { <option [value]="p" [selected]="p === protocol()">{{ p }}</option> }
                    </select>
                    <input class="pv-input pv-grow" type="search" placeholder="Search messages…"
                           [value]="query()" (input)="query.set(asInput($event).value)" />
                </div>
                <select class="pv-select pv-msg-list" size="6" [value]="entryKey()" (change)="pickEntry(asSelect($event).value)">
                    @for (e of entries(); track keyOf(e)) {
                        <option [value]="keyOf(e)" [selected]="keyOf(e) === entryKey()">0x{{ hex2(e.id) }}  {{ e.name }}</option>
                    }
                </select>

                @if (entry(); as e) {
                    @if (e.description) { <div class="pv-note">{{ e.description }}</div> }
                    <div class="pv-form">
                        @for (f of e.fields; track f.name) {
                            <label class="pv-form-label" [title]="fieldHint(f)">{{ f.name }}</label>
                            @if (f.options?.length) {
                                <select class="pv-select" [value]="values()[f.name] ?? ''" (change)="setValue(f.name, asSelect($event).value)">
                                    <option value="">0</option>
                                    @for (o of f.options!; track o) { <option [value]="o">{{ o }}</option> }
                                </select>
                            } @else {
                                <input class="pv-input" [placeholder]="fieldHint(f)" [value]="values()[f.name] ?? ''"
                                       (input)="setValue(f.name, asInput($event).value)" />
                            }
                        }
                        <label class="pv-form-label" title="Extra bytes appended after the fields (hex)">Payload</label>
                        <input class="pv-input" placeholder="hex, appended after the fields" [value]="values()['Payload'] ?? ''"
                               (input)="setValue('Payload', asInput($event).value)" />
                    </div>
                    @if (e.protocol !== 'SCERT') {
                        <div class="pv-row">
                            <span class="pv-field-label">Record</span>
                            <select class="pv-select pv-grow" [value]="'' + rtType()" (change)="rtType.set(+asSelect($event).value)">
                                @for (t of rtTypes; track t.id) { <option [value]="'' + t.id" [selected]="t.id === rtType()">{{ t.name }}</option> }
                            </select>
                            @if (rtNeedsTarget()) {
                                <input class="pv-input pv-narrow" type="number" min="0" title="APP_SINGLE / APP_LIST target (or source, server → client) DME index"
                                       [value]="target()" (input)="target.set(+asInput($event).value)" />
                            }
                        </div>
                    }
                    <div class="pv-row">
                        <button class="lm-tool-btn" [disabled]="!templateOk()" (click)="buildMessage()"
                                [title]="templateOk() ? 'Encode the message and put it in the frame as its TCP/UDP payload' : 'Load a TCP/UDP packet first: its headers are the template'">
                            Put in frame
                        </button>
                        <span class="pv-note pv-inline">Replaces the frame's TCP/UDP payload with one plaintext SCERT record.</span>
                    </div>
                }
            }
        </details>
    </div>

    <!-- ── Send ───────────────────────────────────────────────────────── -->
    <div class="pv-composer-foot">
        <label class="pv-field-label" title="How many times to send">×</label>
        <input class="pv-input pv-narrow" type="number" min="1" max="1000" [value]="repeat()" (input)="repeat.set(clampInt(asInput($event).value, 1, 1000))" />
        <label class="pv-field-label" title="Delay between repeats">every</label>
        <input class="pv-input pv-narrow" type="number" min="0" max="60000" step="50" [value]="intervalMs()" (input)="intervalMs.set(clampInt(asInput($event).value, 0, 60000))" />
        <span class="pv-count">ms</span>
        <button class="pv-primary-btn" [disabled]="!canSend() || sending()" (click)="send()">
            @if (sending()) { Sending… } @else { {{ direction() === 'rx' ? 'Inject to PS2' : 'Send from PS2' }} }
        </button>
    </div>
    @if (log().length) {
        <div class="pv-log">
            @for (l of log(); track $index) {
                <div [class.pv-log-err]="!l.ok"><span class="pv-count">{{ l.time }}</span> {{ l.text }}</div>
            }
        </div>
    }
</section>
`,
})
export class PacketComposerComponent {
    readonly catalog  = input<CatalogEntry[]>([]);
    /** Packet currently selected in the list (for Load selected). */
    readonly selected = input<CapturedPacket | null>(null);
    readonly action   = output<PacketViewerAction>();
    readonly closed   = output<void>();
    readonly settingsChanged = output<ComposerSettings>();

    readonly protocols = BUILD_PROTOCOLS;
    readonly rtTypes   = APP_RT_TYPES;

    readonly direction   = signal<Direction>('rx');
    readonly fix         = signal(true);
    readonly tcpContinue = signal(true);
    readonly frameHex    = signal('');
    readonly preview     = signal<Preview | null>(null);
    readonly previewError = signal('');
    readonly builderOpen = signal(true);
    readonly protocol    = signal('Game');
    readonly query       = signal('');
    readonly entryKey    = signal('');
    readonly values      = signal<Record<string, string>>({});
    readonly rtType      = signal(0x02);
    readonly target      = signal(0);
    readonly repeat      = signal(1);
    readonly intervalMs  = signal(250);
    readonly sending     = signal(false);
    readonly log         = signal<LogEntry[]>([]);

    private _requestId = 0;
    private _previewTimer: ReturnType<typeof setTimeout> | undefined;
    /** Request id whose returned frame should replace the hex (after Put in frame). */
    private _applyRequest = -1;
    private _injectWaiter: ((r: { ok: boolean; message: string }) => void) | undefined;

    readonly frameBytes = computed(() => hexToBytes(this.frameHex()));
    readonly hexError = computed(() => this.frameHex().trim() && !this.frameBytes() ? 'Not valid hex (odd digit count or a non-hex character).' : '');
    readonly canSend = computed(() => (this.frameBytes()?.length ?? 0) >= 14);
    readonly templateOk = computed(() => {
        const b = this.frameBytes();
        return !!b && b.length >= 34 && b[12] === 0x08 && b[13] === 0x00 && (b[23] === 6 || b[23] === 17);
    });
    readonly frameInfo = computed(() => {
        const b = this.frameBytes();
        return b ? `${b.length} bytes` : '';
    });
    readonly tcpWarning = computed(() => {
        const b = this.frameBytes();
        if (!b || b.length < 34 || b[23] !== 6 || this.tcpContinue()) { return ''; }
        return 'TCP: with "Continue TCP flow" off, the original seq/ack go out, and a stack that already saw them drops the segment as a duplicate.';
    });

    readonly entries = computed(() => {
        const q = this.query().trim().toLowerCase();
        return this.catalog().filter(e => e.protocol === this.protocol()
            && (!q || e.name.toLowerCase().includes(q) || this.hex2(e.id).toLowerCase() === q.replace(/^0x/, '')));
    });
    readonly entry = computed(() => this.catalog().find(e => this.keyOf(e) === this.entryKey()) ?? null);
    readonly rtNeedsTarget = computed(() => [0x03, 0x04].includes(this.rtType()));

    // ── Called by the page ───────────────────────────────────────────────────

    /** Copy a captured packet into the editor; its direction becomes the send direction. */
    load(packet: CapturedPacket): void {
        this.frameHex.set(bytesToHex(b64ToBytes(packet.data)));
        this.setDirection(packet.direction === 0 ? 'tx' : 'rx');
        this.changed();
    }

    applySettings(s: ComposerSettings): void {
        if (s.direction) { this.direction.set(s.direction); }
        if (s.fix !== undefined) { this.fix.set(s.fix); }
        if (s.tcpContinue !== undefined) { this.tcpContinue.set(s.tcpContinue); }
        if (s.protocol && BUILD_PROTOCOLS.includes(s.protocol)) { this.protocol.set(s.protocol); }
        this.syncRtType();
    }

    onPreview(msg: { requestId: number; frame: string; layers: DissectedLayer[]; row?: ParsedPacketRow; message: string }): void {
        if (msg.requestId === this._applyRequest) {
            this._applyRequest = -1;
            this.frameHex.set(bytesToHex(b64ToBytes(msg.frame)));
            this.addLog(true, msg.message);
        } else if (msg.requestId !== this._requestId) {
            return;   // stale preview
        }
        this.previewError.set('');
        this.preview.set({ frame: msg.frame, layers: msg.layers, row: msg.row, message: msg.message });
    }

    onPreviewError(message: string): void {
        if (this._applyRequest >= 0) { this._applyRequest = -1; this.addLog(false, message); }
        this.preview.set(null);
        this.previewError.set(message);
    }

    onMessageBuilt(data: string, message: string): void {
        const frame = this.frameBytes();
        if (!frame) { return; }
        this.addLog(true, message);
        this._applyRequest = ++this._requestId;
        this.action.emit({ type: 'buildFrame', frame: bytesToB64(frame), requestId: this._applyRequest, payload: data, fix: true, tcpContinue: false });
    }

    onInjectResult(ok: boolean, message: string): void {
        this._injectWaiter?.({ ok, message });
        this._injectWaiter = undefined;
    }

    // ── UI ───────────────────────────────────────────────────────────────────

    loadSelected(): void {
        const p = this.selected();
        if (p) { this.load(p); }
    }

    setDirection(d: Direction): void {
        this.direction.set(d);
        this.syncRtType();
        this.emitSettings();
    }

    setProtocol(p: string): void {
        this.protocol.set(p);
        this.entryKey.set('');
        this.values.set({});
        this.syncRtType();
        this.emitSettings();
    }

    pickEntry(key: string): void {
        this.entryKey.set(key);
        this.values.set({});
    }

    setValue(name: string, value: string): void {
        this.values.update(v => ({ ...v, [name]: value }));
    }

    /** Debounced preview of the current hex through NETBUILDFRAME. */
    changed(): void {
        this.emitSettings();
        clearTimeout(this._previewTimer);
        this._previewTimer = setTimeout(() => {
            const frame = this.frameBytes();
            if (!frame || frame.length < 14) { this.preview.set(null); this.previewError.set(''); return; }
            this.action.emit({ type: 'buildFrame', frame: bytesToB64(frame), requestId: ++this._requestId, fix: this.fix(), tcpContinue: false });
        }, 300);
    }

    buildMessage(): void {
        const e = this.entry();
        if (!e) { return; }
        const values: Record<string, string> = {};
        for (const [k, v] of Object.entries(this.values())) { if (v.trim() !== '') { values[k] = v; } }
        this.action.emit({
            type: 'buildMessage', protocol: e.protocol, id: e.id, values,
            rtType: e.protocol === 'SCERT' ? undefined : this.rtType(),
            target: this.rtNeedsTarget() ? this.target() : undefined,
        });
    }

    async send(): Promise<void> {
        const frame = this.frameBytes();
        if (!frame || this.sending()) { return; }
        this.sending.set(true);
        try {
            const count = this.repeat();
            for (let i = 0; i < count; i++) {
                if (i > 0 && this.intervalMs() > 0) { await new Promise(r => setTimeout(r, this.intervalMs())); }
                const result = await new Promise<{ ok: boolean; message: string }>(resolve => {
                    this._injectWaiter = resolve;
                    this.action.emit({ type: 'inject', frame: bytesToB64(frame), tx: this.direction() === 'tx', fix: this.fix(), tcpContinue: this.tcpContinue() });
                });
                this.addLog(result.ok, count > 1 ? `[${i + 1}/${count}] ${result.message}` : result.message);
                if (!result.ok) { break; }
            }
        } finally {
            this.sending.set(false);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    keyOf(e: CatalogEntry): string { return `${e.protocol}:${e.id}`; }
    hex2(n: number): string { return n.toString(16).toUpperCase().padStart(2, '0'); }

    fieldHint(f: CatalogField): string {
        const size = f.type === 'cstr' || f.type === 'hex' ? 'rest' : `${f.length} B`;
        return `${f.type} @${f.offset}, ${size}${f.auto ? `, auto: ${f.auto}` : ''}${f.optional ? ', optional' : ''}`;
    }

    clampInt(v: string, min: number, max: number): number {
        const n = Math.floor(Number(v));
        return Number.isFinite(n) ? Math.min(max, Math.max(min, n)) : min;
    }

    asInput(e: Event): HTMLInputElement { return e.target as HTMLInputElement; }
    asSelect(e: Event): HTMLSelectElement { return e.target as HTMLSelectElement; }
    asTextarea(e: Event): HTMLTextAreaElement { return e.target as HTMLTextAreaElement; }
    asDetails(e: Event): HTMLDetailsElement { return e.target as HTMLDetailsElement; }

    /** Default wrapper record for the protocol and direction. */
    private syncRtType(): void {
        const game = this.protocol() === 'Game' || this.protocol() === 'DME';
        const tx = this.direction() === 'tx';
        this.rtType.set(game ? (tx ? 0x02 : 0x03) : (tx ? 0x0B : 0x0A));
    }

    private emitSettings(): void {
        this.settingsChanged.emit({ direction: this.direction(), fix: this.fix(), tcpContinue: this.tcpContinue(), protocol: this.protocol() });
    }

    private addLog(ok: boolean, text: string): void {
        const time = new Date().toLocaleTimeString();
        this.log.update(l => [{ time, ok, text }, ...l].slice(0, 30));
    }
}
