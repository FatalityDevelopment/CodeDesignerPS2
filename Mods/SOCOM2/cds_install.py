"""Compile a .cds through the CodeDesigner bridge and write it into the running game
with the EE paused (PINE writes race the recompiler otherwise).

Hook words (any address listed with --last, default the ESP hook 0x002167C0) are written
after everything else, so the target code is complete before anything jumps to it.

usage: python cds_install.py esp.cds [--dry] [--last 0x002167C0 ...] [--codes esp.codes.txt]
"""
import argparse
import json
import socket
import struct


def send(sock, f, cmd):
    sock.sendall((json.dumps(cmd) + "\n").encode())
    line = f.readline()
    if not line:
        raise SystemExit("bridge closed the connection")
    return json.loads(line)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("source")
    ap.add_argument("--dry", action="store_true", help="compile only")
    ap.add_argument("--last", nargs="*", default=["0x002167C0"])
    ap.add_argument("--codes", help="also write the compiled patch as AAAAAAAA VVVVVVVV lines to this file")
    args = ap.parse_args()
    last = {int(a, 16) & 0x1FFFFFF for a in args.last}

    sock = socket.create_connection(("127.0.0.1", 8765), timeout=30)
    f = sock.makefile("r", encoding="utf-8")

    res = send(sock, f, {"Command": "COMPILECDS", "Data": open(args.source, encoding="utf-8").read()})
    errors = [r for r in res.get("Results") or [] if not r.get("Success") or r.get("Error")]
    for d in res.get("Diagnostics") or []:
        if d.get("Severity", 0) >= 2:
            errors.append(d)
    if not res.get("Success") or errors:
        print(json.dumps(errors or res.get("Message"), indent=1))
        raise SystemExit("compile failed")

    words = []
    for r in res["Results"]:
        if r.get("Hex"):
            a, w = r["Hex"].split()
            words.append((int(a, 16) & 0x1FFFFFF, int(w, 16)))

    blocks = []
    for a, w in words:
        if blocks and a == blocks[-1][0] + 4 * len(blocks[-1][1]) and a not in last:
            blocks[-1][1].append(w)
        else:
            blocks.append([a, [w]])
    blocks.sort(key=lambda b: b[0] in last)
    for a, ws in blocks:
        print("%08X  %d words%s" % (a, len(ws), "  (last)" if a in last else ""))
    if args.codes:
        with open(args.codes, "w") as out:
            for a, ws in blocks:
                for i, w in enumerate(ws):
                    out.write("%08X %08X\n" % (0x20000000 | (a + 4 * i), w))
        print("wrote", args.codes)
    if args.dry:
        return

    send(sock, f, {"Command": "PAUSE"})
    try:
        for a, ws in blocks:
            hexdata = "".join(struct.pack("<I", w).hex().upper() for w in ws)
            r = send(sock, f, {"Command": "WRITEMEM", "Address": a, "Data": hexdata, "Region": "ee"})
            if not r.get("Success"):
                raise SystemExit("write failed at %08X: %s" % (a, r.get("Message")))
    finally:
        send(sock, f, {"Command": "RESUME"})
    print("installed")


if __name__ == "__main__":
    main()
