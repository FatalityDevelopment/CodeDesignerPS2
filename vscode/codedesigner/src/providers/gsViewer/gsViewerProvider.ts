import * as vscode from 'vscode';
import * as net from 'net';
import { getOutputChannel } from '../../extension';
import { getNonce } from '../../utils/nonce';

/**
 * GS framebuffer viewer.
 *
 * Opens a webview showing the live PCSX2 display, sourced from the bridge's
 * dedicated binary fan-out socket on port 8766 (separate from the JSON
 * channel on 8765 to avoid choking command/response under 60fps frame load).
 *
 * Architecture:
 * ```
 *   PCSX2 GSStreamServer  ──TCP──▶  Bridge GSStreamForwarder  ──TCP──▶  this provider
 *                                        (port 8766, binary frames)
 *                                                                        │
 *                                                                        ▼
 *                                                                webview <canvas>
 *                                                            (createImageBitmap+draw)
 * ```
 *
 * Wire format on the inbound socket (little-endian, per frame):
 * ```
 *   u32 size       // bytes following this field
 *   u8  msg_type   // 1 = frame
 *   u32 frame_idx
 *   u16 width
 *   u16 height
 *   u8  codec      // 1 = MJPEG
 *   u8  flags
 *   bytes payload  // complete JPEG when codec=1
 * ```
 */
export class GsViewerProvider {
    public static readonly viewType = 'codedesigner.gsViewer';

    private static readonly DEFAULT_HOST = '127.0.0.1';
    private static readonly DEFAULT_PORT = 8766;

    private _panel: vscode.WebviewPanel | undefined;
    private _socket: net.Socket | undefined;
    private _readBuf: Buffer = Buffer.alloc(0);

    constructor(private readonly _extensionUri: vscode.Uri) {}

    /**
     * Open (or focus) the GS frame viewer panel and connect to the bridge's
     * binary frame channel. Safe to call repeatedly — re-uses an existing
     * panel and re-establishes the socket if it dropped.
     */
    public open(): void {
        const log = getOutputChannel();

        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.Active);
            // Re-arm the socket if it died while the panel was idle.
            if (!this._socket || this._socket.destroyed) {
                this._connect();
            }
            return;
        }

        this._panel = vscode.window.createWebviewPanel(
            GsViewerProvider.viewType,
            'PCSX2 GS Frame',
            vscode.ViewColumn.Active,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [],
            },
        );

        this._panel.webview.html = this._buildHtml(this._panel.webview);

        this._panel.onDidDispose(() => {
            this._teardownSocket();
            this._panel = undefined;
            log.info('[GsViewer] panel closed');
        });

        // The webview asks for "reconnect" if it sees stale frames, so accept
        // those messages and re-establish the socket.
        this._panel.webview.onDidReceiveMessage((msg) => {
            if (msg?.type === 'reconnect') {
                log.info('[GsViewer] webview requested reconnect');
                this._teardownSocket();
                this._connect();
            }
        });

        this._connect();
    }

    private _connect(): void {
        const log = getOutputChannel();
        this._readBuf = Buffer.alloc(0);

        const sock = new net.Socket();
        sock.setNoDelay(true);

        sock.on('connect', () => {
            log.info(`[GsViewer] connected to bridge ${GsViewerProvider.DEFAULT_HOST}:${GsViewerProvider.DEFAULT_PORT}`);
            this._panel?.webview.postMessage({ type: 'status', connected: true });
        });

        sock.on('data', (chunk) => this._onData(chunk));

        sock.on('error', (err) => {
            log.warn(`[GsViewer] socket error: ${err.message}`);
            this._panel?.webview.postMessage({ type: 'status', connected: false, error: err.message });
        });

        sock.on('close', () => {
            log.info('[GsViewer] socket closed');
            this._panel?.webview.postMessage({ type: 'status', connected: false });
        });

        sock.connect(GsViewerProvider.DEFAULT_PORT, GsViewerProvider.DEFAULT_HOST);
        this._socket = sock;
    }

    /**
     * Length-prefixed framing reassembly. The bridge may deliver multiple
     * frames per chunk or split a single frame across chunks; we accumulate
     * into _readBuf and parse out as many complete frames as fit.
     */
    private _onData(chunk: Buffer): void {
        this._readBuf = this._readBuf.length === 0
            ? chunk
            : Buffer.concat([this._readBuf, chunk]);

        // Header: u32 size + u8 type + u32 idx + u16 w + u16 h + u8 codec + u8 flags = 15 bytes
        const HEADER = 15;

        while (this._readBuf.length >= 4) {
            const payloadSize = this._readBuf.readUInt32LE(0);
            const totalSize = 4 + payloadSize;
            if (this._readBuf.length < totalSize) {
                break; // wait for more
            }

            // Sanity: refuse implausible sizes (defensive — a corrupted size
            // header would otherwise allocate gigabytes).
            if (payloadSize < (HEADER - 4) || payloadSize > 16 * 1024 * 1024) {
                getOutputChannel().error(`[GsViewer] implausible frame size ${payloadSize}; resetting socket`);
                this._teardownSocket();
                return;
            }

            const type = this._readBuf.readUInt8(4);
            if (type === 1) {
                const frameIdx = this._readBuf.readUInt32LE(5);
                const width    = this._readBuf.readUInt16LE(9);
                const height   = this._readBuf.readUInt16LE(11);
                const codec    = this._readBuf.readUInt8(13);
                // const flags = this._readBuf.readUInt8(14);
                const payload  = this._readBuf.subarray(HEADER, totalSize);

                // Copy to a fresh buffer so we can hand its underlying ArrayBuffer
                // to postMessage as a transferable. (slice() over a shared
                // pool would keep _readBuf pinned.)
                const out = Buffer.from(payload);

                this._panel?.webview.postMessage(
                    { type: 'frame', frameIdx, width, height, codec, payload: out },
                );
            }

            this._readBuf = this._readBuf.subarray(totalSize);
        }
    }

    private _teardownSocket(): void {
        if (this._socket) {
            try { this._socket.destroy(); } catch { /* ignore */ }
            this._socket = undefined;
        }
        this._readBuf = Buffer.alloc(0);
    }

    private _buildHtml(webview: vscode.Webview): string {
        const nonce = getNonce();
        const csp = [
            `default-src 'none';`,
            `style-src ${webview.cspSource} 'unsafe-inline';`,
            `script-src 'nonce-${nonce}';`,
            `img-src ${webview.cspSource} data: blob:;`,
        ].join(' ');

        return /* html */ `
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8" />
    <meta http-equiv="Content-Security-Policy" content="${csp}" />
    <title>PCSX2 GS Frame</title>
    <style>
        :root { color-scheme: dark; }
        html, body { margin: 0; padding: 0; height: 100%; background: #0a0a0a; color: #ddd;
                     font-family: var(--vscode-font-family, sans-serif); font-size: 12px; }
        body { display: flex; flex-direction: column; }
        #status {
            padding: 4px 8px; border-bottom: 1px solid #2a2a2a;
            display: flex; gap: 16px; align-items: center;
            background: var(--vscode-titleBar-activeBackground, #1a1a1a);
        }
        #status .dot { width: 10px; height: 10px; border-radius: 50%; background: #555; }
        #status .dot.live { background: #4caf50; }
        #status .dot.dead { background: #d32f2f; }
        #status button {
            background: var(--vscode-button-background, #303030);
            color: var(--vscode-button-foreground, #ddd);
            border: 1px solid var(--vscode-button-border, transparent);
            padding: 2px 8px; cursor: pointer; font: inherit;
        }
        #status button:hover { background: var(--vscode-button-hoverBackground, #404040); }
        #stage {
            flex: 1; min-height: 0; display: flex; align-items: center; justify-content: center;
            overflow: hidden;
        }
        canvas { max-width: 100%; max-height: 100%; image-rendering: pixelated; background: #000; }
    </style>
</head>
<body>
    <div id="status">
        <span class="dot" id="dot"></span>
        <span id="text">connecting…</span>
        <span style="flex: 1"></span>
        <span id="meta"></span>
        <button id="reconnect">Reconnect</button>
    </div>
    <div id="stage"><canvas id="canvas" width="640" height="480"></canvas></div>

    <script nonce="${nonce}">
        const vscode = acquireVsCodeApi();
        const dot = document.getElementById('dot');
        const text = document.getElementById('text');
        const meta = document.getElementById('meta');
        const canvas = document.getElementById('canvas');
        const ctx = canvas.getContext('2d', { alpha: false });

        let lastFrameAt = 0;
        let framesShown = 0;
        let busy = false;          // single in-flight decode; drop frames if behind
        let lastWidth = 0, lastHeight = 0;

        function setStatus(connected, errMsg) {
            dot.className = 'dot ' + (connected ? 'live' : 'dead');
            text.textContent = connected ? 'connected to bridge :8766' :
                               (errMsg ? 'disconnected (' + errMsg + ')' : 'disconnected');
        }

        function updateMeta() {
            const fps = framesShown ? (1000 / Math.max(1, performance.now() - lastFrameAt)).toFixed(0) : '–';
            meta.textContent = lastWidth + 'x' + lastHeight + '   ~' + fps + ' fps   #' + framesShown;
        }

        async function drawFrame(payload, width, height, codec) {
            if (busy) return; // drop, decoder is behind
            busy = true;
            try {
                if (codec !== 1) { // unknown codec — skip
                    return;
                }
                const blob = new Blob([payload], { type: 'image/jpeg' });
                const bitmap = await createImageBitmap(blob);
                if (canvas.width !== width || canvas.height !== height) {
                    canvas.width = width;
                    canvas.height = height;
                }
                ctx.drawImage(bitmap, 0, 0, width, height);
                bitmap.close();
                lastWidth = width; lastHeight = height;
                framesShown++;
                lastFrameAt = performance.now();
                updateMeta();
            } catch (err) {
                // Decode failure is non-fatal — just skip the frame.
                console.warn('GsViewer: decode failed', err);
            } finally {
                busy = false;
            }
        }

        window.addEventListener('message', (event) => {
            const msg = event.data;
            if (!msg) return;
            if (msg.type === 'status') {
                setStatus(!!msg.connected, msg.error);
                return;
            }
            if (msg.type === 'frame') {
                // payload arrives as a Uint8Array (Buffer is wrapped client-side).
                const payload = msg.payload instanceof Uint8Array ? msg.payload : new Uint8Array(msg.payload);
                drawFrame(payload, msg.width, msg.height, msg.codec);
            }
        });

        document.getElementById('reconnect').addEventListener('click', () => {
            vscode.postMessage({ type: 'reconnect' });
        });

        setStatus(false);
    </script>
</body>
</html>`;
    }
}
