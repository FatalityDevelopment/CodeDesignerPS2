import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  signal,
  computed,
  output,
} from '@angular/core';

export interface HddPartitionEntry {
  Id:          string;
  Name:        string;
  TotalBytes:  number;
  TypeName:    string;
  FileCount:   number;
  Created:     string;
  Files:       Array<{ Name: string; Size: number; IsDirectory: boolean; Modified: string }>;
}

export interface HddListResult {
  partitions?: HddPartitionEntry[];
  error?: string;
}

export interface HddActionResult {
  error?: string;
  message?: string;
}

@Component({
  selector: 'app-hdd-overlay',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
<div id="hdd-overlay"
     [class.visible]="visible()"
     (click)="onBackdropClick($event)"
     (keydown.escape)="close()">
  <div id="hdd-overlay-box">
    <div id="hdd-overlay-header">
      <span id="hdd-overlay-title">&#x1F4BF; HDD Image Manager</span>
      <button id="hdd-overlay-close" title="Close (Escape)" (click)="close()">&#xD7;</button>
    </div>
    <div id="hdd-overlay-body">

      <!-- HDD file selector -->
      <div class="hdd-field-row">
        <input type="text"
               id="hdd-path-input"
               class="hdd-path-input"
               readonly
               placeholder="Select a HDD.raw image file…"
               [value]="hddPath()" />
        <button class="hdd-browse-btn"
                title="Open an existing HDD raw image"
                (click)="browseHdd.emit()">&#8230;</button>
        <button class="hdd-new-btn"
                title="Create a new blank HDD image (40 GB)"
                (click)="newHdd.emit()">New</button>
      </div>

      <!-- Load / Refresh -->
      <div class="hdd-controls-row">
        <button id="hdd-load-btn"
                [disabled]="!hddPath() || loading()"
                (click)="load()">
          {{ loading() ? 'Loading…' : '&#x21BB; Refresh' }}
        </button>
        <span class="hdd-status"
              [class.hdd-status-ok]="statusOk()"
              [class.hdd-status-error]="statusError()">{{ statusMessage() }}</span>
      </div>

      <!-- Partitions table -->
      <div id="hdd-partitions-area">
        <table class="hdd-table">
          <thead>
            <tr>
              <th class="hdd-col-name">Partition</th>
              <th class="hdd-col-type">Type</th>
              <th class="hdd-col-files">Files</th>
              <th class="hdd-col-size">Size</th>
              <th class="hdd-col-created">Created</th>
              <th class="hdd-col-actions"></th>
            </tr>
          </thead>
          <tbody>
            @for (part of pagedPartitions(); track part.Id) {
              <tr class="hdd-row">
                <td class="hdd-col-name" [title]="part.Id">{{ part.Id }}</td>
                <td class="hdd-col-type">{{ part.TypeName }}</td>
                <td class="hdd-col-files">{{ part.FileCount }}</td>
                <td class="hdd-col-size">{{ formatBytes(part.TotalBytes) }}</td>
                <td class="hdd-col-created">{{ part.Created }}</td>
                <td class="hdd-col-actions">
                  @if (pendingDelete() === part.Id) {
                    <span class="hdd-confirm-delete">
                      Delete?
                      <button class="hdd-action-btn hdd-delete-btn"
                              (click)="doDelete(part.Id)">Yes</button>
                      <button class="hdd-action-btn"
                              (click)="pendingDelete.set(null)">No</button>
                    </span>
                  } @else {
                    @if (part.FileCount > 0) {
                      <button class="hdd-action-btn"
                              title="View files in this partition"
                              (click)="viewFiles.emit({ hddPath: hddPath(), partitionId: part.Id })">
                        &#x1F4C2; Files
                      </button>
                    }
                    <button class="hdd-action-btn"
                            title="Export all files from this partition"
                            (click)="exportPartition.emit({ hddPath: hddPath(), partitionId: part.Id })">
                      &#x2B07; Export
                    </button>
                    <button class="hdd-action-btn hdd-delete-btn"
                            title="Delete this partition"
                            (click)="pendingDelete.set(part.Id)">
                      &#x2715; Delete
                    </button>
                  }
                </td>
              </tr>
            }
            @for (_ of emptyRows(); track $index) {
              <tr class="hdd-row hdd-row-empty">
                <td class="hdd-col-name">&nbsp;</td>
                <td class="hdd-col-type">&nbsp;</td>
                <td class="hdd-col-files">&nbsp;</td>
                <td class="hdd-col-size">&nbsp;</td>
                <td class="hdd-col-created">&nbsp;</td>
                <td class="hdd-col-actions">&nbsp;</td>
              </tr>
            }
          </tbody>
        </table>
        @if (partitions().length > pageSize) {
          <div class="hdd-paging-row">
            <button class="hdd-page-btn"
                    [disabled]="currentPage() === 0"
                    (click)="prevPage()">&#x276E; Back</button>
            <span class="hdd-page-info">Page {{ currentPage() + 1 }} of {{ totalPages() }}</span>
            <button class="hdd-page-btn"
                    [disabled]="currentPage() >= totalPages() - 1"
                    (click)="nextPage()">Next &#x276F;</button>
          </div>
        }
      </div>

    </div>
  </div>
</div>
  `,
})
export class HddOverlayComponent {

  // ── Outputs ─────────────────────────────────────────────────────────────────

  /** Emitted when the user clicks the browse (…) button. */
  readonly browseHdd = output<void>();

  /** Emitted when the user clicks "New" — create a blank HDD image. */
  readonly newHdd = output<void>();

  /** Emitted when the user triggers a partition export. */
  readonly exportPartition = output<{ hddPath: string; partitionId: string }>();

  /** Emitted when the user wants to view files in a partition. */
  readonly viewFiles = output<{ hddPath: string; partitionId: string }>();

  /** Emitted when the user confirms a partition delete. */
  readonly deletePartition = output<{ hddPath: string; partitionId: string }>();

  /** Emitted when the user triggers a refresh / initial load. */
  readonly listPartitions = output<string>();

  /** Emitted on close. */
  readonly close$ = output<void>();

  // ── State ───────────────────────────────────────────────────────────────────

  readonly visible       = signal(false);
  readonly hddPath       = signal('');
  readonly partitions    = signal<HddPartitionEntry[]>([]);
  readonly hasLoaded     = signal(false);
  readonly loading       = signal(false);
  readonly statusMessage = signal('');
  readonly statusOk      = signal(false);
  readonly statusError   = signal(false);
  /** ID of the partition awaiting delete confirmation, or null. */
  readonly pendingDelete = signal<string | null>(null);

  // ── Pagination ──────────────────────────────────────────────────────────────

  readonly pageSize = 10;
  readonly currentPage = signal(0);
  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.partitions().length / this.pageSize)));
  readonly pagedPartitions = computed(() => {
    const start = this.currentPage() * this.pageSize;
    return this.partitions().slice(start, start + this.pageSize);
  });
  readonly emptyRows = computed(() =>
    Array(Math.max(0, this.pageSize - this.pagedPartitions().length)).fill(0));

  // ── Public API ──────────────────────────────────────────────────────────────

  open(): void {
    this.visible.set(true);
  }

  close(): void {
    if (this.visible()) {
      this.visible.set(false);
      this.close$.emit();
    }
  }

  setHddPath(p: string): void {
    this.hddPath.set(p);
    // Auto-load on path set
    if (p) { this.load(); }
  }

  setListResult(result: HddListResult): void {
    this.loading.set(false);
    this.hasLoaded.set(true);
    this.pendingDelete.set(null);
    this.currentPage.set(0);
    if (result.error) {
      this.setStatus('Error: ' + result.error, true);
      this.partitions.set([]);
    } else {
      const count = result.partitions?.length ?? 0;
      this.partitions.set(result.partitions ?? []);
      this.setStatus(count === 0 ? 'HDD image is empty.' : `${count} partition${count === 1 ? '' : 's'} loaded.`, false);
    }
  }

  setActionResult(result: HddActionResult): void {
    this.loading.set(false);
    if (result.error) {
      this.setStatus('Error: ' + result.error, true);
    } else {
      this.setStatus(result.message ?? 'Done.', false);
      // Refresh the partition list
      const p = this.hddPath();
      if (p) { this.load(); }
    }
  }

  // ── Event Handlers ──────────────────────────────────────────────────────────

  load(): void {
    const p = this.hddPath().trim();
    if (!p) { return; }
    this.loading.set(true);
    this.setStatus('Loading…', false);
    this.listPartitions.emit(p);
  }

  doDelete(partitionId: string): void {
    this.pendingDelete.set(null);
    this.loading.set(true);
    this.setStatus('Deleting…', false);
    this.deletePartition.emit({ hddPath: this.hddPath(), partitionId });
  }

  onBackdropClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).id === 'hdd-overlay') {
      this.close();
    }
  }

  prevPage(): void {
    if (this.currentPage() > 0) {
      this.currentPage.update(p => p - 1);
    }
  }

  nextPage(): void {
    if (this.currentPage() < this.totalPages() - 1) {
      this.currentPage.update(p => p + 1);
    }
  }

  formatBytes(bytes: number): string {
    if (bytes < 1024) { return `${bytes} B`; }
    if (bytes < 1024 * 1024) { return `${(bytes / 1024).toFixed(1)} KB`; }
    if (bytes < 1024 * 1024 * 1024) { return `${(bytes / (1024 * 1024)).toFixed(2)} MB`; }
    return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`; }

  // ── Private ─────────────────────────────────────────────────────────────────

  private setStatus(msg: string, isError: boolean): void {
    this.statusMessage.set(msg);
    this.statusOk.set(!isError);
    this.statusError.set(isError);
  }
}
