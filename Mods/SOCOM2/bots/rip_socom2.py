"""Rip every SOCOM II character into a reusable pack library: one memory-card archive (build_charpack.py format:
CLIB_{TXR,PAL,GEO,MDL}.ZED + <NAME>.CDB) per character, with the escort sets grouped, plus a catalog.

Read-only on the ISO. Each model is ripped once (from a multiplayer package when it has one), together with its
low-detail copies (_1 / _2 / _LO), its textures and its palettes (renumbered from 600, see build_charpack).

    python rip_socom2.py                                  # -> F:/SOCOM Packs/SOCOM2
    python rip_socom2.py --out "C:/Users/<you>/Desktop/SOCOM Packs/SOCOM2"

Pack names start with 'm' and are at most 7 characters (build_charpack.check_pack_name): the groups are m2host /
m2vips / m2pows, every other character is m2NNNN - see the README.md / catalog.json written next to the packs.
Load one with  socom_bots.py loadpack <name> mc  after copying its BASCUS-97275<NAME> folder to a memory card
(build_charpack.install, or --card below).
"""
import argparse
import json
import os
import re
import sys

import build_charpack as bp
import characters_readme as cr

HERE = os.path.dirname(os.path.abspath(__file__))

GROUPS = {                                         # spawned together by a game mode
    "m2host": ["alg_UN01", "alg_UN02", "alg_UNworker"],     # mp_algerian_h1/h2/h3 escort hostages
    "m2vips": ["rus_escort01", "rus_escort03"],             # mp_vip1/2/3 Russian VIPs
    "m2pows": ["con_pow01", "con_pow02"],                   # mp_pow1/2/3 POWs
}
LOD = re.compile(r"_[12](_LO)?$|_LO$")


def catalog_models(disc):
    """{model: [packages that have it in their character library]}, MP packages first."""
    where = {}
    for path in sorted(disc.listing, key=lambda p: (not re.match(r"/RUN/MP", p), p)):
        m = re.match(r"/RUN/(M\w+)\.ZDB$", path)
        if not m:
            continue
        pkg = m.group(1)
        kind = "MP" if pkg.startswith("MP") else "SP"
        ent = cr.zdb_entries(disc.iso, *disc.listing[path])
        mdl = ent.get(f"RUN\\{kind}\\{pkg}\\CLIB_MDL.ZED")
        if mdl is None:
            continue
        for model in cr.zed_models(mdl):
            if not LOD.search(model):
                where.setdefault(model, []).append(pkg)
    return where


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--iso", default="F:/SOCOM II - U.S. Navy SEALs.iso")
    ap.add_argument("--out", default="F:/SOCOM Packs/SOCOM2")
    ap.add_argument("--card", help="also install every pack into this PCSX2 folder memory card")
    a = ap.parse_args()

    chartypes = {}
    for line in open(os.path.join(HERE, "chartypes.tsv"), encoding="utf-8"):
        name, _, model = line.rstrip("\n").partition("\t")
        if model:
            chartypes.setdefault(model, []).append(name)

    disc = bp.Disc(a.iso)
    where = catalog_models(disc)
    grouped = {m for ms in GROUPS.values() for m in ms}
    jobs = [(name, models) for name, models in GROUPS.items()]
    singles = sorted(m for m in where if m not in grouped)
    jobs += [(f"m2{i:04d}", [m]) for i, m in enumerate(singles, 1)]
    print(f"{len(where)} character models in {len({p for ps in where.values() for p in ps})} packages -> "
          f"{len(jobs)} packs in {a.out}")

    os.makedirs(a.out, exist_ok=True)
    catalog, failed, total = [], [], 0
    for n, (name, models) in enumerate(jobs, 1):
        bp.check_pack_name(name)
        pack = [(where[m][0], m) for m in models]
        try:
            cdb, info = bp.build(disc, pack, name, out_root=a.out, verbose=False)
        except bp.BuildError as e:
            failed.append((name, models, str(e)))
            print(f"  [{n}/{len(jobs)}] {name}: FAILED {e}")
            continue
        if a.card:
            bp.install(cdb, a.card)
        total += info["bytes"]
        catalog.append({"pack": name, "models": models, "meshes": info["meshes"],
                        "spawn_as": sorted({c for m in models for c in chartypes.get(m, [])}),
                        "from": {m: where[m] for m in models}, "source": pack[0][0] if len(pack) == 1 else
                        sorted({p for p, _ in pack}), "textures": info["textures"], "kb": info["bytes"] // 1024})
        print(f"  [{n}/{len(jobs)}] {name}: {', '.join(models)} ({info['bytes'] // 1024} KB)")
        disc.pkgs = {p: v for p, v in disc.pkgs.items() if any(p == q for q, _ in pack)}   # bound the cache

    json.dump({"game": "SOCOM II (SCUS-97275)", "packs": catalog, "failed": failed},
              open(os.path.join(a.out, "catalog.json"), "w", encoding="utf-8"), indent=1)
    out = ["# SOCOM II character packs", "",
           f"{len(catalog)} packs, {total // 1024 // 1024} MB, ripped from the retail disc by "
           "`Mods/SOCOM2/bots/rip_socom2.py`. Each folder holds one memory-card archive `<NAME>.CDB` (what the game "
           "mounts) and its four libraries `CLIB_{TXR,PAL,GEO,MDL}.ZED`.", "",
           "## Using one", "",
           "1. Copy the pack to a memory card as `BASCUS-97275<NAME>/<NAME>.CDB` (`build_charpack.py` `install()`, or "
           "`rip_socom2.py --card <PCSX2 folder card>`). On a console: any card in slot 1 or 2.",
           "2. In a round: `python Mods/SOCOM2/bots/socom_bots.py loadpack <name> mc` (or `mem` to stream it from "
           "this PC over the bridge).",
           "3. Spawn: `python Mods/SOCOM2/bots/socom_bots.py ai <spawn name> team=0x40020000`.", "",
           "Each map load has a fixed heap budget - `loadpack` refuses a pack that will not fit.", "",
           "| Pack | Models | Spawn as | From | KB |", "|---|---|---|---|---|"]
    for c in catalog:
        spawn = ", ".join(f"`{s}`" for s in c["spawn_as"][:6]) + (f" (+{len(c['spawn_as']) - 6})"
                                                                 if len(c["spawn_as"]) > 6 else "")
        src = ", ".join(sorted({p for ps in c["from"].values() for p in ps}))
        out.append(f"| `{c['pack']}` | {', '.join(f'`{m}`' for m in c['models'])} | {spawn or '-'} | {src} | {c['kb']} |")
    if failed:
        out += ["", "## Not ripped", ""] + [f"- `{n}` {', '.join(m)}: {e}" for n, m, e in failed]
    open(os.path.join(a.out, "README.md"), "w", encoding="utf-8").write("\n".join(out) + "\n")
    print(f"done: {len(catalog)} packs ({total // 1024} KB), {len(failed)} failed -> {a.out}")


if __name__ == "__main__":
    main()
