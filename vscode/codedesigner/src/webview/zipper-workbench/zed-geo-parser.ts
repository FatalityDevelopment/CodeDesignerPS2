// Parser for Zipper *_GEO.ZED scene-graph files.
//
// Format observation: every ZedNode's `type` field is the count of immediate
// children that follow it in the flat entry list. A pre-order walk that
// consumes `type` children after each parent reconstructs the tree exactly.
//
// Typical node names:
//   models           container at the top
//   <object_name>    a scene node (e.g. car_benz_black, m4Acarbine_203)
//   nparams          96-byte payload = 4x4 row-major matrix + extras
//   children         group holding sub-objects
//   visuals          group holding visual instances
//   vis              a single visual instance
//   vparams          24-byte payload = visual params (mesh ref + uv data)

import { base64ToBytes } from './zed-texture-parser';

export interface NodeParams {
  matrix: number[]; // 16 floats, row-major
  translation: [number, number, number];
  extra: Float32Array; // remaining floats after the matrix
}

export interface VisualParams {
  rawBytes: Uint8Array;
}

export interface GeoNode {
  name: string;
  type: number;
  index: number; // index in original entries
  size: number;
  rawData: Uint8Array | null;
  children: GeoNode[];
  // Parsed convenience fields when applicable.
  nparams?: NodeParams;
  vparams?: VisualParams;
}

export interface GeoEntry {
  index: number;
  name: string;
  type: number;
  data: string; // base64
}

export interface GeoScene {
  root: GeoNode;
  byName: Map<string, GeoNode[]>; // names can repeat (e.g. "nparams")
}

function parseNodeParams(bytes: Uint8Array): NodeParams | undefined {
  if (bytes.length < 64) { return undefined; }
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const matrix: number[] = [];
  for (let i = 0; i < 16; i++) {
    matrix.push(view.getFloat32(i * 4, true));
  }
  // Row-major: translation is in the last row (m30, m31, m32) for SOCOM's
  // convention based on inspection (BENZ identity matrix has -20, 0, -40 at
  // +0x40 in a separate vec4, but +0x30..+0x3C is 0,0,0,1 = identity row).
  // We expose translation from row 3 if it differs from identity; otherwise
  // callers can read .extra.
  const translation: [number, number, number] = [matrix[12], matrix[13], matrix[14]];
  const extraFloats = Math.floor((bytes.length - 64) / 4);
  const extra = new Float32Array(extraFloats);
  for (let i = 0; i < extraFloats; i++) {
    extra[i] = view.getFloat32(64 + i * 4, true);
  }
  return { matrix, translation, extra };
}

function parseVisualParams(bytes: Uint8Array): VisualParams {
  return { rawBytes: bytes };
}

// Walk the flat entry list using `type` as the child count for the next N
// siblings. Returns a single synthetic root that owns all top-level entries.
export function parseGeoScene(entries: GeoEntry[]): GeoScene {
  const decoded: Array<{ entry: GeoEntry; data: Uint8Array | null }> = entries.map(e => ({
    entry: e,
    data: e.data ? base64ToBytes(e.data) : null,
  }));

  const byName = new Map<string, GeoNode[]>();

  const buildNode = (cursor: { i: number }): GeoNode | null => {
    if (cursor.i >= decoded.length) { return null; }
    const { entry, data } = decoded[cursor.i];
    const node: GeoNode = {
      name: entry.name,
      type: entry.type,
      index: entry.index,
      size: data ? data.length : 0,
      rawData: data,
      children: [],
    };
    cursor.i++;

    if (data && data.length >= 64 && entry.name === 'nparams') {
      node.nparams = parseNodeParams(data);
    }
    if (data && entry.name === 'vparams') {
      node.vparams = parseVisualParams(data);
    }

    const childCount = entry.type;
    for (let k = 0; k < childCount && cursor.i < decoded.length; k++) {
      const child = buildNode(cursor);
      if (child) { node.children.push(child); }
    }

    const list = byName.get(node.name);
    if (list) { list.push(node); } else { byName.set(node.name, [node]); }
    return node;
  };

  const root: GeoNode = {
    name: '<root>',
    type: 0,
    index: -1,
    size: 0,
    rawData: null,
    children: [],
  };
  const cursor = { i: 0 };
  while (cursor.i < decoded.length) {
    const node = buildNode(cursor);
    if (node) { root.children.push(node); }
  }
  return { root, byName };
}

// Find a named scene node that matches a given model entry name. Looks for the
// node within the 'models' container, falling back to any top-level match.
export function findSceneNode(scene: GeoScene, modelName: string): GeoNode | null {
  const targets = scene.byName.get(modelName);
  if (!targets || targets.length === 0) { return null; }
  // Prefer one that has a 'visuals' or 'children' descendant.
  for (const node of targets) {
    if (hasMeshData(node)) { return node; }
  }
  return targets[0];
}

function hasMeshData(node: GeoNode): boolean {
  for (const c of node.children) {
    if (c.name === 'visuals' || c.name === 'children') { return true; }
    if (hasMeshData(c)) { return true; }
  }
  return false;
}

// Produce a flat list of named "parts" (leaf visual nodes) for chunk labelling.
// For weapons this yields ['m4_high', 'm4_low', 'firepoint', ...] in encounter
// order, which is roughly the order chunks appear in the MDL payload.
export function collectVisualPartNames(node: GeoNode | null): string[] {
  if (!node) { return []; }
  const names: string[] = [];
  const walk = (n: GeoNode) => {
    // A "part" is a named scene node (type >= 1, has nparams) that ultimately
    // owns one or more `vis` entries.
    const isPart = n.children.some(c => c.name === 'nparams' || c.name === 'visuals');
    if (isPart && n.name !== node.name) {
      names.push(n.name);
    }
    for (const c of n.children) { walk(c); }
  };
  for (const c of node.children) { walk(c); }
  return names;
}
