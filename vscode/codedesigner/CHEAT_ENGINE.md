# Cheat Engine — Getting Started Guide

Welcome! This guide explains how to use the **Cheats tab** in CodeDesigner to apply cheat codes to a running PS2 game. No programming experience is required for basic cheats, but we'll also cover the more powerful persistent cheat engine for those who want more control.

---

## What is the Cheat Engine?

The **Cheat Engine** in CodeDesigner lets you:

1. **Apply one-shot cheat codes** — write values directly into game memory right now
2. **Run persistent cheats** — inject a small routine that keeps re-applying your cheat every frame, even if the game tries to reset the value

Most of the time you just want option 1. Option 2 is for values the game constantly overwrites (like health bars).

---

## The Cheats Tab

The **Cheats** tab has two main sections:

### Archive Cheats (PCSX2 mode)
When connected to PCSX2, CodeDesigner automatically fetches known cheats for the running game from the CheatCodeArchive. Just tick the ones you want and click **Apply All Enabled**.

### Manual Cheat Codes
Paste raw GameShark / Action Replay codes into the text box and click **Update** to apply them immediately.

---

## Cheat Code Format

Each cheat is one or more lines in this format:

```
20123456 000003E8
```

- The first number is the **type + address** — the first two digits indicate the type of write, the rest is the memory address
- The second number is the **value** to write

You can group related lines under a title:

```
#Infinite Health
20123456 000003E8

#Max Ammo
20654321 00000063
```

The `#` line is just a human-readable label — it has no effect on the cheat itself.

---

## Write Types

The first two digits of the address word tell the engine what kind of write to perform:

| First two digits | What it does |
|-----------------|--------------|
| `00`            | Write **1 byte** (8-bit) |
| `10`            | Write **2 bytes** (16-bit halfword) |
| `20`            | Write **4 bytes** (32-bit word) — most common |
| `D0`            | **Conditional**: only run the next line if a 16-bit value matches |
| `E0`            | **Conditional**: only run the next line if a 32-bit value matches |

### Example — conditional cheat

```
#God Mode (only when in-game)
E0000001 80001234
20005678 0000FFFF
```

This reads the 32-bit value at `0x80001234`. If it equals `0x00000001`, it writes `0x0000FFFF` to `0x80005678`. Otherwise it does nothing.

---

## How to Find Cheat Addresses

Use the built-in **Memory Scanner** (click 🔍 **Scanner** in the Cheats tab):

1. Start the game and get to a point where you can see the value you want to change (e.g. health = 100)
2. Open the Scanner and search for `100` as a 32-bit value
3. Take a hit in-game, reducing health to say 80
4. Search again for `80` — the Scanner narrows down the matches
5. Repeat until you have one or very few addresses left
6. That address is where the game stores that value

Then write a cheat code like:
```
20 + last_six_digits_of_address   new_value
```

For example, if the address is `0x20347A54` and you want to lock health at `0x3E7` (999):
```
20347A54 000003E7
```

---

## Tools in the Cheats Tab

| Button | What it does |
|--------|--------------|
| ↕ **Converter** | Encrypt or decrypt cheat codes between formats |
| ⇋ **Porter** | Port cheats from one game version (e.g. NTSC-U) to another (PAL) |
| 🔍 **Scanner** | Search live memory for a value to find its address |
| 🕹️ **Joker** | Build a button-combo code that only activates when you press a specific controller button combination |

---

## Persistent Cheats (Every-Frame Engine)

Some games reset health, ammo, or other values every frame. A one-shot apply only works for a split second. For these you need the **persistent cheat engine**.

### What it does

CodeDesigner injects a tiny MIPS routine directly into the game's RAM. This routine runs every single frame and keeps re-writing your cheat values. When you're done, you remove it and the game goes back to normal.

### Using CDS for persistent cheats

Open the **CDS tab** and write a `.cds` file using `freeze`, `cond`, and `timer` directives:

#### `freeze` — lock a value every frame

```cds
freeze word $20347A54 = FFFFFFFF
```

This writes `0xFFFFFFFF` to `0x20347A54` every frame. Sizes:
- `byte` — 1 byte
- `half` — 2 bytes
- `word` — 4 bytes

#### `cond / endcond` — conditional freeze

Only apply the freeze when a memory value matches:

```cds
cond mem[$20347A50] == 0001
  freeze word $20347A54 = 000003E7
endcond
```

This only locks health when the value at `0x20347A50` equals `1` (for example, when the player is alive).

#### `timer / endtimer` — run every N frames

Instead of running every frame, run once every 60 frames:

```cds
timer 60
  freeze word $20347A54 = 000003E7
endtimer
```

#### Combining them

```cds
timer 30
  cond mem[$20347A50] == 0001
    freeze word $20347A54 = 000003E7
  endcond
endtimer
```

Every 30 frames, check if the player is alive (value at `$20347A50` equals 1) — and if so, restore health.

### Injecting the engine

1. Write your `.cds` cheat file
2. Go to the **CDS tab** → click **Compile** to verify it
3. Use the **Inject Cheat Engine** MCP tool (or ask GitHub Copilot) to inject it at a hook address

You need a **hook address** — an instruction in the game that runs frequently (like a main-loop function). The engine replaces that instruction with a jump to itself, runs your cheats, then jumps back.

### Updating and removing

- **Update** — change the cheat values without unhooking (just swaps the cheat table in RAM)
- **Remove** — restore the original instruction and remove the injected code entirely

---

## The Joker Code Builder

The 🕹️ **Joker** button opens a helper that builds a button-combo conditional code.

A Joker code only activates your cheat when you press a specific controller button (or combination). This is useful when you want to toggle a cheat manually during gameplay.

1. Click 🕹️ **Joker**
2. Press the button combination you want on your controller
3. The builder generates the conditional prefix for your cheat code
4. Paste it above your existing cheat lines

Example output (L1 + R1 held):
```
D00ABCD0 0000F9FF
20123456 000003E8
```

---

## Common Cheat Code Examples

### Infinite Health
```
#Infinite Health
20347A54 000003E8
```

### Max Ammo (16-bit write)
```
#Max Ammo
10347A58 00000063
```

### Unlock All Levels (byte write)
```
#All Levels Unlocked
00347A5C 000000FF
```

### Only Apply In-Game (32-bit conditional)
```
#God Mode (in-game only)
E0000001 20347A50
20347A54 000003E8
```
Read the 32-bit value at `0x20347A50`; if it equals `1`, apply the god mode write.

---

## Tips for Beginners

- **Start with the Archive** — if you're playing a well-known game, someone has probably already found the addresses. Check the Archive Cheats section first.
- **Use the Scanner** — you don't need to know assembly to find addresses. The scanner does all the work.
- **32-bit writes are most common** — when in doubt, start with type `20`.
- **One cheat at a time** — apply one cheat, check it works, then add the next. This makes it easy to tell which one is causing a problem.
- **If the game freezes or crashes** — you likely wrote to the wrong address or size. Click **Update** with an empty text box to clear your cheats, then restart the emulator.

---

## Next Steps

- Learn CDS scripting to write more powerful patches — see the **[CDS Guide](CODE_DESIGNER_SOURCE.md)**
- Use **GitHub Copilot** inside VS Code to ask the AI to find addresses, write patches, or explain what a cheat code does
- Check the **Memory Viewer** (EE Main RAM panel) to watch values change in real time as the game runs

Happy cheating! 🎮
