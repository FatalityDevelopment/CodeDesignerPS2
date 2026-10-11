"""Server-side debug tasks for connected players (PCSX2 or real consoles on your server).

Needs the multiplayer server running with the debug API ($env:Online__DebugApiPort = '10090').
Memory commands go over the player's lobby connection (RAW_MEMORY cheat queries / SERVER_MEMORY_POKE);
packet commands go into the DME game world as app messages from a fake client (add one with socom_bots.py).

  Lobby
    python socom_server.py clients                          list lobby players (connection id, account)
    python socom_server.py rooms                            briefing rooms: who is in each, games listed
    python socom_server.py lobbyjoin NAME [ROOM]            put a virtual player in a briefing room (ROOM = 0x44 or
                                                            "US EAST"; default = first room) - shows up in the room's
                                                            player list on your console
    python socom_server.py lobbyleave NAME                  remove a virtual player
    python socom_server.py chat TEXT [--name BOT]           lobby chat line from a fake name to everyone in your room

  Memory
    python socom_server.py peek ADDR [LEN] [--client C]     hex dump of a player's EE memory
    python socom_server.py dump ADDR LEN FILE [--client C]  save memory to a file
    python socom_server.py poke ADDR HEX [--client C]       write bytes (hex string)
    python socom_server.py poke32 ADDR VALUE [--client C]   write one 32-bit word
    python socom_server.py apply CODES.txt [--client C]     send a compiled code list (ADDR VALUE lines)

  Game messages (catalog: ../SOCOM2/packets/packets.json)
    python socom_server.py packets                          list every message with its fields
    python socom_server.py send NAME [field=value ...] [--from K]
        e.g. send chat text="hello from the server" channel=0 --from 1
    python socom_server.py decode HEX                       name and fields of a captured payload
    python socom_server.py capture [--clear] [--follow]     messages the host sent, decoded (--follow: live, as you play)

--client defaults to the only logged-in player; otherwise pass the account name or connection id.
"""
import json, os, struct, sys, urllib.error, urllib.parse, urllib.request

API = "http://127.0.0.1:10090"
HERE = os.path.dirname(os.path.abspath(__file__))
CATALOG = os.path.join(HERE, "..", "SOCOM2", "packets", "packets.json")
GAME_CLASS = 0x02
POKE_CHUNK = 512


# -- debug API ------------------------------------------------------------------
def api(path, **q):
    url = API + path + ("?" + urllib.parse.urlencode({k: v for k, v in q.items() if v is not None}) if q else "")
    try:
        return urllib.request.urlopen(url, timeout=30).read().decode()
    except urllib.error.HTTPError as e:
        sys.exit(f"server: {e.read().decode()}")
    except urllib.error.URLError:
        sys.exit(f"Server debug API not reachable at {API} - start the server with $env:Online__DebugApiPort = '10090'")

def num(s):
    return int(s, 0) if isinstance(s, str) else int(s)


# -- memory ---------------------------------------------------------------------
def peek(addr, length, client=None):
    return bytes.fromhex(api("/peek", client=client, addr=hex(addr), len=length))

def poke(addr, data, client=None):
    for off in range(0, len(data), POKE_CHUNK):
        api("/poke", client=client, addr=hex(addr + off), hex=data[off:off + POKE_CHUNK].hex())

def hexdump(addr, data):
    for i in range(0, len(data), 16):
        row = data[i:i + 16]
        words = " ".join(f"{struct.unpack_from('<I', row, j)[0]:08X}" for j in range(0, len(row) - len(row) % 4, 4))
        text = "".join(chr(c) if 32 <= c < 127 else "." for c in row)
        print(f"{addr + i:08X}  {words:<35}  {text}")

def apply_codes(path, client=None):
    """Send ADDR VALUE lines as pokes, merging consecutive words into runs."""
    if not os.path.exists(path):
        path = os.path.join(HERE, "..", path)
    codes = []
    for line in open(path):
        parts = line.split()
        if len(parts) == 2:
            codes.append((int(parts[0], 16) & 0x01FFFFFF, int(parts[1], 16)))
    runs, cur = [], None
    for a, v in codes:                                   # keep file order (hooks normally come last)
        if cur and a == cur[0] + len(cur[1]):
            cur[1].extend(struct.pack("<I", v))
        else:
            cur = (a, bytearray(struct.pack("<I", v)))
            runs.append(cur)
    for a, data in runs:
        poke(a, bytes(data), client)
    print(f"{os.path.basename(path)}: {len(codes)} words in {len(runs)} pokes")


# -- game messages --------------------------------------------------------------
def load_catalog():
    with open(CATALOG, encoding="utf-8") as f:
        return json.load(f)["packets"]

def find_packet(name_or_id):
    key = name_or_id.lower().rstrip("?")
    for p in load_catalog():
        if p["name"].lower().rstrip("?") == key or int(p["id"], 16) == (num(name_or_id) if name_or_id[:1].isdigit() else -1):
            return p
    sys.exit(f"no packet '{name_or_id}' in {CATALOG} (see: packets)")

FMT = {"u8": "<B", "s8": "<b", "u16": "<H", "s16": "<h", "u32": "<I", "s32": "<i", "f32": "<f"}

def build(p, values):
    fields = {f["name"]: f for f in p["fields"]}
    for k in values:
        if k not in fields:
            sys.exit(f"{p['name']} has no field '{k}' (fields: {', '.join(fields) or 'none'})")
    fixed_end = max([f["offset"] + struct.calcsize(FMT[f["type"]]) for f in p["fields"] if f["type"] in FMT]
                    + [f["offset"] + int(f["type"][4:]) for f in p["fields"] if f["type"].startswith("str:")] + [0])
    size = max(p.get("size") or 0, p.get("minSize") or 0, fixed_end)
    buf = bytearray(size)
    tail = b""
    for f in p["fields"]:
        t, off, v = f["type"], f["offset"], values.get(f["name"])
        if t in FMT:
            if v is None:
                continue
            val = float(v) if t == "f32" else num(v)
            buf[off:off + struct.calcsize(FMT[t])] = struct.pack(FMT[t], val)
        elif t.startswith("str:"):
            n = int(t[4:])
            raw = (v or "").encode("latin1")[:n - 1]
            buf[off:off + n] = raw + bytes(n - len(raw))
        elif t == "cstr":
            del buf[off:]
            tail = (v or "").encode("latin1") + b"\0"
        elif t == "hex":
            del buf[off:]
            tail = bytes.fromhex(v or "")
    data = bytes(buf) + tail
    for f in p["fields"]:                                # derived fields, e.g. chat size = len(text)+6
        if f.get("auto") and f["name"] not in values:
            text = values.get("text", "")
            size_val = eval(f["auto"], {"len": len}, {"text": text})
            width = struct.calcsize(FMT[f["type"]])
            data = data[:f["offset"]] + struct.pack(FMT[f["type"]], size_val) + data[f["offset"] + width:]
            if f["name"] == "size" and len(data) < size_val:           # the game sends exactly `size` bytes
                data += bytes(size_val - len(data))
    return bytes([GAME_CLASS, int(p["id"], 16)]) + data

def decode(payload):
    if len(payload) < 2 or payload[0] != GAME_CLASS:
        return f"not a game message (class 0x{payload[0]:02X})" if payload else "empty"
    pid, data = payload[1], payload[2:]
    p = next((x for x in load_catalog() if int(x["id"], 16) == pid), None)
    if not p:
        return f"unknown id 0x{pid:02X} [{len(data)}] {data.hex(' ')}"
    out = []
    for f in p["fields"]:
        t, off = f["type"], f["offset"]
        if t in FMT and off + struct.calcsize(FMT[t]) <= len(data):
            v = struct.unpack_from(FMT[t], data, off)[0]
            out.append(f"{f['name']}={v:.2f}" if t == "f32" else f"{f['name']}={v}")
        elif t.startswith("str:") or t == "cstr":
            end = off + (int(t[4:]) if t.startswith("str:") else len(data))
            text = data[off:end].split(b"\x00")[0].decode("latin1")
            out.append(f"{f['name']}={text!r}")
        elif t == "hex":
            out.append(f"{f['name']}={data[off:].hex()}")
    return f"{p['name']} (0x{pid:02X}) [{len(data)}] " + " ".join(out)

def list_packets():
    for p in load_catalog():
        fields = ", ".join(f"{f['name']}:{f['type']}@{f['offset']}" for f in p["fields"]) or "-"
        size = p.get("size")
        print(f"0x{int(p['id'], 16):02X} {p['name']:<18} size {size if size is not None else 'var':<4} -> {p['target']:<10} {fields}")
        print(f"      {p['description']}")

def describe(payload):
    """Game messages by catalog name; DME object traffic labelled; None for pings (noise)."""
    if payload[:1] == bytes([GAME_CLASS]):
        return decode(payload)
    if payload[:2] == b"\x00\x01":
        return None                                              # DME ping
    if payload[:2] == b"\x00\x03":
        return f"object update [{len(payload)}] {payload[2:].hex(' ')}"
    if payload[:2] == b"\x00\x04":
        name = next((n for n in (b"PersonaObject", b"SealObject", b"GameObject", b"BombObject", b"TurretObject",
                                 b"BlatherObject") if n in payload), b"?").decode()
        return f"object create {name} [{len(payload)}]"
    return f"other [{len(payload)}] {payload[:24].hex(' ')}"

def capture(clear, follow=False):
    """Messages the host sent (to fake players or broadcast). --follow keeps printing new ones as you play."""
    import time
    seen = set()
    first = True
    while True:
        lines = api("/fake/inbox", **({"clear": 1} if clear and first else {})).splitlines()
        for line in lines:
            parts = line.split()
            if len(parts) < 4 or line in seen:
                continue
            seen.add(line)
            text = describe(bytes.fromhex(parts[3]))
            if text:
                if follow and first:
                    continue                                     # follow: only what happens from now on
                print(parts[0], parts[1], text, flush=True)
        first = False
        if not follow:
            break
        time.sleep(1)


# -- command line ---------------------------------------------------------------
def opt(args, name, default=None):
    if name in args:
        i = args.index(name)
        v = args[i + 1]
        del args[i:i + 2]
        return v
    return default

if __name__ == "__main__":
    args = sys.argv[1:]
    client = opt(args, "--client")
    chat_name = opt(args, "--name", "BOT")
    sender = opt(args, "--from", "1")
    clear = "--clear" in args
    follow = "--follow" in args
    args = [a for a in args if a not in ("--clear", "--follow")]
    cmd, rest = (args[0], args[1:]) if args else ("", [])

    if cmd == "clients":
        print(api("/clients"))
    elif cmd == "rooms":
        print(api("/rooms"))
    elif cmd == "lobbyjoin":
        print(api("/lobby/join", name=rest[0], room=" ".join(rest[1:]) or None))
    elif cmd == "lobbyleave":
        print(api("/lobby/leave", name=rest[0]))
    elif cmd == "chat":
        print(api("/chat", client=client, name=chat_name, text=" ".join(rest)))
    elif cmd == "peek":
        addr = num(rest[0])
        hexdump(addr, peek(addr, num(rest[1]) if len(rest) > 1 else 64, client))
    elif cmd == "dump":
        addr, length, path = num(rest[0]), num(rest[1]), rest[2]
        data = peek(addr, length, client)
        open(path, "wb").write(data)
        print(f"saved {len(data)} bytes from 0x{addr:08X} to {path}")
    elif cmd == "poke":
        data = bytes.fromhex(rest[1])
        poke(num(rest[0]), data, client)
        print(f"poked {len(data)} byte(s) at 0x{num(rest[0]):08X}")
    elif cmd == "poke32":
        poke(num(rest[0]), struct.pack("<I", num(rest[1]) & 0xFFFFFFFF), client)
        print(f"0x{num(rest[0]):08X} = 0x{num(rest[1]) & 0xFFFFFFFF:08X}")
    elif cmd == "apply":
        apply_codes(rest[0], client)
    elif cmd == "packets":
        list_packets()
    elif cmd == "send":
        p = find_packet(rest[0])
        values = dict(a.split("=", 1) for a in rest[1:])
        payload = build(p, values)
        print(decode(payload))
        print(api("/app", **{"from": sender, "hex": payload.hex()}))
    elif cmd == "decode":
        print(decode(bytes.fromhex(rest[0].replace(" ", ""))))
    elif cmd == "capture":
        capture(clear, follow)
    else:
        print(__doc__)
