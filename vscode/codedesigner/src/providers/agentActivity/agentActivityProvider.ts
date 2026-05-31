import * as vscode from 'vscode';
import {
    SocketClient,
    AiActivityEvent,
    AiSessionStartEvent,
    AiLabelUpdateEvent,
    AiPacketEvent,
} from '../../socketClient';
import { getNonce } from '../../utils/nonce';

/** Auto-resolved game context shown in the panel header (never typed by the user). */
export interface ActivityGameContext {
    gameName: string;
    gameDiscId: string;
}

type ActivityKind = 'activity' | 'session' | 'label' | 'packet';

interface ActivityEntry {
    at: number;          // epoch ms
    kind: ActivityKind;
    text: string;        // human summary
    address?: number;    // for kind === 'activity'
    region?: string;
}

/**
 * Live "AI activity" feed for debugging multi-agent sessions. Subscribes to the
 * bridge's unsolicited ai_activity / ai_session_start / ai_label_update pushes
 * and streams them into a lightweight webview — a running log of what the AI is
 * doing (which addresses it touches, when it labels, when a session starts).
 *
 * Deliberately self-contained (shared.css + VS Code theme vars + a tiny nonce'd
 * script) rather than an Angular bundle: it shows backend-pushed events and never
 * collects input, so it needs no game/serial field (the header is auto-resolved).
 */
export class AgentActivityProvider {
    private _panel: vscode.WebviewPanel | undefined;
    private readonly _log: ActivityEntry[] = [];
    private static readonly MAX = 200;

    constructor(
        private readonly _extensionUri: vscode.Uri,
        private readonly _socketClient: SocketClient,
        /** Pulls the currently loaded game so the header needs no manual input. */
        private readonly _gameContextFn: () => ActivityGameContext | null,
    ) {
        // Opt into ai_activity pushes whenever a connection is (re)established.
        _socketClient.onConnected(() => { void _socketClient.subscribeActivity(); });

        _socketClient.onActivityNotification((e: AiActivityEvent) => {
            this._record({
                at: Date.now(), kind: 'activity', address: e.address, region: e.region,
                text: `${e.command} @ ${this._hex(e.address)}${e.region ? `  (${e.region})` : ''}`,
            });
        });
        _socketClient.onSessionStart((_e: AiSessionStartEvent) => {
            this._record({ at: Date.now(), kind: 'session', text: 'AI session started' });
        });
        _socketClient.onLabelUpdate((e: AiLabelUpdateEvent) => {
            const set = e.setName ? `  set '${e.setName}'` : '';
            this._record({ at: Date.now(), kind: 'label', text: `${e.command}${set}` });
        });
        _socketClient.onPacketActivity((e: AiPacketEvent) => {
            const detail = e.enable !== undefined ? (e.enable ? '  capture on' : '  capture off')
                         : e.direction !== undefined ? `  dir ${e.direction}` : '';
            this._record({ at: Date.now(), kind: 'packet', text: `${e.command}${detail}` });
        });
    }

    /** Open (or reveal) the activity panel. */
    public open(): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.agentActivity',
            '🛰 AI Activity',
            vscode.ViewColumn.One,
            { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [this._extensionUri] },
        );
        this._panel = panel;
        panel.onDidDispose(() => { this._panel = undefined; });

        panel.webview.onDidReceiveMessage((msg: Record<string, unknown>) => {
            switch (msg['type']) {
                case 'ready':
                    this._postInit();
                    break;
                case 'clear':
                    this._log.length = 0;
                    break;
            }
        });

        panel.webview.html = this._buildHtml();
        // Make sure the server is pushing activity even if Follow-AI was never on.
        void this._socketClient.subscribeActivity();
    }

    // ── internals ─────────────────────────────────────────────────────────────

    private _record(entry: ActivityEntry): void {
        this._log.push(entry);
        if (this._log.length > AgentActivityProvider.MAX) { this._log.shift(); }
        this._panel?.webview.postMessage({ type: 'append', entry });
    }

    private _postInit(): void {
        const game = this._gameContextFn();
        this._panel?.webview.postMessage({
            type:    'init',
            entries: this._log,
            game:    game ? `${game.gameName} (${game.gameDiscId})` : null,
            connected: this._socketClient.isConnected(),
        });
    }

    private _hex(a: number): string {
        return '0x' + (a >>> 0).toString(16).toUpperCase().padStart(8, '0');
    }

    private _buildHtml(): string {
        const nonce = getNonce();
        // Self-contained page (src/** is excluded from the packaged .vsix, so the
        // shared.css file can't be linked). The .lm-tool-btn primitive is inlined
        // verbatim from src/webview/shared/styles/shared.css; everything else uses
        // VS Code theme vars — same convention as the bundled webviews.
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>
  /* lm-tool-btn (from shared.css) */
  .lm-tool-btn {
    background: #3c3c3c; color: #d4d4d4; border: 1px solid #555; border-radius: 3px;
    padding: 3px 8px; font-size: 11px; cursor: pointer; white-space: nowrap; font-family: inherit;
  }
  .lm-tool-btn:hover:not(:disabled) { background: #4c4c4c; }
  .lm-tool-btn:disabled { opacity: .45; cursor: default; }
  body { padding: 0; }
  .aa-bar {
    display: flex; align-items: center; gap: 8px;
    padding: 6px 10px; position: sticky; top: 0;
    background: var(--vscode-editor-background, #1e1e1e);
    border-bottom: 1px solid var(--vscode-panel-border, #333);
  }
  .aa-bar .aa-game { color: var(--vscode-descriptionForeground, #999); font-size: 12px; }
  .aa-dot { width: 8px; height: 8px; border-radius: 50%; background: #f48771; }
  .aa-dot.on { background: #89d185; }
  .aa-list { padding: 4px 0; }
  .aa-row { display: flex; gap: 8px; padding: 2px 10px; align-items: baseline; }
  .aa-row:hover { background: var(--vscode-list-hoverBackground, #2a2d2e); }
  .aa-time { color: var(--vscode-descriptionForeground, #888); font-size: 11px; min-width: 64px; }
  .aa-tag { font-size: 10px; text-transform: uppercase; min-width: 56px; }
  .aa-tag.activity { color: #569cd6; }
  .aa-tag.session  { color: #c586c0; }
  .aa-tag.label    { color: #89d185; }
  .aa-tag.packet   { color: #d7ba7d; }
  .aa-text { white-space: pre-wrap; }
  .aa-empty { color: var(--vscode-descriptionForeground, #888); padding: 16px; font-size: 12px; }
</style>
</head>
<body>
<div class="aa-bar">
  <span class="aa-dot" id="dot" title="bridge connection"></span>
  <span class="aa-game" id="game">No game loaded</span>
  <span style="flex:1"></span>
  <button class="lm-tool-btn" id="clear">Clear</button>
</div>
<div class="aa-list" id="list"><div class="aa-empty" id="empty">Waiting for AI activity… (start an MCP session)</div></div>
<script nonce="${nonce}">
(function () {
  const vscode = acquireVsCodeApi();
  const list = document.getElementById('list');
  const empty = document.getElementById('empty');
  const dot = document.getElementById('dot');
  const gameEl = document.getElementById('game');
  document.getElementById('clear').addEventListener('click', function () {
    list.innerHTML = '';
    if (empty) { list.appendChild(empty); empty.style.display = ''; }
    vscode.postMessage({ type: 'clear' });
  });
  function esc(s){return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');}
  function fmtTime(ms){ const d = new Date(ms); return d.toTimeString().slice(0,8); }
  function addRow(e){
    if (empty) { empty.style.display = 'none'; }
    const atBottom = (window.innerHeight + window.scrollY) >= (document.body.scrollHeight - 4);
    const row = document.createElement('div'); row.className = 'aa-row';
    row.innerHTML =
      '<span class="aa-time">' + esc(fmtTime(e.at)) + '</span>' +
      '<span class="aa-tag ' + esc(e.kind) + '">' + esc(e.kind) + '</span>' +
      '<span class="aa-text">' + esc(e.text) + '</span>';
    list.appendChild(row);
    if (atBottom) { window.scrollTo(0, document.body.scrollHeight); }
  }
  window.addEventListener('message', function (ev) {
    const m = ev.data;
    if (m.type === 'init') {
      list.innerHTML = ''; if (empty) { list.appendChild(empty); empty.style.display = m.entries.length ? 'none' : ''; }
      gameEl.textContent = m.game || 'No game loaded';
      dot.className = 'aa-dot' + (m.connected ? ' on' : '');
      (m.entries || []).forEach(addRow);
    } else if (m.type === 'append') {
      dot.className = 'aa-dot on';
      addRow(m.entry);
    }
  });
  vscode.postMessage({ type: 'ready' });
})();
</script>
</body>
</html>`;
    }
}
