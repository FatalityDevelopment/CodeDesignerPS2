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
import '../shared/styles/shared.css';
import './cheat-list-page.css';

// Mirrors CheatListItem / CompiledList in src/providers/cheatList/cheatListService.ts,
// GameCheatListInfo / CheatEngineInfo in socketClient.ts.
interface CheatItem { id: string; name: string; enabled: boolean; source: string; notes?: string; author?: string; section?: string; }
interface CheatListInfo { id: number; title: string; cheatCount: number; forDisc: boolean; }
interface CompiledCheat { id: string; name: string; codes: string[]; errors: Array<{ line: number; message: string }>; }
interface CompiledList { cheats: CompiledCheat[]; text: string; codeCount: number; errorCount: number; }
interface EngineInfo { injected: boolean; engineBase: number; hookAddress?: number; entries: number; imageSize: number; warnings: string[]; }

type Busy = '' | 'compile' | 'send' | 'saveBinary' | 'saveToList' | 'importRepository';

const DEFAULT_BASE = 0x000C0000;
const SAVE_DEBOUNCE_MS = 400;
const CODE_LINE = /^\s*[0-9a-f]{8}\s+[0-9a-f]{8}/i;

/** Same identity as cheatKey() in cheatListService.ts. */
function cheatKey(item: { name: string; section?: string }): string {
  return `${item.section || 'Others'}\u0001${item.name}`;
}

@Component({
  selector: 'app-cheat-list-page',
  standalone: true,
  imports: [IconComponent, PageHeaderComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="cl-outer">
  <app-page-header icon="sparkles" title="Cheat List"
                   [subtitle]="game() ? game() + ' — compile, send and save cheats' : 'Compile, send and save cheats'">
    <div page-header-actions class="cl-tools">
      <button class="cl-tool" title="Cheat Scanner" (click)="post({ type: 'openTool', tool: 'scanner' })"><app-icon name="search" [size]="13" /><span>Scanner</span></button>
      <button class="cl-tool" title="Cheat Converter" (click)="post({ type: 'openTool', tool: 'converter' })"><app-icon name="repeat" [size]="13" /><span>Converter</span></button>
      <button class="cl-tool" title="Cheat Porter" (click)="post({ type: 'openTool', tool: 'porter' })"><app-icon name="gitcompare" [size]="13" /><span>Porter</span></button>
      <button class="cl-tool" title="Cheat archive editor — edit the game's cheat lists" (click)="post({ type: 'openTool', tool: 'editor' })"><app-icon name="database" [size]="13" /><span>Archive editor</span></button>
    </div>
  </app-page-header>

  <!-- ── Command bar ─────────────────────────────────────────────────────── -->
  <div class="cl-bar">
    <div class="cl-group">
      <button class="lm-tool-btn" [disabled]="listId() === null" (click)="addCheat()"><app-icon name="plus" [size]="12" /> New cheat</button>
      <button class="lm-tool-btn" [class.cl-on]="importOpen()" [disabled]="listId() === null" (click)="importOpen.set(!importOpen())"><app-icon name="download" [size]="12" /> Paste</button>
      <button class="lm-tool-btn lm-danger-btn" [disabled]="!selected()" title="Delete the selected cheat" (click)="deleteSelected()"><app-icon name="trash2" [size]="12" /></button>
    </div>
    <span class="cl-sep"></span>
    <div class="cl-group">
      <button class="lm-tool-btn" [disabled]="!connected() || !!busy()" (click)="run('compile')">
        <app-icon name="zap" [size]="12" /> {{ busy() === 'compile' ? 'Compiling…' : 'Compile' }}
      </button>
      <div class="cl-split">
        <button class="cl-primary" [disabled]="!connected() || !!busy()" [title]="mode() === 'engine' ? 'Hook the cheat engine so the codes hold every frame' : 'Write the codes to memory once'" (click)="run('send')">
          <app-icon name="play" [size]="12" /> {{ busy() === 'send' ? 'Sending…' : (mode() === 'engine' ? 'Send to engine' : 'Send once') }}
        </button>
        <select class="cl-split-select" title="How to send" [value]="mode()" (change)="setMode(asSelect($event).value)">
          <option value="once" [selected]="mode() === 'once'">Once (PCSX2 write)</option>
          <option value="engine" [selected]="mode() === 'engine'">Cheat engine (every frame)</option>
        </select>
      </div>
      <div class="cl-split">
        <button class="lm-tool-btn" [disabled]="!!busy() || (binKind() === 'engine' && !connected())" (click)="run('saveBinary')">
          <app-icon name="save" [size]="12" /> {{ busy() === 'saveBinary' ? 'Saving…' : 'Save .bin' }}
        </button>
        <select class="cl-split-select" title="Binary format" [value]="binKind()" (change)="binKind.set(asSelect($event).value === 'engine' ? 'engine' : 'raw')">
          <option value="raw" [selected]="binKind() === 'raw'">Raw code pairs</option>
          <option value="engine" [selected]="binKind() === 'engine'">Cheat engine image</option>
        </select>
      </div>
    </div>
    <span class="cl-spacer"></span>
    @if (!connected()) { <span class="cl-warn">Not connected to the bridge</span> }
  </div>

  @if (importOpen()) {
    <div class="cl-import">
      <textarea class="cl-code" spellcheck="false" placeholder="#Infinite Health&#10;20123456 000003E8&#10;&#10;Max Ammo&#10;20789ABC 000001F4"
                [value]="importText()" (input)="importText.set(asTextarea($event).value)"></textarea>
      <div class="cl-import-actions">
        <span class="cl-muted">One cheat per <b>#Name</b>, <b>[Name]</b> or name line above its codes.</span>
        <button class="lm-tool-btn" (click)="importOpen.set(false)">Cancel</button>
        <button class="cl-primary" [disabled]="!importText().trim()" (click)="importPasted()">Add</button>
      </div>
    </div>
  }

  @if (statusMsg()) {
    <div class="cl-status" [class.cl-status-error]="statusIsError()">{{ statusMsg() }}</div>
  }

  <div class="cl-body">
    <!-- ── Cheat list (a cheat archive list) ───────────────────────────────── -->
    <section class="cl-card cl-list-card">
      <div class="cl-card-head cl-list-picker">
        <select class="cl-input cl-list-select" title="Cheat list from the cheat archive editor"
                [disabled]="dirty() || !!busy() || lists().length === 0"
                (change)="selectList(asSelect($event).value)">
          @if (lists().length === 0) { <option value="">No cheat lists</option> }
          @for (l of userLists(); track l.id) {
            <option [value]="l.id" [selected]="l.id === listId()">{{ l.title }} ({{ l.cheatCount }})</option>
          }
          @if (importedLists().length > 0) {
            <optgroup label="Imported from the repository">
              @for (l of importedLists(); track l.id) {
                <option [value]="l.id" [selected]="l.id === listId()">{{ l.title }} ({{ l.cheatCount }})</option>
              }
            </optgroup>
          }
        </select>
        <button class="cl-icon-btn" [disabled]="dirty() || !!busy() || gameId() === null" title="New cheat list for this game" (click)="post({ type: 'newList', gameId: gameId() })">
          <app-icon name="plus" [size]="13" />
        </button>
        <button class="cl-icon-btn" [disabled]="dirty() || !!busy() || !connected()" title="Import this game's cheats from the cheat repository text files into cheat lists"
                (click)="post({ type: 'importRepository' })">
          <app-icon name="download" [size]="13" />
        </button>
      </div>
      <div class="cl-card-head cl-list-tools">
        <label class="cl-check" title="Tick / untick all">
          <input type="checkbox" [checked]="allEnabled()" [indeterminate]="someEnabled()" (change)="setAll(asInput($event).checked)" />
        </label>
        <span class="cl-count">{{ enabledCount() }} / {{ items().length }} on</span>
        <input class="cl-input cl-filter" type="text" placeholder="Filter…" [value]="filter()" (input)="filter.set(asInput($event).value)" />
      </div>
      @if (dirty()) {
        <div class="cl-dirty">
          <span>Unsaved changes</span>
          <button class="lm-tool-btn" [disabled]="!!busy()" (click)="discard()">Discard</button>
          <button class="cl-primary" [disabled]="!!busy() || !connected()" (click)="saveToList()">
            <app-icon name="save" [size]="12" /> {{ busy() === 'saveToList' ? 'Saving…' : 'Save to list' }}
          </button>
        </div>
      }
      <div class="cl-list">
        @if (loadError()) {
          <div class="cl-empty cl-empty-error">{{ loadError() }}</div>
        } @else if (lists().length === 0) {
          <div class="cl-empty">
            <div class="cl-steps">
              <div>This game has no cheat lists yet.</div>
              <div><b>+</b> Create one, or <b>&#8595;</b> import the game's cheats from the cheat repository.</div>
            </div>
          </div>
        } @else if (items().length === 0) {
          <div class="cl-empty">This list is empty — add cheats with <em>New cheat</em> or <em>Paste</em>.</div>
        }
        @for (item of visibleItems(); track item.id) {
          <div class="cl-row" [class.cl-row-on]="item.enabled" [class.cl-row-selected]="item.id === selectedId()" (click)="selectedId.set(item.id)">
            <input type="checkbox" [checked]="item.enabled" (click)="$event.stopPropagation()" (change)="toggle(item, asInput($event).checked)" />
            <span class="cl-row-name" [title]="(item.section ? item.section + ' › ' : '') + item.name">{{ item.name || 'Untitled' }}</span>
            <span class="cl-row-meta">{{ lineCount(item) }}</span>
            <span class="cl-dot" [class.cl-dot-ok]="state(item) === 'ok'" [class.cl-dot-err]="state(item) === 'error'" [title]="stateTitle(item)"></span>
          </div>
        }
      </div>
    </section>

    <!-- ── Editor ──────────────────────────────────────────────────────────── -->
    <section class="cl-card cl-editor-card">
      @if (!selected()) {
        <div class="cl-empty">Select a cheat to edit it.</div>
      } @else {
        <div class="cl-card-head">
          <input class="cl-input cl-name" type="text" spellcheck="false" placeholder="Cheat name"
                 [value]="selected()!.name" (input)="patchSelected({ name: asInput($event).value })" />
          <input class="cl-input cl-section" type="text" spellcheck="false" placeholder="Section" title="Section (folder) in the cheat list"
                 [value]="selected()!.section ?? ''" (input)="patchSelected({ section: asInput($event).value })" />
          <label class="cl-check"><input type="checkbox" [checked]="selected()!.enabled" (change)="toggle(selected()!, asInput($event).checked)" /> On</label>
        </div>
        <textarea class="cl-code cl-source" spellcheck="false"
                  placeholder="// Raw codes and CodeDesigner source can be mixed:&#10;20123456 000003E8&#10;freeze half $00347A52 = 03E7&#10;address $00200000&#10;  addiu v0, zero, 1"
                  [value]="selected()!.source" (input)="patchSelected({ source: asTextarea($event).value })"></textarea>
        <input class="cl-input cl-notes" type="text" placeholder="Description" [value]="selected()!.notes ?? ''" (input)="patchSelected({ notes: asInput($event).value })" />

        <div class="cl-output">
          <div class="cl-card-head">
            <span class="cl-section-title">Compiled</span>
            @if (selectedCompiled(); as c) {
              <span class="cl-count">{{ c.codes.length }} code{{ c.codes.length === 1 ? '' : 's' }}</span>
              <span class="cl-head-actions">
                <button class="lm-tool-btn" [disabled]="c.codes.length === 0" (click)="copy(c.codes.join('\\n'), 'Copied ' + c.codes.length + ' code(s).')"><app-icon name="copy" [size]="12" /> Copy</button>
              </span>
            } @else {
              <span class="cl-count">{{ selected()!.enabled ? 'not compiled yet' : 'tick the cheat to compile it' }}</span>
            }
          </div>
          @if (selectedCompiled(); as c) {
            @if (c.errors.length > 0) {
              <ul class="cl-errors">
                @for (e of c.errors; track $index) { <li><b>line {{ e.line }}</b> {{ e.message }}</li> }
              </ul>
            } @else {
              <pre class="cl-codes">{{ c.codes.join('\\n') }}</pre>
            }
          }
        </div>
      }
    </section>
  </div>

  <!-- ── Cheat engine ────────────────────────────────────────────────────── -->
  <div class="cl-engine">
    <span class="cl-pill" [class.cl-pill-on]="engine()?.injected">
      <span class="cl-pill-dot"></span>{{ engine()?.injected ? 'Engine hooked' : 'Engine off' }}
    </span>
    @if (engine()?.injected) {
      <span class="cl-muted">{{ hex8(engine()!.hookAddress ?? 0) }} → {{ hex8(engine()!.engineBase) }} · {{ engine()!.entries }} row{{ engine()!.entries === 1 ? '' : 's' }}</span>
    }
    <span class="cl-spacer"></span>
    <label class="cl-field" title="Address of an instruction pair that runs every frame (no branches, or jr ra + delay slot)">
      <span class="cl-field-label">Hook</span>
      <input class="cl-input cl-mono cl-addr" type="text" spellcheck="false" placeholder="e.g. 0012A4F0"
             [value]="hookText()" (input)="hookText.set(asInput($event).value)" (change)="saveSettings(0)" />
    </label>
    <label class="cl-field" title="Unused EE RAM for the engine (code + data + table)">
      <span class="cl-field-label">Base</span>
      <input class="cl-input cl-mono cl-addr" type="text" spellcheck="false"
             [value]="baseText()" (input)="baseText.set(asInput($event).value)" (change)="saveSettings(0)" />
    </label>
    <button class="lm-tool-btn lm-danger-btn" [disabled]="!connected() || !engine()?.injected || !!busy()" title="Restore the hooked instructions" (click)="post({ type: 'removeEngine' })">Unhook</button>
  </div>
</div>
  `,
})
export class CheatListPageComponent implements AfterViewInit {
  private readonly vscode = getVsCodeApi();
  private readonly zone = inject(NgZone);
  private _saveTimer: ReturnType<typeof setTimeout> | undefined;
  /** The list as loaded, for Discard. */
  private _loadedItems: CheatItem[] = [];

  readonly game = signal('');
  readonly connected = signal(true);
  readonly lists = signal<CheatListInfo[]>([]);
  readonly listId = signal<number | null>(null);
  readonly gameId = signal<number | null>(null);
  readonly loadError = signal('');
  readonly items = signal<CheatItem[]>([]);
  readonly dirty = signal(false);
  readonly selectedId = signal<string | null>(null);
  readonly filter = signal('');
  readonly compiled = signal<Record<string, CompiledCheat>>({});
  readonly engine = signal<EngineInfo | null>(null);
  readonly busy = signal<Busy>('');
  readonly mode = signal<'once' | 'engine'>('once');
  readonly binKind = signal<'raw' | 'engine'>('raw');
  readonly hookText = signal('');
  readonly baseText = signal(DEFAULT_BASE.toString(16).toUpperCase().padStart(8, '0'));
  readonly importOpen = signal(false);
  readonly importText = signal('');
  readonly statusMsg = signal('');
  readonly statusIsError = signal(false);

  readonly userLists = computed(() => this.lists().filter(l => !l.forDisc));
  readonly importedLists = computed(() => this.lists().filter(l => l.forDisc));
  readonly selected = computed(() => this.items().find(i => i.id === this.selectedId()) ?? null);
  readonly selectedCompiled = computed(() => {
    const s = this.selected();
    return s ? this.compiled()[s.id] ?? null : null;
  });
  readonly visibleItems = computed(() => {
    const q = this.filter().trim().toLowerCase();
    return q
      ? this.items().filter(i => i.name.toLowerCase().includes(q) || (i.section ?? '').toLowerCase().includes(q) || i.source.toLowerCase().includes(q))
      : this.items();
  });
  readonly enabledCount = computed(() => this.items().filter(i => i.enabled).length);
  readonly allEnabled = computed(() => this.items().length > 0 && this.enabledCount() === this.items().length);
  readonly someEnabled = computed(() => this.enabledCount() > 0 && !this.allEnabled());

  constructor() {
    const st = (this.vscode.getState() ?? {}) as { mode?: 'once' | 'engine'; binKind?: 'raw' | 'engine' };
    if (st.mode) { this.mode.set(st.mode); }
    if (st.binKind) { this.binKind.set(st.binKind); }
  }

  ngAfterViewInit(): void {
    window.addEventListener('message', (event: MessageEvent) => {
      const data = event.data as { type: string; [key: string]: unknown };
      this.zone.run(() => this._handleMessage(data));
    });
    this.post({ type: 'ready' });
  }

  // ── Actions ──────────────────────────────────────────────────────────────

  post(msg: Record<string, unknown>): void { this.vscode.postMessage(msg); }

  setMode(v: string): void {
    this.mode.set(v === 'engine' ? 'engine' : 'once');
    this.vscode.setState({ mode: this.mode(), binKind: this.binKind() });
  }

  selectList(value: string): void {
    const id = Number(value);
    if (!Number.isFinite(id) || id === this.listId()) { return; }
    this.post({ type: 'selectList', listId: id });
  }

  addCheat(): void {
    const item: CheatItem = { id: newId(), name: 'New cheat', enabled: true, source: '' };
    this.items.set([...this.items(), item]);
    this.selectedId.set(item.id);
    this.dirty.set(true);
  }

  deleteSelected(): void {
    const id = this.selectedId();
    if (!id) { return; }
    const list = this.items();
    const index = list.findIndex(i => i.id === id);
    const next = list.filter(i => i.id !== id);
    this.items.set(next);
    this.selectedId.set(next[Math.min(index, next.length - 1)]?.id ?? null);
    this.dirty.set(true);
  }

  /** Ticking is page-only state (not part of the list), so it saves straight away. */
  toggle(item: CheatItem, enabled: boolean): void {
    this.items.set(this.items().map(i => i.id === item.id ? { ...i, enabled } : i));
    this.saveSettings();
  }

  setAll(enabled: boolean): void {
    this.items.set(this.items().map(i => ({ ...i, enabled })));
    this.saveSettings();
  }

  patchSelected(patch: Partial<CheatItem>): void {
    const id = this.selectedId();
    this.items.set(this.items().map(i => i.id === id ? { ...i, ...patch } : i));
    this.dirty.set(true);
    if (patch.source !== undefined && id) {
      // Edited source invalidates its compile result.
      const { [id]: _stale, ...rest } = this.compiled();
      this.compiled.set(rest);
    }
  }

  discard(): void {
    this.items.set(this._loadedItems);
    this.dirty.set(false);
    this.compiled.set({});
    if (!this.selected()) { this.selectedId.set(this.items()[0]?.id ?? null); }
  }

  saveToList(): void {
    const listId = this.listId();
    if (listId === null || this.busy()) { return; }
    this.post({ type: 'saveToList', listId, items: this.items() });
  }

  importPasted(): void {
    this.post({ type: 'importText', text: this.importText() });
    this.importText.set('');
    this.importOpen.set(false);
  }

  run(action: 'compile' | 'send' | 'saveBinary'): void {
    if (this.busy()) { return; }
    if (action === 'send' && this.mode() === 'engine' && this._hook() === undefined) {
      this._status('Enter the hook address (an instruction pair that runs every frame) for the cheat engine.', true);
      return;
    }
    this.vscode.setState({ mode: this.mode(), binKind: this.binKind() });
    this.post({
      type: action,
      items: this.items(),
      hookAddress: this._hook(),
      engineBase: parseHex(this.baseText()) ?? DEFAULT_BASE,
      mode: this.mode(),
      kind: this.binKind(),
    });
  }

  copy(text: string, summary: string): void { this.post({ type: 'copy', text, summary }); }

  /** Save ticks and engine fields (debounced). */
  saveSettings(delay = SAVE_DEBOUNCE_MS): void {
    if (this._saveTimer) { clearTimeout(this._saveTimer); }
    const listId = this.listId();
    this._saveTimer = setTimeout(() => {
      if (listId !== this.listId()) { return; }
      this.post({
        type: 'saveSettings',
        listId,
        enabled: this.items().filter(i => i.enabled).map(cheatKey),
        hookAddress: this._hook(),
        engineBase: parseHex(this.baseText()) ?? DEFAULT_BASE,
      });
    }, delay);
  }

  // ── Template helpers ─────────────────────────────────────────────────────

  asInput(e: Event):    HTMLInputElement    { return e.target as HTMLInputElement; }
  asSelect(e: Event):   HTMLSelectElement   { return e.target as HTMLSelectElement; }
  asTextarea(e: Event): HTMLTextAreaElement { return e.target as HTMLTextAreaElement; }

  hex8(n: number): string { return '0x' + (n >>> 0).toString(16).toUpperCase().padStart(8, '0'); }

  lineCount(item: CheatItem): string {
    const lines = item.source.split(/\r?\n/).filter(l => l.trim() && !/^\s*(\/\/|#|;)/.test(l));
    const codes = lines.filter(l => CODE_LINE.test(l)).length;
    if (lines.length === 0) { return 'empty'; }
    return codes === lines.length ? `${codes} code${codes === 1 ? '' : 's'}` : 'CDS';
  }

  state(item: CheatItem): 'ok' | 'error' | 'none' {
    const c = this.compiled()[item.id];
    if (!c) { return 'none'; }
    return c.errors.length > 0 ? 'error' : 'ok';
  }

  stateTitle(item: CheatItem): string {
    const c = this.compiled()[item.id];
    if (!c) { return item.enabled ? 'Not compiled yet' : 'Off'; }
    return c.errors.length > 0 ? `${c.errors.length} error(s)` : `${c.codes.length} code(s)`;
  }

  // ── Internals ────────────────────────────────────────────────────────────

  private _hook(): number | undefined { return parseHex(this.hookText()); }

  private _status(message: string, isError: boolean): void {
    this.statusMsg.set(message);
    this.statusIsError.set(isError);
  }

  private _handleMessage(data: { type: string; [key: string]: unknown }): void {
    switch (data['type']) {
      case 'state': {
        const listChanged = this.listId() !== (data['listId'] ?? null);
        this.game.set((data['game'] as string) ?? '');
        this.connected.set(!!data['connected']);
        this.lists.set((data['lists'] as CheatListInfo[]) ?? []);
        this.gameId.set((data['gameId'] as number | null) ?? null);
        this.loadError.set((data['error'] as string) ?? '');
        if (this.dirty() && !listChanged) {
          // A background refresh (e.g. the sidebar) must not drop unsaved edits.
          break;
        }
        if (this.dirty()) {
          this._status('The cheat list was switched elsewhere — unsaved edits were discarded.', true);
        }
        this.listId.set((data['listId'] as number | null) ?? null);
        this.items.set((data['items'] as CheatItem[]) ?? []);
        this._loadedItems = this.items();
        this.dirty.set(false);
        const hook = data['hookAddress'] as number | undefined;
        const base = data['engineBase'] as number | undefined;
        this.hookText.set(hook !== undefined ? (hook >>> 0).toString(16).toUpperCase().padStart(8, '0') : '');
        this.baseText.set(((base ?? DEFAULT_BASE) >>> 0).toString(16).toUpperCase().padStart(8, '0'));
        this.compiled.set({});
        if (listChanged || !this.selected()) { this.selectedId.set(this.items()[0]?.id ?? null); }
        break;
      }

      case 'compiled': {
        const list = data['compiled'] as CompiledList;
        const map: Record<string, CompiledCheat> = {};
        for (const c of list.cheats) { map[c.id] = c; }
        this.compiled.set(map);
        const firstError = list.cheats.find(c => c.errors.length > 0);
        if (firstError) { this.selectedId.set(firstError.id); }
        break;
      }

      case 'importItems': {
        const added = (data['items'] as CheatItem[]) ?? [];
        if (added.length === 0) { break; }
        this.items.set([...this.items(), ...added]);
        this.selectedId.set(added[0].id);
        this.dirty.set(true);
        break;
      }

      case 'engine':
        this.engine.set((data['engine'] as EngineInfo | null) ?? null);
        break;

      case 'busy':
        this.busy.set(data['busy'] ? ((data['action'] as Busy) ?? 'compile') : '');
        break;

      case 'status':
        this._status(data['message'] as string ?? '', !!data['isError']);
        break;
    }
  }
}

function parseHex(text: string): number | undefined {
  const t = text.trim().replace(/^0x/i, '').replace(/^\$/, '');
  if (!/^[0-9a-f]{1,8}$/i.test(t)) { return undefined; }
  return parseInt(t, 16) >>> 0;
}

function newId(): string {
  return Math.random().toString(36).slice(2, 10) + Date.now().toString(36);
}
