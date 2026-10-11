"""Replace the SOCOM II boot splash (SplashLogo.tif in RUN/UI/UIBF_TXR.ZED) with any PNG.

    python splash_logo.py logo.png [--txr E:/RUN/UI/UIBF_TXR.ZED] [--out Mods/SOCOM2/splash]

SplashLogo.tif is 512x256, 32-bit RGBA (no palette, so UIBF_PAL.ZED is unchanged), rows stored bottom-up, PS2
alpha (0x80 = opaque; the retail image peaks at 0x7F). The PNG is scaled to fit 512x256 keeping its aspect and
centred on a transparent background. Writes <out>/UIBF_TXR.ZED and <out>/splash_preview.png; the input ZED is
never modified.
"""
import argparse
import os
import struct
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "bots"))
sys.path.insert(0, os.path.join(HERE, "..", "SOCOMCA"))

import build_charpack as bp                                   # noqa: E402
import rip_textures as rt                                     # noqa: E402

W, H = 512, 256


def read_png(path):
    """8-bit RGB / RGBA non-interlaced PNG -> (w, h, RGBA bytes)."""
    d = open(path, "rb").read()
    if d[:8] != b"\x89PNG\r\n\x1a\n":
        sys.exit(f"{path} is not a PNG")
    o, idat, w = 8, b"", 0
    while o < len(d):
        ln, tag = struct.unpack_from(">I4s", d, o)
        body = d[o + 8:o + 8 + ln]
        if tag == b"IHDR":
            w, h, depth, ctype, _, _, inter = struct.unpack(">IIBBBBB", body)
            if depth != 8 or ctype not in (2, 6) or inter:
                sys.exit(f"{path}: only 8-bit RGB/RGBA non-interlaced PNGs (got depth {depth}, type {ctype})")
        elif tag == b"IDAT":
            idat += body
        o += 12 + ln
    bpp = 4 if ctype == 6 else 3
    raw, stride = zlib.decompress(idat), w * bpp
    out, prev = bytearray(), bytearray(stride)
    for y in range(h):
        f, line = raw[y * (stride + 1)], bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for i in range(stride):
            a = line[i - bpp] if i >= bpp else 0
            b, c = prev[i], prev[i - bpp] if i >= bpp else 0
            if f == 1:
                line[i] = (line[i] + a) & 255
            elif f == 2:
                line[i] = (line[i] + b) & 255
            elif f == 3:
                line[i] = (line[i] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        out += line
        prev = line
    if bpp == 3:
        out = bytearray(b"".join(bytes(out[i:i + 3]) + b"\xff" for i in range(0, len(out), 3)))
    return w, h, bytes(out)


def fit(w, h, rgba):
    """Box-filter the image into a W x H canvas, aspect kept, centred, transparent around it -> RGBA rows top-down."""
    sc = min(W / w, H / h)
    tw, th = max(1, round(w * sc)), max(1, round(h * sc))
    x0, y0 = (W - tw) // 2, (H - th) // 2
    canvas = bytearray(W * H * 4)
    for ty in range(th):
        sy0, sy1 = ty * h // th, max(ty * h // th + 1, (ty + 1) * h // th)
        for tx in range(tw):
            sx0, sx1 = tx * w // tw, max(tx * w // tw + 1, (tx + 1) * w // tw)
            acc, n = [0, 0, 0, 0], 0
            for sy in range(sy0, sy1):
                for sx in range(sx0, sx1):
                    p = (sy * w + sx) * 4
                    a = rgba[p + 3]
                    for k in range(3):
                        acc[k] += rgba[p + k] * a           # alpha-weighted colour: no dark fringes
                    acc[3] += a
                    n += 1
            o = ((y0 + ty) * W + x0 + tx) * 4
            if acc[3]:
                canvas[o:o + 3] = bytes(min(255, acc[k] // acc[3]) for k in range(3))
            canvas[o + 3] = acc[3] // n
    return bytes(canvas)


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("png")
    ap.add_argument("--txr", default="E:/RUN/UI/UIBF_TXR.ZED")
    ap.add_argument("--out", default=os.path.join(HERE, "splash"))
    a = ap.parse_args()
    header, nodes = bp.zed_read(open(a.txr, "rb").read())
    tex = next(t for t in next(n for n in nodes if n.name == "textures").children if t.name == "SplashLogo.tif")
    texdat = next(c for c in tex.children if c.name == "texdat")
    head = texdat.data[:0xA0]
    if struct.unpack_from("<HH", head, 0) != (W, H) or head[0x0C] != 32:
        sys.exit("SplashLogo.tif is not the 512x256 32-bit texture this tool expects")
    img = fit(*read_png(a.png))
    rows = [img[y * W * 4:(y + 1) * W * 4] for y in range(H)]
    ps2 = bytearray()
    for row in reversed(rows):                                # stored bottom-up
        for i in range(0, len(row), 4):
            ps2 += bytes((row[i], row[i + 1], row[i + 2], row[i + 3] >> 1))   # 0..255 -> 0..0x7F
    texdat.data = head + bytes(ps2)
    blob = bp.zed_write(header, nodes)
    os.makedirs(a.out, exist_ok=True)
    open(os.path.join(a.out, "UIBF_TXR.ZED"), "wb").write(blob)
    rt.png(os.path.join(a.out, "splash_preview.png"), W, H,
           b"".join(bytes(c * img[i + 3] // 255 for c in img[i:i + 3]) for i in range(0, len(img), 4)))
    print(f"{a.out}/UIBF_TXR.ZED ({len(blob)} bytes; the original is {os.path.getsize(a.txr)}) + splash_preview.png")


if __name__ == "__main__":
    main()
