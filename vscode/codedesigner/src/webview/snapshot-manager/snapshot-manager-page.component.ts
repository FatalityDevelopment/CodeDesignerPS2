import {
  Component,
  AfterViewInit,
  ViewEncapsulation,
  signal,
  computed,
  NgZone,
  inject,
} from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import { IconComponent } from '../shared/icon.component';
import { PageHeaderComponent } from '../shared/page-header.component';
import { PaginatedTableComponent, ColumnDef, ConfirmDialogComponent } from '../shared/components/index';
import { SnapshotEntry } from '../disasm-panel/types';
import '../shared/styles/shared.css';
import './snapshot-manager-page.css';

@Component({
  selector: 'app-snapshot-manager-page',
  standalone: true,
  imports: [PaginatedTableComponent, ConfirmDialogComponent, IconComponent, PageHeaderComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="sm-outer">
  <app-page-header icon="camera" title="Snapshots" subtitle="Snapshot manager" />
  <div class="sm-header">
    <span class="sm-title"><app-icon name="camera" [size]="14" /> Snapshot Manager</span>
    <button class="lm-tool-btn sm-refresh-btn" title="Refresh" (click)="onRefresh()">&#x21BB;</button>
  </div>

  @if (statusMsg()) {
    <div class="sm-status" [class.sm-status-error]="statusIsError()">{{ statusMsg() }}</div>
  }

  <app-paginated-table
    [rows]="pagedRows()"
    [columns]="columns"
    [totalCount]="filteredSnapshots().length"
    [currentPage]="currentPage()"
    [pageSize]="pageSize()"
    [totalPages]="totalPages()"
    [filterText]="filterText()"
    [sortColumn]="sortColumn()"
    [sortDirection]="sortDirection()"
    [filterPlaceholder]="'Filter by title, date, or game…'"
    [rowTemplate]="rowTpl"
    (filterTextChange)="filterText.set($event); currentPage.set(0)"
    (pageChange)="currentPage.set($event)"
    (pageSizeChange)="pageSize.set($event); currentPage.set(0)"
    (sortChange)="onSortChange($event)">
  </app-paginated-table>

  <ng-template #rowTpl let-row>
    <tr>
      <td class="sm-col-date lm-mono">{{ $any(row).date }}</td>
      <td class="sm-col-title">{{ $any(row).label }}</td>
      <td class="sm-col-game lm-muted">{{ $any(row).isoName }}</td>
      <td class="sm-col-actions">
        <button class="lm-row-btn" title="Open in file viewer" (click)="onOpen($any(row))">&#x2192; Open</button>
        <button class="lm-row-btn" title="Reload snapshot into emulator" (click)="onReload($any(row))">&#x21BA; Reload</button>
        <button class="lm-row-btn" title="Compare with another snapshot" (click)="onDiff($any(row))">&#x2194; Diff</button>
        <button class="lm-row-btn lm-danger-btn" title="Delete snapshot" (click)="onDeleteRequest($any(row))">
          <svg viewBox="0 0 10 12" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" style="width:11px;height:11px;vertical-align:middle" aria-hidden="true"><path d="M1 3h8M4 1.5h2M2 3l.7 7.5h4.6L8 3"/></svg>
        </button>
      </td>
    </tr>
  </ng-template>

  <app-confirm-dialog
    [visible]="!!pendingDeleteSnap()"
    [title]="'Delete snapshot?'"
    [body]="pendingDeleteSnap()?.label ?? ''"
    [note]="'This permanently deletes the snapshot files from disk.'"
    [confirmLabel]="'Delete'"
    (confirmed)="onConfirmDelete()"
    (cancelled)="pendingDeleteSnap.set(null)">
  </app-confirm-dialog>
</div>
`,
})
export class SnapshotManagerPageComponent implements AfterViewInit {
  private readonly vscode = getVsCodeApi();
  private readonly zone   = inject(NgZone);

  readonly snapshots          = signal<SnapshotEntry[]>([]);
  readonly filterText         = signal('');
  readonly currentPage        = signal(0);
  readonly pageSize           = signal(50);
  readonly sortColumn         = signal<string | null>('date');
  readonly sortDirection      = signal<'asc' | 'desc'>('desc');
  readonly pendingDeleteSnap  = signal<SnapshotEntry | null>(null);
  readonly statusMsg          = signal('');
  readonly statusIsError      = signal(false);

  readonly columns: ColumnDef[] = [
    { key: 'date',    label: 'Date / Time', sortable: true },
    { key: 'label',   label: 'Title',       sortable: true },
    { key: 'isoName', label: 'Game',        sortable: true },
    { key: 'actions', label: 'Actions',     sortable: false },
  ];

  readonly filteredSnapshots = computed(() => {
    const q = this.filterText().toLowerCase();
    if (!q) { return this.snapshots(); }
    return this.snapshots().filter(s =>
      s.label?.toLowerCase().includes(q) ||
      s.date?.toLowerCase().includes(q) ||
      s.isoName?.toLowerCase().includes(q)
    );
  });

  readonly sortedSnapshots = computed(() => {
    const col = this.sortColumn();
    const dir = this.sortDirection();
    const list = [...this.filteredSnapshots()];
    if (!col) { return list; }
    return list.sort((a, b) => {
      const av = (a as unknown as Record<string, unknown>)[col] as string ?? '';
      const bv = (b as unknown as Record<string, unknown>)[col] as string ?? '';
      return dir === 'asc' ? av.localeCompare(bv) : bv.localeCompare(av);
    });
  });

  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.sortedSnapshots().length / this.pageSize()))
  );

  readonly pagedRows = computed((): Record<string, unknown>[] => {
    const start = this.currentPage() * this.pageSize();
    return this.sortedSnapshots().slice(start, start + this.pageSize()) as unknown as Record<string, unknown>[];
  });

  ngAfterViewInit(): void {
    window.addEventListener('message', (event: MessageEvent) => {
      const data = event.data as { type: string; [key: string]: unknown };
      this.zone.run(() => { this._handleMessage(data); });
    });
    this.vscode.postMessage({ type: 'ready' });
  }

  onRefresh(): void {
    this.vscode.postMessage({ type: 'refresh' });
  }

  onOpen(snap: SnapshotEntry): void {
    this.vscode.postMessage({ type: 'snapshotOpen', path: snap.path });
  }

  onReload(snap: SnapshotEntry): void {
    this.vscode.postMessage({ type: 'snapshotReload', path: snap.path });
  }

  onDiff(snap: SnapshotEntry): void {
    this.vscode.postMessage({ type: 'snapshotDiff', snapshot: snap });
  }

  onDeleteRequest(snap: SnapshotEntry): void {
    this.pendingDeleteSnap.set(snap);
  }

  onConfirmDelete(): void {
    const snap = this.pendingDeleteSnap();
    if (!snap) { return; }
    this.pendingDeleteSnap.set(null);
    this.vscode.postMessage({ type: 'snapshotDelete', path: snap.path });
  }

  onSortChange(evt: { column: string; direction: 'asc' | 'desc' }): void {
    this.sortColumn.set(evt.column);
    this.sortDirection.set(evt.direction);
    this.currentPage.set(0);
  }

  private _handleMessage(data: { type: string; [key: string]: unknown }): void {
    switch (data['type']) {
      case 'snapshotList':
        this.snapshots.set((data['snapshots'] as SnapshotEntry[]) ?? []);
        this._showStatus('');
        break;
      case 'snapshotActionResult':
        if (data['error']) {
          this._showStatus(data['error'] as string, true);
        } else {
          this._showStatus((data['message'] as string) ?? '', false);
          this.vscode.postMessage({ type: 'refresh' });
        }
        break;
      case 'snapshotError':
        this._showStatus((data['message'] as string) ?? 'An error occurred.', true);
        break;
    }
  }

  private _showStatus(msg: string, isError = false): void {
    this.statusMsg.set(msg);
    this.statusIsError.set(isError);
  }
}
