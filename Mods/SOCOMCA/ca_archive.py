"""SOCOM: Combined Assault (SCUS-97498) archives and character libraries - read-only.

Containers
    RUN/<hash>.ZAR      SOCOM II ZDB layout (0xFC header, +0x98 count, 0x5C-byte entries from +0xA0) holding the
                        mission files RUN\\<AREA>\\<NAME>.ZDB
    ZDB v6              +0x00 0xC8 header size, +0x04 0x58 entry size, +0x0C data base, +0xC4 entry count; entries from
                        +0xC8: {name[0x40], tag[4], version, offset (from the data base), size, hash, 0}.
                        Tag DEP_ = dependency on another ZDB, ASST = asset library, CHAR = character.rdr, ...
    Per mission         RUN\\<AREA>\\VCOMMON_<MISSION>.ZDB holds ASST "clib" - the mission's characters.

clib (ASST, version 29): a serialized C++ object graph. The game loads it verbatim and turns stored offsets into
pointers (pointer = stored offset + load address). Names are 32-bit hashes. Header vectors {begin, end, cap, alloc}:
    +0x28 models     node trees (root type 0x00839A2C, bones type 0x0083DC7C, 0xE0 apart)
    +0x38 objects    scene objects - the meshes (VIF packets)
    +0x48 textures   records: +0x4C hash, +0x54 pixels, +0x58 palette record, +0x5C psm byte, +0x60 size,
                     +0x14 -> GIF A+D packet (ALPHA, TEX1, TEX0, TEST, CLAMP); pixels at +0x54
    +0x58 palettes   0x220-byte records: +0x00 VRAM slot (written at load), +0x04 0x00020200 (as SOCOM II par),
                     +0x0C hash, +0x14 colour pointer, +0x20 256 x 16-bit colours (as SOCOM II buf)
"""
import os
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "SOCOM2", "bots"))
import characters_readme as cr                         # noqa: E402  ISO listing + SOCOM II ZDB reader

ISO = "F:/SOCOM - Combined Assualt.iso"

# Names are CRC32 (zlib.crc32, case-sensitive) of the original name: bones by their name, textures by "<name>.tif".
# Bone hash -> (CA name, SOCOM II bone it maps to). The rig is SOCOM II's: same joint positions in the rest pose
# (within ~0.01 for hips/legs/spine/head, ~0.2 at elbows). CA adds a forearm twist ("Wrist", 1/3 of the way from the
# elbow to the hand) and a head-top end bone, and has no toes. SOCOM II's rshoulder_wgt / aimnodes / rtoe get no CA
# vertices. The head-top name is not recovered (it has no vertices of its own either).
BONES = {
    0x89ED856D: ("skel_root", "body"),          # CA's sits at the ground (0, 0, 0.53), SOCOM II's at hip height:
                                                #   re-expressed there, ground vertices overflow 16 bits -> use body
    0x7EE3A92F: ("hips", "hips"),
    0x5E4CDCAC: ("spinelo", "spinelo"),
    0xD343BC9D: ("spinehi", "spinehi"),
    0x2011D653: ("neck", "neck"),
    0xA7F3F69C: ("head", "head"),
    0xFC45C9E2: ("<head top>", "head"),
    0x0886299D: ("<head piece>", "head"),       # MP skins: head children carrying their own mesh (gear), 0.26 up
    0x14DD7537: ("<head piece 2>", "head"),     #   and (0.82, 0.25) to the side
    0xDBA80BB2: ("body", "body"),
    0xA36E7E68: ("rightCollar", "rscap"),
    0x45780C10: ("rightShoulder", "rbicep"),
    0x9586FDE0: ("rightElbow", "rforearm"),
    0x4C4BFAA8: ("rightWrist", "rforearm"),     # forearm twist
    0xF708CF93: ("rightHand", "rhand"),
    0x5078BD30: ("leftCollar", "lscap"),
    0x3577BB50: ("leftShoulder", "lbicep"),
    0x3A34EDF2: ("leftElbow", "lforearm"),
    0xE3F9EABA: ("leftWrist", "lforearm"),
    0x4F03913A: ("leftHand", "lhand"),
    0x0EE0BC99: ("rightThigh", "rthigh"),
    0x7A12CE1D: ("rightKnee", "rcalf"),
    0x19E5B48F: ("rightFoot", "rfoot"),
    0xA152AC8B: ("leftThigh", "lthigh"),
    0xC21990B4: ("leftKnee", "lcalf"),
    0xA1EEEA26: ("leftFoot", "lfoot"),
}

# Character type hashes (character.rdr record +0x00) recovered from strings; most are compiled into mission scripts.
CHARACTER_TYPES = {0x12C3453C: "bullfrog", 0x2057A31C: "underwood"}


def v6_entries(d):
    """[(name, tag, version, offset, size)] of a ZDB v6, or None if d is not one. Offsets are from the data base."""
    if len(d) < 0xC8 or struct.unpack_from("<II", d, 0) != (0xC8, 0x58):
        return None
    out = []
    for i in range(struct.unpack_from("<I", d, 0xC4)[0]):
        o = 0xC8 + 0x58 * i
        name = d[o:o + 0x40].split(b"\0")[0].decode("latin1")
        tag = d[o + 0x40:o + 0x44].decode("latin1")
        version, off, size, _ = struct.unpack_from("<4I", d, o + 0x44)
        out.append((name, tag, version, off, size))
    return out


def v6_resource(d, name, tag=None):
    """Bytes of the named resource in a ZDB v6 (tag: e.g. "ASST" - a ZDB can hold a ZANM and an ASST of one name)."""
    base = struct.unpack_from("<I", d, 0x0C)[0]
    for n, t, version, off, size in v6_entries(d) or []:
        if n == name and t != "DEP_" and (tag is None or t == tag):
            return d[base + off:base + off + size]
    return None


class Disc:
    def __init__(self, iso_path=ISO):
        self.iso = open(iso_path, "rb")
        self.listing = cr.iso_listing(self.iso)

    def zdbs(self):
        """yield (archive, inner path, ZDB v6 bytes) for every ZDB v6 on the disc (inside the ZARs and loose)."""
        for path in sorted(self.listing):
            if "/SOUNDS/" in path or not path.endswith((".ZAR", ".ZDB")):
                continue
            size, lba = self.listing[path]
            if path.endswith(".ZAR"):
                for inner, blob in cr.zdb_entries(self.iso, size, lba).items():
                    if inner.upper().endswith(".ZDB") and v6_entries(blob) is not None:
                        yield path, inner, blob
            else:
                self.iso.seek(lba * 2048)
                blob = self.iso.read(size)
                if v6_entries(blob) is not None:
                    yield path, path, blob

    def clibs(self):
        """yield (mission, clib bytes) for every VCOMMON_<MISSION>.ZDB."""
        seen = set()
        for _, inner, blob in self.zdbs():
            base = inner.replace("/", "\\").split("\\")[-1]
            if not base.upper().startswith("VCOMMON_") or base in seen:
                continue
            seen.add(base)
            data = v6_resource(blob, "clib")
            if data and len(data) >= 0x100:                       # some modes carry a 16-byte stub instead
                yield base[8:-4].upper(), data


def palette_size(kind):
    """Palette record +0x04 (as SOCOM II par word 1): 0x00020200 = 256 16-bit colours, 0x00000400 = 256 32-bit."""
    return {0x00020200: 512, 0x00000400: 1024}[kind]


class Clib:
    """A character library as stored on disc (offsets, not pointers). base: where the four header vectors (models,
    objects, textures, palettes) start - by default from the header version; nested() gives a second library."""
    def __init__(self, data, base=None):
        self.d = data
        self._base = base

    def u32(self, o):
        return struct.unpack_from("<I", self.d, o)[0]

    def vector(self, o):
        return [self.u32(x) for x in range(self.u32(o), self.u32(o + 4), 4)]

    @property
    def base(self):
        """Header vectors start at +0x28 in version 2 (character libraries), +0x2C in version 3 (global_assets ...),
        +0x24 in version 1 (ISLE level data)."""
        return self._base if self._base is not None else 0x28 + 4 * (self.u32(0) - 2)

    def nested(self):
        """The second library a version-3 header points at (word 3): global_assets.rdr keeps every weapon there
        (~90 models: ak47, m4a1, mp5, spas12 ... with firepoint / aimpoint / Gun_box nodes). Its vectors start at
        +0x18. None when there is none."""
        if self.u32(0) != 3:
            return None
        o = self.u32(0x0C)
        if not 0 < o < len(self.d) - 0x58:
            return None
        return Clib(self.d, o + 0x18)

    models = property(lambda self: self.vector(self.base))
    objects = property(lambda self: self.vector(self.base + 0x10))
    textures = property(lambda self: self.vector(self.base + 0x20))
    palettes = property(lambda self: self.vector(self.base + 0x30))

    def texture(self, rec):
        """{hash, width, height, psm, tex0, pixels, palette (record offset or 0)}"""
        tex0 = None
        o = self.u32(rec + 0x14)                          # the 2nd DMA tag's address: GIF tag, then A+D pairs
                                                          # (rec + 0x80, or + 0x84 for a record not 16-aligned)
        nloop = self.u32(o) & 0x7FFF
        for i in range(nloop):
            data, reg = struct.unpack_from("<QQ", self.d, o + 0x10 + 0x10 * i)
            if reg == 0x06:
                tex0 = data
        if tex0 is None:
            raise ValueError(f"texture {rec:#x}: no TEX0")
        psm = (tex0 >> 20) & 0x3F
        w, h = 1 << ((tex0 >> 26) & 0xF), 1 << ((tex0 >> 30) & 0xF)
        bpp = {0x13: 8, 0x14: 4, 0x02: 16, 0x00: 32, 0x01: 24}.get(psm, 8)
        pix = self.u32(rec + 0x54)
        return {"hash": self.u32(rec + 0x4C), "width": w, "height": h, "psm": psm, "tex0": tex0,
                "pixels": self.d[pix:pix + w * h * bpp // 8], "palette": self.u32(rec + 0x58)}

    def palette(self, rec):
        """(hash, 256 16-bit colours as stored - CSM1 order)"""
        col = self.u32(rec + 0x14)
        return self.u32(rec + 0x0C), list(struct.unpack_from("<256H", self.d, col))
