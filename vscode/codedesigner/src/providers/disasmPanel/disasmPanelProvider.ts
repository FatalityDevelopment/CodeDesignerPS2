import * as vscode from 'vscode';
import { BasePanelProvider } from '../baseProviders/basePanelProvider';

export class DisasmPanelProvider extends BasePanelProvider {
    constructor() {
        super('disasm.disasmPanel', 'disasmPanelProvider.html', "disasmPanel"); // HTML file
    }

    protected onDidResolveWebviewView(webviewView: vscode.WebviewView) {
        // Listen to messages from the webview
        webviewView.webview.onDidReceiveMessage(msg => {
            if (msg.command === 'connect') {
                vscode.window.showInformationMessage('Panel works!');
            }
        });
    }
}
