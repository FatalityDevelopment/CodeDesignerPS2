import * as vscode from 'vscode';

/**
 * Give CodeDesigner webview tabs the same compact CPU icon used by the
 * disassembler experience. Keeping this in one helper prevents standalone
 * tools from drifting back to a mix of emoji titles and icon-less tabs.
 */
export function applyCodeDesignerPanelIcon(panel: vscode.WebviewPanel, extensionUri: vscode.Uri): void {
    panel.iconPath = vscode.Uri.joinPath(extensionUri, 'dist', 'resources', 'disasm-icon.svg');
}
