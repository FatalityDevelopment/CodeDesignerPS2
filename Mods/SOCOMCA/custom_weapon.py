"""Author a new SOCOM II weapon from code: primitives -> mesh -> chunks (s2static.encode_mesh) -> weapon pack.

    python custom_weapon.py raygun [--out mrgun] [--root "F:/SOCOM Packs/Custom"]

Weapon frame (from SOCOM II / CA pistols): +x forward, +y up, z across, origin at the top of the grip, bore line about
0.5 up, muzzle about 1.5 forward. The texture is one 64x64 8-bit image: each material is a vertical strip of 32
shades (palette index = material * 32 + shade) and every vertex picks its shade from its normal against a light from
above-front, so the shading is baked in. The pack uses the retail weapon layout (<name>_high / _low first, then
firepoint, firepoint_shell, aimpoint, Gun_box - no copied 'di' box) and goes through ca2s2static.check().
In game: socom_bots.py giveweapon <pack> 1   (sidearm).
"""
import argparse
import math
import os
import struct
import sys

import ca2s2static as cs
import s2static

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "SOCOM2", "bots"))
import build_charpack as bp                                  # noqa: E402

LIGHT = (0.35, 0.85, 0.4)                                    # baked light direction (normalised below)
_L = math.sqrt(sum(c * c for c in LIGHT))
LIGHT = tuple(c / _L for c in LIGHT)


class Mesh:
    def __init__(self):
        self.v, self.t = [], []                             # v: (pos, normal, material)

    def tri(self, a, b, c):
        self.t.append((a, b, c))

    def add(self, pos, normal, mat):
        self.v.append((pos, normal, mat))
        return len(self.v) - 1

    # ---- primitives (axis along x) ----
    def lathe(self, profile, mat, cy=0.5, cz=0.0, seg=16, caps=True, scale_y=1.0):
        """profile: [(x, radius)] along +x; a surface of revolution around the bore line (y = cy, z = cz)."""
        rings = []
        for i, (x, r) in enumerate(profile):
            x0, r0 = profile[max(i - 1, 0)]
            x1, r1 = profile[min(i + 1, len(profile) - 1)]
            slope = -(r1 - r0) / ((x1 - x0) or 1e-6)          # profile normal: (slope, radial)
            ring = []
            for k in range(seg):
                a = 2 * math.pi * k / seg
                ny, nz = math.cos(a), math.sin(a)
                n = (slope, ny, nz)
                ln = math.sqrt(sum(c * c for c in n))
                ring.append(self.add((x, cy + r * ny * scale_y, cz + r * nz), tuple(c / ln for c in n), mat))
            rings.append(ring)
        for i in range(len(rings) - 1):
            for k in range(seg):
                a, b = rings[i][k], rings[i][(k + 1) % seg]
                c, d = rings[i + 1][k], rings[i + 1][(k + 1) % seg]
                self.tri(a, c, b)
                self.tri(b, c, d)
        if caps:
            for ring, x, sgn in ((rings[0], profile[0][0], -1), (rings[-1], profile[-1][0], 1)):
                centre = self.add((x, cy, cz), (sgn, 0, 0), mat)
                for k in range(seg):
                    a, b = ring[k], ring[(k + 1) % seg]
                    cap = [self.add(self.v[i][0], (sgn, 0, 0), mat) for i in (a, b)]
                    self.tri(centre, cap[1], cap[0]) if sgn > 0 else self.tri(centre, cap[0], cap[1])

    def sphere(self, centre, r, mat, seg=16, rings=10):
        cx, cy, cz = centre
        prof = [(cx - r * math.cos(math.pi * i / rings), r * math.sin(math.pi * i / rings)) for i in range(rings + 1)]
        self.lathe(prof, mat, cy=cy, cz=cz, seg=seg, caps=False)

    def box(self, lo, hi, mat, lean=0.0):
        """Axis-aligned box; lean shifts x by lean * (y - lo.y) (a raked grip)."""
        x0, y0, z0 = lo
        x1, y1, z1 = hi
        sh = lambda x, y: x + lean * (y - y0)
        faces = [((1, 0, 0), [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)]),
                 ((-1, 0, 0), [(x0, y0, z1), (x0, y1, z1), (x0, y1, z0), (x0, y0, z0)]),
                 ((0, 1, 0), [(x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0)]),
                 ((0, -1, 0), [(x0, y0, z1), (x0, y0, z0), (x1, y0, z0), (x1, y0, z1)]),
                 ((0, 0, 1), [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]),
                 ((0, 0, -1), [(x1, y0, z0), (x0, y0, z0), (x0, y1, z0), (x1, y1, z0)])]
        for n, quad in faces:
            ids = [self.add((sh(x, y), y, z), n, mat) for x, y, z in quad]
            self.tri(ids[0], ids[1], ids[2])
            self.tri(ids[0], ids[2], ids[3])

    def fin(self, x, r_in, r_out, thick, mat, cy=0.5, seg=16):
        """A flat ring (cooling fin) around the bore at x."""
        self.lathe([(x - thick / 2, r_in), (x - thick / 2, r_out), (x + thick / 2, r_out), (x + thick / 2, r_in)],
                   mat, cy=cy, seg=seg, caps=False)


# ---- materials: (light colour, dark colour) per strip ----
MATERIALS = [
    ((235, 40, 30), (70, 8, 6)),        # 0 red body
    ((230, 232, 240), (60, 64, 72)),    # 1 chrome
    ((120, 255, 230), (10, 120, 110)),  # 2 glow (teal)
    ((70, 60, 55), (12, 10, 10)),       # 3 grip
    ((250, 200, 90), (110, 70, 20)),    # 4 brass
    ((40, 40, 46), (6, 6, 8)),          # 5 dark metal
    ((255, 255, 255), (150, 150, 150)), # 6 highlight
    ((180, 30, 25), (40, 4, 3)),        # 7 deep red
]
RED, CHROME, GLOW, GRIP, BRASS, DARK, WHITE, DEEP = range(8)


def raygun():
    m = Mesh()
    by = 0.5                                                 # bore line height
    # body: a fat rounded capsule along the bore, slightly tall
    m.lathe([(-0.95, 0.0), (-0.9, 0.18), (-0.75, 0.3), (-0.3, 0.36), (0.25, 0.34), (0.55, 0.26), (0.62, 0.17)],
            RED, cy=by, seg=18, caps=False, scale_y=1.15)
    # rear dial
    m.sphere((-1.0, by, 0.0), 0.16, CHROME, seg=14, rings=8)
    m.lathe([(-0.55, 0.37), (-0.5, 0.37)], BRASS, cy=by, seg=18, caps=False, scale_y=1.15)   # body band
    # barrel + fins
    m.lathe([(0.55, 0.13), (1.55, 0.13)], CHROME, cy=by, seg=14)
    for x in (0.78, 1.0, 1.22):
        m.fin(x, 0.13, 0.36, 0.07, RED if x != 1.0 else DEEP, cy=by, seg=18)
    # flared muzzle + emitter
    m.lathe([(1.5, 0.12), (1.62, 0.16), (1.78, 0.27), (1.82, 0.27)], CHROME, cy=by, seg=18, caps=False)
    m.sphere((1.78, by, 0.0), 0.15, GLOW, seg=14, rings=8)
    # top sight + rib
    m.box((-0.2, by + 0.38, -0.04), (0.45, by + 0.46, 0.04), DARK)
    m.box((0.38, by + 0.46, -0.03), (0.45, by + 0.56, 0.03), CHROME)
    # grip (raked back), trigger guard, trigger
    m.box((-0.62, -0.85, -0.12), (-0.22, by - 0.25, 0.12), GRIP, lean=0.28)
    m.box((-0.62, -0.88, -0.13), (-0.2, -0.82, 0.13), BRASS, lean=0.28)                  # grip cap
    m.box((-0.2, by - 0.42, -0.03), (0.32, by - 0.36, 0.03), CHROME)                      # guard bottom
    m.box((0.27, by - 0.42, -0.03), (0.32, by - 0.22, 0.03), CHROME)                      # guard front
    m.box((-0.02, by - 0.36, -0.025), (0.04, by - 0.22, 0.025), DARK)                     # trigger
    attach = {"firepoint": (1.9, by, 0.0), "firepoint_shell": (0.2, by + 0.3, 0.2), "aimpoint": (-2.3, by + 0.5, 0.0),
              "Gun_box": (0.0, 0.0, 0.0)}
    return m, attach


MODELS = {"raygun": (raygun, "ca_raygun")}


# ---- texture ----
def palette():
    """256 x A1B5G5R5 in CSM1 storage order (entries 8-15 and 16-23 of every 32 swapped)."""
    cols = []
    for light, dark in MATERIALS:
        for s in range(32):
            t = (s / 31) ** 0.8
            rgb = [int(dark[i] + (light[i] - dark[i]) * t) for i in range(3)]
            cols.append(0x8000 | (rgb[0] >> 3) | (rgb[1] >> 3) << 5 | (rgb[2] >> 3) << 10)
    stored = [cols[(i & ~0x18) | ((i & 0x08) << 1) | ((i & 0x10) >> 1)] for i in range(256)]
    return struct.pack("<256H", *stored)


def pixels():
    """64x64: column strip 8m..8m+7 is material m, row y is shade y // 2 (top = dark, bottom = light)."""
    return bytes(32 * (x // 8) + (y // 2) for y in range(64) for x in range(64))


def uv_for(mat, normal):
    shade = max(0.0, sum(normal[i] * LIGHT[i] for i in range(3))) * 0.85 + 0.15
    s = min(31, int(shade * 31))
    return ((8 * mat + 4) / 64, (2 * s + 1) / 64)


def texdat(pix, w, h, index, pal_id, sample):
    """texdat from a SOCOM II 8-bit sample (header, GIF A+D tail) with our pixels / size / palette id."""
    hdr = bytearray(sample[:0x20])
    struct.pack_into("<HHII", hdr, 0, w, h, len(pix), index)
    tail = bytearray(sample[0x20 + struct.unpack_from("<I", sample, 4)[0]:])
    o = bp.tex0_offset(sample) - 0x20 - struct.unpack_from("<I", sample, 4)[0]
    tex0 = struct.unpack_from("<Q", tail, o)[0]
    tex0 &= ~((0x3FFF) | (0x3F << 14) | (0x3F << 20) | (0xF << 26) | (0xF << 30) | (0x3FFF << 37))
    tex0 |= index | max(1, w // 64) << 14 | 0x13 << 20 | (w.bit_length() - 1) << 26 | (h.bit_length() - 1) << 30
    tex0 |= pal_id << 37
    struct.pack_into("<Q", tail, o, tex0)
    return bytes(hdr) + pix + bytes(tail)


def build(kind, out_name, out_root):
    import ca2s2
    mesh, attach = MODELS[kind][0]()
    model = MODELS[kind][1]
    tpl = cs.template("weapon")
    char = ca2s2.template()
    verts = [(p, n, uv_for(mat, n)) for p, n, mat in mesh.v]
    chunks = s2static.encode_mesh(verts, mesh.t)
    tex_name = f"{out_name}_skin.tif"
    sec = {"name_field": s2static.name_field(tex_name), "material": tpl["material"], "mat_upper": tpl["mat_upper"],
           "chunks": []}
    for g in chunks:
        nv = struct.unpack_from("<I", g, 0x30)[0]
        colour = bytes((0x80, 0x80, 0x80, 0x80)) * nv
        colour += bytes(-len(colour) % 16)
        sec["chunks"].append((colour, (0x01000103, 0x6E00C006 | (nv & 0xFF) << 16), g))
    visuals = {"N000_000": [sec], "N001_000": [sec]}
    data, offsets = s2static.encode(visuals)
    mdl = bp.Node(model, data, [bp.Node(k, struct.pack("<I", offsets[k])) for k in visuals])

    pts = [p for p, _, _ in mesh.v]
    lo = tuple(min(p[i] for p in pts) for i in range(3))
    hi = tuple(max(p[i] for p in pts) for i in range(3))
    ident = [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]]
    centre = tuple((lo[i] + hi[i]) / 2 for i in range(3))
    vp = struct.pack("<IIHHfff", 0x000BC0FA, 0, len(mesh.t), len(mesh.v), *centre)
    base = model[3:]
    lods = [bp.Node(f"{base}_{tag}", children=[bp.Node("nparams", cs.nparams(ident, lo, hi, 1, 0x80040055)),
                                               bp.Node("visuals", children=[bp.Node("vis", children=[bp.Node("vparams", vp)])])])
            for tag in ("high", "low")]
    points = []
    for nm in cs.HELD_ORDER:
        x, y, z = attach[nm]
        local = [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [x, y, z, 1]]
        points.append(bp.Node(nm, children=[bp.Node("nparams", cs.nparams(local, lo, hi, 1, 0x80800055))]))
    geo = bp.Node(model, children=[bp.Node("nparams", cs.nparams(ident, lo, hi, 7, 0x80800055)),
                                   bp.Node("children", children=lods + points)])

    pal_id = __import__("ca2s2").pal_base(out_name)
    tex = bp.Node(tex_name, children=[bp.Node("texdat", texdat(pixels(), 64, 64, 1, pal_id, char["sample"]))])
    pal = bp.Node(f"texpal_{pal_id}", children=[bp.Node("par", struct.pack("<II", pal_id, 0x00020200)),
                                                bp.Node("buf", palette())])
    p = tpl["parts"]
    files = {"TXR": bp.zed_write(p["TXR"][0], [tpl["renderphase"], bp.Node("textures", children=[tex])]),
             "PAL": bp.zed_write(p["PAL"][0], [bp.Node("palettes", children=[pal])]),
             "GEO": bp.zed_write(p["GEO"][0], [bp.Node("models", children=[geo])]),
             "MDL": bp.zed_write(p["MDL"][0], [mdl])}
    blocks, problems = cs.check(files)
    out_dir = os.path.join(out_root, out_name)
    os.makedirs(out_dir, exist_ok=True)
    lib = tpl["lib"]
    for part, blob in files.items():
        open(os.path.join(out_dir, f"{lib}_{part}.ZED"), "wb").write(blob)
    cdb = bp.cdb_write({f"RUN\\MP\\{out_name.upper()}\\{lib}_{k}.ZED": b for k, b in files.items()})
    open(os.path.join(out_dir, f"{out_name.upper()}.CDB"), "wb").write(cdb)
    import render
    render.wire(os.path.join(out_dir, "preview.png"), [tuple(mesh.v[i][0] for i in t) for t in mesh.t])
    open(os.path.join(out_dir, "hud_icon.bin"), "wb").write(hud_icon_bin(mesh))
    print(f"{model}: {len(mesh.v)} verts, {len(mesh.t)} tris, {len(chunks)} chunks, VRAM {blocks:#x}, "
          f"size {[round(hi[i] - lo[i], 2) for i in range(3)]} -> {out_dir}")
    for pr in problems:
        print("  PROBLEM:", pr)


# ---- HUD icon: a 128x32 side view in a category 0x0C library (the HUD's own VRAM pools, like RUN/COMMON/HUDW) ----
HUDW_SAMPLE = os.path.join(HERE, "..", "SOCOM2", "bots", "hudw_sample.json")   # retail icon template (see icon_template)


def icon_template():
    """Header / tail of a retail HUD icon (sigcommando_icon.tif) + the HUDW TXR/PAL ZED headers, from the disc."""
    import json
    if os.path.exists(HUDW_SAMPLE):
        d = json.load(open(HUDW_SAMPLE))
        return bytes.fromhex(d["texdat"]), {int(k): v for k, v in d["txr"].items()}, {int(k): v for k, v in d["pal"].items()}
    import characters_readme as cr
    iso = open("F:/SOCOM II - U.S. Navy SEALs.iso", "rb")
    ent = cr.zdb_entries(iso, *cr.iso_listing(iso)["/RUN/MP61.ZDB"])
    th, tn = bp.zed_read(ent["RUN\\COMMON\\HUDW_TXR.ZED"])
    ph, _ = bp.zed_read(ent["RUN\\COMMON\\HUDW_PAL.ZED"])
    td = next(t for t in next(n for n in tn if n.name == "textures").children
              if t.name == "sigcommando_icon.tif").children[0].data
    json.dump({"texdat": td.hex(), "txr": th, "pal": ph}, open(HUDW_SAMPLE, "w"))
    return td, th, ph


def render_icon(mesh, w=128, h=32, ss=4):
    """Side view (camera on +z) of the mesh, shaded from its normals, transparent background -> (8-bit pixels,
    256 x RGBA palette with alpha 0x80 = opaque, as the retail icons)."""
    W, H = w * ss, h * ss
    pts = [p for p, _, _ in mesh.v]
    x0, x1 = min(p[0] for p in pts), max(p[0] for p in pts)
    y0, y1 = min(p[1] for p in pts), max(p[1] for p in pts)
    sc = min((W - 2 * ss) / (x1 - x0), (H - 2 * ss) / (y1 - y0))
    ox, oy = (W - (x1 - x0) * sc) / 2, (H - (y1 - y0) * sc) / 2
    col = [[None] * W for _ in range(H)]
    zb = [[-1e9] * W for _ in range(H)]
    for a, b, c in mesh.t:
        P = [mesh.v[i] for i in (a, b, c)]
        sx = [ox + (p[0][0] - x0) * sc for p in P]
        sy = [H - (oy + (p[0][1] - y0) * sc) for p in P]
        lo_y, hi_y = max(0, int(min(sy))), min(H - 1, int(max(sy)) + 1)
        lo_x, hi_x = max(0, int(min(sx))), min(W - 1, int(max(sx)) + 1)
        den = (sy[1] - sy[2]) * (sx[0] - sx[2]) + (sx[2] - sx[1]) * (sy[0] - sy[2])
        if abs(den) < 1e-9:
            continue
        for py in range(lo_y, hi_y + 1):
            for px in range(lo_x, hi_x + 1):
                l0 = ((sy[1] - sy[2]) * (px - sx[2]) + (sx[2] - sx[1]) * (py - sy[2])) / den
                l1 = ((sy[2] - sy[0]) * (px - sx[2]) + (sx[0] - sx[2]) * (py - sy[2])) / den
                l2 = 1 - l0 - l1
                if min(l0, l1, l2) < 0:
                    continue
                z = sum(l * p[0][2] for l, p in zip((l0, l1, l2), P))
                if z <= zb[py][px]:
                    continue
                zb[py][px] = z
                n = [sum(l * p[1][i] for l, p in zip((l0, l1, l2), P)) for i in range(3)]
                ln = math.sqrt(sum(x * x for x in n)) or 1
                shade = max(0.0, sum(n[i] / ln * LIGHT[i] for i in range(3))) * 0.75 + 0.25
                light, dark = MATERIALS[P[0][2]]
                col[py][px] = [int(dark[i] + (light[i] - dark[i]) * shade) for i in range(3)]
    # downsample: coverage -> alpha, mean colour; quantize to a 6x7x6 cube (index 0 = transparent)
    pal = [(0, 0, 0, 0)] + [(int(r * 51), int(g * 42.5), int(b * 51), 0x80)
                            for r in range(6) for g in range(7) for b in range(6)]
    pal += [(0, 0, 0, 0)] * (256 - len(pal))
    pix = bytearray()
    for y in range(h):
        for x in range(w):
            cell = [col[y * ss + j][x * ss + i] for j in range(ss) for i in range(ss)]
            hit = [c for c in cell if c]
            if len(hit) * 2 < len(cell):
                pix.append(0)
                continue
            r, g, b = (sum(c[k] for c in hit) / len(hit) for k in range(3))
            pix.append(1 + round(r / 51) * 42 + round(g / 42.5) * 6 + round(b / 51))
    return bytes(pix), pal


def hud_icon_bin(mesh):
    """The HUD icon as the game holds it in RAM: 4096 pixel bytes (128x32, rows bottom-up - the HUD's textures are
    stored flipped) + 1024 palette bytes (256 RGBA, CSM1 order). giveweapon draws it into a stand-in icon's buffers
    (texture object +0x18 / palette object +0x14), which the HUD re-uploads every frame."""
    pix, pal = render_icon(mesh)
    pix = b"".join(pix[y * 128:(y + 1) * 128] for y in range(31, -1, -1))
    stored = [pal[(i & ~0x18) | ((i & 0x08) << 1) | ((i & 0x10) >> 1)] for i in range(256)]
    return pix + b"".join(struct.pack("<I", r | g << 8 | b << 16 | a << 24) for r, g, b, a in stored)


def build_icon(kind, out_name, out_root):
    """A pack holding just the weapon's HUD icon (<out_name>.tif) in a category 0x0C library."""
    import ca2s2
    mesh, _ = MODELS[kind][0]()
    sample, txr_hdr, pal_hdr = icon_template()
    pix, pal = render_icon(mesh)
    pal_id = ca2s2.pal_base(out_name)
    stored = [pal[(i & ~0x18) | ((i & 0x08) << 1) | ((i & 0x10) >> 1)] for i in range(256)]       # CSM1 order
    buf = b"".join(struct.pack("<I", r | g << 8 | b << 16 | a << 24) for r, g, b, a in stored)
    hdr = bytearray(sample[:0x20])
    struct.pack_into("<I", hdr, 0x8, 1)                       # index (TBP); +0x0C stays the format word (8-bit,
                                                             #   32-bit palette: 0x08060033)
    tail = bytearray(sample[0x20 + 0x1000:])
    o = bp.tex0_offset(sample) - 0x20 - 0x1000
    tex0 = struct.unpack_from("<Q", tail, o)[0]
    tex0 = (tex0 & ~(0x3FFF | (0x3FFF << 37))) | 1 | pal_id << 37
    struct.pack_into("<Q", tail, o, tex0)
    tex_name = f"{out_name}.tif"
    tex = bp.Node(tex_name, children=[bp.Node("texdat", bytes(hdr) + pix + bytes(tail))])
    palnode = bp.Node(f"texpal_{pal_id}", children=[bp.Node("par", struct.pack("<II", pal_id, 0x400)),
                                                    bp.Node("buf", buf)])
    tpl = cs.template("weapon")
    p = tpl["parts"]
    files = {"TXR": bp.zed_write(txr_hdr, [bp.Node("renderphase", struct.pack("<I", 0x0C)),
                                           bp.Node("textures", children=[tex])]),
             "PAL": bp.zed_write(pal_hdr, [bp.Node("palettes", children=[palnode])]),
             "GEO": bp.zed_write(p["GEO"][0], [bp.Node("models", children=[])]),
             "MDL": bp.zed_write(p["MDL"][0], [])}
    for part in files:
        bp.zed_read(files[part])
    out_dir = os.path.join(out_root, out_name)
    os.makedirs(out_dir, exist_ok=True)
    for part, blob in files.items():
        open(os.path.join(out_dir, f"HUDX_{part}.ZED"), "wb").write(blob)
    cdb = bp.cdb_write({f"RUN\\MP\\{out_name.upper()}\\HUDX_{k}.ZED": b for k, b in files.items()})
    open(os.path.join(out_dir, f"{out_name.upper()}.CDB"), "wb").write(cdb)
    import rip_textures as rt
    rgb = b"".join(bytes(pal[i][:3]) if pal[i][3] else b"\x20\x20\x28" for i in pix)
    rt.png(os.path.join(out_dir, "icon.png"), 128, 32, rgb)
    print(f"{tex_name}: 128x32 HUD icon -> {out_dir}")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("kind", choices=sorted(MODELS))
    ap.add_argument("--out", default="mrgun")
    ap.add_argument("--icon", default="mrgicon", help="pack for the HUD icon (category 0x0C library)")
    ap.add_argument("--root", default="F:/SOCOM Packs/Custom")
    a = ap.parse_args()
    bp.check_pack_name(a.out)
    bp.check_pack_name(a.icon)
    build(a.kind, a.out, a.root)
    build_icon(a.kind, a.icon, a.root)


if __name__ == "__main__":
    main()
