import * as vscode from 'vscode';
import * as path from 'path';
import * as os from 'os';
import { getOutputChannel } from '../../extension';
import { SocketClient } from '../../socketClient';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { resolveVimmDownload, downloadVimmRom } from './vimm';

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
                // Open the cheats editor, pre-selecting this game by title.
                vscode.commands.executeCommand(
                    'codedesigner.openCheatsEditor', msg['title'] as string | undefined);
                break;
            case 'downloadGame':
                await this._downloadGame(
                    webview,
                    msg['crc'] as string | undefined,
                    msg['title'] as string | undefined);
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

    // ── ROM download (Vimm's Lair) ────────────────────────────────────────────

    /**
     * Resolve a game's CRC to a Vimm's Lair ROM and stream it to a user-chosen
     * file. Vimm has no API, so the vault is searched by CRC and scraped; the
     * download endpoint only answers requests carrying a vimm.net Referer, so
     * the extension host (not the browser) must perform the transfer.
     */
    private async _downloadGame(
        webview: vscode.Webview,
        crc: string | undefined,
        title: string | undefined,
    ): Promise<void> {
        const label = title?.trim() || 'this game';

        if (!crc || !crc.trim()) {
            webview.postMessage({
                type: 'compatError',
                message: `No CRC recorded for ${label} — cannot look it up on Vimm's Lair.`,
            });
            return;
        }

        try {
            webview.postMessage({
                type: 'compatStatus',
                message: `Looking up ${label} on Vimm's Lair…`,
                busy: false,
            });
            const dl = await resolveVimmDownload(crc);

            const target = await vscode.window.showSaveDialog({
                saveLabel: 'Download ROM',
                title: `Save ${dl.fileName}`,
                defaultUri: vscode.Uri.file(
                    path.join(os.homedir(), 'Downloads', dl.fileName)),
            });
            if (!target) {
                webview.postMessage({ type: 'compatStatus', message: '', busy: false });
                return;
            }

            await vscode.window.withProgress({
                location: vscode.ProgressLocation.Notification,
                title: `Downloading ${dl.fileName}`,
                cancellable: true,
            }, async (progress, token) => {
                let lastPct = 0;
                await downloadVimmRom(
                    dl,
                    target.fsPath,
                    ({ receivedBytes, totalBytes }) => {
                        if (!totalBytes) { return; }
                        const pct = Math.floor((receivedBytes / totalBytes) * 100);
                        if (pct <= lastPct) { return; }
                        const mb = (n: number) => (n / (1024 * 1024)).toFixed(0);
                        progress.report({
                            increment: pct - lastPct,
                            message: `${mb(receivedBytes)} / ${mb(totalBytes)} MB (${pct}%)`,
                        });
                        lastPct = pct;
                    },
                    () => token.isCancellationRequested,
                );
            });

            webview.postMessage({
                type: 'compatStatus',
                message: `Downloaded ${dl.fileName}`,
                busy: false,
            });
            vscode.window.showInformationMessage(
                `Downloaded ${dl.fileName} to ${target.fsPath}`);
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : String(err);
            if (message === 'Download cancelled.') {
                webview.postMessage({
                    type: 'compatStatus', message: 'Download cancelled.', busy: false });
                return;
            }
            getOutputChannel().error('[CompatBrowser] ROM download failed: ' + message);
            webview.postMessage({ type: 'compatError', message: `Download failed: ${message}` });
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
