import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  signal,
  computed,
  output,
} from '@angular/core';

export interface McpMemoryEntry {
  id:        number;
  name:      string;
  content:   string;
  gameId?:   string;
  createdAt: string;
  updatedAt: string;
}

export interface McpMemoryListResult {
  entries?: McpMemoryEntry[];
  error?:   string;
}

export interface McpMemoryActionResult {
  entry?:   McpMemoryEntry;
  error?:   string;
  message?: string;
}

@Component({
  selector: 'app-mcp-memory-overlay',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
<div id="mcpmem-panel">
  <div id="mcpmem-header">
    <span id="mcpmem-title">&#x1F4DD; MCP Memory Manager</span>
    <div id="mcpmem-header-actions">
      <button class="mcpmem-btn" title="Refresh list" (click)="load()">&#x21BB;</button>
      <button class="mcpmem-btn" title="Import a .md file" (click)="importEntry.emit()">&#x2B06; Import</button>
      <button class="mcpmem-btn mcpmem-add-btn" title="Create new entry" (click)="onAddClick()">+ New</button>
    </div>
  </div>

  @if (statusMessage()) {
    <div class="mcpmem-status"
         [class.mcpmem-status-ok]="statusOk()"
         [class.mcpmem-status-error]="statusError()">
      {{ statusMessage() }}
    </div>
  }

  <!-- Add form -->
  @if (showAddForm()) {
    <div class="mcpmem-add-form">
      <input type="text"
             class="mcpmem-input"
             placeholder="Entry name…"
             [value]="newName()"
             (input)="newName.set($any($event.target).value)" />
      <div class="mcpmem-add-form-actions">
        <button class="mcpmem-btn mcpmem-add-btn"
                [disabled]="!newName().trim()"
                (click)="doAdd()">Create</button>
        <button class="mcpmem-btn" (click)="showAddForm.set(false)">Cancel</button>
      </div>
    </div>
  }

  <!-- Entries table -->
  <div id="mcpmem-table-area">
    <table class="mcpmem-table">
      <thead>
        <tr>
          <th class="mcpmem-col-name">Name</th>
          <th class="mcpmem-col-updated">Updated</th>
          <th class="mcpmem-col-actions"></th>
        </tr>
      </thead>
      <tbody>
        @for (entry of pagedEntries(); track entry.id) {
          <tr class="mcpmem-row">
            <td class="mcpmem-col-name" [title]="entry.name">{{ entry.name }}</td>
            <td class="mcpmem-col-updated">{{ formatDate(entry.updatedAt) }}</td>
            <td class="mcpmem-col-actions">
              @if (pendingDelete() === entry.id) {
                <span class="mcpmem-confirm-delete">
                  Delete?
                  <button class="mcpmem-action-btn mcpmem-delete-btn"
                          (click)="doDelete(entry.id)">Yes</button>
                  <button class="mcpmem-action-btn"
                          (click)="pendingDelete.set(null)">No</button>
                </span>
              } @else {
                <button class="mcpmem-action-btn"
                        title="Open in editor"
                        (click)="editEntry.emit(entry)">&#x270E; Edit</button>
                <button class="mcpmem-action-btn"
                        title="Export to .md file"
                        (click)="exportEntry.emit(entry)">&#x2B07; Export</button>
                <button class="mcpmem-action-btn mcpmem-delete-btn"
                        title="Delete this entry"
                        (click)="pendingDelete.set(entry.id)">&#x2715;</button>
              }
            </td>
          </tr>
        }
        @for (_ of emptyRows(); track $index) {
          <tr class="mcpmem-row mcpmem-row-empty">
            <td class="mcpmem-col-name">&nbsp;</td>
            <td class="mcpmem-col-updated">&nbsp;</td>
            <td class="mcpmem-col-actions">&nbsp;</td>
          </tr>
        }
      </tbody>
    </table>
    @if (entries().length > pageSize) {
      <div class="mcpmem-paging-row">
        <button class="mcpmem-page-btn"
                [disabled]="currentPage() === 0"
                (click)="prevPage()">&#x276E; Back</button>
        <span class="mcpmem-page-info">Page {{ currentPage() + 1 }} of {{ totalPages() }}</span>
        <button class="mcpmem-page-btn"
                [disabled]="currentPage() >= totalPages() - 1"
                (click)="nextPage()">Next &#x276F;</button>
      </div>
    }
  </div>
</div>
  `,
})
export class McpMemoryOverlayComponent {

  // ── Outputs ─────────────────────────────────────────────────────────────────

  /** Emitted to request a list refresh. */
  readonly listEntries = output<void>();

  /** Emitted when the user wants to create a new entry. Carries the name. */
  readonly addEntry = output<string>();

  /** Emitted when the user clicks Edit on an entry. */
  readonly editEntry = output<McpMemoryEntry>();

  /** Emitted when the user clicks Export on an entry. */
  readonly exportEntry = output<McpMemoryEntry>();

  /** Emitted when the user clicks Import. */
  readonly importEntry = output<void>();

  /** Emitted when the user confirms a delete. Carries the entry id. */
  readonly deleteEntry = output<number>();

  // ── State ───────────────────────────────────────────────────────────────────

  readonly entries       = signal<McpMemoryEntry[]>([]);
  readonly loading       = signal(false);
  readonly statusMessage = signal('');
  readonly statusOk      = signal(false);
  readonly statusError   = signal(false);
  readonly showAddForm   = signal(false);
  readonly newName       = signal('');
  /** Id of the entry awaiting delete confirmation, or null. */
  readonly pendingDelete = signal<number | null>(null);

  // ── Pagination ──────────────────────────────────────────────────────────────

  readonly pageSize    = 10;
  readonly currentPage = signal(0);
  readonly totalPages  = computed(() =>
    Math.max(1, Math.ceil(this.entries().length / this.pageSize)));
  readonly pagedEntries = computed(() => {
    const start = this.currentPage() * this.pageSize;
    return this.entries().slice(start, start + this.pageSize);
  });
  readonly emptyRows = computed(() =>
    Array(Math.max(0, this.pageSize - this.pagedEntries().length)).fill(0));

  // ── Public API ──────────────────────────────────────────────────────────────

  load(): void {
    this.loading.set(true);
    this.setStatus('Loading…', false);
    this.listEntries.emit();
  }

  setListResult(result: McpMemoryListResult): void {
    this.loading.set(false);
    this.pendingDelete.set(null);
    this.currentPage.set(0);
    if (result.error) {
      this.setStatus('Error: ' + result.error, true);
      this.entries.set([]);
    } else {
      const count = result.entries?.length ?? 0;
      this.entries.set(result.entries ?? []);
      this.setStatus(count === 0 ? 'No entries.' : `${count} entr${count === 1 ? 'y' : 'ies'} loaded.`, false);
    }
  }

  setActionResult(result: McpMemoryActionResult): void {
    this.loading.set(false);
    if (result.error) {
      this.setStatus('Error: ' + result.error, true);
    } else {
      this.setStatus(result.message ?? 'Done.', false);
      this.load();
    }
  }

  // ── Event Handlers ──────────────────────────────────────────────────────────

  onAddClick(): void {
    this.newName.set('');
    this.showAddForm.set(true);
  }

  doAdd(): void {
    const name = this.newName().trim();
    if (!name) { return; }
    this.showAddForm.set(false);
    this.loading.set(true);
    this.setStatus('Creating…', false);
    this.addEntry.emit(name);
  }

  doDelete(id: number): void {
    this.pendingDelete.set(null);
    this.loading.set(true);
    this.setStatus('Deleting…', false);
    this.deleteEntry.emit(id);
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

  formatDate(iso: string): string {
    try {
      return new Date(iso).toLocaleDateString();
    } catch {
      return iso;
    }
  }

  // ── Private ─────────────────────────────────────────────────────────────────

  private setStatus(msg: string, isError: boolean): void {
    this.statusMessage.set(msg);
    this.statusOk.set(!isError);
    this.statusError.set(isError);
  }
}
