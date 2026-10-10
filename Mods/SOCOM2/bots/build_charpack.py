"""Build slim character libraries ("packs"): a few character models picked from any map packages, packed into one
CLIB_{TXR,PAL,GEO,MDL}.ZED set that loads like any map's clib but costs a fraction of the heap.

Read-only on the ISO. Writes Mods/SOCOM2/packs/<name>/ (git-ignored: it is retail data).

    python build_charpack.py --all                 # every pack in PRESETS (mhost, mvips, mpow1, ...)
    python build_charpack.py --out mhost           # one preset
    python build_charpack.py --all --card "C:/Users/<you>/OneDrive/Documents/PCSX2/memcards/test.ps2"   # + install
    python build_charpack.py --out mtest --models MP82:rus_escort01,M62:braz_coke01      # any other combination
    python build_charpack.py --iso "F:/SOCOM II - U.S. Navy SEALs.iso" ...

Besides the four ZEDs it writes <NAME>.CDB, the archive the game itself mounts from a memory card (see cdb_write),
which socom_bots.py loadpack loads either from the card (retail-safe) or straight from this PC over the bridge.

ZED layout (header 0x64, string table, 16-byte records {name, rel, size, type}, 0x10-aligned body), where a record's
`type` is its child count and the records are a pre-order tree; the root word holds the top-level node count:
    TXR  renderphase, textures(N) -> <name>.tif(1) -> texdat           root 2
    PAL  palettes(N) -> texpal_<id>(2) -> par, buf                       root 1
    GEO  models(N) -> <model>(...) skeleton/visual subtree               root 1
    MDL  MESH_<model>(3) -> MESH_<model>_START, ref_count, mtx_count     root = MESH count
A MESH names its textures (<name>.tif strings in its data). A texture names its palette in the GS TEX0 register in
the texdat's register tail (CBP, bits 37..50 = palette id); `par` word 0 repeats the id. Palette ids are renumbered
from PAL_BASE (above every id on the disc, 0..594) so the pack can never share an id with a map's own libraries.
"""
import argparse
import os
import re
import struct
import sys

import characters_readme as cr

HERE = os.path.dirname(os.path.abspath(__file__))
BS = chr(92)
PAL_BASE = 600

# The pack set: grouped where a game mode spawns them together, one archive per character otherwise.
# name -> [(package, model)]; names start with 'm' and are at most 7 characters (see check_pack_name)
PRESETS = {
    "mhost": [("MP53", "alg_UN01"),        # mp_algerian_h1   escort hostages (also MP52 / MP71)
              ("MP53", "alg_UN02"),        # mp_algerian_h2
              ("MP53", "alg_UNworker")],   # mp_algerian_h3
    "mvips": [("MP82", "rus_escort01"),    # mp_vip1          Russian VIPs
              ("MP82", "rus_escort03")],   # mp_vip2, mp_vip3
    "mpow1": [("MP10", "con_pow01")],      # mp_pow1, mp_pow3 POWs (also MP11)
    "mpow2": [("MP10", "con_pow02")],      # mp_pow2
    "mbrzl": [("MP61", "braz_leader")],    # brazilian_leader, mp61_terror4, m61_leader2, M63_Quixada
    "mcoke": [("M62", "braz_coke01")],     # brazilian_coke_worker01
    "malbn": [("M53", "alban_leader02")],  # albanian_leader02, m53_Aide
}


# ---- ZED container (mirrors CodeDesigner.Ps2.FileFormats ZedContainer, byte-compatible) ----------------------------

class Node:
    def __init__(self, name, data=b"", children=None):
        self.name, self.data, self.children = name, data, children if children is not None else []


def zed_read(data):
    """-> (header words {0x0C string base, 0x10, 0x58, 0x5C, 0x60}, [top-level Node])"""
    slots, st_size, base = struct.unpack_from("<III", data, 0x04)
    first = 0x64 + st_size + 0x10
    body = len(data) - struct.unpack_from("<I", data, 0x54)[0]
    root = struct.unpack_from("<I", data, first - 4)[0]
    flat = []
    for i in range(slots - 1):
        name_ptr, rel, size, typ = struct.unpack_from("<IIII", data, first + 16 * i)
        so = 0x64 + name_ptr - base
        flat.append((data[so:data.index(b"\0", so)].decode("latin1"), typ, data[body + rel:body + rel + size]))
    pos = 0

    def take():
        nonlocal pos
        name, typ, blob = flat[pos]
        pos += 1
        return Node(name, blob, [take() for _ in range(typ)])

    tops = [take() for _ in range(root)]
    if pos != len(flat):
        raise ValueError(f"tree walk used {pos} of {len(flat)} records")
    return {o: struct.unpack_from("<I", data, o)[0] for o in (0x0C, 0x10, 0x58, 0x5C, 0x60)}, tops


def zed_write(header, tops, align=0x10):
    base = header[0x0C]
    flat = []

    def walk(n):
        flat.append(n)
        for c in n.children:
            walk(c)

    for t in tops:
        walk(t)
    names, table = {}, bytearray()
    for n in flat:
        if n.name not in names:
            names[n.name] = len(table)
            table += n.name.encode("latin1") + b"\0"
    first = 0x64 + len(table) + 0x10
    body_start = (first + 16 * len(flat) + align - 1) & ~(align - 1)
    offs, cur = [], 0
    for n in flat:
        if n.data:
            cur = (cur + align - 1) & ~(align - 1)
            offs.append(cur)
            cur += len(n.data)
        else:
            offs.append(0)
    body_size = (cur + align - 1) & ~(align - 1)
    out = bytearray(body_start + body_size)
    out[first + 16 * len(flat):] = b"\xAF" * (len(out) - first - 16 * len(flat))
    struct.pack_into("<III", out, 0x04, len(flat) + 1, len(table), base)
    for o, v in header.items():
        struct.pack_into("<I", out, o, v)
    struct.pack_into("<I", out, 0x54, body_size)
    out[0x64:0x64 + len(table)] = table
    struct.pack_into("<I", out, first - 4, len(tops))
    for i, n in enumerate(flat):
        struct.pack_into("<IIII", out, first + 16 * i, base + names[n.name], offs[i], len(n.data), len(n.children))
        if n.data:
            out[body_start + offs[i]:body_start + offs[i] + len(n.data)] = n.data
    return bytes(out)


# ---- texture / palette helpers ---------------------------------------------------------------------------------------

def tex0_offset(texdat):
    """Offset of the TEX0 register value in a texdat (0x20 header, pixels, GIF register tail)."""
    tail = 0x20 + struct.unpack_from("<I", texdat, 4)[0]
    for o in range(tail, len(texdat) - 15, 16):
        if struct.unpack_from("<Q", texdat, o + 8)[0] == 0x06:            # register address 0x06 = TEX0_1
            return o
    raise ValueError("texdat has no TEX0 register")


def tex_palette(texdat):
    return (struct.unpack_from("<Q", texdat, tex0_offset(texdat))[0] >> 37) & 0x3FFF


def set_tex_palette(texdat, pal_id):
    b = bytearray(texdat)
    o = tex0_offset(b)
    v = struct.unpack_from("<Q", b, o)[0]
    struct.pack_into("<Q", b, o, (v & ~(0x3FFF << 37)) | (pal_id << 37))
    return bytes(b)


def pixels(texdat):
    return texdat[0x20:0x20 + struct.unpack_from("<I", texdat, 4)[0]]


def mesh_textures(mesh):
    """<name>.tif strings in a MESH. Names sit in fixed-size fields, so a field can still hold the tail of an older,
    longer name after the terminator ("congo_merc01_ear.tif\0ck.tif"): a match that starts right after another
    name's ".tif\0" is such a leftover."""
    return [m.group().decode("latin1") for m in re.finditer(rb"[A-Za-z0-9_\-]+\.tif", mesh)
            if not (m.start() >= 5 and mesh[m.start() - 5:m.start()] == b".tif" + bytes(1))]


# ---- build ------------------------------------------------------------------------------------------------------------

def load_package(iso, listing, pkg):
    """pkg = a map package (MP53, M62: its character library), or "<ZDB>:<DIR>/<LIB>" for any library, e.g.
    "M51:COMMON/WEAP" (weapons), "MP61:MP/MP61/FLIB" (gear), "MP61:MP/MP61/MP61" (the map's props)."""
    if ":" in pkg:
        zdb, path = pkg.split(":", 1)
        ent = cr.zdb_entries(iso, *listing[f"/RUN/{zdb}.ZDB"])
        stem = "RUN" + BS + path.replace("/", BS) + "_"
        out = {part: zed_read(ent[f"{stem}{part}.ZED"]) for part in ("TXR", "PAL", "GEO", "MDL")
               if f"{stem}{part}.ZED" in ent}
        if "PAL" not in out and "TXR" in out:                # OBJT has no palette file: an empty one
            out["PAL"] = (dict(out["TXR"][0]), [Node("palettes", children=[])])
        return out
    size, lba = listing[f"/RUN/{pkg}.ZDB"]
    ent = cr.zdb_entries(iso, size, lba)
    kind = "MP" if pkg.startswith("MP") else "SP"
    return {part: zed_read(ent[f"RUN\\{kind}\\{pkg}\\CLIB_{part}.ZED"]) for part in ("TXR", "PAL", "GEO", "MDL")}


class BuildError(Exception):
    pass


class Disc:
    """The ISO with its file listing and a cache of loaded character libraries (one read per package)."""
    def __init__(self, iso_path):
        self.iso = open(iso_path, "rb")
        self.listing = cr.iso_listing(self.iso)
        self.pkgs = {}

    def package(self, pkg):
        if pkg not in self.pkgs:
            self.pkgs[pkg] = load_package(self.iso, self.listing, pkg)
        return self.pkgs[pkg]


def build(disc, pack, out_name, out_root=None, verbose=True, lib_name="CLIB", mesh_prefix="MESH_", fallback=(),
          allow_missing=False):
    """Pack the (package, model) list into <out_root>/<out_name>/: <LIB>_{TXR,PAL,GEO,MDL}.ZED + <NAME>.CDB.
    lib_name: the library's file name (CLIB characters, WEAP weapons, FLIB gear, ...); mesh_prefix: how its MDL names
    a model ("MESH_" + LOD copies for characters, "" = exactly the model name for static models).
    fallback: other library keys to take a texture (+ palette) from when the model's own TXR lacks it (WEAP c4 uses
    the map FLIB's satchel textures). A name found nowhere is retried without leading characters (the scan can pick
    up bytes before a static model's name field: "dimessy_ground_detail.tif" = "messy_ground_detail.tif");
    allow_missing: skip a texture found nowhere (listed in the result's "missing") instead of failing.
    disc: a Disc or an ISO path. Returns (CDB path, {"meshes", "textures", "palettes", "bytes"})."""
    if isinstance(disc, str):
        disc = Disc(disc)
    pkgs = {p: disc.package(p) for p in sorted({p for p, _ in pack})}
    first = pkgs[pack[0][0]]

    meshes, geos, textures, palettes, missing = [], [], {}, [], []
    pal_map = {}                                                     # (pkg, old id) -> new id
    for pkg, model in pack:
        lib = pkgs[pkg]
        if mesh_prefix:
            variant = re.compile(rf"{mesh_prefix}{re.escape(model)}(_[12])?(_LO)?$")
            picked = [m for m in lib["MDL"][1] if variant.match(m.name)]
        else:
            picked = [m for m in lib["MDL"][1] if m.name == model][:1]
        if not picked:
            raise BuildError(f"{pkg}: no {mesh_prefix}{model} in its MDL")
        geo_models = {g.name: g for g in lib["GEO"][1][0].children}
        tex_nodes = {t.name: t for t in next(n for n in lib["TXR"][1] if n.name == "textures").children}
        pal_nodes = {n.name: n for n in lib["PAL"][1][0].children}
        for mesh in picked:
            mname = mesh.name[len(mesh_prefix):]
            if mname not in geo_models:
                raise BuildError(f"{pkg}: {mname} has a MESH but no GEO model")
            meshes.append(mesh)
            geos.append(geo_models[mname])
            for ref in mesh_textures(mesh.data):
                found = None
                for tif in [ref[i:] for i in range(0, len(ref) - 5)]:          # as scanned, then trimmed
                    if tif in tex_nodes:
                        found = (pkg, tex_nodes, pal_nodes)
                        break
                    for fk in fallback:
                        flib = disc.package(fk)
                        ft = {t.name: t for t in next(n for n in flib["TXR"][1] if n.name == "textures").children}
                        if tif in ft:
                            found = (fk, ft, {n.name: n for n in flib["PAL"][1][0].children})
                            break
                    if found:
                        break
                if not found:
                    if allow_missing:
                        missing.append(ref)
                        continue
                    raise BuildError(f"{pkg}: {mname} uses {ref}, not in its TXR")
                tpkg, tnodes, pnodes = found
                src = tnodes[tif].children[0].data
                psm = (struct.unpack_from("<Q", src, tex0_offset(src))[0] >> 20) & 0x3F
                if psm not in (0x13, 0x14):                          # true colour (5 PSMCT16 skins on the disc):
                    look = (src[:8], pixels(src), b"")               # no palette to carry or renumber
                    if tif in textures:
                        if textures[tif][2] != look and verbose:
                            print(f"  warning: {tif} differs between {textures[tif][0]} and {pkg}; keeping the first")
                        continue
                    textures[tif] = (pkg, Node(tif, children=[Node("texdat", src)]), look)
                    continue
                old = tex_palette(src)
                pnode = pnodes.get(f"texpal_{old}")
                if pnode is None:                                    # the texture's palette lives in another
                    for fk in fallback:                              # library (BCAR humvee -> texpal_104)
                        fp = {n.name: n for n in disc.package(fk)["PAL"][1][0].children}
                        if f"texpal_{old}" in fp:
                            pnode, tpkg = fp[f"texpal_{old}"], fk
                            break
                    else:
                        raise BuildError(f"{pkg}: {mname}: palette {old} of {tif} not found")
                look = (src[:8], pixels(src), pnode.children[1].data)
                if tif in textures:                                  # shared between packages (MP61/M62 Brazil gear)
                    if textures[tif][2] != look and verbose:
                        print(f"  warning: {tif} differs between {textures[tif][0]} and {pkg}; keeping the first")
                    continue
                if (tpkg, old) not in pal_map:                        # one palette can serve several textures
                    new = PAL_BASE + len(pal_map)
                    pal_map[(tpkg, old)] = new
                    par = bytearray(pnode.children[0].data)
                    struct.pack_into("<I", par, 0, new)
                    palettes.append(Node(f"texpal_{new}", children=[Node("par", bytes(par)), pnode.children[1]]))
                texdat = set_tex_palette(src, pal_map[(tpkg, old)])
                textures[tif] = (pkg, Node(tif, children=[Node("texdat", texdat)]), look)
        if verbose:
            print(f"{pkg:5} {model:16} meshes: {', '.join(m.name[len(mesh_prefix):] for m in picked)}")

    # textures keep a sequential index in texdat +8 and TEX0.TBP0 (rewritten at placement; renumber to match)
    tex_list = []
    for i, (_, node, _) in enumerate(textures.values(), 1):
        d = bytearray(node.children[0].data)
        struct.pack_into("<I", d, 8, i)
        o = tex0_offset(d)
        v = struct.unpack_from("<Q", d, o)[0]
        struct.pack_into("<Q", d, o, (v & ~0x3FFF) | i)
        tex_list.append(Node(node.name, children=[Node("texdat", bytes(d))]))

    renderphase = next(n for n in first["TXR"][1] if n.name == "renderphase")
    out_dir = os.path.join(out_root or os.path.join(HERE, "..", "packs"), out_name)
    os.makedirs(out_dir, exist_ok=True)
    files = {
        "TXR": zed_write(first["TXR"][0], [renderphase, Node("textures", children=tex_list)]),
        "PAL": zed_write(first["PAL"][0], [Node("palettes", children=palettes)]),
        "GEO": zed_write(first["GEO"][0], [Node("models", children=geos)]),
        "MDL": zed_write(first["MDL"][0], meshes),
    }
    for part, blob in files.items():
        zed_read(blob)                                             # the tree must parse back
        open(os.path.join(out_dir, f"{lib_name}_{part}.ZED"), "wb").write(blob)
    name = out_name.upper()
    cdb = cdb_write({f"RUN\\MP\\{name}\\{lib_name}_{part}.ZED": blob for part, blob in files.items()})
    open(os.path.join(out_dir, f"{name}.CDB"), "wb").write(cdb)
    if verbose:
        print(f"wrote {os.path.normpath(out_dir)}: {len(meshes)} meshes, {len(tex_list)} textures, "
              f"{len(palettes)} palettes (ids {PAL_BASE}..{PAL_BASE + len(palettes) - 1})")
        print(f"wrote {name}.CDB ({len(cdb) // 1024} KB): load it with  socom_bots.py loadpack {out_name.lower()} [mc|mem]")
    return os.path.join(out_dir, f"{name}.CDB"), {"meshes": [m.name[len(mesh_prefix):] for m in meshes], "textures": len(tex_list),
                                                  "palettes": len(palettes), "bytes": len(cdb), "missing": missing}


# ---- CDB archive (what the game mounts from mc0:/BASCUS-97275<NAME>/<NAME>.CDB) ---------------------------------------
#
# ZDB_Mount (0x003C8C80) tries RUN/<NAME>.ZDB, then RUN/<NAME>.CDB on the disc, then the memory card in slot 1 and 2:
# "BASCUS-97275" <NAME> "/" <NAME> ".CDB" ("Loading mission data from Memory Card"). A CDB is a 0x110-byte header
# (+0x98 entry count, +0x9C entry size) whose first 0x70-byte entry starts at +0xA0:
#     +0x00 entry size (0x70)  +0x04 name[0x40] ("RUN\MP\X\CLIB_TXR.ZED")  +0x44 offset  +0x48 size
#     +0x54 flags (bit 0 = zlib-compressed + SHA-1 at +0x58; clear = stored, read as-is)
# Lookup is by exact name (0x003C86D0); data is read straight through the archive's stream (0x003C7AA0).

CDB_HEADER, CDB_ENTRY, CDB_FIRST = 0x110, 0x70, 0xA0


def cdb_write(files, align=0x800):
    n = len(files)
    table = CDB_FIRST + CDB_ENTRY * n
    out = bytearray((table + align - 1) & ~(align - 1))
    struct.pack_into("<IIII", out, 0, CDB_HEADER, 1, 0, 2)
    struct.pack_into("<II", out, 0x98, n, CDB_ENTRY)
    for i, (name, blob) in enumerate(files.items()):
        e = CDB_FIRST + CDB_ENTRY * i
        off = len(out)
        struct.pack_into("<I", out, e, CDB_ENTRY)
        out[e + 4:e + 4 + len(name)] = name.encode("ascii")
        struct.pack_into("<II", out, e + 0x44, off, len(blob))
        out += blob + bytes(-len(blob) % align)
    return bytes(out)


def check_pack_name(name):
    """The asset-lib loader (0x00336FD0, path helper 0x00337530) turns //mp/<dir>/clib into run/<dir>/clib_txr.zed
    but keeps <dir>'s second component only when the path contains "mp/m" or "sp/m" (map names: mp61, m51). A pack
    called charpack is looked up as run/mp/clib_txr.zed and nothing loads - so pack names start with 'm'.
    ZDB_Mount (0x003C8C80) builds the memory-card path "BASCUS-97275<NAME>/<NAME>.CDB" in a 32-byte stack buffer at
    the top of its frame; a longer path overwrites the caller's return address (MCHARPACK, 36 bytes: the game jumped
    to "CDB" = 0x00424443). 12 + 2 * len + 5 + 1 <= 32 -> at most 7 characters."""
    if not re.fullmatch(r"m[a-z0-9_]{1,6}", name.lower()):
        sys.exit(f"pack name '{name}' must start with 'm' and be at most 7 letters/digits (e.g. mhost)")


def install(cdb_path, card):
    """Copy <NAME>.CDB into a PCSX2 FOLDER memory card as BASCUS-97275<NAME>/<NAME>.CDB (a .ps2 file card: use the
    card manager / mc_import with that folder instead). PCSX2 reads a folder card when it is inserted - re-insert the
    card (or restart the game) after installing."""
    name = os.path.basename(cdb_path)[:-4]
    check_pack_name(name)
    if not os.path.isdir(card):
        sys.exit(f"{card} is not a folder memory card")
    dest = os.path.join(card, "BASCUS-97275" + name)
    os.makedirs(dest, exist_ok=True)
    with open(cdb_path, "rb") as src, open(os.path.join(dest, name + ".CDB"), "wb") as dst:
        dst.write(src.read())
    print(f"installed {os.path.join(dest, name + '.CDB')}")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--iso", default="F:/SOCOM II - U.S. Navy SEALs.iso")
    ap.add_argument("--out", help="pack name: a PRESETS name, or any name for --models (starts with 'm', max 7 "
                                  "characters; also the archive and library name)")
    ap.add_argument("--all", action="store_true", help="build every pack in PRESETS")
    ap.add_argument("--card", help="install into this PCSX2 folder memory card, e.g. "
                                   "%%USERPROFILE%%/OneDrive/Documents/PCSX2/memcards/test.ps2")
    ap.add_argument("--models", help="PACKAGE:model[,PACKAGE:model...] instead of the default set, e.g. "
                                     "MP82:rus_escort01 (one character per archive keeps each load small)")
    a = ap.parse_args()
    if a.all:
        jobs = list(PRESETS.items())
    elif a.out and a.models:
        jobs = [(a.out, [tuple(x.split(":", 1)) for x in a.models.split(",")])]
    elif a.out in PRESETS:
        jobs = [(a.out, PRESETS[a.out])]
    else:
        ap.error(f"--all, a preset (--out {' / '.join(PRESETS)}), or --out NAME --models PKG:model,...")
    disc = Disc(a.iso)
    for name, pack in jobs:
        check_pack_name(name)
        try:
            cdb, _ = build(disc, pack, name)
        except BuildError as e:
            sys.exit(str(e))
        if a.card:
            install(cdb, a.card)


if __name__ == "__main__":
    main()
