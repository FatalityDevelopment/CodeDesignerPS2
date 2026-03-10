import * as vscode from 'vscode';
import { getSocketClient, getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';

export class AsmPanelProvider implements vscode.WebviewViewProvider {
    private webview?: vscode.Webview;

    constructor(private readonly _extensionUri: vscode.Uri) {}

    // Register the provider with VS Code
    public register(context: vscode.ExtensionContext) {
        context.subscriptions.push(
            vscode.window.registerWebviewViewProvider('asm.asmPanel', this)
        );
    }

    resolveWebviewView(webviewView: vscode.WebviewView): void {
        this.webview = webviewView.webview;

        // Setup webview options
        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [this._extensionUri]
        };

        webviewView.webview.html = this.getHtml(webviewView.webview);

        // Listen to messages from the webview
        webviewView.webview.onDidReceiveMessage(async message => {
            if (message.type === 'bootstrapError') {
                getOutputChannel().appendLine(
                    `[ERROR] Asm panel Angular bootstrap failed: ${message.error ?? '(no details)'}`
                );
                return;
            }
            switch (message.command) {
                case 'connect':
                    vscode.window.showInformationMessage(
                        'Connect clicked (emulator hookup later)'
                    );
                    break;

                case 'assemble':
                    await this.handleAssemble(message.data);
                    break;
            }
        });
    }

    private async handleAssemble(asmCode: string) {
        const socketClient = getSocketClient();
        
        if (!socketClient || !socketClient.isConnected()) {
            this.sendResult(false, '', 'Not connected to C# server. Use "Connect to Server" command first.');
            return;
        }

        try {
            const results = await socketClient.assemble(asmCode);
            
            // Format results as hex output
            const hexLines: string[] = [];
            for (const result of results) {
                if (result.Success) {
                    hexLines.push(`${result.Hex}  # ${result.Assembly}`);
                } else {
                    hexLines.push(`ERROR: ${result.Error} (${result.Assembly})`);
                }
            }
            
            this.sendResult(true, hexLines.join('\n'), '');
        } catch (error: any) {
            this.sendResult(false, '', error.message || 'Assembly failed');
        }
    }

    private sendResult(success: boolean, data: string, error: string) {
        if (this.webview) {
            this.webview.postMessage({
                command: 'assembleResult',
                success: success,
                data: data,
                error: error
            });
        }
    }

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
<app-asm-panel><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading...</p></app-asm-panel>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
