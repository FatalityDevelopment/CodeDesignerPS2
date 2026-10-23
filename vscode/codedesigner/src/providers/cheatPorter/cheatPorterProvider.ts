import * as vscode from 'vscode';
import { getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { SocketClient } from '../../socketClient';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';

/** Webview panel that ports cheat codes from one EE RAM dump to another. */
export class CheatPorterProvider {
    private _panel: vscode.WebviewPanel | undefined;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly socketClient: SocketClient,
    ) {}

    /** Open (or reveal) the Cheat Porter panel. */
    public open(): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.cheatPorter',
            'Cheat Porter',
            vscode.ViewColumn.One,
            {
                enableScripts:           true,
                retainContextWhenHidden: true,
                localResourceRoots:      [this.extensionUri],
            }
        );
        applyCodeDesignerPanelIcon(panel, this.extensionUri, 'repeat');
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

        switch (type) {
            case 'ready':
                break;

            case 'browseSource':
            case 'browseTarget': {
                const isSource = type === 'browseSource';
                const uris = await vscode.window.showOpenDialog({
                    canSelectFiles: true, canSelectFolders: false, canSelectMany: false,
                    filters: { 'EE RAM Dumps': ['bin', 'raw', 'dump'], 'All Files': ['*'] },
                    title: isSource ? 'Select Source EE RAM Dump (r0001)' : 'Select Target EE RAM Dump (r0002)',
                });
                if (uris?.[0]) {
                    webview.postMessage({ type: `${type}Result`, filePath: uris[0].fsPath });
                }
                break;
            }

            case 'portCheats':
                await this._handlePortCheats(msg, webview);
                break;

            default:
                getOutputChannel().warn(`[CheatPorter] Unknown message type: ${type}`);
                break;
        }
    }

    private async _handlePortCheats(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        if (!this.socketClient.isConnected()) {
            webview.postMessage({ type: 'portCheatsResult', error: 'Not connected to emulator server.' });
            return;
        }
        try {
            const { results, log } = await this.socketClient.portCheats(
                msg['codes']      as string,
                msg['sourcePath'] as string,
                msg['targetPath'] as string,
            );
            getOutputChannel().info('[Port Cheats]\n' + log);
            webview.postMessage({ type: 'portCheatsResult', results });
        } catch (err: any) {
            const errMsg = err instanceof Error ? err.message : String(err);
            getOutputChannel().error('[Port Cheats] ' + errMsg);
            webview.postMessage({ type: 'portCheatsResult', error: errMsg });
        }
    }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'webview', 'cheat-porter.js')
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
<app-cheat-porter-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Cheat Porter…</p></app-cheat-porter-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
