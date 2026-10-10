"""Character mesh decoder shared by SOCOM II (MESH_<model> in CLIB_MDL.ZED) and Combined Assault (clib mesh objects).

Format (see README.md): material sections, each a texture reference followed by VIF batches.
  vertex batch  UNPACK V4-16: header (flags, tris, bone, count) [+1 extra element in CA], then per vertex
                (x, y, z, slot*2) (nx, ny, nz, weight) - position relative to `bone`, weight Q15.
                flags: 2 first bone (overwrite), 1 add a bone, 5 add + finish, 6 single bone + finish.
  draw batch    UNPACK V4-8 triangles (a, b, c, 3) as slot*3; UNPACK V4-32 GIF tags + (idx addr, 0, verts, tris);
                UNPACK V2-16 texture coordinates (12-bit) for each slot.
decode() returns sections [{texture, batches: [{verts: [(slot, [(bone, (x,y,z), (nx,ny,nz), w)])], uv, tris}]}].
"""
import struct

import vif

KEEP = ("STCYCL", "MSCNT", "NOP", "UNPACK V4-8", "UNPACK V4-32", "UNPACK V2-16", "UNPACK V4-16")


def decode(data, start=0, end=None, ca=False):
    """Walk every material section between start and end. ca: CA layout (16-byte hash field, 2-element header)."""
    end = len(data) if end is None else end
    hdr = 2 if ca else 1
    sections, o = [], start
    while o < end - 8:
        if not (struct.unpack_from("<I", data, o)[0] == 0x01000101
                and (struct.unpack_from("<I", data, o + 4)[0] >> 24) & 0x7F == 0x6D):
            o += 16
            continue
        if ca:
            texture = struct.unpack_from("<I", data, o - 0x10)[0]
        else:
            texture = data[o - 0x20:o].split(b"\0")[0].decode("latin1")
        sec = {"texture": texture, "batches": []}
        slots, last = {}, o
        for off, name, imm, num, pay in vif.walk(data, o, end):
            if name not in KEEP:
                break
            last = off + 4 + len(pay)
            if name == "UNPACK V4-16":
                flags, tris, bone, count = struct.unpack_from("<4h", pay, 0)
                for i in range(count):
                    x, y, z, s2, nx, ny, nz, w = struct.unpack_from("<8h", pay, 8 * hdr + 16 * i)
                    slots.setdefault(s2 // 2, []).append((bone, (x, y, z), (nx, ny, nz), w))
            elif name == "UNPACK V4-8":
                tris = [tuple(b // 3 for b in pay[4 * i:4 * i + 3]) for i in range(num)]
            elif name == "UNPACK V2-16":
                uv = [struct.unpack_from("<2h", pay, 4 * i) for i in range(num)]
                sec["batches"].append({"verts": sorted(slots.items()), "uv": uv, "tris": tris})
                slots = {}
        sections.append(sec)
        o = max(o + 16, (last + 15) & ~15)
    return sections


def mat_mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def transform(p, m):
    """Row vector p (x, y, z, 1) times 4x4 row-major m."""
    return tuple(p[0] * m[0][j] + p[1] * m[1][j] + p[2] * m[2][j] + m[3][j] for j in range(3))


def world_vertices(batch, bone_world, scale):
    """[(x, y, z)] per slot: sum over bones of weight * (local position * bone world matrix)."""
    out = []
    for slot, parts in batch["verts"]:
        acc = [0.0, 0.0, 0.0]
        for bone, (x, y, z), _, w in parts:
            p = transform((x * scale, y * scale, z * scale), bone_world[bone])
            for k in range(3):
                acc[k] += p[k] * (w / 32767.0)
        out.append(tuple(acc))
    return out
