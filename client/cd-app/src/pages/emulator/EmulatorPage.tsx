import { bridgeState } from '../../api/bridge';
import { PageHeader } from '../../shared/PageHeader';
import { currentGame, selectDisc } from '../../shared/gameContext';
import { activePage, host } from '../../shared/host';
import { useSignals } from '../../shared/signals';
import { disassembler as d, formatAddress } from '../disassembler/disassembler-logic';
import './emulator.css';

const STATE_TEXT = { connecting: 'Connecting…', online: 'Connected', offline: 'Not running' };

const DISC_FILTERS = [{ label: 'PS2 disc images', extensions: ['iso', 'bin', 'img'] }];

// Connection, game and run state. Starting the Bridge and emulators comes with the desktop host; until then they are
// started the usual way and this page attaches to them.
export function EmulatorPage() {
  useSignals();
  const state = bridgeState();
  const game = currentGame();
  const pc = d.pc();
  const paused = d.paused();
  const online = state === 'online';

  const chooseDisc = async () => {
    const path = await host.browse({ mode: 'open', title: 'Choose a game disc', filters: DISC_FILTERS, key: 'disc' });
    if (path) { await selectDisc(path); }
  };

  return (
    <div className="emu-page">
      <PageHeader icon="gamepad2" title="Emulator" subtitle="Connection, game and run state" />
      <div className="emu-cards">
        <section className="me-card">
          <div className="me-card-title">Bridge</div>
          <div className="cd-chip"><span className={`cd-dot ${state}`} />{STATE_TEXT[state]}</div>
          {state === 'offline' && <p className="me-note lm-muted">Start the CodeDesigner Bridge, and this page connects by itself.</p>}
        </section>

        <section className="me-card">
          <div className="me-card-title">Game</div>
          <dl className="me-kv">
            <dt>Title</dt><dd>{game?.title ?? 'None'}</dd>
            <dt>Serial</dt><dd>{game?.serial || 'Unknown'}</dd>
          </dl>
          <div className="me-actions">
            <button className="lm-tool-btn" disabled={!online} onClick={() => void chooseDisc()}>Choose disc…</button>
          </div>
        </section>

        <section className="me-card">
          <div className="me-card-title">Emulator</div>
          <dl className="me-kv">
            <dt>State</dt><dd>{pc === null ? 'Not attached' : paused ? 'Paused' : 'Running'}</dd>
            <dt>PC</dt><dd className="lm-mono">{pc === null ? '-' : formatAddress(pc)}</dd>
          </dl>
          <div className="me-actions">
            <button className="lm-tool-btn" disabled={pc === null} onClick={() => void (paused ? d.resume() : d.pause())}>{paused ? 'Resume' : 'Pause'}</button>
            <button className="lm-tool-btn" disabled={pc === null || !paused} onClick={() => void d.stepInto()}>Step</button>
            <button className="lm-tool-btn" onClick={() => activePage.set('disassembler')}>Open Disassembler</button>
          </div>
        </section>
      </div>
    </div>
  );
}
