import {
  Component,
  AfterViewInit,
  ViewEncapsulation,
  signal,
  computed,
  NgZone,
  inject,
} from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import { IconComponent } from '../shared/icon.component';
import { PageHeaderComponent } from '../shared/page-header.component';
import { SnapshotEntry } from '../disasm-panel/types';
import '../shared/styles/shared.css';
import './cheat-porter-page.css';

/** Select value standing for the emulator's live EE RAM. */
const LIVE = '__live__';
const CODE_LINE = /^\s*[0-9a-f]{8}\s+[0-9a-f]{8}/i;

type Side = 'source' | 'target';
type PortSource =
  | { kind: 'live' }
  | { kind: 'dump'; path: string; label: string };

/** One code line as returned by PORTCHEATS (see PortCheatResult in socketClient.ts). */
interface PortLine {
  blockIndex:    number;
  lineIndex:     number;
  blockName:     string;
  blockText:     string | null;
  originalCode:  string;
  newCode:       string | null;
  found:         boolean;
  method:        string;
  confidence:    number;
  ambiguous:     boolean;
  candidates:    string[] | null;
  sourceAddress: number | null;
  newAddress:    number | null;
  note:          string | null;
}

type Status = 'ok' | 'review' | 'missing';
type Filter = 'all' | Status;

interface LineView extends PortLine {
  key:     string;
  status:  Status;
  /** Code chosen for the output: the bridge's best match or a picked alternative. */
  chosen:  string | null;
  options: string[];
}

interface BlockView {
  index:  number;
  name:   string;
  text:   string | null;
  lines:  LineView[];
  status: Status;
}

interface SavedState { source?: string; target?: string; codes?: string }

@Component({
  selector: 'app-cheat-porter-page',
  standalone: true,
  imports: [IconComponent, PageHeaderComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="cp-outer">
  <app-page-header icon="repeat" title="Cheat Porter" subtitle="Move cheat codes from one game version's EE RAM to another's" />

  <!-- ── Dump selection ──────────────────────────────────────────────────── -->
  <div class="cp-source-bar">
    <div class="cp-side">
      <span class="cp-field-label" title="The dump the codes were written for">From</span>
      <select class="cp-select" [value]="source()" (change)="source.set(asSelect($event).value); saveState()">
        <option value="">— dump the codes work on —</option>
        <option [value]="LIVE" [disabled]="!connected()" [selected]="source() === LIVE">Live EE RAM{{ connected() ? '' : ' (not connected)' }}</option>
        @for (s of sourceOptions(); track s.path) {
          <option [value]="s.path" [selected]="source() === s.path">{{ optionLabel(s) }}</option>
        }
      </select>
      <button class="lm-tool-btn" title="Browse for a .bin/.p2s dump" (click)="browse('source')">Browse…</button>
    </div>

    <button class="cp-icon-btn" title="Swap From and To" (click)="swap()">&#x21C4;</button>

    <div class="cp-side">
      <span class="cp-field-label" title="The dump to port the codes to">To</span>
      <select class="cp-select" [value]="target()" (change)="target.set(asSelect($event).value); saveState()">
        <option value="">— dump to port to —</option>
        <option [value]="LIVE" [disabled]="!connected()" [selected]="target() === LIVE">Live EE RAM{{ connected() ? '' : ' (not connected)' }}</option>
        @for (s of sourceOptions(); track s.path) {
          <option [value]="s.path" [selected]="target() === s.path">{{ optionLabel(s) }}</option>
        }
      </select>
      <button class="lm-tool-btn" title="Browse for a .bin/.p2s dump" (click)="browse('target')">Browse…</button>
    </div>

    <button class="cp-primary-btn" [disabled]="!canPort() || busy()" [title]="portBlocker()" (click)="port()">
      @if (busy()) { Porting… } @else { Port codes }
    </button>
    <button class="cp-icon-btn" title="Reload save-state list" (click)="post({ type: 'refreshSnapshots' })">
      <app-icon name="refreshcw" [size]="13" />
    </button>
  </div>

  @if (busy()) {
    <div class="cp-progress"><div class="cp-progress-inner"></div><span>{{ progressMsg() }}</span></div>
  }
  @if (statusMsg()) {
    <div class="cp-status" [class.cp-status-error]="statusIsError()">{{ statusMsg() }}</div>
  }

  <div class="cp-body">
    <!-- ── Input ──────────────────────────────────────────────────────────── -->
    <section class="cp-card cp-input-card">
      <div class="cp-card-head">
        <span class="cp-section-title">Codes</span>
        <span class="cp-count">{{ inputStats() }}</span>
        <span class="cp-head-actions">
          <button class="lm-tool-btn" [disabled]="!connected() || busy()"
                  title="Load the current game's cheats from the cheat archive"
                  (click)="loadGameCheats()">Load game cheats</button>
          <button class="lm-tool-btn" [disabled]="!codes()" (click)="setCodes('')">Clear</button>
        </span>
      </div>
      <textarea class="cp-textarea" spellcheck="false"
                placeholder="Infinite Health&#10;20123456 000003E8&#10;&#10;Max Ammo&#10;20789ABC 000001F4"
                [value]="codes()"
                (input)="setCodes(asTextarea($event).value)"></textarea>
      <div class="cp-hint">Raw GameShark / CodeBreaker codes. A text line names each cheat.</div>
    </section>

    <!-- ── Results ────────────────────────────────────────────────────────── -->
    <section class="cp-card cp-results-card">
      @if (blocks().length === 0) {
        <div class="cp-empty">
          @if (results()) {
            No code lines were found in the input.
          } @else {
            <div class="cp-steps">
              <div><b>1</b> Pick the dump the codes work on (From) and the dump of the other version (To).</div>
              <div><b>2</b> Paste codes, or load the current game's cheats.</div>
              <div><b>3</b> Port, review the matches, then copy the output.</div>
            </div>
          }
        </div>
      } @else {
        <div class="cp-card-head">
          <span class="cp-section-title">Results</span>
          <div class="cp-seg">
            <button [class.cp-seg-on]="filter() === 'all'" (click)="filter.set('all')">All {{ blocks().length }}</button>
            <button [class.cp-seg-on]="filter() === 'ok'" (click)="filter.set('ok')">Ported {{ counts().ok }}</button>
            <button [class.cp-seg-on]="filter() === 'review'" (click)="filter.set('review')"
                    title="Several candidates or a low-confidence match">Review {{ counts().review }}</button>
            <button [class.cp-seg-on]="filter() === 'missing'" (click)="filter.set('missing')">Not found {{ counts().missing }}</button>
          </div>
          <input class="cp-input cp-filter" spellcheck="false" placeholder="Filter cheats…"
                 [value]="filterText()" (input)="filterText.set(asInput($event).value)" />
        </div>

        <div class="cp-block-list">
          @for (b of visibleBlocks(); track b.index) {
            <div class="cp-block">
              <label class="cp-block-head" [title]="b.status === 'missing' ? 'Some lines were not found — this cheat cannot be ported as-is' : ''">
                <input type="checkbox" [checked]="isSelected(b)" [disabled]="b.status === 'missing'"
                       (change)="toggleBlock(b.index, asInput($event).checked)" />
                <span class="cp-block-name">{{ b.name || '(unnamed cheat)' }}</span>
                <span class="cp-badge" [class]="'cp-badge cp-badge-' + b.status">{{ statusLabel(b.status) }}</span>
              </label>
              @if (b.text) { <div class="cp-block-text">{{ b.text }}</div> }
              <table class="cp-lines">
                @for (l of b.lines; track l.key) {
                  <tr [class]="'cp-line-' + l.status">
                    <td class="cp-code cp-old">{{ l.originalCode }}</td>
                    <td class="cp-arrow">&#x2192;</td>
                    <td class="cp-code">
                      @if (!l.found) {
                        <span class="cp-miss">not found</span>
                      } @else if (l.options.length > 1) {
                        <select class="cp-select cp-cand" title="Several matches — pick the right one"
                                (change)="choose(l.key, asSelect($event).value)">
                          @for (o of l.options; track o) {
                            <option [value]="o" [selected]="o === l.chosen">{{ o }}</option>
                          }
                        </select>
                      } @else {
                        <span class="cp-new" [class.cp-same]="l.chosen === l.originalCode">{{ l.chosen }}</span>
                      }
                    </td>
                    <td class="cp-meta" [title]="l.note ?? ''">
                      @if (l.method === 'Unchanged') {
                        <span class="lm-muted">{{ l.note }}</span>
                      } @else {
                        @if (l.found) { <span class="cp-conf">{{ l.confidence }}%</span> }
                        <span class="cp-method">{{ l.method }}</span>
                      }
                    </td>
                  </tr>
                }
              </table>
            </div>
          }
          @if (visibleBlocks().length === 0) {
            <div class="cp-empty">No cheats match this filter.</div>
          }
        </div>

        <!-- ── Output ──────────────────────────────────────────────────── -->
        <div class="cp-output">
          <div class="cp-card-head">
            <label class="cp-select-all">
              <input type="checkbox" [checked]="allSelected()" [disabled]="selectable().length === 0"
                     (change)="selectAll(asInput($event).checked)" />
              <span class="cp-section-title">Output</span>
            </label>
            <span class="cp-count">{{ selectedBlocks().length }} of {{ blocks().length }} cheats</span>
            <span class="cp-head-actions">
              <button class="lm-tool-btn" [disabled]="!output()" (click)="copyOutput()">
                <app-icon name="copy" [size]="12" /> Copy
              </button>
              <button class="lm-tool-btn" [disabled]="!output()" (click)="post({ type: 'openInEditor', text: output() })">Open in editor</button>
            </span>
          </div>
          <textarea class="cp-textarea cp-output-text" readonly spellcheck="false"
                    placeholder="Select ported cheats to build the output."
                    [value]="output()"></textarea>
        </div>
      }
    </section>
  </div>
</div>
`,
})
export class CheatPorterPageComponent implements AfterViewInit {
  private readonly vscode = getVsCodeApi();
  private readonly zone   = inject(NgZone);

  readonly LIVE = LIVE;

  // ── Inputs ────────────────────────────────────────────────────────────────
  readonly snapshots  = signal<SnapshotEntry[]>([]);
  readonly browsed    = signal<SnapshotEntry[]>([]);
  readonly connected  = signal(false);
  readonly source     = signal('');
  readonly target     = signal('');
  readonly codes      = signal('');

  // ── Run state ─────────────────────────────────────────────────────────────
  readonly busy          = signal(false);
  readonly progressMsg   = signal('');
  readonly statusMsg     = signal('');
  readonly statusIsError = signal(false);

  // ── Results ───────────────────────────────────────────────────────────────
  readonly results    = signal<PortLine[] | null>(null);
  readonly selected   = signal<Record<number, boolean>>({});
  readonly choices    = signal<Record<string, string>>({});
  readonly filter     = signal<Filter>('all');
  readonly filterText = signal('');

  readonly sourceOptions = computed(() => {
    const seen = new Set<string>();
    return [...this.snapshots(), ...this.browsed()].filter(s => !seen.has(s.path) && !!seen.add(s.path));
  });

  readonly portBlocker = computed(() => {
    if (!this.connected())                                  { return 'Connect to the bridge to port codes'; }
    if (!this.source() || !this.target())                   { return 'Pick both dumps'; }
    if (this.source() === LIVE && this.target() === LIVE)   { return 'Pick a dump for at least one side'; }
    if (this.source() === this.target())                    { return 'From and To are the same dump'; }
    if (!this.codes().split(/\r?\n/).some(l => CODE_LINE.test(l))) { return 'Enter codes to port'; }
    return '';
  });
  readonly canPort = computed(() => this.portBlocker() === '');

  readonly inputStats = computed(() => {
    const lines = this.codes().split(/\r?\n/);
    const codeLines = lines.filter(l => CODE_LINE.test(l)).length;
    if (codeLines === 0) { return ''; }
    // A name line starts a cheat when it follows a code line, a blank line or the top.
    let names = 0, atBoundary = true;
    for (const l of lines) {
      if (!l.trim()) { atBoundary = true; continue; }
      const isCode = CODE_LINE.test(l);
      if (!isCode && atBoundary) { names++; }
      atBoundary = isCode;
    }
    const cheats = Math.max(names, 1);
    return `${cheats} cheat${cheats === 1 ? '' : 's'}, ${codeLines} line${codeLines === 1 ? '' : 's'}`;
  });

  readonly blocks = computed((): BlockView[] => {
    const choices = this.choices();
    const byBlock = new Map<number, BlockView>();
    for (const r of this.results() ?? []) {
      let b = byBlock.get(r.blockIndex);
      if (!b) {
        b = { index: r.blockIndex, name: r.blockName, text: r.blockText, lines: [], status: 'ok' };
        byBlock.set(r.blockIndex, b);
      }
      const key     = `${r.blockIndex}:${r.lineIndex}`;
      const status: Status = !r.found ? 'missing' : (r.ambiguous || r.confidence < 100) ? 'review' : 'ok';
      const options = r.found && r.candidates?.length
        ? [...new Set([r.newCode ?? '', ...r.candidates].filter(Boolean))]
        : [];
      b.lines.push({ ...r, key, status, chosen: r.found ? (choices[key] ?? r.newCode) : null, options });
    }
    for (const b of byBlock.values()) {
      b.status = b.lines.some(l => l.status === 'missing') ? 'missing'
               : b.lines.some(l => l.status === 'review')  ? 'review' : 'ok';
    }
    return [...byBlock.values()];
  });

  readonly counts = computed(() => {
    const c = { ok: 0, review: 0, missing: 0 };
    for (const b of this.blocks()) { c[b.status]++; }
    return c;
  });

  readonly visibleBlocks = computed(() => {
    const f = this.filter();
    const q = this.filterText().trim().toLowerCase();
    return this.blocks().filter(b =>
      (f === 'all' || b.status === f) &&
      (!q || b.name.toLowerCase().includes(q) || (b.text ?? '').toLowerCase().includes(q) ||
             b.lines.some(l => l.originalCode.toLowerCase().includes(q) || (l.chosen ?? '').toLowerCase().includes(q))));
  });

  readonly selectable     = computed(() => this.blocks().filter(b => b.status !== 'missing'));
  readonly selectedBlocks = computed(() => {
    const sel = this.selected();
    return this.selectable().filter(b => sel[b.index]);
  });
  readonly allSelected = computed(() =>
    this.selectable().length > 0 && this.selectedBlocks().length === this.selectable().length);

  /** Ported cheat list in input order: name, description, then each line's chosen code. */
  readonly output = computed(() =>
    this.selectedBlocks().map(b => [
      ...(b.name ? [b.name] : []),
      ...(b.text ? [b.text] : []),
      ...b.lines.map(l => l.chosen ?? l.originalCode),
    ].join('\n')).join('\n\n'));

  constructor() {
    const st = (this.vscode.getState() ?? {}) as SavedState;
    if (st.source) { this.source.set(st.source); }
    if (st.target) { this.target.set(st.target); }
    if (st.codes)  { this.codes.set(st.codes); }
  }

  ngAfterViewInit(): void {
    window.addEventListener('message', (event: MessageEvent) => {
      const data = event.data as { type: string; [key: string]: unknown };
      this.zone.run(() => { this._handleMessage(data); });
    });
    this.post({ type: 'ready' });
  }

  // ── Actions ──────────────────────────────────────────────────────────────

  post(msg: Record<string, unknown>): void { this.vscode.postMessage(msg); }

  browse(side: Side): void { this.post({ type: 'browse', side }); }

  swap(): void {
    const s = this.source();
    this.source.set(this.target());
    this.target.set(s);
    this.saveState();
  }

  setCodes(text: string): void {
    this.codes.set(text);
    this.saveState();
  }

  loadGameCheats(): void {
    this._status('Loading the current game\'s cheats…', false);
    this.post({ type: 'loadGameCheats' });
  }

  port(): void {
    if (!this.canPort() || this.busy()) { return; }
    this.busy.set(true);
    this.progressMsg.set('Preparing…');
    this._status('', false);
    this.post({
      type:   'port',
      codes:  this.codes(),
      source: this.toSource(this.source()),
      target: this.toSource(this.target()),
    });
  }

  isSelected(b: BlockView): boolean { return !!this.selected()[b.index]; }

  toggleBlock(index: number, on: boolean): void {
    this.selected.set({ ...this.selected(), [index]: on });
  }

  selectAll(on: boolean): void {
    const next: Record<number, boolean> = {};
    for (const b of this.selectable()) { next[b.index] = on; }
    this.selected.set(next);
  }

  choose(key: string, code: string): void {
    this.choices.set({ ...this.choices(), [key]: code });
  }

  copyOutput(): void {
    const n = this.selectedBlocks().length;
    this.post({ type: 'copy', text: this.output(), summary: `Copied ${n} ported cheat${n === 1 ? '' : 's'}.` });
  }

  saveState(): void {
    this.vscode.setState({ source: this.source(), target: this.target(), codes: this.codes() } satisfies SavedState);
  }

  // ── Template helpers ─────────────────────────────────────────────────────

  asInput(e: Event):    HTMLInputElement    { return e.target as HTMLInputElement; }
  asSelect(e: Event):   HTMLSelectElement   { return e.target as HTMLSelectElement; }
  asTextarea(e: Event): HTMLTextAreaElement { return e.target as HTMLTextAreaElement; }

  optionLabel(s: SnapshotEntry): string {
    return s.timestamp ? `${s.label} — ${s.timestamp}` : s.label;
  }

  statusLabel(s: Status): string {
    return s === 'ok' ? 'Ported' : s === 'review' ? 'Review' : 'Not found';
  }

  // ── Internals ────────────────────────────────────────────────────────────

  private toSource(value: string): PortSource {
    if (value === LIVE) { return { kind: 'live' }; }
    const s = this.sourceOptions().find(o => o.path === value);
    return { kind: 'dump', path: value, label: s?.label ?? value.split(/[\\/]/).pop() ?? value };
  }

  private _status(message: string, isError: boolean): void {
    this.statusMsg.set(message);
    this.statusIsError.set(isError);
  }

  private _handleMessage(data: { type: string; [key: string]: unknown }): void {
    switch (data['type']) {
      case 'snapshotList': {
        this.snapshots.set((data['snapshots'] as SnapshotEntry[]) ?? []);
        this.connected.set(!!data['connected']);
        if (data['error']) { this._status(`Save-state list: ${data['error']}`, true); }
        break;
      }

      case 'browseResult': {
        const snap = data['snapshot'] as SnapshotEntry | undefined;
        if (!snap) { break; }
        if (!this.sourceOptions().some(s => s.path === snap.path)) {
          this.browsed.set([...this.browsed(), snap]);
        }
        if (data['side'] === 'source') { this.source.set(snap.path); } else { this.target.set(snap.path); }
        this.saveState();
        break;
      }

      case 'gameCheats': {
        const count = data['count'] as number;
        this.setCodes(data['text'] as string ?? '');
        this._status(`Loaded ${count} cheat${count === 1 ? '' : 's'} for the current game.`, false);
        break;
      }

      case 'progress':
        this.progressMsg.set(data['message'] as string ?? '');
        break;

      case 'portResult': {
        const results = (data['results'] as PortLine[]) ?? [];
        this.busy.set(false);
        this.results.set(results);
        this.choices.set({});
        this.filter.set('all');
        // Fully ported and reviewable cheats start selected; partial ones cannot be.
        const sel: Record<number, boolean> = {};
        for (const b of this.selectable()) { sel[b.index] = true; }
        this.selected.set(sel);
        const c = this.counts();
        this._status(
          `${c.ok} ported, ${c.review} to review, ${c.missing} not found (${results.length} line${results.length === 1 ? '' : 's'}).`,
          c.ok + c.review === 0 && results.length > 0);
        break;
      }

      case 'portError':
        this.busy.set(false);
        this._status(data['message'] as string ?? 'Port failed.', true);
        break;

      case 'status':
        this._status(data['message'] as string ?? '', !!data['isError']);
        break;
    }
  }
}
