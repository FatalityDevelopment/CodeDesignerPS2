# Change Log

All notable changes to the "codedesigner" extension are documented here.

## [Unreleased]

### Added
- Structs / Pointer Manager tree view with nested pointer chains and Go-to-address navigation
- `.cds` language configuration (comment delimiters, bracket pairs) for Code Designer Script files

## [1.3.0] — 2026-03-14

### Added
- **Recompile tab** in the Emulator panel: list ELF executables from a game ISO, select one, and run the static recompiler
- `CodeDesigner: Recompile Game ELF from Disc` command
- Cross-region go-to address navigation: navigating to an address outside the current panel's memory region automatically focuses the correct live panel
- Go-to address input syncs across all open panels in real time

## [1.2.0] — 2026-03-07

### Added
- **CDS tab** in the Emulator panel: Code Designer Script (`.cds`) compiler; compile MIPS assembly source to GameShark-style cheat codes; patch the live emulator or export as a cheat block
- **Cheats tab** in the Emulator panel: paste CodeDesigner cheat codes and click **Update** to apply them to live EE memory
- **Emulator Settings tab**: browse for BIOS ROM and game disc; take and restore memory snapshots from within the panel; import raw binary files into EE/IOP memory
- Live analysis on single step: GPR/register values that changed are highlighted inline on the stepped instruction row
- PC indicator (`▷`) marker on the current program counter row, independent of cursor navigation
- Navigate Back (`Alt+Left`) to return to the previously visited address

## [1.1.0] — 2026-02-28

### Added
- **Debug Panel** (`Ctrl+Alt+R`): collapsible register inspector with tabs for GPR, CP0, FPR, FCR, VU0f, VU0i, GS, and Stack
- **Symbols Panel** (`Ctrl+Alt+S`): collapsible side panel with tabs for Labels, Strings, Functions, Pointers, Floats, and Integers; click any entry to navigate to its address
- **Bottom Panel** (`Ctrl+Alt+T`): Call Stack, Threads (live EE OS thread list), and Breakpoints tabs
- **Hex view** panel aligned alongside the disassembly grid
- **Search** (`Ctrl+F`): text, byte-pattern (`0x12 34 AB`), and ASCII string search modes
- **Go-to address** bar (`Ctrl+G`) for keyboard-driven address navigation
- **Set PC** command to move the program counter without executing code
- `F10` keyboard shortcut for single step (in addition to the toolbar button)
- Status bar shows current address and emulator pause state

## [1.0.0] — 2026-02-26

### Added
- Assembler panel: assemble PS2 EE MIPS assembly text into hex machine code
- Disassembler panel: disassemble hex instruction words into annotated MIPS assembly
- TCP socket client connecting to the C# CodeDesigner backend on `localhost:8765`
- `CodeDesigner: Connect to Server` command
- `CodeDesigner: Disconnect from Server` command
- `CodeDesigner: Send Message to Server` command (raw JSON, for debugging)
- Status-bar indicator showing connection state
- Full PS2 EE instruction set support via the C# backend (core MIPS64, COP0, COP1 FPU,
  COP2/VU0 macro-mode, MMI multimedia extensions)
- Branch disassembly with address offsets when a base address is provided
  (e.g. `beq zero, zero, $00001008 (+2▼)`)
- Live memory disassembler panel with breakpoint support (F9 toggle, F5 resume)
