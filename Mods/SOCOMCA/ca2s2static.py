"""Convert a SOCOM: Combined Assault static model (vehicle, turret, prop, objective item) into a SOCOM II static pack.

    python ca2s2static.py --zdb VEH_HUMV.ZDB --resource veh_humvee_50cal.rdr --model 3 --out mcv0001

The geometry chunks are the same format in both games (s2static / castatic), so they are copied as they are. Per CA
mesh object (one SOCOM II visual), each (texture, chain) section becomes a SOCOM II section: the .tif name field
(ca_<texture hash>.tif), a material block from a SOCOM II template, then the CA colour + geometry blocks. The GEO tree
mirrors the CA node tree (local transforms, bounds, one visual per mesh object, vparams = flags, tris, verts, centroid);
node names are the recovered ones (firepoint, firepoint_shell, aimpoint, Gun_box ...) or n_<hash>.
"""
import argparse
import os
import struct
import sys
import zlib

import ca2s2
import ca_archive as ca
import castatic
import s2static
import vif

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "SOCOM2", "bots"))
import build_charpack as bp                                  # noqa: E402

TEMPLATES = {                                                # kind -> SOCOM II pack: material block, ZED headers and
    "weapon": ("F:/SOCOM Packs/SOCOM2/weapons", "m4Acarbine_203", "WEAP"),   # renderphase (= library category 0x02)
    "vehicle": ("F:/SOCOM Packs/SOCOM2/vehicles", "humvee", "BCAR"),         # category 0x11 (drawn black as a
                                                                             # plain node: lit by a vehicle object)
    "prop": ("F:/SOCOM Packs/SOCOM2/props", "sandbags", "MP61", "PROP"),
    "alpha": ("F:/SOCOM Packs/SOCOM2/props", "sandbags", "MP61", "PROP", 0x0B),   # category 0x0B: drawn in the
                                                                             # TexAlpha phase - its VRAM pools have
                                                                             # room; TexWorld (0x00) fills up fast         # category 0x00: pre-lit map props
}
NODE_NAMES = ["firepoint", "firepoint_shell", "aimpoint", "Gun_box", "body", "turret", "gun", "wheel", "door",
              "seat", "driver", "gunner", "passenger", "hatch", "barrel", "rotor", "tail_rotor", "prop"]
BOUND = 4.0                                                  # placeholder node bounds (culling)
NAMES = {zlib.crc32(n.encode()) & 0xFFFFFFFF: n for n in NODE_NAMES}


def template(kind="weapon"):
    import json
    root, model, lib = TEMPLATES[kind][:3]
    out_lib = TEMPLATES[kind][3] if len(TEMPLATES[kind]) > 3 else lib
    cat = json.load(open(os.path.join(root, "catalog.json")))
    pack = next(p["pack"] for p in cat["packs"] if p["model"] == model)
    d = os.path.join(root, pack)
    parts = {p: bp.zed_read(open(os.path.join(d, f"{lib}_{p}.ZED"), "rb").read()) for p in ("TXR", "PAL", "GEO", "MDL")}
    mdl = parts["MDL"][1][0]
    sec = next(iter(s2static.parse(mdl).values()))[0]
    return {"parts": parts, "material": sec["material"], "mat_upper": sec["mat_upper"], "lib": out_lib,
            "renderphase": (bp.Node("renderphase", struct.pack("<I", TEMPLATES[kind][4])) if len(TEMPLATES[kind]) > 4
                            else next(n for n in parts["TXR"][1] if n.name == "renderphase")),
            "char": ca2s2.template()}                        # 8/16-bit texdat samples


def chunk_stats(g):
    """(verts, tris, [vertex positions]) of one geometry chunk (header words 10/11, scale word 15)."""
    for off, name, imm, num, pay in vif.walk(g):
        if name == "UNPACK V4-32" and num == 4:
            h = struct.unpack("<16I", pay)
            return h[10], h[11]
    return 0, 0


def nparams(local, lo, hi, kind, flags):
    """SOCOM II node params: 3 rotation rows (x4), translation, 1.0, bounds min, bounds max, then two words the loader
    copies into the bound model/node: kind (7 root, 1 other; model +0x58) and flags (model +0x5C). Retail flags:
    0x80800051, | 0x40000 with visuals, minus 0x800000 for a leaf node with visuals, | 0x1000 with a di node."""
    f = [local[0][0], local[0][1], local[0][2], 0.0, local[1][0], local[1][1], local[1][2], 0.0,
         local[2][0], local[2][1], local[2][2], 0.0, local[3][0], local[3][1], local[3][2], 1.0,
         lo[0], lo[1], lo[2], hi[0], hi[1], hi[2]]
    return struct.pack("<22f2I", *f, kind, flags)


def node_meshes(clib, n):
    """(textures {hash}, triangles, bbox (lo, hi) in the node's own space) of a node's mesh objects, or None."""
    import staticmesh
    texs, tris, pts = set(), 0, []
    for obj in clib.vector(n + 0x98):
        for tex, mat, pairs in castatic.sections(clib, obj):
            if tex:
                texs.add(clib.u32(tex + 0x4C))
            for _, _, g in pairs:
                for _, v, t in staticmesh.chunks(g):
                    pts += v
                    tris += len(t)
    if not pts:
        return None
    return texs, tris, ([min(p[i] for p in pts) for i in range(3)], [max(p[i] for p in pts) for i in range(3)])


def auto_skip(clib, model):
    """CA models keep lower-detail copies and an enclosing low-poly hull next to the full mesh and draw one by
    distance; SOCOM II would draw them all (an olive box over the Humvee). No flag marks them, so:
      copy: >= 10 triangles, shares >= 60% of its textures with a node that has more triangles, and its bbox is
            within 25% of that node's on every axis
      hull: one texture of <= 16x16, bbox >= 50% of the most detailed node's on every axis
    (bboxes are in each node's own space - fine for the Humvee's body / LOD / hull nodes, which share one)
    -> {node hash}"""
    nodes = []
    def walk(n):
        m = node_meshes(clib, n)
        if m:
            nodes.append((clib.u32(n + 4), *m))
        for k in clib.vector(n + 0x50):
            walk(k)
    walk(model)
    if not nodes:
        return set()
    size = lambda b: [b[1][i] - b[0][i] for i in range(3)]
    whole = size(max(nodes, key=lambda x: x[2])[3])
    tex_size = {clib.u32(r + 0x4C): clib.texture(r)["width"] * clib.texture(r)["height"] for r in clib.textures}
    skip = set()
    for h, texs, tris, box in nodes:
        sz = size(box)
        if len(texs) == 1 and tex_size.get(next(iter(texs)), 1 << 20) <= 256 and                 all(sz[i] >= 0.5 * whole[i] for i in range(3)):
            skip.add(h)
            continue
        for h2, texs2, tris2, box2 in nodes:
            sz2 = size(box2)
            if h2 != h and tris >= 10 and tris2 > tris and texs and len(texs & texs2) >= 0.6 * len(texs) and                     all(abs(sz[i] - sz2[i]) <= 0.25 * max(sz2[i], 1) for i in range(3)):
                skip.add(h)
                break
    return skip


def convert(clib, model_index, name, out_name, out_root, tpl, lib_name="WEAP", verbose=True, max_size=64, bright=0x80, skip=()):
    model = clib.models[model_index]
    if skip == "auto":
        skip = auto_skip(clib, model)
        if verbose and skip:
            print("  leaving out (LOD copies / hull):", ", ".join(f"{h:08x}" for h in sorted(skip)))
    order = []

    def walk(n, depth):
        f = struct.unpack_from("<16f", clib.d, n + 0x10)
        order.append((n, [list(f[0:4]), list(f[4:8]), list(f[8:12]), list(f[12:16])], depth))
        for k in clib.vector(n + 0x50):
            walk(k, depth + 1)
    walk(model, 0)

    # visuals: one per mesh object, numbered N<index among nodes that have objects>_<object index>
    visuals, node_vis, used_tex, vis_index = {}, {}, [], 0
    for n, local, depth in order:
        objs = [] if clib.u32(n + 4) in skip else clib.vector(n + 0x98)   # skipped: LOD copies, hull
        if not objs:
            continue
        node_vis[n] = []
        for oi, obj in enumerate(objs):
            secs, verts, tris = [], 0, 0
            drawn = set()
            for tex, mat, pairs in castatic.sections(clib, obj):
                geometry = b"".join(g for _, _, g in pairs)
                if tex is None or geometry in drawn:          # alternative chains over the same geometry (wheels:
                    continue                                  # hub / tread / 2 untextured - states CA picks between);
                drawn.add(geometry)                           # SOCOM II would draw them all, the untextured as garbage
                if clib.texture(tex)["psm"] == 0x00 and sum(chunk_stats(g)[1] for _, _, g in pairs) <= 4:
                    continue                                  # a card with a 32-bit (alpha) texture: rotor blur discs -
                                                              # the prop material is opaque, so they'd draw solid
                th = clib.u32(tex + 0x4C)
                if tex and th not in [t for t, _ in used_tex]:
                    used_tex.append((th, tex))
                chunks = []
                for colour, cup, g in pairs:
                    v, t = chunk_stats(g)
                    verts, tris = verts + v, tris + t
                    if colour is not None and not any(colour[j] for j in range(len(colour)) if j % 4 != 3):
                        colour = None                         # all black: CA lights vehicles at run time, SOCOM II
                                                              # multiplies the texture by these (pre-lit) -> black
                    if colour is None:                        # no colour block: neutral grey (0x80)
                        colour = b"\x80" * (4 * v)
                        colour += bytes(-len(colour) % 16)
                        cup = (0x01000103, 0x6E00C006 | (v & 0xFF) << 16)
                    chunks.append((colour, cup, g))
                secs.append({"name_field": s2static.name_field(f"{out_name}_{th:08x}.tif"), "material": tpl["material"],
                             "mat_upper": tpl["mat_upper"], "chunks": chunks})
            key = f"N{vis_index:03d}_{oi:03d}"
            visuals[key] = secs
            centre = struct.unpack_from("<3f", clib.d, obj + 0x10)     # the object's stored centre
            node_vis[n].append((key, verts, tris, centre))
        vis_index += 1
    data, offsets = s2static.encode(visuals)

    # model bounds (game units: positions are 1/16 units, node translations whole units): written to every node so
    # culling is right and spawn_prop can put the lowest point on the ground (CA origins can sit at rotor height)
    import staticmesh
    pts, world = [], []
    for n, local, depth in order:
        del world[depth:]
        parent = world[-1] if world else None
        m = local if parent is None else [[sum(local[r][k] * parent[k][c] for k in range(4)) for c in range(4)]
                                          for r in range(4)]
        world.append(m)
        if n in node_vis:
            for obj in clib.vector(n + 0x98):
                for tex, mat, pairs in castatic.sections(clib, obj):
                    for _, _, g in pairs:
                        for _, v, _ in staticmesh.chunks(g):
                            pts += [[p[0] / 16 * m[0][c] + p[1] / 16 * m[1][c] + p[2] / 16 * m[2][c] + m[3][c]
                                     for c in range(3)] for p in v]
    lo = tuple(min(p[c] for p in pts) for c in range(3)) if pts else (-BOUND,) * 3
    hi = tuple(max(p[c] for p in pts) for c in range(3)) if pts else (BOUND,) * 3

    # GEO tree
    def geo_node(i):
        n, local, depth = order[i]
        h = clib.u32(n + 4)
        nm = name if i == 0 else NAMES.get(h, f"n_{h:08x}")
        children = [j for j in range(i + 1, len(order)) if order[j][2] == depth + 1 and
                    all(order[k][2] > depth for k in range(i + 1, j))]
        flags = 0x80800051
        if n in node_vis:
            flags |= 0x40000
            if i and not children:
                flags &= ~0x800000
        kids = [bp.Node("nparams", nparams(local, lo, hi, 7 if i == 0 else 1, flags))]
        if n in node_vis:
            vis = []
            for key, verts, tris, centre in node_vis[n]:
                # flags 0x0009C0FA: positions in 1/16 units (SOCOM II vehicles and CA static meshes); 0x000BC0FA
                # (bit 0x20000, weapons) means positions normalised by the chunk scale instead
                vp = struct.pack("<IIHHfff", 0x0009C0FA, 0, min(tris, 0xFFFF), min(verts, 0xFFFF), *centre)
                vis.append(bp.Node("vis", children=[bp.Node("vparams", vp)]))
            kids.append(bp.Node("visuals", children=vis))
        if children:
            kids.append(bp.Node("children", children=[geo_node(j) for j in children]))
        return bp.Node(nm, children=kids)
    geo = geo_node(0)

    # textures + palettes (as characters)
    tex_nodes, pal_nodes, pal_ids = [], [], {}
    for i, (h, rec) in enumerate(used_tex, 1):
        t = clib.texture(rec)
        pal_id = 0
        if t["psm"] in (0x13, 0x14):
            prec = t["palette"]
            if prec not in pal_ids:
                pal_ids[prec] = ca2s2.pal_base(out_name) + len(pal_ids)
                col = clib.u32(prec + 0x14)
                pal_nodes.append(bp.Node(f"texpal_{pal_ids[prec]}", children=[
                    bp.Node("par", struct.pack("<II", pal_ids[prec], clib.u32(prec + 4))),
                    bp.Node("buf", clib.d[col:col + ca.palette_size(clib.u32(prec + 4))])]))
            pal_id = pal_ids[prec]
        tex_nodes.append(bp.Node(f"{out_name}_{h:08x}.tif", children=[   # texture names are global: a
            # name already loaded by another pack is not re-registered (that pack's copy, in its pools, is used)
            bp.Node("texdat", ca2s2.texdat(clib, rec, i, pal_id, tpl["char"], max_size))]))

    mdl = bp.Node(name, data, [bp.Node(k, struct.pack("<I", offsets[k])) for k in visuals])
    p = tpl["parts"]
    files = {
        "TXR": bp.zed_write(p["TXR"][0], [tpl["renderphase"], bp.Node("textures", children=tex_nodes)]),
        "PAL": bp.zed_write(p["PAL"][0], [bp.Node("palettes", children=pal_nodes)]),
        "GEO": bp.zed_write(p["GEO"][0], [bp.Node("models", children=[geo])]),
        "MDL": bp.zed_write(p["MDL"][0], [mdl]),
    }
    out_dir = os.path.join(out_root, out_name)
    os.makedirs(out_dir, exist_ok=True)
    for part, blob in files.items():
        bp.zed_read(blob)
        open(os.path.join(out_dir, f"{lib_name}_{part}.ZED"), "wb").write(blob)
    cdb = bp.cdb_write({f"RUN\\MP\\{out_name.upper()}\\{lib_name}_{p}.ZED": b for p, b in files.items()})
    path = os.path.join(out_dir, f"{out_name.upper()}.CDB")
    open(path, "wb").write(cdb)
    info = {"visuals": len(visuals), "textures": len(tex_nodes), "palettes": len(pal_nodes), "kb": len(cdb) // 1024,
            "tris": sum(t for v in node_vis.values() for _, _, t, _ in v)}
    info["vram"], problems = check(files)
    info["bounds"] = (lo, hi)
    if verbose:
        print(f"{name}: {info['visuals']} visuals, {info['tris']} tris, {info['textures']} textures, {info['kb']} KB, "
              f"VRAM {info['vram']:#x} blocks, bounds y {lo[1]:.1f}..{hi[1]:.1f} -> {out_dir}")
        for p in problems:
            print("  PROBLEM:", p)
    return path, info


def check(files):
    """Offline checks of a built pack (every one of these has shown up in game as black / red-green garbage):
    each MDL section's .tif name is in the TXR; each paletted texture's CBP is a palette in the PAL, of the type its
    header byte +0x0F says (0x12 16-bit, 0x13 32-bit); no 4-bit textures. -> (VRAM blocks, [problems])."""
    txr = bp.zed_read(files["TXR"])[1]
    pals = {}
    for n in bp.zed_read(files["PAL"])[1][0].children:
        k = {c.name: c for c in n.children}
        pals[struct.unpack("<I", k["par"].data[:4])[0]] = struct.unpack("<I", k["par"].data[4:8])[0]
    texs, blocks, problems = {}, 0, []
    for t in next(n for n in txr if n.name == "textures").children:
        td = t.children[0].data
        w, h = struct.unpack_from("<HH", td, 0)
        tex0 = struct.unpack_from("<Q", td, bp.tex0_offset(td))[0]
        psm, cbp = (tex0 >> 20) & 0x3F, (tex0 >> 37) & 0x3FFF
        blocks += -(-w * h * td[0x0C] // 8 // 256)
        texs[t.name] = 1
        if psm == 0x14:
            problems.append(f"{t.name}: 4-bit (SOCOM II has none)")
        if psm == 0x13:
            kind = pals.get(cbp)
            if kind is None:
                problems.append(f"{t.name}: palette {cbp} missing")
            elif (kind == 0x400) != (td[0x0F] & 0x0F == 0x03):
                problems.append(f"{t.name}: palette {cbp} type {kind:#x} but header +0x0F {td[0x0F]:#x}")
    mdl = bp.zed_read(files["MDL"])[1][0]
    for key, secs in s2static.parse(mdl).items():
        for s in secs:
            tname = s["name_field"].rstrip(bytes(1)).decode("latin1")
            if tname not in texs:
                problems.append(f"{key}: texture {tname} not in the TXR")
    return blocks, problems


def load(zdb_name, resource):
    for arc, inner, z in ca.Disc().zdbs():
        if inner.replace("/", "\\").split("\\")[-1].upper() == zdb_name.upper():
            r = ca.v6_resource(z, resource, "ASST")
            if r:
                return ca.Clib(r)
    sys.exit(f"no ASST {resource} in {zdb_name}")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--zdb", default="VEH_HUMV.ZDB")
    ap.add_argument("--resource", default="veh_humvee_50cal.rdr")
    ap.add_argument("--model", type=int, default=3)
    ap.add_argument("--as", dest="name", default=None, help="SOCOM II model name (default ca_<hash>)")
    ap.add_argument("--kind", default="vehicle", choices=sorted(TEMPLATES),
                    help="weapon (WEAP), vehicle (BCAR) or prop (PROP: a map-prop library - spawn_prop renders it lit)")
    ap.add_argument("--out", default="mcv0001")
    ap.add_argument("--bright", type=lambda x: int(x, 0), default=0x80, help="vertex colour put on unlit CA meshes "
                    "(0x80 = texture as is, up to 0xFF = twice as bright)")
    ap.add_argument("--skip", default="auto", help="comma-separated CA node hashes to leave out (their meshes): CA keeps "
                    "lower-detail copies + an enclosing hull in the same model and picks one by distance")
    ap.add_argument("--max-size", type=int, default=64, help="halve textures larger than this (VRAM: a fresh map's "
                    "prop pool has ~0xBA 256-byte blocks free)")
    ap.add_argument("--root", default="F:/SOCOM Packs/SOCOMCA/static")
    a = ap.parse_args()
    bp.check_pack_name(a.out)
    clib = load(a.zdb, a.resource)
    name = a.name or f"ca_{clib.u32(clib.models[a.model] + 4):08x}"
    tpl = template(a.kind)
    convert(clib, a.model, name, a.out, a.root, tpl, tpl["lib"], max_size=a.max_size, bright=a.bright,
            skip="auto" if a.skip == "auto" else {int(h, 16) for h in a.skip.split(",") if h})


if __name__ == "__main__":
    main()
