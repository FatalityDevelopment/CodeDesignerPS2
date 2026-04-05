import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  signal,
  computed,
  output,
} from '@angular/core';

export type LabelType = 'Address' | 'Struct' | 'Function' | 'Array' | 'ArrayOfPointers' | 'LinkedList' | 'String';

export interface LabelMemberEntry { offset: number; name: string; }
export interface LabelManagerEntry {
  address:  string;
  name:     string;
  comment?: string;
  /** When true, the entry is hidden from the disassembler view and structs panel. */
  hidden?:  boolean;
  /** Semantic label type. Undefined is backward-compatible (inferred from members). */
  type?:    LabelType;
  members:  LabelMemberEntry[];
  // Function
  argRegs?:    string[];
  returnRegs?: string[];
  // Array
  elementStride?: number;
  elementCount?:  number;
  // LinkedList
  nodeNextOffset?: number;
  nodeSize?:       number;
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
  | { type: 'resolveStrings' }
  | { type: 'resolveLinkedList'; address: string; nextOffset: number }
  | { type: 'refresh' };

const TYPE_BADGE: Record<string, string> = {
  Address:        '[Addr]',
  Struct:         '[Struct]',
  Function:       '[FNC]',
  Array:          '[Array]',
  ArrayOfPointers:'[Ptrs]',
  LinkedList:     '[List]',
  String:         '[Str]',
};

const TYPE_BADGE_CLASS: Record<string, string> = {
  Address:        'lm-badge-addr',
  Struct:         'lm-badge-struct',
  Function:       'lm-badge-fnc',
  Array:          'lm-badge-array',
  ArrayOfPointers:'lm-badge-ptrs',
  LinkedList:     'lm-badge-list',
  String:         'lm-badge-str',
};

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

      <!-- Runtime resolution (PINE only) -->
      <button class="lm-tool-btn" title="Resolve String labels from live PCSX2 memory (PINE mode)" (click)="action$.emit({ type: 'resolveStrings' })">&#x1F50D; Strings</button>

      <!-- Import / Export -->
      <button class="lm-tool-btn" title="Import labels from a JSON file" (click)="triggerImport()">&#x2B06; Import</button>
      <button class="lm-tool-btn" title="Export this set as JSON" [disabled]="activeSetIsBuiltIn()" (click)="onExport()">&#x2B07; Export</button>
      <input #fileInput type="file" accept=".json" style="display:none" (change)="onFileSelected($event)" />

      <button class="lm-tool-btn lm-refresh-btn" title="Refresh" (click)="action$.emit({ type: 'refresh' })">&#x21BB;</button>
    </div>

    <!-- Filter / type-filter row -->
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
      <select id="lm-type-filter" [value]="typeFilter()" (change)="typeFilter.set(asSelect($event).value)">
        <option value="">All types</option>
        <option value="Address">Address</option>
        <option value="Struct">Struct</option>
        <option value="Function">Function</option>
        <option value="Array">Array</option>
        <option value="ArrayOfPointers">Array of Pointers</option>
        <option value="LinkedList">Linked List</option>
        <option value="String">String</option>
      </select>
      <span id="lm-count">{{ filteredEntries().length }} / {{ currentSet()?.entries?.length ?? 0 }}</span>
    </div>

    <!-- Table -->
    <div id="lm-table-wrap">
      <table id="lm-table">
        <thead>
          <tr>
            <th class="lm-col-vis" title="Visibility in disassembler / structs panel">👁</th>
            <th class="lm-col-type">Type</th>
            <th class="lm-col-addr">Address</th>
            <th class="lm-col-name">Name</th>
            <th class="lm-col-comment">Comment</th>
            <th class="lm-col-members">Details</th>
            @if (!activeSetIsBuiltIn()) {
              <th class="lm-col-actions"></th>
            }
          </tr>
        </thead>
        <tbody>
          @for (entry of filteredEntries(); track entry.address) {
            <tr [class.lm-struct-row]="effectiveType(entry) === 'Struct'"
                [class.lm-fnc-row]="effectiveType(entry) === 'Function'"
                [class.lm-hidden-row]="entry.hidden">
              <td class="lm-col-vis">
                <button class="lm-vis-btn"
                        [title]="entry.hidden ? 'Hidden — click to show' : 'Visible — click to hide'"
                        [disabled]="activeSetIsBuiltIn()"
                        (click)="onToggleVisibility(entry)">
                  {{ entry.hidden ? '🙈' : '👁' }}
                </button>
              </td>
              <td class="lm-col-type">
                <span class="lm-type-badge" [class]="typeBadgeClass(entry)">{{ typeBadge(entry) }}</span>
              </td>
              <td class="lm-col-addr lm-mono">{{ entry.address }}</td>
              <td class="lm-col-name">{{ displayName(entry) }}</td>
              <td class="lm-col-comment lm-muted">{{ entry.comment ?? '' }}</td>
              <td class="lm-col-members">{{ entryDetails(entry) }}</td>
              @if (!activeSetIsBuiltIn()) {
                <td class="lm-col-actions">
                  <button class="lm-row-btn" title="Edit" (click)="onEdit(entry)">&#x270E;</button>
                  <button class="lm-row-btn lm-danger-btn" title="Delete" (click)="onDeleteEntry(entry)">&#x1F5D1;</button>
                  @if (effectiveType(entry) === 'LinkedList' && entry.nodeNextOffset != null) {
                    <button class="lm-row-btn" title="Walk linked list (PINE)" (click)="onWalkList(entry)">&#x21C0;</button>
                  }
                </td>
              }
            </tr>
          }
          @if (filteredEntries().length === 0) {
            <tr><td [attr.colspan]="activeSetIsBuiltIn() ? 6 : 7" class="lm-empty-note">
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
        <!-- Type selector -->
        <div class="lm-form-row">
          <label class="lm-form-label">Type</label>
          <select class="lm-form-input" [value]="editType()" (change)="editType.set(asSelect($event).value)">
            <option value="Address">Address</option>
            <option value="Struct">Struct</option>
            <option value="Function">Function</option>
            <option value="Array">Array</option>
            <option value="ArrayOfPointers">Array of Pointers</option>
            <option value="LinkedList">Linked List</option>
            <option value="String">String</option>
          </select>
        </div>
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

        <!-- Struct members editor -->
        @if (editType() === 'Struct') {
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
        }

        <!-- Function: arg + return registers -->
        @if (editType() === 'Function') {
          <div class="lm-form-row">
            <label class="lm-form-label">Arg Regs</label>
            <textarea class="lm-form-input lm-mono lm-textarea" spellcheck="false" rows="3"
                      placeholder="One per line, e.g.&#10;a0=playerPtr&#10;a1=flags"
                      [value]="editArgRegs()"
                      (input)="editArgRegs.set(asTextarea($event).value)"></textarea>
          </div>
          <div class="lm-form-row">
            <label class="lm-form-label">Return Regs</label>
            <textarea class="lm-form-input lm-mono lm-textarea" spellcheck="false" rows="2"
                      placeholder="One per line, e.g.&#10;v0=result"
                      [value]="editReturnRegs()"
                      (input)="editReturnRegs.set(asTextarea($event).value)"></textarea>
          </div>
        }

        <!-- Array: stride + count -->
        @if (editType() === 'Array') {
          <div class="lm-form-row">
            <label class="lm-form-label">Element Stride</label>
            <input class="lm-form-input lm-mono" type="number" min="1"
                   placeholder="bytes per element, e.g. 4"
                   [value]="editElementStride()"
                   (input)="editElementStride.set(asInput($event).value)" />
          </div>
          <div class="lm-form-row">
            <label class="lm-form-label">Element Count</label>
            <input class="lm-form-input lm-mono" type="number" min="1"
                   placeholder="leave blank if unknown"
                   [value]="editElementCount()"
                   (input)="editElementCount.set(asInput($event).value)" />
          </div>
        }

        <!-- LinkedList: next-pointer offset + node size -->
        @if (editType() === 'LinkedList') {
          <div class="lm-form-row">
            <label class="lm-form-label">Next-ptr Offset</label>
            <input class="lm-form-input lm-mono" type="text" spellcheck="false"
                   placeholder="0x00 — byte offset of next-node pointer"
                   [value]="editNodeNextOffset()"
                   (input)="editNodeNextOffset.set(asInput($event).value)" />
          </div>
          <div class="lm-form-row">
            <label class="lm-form-label">Node Size</label>
            <input class="lm-form-input lm-mono" type="number" min="1"
                   placeholder="total node size in bytes (optional)"
                   [value]="editNodeSize()"
                   (input)="editNodeSize.set(asInput($event).value)" />
          </div>
        }

        <!-- String: informational note -->
        @if (editType() === 'String') {
          <div class="lm-form-row">
            <span class="lm-muted lm-form-note">
              The address points to a null-terminated ASCII string.
              Click <strong>🔍 Strings</strong> in the toolbar to resolve content from live PCSX2 memory.
            </span>
          </div>
        }

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
  readonly typeFilter    = signal('');
  readonly statusMsg     = signal('');
  readonly statusIsError = signal(false);

  // Edit form state
  readonly editAddress       = signal('');
  readonly editName          = signal('');
  readonly editComment       = signal('');
  readonly editType          = signal<string>('Address');
  readonly editMembers       = signal<{ offsetStr: string; name: string }[]>([]);
  readonly editArgRegs       = signal('');
  readonly editReturnRegs    = signal('');
  readonly editElementStride = signal('');
  readonly editElementCount  = signal('');
  readonly editNodeNextOffset = signal('');
  readonly editNodeSize       = signal('');
  /** When editing an existing entry, this holds its original address. */
  readonly editingAddress = signal('');

  /**
   * Normalised address (no "0x" prefix, uppercase) that should be pre-filled
   * into the form the next time `setActiveSet` is called after a refresh.
   * Set by `openWithAddress`; cleared immediately once applied.
   */
  private _pendingPrefillAddress: string | null = null;

  // ── Computed ──────────────────────────────────────────────────────────────────
  readonly activeSetIsBuiltIn = computed(() =>
    this.activeSetName() === 'Built-in' || (this.currentSet()?.isBuiltIn ?? false)
  );

  readonly filteredEntries = computed(() => {
    const entries = this.currentSet()?.entries ?? [];
    const f  = this.filterText().trim().toLowerCase();
    const tf = this.typeFilter().trim();
    return entries.filter(e => {
      if (tf && this.effectiveType(e) !== tf) return false;
      if (!f) return true;
      return e.address.toLowerCase().includes(f) ||
             e.name.toLowerCase().includes(f) ||
             (e.comment?.toLowerCase().includes(f) ?? false);
    });
  });

  readonly canSave = computed(() =>
    this.editAddress().trim() !== '' && this.editName().trim() !== ''
  );

  // ── Public API ────────────────────────────────────────────────────────────────

  open(): void {
    this.visible.set(true);
    this.action$.emit({ type: 'refresh' });
  }

  /**
   * Opens the overlay pre-filled with the given address.
   * If the refreshed set already contains an entry for this address, all form
   * fields are populated so the user can edit the full label definition.
   * Otherwise only the address field is pre-filled.
   */
  openWithAddress(addressHex: string): void {
    // Store the normalised address so setActiveSet() can apply it once the
    // refresh response arrives (avoiding a race where clearForm() wipes the field).
    this._pendingPrefillAddress = addressHex.toUpperCase().replace(/^0x/i, '');
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

    const pending = this._pendingPrefillAddress;
    this._pendingPrefillAddress = null;

    if (pending) {
      // A context-menu "Add to Label Manager" was triggered — apply the prefill.
      this.clearForm();
      this.editAddress.set('0x' + pending);
      const existing = set.entries.find(
        e => e.address.toUpperCase().replace(/^0x/i, '') === pending,
      );
      if (existing) {
        this.onEdit(existing);
      }
      // If the active set is read-only, guide the user.
      if (set.isBuiltIn) {
        this.statusMsg.set('Select or create a user label set to add this address.');
        this.statusIsError.set(false);
      } else {
        requestAnimationFrame(() => {
          document.getElementById('lm-edit-form')?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        });
      }
    } else {
      this.clearForm();
      this.statusMsg.set('');
    }
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
    this.editType.set(entry.type ?? (entry.members.length > 0 ? 'Struct' : 'Address'));
    this.editMembers.set(entry.members.map(m => ({ offsetStr: '0x' + m.offset.toString(16).toUpperCase().padStart(2, '0'), name: m.name })));
    this.editArgRegs.set((entry.argRegs ?? []).join('\n'));
    this.editReturnRegs.set((entry.returnRegs ?? []).join('\n'));
    this.editElementStride.set(entry.elementStride != null ? String(entry.elementStride) : '');
    this.editElementCount.set(entry.elementCount != null ? String(entry.elementCount) : '');
    this.editNodeNextOffset.set(entry.nodeNextOffset != null ? '0x' + entry.nodeNextOffset.toString(16).toUpperCase().padStart(2, '0') : '');
    this.editNodeSize.set(entry.nodeSize != null ? String(entry.nodeSize) : '');
    this.editingAddress.set(entry.address);
  }

  onDeleteEntry(entry: LabelManagerEntry): void {
    if (!confirm(`Delete label "${entry.name}" at ${entry.address}?`)) { return; }
    this.action$.emit({ type: 'deleteEntry', setName: this.activeSetName(), address: entry.address });
  }

  onWalkList(entry: LabelManagerEntry): void {
    if (entry.nodeNextOffset == null) { return; }
    this.action$.emit({ type: 'resolveLinkedList', address: entry.address, nextOffset: entry.nodeNextOffset });
  }

  onSave(): void {
    const addr = this.editAddress().trim();
    const name = this.editName().trim();
    if (!addr || !name) { return; }

    const type = this.editType() as LabelType;

    const members = (type === 'Struct')
      ? this.editMembers()
          .filter(m => m.name.trim())
          .map(m => ({ offset: parseInt(m.offsetStr.replace(/^0x/i, ''), 16) || 0, name: m.name.trim() }))
      : [];

    const argRegs    = (type === 'Function') ? splitLines(this.editArgRegs())    : undefined;
    const returnRegs = (type === 'Function') ? splitLines(this.editReturnRegs()) : undefined;

    const elementStride = (type === 'Array' && this.editElementStride()) ? parseInt(this.editElementStride(), 10) || undefined : undefined;
    const elementCount  = (type === 'Array' && this.editElementCount())  ? parseInt(this.editElementCount(), 10) || undefined  : undefined;

    const nodeNextOffset = (type === 'LinkedList' && this.editNodeNextOffset())
      ? parseInt(this.editNodeNextOffset().replace(/^0x/i, ''), 16) || 0
      : undefined;
    const nodeSize = (type === 'LinkedList' && this.editNodeSize()) ? parseInt(this.editNodeSize(), 10) || undefined : undefined;

    const entry: LabelManagerEntry = {
      address: addr,
      name,
      comment: this.editComment().trim() || undefined,
      type,
      members,
      argRegs:       argRegs?.length      ? argRegs      : undefined,
      returnRegs:    returnRegs?.length    ? returnRegs   : undefined,
      elementStride,
      elementCount,
      nodeNextOffset,
      nodeSize,
    };
    this.action$.emit({ type: 'addEntry', setName: this.activeSetName(), entry });
    this.clearForm();
  }

  onExport(): void {
    this.action$.emit({ type: 'exportSet', setName: this.activeSetName() });
  }

  triggerImport(): void {
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
    this.editType.set('Address');
    this.editMembers.set([]);
    this.editArgRegs.set('');
    this.editReturnRegs.set('');
    this.editElementStride.set('');
    this.editElementCount.set('');
    this.editNodeNextOffset.set('');
    this.editNodeSize.set('');
    this.editingAddress.set('');
  }

  // ── Display helpers ───────────────────────────────────────────────────────────

  effectiveType(entry: LabelManagerEntry): LabelType {
    return entry.type ?? 'Address';
  }

  typeBadge(entry: LabelManagerEntry): string {
    return TYPE_BADGE[this.effectiveType(entry)] ?? '[?]';
  }

  typeBadgeClass(entry: LabelManagerEntry): string {
    return 'lm-type-badge ' + (TYPE_BADGE_CLASS[this.effectiveType(entry)] ?? '');
  }

  displayName(entry: LabelManagerEntry): string {
    return this.effectiveType(entry) === 'Function' ? 'FNC_' + entry.name : entry.name;
  }

  entryDetails(entry: LabelManagerEntry): string {
    switch (this.effectiveType(entry)) {
      case 'Struct':
        return entry.members.length > 0
          ? `${entry.members.length} member${entry.members.length !== 1 ? 's' : ''}`
          : '—';
      case 'Function': {
        const parts: string[] = [];
        if (entry.argRegs?.length)    parts.push(`args: ${entry.argRegs.join(', ')}`);
        if (entry.returnRegs?.length) parts.push(`ret: ${entry.returnRegs.join(', ')}`);
        return parts.join(' | ') || '—';
      }
      case 'Array':
        return [
          entry.elementStride != null ? `stride: ${entry.elementStride}` : null,
          entry.elementCount  != null ? `count: ${entry.elementCount}`   : null,
        ].filter(Boolean).join(', ') || '—';
      case 'ArrayOfPointers':
        return entry.elementCount != null ? `count: ${entry.elementCount}` : '—';
      case 'LinkedList':
        return [
          entry.nodeNextOffset != null ? `next@+0x${entry.nodeNextOffset.toString(16).toUpperCase().padStart(2, '0')}` : null,
          entry.nodeSize       != null ? `size: ${entry.nodeSize}` : null,
        ].filter(Boolean).join(', ') || '—';
      default:
        return '—';
    }
  }

  memberSummary(entry: LabelManagerEntry): string {
    return entry.members.map(m => `+0x${m.offset.toString(16).toUpperCase().padStart(2, '0')}: ${m.name}`).join('\n');
  }

  asInput(event: Event): HTMLInputElement { return event.target as HTMLInputElement; }
  asTextarea(event: Event): HTMLTextAreaElement { return event.target as HTMLTextAreaElement; }
  asSelect(event: Event): HTMLSelectElement { return event.target as HTMLSelectElement; }
}

function splitLines(text: string): string[] {
  return text.split('\n').map(s => s.trim()).filter(Boolean);
}
