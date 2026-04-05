// ═══════════════════════════════════════════════════════════════════════════
// Editor State (Angular Signals)
// ═══════════════════════════════════════════════════════════════════════════
//
// Centralised reactive state for the disassembler editor. All state is stored
// as Angular Signals, enabling fine-grained reactivity without zone.js.
//
// Components inject EditorState and read signals directly. Updates happen via
// EditorActions which call signal.set() or signal.update().
//
// ═══════════════════════════════════════════════════════════════════════════

import { Injectable, signal, computed } from '@angular/core';
import {
  DisasmRow,
  SearchMatch,
  SearchMode,
  SymLabel,
  SymString,
  SymFunction,
  SymPointer,
  SymFloat,
  SymInteger,
  RegisterData,
  ThreadEntry,
  CallStackEntry,
} from './types';

@Injectable({ providedIn: 'root' })
export class EditorState {

  // ═══════════════════════════════════════════════════════════════════════════
  // Core Navigation State
  // ═══════════════════════════════════════════════════════════════════════════

  /** Current cursor position (global instruction index, 0-based). */
  readonly cursor = signal(0);

  /** Total number of instructions in the loaded region. */
  readonly totalInstructions = signal(0);

  /** Base address of the loaded region (e.g., 0x00100000). */
  readonly baseAddress = signal(0);

  /** Total bytes in the region. */
  readonly totalBytes = signal(0);

  /** Number of instructions per page (default 1000). */
  readonly pageSize = signal(1000);

  /** Human-readable region label (e.g., "EE RAM"). */
  readonly regionLabel = signal('');

  /** Navigation history stack for back navigation. */
  readonly navHistory = signal<number[]>([]);

  // ═══════════════════════════════════════════════════════════════════════════
  // Page Cache
  // ═══════════════════════════════════════════════════════════════════════════

  /** Cached pages: Map<pageStartIndex, DisasmRow[]>. */
  readonly loadedPages = signal(new Map<number, DisasmRow[]>());

  // ── Computed: Current Page ──────────────────────────────────────────────────

  /** Start index of the page containing the cursor. */
  readonly currentPageStart = computed(() =>
    Math.floor(this.cursor() / this.pageSize()) * this.pageSize()
  );

  /** Rows for the current page (empty array if not loaded). */
  readonly currentPageRows = computed(() =>
    this.loadedPages().get(this.currentPageStart()) ?? []
  );

  /** Local index of cursor within the current page. */
  readonly localCursorIndex = computed(() =>
    this.cursor() - this.currentPageStart()
  );

  /** The DisasmRow at the current cursor position. */
  readonly currentRow = computed(() =>
    this.currentPageRows()[this.localCursorIndex()]
  );

  // ═══════════════════════════════════════════════════════════════════════════
  // Live/Debug State
  // ═══════════════════════════════════════════════════════════════════════════

  /** Whether connected to a live emulator (vs. static dump). */
  readonly isLive = signal(false);

  /** Whether the emulator is currently paused. */
  readonly isPaused = signal(false);

  /** Current PC address when paused (undefined when running). */
  readonly pcAddress = signal<number | undefined>(undefined);

  /** Set of breakpoint addresses. */
  readonly breakpoints = signal(new Set<number>());

  /** Address of breakpoint that was just hit (for highlight). */
  readonly breakpointHitAddress = signal<number | undefined>(undefined);

  /** Set of watch addresses. */
  readonly watches = signal(new Set<number>());

  /** Map of watch address -> current 32-bit value read from memory. */
  readonly watchValues = signal(new Map<number, number>());

  /** Current register values when paused. */
  readonly registers = signal<RegisterData | null>(null);

  /** Stack bytes for stack panel. */
  readonly stackBytes = signal<number[]>([]);

  /** Stack base address. */
  readonly stackBase = signal(0);

  /** Call stack entries. */
  readonly callStack = signal<CallStackEntry[]>([]);

  /** Thread list from emulator. */
  readonly threads = signal<ThreadEntry[]>([]);

  // ── Computed: PC Location ───────────────────────────────────────────────────

  /** Local index of PC within current page (-1 if PC not on this page). */
  readonly pcLocalIndex = computed(() => {
    const pc = this.pcAddress();
    if (pc === undefined) return -1;

    const physPc = toPhysAddrInternal(pc);
    const physBase = toPhysAddrInternal(this.baseAddress());
    const pcInstrIdx = Math.floor((physPc - physBase) / 4);

    if (pcInstrIdx < 0 || pcInstrIdx >= this.totalInstructions()) return -1;

    const pageStart = this.currentPageStart();
    const pcPageStart = Math.floor(pcInstrIdx / this.pageSize()) * this.pageSize();
    if (pcPageStart !== pageStart) return -1;

    return pcInstrIdx - pageStart;
  });

  // ═══════════════════════════════════════════════════════════════════════════
  // Search State
  // ═══════════════════════════════════════════════════════════════════════════

  /** Current search query string. */
  readonly searchQuery = signal('');

  /** Current search mode. */
  readonly searchMode = signal<SearchMode>('text');

  /** Array of search matches. */
  readonly searchMatches = signal<SearchMatch[]>([]);

  /** Index of currently highlighted match (-1 = none). */
  readonly currentMatchIndex = signal(-1);

  // ── Computed: Search ────────────────────────────────────────────────────────

  /** Whether there are any search matches. */
  readonly hasSearchMatches = computed(() => this.searchMatches().length > 0);

  /** Current search match (or undefined). */
  readonly currentMatch = computed(() => {
    const idx = this.currentMatchIndex();
    return idx >= 0 ? this.searchMatches()[idx] : undefined;
  });

  // ═══════════════════════════════════════════════════════════════════════════
  // Panel Visibility
  // ═══════════════════════════════════════════════════════════════════════════

  readonly panels = {
    search: signal(false),
    goto: signal(false),
    setpc: signal(false),
    symbols: signal(false),
    registers: signal(false),
    bottom: signal(false),
    editOverlay: signal(false),
    converterOverlay: signal(false),
    porterOverlay: signal(false),
    scannerOverlay: signal(false),
    structOverlay: signal(false),
    labelManager: signal(false),
  };

  // ═══════════════════════════════════════════════════════════════════════════
  // Hex View
  // ═══════════════════════════════════════════════════════════════════════════

  /** Hex data bytes. */
  readonly hexData = signal<number[]>([]);

  /** Hex view base offset. */
  readonly hexOffset = signal(0);

  /** Address to highlight in hex view. */
  readonly hexHighlightAddr = signal(-1);

  /** Number of bytes to highlight. */
  readonly hexHighlightLength = signal(4);

  // ═══════════════════════════════════════════════════════════════════════════
  // Symbols
  // ═══════════════════════════════════════════════════════════════════════════

  readonly symLabels = signal<SymLabel[]>([]);
  readonly symStrings = signal<SymString[]>([]);
  readonly symFunctions = signal<SymFunction[]>([]);
  readonly symPointers = signal<SymPointer[]>([]);
  readonly symFloats = signal<SymFloat[]>([]);
  readonly symIntegers = signal<SymInteger[]>([]);

  /** Live-resolved labels from the Pointer Manager. */
  readonly pointerLabels = signal<SymLabel[]>([]);

  // ── Computed: Pointer Label Map ─────────────────────────────────────────────

  /** Map of normalised address -> label for fast lookup during rendering. */
  readonly pointerLabelMap = computed(() => {
    const map = new Map<string, string>();
    for (const entry of this.pointerLabels()) {
      const key = entry.address.replace(/^0x/i, '').toUpperCase().padStart(8, '0');
      map.set(key, entry.label);
    }
    return map;
  });

  // ═══════════════════════════════════════════════════════════════════════════
  // Edit Overlay State
  // ═══════════════════════════════════════════════════════════════════════════

  readonly editOverlayAddress = signal(0);
  readonly editOverlayOriginalBytes = signal('');

  // ═══════════════════════════════════════════════════════════════════════════
  // Symbol Panel Pagination
  // ═══════════════════════════════════════════════════════════════════════════

  readonly symPage = {
    strings: signal(0),
    functions: signal(0),
    pointers: signal(0),
    floats: signal(0),
    integers: signal(0),
  };
}

// ═══════════════════════════════════════════════════════════════════════════
// Internal Helpers (copied here to avoid circular imports)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Translate any virtual/logical EE address to its physical equivalent.
 * KSEG0/KSEG1 (0x80000000–0xBFFFFFFF): strip the top 3 bits.
 * EE RAM uncached mirror (0x20000000–0x21FFFFFF): subtract 0x20000000.
 * EE RAM uncached+accelerated (0x30000000–0x31FFFFFF): subtract 0x30000000.
 * All other addresses pass through.
 */
function toPhysAddrInternal(addr: number): number {
  const v = addr >>> 0;
  if (v >= 0x80000000 && v <= 0xBFFFFFFF) return (v & 0x1FFFFFFF) >>> 0;
  if (v >= 0x20000000 && v <= 0x21FFFFFF) return (v - 0x20000000) >>> 0;
  if (v >= 0x30000000 && v <= 0x31FFFFFF) return (v - 0x30000000) >>> 0;
  return v;
}
