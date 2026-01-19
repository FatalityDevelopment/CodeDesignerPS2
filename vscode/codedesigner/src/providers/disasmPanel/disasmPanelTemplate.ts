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

<h3>MIPS Disassembler</h3>
<div class="container">
    <label for="hexInput">Hex Code (one per line or space-separated):</label>
    <textarea id="hexInput" rows="10" placeholder="0x02114020&#10;0x21090064&#10;0x11090010"></textarea>
    
    <button id="disassembleBtn">Disassemble</button>
    
    <label for="asmOutput">Assembly Output:</label>
    <textarea id="asmOutput" rows="10" readonly></textarea>
    
    <div id="status" class="status"></div>
</div>

${getScript(nonce)}

</body>
</html>`;
}

function getScript(nonce: string): string {
    return `<script nonce="${nonce}">
const vscode = acquireVsCodeApi();

document.getElementById('disassembleBtn').onclick = () => {
    const hexInput = document.getElementById('hexInput').value;
    const statusDiv = document.getElementById('status');
    
    statusDiv.textContent = 'Disassembling...';
    statusDiv.className = 'status';
    
    vscode.postMessage({ 
        command: 'disassemble',
        data: hexInput
    });
};

// Listen for messages from the extension
window.addEventListener('message', event => {
    const message = event.data;
    
    switch (message.command) {
        case 'disassembleResult':
            const asmOutput = document.getElementById('asmOutput');
            const statusDiv = document.getElementById('status');
            
            if (message.success) {
                asmOutput.value = message.data;
                statusDiv.textContent = 'Disassembly complete!';
                statusDiv.className = 'status success';
            } else {
                statusDiv.textContent = 'Error: ' + message.error;
                statusDiv.className = 'status error';
            }
            break;
    }
});
</script>`;
}
