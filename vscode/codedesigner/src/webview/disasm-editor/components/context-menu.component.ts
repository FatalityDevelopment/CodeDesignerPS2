import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  signal,
  output,
  computed,
} from '@angular/core';
import { IconComponent } from '../../shared/icon.component';

/**
 * Context data passed when opening the menu via `open()`.
 */
export interface ContextMenuData {
  /** The row's hex address string (e.g. "00100000"). */
  address: string;
  /** Whether a breakpoint is currently set at this address. */
  hasBreakpoint: boolean;
  /** Whether a watch is currently set at this address. */
  hasWatch: boolean;
  /** Whether the instruction has a branch/jump target (enables "Go to Target"). */
  hasTarget: boolean;
  /** Existing label text for this address, if any. */
  currentLabel?: string;
}

/**
 * Right-click context menu for disassembly rows.
 *
 * Usage:
 * ```html
 * <app-context-menu
 *   (setBreakpoint)="onSetBreakpoint()"
 *   (removeBreakpoint)="onRemoveBreakpoint()"
 *   (gotoTarget)="onGotoTarget()"
 *   (editRow)="onEditRow()"
 * />
 * ```
 *
 * Call `open(x, y, data)` from the parent to show the menu at position (x, y).
 * The menu auto-closes on outside click, Escape, or when an action is selected.
 */
@Component({
  selector: 'app-context-menu',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  imports: [IconComponent],
  host: {
    'class': 'context-menu',
    '[class.visible]': 'visible()',
    '[style.left.px]': 'x()',
    '[style.top.px]': 'y()',
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown.escape)': 'close()',
  },
  template: `
    <div class="ctx-menu-addr">{{ addressDisplay() }}</div>
    <div
      class="ctx-menu-item"
      [class.disabled]="data()?.hasBreakpoint"
      (click)="onSetBreakpoint()">
      <app-icon class="ctx-menu-icon" name="circle" [size]="13" />Set Breakpoint
    </div>
    <div
      class="ctx-menu-item"
      [class.disabled]="!data()?.hasBreakpoint"
      (click)="onRemoveBreakpoint()">
      <app-icon class="ctx-menu-icon" name="trash2" [size]="13" />Remove Breakpoint
    </div>
    <div class="ctx-menu-separator"></div>
    <div
      class="ctx-menu-item"
      [class.disabled]="data()?.hasWatch"
      (click)="onAddWatch()">
      <app-icon class="ctx-menu-icon" name="eye" [size]="13" />Add Watch
    </div>
    <div
      class="ctx-menu-item"
      [class.disabled]="!data()?.hasWatch"
      (click)="onRemoveWatch()">
      <app-icon class="ctx-menu-icon" name="ban" [size]="13" />Remove Watch
    </div>
    <div class="ctx-menu-separator"></div>
    <div
      class="ctx-menu-item"
      [class.disabled]="!data()?.hasTarget"
      (click)="onGotoTarget()">
      <app-icon class="ctx-menu-icon" name="arrowright" [size]="13" />Go to Target Address
    </div>
    <div class="ctx-menu-item" (click)="onFindReferences()">
      <app-icon class="ctx-menu-icon" name="search" [size]="13" />Find References
    </div>
    <div class="ctx-menu-item" (click)="onFindCodeReferences()">
      <app-icon class="ctx-menu-icon" name="search" [size]="13" />Find Code References
    </div>
    <div class="ctx-menu-separator"></div>
    <div class="ctx-menu-item" (click)="onAddToLabelManager()">
      <app-icon class="ctx-menu-icon" name="tag" [size]="13" />{{ data()?.currentLabel ? 'Edit Label…' : 'Add Label…' }}
    </div>
    <div class="ctx-menu-separator"></div>
    <div class="ctx-menu-item" (click)="onFormatAs('byte')">
      <span class="ctx-menu-icon">B</span>Format as .byte
    </div>
    <div class="ctx-menu-item" (click)="onFormatAs('half')">
      <span class="ctx-menu-icon">H</span>Format as .half
    </div>
    <div class="ctx-menu-item" (click)="onFormatAs('word')">
      <span class="ctx-menu-icon">W</span>Format as .word
    </div>
    <div class="ctx-menu-item" (click)="onFormatAs('dword')">
      <span class="ctx-menu-icon">D</span>Format as .dword
    </div>
    <div class="ctx-menu-item" (click)="onFormatAs('qword')">
      <span class="ctx-menu-icon">Q</span>Format as .qword
    </div>
    <div class="ctx-menu-item" (click)="onClearFormat()">
      <span class="ctx-menu-icon">A</span>Auto (clear override)
    </div>
  `,
  styles: [`
    /* Styles are provided by the parent disasm-editor.css via ViewEncapsulation.None */
  `],
})
export class ContextMenuComponent {
  // ── State ─────────────────────────────────────────────────────────────────
  readonly visible = signal(false);
  readonly x = signal(0);
  readonly y = signal(0);
  readonly data = signal<ContextMenuData | null>(null);

  // ── Computed ──────────────────────────────────────────────────────────────
  readonly addressDisplay = computed(() => {
    const d = this.data();
    return d ? `0x${d.address.toUpperCase()}` : '—';
  });

  // ── Outputs ───────────────────────────────────────────────────────────────
  readonly setBreakpoint = output<void>();
  readonly removeBreakpoint = output<void>();
  readonly addWatch = output<void>();
  readonly removeWatch = output<void>();
  readonly gotoTarget = output<void>();
  readonly findReferences = output<void>();
  readonly findCodeReferences = output<void>();
  readonly addToLabelManager = output<void>();
  /**
   * Emits when the user picks one of the "Format as …" entries.  The payload
   * is the chosen data-format kind; the parent component forwards it to the
   * service so the override is persisted and the page refreshed.
   */
  readonly formatAs = output<'byte' | 'half' | 'word' | 'dword' | 'qword'>();
  /** Emits when the user picks "Auto (clear override)". */
  readonly clearFormat = output<void>();
  readonly closed = output<void>();

  // ── Public API ────────────────────────────────────────────────────────────

  /**
   * Opens the context menu at the specified position with the given row data.
   * Automatically adjusts position to keep the menu inside the viewport.
   */
  open(clientX: number, clientY: number, rowData: ContextMenuData): void {
    this.data.set(rowData);
    this.visible.set(true);

    // Wait for the menu to render, then adjust position to stay in viewport
    requestAnimationFrame(() => {
      const el = document.querySelector('.context-menu.visible') as HTMLElement;
      if (!el) { return; }

      const menuW = el.offsetWidth;
      const menuH = el.offsetHeight;
      const vw = window.innerWidth;
      const vh = window.innerHeight;

      this.x.set(Math.min(clientX, vw - menuW - 4));
      this.y.set(Math.min(clientY, vh - menuH - 4));
    });

    // Set initial position immediately (may be adjusted after render)
    this.x.set(clientX);
    this.y.set(clientY);
  }

  /**
   * Closes the context menu.
   */
  close(): void {
    if (this.visible()) {
      this.visible.set(false);
      this.data.set(null);
      this.closed.emit();
    }
  }

  // ── Event Handlers ────────────────────────────────────────────────────────

  onDocumentClick(event: MouseEvent): void {
    // Close if clicking outside the menu
    const target = event.target as HTMLElement;
    if (!target.closest('.context-menu')) {
      this.close();
    }
  }

  onSetBreakpoint(): void {
    if (this.data()?.hasBreakpoint) { return; } // Disabled
    this.setBreakpoint.emit();
    this.close();
  }

  onRemoveBreakpoint(): void {
    if (!this.data()?.hasBreakpoint) { return; } // Disabled
    this.removeBreakpoint.emit();
    this.close();
  }

  onAddWatch(): void {
    if (this.data()?.hasWatch) { return; } // Disabled
    this.addWatch.emit();
    this.close();
  }

  onRemoveWatch(): void {
    if (!this.data()?.hasWatch) { return; } // Disabled
    this.removeWatch.emit();
    this.close();
  }

  onGotoTarget(): void {
    if (!this.data()?.hasTarget) { return; } // Disabled
    this.gotoTarget.emit();
    this.close();
  }

  onFindReferences(): void {
    this.findReferences.emit();
    this.close();
  }

  onFindCodeReferences(): void {
    this.findCodeReferences.emit();
    this.close();
  }

  onAddToLabelManager(): void {
    this.addToLabelManager.emit();
    this.close();
  }

  onFormatAs(kind: 'byte' | 'half' | 'word' | 'dword' | 'qword'): void {
    this.formatAs.emit(kind);
    this.close();
  }

  onClearFormat(): void {
    this.clearFormat.emit();
    this.close();
  }
}
