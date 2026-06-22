import { BridgeMessageBus } from '../shared/host-bridge';
import './styles.css';

type DisasmRow = { address: number; opcode: string; label?: string; mnemonic: string; operands: string; comment?: string };

const rows: DisasmRow[] = [
  { address: 0x00100348, opcode: '24050001', mnemonic: 'li', operands: 'a1, $00000001', comment: '(__00000001)' },
  { address: 0x0010034c, opcode: '7fbf0000', mnemonic: 'sq', operands: 's0, $0000(sp)' },
  { address: 0x00100350, opcode: '0009002a', mnemonic: 'daddu', operands: 's2, zero, zero' },
  { address: 0x00100354, opcode: '0c046eee', mnemonic: 'jal', operands: '$0011bba8', comment: 'FNC_0011bba8' },
  { address: 0x00100358, opcode: '0000882d', mnemonic: 'daddu', operands: 's1, zero, zero' },
  { address: 0x0010035c, opcode: '0040802d', mnemonic: 'daddu', operands: 's0, v0, zero' },
  { address: 0x00100360, opcode: '05030009', mnemonic: 'bgezl', operands: 's0, $00100388', comment: '(+10)' },
  { address: 0x00100364, opcode: '0200202d', mnemonic: 'daddu', operands: 'a0, s0, zero' },
  { address: 0x00100368, opcode: '5e200011', label: '__00100368', mnemonic: 'bgtzl', operands: 's1, $001003b0', comment: '(+18)' },
  { address: 0x0010036c, opcode: '0220302d', mnemonic: 'daddu', operands: 'a2, s1, zero' },
  { address: 0x00100370, opcode: '05030014', label: '__00100370', mnemonic: 'bgezl', operands: 's0, $001003c4', comment: '(+21)' },
  { address: 0x00100374, opcode: '0200202d', mnemonic: 'daddu', operands: 'a0, s0, zero' },
  { address: 0x00100378, opcode: '5e200016', label: '__00100378', mnemonic: 'bgtzl', operands: 's1, $001003d4', comment: '(+22)' },
  { address: 0x0010037c, opcode: '0220202d', mnemonic: 'daddu', operands: 'a0, s1, zero' },
  { address: 0x00100380, opcode: '10000028', label: '__00100380', mnemonic: 'beq', operands: 'zero, zero, $00100424', comment: '(+41)' },
  { address: 0x00100384, opcode: '0240102d', mnemonic: 'daddu', operands: 'v0, s2, zero' }
];

const bridge = new BridgeMessageBus();

function hex(value: number, width = 8): string { return value.toString(16).padStart(width, '0'); }

function renderHexPane(): string {
  const bytes = Array.from({ length: 96 }, (_, i) => (i * 13 + 0x2d) & 0xff);
  const lines = [0x00100340, 0x00100350, 0x00100360, 0x00100370].map((addr, line) => {
    const slice = bytes.slice(line * 16, line * 16 + 16);
    const ascii = slice.map(b => (b >= 32 && b < 127 ? String.fromCharCode(b) : '.')).join('');
    return `<div><span class="addr">${hex(addr)}</span> ${slice.map(b => hex(b, 2)).join(' ')} <span class="ascii">${ascii}</span></div>`;
  }).join('');
  return `<div class="hex-header">GreetzBz &nbsp; +0 +1 +2 +3 +4 +5 +6 +7 +8 +9 +A +B +C +D +E +F &nbsp;&nbsp; 0123456789ABCDEF</div>${lines}`;
}

function renderRows(): string {
  return rows.map((row, index) => `
    <div class="row ${index === 2 ? 'selected' : ''}" data-address="${row.address}">
      <span class="addr">${hex(row.address)}</span>
      <span class="opcode">${row.opcode}</span>
      <span class="label">${row.label ?? ''}</span>
      <span class="mnemonic">${row.mnemonic}</span>
      <span class="operands">${row.operands}</span>
      <span class="comment">${row.comment ?? ''}</span>
    </div>`).join('');
}

function render(): void {
  document.querySelector<HTMLDivElement>('#app')!.innerHTML = `
    <main class="window-shell">
      <section class="titlebar"><span class="app-icon">↔</span> ps2dis <span class="window-buttons">▁ □ ×</span></section>
      <nav class="menu">File&nbsp;&nbsp;&nbsp;Edit&nbsp;&nbsp;&nbsp;Preference&nbsp;&nbsp;&nbsp;View&nbsp;&nbsp;&nbsp;Analyzer&nbsp;&nbsp;&nbsp;Help</nav>
      <section class="toolbar"><button>□</button><button>📂</button><button>💾</button><button>?</button></section>
      <section class="hex-pane">${renderHexPane()}</section>
      <section class="disasm-pane">${renderRows()}</section>
      <footer class="status">daddu s2, zero, zero <span>000) --------:--------:--------</span></footer>
    </main>`;

  document.querySelectorAll<HTMLElement>('.row').forEach(row => row.addEventListener('click', () => {
    document.querySelector('.row.selected')?.classList.remove('selected');
    row.classList.add('selected');
    bridge.postMessage({ type: 'gotoAddress', address: Number(row.dataset.address) });
  }));
}

bridge.on('init', (message) => console.debug('init from host', message));
bridge.postMessage({ type: 'ready', surface: 'ps2dis-electron' });
render();
