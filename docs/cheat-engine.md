[← Back to Documentation Index](README.md)

# Cheat Engine — User Guide

CodeDesigner applies cheat codes to a running game in two ways: a **one-shot** write of the codes right now, or the **frame-loop engine**, a small MIPS routine injected into the game that re-applies cheats every frame.

---

## The Cheats tab

### Archive cheats

When you're connected to PCSX2, the tab loads the known cheats for the running game: from the database first, then from the CheatCodeArchive YAML files. Toggle the ones you want; enabling a cheat writes its codes straight away, and **Apply All Enabled** writes every enabled cheat again.

Enabled cheats are also annotated in the disassembler's label column, and the first joker address (`D0` line) gets a `joker` label.

### Your own codes

Paste GameShark / CodeBreaker raw codes into the text box and click **Update** to write them. The CDS tab's **Add Cheat** fills this box from a compiled script.

### Cheat Tools

- **Converter** — detect and convert cheat formats (encrypted ↔ raw). See [Cheat Converter](cheat-converter.md).
- **Scanner** — the memory scanner, for finding values to make cheats from.
- **Porter** — move codes to another game version. See [Cheat Porter](cheat-porter.md).

To edit full cheat lists (with sections and syntax highlighting), open the **Cheats editor** from *Emulator → Tools → Cheats*. To build a button activator for a cheat, use the [Controller](vscode-extension.md#controller) page.

---

## Code format

A cheat is a name line followed by code lines:

```
#Infinite Health
2034A1B0 000003E7

#Max Ammo
1034A1C4 00000063
```

One-shot apply understands these types (first hex digit of the address word):

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

Conditions are checked once, at the moment you apply. For a condition that should be checked every frame (a joker), use the frame-loop engine.

---

## One-shot vs. frame-loop

**One-shot** (toggle, Apply All Enabled, Update) writes the values once. That's enough for unlocks and flags the game doesn't rewrite.

**Frame-loop.** For values the game keeps resetting (health, ammo) or codes gated by a button combo, the engine injects a routine into unused EE RAM (default `0x000C0000`) and hooks it into a function the game calls every frame. The routine then re-applies the cheat table each frame.

The engine is driven through the Bridge and MCP, not a sidebar button:

| Bridge command | MCP tool | Does |
|---|---|---|
| `INJECTCHEATENGINE` | `inject_cheat_engine` | Assemble the routine, write the table and install the hook. Needs `HookAddress` (a per-frame call site); `EngineBase` is optional. |
| `UPDATECHEATENGINE` | `update_cheat_engine` | Replace the cheat table without re-hooking. |
| `REMOVECHEATENGINE` | `remove_cheat_engine` | Restore the hooked instruction and clear the routine. |

Each takes raw codes or CDS source. In CDS, persistent cheats are written with `freeze`, `cond` and `timer` blocks (values are hex):

```cds
freeze $2034A1B0 = 03E7          // keep health at 999

cond mem[$2034A100] == 1         // only while this word is 1
  freeze $2034A1C4 = 63
endcond

timer 60                          // once every 60 frames
  freeze $2034A1C8 = 64
endtimer
```

See [CDS Language → Cheat-engine blocks](cds-language.md#cheat-engine-blocks).

---

## Tips

- Snapshot before experimenting with codes.
- The engine's base address must be unused by the game. If the game crashes after injecting, pass a different `EngineBase`.
- Pick a hook in code that runs once per frame (a main-loop call). The disassembler's cross-references (`X`) help find one.
- **Remove** restores the hook, so the game keeps running without a restart. Values already written stay until the game overwrites them.
