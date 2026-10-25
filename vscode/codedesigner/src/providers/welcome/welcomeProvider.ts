import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';
import { renderMarkdown } from './markdownLite';

/**
 * Data the WelcomeProvider needs from the DisasmPanelProvider — keeps the
 * provider decoupled from the panel's internals (mirrors the
 * SnapshotDataDelegate pattern).
 */
export interface WelcomeDataDelegate {
    /** True when the CodeDesigner.Ps2.Bridge socket connection is up. */
    isBridgeConnected(): boolean;
    /** True when the currently selected emulator process is running. */
    isEmulatorRunning(): Promise<boolean>;
    /** Absolute path of the user's currently selected emulator (may be ''). */
    getSelectedEmulatorPath(): string;
    /** Display name of the currently selected emulator (e.g. 'pcsx2-qtx64-dbg'). */
    getSelectedEmulatorLabel(): string;
    /** Absolute path of the selected BIOS file (may be ''). */
    getSelectedBiosPath(): string;
    /** Display label for the selected BIOS (file name without extension). */
    getSelectedBiosLabel(): string;
    /** Title of the loaded game (resolved from PCSX2 or the ISO), if known. */
    getLoadedGameTitle(): string;
    /** Disc serial of the loaded game (e.g. 'SCUS-97275'), if known. */
    getLoadedGameSerial(): string;
    // ── Actions ──────────────────────────────────────────────────────────
    /** Connect to the configured emulator over PCSX2 PINE. */
    connectEmulator(): void;
}

interface DocEntry { label: string; relPath: string; mtimeMs: number; }
interface ToolEntry { id: string; label: string; subtitle: string; icon: string; }

/**
 * Standalone Welcome panel — the first tab opened when the extension boots.
 * Replaces the old logo splash with a full landing page: status snapshot,
 * quick-start actions, walkthroughs (docs), recent games, common tools,
 * what's-new release notes, and keyboard tips.
 */
export class WelcomeProvider {
    private _panel: vscode.WebviewPanel | undefined;
    private _pollTimer: ReturnType<typeof setInterval> | undefined;

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly extensionPath: string,
        private readonly delegate: WelcomeDataDelegate,
    ) {}

    /** Open (or reveal) the Welcome tab. */
    public open(): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One, false);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.welcome',
            'Welcome',
            vscode.ViewColumn.One,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [this.extensionUri],
            }
        );
        applyCodeDesignerPanelIcon(panel, this.extensionUri, 'sparkles');
        this._panel = panel;

        panel.onDidDispose(() => {
            this._panel = undefined;
            if (this._pollTimer) { clearInterval(this._pollTimer); this._pollTimer = undefined; }
        });

        panel.webview.onDidReceiveMessage(async (msg: Record<string, unknown>) => {
            await this._handleMessage(msg, panel.webview);
        });

        panel.webview.html = this._buildHtml(panel.webview);

        // Re-push status every 4s so the bridge / emulator dots stay live.
        this._pollTimer = setInterval(() => {
            if (this._panel) { void this._pushStatus(this._panel.webview); }
        }, 4000);
    }

    /** Push a fresh status snapshot whenever bridge/emulator/game state changes. */
    public notifyStatusChanged(): void {
        if (this._panel) { void this._pushStatus(this._panel.webview); }
    }

    // ── Message dispatch ──────────────────────────────────────────────────

    private async _handleMessage(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        const type = msg['type'] as string | undefined;

        switch (type) {
            case 'ready':
                await this._pushInit(webview);
                await this._pushStatus(webview);
                break;
            case 'refreshStatus':
                await this._pushStatus(webview);
                break;
            case 'connectEmulator':
                this.delegate.connectEmulator();
                break;
            case 'openProject':
                await this._openProjectFolder();
                break;
            case 'openCommand':
                {
                    const cmd = msg['command'] as string;
                    if (cmd) {
                        try { await vscode.commands.executeCommand(cmd); }
                        catch (err: any) {
                            getOutputChannel().warn(`[Welcome] Command ${cmd} failed: ${err?.message ?? err}`);
                        }
                    }
                }
                break;
            case 'openDoc':
                {
                    const rel = msg['path'] as string;
                    if (rel) { await this._openDoc(rel); }
                }
                break;
            case 'openDocsRoot':
                await this._openDoc('docs/README.md');
                break;
            case 'newCdsFile':
                await this._newCdsFile();
                break;
            case 'newCdsProject':
                await this._newCdsProject();
                break;
            default:
                getOutputChannel().warn(`[Welcome] Unknown message type: ${type}`);
        }
    }

    // ── Init / Status payloads ────────────────────────────────────────────

    private async _pushInit(webview: vscode.Webview): Promise<void> {
        const iconUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'resources', 'codedesigner-icon.png'),
        ).toString();

        webview.postMessage({
            type:        'init',
            iconUri,
            version:     this._resolveVersion(),
            walkthroughs: this._listDocs(),
            tools:       this._tools(),
            whatsNewHtml: this._latestWhatsNewHtml(),
            keyboard:    this._keyboardTips(),
        });
    }

    private async _pushStatus(webview: vscode.Webview): Promise<void> {
        let emuRunning = false;
        try { emuRunning = await this.delegate.isEmulatorRunning(); } catch { /* ignore */ }

        webview.postMessage({
            type:                'status',
            bridgeConnected:     this.delegate.isBridgeConnected(),
            emulatorPath:        this.delegate.getSelectedEmulatorPath(),
            emulatorLabel:       this.delegate.getSelectedEmulatorLabel(),
            emulatorRunning:     emuRunning,
            biosLabel:           this.delegate.getSelectedBiosLabel(),
            gameTitle:           this.delegate.getLoadedGameTitle(),
            gameSerial:          this.delegate.getLoadedGameSerial(),
        });
    }

    // ── Version ──────────────────────────────────────────────────────────

    /**
     * Resolve the version shown in the Welcome header.
     *
     * Resolution order:
     *   1. `version` key in `codedesigner-settings.json` if the user has set
     *      one explicitly (lets release packagers brand the page without
     *      rebuilding the extension).
     *   2. The extension's own `package.json` version as a fallback.
     */
    private _resolveVersion(): string {
        // 1. User settings — first hit from the same locations the disasm
        //    panel searches when reading codedesigner-settings.json.
        const root = this._resolveDocsRoot();
        const candidates: string[] = [];
        if (root) {
            candidates.push(
                path.join(root, 'codedesigner-settings.json'),
                path.join(root, 'CodeDesigner.Ps2.CdsLanguage', 'StdLib', 'codedesigner-settings.json'),
                path.join(this.extensionPath, 'codedesigner-settings.json'),
            );
        }
        for (const folder of vscode.workspace.workspaceFolders ?? []) {
            candidates.push(path.join(folder.uri.fsPath, 'codedesigner-settings.json'));
        }
        for (const p of candidates) {
            try {
                const raw = fs.readFileSync(p, 'utf-8');
                const parsed = JSON.parse(raw) as { version?: string };
                if (typeof parsed.version === 'string' && parsed.version.trim()) {
                    return parsed.version.trim();
                }
            } catch { /* file missing / parse error — try next */ }
        }

        // 2. Extension package.json fallback.
        try {
            const pkg = JSON.parse(fs.readFileSync(path.join(this.extensionPath, 'package.json'), 'utf-8'));
            return pkg.version ?? '';
        } catch {
            return '';
        }
    }

    // ── Docs / walkthroughs ──────────────────────────────────────────────

    /**
     * Resolve the repository root that contains the `docs/` folder. The
     * extension lives at <repo>/vscode/codedesigner/, so walk up from the
     * extension directory looking for `docs/`.
     */
    private _resolveDocsRoot(): string | null {
        const roots = new Set<string>();
        if (this.extensionPath) { roots.add(this.extensionPath); }
        for (const folder of vscode.workspace.workspaceFolders ?? []) {
            roots.add(folder.uri.fsPath);
        }
        if (this.extensionPath) {
            let dir = this.extensionPath;
            for (let i = 0; i < 5; i++) {
                const parent = path.dirname(dir);
                if (parent === dir) { break; }
                roots.add(parent);
                dir = parent;
            }
        }
        for (const r of roots) {
            const candidate = path.join(r, 'docs');
            if (fs.existsSync(candidate) && fs.statSync(candidate).isDirectory()) {
                return r;
            }
        }
        return null;
    }

    private _listDocs(): DocEntry[] {
        const root = this._resolveDocsRoot();
        if (!root) { return []; }
        const docsDir = path.join(root, 'docs');
        try {
            const files = fs.readdirSync(docsDir);
            const entries: DocEntry[] = [];
            for (const f of files) {
                if (!f.toLowerCase().endsWith('.md')) { continue; }
                const full = path.join(docsDir, f);
                let mtimeMs = 0;
                try { mtimeMs = fs.statSync(full).mtimeMs; } catch { /* ignore */ }
                entries.push({
                    label: this._humanizeDocLabel(f),
                    relPath: `docs/${f}`,
                    mtimeMs,
                });
            }
            // Newest first.
            entries.sort((a, b) => b.mtimeMs - a.mtimeMs);
            return entries.slice(0, 6);
        } catch (err: any) {
            getOutputChannel().debug(`[Welcome] Could not list docs/: ${err?.message ?? err}`);
            return [];
        }
    }

    private _humanizeDocLabel(fileName: string): string {
        const base = fileName.replace(/\.md$/i, '');
        // Replace separators with spaces and title-case.
        const spaced = base.replace(/[-_]+/g, ' ').trim();
        return spaced.replace(/\b\w/g, (m) => m.toUpperCase());
    }

    private async _openDoc(relPath: string): Promise<void> {
        const root = this._resolveDocsRoot();
        if (!root) {
            vscode.window.showWarningMessage(`Doc not found: ${relPath}`);
            return;
        }
        const full = path.join(root, relPath);
        if (!fs.existsSync(full)) {
            vscode.window.showWarningMessage(`Doc not found: ${relPath}`);
            return;
        }
        await vscode.commands.executeCommand('markdown.showPreview', vscode.Uri.file(full));
    }

    // ── What's new ────────────────────────────────────────────────────────

    private _latestWhatsNewHtml(): { title: string; html: string } | null {
        const root = this._resolveDocsRoot();
        if (!root) { return null; }
        const dir = path.join(root, 'docs', 'whats-new');
        if (!fs.existsSync(dir)) { return null; }
        try {
            const files = fs.readdirSync(dir).filter(f => f.toLowerCase().endsWith('.md'));
            if (files.length === 0) { return null; }
            // Sort by semantic version descending when filenames look like versions,
            // otherwise fall back to mtime descending.
            files.sort((a, b) => {
                const va = this._parseVersionish(a);
                const vb = this._parseVersionish(b);
                if (va && vb) { return this._compareVersions(vb, va); }
                try {
                    return fs.statSync(path.join(dir, b)).mtimeMs - fs.statSync(path.join(dir, a)).mtimeMs;
                } catch { return 0; }
            });
            const newest = files[0];
            const raw = fs.readFileSync(path.join(dir, newest), 'utf-8');
            return {
                title: newest.replace(/\.md$/i, ''),
                html: renderMarkdown(raw),
            };
        } catch (err: any) {
            getOutputChannel().debug(`[Welcome] Could not read whats-new/: ${err?.message ?? err}`);
            return null;
        }
    }

    private _parseVersionish(fileName: string): number[] | null {
        const m = fileName.replace(/\.md$/i, '').match(/^v?(\d+(?:\.\d+)*)$/);
        if (!m) { return null; }
        return m[1].split('.').map(n => parseInt(n, 10));
    }

    private _compareVersions(a: number[], b: number[]): number {
        const len = Math.max(a.length, b.length);
        for (let i = 0; i < len; i++) {
            const x = a[i] ?? 0, y = b[i] ?? 0;
            if (x !== y) { return x - y; }
        }
        return 0;
    }

    // ── Tools ─────────────────────────────────────────────────────────────

    /**
     * Curated tool list, matching the disassembler panel's surface area.
     * Twelve entries — most useful first; the welcome page may show fewer
     * if the user shrinks the panel.
     */
    private _tools(): ToolEntry[] {
        return [
            { id: 'codedesigner.openLiveMemory',        label: 'Memory Editor',    subtitle: 'Live hex viewer',         icon: 'memorystick' },
            { id: 'workbench.action.files.openFile',    label: 'Disassembler',     subtitle: 'MIPS R5900 + breakpoints', icon: 'code' },
            { id: 'codedesigner.openCheatsEditor',      label: 'Cheats Editor',    subtitle: 'GameShark + frame loop',  icon: 'sparkles' },
            { id: 'codedesigner.recompile',             label: 'Recompile',        subtitle: 'ELF → C++ source',        icon: 'workflow' },
            { id: 'codedesigner.openMemoryCardBrowser', label: 'Memory Card',      subtitle: '.ps2 / .mcd / .mc2',      icon: 'save' },
            { id: 'codedesigner.openHddBrowser',        label: 'HDD Browser',      subtitle: 'Partition images',        icon: 'harddrive' },
            { id: 'codedesigner.openStructureVisualizer', label: 'Structures',     subtitle: 'Vtables + call graphs',   icon: 'gitcompare' },
            { id: 'codedesigner.openCompatBrowser',     label: 'PS2 Compatibility', subtitle: 'Game library + status',  icon: 'disc3' },
            { id: 'codedesigner.openGsFrameViewer',     label: 'Preview',          subtitle: 'Live PCSX2 display',      icon: 'monitor' },
            { id: 'codedesigner.openJokerMaker',        label: 'Controller',       subtitle: 'Joker code maker',        icon: 'gamepad2' },
            { id: 'codedesigner.openAgentActivity',     label: 'AI Activity',      subtitle: 'Agent log + control',     icon: 'bot' },
        ];
    }

    // ── Keyboard tips ─────────────────────────────────────────────────────

    private _keyboardTips(): Array<{ keys: string; label: string }> {
        return [
            { keys: 'Ctrl+Alt+A', label: 'Go to any virtual address' },
            { keys: 'Ctrl+F',     label: 'Search hex / ASCII / text' },
            { keys: 'F9',         label: 'Toggle breakpoint at cursor' },
            { keys: 'Alt+Left',   label: 'Navigate back through history' },
            { keys: 'Ctrl+Alt+R', label: 'Open register inspector' },
            { keys: 'Ctrl+Alt+T', label: 'Open debug tools panel' },
        ];
    }

    // ── New-file / new-project helpers ────────────────────────────────────

    private async _newCdsFile(): Promise<void> {
        const folders = vscode.workspace.workspaceFolders;
        const defaultRoot = folders?.[0]?.uri ?? vscode.Uri.file(this.extensionPath);
        const target = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.joinPath(defaultRoot, 'untitled.cds'),
            filters:    { 'Code Designer Source': ['cds'] },
            title:      'New .cds file',
        });
        if (!target) { return; }
        try {
            await fs.promises.writeFile(target.fsPath, '// New CodeDesigner source file\n', 'utf-8');
            const doc = await vscode.workspace.openTextDocument(target);
            await vscode.window.showTextDocument(doc);
        } catch (err: any) {
            vscode.window.showErrorMessage(`Could not create .cds file: ${err?.message ?? err}`);
        }
    }

    private async _newCdsProject(): Promise<void> {
        const uris = await vscode.window.showOpenDialog({
            canSelectFiles:   false,
            canSelectFolders: true,
            canSelectMany:    false,
            title:            'Choose a location for the new CDS project',
        });
        const parent = uris?.[0];
        if (!parent) { return; }
        const name = await vscode.window.showInputBox({
            prompt:      'Project name',
            placeHolder: 'my-cds-project',
            validateInput: v => v.trim().length === 0 ? 'Project name is required' : null,
        });
        if (!name) { return; }
        const projectDir = path.join(parent.fsPath, name);
        try {
            await fs.promises.mkdir(projectDir, { recursive: true });
            await fs.promises.writeFile(
                path.join(projectDir, 'main.cds'),
                `// ${name}\n// New CodeDesigner project\n`,
                'utf-8'
            );
            await fs.promises.writeFile(
                path.join(projectDir, 'README.md'),
                `# ${name}\n\nCodeDesigner project.\n`,
                'utf-8'
            );
            await vscode.commands.executeCommand('vscode.openFolder', vscode.Uri.file(projectDir), { forceNewWindow: false });
        } catch (err: any) {
            vscode.window.showErrorMessage(`Could not create CDS project: ${err?.message ?? err}`);
        }
    }

    // ── Open folder ───────────────────────────────────────────────────────

    private async _openProjectFolder(): Promise<void> {
        const uris = await vscode.window.showOpenDialog({
            canSelectFiles:   false,
            canSelectFolders: true,
            canSelectMany:    false,
            title:            'Open CodeDesigner project folder',
        });
        const target = uris?.[0];
        if (!target) { return; }
        await vscode.commands.executeCommand('vscode.openFolder', target, { forceNewWindow: false });
    }

    // ── HTML builder ──────────────────────────────────────────────────────

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'webview', 'welcome.js')
        );
        // Same watermark image the disassembler editor uses — surfaced as a
        // CSS variable so the webview can paint it via body::before with
        // identical sizing/opacity rules.
        const bgLogoUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'resources', 'codedesigner-logo.png')
        );
        const nonce = getNonce();
        return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               img-src ${webview.cspSource} data:;
               style-src 'unsafe-inline';
               script-src ${webview.cspSource} 'nonce-${nonce}';">
<style>:root { --cd-bg-logo: url("${bgLogoUri}"); }</style>
<script nonce="${nonce}">${getWebviewErrorHandlerScript()}</script>
</head>
<body>
<app-welcome-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Welcome…</p></app-welcome-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
