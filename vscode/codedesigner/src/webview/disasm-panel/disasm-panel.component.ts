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
}

/** Message sent by the provider with initial panel data (options, existing snapshots). */
interface InitMsg {
  type: 'init';
  emulators: EmuOption[];
  biosOptions: EmuOption[];
  gameOptions: EmuOption[];
  snapshots: SnapshotEntry[];
  connectedEmulatorPath?: string;
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
    ⏭ Step
  </button>
</div>

<h3>Emulator</h3>
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
    <button id="restartBtn" (click)="restart()" [disabled]="!isConnected">⟳ Restart</button>
  </div>

  <div class="row">
    <button id="snapshotBtn" (click)="snapshot()" [disabled]="!isConnected">Take Snapshot</button>
  </div>

  <div *ngIf="snapshots.length > 0" class="snapshot-list">
    <h4>Snapshots</h4>
    <div *ngFor="let snap of snapshots" class="snapshot-item">
      <span class="snapshot-label">{{snap.label}}</span>
      <span class="snapshot-time">{{snap.timestamp}}</span>
      <button class="open-snapshot-btn" (click)="openSnapshot(snap)">Open</button>
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
      }),
      this.bridge.on<InitMsg>('init').subscribe(msg => {
        if (msg.emulators?.length)  { this.emulators    = msg.emulators;    }
        if (msg.biosOptions?.length){ this.biosOptions   = msg.biosOptions;  }
        if (msg.gameOptions?.length){ this.gameOptions   = msg.gameOptions;  }
        if (msg.snapshots?.length)  { this.snapshots     = msg.snapshots;    }
        if (msg.connectedEmulatorPath) { this.selectedEmulator = msg.connectedEmulatorPath; }
      })
    );
    // Signal to the provider that subscriptions are set up and we are ready
    // to receive messages (especially the initial 'init' payload).
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

  restart(): void {
    this.bridge.postMessage({ command: 'restart' });
  }

  snapshot(): void {
    this.bridge.postMessage({ command: 'snapshot' });
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

  onEmulatorChange(): void {
    this.bridge.postMessage({ command: 'emulatorChanged', emulatorPath: this.selectedEmulator });
  }

  onBiosChange(): void {
    this.bridge.postMessage({ command: 'biosChanged', biosPath: this.selectedBios });
  }

  onGameChange(): void {
    this.bridge.postMessage({ command: 'gameChanged', gamePath: this.selectedGame });
  }
}
