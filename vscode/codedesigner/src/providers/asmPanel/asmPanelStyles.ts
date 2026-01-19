/**
 * Styles for the Assembler Panel
 */

export function getAsmPanelStyles(): string {
    return `<style>
body {
    margin: 0;
    padding: 10px;
    font-family: var(--vscode-editor-font-family);
    font-size: var(--vscode-editor-font-size);
    background: var(--vscode-editor-background);
    color: var(--vscode-editor-foreground);
}

select, textarea, button {
    font-family: var(--vscode-editor-font-family);
    font-size: var(--vscode-editor-font-size);
}

select, textarea {
    width: 100%;
    background: var(--vscode-input-background);
    color: var(--vscode-input-foreground);
    border: 1px solid var(--vscode-input-border);
    margin-bottom: 8px;
    padding: 4px;
}

textarea {
    height: 220px;
    resize: vertical;
    font-family: var(--vscode-editor-font-family);
    font-size: var(--vscode-editor-font-size);
}

button {
    background: var(--vscode-button-background);
    color: var(--vscode-button-foreground);
    border: none;
    padding: 4px 10px;
    cursor: pointer;
}

button:hover {
    background: var(--vscode-button-hoverBackground);
}

button:disabled {
    opacity: 0.5;
    cursor: default;
}

.row {
    display: flex;
    gap: 8px;
    margin-bottom: 8px;
}
</style>`;
}
