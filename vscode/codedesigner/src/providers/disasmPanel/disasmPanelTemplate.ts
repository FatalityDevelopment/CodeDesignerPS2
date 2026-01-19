/**
 * Template for the Disassembly Panel
 */

import { getDisasmPanelStyles } from './disasmPanelStyles';

export interface DisasmPanelData {
    connected?: boolean;
}

export function getDisasmPanelHtml(data: DisasmPanelData, nonce: string): string {
    return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               script-src 'nonce-${nonce}';">

${getDisasmPanelStyles()}

</head>
<body>

<h3>Disasm Panel Loaded</h3>
<button id="btn">Connect</button>

${getScript(nonce)}

</body>
</html>`;
}

function getScript(nonce: string): string {
    return `<script nonce="${nonce}">
const vscode = acquireVsCodeApi();
document.getElementById('btn').onclick = () => {
    vscode.postMessage({ command: 'connect' });
};
</script>`;
}
