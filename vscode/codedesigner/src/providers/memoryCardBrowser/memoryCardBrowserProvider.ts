import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import * as os from 'os';
import { SocketClient, McSaveInfo } from '../../socketClient';
import { SaveFile, buildPsu, diffSaveFiles, parsePsu, safeSaveName } from '../../utils/ps2SaveFiles';
import { SaveSnapshotStore } from '../../utils/saveSnapshotStore';

/** Card image extensions the browser opens when one is dropped on it. */
const CARD_EXTENSIONS = ['.ps2', '.mcd', '.mc2', '.bin'];

/** A file or folder dropped from the OS (contents read by the webview). */
interface DroppedItem {
    kind: 'file' | 'folder';
    name: string;
    /** Base64 contents (kind = file). */
    data?: string;
    /** Files of a dropped folder (kind = folder). */
    files?: Array<{ name: string; data: string }>;
}
import { getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';

/**
 * Standalone PS2 memory-card browser webview.
 *
 * The bridge owns card filesystem access. The webview owns icon.sys/icon parsing
 * and WebGL rendering so filtering, selection, and animation stay responsive.
 */
export class MemoryCardBrowserProvider {
    private _panel: vscode.WebviewPanel | undefined;

    private readonly _snapshots: SaveSnapshotStore;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly socketClient: SocketClient,
        storageDir: string,
    ) {
        this._snapshots = new SaveSnapshotStore(storageDir, 'mc');
    }

    public open(initialPath?: string): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            if (initialPath) {
                this._loadCard(this._panel.webview, initialPath);
            }
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.memoryCardBrowser',
            'PS2 Memory Card Browser',
            vscode.ViewColumn.One,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [this.extensionUri],
            },
        );

        applyCodeDesignerPanelIcon(panel, this.extensionUri, 'save');
        this._panel = panel;
        panel.onDidDispose(() => { this._panel = undefined; });
        panel.webview.onDidReceiveMessage(async msg => {
            await this._handleMessage(msg as Record<string, unknown>, panel.webview);
        });
        panel.webview.html = this._buildHtml(panel.webview);

        if (initialPath) {
            setTimeout(() => this._loadCard(panel.webview, initialPath), 100);
        }
    }

    private async _handleMessage(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        switch (msg['type'] as string | undefined) {
            case 'ready':
                webview.postMessage({ type: 'browserReady' });
                break;
            case 'openCard':
                await this._openCard(webview);
                break;
            case 'newCard':
                await this._newCard(webview);
                break;
            case 'refresh':
                await this._loadCard(webview, msg['mcPath'] as string | undefined);
                break;
            case 'readSaveFile':
                await this._readSaveFile(
                    webview,
                    msg['mcPath'] as string | undefined,
                    msg['saveName'] as string | undefined,
                    msg['fileName'] as string | undefined,
                );
                break;
            case 'exportSave':
                await this._exportSave(
                    webview,
                    msg['mcPath'] as string | undefined,
                    msg['saveName'] as string | undefined,
                );
                break;
            case 'importSave':
                await this._importSave(webview, msg['mcPath'] as string | undefined);
                break;
            case 'deleteSave':
                await this._deleteSave(
                    webview,
                    msg['mcPath'] as string | undefined,
                    msg['saveName'] as string | undefined,
                );
                break;
            case 'dropImport':
                await this._dropImport(
                    webview,
                    msg['mcPath'] as string | undefined,
                    (msg['uris'] as string[] | undefined) ?? [],
                    (msg['items'] as DroppedItem[] | undefined) ?? [],
                );
                break;
            case 'buildPsu':
                await this._buildPsuForDrag(webview, msg['mcPath'] as string | undefined, msg['saveName'] as string | undefined);
                break;
            case 'exportPsu':
                await this._exportPsu(webview, msg['mcPath'] as string | undefined, msg['saveName'] as string | undefined);
                break;
            case 'listSnapshots':
                this._postSnapshots(webview, String(msg['saveName'] ?? ''));
                break;
            case 'snapshotSave':
                await this._snapshotSave(webview, msg['mcPath'] as string | undefined, msg['saveName'] as string | undefined);
                break;
            case 'deleteSnapshot':
                this._snapshots.remove(String(msg['saveName'] ?? ''), String(msg['id'] ?? ''));
                this._postSnapshots(webview, String(msg['saveName'] ?? ''));
                break;
            case 'diffSnapshot':
                await this._diffSnapshot(webview, msg['mcPath'] as string | undefined, String(msg['saveName'] ?? ''), String(msg['id'] ?? ''));
                break;
            case 'bootstrapError':
                getOutputChannel().error('[MemoryCardBrowser] Bootstrap error: ' + msg['error']);
                break;
        }
    }

    private async _openCard(webview: vscode.Webview): Promise<void> {
        const uris = await vscode.window.showOpenDialog({
            title: 'Open PS2 Memory Card',
            canSelectFiles: true,
            canSelectFolders: false,
            canSelectMany: false,
            filters: {
                'PS2 Memory Cards': ['ps2', 'mcd', 'mc2', 'bin'],
                'All Files': ['*'],
            },
        });
        if (!uris?.length) { return; }
        await this._loadCard(webview, uris[0].fsPath);
    }

    private async _newCard(webview: vscode.Webview): Promise<void> {
        const target = await vscode.window.showSaveDialog({
            title: 'Create PS2 Memory Card',
            saveLabel: 'Create Card',
            defaultUri: vscode.Uri.file(path.join(os.homedir(), 'Documents', 'MemoryCard.ps2')),
            filters: {
                'PCSX2 Memory Card': ['ps2'],
                'All Files': ['*'],
            },
        });
        if (!target) { return; }

        if (!this._ensureConnected(webview)) { return; }
        try {
            webview.postMessage({ type: 'mcStatus', message: 'Creating memory card...', busy: true });
            await this.socketClient.mcFormat(target.fsPath);
            await this._loadCard(webview, target.fsPath);
        } catch (err: unknown) {
            this._postError(webview, err);
        }
    }

    private async _loadCard(webview: vscode.Webview, mcPath: string | undefined): Promise<void> {
        const cleanPath = mcPath?.trim();
        if (!cleanPath) { return; }
        if (!this._ensureConnected(webview)) { return; }

        try {
            webview.postMessage({ type: 'mcStatus', message: 'Reading memory card...', busy: true });
            const saves = await this.socketClient.mcList(cleanPath);
            webview.postMessage({ type: 'cardLoaded', mcPath: cleanPath, saves });
        } catch (err: unknown) {
            this._postError(webview, err);
        }
    }

    private async _readSaveFile(
        webview: vscode.Webview,
        mcPath: string | undefined,
        saveName: string | undefined,
        fileName: string | undefined,
    ): Promise<void> {
        if (!mcPath || !saveName || !fileName) { return; }
        if (!this._ensureConnected(webview)) { return; }

        try {
            const data = await this.socketClient.mcReadFile(mcPath, saveName, fileName);
            webview.postMessage({ type: 'saveFileData', saveName, fileName, data });
        } catch (err: unknown) {
            webview.postMessage({
                type: 'saveFileError',
                saveName,
                fileName,
                message: err instanceof Error ? err.message : String(err),
            });
        }
    }

    private async _exportSave(
        webview: vscode.Webview,
        mcPath: string | undefined,
        saveName: string | undefined,
    ): Promise<void> {
        if (!mcPath || !saveName) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const dirs = await vscode.window.showOpenDialog({
            title: `Export ${saveName}`,
            canSelectFiles: false,
            canSelectFolders: true,
            canSelectMany: false,
        });
        if (!dirs?.length) { return; }

        try {
            const outPath = await this.socketClient.mcExport(mcPath, saveName, dirs[0].fsPath);
            webview.postMessage({
                type: 'mcStatus',
                message: `Exported ${saveName} to ${outPath}`,
                busy: false,
            });
        } catch (err: unknown) {
            this._postError(webview, err);
        }
    }

    private async _importSave(webview: vscode.Webview, mcPath: string | undefined): Promise<void> {
        if (!mcPath) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const dirs = await vscode.window.showOpenDialog({
            title: 'Import Save Folder',
            canSelectFiles: false,
            canSelectFolders: true,
            canSelectMany: false,
        });
        if (!dirs?.length) { return; }

        try {
            webview.postMessage({ type: 'mcStatus', message: 'Importing save...', busy: true });
            await this.socketClient.mcImport(mcPath, dirs[0].fsPath, true);
            await this._loadCard(webview, mcPath);
        } catch (err: unknown) {
            this._postError(webview, err);
        }
    }

    private async _deleteSave(
        webview: vscode.Webview,
        mcPath: string | undefined,
        saveName: string | undefined,
    ): Promise<void> {
        if (!mcPath || !saveName) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const confirm = await vscode.window.showWarningMessage(
            `Delete save "${saveName}" from this memory card?`,
            { modal: true },
            'Delete',
        );
        if (confirm !== 'Delete') { return; }

        try {
            webview.postMessage({ type: 'mcStatus', message: 'Deleting save...', busy: true });
            await this.socketClient.mcDelete(mcPath, saveName);
            await this._loadCard(webview, mcPath);
        } catch (err: unknown) {
            this._postError(webview, err);
        }
    }

    // ── Drag and drop ──────────────────────────────────────────────────────

    /**
     * Imports what was dropped on the save list: save folders and .psu files
     * are written to the card; a card image (from the VS Code explorer) is
     * opened instead.
     */
    private async _dropImport(webview: vscode.Webview, mcPath: string | undefined, uris: string[], items: DroppedItem[]): Promise<void> {
        const work: Array<{ label: string; run: (card: string) => Promise<string> }> = [];
        for (const raw of uris) {
            let fsPath: string;
            try { fsPath = vscode.Uri.parse(raw.trim()).fsPath; } catch { continue; }
            if (!fsPath || !fs.existsSync(fsPath)) { continue; }
            const ext = path.extname(fsPath).toLowerCase();
            if (fs.statSync(fsPath).isDirectory()) {
                work.push({ label: path.basename(fsPath), run: card => this._importFolder(card, fsPath) });
            } else if (ext === '.psu') {
                work.push({ label: path.basename(fsPath), run: card => this._importPsu(card, fs.readFileSync(fsPath)) });
            } else if (CARD_EXTENSIONS.includes(ext)) {
                await this._loadCard(webview, fsPath);
                return;
            } else {
                this._postError(webview, new Error(`Can't import ${path.basename(fsPath)}: drop a save folder, a .psu file or a memory card image.`));
                return;
            }
        }
        for (const item of items) {
            if (item.kind === 'folder' && item.files?.length) {
                const files = item.files.map(f => ({ name: f.name, data: Buffer.from(f.data, 'base64') }));
                work.push({ label: item.name, run: card => this._importFiles(card, item.name, files) });
            } else if (item.kind === 'file' && item.data && item.name.toLowerCase().endsWith('.psu')) {
                const data = Buffer.from(item.data, 'base64');
                work.push({ label: item.name, run: card => this._importPsu(card, data) });
            } else if (item.kind === 'file' && CARD_EXTENSIONS.includes(path.extname(item.name).toLowerCase())) {
                // The webview can't see where an OS file lives, and a temp copy would hide edits.
                this._postError(webview, new Error(`To open ${item.name}, drag it from the VS Code explorer or use Open.`));
                return;
            } else {
                this._postError(webview, new Error(`Can't import ${item.name}: drop a save folder or a .psu file.`));
                return;
            }
        }
        if (!work.length) { return; }
        if (!mcPath) { this._postError(webview, new Error('Open a memory card first, then drop saves on it.')); return; }
        if (!this._ensureConnected(webview)) { return; }

        const imported: string[] = [];
        try {
            for (const w of work) {
                webview.postMessage({ type: 'mcStatus', message: `Importing ${w.label}...`, busy: true });
                const name = await w.run(mcPath);
                if (name) { imported.push(name); }
            }
            await this._loadCard(webview, mcPath);
            if (imported.length) {
                webview.postMessage({ type: 'mcStatus', message: `Imported ${imported.join(', ')}`, busy: false });
            }
        } catch (err: unknown) {
            this._postError(webview, err);
        }
    }

    /** Asks before replacing a save that is already on the card. Returns false to skip. */
    private async _confirmOverwrite(mcPath: string, saveName: string): Promise<boolean> {
        const existing = await this.socketClient.mcList(mcPath);
        if (!existing.some(s => s.Name.toLowerCase() === saveName.toLowerCase())) { return true; }
        const ok = await vscode.window.showWarningMessage(
            `"${saveName}" is already on this memory card. Replace it?`, { modal: true }, 'Replace');
        return ok === 'Replace';
    }

    private async _importFolder(mcPath: string, dir: string): Promise<string> {
        const name = path.basename(dir);
        if (!await this._confirmOverwrite(mcPath, name)) { return ''; }
        await this.socketClient.mcImport(mcPath, dir, true);
        return name;
    }

    private async _importPsu(mcPath: string, data: Buffer): Promise<string> {
        const { saveName, files } = parsePsu(data);
        return this._importFiles(mcPath, saveName, files);
    }

    /** Writes `files` to a temporary <saveName> folder and imports that folder. */
    private async _importFiles(mcPath: string, saveName: string, files: SaveFile[]): Promise<string> {
        const name = safeSaveName(saveName);
        if (!await this._confirmOverwrite(mcPath, name)) { return ''; }
        const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'cd-mc-import-'));
        const dir = path.join(tmp, name);
        try {
            fs.mkdirSync(dir);
            for (const f of files) { fs.writeFileSync(path.join(dir, safeSaveName(f.name)), f.data); }
            await this.socketClient.mcImport(mcPath, dir, true);
        } finally {
            fs.rmSync(tmp, { recursive: true, force: true });
        }
        return name;
    }

    /** Every file of a save, read through the bridge. */
    private async _readSave(mcPath: string, saveName: string): Promise<SaveFile[]> {
        const saves = await this.socketClient.mcList(mcPath);
        const save = saves.find(s => s.Name === saveName);
        if (!save) { throw new Error(`"${saveName}" is not on this memory card`); }
        const files: SaveFile[] = [];
        for (const f of save.Files) {
            const b64 = await this.socketClient.mcReadFile(mcPath, saveName, f.Name);
            files.push({ name: f.Name, data: Buffer.from(b64, 'base64') });
        }
        return files;
    }

    /** Pre-builds the selected save as a .psu so it can be dragged out of the list. */
    private async _buildPsuForDrag(webview: vscode.Webview, mcPath: string | undefined, saveName: string | undefined): Promise<void> {
        if (!mcPath || !saveName || !this.socketClient.isConnected()) { return; }
        try {
            const psu = buildPsu(saveName, await this._readSave(mcPath, saveName));
            webview.postMessage({ type: 'psuReady', saveName, data: psu.toString('base64') });
        } catch (err: unknown) {
            getOutputChannel().warn(`[MemoryCardBrowser] buildPsu ${saveName}: ${err instanceof Error ? err.message : err}`);
        }
    }

    private async _exportPsu(webview: vscode.Webview, mcPath: string | undefined, saveName: string | undefined): Promise<void> {
        if (!mcPath || !saveName) { return; }
        if (!this._ensureConnected(webview)) { return; }
        const target = await vscode.window.showSaveDialog({
            title: `Export ${saveName} as .psu`,
            saveLabel: 'Export',
            defaultUri: vscode.Uri.file(path.join(path.dirname(mcPath), `${safeSaveName(saveName)}.psu`)),
            filters: { 'PS2 Save (EMS/uLaunchELF)': ['psu'] },
        });
        if (!target) { return; }
        try {
            fs.writeFileSync(target.fsPath, buildPsu(saveName, await this._readSave(mcPath, saveName)));
            webview.postMessage({ type: 'mcStatus', message: `Exported ${saveName} to ${target.fsPath}`, busy: false });
        } catch (err: unknown) {
            this._postError(webview, err);
        }
    }

    // ── Save snapshots and diffs ───────────────────────────────────────────

    private _postSnapshots(webview: vscode.Webview, saveName: string): void {
        webview.postMessage({ type: 'saveSnapshots', saveName, snapshots: saveName ? this._snapshots.list(saveName) : [] });
    }

    private async _snapshotSave(webview: vscode.Webview, mcPath: string | undefined, saveName: string | undefined): Promise<void> {
        if (!mcPath || !saveName) { return; }
        if (!this._ensureConnected(webview)) { return; }
        const label = await vscode.window.showInputBox({
            title: `Snapshot ${saveName}`,
            prompt: 'Optional note, e.g. "before buying the rifle"',
            placeHolder: 'Note',
        });
        if (label === undefined) { return; }
        try {
            webview.postMessage({ type: 'mcStatus', message: `Copying ${saveName}...`, busy: true });
            this._snapshots.add(saveName, mcPath, await this._readSave(mcPath, saveName), label.trim());
            webview.postMessage({ type: 'mcStatus', message: `Snapshot of ${saveName} stored`, busy: false });
            this._postSnapshots(webview, saveName);
        } catch (err: unknown) {
            this._postError(webview, err);
        }
    }

    private async _diffSnapshot(webview: vscode.Webview, mcPath: string | undefined, saveName: string, id: string): Promise<void> {
        if (!mcPath || !saveName || !id) { return; }
        if (!this._ensureConnected(webview)) { return; }
        try {
            webview.postMessage({ type: 'mcStatus', message: `Comparing ${saveName}...`, busy: true });
            const before = this._snapshots.read(saveName, id);
            const after = await this._readSave(mcPath, saveName);
            const files = diffSaveFiles(before, after);
            webview.postMessage({ type: 'saveDiff', saveName, id, files });
            const changed = files.filter(f => f.status !== 'same').length;
            webview.postMessage({ type: 'mcStatus', message: changed ? `${changed} file(s) differ from the snapshot` : 'Save matches the snapshot', busy: false });
        } catch (err: unknown) {
            this._postError(webview, err);
        }
    }

    private _ensureConnected(webview: vscode.Webview): boolean {
        if (this.socketClient.isConnected()) { return true; }
        webview.postMessage({
            type: 'mcError',
            message: 'Not connected to the CodeDesigner server.',
        });
        return false;
    }

    private _postError(webview: vscode.Webview, err: unknown): void {
        webview.postMessage({
            type: 'mcError',
            message: err instanceof Error ? err.message : String(err),
        });
    }

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'webview', 'memory-card-browser.js'),
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
<app-memory-card-browser-page>
  <p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Memory Card Browser...</p>
</app-memory-card-browser-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
