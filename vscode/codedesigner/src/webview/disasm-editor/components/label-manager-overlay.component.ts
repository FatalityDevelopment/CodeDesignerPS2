import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  signal,
  computed,
  output,
  viewChild,
  ElementRef,
} from '@angular/core';

export type LabelType = 'Address' | 'Struct' | 'Function' | 'Array' | 'ArrayOfPointers' | 'LinkedList' | 'String';

export interface LabelMemberEntry { offset: number; name: string; }
export interface LabelManagerEntry {
  address:  string;
  name:     string;
  comment?: string;
  /** When true, the entry is hidden from the disassembler view and structs panel. */
  hidden?:  boolean;
  /** When true, the live memory value at this address is polled and shown in the table. */
  dynamic?: boolean;
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

/** One row in the recursive label-manager tree view. */
export interface TreeRow {
  /** 'entry' = top-level label row; 'member' = struct member sub-row. */
  kind: 'entry' | 'member';
  /** Nesting depth (0 = top-level, 1 = first member level, etc.). */
  depth: number;
  /** Unique path string used as the expansion state key. */
  path: string;
  /** Populated for 'entry' rows. */
  entry?: LabelManagerEntry;
  /** Populated for 'member' rows. */
  memberName?: string;
  memberOffset?: number;
  /** The label in the current set that this member's name resolves to (if any). */
  resolvedEntry?: LabelManagerEntry;
  /** True when this row can be expanded to reveal sub-members. */
  expandable: boolean;
  /** For member rows at depth=1: the address of the parent struct-pointer entry. */
  parentEntryAddress?: string;
}

export type LabelManagerAction =
  | { type: 'setActive';         setName: string }
  | { type: 'createSet';         setName: string }
  | { type: 'deleteSet';         setName: string }
  | { type: 'addEntry';          setName: string; entry: LabelManagerEntry }
  | { type: 'deleteEntry';       setName: string; address: string }
  | { type: 'importSet';         setName: string; json: string }
  | { type: 'exportSet';         setName: string }
  | { type: 'resolveStrings' }
  | { type: 'resolveLinkedList'; address: string; nextOffset: number }
  | { type: 'refresh' }
  /** Navigate to the given absolute address (handled locally by the host component). */
  | { type: 'navigateToLabel';   address: string }
  /** Read the pointer at parentAddress, add offset, then navigate (handled by provider). */
  | { type: 'navigateToMember';  parentAddress: string; offset: number }
  /** Poll live 4-byte values at each address (handled by provider). */
  | { type: 'readLiveValues';    addresses: string[] };

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
      @if (!showNewSetInput()) {
        <button class="lm-tool-btn" title="Create a new label set (JSON file) for the current game" (click)="onNewSet()">&#43; New</button>
      }
      <!-- Inline new-set input (replaces prompt()) -->
      @if (showNewSetInput()) {
        <input class="lm-new-set-input" type="text" spellcheck="false"
               placeholder="Set name…"
               [value]="newSetNameInput()"
               (input)="newSetNameInput.set(asInput($event).value)"
               (keydown.enter)="onConfirmNewSet()"
               (keydown.escape)="onCancelNewSet()" />
        <button class="lm-tool-btn" [disabled]="!newSetNameInput().trim()" (click)="onConfirmNewSet()" title="Create set">&#x2713;</button>
        <button class="lm-tool-btn" (click)="onCancelNewSet()" title="Cancel">&#xD7;</button>
      }
      <button class="lm-tool-btn lm-danger-btn"
              title="Delete this label set"
              [disabled]="activeSetIsBuiltIn()"
              (click)="onDeleteSet()">&#x1F5D1; Delete Set</button>

      @if (!activeSetIsBuiltIn()) {
        <button class="lm-tool-btn lm-add-btn" title="Add a new label entry" (click)="onAddLabel()">&#43; Add Label</button>
      }

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
            <th class="lm-col-vis" title="Toggle all visibility">
              <button class="lm-vis-btn lm-vis-all-btn"
                      [class.lm-is-hidden]="!allVisible()"
                      [disabled]="activeSetIsBuiltIn() || filteredEntries().length === 0"
                      [title]="allVisible() ? 'Hide all' : 'Show all'"
                      (click)="onToggleAllVisibility()">
                <svg class="lm-eye-icon" viewBox="0 0 20 14" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round">
                  <path d="M1 7C3.5 3 6.5 1 10 1s6.5 2 9 6c-2.5 4-5.5 6-9 6S3.5 11 1 7z"/>
                  <circle cx="10" cy="7" r="2.5"/>
                  @if (!allVisible()) {
                    <line x1="3" y1="1" x2="17" y2="13"/>
                  }
                </svg>
              </button>
            </th>
            <th class="lm-col-expand"></th>
            <th class="lm-col-type">Type</th>
            <th class="lm-col-addr">Address</th>
            <th class="lm-col-name">Name</th>
            <th class="lm-col-comment">Comment</th>
            <th class="lm-col-members">Details</th>
            <th class="lm-col-live">Live Value</th>
            @if (!activeSetIsBuiltIn()) {
              <th class="lm-col-actions"></th>
            }
          </tr>
        </thead>
        <tbody>
          @for (row of treeRows(); track row.path) {
            @if (row.kind === 'entry') {
              <!-- Top-level label entry row -->
              <tr [class.lm-struct-row]="effectiveType(row.entry!) === 'Struct'"
                  [class.lm-fnc-row]="effectiveType(row.entry!) === 'Function'"
                  [class.lm-hidden-row]="row.entry!.hidden">
                <td class="lm-col-vis">
                  <button class="lm-vis-btn"
                          [class.lm-is-hidden]="row.entry!.hidden"
                          [title]="row.entry!.hidden ? 'Hidden — click to show' : 'Visible — click to hide'"
                          [disabled]="activeSetIsBuiltIn()"
                          (click)="onToggleVisibility(row.entry!)">
                    <svg class="lm-eye-icon" viewBox="0 0 20 14" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round">
                      <path d="M1 7C3.5 3 6.5 1 10 1s6.5 2 9 6c-2.5 4-5.5 6-9 6S3.5 11 1 7z"/>
                      <circle cx="10" cy="7" r="2.5"/>
                      @if (row.entry!.hidden) {
                        <line x1="3" y1="1" x2="17" y2="13"/>
                      }
                    </svg>
                  </button>
                </td>
                <td class="lm-col-expand">
                  @if (row.expandable) {
                    <button class="lm-expand-btn" [title]="isExpanded(row.path) ? 'Collapse' : 'Expand members'" (click)="toggleExpand(row.path)">
                      {{ isExpanded(row.path) ? '▼' : '▶' }}
                    </button>
                  }
                </td>
                <td class="lm-col-type">
                  <span class="lm-type-badge" [class]="typeBadgeClass(row.entry!)">{{ typeBadge(row.entry!) }}</span>
                </td>
                <td class="lm-col-addr lm-mono">
                  {{ row.entry!.address }}
                </td>
                <td class="lm-col-name">{{ displayName(row.entry!) }}</td>
                <td class="lm-col-comment lm-muted">{{ row.entry!.comment ?? '' }}</td>
                <td class="lm-col-members">{{ entryDetails(row.entry!) }}</td>
                <td class="lm-col-live">
                  @if (row.entry!.dynamic) {
                    <span class="lm-live-value">{{ liveValue(row.entry!) }}</span>
                  }
                </td>
                @if (!activeSetIsBuiltIn()) {
                  <td class="lm-col-actions">
                    <button class="lm-row-btn lm-dyn-toggle-btn"
                            [class.lm-is-dynamic]="row.entry!.dynamic"
                            [title]="row.entry!.dynamic ? 'Dynamic — click to set static' : 'Static — click to enable live value'"
                            (click)="onToggleDynamic(row.entry!)">
                      {{ row.entry!.dynamic ? 'Dyn' : 'Sta' }}
                    </button>
                    <button class="lm-row-btn" title="Edit" (click)="onEdit(row.entry!)">&#x270E;</button>
                    <button class="lm-row-btn lm-danger-btn" title="Delete" (click)="onDeleteEntry(row.entry!)">&#x1F5D1;</button>
                    @if (effectiveType(row.entry!) === 'LinkedList' && row.entry!.nodeNextOffset != null) {
                      <button class="lm-row-btn" title="Walk linked list (PINE)" (click)="onWalkList(row.entry!)">&#x21C0;</button>
                    }
                  </td>
                }
              </tr>
            } @else {
              <!-- Struct member sub-row (supports recursive depth via padding) -->
              <tr class="lm-member-sub-row">
                <td class="lm-col-vis"></td>
                <td class="lm-col-expand lm-member-tree-indent" [style.padding-left.px]="row.depth * 16">
                  @if (row.expandable) {
                    <button class="lm-expand-btn" [title]="isExpanded(row.path) ? 'Collapse' : 'Expand'" (click)="toggleExpand(row.path)">
                      {{ isExpanded(row.path) ? '▼' : '▶' }}
                    </button>
                  } @else {
                    <span class="lm-tree-leaf">└</span>
                  }
                </td>
                <td class="lm-col-type lm-mono lm-member-offset-cell">
                  +0x{{ row.memberOffset!.toString(16).toUpperCase().padStart(4, '0') }}
                </td>
                <td class="lm-col-addr">
                </td>
                <td class="lm-col-name lm-member-sub-name">
                  {{ row.memberName }}
                  @if (row.resolvedEntry) {
                    <span class="lm-member-type-hint lm-muted">&nbsp;{{ typeBadge(row.resolvedEntry) }}</span>
                  }
                </td>
                <td class="lm-col-comment lm-muted">{{ row.resolvedEntry?.comment ?? '' }}</td>
                <td class="lm-col-members">
                  @if (row.resolvedEntry) { {{ entryDetails(row.resolvedEntry) }} }
                </td>
                <td class="lm-col-live"></td>
                @if (!activeSetIsBuiltIn()) {
                  <td class="lm-col-actions"></td>
                }
              </tr>
            }
          }
          @if (filteredEntries().length === 0) {
            <tr><td [attr.colspan]="emptyColspan()" class="lm-empty-note">
              @if ((currentSet()?.entries?.length ?? 0) === 0) {
                @if (activeSetIsBuiltIn()) {
                  This is a read-only built-in set. Click <strong>+ New</strong> to create your own label set, then add labels using the <strong>+ Add Label</strong> button.
                } @else {
                  No labels yet. Click <strong>+ Add Label</strong> in the toolbar to create one.
                }
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
        <div class="lm-form-row">
          <label class="lm-form-label">Dynamic</label>
          <label class="lm-form-check">
            <input type="checkbox"
                   [checked]="editDynamic()"
                   (change)="editDynamic.set(asCheckbox($event).checked)" />
            Show live value in table (polls memory every 500 ms)
          </label>
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

  // ── DOM references ────────────────────────────────────────────────────────────
  private readonly _fileInputRef = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  // ── State ────────────────────────────────────────────────────────────────────
  readonly visible       = signal(false);
  readonly setNames      = signal<string[]>(['Built-in']);
  readonly activeSetName = signal('Built-in');
  readonly currentSet    = signal<LabelSetPayload | null>(null);
  readonly filterText    = signal('');
  readonly typeFilter    = signal('');
  readonly statusMsg     = signal('');
  readonly statusIsError = signal(false);

  // Tree view — track expanded paths by their unique path string
  readonly expandedPaths = signal<Set<string>>(new Set());

  // Inline new-set creation (replaces window.prompt)
  readonly showNewSetInput = signal(false);
  readonly newSetNameInput = signal('');

  // Edit form state
  readonly editAddress       = signal('');
  readonly editName          = signal('');
  readonly editComment       = signal('');
  readonly editType          = signal<string>('Address');
  readonly editHidden        = signal(false);
  readonly editDynamic       = signal(false);
  readonly editMembers       = signal<{ offsetStr: string; name: string }[]>([]);
  readonly editArgRegs       = signal('');
  readonly editReturnRegs    = signal('');
  readonly editElementStride = signal('');
  readonly editElementCount  = signal('');
  readonly editNodeNextOffset = signal('');
  readonly editNodeSize       = signal('');
  /** When editing an existing entry, this holds its original address. */
  readonly editingAddress = signal('');

  /** Map of address → live hex value string for dynamic entries. */
  readonly liveValues = signal<Map<string, string>>(new Map());

  /** Polling timer handle (setInterval id). */
  private _pollTimer: ReturnType<typeof setInterval> | null = null;

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

  /** True when all filtered visible entries have hidden=false (or undefined). */
  readonly allVisible = computed(() => {
    const entries = this.filteredEntries();
    return entries.length > 0 && entries.every(e => !e.hidden);
  });

  /** True when at least one filtered entry has dynamic=true. */
  readonly hasDynamicEntries = computed(() =>
    this.filteredEntries().some(e => e.dynamic),
  );

  /** Addresses of dynamic entries to poll. */
  private readonly _dynamicAddresses = computed(() =>
    this.filteredEntries().filter(e => e.dynamic).map(e => e.address),
  );

  /** Colspan for the "no entries" empty row. Live Value column is always shown. */
  readonly emptyColspan = computed(() =>
    this.activeSetIsBuiltIn() ? 7 : 8
  );

  /**
   * A name → entry map for Struct-type labels only, recomputed only when the
   * current set's entries change (not on filter changes).
   */
  private readonly _structByName = computed<Map<string, LabelManagerEntry>>(() => {
    const allEntries = this.currentSet()?.entries ?? [];
    const map = new Map<string, LabelManagerEntry>();
    for (const e of allEntries) {
      if ((e.type ?? 'Address') === 'Struct') {
        map.set(e.name, e);
      }
    }
    return map;
  });

  /**
   * A flat list of tree rows derived from `filteredEntries()`.
   * Top-level entries are at depth 0.  When a Struct row is expanded its
   * members appear immediately below it as depth-1 rows.  If a member's
   * name resolves to another Struct label in the current set that Struct can
   * itself be expanded, producing depth-2 rows – and so on recursively (with
   * cycle detection so circular references don't loop forever).
   */
  readonly treeRows = computed<TreeRow[]>(() => {
    const structByName = this._structByName();
    const rows: TreeRow[] = [];
    for (const entry of this.filteredEntries()) {
      const path = entry.address;
      const isStruct = (entry.type ?? 'Address') === 'Struct';
      rows.push({
        kind: 'entry',
        depth: 0,
        path,
        entry,
        expandable: isStruct && entry.members.length > 0,
      });
      if (this.expandedPaths().has(path) && isStruct) {
        this._addMemberRows(rows, entry.members, structByName, path, 1, new Set([path]), entry.address);
      }
    }
    return rows;
  });

  // ── Public API ────────────────────────────────────────────────────────────────

  open(): void {
    this.visible.set(true);
    this.action$.emit({ type: 'refresh' });
    this._startPolling();
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
    this._startPolling();
  }

  close(): void {
    if (this.visible()) {
      this.visible.set(false);
      this._stopPolling();
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

    if (pending) {
      // A context-menu "Add Label" was triggered — apply the prefill.
      this.clearForm();
      this.editAddress.set('0x' + pending);
      const existing = set.entries.find(
        e => e.address.toUpperCase().replace(/^0x/i, '') === pending,
      );
      if (existing) {
        this.onEdit(existing);
      }
      // If the active set is read-only, keep the pending address so it is
      // reapplied automatically when the user switches to (or creates) a user set.
      if (set.isBuiltIn) {
        this.statusMsg.set('Create or select a user label set (via + New) to add this address.');
        this.statusIsError.set(false);
      } else {
        // Successfully landed on a writable set — clear the pending address and scroll.
        this._pendingPrefillAddress = null;
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
    this.newSetNameInput.set('');
    this.showNewSetInput.set(true);
    requestAnimationFrame(() => {
      (document.querySelector('.lm-new-set-input') as HTMLInputElement | null)?.focus();
    });
  }

  onConfirmNewSet(): void {
    const name = this.newSetNameInput().trim();
    if (!name) { return; }
    this.showNewSetInput.set(false);
    this.newSetNameInput.set('');
    this.action$.emit({ type: 'createSet', setName: name });
  }

  onCancelNewSet(): void {
    this.showNewSetInput.set(false);
    this.newSetNameInput.set('');
  }

  onDeleteSet(): void {
    const name = this.activeSetName();
    if (!confirm(`Delete label set "${name}"? This cannot be undone.`)) { return; }
    this.action$.emit({ type: 'deleteSet', setName: name });
  }

  onAddLabel(): void {
    this.clearForm();
    requestAnimationFrame(() => {
      const form = document.getElementById('lm-edit-form');
      form?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
      const firstInput = form?.querySelector('input') as HTMLInputElement | null;
      firstInput?.focus();
    });
  }

  onToggleVisibility(entry: LabelManagerEntry): void {
    const updated: LabelManagerEntry = { ...entry, hidden: !entry.hidden };
    // Optimistic update so the icon flips immediately without waiting for the server round-trip.
    const set = this.currentSet();
    if (set) {
      this.currentSet.set({
        ...set,
        entries: set.entries.map(e => e.address === entry.address ? updated : e),
      });
    }
    this.action$.emit({ type: 'addEntry', setName: this.activeSetName(), entry: updated });
  }

  // ── Tree view ─────────────────────────────────────────────────────────────────

  toggleExpand(path: string): void {
    this.expandedPaths.update(set => {
      const next = new Set(set);
      if (next.has(path)) { next.delete(path); } else { next.add(path); }
      return next;
    });
  }

  isExpanded(path: string): boolean {
    return this.expandedPaths().has(path);
  }

  /** Recursively appends member rows to `rows`, with cycle detection via `visited`. */
  private _addMemberRows(
    rows: TreeRow[],
    members: LabelMemberEntry[],
    structByName: Map<string, LabelManagerEntry>,
    parentPath: string,
    depth: number,
    visited: Set<string>,
    parentEntryAddress?: string,
  ): void {
    for (const m of members) {
      const path = `${parentPath}:${m.offset}`;
      const resolved = structByName.get(m.name);
      const canExpand = resolved != null
        && resolved.members.length > 0
        && !visited.has(resolved.address);
      rows.push({
        kind: 'member',
        depth,
        path,
        memberName: m.name,
        memberOffset: m.offset,
        resolvedEntry: resolved,
        expandable: canExpand,
        parentEntryAddress,
      });
      if (canExpand && this.expandedPaths().has(path)) {
        const nextVisited = new Set(visited);
        nextVisited.add(resolved!.address);
        // Deeper levels don't pass parentEntryAddress (goto only supported at depth=1).
        this._addMemberRows(rows, resolved!.members, structByName, path, depth + 1, nextVisited);
      }
    }
  }

  onToggleAllVisibility(): void {
    const makeHidden = this.allVisible();
    const setName = this.activeSetName();
    for (const entry of this.filteredEntries()) {
      const updated: LabelManagerEntry = { ...entry, hidden: makeHidden };
      this.action$.emit({ type: 'addEntry', setName, entry: updated });
    }
  }

  onEdit(entry: LabelManagerEntry): void {
    this.editAddress.set(entry.address);
    this.editName.set(entry.name);
    this.editComment.set(entry.comment ?? '');
    this.editType.set(entry.type ?? (entry.members.length > 0 ? 'Struct' : 'Address'));
    this.editHidden.set(entry.hidden ?? false);
    this.editDynamic.set(entry.dynamic ?? false);
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

  onToggleDynamic(entry: LabelManagerEntry): void {
    const updated: LabelManagerEntry = { ...entry, dynamic: entry.dynamic ? undefined : true };
    // Optimistic local update so the toggle feels instant.
    const set = this.currentSet();
    if (set) {
      this.currentSet.set({
        ...set,
        entries: set.entries.map(e => e.address === entry.address ? updated : e),
      });
    }
    this.action$.emit({ type: 'addEntry', setName: this.activeSetName(), entry: updated });
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
      // Preserve hidden state when editing; default to false (visible) for new entries.
      hidden: this.editingAddress() ? this.editHidden() : false,
      dynamic: this.editDynamic() ? true : undefined,
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
    this._fileInputRef()?.nativeElement?.click();
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
    this.editHidden.set(false);
    this.editDynamic.set(false);
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
  asCheckbox(event: Event): HTMLInputElement { return event.target as HTMLInputElement; }

  // ── Navigation ────────────────────────────────────────────────────────────────

  onGotoLabel(entry: LabelManagerEntry): void {
    this.action$.emit({ type: 'navigateToLabel', address: entry.address });
  }

  onGotoMember(parentAddress: string, offset: number): void {
    this.action$.emit({ type: 'navigateToMember', parentAddress, offset });
  }

  // ── Live value polling ────────────────────────────────────────────────────────

  /** Update the live value map from a provider response. */
  setLiveValues(values: Record<string, string>): void {
    const map = new Map<string, string>(Object.entries(values));
    this.liveValues.set(map);
  }

  liveValue(entry: LabelManagerEntry): string {
    return this.liveValues().get(entry.address) ?? '…';
  }

  /** Start live-value polling. Called by the overlay itself and by the page-mode host. */
  startPolling(): void {
    this._startPolling();
  }

  private _startPolling(): void {
    this._stopPolling();
    this._pollTimer = setInterval(() => {
      const addresses = this._dynamicAddresses();
      if (addresses.length > 0) {
        this.action$.emit({ type: 'readLiveValues', addresses });
      }
    }, 500);
  }

  private _stopPolling(): void {
    if (this._pollTimer !== null) {
      clearInterval(this._pollTimer);
      this._pollTimer = null;
    }
  }
}

function splitLines(text: string): string[] {
  return text.split('\n').map(s => s.trim()).filter(Boolean);
}
