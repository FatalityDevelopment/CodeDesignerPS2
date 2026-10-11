"""SOCOM II DME object messages - schema-driven encode/decode shared by socom_bots.py and socom_server.py.

Schemas (objects.json) are read from the game's class descriptor table 0x0067BE08 (+type*0x228). Wire formats:
  update   00 03 [type u8][n u8][objid u16] then n x [field index u8][value]      (value size from the schema)
  create   00 04 [owner u8][type u8][00 00][u32 2][objid u32][stamp u32][u32 0][class name: 16 bytes]
                 [every field of the class, in index order]                       (e.g. SealObject = 178 bytes)
  destroy  00 0A [objid u16]
  game     02 [id] [data]                                                         (packets.json)
"""
import json, os, struct

HERE = os.path.dirname(os.path.abspath(__file__))
FMT = {"s8": "<b", "u8": "<B", "s16": "<h", "u16": "<H", "u32": "<I", "s32": "<i", "f32": "<f", "vec3": "<3f"}

with open(os.path.join(HERE, "objects.json"), encoding="utf-8") as f:
    CLASSES = {int(k): v for k, v in json.load(f)["classes"].items()}
with open(os.path.join(HERE, "packets.json"), encoding="utf-8") as f:
    PACKETS = {int(p["id"], 16): p for p in json.load(f)["packets"]}


def field_name(cls, f):
    return f.get("name") or f"f{f['index']:02X}"


def find_field(cls, key):
    for f in cls["fields"]:
        if key.lower().rstrip("?") in (field_name(cls, f).lower().rstrip("?"), f"f{f['index']:02x}", str(f["index"])):
            return f
    raise KeyError(f"{cls['name']} has no field '{key}'")


def encode_value(f, value):
    t = f["type"]
    if t.startswith("str:"):
        raw = value.encode("latin1") if isinstance(value, str) else bytes(value)
        return raw[:f["size"]].ljust(f["size"], b"\x00")
    if t == "vec3":
        vals = value if isinstance(value, (list, tuple)) else [float(v) for v in str(value).split(",")]
        return struct.pack("<3f", *map(float, vals))
    if t == "f32":
        return struct.pack("<f", float(value))
    return struct.pack(FMT[t], int(value, 0) if isinstance(value, str) else int(value))


def decode_value(f, data):
    t = f["type"]
    if t.startswith("str:"):
        return repr(data.split(b"\x00")[0].decode("latin1"))
    if t == "vec3":
        return "(" + ", ".join(f"{v:.2f}" for v in struct.unpack("<3f", data)) + ")"
    v = struct.unpack(FMT[t], data)[0]
    return f"{v:.2f}" if t == "f32" else (f"0x{v:X}" if t in ("u32", "s32") and abs(v) > 0xFFFF else str(v))


def build_update(obj_type, objid, values):
    """values: {field key: value} -> 00 03 update message."""
    cls = CLASSES[obj_type]
    body = b""
    for key, value in values.items():
        f = find_field(cls, key)
        body += bytes([f["index"]]) + encode_value(f, value)
    return bytes([0x00, 0x03, obj_type, len(values)]) + struct.pack("<H", objid & 0xFFFF) + body


def build_create(obj_type, owner, objid, class_name, values, stamp=0x000E56EE, template=None):
    """00 04 create: header + 16-byte name + all fields in index order (defaults from template data or zero)."""
    cls = CLASSES[obj_type]
    data = bytearray(template) if template else bytearray(sum(f["size"] for f in cls["fields"]))
    pos = 0
    offsets = {}
    for f in cls["fields"]:
        offsets[f["index"]] = pos
        pos += f["size"]
    for key, value in values.items():
        f = find_field(cls, key)
        data[offsets[f["index"]]:offsets[f["index"]] + f["size"]] = encode_value(f, value)
    head = bytes([0x00, 0x04, owner, obj_type, 0, 0]) + struct.pack("<IIII", 2, objid, stamp, 0)
    return head + class_name.encode()[:15].ljust(16, b"\x00") + bytes(data)


def split(payload):
    """Split one DME app payload into (text) descriptions of each message it carries."""
    out, p = [], 0
    while p + 2 <= len(payload):
        c, i = payload[p], payload[p + 1]
        if c == 0x00 and i == 0x03 and p + 6 <= len(payload):          # object update
            t, n = payload[p + 2], payload[p + 3]
            objid = struct.unpack_from("<H", payload, p + 4)[0]
            cls = CLASSES.get(t)
            q, parts = p + 6, []
            for _ in range(n):
                if not cls or q >= len(payload) or payload[q] >= len(cls["fields"]):
                    parts.append(f"?? {payload[q:].hex(' ')}"); q = len(payload); break
                f = cls["fields"][payload[q]]
                parts.append(f"{field_name(cls, f)}={decode_value(f, payload[q + 1:q + 1 + f['size']])}")
                q += 1 + f["size"]
            out.append(f"update {cls['name'] if cls else t} #{objid}: " + " ".join(parts))
            p = q
        elif c == 0x00 and i == 0x04 and p + 38 <= len(payload):        # object create
            owner, t = payload[p + 2], payload[p + 3]
            objid = struct.unpack_from("<I", payload, p + 10)[0]
            name = payload[p + 22:p + 38].split(b"\x00")[0].decode("latin1")
            cls = CLASSES.get(t)
            size = sum(f["size"] for f in cls["fields"]) if cls else len(payload) - p - 38
            out.append(f"create {name} #{objid & 0xFFFF} owner {owner} type {t} [{size}]")
            p += 38 + size
        elif c == 0x00 and i == 0x0A and p + 4 <= len(payload):         # object destroy
            out.append(f"destroy #{struct.unpack_from('<H', payload, p + 2)[0]}")
            p += 4
        elif c == 0x00 and i == 0x05:
            out.append("end-of-batch (00 05)")
            p += 2
        elif c == 0x00 and i == 0x01:
            return out                                                  # ping - noise
        elif c == 0x02 and i in PACKETS:                                # game message
            pk = PACKETS[i]
            size = pk.get("size") or (len(payload) - p - 2)
            data = payload[p + 2:p + 2 + size]
            vals = []
            for f in pk["fields"]:
                if f["type"] in FMT and f["offset"] + struct.calcsize(FMT[f["type"]]) <= len(data):
                    vals.append(f"{f['name']}={struct.unpack_from(FMT[f['type']], data, f['offset'])[0]}")
            out.append(f"game {pk['name']} (0x{i:02X}) " + " ".join(vals))
            p += 2 + size
        else:
            out.append(f"other {payload[p:p + 24].hex(' ')}")
            break
    return out
