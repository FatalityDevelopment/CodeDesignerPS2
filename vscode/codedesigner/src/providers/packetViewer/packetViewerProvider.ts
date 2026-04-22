import * as vscode from 'vscode';
import * as fs from 'fs';
import { getOutputChannel } from '../../extension';
import { SocketClient } from '../../socketClient';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';

// ── pcap file format constants ────────────────────────────────────────────────

const PCAP_MAGIC_USEC     = 0xa1b2c3d4;  // microsecond-precision timestamps
const PCAP_VERSION_MAJ    = 2;
const PCAP_VERSION_MIN    = 4;
const PCAP_SNAPLEN        = 65535;
const PCAP_LINKTYPE_SLL   = 113;  // LINKTYPE_LINUX_SLL — encodes packet direction

// Linux SLL packet_type values (per linux/if_packet.h)
// These are the raw wire values stored in the SLL frame header.
// SLL_PKT_HOST (0)     → packet received by this host         → direction 1 (RX)
// SLL_PKT_OUTGOING (4) → packet sent from this host outbound  → direction 0 (TX)
const SLL_PKT_HOST      = 0;
const SLL_PKT_OUTGOING  = 4;
// Known Medius server ports used to infer direction from Ethernet-type pcap frames.
// If the source port is a Medius port the packet is server→PS2 (RX=1);
// if the dest port is a Medius port the packet is PS2→server (TX=0).
const PCAP_MEDIUS_PORTS = new Set([10071, 10078, 10075, 10073, 10070, 10069, 443, 10060]);
/** A minimal captured packet payload for pcap I/O. */
interface PcapPacketEntry {
    direction:   number;
    timestampUs: number;
    data:        string; // base64-encoded raw Ethernet frame
}

// ── SLL ↔ Ethernet conversion helpers ────────────────────────────────────────

/**
 * Convert a raw Ethernet frame (dst[6] src[6] type[2] payload…) plus a
 * direction flag into a Linux SLL frame (16-byte SLL header + L3 payload).
 *
 * SLL header layout (all big-endian):
 *   [0-1]  packet_type  – SLL_PKT_OUTGOING (TX=0) or SLL_PKT_HOST (RX=1)
 *   [2-3]  ARPHRD type  – 0x0001 (Ethernet)
 *   [4-5]  addr length  – 6
 *   [6-13] source addr  – src MAC from Ethernet header (+ 2 zero pad)
 *   [14-15] protocol    – EtherType from Ethernet header
 *   [16…]  L3 payload
 */
function ethToSll(ethFrame: Buffer, direction: number): Buffer {
    const pktType = direction === 0 ? SLL_PKT_OUTGOING : SLL_PKT_HOST;

    if (ethFrame.length < 14) {
        // Undersized frame — zero-fill the 8-byte address field and store
        // however many bytes we do have there, with the correct length declared.
        const copyLen = Math.min(ethFrame.length, 8);
        const sll = Buffer.alloc(16 + ethFrame.length);
        sll.writeUInt16BE(pktType,  0);
        sll.writeUInt16BE(0x0001,   2);
        sll.writeUInt16BE(copyLen,  4); // address length = actual bytes written
        ethFrame.copy(sll, 6, 0, copyLen);
        return sll;
    }

    const srcMac   = ethFrame.slice(6, 12);
    const etherType = ethFrame.readUInt16BE(12);
    const l3Payload = ethFrame.slice(14);

    const sll = Buffer.alloc(16 + l3Payload.length);
    sll.writeUInt16BE(pktType,   0);
    sll.writeUInt16BE(0x0001,    2); // ARPHRD_ETHER
    sll.writeUInt16BE(6,         4); // address length
    srcMac.copy(sll, 6);             // source address (6 bytes)
    // bytes 12-13 are zero padding (already zero from alloc)
    sll.writeUInt16BE(etherType, 14);
    l3Payload.copy(sll, 16);

    return sll;
}

/**
 * Convert a Linux SLL frame back to a synthetic Ethernet frame for the
 * webview dissector.  Direction is decoded from the SLL packet_type field.
 *
 * The reconstructed Ethernet frame uses 00:00:00:00:00:00 as the destination
 * MAC (the field is not stored in SLL).  Source MAC, EtherType, and all L3+
 * payload bytes are fully preserved, so IP/TCP/UDP dissection is unaffected.
 */
function sllToEth(sllFrame: Buffer): { direction: number; ethFrame: Buffer } {
    if (sllFrame.length < 16) {
        return { direction: 0, ethFrame: sllFrame };
    }

    const pktType  = sllFrame.readUInt16BE(0);
    // packet_type 4 = OUTGOING (PS2 sent it) → direction 0 (TX)
    // everything else (0=HOST, 1=BROADCAST, etc.) → direction 1 (RX)
    const direction = pktType === SLL_PKT_OUTGOING ? 0 : 1;

    const srcMac   = sllFrame.slice(6, 12);
    const etherType = sllFrame.readUInt16BE(14);
    const l3Payload = sllFrame.slice(16);

    // Reconstruct: [dst=00:00:00:00:00:00][src from SLL][EtherType][L3 payload]
    const ethFrame = Buffer.alloc(14 + l3Payload.length);
    // dst MAC bytes 0-5: zero (already allocated as zero)
    srcMac.copy(ethFrame, 6);
    ethFrame.writeUInt16BE(etherType, 12);
    l3Payload.copy(ethFrame, 14);

    return { direction, ethFrame };
}

// ── pcap parse / build ────────────────────────────────────────────────────────

/**
 * Infer packet direction from an Ethernet frame's TCP/UDP port numbers.
 * Used for LINKTYPE_ETHERNET pcap files where direction is not stored.
 *
 * Returns 1 (RX) when the source port is a known Medius server port, and
 * 0 (TX) otherwise (including non-TCP/UDP frames).
 */
function detectDirectionFromEthFrame(frame: Buffer): number {
    if (frame.length < 34) { return 0; }
    const etherType = frame.readUInt16BE(12);
    if (etherType !== 0x0800) { return 0; }  // not IPv4
    const ihl  = (frame[14] & 0xF) * 4;
    const proto = frame[23];
    if ((proto === 6 || proto === 17) && frame.length >= 14 + ihl + 4) {
        const payOff = 14 + ihl;
        const srcPort = frame.readUInt16BE(payOff);
        const dstPort = frame.readUInt16BE(payOff + 2);
        // Source port is a Medius port → server sent it → RX (direction=1)
        if (PCAP_MEDIUS_PORTS.has(srcPort)) { return 1; }
        // Dest port is a Medius port → PS2 sent it → TX (direction=0)
        if (PCAP_MEDIUS_PORTS.has(dstPort)) { return 0; }
    }
    return 0;
}

/**
 * Parse a pcap file buffer and return an array of packet entries in the same
 * shape that the webview component expects (CapturedPacket-compatible).
 *
 * Supports:
 *  • LINKTYPE_ETHERNET (1)     — direction inferred from TCP/UDP port numbers
 *  • LINKTYPE_LINUX_SLL (113)  — direction decoded from SLL packet_type;
 *                                frame reconstructed as synthetic Ethernet
 */
function parsePcap(buf: Buffer): PcapPacketEntry[] {
    if (buf.length < 24) { return []; }

    const magic = buf.readUInt32LE(0);
    const isLE  = magic === PCAP_MAGIC_USEC || magic === 0xa1b23c4d;
    const isNs  = magic === 0xa1b23c4d || magic === 0xd4c3b2a1;

    if (!isLE && magic !== 0xd4c3b2a1 && magic !== 0x4d3cb2a1) {
        return []; // not a pcap file we recognise
    }

    const readU32 = isLE
        ? (o: number) => buf.readUInt32LE(o)
        : (o: number) => buf.readUInt32BE(o);

    const linkType = readU32(20); // link-layer type from global header

    const packets: PcapPacketEntry[] = [];
    let off = 24; // skip global header

    while (off + 16 <= buf.length) {
        const tsSec   = readU32(off);
        const tsFrac  = readU32(off + 4);
        const inclLen = readU32(off + 8);
        off += 16;

        if (off + inclLen > buf.length) { break; }

        const timestampUs = isNs
            ? tsSec * 1_000_000 + Math.floor(tsFrac / 1000)
            : tsSec * 1_000_000 + tsFrac;

        const frameBytes = buf.slice(off, off + inclLen);
        off += inclLen;

        if (linkType === PCAP_LINKTYPE_SLL) {
            // Decode direction from SLL header; reconstruct synthetic Ethernet frame
            const { direction, ethFrame } = sllToEth(frameBytes);
            packets.push({
                direction,
                timestampUs,
                data: ethFrame.toString('base64'),
            });
        } else {
            // LINKTYPE_ETHERNET or any other type — detect direction from port numbers
            // when the frame contains a known Medius server port, otherwise mark TX.
            const direction = detectDirectionFromEthFrame(frameBytes);
            packets.push({
                direction,
                timestampUs,
                data: frameBytes.toString('base64'),
            });
        }
    }

    return packets;
}

/**
 * Build a pcap file buffer from an array of packet entries.
 *
 * Uses microsecond precision (magic 0xa1b2c3d4) and LINKTYPE_LINUX_SLL (113)
 * so that each packet's direction is preserved in the SLL packet_type field.
 * Files saved with this format are recognised by Wireshark and can be loaded
 * back by this provider with correct TX/RX direction.
 */
function buildPcap(packets: PcapPacketEntry[]): Buffer {
    // Global header: 24 bytes — use LINKTYPE_LINUX_SLL to preserve direction
    const headerBuf = Buffer.allocUnsafe(24);
    headerBuf.writeUInt32LE(PCAP_MAGIC_USEC,      0);
    headerBuf.writeUInt16LE(PCAP_VERSION_MAJ,     4);
    headerBuf.writeUInt16LE(PCAP_VERSION_MIN,     6);
    headerBuf.writeInt32LE(0,                     8);  // thiszone
    headerBuf.writeUInt32LE(0,                   12);  // sigfigs
    headerBuf.writeUInt32LE(PCAP_SNAPLEN,        16);
    headerBuf.writeUInt32LE(PCAP_LINKTYPE_SLL,   20);  // direction-aware link type

    const parts: Buffer[] = [headerBuf];

    for (const pkt of packets) {
        const ethFrame  = Buffer.from(pkt.data, 'base64');
        const sllFrame  = ethToSll(ethFrame, pkt.direction);
        const recHdr    = Buffer.allocUnsafe(16);
        const tsSec     = Math.floor(pkt.timestampUs / 1_000_000);
        const tsUsec    = pkt.timestampUs % 1_000_000;
        recHdr.writeUInt32LE(tsSec,            0);
        recHdr.writeUInt32LE(tsUsec,           4);
        recHdr.writeUInt32LE(sllFrame.length,  8);
        recHdr.writeUInt32LE(sllFrame.length, 12);
        parts.push(recHdr, sllFrame);
    }

    return Buffer.concat(parts);
}

/**
 * Provider for the Packet Viewer standalone webview tab.
 * Manages the panel lifecycle, routes messages between the webview
 * and the SocketClient network commands.
 */
export class PacketViewerProvider {
    private _panel: vscode.WebviewPanel | undefined;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly socketClient: SocketClient,
    ) {}

    /** Open (or reveal) the Packet Viewer panel. */
    public open(): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.packetViewer',
            '📡 Packet Viewer',
            vscode.ViewColumn.One,
            {
                enableScripts:           true,
                retainContextWhenHidden: true,
                localResourceRoots:      [this.extensionUri],
            }
        );
        this._panel = panel;

        panel.onDidDispose(() => { this._panel = undefined; });

        panel.webview.onDidReceiveMessage(async (msg: Record<string, unknown>) => {
            await this._handleMessage(msg, panel.webview);
        });

        panel.webview.html = this._buildHtml(panel.webview);
    }

    // ── Message dispatch ──────────────────────────────────────────────────────

    private async _handleMessage(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        const type = msg['type'] as string | undefined;

        if (type === 'ready') {
            // Try to send initial adapter status
            await this._sendStatus(webview);
            return;
        }

        if (type === 'packetViewerAction') {
            const action = msg['action'] as Record<string, unknown>;
            await this._handleAction(action, webview);
            return;
        }

        if (type === 'bootstrapError') {
            getOutputChannel().error('[PacketViewer] Bootstrap error: ' + msg['error']);
            return;
        }
    }

    private async _handleAction(action: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        const actionType = action['type'] as string;

        try {
            switch (actionType) {
                case 'startCapture': {
                    const dir = (action['direction'] as number) ?? 0;
                    const maxQ = (action['maxQueue'] as number) ?? 1000;
                    await this.socketClient.netCapture(true, dir, maxQ);
                    // Capture was enabled successfully — tell the webview to start polling.
                    webview.postMessage({ type: 'packetViewerCaptureStarted' });
                    break;
                }

                case 'stopCapture':
                    await this.socketClient.netCapture(false);
                    break;

                case 'recvCapture': {
                    const max = (action['maxPackets'] as number) ?? 100;
                    const result = await this.socketClient.netRecvCapture(max);
                    if (result.packets.length > 0) {
                        webview.postMessage({ type: 'packetViewerPackets', packets: result.packets, parsedRows: result.parsedRows });
                    } else {
                        webview.postMessage({ type: 'packetViewerPackets', packets: [], parsedRows: [] });
                    }
                    break;
                }

                case 'setFilter': {
                    const ft = (action['filterType'] as number) ?? 0;
                    const proto = action['protocol'] as number | undefined;
                    const port = action['port'] as number | undefined;
                    const ip = action['ip'] as string | undefined;
                    await this.socketClient.netSetFilter(ft, proto, port, ip);
                    webview.postMessage({ type: 'packetViewerMessage', message: 'Filter applied' });
                    break;
                }

                case 'getStatus':
                    await this._sendStatus(webview);
                    break;

                case 'getStats':
                    await this._sendStats(webview);
                    break;

                case 'disassemble': {
                    const data = action['data'] as string;
                    const text = await this.socketClient.netDisassemble(data);
                    webview.postMessage({ type: 'packetViewerMessage', message: text });
                    break;
                }

                case 'clearPackets':
                    // No server action needed — packets are cleared client-side.
                    break;

                case 'openPcap': {
                    const uris = await vscode.window.showOpenDialog({
                        canSelectMany: false,
                        filters: { 'Packet Capture': ['pcap', 'cap'] },
                        openLabel: 'Open Capture',
                        title: 'Open Packet Capture File',
                    });
                    if (!uris || uris.length === 0) { break; }
                    const filePath = uris[0].fsPath;
                    const buf = fs.readFileSync(filePath);
                    const rawPackets = parsePcap(buf);
                    if (rawPackets.length === 0) {
                        webview.postMessage({
                            type: 'packetViewerError',
                            message: 'No packets found — file may not be a valid pcap.',
                        });
                        break;
                    }
                    // Shape packets to match CapturedPacket expected by addCapturedPackets.
                    const packets = rawPackets.map(p => ({
                        direction:     p.direction,
                        directionName: p.direction === 0 ? 'TX' : 'RX',
                        timestampUs:   p.timestampUs,
                        length:        Math.floor(Buffer.from(p.data, 'base64').length),
                        data:          p.data,
                    }));
                    webview.postMessage({ type: 'packetViewerPcapLoaded', packets, filename: filePath.split(/[\\/]/).pop() ?? filePath });
                    break;
                }

                case 'savePcap': {
                    const uri = await vscode.window.showSaveDialog({
                        filters: { 'Packet Capture': ['pcap'] },
                        saveLabel: 'Save Capture',
                        title: 'Save Packet Capture',
                    });
                    if (!uri) { break; }
                    const pkts = (action['packets'] as PcapPacketEntry[]) ?? [];
                    const buf = buildPcap(pkts);
                    fs.writeFileSync(uri.fsPath, buf);
                    webview.postMessage({
                        type: 'packetViewerSaveResult',
                        message: `Saved ${pkts.length} packet(s) to ${uri.fsPath}`,
                    });
                    break;
                }
            }
        } catch (err: unknown) {
            const msg = err instanceof Error ? err.message : String(err);
            getOutputChannel().error(`[PacketViewer] ${actionType} failed: ${msg}`);
            if (actionType === 'startCapture') {
                webview.postMessage({ type: 'packetViewerCaptureFailed', message: msg });
            } else {
                webview.postMessage({ type: 'packetViewerError', message: msg });
            }
        }
    }

    // ── Data helpers ──────────────────────────────────────────────────────────

    private async _sendStatus(webview: vscode.Webview): Promise<void> {
        try {
            const status = await this.socketClient.netGetStatus();
            webview.postMessage({ type: 'packetViewerStatus', status });
        } catch {
            // Silently ignore — adapter may not be available.
        }
    }

    private async _sendStats(webview: vscode.Webview): Promise<void> {
        try {
            const stats = await this.socketClient.netGetStats();
            webview.postMessage({ type: 'packetViewerStats', stats });
        } catch {
            // Silently ignore — adapter may not be available.
        }
    }

    // ── HTML builder ─────────────────────────────────────────────────────────

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'webview', 'packet-viewer.js')
        );
        const nonce = getNonce();
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               script-src ${webview.cspSource} 'nonce-${nonce}';">
<script nonce="${nonce}">${getWebviewErrorHandlerScript()}</script>
</head>
<body>
<app-packet-viewer-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Packet Viewer…</p></app-packet-viewer-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
