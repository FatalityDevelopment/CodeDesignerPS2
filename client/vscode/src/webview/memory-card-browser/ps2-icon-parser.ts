export interface Ps2IconSys {
  titleLines: [string, string];
  title: string;
  backgroundTransparency: number;
  bgColors: number[][];
  lightDirs: number[][];
  lightColors: number[][];
  ambient: number[];
  iconFileNormal: string;
  iconFileCopy: string;
  iconFileDelete: string;
}

export interface Ps2Icon {
  animationShapes: number;
  texType: number;
  vertexCount: number;
  frameLength: number;
  animSpeed: number;
  playOffset: number;
  frameCount: number;
  vertices: number[][][];
  normals: number[][];
  uvs: number[][];
  colors: number[][];
  textureRgba?: Uint8Array;
}

const ICON_SYS_SIZE = 964;
const ICON_MAGIC = 0x00010000;
const ICON_ANIMATION_MAGIC = 0x00000001;
const FIXED_POINT = 4096;
const TEXTURE_WIDTH = 128;
const TEXTURE_HEIGHT = 128;
const TEXTURE_BYTES = TEXTURE_WIDTH * TEXTURE_HEIGHT * 2;

export function base64ToBytes(value: string): Uint8Array {
  const binary = atob(value);
  const out = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    out[i] = binary.charCodeAt(i);
  }
  return out;
}

export function parseIconSys(bytes: Uint8Array): Ps2IconSys {
  if (bytes.length < ICON_SYS_SIZE) {
    throw new Error(`icon.sys is too small (${bytes.length} bytes)`);
  }
  if (ascii(bytes, 0, 4) !== 'PS2D') {
    throw new Error('icon.sys has an invalid magic');
  }

  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const titleBreak = view.getUint16(6, true);
  const titleBytes = bytes.slice(192, 192 + 68);
  const first = decodeSjis(titleBytes.slice(0, Math.min(titleBreak, titleBytes.length)));
  const second = decodeSjis(titleBytes.slice(Math.min(titleBreak, titleBytes.length)));
  const titleLines: [string, string] = [first, second];

  return {
    titleLines,
    title: titleLines.filter(Boolean).join(' ').trim(),
    backgroundTransparency: view.getUint32(12, true),
    bgColors: [
      readU32Vec4(view, 16),
      readU32Vec4(view, 32),
      readU32Vec4(view, 48),
      readU32Vec4(view, 64),
    ],
    lightDirs: [
      readF32Vec4(view, 80),
      readF32Vec4(view, 96),
      readF32Vec4(view, 112),
    ],
    lightColors: [
      readF32Vec4(view, 128),
      readF32Vec4(view, 144),
      readF32Vec4(view, 160),
    ],
    ambient: readF32Vec4(view, 176),
    iconFileNormal: asciiZ(bytes, 260, 64),
    iconFileCopy: asciiZ(bytes, 324, 64),
    iconFileDelete: asciiZ(bytes, 388, 64),
  };
}

export function parseIcon(bytes: Uint8Array): Ps2Icon {
  if (bytes.length < 40) {
    throw new Error(`icon file is too small (${bytes.length} bytes)`);
  }

  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let offset = 0;
  const magic = view.getUint32(offset, true);
  offset += 4;
  if (magic !== ICON_MAGIC) {
    throw new Error(`invalid icon magic 0x${magic.toString(16)}`);
  }

  const animationShapes = view.getUint32(offset, true); offset += 4;
  const texType = view.getUint32(offset, true); offset += 4;
  offset += 4;
  const vertexCount = view.getUint32(offset, true); offset += 4;

  if (animationShapes === 0 || animationShapes > 128) {
    throw new Error(`invalid animation shape count ${animationShapes}`);
  }
  if (vertexCount === 0 || vertexCount > 200000) {
    throw new Error(`invalid vertex count ${vertexCount}`);
  }

  const vertices: number[][][] = [];
  const normals: number[][] = [];
  const uvs: number[][] = [];
  const colors: number[][] = [];

  for (let i = 0; i < vertexCount; i++) {
    const shapes: number[][] = [];
    for (let s = 0; s < animationShapes; s++) {
      ensure(bytes, offset, 8, 'vertex data');
      shapes.push([
        view.getInt16(offset, true) / FIXED_POINT,
        view.getInt16(offset + 2, true) / FIXED_POINT,
        view.getInt16(offset + 4, true) / FIXED_POINT,
      ]);
      offset += 8;
    }

    ensure(bytes, offset, 8, 'normal data');
    normals.push([
      view.getInt16(offset, true) / FIXED_POINT,
      view.getInt16(offset + 2, true) / FIXED_POINT,
      view.getInt16(offset + 4, true) / FIXED_POINT,
    ]);
    offset += 8;

    ensure(bytes, offset, 4, 'texture coordinate data');
    uvs.push([
      view.getInt16(offset, true) / FIXED_POINT,
      view.getInt16(offset + 2, true) / FIXED_POINT,
    ]);
    offset += 4;

    ensure(bytes, offset, 4, 'vertex color data');
    colors.push([
      normalizeIconColor(bytes[offset]),
      normalizeIconColor(bytes[offset + 1]),
      normalizeIconColor(bytes[offset + 2]),
      normalizeIconColor(bytes[offset + 3]),
    ]);
    offset += 4;

    vertices.push(shapes);
  }

  ensure(bytes, offset, 20, 'animation header');
  const animMagic = view.getUint32(offset, true); offset += 4;
  if (animMagic !== ICON_ANIMATION_MAGIC) {
    throw new Error(`invalid animation header 0x${animMagic.toString(16)}`);
  }
  const frameLength = view.getUint32(offset, true); offset += 4;
  const animSpeed = view.getFloat32(offset, true); offset += 4;
  const playOffset = view.getUint32(offset, true); offset += 4;
  const frameCount = view.getUint32(offset, true); offset += 4;

  for (let i = 0; i < frameCount; i++) {
    ensure(bytes, offset, 16, 'animation frame data');
    offset += 4;
    const keyCount = view.getUint32(offset, true);
    offset += 12;
    if (keyCount > 0) {
      offset += 8 * (keyCount - 1);
      ensure(bytes, offset, 0, 'animation key data');
    }
  }

  let textureRgba: Uint8Array | undefined;
  if ((texType & 0b0100) !== 0 && offset < bytes.length) {
    const texture = (texType & 0b1000) !== 0
      ? loadCompressedTexture(bytes, view, offset)
      : bytes.slice(offset, Math.min(offset + TEXTURE_BYTES, bytes.length));

    if (texture.length >= TEXTURE_BYTES) {
      textureRgba = decodeAbgr1555(texture.slice(0, TEXTURE_BYTES));
    }
  }

  return {
    animationShapes,
    texType,
    vertexCount,
    frameLength: Math.max(1, frameLength),
    animSpeed: Number.isFinite(animSpeed) && animSpeed > 0 ? animSpeed : 1,
    playOffset,
    frameCount,
    vertices,
    normals,
    uvs,
    colors,
    textureRgba,
  };
}

export function uniqueIconFileNames(iconSys: Ps2IconSys): string[] {
  return [
    iconSys.iconFileNormal,
    iconSys.iconFileCopy,
    iconSys.iconFileDelete,
  ].filter((name, index, names) => name.length > 0 && names.indexOf(name) === index);
}

function readU32Vec4(view: DataView, offset: number): number[] {
  return [
    view.getUint32(offset, true),
    view.getUint32(offset + 4, true),
    view.getUint32(offset + 8, true),
    view.getUint32(offset + 12, true),
  ];
}

function readF32Vec4(view: DataView, offset: number): number[] {
  return [
    view.getFloat32(offset, true),
    view.getFloat32(offset + 4, true),
    view.getFloat32(offset + 8, true),
    view.getFloat32(offset + 12, true),
  ];
}

function loadCompressedTexture(bytes: Uint8Array, view: DataView, offset: number): Uint8Array {
  ensure(bytes, offset, 4, 'compressed texture header');
  const compressedSize = view.getUint32(offset, true);
  offset += 4;
  const out: number[] = [];
  let rleOffset = 0;

  while (rleOffset < compressedSize && offset + rleOffset + 2 <= bytes.length) {
    const code = view.getUint16(offset + rleOffset, true);
    rleOffset += 2;

    if ((code & 0x8000) !== 0) {
      const count = 0x8000 - (code ^ 0x8000);
      const byteCount = count * 2;
      ensure(bytes, offset + rleOffset, byteCount, 'compressed texture literal run');
      for (let i = 0; i < byteCount; i++) {
        out.push(bytes[offset + rleOffset + i]);
      }
      rleOffset += byteCount;
    } else {
      const count = code;
      ensure(bytes, offset + rleOffset, 2, 'compressed texture repeat run');
      const lo = bytes[offset + rleOffset];
      const hi = bytes[offset + rleOffset + 1];
      for (let i = 0; i < count; i++) {
        out.push(lo, hi);
      }
      rleOffset += 2;
    }

    if (out.length >= TEXTURE_BYTES) {
      break;
    }
  }

  return new Uint8Array(out.slice(0, TEXTURE_BYTES));
}

function decodeAbgr1555(texture: Uint8Array): Uint8Array {
  const out = new Uint8Array(TEXTURE_WIDTH * TEXTURE_HEIGHT * 4);
  const view = new DataView(texture.buffer, texture.byteOffset, texture.byteLength);
  let rgbaOffset = 0;
  for (let offset = 0; offset + 1 < texture.length; offset += 2) {
    const value = view.getUint16(offset, true);
    const rgb = value & 0x7fff;
    out[rgbaOffset++] = expand5(value & 0x1f);
    out[rgbaOffset++] = expand5((value >> 5) & 0x1f);
    out[rgbaOffset++] = expand5((value >> 10) & 0x1f);
    out[rgbaOffset++] = rgb === 0 ? 0 : 255;
  }
  return out;
}

function expand5(value: number): number {
  return (value << 3) | (value >> 2);
}

function normalizeIconColor(value: number): number {
  return Math.max(0, Math.min(1, value / 128));
}

function decodeSjis(bytes: Uint8Array): string {
  const clean = trimNull(bytes);
  try {
    return new TextDecoder('shift_jis').decode(clean).trim();
  } catch {
    return ascii(clean, 0, clean.length).trim();
  }
}

function asciiZ(bytes: Uint8Array, offset: number, length: number): string {
  const end = Math.min(offset + length, bytes.length);
  let cursor = offset;
  while (cursor < end && bytes[cursor] !== 0) {
    cursor++;
  }
  return ascii(bytes, offset, cursor - offset).trim();
}

function ascii(bytes: Uint8Array, offset: number, length: number): string {
  let value = '';
  for (let i = offset; i < offset + length && i < bytes.length; i++) {
    const b = bytes[i];
    if (b >= 0x20 && b <= 0x7e) {
      value += String.fromCharCode(b);
    }
  }
  return value;
}

function trimNull(bytes: Uint8Array): Uint8Array {
  let end = bytes.length;
  while (end > 0 && bytes[end - 1] === 0) {
    end--;
  }
  return bytes.slice(0, end);
}

function ensure(bytes: Uint8Array, offset: number, size: number, section: string): void {
  if (offset < 0 || offset + size > bytes.length) {
    throw new Error(`unexpected end of ${section}`);
  }
}
