import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  ElementRef,
  signal,
  output,
  viewChild,
} from '@angular/core';

/** Cheat engine format options. */
export type CheatFormat =
  | 'Raw'
  | 'ActionReplay1'
  | 'ActionReplay2'
  | 'ActionReplayMax'
  | 'GameSharkV3'
  | 'CodeBreakerV1'
  | 'CodeBreakerV7';

export interface ConvertRequest {
  codes: string;
  direction: 'toRaw' | 'toEngine';
  format: string;
}

export interface ConvertResult {
  result?: string;
  error?: string;
}

@Component({
  selector: 'app-converter-overlay',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
<div id="converter-overlay"
     [class.visible]="visible()"
     (click)="onBackdropClick($event)"
     (keydown.escape)="close()">
  <div id="converter-overlay-box">
    <div id="converter-overlay-header">
      <span id="converter-overlay-title">&#8645; Cheat Code Converter</span>
      <button id="converter-overlay-close" title="Close (Escape)" (click)="close()">&#xD7;</button>
    </div>
    <div id="converter-overlay-body">
      <div class="converter-overlay-field">
        <label class="converter-overlay-label" for="converter-input">Input Codes</label>
        <textarea id="converter-input"
                  class="converter-overlay-textarea"
                  spellcheck="false"
                  placeholder="Paste cheat codes here…"
                  #inputArea
                  [value]="inputValue()"
                  (input)="inputValue.set(asTextarea($event).value)"></textarea>
      </div>
      <div id="converter-overlay-controls">
        <div class="converter-overlay-field converter-format-field">
          <label class="converter-overlay-label" for="converter-format-select">Cheat Engine &amp; Version</label>
          <select id="converter-format-select"
                  class="converter-overlay-select"
                  [value]="format()"
                  (change)="format.set(asSelect($event).value)">
            <option value="Raw">Raw (unencrypted GameShark / Action Replay)</option>
            <option value="ActionReplay1">Action Replay 1 (fixed-seed XOR)</option>
            <option value="ActionReplay2">Action Replay 2 / GameShark 2 (DEADFACE XOR)</option>
            <option value="ActionReplayMax">Action Replay MAX (DES-like, alphanumeric)</option>
            <option value="GameSharkV3">GameShark V3 / Xploder 4 / Xterminator (Mersenne Twister)</option>
            <option value="CodeBreakerV1">CodeBreaker V1–6 (seed-table XOR)</option>
            <option value="CodeBreakerV7">CodeBreaker V7+ (ARCFOUR + modular-inverse)</option>
          </select>
        </div>
        <div id="converter-overlay-buttons">
          <button id="converter-to-raw-btn"
                  title="Decrypt the input codes to the unencrypted raw format"
                  [disabled]="converting()"
                  (click)="convert('toRaw')">&#8595; Convert to Raw</button>
          <button id="converter-to-engine-btn"
                  title="Encrypt the raw input codes to the selected engine format"
                  [disabled]="converting()"
                  (click)="convert('toEngine')">&#8593; Convert to Engine</button>
        </div>
      </div>
      <div id="converter-overlay-status"
           class="converter-overlay-status"
           [class.converter-status-ok]="statusOk()"
           [class.converter-status-error]="statusError()">{{ statusMessage() }}</div>
      <div class="converter-overlay-field">
        <label class="converter-overlay-label" for="converter-output">Output Codes</label>
        <textarea id="converter-output"
                  class="converter-overlay-textarea"
                  spellcheck="false"
                  readonly
                  placeholder="Converted codes will appear here…"
                  [value]="outputValue()"></textarea>
      </div>
    </div>
  </div>
</div>
  `,
})
export class ConverterOverlayComponent {
  // ── Outputs ─────────────────────────────────────────────────────────────────

  readonly close$ = output<void>();
  readonly convert$ = output<ConvertRequest>();

  // ── State ───────────────────────────────────────────────────────────────────

  readonly visible = signal(false);
  readonly inputValue = signal('');
  readonly outputValue = signal('');
  readonly format = signal('Raw');
  readonly statusMessage = signal('');
  readonly statusOk = signal(false);
  readonly statusError = signal(false);
  readonly converting = signal(false);

  private readonly inputAreaRef = viewChild<ElementRef<HTMLTextAreaElement>>('inputArea');

  // ── Public API ──────────────────────────────────────────────────────────────

  open(): void {
    this.statusMessage.set('');
    this.statusOk.set(false);
    this.statusError.set(false);
    this.visible.set(true);
    requestAnimationFrame(() => {
      this.inputAreaRef()?.nativeElement.focus();
    });
  }

  close(): void {
    if (this.visible()) {
      this.visible.set(false);
      this.close$.emit();
    }
  }

  /** Called by parent when convertCheatResult arrives. */
  setResult(result: ConvertResult): void {
    this.converting.set(false);
    if (result.error) {
      this.statusMessage.set('Error: ' + result.error);
      this.statusOk.set(false);
      this.statusError.set(true);
    } else {
      this.outputValue.set(result.result ?? '');
      this.statusMessage.set('Conversion complete.');
      this.statusOk.set(true);
      this.statusError.set(false);
    }
  }

  // ── Event Handlers ──────────────────────────────────────────────────────────

  convert(direction: 'toRaw' | 'toEngine'): void {
    const codes = this.inputValue().trim();
    if (!codes) {
      this.statusMessage.set('Please enter cheat codes in the input box.');
      this.statusOk.set(false);
      this.statusError.set(true);
      return;
    }
    this.outputValue.set('');
    this.statusMessage.set('Converting…');
    this.statusOk.set(false);
    this.statusError.set(false);
    this.converting.set(true);
    this.convert$.emit({ codes, direction, format: this.format() });
  }

  onBackdropClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).id === 'converter-overlay') {
      this.close();
    }
  }

  asTextarea(event: Event): HTMLTextAreaElement {
    return event.target as HTMLTextAreaElement;
  }

  asSelect(event: Event): HTMLSelectElement {
    return event.target as HTMLSelectElement;
  }
}
