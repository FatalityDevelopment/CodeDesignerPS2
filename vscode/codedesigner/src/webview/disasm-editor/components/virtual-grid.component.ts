import {
  AfterViewInit,
  Component,
  ViewEncapsulation,
  input,
  output,
  viewChild,
  ChangeDetectionStrategy,
  ElementRef,
  signal,
  computed,
  inject,
  Injector,
  afterNextRender,
} from '@angular/core';
import { DisasmRow } from '../state/types';
import { findRowIndexByAddr, rowInstructionIndex } from '../state/row-locator';
import { DisasmRowComponent } from './disasm-row.component';

/**
 * VirtualGridComponent - Virtual scrolling disassembly grid.
 *
 * Renders only the rows around the viewport.  Works with the page-based
 * data loading: `rows` is one page, or several adjacent pages concatenated
 * so the cursor can cross a page boundary without the grid content changing
 * underneath it.
 *
 * Scrolling is fully programmatic (wheel and keyboard scrolling are
 * intercepted and routed through the cursor).  The grid keeps its *desired*
 * scroll offset in `scrollTop` and re-applies it to the DOM after Angular has
 * rendered a new `rows` input, because writing `scrollTop` before the new
 * spacer heights exist lets the browser clamp the value against the old
 * content height.
 */
@Component({
  selector: 'app-virtual-grid',
  standalone: true,
  imports: [DisasmRowComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div
      #viewport
      class="disasm-viewport"
      (scroll)="onScroll()"
      (keydown)="onKeydown($event)"
      tabindex="0"
    >
      <div [style.height.px]="topSpacerHeight()"></div>
      @for (item of visibleRows(); track item.row.addr; let i = $index) {
      <app-disasm-row
        [data]="item.row"
        [globalIndex]="item.globalIndex"
        [localIndex]="windowStart() + i"
        [pointerLabelMap]="pointerLabelMap()"
        [cheatLabelMap]="cheatLabelMap()"
        (select)="onRowSelect($event)"
        (edit)="editRow.emit($event)"
        (contextMenu)="contextMenu.emit($event)"
      />
      }
      <div [style.height.px]="bottomSpacerHeight()"></div>
    </div>
  `,
  styles: [`
    :host {
      display: flex;
      flex: 1;
      min-width: 0;
      min-height: 0;
      overflow: hidden;
    }
    .disasm-viewport {
      scrollbar-width: none;
      -ms-overflow-style: none;
      flex: 1;
      min-width: 0;
      min-height: 0;
      width: 100%;
      overflow-y: auto;
      /* The grid positions itself explicitly when rows are inserted above the
         viewport (page merges, .byte splits).  Browser scroll anchoring would
         shift the offset a second time and produce a visible jump. */
      overflow-anchor: none;
    }
    .disasm-viewport::-webkit-scrollbar {
      display: none;
    }
    .disasm-viewport:focus {
      outline: none;
    }
  `],
})
export class VirtualGridComponent implements AfterViewInit {
  private readonly injector = inject(Injector);

  /** Block mouse-wheel scrolling so navigation is arrow-key only. */
  ngAfterViewInit(): void {
    const el = this.viewport().nativeElement;
    this.viewportHeight.set(el.clientHeight);
    this.scrollTop.set(el.scrollTop);
    el.addEventListener('wheel', (e) => { e.preventDefault(); }, { passive: false });
  }

  /** Reference to the virtual scroll viewport */
  private readonly viewport = viewChild.required<ElementRef<HTMLElement>>('viewport');

  /** Row height in pixels (must match CSS) */
  readonly rowHeight = 20;
  /** Extra rows rendered above/below viewport for smoothness */
  readonly overscan = 24;

  /** The rows to display (one page, or adjacent pages concatenated) */
  readonly rows = input.required<DisasmRow[]>();

  /** Instruction index of the first page in `rows` (kept for diagnostics / data attributes) */
  readonly pageStart = input.required<number>();

  /** Region base address — rows map to instruction indices by address, not by position. */
  readonly baseAddress = input.required<number>();

  /** Pointer label map for address resolution */
  readonly pointerLabelMap = input<Map<string, string>>(new Map());

  /** Transient cheat annotations keyed by row address. */
  readonly cheatLabelMap = input<Map<string, string>>(new Map());

  /** Desired scroll offset (may briefly exceed the rendered content height) and viewport size */
  private readonly scrollTop = signal(0);
  private readonly viewportHeight = signal(600);

  /**
   * Scroll offset waiting to be re-applied once Angular has rendered the
   * current `rows`.  While set, DOM scroll events are ignored so a clamped
   * intermediate value cannot overwrite the desired offset.
   */
  private pendingScrollTop: number | null = null;
  private afterRenderScheduled = false;

  /** Visible window bounds into `rows()` */
  readonly windowStart = computed(() => {
    const start = Math.floor(this.scrollTop() / this.rowHeight) - this.overscan;
    return Math.max(0, start);
  });

  readonly windowEnd = computed(() => {
    const visibleCount = Math.ceil(this.viewportHeight() / this.rowHeight) + this.overscan * 2;
    return Math.min(this.rows().length, this.windowStart() + visibleCount);
  });

  readonly visibleRows = computed(() => {
    const base = this.baseAddress();
    return this.rows()
      .slice(this.windowStart(), this.windowEnd())
      .map(row => ({ row, globalIndex: rowInstructionIndex(row, base) }));
  });
  readonly topSpacerHeight = computed(() => this.windowStart() * this.rowHeight);
  readonly bottomSpacerHeight = computed(() => (this.rows().length - this.windowEnd()) * this.rowHeight);

  /** Output events */
  readonly selectRow = output<DisasmRow>();
  readonly editRow = output<{ row: DisasmRow; address: number; addressHex: string }>();
  readonly contextMenu = output<{ event: MouseEvent; row: DisasmRow; globalIndex: number }>();
  readonly navigateTarget = output<void>();
  readonly navigateBack = output<void>();
  readonly toggleBreakpoint = output<void>();
  readonly cursorMove = output<number>();

  /** Track function for virtual scroll */
  trackByAddr(_index: number, row: DisasmRow): string {
    return row.addr;
  }

  /** Handle row selection */
  protected onRowSelect(row: DisasmRow): void {
    this.selectRow.emit(row);
  }

  protected onScroll(): void {
    // A programmatic write that got clamped against stale content echoes
    // back as a scroll event; keep the desired offset until it is re-applied.
    if (this.pendingScrollTop !== null) { return; }
    const viewport = this.viewport()?.nativeElement;
    if (!viewport) { return; }
    this.scrollTop.set(viewport.scrollTop);
  }

  /** Handle keyboard navigation */
  protected onKeydown(event: KeyboardEvent): void {
    const deltas: Record<string, number> = {
      ArrowDown: 1,
      ArrowUp: -1,
      PageDown: 50,
      PageUp: -50,
    };

    // Alt+Left / Alt+Right are history back / forward, handled at window level.
    if (event.altKey && (event.key === 'ArrowRight' || event.key === 'ArrowLeft')) { return; }

    if (event.key === 'ArrowRight') {
      event.preventDefault();
      this.navigateTarget.emit();
      return;
    }

    if (event.key === 'ArrowLeft') {
      event.preventDefault();
      this.navigateBack.emit();
      return;
    }

    if (event.key === 'F9') {
      event.preventDefault();
      this.toggleBreakpoint.emit();
      return;
    }

    const delta = deltas[event.key];
    if (delta) {
      event.preventDefault();
      this.cursorMove.emit(delta);
    }
  }

  /** Focus the viewport (call from parent when needed) */
  focus(): void {
    // preventScroll: focusing the scroll container must not move it.
    this.viewport()?.nativeElement.focus({ preventScroll: true });
  }

  /** The scroll offset the grid is positioned at (or about to be). */
  get currentScrollTop(): number {
    return this.scrollTop();
  }

  /**
   * Set the desired scroll offset.  Applied to the DOM immediately (so a
   * synchronous read sees it when the content already exists) and again
   * after the next render, when freshly set `rows` have produced their
   * spacer heights and the browser can no longer clamp the value.
   */
  setScrollTop(target: number): void {
    const viewport = this.viewport()?.nativeElement;
    if (!viewport) { return; }
    const desired = Math.max(0, Math.round(target));
    this.scrollTop.set(desired);
    this.pendingScrollTop = desired;
    viewport.scrollTop = desired;

    if (this.afterRenderScheduled) { return; }
    this.afterRenderScheduled = true;
    afterNextRender(() => {
      this.afterRenderScheduled = false;
      const pending = this.pendingScrollTop;
      this.pendingScrollTop = null;
      if (pending === null) { return; }
      viewport.scrollTop = pending;
      // The content is rendered now, so whatever the DOM settled on is final.
      this.scrollTop.set(viewport.scrollTop);
    }, { injector: this.injector });
  }

  /**
   * Keep the same rows on screen when `rows` is about to change from
   * `oldRows` to `newRows` (a page merged in above the viewport, a page
   * dropped, a `.byte` split that inserted rows above, ...).  The row at the
   * top of the viewport is located by address in the new array and the
   * offset is rebased onto it.  Returns false when the anchor row does not
   * exist in the new rows (a jump to a different page), in which case the
   * caller is expected to position the grid explicitly.
   */
  rebaseScroll(oldRows: readonly DisasmRow[], newRows: readonly DisasmRow[]): boolean {
    if (oldRows === newRows || oldRows.length === 0 || newRows.length === 0) { return false; }
    const current = this.scrollTop();
    const anchorIndex = Math.min(oldRows.length - 1, Math.max(0, Math.floor(current / this.rowHeight)));
    const anchorRow = oldRows[anchorIndex];
    if (!anchorRow) { return false; }
    const withinRow = current - anchorIndex * this.rowHeight;

    const newIndex = findRowIndexByAddr(newRows, anchorRow.addr);
    if (newIndex < 0) { return false; }

    const target = newIndex * this.rowHeight + withinRow;
    if (target !== current || this.pendingScrollTop !== null) {
      this.setScrollTop(target);
    }
    return true;
  }

  /** Scroll to show a specific row index without always pinning it to the top. */
  scrollToIndex(index: number, block: ScrollLogicalPosition = 'nearest'): void {
    const viewport = this.viewport()?.nativeElement;
    if (!viewport || index < 0) {
      return;
    }

    const rowTop = index * this.rowHeight;
    const rowBottom = rowTop + this.rowHeight;
    // Compare against the desired offset, not the DOM: the DOM may still be
    // clamped against rows that have not been rendered yet.
    const currentOffset = this.scrollTop();
    const viewportSize = viewport.clientHeight || this.viewportHeight();

    if (block === 'center') {
      this.setScrollTop(rowTop - ((viewportSize - this.rowHeight) / 2));
      return;
    }

    if (block === 'start') {
      this.setScrollTop(rowTop);
      return;
    }

    if (block === 'end') {
      this.setScrollTop(rowBottom - viewportSize);
      return;
    }

    if (rowTop < currentOffset) {
      this.setScrollTop(rowTop);
      return;
    }

    if (rowBottom > currentOffset + viewportSize) {
      this.setScrollTop(rowBottom - viewportSize);
    }
  }
}
