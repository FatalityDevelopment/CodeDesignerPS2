# Disassembler — User Guide

CodeDesigner's disassembler gives you a live, scrollable view of PS2 Emotion Engine memory while a game is running.

---

## Opening the disassembler

1. Connect to an emulator (built-in or PCSX2) from the **Emulator Settings** tab.
2. Pause execution — the live disassembler panels open automatically.
3. Alternatively open any `.elf`, `.bin`, or `.raw` file for a static disassembly view.

---

## Navigating

| Action | How |
|---|---|
| Scroll | Mouse wheel or keyboard arrow keys |
| Go to address | `Ctrl+G` |
| Navigate back | `Alt+Left` |
| Jump to PC | Click the `▷` marker row |

---

## The disassembly view

Each row shows the virtual address, raw hex word, mnemonic, operands, and an auto-generated comment. The current PC row is highlighted with `▷`. Breakpoint rows show a red dot.

---

## Search (`Ctrl+F`)

Three modes:

| Mode | Input |
|---|---|
| Text | ASCII string |
| Byte pattern | `0x27 BD FF D0` |
| ASCII string | Null-terminated |

---

## Breakpoints

Press **F9** on any row to toggle a breakpoint. Execution pauses automatically when hit and the disassembler scrolls to the row.

---

## Execution controls

| Control | Key | Description |
|---|---|---|
| Pause / Resume | `F5` | Toggle running / paused |
| Single Step | Step button | One EE instruction |
| Step Into | Step Into | Enter called function |
| Step Over | Step Over | Skip over function call |

---

## Register panel (`Ctrl+Alt+R`)

| Tab | Contents |
|---|---|
| **GPR** | 32 × 128-bit general-purpose registers |
| **CP0** | COP0 system control registers |
| **FPR** | 32 COP1 float registers + FACC |
| **VU0f** | 32 × (x/y/z/w) VU0 vector floats |
| **VU0i** | 16 VU0 integer registers |
| **GS** | GS privileged register values |
| **Stack** | 32-word stack frame at `$sp` |

---

## Symbols panel (`Ctrl+Alt+S`)

| Tab | Description |
|---|---|
| **Labels** | User-defined and imported symbols |
| **Strings** | Null-terminated ASCII strings |
| **Functions** | Detected function entry points |
| **Pointers** | 32-bit pointer values |
| **Floats** | IEEE-754 float values |
| **Integers** | 32-bit integer values |

Click any entry to jump there. Labels can be added, edited, and deleted inline.

---

## Snapshots

Click **Snapshot** in the Actions section to save the complete CPU and memory state. Click any saved entry to restore it.

---

## Hex view

A resizable hex dump panel sits beside the disassembly grid showing the raw bytes at the same addresses. Drag the divider to resize.
