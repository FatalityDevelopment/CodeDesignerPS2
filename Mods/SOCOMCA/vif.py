"""PS2 VIF command stream walker (shared by the SOCOM II / Combined Assault mesh tools)."""
import struct

UNPACK_SIZE = {(0, 0): 4, (1, 0): 8, (2, 0): 12, (3, 0): 16, (0, 1): 2, (1, 1): 4, (2, 1): 6, (3, 1): 8,
               (0, 2): 1, (1, 2): 2, (2, 2): 3, (3, 2): 4, (3, 3): 2}
NAMES = {0: "NOP", 1: "STCYCL", 2: "OFFSET", 3: "BASE", 4: "ITOP", 5: "STMOD", 6: "MSKPATH3", 7: "MARK",
         0x10: "FLUSHE", 0x11: "FLUSH", 0x13: "FLUSHA", 0x14: "MSCAL", 0x15: "MSCALF", 0x17: "MSCNT",
         0x20: "STMASK", 0x30: "STROW", 0x31: "STCOL", 0x4A: "MPG", 0x50: "DIRECT", 0x51: "DIRECTHL"}


def walk(data, start=0, end=None):
    """yield (offset, name, imm, num, payload) - payload bytes for UNPACK/STROW/STCOL/STMASK/DIRECT."""
    o, end = start, len(data) if end is None else end
    while o + 4 <= end:
        w = struct.unpack_from("<I", data, o)[0]
        cmd, num, imm = (w >> 24) & 0x7F, (w >> 16) & 0xFF, w & 0xFFFF
        if cmd >= 0x60:
            vn, vl = (cmd >> 2) & 3, cmd & 3
            size = UNPACK_SIZE.get((vn, vl))
            if size is None:
                return
            n = num or 256
            ln = (size * n + 3) // 4 * 4
            yield o, f"UNPACK V{vn + 1 if vn else ''}{'S' if vn == 0 else ''}-{(32, 16, 8, 5)[vl]}", imm, n, data[o + 4:o + 4 + ln]
            o += 4 + ln
        elif cmd in NAMES:
            extra = {0x30: 16, 0x31: 16, 0x20: 4}.get(cmd, 0)
            if cmd in (0x50, 0x51):
                extra = (imm or 65536) * 16
            yield o, NAMES[cmd], imm, num, data[o + 4:o + 4 + extra]
            o += 4 + extra
        else:
            return
