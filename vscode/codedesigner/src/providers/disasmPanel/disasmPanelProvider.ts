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
    // IOP RAM: shown at its native physical base (0x00000000–0x003FFFFF, 4 MB).
    // Using native addresses keeps row addresses consistent with the IOP CPU's PC
    // register and the console debug output, so addresses like 0x0000ae94 map
    // directly to the matching row in the IOP RAM panel.  HandleReadMem reads
    // directly from IopMemoryBus.Ram[] when region='iop' and address < RamSize.
    { region: 'iop', address: 0x00000000, size:  4 * 1024 * 1024, label: 'IOP RAM'       },
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
    /** Set to true by handleRun() so that onSocketConnected() resumes the emulator. */
    private _resumeOnConnect: boolean = false;
    private _activeCdsFilePath: string = '';
    private _activeCdsContent: string = '';
    /** Diagnostic collection for CDS compilation errors — shows red squiggles in the editor. */
    private _cdsDiagnostics!: vscode.DiagnosticCollection;

    /** Returns the workspace root directory (used by DisasmEditorProvider for history lookup). */
    public getWorkspaceRoot(): string { return this.workspaceRoot; }
    /** Returns the ISO name derived from the currently selected game path. */
    public getSelectedGameName(): string { return this.selectedGameName; }

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
                case 'snapshot':
                    await this.handleSnapshot();
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

    private handleConnect(emulatorPath: string, biosPath: string, gamePath: string): void {
        const out = getOutputChannel();

        // Save current selections for use by Restart
        this.selectedEmulatorPath = emulatorPath;
        this.selectedBiosPath = biosPath;
        this.selectedGamePath = gamePath;

        // Toggle: if already running or already socket-connected, stop/disconnect
        if (this.emulatorTerminal || this.socketClient.isConnected()) {
            if (this.emulatorTerminal) {
                this.emulatorTerminal.dispose();
                out.info('Emulator stopped by user.');
            } else {
                out.info('Disconnected from server.');
            }
            this.socketClient.disconnect();
            this.cleanupProcess();
            return;
        }

        if (!emulatorPath) {
            vscode.window.showWarningMessage('Please select an emulator.');
            return;
        }

        out.show(true);

        // Spawn the emulator and connect when ready (same as Run).
        this.spawnAndConnect(emulatorPath, biosPath, gamePath);
    }

    /** Launch the emulator in a VS Code terminal tab and, once the server is up, connect and resume. */
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

        // If already connected, just register breakpoints and resume immediately.
        if (this.socketClient.isConnected()) {
            this._registerBreakpointsAndResume(true);
            return;
        }

        // Not yet connected — spawn the emulator and resume once the socket connects.
        this._resumeOnConnect = true;
        this.spawnAndConnect(emulatorPath, biosPath, gamePath);
    }

    /** Launch the emulator in a VS Code terminal tab and, once the server is up, connect. */
    private spawnAndConnect(emulatorPath: string, biosPath: string, gamePath: string): void {
        const out = getOutputChannel();

        const resolvedExe  = this.resolvePath(emulatorPath);
        const resolvedBios = biosPath ? this.resolvePath(biosPath) : undefined;
        const resolvedDisc = gamePath ? this.resolvePath(gamePath) : undefined;

        // Verify the executable actually exists before trying to launch it.
        // Using shellPath: resolvedExe directly fails silently on some VS Code
        // versions when the file is missing; this gives the user a clear message.
        if (!fs.existsSync(resolvedExe)) {
            out.error(`Emulator executable not found: ${resolvedExe}`);
            vscode.window.showErrorMessage(
                `Emulator not found: ${resolvedExe}\n` +
                'Build the project first: cd CodeDesigner.Mips64LE && dotnet build'
            );
            this.cleanupProcess();
            return;
        }

        const cliArgs: string[] = ['--run'];
        if (resolvedBios) { cliArgs.push('--bios', resolvedBios); }
        if (resolvedDisc) { cliArgs.push('--disc', resolvedDisc); }

        out.info(`Starting emulator: ${resolvedExe} ${cliArgs.join(' ')}`);

        // Use shellPath/shellArgs to pass the exe and its arguments directly to
        // the OS without any shell quoting — this avoids shell-injection concerns
        // and works cross-platform regardless of the user's default shell.
        const terminal = vscode.window.createTerminal({
            name:      'PS2 Emulator',
            shellPath: resolvedExe,
            shellArgs: cliArgs,
        });
        terminal.show(true);          // reveal but keep editor focus

        this.emulatorTerminal = terminal;

        // After the emulator starts the embedded socket server, connect the
        // socket client and open a live memory view for every region.
        // Do NOT send connectionStatus:true here — send it only once the socket
        // actually connects (via onSocketConnected / onConnected event) so the
        // sidebar shows the real connection state and not a premature "LIVE".
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
        this.webview?.postMessage({ command: 'connectionStatus', connected: false });
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

        // Read file and convert to hex string
        let fileBytes: Buffer;
        try {
            fileBytes = fs.readFileSync(filePath);
        } catch (err: any) {
            vscode.window.showErrorMessage(`Could not read file: ${err?.message ?? err}`);
            return;
        }

        const hexData   = fileBytes.toString('hex');
        const label     = path.basename(filePath);
        const addrStr   = `0x${address.toString(16).toUpperCase().padStart(8, '0')}`;

        out.info(`Importing ${label} (${fileBytes.length} bytes) → ${addrStr} (${region.value})`);

        try {
            await this.socketClient.writeMemory(address, hexData, region.value);
            out.info(`Import complete — ${fileBytes.length} byte(s) written.`);
            vscode.window.showInformationMessage(
                `Imported ${label} (${fileBytes.length} bytes) at ${addrStr}.`
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

            // Record in the panel's snapshot list
            const entry: SnapshotEntry = {
                label:     safeName,
                name:      safeName,
                timestamp: new Date().toLocaleTimeString(),
                path:      snapshotsDir,
                date:      dateStr,
                isoName,
            };
            this.snapshots.unshift(entry);
            this.webview?.postMessage({
                command:   'updateSnapshots',
                snapshots: this.snapshots,
            });
        } catch (err: any) {
            out.error(`Snapshot failed: ${err?.message ?? err}`);
            vscode.window.showErrorMessage(`Snapshot failed: ${err?.message ?? err}`);
        }
    }

    /** Re-open all .bin files from an earlier snapshot folder. */
    private async handleOpenSnapshot(snapshotPath: string): Promise<void> {
        try {
            const files = fs.readdirSync(snapshotPath)
                .filter(f => f.endsWith('.bin'))
                .map(f => path.join(snapshotPath, f));

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
            const regions = await this.socketClient.loadSnapshot(snapshotPath);
            out.info(`Snapshot reloaded — ${regions.length} region(s) restored.`);
            vscode.window.showInformationMessage(`Snapshot restored (${regions.length} region(s)). Emulator is paused.`);
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
     * Snapshot entries are stored in snapshot-info.json inside each snapshot folder.
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

            // Walk: <isoFolder>/<date>/<name>/snapshot-info.json
            const dateDirs = fs.readdirSync(isoFolder, { withFileTypes: true })
                .filter(d => d.isDirectory())
                .map(d => d.name)
                .sort()
                .reverse();  // newest date first

            for (const dateDir of dateDirs) {
                const datePath = path.join(isoFolder, dateDir);
                const nameDirs = fs.readdirSync(datePath, { withFileTypes: true })
                    .filter(d => d.isDirectory())
                    .map(d => d.name);

                for (const nameDir of nameDirs) {
                    const snapPath = path.join(datePath, nameDir);
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
                        // Fallback for snapshots without info file (legacy)
                        entries.push({
                            label:     nameDir,
                            name:      nameDir,
                            timestamp: '',
                            path:      snapPath,
                            date:      dateDir,
                            isoName,
                        });
                    }
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
    public getAddressHistory(address: number): Array<{ name: string; date: string; value: string }> {
        const results: Array<{ name: string; date: string; value: string }> = [];

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
                        value: '0x' + val.toString(16).toUpperCase().padStart(8, '0'),
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
            const results = await this.socketClient.compileCds(sourceCode);
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

    /** Sync the tracked active CDS file when the editor focus changes. */
    private _syncActiveCdsEditor(editor: vscode.TextEditor | undefined): void {        if (!editor) { return; }
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
        this._setPauseState(true);
        this.webview?.postMessage({ command: 'connectionStatus', connected: true });
        for (const r of LIVE_REGIONS) {
            await this.editorProvider.openLiveMemoryView(r.region, r.address, r.size, r.label);
        }
        // Register any existing breakpoints on the server.
        // Only resume if handleRun() set the flag (i.e. the user explicitly clicked Run).
        const shouldResume = this._resumeOnConnect;
        this._resumeOnConnect = false;
        await this._registerBreakpointsAndResume(shouldResume);
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
     * Auto-start the CodeDesigner console app with --server in a VS Code terminal.
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

        // Build --run arguments; pass the first configured BIOS path if available.
        // --run starts the embedded socket server on port 8765 (used by this extension).
        // --server starts a standalone server on port 8766 and must NOT be used here.
        const cliArgs: string[] = ['--run'];
        const bios = this.settings.bios?.[0];
        if (bios) {
            const resolvedBios = this.resolvePath(bios.path);
            if (fs.existsSync(resolvedBios)) {
                cliArgs.push('--bios', resolvedBios);
            }
        }

        getOutputChannel().info(`Auto-starting CodeDesigner emulator: ${resolvedExe} ${cliArgs.join(' ')}`);

        const terminal = vscode.window.createTerminal({
            name: 'PS2 Emulator',
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
