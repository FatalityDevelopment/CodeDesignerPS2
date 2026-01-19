/**
 * Styles for the Disassembly Panel
 */

export function getDisasmPanelStyles(): string {
    return `<style>
body {
    padding: 10px;
    font-family: var(--vscode-editor-font-family);
    color: var(--vscode-editor-foreground);
    background: var(--vscode-editor-background);
}

button {
    background: var(--vscode-button-background);
    color: var(--vscode-button-foreground);
    border: none;
    padding: 6px 14px;
    cursor: pointer;
}

button:hover {
    background: var(--vscode-button-hoverBackground);
}
</style>`;
}
