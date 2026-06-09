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
} from '@angular/core';
import { DisasmRow } from '../state/types';
import { DisasmRowComponent } from './disasm-row.component';

/**
 * VirtualGridComponent - Virtual scrolling disassembly grid.
 *
 * Uses Angular CDK virtual scroll to efficiently render only visible
 * instruction rows. Works with the existing page-based data loading.
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
      @for (row of visibleRows(); track row.addr; let i = $index) {
      <app-disasm-row
        [data]="row"
        [globalIndex]="pageStart() + windowStart() + i"
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

  /** The rows to display (current page) */
  readonly rows = input.required<DisasmRow[]>();

  /** Page start index for calculating global indices */
  readonly pageStart = input.required<number>();

  /** Pointer label map for address resolution */
  readonly pointerLabelMap = input<Map<string, string>>(new Map());

  /** Transient cheat annotations keyed by row address. */
  readonly cheatLabelMap = input<Map<string, string>>(new Map());

  /** Scroll position and viewport size for windowed rendering */
  private readonly scrollTop = signal(0);
  private readonly viewportHeight = signal(600);

  /** Visible window bounds into `rows()` */
  readonly windowStart = computed(() => {
    const start = Math.floor(this.scrollTop() / this.rowHeight) - this.overscan;
    return Math.max(0, start);
  });

  readonly windowEnd = computed(() => {
    const visibleCount = Math.ceil(this.viewportHeight() / this.rowHeight) + this.overscan * 2;
    return Math.min(this.rows().length, this.windowStart() + visibleCount);
  });

  readonly visibleRows = computed(() => this.rows().slice(this.windowStart(), this.windowEnd()));
  readonly topSpacerHeight = computed(() => this.windowStart() * this.rowHeight);
  readonly bottomSpacerHeight = computed(() => (this.rows().length - this.windowEnd()) * this.rowHeight);

  /** Output events */
  readonly selectRow = output<number>();
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
  protected onRowSelect(globalIndex: number): void {
    this.selectRow.emit(globalIndex);
  }

  protected onScroll(): void {
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
    this.viewport()?.nativeElement.focus();
  }

  /**
   * Adjust the current scroll offset by a pixel delta.
   * Used when the grid's page-start changes (merge/de-merge) to keep
   * the same rows visible without a visual jump.
   */
  adjustScroll(pixelDelta: number): void {
    const viewport = this.viewport()?.nativeElement;
    if (!viewport) { return; }
    const newScroll = Math.max(0, viewport.scrollTop + pixelDelta);
    viewport.scrollTop = newScroll;
    this.scrollTop.set(newScroll);
  }

  /** Scroll to show a specific row index without always pinning it to the top. */
  scrollToIndex(index: number, block: ScrollLogicalPosition = 'nearest'): void {
    const viewport = this.viewport()?.nativeElement;
    if (!viewport || index < 0) {
      return;
    }

    const rowTop = index * this.rowHeight;
    const rowBottom = rowTop + this.rowHeight;
    const currentOffset = viewport.scrollTop;
    const viewportSize = viewport.clientHeight;

    if (block === 'center') {
      const centeredOffset = Math.max(0, rowTop - ((viewportSize - this.rowHeight) / 2));
      viewport.scrollTop = centeredOffset;
      this.scrollTop.set(centeredOffset);
      return;
    }

    if (block === 'start') {
      viewport.scrollTop = rowTop;
      this.scrollTop.set(rowTop);
      return;
    }

    if (block === 'end') {
      const endOffset = Math.max(0, rowBottom - viewportSize);
      viewport.scrollTop = endOffset;
      this.scrollTop.set(endOffset);
      return;
    }

    if (rowTop < currentOffset) {
      viewport.scrollTop = rowTop;
      this.scrollTop.set(rowTop);
      return;
    }

    if (rowBottom > currentOffset + viewportSize) {
      const offset = rowBottom - viewportSize;
      viewport.scrollTop = offset;
      this.scrollTop.set(offset);
    }
  }
}
