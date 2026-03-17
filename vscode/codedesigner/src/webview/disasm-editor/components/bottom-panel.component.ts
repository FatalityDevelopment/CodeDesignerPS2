import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  input,
  output,
  signal,
  computed,
} from '@angular/core';
import { CallStackEntry, ThreadEntry } from '../state/types';

export type BottomTab = 'callstack' | 'threads' | 'breakpoints';

/**
 * BottomPanelComponent - Displays callstack, threads, and breakpoints in 3 tabs.
 *
 * Uses signal inputs for reactive updates without zone.js.
 */
@Component({
  selector: 'app-bottom-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div id="bottom-panel" class="bottom-panel" [class.visible]="visible()">
      <div class="bottom-tabs">
        <button class="bottom-tab" [class.active]="activeTab() === 'callstack'" (click)="selectTab('callstack')">Call Stack</button>
        <button class="bottom-tab" [class.active]="activeTab() === 'threads'" (click)="selectTab('threads')">Threads</button>
        <button class="bottom-tab" [class.active]="activeTab() === 'breakpoints'" (click)="selectTab('breakpoints')">Breakpoints</button>
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
    </div>
  `,
})
export class BottomPanelComponent {
  // Inputs
  readonly visible = input(false);
  readonly callStack = input<CallStackEntry[]>([]);
  readonly threads = input<ThreadEntry[]>([]);
  readonly breakpoints = input<Set<number>>(new Set());

  // Outputs
  readonly navigateToAddress = output<number>();
  readonly toggleBreakpoint = output<number>();
  readonly removeBreakpoint = output<number>();
  readonly clearAllBreakpoints = output<void>();
  readonly selectThread = output<number>();
  readonly refreshTab = output<BottomTab>();
  readonly tabSelected = output<BottomTab>();

  // Internal state
  readonly activeTab = signal<BottomTab>('callstack');

  // Sorted breakpoints array for @for iteration
  readonly sortedBreakpoints = computed(() =>
    Array.from(this.breakpoints()).sort((a, b) => a - b),
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
}

