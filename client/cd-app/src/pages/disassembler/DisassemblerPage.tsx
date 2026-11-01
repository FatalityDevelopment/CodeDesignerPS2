import { useCallback, useEffect, useState } from 'react';
import { bridgeState } from '../../api/bridge';
import { ContextMenu, type MenuAction } from '../../shared/ContextMenu';
import { Icon } from '../../shared/Icon';
import { MenuBar, type Menu } from '../../shared/MenuBar';
import { PageHeader } from '../../shared/PageHeader';
import { Tabs } from '../../shared/Tabs';
import { activePage, host, modalOpen } from '../../shared/host';
import { useSignals, useValue } from '../../shared/signals';
import { disassembler as d, formatAddress, parseAddress, type DisasmRow } from './disassembler-logic';
import './disassembler.css';

type SideTab = 'registers' | 'labels' | 'breakpoints';

const SIDE_TABS: Array<{ id: SideTab; label: string }> = [
  { id: 'registers', label: 'Registers' },
  { id: 'labels', label: 'Labels' },
  { id: 'breakpoints', label: 'Breakpoints' },
];

const GPR_NAMES = ['zero', 'at', 'v0', 'v1', 'a0', 'a1', 'a2', 'a3', 't0', 't1', 't2', 't3', 't4', 't5', 't6', 't7',
  's0', 's1', 's2', 's3', 's4', 's5', 's6', 's7', 't8', 't9', 'k0', 'k1', 'gp', 'sp', 'fp', 'ra'];

export function DisassemblerPage() {
  const online = useValue(() => bridgeState() === 'online');
  const paused = useValue(d.paused);
  const pc = useValue(d.pc);
  const error = useValue(d.error);
  const [addressText, setAddressText] = useState('00100000');
  const [tab, setTab] = useState<SideTab>('registers');
  const [menu, setMenu] = useState<{ x: number; y: number; actions: MenuAction[] } | null>(null);
  const closeMenu = useCallback(() => setMenu(null), []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (activePage() !== 'disassembler' || modalOpen() || (e.target as HTMLElement).tagName === 'INPUT') { return; }
      if (e.key === 'F5') { e.preventDefault(); void (paused ? d.resume() : d.pause()); }
      else if (e.key === 'F10') { e.preventDefault(); void d.stepOver(); }
      else if (e.key === 'F11' && e.shiftKey) { e.preventDefault(); void d.stepOut(); }
      else if (e.key === 'F11') { e.preventDefault(); void d.stepInto(); }
      else if (e.key === 'F9' && d.selected() !== null) { e.preventDefault(); void d.toggleBreakpoint(d.selected()!); }
      else if (e.key === 'PageDown') { e.preventDefault(); void d.scroll(32); }
      else if (e.key === 'PageUp') { e.preventDefault(); void d.scroll(-32); }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [paused]);

  const goto = () => {
    const address = parseAddress(addressText);
    if (address === null) { host.notify('Enter a hex address, for example 00100000', 'warning'); return; }
    void d.goto(address);
  };

  const rowMenu = (row: DisasmRow, x: number, y: number) => setMenu({
    x, y, actions: [
      { label: d.breakpoints().has(row.address) ? 'Clear breakpoint' : 'Set breakpoint', run: () => void d.toggleBreakpoint(row.address) },
      { label: 'Add label…', run: () => void d.addLabel(row.address) },
      { separator: true },
      { label: 'Copy address', run: () => void navigator.clipboard.writeText(formatAddress(row.address)) },
      { label: 'Copy instruction', run: () => void navigator.clipboard.writeText(row.assembly) },
    ],
  });

  const menus: Menu[] = [
    { id: 'debug', label: 'Debug', items: [
      { label: paused ? 'Resume' : 'Pause', shortcut: 'F5', action: () => void (paused ? d.resume() : d.pause()), disabled: () => !online },
      { label: 'Step over', shortcut: 'F10', action: () => void d.stepOver(), disabled: () => !online || !paused },
      { label: 'Step into', shortcut: 'F11', action: () => void d.stepInto(), disabled: () => !online || !paused },
      { label: 'Step out', shortcut: 'Shift+F11', action: () => void d.stepOut(), disabled: () => !online || !paused },
      { separator: true },
      { label: 'Clear all breakpoints', action: () => void d.clearBreakpoints(), disabled: () => !online },
    ] },
    { id: 'go', label: 'Go', items: [
      { label: 'Go to PC', action: () => { if (pc !== null) { void d.goto(pc); } }, disabled: () => pc === null },
      { label: 'Refresh', action: () => void d.refresh(), disabled: () => !online },
    ] },
  ];

  return (
    <div className="me-app has-header">
      <PageHeader icon="code" title="Disassembler" subtitle="Read, step and label EE code in the running game" />
      <MenuBar menus={menus}><GameChip /></MenuBar>
      <div className="me-header">
        <label className="me-toggle">Address
          <input className="me-input dis-address" value={addressText} spellCheck={false}
            onChange={e => setAddressText(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') { goto(); } }} />
        </label>
        <button className="lm-tool-btn" onClick={goto} disabled={!online}>Go</button>
        <button className="lm-tool-btn" onClick={() => { if (pc !== null) { void d.goto(pc); } }} disabled={pc === null}>Go to PC</button>
        <span className="me-spacer" />
        <button className="lm-tool-btn" onClick={() => void (paused ? d.resume() : d.pause())} disabled={!online}>
          <Icon name={paused ? 'play' : 'pause'} size={12} /> {paused ? 'Resume' : 'Pause'}
        </button>
        <button className="lm-tool-btn" onClick={() => void d.stepOver()} disabled={!online || !paused}>Step over</button>
        <button className="lm-tool-btn" onClick={() => void d.stepInto()} disabled={!online || !paused}>Step into</button>
        <button className="lm-tool-btn" onClick={() => void d.stepOut()} disabled={!online || !paused}>Step out</button>
      </div>
      <div className="dis-body">
        <Listing onMenu={rowMenu} />
        <aside className="dis-side">
          <Tabs tabs={SIDE_TABS} active={tab} onChange={setTab} />
          <div className="dis-side-body">
            {tab === 'registers' && <Registers />}
            {tab === 'labels' && <Labels />}
            {tab === 'breakpoints' && <Breakpoints />}
          </div>
        </aside>
      </div>
      <div className={`me-statusbar${error || !online ? ' err' : ''}`}>
        <span>{!online ? 'Bridge not connected' : error ?? (paused ? 'Paused' : 'Running')}</span>
        {pc !== null && <span className="lm-muted">PC {formatAddress(pc)}</span>}
      </div>
      {menu && <ContextMenu {...menu} onClose={closeMenu} />}
    </div>
  );
}

function GameChip() {
  useSignals();
  return <span className="cd-chip">{d.gameLabel()}</span>;
}

function Listing({ onMenu }: { onMenu: (row: DisasmRow, x: number, y: number) => void }) {
  const rows = useValue(d.rows);
  const pc = useValue(d.pc);
  const selected = useValue(d.selected);
  const breakpoints = useValue(d.breakpoints);
  const online = useValue(() => bridgeState() === 'online');
  return (
    <div className="dis-list" onWheel={e => void d.scroll(e.deltaY > 0 ? 8 : -8)}>
      <div className="dis-row dis-head"><span /><span>Address</span><span>Bytes</span><span>Instruction</span><span>Comment</span></div>
      {rows.length === 0 && <div className="me-empty">{online ? 'No code loaded at this address.' : 'Waiting for the Bridge.'}</div>}
      {rows.map(r => (
        <div key={r.address}>
          {r.label && <div className="dis-label">{r.label}:</div>}
          <div
            className={`dis-row${r.address === pc ? ' pc' : ''}${r.address === selected ? ' sel' : ''}`}
            onClick={() => d.selected.set(r.address)}
            onContextMenu={e => { e.preventDefault(); onMenu(r, e.clientX, e.clientY); }}
          >
            <button className={`dis-bp${breakpoints.has(r.address) ? ' on' : ''}`} title="Toggle breakpoint (F9)"
              onClick={e => { e.stopPropagation(); void d.toggleBreakpoint(r.address); }} />
            <span className="dis-addr">{formatAddress(r.address)}</span>
            <span className="dis-hex">{r.hex}</span>
            <span className="dis-asm">{r.assembly}</span>
            <span className="dis-comment">{r.comment}</span>
          </div>
        </div>
      ))}
    </div>
  );
}

function Registers() {
  const regs = useValue(d.registers);
  if (!regs) { return <div className="me-empty">Pause the emulator to read registers.</div>; }
  const word = (hi: number, lo: number) => (hi >>> 0).toString(16).padStart(8, '0') + (lo >>> 0).toString(16).padStart(8, '0');
  return (
    <div className="dis-regs">
      {GPR_NAMES.map((name, i) => (
        <div key={name} className="dis-reg">
          <span>{name}</span>
          <button className="lm-row-btn lm-mono" title="Go to this address" onClick={() => void d.goto(regs.gprLo32[i])}>
            {word(regs.gprHi32[i] ?? 0, regs.gprLo32[i] ?? regs.gpr[i] ?? 0)}
          </button>
        </div>
      ))}
      <div className="dis-reg"><span>pc</span><span className="lm-mono">{formatAddress(regs.pc)}</span></div>
      <div className="dis-reg"><span>hi</span><span className="lm-mono">{formatAddress(regs.hi)}</span></div>
      <div className="dis-reg"><span>lo</span><span className="lm-mono">{formatAddress(regs.lo)}</span></div>
    </div>
  );
}

function Labels() {
  const labels = useValue(d.labels);
  const [filter, setFilter] = useState('');
  const needle = filter.trim().toLowerCase();
  const shown = needle ? labels.filter(l => l.label.toLowerCase().includes(needle) || l.address.toLowerCase().includes(needle)) : labels;
  const addressOf = (text: string) => parseAddress(text.replace(/^0x/i, ''));
  return (
    <>
      <input className="me-search dis-filter" placeholder="Filter labels" value={filter} onChange={e => setFilter(e.target.value)} />
      {shown.length === 0 && <div className="me-empty">No labels.</div>}
      {shown.slice(0, 500).map(l => (
        <div key={l.address} className="dis-side-row" title={l.comment}>
          <button className="lm-row-btn lm-goto-btn" onClick={() => { const a = addressOf(l.address); if (a !== null) { void d.goto(a); } }}>{l.address}</button>
          <span className="dis-side-name">{l.label}</span>
          <button className="lm-row-btn lm-danger-btn" title="Delete label" onClick={() => { const a = addressOf(l.address); if (a !== null) { void d.deleteLabel(a); } }}>
            <Icon name="x" size={12} />
          </button>
        </div>
      ))}
      {shown.length > 500 && <div className="me-empty">{shown.length - 500} more. Filter to narrow the list.</div>}
    </>
  );
}

function Breakpoints() {
  const breakpoints = useValue(d.breakpoints);
  if (breakpoints.size === 0) { return <div className="me-empty">No breakpoints set.</div>; }
  return (
    <>
      {[...breakpoints].sort((a, b) => a - b).map(a => (
        <div key={a} className="dis-side-row">
          <button className="lm-row-btn lm-goto-btn" onClick={() => void d.goto(a)}>{formatAddress(a)}</button>
          <span className="dis-side-name" />
          <button className="lm-row-btn lm-danger-btn" title="Clear breakpoint" onClick={() => void d.toggleBreakpoint(a)}><Icon name="x" size={12} /></button>
        </div>
      ))}
    </>
  );
}
