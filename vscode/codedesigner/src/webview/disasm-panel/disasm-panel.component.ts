import { Component, OnInit, OnDestroy, ViewEncapsulation } from '@angular/core';
import { VsCodeBridgeService } from '../shared/vscode-bridge.service';
import { Subscription } from 'rxjs';
import {
  PanelStatusBarComponent,
  SettingsTabComponent,
  CheatsTabComponent,
  CdsTabComponent,
  RecompileTabComponent,
} from './components';
import { EmuOption, SnapshotEntry } from './types';
import './disasm-panel.css';

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
  selectedEmulator?: string;
  selectedBios?: string;
  selectedGame?: string;
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
  ],
  encapsulation: ViewEncapsulation.None,
  templateUrl: './disasm-panel.component.html',
})
export class DisasmPanelComponent implements OnInit, OnDestroy {
  isConnected = false;
  isPaused = false;
  emulators: EmuOption[] = [];
  biosOptions: EmuOption[] = [];
  gameOptions: EmuOption[] = [];
  snapshots: SnapshotEntry[] = [];
  selectedEmulator = '';
  selectedBios = '';
  selectedGame = '';
  activeTab: 'settings' | 'cheats' | 'cds' | 'recompile' = 'settings';
  /** Cheat text owned here so the CDS "Add Cheat" action can inject codes. */
  cheatText = '';
  currentGameName = '';
  private subs: Subscription[] = [];

  constructor(private bridge: VsCodeBridgeService) {}

  ngOnInit(): void {
    this.subs.push(
      this.bridge.on<ConnectionStatusMsg>('connectionStatus').subscribe(msg => {
        this.isConnected = msg.connected;
        this.isPaused = false;
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
      })
    );
    this.bridge.postMessage({ command: 'ready' });
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
  }

  // ── Status bar handlers ──────────────────────────────────────────────────

  togglePause(): void {
    this.bridge.postMessage({ command: 'togglePause' });
  }

  step(): void {
    this.bridge.postMessage({ command: 'step' });
  }

  // ── Tab navigation ───────────────────────────────────────────────────────

  setTab(tab: 'settings' | 'cheats' | 'cds' | 'recompile'): void {
    this.activeTab = tab;
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

  run(): void {
    this.bridge.postMessage({
      command: 'run',
      emulatorPath: this.selectedEmulator,
      biosPath: this.selectedBios,
      gamePath: this.selectedGame,
    });
  }

  startPcsx2(): void {
    this.bridge.postMessage({
      command: 'startPcsx2',
      emulatorPath: this.selectedEmulator,
      biosPath: this.selectedBios,
      gamePath: this.selectedGame,
    });
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
}
