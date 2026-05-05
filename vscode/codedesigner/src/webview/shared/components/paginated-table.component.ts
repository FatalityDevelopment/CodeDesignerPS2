import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  input,
  output,
  TemplateRef,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

export interface ColumnDef {
  key: string;
  label: string;
  sortable?: boolean;
}

@Component({
  selector: 'app-paginated-table',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  imports: [NgTemplateOutlet],
  styles: [`
.lm-filter-row {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 5px 10px;
  border-bottom: 1px solid #3e3e42;
  flex-shrink: 0;
}
.lm-filter {
  flex: 1;
  background: #3c3c3c;
  color: #d4d4d4;
  border: 1px solid #555;
  border-radius: 3px;
  padding: 3px 8px;
  font-size: 12px;
  font-family: inherit;
}
.lm-clear-filter {
  background: none;
  border: none;
  color: #888;
  cursor: pointer;
  font-size: 14px;
  padding: 0 4px;
  line-height: 1;
}
.lm-clear-filter:hover { color: #ccc; }
.lm-count { color: #777; font-size: 11px; white-space: nowrap; }
.lm-muted  { color: #666; }
.lm-pagination {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 4px 10px;
  background: #1e1e1e;
  border-bottom: 1px solid #3e3e42;
  font-size: 11px;
  flex-shrink: 0;
}
.lm-page-btn {
  background: #2d2d30;
  border: 1px solid #3e3e42;
  color: #ccc;
  padding: 2px 6px;
  cursor: pointer;
  border-radius: 3px;
  font-size: 11px;
  line-height: 1;
}
.lm-page-btn:hover:not(:disabled) { background: #3e3e42; color: #fff; }
.lm-page-btn:disabled { opacity: 0.35; cursor: default; }
.lm-page-info { color: #ccc; padding: 0 6px; white-space: nowrap; user-select: none; }
.lm-page-size-select {
  background: #2d2d30;
  color: #ccc;
  border: 1px solid #3e3e42;
  border-radius: 3px;
  padding: 2px 4px;
  font-size: 11px;
  margin-left: auto;
}
.lm-table-wrap { overflow-y: auto; flex: 1; min-height: 80px; }
.lm-table { width: 100%; border-collapse: collapse; font-size: 12px; }
.lm-table th {
  position: sticky;
  top: 0;
  background: #252526;
  color: #999;
  font-weight: 500;
  text-align: left;
  padding: 5px 8px;
  border-bottom: 1px solid #3e3e42;
}
.lm-table td { padding: 4px 8px; border-bottom: 1px solid #2a2a2a; vertical-align: middle; }
.lm-table tr:hover td { background: #2a2d2e; }
.lm-sort-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  background: none;
  border: none;
  color: inherit;
  font: inherit;
  padding: 0;
  cursor: pointer;
  text-align: left;
}
.lm-sort-btn:hover { color: #ccc; }
.lm-sort-btn.lm-sort-active { color: #d4d4d4; }
.lm-sort-indicator { min-width: 10px; color: #569cd6; font-size: 10px; }
`],
  template: `
@if (showFilter()) {
  <div class="lm-filter-row">
    <input class="lm-filter"
           type="text"
           spellcheck="false"
           [placeholder]="filterPlaceholder()"
           [value]="filterText()"
           (input)="onFilterInput($event)" />
    @if (filterText()) {
      <button class="lm-clear-filter" (click)="filterTextChange.emit('')">&#xD7;</button>
    }
    <span class="lm-count lm-muted">{{ totalCount() }}</span>
  </div>
}

@if (totalPages() > 1 || totalCount() > pageSizeOptions[0]) {
  <div class="lm-pagination">
    <button class="lm-page-btn" [disabled]="currentPage() === 0" title="First page" (click)="pageChange.emit(0)">&#x23EE;</button>
    <button class="lm-page-btn" [disabled]="currentPage() === 0" title="Previous page" (click)="pageChange.emit(currentPage() - 1)">&#x25C0;</button>
    <span class="lm-page-info">
      Page {{ currentPage() + 1 }} of {{ totalPages() }}
      <span class="lm-muted">({{ totalCount() }} entries)</span>
    </span>
    <button class="lm-page-btn" [disabled]="currentPage() >= totalPages() - 1" title="Next page" (click)="pageChange.emit(currentPage() + 1)">&#x25B6;</button>
    <button class="lm-page-btn" [disabled]="currentPage() >= totalPages() - 1" title="Last page" (click)="pageChange.emit(totalPages() - 1)">&#x23ED;</button>
    <select class="lm-page-size-select" [value]="pageSize()" (change)="onPageSizeChange($event)" title="Entries per page">
      @for (opt of pageSizeOptions; track opt) {
        <option [value]="opt" [selected]="pageSize() === opt">{{ opt }} / page</option>
      }
    </select>
  </div>
}

<div class="lm-table-wrap">
  <table class="lm-table">
    <thead>
      <tr>
        @for (col of columns(); track col.key) {
          <th>
            @if (col.sortable) {
              <button type="button" class="lm-sort-btn"
                      [class.lm-sort-active]="sortColumn() === col.key"
                      (click)="onSortClick(col.key)">
                <span>{{ col.label }}</span>
                <span class="lm-sort-indicator">{{ sortIndicator(col.key) }}</span>
              </button>
            } @else {
              {{ col.label }}
            }
          </th>
        }
      </tr>
    </thead>
    <tbody>
      @if (rowTemplate()) {
        @for (row of rows(); track $index) {
          <ng-container *ngTemplateOutlet="rowTemplate()!; context: { $implicit: row }"></ng-container>
        }
      } @else {
        @for (row of rows(); track $index) {
          <tr>
            @for (col of columns(); track col.key) {
              <td>{{ row[col.key] }}</td>
            }
          </tr>
        }
      }
    </tbody>
  </table>
</div>
`,
})
export class PaginatedTableComponent {
  readonly rows            = input<Record<string, unknown>[]>([]);
  readonly columns         = input<ColumnDef[]>([]);
  readonly totalCount      = input<number>(0);
  readonly currentPage     = input<number>(0);
  readonly pageSize        = input<number>(100);
  readonly totalPages      = input<number>(1);
  readonly filterText      = input<string>('');
  readonly sortColumn      = input<string | null>(null);
  readonly sortDirection   = input<'asc' | 'desc'>('asc');
  readonly filterPlaceholder = input<string>('Filter…');
  readonly showFilter      = input<boolean>(true);
  readonly rowTemplate     = input<TemplateRef<{ $implicit: Record<string, unknown> }> | null>(null);

  readonly filterTextChange = output<string>();
  readonly pageChange       = output<number>();
  readonly pageSizeChange   = output<number>();
  readonly sortChange       = output<{ column: string; direction: 'asc' | 'desc' }>();

  readonly pageSizeOptions = [50, 100, 250, 500];

  sortIndicator(col: string): string {
    if (this.sortColumn() !== col) return '';
    return this.sortDirection() === 'asc' ? ' ▲' : ' ▼';
  }

  onFilterInput(event: Event): void {
    this.filterTextChange.emit((event.target as HTMLInputElement).value);
  }

  onPageSizeChange(event: Event): void {
    this.pageSizeChange.emit(Number((event.target as HTMLSelectElement).value));
  }

  onSortClick(col: string): void {
    const direction: 'asc' | 'desc' =
      this.sortColumn() === col && this.sortDirection() === 'asc' ? 'desc' : 'asc';
    this.sortChange.emit({ column: col, direction });
  }
}
