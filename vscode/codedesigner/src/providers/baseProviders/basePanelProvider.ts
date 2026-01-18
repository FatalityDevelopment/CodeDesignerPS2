import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { getNonce } from '../../helpers';

/**
 * Base class for panels using WebviewViewProvider
 * htmlFileName: the HTML file to load
 */
export abstract class BasePanelProvider implements vscode.WebviewViewProvider {
    constructor(private viewId: string, private htmlFileName: string, private providerName: string,) {}

    // Register the provider with VS Code
    public register(context: vscode.ExtensionContext) {
        context.subscriptions.push(
            vscode.window.registerWebviewViewProvider(this.viewId, this)
        );
    }

    // This is required by WebviewViewProvider
    resolveWebviewView(webviewView: vscode.WebviewView): void | Thenable<void> {
        // Common setup
        webviewView.webview.options = {
            enableScripts: true
        };

        // Load HTML from file
        webviewView.webview.html = this.loadHtml(webviewView.webview);

        // Delegate child-specific behavior
        this.onDidResolveWebviewView(webviewView);
    }

    // Load HTML and optionally replace nonce
    protected loadHtml(webview: vscode.Webview): string {
        // Compute the provider folder name by stripping "Provider" from the class name
        const providerFolder = this.providerName;

        // Path to HTML in dist/providers folder (webpack bundles to dist/extension.js)
        const htmlPath = path.join(__dirname, 'providers', providerFolder, this.htmlFileName);

        let html = fs.readFileSync(htmlPath, 'utf8');

        // Add nonce for inline scripts
        const nonce = getNonce();
        html = html.replace(/<script>/g, `<script nonce="${nonce}">`);

        return html;
    }

    // Child classes implement this for panel-specific behavior
    protected abstract onDidResolveWebviewView(webviewView: vscode.WebviewView): void;
}