import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { getOutputChannel } from '../../extension';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { applyCodeDesignerPanelIcon } from '../../utils/webviewPanel';
import { DebugCommand, DebugCommandsFile, buildCommandLine } from '../../webview/debug-commands/types';

/** File name of the command list inside the Mods folder. */
const COMMANDS_FILE = 'debug-commands.json';

/**
 * Debug page: a user-editable list of script commands (Mods/debug-commands.json) shown like
 * code blocks with Copy / Run. Run opens a new VS Code terminal in the Mods folder and sends
 * the command line, the same way the PCSX2 launcher uses the terminal tab.
 */
export class DebugCommandsProvider {
    private _panel: vscode.WebviewPanel | undefined;
    private _watcher: fs.FSWatcher | undefined;
    private _reloadTimer: NodeJS.Timeout | undefined;
    /** Set while we write the file ourselves so the watcher doesn't echo it back. */
    private _selfWriteUntil = 0;

    constructor(private readonly extensionUri: vscode.Uri) {}

    /** Open (or reveal) the Debug panel. */
    public open(): void {
        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.One);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'codedesigner.debugCommands',
            'Debug',
            vscode.ViewColumn.One,
            {
                enableScripts:           true,
                retainContextWhenHidden: true,
                localResourceRoots:      [this.extensionUri],
            }
        );
        applyCodeDesignerPanelIcon(panel, this.extensionUri, 'bug');
        this._panel = panel;

        panel.onDidDispose(() => {
            this._panel = undefined;
            this._watcher?.close();
            this._watcher = undefined;
        });
        panel.webview.onDidReceiveMessage(async (msg: Record<string, unknown>) => {
            await this._handleMessage(msg, panel.webview);
        });
        panel.webview.html = this._buildHtml(panel.webview);
        this._watchFile();
    }

    // ── Mods folder / file ───────────────────────────────────────────────────

    /**
     * The Mods folder: the first workspace folder that has (or sits under a folder that has) a
     * `Mods` directory, walking up so a workspace opened on a sub-project still finds it.
     * Falls back to `<first workspace folder>/Mods`.
     */
    private _modsDir(): string | undefined {
        const folders = vscode.workspace.workspaceFolders ?? [];
        for (const f of folders) {
            let dir = f.uri.fsPath;
            for (let i = 0; i < 6; i++) {
                const candidate = path.join(dir, 'Mods');
                if (fs.existsSync(candidate) && fs.statSync(candidate).isDirectory()) { return candidate; }
                const parent = path.dirname(dir);
                if (parent === dir) { break; }
                dir = parent;
            }
        }
        return folders.length ? path.join(folders[0].uri.fsPath, 'Mods') : undefined;
    }

    private _filePath(): string | undefined {
        const mods = this._modsDir();
        return mods ? path.join(mods, COMMANDS_FILE) : undefined;
    }

    private _load(): DebugCommandsFile {
        const file = this._filePath();
        if (!file || !fs.existsSync(file)) { return { version: 1, commands: [] }; }
        const parsed = JSON.parse(fs.readFileSync(file, 'utf8')) as Partial<DebugCommandsFile>;
        return { version: parsed.version ?? 1, commands: Array.isArray(parsed.commands) ? parsed.commands : [] };
    }

    private _save(data: DebugCommandsFile): void {
        const file = this._filePath();
        if (!file) { throw new Error('Open a workspace folder first - commands are stored in its Mods folder.'); }
        fs.mkdirSync(path.dirname(file), { recursive: true });
        this._selfWriteUntil = Date.now() + 1000;
        fs.writeFileSync(file, JSON.stringify(data, null, 2) + '\n', 'utf8');
    }

    /** Reload the list when the JSON is edited by hand (debounced). */
    private _watchFile(): void {
        const file = this._filePath();
        if (!file || !fs.existsSync(file)) { return; }
        try {
            this._watcher = fs.watch(file, () => {
                if (Date.now() < this._selfWriteUntil) { return; }
                if (this._reloadTimer) { clearTimeout(this._reloadTimer); }
                this._reloadTimer = setTimeout(() => {
                    if (this._panel) { this._pushList(this._panel.webview); }
                }, 250);
            });
        } catch (err: any) {
            getOutputChannel().warn(`[Debug] Could not watch ${file}: ${err?.message ?? err}`);
        }
    }

    private _pushList(webview: vscode.Webview, message?: string): void {
        try {
            const data = this._load();
            webview.postMessage({
                type: 'debugCommandList',
                commands: data.commands,
                modsDir: this._modsDir() ?? '',
                filePath: this._filePath() ?? '',
                message,
            });
        } catch (err: any) {
            webview.postMessage({ type: 'debugCommandError', message: `Could not read ${COMMANDS_FILE}: ${err?.message ?? err}` });
        }
    }

    // ── Message dispatch ─────────────────────────────────────────────────────

    private async _handleMessage(msg: Record<string, unknown>, webview: vscode.Webview): Promise<void> {
        try {
            switch (msg['type'] as string | undefined) {
                case 'ready':
                case 'refresh':
                    this._pushList(webview);
                    break;

                case 'save': {
                    const cmd = msg['command'] as DebugCommand;
                    const data = this._load();
                    const i = data.commands.findIndex(c => c.id === cmd.id);
                    if (i >= 0) { data.commands[i] = cmd; } else { data.commands.push(cmd); }
                    this._save(data);
                    this._pushList(webview, i >= 0 ? `Saved "${cmd.name}".` : `Added "${cmd.name}".`);
                    if (!this._watcher) { this._watchFile(); }
                    break;
                }

                case 'delete': {
                    const id = msg['id'] as string;
                    const data = this._load();
                    const gone = data.commands.find(c => c.id === id);
                    data.commands = data.commands.filter(c => c.id !== id);
                    this._save(data);
                    this._pushList(webview, gone ? `Deleted "${gone.name}".` : undefined);
                    break;
                }

                case 'run': {
                    const cmd = this._load().commands.find(c => c.id === msg['id']);
                    if (!cmd) { throw new Error('Command not found - it may have been deleted.'); }
                    this._run(cmd);
                    webview.postMessage({ type: 'debugCommandStatus', message: `Running "${cmd.name}" in a new terminal.` });
                    break;
                }

                case 'help': {
                    const cmd = this._load().commands.find(c => c.id === msg['id']);
                    if (!cmd?.readme) { throw new Error('This command has no help file.'); }
                    await this._openReadme(cmd.readme);
                    break;
                }

                case 'copy':
                    await vscode.env.clipboard.writeText(String(msg['text'] ?? ''));
                    break;

                case 'openJson': {
                    const file = this._filePath();
                    if (!file) { throw new Error('No workspace folder open.'); }
                    if (!fs.existsSync(file)) { this._save({ version: 1, commands: [] }); }
                    await vscode.commands.executeCommand('vscode.open', vscode.Uri.file(file));
                    break;
                }

                default:
                    getOutputChannel().warn(`[Debug] Unknown message type: ${msg['type']}`);
            }
        } catch (err: any) {
            webview.postMessage({ type: 'debugCommandError', message: err?.message ?? String(err) });
        }
    }

    /** Show a command's help: `readme` is a Markdown file relative to the Mods folder, optionally `#heading`,
     *  rendered in VS Code's Markdown preview (beside the Debug page) and scrolled to the heading. */
    private async _openReadme(readme: string): Promise<void> {
        const mods = this._modsDir();
        if (!mods) { throw new Error('No workspace folder open.'); }
        const [rel, heading] = readme.split('#', 2);
        const file = path.resolve(mods, rel.trim());
        if (!fs.existsSync(file)) { throw new Error(`Help file not found: ${rel.trim()}`); }
        const slug = (heading ?? '').trim().toLowerCase().replace(/[^\w\s-]/g, '').replace(/\s+/g, '-');
        const uri = vscode.Uri.file(file).with({ fragment: slug });
        await vscode.commands.executeCommand('markdown.showPreviewToSide', uri);
    }

    /** Open a fresh terminal in the Mods folder (or the command's cwd) and send the command line. */
    private _run(cmd: DebugCommand): void {
        const mods = this._modsDir();
        if (!mods) { throw new Error('No workspace folder open.'); }
        const cwd = path.resolve(mods, cmd.cwd ?? '');
        const terminal = vscode.window.createTerminal({ name: `Debug: ${cmd.name}`, cwd });
        terminal.show(true);
        terminal.sendText(buildCommandLine(cmd), true);
        getOutputChannel().info(`[Debug] ${cmd.name}: ${buildCommandLine(cmd)} (cwd ${cwd})`);
    }

    // ── HTML builder ─────────────────────────────────────────────────────────

    private _buildHtml(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'webview', 'debug-commands.js')
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
<script nonce="${nonce}">${getWebviewErrorHandlerScript()}</script>
</head>
<body>
<app-debug-commands-page><p style="color:#888;font-family:sans-serif;padding:16px;font-size:13px;">Loading Debug…</p></app-debug-commands-page>
<script src="${scriptUri}"></script>
</body>
</html>`;
    }
}
