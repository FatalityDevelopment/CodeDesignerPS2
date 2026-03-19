import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { getOutputChannel, getDebugChannel } from '../../extension';
import { SocketClient, LabelEntry, DiscElfEntry } from '../../socketClient';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { DisasmEditorProvider } from '../disasmEditor/disasmEditorProvider';

interface EmuSettings {
    bios?: Array<{ name: string; path: string }>;
    games?: Array<{ name: string; path: string }>;
    emulators?: Array<{ type: string; path: string }>;
    pcsx2?: { host?: string; slot?: number };
}

interface UserPreferences {
    selectedEmulator?: string;
    selectedBios?: string;
    selectedGame?: string;
}

interface SnapshotEntry {
    label: string;
    timestamp: string;
    path: string;
    name: string;
    date: string;
    isoName: string;
}

interface SnapshotInfoFile {
    name: string;
    timestamp: string;
    isoName: string;
    regions: Array<{ Label: string; File: string; BaseAddress: number; Size: number }>;
}

/** Memory regions exposed as separate editor windows when the emulator is running. */
const LIVE_REGIONS = [
    // EE Main RAM: shown at the physical base (0x00000000–0x01FFFFFF, 32 MB).
    // Using physical addresses keeps rows consistent with the memory map table
    // regardless of which virtual alias the CPU uses (KSEG0 0x80000000, uncached
    // 0x20100000, or uncached+accelerated 0x30100000).  HandleReadMem accepts
    // physical addresses for the EE region directly.
    { region: 'ee',  address: 0x00000000, size: 32 * 1024 * 1024, label: 'EE Main RAM'  },
    // IOP RAM: shown at the EE physical base (0x1C000000–0x1DFFFFFF, 4 MB).
    // Per the PS2 memory map, IOP RAM is mirrored into EE address space at
    // 0xBC000000 (logical) → 0x1C000000 (physical).  Using the physical base
    // keeps row addresses consistent with the documented memory map table.
    // HandleReadMem translates 0x1C000000+ addresses to IOP RAM offsets via the
    // EeIopMirrorBase subtraction path.
    { region: 'iop', address: 0x1C000000, size:  4 * 1024 * 1024, label: 'IOP RAM'       },
    // BIOS ROM: shown at the physical base (0x1FC00000–0x1FFFFFFF, 4 MB).
    // Both the cached alias (0x9FC00000) and the uncached alias (0xBFC00000) strip
    // to the same physical address via toPhysAddr, so the PC indicator and
    // breakpoints work for whichever alias the CPU uses.  HandleReadMem accepts
    // the physical base directly.
    { region: 'ee',  address: 0x1FC00000, size:  4 * 1024 * 1024, label: 'BIOS ROM'      },
    { region: 'ee',  address: 0x70000000, size:       16 * 1024,  label: 'Scratchpad'    },
];

export class DisasmPanelProvider implements vscode.WebviewViewProvider {
    private webview?: vscode.Webview;
    private emulatorTerminal?: vscode.Terminal;
    private settings: EmuSettings = {};
    private workspaceRoot: string = '';
    private snapshots: SnapshotEntry[] = [];
    private extensionPath: string = '';
    private selectedEmulatorPath: string = '';
    private selectedBiosPath: string = '';
    private selectedGamePath: string = '';
    private selectedGameName: string = '';
    private _paused: boolean = false;
    /** If Run was clicked before socket connect, execute this mode once connected. */
    private _pendingRunMode: 'emulator' | 'pcsx2' | null = null;
    /** True when the current connection is through the PCSX2 PINE bridge. */
    private _bridgeMode: boolean = false;
    private _activeCdsFilePath: string = '';
    private _activeCdsContent: string = '';
    /** Diagnostic collection for CDS compilation errors — shows red squiggles in the editor. */
    private _cdsDiagnostics!: vscode.DiagnosticCollection;

    /** Returns the workspace root directory (used by DisasmEditorProvider for history lookup). */
    public getWorkspaceRoot(): string { return this.workspaceRoot; }
    /** Returns the ISO name derived from the currently selected game path. */
    public getSelectedGameName(): string { return this.selectedGameName; }
    /**
     * Optional callback invoked during CDS compilation to obtain the current set of
     * live-resolved Pointer Manager labels (e.g. "seal.health" → 0x8012AB34).
     * Set by extension.ts after the providers are wired together.
     */
    public getPointerLabels?: () => Promise<LabelEntry[]>;

    constructor(
        private readonly socketClient: SocketClient,
        private readonly editorProvider: DisasmEditorProvider,
        private readonly _extensionUri: vscode.Uri
    ) {}

    // Register the provider with VS Code
    public register(context: vscode.ExtensionContext) {
        this.extensionPath = context.extensionPath;

        // Create the diagnostic collection for CDS compilation errors.
        // Adding it to context.subscriptions ensures it is disposed (and all
        // markers cleared) when the extension is deactivated.
        this._cdsDiagnostics = vscode.languages.createDiagnosticCollection('cds');
        context.subscriptions.push(this._cdsDiagnostics);

        context.subscriptions.push(
            vscode.window.registerWebviewViewProvider('disasm.disasmPanel', this)
        );

        // Reload settings whenever emu-settings.json is created or changed
        const watcher = vscode.workspace.createFileSystemWatcher('**/emu-settings.json');
        watcher.onDidCreate(() => this.reloadAndRefresh());
        watcher.onDidChange(() => this.reloadAndRefresh());
        watcher.onDidDelete(() => this.reloadAndRefresh());
        context.subscriptions.push(watcher);

        // Reload when the user opens a workspace folder after the panel is shown
        context.subscriptions.push(
            vscode.workspace.onDidChangeWorkspaceFolders(() => this.reloadAndRefresh())
        );

        // Detect when the emulator terminal is closed externally
        context.subscriptions.push(
            vscode.window.onDidCloseTerminal(t => {
                if (t === this.emulatorTerminal) {
                    getOutputChannel().info('Emulator terminal closed.');
                    try { this.socketClient.disconnect(); } catch { /* ignore */ }
                    this.cleanupProcess();
                }
            })
        );

        // Track active .cds file so the CDS tab can compile it on demand
        context.subscriptions.push(
            vscode.window.onDidChangeActiveTextEditor(editor => {
                this._syncActiveCdsEditor(editor);
            })
        );
        // Sync content when the document is modified
        context.subscriptions.push(
            vscode.workspace.onDidChangeTextDocument(e => {
                if (e.document.uri.fsPath === this._activeCdsFilePath) {
                    this._activeCdsContent = e.document.getText();
                }
            })
        );
        // Clear stale diagnostics when a .cds file is closed
        context.subscriptions.push(
            vscode.workspace.onDidCloseTextDocument(doc => {
                if (doc.uri.fsPath.toLowerCase().endsWith('.cds')) {
                    this._cdsDiagnostics.delete(doc.uri);
                    // If this was the cached file, reset the cache too
                    if (doc.uri.fsPath === this._activeCdsFilePath) {
                        this._activeCdsFilePath = '';
                        this._activeCdsContent  = '';
                    }
                }
            })
        );
    }

    resolveWebviewView(webviewView: vscode.WebviewView): void {
        this.webview = webviewView.webview;

        // Load emu-settings.json (searches workspace folders then walks up from
        // the extension directory, so this works even when no workspace is open).
        this.loadEmuSettings();
        // Restore saved dropdown selections from settings.json (written alongside emu-settings.json).
        this.loadUserPreferences();

        // Setup webview options
        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [this._extensionUri]
        };

        // Render the Angular shell.
        // NOTE: Do NOT call postInitMessage() here — wait for the Angular component
        // to send { command: 'ready' } before sending init data, so nothing is lost
        // during the async Angular bootstrap.
        webviewView.webview.html = this.getHtml(webviewView.webview);

        // Listen to messages from the webview
        webviewView.webview.onDidReceiveMessage(async msg => {
            if (msg.type === 'bootstrapError') {
                getOutputChannel().error(
                    `Disasm panel Angular bootstrap failed: ${msg.error ?? '(no details)'}`
                );
                return;
            }
            switch (msg.command) {
                case 'ready':
                    // Angular component has bootstrapped and its message listener
                    // is registered — now safe to send the initial settings.
                    this.postInitMessage();
                    break;
                case 'connect':
                    this.handleConnect(msg.emulatorPath, msg.biosPath, msg.gamePath);
                    break;
                case 'run':
                    this.handleRun(
                        msg.emulatorPath ?? this.selectedEmulatorPath,
                        msg.biosPath ?? this.selectedBiosPath,
                        msg.gamePath ?? this.selectedGamePath
                    );
                    break;
                case 'startPcsx2':
                    this.handleStartPcsx2(
                        msg.emulatorPath ?? this.selectedEmulatorPath,
                        msg.biosPath ?? this.selectedBiosPath,
                        msg.gamePath ?? this.selectedGamePath
                    );
                    break;
                case 'snapshot':
                    await this.handleSnapshot();
                    break;
                case 'snapshotPcsx2':
                    await this.handleSnapshotPcsx2();
                    break;
                case 'importBin':
                    await this.handleImportBin();
                    break;
                case 'openSnapshot':
                    await this.handleOpenSnapshot(msg.path);
                    break;
                case 'reloadSnapshot':
                    await this.handleLoadSnapshot(msg.path);
                    break;
                case 'restart':
                    await this.handleRestart();
                    break;
                case 'step':
                    await this.handleStep();
                    break;
                case 'stepInto':
                    await this.handleStepInto();
                    break;
                case 'stepOver':
                    await this.handleStepOver();
                    break;
                case 'togglePause':
                    await this.handleTogglePause();
                    break;
                // Dropdown change notifications — save current selections
                case 'emulatorChanged':
                    this.selectedEmulatorPath = msg.emulatorPath ?? '';
                    this.saveUserPreferences();
                    break;
                case 'biosChanged':
                    this.selectedBiosPath = msg.biosPath ?? '';
                    this.saveUserPreferences();
                    break;
                case 'gameChanged':
                    await this.handleGameChanged(msg.gamePath ?? '');
                    this.saveUserPreferences();
                    break;
                case 'applyCheats':
                    await this.handleApplyCheats(msg.cheatText ?? '');
                    break;
                case 'compileCds':
                    await this.handleCompileCds();
                    break;
                case 'addPatch':
                    await this.handleAddPatch(msg.codes ?? '');
                    break;
                case 'focusRegion':
                    this.editorProvider.focusLivePanelByLabel(msg.label ?? '');
                    break;
                case 'listDiscElfs':
                    await this.handleListDiscElfs(msg.gamePath ?? this.selectedGamePath);
                    break;
                case 'recompile':
                    await this.handleRecompile(
                        msg.gamePath        ?? this.selectedGamePath,
                        msg.elfName         ?? '',
                        msg.outputDirectory ?? '',
                    );
                    break;
                case 'browseOutputDirectory':
                    await this.handleBrowseOutputDirectory();
                    break;
                case 'browseGame':
                    await this.handleBrowseGame();
                    break;
                case 'browseBios':
                    await this.handleBrowseBios();
                    break;
                case 'browseEmulator':
                    await this.handleBrowseEmulator();
                    break;
            }
        });
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    /** Maximum number of parent directories to climb when searching for emu-settings.json. */
    private static readonly MAX_PARENT_SEARCH_DEPTH = 6;

    private loadEmuSettings(): void {
        // 1. Try every open workspace folder
        for (const folder of vscode.workspace.workspaceFolders ?? []) {
            if (this.tryLoadSettingsFrom(path.join(folder.uri.fsPath, 'emu-settings.json'))) {
                return;
            }
        }

        // 2. Walk up from the extension install directory (handles the case where
        //    the developer opens vscode/codedesigner directly and the repo root is
        //    a few levels above).
        if (this.extensionPath) {
            let dir = this.extensionPath;
            for (let i = 0; i < DisasmPanelProvider.MAX_PARENT_SEARCH_DEPTH; i++) {
                if (this.tryLoadSettingsFrom(path.join(dir, 'emu-settings.json'))) {
                    return;
                }
                const parent = path.dirname(dir);
                if (parent === dir) { break; }   // reached filesystem root
                dir = parent;
            }
        }

        getOutputChannel().warn('Could not find emu-settings.json in any workspace folder or extension directory tree.');
        this.settings = {};
    }

    /** Attempt to load settings from a specific file path. Returns true on success. */
    private tryLoadSettingsFrom(settingsPath: string): boolean {
        try {
            const raw = fs.readFileSync(settingsPath, 'utf-8');
            this.settings = JSON.parse(raw) as EmuSettings;
            // Keep workspaceRoot in sync with wherever the file was found
            this.workspaceRoot = path.dirname(settingsPath);
            return true;
        } catch (err: any) {
            // ENOENT (file not found) is expected during the directory walk; only
            // log unexpected errors (e.g. parse failure, permission denied).
            if (err?.code !== 'ENOENT') {
                getOutputChannel().debug(`Could not read ${settingsPath}: ${err?.message ?? err}`);
            }
            return false;
        }
    }

    /** Reload settings and update the Angular component via postMessage. */
    private reloadAndRefresh(): void {
        this.loadEmuSettings();
        this.loadUserPreferences();
        this.postInitMessage();
    }

    /** Returns the path to codedesigner-settings.json in the workspace root directory. */
    private get settingsJsonPath(): string {
        return this.workspaceRoot ? path.join(this.workspaceRoot, 'codedesigner-settings.json') : '';
    }

    /** Load saved dropdown selections from codedesigner-settings.json in the workspace root. */
    private loadUserPreferences(): void {
        const settingsPath = this.settingsJsonPath;
        if (!settingsPath) { return; }
        try {
            const raw = fs.readFileSync(settingsPath, 'utf-8');
            const prefs = JSON.parse(raw) as UserPreferences;
            this.selectedEmulatorPath = prefs.selectedEmulator ?? '';
            this.selectedBiosPath     = prefs.selectedBios     ?? '';
            this.selectedGamePath     = prefs.selectedGame     ?? '';
            this.selectedGameName     = this.selectedGamePath
                ? path.basename(this.selectedGamePath, path.extname(this.selectedGamePath))
                : '';
            this.loadSnapshotsForGame(this.selectedGameName);
            this.loadGameLabels(this.selectedGameName);
        } catch (err: any) {
            if (err?.code !== 'ENOENT') {
                getOutputChannel().debug(`Could not read ${settingsPath}: ${err?.message ?? err}`);
            }
        }
    }

    /** Save the current emu-settings.json back to the workspace root directory. */
    private async saveEmuSettings(): Promise<void> {
        if (!this.workspaceRoot) { return; }
        const settingsPath = path.join(this.workspaceRoot, 'emu-settings.json');
        try {
            await fs.promises.writeFile(settingsPath, JSON.stringify(this.settings, null, 2), 'utf-8');
        } catch (err: any) {
            getOutputChannel().debug(`Could not save ${settingsPath}: ${err?.message ?? err}`);
        }
    }

    /** Save current dropdown selections to codedesigner-settings.json in the workspace root. */
    private async saveUserPreferences(): Promise<void> {
        const settingsPath = this.settingsJsonPath;
        if (!settingsPath) { return; }
        const prefs: UserPreferences = {
            selectedEmulator: this.selectedEmulatorPath,
            selectedBios:     this.selectedBiosPath,
            selectedGame:     this.selectedGamePath,
        };
        try {
            await fs.promises.writeFile(settingsPath, JSON.stringify(prefs, null, 2), 'utf-8');
        } catch (err: any) {
            getOutputChannel().debug(`Could not save ${settingsPath}: ${err?.message ?? err}`);
        }
    }

    private postInitMessage(): void {
        if (!this.webview) { return; }
        const emulators = (this.settings.emulators ?? []).map(e => ({ label: e.type, value: e.path }));
        const biosOptions = (this.settings.bios ?? []).map(b => ({ label: b.name, value: b.path }));
        const gameOptions = (this.settings.games ?? []).map(g => ({ label: g.name, value: g.path }));
        this.webview.postMessage({
            command: 'init',
            emulators,
            biosOptions,
            gameOptions,
            snapshots: this.snapshots,
            selectedGameName: this.selectedGameName,
            isConnected: this.socketClient.isConnected(),
            isPaused: this._paused,
            selectedEmulator: this.selectedEmulatorPath,
            selectedBios: this.selectedBiosPath,
            selectedGame: this.selectedGamePath,
        });
    }

    private getHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'dist', 'webview', 'disasm-panel.js')
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
<app-disasm-panel><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading...</p></app-disasm-panel>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }

    // ── Emulator launch ───────────────────────────────────────────────────────

    private isPcsx2Entry(entry: { type?: string; path: string }): boolean {
        const type = entry.type?.toLowerCase() ?? '';
        const p = entry.path.toLowerCase();
        return p.includes('pcsx2') || type.includes('pcsx2');
    }

    private isPcsx2Selection(emulatorPath: string): boolean {
        if (!emulatorPath) { return false; }
        const lower = emulatorPath.toLowerCase();
        if (lower.includes('pcsx2')) { return true; }
        const resolved = this.resolvePath(emulatorPath).toLowerCase();
        const entry = (this.settings.emulators ?? []).find(e =>
            this.resolvePath(e.path).toLowerCase() === resolved
        );
        return entry ? this.isPcsx2Entry(entry) : false;
    }

    private getControlServerExecutablePath(selectedEmulatorPath?: string): string | null {
        if (selectedEmulatorPath) {
            const selectedEntry = (this.settings.emulators ?? []).find(e =>
                this.resolvePath(e.path).toLowerCase() === this.resolvePath(selectedEmulatorPath).toLowerCase()
            );
            if (selectedEntry && !this.isPcsx2Entry(selectedEntry)) {
                return this.resolvePath(selectedEntry.path);
            }
        }

        const fallback = (this.settings.emulators ?? []).find(e => !this.isPcsx2Entry(e));
        if (!fallback) { return null; }
        return this.resolvePath(fallback.path);
    }

    private handleConnect(emulatorPath: string, biosPath: string, gamePath: string): void {
        const out = getOutputChannel();

        // Save current selections for use by Restart
        this.selectedEmulatorPath = emulatorPath;
        this.selectedBiosPath = biosPath;
        this.selectedGamePath = gamePath;

        // Toggle: if connected and/or launched from this panel, disconnect and close process.
        if (this.socketClient.isConnected() || this.emulatorTerminal) {
            if (this.emulatorTerminal) {
                out.info('Stopping emulator process...');
                this.emulatorTerminal.dispose();
            }
            out.info('Disconnected from server.');
            this.socketClient.disconnect();
            this.cleanupProcess();
            return;
        }

        if (!emulatorPath) {
            vscode.window.showWarningMessage('Please select an emulator.');
            return;
        }

        out.show(true);
        this._pendingRunMode = null;
        this.launchControlServerAndConnect(emulatorPath);
    }

    /** Start the selected runtime mode via socket after Connect has established control-server link. */
    private handleRun(emulatorPath: string, biosPath: string, gamePath: string): void {
        const out = getOutputChannel();

        this.selectedEmulatorPath = emulatorPath;
        this.selectedBiosPath = biosPath;
        this.selectedGamePath = gamePath;

        if (!emulatorPath) {
            vscode.window.showWarningMessage('Please select an emulator.');
            return;
        }

        out.show(true);
        const runMode: 'emulator' | 'pcsx2' = this.isPcsx2Selection(emulatorPath) ? 'pcsx2' : 'emulator';

        // If already connected, switch runtime mode immediately over socket.
        if (this.socketClient.isConnected()) {
            this.startRuntimeMode(runMode, biosPath, gamePath);
            return;
        }

        // Not connected yet: launch control server, then run mode once connected.
        this._pendingRunMode = runMode;
        this.launchControlServerAndConnect(emulatorPath);
    }

    /** Launch CodeDesigner control server with no mode args, then connect socket/views. */
    private launchControlServerAndConnect(selectedEmulatorPath: string): void {
        const out = getOutputChannel();
        const resolvedExe = this.getControlServerExecutablePath(selectedEmulatorPath);
        if (!resolvedExe) {
            out.error('Connect requires CodeDesigner.Mips64LE.exe in emu-settings.json.');
            vscode.window.showErrorMessage(
                'Connect requires CodeDesigner.Mips64LE.exe in emu-settings.json. Add it and try again.'
            );
            this.cleanupProcess();
            return;
        }

        // If we already launched a terminal process, just retry socket connect.
        if (this.emulatorTerminal) {
            this.connectSocketAndOpenViews();
            return;
        }

        // Verify the executable exists before trying to launch it.
        if (!fs.existsSync(resolvedExe)) {
            out.error(`CodeDesigner executable not found: ${resolvedExe}`);
            vscode.window.showErrorMessage(
                `CodeDesigner not found: ${resolvedExe}\n` +
                'Build the project first: cd CodeDesigner.Mips64LE && dotnet build'
            );
            this.cleanupProcess();
            return;
        }

        out.info(`Starting control server: ${resolvedExe}`);

        const terminal = vscode.window.createTerminal({
            name: 'CodeDesigner',
            shellPath: resolvedExe,
            shellArgs: [],
        });
        terminal.show(true);

        this.emulatorTerminal = terminal;
        this.connectSocketAndOpenViews();
    }

    /** Formats a Date into a filesystem-safe snapshot folder name. */
    private static formatSnapshotTimestamp(d: Date): string {
        return d.toISOString()
            .replace(/[:.]/g, '-')
            .replace('T', '_')
            .slice(0, 19);          // YYYY-MM-DD_HH-MM-SS
    }

    /** Polls until the socket server is up (max ~10 s), then opens region windows. */
    private connectSocketAndOpenViews(): void {
        const out = getOutputChannel();
        const maxAttempts = 20;
        let attempts = 0;

        const tryConnect = async () => {
            attempts++;
            try {
                if (!this.socketClient.isConnected()) {
                    await this.socketClient.connect();
                }
                out.info('Socket connected to emulator. Opening memory views…');
                for (const r of LIVE_REGIONS) {
                    await this.editorProvider.openLiveMemoryView(
                        r.region, r.address, r.size, r.label);
                }
            } catch {
                if (attempts < maxAttempts) {
                    setTimeout(tryConnect, 500);
                } else {
                    out.warn('Could not connect to emulator socket server.');
                }
            }
        };

        // Give the emulator ~1 s to start listening before first attempt
        setTimeout(tryConnect, 1000);
    }

    private cleanupProcess(): void {
        this.emulatorTerminal = undefined;
        this._paused = false;
        this._bridgeMode = false;
        this._pendingRunMode = null;
        this.webview?.postMessage({ command: 'connectionStatus', connected: false });
    }

    private startRuntimeMode(mode: 'emulator' | 'pcsx2', biosPath: string, gamePath: string): void {
        const out = getOutputChannel();
        if (!this.socketClient.isConnected()) {
            out.warn('Run requested before socket connection is ready.');
            return;
        }

        void (async () => {
            try {
                if (mode === 'emulator') {
                    const resolvedBios = biosPath ? this.resolvePath(biosPath) : undefined;
                    const resolvedDisc = gamePath ? this.resolvePath(gamePath) : undefined;
                    out.info('Sending STARTEMULATOR command to control server...');
                    await this.socketClient.startEmulator(resolvedBios, resolvedDisc);
                    this._bridgeMode = false;
                    await this._registerBreakpointsAndResume(true);
                } else {
                    const host = (this.settings.pcsx2?.host ?? '127.0.0.1').trim() || '127.0.0.1';
                    const slot = this.settings.pcsx2?.slot ?? 28011;
                    out.info(`Sending STARTPCSX2 command to control server (${host}:${slot})...`);
                    await this.socketClient.startPcsx2(host, slot);
                    this._bridgeMode = true;
                    this._setPauseState(false);
                }
            } catch (err: any) {
                out.error(`Run failed: ${err?.message ?? err}`);
                vscode.window.showErrorMessage(`Run failed: ${err?.message ?? err}`);
            }
        })();
    }

    /**
     * Launch the external PCSX2 application using CLI arguments from panel selections.
     * This does not connect sockets by itself; Connect/Run still control CodeDesigner.
     */
    private handleStartPcsx2(emulatorPath: string, biosPath: string, gamePath: string): void {
        const out = getOutputChannel();

        this.selectedEmulatorPath = emulatorPath;
        this.selectedBiosPath = biosPath;
        this.selectedGamePath = gamePath;

        if (!emulatorPath || !this.isPcsx2Selection(emulatorPath)) {
            vscode.window.showWarningMessage('Please select a PCSX2 emulator entry first.');
            return;
        }

        const pcsx2Exe = this.resolvePath(emulatorPath);
        if (!fs.existsSync(pcsx2Exe)) {
            out.error(`PCSX2 executable not found: ${pcsx2Exe}`);
            vscode.window.showErrorMessage(`PCSX2 executable not found: ${pcsx2Exe}`);
            return;
        }

        const args: string[] = [];
        const resolvedDisc = gamePath ? this.resolvePath(gamePath) : '';
        const resolvedBios = biosPath ? this.resolvePath(biosPath) : '';

        /* if (resolvedBios && fs.existsSync(resolvedBios)) {
            out.info(`PCSX2 BIOS selection: ${resolvedBios}`);
            args.push('-bios', resolvedBios);
        } */

        if (resolvedDisc && fs.existsSync(resolvedDisc)) {
            out.info(`PCSX2 game selection: ${resolvedDisc}`);
            args.push('-batch', resolvedDisc);
        } else {
            out.warn('No valid game selected for PCSX2 launch; starting without a boot image.');
        }

        args.push('-earlyconsolelog');

        const preview = [pcsx2Exe, ...args]
            .map(v => v.includes(' ') ? `"${v}"` : v)
            .join(' ');
        out.info(`Starting PCSX2: ${preview}`);

        const terminal = vscode.window.createTerminal({
            name: 'PCSX2',
            shellPath: pcsx2Exe,
            shellArgs: args,
        });
        terminal.show(true);
    }

    /**
     * Called when the socket disconnects unexpectedly (server crash / network drop).
     * Updates the sidebar status bar to STOPPED so the user knows the connection
     * is gone even without manually clicking Disconnect.
     *
     * NOTE: this is NOT called for deliberate disconnects (user clicking the
     * Connect/Disconnect toggle) because SocketClient.disconnect() sets
     * wasConnected=false before destroying the socket, which prevents the
     * disconnectionHandlers from firing.  Deliberate disconnects are handled
     * inline in handleConnect() which calls cleanupProcess() directly.
     */
    public notifyDisconnected(): void {
        this.cleanupProcess();
    }

    /** Toggle emulator pause/resume via the socket connection. */
    private async handleTogglePause(): Promise<void> {
        const out = getOutputChannel();
        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator.');
            return;
        }
        try {
            if (this._paused) {
                await this.socketClient.resume();
                this._setPauseState(false);
            } else {
                await this.socketClient.pause();
                this._setPauseState(true);
            }
        } catch (err: any) {
            out.error(`Pause/Resume failed: ${err?.message ?? err}`);
        }
    }

    /**
     * Called by DisasmEditorProvider when the emulator's pause state changes
     * (e.g. a breakpoint was hit).  Updates the sidebar status bar accordingly.
     * Does NOT call back into editorProvider to avoid circular notifications.
     */
    public notifyPauseState(paused: boolean): void {
        this._paused = paused;
        this.webview?.postMessage({ command: 'paused', paused });
    }

    /**
     * Update the internal pause flag, post a 'paused' message to the webview,
     * and notify all live disasm editor panels — all three in sync.
     * Use this whenever the panel itself drives a pause-state change.
     */
    private _setPauseState(paused: boolean): void {
        this._paused = paused;
        this.webview?.postMessage({ command: 'paused', paused });
        this.editorProvider.notifyPauseState(paused);
    }

    /**
     * Execute one EE instruction (single-step) while staying paused.
     * Delegates to DisasmEditorProvider.stepAndBroadcast() which fetches the
     * new PC + registers and broadcasts to all live panels.
     */
    private async handleStep(): Promise<void> {
        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator.');
            return;
        }
        await this.editorProvider.stepAndBroadcast();
    }

    /** PCSX2 PINE: step into (non-blocking). Marks sidebar as running until next pause event. */
    private async handleStepInto(): Promise<void> {
        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator.');
            return;
        }
        try {
            await this.socketClient.stepInto();
            this._setPauseState(false);
        } catch (err: any) {
            getOutputChannel().error(`Step Into failed: ${err?.message ?? err}`);
        }
    }

    /** PCSX2 PINE: step over (non-blocking). Marks sidebar as running until next pause event. */
    private async handleStepOver(): Promise<void> {
        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator.');
            return;
        }
        try {
            await this.socketClient.stepOver();
            this._setPauseState(false);
        } catch (err: any) {
            getOutputChannel().error(`Step Over failed: ${err?.message ?? err}`);
        }
    }

    /** Restart the emulator: send RESTART command via socket, keeping the connection alive. */
    private async handleRestart(): Promise<void> {
        const out = getOutputChannel();

        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator. Cannot restart.');
            return;
        }

        out.info('Restarting emulator via socket…');

        // Blank live panels so stale content is not shown during the restart
        this.editorProvider.resetLivePanels();

        try {
            await this.socketClient.restart();
            out.info('Emulator restarted — re-opening views and registering breakpoints…');

            // Re-initialize every live panel with fresh data from the restarted
            // scheduler (RAM is freshly zeroed, BIOS ROM is reloaded).
            for (const r of LIVE_REGIONS) {
                await this.editorProvider.openLiveMemoryView(
                    r.region, r.address, r.size, r.label);
            }

            // Re-register all known breakpoints then resume deterministically.
            await this._registerBreakpointsAndResume();
        } catch (err: any) {
            out.error(`Restart failed: ${err?.message ?? err}`);
            vscode.window.showErrorMessage(`Restart failed: ${err?.message ?? err}`);
        }
    }

    /**
     * Re-sends every breakpoint the user has set (tracked in the extension process,
     * not the Angular webview) to the C# server, then optionally sends RESUME.
     *
     * Because each SETBP is awaited, the server has processed all of them before
     * RESUME arrives — the emulator is guaranteed to start with all BPs armed.
     * This replaces the fragile fixed-delay approach that raced Angular bootstrap.
     *
     * @param shouldResume Whether to resume emulator execution after registering BPs.
     *   Pass false to register BPs while keeping the emulator paused (e.g. on connect).
     *   Pass true (default) after the user clicks Run or after a Restart.
     */
    private async _registerBreakpointsAndResume(shouldResume: boolean = true): Promise<void> {
        const out = getOutputChannel();
        if (!this.socketClient.isConnected()) { return; }
        const bps = this.editorProvider.getKnownBreakpoints();
        for (const bp of bps) {
            try {
                await this.socketClient.setBreakpoint(bp);
            } catch (err: any) {
                // Socket dropped mid-loop — stop registering; the next connect will retry.
                out.warn(`Breakpoint registration interrupted at 0x${bp.toString(16).toUpperCase()}: ${err?.message ?? err}`);
                return;
            }
        }
        if (bps.size > 0) {
            out.info(`Registered ${bps.size} breakpoint(s) on server.`);
        }
        if (shouldResume) {
            this.socketClient.resume().catch((err: any) => {
                // Ignore "already running" responses; log anything unexpected.
                const msg: string = err?.message ?? String(err);
                if (!msg.toLowerCase().includes('already')) {
                    out.warn(`Resume after BP registration: ${msg}`);
                }
            });
            this._setPauseState(false);
        }
    }

    // ── Import Binary ─────────────────────────────────────────────────────────

    /**
     * Prompt the user for a .bin/.raw file and a target address, then write
     * the file contents into live emulator memory via WRITEMEM.
     */
    private async handleImportBin(): Promise<void> {
        const out = getOutputChannel();

        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator.');
            return;
        }

        // Step 1: Pick a file
        const uris = await vscode.window.showOpenDialog({
            title: 'Import Binary File into Emulator Memory',
            openLabel: 'Import',
            canSelectMany: false,
            filters: {
                'Binary Files': ['bin', 'raw'],
                'All Files':    ['*'],
            },
        });
        if (!uris || uris.length === 0) { return; }
        const filePath = uris[0].fsPath;

        // Step 2: Prompt for target address (same style as handleEditLabel)
        const addrInput = await vscode.window.showInputBox({
            title:          'Import Binary — Target Address',
            prompt:         'Enter the memory address to load the file at (hex, e.g. 0x80010000)',
            placeHolder:    '0x80010000',
            ignoreFocusOut: true,
            validateInput:  (v) => {
                const cleaned = v.trim().toLowerCase().replace(/^0x/, '');
                return /^[0-9a-f]+$/.test(cleaned) ? null : 'Enter a valid hex address (e.g. 0x80010000)';
            },
        });
        if (addrInput === undefined) { return; } // cancelled

        // Step 3: Prompt for memory region
        const region = await vscode.window.showQuickPick(
            [
                { label: 'EE (Emotion Engine)',  description: 'Main RAM, BIOS, Scratchpad', value: 'ee'  },
                { label: 'IOP (I/O Processor)', description: 'IOP RAM',                    value: 'iop' },
            ],
            {
                title:        'Import Binary — Memory Region',
                placeHolder:  'Select target memory region',
                ignoreFocusOut: true,
            }
        );
        if (!region) { return; } // cancelled

        // Parse address
        const address = parseInt(addrInput.trim().replace(/^0x/i, ''), 16);
        if (isNaN(address)) {
            vscode.window.showErrorMessage('Invalid address.');
            return;
        }

        const label   = path.basename(filePath);
        const addrStr = `0x${address.toString(16).toUpperCase().padStart(8, '0')}`;

        // Get file size for the log message without reading the entire file
        let fileSizeStr = 'unknown size';
        try {
            const stat = fs.statSync(filePath);
            fileSizeStr = `${stat.size} bytes`;
        } catch (err: any) {
            out.warn(`Could not stat file for size: ${err?.message ?? err}`);
        }

        out.info(`Importing ${label} (${fileSizeStr}) → ${addrStr} (${region.value})`);

        try {
            await this.socketClient.loadBinFromFile(filePath, address, region.value);
            out.info(`Import complete — ${fileSizeStr} written.`);
            vscode.window.showInformationMessage(
                `Imported ${label} (${fileSizeStr}) at ${addrStr}.`
            );
        } catch (err: any) {
            out.error(`Import failed: ${err?.message ?? err}`);
            vscode.window.showErrorMessage(`Import failed: ${err?.message ?? err}`);
        }
    }

    // ── Snapshot ──────────────────────────────────────────────────────────────

    private async handleSnapshot(): Promise<void> {
        const out = getOutputChannel();

        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator.');
            return;
        }

        // Ask user for a name for this snapshot
        const snapName = await vscode.window.showInputBox({
            title: 'Name this snapshot',
            prompt: 'Enter a name for this snapshot',
            placeHolder: 'e.g. Before Boss Fight',
            ignoreFocusOut: true,
        });
        if (snapName === undefined) { return; } // cancelled
        const safeName = (snapName.trim() || 'snapshot').replace(/[^a-zA-Z0-9_\-]/g, '_');

        const isoName = this.selectedGameName || 'unknown_game';
        const dateStr = new Date().toISOString().slice(0, 10); // YYYY-MM-DD
        const timestamp = DisasmPanelProvider.formatSnapshotTimestamp(new Date());

        const baseDir = this.workspaceRoot
            ? path.join(this.workspaceRoot, 'snapshots')
            : path.join(require('os').tmpdir(), 'ps2snapshots');
        const snapshotsDir = path.join(baseDir, isoName, dateStr, safeName);

        out.info(`Taking snapshot → ${snapshotsDir}`);

        try {
            const regions = await this.socketClient.snapshot(snapshotsDir);
            out.info(`Snapshot complete — ${regions.length} region(s) written.`);

            // Save snapshot metadata so we can look up history later
            const info: SnapshotInfoFile = {
                name:      safeName,
                timestamp: new Date().toISOString(),
                isoName,
                regions:   regions.map(r => ({
                    Label:       r.Label,
                    File:        path.basename(r.File),
                    BaseAddress: r.BaseAddress,
                    Size:        r.Size,
                })),
            };
            fs.writeFileSync(
                path.join(snapshotsDir, 'snapshot-info.json'),
                JSON.stringify(info, null, 2),
                'utf-8'
            );

            // Re-scan the snapshots directory so the list reflects what is
            // actually on disk (handles externally deleted snapshots too).
            this.loadSnapshotsForGame(isoName);
        } catch (err: any) {
            out.error(`Snapshot failed: ${err?.message ?? err}`);
            vscode.window.showErrorMessage(`Snapshot failed: ${err?.message ?? err}`);
        }
    }

    /**
     * Save PCSX2 emulator state to a named .p2s file via the PINE socket bridge
     * (SAVESTATETOFILE command → PineClient.SaveStateFile opcode 0x22).
     * Only available in PINE mode.
     */
    private async handleSnapshotPcsx2(): Promise<void> {
        const out = getOutputChannel();

        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator.');
            return;
        }

        const isoName = this.selectedGameName || 'unknown_game';
        const baseDir = this.workspaceRoot
            ? path.join(this.workspaceRoot, 'snapshots')
            : path.join(require('os').tmpdir(), 'ps2snapshots');

        // Prompt user for a save-state name (used as the .p2s file name).
        const snapName = await vscode.window.showInputBox({
            title: 'Save PCSX2 State',
            prompt: 'Enter a name for this save state',
            placeHolder: 'e.g. before_boss_fight',
            ignoreFocusOut: true,
        });
        if (snapName === undefined) { return; } // cancelled

        const safeName = (snapName.trim() || 'savestate').replace(/[^a-zA-Z0-9_\-]/g, '_');
        const dateStr = new Date().toISOString().slice(0, 10);
        const saveDir = path.join(baseDir, isoName, dateStr);
        try { fs.mkdirSync(saveDir, { recursive: true }); } catch (mkErr: any) {
            if (mkErr?.code !== 'EEXIST') {
                out.error(`Could not create save-state directory: ${mkErr?.message ?? mkErr}`);
                vscode.window.showErrorMessage(`Could not create save-state directory: ${mkErr?.message ?? mkErr}`);
                return;
            }
        }

        const filePath = path.join(saveDir, `${safeName}.p2s`);
        out.info(`Saving PCSX2 state → ${filePath}`);

        try {
            await this.socketClient.saveStateFile(filePath);
            out.info(`PCSX2 state saved to '${filePath}'.`);
            vscode.window.showInformationMessage(`PCSX2 state saved: ${path.basename(filePath)}`);

            // Extract memory dumps from the .p2s archive so they appear in snapshot
            // history. This uses the C# server's existing ZipFile extraction code.
            try {
                await this.socketClient.extractP2s(filePath);
                out.info(`PCSX2 state extracted for history: ${filePath}.extracted`);
            } catch (exErr: any) {
                out.warn(`Could not extract .p2s for history (${filePath}): ${exErr?.message ?? exErr}`);
            }

            // Refresh the snapshot list so the new entry is visible immediately.
            this.loadSnapshotsForGame(isoName);
        } catch (err: any) {
            out.error(`PCSX2 save state failed: ${err?.message ?? err}`);
            vscode.window.showErrorMessage(`PCSX2 save state failed: ${err?.message ?? err}`);
        }
    }

    /** Re-open all .bin files from an earlier snapshot folder. */
    private async handleOpenSnapshot(snapshotPath: string): Promise<void> {
        // For PCSX2 .p2s snapshots, open the .bin files from the companion extracted directory.
        const dirPath = snapshotPath.endsWith('.p2s')
            ? snapshotPath + '.extracted'
            : snapshotPath;

        try {
            const files = fs.readdirSync(dirPath)
                .filter(f => f.endsWith('.bin'))
                .map(f => path.join(dirPath, f));

            for (const f of files) {
                const uri = vscode.Uri.file(f);
                await vscode.commands.executeCommand('vscode.openWith', uri, 'disasm.disasmEditor');
            }
        } catch (err: any) {
            vscode.window.showErrorMessage(`Could not open snapshot: ${err?.message ?? err}`);
        }
    }

    /** Reload a snapshot as a save state — writes memory back into the live emulator. */
    private async handleLoadSnapshot(snapshotPath: string): Promise<void> {
        const out = getOutputChannel();

        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator.');
            return;
        }

        const answer = await vscode.window.showWarningMessage(
            'Reload this snapshot? The emulator will be paused and all current memory will be overwritten.',
            { modal: true },
            'Reload'
        );
        if (answer !== 'Reload') { return; }

        out.info(`Reloading snapshot from ${snapshotPath}`);
        try {
            // PCSX2 .p2s snapshots are reloaded via the PINE socket (LOADSTATEFROMFILE).
            // Internal emulator snapshots use the LOADSNAPSHOT memory-restore path.
            if (snapshotPath.endsWith('.p2s')) {
                await this.socketClient.loadStateFile(snapshotPath);
                out.info(`PCSX2 state loaded from '${snapshotPath}'.`);
                vscode.window.showInformationMessage(`PCSX2 state restored: ${path.basename(snapshotPath)}`);
            } else {
                const regions = await this.socketClient.loadSnapshot(snapshotPath);
                out.info(`Snapshot reloaded — ${regions.length} region(s) restored.`);
                vscode.window.showInformationMessage(`Snapshot restored (${regions.length} region(s)). Emulator is paused.`);
            }
        } catch (err: any) {
            out.error(`Reload snapshot failed: ${err?.message ?? err}`);
            vscode.window.showErrorMessage(`Reload snapshot failed: ${err?.message ?? err}`);
        }
    }

    /**
     * Called when the user changes the selected game.
     * Updates selectedGamePath/selectedGameName, loads snapshots for the game,
     * and loads game-specific labels.
     */
    private async handleGameChanged(gamePath: string): Promise<void> {
        this.selectedGamePath = gamePath;
        this.selectedGameName = gamePath
            ? path.basename(gamePath, path.extname(gamePath))
            : '';

        this.loadSnapshotsForGame(this.selectedGameName);
        this.loadGameLabels(this.selectedGameName);
    }

    /**
     * Scan the snapshots/<isoName>/ directory and populate this.snapshots.
     * Handles two snapshot formats:
     *  - Internal emulator: <date>/<name>/ directory with snapshot-info.json + .bin files
     *  - PCSX2 save states: <date>/<name>.p2s file with a companion <name>.p2s.extracted/ directory
     * Previously-deleted snapshots are automatically excluded because this always
     * re-reads the current filesystem state.
     */
    private loadSnapshotsForGame(isoName: string): void {
        if (!isoName || !this.workspaceRoot) {
            this.snapshots = [];
            this.webview?.postMessage({ command: 'updateSnapshots', snapshots: [], gameName: '' });
            return;
        }

        const isoFolder = path.join(this.workspaceRoot, 'snapshots', isoName);
        const entries: SnapshotEntry[] = [];

        try {
            if (!fs.existsSync(isoFolder)) {
                this.snapshots = [];
                this.webview?.postMessage({ command: 'updateSnapshots', snapshots: [], gameName: isoName });
                return;
            }

            // Walk: <isoFolder>/<date>/...
            const dateDirs = fs.readdirSync(isoFolder, { withFileTypes: true })
                .filter(d => d.isDirectory())
                .map(d => d.name)
                .sort()
                .reverse();  // newest date first

            for (const dateDir of dateDirs) {
                const datePath = path.join(isoFolder, dateDir);
                const dirEntries = fs.readdirSync(datePath, { withFileTypes: true });

                // ── Internal-emulator snapshots: sub-directories ──────────────
                for (const dirent of dirEntries.filter(d => d.isDirectory())) {
                    const snapPath = path.join(datePath, dirent.name);
                    const infoPath = path.join(snapPath, 'snapshot-info.json');

                    if (fs.existsSync(infoPath)) {
                        try {
                            const info = JSON.parse(fs.readFileSync(infoPath, 'utf-8')) as SnapshotInfoFile;
                            entries.push({
                                label:     info.name,
                                name:      info.name,
                                timestamp: new Date(info.timestamp).toLocaleTimeString(),
                                path:      snapPath,
                                date:      dateDir,
                                isoName:   info.isoName,
                            });
                        } catch { /* skip malformed info files */ }
                    } else {
                        // Legacy fallback: only include if the directory contains .bin files.
                        const hasBins = fs.readdirSync(snapPath).some(f => f.endsWith('.bin'));
                        if (hasBins) {
                            entries.push({
                                label:     dirent.name,
                                name:      dirent.name,
                                timestamp: '',
                                path:      snapPath,
                                date:      dateDir,
                                isoName,
                            });
                        }
                    }
                }

                // ── PCSX2 save states: .p2s files ─────────────────────────────
                for (const dirent of dirEntries.filter(d => d.isFile() && d.name.endsWith('.p2s'))) {
                    const p2sPath = path.join(datePath, dirent.name);
                    const snapName = path.basename(dirent.name, '.p2s');

                    // Use the extracted companion directory as the snapshot path when
                    // available so the history reader can access memory dumps directly.
                    const extractedDir    = p2sPath + '.extracted';
                    const extractedInfo   = path.join(extractedDir, 'snapshot-info.json');
                    const snapshotDirPath = fs.existsSync(extractedInfo) ? extractedDir : p2sPath;

                    let timestamp = '';
                    try {
                        const stat = fs.statSync(p2sPath);
                        timestamp = stat.mtime.toLocaleTimeString();
                    } catch { /* ignore */ }

                    entries.push({
                        label:     snapName,
                        name:      snapName,
                        timestamp,
                        path:      snapshotDirPath,
                        date:      dateDir,
                        isoName,
                    });
                }
            }
        } catch (err: any) {
            getOutputChannel().warn(`Could not load snapshots for ${isoName}: ${err?.message ?? err}`);
        }

        this.snapshots = entries;
        this.webview?.postMessage({ command: 'updateSnapshots', snapshots: entries, gameName: isoName });
    }

    /**
     * Load game-specific labels from snapshots/<isoName>/labels.json and
     * push them to the editor provider for display in the Labels tab.
     */
    private loadGameLabels(isoName: string): void {
        if (!isoName || !this.workspaceRoot) {
            this.editorProvider.setGameLabels([]);
            return;
        }

        const labelsPath = path.join(this.workspaceRoot, 'snapshots', isoName, 'labels.json');
        if (!fs.existsSync(labelsPath)) {
            this.editorProvider.setGameLabels([]);
            return;
        }

        try {
            const raw = fs.readFileSync(labelsPath, 'utf-8');
            const doc = JSON.parse(raw) as { symbol?: LabelEntry[] };
            const labels = doc.symbol ?? (Array.isArray(doc) ? doc as LabelEntry[] : []);
            this.editorProvider.setGameLabels(labels);
            getOutputChannel().info(`Loaded ${labels.length} game label(s) from ${labelsPath}`);
        } catch (err: any) {
            getOutputChannel().warn(`Could not load game labels: ${err?.message ?? err}`);
            this.editorProvider.setGameLabels([]);
        }
    }

    /**
     * Return all historical 4-byte values at the given virtual address, one per snapshot.
     * Used by DisasmEditorProvider to populate the History tab in the edit overlay.
     */
    public getAddressHistory(address: number): Array<{ name: string; date: string; value: string; disasm?: string }> {
        const results: Array<{ name: string; date: string; value: string; disasm?: string }> = [];

        // Snapshots store physical base addresses (e.g. 0x00000000 for EE Main RAM).
        // Convert any virtual/logical alias to its physical equivalent before
        // doing the offset calculation.
        //   KSEG0/KSEG1 (0x80000000–0xBFFFFFFF): strip top 3 bits → physical
        //   EE RAM uncached mirror (0x20100000–0x21FFFFFF): subtract 0x20000000
        //   EE RAM uncached+accelerated (0x30100000–0x31FFFFFF): subtract 0x30000000
        const addrU32 = address >>> 0;
        let physAddress: number;
        if (addrU32 >= 0x80000000 && addrU32 < 0xC0000000)
            physAddress = (addrU32 & 0x1FFFFFFF) >>> 0;
        else if (addrU32 >= 0x20100000 && addrU32 <= 0x21FFFFFF)
            physAddress = (addrU32 - 0x20000000) >>> 0;
        else if (addrU32 >= 0x30100000 && addrU32 <= 0x31FFFFFF)
            physAddress = (addrU32 - 0x30000000) >>> 0;
        else
            physAddress = addrU32;

        for (const snap of this.snapshots) {
            const infoPath = path.join(snap.path, 'snapshot-info.json');
            if (!fs.existsSync(infoPath)) { continue; }
            try {
                const info = JSON.parse(fs.readFileSync(infoPath, 'utf-8')) as SnapshotInfoFile;
                for (const region of info.regions) {
                    const base = region.BaseAddress >>> 0;
                    const size = region.Size >>> 0;
                    const offset = physAddress - base;
                    if (offset < 0 || offset + 4 > size) { continue; }

                    const binPath = path.join(snap.path, region.File);
                    if (!fs.existsSync(binPath)) { continue; }

                    const fd = fs.openSync(binPath, 'r');
                    const buf = Buffer.alloc(4);
                    const bytesRead = fs.readSync(fd, buf, 0, 4, offset);
                    fs.closeSync(fd);
                    if (bytesRead < 4) { continue; }

                    const val = buf.readUInt32LE(0);
                    results.push({
                        name:  snap.name || snap.label,
                        date:  snap.date,
                        value: val.toString(16).toUpperCase().padStart(8, '0'),
                    });
                    break; // found region for this address; move to next snapshot
                }
            } catch { /* skip on error */ }
        }

        return results;
    }

    /** Resolve a workspace-relative path to an absolute path. */
    private resolvePath(p: string): string {
        if (path.isAbsolute(p)) { return p; }
        return this.workspaceRoot ? path.join(this.workspaceRoot, p) : p;
    }

    /** Apply cheat codes to live emulator memory via the APPLYCHEATS socket command. */
    private async handleApplyCheats(cheatText: string): Promise<void> {
        const out = getOutputChannel();
        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator.');
            return;
        }
        try {
            await this.socketClient.applyCheats(cheatText);
            out.info('Cheats applied successfully.');
            this.webview?.postMessage({ command: 'cheatsApplied', success: true });
        } catch (err: any) {
            out.error(`ApplyCheats error: ${err?.message ?? err}`);
            this.webview?.postMessage({ command: 'cheatsApplied', success: false, error: err?.message ?? String(err) });
        }
    }

    /** Compile the active .cds file and return GameShark codes to the CDS tab. */
    private async handleCompileCds(): Promise<void> {
        const out = getOutputChannel();

        // Always read from the currently active text editor first so clicking
        // Compile always uses whichever .cds tab the user has open.  VS Code
        // preserves vscode.window.activeTextEditor even after a sidebar panel
        // gains focus, so this reliably reflects the file the user had visible
        // before clicking the button.
        const activeEditor = vscode.window.activeTextEditor;
        if (activeEditor?.document.uri.fsPath.toLowerCase().endsWith('.cds')) {
            this._activeCdsFilePath = activeEditor.document.uri.fsPath;
            this._activeCdsContent  = activeEditor.document.getText();
        }

        const sourceCode = this._activeCdsContent;
        const filePath   = this._activeCdsFilePath;
        const fileUri    = filePath ? vscode.Uri.file(filePath) : undefined;

        // Clear any previous diagnostics for this file before every compile run
        if (fileUri) { this._cdsDiagnostics.delete(fileUri); }

        if (!sourceCode) {
            const msg = 'No .cds file is open. Open a Code Designer Source file to compile.';
            out.warn(`CDS compile: ${msg}`);
            this.webview?.postMessage({
                command: 'cdsCompileResult',
                success: false,
                errors: [{ lineNumber: 0, message: msg }]
            });
            return;
        }

        out.info(`CDS compiling: ${filePath ? path.basename(filePath) : '(unknown)'}`);

        if (!this.socketClient.isConnected()) {
            const msg = 'Not connected to emulator server.';
            out.warn(`CDS compile: ${msg}`);
            this.webview?.postMessage({
                command: 'cdsCompileResult',
                success: false,
                errors: [{ lineNumber: 0, message: msg }]
            });
            return;
        }

        try {
            // Resolve pointer-manager labels so CDS source can reference them as
            // :seal.health or @seal.health. Falls back to an empty array when the
            // pointer manager is not wired or the emulator is disconnected.
            let pointerLabels: LabelEntry[] = [];
            if (this.getPointerLabels) {
                try { pointerLabels = await this.getPointerLabels(); } catch { /* ignore — not connected */ }
            }

            const results = await this.socketClient.compileCds(
                sourceCode,
                pointerLabels.length > 0 ? pointerLabels : undefined
            );
            const errors: Array<{ lineNumber: number; message: string }> = [];
            const codeLines: string[] = [];
            const diagnostics: vscode.Diagnostic[] = [];

            // Look up the open document so we can get exact line lengths for ranges.
            // Use URI string comparison (toString()) for reliable cross-platform matching.
            const openDoc = fileUri
                ? vscode.workspace.textDocuments.find(d => d.uri.toString() === fileUri.toString())
                : undefined;

            for (const r of results) {
                if (r.Success && r.Hex) {
                    codeLines.push(r.Hex);
                } else if (!r.Success && r.Error) {
                    const ln = r.LineNumber ?? 0;
                    // Log to the Debug channel with error severity (shows in red)
                    getDebugChannel().error(`Line ${ln}: ${r.Error}`);
                    errors.push({ lineNumber: ln, message: r.Error });

                    // Build a diagnostic that highlights the offending line in the editor
                    if (fileUri && ln > 0) {
                        const lineIndex = ln - 1;   // VS Code ranges are 0-based
                        // Guard against line numbers that exceed the document length
                        const safeIndex = openDoc
                            ? Math.min(lineIndex, openDoc.lineCount - 1)
                            : lineIndex;
                        const lineLen = (openDoc && safeIndex < openDoc.lineCount)
                            ? openDoc.lineAt(safeIndex).text.length
                            : 512;
                        const range = new vscode.Range(safeIndex, 0, safeIndex, lineLen);
                        const diag  = new vscode.Diagnostic(
                            range,
                            `${r.Error}`,
                            vscode.DiagnosticSeverity.Error
                        );
                        diag.source = 'CDS';
                        diagnostics.push(diag);
                    }
                }
            }

            // Push all diagnostics to VS Code so squiggles and gutter icons appear
            if (fileUri) {
                this._cdsDiagnostics.set(fileUri, diagnostics.length > 0 ? diagnostics : []);
            }

            if (errors.length > 0) {
                out.warn(`CDS compilation finished with ${errors.length} error(s).`);
                // Reveal the Debug channel so the user sees the red error entries
                getDebugChannel().show(true);
                this.webview?.postMessage({ command: 'cdsCompileResult', success: false, errors });
            } else {
                const codes = codeLines.join('\n');
                out.info(`CDS compilation succeeded — ${codeLines.length} code(s) generated.`);
                this.webview?.postMessage({ command: 'cdsCompileResult', success: true, codes, errors: [] });
            }
        } catch (err: any) {
            const msg = err?.message ?? String(err);
            getDebugChannel().error(`CDS compile error: ${msg}`);
            getDebugChannel().show(true);
            this.webview?.postMessage({
                command: 'cdsCompileResult',
                success: false,
                errors: [{ lineNumber: 0, message: msg }]
            });
        }
    }

    /** Apply compiled GameShark codes to live memory (Add Patch). */
    private async handleAddPatch(codes: string): Promise<void> {
        const out = getOutputChannel();
        if (!this.socketClient.isConnected()) {
            this.webview?.postMessage({ command: 'cdsPatchApplied', success: false, error: 'Not connected to emulator.' });
            return;
        }
        try {
            await this.socketClient.applyCheats(codes);
            out.info('CDS patch applied to memory successfully.');
            this.webview?.postMessage({ command: 'cdsPatchApplied', success: true });
        } catch (err: any) {
            const msg = err?.message ?? String(err);
            out.error(`CDS add-patch error: ${msg}`);
            this.webview?.postMessage({ command: 'cdsPatchApplied', success: false, error: msg });
        }
    }

    // ── Recompile tab handlers ─────────────────────────────────────────────────

    /**
     * List all ELF and IRX files on the disc image selected as the current game.
     * Posts a 'listDiscElfsResult' message back to the webview.
     */
    private async handleListDiscElfs(gamePath: string): Promise<void> {
        const out = getOutputChannel();
        if (!gamePath) {
            const msg = 'No disc image selected. Choose a game ISO first.';
            out.error(`[Recompile] ${msg}`);
            out.show(true);
            this.webview?.postMessage({
                command: 'listDiscElfsResult',
                success: false,
                error:   msg,
            });
            return;
        }
        const resolvedGamePath = this.resolvePath(gamePath);
        try {
            const { elfFiles, bootElf } = await this.socketClient.listDiscElfs(resolvedGamePath);
            out.info(`[Recompile] Found ${elfFiles.length} ELF/IRX file(s) on disc. BOOT2: ${bootElf || 'unknown'}`);
            this.webview?.postMessage({
                command:  'listDiscElfsResult',
                success:  true,
                elfFiles,
                bootElf,
            });
        } catch (err: any) {
            const msg = err?.message ?? String(err);
            out.error(`[Recompile] LISTDISCELFS error: ${msg}`);
            this.webview?.postMessage({ command: 'listDiscElfsResult', success: false, error: msg });
        }
    }

    /**
     * Extract an ELF from the disc image and run the static recompiler on it.
     * Posts a 'recompileResult' message back to the webview.
     */
    private async handleRecompile(
        gamePath:        string,
        elfName:         string,
        outputDirectory: string,
    ): Promise<void> {
        const out = getOutputChannel();
        if (!gamePath) {
            const errMsg = 'No disc image selected. Choose a game ISO first.';
            out.error(`[Recompile] ${errMsg}`);
            out.show(true);
            this.webview?.postMessage({
                command: 'recompileResult',
                success: false,
                error:   errMsg,
            });
            return;
        }

        const resolvedGamePath = this.resolvePath(gamePath);

        // If no output directory is specified, place generated files next to the ISO.
        const resolvedOutput = outputDirectory.trim()
            ? outputDirectory.trim()
            : require('path').join(require('path').dirname(resolvedGamePath), 'recomp_out');

        out.info(`[Recompile] Starting recompilation: iso=${resolvedGamePath} elf=${elfName || '(BOOT2)'} out=${resolvedOutput}`);
        out.show(true);

        try {
            const result = await this.socketClient.recompile(resolvedGamePath, elfName, resolvedOutput);
            out.info(`[Recompile] Done — ${result.functionCount} function(s) recompiled.`);
            out.info(`[Recompile] Written files:\n${result.writtenFiles.join('\n')}`);
            this.webview?.postMessage({
                command:       'recompileResult',
                success:       true,
                functionCount: result.functionCount,
                writtenFiles:  result.writtenFiles,
                bootElf:       result.bootElf,
            });
        } catch (err: any) {
            const msg = err?.message ?? String(err);
            out.error(`[Recompile] Error: ${msg}`);
            out.show(true);
            this.webview?.postMessage({ command: 'recompileResult', success: false, error: msg });
        }
    }

    /**
     * Open a VS Code folder-picker dialog so the user can select an output directory
     * for the recompiler. Posts 'browseOutputDirectoryResult' back to the webview.
     */
    private async handleBrowseOutputDirectory(): Promise<void> {
        const uris = await vscode.window.showOpenDialog({
            canSelectFiles:   false,
            canSelectFolders: true,
            canSelectMany:    false,
            title:            'Select Output Directory for Generated C++ Files',
        });
        if (uris && uris.length > 0) {
            this.webview?.postMessage({
                command:   'browseOutputDirectoryResult',
                directory: uris[0].fsPath,
            });
        }
    }

    /**
     * Open a VS Code file-picker dialog so the user can select a disc image directly.
     * Accepts plain ISO/BIN images and ZIP archives containing a disc image.
     * Adds the chosen path to emu-settings.json if not already listed, then posts
     * 'browseGameResult' with the updated gameOptions list back to the webview.
     */
    private async handleBrowseGame(): Promise<void> {
        const uris = await vscode.window.showOpenDialog({
            canSelectFiles:   true,
            canSelectFolders: false,
            canSelectMany:    false,
            filters: {
                'Disc Images': ['iso', 'bin', 'img'],
                'ZIP Archives': ['zip'],
                'All Files':    ['*'],
            },
            title: 'Select PS2 Disc Image',
        });
        if (uris && uris.length > 0) {
            const filePath = uris[0].fsPath;
            if (!this.settings.games) { this.settings.games = []; }
            if (!this.settings.games.some(g => g.path === filePath)) {
                const gameName = path.basename(filePath, path.extname(filePath));
                this.settings.games.push({ name: gameName, path: filePath });
                await this.saveEmuSettings();
            }
            const gameOptions = this.settings.games.map(g => ({ label: g.name, value: g.path }));
            this.webview?.postMessage({
                command:     'browseGameResult',
                filePath,
                gameOptions,
            });
        }
    }

    /**
     * Open a VS Code file-picker dialog so the user can select a BIOS image directly.
     * Adds the chosen path to emu-settings.json if not already listed, then posts
     * 'browseBiosResult' with the updated biosOptions list back to the webview.
     */
    private async handleBrowseBios(): Promise<void> {
        const uris = await vscode.window.showOpenDialog({
            canSelectFiles:   true,
            canSelectFolders: false,
            canSelectMany:    false,
            filters: {
                'BIOS Images': ['bin', 'rom'],
                'All Files':   ['*'],
            },
            title: 'Select PS2 BIOS File',
        });
        if (uris && uris.length > 0) {
            const filePath = uris[0].fsPath;
            if (!this.settings.bios) { this.settings.bios = []; }
            if (!this.settings.bios.some(b => b.path === filePath)) {
                const name = path.basename(filePath, path.extname(filePath));
                this.settings.bios.push({ name, path: filePath });
                await this.saveEmuSettings();
            }
            const biosOptions = this.settings.bios.map(b => ({ label: b.name, value: b.path }));
            this.webview?.postMessage({
                command:     'browseBiosResult',
                filePath,
                biosOptions,
            });
        }
    }

    /**
     * Open a VS Code file-picker dialog so the user can select an emulator executable.
     * Adds the chosen path to emu-settings.json if not already listed, then posts
     * 'browseEmulatorResult' with the updated emulators list back to the webview.
     */
    private async handleBrowseEmulator(): Promise<void> {
        const uris = await vscode.window.showOpenDialog({
            canSelectFiles:   true,
            canSelectFolders: false,
            canSelectMany:    false,
            filters: {
                'All Files': ['*'],
            },
            title: 'Select PS2 Emulator Executable',
        });
        if (uris && uris.length > 0) {
            const filePath = uris[0].fsPath;
            if (!this.settings.emulators) { this.settings.emulators = []; }
            if (!this.settings.emulators.some(e => e.path === filePath)) {
                const emulatorName = path.basename(filePath, path.extname(filePath));
                this.settings.emulators.push({ type: emulatorName, path: filePath });
                await this.saveEmuSettings();
            }
            const emulators = this.settings.emulators.map(e => ({ label: e.type, value: e.path }));
            this.webview?.postMessage({
                command:   'browseEmulatorResult',
                filePath,
                emulators,
            });
        }
    }

    /** Sync the tracked active CDS file when the editor focus changes. */
    private _syncActiveCdsEditor(editor: vscode.TextEditor | undefined): void {        
        if (!editor) { return; }
        if (!editor.document.uri.fsPath.toLowerCase().endsWith('.cds')) { return; }
        this._activeCdsFilePath = editor.document.uri.fsPath;
        this._activeCdsContent  = editor.document.getText();
    }

    /**
     * Called when the socket connects (including auto-connect on VS Code startup).
     * Opens live memory views for all regions, reusing any already-open panels.
     * The emulator is left paused — call Resume or click ▶ Run to start execution.
     */
    public async onSocketConnected(): Promise<void> {
        this._setPauseState(false);
        this.webview?.postMessage({ command: 'connectionStatus', connected: true });
        for (const r of LIVE_REGIONS) {
            await this.editorProvider.openLiveMemoryView(r.region, r.address, r.size, r.label);
        }
        const pendingMode = this._pendingRunMode;
        this._pendingRunMode = null;
        if (pendingMode) {
            this.startRuntimeMode(
                pendingMode,
                this.selectedBiosPath,
                this.selectedGamePath
            );
        }
    }

    /**
     * Open all live memory view panels immediately, even if not connected.
     * Panels will show placeholder content until the socket connects.
     * Called at extension activation so tabs are always visible.
     */
    public async openAllLivePanels(): Promise<void> {
        for (const r of LIVE_REGIONS) {
            await this.editorProvider.openLiveMemoryView(r.region, r.address, r.size, r.label);
        }
    }

    /**
    * Auto-start the CodeDesigner console app in default control-server mode.
     * Called at extension activation when auto-connect fails.
     * Looks for the first non-PCSX2 emulator configured in emu-settings.json.
     * Returns true if a terminal was spawned (or was already running), false otherwise.
     */
    public async autoStartServer(): Promise<boolean> {
        // Load settings if not yet done (workspaceRoot is set by loadEmuSettings)
        if (!this.workspaceRoot) {
            this.loadEmuSettings();
        }

        const emulators = this.settings.emulators ?? [];
        if (emulators.length === 0) {
            return false;
        }

        // Prefer the CodeDesigner app over PCSX2
        const emu = emulators.find(e =>
            !e.path.toLowerCase().includes('pcsx2') &&
            !e.type?.toLowerCase().includes('pcsx2')
        );

        if (!emu) {
            getOutputChannel().warn('Auto-start: no CodeDesigner emulator found in emu-settings.json (only PCSX2 entries). Use the Run button to start manually.');
            return false;
        }

        const resolvedExe = this.resolvePath(emu.path);

        if (!fs.existsSync(resolvedExe)) {
            getOutputChannel().warn(`Auto-start: CodeDesigner executable not found at ${resolvedExe}`);
            return false;
        }

        // Don't spawn a second terminal if one is already open
        if (this.emulatorTerminal) {
            getOutputChannel().info('Auto-start: server terminal is already running.');
            return true;
        }

        const cliArgs: string[] = [];

        getOutputChannel().info(`Auto-starting CodeDesigner control server: ${resolvedExe}`);

        const terminal = vscode.window.createTerminal({
            name: 'CodeDesigner',
            shellPath: resolvedExe,
            shellArgs: cliArgs,
        });
        terminal.show(false);   // reveal terminal but keep editor focus
        this.emulatorTerminal = terminal;

        // Poll until the socket server is ready (up to ~10 s), then connect.
        this.connectSocketAndOpenViews();

        return true;
    }
}
