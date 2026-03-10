import { Component, OnInit, OnDestroy, ViewEncapsulation } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { VsCodeBridgeService } from '../shared/vscode-bridge.service';
import { Subscription } from 'rxjs';
import './asm-panel.css';

@Component({
  selector: 'app-asm-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="row">
  <select id="emulatorSelect" [(ngModel)]="selectedEmulator" (change)="onEmulatorChange()">
    <option value="">Select Emulator...</option>
    <option *ngFor="let e of emulators" [value]="e">{{e}}</option>
  </select>
  <button (click)="connect()">Connect</button>
</div>

<div class="textarea-container">
  <label for="asmInput">Assembly Code:</label>
  <textarea id="asmInput"
    [(ngModel)]="code"
    placeholder="add \$t0, \$s0, \$s1&#10;addi \$t1, \$t0, 100&#10;j 0x400000"
    rows="8"></textarea>

  <label for="hexOutput">Hex Output:</label>
  <textarea id="hexOutput" [value]="output" readonly
    placeholder="Assembled hex will appear here..."></textarea>

  <div *ngIf="statusText" class="status" [class.success]="statusSuccess" [class.error]="statusError">
    {{statusText}}
  </div>
</div>

<div class="row">
  <button (click)="assemble()">Assemble</button>
</div>
  `
})
export class AsmPanelComponent implements OnInit, OnDestroy {
  emulators: string[] = ['Project64', 'Mupen64Plus', 'RetroArch'];
  selectedEmulator = '';
  code = '';
  output = '';
  statusText = '';
  statusSuccess = false;
  statusError = false;
  private subs: Subscription[] = [];

  constructor(private bridge: VsCodeBridgeService) {}

  ngOnInit(): void {
    this.subs.push(
      this.bridge.on<Record<string, unknown>>('setCode').subscribe(msg => {
        this.code = (msg['code'] as string) ?? '';
      }),
      this.bridge.on<Record<string, unknown>>('assembleResult').subscribe(msg => {
        if (msg['success']) {
          this.output = (msg['data'] as string) ?? '';
          this.statusText = 'Assembly complete!';
          this.statusSuccess = true;
          this.statusError = false;
        } else {
          this.statusText = 'Error: ' + (msg['error'] as string ?? 'Unknown error');
          this.statusSuccess = false;
          this.statusError = true;
        }
      }),
      this.bridge.on<Record<string, unknown>>('init').subscribe(msg => {
        const emulators = msg['emulators'] as string[] | undefined;
        if (emulators && emulators.length > 0) {
          this.emulators = emulators;
        }
      })
    );
    // Signal readiness so providers can safely send init/state data.
    this.bridge.postMessage({ command: 'ready' });
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
  }

  onEmulatorChange(): void {
    this.bridge.postMessage({ command: 'emulatorChanged', emulator: this.selectedEmulator });
  }

  connect(): void {
    this.bridge.postMessage({ command: 'connect' });
  }

  assemble(): void {
    this.statusText = 'Assembling...';
    this.statusSuccess = false;
    this.statusError = false;
    this.bridge.postMessage({ command: 'assemble', data: this.code });
  }
}
