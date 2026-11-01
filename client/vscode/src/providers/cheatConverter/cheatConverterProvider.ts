import * as vscode from 'vscode';
import { getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { SocketClient } from '../../socketClient';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';

/**
 * Standalone Cheat Converter page: decrypts device codes (Action Replay,
 * GameShark, CodeBreaker, Xploder) to raw and encrypts raw codes back, via the
 * bridge's CONVERTCHEAT command.
 */
export class CheatConverterProvider {
    private _panel: vscode.WebviewPanel | undefined;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly socketClient: SocketClient,
    ) {}

    /** Open (or reveal) the Cheat Converter panel. */
    public open(): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.cheatConverter',
            'Cheat Converter',
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
                webview.postMessage({ type: 'context', connected: this.socketClient.isConnected() });
                break;

            case 'convert': {
                if (!this.socketClient.isConnected()) {
                    webview.postMessage({ type: 'convertResult', error: 'Not connected to the CodeDesigner bridge.' });
                    break;
                }
                try {
                    const result = await this.socketClient.convertCheat(
                        msg['codes'] as string,
                        msg['direction'] as 'toRaw' | 'toEngine',
                        (msg['format'] as string | undefined) || undefined);
                    webview.postMessage({ type: 'convertResult', result });
                } catch (err: unknown) {
                    const error = err instanceof Error ? err.message : String(err);
                    getOutputChannel().error(`[CheatConverter] ${error}`);
                    webview.postMessage({ type: 'convertResult', error });
                }
                break;
            }

            case 'copy':
                await vscode.env.clipboard.writeText(msg['text'] as string ?? '');
                webview.postMessage({ type: 'status', message: msg['summary'] as string ?? 'Copied.' });
                break;

            case 'openInEditor': {
                const doc = await vscode.workspace.openTextDocument({ content: msg['text'] as string ?? '' });
                await vscode.window.showTextDocument(doc, { preview: false, viewColumn: vscode.ViewColumn.Beside });
                break;
            }

            default:
                getOutputChannel().warn(`[CheatConverter] Unknown message type: ${type}`);
                break;
        }
    }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'webview', 'cheat-converter.js')
        );
        const nonce = getNonce();
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               img-src ${webview.cspSource} data:;
               style-src 'unsafe-inline';
               script-src ${webview.cspSource} 'nonce-${nonce}';">
<script nonce="${nonce}">${getWebviewErrorHandlerScript()}</script>
</head>
<body>
<app-cheat-converter-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Cheat Converter…</p></app-cheat-converter-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
