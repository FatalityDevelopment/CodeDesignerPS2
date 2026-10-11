"""Convert every SOCOM: Combined Assault character into a SOCOM II character pack, with previews and a catalog.

Read-only on the ISO. One pack per unique CA model (by its name hash), named m3NNNN, model name ca_<hash>; the
skeleton/texture templates come from the SOCOM II library (ca2s2.TEMPLATE / SAMPLE16 - run rip_socom2.py first).

    python rip_ca.py                                   # -> F:/SOCOM Packs/SOCOMCA/packs + catalog.json + README.md
    python rip_ca.py --card "<PCSX2 folder card>"      # also install every pack (31+ MB - more than one 8 MB card)

A ca_<hash> model has no SOCOM II chartype yet; to spawn one now, convert it with ca2s2.py --as <an existing model the
map does not load> (e.g. con_pow01 -> spawn mp_pow1).
"""
import argparse
import json
import os
import struct
import sys

import ca2s2
import ca_archive as ca
import charmesh as cm
import render
import s2mesh
import vif

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "SOCOM2", "bots"))
import build_charpack as bp                                  # noqa: E402


def preview(pack_dir, name, tpl, path):
    """Wireframe of the converted mesh, read back through its DMA chain and the SOCOM II skeleton."""
    mesh = bp.zed_read(open(os.path.join(pack_dir, "CLIB_MDL.ZED"), "rb").read())[1][0]
    kids = {c.name: c for c in mesh.children}
    secs = s2mesh.parse(mesh.data, struct.unpack("<I", kids[f"MESH_{name}_START"].data)[0])
    tris = []
    for s in secs:
        for g in s["groups"]:
            slots = {}
            for bone, blk in g["vertex"]:
                count = struct.unpack_from("<4h", blk, 8)[3]
                for i in range(count):
                    x, y, z, s2, nx, ny, nz, w = struct.unpack_from("<8h", blk, 16 + 16 * i)
                    slots.setdefault(s2 // 2, []).append((bone, (x, y, z), (nx, ny, nz), w))
            tl = []
            for off, nm, imm, num, pay in vif.walk(g["draw"]):
                if nm == "UNPACK V4-8":
                    tl = [tuple(v // 3 for v in pay[4 * i:4 * i + 3]) for i in range(num)]
            batch = {"verts": sorted(slots.items())}
            wv = cm.world_vertices(batch, tpl["world"], 1 / 4096)
            idx = {sl: i for i, (sl, _) in enumerate(batch["verts"])}
            tris += [tuple(wv[idx[v]] for v in t) for t in tl if all(v in idx for v in t)]
    render.wire(path, tris, size=200)
    return len(tris)


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--iso", default=ca.ISO)
    ap.add_argument("--out", default="F:/SOCOM Packs/SOCOMCA/packs")
    ap.add_argument("--card", help="also install every pack into this PCSX2 folder memory card")
    a = ap.parse_args()
    tpl = ca2s2.template()
    os.makedirs(a.out, exist_ok=True)
    seen, catalog, failed, n = {}, [], [], 0
    for mission, data in ca.Disc(a.iso).clibs():
        clib = ca.Clib(data)
        for i, model in enumerate(clib.models):
            h = clib.u32(model + 4)
            if h in seen:
                seen[h]["missions"].append(mission)
                continue
            n += 1
            pack, name = f"m3{n:04d}", f"ca_{h:08x}"
            try:
                path, info = ca2s2.convert(clib, mission, i, name, pack, a.out, tpl, verbose=False)
                tris = preview(os.path.join(a.out, pack), name, tpl, os.path.join(a.out, pack, "preview.png"))
            except Exception as e:                                     # keep going; listed in the catalog
                failed.append({"pack": pack, "mission": mission, "model": i, "hash": f"{h:08x}", "error": repr(e)})
                print(f"  {pack} {mission}#{i} {h:08x}: FAILED {e!r}")
                continue
            entry = {"pack": pack, "model": name, "hash": f"{h:08x}", "mission": mission, "index": i,
                     "missions": [mission], "triangles": tris, **info}
            seen[h] = entry
            catalog.append(entry)
            if a.card:
                bp.install(path, a.card)
            print(f"  {pack} {mission}#{i} {name}: {tris} tris, {len(info['textures'])} textures, {info['kb']} KB"
                  + (f", {info['clamped']} clamped" if info["clamped"] else ""))
    json.dump({"game": "SOCOM: Combined Assault -> SOCOM II", "packs": catalog, "failed": failed},
              open(os.path.join(a.out, "catalog.json"), "w"), indent=1)
    lines = ["# Combined Assault characters as SOCOM II packs", "",
             f"{len(catalog)} characters converted by `Mods/SOCOMCA/rip_ca.py` ({sum(c['kb'] for c in catalog) // 1024} MB). "
             "Each folder: the memory-card archive `<NAME>.CDB`, its four libraries, and `preview.png` (wireframe "
             "through the SOCOM II skeleton). Textures are in `../textures/<hash>.png`.", "",
             "Model names are `ca_<hash>` (CA keeps only name hashes). To spawn one today, rebuild it with "
             "`ca2s2.py --mission <M> --model <i> --as <SOCOM II model the map does not load> --out <pack>`.", "",
             "| Pack | Model | First seen | Missions | Triangles | Textures | KB |", "|---|---|---|---|---|---|---|"]
    for c in catalog:
        lines.append(f"| `{c['pack']}` | `{c['model']}` | {c['mission']} #{c['index']} | {len(c['missions'])} | "
                     f"{c['triangles']} | {len(c['textures'])} | {c['kb']} |")
    if failed:
        lines += ["", "## Not converted", ""] + [f"- `{f['pack']}` {f['mission']} #{f['model']}: {f['error']}" for f in failed]
    open(os.path.join(a.out, "README.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print(f"done: {len(catalog)} packs, {len(failed)} failed -> {a.out}")


if __name__ == "__main__":
    main()
