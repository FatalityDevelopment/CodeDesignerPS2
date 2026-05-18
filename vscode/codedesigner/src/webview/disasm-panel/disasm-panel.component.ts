import { Component, OnInit, AfterViewInit, OnDestroy, ViewChild, ViewEncapsulation } from '@angular/core';
import { VsCodeBridgeService } from '../shared/vscode-bridge.service';
import { Subscription } from 'rxjs';
import {
  PanelStatusBarComponent,
  SettingsTabComponent,
  CheatsTabComponent,
  CdsTabComponent,
  RecompileTabComponent,
} from './components';
import type { McpMemoryEntry, McpMemoryListResult, McpMemoryActionResult } from './components';
import { EmuOption, SnapshotEntry, NavHistoryEntry, RecentLabelEntry, NetworkStatus, AgentSummary } from './types';
import { IconComponent } from '../shared/icon.component';
import './disasm-panel.css';

/** Poll interval for the MCP server process status badge. */
const MCP_POLL_MS = 8000;

/** Message sent by the provider when connection state changes. */
interface ConnectionStatusMsg {
  type: 'connectionStatus';
  connected: boolean;
}

/** Message sent by the provider when the emulator pauses or resumes. */
interface PausedMsg {
  type: 'paused';
  paused: boolean;
}

/** Message sent by the provider when the snapshot list changes. */
interface UpdateSnapshotsMsg {
  type: 'updateSnapshots';
  snapshots: SnapshotEntry[];
  gameName?: string;
}

/** Message sent by the provider after an ISO file-picker dialog. */
interface BrowseGameResultMsg {
  command: 'browseGameResult';
  filePath: string;
  gameOptions?: EmuOption[];
}

/** Message sent by the provider after a BIOS file-picker dialog. */
interface BrowseBiosResultMsg {
  command: 'browseBiosResult';
  filePath: string;
  biosOptions?: EmuOption[];
}

/** Message sent by the provider after an emulator file-picker dialog. */
interface BrowseEmulatorResultMsg {
  command: 'browseEmulatorResult';
  filePath: string;
  emulators?: EmuOption[];
}

/** Message sent by the provider with initial panel data (options, existing snapshots). */
interface InitMsg {
  type: 'init';
  emulators: EmuOption[];
  biosOptions: EmuOption[];
  gameOptions: EmuOption[];
  snapshots: SnapshotEntry[];
  connectedEmulatorPath?: string;
  selectedGameName?: string;
  isConnected?: boolean;
  isPaused?: boolean;
  isPcsx2?: boolean;
  selectedEmulator?: string;
  selectedBios?: string;
  selectedGame?: string;
}

interface Pcsx2ModeChangedMsg {
  command: 'pcsx2ModeChanged';
  isPcsx2: boolean;
}

/** Rich metadata for the currently loaded game (Loaded Game card). */
export interface LoadedGameInfo {
  title: string;
  serial?: string;
  crc?: string;
  region?: string;
  status?: string;
  lastTestedVersion?: string;
  lastTestedDate?: string;
}

/** Message sent by the provider with the loaded game's details + cover art. */
interface LoadedGameMsg {
  command: 'loadedGame';
  game: LoadedGameInfo | null;
  coverUri?: string;
}

/** Message sent by the provider with the PC navigation history. */
interface NavHistoryResultMsg {
  command: 'navHistoryResult';
  entries?: NavHistoryEntry[];
  error?: string;
}

/** Message sent by the provider with the active label set's entries. */
interface RecentLabelsResultMsg {
  command: 'recentLabelsResult';
  entries?: RecentLabelEntry[];
  error?: string;
}

/** Message sent by the provider with Multiplayer server reachability. */
interface NetworkStatusResultMsg {
  command: 'networkStatusResult';
  servers?: NetworkStatus['servers'];
  capturing?: boolean;
}

/** Message sent by the provider with the MCP server process status. */
interface McpStatusMsg {
  command: 'mcpStatus';
  online: boolean;
}

/** Message sent by the provider with the AI agent profiles. */
interface AgentsResultMsg {
  command: 'agentsResult';
  entries?: AgentSummary[];
  error?: string;
}

@Component({
  selector: 'app-disasm-panel',
  standalone: true,
  imports: [
    PanelStatusBarComponent,
    SettingsTabComponent,
    CheatsTabComponent,
    CdsTabComponent,
    RecompileTabComponent,
    IconComponent,
  ],
  encapsulation: ViewEncapsulation.None,
  templateUrl: './disasm-panel.component.html',
})
export class DisasmPanelComponent implements OnInit, AfterViewInit, OnDestroy {
  isConnected = false;
  isPaused = false;
  isPcsx2 = false;
  /** Mirrors the provider-side followAiEnabled state for the status bar. */
  followAiEnabled = false;
  emulators: EmuOption[] = [];
  biosOptions: EmuOption[] = [];
  gameOptions: EmuOption[] = [];
  snapshots: SnapshotEntry[] = [];
  selectedEmulator = '';
  selectedBios = '';
  selectedGame = '';
  activeTab: 'settings' | 'cheats' | 'cds' | 'recompile' = 'settings';

  /** Cheat text owned here so the CDS "Add Cheat" action can inject codes. Persisted via localStorage. */
  private static readonly CHEAT_TEXT_KEY = 'codedesigner.cheatsText';
  private _cheatText = localStorage.getItem(DisasmPanelComponent.CHEAT_TEXT_KEY) ?? '';
  get cheatText(): string { return this._cheatText; }
  set cheatText(value: string) {
    this._cheatText = value;
    localStorage.setItem(DisasmPanelComponent.CHEAT_TEXT_KEY, value);
  }
  currentGameName = '';
  /** Rich loaded-game metadata for the Loaded Game card; null until resolved. */
  loadedGame: LoadedGameInfo | null = null;
  /** Cover-art data URI for the loaded game, or '' when unavailable. */
  loadedGameCover = '';
  /** Collapsed/expanded state of the Loaded Game accordion card. */
  loadedGameOpen = true;
  /** PC navigation history shown in the History section. */
  navHistory: NavHistoryEntry[] = [];
  navHistoryError = '';
  /** Active label set entries shown in the Recent Labels section. */
  recentLabels: RecentLabelEntry[] = [];
  recentLabelsError = '';
  /** Multiplayer server reachability + capture state for the Network section. */
  networkStatus: NetworkStatus | null = null;
  /** True when the CodeDesigner MCP server process is running. */
  mcpOnline = false;
  /** AI agent profiles shown in the Agents section. */
  agents: AgentSummary[] = [];
  agentsError = '';
  private subs: Subscription[] = [];
  private mcpPollTimer?: ReturnType<typeof setInterval>;

  @ViewChild(SettingsTabComponent) private settingsTab?: SettingsTabComponent;

  constructor(private bridge: VsCodeBridgeService) {}

  ngOnInit(): void {
    this.subs.push(
      this.bridge.on<ConnectionStatusMsg>('connectionStatus').subscribe(msg => {
        this.isConnected = msg.connected;
        this.isPaused = false;
        if (!msg.connected) { this.isPcsx2 = false; }
        // Reload memories when the socket connects (in case the first load failed while disconnected)
        if (msg.connected) { this.settingsTab?.load(); }
      }),
      this.bridge.on<Pcsx2ModeChangedMsg>('pcsx2ModeChanged').subscribe(msg => {
        this.isPcsx2 = msg.isPcsx2;
      }),
      this.bridge.on<PausedMsg>('paused').subscribe(msg => {
        this.isPaused = msg.paused;
      }),
      this.bridge.on<UpdateSnapshotsMsg>('updateSnapshots').subscribe(msg => {
        this.snapshots = msg.snapshots ?? [];
        if (msg.gameName !== undefined) { this.currentGameName = msg.gameName; }
      }),
      this.bridge.on<InitMsg>('init').subscribe(msg => {
        if (msg.emulators?.length)   { this.emulators   = msg.emulators;   }
        if (msg.biosOptions?.length) { this.biosOptions  = msg.biosOptions; }
        if (msg.gameOptions?.length) { this.gameOptions  = msg.gameOptions; }
        // Always sync snapshot list (may be empty array to clear old entries)
        if (msg.snapshots !== undefined)         { this.snapshots       = msg.snapshots;       }
        if (msg.connectedEmulatorPath)           { this.selectedEmulator = msg.connectedEmulatorPath; }
        // Restore saved dropdown selections from settings.json
        if (msg.selectedEmulator !== undefined)  { this.selectedEmulator = msg.selectedEmulator; }
        if (msg.selectedBios     !== undefined)  { this.selectedBios     = msg.selectedBios;     }
        if (msg.selectedGame     !== undefined)  { this.selectedGame     = msg.selectedGame;     }
        if (msg.selectedGameName !== undefined)  { this.currentGameName  = msg.selectedGameName; }
        // Restore connection/pause state so the UI is correct if the sidebar
        // opens after auto-connect has already succeeded.
        if (msg.isConnected !== undefined) { this.isConnected = msg.isConnected; }
        if (msg.isPaused    !== undefined) { this.isPaused    = msg.isPaused;    }
        if (msg.isPcsx2     !== undefined) { this.isPcsx2     = msg.isPcsx2;     }
      }),
      this.bridge.on<BrowseGameResultMsg>('browseGameResult').subscribe(msg => {
        if (msg.filePath) {
          if (msg.gameOptions) { this.gameOptions = msg.gameOptions; }
          this.onSelectedGameChange(msg.filePath);
        }
      }),
      this.bridge.on<BrowseBiosResultMsg>('browseBiosResult').subscribe(msg => {
        if (msg.filePath) {
          if (msg.biosOptions) { this.biosOptions = msg.biosOptions; }
          this.onSelectedBiosChange(msg.filePath);
        }
      }),
      this.bridge.on<BrowseEmulatorResultMsg>('browseEmulatorResult').subscribe(msg => {
        if (msg.filePath) {
          if (msg.emulators) { this.emulators = msg.emulators; }
          this.onSelectedEmulatorChange(msg.filePath);
        }
      }),
      // MCP Memory Manager messages from the provider
      this.bridge.on<{ command: 'mcpMemoryListResult'; entries?: McpMemoryEntry[]; error?: string }>('mcpMemoryListResult').subscribe(msg => {
        this.settingsTab?.setListResult({ entries: msg.entries, error: msg.error });
      }),
      this.bridge.on<{ command: 'mcpMemoryActionResult'; entry?: McpMemoryEntry; error?: string; message?: string }>('mcpMemoryActionResult').subscribe(msg => {
        this.settingsTab?.setActionResult({ entry: msg.entry, error: msg.error, message: msg.message });
      }),
      // Follow-AI state sync from the extension host
      this.bridge.on<{ enabled: boolean }>('followAiState').subscribe(msg => {
        this.followAiEnabled = !!msg.enabled;
      }),
      // Loaded-game card metadata (cover art, region, CRC, compat status)
      this.bridge.on<LoadedGameMsg>('loadedGame').subscribe(msg => {
        this.loadedGame = msg.game ?? null;
        this.loadedGameCover = msg.coverUri ?? '';
      }),
      // PC navigation history for the History section
      this.bridge.on<NavHistoryResultMsg>('navHistoryResult').subscribe(msg => {
        this.navHistory = msg.entries ?? [];
        this.navHistoryError = msg.error ?? '';
      }),
      // Active label set entries for the Recent Labels section
      this.bridge.on<RecentLabelsResultMsg>('recentLabelsResult').subscribe(msg => {
        this.recentLabels = msg.entries ?? [];
        this.recentLabelsError = msg.error ?? '';
      }),
      // Multiplayer server reachability for the Network section
      this.bridge.on<NetworkStatusResultMsg>('networkStatusResult').subscribe(msg => {
        this.networkStatus = { servers: msg.servers ?? [], capturing: !!msg.capturing };
      }),
      // MCP server process status for the badge above the tabs
      this.bridge.on<McpStatusMsg>('mcpStatus').subscribe(msg => {
        this.mcpOnline = !!msg.online;
      }),
      // AI agent profiles for the Agents section
      this.bridge.on<AgentsResultMsg>('agentsResult').subscribe(msg => {
        this.agents = msg.entries ?? [];
        this.agentsError = msg.error ?? '';
      }),
    );
    this.bridge.postMessage({ command: 'ready' });
    // Poll the MCP server process status for the always-visible status badge.
    this.requestMcpStatus();
    this.mcpPollTimer = setInterval(() => this.requestMcpStatus(), MCP_POLL_MS);
  }

  ngAfterViewInit(): void {
    // Load memories list after the view (and child components) are fully initialized
    this.settingsTab?.load();
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
    if (this.mcpPollTimer !== undefined) { clearInterval(this.mcpPollTimer); }
  }

  /** Ask the provider whether the MCP server process is running. */
  requestMcpStatus(): void {
    this.bridge.postMessage({ command: 'requestMcpStatus' });
  }

  // ── Status bar handlers ──────────────────────────────────────────────────

  togglePause(): void {
    this.bridge.postMessage({ command: 'togglePause' });
  }

  step(): void {
    this.bridge.postMessage({ command: 'step' });
  }

  stepInto(): void {
    this.bridge.postMessage({ command: 'stepInto' });
  }

  stepOver(): void {
    this.bridge.postMessage({ command: 'stepOver' });
  }

  onControlModeChange(mode: 'live' | 'ai-control'): void {
    this.bridge.postMessage({ command: 'toggleFollowAi', enabled: mode === 'ai-control' });
  }

  // ── Tab navigation ───────────────────────────────────────────────────────

  setTab(tab: 'settings' | 'cheats' | 'cds' | 'recompile'): void {
    this.activeTab = tab;
  }

  /** Display name of the active view, shown in the view toolbar. */
  get viewLabel(): string {
    switch (this.activeTab) {
      case 'cheats':    return 'Cheats';
      case 'cds':       return 'CDS';
      case 'recompile': return 'Recompile';
      default:          return 'Emulator';
    }
  }

  // ── Loaded Game card helpers ─────────────────────────────────────────────

  /** First letter of the loaded game's title, for the cover placeholder. */
  get loadedGameInitial(): string {
    return (this.loadedGame?.title?.trim()[0] ?? '?').toUpperCase();
  }

  /** CSS modifier for the compatibility-status badge. */
  loadedStatusClass(status: string | undefined): string {
    switch ((status ?? '').toLowerCase()) {
      case 'perfect':  return 'perfect';
      case 'playable': return 'playable';
      case 'ingame':   return 'ingame';
      case 'menus':    return 'menus';
      default:         return 'unknown';
    }
  }

  // ── Settings tab handlers ────────────────────────────────────────────────

  connect(): void {
    this.bridge.postMessage({
      command: 'connect',
      emulatorPath: this.selectedEmulator,
      biosPath: this.selectedBios,
      gamePath: this.selectedGame,
    });
  }

  runPcsx2(): void {
    this.bridge.postMessage({
      command: 'runPcsx2',
      emulatorPath: this.selectedEmulator,
      biosPath: this.selectedBios,
      gamePath: this.selectedGame,
    });
  }

  startNetwork(): void {
    this.bridge.postMessage({ command: 'startNetwork' });
  }

  restart(): void {
    this.bridge.postMessage({ command: 'restart' });
  }

  snapshot(): void {
    this.bridge.postMessage({ command: 'snapshot' });
  }

  snapshotPcsx2(): void {
    this.bridge.postMessage({ command: 'snapshotPcsx2' });
  }

  openGsViewer(): void {
    this.bridge.postMessage({ command: 'openGsViewer' });
  }

  /** Open the markdown help guide that matches the active tab. */
  openHelp(): void {
    const docs: Record<'settings' | 'cheats' | 'cds' | 'recompile', string> = {
      settings:  'docs/README.md',
      cheats:    'docs/cheat-engine.md',
      cds:       'CODE_DESIGNER_SOURCE.md',
      recompile: 'docs/recompiler.md',
    };
    this.bridge.postMessage({ command: 'openMarkdownHelp', file: docs[this.activeTab] });
  }

  openDocs(): void {
    this.bridge.postMessage({ command: 'openMarkdownHelp', file: 'docs/README.md' });
  }

  openArchiveDocs(): void {
    this.bridge.postMessage({ command: 'openMarkdownHelp', file: 'CheatCodeArchive/consoles/README.md' });
  }

  importBin(): void {
    this.bridge.postMessage({ command: 'importBin' });
  }

  openSnapshot(snap: SnapshotEntry): void {
    this.bridge.postMessage({ command: 'openSnapshot', path: snap.path });
  }

  reloadSnapshot(snap: SnapshotEntry): void {
    this.bridge.postMessage({ command: 'reloadSnapshot', path: snap.path });
  }

  onSelectedEmulatorChange(path: string): void {
    this.selectedEmulator = path;
    this.bridge.postMessage({ command: 'emulatorChanged', emulatorPath: path });
  }

  onSelectedBiosChange(path: string): void {
    this.selectedBios = path;
    this.bridge.postMessage({ command: 'biosChanged', biosPath: path });
  }

  onSelectedGameChange(path: string): void {
    this.selectedGame = path;
    this.bridge.postMessage({ command: 'gameChanged', gamePath: path });
  }

  browseEmulator(): void {
    this.bridge.postMessage({ command: 'browseEmulator' });
  }

  browseBios(): void {
    this.bridge.postMessage({ command: 'browseBios' });
  }

  browseGame(): void {
    this.bridge.postMessage({ command: 'browseGame' });
  }

  // ── CDS tab cross-tab handler ────────────────────────────────────────────

  /** Called when the CDS tab's "Add Cheat" button is clicked. */
  onAddCheat(codes: string): void {
    this.cheatText = this.cheatText
      ? this.cheatText.trimEnd() + '\n' + codes
      : codes;
    this.activeTab = 'cheats';
  }

  // ── MCP Memory Manager handlers ──────────────────────────────────────────

  onMcpMemoryList(): void {
    this.bridge.postMessage({ command: 'mcpMemoryList' });
  }

  onMcpMemoryAdd(name: string): void {
    this.bridge.postMessage({ command: 'mcpMemoryAdd', name });
  }

  onMcpMemoryEdit(entry: McpMemoryEntry): void {
    this.bridge.postMessage({ command: 'mcpMemoryEdit', id: entry.id, name: entry.name, content: entry.content });
  }

  onMcpMemoryExport(entry: McpMemoryEntry): void {
    this.bridge.postMessage({ command: 'mcpMemoryExport', id: entry.id, name: entry.name });
  }

  onMcpMemoryImport(): void {
    this.bridge.postMessage({ command: 'mcpMemoryImport' });
  }

  onMcpMemoryDelete(id: number): void {
    this.bridge.postMessage({ command: 'mcpMemoryDelete', id });
  }

  onOpenAiConfig(): void {
    this.bridge.postMessage({ command: 'openAiConfig' });
  }

  onOpenPacketViewer(): void {
    this.bridge.postMessage({ command: 'openPacketViewer' });
  }

  onOpenLabelManager(): void {
    this.bridge.postMessage({ command: 'openLabelManager' });
  }

  onOpenSnapshotManager(): void {
    this.bridge.postMessage({ command: 'openSnapshotManager' });
  }

  onOpenDiffViewer(): void {
    this.bridge.postMessage({ command: 'openDiffViewer' });
  }

  onOpenCompatBrowser(): void {
    this.bridge.postMessage({ command: 'openCompatBrowser' });
  }

  onOpenCheatsEditor(): void {
    this.bridge.postMessage({ command: 'openCheatsEditor' });
  }

  onOpenMemoryCard(): void {
    this.bridge.postMessage({ command: 'openMemoryCard' });
  }

  onOpenHdd(): void {
    this.bridge.postMessage({ command: 'openHdd' });
  }

  onOpenMemoryView(label: string): void {
    this.bridge.postMessage({ command: 'openMemoryView', label });
  }

  onOpenDnsMapping(): void {
    this.bridge.postMessage({ command: 'openDnsMapping' });
  }

  onOpenNetConfig(): void {
    this.bridge.postMessage({ command: 'openNetConfig' });
  }

  // ── History / Recent Labels / Network status handlers ────────────────────

  onRequestHistory(): void {
    this.bridge.postMessage({ command: 'requestNavHistory' });
  }

  onClearHistory(): void {
    this.bridge.postMessage({ command: 'clearNavHistory' });
  }

  onRequestLabels(): void {
    this.bridge.postMessage({ command: 'requestRecentLabels' });
  }

  onRequestNetworkStatus(): void {
    this.bridge.postMessage({ command: 'requestNetworkStatus' });
  }

  /** Jump the disassembler editor to a history entry or label address. */
  onGotoAddress(address: string): void {
    this.bridge.postMessage({ command: 'panelGotoAddress', address });
  }

  onRequestAgents(): void {
    this.bridge.postMessage({ command: 'requestAgents' });
  }

  /** Set (or toggle off) the team-lead agent. */
  onSetAgentLead(agentId: number): void {
    this.bridge.postMessage({ command: 'setAgentLead', agentId });
  }

  /** Open the AI Config agents tab, optionally focused on a specific agent. */
  onOpenAiConfigAgents(agentId: number | undefined): void {
    this.bridge.postMessage({ command: 'openAiConfigAgents', agentId });
  }

  onOpenConverter(): void {
    this.bridge.postMessage({ command: 'openConverter' });
  }

  onOpenPorter(): void {
    this.bridge.postMessage({ command: 'openPorter' });
  }

  onOpenScanner(): void {
    this.bridge.postMessage({ command: 'openScanner' });
  }
}
