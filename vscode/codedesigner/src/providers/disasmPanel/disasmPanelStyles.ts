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

.container {
    display: flex;
    flex-direction: column;
    gap: 10px;
}

label {
    font-weight: bold;
    margin-top: 10px;
}

.row {
    display: flex;
    gap: 8px;
    margin-bottom: 4px;
}

.row select,
.row button {
    flex: 1;
}

select, textarea, button {
    font-family: var(--vscode-editor-font-family);
    font-size: var(--vscode-editor-font-size);
}

select {
    width: 100%;
    background: var(--vscode-input-background);
    color: var(--vscode-input-foreground);
    border: 1px solid var(--vscode-input-border);
    padding: 4px;
}

textarea {
    width: 100%;
    background: var(--vscode-input-background);
    color: var(--vscode-input-foreground);
    border: 1px solid var(--vscode-input-border);
    padding: 8px;
    font-family: var(--vscode-editor-font-family);
    font-size: var(--vscode-editor-font-size);
    resize: vertical;
}

button {
    background: var(--vscode-button-background);
    color: var(--vscode-button-foreground);
    border: none;
    padding: 8px 16px;
    cursor: pointer;
    font-size: 14px;
}

button:hover {
    background: var(--vscode-button-hoverBackground);
}

.section-divider {
    border: none;
    border-top: 1px solid var(--vscode-input-border);
    margin: 8px 0;
}

.status {
    padding: 8px;
    margin-top: 10px;
    border-radius: 4px;
    font-size: 12px;
}

.status.success {
    background: var(--vscode-testing-iconPassed);
    color: white;
}

.status.error {
    background: var(--vscode-testing-iconFailed);
    color: white;
}
</style>`;
}
