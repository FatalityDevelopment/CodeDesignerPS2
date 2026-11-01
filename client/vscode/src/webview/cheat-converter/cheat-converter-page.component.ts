import {
  Component,
  AfterViewInit,
  ViewEncapsulation,
  signal,
  computed,
  NgZone,
  inject,
} from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import { IconComponent } from '../shared/icon.component';
import { PageHeaderComponent } from '../shared/page-header.component';
import '../shared/styles/shared.css';
import './cheat-converter-page.css';

type Direction = 'toRaw' | 'toEngine';

/** CONVERTCHEAT format names (see SocketClient.convertCheat). */
const FORMATS: Array<{ id: string; label: string; detail: string }> = [
  { id: 'Raw',             label: 'Raw',                       detail: 'Unencrypted GameShark / Action Replay' },
  { id: 'ActionReplay1',   label: 'Action Replay 1',           detail: 'Fixed-seed XOR' },
  { id: 'ActionReplay2',   label: 'Action Replay 2 / GameShark 2', detail: 'DEADFACE XOR' },
  { id: 'ActionReplayMax', label: 'Action Replay MAX',         detail: 'DES-like, alphanumeric' },
  { id: 'GameSharkV3',     label: 'GameShark 3 / Xploder 4 / Xterminator', detail: 'Mersenne Twister' },
  { id: 'CodeBreakerV1',   label: 'CodeBreaker 1–6',           detail: 'Seed-table XOR' },
  { id: 'CodeBreakerV7',   label: 'CodeBreaker 7+',            detail: 'ARCFOUR + modular inverse' },
];

/** Empty format = let the bridge detect the input format when decrypting. */
const AUTO = '';
const CODE_LINE = /^\s*[0-9a-z]{8}\s+[0-9a-z]{8}/i;

interface SavedState { direction?: Direction; decryptFormat?: string; encryptFormat?: string; input?: string }

@Component({
  selector: 'app-cheat-converter-page',
  standalone: true,
  imports: [IconComponent, PageHeaderComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="cc-outer">
  <app-page-header icon="repeat" title="Cheat Converter" subtitle="Decrypt device codes to raw, or encrypt raw codes for a cheat device" />

  <div class="cc-bar">
    <div class="cc-seg">
      <button [class.cc-seg-on]="direction() === 'toRaw'" title="Decrypt device codes to raw" (click)="setDirection('toRaw')">Decrypt to raw</button>
      <button [class.cc-seg-on]="direction() === 'toEngine'" title="Encrypt raw codes for a cheat device" (click)="setDirection('toEngine')">Encrypt for device</button>
    </div>
    <label class="cc-format">
      <span class="cc-field-label">{{ direction() === 'toRaw' ? 'Input format' : 'Output format' }}</span>
      <select class="cc-select" (change)="setFormat(asSelect($event).value)">
        @if (direction() === 'toRaw') {
          <option [value]="AUTO" [selected]="format() === AUTO">Auto-detect</option>
        }
        @for (f of formats; track f.id) {
          <option [value]="f.id" [selected]="format() === f.id">{{ f.label }} — {{ f.detail }}</option>
        }
      </select>
    </label>
    <button class="cc-primary-btn" [disabled]="busy() || !input().trim()" title="Convert (Ctrl+Enter)" (click)="convert()">
      @if (busy()) { Converting… } @else { Convert }
    </button>
    @if (!connected()) {
      <span class="cc-conn-warn">Not connected to the bridge</span>
    }
  </div>

  @if (statusMsg()) {
    <div class="cc-status" [class.cc-status-error]="statusIsError()">{{ statusMsg() }}</div>
  }

  <div class="cc-body">
    <section class="cc-card">
      <div class="cc-card-head">
        <span class="cc-section-title">{{ direction() === 'toRaw' ? 'Device codes' : 'Raw codes' }}</span>
        <span class="cc-count">{{ countLines(input()) }}</span>
        <span class="cc-head-actions">
          <button class="lm-tool-btn" [disabled]="!input()" (click)="setInput('')">Clear</button>
        </span>
      </div>
      <textarea class="cc-textarea" spellcheck="false"
                [placeholder]="direction() === 'toRaw' ? 'Paste encrypted codes…' : 'Infinite Health&#10;20123456 000003E8'"
                [value]="input()"
                (input)="setInput(asTextarea($event).value)"
                (keydown.control.enter)="convert()"
                (keydown.meta.enter)="convert()"></textarea>
    </section>

    <button class="cc-swap" title="Use the output as the new input and flip the direction" [disabled]="!output()" (click)="swap()">&#x21C4;</button>

    <section class="cc-card">
      <div class="cc-card-head">
        <span class="cc-section-title">{{ direction() === 'toRaw' ? 'Raw codes' : formatLabel() }}</span>
        <span class="cc-count">{{ countLines(output()) }}</span>
        <span class="cc-head-actions">
          <button class="lm-tool-btn" [disabled]="!output()" (click)="copyOutput()">
            <app-icon name="copy" [size]="12" /> Copy
          </button>
          <button class="lm-tool-btn" [disabled]="!output()" title="Open the output in a new editor tab" (click)="post({ type: 'openInEditor', text: output() })">
            <app-icon name="filecode" [size]="12" /> Open
          </button>
        </span>
      </div>
      <textarea class="cc-textarea cc-output" spellcheck="false" readonly
                placeholder="Converted codes appear here."
                [value]="output()"></textarea>
    </section>
  </div>
</div>
  `,
})
export class CheatConverterPageComponent implements AfterViewInit {
  private readonly vscode = getVsCodeApi();
  private readonly zone = inject(NgZone);

  readonly AUTO = AUTO;
  readonly formats = FORMATS;

  readonly connected = signal(true);
  readonly direction = signal<Direction>('toRaw');
  readonly decryptFormat = signal(AUTO);
  readonly encryptFormat = signal('ActionReplay2');
  readonly input = signal('');
  readonly output = signal('');
  readonly busy = signal(false);
  readonly statusMsg = signal('');
  readonly statusIsError = signal(false);

  readonly format = computed(() => this.direction() === 'toRaw' ? this.decryptFormat() : this.encryptFormat());
  readonly formatLabel = computed(() => FORMATS.find(f => f.id === this.encryptFormat())?.label ?? 'Device codes');

  constructor() {
    const st = (this.vscode.getState() ?? {}) as SavedState;
    if (st.direction) { this.direction.set(st.direction); }
    if (st.decryptFormat !== undefined) { this.decryptFormat.set(st.decryptFormat); }
    if (st.encryptFormat) { this.encryptFormat.set(st.encryptFormat); }
    if (st.input) { this.input.set(st.input); }
  }

  ngAfterViewInit(): void {
    window.addEventListener('message', (event: MessageEvent) => {
      const data = event.data as { type: string; [key: string]: unknown };
      this.zone.run(() => this._handleMessage(data));
    });
    this.post({ type: 'ready' });
  }

  // ── Actions ──────────────────────────────────────────────────────────────

  post(msg: Record<string, unknown>): void { this.vscode.postMessage(msg); }

  setDirection(d: Direction): void {
    this.direction.set(d);
    this.output.set('');
    this._status('', false);
    this.saveState();
  }

  setFormat(id: string): void {
    if (this.direction() === 'toRaw') { this.decryptFormat.set(id); } else { this.encryptFormat.set(id); }
    this.saveState();
  }

  setInput(text: string): void {
    this.input.set(text);
    this.saveState();
  }

  convert(): void {
    const codes = this.input().trim();
    if (!codes || this.busy()) { return; }
    this.busy.set(true);
    this._status('', false);
    this.post({ type: 'convert', codes, direction: this.direction(), format: this.format() });
  }

  /** Feed the output back in and flip the direction (e.g. decrypt, then re-encrypt for another device). */
  swap(): void {
    const out = this.output();
    if (!out) { return; }
    this.input.set(out);
    this.output.set('');
    this.direction.set(this.direction() === 'toRaw' ? 'toEngine' : 'toRaw');
    this._status('', false);
    this.saveState();
  }

  copyOutput(): void {
    this.post({ type: 'copy', text: this.output(), summary: `Copied ${this.countLines(this.output())}.` });
  }

  // ── Template helpers ─────────────────────────────────────────────────────

  asSelect(e: Event):   HTMLSelectElement   { return e.target as HTMLSelectElement; }
  asTextarea(e: Event): HTMLTextAreaElement { return e.target as HTMLTextAreaElement; }

  countLines(text: string): string {
    const n = text.split(/\r?\n/).filter(l => CODE_LINE.test(l)).length;
    return `${n} code line${n === 1 ? '' : 's'}`;
  }

  saveState(): void {
    this.vscode.setState({
      direction: this.direction(), decryptFormat: this.decryptFormat(),
      encryptFormat: this.encryptFormat(), input: this.input(),
    } satisfies SavedState);
  }

  // ── Internals ────────────────────────────────────────────────────────────

  private _status(message: string, isError: boolean): void {
    this.statusMsg.set(message);
    this.statusIsError.set(isError);
  }

  private _handleMessage(data: { type: string; [key: string]: unknown }): void {
    switch (data['type']) {
      case 'context':
        this.connected.set(!!data['connected']);
        break;

      case 'convertResult':
        this.busy.set(false);
        if (data['error']) {
          this._status(String(data['error']), true);
        } else {
          this.output.set((data['result'] as string) ?? '');
          this._status(`Converted ${this.countLines(this.output())}.`, false);
        }
        break;

      case 'status':
        this._status(data['message'] as string ?? '', !!data['isError']);
        break;
    }
  }
}
