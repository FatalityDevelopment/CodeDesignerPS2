# Zipper model and texture format (SOCOM II map editor)

How the Zipper Workbench map editor decodes `*_MDL.ZED`, `*_TXR.ZED`, `*_PAL.ZED` and `*_GEO.ZED` entries, and
why. Every layout here was confirmed by the SOCOM II pack builders in `Mods/SOCOMCA` (`s2static.py`, `ca2s2.py`,
`ca2s2static.py`): packs written this way load and render in the game.

Decoders: `vscode/codedesigner/src/webview/zipper-workbench/zed-model-parser.ts`, `zed-texture-parser.ts`,
`zed-geo-parser.ts`; renderer: `zed-model-renderer.ts`. The C# ports used by the bridge and MCP tools are
`CodeDesigner.Ps2.FileFormats/Zipper/Model/ZedModelFile.cs` (`ParseVisualChains`, `Parse(bytes, chainOffsets)`,
`CollectChainOffsets`, `PositionUnitForFlags`) and `Zipper/Texture/ZedTextureFile.cs` (`ParseGsRegisters`,
`Header.Cbp`, `Header.ResolvePaletteId()`), covered by `CodeDesigner.AI.Tests/ZedChainAndGsTests.cs`.

## Model node (`<model>` + children `N<node>_<visual>`)

A model node's payload holds geometry chunks, name fields, material blocks, colour blocks and one DMA chain per
visual. Each `N<node>_<visual>` child has a 4-byte payload: the offset of that visual's chain in the parent.

Chain at that offset: `u32 count` (low 16 bits), then `count` 16-byte tags `(lo, addr, v0, v1)` with
`kind = (lo >> 16) & 0x3FF` and `qwc = lo & 0xFFFF`:

| Kind | Block | Notes |
|---|---|---|
| 6 | name field | NUL-terminated `.tif` name, padded to 16 or 32 bytes. Starts a material section. |
| 3 | material | `qwc` qwords of GS register data (ALPHA / TEX1 / TEST ...) for the section. |
| 4 | colour | RGBA per vertex, `0x80` = 1.0. `v0`/`v1` are the VIF codes (`STCYCL 0x103`, `UNPACK V4-8 @6`) that put it in the third slot of each vertex. This is the baked lighting. |
| 2 | geometry | `qwc` qwords starting at a chunk header. Uses the colour block that preceded it. |

The editor's **Binding = DMA chain** mode walks these chains, so every chunk gets the exact texture of its section
and its vertex colours. The marker modes (tail scan, nearest marker) are string-scan heuristics kept for payloads
without chains, and the parser falls back to them automatically when a chain does not validate.

## Geometry chunk

```
+0x00 01000101        STCYCL 0x101
+0x04 6C048000        UNPACK V4-32 @0, 4 qwords:
+0x08 GIF tag, GIF tag, (index addr, 0, verts, tris), (0, 0, 0, scale)
+0x30 verts (int32)   +0x34 tris (int32)   +0x44 scale (float)
+0x48 01000203        STCYCL 0x203 (write 2 of every 3 qwords; the third is the colour)
+0x4C                 UNPACK V4-16 @4, 2 elements per vertex
+0x50 per vertex:     int16 x, y, z, nx | int16 u, v, ny, nz
      (u, v: 12-bit fixed point, 4096 = 1.0; normals Q15)
then  STCYCL 0x102 + UNPACK V4-8: triangles (a*3, b*3, c*3, 3)
then  STCYCL 0x102 + UNPACK V3-16: face normals, then MSCNT
```

Positions depend on the model kind, read from the GEO `vparams` flags (first u32):

| vparams flags | Kind | Position |
|---|---|---|
| `0x000BC0FA` (bit `0x20000` set) | weapons | `int16 * scale / 32768` |
| `0x0009C0FA` (bit clear) | props, vehicles, map objects | `int16 * scale / 16` |

The **Position units** dropdown is `auto` by default and can force either convention. `auto` decodes in the scale
convention, then compares the mesh extent with the GEO node's `nparams` bounds (floats 16-21: bounds min / max in
units); the two conventions differ by exactly 2048x, so the matching one is unambiguous. Without usable bounds it
falls back to the `vparams` flags, then to the scale convention. The preview pane's **Position units** row says which
rule was used.

Several visuals can reference the same geometry chunk (detail levels, state alternatives). Chain decoding reads each
chunk once, for the first visual that references it; the **Visual** dropdown isolates one visual.

## Texture record (`<name>.tif` → `texdat`)

```
+0x00 u16 width       +0x02 u16 height
+0x04 u32 pixel bytes +0x08 u32 texture index (TBP0 slot; not a palette id)
+0x0C u8 bpp, 0x06, 0, then 0x10 direct / 0x12 16-bit palette / 0x13 32-bit palette
+0x20 pixels
then  GIF tag + A+D register pairs: ALPHA, TEX1, TEX0, TEST, CLAMP
```

`TEX0` is the authority:

| Field | Use |
|---|---|
| PSM | pixel format: `0x13` 8-bit indexed (CSM1 palette order), `0x14` 4-bit, `0x02`/`0x0A` 16-bit A1B5G5R5, `0x00` 32-bit, `0x01` 24-bit |
| CBP | the palette id: `texpal_<CBP>` in the PAL archive (`par` word 0 is the same id). Palette ids are global in the game; a texture finds its palette by this id. |
| CPSM | palette format: 0 = 32-bit, 2 = 16-bit (the `buf` size, 1024 or 512 bytes, says the same) |
| TW / TH | log2 width / height |

`CLAMP` gives the wrap mode per axis, `TEST` the alpha test (`ATE`, `ATST`, `AREF`), `ALPHA` the blend equation.
The editor applies all three to the Three.js material.

Palette resolution order: `TEX0.CBP` in the texture's own namespace (`m51:texpal_44`), `CBP` globally, the
`.tif` alias from the same TXR/PAL pair, then the old header-id guesses (only for records without a GS tail).

8-bit CLUTs are stored in CSM1 order: entries 8-15 and 16-23 are swapped in every block of 32 (bits 3 and 4 of the
index exchanged). 4-bit textures use storage entries 0-15 of a 256-entry CLUT, low nibble first.

Alpha: GS alpha runs 0..0x80, mapped to 0..255. In 16-bit palettes and textures bit 15 is the STP/alpha bit;
when no entry sets it the image is opaque, otherwise coloured entries stay visible and black entries without the
bit are transparent (how the game's cutouts look through TEXA/AEM).

## Rendering

- Models with chain colours render pre-lit: `MeshBasicMaterial` with `vertexColors`, colour = byte / 128 (the GS
  multiplies texel by colour / 128, so `0x80` is neutral and `0xFF` doubles). Scene lights only apply to models
  without colour blocks.
- Texels are discarded only when the texture's own `TEST` register enables an alpha test (the game's cutouts:
  fences, foliage, decals). Decoded alpha is ignored otherwise, so a palette with black entries never punches holes
  in a wall. Effects keep their soft-blend setup.
- `V orientation = flip V` (default) matches the row-flipped texture upload; leave it unless a texture reads
  upside down.
- The preview pane's **Mesh** section shows how the model was decoded (binding, visuals, chunks bound, baked
  lighting, position units and why, chunks not drawn, missing textures); the **Texture** tab shows the real pixel
  format, the palette id and where it came from, and the GS wrap / alpha-test settings. The log pane prints the same
  as `[decode]`, `[chain]` and `[texture]` lines, and **Dump material report** lists every chunk.
- The extension host sends package and node payloads to the webview as binary (`ArrayBuffer`) instead of base64
  strings; the webview accepts both.
