import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';

/** Quiet period after the last bundle write before the webviews are reloaded. */
const RELOAD_DEBOUNCE_MS = 800;

/**
 * Development-only: reloads every open webview whenever `npm run watch`
 * rewrites the webview bundles in `dist/webview`, so editing an Angular
 * component shows up in the open panel without reloading the window.
 *
 * Uses VS Code's built-in "Developer: Reload Webviews" command, which re-runs
 * each page and so triggers the same `ready` → `init` handshake as a fresh open.
 * Extension-host code (src/providers/**) is not covered; that still needs a
 * window reload.
 *
 * Does nothing outside the Extension Development Host.
 */
export function registerWebviewDevReload(context: vscode.ExtensionContext): void {
    if (context.extensionMode !== vscode.ExtensionMode.Development) { return; }

    const webviewDir = path.join(context.extensionPath, 'dist', 'webview');
    if (!fs.existsSync(webviewDir)) { return; }

    let timer: NodeJS.Timeout | undefined;
    const watcher = fs.watch(webviewDir, (_event, fileName) => {
        if (!fileName || !fileName.endsWith('.js')) { return; }
        // webpack writes many chunks per rebuild; reload once after they settle.
        if (timer) { clearTimeout(timer); }
        timer = setTimeout(() => {
            void vscode.commands.executeCommand('workbench.action.webview.reloadWebviewAction');
        }, RELOAD_DEBOUNCE_MS);
    });

    context.subscriptions.push({
        dispose: () => {
            if (timer) { clearTimeout(timer); }
            watcher.close();
        },
    });
}
