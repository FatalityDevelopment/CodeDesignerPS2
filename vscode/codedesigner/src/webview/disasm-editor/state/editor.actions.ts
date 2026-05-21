// ═══════════════════════════════════════════════════════════════════════════
// Editor Actions
// ═══════════════════════════════════════════════════════════════════════════
//
// All state mutations happen through EditorActions. Components inject this
// service to trigger state changes. EditorActions updates EditorState signals
// and communicates with the VS Code extension host via VsCodeBridgeService.
//
// ═══════════════════════════════════════════════════════════════════════════

import { Injectable, inject } from '@angular/core';
import { EditorState } from './editor.state';
import { VsCodeBridgeService } from '../../shared/vscode-bridge.service';
import { DisasmRow, SearchMode, SearchMatch, RegisterData } from './types';
import { toPhysAddr } from '../utils/address.utils';

const MAX_NAV_HISTORY = 100;

@Injectable({ providedIn: 'root' })
export class EditorActions {
  private readonly state = inject(EditorState);
  private readonly bridge = inject(VsCodeBridgeService);

  // ═══════════════════════════════════════════════════════════════════════════
  // Cursor Navigation
  // ═══════════════════════════════════════════════════════════════════════════

  /**
   * Move cursor by delta, clamping to valid range.
   * Requests page load if cursor moves to unloaded page.
   */
  moveCursor(delta: number): void {
    const newCursor = Math.max(0, Math.min(
      this.state.cursor() + delta,
      this.state.totalInstructions() - 1
    ));
    this.state.cursor.set(newCursor);
    this.ensurePageLoaded();
  }

  /**
   * Set cursor to specific instruction index.
   */
  setCursor(index: number): void {
    const clamped = Math.max(0, Math.min(index, this.state.totalInstructions() - 1));
    this.state.cursor.set(clamped);
    this.ensurePageLoaded();
  }

  /**
   * Navigate to a specific address, pushing current position to history.
   */
  navigateToAddress(address: number): void {
    // Push current cursor to navigation history
    this.pushNavHistory();

    const physAddr = toPhysAddr(address);
    const physBase = toPhysAddr(this.state.baseAddress());
    const index = Math.floor((physAddr - physBase) / 4);

    if (index >= 0 && index < this.state.totalInstructions()) {
      this.state.cursor.set(index);
      this.ensurePageLoaded();
    }
  }

  /**
   * Navigate back to previous cursor position.
   */
  navigateBack(): void {
    const history = this.state.navHistory();
    if (history.length === 0) return;

    const prev = history[history.length - 1];
    this.state.navHistory.update(h => h.slice(0, -1));
    this.state.cursor.set(prev);
    this.ensurePageLoaded();
  }

  /**
   * Push current cursor position to navigation history.
   */
  private pushNavHistory(): void {
    this.state.navHistory.update(h => {
      const newHistory = [...h, this.state.cursor()];
      if (newHistory.length > MAX_NAV_HISTORY) {
        return newHistory.slice(1);
      }
      return newHistory;
    });
  }

  /**
   * Returns true if there's history to navigate back to.
   */
  canNavigateBack(): boolean {
    return this.state.navHistory().length > 0;
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Page Loading
  // ═══════════════════════════════════════════════════════════════════════════

  /**
   * Request page load from extension host if not already cached.
   */
  ensurePageLoaded(): void {
    const pageStart = this.state.currentPageStart();
    if (!this.state.loadedPages().has(pageStart)) {
      this.bridge.postMessage({ type: 'requestPage', startIndex: pageStart });
    }
  }

  /**
   * Store a loaded page in the cache.
   */
  setPage(startIndex: number, rows: DisasmRow[]): void {
    this.state.loadedPages.update(pages => {
      const newPages = new Map(pages);
      newPages.set(startIndex, rows);
      return newPages;
    });
  }

  /**
   * Clear all cached pages (e.g., on reset).
   */
  clearPages(): void {
    this.state.loadedPages.set(new Map());
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Breakpoints
  // ═══════════════════════════════════════════════════════════════════════════

  /**
   * Toggle breakpoint at address.
   */
  toggleBreakpoint(address: number): void {
    this.state.breakpoints.update(bps => {
      const newBps = new Set(bps);
      if (newBps.has(address)) {
        newBps.delete(address);
        this.bridge.postMessage({ type: 'removeBreakpoint', address });
      } else {
        newBps.add(address);
        this.bridge.postMessage({ type: 'setBreakpoint', address });
      }
      return newBps;
    });
  }

  /**
   * Toggle breakpoint at current cursor position.
   */
  toggleBreakpointAtCursor(): void {
    const row = this.state.currentRow();
    if (!row) return;
    const address = parseInt(row.addr, 16);
    this.toggleBreakpoint(address);
  }

  /**
   * Set breakpoint (without toggle).
   */
  setBreakpoint(address: number): void {
    if (this.state.breakpoints().has(address)) return;
    this.state.breakpoints.update(bps => new Set([...bps, address]));
    this.bridge.postMessage({ type: 'setBreakpoint', address });
  }

  /**
   * Remove breakpoint.
   */
  removeBreakpoint(address: number): void {
    if (!this.state.breakpoints().has(address)) return;
    this.state.breakpoints.update(bps => {
      const newBps = new Set(bps);
      newBps.delete(address);
      return newBps;
    });
    this.bridge.postMessage({ type: 'removeBreakpoint', address });
  }

  /**
   * Clear all breakpoints.
   */
  clearAllBreakpoints(): void {
    for (const addr of this.state.breakpoints()) {
      this.bridge.postMessage({ type: 'removeBreakpoint', address: addr });
    }
    this.state.breakpoints.set(new Set());
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Search
  // ═══════════════════════════════════════════════════════════════════════════

  /**
   * Set search mode.
   */
  setSearchMode(mode: SearchMode): void {
    this.state.searchMode.set(mode);
  }

  /**
   * Perform search with current query and mode.
   */
  performSearch(query: string): void {
    this.state.searchQuery.set(query);
    if (!query.trim()) {
      this.clearSearch();
      return;
    }
    this.bridge.postMessage({
      type: 'search',
      query,
      mode: this.state.searchMode()
    });
  }

  /**
   * Handle search results from extension host.
   */
  setSearchResults(matches: SearchMatch[]): void {
    this.state.searchMatches.set(matches);
    if (matches.length > 0) {
      this.state.currentMatchIndex.set(0);
      this.jumpToMatch(0);
    } else {
      this.state.currentMatchIndex.set(-1);
    }
  }

  /**
   * Jump to specific search match.
   */
  jumpToMatch(index: number): void {
    const matches = this.state.searchMatches();
    if (index < 0 || index >= matches.length) return;

    this.state.currentMatchIndex.set(index);
    const match = matches[index];
    this.state.cursor.set(match.instructionIndex);
    this.ensurePageLoaded();
  }

  /**
   * Jump to next search match.
   */
  nextMatch(): void {
    const matches = this.state.searchMatches();
    if (matches.length === 0) return;
    const next = (this.state.currentMatchIndex() + 1) % matches.length;
    this.jumpToMatch(next);
  }

  /**
   * Jump to previous search match.
   */
  prevMatch(): void {
    const matches = this.state.searchMatches();
    if (matches.length === 0) return;
    const prev = (this.state.currentMatchIndex() - 1 + matches.length) % matches.length;
    this.jumpToMatch(prev);
  }

  /**
   * Clear search state.
   */
  clearSearch(): void {
    this.state.searchQuery.set('');
    this.state.searchMatches.set([]);
    this.state.currentMatchIndex.set(-1);
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Panel Visibility
  // ═══════════════════════════════════════════════════════════════════════════

  /**
   * Toggle a panel's visibility.
   */
  togglePanel(panel: keyof typeof this.state.panels): void {
    this.state.panels[panel].update(v => !v);
  }

  /**
   * Show a panel.
   */
  showPanel(panel: keyof typeof this.state.panels): void {
    this.state.panels[panel].set(true);
  }

  /**
   * Hide a panel.
   */
  hidePanel(panel: keyof typeof this.state.panels): void {
    this.state.panels[panel].set(false);
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Emulator Control
  // ═══════════════════════════════════════════════════════════════════════════

  /**
   * Request emulator pause.
   */
  pause(): void {
    this.bridge.postMessage({ type: 'pause' });
  }

  /**
   * Request emulator resume.
   */
  resume(): void {
    this.bridge.postMessage({ type: 'resume' });
  }

  /**
   * Request single step.
   */
  step(): void {
    this.bridge.postMessage({ type: 'step' });
  }

  /**
   * Set PC to specific address.
   */
  setPC(address: number): void {
    this.bridge.postMessage({ type: 'setPC', address });
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Hex View
  // ═══════════════════════════════════════════════════════════════════════════

  /**
   * Update hex view data.
   */
  setHexData(data: number[], offset: number, highlightAddr = -1, highlightLength = 4): void {
    this.state.hexData.set(data);
    this.state.hexOffset.set(offset);
    this.state.hexHighlightAddr.set(highlightAddr);
    this.state.hexHighlightLength.set(highlightLength);
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Edit Overlay
  // ═══════════════════════════════════════════════════════════════════════════

  /**
   * Open edit overlay for a row.
   */
  openEditOverlay(row: DisasmRow): void {
    this.state.editOverlayAddress.set(parseInt(row.addr, 16));
    this.state.editOverlayOriginalBytes.set(row.bytes.replace(/\s+/g, ''));
    this.state.panels.editOverlay.set(true);
    this.bridge.postMessage({
      type: 'getAddressHistory',
      address: parseInt(row.addr, 16)
    });
  }

  /**
   * Close edit overlay.
   */
  closeEditOverlay(): void {
    this.state.panels.editOverlay.set(false);
  }

  /**
   * Save edit overlay changes.
   */
  saveEdit(label: string, comment: string, hexBytes?: string): void {
    const address = this.state.editOverlayAddress();
    const addressHex = address.toString(16).padStart(8, '0').toUpperCase();

    // Patch bytes if changed
    if (hexBytes) {
      const original = this.state.editOverlayOriginalBytes().toUpperCase();
      const newBytes = hexBytes.replace(/\s+/g, '').toUpperCase();
      if (newBytes !== original && /^[0-9A-F]+$/.test(newBytes) && newBytes.length % 2 === 0) {
        this.bridge.postMessage({ type: 'patchBytes', address, hexBytes: newBytes });
      }
    }

    // Save label
    this.bridge.postMessage({
      type: 'saveLabel',
      address,
      addressHex,
      newLabel: label.trim(),
      newComment: comment.trim()
    });

    this.closeEditOverlay();
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Symbols
  // ═══════════════════════════════════════════════════════════════════════════

  /**
   * Refresh symbols from extension host.
   */
  refreshSymbols(): void {
    this.bridge.postMessage({ type: 'refreshSymbols' });
  }

  /**
   * Trigger string scan.
   */
  scanStrings(): void {
    const pageSize = this.state.pageSize();
    const cursor = this.state.cursor();
    const startIndex = pageSize > 0 ? Math.floor(cursor / pageSize) * pageSize : 0;
    this.bridge.postMessage({ type: 'scanStrings', startIndex });
  }

  /**
   * Trigger function scan.
   */
  scanFunctions(): void {
    this.bridge.postMessage({ type: 'scanFunctions' });
  }

  /**
   * Trigger pointer scan.
   */
  scanPointers(): void {
    this.bridge.postMessage({ type: 'scanPointers' });
  }

  /**
   * Trigger float scan.
   */
  scanFloats(): void {
    this.bridge.postMessage({ type: 'scanFloats' });
  }

  /**
   * Trigger integer scan.
   */
  scanIntegers(): void {
    this.bridge.postMessage({ type: 'scanIntegers' });
  }

  /**
   * Add a new symbol label.
   */
  addLabel(address: string, label: string, comment?: string): void {
    this.bridge.postMessage({
      type: 'addLabel',
      address,
      label,
      comment: comment ?? ''
    });
  }

  /**
   * Delete a symbol label.
   */
  deleteLabel(address: string): void {
    this.bridge.postMessage({ type: 'deleteLabel', address });
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Initialisation
  // ═══════════════════════════════════════════════════════════════════════════

  /**
   * Handle init message from extension host.
   */
  handleInit(data: {
    totalInstructions: number;
    pageSize: number;
    baseAddress: number;
    totalBytes?: number;
    regionLabel?: string;
    isLive?: boolean;
  }): void {
    this.state.totalInstructions.set(data.totalInstructions);
    this.state.pageSize.set(data.pageSize);
    this.state.baseAddress.set(data.baseAddress);
    this.state.totalBytes.set(data.totalBytes ?? data.totalInstructions * 4);
    this.state.regionLabel.set(data.regionLabel ?? '');
    this.state.isLive.set(!!data.isLive);

    // Clear caches
    this.clearPages();
    this.state.navHistory.set([]);
    this.state.cursor.set(0);

    // Request first page
    if (data.totalInstructions > 0) {
      this.ensurePageLoaded();
    }

    // Re-register breakpoints in live mode
    if (data.isLive && this.state.breakpoints().size > 0) {
      for (const addr of this.state.breakpoints()) {
        this.bridge.postMessage({ type: 'setBreakpoint', address: addr });
      }
    }
  }

  /**
   * Handle status update from emulator.
   */
  handleStatus(data: {
    paused: boolean;
    pc: number;
    breakpointHitAddress?: number;
    registers: RegisterData;
    stackBytes: number[];
    stackBase: number;
  }): void {
    this.state.isPaused.set(data.paused);

    if (data.paused) {
      this.state.breakpointHitAddress.set(data.breakpointHitAddress);
      this.state.pcAddress.set(data.pc);
      this.state.registers.set(data.registers);
      this.state.stackBytes.set(data.stackBytes);
      this.state.stackBase.set(data.stackBase);

      // Navigate cursor to PC
      const physPc = toPhysAddr(data.pc);
      const physBase = toPhysAddr(this.state.baseAddress());
      const instrIndex = Math.floor((physPc - physBase) / 4);
      if (instrIndex >= 0 && instrIndex < this.state.totalInstructions()) {
        this.state.cursor.set(instrIndex);
        this.ensurePageLoaded();
      }
    } else {
      // Clear debug state when running
      this.state.breakpointHitAddress.set(undefined);
      this.state.pcAddress.set(undefined);
    }
  }

  /**
   * Handle reset message.
   */
  handleReset(): void {
    this.clearPages();
    this.state.breakpointHitAddress.set(undefined);
    this.state.pcAddress.set(undefined);
    this.state.navHistory.set([]);
    this.state.isPaused.set(false);
    this.state.registers.set(null);

    // Request current page
    this.ensurePageLoaded();
  }
}
