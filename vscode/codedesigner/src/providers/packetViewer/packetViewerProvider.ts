import * as vscode from 'vscode';
import * as fs from 'fs';
import { getOutputChannel } from '../../extension';
import { SocketClient } from '../../socketClient';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';

// ── pcap file format constants ────────────────────────────────────────────────

const PCAP_MAGIC_USEC  = 0xa1b2c3d4;  // microsecond-precision timestamps
const PCAP_VERSION_MAJ = 2;
const PCAP_VERSION_MIN = 4;
const PCAP_SNAPLEN     = 65535;
const PCAP_LINKTYPE    = 1;            // LINKTYPE_ETHERNET

/** A minimal captured packet payload for pcap I/O. */
interface PcapPacketEntry {
    direction:   number;
    timestampUs: number;
    data:        string; // base64-encoded raw Ethernet frame
}

/**
 * Parse a pcap file buffer and return an array of packet entries in the same
 * shape that the webview component expects (CapturedPacket-compatible).
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

    const packets: PcapPacketEntry[] = [];
    let off = 24; // skip global header

    while (off + 16 <= buf.length) {
        const tsSec  = readU32(off);
        const tsFrac = readU32(off + 4);
        const inclLen = readU32(off + 8);
        off += 16;

        if (off + inclLen > buf.length) { break; }

        const timestampUs = isNs
            ? tsSec * 1_000_000 + Math.floor(tsFrac / 1000)
            : tsSec * 1_000_000 + tsFrac;

        const frameBytes = buf.slice(off, off + inclLen);
        off += inclLen;

        packets.push({
            direction:   0, // pcap has no direction field; default to TX
            timestampUs,
            data: frameBytes.toString('base64'),
        });
    }

    return packets;
}

/**
 * Build a pcap file buffer from an array of packet entries.
 * Uses microsecond precision (magic 0xa1b2c3d4) and LINKTYPE_ETHERNET.
 */
function buildPcap(packets: PcapPacketEntry[]): Buffer {
    // Global header: 24 bytes
    const headerBuf = Buffer.allocUnsafe(24);
    headerBuf.writeUInt32LE(PCAP_MAGIC_USEC,  0);
    headerBuf.writeUInt16LE(PCAP_VERSION_MAJ, 4);
    headerBuf.writeUInt16LE(PCAP_VERSION_MIN, 6);
    headerBuf.writeInt32LE(0,                 8);  // thiszone
    headerBuf.writeUInt32LE(0,               12);  // sigfigs
    headerBuf.writeUInt32LE(PCAP_SNAPLEN,    16);
    headerBuf.writeUInt32LE(PCAP_LINKTYPE,   20);

    const parts: Buffer[] = [headerBuf];

    for (const pkt of packets) {
        const frameBytes = Buffer.from(pkt.data, 'base64');
        const recHdr = Buffer.allocUnsafe(16);
        const tsSec  = Math.floor(pkt.timestampUs / 1_000_000);
        const tsUsec = pkt.timestampUs % 1_000_000;
        recHdr.writeUInt32LE(tsSec,              0);
        recHdr.writeUInt32LE(tsUsec,             4);
        recHdr.writeUInt32LE(frameBytes.length,  8);
        recHdr.writeUInt32LE(frameBytes.length, 12);
        parts.push(recHdr, frameBytes);
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
                    const packets = await this.socketClient.netRecvCapture(max);
                    if (packets.length > 0) {
                        webview.postMessage({ type: 'packetViewerPackets', packets });
                    } else {
                        // Send an empty response so the in-flight guard is cleared.
                        webview.postMessage({ type: 'packetViewerPackets', packets: [] });
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
                    webview.postMessage({ type: 'packetViewerPcapLoaded', packets });
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
