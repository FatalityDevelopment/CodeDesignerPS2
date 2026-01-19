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
    height: 150px;
    resize: vertical;
    font-family: var(--vscode-editor-font-family);
    font-size: var(--vscode-editor-font-size);
}

label {
    display: block;
    font-weight: bold;
    margin-top: 10px;
    margin-bottom: 4px;
}

.textarea-container {
    display: flex;
    flex-direction: column;
}

button {
    background: var(--vscode-button-background);
    color: var(--vscode-button-foreground);
    border: none;
    padding: 6px 12px;
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
