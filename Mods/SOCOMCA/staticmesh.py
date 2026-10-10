"""SOCOM II static model chunks (WEAP/FLIB/map/vehicle libraries) - decoder for previews.

Mirrors CodeDesigner.Ps2.FileFormats ZedModelFile: a chunk starts 01000101 6C048000 00008000 (STCYCL 0x101, UNPACK
V4-32 @0 n4 header); +0x30 vertex count, +0x34 triangle count, +0x44 scale (float), +0x48 03 02 00 01; vertex records
(int16 x, y, z, w + 8 bytes) from +0x50; 8 bytes later the triangle table (a, b, c, flags) with indices x3.
"""
import struct

MAGIC = struct.pack("<III", 0x01000101, 0x6C048000, 0x00008000)


def chunks(data):
    """yield (offset, vertices [(x,y,z)], triangles [(a,b,c)]) for every chunk in a model's data."""
    o = 0
    while True:
        o = data.find(MAGIC, o)
        if o < 0 or o + 0x50 > len(data):
            return
        if o % 16 or data[o + 0x48:o + 0x4C] != b"\x03\x02\x00\x01":
            o += 4
            continue
        nv, nt = struct.unpack_from("<ii", data, o + 0x30)
        scale = struct.unpack_from("<f", data, o + 0x44)[0]
        end_v = o + 0x50 + 16 * nv
        if not (0 < nv <= 4096 and 0 <= nt <= 4096 and end_v + 8 + 4 * nt <= len(data)):
            o += 4
            continue
        verts = [tuple(c * scale for c in struct.unpack_from("<3h", data, o + 0x50 + 16 * i)) for i in range(nv)]
        tris = []
        for i in range(nt):
            a, b, c, _ = data[end_v + 8 + 4 * i:end_v + 12 + 4 * i]
            a, b, c = a // 3, b // 3, c // 3
            if max(a, b, c) < nv:
                tris.append((a, b, c))
        yield o, verts, tris
        o = end_v + 8 + 4 * nt


def triangles(data):
    """All chunk triangles as world-ish coordinate triples (no node transforms)."""
    out = []
    for _, v, t in chunks(data):
        out += [(v[a], v[b], v[c]) for a, b, c in t]
    return out
