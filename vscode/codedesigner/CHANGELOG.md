# Change Log

All notable changes to the "codedesigner" extension are documented here.

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
