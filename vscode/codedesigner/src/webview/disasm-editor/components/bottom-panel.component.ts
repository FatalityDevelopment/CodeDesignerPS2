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
import { CallStackEntry, ModuleEntry, ThreadEntry, PineWatchInfo, BreakpointInfo, BreakpointField, MemWatchRow, MemWatchType } from '../state/types';
import { IconComponent } from '../../shared/icon.component';

export type BottomTab = 'callstack' | 'threads' | 'breakpoints' | 'modules' | 'watches' | 'memory';

/**
 * BottomPanelComponent - Call stack, threads, breakpoints, PINE watchpoints,
 * the typed memory watch list and IOP modules.
 *
 * Uses signal inputs for reactive updates without zone.js.
 */
@Component({
  selector: 'app-bottom-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  imports: [IconComponent],
  template: `
    <div id="bottom-panel" class="bottom-panel" [class.visible]="visible()" [style.height.px]="panelHeight()">
      <div class="bottom-resize-handle" (mousedown)="onResizeMouseDown($event)"></div>
      <div class="bottom-tabs">
        <button class="bottom-tab" [class.active]="activeTab() === 'callstack'" (click)="selectTab('callstack')">Call Stack</button>
        <button class="bottom-tab" [class.active]="activeTab() === 'threads'" (click)="selectTab('threads')">Threads</button>
        <button class="bottom-tab" [class.active]="activeTab() === 'breakpoints'" (click)="selectTab('breakpoints')">Breakpoints</button>
        <button class="bottom-tab" [class.active]="activeTab() === 'watches'" (click)="selectTab('watches')">Watchpoints</button>
        <button class="bottom-tab" [class.active]="activeTab() === 'memory'" (click)="selectTab('memory')">Memory</button>
        <button class="bottom-tab" [class.active]="activeTab() === 'modules'" (click)="selectTab('modules')">Modules</button>
        <button class="bottom-refresh-btn" title="Refresh" (click)="onRefreshClick()"><app-icon name="refreshcw" [size]="12" /></button>
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
            <button class="bp-clear-all-btn" (click)="clearAllBreakpoints.emit()"><app-icon name="trash2" [size]="12" /><span>Clear All</span></button>
            <button class="bp-clear-all-btn" title="Enable every breakpoint" (click)="setAllBreakpointsEnabled.emit(true)"><span>Enable All</span></button>
            <button class="bp-clear-all-btn" title="Disable every breakpoint (they stay listed)" (click)="setAllBreakpointsEnabled.emit(false)"><span>Disable All</span></button>
            <button class="bp-clear-all-btn" title="Reset every hit counter" (click)="resetBreakpointHits.emit(null)"><app-icon name="refreshcw" [size]="12" /><span>Reset Hits</span></button>
            <span class="bp-help" [title]="helpText">?</span>
            @if (runToAddress() !== null) {
              <span class="bp-runto">Running to 0x{{ toHex(runToAddress()!) }}…</span>
            }
          </div>
          <table class="dbg-table bp-table">
            <thead><tr>
              <th class="dbg-th" title="Enabled breakpoints are armed in the emulator">On</th>
              <th class="dbg-th">Address</th>
              <th class="dbg-th">Type</th>
              <th class="dbg-th">Condition</th>
              <th class="dbg-th">Hit count</th>
              <th class="dbg-th">Log message</th>
              <th class="dbg-th">Hits</th>
              <th class="dbg-th"></th>
            </tr></thead>
            <tbody>
              @if (!breakpoints().length) {
                <tr><td colspan="8" class="dbg-td-empty">No breakpoints set — F9 or right-click an instruction</td></tr>
              }
              @for (bp of breakpoints(); track bp.address) {
                <tr class="dbg-table-row clickable" [class.bp-row-disabled]="!bp.enabled" (click)="navigateToAddress.emit(bp.address)">
                  <td class="dbg-td-enabled">
                    <input type="checkbox" class="dbg-bp-checkbox dbg-bp-enabled" [checked]="bp.enabled"
                      title="Enable / disable (the breakpoint stays in the list)"
                      (click)="$event.stopPropagation(); updateBreakpoint.emit({ address: bp.address, patch: { enabled: !bp.enabled } })" />
                  </td>
                  <td class="dbg-td-addr dbg-bp-addr mono">0x{{ toHex(bp.address) }}</td>
                  <td class="dbg-td-type">{{ bp.logMessage ? 'Log' : 'Execute' }}</td>
                  <td class="dbg-td-cond">
                    <input class="bp-field mono" [class.error]="fieldHasError(bp, 'Condition')" [value]="bp.condition"
                      placeholder="always" spellcheck="false" [title]="bp.error ?? 'Stop only when this expression is non-zero'"
                      (click)="$event.stopPropagation()"
                      (keydown)="onFieldKeydown($event)"
                      (change)="onFieldChange(bp, 'condition', $event)" />
                  </td>
                  <td class="dbg-td-hitcond">
                    <input class="bp-field bp-field-narrow mono" [class.error]="fieldHasError(bp, 'Hit count')" [value]="bp.hitCondition"
                      placeholder="any" spellcheck="false" title="5 or >=5 (from the 5th hit), ==5, >5, %5 (every 5th)"
                      (click)="$event.stopPropagation()"
                      (keydown)="onFieldKeydown($event)"
                      (change)="onFieldChange(bp, 'hitCondition', $event)" />
                  </td>
                  <td class="dbg-td-log">
                    <input class="bp-field mono" [value]="bp.logMessage"
                      placeholder="(break)" spellcheck="false" title="Log point: print this message ({expr} interpolated) and continue"
                      (click)="$event.stopPropagation()"
                      (keydown)="onFieldKeydown($event)"
                      (change)="onFieldChange(bp, 'logMessage', $event)" />
                  </td>
                  <td class="dbg-td-num">
                    <button class="bp-hits-btn mono" title="Reset hit count" (click)="$event.stopPropagation(); resetBreakpointHits.emit(bp.address)">{{ bp.hits }}</button>
                  </td>
                  <td class="dbg-td-del">
                    <button class="dbg-bp-del-btn"
                      title="Remove Breakpoint"
                      (click)="$event.stopPropagation(); removeBreakpoint.emit(bp.address)"><app-icon name="trash2" [size]="12" /></button>
                  </td>
                </tr>
                @if (bp.error) {
                  <tr class="bp-error-row"><td></td><td colspan="7" class="bp-error-text">{{ bp.error }}</td></tr>
                }
              }
            </tbody>
          </table>
        </div>
      }

      @if (activeTab() === 'memory') {
        <div class="bottom-tab-content active">
          <div class="breakpoints-header memwatch-add">
            <input #mwExpr class="bp-field memwatch-expr mono" placeholder="Address or pointer chain: 0x00123456, [[playerPtr]+0x10]+4, $sp+8"
              spellcheck="false" (keydown)="onFieldKeydown($event)" (keydown.enter)="addMemWatch(mwExpr, mwType)" />
            <select #mwType class="bp-field memwatch-type" title="Value type">
              @for (t of memWatchTypes; track t.id) { <option [value]="t.id">{{ t.label }}</option> }
            </select>
            <button class="bp-clear-all-btn" (click)="addMemWatch(mwExpr, mwType)"><app-icon name="plus" [size]="12" /><span>Add</span></button>
            <button class="bp-clear-all-btn" (click)="memWatchClear.emit()"><app-icon name="trash2" [size]="12" /><span>Clear All</span></button>
          </div>
          <table class="dbg-table">
            <thead><tr>
              <th class="dbg-th">Expression</th>
              <th class="dbg-th">Type</th>
              <th class="dbg-th">Address</th>
              <th class="dbg-th">Value</th>
              <th class="dbg-th"></th>
            </tr></thead>
            <tbody>
              @if (!memWatches().length) {
                <tr><td colspan="5" class="dbg-td-empty">No memory watches — add an address or pointer chain above</td></tr>
              }
              @for (w of memWatches(); track w.id) {
                <tr class="dbg-table-row" [class.clickable]="w.address !== undefined" (click)="onMemWatchRowClick(w)">
                  <td class="dbg-td-cond">
                    <input class="bp-field mono" [value]="w.expression" spellcheck="false"
                      (click)="$event.stopPropagation()" (keydown)="onFieldKeydown($event)"
                      (change)="memWatchUpdate.emit({ id: w.id, patch: { expression: inputValue($event) } })" />
                  </td>
                  <td class="dbg-td-type">
                    <select class="bp-field memwatch-type" (click)="$event.stopPropagation()"
                      (change)="onMemWatchTypeChange(w, $event)">
                      @for (t of memWatchTypes; track t.id) { <option [value]="t.id" [selected]="t.id === w.type">{{ t.label }}</option> }
                    </select>
                  </td>
                  <td class="dbg-td-addr mono">{{ w.address !== undefined ? '0x' + toHex(w.address) : '—' }}</td>
                  <td class="dbg-td-func mono">
                    @if (w.error) {
                      <span class="bp-error-text">{{ w.error }}</span>
                    } @else {
                      <input class="bp-field memwatch-value mono" [value]="w.value ?? ''" spellcheck="false"
                        title="Edit and press Enter to write memory"
                        (click)="$event.stopPropagation()" (keydown)="onFieldKeydown($event)"
                        (change)="memWatchWrite.emit({ id: w.id, value: writableValue($event) })" />
                    }
                  </td>
                  <td class="dbg-td-del">
                    <button class="dbg-bp-del-btn" title="Remove watch"
                      (click)="$event.stopPropagation(); memWatchRemove.emit(w.id)"><app-icon name="trash2" [size]="12" /></button>
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
            <button class="bp-clear-all-btn" (click)="clearAllWatches.emit()"><app-icon name="trash2" [size]="12" /><span>Clear All</span></button>
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
                      title="Remove Watchpoint"
                      (click)="$event.stopPropagation(); removeWatch.emit(w)"><app-icon name="trash2" [size]="12" /></button>
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
  readonly breakpoints = input<BreakpointInfo[]>([]);
  readonly runToAddress = input<number | null>(null);
  readonly memWatches = input<MemWatchRow[]>([]);
  readonly modules = input<ModuleEntry[]>([]);
  readonly watches = input<PineWatchInfo[]>([]);

  // Outputs
  readonly navigateToAddress = output<number>();
  readonly toggleBreakpoint = output<number>();
  readonly removeBreakpoint = output<number>();
  readonly clearAllBreakpoints = output<void>();
  readonly updateBreakpoint = output<{ address: number; patch: Partial<Pick<BreakpointInfo, 'enabled' | BreakpointField>> }>();
  readonly setAllBreakpointsEnabled = output<boolean>();
  /** Reset one breakpoint's hit count (address) or all of them (null). */
  readonly resetBreakpointHits = output<number | null>();
  readonly memWatchAdd = output<{ expression: string; type: MemWatchType }>();
  readonly memWatchUpdate = output<{ id: string; patch: Partial<Pick<MemWatchRow, 'expression' | 'type'>> }>();
  readonly memWatchRemove = output<string>();
  readonly memWatchClear = output<void>();
  readonly memWatchWrite = output<{ id: string; value: string }>();
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

  readonly memWatchTypes: ReadonlyArray<{ id: MemWatchType; label: string }> = [
    { id: 'u32', label: 'u32' }, { id: 's32', label: 's32' },
    { id: 'u16', label: 'u16' }, { id: 's16', label: 's16' },
    { id: 'u8', label: 'u8' }, { id: 's8', label: 's8' },
    { id: 'f32', label: 'float' }, { id: 'ptr', label: 'pointer' },
    { id: 'string', label: 'string' },
  ];

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
    return (n >>> 0).toString(16).toUpperCase().padStart(pad, '0');
  }

  protected inputValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  /** The value cell shows "0x1F (31)"; write just the first token. */
  protected writableValue(event: Event): string {
    const v = this.inputValue(event).trim();
    return v.startsWith('"') ? v : v.split(/\s+/)[0];
  }

  protected fieldHasError(bp: BreakpointInfo, prefix: string): boolean {
    return !!bp.error && bp.error.startsWith(prefix);
  }

  protected onFieldChange(bp: BreakpointInfo, field: BreakpointField, event: Event): void {
    const value = this.inputValue(event);
    if (value === bp[field]) { return; }
    this.updateBreakpoint.emit({ address: bp.address, patch: { [field]: value } });
  }

  /** Enter / Escape commit by blurring; keys never reach the grid shortcuts. */
  protected onFieldKeydown(event: KeyboardEvent): void {
    event.stopPropagation();
    if (event.key === 'Enter' || event.key === 'Escape') { (event.target as HTMLElement).blur(); }
  }

  protected readonly helpText =
    'Condition: $a0 == 0x10 && u8[$sp+8] != 0, f32[$a1] > 1.5, [ptr]+4 (u32 read), label names.\n' +
    'Hit count: 5 or >=5 (from the 5th hit), ==5, >5, %5 (every 5th).\n' +
    'Log message: text with {expr} or {expr,d|x|f}. A log point prints to the CodeDesigner output and continues.';

  protected onMemWatchRowClick(w: MemWatchRow): void {
    if (w.address !== undefined) { this.navigateToAddress.emit(w.address); }
  }

  protected onMemWatchTypeChange(w: MemWatchRow, event: Event): void {
    this.memWatchUpdate.emit({ id: w.id, patch: { type: this.inputValue(event) as MemWatchType } });
  }

  protected addMemWatch(expr: HTMLInputElement, type: HTMLSelectElement): void {
    const expression = expr.value.trim();
    if (!expression) { return; }
    this.memWatchAdd.emit({ expression, type: type.value as MemWatchType });
    expr.value = '';
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

