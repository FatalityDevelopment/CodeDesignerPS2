"""Combined Assault static models (vehicles, turrets, props, global assets): node tree + mesh objects.

A model is a node tree (as characters: root 0x00839A2C, nodes 0x0083DC7C, +0x10 local 4x4, +0x50 children,
+0x98 mesh objects). A static mesh object (type 0x00841398): +0x1C data size, +0x30 vector of DMA chains and +0x40
vector of texture records - one (chain, texture) per material section. Chain: count tag, then tag kind 6 = material
(the texture's GS packet), then (kind 3 colour = V4-8 RGBA per vertex, kind 2 geometry = the same geometry chunk as
SOCOM II static models) pairs.
"""
import struct

import ca2s2
import charmesh as cm
import staticmesh


def objects(clib, model):
    """[(node index (pre-order incl. root), node, world matrix, object)]"""
    out = []
    nodes = [(model, None)]
    order = []

    def walk(n, parent):
        f = struct.unpack_from("<16f", clib.d, n + 0x10)
        m = cm.mat_mul([list(f[0:4]), list(f[4:8]), list(f[8:12]), list(f[12:16])], parent)
        order.append((n, m))
        for k in clib.vector(n + 0x50):
            walk(k, m)
    walk(model, ca2s2.I4)
    for i, (n, m) in enumerate(order):
        for obj in clib.vector(n + 0x98):
            out.append((i, n, m, obj))
    return out, order


def sections(clib, obj):
    """-> [(texture record or None, material bytes, [(colour bytes, colour upper, geometry bytes)])]
    Every material tag (kind 6) starts a section and takes the next texture of the object's list (+0x40), counting
    across chains: one chain can switch textures many times (the Humvee body: 1 chain, 10 materials, 10 textures)."""
    chains, textures = clib.vector(obj + 0x30), clib.vector(obj + 0x40)
    out, m = [], 0
    for o in chains:
        n = clib.u32(o) & 0xFFFF
        colour = None
        for i in range(n):
            o += 16
            lo, addr, v0, v1 = struct.unpack_from("<4I", clib.d, o)
            kind, qwc = (lo >> 16) & 0x3FF, lo & 0xFFFF
            blob = clib.d[addr:addr + 16 * qwc]
            if kind == 6:
                out.append((textures[m] if m < len(textures) else None, blob, []))
                m += 1
            elif kind == 3:
                colour = (blob, (v0, v1))
            elif kind == 2:
                if not out:                                   # geometry before any material
                    out.append((None, None, []))
                out[-1][2].append((colour[0] if colour else None, colour[1] if colour else None, blob))
                colour = None
    return out


def triangles(clib, model):
    """World-space triangles of every mesh object (for previews)."""
    objs, _ = objects(clib, model)
    out = []
    for i, n, m, obj in objs:
        for tex, mat, pairs in sections(clib, obj):
            for colour, cu, g in pairs:
                for t in staticmesh.triangles(g):
                    out.append(tuple(cm.transform(p, m) for p in t))
    return out
