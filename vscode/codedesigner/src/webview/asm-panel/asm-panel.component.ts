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
<div class="file-row">
  <input id="fileInput" type="text" [(ngModel)]="currentFile" readonly
    placeholder="Open a .cds file to compile..." />
</div>

<div class="textarea-container">
  <textarea id="codeInput"
    [(ngModel)]="code"
    placeholder="Open a .cds file — its contents will appear here for compilation."
    rows="12"></textarea>

  <div *ngIf="statusText" class="status" [class.success]="statusSuccess" [class.error]="statusError">
    {{statusText}}
  </div>

  <label *ngIf="output" for="hexOutput">Output:</label>
  <textarea *ngIf="output" id="hexOutput" [value]="output" readonly></textarea>
</div>

<div class="row">
  <button (click)="compile()" [disabled]="!code">Compile</button>
</div>
  `
})
export class AsmPanelComponent implements OnInit, OnDestroy {
  currentFile = '';
  code = '';
  output = '';
  statusText = '';
  statusSuccess = false;
  statusError = false;
  private subs: Subscription[] = [];

  constructor(private bridge: VsCodeBridgeService) {}

  ngOnInit(): void {
    this.subs.push(
      this.bridge.on<Record<string, unknown>>('loadFile').subscribe(msg => {
        this.currentFile = (msg['filePath'] as string) ?? '';
        this.code        = (msg['content']  as string) ?? '';
        this.output      = '';
        this.statusText  = '';
      }),
      this.bridge.on<Record<string, unknown>>('compileResult').subscribe(msg => {
        if (msg['success']) {
          this.output      = (msg['data']  as string) ?? '';
          this.statusText  = 'Compilation complete!';
          this.statusSuccess = true;
          this.statusError   = false;
        } else {
          this.output      = '';
          this.statusText  = 'Error: ' + ((msg['error'] as string) ?? 'Unknown error');
          this.statusSuccess = false;
          this.statusError   = true;
        }
      })
    );
    this.bridge.postMessage({ command: 'ready' });
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
  }

  compile(): void {
    if (!this.code) { return; }
    this.statusText    = 'Compiling…';
    this.statusSuccess = false;
    this.statusError   = false;
    this.bridge.postMessage({ command: 'compile', code: this.code });
  }
}
