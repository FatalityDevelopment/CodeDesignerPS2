import {
  Component,
  AfterViewInit,
  OnDestroy,
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
import './cheat-scanner-page.css';

// Mirrors ScanValueType / ScanMode / ScanSummary / ScanRow in
// src/providers/cheatScanner/valueScanEngine.ts (webview code cannot import it).
type ValueType = 'u8' | 'u16' | 'u32' | 'f32' | 'string' | 'bytes';
type ScanMode =
  | 'exact' | 'not' | 'greater' | 'less' | 'between' | 'unknown'
  | 'changed' | 'unchanged' | 'increased' | 'decreased' | 'increasedBy' | 'decreasedBy';
type RegionTag = 'ee' | 'iop';
type StructKind = 'strings' | 'functions' | 'pointers' | 'floats' | 'integers';
type Tab = 'value' | 'struct';

interface ScanSummary {
  count:     number;
  scanCount: number;
  valueType: ValueType;
  region:    RegionTag;
  signed:    boolean;
  size:      number;
  canUndo:   boolean;
  message?:  string;
}

interface ScanRow { address: number; previous: string; raw: string; }
interface StructRow { address: string; value: string; detail?: string; }
interface LabelEntry {
  address: string; name: string; comment?: string; type?: string; valueType?: string; dynamic?: boolean;
}

interface RegionPreset { id: string; label: string; tag: RegionTag; start: number; end: number; }

const REGIONS: RegionPreset[] = [
  { id: 'ee',     label: 'EE RAM (32 MB)',               tag: 'ee',  start: 0x00000000, end: 0x02000000 },
  { id: 'eeuser', label: 'EE RAM — skip kernel (1 MB+)', tag: 'ee',  start: 0x00100000, end: 0x02000000 },
  { id: 'iop',    label: 'IOP RAM (2 MB)',               tag: 'iop', start: 0x00000000, end: 0x00200000 },
  { id: 'custom', label: 'Custom range…',                tag: 'ee',  start: 0x00100000, end: 0x02000000 },
];

const VALUE_TYPES: Array<{ id: ValueType; label: string }> = [
  { id: 'u8',     label: 'Byte (8-bit)' },
  { id: 'u16',    label: '2 bytes (16-bit)' },
  { id: 'u32',    label: '4 bytes (32-bit)' },
  { id: 'f32',    label: 'Float' },
  { id: 'string', label: 'Text' },
  { id: 'bytes',  label: 'Byte pattern' },
];

const MODE_LABELS: Record<ScanMode, string> = {
  exact: 'Exact value', not: 'Not equal to', greater: 'Bigger than', less: 'Smaller than',
  between: 'Value between…', unknown: 'Unknown initial value',
  changed: 'Changed value', unchanged: 'Unchanged value', increased: 'Increased value',
  decreased: 'Decreased value', increasedBy: 'Increased by…', decreasedBy: 'Decreased by…',
};

const STRUCT_KINDS: Array<{ id: StructKind; label: string; hint: string }> = [
  { id: 'strings',   label: 'Strings',   hint: 'Null-terminated ASCII strings' },
  { id: 'functions', label: 'Functions', hint: 'MIPS function entry points' },
  { id: 'pointers',  label: 'Pointers',  hint: 'Words that point into EE/IOP memory' },
  { id: 'floats',    label: 'Floats',    hint: 'Plausible IEEE-754 floats' },
  { id: 'integers',  label: 'Integers',  hint: 'Non-zero 32-bit integers' },
];

const STRUCT_PAGE = 200;
const LIVE_POLL_MS = 1000;

interface SavedState {
  tab?: Tab; valueType?: ValueType; mode?: ScanMode; value?: string; value2?: string;
  hex?: boolean; signed?: boolean; regionId?: string; customStart?: string; customEnd?: string;
  live?: boolean; structKind?: StructKind;
}

@Component({
  selector: 'app-cheat-scanner-page',
  standalone: true,
  imports: [IconComponent, PageHeaderComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="cs-outer">
  <app-page-header icon="search" title="Cheat Scanner"
                   [subtitle]="gameName() ? 'Scanning ' + gameName() : 'Find the addresses behind health, ammo, timers…'" />

  <div class="cs-tabbar">
    <div class="cs-seg">
      <button [class.cs-seg-on]="tab() === 'value'" (click)="setTab('value')">Value scan</button>
      <button [class.cs-seg-on]="tab() === 'struct'" (click)="setTab('struct')">Structure scan</button>
    </div>
    <span class="cs-spacer"></span>
    @if (!connected()) {
      <span class="cs-conn-warn">Not connected to the bridge</span>
    }
  </div>

  @if (statusMsg()) {
    <div class="cs-status" [class.cs-status-error]="statusIsError()">{{ statusMsg() }}</div>
  }

  <!-- ═══ Value scan ═══════════════════════════════════════════════════════ -->
  @if (tab() === 'value') {
    <div class="cs-body">
      <section class="cs-card cs-controls">
        <div class="cs-card-head">
          <span class="cs-section-title">Scan</span>
          @if (summary()) {
            <span class="cs-count">scan #{{ summary()!.scanCount }}</span>
          }
        </div>
        <div class="cs-form">
          <label class="cs-field">
            <span class="cs-field-label">Value type</span>
            <select class="cs-select" [disabled]="!!summary()" (change)="setValueType(asSelect($event).value)">
              @for (t of valueTypes; track t.id) {
                <option [value]="t.id" [selected]="valueType() === t.id">{{ t.label }}</option>
              }
            </select>
          </label>

          @if (isNumeric()) {
            <div class="cs-checks">
              @if (valueType() !== 'f32') {
                <label class="cs-check"><input type="checkbox" [checked]="hex()" (change)="hex.set(asInput($event).checked); saveState()" /> Hex</label>
                <label class="cs-check" [class.cs-check-locked]="!!summary()">
                  <input type="checkbox" [checked]="signed()" [disabled]="!!summary()" (change)="signed.set(asInput($event).checked); saveState()" /> Signed
                </label>
              }
            </div>
          }

          <label class="cs-field">
            <span class="cs-field-label">Scan type</span>
            <select class="cs-select" (change)="setMode(asSelect($event).value)">
              @for (m of modes(); track m) {
                <option [value]="m" [selected]="mode() === m">{{ modeLabel(m) }}</option>
              }
            </select>
          </label>

          @if (needsValue()) {
            <label class="cs-field">
              <span class="cs-field-label">{{ mode() === 'between' ? 'From' : 'Value' }}</span>
              <input class="cs-input cs-mono" type="text" spellcheck="false"
                     [placeholder]="valuePlaceholder()"
                     [value]="value()"
                     (input)="value.set(asInput($event).value); saveState()"
                     (keydown.enter)="scan()" />
            </label>
          }
          @if (mode() === 'between') {
            <label class="cs-field">
              <span class="cs-field-label">To</span>
              <input class="cs-input cs-mono" type="text" spellcheck="false"
                     [value]="value2()"
                     (input)="value2.set(asInput($event).value); saveState()"
                     (keydown.enter)="scan()" />
            </label>
          }

          <label class="cs-field">
            <span class="cs-field-label">Memory</span>
            <select class="cs-select" [disabled]="!!summary()" (change)="regionId.set(asSelect($event).value); saveState()">
              @for (r of regions; track r.id) {
                <option [value]="r.id" [selected]="regionId() === r.id">{{ r.label }}</option>
              }
            </select>
          </label>
          @if (regionId() === 'custom') {
            <div class="cs-range">
              <input class="cs-input cs-mono" type="text" spellcheck="false" title="Start address (hex)" [disabled]="!!summary()"
                     [value]="customStart()" (input)="customStart.set(asInput($event).value); saveState()" />
              <span class="cs-muted">–</span>
              <input class="cs-input cs-mono" type="text" spellcheck="false" title="End address (hex, exclusive)" [disabled]="!!summary()"
                     [value]="customEnd()" (input)="customEnd.set(asInput($event).value); saveState()" />
            </div>
          }

          <div class="cs-scan-buttons">
            @if (!summary()) {
              <button class="cs-primary-btn" [disabled]="busy() || !connected()" (click)="scan()">
                @if (busy()) { Scanning… } @else { First scan }
              </button>
            } @else {
              <button class="cs-primary-btn" [disabled]="busy() || !connected()" (click)="scan()">
                @if (busy()) { Scanning… } @else { Next scan }
              </button>
              <button class="lm-tool-btn" [disabled]="busy() || !summary()!.canUndo" title="Go back to the previous result set" (click)="post({ type: 'undoScan' })">Undo</button>
              <button class="lm-tool-btn" [disabled]="busy()" title="Discard the results and start over" (click)="newScan()">New scan</button>
            }
          </div>
          @if (busy()) {
            <div class="cs-progress"><div class="cs-progress-inner"></div><span>Reading memory…</span></div>
          }
          <div class="cs-hint">
            @if (!summary()) {
              Start with <b>Exact value</b> when you know the number, or <b>Unknown initial value</b> and narrow it down with
              changed / unchanged / increased / decreased as the game runs.
            } @else {
              Change the value in game, then run <b>Next scan</b> with the comparison that matches what happened.
            }
          </div>
        </div>
      </section>

      <section class="cs-card cs-results">
        <div class="cs-card-head">
          <span class="cs-section-title">Results</span>
          <span class="cs-count">{{ resultCountLabel() }}</span>
          <span class="cs-head-actions">
            <label class="cs-check" title="Re-read the shown addresses every second">
              <input type="checkbox" [checked]="live()" (change)="setLive(asInput($event).checked)" /> Live values
            </label>
          </span>
        </div>

        @if (summary() && rows().length > 0) {
          <div class="cs-actionbar">
            <span class="cs-count">{{ selectedRows().length }} selected</span>
            <button class="lm-tool-btn" [disabled]="selectedRows().length !== 1" (click)="gotoSelected()">
              <app-icon name="arrowright" [size]="12" /> Go to
            </button>
            @if (watchListAvailable()) {
              <button class="lm-tool-btn" [disabled]="selectedRows().length === 0" (click)="watchSelected()">
                <app-icon name="eye" [size]="12" /> Watch
              </button>
            }
            <button class="lm-tool-btn" [disabled]="selectedRows().length === 0" (click)="labelSelected()">
              <app-icon name="tag" [size]="12" /> Add label
            </button>
            <button class="lm-tool-btn" [disabled]="selectedRows().length === 0 || summary()!.region !== 'ee'"
                    [title]="summary()!.region !== 'ee' ? 'Raw cheat codes address EE RAM only' : 'Copy as raw (unencrypted) codes using the current value'"
                    (click)="copyCodes()">
              <app-icon name="copy" [size]="12" /> Copy as code
            </button>
            <button class="lm-tool-btn" [disabled]="selectedRows().length === 0" (click)="writeSelected()">
              <app-icon name="edit" [size]="12" /> Write value
            </button>
            <span class="cs-spacer"></span>
            <button class="lm-tool-btn" [disabled]="pageOffset() === 0" (click)="loadPage(pageOffset() - pageSize())">&#8249;</button>
            <span class="cs-count">{{ pageOffset() + 1 }}–{{ pageOffset() + rows().length }}</span>
            <button class="lm-tool-btn" [disabled]="pageOffset() + pageSize() >= summary()!.count" (click)="loadPage(pageOffset() + pageSize())">&#8250;</button>
          </div>
        }

        <div class="cs-table-wrap">
          @if (!summary()) {
            <div class="cs-empty">Results appear here after the first scan.</div>
          } @else if (summary()!.count === 0) {
            <div class="cs-empty">No addresses match. Use Undo to step back, or start a new scan.</div>
          } @else {
            <table class="cs-table">
              <thead>
                <tr>
                  <th class="cs-col-check"><input type="checkbox" [checked]="allSelected()" (change)="selectAll(asInput($event).checked)" /></th>
                  <th>Address</th>
                  <th>Value</th>
                  <th>Previous</th>
                  <th class="cs-col-actions"></th>
                </tr>
              </thead>
              <tbody>
                @for (r of rows(); track r.address) {
                  <tr [class.cs-row-selected]="isSelected(r.address)" (click)="toggleRow(r.address, $event)" (dblclick)="goto(r.address)">
                    <td class="cs-col-check"><input type="checkbox" [checked]="isSelected(r.address)" (click)="$event.stopPropagation()" (change)="setSelected(r.address, asInput($event).checked)" /></td>
                    <td class="cs-mono cs-addr">{{ hex8(r.address) }}</td>
                    <td class="cs-mono" [class.cs-changed]="currentOf(r) !== r.previous">{{ currentOf(r) }}</td>
                    <td class="cs-mono cs-muted">{{ r.previous }}</td>
                    <td class="cs-col-actions">
                      <button class="lm-row-btn lm-goto-btn" title="Go to in disassembler" (click)="goto(r.address); $event.stopPropagation()">Go</button>
                      <button class="lm-row-btn" title="Add to the Label Manager" (click)="labelRows([r]); $event.stopPropagation()">Label</button>
                      @if (summary()!.region === 'ee') {
                        <button class="lm-row-btn" title="Copy as raw cheat code" (click)="copyCodes([r]); $event.stopPropagation()">Code</button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
            @if (rows().length === 0) {
              <div class="cs-empty">{{ summary()!.count.toLocaleString() }} candidates — run Next scan to narrow them down.</div>
            }
          }
        </div>
      </section>
    </div>
  }

  <!-- ═══ Structure scan (moved from the Label Manager) ═══════════════════ -->
  @if (tab() === 'struct') {
    <div class="cs-body cs-body-column">
      <div class="cs-struct-bar">
        <div class="cs-seg">
          @for (k of structKinds; track k.id) {
            <button [class.cs-seg-on]="structKind() === k.id" [title]="k.hint" (click)="structKind.set(k.id); saveState()">{{ k.label }}</button>
          }
        </div>
        <select class="cs-select" (change)="regionId.set(asSelect($event).value); saveState()">
          @for (r of regions; track r.id) {
            <option [value]="r.id" [selected]="regionId() === r.id">{{ r.label }}</option>
          }
        </select>
        @if (regionId() === 'custom') {
          <input class="cs-input cs-mono cs-narrow" type="text" spellcheck="false" title="Start address (hex)"
                 [value]="customStart()" (input)="customStart.set(asInput($event).value); saveState()" />
          <span class="cs-muted">–</span>
          <input class="cs-input cs-mono cs-narrow" type="text" spellcheck="false" title="End address (hex, exclusive)"
                 [value]="customEnd()" (input)="customEnd.set(asInput($event).value); saveState()" />
        }
        <button class="cs-primary-btn" [disabled]="structBusy() || !connected()" (click)="structScan()">
          @if (structBusy()) { Scanning… } @else { Scan }
        </button>
      </div>
      @if (structBusy()) {
        <div class="cs-progress cs-progress-wide"><div class="cs-progress-inner"></div><span>Scanning {{ structKind() }}… this may take a moment</span></div>
      }

      <section class="cs-card cs-results">
        <div class="cs-card-head">
          <span class="cs-section-title">{{ structResultKind() ? kindLabel(structResultKind()!) : 'Results' }}</span>
          <span class="cs-count">{{ structFiltered().length.toLocaleString() }}{{ structFilter() ? ' of ' + structRows().length.toLocaleString() : '' }}</span>
          <input class="cs-input cs-filter" type="text" placeholder="Filter address or value…"
                 [value]="structFilter()" (input)="structFilter.set(asInput($event).value); structPage.set(0)" />
        </div>
        @if (structRows().length > 0) {
          <div class="cs-actionbar">
            <span class="cs-count">{{ structSelectedRows().length }} selected</span>
            <button class="lm-tool-btn" [disabled]="structSelectedRows().length === 0" (click)="labelStruct(structSelectedRows())">
              <app-icon name="tag" [size]="12" /> Add selected as labels
            </button>
            <button class="lm-tool-btn" [disabled]="structSelectedRows().length === 0" (click)="copyStructAddresses()">
              <app-icon name="copy" [size]="12" /> Copy
            </button>
            <button class="lm-tool-btn" title="Open the Label Manager" (click)="post({ type: 'openLabelManager' })">
              <app-icon name="tag" [size]="12" /> Label Manager
            </button>
            <span class="cs-spacer"></span>
            <button class="lm-tool-btn" [disabled]="structPage() === 0" (click)="structPage.set(structPage() - 1)">&#8249;</button>
            <span class="cs-count">{{ structPageLabel() }}</span>
            <button class="lm-tool-btn" [disabled]="(structPage() + 1) * structPageSize >= structFiltered().length" (click)="structPage.set(structPage() + 1)">&#8250;</button>
          </div>
        }
        <div class="cs-table-wrap">
          @if (structRows().length === 0) {
            <div class="cs-empty">Pick what to look for and press Scan. Hits can be turned into Label Manager labels.</div>
          } @else {
            <table class="cs-table">
              <thead>
                <tr>
                  <th class="cs-col-check"><input type="checkbox" [checked]="structAllSelected()" (change)="structSelectPage(asInput($event).checked)" /></th>
                  <th>Address</th>
                  <th>{{ structValueHeader() }}</th>
                  @if (structHasDetail()) { <th>{{ structResultKind() === 'floats' ? 'Raw' : 'Decimal' }}</th> }
                  <th class="cs-col-actions"></th>
                </tr>
              </thead>
              <tbody>
                @for (r of structPageRows(); track r.address) {
                  <tr [class.cs-row-selected]="!!structSelected()[r.address]" (dblclick)="gotoText(r.address)">
                    <td class="cs-col-check"><input type="checkbox" [checked]="!!structSelected()[r.address]" (change)="structSetSelected(r.address, asInput($event).checked)" /></td>
                    <td class="cs-mono cs-addr">{{ r.address }}</td>
                    <td class="cs-mono cs-value-cell" [title]="r.value">{{ r.value }}</td>
                    @if (structHasDetail()) { <td class="cs-mono cs-muted">{{ r.detail }}</td> }
                    <td class="cs-col-actions">
                      <button class="lm-row-btn lm-goto-btn" title="Go to in disassembler" (click)="gotoText(r.address)">Go</button>
                      @if (structResultKind() === 'pointers') {
                        <button class="lm-row-btn lm-goto-btn" title="Go to the pointed-to address" (click)="gotoText(r.value)">Target</button>
                      }
                      <button class="lm-row-btn" title="Add to the Label Manager" (click)="labelStruct([r])">Label</button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </div>
      </section>
    </div>
  }
</div>
  `,
})
export class CheatScannerPageComponent implements AfterViewInit, OnDestroy {
  private readonly vscode = getVsCodeApi();
  private readonly zone = inject(NgZone);
  private _liveTimer: ReturnType<typeof setInterval> | undefined;

  readonly regions = REGIONS;
  readonly valueTypes = VALUE_TYPES;
  readonly structKinds = STRUCT_KINDS;
  readonly structPageSize = STRUCT_PAGE;

  // ── Context ──────────────────────────────────────────────────────────────
  readonly gameName = signal('');
  readonly connected = signal(true);
  readonly watchListAvailable = signal(false);
  readonly tab = signal<Tab>('value');
  readonly statusMsg = signal('');
  readonly statusIsError = signal(false);

  // ── Value scan form ──────────────────────────────────────────────────────
  readonly valueType = signal<ValueType>('u32');
  readonly mode = signal<ScanMode>('exact');
  readonly value = signal('');
  readonly value2 = signal('');
  readonly hex = signal(false);
  readonly signed = signal(false);
  readonly regionId = signal('ee');
  readonly customStart = signal('00100000');
  readonly customEnd = signal('02000000');
  readonly live = signal(true);

  // ── Value scan results ───────────────────────────────────────────────────
  readonly busy = signal(false);
  readonly summary = signal<ScanSummary | null>(null);
  readonly rows = signal<ScanRow[]>([]);
  readonly pageOffset = signal(0);
  readonly pageSize = signal(200);
  readonly current = signal<Record<string, { value: string; raw: string }>>({});
  readonly selected = signal<Record<number, boolean>>({});

  // ── Structure scan ───────────────────────────────────────────────────────
  readonly structKind = signal<StructKind>('strings');
  readonly structResultKind = signal<StructKind | null>(null);
  readonly structBusy = signal(false);
  readonly structRows = signal<StructRow[]>([]);
  readonly structFilter = signal('');
  readonly structPage = signal(0);
  readonly structSelected = signal<Record<string, boolean>>({});

  readonly isNumeric = computed(() => this.valueType() !== 'string' && this.valueType() !== 'bytes');

  readonly modes = computed<ScanMode[]>(() => {
    const first = !this.summary();
    if (!this.isNumeric()) {
      return first ? ['exact'] : ['exact', 'not', 'changed', 'unchanged'];
    }
    return first
      ? ['exact', 'not', 'greater', 'less', 'between', 'unknown']
      : ['exact', 'not', 'greater', 'less', 'between', 'changed', 'unchanged', 'increased', 'decreased', 'increasedBy', 'decreasedBy'];
  });

  readonly needsValue = computed(() =>
    !['unknown', 'changed', 'unchanged', 'increased', 'decreased'].includes(this.mode()));

  readonly valuePlaceholder = computed(() => {
    switch (this.valueType()) {
      case 'f32':    return 'e.g. 100.0';
      case 'string': return 'Text to find';
      case 'bytes':  return 'e.g. 3C 02 ?? 80';
      default:       return this.hex() ? 'e.g. 3E8' : 'e.g. 1000';
    }
  });

  readonly resultCountLabel = computed(() => {
    const s = this.summary();
    if (!s) { return ''; }
    return `${s.count.toLocaleString()} result${s.count === 1 ? '' : 's'}`;
  });

  readonly selectedRows = computed(() => {
    const sel = this.selected();
    return this.rows().filter(r => sel[r.address]);
  });
  readonly allSelected = computed(() => this.rows().length > 0 && this.selectedRows().length === this.rows().length);

  readonly structFiltered = computed(() => {
    const q = this.structFilter().trim().toLowerCase();
    if (!q) { return this.structRows(); }
    return this.structRows().filter(r =>
      r.address.toLowerCase().includes(q) || r.value.toLowerCase().includes(q) || (r.detail ?? '').toLowerCase().includes(q));
  });
  readonly structPageRows = computed(() =>
    this.structFiltered().slice(this.structPage() * STRUCT_PAGE, (this.structPage() + 1) * STRUCT_PAGE));
  readonly structSelectedRows = computed(() => {
    const sel = this.structSelected();
    return this.structRows().filter(r => sel[r.address]);
  });
  readonly structAllSelected = computed(() => {
    const page = this.structPageRows();
    const sel = this.structSelected();
    return page.length > 0 && page.every(r => sel[r.address]);
  });
  readonly structHasDetail = computed(() => this.structResultKind() === 'floats' || this.structResultKind() === 'integers');
  readonly structValueHeader = computed(() => {
    switch (this.structResultKind()) {
      case 'strings':   return 'String';
      case 'functions': return 'Name';
      case 'pointers':  return 'Points to';
      case 'integers':  return 'Hex';
      default:          return 'Value';
    }
  });
  readonly structPageLabel = computed(() => {
    const total = this.structFiltered().length;
    if (total === 0) { return '0'; }
    const from = this.structPage() * STRUCT_PAGE + 1;
    return `${from.toLocaleString()}–${Math.min(total, from + STRUCT_PAGE - 1).toLocaleString()}`;
  });

  constructor() {
    const st = (this.vscode.getState() ?? {}) as SavedState;
    if (st.tab) { this.tab.set(st.tab); }
    if (st.valueType) { this.valueType.set(st.valueType); }
    if (st.mode) { this.mode.set(st.mode); }
    if (st.value !== undefined) { this.value.set(st.value); }
    if (st.value2 !== undefined) { this.value2.set(st.value2); }
    if (st.hex !== undefined) { this.hex.set(st.hex); }
    if (st.signed !== undefined) { this.signed.set(st.signed); }
    if (st.regionId) { this.regionId.set(st.regionId); }
    if (st.customStart) { this.customStart.set(st.customStart); }
    if (st.customEnd) { this.customEnd.set(st.customEnd); }
    if (st.live !== undefined) { this.live.set(st.live); }
    if (st.structKind) { this.structKind.set(st.structKind); }
  }

  ngAfterViewInit(): void {
    window.addEventListener('message', (event: MessageEvent) => {
      const data = event.data as { type: string; [key: string]: unknown };
      this.zone.run(() => this._handleMessage(data));
    });
    this._liveTimer = setInterval(() => this._pollLive(), LIVE_POLL_MS);
    this.post({ type: 'ready' });
  }

  ngOnDestroy(): void {
    if (this._liveTimer) { clearInterval(this._liveTimer); }
  }

  // ── Value scan actions ───────────────────────────────────────────────────

  post(msg: Record<string, unknown>): void { this.vscode.postMessage(msg); }

  setTab(tab: Tab): void { this.tab.set(tab); this.saveState(); }

  setValueType(v: string): void {
    this.valueType.set(v as ValueType);
    if (!this.modes().includes(this.mode())) { this.mode.set('exact'); }
    this.saveState();
  }

  setMode(v: string): void { this.mode.set(v as ScanMode); this.saveState(); }

  setLive(on: boolean): void { this.live.set(on); this.saveState(); }

  modeLabel(m: ScanMode): string { return MODE_LABELS[m]; }

  scan(): void {
    if (this.busy()) { return; }
    const region = this._region();
    if (!region) { return; }
    if (!this.modes().includes(this.mode())) { this.mode.set(this.modes()[0]); }
    this.busy.set(true);
    this._status('', false);
    this.post({
      type: this.summary() ? 'nextScan' : 'firstScan',
      request: {
        valueType: this.valueType(),
        mode:      this.mode(),
        value:     this.value(),
        value2:    this.value2(),
        hex:       this.hex(),
        signed:    this.signed(),
        region:    region.tag,
        start:     region.start,
        end:       region.end,
      },
    });
  }

  newScan(): void {
    this.post({ type: 'newScan' });
  }

  loadPage(offset: number): void {
    this.post({ type: 'page', offset: Math.max(0, offset) });
  }

  isSelected(addr: number): boolean { return !!this.selected()[addr]; }

  setSelected(addr: number, on: boolean): void {
    this.selected.set({ ...this.selected(), [addr]: on });
  }

  toggleRow(addr: number, event: MouseEvent): void {
    if (event.ctrlKey || event.metaKey) {
      this.setSelected(addr, !this.isSelected(addr));
    } else {
      this.selected.set({ [addr]: true });
    }
  }

  selectAll(on: boolean): void {
    const next: Record<number, boolean> = {};
    if (on) { for (const r of this.rows()) { next[r.address] = true; } }
    this.selected.set(next);
  }

  goto(addr: number): void { this.post({ type: 'goto', address: addr }); }

  gotoSelected(): void {
    const r = this.selectedRows()[0];
    if (r) { this.goto(r.address); }
  }

  currentOf(r: ScanRow): string {
    return this.current()[r.address.toString(16)]?.value ?? r.previous;
  }

  labelSelected(): void { this.labelRows(this.selectedRows()); }

  labelRows(rows: ScanRow[]): void {
    const s = this.summary();
    if (!s || rows.length === 0) { return; }
    const entries: LabelEntry[] = rows.map(r => {
      const base = { address: this.hex8(r.address), dynamic: true };
      switch (s.valueType) {
        case 'f32':    return { ...base, name: `float_${this.hex8(r.address).slice(2)}`, type: 'Float', valueType: 'float' };
        case 'string': return { ...base, name: this.currentOf(r).slice(0, 40), type: 'String', dynamic: undefined };
        case 'bytes':  return { ...base, name: `bytes_${this.hex8(r.address).slice(2)}`, comment: r.previous, type: 'Address', dynamic: undefined };
        default: {
          const vt = (s.signed ? 's' : 'u') + s.size * 8;
          return { ...base, name: `${vt}_${this.hex8(r.address).slice(2)}`, type: 'Value', valueType: vt };
        }
      }
    });
    this.post({ type: 'addLabels', entries });
  }

  watchSelected(): void {
    const s = this.summary();
    if (!s) { return; }
    const watchType = s.valueType === 'f32' ? 'f32'
      : s.valueType === 'string' ? 'string'
      : s.valueType === 'bytes' ? 'u8'
      : (s.signed ? 's' : 'u') + s.size * 8;
    for (const r of this.selectedRows()) {
      this.post({ type: 'memWatchAdd', expression: this.hex8(r.address), watchType });
    }
  }

  writeSelected(): void {
    this.post({ type: 'writeValue', addresses: this.selectedRows().map(r => r.address) });
  }

  /** Raw (unencrypted) PS2 codes writing each row's current bytes. */
  copyCodes(rows: ScanRow[] = this.selectedRows()): void {
    if (rows.length === 0) { return; }
    const blocks = rows.map(r => {
      const raw = this.current()[r.address.toString(16)]?.raw ?? r.raw;
      return [`Scanner ${this.hex8(r.address)}`, ...rawCodes(r.address, raw)].join('\n');
    });
    this.post({
      type: 'copy',
      text: blocks.join('\n\n'),
      summary: `Copied ${rows.length} code${rows.length === 1 ? '' : 's'} (raw format — convert them in the Cheat Converter).`,
    });
  }

  // ── Structure scan actions ───────────────────────────────────────────────

  structScan(): void {
    if (this.structBusy()) { return; }
    const region = this._region();
    if (!region) { return; }
    this.structBusy.set(true);
    this._status('', false);
    this.post({ type: 'structScan', kind: this.structKind(), region: region.tag, start: region.start, end: region.end });
  }

  structSetSelected(addr: string, on: boolean): void {
    this.structSelected.set({ ...this.structSelected(), [addr]: on });
  }

  structSelectPage(on: boolean): void {
    const next = { ...this.structSelected() };
    for (const r of this.structPageRows()) { next[r.address] = on; }
    this.structSelected.set(next);
  }

  labelStruct(rows: StructRow[]): void {
    const kind = this.structResultKind();
    if (!kind || rows.length === 0) { return; }
    this.post({ type: 'addStructLabels', kind, rows });
  }

  copyStructAddresses(): void {
    const rows = this.structSelectedRows();
    this.post({
      type: 'copy',
      text: rows.map(r => `${r.address}\t${r.value}`).join('\n'),
      summary: `Copied ${rows.length} row${rows.length === 1 ? '' : 's'}.`,
    });
  }

  gotoText(address: string): void {
    const n = parseInt(address.replace(/^0x/i, ''), 16);
    if (!isNaN(n)) { this.goto(n >>> 0); }
  }

  kindLabel(k: StructKind): string { return STRUCT_KINDS.find(x => x.id === k)?.label ?? k; }

  // ── Template helpers ─────────────────────────────────────────────────────

  asInput(e: Event):  HTMLInputElement  { return e.target as HTMLInputElement; }
  asSelect(e: Event): HTMLSelectElement { return e.target as HTMLSelectElement; }

  hex8(n: number): string { return '0x' + (n >>> 0).toString(16).toUpperCase().padStart(8, '0'); }

  saveState(): void {
    this.vscode.setState({
      tab: this.tab(), valueType: this.valueType(), mode: this.mode(), value: this.value(), value2: this.value2(),
      hex: this.hex(), signed: this.signed(), regionId: this.regionId(), customStart: this.customStart(),
      customEnd: this.customEnd(), live: this.live(), structKind: this.structKind(),
    } satisfies SavedState);
  }

  // ── Internals ────────────────────────────────────────────────────────────

  private _region(): { tag: RegionTag; start: number; end: number } | null {
    const preset = REGIONS.find(r => r.id === this.regionId()) ?? REGIONS[0];
    if (preset.id !== 'custom') { return preset; }
    const start = parseInt(this.customStart().replace(/^0x/i, ''), 16);
    const end = parseInt(this.customEnd().replace(/^0x/i, ''), 16);
    if (isNaN(start) || isNaN(end) || end <= start) {
      this._status('Enter a hex start and end address with end > start.', true);
      return null;
    }
    if (end - start > 0x02000000) {
      this._status('A scan range can be at most 32 MB.', true);
      return null;
    }
    return { tag: 'ee', start: start >>> 0, end: end >>> 0 };
  }

  private _pollLive(): void {
    if (!this.live() || this.busy() || this.tab() !== 'value' || document.hidden) { return; }
    const rows = this.rows();
    if (rows.length === 0) { return; }
    this.post({ type: 'readCurrent', addresses: rows.map(r => r.address) });
  }

  private _status(message: string, isError: boolean): void {
    this.statusMsg.set(message);
    this.statusIsError.set(isError);
  }

  private _handleMessage(data: { type: string; [key: string]: unknown }): void {
    switch (data['type']) {
      case 'context':
        this.gameName.set((data['gameName'] as string) ?? '');
        this.connected.set(!!data['connected']);
        this.watchListAvailable.set(!!data['watchListAvailable']);
        break;

      case 'scanSession': {
        const summary = (data['summary'] as ScanSummary | null) ?? null;
        this.busy.set(false);
        this.summary.set(summary);
        this.rows.set((data['rows'] as ScanRow[]) ?? []);
        this.pageSize.set((data['pageSize'] as number) ?? 200);
        this.pageOffset.set(0);
        this.current.set({});
        this.selected.set({});
        if (summary) {
          // The session fixes the type/signedness — reflect it if the page state drifted.
          this.valueType.set(summary.valueType);
          this.signed.set(summary.signed);
          if (!this.modes().includes(this.mode())) { this.mode.set(this.modes()[0]); }
          this._status(`${summary.count.toLocaleString()} result${summary.count === 1 ? '' : 's'}. ${summary.message ?? ''}`.trim(), false);
        } else {
          this._status('', false);
        }
        break;
      }

      case 'scanPage':
        this.pageOffset.set((data['offset'] as number) ?? 0);
        this.rows.set((data['rows'] as ScanRow[]) ?? []);
        this.selected.set({});
        this.current.set({});
        break;

      case 'scanError':
        this.busy.set(false);
        this._status(data['message'] as string ?? 'Scan failed.', true);
        break;

      case 'currentValues':
        this.current.set({ ...this.current(), ...((data['values'] as Record<string, { value: string; raw: string }>) ?? {}) });
        break;

      case 'structResult': {
        const rows = (data['rows'] as StructRow[]) ?? [];
        const kind = data['kind'] as StructKind;
        this.structBusy.set(false);
        this.structResultKind.set(kind);
        this.structRows.set(rows);
        this.structPage.set(0);
        this.structSelected.set({});
        this._status(`Scan complete: ${rows.length.toLocaleString()} ${kind} found.`, false);
        break;
      }

      case 'structError':
        this.structBusy.set(false);
        this._status(data['message'] as string ?? 'Scan failed.', true);
        break;

      case 'status':
        this.busy.set(false);
        this.structBusy.set(false);
        this._status(data['message'] as string ?? '', !!data['isError']);
        break;
    }
  }
}

/**
 * Raw PS2 write codes for `rawHex` (little-endian bytes) at `address`:
 * 32-bit writes (2xxxxxxx) per word, then 16-bit (1xxxxxxx) / 8-bit (0xxxxxxx)
 * for the tail.
 */
function rawCodes(address: number, rawHex: string): string[] {
  const bytes = rawHex.match(/../g)?.map(h => parseInt(h, 16)) ?? [];
  const codes: string[] = [];
  const addr7 = (a: number) => ((a >>> 0) & 0x01FFFFFF).toString(16).toUpperCase().padStart(7, '0');
  const hexN = (v: number, n: number) => (v >>> 0).toString(16).toUpperCase().padStart(n, '0');
  let i = 0;
  if (address % 4 === 0) {
    for (; i + 4 <= bytes.length; i += 4) {
      const word = bytes[i] | (bytes[i + 1] << 8) | (bytes[i + 2] << 16) | (bytes[i + 3] << 24);
      codes.push(`2${addr7(address + i)} ${hexN(word, 8)}`);
    }
  }
  if ((address + i) % 2 === 0) {
    for (; i + 2 <= bytes.length; i += 2) {
      codes.push(`1${addr7(address + i)} 0000${hexN(bytes[i] | (bytes[i + 1] << 8), 4)}`);
    }
  }
  for (; i < bytes.length; i++) {
    codes.push(`0${addr7(address + i)} 000000${hexN(bytes[i], 2)}`);
  }
  return codes;
}
