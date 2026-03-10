import * as vscode from 'vscode';
import { getSocketClient, getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';

export class AsmPanelProvider implements vscode.WebviewViewProvider {
    private webview?: vscode.Webview;
    private activeFilePath = '';

    constructor(private readonly _extensionUri: vscode.Uri) {}

    public register(context: vscode.ExtensionContext) {
        context.subscriptions.push(
            vscode.window.registerWebviewViewProvider('asm.asmPanel', this)
        );

        // Watch the active editor so the panel auto-loads a .cds file when the
        // user switches to it.
        context.subscriptions.push(
            vscode.window.onDidChangeActiveTextEditor(editor => {
                this.syncActiveEditor(editor);
            })
        );
    }

    resolveWebviewView(webviewView: vscode.WebviewView): void {
        this.webview = webviewView.webview;

        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [this._extensionUri]
        };

        webviewView.webview.html = this.getHtml(webviewView.webview);

        webviewView.webview.onDidReceiveMessage(async message => {
            if (message.type === 'bootstrapError') {
                getOutputChannel().appendLine(
                    `[ERROR] Asm panel Angular bootstrap failed: ${message.error ?? '(no details)'}`
                );
                return;
            }
            switch (message.command) {
                case 'ready':
                    // Angular is bootstrapped — send current .cds file if one is active.
                    this.syncActiveEditor(vscode.window.activeTextEditor);
                    break;
                case 'compile':
                    await this.handleCompile(message.code as string);
                    break;
            }
        });
    }

    // ── Active-editor sync ────────────────────────────────────────────────────

    private syncActiveEditor(editor: vscode.TextEditor | undefined): void {
        if (!editor) { return; }
        const uri = editor.document.uri;
        if (!uri.fsPath.toLowerCase().endsWith('.cds')) { return; }

        this.activeFilePath = uri.fsPath;
        const content = editor.document.getText();
        this.webview?.postMessage({
            command:  'loadFile',
            filePath: uri.fsPath,
            content
        });
    }

    // ── Compile ───────────────────────────────────────────────────────────────

    private async handleCompile(sourceCode: string): Promise<void> {
        const socketClient = getSocketClient();
        if (!socketClient || !socketClient.isConnected()) {
            this.sendResult(false, '', 'Not connected to the emulator server.');
            return;
        }

        try {
            const results = await socketClient.compileCds(sourceCode);
            const lines: string[] = [];
            for (const r of results) {
                lines.push(r.Success
                    ? `${r.Hex}  # ${r.Assembly}`
                    : `ERROR: ${r.Error} (${r.Assembly})`);
            }
            this.sendResult(true, lines.join('\n'), '');
        } catch (err: any) {
            this.sendResult(false, '', err?.message ?? 'Compilation failed');
        }
    }

    private sendResult(success: boolean, data: string, error: string): void {
        this.webview?.postMessage({ command: 'compileResult', success, data, error });
    }

    // ── HTML ──────────────────────────────────────────────────────────────────

    private getHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'dist', 'webview', 'asm-panel.js')
        );
        const nonce = getNonce();
        return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               script-src ${webview.cspSource} 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<script nonce="${nonce}">${getWebviewErrorHandlerScript()}</script>
</head>
<body>
<app-asm-panel><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading…</p></app-asm-panel>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
