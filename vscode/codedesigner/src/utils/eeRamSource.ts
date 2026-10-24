import * as fs from 'fs';
import * as path from 'path';
import { SocketClient } from '../socketClient';

/** 32 MB — full EE RAM */
export const EE_RAM_SIZE = 32 * 1024 * 1024;
export const EE_RAM_BASE = 0x00000000;
/** READMEMREGION returns hex; 4 MB slices keep each response ~8 MB and let us report progress. */
const LIVE_READ_CHUNK = 4 * 1024 * 1024;

/** An EE RAM image: a dump on disk (raw .bin, snapshot folder or .p2s) or the emulator's live RAM. */
export type EeRamSource =
    | { kind: 'live' }
    | { kind: 'dump'; path: string; label: string };

/** Read all of EE RAM from the emulator in READMEMREGION slices. */
export async function readLiveEeRam(socketClient: SocketClient, progress: (m: string) => void): Promise<Buffer> {
    if (!socketClient.isConnected()) {
        throw new Error('Not connected to the emulator — live EE RAM is unavailable.');
    }
    const out = Buffer.alloc(EE_RAM_SIZE);
    for (let off = 0; off < EE_RAM_SIZE; off += LIVE_READ_CHUNK) {
        progress(`Reading live EE RAM… ${off / (1024 * 1024)} / ${EE_RAM_SIZE / (1024 * 1024)} MB`);
        const { bytes } = await socketClient.readMemoryRegion(
            EE_RAM_BASE + off, EE_RAM_BASE + off + LIVE_READ_CHUNK);
        Buffer.from(bytes, 'hex').copy(out, off);
    }
    return out;
}

/**
 * Map a snapshot path to the EE RAM .bin it holds. PCSX2 .p2s states are
 * extracted by the bridge (EEmem.bin in a sibling .extracted folder);
 * snapshot folders use their EE dump; anything else is read as a raw dump.
 */
export async function resolveEeDumpFile(socketClient: SocketClient, p: string): Promise<string> {
    let dir: string | null = null;
    if (p.toLowerCase().endsWith('.p2s')) {
        if (!socketClient.isConnected()) {
            throw new Error('Connect to the bridge to read .p2s save states.');
        }
        dir = await socketClient.extractP2s(p);
    } else {
        try {
            if ((await fs.promises.stat(p)).isDirectory()) { dir = p; }
        } catch {
            throw new Error(`File not found: ${p}`);
        }
    }
    if (!dir) { return p; }

    const bins = (await fs.promises.readdir(dir)).filter(f => f.toLowerCase().endsWith('.bin'));
    const ee = bins.find(f => /^(eemem|eememory)\.bin$/i.test(f))
            ?? bins.find(f => /^ee/i.test(f))
            ?? bins[0];
    if (!ee) { throw new Error(`No .bin memory dump found in ${dir}`); }
    return path.join(dir, ee);
}
