import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { SnapshotEntry } from '../../webview/disasm-panel/types';
import { SnapshotDataDelegate } from '../snapshotManager/snapshotManagerProvider';
import { SocketClient } from '../../socketClient';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';

/** 32 MB — full EE RAM */
const EE_RAM_SIZE      = 32 * 1024 * 1024;
const EE_RAM_BASE      = 0x00000000;
/** READMEMREGION returns hex; 4 MB slices keep each response ~8 MB and let us report progress. */
const LIVE_READ_CHUNK  = 4 * 1024 * 1024;
/** Diff granularity — one GitHub-style "line" of hex is 16 bytes. */
const ROW_BYTES        = 16;
/** Coarse block size for the fast equality pre-pass. */
const SCAN_BLOCK_BYTES = 4096;
/** Cap on changed-row runs sent to the webview (two unrelated dumps can differ everywhere). */
const MAX_RUNS         = 100_000;
/** Cap on codes produced by a single "Copy as codes". */
const MAX_COPY_CODES   = 20_000;

/** One side of a comparison: a dump on disk or the emulator's live EE RAM. */
export type DiffSource =
    | { kind: 'live' }
    | { kind: 'dump'; path: string; label: string };

interface LoadedSide {
    buf:   Buffer;
    label: string;
    /** Resolved .bin actually read (for dumps), shown as a tooltip. */
    file?: string;
}

export class DiffViewerProvider {
    private _panel: vscode.WebviewPanel | undefined;
    /** Buffers of the most recent comparison — row/disasm/copy requests read from these. */
    private _a: LoadedSide | null = null;
    private _b: LoadedSide | null = null;
    /** Incremented per compare so stale responses can be ignored by the webview. */
    private _diffId = 0;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly delegate: SnapshotDataDelegate,
        private readonly socketClient: SocketClient,
    ) {}

    /** Open (or reveal) the Diff Viewer panel, optionally pre-loading snapshots. */
    public open(snapshotA?: SnapshotEntry | null, snapshotB?: SnapshotEntry | null): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            if (snapshotA || snapshotB) {
                this._panel.webview.postMessage({ type: 'preloadSnapshots', snapshotA, snapshotB });
            }
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.diffViewer',
            'Memory Diff',
            vscode.ViewColumn.One,
            {
                enableScripts:           true,
                retainContextWhenHidden: true,
                localResourceRoots:      [this.extensionUri],
            }
        );
        applyCodeDesignerPanelIcon(panel, this.extensionUri, 'gitcompare');
        this._panel = panel;

        panel.onDidDispose(() => {
            this._panel = undefined;
            this._a = null;
            this._b = null;
        });

        panel.webview.onDidReceiveMessage(async (msg: Record<string, unknown>) => {
            await this._handleMessage(msg, panel.webview);
        });

        panel.webview.html = this._buildHtml(panel.webview);

        if (snapshotA || snapshotB) {
            this._pendingPreload = { snapshotA: snapshotA ?? null, snapshotB: snapshotB ?? null };
        }
    }

    private _pendingPreload: { snapshotA: SnapshotEntry | null; snapshotB: SnapshotEntry | null } | null = null;

    // ── Message dispatch ──────────────────────────────────────────────────────

    private async _handleMessage(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        const type = msg['type'] as string | undefined;

        switch (type) {
            case 'ready':
                await this._postSnapshotList(webview);
                if (this._pendingPreload) {
                    webview.postMessage({ type: 'preloadSnapshots', ...this._pendingPreload });
                    this._pendingPreload = null;
                }
                break;

            case 'refreshSnapshots':
                await this._postSnapshotList(webview);
                break;

            case 'browse': {
                const side = msg['side'] as 'A' | 'B';
                const uris = await vscode.window.showOpenDialog({
                    canSelectFiles:   true,
                    canSelectFolders: false,
                    canSelectMany:    false,
                    filters:          { 'Memory dumps': ['bin', 'raw', 'dump', 'p2s'], 'All Files': ['*'] },
                    title:            `Select memory dump for side ${side}`,
                });
                if (!uris || uris.length === 0) { break; }
                const snap: SnapshotEntry = {
                    path:      uris[0].fsPath,
                    label:     path.basename(uris[0].fsPath),
                    name:      path.basename(uris[0].fsPath),
                    isoName:   '',
                    date:      '',
                    timestamp: '',
                };
                webview.postMessage({ type: 'browseResult', side, snapshot: snap });
                break;
            }

            case 'compare':
                await this._handleCompare(
                    msg['a'] as DiffSource,
                    msg['b'] as DiffSource,
                    Number(msg['start'] ?? 0) >>> 0,
                    Number(msg['end'] ?? EE_RAM_SIZE) >>> 0,
                    webview,
                );
                break;

            case 'getRows':
                this._handleGetRows(msg, webview);
                break;

            case 'disassemble':
                await this._handleDisassemble(msg, webview);
                break;

            case 'copyCodes':
                await this._handleCopyCodes(Number(msg['startRow']), Number(msg['endRow']), webview);
                break;

            case 'gotoInEditor':
                vscode.commands.executeCommand('codedesigner.gotoAddress', msg['address']);
                break;

            default:
                getOutputChannel().warn(`[DiffViewer] Unknown message type: ${type}`);
                break;
        }
    }

    private async _postSnapshotList(webview: vscode.Webview): Promise<void> {
        try {
            const snapshots = await this.delegate.getSnapshots();
            webview.postMessage({ type: 'snapshotList', snapshots, connected: this.socketClient.isConnected() });
        } catch (err: any) {
            webview.postMessage({
                type: 'snapshotList', snapshots: [], connected: this.socketClient.isConnected(),
                error: err?.message ?? String(err),
            });
        }
    }

    // ── Compare ───────────────────────────────────────────────────────────────

    private async _handleCompare(
        a: DiffSource, b: DiffSource, start: number, end: number, webview: vscode.Webview,
    ): Promise<void> {
        const diffId = ++this._diffId;
        const progress = (message: string) => webview.postMessage({ type: 'progress', message });
        try {
            if (!a || !b) { throw new Error('Select both sides to compare.'); }
            if (a.kind === 'live' && b.kind === 'live') { throw new Error('Pick a dump for at least one side.'); }

            // Read live once even if (somehow) both sides reference it.
            this._a = await this._loadSide(a, progress);
            this._b = await this._loadSide(b, progress);
            if (diffId !== this._diffId) { return; }

            progress('Comparing…');
            const len      = Math.min(this._a.buf.length, this._b.buf.length);
            const clampEnd = Math.min(end, EE_RAM_BASE + len);
            if (clampEnd <= start) { throw new Error('Address range is empty for these sources.'); }

            const t0 = Date.now();
            const result = this._diffRows(this._a.buf, this._b.buf, start - EE_RAM_BASE, clampEnd - EE_RAM_BASE);
            getOutputChannel().info(
                `[DiffViewer] ${this._a.label} vs ${this._b.label}: ${result.changedRows} rows / ` +
                `${result.changedBytes} bytes changed in ${result.runs.length / 3} runs (${Date.now() - t0} ms)`);

            webview.postMessage({
                type:      'diffResult',
                diffId,
                base:      EE_RAM_BASE,
                rowBytes:  ROW_BYTES,
                minRow:    Math.floor((start - EE_RAM_BASE) / ROW_BYTES),
                maxRow:    Math.ceil((clampEnd - EE_RAM_BASE) / ROW_BYTES),
                labelA:    this._a.label,
                labelB:    this._b.label,
                fileA:     this._a.file ?? '',
                fileB:     this._b.file ?? '',
                lengthA:   this._a.buf.length,
                lengthB:   this._b.buf.length,
                readAt:    new Date().toLocaleTimeString(),
                ...result,
            });
        } catch (err: any) {
            if (diffId !== this._diffId) { return; }
            webview.postMessage({ type: 'diffError', message: err?.message ?? String(err) });
        }
    }

    private async _loadSide(src: DiffSource, progress: (m: string) => void): Promise<LoadedSide> {
        if (src.kind === 'live') {
            return { buf: await this._readLiveEeRam(progress), label: 'Live EE RAM' };
        }
        progress(`Loading ${src.label}…`);
        const file = await this._resolveDumpFile(src.path);
        const buf  = await fs.promises.readFile(file);
        return { buf, label: src.label || path.basename(src.path), file };
    }

    /** Read all of EE RAM from the emulator in READMEMREGION slices. */
    private async _readLiveEeRam(progress: (m: string) => void): Promise<Buffer> {
        if (!this.socketClient.isConnected()) {
            throw new Error('Not connected to the emulator — live EE RAM is unavailable.');
        }
        const out = Buffer.alloc(EE_RAM_SIZE);
        for (let off = 0; off < EE_RAM_SIZE; off += LIVE_READ_CHUNK) {
            progress(`Reading live EE RAM… ${off / (1024 * 1024)} / ${EE_RAM_SIZE / (1024 * 1024)} MB`);
            const { bytes } = await this.socketClient.readMemoryRegion(
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
    private async _resolveDumpFile(p: string): Promise<string> {
        let dir: string | null = null;
        if (p.toLowerCase().endsWith('.p2s')) {
            if (!this.socketClient.isConnected()) {
                throw new Error('Connect to the bridge to read .p2s save states.');
            }
            dir = await this.socketClient.extractP2s(p);
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

    /**
     * Find changed 16-byte rows in [start, end) and collapse them into runs.
     * `runs` is flat triples: startRow, endRow (exclusive), changedBytes.
     */
    private _diffRows(a: Buffer, b: Buffer, start: number, end: number):
        { runs: number[]; changedRows: number; changedBytes: number; truncated: boolean } {
        const runs: number[] = [];
        let changedRows = 0, changedBytes = 0, truncated = false;
        let runStart = -1, runBytes = 0;

        const closeRun = (row: number) => {
            if (runStart < 0) { return; }
            if (runs.length / 3 < MAX_RUNS) { runs.push(runStart, row, runBytes); }
            else                            { truncated = true; }
            runStart = -1;
            runBytes = 0;
        };

        const rowStart = Math.floor(start / ROW_BYTES) * ROW_BYTES;
        for (let blk = rowStart; blk < end; blk += SCAN_BLOCK_BYTES) {
            const blkEnd = Math.min(blk + SCAN_BLOCK_BYTES, end);
            if (a.compare(b, blk, blkEnd, blk, blkEnd) === 0) {
                closeRun(blk / ROW_BYTES);
                continue;
            }
            for (let off = blk; off < blkEnd; off += ROW_BYTES) {
                const rowEnd = Math.min(off + ROW_BYTES, blkEnd);
                let diff = 0;
                for (let i = Math.max(off, start); i < rowEnd; i++) {
                    if (a[i] !== b[i]) { diff++; }
                }
                const row = off / ROW_BYTES;
                if (diff === 0) { closeRun(row); continue; }
                if (runStart < 0) { runStart = row; }
                runBytes     += diff;
                changedRows  += 1;
                changedBytes += diff;
            }
        }
        closeRun(Math.ceil(end / ROW_BYTES));
        return { runs, changedRows, changedBytes, truncated };
    }

    // ── Row / disassembly / copy requests ─────────────────────────────────────

    /** Return hex for row ranges so the webview can render hunks it is showing. */
    private _handleGetRows(msg: Record<string, unknown>, webview: vscode.Webview): void {
        if (!this._a || !this._b) { return; }
        const ranges = (msg['ranges'] as Array<[number, number]>) ?? [];
        const len = Math.min(this._a.buf.length, this._b.buf.length);
        const out = ranges.map(([startRow, endRow]) => {
            const s = Math.max(0, startRow * ROW_BYTES);
            const e = Math.min(len, endRow * ROW_BYTES);
            return {
                startRow,
                a: e > s ? this._a!.buf.toString('hex', s, e) : '',
                b: e > s ? this._b!.buf.toString('hex', s, e) : '',
            };
        });
        webview.postMessage({ type: 'rows', diffId: msg['diffId'], ranges: out });
    }

    /** Disassemble word ranges from one side via the bridge (fixed 4-byte stride). */
    private async _handleDisassemble(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        const side = this._sideBuffer(msg['side'] as string);
        const ranges = (msg['ranges'] as Array<{ addr: number; count: number }>) ?? [];
        const asm: Record<string, string> = {};
        if (side && this.socketClient.isConnected()) {
            await Promise.all(ranges.map(async ({ addr, count }) => {
                const off = addr - EE_RAM_BASE;
                const words: string[] = [];
                for (let i = 0; i < count && off + i * 4 + 4 <= side.length; i++) {
                    words.push('0x' + side.readUInt32LE(off + i * 4).toString(16).padStart(8, '0'));
                }
                if (words.length === 0) { return; }
                try {
                    const results = await this.socketClient.disassemble(words.join(' '), addr, undefined, true);
                    results.forEach((r, i) => {
                        const at = (r.Address ?? addr + i * 4) >>> 0;
                        asm[at.toString()] = r.Assembly ?? r.Error ?? '';
                    });
                } catch (err: any) {
                    getOutputChannel().debug(`[DiffViewer] disassemble @${addr.toString(16)} failed: ${err?.message ?? err}`);
                }
            }));
        }
        webview.postMessage({ type: 'disasm', diffId: msg['diffId'], side: msg['side'], asm });
    }

    /** Copy changed words in [startRow, endRow) as 32-bit write codes using side B's values. */
    private async _handleCopyCodes(startRow: number, endRow: number, webview: vscode.Webview): Promise<void> {
        if (!this._a || !this._b) { return; }
        const a = this._a.buf, b = this._b.buf;
        const end = Math.min(endRow * ROW_BYTES, a.length, b.length) & ~3;
        const lines: string[] = [];
        for (let off = Math.max(0, startRow * ROW_BYTES); off < end && lines.length < MAX_COPY_CODES; off += 4) {
            const wb = b.readUInt32LE(off);
            if (a.readUInt32LE(off) === wb) { continue; }
            const addr = ((EE_RAM_BASE + off) & 0x01FFFFFF) | 0x20000000;
            lines.push(`${hex8(addr)} ${hex8(wb)}`);
        }
        if (lines.length === 0) {
            webview.postMessage({ type: 'status', message: 'No changed words in this hunk.', isError: true });
            return;
        }
        await vscode.env.clipboard.writeText(lines.join('\n'));
        webview.postMessage({
            type: 'status',
            message: `Copied ${lines.length} code${lines.length === 1 ? '' : 's'} (${this._b.label} values).`,
        });
    }

    private _sideBuffer(side: string): Buffer | null {
        return side === 'A' ? this._a?.buf ?? null : side === 'B' ? this._b?.buf ?? null : null;
    }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'webview', 'diff-viewer.js')
        );
        const nonce = getNonce();
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               script-src ${webview.cspSource} 'nonce-${nonce}';">
<script nonce="${nonce}">${getWebviewErrorHandlerScript()}</script>
</head>
<body>
<app-diff-viewer-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Memory Diff…</p></app-diff-viewer-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}

function hex8(n: number): string {
    return (n >>> 0).toString(16).toUpperCase().padStart(8, '0');
}
