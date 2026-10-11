"""Rip every SOCOM II static model - weapons, gear, vehicles/turrets, map items (props), effects, objects - into
reusable memory-card packs (build_charpack format, one model per pack) with previews and a catalog per category.

Read-only on the ISO. Characters are rip_socom2.py; world geometry (WORL) and UI libraries are skipped.

    python rip_static.py                         # -> F:/SOCOM Packs/SOCOM2/<category>/<pack>
    python rip_static.py --only weapons          # one category

A pack keeps its source library's file name (WEAP_*.ZED, FLIB_*.ZED, MP61_*.ZED ...), so its renderphase/category is
the original one. Pack names start with 'm' + a category letter and are at most 7 characters (check_pack_name).
"""
import argparse
import collections
import hashlib
import json
import os
import re
import sys

import build_charpack as bp
import characters_readme as cr

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "SOCOMCA"))
import render                                                # noqa: E402  (Mods/SOCOMCA)
import staticmesh                                            # noqa: E402

BS = chr(92)
CATEGORY = {                                                 # library name -> (category, pack prefix)
    "WEAP": ("weapons", "mw"), "FLIB": ("gear", "mg"), "EFFE": ("effects", "me"), "OBJT": ("objects", "mo"),
}
VEHICLES = {"AH6", "KA50", "PAVE", "T30M", "T72Y", "TCAM", "TLLM", "TLMG", "TLRM", "TRMG", "TURR", "BCAR", "BENZ",
            "VG63", "VM53"}
SKIP = {"CLIB", "WORL", "FONT", "HUD", "HUD2", "HUDW", "LOAD", "ALPH", "JUNK"}


def category(top, lib):
    if lib in CATEGORY:
        return CATEGORY[lib]
    if lib in VEHICLES:
        return ("vehicles", "mv")
    if top == "UI" or lib in SKIP:
        return None
    return ("props", "mi")                                   # the map's own library + small prop sets (SN71, TM53 ...)


def libraries(disc):
    """-> ({(dir, lib): first ZDB} for libraries with models, {ZDB: [library keys with textures]},
    {texture name: library key} over every library with a TXR - textures are shared across libraries: WEAP c4 uses
    FLIB's satchel textures, turrets use WEAP's, effects use the texture-only ALPH)."""
    models, per_zdb, tex_index = {}, collections.defaultdict(list), {}
    for path in sorted(disc.listing):
        if not path.startswith("/RUN/") or not path.endswith(".ZDB") or path.count("/") != 2:
            continue
        try:
            ent = cr.zdb_entries(disc.iso, *disc.listing[path])
        except Exception:
            continue
        zdb = path[5:-4]
        for k, blob in ent.items():
            up = k.upper()
            if not up.endswith("_TXR.ZED"):
                continue
            parts = k.split(BS)
            lib = parts[-1][:-8].upper()
            d = "/".join(parts[1:-1]).upper()
            key = f"{zdb}:{d}/{lib}"
            per_zdb[zdb].append(key)
            try:
                for n in bp.zed_read(blob)[1]:
                    if n.name == "textures":
                        for t in n.children:
                            tex_index.setdefault(t.name, key)
            except Exception:
                pass
            if f"{BS.join(parts[:-1])}{BS}{parts[-1][:-8]}_MDL.ZED" in ent and                     f"{BS.join(parts[:-1])}{BS}{parts[-1][:-8]}_GEO.ZED" in ent:
                models.setdefault((d, lib), zdb)
    return models, per_zdb, tex_index


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--iso", default="F:/SOCOM II - U.S. Navy SEALs.iso")
    ap.add_argument("--out", default="F:/SOCOM Packs/SOCOM2")
    ap.add_argument("--only", help="one category: weapons, gear, vehicles, props, effects, objects")
    a = ap.parse_args()
    disc = bp.Disc(a.iso)
    libs, per_zdb, tex_index = libraries(disc)
    seen, cats, counters = {}, collections.defaultdict(list), collections.Counter()
    failed = collections.defaultdict(list)
    for (d, lib), zdb in sorted(libs.items()):
        cat = category(d.split("/")[0], lib)
        if not cat or (a.only and cat[0] != a.only):
            continue
        cname, prefix = cat
        key = f"{zdb}:{d}/{lib}"
        others = [k for k in per_zdb[zdb] if k != key]
        try:
            parts = disc.package(key)
        except Exception as e:
            failed[cname].append({"library": key, "error": repr(e)})
            continue
        for mesh in parts["MDL"][1]:
            digest = hashlib.md5(mesh.data).hexdigest()[:12]
            ident = (cname, mesh.name, digest)
            if ident in seen:
                seen[ident]["libraries"].append(f"{d}/{lib}")
                continue
            counters[cname] += 1
            pack = f"{prefix}{counters[cname]:04d}"
            root = os.path.join(a.out, cname)
            try:
                own = {t.name for n in parts["TXR"][1] if n.name == "textures" for t in n.children}
                extra = [tex_index[t] for t in bp.mesh_textures(mesh.data) if t not in own and t in tex_index]
                path, info = bp.build(disc, [(key, mesh.name)], pack, out_root=root, verbose=False,
                                      lib_name=lib, mesh_prefix="", fallback=others + extra,
                                      allow_missing=True)
                tris = staticmesh.triangles(mesh.data)
                render.wire(os.path.join(root, pack, "preview.png"), tris, size=200)
            except Exception as e:
                failed[cname].append({"pack": pack, "library": key, "model": mesh.name, "error": repr(e)})
                print(f"  {cname}/{pack} {mesh.name}: FAILED {e!r}")
                continue
            entry = {"pack": pack, "model": mesh.name, "library": f"{d}/{lib}", "source": zdb, "libraries": [f"{d}/{lib}"],
                     "triangles": len(tris), "textures": info["textures"], "kb": info["bytes"] // 1024,
                     "missing_textures": info["missing"]}
            seen[ident] = entry
            cats[cname].append(entry)
        disc.pkgs.clear()                                     # bound the cache: one library at a time
        print(f"  {d}/{lib:5} ({zdb}) -> {cname}: {sum(len(v) for v in cats.values())} packs so far")
    for cname, entries in cats.items():
        root = os.path.join(a.out, cname)
        dupe = collections.Counter(e["model"] for e in entries)
        json.dump({"game": "SOCOM II (SCUS-97275)", "category": cname, "packs": entries, "failed": failed[cname]},
                  open(os.path.join(root, "catalog.json"), "w", encoding="utf-8"), indent=1)
        lines = [f"# SOCOM II {cname}", "",
                 f"{len(entries)} packs ({sum(e['kb'] for e in entries) // 1024} MB), one model each, ripped by "
                 "`Mods/SOCOM2/bots/rip_static.py`. Each folder: the memory-card archive `<NAME>.CDB`, the four library "
                 "files (named after the source library) and `preview.png`.", "",
                 "Models whose name appears more than once differ between maps (different meshes under one name).", "",
                 "| Pack | Model | Library | Also in | Triangles | Textures | KB |", "|---|---|---|---|---|---|---|"]
        for e in entries:
            name = f"`{e['model']}`" + (" *" if dupe[e["model"]] > 1 else "")
            lines.append(f"| `{e['pack']}` | {name} | {e['library']} | {len(e['libraries']) - 1} | {e['triangles']} | "
                         f"{e['textures']} | {e['kb']} |")
        if failed[cname]:
            lines += ["", "## Not ripped", ""] + [f"- {f}" for f in failed[cname]]
        open(os.path.join(root, "README.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("done:", {c: len(v) for c, v in cats.items()}, "failed:", {c: len(v) for c, v in failed.items() if v})


if __name__ == "__main__":
    main()
