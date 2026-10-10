"""Convert every SOCOM: Combined Assault vehicle (VEH_*.ZDB) into a SOCOM II map-prop pack.

    python convert_vehicles.py [--root "F:/SOCOM Packs/SOCOMCA/static"] [--max-size 32] [--bright 0xA0]

Per vehicle file the full model (most triangles) becomes pack mcv100, mcv101 ... (model ca_<vehicle>), with LOD
copies / hull / rotor blur cards left out (ca2s2static). Writes <root>/vehicles.json: pack, model, source,
triangles, VRAM blocks (a fresh map's prop pool has ~0xBA free), size, offline-check problems.
In game: socom_bots.py testprop <pack> <model>  (or loadpack <pack> mem + spawn_prop).
"""
import argparse
import contextlib
import io
import json
import os

import ca2s2static as cs
import ca_archive as ca


def triangles(clib, model):
    total = 0
    def walk(n):
        nonlocal total
        m = cs.node_meshes(clib, n)
        total += m[1] if m else 0
        for k in clib.vector(n + 0x50):
            walk(k)
    walk(model)
    return total


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--root", default="F:/SOCOM Packs/SOCOMCA/static")
    ap.add_argument("--max-size", type=int, default=32)
    ap.add_argument("--bright", type=lambda x: int(x, 0), default=0xA0)
    ap.add_argument("--first", type=int, default=100, help="first pack number (mcv<n>)")
    a = ap.parse_args()
    tpl = cs.template("prop")
    seen, out, n = set(), [], a.first
    for arc, inner, z in ca.Disc().zdbs():
        zdb = inner.replace("\\", "/").split("/")[-1]
        if not zdb.upper().startswith("VEH_"):
            continue
        for name, tag, version, off, size in ca.v6_entries(z):
            if tag != "ASST" or name in seen:
                continue
            seen.add(name)
            clib = ca.Clib(ca.v6_resource(z, name, "ASST"))
            counts = [triangles(clib, m) for m in clib.models]
            mi = counts.index(max(counts))
            model = ("ca_" + name.replace("veh_", "").replace(".rdr", ""))[:24]
            pack, n = f"mcv{n}", n + 1
            log = io.StringIO()
            try:
                with contextlib.redirect_stdout(log):
                    _, info = cs.convert(clib, mi, model, pack, a.root, tpl, tpl["lib"], max_size=a.max_size,
                                         bright=a.bright, skip="auto")
            except Exception as e:                            # noqa: BLE001 - report and keep going
                print(f"{pack} {model}: FAILED {e}")
                continue
            problems = [l.strip() for l in log.getvalue().splitlines() if "PROBLEM" in l]
            lo, hi = info["bounds"]
            out.append({"pack": pack, "model": model, "source": f"{zdb}/{name}#{mi}", "tris": info["tris"],
                        "vram": info["vram"], "kb": info["kb"], "height": round(hi[1] - lo[1], 1),
                        "problems": problems})
            print(f"{pack} {model:26} {info['tris']:6} tris  VRAM {info['vram']:#5x}  {info['kb']:4} KB  "
                  f"{'PROBLEMS: ' + '; '.join(problems) if problems else 'ok'}")
    json.dump(out, open(os.path.join(a.root, "vehicles.json"), "w"), indent=1)


if __name__ == "__main__":
    main()
