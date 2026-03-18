import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  input,
  output,
  computed,
} from '@angular/core';

/**
 * PanelStatusBarComponent - Displays emulator connection/pause status and
 * provides Pause/Resume, Step, Step Into, and Step Over buttons.
 *
 * Accepts `isConnected` and `isPaused` signal inputs and emits the
 * corresponding outputs when buttons are clicked.
 */
@Component({
  selector: 'app-panel-status-bar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div id="emu-status-bar"
         [class.status-connected]="isConnected() && !isPaused()"
         [class.status-disconnected]="!isConnected() && !isPaused()"
         [class.status-paused]="isPaused()">
      <span id="emu-status-dot">●</span>
      <span id="emu-status-text">{{statusText()}}</span>
      <button id="emu-play-pause-btn" (click)="togglePause.emit()" title="Pause/Resume emulator">
        {{isPaused() ? '▶ Resume' : '⏸ Pause'}}
      </button>
      <button id="emu-step-btn" (click)="step.emit()" [disabled]="!isConnected() || !isPaused()"
              title="Single step one EE instruction (only while paused)">
        ▷| Step
      </button>
      <button id="emu-step-into-btn" (click)="stepInto.emit()" [disabled]="!isConnected() || !isPaused()"
              title="Step into: follow branches and calls (PINE mode, only while paused)">
        ↳ Into
      </button>
      <button id="emu-step-over-btn" (click)="stepOver.emit()" [disabled]="!isConnected() || !isPaused()"
              title="Step over: treat function calls as atomic (PINE mode, only while paused)">
        ↷ Over
      </button>
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

  readonly statusText = computed(() => {
    if (this.isPaused()) { return '⏸ PAUSED'; }
    return this.isConnected() ? 'LIVE' : 'STOPPED';
  });
}
