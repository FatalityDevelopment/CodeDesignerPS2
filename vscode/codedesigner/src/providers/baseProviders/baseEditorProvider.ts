import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { getNonce } from '../../helpers';

// Base class with reusable webview logic
export abstract class BaseEditorProvider implements vscode.CustomReadonlyEditorProvider {
    constructor(private htmlFileName: string, private providerName: string) {} // HTML file provided by child

    async openCustomDocument(uri: vscode.Uri): Promise<vscode.CustomDocument> {
        return { uri, dispose() {} };
    }

    protected loadHtml(webview: vscode.Webview): string {
        // Compute the provider folder name by stripping "Provider" from the class name
        const providerFolder = this.providerName;

        // Path to HTML in dist/providers folder (webpack bundles to dist/extension.js)
        const htmlPath = path.join(__dirname, 'providers', providerFolder, this.htmlFileName);
        let html = fs.readFileSync(htmlPath, 'utf8');

        const nonce = getNonce();
        html = html.replace(/<script>/g, `<script nonce="${nonce}">`);
        return html;
    }

    // Implement resolveCustomEditor once in base class
    async resolveCustomEditor(
        document: vscode.CustomDocument,
        webviewPanel: vscode.WebviewPanel
    ): Promise<void> {
        // Common setup
        webviewPanel.webview.options = { enableScripts: true };
        webviewPanel.webview.html = this.loadHtml(webviewPanel.webview);

        // Delegate child-specific logic
        await this.onDidResolveWebview(webviewPanel, document);
    }

    // Child classes must implement this for content-specific logic
    protected abstract onDidResolveWebview(
        webviewPanel: vscode.WebviewPanel,
        document: vscode.CustomDocument
    ): Promise<void>;
}