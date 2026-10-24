import * as fs from 'fs';
import * as path from 'path';
import { SaveFile, safeSaveName } from './ps2SaveFiles';

/** Metadata of one stored copy of a save (memory card save or HDD folder). */
export interface SaveSnapshotInfo {
    id: string;
    saveName: string;
    /** Card / HDD image the save was copied from. */
    source: string;
    created: string;
    label: string;
    fileCount: number;
    totalBytes: number;
}

/**
 * Point-in-time copies of saves kept in the extension's global storage, so a
 * save can be diffed against how it looked earlier (e.g. before earning money
 * in game, to find where the amount is stored).
 *
 * Layout: <root>/<kind>/<saveName>/<id>/{meta.json, files/<name>}
 */
export class SaveSnapshotStore {
    constructor(private readonly root: string, private readonly kind: 'mc' | 'hdd') {}

    private saveDir(saveName: string): string {
        return path.join(this.root, 'save-snapshots', this.kind, safeSaveName(saveName));
    }

    list(saveName: string): SaveSnapshotInfo[] {
        const dir = this.saveDir(saveName);
        if (!fs.existsSync(dir)) { return []; }
        const out: SaveSnapshotInfo[] = [];
        for (const id of fs.readdirSync(dir)) {
            try {
                out.push(JSON.parse(fs.readFileSync(path.join(dir, id, 'meta.json'), 'utf8')) as SaveSnapshotInfo);
            } catch { /* skip incomplete snapshot */ }
        }
        return out.sort((a, b) => b.created.localeCompare(a.created));
    }

    add(saveName: string, source: string, files: SaveFile[], label: string): SaveSnapshotInfo {
        const created = new Date().toISOString();
        const id = created.replace(/[-:.TZ]/g, '').slice(0, 17);
        const dir = path.join(this.saveDir(saveName), id);
        fs.mkdirSync(path.join(dir, 'files'), { recursive: true });
        for (const f of files) {
            fs.writeFileSync(path.join(dir, 'files', safeSaveName(f.name)), f.data);
        }
        const info: SaveSnapshotInfo = {
            id, saveName, source, created, label,
            fileCount: files.length,
            totalBytes: files.reduce((n, f) => n + f.data.length, 0),
        };
        // Original names are kept in the index; on-disk names are sanitised.
        fs.writeFileSync(path.join(dir, 'meta.json'), JSON.stringify({ ...info, names: files.map(f => f.name) }, null, 2));
        return info;
    }

    read(saveName: string, id: string): SaveFile[] {
        const dir = path.join(this.saveDir(saveName), safeSaveName(id));
        const meta = JSON.parse(fs.readFileSync(path.join(dir, 'meta.json'), 'utf8')) as { names?: string[] };
        return (meta.names ?? []).map(name => ({
            name,
            data: fs.readFileSync(path.join(dir, 'files', safeSaveName(name))),
        }));
    }

    remove(saveName: string, id: string): void {
        fs.rmSync(path.join(this.saveDir(saveName), safeSaveName(id)), { recursive: true, force: true });
    }
}
