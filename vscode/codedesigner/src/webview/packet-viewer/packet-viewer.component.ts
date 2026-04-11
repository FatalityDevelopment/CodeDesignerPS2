import {
    Component,
    signal,
    computed,
    ChangeDetectionStrategy,
    ViewEncapsulation,
} from '@angular/core';

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

/** Actions emitted to the host page component. */
export type PacketViewerAction =
    | { type: 'startCapture'; direction: number; maxQueue: number }
    | { type: 'stopCapture' }
    | { type: 'recvCapture'; maxPackets: number }
    | { type: 'setFilter'; filterType: number; protocol?: number; port?: number; ip?: string }
    | { type: 'getStatus' }
    | { type: 'getStats' }
    | { type: 'disassemble'; data: string }
    | { type: 'clearPackets' };

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

function readU16BE(buf: Uint8Array, off: number): number {
    return (buf[off] << 8) | buf[off + 1];
}

function readU32BE(buf: Uint8Array, off: number): number {
    return ((buf[off] << 24) | (buf[off + 1] << 16) | (buf[off + 2] << 8) | buf[off + 3]) >>> 0;
}

function fmtMac(buf: Uint8Array, off: number): string {
    return Array.from(buf.slice(off, off + 6)).map(b => toHex(b)).join(':');
}

function fmtIP(buf: Uint8Array, off: number): string {
    return `${buf[off]}.${buf[off + 1]}.${buf[off + 2]}.${buf[off + 3]}`;
}

/** Lightweight Ethernet/IPv4/ARP/TCP/UDP/ICMP dissector. */
function dissectFrame(raw: Uint8Array): DissectedLayer[] {
    const layers: DissectedLayer[] = [];
    if (raw.length < 14) { return layers; }

    // ── Ethernet II ──
    const dstMac = fmtMac(raw, 0);
    const srcMac = fmtMac(raw, 6);
    const etherType = readU16BE(raw, 12);
    const ethFields: DissectedField[] = [
        { name: 'Destination', value: dstMac },
        { name: 'Source', value: srcMac },
        { name: 'Type', value: `0x${toHex(etherType, 4)} (${etherTypeName(etherType)})` },
    ];
    layers.push({ label: 'Ethernet II', color: '#5b9bd5', fields: ethFields, offset: 0, length: 14 });

    let off = 14;
    if (etherType === 0x0806) {
        // ── ARP ──
        if (raw.length >= off + 28) {
            const op = readU16BE(raw, off + 6);
            const senderMac = fmtMac(raw, off + 8);
            const senderIP = fmtIP(raw, off + 14);
            const targetMac = fmtMac(raw, off + 18);
            const targetIP = fmtIP(raw, off + 24);
            layers.push({
                label: 'ARP',
                color: '#e6a23c',
                fields: [
                    { name: 'Opcode', value: op === 1 ? '1 (Request)' : op === 2 ? '2 (Reply)' : String(op) },
                    { name: 'Sender MAC', value: senderMac },
                    { name: 'Sender IP', value: senderIP },
                    { name: 'Target MAC', value: targetMac },
                    { name: 'Target IP', value: targetIP },
                ],
                offset: off,
                length: 28,
            });
        }
        return layers;
    }

    if (etherType !== 0x0800) { return layers; } // only IPv4 beyond here

    // ── IPv4 ──
    if (raw.length < off + 20) { return layers; }
    const ihl = (raw[off] & 0x0F) * 4;
    const totalLen = readU16BE(raw, off + 2);
    const ttl = raw[off + 8];
    const proto = raw[off + 9];
    const srcIP = fmtIP(raw, off + 12);
    const dstIP = fmtIP(raw, off + 16);
    const ipId = readU16BE(raw, off + 4);
    const flagsFrag = readU16BE(raw, off + 6);
    const flags = (flagsFrag >> 13) & 0x7;
    const fragOff = flagsFrag & 0x1FFF;
    layers.push({
        label: 'Internet Protocol Version 4',
        color: '#67c23a',
        fields: [
            { name: 'Version', value: String((raw[off] >> 4) & 0xF) },
            { name: 'Header Length', value: `${ihl} bytes` },
            { name: 'Total Length', value: String(totalLen) },
            { name: 'Identification', value: `0x${toHex(ipId, 4)} (${ipId})` },
            { name: 'Flags', value: `0x${toHex(flags, 1)}` },
            { name: 'Fragment Offset', value: String(fragOff) },
            { name: 'TTL', value: String(ttl) },
            { name: 'Protocol', value: `${proto} (${ipProtoName(proto)})` },
            { name: 'Source Address', value: srcIP },
            { name: 'Destination Address', value: dstIP },
        ],
        offset: off,
        length: ihl,
    });

    const payloadOff = off + ihl;

    // ── ICMP ──
    if (proto === 1 && raw.length >= payloadOff + 8) {
        const icmpType = raw[payloadOff];
        const icmpCode = raw[payloadOff + 1];
        const icmpId = readU16BE(raw, payloadOff + 4);
        const icmpSeq = readU16BE(raw, payloadOff + 6);
        layers.push({
            label: 'Internet Control Message Protocol',
            color: '#909399',
            fields: [
                { name: 'Type', value: `${icmpType} (${icmpTypeName(icmpType)})` },
                { name: 'Code', value: String(icmpCode) },
                { name: 'Identifier', value: `0x${toHex(icmpId, 4)}` },
                { name: 'Sequence', value: String(icmpSeq) },
            ],
            offset: payloadOff,
            length: Math.min(8, raw.length - payloadOff),
        });
        return layers;
    }

    // ── TCP ──
    if (proto === 6 && raw.length >= payloadOff + 20) {
        const srcPort = readU16BE(raw, payloadOff);
        const dstPort = readU16BE(raw, payloadOff + 2);
        const seqNum = readU32BE(raw, payloadOff + 4);
        const ackNum = readU32BE(raw, payloadOff + 8);
        const dataOff = ((raw[payloadOff + 12] >> 4) & 0xF) * 4;
        const tcpFlags = raw[payloadOff + 13];
        const win = readU16BE(raw, payloadOff + 14);
        const flagNames: string[] = [];
        if (tcpFlags & 0x01) { flagNames.push('FIN'); }
        if (tcpFlags & 0x02) { flagNames.push('SYN'); }
        if (tcpFlags & 0x04) { flagNames.push('RST'); }
        if (tcpFlags & 0x08) { flagNames.push('PSH'); }
        if (tcpFlags & 0x10) { flagNames.push('ACK'); }
        if (tcpFlags & 0x20) { flagNames.push('URG'); }
        layers.push({
            label: 'Transmission Control Protocol',
            color: '#e6a23c',
            fields: [
                { name: 'Source Port', value: String(srcPort) },
                { name: 'Destination Port', value: String(dstPort) },
                { name: 'Sequence Number', value: String(seqNum) },
                { name: 'Acknowledgment', value: String(ackNum) },
                { name: 'Header Length', value: `${dataOff} bytes` },
                { name: 'Flags', value: `0x${toHex(tcpFlags, 2)} (${flagNames.join(', ') || 'none'})` },
                { name: 'Window', value: String(win) },
            ],
            offset: payloadOff,
            length: dataOff,
        });
        // TCP payload
        const tcpPayOff = payloadOff + dataOff;
        const tcpPayLen = raw.length - tcpPayOff;
        if (tcpPayLen > 0) {
            layers.push({
                label: `Data (${tcpPayLen} bytes)`,
                color: '#909399',
                fields: [{ name: 'Length', value: `${tcpPayLen} bytes` }],
                offset: tcpPayOff,
                length: tcpPayLen,
            });
        }
        return layers;
    }

    // ── UDP ──
    if (proto === 17 && raw.length >= payloadOff + 8) {
        const srcPort = readU16BE(raw, payloadOff);
        const dstPort = readU16BE(raw, payloadOff + 2);
        const udpLen = readU16BE(raw, payloadOff + 4);
        layers.push({
            label: 'User Datagram Protocol',
            color: '#409eff',
            fields: [
                { name: 'Source Port', value: String(srcPort) },
                { name: 'Destination Port', value: String(dstPort) },
                { name: 'Length', value: String(udpLen) },
            ],
            offset: payloadOff,
            length: 8,
        });
        const udpPayLen = raw.length - payloadOff - 8;
        if (udpPayLen > 0) {
            layers.push({
                label: `Data (${udpPayLen} bytes)`,
                color: '#909399',
                fields: [{ name: 'Length', value: `${udpPayLen} bytes` }],
                offset: payloadOff + 8,
                length: udpPayLen,
            });
        }
        return layers;
    }

    return layers;
}

function etherTypeName(t: number): string {
    switch (t) {
        case 0x0800: return 'IPv4';
        case 0x0806: return 'ARP';
        case 0x86DD: return 'IPv6';
        default: return 'Unknown';
    }
}

function ipProtoName(p: number): string {
    switch (p) {
        case 1: return 'ICMP';
        case 6: return 'TCP';
        case 17: return 'UDP';
        default: return 'Unknown';
    }
}

function icmpTypeName(t: number): string {
    switch (t) {
        case 0: return 'Echo Reply';
        case 3: return 'Destination Unreachable';
        case 8: return 'Echo Request';
        case 11: return 'Time Exceeded';
        default: return 'Other';
    }
}

/** Generate the brief "Info" column summary for a packet. */
function packetInfoSummary(raw: Uint8Array): string {
    if (raw.length < 14) { return 'Too short'; }
    const etherType = readU16BE(raw, 12);
    if (etherType === 0x0806 && raw.length >= 42) {
        const op = readU16BE(raw, 20);
        const senderIP = fmtIP(raw, 28);
        const targetIP = fmtIP(raw, 38);
        return op === 1 ? `ARP Who has ${targetIP}? Tell ${senderIP}` : `ARP ${senderIP} is at ${fmtMac(raw, 22)}`;
    }
    if (etherType !== 0x0800 || raw.length < 34) { return etherTypeName(etherType); }
    const ihl = (raw[14] & 0x0F) * 4;
    const proto = raw[23];
    const srcIP = fmtIP(raw, 26);
    const dstIP = fmtIP(raw, 30);
    const payOff = 14 + ihl;
    if (proto === 1 && raw.length >= payOff + 2) {
        const t = raw[payOff];
        return `${srcIP} → ${dstIP}  ICMP ${icmpTypeName(t)}`;
    }
    if ((proto === 6 || proto === 17) && raw.length >= payOff + 4) {
        const sp = readU16BE(raw, payOff);
        const dp = readU16BE(raw, payOff + 2);
        const pn = proto === 6 ? 'TCP' : 'UDP';
        if (proto === 6 && raw.length >= payOff + 14) {
            const flags = raw[payOff + 13];
            const fl: string[] = [];
            if (flags & 0x02) { fl.push('SYN'); }
            if (flags & 0x10) { fl.push('ACK'); }
            if (flags & 0x01) { fl.push('FIN'); }
            if (flags & 0x04) { fl.push('RST'); }
            if (flags & 0x08) { fl.push('PSH'); }
            return `${srcIP}:${sp} → ${dstIP}:${dp}  ${pn} [${fl.join(',')}]`;
        }
        return `${srcIP}:${sp} → ${dstIP}:${dp}  ${pn}`;
    }
    return `${srcIP} → ${dstIP}  ${ipProtoName(proto)}`;
}

/** Determine the high-level protocol name for the Protocol column. */
function packetProtocol(raw: Uint8Array): string {
    if (raw.length < 14) { return '???'; }
    const et = readU16BE(raw, 12);
    if (et === 0x0806) { return 'ARP'; }
    if (et !== 0x0800 || raw.length < 24) { return etherTypeName(et); }
    return ipProtoName(raw[23]);
}

/** Build a hex dump with an optional highlighted byte range. */
function hexDump(raw: Uint8Array, hlOff = -1, hlLen = 0): string {
    const lines: string[] = [];
    for (let i = 0; i < raw.length; i += 16) {
        const addr = toHex(i, 4);
        const hexParts: string[] = [];
        let ascii = '';
        for (let j = 0; j < 16; j++) {
            if (i + j < raw.length) {
                const b = raw[i + j];
                const inHl = hlOff >= 0 && (i + j) >= hlOff && (i + j) < hlOff + hlLen;
                hexParts.push(inHl ? `<b>${toHex(b)}</b>` : toHex(b));
                ascii += (b >= 0x20 && b < 0x7F) ? String.fromCharCode(b) : '.';
            } else {
                hexParts.push('  ');
                ascii += ' ';
            }
        }
        lines.push(`${addr}  ${hexParts.slice(0, 8).join(' ')}  ${hexParts.slice(8).join(' ')}  ${ascii}`);
    }
    return lines.join('\n');
}

// ── Component ────────────────────────────────────────────────────────────────

@Component({
    selector: 'app-packet-viewer',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    encapsulation: ViewEncapsulation.None,
    styles: [`
/* ── Top-level layout ─────────────────────────────────────────── */
.pv-root {
    display: flex;
    flex-direction: column;
    height: 100vh;
    overflow: hidden;
    background: #1e1e1e;
    color: #cccccc;
}

/* ── Toolbar ──────────────────────────────────────────────────── */
.pv-toolbar {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 6px 12px;
    background: #252526;
    border-bottom: 1px solid #3c3c3c;
    flex-shrink: 0;
    flex-wrap: wrap;
}
.pv-toolbar button {
    padding: 4px 10px;
    border: 1px solid #555;
    border-radius: 3px;
    background: #333;
    color: #ccc;
    font-size: 12px;
    cursor: pointer;
    white-space: nowrap;
}
.pv-toolbar button:hover { background: #444; }
.pv-toolbar button.pv-active { background: #0e639c; border-color: #1177bb; color: #fff; }
.pv-toolbar button:disabled { opacity: 0.5; cursor: default; }
.pv-toolbar select, .pv-toolbar input {
    padding: 3px 6px;
    border: 1px solid #555;
    border-radius: 3px;
    background: #333;
    color: #ccc;
    font-size: 12px;
}
.pv-toolbar input { width: 110px; }
.pv-toolbar .pv-sep { width: 1px; height: 20px; background: #555; }
.pv-toolbar .pv-stats {
    margin-left: auto;
    font-size: 11px;
    color: #888;
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
    border-bottom: 1px solid #3c3c3c;
}
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
    background: #2d2d2d;
    border-bottom: 1px solid #555;
    text-align: left;
    padding: 4px 8px;
    font-weight: 600;
    color: #999;
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
.pv-table tr:hover { background: #2a2d2e; }
.pv-table tr.pv-selected { background: #094771; }
.pv-dir-tx { color: #e6a23c; }
.pv-dir-rx { color: #67c23a; }

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
.pv-tree-layer:hover { background: #2a2d2e; }
.pv-tree-layer.pv-layer-selected { background: #094771; }
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

/* ── Hex dump ─────────────────────────────────────────────────── */
.pv-hex {
    font-family: var(--vscode-editor-font-family, 'Cascadia Code', 'Consolas', monospace);
    font-size: 12px;
    padding: 6px 12px;
    white-space: pre;
    line-height: 1.4;
    color: #aaa;
}
.pv-hex b { color: #fff; background: #264f78; font-weight: normal; }

/* ── Status bar ───────────────────────────────────────────────── */
.pv-statusbar {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 3px 12px;
    background: #252526;
    border-top: 1px solid #3c3c3c;
    font-size: 11px;
    color: #888;
    flex-shrink: 0;
}
.pv-statusbar .pv-status-ok { color: #67c23a; }
.pv-statusbar .pv-status-err { color: #f56c6c; }

/* ── Empty state ──────────────────────────────────────────────── */
.pv-empty {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    height: 100%;
    color: #666;
    gap: 8px;
}
.pv-empty-icon { font-size: 48px; opacity: 0.4; }
`],
    template: `
<div class="pv-root">
    <!-- ── Toolbar ─────────────────────────────────────────────────── -->
    <div class="pv-toolbar">
        @if (!capturing()) {
            <button (click)="onStartCapture()">▶ Start Capture</button>
        } @else {
            <button class="pv-active" (click)="onStopCapture()">⏹ Stop Capture</button>
        }
        <button (click)="onPollOnce()" [disabled]="!capturing()">⟳ Poll</button>
        <button (click)="onClear()">✕ Clear</button>
        <div class="pv-sep"></div>
        <label style="font-size:12px;color:#999">Direction</label>
        <select [value]="captureDirection()" (change)="captureDirection.set(+$any($event).target.value)">
            <option value="0">Both</option>
            <option value="1">TX only</option>
            <option value="2">RX only</option>
        </select>
        <div class="pv-sep"></div>
        <label style="font-size:12px;color:#999">Filter</label>
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
            <button (click)="onApplyFilter()">Apply</button>
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

    <!-- ── Body: three panes ───────────────────────────────────────── -->
    <div class="pv-body">
        <!-- Packet list -->
        <div class="pv-pane pv-pane-packets">
            @if (packets().length === 0) {
                <div class="pv-empty">
                    <div class="pv-empty-icon">📡</div>
                    <div>No packets captured</div>
                    <div style="font-size:11px">Click <b>Start Capture</b> to begin capturing PS2 network traffic</div>
                </div>
            } @else {
                <table class="pv-table">
                    <thead>
                        <tr>
                            <th style="width:50px">No.</th>
                            <th style="width:80px">Time</th>
                            <th style="width:40px">Dir</th>
                            <th style="width:140px">Source</th>
                            <th style="width:140px">Destination</th>
                            <th style="width:60px">Proto</th>
                            <th style="width:50px">Len</th>
                            <th>Info</th>
                        </tr>
                    </thead>
                    <tbody>
                        @for (pkt of packets(); track pkt.index; let i = $index) {
                            <tr [class.pv-selected]="selectedIndex() === pkt.index"
                                (click)="selectPacket(pkt.index)">
                                <td>{{ pkt.index + 1 }}</td>
                                <td>{{ pkt.timeStr }}</td>
                                <td [class]="pkt.dirClass">{{ pkt.dirLabel }}</td>
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
                            <span class="pv-tree-chevron">{{ expandedLayers().has(li) ? '▾' : '▸' }}</span>
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
                {{ adapterStatus()!.enabled ? '● Connected' : '○ Disconnected' }}
            </span>
            @if (adapterStatus()!.enabled) {
                <span>MAC: {{ adapterStatus()!.ps2Mac }}</span>
                <span>IP: {{ adapterStatus()!.ps2IP }}</span>
            }
        } @else {
            <span>Adapter status unknown — click Start Capture to query</span>
        }
        <span style="margin-left:auto">{{ packets().length }} packets</span>
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
    readonly adapterStatus = signal<NetStatus | null>(null);
    readonly stats = signal<NetStats | null>(null);
    readonly statusMessage = signal('');
    readonly selectedIndex = signal(-1);
    readonly selectedLayerIdx = signal(-1);
    readonly expandedLayers = signal<Set<number>>(new Set());

    /** Internal raw packet storage. */
    private _rawPackets: Array<{ pkt: CapturedPacket; raw: Uint8Array }> = [];
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

    // ── Processed packet list for display ────────────────────────────────────
    readonly packets = signal<Array<{
        index: number;
        timeStr: string;
        dirLabel: string;
        dirClass: string;
        src: string;
        dst: string;
        proto: string;
        length: number;
        info: string;
    }>>([]);

    // ── Dissection of selected packet ────────────────────────────────────────
    readonly selectedLayers = computed(() => {
        const idx = this.selectedIndex();
        if (idx < 0 || idx >= this._rawPackets.length) { return []; }
        return dissectFrame(this._rawPackets[idx].raw);
    });

    readonly hexDumpHtml = computed(() => {
        const idx = this.selectedIndex();
        if (idx < 0 || idx >= this._rawPackets.length) { return ''; }
        const raw = this._rawPackets[idx].raw;
        const li = this.selectedLayerIdx();
        const layers = this.selectedLayers();
        if (li >= 0 && li < layers.length) {
            return hexDump(raw, layers[li].offset, layers[li].length);
        }
        return hexDump(raw);
    });

    // ── Public API (called by page component) ────────────────────────────────

    setStatus(status: NetStatus): void {
        this.adapterStatus.set(status);
    }

    setStats(s: NetStats): void {
        this.stats.set(s);
    }

    addCapturedPackets(packets: CapturedPacket[]): void {
        for (const pkt of packets) {
            const raw = b64ToBytes(pkt.data);
            if (this._baseTimestamp === 0) { this._baseTimestamp = pkt.timestampUs; }
            this._rawPackets.push({ pkt, raw });
        }
        this._rebuildPacketList();
    }

    showStatus(msg: string): void {
        this.statusMessage.set(msg);
    }

    startPolling(): void {
        this.stopPolling();
        this._pollTimer = setInterval(() => {
            this.onAction?.({ type: 'recvCapture', maxPackets: 100 });
        }, 500);
        this._statsTimer = setInterval(() => {
            this.onAction?.({ type: 'getStats' });
        }, 2000);
    }

    stopPolling(): void {
        if (this._pollTimer) { clearInterval(this._pollTimer); this._pollTimer = undefined; }
        if (this._statsTimer) { clearInterval(this._statsTimer); this._statsTimer = undefined; }
    }

    // ── UI event handlers ────────────────────────────────────────────────────

    onStartCapture(): void {
        this.capturing.set(true);
        this.onAction?.({ type: 'getStatus' });
        this.onAction?.({ type: 'startCapture', direction: this.captureDirection(), maxQueue: 1000 });
        this.startPolling();
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
        this._baseTimestamp = 0;
        this._packetCounter = 0;
        this.selectedIndex.set(-1);
        this.selectedLayerIdx.set(-1);
        this.expandedLayers.set(new Set());
        this.packets.set([]);
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
            const relTime = ((pkt.timestampUs - this._baseTimestamp) / 1_000_000).toFixed(6);
            const src = raw.length >= 26 && readU16BE(raw, 12) === 0x0800 ? fmtIP(raw, 26) : fmtMac(raw, 6);
            const dst = raw.length >= 30 && readU16BE(raw, 12) === 0x0800 ? fmtIP(raw, 30) : fmtMac(raw, 0);
            return {
                index: i,
                timeStr: relTime,
                dirLabel: pkt.directionName,
                dirClass: pkt.direction === 0 ? 'pv-dir-tx' : 'pv-dir-rx',
                src,
                dst,
                proto: packetProtocol(raw),
                length: pkt.length,
                info: packetInfoSummary(raw),
            };
        });
        this.packets.set(list);
    }
}
