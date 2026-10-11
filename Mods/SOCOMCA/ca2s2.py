"""Convert a SOCOM: Combined Assault character into a SOCOM II character pack (memory-card archive).

    python ca2s2.py --mission SP411 --model 0 --as con_pow01 --out mca0001
    python ca2s2.py --mission SP411 --model 0 --as con_pow01 --out mca0001 --card "<PCSX2 folder card>"

The pack holds one model named --as. Giving it the name of an existing SOCOM II model that the current map does not
load (con_pow01 = mp_pow1/mp_pow3, only on MP10/MP11) lets the game's own chartype spawn it:
    socom_bots.py loadpack mca0001 mc   then   socom_bots.py ai mp_pow1 team=0x40020000

How (see README.md for the formats):
  vertices   every (bone, position) part: world = position * CA bone world matrix, then re-expressed in the mapped
             SOCOM II bone (ca_archive.BONES) of a template SOCOM II skeleton; normals rotate the same way.
  draw data  CA's draw blocks are copied as-is (same GS tags, same vertex-buffer layout).
  textures   CA texture records -> SOCOM II texdat (header + pixels + GIF tail with TEX0); palettes -> texpal_<id>.
  skeleton   the template's GEO model (same rig), renamed.
"""
import argparse
import os
import struct
import sys

import ca_archive as ca
import charmesh as cm
import s2mesh
import vif

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "SOCOM2", "bots"))
import build_charpack as bp                                  # noqa: E402

SCALE = 4096.0
TEMPLATE = ("F:/SOCOM Packs/SOCOM2/m2host", "alg_UN01")     # SOCOM II skeleton + texture/palette header source
SAMPLE16 = ("F:/SOCOM Packs/SOCOM2/m20021/CLIB_TXR.ZED", "alban_ter03_sleave.tif")   # a retail 16-bit skin
PAL_BASE = 700                                              # above the disc (<=594) and build_charpack packs (600+)


def pal_base(pack):
    """Palette ids are global across everything loaded (a texture finds its palette by the id in TEX0 CBP, first
    registered wins), so every pack gets its own block of 32: 1024 + 32 * (crc32(pack) % 480) (CBP is 14 bits)."""
    import zlib
    return 1024 + 32 * (zlib.crc32(pack.lower().encode()) % 480)
I4 = [[1.0 if i == j else 0.0 for j in range(4)] for i in range(4)]


# ---- matrices -------------------------------------------------------------------------------------------------------

def rigid_inverse(m):
    """Inverse of a row-major rotation+translation matrix (row vector convention)."""
    r = [[m[j][i] for j in range(3)] for i in range(3)]            # transpose of the 3x3
    t = m[3][:3]
    it = [-sum(t[k] * r[k][j] for k in range(3)) for j in range(3)]
    return [r[0] + [0.0], r[1] + [0.0], r[2] + [0.0], it + [1.0]]


def rot(v, m):
    return tuple(v[0] * m[0][j] + v[1] * m[1][j] + v[2] * m[2][j] for j in range(3))


CLAMPED = [0]


def clamp16(x):
    v = int(round(x))
    if not -32768 <= v <= 32767:
        CLAMPED[0] += 1
    return max(-32768, min(32767, v))


# ---- SOCOM II template ----------------------------------------------------------------------------------------------

def template():
    """-> (GEO model node, {mid: world matrix}, {bone name: mid}, TXR header words, sample texdat, PAL header words)"""
    root, model = TEMPLATE
    geo_hdr, geo = bp.zed_read(open(os.path.join(root, "CLIB_GEO.ZED"), "rb").read())
    node = next(g for g in geo[0].children if g.name == model)
    world, mids = {}, {}

    def walk(n, parent):
        kids = {c.name: c for c in n.children}
        m = parent
        if "nparams" in kids:
            f = struct.unpack("<24f", kids["nparams"].data)
            m = cm.mat_mul([[f[0], f[1], f[2], 0], [f[4], f[5], f[6], 0], [f[8], f[9], f[10], 0],
                            [f[12], f[13], f[14], 1]], parent)
            for v in (kids["visuals"].children if "visuals" in kids else []):
                for c in v.children:
                    if c.name == "matrix_id":
                        mid = struct.unpack("<I", c.data)[0]
                        world[mid], mids[n.name] = m, mid
        if "children" in kids:
            for c in kids["children"].children:
                walk(c, m)
    walk(node, I4)
    txr_hdr, txr = bp.zed_read(open(os.path.join(root, "CLIB_TXR.ZED"), "rb").read())
    pal_hdr, _ = bp.zed_read(open(os.path.join(root, "CLIB_PAL.ZED"), "rb").read())
    mdl_hdr, _ = bp.zed_read(open(os.path.join(root, "CLIB_MDL.ZED"), "rb").read())
    sample = next(n for n in txr if n.name == "textures").children[0].children[0].data
    s16 = next(n for n in bp.zed_read(open(SAMPLE16[0], "rb").read())[1] if n.name == "textures")
    sample16 = next(t for t in s16.children if t.name == SAMPLE16[1]).children[0].data
    renderphase = next(n for n in txr if n.name == "renderphase")
    return {"geo_hdr": geo_hdr, "geo": node, "world": world, "mids": mids, "txr_hdr": txr_hdr, "pal_hdr": pal_hdr,
            "mdl_hdr": mdl_hdr, "sample": sample, "sample16": sample16, "renderphase": renderphase}


# ---- CA model ---------------------------------------------------------------------------------------------------------

def ca_bones(clib, model):
    """[(node, hash, world matrix, local matrix, parent hash)] in pre-order without the model root - the order CA
    vertex batches index."""
    out = []

    def walk(n, parent, parent_hash, root):
        f = struct.unpack_from("<16f", clib.d, n + 0x10)
        local = [list(f[0:4]), list(f[4:8]), list(f[8:12]), list(f[12:16])]
        m = cm.mat_mul(local, parent)
        h = clib.u32(n + 4)
        if not root:
            out.append((n, h, m, local, parent_hash))
        for k in clib.vector(n + 0x50):
            walk(k, m, None if root else h, False)
    walk(model, I4, None, True)
    return out


REFERENCE = ("SP411", 0)                     # a CA character whose stored skeleton is the A-pose bind pose
POSED = 1.0                                  # a joint further than this from the reference: the stored skeleton is posed
_ref = {}


def reference_bind(disc_clibs=None):
    """{bone hash: bind world matrix} of the reference CA skeleton."""
    if not _ref:
        clib = next(ca.Clib(d) for m, d in (disc_clibs or ca.Disc().clibs()) if m == REFERENCE[0])
        _ref.update({h: w for n, h, w, l, p in ca_bones(clib, clib.models[REFERENCE[1]])})
    return _ref


def bind_pose(bones):
    """Bind world matrix per CA bone index. A model whose stored skeleton is the A-pose uses it (exact proportions);
    a posed one (menu / victory poses) uses the reference skeleton - bone-relative vertex positions do not depend on
    the pose, only the bind frame does. Bones the reference lacks hang off their reference parent."""
    ref = reference_bind()
    dev = max((sum((w[3][k] - ref[h][3][k]) ** 2 for k in range(3)) ** 0.5 for n, h, w, l, p in bones if h in ref),
              default=0.0)
    if dev <= POSED:
        return [w for n, h, w, l, p in bones], False
    out, by_hash = [], {}
    for n, h, w, local, parent in bones:
        m = ref.get(h) or cm.mat_mul(local, by_hash.get(parent, I4))
        by_hash[h] = m
        out.append(m)
    return out, True


def ca_groups(data, start, end):
    """CA mesh object -> [{texture hash, groups: [{"vertex": [(flags, tris, bone, verts)], "draw": raw bytes}]}]."""
    def batch_at(p):                                         # STCYCL 0x101 + UNPACK V4-16: a vertex batch starts here
        return (p + 8 <= end and struct.unpack_from("<I", data, p)[0] == 0x01000101
                and (struct.unpack_from("<I", data, p + 4)[0] >> 24) & 0x7F == 0x6D)

    sections, o = [], start
    while o < end - 8:
        if not batch_at(o):
            o += 16
            continue
        # a section: 16-byte texture field, then groups (vertex batches + a draw block). After each draw block's
        # MSCNT (padded to 16) either the next group's vertex batch follows, or the next section's texture field -
        # whose hash may happen to look like a VIF command, so never walk across it.
        sec = {"texture": struct.unpack_from("<I", data, o - 0x10)[0], "groups": []}
        p = o
        while batch_at(p):
            group, draw_at, nxt = {"vertex": [], "draw": None}, None, None
            for off, name, imm, num, pay in vif.walk(data, p, end):
                if name not in cm.KEEP:
                    break
                if name == "UNPACK V4-16":
                    flags, tris, bone, count = struct.unpack_from("<4h", pay, 0)
                    verts = [struct.unpack_from("<8h", pay, 16 + 16 * i) for i in range(count)]   # 2 header elements
                    group["vertex"].append((flags, tris, bone, verts))
                elif name == "STCYCL" and imm == 0x102 and draw_at is None:
                    draw_at = off
                elif name == "MSCNT" and draw_at is not None:
                    block = data[draw_at:off + 4]
                    group["draw"] = block + bytes(-len(block) % 16)
                    nxt = (off + 4 + 15) & ~15
                    break
            if nxt is None:                                  # no draw block: the data ended mid-group
                break
            sec["groups"].append(group)
            p = nxt
        sections.append(sec)
        o = max(o + 16, p + 16)                              # skip the next texture field
    return sections


def convert_vertices(flags, tris, ca_bone, verts, bones, bind, tpl):
    """One CA vertex batch -> (SOCOM II bone mid, re-expressed verts)."""
    h = bones[ca_bone][1]
    ca_world = bind[ca_bone]
    s2name = ca.BONES[h][1]
    mid = tpl["mids"][s2name]
    inv = rigid_inverse(tpl["world"][mid])
    out = []
    for x, y, z, slot2, nx, ny, nz, w in verts:
        wp = cm.transform((x / SCALE, y / SCALE, z / SCALE), ca_world)
        lp = cm.transform(wp, inv)
        wn = rot((nx, ny, nz), ca_world)
        ln = rot(wn, inv)
        out.append((clamp16(lp[0] * SCALE), clamp16(lp[1] * SCALE), clamp16(lp[2] * SCALE), slot2,
                    clamp16(ln[0]), clamp16(ln[1]), clamp16(ln[2]), w))
    return mid, out


# ---- textures ---------------------------------------------------------------------------------------------------------

def texdat(clib, rec, index, pal_id, tpl, max_size=None):
    """CA texture record -> SOCOM II texdat: 0x20 header, pixels, GIF A+D tail (copied from a SOCOM II sample)."""
    sample = tpl["sample"]
    t = clib.texture(rec)
    w, h, psm, pix = t["width"], t["height"], t["psm"], t["pixels"]
    tex0 = t["tex0"]
    if psm == 0x00:                                       # 32-bit: SOCOM II skins are 8-bit or 16-bit -> A1B5G5R5
        px = struct.unpack(f"<{w * h}I", pix)
        pix = struct.pack(f"<{w * h}H", *(((c >> 3) & 31) | (((c >> 11) & 31) << 5) | (((c >> 19) & 31) << 10)
                                          | (0x8000 if c >> 31 or (c >> 24) & 0xFF >= 0x40 else 0) for c in px))
        tex0 = (tex0 & ~(0x3F << 20)) | (0x02 << 20)
        psm = 0x02
    if psm == 0x14:                                       # 4-bit (CA vehicles/props; SOCOM II has none) -> 8-bit.
        pix = bytes(b for x in pix for b in (x & 0xF, x >> 4))   # low nibble first; CA's 16 colours already sit at
        tex0 = (tex0 & ~(0x3F << 20)) | (0x13 << 20)              # CSM1 slots 0-7 / 16-23 of the 256-entry palette
        psm = 0x13
    while max_size and max(w, h) > max_size:              # halve (nearest): VRAM for runtime-loaded props is scarce
        bpp = 2 if psm == 0x02 else 1
        rows = [pix[y * w * bpp:(y + 1) * w * bpp] for y in range(0, h, 2)]
        pix = b"".join(b"".join(r[x:x + bpp] for x in range(0, w * bpp, 2 * bpp)) for r in rows)
        w, h = w // 2, h // 2
        tw, th = (tex0 >> 26) & 0xF, (tex0 >> 30) & 0xF
        tex0 = (tex0 & ~(0xFF << 26)) | ((tw - 1) << 26) | ((th - 1) << 30)
        tex0 = (tex0 & ~(0x3F << 14)) | (max(1, w // 64) << 14)              # TBW (64-pixel units)
    if psm == 0x02:                                       # header +0x0C: bpp, 6, 0, 0x10 direct / 0x12 paletted
        sample = tpl["sample16"]
    hdr = bytearray(sample[:0x20])
    struct.pack_into("<HHII", hdr, 0, w, h, len(pix), index)
    if psm == 0x13 and (tex0 >> 51) & 0xF == 0:           # 32-bit palette (CPSM 0): header +0x0F 0x13, not 0x12
        hdr[0x0F] = 0x13                                  # (16-bit) - else the game reads it as 16-bit: red/green
    tail = bytearray(sample[0x20 + struct.unpack_from("<I", sample, 4)[0]:])
    o = bp.tex0_offset(bytes(hdr) + pix + bytes(tail)) - 0x20 - len(pix)
    tex0 = (tex0 & ~0x3FFF) | index                                           # TBP0 = texture index (as the disc)
    if psm in (0x13, 0x14):
        tex0 = (tex0 & ~(0x3FFF << 37)) | (pal_id << 37)                      # CBP = palette id
    else:
        tex0 &= (1 << 37) - 1                                                 # direct colour: no CBP/CPSM/CSM/CSA/CLD
    struct.pack_into("<Q", tail, o, tex0)
    return bytes(hdr) + pix + bytes(tail)


# ---- build ------------------------------------------------------------------------------------------------------------

def convert(clib, mission, model_index, name, out_name, out_root, tpl, verbose=True):
    """One CA model -> <out_root>/<out_name>/ (CLIB_*.ZED + <NAME>.CDB). Returns (CDB path, info dict)."""
    CLAMPED[0] = 0
    model = clib.models[model_index]
    bones = ca_bones(clib, model)
    bind, posed = bind_pose(bones)

    # mesh: every mesh object attached to the model's nodes
    sections_out, used_tex = [], []
    for node in [model] + [b[0] for b in bones]:
        for obj in clib.vector(node + 0x98):
            size = clib.u32(obj + 0x1C)
            for sec in ca_groups(clib.d, obj, obj + 0x80 + size):
                if sec["texture"] not in used_tex:
                    used_tex.append(sec["texture"])
                out = {"texture": f"ca_{sec['texture']:08x}.tif", "groups": []}
                for g in sec["groups"]:
                    vblocks = []
                    for flags, tris, cab, verts in g["vertex"]:
                        mid, conv = convert_vertices(flags, tris, cab, verts, bones, bind, tpl)
                        vblocks.append((mid, s2mesh.vertex_block(flags, tris, mid, conv)))
                    out["groups"].append({"vertex": vblocks, "draw": g["draw"]})
                sections_out.append(out)
    data, start, ref_count = s2mesh.encode(sections_out)

    # textures + palettes
    by_hash = {clib.u32(r + 0x4C): r for r in clib.textures}
    tex_nodes, pal_nodes, pal_ids = [], [], {}
    for i, h in enumerate(used_tex, 1):
        rec = by_hash[h]
        t = clib.texture(rec)
        pal_id = 0
        if t["psm"] in (0x13, 0x14):
            prec = t["palette"]
            if prec not in pal_ids:
                pal_ids[prec] = PAL_BASE + len(pal_ids)
                col = clib.u32(prec + 0x14)
                par = struct.pack("<II", pal_ids[prec], clib.u32(prec + 4))
                pal_nodes.append(bp.Node(f"texpal_{pal_ids[prec]}", children=[
                    bp.Node("par", par), bp.Node("buf", clib.d[col:col + ca.palette_size(clib.u32(prec + 4))])]))
            pal_id = pal_ids[prec]
        tex_nodes.append(bp.Node(f"ca_{h:08x}.tif", children=[
            bp.Node("texdat", texdat(clib, rec, i, pal_id, tpl))]))

    # GEO: the template model, renamed
    geo = bp.Node(name, tpl["geo"].data, tpl["geo"].children)
    mesh = bp.Node(f"MESH_{name}", data, [bp.Node(f"MESH_{name}_START", struct.pack("<I", start)),
                                          bp.Node("ref_count", struct.pack("<I", ref_count)),
                                          bp.Node("mtx_count", struct.pack("<I", 26))])
    files = {
        "TXR": bp.zed_write(tpl["txr_hdr"], [tpl["renderphase"], bp.Node("textures", children=tex_nodes)]),
        "PAL": bp.zed_write(tpl["pal_hdr"], [bp.Node("palettes", children=pal_nodes)]),
        "GEO": bp.zed_write(tpl["geo_hdr"], [bp.Node("models", children=[geo])]),
        "MDL": bp.zed_write(tpl["mdl_hdr"], [mesh]),
    }
    out_dir = os.path.join(out_root, out_name)
    os.makedirs(out_dir, exist_ok=True)
    for part, blob in files.items():
        bp.zed_read(blob)
        open(os.path.join(out_dir, f"CLIB_{part}.ZED"), "wb").write(blob)
    cdb = bp.cdb_write({f"RUN\\MP\\{out_name.upper()}\\CLIB_{p}.ZED": b for p, b in files.items()})
    path = os.path.join(out_dir, f"{out_name.upper()}.CDB")
    open(path, "wb").write(cdb)
    info = {"sections": len(sections_out), "textures": [f"{h:08x}" for h in used_tex], "palettes": len(pal_nodes),
            "groups": sum(len(s["groups"]) for s in sections_out), "kb": len(cdb) // 1024,
            "clamped": CLAMPED[0], "posed": posed}
    if verbose:
        if CLAMPED[0]:
            print(f"  warning: {CLAMPED[0]} vertex values overflowed 16 bits and were clamped")
        print(f"{mission} model {model_index} -> {name}: {len(sections_out)} sections, {len(tex_nodes)} textures, "
              f"{len(pal_nodes)} palettes, mesh {len(data) // 1024} KB, pack {len(cdb) // 1024} KB -> {out_dir}")
    return path, info


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--mission", default="SP411")
    ap.add_argument("--model", type=int, default=0)
    ap.add_argument("--as", dest="name", default="con_pow01", help="SOCOM II model name to give it")
    ap.add_argument("--out", default="mca0001", help="pack name (starts with m, max 7 characters)")
    ap.add_argument("--root", default="F:/SOCOM Packs/SOCOMCA/packs")
    ap.add_argument("--card", help="also install into this PCSX2 folder memory card")
    a = ap.parse_args()
    bp.check_pack_name(a.out)
    clib = next(ca.Clib(d) for m, d in ca.Disc().clibs() if m == a.mission.upper())
    path, _ = convert(clib, a.mission.upper(), a.model, a.name, a.out, a.root, template())
    if a.card:
        bp.install(path, a.card)


if __name__ == "__main__":
    main()
