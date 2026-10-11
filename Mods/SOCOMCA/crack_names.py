"""Recover SOCOM: Combined Assault names from their CRC32 hashes by dictionary attack.

CA keeps only zlib.crc32(name) (case-sensitive): models by name, textures as "<name>.tif", character types by name.
The strings are not on the disc (not even compressed), so names are recovered by guessing: tokens from every SOCOM II
name (textures, models, chartypes - CA reuses the convention) plus CA theater/faction words, combined 1-3 at a time
with numbers and letter variants. CRC32 is extended from shared prefixes (zlib.crc32(b, crc(a)) = crc(a + b)).
A hit is a guess whose CRC32 equals a target: about 1 in 4 billion per guess by chance, so treat a lone hit with an odd
name as suspect.

    python crack_names.py                      # -> F:/SOCOM Packs/SOCOMCA/names.json (merged with earlier runs)
"""
import argparse
import json
import os
import re
import sys
import zlib

import ca_archive as ca

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "SOCOM2", "bots"))
import characters_readme as cr                               # noqa: E402

CA_WORDS = """us usa seal seals navy army marine marines nato ally allied friendly enemy terr ter terrorist rebel rebels
merc mercs mercenary pirate pirates pir pirat militia guerrilla insurgent soldier soldiers trooper commando sniper
spotter ghillie gunner rifleman grunt guard guards officer captain commander general leader boss chief vip hostage
pow prisoner civ civilian civilians woman women female male man men girl boy child kid doctor medic nurse worker
farmer villager scientist pilot driver crew sailor fisherman priest imam elder journalist reporter diplomat agent spy
poland polish pol morocco moroccan mor bangladesh bangla bang bng ban bd pakistan rus russian russia afg afghan alg
algeria algerian alb albania albanian braz brazil brazilian con congo chechen arab african asian euro european
desert woodland jungle arctic urban snow forest mountain tiger camo night day black blue green tan grey gray white
red brown olive khaki digital marpat flecktarn woodl dsrt wdl jng arc
head face facemap hair beard hat helmet cap beret mask goggle goggles balaclava hood scarf shirt torso chest vest
jacket coat sleeve sleave arm arms hand hands glove gloves palm leg legs pants boot boots shoe shoes belt pouch bag
pack backpack gear radio canteen holster knife ammo net rope skin body lo hi lod mp sp tr ia mission
bullfrog underwood jester specter spectre kobra cobra lynx boomer vulture""".split()


def targets():
    """{hash: kind} for every CA model, texture and character type."""
    out = {}
    disc = ca.Disc()
    for mission, data in disc.clibs():
        c = ca.Clib(data)
        for m in c.models:
            out[c.u32(m + 4)] = "model"
        for t in c.textures:
            out[c.u32(t + 0x4C)] = "texture"
    import struct
    for arc, inner, d in disc.zdbs():
        r = ca.v6_resource(d, "character.rdr")
        if not r or r[:4] != b"rdr\\":
            continue
        for o in [0x110] + [struct.unpack_from("<I", r, x)[0] for x in range(0x3C, 0x110, 4)]:
            if o + 8 <= len(r):
                out.setdefault(struct.unpack_from("<I", r, o)[0], "chartype")
    return out


def vocabulary():
    """(first tokens, all tokens, last tokens) from SOCOM II names + CA words."""
    names = set()
    iso = open("F:/SOCOM II - U.S. Navy SEALs.iso", "rb")
    for path, (size, lba) in cr.iso_listing(iso).items():
        if not path.endswith(".ZDB") or "/RUN/" not in path:
            continue
        try:
            ent = cr.zdb_entries(iso, size, lba)
        except Exception:
            continue
        for k, v in ent.items():
            if k.upper().endswith(".ZED"):
                names.update(m.group(1).decode() for m in re.finditer(rb"([A-Za-z][A-Za-z0-9_\-]+)\.tif\0", v))
                names.update(m.group(1).decode() for m in re.finditer(rb"MESH_([A-Za-z][A-Za-z0-9_\-]+)\0", v))
    for line in open(os.path.join(HERE, "..", "SOCOM2", "bots", "chartypes.tsv"), encoding="utf-8"):
        names.update(x for x in line.split() if x)
    first, mid, last = set(), set(), set()
    for n in names:
        toks = [t for t in n.split("_") if t]
        if not toks:
            continue
        first.add(toks[0])
        last.add(toks[-1])
        mid.update(toks)
    for w in CA_WORDS:
        first.add(w), mid.add(w), last.add(w)
    return names, sorted(first), sorted(mid), sorted(last)


def numbers():
    out = [""] + [str(i) for i in range(10)] + [f"{i:02d}" for i in range(1, 21)] + list("ABCDEFGH") + list("abcdefgh")
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--out", default="F:/SOCOM Packs/SOCOMCA/names.json")
    ap.add_argument("--depth", type=int, default=3, help="max tokens per name (3 = slower, wider)")
    a = ap.parse_args()
    want = targets()
    found = json.load(open(a.out)) if os.path.exists(a.out) else {}
    names, first, mid, last = vocabulary()
    print(f"{len(want)} target hashes ({sum(1 for v in want.values() if v == 'model')} models, "
          f"{sum(1 for v in want.values() if v == 'texture')} textures, "
          f"{sum(1 for v in want.values() if v == 'chartype')} chartypes); vocabulary {len(first)}/{len(mid)}/{len(last)}")
    tif = zlib.crc32(b".tif")

    def check(crc, name, tail_crcs):
        # name itself, and name + ".tif"
        if crc in want:
            found.setdefault(f"{crc:08x}", {"name": name, "kind": want[crc]})
        t = zlib.crc32(b".tif", crc)
        if t in want:
            found.setdefault(f"{t:08x}", {"name": name + ".tif", "kind": want[t]})

    nums = numbers()
    seps = ["_", ""]
    count = 0
    # exact SOCOM II names (and case variants)
    for n in names:
        for v in {n, n.lower()}:
            check(zlib.crc32(v.encode()), v, None)
            count += 1
    # token combinations: F[sep N] / F_M[sep N] / F_M_L[sep N]
    for f in first:
        cf = zlib.crc32(f.encode())
        for num in nums:
            for s in (seps if num else [""]):
                check(zlib.crc32((s + num).encode(), cf), f + s + num, None)
                count += 1
        for m in mid:
            cfm = zlib.crc32(("_" + m).encode(), cf)
            for num in nums:
                for s in (seps if num else [""]):
                    check(zlib.crc32((s + num).encode(), cfm), f"{f}_{m}{s}{num}", None)
                    count += 1
            if a.depth >= 3:
                for l in last:
                    cfml = zlib.crc32(("_" + l).encode(), cfm)
                    check(cfml, f"{f}_{m}_{l}", None)
                    count += 1
                    for num in ("1", "2", "01", "02", "A", "B"):
                        check(zlib.crc32(num.encode(), cfml), f"{f}_{m}_{l}{num}", None)
                        check(zlib.crc32(("_" + num).encode(), cfml), f"{f}_{m}_{l}_{num}", None)
                        count += 2
    json.dump(found, open(a.out, "w"), indent=1, sort_keys=True)
    kinds = {}
    for v in found.values():
        kinds[v["kind"]] = kinds.get(v["kind"], 0) + 1
    print(f"{count:,} guesses; names known: {kinds} -> {a.out}")
    for h, v in sorted(found.items(), key=lambda x: (x[1]["kind"], x[1]["name"])):
        print(f"  {h} {v['kind']:9} {v['name']}")


if __name__ == "__main__":
    main()
