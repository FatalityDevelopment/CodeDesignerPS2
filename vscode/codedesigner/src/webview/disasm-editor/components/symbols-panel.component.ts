import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  input,
  output,
  signal,
  computed,
} from '@angular/core';
import {
  SymLabel,
  SymString,
  SymFunction,
  SymPointer,
  SymFloat,
  SymInteger,
} from '../state/types';
import { IconComponent } from '../../shared/icon.component';

export type SymbolTab = 'labels' | 'strings' | 'functions' | 'pointers' | 'floats' | 'integers';

/** Page size for paginated lists */
const PAGE_SIZE = 100;

/**
 * SymbolsPanelComponent - Displays symbols across 6 tabs with filtering and pagination.
 *
 * Tabs: Labels, Strings, Functions, Pointers, Floats, Integers
 *
 * Uses signal inputs for reactive updates without zone.js.
 */
@Component({
  selector: 'app-symbols-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  imports: [IconComponent],
  template: `
    <div id="symbols-panel" class="symbols-panel" [class.visible]="visible()">
      <div id="symbols-panel-header">
        <span class="symbols-title">Symbols</span>
        <span id="sym-region-label" class="sym-region-label">{{ regionLabel() }}</span>
        <button class="sym-header-btn" (click)="onRefresh()" title="Refresh symbols"><app-icon name="refreshcw" [size]="12" /></button>
        <button class="sym-header-btn" (click)="onClose()" title="Close"><app-icon name="x" [size]="13" /></button>
      </div>

      <div id="sym-tabs">
        @for (tab of tabs; track tab.id) {
          <button
            class="sym-tab"
            [class.active]="activeTab() === tab.id"
            (click)="selectTab(tab.id)"
          >{{ tab.label }}</button>
        }
      </div>

      <!-- Labels Tab -->
      @if (activeTab() === 'labels') {
        <div id="sym-labels-content" class="sym-tab-content active">
          <div id="sym-add-row">
            <input
              type="text"
              #newAddrInput
              class="sym-add-input"
              placeholder="0x00000000"
              maxlength="10"
              [value]="newLabelAddr()"
              (input)="newLabelAddr.set(newAddrInput.value)"
            />
            <input
              type="text"
              #newNameInput
              class="sym-add-input"
              placeholder="Label name"
              [value]="newLabelName()"
              (input)="newLabelName.set(newNameInput.value)"
            />
            <input
              type="text"
              #newCommentInput
              class="sym-add-input"
              placeholder="Comment (opt.)"
              [value]="newLabelComment()"
              (input)="newLabelComment.set(newCommentInput.value)"
            />
            <button class="sym-add-btn" (click)="onAddLabel()" title="Add label"><app-icon name="plus" [size]="13" /></button>
          </div>
          <div class="sym-list" [innerHTML]="labelsHtml()"></div>
        </div>
      }

      <!-- Strings Tab -->
      @if (activeTab() === 'strings') {
        <div id="sym-strings-content" class="sym-tab-content active">
          <div class="sym-scan-row">
            <button class="sym-scan-btn" (click)="onScanStrings()">Scan for strings</button>
            <span class="sym-status">{{ stringsStatus() }}</span>
          </div>
          <div class="sym-filter-row">
            <input
              type="text"
              #stringsFilterInput
              class="sym-filter-input"
              placeholder="Filter strings..."
              [value]="stringsFilter()"
              (input)="onStringsFilterChange(stringsFilterInput.value)"
            />
            @if (getPageCount('strings') > 1) {
              <div class="sym-pager">
                <button class="sym-pager-btn" (click)="onPagerPrev('strings')" [disabled]="isPrevDisabled('strings')"><app-icon name="arrowleft" [size]="12" /><span>Prev</span></button>
                <button class="sym-pager-btn" (click)="onPagerNext('strings')" [disabled]="isNextDisabled('strings')"><span>Next</span><app-icon name="arrowright" [size]="12" /></button>
                <span class="sym-pager-info" [title]="getPagerTitle('strings')">{{ getPagerLabel('strings') }}</span>
              </div>
            }
          </div>
          <div class="sym-list" [innerHTML]="stringsHtml()"></div>
        </div>
      }

      <!-- Functions Tab -->
      @if (activeTab() === 'functions') {
        <div id="sym-functions-content" class="sym-tab-content active">
          <div class="sym-scan-row">
            <button class="sym-scan-btn" (click)="onScanFunctions()">Scan for functions</button>
            <span class="sym-status">{{ functionsStatus() }}</span>
          </div>
          <div class="sym-filter-row">
            <input
              type="text"
              #functionsFilterInput
              class="sym-filter-input"
              placeholder="Filter functions..."
              [value]="functionsFilter()"
              (input)="onFunctionsFilterChange(functionsFilterInput.value)"
            />
            @if (getPageCount('functions') > 1) {
              <div class="sym-pager">
                <button class="sym-pager-btn" (click)="onPagerPrev('functions')" [disabled]="isPrevDisabled('functions')"><app-icon name="arrowleft" [size]="12" /><span>Prev</span></button>
                <button class="sym-pager-btn" (click)="onPagerNext('functions')" [disabled]="isNextDisabled('functions')"><span>Next</span><app-icon name="arrowright" [size]="12" /></button>
                <span class="sym-pager-info" [title]="getPagerTitle('functions')">{{ getPagerLabel('functions') }}</span>
              </div>
            }
          </div>
          <div class="sym-list" [innerHTML]="functionsHtml()"></div>
        </div>
      }

      <!-- Pointers Tab -->
      @if (activeTab() === 'pointers') {
        <div id="sym-pointers-content" class="sym-tab-content active">
          <div class="sym-scan-row">
            <button class="sym-scan-btn" (click)="onScanPointers()">Scan for pointers</button>
            <span class="sym-status">{{ pointersStatus() }}</span>
          </div>
          <div class="sym-filter-row">
            <input
              type="text"
              #pointersFilterInput
              class="sym-filter-input"
              placeholder="Filter pointers..."
              [value]="pointersFilter()"
              (input)="onPointersFilterChange(pointersFilterInput.value)"
            />
            @if (getPageCount('pointers') > 1) {
              <div class="sym-pager">
                <button class="sym-pager-btn" (click)="onPagerPrev('pointers')" [disabled]="isPrevDisabled('pointers')"><app-icon name="arrowleft" [size]="12" /><span>Prev</span></button>
                <button class="sym-pager-btn" (click)="onPagerNext('pointers')" [disabled]="isNextDisabled('pointers')"><span>Next</span><app-icon name="arrowright" [size]="12" /></button>
                <span class="sym-pager-info" [title]="getPagerTitle('pointers')">{{ getPagerLabel('pointers') }}</span>
              </div>
            }
          </div>
          <div class="sym-list" [innerHTML]="pointersHtml()"></div>
        </div>
      }

      <!-- Floats Tab -->
      @if (activeTab() === 'floats') {
        <div id="sym-floats-content" class="sym-tab-content active">
          <div class="sym-scan-row">
            <button class="sym-scan-btn" (click)="onScanFloats()">Scan for floats</button>
            <span class="sym-status">{{ floatsStatus() }}</span>
          </div>
          <div class="sym-filter-row">
            <input
              type="text"
              #floatsFilterInput
              class="sym-filter-input"
              placeholder="Filter floats..."
              [value]="floatsFilter()"
              (input)="onFloatsFilterChange(floatsFilterInput.value)"
            />
            @if (getPageCount('floats') > 1) {
              <div class="sym-pager">
                <button class="sym-pager-btn" (click)="onPagerPrev('floats')" [disabled]="isPrevDisabled('floats')"><app-icon name="arrowleft" [size]="12" /><span>Prev</span></button>
                <button class="sym-pager-btn" (click)="onPagerNext('floats')" [disabled]="isNextDisabled('floats')"><span>Next</span><app-icon name="arrowright" [size]="12" /></button>
                <span class="sym-pager-info" [title]="getPagerTitle('floats')">{{ getPagerLabel('floats') }}</span>
              </div>
            }
          </div>
          <div class="sym-list" [innerHTML]="floatsHtml()"></div>
        </div>
      }

      <!-- Integers Tab -->
      @if (activeTab() === 'integers') {
        <div id="sym-integers-content" class="sym-tab-content active">
          <div class="sym-scan-row">
            <button class="sym-scan-btn" (click)="onScanIntegers()">Scan for integers</button>
            <span class="sym-status">{{ integersStatus() }}</span>
          </div>
          <div class="sym-filter-row">
            <input
              type="text"
              #integersFilterInput
              class="sym-filter-input"
              placeholder="Filter integers..."
              [value]="integersFilter()"
              (input)="onIntegersFilterChange(integersFilterInput.value)"
            />
            @if (getPageCount('integers') > 1) {
              <div class="sym-pager">
                <button class="sym-pager-btn" (click)="onPagerPrev('integers')" [disabled]="isPrevDisabled('integers')"><app-icon name="arrowleft" [size]="12" /><span>Prev</span></button>
                <button class="sym-pager-btn" (click)="onPagerNext('integers')" [disabled]="isNextDisabled('integers')"><span>Next</span><app-icon name="arrowright" [size]="12" /></button>
                <span class="sym-pager-info" [title]="getPagerTitle('integers')">{{ getPagerLabel('integers') }}</span>
              </div>
            }
          </div>
          <div class="sym-list" [innerHTML]="integersHtml()"></div>
        </div>
      }
    </div>
  `,
  host: {
    '(click)': 'handlePanelClick($event)',
  },
})
export class SymbolsPanelComponent {
  // ═══════════════════════════════════════════════════════════════════════════════
  // Tab Definitions
  // ═══════════════════════════════════════════════════════════════════════════════

  readonly tabs: { id: SymbolTab; label: string }[] = [
    { id: 'labels', label: 'Labels' },
    { id: 'strings', label: 'Strings' },
    { id: 'functions', label: 'Functions' },
    { id: 'pointers', label: 'Pointers' },
    { id: 'floats', label: 'Floats' },
    { id: 'integers', label: 'Integers' },
  ];

  // ═══════════════════════════════════════════════════════════════════════════════
  // Inputs
  // ═══════════════════════════════════════════════════════════════════════════════

  readonly visible = input(false);
  readonly regionLabel = input('');

  readonly labels = input<SymLabel[]>([]);
  readonly strings = input<SymString[]>([]);
  readonly functions = input<SymFunction[]>([]);
  readonly pointers = input<SymPointer[]>([]);
  readonly floats = input<SymFloat[]>([]);
  readonly integers = input<SymInteger[]>([]);

  readonly stringsStatus = input('');
  readonly functionsStatus = input('');
  readonly pointersStatus = input('');
  readonly floatsStatus = input('');
  readonly integersStatus = input('');

  // ═══════════════════════════════════════════════════════════════════════════════
  // Outputs
  // ═══════════════════════════════════════════════════════════════════════════════

  readonly close = output<void>();
  readonly refresh = output<void>();
  readonly navigateToAddress = output<string>();

  readonly addLabel = output<{ address: string; name: string; comment: string }>();
  readonly deleteLabel = output<string>();

  readonly scanStrings = output<void>();
  readonly scanFunctions = output<void>();
  readonly scanPointers = output<void>();
  readonly scanFloats = output<void>();
  readonly scanIntegers = output<void>();

  // ═══════════════════════════════════════════════════════════════════════════════
  // Internal State
  // ═══════════════════════════════════════════════════════════════════════════════

  readonly activeTab = signal<SymbolTab>('labels');

  // New label form
  readonly newLabelAddr = signal('');
  readonly newLabelName = signal('');
  readonly newLabelComment = signal('');

  // Filters
  readonly stringsFilter = signal('');
  readonly functionsFilter = signal('');
  readonly pointersFilter = signal('');
  readonly floatsFilter = signal('');
  readonly integersFilter = signal('');

  // Page numbers
  readonly stringsPage = signal(0);
  readonly functionsPage = signal(0);
  readonly pointersPage = signal(0);
  readonly floatsPage = signal(0);
  readonly integersPage = signal(0);

  // ═══════════════════════════════════════════════════════════════════════════════
  // Computed: Filtered Data
  // ═══════════════════════════════════════════════════════════════════════════════

  readonly filteredStrings = computed(() => {
    const filter = this.stringsFilter().toLowerCase();
    const items = this.strings();
    if (!filter) return items;
    return items.filter(
      (item) =>
        item.address.toLowerCase().includes(filter) ||
        item.value.toLowerCase().includes(filter)
    );
  });

  readonly filteredFunctions = computed(() => {
    const filter = this.functionsFilter().toLowerCase();
    const items = this.functions();
    if (!filter) return items;
    return items.filter(
      (item) =>
        item.address.toLowerCase().includes(filter) ||
        item.label.toLowerCase().includes(filter)
    );
  });

  readonly filteredPointers = computed(() => {
    const filter = this.pointersFilter().toLowerCase();
    const items = this.pointers();
    if (!filter) return items;
    return items.filter(
      (item) =>
        item.address.toLowerCase().includes(filter) ||
        item.pointsTo.toLowerCase().includes(filter)
    );
  });

  readonly filteredFloats = computed(() => {
    const filter = this.floatsFilter().toLowerCase();
    const items = this.floats();
    if (!filter) return items;
    return items.filter(
      (item) =>
        item.address.toLowerCase().includes(filter) ||
        item.raw.toLowerCase().includes(filter)
    );
  });

  readonly filteredIntegers = computed(() => {
    const filter = this.integersFilter().toLowerCase();
    const items = this.integers();
    if (!filter) return items;
    return items.filter(
      (item) =>
        item.address.toLowerCase().includes(filter) ||
        item.hex.toLowerCase().includes(filter) ||
        String(item.value).includes(filter)
    );
  });

  // ═══════════════════════════════════════════════════════════════════════════════
  // Computed: Labels HTML
  // ═══════════════════════════════════════════════════════════════════════════════

  readonly labelsHtml = computed(() => {
    const items = this.labels();
    if (items.length === 0) {
      return '<div class="sym-empty">No labels defined.</div>';
    }
    return items
      .map((item) => {
        const comment = item.comment
          ? `<span class="sym-item-comment" title="${this.escapeHtml(item.comment)}">${this.escapeHtml(item.comment)}</span>`
          : '';
        return `<div class="sym-item" data-addr="${this.escapeHtml(item.address)}">
          <span class="sym-item-addr">${this.escapeHtml(item.address)}</span>
          <span class="sym-item-name" title="${this.escapeHtml(item.label)}">${this.escapeHtml(item.label)}</span>
          ${comment}
          <button class="sym-delete-btn" data-addr="${this.escapeHtml(item.address)}" title="Delete label">${this.inlineTrashIcon()}</button>
        </div>`;
      })
      .join('');
  });

  // ═══════════════════════════════════════════════════════════════════════════════
  // Computed: Strings HTML + Pager
  // ═══════════════════════════════════════════════════════════════════════════════

  readonly stringsHtml = computed(() => {
    const items = this.filteredStrings();
    if (items.length === 0) {
      return '<div class="sym-empty">No strings found. Click "Scan for strings".</div>';
    }
    const page = this.stringsPage();
    const start = page * PAGE_SIZE;
    const displayed = items.slice(start, start + PAGE_SIZE);
    return displayed
      .map(
        (item) =>
          `<div class="sym-item" data-addr="${this.escapeHtml(item.address)}">
            <span class="sym-item-addr">${this.escapeHtml(item.address)}</span>
            <span class="sym-item-value" title="${this.escapeHtml(item.value)}">${this.escapeHtml(item.value)}</span>
          </div>`
      )
      .join('');
  });

  // ═══════════════════════════════════════════════════════════════════════════════
  // Computed: Functions HTML + Pager
  // ═══════════════════════════════════════════════════════════════════════════════

  readonly functionsHtml = computed(() => {
    const items = this.filteredFunctions();
    if (items.length === 0) {
      return '<div class="sym-empty">No functions found. Click "Scan for functions".</div>';
    }
    const page = this.functionsPage();
    const start = page * PAGE_SIZE;
    const displayed = items.slice(start, start + PAGE_SIZE);
    return displayed
      .map(
        (item) =>
          `<div class="sym-item" data-addr="${this.escapeHtml(item.address)}">
            <span class="sym-item-addr">${this.escapeHtml(item.address)}</span>
            <span class="sym-item-name" title="${this.escapeHtml(item.label)}">${this.escapeHtml(item.label)}</span>
          </div>`
      )
      .join('');
  });

  // ═══════════════════════════════════════════════════════════════════════════════
  // Computed: Pointers HTML + Pager
  // ═══════════════════════════════════════════════════════════════════════════════

  readonly pointersHtml = computed(() => {
    const items = this.filteredPointers();
    if (items.length === 0) {
      return '<div class="sym-empty">No pointers found. Click "Scan for pointers".</div>';
    }
    const page = this.pointersPage();
    const start = page * PAGE_SIZE;
    const displayed = items.slice(start, start + PAGE_SIZE);
    return displayed
      .map(
        (item) =>
          `<div class="sym-item" data-addr="${this.escapeHtml(item.address)}">
            <span class="sym-item-addr">${this.escapeHtml(item.address)}</span>
            <span class="sym-item-value" title="-> ${this.escapeHtml(item.pointsTo)}">-> ${this.escapeHtml(item.pointsTo)}</span>
          </div>`
      )
      .join('');
  });

  // ═══════════════════════════════════════════════════════════════════════════════
  // Computed: Floats HTML + Pager
  // ═══════════════════════════════════════════════════════════════════════════════

  readonly floatsHtml = computed(() => {
    const items = this.filteredFloats();
    if (items.length === 0) {
      return '<div class="sym-empty">No floats found. Click "Scan for floats".</div>';
    }
    const page = this.floatsPage();
    const start = page * PAGE_SIZE;
    const displayed = items.slice(start, start + PAGE_SIZE);
    return displayed
      .map((item) => {
        const floatVal = this.rawBitsToFloat(parseInt(item.raw, 16) >>> 0);
        return `<div class="sym-item" data-addr="${this.escapeHtml(item.address)}">
          <span class="sym-item-addr">${this.escapeHtml(item.address)}</span>
          <span class="sym-item-value" title="${this.escapeHtml(item.raw)}">${floatVal} (${this.escapeHtml(item.raw)})</span>
        </div>`;
      })
      .join('');
  });

  // ═══════════════════════════════════════════════════════════════════════════════
  // Computed: Integers HTML + Pager
  // ═══════════════════════════════════════════════════════════════════════════════

  readonly integersHtml = computed(() => {
    const items = this.filteredIntegers();
    if (items.length === 0) {
      return '<div class="sym-empty">No integers found. Click "Scan for integers".</div>';
    }
    const page = this.integersPage();
    const start = page * PAGE_SIZE;
    const displayed = items.slice(start, start + PAGE_SIZE);
    return displayed
      .map(
        (item) =>
          `<div class="sym-item" data-addr="${this.escapeHtml(item.address)}">
            <span class="sym-item-addr">${this.escapeHtml(item.address)}</span>
            <span class="sym-item-value" title="${this.escapeHtml(item.hex)}">${item.value} (${this.escapeHtml(item.hex)})</span>
          </div>`
      )
      .join('');
  });

  // ═══════════════════════════════════════════════════════════════════════════════
  // Tab Selection
  // ═══════════════════════════════════════════════════════════════════════════════

  selectTab(tab: SymbolTab): void {
    this.activeTab.set(tab);
  }

  // ═══════════════════════════════════════════════════════════════════════════════
  // Event Handlers
  // ═══════════════════════════════════════════════════════════════════════════════

  onRefresh(): void {
    this.refresh.emit();
  }

  onClose(): void {
    this.close.emit();
  }

  onScanStrings(): void {
    this.stringsPage.set(0);
    this.scanStrings.emit();
  }

  onScanFunctions(): void {
    this.functionsPage.set(0);
    this.scanFunctions.emit();
  }

  onScanPointers(): void {
    this.pointersPage.set(0);
    this.scanPointers.emit();
  }

  onScanFloats(): void {
    this.floatsPage.set(0);
    this.scanFloats.emit();
  }

  onScanIntegers(): void {
    this.integersPage.set(0);
    this.scanIntegers.emit();
  }

  onAddLabel(): void {
    const address = this.newLabelAddr().trim();
    const name = this.newLabelName().trim();
    const comment = this.newLabelComment().trim();
    if (!address || !name) return;

    this.addLabel.emit({ address, name, comment });
    // Clear form
    this.newLabelAddr.set('');
    this.newLabelName.set('');
    this.newLabelComment.set('');
  }

  // Filter change handlers (reset page to 0)
  onStringsFilterChange(value: string): void {
    this.stringsFilter.set(value);
    this.stringsPage.set(0);
  }

  onFunctionsFilterChange(value: string): void {
    this.functionsFilter.set(value);
    this.functionsPage.set(0);
  }

  onPointersFilterChange(value: string): void {
    this.pointersFilter.set(value);
    this.pointersPage.set(0);
  }

  onFloatsFilterChange(value: string): void {
    this.floatsFilter.set(value);
    this.floatsPage.set(0);
  }

  onIntegersFilterChange(value: string): void {
    this.integersFilter.set(value);
    this.integersPage.set(0);
  }

  /**
   * Handle clicks on the panel content.
   * Delegates to appropriate action based on target.
   */
  handlePanelClick(event: Event): void {
    const source = event.target;
    if (!(source instanceof Element)) {
      return;
    }

    // Check for delete button
    const deleteBtn = source.closest('.sym-delete-btn') as HTMLElement | null;
    if (deleteBtn) {
      event.stopPropagation();
      const addr = deleteBtn.dataset['addr'];
      if (addr) {
        this.deleteLabel.emit(addr);
      }
      return;
    }

    // Check for pager buttons
    const pagerBtn = source.closest('.sym-pager-btn') as HTMLButtonElement | null;
    if (pagerBtn) {
      event.stopPropagation();
      if (pagerBtn.disabled) {
        return;
      }
      const action = pagerBtn.dataset['action'] as string;
      const tabType = pagerBtn.dataset['tab'] as string;
      if (action && tabType) {
        this.handlePagerClick(tabType, action);
      }
      return;
    }

    // Check for item click (navigate to address)
    const item = source.closest('.sym-item') as HTMLElement | null;
    if (item) {
      const addr = item.dataset['addr'];
      if (addr) {
        this.navigateToAddress.emit(addr);
      }
    }
  }

  // ═══════════════════════════════════════════════════════════════════════════════
  // Pagination Handlers
  // ═══════════════════════════════════════════════════════════════════════════════

  private handlePagerClick(tabType: string, action: string): void {
    switch (tabType) {
      case 'strings':
        this.stringsPage.set(this.getNextPage(this.stringsPage(), this.filteredStrings().length, action));
        break;
      case 'functions':
        this.functionsPage.set(this.getNextPage(this.functionsPage(), this.filteredFunctions().length, action));
        break;
      case 'pointers':
        this.pointersPage.set(this.getNextPage(this.pointersPage(), this.filteredPointers().length, action));
        break;
      case 'floats':
        this.floatsPage.set(this.getNextPage(this.floatsPage(), this.filteredFloats().length, action));
        break;
      case 'integers':
        this.integersPage.set(this.getNextPage(this.integersPage(), this.filteredIntegers().length, action));
        break;
    }
  }

  onPagerPrev(tabType: string): void {
    this.handlePagerClick(tabType, 'prev');
  }

  onPagerNext(tabType: string): void {
    this.handlePagerClick(tabType, 'next');
  }

  getPageCount(tabType: string): number {
    const totalItems = this.getTotalItems(tabType);
    return Math.max(1, Math.ceil(totalItems / PAGE_SIZE));
  }

  getPagerLabel(tabType: string): string {
    const totalItems = this.getTotalItems(tabType);
    if (totalItems === 0) { return 'Page 1/1'; }
    const totalPages = this.getPageCount(tabType);
    const page = this.getCurrentPage(tabType, totalPages);
    return `Page ${page + 1}/${totalPages}`;
  }

  getPagerTitle(tabType: string): string {
    const totalItems = this.getTotalItems(tabType);
    if (totalItems === 0) { return 'No results'; }
    const totalPages = this.getPageCount(tabType);
    const page = this.getCurrentPage(tabType, totalPages);
    const start = page * PAGE_SIZE;
    const end = Math.min(start + PAGE_SIZE, totalItems);
    return `Page ${page + 1} of ${totalPages} (${start + 1}-${end} of ${totalItems})`;
  }

  isPrevDisabled(tabType: string): boolean {
    const totalPages = this.getPageCount(tabType);
    const page = this.getCurrentPage(tabType, totalPages);
    return page <= 0;
  }

  isNextDisabled(tabType: string): boolean {
    const totalPages = this.getPageCount(tabType);
    const page = this.getCurrentPage(tabType, totalPages);
    return page >= totalPages - 1;
  }

  private getTotalItems(tabType: string): number {
    switch (tabType) {
      case 'strings':   return this.filteredStrings().length;
      case 'functions': return this.filteredFunctions().length;
      case 'pointers':  return this.filteredPointers().length;
      case 'floats':    return this.filteredFloats().length;
      case 'integers':  return this.filteredIntegers().length;
      default:          return 0;
    }
  }

  private getCurrentPage(tabType: string, totalPages: number): number {
    const maxPage = Math.max(0, totalPages - 1);
    switch (tabType) {
      case 'strings':   return Math.min(Math.max(0, this.stringsPage()), maxPage);
      case 'functions': return Math.min(Math.max(0, this.functionsPage()), maxPage);
      case 'pointers':  return Math.min(Math.max(0, this.pointersPage()), maxPage);
      case 'floats':    return Math.min(Math.max(0, this.floatsPage()), maxPage);
      case 'integers':  return Math.min(Math.max(0, this.integersPage()), maxPage);
      default:          return 0;
    }
  }

  private getNextPage(currentPage: number, totalItems: number, action: string): number {
    const maxPage = Math.max(0, Math.ceil(totalItems / PAGE_SIZE) - 1);
    const clampedCurrent = Math.min(Math.max(0, currentPage), maxPage);
    if (action === 'prev') { return Math.max(0, clampedCurrent - 1); }
    if (action === 'next') { return Math.min(maxPage, clampedCurrent + 1); }
    return clampedCurrent;
  }

  // ═══════════════════════════════════════════════════════════════════════════════
  // Utilities
  // ═══════════════════════════════════════════════════════════════════════════════

  private escapeHtml(str: string): string {
    return str
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  private inlineTrashIcon(): string {
    return '<svg class="lic-inline" xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 6h18" /><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6" /><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2" /><line x1="10" x2="10" y1="11" y2="17" /><line x1="14" x2="14" y1="11" y2="17" /></svg>';
  }

  private rawBitsToFloat(bits: number): string {
    const buffer = new ArrayBuffer(4);
    const view = new DataView(buffer);
    view.setUint32(0, bits, true);
    const float = view.getFloat32(0, true);
    return float.toFixed(6);
  }
}
