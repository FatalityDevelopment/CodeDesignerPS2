import * as vscode from 'vscode';
import * as fs from 'fs';
import { encodePng } from '../../utils/png';
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
import { CheatPorterProvider } from '../cheatPorter/cheatPorterProvider';
import { CheatScannerProvider } from '../cheatScanner/cheatScannerProvider';
import { CheatConverterProvider } from '../cheatConverter/cheatConverterProvider';
import { CheatListProvider, CheatListGame } from '../cheatList/cheatListProvider';
import { CheatListService, CheatListStore, CheatListItem, SendMode, BinaryKind, parseCheatText, newCheatId } from '../cheatList/cheatListService';
import { findCheatArchiveRoot } from '../../utils/cheatArchive';
import { MemoryRegion } from '../../shared/memoryRegions';
import { JokerMakerProvider } from '../jokerMaker/jokerMakerProvider';
import { CompatBrowserProvider } from '../compatBrowser/compatBrowserProvider';
import { ArchiveCheat, SnapshotEntry } from '../../webview/disasm-panel/types';

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
        // Individual register addresses can be read inside this span;
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


/** URL and branch for the cheat code archive repository. */
const ARCHIVE_REPO_URL    = 'https://github.com/TylerDev888/CheatCodeArchive.git';
const ARCHIVE_REMOTE      = 'origin';
const ARCHIVE_BRANCH      = 'main';

export class DisasmPanelProvider implements vscode.WebviewViewProvider {
    private webview?: vscode.Webview;
    private emulatorTerminal?: vscode.Terminal;
    /**
     * Terminal running Ps2Emulator (`ps2`), launched by Run; cleared by the
     * onDidCloseTerminal hook so a subsequent Run starts it again.
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
    /** True while a game runs in Ps2Emulator, connected (Run, or Connect to Ps2Emulator). */
    private _gameRunning: boolean = false;
    /** Cheats of the selected cheat-archive list for the running game. */
    private _archiveCheats: ArchiveCheat[] = [];
    /** Title of the selected cheat list (e.g. "GSHI", "CodeBreaker"). */
    private _archiveCheatType: string = '';
    /** Serial + title the archive cheats were fetched for. */
    private _archiveSerial: string = '';
    private _archiveGame: string = '';
    /** Serial read from the selected disc's SYSTEM.CNF (cached per path). */
    private _selectedDiscSerial: { path: string; serial: string } | undefined;
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
    /** Starts Ps2Emulator with the selected BIOS and game, or closes it when it runs. */
    public toggleConnectionFromCommand(): void {
        if (this.emulatorProcessTerminal || this.socketClient.isDirect()) {
            void this.stopPs2Emulator().then(() => this.postEmulatorProcessStatus());
        } else {
            this.handleRunPs2Emulator(this.selectedEmulatorPath, this.selectedBiosPath, this.selectedGamePath);
        }
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
            getSnapshots:    () => this.listSnapshots(),
            openSnapshot:    (p) => this.handleOpenSnapshot(p),
            reloadSnapshot:  (p) => this.handleLoadSnapshot(p),
            getScreenshot:   (p) => readSnapshotScreenshot(p),
        };
        this._snapshotManagerProvider = new SnapshotManagerProvider(_extensionUri, delegate);
        this._diffViewerProvider      = new DiffViewerProvider(_extensionUri, delegate, socketClient);
        this._cheatPorterProvider     = new CheatPorterProvider(
            _extensionUri, delegate, socketClient, async () => (await this.getGameCheatsForCheatList()) ?? []);
        this._cheatScannerProvider    = new CheatScannerProvider(_extensionUri, socketClient, editorProvider);
        this._cheatConverterProvider  = new CheatConverterProvider(_extensionUri, socketClient);
        this._cheatListService        = new CheatListService(socketClient, async () => this.getPointerLabels ? this.getPointerLabels() : []);
        this._cheatListStore          = new CheatListStore(() => this._globalStoragePath);
        this._cheatListProvider       = new CheatListProvider(_extensionUri, socketClient, this._cheatListService, this._cheatListStore, {
            getGame:        () => this.cheatListGame(),
            getSerial:        async () => (await this.getCurrentGameForCheats()).serial,
            importRepository: serial => this.importCheatRepository(serial),
            onListChanged:    () => { if (this._gameRunning) { void this.handleFetchArchiveCheats(); } },
            openTool:       tool => {
                switch (tool) {
                    case 'scanner':   this._cheatScannerProvider.open(); break;
                    case 'converter': this._cheatConverterProvider.open(); break;
                    case 'porter':    this._cheatPorterProvider.open(); break;
                    case 'editor':    void vscode.commands.executeCommand('codedesigner.openCheatsEditor'); break;
                }
            },
        });
        // The Label Manager's memory scans now live on the Cheat Scanner page.
        editorProvider.onOpenCheatScanner = () => this._cheatScannerProvider.open();
    }

    private readonly _mcpProgressReportEditorProvider: McpProgressReportEditorProvider;
    private readonly _agentCtrlPanelProvider: AgentCtrlPanelProvider;
    private readonly _compatBrowserProvider: CompatBrowserProvider;
    private readonly _packetViewerProvider: PacketViewerProvider;
    private readonly _debugCommandsProvider: DebugCommandsProvider;
    private readonly _snapshotManagerProvider: SnapshotManagerProvider;
    private readonly _diffViewerProvider: DiffViewerProvider;
    private readonly _cheatPorterProvider: CheatPorterProvider;
    private readonly _cheatScannerProvider: CheatScannerProvider;
    private readonly _cheatConverterProvider: CheatConverterProvider;
    private readonly _cheatListService: CheatListService;
    private readonly _cheatListStore: CheatListStore;
    private readonly _cheatListProvider: CheatListProvider;

    // Register the provider with VS Code
    public register(context: vscode.ExtensionContext) {
        this.extensionPath = context.extensionPath;
        this._globalStoragePath = context.globalStorageUri.fsPath;

        // Initialise workspaceRoot early so that any feature relying on it
        // (label manager, snapshots, settings persistence) works even before
        // resolveWebviewView is called — e.g. when the socket auto-connects
        // at startup before the sidebar becomes visible.
        this.loadEmuSettings();

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
                    getOutputChannel().info('Ps2Emulator exited.');
                    if (this._gameRunning) {
                        this._gameRunning = false;
                        this.webview?.postMessage({ command: 'gameRunningChanged', gameRunning: false });
                    }
                    this.emulatorProcessTerminal = undefined;
                    void this.postEmulatorProcessStatus();
                }
                if (t === this.mcpServerTerminal) {
                    getOutputChannel().info('CodeDesigner MCP server terminal closed.');
                    this.mcpServerTerminal = undefined;
                    void this.handleRequestMcpStatus();
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
                case 'startNetwork':
                    this.handleStartNetwork();
                    break;
                case 'run':
                    this.handleRunPs2Emulator(
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
                    this._cheatListProvider.refresh();
                    break;
                case 'applyCheats':
                    await this.handleApplyCheats(msg.cheatText ?? '');
                    break;
                // ── Cheats tab: CDS cheat source → compile / send / save / list ──
                case 'openCheatList':
                    this._cheatListProvider.open();
                    break;
                case 'cheatsCompile':
                case 'cheatsSend':
                case 'cheatsSaveBin':
                    await this.handleCheatsAction(msg.command, msg.source ?? '', msg.mode as SendMode, msg.kind as BinaryKind);
                    break;
                case 'cheatsAddToList':
                    this.handleCheatsAddToList(msg.source ?? '');
                    break;
                case 'fetchArchiveCheats':
                    await this.handleFetchArchiveCheats();
                    break;
                case 'selectArchiveCheatList':
                    await this.handleFetchArchiveCheats(msg.cheatListId as number);
                    break;
                case 'importCheatArchive':
                    await this.handleImportCheatArchive();
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
                // ── Cheat Porter ──────────────────────────────────────────
                case 'openCheatPorter':
                    this._cheatPorterProvider.open();
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
                    this._cheatConverterProvider.open();
                    break;
                // ── Cheat Code Porter ─────────────────────────────────────
                case 'openPorter':
                    this._cheatPorterProvider.open();
                    break;
                // ── Cheat Scanner ─────────────────────────────────────────
                case 'openScanner':
                    this._cheatScannerProvider.open();
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
        //    the developer opens client/vscode directly and the repo root is
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

        // 3. The emulators list: Ps2Emulator builds (ps2.exe) the user added. PCSX2 and the
        //    built-in emulator are gone, so their entries are dropped. The Bridge is
        //    resolved from the install directory, never from this list.
        this.settings.emulators = (this.settings.emulators ?? []).filter(e =>
            !/pcsx2|codedesigner\.ps2\.emulator/i.test(`${e.type} ${e.path}`));

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
     * Persist the current emulator settings (games list, BIOS list, emulators list).
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
            gameRunning: this._gameRunning,
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
     * currently selected ISO, read from its SYSTEM.CNF. Returns undefined
     * when it yields none — callers must NOT substitute the ISO path.
     */
    private async _resolveSerialForGamePath(gamePath: string): Promise<string | undefined> {
        if (!this.socketClient.isConnected() || !gamePath) { return undefined; }
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
     * Resolve the currently loaded game's rich metadata (region, CRC, PCSX2-list
     * compatibility status, cover art) and push it to the panel's Loaded Game
     * card. Falls back to a bare title when the game is not in the local
     * compatibility database.
     */
    private async postLoadedGame(): Promise<void> {
        if (!this.webview) { return; }

        let serial: string | undefined;
        let title: string | undefined = this.selectedGameName || undefined;

        // The disc serial, read straight from the selected ISO so the card
        // (and cover art) update as soon as a game is picked.
        if (this.selectedGamePath && this.socketClient.isConnected()) {
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

    private getControlServerExecutablePath(): string | null {
        return DisasmPanelProvider.findBridgeExe();
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
        this.launchControlServerAndConnect(emulatorPath);
    }

    /**
     * Run: start the workspace service (the Bridge) if it is not up, launch Ps2Emulator with the
     * selected BIOS and disc (or an ELF) and its debug server, then connect to it directly.
     */
    private handleRunPs2Emulator(emulatorPath: string, biosPath: string, gamePath: string, elfPath?: string): void {
        const out = getOutputChannel();
        this.selectedEmulatorPath = emulatorPath;
        this.selectedBiosPath = biosPath;
        this.selectedGamePath = gamePath;
        out.show(true);
        void (async () => {
            try {
                const exe = emulatorPath ? this.resolvePath(emulatorPath) : '';
                if (!exe || !fs.existsSync(exe)) {
                    throw new Error('Select Ps2Emulator (ps2.exe) as the emulator first.');
                }
                const bios = biosPath ? this.resolvePath(biosPath) : '';
                if (!elfPath && !bios) {
                    throw new Error('Select a BIOS first.');
                }
                if (!this.socketClient.isConnected() && !this.emulatorTerminal) {
                    this.launchControlServerAndConnect(emulatorPath);
                }
                await this.stopPs2Emulator();

                const address = vscode.workspace.getConfiguration('codedesigner')
                    .get<string>('ps2Emulator.debugServer', '127.0.0.1:28100');
                const [host, portText] = address.includes(':') ? address.split(':') : ['127.0.0.1', address];
                const port = parseInt(portText, 10);
                const args = elfPath ? ['elf', elfPath] : ['bios', bios];
                if (elfPath && bios) { args.push('--bios', bios); }
                if (!elfPath && gamePath) { args.push('--disc', this.resolvePath(gamePath)); }
                args.push(...this.ps2EmulatorStorageArgs(bios), '--debug-server', address, '--window');
                if (vscode.workspace.getConfiguration('codedesigner').get<boolean>('ps2Emulator.network', false)) {
                    args.push('--network');
                }
                out.info(`Starting Ps2Emulator: ${exe} ${args.join(' ')}`);
                this.emulatorProcessTerminal = vscode.window.createTerminal({ name: 'Ps2Emulator', shellPath: exe, shellArgs: args });

                if (!await this._waitForPortListening(port, host, 20000)) {
                    throw new Error(`Ps2Emulator's debug server did not start on ${address}.`);
                }
                await vscode.commands.executeCommand('codedesigner.connectPs2Emulator');
                if (!this.socketClient.isDirect()) {
                    throw new Error(`Could not connect to Ps2Emulator at ${address}.`);
                }
                this._gameRunning = true;
                this.webview?.postMessage({ command: 'gameRunningChanged', gameRunning: true });
                void vscode.commands.executeCommand('codedesigner.openGsFrameViewer');
                for (const r of LIVE_REGIONS_DEFAULT) {
                    await this.editorProvider.openLiveMemoryView(r.region, r.address, r.size, r.label);
                }
                await this._registerBreakpointsAndResume(true);
                void this.handleFetchArchiveCheats();
                void this.postLoadedGame();
                void this.postEmulatorProcessStatus();
            } catch (err: any) {
                out.error(`Run failed: ${err?.message ?? err}`);
                vscode.window.showErrorMessage(`Run failed: ${err?.message ?? err}`);
            }
        })();
    }

    /**
     * Ps2Emulator's NVRAM and memory card, kept in the extension's storage: the NVRAM starts as a
     * copy of the .NVM beside the BIOS (so the BIOS settings are there), and both are saved on exit.
     */
    private ps2EmulatorStorageArgs(bios: string): string[] {
        if (!this._globalStoragePath) { return []; }
        const dir = path.join(this._globalStoragePath, 'ps2emulator');
        fs.mkdirSync(dir, { recursive: true });
        const args = ['--memcard', path.join(dir, 'card1.ps2')];
        if (bios) {
            const nvram = path.join(dir, `${path.basename(bios, path.extname(bios))}.nvm`);
            const beside = [bios.replace(/\.[^.\\/]+$/, '.NVM'), bios.replace(/\.[^.\\/]+$/, '.nvm')].find(f => fs.existsSync(f));
            if (!fs.existsSync(nvram) && beside) { fs.copyFileSync(beside, nvram); }
            args.push('--nvram', nvram);
        }
        return args;
    }

    /** Closes the Ps2Emulator this panel launched, and its direct connection. */
    private async stopPs2Emulator(): Promise<void> {
        await vscode.commands.executeCommand('codedesigner.disconnectPs2Emulator');
        if (this.emulatorProcessTerminal) {
            try { this.emulatorProcessTerminal.dispose(); } catch { /* ignore */ }
            this.emulatorProcessTerminal = undefined;
        }
        if (this._gameRunning) {
            this._gameRunning = false;
            this.webview?.postMessage({ command: 'gameRunningChanged', gameRunning: false });
        }
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
        const resolvedExe = this.getControlServerExecutablePath();
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
                out.info('Connected to the workspace service.');
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
        this._paused = false;
        this.webview?.postMessage({ command: 'connectionStatus', connected: false });
        void this.postEmulatorProcessStatus();
    }

    private clearPendingSocketRetry(): void {
        if (this._connectRetryTimer) {
            clearTimeout(this._connectRetryTimer);
            this._connectRetryTimer = undefined;
        }
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

    /** Step into (non-blocking). Marks sidebar as running until next pause event. */
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

    /** Step over (non-blocking). Marks sidebar as running until next pause event. */
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

    /** Restart: close Ps2Emulator and run the selected BIOS and disc again. */
    private async handleRestart(): Promise<void> {
        this.editorProvider.resetLivePanels();
        await this.stopPs2Emulator();
        this.handleRunPs2Emulator(this.selectedEmulatorPath, this.selectedBiosPath, this.selectedGamePath);
    }

    /**
     * Re-sends every breakpoint the user has set (tracked in the extension process,
     * not the Angular webview) to Ps2Emulator, then optionally resumes it.
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
        if (!this.socketClient.isDirect()) { return; }
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

        if (!this.socketClient.isDirect()) {
            vscode.window.showWarningMessage('Ps2Emulator is not running.');
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

        const snapshotsDir = path.join(this.snapshotsRoot(), isoName, dateStr, safeName);

        out.info(`Taking snapshot → ${snapshotsDir}`);

        try {
            const regions = await this.socketClient.snapshot(snapshotsDir, isoName, safeName);
            out.info(`Snapshot complete — ${regions.length} region(s) written.`);

            // Re-scan the snapshots directory so the list reflects what is
            // actually on disk (handles externally deleted snapshots too).
            this.loadSnapshotsForGame(isoName);
        } catch (err: any) {
            out.error(`Snapshot failed: ${err?.message ?? err}`);
            vscode.window.showErrorMessage(`Snapshot failed: ${err?.message ?? err}`);
        }
    }

    /** Saves the frame Ps2Emulator displays now as a PNG (asks where). */
    public async handleSaveScreenshot(): Promise<void> {
        const out = getOutputChannel();
        try {
            const frame = await this.socketClient.screenshot();
            const name = `${this.selectedGameName || 'ps2'}-${DisasmPanelProvider.formatSnapshotTimestamp(new Date())}.png`;
            const target = await vscode.window.showSaveDialog({
                defaultUri: vscode.Uri.file(path.join(this.workspaceRoot || require('os').homedir(), name)),
                filters: { 'PNG image': ['png'] },
            });
            if (!target) { return; }
            fs.writeFileSync(target.fsPath, encodePng(frame.width, frame.height, frame.rgba));
            out.info(`Screenshot saved: ${target.fsPath}`);
        } catch (err: any) {
            out.error(`Save screenshot failed: ${err?.message ?? err}`);
            vscode.window.showErrorMessage(`Save screenshot failed: ${err?.message ?? err}`);
        }
    }
    /** Opens a snapshot's memory dumps (.bin files) in the disassembler. */
    public async handleOpenSnapshot(snapshotPath: string): Promise<void> {
        const dirPath = snapshotPath;

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

    /** Reload a snapshot — writes its RAM back into Ps2Emulator. */
    public async handleLoadSnapshot(snapshotPath: string): Promise<void> {
        const out = getOutputChannel();

        if (!this.socketClient.isDirect()) {
            vscode.window.showWarningMessage('Ps2Emulator is not running.');
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

        // Reset cached serial before resolving — keeps stale state from leaking
        // when the user switches games while still inside the same session.
        this._currentGameSerial = '';
        this.loadSnapshotsForGame(this.selectedGameName);
        this.loadGameLabels(this.selectedGameName);

        // Resolve the SCUS/SLUS serial (the ISO's bootElf), then upsert the
        // Game / GameRegion / GameDisc rows so the label manager can scope by
        // the stable GameDisc.Id. The upsert runs even when no emulator runs,
        // so the Games table has the loaded title.
        if (gamePath) {
            const serial = await this._resolveSerialForGamePath(gamePath);
            if (serial) {
                this._currentGameSerial = serial;
                let discId: number | undefined;
                try {
                    const gameTitle = this.selectedGameName;
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
     * Previously-deleted snapshots are automatically excluded because this always
     * re-reads the current filesystem state.
     */
    private async loadSnapshotsForGame(isoName: string): Promise<void> {
        const entries = await this.listSnapshots();
        this.snapshots = entries;
        this.webview?.postMessage({ command: 'updateSnapshots', snapshots: entries, gameName: isoName });
        this._snapshotManagerProvider.notifySnapshotsChanged();
    }

    /** Where snapshots are kept: the workspace's snapshots folder, else a temporary one. */
    private snapshotsRoot(): string {
        return this.workspaceRoot
            ? path.join(this.workspaceRoot, 'snapshots')
            : path.join(require('os').tmpdir(), 'ps2snapshots');
    }

    /**
     * The selected game's snapshots (every game's when none is selected), newest first: the
     * directories under snapshots/<game>/<date>/ with a snapshot-info.json.
     */
    public async listSnapshots(): Promise<SnapshotEntry[]> {
        const root = this.snapshotsRoot();
        const games = this.selectedGameName ? [this.selectedGameName] : safeReadDir(root);
        const entries: SnapshotEntry[] = [];
        for (const game of games) {
            for (const date of safeReadDir(path.join(root, game))) {
                for (const name of safeReadDir(path.join(root, game, date))) {
                    const dir = path.join(root, game, date, name);
                    const infoPath = path.join(dir, 'snapshot-info.json');
                    if (!fs.existsSync(infoPath)) { continue; }
                    let info: { name?: string; timestamp?: string; regions?: { Size?: number }[] } = {};
                    try { info = JSON.parse(fs.readFileSync(infoPath, 'utf-8')); } catch { /* show it anyway */ }
                    const mtimeMs = fs.statSync(infoPath).mtimeMs;
                    const when = new Date(info.timestamp ?? mtimeMs);
                    entries.push({
                        label:     info.name || name,
                        name,
                        timestamp: when.toLocaleString(),
                        path:      dir,
                        date,
                        isoName:   game,
                        sizeBytes: (info.regions ?? []).reduce((sum, r) => sum + (r.Size ?? 0), 0),
                        mtimeMs,
                    });
                }
            }
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

    /**
     * The game the Cheat List and Cheats tab work on. Keyed by the selected game first
     * so the saved list doesn't move when the serial is resolved later; the serial is
     * the key only when no game file is selected.
     */
    private cheatListGame(): CheatListGame {
        const name = this.selectedGameName || this._currentGameSerial || '';
        return { key: this.selectedGameName || this._currentGameSerial || 'no-game', name };
    }

    /**
     * Cheats from the cheat archive for the Cheat List's "Game cheats" import and the
     * Cheat Porter. Works without an emulator (the serial comes from the selected disc). When
     * the game has several cheat lists, the user picks one; returns null on cancel.
     */
    private async getGameCheatsForCheatList(): Promise<Array<{ name: string; codes: string[] }> | null> {
        if (!this.socketClient.isConnected()) { throw new Error('Not connected to the CodeDesigner bridge.'); }

        const game = await this.getCurrentGameForCheats();
        const serial = game.serial ?? '';
        if (!serial) {
            throw new Error(this.selectedGamePath
                ? `Could not read the serial from ${path.basename(this.selectedGamePath)}.`
                : 'Select a game in the Emulator tab first.');
        }

        const doc = this._cheatListStore.load(this.cheatListGame().key);
        let result = await this.socketClient.getGameCheats(serial, doc.archiveCheatListId);
        if (result.lists.length === 0) {
            throw new Error(`The cheat archive has no cheat lists for ${serial}. Create one in the cheat archive editor, ` +
                'or use "Import from repository" in the Cheats tab to import the game\'s cheats from the cheat repository.');
        }
        if (result.lists.length > 1) {
            const pick = await vscode.window.showQuickPick(
                result.lists.map(l => ({
                    label:       l.title,
                    description: `${l.cheatCount} cheat${l.cheatCount === 1 ? '' : 's'}${l.forDisc ? '' : ' · whole game'}`,
                    picked:      l.id === result.cheatListId,
                    id:          l.id,
                })),
                { title: `Cheat list for ${result.gameTitle ?? game.title ?? serial}`, placeHolder: 'Pick the cheat list to import' },
            );
            if (!pick) { return null; }
            if (pick.id !== result.cheatListId) { result = await this.socketClient.getGameCheats(serial, pick.id); }
        }
        return result.cheats.map(c => ({ name: c.name, codes: c.codes }));
    }

    /** The game being played, for cheat features: the selected disc (serial from its SYSTEM.CNF, cached). */
    public async getCurrentGameForCheats(): Promise<{ serial?: string; title?: string }> {
        if (this._currentGameSerial) { return { serial: this._currentGameSerial, title: this.selectedGameName }; }
        if (!this.selectedGamePath) { return {}; }
        if (this._selectedDiscSerial?.path !== this.selectedGamePath) {
            const serial = await this._resolveSerialForGamePath(this.resolvePath(this.selectedGamePath));
            if (!serial) { return { title: this.selectedGameName }; }
            this._selectedDiscSerial = { path: this.selectedGamePath, serial };
        }
        return { serial: this._selectedDiscSerial.serial, title: this.selectedGameName };
    }

    /** A synced CheatCodeArchive clone (cloned on first use) for importing its text files. */
    public async getArchiveCloneRoot(): Promise<string | undefined> {
        if (!this._globalStoragePath) { return undefined; }
        await this._syncArchiveRepo();
        const root = path.join(this._globalStoragePath, 'CheatCodeArchive');
        return fs.existsSync(path.join(root, 'Database')) ? root : undefined;
    }

    /**
     * Import one game's cheats from the cheat repository (CheatCodeArchive `Database/`
     * text files) into cheat lists in the cheat archive, attached to the game that owns
     * the serial. Uses the workspace checkout, else the extension's synced clone.
     */
    private async importCheatRepository(serial: string): Promise<string> {
        const root = (await findCheatArchiveRoot(this._extensionUri)) ?? (await this.getArchiveCloneRoot());
        if (!root) { throw new Error('No cheat repository (CheatCodeArchive with a Database folder) was found.'); }
        const message = await vscode.window.withProgress(
            { location: vscode.ProgressLocation.Notification, title: `Importing ${serial} from the cheat repository…` },
            () => this.socketClient.cheatEditorImport(root, serial));
        getOutputChannel().info(`[Cheats] ${message}`);
        return message;
    }

    /** Sidebar "Import from repository": import the running game's cheats, then reload. */
    private async handleImportCheatArchive(): Promise<void> {
        try {
            this.webview?.postMessage({ command: 'archiveCheatsLoading' });
            const serial = (await this.getCurrentGameForCheats()).serial;
            if (!serial) { throw new Error('No game serial available (is a game running?).'); }
            await this.importCheatRepository(serial);
            await this.handleFetchArchiveCheats();
            this._cheatListProvider.refresh();
        } catch (err: any) {
            this.webview?.postMessage({ command: 'archiveCheatsError', error: `Import failed: ${err?.message ?? err}` });
        }
    }

    /**
     * Cheats tab Compile / Send / Save .bin: the textarea is CDS cheat source (raw codes,
     * assembly, freeze/cond/timer; `#Name` lines separate cheats). Shares the engine hook
     * settings with the Cheat List page and asks for a hook address when none is saved.
     */
    private async handleCheatsAction(command: string, source: string, mode: SendMode, kind: BinaryKind): Promise<void> {
        const post = (result: Record<string, unknown>) => this.webview?.postMessage({ command: 'cheatsResult', action: command, ...result });
        try {
            const items: CheatListItem[] = parseCheatText(source).map(c => ({ id: newCheatId(), name: c.name, enabled: true, source: c.source }));
            const compiled = await this._cheatListService.compile(items);
            const errors = compiled.cheats.flatMap(c => c.errors.map(e => `${c.name} line ${e.line}: ${e.message}`));
            post({ success: errors.length === 0, phase: 'compiled', codes: compiled.text, errors });
            if (command === 'cheatsCompile' || errors.length > 0) { return; }

            const game = this.cheatListGame();
            const doc = this._cheatListStore.load(game.key);
            const needsHook = (command === 'cheatsSend' && mode === 'engine') || (command === 'cheatsSaveBin' && kind === 'engine');
            if (needsHook && doc.hookAddress === undefined) {
                const text = await vscode.window.showInputBox({
                    title: 'Cheat engine hook address',
                    prompt: 'An instruction pair that runs every frame (no branches, or jr ra + its delay slot). Saved for this game.',
                    placeHolder: '0012A4F0',
                    validateInput: v => /^\s*(0x)?[0-9a-f]{1,8}\s*$/i.test(v) ? undefined : 'Enter a hex address',
                });
                if (text === undefined) { post({ success: false, phase: 'done', message: 'Cancelled — no hook address.' }); return; }
                doc.hookAddress = parseInt(text.trim().replace(/^0x/i, ''), 16) >>> 0;
                this._cheatListStore.update(game.key, { hookAddress: doc.hookAddress });
            }

            const message = command === 'cheatsSend'
                ? await this._cheatListService.send(compiled, mode, doc)
                : await this._cheatListService.saveBinary(compiled, kind, doc, game.name);
            if (message) { post({ success: true, phase: 'done', message }); }
            if (command === 'cheatsSend' && mode === 'engine') { this._cheatListProvider.refresh(); }
        } catch (err: any) {
            const message = err?.message ?? String(err);
            getOutputChannel().error(`[Cheats] ${message}`);
            post({ success: false, phase: 'done', message });
        }
    }

    /** Hand the Cheats tab source to the Cheat List page's open list (unsaved until Save to list). */
    private handleCheatsAddToList(source: string): void {
        const added = parseCheatText(source).map(c => ({ id: newCheatId(), name: c.name, enabled: true, source: c.source }));
        if (added.length === 0) { return; }
        this._cheatListProvider.addCheats(added);
        this.webview?.postMessage({ command: 'cheatsResult', action: 'cheatsAddToList', success: true, phase: 'done',
            message: `Added ${added.length} cheat(s) to the Cheat List — Save to list there to keep them.` });
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
     * The running game's archive cheats for the Controller page's joker
     * detection. Re-fetches when the game changed since the last load.
     */
    public async getGameCheatsForJoker(): Promise<{ game: string; serial: string; cheats: Array<{ name: string; codes: string[] }> }> {
        if (!this._gameRunning) { throw new Error('Game cheats need a game running in Ps2Emulator.'); }
        const { serial, title } = await this.getCurrentGameForCheats();
        if (!serial) { throw new Error('No game serial available (is a game running?).'); }
        if (serial !== this._archiveSerial || this._archiveCheats.length === 0) {
            await this.handleFetchArchiveCheats();
        }
        return {
            game:   this._archiveGame || title || '',
            serial,
            cheats: this._archiveCheats.map(c => ({ name: c.name, codes: c.codes })),
        };
    }

    /**
     * Load the running game's cheats from the cheat archive (database), from
     * `cheatListId` or else the list chosen for this game before, then broadcast
     * them with the game's available lists and update the label overlays.
     */
    private async handleFetchArchiveCheats(cheatListId?: number): Promise<void> {
        if (!this._gameRunning) { return; }

        const out = getOutputChannel();
        this.webview?.postMessage({ command: 'archiveCheatsLoading' });

        const game = await this.getCurrentGameForCheats();
        const serial = game.serial ?? '';
        const gameTitle = game.title ?? '';

        if (!serial) {
            this.webview?.postMessage({ command: 'archiveCheatsError', error: 'No game serial available (is a game running?).' });
            return;
        }

        out.info(`Archive cheats: searching for serial "${serial}" (${gameTitle})…`);
        this._archiveSerial = serial;
        this._archiveGame   = gameTitle;

        try {
            // The list chosen for this game is remembered with its Cheat List document.
            const gameKey = this.cheatListGame().key;
            const doc = this._cheatListStore.load(gameKey);
            const result = await this.socketClient.getGameCheats(serial, cheatListId ?? doc.archiveCheatListId);
            if (cheatListId !== undefined && result.cheatListId === cheatListId) {
                this._cheatListStore.update(gameKey, { archiveCheatListId: cheatListId });
                // The Cheat List page shows the same list.
                this._cheatListProvider.refresh();
            }

            const displayTitle = result.gameTitle || gameTitle;
            this._archiveCheats = result.cheats.map(c => ({
                name:    c.name,
                author:  c.author,
                notes:   c.notes,
                codes:   c.codes,
                enabled: false,
            }));
            this._archiveCheatType = result.cheatListTitle ?? '';
            this._archiveGame      = displayTitle;

            out.info(result.lists.length > 0
                ? `Archive cheats: loaded ${this._archiveCheats.length} cheats from "${this._archiveCheatType}" for "${displayTitle}".`
                : `Archive cheats: the cheat archive has no lists for "${serial}".`);
            this.webview?.postMessage({
                command:     'archiveCheatsLoaded',
                cheats:      this._archiveCheats,
                game:        displayTitle,
                serial,
                cheatType:   this._archiveCheatType,
                lists:       result.lists,
                cheatListId: result.cheatListId,
            });

            // Labels are shown for every archive cheat, whether or not it is toggled on.
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
     * the latest changes. Used as the import source for the cheat archive when
     * no CheatCodeArchive checkout is open in the workspace. Returns the shared
     * in-flight promise if a sync is already running so concurrent callers do
     * not start duplicate operations.
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
        // <repo>/client/vscode/, so ancestor directories may contain
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
                        compilerOut.info(`watchpoint requested: ${sc.AccessType ?? 'write'} @ ${addrHex} (${sc.WatchSize ?? 4} bytes) — installed when sent to Ps2Emulator`);
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
                            await this.socketClient.addWatch(sc.Address, endAddr, cond);
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
                    // Log but do not abort — sidecar install is best-effort (it needs
                    // Ps2Emulator running).
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

        // Pull authoritative user preferences from the DB and refresh the UI.
        await this.loadUserPreferencesFromDb();
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
     * Looks for CodeDesigner.Ps2.Bridge in the install directory.
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

    /** Whether Ps2Emulator runs: launched from this panel, or connected directly. */
    private async detectSelectedEmulatorProcessRunning(_emulatorPath = this.selectedEmulatorPath): Promise<boolean> {
        return !!this.emulatorProcessTerminal || this.socketClient.isDirect();
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

    /** Explicit user action: close Ps2Emulator and refresh the process status. */
    private async handleKillEmulatorProcess(emulatorPath: string): Promise<void> {
        const selected = (emulatorPath ?? this.selectedEmulatorPath ?? '').trim();
        if (selected) { this.selectedEmulatorPath = selected; }
        const running = await this.detectSelectedEmulatorProcessRunning();
        await this.stopPs2Emulator();
        if (!running) {
            vscode.window.showInformationMessage('Ps2Emulator is not running.');
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
     * loopback port (Ps2Emulator's debug server, while it starts).
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
     * Opens a file in Ps2Emulator: an ELF runs as a program (with the selected BIOS, if any); other
     * binaries are imported into the running emulator's memory.
     */
    public async openFileMode(filePath: string): Promise<void> {
        if (/\.elf$/i.test(filePath)) {
            this.handleRunPs2Emulator(this.selectedEmulatorPath, this.selectedBiosPath, '', filePath);
            return;
        }
        if (!this.socketClient.isDirect()) {
            vscode.window.showWarningMessage('Run Ps2Emulator first, then import the binary (Actions → Import).');
            return;
        }
        await this.handleImportBin();
    }
}

/** The entries of a directory, or none when it cannot be read. */
function safeReadDir(dir: string): string[] {
    try { return fs.readdirSync(dir); } catch { return []; }
}

/** A snapshot's Screenshot.png as base64 (no data-URI prefix), or null when it has none. */
async function readSnapshotScreenshot(snapshotPath: string): Promise<string | null> {
    try {
        return (await fs.promises.readFile(path.join(snapshotPath, 'Screenshot.png'))).toString('base64');
    } catch {
        return null;
    }
}
