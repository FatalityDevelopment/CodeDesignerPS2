"""Wireframe preview of decoded character meshes (front + side view PNG)."""
from rip_textures import png


def wire(path, tris_world, size=320):
    """tris_world: [((x,y,z),(x,y,z),(x,y,z))]. Writes a front (x,y) | side (z,y) view."""
    pts = [p for t in tris_world for p in t]
    if not pts:
        return
    lo = [min(p[k] for p in pts) for k in range(3)]
    hi = [max(p[k] for p in pts) for k in range(3)]
    span = max(hi[k] - lo[k] for k in range(3)) or 1.0
    W, H = size * 2, size
    img = bytearray(b"\x18" * W * H * 3)

    def plot(x, y, c):
        if 0 <= x < W and 0 <= y < H:
            i = (y * W + x) * 3
            img[i:i + 3] = c

    def line(a, b, c):
        n = int(max(abs(b[0] - a[0]), abs(b[1] - a[1]))) + 1
        for i in range(n + 1):
            t = i / n
            plot(int(a[0] + (b[0] - a[0]) * t), int(a[1] + (b[1] - a[1]) * t), c)

    def proj(p, view):
        u = (p[0] - lo[0]) if view == 0 else (p[2] - lo[2])
        v = p[1] - lo[1]
        return ((u / span) * (size - 20) + 10 + view * size, size - 10 - (v / span) * (size - 20))

    for view, col in ((0, b"\xe0\xe0\xe0"), (1, b"\x90\xc0\xff")):
        for t in tris_world:
            q = [proj(p, view) for p in t]
            line(q[0], q[1], col); line(q[1], q[2], col); line(q[2], q[0], col)
    png(path, W, H, bytes(img))
