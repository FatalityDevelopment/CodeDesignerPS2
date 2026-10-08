"""SOCOM II 12v12 lobby bots (PCSX2 testing).

Needs: PCSX2 with the CodeDesigner bridge on 127.0.0.1:8765, and the multiplayer server started with
the debug API on port 10090 ($env:Online__DebugApiPort = '10090'). You must be the HOST, sitting in a game lobby.

    python socom_bots.py patch              apply the 12v12 codes (re-run after every login)
    python socom_bots.py add 1 23           add bots BOT1..BOT23 (index range)
    python socom_bots.py add 5              add one bot, BOT5
    python socom_bots.py remove 1 23        remove bots
    python socom_bots.py status             show DME world + host slot table
"""
import base64, json, os, socket, struct, sys, time, urllib.error, urllib.request

BRIDGE = ("127.0.0.1", 8765)
API = "http://127.0.0.1:10090"
HERE = os.path.dirname(os.path.abspath(__file__))
CODES = os.path.join(HERE, "..", "lobby_12v12.codes.txt")

# Server RSA public modulus (Ps2Rsa.ServerKey) - the same key the server sends in the connect notify.
N = 10315955513017997681600210131013411322695824559688299373570246338038100843097466504032586443986679280716603540690692615875074465586629501752500179100369237
KEY = N.to_bytes(64, "little")
# Captured from the host: 38-byte player info (00 0F) and the object-create batch (00 04 ...).
with open(os.path.join(HERE, "templates.txt")) as f:
    INFO, OBJ = (bytes.fromhex(l.replace(" ", "")) for l in f.read().splitlines()[:2])
PERSONA_REC = OBJ[0x7E:0xE5]          # 21-byte record header + "PersonaObject\0" + 76-byte data block
TEAM = {"seal": 0x40000001, "terr": 0x80000100}


# -- bridge ---------------------------------------------------------------------
def bridge(cmd):
    s = socket.create_connection(BRIDGE)
    s.sendall((json.dumps(cmd) + "\n").encode())
    buf = b""
    while not buf.endswith(b"\n"):
        chunk = s.recv(1 << 20)
        if not chunk:
            break
        buf += chunk
    s.close()
    return json.loads(buf)

def rd(a, n):
    return base64.b64decode(bridge({"Command": "READMEM", "Address": a, "Length": n, "Region": "ee"})["Data"])

def wr32(a, w):
    bridge({"Command": "WRITEMEM", "Address": a, "Data": struct.pack("<I", w).hex(), "Region": "ee"})


# -- server debug API -----------------------------------------------------------
def api(path):
    try:
        return urllib.request.urlopen(API + path, timeout=5).read().decode()
    except urllib.error.HTTPError as e:
        return f"HTTP {e.code}: {e.read().decode()}"
    except urllib.error.URLError:
        sys.exit(f"Server debug API not reachable at {API} - start the server with $env:Online__DebugApiPort = '10090'")

def app(k, payload):
    return api(f"/app?from={k}&hex={payload.hex()}")


# -- commands -------------------------------------------------------------------
def patch():
    codes = [l.split() for l in open(CODES) if l.strip()]
    for a, v in codes:
        wr32(int(a, 16) & 0x1FFFFFF, int(v, 16))
    ok = sum(struct.unpack("<I", rd(int(a, 16) & 0x1FFFFFF, 4))[0] == int(v, 16) for a, v in codes)
    print(f"12v12 patch: {ok}/{len(codes)} words applied")

def persona(name, team, obj_id, owner):
    rec = bytearray(PERSONA_REC)
    rec[1] = owner                                    # record +1: owner DME index (0 would be the HOST)
    struct.pack_into("<I", rec, 9, obj_id)            # record +9: object id (low 16 bits = object slot)
    data = 0xA3 - 0x7E
    struct.pack_into("<I", rec, data, TEAM[team])     # data +0: team
    nm = name.encode()[:15]
    rec[data + 14:data + 30] = nm + bytes(16 - len(nm))
    return bytes([0x00]) + bytes(rec) + OBJ[0xE5:]

def add(k, name=None):
    name = name or f"BOT{k}"
    r = api(f"/fake/join?index={k}")
    print(r)
    if not r.startswith("fake client"):
        return
    time.sleep(1)
    app(k, bytes([0, 0x10, 2, k]) + bytes.fromhex("190000000000") + KEY)                 # peer announce
    app(k, bytes([0, 0x16, 5, k, 0x64, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0]))        # status 5 -> game join
    time.sleep(1.5)
    flags = rd(0x44DFA0 + k, 1)[0]
    if flags == 0:
        print(f"  slot {k} not reserved by the host (is the 12v12 patch on? are you host?) - skipping")
        return
    team = "terr" if (flags >> 4) & 3 == 2 else "seal"
    app(k, persona(name, team, 0x80000000 | (0x20 + k), owner=k))                       # persona owned by k
    time.sleep(0.5)
    info = bytearray(INFO)
    info[2] = k                                                                           # player index
    nm = name.encode()[:9]
    info[0x18:0x18 + 10] = nm + bytes(10 - len(nm))
    app(k, bytes(info))
    print(f"  {name} -> slot {k} ({team})")

def remove(k):
    print(api(f"/fake/leave?index={k}"))

def status():
    print(api("/worlds"))
    print("SEAL/TERR/SPEC:", [struct.unpack("<i", rd(a, 4))[0] for a in (0x44DE68, 0x44DE70, 0x44DE78)])
    for i in range(24):
        pid, rec = struct.unpack("<iI", rd(0x44DEE0 + i * 8, 8))
        if rec:
            name = rd(rec + 14, 16).split(b"\0")[0].decode("latin1")
            print(f"  slot {i:2}: id={pid:3} {name}")

def rng(argv):
    a = int(argv[0]); b = int(argv[1]) if len(argv) > 1 else a
    return range(a, b + 1)

if __name__ == "__main__":
    cmd, rest = (sys.argv[1], sys.argv[2:]) if len(sys.argv) > 1 else ("", [])
    if cmd == "patch":
        patch()
    elif cmd == "add":
        for k in rng(rest): add(k)
    elif cmd == "remove":
        for k in rng(rest): remove(k)
    elif cmd == "status":
        status()
    else:
        print(__doc__)
