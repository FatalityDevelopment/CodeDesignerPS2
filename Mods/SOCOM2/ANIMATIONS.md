# SOCOM: Combined Assault animations in SOCOM II

CA's animations are converted onto the SOCOM II skeleton and played by your soldier in game. CA built its animation
set on SOCOM II's: same skeleton (CA adds forearm-twist bones), same units, same int16 keys, and the same clip names
(`seal_walk`, `seal_place_claymore` ... 219 of SOCOM II's 341 names exist in CA). CA adds its own clips, such as the
coop medic animation `seal_healing`.

## Commands

```
python SOCOMCA/ca_anim.py extract                      # once: CA banks + SOCOM II MOTION_P.ZAR -> F:/SOCOM Packs/SOCOMCA/anims
python SOCOM2/bots/socom_bots.py anim seal_healing     # convert, install over animation 59, play it on your soldier
python SOCOM2/bots/socom_bots.py anim play 59          # play animation 59 again
python SOCOM2/bots/socom_bots.py anim restore          # put the original animation back
python SOCOM2/bots/socom_bots.py anim ids              # animation id -> clip name for your soldier
python SOCOMCA/ca_anim.py convert seal_healing 30      # write a SOCOM II .rdr clip (zipper workbench viewer)
```

`anim <clip> [id] [fps]` replaces animation `id` (default 59, `seal_apply_restraints`, which multiplayer never
plays) with the CA clip. Only your game changes - other players see nothing yet. The animation holds its last frame
until you move. One clip is installed at a time (32 KB buffer at 0x000D8000); long clips are sampled at 8 fps or
less, which loses nothing - CA stores about 3 keys a second for `seal_healing`.

## CA clips

`F:/SOCOM Packs/SOCOMCA/anims/clips.json` lists every clip per bank with its name when known:

| Bank | Clips | |
|---|---|---|
| aux_gzan | 460 | general set; 219 named (SOCOM II names), 241 CA-only names not cracked yet |
| aux_szan | 7 | squad: `seal_healing` (16 s medic loop) and the buddy-carry set |
| aux_vzan | 2 | not named yet (vehicles?) |
| aux_mzan | 7 | not named yet |

## Formats

**CA** (`RUN/AUX_*ZAN.ZDB`, resource `bone_loader`, tag BONE): serialized vectors of 0x60-byte clip records:

- `+0x00` tracks vector (start, end, capacity) -> 16-byte tracks: bone hash (`ca_archive.BONES`), position keys,
  rotation keys, counts (low 16 = 3 x position keys, high 16 = 4 x rotation keys)
- `+0x20` flags (`0x2017....` / `0x2019....`), `+0x24` clip name hash (zlib CRC32), `+0x2C` duration (s)
- keys are int16 (x, y, z) and (x, y, z, w / 32767), spread evenly over the duration

**SOCOM II** (`RUN/MOTION_P.ZAR`, per map `MOTION_S.ZAR`; the workbench's `rdr-animation-parser.ts`):

- header `5`, duration, frame intervals, bone count, -1.0, 1.0, name offset, track offset
- 16-byte bone records, names, then a track per bone: `0x0C` positions + rotations animated, `0x1C` positions,
  `0x2C` rotations, `0x3C` static; samples = intervals + 1, evenly over the duration

**Conversion** (`SOCOMCA/ca_anim.py`): CA world rotations re-expressed under SOCOM II's parents (each rig runs a
bone's x axis along the limb). Spine, legs and head copy almost directly; the arms are rebuilt from CA's
collar -> shoulder -> elbow chain onto SOCOM II's spine-mounted biceps; `aimnodes` cancels the hips like SOCOM II's
own clips; rifle, toes and the `*_wgt` helpers keep `seal_stand`'s values. Always built on `seal_stand`'s layout
(still root at standing height, motion on the hips) - a template with a moving root stretched the arms when the
clip ended. Checked: every SOCOM II clip decodes and re-encodes to the same values; CA `seal_stand` converted lands
within ~15 units of SOCOM II's at the hips and feet.

## In the game

- Clips stay in RAM as loaded. The loader rewrites each clip's bone records: position data, rotation data,
  `(skeleton index << 24) | kind` (0x0C 1, 0x2C 3, 0x1C 5, 0x3C F), bone name. Skeleton indices are copied per
  bone name from the clip being replaced.
- Registry record per clip, found with `0x5EB560(*(soldier + 0x528), id)`: `+0` name, `+4` counts (low 16 = frame
  intervals), `+8` clip + 0x20, `+0x10` play length (s).
- Play: `0x58E160(soldier, id, stance, 0, t0 flags)`; the action handler `0x58D710` passes `t0 = 0x0D`, our
  wrapper at `0x000D3300` does the same. Your soldier's animation state (`+0x534`, low half) shows the id.
