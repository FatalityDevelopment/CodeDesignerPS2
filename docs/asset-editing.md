# Asset editing (maps, models, textures)

SOCOM II packages are edited through **asset sessions** in the bridge. The Model Editor and Map Editor webviews and
the `assets_*` MCP tools call the same `ASSET_*` commands, so an agent can do anything a person can in the editor. Both
connect to the same bridge (`localhost:8765`), so they can share a session: the editor polls the session revision
and redraws when an agent edits.

Format details (chunks, chains, GEO, CLUTTER.ZAR, palettes, alpha) are in [zipper-model-format.md](zipper-model-format.md).

## Layers

| Layer | Where |
|---|---|
| Package index, GEO scene, clutter, edits, save | `CodeDesigner.Ps2.FileFormats/Zipper/Package/` (`ZipperPackage`, `PackageEdits`, `PackageSave`, `ModelMesh`, `MeshTopology`), `Zipper/Geo/` (`GeoScene`, `SceneInstances`, `ClutterFile`, `VisualPlacement`), `Zipper/Model/ModelLayout.cs`, `Zipper/ZED/ZedTree.cs` |
| Sessions + commands | `CodeDesigner.Ps2.Bridge/Services/ZipperAssetService.cs` |
| MCP tools | `CodeDesigner.AI/Tools/Assets/AssetTools.cs` |
| Model Editor | `vscode/codedesigner/src/webview/model-editor/` (`editor-viewport.ts` 3D view and tools, `uv-editor.ts`, `mesh-data.ts`), `src/providers/modelEditor/` (command `codedesigner.openModelEditor`; the workbench's **Open in Model Editor** on a `*_MDL.ZED` record) |
| Map Editor | `vscode/codedesigner/src/webview/map-editor/` (`map-viewport.ts` instanced map view, points, gizmo, place), the same provider with `MAP_EDITOR_PAGE` (command `codedesigner.openMapEditor`, also on `.zdb` in the explorer) |
| Shared 3D core | `vscode/codedesigner/src/webview/zipper-core/viewport-core.ts` (renderer, cameras, orbit / WASD fly, gizmo, materials, grid) |
| Tests | `CodeDesigner.AI.Tests/ZipperPackageTests.cs` (retail packages from `G:\RUN` or `ZIPPER_RUN_DIR`) |

## Commands and tools

| Bridge command | MCP tool | What |
|---|---|---|
| `ASSET_OPEN` (InputPath) | `assets_open` | Open a `.ZDB`, a `.CDB` memory-card pack (what pack export writes), or an unpacked folder / loose `.ZED`, or join the session already open on it |
| `ASSET_SESSIONS` / `ASSET_STATUS` | `assets_sessions` | Sessions, revision, changed archives, undo/redo labels |
| `ASSET_LIST_MODELS` | `assets_list_models` | Models by kind: prop, weapon, character, world, sky, library |
| `ASSET_DESCRIBE_MODEL` | `assets_describe_model` | Visuals, chunks, textures (palette id, transparency), GEO definition |
| `ASSET_TEXTURE` | `assets_texture` | Texture info + PNG (TEX0.CBP palette, real alpha) |
| `ASSET_MODEL_MESH` | `assets_model_mesh` | The editable mesh in model space: per chunk `geometryOffset`, visual, texture, placement; optionally base64 positions / uvs / normals / colours / triangles |
| `ASSET_MAP_SCENE` | `assets_map_scene` | What the map places: worldmodel, references (`geoIndex`), clutter (`clutterIndex`) |
| `ASSET_MAP_POINTS` | `assets_map_points` | AIMAPS.MPS starts / waypoints with world X / Z |
| `ASSET_LIST_ANIMATIONS` | `assets_list_animations` | Character clips: the package's `MOTION_S.ZAR` (editable) and the shared `RUN/MOTION_P.ZAR` (read-only) |
| `ASSET_ANIMATION` | `assets_animation` | One clip's tracks per bone: local positions (units) and quaternions per sample |
| `ASSET_WEAPONS` | `assets_weapons` | Weapons in the package (zweapon.rdr): model, FireAnimName, FireWait |
| `ASSET_WEAPON_FX` | `assets_weapon_fx` | A weapon's record and its CZANIM effect tree (sub-effects, models, decoded tracks) |
| `ASSET_EFFECTS` / `ASSET_EFFECT` | `assets_effects` / `assets_effect` | Every ZANIM effect / one fully decoded (names, objects, tracks, instructions) |
| `ASSET_EDIT` (AssetJson `{op,...}`) | `assets_edit` | One undoable edit (below) |
| `ASSET_UNDO` / `ASSET_REDO` | `assets_undo` | Undo / redo |
| `ASSET_SAVE` | `assets_save` | `zdb`: write the package (over the source keeps a one-time `.bak`); `pack`: memory-card `<NAME>.CDB` (+ install into a PCSX2 folder card) |
| `ASSET_ENTRY_BYTES` | (editor) | Current bytes of a root archive, for drawing |
| `ASSET_CLOSE` | `assets_close` | Drop a session and its unsaved edits |

Edit ops (`assets_edit` describes the JSON):

- vertices:
  - `setVertices`: position, uv, colour and/or normal per vertex. Takes one chunk, or `chunks: [...]` as one undo
    step.
  - `transformVertices`: `translate` / `rotate` / `scale` about a `pivot`, or a `matrix`, on whole chunks or listed
    vertices.
  - Both take model space as `ASSET_MODEL_MESH` returns it. Edits go back through the chunk's GEO placement and unit
    to the int16 records; a chunk whose vertices no longer fit int16 is rescaled.
- geometry:
  - `deleteFaces`: unused vertices and emptied chunks go too.
  - `duplicateFaces`: copies with an optional transform.
  - `addMesh`: into a visual and texture, from OBJ text (Y up), a primitive (`cube`, `plane`) or arrays; a new texture
    gets its own material section.
  - These answer `{result, newChunks}`.

- `batch {label?, ops: [...]}`: several ops as one undo step; a failing op rolls back the ones before it. Answers
  each op's result.
- animations (package clips; shared ones are copied in with `addAnimation {from}`):
  - `addAnimation {name, from?, fromSource?, frames?, duration?, after?}`: a new clip copied from any clip
    (default `seal_stand`), optionally resampled / retimed;
  - `deleteAnimation {name}`, `renameAnimation {name, newName}`;
  - `setAnimation {name, frames?, duration?, bones?: [{name, positions?, rotations?}]}`: replace tracks (1 key =
    static, or one per sample);
  - `setAnimationKey {name, bone, frame | allFrames, rotation?, position?}`: one key (a static track becomes animated);
- clutter: `moveClutter`, `addClutter` (answers `newClutterIndex`), `deleteClutter`;
- `movePoint {name, position}`: an AIMAPS point to a new X / Z (snapped to its grid cell);
- scene references: `moveInstance`, `duplicateInstance`, `deleteInstance`;
- `replaceTexture`;
- `retexture` (point a model's sections at another `.tif`);
- `tintVertexColors` (baked lighting).

How placements are given:
- Either a `matrix` (column-major), or `position` with an optional `yaw` and `scale`.
- `moveInstance` takes world space; the bridge converts it to the node's local `nparams`.

How some ops work:
- Clutter adds copy an existing placement of the same model as the record template.
- `replaceTexture` keeps the palette id. It regenerates the palette only when no other texture shares it.

Every edit replaces whole root archives in the session, and undo restores their bytes.

## The Model Editor

- **Menu bar:**
  - File: open, save a ZDB copy, export a pack, export OBJ.
  - Edit: undo / redo.
  - Select: all / none / invert, and the modes.
  - View: frame, the six views, orthographic, hide / isolate / show, back faces, panels.
  - Mesh: mirror X/Y/Z, flip normals, snap vertices to the grid, centre on the origin, reset colours.
  - Help: the controls and shortcuts.
- **Header toolbar:**
  - selection mode: Object / Visual / Chunk / Vertex / Face;
  - shading: Textured / Solid / Wire / X-ray;
  - toggles for baked lighting, grid and normals;
  - snapping, with move and rotate steps;
  - view buttons.
- **Tool shelf:** Select, Move, Rotate, Scale (a gizmo with live preview) and Vertex paint (a brush over the baked
  lighting).
- **Viewport controls:**
  - orbit with the middle mouse button or Alt + left drag, pan with right drag, zoom with the wheel;
  - click to select (Shift adds, Ctrl removes), drag to box-select;
  - an orientation gizmo in the corner.
- **Outliner:** the model's visuals and chunks, with show / hide toggles and click-to-select.
- **Properties tabs:**
  - Item: selection counts, plus location / rotate / scale fields with preview and Apply.
  - Material: chunk material (blend mode, texture, colours, units) and retexture.
  - Texture: preview, replace from PNG, export PNG.
  - Paint: brush colour, intensity, radius and strength; fill selection; reset to neutral.
  - Model: GEO definition, missing textures, the session id for agents.
- **Bottom panel:**
  - UV editor: the selected chunks' UVs over their texture; click / box select, drag to move, flip, rotate 90°,
    pixel snap.
  - Info: the edit log.
- **Status bar:** the current hint or last result, the element under the cursor, and the selection counts.
- **Geometry:**
  - The Add menu and the Item tab's Geometry card add a plane, a cube or an OBJ into the chosen visual and texture,
    at the selection centre.
  - Shift+D duplicates the selection and X / Delete deletes it.
  - New geometry comes back selected with the Move tool.
- **Keys:** Q / G / R / S / P tools, 1-5 modes, A / Alt+A / Ctrl+I select, F / Home frame, H / Shift+H / Alt+H hide,
  Z cycles shading, Numpad 1 / 3 / 7 / 5 views, Ctrl+Z / Ctrl+Y undo / redo, Ctrl+S save copy, Ctrl+O open.
- **Lighting:** the view is unlit by default because much retail baked lighting is near black. The paint tool turns
  baked lighting on.

Every committed gizmo move, numeric transform, paint stroke, UV drag and Mesh-menu operation is one `ASSET_EDIT`. The
editor then reloads the mesh from the bridge, so what you see is what was written.

## Other games: SOCOM 3 / Combined Assault (read-only)

`ASSET_OPEN` on a CA location archive (`RUN/<hash>.ZAR`, e.g. `9EA6A33E.ZAR` = COMPOUND) or a ZDB v6 file opens a
read-only CA session (`CaPackage`, `Zipper/Ca/`); its status carries `game` and `readOnly`, and the editors show
"SOCOM 3 / Combined Assault · read-only". The same commands answer: `ASSET_LIST_MODELS`, `ASSET_DESCRIBE_MODEL`,
`ASSET_MODEL_MESH`, `ASSET_TEXTURE`, `ASSET_MAP_SCENE` (the island world); edits, saves, animations and effects are
refused for now.

- **Archives:** the outer .ZAR is the ZDB type-2 layout; its entries are ZDB v6 files (`ZdbArchive` type 1) whose
  entries carry a resource tag (`ZdbEntry.Tag`: ISLE, ASST, CHAR, ZANM, BONE, DEP_ ...).
- **Libraries** (`CaLibrary`): ISLE (level data, version 1) and ASST (clib characters, vehicle / prop / sky .rdr) are
  serialized object graphs with header vectors {models, objects, textures, palettes}. Model nodes: +0x04 hash, +0x10
  local matrix, +0x50 children, +0x98 mesh objects. Static mesh objects (0x00841398): DMA chains of material (6),
  colour (3) and geometry (2) tags; the geometry is SOCOM II's static chunk format in 1/16 units. The tag kind is the
  low byte of the tag's upper half (materials are often 0x106); a material's texture is the object's record whose TEX0
  it sets, and a chain without a material keeps the previous one (GS state).
- **Textures** live in the island or in a shared library (a record with no pixels names one elsewhere); 4-bit palettes
  sit in CSM1 slots 0-7 / 16-23; the palette format is TEX0.CPSM.
- **Placement:** every location .ZAR has several island pieces (`<loc>_i1` ... `_iN`), all drawn; screen overlays
  stored as islands (OCN_2, RoundComplete, GameComplete, UI folders) are `ui`. A node's objects are separate pieces
  (terrain + road overlay, trunk + canopy) and all are drawn, except an LOD copy (fewer triangles, same textures,
  bounds within a quarter). The map scene instances one source mesh per object (`obj_<offset>`).
- **World:** the ISLE model with the most nodes (COMPOUND `com_i1`: 1,114 nodes, 740 mesh instances, 351,058
  triangles); the other ISLE models are prop prototypes. `Ca_CompoundWorldDecodes` checks these numbers and that all
  134 island textures decode (PSMT8 / PSMT4 with CSM1 palettes, PSMCT16 / 32, linear pixels).
- **Characters** (`clib` models): skeleton = the model's node tree (bone names: `CaBones`), mesh = every mesh object on
  its nodes (`CharacterMesh.DecodeCa`: sections by scan, 2-element batch headers, CA bone i = skeleton bone i + 1).
- **Weapons**: `RUN/GLOBAL.ZDB` › `global_assets.rdr`'s nested library (88), loaded with every location archive; names
  cracked from CRC32 (`CaNames`); positions at the chunk's scale (not 1/16). Props / vehicles / weapons leave out LOD
  copies and the hull (`LodSkip`, the auto_skip rule).
- **UVs**: CA stores V top-down - statics 0..1, characters negated (-1..0); both are flipped to the up-pointing V.
- **Clips** (`CaAnimations`): `BONE` banks of the package plus `RUN/AUX_*ZAN.ZDB` (476 clips, 290 named by
  `CaClipNames`); bone-local int16 keys (1/256 units, quaternions /32767, w last) spread over the clip, resampled at
  30 fps. They play natively on CA characters in the Animation Editor (the `rifle` track mounts the weapon).
- **Not yet:** CA effects (`ZANM`), AI maps, placement of prop prototypes, editing.
- **Black holes in the terrain are intended:** CA leaves out ground a player never sees (mesa / cliff tops; Oasis:
  rims at y 190-300 over ground at 10-130). They render black in the game's own views too; the editor does not fill them. Also
  undrawn: chain tag kind 1 (float VU ribbons: the telephone wires between poles).

## Converting between games

The Model Editor's **Export to SOCOM II… / Export to SOCOM 3 / CA…** button (and `ASSET_EXPORT` / `assets_export`)
converts the selected character, weapon, vehicle or prop, optionally with animations, then opens the result.

- **CA -> SOCOM II** (`Conversion/CaToSocom2`): a C# port of the converters confirmed in game (Mods/SOCOMCA
  `ca2s2`, `ca2s2static`, `s2mesh`, `s2static`, `ca_anim.retarget`); its packs are byte-identical to theirs
  (`Convert_CaToSocom2MatchesTheInGameConverter`). Templates come from SOCOM II's `RUN/` (MP52 alg_UN01, M53, M51
  m4Acarbine_203, MP61 humvee / sandbags, MOTION_P seal_stand), found on any drive or passed as `socom2Run`. Output:
  `Documents/CodeDesigner/Exports/<pack>/<PACK>.CDB` + ZEDs (+ `MOTION_S.ZAR` with retargeted clips).
- **SOCOM II -> CA** (`Conversion/Socom2ToCa`): a ZDB v6 `<MODEL>.ZDB` with an `ASST` library written by
  `CaLibraryWriter` (skinned: the SOCOM II skeleton as the clib node tree, vertex blocks as CA batches, draw blocks
  copied; static: one node per visual, chains of TEX0 material / colours / geometry), a `BONE` clip bank and a
  `NAMS` name list (CodeDesigner's: the names behind the hashes). The mesh round-trips exactly
  (`Convert_Socom2ToCaRoundTripsTheMesh`). Whether CA itself loads it is not known (no CA pack loader yet).
- Mesh and pixel bytes are shared by both games, so both directions copy them as they are; CA stores V top-down,
  which the viewers flip (see the CA section).

## Packs

`<NAME>.CDB` memory-card packs (the editors' pack export, `build_charpack.py`, `rip_ca.py`) are the ZDB type-2
entry table with a 0x110 header and 0x70-byte entries: `ZdbArchive` reads them as archive type 3 and writes them back
with `PackageSave.BuildCdb`, so a pack opens in the Viewer, the editors and `assets_open`, and saving keeps it a pack
(same name; the game mounts `mc0:/BASCUS-97275<NAME>/<NAME>.CDB`). The loose `CLIB_*.ZED` beside a pack open as a
folder package. `Packs_CdbAndLooseZedOpenAsPackages` checks both on real packs (byte-identical re-encode).

## Characters

`MESH_<name>` records in `CLIB_MDL.ZED` are skinned meshes (`CharacterMesh`, a port of `Mods/SOCOMCA/charmesh.py`
/ `s2mesh.py`). The DMA chain at `<name>_START` lists per material section (`cnt` → .tif name) the vertex batches
(a bone matrix ref by matrix_id, then the vertex block) and draw blocks. Each vertex is the weighted sum of its parts:
a bone-local position (1/4096 units) through that bone's bind world matrix. The skeleton is the GEO model `<name>`
(in `CLIB_GEO.ZED`): scene nodes with `nparams` and a `matrix_id` (`CharacterSkeleton`, bone 0 = the model node).

`ASSET_MODEL_MESH` returns one chunk per draw group in bind pose, plus `skeleton` and the skin per vertex
(`skinInfluences` slots of bone index, weight, bone-local position / normal), which is what animation playback
needs. Checked: every character on M51 / MP71 / MP6 / MP53 decodes (about 19 units tall, feet at 0), and the seal's
groups, triangles and bounds match the Python tools (`Character_SkinnedMeshMatchesPythonDecoder`).

## Animations

Character clips are records of `MOTION_S.ZAR` (each map package, 59 on M51) and `RUN/MOTION_P.ZAR` (334 shared),
version-5 `.rdr` (`MotionClip` in `Zipper/Anim/`, the format of `Mods/SOCOM2/ANIMATIONS.md`): per bone a track of
int16 local positions (1/256 units) and quaternions (/32767), animated (one key per sample, samples = frames + 1 over
the duration) or static. Bones match the character skeleton by name (stored names carry leading dashes); clips add
weapon mounts (`rifle`, `pistol`, `launcher`, `weapon`) under the hand. Every retail clip re-encodes byte for byte
(`Animation_EveryClipRoundTripsByteForByte`); `PackageAnimations` adds / renames / replaces / deletes package clips.

The **Animation Editor** (`codedesigner.openAnimationEditor`, `src/webview/animation-editor/`) lists every clip
(package first, shared read-only, filter and prefix chips) and plays it on a character from the package: the
skeleton is posed from the clip (bind locals for bones it lacks) and the mesh skinned on the CPU each frame. Timeline:
play / pause (Space), step (, .), scrub, speed, loop. Edit: New (from `seal_stand`), Duplicate / Copy into package,
Rename, Delete, duration and frame count (resample); click a joint and drag the Rotate (R) / Move (G) gizmo, or type
position / rotation, to key that bone at the current frame; "Hold for all frames" makes it static. Each change is one
undoable `ASSET_EDIT`. A new clip is only played by the game once something references its name.

**Weapon and firing preview.** The Weapon picker mounts any package weapon on the clip's `rifle` bone (the high LOD
and plain scope; the gun's +X barrel lines up with the bone as stored). Fire (Enter) / Auto (at the weapon's
FireWait) runs the weapon's effect scripts: `zweapon.rdr` FireAnimName (e.g. `muzzle_m4`) → CALL_ANIMATION
`shell_eject`, `flash_fire_hider`, `shell_smoke_med`, played by `zanim-player.ts` the way the game's interpreter does
(tracks in parallel; IF / ELSEIF / ELSE with RANDOM_WEIGHT; WAIT; LOOP; STOP_SEQUENCE; OBJECT_ACTIVE / ROTATE /
TRANSLATE_STATE; OBJECT_MOTION_FROM_TO scale curves; LIGHT; CALL_ANIMATION). The M4's flash: placed on the owner (the
gun's `firepoint`) every frame, a uniform pick of six rolls, a growth from 0.1 to 0.9 / 1.2 / 1.3 over 0.05 s, one
more frame, then hidden; the light fades over 0.07 s. The shell still uses a simple throw (its OBJECT_MOTION / VALVE
physics are not decoded); particles (smoke) and sounds are not drawn. The page lists the decoded scripts and what
the preview skipped.

### ZANIM scripts

`ZanimFile` (Zipper/Anim) reads CZANIM.ZAR. The game's opcode table (EE RAM 0x84F2A0 in a SOCOM II save state:
20-byte records {name, opcode, init handler, exec handler, 0}) names all 0x59 opcodes (`ZanimFile.OpNames`).
Argument layouts read from the handlers (byte offsets from the instruction; +0 opcode, +2 words << 4 | flags):

| Opcode | Layout |
|---|---|
| IF / ELSEIF (0x02 / 0x03) | +4 terms; its declared size covers the condition instructions after it; body until ELSEIF / ELSE / ENDIF |
| RANDOM_WEIGHT (0x0A) | +4 float weight: true when rand() <= weight |
| LOOP (0x0E) | +4 flags (1 count, 2 seconds), +8 count (-1 forever) or seconds |
| WAIT (0x0F) | +4 flags (8 seconds, 0x10 frames, 0x20 random extra at +0xC; 2 / 4 pick the clock), +8 value |
| OBJECT_ACTIVE_STATE (0x11) | +4 = 1 on / 0 off, +6 object |
| OBJECT_TRANSLATE / ROTATE_STATE (0x12 / 0x13) | +4 flags (1 set, 2 add, 4 copy reference locally), +6 object, +7 reference, +8 vec3 (Euler radians) |
| OBJECT_MOTION_FROM_TO (0x16) | +4 flags (0x200 scale), +6 object, +0x30 offset of {start, final, rate/s} vec3s, +0x34 duration |
| LIGHT (0x20) | +6 object, +0x20 colour (0-255), +0x2C range, +0x3C time (tentative) |
| CALL_ANIMATION (0x2D) | +4 flags (0x200 wait for it), +6 object, +7 animation name index |

Objects: 0 none, 0xF9 the effect's own object, 0xFA its owner, otherwise an index into `Node_Ref_List` (8-byte
refs: byte 1 name index, byte 2 flags, byte 3 parent). Track headers: +0 target name, +4 flags (bit 0 enabled,
byte 5 run state), +0xC byte size. `WeaponFx_FireAnimationsResolveToEffectModels` checks the walk ends exactly at
every track end in all 288 M51 scripts and that every opcode below 0x200 is named.

## Coordinates

Model space is Y up, the same as the editors' display: sky domes sit above the map and cars stand on their wheels
when the data is drawn unchanged (as the workbench always has). Positions, matrices and OBJ import (`yUp`, the
default) use it as is; `yUp: false` flips a Y-down OBJ.

## The Map Editor

The composed map of a package: the worldmodel, GEO props, clutter and sky as one instanced mesh per model chunk, and
the AIMAPS points as pillars standing on the ground.
- **Layers** (outliner and View menu): World, Props, Clutter, Sky (hidden at first: the dome encloses the map),
  Points. Props and clutter are grouped by model; each group and model can be hidden, and clicking a model selects
  all its instances.
- **Tools:** V select (click, Shift adds, Ctrl removes, drag for a box), G move, R rotate (yaw), T scale, P place.
  W A S D fly with Q / E down / up (Shift faster) while the viewport has focus; the speed is in the header.
- **Item tab:** model, GEO path / clutter index and source; position, yaw and scale fields (game units); Duplicate
  (Shift+D), Delete (X), Drop to ground (End), Open model (also double-click), which opens the Model Editor on the
  same session.
- **Place tab:** pick a prop or library model, then click the ground with the Place tool to `addClutter` it, with
  a yaw (or random yaw) and scale.
- **Points:** select one in the outliner or viewport and move it with the gizmo (ground plane) or the X / Z
  fields.
- **Edits:** a gizmo commit on several instances is one `batch` of `moveInstance` / `moveClutter`; deletes go
  highest index first; GEO duplicates come back selected with the Move tool. The world model and sky do not move.
- Undo / redo, save a ZDB copy and export a pack work as in the Model Editor; edits by an agent or the Model Editor
  on the same session redraw within two seconds.

## Re-laying models

`ModelLayout` parses a chain-drawn payload into visuals → material sections (name field, material) → (colour,
geometry) chunks and writes it back.
- **Layout:** per visual, its geometry blocks, then a material and a name per section, then its colours and chain.
  This is the `Mods/SOCOMCA/s2static.py` layout, confirmed in game.
- **Kept as-is:** blocks shared between visuals stay shared, and the tag flags, chain header words and empty
  (set-up only) sections are kept.
- **Checked on retail:**
  - All 728 chain models of M51 / MP71 / MP6 / MP53 / M62 are fully covered by their chains, so re-laying loses
    nothing.
  - Every retail geometry chunk re-encodes byte for byte through `ReadChunk` / `WriteChunk`: the header's second
    index word is 1, and per-triangle flag bytes of 1 and 3 are kept.
  - A full parse → write keeps the decoded mesh identical.
- **New geometry:**
  - Split into chunks of at most 72 vertices and 48 triangles.
  - Quantised in the visual's own unit: around a chunk origin in 1/16 units for props and the world, or normalised
    by the chunk scale for weapons.
  - Copies the GIF tags and VIF codes of the visual's existing chunks.

## Not yet

- **Mesh import (glTF/OBJ):** not done.
- **Topology edit limits:** new or edited geometry stays in its visual's GEO placement. New visuals or GEO nodes
  can't be added yet, and deleting every face of a visual is refused; hide it instead.
- **Line-strip models:** not decoded (MP6's power lines).
- **Character edits:** skinned meshes are view-only (vertex and topology ops refuse them). Attachments (hats,
  gear, eyes in `FLIB_MDL`) are separate static models and are not yet placed on the body's bones.
- **Map edits in packs:** a pack holding edited *map* archives is written the way `build_charpack.py` writes
  character packs (confirmed in game with `loadpack`). Whether the game prefers it over the disc copy of that map is
  **unverified**. A patched ZDB copy (`mode: zdb`, `outputPath`) is the direct route.

## Offline harness

`vscode/codedesigner/scripts/zipper-harness` runs the webviews in a browser without VS Code or the bridge process.
It serves `dist/webview` and answers webview requests with the bridge's own handlers (`/api` for the workbench's
`ZIPPER_*` reads, `POST /bridge` for any registered Zipper command, `ASSET_*` included). Build the bridge (Debug),
then:

```
dotnet build vscode/codedesigner/scripts/zipper-harness
dotnet vscode/codedesigner/scripts/zipper-harness/bin/Debug/net8.0/harness.dll 18765
```

The pages to open:
- **Workbench:** `http://localhost:18765/?path=G:/RUN/M51.ZDB`
- **Model Editor:** `http://localhost:18765/?page=model-editor&path=G:/RUN/M51.ZDB`
- **Map Editor:** `http://localhost:18765/?page=map-editor&path=G:/RUN/M51.ZDB`
- **Animation Editor:** `http://localhost:18765/?page=animation-editor&path=G:/RUN/M51.ZDB` (`window.__answer`
  answers its name prompts)

The harness loads the bridge DLL from `CodeDesigner.Ps2.Bridge/bin/Debug`: build the bridge before the harness.

Synthetic pointer drags on the gizmo need `button: -1` on the moves (as browsers send), or TransformControls ignores
them.

The page exposes test helpers:
- `__open(archive, entry)` and `__fly(point)`;
- `__capture()`, a fingerprint of the composed model and its materials, for before/after comparisons.

Building the bridge or the test project also builds `CodeDesigner.Ps2.Multiplayer`, whose deploy step copies into
`%LOCALAPPDATA%\CodeDesigner\Multiplayer` and fails while the server runs. Point `LOCALAPPDATA` at a scratch folder
for those builds.
