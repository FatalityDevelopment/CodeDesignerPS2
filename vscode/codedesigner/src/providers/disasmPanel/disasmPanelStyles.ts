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

.row {
    display: flex;
    gap: 8px;
    margin-bottom: 4px;
}

.row select,
.row button {
    flex: 1;
}

select, button {
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

button:disabled {
    opacity: 0.4;
    cursor: not-allowed;
}

.snapshot-list {
    margin-top: 12px;
    border-top: 1px solid var(--vscode-input-border);
    padding-top: 8px;
}

.snapshot-list h4 {
    margin: 0 0 6px;
    font-size: 12px;
    color: var(--vscode-descriptionForeground);
    text-transform: uppercase;
    letter-spacing: 0.05em;
}

.snapshot-item {
    display: flex;
    align-items: center;
    gap: 6px;
    margin-bottom: 4px;
    font-size: 12px;
}

.snapshot-label {
    flex: 1;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.snapshot-time {
    color: var(--vscode-descriptionForeground);
    white-space: nowrap;
}

.open-snapshot-btn {
    padding: 2px 8px;
    font-size: 11px;
}
</style>`;
}
