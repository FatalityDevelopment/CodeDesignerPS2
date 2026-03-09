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
      this.bridge.on<Record<string, unknown>>('connectionStatus').subscribe(msg => {
        this.isConnected = !!(msg['connected']);
        this.isPaused = false;
      }),
      this.bridge.on<Record<string, unknown>>('paused').subscribe(msg => {
        this.isPaused = !!(msg['paused']);
        if (!this.isPaused) { /* resumed */ }
      }),
      this.bridge.on<Record<string, unknown>>('updateSnapshots').subscribe(msg => {
        const snaps = msg['snapshots'] as Array<Record<string, string>> | undefined;
        this.snapshots = (snaps ?? []).map(s => ({
          label: s['label'] ?? '',
          timestamp: s['timestamp'] ?? '',
          path: s['path'] ?? '',
        }));
      })
    );
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
