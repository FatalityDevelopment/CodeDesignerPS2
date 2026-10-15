import * as vscode from 'vscode';
import * as fs from 'fs';
import * as net from 'net';
import * as os from 'os';
import * as path from 'path';
import * as child_process from 'child_process';
import { getOutputChannel, getCdsCompilerChannel } from '../../extension';
import { SocketClient, LabelEntry, DiscElfEntry, CdsSidecarResult, CdsDiagnostic } from '../../socketClient';
import { getNonce, getWebviewErrorHandlerScript } from '../../utils/nonce';
import { DisasmEditorProvider } from '../disasmEditor/disasmEditorProvider';
import { AgentCtrlPanelProvider } from '../agentCtrlPanel/agentCtrlPanelProvider';
import { McpProgressReportEditorProvider } from '../mcpProgressReportEditor/mcpProgressReportEditorProvider';
import { PacketViewerProvider, loadMediusPorts } from '../packetViewer/packetViewerProvider';
import { SnapshotManagerProvider, SnapshotDataDelegate } from '../snapshotManager/snapshotManagerProvider';
import { DebugCommandsProvider } from '../debugCommands/debugCommandsProvider';
import { DiffViewerProvider } from '../diffViewer/diffViewerProvider';
import { MemoryRegion } from '../../shared/memoryRegions';
import { JokerMakerProvider } from '../jokerMaker/jokerMakerProvider';
import { CompatBrowserProvider } from '../compatBrowser/compatBrowserProvider';
import { ArchiveCheat, SnapshotEntry } from '../../webview/disasm-panel/types';

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

interface SnapshotInfoFile {
    name: string;
    timestamp: string;
    isoName: string;
    regions: Array<{ Label: string; File: string; BaseAddress: number; Size: number }>;
}

/**
 * Single source of truth for every memory region the disassembler / hex viewer
 * can open. Each entry pairs:
 *
 *   - `memoryRegion`: the canonical {@link MemoryRegion} enum (shared with
 *     the Bridge); used by region-aware navigation to find the matching panel
 *     and to auto-open it when none exists yet.
 *   - `region`:       the legacy `'ee' | 'iop'` tag passed through to
 *     `HandleReadMem` so the backend knows which address space to read.
 *   - `address`:      the base address rendered by the panel (uses physical /
 *     mirror form so rows align with the documented memory map).
 *   - `size`:         span covered by the panel.
 *   - `label`:        unique human label, also the key for
 *     {@link openOptionalMemoryView} and the panel reuse lookup.
 *   - `defaultOpen`:  true for regions auto-opened on emulator connect.
 *
 * Adding a new region (e.g. VU0 micro) requires only an entry here — the
 * sidebar menu and the auto-open path both pull from this table.
 */
export interface LiveRegionDef {
    memoryRegion: MemoryRegion;
    region:       'ee' | 'iop';
    address:      number;
    size:         number;
    label:        string;
    defaultOpen:  boolean;
}

const LIVE_REGIONS_ALL: LiveRegionDef[] = [
    // ── EE side ─────────────────────────────────────────────────────────
    {
        // EE RAM at physical base 0x00000000-0x01FFFFFF. Rows render at the
        // physical address regardless of which virtual alias the CPU uses
        // (KSEG0 0x80000000, uncached 0x20100000, accelerated 0x30100000) —
        // HandleReadMem accepts the physical form directly.
        memoryRegion: MemoryRegion.EeRam,        region: 'ee',
        address: 0x00000000, size: 32 * 1024 * 1024, label: 'EE RAM',        defaultOpen: true,
    },
    {
        // BIOS ROM at physical 0x1FC00000. Both KSEG0 0x9FC... and KSEG1
        // 0xBFC... aliases strip to the same physical address via toPhysAddr.
        memoryRegion: MemoryRegion.EeBiosRom,    region: 'ee',
        address: 0x1FC00000, size:  4 * 1024 * 1024, label: 'BIOS ROM',      defaultOpen: false,
    },
    {
        memoryRegion: MemoryRegion.EeScratchpad, region: 'ee',
        address: 0x70000000, size:       16 * 1024,  label: 'Scratchpad',    defaultOpen: false,
    },
    {
        // EE hardware-register window (timers, DMAC, GIF, VIF, IPU, GS priv).
        // PINE/Read32 can read individual register addresses inside this span;
        // showing the lower 64 KB keeps the page count manageable.
        memoryRegion: MemoryRegion.EeHardware,   region: 'ee',
        address: 0x10000000, size:       64 * 1024,  label: 'EE HW regs',    defaultOpen: false,
    },

    // ── IOP side (mirrored into EE VTLB at 0x1C000000+) ─────────────────
    {
        // PCSX2's vtlb_MapHandler maps the IOP mirror as 8 MB (covers the devkit
        // ExtraIopRam case); retail games only use the first 2 MB.
        memoryRegion: MemoryRegion.IopRam,       region: 'iop',
        address: 0x1C000000, size:  8 * 1024 * 1024, label: 'IOP RAM',       defaultOpen: false,
    },
    {
        // EE-side window into IOP hardware regs (timers, DMAC, SIF, USB, …).
        memoryRegion: MemoryRegion.IopHardware,  region: 'ee',
        address: 0x1F801000, size:       32 * 1024,  label: 'IOP HW regs',   defaultOpen: false,
    },

    // ── Graphics Synthesizer ────────────────────────────────────────────
    {
        // PMODE, SMODE1/2, SRFSH, SYNCH1/2, SYNCV, DISPFB1/2, DISPLAY1/2,
        // EXTBUF/DATA/WRITE, BGCOLOR, CSR, IMR, BUSDIR, SIGLBLID.
        memoryRegion: MemoryRegion.GsPrivRegs,   region: 'ee',
        address: 0x12000000, size:        8 * 1024,  label: 'GS priv regs',  defaultOpen: false,
    },

    // ── Additional BIOS-era ROMs (PCSX2 Memory.cpp:415-417) ─────────────
    {
        memoryRegion: MemoryRegion.Rom1,         region: 'ee',
        address: 0x1E000000, size:  4 * 1024 * 1024, label: 'ROM1 (DVD)',    defaultOpen: false,
    },
    {
        memoryRegion: MemoryRegion.Rom2,         region: 'ee',
        address: 0x1E400000, size:  4 * 1024 * 1024, label: 'ROM2 (CN ext)', defaultOpen: false,
    },

    // ── Vector Units (EE coprocessors; accessed through EE address space) ─
    {
        memoryRegion: MemoryRegion.Vu0Code,      region: 'ee',
        address: 0x11000000, size:        4 * 1024,  label: 'VU0 micro',     defaultOpen: false,
    },
    {
        memoryRegion: MemoryRegion.Vu0Data,      region: 'ee',
        address: 0x11004000, size:        4 * 1024,  label: 'VU0 data',      defaultOpen: false,
    },
    {
        memoryRegion: MemoryRegion.Vu1Code,      region: 'ee',
        address: 0x11008000, size:       16 * 1024,  label: 'VU1 micro',     defaultOpen: false,
    },
    {
        memoryRegion: MemoryRegion.Vu1Data,      region: 'ee',
        address: 0x1100C000, size:       16 * 1024,  label: 'VU1 data',      defaultOpen: false,
    },
];

/** Subset that auto-opens on emulator connect — keeps boot UX uncluttered. */
const LIVE_REGIONS_DEFAULT = LIVE_REGIONS_ALL.filter(r => r.defaultOpen);

/** Everything else — surfaced through the sidebar Memory Region menu. */
const LIVE_REGIONS_OPTIONAL = LIVE_REGIONS_ALL;

/** All memory regions combined — used when the user explicitly opens all views (restart, etc.). */
const LIVE_REGIONS = LIVE_REGIONS_ALL;

/** URL and branch for the cheat code archive repository. */
const ARCHIVE_REPO_URL    = 'https://github.com/TylerDev888/CheatCodeArchive.git';
const ARCHIVE_REMOTE      = 'origin';
const ARCHIVE_BRANCH      = 'main';

export class DisasmPanelProvider implements vscode.WebviewViewProvider {
    private webview?: vscode.Webview;
    private emulatorTerminal?: vscode.Terminal;
    /**
     * Terminal hosting CodeDesigner.Ps2.Emulator.exe (built-in emulator
     * process). Set by {@link handleStartEmulatorProcess}; cleared by the
     * onDidCloseTerminal hook so a subsequent Run respawns the process.
     */
    private emulatorProcessTerminal?: vscode.Terminal;
    /** Terminal hosting the CodeDesigner.AI MCP stdio server when launched from the panel. */
    private mcpServerTerminal?: vscode.Terminal;
    private settings: EmuSettings = {};
    private workspaceRoot: string = '';
    private snapshots: SnapshotEntry[] = [];
    private extensionPath: string = '';
    private selectedEmulatorPath: string = '';
    private selectedBiosPath: string = '';
    private selectedGamePath: string = '';
    private selectedGameName: string = '';
    /** Disc serial of the currently selected game (set by handleGameChanged). Empty when unknown. */
    private _currentGameSerial: string = '';
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
    /** Monotonic token used to invalidate stale socket retry loops. */
    private _connectAttemptVersion = 0;
    /** Timer handle for deferred socket retry; cleared before scheduling a new retry. */
    private _connectRetryTimer: ReturnType<typeof setTimeout> | undefined;
    /** Track whether this session launched PCSX2 so we can clean up orphans on disconnect/restart. */
    private _launchedPcsx2InSession = false;
    /** PID of a PCSX2 process launched by this VS Code session (if available). */
    private _launchedPcsx2Pid: number | null = null;
    /** Terminal used for PCSX2 launch/management. */
    private _pcsx2Terminal?: vscode.Terminal;
    /** Poll timer tailing the PCSX2 emu log. */
    private _pcsx2LogTimer?: ReturnType<typeof setInterval>;
    /** Byte offset already parsed in the PCSX2 emu log. */
    private _pcsx2LogOffset = 0;
    /** Unique file paths discovered in PCSX2 console output. */
    private _pcsx2DiscoveredPaths = new Set<string>();
    /** True once the PCSX2 emu log has reported "GSStream: listening". */
    private _pcsx2GraphicsReady = false;
    /** Callbacks waiting for PCSX2's GS stream to come up. */
    private _pcsx2ReadyWaiters = new Set<() => void>();
    /** Standalone Controller webview, registered from extension.ts after provider setup. */
    private _jokerMakerProvider?: JokerMakerProvider;

    /** Returns the workspace root directory (used by DisasmEditorProvider for history lookup). */
    public getWorkspaceRoot(): string { return this.workspaceRoot; }
    /** Returns the ISO name derived from the currently selected game path. */
    public getSelectedGameName(): string { return this.selectedGameName; }
    /** Path of the selected emulator (used by the Welcome page status box). */
    public getSelectedEmulatorPath(): string { return this.selectedEmulatorPath; }
    /** Path of the selected BIOS (used by the Welcome page status box). */
    public getSelectedBiosPath(): string { return this.selectedBiosPath; }
    /** Disc serial of the currently selected/loaded game (e.g. SCUS-97275). */
    public getCurrentGameSerial(): string { return this._currentGameSerial; }
    /** Bridge socket connection state for the Welcome page status box. */
    public isBridgeConnected(): boolean { return this.socketClient.isConnected(); }
    /** True when the selected emulator process is running. Wrapper for Welcome page. */
    public async isSelectedEmulatorRunning(): Promise<boolean> {
        return this.detectSelectedEmulatorProcessRunning(this.selectedEmulatorPath);
    }
    /** Friendly display label for the selected emulator, derived from its settings entry. */
    public getSelectedEmulatorLabel(): string {
        const sel = this.selectedEmulatorPath;
        if (!sel) { return ''; }
        const entry = (this.settings.emulators ?? []).find(e => e.path === sel);
        if (entry?.type) { return entry.type; }
        return path.basename(sel, path.extname(sel));
    }
    /** Friendly display label for the selected BIOS. */
    public getSelectedBiosLabel(): string {
        const sel = this.selectedBiosPath;
        if (!sel) { return ''; }
        const entry = (this.settings.bios ?? []).find(b => b.path === sel);
        if (entry?.name) { return entry.name; }
        return path.basename(sel, path.extname(sel));
    }
    /**
     * Toggle the emulator connection — invoked by the panel header
     * (view/title) Connect/Disconnect action. Uses the panel's currently
     * selected emulator / BIOS / game paths.
     */
    public toggleConnectionFromCommand(): void {
        this.handleConnect(this.selectedEmulatorPath, this.selectedBiosPath, this.selectedGamePath);
    }
    /** Provides the standalone Controller panel used by the cheats tab. */
    public setJokerMakerProvider(provider: JokerMakerProvider): void {
        this._jokerMakerProvider = provider;
    }
    /**
     * Optional callback invoked during CDS compilation to obtain the current set of
     * live-resolved Pointer Manager labels (e.g. "seal.health" → 0x8012AB34).
     * Set by extension.ts after the providers are wired together.
     */
    public getPointerLabels?: () => Promise<LabelEntry[]>;

    /** Opens the shared AI agent control panel, optionally focused on a specific tab. */
    public openAgentCtrlPanel(focus?: { tab?: string; agentId?: number; orchestrationId?: number }): void { this._agentCtrlPanelProvider.open(focus); }

    constructor(
        private readonly socketClient: SocketClient,
        private readonly editorProvider: DisasmEditorProvider,
        private readonly _extensionUri: vscode.Uri
    ) {
        this._mcpProgressReportEditorProvider = new McpProgressReportEditorProvider(socketClient, _extensionUri);
        this._agentCtrlPanelProvider = new AgentCtrlPanelProvider(
            _extensionUri, socketClient,
            () => DisasmPanelProvider.detectMcpServerRunning(this.mcpImageName()),
            () => this.handleRunMcpServer(),
        );
        this._compatBrowserProvider   = new CompatBrowserProvider(_extensionUri, socketClient);
        this._packetViewerProvider    = new PacketViewerProvider(_extensionUri, socketClient);
        this._debugCommandsProvider   = new DebugCommandsProvider(_extensionUri);

        const delegate: SnapshotDataDelegate = {
            getSnapshots:    () => this.getPcsx2Savestates(),
            openSnapshot:    (p) => this.handleOpenSnapshot(p),
            reloadSnapshot:  (p) => this.handleLoadSnapshot(p),
            getP2sScreenshot: (p) => this.handleReadP2sScreenshot(p),
        };
        this._snapshotManagerProvider = new SnapshotManagerProvider(_extensionUri, delegate);
        this._diffViewerProvider      = new DiffViewerProvider(_extensionUri, delegate, socketClient);
    }

    private readonly _mcpProgressReportEditorProvider: McpProgressReportEditorProvider;
    private readonly _agentCtrlPanelProvider: AgentCtrlPanelProvider;
    private readonly _compatBrowserProvider: CompatBrowserProvider;
    private readonly _packetViewerProvider: PacketViewerProvider;
    private readonly _debugCommandsProvider: DebugCommandsProvider;
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
                if (t === this.emulatorProcessTerminal) {
                    getOutputChannel().info('Built-in emulator process exited.');
                    this.emulatorProcessTerminal = undefined;
                    void this.postEmulatorProcessStatus();
                }
                if (t === this.mcpServerTerminal) {
                    getOutputChannel().info('CodeDesigner MCP server terminal closed.');
                    this.mcpServerTerminal = undefined;
                    void this.handleRequestMcpStatus();
                }
                if (t === this._pcsx2Terminal) {
                    getOutputChannel().info('Emulator launcher terminal closed.');
                    this._pcsx2Terminal = undefined;
                    void this.postEmulatorProcessStatus();
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
                case 'runPcsx2':
                    this.handleRunPcsx2(
                        msg.emulatorPath ?? this.selectedEmulatorPath,
                        msg.biosPath ?? this.selectedBiosPath,
                        msg.gamePath ?? this.selectedGamePath
                    );
                    break;
                case 'killEmulatorProcess':
                    await this.handleKillEmulatorProcess(msg.emulatorPath ?? this.selectedEmulatorPath);
                    break;
                case 'snapshot':
                    await this.handleSnapshot();
                    break;
                case 'snapshotPcsx2':
                    await this.handleSnapshotPcsx2();
                    break;
                case 'openGsViewer':
                    await vscode.commands.executeCommand('codedesigner.openGsFrameViewer');
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
                case 'openJokerMaker':
                    this._jokerMakerProvider?.open();
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
                // ── AI agent control panel ──────────────────────────────────────
                case 'openAiConfig':
                    this._agentCtrlPanelProvider.open();
                    break;
                // ── PS2 Game Compatibility browser ────────────────────────
                case 'openCompatBrowser':
                    this._compatBrowserProvider.open();
                    break;
                // ── PS2 Cheats editor ─────────────────────────────────────
                case 'openCheatsEditor':
                    vscode.commands.executeCommand('codedesigner.openCheatsEditor');
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
                // ── Memory Card Browser ───────────────────────────────────
                case 'openMemoryCard':
                    await vscode.commands.executeCommand('codedesigner.openMemoryCardBrowser');
                    break;
                case 'openZipperWorkbench':
                    await vscode.commands.executeCommand('codedesigner.openZipperWorkbench');
                    break;
                // ── Asset editors (Assets section) ────────────────────────
                case 'openAssetEditor': {
                    const commands: Record<string, string> = {
                        model: 'codedesigner.openModelEditor',
                        map: 'codedesigner.openMapEditor',
                        animation: 'codedesigner.openAnimationEditor',
                    };
                    const command = commands[String(msg.kind)];
                    if (command) { await vscode.commands.executeCommand(command); }
                    break;
                }
                // ── HDD Image Manager ─────────────────────────────────────
                case 'openHdd':
                    await vscode.commands.executeCommand('codedesigner.openHddBrowser');
                    break;
                // ── DNS Mapping editor ────────────────────────────────────
                case 'openDnsMapping':
                    await this.handleOpenDnsMapping();
                    break;
                // ── Multiplayer config editor ─────────────────────────────
                case 'openNetConfig':
                    await this.handleOpenNetConfig();
                    break;
                // ── Debug commands page (Mods/debug-commands.json) ────────
                case 'openDebugCommands':
                    this._debugCommandsProvider.open();
                    break;
                // ── Multiplayer server reachability + capture state ───────
                case 'requestNetworkStatus':
                    await this.handleRequestNetworkStatus();
                    break;
                // ── MCP server process status ─────────────────────────────
                case 'requestMcpStatus':
                    await this.handleRequestMcpStatus();
                    break;
                case 'runMcpServer':
                    await this.handleRunMcpServer();
                    break;
                case 'requestEmulatorProcessStatus':
                    await this.handleRequestEmulatorProcessStatus();
                    break;
                // ── AI agent profiles ─────────────────────────────────────
                case 'requestAgents':
                    await this.handleRequestAgents();
                    break;
                case 'setAgentLead':
                    await this.handleSetAgentLead(msg.agentId as number);
                    break;
                case 'openAiConfigAgents':
                    this._agentCtrlPanelProvider.open({
                        tab:     'agents',
                        agentId: typeof msg.agentId === 'number' ? msg.agentId : undefined,
                    });
                    break;
                // ── Orchestration plans ───────────────────────────────────
                case 'requestOrchestrations':
                    await this.handleRequestOrchestrations();
                    break;
                case 'setOrchestrationActive':
                    await this.handleSetOrchestrationActive(msg.id as number);
                    break;
                case 'setMultiAgentMode':
                    await this.handleSetMultiAgentMode(!!msg.enabled);
                    break;
                case 'openAiConfigOrchestration':
                    this._agentCtrlPanelProvider.open({
                        tab:             'orchestration',
                        orchestrationId: typeof msg.id === 'number' ? msg.id : undefined,
                    });
                    break;
                // ── PC navigation history ─────────────────────────────────
                case 'requestNavHistory':
                    await this.handleRequestNavHistory();
                    break;
                case 'clearNavHistory':
                    await this.handleClearNavHistory();
                    break;
                // ── Recent labels (active label set) ──────────────────────
                case 'requestRecentLabels':
                    await this.handleRequestRecentLabels();
                    break;
                // ── Jump the disassembler editor to an address ────────────
                case 'panelGotoAddress':
                    this.handlePanelGotoAddress(msg.address);
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
     * Returns the path to CodeDesigner.Ps2.Emulator.exe (the built-in PS2
     * emulator host) in the install directory, or null if it does not exist.
     * Symmetric with {@link findBridgeExe}; the post-build deploy step in the
     * Emulator project copies the exe alongside Bridge.
     */
    private static findEmulatorExe(): string | null {
        const installDir = DisasmPanelProvider.getInstallDirectory();
        const exeName = process.platform === 'win32'
            ? 'CodeDesigner.Ps2.Emulator.exe'
            : 'CodeDesigner.Ps2.Emulator';
        const candidate = path.join(installDir, exeName);
        return fs.existsSync(candidate) ? candidate : null;
    }

    /**
     * Returns the deployed CodeDesigner.AI executable path (the profile-driven
     * MCP server), or null when it has not been built/deployed yet.
     */
    private static findAiMcpServerExe(): string | null {
        const installDir = path.join(DisasmPanelProvider.getInstallDirectory(), 'AI');
        const exeName = process.platform === 'win32'
            ? 'CodeDesigner.AI.exe'
            : 'CodeDesigner.AI';
        const candidate = path.join(installDir, exeName);
        return fs.existsSync(candidate) ? candidate : null;
    }

    /** The process image name for the CodeDesigner.AI MCP server. */
    private mcpImageName(): string {
        return 'CodeDesigner.AI';
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
        //    Keep any user-selected PCSX2-compatible emulator entries too; the
        //    Bridge is resolved from the install directory, never from this list.
        //    BIOS files must be provided and configured by the user.
        const pcsx2Exe = DisasmPanelProvider.detectPcsx2Exe();
        const existing = this.settings.emulators ?? [];
        if (pcsx2Exe) {
            if (!existing.some(e => e.path === pcsx2Exe)) {
                this.settings.emulators = [
                    { type: 'PCSX2', path: pcsx2Exe },
                    ...existing,
                ];
            } else {
                this.settings.emulators = existing;
            }
        } else {
            this.settings.emulators = existing;
        }

        // Dedup games by path (case-insensitive). Keeps the first occurrence —
        // legacy settings files sometimes accumulate duplicate entries after a
        // Windows drive letter changes case between sessions.
        if (this.settings.games?.length) {
            const seenPaths = new Set<string>();
            const deduped: typeof this.settings.games = [];
            for (const g of this.settings.games) {
                const key = (g.path ?? '').toLowerCase();
                if (!key || seenPaths.has(key)) { continue; }
                seenPaths.add(key);
                deduped.push(g);
            }
            this.settings.games = deduped;
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
            // Offline fallback — write to the JSON file only.  Preserve any
            // unknown fields (e.g. `version`) the user / packager added so a
            // round-trip through this provider does not silently drop them.
            const settingsPath = this.settingsJsonPath;
            if (!settingsPath) { return; }
            let existing: Record<string, unknown> = {};
            try {
                const raw = await fs.promises.readFile(settingsPath, 'utf-8');
                const parsed = JSON.parse(raw);
                if (parsed && typeof parsed === 'object') { existing = parsed; }
            } catch { /* file may not exist yet — that's fine */ }
            const merged = { ...existing, ...prefs };
            try {
                await fs.promises.writeFile(settingsPath, JSON.stringify(merged, null, 2), 'utf-8');
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
                try {
                    const parsed: Array<{ name: string; path: string }> = JSON.parse(rawGames);
                    // Dedup by path (case-insensitive) — see loadEmuSettings for context.
                    const seen = new Set<string>();
                    this.settings.games = parsed.filter(g => {
                        const key = (g.path ?? '').toLowerCase();
                        if (!key || seen.has(key)) { return false; }
                        seen.add(key);
                        return true;
                    });
                    changed = true;
                } catch { /* ignore */ }
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
                    let existing: Record<string, unknown> = {};
                    try {
                        const raw = await fs.promises.readFile(prefsPath, 'utf-8');
                        const parsed = JSON.parse(raw);
                        if (parsed && typeof parsed === 'object') { existing = parsed; }
                    } catch { /* may not exist yet */ }
                    const merged = { ...existing, ...prefs };
                    try {
                        await fs.promises.writeFile(prefsPath, JSON.stringify(merged, null, 2), 'utf-8');
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
        // User-selected emulator entries appear in the selector; Bridge is auto-detected.
        const emulators = (this.settings.emulators ?? []).map(e => ({ label: e.type, value: e.path }));
        // BIOS options come from user configuration only (BIOS files must be provided by the user).
        const biosOptions = (this.settings.bios ?? []).map(b => ({ label: b.name, value: b.path }));
        const gameOptions = (this.settings.games ?? []).map(g => ({ label: g.name, value: g.path }));
        const iconUri = this.webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'dist', 'resources', 'codedesigner-icon.png'),
        ).toString();
        this.webview.postMessage({
            command: 'init',
            emulators,
            biosOptions,
            gameOptions,
            iconUri,
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
        void this.postEmulatorProcessStatus();
        void this.postLoadedGame();
    }

    /**
     * Resolve the canonical PS2 disc serial (e.g. "SCUS-97275") for the
     * currently selected ISO. Tries a running PCSX2 first, then reads
     * SYSTEM.CNF from the ISO directly. Returns undefined when neither
     * source yields a serial — callers must NOT substitute the ISO path.
     */
    private async _resolveSerialForGamePath(gamePath: string): Promise<string | undefined> {
        if (!this.socketClient.isConnected()) { return undefined; }

        // 1. Live PCSX2 PINE — only useful if the same game is loaded there.
        try {
            const info = await this.socketClient.getPcsx2GameInfo();
            const serial = (info.ID ?? '').replace(/\0/g, '').trim();
            if (serial) { return serial.toUpperCase(); }
        } catch { /* PCSX2 not running — fall through to ISO read */ }

        // 2. Static ISO read — works even without an attached emulator.
        try {
            const bootElf = await this.socketClient.isoBootElf(gamePath);
            const serial = DisasmPanelProvider.serialFromBootElf(bootElf);
            if (serial) { return serial; }
        } catch { /* ISO unreadable or socket down */ }

        return undefined;
    }

    /**
     * Derives a PS2 disc serial (e.g. "SLUS-21606") from a SYSTEM.CNF BOOT2
     * value such as "cdrom0:\SLUS_216.06;1". Returns undefined when the value
     * does not look like a serial.
     */
    private static serialFromBootElf(bootElf: string): string | undefined {
        if (!bootElf) { return undefined; }
        let s = bootElf.trim();
        const slash = Math.max(s.lastIndexOf('\\'), s.lastIndexOf('/'));
        if (slash >= 0) { s = s.slice(slash + 1); }
        const semi = s.indexOf(';');
        if (semi >= 0) { s = s.slice(0, semi); }
        s = s.trim().toUpperCase().replace(/_/g, '-').replace(/\./g, '');
        return /^[A-Z]{3,4}-?\d{3,6}$/.test(s) ? s : undefined;
    }

    /**
     * Resolve the currently loaded game's rich metadata (region, CRC, PCSX2
     * compatibility status, cover art) and push it to the panel's Loaded Game
     * card. Falls back to a bare title when the game is not in the local
     * compatibility database.
     */
    private async postLoadedGame(): Promise<void> {
        if (!this.webview) { return; }

        let serial: string | undefined;
        let title: string | undefined = this.selectedGameName || undefined;

        if (this._bridgeMode && this.socketClient.isConnected()) {
            try {
                const info = await this.socketClient.getPcsx2GameInfo();
                const s = (info.ID    ?? '').replace(/\0/g, '').trim();
                const t = (info.Title ?? '').replace(/\0/g, '').trim();
                if (s) { serial = s; }
                if (t) { title = t; }
            } catch { /* PCSX2 not attached — fall back to the ISO serial */ }
        }

        // Not attached: read the disc serial straight from the selected ISO so
        // the card (and cover art) update as soon as a game is picked.
        if (!serial && this.selectedGamePath && this.socketClient.isConnected()) {
            try {
                const bootElf = await this.socketClient.isoBootElf(this.selectedGamePath);
                serial = DisasmPanelProvider.serialFromBootElf(bootElf);
            } catch { /* ISO unreadable or socket down — fall back to the name */ }
        }

        if (!serial && !title) {
            this.webview.postMessage({ command: 'loadedGame', game: null });
            return;
        }

        try {
            const game = await this.socketClient.compatDetails({ serial, title });
            let coverUri = '';
            const coverSerial = game?.serial ?? serial;
            if (coverSerial) {
                try {
                    const b64 = await this.socketClient.compatGetCover(coverSerial);
                    if (b64) { coverUri = `data:image/jpeg;base64,${b64}`; }
                } catch { /* no cover available */ }
            }
            this.webview?.postMessage({
                command: 'loadedGame',
                game: game ?? { title: title ?? 'Unknown game', serial, hasCover: false },
                coverUri,
            });
        } catch {
            this.webview?.postMessage({
                command: 'loadedGame',
                game: { title: title ?? 'Unknown game', serial, hasCover: false },
                coverUri: '',
            });
        }
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
               img-src data: ${webview.cspSource};
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
            // PCSX2 is intentionally left running — disconnect/reconnect only
            // re-attaches the sockets and must not disturb emulator state.
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

    /**
     * Single-button PCSX2 flow. Reuses an already-running PCSX2 when its PINE
     * port answers and the bridge attaches cleanly; otherwise kills any
     * stale/orphaned instance and launches a fresh one, then attaches once
     * PCSX2 is ready. Replaces the old Start PCSX2 / Connect / Run trio.
     */
    private handleRunPcsx2(emulatorPath: string, biosPath: string, gamePath: string): void {
        const out = getOutputChannel();
        this.selectedEmulatorPath = emulatorPath;
        this.selectedBiosPath = biosPath;
        this.selectedGamePath = gamePath;
        out.show(true);

        if (emulatorPath && !this.isPcsx2Selection(emulatorPath)) {
            void (async () => {
                try {
                    const host = (this.settings.pcsx2?.host ?? '127.0.0.1').trim() || '127.0.0.1';
                    const slot = this.settings.pcsx2?.slot ?? 28011;

                    this._pendingRunMode = null;
                    if (!this.socketClient.isConnected() && !this.emulatorTerminal) {
                        this.launchControlServerAndConnect(emulatorPath);
                    }

                    if (!this.handleStartPcsx2(emulatorPath, biosPath, gamePath)) { return; }

                    if (!await this._waitForSocketConnected(20000)) {
                        out.error('Control socket never connected; cannot attach external emulator.');
                        return;
                    }

                    if (!await this._waitForPortListening(slot, host, 20000)) {
                        out.error(`External emulator PINE port did not open on ${host}:${slot}.`);
                        return;
                    }

                    if (!await this._attachPcsx2()) {
                        out.error('Failed to attach to the external emulator PINE socket.');
                        return;
                    }

                    await this._registerBreakpointsAndResume(true);
                    out.info('Attached to external emulator and sent RESUME.');
                } catch (err: any) {
                    out.error(`Run external emulator failed: ${err?.message ?? err}`);
                }
            })();
            return;
        }

        void (async () => {
          try {
            const host = (this.settings.pcsx2?.host ?? '127.0.0.1').trim() || '127.0.0.1';
            const slot = this.settings.pcsx2?.slot ?? 28011;

            // Bring up the Bridge control socket — needed either way, and
            // independent of PCSX2. The attach is driven here, so clear any
            // pending run mode that onSocketConnected would otherwise act on.
            this._pendingRunMode = null;
            if (!this.socketClient.isConnected() && !this.emulatorTerminal) {
                this.launchControlServerAndConnect(emulatorPath);
            }

            // Reuse an already-running PCSX2 if its PINE port answers and the
            // bridge can attach — lets users keep their own PCSX2 open.
            if (await DisasmPanelProvider.probePortListening(slot, host)) {
                out.info(`A PCSX2 instance is listening on ${host}:${slot} — trying to attach…`);
                if (await this._waitForSocketConnected(20000)) {
                    // Bridge is up; its GS fan-out listener is too, so the
                    // viewer can connect now and fill in once the stream starts.
                    void vscode.commands.executeCommand('codedesigner.openGsFrameViewer');
                    if (await this._attachPcsx2()) {
                        out.info('Attached to the existing PCSX2 instance.');
                        return;
                    }
                }
                out.warn('Existing PCSX2 did not attach — replacing it with a fresh instance.');
            }

            // No reusable instance: kill any stale/orphaned PCSX2, launch fresh.
            this.terminateOrphanedPcsx2ProcessesIfNeeded('starting a fresh PCSX2', true);
            if (!this.handleStartPcsx2(emulatorPath, biosPath, gamePath)) { return; }

            if (!await this._waitForSocketConnected(20000)) {
                out.error('Control socket never connected — cannot attach PCSX2.');
                return;
            }
            // Pop the GS viewer as soon as the bridge is up — it connects to the
            // bridge's frame fan-out and shows "waiting for frames" until the
            // attach below starts the stream, so the window appears promptly
            // even while PCSX2 is still booting (and even if the attach is slow).
            void vscode.commands.executeCommand('codedesigner.openGsFrameViewer');

            out.info('Waiting for PCSX2 to initialize (PINE + GS stream)…');
            if (await this._waitForPcsx2GraphicsReady(45000)) {
                out.info('PCSX2 GS stream is listening — PINE and graphics are up.');
            } else {
                out.warn('Timed out waiting for PCSX2 GS stream; attaching anyway.');
            }

            if (!await this._attachPcsx2()) {
                out.error('Failed to attach to the freshly launched PCSX2.');
            }
          } catch (err: any) {
            out.error(`Run PCSX2 failed: ${err?.message ?? err}`);
          }
        })();
    }

    /**
     * Sends STARTPCSX2 over the control socket so the bridge attaches to
     * PCSX2's PINE and starts forwarding the GS stream. Returns false if the
     * attach fails (e.g. the targeted PCSX2 is unreachable or unhealthy).
     */
    private async _attachPcsx2(): Promise<boolean> {
        const out = getOutputChannel();
        const host = (this.settings.pcsx2?.host ?? '127.0.0.1').trim() || '127.0.0.1';
        const slot = this.settings.pcsx2?.slot ?? 28011;
        try {
            out.info(`Sending STARTPCSX2 to control server (${host}:${slot})…`);
            await this.socketClient.startPcsx2(host, slot);
        } catch (err: any) {
            out.warn(`STARTPCSX2 failed: ${err?.message ?? err}`);
            return false;
        }
        this._bridgeMode = true;
        this._setPauseState(false);
        this.webview?.postMessage({ command: 'pcsx2ModeChanged', isPcsx2: true });
        void this.handleFetchArchiveCheats();
        return true;
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
        const attemptVersion = ++this._connectAttemptVersion;
        this.clearPendingSocketRetry();

        const tryConnect = async () => {
            if (attemptVersion !== this._connectAttemptVersion) { return; }
            attempts++;
            try {
                if (!this.socketClient.isConnected()) {
                    await this.socketClient.connect();
                }
                if (attemptVersion !== this._connectAttemptVersion) { return; }
                this.clearPendingSocketRetry();
                out.info('Socket connected to emulator. Opening memory views…');
                for (const r of LIVE_REGIONS_DEFAULT) {
                    await this.editorProvider.openLiveMemoryView(
                        r.region, r.address, r.size, r.label);
                }
            } catch {
                if (attemptVersion !== this._connectAttemptVersion) { return; }
                if (attempts < maxAttempts) {
                    this.clearPendingSocketRetry();
                    this._connectRetryTimer = setTimeout(tryConnect, 500);
                } else {
                    out.warn('Could not connect to emulator socket server.');
                }
            }
        };

        this._connectRetryTimer = setTimeout(tryConnect, 1000);
    }

    private cleanupProcess(): void {
        this._connectAttemptVersion++;
        this.clearPendingSocketRetry();
        this.emulatorTerminal = undefined;
        // Tear down the built-in emulator process if it's still running so a
        // subsequent Run respawns it cleanly.
        if (this.emulatorProcessTerminal) {
            try { this.emulatorProcessTerminal.dispose(); } catch { /* ignore */ }
            this.emulatorProcessTerminal = undefined;
        }
        this._paused = false;
        this._bridgeMode = false;
        this._pendingRunMode = null;
        this.webview?.postMessage({ command: 'connectionStatus', connected: false });
        void this.postEmulatorProcessStatus();
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

                    // The internal emulator runs in its own process — spawn
                    // CodeDesigner.Ps2.Emulator.exe ourselves and let Bridge
                    // simply connect to its InternalPineSocket. Mirrors how
                    // we launch PCSX2 ourselves before sending STARTPCSX2.
                    const spawned = this.handleStartEmulatorProcess(resolvedBios, resolvedDisc);
                    if (!spawned) { return; }

                    out.info('Sending STARTEMULATOR command to control server...');
                    await this.socketClient.startEmulator(resolvedBios, resolvedDisc);
                    this._bridgeMode = false;
                    await this._registerBreakpointsAndResume(true);
                } else {
                    const host = (this.settings.pcsx2?.host ?? '127.0.0.1').trim() || '127.0.0.1';
                    const slot = this.settings.pcsx2?.slot ?? 28011;
                    out.info(`Sending STARTPCSX2 command to control server (${host}:${slot})...`);
                    await this.socketClient.startPcsx2(host, slot);
                    // Open the GS viewer when runtime actually starts (Run),
                    // not when the user only launches the PCSX2 process.
                    void vscode.commands.executeCommand('codedesigner.openGsFrameViewer');
                    this._bridgeMode = true;
                    this._setPauseState(false);
                    // Notify webview of PCSX2 mode so the cheats tab shows the archive section.
                    this.webview?.postMessage({ command: 'pcsx2ModeChanged', isPcsx2: true });
                    // Auto-fetch archive cheats for the running game.
                    void this.handleFetchArchiveCheats();
                    // Resolve the loaded game's cover / region / compat status.
                    void this.postLoadedGame();
                }
            } catch (err: any) {
                out.error(`Run failed: ${err?.message ?? err}`);
                vscode.window.showErrorMessage(`Run failed: ${err?.message ?? err}`);
            }
        })();
    }

    private clearPendingSocketRetry(): void {
        if (this._connectRetryTimer) {
            clearTimeout(this._connectRetryTimer);
            this._connectRetryTimer = undefined;
        }
    }

    private terminateOrphanedPcsx2ProcessesIfNeeded(reason: string, force = false): void {
        if (!force && !this._launchedPcsx2InSession) { return; }

        const out = getOutputChannel();
        const platform = process.platform;
        try {
            if (this._launchedPcsx2Pid && Number.isFinite(this._launchedPcsx2Pid)) {
                const pid = this._launchedPcsx2Pid;
                if (platform === "win32") {
                    child_process.spawnSync("taskkill", ["/F", "/PID", String(pid), "/T"], { stdio: "pipe" });
                } else {
                    process.kill(pid, "SIGTERM");
                }
                out.info(`[PCSX2] Requested shutdown for launched background PID ${pid} (${reason}).`);
            }
            if (platform === "win32") {
                const kill = child_process.spawnSync("taskkill", ["/F", "/IM", "pcsx2-qt.exe", "/T"], { stdio: "pipe" });
                const stdout = String(kill.stdout ?? "").trim();
                const stderr = String(kill.stderr ?? "").trim();
                if (kill.status === 0) {
                    out.info(`[PCSX2] Cleared orphan process (${reason}). ${stdout}`.trim());
                } else if (stdout || stderr) {
                    out.info(`[PCSX2] No orphan cleanup needed (${reason}): ${(stderr || stdout).replace(/\s+/g, " " )}`);
                }
            } else {
                const kill = child_process.spawnSync("pkill", ["-f", "pcsx2"], { stdio: "pipe" });
                if (kill.status === 0) {
                    out.info(`[PCSX2] Cleared orphan process (${reason}).`);
                }
            }
        } catch (err: any) {
            out.warn(`[PCSX2] Orphan cleanup failed (${reason}): ${err?.message ?? err}`);
        }

        this._launchedPcsx2InSession = false;
        this._launchedPcsx2Pid = null;
        if (this._pcsx2LogTimer) {
            try { clearInterval(this._pcsx2LogTimer); } catch { /* ignore */ }
            this._pcsx2LogTimer = undefined;
        }
        if (this._pcsx2Terminal) {
            try { this._pcsx2Terminal.dispose(); } catch { /* ignore */ }
            this._pcsx2Terminal = undefined;
        }
        this._pcsx2LogOffset = 0;
        this._pcsx2DiscoveredPaths.clear();
        this._pcsx2PersistedLabeledPaths.clear();
        if (this._pcsx2DiscoveredFlushTimer) { clearTimeout(this._pcsx2DiscoveredFlushTimer); this._pcsx2DiscoveredFlushTimer = undefined; }
        if (this._pcsx2SnapshotRefreshTimer) { clearTimeout(this._pcsx2SnapshotRefreshTimer); this._pcsx2SnapshotRefreshTimer = undefined; }
    }

    /**
     * Launch the external PCSX2 application using CLI arguments from panel selections.
     * This does not connect sockets by itself; Connect/Run still control CodeDesigner.
     * PCSX2 is located from the emulators list, then auto-detected from the install
     * directory's `pcsx2` subfolder, then standard OS paths — no manual path config needed.
     */
    private handleStartPcsx2(emulatorPath: string, biosPath: string, gamePath: string): boolean {
        const out = getOutputChannel();

        this.selectedEmulatorPath = emulatorPath;
        this.selectedBiosPath = biosPath;
        this.selectedGamePath = gamePath;

        if (emulatorPath && !this.isPcsx2Selection(emulatorPath)) {
            const emulatorExe = this.resolvePath(emulatorPath);
            if (!fs.existsSync(emulatorExe)) {
                const message = `Selected emulator not found: ${emulatorExe}`;
                out.error(message);
                vscode.window.showErrorMessage(message);
                return false;
            }

            const args: string[] = [];
            const pinePort = this.settings.pcsx2?.slot ?? 28011;
            args.push('--pine', '--port', String(pinePort));
            out.info(`External emulator PINE port: ${pinePort}`);

            const resolvedBios = biosPath ? this.resolvePath(biosPath) : '';
            if (resolvedBios && fs.existsSync(resolvedBios)) {
                out.info(`External emulator BIOS selection: ${resolvedBios}`);
                args.push('--bios', resolvedBios);
            } else {
                out.warn('No valid BIOS selected for emulator launch; starting without a BIOS argument.');
            }

            const resolvedDisc = gamePath ? this.resolvePath(gamePath) : '';
            if (resolvedDisc && fs.existsSync(resolvedDisc)) {
                out.info(`Emulator game selection: ${resolvedDisc}`);
                args.push(resolvedDisc);
            } else {
                out.warn('No valid game selected for emulator launch; starting without a boot image.');
            }

            const preview = [emulatorExe, ...args]
                .map(v => v.includes(' ') ? `"${v}"` : v)
                .join(' ');
            out.info(`Starting emulator: ${preview}`);

            if (this._pcsx2Terminal) {
                try { this._pcsx2Terminal.dispose(); } catch { /* ignore */ }
                this._pcsx2Terminal = undefined;
            }

            if (process.platform === 'win32') {
                const scriptPath = path.join(this.extensionPath, 'scripts', 'launch-pcsx2-hidden.ps1');
                const logDir = path.join(this._globalStoragePath, 'pcsx2');
                const emuLogPath = path.join(logDir, 'emulog.txt');
                const pidPath = path.join(logDir, 'pid.txt');
                try { fs.mkdirSync(logDir, { recursive: true }); } catch { /* ignore */ }
                try { fs.writeFileSync(pidPath, ''); } catch { /* ignore */ }

                const terminal = vscode.window.createTerminal({ name: 'Emulator Launcher', shellPath: 'powershell.exe', shellArgs: ['-NoLogo'] });
                this._pcsx2Terminal = terminal;
                terminal.show(true);
                const psArgs = args.map(a => `'${a.replace(/'/g, "''")}'`).join(',');
                const cmd = `& "${scriptPath}" -ExePath "${emulatorExe}" -EmuLogPath "${emuLogPath}" -PidPath "${pidPath}" -Pcsx2Mode:$false -ArgList @(${psArgs})`;
                terminal.sendText(cmd, true);
            } else {
                const terminal = vscode.window.createTerminal({
                    name: 'Emulator',
                    shellPath: emulatorExe,
                    shellArgs: args,
                });
                this._pcsx2Terminal = terminal;
                terminal.show(true);
            }

            void this.postEmulatorProcessStatus(emulatorPath);
            setTimeout(() => void this.postEmulatorProcessStatus(emulatorPath), 1500);
            return true;
        }

        // A launch always starts a fresh instance, so sweep a PCSX2 this session
        // started earlier — otherwise instances accumulate (disconnect now leaves
        // PCSX2 running). No-ops on the first launch of the session.
        this.terminateOrphanedPcsx2ProcessesIfNeeded('relaunch');

        // Resolve emulator path: prefer the selected executable even when it is a
        // PCSX2-compatible fork with a different filename, then fall back to auto-detection.
        let pcsx2Exe = '';
        if (emulatorPath) {
            const selectedExe = this.resolvePath(emulatorPath);
            if (fs.existsSync(selectedExe)) {
                pcsx2Exe = selectedExe;
            }
        }
        if (!pcsx2Exe || !fs.existsSync(pcsx2Exe)) {
            pcsx2Exe = DisasmPanelProvider.detectPcsx2Exe() ?? '';
        }

        if (!pcsx2Exe || !fs.existsSync(pcsx2Exe)) {
            out.error('PCSX2 executable not found. Place PCSX2 in the pcsx2 subfolder of the install directory, install it to a standard location, or add it via the emulator selector.');
            vscode.window.showErrorMessage(
                'PCSX2 not found. Place PCSX2 in the pcsx2 subfolder of the CodeDesigner install directory, or add it via the emulator selector.'
            );
            return false;
        }

        const args: string[] = [];
        const resolvedDisc = gamePath ? this.resolvePath(gamePath) : '';

        if (resolvedDisc && fs.existsSync(resolvedDisc)) {
            out.info(`PCSX2 game selection: ${resolvedDisc}`);
            args.push('-batch');
        } else {
            out.warn('No valid game selected for PCSX2 launch; starting without a boot image.');
        }

        args.push('-earlyconsolelog');
        args.push('-nogui');
        args.push('--');
        args.push(resolvedDisc);

        const preview = [pcsx2Exe, ...args]
            .map(v => v.includes(' ') ? `"${v}"` : v)
            .join(' ');
        out.info(`Starting PCSX2: ${preview}`);

        if (process.platform === 'win32') {
            const scriptPath = path.join(this.extensionPath, 'scripts', 'launch-pcsx2-hidden.ps1');
            const logDir = path.join(this._globalStoragePath, 'pcsx2');
            const emuLogPath = path.join(logDir, 'emulog.txt');
            const pidPath = path.join(logDir, 'pid.txt');
            try { fs.mkdirSync(logDir, { recursive: true }); } catch { /* ignore */ }
            try { fs.rmSync(emuLogPath, { force: true }); } catch { /* ignore */ }
            try { fs.writeFileSync(pidPath, ''); } catch { /* ignore */ }

            // Route PCSX2's application log to a path we control. pcsx2-qt opens
            // the -logfile target while parsing args — before EmuFolders::LoadConfig
            // prints the startup directory listing — so that listing is captured
            // in the file. -logfile must precede the "--" boot-file separator.
            const pcsx2Args = [...args];
            const sepIdx = pcsx2Args.indexOf('--');
            const logFileArgs = ['-logfile', emuLogPath];
            if (sepIdx >= 0) {
                pcsx2Args.splice(sepIdx, 0, ...logFileArgs);
            } else {
                pcsx2Args.push(...logFileArgs);
            }

            if (this._pcsx2Terminal) {
                try { this._pcsx2Terminal.dispose(); } catch { /* ignore */ }
                this._pcsx2Terminal = undefined;
            }
            const terminal = vscode.window.createTerminal({ name: 'PCSX2 Launcher', shellPath: 'powershell.exe', shellArgs: ['-NoLogo'] });
            this._pcsx2Terminal = terminal;
            terminal.show(true);
            const cmd = `& "${scriptPath}" -ExePath "${pcsx2Exe}" -EmuLogPath "${emuLogPath}" -PidPath "${pidPath}" -Pcsx2Mode:$true -ArgList @(${pcsx2Args.map(a => `'${a.replace(/'/g, "''")}'`).join(',')})`;
            terminal.sendText(cmd, true);

            this.startPcsx2LogParsing(emuLogPath);
            void this.capturePcsx2PidFromFile(pidPath);
            out.info(`PCSX2 launched. Emu log: ${emuLogPath}`);
            this._launchedPcsx2InSession = true;
            void this.postEmulatorProcessStatus(emulatorPath);
            setTimeout(() => void this.postEmulatorProcessStatus(emulatorPath), 1500);
            return true;
        }

        if (this._pcsx2Terminal) {
            try { this._pcsx2Terminal.dispose(); } catch { /* ignore */ }
            this._pcsx2Terminal = undefined;
        }
        const terminal = vscode.window.createTerminal({
            name: 'PCSX2',
            shellPath: pcsx2Exe,
            shellArgs: args,
        });
        this._pcsx2Terminal = terminal;
        terminal.show(true);
        out.info('PCSX2 started via terminal manager.');
        this._launchedPcsx2InSession = true;
        void this.postEmulatorProcessStatus(emulatorPath);
        setTimeout(() => void this.postEmulatorProcessStatus(emulatorPath), 1500);
        return true;
    }

    private async capturePcsx2PidFromFile(pidPath: string): Promise<void> {
        for (let i = 0; i < 40; i++) {
            try {
                const text = (await fs.promises.readFile(pidPath, 'utf8')).trim();
                const pid = Number(text);
                if (Number.isFinite(pid) && pid > 0) { this._launchedPcsx2Pid = pid; return; }
            } catch { /* ignore */ }
            await new Promise(r => setTimeout(r, 100));
        }
    }

    private startPcsx2LogParsing(logPath: string): void {
        if (this._pcsx2LogTimer) { clearInterval(this._pcsx2LogTimer); this._pcsx2LogTimer = undefined; }
        this._pcsx2LogOffset = 0;
        this._pcsx2DiscoveredPaths.clear();
        this._pcsx2PersistedLabeledPaths.clear();
        this._pcsx2PathsSidecarConsumed = false;
        if (this._pcsx2DiscoveredFlushTimer) { clearTimeout(this._pcsx2DiscoveredFlushTimer); this._pcsx2DiscoveredFlushTimer = undefined; }
        if (this._pcsx2SnapshotRefreshTimer) { clearTimeout(this._pcsx2SnapshotRefreshTimer); this._pcsx2SnapshotRefreshTimer = undefined; }
        this._pcsx2GraphicsReady = false;

        // Poll the emu log instead of fs.watch-ing it: the file does not exist
        // yet when PCSX2 is launching (fs.watch throws ENOENT on a missing
        // path, which previously aborted the whole run flow), and PCSX2
        // truncates it on open. Polling tolerates both.
        const consume = async () => {
            // The PowerShell launcher writes `pcsx2-paths.json` once it has
            // parsed PCSX2's startup directory listing. Ingest it as soon as
            // it appears — this is more reliable than racing the live log
            // ourselves, and works for users who only see PCSX2 through the
            // hidden-window launch flow.
            if (!this._pcsx2PathsSidecarConsumed) {
                await this._tryIngestPcsx2PathsSidecar(path.join(path.dirname(logPath), 'pcsx2-paths.json'));
            }

            try {
                const stat = await fs.promises.stat(logPath);
                // PCSX2 truncates the log when it (re)opens it — restart from 0.
                if (stat.size < this._pcsx2LogOffset) { this._pcsx2LogOffset = 0; }
                if (stat.size <= this._pcsx2LogOffset) { return; }
                const fh = await fs.promises.open(logPath, 'r');
                const len = stat.size - this._pcsx2LogOffset;
                const buf = Buffer.alloc(len);
                await fh.read(buf, 0, len, this._pcsx2LogOffset);
                await fh.close();
                this._pcsx2LogOffset = stat.size;
                const chunk = buf.toString('utf8');
                this.extractAndPersistPcsx2Paths(chunk);
                this._detectPcsx2GraphicsReady(chunk);
            } catch { /* emu log not created yet — keep polling */ }
        };

        this._pcsx2LogTimer = setInterval(() => { void consume(); }, 500);
    }

    /**
     * Labels emitted by `EmuFolders::LoadConfig` in PCSX2 (pcsx2/Pcsx2Config.cpp:2315-2331).
     * Each PCSX2 startup logs `<Label> Directory: <absolute path>` and we persist them
     * under stable `pcsx2.path.<key>` setting keys so other parts of the system can
     * resolve PCSX2's actual on-disk layout without having to re-scan the log.
     */
    private static readonly _pcsx2LabeledPathKeys: ReadonlyArray<readonly [RegExp, string]> = [
        [/^DataRoot Directory:\s*(.+?)\s*$/m,            'pcsx2.path.dataRoot'],
        [/^BIOS Directory:\s*(.+?)\s*$/m,                'pcsx2.path.bios'],
        [/^Snapshots Directory:\s*(.+?)\s*$/m,           'pcsx2.path.snapshots'],
        [/^Savestates Directory:\s*(.+?)\s*$/m,          'pcsx2.path.savestates'],
        [/^MemoryCards Directory:\s*(.+?)\s*$/m,         'pcsx2.path.memoryCards'],
        [/^Logs Directory:\s*(.+?)\s*$/m,                'pcsx2.path.logs'],
        [/^Cheats Directory:\s*(.+?)\s*$/m,              'pcsx2.path.cheats'],
        [/^Patches Directory:\s*(.+?)\s*$/m,             'pcsx2.path.patches'],
        [/^Covers Directory:\s*(.+?)\s*$/m,              'pcsx2.path.covers'],
        [/^Game Settings Directory:\s*(.+?)\s*$/m,       'pcsx2.path.gameSettings'],
        [/^Resources Directory:\s*(.+?)\s*$/m,           'pcsx2.path.resources'],
        [/^User Resources Directory:\s*(.+?)\s*$/m,      'pcsx2.path.userResources'],
        [/^Cache Directory:\s*(.+?)\s*$/m,               'pcsx2.path.cache'],
        [/^Textures Directory:\s*(.+?)\s*$/m,            'pcsx2.path.textures'],
        [/^Input Profile Directory:\s*(.+?)\s*$/m,       'pcsx2.path.inputProfiles'],
        [/^Video Dumping Directory:\s*(.+?)\s*$/m,       'pcsx2.path.videos'],
        [/^Debugger Layouts Directory:\s*(.+?)\s*$/m,    'pcsx2.path.debuggerLayouts'],
        [/^Debugger Settings Directory:\s*(.+?)\s*$/m,   'pcsx2.path.debuggerSettings'],
    ];

    /**
     * Label-form → setting-key mapping for both the live extractor and the
     * sidecar JSON / emu-log fallback reader. The label form matches what
     * PCSX2 prints in the emu log AND what `launch-pcsx2-hidden.ps1` writes
     * as keys in `pcsx2-paths.json`, so the same map drives every path.
     */
    private static readonly _pcsx2PathLabelToKey: Readonly<Record<string, string>> = {
        'DataRoot':         'pcsx2.path.dataRoot',
        'BIOS':             'pcsx2.path.bios',
        'Snapshots':        'pcsx2.path.snapshots',
        'Savestates':       'pcsx2.path.savestates',
        'MemoryCards':      'pcsx2.path.memoryCards',
        'Logs':             'pcsx2.path.logs',
        'Cheats':           'pcsx2.path.cheats',
        'Patches':          'pcsx2.path.patches',
        'Covers':           'pcsx2.path.covers',
        'Game Settings':    'pcsx2.path.gameSettings',
        'Resources':        'pcsx2.path.resources',
        'User Resources':   'pcsx2.path.userResources',
        'Cache':            'pcsx2.path.cache',
        'Textures':         'pcsx2.path.textures',
        'Input Profile':    'pcsx2.path.inputProfiles',
        'Video Dumping':    'pcsx2.path.videos',
        'Debugger Layouts': 'pcsx2.path.debuggerLayouts',
        'Debugger Settings':'pcsx2.path.debuggerSettings',
    };

    /** Labeled keys we've already persisted this session — avoids redundant DB writes. */
    private _pcsx2PersistedLabeledPaths = new Map<string, string>();
    /** Debounce timer for flushing the bulk `pcsx2.path.discovered` JSON array. */
    private _pcsx2DiscoveredFlushTimer?: ReturnType<typeof setTimeout>;
    /** True once the PowerShell-written `pcsx2-paths.json` sidecar has been ingested for this launch. */
    private _pcsx2PathsSidecarConsumed = false;
    /** Debounce timer for refreshing snapshot lists after a wave of path writes. */
    private _pcsx2SnapshotRefreshTimer?: ReturnType<typeof setTimeout>;

    /**
     * Write a `pcsx2.path.*` setting, dedup against what's already cached
     * for this session, and — when the savestates path changes — debounce
     * a refresh of the snapshot list so both the disasm-panel settings tab
     * AND the Snapshot Manager pick up newly-discovered .p2s files without
     * waiting for the next game-change event. Returns true when the value
     * was new (i.e. actually persisted to the DB).
     */
    private _persistPcsx2PathSetting(key: string, value: string): boolean {
        if (!value) { return false; }
        if (this._pcsx2PersistedLabeledPaths.get(key) === value) { return false; }
        this._pcsx2PersistedLabeledPaths.set(key, value);
        void this.socketClient.setSetting(key, value).catch(err => {
            getOutputChannel().debug(`Could not persist ${key}: ${err?.message ?? err}`);
        });
        if (key === 'pcsx2.path.savestates') {
            // Debounce so a burst of writes (e.g. sidecar ingest + emu-log
            // backfill arriving milliseconds apart) only triggers one refresh.
            if (this._pcsx2SnapshotRefreshTimer) { clearTimeout(this._pcsx2SnapshotRefreshTimer); }
            this._pcsx2SnapshotRefreshTimer = setTimeout(() => {
                this._pcsx2SnapshotRefreshTimer = undefined;
                void this.loadSnapshotsForGame(this.selectedGameName);
            }, 250);
        }
        return true;
    }

    private extractAndPersistPcsx2Paths(chunk: string): void {
        // ── 1) Labeled paths: write each to its own setting key ────────────────
        for (const [re, key] of DisasmPanelProvider._pcsx2LabeledPathKeys) {
            const m = chunk.match(re);
            if (!m) { continue; }
            this._persistPcsx2PathSetting(key, m[1].trim());
        }

        // ── 2) Bulk dump: collect every path-shaped string and flush as JSON ──
        const pathPattern = /([A-Za-z]:\\[^\s"']+|\/[^\s"']+)/g;
        let changed = false;
        for (const m of chunk.matchAll(pathPattern)) {
            const p = m[1].trim();
            if (!p || p.length < 3) { continue; }
            const before = this._pcsx2DiscoveredPaths.size;
            this._pcsx2DiscoveredPaths.add(p);
            if (this._pcsx2DiscoveredPaths.size !== before) { changed = true; }
        }
        if (!changed) { return; }

        // Debounce the bulk write — emu log emissions can be chatty during boot.
        if (this._pcsx2DiscoveredFlushTimer) { clearTimeout(this._pcsx2DiscoveredFlushTimer); }
        this._pcsx2DiscoveredFlushTimer = setTimeout(() => {
            this._pcsx2DiscoveredFlushTimer = undefined;
            const json = JSON.stringify(Array.from(this._pcsx2DiscoveredPaths).sort());
            void this.socketClient.setSetting('pcsx2.path.discovered', json).catch(err => {
                getOutputChannel().debug(`Could not persist pcsx2.path.discovered: ${err?.message ?? err}`);
            });
        }, 1000);
    }

    /** Marks PCSX2 graphics-ready when the emu log reports "GSStream: listening". */
    private _detectPcsx2GraphicsReady(chunk: string): void {
        if (this._pcsx2GraphicsReady || !/GSStream:\s*listening/i.test(chunk)) { return; }
        this._pcsx2GraphicsReady = true;
        const waiters = Array.from(this._pcsx2ReadyWaiters);
        this._pcsx2ReadyWaiters.clear();
        for (const w of waiters) { w(); }
    }

    /**
     * Resolves true once the PCSX2 emu log reports "GSStream: listening" (PINE
     * and the GS stream are both up by then), or false if the timeout elapses.
     */
    private _waitForPcsx2GraphicsReady(timeoutMs: number): Promise<boolean> {
        if (this._pcsx2GraphicsReady) { return Promise.resolve(true); }
        return new Promise<boolean>(resolve => {
            let settled = false;
            const finish = (value: boolean) => {
                if (settled) { return; }
                settled = true;
                clearTimeout(timer);
                this._pcsx2ReadyWaiters.delete(waiter);
                resolve(value);
            };
            const waiter = () => finish(true);
            const timer = setTimeout(() => finish(false), timeoutMs);
            this._pcsx2ReadyWaiters.add(waiter);
        });
    }

    /** Polls until the control socket reports connected, or the timeout elapses. */
    private async _waitForSocketConnected(timeoutMs: number): Promise<boolean> {
        const deadline = Date.now() + timeoutMs;
        while (Date.now() < deadline) {
            if (this.socketClient.isConnected()) { return true; }
            await new Promise(r => setTimeout(r, 200));
        }
        return this.socketClient.isConnected();
    }

    /** Polls until a loopback port accepts connections, or the timeout elapses. */
    private async _waitForPortListening(port: number, host: string, timeoutMs: number): Promise<boolean> {
        const deadline = Date.now() + timeoutMs;
        while (Date.now() < deadline) {
            if (await DisasmPanelProvider.probePortListening(port, host)) { return true; }
            await new Promise(r => setTimeout(r, 200));
        }
        return DisasmPanelProvider.probePortListening(port, host);
    }

    /**
     * Launch the built-in emulator (CodeDesigner.Ps2.Emulator.exe) in a
     * VSCode terminal with the supplied BIOS / disc args, then leave it
     * running so the Bridge can connect to its InternalPineSocket via
     * STARTEMULATOR. Returns false when the binary cannot be found —
     * caller aborts rather than sending STARTEMULATOR to a Bridge that
     * has nothing to connect to.
     *
     * Symmetric with {@link handleStartPcsx2}: the front end owns
     * process lifecycle for both runtimes; the Bridge is purely a
     * router that connects to whichever process is already up.
     */
    private handleStartEmulatorProcess(biosPath?: string, discPath?: string): boolean {
        const out = getOutputChannel();

        // Don't spawn a second instance if one is already running from the
        // current VS Code session.
        if (this.emulatorProcessTerminal) {
            out.info('Built-in emulator process is already running; reusing it.');
            return true;
        }

        const exe = DisasmPanelProvider.findEmulatorExe();
        if (!exe) {
            const installDir = DisasmPanelProvider.getInstallDirectory();
            const msg = `CodeDesigner.Ps2.Emulator binary not found in ${installDir}. Build the project first to deploy it.`;
            out.error(msg);
            vscode.window.showErrorMessage(msg);
            return false;
        }

        const args: string[] = [];
        if (biosPath && fs.existsSync(biosPath)) { args.push('--bios', biosPath); }
        if (discPath && fs.existsSync(discPath)) { args.push('--disc', discPath); }

        const preview = [exe, ...args]
            .map(v => v.includes(' ') ? `"${v}"` : v)
            .join(' ');
        out.info(`Starting built-in emulator: ${preview}`);

        const terminal = vscode.window.createTerminal({
            name: 'CodeDesigner Emulator',
            shellPath: exe,
            shellArgs: args,
        });
        terminal.show(false);
        this.emulatorProcessTerminal = terminal;
        void this.postEmulatorProcessStatus();
        setTimeout(() => void this.postEmulatorProcessStatus(), 1500);
        return true;
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
                await this.editorProvider.broadcastCurrentStatus();
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
     * Save the live PCSX2 emulator state to a `.p2s` file in PCSX2's own
     * savestates folder (`pcsx2.path.savestates`, populated from the emu log
     * on first connect). PCSX2 names slot saves `<SERIAL> (<CRC>).<NN>.p2s`;
     * for user-titled saves we keep the same prefix when serial+CRC are known
     * so PCSX2 and the Snapshot Manager's per-game filter both recognise the
     * file. The `.p2s` archive contains everything (memory, GS state, embedded
     * Screenshot.png) — no sidecar `.extracted/` directory is generated here;
     * the Snapshot Manager's "Open Memory" action extracts on demand.
     * Routed through the PINE socket (SAVESTATETOFILE / opcode 0x22).
     */
    private async handleSnapshotPcsx2(): Promise<void> {
        const out = getOutputChannel();

        if (!this.socketClient.isConnected()) {
            vscode.window.showWarningMessage('Not connected to emulator.');
            return;
        }

        const saveDir = await this._resolvePcsx2SavestatesDir();
        if (!saveDir) {
            vscode.window.showWarningMessage(
                "Could not locate PCSX2's savestates folder. Launch PCSX2 once via " +
                'the Connect button so its directory listing is captured, or set ' +
                'pcsx2.path.savestates manually.');
            return;
        }

        // Prompt user for a save-state name (used as the .p2s file name).
        const snapName = await vscode.window.showInputBox({
            title: 'Save PCSX2 State',
            prompt: 'Enter a name for this save state',
            placeHolder: 'e.g. before_boss_fight',
            ignoreFocusOut: true,
        });
        if (snapName === undefined) { return; } // cancelled

        const safeName = (snapName.trim() || 'savestate').replace(/[^a-zA-Z0-9_\-]/g, '_');

        try { fs.mkdirSync(saveDir, { recursive: true }); } catch (mkErr: any) {
            if (mkErr?.code !== 'EEXIST') {
                out.error(`Could not create save-state directory: ${mkErr?.message ?? mkErr}`);
                vscode.window.showErrorMessage(`Could not create save-state directory: ${mkErr?.message ?? mkErr}`);
                return;
            }
        }

        const serial = this._currentGameSerial.trim().toUpperCase();
        const crc    = serial ? this._tryDiscoverCurrentCrc(saveDir, serial) : null;
        let fileName: string;
        if (serial && crc)   { fileName = `${serial} (${crc}).${safeName}.p2s`; }
        else if (serial)     { fileName = `${serial}.${safeName}.p2s`; }
        else                 { fileName = `${safeName}.p2s`; }
        const filePath = path.join(saveDir, fileName);

        out.info(`Saving PCSX2 state → ${filePath}`);

        try {
            await this.socketClient.saveStateFile(filePath);
            out.info(`PCSX2 state saved to '${filePath}'.`);
            vscode.window.showInformationMessage(`PCSX2 state saved: ${fileName}`);

            // Refresh the snapshot list so the new entry shows up immediately
            // in the manager (filtered by the current game's serial).
            void this.loadSnapshotsForGame(this.selectedGameName);
        } catch (err: any) {
            out.error(`PCSX2 save state failed: ${err?.message ?? err}`);
            vscode.window.showErrorMessage(`PCSX2 save state failed: ${err?.message ?? err}`);
        }
    }

    /**
     * Best-effort lookup of the PCSX2 game CRC for the currently loaded disc
     * by scanning sibling `<SERIAL> (<CRC>).*.p2s` files PCSX2 has already
     * written. Returns `null` when the savestates folder contains no canonical
     * save for this serial yet — the caller falls back to a CRC-less filename.
     */
    /**
     * Resolve PCSX2's savestates directory with a fallback chain so the
     * Snapshot button works even when the emu-log path-extractor never ran
     * (e.g. PCSX2 was already running when VS Code attached, or this update
     * shipped after the user's last fresh launch).
     *
     * Resolution order:
     *   1. `pcsx2.path.savestates` setting (auto-captured from the emu log).
     *   2. `<pcsx2.path.dataRoot>/sstates` — PCSX2's default layout
     *      (`EmuFolders::LoadConfig` joins DataRoot + the "sstates" subkey).
     *   3. Windows default: `%USERPROFILE%\Documents\PCSX2\sstates`.
     *
     * On a successful fallback we backfill the `pcsx2.path.savestates`
     * setting so subsequent lookups are fast and consistent — useful when
     * the emu-log polling missed the directory listing for any reason.
     */
    private async _resolvePcsx2SavestatesDir(): Promise<string | null> {
        const trySetting = async (key: string): Promise<string | null> => {
            try { return (await this.socketClient.getSetting(key))?.trim() || null; }
            catch { return null; }
        };

        // 1. Captured savestates path.
        const direct = await trySetting('pcsx2.path.savestates');
        if (direct && fs.existsSync(direct)) { return direct; }

        // 2. Derived from DataRoot.
        const dataRoot = await trySetting('pcsx2.path.dataRoot');
        if (dataRoot) {
            const derived = path.join(dataRoot, 'sstates');
            if (fs.existsSync(derived)) {
                void this._backfillSavestatesPath(derived);
                return derived;
            }
        }

        // 3. PowerShell sidecar JSON, if present (most common case when the
        //    launch flow ran but the live log polling missed the lines).
        if (this._globalStoragePath) {
            const sidecar = path.join(this._globalStoragePath, 'pcsx2', 'pcsx2-paths.json');
            const fromSidecar = await this._tryIngestPcsx2PathsSidecar(sidecar);
            if (fromSidecar && fs.existsSync(fromSidecar)) { return fromSidecar; }
        }

        // 4. Parse the on-disk emu log directly. Handles the case where PCSX2
        //    has been running since before the path-extractor shipped — we
        //    don't depend on the live polling loop having ever seen the
        //    startup lines. Also backfills every captured pcsx2.path.* key.
        const fromLog = await this._readPathsFromOnDiskEmuLog();
        if (fromLog) {
            const fromLogPath = fromLog['pcsx2.path.savestates'];
            if (fromLogPath && fs.existsSync(fromLogPath)) { return fromLogPath; }
            const fromLogRoot = fromLog['pcsx2.path.dataRoot'];
            if (fromLogRoot) {
                const derived = path.join(fromLogRoot, 'sstates');
                if (fs.existsSync(derived)) {
                    void this._backfillSavestatesPath(derived);
                    return derived;
                }
            }
        }

        // 5. Parse PCSX2.ini directly. Works even when PCSX2 has never been
        //    launched through VS Code (no globalStorage emu log, no sidecar)
        //    — we mirror PCSX2's own portable-mode + DataRoot resolution and
        //    read the [Folders] section that the emulator itself uses at
        //    boot. The most general fallback that still relies on a real
        //    PCSX2 install (vs. step 6's hard-coded guess).
        const fromIni = await this._readPathsFromPcsx2Ini();
        if (fromIni) {
            const iniSavestates = fromIni['pcsx2.path.savestates'];
            if (iniSavestates && fs.existsSync(iniSavestates)) { return iniSavestates; }
            const iniRoot = fromIni['pcsx2.path.dataRoot'];
            if (iniRoot) {
                const derived = path.join(iniRoot, 'sstates');
                if (fs.existsSync(derived)) {
                    void this._backfillSavestatesPath(derived);
                    return derived;
                }
            }
        }

        // 6. Windows default — PCSX2 stores user data under
        //    %USERPROFILE%\Documents\PCSX2 unless overridden by portable mode.
        if (process.platform === 'win32') {
            const home = process.env.USERPROFILE || require('os').homedir();
            if (home) {
                const winDefault = path.join(home, 'Documents', 'PCSX2', 'sstates');
                if (fs.existsSync(winDefault)) {
                    void this._backfillSavestatesPath(winDefault);
                    return winDefault;
                }
            }
        }

        return null;
    }

    private async _backfillSavestatesPath(p: string): Promise<void> {
        this._persistPcsx2PathSetting('pcsx2.path.savestates', p);
    }

    /**
     * Ingest the `pcsx2-paths.json` sidecar written by `launch-pcsx2-hidden.ps1`
     * once PCSX2's startup directory listing has been captured. Every
     * directory key is mapped through `_pcsx2PathLabelToKey` and written to
     * the settings table. Returns the resolved `Savestates` path when found
     * (so callers can use it directly without an extra getSetting round-trip).
     */
    private async _tryIngestPcsx2PathsSidecar(sidecarPath: string): Promise<string | null> {
        try {
            if (!fs.existsSync(sidecarPath)) { return null; }
            const raw = await fs.promises.readFile(sidecarPath, 'utf8');
            const doc = JSON.parse(raw) as { directories?: Record<string, string> };
            const dirs = doc?.directories;
            if (!dirs || typeof dirs !== 'object') { return null; }

            let savestates: string | null = null;
            for (const [label, value] of Object.entries(dirs)) {
                const key = DisasmPanelProvider._pcsx2PathLabelToKey[label];
                if (!key || typeof value !== 'string' || !value) { continue; }
                this._persistPcsx2PathSetting(key, value);
                if (key === 'pcsx2.path.savestates') { savestates = value; }
            }
            this._pcsx2PathsSidecarConsumed = true;
            return savestates;
        } catch (err: any) {
            getOutputChannel().debug(`Could not read pcsx2-paths.json sidecar: ${err?.message ?? err}`);
            return null;
        }
    }

    /**
     * Parse PCSX2's `PCSX2.ini` directly. The `[Folders]` section is the
     * source PCSX2 itself reads at boot, so it works even when the user
     * hasn't launched PCSX2 from VS Code (no globalStorage emu log, no
     * sidecar JSON). DataRoot is resolved by mirroring PCSX2's own logic:
     *  - if a `portable.ini` sits next to the executable, DataRoot = exeDir
     *  - otherwise, DataRoot = `%USERPROFILE%\Documents\PCSX2` (Windows)
     *
     * Backfills every parsed directory into `pcsx2.path.*` so subsequent
     * lookups take the cached fast path.
     */
    private async _readPathsFromPcsx2Ini(): Promise<Record<string, string> | null> {
        let exePath: string | null;
        try { exePath = await this.socketClient.getSetting('ui.selectedEmulator'); }
        catch { exePath = null; }
        if (!exePath || !fs.existsSync(exePath)) { return null; }

        const exeDir = path.dirname(exePath);
        let dataRoot: string;
        if (fs.existsSync(path.join(exeDir, 'portable.ini'))) {
            dataRoot = exeDir;
        } else if (process.platform === 'win32') {
            const home = process.env.USERPROFILE || require('os').homedir();
            if (!home) { return null; }
            dataRoot = path.join(home, 'Documents', 'PCSX2');
        } else {
            return null;
        }

        const iniPath = path.join(dataRoot, 'inis', 'PCSX2.ini');
        if (!fs.existsSync(iniPath)) { return null; }

        let text: string;
        try { text = await fs.promises.readFile(iniPath, 'utf8'); }
        catch { return null; }

        const sectionMatch = text.match(/^\[Folders\]\s*\r?\n([\s\S]*?)(?=^\[|\Z)/m);
        if (!sectionMatch) { return null; }

        // PCSX2's INI uses condensed keys (e.g. "MemoryCards", "GameSettings");
        // the emu log uses spaced labels ("Memory Cards", "Game Settings").
        // Map ini-key → emu-label so we can reuse `_pcsx2PathLabelToKey`.
        const iniToLabel: Readonly<Record<string, string>> = {
            'Bios':             'BIOS',
            'Snapshots':        'Snapshots',
            'Savestates':       'Savestates',
            'MemoryCards':      'MemoryCards',
            'Logs':             'Logs',
            'Cheats':           'Cheats',
            'Patches':          'Patches',
            'Covers':           'Covers',
            'GameSettings':     'Game Settings',
            'Resources':        'Resources',
            'UserResources':    'User Resources',
            'Cache':            'Cache',
            'Textures':         'Textures',
            'InputProfiles':    'Input Profile',
            'Videos':           'Video Dumping',
            'DebuggerLayouts':  'Debugger Layouts',
            'DebuggerSettings': 'Debugger Settings',
        };
        // PCSX2's LoadConfig joins these against `<DataRoot>/inis` (the
        // `Settings` folder), not DataRoot itself.
        const settingsRelative = new Set(['DebuggerLayouts', 'DebuggerSettings']);
        const settingsDir = path.join(dataRoot, 'inis');

        const out: Record<string, string> = {
            'pcsx2.path.dataRoot': dataRoot,
        };

        for (const m of sectionMatch[1].matchAll(/^\s*([A-Za-z]+)\s*=\s*(.+?)\s*$/gm)) {
            const iniKey = m[1];
            const value  = m[2];
            const label  = iniToLabel[iniKey];
            if (!label) { continue; }
            const settingKey = DisasmPanelProvider._pcsx2PathLabelToKey[label];
            if (!settingKey || !value) { continue; }
            const baseDir = settingsRelative.has(iniKey) ? settingsDir : dataRoot;
            out[settingKey] = path.isAbsolute(value) ? value : path.join(baseDir, value);
        }

        if (Object.keys(out).length <= 1) { return null; }

        for (const [key, value] of Object.entries(out)) {
            this._persistPcsx2PathSetting(key, value);
        }
        return out;
    }

    /**
     * Last-resort path discovery: parse the on-disk emu log directly,
     * looking for the `<Label> Directory: <path>` lines PCSX2 prints during
     * `EmuFolders::LoadConfig`. Used by `_resolvePcsx2SavestatesDir` so a
     * session whose log polling never ran (e.g. PCSX2 was already running
     * before this code shipped) can still find PCSX2's folders on the next
     * snapshot click — and also opportunistically backfills every
     * `pcsx2.path.*` setting so future lookups are O(1).
     */
    private async _readPathsFromOnDiskEmuLog(): Promise<Record<string, string> | null> {
        const logDir = this._globalStoragePath
            ? path.join(this._globalStoragePath, 'pcsx2')
            : null;
        if (!logDir) { return null; }
        const logPath = path.join(logDir, 'emulog.txt');
        if (!fs.existsSync(logPath)) { return null; }

        let text: string;
        try { text = await fs.promises.readFile(logPath, 'utf8'); }
        catch { return null; }

        const out: Record<string, string> = {};
        const re = /^(?:\[[^\]]*\]\s*)?(.+?) Directory:\s*(.+?)\s*$/gm;
        for (const m of text.matchAll(re)) {
            const label = m[1].trim();
            const value = m[2].trim();
            const key = DisasmPanelProvider._pcsx2PathLabelToKey[label];
            if (!key || !value) { continue; }
            if (!(key in out)) { out[key] = value; } // first occurrence wins
        }
        if (Object.keys(out).length === 0) { return null; }

        // Backfill every captured path into the settings table so this
        // expensive read only happens once.
        for (const [key, value] of Object.entries(out)) {
            this._persistPcsx2PathSetting(key, value);
        }
        return out;
    }

    private _tryDiscoverCurrentCrc(saveDir: string, serial: string): string | null {
        if (!serial) { return null; }
        let files: string[];
        try { files = fs.readdirSync(saveDir); } catch { return null; }
        const escaped = serial.replace(/[-]/g, '\\-');
        const re = new RegExp(`^${escaped}\\s+\\(([0-9A-Fa-f]{8})\\)\\.`, 'i');
        for (const f of files) {
            const m = f.match(re);
            if (m) { return m[1].toUpperCase(); }
        }
        return null;
    }

    /**
     * Take a screenshot of the game's screen in PCSX2 via the PINE socket bridge
     * (PINESAVESCREENSHOT command → PineClient.SaveScreenshot opcode 0x32).
     * Only available in PINE mode.
     */
    public async handleSaveScreenshot(): Promise<void> {
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
        // PCSX2 .p2s save states are zips; extract them to a sibling .extracted
        // directory on demand so the hex viewer can open the embedded .bin
        // memory dumps. Idempotent — the bridge re-uses the existing folder
        // when snapshot-info.json is newer than the .p2s.
        let dirPath = snapshotPath;
        if (snapshotPath.endsWith('.p2s')) {
            try {
                dirPath = await this.socketClient.extractP2s(snapshotPath);
            } catch (err: any) {
                vscode.window.showErrorMessage(`Could not extract snapshot: ${err?.message ?? err}`);
                return;
            }
        }

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

    /**
     * Read Screenshot.png from a .p2s archive via the bridge and return it as
     * a base64-encoded string (no data-URI prefix). Returns `null` when the
     * archive has no embedded screenshot or when the bridge is unavailable.
     * Used by the Snapshot Manager webview's preview pane.
     */
    public async handleReadP2sScreenshot(p2sPath: string): Promise<string | null> {
        if (!this.socketClient.isConnected()) { return null; }
        try {
            return await this.socketClient.readP2sScreenshot(p2sPath);
        } catch (err: any) {
            getOutputChannel().debug(`readP2sScreenshot(${p2sPath}) failed: ${err?.message ?? err}`);
            return null;
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

        // Reset cached serial before resolving — keeps stale state from leaking
        // when the user switches games while still inside the same session.
        this._currentGameSerial = '';
        this.loadSnapshotsForGame(this.selectedGameName);
        this.loadGameLabels(this.selectedGameName);

        // Resolve the SCUS/SLUS serial (PINE → ISO bootElf), then upsert the
        // Game / GameRegion / GameDisc rows so the label manager can scope by
        // the stable GameDisc.Id. The upsert runs even when PCSX2 isn't
        // attached — that's what was missing before, leaving the Games table
        // empty for the loaded title.
        if (gamePath && this._bridgeMode) {
            const serial = await this._resolveSerialForGamePath(gamePath);
            if (serial) {
                this._currentGameSerial = serial;
                let discId: number | undefined;
                try {
                    let gameTitle = this.selectedGameName;
                    try {
                        const info = await this.socketClient.getPcsx2GameInfo();
                        const t = (info.Title ?? '').replace(/\0/g, '').trim();
                        if (t) { gameTitle = t; }
                    } catch { /* PCSX2 not attached — fall back to filename */ }
                    discId = await this.socketClient.updateGameDisc(serial, gamePath, undefined, gameTitle);
                } catch { /* DB upsert is best-effort */ }
                this.editorProvider.setCurrentGame(this.selectedGameName, this.workspaceRoot, serial, discId);
                // Re-list snapshots now that we know the serial — the initial
                // call above filtered with an empty serial (= show all).
                this._snapshotManagerProvider.notifySnapshotsChanged();
            } else {
                // No serial recoverable (unreadable ISO, etc.) — keep the
                // editor context with just the filename so the user can
                // still browse the Built-in set.
                this.editorProvider.setCurrentGame(this.selectedGameName, this.workspaceRoot, gamePath);
            }
        }

        void this.postLoadedGame();
    }

    /**
     * Scan the snapshots/<isoName>/ directory and populate this.snapshots.
     * Handles two snapshot formats:
     *  - Internal emulator: <date>/<name>/ directory with snapshot-info.json + .bin files
     *  - PCSX2 save states: <date>/<name>.p2s file with a companion <name>.p2s.extracted/ directory
     * Previously-deleted snapshots are automatically excluded because this always
     * re-reads the current filesystem state.
     */
    private async loadSnapshotsForGame(isoName: string): Promise<void> {
        const entries = await this.getPcsx2Savestates();
        this.snapshots = entries;
        this.webview?.postMessage({ command: 'updateSnapshots', snapshots: entries, gameName: isoName });
        this._snapshotManagerProvider.notifySnapshotsChanged();
    }

    /**
     * List PCSX2 .p2s save-state files from the directory PCSX2 reported
     * (`pcsx2.path.savestates` in the settings table, populated on the first
     * emulator connection). When a current game serial is known the list is
     * filtered to that game's save states (PCSX2 names them
     * `<SERIAL> (<CRC>).<slot>.p2s`); otherwise every .p2s file in the folder
     * is returned.
     */
    public async getPcsx2Savestates(): Promise<SnapshotEntry[]> {
        if (!this.socketClient.isConnected()) { return []; }

        const dir = await this._resolvePcsx2SavestatesDir();
        if (!dir) { return []; }

        let files: string[];
        try { files = fs.readdirSync(dir); }
        catch { return []; }

        const serialFilter = this._currentGameSerial
            ? this._currentGameSerial.trim().toUpperCase()
            : '';
        // Two-tier match: canonical PCSX2 naming (with CRC) plus a fallback
        // for user-titled saves that follow the "<SERIAL>.<slug>.p2s" form
        // we write when CRC is unknown. Files matching neither pattern still
        // appear in the list — they just lack parsed serial/slot metadata.
        const nameReFull = /^([A-Za-z]+-\d+)\s+\(([0-9A-Fa-f]{8})\)\.(.+?)\.p2s$/;
        const nameReSerialOnly = /^([A-Za-z]+-\d+)\.(.+?)\.p2s$/;
        const entries: SnapshotEntry[] = [];

        for (const name of files) {
            if (!name.toLowerCase().endsWith('.p2s')) { continue; }
            const mFull   = name.match(nameReFull);
            const mSerial = mFull ? null : name.match(nameReSerialOnly);
            const serial  = mFull ? mFull[1].toUpperCase()
                          : mSerial ? mSerial[1].toUpperCase()
                          : '';
            const crc     = mFull ? mFull[2].toUpperCase() : undefined;
            const slot    = mFull ? mFull[3]
                          : mSerial ? mSerial[2]
                          : undefined;

            // Per-game filter: only skip files whose parsed serial *differs*
            // from the current game. Files without a parseable serial fall
            // through (likely user-saved customs) so they're never lost.
            if (serialFilter && serial && serial !== serialFilter) { continue; }

            const full = path.join(dir, name);
            let mtimeMs = 0;
            let size    = 0;
            try {
                const st = fs.statSync(full);
                mtimeMs = st.mtimeMs;
                size    = st.size;
            } catch { /* unreadable — show anyway */ }

            const label = slot === 'resume'
                ? 'Resume state'
                : slot === undefined
                    ? path.basename(name, '.p2s')
                    : /^\d+$/.test(slot)
                        ? `Slot ${parseInt(slot, 10)}`
                        : slot;

            const mtime = new Date(mtimeMs || Date.now());
            entries.push({
                label,
                name,
                timestamp: mtime.toLocaleString(),
                path:      full,
                date:      mtime.toISOString().slice(0, 10),
                isoName:   serial && crc ? `${serial} (${crc})` : '',
                serial:    serial || undefined,
                crc,
                slot,
                sizeBytes: size,
                mtimeMs,
            });
        }

        entries.sort((a, b) => (b.mtimeMs ?? 0) - (a.mtimeMs ?? 0));
        return entries;
    }

    /**
     * Load game-specific labels from snapshots/<isoName>/labels.json and
     * push them to the editor provider for display in the Labels tab.
     */
    private loadGameLabels(isoName: string): void {
        // Update the editor provider's current game context so user-created
        // labels are saved to the correct per-game file. Pass the cached
        // serial when we have one, otherwise the ISO path (handleGameChanged
        // will upgrade this to a numeric discId once the upsert runs).
        this.editorProvider.setCurrentGame(
            isoName,
            this.workspaceRoot,
            this._currentGameSerial || this.selectedGamePath || undefined,
        );

        // If we don't yet have a serial cached (e.g. startup before the user
        // touches the dropdown), resolve it from the ISO in the background,
        // upsert the GameDisc row, and re-seed the editor context with the
        // numeric discId. Without this the Games + GameDiscs tables stay
        // empty for any preference-restored game.
        if (!this._currentGameSerial && this.selectedGamePath && this.socketClient.isConnected()) {
            const gamePath = this.selectedGamePath;
            void (async () => {
                try {
                    const serial = await this._resolveSerialForGamePath(gamePath);
                    if (!serial || this._currentGameSerial) { return; }
                    this._currentGameSerial = serial;
                    let discId: number | undefined;
                    try {
                        discId = await this.socketClient.updateGameDisc(serial, gamePath, undefined, isoName);
                    } catch { /* upsert is best-effort */ }
                    this.editorProvider.setCurrentGame(isoName, this.workspaceRoot, serial, discId);
                    this._snapshotManagerProvider.notifySnapshotsChanged();
                } catch { /* best-effort */ }
            })();
        }

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

    private _formatCdsCompilerLocation(filePath: string | undefined): string {
        return filePath ? `${path.basename(filePath)} (${filePath})` : '(unknown file)';
    }

    private _writeCdsCompilerHeader(channel: vscode.LogOutputChannel, filePath: string | undefined): void {
        channel.clear();
        channel.show(true);
        channel.info('CodeDesigner CDS compile started.');
        channel.info(`Source: ${this._formatCdsCompilerLocation(filePath)}`);
    }

    /** Compile the CDS document that owns an editor-title run action. */
    public async compileCdsDocument(uri?: vscode.Uri): Promise<void> {
        const targetUri = uri ?? vscode.window.activeTextEditor?.document.uri;
        if (!targetUri || targetUri.scheme !== 'file' || !targetUri.fsPath.toLowerCase().endsWith('.cds')) {
            vscode.window.showWarningMessage('Open a .cds file to compile.');
            return;
        }

        const doc = vscode.workspace.textDocuments.find(d => d.uri.toString() === targetUri.toString())
            ?? await vscode.workspace.openTextDocument(targetUri);

        this._activeCdsFilePath = doc.uri.fsPath;
        this._activeCdsContent  = doc.getText();
        await this.handleCompileCds(false);
    }

    /** Compile the active .cds file and return GameShark codes to the CDS tab. */
    private async handleCompileCds(useActiveEditor = true): Promise<void> {
        const out = getOutputChannel();
        const compilerOut = getCdsCompilerChannel();

        // Webview compile requests refresh from the active editor so unsaved
        // .cds edits are compiled. Editor-title run actions pass a specific URI
        // and pre-populate the cached file/content before calling this method.
        const activeEditor = vscode.window.activeTextEditor;
        if (useActiveEditor && activeEditor?.document.uri.fsPath.toLowerCase().endsWith('.cds')) {
            this._activeCdsFilePath = activeEditor.document.uri.fsPath;
            this._activeCdsContent  = activeEditor.document.getText();
        }

        const sourceCode = this._activeCdsContent;
        const filePath   = this._activeCdsFilePath;
        const fileUri    = filePath ? vscode.Uri.file(filePath) : undefined;

        this._writeCdsCompilerHeader(compilerOut, filePath);

        // Clear any previous diagnostics for this file before every compile run
        if (fileUri) { this._cdsDiagnostics.delete(fileUri); }

        if (!sourceCode) {
            const msg = 'No .cds file is open. Open a Code Designer Source file to compile.';
            out.warn(`CDS compile: ${msg}`);
            compilerOut.warn(msg);
            this.webview?.postMessage({
                command: 'cdsCompileResult',
                success: false,
                errors: [{ lineNumber: 0, message: msg }]
            });
            return;
        }

        out.info(`CDS compiling: ${filePath ? path.basename(filePath) : '(unknown)'}`);
        compilerOut.info(`Source length: ${sourceCode.length} character(s).`);

        if (!this.socketClient.isConnected()) {
            const msg = 'Not connected to emulator server.';
            out.warn(`CDS compile: ${msg}`);
            compilerOut.warn(msg);
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
            compilerOut.info(`Pointer labels supplied: ${pointerLabels.length}.`);

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

            compilerOut.info(`Compiler returned ${results.length} instruction result(s) and ${sidecars.length} sidecar directive(s).`);

            for (const r of results) {
                if (r.Success && r.Hex) {
                    codeLines.push(r.Hex);
                } else if (!r.Success && r.Error) {
                    const ln = r.LineNumber ?? 0;
                    compilerOut.error(`Line ${ln}: ${r.Error}`);
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

            // ── Log sidecar commands to the CDS compiler channel ─────────────
            for (const sc of sidecars) {
                const addrHex = `0x${sc.Address.toString(16).toUpperCase().padStart(8, '0')}`;
                switch (sc.Kind) {
                    case 'assert': {
                        const expHex = sc.ExpectedValue !== undefined
                            ? `0x${sc.ExpectedValue.toString(16).toUpperCase().padStart(8, '0')}` : '?';
                        if (sc.AssertPassed === true) {
                            compilerOut.info(`assert @ ${addrHex} ${sc.Operator ?? '=='} ${expHex} — passed`);
                        } else if (sc.AssertPassed === false) {
                            const actHex = sc.ActualValue !== undefined
                                ? `0x${sc.ActualValue.toString(16).toUpperCase().padStart(8, '0')}` : '?';
                            compilerOut.error(
                                `assert @ ${addrHex} ${sc.Operator ?? '=='} ${expHex} FAILED (got ${actHex})`);
                        } else {
                            compilerOut.info(`assert @ ${addrHex} ${sc.Operator ?? '=='} ${expHex} (not checked — no live backend)`);
                        }
                        break;
                    }
                    case 'breakpoint':
                        compilerOut.info(`breakpoint requested @ ${addrHex} — use SETBP to install`);
                        break;
                    case 'watchpoint':
                        compilerOut.info(`watchpoint requested: ${sc.AccessType ?? 'write'} @ ${addrHex} (${sc.WatchSize ?? 4} bytes) — use PINEADDWATCH to install`);
                        break;
                    case 'log': {
                        const directive = sc.Directive ?? 'log';
                        compilerOut.info(`${directive} @ ${addrHex}: ${sc.MessageText ?? ''}`);
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
                compilerOut.warn(`Compilation finished with ${errors.length} error(s).`);
                compilerOut.show(true);
                // Clear any stored sidecars — the patch is not valid when there are errors
                this._lastCdsSidecars = [];
                this.webview?.postMessage({ command: 'cdsCompileResult', success: false, errors });
            } else {
                const codes = codeLines.join('\n');
                // Cache sidecars so handleAddPatch can install breakpoints/watchpoints
                this._lastCdsSidecars = sidecars;
                out.info(`CDS compilation succeeded — ${codeLines.length} code(s) generated.`);
                compilerOut.info(`Compilation succeeded — ${codeLines.length} code(s) generated.`);
                compilerOut.appendLine('');
                compilerOut.appendLine('Generated GameShark codes:');
                compilerOut.appendLine(codes.length > 0 ? codes : '(no codes generated)');
                this.webview?.postMessage({ command: 'cdsCompileResult', success: true, codes, errors: [] });
            }
        } catch (err: any) {
            const msg = err?.message ?? String(err);
            compilerOut.error(`Compile error: ${msg}`);
            compilerOut.show(true);
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
        const compilerOut = getCdsCompilerChannel();
        if (!this.socketClient.isConnected()) {
            this.webview?.postMessage({ command: 'cdsPatchApplied', success: false, error: 'Not connected to emulator.' });
            return;
        }
        try {
            // 1 — Apply the GameShark memory-patch codes
            await this.socketClient.applyCheats(codes);
            out.info('CDS patch applied to memory successfully.');
            compilerOut.info('Patch applied to memory successfully.');

            // 2 — Install sidecar commands from the most recent compilation
            const sidecars = this._lastCdsSidecars;
            for (const sc of sidecars) {
                const addrHex = `0x${sc.Address.toString(16).toUpperCase().padStart(8, '0')}`;
                try {
                    switch (sc.Kind) {
                        case 'breakpoint':
                            await this.socketClient.setBreakpoint(sc.Address);
                            out.info(`CDS breakpoint installed @ ${addrHex}`);
                            compilerOut.info(`breakpoint installed @ ${addrHex}`);
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
                            compilerOut.info(`watchpoint installed: ${sc.AccessType ?? 'write'} @ ${addrHex} (${size} bytes)`);
                            break;
                        }

                        case 'log': {
                            const directive = sc.Directive ?? 'log';
                            compilerOut.info(`${directive} @ ${addrHex}: ${sc.MessageText ?? ''}`);
                            break;
                        }
                    }
                } catch (sidecarErr: any) {
                    // Log but do not abort — sidecar install is best-effort (e.g. breakpoints
                    // require PINE debugger extension; watchpoints require PINE mode).
                    out.warn(`CDS sidecar install failed for ${sc.Kind} @ ${addrHex}: ${sidecarErr?.message ?? sidecarErr}`);
                    compilerOut.warn(`sidecar install failed for ${sc.Kind} @ ${addrHex}: ${sidecarErr?.message ?? sidecarErr}`);
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
            const filePathLower = filePath.toLowerCase();
            if (!this.settings.games.some(g => g.path.toLowerCase() === filePathLower)) {
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
        // Welcome tab replaces the old logo splash. It is registered as a
        // command in extension.ts so the disasm panel does not need to import
        // the WelcomeProvider directly.
        try { await vscode.commands.executeCommand('codedesigner.openWelcome'); }
        catch { /* welcome command not registered (yet) — non-fatal */ }
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

    // ── PC navigation history / labels / network status ──────────────────────

    /** Fetch the server-side PC navigation history and push it to the panel. */
    private async handleRequestNavHistory(): Promise<void> {
        if (!this.socketClient.isConnected()) {
            this.webview?.postMessage({ command: 'navHistoryResult', error: 'Not connected to the emulator.' });
            return;
        }
        try {
            const history = await this.socketClient.getNavHistory();
            // The server records oldest-first; reverse so the panel shows newest-first.
            const entries = history.slice().reverse().map(h => ({
                address:   h.Address,
                label:     h.Label,
                source:    h.Source,
                timestamp: h.Timestamp,
            }));
            this.webview?.postMessage({ command: 'navHistoryResult', entries });
        } catch (err: any) {
            this.webview?.postMessage({ command: 'navHistoryResult', error: err?.message ?? String(err) });
        }
    }

    /** Clear the server-side navigation history, then refresh the panel list. */
    private async handleClearNavHistory(): Promise<void> {
        try {
            await this.socketClient.clearNavHistory();
        } catch { /* non-fatal — the refresh below surfaces any error */ }
        await this.handleRequestNavHistory();
    }

    /** Fetch the active label set's entries and push them to the panel. */
    private async handleRequestRecentLabels(): Promise<void> {
        if (!this.socketClient.isConnected()) {
            this.webview?.postMessage({ command: 'recentLabelsResult', error: 'Not connected to the emulator.' });
            return;
        }
        try {
            const entries = await this.editorProvider.getActiveLabelEntries();
            // Root, visible labels only — child/struct-member entries use synthetic
            // addresses that can't be navigated to directly.
            const slim = entries
                .filter(e => !e.parentAddress && !e.hidden)
                .map(e => ({ name: e.name, type: e.type ?? 'Address', address: e.address }));
            this.webview?.postMessage({ command: 'recentLabelsResult', entries: slim });
        } catch (err: any) {
            this.webview?.postMessage({ command: 'recentLabelsResult', error: err?.message ?? String(err) });
        }
    }

    /** Ping every Multiplayer server port and report reachability + capture state. */
    private async handleRequestNetworkStatus(): Promise<void> {
        const ports = loadMediusPorts();
        const servers = await Promise.all(ports.map(async p => ({
            name:   p.name,
            port:   p.port,
            online: await DisasmPanelProvider.probePortListening(p.port),
        })));
        this.webview?.postMessage({
            command:   'networkStatusResult',
            servers,
            capturing: this._packetViewerProvider.isCapturing(),
        });
    }

    /**
     * Detect whether the CodeDesigner MCP server process is currently running.
     * The MCP server is a stdio process spawned by an AI client (Claude /
     * Copilot); "running" means a client currently has it loaded. It has no
     * network port, so the OS process list is the only way to probe it.
     */
    private static detectMcpServerRunning(imageName: string): Promise<boolean> {
        const isWin = process.platform === 'win32';
        const cmd = isWin
            ? `tasklist /FI "IMAGENAME eq ${imageName}.exe" /NH /FO CSV`
            : `pgrep -f ${imageName}`;
        return new Promise<boolean>(resolve => {
            child_process.exec(cmd, { timeout: 4000, windowsHide: true }, (err, stdout) => {
                // tasklist exits 0 with an "INFO: No tasks…" line when nothing
                // matches, so test the output text. pgrep exits non-zero when
                // nothing matches.
                if (isWin) {
                    const rx = new RegExp(imageName.replace(/[.]/g, '\\.'), 'i');
                    resolve(rx.test(stdout ?? ''));
                } else {
                    resolve(!err && (stdout ?? '').trim().length > 0);
                }
            });
        });
    }

    /** Probe the CodeDesigner.AI MCP server process and push its status to the panel. */
    private async handleRequestMcpStatus(): Promise<void> {
        const online = await DisasmPanelProvider.detectMcpServerRunning(this.mcpImageName());
        this.webview?.postMessage({ command: 'mcpStatus', online });
    }

    /** Launch the deployed CodeDesigner.AI MCP server executable from the panel. */
    private async handleRunMcpServer(): Promise<void> {
        const out = getOutputChannel();

        if (await DisasmPanelProvider.detectMcpServerRunning(this.mcpImageName())) {
            this.webview?.postMessage({ command: 'mcpStatus', online: true });
            return;
        }

        if (this.mcpServerTerminal) {
            try { this.mcpServerTerminal.dispose(); } catch { /* ignore */ }
            this.mcpServerTerminal = undefined;
        }

        // Run with --profile all (full tool surface) from the panel.
        const exe = DisasmPanelProvider.findAiMcpServerExe();
        const args = ['--profile', 'all'];
        if (!exe) {
            const dir = path.join(DisasmPanelProvider.getInstallDirectory(), 'AI');
            const msg = `CodeDesigner.AI not found in ${dir}. Build/deploy the CodeDesigner.AI project first.`;
            out.error(msg);
            vscode.window.showErrorMessage(msg);
            this.webview?.postMessage({ command: 'mcpStatus', online: false });
            return;
        }

        out.info(`Starting CodeDesigner.AI MCP server: ${exe} ${args.join(' ')}`.trim());
        const terminal = vscode.window.createTerminal({
            name: 'CodeDesigner.AI MCP',
            shellPath: exe,
            shellArgs: args,
        });
        terminal.show(true);
        this.mcpServerTerminal = terminal;

        setTimeout(() => void this.handleRequestMcpStatus(), 1000);
        setTimeout(() => void this.handleRequestMcpStatus(), 2500);
    }

    /** Escape a string for a single-quoted PowerShell literal. */
    private static psQuote(value: string): string {
        return `'${value.replace(/'/g, "''")}'`;
    }

    /** Run a short child process and capture stdout/stderr. */
    private static execFileCapture(
        file: string,
        args: string[],
        timeout = 4000,
    ): Promise<{ stdout: string; stderr: string }> {
        return new Promise((resolve, reject) => {
            child_process.execFile(file, args, { timeout, windowsHide: true }, (err, stdout, stderr) => {
                if (err) { reject(err); return; }
                resolve({ stdout: String(stdout ?? ''), stderr: String(stderr ?? '') });
            });
        });
    }

    /** True when a process with the exact executable path is currently running. */
    private static async isProcessRunningByPath(exePath: string): Promise<boolean> {
        if (!exePath) { return false; }

        if (process.platform === 'win32') {
            const imageName = path.basename(exePath).replace(/'/g, "''");
            const script = [
                `$target = ${DisasmPanelProvider.psQuote(exePath)}`,
                '$target = [System.IO.Path]::GetFullPath($target)',
                `$procs = @(Get-CimInstance Win32_Process -Filter "Name='${imageName}'" | Where-Object { $_.ExecutablePath -and ([System.IO.Path]::GetFullPath($_.ExecutablePath) -ieq $target) })`,
                'if ($procs.Count -gt 0) { "1" }',
            ].join('; ');
            try {
                const { stdout } = await DisasmPanelProvider.execFileCapture(
                    'powershell.exe',
                    ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script],
                );
                return stdout.trim().length > 0;
            } catch {
                return false;
            }
        }

        const resolved = path.resolve(exePath);
        try {
            const { stdout } = await DisasmPanelProvider.execFileCapture('ps', ['-axo', 'pid=,args=']);
            return stdout.split(/\r?\n/).some(line => line.includes(resolved));
        } catch {
            return false;
        }
    }

    /** Kill all processes whose executable path exactly matches the provided path. */
    private static async killProcessesByPath(exePath: string): Promise<boolean> {
        if (!exePath) { return false; }

        if (process.platform === 'win32') {
            const imageName = path.basename(exePath).replace(/'/g, "''");
            const script = [
                `$target = ${DisasmPanelProvider.psQuote(exePath)}`,
                '$target = [System.IO.Path]::GetFullPath($target)',
                `$procs = @(Get-CimInstance Win32_Process -Filter "Name='${imageName}'" | Where-Object { $_.ExecutablePath -and ([System.IO.Path]::GetFullPath($_.ExecutablePath) -ieq $target) })`,
                'foreach ($p in $procs) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }',
                'if ($procs.Count -gt 0) { "1" }',
            ].join('; ');
            try {
                const { stdout } = await DisasmPanelProvider.execFileCapture(
                    'powershell.exe',
                    ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script],
                    6000,
                );
                return stdout.trim().length > 0;
            } catch {
                return false;
            }
        }

        const resolved = path.resolve(exePath);
        try {
            const { stdout } = await DisasmPanelProvider.execFileCapture('ps', ['-axo', 'pid=,args=']);
            const pids = stdout
                .split(/\r?\n/)
                .map(line => line.trim())
                .filter(line => line.includes(resolved))
                .map(line => Number(line.split(/\s+/, 1)[0]))
                .filter(pid => Number.isFinite(pid) && pid > 0);
            for (const pid of pids) {
                try { process.kill(pid, 'SIGTERM'); } catch { /* ignore */ }
            }
            return pids.length > 0;
        } catch {
            return false;
        }
    }

    /** Candidate emulator executable paths whose process state can be probed or killed. */
    private emulatorProcessCandidatePaths(emulatorPath = this.selectedEmulatorPath): string[] {
        const candidates: string[] = [];
        const selected = (emulatorPath ?? '').trim();
        if (selected) {
            candidates.push(this.resolvePath(selected));
        }

        const pcsx2Exe = DisasmPanelProvider.detectPcsx2Exe();
        if ((!selected || this.isPcsx2Selection(selected)) && pcsx2Exe) {
            candidates.push(pcsx2Exe);
        }

        const builtInExe = DisasmPanelProvider.findEmulatorExe();
        if (!selected && builtInExe) {
            candidates.push(builtInExe);
        }

        return Array.from(new Set(candidates.filter(Boolean).map(p => path.resolve(p))));
    }

    /** Detects the selected emulator, including instances left running before VS Code reopened. */
    private async detectSelectedEmulatorProcessRunning(emulatorPath = this.selectedEmulatorPath): Promise<boolean> {
        const selected = (emulatorPath ?? '').trim();
        const host = (this.settings.pcsx2?.host ?? '127.0.0.1').trim() || '127.0.0.1';
        const slot = this.settings.pcsx2?.slot ?? 28011;

        // PINE listeners survive VS Code reloads and are the most reliable sign
        // that PCSX2 is alive and attachable. For explicitly selected non-PCSX2
        // emulators, prefer exact process-path checks below so another emulator
        // on the same PINE port does not light this row green.
        if ((!selected || this.isPcsx2Selection(selected)) && await DisasmPanelProvider.probePortListening(slot, host)) {
            return true;
        }

        // With no explicit selection, also detect a leftover built-in emulator.
        if (!selected && await DisasmPanelProvider.probePortListening(28012)) {
            return true;
        }

        for (const candidate of this.emulatorProcessCandidatePaths(emulatorPath)) {
            if (await DisasmPanelProvider.isProcessRunningByPath(candidate)) {
                return true;
            }
        }

        return false;
    }

    /** Probe the selected emulator process and push its status to the panel. */
    private async postEmulatorProcessStatus(emulatorPath = this.selectedEmulatorPath): Promise<void> {
        const running = await this.detectSelectedEmulatorProcessRunning(emulatorPath);
        this.webview?.postMessage({ command: 'emulatorProcessStatus', running });
    }

    /** Webview request handler for the Connection-section emulator status dot. */
    private async handleRequestEmulatorProcessStatus(): Promise<void> {
        await this.postEmulatorProcessStatus();
    }

    /** Explicit user action: stop the selected emulator and refresh the process status. */
    private async handleKillEmulatorProcess(emulatorPath: string): Promise<void> {
        const out = getOutputChannel();
        const selected = (emulatorPath ?? this.selectedEmulatorPath ?? '').trim();
        if (selected) { this.selectedEmulatorPath = selected; }

        let killed = false;
        for (const candidate of this.emulatorProcessCandidatePaths(selected)) {
            if (await DisasmPanelProvider.killProcessesByPath(candidate)) {
                out.info(`Killed emulator process: ${candidate}`);
                killed = true;
            }
        }

        if (!selected || this.isPcsx2Selection(selected)) {
            this.terminateOrphanedPcsx2ProcessesIfNeeded('user requested kill', true);
            killed = true;
        }

        if (this._pcsx2Terminal) {
            try { this._pcsx2Terminal.dispose(); } catch { /* ignore */ }
            this._pcsx2Terminal = undefined;
        }
        if (this._pcsx2LogTimer) {
            try { clearInterval(this._pcsx2LogTimer); } catch { /* ignore */ }
            this._pcsx2LogTimer = undefined;
        }

        if (this.emulatorProcessTerminal) {
            try { this.emulatorProcessTerminal.dispose(); } catch { /* ignore */ }
            this.emulatorProcessTerminal = undefined;
            killed = true;
        }

        if (this.emulatorTerminal) {
            try { this.emulatorTerminal.dispose(); } catch { /* ignore */ }
            this.emulatorTerminal = undefined;
        }

        try { this.socketClient.disconnect(); } catch { /* ignore */ }
        this.cleanupProcess();

        if (!killed) {
            vscode.window.showInformationMessage('No running emulator process was detected.');
        }
        await this.postEmulatorProcessStatus(selected);
    }

    /** Maps an agent name (by keyword) to a bundled Mortal Kombat profile picture. */
    private static readonly AGENT_IMAGE_MAP: ReadonlyArray<{ file: string; keywords: string[] }> = [
        { file: 'scorpion.png',    keywords: ['scorpion'] },
        { file: 'sub-zero.png',    keywords: ['subzero'] },
        { file: 'raiden.png',      keywords: ['raiden'] },
        { file: 'liu-kang.png',    keywords: ['liukang', 'liu'] },
        { file: 'johnny-cage.png', keywords: ['johnnycage', 'johnny', 'cage'] },
        { file: 'sonya-blade.png', keywords: ['sonyablade', 'sonya'] },
        { file: 'kano.png',        keywords: ['kano'] },
        { file: 'kitana.png',      keywords: ['kitana'] },
        { file: 'mileena.png',     keywords: ['mileena'] },
    ];

    /** Resolve a webview URI for the agent's profile picture, or '' when no character matches. */
    private resolveAgentImageUri(name: string): string {
        const norm = (name ?? '').toLowerCase().replace(/[^a-z0-9]/g, '');
        if (!norm || !this.webview) { return ''; }
        const match = DisasmPanelProvider.AGENT_IMAGE_MAP.find(
            m => m.keywords.some(k => norm.includes(k)),
        );
        if (!match) { return ''; }
        return this.webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'media', 'agents', match.file),
        ).toString();
    }

    /** Fetch the AI agent profiles and push a slimmed list to the panel. */
    private async handleRequestAgents(): Promise<void> {
        if (!this.socketClient.isConnected()) {
            this.webview?.postMessage({ command: 'agentsResult', error: 'Not connected to the emulator.' });
            return;
        }
        try {
            const agents = await this.socketClient.mcpAgentList();
            const entries = agents.map(a => ({
                id:          a.id,
                name:        a.name,
                description: a.description ?? '',
                isLead:      !!a.isAgentLead,
                imageUri:    this.resolveAgentImageUri(a.name),
            }));
            this.webview?.postMessage({ command: 'agentsResult', entries });
        } catch (err: any) {
            this.webview?.postMessage({ command: 'agentsResult', error: err?.message ?? String(err) });
        }
    }

    /**
     * Set (or toggle off) the team-lead agent. Only one agent may be lead, so
     * the flag is cleared on every other agent first. Clicking the current
     * lead's star clears the lead entirely.
     */
    private async handleSetAgentLead(agentId: number): Promise<void> {
        if (!this.socketClient.isConnected()) {
            this.webview?.postMessage({ command: 'agentsResult', error: 'Not connected to the emulator.' });
            return;
        }
        try {
            const agents = await this.socketClient.mcpAgentList();
            const target = agents.find(a => a.id === agentId);
            const makeLead = !(target?.isAgentLead);
            for (const a of agents) {
                const shouldBeLead = a.id === agentId && makeLead;
                if (!!a.isAgentLead !== shouldBeLead) {
                    await this.socketClient.mcpAgentUpdate(a.id, { isAgentLead: shouldBeLead });
                }
            }
        } catch (err: any) {
            this.webview?.postMessage({ command: 'agentsResult', error: err?.message ?? String(err) });
            return;
        }
        await this.handleRequestAgents();
    }

    /**
     * Run an optional orchestration mutation, then ALWAYS re-fetch the plan
     * list and push it to the panel — together with any error. Posting the
     * fresh list on every path means a failed action never blanks the
     * section and the per-plan toggles always reflect the true backend state.
     */
    private async runOrchestrationAction(action?: () => Promise<void>): Promise<void> {
        if (!this.socketClient.isConnected()) {
            this.webview?.postMessage({ command: 'orchestrationsResult', entries: [], error: 'Not connected to the emulator.' });
            return;
        }
        let actionError = '';
        if (action) {
            try {
                await action();
            } catch (err: any) {
                actionError = err?.message ?? String(err);
            }
        }
        let entries: Array<{ id: number; name: string; description: string; isActive: boolean }> = [];
        try {
            const plans = await this.socketClient.mcpOrchestrationList();
            entries = plans.map(p => ({
                id:          p.id,
                name:        p.name,
                description: p.description ?? '',
                isActive:    !!p.isActive,
            }));
        } catch (err: any) {
            if (!actionError) { actionError = err?.message ?? String(err); }
        }
        this.webview?.postMessage({
            command: 'orchestrationsResult',
            entries,
            error:   actionError || undefined,
        });
    }

    /** Deactivate every orchestration plan; surfaces a clear hint when the Bridge is too old. */
    private async deactivateOrchestrations(): Promise<void> {
        try {
            await this.socketClient.mcpOrchestrationDeactivate();
        } catch (err: any) {
            const msg = err?.message ?? String(err);
            throw new Error(`Could not turn off the active plan (${msg}). Rebuild and restart the CodeDesigner Bridge to enable single-agent mode.`);
        }
    }

    /** Fetch the orchestration plans and push the list to the panel. */
    private async handleRequestOrchestrations(): Promise<void> {
        await this.runOrchestrationAction();
    }

    /**
     * Toggle an orchestration plan's active state. Only one plan may be active,
     * so activating one replaces any other. Clicking the active plan again
     * deactivates it (single-agent mode).
     */
    private async handleSetOrchestrationActive(id: number): Promise<void> {
        await this.runOrchestrationAction(async () => {
            const plans = await this.socketClient.mcpOrchestrationList();
            const target = plans.find(p => p.id === id);
            if (target?.isActive) {
                await this.deactivateOrchestrations();
            } else {
                await this.socketClient.mcpOrchestrationSetActive(id);
            }
        });
    }

    /**
     * Multi-agent mode master toggle. Turning it off deactivates the active
     * plan; turning it on activates the first plan when none is active yet.
     */
    private async handleSetMultiAgentMode(enabled: boolean): Promise<void> {
        await this.runOrchestrationAction(async () => {
            const plans = await this.socketClient.mcpOrchestrationList();
            if (enabled) {
                if (plans.length > 0 && !plans.some(p => p.isActive)) {
                    await this.socketClient.mcpOrchestrationSetActive(plans[0].id);
                }
            } else {
                await this.deactivateOrchestrations();
            }
        });
    }

    /** Jump the disassembler editor to a hex or numeric address from the panel. */
    private handlePanelGotoAddress(raw: unknown): void {
        const addr = typeof raw === 'string'
            ? parseInt(raw.replace(/^0x/i, ''), 16)
            : (typeof raw === 'number' ? raw : NaN);
        if (Number.isFinite(addr)) {
            this.editorProvider.navigateToAddress(addr >>> 0);
        }
    }

    // ── File mode (internal emulator, no BIOS) ────────────────────────────────

    /**
     * Probe whether a TCP listener is accepting connections on the given
     * loopback port. Used to detect an already-running InternalPineSocket
     * (default 28012) so we don't redundantly spawn a second Emulator.exe
     * that will just fail to bind the port and exit 1, polluting the UI
     * with a phantom "terminal terminated with exit code: 1" toast.
     *
     * Resolves true if the connect succeeds, false on refused / timeout /
     * any error. Times out at 500 ms so the probe never blocks the user's
     * File→Open noticeably.
     */
    private static probePortListening(port: number, host: string = '127.0.0.1'): Promise<boolean> {
        return new Promise(resolve => {
            const socket = new net.Socket();
            let settled = false;
            const done = (listening: boolean) => {
                if (settled) { return; }
                settled = true;
                try { socket.destroy(); } catch { /* ignore */ }
                resolve(listening);
            };
            socket.setTimeout(500);
            socket.once('connect', () => done(true));
            socket.once('timeout', () => done(false));
            socket.once('error',   () => done(false));
            try { socket.connect(port, host); }
            catch { done(false); }
        });
    }

    /**
     * Ensure CodeDesigner.Ps2.Emulator.exe is running in *bare* mode (no BIOS,
     * no file) so Bridge's STARTFILEMODE handler has an InternalPineSocket to
     * push the binary into. Reuses any existing emulator process — including
     * a BIOS-mode one from a previous Run, *and* one we didn't spawn ourselves
     * (e.g. a leftover instance from a previous VS Code session whose
     * extension has been reloaded) — because Bridge writes the file straight
     * into RAM via PINE and resets PC, so no fresh process spawn or
     * port-collision dance is needed.
     *
     * Returns false only when the binary itself cannot be found.
     */
    private async ensureEmulatorRunningForFileMode(): Promise<boolean> {
        const out = getOutputChannel();

        if (this.emulatorProcessTerminal) {
            out.info('Reusing running built-in emulator process for file-mode load.');
            return true;
        }

        // If something is already listening on the InternalPineSocket port
        // (28012), don't spawn another emulator — Bridge will use whatever's
        // there. Without this probe we'd spawn a second --bare which fails to
        // bind the port, exits with code 1, and shows the user a confusing
        // "terminal terminated with exit code: 1" toast even though file mode
        // actually worked.
        if (await DisasmPanelProvider.probePortListening(28012)) {
            out.info('Built-in emulator already listening on port 28012; reusing instead of spawning another.');
            return true;
        }

        const exe = DisasmPanelProvider.findEmulatorExe();
        if (!exe) {
            const installDir = DisasmPanelProvider.getInstallDirectory();
            const msg = `CodeDesigner.Ps2.Emulator binary not found in ${installDir}. Build the project first to deploy it.`;
            out.error(msg);
            vscode.window.showErrorMessage(msg);
            return false;
        }

        const args = ['--bare'];
        const preview = [exe, ...args]
            .map(v => v.includes(' ') ? `"${v}"` : v)
            .join(' ');
        out.info(`Starting built-in emulator (bare mode): ${preview}`);

        const terminal = vscode.window.createTerminal({
            name: 'CodeDesigner Emulator',
            shellPath: exe,
            shellArgs: args,
        });
        terminal.show(false);
        this.emulatorProcessTerminal = terminal;
        void this.postEmulatorProcessStatus();
        setTimeout(() => void this.postEmulatorProcessStatus(), 1500);
        return true;
    }

    /**
     * Load a PS2 ELF, BIN, or RAW file into the internal emulator in file mode
     * (STARTFILEMODE command).  Bridge writes the binary into the running
     * emulator's RAM via PINE and points PC at the entry point — no BIOS, no
     * boot sequence, no per-file process spawn. All default live memory
     * panels are opened so the user can inspect memory immediately.
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

        // Make sure *some* Emulator.exe is running — bare-mode if none yet.
        // Bridge will load the binary into its RAM via PINE, regardless of
        // whether the emulator is bare, BIOS-mode, or already file-loaded.
        if (!(await this.ensureEmulatorRunningForFileMode())) { return; }

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
