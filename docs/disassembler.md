# Disassembler — User Guide

CodeDesigner's disassembler gives you a live, scrollable view of PS2 Emotion Engine memory while a game is running. This guide covers everything from opening your first view to using breakpoints and the hex panel.

---

## Opening the disassembler

1. Connect to an emulator (built-in or PCSX2) from the **Emulator Settings** tab.
2. Pause execution — the live disassembler panels open automatically.
3. Alternatively use **File → Open** on any `.elf`, `.bin`, or `.raw` file to open a static disassembly view.

---

## Navigating

| Action | How |
|---|---|
| Scroll | Mouse wheel or keyboard arrow keys |
| Go to address | `Ctrl+G` — enter any virtual address in hex |
| Navigate back | `Alt+Left` — return to the previous address |
| Jump to PC | Click the `▷` marker row (always visible while paused) |
| Click a symbol | Click any label or function name in the disassembly to follow it |

---

## The disassembly view

Each row shows:

```
0x00100000  27BDFFD0  addiu  $sp, $sp, -0x30       # stack frame
```

| Column | Description |
|---|---|
| Address | Virtual memory address (hex) |
| Hex word | Raw 32-bit instruction word |
| Mnemonic | EE MIPS instruction name |
| Operands | Decoded register and immediate fields |
| Comment | Auto-generated annotation or user label |

The current PC row is highlighted with a `▷` arrow. Breakpoint rows show a red dot.

---

## Hex view

A resizable hex dump panel sits beside the disassembly grid. It shows the raw bytes at the same address, aligned to the disassembly rows. Drag the divider to resize.

---

## Search (`Ctrl+F`)

The search panel supports three modes:

| Mode | Input format | Example |
|---|---|---|
| **Text** | ASCII string | `SLUS_123.45` |
| **Byte pattern** | Space-separated hex bytes | `0x27 BD FF D0` |
| **ASCII string** | Null-terminated search | `GameTitle` |

Results are listed below the search bar. Click a result to navigate there.

---

## Breakpoints

Press `F9` on any instruction row to toggle a breakpoint. When the emulator hits the address:

- Execution pauses automatically.
- The disassembler scrolls to the hit row.
- A **PAUSED** indicator appears in the status bar.

To clear all breakpoints, open the Debug Panel (`Ctrl+Alt+R`) and use the Breakpoints tab.

---

## Execution controls

| Control | Key / Button | Description |
|---|---|---|
| Pause / Resume | `F5` | Toggle between running and paused |
| Single Step | Step button | Execute exactly one EE instruction |
| Step Into | Step Into button | Step into the called function |
| Step Over | Step Over button | Step over the called function |
| Set PC | Right-click → Set PC | Move the program counter without executing |

---

## Register inspection (Debug Panel)

Open with `Ctrl+Alt+R`. Tabs:

| Tab | Contents |
|---|---|
| **GPR** | 32 × 128-bit general-purpose registers (`$zero`–`$ra`) |
| **CP0** | COP0 system control registers (Status, Cause, EPC, Count, …) |
| **FPR** | 32 COP1 floating-point registers + accumulator (FACC) |
| **FCR** | FPU control registers FCR0 and FCR31 |
| **VU0f** | 32 × (x/y/z/w) VU0 vector float registers |
| **VU0i** | 16 VU0 integer registers (VI0–VI15) |
| **GS** | All GS privileged register values |
| **Stack** | 32-word stack frame dump at current `$sp` |

---

## Symbols Panel (`Ctrl+Alt+S`)

| Tab | Description |
|---|---|
| **Labels** | User-defined and auto-imported symbol labels |
| **Strings** | Null-terminated ASCII strings found in memory |
| **Functions** | Detected MIPS function entry points |
| **Pointers** | 32-bit code-referenced pointer values |
| **Floats** | IEEE-754 single-precision float values |
| **Integers** | 32-bit integer values |

Click any entry to jump to that address. Labels can be added, edited, and deleted inline.

---

## Inline register analysis

When you single-step an instruction, the disassembler shows the live register values read and written by that instruction directly on the row — no need to switch to the register panel.

---

## Snapshots

A snapshot saves the complete CPU and memory state so you can return to an exact game moment later. Use the **Snapshot** button in the Actions section of Emulator Settings, then click any saved snapshot entry to restore it.

---

## Tips

- **PCSX2 mode:** Connect to PCSX2 instead of the built-in emulator to debug commercial games. All the same controls apply.
- **Labels persist:** Labels you add in one session are saved to the SQLite database and reloaded next time you open the same game.
- **Multi-region view:** You can open multiple disassembler panels at once — one per memory region — from the memory view menu.
