# Cheat Engine — Getting Started Guide

The CodeDesigner cheat engine lets you apply cheat codes to a running PS2 game as a one-time write or as a persistent routine that re-applies every game frame.

---

## The Cheats tab

The **Cheats** tab has two main sections.

### Archive cheats (PCSX2 mode)

When connected to PCSX2, CodeDesigner fetches known cheats for the running game from the CheatCodeArchive automatically. Tick the cheats you want and click **Apply All Enabled**.

### Manual cheat codes

Paste raw GameShark / Action Replay codes into the text box and click **Update** to apply them immediately.

---

## Cheat code format

```
#Infinite Health
20123456 000003E8

#Max Ammo
20654321 00000063
```

| Prefix | Operation |
|---|---|
| `20` | Write 32-bit word |
| `10` | Write 16-bit halfword |
| `00` | Write 8-bit byte |
| `D0` / `E0` | Conditional word write |

---

## One-shot vs. persistent cheats

**One-shot** — writes the value right now. Good for unlocking levels, setting flags, or any value that stays put.

**Persistent** — injects a small MIPS routine into EE RAM that hooks the VBLANK handler and re-applies every frame. Use this for health bars, ammo counters, and anything the game resets constantly.

### Commands

| Button | Action |
|---|---|
| **Inject** | Assemble the routine and install the hook |
| **Update** | Update cheat table in place (no re-hook) |
| **Remove** | Restore original hook bytes and clear routine |

---

## Writing persistent cheats in CDS

Open the **CDS** tab and use `freeze`, `cond`, or `timer` blocks:

```cds
# Keep health at max every frame
freeze $20123456 = 9999

# Only freeze when a condition is true
cond $80001234 == 1
  freeze $20654321 = 99
endcond

# Run once per second (60 frames)
timer 60
  freeze $20ABCDEF = 100
endtimer
```

Compile in the CDS tab, then click **Add Cheat** → **Inject**.

---

## Tips

- Use one-shot codes for permanent unlocks; use `freeze` for values the game resets every frame.
- The persistent engine injects at `0x000C0000` by default. Verify that address is unused by your game if you see crashes after injection.
- Click **Remove** to cleanly restore the game without restarting.
