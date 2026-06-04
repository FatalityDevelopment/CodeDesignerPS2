import * as vscode from 'vscode';
import * as net from 'net';
import { getOutputChannel } from '../../extension';
import { getNonce } from '../../utils/nonce';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';

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
    private _frameCount = 0;

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
            'Preview',
            vscode.ViewColumn.Active,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [],
            },
        );
        applyCodeDesignerPanelIcon(this._panel, this._extensionUri, 'monitor');

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
                // Defer the new connect by a microtask so the OS-side close
                // for the old socket finishes draining before we bind a new one
                // on the same loopback peer. Avoids transient EADDRNOTAVAIL.
                setImmediate(() => {
                    if (this._panel) this._connect();
                });
            } else if (msg?.type === 'screenshot') {
                vscode.commands.executeCommand('codedesigner.saveScreenshot');
            } else if (msg?.type === 'controller') {
                vscode.commands.executeCommand('codedesigner.openJokerMaker');
            } else if (msg?.type === 'decodeError') {
                log.warn(`[GsViewer] webview decode error: ${msg.error}`);
            }
        });

        this._connect();
    }

    private _connect(): void {
        const log = getOutputChannel();
        this._readBuf = Buffer.alloc(0);
        this._frameCount = 0;

        const sock = new net.Socket();
        sock.setNoDelay(true);

        // Each event handler captures `sock` in its closure and checks against
        // `this._socket` before mutating state. Without this, the `'close'`
        // event from a torn-down OLD socket fires on next tick — AFTER a NEW
        // socket has connected — and clobbers the new socket's "connected"
        // status. Result: the user sees the red dot one tick after a successful
        // reconnect.
        sock.on('connect', () => {
            if (this._socket !== sock) return;
            log.info(`[GsViewer] connected to bridge ${GsViewerProvider.DEFAULT_HOST}:${GsViewerProvider.DEFAULT_PORT}`);
            this._panel?.webview.postMessage({ type: 'status', connected: true });
        });

        sock.on('data', (chunk) => {
            if (this._socket !== sock) return;
            this._onData(chunk);
        });

        sock.on('error', (err) => {
            if (this._socket !== sock) return;
            log.warn(`[GsViewer] socket error: ${err.message}`);
            this._panel?.webview.postMessage({ type: 'status', connected: false, error: err.message });
        });

        sock.on('close', () => {
            if (this._socket !== sock) return;
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

                if (this._frameCount === 0) {
                    getOutputChannel().info(`[GsViewer] first frame received: ${width}x${height} codec=${codec} ${payload.length}B`);
                }
                this._frameCount++;

                // Copy to a fresh buffer so we can hand its underlying ArrayBuffer
                // to postMessage as a transferable. (slice() over a shared
                // pool would keep _readBuf pinned.)
                const out = Buffer.from(payload);
                // VS Code webview message passing does not preserve Node's
                // Buffer type. Send a plain ArrayBuffer slice so the webview
                // receives stable binary bytes (instead of an object that can
                // decode as zero-length data).
                const payloadBuffer = out.buffer.slice(
                    out.byteOffset,
                    out.byteOffset + out.byteLength,
                );

                this._panel?.webview.postMessage(
                    { type: 'frame', frameIdx, width, height, codec, payload: payloadBuffer },
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
    <title>Preview</title>
    <style>
        :root { color-scheme: dark; --bg:#181818; --panel-hi:#252526; --border:var(--vscode-panel-border,#313131); --text:var(--vscode-foreground,#d4d4d4); --muted:var(--vscode-descriptionForeground,#8f8f8f); --accent:#007acc; --live:#4ec94e; --idle:#cca700; --dead:#f48771; }
        * { box-sizing: border-box; }
        html, body { margin:0; padding:0; height:100%; background:var(--bg); color:var(--text); font-family:var(--vscode-font-family,'Segoe UI',sans-serif); font-size:12px; }
        body { display:flex; flex-direction:column; overflow:hidden; }
        #toolbar { min-height:64px; padding:0 22px; border-bottom:1px solid var(--vscode-panel-border,#2d2d30); display:flex; gap:16px; align-items:center; background:#181818; }
        .brand { display:flex; align-items:center; gap:10px; min-width:0; flex:1 1 auto; }
        .brand-mark { width:36px; height:36px; display:grid; place-items:center; flex:0 0 36px; border:1px solid #0e639c; border-radius:7px; background:linear-gradient(135deg,#0e639c,#174b70); color:#fff; }
        .brand-mark svg { display:block; width:22px; height:22px; }
        .brand-title { color:var(--vscode-foreground,#f2f2f2); font-size:15px; font-weight:700; line-height:1.25; letter-spacing:0; } .brand-subtitle { color:var(--muted); font-size:11px; line-height:1.25; margin-top:3px; }
        .connection { display:inline-flex; align-items:center; gap:6px; min-width:0; max-width:min(220px,100%); padding:4px 8px; border:1px solid var(--border); border-radius:10px; color:var(--muted); font-size:10px; text-transform:uppercase; letter-spacing:.06em; white-space:nowrap; }
        .connection .dot { width:7px; height:7px; border-radius:50%; flex:none; background:var(--dead); }
        .connection.live { color:#b5cea8; border-color:#365d36; } .connection.live .dot { background:var(--live); box-shadow:0 0 6px rgba(78,201,78,.65); }
        .connection.dead { color:var(--muted); } .connection.dead .dot { background:var(--dead); }
        #text { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
        .actions { display:flex; flex-wrap:wrap; justify-content:flex-end; gap:6px; margin-left:0; }
        button { min-height:28px; display:inline-flex; align-items:center; justify-content:center; gap:5px; background:#333; color:var(--text); border:1px solid #4a4a4a; border-radius:3px; padding:5px 9px; cursor:pointer; font:inherit; white-space:nowrap; }
        button:hover { background:#414141; border-color:#666; } button:active { background:#0e639c; } button.primary { background:#0e639c; border-color:#1177bb; color:#fff; } button.primary:hover { background:#1177bb; }
        #stage { position:relative; flex:1 1 auto; min-height:0; display:flex; align-items:center; justify-content:center; overflow:hidden; padding:20px; background-color:#101010; background-image:linear-gradient(rgba(255,255,255,.025) 1px,transparent 1px),linear-gradient(90deg,rgba(255,255,255,.025) 1px,transparent 1px); background-size:18px 18px; }
        .frame-shell { position:relative; max-width:100%; max-height:100%; display:flex; border:1px solid #333; box-shadow:0 12px 35px rgba(0,0,0,.35); background:#000; }
        canvas { max-width:100%; max-height:100%; image-rendering:pixelated; background:#000; } body.smooth canvas { image-rendering:auto; }
        #resolution { position:absolute; right:8px; bottom:8px; padding:3px 6px; border-radius:3px; color:#ccc; background:rgba(0,0,0,.68); font-family:var(--vscode-editor-font-family,monospace); font-size:10px; }
        #overlay { position:absolute; inset:0; display:flex; align-items:center; justify-content:center; color:var(--text); pointer-events:none; text-align:center; padding:18px; }
        #overlay .box { max-width:500px; padding:18px 20px; border-radius:7px; border:1px solid #3a3a3a; background:rgba(30,30,30,.94); box-shadow:0 12px 35px rgba(0,0,0,.25); line-height:1.45; } #overlay .hint { color:var(--muted); font-size:11px; margin-top:7px; }
        #footer { min-height:25px; display:flex; align-items:center; gap:16px; padding:0 10px; background:var(--accent); color:#fff; font-size:11px; } #meta { margin-left:auto; font-family:var(--vscode-editor-font-family,monospace); white-space:nowrap; }
        @media (max-width:760px) { #toolbar { min-height:64px; padding:0 14px; gap:10px; } .brand { min-width:150px; } .connection { flex:1 1 180px; } .actions { justify-content:flex-start; } .brand-subtitle { display:none; } }
    </style>
</head>
<body>
    <header id="toolbar">
        <div class="brand"><div class="brand-mark"><svg xmlns="http://www.w3.org/2000/svg" width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="20" height="14" x="2" y="3" rx="2" /><line x1="8" x2="16" y1="21" y2="21" /><line x1="12" x2="12" y1="17" y2="21" /></svg></div><div><div class="brand-title">Preview</div><div class="brand-subtitle">Live PCSX2 framebuffer monitor</div></div></div>
        <div id="status" class="connection"><span class="dot" id="dot"></span><span id="text">connecting…</span></div>
        <div class="actions">
            <button id="smoothing" title="Toggle pixel smoothing"><span>Smoothing</span></button><button id="fullscreen" title="Toggle fullscreen stage"><span>Fullscreen</span></button>
            <button id="screenshot" class="primary" title="Save a screenshot in PCSX2"><span>Screenshot</span></button><button id="controller" title="Open the virtual controller"><span>Controller</span></button><button id="reconnect" title="Reconnect to the GS bridge"><span>Reconnect</span></button>
        </div>
    </header>
    <main id="stage"><div class="frame-shell"><canvas id="canvas" width="640" height="480"></canvas><div id="resolution">640 × 480</div></div><div id="overlay"><div class="box" id="overlayBox">Waiting for connection to bridge…</div></div></main>
    <footer id="footer"><span>GS stream :8766</span><span>MJPEG decoder</span><span id="meta">waiting for frames</span></footer>

    <script nonce="${nonce}">
        const vscode = acquireVsCodeApi();
        const status = document.getElementById('status');
        const dot = document.getElementById('dot');
        const text = document.getElementById('text');
        const meta = document.getElementById('meta');
        const canvas = document.getElementById('canvas');
        const ctx = canvas.getContext('2d', { alpha: false });
        const overlay = document.getElementById('overlay');
        const overlayBox = document.getElementById('overlayBox');
        const resolution = document.getElementById('resolution');

        // Bridge socket state, separate from frame-flow state below.
        let connected = false;
        let lastErr = '';

        // Frame-flow tracking — used to colour the dot yellow when the socket
        // is up but no frames are arriving (PCSX2 not running, or its build
        // pre-dates GSStreamServer, or no game is rendering yet).
        let firstFrameAt = 0;
        let lastFrameAt = 0;
        let framesShown = 0;
        let frameTimes = [];           // sliding window for FPS calc
        let decodeErrors = 0;
        let busy = false;              // single in-flight decode; drop frames if behind
        let lastWidth = 0, lastHeight = 0;

        const STALE_MS = 2000;         // no frames for this long → yellow dot

        function showOverlay(message, hintHtml) {
            overlay.style.display = 'flex';
            overlayBox.innerHTML =
                '<div>' + message + '</div>' +
                (hintHtml ? '<div class="hint">' + hintHtml + '</div>' : '');
        }
        function hideOverlay() { overlay.style.display = 'none'; }

        function refreshUi() {
            const now = performance.now();
            const ageMs = lastFrameAt ? (now - lastFrameAt) : Infinity;
            // Match the Controller page's concise connection badge. Frame-flow
            // details remain available in the footer and stage overlay.
            status.className = 'connection ' + (connected ? 'live' : 'dead');
            dot.className = 'dot';
            text.textContent = connected ? 'connected' : 'offline';

            // Meta (right-aligned)
            const dur = lastFrameAt - firstFrameAt;
            const fps = (framesShown > 1 && dur > 0) ? (1000 * (framesShown - 1) / dur).toFixed(0) : '–';
            const recentFps = (frameTimes.length >= 2)
                ? (1000 * (frameTimes.length - 1) / (frameTimes[frameTimes.length - 1] - frameTimes[0])).toFixed(0)
                : fps;
            const dims = lastWidth ? (lastWidth + 'x' + lastHeight) : '–';
            const ageStr = lastFrameAt ? ('  age: ' + (ageMs < 1000 ? Math.round(ageMs) + 'ms' : (ageMs / 1000).toFixed(1) + 's')) : '';
            const errStr = decodeErrors ? ('   decode errors: ' + decodeErrors) : '';
            meta.textContent = dims + '   ~' + recentFps + ' fps   frame #' + framesShown + ageStr + errStr;
            resolution.textContent = lastWidth ? (lastWidth + ' × ' + lastHeight) : 'waiting';

            // Overlay
            if (!connected) {
                showOverlay(
                    'Bridge socket disconnected',
                    'Make sure CodeDesigner.Ps2.Bridge is running. Click <b>Reconnect</b> to retry.'
                );
            } else if (framesShown === 0) {
                showOverlay(
                    'Waiting for frames from PCSX2…',
                    'Connected to the bridge on :8766, but no frames have arrived yet.<br>' +
                    'Check that: PCSX2 is running with the GSStreamServer-enabled build, ' +
                    'PINE is attached (use the disasm panel\\'s Run / STARTPCSX2), and a game is actually rendering.'
                );
            } else if (ageMs >= STALE_MS) {
                showOverlay('No frames for ' + (ageMs / 1000).toFixed(1) + 's',
                            'PCSX2 may have paused, lost focus, or stopped streaming.');
            } else {
                hideOverlay();
            }
        }

        async function drawFrame(payload, width, height, codec) {
            if (busy) return; // drop, decoder is behind
            busy = true;
            try {
                if (codec !== 1) {
                    decodeErrors++;
                    vscode.postMessage({ type: 'decodeError', error: 'unknown codec ' + codec });
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
                const now = performance.now();
                if (firstFrameAt === 0) firstFrameAt = now;
                lastFrameAt = now;
                frameTimes.push(now);
                if (frameTimes.length > 30) frameTimes.shift();
                refreshUi();
            } catch (err) {
                decodeErrors++;
                const msg = (err && err.message) ? err.message : String(err);
                vscode.postMessage({ type: 'decodeError', error: msg + ' (payload ' + payload.length + 'B, ' + width + 'x' + height + ')' });
            } finally {
                busy = false;
            }
        }

        window.addEventListener('message', (event) => {
            const msg = event.data;
            if (!msg) return;
            if (msg.type === 'status') {
                connected = !!msg.connected;
                lastErr = msg.error || '';
                if (!connected) {
                    // A reconnect resets the frame-flow counters so the
                    // overlay accurately reflects the new session.
                    firstFrameAt = 0;
                    lastFrameAt = 0;
                    framesShown = 0;
                    frameTimes = [];
                }
                refreshUi();
                return;
            }
            if (msg.type === 'frame') {
                const payload = msg.payload instanceof Uint8Array
                    ? msg.payload
                    : msg.payload instanceof ArrayBuffer
                        ? new Uint8Array(msg.payload)
                        : new Uint8Array(msg.payload?.data ?? msg.payload);
                drawFrame(payload, msg.width, msg.height, msg.codec);
            }
        });

        document.getElementById('smoothing').addEventListener('click', () => { document.body.classList.toggle('smooth'); });
        document.getElementById('fullscreen').addEventListener('click', () => { if (document.fullscreenElement) document.exitFullscreen(); else document.getElementById('stage').requestFullscreen(); });

        document.getElementById('reconnect').addEventListener('click', () => {
            vscode.postMessage({ type: 'reconnect' });
        });

        document.getElementById('screenshot').addEventListener('click', () => {
            vscode.postMessage({ type: 'screenshot' });
        });

        document.getElementById('controller').addEventListener('click', () => {
            vscode.postMessage({ type: 'controller' });
        });

        // Tick the UI every second so the "no frames for Ns" indicator
        // updates even when no events are arriving.
        setInterval(refreshUi, 1000);

        refreshUi();
    </script>
</body>
</html>`;
    }
}
