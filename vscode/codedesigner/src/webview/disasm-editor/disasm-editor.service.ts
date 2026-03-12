import { Injectable, OnDestroy } from '@angular/core';
import { Subject, Subscription } from 'rxjs';
import { VsCodeBridgeService } from '../shared/vscode-bridge.service';

// ── Shared data types ─────────────────────────────────────────────────────────

export interface DisasmRow {
  addr: string;
  bytes: string;
  label: string;
  op: string;
  args: string;
  comment: string;
}

export interface SearchMatch {
  instructionIndex: number;
  field: string;
  start: number;
  length: number;
}

export interface SymLabel {
  address: string;
  label: string;
  comment?: string;
}

export interface SymString {
  address: string;
  value: string;
}

export interface SymFunction {
  address: string;
  label: string;
}

export interface SymPointer {
  address:  string;
  pointsTo: string;
}

export interface SymFloat {
  address: string;
  value:   number;
  raw:     string;
}

export interface SymInteger {
  address: string;
  value:   number;
  hex:     string;
}

export interface ThreadEntry {
  id: number;
  status: string;
  pc: number;
  sp: number;
  priority: number;
  entryPc: number;
}

export interface RegisterData {
  pc?: number;
  hi?: number; lo?: number;
  hi1?: number; lo1?: number;
  sa?: number;
  gpr?: number[];
  // Precise 32-bit halves of each 128-bit GPR (see RegisterDump for field semantics)
  gprLo32?: number[];
  gprHi32?: number[];
  gprUpLo32?: number[];
  gprUpHi32?: number[];
  cop0Regs?: number[];
  fpr?: number[];
  fprAcc?: number;
  fcr0?: number; fcr31?: number;
  vu0fX?: number[]; vu0fY?: number[]; vu0fZ?: number[]; vu0fW?: number[];
  vu0AccX?: number; vu0AccY?: number; vu0AccZ?: number; vu0AccW?: number;
  vu0Q?: number; vu0P?: number; vu0I?: number;
  vu0Status?: number; vu0Mac?: number; vu0Clip?: number;
  vu0i?: number[];
  gsPriv?: number[];
}

// ── Editor state ──────────────────────────────────────────────────────────────

export interface DisasmEditorState {
  cursor: number;
  disasmRows: DisasmRow[];
  totalInstructions: number;
  pageSize: number;
  baseAddress: number;
  totalBytes: number;
  regionLabel: string;
  hexData: number[];
  hexOffset: number;
  loadedPages: Map<number, DisasmRow[]>;
  searchMode: string;
  searchQuery: string;
  searchMatches: SearchMatch[];
  currentMatch: number;
  searchActive: boolean;
  gotoActive: boolean;
  symbolsActive: boolean;
  isLive: boolean;
  breakpoints: Set<number>;
  breakpointHitAddress: number | undefined;
  /** The virtual PC address where the emulator is currently paused.  Tracked
   *  separately from `cursor` so that the PC indicator stays visible even
   *  after the user navigates the cursor away from the paused instruction. */
  pcAddress: number | undefined;
  symLabels: SymLabel[];
  symStrings: SymString[];
  symFunctions: SymFunction[];
  symPointers: SymPointer[];
  symFloats: SymFloat[];
  symIntegers: SymInteger[];
  /** Live-resolved labels from the Pointer Manager, keyed by normalised address. */
  pointerLabels: SymLabel[];
}

// ── Event payloads ────────────────────────────────────────────────────────────

export interface InitReceivedEvent {
  isLive: boolean;
  regionLabel: string;
}

export interface HexUpdatedEvent {
  data: number[];
  offset: number;
  highlightAddr?: number;
  highlightLength?: number;
}

export interface StatusUpdatePayload {
  paused: boolean;
  pc: number;
  breakpointHitAddress?: number;
  registers: RegisterData;
  stackBytes: number[];
  stackBase: number;
  instructionAnalysis?: Record<string, string>;
}

// ── Service ───────────────────────────────────────────────────────────────────

/**
 * Centralises shared disassembler state and VS Code message handling.
 *
 * Provide at the component level (`providers: [DisasmEditorService]`) so each
 * editor instance gets its own state.  The `DisasmEditorComponent` subscribes
 * to the Subject observables and reacts by updating the DOM outside the zone.
 */
@Injectable()
export class DisasmEditorService implements OnDestroy {

  // ── Mutable editor state ─────────────────────────────────────────────────
  readonly state: DisasmEditorState = {
    cursor: 0,
    disasmRows: [],
    totalInstructions: 0,
    pageSize: 1000,
    baseAddress: 0,
    totalBytes: 0,
    regionLabel: '',
    hexData: [],
    hexOffset: 0,
    loadedPages: new Map<number, DisasmRow[]>(),
    searchMode: 'text',
    searchQuery: '',
    searchMatches: [],
    currentMatch: -1,
    searchActive: false,
    gotoActive: false,
    symbolsActive: false,
    isLive: false,
    breakpoints: new Set<number>(),
    breakpointHitAddress: undefined,
    pcAddress: undefined,
    symLabels: [],
    symStrings: [],
    symFunctions: [],
    symPointers: [],
    symFloats: [],
    symIntegers: [],
    pointerLabels: [],
  };

  // ── Events the component (and future sub-components) subscribe to ─────────
  readonly initReceived$            = new Subject<InitReceivedEvent>();
  readonly pageLoaded$              = new Subject<{ startIndex: number }>();
  readonly hexUpdated$              = new Subject<HexUpdatedEvent>();
  readonly searchResultsReceived$   = new Subject<SearchMatch[]>();
  readonly statusReceived$          = new Subject<StatusUpdatePayload>();
  readonly resetReceived$           = new Subject<void>();
  readonly symbolsReceived$         = new Subject<void>();
  readonly labelsUpdated$           = new Subject<void>();
  readonly stringsUpdated$          = new Subject<void>();
  readonly functionsUpdated$        = new Subject<void>();
  readonly pointersUpdated$         = new Subject<void>();
  readonly floatsUpdated$           = new Subject<void>();
  readonly integersUpdated$         = new Subject<void>();
  /** Fired when live analysis comments arrive for one or more addresses. */
  readonly analysisUpdated$         = new Subject<Record<string, string>>();
  /** Fired when live-resolved Pointer Manager labels arrive. */
  readonly pointerLabelsUpdated$    = new Subject<void>();
  /** Fired when the extension host instructs this panel to navigate to an address. */
  readonly gotoAddressReceived$     = new Subject<{ address: number }>();
  /** Fired when another panel syncs its goto-input value to this panel. */
  readonly syncGotoInputReceived$   = new Subject<{ value: string }>();
  /** Fired when the extension host reports that no region was found for the requested address. */
  readonly gotoRegionNotFoundReceived$ = new Subject<{ address: number }>();
  /** Fired when the extension host returns the EE OS thread list. */
  readonly threadsReceived$ = new Subject<ThreadEntry[]>();

  private readonly _subs: Subscription[] = [];

  constructor(private readonly _bridge: VsCodeBridgeService) {
    this._subs.push(
      this._bridge.on<Record<string, unknown>>('init').subscribe(msg => this._onInit(msg)),
      this._bridge.on<Record<string, unknown>>('disasmPage').subscribe(msg => this._onDisasmPage(msg)),
      this._bridge.on<Record<string, unknown>>('hexview').subscribe(msg => this._onHexView(msg)),
      this._bridge.on<Record<string, unknown>>('searchResults').subscribe(msg => this._onSearchResults(msg)),
      this._bridge.on<Record<string, unknown>>('status').subscribe(msg => this._onStatus(msg)),
      this._bridge.on<Record<string, unknown>>('reset').subscribe(() => this._onReset()),
      this._bridge.on<Record<string, unknown>>('symbols').subscribe(msg => this._onSymbols(msg)),
      this._bridge.on<Record<string, unknown>>('labelsUpdated').subscribe(msg => this._onLabelsUpdated(msg)),
      this._bridge.on<Record<string, unknown>>('stringsUpdated').subscribe(msg => this._onStringsUpdated(msg)),
      this._bridge.on<Record<string, unknown>>('functionsUpdated').subscribe(msg => this._onFunctionsUpdated(msg)),
      this._bridge.on<Record<string, unknown>>('pointersUpdated').subscribe(msg => this._onPointersUpdated(msg)),
      this._bridge.on<Record<string, unknown>>('floatsUpdated').subscribe(msg => this._onFloatsUpdated(msg)),
      this._bridge.on<Record<string, unknown>>('integersUpdated').subscribe(msg => this._onIntegersUpdated(msg)),
      this._bridge.on<Record<string, unknown>>('pointerLabelsUpdated').subscribe(msg => this._onPointerLabelsUpdated(msg)),
      this._bridge.on<Record<string, unknown>>('gotoAddress').subscribe(msg => this.gotoAddressReceived$.next({ address: msg['address'] as number })),
      this._bridge.on<Record<string, unknown>>('syncGotoInput').subscribe(msg => this.syncGotoInputReceived$.next({ value: msg['value'] as string })),
      this._bridge.on<Record<string, unknown>>('gotoRegionNotFound').subscribe(msg => this.gotoRegionNotFoundReceived$.next({ address: msg['address'] as number })),
      this._bridge.on<Record<string, unknown>>('threads').subscribe(msg => this.threadsReceived$.next((msg['threads'] as ThreadEntry[] | undefined) ?? [])),
    );
  }

  ngOnDestroy(): void {
    this._subs.forEach(s => s.unsubscribe());
  }

  // ── Utility methods ────────────────────────────────────────────────────────

  /** Returns the start instruction index for the page that contains `index`. */
  getPageStart(index: number): number {
    return Math.floor(index / this.state.pageSize) * this.state.pageSize;
  }

  /** Returns the 0-based position of `index` within its page. */
  getLocalIndex(index: number): number {
    return index - this.getPageStart(index);
  }

  /** Posts a message to the VS Code extension host. */
  postMessage(msg: unknown): void {
    this._bridge.postMessage(msg);
  }

  /** Requests the page that contains `startIndex` from the extension host. */
  requestPage(startIndex: number): void {
    this._bridge.postMessage({ type: 'requestPage', startIndex });
  }

  // ── Message handlers ───────────────────────────────────────────────────────

  private _onInit(msg: Record<string, unknown>): void {
    this.state.totalInstructions = msg['totalInstructions'] as number;
    this.state.pageSize          = msg['pageSize']          as number;
    this.state.baseAddress       = msg['baseAddress']       as number;
    this.state.totalBytes        = (msg['totalBytes'] as number | undefined)
      // Fallback: PS2 EE MIPS instructions are always 4 bytes (fixed-width ISA).
      ?? ((msg['totalInstructions'] as number) * 4);
    this.state.regionLabel       = (msg['regionLabel'] as string | undefined) ?? '';
    this.state.loadedPages.clear();
    this.state.disasmRows        = [];
    this.state.cursor            = 0;
    this.state.isLive            = !!(msg['isLive']);

    if (this.state.totalInstructions > 0) {
      this.requestPage(this.getPageStart(0));
    }

    // Re-register any breakpoints the user had set in the editor with the
    // emulator server.  After a restart or reconnect, the server-side
    // breakpoint set is empty; re-sending here keeps the webview and the
    // server in sync without requiring the user to re-add every breakpoint.
    if (this.state.isLive && this.state.breakpoints.size > 0) {
      for (const addr of this.state.breakpoints) {
        this._bridge.postMessage({ type: 'setBreakpoint', address: addr });
      }
    }

    this.initReceived$.next({ isLive: this.state.isLive, regionLabel: this.state.regionLabel });
  }

  private _onDisasmPage(msg: Record<string, unknown>): void {
    const startIndex = msg['startIndex'] as number;
    const rows       = msg['rows']       as DisasmRow[];
    this.state.loadedPages.set(startIndex, rows);
    if (startIndex === this.getPageStart(this.state.cursor)) {
      this.state.disasmRows = rows;
    }
    this.pageLoaded$.next({ startIndex });
  }

  private _onHexView(msg: Record<string, unknown>): void {
    const data   = msg['data']   as number[];
    const offset = (msg['offset'] as number | undefined) ?? 0;
    this.state.hexData   = data;
    this.state.hexOffset = offset;
    this.hexUpdated$.next({
      data,
      offset,
      highlightAddr:   msg['highlight_addr']   as number | undefined,
      highlightLength: msg['highlight_length'] as number | undefined,
    });
  }

  private _onSearchResults(msg: Record<string, unknown>): void {
    this.state.searchMatches = (msg['matches'] as SearchMatch[] | undefined) ?? [];
    this.searchResultsReceived$.next(this.state.searchMatches);
  }

  private _onStatus(msg: Record<string, unknown>): void {
    const instructionAnalysis = msg['instructionAnalysis'] as Record<string, string> | undefined;

    // If the status carries live analysis comments, patch the affected rows in
    // every loaded page so the analysis column reflects real register values.
    if (instructionAnalysis) {
      this._applyAnalysisUpdates(instructionAnalysis);
    }

    this.statusReceived$.next({
      paused:               msg['paused']               as boolean,
      pc:                   msg['pc']                   as number,
      breakpointHitAddress: msg['breakpointHitAddress'] as number | undefined,
      registers:            msg['registers']            as RegisterData,
      stackBytes:           msg['stackBytes']           as number[],
      stackBase:            msg['stackBase']            as number,
      instructionAnalysis,
    });
  }

  /**
   * Update loaded page rows whose address matches an entry in `analysis`.
   * The address key uses the 8-digit upper-case hex form (e.g. "80001234").
   * After patching, fires `analysisUpdated$` so the component can re-render.
   */
  private _applyAnalysisUpdates(analysis: Record<string, string>): void {
    let anyUpdated = false;
    const activePageStart = this.getPageStart(this.state.cursor);
    for (const [addrHex, comment] of Object.entries(analysis)) {
      const addr = parseInt(addrHex, 16);
      // Compute the instruction index relative to the base address.
      const physAddr  = this._toPhysAddr(addr);
      const physBase  = this._toPhysAddr(this.state.baseAddress);
      const instrIdx  = Math.floor((physAddr - physBase) / 4);
      if (instrIdx < 0 || instrIdx >= this.state.totalInstructions) { continue; }
      const pageStart = this.getPageStart(instrIdx);
      const page      = this.state.loadedPages.get(pageStart);
      if (!page) { continue; }
      const local = instrIdx - pageStart;
      if (local >= 0 && local < page.length) {
        page[local] = { ...page[local], comment };
        // Keep the active disasmRows array in sync when this is the current page.
        if (pageStart === activePageStart) {
          this.state.disasmRows[local] = page[local];
        }
        anyUpdated = true;
      }
    }
    if (anyUpdated) {
      this.analysisUpdated$.next(analysis);
    }
  }

  /** Translate any virtual/logical EE address to its physical equivalent.
   *  KSEG0/KSEG1 (0x80000000–0xBFFFFFFF): strip the top 3 bits.
   *  EE RAM uncached mirror (0x20100000–0x21FFFFFF): subtract 0x20000000.
   *  EE RAM uncached+accelerated (0x30100000–0x31FFFFFF): subtract 0x30000000.
   *  All other addresses (physical or special like scratchpad) pass through. */
  private _toPhysAddr(addr: number): number {
    const v = addr >>> 0;
    if (v >= 0x80000000 && v <= 0xBFFFFFFF) return (v & 0x1FFFFFFF) >>> 0;
    if (v >= 0x20100000 && v <= 0x21FFFFFF) return (v - 0x20000000) >>> 0;
    if (v >= 0x30100000 && v <= 0x31FFFFFF) return (v - 0x30000000) >>> 0;
    return v;
  }

  private _onReset(): void {
    this.state.breakpointHitAddress = undefined;
    this.resetReceived$.next();
  }

  private _onSymbols(msg: Record<string, unknown>): void {
    this.state.symLabels    = (msg['labels']    as SymLabel[]    | undefined) ?? [];
    this.state.symStrings   = (msg['strings']   as SymString[]   | undefined) ?? [];
    this.state.symFunctions = (msg['functions'] as SymFunction[] | undefined) ?? [];
    this.symbolsReceived$.next();
  }

  private _onLabelsUpdated(msg: Record<string, unknown>): void {
    this.state.symLabels = (msg['labels'] as SymLabel[] | undefined) ?? [];
    this.labelsUpdated$.next();
  }

  private _onStringsUpdated(msg: Record<string, unknown>): void {
    this.state.symStrings = (msg['strings'] as SymString[] | undefined) ?? [];
    this.stringsUpdated$.next();
  }

  private _onFunctionsUpdated(msg: Record<string, unknown>): void {
    this.state.symFunctions = (msg['functions'] as SymFunction[] | undefined) ?? [];
    this.functionsUpdated$.next();
  }

  private _onPointersUpdated(msg: Record<string, unknown>): void {
    this.state.symPointers = (msg['pointers'] as SymPointer[] | undefined) ?? [];
    this.pointersUpdated$.next();
  }

  private _onFloatsUpdated(msg: Record<string, unknown>): void {
    this.state.symFloats = (msg['floats'] as SymFloat[] | undefined) ?? [];
    this.floatsUpdated$.next();
  }

  private _onIntegersUpdated(msg: Record<string, unknown>): void {
    this.state.symIntegers = (msg['integers'] as SymInteger[] | undefined) ?? [];
    this.integersUpdated$.next();
  }

  private _onPointerLabelsUpdated(msg: Record<string, unknown>): void {
    this.state.pointerLabels = (msg['labels'] as SymLabel[] | undefined) ?? [];
    this.pointerLabelsUpdated$.next();
  }
}
