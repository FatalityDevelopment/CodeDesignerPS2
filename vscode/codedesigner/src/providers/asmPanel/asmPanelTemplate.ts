/**
 * Template for the Assembler Panel
 * Separated from component logic for better organization (Angular-style)
 */

import { getAsmPanelStyles } from './asmPanelStyles';

export interface AsmPanelData {
    code?: string;
    emulators?: string[];
    selectedEmulator?: string;
    isConnected?: boolean;
}

/**
 * Main panel template - returns complete HTML
 */
export function getAsmPanelHtml(data: AsmPanelData, nonce: string): string {
    return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1.0">

${getAsmPanelStyles()}

</head>
<body>

${getControlsSection(data)}
${getTextAreaSection(data)}
${getButtonSection()}

${getScript(nonce)}

</body>
</html>`;
}

/**
 * Controls section (emulator dropdown + connect button)
 */
function getControlsSection(data: AsmPanelData): string {
    const emulators = data.emulators || ['Project64', 'Mupen64Plus', 'RetroArch'];
    const selected = data.selectedEmulator || '';
    
    return `<div class="row">
    <select id="emulatorSelect">
        <option value="">Select Emulator...</option>
        ${emulators.map(emu => 
            `<option value="${emu}" ${selected === emu ? 'selected' : ''}>${emu}</option>`
        ).join('')}
    </select>
    <button id="connectBtn">Connect</button>
    <button id="sendBtn" disabled>Send</button>
</div>`;
}

/**
 * Text area section
 */
function getTextAreaSection(data: AsmPanelData): string {
    const code = data.code || '';
    return `<div class="textarea-container">
    <label for="asmInput">Assembly Code:</label>
    <textarea id="asmInput"
        placeholder="add $t0, $s0, $s1&#10;addi $t1, $t0, 100&#10;j 0x400000">${code}</textarea>
    
    <label for="hexOutput">Hex Output:</label>
    <textarea id="hexOutput" readonly
        placeholder="Assembled hex will appear here..."></textarea>
        
    <div id="status" class="status"></div>
</div>`;
}

/**
 * Button section (compile/decompile)
 */
function getButtonSection(): string {
    return `<div class="row">
    <button id="assembleBtn">Assemble</button>
</div>`;
}

/**
 * Script section (event handlers)
 */
function getScript(nonce: string): string {
    return `<script nonce="${nonce}">
const vscode = acquireVsCodeApi();

document.getElementById('connectBtn').onclick = () => {
    vscode.postMessage({ command: 'connect' });
};

document.getElementById('sendBtn').onclick = () => {
    const code = document.getElementById('asmInput').value;
    vscode.postMessage({ command: 'send', code });
};

document.getElementById('assembleBtn').onclick = () => {
    const asmInput = document.getElementById('asmInput').value;
    const statusDiv = document.getElementById('status');
    
    statusDiv.textContent = 'Assembling...';
    statusDiv.className = 'status';
    
    vscode.postMessage({ 
        command: 'assemble',
        data: asmInput
    });
};

document.getElementById('emulatorSelect').onchange = (e) => {
    vscode.postMessage({ command: 'emulatorChanged', emulator: e.target.value });
};

window.addEventListener('message', event => {
    const message = event.data;
    
    switch (message.command) {
        case 'setCode':
            document.getElementById('asmInput').value = message.code;
            break;
            
        case 'assembleResult':
            const hexOutput = document.getElementById('hexOutput');
            const statusDiv = document.getElementById('status');
            
            if (message.success) {
                hexOutput.value = message.data;
                statusDiv.textContent = 'Assembly complete!';
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
