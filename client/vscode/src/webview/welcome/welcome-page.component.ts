import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  AfterViewInit,
  NgZone,
  computed,
  signal,
  inject,
} from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { getVsCodeApi } from '../shared/vscode-api';
import { IconComponent } from '../shared/icon.component';
import '../shared/styles/shared.css';
import './welcome-page.css';

interface WalkthroughEntry { label: string; relPath: string; mtimeMs: number; }
interface ToolEntry { id: string; label: string; subtitle: string; icon: string; }
interface KeyboardEntry { keys: string; label: string; }
interface WhatsNewEntry { title: string; html: string; }

interface InitMsg {
  type: 'init';
  iconUri: string;
  version: string;
  walkthroughs: WalkthroughEntry[];
  tools: ToolEntry[];
  whatsNewHtml: WhatsNewEntry | null;
  keyboard: KeyboardEntry[];
}

interface StatusMsg {
  type: 'status';
  bridgeConnected: boolean;
  emulatorPath: string;
  emulatorLabel: string;
  emulatorRunning: boolean;
  biosLabel: string;
  gameTitle: string;
  gameSerial: string;
}

@Component({
  selector: 'app-welcome-page',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="cd-welcome">

  <!-- ── Hero (header + status) ─────────────────────────────────────── -->
  <section class="cd-hero">
    <div class="cd-hero-left">
      <div class="cd-hero-head">
        @if (iconUri()) {
          <img class="cd-hero-logo" [src]="iconUri()" alt="CodeDesigner" />
        }
        <div class="cd-hero-copy">
          <h1 class="cd-hero-title">CodeDesigner</h1>
          <p class="cd-hero-subtitle">PS2 Emotion Engine · MIPS R5900 Toolkit @if (version()) { · v{{version()}} }</p>
        </div>
      </div>

      <p class="cd-hero-blurb">
        Live disassembly, hex inspection, CDS patching, and static recompilation
        for the PlayStation 2 Emotion Engine — wired straight into PCSX2.
      </p>

      <div class="cd-hero-actions">
        <button class="cd-btn cd-btn-primary" (click)="connect()" title="Connect to a running PCSX2 / PINE bridge">
          <app-icon name="plug" [size]="13" />
          <span>Connect to PCSX2</span>
        </button>
        <button class="cd-btn" (click)="openProject()" title="Open a folder as a CodeDesigner project">
          <app-icon name="bookopen" [size]="13" />
          <span>Open Project…</span>
        </button>
        <button class="cd-btn" (click)="openDocsRoot()" title="Open the docs/README.md guide">
          <app-icon name="library" [size]="13" />
          <span>Read the docs</span>
        </button>
      </div>
    </div>

    <aside class="cd-status acc-section">
      <div class="cd-status-head">
        <span class="cd-status-dot" [class.cd-status-on]="bridgeConnected()"></span>
        <span class="cd-status-name">{{ bridgeConnected() ? 'Bridge online' : 'Bridge offline' }}</span>
        <span class="cd-status-host">127.0.0.1:8765</span>
      </div>
      <dl class="cd-status-grid">
        <div>
          <dt>Emulator</dt>
          <dd [class.cd-muted]="!emulatorLabel()" [title]="emulatorPath()">
            {{ emulatorLabel() || 'Not selected' }}
          </dd>
        </div>
        <div>
          <dt>BIOS</dt>
          <dd [class.cd-muted]="!biosLabel()">{{ biosLabel() || 'Not selected' }}</dd>
        </div>
        <div>
          <dt>Game</dt>
          <dd [class.cd-muted]="!gameTitle()" [title]="gameSerial()">
            {{ gameTitle() || 'Not loaded' }}
            @if (gameSerial()) { <span class="cd-status-serial">{{ gameSerial() }}</span> }
          </dd>
        </div>
        <div>
          <dt>{{ emulatorProcessLabel() }}</dt>
          <dd class="cd-status-state"
              [class.cd-status-on-text]="emulatorRunning()"
              [class.cd-muted]="!emulatorRunning()">
            <span class="cd-pip" [class.cd-pip-on]="emulatorRunning()"></span>
            {{ emulatorRunning() ? 'Running' : 'Offline' }}
          </dd>
        </div>
      </dl>
      @if (emulatorRunning()) {
        <div class="cd-status-foot">
          <app-icon name="circlecheck" [size]="12" />
          <span>EE RAM streaming · live</span>
        </div>
      }
    </aside>
  </section>

  <!-- ── Start / Walkthroughs ───────────────────────────────────────── -->
  <section class="cd-row cd-row-2col">
    <div class="cd-card acc-section">
      <div class="acc-header cd-card-header">
        <app-icon class="acc-ico" name="zap" [size]="13" />
        <span class="acc-title">Start</span>
      </div>
      <div class="acc-body">
        <ul class="cd-list">
          <li>
            <button class="cd-listrow" (click)="connect()">
              <app-icon class="cd-listrow-ico" name="plug" [size]="14" />
              <span class="cd-listrow-main">
                <span class="cd-listrow-title">Connect emulator…</span>
                <span class="cd-listrow-sub">PCSX2 over TCP</span>
              </span>
              <button class="browse-btn" type="button"
                      (click)="browseEmulator($event)"
                      title="Browse for emulator executable">…</button>
            </button>
          </li>
          <li>
            <button class="cd-listrow" (click)="newCdsFile()">
              <app-icon class="cd-listrow-ico" name="filecode" [size]="14" />
              <span class="cd-listrow-main">
                <span class="cd-listrow-title">New .cds file…</span>
                <span class="cd-listrow-sub">Code Designer Script</span>
              </span>
              <button class="browse-btn" type="button"
                      (click)="newCdsFile($event)"
                      title="Choose where to save the new .cds file">…</button>
            </button>
          </li>
          <li>
            <button class="cd-listrow" (click)="newCdsProject()">
              <app-icon class="cd-listrow-ico" name="workflow" [size]="14" />
              <span class="cd-listrow-main">
                <span class="cd-listrow-title">New CDS project…</span>
                <span class="cd-listrow-sub">Scaffolds main.cds + README</span>
              </span>
              <button class="browse-btn" type="button"
                      (click)="newCdsProject($event)"
                      title="Choose where to create the project folder">…</button>
            </button>
          </li>
          <li>
            <button class="cd-listrow" (click)="openProject()">
              <app-icon class="cd-listrow-ico" name="bookopen" [size]="14" />
              <span class="cd-listrow-main">
                <span class="cd-listrow-title">Open folder…</span>
                <span class="cd-listrow-sub">Existing project</span>
              </span>
              <button class="browse-btn" type="button"
                      (click)="openProject($event)"
                      title="Pick a folder to open as a project">…</button>
            </button>
          </li>
        </ul>
      </div>
    </div>

    <div class="cd-card acc-section">
      <div class="acc-header cd-card-header">
        <app-icon class="acc-ico" name="bookopen" [size]="13" />
        <span class="acc-title">Walkthroughs</span>
        <button class="cd-link-btn" (click)="openDocsRoot()" title="Open the docs folder">
          Browse all
          <app-icon name="chevronright" [size]="11" />
        </button>
      </div>
      <div class="acc-body">
        <ul class="cd-list">
          @if (walkthroughs().length === 0) {
            <li class="cd-empty">No docs found.</li>
          }
          @for (w of walkthroughs(); track w.relPath; let idx = $index) {
            <li>
              <button class="cd-listrow cd-listrow-walk" (click)="openDoc(w.relPath)">
                <span class="cd-listrow-check" [class.cd-listrow-check-on]="idx === 0">
                  @if (idx === 0) {
                    <app-icon name="circlecheck" [size]="13" />
                  } @else {
                    <app-icon name="circle" [size]="13" />
                  }
                </span>
                <span class="cd-listrow-main">
                  <span class="cd-listrow-title">{{ w.label }}</span>
                  <span class="cd-listrow-sub">{{ relativeAge(w.mtimeMs) }}</span>
                </span>
              </button>
            </li>
          }
        </ul>
      </div>
    </div>
  </section>

  <!-- ── Tools ─────────────────────────────────────────────────────── -->
  <section class="cd-card acc-section">
    <div class="acc-header cd-card-header">
      <app-icon class="acc-ico" name="wrench" [size]="13" />
      <span class="acc-title">Tools</span>
      <button class="cd-link-btn" (click)="openCommandsPalette()" title="Browse the CodeDesigner command palette">
        Browse all commands
        <app-icon name="chevronright" [size]="11" />
      </button>
    </div>
    <div class="acc-body">
      <div class="cd-tools-grid">
        @for (t of tools(); track t.id) {
          <button class="cd-tool" (click)="runCommand(t.id)" [title]="t.subtitle">
            <span class="cd-tool-ico"><app-icon [name]="t.icon" [size]="14" /></span>
            <span class="cd-tool-body">
              <span class="cd-tool-title">{{ t.label }}</span>
              <span class="cd-tool-sub">{{ t.subtitle }}</span>
            </span>
          </button>
        }
      </div>
    </div>
  </section>

  <!-- ── What's New + Keyboard tips ────────────────────────────────── -->
  <section class="cd-row cd-row-2col">
    <div class="cd-card acc-section cd-whatsnew">
      <div class="acc-header cd-card-header">
        <app-icon class="acc-ico" name="sparkles" [size]="13" />
        <span class="acc-title">What's new</span>
        @if (whatsNew()) { <span class="cd-tag">v{{ whatsNew()!.title }}</span> }
        <button class="cd-link-btn" (click)="openWhatsNewFile()" title="Open the source markdown">
          Full changelog
          <app-icon name="chevronright" [size]="11" />
        </button>
      </div>
      <div class="acc-body">
        @if (whatsNew()) {
          <div class="cd-md" [innerHTML]="whatsNewHtml()"></div>
        } @else {
          <p class="cd-empty">No release notes yet. Add a markdown file to <code>docs/whats-new/</code>.</p>
        }
      </div>
    </div>

    <div class="cd-card acc-section">
      <div class="acc-header cd-card-header">
        <app-icon class="acc-ico" name="settings" [size]="13" />
        <span class="acc-title">Keyboard tips</span>
      </div>
      <div class="acc-body">
        <ul class="cd-keys">
          @for (k of keyboard(); track k.keys) {
            <li>
              <kbd class="cd-kbd">{{ k.keys }}</kbd>
              <span class="cd-kbd-label">{{ k.label }}</span>
            </li>
          }
        </ul>
      </div>
    </div>
  </section>

</div>
`,
})
export class WelcomePageComponent implements AfterViewInit {
  private readonly api = getVsCodeApi();
  private readonly zone = inject(NgZone);
  private readonly sanitizer = inject(DomSanitizer);

  iconUri = signal('');
  version = signal('');
  walkthroughs = signal<WalkthroughEntry[]>([]);
  tools = signal<ToolEntry[]>([]);
  keyboard = signal<KeyboardEntry[]>([]);
  whatsNew = signal<WhatsNewEntry | null>(null);

  bridgeConnected = signal(false);
  emulatorPath = signal('');
  emulatorLabel = signal('');
  emulatorRunning = signal(false);
  biosLabel = signal('');
  gameTitle = signal('');
  gameSerial = signal('');

  whatsNewHtml = computed<SafeHtml>(() => {
    const wn = this.whatsNew();
    return wn ? this.sanitizer.bypassSecurityTrustHtml(wn.html) : '';
  });

  emulatorProcessLabel = computed<string>(() => {
    const label = this.emulatorLabel().toLowerCase();
    if (label.includes('pcsx2')) { return 'PCSX2'; }
    if (this.emulatorLabel()) { return this.emulatorLabel(); }
    return 'Emulator';
  });

  ngAfterViewInit(): void {
    window.addEventListener('message', (e: MessageEvent) => {
      this.zone.run(() => this.handleMessage(e.data));
    });
    this.api.postMessage({ type: 'ready' });
  }

  private handleMessage(msg: any): void {
    if (!msg || typeof msg !== 'object') { return; }
    if (msg.type === 'init') {
      const init = msg as InitMsg;
      this.iconUri.set(init.iconUri ?? '');
      this.version.set(init.version ?? '');
      this.walkthroughs.set(init.walkthroughs ?? []);
      this.tools.set(init.tools ?? []);
      this.keyboard.set(init.keyboard ?? []);
      this.whatsNew.set(init.whatsNewHtml ?? null);
    } else if (msg.type === 'status') {
      const s = msg as StatusMsg;
      this.bridgeConnected.set(!!s.bridgeConnected);
      this.emulatorPath.set(s.emulatorPath ?? '');
      this.emulatorLabel.set(s.emulatorLabel ?? '');
      this.emulatorRunning.set(!!s.emulatorRunning);
      this.biosLabel.set(s.biosLabel ?? '');
      this.gameTitle.set(s.gameTitle ?? '');
      this.gameSerial.set(s.gameSerial ?? '');
    }
  }

  // ── Action emitters ─────────────────────────────────────────────────

  connect(): void { this.api.postMessage({ type: 'connectEmulator' }); }
  openProject(ev?: Event): void { ev?.stopPropagation(); this.api.postMessage({ type: 'openProject' }); }
  newCdsFile(ev?: Event): void { ev?.stopPropagation(); this.api.postMessage({ type: 'newCdsFile' }); }
  newCdsProject(ev?: Event): void { ev?.stopPropagation(); this.api.postMessage({ type: 'newCdsProject' }); }
  browseEmulator(ev?: Event): void {
    ev?.stopPropagation();
    this.api.postMessage({ type: 'openCommand', command: 'codedesigner.connectServer' });
  }
  openDoc(rel: string): void { this.api.postMessage({ type: 'openDoc', path: rel }); }
  openDocsRoot(): void { this.api.postMessage({ type: 'openDocsRoot' }); }
  openWhatsNewFile(): void {
    const wn = this.whatsNew();
    if (!wn) { return; }
    this.api.postMessage({ type: 'openDoc', path: `docs/whats-new/${wn.title}.md` });
  }
  runCommand(id: string): void { this.api.postMessage({ type: 'openCommand', command: id }); }
  openCommandsPalette(): void { this.api.postMessage({ type: 'openCommand', command: 'workbench.action.showCommands' }); }

  // ── Helpers ────────────────────────────────────────────────────────

  relativeAge(mtimeMs: number): string {
    if (!mtimeMs) { return ''; }
    const diff = Date.now() - mtimeMs;
    const minute = 60_000, hour = 3_600_000, day = 86_400_000;
    if (diff < hour)      { return `${Math.max(1, Math.floor(diff / minute))} min ago`; }
    if (diff < day)       { return `${Math.floor(diff / hour)} hr ago`; }
    if (diff < 7 * day)   { return `${Math.floor(diff / day)} days ago`; }
    if (diff < 30 * day)  { return `${Math.floor(diff / (7 * day))} weeks ago`; }
    return new Date(mtimeMs).toLocaleDateString();
  }
}
