# SOCOM II — native line / text drawing and the camera

Ported from Nknights23's SOCOM 1 thread
(unknowncheats 689014, "SOCOM U.S NAVY SEALs Reversal, Structs & Offsets").
All addresses are **SOCOM II r0004 (online), live EE RAM**; labels are in the
`socom2-render` set. SOCOM 1 addresses in the thread come from a patched
executable — they do not match the SCUS-97134 disc ELF, so everything below
was located in SOCOM II by behaviour, not by byte-porting.

## Draw functions

| SOCOM II | SOCOM 1 | Signature | Notes |
|---|---|---|---|
| `0x0037D570` Draw2DLine | `0x316FB0` | `(f32 start[4], f32 end[4], f32 colA[4], f32 colB[4])` | Pixel space 0..640 × 0..448 (`[0x4A7D04]`/`[0x4A7D08]`), drawn over everything |
| `0x00358B80` RenderLineWorld | `0x249590` | same | World space, depth-tested; = Clip3DLine + Draw3DLine |
| `0x0037D8C0` Draw3DLine | `0x317220` | same | No clipping — call RenderLineWorld instead |
| `0x0037D250` Clip3DLine | `0x316C90` | `(clip = camera+0x400, a, b) -> bool` | |
| `0x00217550` DrawAutoReticle | `0x3F0130` | | Reference for the game's own W2S + Draw2DLine use |
| `0x001BFEC0` sceVu0CopyMatrix | `0x1518A8` | `(dst, src)` | |
| `0x00325920` TransformPoints4 | (inlined) | `(out, mtx, in, count)` | |

Colours are RGBA floats 0..1. All vectors are 16-byte aligned `float[4]`.

## Text (C2DString, vtable `0x433690`, sizeof 0x98)

| Address | Function |
|---|---|
| `0x00381750` | ctor `(self)` |
| `0x00380AD0` | SetString `(self, text, bLocalize)` |
| `0x00381110` | SetText `(self, text)` → display buffer `+0x2C` |
| `0x003812A0` | Draw (vtable slot; uses 2D camera `[0x4A7D28]`) |
| `0x00380EA0` | MakePacket (glyphs) |

Other 2D classes: C2DLine `0x433540`, C2DTriFan `0x4335B0`, C2DPoly `0x433620`,
C2DBitmap `0x433720`, C2D base `0x433790`.

Not yet located: font object pointer and the x/y setters (SOCOM 1 `C2DString_Load`
took `font, x, y`), and `C2DMessage_AddMessage` (top-centre message queue).

## Camera

`zdb::CWorld::m_world = [0x0045F750]` → `+0x128` = `CZCamera*` (SOCOM 1: `+0xC0`).

CZCamera matches SOCOM 1 up to `+0x1E0`; from the matrix set onward everything is
**+0x30**:

| Field | SOCOM 1 | SOCOM II |
|---|---|---|
| `tag_CAMERA_PARAMS` (hfov, vfov, near, mid, far, fog…) | 0x0C0 | 0x0C0 |
| `m_mtxSet` (WorldToView, WorldToClip, ViewToClip, ViewToScreen) | 0x2C0 | 0x2F0 |
| `m_scratch` → `+0x20` = WorldToScreen matrix | 0x3C0 | 0x3F0 |
| `m_ClipSet` (ZCAM_CLIP_DATA) | 0x3D0 | 0x400 |
| `m_screenAspect / Center / Offset` | 0x440/48/50 | 0x470/78/80 |
| `m_screen` RECT (1728, 1824, 2368, 2272) | 0x458 | 0x488 (verified) |

WorldToScreen (same as the game):

```c
u32 cam = *(u32*)(*(u32*)0x45F750 + 0x128);
float m[16]; sceVu0CopyMatrix(m, *(u32*)(cam + 0x3F0) + 0x20);
Vec4 g = m * {x, y, z, 1};          // row-vector, translation in row 3
if (g.w <= 0.001f) offscreen;
px = g.x / g.w - 2048 + 320;        // == g.x/g.w - m_screen.left (1728)
py = g.y / g.w - 2048 + 224;        // == g.y/g.w - m_screen.top  (1824)
```

The result feeds straight into Draw2DLine.

## Player globals (r0004)

| | r0001 | r0004 |
|---|---|---|
| CAppCamera* | 0x415FF0 | 0x4429B0 |
| CZSealBody* local | 0x440C38 | 0x44D648 |
| ZArray<CZSealBody*> seals | 0x4362E0 | 0x442CE8 |

CZSealBody in SOCOM II is **not** a uniform shift of SOCOM 1: team `+4`
(0xC4→0xC8), weapons/ammo/mags `+0xB8` (0x60C→0x6C4), health `+0x174`
(0xED0→0x1044).

Verified in an offline match (2026-10-13):

| Field | SOCOM 1 | SOCOM II |
|---|---|---|
| name `char*` | 0x14 | 0x14 |
| origin Vec3 | 0x1C | 0x1C |
| CNode* (world matrix at node+0x00) | 0x28 | 0x28 |
| quat / matrix | 0x50 / 0x80 | 0x50 / 0x80 |
| pSealCtrl | 0xC0 | 0xC0 |
| (new dword) | — | 0xC4 |
| team | 0xC4 | 0xC8 |
| maxTargetRange / maxTargets / targetCount / CTarget* | 0xC8..0xD4 | 0xCC..0xD8 |
| skeleton `CZBodyPart*[32]` | 0x26C ([33]) | 0x2E8 |
| health (ratio, 1.0 = full) | 0xED0 | 0x1044 |

Bone order matches SOCOM 1 minus `grenade` (0 skel_root, 1 aimnodes, 2 lfoot,
3 rfoot, 4 lhand, 5 spinelo, 6 rhand, 7 hips, 8 head, 9 neck, 10 spinehi, 11 lthigh,
12 rthigh, 13 rcalf, 14 rbicep, 15 rforearm, 16 lbicep, 17 lforearm, 18 lscap,
19 rscap, 20/21 shoulder_wgt, 22 lcalf, 23 ltoe, 24 rtoe, 25 weapon, 26 rifle,
27 pistol, 28-31 eyes/lids).

SOCOM II CZBodyPart: `+0x00 origin`, `+0x0C CNode*` (name at node+0x90; node matrix
is local only), `+0x10 origin copy`, `+0x1C parent*`, `+0x20 quat`, `+0x30 quat copy`.
World position = walk parents (`v = rotate(parent.quat, v) + parent.origin`), then
transform by the seal's CNode matrix — checked offline: head ≈ origin + 10 up,
feet at ground.

Seal ZArray iterator: `{next, prev, data}`, list ends when `next == 0x00442CEC`.
Turrets have team `0x48000000` and no skeleton.

## esp.cds — working (2026-10-13)

`esp.cds` / `esp.codes.txt`: snaplines, per-bone skeletons, 2D boxes (min/max of all
projected bones) and name + distance (two lines) for every on-screen enemy. Verified in an
offline match at 60 fps. Control it with `python SOCOM2/esp.py install|on|off|mode|team|color|status`
(also registered in `Mods/debug-commands.json` under "SOCOM II · ESP").

- Hook: `CHUD::Draw`'s last call `jal 0x00215C10` (the HUD text pass) at `0x00216DA8` →
  code at `0x000ED000`, which calls the text pass itself and then draws. The local seal is
  `CHUD::Draw`'s `$s3`. (The first hook site, `0x002167C0` / DrawAutoReticle, is restored.)
- Mailbox `0x000E0000`: bit0 snaplines, bit1 skeletons, bit2 boxes, bit3 names;
  `+4` teammates; `+8` lines last frame (cap 300); `+C` enemies labelled last frame (max 8); `+10` colour.
- Distance = `sqrt(dx²+dy²+dz²) / 10` metres (same as the game's `RANGE(m)` readout).
  `sqrt.s` must be emitted as `hexcode $46010044` — the EE takes the source in `ft`, the
  assembler emits the MIPS form.

### Text: borrow the HUD's string pool

Drawing our own C2DString (ctor `0x381750`, Load `0x380AA0`, draw `0x3812A0`) produces
garbage glyphs — the glyph shapes are right but the palette/texture state isn't, because the
game draws `font_text_01` text from inside the HUD container (`C2D` at e.g. `0x01C95E0C`)
that sets that state. The fix: the HUD object `[0x00435818]` (`new(0xACD0)`) holds a pool of
28 idle C2DStrings at `+0x878 + i*0x98` (text "00", hidden) that are children of that
container. The ESP borrows the last 16 (`+0xF98 + i*0x98`), two per enemy: the name
(`SetString(slot, name, 0)`) and `[8.7m]` (`sprintf` = `0x001988D0`) in the next slot, 11 px
lower — `\n` does **not** start a new line in this font code (the second line overlaps the
first). Position floats `+0x90/+0x94`, visible = bit 6 of byte `+0x14`; all 16 are hidden
again at the start of every frame. A vtable check (`0x433690`) guards the pool.

Rules learned the hard way:
- **No VU0 calls from the hook.** Calling `sceVu0CopyMatrix` / `TransformPoints4` from the
  HUD hook crashed the PCSX2 debug build within seconds every time; the FPU-only rewrite
  (reads the W2S matrix at `[cam+0x3F0]+0x20` in place) is stable.
- **Pause the VM before writing code over PINE** and put new code at a fresh address, then
  re-point the hook — rewriting code the recompiler has already compiled leaves stale blocks.

## Earlier esp.cds attempt

`esp.cds` (cave `0x000E0000`, hook at `0x002167C0`) crashed PCSX2 as soon as mode 1
was enabled; the pass-through (mode 0) ran fine at 60 fps. Untested suspects, in order:
1. Too many lines per frame — ~33 enemies × 31 bones ≈ 1000 RenderLineWorld calls
   probably overflows the scratchpad GIF packet buffer (`zSysSprGetPacket`).
2. Calling RenderLineWorld from the HUD (2D) pass rather than the 3D pass.
3. `lqc2` in TransformPoints4 on a non-16-byte-aligned matrix/vector.
Next attempt should draw one line for one enemy first, from save state slot 9.

### Live test log (2026-10-13)

- Fixed Draw2DLine from the HUD hook: works, stable, screenshots fine.
- Per-enemy snaplines (seal walk + W2S + Draw2DLine): **render correctly** (lines
  from screen bottom-centre to every visible enemy), then PCSX2 (debug build) died a
  few seconds later each time. Crash reports (`Documents\PCSX2\crash-*.txt`) fault inside
  EE JIT code with the local seal pointer on the stack.
- Common factor in every crash: the hook called the game's VU0 helpers
  (`sceVu0CopyMatrix 0x1BFEC0`, `TransformPoints4 0x325920`), and each crash followed a
  PINE screenshot request. Next test uses an FPU-only W2S that reads the matrix at
  `[cam+0x3F0]+0x20` directly, with no screenshots.
- Always pause the VM before writing code over PINE: writes from the PINE thread race
  the recompiler (a re-pointed `jal` kept executing the old target until a restart).
