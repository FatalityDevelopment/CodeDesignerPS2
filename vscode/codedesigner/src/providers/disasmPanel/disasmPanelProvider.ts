import * as vscode from 'vscode';
import { getNonce } from '../../helpers';
import { getDisasmPanelHtml, DisasmPanelData } from './disasmPanelTemplate';
import { getSocketClient } from '../../extension';

export class DisasmPanelProvider implements vscode.WebviewViewProvider {
    private webview?: vscode.Webview;

    // Register the provider with VS Code
    public register(context: vscode.ExtensionContext) {
        context.subscriptions.push(
            vscode.window.registerWebviewViewProvider('disasm.disasmPanel', this)
        );
    }

    resolveWebviewView(webviewView: vscode.WebviewView): void {
        this.webview = webviewView.webview;

        // Setup webview options
        webviewView.webview.options = {
            enableScripts: true
        };

        // Generate HTML from template
        const templateData: DisasmPanelData = {
            connected: false
        };
        webviewView.webview.html = getDisasmPanelHtml(templateData, getNonce());

        // Listen to messages from the webview
        webviewView.webview.onDidReceiveMessage(async msg => {
            if (msg.command === 'disassemble') {
                await this.handleDisassemble(msg.data);
            }
        });
    }

    private async handleDisassemble(hexCode: string) {
        const socketClient = getSocketClient();
        
        if (!socketClient || !socketClient.isConnected()) {
            this.sendResult(false, '', 'Not connected to C# server. Use "Connect to Server" command first.');
            return;
        }

        try {
            const results = await socketClient.disassemble(hexCode);
            
            // Format results as assembly output
            const asmLines: string[] = [];
            for (const result of results) {
                if (result.Success) {
                    const line = result.Comment 
                        ? `${result.Hex}  ${result.Assembly}  ${result.Comment}`
                        : `${result.Hex}  ${result.Assembly}`;
                    asmLines.push(line);
                } else {
                    asmLines.push(`${result.Hex}  ERROR: ${result.Error}`);
                }
            }
            
            this.sendResult(true, asmLines.join('\n'), '');
        } catch (error: any) {
            this.sendResult(false, '', error.message || 'Disassembly failed');
        }
    }

    private sendResult(success: boolean, data: string, error: string) {
        if (this.webview) {
            this.webview.postMessage({
                command: 'disassembleResult',
                success: success,
                data: data,
                error: error
            });
        }
    }
}
