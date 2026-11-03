import * as vscode from 'vscode';
import { getOutputChannel } from '../../extension';
import { SocketClient, type LabelEntry } from '../../socketClient';
import type { DisasmEditorProvider } from '../disasmEditor/disasmEditorProvider';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';
import { findCheatArchiveRoot } from '../../utils/cheatArchive';

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

    /**
     * The game being played (serial + title), set by DisasmPanelProvider. Used to
     * pre-select that game when the editor opens without an explicit title.
     */
    public getCurrentGame?: () => Promise<{ serial?: string; title?: string }>;

    /** A synced CheatCodeArchive clone to import from when no workspace checkout exists. */
    public archiveCloneRoot?: () => Promise<string | undefined>;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly socketClient: SocketClient,
        private readonly disasmEditorProvider?: DisasmEditorProvider,
    ) {}

    /** Open (or reveal) the cheats editor, pre-selecting `gameTitle` or else the game being played. */
    public open(gameTitle?: string): void {
        this._pendingGameTitle = gameTitle;

        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            if (gameTitle) {
                this._panel.webview.postMessage({ type: 'initGameTitle', title: gameTitle });
            } else {
                void this._postCurrentGame(this._panel.webview);
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
        applyCodeDesignerPanelIcon(panel, this.extensionUri, 'sparkles');
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
                } else {
                    await this._postCurrentGame(webview);
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
            case 'refreshArchive':
                await this._refreshArchive(webview);
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
            case 'viewInDebugger':
                this._viewInDebugger(webview, msg);
                break;
            case 'gotoCheatAddress':
                this._gotoCheatAddress(webview, msg);
                break;
            case 'bootstrapError':
                getOutputChannel().error('[CheatsEditor] Bootstrap error: ' + msg['error']);
                break;
        }
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    /**
     * Pre-select the game being played: its archive game id (looked up by serial)
     * when the archive has it, otherwise its title is put in the game search box.
     */
    private async _postCurrentGame(webview: vscode.Webview): Promise<void> {
        if (!this.getCurrentGame) { return; }
        try {
            const game = await this.getCurrentGame();
            let gameId: number | null = null;
            let title = game.title ?? '';
            if (game.serial && this.socketClient.isConnected()) {
                const found = await this.socketClient.getGameCheats(game.serial);
                gameId = found.gameId;
                title = found.gameTitle || title;
            }
            if (gameId !== null || title) {
                webview.postMessage({ type: 'initGame', gameId, title, serial: game.serial ?? '' });
            }
        } catch (err: unknown) {
            getOutputChannel().warn(`[CheatsEditor] Could not resolve the current game: ${err instanceof Error ? err.message : err}`);
        }
    }

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

    private async _refreshArchive(webview: vscode.Webview): Promise<void> {
        const archiveRoot = await this._findDefaultArchiveRoot();
        if (!archiveRoot) {
            webview.postMessage({
                type: 'cheatError',
                message: 'CheatCodeArchive submodule was not found. Initialise it or use Import to pick the archive folder.',
            });
            return;
        }
        await this._runImport(webview, archiveRoot, 'Refreshing cheats from CheatCodeArchive…');
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
        await this._runImport(webview, uris[0].fsPath, 'Importing cheats…');
    }

    private async _runImport(webview: vscode.Webview, archiveRoot: string, progressMessage: string): Promise<void> {
        try {
            webview.postMessage({ type: 'cheatStatus', message: progressMessage, busy: true });
            const message = await vscode.window.withProgress({
                location: vscode.ProgressLocation.Notification,
                title: progressMessage,
                cancellable: false,
            }, async () => this.socketClient.cheatEditorImport(archiveRoot));
            await this._sendGames(webview);
            if (this._currentGameId !== undefined) {
                await this._sendBuilds(webview, this._currentGameId);
            }
            webview.postMessage({ type: 'cheatStatus', message, busy: false });
        } catch (err: unknown) {
            this._sendError(webview, err);
        }
    }

    private async _findDefaultArchiveRoot(): Promise<string | undefined> {
        return (await findCheatArchiveRoot(this.extensionUri)) ?? (await this.archiveCloneRoot?.());
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


    private _viewInDebugger(webview: vscode.Webview, msg: Record<string, unknown>): void {
        const enabled = msg['enabled'] === true;
        const title = (msg['title'] as string | undefined)?.trim() || 'Current cheat list';

        if (!enabled) {
            this.disasmEditorProvider?.setCheatOverlay([], title);
            if (msg['silent'] !== true) {
                webview.postMessage({ type: 'cheatStatus', message: 'Cheat debugger overlay hidden.', busy: false });
            }
            return;
        }

        const text = (msg['text'] as string | undefined) ?? '';
        const labels = this._parseCheatOverlayLabels(text);
        this.disasmEditorProvider?.setCheatOverlay(labels, title, true);
        webview.postMessage({
            type: 'cheatStatus',
            message: labels.length > 0
                ? `Showing ${labels.length} cheat address annotation(s) in debugger.`
                : 'View in debugger is on, but no cheat address annotations were found.',
            busy: false,
        });
    }


    private _gotoCheatAddress(webview: vscode.Webview, msg: Record<string, unknown>): void {
        const token = (msg['addressToken'] as string | undefined)?.trim() ?? '';
        if (!/^[0-9A-Fa-f]{8}$/.test(token)) { return; }

        const target = this._cheatAddressAliases(token)[0];
        const address = parseInt(target, 16) >>> 0;
        this.disasmEditorProvider?.navigateToAddress(address, 'user');
        webview.postMessage({
            type: 'cheatStatus',
            message: `Debugger goto 0x${target}.`,
            busy: false,
        });
    }

    private _parseCheatOverlayLabels(text: string): LabelEntry[] {
        const labels: LabelEntry[] = [];
        const lines = text.split(/\r?\n/);
        let currentCheat = '';
        let inBlockComment = false;
        const seen = new Set<string>();

        for (const raw of lines) {
            const stripped = this._stripCheatComments(raw, inBlockComment);
            inBlockComment = stripped.inBlockComment;
            const line = stripped.code.trim();
            if (!line) { continue; }
            if (/^\[--SectionStart:.*--\]$/.test(line)) { continue; }

            const header = line.match(/^\[(.+)\]$/);
            if (header) {
                currentCheat = header[1].trim();
                continue;
            }

            if (!currentCheat) { continue; }
            const firstToken = line.split(/\s+/)[0] ?? '';
            if (!/^[0-9A-Fa-f]{8}$/.test(firstToken)) { continue; }

            for (const address of this._cheatAddressAliases(firstToken)) {
                const key = `${address.toUpperCase()}\u0000${currentCheat}`;
                if (seen.has(key)) { continue; }
                seen.add(key);
                labels.push({ address: `0x${address.toUpperCase()}`, label: currentCheat });
            }
        }

        return labels;
    }

    private _cheatAddressAliases(addressToken: string): string[] {
        const raw = addressToken.toUpperCase();
        const commandStripped = raw.slice(2).padStart(8, '0');
        const aliases = [commandStripped, raw];
        const unique = new Set<string>();
        for (const alias of aliases) {
            const value = parseInt(alias, 16) >>> 0;
            unique.add(value.toString(16).toUpperCase().padStart(8, '0'));
            if (value < 0x20000000) {
                unique.add((value + 0x20000000).toString(16).toUpperCase().padStart(8, '0'));
                unique.add((value + 0x80000000).toString(16).toUpperCase().padStart(8, '0'));
                unique.add((value + 0xA0000000).toString(16).toUpperCase().padStart(8, '0'));
            }
        }
        return Array.from(unique);
    }

    private _stripCheatComments(raw: string, inBlockComment: boolean): { code: string; inBlockComment: boolean } {
        let code = '';
        for (let i = 0; i < raw.length;) {
            if (inBlockComment) {
                const end = raw.indexOf('*/', i);
                if (end < 0) { return { code, inBlockComment: true }; }
                i = end + 2;
                inBlockComment = false;
                continue;
            }

            if (raw.startsWith('//', i)) { break; }
            if (raw.startsWith('/*', i)) {
                inBlockComment = true;
                i += 2;
                continue;
            }

            code += raw[i];
            i++;
        }
        return { code, inBlockComment };
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
