// ═══════════════════════════════════════════════════════════════════════════
// Disassembler Editor Type Definitions
// ═══════════════════════════════════════════════════════════════════════════

// ── Disassembly Row ───────────────────────────────────────────────────────────

export type DataFormatKind = 'byte' | 'half' | 'word' | 'dword' | 'qword' | 'float' | 'double';

export interface DisasmRow {
  addr: string;
  bytes: string;
  label: string;
  op: string;
  args: string;
  comment: string;
  formatted?: boolean;
  dataFormatKind?: DataFormatKind;
  /** Code references to this row, from the bridge's cross-reference index. */
  xrefs?: number;
  /** Data words pointing at this row, from the cross-reference index. */
  dataRefs?: number;
  /** A detected function begins at this row. */
  functionStart?: boolean;
}

// ── Search ────────────────────────────────────────────────────────────────────

export type SearchMode = 'text' | 'bytes' | 'string' | 'regex' | 'refs' | 'immediate';

/** One hit of a search across every memory region. */
export interface GlobalSearchResult {
  address: number;
  region: string;
  text: string;
}

export interface SearchMatch {
  instructionIndex: number;
  address: number;
  field: string;
  start: number;
  length: number;
}

// ── Symbols ───────────────────────────────────────────────────────────────────

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
  address: string;
  pointsTo: string;
}

export interface SymFloat {
  address: string;
  value: number;
  raw: string;
}

export interface SymInteger {
  address: string;
  value: number;
  hex: string;
}

// ── Debug/Emulator ────────────────────────────────────────────────────────────

export interface ThreadEntry {
  id: number;
  status: string;
  pc: number;
  sp: number;
  priority: number;
  entryPc: number;
}

export interface ModuleEntry {
  name:     string;
  version:  number;
  textAddr: string;
  entry:    string;
  gp:       string;
  textSize: number;
  dataSize: number;
  bssSize:  number;
}

export interface RegisterData {
  pc?: number;
  hi?: number;
  lo?: number;
  hi1?: number;
  lo1?: number;
  sa?: number;
  gpr?: number[];
  // Precise 32-bit halves of each 128-bit GPR
  gprLo32?: number[];
  gprHi32?: number[];
  gprUpLo32?: number[];
  gprUpHi32?: number[];
  cop0Regs?: number[];
  fpr?: number[];
  fprAcc?: number;
  fcr0?: number;
  fcr31?: number;
  vu0fX?: number[];
  vu0fY?: number[];
  vu0fZ?: number[];
  vu0fW?: number[];
  vu0AccX?: number;
  vu0AccY?: number;
  vu0AccZ?: number;
  vu0AccW?: number;
  vu0Q?: number;
  vu0P?: number;
  vu0I?: number;
  vu0Status?: number;
  vu0Mac?: number;
  vu0Clip?: number;
  vu0i?: number[];
  gsPriv?: number[];
}

export interface CallStackEntry {
  index: number;
  pc: number;
  functionLabel?: string;
}

// ── Event Payloads ────────────────────────────────────────────────────────────

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

// ── Edit Overlay ──────────────────────────────────────────────────────────────

export interface HistoryEntry {
  name: string;
  date: string;
  value: string;
  disasm?: string;
}

export interface RefEntry {
  /** Hex address of the instruction that references the target (e.g. "00123456"). */
  address: string;
  /** Numeric address used for navigation. */
  addressValue?: number;
  /** Reference kind id, e.g. "controlFlow", "resolved", "pointer", "stringPattern", "crc32". */
  kind?: string;
  /** Human-readable reference kind. */
  kindLabel?: string;
  /** Hex address being referenced. */
  targetAddress?: string;
  /** Human-readable instruction text, e.g. "JAL 0x00567890". Empty when unavailable. */
  instruction: string;
  /** Short description of the match. */
  summary?: string;
  /** Optional extra detail, such as string contents. */
  detail?: string;
  /** Matched value, such as a pointer or CRC32 value. */
  matchedValue?: string;
  /** Source addresses that form a combined reference. */
  sourceAddresses?: string[];
}

// ── Panel Visibility ──────────────────────────────────────────────────────────

export interface PanelVisibility {
  search: boolean;
  goto: boolean;
  setpc: boolean;
  registers: boolean;
  bottom: boolean;
}

// ── Overlay State ─────────────────────────────────────────────────────────────

export interface EditOverlayState {
  visible: boolean;
  address: number;
  instruction: string;
  hexBytes: string;
  originalBytes: string;
  label: string;
  comment: string;
}

export interface ConverterOverlayState {
  visible: boolean;
}

// ── PINE Watchpoints ──────────────────────────────────────────────────────────

export interface PineWatchInfo {
  /** CPU the watchpoint applies to: 'EE' or 'IOP'. */
  Cpu:   string;
  /** Inclusive start address as a hex string (e.g. '0x00200000'). */
  Start: string;
  /** Exclusive end address as a hex string (e.g. '0x00200004'). */
  End:   string;
  /** Access condition: 'read', 'write', or 'read+write'. */
  Cond:  string;
}

// ── Breakpoints ───────────────────────────────────────────────────────────────

/** A breakpoint as kept by the extension host (see breakpointManager.ts). */
export interface BreakpointInfo {
  address: number;
  enabled: boolean;
  /** Expression that must be non-zero to stop (empty = always). */
  condition: string;
  /** "N", ">=N", "==N", ">N" or "%N" (empty = always). */
  hitCondition: string;
  /** Non-empty for a log point: printed to the output channel, then continue. */
  logMessage: string;
  hits: number;
  error?: string;
}

export type BreakpointField = 'condition' | 'hitCondition' | 'logMessage';

// ── Memory watch list ─────────────────────────────────────────────────────────

export type MemWatchType = 'u8' | 'u16' | 'u32' | 's8' | 's16' | 's32' | 'f32' | 'ptr' | 'string';

/** One evaluated row of the typed memory watch list. */
export interface MemWatchRow {
  id: string;
  expression: string;
  type: MemWatchType;
  label?: string;
  /** Final address after the pointer chain. */
  address?: number;
  value?: string;
  error?: string;
}
