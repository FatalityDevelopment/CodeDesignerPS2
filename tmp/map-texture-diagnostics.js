const fs = require('fs');
const path = require('path');

const FIXED_HEADER_SIZE = 0x64;
const RECORD_SIZE = 0x10;
const TEXTURE_HEADER_SIZE = 0x20;
const TEXTURE_FOOTER_SIZE = 0x80;

const families = process.argv.slice(2);
const mapDir = families.shift() || 'C:\\Users\\tholu\\Desktop\\RUN\\SP\\M51';
const requestedFamilies = families.length ? families : ['M51', 'ZM51', 'LD51', 'FLIB', 'CLIB', 'BENZ', 'JUNK'];
const outRoot = path.join(process.cwd(), 'tmp', 'map-texture-diagnostics', path.basename(mapDir));

function u32(bytes, off) {
  return (bytes[off] | (bytes[off + 1] << 8) | (bytes[off + 2] << 16) | (bytes[off + 3] << 24)) >>> 0;
}

function readNullTerminated(bytes, start, end) {
  let zero = start;
  while (zero < end && bytes[zero] !== 0) zero++;
  let out = '';
  for (let i = start; i < zero; i++) {
    const b = bytes[i];
    if (b >= 0x20 && b <= 0x7e) out += String.fromCharCode(b);
  }
  return out;
}

function parseZedArchive(bytes) {
  if (bytes.length < FIXED_HEADER_SIZE) throw new Error(`Too small for ZED (${bytes.length})`);
  const slotCount = u32(bytes, 0x04);
  const stringTableSize = u32(bytes, 0x08);
  const stringBase = u32(bytes, 0x0c);
  const bodySize = u32(bytes, 0x54);
  const entryCount = slotCount - 1;
  const stringTableStart = FIXED_HEADER_SIZE;
  const stringTableEnd = stringTableStart + stringTableSize;
  const firstRecordOffset = FIXED_HEADER_SIZE + stringTableSize + 0x10;
  const bodyStart = bytes.length - bodySize;
  const entries = [];
  const entryData = new Map();
  for (let i = 0; i < entryCount; i++) {
    const off = firstRecordOffset + i * RECORD_SIZE;
    const namePointer = u32(bytes, off);
    const relativeOffset = u32(bytes, off + 4);
    const size = u32(bytes, off + 8);
    const childCount = u32(bytes, off + 12);
    let name = '(unnamed)';
    if (namePointer >= stringBase) {
      const stringOffset = stringTableStart + (namePointer - stringBase);
      if (stringOffset >= stringTableStart && stringOffset < stringTableEnd) {
        name = readNullTerminated(bytes, stringOffset, stringTableEnd);
      }
    }
    let payload = Buffer.alloc(0);
    if (size > 0) {
      const start = bodyStart + relativeOffset;
      const end = start + size;
      if (start >= bodyStart && end <= bytes.length) payload = bytes.subarray(start, end);
    }
    entries.push({ index: i, name, size, childCount });
    entryData.set(i, payload);
  }
  return { entries, entryData };
}

function parseTextureHeader(bytes) {
  if (bytes.length < TEXTURE_HEADER_SIZE) throw new Error(`texture too small (${bytes.length})`);
  const width = bytes.readUInt16LE(0x00);
  const height = bytes.readUInt16LE(0x02);
  const paletteId = bytes.readUInt16LE(0x08);
  const formatA = bytes.readUInt16LE(0x0c);
  const formatB = bytes.readUInt16LE(0x0e);
  const indexedSize = width * height + TEXTURE_HEADER_SIZE + TEXTURE_FOOTER_SIZE;
  const rgbaSize = width * height * 4 + TEXTURE_HEADER_SIZE + TEXTURE_FOOTER_SIZE;
  let bpp = 8;
  let isIndexed = true;
  if (formatA === 0x0620 || bytes.length === rgbaSize) {
    bpp = 32;
    isIndexed = false;
  } else if (formatA === 0x0608 || bytes.length === indexedSize) {
    bpp = 8;
    isIndexed = true;
  } else if (Math.abs(bytes.length - rgbaSize) < Math.abs(bytes.length - indexedSize)) {
    bpp = 32;
    isIndexed = false;
  }
  return { width, height, paletteId, formatA, formatB, bpp, isIndexed };
}

function expand5To8(v) {
  return ((v << 3) | (v >> 2)) & 0xff;
}

function ps2AlphaToPngAlpha(v) {
  return v <= 128 ? Math.min(255, v * 2) : v;
}

function rgba5551ToRgba8888(clut) {
  const out = Buffer.alloc(256 * 4);
  for (let i = 0; i < 256 && i * 2 + 1 < clut.length; i++) {
    const v = clut.readUInt16LE(i * 2);
    const r = expand5To8(v & 0x1f);
    const g = expand5To8((v >> 5) & 0x1f);
    const b = expand5To8((v >> 10) & 0x1f);
    const hasStp = (v & 0x8000) !== 0;
    out[i * 4 + 0] = r;
    out[i * 4 + 1] = g;
    out[i * 4 + 2] = b;
    out[i * 4 + 3] = (r !== 0 || g !== 0 || b !== 0 || hasStp) ? 255 : 0;
  }
  return out;
}

function rgba32PaletteToRgba8888(clut) {
  const out = Buffer.alloc(256 * 4);
  for (let i = 0; i < 256 && i * 4 + 3 < clut.length; i++) {
    const p = i * 4;
    const alpha = ps2AlphaToPngAlpha(clut[p + 3]);
    out[p + 0] = clut[p + 0];
    out[p + 1] = clut[p + 1];
    out[p + 2] = clut[p + 2];
    out[p + 3] = alpha > 0 || clut[p] !== 0 || clut[p + 1] !== 0 || clut[p + 2] !== 0 ? 255 : 0;
  }
  return out;
}

function unswizzlePs2Clut8(rgba) {
  const out = Buffer.alloc(256 * 4);
  for (let i = 0; i < 256; i++) {
    const j = (i & 0xe7) | ((i & 0x08) << 1) | ((i & 0x10) >> 1);
    rgba.copy(out, i * 4, j * 4, j * 4 + 4);
  }
  return out;
}

function decodeClutBuffer(buf) {
  if (buf.length >= 1024) return unswizzlePs2Clut8(rgba32PaletteToRgba8888(buf.subarray(0, 1024)));
  if (buf.length >= 512) return unswizzlePs2Clut8(rgba5551ToRgba8888(buf.subarray(0, 512)));
  throw new Error(`CLUT too small (${buf.length})`);
}

function parsePaletteArchive(archive) {
  const byName = new Map();
  const ordered = [];
  for (let i = 0; i < archive.entries.length; i++) {
    const node = archive.entries[i];
    const m = /^texpal_(\d+)$/i.exec(node.name);
    if (!m) continue;
    let bufIndex = -1;
    for (let j = i + 1; j < Math.min(i + 4, archive.entries.length); j++) {
      if (/^buf$/i.test(archive.entries[j].name)) {
        bufIndex = j;
        break;
      }
      if (/^texpal_/i.test(archive.entries[j].name)) break;
    }
    if (bufIndex < 0) continue;
    try {
      const id = Number(m[1]);
      const rgba = decodeClutBuffer(archive.entryData.get(archive.entries[bufIndex].index));
      const pal = { name: node.name.toLowerCase(), paletteId: id, rgba };
      byName.set(pal.name, pal);
      ordered.push(pal);
    } catch {}
  }
  return { byName, ordered };
}

function listTextures(txr) {
  const textures = [];
  for (let i = 0; i < txr.entries.length; i++) {
    const node = txr.entries[i];
    if (!/\.tif$/i.test(node.name)) continue;
    let dataIndex = -1;
    for (let j = i + 1; j < Math.min(i + 4, txr.entries.length); j++) {
      if (/^texdat$/i.test(txr.entries[j].name)) {
        dataIndex = j;
        break;
      }
      if (/\.tif$/i.test(txr.entries[j].name)) break;
    }
    if (dataIndex < 0) continue;
    const data = txr.entryData.get(txr.entries[dataIndex].index);
    if (!data || data.length < TEXTURE_HEADER_SIZE) continue;
    try {
      const header = parseTextureHeader(data);
      textures.push({ name: node.name.toLowerCase(), data, header });
    } catch {}
  }
  return textures;
}

function decodeTexture(data, palette) {
  const header = parseTextureHeader(data);
  const out = Buffer.alloc(header.width * header.height * 4);
  if (!header.isIndexed) {
    const src = data.subarray(TEXTURE_HEADER_SIZE, TEXTURE_HEADER_SIZE + header.width * header.height * 4);
    for (let i = 0; i + 3 < src.length; i += 4) {
      out[i] = src[i];
      out[i + 1] = src[i + 1];
      out[i + 2] = src[i + 2];
      out[i + 3] = ps2AlphaToPngAlpha(src[i + 3]);
    }
    return flipRows(out, header.width, header.height);
  }
  const indices = data.subarray(TEXTURE_HEADER_SIZE, TEXTURE_HEADER_SIZE + header.width * header.height);
  if (palette) {
    for (let i = 0; i < indices.length; i++) {
      const p = indices[i] * 4;
      out[i * 4 + 0] = palette.rgba[p + 0];
      out[i * 4 + 1] = palette.rgba[p + 1];
      out[i * 4 + 2] = palette.rgba[p + 2];
      out[i * 4 + 3] = palette.rgba[p + 3];
    }
  } else {
    for (let i = 0; i < indices.length; i++) {
      const v = indices[i];
      out[i * 4 + 0] = v;
      out[i * 4 + 1] = v;
      out[i * 4 + 2] = v;
      out[i * 4 + 3] = 255;
    }
  }
  return flipRows(out, header.width, header.height);
}

function flipRows(src, width, height) {
  const stride = width * 4;
  const out = Buffer.alloc(stride * height);
  for (let y = 0; y < height; y++) {
    src.copy(out, y * stride, (height - 1 - y) * stride, (height - y) * stride);
  }
  return out;
}

function stats(rgba) {
  let opaque = 0;
  let black = 0;
  let r = 0;
  let g = 0;
  let b = 0;
  const seen = new Set();
  for (let i = 0; i + 3 < rgba.length; i += 4) {
    const a = rgba[i + 3];
    if (a > 0) {
      opaque++;
      r += rgba[i];
      g += rgba[i + 1];
      b += rgba[i + 2];
      if (rgba[i] + rgba[i + 1] + rgba[i + 2] < 25) black++;
      if (seen.size < 512) seen.add(`${rgba[i]},${rgba[i + 1]},${rgba[i + 2]},${a}`);
    }
  }
  return {
    opaque,
    blackRatio: opaque ? black / opaque : 0,
    avgR: opaque ? r / opaque : 0,
    avgG: opaque ? g / opaque : 0,
    avgB: opaque ? b / opaque : 0,
    uniqueColors: seen.size,
  };
}

function writeBmp(filePath, width, height, rgba) {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  const headerSize = 14 + 40;
  const pixels = Buffer.alloc(width * height * 4);
  for (let i = 0; i < width * height; i++) {
    pixels[i * 4 + 0] = rgba[i * 4 + 2];
    pixels[i * 4 + 1] = rgba[i * 4 + 1];
    pixels[i * 4 + 2] = rgba[i * 4 + 0];
    pixels[i * 4 + 3] = rgba[i * 4 + 3];
  }
  const out = Buffer.alloc(headerSize + pixels.length);
  out.write('BM', 0, 'ascii');
  out.writeUInt32LE(out.length, 2);
  out.writeUInt32LE(headerSize, 10);
  out.writeUInt32LE(40, 14);
  out.writeInt32LE(width, 18);
  out.writeInt32LE(-height, 22);
  out.writeUInt16LE(1, 26);
  out.writeUInt16LE(32, 28);
  out.writeUInt32LE(pixels.length, 34);
  pixels.copy(out, headerSize);
  fs.writeFileSync(filePath, out);
}

function drawScaled(dst, dstWidth, dstHeight, src, srcWidth, srcHeight, x, y, w, h) {
  for (let dy = 0; dy < h; dy++) {
    const sy = Math.min(srcHeight - 1, Math.floor(dy * srcHeight / h));
    const dstY = y + dy;
    if (dstY < 0 || dstY >= dstHeight) continue;
    for (let dx = 0; dx < w; dx++) {
      const sx = Math.min(srcWidth - 1, Math.floor(dx * srcWidth / w));
      const dstX = x + dx;
      if (dstX < 0 || dstX >= dstWidth) continue;
      const sp = (sy * srcWidth + sx) * 4;
      const dp = (dstY * dstWidth + dstX) * 4;
      dst[dp + 0] = src[sp + 0];
      dst[dp + 1] = src[sp + 1];
      dst[dp + 2] = src[sp + 2];
      dst[dp + 3] = 255;
    }
  }
}

function makeContactSheet(items, filePath) {
  const cell = 96;
  const gutter = 8;
  const cols = 8;
  const rows = Math.max(1, Math.ceil(items.length / cols));
  const width = cols * cell + (cols + 1) * gutter;
  const height = rows * cell + (rows + 1) * gutter;
  const canvas = Buffer.alloc(width * height * 4, 24);
  for (let i = 0; i < width * height; i++) canvas[i * 4 + 3] = 255;
  for (let i = 0; i < items.length; i++) {
    const item = items[i];
    const col = i % cols;
    const row = Math.floor(i / cols);
    const x = gutter + col * (cell + gutter);
    const y = gutter + row * (cell + gutter);
    const scale = Math.min(cell / item.width, cell / item.height);
    const w = Math.max(1, Math.floor(item.width * scale));
    const h = Math.max(1, Math.floor(item.height * scale));
    drawScaled(canvas, width, height, item.rgba, item.width, item.height, x + Math.floor((cell - w) / 2), y + Math.floor((cell - h) / 2), w, h);
  }
  writeBmp(filePath, width, height, canvas);
}

function safeName(name) {
  return name.replace(/[^a-z0-9_.-]+/gi, '_');
}

function loadFamily(family) {
  const txrPath = path.join(mapDir, `${family}_TXR.ZED`);
  const palPath = path.join(mapDir, `${family}_PAL.ZED`);
  if (!fs.existsSync(txrPath)) return null;
  const txr = parseZedArchive(fs.readFileSync(txrPath));
  const pal = fs.existsSync(palPath) ? parseZedArchive(fs.readFileSync(palPath)) : null;
  return { family, txr, pal, txrPath, palPath };
}

function paletteForHeader(palettes, header) {
  if (!palettes) return null;
  return palettes.byName.get(`texpal_${header.paletteId}`) || palettes.ordered.find(p => p.paletteId === header.paletteId) || null;
}

function run() {
  fs.mkdirSync(outRoot, { recursive: true });
  const html = [];
  const csv = ['family,texture,width,height,bpp,headerPalette,aliasPalette,headerPaletteName,aliasPaletteName,aliasBlackRatio,headerBlackRatio,aliasAvgR,aliasAvgG,aliasAvgB,headerAvgR,headerAvgG,headerAvgB,aliasUniqueColors,headerUniqueColors'];
  html.push('<!doctype html><meta charset="utf-8"><title>Map texture diagnostics</title>');
  html.push('<style>body{font-family:Segoe UI,Arial,sans-serif;background:#181818;color:#ddd}section{margin:24px 0}table{border-collapse:collapse;width:100%}td,th{border:1px solid #333;padding:4px 6px;font-size:12px}img{image-rendering:pixelated;background:#000;max-width:96px;max-height:96px}.warn{color:#ffb86b}</style>');
  html.push(`<h1>${path.basename(mapDir)} texture diagnostics</h1>`);
  for (const family of requestedFamilies) {
    const loaded = loadFamily(family);
    if (!loaded) continue;
    const palettes = loaded.pal ? parsePaletteArchive(loaded.pal) : null;
    const textures = listTextures(loaded.txr);
    const contactItems = [];
    let paletteCursor = 0;
    html.push(`<section><h2>${family}</h2><p>${textures.length} texture(s), ${palettes ? palettes.ordered.length : 0} palette(s)</p>`);
    html.push('<table><thead><tr><th>Texture</th><th>Header</th><th>Alias</th><th>Alias decode</th><th>Header decode</th><th>Notes</th></tr></thead><tbody>');
    for (const tex of textures) {
      const aliasPalette = tex.header.isIndexed && palettes ? palettes.ordered[paletteCursor++] || null : null;
      const headerPalette = tex.header.isIndexed ? paletteForHeader(palettes, tex.header) : null;
      const aliasRgba = decodeTexture(tex.data, aliasPalette);
      const headerRgba = decodeTexture(tex.data, headerPalette);
      const aliasStats = stats(aliasRgba);
      const headerStats = stats(headerRgba);
      const base = safeName(tex.name);
      const aliasRel = `${family}/alias/${base}.bmp`;
      const headerRel = `${family}/header/${base}.bmp`;
      writeBmp(path.join(outRoot, aliasRel), tex.header.width, tex.header.height, aliasRgba);
      writeBmp(path.join(outRoot, headerRel), tex.header.width, tex.header.height, headerRgba);
      contactItems.push({ width: tex.header.width, height: tex.header.height, rgba: aliasRgba });
      const noteParts = [];
      if (tex.header.isIndexed && !aliasPalette) noteParts.push('no alias palette');
      if (tex.header.isIndexed && !headerPalette) noteParts.push('no header palette');
      if (tex.header.isIndexed && Math.abs(aliasStats.blackRatio - headerStats.blackRatio) > 0.25) noteParts.push('large black-ratio delta');
      csv.push([
        family,
        tex.name,
        tex.header.width,
        tex.header.height,
        tex.header.bpp,
        tex.header.paletteId,
        aliasPalette?.paletteId ?? '',
        headerPalette?.name ?? '',
        aliasPalette?.name ?? '',
        aliasStats.blackRatio.toFixed(4),
        headerStats.blackRatio.toFixed(4),
        aliasStats.avgR.toFixed(1),
        aliasStats.avgG.toFixed(1),
        aliasStats.avgB.toFixed(1),
        headerStats.avgR.toFixed(1),
        headerStats.avgG.toFixed(1),
        headerStats.avgB.toFixed(1),
        aliasStats.uniqueColors,
        headerStats.uniqueColors,
      ].map(v => `"${String(v).replace(/"/g, '""')}"`).join(','));
      html.push(`<tr><td>${tex.name}<br>${tex.header.width}x${tex.header.height} ${tex.header.bpp}bpp</td><td>${tex.header.paletteId}<br>${headerPalette?.name ?? ''}</td><td>${aliasPalette?.paletteId ?? ''}<br>${aliasPalette?.name ?? ''}</td><td><img src="${aliasRel.replace(/\\/g, '/')}"><br>black ${(aliasStats.blackRatio * 100).toFixed(1)}%</td><td><img src="${headerRel.replace(/\\/g, '/')}"><br>black ${(headerStats.blackRatio * 100).toFixed(1)}%</td><td class="${noteParts.length ? 'warn' : ''}">${noteParts.join('<br>')}</td></tr>`);
    }
    html.push('</tbody></table></section>');
    makeContactSheet(contactItems, path.join(outRoot, `${family}_alias_contact.bmp`));
    html.push(`<p><a href="${family}_alias_contact.bmp">Alias contact sheet</a></p>`);
  }
  fs.writeFileSync(path.join(outRoot, 'index.html'), html.join('\n'));
  fs.writeFileSync(path.join(outRoot, 'report.csv'), csv.join('\n'));
  console.log(`Wrote diagnostics to ${outRoot}`);
}

run();
