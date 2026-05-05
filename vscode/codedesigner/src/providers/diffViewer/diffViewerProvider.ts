import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { SnapshotEntry } from '../../webview/disasm-panel/types';
import { SnapshotDataDelegate } from '../snapshotManager/snapshotManagerProvider';

const MAX_DIFF_ROWS = 10_000;

export class DiffViewerProvider {
    private _panel: vscode.WebviewPanel | undefined;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly delegate: SnapshotDataDelegate,
    ) {}

    /** Open (or reveal) the Diff Viewer panel, optionally pre-loading a snapshot. */
    public open(snapshotA?: SnapshotEntry | null, snapshotB?: SnapshotEntry | null): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            if (snapshotA || snapshotB) {
                this._panel.webview.postMessage({ type: 'preloadSnapshots', snapshotA, snapshotB });
            }
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.diffViewer',
            '⟺ Memory Diff Viewer',
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

        if (snapshotA || snapshotB) {
            // Will be sent once the webview posts 'ready'.
            this._pendingPreload = { snapshotA: snapshotA ?? null, snapshotB: snapshotB ?? null };
        }
    }

    private _pendingPreload: { snapshotA: SnapshotEntry | null; snapshotB: SnapshotEntry | null } | null = null;

    // ── Message dispatch ──────────────────────────────────────────────────────

    private async _handleMessage(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        const type = msg['type'] as string | undefined;

        switch (type) {
            case 'ready':
                webview.postMessage({
                    type: 'snapshotList',
                    snapshots: this.delegate.getSnapshots(),
                });
                if (this._pendingPreload) {
                    webview.postMessage({ type: 'preloadSnapshots', ...this._pendingPreload });
                    this._pendingPreload = null;
                }
                break;

            case 'startDiff':
                await this._handleStartDiff(msg['pathA'] as string, msg['pathB'] as string, webview);
                break;

            case 'browseSource':
            case 'browseTarget': {
                const side = type === 'browseSource' ? 'A' : 'B';
                const uris = await vscode.window.showOpenDialog({
                    canSelectFiles:   true,
                    canSelectFolders: false,
                    canSelectMany:    false,
                    filters:          { 'Binary dumps': ['bin', 'raw'] },
                    title:            `Select memory dump for Snapshot ${side}`,
                });
                if (!uris || uris.length === 0) { break; }
                const snap: SnapshotEntry = {
                    path:      uris[0].fsPath,
                    label:     path.basename(uris[0].fsPath),
                    name:      path.basename(uris[0].fsPath),
                    isoName:   '',
                    date:      '',
                    timestamp: '',
                };
                webview.postMessage({ type: 'browseResult', side, snapshot: snap });
                break;
            }

            case 'gotoInEditor':
                // Navigate any open disasm editor to the given address.
                vscode.commands.executeCommand('codedesigner.gotoAddress', msg['address']);
                break;

            case 'getSnapshotList':
                webview.postMessage({
                    type: 'snapshotList',
                    snapshots: this.delegate.getSnapshots(),
                });
                break;

            default:
                getOutputChannel().warn(`[DiffViewer] Unknown message type: ${type}`);
                break;
        }
    }

    private async _handleStartDiff(pathA: string, pathB: string, webview: vscode.Webview): Promise<void> {
        if (!pathA || !pathB) {
            webview.postMessage({ type: 'diffError', message: 'Both snapshots must be selected.' });
            return;
        }

        try {
            // Resolve .bin file paths — snapshots may be directories; pick first .bin inside.
            const resolveFile = (p: string): string => {
                try {
                    const stat = fs.statSync(p);
                    if (stat.isDirectory()) {
                        const bins = fs.readdirSync(p).filter(f => f.endsWith('.bin'));
                        if (bins.length > 0) { return path.join(p, bins[0]); }
                        throw new Error(`No .bin file found in ${p}`);
                    }
                } catch { /* p is a file path */ }
                return p;
            };

            const fileA = resolveFile(pathA);
            const fileB = resolveFile(pathB);

            const [bufA, bufB] = await Promise.all([
                fs.promises.readFile(fileA),
                fs.promises.readFile(fileB),
            ]);

            const len = Math.min(bufA.length, bufB.length);
            const rows: Array<{ address: string; valueA: string; valueB: string }> = [];

            // Compare 4-byte words; stop collecting after MAX_DIFF_ROWS changed addresses.
            for (let i = 0; i + 3 < len && rows.length < MAX_DIFF_ROWS; i += 4) {
                const wordA = bufA.readUInt32LE(i);
                const wordB = bufB.readUInt32LE(i);
                if (wordA !== wordB) {
                    rows.push({
                        address: i.toString(16).toUpperCase().padStart(8, '0'),
                        valueA:  wordA.toString(16).toUpperCase().padStart(8, '0'),
                        valueB:  wordB.toString(16).toUpperCase().padStart(8, '0'),
                    });
                }
            }

            webview.postMessage({ type: 'diffResults', rows });
        } catch (err: any) {
            webview.postMessage({ type: 'diffError', message: err?.message ?? String(err) });
        }
    }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'webview', 'diff-viewer.js')
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
<app-diff-viewer-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Diff Viewer…</p></app-diff-viewer-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
