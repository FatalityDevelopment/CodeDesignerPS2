import * as vscode from 'vscode';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { SocketClient } from '../../socketClient';
import { SnapshotEntry } from '../../webview/disasm-panel/types';
import { SnapshotDataDelegate } from '../snapshotManager/snapshotManagerProvider';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';
import { EeRamSource, readLiveEeRam, resolveEeDumpFile } from '../../utils/eeRamSource';

/** The current game's cheats, resolved by DisasmPanelProvider (DB first, then the YAML archive). */
export type GameCheatsLoader = () => Promise<Array<{ name: string; codes: string[] }>>;

/**
 * Webview panel that ports cheat codes from one EE RAM image to another.
 * Sources are the current game's PCSX2 save states, browsed dumps (.bin,
 * .p2s, snapshot folders) or the emulator's live EE RAM; the bridge's
 * PORTCHEATS command does the relocation on resolved raw dump files.
 */
export class CheatPorterProvider {
    private _panel: vscode.WebviewPanel | undefined;
    /** Incremented per port so a superseded run's result is dropped. */
    private _portId = 0;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly delegate: SnapshotDataDelegate,
        private readonly socketClient: SocketClient,
        private readonly loadGameCheats: GameCheatsLoader,
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
            case 'refreshSnapshots':
                await this._postSnapshotList(webview);
                break;

            case 'browse': {
                const side = msg['side'] as 'source' | 'target';
                const uris = await vscode.window.showOpenDialog({
                    canSelectFiles:   true,
                    canSelectFolders: false,
                    canSelectMany:    false,
                    filters:          { 'Memory dumps': ['bin', 'raw', 'dump', 'p2s'], 'All Files': ['*'] },
                    title:            side === 'source'
                        ? 'Select the EE RAM dump the codes were made for'
                        : 'Select the EE RAM dump to port the codes to',
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

            case 'loadGameCheats':
                await this._handleLoadGameCheats(webview);
                break;

            case 'port':
                await this._handlePort(
                    msg['codes'] as string,
                    msg['source'] as EeRamSource,
                    msg['target'] as EeRamSource,
                    webview,
                );
                break;

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
                getOutputChannel().warn(`[CheatPorter] Unknown message type: ${type}`);
                break;
        }
    }

    private async _postSnapshotList(webview: vscode.Webview): Promise<void> {
        try {
            const snapshots = await this.delegate.getSnapshots();
            webview.postMessage({ type: 'snapshotList', snapshots, connected: this.socketClient.isConnected() });
        } catch (err: any) {
            webview.postMessage({
                type: 'snapshotList', snapshots: [], connected: this.socketClient.isConnected(),
                error: err?.message ?? String(err),
            });
        }
    }

    private async _handleLoadGameCheats(webview: vscode.Webview): Promise<void> {
        try {
            const cheats = (await this.loadGameCheats()).filter(c => c.codes.length > 0);
            if (cheats.length === 0) {
                webview.postMessage({ type: 'status', message: 'No cheats found for the current game.', isError: true });
                return;
            }
            const text = cheats.map(c => [c.name, ...c.codes].join('\n')).join('\n\n');
            webview.postMessage({ type: 'gameCheats', text, count: cheats.length });
        } catch (err: any) {
            webview.postMessage({ type: 'status', message: err?.message ?? String(err), isError: true });
        }
    }

    // ── Port ──────────────────────────────────────────────────────────────────

    private async _handlePort(
        codes: string, source: EeRamSource, target: EeRamSource, webview: vscode.Webview,
    ): Promise<void> {
        const portId   = ++this._portId;
        const progress = (message: string) => webview.postMessage({ type: 'progress', message });
        const temps: string[] = [];
        try {
            if (!this.socketClient.isConnected()) {
                throw new Error('Not connected to the bridge — porting runs in the bridge.');
            }
            if (!source || !target)                            { throw new Error('Select both dumps.'); }
            if (source.kind === 'live' && target.kind === 'live') { throw new Error('Pick a dump for at least one side.'); }

            const resolve = async (src: EeRamSource): Promise<string> => {
                if (src.kind === 'dump') {
                    progress(`Loading ${src.label}…`);
                    return resolveEeDumpFile(this.socketClient, src.path);
                }
                // PORTCHEATS reads files, so live RAM goes through a temp dump.
                const buf  = await readLiveEeRam(this.socketClient, progress);
                const file = path.join(os.tmpdir(), `codedesigner-porter-live-${process.pid}-${portId}.bin`);
                await fs.promises.writeFile(file, buf);
                temps.push(file);
                return file;
            };
            const sourceFile = await resolve(source);
            const targetFile = await resolve(target);

            progress('Searching the target dump for each code address… this can take a minute');
            const { results, log } = await this.socketClient.portCheats(codes, sourceFile, targetFile);
            getOutputChannel().info('[Port Cheats]\n' + log);
            if (portId !== this._portId) { return; }
            webview.postMessage({ type: 'portResult', results });
        } catch (err: any) {
            const message = err instanceof Error ? err.message : String(err);
            getOutputChannel().error('[Port Cheats] ' + message);
            if (portId !== this._portId) { return; }
            webview.postMessage({ type: 'portError', message });
        } finally {
            for (const f of temps) { fs.promises.unlink(f).catch(() => { /* already gone */ }); }
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
