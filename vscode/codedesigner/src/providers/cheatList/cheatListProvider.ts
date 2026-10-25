import * as vscode from 'vscode';
import { getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { SocketClient, GameCheatListInfo } from '../../socketClient';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';
import {
    CheatListService, CheatListStore, CheatListItem, SendMode, BinaryKind,
    parseCheatText, newCheatId, cheatKey,
} from './cheatListService';

/** The game whose lists are shown; resolved by DisasmPanelProvider. */
export interface CheatListGame {
    /** Settings key (selected game file name, else the serial). */
    key:  string;
    /** Display name. */
    name: string;
}

/** Hooks into the rest of the extension (owned by DisasmPanelProvider). */
export interface CheatListHost {
    getGame(): CheatListGame;
    /** The game's disc serial (from PCSX2 or the selected disc), used to find its cheat lists. */
    getSerial(): Promise<string | undefined>;
    /** Import the cheat repository's text files for `serial` into cheat lists; returns a status message. */
    importRepository(serial: string): Promise<string>;
    openTool(tool: 'scanner' | 'converter' | 'porter' | 'editor'): void;
    /** The chosen list or its cheats changed (the sidebar's archive section reloads). */
    onListChanged(): void;
}

/** Page payload for one cheat list. */
interface ListState {
    game:      string;
    connected: boolean;
    serial:    string;
    gameId:    number | null;
    lists:     GameCheatListInfo[];
    listId:    number | null;
    items:     CheatListItem[];
    hookAddress?: number;
    engineBase?:  number;
    error?:    string;
}

/**
 * Cheat List page: a cheat-device style view of one of the game's cheat archive
 * lists (the lists the Cheats editor edits, e.g. r0001 / r0004, plus lists imported
 * from the cheat repository), picked from a dropdown. Cheats can be raw codes or
 * CDS; ticked cheats are compiled and sent once, through the cheat engine, or saved
 * as a binary. Save writes the cheats back to the list.
 */
export class CheatListProvider {
    private _panel: vscode.WebviewPanel | undefined;
    private _busy = false;
    /** Cheats handed over by the sidebar's "Add to list" before the page was ready. */
    private _pendingImports: CheatListItem[] = [];
    private _ready = false;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly socketClient: SocketClient,
        private readonly service: CheatListService,
        private readonly store: CheatListStore,
        private readonly host: CheatListHost,
    ) {}

    /** Open (or reveal) the Cheat List page. */
    public open(): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.cheatList',
            'Cheat List',
            vscode.ViewColumn.One,
            {
                enableScripts:           true,
                retainContextWhenHidden: true,
                localResourceRoots:      [this.extensionUri],
            }
        );
        applyCodeDesignerPanelIcon(panel, this.extensionUri, 'sparkles');
        this._panel = panel;
        this._ready = false;

        panel.onDidDispose(() => { this._panel = undefined; this._ready = false; });

        panel.webview.onDidReceiveMessage(async (msg: Record<string, unknown>) => {
            try {
                await this._handleMessage(msg, panel.webview);
            } catch (err: unknown) {
                const message = err instanceof Error ? err.message : String(err);
                getOutputChannel().error(`[CheatList] ${message}`);
                panel.webview.postMessage({ type: 'status', message, isError: true });
            }
        });

        panel.webview.html = this._buildHtml(panel.webview);
    }

    /** The game or connection changed: reload the page's list. */
    public refresh(): void {
        if (this._panel && this._ready) { void this._postList(this._panel.webview); }
    }

    /** Add cheats (unsaved) to the open list — the sidebar's "Add to list". Opens the page. */
    public addCheats(items: CheatListItem[]): void {
        if (items.length === 0) { return; }
        this.open();
        if (this._panel && this._ready) {
            this._panel.webview.postMessage({ type: 'importItems', items });
        } else {
            this._pendingImports.push(...items);
        }
    }

    // ── Message dispatch ──────────────────────────────────────────────────────

    private async _handleMessage(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        const type = msg['type'] as string | undefined;
        const game = this.host.getGame();

        switch (type) {
            case 'ready':
                this._ready = true;
                await this._postList(webview);
                if (this._pendingImports.length > 0) {
                    webview.postMessage({ type: 'importItems', items: this._pendingImports });
                    this._pendingImports = [];
                }
                break;

            case 'refresh':
                await this._postList(webview);
                break;

            case 'selectList': {
                const listId = msg['listId'] as number;
                this.store.update(game.key, { archiveCheatListId: listId });
                await this._postList(webview, listId);
                this.host.onListChanged();
                break;
            }

            case 'saveSettings': {
                // Ticks and engine fields are page-only state, saved as the user changes them.
                const listId = msg['listId'] as number | null;
                const doc = this.store.load(game.key);
                const enabled = { ...(doc.enabled ?? {}) };
                if (listId !== null && listId !== undefined) { enabled[String(listId)] = (msg['enabled'] as string[]) ?? []; }
                this.store.update(game.key, {
                    enabled,
                    hookAddress: msg['hookAddress'] as number | undefined,
                    engineBase:  msg['engineBase'] as number | undefined,
                });
                break;
            }

            case 'saveToList': {
                const listId = msg['listId'] as number;
                await this._withBusy(webview, type, async () => {
                    const items = (msg['items'] as CheatListItem[]) ?? [];
                    const { message, sources } = await this.service.saveToList(listId, items);
                    const doc = this.store.load(game.key);
                    this.store.update(game.key, {
                        sources: { ...(doc.sources ?? {}), [String(listId)]: sources },
                        enabled: { ...(doc.enabled ?? {}), [String(listId)]: items.filter(i => i.enabled).map(cheatKey) },
                    });
                    await this._postList(webview, listId);
                    webview.postMessage({ type: 'status', message });
                    this.host.onListChanged();
                });
                break;
            }

            case 'newList': {
                const gameId = msg['gameId'] as number | null;
                if (gameId === null || gameId === undefined) {
                    throw new Error('This game is not in the cheat archive yet. Use "Import from repository", or create it in the Cheats editor.');
                }
                const title = await vscode.window.showInputBox({ title: 'New cheat list', prompt: 'Name of the cheat list (e.g. r0001)', placeHolder: 'r0001' });
                if (!title?.trim()) { break; }
                const list = await this.socketClient.cheatEditorCreate(gameId, title.trim());
                this.store.update(game.key, { archiveCheatListId: list.id });
                await this._postList(webview, list.id);
                webview.postMessage({ type: 'status', message: `Created cheat list "${list.title}".` });
                this.host.onListChanged();
                break;
            }

            case 'importRepository': {
                await this._withBusy(webview, type, async () => {
                    const serial = await this.host.getSerial();
                    if (!serial) { throw new Error('No game serial — select a game in the Emulator tab or start one in PCSX2.'); }
                    const message = await this.host.importRepository(serial);
                    await this._postList(webview);
                    webview.postMessage({ type: 'status', message });
                    this.host.onListChanged();
                });
                break;
            }

            case 'compile':
            case 'send':
            case 'saveBinary': {
                await this._withBusy(webview, type, async () => {
                    const items = (msg['items'] as CheatListItem[]) ?? [];
                    const doc = {
                        ...this.store.load(game.key),
                        hookAddress: msg['hookAddress'] as number | undefined,
                        engineBase:  msg['engineBase'] as number | undefined,
                    };
                    const compiled = await this.service.compile(items);
                    webview.postMessage({ type: 'compiled', compiled });
                    if (type === 'compile') {
                        webview.postMessage({
                            type: 'status',
                            message: compiled.errorCount > 0
                                ? `${compiled.errorCount} error(s) — fix the highlighted cheats.`
                                : `Compiled ${compiled.codeCount} code(s) from ${compiled.cheats.length} enabled cheat(s).`,
                            isError: compiled.errorCount > 0,
                        });
                    } else if (type === 'send') {
                        const message = await this.service.send(compiled, msg['mode'] as SendMode, doc);
                        webview.postMessage({ type: 'status', message });
                        webview.postMessage({ type: 'engine', engine: await this.service.engineStatus() });
                    } else {
                        const message = await this.service.saveBinary(compiled, msg['kind'] as BinaryKind, doc, game.name);
                        if (message) { webview.postMessage({ type: 'status', message }); }
                    }
                });
                break;
            }

            case 'removeEngine': {
                const info = await this.service.removeEngine();
                webview.postMessage({ type: 'engine', engine: info });
                webview.postMessage({ type: 'status', message: 'Cheat engine unhooked; the original instructions are restored.' });
                break;
            }

            case 'importText': {
                const items: CheatListItem[] = parseCheatText(msg['text'] as string ?? '').map(c => ({
                    id: newCheatId(), name: c.name, enabled: true, source: c.source,
                }));
                webview.postMessage({ type: 'importItems', items });
                webview.postMessage({ type: 'status', message: `Added ${items.length} cheat(s) — Save to keep them in the list.` });
                break;
            }

            case 'openTool':
                this.host.openTool(msg['tool'] as 'scanner' | 'converter' | 'porter' | 'editor');
                break;

            case 'copy':
                await vscode.env.clipboard.writeText(msg['text'] as string ?? '');
                webview.postMessage({ type: 'status', message: msg['summary'] as string ?? 'Copied.' });
                break;

            default:
                getOutputChannel().warn(`[CheatList] Unknown message type: ${type}`);
                break;
        }
    }

    private async _withBusy(webview: vscode.Webview, action: string, work: () => Promise<void>): Promise<void> {
        if (this._busy) { return; }
        this._busy = true;
        webview.postMessage({ type: 'busy', busy: true, action });
        try {
            await work();
        } finally {
            this._busy = false;
            webview.postMessage({ type: 'busy', busy: false });
        }
    }

    /**
     * Load the game's cheat lists and the cheats of `listId` (else the list chosen
     * before, else the first) and post them, overlaid with the saved ticks and CDS sources.
     */
    private async _postList(webview: vscode.Webview, listId?: number): Promise<void> {
        const game = this.host.getGame();
        const doc = this.store.load(game.key);
        const state: ListState = {
            game:        game.name,
            connected:   this.socketClient.isConnected(),
            serial:      '',
            gameId:      null,
            lists:       [],
            listId:      null,
            items:       [],
            hookAddress: doc.hookAddress,
            engineBase:  doc.engineBase,
        };

        try {
            if (!state.connected) { throw new Error('Not connected to the CodeDesigner bridge.'); }
            const serial = await this.host.getSerial();
            if (!serial) { throw new Error('No game serial — select a game in the Emulator tab or start one in PCSX2.'); }
            state.serial = serial;

            const result = await this.socketClient.getGameCheats(serial, listId ?? doc.archiveCheatListId);
            state.gameId = result.gameId;
            state.lists  = result.lists;
            state.listId = result.cheatListId;
            if (result.gameTitle) { state.game = result.gameTitle; }

            const key = String(result.cheatListId ?? '');
            const enabled = new Set(doc.enabled?.[key] ?? []);
            const sources = doc.sources?.[key] ?? {};
            state.items = result.cheats.map(c => {
                const k = cheatKey({ name: c.name, section: c.section });
                return {
                    id:      newCheatId(),
                    name:    c.name,
                    section: c.section,
                    notes:   c.notes,
                    author:  c.author,
                    enabled: enabled.has(k),
                    source:  sources[k] ?? c.codes.join('\n'),
                };
            });
        } catch (err: unknown) {
            state.error = err instanceof Error ? err.message : String(err);
        }

        webview.postMessage({ type: 'state', ...state });
        webview.postMessage({ type: 'engine', engine: await this.service.engineStatus() });
    }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'webview', 'cheat-list.js')
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
<app-cheat-list-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Cheat List…</p></app-cheat-list-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
