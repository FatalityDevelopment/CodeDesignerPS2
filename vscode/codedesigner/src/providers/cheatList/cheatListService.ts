import * as vscode from 'vscode';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { SocketClient, LabelEntry, CheatEngineInfo } from '../../socketClient';

/**
 * One cheat on the Cheat List page. `source` is CDS: raw code pairs, assembly,
 * freeze/cond/timer. Cheats come from (and are saved to) a cheat archive list —
 * the lists the Cheats editor edits.
 */
export interface CheatListItem {
    id:       string;
    name:     string;
    enabled:  boolean;
    source:   string;
    notes?:   string;
    author?:  string;
    /** Section (folder) in the cheat list; "Others" when unset. */
    section?: string;
}

/**
 * Per-game Cheat List settings kept by the extension. The cheats themselves live
 * in the cheat archive (database); this holds what the archive has no place for.
 */
export interface CheatListDocument {
    /** Hook address for the cheat engine (an instruction pair that runs every frame). */
    hookAddress?: number;
    /** Where the engine image goes; the bridge defaults to 0x000C0000. */
    engineBase?:  number;
    /** The cheat archive list chosen for this game (Cheat List page and sidebar Cheats tab). */
    archiveCheatListId?: number;
    /** Per cheat-list id: keys ({@link cheatKey}) of the cheats ticked on the Cheat List page. */
    enabled?: Record<string, string[]>;
    /** Per cheat-list id: CDS source of cheats written in CDS (the list stores their compiled codes). */
    sources?: Record<string, Record<string, string>>;
}

const DEFAULT_SECTION = 'Others';

/** Identity of a cheat within its list, for the enabled/source overlays. */
export function cheatKey(item: { name: string; section?: string }): string {
    return `${item.section || DEFAULT_SECTION}\u0001${item.name}`;
}

const RAW_LINE = /^[0-9A-Fa-f?*]+(\s+[0-9A-Fa-f?*]+)*$/;
const COMMENT_LINE = /^\s*(\/\/|#|;)/;

/** The code lines of a source made only of raw codes (and comments), or null when it has CDS. */
export function rawCodesOf(source: string): string[] | null {
    const lines = source.split(/\r?\n/).map(l => l.trim()).filter(l => l && !COMMENT_LINE.test(l));
    return lines.every(l => RAW_LINE.test(l)) ? lines.map(l => l.toUpperCase()) : null;
}

/**
 * Cheats editor text for a list (mirrors CheatFileFormat.Serialize in
 * CodeDesigner.Ps2.CheatEngine): sections, [Label], Description/Author comments, codes.
 */
export function serializeCheatList(cheats: Array<{ name: string; section?: string; notes?: string; author?: string; codes: string[] }>): string {
    const out: string[] = [];
    let current: string | null = null;
    for (const c of cheats) {
        const section = c.section?.trim() || DEFAULT_SECTION;
        if (section !== current) {
            if (current !== null) { out.push(''); }
            out.push(`[--SectionStart:${section}--]`, '');
            current = section;
        }
        out.push(`[${c.name}]`);
        for (const line of (c.notes ?? '').split(/\r?\n/).filter(l => l.trim())) { out.push(`// Description: ${line.trim()}`); }
        for (const line of (c.author ?? '').split(/\r?\n/).filter(l => l.trim())) { out.push(`// Author: ${line.trim()}`); }
        out.push(...c.codes, '');
    }
    return out.join('\n').trimEnd() + '\n';
}

export interface CompiledCheat {
    id:     string;
    name:   string;
    codes:  string[];
    errors: Array<{ line: number; message: string }>;
}

export interface CompiledList {
    cheats:     CompiledCheat[];
    /** Enabled cheats as cheat-list text: name line, code lines, blank line. */
    text:       string;
    codeCount:  number;
    errorCount: number;
}

export type SendMode = 'once' | 'engine';
export type BinaryKind = 'raw' | 'engine';

const CODE_LINE = /^\s*([0-9a-f]{8})\s+([0-9a-f]{8})/i;
export const DEFAULT_ENGINE_BASE = 0x000C0000;

/**
 * Host-side cheat list operations shared by the sidebar Cheats tab and the
 * Cheat List page: compile CDS cheats to raw codes (COMPILECDS), send them once
 * (APPLYCHEATS) or through the persistent cheat engine (INJECT/UPDATECHEATENGINE),
 * and save them as a raw code-pair .bin or an engine image .bin.
 */
export class CheatListService {
    constructor(
        private readonly socketClient: SocketClient,
        private readonly getPointerLabels: () => Promise<LabelEntry[]>,
    ) {}

    // ── Compile ─────────────────────────────────────────────────────────────

    async compile(items: CheatListItem[]): Promise<CompiledList> {
        this._requireConnection();
        let labels: LabelEntry[] = [];
        try { labels = await this.getPointerLabels(); } catch { /* not connected / no pointer manager */ }

        const cheats: CompiledCheat[] = [];
        for (const item of items) {
            if (!item.enabled || !item.source.trim()) { continue; }
            const { results } = await this.socketClient.compileCds(item.source, labels.length > 0 ? labels : undefined);
            const compiled: CompiledCheat = { id: item.id, name: item.name, codes: [], errors: [] };
            for (const r of results) {
                if (r.Success && r.Hex) {
                    compiled.codes.push(r.Hex);
                } else if (!r.Success && r.Error) {
                    compiled.errors.push({ line: r.LineNumber ?? 0, message: r.Error });
                }
            }
            cheats.push(compiled);
        }

        const text = cheats
            .filter(c => c.errors.length === 0 && c.codes.length > 0)
            .map(c => [c.name || 'Cheat', ...c.codes].join('\n'))
            .join('\n\n');
        return {
            cheats,
            text,
            codeCount:  cheats.reduce((n, c) => n + c.codes.length, 0),
            errorCount: cheats.reduce((n, c) => n + c.errors.length, 0),
        };
    }

    /**
     * Save the page's cheats into a cheat archive list (replacing its contents, like the
     * Cheats editor's Save). Raw-code cheats are stored as-is; a CDS cheat stores its
     * compiled codes, and its source is returned for the caller to keep (by {@link cheatKey}).
     */
    async saveToList(cheatListId: number, items: CheatListItem[]): Promise<{ message: string; sources: Record<string, string> }> {
        this._requireConnection();
        let labels: LabelEntry[] = [];
        try { labels = await this.getPointerLabels(); } catch { /* not connected / no pointer manager */ }

        const cheats: Array<{ name: string; section?: string; notes?: string; author?: string; codes: string[] }> = [];
        const sources: Record<string, string> = {};
        const problems: string[] = [];
        for (const item of items) {
            const name = (item.name || 'Untitled').replace(/[\r\n]+/g, ' ').trim();
            let codes = rawCodesOf(item.source);
            if (!codes) {
                const { results } = await this.socketClient.compileCds(item.source, labels.length > 0 ? labels : undefined);
                const errors = results.filter(r => !r.Success && r.Error);
                if (errors.length > 0) {
                    problems.push(`${name}: line ${errors[0].LineNumber ?? 0} ${errors[0].Error}`);
                    continue;
                }
                codes = results.filter(r => r.Success && r.Hex).map(r => r.Hex!);
                sources[cheatKey({ name, section: item.section })] = item.source;
            }
            if (codes.length === 0) {
                problems.push(`${name}: no codes`);
                continue;
            }
            cheats.push({ name, section: item.section, notes: item.notes, author: item.author, codes });
        }
        if (problems.length > 0) {
            throw new Error(`Not saved — fix these first: ${problems.join('; ')}`);
        }
        const message = await this.socketClient.cheatEditorSave(cheatListId, serializeCheatList(cheats));
        return { message, sources };
    }

    // ── Send ────────────────────────────────────────────────────────────────

    /** Apply the compiled list once, or through the engine (updating the table in place when already hooked there). */
    async send(compiled: CompiledList, mode: SendMode, doc: CheatListDocument): Promise<string> {
        this._requireConnection();
        if (compiled.errorCount > 0) { throw new Error(`Fix ${compiled.errorCount} compile error(s) first.`); }
        if (!compiled.text) { throw new Error('No enabled cheats with codes to send.'); }

        if (mode === 'once') {
            await this.socketClient.applyCheats(compiled.text);
            return `Wrote ${compiled.codeCount} code(s) once.`;
        }

        if (doc.hookAddress === undefined) { throw new Error('Set a hook address for the cheat engine first.'); }
        const base = doc.engineBase ?? DEFAULT_ENGINE_BASE;
        const status = await this.socketClient.cheatEngineStatus();
        const info = status.injected && status.hookAddress === doc.hookAddress && status.engineBase === base
            ? await this.socketClient.updateCheatEngine(compiled.text)
            : await this.socketClient.injectCheatEngine(compiled.text, doc.hookAddress, base);
        return describeEngine(info, status.injected && status.hookAddress === doc.hookAddress ? 'Engine table updated' : 'Engine hooked');
    }

    async removeEngine(): Promise<CheatEngineInfo> {
        this._requireConnection();
        return this.socketClient.removeCheatEngine();
    }

    async engineStatus(): Promise<CheatEngineInfo | null> {
        if (!this.socketClient.isConnected()) { return null; }
        try { return await this.socketClient.cheatEngineStatus(); } catch { return null; }
    }

    // ── Save as binary ──────────────────────────────────────────────────────

    /**
     * Save the compiled list. `raw` writes 8-byte little-endian (code, value) pairs per
     * line; `engine` writes the cheat-engine image to load at the engine base.
     * Returns a status message, or undefined when the user cancelled.
     */
    async saveBinary(compiled: CompiledList, kind: BinaryKind, doc: CheatListDocument, gameName: string): Promise<string | undefined> {
        if (compiled.errorCount > 0) { throw new Error(`Fix ${compiled.errorCount} compile error(s) first.`); }
        if (!compiled.text) { throw new Error('No enabled cheats with codes to save.'); }

        const stem = (gameName || 'cheats').replace(/[^A-Za-z0-9._-]+/g, '_');
        let bytes: Buffer;
        let fileName: string;
        let summary: string;
        if (kind === 'raw') {
            bytes = rawCodePairs(compiled.text);
            fileName = `${stem}.codes.bin`;
            summary = `${bytes.length / 8} code pair(s)`;
        } else {
            this._requireConnection();
            const base = doc.engineBase ?? DEFAULT_ENGINE_BASE;
            const { image, info } = await this.socketClient.buildCheatEngine(compiled.text, doc.hookAddress, base);
            bytes = image;
            fileName = `${stem}.engine-${hex8(base)}.bin`;
            const hook = info.hookAddress !== undefined
                ? ` Hook: write ${hex8(info.hookWord ?? 0)} 00000000 at ${hex8(info.hookAddress)}.`
                : '';
            summary = `engine image, ${info.entries} row(s), load at ${hex8(base)}.${hook}` +
                (info.warnings.length > 0 ? ` ${info.warnings.join(' ')}` : '');
        }

        const defaultDir = vscode.workspace.workspaceFolders?.[0]?.uri;
        const target = await vscode.window.showSaveDialog({
            title: kind === 'raw' ? 'Save cheat codes as binary' : 'Save cheat engine image',
            defaultUri: defaultDir ? vscode.Uri.joinPath(defaultDir, fileName) : undefined,
            filters: { 'Binary': ['bin'], 'All Files': ['*'] },
        });
        if (!target) { return undefined; }
        fs.writeFileSync(target.fsPath, bytes);
        return `Saved ${path.basename(target.fsPath)} (${bytes.length} bytes): ${summary}`;
    }

    private _requireConnection(): void {
        if (!this.socketClient.isConnected()) { throw new Error('Not connected to the CodeDesigner bridge.'); }
    }
}

/** Little-endian (code, value) word pairs for every code line in `text`. */
export function rawCodePairs(text: string): Buffer {
    const pairs: number[][] = [];
    for (const line of text.split(/\r?\n/)) {
        const m = CODE_LINE.exec(line);
        if (m) { pairs.push([parseInt(m[1], 16), parseInt(m[2], 16)]); }
    }
    const buf = Buffer.alloc(pairs.length * 8);
    pairs.forEach(([a, b], i) => {
        buf.writeUInt32LE(a >>> 0, i * 8);
        buf.writeUInt32LE(b >>> 0, i * 8 + 4);
    });
    return buf;
}

/**
 * Split cheat-list text into cheats. A cheat starts at a `#Name` or `[Name]` header,
 * or at a plain text line directly above code lines (the archive's paste format,
 * whose bare name is not valid CDS); everything until the next header is its source.
 */
export function parseCheatText(text: string): Array<{ name: string; source: string }> {
    type Draft = { name: string; source: string[] };
    const lines = text.split(/\r?\n/);
    const cheats: Draft[] = [];
    const start = (name: string): Draft => {
        const draft: Draft = { name, source: [] };
        cheats.push(draft);
        return draft;
    };
    let current: Draft | null = null;

    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const t = line.trim();
        const header = /^#\s*(.+)$/.exec(t) ?? /^\[(.+)\]$/.exec(t);
        const prevBlank = i === 0 || lines[i - 1].trim() === '';
        const bareName = !header && t !== '' && !CODE_LINE.test(t) && prevBlank
            && i + 1 < lines.length && CODE_LINE.test(lines[i + 1]);
        if (header && (prevBlank || current === null)) {
            current = start(header[1].trim());
            continue;
        }
        if (bareName) { current = start(t); continue; }
        if (!current) {
            if (t === '') { continue; }
            current = start('Cheat');
        }
        current.source.push(line);
    }
    return cheats
        .map(c => ({ name: c.name, source: c.source.join('\n').replace(/^\n+|\s+$/g, '') }))
        .filter(c => c.source !== '' || c.name !== 'Cheat');
}

export function newCheatId(): string {
    return Math.random().toString(36).slice(2, 10) + Date.now().toString(36);
}

export function hex8(n: number): string {
    return '0x' + (n >>> 0).toString(16).toUpperCase().padStart(8, '0');
}

function describeEngine(info: CheatEngineInfo, verb: string): string {
    const where = info.hookAddress !== undefined ? ` at ${hex8(info.hookAddress)}` : '';
    const warn = info.warnings.length > 0 ? ` (${info.warnings.join(' ')})` : '';
    return `${verb}${where}: ${info.entries} row(s) re-applied every frame from ${hex8(info.engineBase)}.${warn}`;
}

/**
 * Per-game Cheat List settings stored as JSON under the extension's global storage
 * (`cheat-lists/<game>.json`), shared by the Cheat List page and the sidebar.
 */
export class CheatListStore {
    constructor(private readonly getStorageDir: () => string) {}

    load(gameKey: string): CheatListDocument {
        try {
            const doc = JSON.parse(fs.readFileSync(this._file(gameKey), 'utf8')) as CheatListDocument;
            return {
                hookAddress:        doc.hookAddress,
                engineBase:         doc.engineBase,
                archiveCheatListId: doc.archiveCheatListId,
                enabled:            doc.enabled ?? {},
                sources:            doc.sources ?? {},
            };
        } catch {
            return { enabled: {}, sources: {} };
        }
    }

    /** Merge `patch` into the game's saved settings. */
    update(gameKey: string, patch: Partial<CheatListDocument>): CheatListDocument {
        const doc = { ...this.load(gameKey), ...patch };
        const file = this._file(gameKey);
        fs.mkdirSync(path.dirname(file), { recursive: true });
        fs.writeFileSync(file, JSON.stringify(doc, null, 2), 'utf8');
        return doc;
    }

    private _file(gameKey: string): string {
        const safe = (gameKey || 'no-game').replace(/[^A-Za-z0-9._-]+/g, '_');
        return path.join(this.getStorageDir() || path.join(os.tmpdir(), 'codedesigner'), 'cheat-lists', `${safe}.json`);
    }
}
