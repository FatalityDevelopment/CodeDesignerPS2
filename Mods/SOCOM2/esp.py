"""SOCOM II ESP control (snaplines, skeletons, 2D boxes, name + distance). See RENDER.md.

usage: python esp.py install          compile esp.cds and install it (EE paused while writing)
       python esp.py on | off         everything on (mode 15) / off (mode 0)
       python esp.py mode <bits>      1 snaplines, 2 skeletons, 4 boxes, 8 names (e.g. 12 = boxes + names)
       python esp.py team on|off      also show teammates
       python esp.py color <r> <g> <b> line colour, 0..1 floats
       python esp.py status

Mode/team/colour are plain data in the mailbox at 0x000E0000, so they apply instantly.
Install once per boot (or after a crash); installing over an identical copy is harmless.
"""
import base64
import json
import os
import socket
import struct
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
MAILBOX = 0x000E0000
HOOKS = ["0x002167C0", "0x00216DA8"]   # old hook site restored + current hook site, written last


class Bridge:
    def __init__(self):
        self.sock = socket.create_connection(("127.0.0.1", 8765), timeout=30)
        self.f = self.sock.makefile("r", encoding="utf-8")

    def send(self, cmd):
        self.sock.sendall((json.dumps(cmd) + "\n").encode())
        r = json.loads(self.f.readline())
        if not r.get("Success"):
            raise SystemExit("%s failed: %s" % (cmd["Command"], r.get("Message")))
        return r

    def read(self, addr, n):
        r = self.send({"Command": "READMEM", "Address": addr, "Length": n, "Region": "ee"})
        return base64.b64decode(r["Data"])

    def write(self, addr, data):
        self.send({"Command": "WRITEMEM", "Address": addr, "Data": data.hex().upper(), "Region": "ee"})


def installed(b):
    return struct.unpack("<I", b.read(0x00216DA8, 4))[0] != 0x0C085704   # original: jal 0x00215C10


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    cmd, args = sys.argv[1], sys.argv[2:]

    if cmd == "install":
        subprocess.check_call([sys.executable, os.path.join(HERE, "cds_install.py"),
                               os.path.join(HERE, "esp.cds"), "--last", *HOOKS])
        return

    b = Bridge()
    if cmd in ("on", "off", "mode"):
        if not installed(b):
            raise SystemExit("ESP is not installed - run: python esp.py install")
        mode = {"on": 15, "off": 0}.get(cmd)
        if mode is None:
            mode = int(args[0], 0)
        b.write(MAILBOX, struct.pack("<I", mode))
        print("mode = %d" % mode)
    elif cmd == "team":
        b.write(MAILBOX + 4, struct.pack("<I", 1 if args and args[0] == "on" else 0))
        print("teammates %s" % ("shown" if args and args[0] == "on" else "hidden"))
    elif cmd == "color":
        r, g, bl = (float(x) for x in args[:3])
        b.write(MAILBOX + 0x10, struct.pack("<4f", r, g, bl, 1.0))
        print("colour = %.2f %.2f %.2f" % (r, g, bl))
    elif cmd == "status":
        mode, team, lines, names = struct.unpack("<4I", b.read(MAILBOX, 16))
        print("installed=%s mode=%d team=%d lines/frame=%d names/frame=%d" % (installed(b), mode, team, lines, names))
    else:
        raise SystemExit(__doc__)


if __name__ == "__main__":
    main()
