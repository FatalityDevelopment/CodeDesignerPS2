import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  signal,
  computed,
  inject,
  input,
  OnDestroy,
} from '@angular/core';
import { Subscription } from 'rxjs';
import { VsCodeBridgeService } from '../../shared/vscode-bridge.service';

interface Ps2Button {
  name: string;
  value: number;
  checked: boolean;
}

const INITIAL_BUTTONS: Ps2Button[] = [
  { name: 'Up Arrow',    value: 0x0010, checked: false },
  { name: 'Left Arrow',  value: 0x0080, checked: false },
  { name: 'Down Arrow',  value: 0x0040, checked: false },
  { name: 'Right Arrow', value: 0x0020, checked: false },
  { name: 'Triangle',    value: 0x1000, checked: false },
  { name: 'Circle',      value: 0x2000, checked: false },
  { name: 'Cross',       value: 0x4000, checked: false },
  { name: 'Square',      value: 0x8000, checked: false },
  { name: 'R1',          value: 0x0800, checked: false },
  { name: 'R2',          value: 0x0200, checked: false },
  { name: 'L1',          value: 0x0400, checked: false },
  { name: 'L2',          value: 0x0100, checked: false },
  { name: 'R3',          value: 0x0004, checked: false },
  { name: 'L3',          value: 0x0002, checked: false },
  { name: 'Start',       value: 0x0008, checked: false },
  { name: 'Select',      value: 0x0001, checked: false },
];

/**
 * JokerMakerOverlayComponent — builds a Joker/D-pad cheat code button mask.
 *
 * Displays a modal with checkboxes for each PS2 button. As the user selects
 * buttons the output field updates to show the 8-digit GameShark data value
 * (`0000XXXX`) that encodes the selected button combination.
 *
 * Computation mirrors the classic VB tool:
 *   H = 0xFFFF
 *   For each checked button: H -= button.value
 *   Output: "0000" + H.toString(16).padStart(4, "0").toUpperCase()
 */
@Component({
  selector: 'app-joker-maker-overlay',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
<div id="joker-overlay"
     [class.visible]="visible()"
     (click)="onBackdropClick($event)"
     (keydown.escape)="close()">
  <div id="joker-overlay-box">

    <div id="joker-overlay-header">
      <span id="joker-overlay-title">🕹️ Joker Maker</span>
      <button id="joker-overlay-close" title="Close (Escape)" (click)="close()">&#xD7;</button>
    </div>

    <div id="joker-overlay-body">

      <p class="joker-hint">
        Select the buttons that must be held. The code updates automatically.
      </p>

      <div class="joker-button-grid">
        @for (btn of buttons(); track btn.name; let i = $index) {
          <label class="joker-btn-label" [class.joker-btn-checked]="btn.checked">
            <input type="checkbox"
                   [checked]="btn.checked"
                   (change)="toggle(i, $any($event.target).checked)" />
            <span>{{ btn.name }}</span>
            @if (isConnected()) {
              <span class="joker-pad-dot" [class.joker-pad-dot-active]="isBtnPressed(btn.value)" title="Live pad status"></span>
            }
          </label>
        }
      </div>

      <div class="joker-config-row">
        <span class="joker-config-label">Active:</span>
        <span class="joker-config-value">{{ activeConfig() }}</span>
      </div>

      @if (isConnected()) {
        <div class="joker-config-row joker-live-row">
          <span class="joker-config-label">
            <span class="joker-live-indicator" [class.joker-live-active]="isPolling()"></span>
            Live:
          </span>
          <span class="joker-config-value">{{ liveConfig() }}</span>
        </div>
      }

      <div class="joker-output-row">
        <span class="joker-output-label">Code Data:</span>
        <code class="joker-output-code">{{ outputCode() }}</code>
        <button class="joker-copy-btn"
                title="Copy code to clipboard"
                (click)="copyCode()">
          {{ copyLabel() }}
        </button>
      </div>

      <div class="joker-actions-row">
        <button class="joker-clear-btn" (click)="clearAll()">Clear All</button>
      </div>

    </div>
  </div>
</div>
  `,
})
export class JokerMakerOverlayComponent implements OnDestroy {

  private readonly bridge = inject(VsCodeBridgeService);
  private readonly subs: Subscription[] = [];

  readonly visible = signal(false);

  /** Whether the emulator is connected (enables live pad status). */
  readonly isConnected = input(false);

  /** Mutable button array kept as a signal so the template reacts to changes. */
  readonly buttons = signal<Ps2Button[]>(INITIAL_BUTTONS.map(b => ({ ...b })));

  /** Raw pad button bitmask from the emulator (0xFFFF = nothing pressed, clear bit = pressed). */
  readonly livePadButtons = signal(0xFFFF);

  /** Whether the pad polling loop is active. */
  readonly isPolling = signal(false);

  /** Computed 8-digit GameShark data value. */
  readonly outputCode = computed(() => {
    let h = 0xFFFF;
    for (const btn of this.buttons()) {
      if (btn.checked) { h -= btn.value; }
    }
    return '0000' + h.toString(16).padStart(4, '0').toUpperCase();
  });

  /** Human-readable list of selected button names. */
  readonly activeConfig = computed(() => {
    const names = this.buttons()
      .filter(b => b.checked)
      .map(b => b.name);
    return names.length ? names.join(', ') : '(None)';
  });

  /** Human-readable list of currently pressed button names from the live pad. */
  readonly liveConfig = computed(() => {
    const mask = this.livePadButtons();
    const names = INITIAL_BUTTONS
      .filter(b => (mask & b.value) === 0)
      .map(b => b.name);
    return names.length ? names.join(', ') : '(None)';
  });

  readonly copyLabel = signal('Copy');

  constructor() {
    this.subs.push(
      this.bridge.on<{ command: 'jokerPadState'; buttons: number }>('jokerPadState').subscribe(msg => {
        this.livePadButtons.set(msg.buttons);
        this.isPolling.set(true);
      }),
    );
  }

  ngOnDestroy(): void {
    this.stopPolling();
    this.subs.forEach(s => s.unsubscribe());
  }

  /** Returns true when the given button value is currently pressed on the live pad. */
  isBtnPressed(value: number): boolean {
    return (this.livePadButtons() & value) === 0;
  }

  // ── Public API ─────────────────────────────────────────────────────────────

  open(): void {
    this.visible.set(true);
    this.startPolling();
  }

  close(): void {
    this.visible.set(false);
    this.stopPolling();
  }

  // ── Event Handlers ─────────────────────────────────────────────────────────

  toggle(index: number, checked: boolean): void {
    this.buttons.update(btns =>
      btns.map((b, i) => i === index ? { ...b, checked } : b)
    );
  }

  clearAll(): void {
    this.buttons.update(btns => btns.map(b => ({ ...b, checked: false })));
  }

  copyCode(): void {
    navigator.clipboard.writeText(this.outputCode()).then(() => {
      this.copyLabel.set('Copied!');
      setTimeout(() => this.copyLabel.set('Copy'), 2000);
    }).catch(() => {
      this.copyLabel.set('Copy');
    });
  }

  onBackdropClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).id === 'joker-overlay') {
      this.close();
    }
  }

  // ── Private helpers ────────────────────────────────────────────────────────

  private startPolling(): void {
    if (this.isConnected()) {
      this.bridge.postMessage({ command: 'startJokerPadPoll' });
    }
  }

  private stopPolling(): void {
    const wasPolling = this.isPolling();
    this.isPolling.set(false);
    this.livePadButtons.set(0xFFFF);
    if (wasPolling) {
      this.bridge.postMessage({ command: 'stopJokerPadPoll' });
    }
  }
}
