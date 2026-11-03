import * as vscode from 'vscode';
import * as fs from 'fs';
import { getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { SnapshotEntry } from '../../webview/disasm-panel/types';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';

/** Minimal interface the SnapshotManagerProvider needs from DisasmPanelProvider. */
export interface SnapshotDataDelegate {
    /**
     * Async list of PCSX2 .p2s save states from `pcsx2.path.savestates`,
     * already filtered to the currently selected game and sorted newest-first.
     */
    getSnapshots(): Promise<SnapshotEntry[]>;
    openSnapshot(snapshotPath: string): Promise<void>;
    reloadSnapshot(snapshotPath: string): Promise<void>;
    /**
     * Read the embedded `Screenshot.png` from a .p2s archive via the bridge.
     * Returns base64 PNG bytes (no data-URI prefix) or `null` when no
     * screenshot is embedded / the bridge is unreachable.
     */
    getP2sScreenshot(p2sPath: string): Promise<string | null>;
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
        void this._pushList(this._panel.webview);
    }

    private async _pushList(webview: vscode.Webview): Promise<void> {
        try {
            const snapshots = await this.delegate.getSnapshots();
            webview.postMessage({ type: 'snapshotList', snapshots });
        } catch (err: any) {
            webview.postMessage({
                type: 'snapshotError',
                message: `Could not list snapshots: ${err?.message ?? err}`,
            });
        }
    }

    // ── Message dispatch ──────────────────────────────────────────────────────

    private async _handleMessage(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        const type = msg['type'] as string | undefined;

        switch (type) {
            case 'ready':
            case 'refresh':
                await this._pushList(webview);
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
                    // Best-effort: also drop the sibling .extracted/ companion the
                    // bridge generates on demand for the hex viewer.
                    const sidecar = snapPath + '.extracted';
                    try { fs.rmSync(sidecar, { recursive: true, force: true }); } catch { /* may not exist */ }
                    webview.postMessage({ type: 'snapshotActionResult', message: 'Snapshot deleted.' });
                    await this._pushList(webview);
                } catch (err: any) {
                    webview.postMessage({ type: 'snapshotError', message: `Delete failed: ${err?.message ?? err}` });
                }
                break;
            }

            case 'snapshotScreenshot': {
                const p2sPath = msg['path'] as string;
                try {
                    const data = await this.delegate.getP2sScreenshot(p2sPath);
                    webview.postMessage({ type: 'snapshotScreenshot', path: p2sPath, data });
                } catch (err: any) {
                    webview.postMessage({
                        type: 'snapshotScreenshot',
                        path: p2sPath,
                        data: null,
                        error: err?.message ?? String(err),
                    });
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
               img-src ${webview.cspSource} data:;
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
