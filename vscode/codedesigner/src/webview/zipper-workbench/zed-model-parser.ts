// Decoder for Zipper PS2 model payloads stored inside ZED containers
// (WEAP_MDL.ZED, WORL_MDL.ZED, and friends).
//
// Chunk format verified against the working C# decoder shipped with the format
// notes:
//   chunk + 0x00 = 0x01000101
//   chunk + 0x04 = 0x6C048000
//   chunk + 0x08 = 0x00008000
//   chunk + 0x30 = vertexCount   (int32)
//   chunk + 0x34 = triangleCount (int32)
//   chunk + 0x44 = scale         (float32)
//   chunk + 0x48 = 03 02 00 01   (geometry command marker)
//   chunk + 0x50 = vertex records, 0x10 bytes each (int16 X, Y, Z, W + 8 bytes)
//   vertexEnd + 0x08 = triangle table, 4 bytes each (byte a, b, c, flags)
//   Triangle indices are packed VU slot offsets; real index = packed / 3.

export interface ZedModelVertex {
  x: number;
  y: number;
  z: number;
  u: number;
  v: number;
}

export interface ZedModelChunk {
  offset: number;
  vertexCount: number;
  triangleCount: number;
  decodedTriangleCount: number;
  scale: number;
  vertices: ZedModelVertex[];
  indices: number[];
  // Name of the .tif this chunk references (lowercased). Found in the tail
  // area after the triangle table, before the next chunk header.
  textureName: string | null;
}

export interface ZedModel {
  chunks: ZedModelChunk[];
  vertices: ZedModelVertex[];
  indices: number[];
  textureRefs: string[];
  boundingRadius: number;
}

export function looksLikeModel(bytes: Uint8Array): boolean {
  return findChunkOffsets(bytes).length > 0;
}

// Detect a nested ZED container payload. ZED files start with a 0x64-byte
// header where +0x10 = 0x10 (default HeaderWord10), +0x04 = slot count, and
// +0x08 = string table size. If the entry we're previewing is itself a ZED
// file, we should NOT parse it as a model.
export function looksLikeZedContainer(bytes: Uint8Array): boolean {
  if (bytes.length < 0x64) { return false; }
  const headerWord10 = bytes[0x10] | (bytes[0x11] << 8) | (bytes[0x12] << 16) | (bytes[0x13] << 24);
  if (headerWord10 !== 0x10) { return false; }
  const slotCount =
    bytes[0x04] | (bytes[0x05] << 8) | (bytes[0x06] << 16) | (bytes[0x07] << 24);
  if (slotCount === 0 || slotCount > 1_000_000) { return false; }
  const strTableSize =
    bytes[0x08] | (bytes[0x09] << 8) | (bytes[0x0a] << 16) | (bytes[0x0b] << 24);
  if (strTableSize > bytes.length) { return false; }
  return true;
}

function readU32LE(bytes: Uint8Array, offset: number): number {
  return (
    bytes[offset] |
    (bytes[offset + 1] << 8) |
    (bytes[offset + 2] << 16) |
    (bytes[offset + 3] << 24)
  ) >>> 0;
}

function findChunkOffsets(bytes: Uint8Array): number[] {
  const offsets: number[] = [];
  const limit = bytes.length - 0x50;
  for (let i = 0; i <= limit; i += 0x10) {
    if (readU32LE(bytes, i + 0x00) !== 0x01000101) { continue; }
    if (readU32LE(bytes, i + 0x04) !== 0x6c048000) { continue; }
    if (readU32LE(bytes, i + 0x08) !== 0x00008000) { continue; }
    if (
      bytes[i + 0x48] !== 0x03 ||
      bytes[i + 0x49] !== 0x02 ||
      bytes[i + 0x4a] !== 0x00 ||
      bytes[i + 0x4b] !== 0x01
    ) { continue; }

    const view = new DataView(bytes.buffer, bytes.byteOffset + i, 0x50);
    const vertexCount = view.getInt32(0x30, true);
    const triangleCount = view.getInt32(0x34, true);
    // Tight bounds exactly match the working HTML-spec C# decoder.
    if (vertexCount <= 0 || vertexCount > 1000) { continue; }
    if (triangleCount < 0 || triangleCount > 1000) { continue; }
    const expectedEnd = i + 0x50 + vertexCount * 0x10 + 0x08 + triangleCount * 0x04;
    if (expectedEnd > bytes.length) { continue; }

    offsets.push(i);
  }
  return offsets;
}

function extractTextureRefs(bytes: Uint8Array, start: number, end: number): string[] {
  const refs = new Set<string>();
  const clampedEnd = Math.min(end, bytes.length);
  let runStart = -1;
  for (let i = start; i < clampedEnd; i++) {
    const b = bytes[i];
    const printable = b >= 0x20 && b <= 0x7e;
    if (printable) {
      if (runStart < 0) { runStart = i; }
    } else {
      if (runStart >= 0 && i - runStart >= 4) {
        const text = new TextDecoder('ascii').decode(bytes.subarray(runStart, i));
        const match = text.match(/[A-Za-z0-9_\-.]+\.tif/i);
        if (match) { refs.add(match[0]); }
      }
      runStart = -1;
    }
  }
  return [...refs];
}

function findTifInRange(bytes: Uint8Array, start: number, end: number): string | null {
  const clampedEnd = Math.min(end, bytes.length);
  let runStart = -1;
  for (let i = start; i < clampedEnd; i++) {
    const b = bytes[i];
    const printable = b >= 0x20 && b <= 0x7e;
    if (printable) {
      if (runStart < 0) { runStart = i; }
    } else {
      if (runStart >= 0 && i - runStart >= 5) {
        const text = new TextDecoder('ascii').decode(bytes.subarray(runStart, i));
        const m = text.match(/[A-Za-z0-9_\-.]+\.tif/i);
        if (m) { return m[0].toLowerCase(); }
      }
      runStart = -1;
    }
  }
  return null;
}

function parseChunk(bytes: Uint8Array, offset: number, nextChunkOffset: number): ZedModelChunk | null {
  const view = new DataView(bytes.buffer, bytes.byteOffset + offset, Math.min(0x50, bytes.length - offset));
  const vertexCount = view.getInt32(0x30, true);
  const triangleCount = view.getInt32(0x34, true);
  let scale = view.getFloat32(0x44, true);
  if (!Number.isFinite(scale) || scale === 0) { scale = 1; }

  const vertexStart = offset + 0x50;
  const vertexBlockSize = vertexCount * 0x10;
  if (vertexStart + vertexBlockSize > bytes.length) { return null; }

  const vertices: ZedModelVertex[] = [];
  for (let i = 0; i < vertexCount; i++) {
    const o = vertexStart + i * 0x10;
    const v = new DataView(bytes.buffer, bytes.byteOffset + o, 0x10);
    const xi = v.getInt16(0, true);
    const yi = v.getInt16(2, true);
    const zi = v.getInt16(4, true);
    // Remaining 8 bytes (offsets 0x08..0x0f) carry packed UV / normal / color.
    // Treat the next two int16s as UV in [0, 1] fixed-point for preview render.
    const ui = v.getInt16(0x08, true);
    const vi = v.getInt16(0x0a, true);
    vertices.push({
      x: (xi / 32767) * scale,
      y: (yi / 32767) * scale,
      z: (zi / 32767) * scale,
      u: ui / 4096,
      v: vi / 4096,
    });
  }

  const triCommand = vertexStart + vertexBlockSize;
  const triData = triCommand + 0x08;

  const indices: number[] = [];
  let decoded = 0;
  for (let i = 0; i < triangleCount; i++) {
    const o = triData + i * 4;
    if (o + 4 > bytes.length) { break; }
    const aPacked = bytes[o + 0];
    const bPacked = bytes[o + 1];
    const cPacked = bytes[o + 2];
    // bytes[o + 3] is flags; we don't use it for preview rendering yet.
    const a = (aPacked / 3) | 0;
    const b = (bPacked / 3) | 0;
    const c = (cPacked / 3) | 0;
    if (a >= vertexCount || b >= vertexCount || c >= vertexCount) { continue; }
    indices.push(a, b, c);
    decoded++;
  }

  // Tail area between triangle data and the next chunk holds the .tif name.
  const triEnd = triData + triangleCount * 4;
  const textureName = findTifInRange(bytes, triEnd, nextChunkOffset);

  return {
    offset,
    vertexCount,
    triangleCount,
    decodedTriangleCount: decoded,
    scale,
    vertices,
    indices,
    textureName,
  };
}

export function parseModel(bytes: Uint8Array): ZedModel {
  const offsets = findChunkOffsets(bytes);
  const chunks: ZedModelChunk[] = [];
  for (let i = 0; i < offsets.length; i++) {
    const off = offsets[i];
    const nextOff = i + 1 < offsets.length ? offsets[i + 1] : bytes.length;
    const chunk = parseChunk(bytes, off, nextOff);
    if (chunk) { chunks.push(chunk); }
  }

  const vertices: ZedModelVertex[] = [];
  const indices: number[] = [];
  let radius = 0;
  for (const chunk of chunks) {
    const base = vertices.length;
    for (const v of chunk.vertices) {
      vertices.push(v);
      const d = Math.hypot(v.x, v.y, v.z);
      if (d > radius) { radius = d; }
    }
    for (const idx of chunk.indices) {
      indices.push(base + idx);
    }
  }

  // Scan the full payload for embedded .tif texture references. These usually
  // live in a string block after the geometry chunks.
  const textureRefs = extractTextureRefs(bytes, 0, bytes.length);

  return {
    chunks,
    vertices,
    indices,
    textureRefs,
    boundingRadius: radius || 1,
  };
}
