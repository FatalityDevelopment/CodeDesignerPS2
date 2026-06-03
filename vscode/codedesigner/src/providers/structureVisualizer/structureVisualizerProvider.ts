import * as vscode from 'vscode';
import { getOutputChannel } from '../../extension';
import { SocketClient } from '../../socketClient';
import { DisasmEditorProvider } from '../disasmEditor/disasmEditorProvider';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';

/**
 * Provider for the Structure Visualizer standalone webview.
 *
 * Renders Mermaid diagrams of a game's structure — classes/objects, vtables and
 * call-graph chains — and lets the user build that structure (typed labels and
 * label groups) in place. The provider is a thin proxy: it resolves the loaded
 * game automatically from {@link DisasmEditorProvider}, forwards webview
 * requests to the C# backend over the socket, and posts the results back.
 */
export class StructureVisualizerProvider {
    private _panel: vscode.WebviewPanel | undefined;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly socketClient: SocketClient,
        private readonly editorProvider: DisasmEditorProvider,
    ) {}

    /** Open (or reveal) the Structure Visualizer panel. */
    public open(): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.structureVisualizer',
            'Structure Visualizer',
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

    // ── Game context ──────────────────────────────────────────────────────────

    /** Resolve the loaded game automatically — never ask the user to type it. */
    private _ctx(): { workspaceDir: string; gameId: string; gameName: string; activeSetName: string | null } | null {
        const c = this.editorProvider.getLabelManagerContext();
        if (!c) { return null; }
        return {
            workspaceDir:  c.workspaceDir,
            gameId:        c.gameDiscId,
            gameName:      c.gameName,
            activeSetName: c.activeSetName,
        };
    }

    // ── Message dispatch ──────────────────────────────────────────────────────

    private async _handleMessage(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        const type = msg['type'] as string | undefined;
        try {
            switch (type) {
                case 'ready':
                case 'refresh':
                    await this._sendContext(webview);
                    break;

                case 'loadData': {
                    const ctx = this._requireCtx();
                    const setName = String(msg['setName'] ?? '');
                    const labelSet = await this.socketClient.getLabelSet(ctx.workspaceDir, ctx.gameId, setName);
                    let groups: unknown[] = [];
                    try {
                        groups = await this.socketClient.getLabelGroups(ctx.gameId, setName);
                    } catch (err: unknown) {
                        // Groups are optional — a set may simply have none yet.
                        getOutputChannel().warn('[StructureVisualizer] getLabelGroups: ' + errText(err));
                    }
                    webview.postMessage({ type: 'data', labelSet, groups });
                    break;
                }

                case 'getCallGraph': {
                    const ctx = this._requireCtx();
                    const address = String(msg['address'] ?? '');
                    const depth = Number(msg['depth'] ?? 3);
                    const direction = String(msg['direction'] ?? 'both');
                    const graph = await this.socketClient.getCallGraph(
                        ctx.gameId,
                        address,
                        depth,
                        direction,
                    );
                    if ((graph.edges?.length ?? 0) === 0) {
                        const focus = parseAddress(address);
                        if (focus !== undefined) {
                            const autoScanLen = 0x200000;
                            try {
                                await this.socketClient.scanCallGraph(ctx.gameId, `0x${focus.toString(16)}`, autoScanLen);
                                const rescanned = await this.socketClient.getCallGraph(ctx.gameId, address, depth, direction);
                                webview.postMessage({ type: 'scanResult', result: { totalEdges: rescanned.edges?.length ?? 0, directEdges: 0, indirectEdges: 0 } });
                                webview.postMessage({ type: 'callGraph', graph: rescanned });
                                break;
                            } catch (err: unknown) {
                                getOutputChannel().warn('[StructureVisualizer] auto scan/getCallGraph: ' + errText(err));
                            }
                        }
                    }
                    webview.postMessage({ type: 'callGraph', graph });
                    break;
                }

                case 'scanCallGraph': {
                    const ctx = this._requireCtx();
                    const result = await this.socketClient.scanCallGraph(
                        ctx.gameId,
                        String(msg['address'] ?? ''),
                        Number(msg['length'] ?? 0),
                        msg['region'] ? String(msg['region']) : undefined,
                    );
                    webview.postMessage({ type: 'scanResult', result });
                    break;
                }

                case 'saveLabel': {
                    const ctx = this._requireCtx();
                    const setName = String(msg['setName'] ?? '');
                    await this.socketClient.addLabelEntry(
                        ctx.workspaceDir, ctx.gameId, setName,
                        msg['entry'] as Parameters<SocketClient['addLabelEntry']>[3]);
                    await this._reloadData(webview, setName);
                    break;
                }

                case 'deleteLabel': {
                    const ctx = this._requireCtx();
                    const setName = String(msg['setName'] ?? '');
                    await this.socketClient.deleteLabelEntry(
                        ctx.workspaceDir, ctx.gameId, setName, String(msg['address'] ?? ''));
                    await this._reloadData(webview, setName);
                    break;
                }

                case 'createSet': {
                    const ctx = this._requireCtx();
                    const name = (await vscode.window.showInputBox({
                        title: 'New label set',
                        prompt: 'Name for the new label set',
                        placeHolder: 'e.g. structures',
                        validateInput: v => v.trim() ? undefined : 'A name is required.',
                    }))?.trim();
                    if (!name) { break; }
                    await this.socketClient.createLabelSet(ctx.workspaceDir, ctx.gameId, name);
                    await this._sendContext(webview, name);
                    break;
                }

                case 'saveGroup': {
                    const ctx = this._requireCtx();
                    const setName = String(msg['setName'] ?? '');
                    await this.socketClient.addLabelGroup(
                        ctx.gameId, setName,
                        String(msg['name'] ?? ''), String(msg['kind'] ?? 'Class'),
                        msg['comment'] ? String(msg['comment']) : undefined,
                        msg['color'] ? String(msg['color']) : undefined,
                    );
                    await this._reloadGroups(webview, setName);
                    break;
                }

                case 'deleteGroup': {
                    const ctx = this._requireCtx();
                    const setName = String(msg['setName'] ?? '');
                    await this.socketClient.deleteLabelGroup(ctx.gameId, setName, String(msg['groupName'] ?? ''));
                    await this._reloadGroups(webview, setName);
                    break;
                }

                case 'addMember': {
                    const ctx = this._requireCtx();
                    const setName = String(msg['setName'] ?? '');
                    await this.socketClient.addLabelGroupMember(
                        ctx.gameId, setName,
                        String(msg['groupName'] ?? ''), String(msg['labelAddress'] ?? ''),
                        msg['role'] ? String(msg['role']) : undefined,
                        msg['sortOrder'] !== undefined ? Number(msg['sortOrder']) : undefined,
                    );
                    await this._reloadGroups(webview, setName);
                    break;
                }

                case 'removeMember': {
                    const ctx = this._requireCtx();
                    const setName = String(msg['setName'] ?? '');
                    await this.socketClient.deleteLabelGroupMember(
                        ctx.gameId, setName,
                        String(msg['groupName'] ?? ''), String(msg['labelAddress'] ?? ''));
                    await this._reloadGroups(webview, setName);
                    break;
                }

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

    /** Resolve the loaded game and its label sets, and push them to the webview. */
    private async _sendContext(webview: vscode.Webview, preferSet?: string): Promise<void> {
        const ctx = this._ctx();
        if (!ctx) {
            webview.postMessage({ type: 'error', message: 'No workspace folder is open.' });
            return;
        }
        let sets: string[] = [];
        try {
            sets = await this.socketClient.getLabelSets(ctx.workspaceDir, ctx.gameId);
        } catch (err: unknown) {
            getOutputChannel().warn('[StructureVisualizer] getLabelSets: ' + errText(err));
        }
        const activeSet =
            (preferSet && sets.includes(preferSet) && preferSet) ||
            (ctx.activeSetName && sets.includes(ctx.activeSetName) && ctx.activeSetName) ||
            sets.find(s => s !== 'Built-in') ||
            sets[0] || '';
        webview.postMessage({
            type: 'init',
            gameName: ctx.gameName,
            gameId:   ctx.gameId,
            sets,
            activeSet,
        });
    }

    private _requireCtx(): { workspaceDir: string; gameId: string; gameName: string; activeSetName: string | null } {
        const ctx = this._ctx();
        if (!ctx) { throw new Error('No workspace folder is open.'); }
        return ctx;
    }

    private async _reloadData(webview: vscode.Webview, setName: string): Promise<void> {
        const ctx = this._requireCtx();
        const labelSet = await this.socketClient.getLabelSet(ctx.workspaceDir, ctx.gameId, setName);
        let groups: unknown[] = [];
        try { groups = await this.socketClient.getLabelGroups(ctx.gameId, setName); } catch { /* optional */ }
        webview.postMessage({ type: 'data', labelSet, groups });
    }

    private async _reloadGroups(webview: vscode.Webview, setName: string): Promise<void> {
        const ctx = this._requireCtx();
        const groups = await this.socketClient.getLabelGroups(ctx.gameId, setName);
        webview.postMessage({ type: 'groups', groups });
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
function parseAddress(raw: string | undefined | null): number | undefined {
    const t = String(raw ?? '').trim();
    if (!t) { return undefined; }
    const lower = t.toLowerCase();
    const n = lower.startsWith('0x') ? parseInt(t.slice(2), 16) : parseInt(t, 10);
    return Number.isFinite(n) ? n : undefined;
}

function errText(err: unknown): string {
    return err instanceof Error ? err.message : String(err);
}
