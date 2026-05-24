import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { SocketClient, ZipperOperationResult } from '../../socketClient';
import { getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';

export class ZipperWorkbenchProvider {
    public static readonly viewType = 'codedesigner.zipperWorkbench';

    private _panel: vscode.WebviewPanel | undefined;
    private _pendingInitialPath = '';

    constructor(
        private readonly _extensionUri: vscode.Uri,
        private readonly _socketClient: SocketClient,
    ) {}

    public open(initialPath?: string): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.Active);
            if (initialPath) { void this._inspectPath(this._panel.webview, initialPath); }
            return;
        }

        this._pendingInitialPath = initialPath ?? '';
        const panel = vscode.window.createWebviewPanel(
            ZipperWorkbenchProvider.viewType,
            'Zipper FileFormats',
            vscode.ViewColumn.Active,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [this._extensionUri],
            },
        );

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
                webview.postMessage({ type: 'zipperReady' });
                if (this._pendingInitialPath) {
                    const initial = this._pendingInitialPath;
                    this._pendingInitialPath = '';
                    await this._inspectPath(webview, initial);
                }
                break;
            case 'openFile':
                await this._openFile(webview);
                break;
            case 'refresh':
                await this._inspectPath(webview, msg['path'] as string | undefined);
                break;
            case 'extractAll':
                await this._extractAll(webview, msg['path'] as string | undefined);
                break;
            case 'extractEntry':
                await this._extractEntry(
                    webview,
                    msg['path'] as string | undefined,
                    msg['entryIndex'] as number | undefined,
                    msg['entryName'] as string | undefined,
                );
                break;
            case 'saveExportDecrypted':
                await this._saveExportDecrypted(webview, msg['path'] as string | undefined);
                break;
            case 'saveCraft':
                await this._saveCraft(webview, msg['path'] as string | undefined, msg['mode'] as string | undefined);
                break;
            case 'apacheDecryptSection':
                await this._apacheDecryptSection(
                    webview,
                    msg['path'] as string | undefined,
                    msg['section'] as 'ftscore' | 'zsealetc' | undefined,
                );
                break;
            case 'apacheBuildPatch':
                await this._apacheBuildPatch(webview, msg['path'] as string | undefined);
                break;
            case 'revealPath':
                await this._revealPath(msg['path'] as string | undefined);
                break;
            case 'readNode':
                await this._readNode(
                    webview,
                    msg['path'] as string | undefined,
                    msg['entryIndex'] as number | undefined,
                );
                break;
            case 'readPaletteFile':
                await this._readPaletteFile(webview, msg['path'] as string | undefined);
                break;
            case 'readCompanionFiles':
                await this._readCompanionFiles(webview, msg['path'] as string | undefined);
                break;
            case 'inspectRdr':
                await this._inspectRdr(
                    webview,
                    msg['path'] as string | undefined,
                    msg['entryName'] as string | undefined,
                );
                break;
            case 'bootstrapError':
                getOutputChannel().error('[ZipperWorkbench] Bootstrap error: ' + msg['error']);
                break;
        }
    }

    private async _readNode(
        webview: vscode.Webview,
        filePath: string | undefined,
        entryIndex: number | undefined,
    ): Promise<void> {
        if (!filePath || entryIndex === undefined) { return; }
        if (!this._ensureConnected(webview)) { return; }
        try {
            const data = await this._socketClient.zipperReadNode(filePath, entryIndex);
            webview.postMessage({ type: 'nodeData', entryIndex, data });
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            webview.postMessage({ type: 'nodeError', entryIndex, message });
        }
    }

    private async _readPaletteFile(
        webview: vscode.Webview,
        currentPath: string | undefined,
    ): Promise<void> {
        if (!currentPath) { return; }
        if (!this._ensureConnected(webview)) { return; }
        try {
            const palPath = this._findSiblingPaletteFile(currentPath);
            if (!palPath) {
                webview.postMessage({ type: 'paletteFile', found: false });
                return;
            }
            const nodes = await this._socketClient.zipperReadAllNodes(palPath);
            webview.postMessage({ type: 'paletteFile', found: true, path: palPath, nodes });
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            webview.postMessage({ type: 'paletteFile', found: false, message });
        }
    }

    private async _readCompanionFiles(
        webview: vscode.Webview,
        currentPath: string | undefined,
    ): Promise<void> {
        if (!currentPath) { return; }
        if (!this._ensureConnected(webview)) { return; }
        try {
            const companions = this._findCompanionFiles(currentPath);
            const out: Record<string, { path: string; nodes: Array<{ index: number; name: string; type: number; data: string }> }> = {};
            for (const [kind, p] of Object.entries(companions)) {
                if (!p) { continue; }
                try {
                    const nodes = await this._socketClient.zipperReadAllNodes(p);
                    out[kind] = { path: p, nodes };
                } catch (err) {
                    getOutputChannel().warn(`[ZipperWorkbench] companion ${kind} load failed: ${err instanceof Error ? err.message : err}`);
                }
            }
            webview.postMessage({ type: 'companionFiles', companions: out });
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            webview.postMessage({ type: 'companionFiles', companions: {}, message });
        }
    }

    private async _inspectRdr(
        webview: vscode.Webview,
        filePath: string | undefined,
        entryName: string | undefined,
    ): Promise<void> {
        if (!filePath) { return; }
        if (!this._ensureConnected(webview)) { return; }
        try {
            const result = await this._socketClient.zipperInspectRdr(filePath, entryName);
            webview.postMessage({ type: 'rdrData', entryName, result });
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            webview.postMessage({ type: 'rdrError', entryName, message });
        }
    }

    private _findCompanionFiles(currentPath: string): Partial<Record<'txr' | 'pal' | 'geo' | 'mdl', string>> {
        const dir = path.dirname(currentPath);
        const base = path.basename(currentPath);
        const result: Partial<Record<'txr' | 'pal' | 'geo' | 'mdl', string>> = {};

        // Match the prefix before _MDL / _TXR / _PAL / _GEO.
        const familyMatch = base.match(/^(.+?)_(MDL|TXR|PAL|GEO)(\([^)]+\))?\.ZED$/i);
        if (!familyMatch) { return result; }
        const prefix = familyMatch[1];

        const candidates: Record<'txr' | 'pal' | 'geo' | 'mdl', string[]> = {
            txr: [`${prefix}_TXR.ZED`, `${prefix}_TXR(1).ZED`],
            pal: [`${prefix}_PAL.ZED`, `${prefix}_PAL(1).ZED`],
            geo: [`${prefix}_GEO.ZED`],
            mdl: [`${prefix}_MDL.ZED`],
        };

        for (const [kind, names] of Object.entries(candidates) as Array<['txr' | 'pal' | 'geo' | 'mdl', string[]]>) {
            for (const name of names) {
                const full = path.join(dir, name);
                if (fs.existsSync(full)) {
                    result[kind] = full;
                    break;
                }
            }
        }
        return result;
    }

    private _findSiblingPaletteFile(currentPath: string): string | undefined {
        const dir = path.dirname(currentPath);
        const base = path.basename(currentPath);
        const candidates: string[] = [];

        const txrMatch = base.match(/^(.*?)_TXR(?:\([^)]+\))?\.ZED$/i);
        if (txrMatch) {
            candidates.push(`${txrMatch[1]}_PAL.ZED`);
            candidates.push(`${txrMatch[1]}_PAL(1).ZED`);
        }
        candidates.push(base.replace(/_TXR/i, '_PAL'));

        for (const candidate of candidates) {
            const full = path.join(dir, candidate);
            if (fs.existsSync(full)) { return full; }
        }

        try {
            for (const entry of fs.readdirSync(dir)) {
                if (/_PAL(?:\([^)]+\))?\.ZED$/i.test(entry)) {
                    return path.join(dir, entry);
                }
            }
        } catch { /* ignore */ }
        return undefined;
    }

    private async _openFile(webview: vscode.Webview): Promise<void> {
        const uris = await vscode.window.showOpenDialog({
            title: 'Open Zipper/FileFormats File',
            canSelectFiles: true,
            canSelectFolders: false,
            canSelectMany: false,
            filters: {
                'Zipper/FileFormats': ['zdb', 'zar', 'zed'],
                'All Files': ['*'],
            },
        });
        if (!uris?.length) { return; }
        await this._inspectPath(webview, uris[0].fsPath);
    }

    private async _inspectPath(webview: vscode.Webview, filePath: string | undefined): Promise<void> {
        const cleanPath = filePath?.trim();
        if (!cleanPath) { return; }
        if (!this._ensureConnected(webview)) { return; }

        try {
            this._postStatus(webview, 'Inspecting file...', true);
            const result = await this._socketClient.zipperInspect(cleanPath);
            webview.postMessage({ type: 'zipperLoaded', result });
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _extractAll(webview: vscode.Webview, filePath: string | undefined): Promise<void> {
        if (!filePath) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const dirs = await vscode.window.showOpenDialog({
            title: 'Extract All',
            canSelectFiles: false,
            canSelectFolders: true,
            canSelectMany: false,
        });
        if (!dirs?.length) { return; }

        try {
            this._postStatus(webview, 'Extracting entries...', true);
            const result = await this._socketClient.zipperExtractAll(filePath, dirs[0].fsPath);
            this._postOperation(webview, 'Extracted entries.', result);
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _extractEntry(
        webview: vscode.Webview,
        filePath: string | undefined,
        entryIndex: number | undefined,
        entryName: string | undefined,
    ): Promise<void> {
        if (!filePath || entryIndex === undefined) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const target = await vscode.window.showSaveDialog({
            title: `Extract ${entryName ?? 'Entry'}`,
            defaultUri: vscode.Uri.file(this._defaultOutputPath(filePath, entryName ?? `entry_${entryIndex}`)),
            filters: { 'All Files': ['*'] },
        });
        if (!target) { return; }

        try {
            this._postStatus(webview, 'Extracting entry...', true);
            const result = await this._socketClient.zipperExtractEntry(filePath, entryIndex, target.fsPath);
            this._postOperation(webview, 'Extracted entry.', result);
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _saveExportDecrypted(webview: vscode.Webview, filePath: string | undefined): Promise<void> {
        if (!filePath) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const target = await vscode.window.showSaveDialog({
            title: 'Export Decrypted Save',
            defaultUri: vscode.Uri.file(this._defaultOutputPath(filePath, `${path.basename(filePath)}.decrypted.bin`)),
            filters: { 'Binary': ['bin'], 'All Files': ['*'] },
        });
        if (!target) { return; }

        try {
            this._postStatus(webview, 'Exporting decrypted save...', true);
            const result = await this._socketClient.zipperSaveExportDecrypted(filePath, target.fsPath);
            this._postOperation(webview, 'Exported decrypted save.', result);
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _saveCraft(webview: vscode.Webview, filePath: string | undefined, mode: string | undefined): Promise<void> {
        if (!filePath) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const selectedMode = this._normalizeSaveMode(mode);
        let title: string | undefined;
        let titleLength: number | undefined;

        if (selectedMode === 'active' || selectedMode === 'extreme') {
            title = await vscode.window.showInputBox({
                title: selectedMode === 'active' ? 'Active Save Title' : 'Extreme Probe Title',
                value: selectedMode === 'active' ? 'SOCOM Revival Save' : 'EXTREME INT TEST',
            });
            if (title === undefined) { return; }
        } else if (selectedMode === 'overflow') {
            const raw = await vscode.window.showInputBox({
                title: 'Overflow Probe Title Length',
                value: '200',
                validateInput: value => {
                    const n = Number(value);
                    return Number.isInteger(n) && n > 0 ? null : 'Enter a positive integer.';
                },
            });
            if (raw === undefined) { return; }
            titleLength = Number(raw);
        }

        const dirs = await vscode.window.showOpenDialog({
            title: 'Save Crafted Files',
            canSelectFiles: false,
            canSelectFolders: true,
            canSelectMany: false,
        });
        if (!dirs?.length) { return; }

        try {
            this._postStatus(webview, 'Crafting save file...', true);
            const result = await this._socketClient.zipperSaveCraft(filePath, dirs[0].fsPath, selectedMode, title, titleLength);
            this._postOperation(webview, 'Crafted save file.', result);
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _apacheDecryptSection(
        webview: vscode.Webview,
        filePath: string | undefined,
        section: 'ftscore' | 'zsealetc' | undefined,
    ): Promise<void> {
        if (!filePath || !section) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const dirs = await vscode.window.showOpenDialog({
            title: `Decrypt ${section}`,
            canSelectFiles: false,
            canSelectFolders: true,
            canSelectMany: false,
        });
        if (!dirs?.length) { return; }

        try {
            this._postStatus(webview, `Decrypting ${section}...`, true);
            const result = await this._socketClient.apacheDecryptSection(filePath, dirs[0].fsPath, section);
            this._postOperation(webview, `Decrypted ${section}.`, result);
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _apacheBuildPatch(webview: vscode.Webview, filePath: string | undefined): Promise<void> {
        if (!filePath) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const ftScore = await vscode.window.showOpenDialog({
            title: 'Select Decrypted FTScore MWo3',
            canSelectFiles: true,
            canSelectFolders: false,
            canSelectMany: false,
            filters: { 'Binary': ['bin'], 'All Files': ['*'] },
        });
        if (!ftScore?.length) { return; }

        const dirs = await vscode.window.showOpenDialog({
            title: 'Patch Output Folder',
            canSelectFiles: false,
            canSelectFolders: true,
            canSelectMany: false,
        });
        if (!dirs?.length) { return; }

        try {
            this._postStatus(webview, 'Building APACHE00 patch...', true);
            const result = await this._socketClient.apacheBuildPatch(filePath, ftScore[0].fsPath, dirs[0].fsPath);
            this._postOperation(webview, `Built APACHE00 patch${result.buildVersion ? ` ${result.buildVersion}` : ''}.`, result);
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private _ensureConnected(webview: vscode.Webview): boolean {
        if (this._socketClient.isConnected()) { return true; }
        webview.postMessage({
            type: 'zipperError',
            message: 'Not connected to the CodeDesigner server.',
        });
        return false;
    }

    private _postStatus(webview: vscode.Webview, message: string, busy: boolean): void {
        webview.postMessage({ type: 'zipperStatus', message, busy });
    }

    private _postError(webview: vscode.Webview, err: unknown): void {
        const message = err instanceof Error ? err.message : String(err);
        webview.postMessage({ type: 'zipperError', message });
    }

    private _postOperation(webview: vscode.Webview, message: string, result: ZipperOperationResult): void {
        webview.postMessage({ type: 'zipperOperation', message, result });
    }

    private async _revealPath(targetPath: string | undefined): Promise<void> {
        if (!targetPath) { return; }
        await vscode.commands.executeCommand('revealFileInOS', vscode.Uri.file(targetPath));
    }

    private _defaultOutputPath(sourcePath: string, name: string): string {
        const dir = path.dirname(sourcePath);
        return path.join(dir, this._safeFileName(name));
    }

    private _safeFileName(name: string): string {
        return name.replace(/[<>:"/\\|?*\x00-\x1f]/g, '_') || 'entry.bin';
    }

    private _normalizeSaveMode(mode: string | undefined): 'active' | 'overflow' | 'extreme' | 'all' {
        switch ((mode ?? '').toLowerCase()) {
            case 'overflow': return 'overflow';
            case 'extreme': return 'extreme';
            case 'all': return 'all';
            default: return 'active';
        }
    }

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'dist', 'webview', 'zipper-workbench.js'),
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
<app-zipper-workbench-page>
  <p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Zipper FileFormats...</p>
</app-zipper-workbench-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
