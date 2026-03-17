import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  input,
  signal,
  model,
  OnDestroy,
  inject,
} from '@angular/core';
import { Subscription } from 'rxjs';
import { VsCodeBridgeService } from '../../shared/vscode-bridge.service';

interface CheatsAppliedMsg {
  command: 'cheatsApplied';
  success: boolean;
  error?: string;
}

/**
 * CheatsTabComponent - GameShark/Action Replay cheat code editor tab.
 *
 * Owns the cheats textarea via a two-way `cheatText` model input so the parent
 * can inject text from the CDS "Add Cheat" action while the user can also edit
 * freely. Subscribes directly to the `cheatsApplied` bridge message to display
 * inline success/error feedback. Posts `applyCheats` to the extension host when
 * the "Update" button is clicked.
 */
@Component({
  selector: 'app-cheats-tab',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="cheats-container">
      <textarea id="cheats-input"
        [value]="cheatText()"
        (input)="cheatText.set($any($event.target).value)"
        placeholder="Paste cheat codes here (GameShark/Action Replay format)&#10;&#10;Example:&#10;#Infinite Health&#10;20123456 000003E8"></textarea>
      <div class="row">
        <button id="cheatsUpdateBtn" (click)="onApplyCheats()" [disabled]="!isConnected()">
          Update
        </button>
      </div>
      @if (cheatMessage()) {
        <div class="cheat-message" [class.cheat-error]="cheatError()">{{cheatMessage()}}</div>
      }
    </div>
  `,
})
export class CheatsTabComponent implements OnDestroy {
  private readonly bridge = inject(VsCodeBridgeService);
  private readonly subs: Subscription[] = [];

  readonly isConnected = input(false);

  /** Two-way bound cheat text — parent can push text in (e.g. from CDS tab). */
  readonly cheatText = model('');

  readonly cheatMessage = signal('');
  readonly cheatError = signal(false);

  constructor() {
    this.subs.push(
      this.bridge.on<CheatsAppliedMsg>('cheatsApplied').subscribe(msg => {
        this.cheatError.set(!msg.success);
        this.cheatMessage.set(msg.success ? 'Cheats applied!' : (msg.error ?? 'Failed to apply cheats.'));
        setTimeout(() => this.cheatMessage.set(''), 4000);
      })
    );
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
  }

  onApplyCheats(): void {
    this.bridge.postMessage({ command: 'applyCheats', cheatText: this.cheatText() });
  }
}
