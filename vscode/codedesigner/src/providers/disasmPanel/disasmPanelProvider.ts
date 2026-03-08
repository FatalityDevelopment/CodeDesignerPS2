import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { spawn, ChildProcess } from 'child_process';
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
    { region: 'ee',  address: 0x00000000, size: 32 * 1024 * 1024, label: 'EE Main RAM'  },
    { region: 'iop', address: 0x00000000, size:  4 * 1024 * 1024, label: 'IOP RAM'       },
    { region: 'ee',  address: 0x1FC00000, size:  4 * 1024 * 1024, label: 'BIOS ROM'      },
    { region: 'ee',  address: 0x70000000, size:       16 * 1024,  label: 'Scratchpad'    },
];

export class DisasmPanelProvider implements vscode.WebviewViewProvider {
    private webview?: vscode.Webview;
    private emulatorProcess?: ChildProcess;
    private settings: EmuSettings = {};
    private workspaceRoot: string = '';
    private snapshots: SnapshotEntry[] = [];

    constructor(
        private readonly socketClient: SocketClient,
        private readonly editorProvider: DisasmEditorProvider
    ) {}

    // Register the provider with VS Code
    public register(context: vscode.ExtensionContext) {
        context.subscriptions.push(
            vscode.window.registerWebviewViewProvider('disasm.disasmPanel', this)
        );
    }

    resolveWebviewView(webviewView: vscode.WebviewView): void {
        this.webview = webviewView.webview;

        // Resolve workspace root (used to turn relative paths into absolute)
        this.workspaceRoot = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? '';

        // Load emu-settings.json from the workspace root
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

    private loadEmuSettings(): void {
        if (!this.workspaceRoot) { return; }
        const settingsPath = path.join(this.workspaceRoot, 'emu-settings.json');
        try {
            const raw = fs.readFileSync(settingsPath, 'utf-8');
            this.settings = JSON.parse(raw) as EmuSettings;
        } catch (err: any) {
            getOutputChannel().appendLine(
                `[WARN] Could not load emu-settings.json: ${err?.message ?? err}`);
            this.settings = {};
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
            connected:   this.emulatorProcess != null,
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

        // Toggle: if already running, kill it
        if (this.emulatorProcess) {
            this.emulatorProcess.kill();
            out.appendLine('[INFO] Emulator stopped by user.');
            this.cleanupProcess();
            return;
        }

        if (!emulatorPath) {
            vscode.window.showWarningMessage('Please select an emulator.');
            return;
        }

        const resolvedExe  = this.resolvePath(emulatorPath);
        const resolvedBios = biosPath ? this.resolvePath(biosPath) : undefined;
        const resolvedDisc = gamePath ? this.resolvePath(gamePath) : undefined;

        const cliArgs: string[] = ['--run'];
        if (resolvedBios) { cliArgs.push('--bios', resolvedBios); }
        if (resolvedDisc) { cliArgs.push('--disc', resolvedDisc); }

        out.show(true);
        out.appendLine(`[INFO] Starting emulator: ${resolvedExe} ${cliArgs.join(' ')}`);

        const proc = spawn(resolvedExe, cliArgs, {
            windowsHide: true,
            stdio: ['ignore', 'pipe', 'pipe'],
        });

        this.emulatorProcess = proc;
        this.webview?.postMessage({ command: 'connectionStatus', connected: true });

        proc.stdout?.on('data', (chunk: Buffer) => out.append(chunk.toString()));
        proc.stderr?.on('data', (chunk: Buffer) => out.append(chunk.toString()));

        proc.on('error', (err: Error) => {
            out.appendLine(`[ERROR] Failed to start emulator: ${err.message}`);
            vscode.window.showErrorMessage(`Failed to start emulator: ${err.message}`);
            this.cleanupProcess();
        });

        proc.on('close', (code: number | null) => {
            out.appendLine(`[INFO] Emulator exited with code ${code ?? 'null'}.`);
            this.cleanupProcess();
        });

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
                if (attempts < maxAttempts && this.emulatorProcess) {
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
        this.emulatorProcess = undefined;
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
