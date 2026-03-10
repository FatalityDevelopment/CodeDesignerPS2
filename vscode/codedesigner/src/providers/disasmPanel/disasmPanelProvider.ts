import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { getOutputChannel } from '../../extension';
import { SocketClient, LabelEntry } from '../../socketClient';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { DisasmEditorProvider } from '../disasmEditor/disasmEditorProvider';

interface EmuSettings {
    bios?: Array<{ name: string; path: string }>;
    games?: Array<{ name: string; path: string }>;
    emulators?: Array<{ type: string; path: string }>;
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
    // EE Main RAM: shown at KSEG0 (0x80000000–0x81FFFFFF) so that row addresses
    // match the virtual PC the CPU actually executes from.  Kernel and game code
    // run in KSEG0, so a breakpoint set on a row at 0x80001234 matches the CPU PC
    // directly.  HandleReadMem strips the top 3 bits (KSEG0→physical) server-side.
    { region: 'ee',  address: 0x80000000, size: 32 * 1024 * 1024, label: 'EE Main RAM'  },
    // IOP RAM: shown at its native physical base (0x00000000–0x003FFFFF, 4 MB).
    // Using native addresses keeps row addresses consistent with the IOP CPU's PC
    // register and the console debug output, so addresses like 0x0000ae94 map
    // directly to the matching row in the IOP RAM panel.  HandleReadMem reads
    // directly from IopMemoryBus.Ram[] when region='iop' and address < RamSize.
    { region: 'iop', address: 0x00000000, size:  4 * 1024 * 1024, label: 'IOP RAM'       },
    // BIOS ROM: shown at KSEG0 (0x9FC00000) so row addresses match the kernel
    // PC.  After the initial uncached reset (0xBFC00000), the EE kernel runs
    // almost entirely from the cached KSEG0 alias (0x9FC00000–0x9FFFFFFF).
    // Both KSEG0 and KSEG1 strip to the same physical 0x1FC00000 via toPhysAddr,
    // so cross-region navigation, cursor highlight and breakpoints all work for
    // whichever alias the CPU uses.  HandleReadMem strips the top 3 bits server-side.
    { region: 'ee',  address: 0x9FC00000, size:  4 * 1024 * 1024, label: 'BIOS ROM'      },
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
                    getOutputChannel().appendLine('[INFO] Emulator terminal closed.');
                    try { this.socketClient.disconnect(); } catch { /* ignore */ }
                    this.cleanupProcess();
                }
            })
        );
    }

    resolveWebviewView(webviewView: vscode.WebviewView): void {
        this.webview = webviewView.webview;

        // Load emu-settings.json (searches workspace folders then walks up from
        // the extension directory, so this works even when no workspace is open).
        this.loadEmuSettings();

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
                getOutputChannel().appendLine(
                    `[ERROR] Disasm panel Angular bootstrap failed: ${msg.error ?? '(no details)'}`
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
                case 'snapshot':
                    await this.handleSnapshot();
                    break;
                case 'importBin':
                    await this.handleImportBin();
                    break;
                case 'openSnapshot':
                    await this.handleOpenSnapshot(msg.path);
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
                    break;
                case 'biosChanged':
                    this.selectedBiosPath = msg.biosPath ?? '';
                    break;
                case 'gameChanged':
                    await this.handleGameChanged(msg.gamePath ?? '');
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

        getOutputChannel().appendLine('[WARN] Could not find emu-settings.json in any workspace folder or extension directory tree.');
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
                getOutputChannel().appendLine(`[DEBUG] Could not read ${settingsPath}: ${err?.message ?? err}`);
            }
            return false;
        }
    }

    /** Reload settings and update the Angular component via postMessage. */
    private reloadAndRefresh(): void {
        this.loadEmuSettings();
        this.postInitMessage();
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
                out.appendLine('[INFO] Emulator stopped by user.');
            } else {
                out.appendLine('[INFO] Disconnected from server.');
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

        // Try connecting to an already-running server first. If the C# server
        // was started manually (e.g. `dotnet run -- --server` or `--run`), we
        // can reuse it without spawning a new process.
        // NOTE: do NOT open memory views here — the socketClient.onConnected
        // event (registered in extension.ts) will fire onSocketConnected() which
        // opens/reinits all panels.  Duplicating that work here would queue two
        // concurrent sets of READMEM requests against the server, which (before
        // the _pendingResponses fix) caused response mis-routing and is still
        // wasteful.
        this.socketClient.connect().then(() => {
            out.appendLine('[INFO] Connected to existing emulator server. Memory views will open via onSocketConnected.');
        }).catch(() => {
            // No existing server — spawn the emulator and then connect
            this.spawnAndConnect(emulatorPath, biosPath, gamePath);
        });
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
            out.appendLine(`[ERROR] Emulator executable not found: ${resolvedExe}`);
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

        out.appendLine(`[INFO] Starting emulator: ${resolvedExe} ${cliArgs.join(' ')}`);

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
        this.webview?.postMessage({ command: 'connectionStatus', connected: true });

        // After the emulator starts the embedded socket server, connect the
        // socket client and open a live memory view for every region.
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
                out.appendLine('[INFO] Socket connected to emulator. Opening memory views…');
                for (const r of LIVE_REGIONS) {
                    await this.editorProvider.openLiveMemoryView(
                        r.region, r.address, r.size, r.label);
                }
            } catch {
                if (attempts < maxAttempts) {
                    setTimeout(tryConnect, 500);
                } else {
                    out.appendLine('[WARN] Could not connect to emulator socket server.');
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
                this._paused = false;
            } else {
                await this.socketClient.pause();
                this._paused = true;
            }
            this.webview?.postMessage({ command: 'paused', paused: this._paused });
            this.editorProvider.notifyPauseState(this._paused);
        } catch (err: any) {
            out.appendLine(`[ERROR] Pause/Resume failed: ${err?.message ?? err}`);
        }
    }

    /**
     * Called by DisasmEditorProvider when the emulator's pause state changes
     * (e.g. a breakpoint was hit).  Updates the sidebar status bar accordingly.
     */
    public notifyPauseState(paused: boolean): void {
        this._paused = paused;
        this.webview?.postMessage({ command: 'paused', paused });
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

        out.appendLine('[INFO] Restarting emulator via socket…');

        // Blank live panels so stale content is not shown during the restart
        this.editorProvider.resetLivePanels();

        try {
            await this.socketClient.restart();
            out.appendLine('[INFO] Emulator restarted — re-opening views and registering breakpoints…');

            // Re-initialize every live panel with fresh data from the restarted
            // scheduler (RAM is freshly zeroed, BIOS ROM is reloaded).
            for (const r of LIVE_REGIONS) {
                await this.editorProvider.openLiveMemoryView(
                    r.region, r.address, r.size, r.label);
            }

            // Re-register all known breakpoints then resume deterministically.
            await this._registerBreakpointsAndResume();
        } catch (err: any) {
            out.appendLine(`[ERROR] Restart failed: ${err?.message ?? err}`);
            vscode.window.showErrorMessage(`Restart failed: ${err?.message ?? err}`);
        }
    }

    /**
     * Re-sends every breakpoint the user has set (tracked in the extension process,
     * not the Angular webview) to the C# server, then sends RESUME.
     *
     * Because each SETBP is awaited, the server has processed all of them before
     * RESUME arrives — the emulator is guaranteed to start with all BPs armed.
     * This replaces the fragile fixed-delay approach that raced Angular bootstrap.
     */
    private async _registerBreakpointsAndResume(): Promise<void> {
        const out = getOutputChannel();
        if (!this.socketClient.isConnected()) { return; }
        const bps = this.editorProvider.getKnownBreakpoints();
        for (const bp of bps) {
            try {
                await this.socketClient.setBreakpoint(bp);
            } catch (err: any) {
                // Socket dropped mid-loop — stop registering; the next connect will retry.
                out.appendLine(`[WARN] Breakpoint registration interrupted at 0x${bp.toString(16).toUpperCase()}: ${err?.message ?? err}`);
                return;
            }
        }
        if (bps.size > 0) {
            out.appendLine(`[INFO] Registered ${bps.size} breakpoint(s) on server.`);
        }
        this.socketClient.resume().catch((err: any) => {
            // Ignore "already running" responses; log anything unexpected.
            const msg: string = err?.message ?? String(err);
            if (!msg.toLowerCase().includes('already')) {
                out.appendLine(`[WARN] Resume after BP registration: ${msg}`);
            }
        });
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

        out.appendLine(`[INFO] Importing ${label} (${fileBytes.length} bytes) → ${addrStr} (${region.value})`);

        try {
            await this.socketClient.writeMemory(address, hexData, region.value);
            out.appendLine(`[INFO] Import complete — ${fileBytes.length} byte(s) written.`);
            vscode.window.showInformationMessage(
                `Imported ${label} (${fileBytes.length} bytes) at ${addrStr}.`
            );
        } catch (err: any) {
            out.appendLine(`[ERROR] Import failed: ${err?.message ?? err}`);
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

        out.appendLine(`[INFO] Taking snapshot → ${snapshotsDir}`);

        try {
            const regions = await this.socketClient.snapshot(snapshotsDir);
            out.appendLine(`[INFO] Snapshot complete — ${regions.length} region(s) written.`);

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
            out.appendLine(`[ERROR] Snapshot failed: ${err?.message ?? err}`);
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
            getOutputChannel().appendLine(`[WARN] Could not load snapshots for ${isoName}: ${err?.message ?? err}`);
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
            getOutputChannel().appendLine(`[INFO] Loaded ${labels.length} game label(s) from ${labelsPath}`);
        } catch (err: any) {
            getOutputChannel().appendLine(`[WARN] Could not load game labels: ${err?.message ?? err}`);
            this.editorProvider.setGameLabels([]);
        }
    }

    /**
     * Return all historical 4-byte values at the given virtual address, one per snapshot.
     * Used by DisasmEditorProvider to populate the History tab in the edit overlay.
     */
    public getAddressHistory(address: number): Array<{ name: string; date: string; value: string }> {
        const results: Array<{ name: string; date: string; value: string }> = [];

        for (const snap of this.snapshots) {
            const infoPath = path.join(snap.path, 'snapshot-info.json');
            if (!fs.existsSync(infoPath)) { continue; }
            try {
                const info = JSON.parse(fs.readFileSync(infoPath, 'utf-8')) as SnapshotInfoFile;
                for (const region of info.regions) {
                    const base = region.BaseAddress >>> 0;
                    const size = region.Size >>> 0;
                    const offset = (address >>> 0) - base;
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


    /**
     * Called when the socket connects (including auto-connect on VS Code startup).
     * Opens live memory views for all regions, reusing any already-open panels.
     */
    public async onSocketConnected(): Promise<void> {
        this._paused = false;
        this.webview?.postMessage({ command: 'connectionStatus', connected: true });
        for (const r of LIVE_REGIONS) {
            await this.editorProvider.openLiveMemoryView(r.region, r.address, r.size, r.label);
        }
        // The emulator starts paused.  Re-register every breakpoint the user has
        // set (tracked in the extension process, no Angular roundtrip needed), then
        // immediately resume.  Because the SETBP commands are awaited, the server
        // has acknowledged all of them before RESUME is sent — no timing dependency.
        await this._registerBreakpointsAndResume();
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
}
