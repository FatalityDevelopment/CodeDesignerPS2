import * as vscode from 'vscode';
import { getOutputChannel } from '../../extension';
import { SocketClient } from '../../socketClient';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';

/**
 * Provider for the PS2 Cheats editor standalone webview tab.
 *
 * A three-panel cheat-code editor: pick a game and one of its cheat lists
 * ("builds"), edit the cheat file as text, and save it back. Also runs the
 * bulk cheat importer against a CheatCodeArchive folder.
 */
export class CheatsEditorProvider {
    private _panel: vscode.WebviewPanel | undefined;
    private _currentGameId: number | undefined;
    private _pendingGameTitle: string | undefined;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly socketClient: SocketClient,
    ) {}

    /** Open (or reveal) the cheats editor, optionally pre-selecting a game by title. */
    public open(gameTitle?: string): void {
        this._pendingGameTitle = gameTitle;

        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            if (gameTitle) {
                this._panel.webview.postMessage({ type: 'initGameTitle', title: gameTitle });
            }
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.cheatsEditor',
            'PS2 Cheats Editor',
            vscode.ViewColumn.One,
            {
                enableScripts:           true,
                retainContextWhenHidden: true,
                localResourceRoots:      [this.extensionUri],
            }
        );
        applyCodeDesignerPanelIcon(panel, this.extensionUri);
        this._panel = panel;
        panel.onDidDispose(() => { this._panel = undefined; });

        panel.webview.onDidReceiveMessage(async (msg: Record<string, unknown>) => {
            await this._handleMessage(msg, panel.webview);
        });

        panel.webview.html = this._buildHtml(panel.webview);
    }

    // ── Message dispatch ──────────────────────────────────────────────────────

    private async _handleMessage(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        switch (msg['type'] as string | undefined) {
            case 'ready':
                await this._sendGames(webview);
                if (this._pendingGameTitle) {
                    webview.postMessage({ type: 'initGameTitle', title: this._pendingGameTitle });
                }
                break;
            case 'selectGame':
                this._currentGameId = msg['gameId'] as number;
                await this._sendBuilds(webview, this._currentGameId);
                break;
            case 'loadBuild':
                await this._loadBuild(webview, msg['cheatListId'] as number, msg['dirty'] === true);
                break;
            case 'saveBuild':
                await this._save(webview, msg['cheatListId'] as number, msg['text'] as string);
                break;
            case 'createBuild':
                await this._createBuild(webview, msg['gameId'] as number);
                break;
            case 'deleteBuild':
                await this._deleteBuild(webview, msg['cheatListId'] as number);
                break;
            case 'bulkImport':
                await this._bulkImport(webview);
                break;
            case 'getCover':
                await this._sendCover(webview, msg['serial'] as string | undefined);
                break;
            case 'convert':
                await this._convert(webview, msg);
                break;
            case 'bootstrapError':
                getOutputChannel().error('[CheatsEditor] Bootstrap error: ' + msg['error']);
                break;
        }
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private async _sendGames(webview: vscode.Webview): Promise<void> {
        try {
            const games = await this.socketClient.cheatEditorGames();
            webview.postMessage({ type: 'cheatGames', games });
        } catch (err: unknown) {
            this._sendError(webview, err);
        }
    }

    private async _sendBuilds(webview: vscode.Webview, gameId: number): Promise<void> {
        try {
            const builds = await this.socketClient.cheatEditorLists(gameId);
            webview.postMessage({ type: 'cheatBuilds', gameId, builds });
        } catch (err: unknown) {
            this._sendError(webview, err);
        }
    }

    private async _loadBuild(webview: vscode.Webview, cheatListId: number, dirty: boolean): Promise<void> {
        if (dirty) {
            const choice = await vscode.window.showWarningMessage(
                'Discard unsaved changes to the current cheat list?',
                { modal: true }, 'Discard');
            if (choice !== 'Discard') { return; }
        }
        await this._sendBuildText(webview, cheatListId);
    }

    private async _sendBuildText(webview: vscode.Webview, cheatListId: number): Promise<void> {
        try {
            const text = await this.socketClient.cheatEditorGet(cheatListId);
            webview.postMessage({ type: 'cheatBuildText', cheatListId, text });
        } catch (err: unknown) {
            this._sendError(webview, err);
        }
    }

    private async _save(webview: vscode.Webview, cheatListId: number, text: string): Promise<void> {
        try {
            const message = await this.socketClient.cheatEditorSave(cheatListId, text);
            webview.postMessage({ type: 'cheatStatus', message, busy: false });
            // Reload the build text (canonical form) and refresh the build list counts.
            await this._sendBuildText(webview, cheatListId);
            if (this._currentGameId !== undefined) {
                await this._sendBuilds(webview, this._currentGameId);
            }
        } catch (err: unknown) {
            this._sendError(webview, err);
        }
    }

    private async _createBuild(webview: vscode.Webview, gameId: number): Promise<void> {
        const title = await vscode.window.showInputBox({
            title:       'New Cheat List',
            prompt:      'Name for the new cheat list',
            value:       'New Cheat List',
            ignoreFocusOut: true,
        });
        if (!title) { return; }
        try {
            await this.socketClient.cheatEditorCreate(gameId, title);
            await this._sendBuilds(webview, gameId);
            webview.postMessage({ type: 'cheatStatus', message: `Created "${title}".`, busy: false });
        } catch (err: unknown) {
            this._sendError(webview, err);
        }
    }

    private async _deleteBuild(webview: vscode.Webview, cheatListId: number): Promise<void> {
        const choice = await vscode.window.showWarningMessage(
            'Delete this cheat list and all of its cheats?',
            { modal: true }, 'Delete');
        if (choice !== 'Delete') { return; }
        try {
            await this.socketClient.cheatEditorDelete(cheatListId);
            if (this._currentGameId !== undefined) {
                await this._sendBuilds(webview, this._currentGameId);
            }
            webview.postMessage({ type: 'cheatStatus', message: 'Cheat list deleted.', busy: false });
        } catch (err: unknown) {
            this._sendError(webview, err);
        }
    }

    private async _bulkImport(webview: vscode.Webview): Promise<void> {
        const uris = await vscode.window.showOpenDialog({
            canSelectFiles:   false,
            canSelectFolders: true,
            canSelectMany:    false,
            title:            'Select the CheatCodeArchive root folder (contains "Database")',
        });
        if (!uris || uris.length === 0) {
            webview.postMessage({ type: 'cheatStatus', message: '', busy: false });
            return;
        }
        try {
            webview.postMessage({ type: 'cheatStatus', message: 'Importing cheats…', busy: true });
            const message = await this.socketClient.cheatEditorImport(uris[0].fsPath);
            await this._sendGames(webview);
            webview.postMessage({ type: 'cheatStatus', message, busy: false });
        } catch (err: unknown) {
            this._sendError(webview, err);
        }
    }

    private async _sendCover(webview: vscode.Webview, serial: string | undefined): Promise<void> {
        if (!serial) { return; }
        try {
            const base64 = await this.socketClient.compatGetCover(serial);
            webview.postMessage({
                type: 'cheatCover',
                serial,
                dataUri: base64 ? `data:image/jpeg;base64,${base64}` : '',
            });
        } catch {
            webview.postMessage({ type: 'cheatCover', serial, dataUri: '' });
        }
    }

    private async _convert(webview: vscode.Webview, msg: Record<string, unknown>): Promise<void> {
        try {
            const text = await this.socketClient.convertCheat(
                msg['text'] as string,
                msg['direction'] as 'toRaw' | 'toEngine',
                msg['format'] as string,
            );
            webview.postMessage({ type: 'convertResult', text });
        } catch (err: unknown) {
            this._sendError(webview, err);
        }
    }

    private _sendError(webview: vscode.Webview, err: unknown): void {
        webview.postMessage({
            type: 'cheatError',
            message: err instanceof Error ? err.message : String(err),
        });
    }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'webview', 'cheats-editor.js')
        );
        const nonce = getNonce();
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               img-src data:;
               script-src ${webview.cspSource} 'nonce-${nonce}';">
<script nonce="${nonce}">${getWebviewErrorHandlerScript()}</script>
</head>
<body>
<app-cheats-editor-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Cheats Editor…</p></app-cheats-editor-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
