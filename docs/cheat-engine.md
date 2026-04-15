[← Back to Documentation Index](README.md)

# Cheat Engine — User Guide

The CodeDesigner cheat engine lets you apply cheat codes to a running PS2 game, either as a one-time write or as a persistent routine that re-applies the cheat every game frame.

---

## The Cheats tab

The **Cheats** tab has two main sections.

### Archive cheats (PCSX2 mode)

When connected to PCSX2, CodeDesigner automatically fetches known cheats for the running game from the CheatCodeArchive. Tick the cheats you want and click **Apply All Enabled**.

### Manual cheat codes

Paste raw GameShark / Action Replay codes into the text box and click **Update** to apply them immediately.

---

## Cheat code format

Each block has an optional title line and one or more code lines:

```
#Infinite Health
20123456 000003E8

#Max Ammo
20654321 00000063
```

The first byte of the address determines the operation:

| Prefix | Operation |
|---|---|
| `20` | Write 32-bit word |
| `10` | Write 16-bit halfword |
| `00` | Write 8-bit byte |
| `D0` / `E0` | Conditional word write (compare first) |

---

## One-shot vs. persistent cheats

### One-shot (Apply / Update)

Writes the value directly into game memory right now. Works well for values the game does not reset every frame (e.g. unlocking a level or setting a flag).

### Persistent (Cheat Engine injection)

For values the game constantly overwrites — health bars, ammo counters — you need a routine that runs every frame and keeps re-applying the write.

Use the CDS language's `freeze`, `cond`, and `timer` blocks to describe persistent cheats, then click **Inject** in the Cheats tab. CodeDesigner assembles a small MIPS routine and injects it into EE RAM. The routine hooks into the game's VBLANK handler and re-applies all active cheats each frame.

Commands:
- **Inject** — assemble the routine and install the hook
- **Update** — update the cheat table in place (no re-hook needed)
- **Remove** — restore the original hook bytes and clear the routine

---

## Writing persistent cheats in CDS

```cds
# Keep health at 9999 every frame
freeze $20123456 = 9999

# Only freeze ammo when the weapon is active
cond $80001234 == 1
  freeze $20654321 = 99
endcond

# Run a write once every 60 frames (once per second at 60 fps)
timer 60
  freeze $20ABCDEF = 100
endtimer
```

Compile the `.cds` file from the **CDS** tab, then click **Add Cheat** to send the result to the Cheats tab and inject it.

---

## Format conversion

The **Cheat Converter** converts between GameShark, Action Replay, and other PS2 cheat formats. Open it from the toolbar in the Cheats tab.

## Porting cheats between game revisions

The **Cheat Porter** maps cheat codes from one game version (e.g. NTSC-U) to another (e.g. PAL) by scanning for equivalent patterns in both ELF images. Open it from the toolbar in the Cheats tab.

---

## Tips

- Use one-shot codes for permanent unlocks; use `freeze` for values the game resets every frame.
- The persistent engine is injected at `0x000C0000` by default. This address must be unused by the game — check the memory map for your title if you see crashes after injection.
- Disabling all `freeze` blocks and clicking **Remove** cleanly restores the game to its original state without restarting.
