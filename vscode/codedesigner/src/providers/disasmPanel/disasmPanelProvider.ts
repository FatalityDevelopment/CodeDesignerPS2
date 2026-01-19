import * as vscode from 'vscode';
import { getNonce } from '../../helpers';
import { getDisasmPanelHtml, DisasmPanelData } from './disasmPanelTemplate';

export class DisasmPanelProvider implements vscode.WebviewViewProvider {
    // Register the provider with VS Code
    public register(context: vscode.ExtensionContext) {
        context.subscriptions.push(
            vscode.window.registerWebviewViewProvider('disasm.disasmPanel', this)
        );
    }

    resolveWebviewView(webviewView: vscode.WebviewView): void {
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
        webviewView.webview.onDidReceiveMessage(msg => {
            if (msg.command === 'connect') {
                vscode.window.showInformationMessage('Panel works!');
            }
        });
    }
}
