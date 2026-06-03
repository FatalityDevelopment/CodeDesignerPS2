import * as vscode from 'vscode';
import * as fs from 'fs';
import { getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { SnapshotEntry } from '../../webview/disasm-panel/types';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';

/** Minimal interface the SnapshotManagerProvider needs from DisasmPanelProvider. */
export interface SnapshotDataDelegate {
    getSnapshots(): SnapshotEntry[];
    openSnapshot(snapshotPath: string): Promise<void>;
    reloadSnapshot(snapshotPath: string): Promise<void>;
}

export class SnapshotManagerProvider {
    private _panel: vscode.WebviewPanel | undefined;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly delegate: SnapshotDataDelegate,
    ) {}

    /** Open (or reveal) the Snapshot Manager panel. */
    public open(): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.snapshotManager',
            'Snapshot Manager',
            vscode.ViewColumn.One,
            {
                enableScripts:           true,
                retainContextWhenHidden: true,
                localResourceRoots:      [this.extensionUri],
            }
        );
        applyCodeDesignerPanelIcon(panel, this.extensionUri, 'camera');
        this._panel = panel;

        panel.onDidDispose(() => { this._panel = undefined; });

        panel.webview.onDidReceiveMessage(async (msg: Record<string, unknown>) => {
            await this._handleMessage(msg, panel.webview);
        });

        panel.webview.html = this._buildHtml(panel.webview);
    }

    /** Push an updated snapshot list to the open panel (if any). */
    public notifySnapshotsChanged(): void {
        if (!this._panel) { return; }
        this._panel.webview.postMessage({
            type: 'snapshotList',
            snapshots: this.delegate.getSnapshots(),
        });
    }

    // ── Message dispatch ──────────────────────────────────────────────────────

    private async _handleMessage(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        const type = msg['type'] as string | undefined;

        switch (type) {
            case 'ready':
            case 'refresh':
                webview.postMessage({
                    type: 'snapshotList',
                    snapshots: this.delegate.getSnapshots(),
                });
                break;

            case 'snapshotOpen':
                try {
                    await this.delegate.openSnapshot(msg['path'] as string);
                } catch (err: any) {
                    webview.postMessage({ type: 'snapshotError', message: err?.message ?? String(err) });
                }
                break;

            case 'snapshotReload':
                try {
                    await this.delegate.reloadSnapshot(msg['path'] as string);
                    webview.postMessage({ type: 'snapshotActionResult', message: 'Snapshot reloaded.' });
                } catch (err: any) {
                    webview.postMessage({ type: 'snapshotError', message: err?.message ?? String(err) });
                }
                break;

            case 'snapshotDelete': {
                const snapPath = msg['path'] as string;
                try {
                    fs.rmSync(snapPath, { recursive: true, force: true });
                    webview.postMessage({ type: 'snapshotActionResult', message: 'Snapshot deleted.' });
                    webview.postMessage({
                        type: 'snapshotList',
                        snapshots: this.delegate.getSnapshots(),
                    });
                } catch (err: any) {
                    webview.postMessage({ type: 'snapshotError', message: `Delete failed: ${err?.message ?? err}` });
                }
                break;
            }

            case 'snapshotDiff':
                // Handled by the caller (DisasmPanelProvider) forwarding to DiffViewerProvider.
                // Re-post upward so the parent provider can intercept it.
                getOutputChannel().info(`[SnapshotManager] Diff requested for: ${msg['snapshot']}`);
                break;

            default:
                getOutputChannel().warn(`[SnapshotManager] Unknown message type: ${type}`);
                break;
        }
    }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'webview', 'snapshot-manager.js')
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
<app-snapshot-manager-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Snapshot Manager…</p></app-snapshot-manager-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
