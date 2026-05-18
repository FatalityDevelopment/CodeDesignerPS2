import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  input,
  output,
  signal,
  OnDestroy,
  inject,
} from '@angular/core';
import { Subscription } from 'rxjs';
import { VsCodeBridgeService } from '../../shared/vscode-bridge.service';
import { IconComponent } from '../../shared/icon.component';

interface CdsCompileResultMsg {
  command: 'cdsCompileResult';
  success: boolean;
  codes?: string;
}

/**
 * CdsTabComponent - Code Designer Source (CDS) compiler output tab.
 *
 * Manages its own `cdsOutput` signal, subscribing to `cdsCompileResult` bridge
 * messages to populate the read-only textarea. Posts `compileCds` and `addPatch`
 * commands directly to the extension host. Emits `addCheatClick` with the
 * compiled codes so the parent can inject them into the Cheats tab textarea.
 */
@Component({
  selector: 'app-cds-tab',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="cds-container">
      <div class="t-head">
        <app-icon name="filecode" [size]="14" />
        <span class="t-head-label">Compiled GameShark Codes</span>
      </div>
      <textarea id="cds-output" readonly
        [value]="cdsOutput()"
        placeholder="Compiled GameShark codes will appear here after pressing Compile.&#10;&#10;Add an address directive in your .cds file:&#10;  address 0x20123456&#10;  nop&#10;  add $t0, $s0, $s1"></textarea>
      <div class="row">
        <button class="ibtn" id="cdsCompileBtn" (click)="onCompileCds()" [disabled]="!isConnected()">
          <app-icon name="zap" [size]="13" /><span>Compile</span>
        </button>
        <button class="ibtn" id="cdsAddPatchBtn" (click)="onAddPatch()"
                [disabled]="!isConnected() || !cdsOutput()">
          <app-icon name="plugzap" [size]="13" /><span>Add Patch</span>
        </button>
      </div>
      <div class="row">
        <button class="ibtn" id="cdsAddCheatBtn" (click)="onAddCheat()" [disabled]="!cdsOutput()">
          <app-icon name="sparkles" [size]="13" /><span>Add Cheat</span>
        </button>
      </div>
    </div>
  `,
})
export class CdsTabComponent implements OnDestroy {
  private readonly bridge = inject(VsCodeBridgeService);
  private readonly subs: Subscription[] = [];

  readonly isConnected = input(false);

  /** Emits compiled GameShark codes when "Add Cheat" is clicked. */
  readonly addCheatClick = output<string>();

  readonly cdsOutput = signal('');

  constructor() {
    this.subs.push(
      this.bridge.on<CdsCompileResultMsg>('cdsCompileResult').subscribe(msg => {
        this.cdsOutput.set(msg.success && msg.codes ? msg.codes : '');
      }),
      this.bridge.on<Record<string, unknown>>('cdsPatchApplied').subscribe(() => {
        // Patch results are logged to the Debug output channel server-side.
      })
    );
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
  }

  onCompileCds(): void {
    this.bridge.postMessage({ command: 'compileCds' });
  }

  onAddPatch(): void {
    if (!this.cdsOutput()) { return; }
    this.bridge.postMessage({ command: 'addPatch', codes: this.cdsOutput() });
  }

  onAddCheat(): void {
    if (!this.cdsOutput()) { return; }
    this.addCheatClick.emit(this.cdsOutput());
  }
}
