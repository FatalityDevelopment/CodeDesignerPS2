/**
 * Template for the Disassembly Panel
 */

import { getDisasmPanelStyles } from './disasmPanelStyles';

export interface EmuOption {
    name?: string;
    type?: string;
    path: string;
}

export interface DisasmPanelData {
    connected?: boolean;
    emulators?: EmuOption[];
    biosOptions?: EmuOption[];
    gameOptions?: EmuOption[];
    selectedEmulator?: string;
    selectedBios?: string;
    selectedGame?: string;
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

    ${getEmulatorControls(data)}

    <hr class="section-divider">

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

function getEmulatorControls(data: DisasmPanelData): string {
    const emulators = data.emulators || [];
    const biosOptions = data.biosOptions || [];
    const gameOptions = data.gameOptions || [];
    const selEmu = data.selectedEmulator || '';
    const selBios = data.selectedBios || '';
    const selGame = data.selectedGame || '';

    const emuOptions = emulators.map(e => {
        const label = e.type ?? e.name ?? '';
        const val = e.path;
        return `<option value="${escapeHtml(val)}" ${selEmu === val ? 'selected' : ''}>${escapeHtml(label)}</option>`;
    }).join('');

    const biosOptHtml = biosOptions.map(b => {
        const label = b.name ?? b.type ?? '';
        const val = b.path;
        return `<option value="${escapeHtml(val)}" ${selBios === val ? 'selected' : ''}>${escapeHtml(label)}</option>`;
    }).join('');

    const gameOptHtml = gameOptions.map(g => {
        const label = g.name ?? g.type ?? '';
        const val = g.path;
        return `<option value="${escapeHtml(val)}" ${selGame === val ? 'selected' : ''}>${escapeHtml(label)}</option>`;
    }).join('');

    return `<div class="row">
    <select id="emulatorSelect">
        <option value="">Select Emulator...</option>
        ${emuOptions}
    </select>
</div>
<div class="row">
    <select id="biosSelect">
        <option value="">Select BIOS...</option>
        ${biosOptHtml}
    </select>
</div>
<div class="row">
    <select id="gameSelect">
        <option value="">Select Game...</option>
        ${gameOptHtml}
    </select>
</div>
<div class="row">
    <button id="connectBtn">${data.connected ? 'Disconnect' : 'Connect'}</button>
</div>`;
}

function escapeHtml(str: string): string {
    return str
        .replace(/&/g, '&amp;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
}

function getScript(nonce: string): string {
    return `<script nonce="${nonce}">
const vscode = acquireVsCodeApi();

document.getElementById('connectBtn').onclick = () => {
    const emulatorPath = document.getElementById('emulatorSelect').value;
    const biosPath = document.getElementById('biosSelect').value;
    const gamePath = document.getElementById('gameSelect').value;
    vscode.postMessage({ command: 'connect', emulatorPath, biosPath, gamePath });
};

document.getElementById('emulatorSelect').onchange = (e) => {
    vscode.postMessage({ command: 'emulatorChanged', emulatorPath: e.target.value });
};

document.getElementById('biosSelect').onchange = (e) => {
    vscode.postMessage({ command: 'biosChanged', biosPath: e.target.value });
};

document.getElementById('gameSelect').onchange = (e) => {
    vscode.postMessage({ command: 'gameChanged', gamePath: e.target.value });
};

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

        case 'connectionStatus':
            const btn = document.getElementById('connectBtn');
            btn.textContent = message.connected ? 'Disconnect' : 'Connect';
            break;
    }
});
</script>`;
}
