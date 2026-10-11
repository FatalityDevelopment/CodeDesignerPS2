"""Render a HUD icon for a converted weapon pack from its own 3D model (like custom_weapon.py does for the ray gun).

    python weapon_icon.py mcw25 [mcw12 ...]        # -> <pack folder>/hud_icon.bin + icon.png

Decodes the pack's <x>_high visual (WEAP_MDL.ZED geometry chunks), renders the 128x32 side view in gunmetal and
writes hud_icon.bin; socom_bots.py apply_weapon (giveweapon / armory) draws it into the stand-in icon's buffers.
"""
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "SOCOM2", "bots"))

import build_charpack as bp                                   # noqa: E402
import custom_weapon as cw                                    # noqa: E402
import render as rt                                           # noqa: E402
import s2static                                               # noqa: E402
import staticmesh                                             # noqa: E402

GUNMETAL = len(cw.MATERIALS)
cw.MATERIALS.append(((205, 208, 214), (48, 50, 56)))


class Mesh:
    def __init__(self):
        self.v, self.t = [], []


def pack_mesh(folder):
    """The pack's <x>_high visual as flat-shaded triangles (position, face normal, material)."""
    name = next(f for f in os.listdir(folder) if f.upper().endswith("_MDL.ZED"))
    mdl = bp.zed_read(open(os.path.join(folder, name), "rb").read())[1][0]
    visuals = s2static.parse(mdl)
    high = [k for k in visuals if k.lower().endswith("_high")] or list(visuals)[:1]
    mesh = Mesh()
    for key in high:
        for s in visuals[key]:
            for _, _, geo in s["chunks"]:
                for _, verts, tris in staticmesh.chunks(geo):
                    for a, b, c in tris:
                        p = [verts[a], verts[b], verts[c]]
                        e1 = [p[1][i] - p[0][i] for i in range(3)]
                        e2 = [p[2][i] - p[0][i] for i in range(3)]
                        n = (e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0])
                        if n[2] < 0:                          # two-sided: face the camera (+z)
                            n = tuple(-x for x in n)
                        base = len(mesh.v)
                        mesh.v += [(q, n, GUNMETAL) for q in p]
                        mesh.t.append((base, base + 1, base + 2))
    if not mesh.t:
        sys.exit(f"{folder}: no geometry in {high}")
    return mesh


def main():
    import socom_bots as sb
    for pack in sys.argv[1:] or sys.exit(__doc__):
        folder = os.path.dirname(sb.find_pack(pack))
        mesh = pack_mesh(folder)
        open(os.path.join(folder, "hud_icon.bin"), "wb").write(cw.hud_icon_bin(mesh))
        pix, pal = cw.render_icon(mesh)
        rgb = b"".join(bytes(pal[i][:3]) if pal[i][3] else b"\x20\x20\x24" for i in pix)
        rt.png(os.path.join(folder, "icon.png"), 128, 32, rgb)
        print(f"{pack}: {len(mesh.t)} triangles -> {folder}/hud_icon.bin, icon.png")


if __name__ == "__main__":
    main()
