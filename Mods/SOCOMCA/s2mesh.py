"""SOCOM II character mesh (MESH_<model> in CLIB_MDL.ZED): parse through its DMA chain, and encode.

MESH data = blocks, then the DMA chain at <model>_START:
    end   qwc=ref_count (the number of tags after it)
    per material section:
        cnt   qwc=0 addr=<name field> bits16-25=6        section start; name field = .tif name + NUL padded to 16
        per vertex batch:
            ref qwc=4 addr=<bone matrix_id> bits16-25=9  vif STCYCL 0x101, UNPACK V4-32 @0 n4  bone matrix -> VU 0..3
            ref qwc=n addr=<vertex block>   bits16-25=10  upper: bone, vertex count   (STCYCL 0x101, UNPACK V4-16 @4 ..)
        ref qwc=n addr=<draw block>         bits16-25=11  upper: 2 * vertex batches in the group, 0
The model node also carries <model>_START (chain offset), ref_count, mtx_count (26).
"""
import struct

import vif

STCYCL_101, UNPACK_M4 = 0x01000101, 0x6C048000          # STCYCL 0x101 / UNPACK V4-32 @0 n4 (bone matrix)
MSCNT = 0x17000000


def parse(data, start):
    """-> [{"texture": name, "groups": [{"vertex": [(bone, block bytes)], "draw": block bytes}]}]"""
    o = start
    lo = struct.unpack_from("<I", data, o)[0]
    count = lo & 0xFFFF
    sections, group, pending_bone, prev_end = [], None, None, 0
    for i in range(count):
        o += 16
        lo, addr, v0, v1 = struct.unpack_from("<4I", data, o)
        tid, qwc = (lo >> 28) & 7, lo & 0xFFFF
        if tid == 1:                                         # cnt: new section
            if sections:
                prev_end = sections[-1]["end"]
            sections.append({"texture": None, "name_field": None, "groups": [], "end": prev_end,
                             "cnt_field": (lo >> 16) & 0x3FF})
            group = None
        elif v0 == STCYCL_101 and v1 == UNPACK_M4:           # bone matrix
            pending_bone = addr
        else:
            block = data[addr:addr + 16 * qwc]
            sec = sections[-1]
            sec["end"] = max(sec["end"], addr + 16 * qwc)
            if sec["texture"] is None:                       # name + NUL padded to 16 (leftover bytes after the NUL)
                sec["name_field"] = data[prev_end:addr]
                sec["texture"] = sec["name_field"].split(b"\0")[0].decode("latin1")
            if pending_bone is not None:
                if group is None or group.get("draw") is not None:
                    group = {"vertex": [], "draw": None}
                    sec["groups"].append(group)
                group["vertex"].append((pending_bone, block))
                pending_bone = None
            else:
                group["draw"] = block
    return sections


def vertex_block(flags, tris, bone, verts):
    """verts: [(x, y, z, slot2, nx, ny, nz, w)] -> STCYCL 0x101, UNPACK V4-16 @4 (flg), header + verts, MSCNT, pad."""
    n = 1 + 2 * len(verts)
    body = struct.pack("<4h", flags, tris, bone, len(verts)) + b"".join(struct.pack("<8h", *v) for v in verts)
    out = struct.pack("<II", STCYCL_101, 0x6D008004 | (n & 0xFF) << 16) + body + struct.pack("<I", MSCNT)
    return out + bytes(-len(out) % 16)


def encode(sections, mtx_count=26):
    """-> (MESH data, _START offset, ref_count). sections as returned by parse()."""
    data, chain = bytearray(), []
    for sec in sections:
        chain.append(struct.pack("<4I", 0x10000000 | sec.get("cnt_field", 6) << 16, len(data), 0, 0))   # cnt -> name
        field = sec.get("name_field")
        if field is None:                                    # new mesh: name + NUL, padded to 16
            name = sec["texture"].encode("latin1") + bytes(1)
            field = name + bytes(-len(name) % 16)
        data += field
        for g in sec["groups"]:
            for bone, block in g["vertex"]:
                at = len(data)
                data += block
                chain.append(struct.pack("<4I", 0x30090004, bone, STCYCL_101, UNPACK_M4))
                flags, tris, vbone, count = struct.unpack_from("<4h", block, 8)
                chain.append(struct.pack("<4I", 0x300A0000 | len(block) // 16, at, vbone, count))
            if g["draw"] is not None:
                at = len(data)
                data += g["draw"]
                chain.append(struct.pack("<4I", 0x300B0000 | len(g["draw"]) // 16, at, 2 * len(g["vertex"]), 0))
    start = len(data)
    data += struct.pack("<4I", 0x70000000 | len(chain), 0, 0, 0) + b"".join(chain)
    return bytes(data), start, len(chain)
