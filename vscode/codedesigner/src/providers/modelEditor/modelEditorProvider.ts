import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { AssetCommand, SocketClient } from '../../socketClient';
import { getOutputChannel, getZdbChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { applyCodeDesignerPanelIcon, type CodeDesignerPanelIcon } from '../../utils/webviewPanel';

// Model Editor: browse, inspect and edit the models and textures of a SOCOM II map package. Every query, edit and
// save is an ASSET_* bridge command on an asset session, the same surface the assets_* MCP tools use, so an agent
// and the editor can work on one package together. The webview only decodes locally to draw.
// The Map Editor is the same host with another bundle (AssetEditorPage).
export interface AssetEditorPage {
    viewType: string;
    title: string;
    // Webpack entry name (dist/webview/<bundle>.js) and the Angular root tag.
    bundle: string;
    tag: string;
    icon: CodeDesignerPanelIcon;
}

export const MODEL_EDITOR_PAGE: AssetEditorPage = {
    viewType: 'codedesigner.modelEditor', title: 'Model Editor', bundle: 'model-editor', tag: 'app-model-editor-page', icon: 'database',
};

export const MAP_EDITOR_PAGE: AssetEditorPage = {
    viewType: 'codedesigner.mapEditor', title: 'Map Editor', bundle: 'map-editor', tag: 'app-map-editor-page', icon: 'monitor',
};

export const ANIMATION_EDITOR_PAGE: AssetEditorPage = {
    viewType: 'codedesigner.animationEditor', title: 'Animation Editor', bundle: 'animation-editor', tag: 'app-animation-editor-page', icon: 'repeat',
};

export class ModelEditorProvider {
    public static readonly viewType = MODEL_EDITOR_PAGE.viewType;

    private _panel: vscode.WebviewPanel | undefined;
    private _pendingPath = '';

    constructor(
        private readonly _extensionUri: vscode.Uri,
        private readonly _socketClient: SocketClient,
        private readonly _page: AssetEditorPage = MODEL_EDITOR_PAGE,
    ) {}

    public open(packagePath?: string, model?: { archive: string; name: string }): void {
        const target = packagePath && /\.(zdb|cdb|zar)$/i.test(packagePath) ? packagePath : this._packageFor(packagePath);
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.Active);
            if (target) { this._panel.webview.postMessage({ type: 'openPackage', path: target, model }); }
            return;
        }
        this._pendingPath = target ?? '';
        const panel = vscode.window.createWebviewPanel(
            this._page.viewType,
            this._page.title,
            vscode.ViewColumn.Active,
            { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [this._extensionUri] },
        );
        applyCodeDesignerPanelIcon(panel, this._extensionUri, this._page.icon);
        this._panel = panel;
        panel.onDidDispose(() => { this._panel = undefined; });
        panel.webview.onDidReceiveMessage(msg => this._handle(msg as Record<string, unknown>, panel.webview, model));
        panel.webview.html = this._buildHtml(panel.webview);
    }

    // A loose map file (WORL_MDL.ZED, M51_MDL.ZED) opens its folder; anything else opens nothing.
    private _packageFor(filePath?: string): string | undefined {
        if (!filePath) { return undefined; }
        try {
            return fs.statSync(filePath).isDirectory() ? filePath : path.dirname(filePath);
        } catch { return undefined; }
    }

    private async _handle(msg: Record<string, unknown>, webview: vscode.Webview, model?: { archive: string; name: string }): Promise<void> {
        switch (msg['type']) {
            case 'ready':
                if (this._pendingPath) {
                    webview.postMessage({ type: 'openPackage', path: this._pendingPath, model });
                    this._pendingPath = '';
                }
                break;
            case 'asset': {
                const id = msg['id'];
                try {
                    if (!this._socketClient.isConnected()) { throw new Error('Not connected to the CodeDesigner bridge.'); }
                    const data = await this._socketClient.assetCommand(msg['command'] as AssetCommand);
                    webview.postMessage({ type: 'assetResult', id, ok: true, data });
                } catch (err) {
                    webview.postMessage({ type: 'assetResult', id, ok: false, error: err instanceof Error ? err.message : String(err) });
                }
                break;
            }
            case 'pickPackage': {
                const uris = await vscode.window.showOpenDialog({
                    title: 'Open map package',
                    canSelectMany: false,
                    // A .ZDB package, a .CDB memory-card pack, or any loose .ZED of an unpacked package / pack folder.
                    filters: { 'SOCOM package, pack or archive': ['zdb', 'ZDB', 'cdb', 'CDB', 'zed', 'ZED', 'zar', 'ZAR'] },
                });
                if (uris?.[0]) { webview.postMessage({ type: 'openPackage', path: uris[0].fsPath }); }
                break;
            }
            case 'pickImage': {
                const uris = await vscode.window.showOpenDialog({
                    title: 'Choose an image',
                    canSelectMany: false,
                    filters: { 'PNG image': ['png'] },
                });
                const file = uris?.[0]?.fsPath;
                webview.postMessage({
                    type: 'imagePicked',
                    id: msg['id'],
                    name: file ? path.basename(file) : null,
                    base64: file ? fs.readFileSync(file).toString('base64') : null,
                });
                break;
            }
            case 'pickText': {
                const uris = await vscode.window.showOpenDialog({
                    title: 'Import',
                    canSelectMany: false,
                    filters: (msg['filters'] as Record<string, string[]>) ?? undefined,
                });
                const file = uris?.[0]?.fsPath;
                webview.postMessage({
                    type: 'textPicked',
                    id: msg['id'],
                    name: file ? path.basename(file) : null,
                    text: file ? fs.readFileSync(file, 'utf8') : null,
                });
                break;
            }
            case 'pickSavePath': {
                const kind = msg['kind'] === 'folder' || msg['kind'] === 'pack' ? msg['kind'] : 'zdb';
                let picked: string | undefined;
                let packName: string | undefined;
                if (kind === 'pack') {
                    packName = await vscode.window.showInputBox({
                        title: 'Memory-card pack name',
                        value: typeof msg['suggested'] === 'string' ? msg['suggested'] : 'MEDIT',
                        prompt: 'Starts with M, at most 7 letters/digits (the game mounts mc0:/BASCUS-97275<NAME>/<NAME>.CDB)',
                        validateInput: v => /^m[a-z0-9_]{1,6}$/i.test(v) ? null : 'Start with M, at most 7 letters/digits',
                    });
                    if (packName) {
                        const uris = await vscode.window.showOpenDialog({ title: 'Folder for the pack', canSelectFiles: false, canSelectFolders: true });
                        picked = uris?.[0]?.fsPath;
                    }
                } else if (kind === 'folder') {
                    const uris = await vscode.window.showOpenDialog({ title: 'Choose a folder', canSelectFiles: false, canSelectFolders: true });
                    picked = uris?.[0]?.fsPath;
                } else {
                    const uri = await vscode.window.showSaveDialog({
                        title: 'Save the edited package as',
                        defaultUri: typeof msg['suggested'] === 'string' ? vscode.Uri.file(msg['suggested']) : undefined,
                        // Same kind as the open package: a .ZDB, or a .CDB memory-card pack.
                        filters: typeof msg['suggested'] === 'string' && /\.cdb$/i.test(msg['suggested'])
                            ? { 'Memory-card pack': ['CDB'] }
                            : { 'SOCOM II package': ['ZDB'] },
                    });
                    picked = uri?.fsPath;
                }
                webview.postMessage({ type: 'pathPicked', id: msg['id'], path: picked ?? null, packName: packName ?? null });
                break;
            }
            case 'saveFile': {
                // Exports (OBJ, PNG): text or base64 content written where the user picks.
                const uri = await vscode.window.showSaveDialog({
                    title: 'Export',
                    defaultUri: typeof msg['suggested'] === 'string' ? vscode.Uri.file(msg['suggested']) : undefined,
                    filters: (msg['filters'] as Record<string, string[]>) ?? undefined,
                });
                if (uri) {
                    const content = typeof msg['text'] === 'string'
                        ? Buffer.from(msg['text'], 'utf8')
                        : Buffer.from(String(msg['base64'] ?? ''), 'base64');
                    fs.writeFileSync(uri.fsPath, content);
                    vscode.window.showInformationMessage(`Exported ${path.basename(uri.fsPath)}`);
                }
                webview.postMessage({ type: 'saved', id: msg['id'], path: uri?.fsPath ?? null });
                break;
            }
            case 'askText': {
                // A name (new / renamed animation): VS Code's input box, validated against the page's pattern.
                const pattern = typeof msg['pattern'] === 'string' ? new RegExp(msg['pattern']) : null;
                const text = await vscode.window.showInputBox({
                    title: typeof msg['title'] === 'string' ? msg['title'] : undefined,
                    value: typeof msg['value'] === 'string' ? msg['value'] : '',
                    validateInput: v => !pattern || pattern.test(v) ? null : String(msg['hint'] ?? 'Invalid name'),
                });
                webview.postMessage({ type: 'textAnswered', id: msg['id'], text: text ?? null });
                break;
            }
            case 'openModelEditor':
                // From the Map Editor: the same package (and asset session) with one model selected.
                vscode.commands.executeCommand('codedesigner.openModelEditor', msg['path'], msg['model']);
                break;
            case 'log': {
                const text = (msg['text'] as string) ?? '';
                if (text) { getZdbChannel().appendLine(`[${this._page.bundle}] ${text}`); }
                break;
            }
            case 'bootstrapError':
                getOutputChannel().error(`[${this._page.title}] Bootstrap error: ` + msg['error']);
                break;
        }
    }

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'dist', 'webview', `${this._page.bundle}.js`));
        const nonce = getNonce();
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               img-src data: blob:;
               script-src ${webview.cspSource} 'nonce-${nonce}';">
<script nonce="${nonce}">${getWebviewErrorHandlerScript()}</script>
</head>
<body>
<${this._page.tag}>
  <p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading ${this._page.title}...</p>
</${this._page.tag}>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
