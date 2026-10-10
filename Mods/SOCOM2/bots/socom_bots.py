"""SOCOM II 12v12 lobby bots (PCSX2 testing).

Needs: PCSX2 with the CodeDesigner bridge on 127.0.0.1:8765, and the multiplayer server started with
the debug API on port 10090 ($env:Online__DebugApiPort = '10090'). You must be the HOST, sitting in a game lobby.

    python socom_bots.py login              auto-login with the account in ../auto_login.local.json (git-ignored)
    python socom_bots.py patch              apply the 12v12 codes (re-run after every login)
    python socom_bots.py apply offline_maps_online.codes.txt   apply any compiled code list (also re-run after login)
    python socom_bots.py add 1 23           add bots BOT1..BOT23 (index range)
    python socom_bots.py add 5              add one bot, BOT5
    python socom_bots.py spawns             list the map's own spawn points per team (read from the game)
    python socom_bots.py spawn all          in a round: give every bot a soldier at the spawn the game assigns it
    python socom_bots.py spawn terr here    only TERR bots, at YOUR current spot instead (also: seal)
    python socom_bots.py capture mp61 terr  save your current position as a map/team spawn in spawns.json
    python socom_bots.py where              print your soldier's position (x y z)
    python socom_bots.py tp X Y Z           teleport your soldier
    python socom_bots.py hold X Y Z         pin your soldier there every frame (main-loop hook); unhold releases
    python socom_bots.py ready 1 23         bots press READY in the lobby (unready 1 23 to undo)
    python socom_bots.py chat 1 "hello"     BOT1 says hello in the lobby chat (server-side Medius chat)
    python socom_bots.py matchchat 1 "hi"   BOT1 says hi in the match chat (DME 02 09; channel 0 all, 1/2 team)
    python socom_bots.py team terr 1 4      switch bots 1-4 to TERR (or seal), lobby or in game
    python socom_bots.py armory 3 M4A1 "Mark 23" M67 AN-M8 C4   bot 3's loadout, used by the next spawn
    python socom_bots.py move 1 X Y Z       move BOT1's soldier (in a round, after spawn)
    python socom_bots.py stance 1 2         BOT1 stance field (1/2 seen in captures)
    python socom_bots.py aim 1 -20          BOT1 aim pitch
    python socom_bots.py shoot 1 5          BOT1 fires 5 times (SealState shot_count +1 per shot)
    python socom_bots.py myteam seal         switch your own side mid-round (seal / terr)
    python socom_bots.py aistate surrender  push an AI behaviour on the nearest AI (add 'all'); names in AI_STATES
    python socom_bots.py ai m51_terror01 here team=0x80000200 lib=//sp/m51/clib   campaign terrorist in MP
    python socom_bots.py loadlib //mp/mp53/clib   load another map's character library (hostage models) at runtime
    python socom_bots.py loadpack mhost mc       load a build_charpack.py pack (//mp/mhost/clib) from the memory card
    python socom_bots.py loadpack mhost mem      ... or straight from this PC over the bridge (no card needed)
    python socom_bots.py inject //mp/mp53/clib   load another map's characters at the NEXT map load
    python socom_bots.py inject-status
    python socom_bots.py aifollow [all]     nearest (or every) AI follows you
    python socom_bots.py ai-list            the round's mission characters (VIPs / hostages)
    python socom_bots.py ai mp_algerian_h1 here   spawn another one as AI next to you (uses the round's own record)
    python socom_bots.py ai ca_7199a995         spawn a loaded model no chartype names (converted CA characters)
    python socom_bots.py ai mp_algerian_h1 here team=0x80020000   same, on the terrorist side
    python socom_bots.py bomb [x y z]       spawn the MP bomb at your feet (or x y z)
    python socom_bots.py action 1 1 34 4    BOT1 soldier action event 02 02 (action arg arg2 [flag])
    python socom_bots.py grenade 1          BOT1 throws a frag at you (or: grenade 1 X Y Z)
    python socom_bots.py act 1 field=value  any SealState field (see ../packets/objects.json)
    python socom_bots.py remove 1 23        remove bots
    python socom_bots.py status             show DME world + host slot table
"""
import base64, re, json, os, socket, struct, sys, time, urllib.error, urllib.parse, urllib.request

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

def apply_codes(path):
    """Write a compiled code list (ADDR VALUE per line): code-cave words first, game-code hooks last, then verify."""
    if not os.path.isabs(path) and not os.path.exists(path):
        path = os.path.join(HERE, "..", path)
    codes = [(int(a, 16) & 0x1FFFFFF, int(v, 16)) for a, v in (l.split() for l in open(path) if l.strip())]
    cave = [c for c in codes if c[0] < 0x00100000]
    hooks = [c for c in codes if c[0] >= 0x00100000]
    for a, v in cave + hooks:
        wr32(a, v)
    ok = sum(struct.unpack("<I", rd(a, 4))[0] == v for a, v in codes)
    print(f"{os.path.basename(path)}: {ok}/{len(codes)} words applied ({len(cave)} code, {len(hooks)} hooks)")

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

def host_soldier():
    """The host's latest SealObject create record (from its round-start broadcast) and its current position.

    Create record: [00 04][owner][type 0][..][objid:4 @+10][stamp][0]["SealObject"] data: pos f32 x3 @+0x26,
    team @+0x56 (0x40000001 SEAL / 0x80000100 TERR), character model name @+0x62 (e.g. mp61_seal1).
    Updates: [00 03][00][n][objid:2] then n x [field:1][f32], field 0/1/2 = x/y/z.
    """
    lines = [l.split() for l in api("/fake/inbox").splitlines() if " 0->-1 " in l]
    rec, pos = None, None
    for _, _, _, hx in lines:
        b = bytes.fromhex(hx)
        i = b.find(b"SealObject\0")
        if i >= 0x16 and b[i - 0x16:i - 0x14] == b"\x00\x04" and b[i - 0x14] == 0:
            start = i - 0x16
            nxt = b.find(b"\x00\x04", start + 0xB2)
            rec = bytearray(b[start:start + 0xB2])
            pos = list(struct.unpack_from("<3f", rec, 0x26))
            obj = struct.unpack_from("<I", rec, 10)[0]
        elif rec and b[:3] == b"\x00\x03\x00" and len(b) >= 6 and b[4] | b[5] << 8 == obj:
            p = 6
            for _ in range(b[3]):
                if p + 5 > len(b) or b[p] > 2:
                    break
                pos[b[p]] = struct.unpack_from("<f", b, p + 1)[0]
                p += 5
    return rec, pos

# -- the game's own spawn points ------------------------------------------------
# Spawn manager 0x00669B30 (loaded from the map's AIMAPS.MPS):
#   +0 grid count, +8 grid list (node: +4 next, +8 grid*), +0x34 point count, +0x38 point array (12 bytes each).
# Point: grid = b0 & 0x3F; xcell = (w0 >> 6) & 0x1FFF; zcell = (w0 >> 19) & 0x1FFF; group = u16@4 >> 4;
#        facing = b4 & 0xF. Groups are sorted; MP uses group 0 = SEAL, group 2 = TERR (0x002C4D20), and a
#        player's point = first + (DME index % count) (0x002B9C90 -> 0x0052E6E0, index = soldier+0xFC8).
# World (0x005305A0): x = g.minX + (xcell+0.5)*g.cellX, z = g.minZ + (zcell+0.5)*g.cellZ,
#        y = g.minY + ((cell.w0 >> 9) & 0x3FF) * g.yScale  (cell lookup 0x00511F80, height 0x00516A80).
SPAWN_MGR = 0x00669B30
TEAM_GROUP = {"seal": 0, "terr": 2}

def _u32(a): return struct.unpack("<I", rd(a, 4))[0]
def _f32(a): return struct.unpack("<f", rd(a, 4))[0]

def game_spawn_points(group):
    """[(x, y, z, facing)] of a spawn group, decoded exactly the way the game does."""
    grids, node = [], _u32(SPAWN_MGR + 8)
    for _ in range(_u32(SPAWN_MGR)):
        grids.append(_u32(node + 8))
        node = _u32(node + 4)
    n, arr = _u32(SPAWN_MGR + 0x34), _u32(SPAWN_MGR + 0x38)
    if n == 0 or not arr:                                             # campaign maps: no spawn points
        return []
    raw = rd(arr, n * 12)
    out = []
    for i in range(n):
        w0 = struct.unpack_from("<I", raw, i * 12)[0]
        if struct.unpack_from("<H", raw, i * 12 + 4)[0] >> 4 != group:
            continue
        g = grids[raw[i * 12] & 0x3F]
        xc, zc = (w0 >> 6) & 0x1FFF, (w0 >> 19) & 0x1FFF
        x = _f32(g) + (xc + 0.5) * _f32(g + 0x20)
        z = _f32(g + 8) + (zc + 0.5) * _f32(g + 0x24)
        y = _f32(g + 4)
        if zc < _u32(g + 0x1C):                                       # row table: {u16 xmin, u16 xmax, u32 first}
            xmin, xmax, first = struct.unpack("<HHI", rd(_u32(g + 0x154) + zc * 12, 8))
            if xmin <= xc < xmax:
                cells = _u32(_u32(_u32(g + 0xDC)) + 0x28)
                cw = _u32(cells + (first + xc - xmin) * 8)
                y += ((cw >> 9) & 0x3FF) * _f32(g + 0xE8)
        out.append((x, y + 1.0, z, raw[i * 12 + 4] & 0xF))          # +1.0: placement lift at 0x002B9E5C
    return out

def spawns_cmd():
    for team, grp in TEAM_GROUP.items():
        pts = game_spawn_points(grp)
        print(f"{team}: {len(pts)} points (group {grp})")
        for i, (x, y, z, f) in enumerate(pts):
            print(f"  {i:2}: {x:8.2f} {y:8.2f} {z:8.2f}  facing {f}")

SPAWNS = os.path.join(HERE, "spawns.json")

def load_spawns():
    try:
        with open(SPAWNS) as f:
            return json.load(f)
    except FileNotFoundError:
        return {}

def capture(map_name, team):
    """Save your soldier's current position as MAP's TEAM spawn (stand where that team spawns)."""
    x, y, z = struct.unpack("<3f", rd(host_seal() + 0x1C, 12))
    spawns = load_spawns()
    spawns.setdefault(map_name, {})[team] = [round(x, 2), round(y, 2), round(z, 2)]
    with open(SPAWNS, "w") as f:
        json.dump(spawns, f, indent=2)
    print(f"{map_name} {team} spawn = {x:.2f} {y:.2f} {z:.2f}")

def spawn(which, here=False):
    rec, pos = host_soldier()
    if rec is None:                     # no round-start broadcast recorded: use a saved SealObject record
        tpl = os.path.join(HERE, "sealobject_mp61.hex")
        rec = bytearray(bytes.fromhex(open(tpl).read()))
        pos = list(struct.unpack("<3f", rd(host_seal() + 0x1C, 12)))
        print("using saved SealObject template")
    prefix = rd(0x004417C0, 4).split(b"\0")[0].decode()                        # current map id, e.g. mp61
    points = {} if here else {t: game_spawn_points(g) for t, g in TEAM_GROUP.items()}
    table = load_spawns().get(prefix, {})
    if not here:
        for t in TEAM_GROUP:                          # no MP points (campaign map): offline-maps spawn table
            if not points[t] and t in table:
                points[t] = [tuple(table[t]) + (0,)]
    host_model = rec[0x62:0x72].split(b"\0")[0].decode()
    print(f"host at x={pos[0]:.1f} y={pos[1]:.1f} z={pos[2]:.1f}, map {prefix}, "
          + ("bots at your position" if here else f"spawn points: seal {len(points['seal'])}, terr {len(points['terr'])}"))
    counts = {}
    n = 0
    for k in range(1, 24):
        pid, prec = struct.unpack("<iI", rd(0x44DEE0 + k * 8, 8))
        if not prec:
            continue
        team = "terr" if struct.unpack("<I", rd(prec, 4))[0] == TEAM["terr"] else "seal"
        if which != "all" and which != team:
            continue
        m = bytearray(rec)
        m[2] = k                                                                  # owner
        struct.pack_into("<I", m, 10, 0x60 + k)                                   # object id
        if len(points.get(team, [])) > 1:                                         # the game's own point for index k
            x, y, z, _ = points[team][k % len(points[team])]
        elif points.get(team):                                                    # single fallback point: spread out
            i = counts[team] = counts.get(team, -1) + 1
            bx, by, bz, _ = points[team][0]
            x, y, z = bx + 2.0 * (i % 4), by, bz + 2.0 * (i // 4)
        else:                                                                     # 'here': grid at your spot
            i = counts[team] = counts.get(team, -1) + 1
            x, y, z = pos[0] + 2.0 * (i % 4), pos[1], pos[2] + 2.0 * (i // 4)
        struct.pack_into("<3f", m, 0x26, x, y, z)
        struct.pack_into("<I", m, 0x56, TEAM[team])
        struct.pack_into("<H", m, 0x26 + 0x74, 0x20 + 0x60 + k)                   # entity_ref = 0x20 + own entity index (host: 0x20 + 3)
        loadout = load_loadouts().get(str(k))
        if loadout:                                                               # bot armory (armory command)
            apply_loadout(m, loadout)
        if prefix.startswith("mp"):
            model = f"{prefix}_{'terror' if team == 'terr' else 'seal'}{k % 4 + 1}"
        elif team == "terr" and table.get("terr_models"):
            model = table["terr_models"][k % 4]
        else:
            model = host_model                                                    # campaign SEAL: copy the host's
        model = model.encode()
        m[0x62:0x72] = model + bytes(16 - len(model))
        app(k, bytes(m))
        print(f"  BOT{k} soldier ({team}, {model.decode()})")
        n += 1

def host_seal():
    """The host's soldier entity ("seal pointer"): SealObject object with owner 0 -> data (+0x1C) -> seal (+0x8C)."""
    r = bridge({"Command": "SCANPATTERN", "SearchPattern": list(b"SealObject\0"), "Region": "ee"})
    for a in r.get("MatchAddresses") or []:
        hdr = struct.unpack("<8I", rd(a - 0xC, 32))
        if hdr[0] == 0x01020000 and hdr[2] == 0:
            return struct.unpack("<I", rd(hdr[7] + 0x8C, 4))[0]
    sys.exit("Host soldier not found - are you in a round?")

def where():
    x, y, z = struct.unpack("<3f", rd(host_seal() + 0x1C, 12))
    print(f"{x:.1f} {y:.1f} {z:.1f}")

def teleport(x, y, z):
    """Write the new position into every copy of your soldier's current position (soldier, physics, network data).
    Writing only some of them gets overwritten from the others within a frame. Your soldier is the Seal Pointer
    (0x0044D648) - host_seal() can return a spawned AI - and move_body keeps other bodies' copies out of it."""
    me = struct.unpack("<I", rd(0x0044D648, 4))[0]
    move_body(me, (x, y, z))
    print(f"  you: {' '.join(f'{v:.1f}' for v in my_pos())}")

HOLD_CODES = os.path.join(HERE, "..", "hold_position.codes.txt")
HOLD_HOOK = 0x001E70E4

def hold(x, y, z):
    """Pin the host's soldier at x y z every frame (main-loop hook, see hold_position.cds)."""
    codes = [(int(a, 16) & 0x1FFFFFF, int(v, 16)) for a, v in (l.split() for l in open(HOLD_CODES) if l.strip())]
    for a, v in codes:                                  # stub + mailbox first, hook word last
        if a != HOLD_HOOK and a >= 0x000D0520:
            wr32(a, v)
    wr32(0x000D0504, host_seal())
    for i, f in enumerate((x, y, z)):
        wr32(0x000D0508 + 4 * i, struct.unpack("<I", struct.pack("<f", f))[0])
    wr32(0x000D0500, 1)
    wr32(HOLD_HOOK, dict(codes)[HOLD_HOOK])
    print(f"holding at {x} {y} {z} (unhold to release)")

def unhold():
    wr32(0x000D0500, 0)
    print("released")

def set_ready(k, ready=True):
    """Lobby READY toggle: object update on the bot's PersonaObject (id 0x20+k), field 9 = 2 ready / 1 not ready.
    Captured from the host: [00 03][01][n=1][objid:2][field 09][value]."""
    app(k, bytes([0x00, 0x03, 0x01, 0x01, 0x20 + k, 0x00, 0x09, 2 if ready else 1]))
    print(f"  BOT{k} {'ready' if ready else 'not ready'}")

# -- armory -----------------------------------------------------------------------
# The lobby armory is local; a player's loadout only goes out in its SealObject create record:
# five 6-byte slots [ff][item id][u32 0] at record +0x82/+0x88/+0x8E/+0x94/+0x9A (primary, secondary,
# three items), and the primary id again at +0x52. Item ids come from zweapon.rdr (weapons.json).
LOADOUT_SLOTS = (0x82, 0x88, 0x8E, 0x94, 0x9A)
LOADOUTS = os.path.join(HERE, "loadouts.json")

def weapon_id(name):
    with open(os.path.join(HERE, "weapons.json")) as f:
        table = json.load(f)
    if name.lower() in ("none", "-", ""):
        return 0
    if name.lower().startswith("0x") or name.isdigit():
        return int(name, 0)
    for n, w in table.items():
        if name.lower() in (n.lower(), w["display"].lower()) and w["id"]:
            return w["id"]
    sys.exit(f"unknown weapon '{name}' - see weapons.json (names or display names, or a hex id)")

def load_loadouts():
    try:
        with open(LOADOUTS) as f:
            return json.load(f)
    except FileNotFoundError:
        return {}

def armory(k, names):
    """Save bot k's loadout (up to 5 names: primary secondary item item item); used by spawn."""
    ids = [weapon_id(n) for n in names][:5]
    data = load_loadouts()
    data[str(k)] = names[:5]
    with open(LOADOUTS, "w") as f:
        json.dump(data, f, indent=2)
    print(f"  BOT{k} armory: " + ", ".join(f"{n} (0x{i:02X})" for n, i in zip(names, ids)))

def apply_loadout(rec, names):
    for slot, name in zip(LOADOUT_SLOTS, names):
        wid = weapon_id(name)
        rec[slot:slot + 6] = bytes([0xFF if wid else 0x00, wid]) + bytes(4)
    if names:
        rec[0x52] = weapon_id(names[0])

def lobby_chat(k, text):
    """Lobby / game-lobby chat as BOTk. Lobby chat is Medius (MLS), so the server sends a ChatFwdMessage
    to the host's lobby world on the bot's behalf (debug API /chat)."""
    print(api(f"/chat?name=BOT{k}&text={urllib.parse.quote(text)}"))

def match_chat(k, text, channel=0):
    """In-match chat: DME game message 02 09 [u16 len(text)+6][u16 channel][text\0][pad]."""
    raw = text.encode("latin1")[:58]
    size = len(raw) + 6
    body = struct.pack("<HH", size, channel) + raw + b"\x00"
    app(k, bytes([0x02, 0x09]) + body + bytes(size - len(body)))
    print(f"  BOT{k}: {text}")

# -- auto login (Mods/SOCOM2/auto_login.cds) -------------------------------------
# The login connect function 0x00276040 and the have-credentials check 0x0027EEA0 look up PLAYERNAMEVAR /
# PLAYERPASSWORD through 0x003A5AD0; four jal hooks point those lookups at stubs returning fixed string
# variants { type 3, char* } whose strings come from auto_login.local.json (git-ignored, filled in by you).
LOGIN_FILE = os.path.join(HERE, "..", "auto_login.local.json")
LOGIN_CODE = [                                   # compiled auto_login.cds, minus its placeholder strings
    (0x000D0400, 0x00000003), (0x000D0404, 0x000D0440),       # name variant -> 0x000D0440
    (0x000D0408, 0x00000003), (0x000D040C, 0x000D0460),       # password variant -> 0x000D0460
    (0x000D0410, 0x3C02000D), (0x000D0414, 0x03E00008), (0x000D0418, 0x24420400),   # name_lookup
    (0x000D041C, 0x3C02000D), (0x000D0420, 0x03E00008), (0x000D0424, 0x24420408),   # pass_lookup
]
LOGIN_HOOKS = [(0x002760B4, 0x0C034104), (0x002760D4, 0x0C034107),   # login connect: name / password
               (0x0027EEC0, 0x0C034104), (0x0027EEE0, 0x0C034107),   # have-credentials check 0x0027EEA0
               (0x0027EF80, 0x0C034104), (0x0027EFA0, 0x0C034107),   # second have-credentials check
               (0x0027676C, 0x0C034104), (0x00277258, 0x0C034104)]   # name reads for the login screen
# Not hooked on purpose: 0x002763F4/0x00276444/0x002772E4/0x0027734C/0x002778A4/0x002778EC pass the variable
# to 0x0031A8B0/0x0031A7B0, which store into it (profile / network load) - they must keep the real variable.

def login():
    if not os.path.exists(LOGIN_FILE):
        with open(LOGIN_FILE, "w") as f:
            json.dump({"username": "", "password": ""}, f, indent=2)
        sys.exit(f"Created {os.path.normpath(LOGIN_FILE)} - put your account name and password in it, then run login again.")
    with open(LOGIN_FILE) as f:
        cfg = json.load(f)
    user, pw = cfg.get("username", ""), cfg.get("password", "")
    if not user or not pw:
        sys.exit(f"Fill in username and password in {os.path.normpath(LOGIN_FILE)} first.")
    for label, s in (("username", user), ("password", pw)):
        if len(s.encode("latin1")) > 31:
            sys.exit(f"{label} is longer than 31 characters")
    for a, v in LOGIN_CODE:
        wr32(a, v)
    for base, s in ((0x000D0440, user), (0x000D0460, pw)):
        raw = s.encode("latin1").ljust(32, b"\x00")
        for i in range(0, 32, 4):
            wr32(base + i, struct.unpack_from("<I", raw, i)[0])
    for a, v in LOGIN_HOOKS:
        wr32(a, v)
    ok = sum(struct.unpack("<I", rd(a, 4))[0] == v for a, v in LOGIN_CODE + LOGIN_HOOKS)
    stored = rd(0x000D0440, 32).split(b"\x00")[0].decode("latin1")
    print(f"auto-login installed ({ok}/{len(LOGIN_CODE) + len(LOGIN_HOOKS)} words) for account '{stored}'")
    set_ui_vars([("PLAYERNAMEVAR", 0x000D0440), ("PLAYERPASSWORD", 0x000D0460)])
    ui_command(7, "connect_btn")                          # EnableButton connect_btn
    print("  CONNECT unlocked - select it and press X")

UI_QUEUE, UI_QUEUE_COUNT, UI_QUEUE_INDEX, UI_QUEUE_STRIDE = 0x00441890, 0x004420B0, 0x004420B4, 52

def ui_command(cmd, arg):
    """Queue a UI command the way the script natives do (EnableButton 0x0027D470 = cmd 7, DisableButton = 0x0027D370):
    index += 1, slot[index] = {u32 cmd, char arg[48]} at 0x00441890 + index*52, count += 1. The UI drains it next frame."""
    idx = struct.unpack("<i", rd(UI_QUEUE_INDEX, 4))[0] + 1
    raw = struct.pack("<I", cmd) + arg.encode("latin1")[:47].ljust(48, b"\x00")
    slot = UI_QUEUE + idx * UI_QUEUE_STRIDE
    for j in range(0, UI_QUEUE_STRIDE, 4):
        wr32(slot + j, struct.unpack_from("<I", raw, j)[0])
    wr32(UI_QUEUE_INDEX, idx)
    wr32(UI_QUEUE_COUNT, struct.unpack("<i", rd(UI_QUEUE_COUNT, 4))[0] + 1)

UIVAR_CODES = os.path.join(HERE, "..", "set_uivars.codes.txt")
MAIN_LOOP_HOOK = 0x001E70E4

def set_ui_vars(pairs):
    """Set UI string variables through the game's own find/set (Mods/SOCOM2/set_uivars.cds): runs a one-shot
    stub from the main-loop hook, waits for it, then restores the hook word. pairs = [(var name, value addr)]."""
    codes = [(int(a, 16) & 0x1FFFFFF, int(v, 16)) for a, v in (l.split() for l in open(UIVAR_CODES) if l.strip())]
    for a, v in codes:
        wr32(a, v)
    names_at = 0x000D04B0
    table = []
    for i, (name, value_addr) in enumerate(pairs):
        raw = name.encode().ljust(16, b"\x00")
        for j in range(0, 16, 4):
            wr32(names_at + 16 * i + j, struct.unpack_from("<I", raw, j)[0])
        table += [names_at + 16 * i, value_addr]
    for i, w in enumerate(table + [0, 0]):
        wr32(0x000D0490 + 4 * i, w)
    old = struct.unpack("<I", rd(MAIN_LOOP_HOOK, 4))[0]
    wr32(0x000D0480, 1)
    wr32(MAIN_LOOP_HOOK, 0x0C000000 | (0x000D0600 >> 2))
    for _ in range(40):
        time.sleep(0.05)
        if struct.unpack("<I", rd(0x000D0480, 4))[0] == 0:
            break
    else:
        wr32(0x000D0480, 0)
        print("  (the game did not run the stub - is it paused?)")
    wr32(MAIN_LOOP_HOOK, old)
    print("  set " + ", ".join(n for n, _ in pairs) + " in the game's UI variables")

CALL_CODES = os.path.join(HERE, "..", "game_call.codes.txt")
CALL_FLAG, CALL_TABLE, CALL_BUF = 0x000D06F0, 0x000D0700, 0x000D0780

def game_call(calls, wait=3.0, serve=None):
    """Run game functions once from the main loop (Mods/SOCOM2/game_call.cds). calls = [(func, a0, a1, a2, a3)],
    up to 6, args as ints. Returns each call's v0. serve() runs on every poll while the calls are running (loadpack
    mem answers the game's archive reads from it)."""
    for a, v in ((int(a, 16) & 0x1FFFFFF, int(v, 16)) for a, v in (l.split() for l in open(CALL_CODES) if l.strip())):
        wr32(a, v)
    for i, c in enumerate(calls):
        for j, w in enumerate((list(c) + [0, 0, 0, 0])[:5]):
            wr32(CALL_TABLE + 20 * i + 4 * j, w & 0xFFFFFFFF)
    wr32(CALL_TABLE + 20 * len(calls), 0)
    old = struct.unpack("<I", rd(MAIN_LOOP_HOOK, 4))[0]
    wr32(CALL_FLAG, 1)
    wr32(MAIN_LOOP_HOOK, 0x0C000000 | (0x000D0800 >> 2))
    deadline = time.time() + wait
    while time.time() < deadline:
        if not (serve and serve()):                          # serve() -> True when it just answered a request
            time.sleep(0.02 if serve else 0.05)
        if struct.unpack("<I", rd(CALL_FLAG, 4))[0] == 0:
            break
    else:
        wr32(CALL_FLAG, 0)
        wr32(MAIN_LOOP_HOOK, old)
        sys.exit("  the game did not run the call stub - is it paused?")
    wr32(MAIN_LOOP_HOOK, old)
    return [struct.unpack("<I", rd(CALL_TABLE + 20 * i, 4))[0] for i in range(len(calls))]

GAME_PTR = 0x004446F8
BOMB_START, BOMB_PLACE = 0x002C36F0, 0x002C3860              # StartBomb (spawn point "StartBomb"), place at vec3*
BOMB_GLOBALS = 0x0044DDD0                                    # +1 placed, +4 pos vec3, +0x10 map item, +0x1C object id

def bomb(pos=None):
    """Spawn the MP demolition bomb (the game's own StartBomb, which creates the networked BombObject),
    then drop it at pos (default: at your feet)."""
    game = struct.unpack("<I", rd(GAME_PTR, 4))[0]
    if not game:
        sys.exit("not in a round")
    pos = pos or tuple(struct.unpack("<3f", rd(host_seal() + 0x1C, 12)))
    for i, f in enumerate(pos):
        wr32(CALL_BUF + 4 * i, struct.unpack("<I", struct.pack("<f", f))[0])
    flags = rd(game + 0xD9, 2)
    wr8 = lambda a, v: wr32(a & ~3, (struct.unpack("<I", rd(a & ~3, 4))[0] & ~(0xFF << 8 * (a & 3))) | (v << 8 * (a & 3)))
    wr8(game + 0xDA, 0)                                      # "bomb started" gate - StartBomb only runs once per round
    game_call([(BOMB_START,), (BOMB_PLACE, CALL_BUF)])
    g = rd(BOMB_GLOBALS, 0x20)
    objid = struct.unpack_from("<I", g, 0x1C)[0]
    bx, by, bz = struct.unpack_from("<3f", g, 4)
    print(f"  bomb object #{objid} at {bx:.1f} {by:.1f} {bz:.1f} (was started: {flags[1]})")

SPAWN_FROM, SPAWN_CTX = 0x002B42E0, 0x00442EF0              # SpawnCharacterFrom(ctx, charRecord*, isPlayer)
CHAR_TABLE, CHARTYPES, CHARTYPE_COUNT = 0x00444704, 0x004500E0, 0x004500DC
MODEL_FIND = 0x0033D320                                      # loaded-model lookup by name (0 = not in memory)
AI_RECORDS, AI_RECORD_SLOTS = 0x000D2000, 40                  # our 0x160-byte character records (0xD2000..0xD5700)
AI_NEXT_SLOT = 0x000D1FFC                                     # next free record slot - a spawned AI keeps using its record

def chartype(name, required=True):
    """Registered chartype record by name -> (record, model name pointer)."""
    arr = struct.unpack("<I", rd(CHARTYPES, 4))[0]
    for i in range(struct.unpack("<I", rd(CHARTYPE_COUNT, 4))[0]):
        rec = struct.unpack("<I", rd(arr + 4 * i, 4))[0]
        if rd(struct.unpack("<I", rd(rec, 4))[0], 32).split(b"\0")[0].decode("latin1") == name:
            return rec, struct.unpack("<I", rd(rec + 0xC, 4))[0]
    if required:
        sys.exit(f"no chartype '{name}'")
    return None, None

INSTANTIATE = 0x00335330                                     # zdb: Instantiate(model, vec3* pos, rot*) -> new CNode
WORLD_ROOT = 0x0045F750                                      # *(.) = the map's root CNode ("mp61"); vtable+0x18 AddChild

def spawn_prop(model, pos=None):
    """Place a loaded model in the world as a static object (map item / prop): the game's own path (0x003F3900):
    node = Instantiate(model, &pos, 0); (*(0x0045F750))->vtable[0x18](root, node). Returns the new node (zdb::CNode:
    its world matrix is the first member, +0x30 = position)."""
    require_round()
    _put_str(MODEL_NAME_BUF, model)
    m = game_call([(MODEL_FIND, MODEL_NAME_BUF)])[0]
    if not m:
        sys.exit(f"model '{model}' is not loaded on this map - loadpack its pack first")
    pos = pos or my_pos((8.0, 0.0, 8.0))
    for i, f in enumerate(pos):
        wr32(CALL_BUF + 4 * i, struct.unpack("<I", struct.pack("<f", f))[0])
    node = game_call([(INSTANTIATE, m, CALL_BUF, 0)])[0]
    if not node:
        sys.exit("Instantiate returned nothing")
    root = struct.unpack("<I", rd(WORLD_ROOT, 4))[0]
    add_child = struct.unpack("<I", rd(struct.unpack("<I", rd(root + 0x60, 4))[0] + 0x18, 4))[0]
    game_call([(add_child, root, node)])
    print(f"  {model}: node {node:#x} at {pos[0]:.1f} {pos[1]:.1f} {pos[2]:.1f}")
    return node

MODEL_DONOR = "mp_pow1"                                      # chartype lent to models that have none (ai ca_<hash>)
MODEL_NAME_BUF = 0x000D1F80                                  # its borrowed model name string

def spawn_model(model, pos=None, team=0x40020000, slot=None):
    """Spawn a loaded model that no chartype names (e.g. a converted Combined Assault character, ca_<hash>): lend it
    the MODEL_DONOR chartype for the spawn - its model pointer points at our name, then is put back."""
    require_round()
    _put_str(MODEL_NAME_BUF, model)
    if not game_call([(MODEL_FIND, MODEL_NAME_BUF)])[0]:
        sys.exit(f"model '{model}' is not loaded - loadpack its pack first")
    rec, old = chartype(MODEL_DONOR)
    wr32(rec + 0xC, MODEL_NAME_BUF)
    try:
        body = spawn_ai(MODEL_DONOR, slot, pos, team)
        time.sleep(1.0)                                      # the body resolves its model during the spawn frames
    finally:
        wr32(rec + 0xC, old)
    return body

ASSET_ACQUIRE, LIB_PATH_BUF = 0x0033E380, 0x000D0900       # AssetLib_Acquire(path); path string buffer

ZDB_CTX, ZDB_RESET, ZDB_MOUNT = 0x004A4DD0, 0x003C8B80, 0x003C8C80   # the one mounted map archive (RUN/<NAME>.ZDB)
WORLD_LIBS = 0x0045F790
PLACE_LIB, PLACE_LIB_CODES = 0x000D1400, os.path.join(HERE, "..", "place_lib.codes.txt")   # VRAM/palette placement
PLACEMENT = {                                                # library category (renderphase) -> place_lib.cds args:
    0x10: (0x1D30, 3, 1),                                    # characters: pools 0x004A1D30/34/38, clear pool+4
    0x02: (0x1C88, 2, 0),                                    # weapons / gear: 0x004A1C88/8C (TexCommonCS:B1/B2)
    0x0B: (0x1CF4, 2, 0),                                    # alpha: 0x004A1CF4/F8 (TexAlpha:B1/B2) - roomy, for
                                                             #   converted props (TexWorld, 0x00, fills up fast)
    0x11: (0x1D3C, 3, 1),                                    # vehicles / turrets: 0x004A1D3C/40/44
    0x00: (0x1C70, 1, 0),                                    # map props (the map's own library): 0x004A1C70
}
LIB_MODELS, LIB_MODELS_CODES = 0x000D1000, os.path.join(HERE, "..", "lib_models.codes.txt")   # <lib>_mdl.zed loader

def _put_str(addr, text):
    raw = text.encode() + b"\0"
    raw = raw.ljust((len(raw) + 3) & ~3, b"\0")
    for i in range(0, len(raw), 4):
        wr32(addr + i, struct.unpack_from("<I", raw, i)[0])

FREE_GUARD_CODES = os.path.join(HERE, "..", "free_guard.codes.txt")
FREE_GUARD_FLAG, FREE_GUARD_SKIPPED = 0x000D06F8, 0x000D06FC

def install_free_guard():
    """Free/delete guard (Mods/SOCOM2/free_guard.cds): stubs first, then each 2-word entry patch in ONE write so the
    game never runs a half-patched function entry. Idempotent."""
    codes = [(int(a, 16) & 0x1FFFFFF, int(v, 16)) for a, v in (l.split() for l in open(FREE_GUARD_CODES) if l.strip())]
    entries = {0x00180280, 0x00180284, 0x00180AD0, 0x00180AD4}
    for a, v in codes:
        if a not in entries:
            wr32(a, v)
    for base in (0x00180280, 0x00180AD0):
        pair = struct.pack("<II", dict(codes)[base], dict(codes)[base + 4])
        bridge({"Command": "WRITEMEM", "Address": base, "Data": pair.hex(), "Region": "ee"})

def loaded_libs():
    """Paths of the asset libraries in the world list -> (lib object, has data)."""
    out = {}
    node = struct.unpack("<I", rd(WORLD_LIBS + 8, 4))[0]
    while node != WORLD_LIBS + 4:
        lib = struct.unpack("<I", rd(node + 8, 4))[0]
        out[rd(struct.unpack("<I", rd(lib, 4))[0], 64).split(b"\0")[0].decode("latin1")] = (lib, any(rd(lib + 8, 8)))
        node = struct.unpack("<I", rd(node + 4, 4))[0]
    return out

HEAP_TOP, HEAP_LIMIT = 0x001CC768, 0x01DC0000                # newlib top chunk pointer; highest heap end seen (MP61 /
                                                              # M51: a fixed block starts at 0x01DC0000)

def heap_room():
    """Bytes the heap can still hand out from its top: the top chunk plus growth up to HEAP_LIMIT (free-list holes
    are not counted, so this is a lower bound)."""
    top = struct.unpack("<I", rd(HEAP_TOP, 4))[0]
    size = struct.unpack("<I", rd(top + 4, 4))[0] & ~3
    return max(0, HEAP_LIMIT - top - 16) if top + size <= HEAP_LIMIT else size

PACK_STREAM_CODES = os.path.join(HERE, "..", "pack_stream.codes.txt")
MEM_MOUNT, MEM_UNMOUNT = 0x000D6000, 0x000D6040                 # pack_stream.cds
PACK_MAILBOX, PACK_STREAM, PACK_TABLE = 0x000D7000, 0x000D7040, 0x000D7800   # data page, apart from the code
PACK_ROOTS = [os.path.join(HERE, "..", "packs"), "F:/SOCOM Packs/SOCOM2", "F:/SOCOM Packs/SOCOMCA/packs",
              "F:/SOCOM Packs/SOCOMCA/static"] +              [f"F:/SOCOM Packs/SOCOM2/{c}" for c in ("weapons", "gear", "vehicles", "props", "effects", "objects")] +              [p for p in os.environ.get("SOCOM_PACKS", "").split(os.pathsep) if p]

def find_pack(name):
    """<root>/<name>/<NAME>.CDB in the pack library folders (build_charpack, rip_socom2, Mods/SOCOMCA/ca2s2)."""
    for root in PACK_ROOTS:
        path = os.path.join(root, name.lower(), name.upper() + ".CDB")
        if os.path.exists(path):
            return path
    sys.exit(f"no pack {name} in {PACK_ROOTS} (set SOCOM_PACKS to add a folder)")

def _pack_server(cdb):
    """serve() for game_call while pack_stream.cds is mounted: answer each read request by writing the archive bytes
    straight into the game's buffer, then release the spinning read."""
    def serve():
        if struct.unpack("<I", rd(PACK_MAILBOX, 4))[0] != 1:
            return False
        buf, pos, n = struct.unpack("<III", rd(PACK_MAILBOX + 4, 12))
        data = cdb[pos:pos + n].ljust(n, bytes(1))
        for i in range(0, n, 0x10000):
            bridge({"Command": "WRITEMEM", "Address": (buf + i) & 0x1FFFFFFF, "Data": data[i:i + 0x10000].hex(),
                    "Region": "ee"})
        wr32(PACK_MAILBOX, 0)
        serve.served += n
        return True
    serve.served = 0
    return serve

def load_pack(name, mode="mc"):
    """Load a character pack built by build_charpack.py (Mods/SOCOM2/packs/<name>/<NAME>.CDB) as library
    //mp/<name>/clib.
      mc   the game's own loader: ZDB_Mount(<NAME>) finds no RUN/<NAME>.ZDB on the disc and mounts
           mc0:/BASCUS-97275<NAME>/<NAME>.CDB (slot 1, then slot 2). Works on a retail console.
      mem  straight from this PC over the bridge (pack_stream.cds): no memory card needed - for iterating on a pack."""
    if mode not in ("mc", "mem"):
        sys.exit("mode is mc or mem")
    if not re.fullmatch(r"m[a-z0-9_]{1,6}", name.lower()):   # see build_charpack.check_pack_name
        sys.exit(f"pack names start with 'm' (the asset loader only keeps //mp/<m...>/ as a folder) and are at most "
                 f"7 characters (ZDB_Mount's memory-card path buffer is 32 bytes): {name}")
    folder = os.path.dirname(find_pack(name))               # the library name: <LIB>_MDL.ZED (CLIB, WEAP, BCAR ...)
    lib = next((f[:-8] for f in os.listdir(folder) if f.upper().endswith("_MDL.ZED")), "CLIB").lower()
    return load_lib(f"//mp/{name.lower()}/{lib}", source=mode)

VRAM_MARGIN = 0x30    # the pool's next-free moved 0x22 blocks between the check and a load once (game allocations)

def vram_need(name):
    """(VRAM blocks a pack's textures take, blocks left in its category's first pool, category). Static pools
    (*(0x004A1C48))[*(idx var)]: +0x08 start, +0x0C next free, +0x10 end (256-byte blocks). Textures are placed one
    after another from +0x0C; nothing stops them running past the end (0x3FFF = top of VRAM) into the frame buffer."""
    import build_charpack as bp
    folder = os.path.dirname(find_pack(name))
    txr = next(os.path.join(folder, f) for f in os.listdir(folder) if f.upper().endswith("_TXR.ZED"))
    nodes = bp.zed_read(open(txr, "rb").read())[1]
    category = next((struct.unpack("<I", n.data)[0] for n in nodes if n.name == "renderphase"), 0)
    need = 0
    for n in nodes:
        for t in (n.children if n.name == "textures" else []):
            td = next(c for c in t.children if c.name == "texdat").data
            w, h = struct.unpack_from("<HH", td, 0)
            need += -(-w * h * td[0x0C] // 8 // 256)
    if category not in PLACEMENT:
        return need, 1 << 30, category
    pool = struct.unpack("<I", rd(struct.unpack("<I", rd(0x004A1C48, 4))[0]
                                  + 4 * struct.unpack("<I", rd(0x004A0000 + PLACEMENT[category][0], 4))[0], 4))[0]
    nxt, end = struct.unpack("<II", rd(pool + 0x0C, 8))
    return need, max(0, end + 1 - nxt) if PLACEMENT[category][2] == 0 else 1 << 30, category   # streaming pools: n/a

VRAM_MARK = os.path.join(HERE, ".vram_mark")

def _prop_pool():
    return struct.unpack("<I", rd(struct.unpack("<I", rd(0x004A1C48, 4))[0]
                                  + 4 * struct.unpack("<I", rd(0x004A1C70, 4))[0], 4))[0]

def test_prop(pack, model, offset=(0.0, 0.0, -10.0)):
    """Iterate on a converted prop without reloading the map: the map-prop pool (TexWorld) is a bump allocator, so
    rewind its next-free (+0x0C) to where it was before the first test load, then load + spawn. Earlier test props'
    textures get overwritten (they draw garbage - ignore them). Heap still grows ~pack size per load."""
    pool = _prop_pool()
    nxt = struct.unpack("<I", rd(pool + 0x0C, 4))[0]
    current = rd(0x004417C0, 8).split(b"\0")[0].decode()
    mark = None
    if os.path.exists(VRAM_MARK):
        m_map, m_pool, m_next = open(VRAM_MARK).read().split()
        if m_map == current and int(m_pool, 16) == pool and int(m_next, 16) <= nxt:
            mark = int(m_next, 16)
    if mark is None:
        mark = nxt
        open(VRAM_MARK, "w").write(f"{current} {pool:x} {mark:x}")
    if nxt != mark:
        wr32(pool + 0x0C, mark)
        print(f"  rewound the prop texture pool {nxt:#x} -> {mark:#x}")
    load_pack(pack, "mem")
    return spawn_prop(model, my_pos(offset))

def load_lib(path, source="disc"):
    """Load another map's asset library at runtime, e.g. //mp/mp53/clib (escort hostage models alg_UN01/02),
    //mp/mp10/clib (con_pow01), //mp/mp82/clib (rus_escort01 VIPs), //sp/m51/clib (campaign terrorists).
    Libraries are read from the map's archive RUN/<MAP>.ZDB and only one archive is mounted at a time, so this
    swaps the mount: reset, mount the other map, AssetLib_Acquire(path), reset, remount the current map - all in one
    main-loop call. (AssetLib_Acquire on its own only registers an EMPTY library.)
    source: disc (RUN/<MAP>.ZDB), mc / mem (a pack's <NAME>.CDB from the memory card / from this PC - see load_pack)."""
    have = loaded_libs()
    if path in have:
        if have[path][1]:
            print(f"  {path} is already loaded")
            return have[path][0]
        sys.exit(f"{path} is registered but empty (an earlier load without the archive swap) - "
                 "reload the map to clear it, or use another map's copy of the models")
    swapped = [k for k, (l, d) in have.items() if d and "/" in k and k.strip("/").split("/")[1].upper()
               != rd(0x004417C0, 8).split(b"\0")[0].decode().upper() and k.startswith(("//mp/", "//sp/"))]
    other = path.strip("/").split("/")[1].upper()            # //mp/mp53/clib -> MP53, //sp/m51/clib -> M51
    if source == "disc" and swapped and os.environ.get("SOCOM_LOADLIB_AGAIN") != "1":
        sys.exit(f"already loaded {swapped} from another map this session - a second archive swap froze the game "
                 "twice (the heap ran out); restart the map first (or set SOCOM_LOADLIB_AGAIN=1 to risk it)")
    if source != "disc":                                     # a pack's size is known: check the heap instead
        size = os.path.getsize(find_pack(other))
        room = heap_room()
        if size + 0x40000 > room and os.environ.get("SOCOM_LOADLIB_AGAIN") != "1":
            sys.exit(f"{other}.CDB needs ~{size // 1024} KB of heap but only ~{room // 1024} KB is left - the allocator "
                     "stops the game when it runs out; reload the map (or SOCOM_LOADLIB_AGAIN=1 to risk it)")
        need, have_blocks, category = vram_need(other)
        if need + VRAM_MARGIN > have_blocks and os.environ.get("SOCOM_LOADLIB_AGAIN") != "1":
            sys.exit(f"{other}'s textures need {need:#x} VRAM blocks but the category {category:#x} pool has {have_blocks:#x} "
                     "left - past its end they wrap into the frame buffer (garbage / black); reload the map")
    current = rd(0x004417C0, 8).split(b"\0")[0].decode().upper()
    _put_str(LIB_PATH_BUF, path)
    _put_str(LIB_PATH_BUF + 0x40, other)
    _put_str(LIB_PATH_BUF + 0x50, current)
    parts = path.strip("/").split("/")                       # mp, mp53, clib
    _put_str(LIB_PATH_BUF + 0x60, f"run/{parts[0]}/{parts[1]}/{parts[2]}_mdl.zed")
    serve, wait, mount = None, 30.0, (ZDB_MOUNT, ZDB_CTX, LIB_PATH_BUF + 0x40)
    if source == "mem":
        cdb_path = find_pack(other)
        cdb = open(cdb_path, "rb").read()
        table = 0xA0 + 0x70 * struct.unpack_from("<I", cdb, 0x98)[0]
        if table > 0x800:
            sys.exit("pack has too many files for pack_stream.cds's 0x800-byte table")
        for a, v in ((int(a, 16) & 0x1FFFFFF, int(v, 16)) for a, v in (l.split() for l in open(PACK_STREAM_CODES) if l.strip())):
            wr32(a, v)
        bridge({"Command": "WRITEMEM", "Address": PACK_TABLE, "Data": cdb[:table].hex(), "Region": "ee"})
        wr32(PACK_STREAM + 8, len(cdb))
        wr32(PACK_MAILBOX, 0)
        serve, mount = _pack_server(cdb), (MEM_MOUNT, ZDB_CTX)
    elif source == "mc":
        wait = 90.0                                          # memory-card reads are slow on a console
        probe = game_call([(ZDB_RESET, ZDB_CTX), (ZDB_MOUNT, ZDB_CTX, LIB_PATH_BUF + 0x40),
                           (ZDB_RESET, ZDB_CTX), (ZDB_MOUNT, ZDB_CTX, LIB_PATH_BUF + 0x50)], wait=wait)
        if not probe[1]:                                     # do not Acquire from nothing: that leaves an EMPTY lib
            sys.exit(f"no BASCUS-97275{other}/{other}.CDB on the memory card in slot 1 or 2 "
                     f"(remounted RUN/{current}.ZDB: {'ok' if probe[3] == 1 else 'FAILED'})")
    for a, v in ((int(a, 16) & 0x1FFFFFF, int(v, 16)) for a, v in (l.split() for l in open(LIB_MODELS_CODES) if l.strip())):
        wr32(a, v)
    use_guard = os.environ.get("SOCOM_FREE_GUARD", "0") == "1"   # off by default: suspected of breaking the mount
    if use_guard:
        install_free_guard()
        wr32(FREE_GUARD_SKIPPED, 0)
        wr32(FREE_GUARD_FLAG, 1)                             # keep everything this load allocates (see free_guard.cds)
    try:
        r = game_call([(ZDB_RESET, ZDB_CTX), mount, (ASSET_ACQUIRE, LIB_PATH_BUF),
                       (LIB_MODELS, CALL_TABLE + 2 * 20, LIB_PATH_BUF + 0x60),   # <lib>_mdl.zed (lib = call 2's result)
                       (MEM_UNMOUNT if source == "mem" else ZDB_RESET, ZDB_CTX),  # mem_unmount ends in ZDB_Reset
                       (ZDB_MOUNT, ZDB_CTX, LIB_PATH_BUF + 0x50)], wait=wait, serve=serve)
        if serve:
            print(f"  served {serve.served // 1024} KB from {other}.CDB over the bridge")
    finally:
        if use_guard:
            wr32(FREE_GUARD_FLAG, 0)
    if use_guard:
        print(f"  free guard: skipped {struct.unpack('<I', rd(FREE_GUARD_SKIPPED, 4))[0]} frees during the load")
    category = rd(r[2] + 0x2F, 1)[0] if r[2] else -1
    if r[2] and category not in PLACEMENT:
        print(f"  library category {category:#x}: its placement case is not replicated yet - textures NOT placed "
              f"(known: {', '.join(hex(c) for c in PLACEMENT)})")
    elif r[2]:                                               # VRAM + palette placement (place_lib.cds), else red noise
        for a, v in ((int(a, 16) & 0x1FFFFFF, int(v, 16)) for a, v in (l.split() for l in open(PLACE_LIB_CODES) if l.strip())):
            wr32(a, v)
        game_call([(PLACE_LIB, r[2], *PLACEMENT[category])], wait=10.0)
        arr, cnt = struct.unpack("<I", rd(r[2] + 0x1C, 4))[0], struct.unpack("<I", rd(r[2] + 0x18, 4))[0]
        bound = sum(1 for i in range(cnt)
                    if struct.unpack("<I", rd(struct.unpack("<I", rd(struct.unpack("<I", rd(arr + 4 * i, 4))[0] + 0xC, 4))[0] + 0x1C, 4))[0])
        print(f"  placed in the category {category:#x} texture pools: {bound}/{cnt} textures have palettes")
    ok = loaded_libs().get(path, (0, False))
    origin = {"disc": f"RUN/{other}.ZDB", "mc": f"mc:BASCUS-97275{other}/{other}.CDB", "mem": f"{other}.CDB (bridge)"}[source]
    print(f"  {path} from {origin} -> lib {r[2]:#x} {'loaded' if ok[1] else 'EMPTY'}, models {r[3]:#x}; "
          f"remounted RUN/{current}.ZDB ({'ok' if r[5] == 1 else 'FAILED'})")
    return r[2]

def require_round():
    """Refuse to touch the world unless a round is actually running: your soldier exists and the round phase
    (*(0x004446F8)+0x117) is 3. Spawning while the game was still in menus/loading froze it."""
    game = struct.unpack("<I", rd(GAME_PTR, 4))[0]
    me = struct.unpack("<I", rd(0x0044D648, 4))[0]
    phase = rd(game + 0x117, 1)[0] if game else -1
    campaign = re.fullmatch(rb"m[0-9]+", rd(0x004417C0, 8).split(bytes(1))[0]) is not None
    if not game or not me or (phase != 3 and not campaign):   # MP round phase 3; campaign missions keep 0
        sys.exit(f"not in a running round (game {game:#x}, soldier {me:#x}, phase {phase}) - wait until you're in game")

INJECT_CODES = os.path.join(HERE, "..", "map_inject.codes.txt")
INJ_ARMED, INJ_MODELS, INJ_LIB, INJ_OTHER_RC = 0x000D0980, 0x000D0984, 0x000D0988, 0x000D098C
INJ_OTHER, INJ_CAPTURED, INJ_PATH, INJ_MDL, INJ_REMOUNT_RC = 0x000D0990, 0x000D09A0, 0x000D09C0, 0x000D09E0, 0x000D09FC
INJ_SITES = {0x002B0A84: 0x0C000000 | (0x000D1200 >> 2), 0x001EA0A0: 0x0C000000 | (0x000D123C >> 2)}   # mount capture, before 1st post-load pass

def inject_lib(path):
    """Arm loading another map's library DURING the next map load (Mods/SOCOM2/map_inject.cds), so the game's own
    post-load passes bind its palettes and VRAM placement - a library loaded mid-round never gets those."""
    originals = {0x002B0A84: 0x0C000000 | (0x003C8C80 >> 2), 0x001EA0A0: 0x0C000000 | (0x0033AD00 >> 2)}
    for site, word in INJ_SITES.items():                     # verify before patching (game code must be loaded)
        cur = struct.unpack("<I", rd(site, 4))[0]
        if cur not in (word, originals[site]):
            sys.exit(f"call site {site:#x} holds {cur:#010x}, not the expected jal - is the game booted to the lobby?")
    for codes in (LIB_MODELS_CODES, INJECT_CODES):
        for a, v in ((int(a, 16) & 0x1FFFFFF, int(v, 16)) for a, v in (l.split() for l in open(codes) if l.strip())):
            if a != INJ_ARMED:
                wr32(a, v)
    parts = path.strip("/").split("/")                       # mp, mp53, clib
    for addr, text in ((INJ_OTHER, parts[1].upper()), (INJ_PATH, path),
                       (INJ_MDL, f"run/{parts[0]}/{parts[1]}/{parts[2]}_mdl.zed")):
        _put_str(addr, text)
    for a in (INJ_MODELS, INJ_LIB, INJ_OTHER_RC, INJ_REMOUNT_RC):
        wr32(a, 0)
    for site, word in INJ_SITES.items():                     # one-word call-site patches
        wr32(site, word)
    wr32(INJ_ARMED, 1)
    print(f"  armed: the next map load also loads {path} from RUN/{parts[1].upper()}.ZDB")

def inject_status():
    u = lambda a: struct.unpack("<I", rd(a, 4))[0]
    print(f"  armed {u(INJ_ARMED)}  captured map archive {rd(INJ_CAPTURED, 32).split(bytes(1))[0].decode('latin1')!r}")
    print(f"  other mount rc {u(INJ_OTHER_RC)}  library {u(INJ_LIB):#x}  models {u(INJ_MODELS):#x}  remount rc {u(INJ_REMOUNT_RC)}")
    print(f"  call sites patched: {all(u(a) == w for a, w in INJ_SITES.items())}")

def spawn_ai(type_name, slot=None, pos=None, team=None, lib=None, template_name=None):
    """Spawn an AI character (CSealCtrlAi) with the game's own SpawnCharacterFrom. The template is the round's own
    mission record for that chartype (escort VIPs / extraction hostages: flags 0x40020000, their spawn group), so the
    spawn is exactly what the game does itself. Without such a record (e.g. demolition) the AI crashed PCSX2, so
    that path needs SOCOM_AI_UNSAFE=1. Chartypes whose model is not loaded are always refused."""
    require_round()
    _, model = chartype(type_name)
    if lib:
        load_lib(lib)
    if not model or not game_call([(MODEL_FIND, model)])[0]:
        sys.exit(f"'{type_name}' model is not loaded on this map - not spawning (it would crash the game)")
    table = struct.unpack("<I", rd(CHAR_TABLE, 4))[0]
    count, records = struct.unpack("<II", rd(table + 4, 8))
    template = next((records + 0x160 * i for i in range(count)
                     if rd(records + 0x160 * i + 0x20, 0x30).split(b"\0")[0].decode("latin1") == type_name), None)
    if template_name:                                        # explicit template: a mission record by name (e.g. Road1)
        template = next((records + 0x160 * i for i in range(count)
                         if rd(records + 0x160 * i, 0x20).split(b"\0")[0].decode("latin1") == template_name), None)
        if template is None:
            sys.exit(f"no mission record named '{template_name}' (see ai-list)")
    if template is None and team is None and os.environ.get("SOCOM_AI_UNSAFE") != "1":
        sys.exit(f"this round has no mission record for '{type_name}', and the player record's own team flags "
                 "(0x80000006) crashed PCSX2 - pass team=0x40020000 (escortable VIP side), a template=, "
                 "or set SOCOM_AI_UNSAFE=1")
    level = struct.unpack("<I", rd(0x00435538, 4))[0]
    if struct.unpack("<I", rd(AI_NEXT_SLOT - 4, 4))[0] != level:      # new map load: old records are dead
        wr32(AI_NEXT_SLOT - 4, level)
        wr32(AI_NEXT_SLOT, 0)
    if slot is None:                                         # each AI keeps a pointer to its record: never reuse one
        slot = struct.unpack("<I", rd(AI_NEXT_SLOT, 4))[0]
        if slot >= AI_RECORD_SLOTS:
            sys.exit(f"all {AI_RECORD_SLOTS} AI record slots used this map - reload the map")
        wr32(AI_NEXT_SLOT, slot + 1)
    rec = bytearray(rd(template or records, 0x160))
    name = f"ai{slot + 1}"
    for off, text, size in ((0x00, name, 0x20), (0x20, type_name, 0x30), (0x50, name, 0x20)):
        rec[off:off + size] = text.encode()[:size - 1].ljust(size, b"\0")
    struct.pack_into("<I", rec, 0x15C, 0)                    # not "already spawned"
    if team is not None:
        struct.pack_into("<I", rec, 0x11C, team)             # becomes the body's team mask (+0xC8)
    at = AI_RECORDS + 0x160 * slot
    for i in range(0, 0x160, 4):
        wr32(at + i, struct.unpack_from("<I", rec, i)[0])
    body = game_call([(SPAWN_FROM, SPAWN_CTX, at, 0)])[0]
    if not body:
        sys.exit("spawn failed")
    if pos:
        time.sleep(1.0)                                      # the game puts it on its spawn point a frame later
        move_body(body, pos)
    print(f"  AI '{type_name}' body {body:#x}" + (f" at {pos[0]:.1f} {pos[1]:.1f} {pos[2]:.1f}" if pos else ""))
    return body

BODY_SPAN = 0x2000                                            # a CZSealBody's own position copies: +0x1C .. +0x19E0

def _ptrs(body):
    return struct.unpack("<2048I", b"".join(rd(body + i, 0x400) for i in range(0, BODY_SPAN, 0x400)))

def move_body(body, pos, others=None):
    """Teleport any CZSealBody: rewrite every copy of its current position (body, physics, network state).
    Copies are found by value, so bodies standing on the same spot share matches; ownership decides which to write:
    inside this body -> yes; inside another body (others, default every AI body) -> no; elsewhere -> only when this
    body holds a pointer just below it (its physics / scene objects), or when no other body is at the same spot.
    Static data (< 0x00700000, the spawn tables) and unaligned matches are never touched."""
    cur = rd(body + 0x1C, 12)
    hits = [a for a in bridge({"Command": "SCANPATTERN", "SearchPattern": list(cur), "Region": "ee"}).get("MatchAddresses") or []
            if a >= 0x00700000 and a % 4 == 0]
    others = [b for b in (others if others is not None else [b for _, b, _ in ai_bodies()]) if b != body]
    twins = [b for b in others if rd(b + 0x1C, 12) == cur]   # bodies on exactly the same spot
    mine = _ptrs(body)
    theirs = [_ptrs(b) for b in twins]
    near = lambda ptrs, a: any(0 <= a - p < 0x800 for p in ptrs)
    new = struct.pack("<3f", *pos).hex()
    for a in hits:
        if body <= a < body + BODY_SPAN:
            pass
        elif any(b <= a < b + BODY_SPAN for b in others):
            continue
        elif twins and not (near(mine, a) and not any(near(t, a) for t in theirs)):
            continue
        bridge({"Command": "WRITEMEM", "Address": a, "Data": new, "Region": "ee"})

def my_pos(offset=(0.0, 0.0, 0.0)):
    me = struct.unpack("<I", rd(0x0044D648, 4))[0]                # Seal Pointer (host_seal() can pick a VIP now)
    x, y, z = struct.unpack("<3f", rd(me + 0x1C, 12))
    return (x + offset[0], y + offset[1], z + offset[2])

def my_team(team):
    """Switch YOUR soldier's side in the current round: body team mask (+0xC8) and your PersonaObject team
    (object 1, sent to the others by the normal delta updates). Skips the game's team-change flow, so the
    scoreboard/team counts are not updated and the next round may put you back."""
    me = struct.unpack("<I", rd(0x0044D648, 4))[0]
    persona = struct.unpack("<I", rd(struct.unpack("<I", rd(0x00675F20 + 4, 4))[0] + 0x1C, 4))[0]
    old = struct.unpack("<I", rd(me + 0xC8, 4))[0]
    wr32(me + 0xC8, TEAM[team])
    wr32(persona, TEAM[team])
    print(f"  you: {old:#010x} -> {TEAM[team]:#010x} ({team})")

AI_STATES = {"script": 1, "hold": 2, "avoid": 3, "deploy": 4, "surrender": 6, "usevehicle": 7, "goto": 8,
             "follow": 9, "breach": 10, "action": 11, "pursue": 12, "animate": 13, "cqbattack": 14, "flee": 15,
             "pathrecover": 16, "rescued": 17, "suspended": 18, "shotat": 19, "reload": 20, "noammo": 21,
             "pickup": 22, "hostage": 23, "plan": 24, "event": 25, "target": 26, "grenade": 27, "stun": 28, "rush": 29}
AI_PUSH_STATE, AI_SET_STATE = 0x005FF6C0, 0x005FF560          # (ctrl+0x41C state stack, ctrl, id, 0)
CTRL_AI_VT, BODY_VT = 0x00668D70, 0x00668B20

def ai_bodies():
    """All AI soldiers (CZSealBody whose controller is CSealCtrlAi), nearest to you first."""
    me = my_pos()
    hits = bridge({"Command": "SCANPATTERN", "SearchPattern": list(struct.pack("<I", BODY_VT)), "Region": "ee"})
    out = []
    for b in hits.get("MatchAddresses") or []:
        if b < 0x00700000:
            continue
        ctrl = struct.unpack("<I", rd(b + 0xC0, 4))[0]
        if not ctrl or struct.unpack("<I", rd(ctrl, 4))[0] != CTRL_AI_VT:
            continue
        x, y, z = struct.unpack("<3f", rd(b + 0x1C, 12))
        out.append((((x - me[0]) ** 2 + (y - me[1]) ** 2 + (z - me[2]) ** 2) ** 0.5, b, ctrl))
    return sorted(out)

def ai_state(state, which="nearest", replace=False):
    """Push (or replace with) an AI behaviour state on an AI soldier: aistate surrender | aistate 6 all."""
    sid = AI_STATES.get(state.lower()) or int(state, 0)
    targets = ai_bodies() if which == "all" else ai_bodies()[:1]
    if not targets:
        sys.exit("no AI soldiers in this round")
    game_call([(AI_SET_STATE if replace else AI_PUSH_STATE, ctrl + 0x41C, ctrl, sid, 0) for _, _, ctrl in targets[:6]])
    for dist, body, ctrl in targets[:6]:
        stack = struct.unpack("<I", rd(ctrl + 0x424, 4))[0]
        top = struct.unpack("<I", rd(stack + 8, 4))[0] if stack else 0
        print(f"  AI {body:#x} ({dist:.0f} away): state {sid} -> top state now {rd(top + 0x30, 1)[0] if top else '?'}")

def ai_follow(which="nearest"):
    """Make AI soldiers follow you (the escort link): ctrl+0x5D4 = leader body, then push Follow (9);
    the Follow state keeps the leader at state+0x58. Must be set before the push - Follow exits at once without one."""
    me = struct.unpack("<I", rd(0x0044D648, 4))[0]
    targets = ai_bodies() if which == "all" else ai_bodies()[:1]
    for _, body, ctrl in targets[:6]:
        wr32(ctrl + 0x5D4, me)
    game_call([(AI_PUSH_STATE, ctrl + 0x41C, ctrl, 9, 0) for _, _, ctrl in targets[:6]])
    for dist, body, ctrl in targets[:6]:
        top = struct.unpack("<I", rd(struct.unpack("<I", rd(ctrl + 0x424, 4))[0] + 8, 4))[0]
        ok = rd(top + 0x30, 1)[0] == 9 and struct.unpack("<I", rd(top + 0x58, 4))[0] == me
        print(f"  AI {body:#x} ({dist:.0f} away): {'following you' if ok else 'did not take the Follow state'}")

def ai_list():
    """The round's mission character records (templates the ai command can spawn safely)."""
    table = struct.unpack("<I", rd(CHAR_TABLE, 4))[0]
    count, records = struct.unpack("<II", rd(table + 4, 8))
    for i in range(count):
        r = rd(records + 0x160 * i, 0x160)
        text = lambda o, n: r[o:o + n].split(b"\0")[0].decode("latin1")
        print(f"  {text(0, 0x20):14} chartype {text(0x20, 0x30):18} spawn {text(0x78, 0x18):16} "
              f"flags {struct.unpack_from('<I', r, 0x11C)[0]:#010x}")

# -- in-game actions (schema-driven, Mods/SOCOM2/packets/dme_objects.py) ------------------
sys.path.insert(0, os.path.join(HERE, "..", "packets"))
import dme_objects as dme                                     # noqa: E402

SEAL_TYPE, GRENADE_TYPE = 0, 3

def soldier_id(k):
    return 0x60 + k                                           # object id given by spawn

def soldier_state(owner):
    """Live SealState of a soldier (data block of its SealObject) - pos at +0."""
    r = bridge({"Command": "SCANPATTERN", "SearchPattern": list(b"SealObject\0"), "Region": "ee"})
    for a in r.get("MatchAddresses") or []:
        hdr = struct.unpack("<8I", rd(a - 0xC, 32))
        if hdr[0] == 0x01020000 and hdr[2] == owner:
            return hdr[7]
    sys.exit(f"no soldier for player {owner} - spawn the bots first")

def soldier_pos(owner):
    return list(struct.unpack("<3f", rd(soldier_state(owner), 12)))

def act(k, values):
    """Send SealState field updates for bot k's soldier (x y z stance pitch slot item ... or fNN)."""
    app(k, dme.build_update(SEAL_TYPE, soldier_id(k), values))
    print(f"  BOT{k} " + " ".join(f"{a}={b}" for a, b in values.items()))

def move(k, x, y, z):
    act(k, {"x": x, "y": y, "z": z})

SHOT_COUNT = 0x8B                                             # SealState +0x8B (field shot_count / f20)

def shoot(k, kick=7):
    """Fire one shot: the host's own shots only bump SealState shot_count (+1 per shot) with a pitch kick,
    so do the same from bot k's current values (read from its copy in the host's memory)."""
    st = soldier_state(k)
    count = rd(st + SHOT_COUNT, 1)[0]
    pitch = struct.unpack("<b", rd(st + 0x7D, 1))[0]
    act(k, {"shot_count": (count + 1) & 0xFF, "pitch": max(-127, pitch - kick)})
    act(k, {"pitch": pitch})

def action(k, action_id, arg=0, arg2=0, flag=0):
    """Soldier action/animation event 02 02 [soldier u16][arg s16][action u16][arg2 u8][flag u8]
    (receiver 0x2BD720 -> 0x58D710 switch on action 0-21). Captured: 0/243/1 before a throw, 1/34/4 animation."""
    app(k, bytes([0x02, 0x02]) + struct.pack("<HhHBB", soldier_id(k), arg, action_id, arg2, flag))
    print(f"  BOT{k} action {action_id} arg={arg} arg2={arg2} flag={flag}")

_GRENADE_SEQ = os.path.join(HERE, ".grenade_seq")
GRAVITY = 98.0                                                # from the host's grenade: vel.y -6.54 per 0.067 s step

def grenade(k, target=None, fuse=3.0):
    """Throw a frag the way the host does: create KProjectileObject (pos, vel), state=1 (thrown),
    after the fuse state=2 (explode), then destroy. Default target: the host's soldier."""
    start = soldier_pos(k)
    start[1] += 1.5
    tx, ty, tz = target or soldier_pos(0)
    dx, dy, dz = tx - start[0], ty - start[1], tz - start[2]
    dist = max((dx * dx + dz * dz) ** 0.5, 1.0)
    flight = min(1.5, max(0.4, dist / 146.0))                # host's full throw: ~146 u/s horizontal
    vel = (dx / flight, GRAVITY * flight / 2 + dy / flight, dz / flight)
    act(k, {"equipped": 121, "weapon_state": 1})             # select the M67, as the host does before throwing
    action(k, 0, 243, 1)                                     # the 02 02 event the host sent right before its throw
    try:
        seq = int(open(_GRENADE_SEQ).read())
    except (OSError, ValueError):
        seq = 0
    open(_GRENADE_SEQ, "w").write(str(seq + 1))
    gid = 0x40 + ((k - 1) * 2 + seq % 2) % 0x20               # 0x40-0x5F: free in the object table, 2 per bot
    app(k, dme.build_create(GRENADE_TYPE, k, gid, "KProjectileObject", {"pos": start, "vel": vel, "thrower": soldier_id(k)}))
    app(k, dme.build_update(GRENADE_TYPE, gid, {"state": 1}))
    print(f"  BOT{k} throws grenade #{gid} from {[round(v, 1) for v in start]} vel {[round(v, 1) for v in vel]}")
    time.sleep(fuse)
    app(k, dme.build_update(GRENADE_TYPE, gid, {"state": 2}))
    print("  boom")
    time.sleep(1.5)
    app(k, bytes([0x00, 0x0A]) + struct.pack("<H", gid))

def set_team(k, team):
    """Switch teams (lobby or in game): PersonaObject update, field 0 = team value (u32).
    Captured from the host: [00 03][01][n=1][objid 0x0001][field 00][80 00 01 00] = switch to TERR."""
    value = TEAM[team]
    app(k, bytes([0x00, 0x03, 0x01, 0x01, 0x20 + k, 0x00, 0x00]) + struct.pack("<I", value))
    print(f"  BOT{k} -> {team}")

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
    elif cmd == "apply":
        apply_codes(rest[0])
    elif cmd == "add":
        for k in rng(rest): add(k)
    elif cmd == "act":                                # act K field=value ...
        act(int(rest[0]), dict(a.split("=", 1) for a in rest[1:]))
    elif cmd == "move":                               # move K x y z
        move(int(rest[0]), *map(float, rest[1:4]))
    elif cmd == "stance":
        act(int(rest[0]), {"stance": rest[1]})
    elif cmd == "aim":
        act(int(rest[0]), {"pitch": rest[1]})
    elif cmd == "shoot":                              # shoot K [count]
        for _ in range(int(rest[1]) if len(rest) > 1 else 1):
            shoot(int(rest[0])); time.sleep(0.15)
    elif cmd == "myteam":                             # myteam seal|terr
        my_team(rest[0])
    elif cmd == "aistate":                            # aistate STATE [nearest|all] [replace]
        ai_state(rest[0], "all" if "all" in rest[1:] else "nearest", "replace" in rest[1:])
    elif cmd == "aifollow":                           # aifollow [all]
        ai_follow("all" if "all" in rest else "nearest")
    elif cmd == "loadlib":                            # loadlib //mp/mp53/clib
        load_lib(rest[0])
    elif cmd == "loadpack":                           # loadpack mhost [mc|mem]
        load_pack(rest[0], rest[1] if len(rest) > 1 else "mc")
    elif cmd == "testprop":                           # testprop mcv0016 ca_hmv_lod  (no map reload between builds)
        test_prop(rest[0], rest[1])
    elif cmd == "inject":                             # inject //mp/mp53/clib  (takes effect on the next map load)
        inject_lib(rest[0])
    elif cmd == "inject-status":
        inject_status()
    elif cmd == "ai-list":
        ai_list()
    elif cmd == "ai":                                 # ai CHARTYPE [slot] [here]
        nums = [a for a in rest[1:] if a.isdigit()]
        team = next((int(a[5:], 0) for a in rest[1:] if a.startswith("team=")), None)
        lib = next((a[4:] for a in rest[1:] if a.startswith("lib=")), None)
        tmpl = next((a[9:] for a in rest[1:] if a.startswith("template=")), None)
        pos = my_pos((2.0, 0.0, 2.0)) if "here" in rest[1:] else None
        if chartype(rest[0], required=False)[0] is None:   # not a chartype: a model name (ai ca_7199a995)
            spawn_model(rest[0], pos, team if team is not None else 0x40020000, int(nums[0]) if nums else None)
        else:
            spawn_ai(rest[0], int(nums[0]) if nums else None, pos, team, lib, tmpl)
    elif cmd == "bomb":                               # bomb [x y z]
        bomb(tuple(map(float, rest[0:3])) if len(rest) >= 3 else None)
    elif cmd == "action":                             # action K ACTION [ARG ARG2 FLAG]
        action(int(rest[0]), *(int(v, 0) for v in rest[1:5]))
    elif cmd == "grenade":                            # grenade K [x y z]
        grenade(int(rest[0]), tuple(map(float, rest[1:4])) if len(rest) >= 4 else None)
    elif cmd == "login":
        login()
    elif cmd == "chat":                               # chat K "text"   (lobby, via the server)
        lobby_chat(int(rest[0]), " ".join(rest[1:]))
    elif cmd == "matchchat":                          # matchchat K "text" [channel]
        match_chat(int(rest[0]), rest[1], int(rest[2]) if len(rest) > 2 else 0)
    elif cmd == "armory":                             # armory K primary secondary item item item
        armory(int(rest[0]), rest[1:])
    elif cmd == "team":                               # team seal|terr A [B]
        for k in rng(rest[1:]): set_team(k, rest[0])
    elif cmd == "ready":
        for k in rng(rest): set_ready(k, True)
    elif cmd == "unready":
        for k in rng(rest): set_ready(k, False)
    elif cmd == "remove":
        for k in rng(rest): remove(k)
    elif cmd == "spawn":
        spawn(rest[0] if rest else "all", here="here" in rest)
    elif cmd == "capture":
        capture(rest[0], rest[1])
    elif cmd == "spawns":
        spawns_cmd()
    elif cmd == "where":
        where()
    elif cmd == "tp":
        teleport(*map(float, rest[:3]))
    elif cmd == "hold":
        hold(*map(float, rest[:3]))
    elif cmd == "unhold":
        unhold()
    elif cmd == "status":
        status()
    else:
        print(__doc__)
