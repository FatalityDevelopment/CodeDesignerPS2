import * as vscode from 'vscode';
import * as path from 'path';
import * as os from 'os';
import { SocketClient, McSaveInfo } from '../../socketClient';
import { getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';

/**
 * Standalone PS2 memory-card browser webview.
 *
 * The bridge owns card filesystem access. The webview owns icon.sys/icon parsing
 * and WebGL rendering so filtering, selection, and animation stay responsive.
 */
export class MemoryCardBrowserProvider {
    private _panel: vscode.WebviewPanel | undefined;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly socketClient: SocketClient,
    ) {}

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
