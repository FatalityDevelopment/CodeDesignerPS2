import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  input,
  output,
  computed,
  signal,
  effect,
} from '@angular/core';
import { IconComponent } from '../../shared/icon.component';

/**
 * PanelStatusBarComponent - Displays emulator connection/pause status and
 * provides Pause/Resume and Step buttons. Also provides a mode toggle
 * to switch between "Live" and "AI Control" modes.
 *
 * Accepts `isConnected` and `isPaused` signal inputs and emits `togglePause`
 * and `step` outputs when the corresponding buttons are clicked.
 */
@Component({
  selector: 'app-panel-status-bar',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div id="emu-status-bar"
         [class.status-connected]="isConnected() && !isPaused() && controlMode() === 'live'"
         [class.status-disconnected]="!isConnected() && !isPaused()"
         [class.status-paused]="isPaused()"
         [class.status-ai-control]="isConnected() && !isPaused() && controlMode() === 'ai-control'">

      <!-- Row 1: brand icon + status indicator + mode toggle -->
      <div id="emu-status-row">
        @if (iconUri()) {
          <img id="emu-brand-icon" [src]="iconUri()" alt="CodeDesigner" />
        }
        <span id="emu-status-dot">
          @if (controlMode() === 'ai-control' && isConnected() && !isPaused()) {
            <app-icon name="bot" [size]="15" />
          } @else {
            ●
          }
        </span>
        <span id="emu-status-text">{{statusText()}}</span>
        @if (isConnected()) {
          <span id="emu-mode-toggle">
            <button class="mode-pill"
                    [class.mode-pill-active]="controlMode() === 'live'"
                    (click)="setControlMode('live')"
                    title="Live mode: monitor emulator in real time">Live</button>
            <button class="mode-pill"
                    [class.mode-pill-active]="controlMode() === 'ai-control'"
                    (click)="setControlMode('ai-control')"
                    title="AI Control mode: MCP agent drives the emulator">AI</button>
          </span>
        }
      </div>

      <!-- Row 2: debug controls (only when connected) -->
      @if (isConnected()) {
        <div id="emu-debug-row">
          <button class="dbg-btn" id="emu-play-pause-btn"
                  (click)="togglePause.emit()"
                  title="{{isPaused() ? 'Resume emulator' : 'Pause emulator'}}">
            <app-icon [name]="isPaused() ? 'play' : 'pause'" [size]="12" />
            <span class="dbg-btn-label">{{isPaused() ? 'Resume' : 'Pause'}}</span>
          </button>
          <button class="dbg-btn" id="emu-step-btn"
                  (click)="step.emit()" [disabled]="!isPaused()"
                  title="Step — execute one EE instruction">
            <app-icon name="play" [size]="11" /><span class="dbg-btn-label">Step</span>
          </button>
          <button class="dbg-btn" id="emu-step-into-btn"
                  (click)="stepInto.emit()" [disabled]="!isPaused()"
                  title="Step Into — follow calls (PCSX2 PINE)">
            <span class="dbg-glyph">↓</span><span class="dbg-btn-label">Into</span>
          </button>
          <button class="dbg-btn" id="emu-step-over-btn"
                  (click)="stepOver.emit()" [disabled]="!isPaused()"
                  title="Step Over — skip calls (PCSX2 PINE)">
            <span class="dbg-glyph">↷</span><span class="dbg-btn-label">Over</span>
          </button>
        </div>
      }
    </div>
  `,
})
export class PanelStatusBarComponent {
  readonly isConnected = input(false);
  readonly isPaused = input(false);
  /** External follow-AI enabled state; syncs the local controlMode signal. */
  readonly followAiEnabled = input(false);
  /** Webview URI of the CodeDesigner emblem shown at the start of the top bar. */
  readonly iconUri = input('');

  readonly togglePause = output<void>();
  readonly step = output<void>();
  readonly stepInto = output<void>();
  readonly stepOver = output<void>();
  /** Emitted when the user clicks Live or AI mode buttons. */
  readonly controlModeChange = output<'live' | 'ai-control'>();

  readonly controlMode = signal<'live' | 'ai-control'>('live');

  constructor() {
    // Keep local controlMode in sync with the external followAiEnabled input.
    effect(() => {
      this.controlMode.set(this.followAiEnabled() ? 'ai-control' : 'live');
    });
  }

  setControlMode(mode: 'live' | 'ai-control'): void {
    this.controlMode.set(mode);
    this.controlModeChange.emit(mode);
  }

  readonly statusText = computed(() => {
    if (this.isPaused()) { return '⏸ PAUSED'; }
    if (this.isConnected()) {
      return this.controlMode() === 'ai-control' ? 'AI CONTROL' : 'LIVE';
    }
    return 'STOPPED';
  });

}
