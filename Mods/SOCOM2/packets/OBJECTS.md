# SOCOM II network objects (SCUS-97275)

Machine-readable schema: [objects.json](objects.json) (used by `dme_objects.py`, the bot script and the server script).
Labels: set **SOCOM2_r0004**, the Seal Pointer tree (`0x0044D648`) plus the DME labels listed at the end.

## In-memory object

Find an object by its class-name string. The object base is the name address minus 0x10.

| Off  | Type      | Meaning |
|------|-----------|---------|
| 0x00 | u32       | 0x51 (constant) |
| 0x04 | u16       | type: 0 Seal, 1 Persona, 2 Blather, 3 Grenade (KProjectileObject), 4 Game, 5 Bomb, 6 Turret (TurretObject) |
| 0x06 | u16       | flags: 0x0102 player-owned (Seal, Persona, Blather), 0x0002 host-global (Game, Bomb) |
| 0x0C | u32       | owner: DME client index (0 = host) |
| 0x10 | char[16]  | class name (`SealObject`, `PersonaObject`, ...) |
| 0x20 | ptr       | current state (layout below) |
| 0x24 | ptr       | last-sent copy; `00 03` updates carry only the fields that differ from it |

**Object table `0x00675F20`**: 256 record pointers indexed by object id. The record is the 0x51 word + 4: +0 type, +8 owner, +0xC name, +0x1C state. Lookup: `0x0062A108(&out, id)`. In a round on mp73 the table held: 0x00 Game, 0x01 your persona, 0x02 Blather, 0x03 your soldier, 0x04–0x06 TurretObjects, 0x07 the round's bomb, 0x21–0x25 bot personas, 0x61–0x65 bot soldiers.

The existence table at `0x00679FE8[id]` holds 2 when the object exists. Ids seen: personas 0x20+i, grenades 0x40–0x5F, bot soldiers 0x60+i.

## Class descriptors: `0x0067BE08 + type*0x228`

- `+0` state size
- `+4` field count
- `+8` name
- `+0x28` pointers to 32-byte field descriptors: {offset, element size, count, kind, priority}
- Kinds: 0 s8, 1 u8, 2 s16, 3 u16, 4 u32, 5 s32, 6 f32, 13 vec3

On the wire, fields are referred to by **index**, not by offset.

## SealState: type 0, 0x90 bytes, 36 fields

`SealState_Pack` (`0x002BF280`) builds this state from the CSealEx entity every frame. Every source offset below is also a member under **Seal Pointer**.

| Idx | Off  | Type   | Name | CSealEx source |
|-----|------|--------|------|----------------|
| 00–02 | 0x00 | f32 ×3 | x y z | +0x1C / +0x20 / +0x24 |
| 1D | 0x0C | s32 | team | +0xC8 team mask (0x40000001 SEAL, 0x80000100 TERR) |
| 1E | 0x10 | s32 | unit | controller (+0xC0) vfunc +0x6C |
| 22 | 0x14 | str32 | model | `mpXX_sealN` chartype |
| 23 | 0x34 | str48 | loadout | equipment block (0x543860) |
| 03 | 0x64 | s16 | heading | atan2 of body (+0x28) direction × 10430.06 |
| 1B | 0x66 | u16 | equipped | 0x5D00E0(+0x5E0): equipped weapon id (121 = M67 once the grenade is selected) |
| 04–09 | 0x68–0x72 | s16 | anim0–anim5 | +0x59E, +0x598, +0x5C8, +0x556, +0x550, +0x580 |
| 1C | 0x74 | u16 | entity_ref | `0x20 +` the body's entity index (`CZSealBody+0x04`); your soldier (object 3) sends 35. Bots send `0x20 +` their soldier object id |
| 18 | 0x76 | u16 | weapon | +0xFBC (255 = none) |
| 21 | 0x78 | u16 | f21 | +0x0C |
| 0A/0C/0B | 0x7A–0x7C | s8 | input_x / input_y / input_z | controller +0x08 / +0x0C / +0x10, ×127 |
| 1F | 0x7D | s8 | pitch | aim vector +0x144C (+ accumulator +0x228) |
| 0D | 0x7E | u8 | stance | 0x5E77E0(entity) |
| 0E/0F | 0x7F/0x80 | u8 | f0E / f0F | +0x374 / +0x375 |
| 10 | 0x81 | u8 | ctrl_flag | controller +0x170 bit |
| 11–16 | 0x82–0x87 | u8 | hp_part0–5 | cur/max ×255; cur +0xFFC/+0x1008/+0x1010/+0x100C/+0x1004/+0x1000, max +0x18 |
| 17 | 0x88 | u8 | slot | +0xF7A |
| 19 | 0x89 | u8 | item | 0x5A7B20(+0xFC0), held item (121 = M67) |
| 1A | 0x8A | u8 | weapon_state | 0x5CAC60(+0x5E0): 1 when the grenade is selected; 1 → 2 while sprinting |
| 20 | 0x8B | u8 | **shot_count** | +0x220: +1 per shot, with a pitch kick; this is how shots go over the network |
| (none) | 0x8C | ptr | CSealEx* | local only, not sent |

Live host values:

- Body-part max health is [8, 30, 30, 50, 30, 30], so part 0 is probably the head and part 1 the torso.
- `+0x54` (labelled health) read 0.38 while every body part was full, so it may be something else.

## What the in-game capture showed (move, shoot, animation, grenade)

- **Shooting** sends no message of its own. Each shot bumps `shot_count` (1→5 over 5 shots), and `pitch` kicks from -34 to -41.
- **`02 02` is `soldier_action`**, not weapon fire. The receiver `0x2BD720` calls `0x58D710(soldier, arg, arg2, flag, action)`, which switches on action 0–21 (jump table `0x65D170`) and plays animations via `0x58E160`. Seen: action 1/34/4 when you did the animation, and action 0/243/1 right before the grenade throw.
- **Moving:** `input_x` goes to ±127 and 0, with x/z position updates. `ctrl_flag` toggles 1/0 on a held button.
- **Grenade:**
  1. Selecting it sends `equipped=121 weapon_state=1`.
  2. A `soldier_action 0/243/1` event, then `stance` 1→2.
  3. Create KProjectileObject: `thrower` = the thrower's soldier id (3 for the host), vel (-107, 31, -100).
  4. `state=1`, then the owner streams pos/vel. vel.y drops 6.54 per 0.067 s, so gravity is about 98 u/s². Speed shrinks on each bounce and is 0 at rest.
  5. `state=2` (explode), then destroy.

## PersonaState: type 1, 0x4C bytes, 12 fields

| Idx | Off | Type | Name | Seen |
|-----|-----|------|------|------|
| 0 | 0x00 | s32 | team | team mask |
| 1 | 0x04 | u32 | f01 | 2 |
| 2 | 0x08 | u32 | f02 | 985 (bots) / 970 (host) |
| 3 | 0x0C | u16 | f03 | 2 |
| 4 | 0x0E | str32 | name | player name |
| 5 | 0x2E | str16 | clan | empty |
| 6–8 | 0x3E–0x40 | u8 | f06–f08 | f08: 0 bots / 2 host |
| 9 | 0x41 | u8 | ready | 2 ready / 1 not |
| 10 | 0x42 | u8 | f0A | 5 |
| 11 | 0x43 | s8 | f0B | 0 bots / -1 host |

`+0x44`, which is not sent, holds the player index.

## GrenadeState: type 3 (`KProjectileObject`), 0x24 bytes

| Idx | Off | Type | Name |
|-----|-----|------|------|
| 0 | 0x00 | vec3 | pos |
| 1 | 0x0C | vec3 | vel |
| 3 | 0x18 | u16 | thrower: the soldier object id, which must exist |
| 2 | 0x1A | u8 | state: 1 thrown, 2 explode |

Create handler: `0x002C3400`. It also needs `*(0x004446F8)+0x117 == 3` (round in progress).

## GameState: type 4, 0x5C bytes (host-global)

| Idx | Off | Type | Name | mp61 |
|-----|-----|------|------|------|
| 0 | 0x00 | str5 | map | `mp61` |
| 1, 2 | 0x08, 0x0C | s32 | time_limit? | 300, 300 |
| 3 | 0x10 | s32 | start_stamp? | 0xF4AD5, the same stamp as in `00 04` creates |
| 5, 6 | 0x14, 0x16 | u16 | f05, f06 | 73, 1715 |
| 4 | 0x18 | s32 | stamp2? | 0x2A211A |
| 7–12 | 0x1C–0x21 | u8 | f07–f0C | 4, 11, 64, 0, 4, 0 |
| 13 | 0x22 | str32 | str22 | |
| 14 | 0x42 | str24 | str42 | |

These values did not change across 8 s of sampling, so the timer fields still need a capture across a round change.

## BombState: type 5 (host-global)

`pos` vec3 @0x00, `carrier?` u16 @0x0C, `f03` u16 @0x0E, `state?` u8 @0x10 (1 while the bomb is at its spawn).

## BlatherState: type 2

A 460-byte block of quick-chat/radio entries such as `_60757_ImGoingLeft_MSG`.

## Labels added (SOCOM2_r0004)

| Address | Name |
|---------|------|
| `0x0067BE08` | DME Class Descriptors (array 7 × 0x228) |
| `0x00679FE8` | DME Object State Table |
| `0x0044D820` | DME Game Message Table |
| `0x002BF280` | SealState_Pack (other sets call it `UDP_InboundPacketProcessor`, which is wrong) |
| `0x002BF110` | DME_FindObjectById |
| `0x002C3400` | GrenadeObject_OnCreate |
| `0x0032A420` | DME_SendGameMessage |
| `0x004446F8` | Game State Ptr |

There are also 32 new members under Seal Pointer, covering every packer source above.
