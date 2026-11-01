import type { ReactNode } from 'react';
import { Icon } from './shared/Icon';
import { HostDialog } from './shared/HostDialog';
import { FileBrowser } from './shared/FileBrowser';
import { OutputPanel } from './shared/OutputPanel';
import { Toasts } from './shared/Toasts';
import { activePage, useSignals, type PageId } from './shared';
import { DisassemblerPage } from './pages/disassembler/DisassemblerPage';
import { EmulatorPage } from './pages/emulator/EmulatorPage';

const PAGES: Array<{ id: PageId; label: string; icon: string; render: () => ReactNode }> = [
  { id: 'emulator', label: 'Emulator', icon: 'gamepad2', render: () => <EmulatorPage /> },
  { id: 'disassembler', label: 'Disassembler', icon: 'code', render: () => <DisassemblerPage /> },
];

// The activity bar switches pages; every page stays mounted so its state survives. The Output panel sits under them all.
export function AppShell() {
  useSignals();
  const current = activePage();
  return (
    <div className="mb-shell">
      <nav className="mb-activity">
        <div className="mb-activity-logo"><img src="/codedesigner-icon.png" alt="CodeDesigner" /></div>
        {PAGES.map(p => (
          <button key={p.id} className={`mb-activity-btn${current === p.id ? ' on' : ''}`} title={p.label} onClick={() => activePage.set(p.id)}>
            <Icon name={p.icon} size={22} strokeWidth={1.5} />
          </button>
        ))}
      </nav>
      <div className="cd-main">
        <main className="mb-pages">
          {PAGES.map(p => (
            <div key={p.id} className={`mb-page${current === p.id ? ' on' : ''}`}>{p.render()}</div>
          ))}
        </main>
        <OutputPanel />
      </div>
      <HostDialog />
      <FileBrowser />
      <Toasts />
    </div>
  );
}
