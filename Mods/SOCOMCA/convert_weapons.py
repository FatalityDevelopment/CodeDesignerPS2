"""Convert every SOCOM: Combined Assault weapon into a SOCOM II weapon pack (WEAP library, weapon flags).

    python convert_weapons.py [--root "F:/SOCOM Packs/SOCOMCA/weapons"] [--max-size 64]

The weapons are not in the per-map data: GLOBAL.ZDB global_assets.rdr holds a second (nested) library with all of
them (Clib.nested(): 88 models, firepoint / firepoint_shell / aimpoint / Gun_box nodes like SOCOM II's). Each becomes
pack mcw<nn> with a wireframe preview.png; <root>/weapons.json lists pack, model name, triangles, size, check results.
Names are CRC32s: cracked from a weapon word list against each model's root and mesh node hashes.
"""
import argparse
import contextlib
import io
import itertools
import json
import os
import zlib

import ca2s2static as cs
import ca_archive as ca

WORDS = """m4a1 m4 m4a1_sd m16 m16a2 m16a4 ak47 ak74 aks74 aks74u ak101 ak103 m14 m21 m24 m40 m40a3 m82 m82a1 m107 sr25
sr25sd svd dragunov g3 g3a3 g3sg1 g36 g36c g36k hk416 hk417 hk21 m249 m60 m60e3 m60e4 m240 m240b pkm pkt rpk rpd mk43
mk46 mk48 mg3 mg42 m203 m79 mgl m32 at4 rpg rpg7 smaw m72 law stinger mp5 mp5sd mp5k mp7 ump ump45 p90 fnp90 mac10
mac11 mp9 tmp uzi bizon pp2000 m1014 m870 spas spas12 saiga saiga12 aa12 benelli mk23 mk23sd m9 m92 beretta deagle
desert_eagle glock glock17 glock18 sig226 sig_p226 hkp9s usp usp45 fiveseven five7 p9 makarov tt33 ots33 sig_commando
sig551 sig552 sig550 famas aug steyr_aug iw80 iw80a2 l85 l85a2 galil tar21 scar scar_h scar_l fal fnfal m4super90
m590 pancor jackhammer frag grenade m67 he_grenade flashbang smoke smokegrenade claymore satchel c4 m18 ammo knife
binoculars laser_designator designator radio scope silencer suppressor magazine bipod grip flashlight laser rifle
pistol shotgun smg sniper launcher rocket mortar mine""".split()
AFFIX = ([""] + "wep_ w_ weap_ weapon_".split(), [""] + "_3p _tp _world _hi _high _lo _low _sd _mag _clip".split())


def cracker():
    table = {}
    for w in WORDS:
        for pre, suf in itertools.product(*AFFIX):
            for v in {w, w.upper(), w.capitalize()}:
                table[zlib.crc32((pre + v + suf).encode()) & 0xFFFFFFFF] = pre + v + suf
    return table


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--root", default="F:/SOCOM Packs/SOCOMCA/weapons")
    ap.add_argument("--max-size", type=int, default=64)
    a = ap.parse_args()
    names = cracker()
    lib = None
    for arc, inner, z in ca.Disc().zdbs():
        if inner.upper().endswith("GLOBAL.ZDB"):
            lib = ca.Clib(ca.v6_resource(z, "global_assets.rdr", "ASST")).nested()
            break
    tpl = cs.template("weapon")
    os.makedirs(a.root, exist_ok=True)
    out = []
    for i, root in enumerate(lib.models):
        label = names.get(lib.u32(root + 4))                  # root name, else the first named mesh node
        def walk(n):
            nonlocal label
            if label is None and cs.node_meshes(lib, n):
                label = names.get(lib.u32(n + 4))
            for k in lib.vector(n + 0x50):
                walk(k)
        walk(root)
        model = ("ca_" + label if label else f"ca_w{lib.u32(root + 4):08x}")[:24]
        pack = f"mcw{i:02d}"
        log = io.StringIO()
        try:
            with contextlib.redirect_stdout(log):
                _, info = cs.convert(lib, i, model, pack, a.root, tpl, tpl["lib"], max_size=a.max_size, skip="auto")
        except Exception as e:                                # noqa: BLE001 - report and keep going
            print(f"{pack} {model}: FAILED {e}")
            continue
        problems = [l.strip() for l in log.getvalue().splitlines() if "PROBLEM" in l]
        lo, hi = info["bounds"]
        size = [round(hi[c] - lo[c], 2) for c in range(3)]
        out.append({"pack": pack, "model": model, "index": i, "root": f"{lib.u32(root + 4):08x}", "tris": info["tris"],
                    "textures": info["textures"], "size": size, "kb": info["kb"], "problems": problems})
        print(f"{pack} {model:26} {info['tris']:4} tris  size {size}  {'PROBLEMS: ' + '; '.join(problems) if problems else 'ok'}")
    json.dump(out, open(os.path.join(a.root, "weapons.json"), "w"), indent=1)


if __name__ == "__main__":
    main()
