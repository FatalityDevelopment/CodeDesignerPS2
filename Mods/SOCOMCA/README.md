# SOCOM: Combined Assault tools

Read-only tools for SOCOM: Combined Assault (SCUS-97498), working toward porting its characters into SOCOM II.

| File | What it does |
|---|---|
| `ca_archive.py` | Reads the disc: `RUN/<hash>.ZAR` archives, the nested ZDB v6 files, and each mission's character library (`VCOMMON_<MISSION>.ZDB` → `clib`). The format notes are in its docstring. |
| `rip_ca.py` | Converts every CA character into a SOCOM II pack, with previews and a catalog. |
| `ca2s2.py` | Converts one CA character into a SOCOM II character pack: `python ca2s2.py --mission SP411 --model 0 --as con_pow01 --out mca0001`. |
| `charmesh.py` / `s2mesh.py` / `render.py` / `vif.py` | The shared character mesh decoder, the SOCOM II mesh/DMA-chain encoder, wireframe previews, and the VIF walker. |
| `crack_names.py` | Recovers names from CRC32 hashes by dictionary attack (SOCOM II vocabulary + CA words). Expect chance matches: filter to families/character words (`names_credible.json`). |
| `rip_textures.py` | Writes every character texture, with its palette applied, to PNG (one file per unique texture hash) plus `index.json`. |

```bash
python Mods/SOCOMCA/rip_textures.py
```

Output: `F:/SOCOM Packs/SOCOMCA/textures` (292 unique textures, about 4.5 MB).

## Port status

| Step | State |
|---|---|
| Archives, character library layout | Done (`ca_archive.py`) |
| Textures and palettes | Done. 281 are 8-bit paletted with 16-bit palettes, 5 are 16-bit and 6 are 32-bit. Their GS register packet matches SOCOM II's texture data tail. |
| Skeleton | Same rig and rest pose as SOCOM II. `ca_archive.BONES` maps every bone of all 108 CA models to a SOCOM II bone. CA adds a forearm twist bone, a head-top bone and two headgear nodes on MP skins, and has no toes. |
| Names | CRC32 of the case-sensitive name (`.tif` for textures). `crack_names.py` recovered 56 character types (`netchar1–35`, `UiCharType1–7`, `merc_mp1–4`, `woodland_mp1–3`, `male/female_civilian`, `ia_leader`, `bullfrog`, `underwood` …) and 49 texture names (`female_face1–4`, `ghillie_*`, `merc_*`, `villager_*`, `vip_*` …). Model names are not on the disc and resist guessing; 13 models are labelled through the character types that use them. |
| Meshes | Decoded for both games (`charmesh.py`). SOCOM II meshes re-encode byte-identical (`s2mesh.py`, 178/180; the other 2 differ only in block order). |
| Conversion | `ca2s2.py` converts one CA character; `rip_ca.py` converts all 108 into `F:/SOCOM Packs/SOCOMCA/packs` (`m3NNNN`, model `ca_<hash>`, with wireframe previews and a catalog). Vertices are re-expressed per bone with the CA bind frame × inverse SOCOM II bone. Posed stored skeletons (2 menu-pose models) use a reference A-pose bind. **Confirmed in game 2026-10-10** on MP61: `mca0001` (SP411 #0, as `con_pow01` → spawn `mp_pow1`) loaded from the memory card, and `mca0002` (MP322BRE #16 ghillie sniper, as `rus_escort01` → spawn `mp_vip1`, 16-bit textures) loaded over the bridge. Both render and their AI moves. |

## Character mesh format (SOCOM II `MESH_<model>` and CA mesh objects)

A mesh is a list of material sections. Each starts with a texture reference: a 32-byte `.tif` name field in SOCOM II, or a 16-byte field holding the CRC32 of the name in CA. A run of VIF batches follows, each ending in `MSCNT`. Walk them with `vif.py`.

- **Vertex batch:** `STCYCL 0x101`, then `UNPACK V4-16` (write address 4 in SOCOM II, 0 in CA).
  - Header: `(flags, triangles, bone, count)`. Flags: 2 = first bone (overwrite), 1 = add a bone, 5 = add the last bone and finish, 6 = single bone and finish. `triangles` repeats the following draw batch's triangle count. `bone` is the SOCOM II matrix_id, or in CA the pre-order index of the bone node (excluding the model root).
  - SOCOM II has 1 header element; CA has 2 (the second is `0,0,0,0x8000`).
  - Then `count` vertices, each 2 elements: `(x, y, z, slot*2)` and `(nx, ny, nz, weight)`. Positions are 16-bit, relative to the bone. Normals and the weight are Q15 (32767 = 1.0).
  - Position scale is 1/4096. A vertex's world position is the weighted sum of `position × bone world matrix` over its bones.
- **Draw batch:**
  - `STCYCL 0x102`, `UNPACK V4-8` (unsigned): triangles `(a, b, c, 3)`, vertex slots in steps of 3.
  - `STCYCL 0x101`, `UNPACK V4-32 @0`: two GIF tags (registers 0x412: texture coordinate, colour, position; vertex count) plus `(index address, 0, vertex count, triangle count)`.
  - `STCYCL 0x103`, `UNPACK V2-16 @5`: texture coordinates, 12-bit fixed point (0..4095), written every third qword.
