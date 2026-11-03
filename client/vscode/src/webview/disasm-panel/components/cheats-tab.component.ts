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

/** One of the game's cheat lists in the cheat archive (GameCheatListInfo in socketClient.ts). */
interface ArchiveCheatList {
  id: number;
  title: string;
  cheatCount: number;
  forDisc: boolean;
}

interface ArchiveCheatsLoadedMsg {
  command: 'archiveCheatsLoaded';
  cheats: ArchiveCheat[];
  game: string;
  serial: string;
  cheatType?: string;
  lists?: ArchiveCheatList[];
  cheatListId?: number | null;
}

interface ArchiveCheatsUpdatedMsg {
  command: 'archiveCheatsUpdated';
  cheats: ArchiveCheat[];
}

/** Result of cheatsCompile / cheatsSend / cheatsSaveBin / cheatsAddToList (DisasmPanelProvider.handleCheatsAction). */
interface CheatsResultMsg {
  command: 'cheatsResult';
  action: string;
  success: boolean;
  /** 'compiled' carries codes/errors; 'done' carries the final message. */
  phase: 'compiled' | 'done';
  codes?: string;
  errors?: string[];
  message?: string;
}

interface CdsCompileResultMsg {
  command: 'cdsCompileResult';
  success: boolean;
  codes?: string;
  errors?: Array<{ lineNumber: number; message: string }>;
}

type Busy = '' | 'cheatsCompile' | 'cheatsSend' | 'cheatsSaveBin' | 'cds';

/**
 * CheatsTabComponent — the sidebar Cheats tab (the old Cheats and CDS tabs merged).
 *
 *  1. Cheat tools — Cheat List page, archive editor, Converter, Porter, Scanner.
 *  2. Archive cheats (PCSX2 mode) — toggle and apply cheats from CheatCodeArchive.
 *  3. Cheat source — raw codes and Code Designer Source in one textarea (`#Name`
 *     lines separate cheats). Compile shows the resulting codes; Send writes them
 *     once or through the cheat engine; Save writes a raw-pair or engine .bin.
 *     "Use .cds file" compiles the active .cds editor (with Add Patch for its
 *     breakpoint/watchpoint sidecars), like the old CDS tab.
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
        <button class="cheats-list-btn" (click)="openCheatListClick.emit()"
                title="Open the Cheat List: manage, compile, send and save the game's cheats">
          <app-icon name="sparkles" [size]="14" />
          <span class="cheats-list-label">Cheat List</span>
          <span class="cheats-list-hint">engine · .bin export</span>
          <app-icon name="chevronright" [size]="13" />
        </button>
        <div class="btn-grid">
          <button class="cheats-tool-btn" (click)="openCheatsEditorClick.emit()"
                  title="Open the cheat archive editor">
            <app-icon name="database" [size]="13" /><span>Archive</span>
          </button>
          <button class="cheats-tool-btn" (click)="openScannerClick.emit()"
                  title="Open the Cheat Scanner: first/next value scans and memory structure scans">
            <app-icon name="search" [size]="13" /><span>Scanner</span>
          </button>
          <button class="cheats-tool-btn" (click)="openConverterClick.emit()"
                  title="Open the Cheat Code Converter to encrypt/decrypt cheat codes">
            <app-icon name="repeat" [size]="13" /><span>Converter</span>
          </button>
          <button class="cheats-tool-btn" (click)="openPorterClick.emit()"
                  title="Open the Cheat Code Porter to port cheats between game revisions">
            <app-icon name="gitcompare" [size]="13" /><span>Porter</span>
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

          @if (archiveLists().length > 0) {
            <label class="archive-list-picker" title="Which of the game's cheat lists to use">
              <span>List</span>
              <select [disabled]="archiveLoading()" (change)="onSelectList($any($event.target).value)">
                @for (l of archiveLists(); track l.id) {
                  <option [value]="l.id" [selected]="l.id === archiveListId()">
                    {{ l.title }} ({{ l.cheatCount }}){{ l.forDisc ? '' : ' · whole game' }}
                  </option>
                }
              </select>
            </label>
          }

          @if (archiveError()) {
            <div class="cheat-message cheat-error">{{ archiveError() }}</div>
          }

          @if (archiveLoaded() && archiveLists().length === 0 && !archiveLoading() && !archiveError()) {
            <div class="archive-empty">
              No cheat lists for this game in the cheat archive.
              <button class="ibtn archive-import-btn" [disabled]="!isConnected()" (click)="onImportArchive()"
                      title="Import this game's cheats from the cheat repository text files into cheat lists">
                <app-icon name="download" [size]="12" /><span>Import from repository</span>
              </button>
            </div>
          } @else if (archiveLoaded() && archiveCheats().length === 0 && !archiveLoading() && !archiveError()) {
            <div class="archive-empty">This cheat list is empty.</div>
          }

          @if (archiveCheats().length > 0) {
            <div class="archive-apply-row">
              <button class="ibtn" (click)="onApplyAll()"
                      [disabled]="!isConnected() || !hasEnabledArchiveCheats()">
                <app-icon name="zap" [size]="13" /><span>Apply All Enabled</span>
              </button>
            </div>
            <ul class="archive-list">
              @for (cheat of archiveCheats(); track $index; let i = $index) {
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

      <!-- ── Cheat source (raw codes + CDS) ─────────────────────────────── -->
      <div class="t-head cheats-source-head">
        <app-icon name="filecode" [size]="14" />
        <span class="t-head-label">Cheat Source</span>
        <span class="cheats-chip" title="Raw code pairs, assembly (address / instructions), freeze, cond and timer can be mixed">raw + CDS</span>
      </div>
      <textarea id="cheats-input"
        spellcheck="false"
        [value]="cheatText()"
        (input)="cheatText.set($any($event.target).value)"
        (keydown.control.enter)="onRun('cheatsCompile')"
        placeholder="#Infinite Health&#10;20123456 000003E8&#10;&#10;#Moon jump (CDS)&#10;freeze half $00347A52 = 03E7&#10;address $00200000&#10;  addiu v0, zero, 1"></textarea>

      <div class="cheats-actions">
        <button class="ibtn" (click)="onRun('cheatsCompile')" [disabled]="!isConnected() || !!busy() || !cheatText().trim()" title="Compile to raw codes (Ctrl+Enter)">
          <app-icon name="zap" [size]="13" /><span>{{ busy() === 'cheatsCompile' ? 'Compiling…' : 'Compile' }}</span>
        </button>
        <button class="ibtn" (click)="onCompileCdsFile()" [disabled]="!isConnected() || !!busy()" title="Compile the active .cds editor instead">
          <app-icon name="filecode" [size]="13" /><span>{{ busy() === 'cds' ? 'Compiling…' : 'Use .cds file' }}</span>
        </button>
        <button class="ibtn" (click)="onAddToList()" [disabled]="!cheatText().trim()" title="Add these cheats to the game's Cheat List">
          <app-icon name="plus" [size]="13" /><span>Add to list</span>
        </button>
      </div>

      <div class="cheats-send-row">
        <div class="cheats-seg" title="Once writes the codes a single time; the engine re-applies them every frame">
          <button [class.cheats-seg-on]="sendMode() === 'once'" (click)="sendMode.set('once')">Once</button>
          <button [class.cheats-seg-on]="sendMode() === 'engine'" (click)="sendMode.set('engine')">Engine</button>
        </div>
        <button class="ibtn cheats-primary" (click)="onRun('cheatsSend')" [disabled]="!isConnected() || !!busy() || !cheatText().trim()">
          <app-icon name="play" [size]="13" /><span>{{ busy() === 'cheatsSend' ? 'Sending…' : 'Send' }}</span>
        </button>
      </div>
      <div class="cheats-send-row">
        <div class="cheats-seg" title="Raw writes 8-byte code pairs; Engine writes the cheat engine image">
          <button [class.cheats-seg-on]="binKind() === 'raw'" (click)="binKind.set('raw')">Raw</button>
          <button [class.cheats-seg-on]="binKind() === 'engine'" (click)="binKind.set('engine')">Engine</button>
        </div>
        <button class="ibtn" (click)="onRun('cheatsSaveBin')" [disabled]="!!busy() || !cheatText().trim() || (binKind() === 'engine' && !isConnected())">
          <app-icon name="save" [size]="13" /><span>{{ busy() === 'cheatsSaveBin' ? 'Saving…' : 'Save .bin' }}</span>
        </button>
      </div>

      @if (errors().length > 0) {
        <ul class="cheats-errors">
          @for (e of errors(); track $index) { <li>{{ e }}</li> }
        </ul>
      } @else if (output()) {
        <div class="cheats-output">
          <div class="cheats-output-head">
            <span>{{ outputFromCds() ? 'Compiled .cds file' : 'Compiled codes' }}</span>
            @if (outputFromCds()) {
              <button class="cheats-mini-btn" (click)="onAddPatch()" [disabled]="!isConnected()" title="Write the codes and install the file's breakpoint/watchpoint sidecars">
                <app-icon name="plugzap" [size]="11" /> Add Patch
              </button>
              <button class="cheats-mini-btn" (click)="onUseOutput()" title="Put these codes in the cheat source">
                <app-icon name="arrowleft" [size]="11" /> To source
              </button>
            }
          </div>
          <pre class="cheats-output-codes">{{ output() }}</pre>
        </div>
      }

      @if (cheatMessage()) {
        <div class="cheat-message" [class.cheat-error]="cheatError()">{{ cheatMessage() }}</div>
      }
    </div>
  `,
})
export class CheatsTabComponent implements OnDestroy {
  private readonly bridge = inject(VsCodeBridgeService);
  private readonly subs: Subscription[] = [];
  private _messageTimer: ReturnType<typeof setTimeout> | undefined;

  readonly isConnected = input(false);
  /** True when the sidebar is connected in PCSX2 PINE bridge mode. */
  readonly isPcsx2 = input(false);

  // ── Tool outputs (relayed to panel parent → panel provider) ────────────────
  readonly openConverterClick    = output<void>();
  readonly openPorterClick       = output<void>();
  readonly openScannerClick      = output<void>();
  readonly openCheatListClick    = output<void>();
  readonly openCheatsEditorClick = output<void>();

  /** Two-way bound cheat source (persisted by the parent). */
  readonly cheatText = model('');

  readonly cheatMessage = signal('');
  readonly cheatError   = signal(false);
  readonly busy         = signal<Busy>('');
  readonly sendMode     = signal<'once' | 'engine'>('once');
  readonly binKind      = signal<'raw' | 'engine'>('raw');
  readonly output       = signal('');
  readonly outputFromCds = signal(false);
  readonly errors       = signal<string[]>([]);

  // ── Archive state ──────────────────────────────────────────────────────────
  readonly archiveCheats  = signal<ArchiveCheat[]>([]);
  readonly archiveGame    = signal('');
  readonly archiveLoading = signal(false);
  readonly archiveError   = signal('');
  readonly archiveLists   = signal<ArchiveCheatList[]>([]);
  readonly archiveListId  = signal<number | null>(null);
  /** True once a fetch has answered, so "no lists" isn't shown before the first load. */
  readonly archiveLoaded  = signal(false);

  constructor() {
    this.subs.push(
      this.bridge.on<CheatsAppliedMsg>('cheatsApplied').subscribe(msg => {
        this._message(msg.success ? 'Cheats applied!' : (msg.error ?? 'Failed to apply cheats.'), !msg.success);
      }),
      this.bridge.on<CheatsResultMsg>('cheatsResult').subscribe(msg => {
        if (msg.phase === 'compiled') {
          this.errors.set(msg.errors ?? []);
          this.output.set(msg.codes ?? '');
          this.outputFromCds.set(false);
          if (msg.action === 'cheatsCompile' || !msg.success) {
            this.busy.set('');
            const count = (msg.codes ?? '').split('\n').filter(l => /^[0-9a-f]{8}\s/i.test(l)).length;
            this._message(msg.success ? `Compiled ${count} code(s).` : `${(msg.errors ?? []).length} compile error(s).`, !msg.success);
          }
          return;
        }
        this.busy.set('');
        if (msg.message) { this._message(msg.message, !msg.success); }
      }),
      this.bridge.on<CdsCompileResultMsg>('cdsCompileResult').subscribe(msg => {
        // Also arrives from the .cds editor-title Run action, so show it whenever it comes.
        if (this.busy() === 'cds') { this.busy.set(''); }
        this.outputFromCds.set(msg.success);
        this.output.set(msg.success ? msg.codes ?? '' : '');
        this.errors.set(msg.success ? [] : (msg.errors ?? []).map(e => e.lineNumber > 0 ? `line ${e.lineNumber}: ${e.message}` : e.message));
        if (msg.success) { this._message('Compiled the active .cds file.', false); }
      }),
      this.bridge.on<{ command: 'cdsPatchApplied'; success?: boolean; error?: string }>('cdsPatchApplied').subscribe(msg => {
        this._message(msg.success === false ? (msg.error ?? 'Patch failed.') : 'Patch applied.', msg.success === false);
      }),
      this.bridge.on<{ command: 'archiveCheatsLoading' }>('archiveCheatsLoading').subscribe(() => {
        this.archiveLoading.set(true);
        this.archiveError.set('');
      }),
      this.bridge.on<ArchiveCheatsLoadedMsg>('archiveCheatsLoaded').subscribe(msg => {
        this.archiveLoading.set(false);
        this.archiveError.set('');
        this.archiveLoaded.set(true);
        this.archiveCheats.set(msg.cheats ?? []);
        this.archiveGame.set(msg.game ?? '');
        this.archiveLists.set(msg.lists ?? []);
        this.archiveListId.set(msg.cheatListId ?? null);
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
    if (this._messageTimer) { clearTimeout(this._messageTimer); }
  }

  hasEnabledArchiveCheats(): boolean {
    return this.archiveCheats().some(c => c.enabled);
  }

  onFetchArchive(): void {
    this.bridge.postMessage({ command: 'fetchArchiveCheats' });
  }

  onSelectList(value: string): void {
    const id = Number(value);
    if (!Number.isFinite(id) || id === this.archiveListId()) { return; }
    this.archiveListId.set(id);
    this.bridge.postMessage({ command: 'selectArchiveCheatList', cheatListId: id });
  }

  onImportArchive(): void {
    this.bridge.postMessage({ command: 'importCheatArchive' });
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

  onRun(command: 'cheatsCompile' | 'cheatsSend' | 'cheatsSaveBin'): void {
    if (this.busy() || !this.cheatText().trim()) { return; }
    this.busy.set(command);
    this.cheatMessage.set('');
    this.bridge.postMessage({ command, source: this.cheatText(), mode: this.sendMode(), kind: this.binKind() });
  }

  onCompileCdsFile(): void {
    if (this.busy()) { return; }
    this.busy.set('cds');
    this.bridge.postMessage({ command: 'compileCds' });
  }

  onAddPatch(): void {
    if (!this.output()) { return; }
    this.bridge.postMessage({ command: 'addPatch', codes: this.output() });
  }

  onUseOutput(): void {
    const codes = this.output();
    if (!codes) { return; }
    const text = this.cheatText().trimEnd();
    this.cheatText.set(text ? `${text}\n\n#From .cds file\n${codes}` : `#From .cds file\n${codes}`);
    this.outputFromCds.set(false);
  }

  onAddToList(): void {
    this.bridge.postMessage({ command: 'cheatsAddToList', source: this.cheatText() });
  }

  private _message(text: string, isError: boolean): void {
    this.cheatError.set(isError);
    this.cheatMessage.set(text);
    if (this._messageTimer) { clearTimeout(this._messageTimer); }
    // Errors and long engine/file reports stay until the next action.
    if (!isError && text.length < 60) {
      this._messageTimer = setTimeout(() => this.cheatMessage.set(''), 5000);
    }
  }
}
