import * as vscode from 'vscode';
import { SocketClient } from '../../socketClient';
import { getOutputChannel } from '../../extension';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';

/**
 * Opens a VS Code WebView panel for editing a single progress report entry.
 * The panel shows a textarea for the Markdown content and a Save button.
 * On save, the content is sent back to the server via MCPMEMORY_UPDATE.
 */
export class McpProgressReportEditorProvider {
    private readonly _panels = new Map<number, vscode.WebviewPanel>();

    constructor(private readonly socketClient: SocketClient, private readonly extensionUri: vscode.Uri) {}

    /**
     * Opens (or re-focuses) the editor panel for the given progress report entry.
     */
    public open(id: number, name: string, content: string): void {
        const existing = this._panels.get(id);
        if (existing) {
            existing.reveal(vscode.ViewColumn.One);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'mcpProgressReportEditor',
            `Progress Report: ${name}`,
            vscode.ViewColumn.One,
            { enableScripts: true }
        );

        applyCodeDesignerPanelIcon(panel, this.extensionUri);
        this._panels.set(id, panel);

        panel.onDidDispose(() => {
            this._panels.delete(id);
        });

        panel.webview.onDidReceiveMessage(async (msg) => {
            if (msg.command === 'save') {
                await this._handleSave(id, name, msg.content, panel);
            }
        });

        panel.webview.html = this._getHtml(id, name, content);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async _handleSave(id: number, name: string, content: string, panel: vscode.WebviewPanel): Promise<void> {
        try {
            await this.socketClient.mcpMemoryUpdate(id, undefined, content);
            panel.webview.postMessage({ command: 'saveResult', success: true });
            vscode.window.showInformationMessage(`Progress report '${name}' saved.`);
        } catch (err: any) {
            panel.webview.postMessage({ command: 'saveResult', success: false, error: err?.message ?? String(err) });
            vscode.window.showErrorMessage(`Failed to save progress report '${name}': ${err?.message ?? err}`);
            getOutputChannel().error(`McpProgressReportEditor save error: ${err?.message ?? err}`);
        }
    }

    private _getHtml(id: number, name: string, content: string): string {
        const escapedName    = this._escapeHtml(name);
        const escapedContent = this._escapeHtml(content);
        return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Progress Report: ${escapedName}</title>
<style>
  body { margin: 0; padding: 0; display: flex; flex-direction: column; height: 100vh; font-family: var(--vscode-font-family); background: var(--vscode-editor-background); color: var(--vscode-editor-foreground); }
  #toolbar { display: flex; align-items: center; gap: 8px; padding: 6px 10px; background: var(--vscode-editorGroupHeader-tabsBackground); border-bottom: 1px solid var(--vscode-editorGroup-border); }
  #toolbar h2 { margin: 0; font-size: 13px; flex: 1; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  #save-btn { padding: 4px 12px; cursor: pointer; background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none; border-radius: 2px; font-size: 12px; }
  #save-btn:hover { background: var(--vscode-button-hoverBackground); }
  #save-btn:disabled { opacity: 0.5; cursor: not-allowed; }
  #status { font-size: 11px; color: var(--vscode-descriptionForeground); }
  #status.error { color: var(--vscode-errorForeground); }
  textarea { flex: 1; resize: none; border: none; outline: none; padding: 10px; font-family: var(--vscode-editor-font-family, monospace); font-size: var(--vscode-editor-font-size, 13px); background: var(--vscode-editor-background); color: var(--vscode-editor-foreground); }
</style>
</head>
<body>
<div id="toolbar">
  <h2>&#x1F4DD; ${escapedName}</h2>
  <span id="status"></span>
  <button id="save-btn" onclick="save()">&#x1F4BE; Save</button>
</div>
<textarea id="content" spellcheck="false">${escapedContent}</textarea>
<script>
  const vscode = acquireVsCodeApi();
  const textarea = document.getElementById('content');
  const saveBtn = document.getElementById('save-btn');
  const status = document.getElementById('status');
  let dirty = false;

  textarea.addEventListener('input', () => {
    dirty = true;
    saveBtn.disabled = false;
    status.textContent = '';
    status.className = '';
  });

  function save() {
    saveBtn.disabled = true;
    status.textContent = 'Saving…';
    status.className = '';
    vscode.postMessage({ command: 'save', id: ${id}, content: textarea.value });
  }

  window.addEventListener('message', (e) => {
    const msg = e.data;
    if (msg.command === 'saveResult') {
      if (msg.success) {
        dirty = false;
        status.textContent = 'Saved ✓';
        status.className = '';
        saveBtn.disabled = false;
      } else {
        status.textContent = 'Error: ' + (msg.error ?? 'Unknown error');
        status.className = 'error';
        saveBtn.disabled = false;
      }
    }
  });
</script>
</body>
</html>`;
    }

    private _escapeHtml(s: string): string {
        return s
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }
}
