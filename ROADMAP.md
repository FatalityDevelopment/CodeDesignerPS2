# CodeDesigner Development Roadmap

> Planning document for upcoming refactoring and feature development tasks. Last updated: 2026-05-05

---

## Refactoring Tasks

### 1. Component Refactor: Label Manager Table Component

**Status**: Pending  
**Priority**: High  
**Complexity**: Medium

**Overview**:
Split the label manager table into a modular, reusable component library that can be used across multiple views. This component will serve as the foundation for the Snapshot Manager and other table-based interfaces.

**Requirements**:
- [ ] Extract paginated table component with column search/filter
- [ ] Create live address table with pagination, filtering, and type indicators
- [ ] Implement scan type toolbar as a separate composable
- [ ] Ensure full reusability across different contexts (labels, snapshots, cheats, etc.)
- [ ] Support configurable columns and actions

**Related Files**:
```
src/
  webviews/
    - LabelManager.tsx          (current implementation)
    - components/
      - PaginatedTable.tsx       (NEW - reusable table component)
      - SearchFilter.tsx         (NEW - column search)
      - ScanTypeToolbar.tsx      (NEW - toolbar component)
  extension.ts                   (command registration)
  types/
    - labels.ts                  (type definitions)
```

**Acceptance Criteria**:
- Table is extracted into `components/PaginatedTable.tsx`
- Column configuration is data-driven
- Supports custom row renderers for different data types
- Search/filter works across all columns
- Pagination controls are accessible

---

### 2. Architecture Refactor: Memory Region Navigation

**Status**: Pending  
**Priority**: High  
**Complexity**: High

**Overview**:
Consolidate EE/IOP/BIOS/Scratchpad region logic into the disassembler view, enabling seamless navigation across all PS2 memory regions from a single tab. Currently, region selection is split across multiple views; this refactor unifies the experience.

**Requirements**:
- [ ] Create unified memory map with all regions (EE RAM, IOP RAM, BIOS, Scratchpad, KSEG0, etc.)
- [ ] Move region button logic from disassembler panel into disassembler view
- [ ] Support "goto address" across region boundaries
- [ ] Handle PCSX2 save state file navigation (.raw, .bin, .p2s formats)
- [ ] Prompt user for target region when opening binary files without context

**Related Files**:
```
src/
  webviews/
    - Disassembler.tsx           (main disassembler view)
    - components/
      - MemoryRegionSelector.tsx (NEW - unified region selection)
      - MemoryMap.ts             (NEW - memory layout definitions)
  panels/
    - DisassemblerPanel.ts       (currently holds region buttons)
  types/
    - memory.ts                  (memory region types)
    - pcsx2.ts                   (PCSX2 state handling)
  services/
    - memoryService.ts           (memory I/O operations)
```

**Memory Map Structure**:
```
EE RAM:        0x00000000 - 0x01FFFFFF (32 MB)
KSEG0:         0x80000000 - 0x81FFFFFF (cached EE RAM)
KSEG1:         0xA0000000 - 0xA1FFFFFF (uncached EE RAM)
IOP RAM:       0x1C000000 - 0x1C3FFFFF (4 MB, via IOP mirror)
BIOS:          0x1FC00000 - 0x1FFFFFFF (4 MB)
Scratchpad:    0x70000000 - 0x70003FFF (16 KB)
```

**Acceptance Criteria**:
- Single disassembler tab handles all regions
- Goto command switches regions automatically
- Save state files open in flat file viewer
- Region selection prompts appear when loading .bin/.raw files
- No region buttons in disassembler panel (moved to view)

---

## Feature Development Tasks

### 3. Feature: Snapshot Manager WebView Component

**Status**: Pending  
**Priority**: High  
**Complexity**: Medium

**Overview**:
Create a dedicated snapshot management interface with a paginated table view, allowing users to browse, compare, and manage memory snapshots. Reuses the label manager table component for consistency.

**Requirements**:
- [ ] Create new WebView-based snapshot manager
- [ ] Paginated table: Date/Time | Title | Description | Actions
- [ ] Action buttons: Delete, Diff, Reload, Open
- [ ] Higher-level toolbar with bulk actions
- [ ] Integration with disassembler panel (button to open manager)

**Row Actions**:
- **Delete**: Remove snapshot from storage (confirm before deletion)
- **Diff**: Open Memory Diff Viewer comparing snapshot to current/another snapshot
- **Reload**: Load snapshot state back into PCSX2
- **Open**: View snapshot in flat file viewer

**Related Files**:
```
src/
  webviews/
    - SnapshotManager.tsx         (NEW - main manager view)
    - components/
      - SnapshotTable.tsx         (NEW - reuses PaginatedTable)
      - SnapshotActions.tsx       (NEW - action buttons)
  panels/
    - DisassemblerPanel.ts        (add open manager button)
  services/
    - snapshotService.ts          (snapshot CRUD operations)
  types/
    - snapshot.ts                 (snapshot data structures)
```

**Acceptance Criteria**:
- Snapshots display in paginated table format
- All CRUD operations work correctly
- Delete operations require confirmation
- Manager is accessible from disassembler panel
- Reuses label manager table component

---

### 4. Feature: Memory Diff Viewer WebView Component

**Status**: Pending  
**Priority**: High  
**Complexity**: High

**Overview**:
Create an advanced memory comparison tool with two disassembler views side-by-side, enabling Git-style diffs between memory states. Replaces the cheat porter dialog with a comprehensive diffing interface. Two tabs: Memory Diff and Cheat Code Porter.

#### Tab 1: Memory Diff

**Requirements**:
- [ ] Display two disassembler webview components side-by-side
- [ ] Source/target options: Memory dump (file) or Live memory (PCSX2)
- [ ] Git-style diff highlighting (added, removed, modified)
- [ ] Higher-level controls:
  - Goto textbox: Navigate both views to same address
  - Diff toggle: Enable/disable highlighting
  - Pattern search: Apply byte pattern across both views
- [ ] Synchronized scrolling (optional advanced feature)

**Related Files**:
```
src/
  webviews/
    - MemoryDiffViewer.tsx        (NEW - main diff view)
    - MemoryDiffTab.tsx           (NEW - diff tab implementation)
    - components/
      - SideBySideDisassembler.tsx (NEW - dual disassembler layout)
      - DiffControls.tsx          (NEW - goto, toggle, pattern controls)
      - DiffHighlighter.ts        (NEW - diff highlighting logic)
  types/
    - diff.ts                     (diff data structures)
```

**Acceptance Criteria**:
- Two disassembler views display side-by-side
- Goto updates both views to same address
- Diff highlighting shows changes clearly
- Pattern search works across both memory regions
- Performance acceptable for large memory dumps

#### Tab 2: Cheat Code Porter

**Requirements**:
- [ ] Reuse Snapshot Manager table component
- [ ] Adapt cheat porter backend logic into this tab
- [ ] Display source/target snapshots or memory dumps
- [ ] Show detected vs. ported cheats in table
- [ ] Allow batch operations and code refinement

**Related Files**:
```
src/
  webviews/
    - CheatPorterTab.tsx          (NEW - cheat porter tab)
    - components/
      - CheatPortTable.tsx        (reuses PaginatedTable)
  services/
    - cheatPorterService.ts       (existing backend logic)
    - patternMatcher.ts           (pattern detection)
```

**Acceptance Criteria**:
- Cheat porter logic integrated into new tab
- Table displays source/target information
- Pattern matching produces accurate results
- Ported codes are validated before export

---

### 5. Feature: AI Config Improvements

**Status**: Pending  
**Priority**: Medium  
**Complexity**: Medium

**Overview**:
Enhance the AI configuration interface with better organization and new planning/prompt builder capabilities.

#### New Tabs:

**Tab 1: Conversation History**
- [ ] Display conversation log
- [ ] Search/filter conversations
- [ ] Export/import capabilities
- [ ] Session management

**Tab 2: Prompt Builder/Planner**
- [ ] Visual prompt editor with suggestions
- [ ] Template library for common tasks
- [ ] Prompt testing/preview
- [ ] Version control for prompt templates

**Related Files**:
```
src/
  webviews/
    - AIConfig.tsx               (existing - main AI config)
    - ConversationHistory.tsx    (NEW)
    - PromptBuilder.tsx          (NEW)
  panels/
    - AIConfigPanel.ts           (command registration)
  services/
    - conversationService.ts     (NEW)
    - promptService.ts           (NEW)
```

**Acceptance Criteria**:
- Conversation history displays and is searchable
- Prompt builder allows template creation
- Both tabs integrate with existing AI config
- Data persists across sessions

---

## Cross-Cutting Concerns

### Component Reusability Strategy
- **PaginatedTable**: Base component for all tabular data (labels, snapshots, cheats)
- **DisassemblerView**: Unified memory navigation (all regions, all sources)
- **DiffHighlighter**: Shared highlighting logic across diff viewers

### State Management
- Snapshot state: `snapshotService.ts`
- Label state: `labelService.ts`
- Diff state: `diffService.ts`
- Memory region state: `memoryService.ts`
---

## Implementation Order (Recommended)

1. **PaginatedTable Component** (enables all table-based features)
2. **Memory Region Navigation** (architectural foundation)
3. **Snapshot Manager** (uses PaginatedTable + Memory Region Navigation)
4. **Memory Diff Viewer** (uses all previous work)
5. **Cheat Code Porter Integration** (into Memory Diff)
6. **AI Config Improvements** (standalone feature)

---

## Notes for Planning

- **Component Reuse**: The PaginatedTable component is the critical path item. All other table-based features depend on it.
- **Architectural Consistency**: Memory region logic must be unified before building features that depend on navigation.
- **Performance**: Memory diff viewer handling large dumps (32+ MB) requires optimization—consider virtual scrolling.
- **File Format Support**: Ensure proper handling of .p2s, .raw, .bin, and .asm files across all features.
- **Testing**: Each feature should have clear acceptance criteria before implementation begins.

