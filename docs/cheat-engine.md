[← Back to Documentation Index](README.md)

# Cheat Engine — User Guide

CodeDesigner applies cheat codes to a running game in two ways: a **one-shot** write of the codes right now, or the **cheat engine**, a small MIPS routine injected into the game that re-applies cheats every frame.

---

## The Cheats tab

The sidebar's Cheats tab holds the cheat tools, the archive cheats and a cheat source box. (It replaces the old separate Cheats and CDS tabs.)

### Cheat Tools

- **Cheat List** — the full cheat manager page (below).
- **Archive** — the cheat archive editor: the game's cheat lists, edited as text with sections and syntax highlighting. It opens on the game being played.
- **Scanner** — the Cheat Scanner, for finding values to make cheats from.
- **Converter** — detect and convert cheat formats (encrypted ↔ raw). See [Cheat Converter](cheat-converter.md).
- **Porter** — move codes to another game version. See [Cheat Porter](cheat-porter.md).

To build a button activator for a cheat, use the [Controller](vscode-extension.md#controller) page.

### Archive cheats

When you're connected to PCSX2, the tab loads the running game's cheats from the cheat archive (the lists the cheat archive editor edits). **List** picks which of the game's cheat lists to use — your own (e.g. `r0001`, `r0004`) or ones imported from the repository; the choice is remembered per game and shared with the Cheat List page. Toggle the cheats you want; enabling a cheat writes its codes straight away, and **Apply All Enabled** writes every enabled cheat again.

If the game has no lists yet, **Import from repository** imports the game's text files from the cheat repository (CheatCodeArchive `Database/*.CMP.txt`) into cheat lists — one per database folder (GSHI, CodeBreaker, CMP, Xploder (EU)). Imported lists are attached to the game that already has a disc with the serial, so they sit next to your own lists in the cheat archive editor. The import uses the `CheatCodeArchive` checkout in your workspace, or otherwise a clone of the repository that the extension keeps up to date.

Enabled cheats are also annotated in the disassembler's label column, and the first joker address (`D0` line) gets a `joker` label.

### Cheat source

The text box takes raw codes **and** Code Designer Source in the same text — see [Code format](#code-format). `#Name` lines separate cheats.

- **Compile** (Ctrl+Enter) shows the raw codes the source compiles to, or the errors.
- **Use .cds file** compiles the active `.cds` editor instead. Its output has **Add Patch** (write the codes and install the file's breakpoint / watchpoint sidecars) and **To source**.
- **Add to list** copies the cheats into the game's Cheat List.
- **Send** — *Once* writes the codes a single time; *Engine* sends them to the cheat engine.
- **Save .bin** — *Raw* or *Engine* binary, see [Saving as a binary](#saving-as-a-binary).

The engine needs a hook address. If the game has none saved yet, Send asks for one and remembers it.

---

## The Cheat List page

A cheat-device style view of one of the game's cheat lists from the **cheat archive** — the lists the cheat archive editor edits (e.g. `r0001`, `r0004`), plus lists imported from the cheat repository. It works without PCSX2: the game's serial is read from the selected disc.

- **Cheat list** (left, top) — the dropdown picks the list; your own lists come first, then *Imported from the repository*. **+** creates a list for the game; **↓** imports the game's cheats from the cheat repository text files into cheat lists. The choice is shared with the sidebar's Cheats tab.
- **Cheats** (left) — tick the cheats to use (ticks are remembered per list). The dot shows the last compile: green compiled, red errors.
- **Editor** (right) — name, section, source (raw codes and CDS mixed), description, and the compiled codes or errors for the selected cheat.
- **New cheat / Paste / delete** change the list in the page; **Save to list** writes it back to the cheat archive (**Discard** reverts). A cheat written in CDS saves its compiled codes to the list — the list only holds raw codes — and the page keeps the CDS source so you can keep editing it.
- **Compile**, **Send** (*Once* or *Cheat engine*), **Save .bin** (*Raw code pairs* or *Cheat engine image*) work on every ticked cheat.
- **Engine bar** (bottom) — whether the engine is hooked, where, and how many rows it runs; the **Hook** and **Base** addresses; **Unhook**.

The header buttons open the Scanner, Converter, Porter and the cheat archive editor.

---

## Code format

A cheat is a name line followed by code lines:

```
#Infinite Health
2034A1B0 000003E7

#Max Ammo
1034A1C4 00000063
```

Raw code pairs are also valid CDS, so they can be mixed with assembly and the cheat-engine blocks. Everything compiles to raw codes, in source order:

```cds
#Moon jump
20347A50 3F800000                 // a raw code
freeze half $00347A52 = 03E7      // → 10347A52 000003E7
address $00200000                 // assembled words → 2xxxxxxx writes
  addiu v0, zero, 1
```

`cond mem[$A] == V` compiles to two chained 16-bit multi-line conditionals (`E0nnVVVV 0AAAAAAA`, low half then high half); `timer N` compiles to `Bnnnnnnn IIIIIIII` (run the next n lines every I frames — a CodeDesigner engine code).

**One-shot apply** understands:

| Type | Operation |
|---|---|
| `0` | 8-bit write |
| `1` | 16-bit write |
| `2` | 32-bit write |
| `5` | Copy bytes |
| `7` | Pointer write |
| `B` | Timer |
| `C` | 32-bit condition (gates the rest of the cheat) |
| `D` | 16-bit condition (gates the rest of the cheat); used for jokers |

Conditions are checked once, at the moment you apply. For a condition that should be checked every frame (a joker), use the cheat engine.

**The cheat engine** understands:

| Type | Operation |
|---|---|
| `0` `1` `2` | 8/16/32-bit write |
| `3` | 8/16/32-bit increment / decrement (`30S0VVVV 0AAAAAAA`, 32-bit forms take a value line) |
| `4` | 32-bit serial write (`4AAAAAAA NNNNSSSS` + `VVVVVVVV IIIIIIII`) |
| `B` | Timer: the next n lines every I frames |
| `C` | 32-bit equal; when false the rest of the cheat is skipped |
| `D` | 16-bit compare (`00T0VVVV`, T = 0 `==`, 1 `!=`, 2 `<`, 3 `>`) guarding the next line |
| `E` | 16-bit (`E0NNVVVV`) or 8-bit (`E1NN00VV`) compare guarding the next NN lines |
| `9` `F` | Hook / master codes — ignored, the engine has its own hook |

Other types (copy, pointer, bitwise compares) are rejected with the line number, so nothing is half-applied.

---

## One-shot vs. cheat engine

**One-shot** (toggle, Apply All Enabled, Send → Once) writes the values once. That's enough for unlocks and flags the game doesn't rewrite.

**Cheat engine.** For values the game keeps resetting (health, ammo) or codes gated by a button combo, the engine injects a routine into unused EE RAM (default `0x000C0000`) and hooks it into code the game runs every frame. The routine then re-applies its table each time the hook runs.

- **The hook** is the address of an instruction pair that runs every frame. It replaces them with `j engine; nop`; the engine replays the two instructions and jumps back. Plain instructions are fine, and so is a function's `jr ra` + delay slot (a good choice). Branches and linking jumps can't be moved and are rejected.
- **Registers** the engine uses are saved in full (128-bit) along with HI/LO, so the game's state is untouched.
- **Layout**: code at the base, data at `+0x400` (hooked instructions, return address, frame counter), table at `+0x500` (16 bytes per row, up to 512 rows; about 8.5 KB in total).
- **Sending again** to the same hook updates the table in place; a different hook or base re-injects.

| Bridge command | MCP tool | Does |
|---|---|---|
| `INJECTCHEATENGINE` | `inject_cheat_engine` | Build the engine, write the table and install the hook. Needs `HookAddress`; `EngineBase` is optional. A previous injection is removed first. |
| `UPDATECHEATENGINE` | `update_cheat_engine` | Replace the table without re-hooking. |
| `REMOVECHEATENGINE` | `remove_cheat_engine` | Restore the hooked instructions. |
| `CHEATENGINESTATUS` | — | Whether the engine is hooked, where, and its row count. |
| `BUILDCHEATENGINE` | — | Build the engine image without writing memory (for Save .bin). |

Each takes raw codes (`Data`) or CDS source (`CdsSource`).

---

## Saving as a binary

- **Raw code pairs** — every compiled code as two little-endian 32-bit words (`20123456 000003E8` → `56 34 12 20 E8 03 00 00`).
- **Cheat engine image** — the engine's code, data and table, to load at the engine base. With a hook address and a live connection the hooked instructions are read and embedded, and the save message gives the word to write at the hook (`j engine`, then `00000000`). Without them the image's replay slots are nops and must be patched before use.

---

## Tips

- Snapshot before experimenting with codes.
- The engine's base address must be unused by the game. If the game crashes after injecting, use a different **Base**.
- Pick a hook in code that runs once per frame (a main-loop call or a per-frame function's `jr ra`). The disassembler's cross-references (`X`) help find one.
- **Unhook** restores the hooked instructions, so the game keeps running without a restart. Values already written stay until the game overwrites them.
