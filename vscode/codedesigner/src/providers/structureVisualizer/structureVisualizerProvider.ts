import * as vscode from 'vscode';
import { getOutputChannel } from '../../extension';
import { SocketClient } from '../../socketClient';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';

/**
 * Provider for the Structure Visualizer standalone webview.
 *
 * Renders Mermaid diagrams of a game's structure: classes/objects from Struct
 * labels, vtables from ArrayOfPointers labels, and call-graph chains from the
 * persisted CallEdge table. The provider is a thin proxy — it forwards webview
 * requests to the C# backend over the socket and posts the results back.
 */
export class StructureVisualizerProvider {
    private _panel: vscode.WebviewPanel | undefined;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly socketClient: SocketClient,
    ) {}

    /** Open (or reveal) the Structure Visualizer panel. */
    public open(): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.structureVisualizer',
            '🧬 Structure Visualizer',
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
        try {
            switch (type) {
                case 'ready':
                    webview.postMessage({
                        type: 'init',
                        workspaceDir: this._workspaceDir(),
                    });
                    break;

                case 'loadSets': {
                    const gameId = String(msg['gameId'] ?? '');
                    const sets = await this.socketClient.getLabelSets(this._workspaceDir(), gameId);
                    webview.postMessage({ type: 'sets', sets });
                    break;
                }

                case 'loadData': {
                    const gameId  = String(msg['gameId'] ?? '');
                    const setName = String(msg['setName'] ?? '');
                    const labelSet = await this.socketClient.getLabelSet(this._workspaceDir(), gameId, setName);
                    let groups: unknown[] = [];
                    try {
                        groups = await this.socketClient.getLabelGroups(gameId, setName);
                    } catch (err: unknown) {
                        // Groups are optional — a set may simply have none yet.
                        getOutputChannel().warn('[StructureVisualizer] getLabelGroups: ' + errText(err));
                    }
                    webview.postMessage({ type: 'data', labelSet, groups });
                    break;
                }

                case 'getCallGraph': {
                    const graph = await this.socketClient.getCallGraph(
                        String(msg['gameId'] ?? ''),
                        String(msg['address'] ?? ''),
                        Number(msg['depth'] ?? 3),
                        String(msg['direction'] ?? 'both'),
                    );
                    webview.postMessage({ type: 'callGraph', graph });
                    break;
                }

                case 'scanCallGraph': {
                    const result = await this.socketClient.scanCallGraph(
                        String(msg['gameId'] ?? ''),
                        String(msg['address'] ?? ''),
                        Number(msg['length'] ?? 0),
                        msg['region'] ? String(msg['region']) : undefined,
                    );
                    webview.postMessage({ type: 'scanResult', result });
                    break;
                }

                case 'saveGroup':
                    await this.socketClient.addLabelGroup(
                        String(msg['gameId'] ?? ''), String(msg['setName'] ?? ''),
                        String(msg['name'] ?? ''), String(msg['kind'] ?? 'Class'),
                        msg['comment'] ? String(msg['comment']) : undefined,
                        msg['color'] ? String(msg['color']) : undefined,
                    );
                    await this._reloadGroups(webview, msg);
                    break;

                case 'deleteGroup':
                    await this.socketClient.deleteLabelGroup(
                        String(msg['gameId'] ?? ''), String(msg['setName'] ?? ''),
                        String(msg['groupName'] ?? ''));
                    await this._reloadGroups(webview, msg);
                    break;

                case 'addMember':
                    await this.socketClient.addLabelGroupMember(
                        String(msg['gameId'] ?? ''), String(msg['setName'] ?? ''),
                        String(msg['groupName'] ?? ''), String(msg['labelAddress'] ?? ''),
                        msg['role'] ? String(msg['role']) : undefined,
                        msg['sortOrder'] !== undefined ? Number(msg['sortOrder']) : undefined,
                    );
                    await this._reloadGroups(webview, msg);
                    break;

                case 'removeMember':
                    await this.socketClient.deleteLabelGroupMember(
                        String(msg['gameId'] ?? ''), String(msg['setName'] ?? ''),
                        String(msg['groupName'] ?? ''), String(msg['labelAddress'] ?? ''));
                    await this._reloadGroups(webview, msg);
                    break;

                case 'navigate': {
                    const addr = parseAddress(String(msg['address'] ?? ''));
                    if (addr !== undefined) {
                        await this.socketClient.navigateTo(addr);
                    }
                    break;
                }

                case 'bootstrapError':
                    getOutputChannel().error('[StructureVisualizer] Bootstrap error: ' + msg['error']);
                    break;
            }
        } catch (err: unknown) {
            webview.postMessage({ type: 'error', message: errText(err) });
        }
    }

    private async _reloadGroups(webview: vscode.Webview, msg: Record<string, unknown>): Promise<void> {
        const groups = await this.socketClient.getLabelGroups(
            String(msg['gameId'] ?? ''), String(msg['setName'] ?? ''));
        webview.postMessage({ type: 'groups', groups });
    }

    private _workspaceDir(): string {
        return vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? '';
    }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'webview', 'structure-visualizer.js')
        );
        const nonce = getNonce();
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src ${webview.cspSource} 'unsafe-inline';
               img-src ${webview.cspSource} data: blob:;
               font-src ${webview.cspSource} data:;
               script-src ${webview.cspSource} 'nonce-${nonce}';">
<script nonce="${nonce}">${getWebviewErrorHandlerScript()}</script>
</head>
<body>
<app-structure-visualizer-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Structure Visualizer…</p></app-structure-visualizer-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}

/** Parse a hex ("0x..") or decimal address string to a number. */
function parseAddress(raw: string): number | undefined {
    const t = raw.trim();
    if (!t) { return undefined; }
    const n = t.toLowerCase().startsWith('0x') ? parseInt(t.slice(2), 16) : parseInt(t, 10);
    return Number.isFinite(n) ? n : undefined;
}

function errText(err: unknown): string {
    return err instanceof Error ? err.message : String(err);
}
