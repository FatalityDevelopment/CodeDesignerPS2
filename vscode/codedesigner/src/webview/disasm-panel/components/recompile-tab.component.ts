import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  input,
  signal,
  OnDestroy,
  inject,
} from '@angular/core';
import { Subscription } from 'rxjs';
import { VsCodeBridgeService } from '../../shared/vscode-bridge.service';
import { DiscElfEntry } from '../types';

interface ListDiscElfsResultMsg {
  command: 'listDiscElfsResult';
  success: boolean;
  elfFiles?: DiscElfEntry[];
  error?: string;
}

interface RecompileResultMsg {
  command: 'recompileResult';
  success: boolean;
  functionCount?: number;
  writtenFiles?: string[];
  error?: string;
}

interface BrowseOutputDirectoryResultMsg {
  command: 'browseOutputDirectoryResult';
  directory: string;
}

/**
 * RecompileTabComponent - Static recompiler tab.
 *
 * Manages its own internal state (ELF list, selected ELF, output directory,
 * running flag, status messages). Subscribes directly to the relevant bridge
 * messages (`listDiscElfsResult`, `recompileResult`, `browseOutputDirectoryResult`)
 * and posts commands (`listDiscElfs`, `browseOutputDirectory`, `recompile`) to
 * the extension host. Accepts read-only `isConnected` and `selectedGame` inputs
 * from the parent.
 */
@Component({
  selector: 'app-recompile-tab',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="container">
      <div class="section-label">Static Recompiler</div>
      <p class="recompile-help">
        Extracts an ELF from the loaded disc image and generates C++ source files.
        Each game has one main executable (BOOT2, shown with ★); other files are
        IOP modules (IRX) or game overlays.
      </p>

      <div class="row">
        <button id="recompileListBtn" (click)="onListDiscElfs()" [disabled]="!selectedGame()">
          Load ELF list from disc
        </button>
      </div>

      @if (elfList().length > 0) {
        <div class="row">
          <select id="recompileElfSelect" [value]="selectedElf()"
                  (change)="selectedElf.set($any($event.target).value)">
            <option value="">Select ELF to recompile…</option>
            @for (e of elfList(); track e.Name) {
              <option [value]="e.Name">
                {{e.IsBootElf ? '★ ' : ''}}{{e.Name}} ({{formatBytes(e.Size)}})
              </option>
            }
          </select>
        </div>
      }

      <div class="row">
        <input id="recompileOutputDir" type="text"
          [value]="outputDir()"
          (input)="outputDir.set($any($event.target).value)"
          placeholder="Output directory (default: recomp_out)" />
        <button id="recompileBrowseBtn" (click)="onBrowseOutputDir()"
                title="Browse for output directory"
                aria-label="Browse for output directory">…</button>
      </div>

      <div class="row">
        <button id="recompileRunBtn" (click)="onRunRecompile()"
                [disabled]="!selectedGame() || running()">
          {{running() ? 'Recompiling…' : 'Recompile'}}
        </button>
      </div>

      @if (status()) {
        <div class="recompile-status" [class.recompile-error]="isError()">
          {{status()}}
        </div>
      }

      @if (writtenFiles().length > 0) {
        <div class="recompile-files">
          <div class="section-label">Generated files</div>
          <ul>
            @for (f of writtenFiles(); track f) {
              <li class="recompile-file-item">{{f}}</li>
            }
          </ul>
        </div>
      }
    </div>
  `,
})
export class RecompileTabComponent implements OnDestroy {
  private readonly bridge = inject(VsCodeBridgeService);
  private readonly subs: Subscription[] = [];

  readonly isConnected = input(false);
  readonly selectedGame = input('');

  readonly elfList = signal<DiscElfEntry[]>([]);
  readonly selectedElf = signal('');
  readonly outputDir = signal('');
  readonly running = signal(false);
  readonly status = signal('');
  readonly isError = signal(false);
  readonly writtenFiles = signal<string[]>([]);

  constructor() {
    this.subs.push(
      this.bridge.on<ListDiscElfsResultMsg>('listDiscElfsResult').subscribe(msg => {
        if (msg.success && msg.elfFiles) {
          this.elfList.set(msg.elfFiles);
          const boot = msg.elfFiles.find(e => e.IsBootElf);
          if (boot) { this.selectedElf.set(boot.Name); }
          this.status.set('');
          this.isError.set(false);
        } else {
          this.status.set(msg.error ?? 'Failed to load ELF list.');
          this.isError.set(true);
        }
      }),
      this.bridge.on<RecompileResultMsg>('recompileResult').subscribe(msg => {
        this.running.set(false);
        if (msg.success) {
          this.status.set(`Done — ${msg.functionCount ?? 0} function(s) recompiled.`);
          this.isError.set(false);
          this.writtenFiles.set(msg.writtenFiles ?? []);
        } else {
          this.status.set(msg.error ?? 'Recompilation failed.');
          this.isError.set(true);
          this.writtenFiles.set([]);
        }
      }),
      this.bridge.on<BrowseOutputDirectoryResultMsg>('browseOutputDirectoryResult').subscribe(msg => {
        if (msg.directory) {
          this.outputDir.set(msg.directory);
        }
      })
    );
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
  }

  onListDiscElfs(): void {
    this.elfList.set([]);
    this.status.set('Loading ELF list…');
    this.isError.set(false);
    this.writtenFiles.set([]);
    this.bridge.postMessage({ command: 'listDiscElfs', gamePath: this.selectedGame() });
  }

  onBrowseOutputDir(): void {
    this.bridge.postMessage({ command: 'browseOutputDirectory' });
  }

  onRunRecompile(): void {
    if (!this.selectedGame() || this.running()) { return; }
    this.running.set(true);
    this.status.set('Recompiling…');
    this.isError.set(false);
    this.writtenFiles.set([]);
    this.bridge.postMessage({
      command: 'recompile',
      gamePath: this.selectedGame(),
      elfName: this.selectedElf(),
      outputDirectory: this.outputDir(),
    });
  }

  formatBytes(bytes: number): string {
    if (bytes < 1024) { return `${bytes} B`; }
    if (bytes < 1024 * 1024) { return `${(bytes / 1024).toFixed(1)} KB`; }
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
