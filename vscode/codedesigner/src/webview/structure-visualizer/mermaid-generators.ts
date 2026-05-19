// ───────────────────────────────────────────────────────────────────────────
// Pure helpers that turn CodeDesigner structure data into Mermaid diagram text.
// The webview renders the returned strings; nothing here touches the DOM.
// ───────────────────────────────────────────────────────────────────────────

/** A label entry, mirroring the LabelManagerEntry wire payload. */
export interface SvLabel {
    address:        string;
    parentAddress?: string;
    offset?:        number;
    name:           string;
    comment?:       string;
    valueType?:     string;
    type?:          string;
    argRegs?:       string[];
    returnRegs?:    string[];
    elementStride?: number;
    elementCount?:  number;
}

/** One member of a label group. */
export interface SvGroupMember {
    labelAddress: string;
    labelName:    string;
    labelType:    string;
    role?:        string;
    sortOrder:    number;
}

/** A label group ("objectmap"). */
export interface SvGroup {
    id:       number;
    name:     string;
    kind:     string;
    comment?: string;
    color?:   string;
    members:  SvGroupMember[];
}

/** One caller→callee edge. */
export interface SvCallEdge { from: string; to: string; kind: string; }

/** A call-graph subgraph around a focus address. */
export interface SvCallGraph {
    focus:     string;
    depth:     number;
    direction: string;
    nodes:     string[];
    edges:     SvCallEdge[];
}

// ── Sanitisers ─────────────────────────────────────────────────────────────

/** Normalise an address string to lowercase 8-hex without the 0x prefix. */
export function hexKey(addr: string): string {
    let t = addr.trim().toLowerCase();
    if (t.startsWith('0x')) { t = t.slice(2); }
    const colon = t.indexOf(':');
    if (colon >= 0) { t = t.slice(0, colon); }
    const n = parseInt(t, 16);
    return Number.isFinite(n) ? (n >>> 0).toString(16).padStart(8, '0') : t.replace(/[^0-9a-f]/g, '');
}

/** Keep only characters that are safe inside a Mermaid member / label. */
function safeText(s: string): string {
    return (s || '').replace(/[^A-Za-z0-9_ .+\-*]/g, '_').trim() || '_';
}

/** Strip the register= prefix from an argRegs entry, e.g. "a0=playerPtr" → "playerPtr". */
function argName(raw: string): string {
    const eq = raw.indexOf('=');
    return safeText(eq >= 0 ? raw.slice(eq + 1) : raw);
}

// ── Class / object diagram ─────────────────────────────────────────────────

/**
 * Build a classDiagram for a Struct label: the struct is the class, its child
 * labels are the fields (and any Function children become methods).
 */
export function classDiagramFromStruct(struct: SvLabel, children: SvLabel[]): string {
    const id   = 'c' + hexKey(struct.address);
    const name = safeText(struct.name) || ('Struct_' + hexKey(struct.address));
    const lines = ['classDiagram', `  class ${id}["${name}"]`];

    const sorted = [...children].sort((a, b) => (a.offset ?? 0) - (b.offset ?? 0));
    if (sorted.length === 0) {
        lines.push(`  ${id} : (no fields)`);
    }
    for (const c of sorted) {
        const off = `+0x${(c.offset ?? 0).toString(16).toUpperCase()}`;
        if (c.type === 'Function') {
            const args = (c.argRegs ?? []).map(argName).join(', ');
            lines.push(`  ${id} : ${off} ${safeText(c.name)}(${args})`);
        } else {
            const vt = c.valueType ? ' ' + safeText(c.valueType) : '';
            lines.push(`  ${id} : ${off} ${safeText(c.name)}${vt}`);
        }
    }
    return lines.join('\n');
}

/**
 * Build a classDiagram from a label group: Function members become methods,
 * everything else becomes a field. Members are ordered by their sortOrder.
 */
export function classDiagramFromGroup(group: SvGroup): string {
    const id   = 'g' + group.id;
    const name = safeText(group.name) || ('Group_' + group.id);
    const lines = ['classDiagram', `  class ${id}["${name}"]`];

    const members = [...group.members].sort((a, b) => a.sortOrder - b.sortOrder);
    if (members.length === 0) {
        lines.push(`  ${id} : (empty group)`);
    }
    for (const m of members) {
        const tag = m.role ? `+${safeText(m.role)} ` : '+';
        if (m.labelType === 'Function') {
            lines.push(`  ${id} : ${tag}${safeText(m.labelName)}()`);
        } else {
            lines.push(`  ${id} : ${tag}${safeText(m.labelName)}`);
        }
    }
    return lines.join('\n');
}

// ── VTable diagram ─────────────────────────────────────────────────────────

/**
 * Build a classDiagram for an ArrayOfPointers (vtable) label: each child slot
 * is a numbered method entry.
 */
export function vtableDiagramFromLabel(vt: SvLabel, children: SvLabel[]): string {
    const id     = 'v' + hexKey(vt.address);
    const name   = safeText(vt.name) || ('VTable_' + hexKey(vt.address));
    const stride = vt.elementStride && vt.elementStride > 0 ? vt.elementStride : 4;
    const lines  = ['classDiagram', `  class ${id}["${name}"]`];

    const sorted = [...children].sort((a, b) => (a.offset ?? 0) - (b.offset ?? 0));
    if (sorted.length === 0) {
        lines.push(`  ${id} : (no resolved slots)`);
    }
    for (const c of sorted) {
        const slot = Math.floor((c.offset ?? 0) / stride);
        lines.push(`  ${id} : +[${slot}] ${safeText(c.name)}()`);
    }
    return lines.join('\n');
}

/** Build a classDiagram for a vtable-kind label group (members are the slots). */
export function vtableDiagramFromGroup(group: SvGroup): string {
    const id   = 'g' + group.id;
    const name = safeText(group.name) || ('VTable_' + group.id);
    const lines = ['classDiagram', `  class ${id}["${name}"]`];

    const members = [...group.members].sort((a, b) => a.sortOrder - b.sortOrder);
    if (members.length === 0) {
        lines.push(`  ${id} : (empty vtable)`);
    }
    members.forEach((m, i) => {
        lines.push(`  ${id} : +[${m.sortOrder || i}] ${safeText(m.labelName)}()`);
    });
    return lines.join('\n');
}

// ── Call-graph flowchart ───────────────────────────────────────────────────

/**
 * Build a flowchart for a call-graph subgraph. The focus node is highlighted;
 * node ids are `n<hex>` so the webview can wire click-to-navigate.
 */
export function callGraphFlowchart(graph: SvCallGraph, nameOf: (addr: string) => string): string {
    const focusKey = hexKey(graph.focus);
    const lines = ['flowchart LR'];

    for (const node of graph.nodes) {
        const key   = hexKey(node);
        const label = `${safeText(nameOf(node))} 0x${key.toUpperCase()}`;
        lines.push(`  n${key}["${label}"]`);
    }
    if (graph.nodes.length === 0) {
        lines.push(`  n${focusKey}["${safeText(nameOf(graph.focus))} 0x${focusKey.toUpperCase()}"]`);
    }
    for (const e of graph.edges) {
        const arrow = e.kind === 'Direct' ? '-->' : '-.->';
        lines.push(`  n${hexKey(e.from)} ${arrow} n${hexKey(e.to)}`);
    }
    lines.push(`  class n${focusKey} focusNode`);
    lines.push('  classDef focusNode fill:#3a6ea5,stroke:#9cf,color:#fff,stroke-width:2px');
    return lines.join('\n');
}
