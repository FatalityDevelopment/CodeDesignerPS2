// ═══════════════════════════════════════════════════════════════════════════
// PS2 save helpers: .psu (EMS / uLaunchELF single-file saves) and save diffs
// ═══════════════════════════════════════════════════════════════════════════
//
// A .psu is a sequence of 512-byte entry headers: the save folder, "." and
// "..", then one header per file followed by its data padded to 1 KB.

export interface SaveFile {
    name: string;
    data: Buffer;
}

const HEADER_SIZE = 512;
const MODE_DIR  = 0x8427;
const MODE_FILE = 0x8497;

/** PS2 timestamps are stored in JST (UTC+9). */
function writeTime(buf: Buffer, offset: number, date: Date): void {
    const jst = new Date(date.getTime() + 9 * 3600_000);
    buf[offset] = 0;
    buf[offset + 1] = jst.getUTCSeconds();
    buf[offset + 2] = jst.getUTCMinutes();
    buf[offset + 3] = jst.getUTCHours();
    buf[offset + 4] = jst.getUTCDate();
    buf[offset + 5] = jst.getUTCMonth() + 1;
    buf.writeUInt16LE(jst.getUTCFullYear(), offset + 6);
}

function header(mode: number, size: number, name: string, date: Date): Buffer {
    const h = Buffer.alloc(HEADER_SIZE);
    h.writeUInt16LE(mode, 0);
    h.writeUInt32LE(size >>> 0, 4);
    writeTime(h, 0x08, date);
    writeTime(h, 0x18, date);
    h.write(name, 0x40, Math.min(Buffer.byteLength(name, 'latin1'), 447), 'latin1');
    return h;
}

/** Builds a .psu from a save folder name and its files. */
export function buildPsu(saveName: string, files: SaveFile[], date = new Date()): Buffer {
    const parts: Buffer[] = [
        header(MODE_DIR, files.length + 2, saveName, date),
        header(MODE_DIR, 0, '.', date),
        header(MODE_DIR, 0, '..', date),
    ];
    for (const f of files) {
        parts.push(header(MODE_FILE, f.data.length, f.name, date));
        parts.push(f.data);
        const pad = (1024 - (f.data.length % 1024)) % 1024;
        if (pad) { parts.push(Buffer.alloc(pad)); }
    }
    return Buffer.concat(parts);
}

function readName(buf: Buffer, offset: number): string {
    const raw = buf.subarray(offset + 0x40, offset + HEADER_SIZE);
    const end = raw.indexOf(0);
    return raw.subarray(0, end < 0 ? raw.length : end).toString('latin1');
}

/** Parses a .psu; throws with a readable message when it is not one. */
export function parsePsu(buf: Buffer): { saveName: string; files: SaveFile[] } {
    if (buf.length < HEADER_SIZE * 3) { throw new Error('File is too small to be a .psu save'); }
    const rootMode = buf.readUInt16LE(0);
    if ((rootMode & 0x0020) === 0) { throw new Error('Not a .psu save (first entry is not a folder)'); }
    const saveName = readName(buf, 0);
    const count = buf.readUInt32LE(4);
    if (!saveName || count < 2 || count > 1024) { throw new Error('Not a .psu save (bad folder header)'); }

    const files: SaveFile[] = [];
    let pos = HEADER_SIZE;
    for (let i = 0; i < count && pos + HEADER_SIZE <= buf.length; i++) {
        const mode = buf.readUInt16LE(pos);
        const size = buf.readUInt32LE(pos + 4);
        const name = readName(buf, pos);
        pos += HEADER_SIZE;
        if ((mode & 0x0020) !== 0) { continue; } // "." / ".."
        if (pos + size > buf.length) { throw new Error(`.psu is truncated inside "${name}"`); }
        files.push({ name, data: Buffer.from(buf.subarray(pos, pos + size)) });
        pos += Math.ceil(size / 1024) * 1024;
    }
    return { saveName, files };
}

/** Folder names a memory card accepts (no path separators). */
export function safeSaveName(name: string): string {
    return name.replace(/[\\/:*?"<>|]/g, '_').slice(0, 31) || 'SAVE';
}

// ── Diff ─────────────────────────────────────────────────────────────────────

export interface ByteRun {
    offset: number;
    before: string;
    after: string;
}

export interface FileDiff {
    name: string;
    status: 'same' | 'changed' | 'added' | 'removed';
    sizeBefore: number;
    sizeAfter: number;
    changedBytes: number;
    runs: ByteRun[];
    /** True when more runs exist than were returned. */
    truncated: boolean;
}

const MAX_RUNS_PER_FILE = 300;
/** Runs closer than this are merged so one changed field reads as one row. */
const MERGE_GAP = 4;
const MAX_RUN_BYTES = 32;

const hex = (b: Buffer) => Array.from(b, x => x.toString(16).toUpperCase().padStart(2, '0')).join(' ');

/** Byte-level diff of `before` (the snapshot) against `after` (the current save). */
export function diffSaveFiles(before: SaveFile[], after: SaveFile[]): FileDiff[] {
    const byName = (list: SaveFile[]) => new Map(list.map(f => [f.name.toLowerCase(), f]));
    const a = byName(before);
    const b = byName(after);
    const names = [...new Set([...a.keys(), ...b.keys()])].sort();
    return names.map(key => {
        const fa = a.get(key);
        const fb = b.get(key);
        const name = (fb ?? fa)!.name;
        if (!fa) { return { name, status: 'added', sizeBefore: 0, sizeAfter: fb!.data.length, changedBytes: fb!.data.length, runs: [], truncated: false }; }
        if (!fb) { return { name, status: 'removed', sizeBefore: fa.data.length, sizeAfter: 0, changedBytes: fa.data.length, runs: [], truncated: false }; }

        const len = Math.max(fa.data.length, fb.data.length);
        const runs: ByteRun[] = [];
        let changed = 0;
        let truncated = false;
        let i = 0;
        while (i < len) {
            if (fa.data[i] === fb.data[i]) { i++; continue; }
            let end = i + 1;
            let lastDiff = i;
            changed++;
            while (end < len && end - i < MAX_RUN_BYTES && end - lastDiff <= MERGE_GAP) {
                if (fa.data[end] !== fb.data[end]) { lastDiff = end; changed++; }
                end++;
            }
            end = lastDiff + 1;
            if (runs.length < MAX_RUNS_PER_FILE) {
                runs.push({ offset: i, before: hex(fa.data.subarray(i, end)), after: hex(fb.data.subarray(i, end)) });
            } else {
                truncated = true;
            }
            i = end;
        }
        return { name, status: changed ? 'changed' : 'same', sizeBefore: fa.data.length, sizeAfter: fb.data.length, changedBytes: changed, runs, truncated };
    });
}
