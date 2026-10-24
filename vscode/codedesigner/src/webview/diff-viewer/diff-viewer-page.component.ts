import {
  Component,
  AfterViewInit,
  ViewEncapsulation,
  signal,
  computed,
  effect,
  untracked,
  NgZone,
  inject,
} from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import { IconComponent } from '../shared/icon.component';
import { PageHeaderComponent } from '../shared/page-header.component';
import { SnapshotEntry } from '../disasm-panel/types';
import '../shared/styles/shared.css';
import './diff-viewer-page.css';

/** Select value standing for the emulator's live EE RAM. */
const LIVE = '__live__';
const HUNKS_PER_PAGE   = 20;
/** Rows of a hunk rendered before "Show more" (a single hunk can span megabytes). */
const ROWS_PER_HUNK    = 64;
/** Rows revealed per click of an expander, like GitHub's 20-line steps. */
const EXPAND_STEP      = 20;
/** Disassembly is requested in chunks of this many rows so "Show more" only fetches the new part. */
const ASM_CHUNK_ROWS   = 64;

type DiffLayout = 'unified' | 'split';
type DiffView   = 'bytes' | 'words' | 'instr';

type DiffSource =
  | { kind: 'live' }
  | { kind: 'dump'; path: string; label: string };

interface DiffMeta {
  diffId:       number;
  base:         number;
  rowBytes:     number;
  minRow:       number;
  maxRow:       number;
  labelA:       string;
  labelB:       string;
  fileA:        string;
  fileB:        string;
  lengthA:      number;
  lengthB:      number;
  readAt:       string;
  /** Flat triples: startRow, endRow (exclusive), changedBytes. */
  runs:         number[];
  changedRows:  number;
  changedBytes: number;
  truncated:    boolean;
}

interface Hunk {
  start:        number;
  end:          number;
  changedRows:  number;
  changedBytes: number;
  /** Unchanged rows between the previous hunk (or range start) and this one. */
  gapBefore:    number;
  /** End row of the previous hunk (range start for the first), for "expand down" from it. */
  prevEnd:      number;
  hasPrev:      boolean;
}

/** One rendered cell: text plus whether it differs from the other side. */
interface Tok { t: string; c: boolean; g?: boolean }

interface UnifiedLine {
  kind:  'ctx' | 'del' | 'add';
  addr:  string;
  toks:  Tok[];
  extra: Tok[] | null;
  asm:   string | null;
}

interface SplitLine {
  kind:  'ctx' | 'chg';
  addr:  string;
  l:     Tok[];
  r:     Tok[];
  le:    Tok[] | null;
  re:    Tok[] | null;
  lasm:  string | null;
  rasm:  string | null;
}

interface HunkView {
  hunk:      Hunk;
  id:        string;
  header:    string;
  unified:   UnifiedLine[];
  split:     SplitLine[];
  loading:   boolean;
  hiddenRows: number;
}

interface Unit { addr: number; a: string; b: string }

interface SavedState { layout?: DiffLayout; view?: DiffView; context?: number; sideA?: string; sideB?: string }

@Component({
  selector: 'app-diff-viewer-page',
  standalone: true,
  imports: [IconComponent, PageHeaderComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="dv-outer">
  <app-page-header icon="gitcompare" title="Memory Diff" subtitle="Compare memory dumps, or live EE RAM against a dump" />

  <!-- ── Compare bar ─────────────────────────────────────────────────────── -->
  <div class="dv-compare-bar">
    <div class="dv-side">
      <span class="dv-side-tag dv-side-a">A</span>
      <select class="dv-select" [value]="sideA()" (change)="sideA.set(asSelect($event).value); saveState()">
        <option value="">— select base —</option>
        <option [value]="LIVE" [disabled]="!connected()" [selected]="sideA() === LIVE">Live EE RAM{{ connected() ? '' : ' (not connected)' }}</option>
        @for (s of sourceOptions(); track s.path) {
          <option [value]="s.path" [selected]="sideA() === s.path">{{ optionLabel(s) }}</option>
        }
      </select>
      <button class="lm-tool-btn" title="Browse for a .bin/.p2s dump" (click)="browse('A')">Browse…</button>
    </div>

    <button class="dv-icon-btn" title="Swap A and B" (click)="swap()">&#x21C4;</button>

    <div class="dv-side">
      <span class="dv-side-tag dv-side-b">B</span>
      <select class="dv-select" [value]="sideB()" (change)="sideB.set(asSelect($event).value); saveState()">
        <option value="">— select compare —</option>
        <option [value]="LIVE" [disabled]="!connected()" [selected]="sideB() === LIVE">Live EE RAM{{ connected() ? '' : ' (not connected)' }}</option>
        @for (s of sourceOptions(); track s.path) {
          <option [value]="s.path" [selected]="sideB() === s.path">{{ optionLabel(s) }}</option>
        }
      </select>
      <button class="lm-tool-btn" title="Browse for a .bin/.p2s dump" (click)="browse('B')">Browse…</button>
    </div>

    <div class="dv-range">
      <span class="dv-field-label">Range</span>
      <input class="dv-input dv-addr-input" spellcheck="false" [value]="rangeStart()"
             (input)="rangeStart.set(asInput($event).value)" (keydown.enter)="compare()" />
      <span class="lm-muted">–</span>
      <input class="dv-input dv-addr-input" spellcheck="false" [value]="rangeEnd()"
             (input)="rangeEnd.set(asInput($event).value)" (keydown.enter)="compare()" />
    </div>

    <button class="dv-primary-btn" [disabled]="!canCompare() || busy()" (click)="compare()">
      @if (busy()) { Comparing… } @else if (usesLive() && diff()) { &#x21BB; Refresh } @else { Compare }
    </button>
    <button class="dv-icon-btn" title="Reload snapshot list" (click)="post({ type: 'refreshSnapshots' })">
      <app-icon name="refreshcw" [size]="13" />
    </button>
  </div>

  @if (busy()) {
    <div class="dv-progress"><div class="dv-progress-inner"></div><span>{{ progressMsg() }}</span></div>
  }
  @if (statusMsg()) {
    <div class="dv-status" [class.dv-status-error]="statusIsError()">{{ statusMsg() }}</div>
  }

  @if (diff(); as d) {
    <!-- ── Results toolbar ──────────────────────────────────────────────── -->
    <div class="dv-toolbar">
      <div class="dv-summary">
        Showing <b>{{ fmt(d.changedRows) }}</b> changed row{{ d.changedRows === 1 ? '' : 's' }}
        with <b>{{ fmt(d.changedBytes) }}</b> changed byte{{ d.changedBytes === 1 ? '' : 's' }}
        in <b>{{ fmt(hunks().length) }}</b> hunk{{ hunks().length === 1 ? '' : 's' }}
        @if (d.truncated) { <span class="dv-warn">(truncated — narrow the range)</span> }
      </div>
      <div class="dv-toolbar-right">
        <input class="dv-input dv-jump-input" spellcheck="false" placeholder="Jump to address…"
               [value]="jumpText()" (input)="jumpText.set(asInput($event).value)" (keydown.enter)="jump()" />
        <div class="dv-seg">
          <button [class.dv-seg-on]="view() === 'bytes'" (click)="setView('bytes')">Bytes</button>
          <button [class.dv-seg-on]="view() === 'words'" (click)="setView('words')">Words</button>
          <button [class.dv-seg-on]="view() === 'instr'" (click)="setView('instr')"
                  [title]="connected() ? 'Disassemble changed words' : 'Needs the bridge for disassembly'">Instructions</button>
        </div>
        <div class="dv-seg">
          <button [class.dv-seg-on]="layout() === 'unified'" (click)="setLayout('unified')">Unified</button>
          <button [class.dv-seg-on]="layout() === 'split'" (click)="setLayout('split')">Split</button>
        </div>
        <select class="dv-select dv-ctx-select" title="Context rows around each change"
                [value]="'' + context()" (change)="setContext(+asSelect($event).value)">
          @for (n of contextOptions; track n) { <option [value]="'' + n" [selected]="context() === n">{{ n }} context</option> }
        </select>
      </div>
    </div>

    <div class="dv-body">
      <!-- ── Hunk navigator (GitHub's file tree) ────────────────────────── -->
      <nav class="dv-nav">
        <div class="dv-nav-head">Changes</div>
        @if (hunks().length === 0) {
          <div class="dv-nav-empty">No differences</div>
        }
        @for (v of pageViews(); track v.id) {
          <button class="dv-nav-item" (click)="scrollToHunk(v.id)" [title]="v.header">
            <span class="dv-nav-addr">{{ hex8(addrOf(v.hunk.start)) }}</span>
            <span class="dv-nav-count">{{ fmt(v.hunk.changedBytes) }} B</span>
          </button>
        }
        @if (totalPages() > 1) {
          <div class="dv-nav-pager">
            <button class="lm-tool-btn" [disabled]="page() === 0" (click)="setPage(page() - 1)">&#x2039;</button>
            <span>{{ page() + 1 }} / {{ totalPages() }}</span>
            <button class="lm-tool-btn" [disabled]="page() >= totalPages() - 1" (click)="setPage(page() + 1)">&#x203A;</button>
          </div>
        }
      </nav>

      <!-- ── Diff box ───────────────────────────────────────────────────── -->
      <main class="dv-main" id="dv-main">
        <div class="dv-file">
          <div class="dv-file-head">
            <span class="dv-file-name">EE RAM</span>
            <span class="dv-file-range lm-muted">{{ hex8(addrOf(d.minRow)) }}–{{ hex8(addrOf(d.maxRow)) }}</span>
            <span class="dv-stat-del">−{{ fmt(d.changedBytes) }}</span>
            <span class="dv-stat-add">+{{ fmt(d.changedBytes) }}</span>
            <span class="dv-diffstat">
              @for (b of diffstat(); track $index) { <i [class]="'dv-ds dv-ds-' + b"></i> }
            </span>
            <span class="dv-file-sides">
              <span class="dv-side-tag dv-side-a">A</span> <span [title]="d.fileA">{{ d.labelA }}</span>
              <span class="lm-muted">→</span>
              <span class="dv-side-tag dv-side-b">B</span> <span [title]="d.fileB">{{ d.labelB }}</span>
              @if (usesLive()) { <span class="lm-muted">· live read {{ d.readAt }}</span> }
            </span>
          </div>

          @if (d.lengthA !== d.lengthB) {
            <div class="dv-note">Sizes differ (A {{ fmt(d.lengthA) }} B, B {{ fmt(d.lengthB) }} B) — compared the common prefix.</div>
          }
          @if (hunks().length === 0) {
            <div class="dv-empty">
              <app-icon name="circlecheck" [size]="18" />
              <span>No differences in this range.</span>
            </div>
          }

          @for (v of pageViews(); track v.id) {
            <table class="dv-table" [class.dv-split]="layout() === 'split'" [class]="'dv-view-' + view()" [attr.id]="v.id">
              <colgroup>
                @if (layout() === 'unified') {
                  <col class="dv-c-addr" /><col class="dv-c-sign" /><col class="dv-c-hex" /><col class="dv-c-extra" />
                } @else {
                  <col class="dv-c-addr" /><col class="dv-c-sign" /><col class="dv-c-hex" /><col class="dv-c-extra" />
                  <col class="dv-c-addr" /><col class="dv-c-sign" /><col class="dv-c-hex" /><col class="dv-c-extra" />
                }
              </colgroup>
              <tbody>
                <!-- Hunk header with GitHub-style expanders -->
                <tr class="dv-hunk-row">
                  <td class="dv-expand-cell">
                    @if (v.hunk.gapBefore > 0) {
                      @if (v.hunk.gapBefore <= expandStep) {
                        <button class="dv-expand-btn" title="Expand all {{ v.hunk.gapBefore }} hidden rows"
                                (click)="reveal(v.hunk.start - v.hunk.gapBefore, v.hunk.start)">&#x2195;</button>
                      } @else {
                        @if (v.hunk.hasPrev) {
                          <button class="dv-expand-btn" title="Expand down from previous hunk"
                                  (click)="reveal(v.hunk.prevEnd, v.hunk.prevEnd + expandStep)">&#x2193;</button>
                        }
                        <button class="dv-expand-btn" title="Expand up"
                                (click)="reveal(v.hunk.start - expandStep, v.hunk.start)">&#x2191;</button>
                      }
                    }
                  </td>
                  <td class="dv-hunk-text" [attr.colspan]="layout() === 'unified' ? 3 : 7">
                    <div class="dv-hunk-inner">
                    <span class="dv-hunk-title">{{ v.header }}</span>
                    @if (v.hunk.gapBefore > 0) {
                      <span class="dv-hunk-gap">{{ fmt(v.hunk.gapBefore) }} unchanged row{{ v.hunk.gapBefore === 1 ? '' : 's' }} hidden</span>
                    }
                    <span class="dv-hunk-actions">
                      <button class="lm-row-btn" title="Copy changed words as 32-bit write codes (B values)"
                              (click)="copyCodes(v.hunk)"><app-icon name="copy" [size]="12" /> Codes</button>
                      <button class="lm-row-btn lm-goto-btn" title="Open this address in the disassembler"
                              (click)="gotoAddr(addrOf(v.hunk.start))"><app-icon name="arrowright" [size]="12" /> Disasm</button>
                    </span>
                    </div>
                  </td>
                </tr>

                @if (layout() === 'unified') {
                  @for (ln of v.unified; track $index) {
                    <tr [class]="'dv-line dv-' + ln.kind">
                      <td class="dv-num">{{ ln.addr }}</td>
                      <td class="dv-sign">{{ ln.kind === 'del' ? '−' : ln.kind === 'add' ? '+' : '' }}</td>
                      <td class="dv-code">@for (t of ln.toks; track $index) {<span class="dv-tok" [class.dv-hl]="t.c" [class.dv-gap]="t.g">{{ t.t }}</span>}</td>
                      <td class="dv-extra">
                        @if (ln.extra) {@for (t of ln.extra; track $index) {<span [class.dv-hl]="t.c">{{ t.t }}</span>}}
                        @if (ln.asm !== null) {<span class="dv-asm">{{ ln.asm }}</span>}
                      </td>
                    </tr>
                  }
                } @else {
                  @for (ln of v.split; track $index) {
                    <tr class="dv-line" [class.dv-ctx]="ln.kind === 'ctx'">
                      <td class="dv-num" [class.dv-del]="ln.kind === 'chg'">{{ ln.addr }}</td>
                      <td class="dv-sign" [class.dv-del]="ln.kind === 'chg'">{{ ln.kind === 'chg' ? '−' : '' }}</td>
                      <td class="dv-code" [class.dv-del]="ln.kind === 'chg'">@for (t of ln.l; track $index) {<span class="dv-tok" [class.dv-hl]="t.c" [class.dv-gap]="t.g">{{ t.t }}</span>}</td>
                      <td class="dv-extra dv-split-mid" [class.dv-del]="ln.kind === 'chg'">
                        @if (ln.le) {@for (t of ln.le; track $index) {<span [class.dv-hl]="t.c">{{ t.t }}</span>}}
                        @if (ln.lasm !== null) {<span class="dv-asm">{{ ln.lasm }}</span>}
                      </td>
                      <td class="dv-num" [class.dv-add]="ln.kind === 'chg'">{{ ln.addr }}</td>
                      <td class="dv-sign" [class.dv-add]="ln.kind === 'chg'">{{ ln.kind === 'chg' ? '+' : '' }}</td>
                      <td class="dv-code" [class.dv-add]="ln.kind === 'chg'">@for (t of ln.r; track $index) {<span class="dv-tok" [class.dv-hl]="t.c" [class.dv-gap]="t.g">{{ t.t }}</span>}</td>
                      <td class="dv-extra" [class.dv-add]="ln.kind === 'chg'">
                        @if (ln.re) {@for (t of ln.re; track $index) {<span [class.dv-hl]="t.c">{{ t.t }}</span>}}
                        @if (ln.rasm !== null) {<span class="dv-asm">{{ ln.rasm }}</span>}
                      </td>
                    </tr>
                  }
                }

                @if (v.loading) {
                  <tr class="dv-more-row"><td [attr.colspan]="layout() === 'unified' ? 4 : 8">Loading rows…</td></tr>
                } @else if (v.hiddenRows > 0) {
                  <tr class="dv-more-row">
                    <td [attr.colspan]="layout() === 'unified' ? 4 : 8">
                      <button class="dv-link-btn" (click)="showMore(v.hunk)">
                        Show {{ fmt(Math.min(v.hiddenRows, rowsPerHunk * 4)) }} more rows
                      </button>
                      <span class="lm-muted">({{ fmt(v.hiddenRows) }} not shown)</span>
                    </td>
                  </tr>
                }

                @if (isLastHunk(v.hunk) && d.maxRow > v.hunk.end) {
                  <tr class="dv-hunk-row dv-hunk-tail">
                    <td class="dv-expand-cell">
                      <button class="dv-expand-btn" title="Expand down"
                              (click)="reveal(v.hunk.end, v.hunk.end + expandStep)">&#x2193;</button>
                    </td>
                    <td class="dv-hunk-text" [attr.colspan]="layout() === 'unified' ? 3 : 7">
                      <span class="dv-hunk-gap">{{ fmt(d.maxRow - v.hunk.end) }} unchanged rows to end of range</span>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </div>

        @if (totalPages() > 1) {
          <div class="dv-pager">
            <button class="lm-tool-btn" [disabled]="page() === 0" (click)="setPage(0)">First</button>
            <button class="lm-tool-btn" [disabled]="page() === 0" (click)="setPage(page() - 1)">&#x2039; Previous</button>
            <span>Page {{ page() + 1 }} of {{ totalPages() }} · hunks {{ page() * hunksPerPage + 1 }}–{{ Math.min((page() + 1) * hunksPerPage, hunks().length) }}</span>
            <button class="lm-tool-btn" [disabled]="page() >= totalPages() - 1" (click)="setPage(page() + 1)">Next &#x203A;</button>
            <button class="lm-tool-btn" [disabled]="page() >= totalPages() - 1" (click)="setPage(totalPages() - 1)">Last</button>
          </div>
        }
      </main>
    </div>
  } @else if (!busy()) {
    <div class="dv-placeholder">
      <app-icon name="gitcompare" [size]="28" />
      <div class="dv-placeholder-title">Compare two memory images</div>
      <div class="lm-muted">Pick a snapshot or dump for A and B — or Live EE RAM for one side — then press Compare.</div>
    </div>
  }
</div>
`,
})
export class DiffViewerPageComponent implements AfterViewInit {
  private readonly vscode = getVsCodeApi();
  private readonly zone   = inject(NgZone);

  readonly LIVE           = LIVE;
  readonly Math           = Math;
  readonly expandStep     = EXPAND_STEP;
  readonly rowsPerHunk    = ROWS_PER_HUNK;
  readonly hunksPerPage   = HUNKS_PER_PAGE;
  readonly contextOptions = [0, 1, 3, 5, 10];

  // ── Source selection ──────────────────────────────────────────────────────
  readonly snapshots   = signal<SnapshotEntry[]>([]);
  readonly browsed     = signal<SnapshotEntry[]>([]);
  readonly connected   = signal(false);
  readonly sideA       = signal('');
  readonly sideB       = signal('');
  readonly rangeStart  = signal('00000000');
  readonly rangeEnd    = signal('02000000');

  // ── View options ──────────────────────────────────────────────────────────
  readonly layout  = signal<DiffLayout>('unified');
  readonly view    = signal<DiffView>('bytes');
  readonly context = signal(3);

  // ── Diff state ────────────────────────────────────────────────────────────
  readonly diff        = signal<DiffMeta | null>(null);
  readonly revealed    = signal<Array<[number, number]>>([]);
  readonly shownLimit  = signal<Record<number, number>>({});
  readonly page        = signal(0);
  readonly jumpText    = signal('');
  readonly busy        = signal(false);
  readonly progressMsg = signal('');
  readonly statusMsg   = signal('');
  readonly statusIsError = signal(false);

  /** Row hex keyed by row index; `cacheVersion` bumps when either cache grows. */
  private readonly rowCache   = new Map<number, { a: string; b: string }>();
  private readonly asmCache   = new Map<string, string>();
  private readonly pending    = new Set<string>();
  private readonly cacheVersion = signal(0);

  readonly sourceOptions = computed(() => {
    const seen = new Set<string>();
    return [...this.snapshots(), ...this.browsed()].filter(s => !seen.has(s.path) && !!seen.add(s.path));
  });

  readonly usesLive   = computed(() => this.sideA() === LIVE || this.sideB() === LIVE);
  readonly canCompare = computed(() =>
    !!this.sideA() && !!this.sideB() && !(this.sideA() === LIVE && this.sideB() === LIVE));

  /** Changed-row runs widened by context and user-revealed rows, merged into hunks. */
  readonly hunks = computed((): Hunk[] => {
    const d = this.diff();
    if (!d) { return []; }
    const ctx = this.context();
    const iv: Array<[number, number, number, number]> = [];
    for (let i = 0; i < d.runs.length; i += 3) {
      const s = d.runs[i], e = d.runs[i + 1];
      iv.push([Math.max(d.minRow, s - ctx), Math.min(d.maxRow, e + ctx), e - s, d.runs[i + 2]]);
    }
    const rev = this.revealed();
    if (rev.length) {
      for (const [s, e] of rev) { iv.push([Math.max(d.minRow, s), Math.min(d.maxRow, e), 0, 0]); }
      iv.sort((x, y) => x[0] - y[0]);
    }

    const merged: Hunk[] = [];
    for (const [s, e, rows, bytes] of iv) {
      if (e <= s) { continue; }
      const last = merged[merged.length - 1];
      if (last && s <= last.end) {
        last.end = Math.max(last.end, e);
        last.changedRows  += rows;
        last.changedBytes += bytes;
      } else {
        merged.push({ start: s, end: e, changedRows: rows, changedBytes: bytes, gapBefore: 0, prevEnd: 0, hasPrev: false });
      }
    }
    // Revealed rows that no longer touch any change (e.g. after lowering context) are dropped.
    const out = merged.filter(h => h.changedRows > 0);
    out.forEach((h, i) => {
      h.hasPrev   = i > 0;
      h.prevEnd   = i > 0 ? out[i - 1].end : d.minRow;
      h.gapBefore = h.start - h.prevEnd;
    });
    return out;
  });

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.hunks().length / HUNKS_PER_PAGE)));

  readonly pageHunks = computed(() => {
    const start = this.page() * HUNKS_PER_PAGE;
    return this.hunks().slice(start, start + HUNKS_PER_PAGE);
  });

  /** Five GitHub-style diffstat blocks: share of the range that changed. */
  readonly diffstat = computed((): string[] => {
    const d = this.diff();
    if (!d) { return []; }
    const total  = Math.max(1, d.maxRow - d.minRow);
    const filled = d.changedRows === 0 ? 0 : Math.max(2, Math.min(5, Math.round((d.changedRows / total) * 5)));
    const del    = Math.ceil(filled / 2);
    return [0, 1, 2, 3, 4].map(i => i < del ? 'del' : i < filled ? 'add' : 'neutral');
  });

  readonly pageViews = computed((): HunkView[] => {
    const d = this.diff();
    if (!d) { return []; }
    this.cacheVersion();
    const limits = this.shownLimit();
    const view   = this.view();
    const layout = this.layout();
    return this.pageHunks().map(h => this.buildHunkView(d, h, limits[h.start] ?? ROWS_PER_HUNK, view, layout));
  });

  constructor() {
    const st = (this.vscode.getState() ?? {}) as SavedState;
    if (st.layout)  { this.layout.set(st.layout); }
    if (st.view)    { this.view.set(st.view); }
    if (st.context !== undefined) { this.context.set(st.context); }
    if (st.sideA)   { this.sideA.set(st.sideA); }
    if (st.sideB)   { this.sideB.set(st.sideB); }

    // Fetch any rows (and disassembly) the visible page needs.
    effect(() => {
      const hunks  = this.pageHunks();
      const limits = this.shownLimit();
      const view   = this.view();
      untracked(() => this.ensureData(hunks, limits, view));
    });
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

  browse(side: 'A' | 'B'): void { this.post({ type: 'browse', side }); }

  swap(): void {
    const a = this.sideA();
    this.sideA.set(this.sideB());
    this.sideB.set(a);
    this.saveState();
  }

  compare(): void {
    if (!this.canCompare() || this.busy()) { return; }
    const start = parseInt(this.rangeStart().replace(/^0x/i, ''), 16);
    const end   = parseInt(this.rangeEnd().replace(/^0x/i, ''), 16);
    if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start) {
      this._status('Range must be two hex addresses with end > start.', true);
      return;
    }
    this.busy.set(true);
    this.progressMsg.set('Starting…');
    this._status('');
    this.post({ type: 'compare', a: this.toSource(this.sideA()), b: this.toSource(this.sideB()), start, end });
  }

  setView(v: DiffView): void   { this.view.set(v); this.saveState(); }
  setLayout(l: DiffLayout): void { this.layout.set(l); this.saveState(); }
  setContext(n: number): void  { this.context.set(n); this.saveState(); }

  setPage(p: number): void {
    this.page.set(Math.max(0, Math.min(p, this.totalPages() - 1)));
    document.getElementById('dv-main')?.scrollTo({ top: 0 });
  }

  reveal(start: number, end: number): void {
    this.revealed.set([...this.revealed(), [start, end]]);
  }

  showMore(h: Hunk): void {
    const cur = this.shownLimit()[h.start] ?? ROWS_PER_HUNK;
    this.shownLimit.set({ ...this.shownLimit(), [h.start]: cur + ROWS_PER_HUNK * 4 });
  }

  isLastHunk(h: Hunk): boolean {
    const all = this.hunks();
    return all.length > 0 && all[all.length - 1] === h;
  }

  jump(): void {
    const addr = parseInt(this.jumpText().trim().replace(/^0x/i, ''), 16);
    const d = this.diff();
    if (!d || !Number.isFinite(addr)) { return; }
    const row = Math.floor((addr - d.base) / d.rowBytes);
    const idx = this.hunks().findIndex(h => h.end > row);
    if (idx < 0) { this._status(`No changes at or after ${this.hex8(addr)}.`, true); return; }
    this._status('');
    this.page.set(Math.floor(idx / HUNKS_PER_PAGE));
    const id = this.hunkId(this.hunks()[idx]);
    setTimeout(() => this.scrollToHunk(id), 30);
  }

  scrollToHunk(id: string): void {
    document.getElementById(id)?.scrollIntoView({ block: 'start', behavior: 'smooth' });
  }

  copyCodes(h: Hunk): void { this.post({ type: 'copyCodes', startRow: h.start, endRow: h.end }); }

  gotoAddr(addr: number): void { this.post({ type: 'gotoInEditor', address: this.hex8(addr) }); }

  saveState(): void {
    this.vscode.setState({
      layout: this.layout(), view: this.view(), context: this.context(),
      sideA: this.sideA(), sideB: this.sideB(),
    } satisfies SavedState);
  }

  // ── Template helpers ─────────────────────────────────────────────────────

  asInput(e: Event):  HTMLInputElement  { return e.target as HTMLInputElement; }
  asSelect(e: Event): HTMLSelectElement { return e.target as HTMLSelectElement; }

  optionLabel(s: SnapshotEntry): string {
    return s.timestamp ? `${s.label} — ${s.timestamp}` : s.label;
  }

  fmt(n: number): string { return n.toLocaleString(); }

  hex8(n: number): string { return (n >>> 0).toString(16).toUpperCase().padStart(8, '0'); }

  addrOf(row: number): number {
    const d = this.diff();
    return d ? d.base + row * d.rowBytes : row * 16;
  }

  // ── Rendering ────────────────────────────────────────────────────────────

  private hunkId(h: Hunk): string { return `dv-hunk-${h.start}`; }

  private buildHunkView(d: DiffMeta, h: Hunk, limit: number, view: DiffView, layout: DiffLayout): HunkView {
    const shownEnd = Math.min(h.end, h.start + limit);
    const header = `@@ ${this.hex8(this.addrOf(h.start))}–${this.hex8(this.addrOf(h.end) - 1)} @@  ` +
      `${this.fmt(h.changedRows)} changed row${h.changedRows === 1 ? '' : 's'}, ${this.fmt(h.changedBytes)} byte${h.changedBytes === 1 ? '' : 's'}`;

    // Collect line units: one per 16-byte row, or one per word for instructions.
    const units: Unit[] = [];
    let loading = false;
    for (let r = h.start; r < shownEnd; r++) {
      const row = this.rowCache.get(r);
      if (!row) { loading = true; break; }
      const addr = d.base + r * d.rowBytes;
      if (view === 'instr') {
        for (let w = 0; w * 8 < row.a.length; w++) {
          units.push({ addr: addr + w * 4, a: row.a.substr(w * 8, 8), b: row.b.substr(w * 8, 8) });
        }
      } else {
        units.push({ addr, a: row.a, b: row.b });
      }
    }

    const unified: UnifiedLine[] = [];
    const split:   SplitLine[]   = [];
    let changed: Unit[] = [];

    const flush = () => {
      if (!changed.length) { return; }
      if (layout === 'unified') {
        for (const u of changed) { unified.push(this.line('del', u, u.a, u.b, 'A')); }
        for (const u of changed) { unified.push(this.line('add', u, u.b, u.a, 'B')); }
      } else {
        for (const u of changed) { split.push(this.splitLine('chg', u)); }
      }
      changed = [];
    };

    for (const u of units) {
      if (u.a !== u.b) { changed.push(u); continue; }
      flush();
      if (layout === 'unified') { unified.push(this.line('ctx', u, u.a, u.b, 'A')); }
      else                      { split.push(this.splitLine('ctx', u)); }
    }
    flush();

    return {
      hunk: h, id: this.hunkId(h), header, unified, split, loading,
      hiddenRows: loading ? 0 : h.end - shownEnd,
    };
  }

  private line(kind: UnifiedLine['kind'], u: Unit, mine: string, other: string, side: 'A' | 'B'): UnifiedLine {
    const view = this.view();
    return {
      kind,
      addr:  this.hex8(u.addr),
      toks:  this.tokens(mine, other, view),
      extra: view === 'bytes' ? this.ascii(mine, other) : null,
      asm:   view === 'instr' ? this.asmFor(side, u.addr) : null,
    };
  }

  private splitLine(kind: SplitLine['kind'], u: Unit): SplitLine {
    const view = this.view();
    return {
      kind,
      addr: this.hex8(u.addr),
      l:    this.tokens(u.a, u.b, view),
      r:    this.tokens(u.b, u.a, view),
      le:   view === 'bytes' ? this.ascii(u.a, u.b) : null,
      re:   view === 'bytes' ? this.ascii(u.b, u.a) : null,
      lasm: view === 'instr' ? this.asmFor('A', u.addr) : null,
      rasm: view === 'instr' ? this.asmFor('B', u.addr) : null,
    };
  }

  /** Split a row's hex into display cells, flagging the ones that differ. */
  private tokens(mine: string, other: string, view: DiffView): Tok[] {
    const out: Tok[] = [];
    if (view === 'bytes') {
      for (let i = 0; i < mine.length; i += 2) {
        out.push({ t: mine.substr(i, 2).toUpperCase(), c: mine.substr(i, 2) !== other.substr(i, 2), g: i === 14 });
      }
    } else {
      for (let i = 0; i < mine.length; i += 8) {
        const w = mine.substr(i, 8);
        out.push({ t: leWord(w), c: w !== other.substr(i, 8) });
      }
    }
    return out;
  }

  private ascii(mine: string, other: string): Tok[] {
    const out: Tok[] = [];
    for (let i = 0; i < mine.length; i += 2) {
      const v = parseInt(mine.substr(i, 2), 16);
      out.push({ t: v >= 0x20 && v < 0x7F ? String.fromCharCode(v) : '.', c: mine.substr(i, 2) !== other.substr(i, 2) });
    }
    return out;
  }

  private asmFor(side: 'A' | 'B', addr: number): string {
    return this.asmCache.get(`${side}:${addr}`) ?? (this.connected() ? '…' : '');
  }

  // ── Data loading ─────────────────────────────────────────────────────────

  private ensureData(hunks: Hunk[], limits: Record<number, number>, view: DiffView): void {
    const d = this.diff();
    if (!d) { return; }
    const rowRanges: Array<[number, number]> = [];
    const asmRanges: Array<{ addr: number; count: number }> = [];

    for (const h of hunks) {
      const shownEnd = Math.min(h.end, h.start + (limits[h.start] ?? ROWS_PER_HUNK));
      let first = -1;
      for (let r = h.start; r < shownEnd; r++) {
        if (!this.rowCache.has(r)) { first = r; break; }
      }
      if (first >= 0) {
        const key = `r:${first}-${shownEnd}`;
        if (!this.pending.has(key)) { this.pending.add(key); rowRanges.push([first, shownEnd]); }
      }

      if (view === 'instr' && this.connected()) {
        for (let r = h.start; r < shownEnd; r += ASM_CHUNK_ROWS) {
          const addr = d.base + r * d.rowBytes;
          const key  = `a:${addr}`;
          if (this.asmCache.has(`A:${addr}`) || this.pending.has(key)) { continue; }
          this.pending.add(key);
          asmRanges.push({ addr, count: Math.min(ASM_CHUNK_ROWS, shownEnd - r) * (d.rowBytes / 4) });
        }
      }
    }

    if (rowRanges.length) { this.post({ type: 'getRows', diffId: d.diffId, ranges: rowRanges }); }
    if (asmRanges.length) {
      this.post({ type: 'disassemble', diffId: d.diffId, side: 'A', ranges: asmRanges });
      this.post({ type: 'disassemble', diffId: d.diffId, side: 'B', ranges: asmRanges });
    }
  }

  private toSource(value: string): DiffSource {
    if (value === LIVE) { return { kind: 'live' }; }
    const s = this.sourceOptions().find(o => o.path === value);
    return { kind: 'dump', path: value, label: s?.label ?? value.split(/[\\/]/).pop() ?? value };
  }

  private resetDiffState(): void {
    this.rowCache.clear();
    this.asmCache.clear();
    this.pending.clear();
    this.revealed.set([]);
    this.shownLimit.set({});
    this.page.set(0);
  }

  private _handleMessage(data: { type: string; [key: string]: unknown }): void {
    switch (data['type']) {
      case 'snapshotList': {
        const list = (data['snapshots'] as SnapshotEntry[]) ?? [];
        this.snapshots.set(list);
        this.connected.set(!!data['connected']);
        // Sensible first-run defaults: newest snapshot vs live RAM.
        if (!this.sideA() && list.length) { this.sideA.set(list[0].path); }
        if (!this.sideB() && this.connected()) { this.sideB.set(LIVE); }
        if (data['error']) { this._status(`Snapshot list: ${data['error']}`, true); }
        break;
      }

      case 'browseResult': {
        const snap = data['snapshot'] as SnapshotEntry | undefined;
        if (!snap) { break; }
        if (!this.sourceOptions().some(s => s.path === snap.path)) {
          this.browsed.set([...this.browsed(), snap]);
        }
        if (data['side'] === 'A') { this.sideA.set(snap.path); } else { this.sideB.set(snap.path); }
        this.saveState();
        break;
      }

      case 'preloadSnapshots': {
        const a = data['snapshotA'] as SnapshotEntry | null;
        const b = data['snapshotB'] as SnapshotEntry | null;
        const extra = [a, b].filter((s): s is SnapshotEntry => !!s && !this.sourceOptions().some(o => o.path === s.path));
        if (extra.length) { this.browsed.set([...this.browsed(), ...extra]); }
        if (a) { this.sideA.set(a.path); }
        if (b) { this.sideB.set(b.path); }
        break;
      }

      case 'progress':
        this.progressMsg.set(data['message'] as string ?? '');
        break;

      case 'diffResult':
        this.busy.set(false);
        this.resetDiffState();
        this.diff.set(data as unknown as DiffMeta);
        break;

      case 'diffError':
        this.busy.set(false);
        this._status((data['message'] as string) ?? 'Diff failed.', true);
        break;

      case 'rows': {
        if (data['diffId'] !== this.diff()?.diffId) { break; }
        const rowBytes = this.diff()!.rowBytes;
        const hexPerRow = rowBytes * 2;
        for (const r of (data['ranges'] as Array<{ startRow: number; a: string; b: string }>) ?? []) {
          for (let i = 0, row = r.startRow; i < r.a.length; i += hexPerRow, row++) {
            this.rowCache.set(row, { a: r.a.substr(i, hexPerRow), b: r.b.substr(i, hexPerRow) });
          }
        }
        this.pending.forEach(k => { if (k.startsWith('r:')) { this.pending.delete(k); } });
        this.cacheVersion.update(v => v + 1);
        break;
      }

      case 'disasm': {
        if (data['diffId'] !== this.diff()?.diffId) { break; }
        const side = data['side'] as string;
        for (const [addr, text] of Object.entries((data['asm'] as Record<string, string>) ?? {})) {
          this.asmCache.set(`${side}:${addr}`, text);
        }
        this.cacheVersion.update(v => v + 1);
        break;
      }

      case 'status':
        this._status(data['message'] as string ?? '', !!data['isError']);
        break;
    }
  }

  private _status(msg: string, isError = false): void {
    this.statusMsg.set(msg);
    this.statusIsError.set(isError);
  }
}

/** Little-endian word hex ("78563412") → numeric display ("12345678"). */
function leWord(hex: string): string {
  let out = '';
  for (let i = hex.length - 2; i >= 0; i -= 2) { out += hex.substr(i, 2); }
  return out.toUpperCase();
}
