import { Component, AfterViewInit, NgZone, ViewEncapsulation } from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import {
  DisasmEditorService,
  DisasmRow,
  SearchMatch,
  RegisterData,
  ThreadEntry,
  SymPointer,
  SymFloat,
  SymInteger,
} from './disasm-editor.service';
import { HexViewComponent } from './components/hex-view.component';
import './disasm-editor.css';

@Component({
  selector: 'app-disasm-editor',
  standalone: true,
  imports: [HexViewComponent],
  providers: [DisasmEditorService],
  encapsulation: ViewEncapsulation.None,
  template: `
<div id="header">
<div id="header-toolbar">
    <button class="header-tool-btn" id="symbols-toggle-btn" title="Toggle Symbols Panel (Ctrl+Alt+S)">&#8862; Symbols</button>
    <button class="header-tool-btn" id="registers-toggle-btn" title="Toggle Registers Panel (Ctrl+Alt+R)">&#8863; Regs</button>
    <button class="header-tool-btn" id="bottom-panel-toggle-btn" title="Toggle Bottom Panel (Ctrl+Alt+T)">&#8693; Stack</button>
    <button class="header-tool-btn" id="search-toggle-btn" title="Search (Ctrl+F)">&#8853; Search</button>
    <button class="header-tool-btn" id="goto-toggle-btn" title="Go to Address (Ctrl+Alt+A)">&#8594; Go To</button>
    <button class="header-tool-btn live-only" id="setpc-toggle-btn" title="Set PC to Address (Ctrl+Alt+P)">&#9655; Set PC</button>
    <button class="header-tool-btn" id="nav-back-btn" title="Navigate Back (Alt+Left)" disabled>&#8592; Back</button>

</div>
<div id="goto-panel">
    <div class="search-row">
        <div class="search-input-wrapper">
            <input type="text" id="goto-input" class="search-input" placeholder="Go to address (hex)" />
            <button class="search-clear" id="goto-clear">&#xD7;</button>
        </div>
        <div class="search-nav">
            <button class="nav-btn" id="goto-btn" title="Go to address (Enter)">Go</button>
        </div>
        <span class="search-info" id="goto-info"></span>
        <button class="close-search" id="close-goto" title="Close (Escape)">&#xD7;</button>
    </div>
</div>
<div id="setpc-panel">
    <div class="search-row">
        <div class="search-input-wrapper">
            <input type="text" id="setpc-input" class="search-input" placeholder="New PC address (hex)" />
            <button class="search-clear" id="setpc-clear">&#xD7;</button>
        </div>
        <div class="search-nav">
            <button class="nav-btn" id="setpc-btn" title="Set PC (Enter)">Set PC</button>
        </div>
        <span class="search-info" id="setpc-info"></span>
        <button class="close-search" id="close-setpc" title="Close (Escape)">&#xD7;</button>
    </div>
</div>
<div id="search-panel">
    <div class="search-row">
        <div class="search-input-wrapper">
            <input type="text" id="search-input" class="search-input" placeholder="Search (text or bytes: 0x12 34 AB)" />
            <button class="search-clear" id="search-clear">&#xD7;</button>
        </div>
        <div class="search-mode">
            <button class="mode-btn active" data-mode="text" title="Text Search">Aa</button>
            <button class="mode-btn" data-mode="bytes" title="Byte Pattern Search">0x</button>
            <button class="mode-btn" data-mode="string" title="String Search (ASCII/UTF-8)">"ab"</button>
            <button class="mode-btn" data-mode="regex" title="Regular Expression">.*</button>
        </div>
        <div class="search-nav">
            <button class="nav-btn" id="prev-match" title="Previous Match (Shift+Enter)">&#8593;</button>
            <button class="nav-btn" id="next-match" title="Next Match (Enter)">&#8595;</button>
        </div>
        <span class="search-info" id="search-info">No results</span>
        <button class="close-search" id="close-search" title="Close (Escape)">&#xD7;</button>
    </div>
</div>
<app-hex-view
    [data]="hexViewData"
    [offset]="hexViewOffset"
    [highlightAddr]="hexViewHighlightAddr"
    [highlightLength]="hexViewHighlightLength">
</app-hex-view>
</div>
<div id="content-wrapper">
<div id="grid" tabindex="0"></div>
<div id="symbols-panel">
    <div id="symbols-panel-header">
        <span class="symbols-title">Symbols</span>
        <span id="sym-region-label" class="sym-region-label"></span>
        <button class="sym-header-btn" id="symbols-refresh-btn" title="Refresh symbols">&#8635;</button>
        <button class="sym-header-btn" id="close-symbols-btn" title="Close">&#xD7;</button>
    </div>
    <div id="sym-tabs">
        <button class="sym-tab active" data-symtab="labels">Labels</button>
        <button class="sym-tab" data-symtab="strings">Strings</button>
        <button class="sym-tab" data-symtab="functions">Functions</button>
        <button class="sym-tab" data-symtab="pointers">Pointers</button>
        <button class="sym-tab" data-symtab="floats">Floats</button>
        <button class="sym-tab" data-symtab="integers">Integers</button>
    </div>
    <div id="sym-labels-content" class="sym-tab-content active">
        <div id="sym-add-row">
            <input type="text" id="sym-new-addr"    class="sym-add-input" placeholder="0x00000000" maxlength="10" />
            <input type="text" id="sym-new-name"    class="sym-add-input" placeholder="Label name" />
            <input type="text" id="sym-new-comment" class="sym-add-input" placeholder="Comment (opt.)" />
            <button id="sym-add-btn" class="sym-add-btn" title="Add label">+</button>
        </div>
        <div id="sym-labels-list" class="sym-list"></div>
    </div>
    <div id="sym-strings-content" class="sym-tab-content">
        <div class="sym-scan-row">
            <button id="sym-scan-strings-btn" class="sym-scan-btn">Scan for strings</button>
            <span id="sym-strings-status" class="sym-status"></span>
        </div>
        <div id="sym-strings-list" class="sym-list"></div>
    </div>
    <div id="sym-functions-content" class="sym-tab-content">
        <div class="sym-scan-row">
            <button id="sym-scan-functions-btn" class="sym-scan-btn">Scan for functions</button>
            <span id="sym-functions-status" class="sym-status"></span>
        </div>
        <div id="sym-functions-list" class="sym-list"></div>
    </div>
    <div id="sym-pointers-content" class="sym-tab-content">
        <div class="sym-scan-row">
            <button id="sym-scan-pointers-btn" class="sym-scan-btn">Scan for pointers</button>
            <span id="sym-pointers-status" class="sym-status"></span>
        </div>
        <div id="sym-pointers-list" class="sym-list"></div>
    </div>
    <div id="sym-floats-content" class="sym-tab-content">
        <div class="sym-scan-row">
            <button id="sym-scan-floats-btn" class="sym-scan-btn">Scan for floats</button>
            <span id="sym-floats-status" class="sym-status"></span>
        </div>
        <div id="sym-floats-list" class="sym-list"></div>
    </div>
    <div id="sym-integers-content" class="sym-tab-content">
        <div class="sym-scan-row">
            <button id="sym-scan-integers-btn" class="sym-scan-btn">Scan for integers</button>
            <span id="sym-integers-status" class="sym-status"></span>
        </div>
        <div id="sym-integers-list" class="sym-list"></div>
    </div>
</div>
<div id="debug-panel">
    <div id="debug-tabs">
        <button class="debug-tab active" data-tab="registers">GPR</button>
        <button class="debug-tab" data-tab="cop0">CP0</button>
        <button class="debug-tab" data-tab="fpr">FPR</button>
        <button class="debug-tab" data-tab="fcr">FCR</button>
        <button class="debug-tab" data-tab="vu0f">VU0f</button>
        <button class="debug-tab" data-tab="vu0i">VU0i</button>
        <button class="debug-tab" data-tab="gs">GS</button>
        <button class="debug-tab" data-tab="stack">Stack</button>
    </div>
    <div id="registers-content" class="debug-tab-content active"></div>
    <div id="cop0-content" class="debug-tab-content"></div>
    <div id="fpr-content" class="debug-tab-content"></div>
    <div id="fcr-content" class="debug-tab-content"></div>
    <div id="vu0f-content" class="debug-tab-content"></div>
    <div id="vu0i-content" class="debug-tab-content"></div>
    <div id="gs-content" class="debug-tab-content"></div>
    <div id="stack-content" class="debug-tab-content"></div>
</div>
</div>
<div id="bottom-panel">
    <div id="bottom-panel-header">
        <div id="bottom-tabs">
            <button class="bottom-tab active" data-btab="callstack">Call Stack</button>
            <button class="bottom-tab" data-btab="threads">Threads</button>
            <button class="bottom-tab" data-btab="breakpoints">Breakpoints</button>
        </div>
        <div id="bottom-panel-actions">
            <button id="bottom-refresh-btn" class="bottom-action-btn" title="Refresh">&#8635;</button>
        </div>
    </div>
    <div id="callstack-content" class="bottom-tab-content active">
        <table class="dbg-table">
            <thead><tr><th class="dbg-th">#</th><th class="dbg-th">PC</th><th class="dbg-th">Function</th></tr></thead>
            <tbody id="callstack-body"></tbody>
        </table>
    </div>
    <div id="threads-content" class="bottom-tab-content">
        <table class="dbg-table">
            <thead><tr><th class="dbg-th">TID</th><th class="dbg-th">Status</th><th class="dbg-th">PC</th><th class="dbg-th">SP</th><th class="dbg-th">Priority</th><th class="dbg-th">Entry</th></tr></thead>
            <tbody id="threads-body"></tbody>
        </table>
    </div>
    <div id="breakpoints-content" class="bottom-tab-content">
        <div id="breakpoints-toolbar">
            <button id="bp-clear-all-btn" class="bottom-action-btn" title="Remove all breakpoints">Clear All</button>
        </div>
        <table class="dbg-table">
            <thead><tr><th class="dbg-th">Address</th><th class="dbg-th">Type</th><th class="dbg-th">Condition</th><th class="dbg-th">Enabled</th><th class="dbg-th"></th></tr></thead>
            <tbody id="breakpoints-body"></tbody>
        </table>
    </div>
</div>
<div id="edit-overlay">
    <div id="edit-overlay-box">
        <div id="edit-overlay-header">
            <span id="edit-overlay-title">Edit Address</span>
            <button id="edit-overlay-close" title="Close (Escape)">&#xD7;</button>
        </div>
        <div id="edit-overlay-tabs">
            <button class="edit-overlay-tab active" data-tab="edit">Edit</button>
            <button class="edit-overlay-tab" data-tab="history">History</button>
        </div>
        <div id="edit-overlay-tab-edit" class="edit-overlay-tab-content active">
            <div class="edit-overlay-field">
                <label class="edit-overlay-label" for="edit-overlay-instr-in">Instruction</label>
                <input type="text" id="edit-overlay-instr-in" class="edit-overlay-input" placeholder="e.g. ADDIU $v0, $zero, 5" spellcheck="false" />
            </div>
            <div class="edit-overlay-field">
                <label class="edit-overlay-label" for="edit-overlay-bytes-in">Bytes (hex)</label>
                <input type="text" id="edit-overlay-bytes-in" class="edit-overlay-input" placeholder="e.g. 24020005" spellcheck="false" />
            </div>
            <div id="edit-overlay-disasm-preview" class="edit-overlay-preview"></div>
            <div class="edit-overlay-field">
                <label class="edit-overlay-label" for="edit-overlay-label-input">Label</label>
                <input type="text" id="edit-overlay-label-input" class="edit-overlay-input" placeholder="Label name (leave empty to remove)" />
            </div>
            <div class="edit-overlay-field">
                <label class="edit-overlay-label" for="edit-overlay-comment-input">Comment</label>
                <input type="text" id="edit-overlay-comment-input" class="edit-overlay-input" placeholder="Comment (leave empty to remove)" />
            </div>
            <div id="edit-overlay-actions">
                <button id="edit-overlay-save">Save</button>
                <button id="edit-overlay-cancel">Cancel</button>
            </div>
        </div>
        <div id="edit-overlay-tab-history" class="edit-overlay-tab-content">
            <div id="edit-overlay-history-list" class="edit-overlay-history-list">
                <div class="edit-overlay-history-empty">Loading history...</div>
            </div>
        </div>
    </div>
</div>
  `
})
export class DisasmEditorComponent implements AfterViewInit {

  // ── Hex view @Input() bindings ─────────────────────────────────────────────
  // Updated via NgZone.run() from the outside-zone editor logic so Angular
  // change detection propagates them to HexViewComponent.ngOnChanges().
  hexViewData: number[] = [];
  hexViewOffset = 0;
  hexViewHighlightAddr = -1;
  hexViewHighlightLength = 4;

  constructor(private zone: NgZone, private svc: DisasmEditorService) {}

  ngAfterViewInit(): void {
    // The disassembly editor uses direct DOM manipulation for the instruction
    // grid and sets up setInterval timers for live refresh.  Running these
    // inside Angular's zone would trigger change-detection on every timer tick
    // and keyboard event, causing perceptible jank when live refresh fires at
    // 250 ms intervals.
    //
    // VS Code message handling has been moved to DisasmEditorService, which runs
    // in-zone via VsCodeBridgeService.  A single CD cycle per incoming message is
    // acceptable.  The timer and keyboard handlers (high-frequency) remain outside
    // the zone.  HexViewComponent runs in-zone and receives data through Angular
    // @Input() bindings updated via NgZone.run().
    this.zone.runOutsideAngular(() => this.initEditorLogic());
  }

  private initEditorLogic(): void {
    const svc    = this.svc;
    const vscode = getVsCodeApi();

    const SCAN_TIMEOUT_MS = 120_000;

    // ── DOM refs ──────────────────────────────────────────────────────────────
    // Note: hex-header and hex-container are rendered by <app-hex-view> and
    // are not accessed directly from this component.
    const grid           = document.getElementById('grid')!;
    const searchPanel    = document.getElementById('search-panel')!;
    const searchInput    = document.getElementById('search-input') as HTMLInputElement;
    const searchClear    = document.getElementById('search-clear')!;
    const searchInfo     = document.getElementById('search-info')!;
    const closeSearch    = document.getElementById('close-search')!;
    const prevMatch      = document.getElementById('prev-match') as HTMLButtonElement;
    const nextMatch      = document.getElementById('next-match') as HTMLButtonElement;
    const gotoPanel      = document.getElementById('goto-panel')!;
    const gotoInput      = document.getElementById('goto-input') as HTMLInputElement;
    const gotoClear      = document.getElementById('goto-clear')!;
    const gotoBtn        = document.getElementById('goto-btn')!;
    const gotoInfo       = document.getElementById('goto-info')!;
    const closeGoto      = document.getElementById('close-goto')!;
    const setpcPanel     = document.getElementById('setpc-panel')!;
    const setpcInput     = document.getElementById('setpc-input') as HTMLInputElement;
    const setpcClear     = document.getElementById('setpc-clear')!;
    const setpcBtn       = document.getElementById('setpc-btn')!;
    const setpcInfo      = document.getElementById('setpc-info')!;
    const closeSetpc     = document.getElementById('close-setpc')!;
    const setpcToggleBtn = document.getElementById('setpc-toggle-btn')!;
    const headerToolbar  = document.getElementById('header-toolbar')!;

    const registersToggleBtn   = document.getElementById('registers-toggle-btn')!;
    const bottomPanelToggleBtn = document.getElementById('bottom-panel-toggle-btn')!;
    const searchToggleBtn      = document.getElementById('search-toggle-btn')!;
    const gotoToggleBtn        = document.getElementById('goto-toggle-btn')!;
    const navBackBtn           = document.getElementById('nav-back-btn') as HTMLButtonElement;

    const bottomPanel    = document.getElementById('bottom-panel')!;
    const debugPanel     = document.getElementById('debug-panel')!;
    const regContent     = document.getElementById('registers-content')!;
    const stackContent   = document.getElementById('stack-content')!;

    // Symbols panel
    const symbolsPanel        = document.getElementById('symbols-panel')!;
    const symbolsToggleBtn    = document.getElementById('symbols-toggle-btn')!;
    const symbolsRefreshBtn   = document.getElementById('symbols-refresh-btn')!;
    const closeSymbolsBtn     = document.getElementById('close-symbols-btn')!;
    const symNewAddr          = document.getElementById('sym-new-addr') as HTMLInputElement;
    const symNewName          = document.getElementById('sym-new-name') as HTMLInputElement;
    const symNewComment       = document.getElementById('sym-new-comment') as HTMLInputElement;
    const symAddBtn           = document.getElementById('sym-add-btn')!;
    const symLabelsList       = document.getElementById('sym-labels-list')!;
    const symStringsList      = document.getElementById('sym-strings-list')!;
    const symFunctionsList    = document.getElementById('sym-functions-list')!;
    const symPointersList     = document.getElementById('sym-pointers-list')!;
    const symFloatsList       = document.getElementById('sym-floats-list')!;
    const symIntegersList     = document.getElementById('sym-integers-list')!;
    const symStringsStatus    = document.getElementById('sym-strings-status')!;
    const symFunctionsStatus  = document.getElementById('sym-functions-status')!;
    const symPointersStatus   = document.getElementById('sym-pointers-status')!;
    const symFloatsStatus     = document.getElementById('sym-floats-status')!;
    const symIntegersStatus   = document.getElementById('sym-integers-status')!;
    const symScanStringsBtn   = document.getElementById('sym-scan-strings-btn') as HTMLButtonElement;
    const symScanFunctionsBtn = document.getElementById('sym-scan-functions-btn') as HTMLButtonElement;
    const symScanPointersBtn  = document.getElementById('sym-scan-pointers-btn') as HTMLButtonElement;
    const symScanFloatsBtn    = document.getElementById('sym-scan-floats-btn') as HTMLButtonElement;
    const symScanIntegersBtn  = document.getElementById('sym-scan-integers-btn') as HTMLButtonElement;

    // Edit overlay
    const editOverlay          = document.getElementById('edit-overlay')!;
    const editOverlayTitle     = document.getElementById('edit-overlay-title')!;
    const editOverlayInstrIn   = document.getElementById('edit-overlay-instr-in')! as HTMLInputElement;
    const editOverlayBytesIn   = document.getElementById('edit-overlay-bytes-in')! as HTMLInputElement;
    const editOverlayDisasmPreview = document.getElementById('edit-overlay-disasm-preview')!;
    const editOverlayLabelIn   = document.getElementById('edit-overlay-label-input') as HTMLInputElement;
    const editOverlayCommentIn = document.getElementById('edit-overlay-comment-input') as HTMLInputElement;
    const editOverlaySave      = document.getElementById('edit-overlay-save')!;
    const editOverlayCancel    = document.getElementById('edit-overlay-cancel')!;
    const editOverlayClose     = document.getElementById('edit-overlay-close')!;
    const editOverlayHistoryList = document.getElementById('edit-overlay-history-list')!;

    // Tab switching in the edit overlay
    editOverlay.querySelectorAll('.edit-overlay-tab').forEach(btn => {
        btn.addEventListener('click', () => {
            const tab = (btn as HTMLElement).dataset['tab'];
            editOverlay.querySelectorAll('.edit-overlay-tab').forEach(b => b.classList.remove('active'));
            editOverlay.querySelectorAll('.edit-overlay-tab-content').forEach(c => c.classList.remove('active'));
            btn.classList.add('active');
            document.getElementById('edit-overlay-tab-' + tab)?.classList.add('active');
        });
    });

    let _editOverlayAddr = 0;
    let _editOverlayAddrHex = '';
    let _editOverlayOrigBytes: string = '';

    // ── State reference ───────────────────────────────────────────────────────
    // Alias the service state so all existing `state.xxx` references remain
    // unchanged.  The service owns and initialises the state object.
    const state = svc.state;

    let liveRefreshTimer: ReturnType<typeof setInterval> | null = null;
    let liveStatusTimer:  ReturnType<typeof setInterval> | null = null;
    let liveRefreshPending = false;
    let livePaused = false;
    // Callback set later by the bottom-panel section to update call-stack
    // and breakpoints when the emulator pauses.
    let onDebugPanelShown: ((regs: RegisterData, stackBytes: number[], stackBase: number) => void) | null = null;

    const clamp = (val: number, min: number, max: number) => Math.max(min, Math.min(val, max));
    const getPageStart = (index: number) => Math.floor(index / state.pageSize) * state.pageSize;
    const getLocalIndex = (index: number) => index - getPageStart(index);
    // Returns the 0-based index of the current PC within the page that starts at
    // `pageStart`, or -1 if the PC is not on that page (or not set).
    const getPcLocalIndex = (pageStart: number): number => {
      if (state.pcAddress === undefined) { return -1; }
      const physPc    = toPhysAddr(state.pcAddress);
      const physBase  = toPhysAddr(state.baseAddress);
      const pcInstrIdx = Math.floor((physPc - physBase) / 4);
      if (pcInstrIdx < 0 || pcInstrIdx >= state.totalInstructions) { return -1; }
      if (getPageStart(pcInstrIdx) !== pageStart) { return -1; }
      return pcInstrIdx - pageStart;
    };

    // ── Navigation history ────────────────────────────────────────────────────
    const navHistory: number[] = [];
    const MAX_NAV_HISTORY = 100;

    function pushNavHistory(): void {
      navHistory.push(state.cursor);
      if (navHistory.length > MAX_NAV_HISTORY) { navHistory.shift(); }
      navBackBtn.disabled = false;
    }

    function navigateBack(): void {
      const prev = navHistory.pop();
      if (prev === undefined) { return; }
      state.cursor = prev;
      navBackBtn.disabled = navHistory.length === 0;
      const pageReady = checkAndLoadPage();
      if (pageReady) { updateCursor('center'); }
      grid.focus();
    }

    // ── Hex view helper ───────────────────────────────────────────────────────
    // Updates the <app-hex-view> @Input() bindings from outside-zone code.
    // Calling zone.run() triggers a single Angular CD cycle which propagates
    // the new values to HexViewComponent.ngOnChanges() for re-rendering.
    const updateHexView = (data: number[], offset: number, highlightAddr = -1, highlightLength = 4) => {
      this.zone.run(() => {
        this.hexViewData          = data;
        this.hexViewOffset        = offset;
        this.hexViewHighlightAddr = highlightAddr;
        this.hexViewHighlightLength = highlightLength;
      });
    };

    // ── Placeholder data ──────────────────────────────────────────────────────
    // Pre-populate both panels with fake data that exactly mimics the format the
    // extension host sends.  This makes the view look correct at launch (before
    // the emulator connects) and ensures cursor navigation + byte highlighting
    // work with the placeholder.  Real data from 'init'/'disasmPage'/'hexview'
    // messages seamlessly replaces the placeholder when the emulator connects.
    //
    // Constants mirror the provider:
    //   HEX_VIEW_SIZE   = 0x30 (48 bytes = 3 rows)
    //   PAGE_SIZE       = 1000 rows
    //   BASE_ADDRESS    = 0x00000000
    {
      const PLACEHOLDER_HEX_SIZE   = 0x30;  // matches DisasmEditorProvider.HEX_VIEW_SIZE
      const PLACEHOLDER_PAGE_SIZE  = 1000;  // matches DisasmEditorProvider.PAGE_SIZE
      const PLACEHOLDER_BASE       = 0x00000000;
      const PLACEHOLDER_INSTR_SIZE = 4;     // 4 bytes per MIPS instruction

      state.hexData   = new Array(PLACEHOLDER_HEX_SIZE).fill(0);
      state.hexOffset = PLACEHOLDER_BASE;
      state.totalInstructions = PLACEHOLDER_PAGE_SIZE;
      state.baseAddress = PLACEHOLDER_BASE;

      const placeholderRows: DisasmRow[] = [];
      for (let i = 0; i < PLACEHOLDER_PAGE_SIZE; i++) {
        placeholderRows.push({
          addr:    (PLACEHOLDER_BASE + i * PLACEHOLDER_INSTR_SIZE).toString(16).toUpperCase().padStart(8, '0'),
          bytes:   '00000000',
          label:   '',
          op:      'nop',
          args:    '',
          comment: '',
        });
      }
      state.disasmRows = placeholderRows;
      state.loadedPages.set(0, placeholderRows);

      updateHexView(state.hexData, state.hexOffset);
      renderDisassembly();
    }

    // ── Service event subscriptions ───────────────────────────────────────────
    // The DisasmEditorService handles all VS Code message parsing and state
    // updates.  We subscribe here to react with DOM rendering and timer control.
    // Subscriptions are set up before sending 'ready' so no messages are lost.
    svc.initReceived$.subscribe(({ isLive, regionLabel }) => {
      liveRefreshPending = false;
      navHistory.length = 0;
      navBackBtn.disabled = true;
      if (regionLabel) {
        const rl = document.getElementById('sym-region-label');
        if (rl) { rl.textContent = `— ${regionLabel}`; }
      }
      if (isLive) {
        headerToolbar.classList.add('live-active');
        startLiveRefresh();
        // Pre-populate the register panel with zeroed placeholder data so
        // that when the user opens the panel before the first breakpoint is
        // hit they see all register fields rather than a blank panel.
        const ph = makePlaceholderRegs();
        renderGprPanel(ph, [], 0);
        renderCop0Panel(ph);
        renderFprPanel(ph);
        renderFcrPanel(ph);
        renderVu0fPanel(ph);
        renderVu0iPanel(ph);
        renderGsPanel(ph);
        renderStackPanel(ph, [], 0);
      }
    });

    svc.pageLoaded$.subscribe(({ startIndex }) => {
      liveRefreshPending = false;
      if (startIndex === getPageStart(state.cursor)) {
        // Grid is rebuilt with fresh data — centre the cursor so the view
        // doesn't abruptly jump to the top of the newly loaded page.
        renderDisassembly('center');
      }
    });

    svc.hexUpdated$.subscribe(event => {
      // Prefer the current cursor position for the highlight so that a stale
      // address from an in-flight request does not flash on screen when the
      // cursor has already moved on.
      const localIndex = getLocalIndex(state.cursor);
      const curRow     = state.disasmRows[localIndex];
      let hiAddr   = event.highlightAddr   ?? -1;
      let hiLength = event.highlightLength ?? 4;
      if (curRow) {
        const curAddr   = parseInt(curRow.addr, 16);
        const curBytes  = curRow.bytes?.trim() || '';
        const curLength = curBytes.length > 0 ? Math.floor(curBytes.length / 2) : 4;
        if (curAddr >= event.offset && curAddr + curLength <= event.offset + event.data.length) {
          hiAddr   = curAddr;
          hiLength = curLength;
        }
      }
      updateHexView(event.data, event.offset, hiAddr, hiLength);
    });

    svc.searchResultsReceived$.subscribe(matches => {
      handleSearchResults(matches);
    });

    svc.statusReceived$.subscribe(({ paused, pc, breakpointHitAddress, registers, stackBytes, stackBase }) => {
      handleStatusUpdate(paused, pc, breakpointHitAddress, registers, stackBytes, stackBase);
    });

    svc.resetReceived$.subscribe(() => {
      handleReset();
    });

    svc.symbolsReceived$.subscribe(() => {
      renderSymLabels();
      renderSymStrings();
      renderSymFunctions();
      renderSymPointers();
      renderSymFloats();
      renderSymIntegers();
    });

    svc.labelsUpdated$.subscribe(() => {
      renderSymLabels();
      state.loadedPages.delete(getPageStart(state.cursor));
      checkAndLoadPage();
    });

    svc.stringsUpdated$.subscribe(() => {
      renderSymStrings();
      if (symStringsStatus) {
        symStringsStatus.textContent = state.symStrings.length > 0 ? `${state.symStrings.length} found` : 'None found';
      }
      if (symScanStringsBtn) { symScanStringsBtn.disabled = false; }
    });

    svc.functionsUpdated$.subscribe(() => {
      renderSymFunctions();
      if (symFunctionsStatus) {
        symFunctionsStatus.textContent = state.symFunctions.length > 0 ? `${state.symFunctions.length} found` : 'None found';
      }
      if (symScanFunctionsBtn) { symScanFunctionsBtn.disabled = false; }
    });

    svc.pointersUpdated$.subscribe(() => {
      renderSymPointers();
      if (symPointersStatus) {
        symPointersStatus.textContent = state.symPointers.length > 0 ? `${state.symPointers.length} found` : 'None found';
      }
      if (symScanPointersBtn) { symScanPointersBtn.disabled = false; }
    });

    svc.floatsUpdated$.subscribe(() => {
      renderSymFloats();
      if (symFloatsStatus) {
        symFloatsStatus.textContent = state.symFloats.length > 0 ? `${state.symFloats.length} found` : 'None found';
      }
      if (symScanFloatsBtn) { symScanFloatsBtn.disabled = false; }
    });

    svc.integersUpdated$.subscribe(() => {
      renderSymIntegers();
      if (symIntegersStatus) {
        symIntegersStatus.textContent = state.symIntegers.length > 0 ? `${state.symIntegers.length} found` : 'None found';
      }
      if (symScanIntegersBtn) { symScanIntegersBtn.disabled = false; }
    });

    svc.analysisUpdated$.subscribe(() => {
      // Re-render so the updated analysis comments are visible immediately.
      renderDisassembly();
    });

    // Tell the extension host we are ready; it will (re-)send init + first page.
    // VsCodeBridgeService buffers any messages that arrive before subscriptions
    // are set up, so no messages are lost.
    vscode.postMessage({ type: 'ready' });

    function openEditOverlay(address: number, addressHex: string, op: string, args: string, bytes: string, currentLabel: string, currentComment: string) {
      _editOverlayAddr = address;
      _editOverlayAddrHex = addressHex;
      editOverlayTitle.textContent = 'Edit Address — 0x' + addressHex.toUpperCase();
      const instrStr = (op || '') + (args ? '  ' + args : '');
      editOverlayInstrIn.value = instrStr;
      // Populate the bytes field (strip spaces so it's a clean hex string)
      const rawBytes = (bytes || '').replace(/\s+/g, '');
      _editOverlayOrigBytes = rawBytes;
      editOverlayBytesIn.value = rawBytes;
      editOverlayDisasmPreview.textContent = '';
      editOverlayLabelIn.value = currentLabel || '';
      editOverlayCommentIn.value = currentComment || '';
      // Switch to the Edit tab by default
      editOverlay.querySelectorAll('.edit-overlay-tab').forEach(b => b.classList.remove('active'));
      editOverlay.querySelectorAll('.edit-overlay-tab-content').forEach(c => c.classList.remove('active'));
      editOverlay.querySelector('.edit-overlay-tab[data-tab="edit"]')?.classList.add('active');
      document.getElementById('edit-overlay-tab-edit')?.classList.add('active');
      // Reset history tab to loading state and request history
      editOverlayHistoryList.innerHTML = '<div class="edit-overlay-history-empty">Loading history...</div>';
      vscode.postMessage({ type: 'getAddressHistory', address });
      editOverlay.classList.add('visible');
      editOverlayInstrIn.focus();
      editOverlayInstrIn.select();
    }

    function closeEditOverlay() {
      editOverlay.classList.remove('visible');
    }

    function saveEditOverlay() {
      const newLabel   = editOverlayLabelIn.value.trim();
      const newComment = editOverlayCommentIn.value.trim();
      const newBytes   = editOverlayBytesIn.value.replace(/\s+/g, '').toUpperCase();
      // If bytes were changed, patch the instruction bytes first, then save the label.
      const origBytes = (_editOverlayOrigBytes || '').toUpperCase();
      if (newBytes && newBytes !== origBytes && /^[0-9A-F]+$/.test(newBytes) && newBytes.length % 2 === 0) {
        vscode.postMessage({ type: 'patchBytes', address: _editOverlayAddr, hexBytes: newBytes });
      }
      vscode.postMessage({ type: 'saveLabel', address: _editOverlayAddr, addressHex: _editOverlayAddrHex, newLabel, newComment });
      closeEditOverlay();
    }

    editOverlaySave.addEventListener('click', saveEditOverlay);
    editOverlayCancel.addEventListener('click', closeEditOverlay);
    editOverlayClose.addEventListener('click', closeEditOverlay);

    editOverlayInstrIn.addEventListener('input', () => {
      const instr = editOverlayInstrIn.value.trim();
      if (instr) {
        vscode.postMessage({ type: 'assembleInstr', instruction: instr, address: _editOverlayAddr });
      } else {
        editOverlayDisasmPreview.textContent = '';
      }
    });
    editOverlayInstrIn.addEventListener('keydown', (e: KeyboardEvent) => {
      if (e.key === 'Enter') { editOverlayBytesIn.focus(); editOverlayBytesIn.select(); }
      if (e.key === 'Escape') { closeEditOverlay(); }
    });
    editOverlayBytesIn.addEventListener('input', () => {
      const hex = editOverlayBytesIn.value.replace(/\s+/g, '');
      if (hex.length === 8 && /^[0-9a-fA-F]{8}$/.test(hex)) {
        vscode.postMessage({ type: 'disasmBytes', hexBytes: hex, address: _editOverlayAddr });
      } else {
        editOverlayDisasmPreview.textContent = '';
      }
    });
    editOverlayBytesIn.addEventListener('keydown', (e: KeyboardEvent) => {
      if (e.key === 'Enter') { editOverlayLabelIn.focus(); editOverlayLabelIn.select(); }
      if (e.key === 'Escape') { closeEditOverlay(); }
    });
    editOverlayLabelIn.addEventListener('keydown', (e: KeyboardEvent) => {
      if (e.key === 'Enter') { editOverlayCommentIn.focus(); editOverlayCommentIn.select(); }
      if (e.key === 'Escape') { closeEditOverlay(); }
    });
    editOverlayCommentIn.addEventListener('keydown', (e: KeyboardEvent) => {
      if (e.key === 'Enter') { saveEditOverlay(); }
      if (e.key === 'Escape') { closeEditOverlay(); }
    });
    window.addEventListener('message', (e: MessageEvent) => {
      const data = e.data as Record<string, unknown>;
      if (data?.['type'] === 'disasmBytesResult') {
        const preview = (data['preview'] as string) ?? '';
        editOverlayDisasmPreview.textContent = preview;
        // Update instruction field when bytes are edited (avoid feedback loop)
        if (document.activeElement !== editOverlayInstrIn && preview && preview !== '(invalid)') {
          editOverlayInstrIn.value = preview;
        }
      } else if (data?.['type'] === 'assembleInstrResult') {
        const hexBytes = data['hexBytes'] as string | undefined;
        if (hexBytes) {
          // Update bytes field when instruction is edited (avoid feedback loop)
          if (document.activeElement !== editOverlayBytesIn) {
            editOverlayBytesIn.value = hexBytes;
          }
          editOverlayDisasmPreview.textContent = '';
        } else {
          editOverlayDisasmPreview.textContent = '(assembly error)';
        }
      } else if (data?.['type'] === 'addressHistoryResult') {
        const history = data['history'] as Array<{ name: string; date: string; value: string }> | undefined;
        if (!history || history.length === 0) {
          editOverlayHistoryList.innerHTML = '<div class="edit-overlay-history-empty">No snapshot history for this address.</div>';
        } else {
          let html = '';
          for (const h of history) {
            html += `<div class="edit-overlay-history-row">`;
            html += `<span class="edit-overlay-history-value">${h.value}</span>`;
            html += `<span class="edit-overlay-history-name">${h.name}</span>`;
            html += `<span class="edit-overlay-history-date">${h.date}</span>`;
            html += `</div>`;
          }
          editOverlayHistoryList.innerHTML = html;
        }
      }
    });
    editOverlay.addEventListener('click', (e: MouseEvent) => {
      if (e.target === editOverlay) { closeEditOverlay(); }
    });

    // ── Address helpers ───────────────────────────────────────────────────────
    function parseAddress(input: string): number | null {
      const cleaned = input.trim().toLowerCase().replace(/^0x/, '');
      if (!/^[0-9a-f]+$/.test(cleaned)) { return null; }
      return parseInt(cleaned, 16);
    }

    /** Translate any virtual/logical EE address to its physical equivalent.
     *  KSEG0/KSEG1 (0x80000000–0xBFFFFFFF): strip the top 3 bits.
     *  EE RAM uncached mirror (0x20100000–0x21FFFFFF): subtract 0x20000000.
     *  EE RAM uncached+accelerated (0x30100000–0x31FFFFFF): subtract 0x30000000.
     *  All other addresses (physical or special like scratchpad) pass through. */
    function toPhysAddr(va: number): number {
      const v = va >>> 0;
      if (v >= 0x80000000 && v <= 0xBFFFFFFF) return ((v & 0x1FFFFFFF) >>> 0);
      if (v >= 0x20100000 && v <= 0x21FFFFFF) return ((v - 0x20000000) >>> 0);
      if (v >= 0x30100000 && v <= 0x31FFFFFF) return ((v - 0x30000000) >>> 0);
      return v;
    }

    // ── Search ────────────────────────────────────────────────────────────────
    function toggleSearch() {
      state.searchActive = !state.searchActive;
      searchPanel.classList.toggle('visible', state.searchActive);
      searchToggleBtn.classList.toggle('active', state.searchActive);
      if (state.searchActive) {
        searchInput.focus();
        searchInput.select();
      } else {
        clearSearchMatches();
      }
    }

    function parseBytePattern(pattern: string): number[] | null {
      const cleaned = pattern.replace(/0x/gi, '').replace(/[,\s]+/g, ' ').trim();
      if (!cleaned) { return null; }
      const bytes = cleaned.split(/\s+/);
      const result: number[] = [];
      for (const byte of bytes) {
        if (!/^[0-9a-f]{1,2}$/i.test(byte)) { return null; }
        result.push(parseInt(byte, 16));
      }
      return result.length > 0 ? result : null;
    }

    function performSearch() {
      const query = searchInput.value.trim();
      const mode = state.searchMode;
      if (!query) { clearSearchMatches(); return; }
      if (mode === 'bytes') {
        const pattern = parseBytePattern(query);
        if (!pattern) {
          searchInput.classList.add('error');
          searchInfo.textContent = 'Invalid byte pattern';
          state.searchMatches = [];
          state.currentMatch = -1;
          updateSearchInfo();
          return;
        }
      }
      searchInput.classList.remove('error');
      state.searchQuery = query;
      vscode.postMessage({ type: 'search', query, mode });
      searchInfo.textContent = 'Searching...';
    }

    function handleSearchResults(matches: SearchMatch[]) {
      state.searchMatches = matches || [];
      if (state.searchMatches.length > 0) {
        state.currentMatch = 0;
        jumpToMatch(0);
      } else {
        state.currentMatch = -1;
        updateSearchInfo();
        renderDisassembly();
      }
    }

    function jumpToMatch(matchIndex: number) {
      if (matchIndex < 0 || matchIndex >= state.searchMatches.length) { return; }
      state.currentMatch = matchIndex;
      const match = state.searchMatches[matchIndex];
      state.cursor = match.instructionIndex;
      const pageReady = checkAndLoadPage();
      if (pageReady) { updateCursor(); }
      updateSearchInfo();
    }

    function nextSearchMatch() {
      if (state.searchMatches.length === 0) { return; }
      jumpToMatch((state.currentMatch + 1) % state.searchMatches.length);
    }

    function prevSearchMatch() {
      if (state.searchMatches.length === 0) { return; }
      jumpToMatch((state.currentMatch - 1 + state.searchMatches.length) % state.searchMatches.length);
    }

    function clearSearchMatches() {
      state.searchMatches = [];
      state.currentMatch = -1;
      state.searchQuery = '';
      searchInput.value = '';
      searchInput.classList.remove('error');
      updateSearchInfo();
      renderDisassembly();
    }

    function updateSearchInfo() {
      if (state.searchMatches.length === 0) {
        searchInfo.textContent = state.searchQuery ? 'No results' : '';
        prevMatch.disabled = true;
        nextMatch.disabled = true;
      } else {
        searchInfo.textContent = `${state.currentMatch + 1} of ${state.searchMatches.length}`;
        prevMatch.disabled = false;
        nextMatch.disabled = false;
      }
    }

    function isRowSearchMatch(globalIndex: number): boolean {
      return state.searchMatches.some(m => m.instructionIndex === globalIndex);
    }

    function isRowCurrentMatch(globalIndex: number): boolean {
      if (state.currentMatch === -1) { return false; }
      const match = state.searchMatches[state.currentMatch];
      return !!(match && match.instructionIndex === globalIndex);
    }

    // ── Mode buttons ──────────────────────────────────────────────────────────
    document.querySelectorAll('.mode-btn').forEach(btn => {
      btn.addEventListener('click', () => {
        document.querySelectorAll('.mode-btn').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        state.searchMode = (btn as HTMLElement).dataset['mode'] ?? 'text';
        if (state.searchMode === 'bytes') {
          searchInput.placeholder = 'Byte pattern: 0x12 34 AB CD';
        } else if (state.searchMode === 'string') {
          searchInput.placeholder = 'String in binary (case-sensitive)';
        } else if (state.searchMode === 'regex') {
          searchInput.placeholder = 'Regular expression';
        } else {
          searchInput.placeholder = 'Search text';
        }
        if (searchInput.value.trim()) { performSearch(); }
      });
    });

    searchInput.addEventListener('input', () => {
      if (!searchInput.value.trim()) { clearSearchMatches(); }
    });

    searchInput.addEventListener('keydown', (e: KeyboardEvent) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        if (e.shiftKey) {
          prevSearchMatch();
        } else {
          if (searchInput.value.trim() && state.searchMatches.length === 0) {
            performSearch();
          } else {
            nextSearchMatch();
          }
        }
      } else if (e.key === 'Escape') {
        toggleSearch();
        grid.focus();
      }
    });

    searchClear.addEventListener('click', () => { clearSearchMatches(); searchInput.focus(); });
    prevMatch.addEventListener('click', prevSearchMatch);
    nextMatch.addEventListener('click', nextSearchMatch);
    closeSearch.addEventListener('click', () => { toggleSearch(); grid.focus(); });

    document.addEventListener('keydown', (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'f') {
        e.preventDefault();
        toggleSearch();
      }
      if (e.key === 'F3' && !e.shiftKey && state.searchActive) {
        e.preventDefault();
        nextSearchMatch();
      }
      if (e.key === 'F3' && e.shiftKey && state.searchActive) {
        e.preventDefault();
        prevSearchMatch();
      }
    });

    // ── Goto ─────────────────────────────────────────────────────────────────
    function toggleGoto() {
      state.gotoActive = !state.gotoActive;
      gotoPanel.classList.toggle('visible', state.gotoActive);
      gotoToggleBtn.classList.toggle('active', state.gotoActive);
      if (state.gotoActive) {
        gotoInput.focus();
        gotoInput.select();
      } else {
        gotoInput.value = '';
        gotoInfo.textContent = '';
      }
    }

    function goToAddress() {
      const value = gotoInput.value;
      const addr = parseAddress(value);
      if (addr === null) {
        gotoInput.classList.add('error');
        gotoInfo.textContent = 'Invalid hex address';
        return;
      }
      gotoInput.classList.remove('error');
      const physAddr   = toPhysAddr(addr);
      const physBase   = toPhysAddr(state.baseAddress);
      const instrIndex = Math.floor((physAddr - physBase) / 4);
      if (instrIndex < 0 || instrIndex >= state.totalInstructions) {
        // Address is not in the current region — ask the extension host to
        // reveal the panel that owns this address and navigate there.
        vscode.postMessage({ type: 'gotoRegion', address: addr });
        gotoInfo.textContent = `Navigating to 0x${addr.toString(16).toUpperCase()}...`;
        return;
      }
      pushNavHistory();
      state.cursor = instrIndex;
      state.searchActive = false;
      searchPanel.classList.remove('visible');
      searchToggleBtn.classList.remove('active');
      const pageReady = checkAndLoadPage();
      if (pageReady) { updateCursor(); }
      gotoInfo.textContent = `Moved to 0x${addr.toString(16).toUpperCase()}`;
    }

    gotoBtn.addEventListener('click', goToAddress);
    gotoInput.addEventListener('keydown', (e: KeyboardEvent) => {
      if (e.key === 'Enter') { e.preventDefault(); goToAddress(); }
      else if (e.key === 'Escape') { toggleGoto(); grid.focus(); }
    });
    gotoClear.addEventListener('click', () => { gotoInput.value = ''; gotoInfo.textContent = ''; gotoInput.focus(); });
    closeGoto.addEventListener('click', () => { toggleGoto(); grid.focus(); });
    navBackBtn.addEventListener('click', () => navigateBack());

    // Broadcast every keystroke to the other region panels so their goto
    // input stays in sync with this one.  A guard flag prevents the echo
    // message (from the extension host back to this panel) from looping.
    let syncingGotoInput = false;
    gotoInput.addEventListener('input', () => {
      if (syncingGotoInput) { return; }
      vscode.postMessage({ type: 'syncGotoInput', value: gotoInput.value });
    });

    // ── Cross-region goto / input-sync service subscriptions ─────────────────
    svc.syncGotoInputReceived$.subscribe(({ value }) => {
      syncingGotoInput = true;
      gotoInput.value = value;
      syncingGotoInput = false;
      if (state.gotoActive) { gotoInfo.textContent = ''; }
    });

    svc.gotoAddressReceived$.subscribe(({ address }) => {
      // The extension host has routed navigation to this panel because this
      // region owns the address.  Navigate directly — do NOT call goToAddress()
      // here to avoid sending a second gotoRegion message if the arithmetic
      // produces an unexpected result.
      const physAddr   = toPhysAddr(address);
      const physBase   = toPhysAddr(state.baseAddress);
      const instrIndex = Math.floor((physAddr - physBase) / 4);
      if (instrIndex < 0 || instrIndex >= state.totalInstructions) { return; }

      pushNavHistory();
      // Show the goto panel with the destination address and a confirmation.
      gotoInput.value = `0x${address.toString(16).toUpperCase()}`;
      if (!state.gotoActive) { toggleGoto(); }
      gotoInfo.textContent = `Moved to 0x${address.toString(16).toUpperCase()}`;
      gotoInput.classList.remove('error');

      state.cursor = instrIndex;
      state.searchActive = false;
      searchPanel.classList.remove('visible');
      searchToggleBtn.classList.remove('active');
      const pageReady = checkAndLoadPage();
      if (pageReady) { updateCursor('center'); }
    });

    svc.gotoRegionNotFoundReceived$.subscribe(() => {
      gotoInfo.textContent = 'Address out of range';
    });

    // ── Set PC ────────────────────────────────────────────────────────────────
    function toggleSetPc() {
      const active = setpcPanel.classList.toggle('visible');
      setpcToggleBtn.classList.toggle('active', active);
      if (active) {
        setpcInput.focus();
        setpcInput.select();
      } else {
        setpcInput.value = '';
        setpcInfo.textContent = '';
      }
    }

    function doSetPC() {
      const addr = parseAddress(setpcInput.value);
      if (addr === null) {
        setpcInput.classList.add('error');
        setpcInfo.textContent = 'Invalid hex address';
        return;
      }
      setpcInput.classList.remove('error');
      setpcInfo.textContent = `Setting PC to 0x${addr.toString(16).toUpperCase()}...`;
      // Ask the provider to pause, update PC, broadcast status, and navigate.
      vscode.postMessage({ type: 'setPC', address: addr });
    }

    setpcBtn.addEventListener('click', doSetPC);
    setpcInput.addEventListener('keydown', (e: KeyboardEvent) => {
      if (e.key === 'Enter') { e.preventDefault(); doSetPC(); }
      else if (e.key === 'Escape') { toggleSetPc(); grid.focus(); }
    });
    setpcClear.addEventListener('click', () => { setpcInput.value = ''; setpcInfo.textContent = ''; setpcInput.focus(); });
    closeSetpc.addEventListener('click', () => { toggleSetPc(); grid.focus(); });
    setpcToggleBtn.addEventListener('click', toggleSetPc);

    // ── Disassembly grid ──────────────────────────────────────────────────────
    // scrollBlock controls where the cursor row appears in the viewport after
    // a scrollIntoView call:
    //   'center'  – used when the grid is fully rebuilt (page change / reset) so
    //               the cursor appears in the middle of the visible area instead
    //               of jumping to the top of the new page data.
    //   'nearest' – used for single-step keyboard navigation; only scrolls if
    //               the cursor row is already out of view.
    function renderDisassembly(scrollBlock: ScrollLogicalPosition = 'nearest') {
      grid.innerHTML = '';
      const pageStart = getPageStart(state.cursor);
      const localCursor = getLocalIndex(state.cursor);
      // Pre-compute the local index of the PC so each row can add current-pc class.
      const pcLocalIndex = getPcLocalIndex(pageStart);
      state.disasmRows.forEach((rowData, i) => {
        const globalIndex = pageStart + i;
        const row = document.createElement('div');
        row.className = 'row';
        row.dataset['index'] = String(i);
        row.dataset['globalIndex'] = String(globalIndex);
        if (i === localCursor) { row.classList.add('active'); }
        if (i === pcLocalIndex) { row.classList.add('current-pc'); }
        if (isRowSearchMatch(globalIndex)) { row.classList.add('search-match'); }
        if (isRowCurrentMatch(globalIndex)) { row.classList.add('search-current'); }
        const rowAddr = parseInt(rowData.addr, 16);
        if (state.breakpoints.has(rowAddr)) {
          row.classList.add('breakpoint', 'breakpoint-active');
        }
        if (state.breakpointHitAddress !== undefined) {
          const hitPhys = toPhysAddr(state.breakpointHitAddress);
          const rowPhys = toPhysAddr(rowAddr);
          if (hitPhys === rowPhys) { row.classList.add('breakpoint-hit'); }
        }
        row.innerHTML = buildDisasmRowHtml(rowData, globalIndex);
        row.onclick = () => { state.cursor = globalIndex; updateCursor(); };
        row.ondblclick = () => {
          state.cursor = globalIndex;
          updateCursor();
          openEditOverlay(
            parseInt(rowData.addr, 16), rowData.addr,
            rowData.op || '', rowData.args || '',
            rowData.bytes || '', rowData.label || '', rowData.comment || ''
          );
        };
        grid.appendChild(row);
      });
      updateCursor(scrollBlock);
    }

    function highlightSearchInText(text: string, globalIndex: number, field: string): string {
      if (!state.searchQuery || !text) { return text; }
      const match = state.searchMatches.find(m => m.instructionIndex === globalIndex && m.field === field);
      if (!match) { return text; }
      const isCurrent = isRowCurrentMatch(globalIndex);
      const highlightClass = isCurrent ? 'search-current-highlight' : 'search-match-highlight';
      const before = text.substring(0, match.start);
      const highlighted = text.substring(match.start, match.start + match.length);
      const after = text.substring(match.start + match.length);
      return `${before}<span class="${highlightClass}">${highlighted}</span>${after}`;
    }

    function buildDisasmRowHtml(r: DisasmRow, globalIndex: number): string {
      const branchMatch = r.args.match(/^(.*\$[0-9A-F]{8})\s*(\([+-]\d+[▼▲]\))(.*)$/);
      let args = branchMatch ? `${branchMatch[1]} <span style="color:#6a9955">${branchMatch[2]}</span>${branchMatch[3]}` : r.args;
      const addr    = highlightSearchInText(r.addr, globalIndex, 'addr');
      const bytes   = highlightSearchInText(r.bytes, globalIndex, 'bytes');
      const label   = highlightSearchInText(r.label || '', globalIndex, 'label');
      const op      = highlightSearchInText(r.op, globalIndex, 'op');
      args          = highlightSearchInText(args, globalIndex, 'args');
      const comment = highlightSearchInText(r.comment || '', globalIndex, 'comment');
      return `<span class="addr">${addr}</span><span class="bytes">${bytes}</span><span class="label">${label}</span><span class="op">${op}</span><span class="args">${args}</span><span class="comment">${comment}</span>`;
    }

    // ── Grid keyboard navigation ──────────────────────────────────────────────
    grid.addEventListener('keydown', (e: KeyboardEvent) => {
      if (state.searchActive && document.activeElement === searchInput) { return; }
      if (e.key === 'ArrowRight') {
        e.preventDefault();
        navigateToCurrentRowTarget();
        return;
      }
      if (e.key === 'ArrowLeft') {
        e.preventDefault();
        navigateBack();
        return;
      }
      if (e.key === 'F9') {
        e.preventDefault();
        toggleBreakpointAtCursor();
        return;
      }
      const deltas: Record<string, number> = { ArrowDown: 1, ArrowUp: -1, PageDown: 50, PageUp: -50 };
      const delta = deltas[e.key];
      if (!delta) { return; }
      e.preventDefault();
      state.cursor = clamp(state.cursor + delta, 0, state.totalInstructions - 1);
      const pageReady = checkAndLoadPage();
      if (pageReady) { updateCursor(); }
    });

    function navigateToCurrentRowTarget() {
      const localIndex = getLocalIndex(state.cursor);
      const row = state.disasmRows[localIndex];
      if (!row) { return; }
      const branchMatch = row.args.match(/\$([0-9A-F]{8})/i);
      if (branchMatch) { navigateToAddress(parseInt(branchMatch[1], 16)); return; }
      const jumpMatch = row.args.match(/0x([0-9A-F]{8})/i);
      if (jumpMatch) { navigateToAddress(parseInt(jumpMatch[1], 16)); return; }
      if (row.comment) {
        const commentMatch = row.comment.match(/0x([0-9A-F]{8})/i) || row.comment.match(/\b([0-9A-F]{8})\b/i);
        if (commentMatch) { navigateToAddress(parseInt(commentMatch[1], 16)); }
      }
    }

    function navigateToAddress(addr: number) {
      const physAddr   = toPhysAddr(addr);
      const physBase   = toPhysAddr(state.baseAddress);
      const instrIndex = Math.floor((physAddr - physBase) / 4);
      if (instrIndex < 0 || instrIndex >= state.totalInstructions) { return; }
      pushNavHistory();
      state.cursor = instrIndex;
      const pageReady = checkAndLoadPage();
      if (pageReady) { updateCursor(); }
    }

    function toggleBreakpointAtCursor() {
      const localIndex = getLocalIndex(state.cursor);
      const row = state.disasmRows[localIndex];
      if (!row) { return; }
      const addr = parseInt(row.addr, 16);
      if (state.breakpoints.has(addr)) {
        state.breakpoints.delete(addr);
        vscode.postMessage({ type: 'clearBreakpoint', address: addr });
      } else {
        state.breakpoints.add(addr);
        vscode.postMessage({ type: 'setBreakpoint', address: addr });
      }
      renderDisassembly();
    }

    function checkAndLoadPage(): boolean {
      const startIndex = getPageStart(state.cursor);
      if (!state.loadedPages.has(startIndex)) {
        vscode.postMessage({ type: 'requestPage', startIndex });
        return false;
      } else {
        const cachedRows = state.loadedPages.get(startIndex)!;
        if (state.disasmRows !== cachedRows) {
          state.disasmRows = cachedRows;
          // Page switched from cache — centre the cursor so it doesn't jump
          // to the top of the newly displayed page.
          renderDisassembly('center');
        }
        return true;
      }
    }

    window.addEventListener('keydown', (e: KeyboardEvent) => {
      if (e.ctrlKey && e.altKey && !e.shiftKey && e.code === 'KeyA') {
        e.preventDefault(); e.stopPropagation(); toggleGoto();
      }
      if (e.ctrlKey && e.altKey && !e.shiftKey && e.code === 'KeyS') {
        e.preventDefault(); e.stopPropagation(); toggleSymbolsPanel();
      }
      if (e.ctrlKey && e.altKey && !e.shiftKey && e.code === 'KeyR') {
        e.preventDefault(); e.stopPropagation(); toggleRegistersPanel();
      }
      if (e.ctrlKey && e.altKey && !e.shiftKey && e.code === 'KeyT') {
        e.preventDefault(); e.stopPropagation(); toggleBottomPanel();
      }
      if (e.ctrlKey && e.altKey && !e.shiftKey && e.code === 'KeyP') {
        e.preventDefault(); e.stopPropagation();
        if (state.isLive) { toggleSetPc(); }
      }
      if (e.altKey && !e.ctrlKey && !e.shiftKey && e.code === 'ArrowLeft') {
        // Only fire when no editable element is focused, so the shortcut
        // does not interfere with text input or contenteditable regions.
        const active = document.activeElement as HTMLElement | null;
        const isEditing = active && (
          active.tagName === 'INPUT' || active.tagName === 'TEXTAREA' ||
          active.tagName === 'SELECT' || active.isContentEditable
        );
        if (!isEditing) {
          e.preventDefault(); e.stopPropagation(); navigateBack();
        }
      }
    });

    // Highlights the cursor row and scrolls it into view.
    // `scrollBlock` should be 'center' after a full grid rebuild (page change)
    // so the cursor is not stranded at the top of new data; use the default
    // 'nearest' for single-step navigation to avoid jarring jumps.
    function updateCursor(scrollBlock: ScrollLogicalPosition = 'nearest') {
      const localIndex = getLocalIndex(state.cursor);
      const breakpointHitPhys = state.breakpointHitAddress !== undefined
        ? toPhysAddr(state.breakpointHitAddress)
        : undefined;
      // Compute the local index of the current PC so we can keep the PC
      // arrow visible even when the user has navigated the cursor elsewhere.
      const pcLocalIndex = getPcLocalIndex(getPageStart(state.cursor));
      Array.from(grid.children).forEach((r, i) => {
        r.classList.remove('active', 'branch-highlight', 'breakpoint-hit', 'current-pc');
        if (breakpointHitPhys !== undefined) {
          const rowData = state.disasmRows[i];
          if (rowData) {
            const rowPhys = toPhysAddr(parseInt(rowData.addr, 16));
            if (breakpointHitPhys === rowPhys) { (r as Element).classList.add('breakpoint-hit'); }
          }
        }
        if (i === pcLocalIndex) { (r as Element).classList.add('current-pc'); }
      });
      const rowEl = grid.children[localIndex] as HTMLElement | undefined;
      if (!rowEl) { return; }
      rowEl.classList.add('active');
      rowEl.scrollIntoView({ block: scrollBlock, behavior: 'instant' as ScrollBehavior });
      const row = state.disasmRows[localIndex];
      if (!row) { return; }
      handleBranchHighlight(row, localIndex);
      updateHexForRow(row);
    }

    function updateHexForRow(row: DisasmRow) {
      const addr    = parseInt(row.addr, 16);
      const byteStr = row.bytes?.trim() || '';
      const length  = byteStr.length > 0 ? Math.floor(byteStr.length / 2) : 4;
      const inView  = addr >= state.hexOffset && addr + length <= state.hexOffset + state.hexData.length;
      if (inView) {
        updateHexView(state.hexData, state.hexOffset, addr, length);
      } else {
        // Cursor moved outside the current hex window.  Request fresh data
        // from the extension host without overwriting the current display —
        // showing zeros while waiting causes a visible flicker on every live
        // refresh cycle.  The hexUpdated$ subscriber updates the view
        // atomically with real bytes once the host responds.
        vscode.postMessage({ type: 'updateHex', address: addr, length });
      }
    }

    function handleBranchHighlight(row: DisasmRow, rowIndex: number) {
      const match = row.args.match(/\$([0-9A-F]{8})\s*\(([+-]\d+)/);
      if (!match) { return; }
      const targetAddr  = parseInt(match[1], 16);
      const targetIndex = state.disasmRows.findIndex(r => parseInt(r.addr, 16) === targetAddr);
      if (targetIndex === -1) { return; }
      const start = Math.min(rowIndex, targetIndex);
      const end   = Math.max(rowIndex, targetIndex);
      for (let i = start; i <= end; i++) {
        if (i !== rowIndex) {
          (grid.children[i] as HTMLElement | undefined)?.classList.add('branch-highlight');
        }
      }
    }

    document.addEventListener('wheel', e => e.preventDefault(), { passive: false });

    // ── Live refresh ──────────────────────────────────────────────────────────
    function startLiveRefresh() {
      if (liveRefreshTimer !== null || livePaused) { return; }
      liveRefreshTimer = setInterval(doLiveRefresh, 1000);
      if (liveStatusTimer === null) {
        liveStatusTimer = setInterval(() => vscode.postMessage({ type: 'getStatus' }), 800);
      }
    }

    function stopLiveRefresh() {
      if (liveRefreshTimer !== null) { clearInterval(liveRefreshTimer); liveRefreshTimer = null; }
      if (liveStatusTimer  !== null) { clearInterval(liveStatusTimer);  liveStatusTimer  = null; }
    }

    function restartLiveRefresh() {
      stopLiveRefresh();
      if (!livePaused) { startLiveRefresh(); }
    }

    function doLiveRefresh() {
      if (liveRefreshPending) { return; }
      liveRefreshPending = true;
      const startIndex = getPageStart(state.cursor);
      state.loadedPages.delete(startIndex);
      vscode.postMessage({ type: 'requestPage', startIndex });
    }

    function setLivePausedUi(paused: boolean) {
      headerToolbar.classList.toggle('live-active', !paused);
      headerToolbar.classList.toggle('live-paused', paused);
    }

    function handleStatusUpdate(paused: boolean, pc: number, breakpointHitAddress: number | undefined, registers: RegisterData, stackBytes: number[], stackBase: number) {
      if (paused && !livePaused) {
        livePaused = true;
        stopLiveRefresh();
        setLivePausedUi(true);
      }
      if (paused) {
        state.breakpointHitAddress = breakpointHitAddress;
        // Track the actual PC separately from the cursor so the PC arrow
        // indicator stays visible even after the user navigates elsewhere.
        if (pc !== undefined && pc !== null) {
          state.pcAddress = pc;
          // If the Set PC panel is open, confirm the new PC value.
          if (setpcPanel.classList.contains('visible')) {
            setpcInfo.textContent = `PC set to 0x${pc.toString(16).toUpperCase()}`;
          }
        }
      } else {
        state.breakpointHitAddress = undefined;
        state.pcAddress = undefined;
      }
      if (paused && pc !== undefined && pc !== null) {
        const physPc    = toPhysAddr(pc);
        const physBase  = toPhysAddr(state.baseAddress);
        const instrIndex = Math.floor((physPc - physBase) / 4);
        if (instrIndex >= 0 && instrIndex < state.totalInstructions) {
          state.cursor = instrIndex;
          const pageReady = checkAndLoadPage();
          if (pageReady) { updateCursor(); }
        }
      }
      if (!paused && livePaused) {
        livePaused = false;
        state.breakpointHitAddress = undefined;
        state.pcAddress = undefined;
        setLivePausedUi(false);
        startLiveRefresh();
        hideDebugPanel();
        // Immediately clear the yellow breakpoint-hit and current-pc highlights.
        Array.from(grid.children).forEach(r => r.classList.remove('breakpoint-hit', 'current-pc'));
        return;
      }
      if (paused && registers) {
        showDebugPanel(registers, stackBytes, stackBase);
        onDebugPanelShown?.(registers, stackBytes, stackBase);
      }
    }

    function handleReset() {
      state.loadedPages.clear();
      state.disasmRows = [];
      state.breakpointHitAddress = undefined;
      navHistory.length = 0;
      navBackBtn.disabled = true;
      state.pcAddress = undefined;
      const startIndex = getPageStart(state.cursor);
      const zeroRows = generateZeroPage(startIndex);
      state.loadedPages.set(startIndex, zeroRows);
      state.disasmRows = zeroRows;
      renderDisassembly();
      _lastStackRegs  = null;
      _lastStackBytes = [];
      _lastStackBase  = 0;
      hideDebugPanel();
      livePaused = false;
      setLivePausedUi(false);
      restartLiveRefresh();
    }

    function generateZeroPage(startIndex: number): DisasmRow[] {
      const rows: DisasmRow[] = [];
      const count = Math.min(state.pageSize, state.totalInstructions - startIndex);
      for (let i = 0; i < count; i++) {
        const addr = state.baseAddress + (startIndex + i) * 4;
        rows.push({ addr: addr.toString(16).toUpperCase().padStart(8, '0'), bytes: '00000000', label: '', op: 'nop', args: '', comment: '' });
      }
      return rows;
    }

    // ── Symbols panel ─────────────────────────────────────────────────────────
    function toggleSymbolsPanel() {
      state.symbolsActive = !state.symbolsActive;
      symbolsPanel.classList.toggle('visible', state.symbolsActive);
      symbolsToggleBtn.classList.toggle('active', state.symbolsActive);
      if (state.symbolsActive) { loadSymbols(); }
    }

    // ── Registers panel toggle ────────────────────────────────────────────────
    function toggleRegistersPanel() {
      const isVisible = debugPanel.classList.contains('visible');
      if (isVisible) {
        hideDebugPanel();
      } else {
        // Always render content when opening the panel — use the most recent
        // register snapshot, or zeroed placeholder data if no breakpoint has
        // been hit yet.  When using placeholder data, pass empty stack state
        // to avoid showing stale bytes from a previous session.
        const regs       = _lastStackRegs ?? makePlaceholderRegs();
        const stackBytes = _lastStackRegs ? _lastStackBytes : [];
        const stackBase  = _lastStackRegs ? _lastStackBase  : 0;
        showDebugPanel(regs, stackBytes, stackBase);
      }
    }

    // ── Bottom panel toggle ───────────────────────────────────────────────────
    function toggleBottomPanel() {
      const isHidden = bottomPanel.classList.contains('hidden');
      bottomPanel.classList.toggle('hidden', !isHidden);
      bottomPanelToggleBtn.classList.toggle('active', isHidden);
    }

    function loadSymbols() {
      vscode.postMessage({ type: 'getSymbols' });
    }

    function symNavigate(addrHex: string) {
      const addr = parseAddress(addrHex.replace(/^0x/i, ''));
      if (addr === null) { return; }
      const physAddr   = toPhysAddr(addr);
      const physBase   = toPhysAddr(state.baseAddress);
      const instrIndex = Math.floor((physAddr - physBase) / 4);
      if (instrIndex < 0 || instrIndex >= state.totalInstructions) { return; }
      pushNavHistory();
      state.cursor = instrIndex;
      const pageReady = checkAndLoadPage();
      if (pageReady) { updateCursor(); }
    }

    function parseAddrHex(addrHex: string): number {
      return parseInt(String(addrHex).replace(/^0x/i, ''), 16);
    }

    function isAddrInCurrentRegion(addrHex: string): boolean {
      if (!state.isLive || state.totalBytes <= 0) { return true; }
      const addr = parseAddrHex(addrHex);
      if (isNaN(addr)) { return false; }
      const physAddr = toPhysAddr(addr);
      const physBase = toPhysAddr(state.baseAddress);
      return physAddr >= physBase && physAddr < physBase + state.totalBytes;
    }

    function escHtml(str: unknown): string {
      return String(str ?? '')
        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function renderSymLabels() {
      if (!symLabelsList) { return; }
      const items = (state.symLabels || []).filter(item => isAddrInCurrentRegion(item.address));
      if (items.length === 0) {
        symLabelsList.innerHTML = '<div class="sym-empty">No labels defined.</div>';
        return;
      }
      symLabelsList.innerHTML = items.map(item => {
        const escapedComment = item.comment ? escHtml(item.comment) : '';
        const comment = escapedComment ? `<span class="sym-item-comment" title="${escapedComment}">${escapedComment}</span>` : '';
        return `<div class="sym-item" data-addr="${escHtml(item.address)}">
          <span class="sym-item-addr">${escHtml(item.address)}</span>
          <span class="sym-item-name" title="${escHtml(item.label)}">${escHtml(item.label)}</span>
          ${comment}
          <button class="sym-delete-btn" data-addr="${escHtml(item.address)}" title="Delete label">×</button>
        </div>`;
      }).join('');

      symLabelsList.querySelectorAll('.sym-item').forEach(el => {
        el.addEventListener('click', (e: Event) => {
          if ((e.target as HTMLElement).classList.contains('sym-delete-btn')) { return; }
          symNavigate((el as HTMLElement).dataset['addr'] ?? '');
        });
      });
      symLabelsList.querySelectorAll('.sym-delete-btn').forEach(btn => {
        btn.addEventListener('click', (e: Event) => {
          e.stopPropagation();
          const addrStr = (btn as HTMLElement).dataset['addr'] ?? '';
          const addrNum = parseInt(addrStr.replace(/^0x/i, ''), 16);
          vscode.postMessage({ type: 'deleteLabel', address: addrNum });
        });
      });
    }

    function renderSymStrings() {
      if (!symStringsList) { return; }
      const items = (state.symStrings || []).filter(item => isAddrInCurrentRegion(item.address));
      if (items.length === 0) {
        symStringsList.innerHTML = '<div class="sym-empty">No strings found. Click "Scan for strings".</div>';
        return;
      }
      symStringsList.innerHTML = items.map(item =>
        `<div class="sym-item" data-addr="${escHtml(item.address)}">
          <span class="sym-item-addr">${escHtml(item.address)}</span>
          <span class="sym-item-value" title="${escHtml(item.value)}">${escHtml(item.value)}</span>
        </div>`
      ).join('');
      symStringsList.querySelectorAll('.sym-item').forEach(el => {
        el.addEventListener('click', () => symNavigate((el as HTMLElement).dataset['addr'] ?? ''));
      });
    }

    function renderSymFunctions() {
      if (!symFunctionsList) { return; }
      const items = (state.symFunctions || []).filter(item => isAddrInCurrentRegion(item.address));
      if (items.length === 0) {
        symFunctionsList.innerHTML = '<div class="sym-empty">No functions found. Click "Scan for functions".</div>';
        return;
      }
      symFunctionsList.innerHTML = items.map(item =>
        `<div class="sym-item" data-addr="${escHtml(item.address)}">
          <span class="sym-item-addr">${escHtml(item.address)}</span>
          <span class="sym-item-name" title="${escHtml(item.label)}">${escHtml(item.label)}</span>
        </div>`
      ).join('');
      symFunctionsList.querySelectorAll('.sym-item').forEach(el => {
        el.addEventListener('click', () => symNavigate((el as HTMLElement).dataset['addr'] ?? ''));
      });
    }

    function renderSymPointers() {
      if (!symPointersList) { return; }
      const items = (state.symPointers || []).filter(item => isAddrInCurrentRegion(item.address));
      if (items.length === 0) {
        symPointersList.innerHTML = '<div class="sym-empty">No pointers found. Click "Scan for pointers".</div>';
        return;
      }
      symPointersList.innerHTML = items.map(item =>
        `<div class="sym-item" data-addr="${escHtml(item.address)}">
          <span class="sym-item-addr">${escHtml(item.address)}</span>
          <span class="sym-item-value" title="→ ${escHtml(item.pointsTo)}">→ ${escHtml(item.pointsTo)}</span>
        </div>`
      ).join('');
      symPointersList.querySelectorAll('.sym-item').forEach(el => {
        el.addEventListener('click', () => symNavigate((el as HTMLElement).dataset['addr'] ?? ''));
      });
    }

    function renderSymFloats() {
      if (!symFloatsList) { return; }
      const items = (state.symFloats || []).filter(item => isAddrInCurrentRegion(item.address));
      if (items.length === 0) {
        symFloatsList.innerHTML = '<div class="sym-empty">No floats found. Click "Scan for floats".</div>';
        return;
      }
      symFloatsList.innerHTML = items.map(item =>
        `<div class="sym-item" data-addr="${escHtml(item.address)}">
          <span class="sym-item-addr">${escHtml(item.address)}</span>
          <span class="sym-item-value" title="${escHtml(item.raw)}">${rawBitsToFloat(parseInt(item.raw, 16) >>> 0)} (${escHtml(item.raw)})</span>
        </div>`
      ).join('');
      symFloatsList.querySelectorAll('.sym-item').forEach(el => {
        el.addEventListener('click', () => symNavigate((el as HTMLElement).dataset['addr'] ?? ''));
      });
    }

    function renderSymIntegers() {
      if (!symIntegersList) { return; }
      const items = (state.symIntegers || []).filter(item => isAddrInCurrentRegion(item.address));
      if (items.length === 0) {
        symIntegersList.innerHTML = '<div class="sym-empty">No integers found. Click "Scan for integers".</div>';
        return;
      }
      symIntegersList.innerHTML = items.map(item =>
        `<div class="sym-item" data-addr="${escHtml(item.address)}">
          <span class="sym-item-addr">${escHtml(item.address)}</span>
          <span class="sym-item-value" title="${escHtml(item.hex)}">${item.value} (${escHtml(item.hex)})</span>
        </div>`
      ).join('');
      symIntegersList.querySelectorAll('.sym-item').forEach(el => {
        el.addEventListener('click', () => symNavigate((el as HTMLElement).dataset['addr'] ?? ''));
      });
    }

    symbolsToggleBtn.addEventListener('click', () => toggleSymbolsPanel());
    closeSymbolsBtn.addEventListener('click', () => {
      state.symbolsActive = false;
      symbolsPanel.classList.remove('visible');
      symbolsToggleBtn.classList.remove('active');
    });
    symbolsRefreshBtn.addEventListener('click', () => loadSymbols());

    registersToggleBtn.addEventListener('click', () => toggleRegistersPanel());
    bottomPanelToggleBtn.addEventListener('click', () => toggleBottomPanel());
    searchToggleBtn.addEventListener('click', () => toggleSearch());
    gotoToggleBtn.addEventListener('click', () => toggleGoto());

    document.querySelectorAll('.sym-tab').forEach(btn => {
      btn.addEventListener('click', () => {
        document.querySelectorAll('.sym-tab').forEach(b => b.classList.remove('active'));
        document.querySelectorAll('.sym-tab-content').forEach(c => c.classList.remove('active'));
        btn.classList.add('active');
        const tab = (btn as HTMLElement).dataset['symtab'] ?? '';
        document.getElementById('sym-' + tab + '-content')?.classList.add('active');
      });
    });

    symAddBtn.addEventListener('click', () => {
      const addrRaw = symNewAddr.value.trim();
      const name    = symNewName.value.trim();
      const comment = symNewComment.value.trim();
      if (!addrRaw || !name) { return; }
      const addrNum = parseInt(addrRaw.replace(/^0x/i, ''), 16);
      if (isNaN(addrNum)) { return; }
      vscode.postMessage({ type: 'addLabel', address: addrNum, labelName: name, comment: comment || undefined });
      symNewAddr.value = '';
      symNewName.value = '';
      symNewComment.value = '';
    });

    [symNewAddr, symNewName, symNewComment].forEach(input => {
      input?.addEventListener('keydown', (e: KeyboardEvent) => {
        if (e.key === 'Enter') { symAddBtn.click(); }
      });
    });

    symScanStringsBtn.addEventListener('click', () => {
      symStringsStatus.textContent = 'Scanning…';
      symScanStringsBtn.disabled = true;
      vscode.postMessage({ type: 'scanStrings' });
      setTimeout(() => {
        symScanStringsBtn.disabled = false;
        if (symStringsStatus.textContent === 'Scanning…') { symStringsStatus.textContent = ''; }
      }, SCAN_TIMEOUT_MS);
    });

    symScanFunctionsBtn.addEventListener('click', () => {
      symFunctionsStatus.textContent = 'Scanning…';
      symScanFunctionsBtn.disabled = true;
      vscode.postMessage({ type: 'scanFunctions' });
      setTimeout(() => {
        symScanFunctionsBtn.disabled = false;
        if (symFunctionsStatus.textContent === 'Scanning…') { symFunctionsStatus.textContent = ''; }
      }, SCAN_TIMEOUT_MS);
    });

    symScanPointersBtn.addEventListener('click', () => {
      symPointersStatus.textContent = 'Scanning…';
      symScanPointersBtn.disabled = true;
      vscode.postMessage({ type: 'scanPointers' });
      setTimeout(() => {
        symScanPointersBtn.disabled = false;
        if (symPointersStatus.textContent === 'Scanning…') { symPointersStatus.textContent = ''; }
      }, SCAN_TIMEOUT_MS);
    });

    symScanFloatsBtn.addEventListener('click', () => {
      symFloatsStatus.textContent = 'Scanning…';
      symScanFloatsBtn.disabled = true;
      vscode.postMessage({ type: 'scanFloats' });
      setTimeout(() => {
        symScanFloatsBtn.disabled = false;
        if (symFloatsStatus.textContent === 'Scanning…') { symFloatsStatus.textContent = ''; }
      }, SCAN_TIMEOUT_MS);
    });

    symScanIntegersBtn.addEventListener('click', () => {
      symIntegersStatus.textContent = 'Scanning…';
      symScanIntegersBtn.disabled = true;
      vscode.postMessage({ type: 'scanIntegers' });
      setTimeout(() => {
        symScanIntegersBtn.disabled = false;
        if (symIntegersStatus.textContent === 'Scanning…') { symIntegersStatus.textContent = ''; }
      }, SCAN_TIMEOUT_MS);
    });

    // ── Debug panel ───────────────────────────────────────────────────────────
    const MIPS_REG_NAMES = [
      '$zero','$at','$v0','$v1','$a0','$a1','$a2','$a3',
      '$t0','$t1','$t2','$t3','$t4','$t5','$t6','$t7',
      '$s0','$s1','$s2','$s3','$s4','$s5','$s6','$s7',
      '$t8','$t9','$k0','$k1','$gp','$sp','$fp','$ra',
    ];

    // COP0 register names using standard MIPS descriptive names.
    // r24 is 'Debug' (PS2 EE hardware debug register, not a reserved slot).
    // All other names follow standard MIPS / PS2 EE documentation.
    const COP0_REG_NAMES = [
      'Index','Random','EntryLo0','EntryLo1','Context','PageMask','Wired','Reserved7',
      'BadVAddr','Count','EntryHi','Compare','Status','Cause','EPC','PRId',
      'Config','LLAddr','WatchLo','WatchHi','XContext','Reserved21','Reserved22','Reserved23',
      'Debug','PerfCnt','ErrCtl','CacheErr','TagLo','TagHi','ErrorEPC','Reserved31',
    ];

    const GS_PRIV_NAMES = [
      'PMODE','SMODE1','SMODE2','SRFSH','SYNCH1','SYNCH2','SYNCV',
      'DISPFB1','DISPLAY1','DISPFB2','DISPLAY2',
      'EXTBUF','EXTDATA','EXTWRITE','BGCOLOR',
      'CSR','IMR','BUSDIR','SIGLBLID',
    ];

    // COP2 (VU0) control register names matching PCSX2 DisR5900asm.cpp COP2_REG_CTL exactly.
    // Indices 0-15: vi00-vi15 (integer), 16-31: control registers.
    const COP2_REG_CTL = [
      'vi00','vi01','vi02','vi03','vi04','vi05','vi06','vi07',
      'vi08','vi09','vi10','vi11','vi12','vi13','vi14','vi15',
      'Status','MACflag','ClipFlag','c2c19','R','I','Q','c2c23',
      'c2c24','c2c25','TPC','CMSAR0','FBRST','VPU-STAT','c2c30','CMSAR1',
    ];

    const hex32 = (n: number) => (n >>> 0).toString(16).toUpperCase().padStart(8, '0');
    const hex64 = (lo: number, hi: number) =>
      (hi >>> 0).toString(16).toUpperCase().padStart(8, '0') + '_' +
      (lo >>> 0).toString(16).toUpperCase().padStart(8, '0');

    function rawBitsToFloat(bits: number): string {
      const buf = new ArrayBuffer(4);
      new Uint32Array(buf)[0] = bits >>> 0;
      const f = new DataView(buf).getFloat32(0, true);
      return f.toPrecision(6).replace(/\.?0+$/, '');
    }

    // ── Register in-place editor ──────────────────────────────────────────────
    const stripSeparators = (s: string) => s.replace(/_/g, '');

    function openRegisterEdit(regType: string, regIdx: number, currentVal: string, targetEl: HTMLElement): void {
      // Replace the value element with an <input> for inline editing.
      const origText = targetEl.textContent ?? '';
      const input = document.createElement('input');
      input.type = 'text';
      input.value = stripSeparators(currentVal);
      input.style.cssText =
        'font-family:monospace;font-size:12px;color:#d4d4d4;background:#3c3c3c;' +
        'border:1px solid #007acc;outline:none;width:18ch;padding:0 2px;';

      function commitEdit() {
        const raw = input.value.trim();
        targetEl.textContent = origText;  // restore immediately
        if (raw === '' || raw === stripSeparators(origText)) { return; }
        vscode.postMessage({ type: 'setRegister', regType, regIdx, regValue: raw });
      }
      function cancelEdit() { targetEl.textContent = origText; }

      targetEl.textContent = '';
      targetEl.appendChild(input);
      input.focus();
      input.select();
      input.addEventListener('blur', commitEdit);
      input.addEventListener('keydown', (e: KeyboardEvent) => {
        if (e.key === 'Enter')  { input.removeEventListener('blur', commitEdit); commitEdit(); }
        if (e.key === 'Escape') { input.removeEventListener('blur', commitEdit); cancelEdit(); }
      });
    }

    function showDebugPanel(regs: RegisterData, stackBytes: number[], stackBase: number) {
      debugPanel.classList.add('visible');
      registersToggleBtn.classList.add('active');
      renderGprPanel(regs, stackBytes, stackBase);
      renderCop0Panel(regs);
      renderFprPanel(regs);
      renderFcrPanel(regs);
      renderVu0fPanel(regs);
      renderVu0iPanel(regs);
      renderGsPanel(regs);
      renderStackPanel(regs, stackBytes, stackBase);
    }

    function hideDebugPanel() {
      debugPanel.classList.remove('visible');
      registersToggleBtn.classList.remove('active');
    }

    /**
     * Returns a zero-filled `RegisterData` object covering every register
     * group (GPR, COP0, FPU, VU0, GS).  Used as placeholder content for the
     * register panel before the first breakpoint snapshot is received from the
     * emulator — all values show as 0x00000000, giving the panel a consistent
     * layout rather than appearing empty.
     */
    function makePlaceholderRegs(): RegisterData {
      return {
        pc: 0,
        hi: 0, lo: 0, hi1: 0, lo1: 0, sa: 0,
        gpr: new Array(32).fill(0),
        gprLo32:   new Array(32).fill(0),
        gprHi32:   new Array(32).fill(0),
        gprUpLo32: new Array(32).fill(0),
        gprUpHi32: new Array(32).fill(0),
        cop0Regs: new Array(32).fill(0),
        fpr: new Array(32).fill(0),
        fprAcc: 0,
        fcr0: 0, fcr31: 0,
        vu0fX: new Array(32).fill(0), vu0fY: new Array(32).fill(0),
        vu0fZ: new Array(32).fill(0), vu0fW: new Array(32).fill(0),
        vu0AccX: 0, vu0AccY: 0, vu0AccZ: 0, vu0AccW: 0,
        vu0Q: 0, vu0P: 0, vu0I: 0,
        vu0Status: 0, vu0Mac: 0, vu0Clip: 0,
        vu0i:   new Array(32).fill(0),
        gsPriv: new Array(64).fill(0),
      };
    }

    function renderGprPanel(regs: RegisterData, stackBytes?: number[], stackBase?: number) {
      if (!regs) { regContent.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
      const lo32  = regs.gprLo32  ?? [];
      const hi32  = regs.gprHi32  ?? [];
      const upLo  = regs.gprUpLo32 ?? [];
      const upHi  = regs.gprUpHi32 ?? [];

      const h32 = (n: number) => (n >>> 0).toString(16).toUpperCase().padStart(8, '0');

      // Format a full 128-bit GPR value as four 32-bit words — upper lane first,
      // matching PCSX2's quad-word register display.
      // Layout (MSB→LSB): upHi32 upLo32 hi32 lo32
      // The upper 64-bit lane is dimmed when zero (common during normal PS2 operation)
      // but always shown to make the 128-bit nature of GPRs visible.
      function gpr128(i: number): string {
        const l0 = lo32[i] ?? 0; const l1 = hi32[i] ?? 0;
        const u0 = upLo[i] ?? 0; const u1 = upHi[i] ?? 0;
        const upperZero = u0 === 0 && u1 === 0;
        const upperCls = upperZero ? ' class="dbg-qw-upper"' : '';
        return `<span${upperCls}>${h32(u1)}_${h32(u0)}</span>_${h32(l1)}_${h32(l0)}`;
      }

      function gprNz(i: number): boolean {
        return (lo32[i] ?? 0) !== 0 || (hi32[i] ?? 0) !== 0 ||
               (upLo[i] ?? 0) !== 0 || (upHi[i] ?? 0) !== 0;
      }

      // Returns the CSS category class for the given GPR index, used to colour
      // register names by their MIPS ABI role so different categories are
      // visually distinct in the debug panel.
      function gprCatClass(i: number): string {
        if (i === 0)                            return 'dbg-reg-zero';   // $zero
        if (i === 1)                            return 'dbg-reg-at';     // $at
        if (i >= 2  && i <= 3)                  return 'dbg-reg-v';      // $v0-$v1
        if (i >= 4  && i <= 7)                  return 'dbg-reg-a';      // $a0-$a3
        if ((i >= 8 && i <= 15) || i === 24 || i === 25) return 'dbg-reg-t'; // $t0-$t9
        if (i >= 16 && i <= 23)                 return 'dbg-reg-s';      // $s0-$s7
        if (i === 26 || i === 27)               return 'dbg-reg-k';      // $k0-$k1
        return 'dbg-reg-special';                                         // $gp $sp $fp $ra
      }

      // HI/LO are 64-bit on the PS2 EE. We receive them as the lower 32 bits
      // from `regs.hi`/`regs.lo`; the socket client maps them from the ulong
      // field but JavaScript's number type loses precision above 2^53.  For a
      // faithful PCSX2-style display we show the low 32 bits only since the
      // full 64-bit value is only meaningful to the pipeline internals.
      let html = '<div class="dbg-section-title">General Purpose Registers (128-bit)</div>';
      html += `<div class="dbg-row" data-reg-type="PC" data-reg-idx="0"><span class="dbg-name">pc   </span><span class="dbg-val dbg-editable" title="Double-click to edit">${hex32(regs.pc ?? 0)}</span></div>`;
      html += `<div class="dbg-row" data-reg-type="HI"  data-reg-idx="0"><span class="dbg-name">hi   </span><span class="dbg-val dbg-editable" title="Double-click to edit">${hex32(regs.hi ?? 0)}</span>`;
      html += ` <span class="dbg-name" style="margin-left:8px">lo   </span><span class="dbg-val dbg-editable" data-reg-type="LO" title="Double-click to edit">${hex32(regs.lo ?? 0)}</span></div>`;
      html += `<div class="dbg-row" data-reg-type="HI1" data-reg-idx="0"><span class="dbg-name">hi1  </span><span class="dbg-val dbg-editable" title="Double-click to edit">${hex32(regs.hi1 ?? 0)}</span>`;
      html += ` <span class="dbg-name" style="margin-left:8px">lo1  </span><span class="dbg-val dbg-editable" data-reg-type="LO1" title="Double-click to edit">${hex32(regs.lo1 ?? 0)}</span></div>`;
      html += `<div class="dbg-row" data-reg-type="SA" data-reg-idx="0"><span class="dbg-name">sa   </span><span class="dbg-val dbg-editable" title="Double-click to edit">${hex32(regs.sa ?? 0)}</span></div>`;
      html += '<div class="dbg-divider"></div>';
      // Column header mimicking PCSX2's quad-word layout
      html += '<div class="dbg-row dbg-qw-header"><span class="dbg-name">     </span><span class="dbg-qw-hdr-val"><span class="dbg-qw-col">bits[127:96]</span><span class="dbg-qw-sep"> </span><span class="dbg-qw-col">bits[95:64]</span><span class="dbg-qw-sep"> </span><span class="dbg-qw-col">bits[63:32]</span><span class="dbg-qw-sep"> </span><span class="dbg-qw-col">bits[31:0]</span></span></div>';
      for (let i = 0; i < 32; i++) {
        const name = MIPS_REG_NAMES[i].padEnd(5);
        const val  = gpr128(i);
        const nz   = gprNz(i);
        const ro   = i === 0;  // $zero is read-only
        const cat  = gprCatClass(i);
        const editAttr = ro ? '' : ` data-reg-type="GPR" data-reg-idx="${i}"`;
        html += `<div class="dbg-row${nz ? ' dbg-nonzero' : ''}"${editAttr}><span class="dbg-name ${cat}">${name}</span><span class="dbg-val dbg-qw-val${ro ? '' : ' dbg-editable'}" title="${ro ? '' : 'Double-click to edit'}">${val}</span></div>`;
      }

      // ── Inline call-stack trace from stack memory ────────────────────────
      if (stackBytes && stackBytes.length > 0) {
        const sp = lo32[29] ?? 0;
        html += '<div class="dbg-divider"></div>';
        html += `<div class="dbg-section-title">Stack Trace  SP=0x${hex32(sp)}</div>`;
        const readUInt32LE = (b: number[], off: number) =>
          ((b[off+3] << 24) | (b[off+2] << 16) | (b[off+1] << 8) | b[off]) >>> 0;
        const codeAddrs: { addr: number; val: number }[] = [];
        for (let i = 0; i + 4 <= Math.min(stackBytes.length, 128); i += 4) {
          const word = readUInt32LE(stackBytes, i);
          if (word >= 0x80000000 && word < 0xC0000000) {
            codeAddrs.push({ addr: (stackBase! + i) >>> 0, val: word });
          }
        }
        if (codeAddrs.length > 0) {
          for (const { addr, val } of codeAddrs) {
            html += `<div class="dbg-row dbg-retaddr"><span class="dbg-addr">${hex32(addr)}</span><span class="dbg-val">${hex32(val)}</span><span class="dbg-dim" style="margin-left:6px">← return addr</span></div>`;
          }
        } else {
          html += '<div class="dbg-row dbg-dim">No return addresses on stack</div>';
        }
      }

      regContent.innerHTML = html;

      // Attach double-click edit handlers to all editable rows.
      regContent.querySelectorAll('[data-reg-type]').forEach(el => {
        el.addEventListener('dblclick', () => {
          const row = el as HTMLElement;
          const regType  = row.dataset['regType'] ?? '';
          const regIdxRaw = row.dataset['regIdx'];
          const regIdx   = regIdxRaw !== undefined ? parseInt(regIdxRaw, 10) : 0;
          const valEl    = row.querySelector('.dbg-editable') as HTMLElement | null;
          if (!valEl) { return; }
          const currentVal = valEl.textContent ?? '';
          openRegisterEdit(regType, regIdx, currentVal, valEl);
        });
      });
    }

    function renderCop0Panel(regs: RegisterData) {
      const el = document.getElementById('cop0-content');
      if (!el) { return; }
      if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
      let html = '<div class="dbg-section-title">System Control (COP0)</div>';
      const cop0 = regs.cop0Regs ?? [];
      for (let i = 0; i < 32; i++) {
        const name = ('r' + i + ' ' + COP0_REG_NAMES[i]).substring(0, 14).padEnd(14);
        const val  = hex32(cop0[i] ?? 0);
        const nz   = (cop0[i] ?? 0) !== 0;
        html += `<div class="dbg-row${nz ? ' dbg-nonzero' : ''}" data-reg-type="COP0" data-reg-idx="${i}"><span class="dbg-name" style="font-size:10px">${name}</span><span class="dbg-val dbg-editable" title="Double-click to edit">${val}</span></div>`;
      }
      el.innerHTML = html;
      el.querySelectorAll('[data-reg-type]').forEach(row => {
        row.addEventListener('dblclick', () => {
          const r = row as HTMLElement;
          const valEl = r.querySelector('.dbg-editable') as HTMLElement | null;
          if (!valEl) { return; }
          openRegisterEdit('COP0', parseInt(r.dataset['regIdx'] ?? '0', 10), valEl.textContent ?? '', valEl);
        });
      });
    }

    function renderFprPanel(regs: RegisterData) {
      const el = document.getElementById('fpr-content');
      if (!el) { return; }
      if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
      let html = '<div class="dbg-section-title">Floating Point Registers (COP1)</div>';
      const fpr = regs.fpr ?? [];
      for (let i = 0; i < 32; i++) {
        const bits = (fpr[i] ?? 0) >>> 0;
        const fval = rawBitsToFloat(bits);
        const nz   = bits !== 0;
        html += `<div class="dbg-row${nz ? ' dbg-nonzero' : ''}" data-reg-type="FPR" data-reg-idx="${i}"><span class="dbg-name">f${String(i).padStart(2,'0')} </span><span class="dbg-val dbg-editable" title="Double-click to edit">${hex32(bits)}</span><span class="dbg-dim" style="margin-left:6px">${fval}</span></div>`;
      }
      html += '<div class="dbg-divider"></div>';
      const accBits = (regs.fprAcc ?? 0) >>> 0;
      html += `<div class="dbg-row${accBits !== 0 ? ' dbg-nonzero' : ''}"><span class="dbg-name">ACC  </span><span class="dbg-val">${hex32(accBits)}</span><span class="dbg-dim" style="margin-left:6px">${rawBitsToFloat(accBits)}</span></div>`;
      el.innerHTML = html;
      el.querySelectorAll('[data-reg-type]').forEach(row => {
        row.addEventListener('dblclick', () => {
          const r = row as HTMLElement;
          const valEl = r.querySelector('.dbg-editable') as HTMLElement | null;
          if (!valEl) { return; }
          openRegisterEdit('FPR', parseInt(r.dataset['regIdx'] ?? '0', 10), valEl.textContent ?? '', valEl);
        });
      });
    }

    function renderFcrPanel(regs: RegisterData) {
      const el = document.getElementById('fcr-content');
      if (!el) { return; }
      if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
      let html = '<div class="dbg-section-title">FPU Control Registers (FCR)</div>';
      const fcr0  = (regs.fcr0  ?? 0) >>> 0;
      const fcr31 = (regs.fcr31 ?? 0) >>> 0;
      html += `<div class="dbg-row"><span class="dbg-name">FCR0  </span><span class="dbg-val">${hex32(fcr0)}</span><span class="dbg-dim" style="margin-left:6px">FIR (implementation)</span></div>`;
      html += `<div class="dbg-row${fcr31 !== 0 ? ' dbg-nonzero' : ''}"><span class="dbg-name">FCR31 </span><span class="dbg-val">${hex32(fcr31)}</span><span class="dbg-dim" style="margin-left:6px">FCSR (control/status)</span></div>`;
      html += '<div class="dbg-divider"></div>';
      html += '<div class="dbg-section-title">FCR31 decode</div>';
      const rm    = fcr31 & 0x3;
      const flags = (fcr31 >> 7)  & 0x1F;
      const ena   = (fcr31 >> 12) & 0x1F;
      const cause = (fcr31 >> 18) & 0x3F;
      const cc    = (fcr31 >> 23) & 0x1;
      const fs    = (fcr31 >> 24) & 0x1;
      const rmStr = ['RN (nearest)','RZ (zero)','RP (+inf)','RM (-inf)'][rm];
      html += `<div class="dbg-row"><span class="dbg-name">RM    </span><span class="dbg-val">${rmStr}</span></div>`;
      html += `<div class="dbg-row"><span class="dbg-name">Flags </span><span class="dbg-val">${flags.toString(2).padStart(5,'0')}</span><span class="dbg-dim" style="margin-left:6px">V Z O U I (sticky)</span></div>`;
      html += `<div class="dbg-row"><span class="dbg-name">Enable</span><span class="dbg-val">${ena.toString(2).padStart(5,'0')}</span><span class="dbg-dim" style="margin-left:6px">V Z O U I</span></div>`;
      html += `<div class="dbg-row"><span class="dbg-name">Cause </span><span class="dbg-val">${cause.toString(2).padStart(6,'0')}</span><span class="dbg-dim" style="margin-left:6px">E V Z O U I</span></div>`;
      html += `<div class="dbg-row"><span class="dbg-name">CC    </span><span class="dbg-val">${cc}</span></div>`;
      html += `<div class="dbg-row"><span class="dbg-name">FS    </span><span class="dbg-val">${fs}</span><span class="dbg-dim" style="margin-left:6px">flush-to-zero</span></div>`;
      el.innerHTML = html;
    }

    function renderVu0fPanel(regs: RegisterData) {
      const el = document.getElementById('vu0f-content');
      if (!el) { return; }
      if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
      let html = '<div class="dbg-section-title">VU0 Float Registers (vf0–vf31)</div>';
      const xs = regs.vu0fX ?? []; const ys = regs.vu0fY ?? [];
      const zs = regs.vu0fZ ?? []; const ws = regs.vu0fW ?? [];
      for (let i = 0; i < 32; i++) {
        const xb = (xs[i] ?? 0) >>> 0; const yb = (ys[i] ?? 0) >>> 0;
        const zb = (zs[i] ?? 0) >>> 0; const wb = (ws[i] ?? 0) >>> 0;
        const nz = xb !== 0 || yb !== 0 || zb !== 0 || wb !== 0;
        html += `<div class="dbg-row${nz ? ' dbg-nonzero' : ''}">
          <span class="dbg-name">vf${String(i).padStart(2,'0')}</span>
          <span class="dbg-dim" style="font-size:9px;margin-left:4px">x:${rawBitsToFloat(xb)} y:${rawBitsToFloat(yb)} z:${rawBitsToFloat(zb)} w:${rawBitsToFloat(wb)}</span>
        </div>`;
      }
      html += '<div class="dbg-divider"></div>';
      const axb = (regs.vu0AccX ?? 0) >>> 0; const ayb = (regs.vu0AccY ?? 0) >>> 0;
      const azb = (regs.vu0AccZ ?? 0) >>> 0; const awb = (regs.vu0AccW ?? 0) >>> 0;
      html += `<div class="dbg-row"><span class="dbg-name">ACC  </span><span class="dbg-dim" style="font-size:9px;margin-left:4px">x:${rawBitsToFloat(axb)} y:${rawBitsToFloat(ayb)} z:${rawBitsToFloat(azb)} w:${rawBitsToFloat(awb)}</span></div>`;
      html += `<div class="dbg-row"><span class="dbg-name">Q    </span><span class="dbg-val">${hex32((regs.vu0Q ?? 0) >>> 0)}</span><span class="dbg-dim" style="margin-left:6px">${rawBitsToFloat((regs.vu0Q ?? 0) >>> 0)}</span></div>`;
      html += `<div class="dbg-row"><span class="dbg-name">P    </span><span class="dbg-val">${hex32((regs.vu0P ?? 0) >>> 0)}</span><span class="dbg-dim" style="margin-left:6px">${rawBitsToFloat((regs.vu0P ?? 0) >>> 0)}</span></div>`;
      html += `<div class="dbg-row"><span class="dbg-name">I    </span><span class="dbg-val">${hex32((regs.vu0I ?? 0) >>> 0)}</span><span class="dbg-dim" style="margin-left:6px">${rawBitsToFloat((regs.vu0I ?? 0) >>> 0)}</span></div>`;
      html += '<div class="dbg-divider"></div>';
      html += `<div class="dbg-row"><span class="dbg-name">Status</span><span class="dbg-val">${hex32(regs.vu0Status ?? 0)}</span></div>`;
      html += `<div class="dbg-row"><span class="dbg-name">MAC   </span><span class="dbg-val">${((regs.vu0Mac ?? 0) >>> 0).toString(16).toUpperCase().padStart(4,'0')}</span></div>`;
      html += `<div class="dbg-row"><span class="dbg-name">Clip  </span><span class="dbg-val">${hex32(regs.vu0Clip ?? 0)}</span></div>`;
      el.innerHTML = html;
    }

    function renderVu0iPanel(regs: RegisterData) {
      const el = document.getElementById('vu0i-content');
      if (!el) { return; }
      if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
      const vi = regs.vu0i ?? [];
      // Integer registers vi00–vi15 (16-bit each)
      let html = '<div class="dbg-section-title">VU0 Integer Registers (COP2_REG_CTL)</div>';
      for (let i = 0; i < 16; i++) {
        const val = (vi[i] ?? 0) & 0xFFFF;
        const nz  = val !== 0;
        const ro  = i === 0;
        const editAttr = ro ? '' : ` data-reg-type="VU0I" data-reg-idx="${i}"`;
        html += `<div class="dbg-row${nz ? ' dbg-nonzero' : ''}"${editAttr}><span class="dbg-name" style="font-size:10px">${COP2_REG_CTL[i].padEnd(8)}</span><span class="dbg-val${ro ? '' : ' dbg-editable'}" title="${ro ? '' : 'Double-click to edit'}">${val.toString(16).toUpperCase().padStart(4,'0')}</span><span class="dbg-dim" style="margin-left:6px">${val}</span></div>`;
      }
      // Control registers vi16–vi31
      html += '<div class="dbg-divider"></div>';
      html += '<div class="dbg-section-title">VU0 Control Registers</div>';
      for (let i = 16; i < 32; i++) {
        const raw = vi[i] ?? 0;
        const nz  = raw !== 0;
        const name = COP2_REG_CTL[i];
        // Detect float registers (R=20, I=21, Q=22) and display as both hex and float
        let valStr: string;
        let annot = '';
        if (i === 20 || i === 21 || i === 22) {
          valStr = hex32(raw);
          annot = `<span class="dbg-dim" style="margin-left:6px">${rawBitsToFloat(raw)}</span>`;
        } else if (i === 17) {
          valStr = (raw & 0xFFFF).toString(16).toUpperCase().padStart(4,'0');
        } else {
          valStr = hex32(raw);
        }
        html += `<div class="dbg-row${nz ? ' dbg-nonzero' : ''}"><span class="dbg-name" style="font-size:10px">${name.padEnd(8)}</span><span class="dbg-val">${valStr}</span>${annot}</div>`;
      }
      el.innerHTML = html;

      // Click-to-edit for vi01–vi15
      el.querySelectorAll('[data-reg-type]').forEach(row => {
        row.addEventListener('dblclick', () => {
          const r = row as HTMLElement;
          const valEl = r.querySelector('.dbg-editable') as HTMLElement | null;
          if (!valEl) { return; }
          openRegisterEdit('VU0I', parseInt(r.dataset['regIdx'] ?? '0', 10), valEl.textContent ?? '', valEl);
        });
      });
    }

    function renderGsPanel(regs: RegisterData) {
      const el = document.getElementById('gs-content');
      if (!el) { return; }
      if (!regs) { el.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
      let html = '<div class="dbg-section-title">Graphics Synthesizer Registers</div>';
      const priv = regs.gsPriv ?? [];
      for (let i = 0; i < GS_PRIV_NAMES.length; i++) {
        const raw = priv[i] ?? 0;
        const lo  = (raw & 0xFFFFFFFF) >>> 0;
        const hi  = Math.floor(raw / 0x100000000) >>> 0;
        const valStr = hi !== 0 ? hex64(lo, hi) : hex32(lo);
        const nz  = raw !== 0;
        const name = GS_PRIV_NAMES[i].padEnd(10);
        html += `<div class="dbg-row${nz ? ' dbg-nonzero' : ''}"><span class="dbg-name" style="font-size:10px">${name}</span><span class="dbg-val" style="font-size:10px">${valStr}</span></div>`;
      }
      el.innerHTML = html;
    }

    function renderStackPanel(regs: RegisterData, stackBytes: number[], stackBase: number) {
      if (!regs) { stackContent.innerHTML = '<div class="dbg-row">No register data</div>'; return; }
      const sp = (regs.gpr ?? [])[29] ?? 0;
      let html = `<div class="dbg-section-title">Stack  SP=${hex32(sp)}</div>`;
      if (!stackBytes || stackBytes.length === 0) {
        html += '<div class="dbg-row dbg-dim">Stack data unavailable</div>';
        stackContent.innerHTML = html;
        return;
      }
      for (let i = 0; i < Math.min(stackBytes.length, 128); i += 4) {
        const addr = (stackBase + i) >>> 0;
        const word = ((stackBytes[i+3] << 24) | (stackBytes[i+2] << 16) | (stackBytes[i+1] << 8) | stackBytes[i]) >>> 0;
        const isCodeAddr = word >= 0x80000000 && word < 0xC0000000;
        const cls = isCodeAddr ? ' dbg-retaddr' : '';
        const annotation = isCodeAddr ? ' <span class="dbg-dim">← code ptr</span>' : '';
        html += `<div class="dbg-row${cls}"><span class="dbg-addr">${hex32(addr)}</span><span class="dbg-val">${hex32(word)}</span>${annotation}</div>`;
      }
      stackContent.innerHTML = html;
    }

    document.querySelectorAll('.debug-tab').forEach(btn => {
      btn.addEventListener('click', () => {
        document.querySelectorAll('.debug-tab').forEach(b => b.classList.remove('active'));
        document.querySelectorAll('.debug-tab-content').forEach(c => c.classList.remove('active'));
        btn.classList.add('active');
        const tab = (btn as HTMLElement).dataset['tab'] ?? '';
        document.getElementById(tab + '-content')?.classList.add('active');
      });
    });

    // ── Bottom panel (Call Stack / Threads / Breakpoints) ─────────────────────
    const callstackBody  = document.getElementById('callstack-body')!;
    const threadsBody    = document.getElementById('threads-body')!;
    const breakpointsBody = document.getElementById('breakpoints-body')!;
    const bpClearAllBtn  = document.getElementById('bp-clear-all-btn')!;
    const bottomRefreshBtn = document.getElementById('bottom-refresh-btn')!;

    // Track latest stack data so the call-stack tab can re-render on tab switch.
    let _lastStackRegs:  RegisterData | null = null;
    let _lastStackBytes: number[] = [];
    let _lastStackBase:  number = 0;

    // ── Bottom panel tab switching ─────────────────────────────────────────────
    document.querySelectorAll('.bottom-tab').forEach(btn => {
      btn.addEventListener('click', () => {
        document.querySelectorAll('.bottom-tab').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        const tab = (btn as HTMLElement).dataset['btab'] ?? '';
        document.querySelectorAll('.bottom-tab-content').forEach(c => c.classList.remove('active'));
        document.getElementById(tab + '-content')?.classList.add('active');
        if (tab === 'threads' && state.isLive) { requestThreads(); }
        if (tab === 'breakpoints') { renderBreakpointsPanel(); }
        if (tab === 'callstack' && _lastStackRegs) {
          renderCallStackPanel(_lastStackRegs, _lastStackBytes, _lastStackBase);
        }
      });
    });

    bottomRefreshBtn.addEventListener('click', () => {
      const activeTab = (document.querySelector('.bottom-tab.active') as HTMLElement | null)?.dataset['btab'] ?? '';
      if (activeTab === 'threads' && state.isLive) { requestThreads(); }
      if (activeTab === 'callstack' && _lastStackRegs) {
        renderCallStackPanel(_lastStackRegs, _lastStackBytes, _lastStackBase);
      }
      if (activeTab === 'breakpoints') { renderBreakpointsPanel(); }
    });

    // ── Call Stack panel ───────────────────────────────────────────────────────
    // Mimics PCSX2's call stack display:
    //   Frame 0  = current PC (always the executing instruction)
    //   Frame 1+ = code pointers found on the stack (return addresses)
    // Each row shows: frame #, address, function/label name (or "Unknown").
    function renderCallStackPanel(regs: RegisterData, stackBytes: number[], stackBase: number) {
      _lastStackRegs  = regs;
      _lastStackBytes = stackBytes;
      _lastStackBase  = stackBase;

      if (!regs) {
        callstackBody.innerHTML = '<tr><td colspan="3" class="dbg-td-empty">No register data</td></tr>';
        return;
      }

      const frames: Array<{ pc: number }> = [];

      // Frame 0: current PC
      const currentPc = (regs.pc ?? 0) >>> 0;
      frames.push({ pc: currentPc });

      // Remaining frames: scan stack for code pointers
      if (stackBytes && stackBytes.length > 0) {
        for (let i = 0; i < Math.min(stackBytes.length, 512); i += 4) {
          const word = ((stackBytes[i+3] << 24) | (stackBytes[i+2] << 16) | (stackBytes[i+1] << 8) | stackBytes[i]) >>> 0;
          // PS2 code addresses: KSEG0 (0x80000000–0x9FFFFFFF) and KSEG1 (0xA0000000–0xBFFFFFFF)
          if (word >= 0x80000000 && word < 0xC0000000) {
            frames.push({ pc: word });
          }
        }
      }

      if (frames.length === 0) {
        callstackBody.innerHTML = '<tr><td colspan="3" class="dbg-td-empty">No stack frames available</td></tr>';
        return;
      }

      callstackBody.innerHTML = frames.map((f, idx) => {
        const addrHex = hex32(f.pc);
        // Try to find a function label for this address
        const label = lookupLabel(f.pc);
        const funcName = label || 'Unknown';
        const cls = idx === 0 ? ' class="dbg-tr-current"' : '';
        return `<tr${cls} data-addr="${addrHex}">
          <td class="dbg-td-num">${idx}</td>
          <td class="dbg-td-addr">${addrHex}</td>
          <td class="dbg-td-func">${escHtml(funcName)}</td>
        </tr>`;
      }).join('');

      // Navigate to address on row click
      callstackBody.querySelectorAll('tr[data-addr]').forEach(row => {
        row.addEventListener('click', () => {
          const addr = parseInt((row as HTMLElement).dataset['addr'] ?? '0', 16);
          if (!isNaN(addr)) { navigateToAddress(addr); }
        });
      });
    }

    // Look up the nearest label at or before `pc` from the symbol table.
    function lookupLabel(pc: number): string | null {
      const labels = state.symLabels;
      if (!labels || labels.length === 0) { return null; }
      let best: string | null = null;
      let bestDist = Infinity;
      for (const item of labels) {
        const addr = parseInt(String(item.address).replace(/^0x/i, ''), 16);
        if (!isNaN(addr) && addr <= pc && pc - addr < bestDist) {
          bestDist = pc - addr;
          best = item.label;
        }
      }
      return best;
    }

    // ── Threads panel ──────────────────────────────────────────────────────────
    // Mimics PCSX2's thread view: TID, Status, PC, SP, Priority, Entry.
    function requestThreads() {
      threadsBody.innerHTML = '<tr><td colspan="6" class="dbg-td-empty">Loading…</td></tr>';
      vscode.postMessage({ type: 'getThreads' });
    }

    function renderThreadsPanel(threads: ThreadEntry[]) {
      if (!threads || threads.length === 0) {
        threadsBody.innerHTML = '<tr><td colspan="6" class="dbg-td-empty">No thread data available</td></tr>';
        return;
      }
      threadsBody.innerHTML = threads.map(t => {
        const statusCls = t.status === 'Running' ? ' class="dbg-tr-running"' : '';
        return `<tr${statusCls} data-addr="${hex32(t.pc)}">
          <td class="dbg-td-num">${t.id}</td>
          <td class="dbg-td-status">${escHtml(t.status)}</td>
          <td class="dbg-td-addr">${hex32(t.pc)}</td>
          <td class="dbg-td-addr">${hex32(t.sp)}</td>
          <td class="dbg-td-num">${t.priority}</td>
          <td class="dbg-td-addr">${hex32(t.entryPc)}</td>
        </tr>`;
      }).join('');

      // Navigate to thread PC on click
      threadsBody.querySelectorAll('tr[data-addr]').forEach(row => {
        row.addEventListener('click', () => {
          const addr = parseInt((row as HTMLElement).dataset['addr'] ?? '0', 16);
          if (!isNaN(addr)) { navigateToAddress(addr); }
        });
      });
    }

    // ── Breakpoints panel ──────────────────────────────────────────────────────
    // Mimics PCSX2's breakpoints view: Address, Type, Condition, Enabled, Delete.
    function renderBreakpointsPanel() {
      if (state.breakpoints.size === 0) {
        breakpointsBody.innerHTML = '<tr><td colspan="5" class="dbg-td-empty">No breakpoints set</td></tr>';
        return;
      }
      const sorted = Array.from(state.breakpoints).sort((a, b) => a - b);
      breakpointsBody.innerHTML = sorted.map(addr => {
        const addrHex = hex32(addr);
        return `<tr data-addr="${addrHex}">
          <td class="dbg-td-addr dbg-bp-addr">${addrHex}</td>
          <td class="dbg-td-type">Execute</td>
          <td class="dbg-td-cond">—</td>
          <td class="dbg-td-enabled"><input type="checkbox" class="dbg-bp-enabled" checked data-addr="${addrHex}" title="Enabled" /></td>
          <td class="dbg-td-del"><button class="dbg-bp-del-btn" data-addr="${addrHex}" title="Remove breakpoint">×</button></td>
        </tr>`;
      }).join('');

      // Navigate to address on row click (but not on checkbox/button)
      breakpointsBody.querySelectorAll('tr[data-addr]').forEach(row => {
        row.addEventListener('click', (e: Event) => {
          const t = e.target as HTMLElement;
          if (t.classList.contains('dbg-bp-del-btn') || t.classList.contains('dbg-bp-enabled')) { return; }
          const addr = parseInt((row as HTMLElement).dataset['addr'] ?? '0', 16);
          if (!isNaN(addr)) { navigateToAddress(addr); }
        });
      });

      // Delete button
      breakpointsBody.querySelectorAll('.dbg-bp-del-btn').forEach(btn => {
        btn.addEventListener('click', (e: Event) => {
          e.stopPropagation();
          const addrHex = (btn as HTMLElement).dataset['addr'] ?? '0';
          const addr = parseInt(addrHex, 16);
          if (!isNaN(addr)) {
            state.breakpoints.delete(addr);
            vscode.postMessage({ type: 'clearBreakpoint', address: addr });
            renderBreakpointsPanel();
            renderDisassembly();
          }
        });
      });
    }

    bpClearAllBtn.addEventListener('click', () => {
      if (state.breakpoints.size === 0) { return; }
      state.breakpoints.clear();
      vscode.postMessage({ type: 'clearAllBreakpoints' });
      renderBreakpointsPanel();
      renderDisassembly();
    });

    // Subscribe to threads response from extension host
    svc.threadsReceived$.subscribe(threads => {
      renderThreadsPanel(threads);
    });

    // Wire the callback so handleStatusUpdate updates the bottom panel on pause.
    onDebugPanelShown = (regs, stackBytes, stackBase) => {
      renderCallStackPanel(regs, stackBytes, stackBase);
      renderBreakpointsPanel();
    };

    // ── Cleanup ───────────────────────────────────────────────────────────────
    window.addEventListener('unload', () => {
      stopLiveRefresh();
    });
  }
}
