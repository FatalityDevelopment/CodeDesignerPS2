import * as vscode from 'vscode';
import * as path from 'path';
import * as os from 'os';
import * as fs from 'fs';
import { SocketClient, HddFileEntryInfo } from '../../socketClient';
import { getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';
import { SaveFile, diffSaveFiles } from '../../utils/ps2SaveFiles';
import { SaveSnapshotStore } from '../../utils/saveSnapshotStore';

/** HDD image extensions opened when one is dropped on the browser. */
const HDD_EXTENSIONS = ['.raw', '.img', '.hdd'];
/** Root files are copied whole into a snapshot; game partitions can hold GBs. */
const MAX_SNAPSHOT_BYTES = 64 * 1024 * 1024;
/** Largest file read ahead so it can be dragged out of the list. */
const MAX_DRAG_BYTES = 8 * 1024 * 1024;

export class HddBrowserProvider {
    public static readonly viewType = 'codedesigner.hddBrowser';

    private _panel: vscode.WebviewPanel | undefined;
    private _pendingInitialPath = '';

    private readonly _snapshots: SaveSnapshotStore;

    constructor(
        private readonly _extensionUri: vscode.Uri,
        private readonly _socketClient: SocketClient,
        storageDir: string,
    ) {
        this._snapshots = new SaveSnapshotStore(storageDir, 'hdd');
    }

    public open(initialPath?: string): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            if (initialPath) { void this._loadHdd(this._panel.webview, initialPath); }
            return;
        }

        this._pendingInitialPath = initialPath ?? '';
        const panel = vscode.window.createWebviewPanel(
            HddBrowserProvider.viewType,
            'PS2 HDD Browser',
            vscode.ViewColumn.One,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [this._extensionUri],
            },
        );

        applyCodeDesignerPanelIcon(panel, this._extensionUri, 'harddrive');
        this._panel = panel;
        panel.onDidDispose(() => { this._panel = undefined; });
        panel.webview.onDidReceiveMessage(async msg => {
            await this._handleMessage(msg as Record<string, unknown>, panel.webview);
        });
        panel.webview.html = this._buildHtml(panel.webview);
    }

    private async _handleMessage(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        switch (msg['type'] as string | undefined) {
            case 'ready':
                webview.postMessage({ type: 'hddReady' });
                if (this._pendingInitialPath) {
                    const initial = this._pendingInitialPath;
                    this._pendingInitialPath = '';
                    await this._loadHdd(webview, initial);
                }
                break;
            case 'openHdd':
                await this._openHdd(webview);
                break;
            case 'newHdd':
                await this._newHdd(webview);
                break;
            case 'refresh':
                await this._loadHdd(webview, msg['hddPath'] as string | undefined);
                break;
            case 'listFiles':
                await this._listFiles(
                    webview,
                    msg['hddPath'] as string | undefined,
                    msg['partitionId'] as string | undefined,
                );
                break;
            case 'readPartitionFile':
                await this._readPartitionFile(
                    webview,
                    msg['hddPath'] as string | undefined,
                    msg['partitionId'] as string | undefined,
                    msg['fileName'] as string | undefined,
                    msg['source'] as string | undefined,
                );
                break;
            case 'exportPartition':
                await this._exportPartition(
                    webview,
                    msg['hddPath'] as string | undefined,
                    msg['partitionId'] as string | undefined,
                    msg['files'] as HddFileEntryInfo[] | undefined,
                );
                break;
            case 'exportFile':
                await this._exportFile(
                    webview,
                    msg['hddPath'] as string | undefined,
                    msg['partitionId'] as string | undefined,
                    msg['fileName'] as string | undefined,
                    msg['source'] as string | undefined,
                );
                break;
            case 'deletePartition':
                await this._deletePartition(
                    webview,
                    msg['hddPath'] as string | undefined,
                    msg['partitionId'] as string | undefined,
                );
                break;
            case 'revealPath':
                await this._revealPath(msg['path'] as string | undefined);
                break;
            case 'dropOpen':
                await this._dropOpen(webview, (msg['uris'] as string[] | undefined) ?? [], (msg['names'] as string[] | undefined) ?? []);
                break;
            case 'prefetchFile':
                await this._prefetchFile(
                    webview,
                    msg['hddPath'] as string | undefined,
                    msg['partitionId'] as string | undefined,
                    msg['fileName'] as string | undefined,
                    msg['source'] as string | undefined,
                );
                break;
            case 'listSnapshots':
                this._postSnapshots(webview, String(msg['partitionId'] ?? ''));
                break;
            case 'snapshotPartition':
                await this._snapshotPartition(webview, msg['hddPath'] as string | undefined, String(msg['partitionId'] ?? ''));
                break;
            case 'deleteSnapshot':
                this._snapshots.remove(String(msg['partitionId'] ?? ''), String(msg['id'] ?? ''));
                this._postSnapshots(webview, String(msg['partitionId'] ?? ''));
                break;
            case 'diffSnapshot':
                await this._diffSnapshot(webview, msg['hddPath'] as string | undefined, String(msg['partitionId'] ?? ''), String(msg['id'] ?? ''));
                break;
            case 'bootstrapError':
                getOutputChannel().error('[HddBrowser] Bootstrap error: ' + msg['error']);
                break;
        }
    }

    private async _openHdd(webview: vscode.Webview): Promise<void> {
        const uris = await vscode.window.showOpenDialog({
            title: 'Open PS2 HDD Image',
            canSelectFiles: true,
            canSelectFolders: false,
            canSelectMany: false,
            filters: {
                'PS2 HDD Images': ['raw', 'img', 'hdd'],
                'All Files': ['*'],
            },
        });
        if (!uris?.length) { return; }
        await this._loadHdd(webview, uris[0].fsPath);
    }

    private async _newHdd(webview: vscode.Webview): Promise<void> {
        const target = await vscode.window.showSaveDialog({
            title: 'Create PS2 HDD Image',
            saveLabel: 'Create HDD',
            defaultUri: vscode.Uri.file(path.join(os.homedir(), 'Documents', 'HDD.raw')),
            filters: {
                'PS2 HDD Image': ['raw'],
                'All Files': ['*'],
            },
        });
        if (!target) { return; }

        const rawSize = await vscode.window.showInputBox({
            title: 'HDD Size',
            value: '40',
            prompt: 'Size in GB',
            validateInput: value => {
                const n = Number(value);
                return Number.isInteger(n) && n > 0 ? null : 'Enter a positive whole number.';
            },
        });
        if (rawSize === undefined) { return; }

        if (!this._ensureConnected(webview)) { return; }
        try {
            this._postStatus(webview, 'Creating HDD image...', true);
            await this._socketClient.hddCreate(target.fsPath, Number(rawSize));
            await this._loadHdd(webview, target.fsPath);
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _loadHdd(webview: vscode.Webview, hddPath: string | undefined): Promise<void> {
        const cleanPath = hddPath?.trim();
        if (!cleanPath) { return; }
        if (!this._ensureConnected(webview)) { return; }

        try {
            this._postStatus(webview, 'Reading HDD partitions...', true);
            const partitions = await this._socketClient.hddList(cleanPath);
            webview.postMessage({ type: 'hddLoaded', hddPath: cleanPath, partitions });
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _listFiles(webview: vscode.Webview, hddPath: string | undefined, partitionId: string | undefined): Promise<void> {
        if (!hddPath || !partitionId) { return; }
        if (!this._ensureConnected(webview)) { return; }

        try {
            const files = await this._socketClient.hddListFiles(hddPath, partitionId);
            webview.postMessage({ type: 'hddFilesLoaded', partitionId, files });
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _readPartitionFile(
        webview: vscode.Webview,
        hddPath: string | undefined,
        partitionId: string | undefined,
        fileName: string | undefined,
        source: string | undefined,
    ): Promise<void> {
        if (!hddPath || !partitionId || !fileName) { return; }
        if (!this._ensureConnected(webview)) { return; }

        try {
            const data = await this._socketClient.hddReadFile(hddPath, partitionId, fileName, source);
            webview.postMessage({ type: 'hddFileData', partitionId, fileName, data });
        } catch (err) {
            webview.postMessage({
                type: 'hddFileError',
                partitionId,
                fileName,
                message: err instanceof Error ? err.message : String(err),
            });
        }
    }

    private async _exportPartition(
        webview: vscode.Webview,
        hddPath: string | undefined,
        partitionId: string | undefined,
        files: HddFileEntryInfo[] | undefined,
    ): Promise<void> {
        if (!hddPath || !partitionId) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const dirs = await vscode.window.showOpenDialog({
            title: `Export ${partitionId}`,
            canSelectFiles: false,
            canSelectFolders: true,
            canSelectMany: false,
        });
        if (!dirs?.length) { return; }

        try {
            this._postStatus(webview, 'Exporting partition files...', true);
            const currentFiles = files?.length ? files : await this._socketClient.hddListFiles(hddPath, partitionId);
            const exportDir = path.join(dirs[0].fsPath, this._safeName(partitionId));
            await fs.promises.mkdir(exportDir, { recursive: true });

            let count = 0;
            let lastPath = exportDir;
            for (const file of currentFiles.filter(f => !f.IsDirectory)) {
                const targetDir = file.Source === 'header' ? path.join(exportDir, '_header') : exportDir;
                if (file.Source === 'header') {
                    await fs.promises.mkdir(targetDir, { recursive: true });
                }
                lastPath = await this._socketClient.hddExport(hddPath, partitionId, file.Name, targetDir, file.Source);
                count++;
            }

            webview.postMessage({
                type: 'hddOperation',
                message: count === 0 ? 'No root files to export.' : `Exported ${count} file${count === 1 ? '' : 's'}.`,
                result: { outputDirectory: exportDir, outputPath: lastPath, count },
            });
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _exportFile(
        webview: vscode.Webview,
        hddPath: string | undefined,
        partitionId: string | undefined,
        fileName: string | undefined,
        source: string | undefined,
    ): Promise<void> {
        if (!hddPath || !partitionId || !fileName) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const dirs = await vscode.window.showOpenDialog({
            title: `Export ${fileName}`,
            canSelectFiles: false,
            canSelectFolders: true,
            canSelectMany: false,
        });
        if (!dirs?.length) { return; }

        try {
            this._postStatus(webview, 'Exporting file...', true);
            const outputPath = await this._socketClient.hddExport(hddPath, partitionId, fileName, dirs[0].fsPath, source);
            webview.postMessage({
                type: 'hddOperation',
                message: `Exported ${fileName}.`,
                result: { outputPath, count: 1 },
            });
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _deletePartition(webview: vscode.Webview, hddPath: string | undefined, partitionId: string | undefined): Promise<void> {
        if (!hddPath || !partitionId) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const confirm = await vscode.window.showWarningMessage(
            `Delete HDD partition "${partitionId}"?`,
            { modal: true },
            'Delete',
        );
        if (confirm !== 'Delete') { return; }

        try {
            this._postStatus(webview, 'Deleting partition...', true);
            await this._socketClient.hddDelete(hddPath, partitionId);
            await this._loadHdd(webview, hddPath);
        } catch (err) {
            this._postError(webview, err);
        }
    }

    // ── Drag and drop ──────────────────────────────────────────────────────

    /** Opens an HDD image dropped from the VS Code explorer. */
    private async _dropOpen(webview: vscode.Webview, uris: string[], names: string[]): Promise<void> {
        for (const raw of uris) {
            let fsPath: string;
            try { fsPath = vscode.Uri.parse(raw.trim()).fsPath; } catch { continue; }
            if (HDD_EXTENSIONS.includes(path.extname(fsPath).toLowerCase()) && fs.existsSync(fsPath)) {
                await this._loadHdd(webview, fsPath);
                return;
            }
        }
        const dropped = names[0] ?? (uris[0] ? path.basename(vscode.Uri.parse(uris[0]).fsPath) : 'that');
        const isImage = HDD_EXTENSIONS.includes(path.extname(dropped).toLowerCase());
        this._postError(webview, new Error(isImage
            ? `To open ${dropped}, drag it from the VS Code explorer or use Open.`
            // HDD_IMPORT needs PFS write support in the bridge (see HddService.cs).
            : `Can't import ${dropped}: writing files into HDD partitions isn't supported yet. Drop an HDD image to open it.`));
    }

    /** Reads a root file ahead of a drag so the drag can carry its bytes. */
    private async _prefetchFile(
        webview: vscode.Webview,
        hddPath: string | undefined,
        partitionId: string | undefined,
        fileName: string | undefined,
        source: string | undefined,
    ): Promise<void> {
        if (!hddPath || !partitionId || !fileName || !this._socketClient.isConnected()) { return; }
        try {
            const data = await this._socketClient.hddReadFile(hddPath, partitionId, fileName, source);
            if (data.length * 0.75 > MAX_DRAG_BYTES) { return; }
            webview.postMessage({ type: 'hddFileBytes', partitionId, fileName, source: source ?? '', data });
        } catch { /* the row just won't drag a file out */ }
    }

    // ── Snapshots and diffs ────────────────────────────────────────────────

    private _postSnapshots(webview: vscode.Webview, partitionId: string): void {
        webview.postMessage({ type: 'hddSnapshots', partitionId, snapshots: partitionId ? this._snapshots.list(partitionId) : [] });
    }

    /** Every root file of a partition (header files prefixed "_header/"). */
    private async _readPartition(hddPath: string, partitionId: string): Promise<SaveFile[]> {
        const list = (await this._socketClient.hddListFiles(hddPath, partitionId)).filter(f => !f.IsDirectory);
        const total = list.reduce((n, f) => n + (f.Size || 0), 0);
        if (total > MAX_SNAPSHOT_BYTES) {
            throw new Error(`${partitionId} has ${(total / 1048576).toFixed(0)} MB of root files; snapshots are limited to ${MAX_SNAPSHOT_BYTES / 1048576} MB (use them for save partitions).`);
        }
        const files: SaveFile[] = [];
        for (const f of list) {
            const b64 = await this._socketClient.hddReadFile(hddPath, partitionId, f.Name, f.Source);
            files.push({ name: f.Source === 'header' ? `_header/${f.Name}` : f.Name, data: Buffer.from(b64, 'base64') });
        }
        return files;
    }

    private async _snapshotPartition(webview: vscode.Webview, hddPath: string | undefined, partitionId: string): Promise<void> {
        if (!hddPath || !partitionId) { return; }
        if (!this._ensureConnected(webview)) { return; }
        const label = await vscode.window.showInputBox({
            title: `Snapshot ${partitionId}`,
            prompt: 'Optional note, e.g. "after chapter 2"',
            placeHolder: 'Note',
        });
        if (label === undefined) { return; }
        try {
            this._postStatus(webview, `Copying ${partitionId}...`, true);
            this._snapshots.add(partitionId, hddPath, await this._readPartition(hddPath, partitionId), label.trim());
            this._postStatus(webview, `Snapshot of ${partitionId} stored`, false);
            this._postSnapshots(webview, partitionId);
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _diffSnapshot(webview: vscode.Webview, hddPath: string | undefined, partitionId: string, id: string): Promise<void> {
        if (!hddPath || !partitionId || !id) { return; }
        if (!this._ensureConnected(webview)) { return; }
        try {
            this._postStatus(webview, `Comparing ${partitionId}...`, true);
            const files = diffSaveFiles(this._snapshots.read(partitionId, id), await this._readPartition(hddPath, partitionId));
            webview.postMessage({ type: 'hddDiff', partitionId, id, files });
            const changed = files.filter(f => f.status !== 'same').length;
            this._postStatus(webview, changed ? `${changed} file(s) differ from the snapshot` : 'Partition matches the snapshot', false);
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private _ensureConnected(webview: vscode.Webview): boolean {
        if (this._socketClient.isConnected()) { return true; }
        webview.postMessage({
            type: 'hddError',
            message: 'Not connected to the CodeDesigner server.',
        });
        return false;
    }

    private _postStatus(webview: vscode.Webview, message: string, busy: boolean): void {
        webview.postMessage({ type: 'hddStatus', message, busy });
    }

    private _postError(webview: vscode.Webview, err: unknown): void {
        webview.postMessage({
            type: 'hddError',
            message: err instanceof Error ? err.message : String(err),
        });
    }

    private async _revealPath(targetPath: string | undefined): Promise<void> {
        if (!targetPath) { return; }
        await vscode.commands.executeCommand('revealFileInOS', vscode.Uri.file(targetPath));
    }

    private _safeName(name: string): string {
        return name.replace(/[<>:"/\\|?*\x00-\x1f]/g, '_') || 'partition';
    }

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'dist', 'webview', 'hdd-browser.js'),
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
<app-hdd-browser-page>
  <p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading PS2 HDD Browser...</p>
</app-hdd-browser-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
