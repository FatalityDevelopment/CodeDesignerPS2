"""SOCOM: Combined Assault animations -> SOCOM II motion clips.

CA (RUN/AUX_*ZAN.ZDB bone_loader, tag BONE): serialized vectors of clip records (0x60 bytes):
    +0x00 tracks vector (start, end, capacity) -> 16-byte tracks {bone hash, pos keys, rot keys, counts}
    +0x20 flags (0x2017.... / 0x2019....), +0x24 clip name hash (zlib CRC32 of the SOCOM II-style name,
    e.g. seal_stand, seal_healing), +0x2C duration (s)
    counts: low 16 = 3 * position keys, high 16 = 4 * rotation keys; keys int16 (x, y, z) / (x, y, z, w)
SOCOM II (MOTION_P.ZAR / MOTION_S.ZAR clip, the zipper workbench's rdr-animation-parser.ts):
    +0 5, +4 duration, +8 frame intervals, +0xC bone count, +0x10/+0x14 min/max norm, +0x18 names, +0x1C tracks;
    16-byte bone records from +0x20; names; per bone a track 0x0C / 0x1C / 0x2C / 0x3C (pos / rot animated or not)
"""
import struct
import zlib


def name_hash(name):
    return zlib.crc32(name.encode()) & 0xFFFFFFFF


# ---- CA ----------------------------------------------------------------------------------------------------------
def ca_clips(d):
    """{name hash: record offset} for every clip record in a CA bone_loader blob."""
    u = lambda o: struct.unpack_from("<I", d, o)[0]
    out = {}
    for o in range(0, len(d) - 0x60, 4):
        if (u(o + 0x20) >> 16) in (0x2017, 0x2019):
            a, b, c = u(o), u(o + 4), u(o + 8)
            if 0 < a < b == c <= len(d) and (b - a) % 16 == 0 and (b - a) // 16 <= 40:
                out[u(o + 0x24)] = o
    return out


def ca_clip(d, rec):
    """-> {duration, tracks: {bone hash: (positions [(x,y,z)], rotations [(x,y,z,w)])}} (raw int16)."""
    u = lambda o: struct.unpack_from("<I", d, o)[0]
    a, b = u(rec), u(rec + 4)
    tracks = {}
    for t in range(a, b, 16):
        h, pp, rp, cnt = struct.unpack_from("<4I", d, t)
        npos, nrot = (cnt & 0xFFFF) // 3, (cnt >> 16) // 4
        pos = [struct.unpack_from("<3h", d, pp + 6 * k) for k in range(npos)]
        rot = [struct.unpack_from("<4h", d, rp + 8 * k) for k in range(nrot)]
        tracks[h] = (pos, rot)
    return {"duration": struct.unpack_from("<f", d, rec + 0x2C)[0], "flags": u(rec + 0x20), "tracks": tracks}


# ---- SOCOM II ----------------------------------------------------------------------------------------------------
def s2_clip(d):
    """Decode a SOCOM II motion clip -> {duration, intervals, bones [name], records [16 B], tracks [(type, pos, rot)],
    header} with pos / rot as lists of int16 tuples (one entry when static)."""
    ver, dur, iv, nb, mn, mx, noff, aoff = struct.unpack_from("<IfIIffII", d, 0)
    if ver != 5:
        raise ValueError(f"not a version-5 clip ({ver})")
    records = [d[0x20 + 16 * i:0x30 + 16 * i] for i in range(nb)]
    names, p = [], noff
    while len(names) < nb:
        e = d.index(b"\0", p)
        names.append(d[p:e].decode("latin1"))
        p = e + 1
    n = iv + 1
    tracks, o = [], aoff
    for _ in range(nb):
        typ = struct.unpack_from("<I", d, o)[0]
        if typ == 0x3C:
            pos, rot, o = [struct.unpack_from("<4h", d, o + 4)], [struct.unpack_from("<4h", d, o + 12)], o + 20
        elif typ == 0x2C:
            pos = [struct.unpack_from("<4h", d, o + 4)]
            rot = [struct.unpack_from("<4h", d, o + 12 + 8 * k) for k in range(n)]
            o += 12 + 8 * n
        elif typ in (0x1C, 0x0C):
            pos = [struct.unpack_from("<3h", d, o + 4 + 6 * k) for k in range(n)]
            q = (o + 4 + 6 * n + 3) & ~3
            if typ == 0x1C:
                rot, o = [struct.unpack_from("<4h", d, q)], q + 8
            else:
                rot, o = [struct.unpack_from("<4h", d, q + 8 * k) for k in range(n)], q + 8 * n
        else:
            raise ValueError(f"track type {typ:#x} at {o:#x}")
        tracks.append((typ, pos, rot))
    pad = d[p:aoff]                                          # alignment after the names: leftover bytes, kept
    return {"duration": dur, "intervals": iv, "bones": names, "records": records, "tracks": tracks,
            "norm": (mn, mx), "pad": pad, "end": o}


# ---- retarget CA -> SOCOM II -------------------------------------------------------------------------------------
import json
import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))
CA_PARENT = {int(k, 16): (int(v, 16) if v else None)
             for k, v in json.load(open(os.path.join(HERE, "ca_bone_parents.json"))).items()}
CA_PARENT.update({0xBBD4FAD5: 0xF708CF93,                    # clip-only bones: weapon under rightHand,
                  0x799F10C4: 0x19E5B48F, 0xCF64B6F3: 0xA1EEEA26})   # toes under the feet
S2_PARENT = {"hips": "skel_root", "rthigh": "hips", "rcalf": "rthigh", "rfoot": "rcalf", "rtoe": "rfoot",
             "lthigh": "hips", "lcalf": "lthigh", "lfoot": "lcalf", "ltoe": "lfoot", "aimnodes": "hips",
             "spinelo": "aimnodes", "spinehi": "spinelo", "rshoulder_wgt": "spinehi", "rscap": "spinehi",
             "rbicep": "spinehi", "rforearm": "rbicep", "rhand": "rforearm", "rifle": "rhand", "neck": "spinehi",
             "head": "neck", "lshoulder_wgt": "spinehi", "lscap": "spinehi", "lbicep": "spinehi",
             "lforearm": "lbicep", "lhand": "lforearm", "do_not_use": "hips"}
# SOCOM II bone <- CA bone whose WORLD rotation it takes (both rigs run each bone's x axis along the limb)
SOURCE = {"hips": 0x7EE3A92F, "spinelo": 0x5E4CDCAC, "spinehi": 0xD343BC9D, "neck": 0x2011D653,
          "head": 0xA7F3F69C, "rscap": 0xA36E7E68, "rbicep": 0x45780C10, "rforearm": 0x9586FDE0,
          "rhand": 0xF708CF93, "lscap": 0x5078BD30, "lbicep": 0x3577BB50, "lforearm": 0x3A34EDF2,
          "lhand": 0x4F03913A, "rthigh": 0x0EE0BC99, "rcalf": 0x7A12CE1D, "rfoot": 0x19E5B48F,
          "lthigh": 0xA152AC8B, "lcalf": 0xC21990B4, "lfoot": 0xA1EEEA26}


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz)


def qinv(q):
    return (-q[0], -q[1], -q[2], q[3])


def qnorm(q):
    n = math.sqrt(sum(c * c for c in q)) or 1.0
    return tuple(c / n for c in q)


def qrot(q, v):
    p = qmul(qmul(q, (v[0], v[1], v[2], 0.0)), qinv(q))
    return p[:3]


def qslerp(a, b, t):
    d = sum(x * y for x, y in zip(a, b))
    if d < 0:
        b, d = tuple(-c for c in b), -d
    return qnorm(tuple(x + (y - x) * t for x, y in zip(a, b)))     # nlerp: keys are 1/30 s apart


def sample(keys, t, duration, lerp):
    """Key list spread evenly over [0, duration] (one key = constant) -> value at time t."""
    if len(keys) == 1:
        return keys[0]
    f = max(0.0, min(len(keys) - 1, t / duration * (len(keys) - 1)))
    i = min(int(f), len(keys) - 2)
    return lerp(keys[i], keys[i + 1], f - i)


def retarget(ca, ref, fps=30.0):
    """ca: ca_clip(); ref: s2_clip() of a SOCOM II clip giving the bone list, track kinds, static values and the
    root. -> s2 clip dict (encode_s2) at `fps`."""
    dur = ca["duration"]
    frames = max(1, round(dur * fps))
    q16 = lambda q: qnorm(tuple(c / 32767.0 for c in q))
    lerp3 = lambda a, b, t: tuple(x + (y - x) * t for x, y in zip(a, b))
    names = [b.lstrip("-") for b in ref["bones"]]
    ref_rot = {n: q16(tr[2][0]) for n, tr in zip(names, ref["tracks"])}
    ref_pos = {n: tr[1][0] for n, tr in zip(names, ref["tracks"])}
    root_pos = ref_pos["skel_root"][:3]
    out_rot = {n: [] for n in names}
    out_pos = {n: [] for n in names}
    for k in range(frames + 1):
        t = dur * k / frames
        wr, wp = {}, {}                                       # CA world rotation / position per bone hash
        for h in CA_PARENT:
            if h not in ca["tracks"]:
                continue
            pos, rot = ca["tracks"][h]
            lp = sample(pos, t, dur, lerp3)
            lr = sample([q16(q) for q in rot], t, dur, qslerp)
            p = CA_PARENT.get(h)
            if p in wr:
                wr[h], wp[h] = qnorm(qmul(wr[p], lr)), tuple(a + b for a, b in zip(wp[p], qrot(wr[p], lp)))
            else:
                wr[h], wp[h] = lr, lp
        sw = {"skel_root": ref_rot["skel_root"]}               # SOCOM II world rotations as we go (parents first)
        order = ["skel_root"] + [n for n in names if n != "skel_root"]
        done = set(["skel_root"])
        while len(done) < len(names):
            for n in order:
                if n in done or S2_PARENT.get(n) not in done:
                    continue
                par = sw[S2_PARENT[n]]
                if n in SOURCE and SOURCE[n] in wr:
                    loc = qnorm(qmul(qinv(par), wr[SOURCE[n]]))
                elif n == "aimnodes":                         # cancels the hips: spine relative to the root
                    loc = qinv(out_rot["hips"][-1])
                else:
                    loc = ref_rot[n]
                sw[n] = qnorm(qmul(par, loc))
                out_rot[n].append(loc)
                if n == "hips":                               # CA root + hips (ground-based) minus SOCOM II's root
                    hp = wp[0x7EE3A92F]
                    out_pos[n].append(tuple(hp[i] - root_pos[i] for i in range(3)))
                else:
                    out_pos[n].append(ref_pos[n][:3])
                done.add(n)
    out_rot["skel_root"] = [ref_rot["skel_root"]] * (frames + 1)
    tracks = []
    for n, (typ, rp, rr) in zip(names, ref["tracks"]):
        rot = [tuple(max(-32767, min(32767, round(c * 32767))) for c in q) for q in out_rot[n]]
        pos = [tuple(round(c) for c in p) for p in out_pos[n]] if n == "hips" else None
        if n == "hips":
            tracks.append((0x0C, pos, rot))
        elif n == "skel_root" or len(set(rot)) == 1:
            tracks.append((0x3C, [rp[0] if len(rp[0]) == 4 else tuple(rp[0]) + (612,)], [rot[0]]))
        else:
            tracks.append((0x2C, [rp[0] if len(rp[0]) == 4 else tuple(rp[0]) + (612,)], rot))
    return {"duration": dur, "intervals": frames, "bones": ref["bones"], "records": ref["records"],
            "tracks": tracks, "norm": ref["norm"], "pad": ref["pad"]}


def encode_s2(c):
    """s2 clip dict -> bytes (the layout s2_clip() reads)."""
    nb = len(c["bones"])
    names = b"".join(n.encode("latin1") + b"\0" for n in c["bones"])
    noff = 0x20 + 16 * nb
    aoff = (noff + len(names) + 3) & ~3
    out = bytearray(struct.pack("<IfIIffII", 5, c["duration"], c["intervals"], nb, *c["norm"], noff, aoff))
    out += b"".join(c["records"]) + names
    pad = c.get("pad", b"")
    out += pad if len(pad) == aoff - len(out) else bytes(aoff - len(out))
    for typ, pos, rot in c["tracks"]:
        out += struct.pack("<I", typ)
        if typ == 0x3C:
            out += struct.pack("<4h", *pos[0]) + struct.pack("<4h", *rot[0])
        elif typ == 0x2C:
            out += struct.pack("<4h", *pos[0]) + b"".join(struct.pack("<4h", *q) for q in rot)
        else:
            out += b"".join(struct.pack("<3h", *p) for p in pos)
            out += bytes(-len(out) % 4)
            out += b"".join(struct.pack("<4h", *q) for q in (rot if typ == 0x0C else rot[:1]))
    return bytes(out)


# ---- extraction / tooling ----------------------------------------------------------------------------------------
ANIM_ROOT = "F:/SOCOM Packs/SOCOMCA/anims"
S2_ISO = "F:/SOCOM II - U.S. Navy SEALs.iso"
EXTRA_NAMES = ["seal_healing"]                                # CA-only clips cracked so far


def extract(root=ANIM_ROOT):
    """Copy CA's animation banks (AUX_*ZAN.ZDB bone_loader) and SOCOM II's MOTION_P.ZAR to `root`, plus
    clips.json {bank: [{hash, name, duration, bones}]} with every name that matches a SOCOM II clip name."""
    import hashlib
    import sys
    import ca_archive as ca
    sys.path.insert(0, os.path.join(HERE, "..", "SOCOM2", "bots"))
    import build_charpack as bp
    import characters_readme as cr
    os.makedirs(root, exist_ok=True)
    iso = open(S2_ISO, "rb")
    sz, lba = cr.iso_listing(iso)["/RUN/MOTION_P.ZAR"]
    iso.seek(lba * 2048)
    motion = iso.read(sz)
    open(os.path.join(root, "MOTION_P.ZAR"), "wb").write(motion)
    names = {name_hash(n.name): n.name for n in bp.zed_read(motion)[1]}
    names.update({name_hash(n): n for n in EXTRA_NAMES})
    cracked = os.path.join(root, "cracked.json")              # CA-only names found by word combinations
    if os.path.exists(cracked):
        names.update({int(h, 16): n for h, n in json.load(open(cracked)).items()})
    seen, index = {}, {}
    for _, inner, z in ca.Disc().zdbs():
        for n, t, *_ in ca.v6_entries(z) or []:
            if t != "BONE":
                continue
            d = ca.v6_resource(z, n, t)
            h = hashlib.md5(d).hexdigest()
            if h in seen:
                continue
            bank = os.path.basename(inner.replace("\\", "/"))[:-4].lower()
            bank = bank if bank not in seen.values() else f"{bank}_{len(seen)}"
            seen[h] = bank
            open(os.path.join(root, bank + ".bone"), "wb").write(d)
            index[bank] = [{"hash": f"{k:08x}", "name": names.get(k), "duration": round(ca_clip(d, o)["duration"], 3),
                            "bones": len(ca_clip(d, o)["tracks"])} for k, o in sorted(ca_clips(d).items(), key=lambda x: x[1])]
    json.dump(index, open(os.path.join(root, "clips.json"), "w"), indent=1)
    for bank, clips in index.items():
        print(f"{bank}: {len(clips)} clips, {sum(1 for c in clips if c['name'])} named")


def find_clip(name, root=ANIM_ROOT):
    """CA clip by name -> (bank bytes, record offset)."""
    h = name_hash(name)
    for bank, clips in json.load(open(os.path.join(root, "clips.json"))).items():
        if any(int(c["hash"], 16) == h for c in clips):
            d = open(os.path.join(root, bank + ".bone"), "rb").read()
            return d, ca_clips(d)[h]
    raise KeyError(f"no CA clip named {name!r} (see {root}/clips.json)")


def s2_reference(name="seal_stand", root=ANIM_ROOT):
    import sys
    sys.path.insert(0, os.path.join(HERE, "..", "SOCOM2", "bots"))
    import build_charpack as bp
    return s2_clip(next(n.data for n in bp.zed_read(open(os.path.join(root, "MOTION_P.ZAR"), "rb").read())[1]
                        if n.name == name))


def convert(name, fps=8.0, ref="seal_stand"):
    """CA clip name -> SOCOM II clip dict (seal_stand's layout: still root, motion on the hips)."""
    d, rec = find_clip(name)
    return retarget(ca_clip(d, rec), s2_reference(ref), fps=fps)


if __name__ == "__main__":
    import sys
    if sys.argv[1:2] == ["extract"]:
        extract()
    elif sys.argv[1:2] == ["convert"] and len(sys.argv) >= 3:
        c = convert(sys.argv[2], float(sys.argv[3]) if len(sys.argv) > 3 else 30.0)
        out = os.path.join(ANIM_ROOT, sys.argv[2] + ".rdr")
        open(out, "wb").write(encode_s2(c))
        print(f"{out}: {c['intervals'] + 1} frames, {c['duration']:.2f} s")
    else:
        print("ca_anim.py extract | convert <ca clip name> [fps]")
