import {
    Component,
    signal,
    computed,
    ChangeDetectionStrategy,
    ViewEncapsulation,
} from '@angular/core';
import { IconComponent } from '../shared/icon.component';

// ── Types ────────────────────────────────────────────────────────────────────

/** A captured packet received from the bridge. */
export interface CapturedPacket {
    /** 0=TX (PS2→Host), 1=RX (Host→PS2). */
    direction: number;
    directionName: string;
    timestampUs: number;
    length: number;
    /** Base64-encoded raw Ethernet frame. */
    data: string;
    /**
     * Pre-computed layer tree from the bridge dissector.
     * Populated by NETRECVCAPTURE (live capture) and the pcap-load path; consumed
     * directly by the tree view / hex highlighter — the webview no longer parses
     * SCERT or Medius. See PacketDissector in CodeDesigner.Ps2.Multiplayer.
     */
    layers?: DissectedLayer[];
}

/** Pre-parsed table row returned by the Bridge alongside a captured packet. */
export interface ParsedPacketRow {
    no: number;
    time: string;
    dir: string;
    server: string;
    src: string;
    dst: string;
    proto: string;
    len: number;
    info: string;
}

/** Adapter status snapshot. */
export interface NetStatus {
    enabled: boolean;
    adapterType: number;
    ps2Mac: string;
    ps2IP: string;
}

/** Traffic statistics snapshot. */
export interface NetStats {
    txPackets: number;
    rxPackets: number;
    txBytes: number;
    rxBytes: number;
    captureQueueSize: number;
    droppedCount: number;
}

/** A parsed protocol layer for the dissection tree. */
export interface DissectedLayer {
    label: string;          // e.g. "Ethernet II", "IPv4", "TCP"
    color: string;          // accent colour for the row
    fields: DissectedField[];
    /** Byte offset and length in the raw frame for highlighting. */
    offset: number;
    length: number;
}

export interface DissectedField {
    name: string;
    value: string;
}

/** A packet payload for pcap save/load operations. */
export interface PcapPacketEntry {
    direction: number;
    timestampUs: number;
    data: string; // base64-encoded raw Ethernet frame
}

/** Actions emitted to the host page component. */
export type PacketViewerAction =
    | { type: 'startCapture'; direction: number; maxQueue: number }
    | { type: 'stopCapture' }
    | { type: 'recvCapture'; maxPackets: number }
    | { type: 'setFilter'; filterType: number; protocol?: number; port?: number; ip?: string }
    | { type: 'getStatus' }
    | { type: 'getStats' }
    | { type: 'disassemble'; data: string }
    | { type: 'clearPackets' }
    | { type: 'openPcap' }
    | { type: 'savePcap'; packets: PcapPacketEntry[] };

// ── Helpers ──────────────────────────────────────────────────────────────────

function b64ToBytes(b64: string): Uint8Array {
    const bin = atob(b64);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) { bytes[i] = bin.charCodeAt(i); }
    return bytes;
}

function toHex(b: number, pad = 2): string {
    return b.toString(16).toUpperCase().padStart(pad, '0');
}

/**
 * Build a structured hex dump with per-layer field colouring.
 *
 * Each layer's bytes are coloured with that layer's accent colour (text).
 * Bytes belonging to the selected layer additionally receive the `pv-hex-hl`
 * background highlight so they stand out clearly.
 *
 * Later layers in the array override earlier ones (innermost wins), matching
 * the order the bridge dissector emits layers: Ethernet -> IP -> TCP -> SCERT -> Medius.
 */
function hexDump(raw: Uint8Array, layers: DissectedLayer[] = [], selectedLayerIdx = -1): string {
    // Per-byte colour and selection state — innermost (later) layer wins.
    const byteColor  = new Array<string | undefined>(raw.length);
    const byteSel    = new Uint8Array(raw.length);

    for (let li = 0; li < layers.length; li++) {
        const layer = layers[li];
        const end   = Math.min(layer.offset + layer.length, raw.length);
        const isSel = li === selectedLayerIdx;
        for (let b = layer.offset; b < end; b++) {
            byteColor[b] = layer.color;
            byteSel[b]   = isSel ? 1 : 0;
        }
    }

    // Header row
    let header = '<div class="pv-hex-row">';
    header += '<span class="pv-hex-addr">Offset</span>';
    header += '<span class="pv-hex-bytes">';
    for (let i = 0; i < 16; i++) {
        if (i && i % 4 === 0) { header += '<span class="pv-hex-gap"></span>'; }
        header += `<span class="pv-hex-byte">+${i.toString(16).toUpperCase()}</span>`;
    }
    header += '</span>';
    header += '<span class="pv-hex-ascii">0123456789ABCDEF</span>';
    header += '</div>';

    // Data rows
    const rows: string[] = [header];
    for (let i = 0; i < raw.length; i += 16) {
        const addr = toHex(i, 8);
        let bytesHtml = '';
        let asciiHtml = '';
        for (let j = 0; j < 16; j++) {
            if (j && j % 4 === 0) { bytesHtml += '<span class="pv-hex-gap"></span>'; }
            if (i + j < raw.length) {
                const b   = raw[i + j];
                const sel = byteSel[i + j];
                const col = byteColor[i + j];
                const ch  = (b >= 0x20 && b < 0x7F) ? String.fromCharCode(b) : '.';
                if (sel) {
                    // Selected layer — prominent background highlight.
                    bytesHtml += `<span class="pv-hex-byte pv-hex-hl">${toHex(b)}</span>`;
                    asciiHtml += `<span class="pv-hex-char pv-hex-hl">${ch}</span>`;
                } else if (col) {
                    // Non-selected layer — layer accent colour on text only.
                    const sty = ` style="color:${col}"`;
                    bytesHtml += `<span class="pv-hex-byte"${sty}>${toHex(b)}</span>`;
                    asciiHtml += `<span class="pv-hex-char"${sty}>${ch}</span>`;
                } else {
                    bytesHtml += `<span class="pv-hex-byte">${toHex(b)}</span>`;
                    asciiHtml += `<span class="pv-hex-char">${ch}</span>`;
                }
            } else {
                bytesHtml += '<span class="pv-hex-byte">  </span>';
                asciiHtml += '<span class="pv-hex-char"> </span>';
            }
        }
        rows.push(
            `<div class="pv-hex-row">` +
            `<span class="pv-hex-addr">${addr}</span>` +
            `<span class="pv-hex-bytes">${bytesHtml}</span>` +
            `<span class="pv-hex-ascii">${asciiHtml}</span>` +
            `</div>`
        );
    }
    return rows.join('');
}

// ── Component ────────────────────────────────────────────────────────────────

@Component({
    selector: 'app-packet-viewer',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    encapsulation: ViewEncapsulation.None,
    imports: [IconComponent],
    styles: [`
/* ── Top-level layout ─────────────────────────────────────────── */
.pv-root {
    --cd-panel-bg: var(--vscode-editor-background, #1e1e1e);
    --cd-panel-bg-raised: var(--vscode-editorWidget-background, var(--vscode-sideBar-background, #252526));
    --cd-panel-header-bg: var(--vscode-editorGroupHeader-tabsBackground, var(--vscode-sideBar-background, #252526));
    --cd-panel-control-bg: var(--vscode-button-secondaryBackground, rgba(255, 255, 255, 0.08));
    --cd-panel-control-hover-bg: var(--vscode-button-secondaryHoverBackground, var(--vscode-toolbar-hoverBackground, rgba(255, 255, 255, 0.12)));
    --cd-panel-border: var(--vscode-panel-border, var(--vscode-editorGroup-border, #3e3e42));
    --cd-panel-border-subtle: color-mix(in srgb, var(--cd-panel-border) 58%, transparent);
    --cd-panel-row-hover: var(--vscode-list-hoverBackground, rgba(255, 255, 255, 0.06));
    --cd-panel-muted: var(--vscode-descriptionForeground, #858585);
    --cd-panel-shadow: 0 2px 8px rgba(0, 0, 0, 0.22);
    display: flex;
    flex-direction: column;
    height: 100%;
    flex: 1 1 auto;
    min-height: 0;
    overflow: hidden;
    background: var(--cd-panel-bg);
    color: var(--vscode-editor-foreground, #d4d4d4);
}

.pv-root app-icon,
.pv-root .lic {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
    line-height: 0;
}

/* ── Toolbar ──────────────────────────────────────────────────── */
.pv-toolbar {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 8px 10px;
    background: var(--cd-panel-header-bg);
    border-bottom: 1px solid var(--cd-panel-border);
    flex-shrink: 0;
    flex-wrap: wrap;
}
.pv-toolbar button {
    display: inline-flex;
    align-items: center;
    gap: 5px;
    padding: 4px 10px;
    border: 1px solid var(--cd-panel-border);
    border-radius: 4px;
    background: var(--cd-panel-control-bg);
    color: var(--vscode-editor-foreground, #d4d4d4);
    font-size: 12px;
    cursor: pointer;
    white-space: nowrap;
}
.pv-toolbar button:hover { background: var(--cd-panel-control-hover-bg); color: #fff; }
.pv-toolbar button.pv-active { background: var(--vscode-button-background, #0e639c); border-color: var(--vscode-button-background, #0e639c); color: #fff; }
.pv-toolbar button:disabled { opacity: 0.5; cursor: default; }
.pv-toolbar select, .pv-toolbar input {
    padding: 3px 6px;
    border: 1px solid var(--cd-panel-border);
    border-radius: 4px;
    background: var(--cd-panel-control-bg);
    color: var(--vscode-editor-foreground, #d4d4d4);
    font-size: 12px;
}
.pv-toolbar input { width: 110px; }
.pv-toolbar .pv-sep { width: 1px; height: 20px; background: var(--cd-panel-border); }
.pv-toolbar .pv-stats {
    margin-left: auto;
    font-size: 11px;
    color: var(--cd-panel-muted);
    white-space: nowrap;
}

/* ── Three-pane body (packet list / dissection tree / hex dump) ─ */
.pv-body {
    display: flex;
    flex-direction: column;
    flex: 1;
    overflow: hidden;
}
.pv-pane {
    overflow: auto;
    margin: 8px 10px 0;
    border: 1px solid var(--cd-panel-border);
    border-radius: 8px;
    background: var(--cd-panel-bg-raised);
    box-shadow: var(--cd-panel-shadow);
}
.pv-pane:last-child { margin-bottom: 8px; }
.pv-pane-packets { flex: 2; min-height: 80px; }
.pv-pane-dissect { flex: 1.2; min-height: 80px; }
.pv-pane-hexdump { flex: 1; min-height: 60px; }

/* ── Packet list table ────────────────────────────────────────── */
.pv-table {
    width: 100%;
    border-collapse: collapse;
    font-family: var(--vscode-editor-font-family, 'Cascadia Code', 'Consolas', monospace);
    font-size: 12px;
}
.pv-table th {
    position: sticky;
    top: 0;
    background: var(--cd-panel-header-bg);
    border-bottom: 1px solid var(--cd-panel-border);
    text-align: left;
    padding: 4px 8px;
    font-weight: 600;
    color: var(--cd-panel-muted);
    font-size: 11px;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    z-index: 1;
}
.pv-table td {
    padding: 2px 8px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    max-width: 500px;
}
.pv-table tr { cursor: pointer; }
.pv-table tr:hover { background: var(--cd-panel-row-hover); }
.pv-table tr.pv-selected { background: var(--vscode-list-activeSelectionBackground, #094771); color: var(--vscode-list-activeSelectionForeground, #fff); }
.pv-dir-tx { color: #e6a23c; }
.pv-dir-rx { color: #67c23a; }
.pv-server { color: #c792ea; font-weight: 600; }

/* ── Dissection tree ──────────────────────────────────────────── */
.pv-tree { padding: 4px 0; }
.pv-tree-layer {
    padding: 2px 8px;
    cursor: pointer;
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 12px;
}
.pv-tree-layer:hover { background: var(--cd-panel-row-hover); }
.pv-tree-layer.pv-layer-selected { background: var(--vscode-list-activeSelectionBackground, #094771); color: var(--vscode-list-activeSelectionForeground, #fff); }
.pv-tree-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    flex-shrink: 0;
}
.pv-tree-chevron {
    width: 12px;
    text-align: center;
    color: #888;
    flex-shrink: 0;
    font-size: 10px;
    user-select: none;
}
.pv-tree-field {
    padding: 1px 8px 1px 40px;
    font-size: 12px;
    font-family: var(--vscode-editor-font-family, 'Cascadia Code', 'Consolas', monospace);
    color: #aaa;
}
.pv-tree-field-name { color: #9cdcfe; }
.pv-tree-field-value { color: #ce9178; }

/* ── Hex dump (disassembler-style) ────────────────────────────── */
.pv-hex {
    font-family: var(--vscode-editor-font-family, 'Cascadia Code', 'Consolas', monospace);
    font-size: 12px;
    padding: 2px 4px 4px;
    background: var(--cd-panel-bg-raised);
}
.pv-hex-row {
    display: flex;
    gap: 8px;
    padding: 2px 6px;
    line-height: 1.4;
}
.pv-hex-row:first-child {
    background: var(--cd-panel-header-bg);
    border-bottom: 1px solid var(--cd-panel-border);
}
.pv-hex-addr {
    color: #858585;
    width: 10ch;
    flex-shrink: 0;
}
.pv-hex-bytes {
    display: flex;
    gap: 0;
    flex: 1;
}
.pv-hex-byte {
    color: #d4d4d4;
    width: 2ch;
    text-align: center;
    margin: 0 1px;
}
.pv-hex-byte.pv-hex-hl {
    background: #264f78;
    color: #fff;
}
.pv-hex-gap {
    width: 8px;
    flex-shrink: 0;
}
.pv-hex-ascii {
    color: #608b4e;
    width: 16ch;
    border-left: 1px solid var(--cd-panel-border);
    padding-left: 8px;
    display: flex;
    flex-shrink: 0;
}
.pv-hex-char {
    width: 1ch;
    display: inline-block;
    text-align: center;
}
.pv-hex-char.pv-hex-hl {
    background: #264f78;
    color: #fff;
}

/* ── Status bar ───────────────────────────────────────────────── */
.pv-statusbar {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 4px 12px;
    background: var(--cd-panel-header-bg);
    border-top: 1px solid var(--cd-panel-border);
    font-size: 11px;
    color: var(--cd-panel-muted);
    flex-shrink: 0;
}
.pv-statusbar .pv-status-ok { color: #67c23a; display: inline-flex; align-items: center; gap: 5px; }
.pv-statusbar .pv-status-err { color: #f56c6c; display: inline-flex; align-items: center; gap: 5px; }

/* ── Empty state ──────────────────────────────────────────────── */
.pv-empty {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    height: 100%;
    color: var(--cd-panel-muted);
    gap: 8px;
}
.pv-empty-icon { opacity: 0.42; color: var(--cd-panel-muted); }

/* ── Display filter bar ───────────────────────────────────────── */
.pv-filterbar {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 6px 10px;
    background: var(--cd-panel-bg);
    border-bottom: 1px solid var(--cd-panel-border);
    flex-shrink: 0;
}
.pv-filterbar-label {
    font-size: 11px;
    color: var(--cd-panel-muted);
    white-space: nowrap;
}
.pv-filterbar-input {
    flex: 1;
    padding: 3px 6px;
    border: 1px solid var(--cd-panel-border);
    border-radius: 4px;
    background: var(--cd-panel-control-bg);
    color: var(--vscode-editor-foreground, #d4d4d4);
    font-size: 12px;
    font-family: var(--vscode-editor-font-family, 'Cascadia Code', 'Consolas', monospace);
    min-width: 0;
}
.pv-filterbar-input:focus { outline: 1px solid var(--vscode-focusBorder, #0e639c); border-color: var(--vscode-focusBorder, #0e639c); }
.pv-filterbar-clear {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    padding: 3px 6px;
    border: 1px solid var(--cd-panel-border);
    border-radius: 4px;
    background: var(--cd-panel-control-bg);
    color: var(--vscode-editor-foreground, #d4d4d4);
    font-size: 11px;
    cursor: pointer;
    flex-shrink: 0;
}
.pv-filterbar-clear:hover { background: var(--cd-panel-control-hover-bg); color: #fff; }
.pv-filterbar-count {
    font-size: 11px;
    color: var(--cd-panel-muted);
    white-space: nowrap;
    flex-shrink: 0;
}
.pv-file-chip {
    display: inline-flex;
    align-items: center;
    gap: 5px;
    font-size: 12px;
    color: var(--cd-panel-muted);
    white-space: nowrap;
    overflow: hidden;
    max-width: 220px;
    text-overflow: ellipsis;
}
`],
    template: `
<div class="pv-root">
    <!-- ── Toolbar ─────────────────────────────────────────────────── -->
    <div class="pv-toolbar">
        @if (!capturing()) {
            <button (click)="onStartCapture()"><app-icon name="play" [size]="13" /><span>Start Capture</span></button>
        } @else {
            <button class="pv-active" (click)="onStopCapture()"><app-icon name="pause" [size]="13" /><span>Stop Capture</span></button>
        }
        <button (click)="onPollOnce()" [disabled]="!capturing()"><app-icon name="refreshcw" [size]="13" /><span>Poll</span></button>
        <button (click)="onClear()"><app-icon name="trash2" [size]="13" /><span>Clear</span></button>
        <div class="pv-sep"></div>
        <button (click)="onOpenPcap()" title="Open a .pcap file"><app-icon name="upload" [size]="13" /><span>Open</span></button>
        <button (click)="onSavePcap()" [disabled]="packets().length === 0" title="Save capture as .pcap"><app-icon name="save" [size]="13" /><span>Save</span></button>
        @if (filename()) {
            <span class="pv-file-chip" [title]="filename()"><app-icon name="filecode" [size]="13" />{{ filename() }}</span>
        }
        <div class="pv-sep"></div>
        <label style="font-size:12px;color:#999">Direction</label>
        <select [value]="captureDirection()" (change)="captureDirection.set(+$any($event).target.value)">
            <option value="0">Both</option>
            <option value="1">TX only</option>
            <option value="2">RX only</option>
        </select>
        <div class="pv-sep"></div>
        <label style="font-size:12px;color:#999">Capture Filter</label>
        <select [value]="filterType()" (change)="filterType.set(+$any($event).target.value)">
            <option value="0">None</option>
            <option value="1">Protocol</option>
            <option value="2">Port</option>
            <option value="3">IP</option>
        </select>
        @if (filterType() === 1) {
            <select [value]="filterProtocol()" (change)="filterProtocol.set(+$any($event).target.value)">
                <option value="0">All</option>
                <option value="1">TCP</option>
                <option value="2">UDP</option>
                <option value="3">ICMP</option>
                <option value="4">ARP</option>
                <option value="5">DNS</option>
                <option value="6">DHCP</option>
            </select>
        }
        @if (filterType() === 2) {
            <input type="number" placeholder="Port" [value]="filterPort()" (input)="filterPort.set(+$any($event).target.value)" min="0" max="65535" />
        }
        @if (filterType() === 3) {
            <input type="text" placeholder="IP address" [value]="filterIP()" (input)="filterIP.set($any($event).target.value)" />
        }
        @if (filterType() !== 0) {
            <button (click)="onApplyFilter()"><app-icon name="filter" [size]="13" /><span>Apply</span></button>
        }
        <span class="pv-stats">
            @if (stats()) {
                TX: {{ stats()!.txPackets }} pkts / {{ formatBytes(stats()!.txBytes) }}
                &nbsp;|&nbsp;
                RX: {{ stats()!.rxPackets }} pkts / {{ formatBytes(stats()!.rxBytes) }}
                &nbsp;|&nbsp;
                Queue: {{ stats()!.captureQueueSize }}
                @if (stats()!.droppedCount > 0) {
                    &nbsp;| <span style="color:#f56c6c">Dropped: {{ stats()!.droppedCount }}</span>
                }
            }
        </span>
    </div>

    <!-- ── Display filter bar ───────────────────────────────────────── -->
    <div class="pv-filterbar">
        <label class="pv-filterbar-label">Display Filter:</label>
        <input class="pv-filterbar-input" type="text" placeholder="Search packets... (src, dst, proto, info)"
               [value]="displayFilter()"
               (input)="displayFilter.set($any($event).target.value)" />
        @if (displayFilter()) {
            <button class="pv-filterbar-clear" (click)="displayFilter.set('')" title="Clear filter"><app-icon name="x" [size]="12" /></button>
            <span class="pv-filterbar-count">{{ filteredPackets().length }} / {{ packets().length }}</span>
        }
    </div>

    <!-- ── Body: three panes ───────────────────────────────────────── -->
    <div class="pv-body">
        <!-- Packet list -->
        <div class="pv-pane pv-pane-packets">
            @if (packets().length === 0) {
                <div class="pv-empty">
                    <app-icon class="pv-empty-icon" name="radiotower" [size]="44" />
                    <div>No packets captured</div>
                    <div style="font-size:11px">Click <b>Start Capture</b> to begin or <b>Open</b> to load a .pcap file</div>
                </div>
            } @else if (filteredPackets().length === 0) {
                <div class="pv-empty">
                    <app-icon class="pv-empty-icon" name="search" [size]="44" />
                    <div>No packets match the display filter</div>
                </div>
            } @else {
                <table class="pv-table">
                    <thead>
                        <tr>
                            <th style="width:50px">No.</th>
                            <th style="width:80px">Time</th>
                            <th style="width:40px">Dir</th>
                            <th style="width:60px">Server</th>
                            <th style="width:130px">Source</th>
                            <th style="width:130px">Destination</th>
                            <th style="width:55px">Proto</th>
                            <th style="width:45px">Len</th>
                            <th>Info</th>
                        </tr>
                    </thead>
                    <tbody>
                        @for (pkt of filteredPackets(); track pkt.index; let i = $index) {
                            <tr [class.pv-selected]="selectedIndex() === pkt.index"
                                (click)="selectPacket(pkt.index)">
                                <td>{{ pkt.index + 1 }}</td>
                                <td>{{ pkt.timeStr }}</td>
                                <td [class]="pkt.dirClass">{{ pkt.dirLabel }}</td>
                                <td class="pv-server">{{ pkt.server }}</td>
                                <td>{{ pkt.src }}</td>
                                <td>{{ pkt.dst }}</td>
                                <td>{{ pkt.proto }}</td>
                                <td>{{ pkt.length }}</td>
                                <td>{{ pkt.info }}</td>
                            </tr>
                        }
                    </tbody>
                </table>
            }
        </div>

        <!-- Dissection tree -->
        <div class="pv-pane pv-pane-dissect">
            @if (selectedLayers().length > 0) {
                <div class="pv-tree">
                    @for (layer of selectedLayers(); track $index; let li = $index) {
                        <div class="pv-tree-layer" [class.pv-layer-selected]="selectedLayerIdx() === li"
                             (click)="selectLayer(li)">
                            <app-icon class="pv-tree-chevron" [name]="expandedLayers().has(li) ? 'chevrondown' : 'chevronright'" [size]="12" />
                            <span class="pv-tree-dot" [style.background]="layer.color"></span>
                            <span>{{ layer.label }}</span>
                        </div>
                        @if (expandedLayers().has(li)) {
                            @for (f of layer.fields; track $index) {
                                <div class="pv-tree-field">
                                    <span class="pv-tree-field-name">{{ f.name }}: </span>
                                    <span class="pv-tree-field-value">{{ f.value }}</span>
                                </div>
                            }
                        }
                    }
                </div>
            }
        </div>

        <!-- Hex dump -->
        <div class="pv-pane pv-pane-hexdump">
            @if (hexDumpHtml()) {
                <div class="pv-hex" [innerHTML]="hexDumpHtml()"></div>
            }
        </div>
    </div>

    <!-- ── Status bar ──────────────────────────────────────────────── -->
    <div class="pv-statusbar">
        @if (adapterStatus()) {
            <span [class]="adapterStatus()!.enabled ? 'pv-status-ok' : 'pv-status-err'">
                <app-icon [name]="adapterStatus()!.enabled ? 'circlecheck' : 'ban'" [size]="12" />
                <span>{{ adapterStatus()!.enabled ? 'Connected' : 'Disconnected' }}</span>
            </span>
            @if (adapterStatus()!.enabled) {
                <span>MAC: {{ adapterStatus()!.ps2Mac }}</span>
                <span>IP: {{ adapterStatus()!.ps2IP }}</span>
            }
        } @else {
            <span>Adapter status unknown - click Start Capture to query</span>
        }
        <span style="margin-left:auto">
            @if (displayFilter() && filteredPackets().length !== packets().length) {
                {{ filteredPackets().length }} displayed / {{ packets().length }} packets
            } @else {
                {{ packets().length }} packets
            }
        </span>
        @if (statusMessage()) {
            <span>{{ statusMessage() }}</span>
        }
    </div>
</div>
`,
})
export class PacketViewerComponent {
    // ── State ────────────────────────────────────────────────────────────────
    readonly capturing = signal(false);
    readonly captureDirection = signal(0);
    readonly filterType = signal(0);
    readonly filterProtocol = signal(0);
    readonly filterPort = signal(0);
    readonly filterIP = signal('');
    /** Client-side display filter — case-insensitive substring search across visible columns. */
    readonly displayFilter = signal('');
    readonly adapterStatus = signal<NetStatus | null>(null);
    readonly stats = signal<NetStats | null>(null);
    readonly statusMessage = signal('');
    readonly filename = signal('');
    readonly selectedIndex = signal(-1);
    readonly selectedLayerIdx = signal(-1);
    readonly expandedLayers = signal<Set<number>>(new Set());

    /** Internal raw packet storage. */
    private _rawPackets: Array<{ pkt: CapturedPacket; raw: Uint8Array }> = [];
    /** Pre-parsed table rows from the Bridge (null = use local fallback). */
    private _parsedRowCache: Array<ParsedPacketRow | null> = [];
    /** First timestamp for relative time display. */
    private _baseTimestamp = 0;
    /** Monotonic packet counter. */
    private _packetCounter = 0;

    /** Polling interval handle. */
    private _pollTimer: ReturnType<typeof setInterval> | undefined;
    /** Stats polling interval handle. */
    private _statsTimer: ReturnType<typeof setInterval> | undefined;

    // Public callback — set by the page component to emit actions.
    onAction: ((action: PacketViewerAction) => void) | undefined;

    /** True while a recvCapture request is in-flight — prevents overlapping polls. */
    private _recvInFlight = false;

    // ── Processed packet list for display ────────────────────────────────────
    readonly packets = signal<Array<{
        index: number;
        timeStr: string;
        dirLabel: string;
        dirClass: string;
        server: string;
        src: string;
        dst: string;
        proto: string;
        length: number;
        info: string;
    }>>([]);

    /** Packets filtered by the display filter text — case-insensitive substring match. */
    readonly filteredPackets = computed(() => {
        const all = this.packets();
        const f = this.displayFilter().trim().toLowerCase();
        if (!f) { return all; }
        return all.filter(p =>
            p.dirLabel.toLowerCase().includes(f) ||
            p.server.toLowerCase().includes(f) ||
            p.src.toLowerCase().includes(f) ||
            p.dst.toLowerCase().includes(f) ||
            p.proto.toLowerCase().includes(f) ||
            p.info.toLowerCase().includes(f) ||
            String(p.length).includes(f) ||
            p.timeStr.includes(f)
        );
    });

    // ── Dissection of selected packet ────────────────────────────────────────
    //
    // The dissection tree comes straight from the bridge — the webview no
    // longer parses Ethernet/SCERT/Medius. `CapturedPacket.layers` is filled
    // in by NETRECVCAPTURE (live drain) and the pcap-load path before the
    // packet reaches this signal, so click renders instantly.
    readonly selectedLayers = computed<DissectedLayer[]>(() => {
        const idx = this.selectedIndex();
        if (idx < 0 || idx >= this._rawPackets.length) { return []; }
        return this._rawPackets[idx].pkt.layers ?? [];
    });

    readonly hexDumpHtml = computed(() => {
        const idx = this.selectedIndex();
        if (idx < 0 || idx >= this._rawPackets.length) { return ''; }
        const raw    = this._rawPackets[idx].raw;
        const li     = this.selectedLayerIdx();
        const layers = this.selectedLayers();
        return hexDump(raw, layers, li);
    });

    // ── Public API (called by page component) ────────────────────────────────

    setStatus(status: NetStatus): void {
        this.adapterStatus.set(status);
    }

    setStats(s: NetStats): void {
        this.stats.set(s);
    }

    addCapturedPackets(packets: CapturedPacket[], parsedRows?: ParsedPacketRow[]): void {
        for (let i = 0; i < packets.length; i++) {
            const pkt = packets[i];
            const raw = b64ToBytes(pkt.data);
            if (this._baseTimestamp === 0) { this._baseTimestamp = pkt.timestampUs; }
            this._rawPackets.push({ pkt, raw });
            this._parsedRowCache.push(parsedRows?.[i] ?? null);
        }
        this._rebuildPacketList();
    }

    showStatus(msg: string): void {
        this.statusMessage.set(msg);
    }

    setFilename(name: string): void {
        this.filename.set(name);
    }

    startPolling(): void {
        this.stopPolling();
        this._pollTimer = setInterval(() => {
            // Skip this tick if the previous recvCapture hasn't returned yet.
            if (this._recvInFlight) { return; }
            this._recvInFlight = true;
            this.onAction?.({ type: 'recvCapture', maxPackets: 100 });
        }, 500);
        this._statsTimer = setInterval(() => {
            this.onAction?.({ type: 'getStats' });
        }, 2000);
    }

    /** Called by the page component after a recvCapture response (success or error). */
    recvCaptureComplete(): void {
        this._recvInFlight = false;
    }

    stopPolling(): void {
        if (this._pollTimer) { clearInterval(this._pollTimer); this._pollTimer = undefined; }
        if (this._statsTimer) { clearInterval(this._statsTimer); this._statsTimer = undefined; }
    }

    // ── UI event handlers ────────────────────────────────────────────────────

    onStartCapture(): void {
        this.statusMessage.set('Starting capture...');
        this.onAction?.({ type: 'startCapture', direction: this.captureDirection(), maxQueue: 1000 });
    }

    /** Called by the page component when the provider confirms capture started. */
    captureStarted(): void {
        this.capturing.set(true);
        this.statusMessage.set('');
        this.onAction?.({ type: 'getStatus' });
        this.startPolling();
    }

    /** Called by the page component when the provider reports capture failed to start. */
    captureStartFailed(msg: string): void {
        this.capturing.set(false);
        this.statusMessage.set(msg);
    }

    onStopCapture(): void {
        this.capturing.set(false);
        this.onAction?.({ type: 'stopCapture' });
        this.stopPolling();
    }

    onPollOnce(): void {
        this.onAction?.({ type: 'recvCapture', maxPackets: 100 });
    }

    onClear(): void {
        this._rawPackets = [];
        this._parsedRowCache = [];
        this._baseTimestamp = 0;
        this._packetCounter = 0;
        this.selectedIndex.set(-1);
        this.selectedLayerIdx.set(-1);
        this.expandedLayers.set(new Set());
        this.packets.set([]);
        this.filename.set('');
        this.onAction?.({ type: 'clearPackets' });
    }

    onApplyFilter(): void {
        this.onAction?.({
            type: 'setFilter',
            filterType: this.filterType(),
            protocol: this.filterProtocol(),
            port: this.filterPort(),
            ip: this.filterIP(),
        });
    }

    onOpenPcap(): void {
        this.onAction?.({ type: 'openPcap' });
    }

    onSavePcap(): void {
        if (this._rawPackets.length === 0) {
            return;
        }
        const packets: PcapPacketEntry[] = this._rawPackets.map(e => ({
            direction:   e.pkt.direction,
            timestampUs: e.pkt.timestampUs,
            data:        e.pkt.data,
        }));
        this.onAction?.({ type: 'savePcap', packets });
    }

    selectPacket(idx: number): void {
        this.selectedIndex.set(idx);
        this.selectedLayerIdx.set(-1);
        // Auto-expand all layers on selection
        const layers = this.selectedLayers();
        this.expandedLayers.set(new Set(layers.map((_, i) => i)));
    }

    selectLayer(li: number): void {
        this.selectedLayerIdx.set(li);
        // Toggle expansion
        const ex = new Set(this.expandedLayers());
        if (ex.has(li)) { ex.delete(li); } else { ex.add(li); }
        this.expandedLayers.set(ex);
    }

    formatBytes(n: number): string {
        if (n < 1024) { return `${n} B`; }
        if (n < 1048576) { return `${(n / 1024).toFixed(1)} KB`; }
        return `${(n / 1048576).toFixed(1)} MB`;
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private _rebuildPacketList(): void {
        const list = this._rawPackets.map((entry, i) => {
            const { pkt, raw } = entry;
            const parsed = this._parsedRowCache[i];

            if (parsed) {
                // Use pre-parsed data from the Bridge.
                return {
                    index: i,
                    timeStr: parsed.time,
                    dirLabel: pkt.directionName,
                    dirClass: pkt.direction === 0 ? 'pv-dir-tx' : 'pv-dir-rx',
                    server: parsed.server,
                    src: parsed.src,
                    dst: parsed.dst,
                    proto: parsed.proto,
                    length: parsed.len,
                    info: parsed.info,
                };
            }

            // Fallback when no parsed row arrived (older host or partial data):
            // produce an empty-but-valid row. The pcap-load path on the host
            // side runs every frame through NETDISASSEMBLE before posting, so
            // in practice `parsed` is always present.
            const relTime = ((pkt.timestampUs - this._baseTimestamp) / 1_000_000).toFixed(6);
            return {
                index: i,
                timeStr: relTime,
                dirLabel: pkt.directionName,
                dirClass: pkt.direction === 0 ? 'pv-dir-tx' : 'pv-dir-rx',
                server: '',
                src: '',
                dst: '',
                proto: '',
                length: pkt.length,
                info: '',
            };
        });
        this.packets.set(list);
    }
}
