import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  input,
  output,
  signal,
  model,
  OnDestroy,
  inject,
} from '@angular/core';
import { Subscription } from 'rxjs';
import { VsCodeBridgeService } from '../../shared/vscode-bridge.service';
import { IconComponent } from '../../shared/icon.component';
import { ArchiveCheat } from '../types';

interface CheatsAppliedMsg {
  command: 'cheatsApplied';
  success: boolean;
  error?: string;
}

interface ArchiveCheatsLoadedMsg {
  command: 'archiveCheatsLoaded';
  cheats: ArchiveCheat[];
  game: string;
  serial: string;
  cheatType?: string;
}

interface ArchiveCheatsUpdatedMsg {
  command: 'archiveCheatsUpdated';
  cheats: ArchiveCheat[];
}

/**
 * CheatsTabComponent - GameShark/Action Replay cheat code editor tab.
 *
 * Shows two sections:
 *  1. Archive section (PCSX2 mode only) — cheats fetched from CheatCodeArchive,
 *     each with a toggle to enable/disable and apply to emulator memory.
 *  2. Manual section — a raw textarea for pasting/editing GameShark codes and an
 *     Update button, plus cross-tab injection from the CDS "Add Cheat" action.
 */
@Component({
  selector: 'app-cheats-tab',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="cheats-container">

      <!-- ── Cheat tools ──────────────────────────────────────────────────── -->
      <div class="cheats-tools">
        <div class="t-head">
          <app-icon name="sparkles" [size]="14" />
          <span class="t-head-label">Cheat Tools</span>
        </div>
        <div class="btn-grid">
          <button class="cheats-tool-btn" (click)="openConverterClick.emit()"
                  title="Open the Cheat Code Converter to encrypt/decrypt cheat codes">
            <app-icon name="repeat" [size]="13" /><span>Converter</span>
          </button>
          <button class="cheats-tool-btn" (click)="openPorterClick.emit()"
                  title="Open the Cheat Code Porter to port cheats between game revisions">
            <app-icon name="gitcompare" [size]="13" /><span>Porter</span>
          </button>
          <button class="cheats-tool-btn" (click)="openScannerClick.emit()"
                  title="Open the Memory Scanner to search for values in memory">
            <app-icon name="search" [size]="13" /><span>Scanner</span>
          </button>
          <button class="cheats-tool-btn" id="jokerMakerBtn" (click)="openJokerMaker()"
                  title="Build a Joker/button-combo code">
            <app-icon name="gamepad2" [size]="13" /><span>Joker</span>
          </button>
          <button class="cheats-tool-btn" (click)="onOpenHelp()" title="Open the Cheat Engine guide">
            <app-icon name="bookopen" [size]="13" /><span>Help</span>
          </button>
        </div>
      </div>

      <!-- ── Archive cheats section (PCSX2 mode) ─────────────────────────── -->
      @if (isPcsx2()) {
        <div class="archive-section">
          <div class="archive-header">
            <span class="archive-title">
              @if (archiveGame()) {
                {{ archiveGame() }}
              } @else {
                Archive Cheats
              }
            </span>
            <button class="archive-fetch-btn ibtn"
                    (click)="onFetchArchive()"
                    [disabled]="!isConnected() || archiveLoading()">
              <app-icon name="refreshcw" [size]="12" />
              <span>{{ archiveLoading() ? 'Loading…' : 'Refresh' }}</span>
            </button>
          </div>

          @if (archiveError()) {
            <div class="cheat-message cheat-error">{{ archiveError() }}</div>
          }

          @if (archiveCheats().length === 0 && !archiveLoading() && !archiveError()) {
            <div class="archive-empty">No archive cheats found for this game.</div>
          }

          @if (archiveCheats().length > 0) {
            <div class="archive-apply-row">
              <button class="ibtn" (click)="onApplyAll()"
                      [disabled]="!isConnected() || !hasEnabledArchiveCheats()">
                <app-icon name="zap" [size]="13" /><span>Apply All Enabled</span>
              </button>
            </div>
            <ul class="archive-list">
              @for (cheat of archiveCheats(); track cheat.name; let i = $index) {
                <li class="archive-item" [class.archive-item-enabled]="cheat.enabled">
                  <label class="archive-toggle">
                    <input type="checkbox"
                           [checked]="cheat.enabled"
                           [disabled]="!isConnected()"
                           (change)="onToggleCheat(i, $any($event.target).checked)" />
                    <span class="archive-cheat-name">{{ cheat.name }}</span>
                  </label>
                  @if (cheat.notes) {
                    <span class="archive-cheat-notes">{{ cheat.notes }}</span>
                  }
                </li>
              }
            </ul>
          }
        </div>
        <div class="archive-divider"></div>
      }

      <!-- ── Manual cheat textarea ──────────────────────────────────────── -->
      <textarea id="cheats-input"
        [value]="cheatText()"
        (input)="cheatText.set($any($event.target).value)"
        placeholder="Paste cheat codes here (GameShark/Action Replay format)&#10;&#10;Example:&#10;#Infinite Health&#10;20123456 000003E8"></textarea>
      <div class="row">
        <button class="ibtn" id="cheatsUpdateBtn" (click)="onApplyCheats()" [disabled]="!isConnected()">
          <app-icon name="zap" [size]="13" /><span>Update</span>
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
  /** True when the sidebar is connected in PCSX2 PINE bridge mode. */
  readonly isPcsx2 = input(false);

  // ── Tool outputs (relayed to panel parent → panel provider) ────────────────
  readonly openConverterClick = output<void>();
  readonly openPorterClick    = output<void>();
  readonly openScannerClick   = output<void>();

  /** Two-way bound cheat text — parent can push text in (e.g. from CDS tab). */
  readonly cheatText = model('');

  readonly cheatMessage = signal('');
  readonly cheatError   = signal(false);

  // ── Archive state ──────────────────────────────────────────────────────────
  readonly archiveCheats  = signal<ArchiveCheat[]>([]);
  readonly archiveGame    = signal('');
  readonly archiveLoading = signal(false);
  readonly archiveError   = signal('');

  constructor() {
    this.subs.push(
      this.bridge.on<CheatsAppliedMsg>('cheatsApplied').subscribe(msg => {
        this.cheatError.set(!msg.success);
        this.cheatMessage.set(msg.success ? 'Cheats applied!' : (msg.error ?? 'Failed to apply cheats.'));
        setTimeout(() => this.cheatMessage.set(''), 4000);
      }),
      this.bridge.on<{ command: 'archiveCheatsLoading' }>('archiveCheatsLoading').subscribe(() => {
        this.archiveLoading.set(true);
        this.archiveError.set('');
      }),
      this.bridge.on<ArchiveCheatsLoadedMsg>('archiveCheatsLoaded').subscribe(msg => {
        this.archiveLoading.set(false);
        this.archiveError.set('');
        this.archiveCheats.set(msg.cheats ?? []);
        this.archiveGame.set(msg.game ?? '');
      }),
      this.bridge.on<{ command: 'archiveCheatsError'; error: string }>('archiveCheatsError').subscribe(msg => {
        this.archiveLoading.set(false);
        this.archiveError.set(msg.error ?? 'Failed to load archive cheats.');
      }),
      this.bridge.on<ArchiveCheatsUpdatedMsg>('archiveCheatsUpdated').subscribe(msg => {
        this.archiveCheats.set(msg.cheats ?? []);
      }),
    );
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
  }

  hasEnabledArchiveCheats(): boolean {
    return this.archiveCheats().some(c => c.enabled);
  }

  onFetchArchive(): void {
    this.bridge.postMessage({ command: 'fetchArchiveCheats' });
  }

  onToggleCheat(index: number, enabled: boolean): void {
    // Optimistically update local state so the checkbox feels instant.
    const updated = this.archiveCheats().map((c, i) =>
      i === index ? { ...c, enabled } : c
    );
    this.archiveCheats.set(updated);
    this.bridge.postMessage({ command: 'toggleArchiveCheat', index, enabled });
  }

  onApplyAll(): void {
    this.bridge.postMessage({ command: 'applyAllArchiveCheats' });
  }

  onApplyCheats(): void {
    this.bridge.postMessage({ command: 'applyCheats', cheatText: this.cheatText() });
  }

  openJokerMaker(): void {
    this.bridge.postMessage({ command: 'openJokerMaker' });
  }

  onOpenHelp(): void {
    this.bridge.postMessage({ command: 'openMarkdownHelp', file: 'CHEAT_ENGINE.md' });
  }
}
