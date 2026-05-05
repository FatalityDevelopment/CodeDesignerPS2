import * as vscode from 'vscode';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as child_process from 'child_process';
import { getOutputChannel, getDebugChannel } from '../../extension';
import { SocketClient, LabelEntry, DiscElfEntry, CdsSidecarResult, CdsDiagnostic } from '../../socketClient';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { DisasmEditorProvider } from '../disasmEditor/disasmEditorProvider';
import { AiConfigProvider } from '../aiConfig/aiConfigProvider';
import { McpProgressReportEditorProvider } from '../mcpProgressReportEditor/mcpProgressReportEditorProvider';
import { PacketViewerProvider } from '../packetViewer/packetViewerProvider';
import { SnapshotManagerProvider, SnapshotDataDelegate } from '../snapshotManager/snapshotManagerProvider';
import { DiffViewerProvider } from '../diffViewer/diffViewerProvider';
import { ArchiveCheat } from '../../webview/disasm-panel/types';

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

/**
 * Memory regions that are always opened as live views on emulator connect.
 * Rarely-used regions (IOP RAM, BIOS ROM, Scratchpad) are kept in a separate
 * list so they can be opened on-demand via the sidebar menu without adding
 * constant socket traffic and causing flickering in the primary EE view.
 */
const LIVE_REGIONS_DEFAULT = [
    // EE RAM: shown at the physical base (0x00000000–0x01FFFFFF, 32 MB).
    // Using physical addresses keeps rows consistent with the memory map table
    // regardless of which virtual alias the CPU uses (KSEG0 0x80000000, uncached
    // 0x20100000, or uncached+accelerated 0x30100000).  HandleReadMem accepts
    // physical addresses for the EE region directly.
    { region: 'ee',  address: 0x00000000, size: 32 * 1024 * 1024, label: 'EE RAM'  },
];

/** Rarely-used memory regions available on-demand via the sidebar menu. */
const LIVE_REGIONS_OPTIONAL = [
    { region: 'ee', address: 0x00000000, size: 32 * 1024 * 1024, label: 'EE RAM' },
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

/** All memory regions combined — used when the user explicitly opens all views (restart, etc.). */
const LIVE_REGIONS = [...LIVE_REGIONS_DEFAULT, ...LIVE_REGIONS_OPTIONAL];

/** URL and branch for the cheat code archive repository. */
const ARCHIVE_REPO_URL    = 'https://github.com/TylerDev888/CheatCodeArchive.git';
const ARCHIVE_REMOTE      = 'origin';
const ARCHIVE_BRANCH      = 'main';

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
    /** Archive cheats fetched from CheatCodeArchive for the current PCSX2 game. */
    private _archiveCheats: ArchiveCheat[] = [];
    /** Cheat type string reported by the archive YAML (e.g. "CodeBreaker"). */
    private _archiveCheatType: string = '';
    private _activeCdsFilePath: string = '';
    private _activeCdsContent: string = '';
    /** Sidecar commands (assert/breakpoint/watchpoint) from the most recent successful CDS compilation. */
    private _lastCdsSidecars: CdsSidecarResult[] = [];
    /** Diagnostic collection for CDS compilation errors — shows red squiggles in the editor. */
    private _cdsDiagnostics!: vscode.DiagnosticCollection;
    /** Debounce timer for background CDS validation. */
    private _cdsValidationTimer: ReturnType<typeof setTimeout> | undefined;
    /** Monotonic token used to ignore stale async CDS validation responses. */
    private _cdsValidationVersion = 0;
    /** Path to the extension's global storage directory (set in register()). */
    private _globalStoragePath: string = '';
    /** In-flight archive sync promise — reused so concurrent callers don't start duplicate clones. */
    private _archiveSyncPromise: Promise<void> | null = null;
    /** Interval timer for live Joker pad-status polling (cleared on close/disconnect). */
    private _jokerPadPollTimer: ReturnType<typeof setInterval> | undefined;
    /** Last pad button bitmask sent to the webview — used to skip duplicate messages. */
    private _lastJokerPadButtons: number | undefined;

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
    ) {
        this._mcpProgressReportEditorProvider = new McpProgressReportEditorProvider(socketClient);
        this._aiConfigProvider        = new AiConfigProvider(_extensionUri, socketClient);
        this._packetViewerProvider    = new PacketViewerProvider(_extensionUri, socketClient);

        const delegate: SnapshotDataDelegate = {
            getSnapshots:   () => this.snapshots,
            openSnapshot:   (p) => this.handleOpenSnapshot(p),
            reloadSnapshot: (p) => this.handleLoadSnapshot(p),
        };
        this._snapshotManagerProvider = new SnapshotManagerProvider(_extensionUri, delegate);
        this._diffViewerProvider      = new DiffViewerProvider(_extensionUri, delegate);
    }

    private readonly _mcpProgressReportEditorProvider: McpProgressReportEditorProvider;
    private readonly _aiConfigProvider: AiConfigProvider;
    private readonly _packetViewerProvider: PacketViewerProvider;
    private readonly _snapshotManagerProvider: SnapshotManagerProvider;
    private readonly _diffViewerProvider: DiffViewerProvider;

    // Register the provider with VS Code
    public register(context: vscode.ExtensionContext) {
        this.extensionPath = context.extensionPath;
        this._globalStoragePath = context.globalStorageUri.fsPath;

        // Initialise workspaceRoot early so that any feature relying on it
        // (label manager, snapshots, settings persistence) works even before
        // resolveWebviewView is called — e.g. when the socket auto-connects
        // at startup before the sidebar becomes visible.
        this.loadEmuSettings();

        // Sync the CheatCodeArchive repo in the background so cheats are
        // available without blocking the UI.
        this._syncArchiveRepo().catch(err =>
            getOutputChannel().warn(`CheatCodeArchive sync: ${err?.message ?? err}`)
        );

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
                this._scheduleValidateCds(50);
            })
        );
        // Sync content when the document is modified
        context.subscriptions.push(
            vscode.workspace.onDidChangeTextDocument(e => {
                if (e.document.uri.fsPath === this._activeCdsFilePath) {
                    this._activeCdsContent = e.document.getText();
                    this._scheduleValidateCds();
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
                    if (this._cdsValidationTimer) {
                        clearTimeout(this._cdsValidationTimer);
                        this._cdsValidationTimer = undefined;
                    }
                }
            })
        );

        this._syncActiveCdsEditor(vscode.window.activeTextEditor);
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
                case 'startNetwork':
                    this.handleStartNetwork();
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
                case 'saveScreenshot':
                    await this.handleSaveScreenshot();
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
                case 'fetchArchiveCheats':
                    await this.handleFetchArchiveCheats();
                    break;
                case 'toggleArchiveCheat':
                    await this.handleToggleArchiveCheat(msg.index as number, msg.enabled as boolean);
                    break;
                case 'applyAllArchiveCheats':
                    await this.handleApplyAllArchiveCheats();
                    break;
                case 'startJokerPadPoll':
                    this.startJokerPadPoll();
                    break;
                case 'stopJokerPadPoll':
                    this.stopJokerPadPoll();
                    break;
                case 'compileCds':
                    await this.handleCompileCds();
                    break;
                case 'addPatch':
                    await this.handleAddPatch(msg.codes ?? '');
                    break;
                case 'openMarkdownHelp':
                    await this.handleOpenMarkdownHelp(msg.file ?? '');
                    break;
                case 'focusRegion':
                    this.editorProvider.focusLivePanelByLabel(msg.label ?? '');
                    break;
                case 'openMemoryView':
                    await this.openOptionalMemoryView(msg.label ?? '');
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
                // ── MCP Memory Manager ────────────────────────────────────
                case 'mcpMemoryList':
                    await this.handleMcpMemoryList();
                    break;
                case 'mcpMemoryAdd':
                    await this.handleMcpMemoryAdd(msg.name ?? '');
                    break;
                case 'mcpMemoryEdit':
                    this._mcpProgressReportEditorProvider.open(msg.id as number, msg.name ?? '', msg.content ?? '');
                    break;
                case 'mcpMemoryExport':
                    await this.handleMcpMemoryExport(msg.id as number, msg.name ?? '');
                    break;
                case 'mcpMemoryImport':
                    await this.handleMcpMemoryImport();
                    break;
                case 'mcpMemoryDelete':
                    await this.handleMcpMemoryDelete(msg.id as number);
                    break;
                // ── AI Configuration ──────────────────────────────────────
                case 'openAiConfig':
                    this._aiConfigProvider.open();
                    break;
                // ── Packet Viewer ─────────────────────────────────────────
                case 'openPacketViewer':
                    this._packetViewerProvider.open();
                    break;
                // ── Snapshot Manager ──────────────────────────────────────
                case 'openSnapshotManager':
                    this._snapshotManagerProvider.open();
                    break;
                // ── Diff Viewer ───────────────────────────────────────────
                case 'openDiffViewer':
                    this._diffViewerProvider.open();
                    break;
                // ── Label Manager ─────────────────────────────────────────
                case 'openLabelManager':
                    this.editorProvider.openLabelManagerTab();
                    break;
                // ── Memory Card Manager ───────────────────────────────────
                case 'openMemoryCard':
                    this.editorProvider.openMemoryCardOverlay();
                    break;
                // ── HDD Image Manager ─────────────────────────────────────
                case 'openHdd':
                    this.editorProvider.openHddOverlay();
                    break;
                // ── DNS Mapping editor ────────────────────────────────────
                case 'openDnsMapping':
                    await this.handleOpenDnsMapping();
                    break;
                // ── Multiplayer config editor ─────────────────────────────
                case 'openNetConfig':
                    await this.handleOpenNetConfig();
                    break;
                // ── Cheat Code Converter ──────────────────────────────────
                case 'openConverter':
                    this.editorProvider.openConverterOverlay();
                    break;
                // ── Cheat Code Porter ─────────────────────────────────────
                case 'openPorter':
                    this.editorProvider.openPorterOverlay();
                    break;
                // ── Memory Scanner ────────────────────────────────────────
                case 'openScanner':
                    this.editorProvider.openScannerOverlay();
                    break;
                // ── Follow-AI mode toggle ─────────────────────────────────
                case 'toggleFollowAi': {
                    const enabled = !!msg.enabled;
                    this.editorProvider.setFollowAiEnabled(enabled);
                    // Echo the new state back to the panel webview so the
                    // status bar can sync its local controlMode signal.
                    this.webview?.postMessage({ command: 'followAiState', enabled });
                    break;
                }
            }
        });
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    /** Maximum number of parent directories to climb when searching for emu-settings.json. */
    private static readonly MAX_PARENT_SEARCH_DEPTH = 6;

    /**
     * Returns the CodeDesigner install directory — the same location as the
     * SQLite database.  Mirrors the C# DatabasePaths.GetInstallDirectory() logic.
     *
     * Windows  → %LOCALAPPDATA%\CodeDesigner\
     * Linux/macOS → ~/.local/share/CodeDesigner/
     */
    private static getInstallDirectory(): string {
        if (process.platform === 'win32') {
            const localAppData = process.env.LOCALAPPDATA
                ?? path.join(os.homedir(), 'AppData', 'Local');
            return path.join(localAppData, 'CodeDesigner');
        }
        return path.join(os.homedir(), '.local', 'share', 'CodeDesigner');
    }

    /**
     * Returns the path to CodeDesigner.Ps2.Bridge.exe in the install directory,
     * or null if it does not exist.
     */
    private static findBridgeExe(): string | null {
        const installDir = DisasmPanelProvider.getInstallDirectory();
        const exeName = process.platform === 'win32'
            ? 'CodeDesigner.Ps2.Bridge.exe'
            : 'CodeDesigner.Ps2.Bridge';
        const candidate = path.join(installDir, exeName);
        return fs.existsSync(candidate) ? candidate : null;
    }

    /**
     * Returns BIOS files discovered in the install directory's Resources/Bios
     * sub-folder (populated when Bridge is built and deployed).
     */
    private static detectBiosFiles(): Array<{ name: string; path: string }> {
        const biosDir = path.join(DisasmPanelProvider.getInstallDirectory(), 'Resources', 'Bios');
        const results: Array<{ name: string; path: string }> = [];
        try {
            if (!fs.existsSync(biosDir)) { return results; }
            for (const file of fs.readdirSync(biosDir)) {
                if (/\.bin$/i.test(file)) {
                    results.push({
                        name: path.basename(file, path.extname(file)),
                        path: path.join(biosDir, file),
                    });
                }
            }
        } catch { /* ignore read errors */ }
        return results;
    }

    /**
     * Tries to locate a PCSX2 executable.
     * Resolution order:
     * 1. `pcsx2` subfolder inside the CodeDesigner install directory
     *    (e.g. `%LOCALAPPDATA%\CodeDesigner\pcsx2\pcsx2-qt.exe`).
     * 2. Standard OS-specific installation paths.
     * Returns the first path found, or null if PCSX2 is not detected.
     */
    private static detectPcsx2Exe(): string | null {
        const installDir = DisasmPanelProvider.getInstallDirectory();
        const candidates: string[] = [];

        if (process.platform === 'win32') {
            // 1. pcsx2 subfolder inside the install directory (preferred)
            candidates.push(path.join(installDir, 'pcsx2', 'pcsx2-qt.exe'));
            // 2. Standard Windows installation paths
            const pf    = process.env.PROGRAMFILES         ?? 'C:\\Program Files';
            const pf86  = process.env['PROGRAMFILES(X86)'] ?? 'C:\\Program Files (x86)';
            const local = process.env.LOCALAPPDATA         ?? '';
            candidates.push(
                path.join(pf,   'PCSX2', 'pcsx2-qt.exe'),
                path.join(pf86, 'PCSX2', 'pcsx2-qt.exe'),
                ...(local ? [path.join(local, 'Programs', 'PCSX2', 'pcsx2-qt.exe')] : []),
            );
        } else if (process.platform === 'darwin') {
            candidates.push(
                path.join(installDir, 'pcsx2', 'PCSX2'),
                '/Applications/PCSX2.app/Contents/MacOS/PCSX2',
            );
        } else {
            candidates.push(
                path.join(installDir, 'pcsx2', 'pcsx2'),
                '/usr/bin/pcsx2',
                '/usr/local/bin/pcsx2',
                '/snap/bin/pcsx2',
            );
        }
        return candidates.find(c => c && fs.existsSync(c)) ?? null;
    }

    private loadEmuSettings(): void {
        let settingsFound = false;

        // 1. Try every open workspace folder
        for (const folder of vscode.workspace.workspaceFolders ?? []) {
            if (this.tryLoadSettingsFrom(path.join(folder.uri.fsPath, 'emu-settings.json'))) {
                settingsFound = true;
                break;
            }
        }

        // 2. Walk up from the extension install directory (handles the case where
        //    the developer opens vscode/codedesigner directly and the repo root is
        //    a few levels above).
        if (!settingsFound && this.extensionPath) {
            let dir = this.extensionPath;
            for (let i = 0; i < DisasmPanelProvider.MAX_PARENT_SEARCH_DEPTH; i++) {
                if (this.tryLoadSettingsFrom(path.join(dir, 'emu-settings.json'))) {
                    break;
                }
                const parent = path.dirname(dir);
                if (parent === dir) { break; }   // reached filesystem root
                dir = parent;
            }
        }

        // 2b. If no settings file was found anywhere, still set workspaceRoot so
        //     that features depending on it (label manager, snapshot loading, save
        //     preferences) can function.  The settings file will be created here
        //     once the user saves settings.  Prefer the first open workspace folder;
        //     fall back to the extension install directory.
        if (!this.workspaceRoot) {
            const firstFolder = vscode.workspace.workspaceFolders?.[0];
            if (firstFolder) {
                this.workspaceRoot = firstFolder.uri.fsPath;
            } else if (this.extensionPath) {
                this.workspaceRoot = this.extensionPath;
            }
        }

        // 3. Auto-detect the PCSX2 executable and add it to the emulators list.
        //    Only PCSX2 entries appear in the selector; the Bridge is always
        //    resolved from the install directory, never from this list.
        //    BIOS files must be provided and configured by the user.
        const pcsx2Exe = DisasmPanelProvider.detectPcsx2Exe();
        if (pcsx2Exe) {
            const existing = (this.settings.emulators ?? []).filter(e => this.isPcsx2Entry(e));
            if (!existing.some(e => e.path === pcsx2Exe)) {
                this.settings.emulators = [
                    { type: 'PCSX2', path: pcsx2Exe },
                    ...existing,
                ];
            } else {
                this.settings.emulators = existing;
            }
        } else {
            // Keep only PCSX2 entries — CodeDesigner Bridge is auto-detected.
            this.settings.emulators = (this.settings.emulators ?? []).filter(e => this.isPcsx2Entry(e));
        }
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
            this.selectedGameName = this.selectedGamePath
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

    /**
     * Persist the current emulator settings (games list, BIOS list, emulators list,
     * and PCSX2 connection settings).
     *
     * Strategy:
     *  - Connected  → write to the DB only (DB is the authoritative store).
     *  - Disconnected → write to emu-settings.json as a local fallback.
     */
    private async saveEmuSettings(): Promise<void> {
        if (this.socketClient.isConnected()) {
            // DB is authoritative — persist there only.
            try {
                await this.socketClient.setSetting('emu.games',     JSON.stringify(this.settings.games     ?? []));
                await this.socketClient.setSetting('emu.bios',      JSON.stringify(this.settings.bios      ?? []));
                await this.socketClient.setSetting('emu.emulators', JSON.stringify(this.settings.emulators ?? []));
                if (this.settings.pcsx2?.host != null)
                    await this.socketClient.setSetting('emu.pcsx2.host', String(this.settings.pcsx2.host));
                if (this.settings.pcsx2?.slot != null)
                    await this.socketClient.setSetting('emu.pcsx2.slot', String(this.settings.pcsx2.slot));
            } catch (err: any) {
                getOutputChannel().debug(`Could not persist emu-settings to DB: ${err?.message ?? err}`);
            }
        } else {
            // Offline fallback — write to the JSON file only.
            if (!this.workspaceRoot) { return; }
            const settingsPath = path.join(this.workspaceRoot, 'emu-settings.json');
            const toSave = {
                bios:      this.settings.bios,
                games:     this.settings.games,
                emulators: this.settings.emulators,
                pcsx2:     this.settings.pcsx2,
            };
            try {
                await fs.promises.writeFile(settingsPath, JSON.stringify(toSave, null, 2), 'utf-8');
            } catch (err: any) {
                getOutputChannel().debug(`Could not save ${settingsPath}: ${err?.message ?? err}`);
            }
        }
    }

    /**
     * Persist the currently selected emulator, bios, and game paths.
     *
     * Strategy (mirrors saveEmuSettings):
     *  - Connected  → write to DB only.
     *  - Disconnected → write to codedesigner-settings.json as a local fallback.
     */
    private async saveUserPreferences(): Promise<void> {
        const prefs: UserPreferences = {
            selectedEmulator: this.selectedEmulatorPath,
            selectedBios:     this.selectedBiosPath,
            selectedGame:     this.selectedGamePath,
        };

        if (this.socketClient.isConnected()) {
            // DB is authoritative — persist there only.
            try {
                await this.socketClient.setSetting('ui.selectedEmulator', this.selectedEmulatorPath);
                await this.socketClient.setSetting('ui.selectedBios',     this.selectedBiosPath);
                await this.socketClient.setSetting('ui.selectedGame',     this.selectedGamePath);
            } catch (err: any) {
                getOutputChannel().debug(`Could not persist preferences to DB: ${err?.message ?? err}`);
            }
        } else {
            // Offline fallback — write to the JSON file only.
            const settingsPath = this.settingsJsonPath;
            if (!settingsPath) { return; }
            try {
                await fs.promises.writeFile(settingsPath, JSON.stringify(prefs, null, 2), 'utf-8');
            } catch (err: any) {
                getOutputChannel().debug(`Could not save ${settingsPath}: ${err?.message ?? err}`);
            }
        }
    }

    /**
     * Pull user preferences and emulator settings from the server-side DB after connect.
     * DB values take priority over any local JSON file values because they represent the
     * most recently saved state.
     */
    private async loadUserPreferencesFromDb(): Promise<void> {
        try {
            const settings = await this.socketClient.getAllSettings();
            const emulator = settings['ui.selectedEmulator'];
            const bios     = settings['ui.selectedBios'];
            const game     = settings['ui.selectedGame'];

            let changed = false;
            if (emulator != null && emulator !== this.selectedEmulatorPath) {
                this.selectedEmulatorPath = emulator;
                changed = true;
            }
            if (bios != null && bios !== this.selectedBiosPath) {
                this.selectedBiosPath = bios;
                changed = true;
            }
            if (game && game !== this.selectedGamePath) {
                this.selectedGamePath = game;
                this.selectedGameName = game
                    ? path.basename(game, path.extname(game))
                    : '';
                this.loadSnapshotsForGame(this.selectedGameName);
                this.loadGameLabels(this.selectedGameName);
                changed = true;
            }

            // Merge game list from DB if present.
            const rawGames = settings['emu.games'];
            if (rawGames) {
                try { this.settings.games = JSON.parse(rawGames); changed = true; } catch { /* ignore */ }
            }

            // Merge BIOS list from DB if present.
            const rawBios = settings['emu.bios'];
            if (rawBios) {
                try { this.settings.bios = JSON.parse(rawBios); changed = true; } catch { /* ignore */ }
            }

            // Merge emulator list from DB if present, adding any DB entries not
            // already in the auto-detected list.
            const rawEmulators = settings['emu.emulators'];
            if (rawEmulators) {
                try {
                    const dbEmulators: Array<{ type: string; path: string }> = JSON.parse(rawEmulators);
                    const existing = this.settings.emulators ?? [];
                    const merged = [...existing];
                    for (const e of dbEmulators) {
                        if (!merged.some(x => x.path === e.path)) {
                            merged.push(e);
                        }
                    }
                    if (merged.length !== existing.length) {
                        this.settings.emulators = merged;
                        changed = true;
                    }
                } catch { /* ignore */ }
            }

            if (changed) {
                this.postInitMessage();

                // Write the authoritative DB data back to the local JSON file so
                // it stays in sync and offline sessions load the correct settings.
                if (this.workspaceRoot) {
                    const emuSettingsPath = path.join(this.workspaceRoot, 'emu-settings.json');
                    const toSave = {
                        bios:      this.settings.bios,
                        games:     this.settings.games,
                        emulators: this.settings.emulators,
                        pcsx2:     this.settings.pcsx2,
                    };
                    try {
                        await fs.promises.writeFile(
                            emuSettingsPath,
                            JSON.stringify(toSave, null, 2),
                            'utf-8'
                        );
                    } catch (err: any) {
                        getOutputChannel().debug(`Could not sync emu-settings.json from DB: ${err?.message ?? err}`);
                    }
                }

                const prefsPath = this.settingsJsonPath;
                if (prefsPath) {
                    const prefs: UserPreferences = {
                        selectedEmulator: this.selectedEmulatorPath,
                        selectedBios:     this.selectedBiosPath,
                        selectedGame:     this.selectedGamePath,
                    };
                    try {
                        await fs.promises.writeFile(prefsPath, JSON.stringify(prefs, null, 2), 'utf-8');
                    } catch (err: any) {
                        getOutputChannel().debug(`Could not sync codedesigner-settings.json from DB: ${err?.message ?? err}`);
                    }
                }
            }
        } catch (err: any) {
            getOutputChannel().debug(`Could not load preferences from DB: ${err?.message ?? err}`);
        }
    }

    private postInitMessage(): void {
        if (!this.webview) { return; }
        // Only PCSX2 entries appear in the emulator selector; Bridge is auto-detected.
        const emulators = (this.settings.emulators ?? []).map(e => ({ label: e.type, value: e.path }));
        // BIOS options come from user configuration only (BIOS files must be provided by the user).
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
            isPcsx2: this._bridgeMode,
            selectedEmulator: this.selectedEmulatorPath,
            selectedBios: this.selectedBiosPath,
            selectedGame: this.selectedGamePath,
        });
        // Restore Follow-AI button state (the editor provider owns the flag).
        this.webview.postMessage({ command: 'followAiState', enabled: this.editorProvider.followAiEnabled });
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
        // Primary: Bridge deployed to the install directory (no user configuration needed).
        const installExe = DisasmPanelProvider.findBridgeExe();
        if (installExe) { return installExe; }

        // Fallback: a non-PCSX2 emulator entry the user may have configured manually.
        if (selectedEmulatorPath && !this.isPcsx2Selection(selectedEmulatorPath)) {
            const selectedEntry = (this.settings.emulators ?? []).find(e =>
                this.resolvePath(e.path).toLowerCase() === this.resolvePath(selectedEmulatorPath).toLowerCase()
            );
            if (selectedEntry && !this.isPcsx2Entry(selectedEntry)) {
                return this.resolvePath(selectedEntry.path);
            }
        }

        const fallback = (this.settings.emulators ?? []).find(e => !this.isPcsx2Entry(e));
        if (fallback) { return this.resolvePath(fallback.path); }

        return null;
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

    /** Launch the Multiplayer server executable in a VSCode terminal. */
    private handleStartNetwork(): void {
        const out = getOutputChannel();
        const installDir = DisasmPanelProvider.getInstallDirectory();
        const exeName = process.platform === 'win32'
            ? 'CodeDesigner.Ps2.Multiplayer.exe'
            : 'CodeDesigner.Ps2.Multiplayer';
        const exePath = path.join(installDir, 'Multiplayer', exeName);

        if (!fs.existsSync(exePath)) {
            const multiDir = path.join(installDir, 'Multiplayer');
            out.error(`Multiplayer server not found: ${exePath}. Build the project first to deploy it.`);
            vscode.window.showErrorMessage(
                `CodeDesigner.Ps2.Multiplayer not found in ${multiDir}. ` +
                'Build the Multiplayer project (dotnet build CodeDesigner.Ps2.Multiplayer) to deploy it.'
            );
            return;
        }

        out.info(`Starting Multiplayer server: ${exePath}`);

        const terminal = vscode.window.createTerminal({
            name: 'CodeDesigner Multiplayer',
            shellPath: exePath,
            shellArgs: [],
        });
        terminal.show(true);
    }

    /**
     * Open the Multiplayer server config.json in the VS Code text editor.
     * The file lives in the install directory's Multiplayer sub-folder.
     */
    private async handleOpenNetConfig(): Promise<void> {
        const installDir = DisasmPanelProvider.getInstallDirectory();
        const configPath = path.join(installDir, 'Multiplayer', 'config.json');
        if (!fs.existsSync(configPath)) {
            vscode.window.showErrorMessage(
                `Multiplayer config not found: ${configPath}. ` +
                'Build the Multiplayer project first (dotnet build CodeDesigner.Ps2.Multiplayer) to deploy it.'
            );
            return;
        }
        const uri = vscode.Uri.file(configPath);
        await vscode.commands.executeCommand('vscode.open', uri);
    }

    /**
     * Open the DnsMappings section of config.json for inline editing,
     * then show an information message about the format.
     */
    private async handleOpenDnsMapping(): Promise<void> {
        const installDir = DisasmPanelProvider.getInstallDirectory();
        const configPath = path.join(installDir, 'Multiplayer', 'config.json');
        if (!fs.existsSync(configPath)) {
            vscode.window.showErrorMessage(
                `Multiplayer config not found: ${configPath}. ` +
                'Build the Multiplayer project first (dotnet build CodeDesigner.Ps2.Multiplayer) to deploy it.'
            );
            return;
        }
        // Open the config file and reveal the DnsMappings key
        const uri = vscode.Uri.file(configPath);
        const doc = await vscode.workspace.openTextDocument(uri);
        const editor = await vscode.window.showTextDocument(doc);

        // Find the "DnsMappings" key and move the cursor there
        const text = doc.getText();
        const idx  = text.indexOf('"DnsMappings"');
        if (idx >= 0) {
            const pos = doc.positionAt(idx);
            editor.selection = new vscode.Selection(pos, pos);
            editor.revealRange(new vscode.Range(pos, pos), vscode.TextEditorRevealType.InCenter);
        }

        vscode.window.showInformationMessage(
            'Add DNS overrides under "DnsMappings": { "hostname": "ip" }. ' +
            'The DNS server intercepts A queries for these hostnames and responds with the mapped IP. ' +
            'Restart the Multiplayer server for changes to take effect.'
        );
    }

    /** Launch CodeDesigner control server with no mode args, then connect socket/views. */
    private launchControlServerAndConnect(selectedEmulatorPath: string): void {
        const out = getOutputChannel();
        const resolvedExe = this.getControlServerExecutablePath(selectedEmulatorPath);
        if (!resolvedExe) {
            const installDir = DisasmPanelProvider.getInstallDirectory();
            out.error(`CodeDesigner.Ps2.Bridge not found in the install directory (${installDir}). Build the project first to deploy it.`);
            vscode.window.showErrorMessage(
                `CodeDesigner.Ps2.Bridge not found in ${installDir}. ` +
                'Build the Bridge project (dotnet build CodeDesigner.Ps2.Bridge) to deploy it.'
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
                'Build the Bridge project first: dotnet build CodeDesigner.Ps2.Bridge'
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
                for (const r of LIVE_REGIONS_DEFAULT) {
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
        this.stopJokerPadPoll();
        this.emulatorTerminal = undefined;
        this._paused = false;
        this._bridgeMode = false;
        this._pendingRunMode = null;
        this.webview?.postMessage({ command: 'connectionStatus', connected: false });
    }

    // ── Joker live pad polling ──────────────────────────────────────────────

    private startJokerPadPoll(): void {
        this.stopJokerPadPoll();
        if (!this.socketClient.isConnected()) { return; }

        let errorCount = 0;
        this._lastJokerPadButtons = undefined;
        const poll = async () => {
            try {
                const state = await this.socketClient.padGetState(0);
                errorCount = 0;
                // Only post to webview when the pad state actually changed.
                if (state.buttons !== this._lastJokerPadButtons) {
                    this._lastJokerPadButtons = state.buttons;
                    this.webview?.postMessage({ command: 'jokerPadState', buttons: state.buttons });
                }
            } catch {
                errorCount++;
                if (errorCount >= 5) {
                    getOutputChannel().warn('Joker pad poll: too many consecutive failures, stopping.');
                    this.stopJokerPadPoll();
                }
            }
        };
        // Fire immediately, then every 500ms.
        void poll();
        this._jokerPadPollTimer = setInterval(() => void poll(), 500);
    }

    private stopJokerPadPoll(): void {
        if (this._jokerPadPollTimer !== undefined) {
            clearInterval(this._jokerPadPollTimer);
            this._jokerPadPollTimer = undefined;
        }
        this._lastJokerPadButtons = undefined;
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
                    // Notify webview of PCSX2 mode so the cheats tab shows the archive section.
                    this.webview?.postMessage({ command: 'pcsx2ModeChanged', isPcsx2: true });
                    // Auto-fetch archive cheats for the running game.
                    void this.handleFetchArchiveCheats();
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
     * PCSX2 is located from the emulators list, then auto-detected from the install
     * directory's `pcsx2` subfolder, then standard OS paths — no manual path config needed.
     */
    private handleStartPcsx2(emulatorPath: string, biosPath: string, gamePath: string): void {
        const out = getOutputChannel();

        this.selectedEmulatorPath = emulatorPath;
        this.selectedBiosPath = biosPath;
        this.selectedGamePath = gamePath;

        // Resolve PCSX2 path: prefer the passed value (from emulator selector if PCSX2 entry),
        // then fall back to auto-detection.
        let pcsx2Exe = '';
        if (emulatorPath && this.isPcsx2Selection(emulatorPath)) {
            pcsx2Exe = this.resolvePath(emulatorPath);
        }
        if (!pcsx2Exe || !fs.existsSync(pcsx2Exe)) {
            pcsx2Exe = DisasmPanelProvider.detectPcsx2Exe() ?? '';
        }

        if (!pcsx2Exe || !fs.existsSync(pcsx2Exe)) {
            out.error('PCSX2 executable not found. Place PCSX2 in the pcsx2 subfolder of the install directory, install it to a standard location, or add it via the emulator selector.');
            vscode.window.showErrorMessage(
                'PCSX2 not found. Place PCSX2 in the pcsx2 subfolder of the CodeDesigner install directory, or add it via the emulator selector.'
            );
            return;
        }

        const args: string[] = [];
        const resolvedDisc = gamePath ? this.resolvePath(gamePath) : '';

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

            if (this._bridgeMode) {
                // In PCSX2 mode the external emulator manages its own memory; just
                // re-register breakpoints and leave the live panels blank until the
                // user manually navigates or pauses.
                await this._registerBreakpointsAndResume();
            } else {
                // Re-initialize every live panel with fresh data from the restarted
                // scheduler (RAM is freshly zeroed, BIOS ROM is reloaded).
                for (const r of LIVE_REGIONS) {
                    await this.editorProvider.openLiveMemoryView(
                        r.region, r.address, r.size, r.label);
                }

                // Re-register all known breakpoints then resume deterministically.
                await this._registerBreakpointsAndResume();
            }
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

    /**
     * Take a screenshot of the game's screen in PCSX2 via the PINE socket bridge
     * (PINESAVESCREENSHOT command → PineClient.SaveScreenshot opcode 0x32).
     * Only available in PINE mode.
     */
    private async handleSaveScreenshot(): Promise<void> {
        const out = getOutputChannel();

        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator.');
            return;
        }

        try {
            await this.socketClient.pineSaveScreenshot();
            out.info('Screenshot saved in PCSX2.');
            vscode.window.showInformationMessage('Screenshot saved in PCSX2.');
        } catch (err: any) {
            out.error(`Save screenshot failed: ${err?.message ?? err}`);
            vscode.window.showErrorMessage(`Save screenshot failed: ${err?.message ?? err}`);
        }
    }
    public async handleOpenSnapshot(snapshotPath: string): Promise<void> {
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
    public async handleLoadSnapshot(snapshotPath: string): Promise<void> {
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

        // Persist the ISO path in the database so label sets can be resolved
        // by GameDisc. Try to get the serial from a running PINE session first;
        // if unavailable just update the path with an empty serial placeholder.
        if (gamePath && this._bridgeMode) {
            try {
                const info = await this.socketClient.getPcsx2GameInfo();
                const serial    = (info.ID    ?? '').replace(/\0/g, '').trim();
                const gameTitle = (info.Title ?? '').replace(/\0/g, '').trim() || this.selectedGameName;
                if (serial) {
                    // Upsert Game/GameRegion/GameDisc in the database with IsoPath,
                    // serial, and title so the game appears in the DB immediately.
                    await this.socketClient.updateGameDisc(serial, gamePath, undefined, gameTitle);
                    this.editorProvider.setCurrentGame(this.selectedGameName, this.workspaceRoot, serial);
                } else {
                    // Serial unavailable — just store IsoPath; use filename for DB fallback.
                    this.editorProvider.setCurrentGame(this.selectedGameName, this.workspaceRoot, gamePath);
                }
            } catch {
                // Not in PINE mode or PCSX2 not running — fall back to ISO path.
                this.editorProvider.setCurrentGame(this.selectedGameName, this.workspaceRoot, gamePath);
            }
        }
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
            this._snapshotManagerProvider.notifySnapshotsChanged();
            return;
        }

        const isoFolder = path.join(this.workspaceRoot, 'snapshots', isoName);
        const entries: SnapshotEntry[] = [];

        try {
            if (!fs.existsSync(isoFolder)) {
                this.snapshots = [];
                this.webview?.postMessage({ command: 'updateSnapshots', snapshots: [], gameName: isoName });
                this._snapshotManagerProvider.notifySnapshotsChanged();
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
                    // Skip .extracted directories (PCSX2 snapshot extraction folders)
                    if (dirent.name.endsWith('.extracted')) {
                        continue;
                    }

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
        this._snapshotManagerProvider.notifySnapshotsChanged();
    }

    /**
     * Load game-specific labels from snapshots/<isoName>/labels.json and
     * push them to the editor provider for display in the Labels tab.
     */
    private loadGameLabels(isoName: string): void {
        // Always update the editor provider's current game context so user-created
        // labels are saved to the correct per-game file.
        // Pass the full ISO path (when available) so that _labelManagerCtx() can
        // resolve the GameDisc even when PINE is not connected.  Without this the
        // serial stays empty after a settings-restore and ResolveGameDiscIdAsync
        // only receives the bare filename — which may fail to match an IsoPath
        // that is stored as a full path in the database.
        this.editorProvider.setCurrentGame(isoName, this.workspaceRoot, this.selectedGamePath || undefined);

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

        // Snapshots store physical base addresses (e.g. 0x00000000 for EE RAM).
        // Convert any virtual/logical alias to its physical equivalent before
        // doing the offset calculation.
        //   KSEG0/KSEG1 (0x80000000–0xBFFFFFFF): strip top 3 bits → physical
        //   EE RAM uncached mirror (0x20000000–0x21FFFFFF): subtract 0x20000000
        //   EE RAM uncached+accelerated (0x30000000–0x31FFFFFF): subtract 0x30000000
        const addrU32 = address >>> 0;
        let physAddress: number;
        if (addrU32 >= 0x80000000 && addrU32 < 0xC0000000)
            physAddress = (addrU32 & 0x1FFFFFFF) >>> 0;
        else if (addrU32 >= 0x20000000 && addrU32 <= 0x21FFFFFF)
            physAddress = (addrU32 - 0x20000000) >>> 0;
        else if (addrU32 >= 0x30000000 && addrU32 <= 0x31FFFFFF)
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

    // ── Archive cheat constants ───────────────────────────────────────────────
    /** Code-type byte for GameShark/CodeBreaker Joker (conditional) addresses. */
    private static readonly CHEAT_TYPE_JOKER   = 0xD0;
    /** Upper bound (inclusive) of the 32 MB EE RAM address space (physical). */
    private static readonly MAX_EE_RAM_ADDRESS = 0x1FFFFFF;
    /** EE uncached user-space mirror base — OR with a physical EE address to get the EE uncached address. */
    private static readonly EE_UNCACHED_BASE   = 0x20000000;

    // ── Archive cheat helpers ─────────────────────────────────────────────────

    /**
     * Sanitise a cheat name for use in a label: replace runs of non-alphanumeric
     * characters with a single underscore and strip leading/trailing underscores.
     */
    private static _sanitiseLabelName(name: string): string {
        return name.replace(/[^A-Za-z0-9]+/g, '_').replace(/^_+|_+$/g, '');
    }

    /**
     * Build archive labels from currently-enabled archive cheats.
     *
     * Rules:
     * - Each code line whose address byte is NOT D0 generates a label at the
     *   EE uncached user-space mirror address `0x20AAAAAA` (where `AAAAAA` is
     *   the lower 24-bit address field of the code), provided the address is
     *   in the 32 MB EE RAM range.  This format matches the cheat code address
     *   convention so the label display reflects the same EE memory values as
     *   the cheat codes themselves.
     * - Code lines whose first byte IS D0 are "Joker" addresses.  The very first
     *   joker address encountered (across all enabled cheats) gets a single
     *   `joker` label; all subsequent joker lines are ignored.
     * - Multi-code cheats get numbered suffixes: `Archive_Name_Line_1`, etc.
     *   Single-code cheats get just `Archive_Name`.
     */
    private _buildArchiveLabels(): LabelEntry[] {
        const labels: LabelEntry[] = [];
        let jokerLabelled = false;

        for (const cheat of this._archiveCheats) {
            const safeName = DisasmPanelProvider._sanitiseLabelName(cheat.name);
            // Count how many non-joker codes have valid EE RAM addresses
            const validCodes: Array<{ address: number; line: number }> = [];

            for (let i = 0; i < cheat.codes.length; i++) {
                const code = cheat.codes[i];
                const match = /^([0-9a-f]{2})([0-9a-f]{6})\s+[0-9a-f]{8}/i.exec(code);
                if (!match) { continue; }

                const typeByte = parseInt(match[1], 16);
                const addrField = parseInt(match[2], 16);

                // D0 = Joker/conditional address
                if (typeByte === DisasmPanelProvider.CHEAT_TYPE_JOKER) {
                    if (!jokerLabelled && addrField <= DisasmPanelProvider.MAX_EE_RAM_ADDRESS) {
                        const eeAddr = (DisasmPanelProvider.EE_UNCACHED_BASE | addrField) >>> 0;
                        labels.push({
                            address: `0x${eeAddr.toString(16).padStart(8, '0')}`,
                            label:   'joker',
                        });
                        jokerLabelled = true;
                    }
                    continue; // skip joker for regular label generation
                }

                // Only label addresses in the 32 MB EE RAM range
                if (addrField > DisasmPanelProvider.MAX_EE_RAM_ADDRESS) { continue; }
                validCodes.push({ address: addrField, line: i + 1 });
            }

            for (const { address, line } of validCodes) {
                const eeAddr = (DisasmPanelProvider.EE_UNCACHED_BASE | address) >>> 0;
                const labelName = validCodes.length > 1
                    ? `Archive_${safeName}_Line_${line}`
                    : `Archive_${safeName}`;
                labels.push({
                    address: `0x${eeAddr.toString(16).padStart(8, '0')}`,
                    label:   labelName,
                });
            }
        }

        return labels;
    }

    /**
     * Fetch archive cheats from CheatCodeArchive for the currently running PCSX2
     * game, then broadcast them to the webview and update the label overlays.
     */
    private async handleFetchArchiveCheats(): Promise<void> {
        if (!this._bridgeMode) { return; }

        const out = getOutputChannel();
        this.webview?.postMessage({ command: 'archiveCheatsLoading' });

        let serial = '';
        let gameTitle = '';
        try {
            const info = await this.socketClient.getPcsx2GameInfo();
            // Strip embedded null bytes that the PINE protocol may include in
            // string responses (the null terminator is counted in strSize on
            // the C# side), then trim surrounding whitespace.
            serial    = (info.ID    ?? '').replace(/\0/g, '').trim();
            gameTitle = (info.Title ?? '').replace(/\0/g, '').trim();
        } catch (err: any) {
            out.warn(`Archive cheats: could not get PCSX2 game info — ${err?.message ?? err}`);
            this.webview?.postMessage({ command: 'archiveCheatsError', error: 'Could not read game info from PCSX2.' });
            return;
        }

        if (!serial) {
            this.webview?.postMessage({ command: 'archiveCheatsError', error: 'No game serial available (is a game running?).' });
            return;
        }

        out.info(`Archive cheats: searching for serial "${serial}" (${gameTitle})…`);

        try {
            // ── 1. Try the database first ──────────────────────────────────────
            let dbResult: Awaited<ReturnType<typeof this.socketClient.getGameCheats>> | null = null;
            try {
                dbResult = await this.socketClient.getGameCheats(serial);
            } catch (dbErr: any) {
                out.warn(`Archive cheats: DB query failed — ${dbErr?.message ?? dbErr}`);
            }

            if (dbResult && dbResult.cheats.length > 0) {
                const displayTitle = dbResult.gameTitle || gameTitle;
                this._archiveCheats = dbResult.cheats.map(c => ({
                    name:    c.name,
                    author:  c.author,
                    notes:   c.notes,
                    codes:   c.codes,
                    enabled: false,
                }));
                this._archiveCheatType = dbResult.cheatListTitle ?? '';

                out.info(`Archive cheats: loaded ${this._archiveCheats.length} cheats from database for "${displayTitle}".`);
                this.webview?.postMessage({
                    command:   'archiveCheatsLoaded',
                    cheats:    this._archiveCheats,
                    game:      displayTitle,
                    serial,
                    cheatType: this._archiveCheatType,
                });

                this.editorProvider.setArchiveLabels(this._buildArchiveLabels());
                return;
            }

            // ── 2. Fall back to local YAML archive ────────────────────────────
            out.info(`Archive cheats: no DB results for "${serial}", trying local YAML archive…`);
            const result = await this._fetchArchiveForSerial(serial);
            if (!result) {
                out.warn(`Archive cheats: no entry found for serial "${serial}".`);
                this._archiveCheats    = [];
                this._archiveCheatType = '';
                this.webview?.postMessage({ command: 'archiveCheatsLoaded', cheats: [], game: gameTitle, serial });
                return;
            }

            this._archiveCheats    = result.cheats;
            this._archiveCheatType = result.cheatType;

            out.info(`Archive cheats: loaded ${result.cheats.length} cheats from YAML for "${result.game}".`);
            this.webview?.postMessage({
                command:   'archiveCheatsLoaded',
                cheats:    this._archiveCheats,
                game:      result.game || gameTitle,
                serial,
                cheatType: result.cheatType,
            });

            // Broadcast labels for all archive cheats immediately — labels are
            // always shown regardless of whether the cheat is toggled on.
            this.editorProvider.setArchiveLabels(this._buildArchiveLabels());
        } catch (err: any) {
            out.error(`Archive cheats fetch error: ${err?.message ?? err}`);
            this.webview?.postMessage({ command: 'archiveCheatsError', error: `Fetch failed: ${err?.message ?? err}` });
        }
    }

    /**
     * Toggle a single archive cheat on or off.
     * Enabling a cheat immediately writes its codes into emulator memory.
     * Disabling only updates the label overlays (cheat effects persist in RAM
     * until the game overwrites them).
     */
    private async handleToggleArchiveCheat(index: number, enabled: boolean): Promise<void> {
        if (index < 0 || index >= this._archiveCheats.length) { return; }

        this._archiveCheats[index].enabled = enabled;

        if (enabled && this.socketClient.isConnected()) {
            const cheat = this._archiveCheats[index];
            const cheatText = `#${cheat.name}\n${cheat.codes.join('\n')}`;
            try {
                await this.socketClient.applyCheats(cheatText);
            } catch (err: any) {
                getOutputChannel().error(`Archive cheat apply error: ${err?.message ?? err}`);
            }
        }

        // Always refresh label overlays regardless of apply success.
        this.editorProvider.setArchiveLabels(this._buildArchiveLabels());

        // Reflect updated state back to the webview.
        this.webview?.postMessage({ command: 'archiveCheatsUpdated', cheats: this._archiveCheats });
    }

    /**
     * Re-apply every currently-enabled archive cheat to emulator memory in one batch.
     */
    private async handleApplyAllArchiveCheats(): Promise<void> {
        const enabled = this._archiveCheats.filter(c => c.enabled);
        if (enabled.length === 0) { return; }
        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator.');
            return;
        }

        const lines: string[] = [];
        for (const cheat of enabled) {
            lines.push(`#${cheat.name}`);
            lines.push(...cheat.codes);
        }

        try {
            await this.socketClient.applyCheats(lines.join('\n'));
            this.webview?.postMessage({ command: 'cheatsApplied', success: true });
        } catch (err: any) {
            getOutputChannel().error(`ApplyAllArchiveCheats error: ${err?.message ?? err}`);
            this.webview?.postMessage({ command: 'cheatsApplied', success: false, error: err?.message ?? String(err) });
        }
    }

    /**
     * Run a git command and return its stdout.
     * Rejects if git exits with a non-zero code, with a user-friendly message
     * when git itself is not found in PATH.
     */
    private _runGit(args: string[], cwd?: string): Promise<string> {
        return new Promise((resolve, reject) => {
            const proc = child_process.spawn('git', args, {
                cwd,
                stdio: ['ignore', 'pipe', 'pipe'],
            });
            let stdout = '';
            let stderr = '';
            proc.stdout?.on('data', (d: Buffer) => { stdout += d.toString(); });
            proc.stderr?.on('data', (d: Buffer) => { stderr += d.toString(); });
            proc.on('error', (err: NodeJS.ErrnoException) => {
                if (err.code === 'ENOENT') {
                    reject(new Error(
                        'git not found in PATH — please install Git and ensure it is on your PATH, ' +
                        'then reload the window.'
                    ));
                } else {
                    reject(err);
                }
            });
            proc.on('close', (code: number | null) => {
                if (code === 0) {
                    resolve(stdout);
                } else {
                    reject(new Error(`git ${args[0]} exited with code ${code}: ${stderr.trim()}`));
                }
            });
        });
    }

    /**
     * Clone TylerDev888/CheatCodeArchive (branch: main) into the extension's
     * global storage the first time it is called; on subsequent calls it pulls
     * the latest changes.  Returns the shared in-flight promise if a sync is
     * already running so concurrent callers do not start duplicate operations.
     */
    private _syncArchiveRepo(): Promise<void> {
        if (this._archiveSyncPromise) {
            return this._archiveSyncPromise;
        }

        this._archiveSyncPromise = (async () => {
            const archiveDir = path.join(this._globalStoragePath, 'CheatCodeArchive');
            await fs.promises.mkdir(this._globalStoragePath, { recursive: true });

            const isCloned = fs.existsSync(path.join(archiveDir, '.git'));
            if (isCloned) {
                getOutputChannel().info('CheatCodeArchive: pulling latest changes…');
                await this._runGit(['pull', '--ff-only', ARCHIVE_REMOTE, ARCHIVE_BRANCH], archiveDir);
                getOutputChannel().info('CheatCodeArchive: up to date.');
            } else {
                getOutputChannel().info('CheatCodeArchive: cloning repository…');
                await this._runGit([
                    'clone',
                    '--branch', ARCHIVE_BRANCH,
                    ARCHIVE_REPO_URL,
                    archiveDir,
                ]);
                getOutputChannel().info('CheatCodeArchive: clone complete.');
            }
        })().finally(() => {
            // Allow future start-up syncs to run again (e.g. after a window reload).
            this._archiveSyncPromise = null;
        });

        return this._archiveSyncPromise;
    }

    /**
     * Recursively walk `dir` looking for a `cheats.yaml` file that contains
     * the given PS2 serial as an exact token (not a substring of a longer
     * serial).  Returns the full path of the first match, or null if none found.
     */
    private async _findCheatFile(dir: string, serial: string): Promise<string | null> {
        // Build a pattern that matches the serial only as a standalone value —
        // not as a prefix/suffix of a longer serial like 'SLUS-123456'.
        const escapedSerial = serial.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        const serialRe = new RegExp(`(?<![A-Z0-9-])${escapedSerial}(?![A-Z0-9-])`, 'i');

        let entries: fs.Dirent[];
        try {
            entries = await fs.promises.readdir(dir, { withFileTypes: true });
        } catch {
            return null;
        }

        for (const entry of entries) {
            // Skip hidden directories (e.g. .git)
            if (entry.name.startsWith('.')) { continue; }

            const full = path.join(dir, entry.name);
            if (entry.isDirectory()) {
                const sub = await this._findCheatFile(full, serial);
                if (sub) { return sub; }
            } else if (entry.name === 'cheats.yaml') {
                try {
                    const content = await fs.promises.readFile(full, 'utf-8');
                    if (serialRe.test(content)) { return full; }
                } catch {
                    /* skip unreadable files */
                }
            }
        }
        return null;
    }

    /**
     * Fetch the cheats.yaml from the local CheatCodeArchive clone for the given
     * PS2 serial.  If the archive has not been cloned yet it is cloned now.
     * Returns null when no matching file is found.
     */
    private async _fetchArchiveForSerial(serial: string): Promise<{
        game: string; cheatType: string; cheats: ArchiveCheat[];
    } | null> {
        const archiveDir = path.join(this._globalStoragePath, 'CheatCodeArchive');

        // Clone on demand if the background sync hasn't finished yet (or failed).
        if (!fs.existsSync(path.join(archiveDir, '.git'))) {
            await this._syncArchiveRepo();
        }

        const yamlPath = await this._findCheatFile(archiveDir, serial);
        if (!yamlPath) { return null; }

        const yaml = await fs.promises.readFile(yamlPath, 'utf-8');
        return DisasmPanelProvider._parseArchiveYaml(yaml);
    }

    /**
     * Minimal YAML parser for the specific structure used by CheatCodeArchive.
     * Handles the top-level scalar fields and the `cheats` list.
     */
    private static _parseArchiveYaml(yaml: string): {
        game: string; cheatType: string; cheats: ArchiveCheat[];
    } {
        const field = (key: string): string => {
            const m = new RegExp(`^${key}:\\s*"([^"]*)"`, 'm').exec(yaml);
            return m?.[1] ?? '';
        };

        const game      = field('game');
        const cheatType = field('cheat_type');

        // Split the cheats block into individual entries.
        const cheatsSectionIdx = yaml.indexOf('\ncheats:');
        if (cheatsSectionIdx === -1) {
            return { game, cheatType, cheats: [] };
        }
        const cheatsSection = yaml.substring(cheatsSectionIdx);

        const entryRe = /\n  - name:\s*"([^"]+)"/g;
        const entries: Array<{ name: string; start: number }> = [];
        let m: RegExpExecArray | null;
        while ((m = entryRe.exec(cheatsSection)) !== null) {
            entries.push({ name: m[1], start: m.index });
        }

        const cheats: ArchiveCheat[] = [];
        for (let i = 0; i < entries.length; i++) {
            const start   = entries[i].start;
            const end     = i + 1 < entries.length ? entries[i + 1].start : cheatsSection.length;
            const block   = cheatsSection.substring(start, end);

            const author  = /\n    author:\s*"([^"]*)"/.exec(block)?.[1];
            const notes   = /\n    notes:\s*"([^"]*)"/.exec(block)?.[1];

            const codes: string[] = [];
            const codeRe = /\n      - "([^"]+)"/g;
            let cm: RegExpExecArray | null;
            while ((cm = codeRe.exec(block)) !== null) {
                codes.push(cm[1]);
            }

            if (codes.length > 0) {
                cheats.push({ name: entries[i].name, author, notes, codes, enabled: false });
            }
        }

        return { game, cheatType, cheats };
    }

    /**
     * Open a markdown help file as a VS Code markdown preview panel.
     * `fileName` should be a bare filename such as "CODE_DESIGNER_SOURCE.md"
     * or "CHEAT_ENGINE.md".
     *
     * Resolution order:
     *   1. Extension installation directory (bundled help files).
     *   2. Workspace root / first workspace folder (user-provided overrides).
     */
    private async handleOpenMarkdownHelp(fileName: string): Promise<void> {
        if (!fileName) { return; }

        // Build a list of directories to probe, in priority order:
        //   1. Extension install directory (bundled help files like CHEAT_ENGINE.md)
        //   2. workspaceRoot (where codedesigner-settings.json was found)
        //   3. Every open workspace folder
        //   4. Walk up from extensionPath to find the repo root (handles F5 dev
        //      host where the repo root isn't an open workspace folder)
        const roots = new Set<string>();
        if (this.extensionPath) { roots.add(this.extensionPath); }
        if (this.workspaceRoot) { roots.add(this.workspaceRoot); }
        for (const folder of vscode.workspace.workspaceFolders ?? []) {
            roots.add(folder.uri.fsPath);
        }
        // Walk up from the extension directory — the extension lives at
        // <repo>/vscode/codedesigner/, so ancestor directories may contain
        // the target file (e.g. <repo>/docs/README.md).
        if (this.extensionPath) {
            let dir = this.extensionPath;
            for (let i = 0; i < 5; i++) {
                const parent = path.dirname(dir);
                if (parent === dir) { break; }
                roots.add(parent);
                dir = parent;
            }
        }

        const candidates = [...roots].map(r => path.join(r, fileName));
        const filePath = candidates.find(p => fs.existsSync(p));
        if (!filePath) {
            vscode.window.showWarningMessage(`Help file not found: ${fileName}`);
            return;
        }

        const uri = vscode.Uri.file(filePath);
        await vscode.commands.executeCommand('markdown.showPreview', uri);
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

            const { results, sidecars } = await this.socketClient.compileCds(
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

            // ── Log sidecar commands to the Debug channel ─────────────────────
            for (const sc of sidecars) {
                const addrHex = `0x${sc.Address.toString(16).toUpperCase().padStart(8, '0')}`;
                switch (sc.Kind) {
                    case 'assert': {
                        const expHex = sc.ExpectedValue !== undefined
                            ? `0x${sc.ExpectedValue.toString(16).toUpperCase().padStart(8, '0')}` : '?';
                        if (sc.AssertPassed === true) {
                            out.info(`CDS assert @ ${addrHex} ${sc.Operator ?? '=='} ${expHex} — passed`);
                        } else if (sc.AssertPassed === false) {
                            const actHex = sc.ActualValue !== undefined
                                ? `0x${sc.ActualValue.toString(16).toUpperCase().padStart(8, '0')}` : '?';
                            getDebugChannel().error(
                                `CDS assert @ ${addrHex} ${sc.Operator ?? '=='} ${expHex} FAILED (got ${actHex})`);
                        } else {
                            out.info(`CDS assert @ ${addrHex} ${sc.Operator ?? '=='} ${expHex} (not checked — no live backend)`);
                        }
                        break;
                    }
                    case 'breakpoint':
                        out.info(`CDS breakpoint requested @ ${addrHex} — use SETBP to install`);
                        break;
                    case 'watchpoint':
                        out.info(`CDS watchpoint requested: ${sc.AccessType ?? 'write'} @ ${addrHex} (${sc.WatchSize ?? 4} bytes) — use PINEADDWATCH to install`);
                        break;
                    case 'log': {
                        const directive = sc.Directive ?? 'log';
                        out.info(`CDS ${directive} @ ${addrHex}: ${sc.MessageText ?? ''}`);
                        break;
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
                // Clear any stored sidecars — the patch is not valid when there are errors
                this._lastCdsSidecars = [];
                this.webview?.postMessage({ command: 'cdsCompileResult', success: false, errors });
            } else {
                const codes = codeLines.join('\n');
                // Cache sidecars so handleAddPatch can install breakpoints/watchpoints
                this._lastCdsSidecars = sidecars;
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

    private _scheduleValidateCds(delayMs = 250): void {
        if (this._cdsValidationTimer) {
            clearTimeout(this._cdsValidationTimer);
        }

        const validationVersion = ++this._cdsValidationVersion;
        this._cdsValidationTimer = setTimeout(() => {
            void this._runValidateCds(validationVersion);
        }, delayMs);
    }

    private async _runValidateCds(validationVersion: number): Promise<void> {
        const filePath = this._activeCdsFilePath;
        const sourceCode = this._activeCdsContent;
        if (!filePath || !sourceCode || !filePath.toLowerCase().endsWith('.cds')) { return; }
        if (!this.socketClient.isConnected()) { return; }

        const fileUri = vscode.Uri.file(filePath);
        const openDoc = vscode.workspace.textDocuments.find(d => d.uri.toString() === fileUri.toString());

        try {
            let pointerLabels: LabelEntry[] = [];
            if (this.getPointerLabels) {
                try { pointerLabels = await this.getPointerLabels(); } catch { /* ignore */ }
            }

            const { diagnostics } = await this.socketClient.validateCds(
                sourceCode,
                pointerLabels.length > 0 ? pointerLabels : undefined
            );

            if (validationVersion !== this._cdsValidationVersion) { return; }
            this._cdsDiagnostics.set(fileUri, this._mapCdsDiagnostics(diagnostics, openDoc));
        } catch {
            // Ignore background validation failures; explicit compile still surfaces errors.
        }
    }

    private _mapCdsDiagnostics(
        diagnostics: CdsDiagnostic[],
        openDoc: vscode.TextDocument | undefined
    ): vscode.Diagnostic[] {
        return diagnostics.map(diag => {
            const startLine = Math.max(0, (diag.LineNumber || 1) - 1);
            const endLineRaw = (diag.EndLineNumber || diag.LineNumber || 1) - 1;
            const endLine = Math.max(startLine, endLineRaw);

            const startChar = Math.max(0, (diag.ColumnNumber || 1) - 1);
            const fallbackEndChar = openDoc && startLine < openDoc.lineCount
                ? openDoc.lineAt(startLine).text.length
                : startChar + 1;
            const endChar = Math.max(
                startChar + 1,
                diag.EndColumnNumber ? diag.EndColumnNumber - 1 : fallbackEndChar
            );

            const range = new vscode.Range(startLine, startChar, endLine, endChar);
            const severity = diag.Severity === 'Error'
                ? vscode.DiagnosticSeverity.Error
                : diag.Severity === 'Warning'
                    ? vscode.DiagnosticSeverity.Warning
                    : vscode.DiagnosticSeverity.Information;

            const vscodeDiagnostic = new vscode.Diagnostic(range, diag.Message, severity);
            vscodeDiagnostic.source = 'CDS';
            if (diag.Code) {
                vscodeDiagnostic.code = diag.Code;
            }
            return vscodeDiagnostic;
        });
    }

    /** Apply compiled GameShark codes to live memory (Add Patch), then install any sidecar breakpoints/watchpoints. */
    private async handleAddPatch(codes: string): Promise<void> {
        const out = getOutputChannel();
        if (!this.socketClient.isConnected()) {
            this.webview?.postMessage({ command: 'cdsPatchApplied', success: false, error: 'Not connected to emulator.' });
            return;
        }
        try {
            // 1 — Apply the GameShark memory-patch codes
            await this.socketClient.applyCheats(codes);
            out.info('CDS patch applied to memory successfully.');

            // 2 — Install sidecar commands from the most recent compilation
            const sidecars = this._lastCdsSidecars;
            for (const sc of sidecars) {
                const addrHex = `0x${sc.Address.toString(16).toUpperCase().padStart(8, '0')}`;
                try {
                    switch (sc.Kind) {
                        case 'breakpoint':
                            await this.socketClient.setBreakpoint(sc.Address);
                            out.info(`CDS breakpoint installed @ ${addrHex}`);
                            break;

                        case 'watchpoint': {
                            // Watchpoint covers [Address, Address + WatchSize)
                            const size    = sc.WatchSize ?? 4;
                            // Guard against address + size wrapping past 0xFFFFFFFF
                            if (sc.Address > 0xFFFFFFFF - size) {
                                throw new Error(`Watchpoint range overflow: 0x${sc.Address.toString(16).toUpperCase()} + ${size} exceeds 32-bit address space`);
                            }
                            const endAddr = sc.Address + size;
                            // Condition: 1=read, 2=write, 3=readwrite
                            const cond = sc.AccessType === 'read'      ? 1
                                       : sc.AccessType === 'readwrite' ? 3
                                       : 2; // default: write
                            await this.socketClient.addPineWatch(sc.Address, endAddr, cond);
                            out.info(`CDS watchpoint installed: ${sc.AccessType ?? 'write'} @ ${addrHex} (${size} bytes)`);
                            break;
                        }

                        case 'log': {
                            const directive = sc.Directive ?? 'log';
                            out.info(`CDS ${directive} @ ${addrHex}: ${sc.MessageText ?? ''}`);
                            break;
                        }
                    }
                } catch (sidecarErr: any) {
                    // Log but do not abort — sidecar install is best-effort (e.g. breakpoints
                    // require PINE debugger extension; watchpoints require PINE mode).
                    out.warn(`CDS sidecar install failed for ${sc.Kind} @ ${addrHex}: ${sidecarErr?.message ?? sidecarErr}`);
                }
            }

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
        this._scheduleValidateCds(50);
        for (const r of LIVE_REGIONS_DEFAULT) {
            await this.editorProvider.openLiveMemoryView(r.region, r.address, r.size, r.label);
        }

        // Pull authoritative user preferences from the DB and refresh the UI.
        await this.loadUserPreferencesFromDb();

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
     * Open the default live memory view panels immediately, even if not connected.
     * Panels will show placeholder content until the socket connects.
     * Called at extension activation so the EE RAM tab is always visible.
     * Rarely-used regions (IOP, BIOS, Scratchpad) can be opened on-demand via
     * the sidebar menu to reduce socket traffic and prevent flickering.
     */
    public async openAllLivePanels(): Promise<void> {
        for (const r of LIVE_REGIONS_DEFAULT) {
            await this.editorProvider.openLiveMemoryView(r.region, r.address, r.size, r.label);
        }
        this.editorProvider.openIntroPanel();
    }

    /**
     * Open an optional memory view by label name.
     * Called from the sidebar when the user chooses to open IOP/BIOS/Scratchpad.
     */
    public async openOptionalMemoryView(label: string): Promise<void> {
        const r = LIVE_REGIONS_OPTIONAL.find(r => r.label === label);
        if (r) {
            await this.editorProvider.openLiveMemoryView(r.region, r.address, r.size, r.label);
        } else {
            getOutputChannel().warn(`Unknown optional memory view: '${label}'`);
        }
    }

    /**
    * Auto-start the CodeDesigner console app in default control-server mode.
     * Called at extension activation when auto-connect fails.
     * Looks for CodeDesigner.Ps2.Bridge in the install directory first, then
     * falls back to any non-PCSX2 emulator configured in emu-settings.json.
     * Returns true if a terminal was spawned (or was already running), false otherwise.
     */
    public async autoStartServer(): Promise<boolean> {
        // Don't spawn a second terminal if one is already open
        if (this.emulatorTerminal) {
            getOutputChannel().info('Auto-start: server terminal is already running.');
            return true;
        }

        // Primary: Bridge deployed to the install directory.
        const resolvedExe = DisasmPanelProvider.findBridgeExe();
        if (!resolvedExe) {
            const installDir = DisasmPanelProvider.getInstallDirectory();
            getOutputChannel().warn(
                `Auto-start: CodeDesigner.Ps2.Bridge not found in ${installDir}. ` +
                'Build the Bridge project (dotnet build CodeDesigner.Ps2.Bridge) to deploy it automatically.'
            );
            return false;
        }

        getOutputChannel().info(`Auto-starting CodeDesigner control server: ${resolvedExe}`);

        const terminal = vscode.window.createTerminal({
            name: 'CodeDesigner',
            shellPath: resolvedExe,
            shellArgs: [],
        });
        terminal.show(false);   // reveal terminal but keep editor focus
        this.emulatorTerminal = terminal;

        // Poll until the socket server is ready (up to ~10 s), then connect.
        this.connectSocketAndOpenViews();

        return true;
    }

    // ── MCP Memory Manager ────────────────────────────────────────────────────

    private async handleMcpMemoryList(): Promise<void> {
        try {
            const entries = await this.socketClient.mcpMemoryList();
            this.webview?.postMessage({ command: 'mcpMemoryListResult', entries });
        } catch (err: any) {
            this.webview?.postMessage({ command: 'mcpMemoryListResult', error: err?.message ?? String(err) });
        }
    }

    private async handleMcpMemoryAdd(name: string): Promise<void> {
        try {
            const entry = await this.socketClient.mcpMemoryAdd(name, '');
            this.webview?.postMessage({ command: 'mcpMemoryActionResult', entry, message: `Created '${entry.name}'` });
        } catch (err: any) {
            this.webview?.postMessage({ command: 'mcpMemoryActionResult', error: err?.message ?? String(err) });
        }
    }

    private async handleMcpMemoryDelete(id: number): Promise<void> {
        try {
            await this.socketClient.mcpMemoryDelete(id);
            this.webview?.postMessage({ command: 'mcpMemoryActionResult', message: 'Deleted.' });
        } catch (err: any) {
            this.webview?.postMessage({ command: 'mcpMemoryActionResult', error: err?.message ?? String(err) });
        }
    }

    private async handleMcpMemoryImport(): Promise<void> {
        const uris = await vscode.window.showOpenDialog({
            canSelectMany: false,
            filters: { 'Markdown': ['md'] },
            title: 'Import MCP Memory Entry',
        });
        if (!uris?.length) { return; }
        try {
            const entry = await this.socketClient.mcpMemoryImport(uris[0].fsPath);
            this.webview?.postMessage({ command: 'mcpMemoryActionResult', entry, message: `Imported '${entry.name}'` });
        } catch (err: any) {
            this.webview?.postMessage({ command: 'mcpMemoryActionResult', error: err?.message ?? String(err) });
        }
    }

    private async handleMcpMemoryExport(id: number, name: string): Promise<void> {
        const uri = await vscode.window.showSaveDialog({
            defaultUri: vscode.Uri.file(`${name}.md`),
            filters: { 'Markdown': ['md'] },
            title: 'Export MCP Memory Entry',
        });
        if (!uri) { return; }
        try {
            await this.socketClient.mcpMemoryExport(id, uri.fsPath);
            vscode.window.showInformationMessage(`MCP Memory '${name}' exported to ${uri.fsPath}`);
        } catch (err: any) {
            vscode.window.showErrorMessage(`MCP Memory export failed: ${err?.message ?? err}`);
        }
    }

    // ── File mode (internal emulator, no BIOS) ────────────────────────────────

    /**
     * Load a PS2 ELF, BIN, or RAW file into the internal emulator in file mode
     * (STARTFILEMODE command).  The emulator is initialised with a bare scheduler —
     * no BIOS, no disc — and left paused at the file's entry point.  All default
     * live memory panels are opened so the user can inspect memory immediately.
     *
     * Called from extension.ts when the user opens a registered file type
     * (.elf, .bin, .raw) via the standard VS Code File → Open menu.
     */
    public async openFileMode(filePath: string): Promise<void> {
        const out = getOutputChannel();
        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator. Cannot open file.');
            return;
        }

        out.info(`Opening file in internal emulator (file mode): ${filePath}`);
        try {
            const result = await this.socketClient.startFileMode(filePath);
            const entryHex = result.entryPoint.toString(16).toUpperCase().padStart(8, '0');
            out.info(`File mode started — entry point 0x${entryHex}`);

            for (const r of LIVE_REGIONS_DEFAULT) {
                await this.editorProvider.openLiveMemoryView(r.region, r.address, r.size, r.label);
            }

            vscode.window.showInformationMessage(
                `File loaded. Entry point: 0x${entryHex}. Emulator is paused — set breakpoints then press Run.`
            );
        } catch (err: any) {
            out.error(`File mode failed: ${err?.message ?? err}`);
            vscode.window.showErrorMessage(`File mode failed: ${err?.message ?? err}`);
        }
    }
}
