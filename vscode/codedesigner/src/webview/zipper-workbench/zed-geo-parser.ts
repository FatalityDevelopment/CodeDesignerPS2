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

// ---------------------------------------------------------------------------
// GEO scene composition (HTML map viewer parity). Walks the scene tree and
// emits one placement record per node that names a model (via `model_name` or
// implied by a `visuals` child). Each placement carries the resolved 4x4
// matrix from its `nparams` child so the caller can transform the model's
// vertices into world space.
// ---------------------------------------------------------------------------

export interface GeoInstance {
  // The cleaned name of the placed-into-the-scene model (e.g. "car_benz_black").
  modelName: string;
  // The name of the scene node itself (sometimes "{var} car_benz_black").
  instanceName: string;
  // Slash-joined breadcrumb for diagnostics (e.g. "models/worldmodel/children/objects/inst_42").
  path: string;
  // The 4x4 transform pulled from nparams. Column-major (matches HTML reference).
  matrix: number[];
  // Source GEO node for further inspection.
  node: GeoNode;
}

function safeFindChild(node: GeoNode, name: string): GeoNode | null {
  return node.children.find(c => c.name === name) ?? null;
}

function cleanInstanceName(value: string): string {
  let s = (value ?? '').trim();
  const brace = s.indexOf('{');
  if (brace >= 0) {
    const end = s.indexOf('}', brace + 1);
    if (end > brace) { s = s.slice(brace + 1, end).trim(); }
    else { s = s.slice(0, brace).trim(); }
  }
  return s;
}

function bytesToAscii(data: Uint8Array): string {
  let s = '';
  for (let i = 0; i < data.length; i++) {
    const b = data[i];
    if (b === 0) break;
    if (b >= 0x20 && b < 0x7f) { s += String.fromCharCode(b); }
  }
  return s.trim();
}

function identityMatrix(): number[] {
  return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
}

export function readGeoTransform(node: GeoNode): number[] {
  const nparams = safeFindChild(node, 'nparams');
  if (!nparams || !nparams.rawData || nparams.rawData.length < 0x40) { return identityMatrix(); }
  const data = nparams.rawData;
  const view = new DataView(data.buffer, data.byteOffset, data.byteLength);
  const f: number[] = [];
  const max = Math.min(16, Math.floor(data.length / 4));
  for (let i = 0; i < max; i++) { f.push(view.getFloat32(i * 4, true)); }
  while (f.length < 16) { f.push(0); }
  // Identity rows store 1 at f[0]; if not we treat as junk and fall back to
  // identity. Mirrors the HTML guard.
  if (!Number.isFinite(f[0]) || Math.abs(f[0]) < 1e-6) { return identityMatrix(); }
  return f;
}

// Column-major matrix-vector multiply: row[k] = sum(m[k + col*4] * v[col]).
export function transformPoint(m: number[], x: number, y: number, z: number): [number, number, number] {
  return [
    x * m[0] + y * m[4] + z * m[8] + m[12],
    x * m[1] + y * m[5] + z * m[9] + m[13],
    x * m[2] + y * m[6] + z * m[10] + m[14],
  ];
}

function formatGeoPath(node: GeoNode, root: GeoNode): string {
  // Walk from `node` up to `root` via parent links? The flat tree doesn't
  // carry parent refs; do a DFS-with-trail instead.
  const trail: string[] = [];
  const walk = (n: GeoNode, acc: string[]): boolean => {
    acc.push(n.name);
    if (n === node) { trail.push(...acc); return true; }
    for (const c of n.children) {
      if (walk(c, acc)) { return true; }
    }
    acc.pop();
    return false;
  };
  walk(root, []);
  return trail.join('/');
}

// Walks the scene tree for nodes that look like placed models — they either
// carry a `model_name` child (explicit) or a `visuals` child (implicit, name
// comes from the node itself). Skips the sky shell because it usually
// dominates bounds and hides the actual playable area.
export function collectGeoInstances(scene: GeoScene, root: GeoNode): GeoInstance[] {
  const out: GeoInstance[] = [];
  const walk = (node: GeoNode) => {
    const explicitChild = safeFindChild(node, 'model_name');
    const explicitName = explicitChild?.rawData ? bytesToAscii(explicitChild.rawData) : '';
    const hasVisuals = safeFindChild(node, 'visuals') !== null;
    const cleanNode = cleanInstanceName(node.name);
    const candidate = explicitName || (hasVisuals ? cleanNode : '');

    if (candidate && cleanNode.toLowerCase() !== 'sky' && candidate.toLowerCase() !== 'sky') {
      out.push({
        modelName: candidate,
        instanceName: cleanNode,
        path: formatGeoPath(node, scene.root),
        matrix: readGeoTransform(node),
        node,
      });
    }

    for (const c of node.children) { walk(c); }
  };
  walk(root);

  // Dedup by path+modelName so a model placed in multiple equivalent ways
  // doesn't get rendered twice.
  const seen = new Set<string>();
  return out.filter(inst => {
    const key = `${inst.path}|${inst.modelName.toLowerCase()}`;
    if (seen.has(key)) { return false; }
    seen.add(key);
    return true;
  });
}

// Loose name matching for resolving an instance's `modelName` to an actual
// model entry across every loaded *_MDL.ZED. Mirrors the HTML's findModelFor
// Instance + preferModelHit cascade.
export function compactName(value: string): string {
  let out = '';
  const s = String(value).toLowerCase();
  for (let i = 0; i < s.length; i++) {
    const c = s.charCodeAt(i);
    if ((c >= 48 && c <= 57) || (c >= 97 && c <= 122)) { out += s[i]; }
  }
  return out;
}

// Strip LOD / index suffixes from model names so a placement that asks for
// "car_benz_01" can resolve to a node called "car_benz". Matches the new
// HTML map viewer's stripModelSuffix.
export function stripModelSuffix(name: string): string {
  let s = String(name ?? '').trim();
  s = s.replace(/_LO$/i, '');
  s = s.replace(/_[0-9]+$/i, '');
  return s;
}


// Known cross-convention aliases seen in weapon/map packages where the GEO
// instance name (iw*/ra*/etc) differs from the MDL node naming family.
const MODEL_NAME_ALIASES: Record<string, string[]> = {
  // M51 packages often place iw80a2 instances that resolve to m16a2 meshes.
  iw80a2: ['m16a2', 'm16', 'mesh_m16a2', 'mesh_m16'],
  mesh_iw80a2: ['m16a2', 'm16', 'mesh_m16a2', 'mesh_m16'],
};

// Generate every name variant we should try when matching a GEO instance
// name to an MDL entry. Includes:
//   1. lowercased original
//   2. lowercased cleaned (braces stripped)
//   3. compact (alphanumerics only)
//   4. stripped suffix variants of the above
//   5. MESH_-prefixed variants (CLIB convention)
export function instanceNameVariants(rawName: string): string[] {
  const clean = cleanInstanceName(rawName);
  const variants = new Set<string>();
  const addAll = (base: string) => {
    const lower = base.toLowerCase();
    if (!lower) { return; }
    variants.add(lower);
    variants.add(compactName(lower));
    const stripped = stripModelSuffix(lower);
    variants.add(stripped);
    variants.add(compactName(stripped));
    variants.add('mesh_' + lower);
    variants.add('mesh_' + stripped);
    variants.add(compactName('mesh' + lower));
    variants.add(compactName('mesh' + stripped));
  };
  addAll(rawName);
  addAll(clean);

  const aliasKeys = [String(rawName ?? "").toLowerCase(), String(clean ?? "").toLowerCase()];
  for (const k of aliasKeys) {
    const aliases = MODEL_NAME_ALIASES[k];
    if (!aliases) { continue; }
    for (const a of aliases) { addAll(a); }
  }

  variants.delete('');
  return [...variants];
}
