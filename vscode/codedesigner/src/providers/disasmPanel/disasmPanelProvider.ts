import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { spawn, ChildProcess } from 'child_process';
import { getNonce } from '../../helpers';
import { getDisasmPanelHtml, DisasmPanelData, EmuOption } from './disasmPanelTemplate';
import { getSocketClient, getOutputChannel } from '../../extension';

interface EmuSettings {
    bios?: Array<{ name: string; path: string }>;
    games?: Array<{ name: string; path: string }>;
    emulators?: Array<{ type: string; path: string }>;
}

export class DisasmPanelProvider implements vscode.WebviewViewProvider {
    private webview?: vscode.Webview;
    private emulatorProcess?: ChildProcess;
    private settings: EmuSettings = {};
    private workspaceRoot: string = '';

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
        webviewView.webview.options = {
            enableScripts: true
        };

        // Render the panel with settings data
        webviewView.webview.html = this.buildHtml();

        // Listen to messages from the webview
        webviewView.webview.onDidReceiveMessage(async msg => {
            switch (msg.command) {
                case 'connect':
                    this.handleConnect(msg.emulatorPath, msg.biosPath, msg.gamePath);
                    break;

                case 'disassemble':
                    await this.handleDisassemble(msg.data);
                    break;

                // Dropdown change notifications (no-op for now – stored server-side if needed)
                case 'emulatorChanged':
                case 'biosChanged':
                case 'gameChanged':
                    break;
            }
        });
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    private loadEmuSettings(): void {
        if (!this.workspaceRoot) {
            return;
        }
        const settingsPath = path.join(this.workspaceRoot, 'emu-settings.json');
        try {
            const raw = fs.readFileSync(settingsPath, 'utf-8');
            this.settings = JSON.parse(raw) as EmuSettings;
        } catch (err: any) {
            const outputChannel = getOutputChannel();
            outputChannel.appendLine(`[WARN] Could not load emu-settings.json: ${err?.message ?? err}`);
            this.settings = {};
        }
    }

    private buildHtml(): string {
        const emulators: EmuOption[] = (this.settings.emulators ?? []).map(e => ({
            type: e.type,
            path: e.path
        }));
        const biosOptions: EmuOption[] = (this.settings.bios ?? []).map(b => ({
            name: b.name,
            path: b.path
        }));
        const gameOptions: EmuOption[] = (this.settings.games ?? []).map(g => ({
            name: g.name,
            path: g.path
        }));

        const templateData: DisasmPanelData = {
            connected: this.emulatorProcess != null,
            emulators,
            biosOptions,
            gameOptions
        };
        return getDisasmPanelHtml(templateData, getNonce());
    }

    // ── Emulator launch ───────────────────────────────────────────────────────

    private handleConnect(emulatorPath: string, biosPath: string, gamePath: string): void {
        const outputChannel = getOutputChannel();

        // If already running, kill it (toggle behaviour)
        if (this.emulatorProcess) {
            this.emulatorProcess.kill();
            outputChannel.appendLine('[INFO] Emulator stopped by user.');
            this.cleanupProcess();
            return;
        }

        if (!emulatorPath) {
            vscode.window.showWarningMessage('Please select an emulator.');
            return;
        }

        // Resolve relative paths against the workspace root
        const resolvedExe = this.resolvePath(emulatorPath);
        const resolvedBios = biosPath ? this.resolvePath(biosPath) : undefined;
        const resolvedDisc = gamePath ? this.resolvePath(gamePath) : undefined;

        // Build argument list:  --run [--bios <path>] [--disc <path>]
        const cliArgs: string[] = ['--run'];
        if (resolvedBios) {
            cliArgs.push('--bios', resolvedBios);
        }
        if (resolvedDisc) {
            cliArgs.push('--disc', resolvedDisc);
        }

        outputChannel.show(true);
        outputChannel.appendLine(`[INFO] Starting emulator: ${resolvedExe} ${cliArgs.join(' ')}`);

        // Spawn without a visible console window
        const proc = spawn(resolvedExe, cliArgs, {
            windowsHide: true,
            stdio: ['ignore', 'pipe', 'pipe']
        });

        this.emulatorProcess = proc;
        this.webview?.postMessage({ command: 'connectionStatus', connected: true });

        proc.stdout?.on('data', (chunk: Buffer) => {
            outputChannel.append(chunk.toString());
        });

        proc.stderr?.on('data', (chunk: Buffer) => {
            outputChannel.append(chunk.toString());
        });

        proc.on('error', (err: Error) => {
            outputChannel.appendLine(`[ERROR] Failed to start emulator: ${err.message}`);
            vscode.window.showErrorMessage(`Failed to start emulator: ${err.message}`);
            this.cleanupProcess();
        });

        proc.on('close', (code: number | null) => {
            outputChannel.appendLine(`[INFO] Emulator exited with code ${code ?? 'null'}.`);
            this.cleanupProcess();
        });
    }

    private cleanupProcess(): void {
        this.emulatorProcess = undefined;
        this.webview?.postMessage({ command: 'connectionStatus', connected: false });
    }

    /** Resolve a workspace-relative path to an absolute path. */
    private resolvePath(p: string): string {
        if (path.isAbsolute(p)) {
            return p;
        }
        return this.workspaceRoot ? path.join(this.workspaceRoot, p) : p;
    }

    // ── Disassembly ───────────────────────────────────────────────────────────

    private async handleDisassemble(hexCode: string) {
        const socketClient = getSocketClient();

        if (!socketClient || !socketClient.isConnected()) {
            this.sendDisasmResult(false, '', 'Not connected to C# server. Use "Connect to Server" command first.');
            return;
        }

        try {
            const results = await socketClient.disassemble(hexCode);

            const asmLines: string[] = [];
            for (const result of results) {
                if (result.Success) {
                    const line = result.Comment
                        ? `${result.Hex}  ${result.Assembly}  ${result.Comment}`
                        : `${result.Hex}  ${result.Assembly}`;
                    asmLines.push(line);
                } else {
                    asmLines.push(`${result.Hex}  ERROR: ${result.Error}`);
                }
            }

            this.sendDisasmResult(true, asmLines.join('\n'), '');
        } catch (error: any) {
            this.sendDisasmResult(false, '', error.message || 'Disassembly failed');
        }
    }

    private sendDisasmResult(success: boolean, data: string, error: string) {
        if (this.webview) {
            this.webview.postMessage({
                command: 'disassembleResult',
                success,
                data,
                error
            });
        }
    }
}
