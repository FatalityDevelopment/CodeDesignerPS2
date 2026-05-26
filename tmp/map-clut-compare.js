const fs = require('fs');
const path = require('path');

const dir = 'C:/Users/tholu/Desktop/RUN/SP/M51';
const outDir = path.join(process.cwd(), 'tmp', 'map-clut-compare');
const names = ['concrete_base.tif', 'concrete_detail.tif', 'ground_gravel01.tif', 'ground_grass02.tif', 'grassd01.tif', 'wall_dirt1.tif', 'stone01.tif'];

function u32(b, o) { return (b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)) >>> 0; }
function str(b, s, e) { let z = s; while (z < e && b[z]) z++; let o = ''; for (let i = s; i < z; i++) { const c = b[i]; if (c >= 32 && c <= 126) o += String.fromCharCode(c); } return o; }
function parseZed(file) {
  const b = fs.readFileSync(file);
  const slots = u32(b, 4), strSize = u32(b, 8), strBase = u32(b, 12), bodySize = u32(b, 0x54);
  const first = 0x64 + strSize + 0x10, body = b.length - bodySize;
  const entries = [], data = [];
  for (let i = 0; i < slots - 1; i++) {
    const off = first + i * 16, np = u32(b, off), rel = u32(b, off + 4), size = u32(b, off + 8);
    const name = np >= strBase ? str(b, 0x64 + np - strBase, 0x64 + strSize) : '';
    entries.push({ i, name, size });
    data.push(size ? b.subarray(body + rel, body + rel + size) : Buffer.alloc(0));
  }
  return { entries, data };
}
function header(b) { return { w: b.readUInt16LE(0), h: b.readUInt16LE(2), pal: b.readUInt16LE(8), fmt: b.readUInt16LE(12) }; }
function expand5(v) { return ((v << 3) | (v >> 2)) & 255; }
function pal5551(buf) {
  const out = Buffer.alloc(1024);
  for (let i = 0; i < 256; i++) {
    const v = buf.readUInt16LE(i * 2);
    const r = expand5(v & 31), g = expand5((v >> 5) & 31), b = expand5((v >> 10) & 31), stp = !!(v & 0x8000);
    out[i * 4] = r; out[i * 4 + 1] = g; out[i * 4 + 2] = b; out[i * 4 + 3] = (r || g || b || stp) ? 255 : 0;
  }
  return out;
}
function pal32(buf) {
  const out = Buffer.alloc(1024);
  for (let i = 0; i < 256; i++) {
    out[i * 4] = buf[i * 4]; out[i * 4 + 1] = buf[i * 4 + 1]; out[i * 4 + 2] = buf[i * 4 + 2]; out[i * 4 + 3] = 255;
  }
  return out;
}
function unswizzle(pal) {
  const out = Buffer.alloc(1024);
  for (let i = 0; i < 256; i++) {
    const j = (i & 0xe7) | ((i & 0x08) << 1) | ((i & 0x10) >> 1);
    pal.copy(out, i * 4, j * 4, j * 4 + 4);
  }
  return out;
}
function palettes(palZed) {
  const list = [];
  for (let i = 0; i < palZed.entries.length; i++) {
    const m = /^texpal_(\d+)$/i.exec(palZed.entries[i].name);
    if (!m) continue;
    for (let j = i + 1; j < Math.min(i + 4, palZed.entries.length); j++) {
      if (/^buf$/i.test(palZed.entries[j].name)) {
        const raw = palZed.data[palZed.entries[j].i];
        const rgba = raw.length >= 1024 ? pal32(raw) : pal5551(raw);
        list.push({ id: Number(m[1]), name: palZed.entries[i].name.toLowerCase(), raw: rgba, unswizzled: unswizzle(rgba) });
        break;
      }
    }
  }
  return list;
}
function textures(txrZed) {
  const map = new Map();
  let palCursor = 0;
  for (let i = 0; i < txrZed.entries.length; i++) {
    if (!/\.tif$/i.test(txrZed.entries[i].name)) continue;
    for (let j = i + 1; j < Math.min(i + 4, txrZed.entries.length); j++) {
      if (/^texdat$/i.test(txrZed.entries[j].name)) {
        const data = txrZed.data[txrZed.entries[j].i];
        map.set(txrZed.entries[i].name.toLowerCase(), { data, palSlot: palCursor++ });
        break;
      }
      if (/\.tif$/i.test(txrZed.entries[j].name)) break;
    }
  }
  return map;
}
function decode(tex, pal) {
  const h = header(tex);
  const out = Buffer.alloc(h.w * h.h * 4);
  const idx = unswizzle8(tex.subarray(0x20, 0x20 + h.w * h.h), h.w, h.h);
  for (let i = 0; i < idx.length; i++) {
    pal.copy(out, i * 4, idx[i] * 4, idx[i] * 4 + 4);
  }
  return { h, rgba: flip(out, h.w, h.h) };
}
function decodeLinear(tex, pal) {
  const h = header(tex);
  const out = Buffer.alloc(h.w * h.h * 4);
  const idx = tex.subarray(0x20, 0x20 + h.w * h.h);
  for (let i = 0; i < idx.length; i++) {
    pal.copy(out, i * 4, idx[i] * 4, idx[i] * 4 + 4);
  }
  return { h, rgba: flip(out, h.w, h.h) };
}
function unswizzle8(src, width, height) {
  if (width < 16 || height < 4) return src;
  const out = Buffer.alloc(width * height);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const blockLocation = (y & ~0x0f) * width + (x & ~0x0f) * 2;
      const swapSelector = (((y + 2) >> 2) & 1) * 4;
      const posY = ((((y & ~3) >> 1) + (y & 1)) & 7);
      const columnLocation = posY * width * 2 + ((x + swapSelector) & 7) * 4;
      const byteNum = ((y >> 1) & 1) + ((x >> 2) & 2);
      const srcIndex = blockLocation + columnLocation + byteNum;
      out[y * width + x] = srcIndex < src.length ? src[srcIndex] : 0;
    }
  }
  return out;
}
function unswizzle8Inverse(src, width, height) {
  if (width < 16 || height < 4) return src;
  const out = Buffer.alloc(width * height);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const blockLocation = (y & ~0x0f) * width + (x & ~0x0f) * 2;
      const swapSelector = (((y + 2) >> 2) & 1) * 4;
      const posY = ((((y & ~3) >> 1) + (y & 1)) & 7);
      const columnLocation = posY * width * 2 + ((x + swapSelector) & 7) * 4;
      const byteNum = ((y >> 1) & 1) + ((x >> 2) & 2);
      const dstIndex = blockLocation + columnLocation + byteNum;
      if (dstIndex < out.length) out[dstIndex] = src[y * width + x];
    }
  }
  return out;
}
function flip(src, w, h) {
  const out = Buffer.alloc(src.length), stride = w * 4;
  for (let y = 0; y < h; y++) src.copy(out, y * stride, (h - 1 - y) * stride, (h - y) * stride);
  return out;
}
function bmp(file, w, h, rgba) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  const head = Buffer.alloc(54), px = Buffer.alloc(w * h * 4);
  for (let i = 0; i < w * h; i++) { px[i * 4] = rgba[i * 4 + 2]; px[i * 4 + 1] = rgba[i * 4 + 1]; px[i * 4 + 2] = rgba[i * 4]; px[i * 4 + 3] = rgba[i * 4 + 3]; }
  head.write('BM'); head.writeUInt32LE(54 + px.length, 2); head.writeUInt32LE(54, 10); head.writeUInt32LE(40, 14); head.writeInt32LE(w, 18); head.writeInt32LE(-h, 22); head.writeUInt16LE(1, 26); head.writeUInt16LE(32, 28);
  fs.writeFileSync(file, Buffer.concat([head, px]));
}

const txr = textures(parseZed(path.join(dir, 'M51_TXR.ZED')));
const pals = palettes(parseZed(path.join(dir, 'M51_PAL.ZED')));
for (const name of names) {
  const t = txr.get(name);
  if (!t) continue;
  const p = pals[t.palSlot];
  if (!p) continue;
  const raw = decodeLinear(t.data, p.raw);
  const swz = decodeLinear(t.data, p.unswizzled);
  const texel = decode(t.data, p.unswizzled);
  const invData = Buffer.from(t.data);
  const h = header(t.data);
  unswizzle8Inverse(t.data.subarray(0x20, 0x20 + h.w * h.h), h.w, h.h).copy(invData, 0x20);
  const inv = decodeLinear(invData, p.unswizzled);
  bmp(path.join(outDir, `${name}.raw.bmp`), raw.h.w, raw.h.h, raw.rgba);
  bmp(path.join(outDir, `${name}.unswizzled.bmp`), swz.h.w, swz.h.h, swz.rgba);
  bmp(path.join(outDir, `${name}.texel-unswizzle.bmp`), texel.h.w, texel.h.h, texel.rgba);
  bmp(path.join(outDir, `${name}.texel-inverse.bmp`), inv.h.w, inv.h.h, inv.rgba);
  console.log(`${name}: slot=${t.palSlot} pal=${p.name}`);
}
