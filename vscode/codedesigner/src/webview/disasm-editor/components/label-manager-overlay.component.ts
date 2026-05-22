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
import { IconComponent } from '../../shared/icon.component';

export type LabelType =
  | 'Address' | 'Function' | 'String' | 'Pointer' | 'Float' | 'Value' | 'Hex'
  /** Legacy — mapped to Pointer at display time. */
  | 'Struct' | 'Array' | 'ArrayOfPointers' | 'LinkedList';

/**
 * A unified label entry. Labels are recursive: child entries have parentAddress + offset set
 * and their address is a synthetic path key (e.g. "0x80010000:+0x0010").
 */
export interface LabelManagerEntry {
  /** Real PS2 address for root entries; synthetic path key for child entries. */
  address:  string;
  /** Address of the parent label within the same set. Undefined for root entries. */
  parentAddress?: string;
  /** Byte offset from the dereferenced parent pointer. Only set when parentAddress is defined. */
  offset?: number;
  name:     string;
  comment?: string;
  /** Optional data-type annotation, e.g. "u32", "float", "pointer". */
  valueType?: string;
  /** When true, the entry is hidden from the disassembler view and structs panel. */
  hidden?:  boolean;
  /** When true, the live memory value at this address is polled and shown in the table. */
  dynamic?: boolean;
  /** Semantic label type. */
  type?:    LabelType;
  // Function
  argRegs?:    string[];
  returnRegs?: string[];
  // Pointer (parent — children hang off dereferenced base)
  // (no extra fields needed)
  // Array (legacy)
  elementStride?: number;
  elementCount?:  number;
  // LinkedList (legacy)
  nodeNextOffset?: number;
  nodeSize?:       number;
}
export interface LabelSetPayload {
  setName:      string;
  gameDiscId?:  string;
  isBuiltIn:    boolean;
  /** Flat list of ALL labels (roots + children at all depths). */
  entries:      LabelManagerEntry[];
}

/** One row in the recursive label-manager tree view. */
export interface TreeRow {
  /** 'entry' = top-level label row; 'member' = child label sub-row. */
  kind: 'entry' | 'member';
  /** Nesting depth (0 = top-level, 1 = first child level, etc.). */
  depth: number;
  /** Unique path string used as the expansion state key (= entry.address). */
  path: string;
  /** The full label entry for this row (always set). */
  entry: LabelManagerEntry;
  /** True when this row can be expanded to reveal child entries. */
  expandable: boolean;
  /**
   * True when this is the last child among its siblings.
   * Used to pick `└─` vs `├─` tree connector characters.
   * Always `true` for root (kind='entry') rows.
   */
  isLastChild: boolean;
  /**
   * For each ancestor depth level (index 0 = immediate parent's level),
   * `true` means that ancestor still has siblings below it — draw a `│` guide line.
   * Empty for root entries and depth-1 children.
   */
  ancestorLines: boolean[];
}

type LabelSortColumn = 'type' | 'address' | 'live' | 'name' | 'comment';
type LabelSortDirection = 'asc' | 'desc';

export type ScannerKind = 'functions' | 'pointers' | 'strings' | 'floats' | 'hexValues';

export type LabelManagerAction =
  | { type: 'setActive';         setName: string }
  | { type: 'createSet';         setName: string }
  | { type: 'deleteSet';         setName: string }
  | { type: 'renameSet';         setName: string; newSetName: string }
  | { type: 'addEntry';          setName: string; entry: LabelManagerEntry }
  | { type: 'deleteEntry';       setName: string; address: string }
  | { type: 'importSet';         setName: string; json: string }
  | { type: 'exportSet';         setName: string }
  | { type: 'resolveStrings' }
  | { type: 'resolveLinkedList'; address: string; nextOffset: number }
  | { type: 'refresh';           activeSetName?: string }
  /** Navigate to the given absolute address (handled locally by the host component). */
  | { type: 'navigateToLabel';   address: string }
  /** Read the pointer at parentAddress, add offset, then navigate (handled by provider). */
  | { type: 'navigateToMember';  parentAddress: string; offset: number }
  /** Poll live values at each address (handled by provider). String entries read as null-terminated strings. */
  | { type: 'readLiveValues';    setName?: string; entries: { address: string; entryType?: LabelType; valueType?: string }[] }
  /** Poll live values at resolved member addresses (handled by provider). */
  | { type: 'readMemberLiveValues'; setName?: string; members: { parentAddress: string; offset: number; path: string; entryType?: LabelType; valueType?: string }[] }
  /** Run a memory scanner and display results as entries in the tree. */
  | { type: 'runScanner';        scannerKind: ScannerKind }
  /** Clear scanner results. */
  | { type: 'clearScanResults' };

const TYPE_BADGE: Record<string, string> = {
  Address:  '[Addr]',
  Pointer:  '[Ptr]',
  Function: '[FNC]',
  Float:    '[Float]',
  Value:    '[Value]',
  String:   '[Str]',
  Hex:      '[Hex]',
  // legacy back-compat
  Struct:         '[Ptr]',
  Array:          '[Array]',
  ArrayOfPointers:'[AoP]',
  LinkedList:     '[List]',
};

const TYPE_BADGE_CLASS: Record<string, string> = {
  Address:  'lm-badge-addr',
  Pointer:  'lm-badge-ptr',
  Function: 'lm-badge-fnc',
  Float:    'lm-badge-float',
  Value:    'lm-badge-value',
  String:   'lm-badge-str',
  Hex:      'lm-badge-value',
  // legacy
  Struct:         'lm-badge-ptr',
  Array:          'lm-badge-array',
  ArrayOfPointers:'lm-badge-ptrs',
  LinkedList:     'lm-badge-list',
};

/**
 * The six types shown in all label-type dropdowns.
 */
const UNIFIED_TYPES: { value: string; label: string }[] = [
  { value: 'Address',  label: 'Address'  },
  { value: 'Pointer',  label: 'Pointer'  },
  { value: 'Float',    label: 'Float'    },
  { value: 'Value',    label: 'Value'    },
  { value: 'Function', label: 'FNC'      },
  { value: 'String',   label: 'String'   },
];

/**
 * All semantic LabelType values (controls which filter column is used).
 * Derived from UNIFIED_TYPES so the two stay in sync automatically.
 */
const SEMANTIC_LABEL_TYPES = new Set(UNIFIED_TYPES.map(t => t.value));

@Component({
  selector: 'app-label-manager-overlay',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  imports: [IconComponent],
  template: `
<div id="lm-overlay"
     [class.visible]="visible()"
     (click)="onBackdropClick($event)"
     (keydown.escape)="close()">
  <div id="lm-box">

    <!-- Header -->
    <div id="lm-header">
      <span id="lm-title">
        <app-icon name="tag" [size]="14" />
        Label Manager
      </span>
      <button class="lm-close-btn" title="Close (Escape)" (click)="close()">
        <app-icon name="x" [size]="16" />
      </button>
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
      @if (!showNewSetInput() && !showRenameSetInput()) {
        <button class="lm-tool-btn" title="Create a new label set (JSON file) for the current game" (click)="onNewSet()">
          <app-icon name="plus" [size]="12" /> New
        </button>
      }
      <!-- Inline new-set input (replaces prompt()) -->
      @if (showNewSetInput()) {
        <input class="lm-new-set-input" type="text" spellcheck="false"
               placeholder="Set name…"
               [value]="newSetNameInput()"
               (input)="newSetNameInput.set(asInput($event).value)"
               (keydown.enter)="onConfirmNewSet()"
               (keydown.escape)="onCancelNewSet()" />
        <button class="lm-tool-btn lm-icon-btn" [disabled]="!newSetNameInput().trim()" (click)="onConfirmNewSet()" title="Create set">
          <app-icon name="circlecheck" [size]="14" />
        </button>
        <button class="lm-tool-btn lm-icon-btn" (click)="onCancelNewSet()" title="Cancel">
          <app-icon name="x" [size]="14" />
        </button>
      }
      <!-- Inline rename-set input -->
      @if (showRenameSetInput()) {
        <input class="lm-rename-set-input" type="text" spellcheck="false"
               placeholder="New name…"
               [value]="renameSetInput()"
               (input)="renameSetInput.set(asInput($event).value)"
               (keydown.enter)="onConfirmRenameSet()"
               (keydown.escape)="onCancelRenameSet()" />
        <button class="lm-tool-btn lm-icon-btn" [disabled]="!renameSetInput().trim()" (click)="onConfirmRenameSet()" title="Rename set">
          <app-icon name="circlecheck" [size]="14" />
        </button>
        <button class="lm-tool-btn lm-icon-btn" (click)="onCancelRenameSet()" title="Cancel">
          <app-icon name="x" [size]="14" />
        </button>
      }
      @if (!showNewSetInput() && !showRenameSetInput()) {
        <button class="lm-tool-btn"
                title="Rename this label set"
                [disabled]="activeSetIsBuiltIn()"
                (click)="onRenameSet()">
          <app-icon name="edit" [size]="12" /> Rename
        </button>
      }
      <button class="lm-tool-btn lm-danger-btn"
              title="Delete this label set"
              [disabled]="activeSetIsBuiltIn()"
              (click)="onDeleteSet()">
        <app-icon name="trash2" [size]="12" /> Delete Set
      </button>

      @if (!activeSetIsBuiltIn()) {
        <button class="lm-tool-btn lm-add-btn" title="Add a new label entry" (click)="onAddLabel()">
          <app-icon name="plus" [size]="12" /> Add Label
        </button>
      }

      <span class="lm-spacer"></span>

      <!-- Import / Export -->
      <button class="lm-tool-btn lm-icon-btn" title="Import labels from a JSON file" (click)="triggerImport()">
        <app-icon name="upload" [size]="13" />
      </button>
      <button class="lm-tool-btn lm-icon-btn" title="Export this set as JSON" [disabled]="activeSetIsBuiltIn()" (click)="onExport()">
        <app-icon name="download" [size]="13" />
      </button>
      <input #fileInput type="file" accept=".json" style="display:none" (change)="onFileSelected($event)" />

      <button class="lm-tool-btn lm-icon-btn" title="Refresh" (click)="action$.emit({ type: 'refresh', activeSetName: activeSetName() })">
        <app-icon name="refreshcw" [size]="13" />
      </button>

      <!-- Live polling toggle -->
      <label class="lm-live-toggle" title="Enable continuous live-value polling (500ms). When off, values are fetched once on open.">
        <input type="checkbox"
               [checked]="livePollingEnabled()"
               (change)="setLivePolling(asInput($event).checked)" />
        Live
      </label>
    </div>

    <!-- Scanner row (own row so buttons don't cramp on narrow widths) -->
    <div id="lm-scanner-row">
      <span class="lm-scanner-label">Scan memory for</span>
      <div class="lm-scanner-grid">
        <button class="lm-scan-btn" [class.lm-scan-active]="activeScannerKind() === 'functions'"
                [disabled]="scannerStatus() === 'scanning'"
                title="Scan memory for function entry points"
                (click)="onRunScanner('functions')">
          <app-icon name="workflow" [size]="13" />
          <span>Functions</span>
        </button>
        <button class="lm-scan-btn" [class.lm-scan-active]="activeScannerKind() === 'pointers'"
                [disabled]="scannerStatus() === 'scanning'"
                title="Scan memory for pointer values"
                (click)="onRunScanner('pointers')">
          <app-icon name="link" [size]="13" />
          <span>Pointers</span>
        </button>
        <button class="lm-scan-btn" [class.lm-scan-active]="activeScannerKind() === 'strings'"
                [disabled]="scannerStatus() === 'scanning'"
                title="Scan memory for null-terminated strings"
                (click)="onRunScanner('strings')">
          <app-icon name="bookopen" [size]="13" />
          <span>Strings</span>
        </button>
        <button class="lm-scan-btn" [class.lm-scan-active]="activeScannerKind() === 'floats'"
                [disabled]="scannerStatus() === 'scanning'"
                title="Scan memory for IEEE-754 float values"
                (click)="onRunScanner('floats')">
          <app-icon name="sparkles" [size]="13" />
          <span>Floats</span>
        </button>
        <button class="lm-scan-btn" [class.lm-scan-active]="activeScannerKind() === 'hexValues'"
                [disabled]="scannerStatus() === 'scanning'"
                title="Scan memory for non-zero hex values (integers)"
                (click)="onRunScanner('hexValues')">
          <app-icon name="database" [size]="13" />
          <span>Hex</span>
        </button>
      </div>
      @if (scanResultEntries().length > 0) {
        <button class="lm-tool-btn lm-danger-btn" title="Clear scan results" (click)="onClearScanResults()">
          <app-icon name="x" [size]="12" /> Clear
        </button>
      }
    </div>

    <!-- Filter / type-filter row -->
    <div id="lm-filter-row">
      <input id="lm-filter"
             type="text"
             placeholder="Filter by address or name…"
             spellcheck="false"
             [value]="filterText()"
             (input)="onFilterTextChange(asInput($event).value)" />
      @if (filterText()) {
        <button class="lm-clear-filter" title="Clear filter" (click)="onFilterTextChange('')">
          <app-icon name="x" [size]="13" />
        </button>
      }
      <select id="lm-type-filter" [value]="typeFilter()" (change)="onTypeFilterChange(asSelect($event).value)">
        <option value="">All types</option>
        @for (t of unifiedTypes; track t.value) {
          <option [value]="t.value">{{ t.label }}</option>
        }
      </select>
      <span id="lm-count">{{ filteredEntries().length }} / {{ totalEntryCount() }}</span>
    </div>

    <!-- Pagination controls -->
    @if (totalPages() > 1 || totalRootEntries() > pageSizeOptions[0]) {
      <div class="lm-pagination">
        <button class="lm-page-btn lm-icon-btn" [disabled]="currentPage() === 0" title="First page" (click)="goToPage(0)">
          <app-icon name="chevronleft" [size]="12" /><app-icon name="chevronleft" [size]="12" />
        </button>
        <button class="lm-page-btn lm-icon-btn" [disabled]="currentPage() === 0" title="Previous page" (click)="goToPage(currentPage() - 1)">
          <app-icon name="chevronleft" [size]="12" />
        </button>
        <span class="lm-page-info">
          Page {{ currentPage() + 1 }} of {{ totalPages() }}
          <span class="lm-muted">({{ totalRootEntries() }} entries)</span>
        </span>
        <button class="lm-page-btn lm-icon-btn" [disabled]="currentPage() >= totalPages() - 1" title="Next page" (click)="goToPage(currentPage() + 1)">
          <app-icon name="chevronright" [size]="12" />
        </button>
        <button class="lm-page-btn lm-icon-btn" [disabled]="currentPage() >= totalPages() - 1" title="Last page" (click)="goToPage(totalPages() - 1)">
          <app-icon name="chevronright" [size]="12" /><app-icon name="chevronright" [size]="12" />
        </button>
        <select class="lm-page-size-select" [value]="pageSize()" (change)="onPageSizeChange(asSelect($event).value)" title="Entries per page">
          @for (opt of pageSizeOptions; track opt) {
            <option [value]="opt" [selected]="pageSize() === opt">{{ opt }} / page</option>
          }
        </select>
      </div>
    }

    <!-- Scanning progress indicator -->
    @if (scannerStatus() === 'scanning') {
      <div class="lm-scanning-bar">
        <div class="lm-scanning-bar-inner"></div>
        <span class="lm-scanning-text">Scanning {{ activeScannerKind() }}… this may take a moment</span>
      </div>
    }

    <!-- Table -->
    <div id="lm-table-wrap">
      <table id="lm-table">
        <thead>
          <tr>
            <th class="lm-col-vis" title="Toggle all visibility">
              @if (!isScannerView()) {
              <button class="lm-vis-btn lm-vis-all-btn"
                      [class.lm-is-hidden]="!allVisible()"
                      [disabled]="activeSetIsBuiltIn() || filteredEntries().length === 0"
                      [title]="allVisible() ? 'Hide all' : 'Show all'"
                      (click)="onToggleAllVisibility()">
                <app-icon [name]="allVisible() ? 'eye' : 'ban'" [size]="14" />
              </button>
              }
            </th>
            <th class="lm-col-expand"></th>
            <th class="lm-col-type">
              <button type="button" class="lm-sort-btn" [class.lm-sort-active]="sortColumn() === 'type'" (click)="onSort('type')">
                <span>Type</span><span class="lm-sort-indicator">{{ sortIndicator('type') }}</span>
              </button>
            </th>
            <th class="lm-col-addr">
              <button type="button" class="lm-sort-btn" [class.lm-sort-active]="sortColumn() === 'address'" (click)="onSort('address')">
                <span>Address</span><span class="lm-sort-indicator">{{ sortIndicator('address') }}</span>
              </button>
            </th>
            <th class="lm-col-live">
              <button type="button" class="lm-sort-btn" [class.lm-sort-active]="sortColumn() === 'live'" (click)="onSort('live')">
                <span>Live Value</span><span class="lm-sort-indicator">{{ sortIndicator('live') }}</span>
              </button>
            </th>
            <th class="lm-col-name">
              <button type="button" class="lm-sort-btn" [class.lm-sort-active]="sortColumn() === 'name'" (click)="onSort('name')">
                <span>Name</span><span class="lm-sort-indicator">{{ sortIndicator('name') }}</span>
              </button>
            </th>
            <th class="lm-col-comment">
              <button type="button" class="lm-sort-btn" [class.lm-sort-active]="sortColumn() === 'comment'" (click)="onSort('comment')">
                <span>Comment</span><span class="lm-sort-indicator">{{ sortIndicator('comment') }}</span>
              </button>
            </th>
            <th class="lm-col-actions"></th>
          </tr>
        </thead>
        <tbody>
          @for (row of treeRows(); track row.path) {
            @if (row.kind === 'entry') {
              <!-- Top-level label entry row -->
              <tr [class.lm-struct-row]="effectiveType(row.entry!) === 'Pointer' || effectiveType(row.entry!) === 'Struct'"
                  [class.lm-fnc-row]="effectiveType(row.entry!) === 'Function'"
                  [class.lm-hidden-row]="row.entry!.hidden">
                <td class="lm-col-vis">
                  @if (!isScannerView()) {
                  <button class="lm-vis-btn"
                          [class.lm-is-hidden]="row.entry!.hidden"
                          [title]="row.entry!.hidden ? 'Hidden — click to show' : 'Visible — click to hide'"
                          [disabled]="activeSetIsBuiltIn()"
                          (click)="onToggleVisibility(row.entry!)">
                    <app-icon [name]="row.entry!.hidden ? 'ban' : 'eye'" [size]="13" />
                  </button>
                  }
                </td>
                <td class="lm-col-expand">
                  @if (row.expandable) {
                    <button class="lm-expand-btn" [title]="isExpanded(row.path) ? 'Collapse' : 'Expand members'" (click)="toggleExpand(row.path)">
                      <app-icon [name]="isExpanded(row.path) ? 'chevrondown' : 'chevronright'" [size]="12" />
                    </button>
                  }
                </td>
                <td class="lm-col-type">
                  <span class="lm-type-badge" [class]="typeBadgeClass(row.entry!)">{{ typeBadge(row.entry!) }}</span>
                </td>
                <td class="lm-col-addr lm-mono">
                  <button class="lm-row-btn lm-goto-btn" title="Go to address" (click)="onGotoLabel(row.entry!)">
                    <app-icon name="arrowright" [size]="12" />
                  </button>
                  {{ row.entry!.address }}@if (entryDetails(row.entry!) !== '—') { <span class="lm-addr-details lm-muted">({{ entryDetails(row.entry!) }})</span> }
                </td>
                <td class="lm-col-live">
                  @if (shouldShowLive(row.entry!)) {
                    @if (effectiveType(row.entry!) === 'Function') {
                      <span class="lm-live-fnc">{{ fncLiveDisplay(row.entry!) }}</span>
                    } @else if (effectiveType(row.entry!) === 'String') {
                      <span class="lm-live-string">{{ liveValue(row.entry!) }}</span>
                    } @else {
                      <span [class]="liveValueClass(row.entry!)">{{ liveValue(row.entry!) }}</span>
                    }
                  }
                </td>
                <td class="lm-col-name">{{ displayName(row.entry!) }}</td>
                <td class="lm-col-comment lm-muted">{{ entryComment(row.entry!) }}</td>
                <td class="lm-col-actions">
                  <button class="lm-row-btn lm-live-toggle-btn"
                          [class.lm-is-dynamic]="row.entry!.dynamic"
                          [title]="row.entry!.dynamic ? 'Pause live polling' : 'Start live polling'"
                          (click)="onToggleDynamic(row.entry!)">
                    <app-icon [name]="row.entry!.dynamic ? 'pause' : 'play'" [size]="11" />
                  </button>
                  @if (!activeSetIsBuiltIn()) {
                    <button class="lm-row-btn" title="Edit" (click)="onEdit(row.entry!)">
                      <app-icon name="edit" [size]="12" />
                    </button>
                    <button class="lm-row-btn lm-danger-btn" title="Delete" (click)="onDeleteEntry(row.entry!)">
                      <app-icon name="trash2" [size]="12" />
                    </button>
                  }
                </td>
              </tr>
            } @else {
              <!-- Child label sub-row (recursive; entry = the child LabelManagerEntry) -->
              <tr class="lm-member-sub-row" [class.lm-hidden-row]="row.entry.hidden">
                <td class="lm-col-vis">
                  @if (!isScannerView()) {
                  <button class="lm-vis-btn"
                          [class.lm-is-hidden]="row.entry.hidden"
                          [title]="row.entry.hidden ? 'Hidden — click to show' : 'Visible — click to hide'"
                          [disabled]="activeSetIsBuiltIn()"
                          (click)="onToggleChildVisibility(row.entry)">
                    <app-icon [name]="row.entry.hidden ? 'ban' : 'eye'" [size]="13" />
                  </button>
                  }
                </td>
                <td class="lm-col-expand lm-member-tree-indent">
                  @for (hasLine of row.ancestorLines; track $index) {
                    <span class="lm-tree-vert">{{ hasLine ? '│' : '\xa0' }}</span>
                  }
                  @if (row.expandable) {
                    <span class="lm-tree-connector">{{ row.isLastChild ? '└' : '├' }}</span>
                    <button class="lm-expand-btn" [title]="isExpanded(row.path) ? 'Collapse' : 'Expand'" (click)="toggleExpand(row.path)">
                      <app-icon [name]="isExpanded(row.path) ? 'chevrondown' : 'chevronright'" [size]="12" />
                    </button>
                  } @else {
                    <span class="lm-tree-leaf">{{ row.isLastChild ? '└─' : '├─' }}</span>
                  }
                </td>
                <!-- Type column: badge based on child type -->
                <td class="lm-col-type">
                  <span class="lm-type-badge" [class]="typeBadgeClass(row.entry)">{{ typeBadge(row.entry) }}</span>
                </td>
                <!-- Address column: byte offset from parent base -->
                <td class="lm-col-addr lm-mono lm-child-offset">
                  +0x{{ (row.entry.offset ?? 0).toString(16).toUpperCase().padStart(4, '0') }}
                </td>
                <!-- Live value column: formatted by child type, only when dynamic -->
                <td class="lm-col-live">
                  @if (row.entry.dynamic) {
                    @if (childEffectiveType(row.entry) === 'String') {
                      <span class="lm-live-string">{{ memberLiveValues().get(row.path) ?? '…' }}</span>
                    } @else if (memberLiveValues().has(row.path)) {
                      <span [class]="childLiveClass(row.entry)">{{ memberLiveValues().get(row.path) }}</span>
                    } @else {
                      <span class="lm-live-placeholder">…</span>
                    }
                  }
                </td>
                <td class="lm-col-name lm-member-sub-name">{{ row.entry.name }}</td>
                <td class="lm-col-comment lm-muted">{{ row.entry.comment ?? '' }}</td>
                <td class="lm-col-actions">
                  <button class="lm-row-btn lm-live-toggle-btn"
                          [class.lm-is-dynamic]="row.entry.dynamic"
                          [title]="row.entry.dynamic ? 'Pause live polling' : 'Start live polling'"
                          (click)="onToggleChildDynamic(row.entry)">
                    <app-icon [name]="row.entry.dynamic ? 'pause' : 'play'" [size]="11" />
                  </button>
                  @if (!activeSetIsBuiltIn()) {
                    @if (row.entry.parentAddress != null) {
                      <button class="lm-row-btn lm-goto-btn" title="Navigate to resolved address" (click)="onGotoChild(row.entry)">
                        <app-icon name="arrowright" [size]="12" />
                      </button>
                    }
                    <button class="lm-row-btn" title="Edit" aria-label="Edit sub-label" (click)="onEdit(row.entry)">
                      <app-icon name="edit" [size]="12" />
                    </button>
                    <button class="lm-row-btn lm-danger-btn" title="Delete" (click)="onDeleteEntry(row.entry)">
                      <app-icon name="trash2" [size]="12" />
                    </button>
                  } @else {
                    @if (row.entry.parentAddress != null) {
                      <button class="lm-row-btn lm-goto-btn" title="Navigate to resolved address" (click)="onGotoChild(row.entry)">
                        <app-icon name="arrowright" [size]="12" />
                      </button>
                    }
                  }
                </td>
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
      <div id="lm-edit-form-section" class="lm-acc-section">
        <button type="button" class="lm-acc-header" [class.lm-acc-open]="editFormOpen()" (click)="toggleEditForm()">
          <app-icon class="lm-acc-chevron" [name]="editFormOpen() ? 'chevrondown' : 'chevronright'" [size]="13" />
          <span class="lm-acc-title">{{ editingAddress() ? 'Edit Label' : 'Label Editor' }}</span>
          @if (editingAddress()) {
            <span class="lm-acc-subtitle lm-mono">{{ editingAddress() }}</span>
          } @else {
            <span class="lm-acc-subtitle lm-muted">click to add a new label</span>
          }
        </button>
        @if (editFormOpen()) {
        <div id="lm-edit-form" class="lm-acc-body">
        <!-- Type selector -->
        <div class="lm-form-row">
          <label class="lm-form-label">Type</label>
          <select class="lm-form-input" (change)="editType.set(asSelect($event).value)">
            @for (t of unifiedTypes; track t.value) {
              <option [value]="t.value" [selected]="editType() === t.value">{{ t.label }}</option>
            }
          </select>
        </div>
        @if (editParentAddress()) {
          <!-- Editing a child entry: show parent context + editable offset -->
          <div class="lm-form-row">
            <label class="lm-form-label">Parent</label>
            <span class="lm-form-input lm-mono lm-readonly-addr">{{ editParentAddress() }}</span>
          </div>
          <div class="lm-form-row">
            <label class="lm-form-label">Offset</label>
            <input class="lm-form-input lm-mono" type="text" spellcheck="false"
                   placeholder="0x0000"
                   [value]="editOffsetStr()"
                   (input)="editOffsetStr.set(asInput($event).value)" />
          </div>
        } @else {
          <div class="lm-form-row">
            <label class="lm-form-label">Address</label>
            <input class="lm-form-input lm-mono" type="text" spellcheck="false"
                   placeholder="0x80010000"
                   [value]="editAddress()"
                   (input)="editAddress.set(asInput($event).value)" />
          </div>
        }
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

        <!-- Child entries editor (for Pointer type — children hang off dereferenced base) -->
        @if (editType() === 'Pointer') {
          <div class="lm-form-row lm-members-row">
            <label class="lm-form-label">Children</label>
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
                  <select class="lm-member-type"
                          (change)="updateMemberType($index, asSelect($event).value)">
                    <option value="" [selected]="!m.type || m.type === ''">type…</option>
                    @for (t of unifiedTypes; track t.value) {
                      <option [value]="t.value" [selected]="m.type === t.value">{{ t.label }}</option>
                    }
                  </select>
                  <input class="lm-member-comment" type="text" spellcheck="false"
                         placeholder="comment…"
                         [value]="m.comment ?? ''"
                         (input)="updateMemberComment($index, asInput($event).value)" />
                  <button class="lm-member-del" title="Remove child" (click)="removeMember($index)">
                    <app-icon name="x" [size]="12" />
                  </button>
                </div>
              }
              <button class="lm-tool-btn" (click)="addMember()">
                <app-icon name="plus" [size]="12" /> Child
              </button>
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

        <!-- Pointer: informational note -->
        @if (editType() === 'Pointer') {
          <div class="lm-form-row">
            <span class="lm-muted lm-form-note">
              The address holds a 32-bit pointer. Live view shows the dereferenced value.
              Add children above — their offsets are relative to that dereferenced base address.
            </span>
          </div>
        }

        <!-- Float: informational note -->
        @if (editType() === 'Float') {
          <div class="lm-form-row">
            <span class="lm-muted lm-form-note">
              The address holds a 32-bit IEEE-754 single-precision float. Live view shows the decoded value.
            </span>
          </div>
        }

        <!-- Value: informational note -->
        @if (editType() === 'Value') {
          <div class="lm-form-row">
            <span class="lm-muted lm-form-note">
              The address holds an integer value. Live view shows the raw number.
            </span>
          </div>
        }

        <!-- String: informational note -->
        @if (editType() === 'String') {
          <div class="lm-form-row">
            <span class="lm-muted lm-form-note">
              The address points to (or is) a null-terminated ASCII string. Live view reads up to 256 bytes;
              if the first byte is not printable it tries to dereference the address as a pointer first.
            </span>
          </div>
        }

        <div class="lm-form-row lm-form-actions">
          <button class="lm-tool-btn lm-add-btn" [disabled]="!canSave()" (click)="onSave()">
            <app-icon name="save" [size]="12" />
            {{ editingAddress() ? 'Update' : 'Add Label' }}
          </button>
          @if (editingAddress()) {
            <button class="lm-tool-btn" (click)="clearForm()">
              <app-icon name="x" [size]="12" /> Cancel
            </button>
          }
        </div>
        </div>
        }
      </div>
    }

    <!-- Status bar -->
    @if (statusMsg()) {
      <div id="lm-status" [class.lm-status-error]="statusIsError()">{{ statusMsg() }}</div>
    }

    <!-- Inline delete-entry confirmation (replaces window.confirm which is blocked in webviews) -->
    @if (pendingDeleteEntry()) {
      <div class="lm-confirm-backdrop" (click)="cancelDeleteEntry()">
        <div class="lm-confirm-dialog" (click)="$event.stopPropagation()">
          <p class="lm-confirm-title">
            <app-icon name="trash2" [size]="14" /> Delete label?
          </p>
          <p class="lm-confirm-body">
            <strong>{{ pendingDeleteEntry()!.name }}</strong>
            @if (pendingDeleteEntry()!.parentAddress) {
              &nbsp;at offset {{ pendingDeleteEntry()!.address.split(':').slice(1).join(':') }}
            } @else {
              &nbsp;at {{ pendingDeleteEntry()!.address }}
            }
          </p>
          <p class="lm-confirm-note lm-muted">This action cannot be undone.</p>
          <div class="lm-confirm-actions">
            <button class="lm-tool-btn lm-danger-btn" (click)="confirmDeleteEntry()">Delete</button>
            <button class="lm-tool-btn" (click)="cancelDeleteEntry()">Cancel</button>
          </div>
        </div>
      </div>
    }

    <!-- Inline delete-set confirmation -->
    @if (pendingDeleteSet()) {
      <div class="lm-confirm-backdrop" (click)="cancelDeleteSet()">
        <div class="lm-confirm-dialog" (click)="$event.stopPropagation()">
          <p class="lm-confirm-title">
            <app-icon name="trash2" [size]="14" /> Delete label set?
          </p>
          <p class="lm-confirm-body">Set: <strong>{{ activeSetName() }}</strong></p>
          <p class="lm-confirm-note lm-muted">All labels in this set will be permanently deleted.</p>
          <div class="lm-confirm-actions">
            <button class="lm-tool-btn lm-danger-btn" (click)="confirmDeleteSet()">Delete Set</button>
            <button class="lm-tool-btn" (click)="cancelDeleteSet()">Cancel</button>
          </div>
        </div>
      </div>
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
  readonly sortColumn    = signal<LabelSortColumn | null>(null);
  readonly sortDirection = signal<LabelSortDirection>('asc');

  // Inline new-set creation (replaces window.prompt)
  readonly showNewSetInput = signal(false);
  readonly newSetNameInput = signal('');

  // Inline rename-set
  readonly showRenameSetInput = signal(false);
  readonly renameSetInput     = signal('');

  // Edit form state
  readonly editAddress       = signal('');
  readonly editName          = signal('');
  readonly editComment       = signal('');
  readonly editType          = signal<string>('Address');
  readonly editHidden        = signal(false);
  readonly editDynamic       = signal(false);
  readonly editMembers       = signal<{ offsetStr: string; name: string; type?: string; comment?: string; dynamic?: boolean; hidden?: boolean }[]>([]);
  readonly editArgRegs       = signal('');
  readonly editReturnRegs    = signal('');
  readonly editElementStride = signal('');
  readonly editElementCount  = signal('');
  readonly editNodeNextOffset = signal('');
  readonly editNodeSize       = signal('');
  /** When editing an existing entry, this holds its original address. */
  readonly editingAddress = signal('');
  /**
   * Collapses the bottom edit form so the table has more vertical room.
   * Auto-opens when the user clicks "Add Label" or the per-row Edit button;
   * the user can also toggle it via the section header chevron.
   */
  readonly editFormOpen = signal(false);
  /** When editing a child entry, this preserves its parent address so the relationship is not lost on save. */
  readonly editParentAddress = signal<string | undefined>(undefined);
  /** When editing a child entry, this preserves its byte offset from the parent pointer. */
  readonly editParentOffset  = signal<number | undefined>(undefined);
  /** When editing a child entry, the user-editable offset string (hex). */
  readonly editOffsetStr     = signal('');

  // Inline confirmation state (replaces window.confirm which doesn't work in VS Code webviews)
  readonly pendingDeleteEntry = signal<LabelManagerEntry | null>(null);
  readonly pendingDeleteSet   = signal(false);

  /** Addresses of original children tracked when the edit form is opened, for deletion on save. */
  private _editingOriginalChildAddresses: string[] = [];

  /** Map of address → live hex value string for dynamic entries. */
  readonly liveValues = signal<Map<string, string>>(new Map());

  /** Map of tree-row path → live value string for dynamic member sub-rows. */
  readonly memberLiveValues = signal<Map<string, string>>(new Map());

  /** Polling timer handle (setInterval id). */
  private _pollTimer: ReturnType<typeof setInterval> | null = null;

  /**
   * Whether continuous live-value polling is enabled.  Defaults to false —
   * the label manager fetches values once on open and then stops.  The user
   * can toggle this to true via a checkbox to enable continuous 500ms polling.
   */
  readonly livePollingEnabled = signal(false);

  // ── Scanner state ──────────────────────────────────────────────────────────
  /** Which scanner is currently active/running. */
  readonly activeScannerKind = signal<ScannerKind | null>(null);
  /** 'idle', 'scanning', or 'done'. */
  readonly scannerStatus = signal<'idle' | 'scanning' | 'done'>('idle');
  /** Scanner results converted to LabelManagerEntry items. */
  readonly scanResultEntries = signal<LabelManagerEntry[]>([]);

  // ── Pagination state ─────────────────────────────────────────────────────────
  /** Current page index (0-based). */
  readonly currentPage = signal(0);
  /** Number of root entries displayed per page. */
  readonly pageSize = signal(100);
  /** Available page-size options. */
  readonly pageSizeOptions = [50, 100, 250, 500];

  /**
   * Normalised address (no "0x" prefix, uppercase) that should be pre-filled
   * into the form the next time `setActiveSet` is called after a refresh.
   * Set by `openWithAddress`; cleared immediately once applied.
   */
  private _pendingPrefillAddress: string | null = null;

  // ── Computed ──────────────────────────────────────────────────────────────────
  /** Expose the unified type list to the template. */
  readonly unifiedTypes = UNIFIED_TYPES;

  readonly activeSetIsBuiltIn = computed(() =>
    this.activeSetName() === 'Built-in' || (this.currentSet()?.isBuiltIn ?? false)
  );

  /** True when scanner results are being displayed instead of a label set. */
  readonly isScannerView = computed(() => this.scanResultEntries().length > 0);

  readonly filteredEntries = computed(() => {
    const scanEntries = this.scanResultEntries();
    // When scanner results are present, show only scan results (not the label set).
    const entries = scanEntries.length > 0
      ? scanEntries
      : (this.currentSet()?.entries ?? []);
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

  readonly sortedRootEntries = computed(() => {
    const roots = this.filteredEntries().filter(entry => !entry.parentAddress);
    const sortColumn = this.sortColumn();
    if (!sortColumn) {
      return roots;
    }

    const direction = this.sortDirection() === 'asc' ? 1 : -1;
    return [...roots].sort((a, b) => {
      const compare = this.compareEntries(a, b, sortColumn);
      if (compare !== 0) {
        return compare * direction;
      }
      return this.compareEntries(a, b, 'address');
    });
  });

  readonly canSave = computed(() => {
    const hasName = this.editName().trim() !== '';
    if (this.editParentAddress()) {
      return hasName && this.editOffsetStr().trim() !== '';
    }
    return hasName && this.editAddress().trim() !== '';
  });

  /** Total entry count: scan results when scanning, otherwise label set entries. */
  readonly totalEntryCount = computed(() => {
    const scan = this.scanResultEntries();
    return scan.length > 0 ? scan.length : (this.currentSet()?.entries?.length ?? 0);
  });

  /** True when all filtered visible entries have hidden=false (or undefined). */
  readonly allVisible = computed(() => {
    const entries = this.filteredEntries();
    return entries.length > 0 && entries.every(e => !e.hidden);
  });

  /** True when at least one filtered entry has dynamic=true. */
  readonly hasDynamicEntries = computed(() =>
    this.filteredEntries().some(e => e.dynamic),
  );

  /** Root entries to poll: entries with dynamic===true from the active set OR scanner results. */
  private readonly _dynamicAddresses = computed(() => {
    const scanEntries = this.scanResultEntries();
    const source = scanEntries.length > 0
      ? scanEntries
      : (this.currentSet()?.entries ?? []);
    return source
      .filter(e => !e.parentAddress && e.dynamic)
      .map(e => ({ address: e.address, entryType: e.type ?? 'Address' as LabelType, valueType: e.valueType }));
  });

  /**
   * Visible child rows to poll: only expanded member rows with dynamic===true.
   * The `path` (synthetic address) encodes the full resolution chain.
   */
  private readonly _dynamicMemberRows = computed(() =>
    this.treeRows().filter(
      r => r.kind === 'member' && r.entry.parentAddress != null && r.entry.dynamic,
    ),
  );

  /** Colspan for the "no entries" empty row. Actions column is always present. */
  readonly emptyColspan = computed(() => 7);

  /**
   * A name → entry map for Struct-type labels only, recomputed only when the
   * current set's entries change (not on filter changes).
   */
  private readonly _structByName = computed<Map<string, LabelManagerEntry>>(() => {
    const allEntries = this.currentSet()?.entries ?? [];
    const map = new Map<string, LabelManagerEntry>();
    for (const e of allEntries) {
      const t = e.type ?? 'Address';
      if (t === 'Struct' || t === 'Pointer') {
        map.set(e.name, e);
      }
    }
    return map;
  });

  /**
   * Look up children of the given parent address from the current set's flat entry list.
   */
  private _childrenOf(parentAddress: string): LabelManagerEntry[] {
    const all = this.currentSet()?.entries ?? [];
    return all.filter(e => e.parentAddress === parentAddress && !e.hidden);
  }

  /**
   * A flat list of tree rows derived from `filteredEntries()`.
   * Root entries (no parentAddress) are at depth 0.  When expanded, their
   * child entries appear immediately below.  Children can themselves be expanded
   * if they have children — fully recursive with cycle detection.
   */
  readonly treeRows = computed<TreeRow[]>(() => {
    const roots = this.sortedRootEntries();
    const size = this.pageSize();
    const maxPage = Math.max(0, Math.ceil(roots.length / size) - 1);
    const page = Math.min(this.currentPage(), maxPage);
    const start = page * size;
    const end = start + size;
    const pagedRoots = roots.slice(start, end);

    const rows: TreeRow[] = [];
    for (const entry of pagedRoots) {
      const path = entry.address;
      const children = this._childrenOf(path);
      rows.push({
        kind: 'entry',
        depth: 0,
        path,
        entry,
        expandable: children.length > 0,
        isLastChild: true,
        ancestorLines: [],
      });
      if (this.expandedPaths().has(path)) {
        this._addChildRows(rows, children, 1, new Set([path]), []);
      }
    }
    return rows;
  });

  /** Total number of root entries (before paging). */
  readonly totalRootEntries = computed(() => this.sortedRootEntries().length);

  /** Total number of pages. */
  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalRootEntries() / this.pageSize()))
  );


  // ── Public API ────────────────────────────────────────────────────────────────

  open(): void {
    this.visible.set(true);
    this.action$.emit({ type: 'refresh' });
    // Fetch values once; continuous polling only if user enables it.
    this._emitOnePoll();
    if (this.livePollingEnabled()) {
      this._updatePolling();
    }
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
    // Fetch values once; continuous polling only if user enables it.
    this._emitOnePoll();
    if (this.livePollingEnabled()) {
      this._updatePolling();
    }
  }

  close(): void {
    if (this.visible()) {
      this.visible.set(false);
      this._stopPolling();
      this.close$.emit();
    }
  }

  setSets(names: string[]): void {
    // Ensure "Built-in" is always first and deduplicate set names.
    const rest = [...new Set(names.filter(n => n !== 'Built-in'))];
    this.setNames.set(['Built-in', ...rest]);
  }

  setActiveSet(set: LabelSetPayload): void {
    this.activeSetName.set(set.setName);
    // Ensure entries is always an array to avoid downstream null-reference errors.
    const safeSet: LabelSetPayload = set.entries ? set : { ...set, entries: [] };
    this.currentSet.set(safeSet);

    const pending = this._pendingPrefillAddress;

    if (pending) {
      // A context-menu "Add Label" was triggered — apply the prefill.
      this.clearForm();
      this.editAddress.set('0x' + pending);
      const existing = safeSet.entries.find(
        e => e.address.toUpperCase().replace(/^0x/i, '') === pending,
      );
      if (existing) {
        this.onEdit(existing);
      }
      // If the active set is read-only, keep the pending address so it is
      // reapplied automatically when the user switches to (or creates) a user set.
      if (safeSet.isBuiltIn) {
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

    // Now that entries are loaded, fetch live values for any dynamic entries.
    this._emitOnePoll();
    if (this.livePollingEnabled()) {
      this._updatePolling();
    }
  }

  showStatus(msg: string, isError = false): void {
    this.statusMsg.set(msg);
    this.statusIsError.set(isError);
    if (!isError) {
      setTimeout(() => { if (this.statusMsg() === msg) { this.statusMsg.set(''); } }, 3000);
    }
  }

  // ── Scanner public API ───────────────────────────────────────────────────────

  /** Called by the toolbar scan buttons. */
  onRunScanner(kind: ScannerKind): void {
    // Stop any active polling to avoid socket FIFO conflicts during the scan.
    this._stopPolling();
    // Collapse the bottom editor so the scan-result table gets the full
    // vertical room — otherwise a previously opened edit panel can squeeze
    // the table down to its min-height.
    this.editFormOpen.set(false);
    this.activeScannerKind.set(kind);
    this.scannerStatus.set('scanning');
    this.scanResultEntries.set([]);
    this.currentPage.set(0);
    this.action$.emit({ type: 'runScanner', scannerKind: kind });
  }

  /** Clear scanner results from the view. */
  onClearScanResults(): void {
    this.activeScannerKind.set(null);
    this.scannerStatus.set('idle');
    this.scanResultEntries.set([]);
    this.currentPage.set(0);
    this.action$.emit({ type: 'clearScanResults' });
  }

  /**
   * Called by the host when scanner results arrive from the provider.
   * Converts raw scanner entries into LabelManagerEntry format for the tree.
   */
  setScanResults(kind: ScannerKind, entries: LabelManagerEntry[]): void {
    console.log('[LabelManager] setScanResults', kind, 'received', entries.length, 'entries; sample:', entries[0]);
    this.activeScannerKind.set(kind);
    this.scannerStatus.set('done');
    // Keep the editor collapsed so the result table always has room.
    this.editFormOpen.set(false);
    this.scanResultEntries.set(entries);
    this.currentPage.set(0);
    // Move off any active type filter so scan entries are not filtered out
    // by a leftover type selection from a prior interaction.
    this.typeFilter.set('');
    this.filterText.set('');
    const count = entries.length;
    this.showStatus(`Scan complete: ${count} ${kind} found.`);
    // Restart polling if the user had live polling enabled before the scan.
    if (this.livePollingEnabled()) {
      this._updatePolling();
    }
    // Defer one frame so OnPush can see the signal updates and re-run the
    // table computed; logs the resolved filteredEntries length to confirm.
    queueMicrotask(() => {
      console.log('[LabelManager] post-scan filteredEntries=', this.filteredEntries().length,
        'treeRows=', this.treeRows().length,
        'activeSet=', this.activeSetName(),
        'isBuiltIn=', this.activeSetIsBuiltIn());
    });
  }

  // ── Pagination ──────────────────────────────────────────────────────────────

  /** Navigate to the given page (clamped to valid range). */
  goToPage(page: number): void {
    const clamped = Math.max(0, Math.min(page, this.totalPages() - 1));
    this.currentPage.set(clamped);
  }

  /** Handle page-size dropdown change. */
  onPageSizeChange(value: string): void {
    const size = parseInt(value, 10);
    if (!Number.isNaN(size) && size > 0) {
      this.pageSize.set(size);
      this.currentPage.set(0);
    }
  }

  // ── Event Handlers ────────────────────────────────────────────────────────────

  /** Update filter text and reset to first page. */
  onFilterTextChange(value: string): void {
    this.filterText.set(value);
    this.currentPage.set(0);
  }

  /** Update type filter and reset to first page. */
  onTypeFilterChange(value: string): void {
    this.typeFilter.set(value);
    this.currentPage.set(0);
  }

  onSetChange(name: string): void {
    this.currentPage.set(0);
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

  onRenameSet(): void {
    this.renameSetInput.set(this.activeSetName());
    this.showRenameSetInput.set(true);
    requestAnimationFrame(() => {
      const input = document.querySelector('.lm-rename-set-input') as HTMLInputElement | null;
      input?.focus();
      input?.select();
    });
  }

  onConfirmRenameSet(): void {
    const newName = this.renameSetInput().trim();
    if (!newName) { return; }
    const oldName = this.activeSetName();
    this.showRenameSetInput.set(false);
    this.renameSetInput.set('');
    this.action$.emit({ type: 'renameSet', setName: oldName, newSetName: newName });
  }

  onCancelRenameSet(): void {
    this.showRenameSetInput.set(false);
    this.renameSetInput.set('');
  }

  onDeleteSet(): void {
    this.pendingDeleteSet.set(true);
  }

  confirmDeleteSet(): void {
    this.pendingDeleteSet.set(false);
    this.action$.emit({ type: 'deleteSet', setName: this.activeSetName() });
  }

  cancelDeleteSet(): void {
    this.pendingDeleteSet.set(false);
  }

  onAddLabel(): void {
    this.clearForm();
    this.editFormOpen.set(true);
    requestAnimationFrame(() => {
      const form = document.getElementById('lm-edit-form');
      form?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
      const firstInput = form?.querySelector('input') as HTMLInputElement | null;
      firstInput?.focus();
    });
  }

  /** Toggle the bottom edit form open/closed (called by the section header). */
  toggleEditForm(): void {
    this.editFormOpen.update(v => !v);
  }

  onToggleVisibility(entry: LabelManagerEntry): void {
    const updated: LabelManagerEntry = { ...entry, hidden: entry.hidden ? undefined : true };
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

  onSort(column: LabelSortColumn): void {
    if (this.sortColumn() === column) {
      this.sortDirection.update(direction => direction === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortColumn.set(column);
      this.sortDirection.set('asc');
    }
    this.currentPage.set(0);
  }

  sortIndicator(column: LabelSortColumn): string {
    if (this.sortColumn() !== column) { return ''; }
    return this.sortDirection() === 'asc' ? '▲' : '▼';
  }

  /** Recursively appends member rows to `rows`, with cycle detection via `visited`. */
  /**
   * Recursively adds child label rows to the tree, with cycle detection.
   * `ancestorLines[i]` is true when the ancestor at depth `i+1` has more siblings
   * below it, meaning we should draw a │ connector guide at that horizontal position.
   */
  private _addChildRows(
    rows: TreeRow[],
    children: LabelManagerEntry[],
    depth: number,
    visited: Set<string>,
    ancestorLines: boolean[],
  ): void {
    const sorted = [...children].sort((a, b) => (a.offset ?? 0) - (b.offset ?? 0));
    for (let idx = 0; idx < sorted.length; idx++) {
      const child = sorted[idx];
      const isLastChild = idx === sorted.length - 1;
      if (visited.has(child.address)) { continue; } // cycle guard
      const grandchildren = this._childrenOf(child.address);
      const canExpand = grandchildren.length > 0;
      rows.push({
        kind: 'member',
        depth,
        path: child.address,
        entry: child,
        expandable: canExpand,
        isLastChild,
        ancestorLines: [...ancestorLines],
      });
      if (canExpand && this.expandedPaths().has(child.address)) {
        const nextVisited = new Set(visited);
        nextVisited.add(child.address);
        // If this child is NOT the last, its descendants need a │ guide at this level.
        this._addChildRows(rows, grandchildren, depth + 1, nextVisited, [...ancestorLines, !isLastChild]);
      }
    }
  }

  onToggleAllVisibility(): void {
    const makeHidden = this.allVisible();
    const setName = this.activeSetName();
    const entries = this.filteredEntries();
    // Optimistic local update — flip all entries at once so the UI responds immediately
    // without waiting for each individual server round-trip.
    const set = this.currentSet();
    if (set) {
      const updates = new Map(entries.map(e => [e.address, { ...e, hidden: makeHidden ? true : undefined }]));
      this.currentSet.set({
        ...set,
        entries: set.entries.map(e => updates.get(e.address) ?? e),
      });
    }
    for (const entry of entries) {
      const updated: LabelManagerEntry = { ...entry, hidden: makeHidden ? true : undefined };
      this.action$.emit({ type: 'addEntry', setName, entry: updated });
    }
  }

  onEdit(entry: LabelManagerEntry): void {
    this.editFormOpen.set(true);
    this.editAddress.set(entry.address);
    this.editName.set(entry.name);
    this.editComment.set(entry.comment ?? '');
    const existingChildren = this._childrenOf(entry.address);
    // Map legacy Struct to Pointer; detect type from entry or fall back by children presence.
    const raw = entry.type ?? (existingChildren.length > 0 ? 'Pointer' : 'Address');
    const mapped = raw === 'Struct' ? 'Pointer' : raw;
    this.editType.set(mapped);
    this.editHidden.set(entry.hidden ?? false);
    this.editDynamic.set(entry.dynamic ?? false);
    // Populate editMembers from child entries, mapping legacy valueType → type.
    const children = this._childrenOf(entry.address).sort((a, b) => (a.offset ?? 0) - (b.offset ?? 0));
    this._editingOriginalChildAddresses = children.map(c => c.address);
    this.editMembers.set(children.map(c => ({
      offsetStr: '0x' + (c.offset ?? 0).toString(16).toUpperCase().padStart(2, '0'),
      name: c.name,
      type: c.type ?? legacyValueTypeToType(c.valueType) ?? undefined,
      comment: c.comment,
      dynamic: c.dynamic,
      hidden: c.hidden,
    })));
    this.editArgRegs.set((entry.argRegs ?? []).join('\n'));
    this.editReturnRegs.set((entry.returnRegs ?? []).join('\n'));
    this.editElementStride.set(entry.elementStride != null ? String(entry.elementStride) : '');
    this.editElementCount.set(entry.elementCount != null ? String(entry.elementCount) : '');
    this.editNodeNextOffset.set(entry.nodeNextOffset != null ? '0x' + entry.nodeNextOffset.toString(16).toUpperCase().padStart(2, '0') : '');
    this.editNodeSize.set(entry.nodeSize != null ? String(entry.nodeSize) : '');
    this.editingAddress.set(entry.address);
    this.editParentAddress.set(entry.parentAddress);
    this.editParentOffset.set(entry.offset);
    this.editOffsetStr.set(
      entry.parentAddress != null
        ? '0x' + (entry.offset ?? 0).toString(16).toUpperCase().padStart(4, '0')
        : '',
    );
  }

  onDeleteEntry(entry: LabelManagerEntry): void {
    this.pendingDeleteEntry.set(entry);
  }

  confirmDeleteEntry(): void {
    const entry = this.pendingDeleteEntry();
    if (!entry) { return; }
    this.pendingDeleteEntry.set(null);
    this.action$.emit({ type: 'deleteEntry', setName: this.activeSetName(), address: entry.address });
  }

  cancelDeleteEntry(): void {
    this.pendingDeleteEntry.set(null);
  }

  onWalkList(entry: LabelManagerEntry): void {
    if (entry.nodeNextOffset == null) { return; }
    this.action$.emit({ type: 'resolveLinkedList', address: entry.address, nextOffset: entry.nodeNextOffset });
  }

  onToggleDynamic(entry: LabelManagerEntry): void {
    // Use undefined (not false) so the field is omitted from the JSON payload when disabled,
    // matching the WhenWritingDefault serialisation convention on the C# side.
    const updated: LabelManagerEntry = { ...entry, dynamic: entry.dynamic ? undefined : true };
    if (this.isScannerView()) {
      // Scanner entries are ephemeral — update only the local scan results signal.
      this.scanResultEntries.update(entries =>
        entries.map(e => e.address === entry.address ? updated : e),
      );
    } else {
      // Optimistic local update so the toggle feels instant.
      const set = this.currentSet();
      if (set) {
        this.currentSet.set({
          ...set,
          entries: set.entries.map(e => e.address === entry.address ? updated : e),
        });
      }
      // Persist only for user (non-built-in) sets.
      if (!this.activeSetIsBuiltIn()) {
        this.action$.emit({ type: 'addEntry', setName: this.activeSetName(), entry: updated });
      }
    }
    // Start or stop polling based on whether any dynamic entries remain.
    this._updatePolling();
  }

  /** Toggle dynamic flag on a child entry directly. */
  onToggleChildDynamic(child: LabelManagerEntry): void {
    const updated: LabelManagerEntry = { ...child, dynamic: child.dynamic ? undefined : true };
    const set = this.currentSet();
    if (set) {
      this.currentSet.set({
        ...set,
        entries: set.entries.map(e => e.address === child.address ? updated : e),
      });
    }
    // Persist only for user (non-built-in) sets.
    if (!this.activeSetIsBuiltIn()) {
      this.action$.emit({ type: 'addEntry', setName: this.activeSetName(), entry: updated });
    }
    // Start or stop polling based on whether any dynamic entries remain.
    this._updatePolling();
  }

  /** Toggle hidden flag on a child entry directly. */
  onToggleChildVisibility(child: LabelManagerEntry): void {
    const updated: LabelManagerEntry = { ...child, hidden: child.hidden ? undefined : true };
    const set = this.currentSet();
    if (set) {
      this.currentSet.set({
        ...set,
        entries: set.entries.map(e => e.address === child.address ? updated : e),
      });
    }
    this.action$.emit({ type: 'addEntry', setName: this.activeSetName(), entry: updated });
  }

  /** Navigate to the resolved address of a child entry (dereferences parent pointer + offset). */
  onGotoChild(child: LabelManagerEntry): void {
    if (child.parentAddress != null && child.offset != null) {
      this.action$.emit({ type: 'navigateToMember', parentAddress: child.parentAddress, offset: child.offset });
    }
  }

  onSave(): void {
    const parentAddr = this.editParentAddress();

    // When editing a child entry, derive address from parent + offset.
    let addr: string;
    let childOffset: number | undefined;
    if (parentAddr) {
      childOffset = parseInt(this.editOffsetStr().replace(/^0x/i, ''), 16) || 0;
      addr = parentAddr + ':+0x' + childOffset.toString(16).toUpperCase().padStart(4, '0');
    } else {
      addr = this.editAddress().trim();
    }

    const name = this.editName().trim();
    if (!addr || !name) { return; }

    const rawType = this.editType();
    // Map legacy Struct → Pointer; all entries in UNIFIED_TYPES are semantic.
    const mappedType = rawType === 'Struct' ? 'Pointer' : rawType;
    const isSemantic = SEMANTIC_LABEL_TYPES.has(mappedType);
    const type: LabelType = isSemantic ? mappedType as LabelType : 'Address';
    const rootValueType: string | undefined = undefined; // data types are gone from the type picker
    const setName = this.activeSetName();

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
      parentAddress: parentAddr,
      offset: parentAddr != null ? childOffset : this.editParentOffset(),
      name,
      comment: this.editComment().trim() || undefined,
      type,
      valueType: rootValueType,
      hidden: this.editingAddress() ? this.editHidden() : false,
      dynamic: this.editDynamic() ? true : undefined,
      argRegs:       argRegs?.length      ? argRegs      : undefined,
      returnRegs:    returnRegs?.length    ? returnRegs   : undefined,
      elementStride,
      elementCount,
      nodeNextOffset,
      nodeSize,
    };
    this.action$.emit({ type: 'addEntry', setName, entry });

    // For Pointer type: save children as separate label entries.
    if (type === 'Pointer') {
      const newChildren = this.editMembers()
        .filter(m => m.name.trim())
        .map(m => {
          const offset = parseInt(m.offsetStr.replace(/^0x/i, ''), 16) || 0;
          const syntheticAddr = addr + ':+0x' + offset.toString(16).toUpperCase().padStart(4, '0');
          const child: LabelManagerEntry = {
            address: syntheticAddr,
            parentAddress: addr,
            offset,
            name: m.name.trim(),
            comment: m.comment?.trim() || undefined,
            type: (m.type || 'Address') as LabelType,
            dynamic: m.dynamic || undefined,
            hidden: m.hidden || undefined,
          };
          return child;
        });

      // Delete children that were removed (existed before but not in newChildren).
      const newChildAddresses = new Set(newChildren.map(c => c.address));
      for (const oldAddr of this._editingOriginalChildAddresses) {
        if (!newChildAddresses.has(oldAddr)) {
          this.action$.emit({ type: 'deleteEntry', setName, address: oldAddr });
        }
      }
      // Upsert all new/updated children.
      for (const child of newChildren) {
        this.action$.emit({ type: 'addEntry', setName, entry: child });
      }
    }

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

  updateMemberType(index: number, value: string): void {
    this.editMembers.update(m => m.map((item, i) => i === index ? { ...item, type: value || undefined } : item));
  }

  updateMemberComment(index: number, value: string): void {
    this.editMembers.update(m => m.map((item, i) => i === index ? { ...item, comment: value || undefined } : item));
  }

  clearForm(): void {
    this.editAddress.set('');
    this.editName.set('');
    this.editComment.set('');
    this.editType.set('Address');
    this.editHidden.set(false);
    this.editDynamic.set(false);
    this.editMembers.set([]);
    this._editingOriginalChildAddresses = [];
    this.editArgRegs.set('');
    this.editReturnRegs.set('');
    this.editElementStride.set('');
    this.editElementCount.set('');
    this.editNodeNextOffset.set('');
    this.editNodeSize.set('');
    this.editingAddress.set('');
    this.editParentAddress.set(undefined);
    this.editParentOffset.set(undefined);
    this.editOffsetStr.set('');
  }

  // ── Display helpers ───────────────────────────────────────────────────────────

  effectiveType(entry: LabelManagerEntry): LabelType {
    const t = entry.type;
    if (!t || t === 'Address') return 'Address';
    // Map legacy types to the new system.
    if (t === 'Struct') return 'Pointer';
    if (t === 'Array' || t === 'ArrayOfPointers' || t === 'LinkedList') return 'Value';
    return t as LabelType;
  }

  /**
   * Resolves the effective type for a child entry.
   * Children store their type in `type`; legacy entries may have used `valueType`.
   */
  childEffectiveType(entry: LabelManagerEntry): LabelType {
    const t = entry.type;
    if (t && t !== 'Address') return this.effectiveType(entry);
    // Legacy: map old valueType strings to the new type system.
    if (entry.valueType) {
      return legacyValueTypeToType(entry.valueType) as LabelType ?? 'Address';
    }
    return 'Address';
  }

  /** True when this entry's live value should be shown (requires dynamic===true). */
  shouldShowLive(entry: LabelManagerEntry): boolean {
    return entry.dynamic ?? false;
  }

  /** True when this child entry should show a live value (requires dynamic===true). */
  hasLiveChild(entry: LabelManagerEntry): boolean {
    return entry.dynamic ?? false;
  }

  liveValueClass(entry: LabelManagerEntry): string {
    switch (this.effectiveType(entry)) {
      case 'Float':   return 'lm-live-float';
      case 'Value':   return 'lm-live-number';
      case 'Pointer': return 'lm-live-ptr';
      default:        return 'lm-live-value';
    }
  }

  childLiveClass(entry: LabelManagerEntry): string {
    switch (this.childEffectiveType(entry)) {
      case 'Float':   return 'lm-live-float';
      case 'Value':   return 'lm-live-number';
      case 'Pointer': return 'lm-live-ptr';
      case 'String':  return 'lm-live-string';
      default:        return 'lm-live-value';
    }
  }

  /** Build the FNC live display: live-scanned prolog/epilog result when connected, else saved arg/ret annotations. */
  fncLiveDisplay(entry: LabelManagerEntry): string {
    // Prefer live-scanned value from provider (prolog/epilog analysis) if available.
    const live = this.liveValues().get(entry.address);
    if (live !== undefined && live !== '…') {
      return live;
    }
    // Fall back to saved static arg/ret register annotations.
    const parts: string[] = [];
    if (entry.argRegs?.length) {
      parts.push(`args[${entry.argRegs.map(r => r.split('=')[0].trim()).join(',')}]`);
    }
    if (entry.returnRegs?.length) {
      parts.push(`rets[${entry.returnRegs.map(r => r.split('=')[0].trim()).join(',')}]`);
    }
    return parts.join(' ');
  }

  typeBadge(entry: LabelManagerEntry): string {
    const etype = this.effectiveType(entry);
    return TYPE_BADGE[etype] ?? TYPE_BADGE[entry.type ?? 'Address'] ?? '[?]';
  }

  typeBadgeClass(entry: LabelManagerEntry): string {
    const etype = this.effectiveType(entry);
    const cls = TYPE_BADGE_CLASS[etype] ?? TYPE_BADGE_CLASS[entry.type ?? 'Address'] ?? '';
    return 'lm-type-badge ' + cls;
  }

  displayName(entry: LabelManagerEntry): string {
    if (this.effectiveType(entry) === 'Function') {
      // Avoid double-prefixing — the backend may already return FNC_ names.
      return entry.name.startsWith('FNC_') ? entry.name : 'FNC_' + entry.name;
    }
    return entry.name;
  }

  /** Returns the comment text for an entry. */
  entryComment(entry: LabelManagerEntry): string {
    return entry.comment ?? '';
  }

  entryDetails(entry: LabelManagerEntry): string {
    const childCount = this._childrenOf(entry.address).length;
    if (childCount > 0) {
      return `${childCount} child${childCount !== 1 ? 'ren' : ''}`;
    }
    switch (this.effectiveType(entry)) {
      case 'Pointer': return '—';
      case 'Function': {
        const parts: string[] = [];
        if (entry.argRegs?.length)    parts.push(`args: ${entry.argRegs.join(', ')}`);
        if (entry.returnRegs?.length) parts.push(`ret: ${entry.returnRegs.join(', ')}`);
        return parts.join(' | ') || '—';
      }
      default:
        return '—';
    }
  }

  memberSummary(entry: LabelManagerEntry): string {
    return this._childrenOf(entry.address)
      .sort((a, b) => (a.offset ?? 0) - (b.offset ?? 0))
      .map(c => `+0x${(c.offset ?? 0).toString(16).toUpperCase().padStart(2, '0')}: ${c.name}`)
      .join('\n');
  }

  private compareEntries(a: LabelManagerEntry, b: LabelManagerEntry, column: LabelSortColumn): number {
    switch (column) {
      case 'address':
        return this.parseSortAddress(a.address) - this.parseSortAddress(b.address);
      case 'type':
        return this.effectiveType(a).localeCompare(this.effectiveType(b));
      case 'live':
        return this.liveValue(a).localeCompare(this.liveValue(b), undefined, { numeric: true, sensitivity: 'base' });
      case 'name':
        return this.displayName(a).localeCompare(this.displayName(b), undefined, { numeric: true, sensitivity: 'base' });
      case 'comment':
        return this.entryComment(a).localeCompare(this.entryComment(b), undefined, { numeric: true, sensitivity: 'base' });
    }
  }

  private parseSortAddress(address: string): number {
    const normalized = address.split(':')[0].replace(/^0x/i, '');
    const parsed = parseInt(normalized, 16);
    return Number.isNaN(parsed) ? 0 : parsed;
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

  /** Update the member live value map from a provider response. */
  setMemberLiveValues(values: Record<string, string>): void {
    this.memberLiveValues.set(new Map<string, string>(Object.entries(values)));
  }

  liveValue(entry: LabelManagerEntry): string {
    return this.liveValues().get(entry.address) ?? '…';
  }

  /**
   * Start live-value polling if there are dynamic entries. Called by the
   * page-mode host.  In the default mode (`livePollingEnabled === false`),
   * this performs a single fetch and then stops.  When live polling is
   * enabled, it starts the 500ms interval timer.
   */
  startPolling(): void {
    // Always do a single immediate fetch so values are populated on open.
    this._emitOnePoll();
    if (this.livePollingEnabled()) {
      this._updatePolling();
    }
  }

  /** Toggle continuous live-value polling on/off. */
  setLivePolling(enabled: boolean): void {
    this.livePollingEnabled.set(enabled);
    if (enabled) {
      this._updatePolling();
    } else {
      this._stopPolling();
    }
  }

  /**
   * Start or stop the poll timer based on whether any dynamic entries exist.
   * If no entries have `dynamic === true`, polling is stopped to avoid
   * unnecessary socket traffic.
   */
  private _updatePolling(): void {
    if (!this.livePollingEnabled()) {
      this._stopPolling();
      return;
    }
    const hasDynamic =
      this._dynamicAddresses().length > 0 || this._dynamicMemberRows().length > 0;
    if (hasDynamic) {
      if (this._pollTimer === null) {
        this._startPolling();
      }
    } else {
      this._stopPolling();
    }
  }

  private _startPolling(): void {
    this._stopPolling();
    this._pollTimer = setInterval(() => {
      const entries = this._dynamicAddresses();
      if (entries.length > 0) {
        this.action$.emit({ type: 'readLiveValues', setName: this.activeSetName(), entries });
      }
      const dynMembers = this._dynamicMemberRows();
      if (dynMembers.length > 0) {
        this.action$.emit({
          type: 'readMemberLiveValues',
          setName: this.activeSetName(),
          members: dynMembers.map(r => ({
            parentAddress: r.entry.parentAddress!,
            offset: r.entry.offset ?? 0,
            path: r.path,
            entryType: this.childEffectiveType(r.entry) as LabelType,
            valueType: r.entry.valueType,
          })),
        });
      }
    }, 500);
  }

  private _stopPolling(): void {
    if (this._pollTimer !== null) {
      clearInterval(this._pollTimer);
      this._pollTimer = null;
    }
  }

  /** Emit a single poll cycle without starting the interval timer. */
  private _emitOnePoll(): void {
    const entries = this._dynamicAddresses();
    if (entries.length > 0) {
      this.action$.emit({ type: 'readLiveValues', setName: this.activeSetName(), entries });
    }
    const dynMembers = this._dynamicMemberRows();
    if (dynMembers.length > 0) {
      this.action$.emit({
        type: 'readMemberLiveValues',
        setName: this.activeSetName(),
        members: dynMembers.map(r => ({
          parentAddress: r.entry.parentAddress!,
          offset: r.entry.offset ?? 0,
          path: r.path,
          entryType: this.childEffectiveType(r.entry) as LabelType,
          valueType: r.entry.valueType,
        })),
      });
    }
  }
}

function splitLines(text: string): string[] {
  return text.split('\n').map(s => s.trim()).filter(Boolean);
}

/**
 * Maps a legacy `valueType` string (old data-type annotation) to the nearest
 * new semantic LabelType string.  Returns `null` when no mapping is applicable.
 */
function legacyValueTypeToType(valueType: string | undefined): string | null {
  if (!valueType) return null;
  switch (valueType.toLowerCase()) {
    case 'string':  return 'String';
    case 'float':
    case 'double':  return 'Float';
    case 'pointer': return 'Pointer';
    case 'u8': case 'u16': case 'u32': case 'u64':
    case 's8': case 's16': case 's32': case 's64': return 'Value';
    default:        return null;
  }
}
