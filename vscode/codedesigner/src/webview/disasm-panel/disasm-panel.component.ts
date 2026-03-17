import { Component, OnInit, OnDestroy, ViewEncapsulation } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { VsCodeBridgeService } from '../shared/vscode-bridge.service';
import { Subscription } from 'rxjs';
import './disasm-panel.css';

interface EmuOption {
  label: string;
  value: string;
}

interface SnapshotEntry {
  label: string;
  timestamp: string;
  path: string;
  name: string;
  date: string;
  isoName: string;
}

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

/** Message sent by the provider after applying cheats. */
interface CheatsAppliedMsg {
  command: 'cheatsApplied';
  success: boolean;
  error?: string;
}

/** Message sent by the provider after a CDS compile. */
interface CdsCompileResultMsg {
  command: 'cdsCompileResult';
  success: boolean;
  codes?: string;
  errors?: Array<{ lineNumber: number; message: string }>;
}

/** Message sent by the provider after Add Patch. */
interface CdsPatchAppliedMsg {
  command: 'cdsPatchApplied';
  success: boolean;
  error?: string;
}

/** One ELF or IRX file listed on the disc. */
interface DiscElfEntry {
  Path: string;
  Name: string;
  Size: number;
  IsBootElf: boolean;
}

/** Message sent by the provider after a LISTDISCELFS call. */
interface ListDiscElfsResultMsg {
  command: 'listDiscElfsResult';
  success: boolean;
  elfFiles?: DiscElfEntry[];
  bootElf?: string;
  error?: string;
}

/** Message sent by the provider after a RECOMPILE call. */
interface RecompileResultMsg {
  command: 'recompileResult';
  success: boolean;
  functionCount?: number;
  writtenFiles?: string[];
  bootElf?: string;
  error?: string;
}

/** Message sent by the provider after a folder-picker dialog for the output directory. */
interface BrowseOutputDirectoryResultMsg {
  command: 'browseOutputDirectoryResult';
  directory: string;
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
  imports: [CommonModule, FormsModule],
  encapsulation: ViewEncapsulation.None,
  templateUrl: './disasm-panel.component.html'
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
  cheatText = '';
  cheatMessage = '';
  cheatError = false;
  currentGameName = '';
  // CDS tab state
  cdsOutput = '';
  // Recompile tab state
  recompileElfList: DiscElfEntry[] = [];
  recompileSelectedElf = '';
  recompileOutputDir = '';
  recompileRunning = false;
  recompileStatus = '';
  recompileIsError = false;
  recompileWrittenFiles: string[] = [];
  private subs: Subscription[] = [];

  get statusText(): string {
    if (this.isPaused) { return '⏸ PAUSED'; }
    return this.isConnected ? 'LIVE' : 'STOPPED';
  }

  get isPcsx2Selected(): boolean {
    return (this.selectedEmulator ?? '').toLowerCase().includes('pcsx2');
  }

  get isCodeDesignerSelected(): boolean {
    return !this.isPcsx2Selected;
  }

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
        if (msg.emulators?.length)  { this.emulators    = msg.emulators;    }
        if (msg.biosOptions?.length){ this.biosOptions   = msg.biosOptions;  }
        if (msg.gameOptions?.length){ this.gameOptions   = msg.gameOptions;  }
        // Always sync snapshot list (may be empty array to clear old entries)
        if (msg.snapshots !== undefined) { this.snapshots = msg.snapshots; }
        if (msg.connectedEmulatorPath) { this.selectedEmulator = msg.connectedEmulatorPath; }
        // Restore saved dropdown selections from settings.json
        if (msg.selectedEmulator !== undefined) { this.selectedEmulator = msg.selectedEmulator; }
        if (msg.selectedBios     !== undefined) { this.selectedBios     = msg.selectedBios;     }
        if (msg.selectedGame     !== undefined) { this.selectedGame     = msg.selectedGame;     }
        if (msg.selectedGameName !== undefined) { this.currentGameName = msg.selectedGameName; }
        // Restore connection/pause state so the UI is correct if the sidebar
        // opens after auto-connect has already succeeded.
        if (msg.isConnected !== undefined) { this.isConnected = msg.isConnected; }
        if (msg.isPaused    !== undefined) { this.isPaused    = msg.isPaused;    }
      }),
      this.bridge.on<CheatsAppliedMsg>('cheatsApplied').subscribe(msg => {
        this.cheatError   = !msg.success;
        this.cheatMessage = msg.success ? 'Cheats applied!' : (msg.error ?? 'Failed to apply cheats.');
        setTimeout(() => { this.cheatMessage = ''; }, 4000);
      }),
      this.bridge.on<CdsCompileResultMsg>('cdsCompileResult').subscribe(msg => {
        if (msg.success && msg.codes) {
          this.cdsOutput = msg.codes;
        } else {
          this.cdsOutput = '';
        }
      }),
      this.bridge.on<CdsPatchAppliedMsg>('cdsPatchApplied').subscribe(_msg => {
        // Patch results are logged to the Debug output channel server-side
      }),
      this.bridge.on<ListDiscElfsResultMsg>('listDiscElfsResult').subscribe(msg => {
        if (msg.success && msg.elfFiles) {
          this.recompileElfList = msg.elfFiles;
          // Default to the BOOT2 executable if available
          const boot = msg.elfFiles.find(e => e.IsBootElf);
          if (boot) { this.recompileSelectedElf = boot.Name; }
          this.recompileStatus  = '';
          this.recompileIsError = false;
        } else {
          this.recompileStatus  = msg.error ?? 'Failed to load ELF list.';
          this.recompileIsError = true;
        }
      }),
      this.bridge.on<RecompileResultMsg>('recompileResult').subscribe(msg => {
        this.recompileRunning = false;
        if (msg.success) {
          this.recompileStatus      = `Done — ${msg.functionCount ?? 0} function(s) recompiled.`;
          this.recompileIsError     = false;
          this.recompileWrittenFiles = msg.writtenFiles ?? [];
        } else {
          this.recompileStatus      = msg.error ?? 'Recompilation failed.';
          this.recompileIsError     = true;
          this.recompileWrittenFiles = [];
        }
      }),
      this.bridge.on<BrowseOutputDirectoryResultMsg>('browseOutputDirectoryResult').subscribe(msg => {
        if (msg.directory) {
          this.recompileOutputDir = msg.directory;
        }
      }),
      this.bridge.on<BrowseGameResultMsg>('browseGameResult').subscribe(msg => {
        if (msg.filePath) {
          if (msg.gameOptions) { this.gameOptions = msg.gameOptions; }
          this.selectedGame = msg.filePath;
          this.onGameChange();
        }
      }),
      this.bridge.on<BrowseBiosResultMsg>('browseBiosResult').subscribe(msg => {
        if (msg.filePath) {
          if (msg.biosOptions) { this.biosOptions = msg.biosOptions; }
          this.selectedBios = msg.filePath;
          this.onBiosChange();
        }
      }),
      this.bridge.on<BrowseEmulatorResultMsg>('browseEmulatorResult').subscribe(msg => {
        if (msg.filePath) {
          if (msg.emulators) { this.emulators = msg.emulators; }
          this.selectedEmulator = msg.filePath;
          this.onEmulatorChange();
        }
      })
    );
    this.bridge.postMessage({ command: 'ready' });
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
  }

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

  togglePause(): void {
    this.bridge.postMessage({ command: 'togglePause' });
  }

  step(): void {
    this.bridge.postMessage({ command: 'step' });
  }

  openSnapshot(snap: SnapshotEntry): void {
    this.bridge.postMessage({ command: 'openSnapshot', path: snap.path });
  }

  reloadSnapshot(snap: SnapshotEntry): void {
    this.bridge.postMessage({ command: 'reloadSnapshot', path: snap.path });
  }

  onEmulatorChange(): void {
    this.bridge.postMessage({ command: 'emulatorChanged', emulatorPath: this.selectedEmulator });
  }

  onBiosChange(): void {
    this.bridge.postMessage({ command: 'biosChanged', biosPath: this.selectedBios });
  }

  onGameChange(): void {
    this.bridge.postMessage({ command: 'gameChanged', gamePath: this.selectedGame });
  }

  setTab(tab: 'settings' | 'cheats' | 'cds' | 'recompile'): void {
    this.activeTab = tab;
  }

  applyCheats(): void {
    this.bridge.postMessage({ command: 'applyCheats', cheatText: this.cheatText });
  }

  compileCds(): void {
    this.bridge.postMessage({ command: 'compileCds' });
  }

  addPatch(): void {
    if (!this.cdsOutput) { return; }
    this.bridge.postMessage({ command: 'addPatch', codes: this.cdsOutput });
  }

  addCheat(): void {
    if (!this.cdsOutput) { return; }
    // Append compiled codes to the cheats tab list and switch to it
    this.cheatText = this.cheatText
      ? this.cheatText.trimEnd() + '\n' + this.cdsOutput
      : this.cdsOutput;
    this.activeTab = 'cheats';
  }

  listDiscElfs(): void {
    this.recompileElfList    = [];
    this.recompileStatus     = 'Loading ELF list…';
    this.recompileIsError    = false;
    this.recompileWrittenFiles = [];
    this.bridge.postMessage({ command: 'listDiscElfs', gamePath: this.selectedGame });
  }

  browseOutputDirectory(): void {
    this.bridge.postMessage({ command: 'browseOutputDirectory' });
  }

  browseGame(): void {
    this.bridge.postMessage({ command: 'browseGame' });
  }

  browseBios(): void {
    this.bridge.postMessage({ command: 'browseBios' });
  }

  browseEmulator(): void {
    this.bridge.postMessage({ command: 'browseEmulator' });
  }

  runRecompile(): void {
    if (!this.selectedGame || this.recompileRunning) { return; }
    this.recompileRunning    = true;
    this.recompileStatus     = 'Recompiling…';
    this.recompileIsError    = false;
    this.recompileWrittenFiles = [];
    this.bridge.postMessage({
      command:         'recompile',
      gamePath:        this.selectedGame,
      elfName:         this.recompileSelectedElf,
      outputDirectory: this.recompileOutputDir,
    });
  }

  formatBytes(bytes: number): string {
    if (bytes < 1024) { return `${bytes} B`; }
    if (bytes < 1024 * 1024) { return `${(bytes / 1024).toFixed(1)} KB`; }
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
