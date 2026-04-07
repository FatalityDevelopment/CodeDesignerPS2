import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  input,
  output,
  computed,
  signal,
} from '@angular/core';

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
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div id="emu-status-bar"
         [class.status-connected]="isConnected() && !isPaused() && controlMode() === 'live'"
         [class.status-disconnected]="!isConnected() && !isPaused()"
         [class.status-paused]="isPaused()"
         [class.status-ai-control]="isConnected() && !isPaused() && controlMode() === 'ai-control'">

      <!-- Row 1: status indicator + mode toggle -->
      <div id="emu-status-row">
        <span id="emu-status-dot">{{controlMode() === 'ai-control' && isConnected() && !isPaused() ? '🤖' : '●'}}</span>
        <span id="emu-status-text">{{statusText()}}</span>
        @if (isConnected()) {
          <span id="emu-mode-toggle">
            <button class="mode-pill"
                    [class.mode-pill-active]="controlMode() === 'live'"
                    (click)="controlMode.set('live')"
                    title="Live mode: monitor emulator in real time">Live</button>
            <button class="mode-pill"
                    [class.mode-pill-active]="controlMode() === 'ai-control'"
                    (click)="controlMode.set('ai-control')"
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
            {{isPaused() ? '▶' : '⏸'}}
            <span class="dbg-btn-label">{{isPaused() ? 'Resume' : 'Pause'}}</span>
          </button>
          <div class="dbg-separator"></div>
          <button class="dbg-btn" id="emu-step-btn"
                  (click)="step.emit()" [disabled]="!isPaused()"
                  title="Step — execute one EE instruction">
            ▷<span class="dbg-btn-label">Step</span>
          </button>
          <button class="dbg-btn" id="emu-step-into-btn"
                  (click)="stepInto.emit()" [disabled]="!isPaused()"
                  title="Step Into — follow calls (PCSX2 PINE)">
            ↓<span class="dbg-btn-label">Into</span>
          </button>
          <button class="dbg-btn" id="emu-step-over-btn"
                  (click)="stepOver.emit()" [disabled]="!isPaused()"
                  title="Step Over — skip calls (PCSX2 PINE)">
            ↷<span class="dbg-btn-label">Over</span>
          </button>
        </div>
      }
    </div>
  `,
})
export class PanelStatusBarComponent {
  readonly isConnected = input(false);
  readonly isPaused = input(false);

  readonly togglePause = output<void>();
  readonly step = output<void>();
  readonly stepInto = output<void>();
  readonly stepOver = output<void>();

  readonly controlMode = signal<'live' | 'ai-control'>('live');

  readonly statusText = computed(() => {
    if (this.isPaused()) { return '⏸ PAUSED'; }
    if (this.isConnected()) {
      return this.controlMode() === 'ai-control' ? 'AI CONTROL' : 'LIVE';
    }
    return 'STOPPED';
  });

}
