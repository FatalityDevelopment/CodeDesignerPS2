import {
    Component,
    ChangeDetectionStrategy,
    ElementRef,
    ViewEncapsulation,
    computed,
    output,
    signal,
    viewChild,
} from '@angular/core';
import { IconComponent } from '../shared/icon.component';
import {
    CapturedPacket,
    DissectedLayer,
    NetStats,
    NetStatus,
    PacketViewerAction,
    ParsedPacketRow,
    b64ToBytes,
    bytesToHex,
} from './types';

/** One table row. */
interface PacketRow {
    index: number;
    time: string;
    dir: 'TX' | 'RX';
    server: string;
    src: string;
    dst: string;
    proto: string;
    length: number;
    info: string;
    /** Lower-cased text the display filter searches. */
    search: string;
    noise: boolean;
}

/** Saved view settings (see PacketViewerPageComponent's webview state). */
export interface ViewerSettings {
    captureDirection?: number;
    displayFilter?: string;
    protoFilter?: string;
    hideNoise?: boolean;
    follow?: boolean;
}

/** Keep-alives and bare TCP ACKs: hidden by "Hide noise". */
const NOISE_INFO = /^(\d+ → \d+ \[[A-Z, ]*\] Len=0|((RT_MSG_(CLIENT|SERVER)_ECHO|Ping( \(echo\))?|EndOfBatch)(, )?)+)$/;

function toHex(b: number, pad = 2): string {
    return b.toString(16).toUpperCase().padStart(pad, '0');
}

/**
 * Hex dump with the selected layer's bytes highlighted. Every other byte takes the colour of the
 * innermost layer that covers it (the bridge emits layers outermost first).
 */
function hexDump(raw: Uint8Array, layers: DissectedLayer[], selectedLayer: number): string {
    const color = new Array<string | undefined>(raw.length);
    const sel   = new Uint8Array(raw.length);
    layers.forEach((layer, li) => {
        const end = Math.min(layer.offset + layer.length, raw.length);
        for (let b = layer.offset; b < end; b++) {
            color[b] = layer.color;
            sel[b]   = li === selectedLayer ? 1 : 0;
        }
    });

    const rows: string[] = [];
    let head = '<div class="pv-hex-row pv-hex-head"><span class="pv-hex-addr">Offset</span><span class="pv-hex-bytes">';
    for (let i = 0; i < 16; i++) { head += `${i && i % 8 === 0 ? '<span class="pv-hex-gap"></span>' : ''}<span class="pv-hex-byte">${toHex(i, 1)}</span>`; }
    rows.push(head + '</span><span class="pv-hex-ascii">ASCII</span></div>');

    for (let i = 0; i < raw.length; i += 16) {
        let bytes = '';
        let ascii = '';
        for (let j = 0; j < 16; j++) {
            if (j && j % 8 === 0) { bytes += '<span class="pv-hex-gap"></span>'; }
            const k = i + j;
            if (k >= raw.length) { bytes += '<span class="pv-hex-byte"></span>'; continue; }
            const b  = raw[k];
            const ch = b >= 0x20 && b < 0x7F ? String.fromCharCode(b).replace('&', '&amp;').replace('<', '&lt;') : '.';
            const cls = sel[k] ? ' pv-hex-hl' : '';
            const sty = !sel[k] && color[k] ? ` style="color:${color[k]}"` : '';
            bytes += `<span class="pv-hex-byte${cls}"${sty}>${toHex(b)}</span>`;
            ascii += `<span class="pv-hex-char${cls}"${sty}>${ch}</span>`;
        }
        rows.push(`<div class="pv-hex-row"><span class="pv-hex-addr">${toHex(i, 4)}</span><span class="pv-hex-bytes">${bytes}</span><span class="pv-hex-ascii">${ascii}</span></div>`);
    }
    return rows.join('');
}

/**
 * Packet list + details: capture controls, the labelled packet table, and the selected packet's
 * layer tree and bytes. Labels come from the bridge dissector, which names every message the
 * multiplayer server defines (MessageCatalog.cs).
 */
@Component({
    selector: 'app-packet-viewer',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    encapsulation: ViewEncapsulation.None,
    imports: [IconComponent],
    template: `
<!-- ── Capture bar ───────────────────────────────────────────────────── -->
<div class="pv-bar">
    @if (!capturing()) {
        <button class="pv-primary-btn" (click)="onStartCapture()"><app-icon name="play" [size]="12" /> Start capture</button>
    } @else {
        <button class="pv-primary-btn pv-stop" (click)="onStopCapture()"><app-icon name="pause" [size]="12" /> Stop capture</button>
    }
    <div class="pv-seg" title="Which direction the bridge captures">
        @for (d of directions; track d.value) {
            <button [class.pv-seg-on]="captureDirection() === d.value" [disabled]="capturing()"
                    (click)="captureDirection.set(d.value); emitSettings()">{{ d.label }}</button>
        }
    </div>
    <span class="pv-sep"></span>
    <button class="lm-tool-btn" (click)="emit({ type: 'openPcap' })" title="Open a .pcap file"><app-icon name="upload" [size]="12" /> Open…</button>
    <button class="lm-tool-btn" [disabled]="!packets().length" (click)="onSavePcap()" title="Save the capture as .pcap (direction kept)"><app-icon name="save" [size]="12" /> Save…</button>
    <button class="lm-tool-btn" [disabled]="!packets().length" (click)="onClear()"><app-icon name="trash2" [size]="12" /> Clear</button>
    @if (filename()) {
        <span class="pv-file-chip" [title]="filename()"><app-icon name="filecode" [size]="12" />{{ filename() }}</span>
    }
    <span class="pv-sep"></span>
    <span class="pv-field-label" title="Bridge-side capture filter: only matching frames are queued">Capture filter</span>
    <select class="pv-select" [value]="'' + filterType()" (change)="filterType.set(+asSelect($event).value)">
        <option value="0">None</option>
        <option value="1">Protocol</option>
        <option value="2">Port</option>
        <option value="3">IP</option>
    </select>
    @if (filterType() === 1) {
        <select class="pv-select" [value]="'' + filterProtocol()" (change)="filterProtocol.set(+asSelect($event).value)">
            @for (p of captureProtocols; track p.value) { <option [value]="'' + p.value">{{ p.label }}</option> }
        </select>
    }
    @if (filterType() === 2) {
        <input class="pv-input pv-narrow" type="number" placeholder="Port" min="0" max="65535" [value]="filterPort()" (input)="filterPort.set(+asInput($event).value)" />
    }
    @if (filterType() === 3) {
        <input class="pv-input" type="text" placeholder="IPv4 address" [value]="filterIP()" (input)="filterIP.set(asInput($event).value)" />
    }
    <button class="lm-tool-btn" (click)="onApplyFilter()" title="Send the capture filter to the bridge">Apply</button>
    <span class="pv-spacer"></span>
    <button class="lm-tool-btn" [class.pv-toggle-on]="composerOpen()" (click)="composerToggled.emit()"
            title="Craft, edit and inject packets"><app-icon name="zap" [size]="12" /> Composer</button>
</div>

<div class="pv-main">
    <!-- ── Packet table ─────────────────────────────────────────────── -->
    <section class="pv-card pv-list-card">
        <div class="pv-card-head">
            <span class="pv-section-title">Packets</span>
            <span class="pv-count">{{ countLabel() }}</span>
            <div class="pv-seg pv-proto-seg">
                <button [class.pv-seg-on]="protoFilter() === ''" (click)="protoFilter.set(''); emitSettings()">All</button>
                @for (p of protocolsSeen(); track p) {
                    <button [class.pv-seg-on]="protoFilter() === p" (click)="protoFilter.set(p); emitSettings()">{{ p }}</button>
                }
            </div>
            <input class="pv-input pv-filter" type="search" placeholder="Filter: words match any column (e.g. MLS AccountLogin)"
                   [value]="displayFilter()" (input)="displayFilter.set(asInput($event).value); emitSettings()" />
            <label class="pv-check" title="Hide bare TCP ACKs, echoes and DME pings">
                <input type="checkbox" [checked]="hideNoise()" (change)="hideNoise.set(asInput($event).checked); emitSettings()" /> Hide noise
            </label>
            <label class="pv-check" title="Keep the newest packet in view while capturing">
                <input type="checkbox" [checked]="follow()" (change)="follow.set(asInput($event).checked); emitSettings()" /> Follow
            </label>
        </div>
        <div class="pv-table-wrap" #tableWrap tabindex="0" (keydown)="onTableKey($event)">
            @if (!packets().length) {
                <div class="pv-empty">
                    <app-icon name="radiotower" [size]="36" />
                    <div>No packets yet</div>
                    <div class="pv-count">Start a capture while the game is online, or open a .pcap file.</div>
                </div>
            } @else if (!visible().length) {
                <div class="pv-empty"><app-icon name="search" [size]="36" /><div>No packets match the filter</div></div>
            } @else {
                <table class="pv-table">
                    <thead>
                        <tr>
                            <th class="pv-c-no">No.</th>
                            <th class="pv-c-time">Time</th>
                            <th class="pv-c-dir">Dir</th>
                            <th class="pv-c-server">Server</th>
                            <th class="pv-c-addr">Source</th>
                            <th class="pv-c-addr">Destination</th>
                            <th class="pv-c-proto">Proto</th>
                            <th class="pv-c-len">Len</th>
                            <th>Info</th>
                        </tr>
                    </thead>
                    <tbody>
                        @for (p of visible(); track p.index) {
                            <tr [class.pv-selected]="selectedIndex() === p.index" (click)="selectPacket(p.index)" (dblclick)="editRequested.emit(p.index)">
                                <td class="pv-c-no">{{ p.index + 1 }}</td>
                                <td class="pv-c-time">{{ p.time }}</td>
                                <td class="pv-c-dir"><span class="pv-dir" [class.pv-dir-tx]="p.dir === 'TX'">{{ p.dir }}</span></td>
                                <td class="pv-c-server">{{ p.server }}</td>
                                <td class="pv-c-addr">{{ p.src }}</td>
                                <td class="pv-c-addr">{{ p.dst }}</td>
                                <td class="pv-c-proto"><span class="pv-proto" [attr.data-proto]="p.proto">{{ p.proto }}</span></td>
                                <td class="pv-c-len">{{ p.length }}</td>
                                <td class="pv-c-info" [title]="p.info">{{ p.info }}</td>
                            </tr>
                        }
                    </tbody>
                </table>
            }
        </div>
    </section>

    <!-- ── Details ──────────────────────────────────────────────────── -->
    <section class="pv-card pv-detail-card">
        <div class="pv-card-head">
            <span class="pv-section-title">Details</span>
            @if (selectedRow(); as r) {
                <span class="pv-count">#{{ r.index + 1 }} · {{ r.dir === 'TX' ? 'PS2 → network' : 'network → PS2' }} · {{ r.length }} bytes</span>
                <span class="pv-head-actions">
                    <button class="lm-tool-btn" (click)="resendRequested.emit(r.index)"
                            [title]="r.dir === 'TX' ? 'Send this frame from the PS2 again (NETINJECTTX)' : 'Inject this frame into the PS2 again (NETINJECT)'">
                        <app-icon name="repeat" [size]="12" /> Resend
                    </button>
                    <button class="lm-tool-btn" (click)="editRequested.emit(r.index)" title="Open this frame in the composer to edit it">
                        <app-icon name="edit" [size]="12" /> Edit in composer
                    </button>
                    <button class="lm-tool-btn" (click)="copyHex()" title="Copy the frame as hex"><app-icon name="copy" [size]="12" /> Copy hex</button>
                </span>
            }
        </div>
        @if (selectedRow()) {
            <div class="pv-detail-body">
                <div class="pv-tree">
                    @for (layer of selectedLayers(); track $index; let li = $index) {
                        <div class="pv-tree-layer" [class.pv-layer-selected]="selectedLayer() === li" (click)="selectLayer(li)">
                            <app-icon [name]="expanded().has(li) ? 'chevrondown' : 'chevronright'" [size]="12" />
                            <span class="pv-tree-dot" [style.background]="layer.color"></span>
                            <span class="pv-tree-label">{{ layer.label }}</span>
                        </div>
                        @if (expanded().has(li)) {
                            @for (f of layer.fields; track $index) {
                                <div class="pv-tree-field"><span class="pv-tree-name">{{ f.name }}</span><span class="pv-tree-value">{{ f.value }}</span></div>
                            }
                        }
                    } @empty {
                        <div class="pv-empty pv-count">No dissection (the bridge was offline when this packet loaded).</div>
                    }
                </div>
                <div class="pv-hex" [innerHTML]="hexHtml()"></div>
            </div>
        } @else {
            <div class="pv-empty pv-count">Select a packet to see its layers and bytes.</div>
        }
    </section>
</div>

<!-- ── Status bar ────────────────────────────────────────────────────── -->
<div class="pv-statusbar">
    @if (adapterStatus(); as s) {
        <span class="pv-status" [class.pv-status-ok]="s.enabled"><span class="pv-status-dot"></span>{{ s.enabled ? 'Adapter up' : 'Adapter down' }}</span>
        @if (s.enabled) { <span>PS2 {{ s.ps2IP }} · {{ s.ps2Mac }}</span> }
    } @else {
        <span class="pv-status"><span class="pv-status-dot"></span>Adapter status unknown</span>
    }
    @if (stats(); as st) {
        <span>TX {{ st.txPackets }} / {{ formatBytes(st.txBytes) }}</span>
        <span>RX {{ st.rxPackets }} / {{ formatBytes(st.rxBytes) }}</span>
        <span>Queue {{ st.captureQueueSize }}</span>
        @if (st.droppedCount > 0) { <span class="pv-status-warn">Dropped {{ st.droppedCount }}</span> }
    }
    <span class="pv-spacer"></span>
    @if (statusMessage()) {
        <span class="pv-status-msg" [class.pv-status-err]="statusIsError()" [title]="statusMessage()">{{ statusMessage() }}</span>
    }
</div>
`,
})
export class PacketViewerComponent {
    readonly action          = output<PacketViewerAction>();
    readonly resendRequested = output<number>();
    readonly editRequested   = output<number>();
    readonly composerToggled = output<void>();
    readonly selectionChanged = output<CapturedPacket | null>();
    readonly settingsChanged = output<ViewerSettings>();

    readonly directions = [
        { value: 0, label: 'Both' },
        { value: 1, label: 'TX' },
        { value: 2, label: 'RX' },
    ];
    readonly captureProtocols = [
        { value: 0, label: 'All' }, { value: 1, label: 'TCP' }, { value: 2, label: 'UDP' }, { value: 3, label: 'ICMP' },
        { value: 4, label: 'ARP' }, { value: 5, label: 'DNS' }, { value: 6, label: 'DHCP' },
    ];

    // ── State ────────────────────────────────────────────────────────────────
    readonly capturing        = signal(false);
    readonly composerOpen     = signal(false);
    readonly captureDirection = signal(0);
    readonly filterType       = signal(0);
    readonly filterProtocol   = signal(0);
    readonly filterPort       = signal(0);
    readonly filterIP         = signal('');
    readonly displayFilter    = signal('');
    readonly protoFilter      = signal('');
    readonly hideNoise        = signal(false);
    readonly follow           = signal(true);
    readonly adapterStatus    = signal<NetStatus | null>(null);
    readonly stats            = signal<NetStats | null>(null);
    readonly statusMessage    = signal('');
    readonly statusIsError    = signal(false);
    readonly filename         = signal('');
    readonly selectedIndex    = signal(-1);
    readonly selectedLayer    = signal(-1);
    readonly expanded         = signal<Set<number>>(new Set());
    readonly packets          = signal<PacketRow[]>([]);

    private readonly tableWrap = viewChild<ElementRef<HTMLElement>>('tableWrap');

    /** Raw packets, index-aligned with packets(). */
    private _raw: Array<{ pkt: CapturedPacket; bytes: Uint8Array }> = [];
    private _baseTimestamp = -1;
    private _pollTimer: ReturnType<typeof setInterval> | undefined;
    private _statsTimer: ReturnType<typeof setInterval> | undefined;
    private _recvInFlight = false;

    readonly protocolsSeen = computed(() => [...new Set(this.packets().map(p => p.proto).filter(p => p))].sort());

    readonly visible = computed(() => {
        const terms = this.displayFilter().trim().toLowerCase().split(/\s+/).filter(t => t);
        const proto = this.protoFilter();
        const hide  = this.hideNoise();
        return this.packets().filter(p =>
            (!proto || p.proto === proto) && (!hide || !p.noise) && terms.every(t => p.search.includes(t)));
    });

    readonly countLabel = computed(() => {
        const all = this.packets().length;
        const shown = this.visible().length;
        return shown === all ? `${all}` : `${shown} of ${all}`;
    });

    readonly selectedRow = computed(() => this.packets()[this.selectedIndex()] ?? null);

    readonly selectedLayers = computed<DissectedLayer[]>(() => {
        const idx = this.selectedIndex();
        return idx >= 0 && idx < this._raw.length ? this._raw[idx].pkt.layers ?? [] : [];
    });

    readonly hexHtml = computed(() => {
        const idx = this.selectedIndex();
        if (idx < 0 || idx >= this._raw.length) { return ''; }
        return hexDump(this._raw[idx].bytes, this.selectedLayers(), this.selectedLayer());
    });

    // ── Public API (page component) ──────────────────────────────────────────

    packetAt(index: number): CapturedPacket | null {
        return this._raw[index]?.pkt ?? null;
    }

    selectedPacket(): CapturedPacket | null {
        return this.packetAt(this.selectedIndex());
    }

    applySettings(s: ViewerSettings): void {
        if (s.captureDirection !== undefined) { this.captureDirection.set(s.captureDirection); }
        if (s.displayFilter !== undefined) { this.displayFilter.set(s.displayFilter); }
        if (s.protoFilter !== undefined) { this.protoFilter.set(s.protoFilter); }
        if (s.hideNoise !== undefined) { this.hideNoise.set(s.hideNoise); }
        if (s.follow !== undefined) { this.follow.set(s.follow); }
    }

    setStatus(status: NetStatus): void { this.adapterStatus.set(status); }
    setStats(s: NetStats): void { this.stats.set(s); }
    setFilename(name: string): void { this.filename.set(name); }

    showStatus(msg: string, error = false): void {
        this.statusMessage.set(msg);
        this.statusIsError.set(error);
    }

    addCapturedPackets(packets: CapturedPacket[], parsedRows: ParsedPacketRow[] = []): void {
        if (!packets.length) { return; }
        const rows = [...this.packets()];
        packets.forEach((pkt, i) => {
            const bytes = b64ToBytes(pkt.data);
            if (this._baseTimestamp < 0) { this._baseTimestamp = pkt.timestampUs; }
            this._raw.push({ pkt, bytes });
            rows.push(this.toRow(this._raw.length - 1, pkt, parsedRows[i]));
        });
        this.packets.set(rows);
        if (this.follow() && this.capturing()) {
            queueMicrotask(() => {
                const el = this.tableWrap()?.nativeElement;
                if (el) { el.scrollTop = el.scrollHeight; }
            });
        }
    }

    startPolling(): void {
        this.stopPolling();
        this._pollTimer = setInterval(() => {
            if (this._recvInFlight) { return; }   // previous drain still running
            this._recvInFlight = true;
            this.emit({ type: 'recvCapture', maxPackets: 200 });
        }, 500);
        this._statsTimer = setInterval(() => this.emit({ type: 'getStats' }), 2000);
    }

    stopPolling(): void {
        clearInterval(this._pollTimer);
        clearInterval(this._statsTimer);
        this._pollTimer = this._statsTimer = undefined;
    }

    /** Called after every recvCapture response (success or error). */
    recvCaptureComplete(): void { this._recvInFlight = false; }

    captureStarted(): void {
        this.capturing.set(true);
        this.showStatus('Capturing');
        this.emit({ type: 'getStatus' });
        this.startPolling();
    }

    captureStartFailed(msg: string): void {
        this.capturing.set(false);
        this.showStatus(msg, true);
    }

    // ── UI handlers ──────────────────────────────────────────────────────────

    emit(a: PacketViewerAction): void { this.action.emit(a); }

    emitSettings(): void {
        this.settingsChanged.emit({
            captureDirection: this.captureDirection(), displayFilter: this.displayFilter(),
            protoFilter: this.protoFilter(), hideNoise: this.hideNoise(), follow: this.follow(),
        });
    }

    onStartCapture(): void {
        this.showStatus('Starting capture…');
        this.emit({ type: 'startCapture', direction: this.captureDirection(), maxQueue: 1000 });
    }

    onStopCapture(): void {
        this.capturing.set(false);
        this.stopPolling();
        this.emit({ type: 'stopCapture' });
        this.showStatus('Capture stopped');
    }

    onClear(): void {
        this._raw = [];
        this._baseTimestamp = -1;
        this.selectedIndex.set(-1);
        this.selectedLayer.set(-1);
        this.expanded.set(new Set());
        this.packets.set([]);
        this.selectionChanged.emit(null);
        this.filename.set('');
        this.emit({ type: 'clearPackets' });
    }

    onApplyFilter(): void {
        this.emit({ type: 'setFilter', filterType: this.filterType(), protocol: this.filterProtocol(), port: this.filterPort(), ip: this.filterIP() });
    }

    onSavePcap(): void {
        if (!this._raw.length) { return; }
        this.emit({ type: 'savePcap', packets: this._raw.map(e => ({ direction: e.pkt.direction, timestampUs: e.pkt.timestampUs, data: e.pkt.data })) });
    }

    selectPacket(idx: number): void {
        this.selectedIndex.set(idx);
        this.selectedLayer.set(-1);
        this.expanded.set(new Set(this.selectedLayers().map((_, i) => i)));
        this.selectionChanged.emit(this.packetAt(idx));
    }

    selectLayer(li: number): void {
        this.selectedLayer.set(li);
        const ex = new Set(this.expanded());
        if (ex.has(li)) { ex.delete(li); } else { ex.add(li); }
        this.expanded.set(ex);
    }

    /** Up/Down move through the visible rows. */
    onTableKey(e: KeyboardEvent): void {
        if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') { return; }
        const rows = this.visible();
        if (!rows.length) { return; }
        e.preventDefault();
        const at = rows.findIndex(r => r.index === this.selectedIndex());
        const next = at < 0 ? 0 : Math.min(rows.length - 1, Math.max(0, at + (e.key === 'ArrowDown' ? 1 : -1)));
        this.selectPacket(rows[next].index);
        queueMicrotask(() => this.tableWrap()?.nativeElement.querySelector('tr.pv-selected')?.scrollIntoView({ block: 'nearest' }));
    }

    copyHex(): void {
        const p = this.selectedPacket();
        if (!p) { return; }
        void navigator.clipboard?.writeText(bytesToHex(b64ToBytes(p.data))).then(() => this.showStatus('Frame copied as hex'));
    }

    formatBytes(n: number): string {
        if (n < 1024) { return `${n} B`; }
        if (n < 1048576) { return `${(n / 1024).toFixed(1)} KB`; }
        return `${(n / 1048576).toFixed(1)} MB`;
    }

    asInput(e: Event): HTMLInputElement { return e.target as HTMLInputElement; }
    asSelect(e: Event): HTMLSelectElement { return e.target as HTMLSelectElement; }

    private toRow(index: number, pkt: CapturedPacket, parsed: ParsedPacketRow | undefined): PacketRow {
        // Time is relative to the first packet in the view (the bridge's row time restarts each drain).
        const time = ((pkt.timestampUs - this._baseTimestamp) / 1_000_000).toFixed(6);
        const dir: 'TX' | 'RX' = pkt.direction === 0 ? 'TX' : 'RX';
        const row = {
            index, time, dir,
            server: parsed?.server ?? '',
            src:    parsed?.src ?? '',
            dst:    parsed?.dst ?? '',
            proto:  parsed?.proto ?? '',
            length: pkt.length || parsed?.len || 0,
            info:   parsed?.info ?? '',
        };
        return {
            ...row,
            search: [dir, row.server, row.src, row.dst, row.proto, row.info, String(row.length)].join(' ').toLowerCase(),
            noise:  NOISE_INFO.test(row.info),
        };
    }
}
