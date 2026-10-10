"""Rip every SOCOM: Combined Assault character texture (with its palette) to PNG, one file per unique texture hash.

Read-only on the ISO. Writes <out>/<hash>.png and <out>/index.json ({hash: size, format, palette hash, missions}).

    python rip_textures.py                                  # -> F:/SOCOM Packs/SOCOMCA/textures
    python rip_textures.py --mission SP411                  # one mission only
"""
import argparse
import json
import os
import struct
import zlib

import ca_archive as ca


def png(path, w, h, rgb):
    """Minimal RGB PNG (no external image library)."""
    raw = b"".join(b"\0" + rgb[y * w * 3:(y + 1) * w * 3] for y in range(h))

    def chunk(tag, body):
        return struct.pack(">I", len(body)) + tag + body + struct.pack(">I", zlib.crc32(tag + body) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def rgb16(c):
    """PSMCT16 (A1 B5 G5 R5) -> RGB bytes."""
    return bytes((((c & 31) * 255) // 31, (((c >> 5) & 31) * 255) // 31, (((c >> 10) & 31) * 255) // 31))


def csm1(colours):
    """GS CSM1 palette storage swaps entries 8-15 and 16-23 in every block of 32 - undo it."""
    if len(colours) < 256:
        return colours
    return [colours[(i & ~0x18) | ((i & 0x08) << 1) | ((i & 0x10) >> 1)] for i in range(256)]


def decode(clib, rec):
    t = clib.texture(rec)
    w, h, psm, pix = t["width"], t["height"], t["psm"], t["pixels"]
    pal_hash = 0
    if psm in (0x13, 0x14):
        pal_hash, colours = clib.palette(t["palette"])
        lut = [rgb16(c) for c in (csm1(colours) if psm == 0x13 else colours[:16])]
        if psm == 0x13:
            rgb = b"".join(lut[p] for p in pix)
        else:
            rgb = b"".join(lut[b & 15] + lut[b >> 4] for b in pix)
    elif psm == 0x02:
        rgb = b"".join(rgb16(c) for c in struct.unpack(f"<{w * h}H", pix))
    else:
        rgb = b"".join(pix[i:i + 3] for i in range(0, len(pix), 4))
    return t, pal_hash, rgb


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--iso", default=ca.ISO)
    ap.add_argument("--out", default="F:/SOCOM Packs/SOCOMCA/textures")
    ap.add_argument("--mission", help="only this mission (e.g. SP411)")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    index, failed = {}, []
    disc = ca.Disc(a.iso)
    for mission, data in disc.clibs():
        if a.mission and mission != a.mission.upper():
            continue
        clib = ca.Clib(data)
        new = 0
        for rec in clib.textures:
            try:
                h = clib.u32(rec + 0x4C)
                if f"{h:08x}" not in index:
                    t, pal_hash, rgb = decode(clib, rec)
                    png(os.path.join(a.out, f"{h:08x}.png"), t["width"], t["height"], rgb)
                    index[f"{h:08x}"] = {"width": t["width"], "height": t["height"], "psm": t["psm"],
                                         "palette": f"{pal_hash:08x}", "missions": []}
                    new += 1
                index[f"{h:08x}"]["missions"].append(mission)
            except Exception as e:                                     # keep going, report at the end
                failed.append((mission, hex(rec), str(e)))
        print(f"  {mission:12} {len(clib.textures):3} textures, {new} new")
    json.dump({"textures": index, "failed": failed}, open(os.path.join(a.out, "index.json"), "w"), indent=1)
    print(f"done: {len(index)} unique textures, {len(failed)} failed -> {a.out}")


if __name__ == "__main__":
    main()
