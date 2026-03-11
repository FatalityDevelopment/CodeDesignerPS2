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
}

@Component({
  selector: 'app-disasm-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  encapsulation: ViewEncapsulation.None,
  template: `
<div id="emu-status-bar"
     [class.status-connected]="isConnected && !isPaused"
     [class.status-disconnected]="!isConnected && !isPaused"
     [class.status-paused]="isPaused">
  <span id="emu-status-dot">●</span>
  <span id="emu-status-text">{{statusText}}</span>
  <button id="emu-play-pause-btn" (click)="togglePause()" title="Pause/Resume emulator">
    {{isPaused ? '▶ Resume' : '⏸ Pause'}}
  </button>
  <button id="emu-step-btn" (click)="step()" [disabled]="!isConnected || !isPaused" title="Single step one EE instruction (only while paused)">
    ▷| Step
  </button>
</div>

<div id="panel-tabs">
  <button class="panel-tab" [class.active]="activeTab === 'settings'" (click)="setTab('settings')">Emulator Settings</button>
  <button class="panel-tab" [class.active]="activeTab === 'cheats'" (click)="setTab('cheats')">Cheats</button>
  <button class="panel-tab" [class.active]="activeTab === 'cds'" (click)="setTab('cds')">CDS</button>
</div>

<div id="tab-settings" class="tab-content" [style.display]="activeTab === 'settings' ? 'block' : 'none'">
  <div class="container">

    <div class="row">
      <select id="emulatorSelect" [(ngModel)]="selectedEmulator" (change)="onEmulatorChange()">
        <option value="">Select Emulator...</option>
        <option *ngFor="let e of emulators" [value]="e.value">{{e.label}}</option>
      </select>
    </div>

    <div class="row">
      <select id="biosSelect" [(ngModel)]="selectedBios" (change)="onBiosChange()">
        <option value="">Select BIOS...</option>
        <option *ngFor="let b of biosOptions" [value]="b.value">{{b.label}}</option>
      </select>
    </div>

    <div class="row">
      <select id="gameSelect" [(ngModel)]="selectedGame" (change)="onGameChange()">
        <option value="">Select Game...</option>
        <option *ngFor="let g of gameOptions" [value]="g.value">{{g.label}}</option>
      </select>
    </div>

    <div class="row">
      <button id="connectBtn" (click)="connect()">
        {{isConnected ? 'Disconnect' : 'Connect'}}
      </button>
    </div>

    <div class="row">
      <button id="runBtn" (click)="run()">▶ Run</button>
    </div>

    <div class="row">
      <button id="restartBtn" (click)="restart()" [disabled]="!isConnected">⟳ Restart</button>
    </div>

    <div class="row">
      <button id="importBinBtn" (click)="importBin()" [disabled]="!isConnected">⬆ Import Binary</button>
    </div>

    <div class="row">
      <button id="snapshotBtn" (click)="snapshot()" [disabled]="!isConnected">Take Snapshot</button>
    </div>

    <div class="snapshot-list">
      <h4>Snapshots<span *ngIf="currentGameName" class="snapshot-game-name"> — {{currentGameName}}</span></h4>
      <div *ngIf="snapshots.length === 0" class="snapshot-empty">No snapshots yet</div>
      <div class="snapshot-scroll">
        <div *ngFor="let snap of snapshots" class="snapshot-item">
          <div class="snapshot-item-info">
            <span class="snapshot-label">{{snap.name || snap.label}}</span>
            <span class="snapshot-date-time">{{snap.date}} {{snap.timestamp}}</span>
          </div>
          <button class="open-snapshot-btn" (click)="openSnapshot(snap)">Open</button>
          <button class="reload-snapshot-btn" (click)="reloadSnapshot(snap)" [disabled]="!isConnected" title="Restore this snapshot into the live emulator">Reload</button>
        </div>
      </div>
    </div>

  </div>
</div>

<div id="tab-cheats" class="tab-content" [style.display]="activeTab === 'cheats' ? 'block' : 'none'">
  <div class="cheats-container">
    <textarea id="cheats-input" [(ngModel)]="cheatText"
      placeholder="Paste cheat codes here (GameShark/Action Replay format)&#10;&#10;Example:&#10;#Infinite Health&#10;20123456 000003E8"></textarea>
    <div class="row">
      <button id="cheatsUpdateBtn" (click)="applyCheats()" [disabled]="!isConnected">Update</button>
    </div>
    <div *ngIf="cheatMessage" class="cheat-message" [class.cheat-error]="cheatError">{{cheatMessage}}</div>
  </div>
</div>

<div id="tab-cds" class="tab-content" [style.display]="activeTab === 'cds' ? 'flex' : 'none'">
  <div class="cds-container">
    <div class="section-label">Compiled GameShark Codes</div>
    <textarea id="cds-output" [(ngModel)]="cdsOutput" readonly
      placeholder="Compiled GameShark codes will appear here after pressing Compile.&#10;&#10;Add an address directive in your .cds file:&#10;  address 0x20123456&#10;  nop&#10;  add $t0, $s0, $s1"></textarea>
    <div class="row">
      <button id="cdsCompileBtn" (click)="compileCds()" [disabled]="!isConnected">Compile</button>
      <button id="cdsAddPatchBtn" (click)="addPatch()" [disabled]="!isConnected || !cdsOutput">Add Patch</button>
    </div>
    <div class="row">
      <button id="cdsAddCheatBtn" (click)="addCheat()" [disabled]="!cdsOutput">Add Cheat</button>
    </div>
  </div>
</div>
  `
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
  activeTab: 'settings' | 'cheats' | 'cds' = 'settings';
  cheatText = '';
  cheatMessage = '';
  cheatError = false;
  currentGameName = '';
  // CDS tab state
  cdsOutput = '';
  private subs: Subscription[] = [];

  get statusText(): string {
    if (this.isPaused) { return '⏸ PAUSED'; }
    return this.isConnected ? 'LIVE' : 'STOPPED';
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

  restart(): void {
    this.bridge.postMessage({ command: 'restart' });
  }

  snapshot(): void {
    this.bridge.postMessage({ command: 'snapshot' });
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

  setTab(tab: 'settings' | 'cheats' | 'cds'): void {
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
}
