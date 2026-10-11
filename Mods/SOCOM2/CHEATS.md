# SOCOM II test cheats and player tools

Commands in `SOCOM2/bots/socom_bots.py` for testing on PCSX2 with the CodeDesigner bridge connected. You need to be
in a round (single player or online). They change only your own game, nothing is sent over the network, so other
players see normal behaviour. Don't use them in real online matches.

## Cheats

```
python SOCOM2/bots/socom_bots.py cheats god ammo autoaim rapid
```

Holds the chosen cheats until you stop it (Ctrl+C in its terminal, or close the terminal). Pick any of:

| Cheat | What it does | Field (your soldier, from the Seal Pointer) |
|---|---|---|
| `god` | Health stays full | +0x1044 health (float, 1.0 = full) |
| `ammo` | Magazines refill as you fire | +0x7B4 rounds in magazine, one per weapon slot |
| `autoaim` | Auto-aim on (same as the menu option) | +0x5E0 weapon bits, 0x10000 |
| `rapid` | Rapid fire / fires automatically at close enemies | +0x5E0 weapon bits, 0x10 (cleared when you stop) |

With no names it runs `god ammo`. It follows you through respawns.

## Soldiers on the map

```
python SOCOM2/bots/socom_bots.py bodies
```

Lists every soldier with its body address and callsign (SPECTER, JESTER, enemy names ...).

## Squad panel

```
python SOCOM2/bots/socom_bots.py squadhud [JESTER WARDOG VANDAL]
```

Fills the bottom-of-screen squad panel (name, health bar, order) with the named soldiers, or your three nearest
teammates. **Work in progress:** the slots change but a new name doesn't appear yet - the panel builds its rows
when the squad is assigned, and that setup path isn't found yet.

## Weapon look swap

```
python SOCOM2/bots/socom_bots.py swapweapon sig_commando mcw02
```

Test only: points held copies of a SOCOM II weapon at a converted weapon model (here the SOCOM: Combined Assault
AK-47). The game draws held weapons from the model's own instance list, so this does not show yet; weapon pickups
are the planned route.

## Field map

Offsets from the Seal Pointer (`*0x0044D648`, the player's CZSealBody):

| Offset | Field |
|---|---|
| +0x014 | callsign string pointer |
| +0x01C | position (x, y, z) |
| +0x0C0 | controller pointer |
| +0x0C8 | team mask (0x40000001 SEAL, 0x80000100 terrorist) |
| +0x174 | stance (0 stand, 1 crouch, 2 prone) |
| +0x534 | current action / animation state |
| +0x594 | shots fired |
| +0x5E0 | weapon bits: 0x4 firing, 0x10 rapid / auto fire, 0x10000 auto-aim |
| +0x6C4 | weapon record pointer per slot (0x290-byte records: name, icon, model, stats) |
| +0x73C | ammo object pointer per slot |
| +0x7B4 | rounds in magazine per slot |
| +0x7DC | second per-slot counter |
| +0xDFC-+0xE54 | recoil, spread and stance accuracy |
| +0xFFC | damage ratio per body part (18 floats) |
| +0x1044 | health |
