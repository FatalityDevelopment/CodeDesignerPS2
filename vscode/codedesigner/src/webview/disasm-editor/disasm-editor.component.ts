import { Component, AfterViewInit, NgZone, ViewEncapsulation, inject, signal, viewChild, effect, WritableSignal, Injector, runInInjectionContext } from '@angular/core';
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
import { EditorState } from './state/editor.state';
import { SearchMode, DisasmRow as DisasmRowType, HistoryEntry, CallStackEntry, ModuleEntry, PineWatchInfo } from './state/types';
import { HexViewComponent } from './components/hex-view.component';
import { SearchPanelComponent } from './components/search-panel.component';
import { AddressInputPanelComponent } from './components/address-input-panel.component';
import { VirtualGridComponent } from './components/virtual-grid.component';
import { ContextMenuComponent, ContextMenuData } from './components/context-menu.component';
import { DebugPanelComponent } from './components/debug-panel.component';
import { BottomPanelComponent, BottomTab } from './components/bottom-panel.component';
import { SymbolsPanelComponent, SymbolTab } from './components/symbols-panel.component';
import { EditOverlayComponent, EditOverlayOpenData, EditOverlaySaveEvent } from './components/edit-overlay.component';
import { ConverterOverlayComponent, ConvertRequest, ConvertResult } from './components/converter-overlay.component';
import { PorterOverlayComponent, PortRequest, PortResultPayload } from './components/porter-overlay.component';
import { MemoryCardOverlayComponent, McListResult, McActionResult } from './components/memory-card-overlay.component';
import { HddOverlayComponent, HddListResult, HddActionResult } from './components/hdd-overlay.component';
import { ScannerOverlayComponent, ScanRequest, ScanResult } from './components/scanner-overlay.component';
import { StructOffsetOverlayComponent, StructEntryData, AddStructEvent, AddStructMemberEvent } from './components/struct-offset-overlay.component';
import { LabelManagerOverlayComponent, LabelManagerAction, LabelSetPayload } from './components/label-manager-overlay.component';
import './disasm-editor.css';

@Component({
  selector: 'app-disasm-editor',
  standalone: true,
  imports: [HexViewComponent, SearchPanelComponent, AddressInputPanelComponent, VirtualGridComponent, ContextMenuComponent, DebugPanelComponent, BottomPanelComponent, SymbolsPanelComponent, EditOverlayComponent, ConverterOverlayComponent, PorterOverlayComponent, MemoryCardOverlayComponent, HddOverlayComponent, ScannerOverlayComponent, StructOffsetOverlayComponent, LabelManagerOverlayComponent],
  providers: [DisasmEditorService],
  encapsulation: ViewEncapsulation.None,
  templateUrl: './disasm-editor.component.html'
})
export class DisasmEditorComponent implements AfterViewInit {

  // ── Hex view @Input() bindings ─────────────────────────────────────────────
  // Updated via NgZone.run() from the outside-zone editor logic so Angular
  // change detection propagates them to HexViewComponent.ngOnChanges().
  hexViewData: number[] = [];
  hexViewOffset = 0;
  hexViewHighlightAddr = -1;
  hexViewHighlightLength = 4;

  // ═══════════════════════════════════════════════════════════════════════════
  // Virtual Grid State (Phase 5)
  // ═══════════════════════════════════════════════════════════════════════════

  /** Virtual grid is now the only active grid implementation. */
  protected readonly useVirtualGrid = signal(true);

  /** Reference to virtual grid component */
  private readonly virtualGridRef = viewChild<VirtualGridComponent>('virtualGrid');

  /** Current page rows for virtual grid */
  protected readonly gridRows = signal<DisasmRowType[]>([]);

  /** Current page start index */
  protected readonly gridPageStart = signal(0);

  /** Pointer label map for address resolution */
  protected readonly gridPointerLabelMap = signal(new Map<string, string>());

  // Virtual grid actions (set by initEditorLogic)
  private readonly gridRuntime = {
    selectRow: null as ((globalIndex: number) => void) | null,
    editRow: null as ((row: DisasmRowType, address: number, addressHex: string) => void) | null,
    navigateTarget: null as (() => void) | null,
    navigateBack: null as (() => void) | null,
    toggleBreakpoint: null as (() => void) | null,
    cursorMove: null as ((delta: number) => void) | null,
  };

  // ═══════════════════════════════════════════════════════════════════════════
  // Context Menu State (Phase 6)
  // ═══════════════════════════════════════════════════════════════════════════

  /** Reference to context menu component */
  private readonly contextMenuRef = viewChild<ContextMenuComponent>('contextMenu');

  /** Current context menu row data (for actions) */
  private _ctxRow: DisasmRowType | null = null;
  private _ctxGlobalIndex = -1;

  // Context menu actions (set by initEditorLogic)
  private readonly contextMenuRuntime = {
    setBreakpoint: null as (() => void) | null,
    removeBreakpoint: null as (() => void) | null,
    addWatch: null as (() => void) | null,
    removeWatch: null as (() => void) | null,
    gotoTarget: null as (() => void) | null,
    addToLabelManager: null as (() => void) | null,
  };

  // ═══════════════════════════════════════════════════════════════════════════
  // Debug Panel State (Phase 7)
  // ═══════════════════════════════════════════════════════════════════════════

  /** Debug panel visibility state */
  protected readonly debugPanelVisible = signal(false);

  /** Register data for debug panel */
  protected readonly debugPanelRegisters = signal<RegisterData | null>(null);

  /** Stack bytes for debug panel */
  protected readonly debugPanelStackBytes = signal<number[]>([]);

  /** Stack base address for debug panel */
  protected readonly debugPanelStackBase = signal(0);

  /** Watches for debug panel Inspect tab */
  protected readonly debugPanelWatches = signal<PineWatchInfo[]>([]);

  // Debug panel callback (set by initEditorLogic)
  private _debugEditRegisterCallback: ((regType: string, regIdx: number, currentValue: string) => void) | null = null;

  // ═══════════════════════════════════════════════════════════════════════════
  // Bottom Panel State (Phase 8)
  // ═══════════════════════════════════════════════════════════════════════════

  /** Bottom panel visibility state */
  protected readonly bottomPanelVisible = signal(false);

  /** Call stack frames for bottom panel */
  protected readonly bottomPanelCallStack = signal<import('./state/types').CallStackEntry[]>([]);

  /** Threads for bottom panel */
  protected readonly bottomPanelThreads = signal<ThreadEntry[]>([]);

  /** IOP modules for bottom panel */
  protected readonly bottomPanelModules = signal<ModuleEntry[]>([]);

  /** Breakpoints for bottom panel */
  protected readonly bottomPanelBreakpoints = signal<Set<number>>(new Set());

  // Bottom panel actions (set by initEditorLogic)
  private readonly bottomPanelRuntime = {
    navigate: null as ((addr: number) => void) | null,
    refresh: null as ((tab: BottomTab) => void) | null,
    removeBreakpoint: null as ((addr: number) => void) | null,
    clearAllBreakpoints: null as (() => void) | null,
  };

  // ═══════════════════════════════════════════════════════════════════════════
  // Symbols Panel State (Phase 9)
  // ═══════════════════════════════════════════════════════════════════════════

  /** Symbols panel visibility state */
  protected readonly symbolsPanelVisible = signal(false);

  /** Region label for symbols panel */
  protected readonly symbolsPanelRegionLabel = signal('');

  /** Labels for symbols panel */
  protected readonly symbolsPanelLabels = signal<import('./state/types').SymLabel[]>([]);

  /** Strings for symbols panel */
  protected readonly symbolsPanelStrings = signal<import('./state/types').SymString[]>([]);

  /** Functions for symbols panel */
  protected readonly symbolsPanelFunctions = signal<import('./state/types').SymFunction[]>([]);

  /** Pointers for symbols panel */
  protected readonly symbolsPanelPointers = signal<SymPointer[]>([]);

  /** Floats for symbols panel */
  protected readonly symbolsPanelFloats = signal<SymFloat[]>([]);

  /** Integers for symbols panel */
  protected readonly symbolsPanelIntegers = signal<SymInteger[]>([]);

  /** Scan status strings */
  protected readonly symbolsPanelStringsStatus = signal('');
  protected readonly symbolsPanelFunctionsStatus = signal('');
  protected readonly symbolsPanelPointersStatus = signal('');
  protected readonly symbolsPanelFloatsStatus = signal('');
  protected readonly symbolsPanelIntegersStatus = signal('');

  // Symbols panel actions (set by initEditorLogic)
  private readonly symbolsPanelRuntime = {
    close: null as (() => void) | null,
    refresh: null as (() => void) | null,
    navigate: null as ((addr: string) => void) | null,
    addLabel: null as ((address: string, name: string, comment: string) => void) | null,
    deleteLabel: null as ((addr: string) => void) | null,
    scanStrings: null as (() => void) | null,
    scanFunctions: null as (() => void) | null,
    scanPointers: null as (() => void) | null,
    scanFloats: null as (() => void) | null,
    scanIntegers: null as (() => void) | null,
  };

  private readonly liveRuntime = {
    refreshTimer: null as ReturnType<typeof setInterval> | null,
    statusTimer: null as ReturnType<typeof setInterval> | null,
    refreshPending: false,
    paused: false,
  };

  private pendingPageScrollBlock: ScrollLogicalPosition | null = null;
  private lastGridState: { pageStart: number; p1: DisasmRow[] | undefined; p2: DisasmRow[] | undefined } | null = null;
  /** Last synced breakpoints size — avoids creating a new Set on every live-refresh tick. */
  private _lastBreakpointsSize = -1;
  /** Last synced search-matches length — avoids creating a new Array on every tick. */
  private _lastSearchMatchesLength = -1;

  // ═══════════════════════════════════════════════════════════════════════════
  // Edit Overlay State (Phase 10)
  // ═══════════════════════════════════════════════════════════════════════════

  /** Disasm preview text (set by message handler) */
  protected readonly editOverlayDisasmPreview = signal('');

  /** External hex bytes (from assembleInstrResult) */
  protected readonly editOverlayExternalHexBytes = signal('');

  /** External instruction preview (from disasmBytesResult) */
  protected readonly editOverlayExternalInstrPreview = signal('');

  /** Reference to edit overlay component */
  private readonly editOverlayRef = viewChild<EditOverlayComponent>('editOverlayComp');

  // ═══════════════════════════════════════════════════════════════════════════
  // Converter Overlay State (Phase 10)
  // ═══════════════════════════════════════════════════════════════════════════

  /** Reference to converter overlay component */
  private readonly converterOverlayRef = viewChild<ConverterOverlayComponent>('converterOverlayComp');

  // ═══════════════════════════════════════════════════════════════════════════
  // Porter Overlay State (Phase 10)
  // ═══════════════════════════════════════════════════════════════════════════

  /** Reference to porter overlay component */
  private readonly porterOverlayRef = viewChild<PorterOverlayComponent>('porterOverlayComp');

  // ═══════════════════════════════════════════════════════════════════════════
  // Memory Card Overlay State
  // ═══════════════════════════════════════════════════════════════════════════

  /** Reference to memory card overlay component */
  private readonly mcOverlayRef = viewChild<MemoryCardOverlayComponent>('mcOverlayComp');

  // ═══════════════════════════════════════════════════════════════════════════
  // HDD Overlay State
  // ═══════════════════════════════════════════════════════════════════════════

  /** Reference to HDD overlay component */
  private readonly hddOverlayRef = viewChild<HddOverlayComponent>('hddOverlayComp');

  // ═══════════════════════════════════════════════════════════════════════════
  // Scanner Overlay State
  // ═══════════════════════════════════════════════════════════════════════════

  /** Reference to scanner overlay component */
  private readonly scannerOverlayRef = viewChild<ScannerOverlayComponent>('scannerOverlayComp');

  // ═══════════════════════════════════════════════════════════════════════════
  // Struct Offset Overlay State
  // ═══════════════════════════════════════════════════════════════════════════

  /** Reference to struct offset overlay component */
  private readonly structOverlayRef = viewChild<StructOffsetOverlayComponent>('structOverlayComp');

  // ═══════════════════════════════════════════════════════════════════════════
  // Label Manager Overlay State
  // ═══════════════════════════════════════════════════════════════════════════

  /** Reference to label manager overlay component */
  private readonly labelManagerRef = viewChild<LabelManagerOverlayComponent>('labelManagerComp');

  /** Signal-based state (new architecture) */
  protected readonly signals = inject(EditorState);

  /** VS Code API reference for posting messages */
  private readonly vscode = getVsCodeApi();
  private readonly injector = inject(Injector);
  constructor(private zone: NgZone, private svc: DisasmEditorService) {}

  // ═══════════════════════════════════════════════════════════════════════════
  // Search Panel Event Handlers
  // ═══════════════════════════════════════════════════════════════════════════

  protected onSearch(event: { query: string; mode: SearchMode }): void {
    this.svc.state.searchQuery = event.query;
    this.svc.state.searchMode = event.mode;
    this.signals.searchQuery.set(event.query);
    this.signals.searchMode.set(event.mode);
    this.vscode.postMessage({ type: 'search', query: event.query, mode: event.mode });
  }

  protected onSearchClear(): void {
    this.svc.state.searchMatches = [];
    this.svc.state.currentMatch = -1;
    this.svc.state.searchQuery = '';
    this.signals.searchMatches.set([]);
    this.signals.currentMatchIndex.set(-1);
    this.signals.searchQuery.set('');
  }

  protected onSearchClose(): void {
    this.svc.state.searchActive = false;
    this.signals.panels.search.set(false);
    this.onSearchClear();
    this.focusVirtualGrid();
  }

  protected onPrevMatch(): void {
    this.searchRuntime.prevMatch?.();
  }

  protected onNextMatch(): void {
    this.searchRuntime.nextMatch?.();
  }

  // Search/goto actions (set by initEditorLogic)
  private readonly searchRuntime = {
    prevMatch: null as (() => void) | null,
    nextMatch: null as (() => void) | null,
  };

  private readonly gotoRuntime = {
    navigate: null as ((addr: number) => void) | null,
  };

  // ═══════════════════════════════════════════════════════════════════════════
  // Goto Panel State & Handlers
  // ═══════════════════════════════════════════════════════════════════════════

  /** Status message for goto panel */
  protected readonly gotoStatus = signal('');
  /** Pre-filled value for goto input (used by external navigation) */
  protected readonly gotoValue = signal('');

  protected onGotoAddress(addr: number): void {
    this.gotoRuntime.navigate?.(addr);
  }

  protected onGotoValueChange(value: string): void {
    this.gotoValue.set(value);
    this.vscode.postMessage({ type: 'syncGotoInput', value });
  }

  protected onGotoClose(): void {
    this.svc.state.gotoActive = false;
    this.signals.panels.goto.set(false);
    this.gotoStatus.set('');
    this.gotoValue.set('');
    this.focusVirtualGrid();
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Set PC Panel State & Handlers
  // ═══════════════════════════════════════════════════════════════════════════

  /** Status message for setpc panel */
  protected readonly setpcStatus = signal('');

  protected onSetPc(addr: number): void {
    this.setpcStatus.set(`Setting PC to 0x${addr.toString(16).toUpperCase()}...`);
    this.vscode.postMessage({ type: 'setPC', address: addr });
  }

  protected onSetPcClose(): void {
    this.signals.panels.setpc.set(false);
    this.setpcStatus.set('');
    this.focusVirtualGrid();
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Virtual Grid Event Handlers (Phase 5)
  // ═══════════════════════════════════════════════════════════════════════════

  protected onGridSelectRow(globalIndex: number): void {
    this.gridRuntime.selectRow?.(globalIndex);
  }

  protected onGridEditRow(event: { row: DisasmRowType; address: number; addressHex: string }): void {
    this.gridRuntime.editRow?.(event.row, event.address, event.addressHex);
  }

  protected onGridNavigateTarget(): void {
    this.gridRuntime.navigateTarget?.();
  }

  protected onGridNavigateBack(): void {
    this.gridRuntime.navigateBack?.();
  }

  protected onGridToggleBreakpoint(): void {
    this.gridRuntime.toggleBreakpoint?.();
  }

  protected onGridCursorMove(delta: number): void {
    this.gridRuntime.cursorMove?.(delta);
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Context Menu Event Handlers (Phase 6)
  // ═══════════════════════════════════════════════════════════════════════════

  protected onGridContextMenu(event: { event: MouseEvent; row: DisasmRowType; globalIndex: number }): void {
    // Store row data for action handlers
    this._ctxRow = event.row;
    this._ctxGlobalIndex = event.globalIndex;

    // Move cursor to the right-clicked row
    this.gridRuntime.selectRow?.(event.globalIndex);

    // Determine menu state
    const addr = parseInt(event.row.addr, 16);
    const hasBreakpoint = this.svc.state.breakpoints.has(addr);
    const hasWatch = this.svc.state.pineWatches.some((w: PineWatchInfo) => parseInt(w.Start, 16) === addr);
    const hasTarget = !!(event.row.args.match(/\$([0-9A-F]{8})/i) ||
                          event.row.args.match(/0x([0-9A-F]{8})/i));

    // Open the context menu
    const menuData: ContextMenuData = {
      address: event.row.addr,
      hasBreakpoint,
      hasWatch,
      hasTarget,
      currentLabel: event.row.label || undefined,
    };
    this.contextMenuRef()?.open(event.event.clientX, event.event.clientY, menuData);
  }

  protected onCtxSetBreakpoint(): void {
    this.contextMenuRuntime.setBreakpoint?.();
  }

  protected onCtxRemoveBreakpoint(): void {
    this.contextMenuRuntime.removeBreakpoint?.();
  }

  protected onCtxAddWatch(): void {
    this.contextMenuRuntime.addWatch?.();
  }

  protected onCtxRemoveWatch(): void {
    this.contextMenuRuntime.removeWatch?.();
  }

  protected onCtxGotoTarget(): void {
    this.contextMenuRuntime.gotoTarget?.();
  }

  protected onCtxAddToLabelManager(): void {
    this.contextMenuRuntime.addToLabelManager?.();
  }

  protected onCtxClosed(): void {
    this.focusVirtualGrid();
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Debug Panel Event Handlers (Phase 7)
  // ═══════════════════════════════════════════════════════════════════════════

  protected onDebugEditRegister(event: { regType: string; regIdx: number; currentValue: string }): void {
    this._debugEditRegisterCallback?.(event.regType, event.regIdx, event.currentValue);
  }

  protected onDebugPanelRemoveWatch(watch: PineWatchInfo): void {
    const addr = parseInt(watch.Start, 16) || 0;
    const end  = parseInt(watch.End,   16) || addr + 4;
    const cpu  = watch.Cpu === 'IOP' ? 1 : 0;
    this.vscode.postMessage({ type: 'removePineWatch', address: addr, end, cpu });
  }

  protected onDebugPanelClearAllWatches(): void {
    this.vscode.postMessage({ type: 'clearAllPineWatches' });
  }

  protected onDebugPanelNavigateWatch(addr: number): void {
    this.bottomPanelRuntime.navigate?.(addr);
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Bottom Panel Event Handlers (Phase 8)
  // ═══════════════════════════════════════════════════════════════════════════

  protected onBottomPanelNavigate(addr: number): void {
    this.bottomPanelRuntime.navigate?.(addr);
  }

  protected onBottomPanelRemoveBreakpoint(addr: number): void {
    this.bottomPanelRuntime.removeBreakpoint?.(addr);
  }

  protected onBottomPanelClearAllBreakpoints(): void {
    this.bottomPanelRuntime.clearAllBreakpoints?.();
  }

  protected onBottomPanelRefresh(tab: BottomTab): void {
    this.bottomPanelRuntime.refresh?.(tab);
  }

  protected onBottomPanelTabSelected(tab: BottomTab): void {
    if (tab === 'threads' && this.svc.state.isLive) {
      this.vscode.postMessage({ type: 'getThreads' });
    } else if (tab === 'modules' && this.svc.state.isLive) {
      this.vscode.postMessage({ type: 'getModules' });
    } else if (tab === 'watches' && this.svc.state.isLive) {
      this.vscode.postMessage({ type: 'listPineWatches' });
    }
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Symbols Panel Event Handlers (Phase 9)
  // ═══════════════════════════════════════════════════════════════════════════

  protected onSymbolsPanelClose(): void {
    this.symbolsPanelRuntime.close?.();
  }

  protected onSymbolsPanelRefresh(): void {
    this.symbolsPanelRuntime.refresh?.();
  }

  protected onSymbolsPanelNavigate(addr: string): void {
    this.symbolsPanelRuntime.navigate?.(addr);
  }

  protected onSymbolsPanelAddLabel(event: { address: string; name: string; comment: string }): void {
    this.symbolsPanelRuntime.addLabel?.(event.address, event.name, event.comment);
  }

  protected onSymbolsPanelDeleteLabel(addr: string): void {
    this.symbolsPanelRuntime.deleteLabel?.(addr);
  }

  protected onSymbolsPanelScanStrings(): void {
    this.symbolsPanelRuntime.scanStrings?.();
  }

  protected onSymbolsPanelScanFunctions(): void {
    this.symbolsPanelRuntime.scanFunctions?.();
  }

  protected onSymbolsPanelScanPointers(): void {
    this.symbolsPanelRuntime.scanPointers?.();
  }

  protected onSymbolsPanelScanFloats(): void {
    this.symbolsPanelRuntime.scanFloats?.();
  }

  protected onSymbolsPanelScanIntegers(): void {
    this.symbolsPanelRuntime.scanIntegers?.();
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Edit Overlay Event Handlers (Phase 10)
  // ═══════════════════════════════════════════════════════════════════════════

  protected onEditOverlaySave(event: EditOverlaySaveEvent): void {
    const origBytes = event.originalBytes;
    if (event.newBytes && event.newBytes !== origBytes && /^[0-9A-F]+$/.test(event.newBytes) && event.newBytes.length % 2 === 0) {
      this.vscode.postMessage({ type: 'patchBytes', address: event.address, hexBytes: event.newBytes });
    }
    this.vscode.postMessage({ type: 'saveLabel', address: event.address, addressHex: event.addressHex, newLabel: event.newLabel, newComment: event.newComment });
  }

  protected onEditOverlayClose(): void {
    this.signals.panels.editOverlay.set(false);
  }

  protected onAssembleInstr(event: { instruction: string; address: number }): void {
    this.vscode.postMessage({ type: 'assembleInstr', instruction: event.instruction, address: event.address });
  }

  protected onDisasmBytes(event: { hexBytes: string; address: number }): void {
    this.vscode.postMessage({ type: 'disasmBytes', hexBytes: event.hexBytes, address: event.address });
  }

  protected onRequestHistory(address: number): void {
    this.vscode.postMessage({ type: 'getAddressHistory', address });
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Converter Overlay Event Handlers (Phase 10)
  // ═══════════════════════════════════════════════════════════════════════════

  protected onConverterOverlayClose(): void {
    this.signals.panels.converterOverlay.set(false);
  }

  protected onConvertCheat(event: ConvertRequest): void {
    this.vscode.postMessage({ type: 'convertCheat', codes: event.codes, direction: event.direction, format: event.format });
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Porter Overlay Event Handlers (Phase 10)
  // ═══════════════════════════════════════════════════════════════════════════

  protected onPorterOverlayClose(): void {
    this.signals.panels.porterOverlay.set(false);
  }

  protected onPortCheats(event: PortRequest): void {
    this.vscode.postMessage({ type: 'portCheats', codes: event.codes, sourcePath: event.sourcePath, targetPath: event.targetPath });
  }

  protected onBrowseSourceDump(): void {
    this.vscode.postMessage({ type: 'browseSourceDump' });
  }

  protected onBrowseTargetDump(): void {
    this.vscode.postMessage({ type: 'browseTargetDump' });
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Memory Card Overlay Event Handlers
  // ═══════════════════════════════════════════════════════════════════════════

  protected onMcBrowse(): void {
    this.vscode.postMessage({ type: 'browseMc' });
  }

  protected onMcNew(): void {
    this.vscode.postMessage({ type: 'newMc' });
  }

  protected onMcList(mcPath: string): void {
    this.vscode.postMessage({ type: 'mcList', mcPath });
  }

  protected onMcExport(event: { mcPath: string; saveName: string }): void {
    this.vscode.postMessage({ type: 'mcExport', mcPath: event.mcPath, saveName: event.saveName });
  }

  protected onMcImport(mcPath: string): void {
    this.vscode.postMessage({ type: 'mcImport', mcPath });
  }

  protected onMcDelete(event: { mcPath: string; saveName: string }): void {
    this.vscode.postMessage({ type: 'mcDelete', mcPath: event.mcPath, saveName: event.saveName });
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // HDD Overlay Event Handlers
  // ═══════════════════════════════════════════════════════════════════════════

  protected onHddBrowse(): void {
    this.vscode.postMessage({ type: 'browseHdd' });
  }

  protected onHddNew(): void {
    this.vscode.postMessage({ type: 'newHdd' });
  }

  protected onHddList(hddPath: string): void {
    this.vscode.postMessage({ type: 'hddList', hddPath });
  }

  protected onHddExport(event: { hddPath: string; partitionId: string }): void {
    this.vscode.postMessage({ type: 'hddExport', hddPath: event.hddPath, partitionId: event.partitionId });
  }

  protected onHddViewFiles(event: { hddPath: string; partitionId: string }): void {
    this.vscode.postMessage({ type: 'hddListFiles', hddPath: event.hddPath, partitionId: event.partitionId });
  }

  protected onHddDelete(event: { hddPath: string; partitionId: string }): void {
    this.vscode.postMessage({ type: 'hddDelete', hddPath: event.hddPath, partitionId: event.partitionId });
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Scanner Overlay Event Handlers
  // ═══════════════════════════════════════════════════════════════════════════

  protected onScannerOverlayClose(): void {
    this.signals.panels.scannerOverlay.set(false);
  }

  protected onScanValue(event: ScanRequest): void {
    this.vscode.postMessage({
      type:              'scanValue',
      value:             event.value,
      comparison:        event.comparison,
      dataType:          event.dataType,
      startAddress:      event.startAddress,
      endAddress:        event.endAddress,
      region:            event.region,
      previousAddresses: event.previousAddresses,
    });
  }

  protected onScannerNavigate(address: string): void {
    const addr = parseInt(address, 16);
    if (!isNaN(addr)) {
      this.gotoRuntime.navigate?.(addr);
    }
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Struct Offset Overlay Event Handlers
  // ═══════════════════════════════════════════════════════════════════════════

  protected onStructOverlayClose(): void {
    this.signals.panels.structOverlay.set(false);
  }

  protected onStructLoadStructs(): void {
    this.vscode.postMessage({ type: 'getStructs' });
  }

  protected onSaveStruct(event: AddStructEvent): void {
    this.vscode.postMessage({ type: 'addStruct', name: event.name, baseAddress: event.baseAddress });
  }

  protected onAddStructMember(event: AddStructMemberEvent): void {
    this.vscode.postMessage({
      type:        'addStructMember',
      structName:  event.structName,
      offset:      event.offset,
      memberName:  event.memberName,
    });
  }

  protected onReadStructPointer(pointerAddress: string): void {
    this.vscode.postMessage({ type: 'readPointerAt', address: pointerAddress });
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Label Manager Overlay Event Handlers
  // ═══════════════════════════════════════════════════════════════════════════

  private openLabelManager(): void {
    this.signals.panels.labelManager.set(true);
    this.labelManagerRef()?.open();
  }

  protected onLabelManagerClose(): void {
    this.signals.panels.labelManager.set(false);
  }

  protected onLabelManagerAction(action: LabelManagerAction): void {
    this.vscode.postMessage({ type: 'labelManagerAction', action });
  }

  protected onLabelImportFile(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) { return; }
    const reader = new FileReader();
    reader.onload = (e) => {
      const json = e.target?.result as string;
      const setName = file.name.replace(/\.json$/i, '');
      this.vscode.postMessage({ type: 'labelManagerAction', action: { type: 'importSet', setName, json } });
    };
    reader.readAsText(file);
    (event.target as HTMLInputElement).value = '';
  }

  private focusVirtualGrid(): void {
    this.virtualGridRef()?.focus();
  }

  private bindSearchShortcuts(params: {
    state: DisasmEditorService['state'];
    toggleSearch: () => void;
    nextSearchMatch: () => void;
    prevSearchMatch: () => void;
  }): void {
    document.addEventListener('keydown', (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'f') {
        e.preventDefault();
        params.toggleSearch();
      }
      if (e.key === 'F3' && !e.shiftKey && params.state.searchActive) {
        e.preventDefault();
        params.nextSearchMatch();
      }
      if (e.key === 'F3' && e.shiftKey && params.state.searchActive) {
        e.preventDefault();
        params.prevSearchMatch();
      }
    });
  }

  private toggleGotoPanel(params: {
    state: DisasmEditorService['state'];
    signals: EditorState;
    gotoToggleBtn: HTMLElement;
    gotoValue: WritableSignal<string>;
    gotoStatus: WritableSignal<string>;
  }): void {
    params.state.gotoActive = !params.state.gotoActive;
    params.signals.panels.goto.set(params.state.gotoActive);
    params.gotoToggleBtn.classList.toggle('active', params.state.gotoActive);
    if (!params.state.gotoActive) {
      params.gotoValue.set('');
      params.gotoStatus.set('');
    }
  }

  private toggleSetPcPanel(params: {
    signals: EditorState;
    setpcToggleBtn: HTMLElement;
    setpcStatus: WritableSignal<string>;
  }): void {
    const active = !params.signals.panels.setpc();
    params.signals.panels.setpc.set(active);
    params.setpcToggleBtn.classList.toggle('active', active);
    if (!active) {
      params.setpcStatus.set('');
    }
  }

  private toggleSymbolsPanel(params: {
    state: DisasmEditorService['state'];
    signals: EditorState;
    symbolsPanelVisible: WritableSignal<boolean>;
    vscode: ReturnType<typeof getVsCodeApi>;
  }): void {
    params.state.symbolsActive = !params.state.symbolsActive;
    params.signals.panels.symbols.set(params.state.symbolsActive);
    params.symbolsPanelVisible.set(params.state.symbolsActive);
    if (params.state.symbolsActive) {
      params.vscode.postMessage({ type: 'getSymbols' });
    }
  }

  private toggleRegistersPanel(params: {
    signals: EditorState;
    registersToggleBtn: HTMLElement;
    debugPanelVisible: WritableSignal<boolean>;
    debugPanelRegisters: WritableSignal<RegisterData | null>;
    debugPanelStackBytes: WritableSignal<number[]>;
    debugPanelStackBase: WritableSignal<number>;
    createPlaceholderRegs: () => RegisterData;
  }): void {
    const isVisible = params.signals.panels.registers();
    if (isVisible) {
      params.signals.panels.registers.set(false);
      params.registersToggleBtn.classList.remove('active');
      params.debugPanelVisible.set(false);
      return;
    }

    params.signals.panels.registers.set(true);
    params.registersToggleBtn.classList.add('active');
    params.debugPanelVisible.set(true);
    if (!params.debugPanelRegisters()) {
      const ph = params.createPlaceholderRegs();
      params.debugPanelRegisters.set(ph);
      params.debugPanelStackBytes.set([]);
      params.debugPanelStackBase.set(0);
    }
  }

  private toggleBottomPanel(params: {
    signals: EditorState;
    bottomPanelToggleBtn: HTMLElement;
    bottomPanelVisible: WritableSignal<boolean>;
    bottomPanelBreakpoints: WritableSignal<Set<number>>;
    state: DisasmEditorService['state'];
  }): void {
    const nowVisible = !params.signals.panels.bottom();
    params.signals.panels.bottom.set(nowVisible);
    params.bottomPanelToggleBtn.classList.toggle('active', nowVisible);
    params.bottomPanelVisible.set(nowVisible);
    if (nowVisible) {
      params.bottomPanelBreakpoints.set(new Set(params.state.breakpoints));
    }
  }

  private bindGlobalShortcuts(params: {
    state: DisasmEditorService['state'];
    toggleGoto: () => void;
    toggleSymbolsPanel: () => void;
    toggleRegistersPanel: () => void;
    toggleBottomPanel: () => void;
    toggleSetPc: () => void;
    navigateBack: () => void;
  }): void {
    window.addEventListener('keydown', (e: KeyboardEvent) => {
      if (e.ctrlKey && e.altKey && !e.shiftKey && e.code === 'KeyA') {
        e.preventDefault(); e.stopPropagation(); params.toggleGoto();
      }
      if (e.ctrlKey && e.altKey && !e.shiftKey && e.code === 'KeyS') {
        e.preventDefault(); e.stopPropagation(); params.toggleSymbolsPanel();
      }
      if (e.ctrlKey && e.altKey && !e.shiftKey && e.code === 'KeyR') {
        e.preventDefault(); e.stopPropagation(); params.toggleRegistersPanel();
      }
      if (e.ctrlKey && e.altKey && !e.shiftKey && e.code === 'KeyT') {
        e.preventDefault(); e.stopPropagation(); params.toggleBottomPanel();
      }
      if (e.ctrlKey && e.altKey && !e.shiftKey && e.code === 'KeyP') {
        e.preventDefault(); e.stopPropagation();
        if (params.state.isLive) { params.toggleSetPc(); }
      }
      if (e.altKey && !e.ctrlKey && !e.shiftKey && e.code === 'ArrowLeft') {
        const active = document.activeElement as HTMLElement | null;
        const isEditing = active && (
          active.tagName === 'INPUT' || active.tagName === 'TEXTAREA' ||
          active.tagName === 'SELECT' || active.isContentEditable
        );
        if (!isEditing) {
          e.preventDefault(); e.stopPropagation(); params.navigateBack();
        }
      }
    });
  }

  private isCodePointerValue(addr: number): boolean {
    const v = addr >>> 0;
    if (v === 0 || (v & 3) !== 0) { return false; }
    const phys = this.toPhysicalAddress(v);
    return (phys < 0x02000000) || (phys >= 0x1FC00000 && phys < 0x20000000);
  }

  private parseHexAddress(input: string): number | null {
    const cleaned = input.trim().toLowerCase().replace(/^0x/, '');
    if (!/^[0-9a-f]+$/.test(cleaned)) { return null; }
    return parseInt(cleaned, 16);
  }

  private toPhysicalAddress(va: number): number {
    const v = va >>> 0;
    if (v >= 0x80000000 && v <= 0xBFFFFFFF) return ((v & 0x1FFFFFFF) >>> 0);
    if (v >= 0x20000000 && v <= 0x21FFFFFF) return ((v - 0x20000000) >>> 0);
    if (v >= 0x30000000 && v <= 0x31FFFFFF) return ((v - 0x30000000) >>> 0);
    return v;
  }

  private buildCallStackFrames(
    regs: RegisterData,
    stackBytes: number[],
    _stackBase: number,
    labels: Array<{ address: string; label: string }>,
  ): CallStackEntry[] {
    const frames: Array<{ pc: number }> = [];
    const currentPc = (regs.pc ?? 0) >>> 0;
    frames.push({ pc: currentPc });

    if (stackBytes && stackBytes.length > 0) {
      for (let i = 0; i < Math.min(stackBytes.length, 512); i += 4) {
        const word = ((stackBytes[i + 3] << 24) | (stackBytes[i + 2] << 16) | (stackBytes[i + 1] << 8) | stackBytes[i]) >>> 0;
        if (this.isCodePointerValue(word)) { frames.push({ pc: word }); }
      }
    }

    const lookupLabel = (pc: number): string | undefined => {
      let best: string | undefined;
      let bestDist = Infinity;
      for (const item of labels) {
        const addr = parseInt(String(item.address).replace(/^0x/i, ''), 16);
        if (!isNaN(addr) && addr <= pc && pc - addr < bestDist) {
          bestDist = pc - addr;
          best = item.label;
        }
      }
      return best;
    };

    return frames.map((frame, index) => ({ index, pc: frame.pc, functionLabel: lookupLabel(frame.pc) }));
  }

  private generateZeroPageRows(
    startIndex: number,
    pageSize: number,
    totalInstructions: number,
    baseAddress: number,
  ): DisasmRow[] {
    const rows: DisasmRow[] = [];
    const count = Math.min(pageSize, totalInstructions - startIndex);
    for (let i = 0; i < count; i++) {
      const addr = baseAddress + (startIndex + i) * 4;
      rows.push({ addr: addr.toString(16).toUpperCase().padStart(8, '0'), bytes: '00000000', label: '', op: 'nop', args: '', comment: '' });
    }
    return rows;
  }

  private createPlaceholderRegs(): RegisterData {
    return {
      pc: 0,
      hi: 0, lo: 0, hi1: 0, lo1: 0, sa: 0,
      gpr: new Array(32).fill(0),
      gprLo32: new Array(32).fill(0),
      gprHi32: new Array(32).fill(0),
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
      vu0i: new Array(32).fill(0),
      gsPriv: new Array(64).fill(0),
    };
  }

  private setLivePausedUi(headerToolbar: HTMLElement, paused: boolean): void {
    headerToolbar.classList.toggle('live-active', !paused);
    headerToolbar.classList.toggle('live-paused', paused);
  }

  private startLiveRefresh(vscode: ReturnType<typeof getVsCodeApi>, requestRefresh: () => void): void {
    if (this.liveRuntime.refreshTimer !== null || this.liveRuntime.paused) { return; }
    this.liveRuntime.refreshTimer = setInterval(requestRefresh, 1000);
    if (this.liveRuntime.statusTimer === null) {
      this.liveRuntime.statusTimer = setInterval(() => vscode.postMessage({ type: 'getStatus' }), 800);
    }
  }

  private stopLiveRefresh(): void {
    if (this.liveRuntime.refreshTimer !== null) {
      clearInterval(this.liveRuntime.refreshTimer);
      this.liveRuntime.refreshTimer = null;
    }
    if (this.liveRuntime.statusTimer !== null) {
      clearInterval(this.liveRuntime.statusTimer);
      this.liveRuntime.statusTimer = null;
    }
  }

  private restartLiveRefresh(vscode: ReturnType<typeof getVsCodeApi>, requestRefresh: () => void): void {
    this.stopLiveRefresh();
    if (!this.liveRuntime.paused) {
      this.startLiveRefresh(vscode, requestRefresh);
    }
  }

  private performLiveRefresh(params: {
    vscode: ReturnType<typeof getVsCodeApi>;
    state: DisasmEditorService['state'];
    getPageStart: (index: number) => number;
  }): void {
    if (this.liveRuntime.refreshPending) { return; }
    this.liveRuntime.refreshPending = true;
    const startIndex = params.getPageStart(params.state.cursor);
    params.vscode.postMessage({ type: 'requestPage', startIndex });
  }

  private handleLiveStatusUpdate(params: {
    paused: boolean;
    pc: number;
    breakpointHitAddress: number | undefined;
    registers: RegisterData;
    stackBytes: number[];
    stackBase: number;
    state: DisasmEditorService['state'];
    signals: EditorState;
    headerToolbar: HTMLElement;
    registersToggleBtn: HTMLElement;
    debugPanelVisible: WritableSignal<boolean>;
    debugPanelRegisters: WritableSignal<RegisterData | null>;
    debugPanelStackBytes: WritableSignal<number[]>;
    debugPanelStackBase: WritableSignal<number>;
    debugPanelWatches: WritableSignal<PineWatchInfo[]>;
    bottomPanelCallStack: WritableSignal<CallStackEntry[]>;
    bottomPanelBreakpoints: WritableSignal<Set<number>>;
    setpcStatus: WritableSignal<string>;
    buildCallStackFrames: (
      regs: RegisterData,
      stackBytes: number[],
      stackBase: number,
      labels: Array<{ address: string; label: string }>,
    ) => CallStackEntry[];
    checkAndLoadPage: () => boolean;
    updateCursor: (scrollBlock?: ScrollLogicalPosition | null) => void;
    syncEditorSignals: () => void;
    vscode: ReturnType<typeof getVsCodeApi>;
    requestRefresh: () => void;
  }): void {
    if (params.paused && (params.pc === undefined || params.pc === null || !params.registers)) {
      // Sidebar-initiated pause can arrive as a minimal status packet first.
      // Request full status so we can snap to PC and populate registers/stack.
      params.vscode.postMessage({ type: 'getStatus' });
    }

    if (params.paused && !this.liveRuntime.paused) {
      this.liveRuntime.paused = true;
      this.stopLiveRefresh();
      this.setLivePausedUi(params.headerToolbar, true);
    }
    if (params.paused) {
      params.state.breakpointHitAddress = params.breakpointHitAddress ?? params.pc;
      if (params.pc !== undefined && params.pc !== null) {
        params.state.pcAddress = params.pc;
        if (params.signals.panels.setpc()) {
          params.setpcStatus.set(`PC set to 0x${params.pc.toString(16).toUpperCase()}`);
        }
      }
    } else {
      params.state.breakpointHitAddress = undefined;
      params.state.pcAddress = undefined;
    }

    if (params.paused && params.pc !== undefined && params.pc !== null) {
      const physPc = this.toPhysicalAddress(params.pc);
      const physBase = this.toPhysicalAddress(params.state.baseAddress);
      const instrIndex = Math.floor((physPc - physBase) / 4);
      if (instrIndex >= 0 && instrIndex < params.state.totalInstructions) {
        params.state.cursor = instrIndex;
        this.pendingPageScrollBlock = 'center';
        const pageReady = params.checkAndLoadPage();
        if (pageReady) {
          this.pendingPageScrollBlock = null;
          params.updateCursor('center');
        }
      } else {
        params.vscode.postMessage({ type: 'gotoRegion', address: params.pc });
      }
    }

    if (!params.paused && this.liveRuntime.paused) {
      this.liveRuntime.paused = false;
      params.state.breakpointHitAddress = undefined;
      params.state.pcAddress = undefined;
      this.setLivePausedUi(params.headerToolbar, false);
      this.startLiveRefresh(params.vscode, params.requestRefresh);
      params.registersToggleBtn.classList.remove('active');
      params.signals.panels.registers.set(false);
      params.debugPanelVisible.set(false);
      params.debugPanelRegisters.set(null);
      params.syncEditorSignals();
      return;
    }

    if (params.paused && params.registers) {
      params.registersToggleBtn.classList.add('active');
      params.signals.panels.registers.set(true);
      params.debugPanelVisible.set(true);
      params.debugPanelRegisters.set(params.registers);
      params.debugPanelStackBytes.set(params.stackBytes);
      params.debugPanelStackBase.set(params.stackBase);
      params.debugPanelWatches.set([...params.state.pineWatches]);
      if (params.state.isLive) {
        params.vscode.postMessage({ type: 'listPineWatches' });
      }
      const typedFrames = params.buildCallStackFrames(params.registers, params.stackBytes, params.stackBase, params.state.symLabels ?? []);
      params.signals.callStack.set(typedFrames);
      params.bottomPanelCallStack.set(typedFrames);
      params.bottomPanelBreakpoints.set(new Set(params.state.breakpoints));
    }
  }

  private handleLiveReset(params: {
    state: DisasmEditorService['state'];
    navHistory: number[];
    navBackBtn: HTMLButtonElement;
    getPageStart: (index: number) => number;
    generateZeroPageRows: (
      startIndex: number,
      pageSize: number,
      totalInstructions: number,
      baseAddress: number,
    ) => DisasmRow[];
    renderDisassembly: () => void;
    headerToolbar: HTMLElement;
    vscode: ReturnType<typeof getVsCodeApi>;
    requestRefresh: () => void;
  }): void {
    params.state.loadedPages.clear();
    params.state.disasmRows = [];
    params.state.breakpointHitAddress = undefined;
    params.navHistory.length = 0;
    params.navBackBtn.disabled = true;
    params.state.pcAddress = undefined;
    const startIndex = params.getPageStart(params.state.cursor);
    const zeroRows = params.generateZeroPageRows(startIndex, params.state.pageSize, params.state.totalInstructions, params.state.baseAddress);
    params.state.loadedPages.set(startIndex, zeroRows);
    params.state.disasmRows = zeroRows;
    params.renderDisassembly();
    this.liveRuntime.paused = false;
    this.setLivePausedUi(params.headerToolbar, false);
    this.restartLiveRefresh(params.vscode, params.requestRefresh);
  }

  private navigateToAddress(params: {
    address: number;
    state: DisasmEditorService['state'];
    pushNavHistory: () => void;
    checkAndLoadPage: () => boolean;
    updateCursor: (scrollBlock?: ScrollLogicalPosition | null) => void;
  }): void {
    const physAddr = this.toPhysicalAddress(params.address);
    const physBase = this.toPhysicalAddress(params.state.baseAddress);
    const instrIndex = Math.floor((physAddr - physBase) / 4);
    if (instrIndex < 0 || instrIndex >= params.state.totalInstructions) { return; }
    params.pushNavHistory();
    params.state.cursor = instrIndex;
    this.pendingPageScrollBlock = 'center';
    if (params.checkAndLoadPage()) {
      this.pendingPageScrollBlock = null;
      params.updateCursor('center');
    }
  }

  private navigateToCurrentRowTarget(params: {
    state: DisasmEditorService['state'];
    getLocalIndex: (index: number) => number;
    navigateToAddress: (address: number) => void;
  }): void {
    const localIndex = params.getLocalIndex(params.state.cursor);
    const row = params.state.disasmRows[localIndex];
    if (!row) { return; }

    const branchMatch = row.args.match(/\$([0-9A-F]{8})/i);
    if (branchMatch) {
      params.navigateToAddress(parseInt(branchMatch[1], 16));
      return;
    }

    const jumpMatch = row.args.match(/0x([0-9A-F]{8})/i);
    if (jumpMatch) {
      params.navigateToAddress(parseInt(jumpMatch[1], 16));
      return;
    }

    if (row.comment) {
      const commentMatch = row.comment.match(/0x([0-9A-F]{8})/i) || row.comment.match(/\b([0-9A-F]{8})\b/i);
      if (commentMatch) {
        const address = parseInt(commentMatch[1], 16);
        // Validate address is in valid EE address ranges:
        // Physical: 0x00000000-0x1FFFFFFF (512MB)
        // KSEG0/1: 0x80000000-0xBFFFFFFF
        const isPhysical = address <= 0x1FFFFFFF;
        const isKseg = address >= 0x80000000 && address <= 0xBFFFFFFF;
        if (isPhysical || isKseg) {
          params.navigateToAddress(address);
          return;
        }
      }
    }

    // Check if the current row's address matches a known pointer entry, and if
    // so navigate to the pointsTo address (pointer dereference navigation).
    if (params.state.symPointers && params.state.symPointers.length > 0) {
      const rowAddrNorm = row.addr.replace(/^0x/i, '').toUpperCase().padStart(8, '0');
      const ptrEntry = params.state.symPointers.find(p => {
        const pAddrNorm = p.address.replace(/^0x/i, '').toUpperCase().padStart(8, '0');
        return pAddrNorm === rowAddrNorm;
      });
      if (ptrEntry) {
        const target = parseInt(ptrEntry.pointsTo.replace(/^0x/i, ''), 16);
        if (!isNaN(target) && target !== 0) {
          params.navigateToAddress(target);
          return;
        }
      }
    }
  }

  private toggleBreakpointAtCursor(params: {
    state: DisasmEditorService['state'];
    getLocalIndex: (index: number) => number;
    vscode: ReturnType<typeof getVsCodeApi>;
    renderDisassembly: (scrollBlock?: ScrollLogicalPosition | null) => void;
  }): void {
    const localIndex = params.getLocalIndex(params.state.cursor);
    const row = params.state.disasmRows[localIndex];
    if (!row) { return; }

    const addr = parseInt(row.addr, 16);
    if (params.state.breakpoints.has(addr)) {
      params.state.breakpoints.delete(addr);
      params.vscode.postMessage({ type: 'clearBreakpoint', address: addr });
    } else {
      params.state.breakpoints.add(addr);
      params.vscode.postMessage({ type: 'setBreakpoint', address: addr });
    }
    params.renderDisassembly();
  }

  private syncEditorSignals(state: DisasmEditorService['state'], signals: EditorState): void {
    signals.cursor.set(state.cursor);
    signals.totalInstructions.set(state.totalInstructions);
    signals.baseAddress.set(state.baseAddress);
    signals.pageSize.set(state.pageSize);
    // loadedPages is already kept in sync by the service layer; skip the
    // redundant new Map() copy here to avoid triggering downstream re-renders
    // on every live-refresh tick.
    signals.pcAddress.set(state.pcAddress);
    // Only rebuild the breakpoints Set when the count actually changes so that
    // DisasmRowComponent.hasBreakpoint() computed does not re-run on every
    // live-refresh tick (which would cause all visible rows to flicker).
    if (state.breakpoints.size !== this._lastBreakpointsSize) {
      this._lastBreakpointsSize = state.breakpoints.size;
      signals.breakpoints.set(new Set(state.breakpoints));
    }
    signals.breakpointHitAddress.set(state.breakpointHitAddress);
    signals.searchQuery.set(state.searchQuery ?? '');
    signals.searchMode.set(state.searchMode as SearchMode);
    // Same guard for searchMatches — only spread into a new array when the
    // result count changes so isSearchMatch() does not re-run every tick.
    const matchLen = state.searchMatches?.length ?? 0;
    if (matchLen !== this._lastSearchMatchesLength) {
      this._lastSearchMatchesLength = matchLen;
      signals.searchMatches.set([...(state.searchMatches ?? [])]);
    }
    signals.currentMatchIndex.set(state.currentMatch);
  }

  private syncVirtualGridState(params: {
    state: DisasmEditorService['state'];
    getPageStart: (index: number) => number;
    pointerLabelMap: Map<string, string>;
  }): void {
    if (!this.useVirtualGrid()) { return; }
    const currentPageStart = params.getPageStart(params.state.cursor);
    const pageRows = params.state.loadedPages.get(currentPageStart) as DisasmRowType[] | undefined;

    const last = this.lastGridState;
    if (!last || last.pageStart !== currentPageStart || last.p1 !== pageRows) {
      const gridRows = pageRows && pageRows.length > 0
        ? pageRows
        : (params.state.disasmRows as DisasmRowType[]);
      this.gridRows.set(gridRows);
      this.gridPageStart.set(currentPageStart);
      this.lastGridState = { pageStart: currentPageStart, p1: pageRows, p2: undefined };
    }

    // Pass the same map reference so the signal only changes when rebuildPointerLabelMap() fires
    this.gridPointerLabelMap.set(params.pointerLabelMap);
  }

  private updateHexForRow(params: {
    row: DisasmRow;
    state: DisasmEditorService['state'];
    updateHexView: (data: number[], offset: number, highlightAddr?: number, highlightLength?: number) => void;
    vscode: ReturnType<typeof getVsCodeApi>;
  }): void {
    const addr = parseInt(params.row.addr, 16);
    const byteStr = params.row.bytes?.trim() || '';
    const length = byteStr.length > 0 ? Math.floor(byteStr.length / 2) : 4;
    const inView = addr >= params.state.hexOffset && addr + length <= params.state.hexOffset + params.state.hexData.length;
    if (inView) {
      params.updateHexView(params.state.hexData, params.state.hexOffset, addr, length);
      return;
    }

    params.vscode.postMessage({ type: 'updateHex', address: addr, length });
  }

  private updateCursor(params: {
    state: DisasmEditorService['state'];
    signals: EditorState;
    getLocalIndex: (index: number) => number;
    scrollBlock?: ScrollLogicalPosition | null;
    updateHexView: (data: number[], offset: number, highlightAddr?: number, highlightLength?: number) => void;
    vscode: ReturnType<typeof getVsCodeApi>;
  }): void {
    const currentPageLocalIndex = params.getLocalIndex(params.state.cursor);
    const gridLocalIndex = params.state.cursor - this.gridPageStart();
    this.syncEditorSignals(params.state, params.signals);
    if (params.scrollBlock !== null) {
      this.virtualGridRef()?.scrollToIndex(gridLocalIndex, params.scrollBlock ?? 'nearest');
    }
    const row = params.state.disasmRows[currentPageLocalIndex];
    if (!row) { return; }
    this.updateHexForRow({
      row,
      state: params.state,
      updateHexView: params.updateHexView,
      vscode: params.vscode,
    });
  }

  private renderDisassembly(params: {
    state: DisasmEditorService['state'];
    getPageStart: (index: number) => number;
    pointerLabelMap: Map<string, string>;
    signals: EditorState;
    getLocalIndex: (index: number) => number;
    scrollBlock?: ScrollLogicalPosition | null;
    updateHexView: (data: number[], offset: number, highlightAddr?: number, highlightLength?: number) => void;
    vscode: ReturnType<typeof getVsCodeApi>;
  }): void {
    this.syncVirtualGridState({
      state: params.state,
      getPageStart: params.getPageStart,
      pointerLabelMap: params.pointerLabelMap,
    });
    this.updateCursor({
      state: params.state,
      signals: params.signals,
      getLocalIndex: params.getLocalIndex,
      scrollBlock: params.scrollBlock,
      updateHexView: params.updateHexView,
      vscode: params.vscode,
    });
  }

  private checkAndLoadPage(params: {
    state: DisasmEditorService['state'];
    getPageStart: (index: number) => number;
    vscode: ReturnType<typeof getVsCodeApi>;
    renderDisassembly: (scrollBlock?: ScrollLogicalPosition | null) => void;
  }): boolean {
    const startIndex = params.getPageStart(params.state.cursor);
    if (!params.state.loadedPages.has(startIndex)) {
      params.vscode.postMessage({ type: 'requestPage', startIndex });
      return false;
    }

    const cachedRows = params.state.loadedPages.get(startIndex)!;
    if (params.state.disasmRows !== cachedRows) {
      params.state.disasmRows = cachedRows;
      params.renderDisassembly('nearest');
    }
    return true;
  }

  private buildPointerLabelMap(pointerLabels: DisasmEditorService['state']['pointerLabels']): Map<string, string> {
    const pointerLabelMap = new Map<string, string>();
    for (const entry of pointerLabels) {
      const key = entry.address.replace(/^0x/i, '').toUpperCase().padStart(8, '0');
      pointerLabelMap.set(key, entry.label);
    }
    return pointerLabelMap;
  }

  private pushNavHistory(params: {
    navHistory: number[];
    state: DisasmEditorService['state'];
    navBackBtn: HTMLButtonElement;
    maxHistory: number;
  }): void {
    params.navHistory.push(params.state.cursor);
    if (params.navHistory.length > params.maxHistory) {
      params.navHistory.shift();
    }
    params.navBackBtn.disabled = false;
  }

  private navigateBack(params: {
    navHistory: number[];
    state: DisasmEditorService['state'];
    navBackBtn: HTMLButtonElement;
    checkAndLoadPage: () => boolean;
    updateCursor: (scrollBlock?: ScrollLogicalPosition | null) => void;
    focusGrid: () => void;
  }): void {
    const prev = params.navHistory.pop();
    if (prev === undefined) { return; }
    params.state.cursor = prev;
    params.navBackBtn.disabled = params.navHistory.length === 0;
    if (params.checkAndLoadPage()) {
      params.updateCursor('center');
    }
    params.focusGrid();
  }

  private clearSearchMatches(params: {
    state: DisasmEditorService['state'];
    syncEditorSignals: () => void;
    renderDisassembly: (scrollBlock?: ScrollLogicalPosition | null) => void;
  }): void {
    params.state.searchMatches = [];
    params.state.currentMatch = -1;
    params.state.searchQuery = '';
    params.syncEditorSignals();
    params.renderDisassembly();
  }

  private toggleSearch(params: {
    state: DisasmEditorService['state'];
    signals: EditorState;
    searchToggleBtn: HTMLElement;
    clearSearchMatches: () => void;
  }): void {
    params.state.searchActive = !params.state.searchActive;
    params.signals.panels.search.set(params.state.searchActive);
    params.searchToggleBtn.classList.toggle('active', params.state.searchActive);
    if (!params.state.searchActive) {
      params.clearSearchMatches();
    }
  }

  private jumpToSearchMatch(params: {
    matchIndex: number;
    state: DisasmEditorService['state'];
    checkAndLoadPage: () => boolean;
    updateCursor: (scrollBlock?: ScrollLogicalPosition | null) => void;
    syncEditorSignals: () => void;
  }): void {
    if (params.matchIndex < 0 || params.matchIndex >= params.state.searchMatches.length) { return; }
    params.state.currentMatch = params.matchIndex;
    const match = params.state.searchMatches[params.matchIndex];
    params.state.cursor = match.instructionIndex;
    if (params.checkAndLoadPage()) {
      params.updateCursor();
    }
    params.syncEditorSignals();
  }

  private handleSearchResults(params: {
    matches: SearchMatch[];
    state: DisasmEditorService['state'];
    syncEditorSignals: () => void;
    jumpToMatch: (matchIndex: number) => void;
    renderDisassembly: (scrollBlock?: ScrollLogicalPosition | null) => void;
  }): void {
    params.state.searchMatches = params.matches || [];
    params.syncEditorSignals();
    if (params.state.searchMatches.length > 0) {
      params.state.currentMatch = 0;
      params.jumpToMatch(0);
      return;
    }

    params.state.currentMatch = -1;
    params.syncEditorSignals();
    params.renderDisassembly();
  }

  private nextSearchMatch(params: {
    state: DisasmEditorService['state'];
    jumpToMatch: (matchIndex: number) => void;
  }): void {
    if (params.state.searchMatches.length === 0) { return; }
    params.jumpToMatch((params.state.currentMatch + 1) % params.state.searchMatches.length);
  }

  private prevSearchMatch(params: {
    state: DisasmEditorService['state'];
    jumpToMatch: (matchIndex: number) => void;
  }): void {
    if (params.state.searchMatches.length === 0) { return; }
    params.jumpToMatch((params.state.currentMatch - 1 + params.state.searchMatches.length) % params.state.searchMatches.length);
  }

  private openEditOverlay(params: {
    address: number;
    addressHex: string;
    op: string;
    args: string;
    bytes: string;
    currentLabel: string;
    currentComment: string;
  }): void {
    this.zone.run(() => {
      this.editOverlayRef()?.open(params);
      this.signals.panels.editOverlay.set(true);
    });
  }

  private handleEditOverlayMessage(data: Record<string, unknown>): void {
    if (data['type'] === 'disasmBytesResult') {
      const preview = (data['preview'] as string) ?? '';
      this.zone.run(() => {
        this.editOverlayDisasmPreview.set(preview);
        if (preview && preview !== '(invalid)') {
          this.editOverlayExternalInstrPreview.set(preview);
        }
      });
      return;
    }

    if (data['type'] === 'assembleInstrResult') {
      const hexBytes = data['hexBytes'] as string | undefined;
      this.zone.run(() => {
        if (hexBytes) {
          this.editOverlayExternalHexBytes.set(hexBytes);
          this.editOverlayDisasmPreview.set('');
        } else {
          this.editOverlayDisasmPreview.set('(assembly error)');
        }
      });
      return;
    }

    if (data['type'] === 'addressHistoryResult') {
      const history = data['history'] as Array<{ name: string; date: string; value: string; disasm?: string }> | undefined;
      this.zone.run(() => {
        this.editOverlayRef()?.setHistory(history ?? []);
      });
    }
  }

  private openConverterOverlay(): void {
    this.zone.run(() => {
      this.converterOverlayRef()?.open();
      this.signals.panels.converterOverlay.set(true);
    });
  }

  private handleConverterOverlayMessage(data: Record<string, unknown>): void {
    if (data['type'] !== 'convertCheatResult') { return; }
    this.zone.run(() => {
      this.converterOverlayRef()?.setResult({
        result: data['result'] as string | undefined,
        error: data['error'] as string | undefined,
      });
    });
  }

  private openPorterOverlay(): void {
    this.zone.run(() => {
      this.porterOverlayRef()?.open();
      this.signals.panels.porterOverlay.set(true);
    });
  }

  private handlePorterOverlayMessage(data: Record<string, unknown>): void {
    if (data['type'] === 'browseSourceDumpResult') {
      if (data['filePath']) {
        this.zone.run(() => this.porterOverlayRef()?.setSourcePath(data['filePath'] as string));
      }
      return;
    }

    if (data['type'] === 'browseTargetDumpResult') {
      if (data['filePath']) {
        this.zone.run(() => this.porterOverlayRef()?.setTargetPath(data['filePath'] as string));
      }
      return;
    }

    if (data['type'] === 'portCheatsResult') {
      this.zone.run(() => {
        this.porterOverlayRef()?.setResult({
          results: data['results'] as import('./components/porter-overlay.component').PorterResult[] | undefined,
          error: data['error'] as string | undefined,
        });
      });
    }
  }

  private openMemoryCardOverlay(): void {
    this.zone.run(() => {
      this.mcOverlayRef()?.open();
    });
  }

  private handleMemoryCardMessage(data: Record<string, unknown>): void {
    if (data['type'] === 'browseMcResult') {
      if (data['filePath']) {
        this.zone.run(() => this.mcOverlayRef()?.setMcPath(data['filePath'] as string));
      }
      return;
    }

    if (data['type'] === 'newMcResult') {
      if (data['filePath']) {
        this.zone.run(() => this.mcOverlayRef()?.setMcPath(data['filePath'] as string));
      }
      return;
    }

    if (data['type'] === 'mcListResult') {
      this.zone.run(() => {
        this.mcOverlayRef()?.setListResult({
          saves: data['saves'] as McListResult['saves'],
          error: data['error'] as string | undefined,
        });
      });
      return;
    }

    if (data['type'] === 'mcActionResult') {
      this.zone.run(() => {
        this.mcOverlayRef()?.setActionResult({
          message: data['message'] as string | undefined,
          error: data['error'] as string | undefined,
        });
      });
    }
  }

  private openHddOverlay(): void {
    this.zone.run(() => {
      this.hddOverlayRef()?.open();
    });
  }

  private handleHddMessage(data: Record<string, unknown>): void {
    if (data['type'] === 'browseHddResult') {
      if (data['filePath']) {
        this.zone.run(() => this.hddOverlayRef()?.setHddPath(data['filePath'] as string));
      }
      return;
    }

    if (data['type'] === 'newHddResult') {
      if (data['filePath']) {
        this.zone.run(() => this.hddOverlayRef()?.setHddPath(data['filePath'] as string));
      }
      return;
    }

    if (data['type'] === 'hddListResult') {
      this.zone.run(() => {
        this.hddOverlayRef()?.setListResult({
          partitions: data['partitions'] as HddListResult['partitions'],
          error: data['error'] as string | undefined,
        });
      });
      return;
    }

    if (data['type'] === 'hddActionResult') {
      this.zone.run(() => {
        this.hddOverlayRef()?.setActionResult({
          message: data['message'] as string | undefined,
          error: data['error'] as string | undefined,
        });
      });
    }
  }

  private handleWindowMessage(data: Record<string, unknown>): void {
    // Handle extension-driven overlay open requests (e.g. from tree-view header button).
    if (data['type'] === 'openStructCalculator') {
      this.zone.run(() => this.openStructOverlay());
      return;
    }
    if (data['type'] === 'pineWatches') {
      this.handlePineWatchesMessage(data);
      return;
    }
    this.handleEditOverlayMessage(data);
    this.handleConverterOverlayMessage(data);
    this.handlePorterOverlayMessage(data);
    this.handleMemoryCardMessage(data);
    this.handleHddMessage(data);
    this.handleScannerMessage(data);
    this.handleStructMessage(data);
    this.handleLabelManagerMessage(data);
  }

  private handlePineWatchesMessage(data: Record<string, unknown>): void {
    const watches = (data['watches'] as PineWatchInfo[] | undefined) ?? [];
    this.zone.run(() => {
      this.svc.state.pineWatches = watches;
      this.debugPanelWatches.set([...watches]);
    });
  }

  private openScannerOverlay(): void {
    this.zone.run(() => {
      this.scannerOverlayRef()?.open();
      this.signals.panels.scannerOverlay.set(true);
    });
  }

  private handleScannerMessage(data: Record<string, unknown>): void {
    if (data['type'] !== 'scanValueResult') { return; }
    this.zone.run(() => {
      this.scannerOverlayRef()?.setResult({
        entries:       data['entries']       as ScanResult['entries'] | undefined,
        error:         data['error']         as string | undefined,
        serverMessage: data['serverMessage'] as string | undefined,
      });
    });
  }

  private openStructOverlay(): void {
    const cursorRow = this.signals.currentRow();
    const cursorAddr = cursorRow ? parseInt(cursorRow.addr || '0', 16) : 0;
    this.zone.run(() => {
      this.structOverlayRef()?.open(cursorAddr);
      this.signals.panels.structOverlay.set(true);
    });
  }

  private handleStructMessage(data: Record<string, unknown>): void {
    if (data['type'] === 'getStructsResult') {
      const structs = data['structs'];
      if (structs !== undefined && structs !== null) {
        this.zone.run(() => {
          this.structOverlayRef()?.setStructs(structs as StructEntryData[]);
        });
      } else {
        // Not connected or request failed — stop loading spinner but keep existing list
        this.zone.run(() => {
          this.structOverlayRef()?.stopLoading();
        });
      }
    } else if (data['type'] === 'addStructResult' || data['type'] === 'addStructMemberResult') {
      this.zone.run(() => {
        this.structOverlayRef()?.setStructs((data['structs'] as StructEntryData[]) ?? []);
      });
    } else if (data['type'] === 'readPointerAtResult') {
      this.zone.run(() => {
        this.structOverlayRef()?.setResolvedBase((data['value'] as string) ?? '');
      });
    }
  }

  private handleLabelManagerMessage(data: Record<string, unknown>): void {
    const type = data['type'] as string | undefined;
    if (!type?.startsWith('labelManager')) { return; }
    this.zone.run(() => {
      const lm = this.labelManagerRef();
      if (!lm) { return; }
      if (type === 'labelManagerSets') {
        lm.setSets((data['sets'] as string[]) ?? []);
      } else if (type === 'labelManagerSet') {
        lm.setActiveSet(data['set'] as LabelSetPayload);
      } else if (type === 'labelManagerExport') {
        // Trigger a file download via a temporary <a> element.
        const json    = data['json'] as string;
        const setName = data['setName'] as string;
        const blob = new Blob([json], { type: 'application/json' });
        const url  = URL.createObjectURL(blob);
        const a    = document.createElement('a');
        a.href     = url;
        a.download = (setName || 'labels') + '.json';
        a.click();
        URL.revokeObjectURL(url);
      } else if (type === 'labelManagerError') {
        lm.showStatus((data['message'] as string) ?? 'Error', true);
      } else if (type === 'labelManagerStatus') {
        lm.showStatus((data['message'] as string) ?? '');
      }
    });
  }

  private registerServiceSubscriptions(params: {
    svc: DisasmEditorService;
    state: DisasmEditorService['state'];
    signals: EditorState;
    headerToolbar: HTMLElement;
    registersToggleBtn: HTMLElement;
    navBackBtn: HTMLButtonElement;
    setpcStatus: WritableSignal<string>;
    debugPanelVisible: WritableSignal<boolean>;
    debugPanelRegisters: WritableSignal<RegisterData | null>;
    debugPanelStackBytes: WritableSignal<number[]>;
    debugPanelStackBase: WritableSignal<number>;
    debugPanelWatches: WritableSignal<PineWatchInfo[]>;
    bottomPanelCallStack: WritableSignal<CallStackEntry[]>;
    bottomPanelBreakpoints: WritableSignal<Set<number>>;
    buildCallStackFrames: (
      regs: RegisterData,
      stackBytes: number[],
      stackBase: number,
      labels: Array<{ address: string; label: string }>,
    ) => CallStackEntry[];
    getPageStart: (index: number) => number;
    getLocalIndex: (index: number) => number;
    navHistory: number[];
    createPlaceholderRegs: () => RegisterData;
    generateZeroPageRows: (
      startIndex: number,
      pageSize: number,
      totalInstructions: number,
      baseAddress: number,
    ) => DisasmRow[];
    updateHexView: (data: number[], offset: number, highlightAddr?: number, highlightLength?: number) => void;
    renderDisassembly: (scrollBlock?: ScrollLogicalPosition | null) => void;
    checkAndLoadPage: () => boolean;
    updateCursor: (scrollBlock?: ScrollLogicalPosition | null) => void;
    syncEditorSignals: () => void;
    handleSearchResults: (matches: SearchMatch[]) => void;
    rebuildPointerLabelMap: () => void;
    vscode: ReturnType<typeof getVsCodeApi>;
    requestRefresh: () => void;
  }): void {
    params.svc.initReceived$.subscribe(({ isLive, regionLabel }) => {
      this.liveRuntime.refreshPending = false;
      params.navHistory.length = 0;
      params.navBackBtn.disabled = true;
      if (regionLabel) {
        const rl = document.getElementById('sym-region-label');
        if (rl) { rl.textContent = `— ${regionLabel}`; }
      }
      if (isLive) {
        params.headerToolbar.classList.add('live-active');
        this.startLiveRefresh(params.vscode, params.requestRefresh);
        const ph = params.createPlaceholderRegs();
        this.debugPanelRegisters.set(ph);
        this.debugPanelStackBytes.set([]);
        this.debugPanelStackBase.set(0);
      }
    });

    params.svc.pageLoaded$.subscribe(({ startIndex, changed }) => {
      this.liveRuntime.refreshPending = false;
      if (startIndex === params.getPageStart(params.state.cursor)) {
        if (!changed && this.pendingPageScrollBlock === null && !this.liveRuntime.paused) {
          return;
        }
        const scrollBlock = this.liveRuntime.paused ? 'center' : this.pendingPageScrollBlock;
        this.pendingPageScrollBlock = null;
        params.renderDisassembly(scrollBlock);
      }
    });

    params.svc.hexUpdated$.subscribe(event => {
      const localIndex = params.getLocalIndex(params.state.cursor);
      const curRow = params.state.disasmRows[localIndex];
      let hiAddr = event.highlightAddr ?? event.offset;
      let hiLength = event.highlightLength ?? 4;
      if (curRow) {
        const curAddr = parseInt(curRow.addr, 16);
        const curBytes = curRow.bytes?.trim() || '';
        const curLength = curBytes.length > 0 ? Math.floor(curBytes.length / 2) : 4;
        if (curAddr >= event.offset && curAddr + curLength <= event.offset + event.data.length) {
          hiAddr = curAddr;
          hiLength = curLength;
        }
      }
      params.updateHexView(event.data, event.offset, hiAddr, hiLength);
    });

    params.svc.searchResultsReceived$.subscribe(matches => {
      params.handleSearchResults(matches);
    });

    params.svc.statusReceived$.subscribe(({ paused, pc, breakpointHitAddress, registers, stackBytes, stackBase }) => {
      this.handleLiveStatusUpdate({
        paused,
        pc,
        breakpointHitAddress,
        registers,
        stackBytes,
        stackBase,
        state: params.state,
        signals: params.signals,
        setpcStatus: params.setpcStatus,
        headerToolbar: params.headerToolbar,
        registersToggleBtn: params.registersToggleBtn,
        debugPanelVisible: params.debugPanelVisible,
        debugPanelRegisters: params.debugPanelRegisters,
        debugPanelStackBytes: params.debugPanelStackBytes,
        debugPanelStackBase: params.debugPanelStackBase,
        debugPanelWatches: params.debugPanelWatches,
        bottomPanelCallStack: params.bottomPanelCallStack,
        bottomPanelBreakpoints: params.bottomPanelBreakpoints,
        requestRefresh: params.requestRefresh,
        vscode: params.vscode,
        checkAndLoadPage: params.checkAndLoadPage,
        updateCursor: (scrollBlock?: ScrollLogicalPosition | null) => params.updateCursor(scrollBlock),
        buildCallStackFrames: params.buildCallStackFrames,
        syncEditorSignals: params.syncEditorSignals,
      });
    });

    params.svc.resetReceived$.subscribe(() => {
      this.handleLiveReset({
        state: params.state,
        navHistory: params.navHistory,
        navBackBtn: params.navBackBtn,
        headerToolbar: params.headerToolbar,
        vscode: params.vscode,
        requestRefresh: params.requestRefresh,
        getPageStart: params.getPageStart,
        generateZeroPageRows: params.generateZeroPageRows,
        renderDisassembly: params.renderDisassembly,
      });
    });

    params.svc.symbolsReceived$.subscribe(() => {
      this.symbolsPanelLabels.set(params.state.symLabels ?? []);
      this.symbolsPanelStrings.set(params.state.symStrings ?? []);
      this.symbolsPanelFunctions.set(params.state.symFunctions ?? []);
      this.symbolsPanelPointers.set(params.state.symPointers ?? []);
      this.symbolsPanelFloats.set(params.state.symFloats ?? []);
      this.symbolsPanelIntegers.set(params.state.symIntegers ?? []);
    });

    params.svc.labelsUpdated$.subscribe(() => {
      this.symbolsPanelLabels.set(params.state.symLabels ?? []);
      params.state.loadedPages.delete(params.getPageStart(params.state.cursor));
      params.checkAndLoadPage();
    });

    params.svc.stringsUpdated$.subscribe(() => {
      this.symbolsPanelStringsStatus.set(params.state.symStrings.length > 0 ? `${params.state.symStrings.length} found` : 'None found');
      this.symbolsPanelStrings.set(params.state.symStrings ?? []);
    });

    params.svc.functionsUpdated$.subscribe(() => {
      this.symbolsPanelFunctionsStatus.set(params.state.symFunctions.length > 0 ? `${params.state.symFunctions.length} found` : 'None found');
      this.symbolsPanelFunctions.set(params.state.symFunctions ?? []);
    });

    params.svc.pointersUpdated$.subscribe(() => {
      this.symbolsPanelPointersStatus.set(params.state.symPointers.length > 0 ? `${params.state.symPointers.length} found` : 'None found');
      this.symbolsPanelPointers.set(params.state.symPointers ?? []);
    });

    params.svc.floatsUpdated$.subscribe(() => {
      this.symbolsPanelFloatsStatus.set(params.state.symFloats.length > 0 ? `${params.state.symFloats.length} found` : 'None found');
      this.symbolsPanelFloats.set(params.state.symFloats ?? []);
    });

    params.svc.integersUpdated$.subscribe(() => {
      this.symbolsPanelIntegersStatus.set(params.state.symIntegers.length > 0 ? `${params.state.symIntegers.length} found` : 'None found');
      this.symbolsPanelIntegers.set(params.state.symIntegers ?? []);
    });

    params.svc.analysisUpdated$.subscribe(() => {
      params.renderDisassembly(null);
    });

    params.svc.pointerLabelsUpdated$.subscribe(() => {
      params.rebuildPointerLabelMap();
      params.renderDisassembly(null);
    });
  }

  private configureRuntimeCallbacks(params: {
    state: DisasmEditorService['state'];
    signals: EditorState;
    clamp: (val: number, min: number, max: number) => number;
    toPhysicalAddress: (value: number) => number;
    parseHexAddress: (input: string) => number | null;
    prevSearchMatch: () => void;
    nextSearchMatch: () => void;
    pushNavHistory: () => void;
    checkAndLoadPage: () => boolean;
    updateCursor: (scrollBlock?: ScrollLogicalPosition | null) => void;
    renderDisassembly: (scrollBlock?: ScrollLogicalPosition | null) => void;
    navigateBack: () => void;
    navigateToCurrentRowTarget: () => void;
    toggleBreakpointAtCursor: () => void;
    openEditOverlayFn: (
      address: number,
      addressHex: string,
      op: string,
      args: string,
      bytes: string,
      currentLabel: string,
      currentComment: string,
    ) => void;
    vscode: ReturnType<typeof getVsCodeApi>;
    gotoStatus: WritableSignal<string>;
    debugPanelRegisters: WritableSignal<RegisterData | null>;
    debugPanelStackBytes: WritableSignal<number[]>;
    debugPanelStackBase: WritableSignal<number>;
    debugPanelWatches: WritableSignal<PineWatchInfo[]>;
    bottomPanelCallStack: WritableSignal<CallStackEntry[]>;
    bottomPanelBreakpoints: WritableSignal<Set<number>>;
    buildCallStackFrames: (
      regs: RegisterData,
      stackBytes: number[],
      stackBase: number,
      labels: Array<{ address: string; label: string }>,
    ) => CallStackEntry[];
    createPlaceholderRegs: () => RegisterData;
  }): void {
    this.searchRuntime.prevMatch = params.prevSearchMatch;
    this.searchRuntime.nextMatch = params.nextSearchMatch;

    this.gotoRuntime.navigate = (addr: number) => {
      const physAddr = params.toPhysicalAddress(addr);
      const physBase = params.toPhysicalAddress(params.state.baseAddress);
      const instrIndex = Math.floor((physAddr - physBase) / 4);
      if (instrIndex < 0 || instrIndex >= params.state.totalInstructions) {
        params.vscode.postMessage({ type: 'gotoRegion', address: addr });
        params.gotoStatus.set(`Navigating to 0x${addr.toString(16).toUpperCase()}...`);
        return;
      }
      params.pushNavHistory();
      params.state.cursor = instrIndex;
      params.state.searchActive = false;
      params.signals.panels.search.set(false);
      this.pendingPageScrollBlock = 'center';
      if (params.checkAndLoadPage()) {
        this.pendingPageScrollBlock = null;
        params.updateCursor('center');
      }
      params.gotoStatus.set(`Moved to 0x${addr.toString(16).toUpperCase()}`);
    };

    this.gridRuntime.selectRow = (globalIndex: number) => {
      params.state.cursor = globalIndex;
      if (params.checkAndLoadPage()) {
        params.updateCursor();
      }
    };

    this.gridRuntime.editRow = (row: DisasmRow, address: number, addressHex: string) => {
      params.openEditOverlayFn(address, addressHex, row.op || '', row.args || '', row.bytes || '', row.label || '', row.comment || '');
    };

    this.gridRuntime.navigateTarget = params.navigateToCurrentRowTarget;
    this.gridRuntime.navigateBack = params.navigateBack;
    // Wrap toggleBreakpoint so the bottom-panel Breakpoints tab stays in sync
    // after an F9 / row-toggle action.
    this.gridRuntime.toggleBreakpoint = () => {
      params.toggleBreakpointAtCursor();
      params.bottomPanelBreakpoints.set(new Set(params.state.breakpoints));
    };
    this.gridRuntime.cursorMove = (delta: number) => {
      params.state.cursor = params.clamp(params.state.cursor + delta, 0, params.state.totalInstructions - 1);
      if (params.checkAndLoadPage()) {
        params.updateCursor();
      }
    };

    this.contextMenuRuntime.setBreakpoint = () => {
      if (!this._ctxRow || this._ctxGlobalIndex < 0) { return; }
      const addr = parseInt(this._ctxRow.addr, 16);
      if (!params.state.breakpoints.has(addr)) {
        params.state.breakpoints.add(addr);
        params.vscode.postMessage({ type: 'setBreakpoint', address: addr });
        params.bottomPanelBreakpoints.set(new Set(params.state.breakpoints));
        params.renderDisassembly();
      }
    };

    this.contextMenuRuntime.removeBreakpoint = () => {
      if (!this._ctxRow || this._ctxGlobalIndex < 0) { return; }
      const addr = parseInt(this._ctxRow.addr, 16);
      if (params.state.breakpoints.has(addr)) {
        params.state.breakpoints.delete(addr);
        params.vscode.postMessage({ type: 'clearBreakpoint', address: addr });
        params.bottomPanelBreakpoints.set(new Set(params.state.breakpoints));
        params.renderDisassembly();
      }
    };

    this.contextMenuRuntime.addWatch = () => {
      if (!this._ctxRow || this._ctxGlobalIndex < 0) { return; }
      const addr = parseInt(this._ctxRow.addr, 16);
      const end  = addr + 4;
      const alreadyWatched = params.state.pineWatches.some((w: PineWatchInfo) => parseInt(w.Start, 16) === addr);
      if (!alreadyWatched) {
        params.vscode.postMessage({ type: 'addPineWatch', address: addr, end });
      }
    };

    this.contextMenuRuntime.removeWatch = () => {
      if (!this._ctxRow || this._ctxGlobalIndex < 0) { return; }
      const addr = parseInt(this._ctxRow.addr, 16);
      const end  = addr + 4;
      const watch = params.state.pineWatches.find((w: PineWatchInfo) => parseInt(w.Start, 16) === addr);
      if (watch) {
        const cpu = watch.Cpu === 'IOP' ? 1 : 0;
        params.vscode.postMessage({ type: 'removePineWatch', address: addr, end, cpu });
      }
    };

    this.contextMenuRuntime.gotoTarget = () => {
      if (this._ctxGlobalIndex < 0) { return; }
      params.state.cursor = this._ctxGlobalIndex;
      params.navigateToCurrentRowTarget();
    };

    this.contextMenuRuntime.addToLabelManager = () => {
      if (!this._ctxRow) { return; }
      this.zone.run(() => {
        this.signals.panels.labelManager.set(true);
        this.labelManagerRef()?.openWithAddress(this._ctxRow!.addr);
      });
    };

    this._debugEditRegisterCallback = (regType: string, regIdx: number, currentValue: string) => {
      console.log(`Edit register: type=${regType}, idx=${regIdx}, value=${currentValue}`);
    };

    this.bottomPanelRuntime.navigate = (addr: number) => {
      const physAddr = params.toPhysicalAddress(addr);
      const physBase = params.toPhysicalAddress(params.state.baseAddress);
      const instrIndex = Math.floor((physAddr - physBase) / 4);
      if (instrIndex < 0 || instrIndex >= params.state.totalInstructions) {
        params.vscode.postMessage({ type: 'gotoRegion', address: addr });
        return;
      }
      params.pushNavHistory();
      params.state.cursor = instrIndex;
      if (params.checkAndLoadPage()) {
        params.updateCursor();
      }
    };

    this.bottomPanelRuntime.refresh = (tab: BottomTab) => {
      if (tab === 'callstack') {
        const regs = params.debugPanelRegisters();
        if (regs) {
          const typedFrames = params.buildCallStackFrames(regs, params.debugPanelStackBytes(), params.debugPanelStackBase(), params.state.symLabels ?? []);
          params.signals.callStack.set(typedFrames);
          params.bottomPanelCallStack.set(typedFrames);
        }
      } else if (tab === 'threads') {
        params.vscode.postMessage({ type: 'getThreads' });
      } else if (tab === 'breakpoints') {
        params.bottomPanelBreakpoints.set(new Set(params.state.breakpoints));
      } else if (tab === 'watches') {
        params.vscode.postMessage({ type: 'listPineWatches' });
      } else if (tab === 'modules') {
        params.vscode.postMessage({ type: 'getModules' });
      }
    };

    this.bottomPanelRuntime.removeBreakpoint = (addr: number) => {
      if (!isNaN(addr)) {
        params.state.breakpoints.delete(addr);
        params.vscode.postMessage({ type: 'clearBreakpoint', address: addr });
        params.bottomPanelBreakpoints.set(new Set(params.state.breakpoints));
        params.renderDisassembly();
      }
    };

    this.bottomPanelRuntime.clearAllBreakpoints = () => {
      if (params.state.breakpoints.size === 0) { return; }
      params.state.breakpoints.clear();
      params.vscode.postMessage({ type: 'clearAllBreakpoints' });
      params.bottomPanelBreakpoints.set(new Set(params.state.breakpoints));
      params.renderDisassembly();
    };

    this.symbolsPanelRuntime.close = () => {
      params.state.symbolsActive = false;
      params.signals.panels.symbols.set(false);
      this.symbolsPanelVisible.set(false);
    };

    this.symbolsPanelRuntime.refresh = () => {
      params.vscode.postMessage({ type: 'getSymbols' });
    };

    this.symbolsPanelRuntime.navigate = (addrHex: string) => {
      const addr = params.parseHexAddress(addrHex.replace(/^0x/i, ''));
      if (addr === null) { return; }
      const physAddr = params.toPhysicalAddress(addr);
      const physBase = params.toPhysicalAddress(params.state.baseAddress);
      const instrIndex = Math.floor((physAddr - physBase) / 4);
      if (instrIndex < 0 || instrIndex >= params.state.totalInstructions) { return; }
      params.pushNavHistory();
      params.state.cursor = instrIndex;
      if (params.checkAndLoadPage()) {
        params.updateCursor();
      }
    };

    this.symbolsPanelRuntime.addLabel = (address: string, name: string, comment: string) => {
      const addr = params.parseHexAddress(address);
      if (addr === null) { return; }
      params.vscode.postMessage({ type: 'addLabel', address: addr, label: name, comment });
    };

    this.symbolsPanelRuntime.deleteLabel = (addrStr: string) => {
      const addrNum = parseInt(addrStr.replace(/^0x/i, ''), 16);
      params.vscode.postMessage({ type: 'deleteLabel', address: addrNum });
    };

    this.symbolsPanelRuntime.scanStrings = () => {
      this.symbolsPanelStringsStatus.set('Scanning...');
      params.vscode.postMessage({ type: 'scanStrings' });
    };

    this.symbolsPanelRuntime.scanFunctions = () => {
      this.symbolsPanelFunctionsStatus.set('Scanning...');
      params.vscode.postMessage({ type: 'scanFunctions' });
    };

    this.symbolsPanelRuntime.scanPointers = () => {
      this.symbolsPanelPointersStatus.set('Scanning...');
      params.vscode.postMessage({ type: 'scanPointers' });
    };

    this.symbolsPanelRuntime.scanFloats = () => {
      this.symbolsPanelFloatsStatus.set('Scanning...');
      params.vscode.postMessage({ type: 'scanFloats' });
    };

    this.symbolsPanelRuntime.scanIntegers = () => {
      this.symbolsPanelIntegersStatus.set('Scanning...');
      params.vscode.postMessage({ type: 'scanIntegers' });
    };
  }

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
    const signals = this.signals;  // Signal-based state (new architecture)
    const vscode = getVsCodeApi();
    // ── DOM refs ──────────────────────────────────────────────────────────────
    // Note: hex-header and hex-container are rendered by <app-hex-view> and
    // are not accessed directly from this component.
    const setpcToggleBtn = document.getElementById('setpc-toggle-btn')!;
    const headerToolbar  = document.getElementById('header-toolbar')!;

    const symbolsToggleBtn     = document.getElementById('symbols-toggle-btn')!;
    const registersToggleBtn   = document.getElementById('registers-toggle-btn')!;
    const bottomPanelToggleBtn = document.getElementById('bottom-panel-toggle-btn')!;
    const searchToggleBtn      = document.getElementById('search-toggle-btn')!;
    const gotoToggleBtn        = document.getElementById('goto-toggle-btn')!;
    const navBackBtn           = document.getElementById('nav-back-btn') as HTMLButtonElement;

    const debugPanelVisible = this.debugPanelVisible;
    const debugPanelRegisters = this.debugPanelRegisters;
    const debugPanelStackBytes = this.debugPanelStackBytes;
    const debugPanelStackBase = this.debugPanelStackBase;
    const debugPanelWatches = this.debugPanelWatches;
    const bottomPanelVisible = this.bottomPanelVisible;
    const symbolsPanelVisible = this.symbolsPanelVisible;
    const bottomPanelCallStack = this.bottomPanelCallStack;
    const bottomPanelThreads = this.bottomPanelThreads;
    const bottomPanelModules = this.bottomPanelModules;
    const bottomPanelBreakpoints = this.bottomPanelBreakpoints;
    const gotoStatus = this.gotoStatus;
    const gotoValue = this.gotoValue;
    const setpcStatus = this.setpcStatus;
    const buildCallStackFrames = this.buildCallStackFrames.bind(this);
    const createPlaceholderRegs = this.createPlaceholderRegs.bind(this);
    const generateZeroPageRows = this.generateZeroPageRows.bind(this);
    const parseHexAddress = this.parseHexAddress.bind(this);
    const startLiveRefresh = this.startLiveRefresh.bind(this);
    const stopLiveRefresh = this.stopLiveRefresh.bind(this);
    const restartLiveRefresh = this.restartLiveRefresh.bind(this);
    const performLiveRefresh = this.performLiveRefresh.bind(this);
    const handleLiveStatusUpdate = this.handleLiveStatusUpdate.bind(this);
    const handleLiveReset = this.handleLiveReset.bind(this);
    const toPhysicalAddress = this.toPhysicalAddress.bind(this);

    runInInjectionContext(this.injector, () => {
      effect(() => {
        symbolsToggleBtn.classList.toggle('active', signals.panels.symbols());
        registersToggleBtn.classList.toggle('active', signals.panels.registers());
        bottomPanelToggleBtn.classList.toggle('active', signals.panels.bottom());
        searchToggleBtn.classList.toggle('active', signals.panels.search());
        gotoToggleBtn.classList.toggle('active', signals.panels.goto());
        setpcToggleBtn.classList.toggle('active', signals.panels.setpc());
      });
    });

    // ── State reference ───────────────────────────────────────────────────────
    // Alias the service state so all existing `state.xxx` references remain
    // unchanged.  The service owns and initialises the state object.
    const state = svc.state;

    const clamp = (val: number, min: number, max: number) => Math.max(min, Math.min(val, max));
    const getPageStart = (index: number) => Math.floor(index / state.pageSize) * state.pageSize;
    const getLocalIndex = (index: number) => index - getPageStart(index);

    // ── Pointer-label map ─────────────────────────────────────────────────────
    // Kept in sync with state.pointerLabels; keyed by 8-digit upper-case hex
    // address without the "0x" prefix for fast row label lookup.
    let pointerLabelMap = new Map<string, string>();
    const rebuildPointerLabelMap = () => {
      pointerLabelMap = this.buildPointerLabelMap(state.pointerLabels);
    };

    const focusGrid = () => this.focusVirtualGrid();

    const syncEditorSignals = () => this.syncEditorSignals(state, signals);

    const updateCursor = (scrollBlock: ScrollLogicalPosition | null = 'nearest') => this.updateCursor({
      state,
      signals,
      getLocalIndex,
      scrollBlock,
      updateHexView,
      vscode,
    });

    const renderDisassembly = (scrollBlock: ScrollLogicalPosition | null = 'nearest') => this.renderDisassembly({
      state,
      getPageStart,
      pointerLabelMap,
      signals,
      getLocalIndex,
      scrollBlock,
      updateHexView,
      vscode,
    });

    const checkAndLoadPage = () => this.checkAndLoadPage({
      state,
      getPageStart,
      vscode,
      renderDisassembly,
    });
    // Returns the 0-based index of the current PC within the page that starts at
    // `pageStart`, or -1 if the PC is not on that page (or not set).
    const getPcLocalIndex = (pageStart: number): number => {
      if (state.pcAddress === undefined) { return -1; }
      const physPc    = toPhysicalAddress(state.pcAddress);
      const physBase  = toPhysicalAddress(state.baseAddress);
      const pcInstrIdx = Math.floor((physPc - physBase) / 4);
      if (pcInstrIdx < 0 || pcInstrIdx >= state.totalInstructions) { return -1; }
      if (getPageStart(pcInstrIdx) !== pageStart) { return -1; }
      return pcInstrIdx - pageStart;
    };

    // ── Navigation history ────────────────────────────────────────────────────
    const navHistory: number[] = [];
    const MAX_NAV_HISTORY = 100;
    const pushNavHistory = () => this.pushNavHistory({
      navHistory,
      state,
      navBackBtn,
      maxHistory: MAX_NAV_HISTORY,
    });

    const navigateBack = () => this.navigateBack({
      navHistory,
      state,
      navBackBtn,
      checkAndLoadPage,
      updateCursor,
      focusGrid,
    });

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
    this.registerServiceSubscriptions({
      svc,
      state,
      signals,
      headerToolbar,
      registersToggleBtn,
      navBackBtn,
      setpcStatus,
      debugPanelVisible,
      debugPanelRegisters,
      debugPanelStackBytes,
      debugPanelStackBase,
      debugPanelWatches,
      bottomPanelCallStack,
      bottomPanelBreakpoints,
      buildCallStackFrames,
      getPageStart,
      getLocalIndex,
      navHistory,
      createPlaceholderRegs,
      generateZeroPageRows,
      updateHexView,
      renderDisassembly,
      checkAndLoadPage,
      updateCursor,
      syncEditorSignals,
      handleSearchResults: (matches: SearchMatch[]) => handleSearchResults(matches),
      rebuildPointerLabelMap,
      vscode,
      requestRefresh: () => performLiveRefresh({ vscode, state, getPageStart }),
    });

    // Tell the extension host we are ready; it will (re-)send init + first page.
    // VsCodeBridgeService buffers any messages that arrive before subscriptions
    // are set up, so no messages are lost.
    vscode.postMessage({ type: 'ready' });

    const openEditOverlayFn = (
      address: number,
      addressHex: string,
      op: string,
      args: string,
      bytes: string,
      currentLabel: string,
      currentComment: string,
    ) => this.openEditOverlay({
      address,
      addressHex,
      op,
      args,
      bytes,
      currentLabel,
      currentComment,
    });

    window.addEventListener('message', (e: MessageEvent) => {
      this.handleWindowMessage((e.data as Record<string, unknown>) ?? {});
    });

    // ── Converter overlay (Angular-only) ─────────────────────────────────────
    const converterBtn = document.getElementById('converter-btn')!;

    converterBtn.addEventListener('click', () => this.openConverterOverlay());

    // ── Porter overlay (Angular-only) ────────────────────────────────────────
    const porterBtn = document.getElementById('porter-btn')!;

    porterBtn.addEventListener('click', () => this.openPorterOverlay());

    // ── Memory Card overlay (Angular-only) ───────────────────────────────────
    const mcBtn = document.getElementById('mc-btn')!;

    mcBtn.addEventListener('click', () => this.openMemoryCardOverlay());

    // ── HDD overlay (Angular-only) ──────────────────────────────────────────
    const hddBtn = document.getElementById('hdd-btn');
    if (hddBtn) {
      hddBtn.addEventListener('click', () => this.openHddOverlay());
    }

    // ── Scanner overlay (Angular-only) ───────────────────────────────────────
    const scannerBtn = document.getElementById('scanner-btn');
    if (scannerBtn) {
      scannerBtn.addEventListener('click', () => this.openScannerOverlay());
    }

    // ── Label Manager overlay (Angular-only) ─────────────────────────────────
    const labelsBtn = document.getElementById('labels-btn');
    if (labelsBtn) {
      labelsBtn.addEventListener('click', () => this.openLabelManager());
    }

    // ── Search ────────────────────────────────────────────────────────────────
    const clearSearchMatches = () => this.clearSearchMatches({
      state,
      syncEditorSignals,
      renderDisassembly,
    });

    const toggleSearch = () => this.toggleSearch({
      state,
      signals,
      searchToggleBtn,
      clearSearchMatches,
    });

    const jumpToMatch = (matchIndex: number) => this.jumpToSearchMatch({
      matchIndex,
      state,
      checkAndLoadPage,
      updateCursor,
      syncEditorSignals,
    });

    const handleSearchResults = (matches: SearchMatch[]) => this.handleSearchResults({
      matches,
      state,
      syncEditorSignals,
      jumpToMatch,
      renderDisassembly,
    });

    const nextSearchMatch = () => this.nextSearchMatch({
      state,
      jumpToMatch,
    });

    const prevSearchMatch = () => this.prevSearchMatch({
      state,
      jumpToMatch,
    });

    const toggleGoto = () => this.toggleGotoPanel({
      state,
      signals,
      gotoToggleBtn,
      gotoValue,
      gotoStatus,
    });

    const toggleSetPc = () => this.toggleSetPcPanel({
      signals,
      setpcToggleBtn,
      setpcStatus,
    });

    const toggleSymbolsPanel = () => this.toggleSymbolsPanel({
      state,
      signals,
      symbolsPanelVisible,
      vscode,
    });

    const toggleRegistersPanel = () => this.toggleRegistersPanel({
      signals,
      registersToggleBtn,
      debugPanelVisible,
      debugPanelRegisters,
      debugPanelStackBytes,
      debugPanelStackBase,
      createPlaceholderRegs,
    });

    const toggleBottomPanel = () => this.toggleBottomPanel({
      signals,
      bottomPanelToggleBtn,
      bottomPanelVisible,
      bottomPanelBreakpoints,
      state,
    });

    const navigateToAddress = (address: number) => this.navigateToAddress({
      address,
      state,
      pushNavHistory,
      checkAndLoadPage,
      updateCursor,
    });

    const navigateToCurrentRowTarget = () => this.navigateToCurrentRowTarget({
      state,
      getLocalIndex,
      navigateToAddress,
    });

    const toggleBreakpointAtCursor = () => this.toggleBreakpointAtCursor({
      state,
      getLocalIndex,
      vscode,
      renderDisassembly,
    });

    // ── Virtual Grid callbacks ────────────────────────────────────────────────
    // These allow the new VirtualGridComponent to call the outside-zone logic.
    // Note: clamp is already defined earlier in initEditorLogic

      this.configureRuntimeCallbacks({
        state,
        signals,
        clamp,
        toPhysicalAddress,
        parseHexAddress,
        prevSearchMatch,
        nextSearchMatch,
        pushNavHistory,
        checkAndLoadPage,
        updateCursor,
        renderDisassembly,
        navigateBack,
        navigateToCurrentRowTarget,
        toggleBreakpointAtCursor,
        openEditOverlayFn,
        vscode,
        gotoStatus,
        debugPanelRegisters,
        debugPanelStackBytes,
        debugPanelStackBase,
        debugPanelWatches,
        bottomPanelCallStack,
        bottomPanelBreakpoints,
        buildCallStackFrames,
        createPlaceholderRegs,
      });
    // ── Goto ─────────────────────────────────────────────────────────────────

    navBackBtn.addEventListener('click', () => navigateBack());

    // Broadcast every keystroke to the other region panels so their goto
    // input stays in sync with this one. A guard flag prevents the echo
    // message (from the extension host back to this panel) from looping.
    let syncingGotoInput = false;

    // ── Cross-region goto / input-sync service subscriptions ─────────────────
    svc.syncGotoInputReceived$.subscribe(({ value }) => {
      syncingGotoInput = true;
      this.gotoValue.set(value);
      syncingGotoInput = false;
      if (state.gotoActive) { this.gotoStatus.set(''); }
    });

    svc.gotoAddressReceived$.subscribe(({ address }) => {
      // The extension host has routed navigation to this panel because this
      // region owns the address. Navigate directly without bouncing through
      // gotoRegion a second time.
      const physAddr   = toPhysicalAddress(address);
      const physBase   = toPhysicalAddress(state.baseAddress);
      const instrIndex = Math.floor((physAddr - physBase) / 4);
      if (instrIndex < 0 || instrIndex >= state.totalInstructions) { return; }

      pushNavHistory();
      const addrHex = `0x${address.toString(16).toUpperCase()}`;
      this.gotoValue.set(addrHex);
      if (!state.gotoActive) { toggleGoto(); }
      this.gotoStatus.set(`Moved to ${addrHex}`);

      state.cursor = instrIndex;
      state.searchActive = false;
      signals.panels.search.set(false);
      searchToggleBtn.classList.remove('active');
      this.pendingPageScrollBlock = 'center';
      const pageReady = checkAndLoadPage();
      if (pageReady) {
        this.pendingPageScrollBlock = null;
        updateCursor('center');
      }
    });

    svc.gotoRegionNotFoundReceived$.subscribe(() => {
      this.gotoStatus.set('Address out of range');
    });

    setpcToggleBtn.addEventListener('click', toggleSetPc);

    this.bindGlobalShortcuts({
      state,
      toggleGoto,
      toggleSymbolsPanel,
      toggleRegistersPanel,
      toggleBottomPanel,
      toggleSetPc,
      navigateBack,
    });

    symbolsToggleBtn.addEventListener('click', () => toggleSymbolsPanel());
    registersToggleBtn.addEventListener('click', () => toggleRegistersPanel());
    bottomPanelToggleBtn.addEventListener('click', () => toggleBottomPanel());
    searchToggleBtn.addEventListener('click', () => toggleSearch());
    gotoToggleBtn.addEventListener('click', () => toggleGoto());
    // Subscribe to threads response from extension host
    svc.threadsReceived$.subscribe(threads => {
      signals.threads.set(threads ?? []);
      bottomPanelThreads.set(threads ?? []);
    });
    // Subscribe to modules response from extension host
    svc.modulesReceived$.subscribe(modules => {
      bottomPanelModules.set(modules ?? []);
    });

    // ── Cleanup ───────────────────────────────────────────────────────────────
    window.addEventListener('unload', () => {
      stopLiveRefresh();
    });

  }
}



