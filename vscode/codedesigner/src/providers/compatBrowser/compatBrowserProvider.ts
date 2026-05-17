import * as vscode from 'vscode';
import { getOutputChannel } from '../../extension';
import { SocketClient } from '../../socketClient';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';

/**
 * Provider for the PS2 Game Compatibility standalone webview tab.
 *
 * Mirrors PCSX2's published compatibility list into the local database and
 * displays it. On first open the database is auto-synced when empty; the
 * webview's Refresh button re-pulls the list on demand.
 */
export class CompatBrowserProvider {
    private _panel: vscode.WebviewPanel | undefined;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly socketClient: SocketClient,
    ) {}

    /** Open (or reveal) the compatibility browser panel. */
    public open(): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.compatBrowser',
            '🎮 PS2 Game Compatibility',
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
        switch (msg['type'] as string | undefined) {
            case 'ready':
                await this._loadList(webview, /* autoSyncIfEmpty */ true);
                break;
            case 'refresh':
                await this._sync(webview);
                break;
            case 'getCover':
                await this._sendCover(webview, msg['serial'] as string | undefined);
                break;
            case 'openCheats':
                // The cheats screen is built in a follow-up PR — acknowledge for now.
                vscode.window.showInformationMessage(
                    `Cheats for "${msg['title'] ?? 'this game'}" — coming in a future update.`);
                break;
            case 'bootstrapError':
                getOutputChannel().error('[CompatBrowser] Bootstrap error: ' + msg['error']);
                break;
        }
    }

    // ── Compatibility list ────────────────────────────────────────────────────

    private async _loadList(webview: vscode.Webview, autoSyncIfEmpty: boolean): Promise<void> {
        try {
            let games = await this.socketClient.compatList();
            if (games.length === 0 && autoSyncIfEmpty) {
                webview.postMessage({
                    type: 'compatStatus',
                    message: 'No local data — downloading PCSX2 compatibility list…',
                    busy: true,
                });
                await this.socketClient.compatSync();
                games = await this.socketClient.compatList();
            }
            webview.postMessage({ type: 'compatData', games });
        } catch (err: unknown) {
            webview.postMessage({
                type: 'compatError',
                message: err instanceof Error ? err.message : String(err),
            });
        }
    }

    private async _sync(webview: vscode.Webview): Promise<void> {
        try {
            webview.postMessage({ type: 'compatStatus', message: 'Syncing with PCSX2…', busy: true });
            const summary = await this.socketClient.compatSync();
            const games = await this.socketClient.compatList();
            webview.postMessage({ type: 'compatData', games });
            webview.postMessage({ type: 'compatStatus', message: summary, busy: false });
        } catch (err: unknown) {
            webview.postMessage({
                type: 'compatError',
                message: err instanceof Error ? err.message : String(err),
            });
        }
    }

    private async _sendCover(webview: vscode.Webview, serial: string | undefined): Promise<void> {
        if (!serial) { return; }
        try {
            const base64 = await this.socketClient.compatGetCover(serial);
            webview.postMessage({
                type: 'compatCover',
                serial,
                dataUri: base64 ? `data:image/jpeg;base64,${base64}` : '',
            });
        } catch (err: unknown) {
            getOutputChannel().warn(`[CompatBrowser] cover fetch failed for ${serial}: ` +
                (err instanceof Error ? err.message : String(err)));
            // Send an empty cover so the webview stops waiting on this serial.
            webview.postMessage({ type: 'compatCover', serial, dataUri: '' });
        }
    }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'webview', 'compat-browser.js')
        );
        const nonce = getNonce();
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               img-src data:;
               script-src ${webview.cspSource} 'nonce-${nonce}';">
<script nonce="${nonce}">${getWebviewErrorHandlerScript()}</script>
</head>
<body>
<app-compat-browser-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading PS2 Game Compatibility…</p></app-compat-browser-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
