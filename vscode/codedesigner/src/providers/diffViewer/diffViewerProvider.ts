import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { SnapshotEntry } from '../../webview/disasm-panel/types';
import { SnapshotDataDelegate } from '../snapshotManager/snapshotManagerProvider';
import { SocketClient } from '../../socketClient';

const MAX_DIFF_ROWS  = 10_000;
/** 32 MB — full EE RAM */
const EE_RAM_SIZE    = 32 * 1024 * 1024;
const EE_RAM_BASE    = 0x00000000;

export class DiffViewerProvider {
    private _panel: vscode.WebviewPanel | undefined;
    /** Cached live EE RAM buffer captured via "Read Live Memory". */
    private _liveBuffer: Buffer | null = null;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly delegate: SnapshotDataDelegate,
        private readonly socketClient: SocketClient,
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

            case 'startLiveDiff':
                await this._handleStartLiveDiff(msg['pathB'] as string, webview);
                break;

            case 'readLiveMemory':
                await this._handleReadLiveMemory(webview);
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

            case 'porterBrowseSource': {
                const uris = await vscode.window.showOpenDialog({
                    canSelectFiles: true, canSelectFolders: false, canSelectMany: false,
                    filters: { 'EE RAM Dumps': ['bin', 'raw', 'dump'], 'All Files': ['*'] },
                    title: 'Select Source EE RAM Dump (r0001)',
                });
                if (uris?.[0]) {
                    webview.postMessage({ type: 'porterBrowseSourceResult', filePath: uris[0].fsPath });
                }
                break;
            }

            case 'porterBrowseTarget': {
                const uris = await vscode.window.showOpenDialog({
                    canSelectFiles: true, canSelectFolders: false, canSelectMany: false,
                    filters: { 'EE RAM Dumps': ['bin', 'raw', 'dump'], 'All Files': ['*'] },
                    title: 'Select Target EE RAM Dump (r0002)',
                });
                if (uris?.[0]) {
                    webview.postMessage({ type: 'porterBrowseTargetResult', filePath: uris[0].fsPath });
                }
                break;
            }

            case 'portCheats':
                await this._handlePortCheats(msg, webview);
                break;

            case 'gotoInEditor':
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

    // ── Diff handlers ─────────────────────────────────────────────────────────

    private async _handleStartDiff(pathA: string, pathB: string, webview: vscode.Webview): Promise<void> {
        if (!pathA || !pathB) {
            webview.postMessage({ type: 'diffError', message: 'Both snapshots must be selected.' });
            return;
        }
        try {
            const fileA = this._resolveFile(pathA);
            const fileB = this._resolveFile(pathB);
            const [bufA, bufB] = await Promise.all([
                fs.promises.readFile(fileA),
                fs.promises.readFile(fileB),
            ]);
            webview.postMessage({ type: 'diffResults', rows: this._diffBuffers(bufA, bufB) });
        } catch (err: any) {
            webview.postMessage({ type: 'diffError', message: err?.message ?? String(err) });
        }
    }

    private async _handleReadLiveMemory(webview: vscode.Webview): Promise<void> {
        if (!this.socketClient.isConnected()) {
            webview.postMessage({ type: 'liveMemoryError', message: 'Not connected to emulator.' });
            return;
        }
        try {
            getOutputChannel().info('[DiffViewer] Reading live EE RAM…');
            this._liveBuffer = await this.socketClient.readMemory(EE_RAM_BASE, EE_RAM_SIZE, 'ee');
            getOutputChannel().info(`[DiffViewer] Live EE RAM captured (${this._liveBuffer.length} bytes).`);
            webview.postMessage({ type: 'liveMemoryReady' });
        } catch (err: any) {
            this._liveBuffer = null;
            webview.postMessage({ type: 'liveMemoryError', message: err?.message ?? String(err) });
        }
    }

    private async _handleStartLiveDiff(pathB: string, webview: vscode.Webview): Promise<void> {
        if (!this._liveBuffer) {
            webview.postMessage({ type: 'diffError', message: 'No live memory captured. Click "Read Now" first.' });
            return;
        }
        if (!pathB) {
            webview.postMessage({ type: 'diffError', message: 'Snapshot B must be selected.' });
            return;
        }
        try {
            const fileB = this._resolveFile(pathB);
            const bufB  = await fs.promises.readFile(fileB);
            webview.postMessage({ type: 'diffResults', rows: this._diffBuffers(this._liveBuffer, bufB) });
        } catch (err: any) {
            webview.postMessage({ type: 'diffError', message: err?.message ?? String(err) });
        }
    }

    private _diffBuffers(bufA: Buffer, bufB: Buffer): Array<{ address: string; valueA: string; valueB: string }> {
        const len  = Math.min(bufA.length, bufB.length);
        const rows: Array<{ address: string; valueA: string; valueB: string }> = [];
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
        return rows;
    }

    private _resolveFile(p: string): string {
        try {
            const stat = fs.statSync(p);
            if (stat.isDirectory()) {
                const bins = fs.readdirSync(p).filter((f: string) => f.endsWith('.bin'));
                if (bins.length > 0) { return path.join(p, bins[0]); }
                throw new Error(`No .bin file found in ${p}`);
            }
        } catch { /* p is a file path */ }
        return p;
    }

    // ── Porter handler ────────────────────────────────────────────────────────

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
