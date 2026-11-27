import * as vscode from 'vscode';
import { getOutputChannel } from '../../extension';
import { SocketClient } from '../../socketClient';
import { getNonce } from '../../utils/nonce';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';
import type { StopStream } from '../../ps2debug/directEmulator';

/**
 * GS framebuffer viewer: Ps2Emulator's display, from its `Media.Frames`
 * stream (raw RGBA, codec 2), drawn on the webview's canvas.
 */
export class GsViewerProvider {
    public static readonly viewType = 'codedesigner.gsViewer';

    /** Frame rate asked of a directly connected Ps2Emulator (raw RGBA is ~1 MB a frame). */
    private static readonly DIRECT_MAX_FPS = 30;

    private _panel: vscode.WebviewPanel | undefined;
    /** Stops the direct emulator's frame stream, while one is open. */
    private _stopFrames: StopStream | undefined;
    private _frameCount = 0;

    constructor(
        private readonly _extensionUri: vscode.Uri,
        private readonly _socketClient?: SocketClient,
    ) {}

    /** Follows Ps2Emulator's frames when a direct connection comes or goes. */
    public notifyConnectionChanged(): void {
        const direct = !!this._socketClient?.getDirect();
        if (this._panel && direct !== !!this._stopFrames) {
            this._teardownSocket();
            this._connect();
        }
        void this._sendAudioState();
    }

    /**
     * Open (or focus) the GS frame viewer panel and follow Ps2Emulator's frames.
     * Safe to call repeatedly — re-uses an existing panel and re-opens the
     * stream if it dropped.
     */
    public open(): void {
        const log = getOutputChannel();

        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.Active);
            // Re-open the stream if it ended while the panel was idle.
            if (!this._stopFrames) {
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

        // The webview asks for "reconnect" if it sees stale frames.
        this._panel.webview.onDidReceiveMessage((msg) => {
            if (msg?.type === 'reconnect') {
                log.info('[GsViewer] webview requested reconnect');
                this._teardownSocket();
                setImmediate(() => {
                    if (this._panel) this._connect();
                });
            } else if (msg?.type === 'screenshot') {
                vscode.commands.executeCommand('codedesigner.saveScreenshot');
            } else if (msg?.type === 'controller') {
                vscode.commands.executeCommand('codedesigner.openJokerMaker');
            } else if (msg?.type === 'setMuted') {
                void this._sendAudioState(!!msg.muted);
            } else if (msg?.type === 'ready') {
                void this._sendAudioState();
            } else if (msg?.type === 'decodeError') {
                log.warn(`[GsViewer] webview decode error: ${msg.error}`);
            }
        });

        this._connect();
    }

    private _connect(): void {
        this._frameCount = 0;
        const direct = this._socketClient?.getDirect();
        if (direct) {
            this._connectDirect(direct);
        } else {
            this._panel?.webview.postMessage({ type: 'status', connected: false, error: 'Ps2Emulator is not running: Run it from the panel.' });
        }
    }

    /** Frames from a directly connected Ps2Emulator (raw RGBA, codec 2). */
    private _connectDirect(direct: NonNullable<ReturnType<SocketClient['getDirect']>>): void {
        const log = getOutputChannel();
        log.info(`[GsViewer] following Ps2Emulator frames at ${direct.address}`);
        this._panel?.webview.postMessage({ type: 'status', connected: true });
        let stop: StopStream | undefined = undefined;
        stop = direct.frames(GsViewerProvider.DIRECT_MAX_FPS, frame => {
            this._frameCount++;
            const out = frame.rgba;
            const payload = out.buffer.slice(out.byteOffset, out.byteOffset + out.byteLength);
            this._panel?.webview.postMessage(
                { type: 'frame', frameIdx: frame.field, width: frame.width, height: frame.height, codec: 2, payload },
            );
        }, error => {
            if (this._stopFrames !== stop) return;
            this._stopFrames = undefined;
            if (error) log.warn(`[GsViewer] frame stream: ${error.message}`);
            this._panel?.webview.postMessage({ type: 'status', connected: false, error: error?.message });
        });
        this._stopFrames = stop;
    }

    /** Sound control: Ps2Emulator makes no sound yet (its SPU2 does not synthesise), so there is none. */
    private async _sendAudioState(_muted?: boolean): Promise<void> {
        this._panel?.webview.postMessage({ type: 'audio', available: false, error: 'Ps2Emulator has no sound yet.' });
    }

    private _teardownSocket(): void {
        const stopFrames = this._stopFrames;
        this._stopFrames = undefined;
        stopFrames?.();
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
        button:disabled { opacity:.45; cursor:default; pointer-events:none; }
        button svg { width:14px; height:14px; flex:none; } #sound.muted { color:#f48771; border-color:#7d3c2a; }
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
            <button id="sound" title="Sound (Ps2Emulator has none yet)" disabled><svg id="soundIcon" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"></svg><span id="soundText">Sound</span></button>
            <button id="screenshot" class="primary" title="Save the displayed frame as a PNG"><span>Screenshot</span></button><button id="controller" title="Open the virtual controller"><span>Controller</span></button><button id="reconnect" title="Reconnect to the GS bridge"><span>Reconnect</span></button>
        </div>
    </header>
    <main id="stage"><div class="frame-shell"><canvas id="canvas" width="640" height="480"></canvas><div id="resolution">640 × 480</div></div><div id="overlay"><div class="box" id="overlayBox">Waiting for connection to bridge…</div></div></main>
    <footer id="footer"><span>Ps2Emulator frames</span><span>RGBA</span><span id="meta">waiting for frames</span></footer>

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

        // Stream state, separate from frame-flow state below.
        let connected = false;
        let lastErr = '';

        // Frame-flow tracking — used to colour the dot yellow when the stream
        // is open but no frames are arriving (nothing displayed yet).
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
                    'Ps2Emulator is not connected',
                    'Run Ps2Emulator from the panel, then click <b>Reconnect</b>.'
                );
            } else if (framesShown === 0) {
                showOverlay(
                    'Waiting for frames from Ps2Emulator…',
                    'Connected, but nothing has been displayed yet (the BIOS or game may still be starting).'
                );
            } else if (ageMs >= STALE_MS) {
                showOverlay('No frames for ' + (ageMs / 1000).toFixed(1) + 's',
                            'Ps2Emulator may be paused.');
            } else {
                hideOverlay();
            }
        }

        async function drawFrame(payload, width, height, codec) {
            if (busy) return; // drop, decoder is behind
            busy = true;
            try {
                if (codec !== 1 && codec !== 2) {
                    decodeErrors++;
                    vscode.postMessage({ type: 'decodeError', error: 'unknown codec ' + codec });
                    return;
                }
                if (canvas.width !== width || canvas.height !== height) {
                    canvas.width = width;
                    canvas.height = height;
                }
                if (codec === 2) {
                    // Raw RGBA8 from Ps2Emulator's debug server.
                    const pixels = new Uint8ClampedArray(payload.buffer, payload.byteOffset, width * height * 4);
                    ctx.putImageData(new ImageData(pixels, width, height), 0, 0);
                } else {
                    const blob = new Blob([payload], { type: 'image/jpeg' });
                    const bitmap = await createImageBitmap(blob);
                    ctx.drawImage(bitmap, 0, 0, width, height);
                    bitmap.close();
                }
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

        // Sound toggle: Ps2Emulator has no sound yet, so it stays disabled.
        const soundBtn = document.getElementById('sound');
        const soundIcon = document.getElementById('soundIcon');
        const soundText = document.getElementById('soundText');
        const SPEAKER = '<path d="M11 5 6 9H2v6h4l5 4V5z" />';
        const WAVES = '<path d="M15.5 8.5a5 5 0 0 1 0 7" /><path d="M19 5a10 10 0 0 1 0 14" />';
        const CROSS = '<line x1="22" x2="16" y1="9" y2="15" /><line x1="16" x2="22" y1="9" y2="15" />';
        let audioMuted = false;
        function renderSound(state) {
            soundBtn.disabled = !state.available || state.pending;
            audioMuted = !!state.muted;
            soundBtn.classList.toggle('muted', state.available && audioMuted);
            soundIcon.innerHTML = SPEAKER + (state.available && audioMuted ? CROSS : WAVES);
            soundText.textContent = !state.available ? 'Sound' : (audioMuted ? 'Muted' : 'Sound on');
            soundBtn.title = !state.available
                ? (state.error || 'No sound.')
                : (audioMuted ? 'Sound is muted. Click to unmute.' : 'Sound is on. Click to mute.');
        }
        soundBtn.addEventListener('click', () => {
            renderSound({ available: true, muted: audioMuted, pending: true });
            vscode.postMessage({ type: 'setMuted', muted: !audioMuted });
        });
        window.addEventListener('message', (event) => {
            if (event.data && event.data.type === 'audio') renderSound(event.data);
        });
        renderSound({ available: false });
        vscode.postMessage({ type: 'ready' });

        // Tick the UI every second so the "no frames for Ns" indicator
        // updates even when no events are arriving.
        setInterval(refreshUi, 1000);

        refreshUi();
    </script>
</body>
</html>`;
    }
}
