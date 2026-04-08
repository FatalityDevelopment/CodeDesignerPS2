import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  signal,
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
<div class="mem-section">

  <!-- Section header -->
  <div class="mem-section-header">
    <h4 class="mem-section-title">Memories</h4>
    <div class="mem-header-actions">
      <button class="mem-icon-btn" title="Refresh list" (click)="load()">&#x21BB;</button>
      <button class="mem-small-btn" title="Import a .md file" (click)="importEntry.emit()">&#x2B06; Import</button>
      <button class="mem-small-btn mem-primary-btn" title="Create new memory" (click)="onAddClick()">+ New</button>
    </div>
  </div>

  <!-- Status message -->
  @if (statusMessage()) {
    <div class="mem-status"
         [class.mem-status-ok]="statusOk()"
         [class.mem-status-error]="statusError()">
      {{ statusMessage() }}
    </div>
  }

  <!-- Inline add form -->
  @if (showAddForm()) {
    <div class="mem-add-form">
      <input class="mem-name-input"
             type="text"
             placeholder="Memory name…"
             autocomplete="off"
             spellcheck="false"
             [value]="newName()"
             (input)="newName.set($any($event.target).value)"
             (keydown.enter)="doAdd()"
             (keydown.escape)="showAddForm.set(false)" />
      <div class="mem-add-form-row">
        <button class="mem-small-btn mem-primary-btn"
                [disabled]="!newName().trim()"
                (click)="doAdd()">Create</button>
        <button class="mem-small-btn" (click)="showAddForm.set(false)">Cancel</button>
      </div>
    </div>
  }

  <!-- Entries list -->
  <div class="mem-list">
    @if (entries().length === 0 && !loading()) {
      <div class="mem-empty">No memories yet. Click "+ New" to create one.</div>
    }
    @for (entry of entries(); track entry.id) {
      <div class="mem-item">
        <div class="mem-item-info">
          <span class="mem-item-name" [title]="entry.name">{{ entry.name }}</span>
          <span class="mem-item-date">{{ formatDate(entry.updatedAt) }}</span>
        </div>
        <div class="mem-item-actions">
          @if (pendingDelete() === entry.id) {
            <span class="mem-item-confirm">Delete?</span>
            <button class="mem-item-btn mem-danger-btn"
                    (click)="doDelete(entry.id)">Yes</button>
            <button class="mem-item-btn"
                    (click)="pendingDelete.set(null)">No</button>
          } @else {
            <button class="mem-item-btn"
                    title="Open in editor"
                    (click)="editEntry.emit(entry)">Edit</button>
            <button class="mem-item-btn"
                    title="Export to .md file"
                    (click)="exportEntry.emit(entry)">&#x2B07;</button>
            <button class="mem-item-btn mem-danger-btn"
                    title="Delete this entry"
                    (click)="pendingDelete.set(entry.id)">&#x2715;</button>
          }
        </div>
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

  // ── Public API ──────────────────────────────────────────────────────────────

  load(): void {
    this.loading.set(true);
    this.setStatus('Loading…', false);
    this.listEntries.emit();
  }

  setListResult(result: McpMemoryListResult): void {
    this.loading.set(false);
    this.pendingDelete.set(null);
    if (result.error) {
      this.setStatus('Error: ' + result.error, true);
      this.entries.set([]);
    } else {
      const count = result.entries?.length ?? 0;
      this.entries.set(result.entries ?? []);
      this.setStatus(count === 0 ? '' : `${count} entr${count === 1 ? 'y' : 'ies'}`, false);
    }
  }

  setActionResult(result: McpMemoryActionResult): void {
    this.loading.set(false);
    if (result.error) {
      this.setStatus('Error: ' + result.error, true);
    } else {
      this.setStatus('', false);
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
