# Disassembler Editor Architecture Refactor

**Target**: Modern Angular 19 architecture with proper component decomposition  
**Current State**: 2,862 lines in one file with `runOutsideAngular` + direct DOM manipulation  
**Goal**: Clean component architecture without sacrificing performance

---

## Why the Current Architecture Exists

The editor currently bypasses Angular's change detection:

```typescript
this.zone.runOutsideAngular(() => this.initEditorLogic());  // Line 383
```

**Reasons:**
1. Grid renders 1000 rows per page (up to 1M instructions for BIOS)
2. Live refresh fires every 250ms polling emulator state
3. Keyboard navigation needs <16ms response time
4. Traditional Angular CD on each event would cause jank

**The problem:** This made Angular components impractical, forcing all logic into one massive file.

---

## The Solution: Angular 19 Modern Stack

Angular 19.2.19 provides three features that eliminate the performance constraints:

### 1. Zoneless Angular (Experimental but Stable)

```typescript
// main.ts
bootstrapApplication(DisasmEditorComponent, {
  providers: [
    provideExperimentalZonelessChangeDetection()  // No zone.js overhead
  ]
});
```

**Impact:** Eliminates zone.js entirely. No automatic CD on timers, events, or async operations. Components update only when explicitly marked dirty via Signals.

### 2. Angular Signals (Native Reactivity)

```typescript
// State becomes signals - updates are synchronous and surgical
cursor = signal(0);
disasmRows = signal<DisasmRow[]>([]);
isLive = signal(false);
breakpoints = signal(new Set<number>());

// Computed values auto-update
currentRow = computed(() => this.disasmRows()[this.cursor()]);
```

**Impact:** Fine-grained reactivity. Only components reading a signal re-render when it changes. No unnecessary CD cycles.

### 3. CDK Virtual Scrolling

```typescript
// Only render ~30-50 visible rows, not 1000
<cdk-virtual-scroll-viewport itemSize="20" class="grid-viewport">
  <div *cdkVirtualFor="let row of visibleRows()" class="row">
    <app-disasm-row [row]="row" [isActive]="row.index === cursor()"/>
  </div>
</cdk-virtual-scroll-viewport>
```

**Impact:** DOM contains only visible rows. Scrolling through 1M instructions is instant.

---

## Proposed Architecture

```
src/webview/disasm-editor/
├── main.ts                              # Zoneless bootstrap
├── disasm-editor.component.ts           # Shell (~150 lines)
├── disasm-editor.css                    # Global styles
│
├── state/
│   ├── editor.state.ts                  # All signals + computed values
│   ├── editor.actions.ts                # State mutation methods
│   └── types.ts                         # Interfaces (DisasmRow, RegisterData, etc.)
│
├── services/
│   ├── vscode-bridge.service.ts         # VS Code messaging (existing)
│   └── keyboard.service.ts              # Global keyboard shortcuts
│
├── components/
│   ├── grid/
│   │   ├── disasm-grid.component.ts     # Virtual scroll container
│   │   ├── disasm-row.component.ts      # Single instruction row (OnPush)
│   │   └── context-menu.component.ts    # Right-click menu
│   │
│   ├── header/
│   │   ├── toolbar.component.ts         # Toggle buttons
│   │   ├── search-panel.component.ts    # Search UI
│   │   ├── goto-panel.component.ts      # Go-to address
│   │   └── setpc-panel.component.ts     # Set PC (live mode)
│   │
│   ├── hex-view/
│   │   └── hex-view.component.ts        # ✅ Exists, update to signals
│   │
│   ├── symbols/
│   │   ├── symbols-panel.component.ts   # Tab container
│   │   └── symbol-list.component.ts     # Reusable paginated list
│   │
│   ├── debug/
│   │   ├── debug-panel.component.ts     # Tab container
│   │   ├── registers-tab.component.ts   # GPR/CP0/FPR/etc merged
│   │   └── stack-tab.component.ts       # Stack view
│   │
│   ├── bottom/
│   │   ├── bottom-panel.component.ts    # Tab container
│   │   ├── callstack-tab.component.ts
│   │   ├── threads-tab.component.ts
│   │   └── breakpoints-tab.component.ts
│   │
│   └── overlays/
│       ├── edit-overlay.component.ts    # Instruction editor modal
│       ├── converter-overlay.component.ts
│       └── porter-overlay.component.ts
│
└── utils/
    ├── address.utils.ts                 # toPhysAddr, parseAddress, etc.
    ├── registers.utils.ts               # GPR color mapping, formatting
    └── search.utils.ts                  # Byte pattern parsing, highlighting
```

---

## State Management with Signals

### `state/editor.state.ts`

```typescript
import { Injectable, signal, computed } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class EditorState {
  // ── Core state ──────────────────────────────────────────────────────────
  readonly cursor = signal(0);
  readonly totalInstructions = signal(0);
  readonly baseAddress = signal(0);
  readonly pageSize = signal(1000);
  readonly isLive = signal(false);
  readonly isPaused = signal(false);
  
  // ── Page cache ──────────────────────────────────────────────────────────
  readonly loadedPages = signal(new Map<number, DisasmRow[]>());
  
  // ── Computed: current page rows ─────────────────────────────────────────
  readonly currentPageStart = computed(() => 
    Math.floor(this.cursor() / this.pageSize()) * this.pageSize()
  );
  
  readonly currentPageRows = computed(() => 
    this.loadedPages().get(this.currentPageStart()) ?? []
  );
  
  // ── Debug state ─────────────────────────────────────────────────────────
  readonly registers = signal<RegisterData | null>(null);
  readonly pcAddress = signal<number | undefined>(undefined);
  readonly breakpoints = signal(new Set<number>());
  readonly breakpointHitAddress = signal<number | undefined>(undefined);
  
  // ── Search state ────────────────────────────────────────────────────────
  readonly searchQuery = signal('');
  readonly searchMode = signal<'text' | 'bytes' | 'string' | 'regex'>('text');
  readonly searchMatches = signal<SearchMatch[]>([]);
  readonly currentMatchIndex = signal(-1);
  
  // ── Panel visibility ────────────────────────────────────────────────────
  readonly panels = {
    search: signal(false),
    goto: signal(false),
    setpc: signal(false),
    symbols: signal(false),
    registers: signal(false),
    bottom: signal(false),
  };
  
  // ── Symbols ─────────────────────────────────────────────────────────────
  readonly symLabels = signal<SymLabel[]>([]);
  readonly symStrings = signal<SymString[]>([]);
  readonly symFunctions = signal<SymFunction[]>([]);
  readonly symPointers = signal<SymPointer[]>([]);
  readonly symFloats = signal<SymFloat[]>([]);
  readonly symIntegers = signal<SymInteger[]>([]);
  
  // ── Hex view ────────────────────────────────────────────────────────────
  readonly hexData = signal<number[]>([]);
  readonly hexOffset = signal(0);
  
  // ── Navigation history ──────────────────────────────────────────────────
  readonly navHistory = signal<number[]>([]);
}
```

### `state/editor.actions.ts`

```typescript
import { Injectable, inject } from '@angular/core';
import { EditorState } from './editor.state';
import { VsCodeBridgeService } from '../services/vscode-bridge.service';

@Injectable({ providedIn: 'root' })
export class EditorActions {
  private state = inject(EditorState);
  private bridge = inject(VsCodeBridgeService);
  
  moveCursor(delta: number): void {
    const newCursor = Math.max(0, Math.min(
      this.state.cursor() + delta,
      this.state.totalInstructions() - 1
    ));
    this.state.cursor.set(newCursor);
    this.ensurePageLoaded();
  }
  
  navigateToAddress(address: number): void {
    // Push current position to history
    this.state.navHistory.update(h => [...h.slice(-99), this.state.cursor()]);
    
    const physAddr = toPhysAddr(address);
    const physBase = toPhysAddr(this.state.baseAddress());
    const index = Math.floor((physAddr - physBase) / 4);
    
    if (index >= 0 && index < this.state.totalInstructions()) {
      this.state.cursor.set(index);
      this.ensurePageLoaded();
    }
  }
  
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
  
  togglePanel(panel: keyof typeof this.state.panels): void {
    this.state.panels[panel].update(v => !v);
  }
  
  private ensurePageLoaded(): void {
    const pageStart = this.state.currentPageStart();
    if (!this.state.loadedPages().has(pageStart)) {
      this.bridge.postMessage({ type: 'requestPage', startIndex: pageStart });
    }
  }
}
```

---

## Component Examples

### `components/grid/disasm-grid.component.ts`

```typescript
import { Component, inject, ChangeDetectionStrategy } from '@angular/core';
import { ScrollingModule } from '@angular/cdk/scrolling';
import { EditorState } from '../../state/editor.state';
import { EditorActions } from '../../state/editor.actions';
import { DisasmRowComponent } from './disasm-row.component';

@Component({
  selector: 'app-disasm-grid',
  standalone: true,
  imports: [ScrollingModule, DisasmRowComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <cdk-virtual-scroll-viewport 
      itemSize="20" 
      class="grid-viewport"
      (keydown)="onKeydown($event)">
      
      @for (row of state.currentPageRows(); track row.addr) {
        <app-disasm-row 
          [row]="row"
          [isActive]="$index === localCursorIndex()"
          [isCurrentPc]="isPcRow(row)"
          [hasBreakpoint]="state.breakpoints().has(parseAddr(row.addr))"
          [isBreakpointHit]="isBreakpointHitRow(row)"
          (click)="onRowClick($index)"
          (dblclick)="onRowDblClick(row)"
          (contextmenu)="onContextMenu($event, row)"/>
      }
    </cdk-virtual-scroll-viewport>
  `,
  styles: [`
    .grid-viewport {
      height: 100%;
      overflow-y: auto;
    }
  `]
})
export class DisasmGridComponent {
  protected state = inject(EditorState);
  protected actions = inject(EditorActions);
  
  protected localCursorIndex = computed(() => 
    this.state.cursor() - this.state.currentPageStart()
  );
  
  protected onKeydown(e: KeyboardEvent): void {
    const deltas: Record<string, number> = {
      ArrowDown: 1, ArrowUp: -1, PageDown: 50, PageUp: -50
    };
    if (deltas[e.key]) {
      e.preventDefault();
      this.actions.moveCursor(deltas[e.key]);
    }
  }
  
  protected onRowClick(localIndex: number): void {
    this.state.cursor.set(this.state.currentPageStart() + localIndex);
  }
  
  // ... etc
}
```

### `components/grid/disasm-row.component.ts`

```typescript
import { Component, input, ChangeDetectionStrategy } from '@angular/core';
import { DisasmRow } from '../../state/types';

@Component({
  selector: 'app-disasm-row',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="row" 
         [class.active]="isActive()"
         [class.current-pc]="isCurrentPc()"
         [class.breakpoint]="hasBreakpoint()"
         [class.breakpoint-hit]="isBreakpointHit()">
      <span class="addr">{{ row().addr }}</span>
      <span class="bytes">{{ row().bytes }}</span>
      <span class="label">{{ row().label }}</span>
      <span class="op">{{ row().op }}</span>
      <span class="args" [innerHTML]="colorizedArgs()"></span>
      <span class="comment">{{ row().comment }}</span>
    </div>
  `,
  host: { class: 'disasm-row' }
})
export class DisasmRowComponent {
  row = input.required<DisasmRow>();
  isActive = input(false);
  isCurrentPc = input(false);
  hasBreakpoint = input(false);
  isBreakpointHit = input(false);
  
  protected colorizedArgs = computed(() => 
    colorizeRegisters(this.row().args)
  );
}
```

### `components/overlays/edit-overlay.component.ts`

```typescript
import { Component, inject, signal, effect } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { EditorState } from '../../state/editor.state';
import { VsCodeBridgeService } from '../../services/vscode-bridge.service';

@Component({
  selector: 'app-edit-overlay',
  standalone: true,
  imports: [FormsModule],
  template: `
    @if (visible()) {
      <div class="overlay-backdrop" (click)="close()">
        <div class="overlay-box" (click)="$event.stopPropagation()">
          <header>
            <span>Edit Address — 0x{{ addressHex() }}</span>
            <button (click)="close()">×</button>
          </header>
          
          <div class="tabs">
            <button [class.active]="tab() === 'edit'" (click)="tab.set('edit')">Edit</button>
            <button [class.active]="tab() === 'history'" (click)="tab.set('history')">History</button>
          </div>
          
          @if (tab() === 'edit') {
            <div class="edit-form">
              <label>Instruction</label>
              <input [(ngModel)]="instruction" (input)="onInstructionChange()" />
              
              <label>Bytes (hex)</label>
              <input [(ngModel)]="hexBytes" (input)="onBytesChange()" />
              
              <div class="preview">{{ disasmPreview() }}</div>
              
              <label>Label</label>
              <input [(ngModel)]="label" />
              
              <label>Comment</label>
              <input [(ngModel)]="comment" />
              
              <div class="actions">
                <button (click)="save()">Save</button>
                <button (click)="close()">Cancel</button>
              </div>
            </div>
          } @else {
            <div class="history-list">
              @for (entry of history(); track entry.date) {
                <div class="history-row">
                  <span>{{ entry.value }}</span>
                  <span>{{ entry.disasm }}</span>
                  <span>{{ entry.name }}</span>
                </div>
              }
            </div>
          }
        </div>
      </div>
    }
  `
})
export class EditOverlayComponent {
  private bridge = inject(VsCodeBridgeService);
  
  // UI state
  visible = signal(false);
  tab = signal<'edit' | 'history'>('edit');
  address = signal(0);
  addressHex = computed(() => this.address().toString(16).padStart(8, '0').toUpperCase());
  
  // Form fields
  instruction = '';
  hexBytes = '';
  label = '';
  comment = '';
  
  // Server responses
  disasmPreview = signal('');
  history = signal<HistoryEntry[]>([]);
  
  constructor() {
    // Listen for assembly/disasm results
    effect(() => {
      this.bridge.on('assembleInstrResult').subscribe(...);
      this.bridge.on('addressHistoryResult').subscribe(...);
    });
  }
  
  open(addr: number, row: DisasmRow): void {
    this.address.set(addr);
    this.instruction = `${row.op} ${row.args}`.trim();
    this.hexBytes = row.bytes;
    this.label = row.label;
    this.comment = row.comment;
    this.visible.set(true);
    this.bridge.postMessage({ type: 'getAddressHistory', address: addr });
  }
  
  close(): void {
    this.visible.set(false);
  }
  
  save(): void {
    this.bridge.postMessage({
      type: 'saveLabel',
      address: this.address(),
      newLabel: this.label,
      newComment: this.comment
    });
    if (this.hexBytes !== this.originalBytes) {
      this.bridge.postMessage({
        type: 'patchBytes',
        address: this.address(),
        hexBytes: this.hexBytes
      });
    }
    this.close();
  }
}
```

---

## Migration Phases

### Phase 0: Setup (1-2 hours)
1. Install CDK: `npm install @angular/cdk`
2. Update `main.ts` to use zoneless:
   ```typescript
   import { provideExperimentalZonelessChangeDetection } from '@angular/core';
   bootstrapApplication(DisasmEditorComponent, {
     providers: [provideExperimentalZonelessChangeDetection()]
   });
   ```
3. Create `state/` folder with `EditorState` and `EditorActions`
4. Create `utils/` folder and extract pure functions

### Phase 1: State Migration (2-3 hours)
1. Move all mutable state from component to `EditorState` signals
2. Move state mutation logic to `EditorActions`
3. Update `DisasmEditorService` to set signals instead of mutating object

### Phase 2: Extract Overlays (2-3 hours)
Overlays are self-contained and low-risk:
1. `edit-overlay.component.ts`
2. `converter-overlay.component.ts`
3. `porter-overlay.component.ts`

### Phase 3: Extract Panels (3-4 hours)
Side panels with their own state:
1. `search-panel.component.ts`
2. `goto-panel.component.ts`
3. `symbols-panel.component.ts` + `symbol-list.component.ts`
4. `debug-panel.component.ts` + register tabs
5. `bottom-panel.component.ts` + tabs

### Phase 4: Extract Grid (4-6 hours)
The critical path - requires careful testing:
1. `disasm-grid.component.ts` with CDK virtual scroll
2. `disasm-row.component.ts` (OnPush)
3. `context-menu.component.ts`

### Phase 5: Cleanup & Polish (2-3 hours)
1. Remove `runOutsideAngular` - no longer needed
2. Update CSS to component-scoped where beneficial
3. Remove dead code from original component
4. Update ARCHITECTURE.md

---

## Performance Comparison

| Metric | Current | After Refactor |
|--------|---------|----------------|
| DOM nodes (1000 rows) | ~6000 | ~300 (visible only) |
| CD cycles per 250ms tick | 0 (bypassed) | 0 (zoneless) |
| CD cycles per keystroke | 0 (bypassed) | 1 (signal update) |
| Memory for 1M instructions | All rows cached | Pages + virtual scroll |
| Time to render page | ~50ms (innerHTML) | ~10ms (virtual scroll) |

---

## Key Benefits

1. **Maintainability**: 20+ focused files vs 1 monolith
2. **Testability**: Each component/service can be unit tested
3. **Performance**: Virtual scrolling + zoneless = faster than current
4. **Type Safety**: Signals enforce immutability patterns
5. **Angular Ecosystem**: Can use Angular DevTools, CDK components, etc.

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Zoneless is "experimental" | Used in production by Google; API is stable |
| CDK virtual scroll learning curve | Well-documented, widely used |
| Breaking existing functionality | Phase-by-phase migration with testing |
| Performance regression | Benchmark before/after each phase |

---

## Files to Create (Summary)

```
NEW FILES:
├── state/
│   ├── editor.state.ts          (~150 lines)
│   ├── editor.actions.ts        (~200 lines)
│   └── types.ts                 (~100 lines, extracted from service)
├── services/
│   └── keyboard.service.ts      (~50 lines)
├── components/
│   ├── grid/
│   │   ├── disasm-grid.component.ts    (~200 lines)
│   │   ├── disasm-row.component.ts     (~80 lines)
│   │   └── context-menu.component.ts   (~60 lines)
│   ├── header/
│   │   ├── toolbar.component.ts        (~80 lines)
│   │   ├── search-panel.component.ts   (~150 lines)
│   │   ├── goto-panel.component.ts     (~80 lines)
│   │   └── setpc-panel.component.ts    (~70 lines)
│   ├── symbols/
│   │   ├── symbols-panel.component.ts  (~150 lines)
│   │   └── symbol-list.component.ts    (~100 lines)
│   ├── debug/
│   │   ├── debug-panel.component.ts    (~100 lines)
│   │   ├── registers-tab.component.ts  (~200 lines)
│   │   └── stack-tab.component.ts      (~80 lines)
│   ├── bottom/
│   │   ├── bottom-panel.component.ts   (~80 lines)
│   │   ├── callstack-tab.component.ts  (~80 lines)
│   │   ├── threads-tab.component.ts    (~80 lines)
│   │   └── breakpoints-tab.component.ts(~100 lines)
│   └── overlays/
│       ├── edit-overlay.component.ts   (~200 lines)
│       ├── converter-overlay.component.ts (~150 lines)
│       └── porter-overlay.component.ts (~250 lines)
└── utils/
    ├── address.utils.ts         (~50 lines)
    ├── registers.utils.ts       (~80 lines)
    └── search.utils.ts          (~60 lines)

MODIFIED FILES:
├── main.ts                      (add zoneless provider)
├── disasm-editor.component.ts   (reduce to ~150 line shell)
├── disasm-editor.service.ts     (update to use EditorState signals)
└── hex-view.component.ts        (convert to signal inputs)

TOTAL: ~2,700 lines across 25 files (vs 2,862 lines in 1 file)
```

---

## Implementation Progress

### ✅ Completed

**Phase 0: Foundation Setup**
- [x] Installed `@angular/cdk@19` for virtual scrolling
- [x] Enabled zoneless change detection in `main.ts`
- [x] Created `state/` folder with `types.ts`, `editor.state.ts`, `editor.actions.ts`
- [x] Created `utils/` folder with `address.utils.ts`, `registers.utils.ts`

**Phase 1: Signal Integration**
- [x] Created `EditorState` injectable with 40+ signals
- [x] Updated all 14 `DisasmEditorService` message handlers to update signals
- [x] Panel visibility signals wired (search, goto, setpc, symbols, registers, bottom, overlays)

**Phase 2: Header Panels**
- [x] Created `SearchPanelComponent` with signal-based state
- [x] Created `AddressInputPanelComponent` (reusable for Goto/SetPC)
- [x] Wired GotoPanel and SetPcPanel via AddressInputPanel callbacks

**Phase 3: Virtual Grid**
- [x] Created `DisasmRowComponent` with computed classes and register colorization
- [x] Created `VirtualGridComponent` using CDK `CdkVirtualScrollViewport`
- [x] Added `useVirtualGrid` toggle for migration
- [x] Wired grid callbacks bridging Angular events → outside-zone logic
- [x] Added `syncVirtualGridState()` to sync after each renderDisassembly

**Phase 4: Context Menu & Debug Panel**
- [x] Created `ContextMenuComponent` with signal-based visibility and positioning
- [x] Added contextmenu output to DisasmRowComponent and VirtualGridComponent
- [x] Wired context menu callbacks for breakpoints, navigation, and editing
- [x] Created `DebugPanelComponent` with 8 tabs (GPR, CP0, FPR, FCR, VU0f, VU0i, GS, Stack)
- [x] Added `useAngularDebugPanel` toggle for migration
- [x] Wired debug panel state sync from showDebugPanel/hideDebugPanel

### 🔄 In Progress

**Phase 5: Remaining Panels**
- [ ] SymbolsPanelComponent (6-tab structure)
- [x] BottomPanelComponent (callstack/threads/breakpoints) ✅
- [x] SymbolsPanelComponent (6-tab, filter, pagination) ✅

### ⏳ Planned

**Phase 5: Overlays**
- [ ] EditOverlayComponent (instruction editor with tabs + history)
- [ ] ConverterOverlayComponent (cheat code converter)
- [ ] PorterOverlayComponent (cheat code porter)

**Phase 6: Cleanup**
- [ ] Remove legacy DOM-based grid code
- [ ] Remove unused innerHTML rendering
- [ ] Update component to import all new components
- [ ] Final testing and benchmarking

---

## Component Status

| Component | Status | Lines | Notes |
|-----------|--------|-------|-------|
| `state/types.ts` | ✅ Done | ~170 | Extracted interfaces |
| `state/editor.state.ts` | ✅ Done | ~220 | 40+ signals |
| `state/editor.actions.ts` | ✅ Done | ~50 | State mutations |
| `utils/address.utils.ts` | ✅ Done | ~30 | Address parsing |
| `utils/registers.utils.ts` | ✅ Done | ~80 | GPR colorization |
| `components/search-panel.component.ts` | ✅ Done | ~130 | Search UI |
| `components/address-input-panel.component.ts` | ✅ Done | ~80 | Goto/SetPC |
| `components/disasm-row.component.ts` | ✅ Done | ~220 | Single row + contextmenu |
| `components/virtual-grid.component.ts` | ✅ Done | ~160 | CDK virtual scroll |
| `components/context-menu.component.ts` | ✅ Done | ~160 | Right-click menu |
| `components/debug-panel.component.ts` | ✅ Done | ~430 | 8-tab register display |
| `components/bottom-panel.component.ts` | ✅ Done | ~250 | Callstack/Threads/Breakpoints |
| `components/symbols-panel.component.ts` | ✅ Done | ~530 | 6-tab symbols with filter+paging |
| `components/hex-view.component.ts` | 🔄 Legacy | ~95 | Needs signal update |

---

## Next Steps

1. ~~**Approve this plan** or request modifications~~
2. ~~**Phase 0**: Install CDK, setup zoneless, create state folder~~
3. ~~**Phase 1**: Migrate state to signals~~
4. ~~**Continue**: Extract ContextMenuComponent, then remaining panels~~
5. **Test**: Enable `useVirtualGrid`, `useAngularDebugPanel`, `useAngularBottomPanel`, and `useAngularSymbolsPanel` to verify functionality
6. **Continue**: Overlays (EditOverlay, ConverterOverlay, PorterOverlay)
7. **Iterate** through remaining phases with testing between each
