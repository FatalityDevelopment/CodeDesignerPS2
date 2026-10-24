[← Back to Documentation Index](README.md)

# Disassembler — User Guide

The disassembler (the **memory editor**) is a live, scrollable view of PS2 memory while a game runs: disassembly, a hex view, labels, comment hints, breakpoints, watches and registers in one panel. This guide covers opening it, reading it, and debugging with it.

---

## Opening a panel

1. Connect to an emulator from the sidebar's **Emulator** tab (PCSX2 or the built-in emulator).
2. Open a memory region from **Memory Region** in the same tab. Each region opens its own panel:

   | Region | | Region | |
   |---|---|---|---|
   | EE RAM | main RAM (32 MB) | VU0 micro / VU0 data | VU0 memory |
   | IOP RAM | IOP RAM (2 MB) | VU1 micro / VU1 data | VU1 memory |
   | BIOS ROM | 4 MB boot ROM | IOP HW regs | IOP registers |
   | Scratchpad | 16 KB EE scratchpad | GS priv regs | GS privileged registers |
   | EE HW regs | EE hardware registers | ROM1 (DVD) / ROM2 (CN ext) | DVD and extended ROMs |

3. When the emulator pauses (breakpoint or step), the panel that holds the PC opens and scrolls to it.

**Go to address** (`Ctrl+Alt+A`) accepts any address. If its region has no panel yet (a BIOS address, say), that panel opens and lands on the address once it has loaded.

You can also open `.elf`, `.bin` and `.raw` files from the Explorer for a static view.

---

## Reading a row

```
00100008  27BDFFD0  addiu   sp, sp, -$0030          ; FNC_00100008 stack frame
```

| Column | Contents |
|---|---|
| Address | Virtual address. A `◂N` badge counts incoming references (`◂3+2` = 3 code refs + 2 data pointers). |
| Word | The raw 32-bit word. |
| Instruction | The decoded EE instruction, or a data directive (`.word`, `.float`, …). |
| Label | Your label, a cheat annotation, or an `FNC_` name for a detected function. |
| Comment | Your comment first, then generated hints. |

The decoder covers the full R5900 set: core, MMI, COP0, COP1, VU0 macro mode and VU1. Signed immediates print as `-$0030`, and pseudo-ops (`move`, `li`, `b`, `beqz`, `not`, …) are shown where they apply. Every printed form assembles back to the same word. Opcodes the EE doesn't have show as `.word`.

### Functions and data

- **Functions** are found from call targets and stack-frame prologues. Each gets a divider row and an `FNC_` name when you haven't named it.
- **Tables** are recognised and shown as data: switch jump tables (`case N -> target`), pointer tables, and float tables loaded with `lwc1`.
- Your own data formats always win. Put the cursor on a row and press `B` `H` `W` `F` `D` `Q` (byte, half, word, float, dword, qword), or `A` to go back to automatic. The right-click menu has the same options plus `.double`.

### Comment hints

Hints show what an instruction does with known values: the address a `lui`+`addiu` pair builds, the value a load reads, branch targets, `syscall` names, `jal` arguments (including ones set in the delay slot) and pointer chains through loaded pointers. Register state is simulated from the start of the function, so values set long before the visible page still resolve. When paused, the live register values apply from the PC row on.

The **Hints** dropdown in the toolbar picks **Full**, **Addresses** (calls, branches, loads/stores, built addresses and syscalls only) or **Off**. The setting is stored by the bridge and shared by every panel.

---

## Toolbar and keys

| Action | Key |
|---|---|
| Go to address | `Ctrl+Alt+A` |
| Navigate back | `Alt+Left` |
| Search | `Ctrl+F`, then `F3` / `Shift+F3` for next / previous |
| Toggle breakpoint on the cursor row | `F9` |
| Cross-references for the cursor row | `X` |
| Set PC to an address (live) | `Ctrl+Alt+P` |
| Registers panel | `Ctrl+Alt+R` |
| Bottom panel (breakpoints, watches, threads, modules, call stack) | `Ctrl+Alt+T` |
| Data format | `B` `H` `W` `F` `D` `Q`, `A` = auto |

Pause, resume, **Step**, **Step Over** and **Step Into** are in the sidebar's status bar. Step Over and Step Into need PCSX2.

---

## Editing a row

Double-click a row to open the **Address Editor**:

- **Edit** — change the instruction (it is assembled for you), the raw bytes, the label or the comment, then **Save**. Patches are written to the panel's own region.
- **History** — the value at this address in each saved snapshot.
- **References** — every caller, branch, load, store and pointer that targets the address.

---

## Cross-references

The bridge builds a cross-reference index for each region once, so the `◂N` badges and the References list are instant. Click a badge or press `X` to open the list. After the game loads new code (an overlay or a new level), right-click → **Rebuild Cross-reference Index**.

The right-click menu also has **Go to Target Address**, **Find References** and **Find Code References**.

---

## Search

`Ctrl+F` opens the search bar with five modes:

| Mode | Finds |
|---|---|
| `Aa` Text | Text in addresses, words, labels, instructions and comments (case-insensitive) |
| `0x` Bytes | A byte pattern, e.g. `0x27 BD FF D0` |
| `"ab"` String | An ASCII/UTF-8 string in memory (case-sensitive) |
| `.*` Regex | A regular expression over the same fields as Text |
| `ref` References | Instructions that reference an address |

Matches are highlighted; `F3` and `Shift+F3` move between them.

---

## Breakpoints and watches

- `F9` or right-click → **Set Breakpoint** toggles an execution breakpoint. When it hits, the emulator pauses and the panel scrolls to the row.
- Right-click → **Add Watch** adds a memory watchpoint (PCSX2).
- The bottom panel (`Ctrl+Alt+T`) lists breakpoints, watches, threads, modules and the call stack, with **Clear All**.

---

## Registers

`Ctrl+Alt+R` opens the registers panel:

| Tab | Contents |
|---|---|
| GPR | 32 × 128-bit general-purpose registers, HI/LO |
| CP0 | COP0 system registers (Status, Cause, EPC, Count, …) |
| FPR | COP1 float registers and the accumulator |
| FCR | FPU control registers |
| VU0f / VU0i | VU0 vector float and integer registers |
| GS | GS privileged registers |
| Inspect | The stack at `$sp` and active watchpoints |

Double-click a value to edit it while paused.

---

## Symbols

The symbols panel lists **Labels**, **Strings**, **Functions**, **Pointers**, **Floats** and **Integers** found in memory. Click an entry to jump to it; labels can be added and deleted inline. For label sets, pointer structs and imports, use the [Label Manager](labels.md).

---

## Tips

- Labels, comments and data formats are saved in the CodeDesigner database (SQL Server LocalDB) per game, and show up in every panel.
- Enabled cheats from the Cheats tab are annotated in the label column, and the first joker address is labelled `joker`.
- A **Snapshot** (sidebar → Actions) saves memory so you can compare or restore it later in the Snapshot Manager or [Memory Diff](vscode-extension.md#memory-diff).
