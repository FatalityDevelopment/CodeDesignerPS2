import {
  Component,
  AfterViewInit,
  OnDestroy,
  ViewEncapsulation,
  signal,
  computed,
  viewChild,
  NgZone,
  inject,
  effect,
} from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import { PaginatedTableComponent, ColumnDef } from '../shared/components/index';
import { PorterPageComponent, PortResultPayload } from './porter-page.component';
import { SnapshotEntry } from '../disasm-panel/types';
import '../shared/styles/shared.css';
import './diff-viewer-page.css';
import '../disasm-editor/disasm-editor.css';

export interface DiffRow {
  address:  string;
  valueA:   string;
  valueB:   string;
  disasmA?: string;
  disasmB?: string;
}

type DiffTab  = 'diff' | 'porter';
type DiffMode = 'flat-flat' | 'live-flat';

@Component({
  selector: 'app-diff-viewer-page',
  standalone: true,
  imports: [PaginatedTableComponent, PorterPageComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="dv-outer">
  <!-- Tab bar -->
  <div class="dv-tab-bar">
    <button class="dv-tab" [class.dv-tab-active]="activeTab() === 'diff'"   (click)="activeTab.set('diff')">&#x2194; Memory Diff</button>
    <button class="dv-tab" [class.dv-tab-active]="activeTab() === 'porter'" (click)="activeTab.set('porter')">&#x2699; Cheat Code Porter</button>
  </div>

  <!-- ── Diff tab ──────────────────────────────────────────────────────── -->
  <div class="dv-panel" [class.dv-hidden]="activeTab() !== 'diff'">

    <!-- Mode selector -->
    <div class="dv-mode-bar">
      <span class="dv-mode-label">Diff mode:</span>
      <button class="dv-mode-btn" [class.dv-mode-active]="diffMode() === 'flat-flat'"
              (click)="diffMode.set('flat-flat')">
        &#x1F4C2; Snapshot ↔ Snapshot
      </button>
      <button class="dv-mode-btn" [class.dv-mode-active]="diffMode() === 'live-flat'"
              (click)="diffMode.set('live-flat')">
        &#x1F534; Live Memory ↔ Snapshot
      </button>
    </div>

    <!-- Source controls -->
    <div class="dv-controls">

      <!-- Snapshot A row — hidden in live-flat mode -->
      @if (diffMode() === 'flat-flat') {
        <div class="dv-source-row">
          <label class="dv-label">Snapshot A:</label>
          <select class="dv-snap-select" (change)="onSnapshotAChange($event)">
            <option value="">— select —</option>
            @for (s of availableSnapshots(); track s.path) {
              <option [value]="s.path" [selected]="snapshotA()?.path === s.path">{{ s.label }} ({{ s.isoName }})</option>
            }
          </select>
          <button class="lm-tool-btn" title="Browse for a .bin file" (click)="onBrowseSource('A')">&#x1F4C2;</button>
        </div>
      } @else {
        <div class="dv-source-row">
          <label class="dv-label">Source A:</label>
          <span class="dv-live-badge">&#x1F534; Live EE RAM</span>
          <button class="lm-tool-btn" [disabled]="isReadingLive()" (click)="onReadLiveMemory()">
            @if (isReadingLive()) { &#9203; Reading… } @else { &#x21BB; Read Now }
          </button>
          @if (liveReadStatus()) {
            <span class="dv-live-status" [class.dv-live-ok]="!liveReadError()">{{ liveReadStatus() }}</span>
          }
        </div>
      }

      <!-- Snapshot B row (always flat file) -->
      <div class="dv-source-row">
        <label class="dv-label">Snapshot B:</label>
        <select class="dv-snap-select" (change)="onSnapshotBChange($event)">
          <option value="">— select —</option>
          @for (s of availableSnapshots(); track s.path) {
            <option [value]="s.path" [selected]="snapshotB()?.path === s.path">{{ s.label }} ({{ s.isoName }})</option>
          }
        </select>
        <button class="lm-tool-btn" title="Browse for a .bin file" (click)="onBrowseSource('B')">&#x1F4C2;</button>
      </div>

      <!-- Action row -->
      <div class="dv-action-row">
        <input class="dv-goto-input" type="text" spellcheck="false" placeholder="Go to address…"
               [value]="gotoAddress()"
               (input)="gotoAddress.set(asInput($event).value)"
               (keydown.enter)="onGoto()" />
        <button class="lm-tool-btn" (click)="onGoto()" title="Navigate editor to address">&#x2192; Goto</button>
        <label class="dv-toggle" title="Toggle diff highlighting">
          <input type="checkbox" [checked]="showDiff()" (change)="showDiff.set(asInput($event).checked)" />
          Highlight changes
        </label>
        <button class="lm-tool-btn" [disabled]="!canDiff() || isDiffing()"
                (click)="onStartDiff()">
          @if (isDiffing()) { &#9203; Comparing… } @else { &#x2194; Compare }
        </button>
      </div>
    </div>

    @if (statusMsg()) {
      <div class="dv-status" [class.dv-status-error]="statusIsError()">{{ statusMsg() }}</div>
    }

    <app-paginated-table
      [rows]="pagedDiffRows()"
      [columns]="diffColumns"
      [totalCount]="filteredDiffRows().length"
      [currentPage]="currentPage()"
      [pageSize]="pageSize()"
      [totalPages]="totalPages()"
      [filterText]="diffFilterText()"
      [sortColumn]="sortColumn()"
      [sortDirection]="sortDirection()"
      [filterPlaceholder]="'Filter by address or value…'"
      [rowTemplate]="diffRowTpl"
      (filterTextChange)="diffFilterText.set($event); currentPage.set(0)"
      (pageChange)="currentPage.set($event)"
      (pageSizeChange)="pageSize.set($event); currentPage.set(0)"
      (sortChange)="onSortChange($event)">
    </app-paginated-table>

    <ng-template #diffRowTpl let-row>
      <tr [class.dv-changed-row]="showDiff()">
        <td class="dv-col-addr lm-mono">
          <button class="lm-row-btn lm-goto-btn" title="Go to in editor"
                  (click)="onGotoRow($any(row).address)">&#x2192;</button>
          {{ $any(row).address }}
        </td>
        <td class="dv-col-val-a lm-mono">{{ $any(row).valueA }}</td>
        <td class="dv-col-val-b lm-mono dv-changed">{{ $any(row).valueB }}</td>
        <td class="dv-col-disasm lm-muted">{{ $any(row).disasmA }}</td>
        <td class="dv-col-disasm lm-muted">{{ $any(row).disasmB }}</td>
      </tr>
    </ng-template>
  </div>

  <!-- ── Porter tab ────────────────────────────────────────────────────── -->
  <div class="dv-panel dv-porter-panel" [class.dv-hidden]="activeTab() !== 'porter'">
    <app-porter-page #porterComp
      (browseSource)="onPorterBrowseSource()"
      (browseTarget)="onPorterBrowseTarget()"
      (port$)="onPortRequest($event)" />
  </div>
</div>
`,
})
export class DiffViewerPageComponent implements AfterViewInit, OnDestroy {
  private readonly vscode    = getVsCodeApi();
  private readonly zone      = inject(NgZone);
  private readonly porterRef = viewChild<PorterPageComponent>('porterComp');

  constructor() {
    // Keep porter source/target in sync with whatever is selected in the diff tab.
    // porterRef() is undefined before view init; when it becomes available the effect
    // re-runs and pushes the current snapshot paths into the porter automatically.
    effect(() => { this.porterRef()?.setSourcePath(this.snapshotA()?.path ?? ''); });
    effect(() => { this.porterRef()?.setTargetPath(this.snapshotB()?.path ?? ''); });
  }

  readonly activeTab          = signal<DiffTab>('diff');
  readonly diffMode           = signal<DiffMode>('flat-flat');
  readonly availableSnapshots = signal<SnapshotEntry[]>([]);
  readonly snapshotA          = signal<SnapshotEntry | null>(null);
  readonly snapshotB          = signal<SnapshotEntry | null>(null);
  readonly liveMemoryReady    = signal(false);
  readonly isReadingLive      = signal(false);
  readonly liveReadStatus     = signal('');
  readonly liveReadError      = signal(false);
  readonly diffResults        = signal<DiffRow[]>([]);
  readonly diffFilterText     = signal('');
  readonly currentPage        = signal(0);
  readonly pageSize           = signal(100);
  readonly sortColumn         = signal<string | null>('address');
  readonly sortDirection      = signal<'asc' | 'desc'>('asc');
  readonly gotoAddress        = signal('');
  readonly showDiff           = signal(true);
  readonly isDiffing          = signal(false);
  readonly statusMsg          = signal('');
  readonly statusIsError      = signal(false);

  readonly canDiff = computed(() => {
    if (this.diffMode() === 'flat-flat') { return !!this.snapshotA() && !!this.snapshotB(); }
    return this.liveMemoryReady() && !!this.snapshotB();
  });

  readonly diffColumns: ColumnDef[] = [
    { key: 'address', label: 'Address',    sortable: true  },
    { key: 'valueA',  label: 'Source A',   sortable: false },
    { key: 'valueB',  label: 'Snapshot B', sortable: false },
    { key: 'disasmA', label: 'Disasm A',   sortable: false },
    { key: 'disasmB', label: 'Disasm B',   sortable: false },
  ];

  readonly filteredDiffRows = computed(() => {
    const q = this.diffFilterText().toLowerCase();
    if (!q) { return this.diffResults(); }
    return this.diffResults().filter(r =>
      r.address.toLowerCase().includes(q) ||
      r.valueA.toLowerCase().includes(q)  ||
      r.valueB.toLowerCase().includes(q)
    );
  });

  readonly sortedDiffRows = computed(() => {
    const col = this.sortColumn();
    const dir = this.sortDirection();
    const list = [...this.filteredDiffRows()];
    if (col !== 'address') { return list; }
    return list.sort((a, b) => {
      const av = parseInt(a.address, 16);
      const bv = parseInt(b.address, 16);
      return dir === 'asc' ? av - bv : bv - av;
    });
  });

  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.sortedDiffRows().length / this.pageSize()))
  );

  readonly pagedDiffRows = computed((): Record<string, unknown>[] => {
    const start = this.currentPage() * this.pageSize();
    return this.sortedDiffRows().slice(start, start + this.pageSize()) as unknown as Record<string, unknown>[];
  });

  ngAfterViewInit(): void {
    window.addEventListener('message', (event: MessageEvent) => {
      const data = event.data as { type: string; [key: string]: unknown };
      this.zone.run(() => { this._handleMessage(data); });
    });
    this.vscode.postMessage({ type: 'ready' });
  }

  ngOnDestroy(): void { /* event listener cleaned up on page unload */ }

  onSnapshotAChange(event: Event): void {
    const path = this.asSelect(event).value;
    this.snapshotA.set(this.availableSnapshots().find(s => s.path === path) ?? null);
  }

  onSnapshotBChange(event: Event): void {
    const path = this.asSelect(event).value;
    this.snapshotB.set(this.availableSnapshots().find(s => s.path === path) ?? null);
  }

  onBrowseSource(side: 'A' | 'B'): void {
    this.vscode.postMessage({ type: side === 'A' ? 'browseSource' : 'browseTarget' });
  }

  onReadLiveMemory(): void {
    this.isReadingLive.set(true);
    this.liveMemoryReady.set(false);
    this.liveReadStatus.set('');
    this.liveReadError.set(false);
    this.vscode.postMessage({ type: 'readLiveMemory' });
  }

  onStartDiff(): void {
    const b = this.snapshotB();
    if (!b) { return; }
    this.isDiffing.set(true);
    this.diffResults.set([]);
    this._showStatus('');
    if (this.diffMode() === 'flat-flat') {
      const a = this.snapshotA();
      if (!a) { return; }
      this.vscode.postMessage({ type: 'startDiff', pathA: a.path, pathB: b.path });
    } else {
      this.vscode.postMessage({ type: 'startLiveDiff', pathB: b.path });
    }
  }

  onGoto(): void {
    const addr = this.gotoAddress().trim();
    if (!addr) { return; }
    this.vscode.postMessage({ type: 'gotoInEditor', address: addr });
  }

  onGotoRow(address: string): void {
    this.vscode.postMessage({ type: 'gotoInEditor', address });
  }

  onSortChange(evt: { column: string; direction: 'asc' | 'desc' }): void {
    this.sortColumn.set(evt.column);
    this.sortDirection.set(evt.direction);
    this.currentPage.set(0);
  }

  // ── Porter tab handlers ──────────────────────────────────────────────────

  onPorterBrowseSource(): void {
    this.vscode.postMessage({ type: 'porterBrowseSource' });
  }

  onPorterBrowseTarget(): void {
    this.vscode.postMessage({ type: 'porterBrowseTarget' });
  }

  onPortRequest(req: { codes: string; sourcePath: string; targetPath: string }): void {
    this.vscode.postMessage({ type: 'portCheats', codes: req.codes, sourcePath: req.sourcePath, targetPath: req.targetPath });
  }

  // ── Helpers ──────────────────────────────────────────────────────────────

  asInput(e: Event):  HTMLInputElement    { return e.target as HTMLInputElement; }
  asSelect(e: Event): HTMLSelectElement   { return e.target as HTMLSelectElement; }

  private _handleMessage(data: { type: string; [key: string]: unknown }): void {
    switch (data['type']) {
      case 'snapshotList':
        this.availableSnapshots.set((data['snapshots'] as SnapshotEntry[]) ?? []);
        break;

      case 'diffResults':
        this.isDiffing.set(false);
        this.diffResults.set((data['rows'] as DiffRow[]) ?? []);
        this.currentPage.set(0);
        this._showStatus(`${this.diffResults().length} changed addresses found.`);
        break;

      case 'diffError':
        this.isDiffing.set(false);
        this._showStatus((data['message'] as string) ?? 'Diff failed.', true);
        break;

      case 'liveMemoryReady':
        this.isReadingLive.set(false);
        this.liveMemoryReady.set(true);
        this.liveReadStatus.set('Live EE RAM captured ✓');
        this.liveReadError.set(false);
        break;

      case 'liveMemoryError':
        this.isReadingLive.set(false);
        this.liveMemoryReady.set(false);
        this.liveReadStatus.set((data['message'] as string) ?? 'Failed to read live memory.');
        this.liveReadError.set(true);
        break;

      case 'preloadSnapshots': {
        const snapA = data['snapshotA'] as SnapshotEntry | null;
        const snapB = data['snapshotB'] as SnapshotEntry | null;
        const cur   = this.availableSnapshots();
        const toAdd = [snapA, snapB].filter((s): s is SnapshotEntry =>
          !!s && !cur.some(e => e.path === s.path)
        );
        if (toAdd.length) { this.availableSnapshots.set([...cur, ...toAdd]); }
        if (snapA) { this.snapshotA.set(snapA); }
        if (snapB) { this.snapshotB.set(snapB); }
        break;
      }

      case 'browseResult': {
        const side = data['side'] as 'A' | 'B';
        const snap = data['snapshot'] as SnapshotEntry | undefined;
        if (snap) {
          // Add to the list so the <select> option renders and [selected] binding works.
          if (!this.availableSnapshots().some(s => s.path === snap.path)) {
            this.availableSnapshots.set([...this.availableSnapshots(), snap]);
          }
          if (side === 'A') { this.snapshotA.set(snap); }
          else              { this.snapshotB.set(snap); }
        }
        break;
      }

      case 'porterBrowseSourceResult':
        this.porterRef()?.setSourcePath(data['filePath'] as string ?? '');
        break;

      case 'porterBrowseTargetResult':
        this.porterRef()?.setTargetPath(data['filePath'] as string ?? '');
        break;

      case 'portCheatsResult':
        this.porterRef()?.setResult(data as unknown as PortResultPayload);
        break;
    }
  }

  private _showStatus(msg: string, isError = false): void {
    this.statusMsg.set(msg);
    this.statusIsError.set(isError);
  }
}
