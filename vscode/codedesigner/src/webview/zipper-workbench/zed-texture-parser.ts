// Decoder for Zipper PS2 ".tif" texture records and PAL palettes packed inside
// ZED containers. Pixel data is delivered as RGBA8888, already flipped to top-
// down orientation for browser display.

export interface ZedTextureHeader {
  width: number;
  height: number;
  paletteId: number;
  formatA: number;
  formatB: number;
  bpp: number;
  isIndexed: boolean;
}

export interface ZedTexture {
  header: ZedTextureHeader;
  rgba: Uint8Array;
  paletteResolved: boolean;
}

export interface ZedPalette {
  // Real PAL name id, for example texpal_563 -> 563.
  paletteId: number;
  rgba: Uint8Array;
}

const TEXTURE_HEADER_SIZE = 0x20;
const TEXTURE_FOOTER_SIZE = 0x80;

export function base64ToBytes(value: string): Uint8Array {
  const binary = atob(value);
  const out = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    out[i] = binary.charCodeAt(i);
  }
  return out;
}

export function parseTextureHeader(bytes: Uint8Array): ZedTextureHeader {
  if (bytes.length < TEXTURE_HEADER_SIZE) {
    throw new Error(`texture too small (${bytes.length} bytes)`);
  }

  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const width = view.getUint16(0x00, true);
  const height = view.getUint16(0x02, true);

  // This value is the TXR texture/material slot id, not always the literal PAL
  // name id. For example UIBF_TXR uses ids 1..24 while UIBF_PAL names are
  // texpal_563..584. `aliasPalettesForTextureEntries` wires those together.
  const paletteId = view.getUint16(0x08, true);
  const formatA = view.getUint16(0x0c, true);
  const formatB = view.getUint16(0x0e, true);

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

// Decode a 32-bit RGBA texture. PS2 GS alpha often uses 0..128; expand to PNG.
function decodeRgba32(bytes: Uint8Array, header: ZedTextureHeader): Uint8Array {
  const { width, height } = header;
  const pixelBytes = width * height * 4;
  const src = bytes.subarray(TEXTURE_HEADER_SIZE, TEXTURE_HEADER_SIZE + pixelBytes);
  const out = new Uint8Array(pixelBytes);
  for (let i = 0; i + 3 < src.length; i += 4) {
    out[i + 0] = src[i + 0];
    out[i + 1] = src[i + 1];
    out[i + 2] = src[i + 2];
    out[i + 3] = ps2AlphaToPngAlpha(src[i + 3]);
  }
  return flipRowsRgba(out, width, height);
}

function flipRowsRgba(src: Uint8Array, width: number, height: number): Uint8Array {
  const stride = width * 4;
  const out = new Uint8Array(stride * height);
  for (let y = 0; y < height; y++) {
    const srcRow = (height - 1 - y) * stride;
    out.set(src.subarray(srcRow, srcRow + stride), y * stride);
  }
  return out;
}

function expand5To8(value: number): number {
  return ((value << 3) | (value >> 2)) & 0xff;
}

function ps2AlphaToPngAlpha(value: number): number {
  // PS2 GS alpha is commonly 0..128 rather than 0..255.
  return value <= 128 ? Math.min(255, value * 2) : value;
}

function rgba5551ToRgba8888(clut: Uint8Array): Uint8Array {
  const out = new Uint8Array(256 * 4);
  const view = new DataView(clut.buffer, clut.byteOffset, clut.byteLength);
  for (let i = 0; i < 256; i++) {
    const v = view.getUint16(i * 2, true);
    const r = expand5To8(v & 0x1f);
    const g = expand5To8((v >> 5) & 0x1f);
    const b = expand5To8((v >> 10) & 0x1f);
    const hasStp = (v & 0x8000) !== 0;
    out[i * 4 + 0] = r;
    out[i * 4 + 1] = g;
    out[i * 4 + 2] = b;
    // PS2 16-bit CLUT bit 15 is controlled by TEXA/STP state; treating a
    // clear bit as PNG alpha 0 punches black holes through opaque textures.
    // Keep real coloured entries visible and reserve transparency for the
    // fully-empty palette entry.
    out[i * 4 + 3] = (r !== 0 || g !== 0 || b !== 0 || hasStp) ? 255 : 0;
  }
  return out;
}

function rgba32PaletteToRgba8888(clut: Uint8Array): Uint8Array {
  const out = new Uint8Array(256 * 4);
  for (let i = 0; i < 256; i++) {
    const p = i * 4;
    const alpha = ps2AlphaToPngAlpha(clut[p + 3]);
    out[p + 0] = clut[p + 0];
    out[p + 1] = clut[p + 1];
    out[p + 2] = clut[p + 2];
    out[p + 3] = alpha > 0 || clut[p + 0] !== 0 || clut[p + 1] !== 0 || clut[p + 2] !== 0 ? 255 : 0;
  }
  return out;
}

// PS2 8-bit CLUT order swaps bit 3 and bit 4 of the palette index. Do this at
// the decoded RGBA-entry level so it works for both 16-bit and 32-bit CLUTs.
function unswizzlePs2Clut8(rgba: Uint8Array): Uint8Array {
  const out = new Uint8Array(256 * 4);
  for (let i = 0; i < 256; i++) {
    const j = (i & 0xe7) | ((i & 0x08) << 1) | ((i & 0x10) >> 1);
    out.set(rgba.subarray(j * 4, j * 4 + 4), i * 4);
  }
  return out;
}

// Decode a raw CLUT buffer into RGBA8888 with the PS2 8-bit CLUT unswizzle.
// Buffers are either 512 bytes (256 x RGBA5551) or 1024 bytes (256 x RGBA8888).
function decodeClutBuffer(buf: Uint8Array): Uint8Array {
  if (buf.length >= 1024) {
    return unswizzlePs2Clut8(rgba32PaletteToRgba8888(buf.subarray(0, 1024)));
  }
  if (buf.length >= 512) {
    return unswizzlePs2Clut8(rgba5551ToRgba8888(buf.subarray(0, 512)));
  }
  throw new Error(`CLUT too small (${buf.length} bytes)`);
}

// Real PAL.ZED files store palettes as triplets of entries:
//   texpal_NNN (size 0, name marker)
//   par        (size 8, per-palette params)
//   buf        (size 512 or 1024, the CLUT data)
// Walk the entry list and group them.
export function parsePaletteNodes(
  nodes: Array<{ index: number; name: string; data: string }>,
): Map<string, ZedPalette> {
  return parsePaletteEntries(
    nodes.map(n => ({ name: n.name })),
    i => base64ToBytes(nodes[i].data),
  );
}

// Variant that consumes entries already extracted in memory. Used when we've
// drilled into a nested PAL archive via the client-side parser.
export function parsePaletteEntries(
  entries: Array<{ name: string }>,
  getBytes: (i: number) => Uint8Array,
): Map<string, ZedPalette> {
  const result = new Map<string, ZedPalette>();
  let sequential = 1;

  for (let i = 0; i < entries.length; i++) {
    const node = entries[i];
    const m = /^texpal_(\d+)$/i.exec(node.name);
    if (!m) { continue; }

    const paletteId = Number(m[1]);
    let bufIndex = -1;
    for (let j = i + 1; j < Math.min(i + 4, entries.length); j++) {
      if (/^buf$/i.test(entries[j].name)) { bufIndex = j; break; }
      if (/^texpal_/i.test(entries[j].name)) { break; }
    }
    if (bufIndex < 0) { continue; }

    try {
      const buf = getBytes(bufIndex);
      if (buf.length < 512) { continue; }
      const pal: ZedPalette = { paletteId, rgba: decodeClutBuffer(buf) };
      result.set(node.name.toLowerCase(), pal);

      // Useful fallback for simple files where the texture header's palette slot
      // is just 1,2,3... instead of the real texpal_NNN name.
      if (!result.has(`texpal_${sequential}`)) {
        result.set(`texpal_${sequential}`, pal);
      }
      sequential++;
    } catch { /* skip invalid palette */ }
  }

  return result;
}

// Adds exact aliases from TXR texture-slot ids to PAL entries by walking the TXR
// entries and assigning palettes only to indexed texdat records. This fixes
// files like UIBF where texture ids are 1..24 but PAL names are 563..584, and
// where RGBA32 texture records consume a texture id but no palette.
export function aliasPalettesForTextureEntries(
  palettes: Map<string, ZedPalette>,
  entries: Array<{ name: string }>,
  getBytes: (i: number) => Uint8Array,
): Map<string, ZedPalette> {
  if (palettes.size === 0) { return palettes; }

  const out = new Map<string, ZedPalette>(palettes);
  const paletteList: ZedPalette[] = [];
  const seen = new Set<number>();

  for (const [key, value] of palettes) {
    if (!/^texpal_\d+$/i.test(key)) { continue; }
    if (seen.has(value.paletteId)) { continue; }
    seen.add(value.paletteId);
    paletteList.push(value);
  }

  let paletteCursor = 0;
  for (let i = 0; i < entries.length; i++) {
    if (!/\.tif$/i.test(entries[i].name)) { continue; }

    let dataIndex = -1;
    for (let j = i + 1; j < Math.min(i + 4, entries.length); j++) {
      if (/^texdat$/i.test(entries[j].name)) { dataIndex = j; break; }
      if (/\.tif$/i.test(entries[j].name)) { break; }
    }
    if (dataIndex < 0) { continue; }

    try {
      const header = parseTextureHeader(getBytes(dataIndex));
      if (!header.isIndexed) { continue; }
      const pal = paletteList[paletteCursor++];
      if (!pal) { break; }
      // Keep only the tif-name alias. Overwriting texpal_${header.paletteId}
      // with the order-based palette breaks header-palId lookups for other
      // textures and was the cause of "wrong colours per weapon".
      out.set(entries[i].name.toLowerCase(), pal);
    } catch { /* skip bad texture record */ }
  }

  return out;
}

// Decode a full texture, resolving the palette from a parsed palette map if
// needed. Returns rgba pixels in top-down orientation.
export function parseTexture(
  bytes: Uint8Array,
  palettes: Map<string, ZedPalette> | null,
  paletteAlias?: string | null,
): ZedTexture {
  const header = parseTextureHeader(bytes);

  if (!header.isIndexed) {
    return { header, rgba: decodeRgba32(bytes, header), paletteResolved: true };
  }

  const pixelBytes = header.width * header.height;
  const indices = bytes.subarray(TEXTURE_HEADER_SIZE, TEXTURE_HEADER_SIZE + pixelBytes);

  let palette: ZedPalette | undefined;
  if (palettes) {
    // Alias-first lookup. `aliasPalettesForTextureEntries` wires each .tif
    // name (e.g. "door_wood.tif") to the exact palette that came from the
    // SAME TXR/PAL pair the texture lives in. That pairing is the only
    // reliable signal once palettes from multiple prefixes (CLIB / FLIB /
    // M51 / WEAP) get merged into one map — byId-first would happily pick
    // up texpal_44 from a different package because paletteId 44 isn't
    // unique across packages.
    if (paletteAlias) {
      palette = palettes.get(paletteAlias.toLowerCase());
    }
    const expectedName = `texpal_${header.paletteId}`.toLowerCase();
    palette ??= palettes.get(expectedName);
    if (!palette) {
      for (const candidate of palettes.values()) {
        if (candidate.paletteId === header.paletteId) {
          palette = candidate;
          break;
        }
      }
    }
  }

  const flat = new Uint8Array(header.width * header.height * 4);
  if (palette) {
    for (let i = 0; i < indices.length; i++) {
      const idx = indices[i] * 4;
      flat[i * 4 + 0] = palette.rgba[idx + 0];
      flat[i * 4 + 1] = palette.rgba[idx + 1];
      flat[i * 4 + 2] = palette.rgba[idx + 2];
      flat[i * 4 + 3] = palette.rgba[idx + 3];
    }
  } else {
    // No palette: render indices as grayscale so the user still sees structure.
    for (let i = 0; i < indices.length; i++) {
      const v = indices[i];
      flat[i * 4 + 0] = v;
      flat[i * 4 + 1] = v;
      flat[i * 4 + 2] = v;
      flat[i * 4 + 3] = 255;
    }
  }

  return {
    header,
    rgba: flipRowsRgba(flat, header.width, header.height),
    paletteResolved: !!palette,
  };
}
