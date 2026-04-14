# CodeDesigner.Ps2.CheatEngine — cheat formats, persistent engine, and CDS workflow

## Overview

`CodeDesigner.Ps2.CheatEngine` contains two related cheat workflows:

1. **`CheatEngine`** for applying parsed cheat codes to memory immediately
2. **`CdsCheatEngine`** for injecting a small MIPS routine into EE RAM so cheats are re-applied every frame

That second workflow is the major recent improvement: instead of only feeding raw GameShark-style lines, you can now describe cheat behavior in CDS using:

- `freeze`
- `cond / endcond`
- `timer / endtimer`

and inject/update/remove the persistent engine through C#, bridge commands, or MCP tools.

---

## When to use which engine

### `CheatEngine`

Use this when you want a one-shot apply against memory:

- write a value now
- parse/import/export cheat text
- convert between cheat formats

### `CdsCheatEngine`

Use this when the game constantly overwrites the target value and you need the cheat to persist:

- infinite health
- locked ammo
- repeated conditional writes
- timer-gated cheats that should only run every N frames

---

## Raw cheat-code format

Each cheat block can have an optional title and one or more code lines:

```text
#Infinite Health
20123456 000003E8

#God Mode (conditional)
E0000001 80001234
20005678 0000FFFF
```

Each code line is two hex words:

- **word 0** = type + address payload
- **word 1** = value / interval

### Supported line types

| Type byte | Meaning |
|---|---|
| `00` | 8-bit write |
| `10` | 16-bit write |
| `20` | 32-bit write |
| `B0` | timer gate |
| `D0` | 16-bit conditional |
| `E0` | 32-bit conditional |

### Address encoding

For write and condition rows, the lower 24 bits store the address payload.

Example:

- PS2 address `0x80123456`
- encoded row word = `0x20123456`

---

## Persistent CDS cheat engine

`CdsCheatEngine` injects a MIPS routine into unused EE RAM and hooks a live game instruction so the routine executes every frame.

### High-level lifecycle

1. compile the embedded CDS engine template
2. write the engine into EE RAM
3. save the original instructions at the hook site
4. patch the engine with:
   - original instructions
   - return address
   - cheat-table base pointer
5. write the cheat table
6. replace the hook instruction with `j engineEntry`
7. later:
   - update the cheat table without re-hooking
   - or remove the hook and restore original instructions

### Memory layout

Offsets are relative to `EngineBase` (default `0x000C0000`):

| Offset | Purpose |
|---|---|
| `+0x0000` | injected engine code |
| `+0x0200` | engine data (saved instructions, return address, frame counter) |
| `+0x0300` | cheat table |

### Hook-site requirements

Choose a hook address that runs frequently, typically:

- a main loop instruction
- a frame-update routine
- a VBlank-adjacent function

The engine replaces the instruction at `hookAddress` and uses the next word as a delay-slot NOP, while replaying the original two instructions inside the injected routine before returning.

---

## CDS cheat source

The biggest usability improvement is that you can now author persistent cheats in CDS instead of hand-writing raw cheat rows.

### `freeze`

```cds
freeze byte $20347A50 = 63
freeze half $20347A52 = 03E7
freeze word $20347A54 = FFFFFFFF
```

### `cond / endcond`

```cds
cond mem[$20347A50] == 0001
  freeze word $20347A54 = FF
endcond
```

### `timer / endtimer`

```cds
timer 60
  freeze word $20347A54 = FF
endtimer
```

### Nesting

```cds
timer 30
  cond mem[$20347A50] == 0001
    freeze word $20347A54 = FF
  endcond
endtimer
```

This compiles into cheat-table rows in the same order the engine will process them.

---

## How CDS maps to cheat rows

`CdsCompiler.GetCheatTableEntries(...)` converts cheat-oriented CDS directives into raw engine rows.

### Example mapping

#### Source

```cds
freeze word $20347A54 = FF
```

#### Cheat row

```text
20347A54 000000FF
```

#### Source

```cds
cond mem[$20347A50] == 0001
  freeze word $20347A54 = FF
endcond
```

#### Cheat rows

```text
C0347A50 00000001
20347A54 000000FF
```

#### Source

```cds
timer 60
  freeze word $20347A54 = FF
endtimer
```

#### Cheat rows

```text
B0000001 0000003C
20347A54 000000FF
```

The timer row stores:

- type `0xB0`
- guarded body-entry count in the low 24 bits of word 0
- frame interval in word 1

---

## C# examples

### One-shot cheat application

```csharp
using CodeDesigner.Ps2.CheatEngine;

string cheatText = "#Infinite Health\n20123456 000003E8";
var list = CheatList.Parse(cheatText);

var engine = new CheatEngine(list);
engine.Apply(byteEditor);
```

### Inject persistent engine from raw cheat codes

```csharp
using CodeDesigner.Ps2.CheatEngine;

var engine = new CdsCheatEngine(byteEditor);
engine.Inject(0x001A3BC0, @"
#Infinite Health
20123456 000003E8
");
```

### Inject persistent engine from CDS

```csharp
using CodeDesigner.Ps2.CheatEngine;

var engine = new CdsCheatEngine(byteEditor);

engine.InjectFromCds(0x001A3BC0, @"
freeze word $20347A54 = FFFFFFFF

cond mem[$20347A50] == 0001
  freeze half $20347A52 = 03E7
endcond
");
```

### Update without re-hooking

```csharp
engine.UpdateCheatTableFromCds(@"
timer 60
  freeze byte $20347A50 = 63
endtimer
");
```

### Remove the engine

```csharp
engine.Remove();
```

---

## MCP workflow

The MCP server exposes the persistent cheat-engine lifecycle directly.

### Inject

Tool: `inject_cheat_engine`

Arguments:

- `hookAddress` (required)
- `cdsSource` or `cheatCodes`
- optional `engineBase`

Example:

```text
inject_cheat_engine(
  hookAddress: "0x001A3BC0",
  cdsSource: "
    freeze word $20347A54 = FFFFFFFF
    cond mem[$20347A50] == 0001
      freeze half $20347A52 = 03E7
    endcond
  "
)
```

### Update

Tool: `update_cheat_engine`

Example:

```text
update_cheat_engine(
  cdsSource: "
    timer 60
      freeze byte $20347A50 = 63
    endtimer
  "
)
```

### Remove

Tool: `remove_cheat_engine`

```text
remove_cheat_engine()
```

---

## Bridge commands

The same workflow is available through the bridge:

- `INJECTCHEATENGINE`
- `UPDATECHEATENGINE`
- `REMOVECHEATENGINE`

Those commands accept either:

- raw cheat text in `Data`
- CDS cheat source in `CdsSource`

When both are supplied, CDS takes precedence.

---

## End-to-end examples

### Example 1: always-on infinite health

```cds
freeze word $20347A54 = FFFFFFFF
```

Best for values the game rewrites every frame.

### Example 2: conditional cheat

```cds
cond mem[$20347A50] == 0001
  freeze word $20347A54 = FF
endcond
```

Best when the cheat should only apply in a known state or mode.

### Example 3: timer-gated behavior

```cds
timer 30
  freeze word $00123456 = 00000001
endtimer
```

Best when the cheat should run periodically instead of every engine iteration.

### Example 4: combined live workflow

1. pick a stable hook like `0x001A3BC0`
2. inject:

```cds
freeze word $20347A54 = FFFFFFFF

timer 60
  cond mem[$20347A50] == 0001
    freeze half $20347A52 = 03E7
  endcond
endtimer
```

3. play and verify
4. update only the table if you need different values
5. remove the engine to restore the original code

---

## Related classes

| Class | Role |
|---|---|
| `CheatEngine` | Applies parsed cheat codes to an `IByteEditor` |
| `CheatList` | Collection of cheat blocks parsed from text |
| `CheatBlock` | Named group of cheat rows |
| `Cheat` | One decoded cheat row |
| `CheatTimer` | Timer bookkeeping for recurring cheats |
| `CdsCheatEngine` | Injected every-frame cheat engine driven by raw cheats or CDS |
| `IByteEditor` | Memory read/write abstraction used by both engines |

---

## Related docs

- [../CodeDesigner.Ps2.CdsLanguage/README.md](../CodeDesigner.Ps2.CdsLanguage/README.md) — full CDS language guide
- [../SOCKET_COMMUNICATION.md](../SOCKET_COMMUNICATION.md) — socket-level command schemas
- [../CodeDesigner.Ps2.Bridge/README.md](../CodeDesigner.Ps2.Bridge/README.md) — bridge integration details
