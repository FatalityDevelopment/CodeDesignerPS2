import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  signal,
  output,
  computed,
  effect,
} from '@angular/core';
import { IconComponent } from '../../shared/icon.component';

export interface McpMemoryEntry {
  id:        number;
  name:      string;
  content:   string;
  gameId?:   string;
  /** Agent database ID when scoped to a specific agent; undefined/null means shared. */
  agentId?:  number;
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

export type McpMemoryFilter = 'all' | 'shared' | 'agent';

const MEM_PREVIEW = 5;

@Component({
  selector: 'app-mcp-memory-overlay',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="mem-section">

  <!-- Section header -->
  <div class="mem-section-header">
    <h4 class="mem-section-title">Memory Store</h4>
    <div class="mem-header-actions">
      <button class="mem-icon-btn" title="Refresh list" (click)="load()">
        <app-icon name="refreshcw" [size]="12" />
      </button>
      <button class="mem-small-btn" title="Import a .md file" (click)="importEntry.emit()">
        <app-icon name="upload" [size]="12" /><span>Import</span>
      </button>
      <button class="mem-small-btn mem-primary-btn" title="Create new memory" (click)="onAddClick()">
        <app-icon name="plus" [size]="12" /><span>New</span>
      </button>
    </div>
  </div>

  <!-- Filter bar -->
  <div class="mem-filter-bar">
    <button class="mem-filter-btn" [class.mem-filter-active]="filter() === 'all'"    (click)="filter.set('all')">All</button>
    <button class="mem-filter-btn" [class.mem-filter-active]="filter() === 'shared'" (click)="filter.set('shared')">Shared</button>
    <button class="mem-filter-btn" [class.mem-filter-active]="filter() === 'agent'"  (click)="filter.set('agent')">Agent-specific</button>
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
    @if (filteredEntries().length === 0 && !loading()) {
      <div class="mem-empty">
        @if (filter() === 'all') { No memories yet. Click "+ New" to create one. }
        @if (filter() === 'shared') { No shared memories found. }
        @if (filter() === 'agent') { No agent-specific memories found. }
      </div>
    }
    @for (entry of filteredEntries(); track entry.id) {
      <div class="mem-item">
        <div class="mem-item-info">
          <span class="mem-item-name" [title]="entry.name">{{ entry.name }}</span>
          <span class="mem-item-badge" [class.mem-badge-agent]="entry.agentId != null" [class.mem-badge-shared]="entry.agentId == null">
            {{ entry.agentId != null ? ('Agent ' + entry.agentId) : 'Shared' }}
          </span>
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
                    (click)="exportEntry.emit(entry)">
              <app-icon name="download" [size]="12" />
            </button>
            <button class="mem-item-btn mem-danger-btn"
                    title="Delete this entry"
                    (click)="pendingDelete.set(entry.id)">
              <app-icon name="x" [size]="12" />
            </button>
          }
        </div>
      </div>
    }
  </div>
  @if (totalFilteredCount() > memPreview) {
    <button class="show-more-btn" (click)="showAll.set(!showAll())">
      {{showAll() ? '▴ Show fewer' : '▾ Show ' + (totalFilteredCount() - memPreview) + ' more'}}
    </button>
  }

</div>
  `,
  styles: [`
    .mem-filter-bar {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 3px;
      padding: 3px;
      margin-bottom: 2px;
      border: 1px solid var(--vscode-panel-border, #313131);
      border-radius: 4px;
      background: rgba(255,255,255,0.018);
    }
    .mem-filter-btn {
      display: flex;
      align-items: center;
      justify-content: center;
      min-width: 0;
      height: 24px;
      padding: 0 6px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      font-size: 11px;
      cursor: pointer;
      border: 1px solid transparent;
      background: transparent;
      color: var(--vscode-descriptionForeground, #8f8f8f);
      border-radius: 3px;
    }
    .mem-filter-btn:hover {
      background: rgba(255,255,255,0.07);
      color: var(--vscode-foreground, #d4d4d4);
    }
    .mem-filter-active {
      background: var(--vscode-button-secondaryBackground, #252526) !important;
      border-color: var(--vscode-panel-border, #454545) !important;
      color: var(--vscode-foreground, #d4d4d4) !important;
    }
    .mem-item-badge {
      display: inline-flex;
      align-items: center;
      align-self: flex-start;
      height: 16px;
      margin-top: 3px;
      padding: 0 6px;
      border-radius: 3px;
      font-size: 10px;
      line-height: 1;
    }
    .mem-badge-shared {
      background: rgba(78, 201, 176, 0.14);
      border: 1px solid rgba(78, 201, 176, 0.28);
      color: #7bd9c8;
    }
    .mem-badge-agent {
      background: rgba(232, 160, 48, 0.14);
      border: 1px solid rgba(232, 160, 48, 0.32);
      color: #e8a030;
    }
  `],
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
  /** Active filter: 'all' | 'shared' | 'agent' */
  readonly filter        = signal<McpMemoryFilter>('all');
  /** Show all entries (true) or just the first MEM_PREVIEW (false). */
  readonly showAll       = signal(false);
  /** Expose constant so the template can read it. */
  readonly memPreview    = MEM_PREVIEW;

  /** Total count of entries matching the current filter (before show-all capping). */
  readonly totalFilteredCount = computed(() => {
    const f = this.filter();
    const all = this.entries();
    // eslint-disable-next-line eqeqeq
    if (f === 'shared') { return all.filter(e => e.agentId == null).length; }
    // eslint-disable-next-line eqeqeq
    if (f === 'agent')  { return all.filter(e => e.agentId != null).length; }
    return all.length;
  });

  readonly filteredEntries = computed(() => {
    const f = this.filter();
    const all = this.entries();
    let result: McpMemoryEntry[];
    // Use loose equality to catch both null and undefined from the JSON response
    // eslint-disable-next-line eqeqeq
    if (f === 'shared') { result = all.filter(e => e.agentId == null); }
    // eslint-disable-next-line eqeqeq
    else if (f === 'agent') { result = all.filter(e => e.agentId != null); }
    else { result = all; }
    return this.showAll() ? result : result.slice(0, MEM_PREVIEW);
  });

  // ── Public API ──────────────────────────────────────────────────────────────

  constructor() {
    // Reset show-all when the filter changes so the preview is fresh.
    effect(() => {
      this.filter();
      this.showAll.set(false);
    });
  }

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
