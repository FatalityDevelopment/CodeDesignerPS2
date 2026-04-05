import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  signal,
  computed,
  output,
  input,
} from '@angular/core';

export interface LabelMemberEntry { offset: number; name: string; }
export interface LabelManagerEntry {
  address:  string;
  name:     string;
  comment?: string;
  /** When true, the entry is hidden from the disassembler view and structs panel. */
  hidden?:  boolean;
  members:  LabelMemberEntry[];
}
export interface LabelSetPayload {
  setName:   string;
  gameId?:   string;
  isBuiltIn: boolean;
  entries:   LabelManagerEntry[];
}

export type LabelManagerAction =
  | { type: 'setActive';     setName: string }
  | { type: 'createSet';     setName: string }
  | { type: 'deleteSet';     setName: string }
  | { type: 'addEntry';      setName: string; entry: LabelManagerEntry }
  | { type: 'deleteEntry';   setName: string; address: string }
  | { type: 'importSet';     setName: string; json: string }
  | { type: 'exportSet';     setName: string }
  | { type: 'refresh' };

@Component({
  selector: 'app-label-manager-overlay',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
<div id="lm-overlay"
     [class.visible]="visible()"
     (click)="onBackdropClick($event)"
     (keydown.escape)="close()">
  <div id="lm-box">

    <!-- Header -->
    <div id="lm-header">
      <span id="lm-title">&#128211; Label Manager</span>
      <button class="lm-close-btn" title="Close (Escape)" (click)="close()">&#xD7;</button>
    </div>

    <!-- Toolbar row -->
    <div id="lm-toolbar">
      <!-- Set selector -->
      <select id="lm-set-select" [value]="activeSetName()" (change)="onSetChange(asSelect($event).value)">
        @for (s of setNames(); track s) {
          <option [value]="s">{{ s }}</option>
        }
      </select>

      <!-- Set management buttons -->
      <button class="lm-tool-btn" title="New label set" (click)="onNewSet()">&#43; New</button>
      <button class="lm-tool-btn lm-danger-btn"
              title="Delete this label set"
              [disabled]="activeSetIsBuiltIn()"
              (click)="onDeleteSet()">&#x1F5D1; Delete Set</button>

      <span class="lm-spacer"></span>

      <!-- Import / Export -->
      <button class="lm-tool-btn" title="Import labels from a JSON file" (click)="triggerImport()">&#x2B06; Import</button>
      <button class="lm-tool-btn" title="Export this set as JSON" [disabled]="activeSetIsBuiltIn()" (click)="onExport()">&#x2B07; Export</button>
      <input #fileInput type="file" accept=".json" style="display:none" (change)="onFileSelected($event)" />

      <button class="lm-tool-btn lm-refresh-btn" title="Refresh" (click)="action$.emit({ type: 'refresh' })">&#x21BB;</button>
    </div>

    <!-- Filter row -->
    <div id="lm-filter-row">
      <input id="lm-filter"
             type="text"
             placeholder="Filter by address or name…"
             spellcheck="false"
             [value]="filterText()"
             (input)="filterText.set(asInput($event).value)" />
      @if (filterText()) {
        <button class="lm-clear-filter" (click)="filterText.set('')">&#xD7;</button>
      }
      <span id="lm-count">{{ filteredEntries().length }} / {{ currentSet()?.entries?.length ?? 0 }}</span>
    </div>

    <!-- Table -->
    <div id="lm-table-wrap">
      <table id="lm-table">
        <thead>
          <tr>
            <th class="lm-col-vis" title="Visibility in disassembler / structs panel">&#x1F441;</th>
            <th class="lm-col-addr">Address</th>
            <th class="lm-col-name">Name</th>
            <th class="lm-col-comment">Comment</th>
            <th class="lm-col-members">Members</th>
            @if (!activeSetIsBuiltIn()) {
              <th class="lm-col-actions"></th>
            }
          </tr>
        </thead>
        <tbody>
          @for (entry of filteredEntries(); track entry.address) {
            <tr [class.lm-struct-row]="entry.members.length > 0" [class.lm-hidden-row]="entry.hidden">
              <td class="lm-col-vis">
                <button class="lm-vis-btn"
                        [title]="entry.hidden ? 'Hidden — click to show in disassembler / structs' : 'Visible — click to hide from disassembler / structs'"
                        [disabled]="activeSetIsBuiltIn()"
                        (click)="onToggleVisibility(entry)">
                  {{ entry.hidden ? '&#x1F648;' : '&#x1F441;' }}
                </button>
              </td>
              <td class="lm-col-addr lm-mono">{{ entry.address }}</td>
              <td class="lm-col-name">{{ entry.name }}</td>
              <td class="lm-col-comment lm-muted">{{ entry.comment ?? '' }}</td>
              <td class="lm-col-members">
                @if (entry.members.length > 0) {
                  <span class="lm-member-badge" [title]="memberSummary(entry)">
                    {{ entry.members.length }} member{{ entry.members.length !== 1 ? 's' : '' }}
                  </span>
                } @else {
                  <span class="lm-muted">—</span>
                }
              </td>
              @if (!activeSetIsBuiltIn()) {
                <td class="lm-col-actions">
                  <button class="lm-row-btn" title="Edit" (click)="onEdit(entry)">&#x270E;</button>
                  <button class="lm-row-btn lm-danger-btn" title="Delete" (click)="onDeleteEntry(entry)">&#x1F5D1;</button>
                </td>
              }
            </tr>
          }
          @if (filteredEntries().length === 0) {
            <tr><td [attr.colspan]="activeSetIsBuiltIn() ? 5 : 6" class="lm-empty-note">
              @if ((currentSet()?.entries?.length ?? 0) === 0) {
                No labels yet. Click <strong>+ Add</strong> to create one.
              } @else {
                No labels match the current filter.
              }
            </td></tr>
          }
        </tbody>
      </table>
    </div>

    <!-- Add/Edit form -->
    @if (!activeSetIsBuiltIn()) {
      <div id="lm-edit-form">
        <div class="lm-form-row">
          <label class="lm-form-label">Address</label>
          <input class="lm-form-input lm-mono" type="text" spellcheck="false"
                 placeholder="0x80010000"
                 [value]="editAddress()"
                 (input)="editAddress.set(asInput($event).value)" />
        </div>
        <div class="lm-form-row">
          <label class="lm-form-label">Name</label>
          <input class="lm-form-input" type="text" spellcheck="false"
                 placeholder="e.g. PlayerHealth"
                 [value]="editName()"
                 (input)="editName.set(asInput($event).value)" />
        </div>
        <div class="lm-form-row">
          <label class="lm-form-label">Comment</label>
          <input class="lm-form-input" type="text" spellcheck="false"
                 placeholder="optional description"
                 [value]="editComment()"
                 (input)="editComment.set(asInput($event).value)" />
        </div>
        <!-- Members editor -->
        <div class="lm-form-row lm-members-row">
          <label class="lm-form-label">Members</label>
          <div class="lm-members-list">
            @for (m of editMembers(); track $index) {
              <div class="lm-member-item">
                <input class="lm-member-offset lm-mono" type="text" spellcheck="false"
                       placeholder="0x00"
                       [value]="m.offsetStr"
                       (input)="updateMemberOffset($index, asInput($event).value)" />
                <input class="lm-member-name" type="text" spellcheck="false"
                       placeholder="field name"
                       [value]="m.name"
                       (input)="updateMemberName($index, asInput($event).value)" />
                <button class="lm-member-del" (click)="removeMember($index)">&#xD7;</button>
              </div>
            }
            <button class="lm-tool-btn" (click)="addMember()">&#43; Member</button>
          </div>
        </div>
        <div class="lm-form-row lm-form-actions">
          <button class="lm-tool-btn" [disabled]="!canSave()" (click)="onSave()">
            {{ editingAddress() ? 'Update' : 'Add Label' }}
          </button>
          @if (editingAddress()) {
            <button class="lm-tool-btn" (click)="clearForm()">Cancel</button>
          }
        </div>
      </div>
    }

    <!-- Status bar -->
    @if (statusMsg()) {
      <div id="lm-status" [class.lm-status-error]="statusIsError()">{{ statusMsg() }}</div>
    }

  </div>
</div>
  `,
})
export class LabelManagerOverlayComponent {

  // ── Outputs ──────────────────────────────────────────────────────────────────
  readonly close$  = output<void>();
  readonly action$ = output<LabelManagerAction>();

  // ── State ────────────────────────────────────────────────────────────────────
  readonly visible       = signal(false);
  readonly setNames      = signal<string[]>(['Built-in']);
  readonly activeSetName = signal('Built-in');
  readonly currentSet    = signal<LabelSetPayload | null>(null);
  readonly filterText    = signal('');
  readonly statusMsg     = signal('');
  readonly statusIsError = signal(false);

  // Edit form state
  readonly editAddress  = signal('');
  readonly editName     = signal('');
  readonly editComment  = signal('');
  readonly editMembers  = signal<{ offsetStr: string; name: string }[]>([]);
  /** When editing an existing entry, this holds its original address. */
  readonly editingAddress = signal('');

  // ── Computed ──────────────────────────────────────────────────────────────────
  readonly activeSetIsBuiltIn = computed(() =>
    this.activeSetName() === 'Built-in' || (this.currentSet()?.isBuiltIn ?? false)
  );

  readonly filteredEntries = computed(() => {
    const entries = this.currentSet()?.entries ?? [];
    const f = this.filterText().trim().toLowerCase();
    if (!f) { return entries; }
    return entries.filter(e =>
      e.address.toLowerCase().includes(f) ||
      e.name.toLowerCase().includes(f) ||
      (e.comment?.toLowerCase().includes(f) ?? false)
    );
  });

  readonly canSave = computed(() =>
    this.editAddress().trim() !== '' && this.editName().trim() !== ''
  );

  // ── Public API ────────────────────────────────────────────────────────────────

  open(): void {
    this.visible.set(true);
    this.action$.emit({ type: 'refresh' });
  }

  close(): void {
    if (this.visible()) {
      this.visible.set(false);
      this.close$.emit();
    }
  }

  setSets(names: string[]): void {
    // Ensure "Built-in" is always first.
    const rest = names.filter(n => n !== 'Built-in');
    this.setNames.set(['Built-in', ...rest]);
  }

  setActiveSet(set: LabelSetPayload): void {
    this.activeSetName.set(set.setName);
    this.currentSet.set(set);
    this.clearForm();
    this.statusMsg.set('');
  }

  showStatus(msg: string, isError = false): void {
    this.statusMsg.set(msg);
    this.statusIsError.set(isError);
    if (!isError) {
      setTimeout(() => { if (this.statusMsg() === msg) { this.statusMsg.set(''); } }, 3000);
    }
  }

  // ── Event Handlers ────────────────────────────────────────────────────────────

  onSetChange(name: string): void {
    this.action$.emit({ type: 'setActive', setName: name });
  }

  onNewSet(): void {
    const name = prompt('Enter a name for the new label set:')?.trim();
    if (!name) { return; }
    this.action$.emit({ type: 'createSet', setName: name });
  }

  onDeleteSet(): void {
    const name = this.activeSetName();
    if (!confirm(`Delete label set "${name}"? This cannot be undone.`)) { return; }
    this.action$.emit({ type: 'deleteSet', setName: name });
  }

  onToggleVisibility(entry: LabelManagerEntry): void {
    const updated: LabelManagerEntry = { ...entry, hidden: !entry.hidden };
    this.action$.emit({ type: 'addEntry', setName: this.activeSetName(), entry: updated });
  }

  onEdit(entry: LabelManagerEntry): void {
    this.editAddress.set(entry.address);
    this.editName.set(entry.name);
    this.editComment.set(entry.comment ?? '');
    this.editMembers.set(entry.members.map(m => ({ offsetStr: '0x' + m.offset.toString(16).toUpperCase().padStart(2, '0'), name: m.name })));
    this.editingAddress.set(entry.address);
  }

  onDeleteEntry(entry: LabelManagerEntry): void {
    if (!confirm(`Delete label "${entry.name}" at ${entry.address}?`)) { return; }
    this.action$.emit({ type: 'deleteEntry', setName: this.activeSetName(), address: entry.address });
  }

  onSave(): void {
    const addr = this.editAddress().trim();
    const name = this.editName().trim();
    if (!addr || !name) { return; }

    const members = this.editMembers()
      .filter(m => m.name.trim())
      .map(m => ({
        offset: parseInt(m.offsetStr.replace(/^0x/i, ''), 16) || 0,
        name:   m.name.trim(),
      }));

    const entry: LabelManagerEntry = {
      address: addr,
      name,
      comment: this.editComment().trim() || undefined,
      members,
    };
    this.action$.emit({ type: 'addEntry', setName: this.activeSetName(), entry });
    this.clearForm();
  }

  onExport(): void {
    this.action$.emit({ type: 'exportSet', setName: this.activeSetName() });
  }

  triggerImport(): void {
    // Programmatically click the hidden file input.
    const inp = document.getElementById('lm-file-input') as HTMLInputElement | null;
    inp?.click();
  }

  onFileSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) { return; }
    const reader = new FileReader();
    reader.onload = (e) => {
      const json = e.target?.result as string;
      const setName = file.name.replace(/\.json$/i, '');
      this.action$.emit({ type: 'importSet', setName, json });
    };
    reader.readAsText(file);
    (event.target as HTMLInputElement).value = '';
  }

  onBackdropClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).id === 'lm-overlay') { this.close(); }
  }

  // ── Member editor ─────────────────────────────────────────────────────────────

  addMember(): void {
    this.editMembers.update(m => [...m, { offsetStr: '0x00', name: '' }]);
  }

  removeMember(index: number): void {
    this.editMembers.update(m => m.filter((_, i) => i !== index));
  }

  updateMemberOffset(index: number, value: string): void {
    this.editMembers.update(m => m.map((item, i) => i === index ? { ...item, offsetStr: value } : item));
  }

  updateMemberName(index: number, value: string): void {
    this.editMembers.update(m => m.map((item, i) => i === index ? { ...item, name: value } : item));
  }

  clearForm(): void {
    this.editAddress.set('');
    this.editName.set('');
    this.editComment.set('');
    this.editMembers.set([]);
    this.editingAddress.set('');
  }

  // ── Helpers ───────────────────────────────────────────────────────────────────

  memberSummary(entry: LabelManagerEntry): string {
    return entry.members.map(m => `+0x${m.offset.toString(16).toUpperCase().padStart(2, '0')}: ${m.name}`).join('\n');
  }

  asInput(event: Event): HTMLInputElement { return event.target as HTMLInputElement; }
  asSelect(event: Event): HTMLSelectElement { return event.target as HTMLSelectElement; }
}
