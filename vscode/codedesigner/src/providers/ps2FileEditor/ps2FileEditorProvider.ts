import * as vscode from 'vscode';
import { SocketClient } from '../../socketClient';
import { DisasmPanelProvider } from '../disasmPanel/disasmPanelProvider';
import { getOutputChannel } from '../../extension';

/**
 * Custom readonly editor for PS2 binary files (.elf, .bin, .raw).
 *
 * When VS Code opens one of these files it routes them through this provider
 * instead of showing a raw text/hex view.  The provider shows a minimal
 * placeholder webview and immediately loads the file into the internal
 * emulator via the STARTFILEMODE socket command.
 */
export class Ps2FileEditorProvider implements vscode.CustomReadonlyEditorProvider {

    public static readonly viewType = 'codedesigner.ps2File';

    constructor(
        private readonly _socketClient: SocketClient,
        private readonly _panelProvider: DisasmPanelProvider,
        private readonly _extensionUri: vscode.Uri,
    ) {}

    // ── CustomReadonlyEditorProvider ─────────────────────────────────────────

    openCustomDocument(uri: vscode.Uri): vscode.CustomDocument {
        return { uri, dispose: () => {} };
    }

    async resolveCustomEditor(
        document: vscode.CustomDocument,
        webviewPanel: vscode.WebviewPanel,
    ): Promise<void> {
        const filePath = document.uri.fsPath;
        const fileName = filePath.split(/[\\/]/).pop() ?? filePath;

        webviewPanel.webview.options = { enableScripts: false };
        webviewPanel.webview.html = this._getHtml(fileName, 'Loading file into emulator…');

        const out = getOutputChannel();

        if (!this._socketClient.isConnected()) {
            const msg = 'Not connected to emulator. Connect first, then re-open the file.';
            out.warn(msg);
            webviewPanel.webview.html = this._getHtml(fileName, msg);
            return;
        }

        // Close this placeholder tab — openFileMode opens the real live
        // memory panels via the DisasmEditorProvider.
        webviewPanel.dispose();

        // Delegate entirely to the panel provider which sends the
        // STARTFILEMODE socket command and opens live memory panels.
        await this._panelProvider.openFileMode(filePath);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private _getHtml(fileName: string, status: string): string {
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none';">
<style>
  html, body {
    margin: 0; padding: 24px;
    background: #1e1e1e; color: #ccc;
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
    font-size: 14px;
  }
  h2 { margin: 0 0 12px; color: #e0e0e0; }
  p  { margin: 0; }
</style>
</head>
<body>
  <h2>${this._escapeHtml(fileName)}</h2>
  <p>${this._escapeHtml(status)}</p>
</body>
</html>`;
    }

    private _escapeHtml(text: string): string {
        return text
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }
}
