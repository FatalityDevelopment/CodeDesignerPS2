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
 * provides Pause/Resume and Step buttons. Also provides a mode selector
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
      <span id="emu-status-dot">{{controlMode() === 'ai-control' && isConnected() && !isPaused() ? '🤖' : '●'}}</span>
      <span id="emu-status-text">{{statusText()}}</span>
      <select id="emu-mode-select"
              [style.display]="isConnected() ? 'inline-block' : 'none'"
              (change)="onModeChange($event)"
              title="Control mode">
        <option value="live" [selected]="controlMode() === 'live'">Live</option>
        <option value="ai-control" [selected]="controlMode() === 'ai-control'">AI Control</option>
      </select>
       <button id="emu-play-pause-btn" (click)="togglePause.emit()" title="Pause/Resume emulator">
         {{isPaused() ? '▶ Resume' : '⏸ Pause'}}
       </button>
       <button id="emu-step-btn" (click)="step.emit()" [disabled]="!isConnected() || !isPaused()"
               title="Single step one EE instruction (only while paused)">
         ▷| Step
       </button>
       <button id="emu-step-into-btn" (click)="stepInto.emit()" [disabled]="!isConnected() || !isPaused()"
               title="Step into (PCSX2 PINE)">
         ▷▷ Step In
       </button>
       <button id="emu-step-over-btn" (click)="stepOver.emit()" [disabled]="!isConnected() || !isPaused()"
               title="Step over (PCSX2 PINE)">
         ▷▷| Step Over
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

  readonly controlMode = signal<'live' | 'ai-control'>('live');

  readonly statusText = computed(() => {
    if (this.isPaused()) { return '⏸ PAUSED'; }
    if (this.isConnected()) {
      return this.controlMode() === 'ai-control' ? 'AI CONTROL' : 'LIVE';
    }
    return 'STOPPED';
  });

  onModeChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    this.controlMode.set(select.value as 'live' | 'ai-control');
  }
}
