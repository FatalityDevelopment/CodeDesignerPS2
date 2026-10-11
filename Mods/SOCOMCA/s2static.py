"""SOCOM II static model (WEAP/FLIB/map/vehicle libraries, MDL top node <model> + children N<node>_<visual>):
parse through its DMA chains, and encode.

A visual (GEO node <node>, visual <visual>) is drawn by the chain at offset N<node>_<visual>:
    refe  qwc=<tags after it>
    per material section:
        cnt   qwc=0  field 6  addr=<name field>         .tif name + NUL padded to 16
        ref   qwc=3  field 3  addr=<material block>      upper: STCYCL 0x101, UNPACK V4-32 @2 n2 (GS ALPHA/TEX1/TEST..)
        per chunk:
            ref qwc=n field 4 addr=<colour block>        upper: STCYCL 0x103, UNPACK V4-8 @6 n=<verts> (RGBA)
            ref qwc=n field 2 addr=<geometry chunk>      STCYCL 0x101 UNPACK V4-32 n4 (GIF tags, idx addr, 0, verts,
                                                         tris ... scale), STCYCL 0x203 UNPACK V4-16 @4 (vertices),
                                                         V4-8 triangles, V3-16 per triangle, MSCNT
Data layout per GEO node (N<node>_*): for each visual its geometry chunks then (material, name) per section; then for
each visual its colour blocks then its chain. Combined Assault static objects use the same geometry chunks.
"""
import struct

KIND = {6: "name", 3: "material", 4: "colour", 2: "geometry"}


def parse(mdl):
    """mdl: the MDL top node -> {child name: [section {name_field, material, mat_upper, chunks: [(colour, colour_upper,
    geometry)]}]} in child order."""
    d = mdl.data
    out = {}
    for c in mdl.children:
        o = struct.unpack("<I", c.data)[0]
        count = struct.unpack_from("<I", d, o)[0] & 0xFFFF
        sections, colour = [], None
        for i in range(count):
            o += 16
            lo, addr, v0, v1 = struct.unpack_from("<4I", d, o)
            kind, qwc = (lo >> 16) & 0x3FF, lo & 0xFFFF
            if kind == 6:
                sections.append({"name_field": None, "name_at": addr, "material": None, "mat_upper": None, "chunks": []})
            elif kind == 3:
                sections[-1]["material"] = d[addr:addr + 16 * qwc]
                sections[-1]["mat_upper"] = (v0, v1)
            elif kind == 4:
                colour = (d[addr:addr + 16 * qwc], (v0, v1))
            elif kind == 2:
                sections[-1]["chunks"].append((colour[0], colour[1], d[addr:addr + 16 * qwc]))
                colour = None
            else:
                raise ValueError(f"{mdl.name}/{c.name}: tag kind {kind}")
        for s in sections:                                     # name field: up to the next block (16 or 32 bytes)
            at = s.pop("name_at")
            end = min([b for b in block_starts(d, mdl) if b > at] + [len(d)])
            s["name_field"] = d[at:end]
        out[c.name] = sections
    return out


def block_starts(d, mdl):
    starts = set()
    for c in mdl.children:
        o = struct.unpack("<I", c.data)[0]
        starts.add(o)
        for i in range(struct.unpack_from("<I", d, o)[0] & 0xFFFF):
            o += 16
            starts.add(struct.unpack_from("<I", d, o + 4)[0])
    return starts


def name_field(texture):
    raw = texture.encode("latin1") + bytes(1)
    return raw + bytes(-len(raw) % 16)


def encode(visuals):
    """visuals: {"N<node>_<visual>": sections} (as parse()). -> (data, {child name: chain offset})."""
    data, offsets = bytearray(), {}
    by_node = {}
    for name in visuals:
        by_node.setdefault(name.split("_")[0], []).append(name)
    for node, names in by_node.items():
        placed = {}
        for name in names:                                    # geometry, then (material, name) per section
            geo = []
            for s in visuals[name]:
                for colour, cup, g in s["chunks"]:
                    geo.append(len(data))
                    data += g
            mats = []
            for s in visuals[name]:
                m_at = len(data)
                data += s["material"]
                n_at = len(data)
                data += s["name_field"]
                mats.append((m_at, n_at))
            placed[name] = (geo, mats)
        for name in names:                                    # colours, then the chain
            geo, mats = placed[name]
            tags, gi = [], 0
            for s, (m_at, n_at) in zip(visuals[name], mats):
                tags.append(struct.pack("<4I", 0x10060000, n_at, 0, 0))
                tags.append(struct.pack("<4I", 0x30030000 | len(s["material"]) // 16, m_at, *s["mat_upper"]))
                for colour, cup, g in s["chunks"]:
                    c_at = len(data)
                    data += colour
                    tags.append(struct.pack("<4I", 0x30040000 | len(colour) // 16, c_at, *cup))
                    tags.append(struct.pack("<4I", 0x30020000 | len(g) // 16, geo[gi], 0, 0))
                    gi += 1
            offsets[name] = len(data)
            data += struct.pack("<4I", len(tags), 0, 0, 0) + b"".join(tags)
    return bytes(data), offsets


# ---- authoring: triangle mesh -> geometry chunks -----------------------------------------------------------------
# One chunk (weapon convention, decoded from retail / CA weapons):
#   STCYCL 0x101; UNPACK V4-32 @0 n4: GIF tag (EOP, nloop 0), GIF tag (EOP, nloop = verts), (idx addr, 0, verts, tris),
#                                     (0, 0, 0, scale float)
#   STCYCL 0x203; UNPACK V4-16 @4 n=2*verts (flag): per vertex (x, y, z, nx) (u, v, ny, nz) - positions int16 *
#                                     scale / 32768, normals int16 * 32767, uv 4096 = 1.0; every 3rd qword is the colour
#   STCYCL 0x102; UNPACK V4-8 @idx n=tris (usn+flag): (a*3, b*3, c*3, 3)
#   STCYCL 0x102; UNPACK V3-16 @idx+1 n=tris (flag): face normal int16
#   MSCNT, NOP padding to a qword. idx = 4 + 3*verts. Keep verts <= 72, tris <= 48 (VU1 memory).
CHUNK_VERTS, CHUNK_TRIS = 72, 48


def _clamp16(x):
    return max(-32767, min(32767, int(round(x))))


def encode_chunk(verts, tris):
    """verts: [((x, y, z), (nx, ny, nz), (u, v))], tris: [(a, b, c)] -> one chunk (bytes, qword aligned)."""
    nv, nt = len(verts), len(tris)
    assert 0 < nv <= 127 and 0 < nt <= 255
    scale = max(max(abs(c) for c in p) for p, _, _ in verts) * 1.0001 or 1.0
    idx = 4 + 3 * nv
    out = bytearray(struct.pack("<II", 0x01000101, 0x6C048000))
    out += struct.pack("<4I", 0x8000, 0x303EC000, 0x412, 0)
    out += struct.pack("<4I", 0x8000 | nv, 0x303DC000, 0x412, 0)
    out += struct.pack("<4I", idx, 0, nv, nt)
    out += struct.pack("<3If", 0, 0, 0, scale)
    out += struct.pack("<II", 0x01000203, 0x6D000000 | (2 * nv) << 16 | 0x8004)
    for (x, y, z), (nx, ny, nz), (u, v) in verts:
        out += struct.pack("<8h", _clamp16(x / scale * 32768), _clamp16(y / scale * 32768),
                           _clamp16(z / scale * 32768), _clamp16(nx * 32767),
                           _clamp16(u * 4096), _clamp16(v * 4096), _clamp16(ny * 32767), _clamp16(nz * 32767))
    out += struct.pack("<II", 0x01000102, 0x6E000000 | nt << 16 | 0xC000 | idx)
    for a, b, c in tris:
        out += bytes((3 * a, 3 * b, 3 * c, 3))
    out += struct.pack("<II", 0x01000102, 0x69000000 | nt << 16 | 0x8000 | (idx + 1))
    for a, b, c in tris:
        p0, p1, p2 = verts[a][0], verts[b][0], verts[c][0]
        e1 = [p1[i] - p0[i] for i in range(3)]
        e2 = [p2[i] - p0[i] for i in range(3)]
        n = [e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0]]
        ln = sum(x * x for x in n) ** 0.5 or 1.0
        out += struct.pack("<3h", *(_clamp16(x / ln * 32766) for x in n))
    out += bytes(-len(out) % 4)
    out += struct.pack("<I", 0x17000000)                    # MSCNT
    out += bytes(-len(out) % 16)
    return bytes(out)


def encode_mesh(verts, tris):
    """Split a mesh into chunks (each with its own vertex subset) -> [chunk bytes, ...]."""
    chunks, i = [], 0
    while i < len(tris):
        remap, sub_v, sub_t = {}, [], []
        while i < len(tris) and len(sub_t) < CHUNK_TRIS:
            need = [k for k in tris[i] if k not in remap]
            if len(sub_v) + len(set(need)) > CHUNK_VERTS:
                break
            for k in tris[i]:
                if k not in remap:
                    remap[k] = len(sub_v)
                    sub_v.append(verts[k])
            sub_t.append(tuple(remap[k] for k in tris[i]))
            i += 1
        chunks.append(encode_chunk(sub_v, sub_t))
    return chunks
