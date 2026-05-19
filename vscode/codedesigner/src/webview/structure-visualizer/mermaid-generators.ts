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
export interface SvCallEdge {
    from:       string;
    to:         string;
    kind:       string;
    argRegs?:    string[];
    returnRegs?: string[];
}

/** Resolved metadata for one call-graph node. */
export interface SvCallNodeInfo {
    address:    string;
    label?:     string;
    argRegs?:    string[];
    returnRegs?: string[];
}

/** A call-graph subgraph around a focus address. */
export interface SvCallGraph {
    focus:     string;
    depth:     number;
    direction: string;
    nodes:     string[];
    nodeInfo?: SvCallNodeInfo[];
    edges:     SvCallEdge[];
}

// ── Sanitisers ─────────────────────────────────────────────────────────────

/** Normalise an address string to lowercase 8-hex without the 0x prefix. */
export function hexKey(addr: string): string {
    if (typeof addr !== 'string') { return ''; }
    let t = addr.trim().toLowerCase();
    if (t.startsWith('0x')) { t = t.slice(2); }
    const colon = t.indexOf(':');
    if (colon >= 0) { t = t.slice(0, colon); }
    const n = parseInt(t, 16);
    if (!Number.isFinite(n)) { return t.replace(/[^0-9a-f]/g, ''); }
    const v = n >>> 0;
    const masked = v & 0x1FFFFFFF;
    const canonical = v >= 0x02000000 && masked < 0x02000000 ? masked : v;
    return canonical.toString(16).padStart(8, '0');
}

/** Keep only characters that are safe inside a Mermaid member / label. */
function safeText(s: string): string {
    return (s || '').replace(/[^A-Za-z0-9_ .+\-*]/g, '_').trim() || '_';
}

/** Flowchart labels can carry richer signatures, but keep Mermaid delimiters out. */
function safeFlowText(s: string): string {
    return (s || '').replace(/[|"[\]{}<>]/g, '_').replace(/\s+/g, ' ').trim();
}

/** Mermaid class members are parser-sensitive; keep a conservative subset. */
function safeMemberText(s: string): string {
    const t = safeText(s).replace(/[+\-#~]/g, '_');
    return t || '_';
}

/** Strip the register= prefix from an argRegs entry, e.g. "a0=playerPtr" → "playerPtr". */
function argName(raw: string): string {
    const eq = raw.indexOf('=');
    return safeText(eq >= 0 ? raw.slice(eq + 1) : raw);
}

/** Mermaid classDiagram uses bracket labels, not flowchart-style `as` aliases. */
function classDeclaration(id: string, label: string): string {
    return `  class ${id}[${JSON.stringify(label)}]`;
}

// ── Class / object diagram ─────────────────────────────────────────────────

/**
 * Build a classDiagram for a Struct label: the struct is the class, its child
 * labels are the fields (and any Function children become methods).
 */
export function classDiagramFromStruct(struct: SvLabel, children: SvLabel[]): string {
    const id   = 'c' + hexKey(struct.address);
    const name = safeText(struct.name) || ('Struct_' + hexKey(struct.address));
    const lines = ['classDiagram', classDeclaration(id, name)];

    const sorted = [...children].sort((a, b) => (a.offset ?? 0) - (b.offset ?? 0));
    if (sorted.length === 0) {
        lines.push(`  ${id} : (no fields)`);
    }
    for (const c of sorted) {
        const off = `off_0x${(c.offset ?? 0).toString(16).toUpperCase()}`;
        if (c.type === 'Function') {
            const args = (c.argRegs ?? []).map(argName).join(', ');
            lines.push(`  ${id} : ${safeMemberText(`${off} ${safeText(c.name)}(${args})`)}`);
        } else {
            const vt = c.valueType ? ' ' + safeText(c.valueType) : '';
            lines.push(`  ${id} : ${safeMemberText(`${off} ${safeText(c.name)}${vt}`)}`);
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
    const lines = ['classDiagram', classDeclaration(id, name)];

    const members = [...group.members].sort((a, b) => a.sortOrder - b.sortOrder);
    if (members.length === 0) {
        lines.push(`  ${id} : (empty group)`);
    }
    for (const m of members) {
        const tag = m.role ? `${safeText(m.role)} ` : '';
        if (m.labelType === 'Function') {
            lines.push(`  ${id} : ${safeMemberText(`${tag}${safeText(m.labelName)}()` )}`);
        } else {
            lines.push(`  ${id} : ${safeMemberText(`${tag}${safeText(m.labelName)}`)}`);
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
    const lines  = ['classDiagram', classDeclaration(id, name)];

    const sorted = [...children].sort((a, b) => (a.offset ?? 0) - (b.offset ?? 0));
    if (sorted.length === 0) {
        lines.push(`  ${id} : (no resolved slots)`);
    }
    for (const c of sorted) {
        const slot = Math.floor((c.offset ?? 0) / stride);
        lines.push(`  ${id} : ${safeMemberText(`slot_${slot} ${safeText(c.name)}()` )}`);
    }
    return lines.join('\n');
}

/** Build a classDiagram for a vtable-kind label group (members are the slots). */
export function vtableDiagramFromGroup(group: SvGroup): string {
    const id   = 'g' + group.id;
    const name = safeText(group.name) || ('VTable_' + group.id);
    const lines = ['classDiagram', classDeclaration(id, name)];

    const members = [...group.members].sort((a, b) => a.sortOrder - b.sortOrder);
    if (members.length === 0) {
        lines.push(`  ${id} : (empty vtable)`);
    }
    members.forEach((m, i) => {
        lines.push(`  ${id} : ${safeMemberText(`slot_${m.sortOrder || i} ${safeText(m.labelName)}()` )}`);
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
    const nodeInfo = new Map<string, SvCallNodeInfo>();
    for (const info of graph.nodeInfo ?? []) {
        const key = hexKey(info.address);
        if (key) { nodeInfo.set(key, info); }
    }

    for (const node of (graph.nodes ?? []).filter((n): n is string => typeof n === 'string' && !!hexKey(n))) {
        const key   = hexKey(node);
        const info  = nodeInfo.get(key);
        const name  = safeFlowText(info?.label || nameOf(node) || `sub_${key.toUpperCase()}`);
        const args  = signaturePart('args', info?.argRegs);
        const ret   = signaturePart('ret', info?.returnRegs);
        const label = [name, `0x${key.toUpperCase()}`, args, ret].filter(Boolean).join('<br/>');
        lines.push(`  n${key}["${label}"]`);
    }
    if (lines.length === 1 && focusKey) {
        lines.push(`  n${focusKey}["${safeFlowText(nameOf(graph.focus) || `sub_${focusKey.toUpperCase()}`)}<br/>0x${focusKey.toUpperCase()}"]`);
    }
    for (const e of (graph.edges ?? []).filter(e => !!hexKey(e?.from ?? '') && !!hexKey(e?.to ?? ''))) {
        const arrow = e.kind === 'Direct' ? '-->' : '-.->';
        const annotation = edgeAnnotation(e);
        const label = annotation ? `|${annotation}|` : '';
        lines.push(`  n${hexKey(e.from)} ${arrow}${label} n${hexKey(e.to)}`);
    }
    if (focusKey) {
        lines.push(`  class n${focusKey} focusNode`);
        lines.push('  classDef focusNode fill:#3a6ea5,stroke:#9cf,color:#fff,stroke-width:2px');
    }
    return lines.join('\n');
}

function signaturePart(prefix: string, regs: string[] | undefined): string {
    const values = (regs ?? []).map(safeFlowText).filter(Boolean);
    return values.length ? `${prefix}: ${values.join(', ')}` : '';
}

function edgeAnnotation(edge: SvCallEdge): string {
    const args = signaturePart('args', edge.argRegs);
    const ret  = signaturePart('ret', edge.returnRegs);
    return safeFlowText([args, ret].filter(Boolean).join('; '));
}
