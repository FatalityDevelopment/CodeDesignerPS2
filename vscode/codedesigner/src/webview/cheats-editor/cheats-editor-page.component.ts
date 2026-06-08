import {
  Component,
  AfterViewInit,
  ViewEncapsulation,
  computed,
  effect,
  signal,
  viewChild,
  ElementRef,
} from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import { PageHeaderComponent } from '../shared/page-header.component';
import './cheats-editor-page.css';

/** A game that owns cheat lists — mirrors CheatGameInfo on the C# side. */
interface CheatGame {
  gameId: number;
  title: string;
  coverSerial?: string;
  hasCover: boolean;
  listCount: number;
}

/** A cheat list ("build") — mirrors CheatListInfo on the C# side. */
interface CheatBuild {
  id: number;
  gameId: number;
  title: string;
  cheatCount: number;
  region?: string;
  serial?: string;
  crc?: string;
  updatedAt?: string;
}

/** A cheat parsed out of the editor text, for the sections panel. */
interface ParsedCheat {
  label: string;
  section: string;
  codeCount: number;
  lineIndex: number;
}

const DEFAULT_SECTION = 'Others';
const CONVERT_FORMATS = [
  'Raw', 'ActionReplay1', 'ActionReplay2', 'ActionReplayMax',
  'GameSharkV3', 'CodeBreakerV1', 'CodeBreakerV7',
];

const SECTION_RE = /^\[--SectionStart:(.*)--\]$/;
const BRACKET_RE = /^\[(.+)\]$/;
const CODE_RE = /^[0-9A-Fa-f?*]+(\s+[0-9A-Fa-f?*]+)*$/;

/**
 * Three-panel PS2 cheat-code editor:
 *  - left:   game picker + cover + cheat-list ("build") list
 *  - middle: the cheat file as editable text, with a validity indicator
 *  - right:  the sections found in the text + the cheats within each
 */
@Component({
  selector: 'app-cheats-editor-page',
  standalone: true,
  imports: [PageHeaderComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="ce-shell">
      <app-page-header icon="sparkles" title="Cheats" subtitle="Cheat code editor" />
      <!-- ══ Panel 1 — games & builds ══════════════════════════════════ -->
      <div class="ce-panel ce-panel-left">
        <div class="ce-panel-head">Cheats</div>

        <input class="ce-search" type="text" placeholder="Search games…"
               [value]="gameSearch()" (input)="onGameSearch($event)" />

        @if (selectedGame(); as g) {
          <div class="ce-cover">
            @if (cover(g.coverSerial); as src) {
              <img class="ce-cover-img" [src]="src" [alt]="g.title" />
            } @else {
              <div class="ce-cover-ph">{{ initial(g.title) }}</div>
            }
          </div>
          <div class="ce-game-name">{{ g.title }}</div>
        }

        <div class="ce-list">
          @if (gameSearch().trim() && !selectedGame()) {
            @for (g of filteredGames(); track g.gameId) {
              <div class="ce-list-row" (click)="selectGame(g)">
                <span class="ce-list-title">{{ g.title }}</span>
                <span class="ce-badge">{{ g.listCount }}</span>
              </div>
            }
            @if (filteredGames().length === 0) {
              <div class="ce-empty">No games match.</div>
            }
          } @else if (selectedGame()) {
            @for (b of builds(); track b.id) {
              <div class="ce-list-row" [class.selected]="b.id === selectedBuildId()"
                   (click)="selectBuild(b)">
                <span class="ce-list-title">{{ b.title }}</span>
                <span class="ce-badge">{{ b.cheatCount }}</span>
              </div>
            }
            @if (builds().length === 0) {
              <div class="ce-empty">No cheat lists for this game.</div>
            }
          } @else {
            <div class="ce-empty">Search for a game to begin.</div>
          }
        </div>

        <div class="ce-toolbar">
          <button class="ce-btn" [disabled]="!selectedGame()" (click)="addBuild()"
                  title="Add a new cheat list">＋ Build</button>
          <button class="ce-btn" [disabled]="!selectedBuildId()" (click)="deleteBuild()"
                  title="Delete the selected cheat list">🗑 Delete</button>
          <button class="ce-btn" [disabled]="busy()" (click)="bulkImport()"
                  title="Bulk-import cheats from a CheatCodeArchive folder">⤵ Import</button>
        </div>
      </div>

      <!-- ══ Panel 2 — editor ══════════════════════════════════════════ -->
      <div class="ce-panel ce-panel-mid">
        <div class="ce-panel-head">
          Cheats
          @if (selectedBuildId()) {
            <span class="ce-validity" [class.invalid]="!valid()">
              {{ valid() ? '-- Valid --' : '-- Invalid --' }}
            </span>
          }
        </div>

        @if (selectedBuild(); as b) {
          <div class="ce-meta-row">
            <span class="ce-meta-label">Build:</span>
            <span class="ce-meta-val">{{ b.crc || b.serial || '—' }}</span>
            <span class="ce-meta-label">Region:</span>
            <span class="ce-meta-val">{{ b.region || '—' }}</span>
            @if (dirty()) { <span class="ce-dirty">● unsaved</span> }
          </div>
        }

        <textarea #editor class="ce-editor" spellcheck="false"
                  [value]="editorText()" (input)="onEditorInput($event)"
                  [disabled]="!selectedBuildId()"
                  placeholder="Select a cheat list to edit…"></textarea>

        @if (!valid() && errors().length) {
          <div class="ce-errors">
            @for (e of errors(); track e) { <div>{{ e }}</div> }
          </div>
        }

        @if (convertOpen()) {
          <div class="ce-convert">
            <select class="ce-select" [value]="convertFormat()" (change)="onConvertFormat($event)">
              @for (f of formats; track f) { <option [value]="f">{{ f }}</option> }
            </select>
            <button class="ce-btn" (click)="convert('toRaw')">→ To Raw</button>
            <button class="ce-btn" (click)="convert('toEngine')">→ To {{ convertFormat() }}</button>
            <span class="ce-convert-hint">Converts the selected text (or all text).</span>
          </div>
        }

        <div class="ce-toolbar">
          <button class="ce-btn ce-primary-btn" [disabled]="!selectedBuildId() || !dirty()"
                  (click)="save()">💾 Save</button>
          <button class="ce-btn" [disabled]="!selectedBuildId()" (click)="insertSection()"
                  title="Insert a new section marker">＋ Section</button>
          <button class="ce-btn" [disabled]="!selectedBuildId()"
                  [class.active]="convertOpen()" (click)="convertOpen.set(!convertOpen())"
                  title="Convert cheat code formats">⇄ Convert</button>
        </div>
      </div>

      <!-- ══ Panel 3 — sections ════════════════════════════════════════ -->
      <div class="ce-panel ce-panel-right">
        <div class="ce-panel-head">Cheat Sections</div>

        <select class="ce-select ce-section-select" [value]="selectedSection()"
                (change)="onSectionChange($event)">
          @for (s of sections(); track s) { <option [value]="s">{{ s }}</option> }
        </select>

        <div class="ce-list">
          @for (c of cheatsInSection(); track c.lineIndex) {
            <div class="ce-list-row" (click)="jumpToCheat(c)">
              <span class="ce-list-title">{{ c.label }}</span>
              <span class="ce-badge">{{ c.codeCount }}</span>
            </div>
          }
          @if (cheatsInSection().length === 0) {
            <div class="ce-empty">No cheats in this section.</div>
          }
        </div>
      </div>
    </div>

    @if (statusMsg()) {
      <div class="ce-status" [class.error]="statusIsError()">{{ statusMsg() }}</div>
    }
  `,
})
export class CheatsEditorPageComponent implements AfterViewInit {
  private readonly vscode = getVsCodeApi();
  private readonly editorRef = viewChild<ElementRef<HTMLTextAreaElement>>('editor');

  readonly formats = CONVERT_FORMATS;

  readonly games = signal<CheatGame[]>([]);
  readonly gameSearch = signal('');
  readonly selectedGameId = signal<number | null>(null);

  readonly builds = signal<CheatBuild[]>([]);
  readonly selectedBuildId = signal<number | null>(null);

  readonly editorText = signal('');
  private readonly loadedText = signal('');

  readonly selectedSection = signal(DEFAULT_SECTION);
  readonly convertOpen = signal(false);
  readonly convertFormat = signal('Raw');

  readonly busy = signal(false);
  readonly statusMsg = signal('');
  readonly statusIsError = signal(false);

  private readonly coverMap = signal<Record<string, string>>({});
  private readonly coverRequested = new Set<string>();
  /** A game title to pre-select once the game list arrives. */
  private pendingGameTitle: string | null = null;

  // ── Derived state ──────────────────────────────────────────────────────

  readonly filteredGames = computed(() => {
    const q = this.gameSearch().trim().toLowerCase();
    if (!q) { return []; }
    return this.games()
      .filter(g => g.title.toLowerCase().includes(q))
      .slice(0, 200);
  });

  readonly selectedGame = computed(() =>
    this.games().find(g => g.gameId === this.selectedGameId()) ?? null);

  readonly selectedBuild = computed(() =>
    this.builds().find(b => b.id === this.selectedBuildId()) ?? null);

  readonly dirty = computed(() => this.editorText() !== this.loadedText());

  /** Parses the editor text into cheats + section list + validation errors. */
  private readonly parsed = computed(() => {
    const lines = this.editorText().split(/\r?\n/);
    const cheats: ParsedCheat[] = [];
    const sections: string[] = [];
    const errors: string[] = [];
    let section = DEFAULT_SECTION;
    let current: ParsedCheat | null = null;

    lines.forEach((raw, idx) => {
      const line = raw.trim();
      if (!line) { return; }

      const sm = SECTION_RE.exec(line);
      if (sm) {
        section = sm[1].trim() || DEFAULT_SECTION;
        if (!sections.includes(section)) { sections.push(section); }
        current = null;
        return;
      }
      const bm = BRACKET_RE.exec(line);
      if (bm) {
        if (!sections.includes(section)) { sections.push(section); }
        current = { label: bm[1].trim(), section, codeCount: 0, lineIndex: idx };
        cheats.push(current);
        return;
      }
      if (!current) {
        errors.push(`Line ${idx + 1}: code "${line}" before any [cheat] header.`);
        return;
      }
      if (!CODE_RE.test(line)) {
        errors.push(`Line ${idx + 1}: "${line}" is not a valid code line (hex digits plus ? or * placeholders).`);
        return;
      }
      current.codeCount++;
    });

    for (const c of cheats) {
      if (c.codeCount === 0) { errors.push(`Cheat "${c.label}" has no code lines.`); }
    }
    if (sections.length === 0) { sections.push(DEFAULT_SECTION); }
    return { cheats, sections, errors };
  });

  readonly sections = computed(() => this.parsed().sections);
  readonly errors = computed(() => this.parsed().errors);
  readonly valid = computed(() => this.parsed().errors.length === 0);
  readonly cheatsInSection = computed(() =>
    this.parsed().cheats.filter(c => c.section === this.selectedSection()));

  constructor() {
    // Keep the section dropdown pointed at a section that still exists.
    effect(() => {
      const secs = this.sections();
      if (!secs.includes(this.selectedSection())) {
        this.selectedSection.set(secs[0] ?? DEFAULT_SECTION);
      }
    });
  }

  ngAfterViewInit(): void {
    window.addEventListener('message', (event: MessageEvent) => {
      const data = event.data as { type: string; [key: string]: unknown };
      switch (data['type']) {
        case 'cheatGames':
          this.games.set((data['games'] as CheatGame[]) ?? []);
          this.busy.set(false);
          this.applyPendingGame();
          break;
        case 'cheatBuilds': {
          const builds = (data['builds'] as CheatBuild[]) ?? [];
          this.builds.set(builds);
          // Drop the selection if the build it pointed at no longer exists.
          if (this.selectedBuildId() !== null &&
              !builds.some(b => b.id === this.selectedBuildId())) {
            this.selectedBuildId.set(null);
            this.editorText.set('');
            this.loadedText.set('');
          }
          break;
        }
        case 'cheatBuildText':
          this.selectedBuildId.set((data['cheatListId'] as number) ?? null);
          this.editorText.set((data['text'] as string) ?? '');
          this.loadedText.set((data['text'] as string) ?? '');
          break;
        case 'cheatStatus':
          this.statusMsg.set((data['message'] as string) ?? '');
          this.statusIsError.set(false);
          this.busy.set((data['busy'] as boolean) ?? false);
          break;
        case 'cheatError':
          this.statusMsg.set((data['message'] as string) ?? 'Error');
          this.statusIsError.set(true);
          this.busy.set(false);
          break;
        case 'cheatCover': {
          const serial = data['serial'] as string;
          this.coverMap.update(m => ({ ...m, [serial]: (data['dataUri'] as string) ?? '' }));
          break;
        }
        case 'convertResult':
          this.applyConvertResult((data['text'] as string) ?? '');
          break;
        case 'initGameTitle':
          this.pendingGameTitle = (data['title'] as string) ?? null;
          this.applyPendingGame();
          break;
      }
    });
    this.vscode.postMessage({ type: 'ready' });
  }

  private applyPendingGame(): void {
    if (!this.pendingGameTitle) { return; }
    const match = this.games().find(
      g => g.title.toLowerCase() === this.pendingGameTitle!.toLowerCase());
    this.pendingGameTitle = null;
    if (match) { this.selectGame(match); }
  }

  // ── Game / build selection ─────────────────────────────────────────────

  onGameSearch(e: Event): void {
    this.gameSearch.set((e.target as HTMLInputElement).value);
    this.selectedGameId.set(null);
    this.builds.set([]);
    this.selectedBuildId.set(null);
    this.editorText.set('');
    this.loadedText.set('');
  }

  selectGame(g: CheatGame): void {
    this.selectedGameId.set(g.gameId);
    this.gameSearch.set('');
    this.builds.set([]);
    this.selectedBuildId.set(null);
    this.editorText.set('');
    this.loadedText.set('');
    if (g.coverSerial) { this.requestCover(g.coverSerial); }
    this.vscode.postMessage({ type: 'selectGame', gameId: g.gameId });
  }

  selectBuild(b: CheatBuild): void {
    if (b.id === this.selectedBuildId()) { return; }
    // The extension confirms discarding unsaved edits, then replies with the
    // text — selectedBuildId is only updated once that reply arrives.
    this.vscode.postMessage({ type: 'loadBuild', cheatListId: b.id, dirty: this.dirty() });
  }

  addBuild(): void {
    const g = this.selectedGame();
    if (!g) { return; }
    this.vscode.postMessage({ type: 'createBuild', gameId: g.gameId });
  }

  deleteBuild(): void {
    const b = this.selectedBuild();
    if (!b) { return; }
    this.vscode.postMessage({ type: 'deleteBuild', cheatListId: b.id });
  }

  bulkImport(): void {
    this.busy.set(true);
    this.vscode.postMessage({ type: 'bulkImport' });
  }

  // ── Editor ─────────────────────────────────────────────────────────────

  onEditorInput(e: Event): void {
    this.editorText.set((e.target as HTMLTextAreaElement).value);
  }

  save(): void {
    const b = this.selectedBuild();
    if (!b) { return; }
    if (!this.valid()) {
      this.statusMsg.set('Cannot save — fix the highlighted errors first.');
      this.statusIsError.set(true);
      return;
    }
    this.vscode.postMessage({ type: 'saveBuild', cheatListId: b.id, text: this.editorText() });
  }

  insertSection(): void {
    // Insert a marker with a placeholder name; the user renames it inline.
    const marker = `\n[--SectionStart:New Section--]\n\n`;
    const ta = this.editorRef()?.nativeElement;
    if (ta) {
      const at = ta.selectionStart;
      const text = this.editorText();
      this.editorText.set(text.slice(0, at) + marker + text.slice(at));
    } else {
      this.editorText.update(t => t + marker);
    }
  }

  // ── Sections panel ─────────────────────────────────────────────────────

  onSectionChange(e: Event): void {
    this.selectedSection.set((e.target as HTMLSelectElement).value);
  }

  jumpToCheat(c: ParsedCheat): void {
    const ta = this.editorRef()?.nativeElement;
    if (!ta) { return; }
    const lines = this.editorText().split(/\r?\n/);
    let offset = 0;
    for (let i = 0; i < c.lineIndex && i < lines.length; i++) {
      offset += lines[i].length + 1;
    }
    const end = offset + (lines[c.lineIndex]?.length ?? 0);
    ta.focus();
    ta.setSelectionRange(offset, end);
    // Approximate scroll so the selected cheat is visible.
    const lineHeight = ta.scrollHeight / Math.max(1, lines.length);
    ta.scrollTop = Math.max(0, lineHeight * c.lineIndex - ta.clientHeight / 3);
  }

  // ── Convert ────────────────────────────────────────────────────────────

  onConvertFormat(e: Event): void {
    this.convertFormat.set((e.target as HTMLSelectElement).value);
  }

  convert(direction: 'toRaw' | 'toEngine'): void {
    const ta = this.editorRef()?.nativeElement;
    if (!ta) { return; }
    const hasSelection = ta.selectionStart !== ta.selectionEnd;
    const text = hasSelection
      ? this.editorText().slice(ta.selectionStart, ta.selectionEnd)
      : this.editorText();
    if (!text.trim()) {
      this.statusMsg.set('Nothing to convert.');
      this.statusIsError.set(true);
      return;
    }
    this.convertSelStart = hasSelection ? ta.selectionStart : 0;
    this.convertSelEnd = hasSelection ? ta.selectionEnd : this.editorText().length;
    this.vscode.postMessage({
      type: 'convert', text, direction, format: this.convertFormat(),
    });
  }

  private convertSelStart = 0;
  private convertSelEnd = 0;

  private applyConvertResult(result: string): void {
    if (!result) { return; }
    const text = this.editorText();
    this.editorText.set(text.slice(0, this.convertSelStart) + result + text.slice(this.convertSelEnd));
  }

  // ── Covers ─────────────────────────────────────────────────────────────

  cover(serial: string | undefined): string | null {
    if (!serial) { return null; }
    const uri = this.coverMap()[serial];
    return uri ? uri : null;
  }

  private requestCover(serial: string): void {
    if (this.coverRequested.has(serial)) { return; }
    this.coverRequested.add(serial);
    this.vscode.postMessage({ type: 'getCover', serial });
  }

  initial(title: string): string {
    return (title.trim()[0] ?? '?').toUpperCase();
  }
}
