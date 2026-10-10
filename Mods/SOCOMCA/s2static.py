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
