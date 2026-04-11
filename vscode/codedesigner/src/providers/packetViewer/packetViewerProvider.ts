import * as vscode from 'vscode';
import { getOutputChannel } from '../../extension';
import { SocketClient } from '../../socketClient';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';

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
            }
        } catch (err: unknown) {
            const msg = err instanceof Error ? err.message : String(err);
            getOutputChannel().error(`[PacketViewer] ${actionType} failed: ${msg}`);
            webview.postMessage({ type: 'packetViewerError', message: msg });
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
