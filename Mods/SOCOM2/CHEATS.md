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
| `god` | Health stays full | +0xFFC body-part health (refilled from +0x1014) and +0x1044 health bar |
| `ammo` | Magazines refill as you fire | +0x7B4 rounds in magazine, one per weapon slot |
| `autoaim` | Auto-aim on (same as the menu option) | +0x5E0 weapon bits, 0x10000 |
| `rapid` | Rapid fire / fires automatically at close enemies | +0x5E0 weapon bits, 0x10 (cleared when you stop) |

With no names it runs `god ammo`. It follows you through respawns.

## Kill and respawn

```
python SOCOM2/bots/socom_bots.py kill
python SOCOM2/bots/socom_bots.py respawn
```

`kill` zeroes each body part's health (+0xFFC) and the health bar (+0x1044), then runs the game's death check
(0x5AB040) - the routine every hit goes through - so you die normally. `respawn` works alive or dead: it turns the
`mp_allow_respawn` setting on (value at 0x695450) and writes the respawn trigger `0x66666666` to your soldier at
+0xFCC **once**; the game rebuilds you at a spawn point through its own path, so other players see it. (Writing it
every frame, as the old instant-respawn code did, respawns you over and over.)

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

## Give a weapon

```
python SOCOM2/bots/socom_bots.py giveweapon <pack> [slot]
```

Turns a weapon you carry (slot 0 primary - the default, 1 sidearm) into a converted or custom weapon and respawns you
holding it: loads the pack, points the weapon record at the new model and sets its HUD name, magazine size and icon
(copied from the closest SOCOM II icon). Looks and HUD only - damage, fire rate and sound stay the old weapon's.
Everyone using that weapon on the map gets the new look; a new map undoes it.

| Pack | Weapon |
|---|---|
| mcw02 | AK-47 (SOCOM: Combined Assault) |
| mcw12 | Desert Eagle (CA) |
| mcw16 | FN P90 (CA) |
| mcw24 / mcw30 / mcw32 | MP5 / M4A1 / M16A2 (CA) |
| mrgun | Ray gun (custom - `SOCOMCA/custom_weapon.py raygun`) |

All 88 CA weapons are in `F:/SOCOM Packs/SOCOMCA/weapons` (`weapons.json`, a preview per pack).

## Add a weapon to the armory

```
python SOCOM2/bots/socom_bots.py armory mcw16 F90
python SOCOM2/bots/socom_bots.py armory mrgun F57
```

Takes a weapon record that normally isn't offered (F90, F57 ...), makes it the weapon in the pack (model, name,
magazine, icon), turns its `Enable_<weapon>` setting on for both teams (0x3F13F0(code), halfword at +4: 0x1 SEALs,
0x8 terrorists) and renames its armory caption. Run it in the lobby: the armory spins the new model. The caption
("SUBMACHINE GUN: F90") is a language-table message (`_1049_SubmGunF90_MSG`), not the record, and the armory copies
the one on screen, so every copy is rewritten in place; a new name can't be longer than the old caption.

In the lobby the record keeps its retail model (+0x1C): the map load builds every loadout weapon from it, and a
pack model isn't in the new map (pointing it at one hung the load). Run the command again once the round starts:
that loads the pack, swaps the model and the HUD icon - then respawn (or use `giveweapon`) to hold it.

The record's real name (+0x04) is never changed: the loadout list keys on it (rename it and the slot shows
`<NONE>`); only the display names (+0x08/+0x0C) are.

In the lobby it also makes the armory spin the new model. Each weapon has a lobby preview node `Equip<weapon>` (the
`<model>UI` model's +0x64) with one child mesh; the command hangs a fresh instance's `_high` mesh there (copying the
old mesh's render mask +0x80). Lobby draws only use the category 0x00 texture pool, so the pack's textures are
placed there as well. `.armory_ui` remembers each record's original preview model for re-runs.

## Weapon look swap

```
python SOCOM2/bots/socom_bots.py swapweapon sig_commando mcw02
```

Test only: points held copies of a SOCOM II weapon at a converted weapon model (here the SOCOM: Combined Assault
AK-47). The game draws held weapons from the model's own instance list, so this does not show yet; weapon pickups
are the planned route.

## Weapon records

0x290-byte records (one per weapon and variant, e.g. 552 and 552SD): +0x00 class 0x434D30, +0x04/+0x08/+0x0C
name, +0x14 icon (.tif), +0x1C model name, +0x20 impact mark, +0x28 magazine size, then stats (floats).

## Field map

Offsets from the Seal Pointer (`*0x0044D648`, the player's CZSealBody):

| Offset | Field |
|---|---|
| +0x014 | callsign string pointer |
| +0x01C | position (x, y, z) |
| +0x0C0 | controller pointer |
| +0x0C8 | team mask (0x40000001 SEAL, 0x80000100 terrorist) |
| +0x0E0 | flags byte: 0x40 = a body is in reach to pick up |
| +0x0E4 / +0x0E8 | interaction flags: 0x80000 action icon on screen (pick up / drop body), 0x100000 carrying a body |
| +0x0D4 / +0x0D8 | nearby soldiers: count / list of 0x30-byte entries (body, position relative to you), nearest first |
| +0x174 | stance (0 stand, 1 crouch, 2 prone, 9 carrying a body) |
| +0x1C8 | action icon visible (1) |
| +0x528, +0x52C, +0x530 | animation set pointers (shared table, per-movement floats) |
| +0x534 | action / animation state (packed: low half current, high half likely next; 0xD9 carrying a body) |
| +0x540 | UI character type |
| +0x594 | shots fired |
| +0x5E0 | weapon bits: 0x4 firing, 0x10 rapid / auto fire, 0x10000 auto-aim |
| +0x6C4 | weapon records: +0x6C4 is the weapon in hand (switches to a one-handed weapon while carrying a body) |
| +0x73C | ammo object pointer per slot |
| +0x7B4 | rounds in magazine per slot |
| +0x7DC | second per-slot counter |
| +0xDFC-+0xE54 | recoil, spread and stance accuracy |
| +0xEC4-+0xF0C | damage multiplier per body part (18 words: 0 = no damage, 0x52222222 = any hit kills) |
| +0xFB8-+0xFC4 | killer / cause of death (0xFFFFFFFF / 0xFF while alive) |
| +0xFCC | respawn trigger: write 0x66666666 once to respawn (0x19 alive, 0x20 dead) |
| +0xFD0 / +0xFD8 | death state / timer (0x1F0001 / 0xFFFF dead, 0 / 1.0 alive) |
| +0xFFC | health per body part (6 floats, zeroed at death); +0x1014 their maximums (online: 8, 30, 30, 50, 30, 30) |
| +0x1044 | health bar (display, 1.0 = full) |
