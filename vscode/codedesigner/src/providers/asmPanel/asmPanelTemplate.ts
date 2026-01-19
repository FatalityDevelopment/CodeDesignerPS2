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
    return `<textarea id="codeArea"
    placeholder="Paste CDS source here (optional)...">${code}</textarea>`;
}

/**
 * Button section (compile/decompile)
 */
function getButtonSection(): string {
    return `<div class="row">
    <button id="compileBtn">Compile</button>
    <button id="decompileBtn">Decompile</button>
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
    const code = document.getElementById('codeArea').value;
    vscode.postMessage({ command: 'send', code });
};

document.getElementById('compileBtn').onclick = () => {
    vscode.postMessage({ command: 'compile' });
};

document.getElementById('decompileBtn').onclick = () => {
    vscode.postMessage({ command: 'decompile' });
};

document.getElementById('emulatorSelect').onchange = (e) => {
    vscode.postMessage({ command: 'emulatorChanged', emulator: e.target.value });
};

window.addEventListener('message', event => {
    const message = event.data;
    if (message.command === 'setCode') {
        document.getElementById('codeArea').value = message.code;
    }
});
</script>`;
}
