# CodeDesigner — PlayStation 2 Toolkit

**CodeDesigner** is a C# library and toolset for the **Sony PlayStation 2 Emotion Engine (EE)**. It provides a complete **assembler**, **disassembler**, **instruction decoder/encoder**, **PS2 emulator**, **cheat engine**, **static recompiler**, and a **VS Code extension** — all verified against [PCSX2](https://pcsx2.net/) reference behaviour.

---

## Project Structure

```
CodeDesigner/
├── CodeDesigner.Ps2.Common/        # Core library: assembler, disassembler, instructions, encoder/decoder
├── CodeDesigner.Ps2.Emulator/      # PS2 emulator: EE + IOP CPUs, hardware peripherals, ELF loader
├── CodeDesigner.Ps2/               # Console application + embedded socket server
├── CodeDesigner.Ps2.McpServer/     # MCP stdio bridge for GitHub Copilot
├── CodeDesigner.Ps2.CdsLanguage/   # Code Designer Script (CDS) compiler and parser
├── CodeDesigner.Ps2.CheatEngine/   # Cheat code engine: apply, timer, condition cheats
├── CodeDesigner.Ps2.CheatPorter/   # Port cheat codes between game revisions
├── CodeDesigner.Ps2.CheatConverter/# Convert cheat codes between formats (AR2, CB, GS, raw)
├── CodeDesigner.Ps2.MemoryCardManager/ # PS2 memory card emulation and management
├── CodeDesigner.Ps2.Recompiler/    # Static recompiler: PS2 ELF → C++ source
├── CodeDesigner.Ps2.TestProject/   # Unit-test suite (1097 tests)
└── vscode/codedesigner/            # VS Code extension frontend (Angular 19 + TypeScript)
```

---

## Quick Start

### Requirements
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- Node.js + npm (for the optional VS Code extension)

### Build

```bash
dotnet build CodeDesigner.Ps2.sln
```

### Run Tests

```bash
dotnet test CodeDesigner.Ps2.TestProject/
```

All 1097 tests should pass.

### Console Application

```bash
cd CodeDesigner.Ps2
dotnet run
```

To start the PS2 emulator with the embedded socket server (port 8765) for the VS Code extension and MCP bridge:

```bash
dotnet run -- --run
```

The VS Code extension can also start the emulator automatically via its **Connect** button.

To start the standalone socket server (port 8766, assembler/disassembler only, no live emulator):

```bash
dotnet run -- --server
```

---

## Features

| Feature | Status |
|---------|--------|
| MIPS EE core instruction decode/encode | ✅ |
| Assembler (text → machine code) | ✅ |
| Disassembler (machine code → text) | ✅ |
| Instruction execution / emulation | ✅ |
| Static analysis / annotation | ✅ |
| COP0 (TLB, ERET, exception registers) | ✅ |
| COP1 FPU (single-precision float) | ✅ |
| COP2/VU0 macro-mode vector instructions | ✅ |
| MMI multimedia extensions | ✅ |
| 64-bit EE-specific behaviour (HI/LO sign-ext) | ✅ |
| FPU flush-to-zero for denormals | ✅ |
| Branch delay slots + branch-likely | ✅ |
| COP0 exception dispatch (SYSCALL, BREAK, overflow, trap) | ✅ |
| ELF32 loader (EE `.elf` into RAM) | ✅ |
| EE timers T0–T3 + INTC | ✅ |
| EE DMAC (10 channels, normal mode) | ✅ |
| SIF (EE↔IOP sub-bus interface) | ✅ |
| IOP (R3000A) emulator + memory bus | ✅ |
| IOP timers T0–T5 | ✅ |
| IOP DMAC (13 channels) | ✅ |
| CDVD subsystem (ISO-9660) | ✅ |
| SIO2 + DualShock 2 pad input | ✅ |
| IOP module loader (IRX/ELF32) | ✅ |
| SPU2 (48-voice ADPCM audio) | ✅ |
| VIF0 / VIF1 (Vector Interface, all UNPACK formats) | ✅ |
| GIF (Graphics Interface, PACKED/REGLIST/IMAGE) | ✅ |
| GS (Graphics Synthesizer, rasteriser + 4 MB VRAM) | ✅ |
| VU1 (Vector Unit 1 microprogram execution + XGKICK) | ✅ |
| TLB (Translation Lookaside Buffer) | ✅ |
| BIOS ROM loader + validation | ✅ |
| Game disc loader (ISO-9660 + ELF boot) | ✅ |
| Dual-CPU scheduler (EE + IOP lock-step) | ✅ |
| Controller input end-to-end pipeline | ✅ |
| COP0 Performance Counters (PCCR/PCR0/PCR1 at reg 25 via sel) | ✅ |
| COP0 Count/Compare timer interrupt (IP7) | ✅ |
| FPU FCSR cause + sticky exception flags | ✅ |
| DMAC chain mode + interleave mode (EE) | ✅ |
| IOP DMAC linked-list mode | ✅ |
| VU0 micro-mode microprogram execution | ✅ |
| GS bilinear texture filtering | ✅ |
| GS fog blending | ✅ |
| GS LOCAL→HOST VRAM readback | ✅ |
| GS CLAMP/REGION_CLAMP/REGION_REPEAT wrap modes | ✅ |
| GS display output (GetFrameBuffer) | ✅ |
| SPU2 ADPCM mixing, ADSR envelopes, reverb | ✅ |
| Memory card emulation (8 MB PS2 MC) | ✅ |
| DVD-Video sector types + TOC | ✅ |
| USB controller (OHCI, IOP INTC bit 22) | ✅ |
| i.LINK / FireWire controller (IOP INTC bit 18) | ✅ |
| DEV9 parallel-port expansion (IOP INTC bit 13) | ✅ |
| VSCode extension frontend | ✅ |
| Breakpoint system (address-based pause + F9 in disasm editor) | ✅ |

---

## PS2 Emotion Engine Architecture

### Core CPU
- **Architecture:** MIPS III / MIPS64 with Sony/Toshiba extensions
- **Clock:** ~294.912 MHz
- **Endianness:** Little-endian
- **Pipeline:** Superscalar, dual-issue, in-order
- **Registers:** 32 × 128-bit GPRs (64-bit integer lane + 64-bit extension), PC, HI, HI1, LO, LO1, SA

### EE-Specific Behaviours (vs standard MIPS64)
- `MULT`/`MULTU`/`DIV`/`DIVU` — 32-bit results **sign-extended to 64 bits** when written to HI/LO
- `MFHI`/`MFLO` — upper 64 bits of destination GPR sign-extended from bit 63
- All 64-bit integer instructions sign-extend result into upper 64-bit lane of 128-bit GPR
- `ERET` — checks ERL bit first (uses ErrorEPC/reg 30), then falls back to EXL/EPC
- FPU (COP1) — **no IEEE 754 denormal support**; all denormal inputs/outputs are flushed to zero
- FPU instruction encoding — `fmt` field (bits\[25:21\] = 0x10) is **not a source register**; `fd`=bits\[10:6\], `fs`=bits\[15:11\], `ft`=bits\[20:16\]
- Unaligned load/store (`LWL`/`LWR`/`SWL`/`SWR`/`LDL`/`LDR`/`SDL`/`SDR`) implemented for little-endian memory layout

---

## Instruction Support

### Core Integer (MIPS III / MIPS64)

| Category | Instructions |
|----------|-------------|
| Arithmetic (32-bit) | `ADD` `ADDU` `SUB` `SUBU` `ADDI` `ADDIU` |
| Arithmetic (64-bit) | `DADD` `DADDU` `DSUB` `DSUBU` `DADDI` `DADDIU` |
| Multiply / Divide (32-bit) | `MULT` `MULTU` `DIV` `DIVU` |
| Multiply / Divide (64-bit) | `DMULT` `DMULTU` `DDIV` `DDIVU` |
| HI/LO Move | `MFHI` `MTHI` `MFLO` `MTLO` |
| Shift (32-bit) | `SLL` `SRL` `SRA` `SLLV` `SRLV` `SRAV` |
| Shift (64-bit) | `DSLL` `DSRL` `DSRA` `DSLLV` `DSRLV` `DSRAV` `DSLL32` `DSRL32` `DSRA32` |
| Logic | `AND` `OR` `NOR` `XOR` `ANDI` `ORI` `XORI` |
| Compare/Set | `SLT` `SLTU` `SLTI` `SLTIU` |
| Conditional Move | `MOVN` `MOVZ` |
| Load Upper | `LUI` |
| Branch | `BEQ` `BNE` `BGEZ` `BGTZ` `BLEZ` `BLTZ` `BEQL` `BNEL` `BGEZL` `BGTZL` `BLEZL` `BLTZL` |
| Branch-and-Link | `BGEZAL` `BLTZAL` `BGEZALL` `BLTZALL` |
| Jump | `J` `JAL` `JR` `JALR` |
| Load | `LB` `LBU` `LH` `LHU` `LW` `LWU` `LWL` `LWR` `LD` `LDL` `LDR` `LQ` `LUI` |
| Store | `SB` `SH` `SW` `SWL` `SWR` `SD` `SDL` `SDR` `SQ` |
| Trap | `SYSCALL` `BREAK` `SYNC` `TEQ` `TNE` `TGE` `TGEU` `TLT` `TLTU` `TEQI` `TNEI` `TGEI` `TGEIU` `TLTI` `TLTIU` |
| Special | `NOP` |
| EE SA register | `MFSA` `MTSA` `MTSAB` `MTSAH` |

### COP0 — System Control

| Instruction | Description |
|-------------|-------------|
| `MFC0` `DMFC0` `CFC0` | Move from COP0 register |
| `MTC0` `DMTC0` `CTC0` | Move to COP0 register |
| `ERET` | Return from exception (checks ERL/EXL, uses ErrorEPC/EPC) |
| `TLBR` | Read indexed TLB entry |
| `TLBWI` | Write indexed TLB entry |
| `TLBWR` | Write random TLB entry |
| `TLBP` | Probe TLB for matching entry |
| `CACHE` | Cache operation |
| `EI` `DI` | Enable / Disable interrupts (EE extension) |

### COP1 — Floating-Point Unit (FPU)

> All FPU operations use **single precision (32-bit)** only. Denormals are flushed to zero (PS2 EE behaviour, differs from IEEE 754 default).

| Instruction | Description |
|-------------|-------------|
| `ADD.S` `SUB.S` `MUL.S` `DIV.S` | Basic arithmetic |
| `SQRT.S` `RSQRT.S` | Square root, reciprocal square root |
| `ABS.S` `NEG.S` `MOV.S` | Unary operations |
| `MADD.S` `MSUB.S` `MADDA.S` `MSUBA.S` | Multiply-accumulate |
| `ADDA.S` `SUBA.S` `MULA.S` | Accumulate variants |
| `MAX.S` `MIN.S` | Min/max |
| `CVT.W.S` `CVT.S.W` | Float/integer conversion |
| `CEIL.W.S` `FLOOR.W.S` `ROUND.W.S` `TRUNC.W.S` | Rounding |
| `C.F.S` `C.EQ.S` `C.LT.S` `C.LE.S` | Comparisons (PS2 EE functs 0x30/0x32/0x34/0x36) |
| `BC1F` `BC1T` `BC1FL` `BC1TL` | Branch on FPU condition code |
| `MFC1` `DMFC1` `CFC1` `MTC1` `DMTC1` `CTC1` | Register moves |
| `LWC1` `SWC1` `LDC1` `SDC1` | FPU load/store |

### COP2 / VU0 Macro-Mode

> 128-bit vector operations. Each register holds 4 × 32-bit float components (x, y, z, w).

| Instruction | Description |
|-------------|-------------|
| `VADD` `VSUB` `VMUL` | Vector add/sub/mul |
| `VMADD` `VMSUB` | Vector multiply-accumulate |
| `VADDA` `VSUBA` `VMULA` | Vector accumulate |
| `VABS` | Vector absolute value |
| `VMAX` `VMIN` | Vector max/min |
| `VDIV` `VSQRT` `VRSQRT` | Scalar division/sqrt |
| `VFTOI0` `VFTOI4` | Float to integer conversion |
| `VITOF0` `VITOF4` | Integer to float conversion |
| `VMOVE` `VMR32` | Vector register move |
| `VOPMULA` `VOPMSUB` | Outer product multiply |
| `VCLIPW` | Clip test |
| `VNOP` | No operation |
| `VLQ` `VSQ` | Quadword load/store (128-bit) |
| `VCALLMS` `VCALLMSR` | Call VU0 microsubroutine |
| `QMFC2` `QMTC2` | Quadword moves between EE and VU0 |
| `CFC2` `CTC2` | COP2 control register moves |
| `VRNEXT` `VRGET` `VRINIT` `VRXOR` | VU0 LFSR random number generator |
| VADDBc, VSUBBc, VMULBc, ... | Per-component broadcast variants (x/y/z/w) for VADD/VSUB/VMUL/VMADD/VMSUB/VADDA/VSUBA/VMULA |

### MMI — Multimedia Instructions (PS2 EE Extension)

| Category | Instructions |
|----------|-------------|
| Multiply-Accumulate | `MADD` `MADDU` `MADD1` `MADDU1` `MULT1` `MULTU1` `DIV1` `DIVU1` |
| HI1/LO1 Pipeline | `MFHI1` `MTHI1` `MFLO1` `MTLO1` |
| Halfword/Word Operations | `PMFHI` `PMTHI` `PMFLO` `PMTLO` `PMFHL` `PMTHL` |
| SIMD Integer Arithmetic | `PADDW` `PADDH` `PADDB` `PSUBW` `PSUBH` `PSUBB` |
| SIMD Unsigned Saturate | `PADDUW` `PADDUH` `PADDUB` `PSUBUW` `PSUBUH` `PSUBUB` |
| SIMD Signed Saturate | `PADDSW` `PADDSH` `PADDSB` `PSUBSW` `PSUBSH` `PSUBSB` |
| SIMD Compare | `PCEQW` `PCEQH` `PCEQB` `PCGTW` `PCGTH` `PCGTB` |
| SIMD Min/Max | `PMAXW` `PMINW` `PMAXH` `PMINH` |
| SIMD Shift | `PSLLW` `PSRLW` `PSRAW` `PSLLH` `PSRLH` `PSRAH` `PSLLVW` `PSRLVW` `PSRAVW` |
| SIMD Multiply | `PMULTW` `PMULTUW` `PMULTH` `PMADDW` `PMADDUW` `PMADDH` `PMSUBW` `PMSUBH` |
| SIMD Divide | `PDIVW` `PDIVUW` `PDIVBW` |
| Bitwise | `PAND` `POR` `PNOR` `PXOR` |
| Pack/Extend | `PEXTLW` `PEXTUW` `PEXTLH` `PEXTUH` `PEXTLB` `PEXTUB` |
| Pack | `PPACW` `PPACH` `PPACB` `PPAC5` `PEXT5` |
| Shuffle/Rearrange | `PEXCH` `PEXCW` `PEXEH` `PEXEW` `PCPYH` `PCPYLD` `PCPYUD` `PREVH` `PROT3W` `PINTEH` `PINTH` |
| Absolute Value | `PABSW` `PABSH` |
| Halfword Multiply | `PHMADH` `PHMSBH` |
| Signed-to-Byte | `PADSBH` |
| Misc | `PLZCW` `QFSRV` |

---

## PS2 EE vs Standard MIPS — Key Differences

| Aspect | Standard MIPS64 | PS2 EE |
|--------|-----------------|--------|
| GPR width | 64 bits | 128 bits (lower 64 used for int) |
| `MULT`/`DIV` HI/LO | 64-bit raw | **32-bit result sign-extended to 64 bits** |
| FPU compare functs | C.LT=0x3C, C.LE=0x3E | C.LT=0x34, C.LE=0x36 |
| FPU denormals | IEEE 754 (supported) | **Flushed to zero** |
| FPU register encoding | fd=bits\[10:6\], fs=bits\[15:11\] | Same — **args.Rs is format (0x10), not a register** |
| `ERET` | Clears EXL | **Checks ERL first, uses ErrorEPC (reg 30) if set** |
| Shift 64-bit RegsHi | N/A | **Sign-extended from result bit 63** |
| VU0 | Not present | COP2 macro-mode + micro-mode |
| MMI instructions | Not present | Full SIMD multimedia extension |

---

## Breakpoint System

The toolkit has a built-in breakpoint mechanism that lets you pause EE execution at any instruction address, inspect CPU state, and resume — all without modifying the program under test.

### How It Works

`DualCpuScheduler` maintains a `HashSet<uint> Breakpoints`.  After every EE `Step()`, the scheduler checks whether the new EE PC is in that set.  If it is, `Paused` is set to `true` and `LastBreakpointHit` records the address — execution stops until you send a `RESUME` command.

### Setting Breakpoints in the VS Code Disassembler

1. Open a **Live Memory** disassembly panel while the emulator is running.
2. Navigate to the instruction you want to break at (arrow keys / search).
3. Press **F9** to toggle a breakpoint on the highlighted row.  
   - A highlighted row indicates an active breakpoint.
   - Press F9 again on the same row to clear it.
4. When the EE PC reaches that address the emulator pauses automatically.  The panel scrolls to the hit address and shows a **PAUSED** indicator.
5. Press **F5** (or use the *Resume* button) to continue execution.

The panel polls the emulator status every 800 ms, so you will see the pause reflected within about one second.

### Socket Commands

All breakpoint operations are also available over the [TCP socket protocol](SOCKET_COMMUNICATION.md).  Use these commands from any client (e.g. a custom debug script):

| Command | Description |
|---------|-------------|
| `SETBP` | Add a breakpoint at a given EE virtual address |
| `CLEARBP` | Remove the breakpoint at a given address |
| `CLEARBP_ALL` | Remove all breakpoints |
| `PAUSE` | Manually pause execution (without a breakpoint) |
| `RESUME` | Resume execution and clear `LastBreakpointHit` |
| `GETPC` | Query current EE PC, paused state, and last breakpoint hit address |

See [SOCKET_COMMUNICATION.md](SOCKET_COMMUNICATION.md) for full request/response schemas.

### `BREAK` Instruction

The MIPS `break` instruction (opcode `0x0D`) triggers a `Breakpoint` exception via `CpuExceptionSignal`.  This is the software-only path (EE handles it as a CPU exception); it is separate from the address-based hardware breakpoints described above.

---

## Implementation Notes

### Verified Against PCSX2
All instruction semantics have been cross-referenced with [PCSX2](https://pcsx2.net/) source code, particularly:
- `pcsx2/R5900OpcodeImpl.cpp` — EE core instruction execution
- `pcsx2/R5900OpcodeTables.cpp` — EE opcode tables and encoding
- `pcsx2/VUops.cpp` — VU0/VU1 vector operations
- `pcsx2/FPU.cpp` — COP1 floating-point unit

### Test Coverage
- **960 unit tests** covering core, FPU, COP0, COP2/VU0, MMI, EE hardware, IOP, CDVD, SPU2, SIO2, GS/GIF/VIF, VU1, BiosLoader, GameLoader, TLB, IOP module loader, memory card, USB, i.LINK, DEV9, performance counters, and FCSR flags
- Tests verify both disassembly output and execution correctness
- Special cases: long.MinValue in DMULT, negative DIV sign-extension, ERET ERL handling

---

## Documentation

> All documents are cross-linked below by category.  
> Click any entry to jump directly to the relevant file.

### Project Overview & Guides

| Document | Description |
|----------|-------------|
| [README.md](README.md) | This file — project overview, full instruction set reference, and architecture summary |
| [QUICKSTART.md](QUICKSTART.md) | Step-by-step guide: build, start server, assemble and disassemble code in VS Code |
| [ROADMAP.md](ROADMAP.md) | Phase-by-phase development history and status tracking |

### Technical Integration

| Document | Description |
|----------|-------------|
| [ASSEMBLER_DISASSEMBLER_INTEGRATION.md](ASSEMBLER_DISASSEMBLER_INTEGRATION.md) | JSON protocol, supported commands (`ASSEMBLE`/`DISASSEMBLE`/`PING`), and C# ↔ TypeScript API reference |
| [SOCKET_COMMUNICATION.md](SOCKET_COMMUNICATION.md) | TCP socket architecture, message framing, command/response schemas, and troubleshooting |

### Architecture Deep-Dives

These documents live in [`CodeDesigner.Ps2.Common/Documentation/Architecture/`](CodeDesigner.Ps2.Common/Documentation/Architecture/).

| Document | Description |
|----------|-------------|
| [EmotionEngine.md](CodeDesigner.Ps2.Common/Documentation/Architecture/EmotionEngine.md) | PS2 Emotion Engine CPU overview: registers, memory system, DMA, EE-specific instruction set extensions |
| [COP0.md](CodeDesigner.Ps2.Common/Documentation/Architecture/COP0.md) | System Control Coprocessor: full register table (including PCCR at reg 25 sel, ErrorEPC at reg 30), exception flow, TLB, interrupts |
| [COP1.md](CodeDesigner.Ps2.Common/Documentation/Architecture/COP1.md) | Floating-Point Unit: PS2 EE single-precision-only FPU, denormal flush, FCSR sticky flags, compare encoding differences vs standard MIPS |
| [VU0.md](CodeDesigner.Ps2.Common/Documentation/Architecture/VU0.md) | Vector Unit 0: macro-mode (COP2) and micro-mode instruction sets, broadcast variants, LFSR RNG, VCALLMS/VCALLMSR |
| [VU1.md](CodeDesigner.Ps2.Common/Documentation/Architecture/VU1.md) | Vector Unit 1: full micro-mode instruction reference — upper/lower word ops, EFU (P register), XGKICK → GIF PATH1 |

### Component README Files

| Document | Description |
|----------|-------------|
| [CodeDesigner.Ps2.Common/README.md](CodeDesigner.Ps2.Common/README.md) | Core library overview: EE CPU architecture, vector units, FPU, memory system |
| [CodeDesigner.Ps2/README.md](CodeDesigner.Ps2/README.md) | Console application and socket server overview |
| [CodeDesigner.Ps2.McpServer/README.md](CodeDesigner.Ps2.McpServer/README.md) | MCP stdio bridge: GitHub Copilot integration, available tools, registration |
| [CodeDesigner.Ps2.CdsLanguage/README.md](CodeDesigner.Ps2.CdsLanguage/README.md) | CDS compiler: language syntax, compilation pipeline, cheat code output |
| [CodeDesigner.Ps2.CheatEngine/README.md](CodeDesigner.Ps2.CheatEngine/README.md) | Cheat engine: line types, address encoding, timer/condition cheats |
| [CodeDesigner.Ps2.CheatPorter/README.md](CodeDesigner.Ps2.CheatPorter/README.md) | Cheat porter: multi-strategy pattern matching to port codes between game versions |
| [CodeDesigner.Ps2.CheatConverter/README.md](CodeDesigner.Ps2.CheatConverter/README.md) | Cheat converter: format detection and conversion (AR2, CB, GS, raw) |
| [CodeDesigner.Ps2.MemoryCardManager/README.md](CodeDesigner.Ps2.MemoryCardManager/README.md) | Memory card manager: PS2 8 MB memory card emulation and file access |
| [CodeDesigner.Ps2.Recompiler/README.md](CodeDesigner.Ps2.Recompiler/README.md) | Static recompiler: translate PS2 ELF to native C++ source |
| [CodeDesigner.Ps2.TestProject/README.md](CodeDesigner.Ps2.TestProject/README.md) | Test project: running the 1097-test suite |
| [vscode/codedesigner/README.md](vscode/codedesigner/README.md) | VS Code extension user guide: setup, commands, supported instructions |
| [vscode/codedesigner/CHANGELOG.md](vscode/codedesigner/CHANGELOG.md) | VS Code extension release history |
| [vscode/codedesigner/ARCHITECTURE.md](vscode/codedesigner/ARCHITECTURE.md) | Extension architecture: webpack, Angular, provider pattern, communication |
| [vscode/codedesigner/vsc-extension-quickstart.md](vscode/codedesigner/vsc-extension-quickstart.md) | Extension developer quick-start: project structure, build, debug, and extend |

---

## References

- Sony PlayStation 2 EE Core User's Manual
- MIPS64 Architecture Specification (MIPS Technologies)
- [PCSX2 Emulator Source Code](https://github.com/PCSX2/pcsx2)
- PS2 Linux Developer Tools Documentation
- PS2 Reverse Engineering Community Resources

---

## Disclaimer

This project is for **educational and research purposes** only.  
PlayStation, Emotion Engine, and related trademarks are the property of **Sony Interactive Entertainment**.  
This project is not affiliated with or endorsed by Sony Interactive Entertainment.
