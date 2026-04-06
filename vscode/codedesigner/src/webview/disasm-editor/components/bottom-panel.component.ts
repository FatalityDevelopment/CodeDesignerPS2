import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  input,
  output,
  signal,
  computed,
  OnDestroy,
} from '@angular/core';
import { CallStackEntry, ModuleEntry, ThreadEntry, PineWatchInfo } from '../state/types';

export type BottomTab = 'callstack' | 'threads' | 'breakpoints' | 'modules' | 'watches';

/**
 * BottomPanelComponent - Displays callstack, threads, breakpoints, and modules in 4 tabs.
 *
 * Uses signal inputs for reactive updates without zone.js.
 */
@Component({
  selector: 'app-bottom-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div id="bottom-panel" class="bottom-panel" [class.visible]="visible()" [style.height.px]="panelHeight()">
      <div class="bottom-resize-handle" (mousedown)="onResizeMouseDown($event)"></div>
      <div class="bottom-tabs">
        <button class="bottom-tab" [class.active]="activeTab() === 'callstack'" (click)="selectTab('callstack')">Call Stack</button>
        <button class="bottom-tab" [class.active]="activeTab() === 'threads'" (click)="selectTab('threads')">Threads</button>
        <button class="bottom-tab" [class.active]="activeTab() === 'breakpoints'" (click)="selectTab('breakpoints')">Breakpoints</button>
        <button class="bottom-tab" [class.active]="activeTab() === 'watches'" (click)="selectTab('watches')">Watches</button>
        <button class="bottom-tab" [class.active]="activeTab() === 'modules'" (click)="selectTab('modules')">Modules</button>
        <button class="bottom-refresh-btn" (click)="onRefreshClick()">⟳</button>
      </div>

      @if (activeTab() === 'callstack') {
        <div class="bottom-tab-content active">
          <table class="dbg-table">
            <thead><tr>
              <th class="dbg-th">#</th>
              <th class="dbg-th">PC</th>
              <th class="dbg-th">Function</th>
            </tr></thead>
            <tbody>
              @if (!callStack().length) {
                <tr><td colspan="3" class="dbg-td-empty">No stack frames available</td></tr>
              }
              @for (frame of callStack(); track frame.pc; let idx = $index) {
                <tr class="dbg-table-row clickable" (click)="navigateToAddress.emit(frame.pc)">
                  <td class="dbg-td-num">{{ idx }}</td>
                  <td class="dbg-td-addr mono">0x{{ toHex(frame.pc) }}</td>
                  <td class="dbg-td-func">{{ frame.functionLabel ?? '—' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      @if (activeTab() === 'threads') {
        <div class="bottom-tab-content active">
          <table class="dbg-table">
            <thead><tr>
              <th class="dbg-th">TID</th>
              <th class="dbg-th">Status</th>
              <th class="dbg-th">PC</th>
              <th class="dbg-th">SP</th>
              <th class="dbg-th">Priority</th>
              <th class="dbg-th">Entry</th>
            </tr></thead>
            <tbody>
              @if (!threads().length) {
                <tr><td colspan="6" class="dbg-td-empty">No threads</td></tr>
              }
              @for (t of threads(); track t.id) {
                <tr class="dbg-table-row clickable" (click)="selectThread.emit(t.id); navigateToAddress.emit(t.pc)">
                  <td class="dbg-td-num">{{ t.id }}</td>
                  <td class="dbg-td-status">{{ t.status }}</td>
                  <td class="dbg-td-addr mono">0x{{ toHex(t.pc) }}</td>
                  <td class="dbg-td-addr mono">0x{{ toHex(t.sp) }}</td>
                  <td class="dbg-td-num">{{ t.priority }}</td>
                  <td class="dbg-td-addr mono">0x{{ toHex(t.entryPc) }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      @if (activeTab() === 'breakpoints') {
        <div class="bottom-tab-content active">
          <div class="breakpoints-header">
            <button class="bp-clear-all-btn" (click)="clearAllBreakpoints.emit()">Clear All</button>
          </div>
          <table class="dbg-table">
            <thead><tr>
              <th class="dbg-th">Address</th>
              <th class="dbg-th">Type</th>
              <th class="dbg-th">Condition</th>
              <th class="dbg-th">Enabled</th>
              <th class="dbg-th"></th>
            </tr></thead>
            <tbody>
              @if (!breakpoints().size) {
                <tr><td colspan="5" class="dbg-td-empty">No breakpoints set</td></tr>
              }
              @for (addr of sortedBreakpoints(); track addr) {
                <tr class="dbg-table-row clickable" (click)="navigateToAddress.emit(addr)">
                  <td class="dbg-td-addr dbg-bp-addr mono">0x{{ toHex(addr) }}</td>
                  <td class="dbg-td-type">Execute</td>
                  <td class="dbg-td-cond">—</td>
                  <td class="dbg-td-enabled">
                    <input type="checkbox" class="dbg-bp-checkbox dbg-bp-enabled" checked
                      (click)="$event.stopPropagation(); toggleBreakpoint.emit(addr)" />
                  </td>
                  <td class="dbg-td-del">
                    <button class="dbg-bp-del-btn"
                      (click)="$event.stopPropagation(); removeBreakpoint.emit(addr)">✕</button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      @if (activeTab() === 'watches') {
        <div class="bottom-tab-content active">
          <div class="breakpoints-header">
            <button class="bp-clear-all-btn" (click)="clearAllWatches.emit()">Clear All</button>
          </div>
          <table class="dbg-table">
            <thead><tr>
              <th class="dbg-th">CPU</th>
              <th class="dbg-th">Range</th>
              <th class="dbg-th">Condition</th>
              <th class="dbg-th"></th>
            </tr></thead>
            <tbody>
              @if (!sortedWatches().length) {
                <tr><td colspan="4" class="dbg-td-empty">No watchpoints — right-click an instruction to add one</td></tr>
              }
              @for (w of sortedWatches(); track w.Start) {
                <tr class="dbg-table-row clickable" (click)="navigateToWatchAddress.emit(parseWatchStart(w))">
                  <td class="dbg-td-type mono">{{ w.Cpu }}</td>
                  <td class="dbg-td-addr mono">{{ w.Start }}–{{ w.End }}</td>
                  <td class="dbg-td-type mono">{{ w.Cond }}</td>
                  <td class="dbg-td-del">
                    <button class="dbg-bp-del-btn"
                      (click)="$event.stopPropagation(); removeWatch.emit(w)">✕</button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      @if (activeTab() === 'modules') {
        <div class="bottom-tab-content active">
          <table class="dbg-table">
            <thead><tr>
              <th class="dbg-th">Name</th>
              <th class="dbg-th">Version</th>
              <th class="dbg-th">Entry</th>
              <th class="dbg-th">GP</th>
              <th class="dbg-th">Text</th>
              <th class="dbg-th">Data</th>
              <th class="dbg-th">BSS</th>
            </tr></thead>
            <tbody>
              @if (!modules().length) {
                <tr><td colspan="7" class="dbg-td-empty">No modules</td></tr>
              }
              @for (m of modules(); track m.name) {
                <tr class="dbg-table-row clickable" (click)="navigateToAddress.emit(parseHex(m.textAddr))">
                  <td class="dbg-td-func">{{ m.name }}</td>
                  <td class="dbg-td-num">{{ formatVersion(m.version) }}</td>
                  <td class="dbg-td-addr mono">{{ m.entry }}</td>
                  <td class="dbg-td-addr mono">{{ m.gp }}</td>
                  <td class="dbg-td-num">{{ m.textSize }}</td>
                  <td class="dbg-td-num">{{ m.dataSize }}</td>
                  <td class="dbg-td-num">{{ m.bssSize }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>
  `,
})
export class BottomPanelComponent implements OnDestroy {
  // ═══════════════════════════════════════════════════════════════════════════
  // Resize constants
  // ═══════════════════════════════════════════════════════════════════════════

  private readonly MIN_HEIGHT = 80;
  private readonly MAX_HEIGHT = 700;
  private readonly STORAGE_KEY = 'codedesigner.bottomPanel.height';

  // Inputs
  readonly visible = input(false);
  readonly callStack = input<CallStackEntry[]>([]);
  readonly threads = input<ThreadEntry[]>([]);
  readonly breakpoints = input<Set<number>>(new Set());
  readonly modules = input<ModuleEntry[]>([]);
  readonly watches = input<PineWatchInfo[]>([]);

  // Outputs
  readonly navigateToAddress = output<number>();
  readonly toggleBreakpoint = output<number>();
  readonly removeBreakpoint = output<number>();
  readonly clearAllBreakpoints = output<void>();
  readonly removeWatch = output<PineWatchInfo>();
  readonly clearAllWatches = output<void>();
  readonly navigateToWatchAddress = output<number>();
  readonly selectThread = output<number>();
  readonly refreshTab = output<BottomTab>();
  readonly tabSelected = output<BottomTab>();

  // Internal state
  readonly activeTab = signal<BottomTab>('callstack');

  /** Current panel height in pixels, persisted to localStorage */
  readonly panelHeight = signal(this._loadHeight());

  // Sorted breakpoints array for @for iteration
  readonly sortedBreakpoints = computed(() =>
    Array.from(this.breakpoints()).sort((a, b) => a - b),
  );

  // Sorted watches array for @for iteration
  readonly sortedWatches = computed(() =>
    [...this.watches()].sort((a, b) => parseInt(a.Start, 16) - parseInt(b.Start, 16)),
  );

  selectTab(tab: BottomTab): void {
    this.activeTab.set(tab);
    this.tabSelected.emit(tab);
  }

  onRefreshClick(): void {
    this.refreshTab.emit(this.activeTab());
  }

  toHex(n: number, pad = 8): string {
    return n.toString(16).toUpperCase().padStart(pad, '0');
  }

  parseHex(s: string): number {
    return parseInt(s.replace(/^0x/i, ''), 16) || 0;
  }

  parseWatchStart(w: PineWatchInfo): number {
    return parseInt(w.Start, 16) || 0;
  }

  /**
   * Format a BCD-encoded IOP module version as "Major.Minor".
   * IOP versions are stored as a 16-bit BCD value where the high byte is the
   * major version and the low byte is the minor version (e.g. 0x0101 → "1.1").
   */
  formatVersion(v: number): string {
    return `${(v >> 8) & 0xFF}.${v & 0xFF}`;
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Resize logic
  // ═══════════════════════════════════════════════════════════════════════════

  private _resizeCleanup: (() => void) | null = null;

  ngOnDestroy(): void {
    this._resizeCleanup?.();
  }

  onResizeMouseDown(event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();
    const startY = event.clientY;
    const startHeight = this.panelHeight();
    document.body.style.cursor = 'ns-resize';
    document.body.style.userSelect = 'none';

    const onMouseMove = (e: MouseEvent) => {
      // Moving up (negative delta) increases height; moving down decreases it.
      const newHeight = Math.max(this.MIN_HEIGHT, Math.min(this.MAX_HEIGHT, startHeight + (startY - e.clientY)));
      this.panelHeight.set(newHeight);
    };

    const onMouseUp = () => {
      cleanup();
      localStorage.setItem(this.STORAGE_KEY, String(this.panelHeight()));
    };

    const cleanup = () => {
      document.removeEventListener('mousemove', onMouseMove);
      document.removeEventListener('mouseup', onMouseUp);
      document.body.style.cursor = '';
      document.body.style.userSelect = '';
      this._resizeCleanup = null;
    };

    this._resizeCleanup = cleanup;
    document.addEventListener('mousemove', onMouseMove);
    document.addEventListener('mouseup', onMouseUp);
  }

  private _loadHeight(): number {
    try {
      const saved = localStorage.getItem(this.STORAGE_KEY);
      if (saved) {
        const h = parseInt(saved, 10);
        if (!isNaN(h)) return Math.max(this.MIN_HEIGHT, Math.min(this.MAX_HEIGHT, h));
      }
    } catch { /* ignore */ }
    return 200;
  }
}

