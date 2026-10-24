import * as vscode from 'vscode';
import { ReferenceEntry } from '../../socketClient';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';

/** What the cross-references view needs from the disassembler. */
export interface XrefsDelegate {
    /** References to `address` of the given kinds (empty when not connected). */
    getXrefs(address: number, kinds: string[]): Promise<ReferenceEntry[]>;
    navigateToAddress(address: number): void;
}

/**
 * Dockable "Cross-references" view. Lives in the panel area by default; the
 * user can drag it into the sidebar, secondary sidebar or any other view
 * container. Follows the disassembler cursor unless pinned.
 */
export class XrefsViewProvider implements vscode.WebviewViewProvider {
    public static readonly viewId = 'codedesigner.xrefsView';

    private _view?: vscode.WebviewView;
    private _target: { address: number; label?: string } | undefined;
    private _pinned = false;
    private _kinds = ['controlFlow', 'resolved', 'pointer', 'stringPattern'];
    private _requestToken = 0;
    private _debounce: ReturnType<typeof setTimeout> | undefined;

    constructor(private readonly _extensionUri: vscode.Uri, private readonly _delegate: XrefsDelegate) {}

    public register(context: vscode.ExtensionContext): void {
        context.subscriptions.push(
            vscode.window.registerWebviewViewProvider(XrefsViewProvider.viewId, this, {
                webviewOptions: { retainContextWhenHidden: true },
            }),
        );
    }

    /** Called when the disassembler cursor moves. */
    public onCursorAddress(address: number, label?: string): void {
        if (this._pinned) { return; }
        if (this._target?.address === address) { return; }
        this._target = { address, label };
        if (this._debounce) { clearTimeout(this._debounce); }
        // Cursor moves come in bursts (holding an arrow key); look up the last one.
        this._debounce = setTimeout(() => { void this._refresh(); }, 250);
    }

    /** Pin the view on `address` (context menu) and reveal it. */
    public show(address: number, label?: string): void {
        this._target = { address, label };
        this._pinned = true;
        this._postState();
        void vscode.commands.executeCommand(`${XrefsViewProvider.viewId}.focus`);
        void this._refresh();
    }

    public resolveWebviewView(view: vscode.WebviewView): void {
        this._view = view;
        view.webview.options = { enableScripts: true, localResourceRoots: [this._extensionUri] };
        view.webview.onDidReceiveMessage(async (msg: Record<string, unknown>) => {
            switch (msg['type']) {
                case 'ready':
                    this._postState();
                    void this._refresh();
                    break;
                case 'navigate':
                    this._delegate.navigateToAddress(Number(msg['address']));
                    break;
                case 'setPinned':
                    this._pinned = !!msg['pinned'];
                    this._postState();
                    break;
                case 'setKinds':
                    this._kinds = (msg['kinds'] as string[] | undefined) ?? this._kinds;
                    void this._refresh();
                    break;
                case 'refresh':
                    void this._refresh();
                    break;
            }
        });
        view.onDidChangeVisibility(() => { if (view.visible) { void this._refresh(); } });
        view.webview.html = this._buildHtml(view.webview);
    }

    private _postState(): void {
        this._view?.webview.postMessage({ type: 'state', pinned: this._pinned, kinds: this._kinds });
    }

    private async _refresh(): Promise<void> {
        const view = this._view;
        if (!view?.visible || !this._target) { return; }
        const token = ++this._requestToken;
        const target = this._target;
        view.webview.postMessage({ type: 'loading', address: target.address, label: target.label });
        let refs: ReferenceEntry[] = [];
        let error: string | undefined;
        try {
            refs = await this._delegate.getXrefs(target.address, this._kinds);
        } catch (err: any) {
            error = err?.message ?? String(err);
        }
        if (token !== this._requestToken) { return; }
        view.webview.postMessage({ type: 'xrefs', address: target.address, label: target.label, refs, error });
    }

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'dist', 'webview', 'xrefs-view.js'),
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
<app-xrefs-view><p style="color:#888;font-family:sans-serif;padding:8px;font-size:12px;">Loading…</p></app-xrefs-view>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
