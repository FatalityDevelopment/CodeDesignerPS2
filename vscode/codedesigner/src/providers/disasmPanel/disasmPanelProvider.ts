import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { getNonce } from '../../helpers';
import { getDisasmPanelHtml, DisasmPanelData, EmuOption, SnapshotEntry } from './disasmPanelTemplate';
import { getOutputChannel } from '../../extension';
import { SocketClient } from '../../socketClient';
import { DisasmEditorProvider } from '../disasmEditor/disasmEditorProvider';

interface EmuSettings {
    bios?: Array<{ name: string; path: string }>;
    games?: Array<{ name: string; path: string }>;
    emulators?: Array<{ type: string; path: string }>;
}

/** Memory regions exposed as separate editor windows when the emulator is running. */
const LIVE_REGIONS = [
    // EE Main RAM: show via the uncached direct-mapped window (0x20000000–0x21FFFFFF).
    // The PS2 kernel TLB maps physical RAM here without caching, so user-mode code
    // typically accesses / patches memory through this range.
    { region: 'ee',  address: 0x20000000, size: 32 * 1024 * 1024, label: 'EE Main RAM'  },
    // IOP RAM: show via the EE VTLB mirror at 0x1C000000–0x1C1FFFFF.
    // PCSX2 vtlb.cpp maps psxM (IOP RAM) to EE virtual 0x1C000000; the server
    // translates this virtual address back to a physical IOP RAM offset.
    { region: 'iop', address: 0x1C000000, size:  4 * 1024 * 1024, label: 'IOP RAM'       },
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

    constructor(
        private readonly socketClient: SocketClient,
        private readonly editorProvider: DisasmEditorProvider
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
        webviewView.webview.options = { enableScripts: true };

        // Render the panel with settings data
        webviewView.webview.html = this.buildHtml();

        // Listen to messages from the webview
        webviewView.webview.onDidReceiveMessage(async msg => {
            switch (msg.command) {
                case 'connect':
                    this.handleConnect(msg.emulatorPath, msg.biosPath, msg.gamePath);
                    break;
                case 'snapshot':
                    await this.handleSnapshot();
                    break;
                case 'openSnapshot':
                    await this.handleOpenSnapshot(msg.path);
                    break;
                // Dropdown change notifications (no-op for now)
                case 'emulatorChanged':
                case 'biosChanged':
                case 'gameChanged':
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

    /** Reload settings and re-render the webview panel. */
    private reloadAndRefresh(): void {
        this.loadEmuSettings();
        if (this.webview) {
            this.webview.html = this.buildHtml();
        }
    }

    private buildHtml(): string {
        const emulators: EmuOption[] = (this.settings.emulators ?? []).map(e => ({
            type: e.type, path: e.path
        }));
        const biosOptions: EmuOption[] = (this.settings.bios ?? []).map(b => ({
            name: b.name, path: b.path
        }));
        const gameOptions: EmuOption[] = (this.settings.games ?? []).map(g => ({
            name: g.name, path: g.path
        }));

        const templateData: DisasmPanelData = {
            connected:   this.emulatorTerminal != null || this.socketClient.isConnected(),
            emulators,
            biosOptions,
            gameOptions,
            snapshots:   this.snapshots,
        };
        return getDisasmPanelHtml(templateData, getNonce());
    }

    // ── Emulator launch ───────────────────────────────────────────────────────

    private handleConnect(emulatorPath: string, biosPath: string, gamePath: string): void {
        const out = getOutputChannel();

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
        this.socketClient.connect().then(async () => {
            out.appendLine('[INFO] Connected to existing emulator server. Opening memory views…');
            this.webview?.postMessage({ command: 'connectionStatus', connected: true });
            for (const r of LIVE_REGIONS) {
                await this.editorProvider.openLiveMemoryView(r.region, r.address, r.size, r.label);
            }
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
        this.webview?.postMessage({ command: 'connectionStatus', connected: false });
    }

    // ── Snapshot ──────────────────────────────────────────────────────────────

    private async handleSnapshot(): Promise<void> {
        const out = getOutputChannel();

        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator.');
            return;
        }

        const timestamp = DisasmPanelProvider.formatSnapshotTimestamp(new Date());

        const snapshotsDir = this.workspaceRoot
            ? path.join(this.workspaceRoot, 'snapshots', timestamp)
            : path.join(require('os').tmpdir(), 'ps2snapshots', timestamp);

        out.appendLine(`[INFO] Taking snapshot → ${snapshotsDir}`);

        try {
            const regions = await this.socketClient.snapshot(snapshotsDir);
            out.appendLine(`[INFO] Snapshot complete — ${regions.length} region(s) written.`);

            // Record in the panel's snapshot list
            const entry: SnapshotEntry = {
                label:     timestamp,
                timestamp: new Date().toLocaleTimeString(),
                path:      snapshotsDir,
            };
            this.snapshots.unshift(entry);
            this.webview?.postMessage({
                command:   'updateSnapshots',
                snapshots: this.snapshots,
            });

            // Open each dumped .bin in the disassembler editor
            for (const r of regions) {
                const uri = vscode.Uri.file(r.File);
                await vscode.commands.executeCommand('vscode.openWith', uri, 'disasm.disasmEditor');
            }
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

    /** Resolve a workspace-relative path to an absolute path. */
    private resolvePath(p: string): string {
        if (path.isAbsolute(p)) { return p; }
        return this.workspaceRoot ? path.join(this.workspaceRoot, p) : p;
    }
}
