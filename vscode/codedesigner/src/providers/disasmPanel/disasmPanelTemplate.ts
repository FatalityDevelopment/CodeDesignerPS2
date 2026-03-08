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
    snapshots?: SnapshotEntry[];
}

export interface SnapshotEntry {
    label: string;
    timestamp: string;
    path: string;
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

<h3>Emulator</h3>
<div class="container">

    ${getEmulatorControls(data)}

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
</div>
<div class="row">
    <button id="snapshotBtn" ${data.connected ? '' : 'disabled'}>Take Snapshot</button>
</div>
${data.snapshots && data.snapshots.length > 0 ? getSnapshotList(data.snapshots) : ''}`;
}

function getSnapshotList(snapshots: SnapshotEntry[]): string {
    const items = snapshots.map(s => `
<div class="snapshot-item">
    <span class="snapshot-label">${escapeHtml(s.label)}</span>
    <span class="snapshot-time">${escapeHtml(s.timestamp)}</span>
    <button class="open-snapshot-btn" data-path="${escapeHtml(s.path)}">Open</button>
</div>`).join('');
    return `<div class="snapshot-list"><h4>Snapshots</h4>${items}</div>`;
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

document.getElementById('snapshotBtn')?.addEventListener('click', () => {
    vscode.postMessage({ command: 'snapshot' });
});

document.getElementById('emulatorSelect').onchange = (e) => {
    vscode.postMessage({ command: 'emulatorChanged', emulatorPath: e.target.value });
};

document.getElementById('biosSelect').onchange = (e) => {
    vscode.postMessage({ command: 'biosChanged', biosPath: e.target.value });
};

document.getElementById('gameSelect').onchange = (e) => {
    vscode.postMessage({ command: 'gameChanged', gamePath: e.target.value });
};

// Delegate clicks on dynamically-rendered Open buttons
document.addEventListener('click', (e) => {
    const btn = e.target.closest('.open-snapshot-btn');
    if (btn) {
        vscode.postMessage({ command: 'openSnapshot', path: btn.dataset.path });
    }
});

// Listen for messages from the extension
window.addEventListener('message', event => {
    const message = event.data;

    switch (message.command) {
        case 'connectionStatus': {
            const btn = document.getElementById('connectBtn');
            btn.textContent = message.connected ? 'Disconnect' : 'Connect';
            const snapBtn = document.getElementById('snapshotBtn');
            snapBtn.disabled = !message.connected;
            break;
        }
        case 'updateSnapshots': {
            let listEl = document.querySelector('.snapshot-list');
            if (!listEl) {
                listEl = document.createElement('div');
                listEl.className = 'snapshot-list';
                document.querySelector('.container').appendChild(listEl);
            }
            const esc = s => s.replace(/&/g,'&amp;').replace(/"/g,'&quot;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
            listEl.innerHTML = '<h4>Snapshots</h4>' +
                message.snapshots.map(s =>
                    '<div class="snapshot-item">' +
                    '<span class="snapshot-label">' + esc(s.label) + '</span>' +
                    '<span class="snapshot-time">' + esc(s.timestamp) + '</span>' +
                    '<button class="open-snapshot-btn" data-path="' + esc(s.path) + '">Open</button>' +
                    '</div>'
                ).join('');
            break;
        }
    }
});
</script>`;
}

