import { Injectable, OnDestroy, inject } from '@angular/core';
import { Subject, Subscription } from 'rxjs';
import { VsCodeBridgeService } from '../shared/vscode-bridge.service';
import { EditorState } from './state/editor.state';
import { findRowIndexForByteOffset } from './state/row-locator';
import {
  DisasmRow,
  type DataFormatKind,
  SearchMatch,
  SymLabel,
  ModuleEntry,
  ThreadEntry,
  RegisterData,
  PineWatchInfo,
} from './state/types';

// Re-export types for backward compatibility
export type {
  DisasmRow,
  DataFormatKind,
  SearchMatch,
  SymLabel,
  ThreadEntry,
  RegisterData,
  PineWatchInfo,
} from './state/types';

// ── Editor state ──────────────────────────────────────────────────────────────

export interface DisasmEditorState {
  /** Global instruction index of the cursor (one unit per 4 bytes). */
  cursor: number;
  /** Byte offset of the cursor inside its instruction word (0–3); see EditorState. */
  cursorByteOffset: number;
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
  isLive: boolean;
  breakpoints: Set<number>;
  breakpointHitAddress: number | undefined;
  /** Active PINE hardware watchpoints (synced from PCSX2 via PINELISTWATCHES). */
  pineWatches: PineWatchInfo[];
  /** The virtual PC address where the emulator is currently paused.  Tracked
   *  separately from `cursor` so that the PC indicator stays visible even
   *  after the user navigates the cursor away from the paused instruction. */
  pcAddress: number | undefined;
  symLabels: SymLabel[];
  /** Live-resolved labels from the Pointer Manager, keyed by normalised address. */
  pointerLabels: SymLabel[];
  /** Transient cheat-list annotations supplied by the Cheats Editor. */
  cheatLabels: SymLabel[];
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
    cursorByteOffset: 0,
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
    isLive: false,
    breakpoints: new Set<number>(),
    breakpointHitAddress: undefined,
    pineWatches: [],
    pcAddress: undefined,
    symLabels: [],
    pointerLabels: [],
    cheatLabels: [],
  };

  // ── Events the component (and future sub-components) subscribe to ─────────
  readonly initReceived$            = new Subject<InitReceivedEvent>();
  readonly pageLoaded$              = new Subject<{ startIndex: number; changed: boolean }>();
  readonly hexUpdated$              = new Subject<HexUpdatedEvent>();
  readonly searchResultsReceived$   = new Subject<SearchMatch[]>();
  readonly statusReceived$          = new Subject<StatusUpdatePayload>();
  readonly resetReceived$           = new Subject<void>();
  readonly symbolsReceived$         = new Subject<void>();
  readonly labelsUpdated$           = new Subject<void>();
  /** Fired when live analysis comments arrive for one or more addresses. */
  readonly analysisUpdated$         = new Subject<Record<string, string>>();
  /** Fired when live-resolved Pointer Manager labels arrive. */
  readonly pointerLabelsUpdated$    = new Subject<void>();
  /** Fired when transient Cheats Editor address annotations arrive. */
  readonly cheatLabelsUpdated$      = new Subject<void>();
  /** Fired when the extension host instructs this panel to navigate to an address. */
  readonly gotoAddressReceived$     = new Subject<{ address: number }>();
  /** Fired when another panel syncs its goto-input value to this panel. */
  readonly syncGotoInputReceived$   = new Subject<{ value: string }>();
  /** Fired when the extension host reports that no region was found for the requested address. */
  readonly gotoRegionNotFoundReceived$ = new Subject<{ address: number }>();
  /** Fired when the extension host returns the EE OS thread list. */
  readonly threadsReceived$ = new Subject<ThreadEntry[]>();
  /** Fired when the extension host returns the IOP module list. */
  readonly modulesReceived$ = new Subject<ModuleEntry[]>();
  /** Fired when the extension host reports that the panel became visible or hidden. */
  readonly panelVisibilityChanged$ = new Subject<{ visible: boolean }>();

  private readonly _subs: Subscription[] = [];

  // ── Signal-based state (new architecture) ─────────────────────────────────
  /** Inject EditorState for signal-based reactive updates. */
  readonly signals = inject(EditorState);

  constructor(private readonly _bridge: VsCodeBridgeService) {
    this._subs.push(
      this._bridge.on<Record<string, unknown>>('init').subscribe(msg => this._onInit(msg)),
      this._bridge.on<Record<string, unknown>>('disasmPage').subscribe(msg => this._onDisasmPage(msg)),
      this._bridge.on<Record<string, unknown>>('disasmRefreshSkipped').subscribe(msg => this._onDisasmRefreshSkipped(msg)),
      // Something that changes row content without changing memory happened (the
      // cross-reference index finished, the hint level changed): reload the
      // visible page.
      this._bridge.on<Record<string, unknown>>('reloadPage').subscribe(() => {
        if (this.state.isLive) {
          this._bridge.postMessage({ type: 'requestPage', startIndex: this.getPageStart(this.state.cursor) });
        }
      }),
      this._bridge.on<Record<string, unknown>>('hexview').subscribe(msg => this._onHexView(msg)),
      this._bridge.on<Record<string, unknown>>('searchResults').subscribe(msg => this._onSearchResults(msg)),
      this._bridge.on<Record<string, unknown>>('status').subscribe(msg => this._onStatus(msg)),
      this._bridge.on<Record<string, unknown>>('reset').subscribe(() => this._onReset()),
      this._bridge.on<Record<string, unknown>>('symbols').subscribe(msg => this._onSymbols(msg)),
      this._bridge.on<Record<string, unknown>>('labelsUpdated').subscribe(msg => this._onLabelsUpdated(msg)),
      this._bridge.on<Record<string, unknown>>('pointerLabelsUpdated').subscribe(msg => this._onPointerLabelsUpdated(msg)),
      this._bridge.on<Record<string, unknown>>('cheatLabelsUpdated').subscribe(msg => this._onCheatLabelsUpdated(msg)),
      this._bridge.on<Record<string, unknown>>('gotoAddress').subscribe(msg => this.gotoAddressReceived$.next({ address: msg['address'] as number })),
      this._bridge.on<Record<string, unknown>>('syncGotoInput').subscribe(msg => this.syncGotoInputReceived$.next({ value: msg['value'] as string })),
      this._bridge.on<Record<string, unknown>>('gotoRegionNotFound').subscribe(msg => this.gotoRegionNotFoundReceived$.next({ address: msg['address'] as number })),
      this._bridge.on<Record<string, unknown>>('threads').subscribe(msg => this.threadsReceived$.next((msg['threads'] as ThreadEntry[] | undefined) ?? [])),
      this._bridge.on<Record<string, unknown>>('modules').subscribe(msg => this.modulesReceived$.next((msg['modules'] as ModuleEntry[] | undefined) ?? [])),
      this._bridge.on<Record<string, unknown>>('panelVisibility').subscribe(msg => this.panelVisibilityChanged$.next({ visible: msg['visible'] as boolean })),
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

  /**
   * Persist a data-format override that re-renders `[address, address+length)`
   * as the given typed directive (`.byte`/`.half`/`.word`/`.dword`/`.qword`/`.float`/`.double`).
   * The extension host then refreshes the affected page.
   */
  setDataFormat(
    address: number,
    length: number,
    kind: DataFormatKind,
    startIndex?: number,
  ): void {
    this._bridge.postMessage({
      type: 'setDataFormat',
      address,
      length,
      kind,
      startIndex,
    });
  }

  /**
   * Remove every data-format override that intersects
   * `[address, address+length)`, restoring default 4-byte instruction
   * decoding for the affected rows.
   */
  clearDataFormat(
    address: number,
    length: number,
    startIndex?: number,
  ): void {
    this._bridge.postMessage({
      type: 'clearDataFormat',
      address,
      length,
      startIndex,
    });
  }

  // ── Message handlers ───────────────────────────────────────────────────────

  private _onInit(msg: Record<string, unknown>): void {
    const totalInstructions = msg['totalInstructions'] as number;
    const pageSize = msg['pageSize'] as number;
    const baseAddress = msg['baseAddress'] as number;
    const totalBytes = (msg['totalBytes'] as number | undefined) ?? (totalInstructions * 4);
    const regionLabel = (msg['regionLabel'] as string | undefined) ?? '';
    const isLive = !!(msg['isLive']);

    // Update mutable state (legacy)
    this.state.totalInstructions = totalInstructions;
    this.state.pageSize          = pageSize;
    this.state.baseAddress       = baseAddress;
    this.state.totalBytes        = totalBytes;
    this.state.regionLabel       = regionLabel;
    this.state.loadedPages.clear();
    this.state.disasmRows        = [];
    this.state.cursor            = 0;
    this.state.cursorByteOffset  = 0;
    this.state.isLive            = isLive;

    // Update signals (new architecture)
    this.signals.totalInstructions.set(totalInstructions);
    this.signals.pageSize.set(pageSize);
    this.signals.baseAddress.set(baseAddress);
    this.signals.totalBytes.set(totalBytes);
    this.signals.regionLabel.set(regionLabel);
    this.signals.loadedPages.set(new Map());
    this.signals.cursor.set(0);
    this.signals.cursorByteOffset.set(0);
    this.signals.isLive.set(isLive);
    this.signals.navHistory.set([]);

    if (totalInstructions > 0) {
      this.requestPage(this.getPageStart(0));
    }

    // Re-register any breakpoints the user had set in the editor with the
    // emulator server.  After a restart or reconnect, the server-side
    // breakpoint set is empty; re-sending here keeps the webview and the
    // server in sync without requiring the user to re-add every breakpoint.
    if (isLive && this.state.breakpoints.size > 0) {
      for (const addr of this.state.breakpoints) {
        this._bridge.postMessage({ type: 'setBreakpoint', address: addr });
      }
    }

    this.initReceived$.next({ isLive, regionLabel });
  }

  private _onDisasmPage(msg: Record<string, unknown>): void {
    const startIndex = msg['startIndex'] as number;
    const rows       = msg['rows']       as DisasmRow[];

    // Stabilize row-object references: reuse existing objects where content is
    // identical so that Angular CDK virtual scroll does not push new `data`
    // signal values into DisasmRowComponent instances on every live-refresh
    // tick.  When every row is unchanged, `stableRows` is the exact same array
    // reference as `existing`, which lets lastGridState's p1/p2 cache hit and
    // skip gridRows.set() entirely — zero DOM re-renders on a quiet tick.
    const existing  = this.state.loadedPages.get(startIndex);
    let stableRows: DisasmRow[];
    if (existing && existing.length === rows.length) {
      let allSame = true;
      const mapped = rows.map((r, i) => {
        const e = existing[i];
        if (e
          && e.addr    === r.addr
          && e.op      === r.op
          && e.args    === r.args
          && e.bytes   === r.bytes
          && e.label   === r.label
          && e.comment === r.comment
          && e.formatted === r.formatted
          && e.dataFormatKind === r.dataFormatKind) {
          return e;
        }
        allSame = false;
        return r;
      });
      stableRows = allSame ? existing : mapped;
    } else {
      stableRows = rows;
    }

    // Update mutable state (legacy)
    this.state.loadedPages.set(startIndex, stableRows);
    if (startIndex === this.getPageStart(this.state.cursor)) {
      this.state.disasmRows = stableRows;
    }

    // Update signals (new architecture) — skip when content is unchanged to
    // avoid creating a new Map reference that could cascade unnecessary work.
    if (stableRows !== existing) {
      this.signals.loadedPages.update(pages => {
        const newPages = new Map(pages);
        newPages.set(startIndex, stableRows);
        return newPages;
      });
    }

    this.pageLoaded$.next({ startIndex, changed: stableRows !== existing });
  }

  private _onDisasmRefreshSkipped(msg: Record<string, unknown>): void {
    const startIndex = msg['startIndex'] as number;
    this.pageLoaded$.next({ startIndex, changed: false });
  }

  private _onHexView(msg: Record<string, unknown>): void {
    const data   = msg['data']   as number[];
    const offset = (msg['offset'] as number | undefined) ?? 0;
    const highlightAddr = msg['highlight_addr'] as number | undefined;
    const highlightLength = msg['highlight_length'] as number | undefined;

    // Update mutable state (legacy)
    this.state.hexData   = data;
    this.state.hexOffset = offset;

    // Update signals (new architecture)
    this.signals.hexData.set(data);
    this.signals.hexOffset.set(offset);
    if (highlightAddr !== undefined) {
      this.signals.hexHighlightAddr.set(highlightAddr);
    }
    if (highlightLength !== undefined) {
      this.signals.hexHighlightLength.set(highlightLength);
    }

    this.hexUpdated$.next({ data, offset, highlightAddr, highlightLength });
  }

  private _onSearchResults(msg: Record<string, unknown>): void {
    const matches = (msg['matches'] as SearchMatch[] | undefined) ?? [];

    // Update mutable state (legacy)
    this.state.searchMatches = matches;

    // Update signals (new architecture)
    this.signals.searchMatches.set(matches);
    if (matches.length > 0) {
      this.signals.currentMatchIndex.set(0);
    } else {
      this.signals.currentMatchIndex.set(-1);
    }

    this.searchResultsReceived$.next(matches);
  }

  private _onStatus(msg: Record<string, unknown>): void {
    const instructionAnalysis = msg['instructionAnalysis'] as Record<string, string> | undefined;
    const paused = msg['paused'] as boolean;
    const pc = msg['pc'] as number;
    const breakpointHitAddress = msg['breakpointHitAddress'] as number | undefined;
    const registers = msg['registers'] as RegisterData;
    const stackBytes = msg['stackBytes'] as number[];
    const stackBase = msg['stackBase'] as number;

    // Update signals (new architecture)
    this.signals.isPaused.set(paused);
    if (paused) {
      this.signals.pcAddress.set(pc);
      this.signals.breakpointHitAddress.set(breakpointHitAddress);
      this.signals.registers.set(registers);
      this.signals.stackBytes.set(stackBytes);
      this.signals.stackBase.set(stackBase);
    } else {
      this.signals.pcAddress.set(undefined);
      this.signals.breakpointHitAddress.set(undefined);
    }

    // If the status carries live analysis comments, patch the affected rows in
    // every loaded page so the analysis column reflects real register values.
    if (instructionAnalysis) {
      this._applyAnalysisUpdates(instructionAnalysis);
    }

    this.statusReceived$.next({
      paused,
      pc,
      breakpointHitAddress,
      registers,
      stackBytes,
      stackBase,
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
    const updatedPages = new Map<number, DisasmRow[]>();

    for (const [addrHex, comment] of Object.entries(analysis)) {
      const addr = parseInt(addrHex, 16);
      // Compute the instruction index relative to the base address.
      const physAddr  = this._toPhysAddr(addr);
      const physBase  = this._toPhysAddr(this.state.baseAddress);
      const instrIdx  = Math.floor((physAddr - physBase) / 4);
      if (instrIdx < 0 || instrIdx >= this.state.totalInstructions) { continue; }
      const pageStart = this.getPageStart(instrIdx);
      const page      = updatedPages.get(pageStart) ?? this.state.loadedPages.get(pageStart);
      if (!page) { continue; }
      // Rows aren't one per 4 bytes once a page holds .byte/.half/string rows,
      // so find the row by its address rather than by index.
      const local = findRowIndexForByteOffset(page, this.state.baseAddress, physAddr - physBase);
      if (local >= 0 && local < page.length) {
        if ((page[local].comment ?? '') === (comment ?? '')) { continue; }
        const nextPage = page.slice();
        nextPage[local] = { ...nextPage[local], comment };
        updatedPages.set(pageStart, nextPage);
        anyUpdated = true;
      }
    }

    if (anyUpdated) {
      for (const [pageStart, page] of updatedPages) {
        this.state.loadedPages.set(pageStart, page);
        if (pageStart === activePageStart) {
          this.state.disasmRows = page;
        }
      }
      this.signals.loadedPages.update(pages => {
        const newPages = new Map(pages);
        for (const [pageStart, page] of updatedPages) {
          newPages.set(pageStart, page);
        }
        return newPages;
      });
      this.analysisUpdated$.next(analysis);
    }
  }

  /** Translate any virtual/logical EE address to its physical equivalent.
   *  KSEG0/KSEG1 (0x80000000–0xBFFFFFFF): strip the top 3 bits.
   *  EE RAM uncached mirror (0x20000000–0x21FFFFFF): subtract 0x20000000.
   *  EE RAM uncached+accelerated (0x30000000–0x31FFFFFF): subtract 0x30000000.
   *  All other addresses (physical or special like scratchpad) pass through. */
  private _toPhysAddr(addr: number): number {
    const v = addr >>> 0;
    if (v >= 0x80000000 && v <= 0xBFFFFFFF) return (v & 0x1FFFFFFF) >>> 0;
    if (v >= 0x20000000 && v <= 0x21FFFFFF) return (v - 0x20000000) >>> 0;
    if (v >= 0x30000000 && v <= 0x31FFFFFF) return (v - 0x30000000) >>> 0;
    return v;
  }

  private _onReset(): void {
    // Update mutable state (legacy)
    this.state.breakpointHitAddress = undefined;

    // Update signals (new architecture)
    this.signals.breakpointHitAddress.set(undefined);
    this.signals.pcAddress.set(undefined);
    this.signals.isPaused.set(false);
    this.signals.loadedPages.set(new Map());
    this.signals.navHistory.set([]);

    this.resetReceived$.next();
  }

  private _onSymbols(msg: Record<string, unknown>): void {
    const labels = (msg['labels'] as SymLabel[] | undefined) ?? [];

    // Update mutable state (legacy)
    this.state.symLabels    = labels;

    // Update signals (new architecture)
    this.signals.symLabels.set(labels);

    this.symbolsReceived$.next();
  }

  private _onLabelsUpdated(msg: Record<string, unknown>): void {
    const labels = (msg['labels'] as SymLabel[] | undefined) ?? [];
    this.state.symLabels = labels;
    this.signals.symLabels.set(labels);
    this.labelsUpdated$.next();
  }

  private _onPointerLabelsUpdated(msg: Record<string, unknown>): void {
    const labels = (msg['labels'] as SymLabel[] | undefined) ?? [];
    this.state.pointerLabels = labels;
    this.signals.pointerLabels.set(labels);
    this.pointerLabelsUpdated$.next();
  }

  private _onCheatLabelsUpdated(msg: Record<string, unknown>): void {
    const labels = (msg['labels'] as SymLabel[] | undefined) ?? [];
    this.state.cheatLabels = labels;
    this.cheatLabelsUpdated$.next();
  }
}
