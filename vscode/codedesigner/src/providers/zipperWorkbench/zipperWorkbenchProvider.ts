import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { SocketClient, ZipperOperationResult } from '../../socketClient';
import { getOutputChannel, getZdbChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';

export class ZipperWorkbenchProvider {
    public static readonly viewType = 'codedesigner.zipperWorkbench';

    private _panel: vscode.WebviewPanel | undefined;
    private _pendingInitialPath = '';
    // Cache of the last fully-loaded ZDB package so closing + reopening the
    // panel doesn't force the user to wait for a fresh extract. Keyed by
    // absolute file path. Storing nodes in the extension process means the
    // data survives the webview lifecycle (which can recreate the page).
    private _packageCache = new Map<string, {
        nodes: Array<{ index: number; name: string; type: number; data: string }>;
        loadedAt: number;
        preloadToken?: string;
    }>();
    // The last path the user inspected — used to auto-restore on panel reopen.
    private _lastInspectPath = '';

    constructor(
        private readonly _extensionUri: vscode.Uri,
        private readonly _socketClient: SocketClient,
    ) {}

    public open(initialPath?: string): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.Active);
            if (initialPath) { void this._inspectPath(this._panel.webview, initialPath); }
            return;
        }

        // When no explicit initial path is provided, default to the last
        // package the user inspected. This restores their work after a
        // close/reopen cycle without having to re-pick the file.
        this._pendingInitialPath = initialPath ?? this._lastInspectPath ?? '';
        const panel = vscode.window.createWebviewPanel(
            ZipperWorkbenchProvider.viewType,
            'Zipper FileFormats',
            vscode.ViewColumn.Active,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [this._extensionUri],
            },
        );

        applyCodeDesignerPanelIcon(panel, this._extensionUri);
        this._panel = panel;
        panel.onDidDispose(() => { this._panel = undefined; });
        panel.webview.onDidReceiveMessage(async msg => {
            await this._handleMessage(msg as Record<string, unknown>, panel.webview);
        });
        panel.webview.html = this._buildHtml(panel.webview);
    }

    private async _handleMessage(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        switch (msg['type'] as string | undefined) {
            case 'ready':
                webview.postMessage({ type: 'zipperReady' });
                if (this._pendingInitialPath) {
                    const initial = this._pendingInitialPath;
                    this._pendingInitialPath = '';
                    await this._inspectPath(webview, initial);
                }
                break;
            case 'openFile':
                await this._openFile(webview);
                break;
            case 'refresh':
                await this._inspectPath(webview, msg['path'] as string | undefined);
                break;
            case 'extractAll':
                await this._extractAll(webview, msg['path'] as string | undefined);
                break;
            case 'extractEntry':
                await this._extractEntry(
                    webview,
                    msg['path'] as string | undefined,
                    msg['entryIndex'] as number | undefined,
                    msg['entryName'] as string | undefined,
                );
                break;
            case 'saveExportDecrypted':
                await this._saveExportDecrypted(webview, msg['path'] as string | undefined);
                break;
            case 'saveCraft':
                await this._saveCraft(webview, msg['path'] as string | undefined, msg['mode'] as string | undefined);
                break;
            case 'apacheDecryptSection':
                await this._apacheDecryptSection(
                    webview,
                    msg['path'] as string | undefined,
                    msg['section'] as 'ftscore' | 'zsealetc' | undefined,
                );
                break;
            case 'apacheBuildPatch':
                await this._apacheBuildPatch(webview, msg['path'] as string | undefined);
                break;
            case 'revealPath':
                await this._revealPath(msg['path'] as string | undefined);
                break;
            case 'readNode':
                await this._readNode(
                    webview,
                    msg['path'] as string | undefined,
                    msg['entryIndex'] as number | undefined,
                );
                break;
            case 'readPaletteFile':
                await this._readPaletteFile(webview, msg['path'] as string | undefined);
                break;
            case 'readCompanionFiles':
                await this._readCompanionFiles(webview, msg['path'] as string | undefined);
                break;
            case 'inspectRdr':
                await this._inspectRdr(
                    webview,
                    msg['path'] as string | undefined,
                    msg['entryName'] as string | undefined,
                );
                break;
            case 'inspectRdrBytes':
                await this._inspectRdrBytes(
                    webview,
                    msg['base64'] as string | undefined,
                    msg['entryName'] as string | undefined,
                );
                break;
            case 'inspectMps':
                await this._inspectMps(webview, msg['path'] as string | undefined);
                break;
            case 'loadZdbPackage':
                // Mirrors the HTML reference behaviour: when a .zdb is opened,
                // fetch every top-level entry's bytes in one shot so the webview
                // can decode textures / palettes / GEO across the whole package
                // without round-tripping per entry.
                await this._loadZdbPackage(webview, msg['path'] as string | undefined, false);
                break;
            case 'loadArchiveNodes':
                await this._loadArchiveNodes(webview, msg['path'] as string | undefined);
                break;
            case 'reloadZdbPackage':
                // Force-bypass the cache (toolbar "Reload Package" button).
                await this._loadZdbPackage(webview, msg['path'] as string | undefined, true);
                break;
            case 'zipperLog': {
                // Stream the webview's log line into the dedicated output channel
                // so the user has the same kind of running diagnostic feed the
                // HTML reference viewers print on-page.
                const text = (msg['text'] as string) ?? '';
                if (text) { getZdbChannel().appendLine(text); }
                if (msg['reveal']) { getZdbChannel().show(true); }
                break;
            }
            case 'bootstrapError':
                getOutputChannel().error('[ZipperWorkbench] Bootstrap error: ' + msg['error']);
                break;
        }
    }

    private async _readNode(
        webview: vscode.Webview,
        filePath: string | undefined,
        entryIndex: number | undefined,
    ): Promise<void> {
        if (!filePath || entryIndex === undefined) { return; }
        if (!this._ensureConnected(webview)) { return; }
        try {
            const data = await this._socketClient.zipperReadNode(filePath, entryIndex);
            webview.postMessage({ type: 'nodeData', entryIndex, data });
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            webview.postMessage({ type: 'nodeError', entryIndex, message });
        }
    }

    private async _readPaletteFile(
        webview: vscode.Webview,
        currentPath: string | undefined,
    ): Promise<void> {
        if (!currentPath) { return; }
        if (!this._ensureConnected(webview)) { return; }
        try {
            const palPath = this._findSiblingPaletteFile(currentPath);
            if (!palPath) {
                webview.postMessage({ type: 'paletteFile', found: false });
                return;
            }
            const nodes = await this._socketClient.zipperReadAllNodes(palPath);
            webview.postMessage({ type: 'paletteFile', found: true, path: palPath, nodes });
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            webview.postMessage({ type: 'paletteFile', found: false, message });
        }
    }

    private async _readCompanionFiles(
        webview: vscode.Webview,
        currentPath: string | undefined,
    ): Promise<void> {
        if (!currentPath) { return; }
        if (!this._ensureConnected(webview)) { return; }
        try {
            const companions = this._findCompanionFiles(currentPath);
            const out: Record<string, { path: string; nodes: Array<{ index: number; name: string; type: number; data: string }> }> = {};
            for (const [kind, p] of Object.entries(companions)) {
                if (!p) { continue; }
                try {
                    const nodes = await this._socketClient.zipperReadAllNodes(p);
                    out[kind] = { path: p, nodes };
                } catch (err) {
                    getOutputChannel().warn(`[ZipperWorkbench] companion ${kind} load failed: ${err instanceof Error ? err.message : err}`);
                }
            }
            webview.postMessage({ type: 'companionFiles', companions: out });
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            webview.postMessage({ type: 'companionFiles', companions: {}, message });
        }
    }

    private async _inspectRdr(
        webview: vscode.Webview,
        filePath: string | undefined,
        entryName: string | undefined,
    ): Promise<void> {
        if (!filePath) { return; }
        if (!this._ensureConnected(webview)) { return; }
        try {
            const result = await this._socketClient.zipperInspectRdr(filePath, entryName);
            webview.postMessage({ type: 'rdrData', entryName, result });
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            webview.postMessage({ type: 'rdrError', entryName, message });
        }
    }

    private async _inspectRdrBytes(
        webview: vscode.Webview,
        base64: string | undefined,
        entryName: string | undefined,
    ): Promise<void> {
        if (!base64) { return; }
        if (!this._ensureConnected(webview)) { return; }
        try {
            const result = await this._socketClient.zipperInspectRdrBytes(base64, entryName);
            webview.postMessage({ type: 'rdrData', entryName, result });
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            webview.postMessage({ type: 'rdrError', entryName, message });
        }
    }

    // AIMAPS.MPS inspection. The .mps file isn't inside a container so we
    // ship it straight from the file system to the parser.
    private async _inspectMps(webview: vscode.Webview, filePath: string | undefined): Promise<void> {
        if (!filePath) { return; }
        if (!this._ensureConnected(webview)) { return; }
        try {
            const result = await this._socketClient.zipperInspectMps(filePath);
            webview.postMessage({ type: 'mpsData', result });
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            webview.postMessage({ type: 'mpsError', message });
        }
    }

    private async _loadZdbPackage(
        webview: vscode.Webview,
        filePath: string | undefined,
        force = false,
    ): Promise<void> {
        if (!filePath) { return; }

        // Reuse the cached package when available — survives the panel being
        // closed and reopened, since the cache lives in the extension process
        // (not in the webview's JS memory).
        if (!force) {
            const cached = this._packageCache.get(filePath);
            if (cached) {
                webview.postMessage({ type: 'zdbPackage', path: filePath, nodes: cached.nodes, fromCache: true, preloadToken: cached.preloadToken });
                return;
            }
        }

        if (!this._ensureConnected(webview)) { return; }
        try {
            if (this._isLooseMapPackageFile(filePath)) {
                const nodes = this._loadLooseMapPackageNodes(filePath);
                this._packageCache.set(filePath, { nodes, loadedAt: Date.now() });
                webview.postMessage({
                    type: 'zdbPackage',
                    path: filePath,
                    nodes,
                    fromCache: false,
                    preloadStats: {
                        preloadToken: '',
                        resourceCount: nodes.length,
                        totalBytes: nodes.reduce((sum, n) => sum + Math.floor((n.data.length * 3) / 4), 0),
                        cacheHit: false,
                    },
                    looseMapPackage: true,
                });
                return;
            }

            const preload = await this._socketClient.zipperPreloadMapPackage(filePath);
            // Current webview pipeline still expects full node payloads. We keep
            // this for compatibility while also priming backend preload cache.
            const nodes = await this._socketClient.zipperReadAllNodes(filePath);
            this._packageCache.set(filePath, { nodes, loadedAt: Date.now(), preloadToken: preload.preloadToken });
            // Trim cache to the last 4 packages so memory doesn't grow forever.
            if (this._packageCache.size > 4) {
                const oldest = [...this._packageCache.entries()].sort((a, b) => a[1].loadedAt - b[1].loadedAt)[0];
                if (oldest) {
                    if (oldest[1].preloadToken) {
                        this._socketClient.zipperReleasePreload(oldest[1].preloadToken).catch(() => undefined);
                    }
                    this._packageCache.delete(oldest[0]);
                }
            }
            webview.postMessage({ type: 'zdbPackage', path: filePath, nodes, fromCache: false, preloadToken: preload.preloadToken, preloadStats: preload });
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            webview.postMessage({ type: 'zdbPackageError', path: filePath, message });
        }
    }

    private _isLooseMapPackageFile(filePath: string): boolean {
        if (path.extname(filePath).toLowerCase() !== '.zed') { return false; }
        const dir = path.dirname(filePath);
        const stem = path.basename(filePath, path.extname(filePath));
        const dirPrefix = path.basename(dir);
        const hasWorldModel = fs.existsSync(path.join(dir, 'WORL_MDL.ZED'));
        const hasMapFamily = (prefix: string): boolean =>
            fs.existsSync(path.join(dir, `${prefix}_TXR.ZED`)) &&
            fs.existsSync(path.join(dir, `${prefix}_PAL.ZED`));
        return hasWorldModel && (hasMapFamily(stem) || hasMapFamily(dirPrefix) || !!this._findMapCompanionPrefix(dir));
    }

    private _loadLooseMapPackageNodes(filePath: string): Array<{ index: number; name: string; type: number; data: string }> {
        const dir = path.dirname(filePath);
        const candidates = fs.readdirSync(dir)
            .filter(name => /\.ZED$/i.test(name))
            .filter(name => /(?:_(?:MDL|TXR|PAL|GEO)(?:\([^)]+\))?|^WORL_MDL)\.ZED$/i.test(name))
            .sort((a, b) => {
                const rank = (name: string): number => {
                    if (/^worl_mdl/i.test(name)) { return 0; }
                    if (/_geo/i.test(name)) { return 1; }
                    if (/_mdl/i.test(name)) { return 2; }
                    if (/_txr/i.test(name)) { return 3; }
                    if (/_pal/i.test(name)) { return 4; }
                    return 5;
                };
                return rank(a) - rank(b) || a.localeCompare(b);
            });

        return candidates.map((name, index) => {
            const bytes = fs.readFileSync(path.join(dir, name));
            return {
                index,
                name,
                type: 0,
                data: Buffer.from(bytes).toString('base64'),
            };
        });
    }

    // True when this file is the entry point for the SOCOM UI bundle —
    // currently restricted to READERC.ZAR / READERX.ZAR. Opening a single
    // _TXR / _PAL / _MDL inside the same folder used to trigger the entire
    // bundle (~50 ZED archives) which made the model viewer freeze while
    private async _loadArchiveNodes(webview: vscode.Webview, filePath: string | undefined): Promise<void> {
        if (!filePath) { return; }
        if (!this._ensureConnected(webview)) { return; }
        try {
            const nodes = await this._socketClient.zipperReadAllNodes(filePath);
            webview.postMessage({ type: 'archiveNodes', path: filePath, nodes });
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            webview.postMessage({ type: 'archiveNodesError', path: filePath, message });
        }
    }

    private _findCompanionFiles(currentPath: string): Partial<Record<'txr' | 'pal' | 'geo' | 'mdl', string>> {
        const dir = path.dirname(currentPath);
        const base = path.basename(currentPath);
        const result: Partial<Record<'txr' | 'pal' | 'geo' | 'mdl', string>> = {};

        // Reader archives (READERC.ZAR / READERX.ZAR): load UI_TXR + UI_PAL so
        // that RDR screens can display their texture thumbnails immediately.
        if (/^reader[cx]\.zar$/i.test(base)) {
            for (const name of ['UI_TXR.ZED', 'UI2D_TXR.ZED']) {
                const p = path.join(dir, name);
                if (fs.existsSync(p)) { result['txr'] = p; break; }
            }
            for (const name of ['UI_PAL.ZED', 'UI2D_PAL.ZED']) {
                const p = path.join(dir, name);
                if (fs.existsSync(p)) { result['pal'] = p; break; }
            }
            return result;
        }

        // Match the prefix before _MDL / _TXR / _PAL / _GEO.
        const familyMatch = base.match(/^(.+?)_(MDL|TXR|PAL|GEO)(\([^)]+\))?\.ZED$/i);
        if (!familyMatch) { return result; }
        const prefix = familyMatch[1];
        const companionPrefix = /^worl$/i.test(prefix)
            ? (this._findMapCompanionPrefix(dir) ?? prefix)
            : prefix;

        const candidates: Record<'txr' | 'pal' | 'geo' | 'mdl', string[]> = {
            txr: [`${companionPrefix}_TXR.ZED`, `${companionPrefix}_TXR(1).ZED`],
            pal: [`${companionPrefix}_PAL.ZED`, `${companionPrefix}_PAL(1).ZED`],
            geo: [`${companionPrefix}_GEO.ZED`, `${prefix}_GEO.ZED`],
            mdl: [`${prefix}_MDL.ZED`],
        };

        for (const [kind, names] of Object.entries(candidates) as Array<['txr' | 'pal' | 'geo' | 'mdl', string[]]>) {
            for (const name of names) {
                const full = path.join(dir, name);
                if (fs.existsSync(full)) {
                    result[kind] = full;
                    break;
                }
            }
        }
        return result;
    }

    private _findMapCompanionPrefix(dir: string): string | undefined {
        const dirPrefix = path.basename(dir);
        const hasTxrPal = (prefix: string): boolean =>
            fs.existsSync(path.join(dir, `${prefix}_TXR.ZED`)) &&
            fs.existsSync(path.join(dir, `${prefix}_PAL.ZED`));

        if (dirPrefix && hasTxrPal(dirPrefix)) {
            return dirPrefix;
        }

        const supportPrefixes = new Set([
            'alph', 'benz', 'clib', 'effe', 'flib', 'font', 'hud', 'hud2',
            'hudw', 'junk', 'ld', 'load', 'objt', 'tlrm', 'trmg', 'weap',
        ]);
        try {
            const entries = fs.readdirSync(dir);
            const prefixes = new Set<string>();
            for (const entry of entries) {
                const m = /^(.+?)_TXR(?:\([^)]+\))?\.ZED$/i.exec(entry);
                if (m) { prefixes.add(m[1]); }
            }
            const candidates = [...prefixes].filter(prefix => {
                const lower = prefix.toLowerCase();
                if (supportPrefixes.has(lower)) { return false; }
                if (/^ld\d+$/i.test(prefix) || /^z.+\d+$/i.test(prefix)) { return false; }
                return hasTxrPal(prefix);
            });
            return candidates[0];
        } catch {
            return undefined;
        }
    }

    private _findSiblingPaletteFile(currentPath: string): string | undefined {
        const dir = path.dirname(currentPath);
        const base = path.basename(currentPath);
        const candidates: string[] = [];

        const txrMatch = base.match(/^(.*?)_TXR(?:\([^)]+\))?\.ZED$/i);
        if (txrMatch) {
            candidates.push(`${txrMatch[1]}_PAL.ZED`);
            candidates.push(`${txrMatch[1]}_PAL(1).ZED`);
        }
        candidates.push(base.replace(/_TXR/i, '_PAL'));

        for (const candidate of candidates) {
            const full = path.join(dir, candidate);
            if (fs.existsSync(full)) { return full; }
        }

        try {
            for (const entry of fs.readdirSync(dir)) {
                if (/_PAL(?:\([^)]+\))?\.ZED$/i.test(entry)) {
                    return path.join(dir, entry);
                }
            }
        } catch { /* ignore */ }
        return undefined;
    }


    private _findParentPackageCache(filePath: string): { path: string; nodes: Array<{ index: number; name: string; type: number; data: string }>; preloadToken?: string } | undefined {
        const direct = this._packageCache.get(filePath);
        if (direct) {
            return { path: filePath, nodes: direct.nodes, preloadToken: direct.preloadToken };
        }

        const dir = path.dirname(filePath);
        const lowered = filePath.toLowerCase();
        // When a child archive from a map package is opened (e.g. .zar/.zed),
        // reuse any cached parent .zdb package in the same directory so common
        // resources remain available to the webview resolver.
        const candidate = [...this._packageCache.entries()]
            .filter(([cachedPath]) => cachedPath.toLowerCase().endsWith('.zdb') && path.dirname(cachedPath) === dir)
            .sort((a, b) => b[1].loadedAt - a[1].loadedAt)[0];

        if (candidate) {
            return { path: candidate[0], nodes: candidate[1].nodes, preloadToken: candidate[1].preloadToken };
        }

        // Last fallback: prefix match helps when child path contains same base stem.
        const stem = path.basename(filePath, path.extname(filePath)).toLowerCase();
        const byStem = [...this._packageCache.entries()]
            .filter(([cachedPath]) => cachedPath.toLowerCase().endsWith('.zdb') && path.basename(cachedPath, '.zdb').toLowerCase().includes(stem.slice(0, Math.min(stem.length, 6))))
            .sort((a, b) => b[1].loadedAt - a[1].loadedAt)[0];
        if (byStem) {
            return { path: byStem[0], nodes: byStem[1].nodes, preloadToken: byStem[1].preloadToken };
        }

        return undefined;
    }

    private async _openFile(webview: vscode.Webview): Promise<void> {
        const uris = await vscode.window.showOpenDialog({
            title: 'Open Zipper/FileFormats File',
            canSelectFiles: true,
            canSelectFolders: false,
            canSelectMany: false,
            filters: {
                'Zipper/FileFormats': ['zdb', 'zar', 'zed'],
                'All Files': ['*'],
            },
        });
        if (!uris?.length) { return; }
        await this._inspectPath(webview, uris[0].fsPath);
    }

    private async _inspectPath(webview: vscode.Webview, filePath: string | undefined): Promise<void> {
        const cleanPath = filePath?.trim();
        if (!cleanPath) { return; }
        if (!this._ensureConnected(webview)) { return; }

        try {
            this._postStatus(webview, 'Inspecting file...', true);
            const result = await this._socketClient.zipperInspect(cleanPath);
            // Remember the path so re-opening the panel auto-restores the
            // last-loaded file (combined with _packageCache this means the
            // user can close + reopen without re-extracting anything).
            this._lastInspectPath = cleanPath;
            webview.postMessage({ type: 'zipperLoaded', result });

            const ext = path.extname(cleanPath).toLowerCase();
            if (ext === '.zar' || ext === '.zed') {
                const parent = this._findParentPackageCache(cleanPath);
                if (parent) {
                    webview.postMessage({
                        type: 'zdbPackage',
                        path: parent.path,
                        nodes: parent.nodes,
                        fromCache: true,
                        fromParentPackage: true,
                        preloadToken: parent.preloadToken,
                    });
                }
            }
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _extractAll(webview: vscode.Webview, filePath: string | undefined): Promise<void> {
        if (!filePath) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const dirs = await vscode.window.showOpenDialog({
            title: 'Extract All',
            canSelectFiles: false,
            canSelectFolders: true,
            canSelectMany: false,
        });
        if (!dirs?.length) { return; }

        try {
            this._postStatus(webview, 'Extracting entries...', true);
            const result = await this._socketClient.zipperExtractAll(filePath, dirs[0].fsPath);
            this._postOperation(webview, 'Extracted entries.', result);
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _extractEntry(
        webview: vscode.Webview,
        filePath: string | undefined,
        entryIndex: number | undefined,
        entryName: string | undefined,
    ): Promise<void> {
        if (!filePath || entryIndex === undefined) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const target = await vscode.window.showSaveDialog({
            title: `Extract ${entryName ?? 'Entry'}`,
            defaultUri: vscode.Uri.file(this._defaultOutputPath(filePath, entryName ?? `entry_${entryIndex}`)),
            filters: { 'All Files': ['*'] },
        });
        if (!target) { return; }

        try {
            this._postStatus(webview, 'Extracting entry...', true);
            const result = await this._socketClient.zipperExtractEntry(filePath, entryIndex, target.fsPath);
            this._postOperation(webview, 'Extracted entry.', result);
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _saveExportDecrypted(webview: vscode.Webview, filePath: string | undefined): Promise<void> {
        if (!filePath) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const target = await vscode.window.showSaveDialog({
            title: 'Export Decrypted Save',
            defaultUri: vscode.Uri.file(this._defaultOutputPath(filePath, `${path.basename(filePath)}.decrypted.bin`)),
            filters: { 'Binary': ['bin'], 'All Files': ['*'] },
        });
        if (!target) { return; }

        try {
            this._postStatus(webview, 'Exporting decrypted save...', true);
            const result = await this._socketClient.zipperSaveExportDecrypted(filePath, target.fsPath);
            this._postOperation(webview, 'Exported decrypted save.', result);
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _saveCraft(webview: vscode.Webview, filePath: string | undefined, mode: string | undefined): Promise<void> {
        if (!filePath) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const selectedMode = this._normalizeSaveMode(mode);
        let title: string | undefined;
        let titleLength: number | undefined;

        if (selectedMode === 'active' || selectedMode === 'extreme') {
            title = await vscode.window.showInputBox({
                title: selectedMode === 'active' ? 'Active Save Title' : 'Extreme Probe Title',
                value: selectedMode === 'active' ? 'SOCOM Revival Save' : 'EXTREME INT TEST',
            });
            if (title === undefined) { return; }
        } else if (selectedMode === 'overflow') {
            const raw = await vscode.window.showInputBox({
                title: 'Overflow Probe Title Length',
                value: '200',
                validateInput: value => {
                    const n = Number(value);
                    return Number.isInteger(n) && n > 0 ? null : 'Enter a positive integer.';
                },
            });
            if (raw === undefined) { return; }
            titleLength = Number(raw);
        }

        const dirs = await vscode.window.showOpenDialog({
            title: 'Save Crafted Files',
            canSelectFiles: false,
            canSelectFolders: true,
            canSelectMany: false,
        });
        if (!dirs?.length) { return; }

        try {
            this._postStatus(webview, 'Crafting save file...', true);
            const result = await this._socketClient.zipperSaveCraft(filePath, dirs[0].fsPath, selectedMode, title, titleLength);
            this._postOperation(webview, 'Crafted save file.', result);
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _apacheDecryptSection(
        webview: vscode.Webview,
        filePath: string | undefined,
        section: 'ftscore' | 'zsealetc' | undefined,
    ): Promise<void> {
        if (!filePath || !section) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const dirs = await vscode.window.showOpenDialog({
            title: `Decrypt ${section}`,
            canSelectFiles: false,
            canSelectFolders: true,
            canSelectMany: false,
        });
        if (!dirs?.length) { return; }

        try {
            this._postStatus(webview, `Decrypting ${section}...`, true);
            const result = await this._socketClient.apacheDecryptSection(filePath, dirs[0].fsPath, section);
            this._postOperation(webview, `Decrypted ${section}.`, result);
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private async _apacheBuildPatch(webview: vscode.Webview, filePath: string | undefined): Promise<void> {
        if (!filePath) { return; }
        if (!this._ensureConnected(webview)) { return; }

        const ftScore = await vscode.window.showOpenDialog({
            title: 'Select Decrypted FTScore MWo3',
            canSelectFiles: true,
            canSelectFolders: false,
            canSelectMany: false,
            filters: { 'Binary': ['bin'], 'All Files': ['*'] },
        });
        if (!ftScore?.length) { return; }

        const dirs = await vscode.window.showOpenDialog({
            title: 'Patch Output Folder',
            canSelectFiles: false,
            canSelectFolders: true,
            canSelectMany: false,
        });
        if (!dirs?.length) { return; }

        try {
            this._postStatus(webview, 'Building APACHE00 patch...', true);
            const result = await this._socketClient.apacheBuildPatch(filePath, ftScore[0].fsPath, dirs[0].fsPath);
            this._postOperation(webview, `Built APACHE00 patch${result.buildVersion ? ` ${result.buildVersion}` : ''}.`, result);
        } catch (err) {
            this._postError(webview, err);
        }
    }

    private _ensureConnected(webview: vscode.Webview): boolean {
        if (this._socketClient.isConnected()) { return true; }
        webview.postMessage({
            type: 'zipperError',
            message: 'Not connected to the CodeDesigner server.',
        });
        return false;
    }

    private _postStatus(webview: vscode.Webview, message: string, busy: boolean): void {
        webview.postMessage({ type: 'zipperStatus', message, busy });
    }

    private _postError(webview: vscode.Webview, err: unknown): void {
        const message = err instanceof Error ? err.message : String(err);
        webview.postMessage({ type: 'zipperError', message });
    }

    private _postOperation(webview: vscode.Webview, message: string, result: ZipperOperationResult): void {
        webview.postMessage({ type: 'zipperOperation', message, result });
    }

    private async _revealPath(targetPath: string | undefined): Promise<void> {
        if (!targetPath) { return; }
        await vscode.commands.executeCommand('revealFileInOS', vscode.Uri.file(targetPath));
    }

    private _defaultOutputPath(sourcePath: string, name: string): string {
        const dir = path.dirname(sourcePath);
        return path.join(dir, this._safeFileName(name));
    }

    private _safeFileName(name: string): string {
        return name.replace(/[<>:"/\\|?*\x00-\x1f]/g, '_') || 'entry.bin';
    }

    private _normalizeSaveMode(mode: string | undefined): 'active' | 'overflow' | 'extreme' | 'all' {
        switch ((mode ?? '').toLowerCase()) {
            case 'overflow': return 'overflow';
            case 'extreme': return 'extreme';
            case 'all': return 'all';
            default: return 'active';
        }
    }

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'dist', 'webview', 'zipper-workbench.js'),
        );
        const nonce = getNonce();
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               style-src 'unsafe-inline';
               img-src data:;
               script-src ${webview.cspSource} 'nonce-${nonce}';">
<script nonce="${nonce}">${getWebviewErrorHandlerScript()}</script>
</head>
<body>
<app-zipper-workbench-page>
  <p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Zipper FileFormats...</p>
</app-zipper-workbench-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
