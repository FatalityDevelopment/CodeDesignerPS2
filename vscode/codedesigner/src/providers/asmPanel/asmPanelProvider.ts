import * as vscode from 'vscode';
import { getNonce } from '../../utils/nonce';
import { getAsmPanelHtml, AsmPanelData } from './asmPanelTemplate';
import { getSocketClient } from '../../extension';

export class AsmPanelProvider implements vscode.WebviewViewProvider {
    private webview?: vscode.Webview;

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
            enableScripts: true
        };

        // Generate HTML from template
        const templateData: AsmPanelData = {
            code: '',
            emulators: ['Project64', 'Mupen64Plus', 'RetroArch']
        };
        webviewView.webview.html = getAsmPanelHtml(templateData, getNonce());

        // Listen to messages from the webview
        webviewView.webview.onDidReceiveMessage(async message => {
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
}
