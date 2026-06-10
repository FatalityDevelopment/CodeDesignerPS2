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
import { IconComponent } from '../shared/icon.component';
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

interface VisibleAddressLink {
  key: string;
  token: string;
  top: number;
  left: number;
  width: number;
  height: number;
}

const DEFAULT_SECTION = 'Others';
const CONVERT_FORMATS = [
  'Raw', 'ActionReplay1', 'ActionReplay2', 'ActionReplayMax',
  'GameSharkV3', 'CodeBreakerV1', 'CodeBreakerV7',
];

const SECTION_RE = /^\[--SectionStart:(.*)--\]$/;
const BRACKET_RE = /^\[(.+)\]$/;
const CODE_RE = /^[0-9A-Fa-f?*]+(\s+[0-9A-Fa-f?*]+)*$/;
const CODE_TOKEN_RE = /[0-9A-Fa-f?*]+|\s+|\S+/g;
const MAX_HIGHLIGHTED_CHEAT_TEXT_CHARS = 200_000;

/**
 * Three-panel PS2 cheat-code editor:
 *  - left:   game picker + cover + cheat-list ("build") list
 *  - middle: the cheat file as editable text, with a validity indicator
 *  - right:  the sections found in the text + the cheats within each
 */
@Component({
  selector: 'app-cheats-editor-page',
  standalone: true,
  imports: [PageHeaderComponent, IconComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="ce-shell">
      <app-page-header icon="sparkles" title="Cheats" subtitle="Cheat code editor">
        <div page-header-actions class="ce-header-actions">
          <label class="ce-debug-toggle" [class.enabled]="debugViewEnabled()" title="Display this cheat list's addresses in the debugger">
            <input type="checkbox"
                   [checked]="debugViewEnabled()"
                   [disabled]="!selectedBuildId()"
                   (change)="onDebugViewToggle($event)" />
            <span>View in debugger</span>
          </label>
        </div>
      </app-page-header>
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
          <button class="ce-btn ce-icon-btn" [disabled]="busy()" (click)="refreshArchive()"
                  title="Refresh cheats from the project's CheatCodeArchive submodule">
            <app-icon name="refreshcw" [size]="13" /><span>Refresh</span>
          </button>
          <button class="ce-btn ce-icon-btn" [disabled]="busy()" (click)="bulkImport()"
                  title="Bulk-import cheats from a selected CheatCodeArchive folder">
            <app-icon name="download" [size]="13" /><span>Import</span>
          </button>
        </div>
      </div>

      @if (busy()) {
        <div class="ce-refresh-bar">
          <div class="ce-refresh-bar-inner"></div>
          <span class="ce-refresh-text">Refreshing CheatCodeArchive… this can take several minutes</span>
        </div>
      }

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

        <div class="ce-editor-wrap">
          @if (!highlightDisabled()) {
            <pre #highlighter class="ce-editor-highlight" aria-hidden="true" [innerHTML]="highlightedHtml()"></pre>
          }
          <textarea #editor class="ce-editor" [class.highlight-disabled]="highlightDisabled()" spellcheck="false" wrap="off"
                    [value]="editorText()" (input)="onEditorInput($event)" (click)="onEditorClick($event)" (scroll)="syncEditorScroll()"
                    [disabled]="!selectedBuildId()"
                    placeholder="Select a cheat list to edit…"></textarea>
          <div class="ce-address-link-layer" aria-hidden="true">
            @for (link of visibleAddressLinks(); track link.key) {
              <button type="button" class="ce-address-hitbox" tabindex="-1"
                      [style.top.px]="link.top"
                      [style.left.px]="link.left"
                      [style.width.px]="link.width"
                      [style.height.px]="link.height"
                      [title]="'Go to 0x' + link.token + ' in debugger'"
                      (mousedown)="$event.preventDefault()"
                      (click)="onAddressLinkClick($event, link.token)"></button>
            }
          </div>
        </div>

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

      @if (statusMsg()) {
        <div class="ce-status" [class.error]="statusIsError()">{{ statusMsg() }}</div>
      }
    </div>
  `,
})
export class CheatsEditorPageComponent implements AfterViewInit {
  private readonly vscode = getVsCodeApi();
  private readonly editorRef = viewChild<ElementRef<HTMLTextAreaElement>>('editor');
  private readonly highlighterRef = viewChild<ElementRef<HTMLPreElement>>('highlighter');

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
  readonly debugViewEnabled = signal(false);
  readonly visibleAddressLinks = signal<VisibleAddressLink[]>([]);

  private readonly coverMap = signal<Record<string, string>>({});
  private readonly coverRequested = new Set<string>();
  /** A game title to pre-select once the game list arrives. */
  private pendingGameTitle: string | null = null;
  private debuggerPostTimer: ReturnType<typeof setTimeout> | undefined;
  private measureCanvas: HTMLCanvasElement | undefined;
  private addressLinkFrame: number | undefined;
  private lineStartCacheText = '';
  private lineStartCache: number[] = [0];

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

    let inBlockComment = false;

    lines.forEach((raw, idx) => {
      const line = this.stripComments(raw, inBlockComment);
      inBlockComment = line.inBlockComment;
      const code = line.code.trim();
      if (!code) { return; }

      const sm = SECTION_RE.exec(code);
      if (sm) {
        section = sm[1].trim() || DEFAULT_SECTION;
        if (!sections.includes(section)) { sections.push(section); }
        current = null;
        return;
      }
      const bm = BRACKET_RE.exec(code);
      if (bm) {
        if (!sections.includes(section)) { sections.push(section); }
        current = { label: bm[1].trim(), section, codeCount: 0, lineIndex: idx };
        cheats.push(current);
        return;
      }
      if (!current) {
        errors.push(`Line ${idx + 1}: code "${code}" before any [cheat] header.`);
        return;
      }
      if (!CODE_RE.test(code)) {
        errors.push(`Line ${idx + 1}: "${code}" is not a valid code line (hex digits plus ? or * placeholders).`);
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
  readonly highlightDisabled = computed(() => this.editorText().length > MAX_HIGHLIGHTED_CHEAT_TEXT_CHARS);
  readonly highlightedHtml = computed(() => this.highlightDisabled() ? '' : this.renderHighlightedText(this.editorText()));
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
            this.clearEditorSelection();
          }
          break;
        }
        case 'cheatBuildText':
          this.selectedBuildId.set((data['cheatListId'] as number) ?? null);
          this.editorText.set((data['text'] as string) ?? '');
          this.loadedText.set((data['text'] as string) ?? '');
          this.scheduleVisibleAddressLinkRefresh();
          this.postDebuggerCheats();
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
    this.clearEditorSelection();
  }

  selectGame(g: CheatGame): void {
    this.selectedGameId.set(g.gameId);
    this.gameSearch.set('');
    this.builds.set([]);
    this.clearEditorSelection();
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

  refreshArchive(): void {
    this.busy.set(true);
    this.vscode.postMessage({ type: 'refreshArchive' });
  }

  bulkImport(): void {
    this.busy.set(true);
    this.vscode.postMessage({ type: 'bulkImport' });
  }

  // ── Editor ─────────────────────────────────────────────────────────────

  onEditorInput(e: Event): void {
    this.editorText.set((e.target as HTMLTextAreaElement).value);
    this.scheduleDebuggerPost();
    this.scheduleVisibleAddressLinkRefresh();
    queueMicrotask(() => this.syncEditorScroll());
  }

  onEditorClick(event: Event): void {
    if (!(event instanceof MouseEvent)) { return; }
    const token = this.cheatAddressTokenAtCaret();
    if (token) { this.gotoCheatAddress(token); }
  }

  syncEditorScroll(): void {
    const editor = this.editorRef()?.nativeElement;
    const highlighter = this.highlighterRef()?.nativeElement;
    if (!editor) { return; }
    if (highlighter) {
      highlighter.scrollTop = editor.scrollTop;
      highlighter.scrollLeft = editor.scrollLeft;
    }
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
    this.postDebuggerCheats();
  }

  onDebugViewToggle(e: Event): void {
    const enabled = (e.target as HTMLInputElement).checked;
    this.debugViewEnabled.set(enabled);
    this.postDebuggerCheats();
  }

  private clearEditorSelection(): void {
    this.selectedBuildId.set(null);
    this.editorText.set('');
    this.loadedText.set('');
    this.debugViewEnabled.set(false);
    this.visibleAddressLinks.set([]);
    this.postDebuggerCheats(false, true);
  }

  private scheduleDebuggerPost(): void {
    if (!this.debugViewEnabled()) { return; }
    if (this.debuggerPostTimer !== undefined) { clearTimeout(this.debuggerPostTimer); }
    this.debuggerPostTimer = setTimeout(() => {
      this.debuggerPostTimer = undefined;
      this.postDebuggerCheats();
    }, 250);
  }

  private postDebuggerCheats(forceEnabled?: boolean, silent = false): void {
    const enabled = forceEnabled ?? this.debugViewEnabled();
    const build = this.selectedBuild();
    this.vscode.postMessage({
      type: 'viewInDebugger',
      enabled: enabled && !!build,
      cheatListId: build?.id ?? this.selectedBuildId(),
      title: build?.title ?? '',
      text: enabled && build ? this.editorText() : '',
      silent,
    });
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
    this.syncEditorScroll();
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


  private gotoCheatAddress(token: string): void {
    this.vscode.postMessage({ type: 'gotoCheatAddress', addressToken: token });
  }

  private cheatAddressTokenAtCaret(): string | null {
    const ta = this.editorRef()?.nativeElement;
    if (!ta || !this.selectedBuildId() || ta.selectionStart !== ta.selectionEnd) { return null; }

    const metrics = this.editorMetrics(ta);
    if (!metrics) { return null; }

    const rect = ta.getBoundingClientRect();
    const x = event.clientX - rect.left + ta.scrollLeft - metrics.paddingLeft;
    const y = event.clientY - rect.top + ta.scrollTop - metrics.paddingTop;
    if (x < 0 || y < 0) { return null; }

    const lines = this.editorText().split(/\r?\n/);
    const lineIndex = Math.floor(y / metrics.lineHeight);
    if (lineIndex < 0 || lineIndex >= lines.length) { return null; }

    const column = Math.floor(x / metrics.charWidth);
    let lineStart = 0;
    for (let i = 0; i < lineIndex; i++) {
      lineStart += lines[i].length + 1;
    }

    const hit = this.firstCheatAddressTokenOnLine(lineStart, lineStart + lines[lineIndex].length);
    if (!hit) { return null; }

    const tokenStartColumn = hit.start - lineStart;
    const tokenEndColumn = hit.end - lineStart;
    return column >= tokenStartColumn && column <= tokenEndColumn ? hit.token : null;
  }

  private editorMetrics(ta: HTMLTextAreaElement): {
    lineHeight: number;
    charWidth: number;
    paddingLeft: number;
    paddingTop: number;
  } | null {
    const style = getComputedStyle(ta);
    const fontSize = parseFloat(style.fontSize);
    const lineHeight = style.lineHeight === 'normal'
      ? fontSize * 1.5
      : parseFloat(style.lineHeight);
    if (!Number.isFinite(lineHeight) || lineHeight <= 0) { return null; }

    if (!this.measureCanvas) { this.measureCanvas = document.createElement('canvas'); }
    const ctx = this.measureCanvas.getContext('2d');
    if (!ctx) { return null; }
    ctx.font = style.font;
    const charWidth = ctx.measureText('0').width;
    if (!Number.isFinite(charWidth) || charWidth <= 0) { return null; }

    return {
      lineHeight,
      charWidth,
      paddingLeft: parseFloat(style.paddingLeft) || 0,
      paddingTop: parseFloat(style.paddingTop) || 0,
    };
  }

  private firstCheatAddressTokenOnLine(lineStart: number, lineEnd: number): { token: string; start: number; end: number } | null {
    const text = this.editorText();
    const caret = ta.selectionStart;
    const caretLineStart = text.lastIndexOf('\n', Math.max(0, caret - 1)) + 1;
    const nextNewline = text.indexOf('\n', caret);
    let lineEnd = nextNewline < 0 ? text.length : nextNewline;
    if (lineEnd > caretLineStart && text[lineEnd - 1] === '\r') { lineEnd--; }

    const code = this.stripComments(line, false).code;
    const first = code.match(/^\s*([0-9A-Fa-f]{8})(?=\s|$)/);
    if (!first?.[1]) { return null; }

  private scheduleVisibleAddressLinkRefresh(): void {
    if (this.addressLinkFrame !== undefined) { cancelAnimationFrame(this.addressLinkFrame); }
    this.addressLinkFrame = requestAnimationFrame(() => {
      this.addressLinkFrame = undefined;
      this.refreshVisibleAddressLinks();
    });
  }

  private refreshVisibleAddressLinks(): void {
    const ta = this.editorRef()?.nativeElement;
    if (!ta || !this.selectedBuildId()) {
      this.visibleAddressLinks.set([]);
      return;
    }

    const metrics = this.editorMetrics(ta);
    if (!metrics) {
      this.visibleAddressLinks.set([]);
      return;
    }

    const text = this.editorText();
    const lineStarts = this.getLineStarts(text);
    const firstLine = Math.max(0, Math.floor(ta.scrollTop / metrics.lineHeight) - ADDRESS_LINK_VERTICAL_BUFFER_LINES);
    const lastLine = Math.min(
      lineStarts.length - 1,
      Math.ceil((ta.scrollTop + ta.clientHeight) / metrics.lineHeight) + ADDRESS_LINK_VERTICAL_BUFFER_LINES,
    );
    const links: VisibleAddressLink[] = [];

    for (let lineIndex = firstLine; lineIndex <= lastLine; lineIndex++) {
      const visibleLineStart = lineStarts[lineIndex];
      const nextLineStart = lineStarts[lineIndex + 1] ?? text.length + 1;
      let lineEnd = Math.min(text.length, nextLineStart - 1);
      if (lineEnd > visibleLineStart && text[lineEnd - 1] === '\r') { lineEnd--; }

      const hit = this.firstCheatAddressTokenOnLine(text.slice(visibleLineStart, lineEnd), visibleLineStart);
      if (!hit) { continue; }

      const column = hit.start - visibleLineStart;
      const top = (lineIndex * metrics.lineHeight) - ta.scrollTop + metrics.paddingTop;
      const left = (column * metrics.charWidth) - ta.scrollLeft + metrics.paddingLeft;
      const width = (hit.end - hit.start) * metrics.charWidth;
      if (top + metrics.lineHeight < 0 || top > ta.clientHeight || left + width < 0 || left > ta.clientWidth) { continue; }

      links.push({
        key: `${lineIndex}:${hit.token}:${hit.start}`,
        token: hit.token,
        top,
        left,
        width,
        height: metrics.lineHeight,
      });
    }

    this.visibleAddressLinks.set(links);
  }

  private getLineStarts(text: string): number[] {
    if (text === this.lineStartCacheText) { return this.lineStartCache; }

    const starts = [0];
    for (let i = 0; i < text.length; i++) {
      if (text.charCodeAt(i) === 10) { starts.push(i + 1); }
    }
    this.lineStartCacheText = text;
    this.lineStartCache = starts;
    return starts;
  }

  private editorMetrics(ta: HTMLTextAreaElement): {
    lineHeight: number;
    charWidth: number;
    paddingLeft: number;
    paddingTop: number;
  } | null {
    const style = getComputedStyle(ta);
    const fontSize = parseFloat(style.fontSize);
    const lineHeight = style.lineHeight === 'normal'
      ? fontSize * 1.5
      : parseFloat(style.lineHeight);
    if (!Number.isFinite(lineHeight) || lineHeight <= 0) { return null; }

    if (!this.measureCanvas) { this.measureCanvas = document.createElement('canvas'); }
    const ctx = this.measureCanvas.getContext('2d');
    if (!ctx) { return null; }
    ctx.font = style.font;
    const charWidth = ctx.measureText('0').width;
    if (!Number.isFinite(charWidth) || charWidth <= 0) { return null; }

    return {
      lineHeight,
      charWidth,
      paddingLeft: parseFloat(style.paddingLeft) || 0,
      paddingTop: parseFloat(style.paddingTop) || 0,
    };
  }

  private firstCheatAddressTokenOnLine(line: string, absoluteLineStart: number): { token: string; start: number; end: number } | null {
    let i = 0;
    while (i < line.length && /\s/.test(line[i])) { i++; }
    if (line.startsWith('//', i) || line.startsWith('/*', i)) { return null; }

    const startColumn = i;
    while (i < line.length && /[0-9A-Fa-f?*]/.test(line[i])) { i++; }
    const token = line.slice(startColumn, i);
    if (!/^[0-9A-Fa-f]{8}$/.test(token)) { return null; }
    if (i < line.length && !/\s/.test(line[i])) { return null; }

    return {
      token: token.toUpperCase(),
      start: absoluteLineStart + startColumn,
      end: absoluteLineStart + i,
    };
  }

  // ── Syntax highlighting / comments ──────────────────────────────────────

  private stripComments(raw: string, inBlockComment: boolean): { code: string; inBlockComment: boolean } {
    let code = '';
    for (let i = 0; i < raw.length;) {
      if (inBlockComment) {
        const end = raw.indexOf('*/', i);
        if (end < 0) { return { code, inBlockComment: true }; }
        i = end + 2;
        inBlockComment = false;
        continue;
      }

      if (raw.startsWith('//', i)) { break; }
      if (raw.startsWith('/*', i)) {
        inBlockComment = true;
        i += 2;
        continue;
      }

      code += raw[i];
      i++;
    }
    return { code, inBlockComment };
  }

  private renderHighlightedText(text: string): string {
    const lines = text.split(/\r?\n/);
    let inBlockComment = false;
    return lines.map(line => {
      const rendered = this.renderHighlightedLine(line, inBlockComment);
      inBlockComment = rendered.inBlockComment;
      return rendered.html || '&nbsp;';
    }).join('\n');
  }

  private renderHighlightedLine(raw: string, inBlockComment: boolean): { html: string; inBlockComment: boolean } {
    let html = '';
    let codeText = '';

    const flushCode = (): void => {
      if (!codeText) { return; }
      html += this.renderCodeSegment(codeText);
      codeText = '';
    };

    for (let i = 0; i < raw.length;) {
      if (inBlockComment) {
        const end = raw.indexOf('*/', i);
        const comment = end < 0 ? raw.slice(i) : raw.slice(i, end + 2);
        html += `<span class="ce-syn-comment">${this.escapeHtml(comment)}</span>`;
        if (end < 0) { return { html, inBlockComment: true }; }
        i = end + 2;
        inBlockComment = false;
        continue;
      }

      if (raw.startsWith('//', i)) {
        flushCode();
        html += `<span class="ce-syn-comment">${this.escapeHtml(raw.slice(i))}</span>`;
        break;
      }

      if (raw.startsWith('/*', i)) {
        flushCode();
        const end = raw.indexOf('*/', i + 2);
        const comment = end < 0 ? raw.slice(i) : raw.slice(i, end + 2);
        html += `<span class="ce-syn-comment">${this.escapeHtml(comment)}</span>`;
        if (end < 0) { return { html, inBlockComment: true }; }
        i = end + 2;
        continue;
      }

      codeText += raw[i];
      i++;
    }

    flushCode();
    return { html, inBlockComment };
  }

  private renderCodeSegment(segment: string): string {
    const trimmed = segment.trim();
    if (!trimmed) { return this.escapeHtml(segment); }

    const section = SECTION_RE.exec(trimmed);
    if (section) {
      return this.wrapWithOriginalSpacing(segment, `<span class="ce-syn-section">${this.escapeHtml(trimmed)}</span>`);
    }

    const header = BRACKET_RE.exec(trimmed);
    if (header) {
      return this.wrapWithOriginalSpacing(segment, `<span class="ce-syn-label">${this.escapeHtml(trimmed)}</span>`);
    }

    if (CODE_RE.test(trimmed)) {
      return this.renderCodeLineSegment(segment);
    }

    return this.escapeHtml(segment);
  }

  private renderCodeLineSegment(segment: string): string {
    let tokenIndex = 0;
    return (segment.match(CODE_TOKEN_RE) ?? []).map(token => {
      if (/^\s+$/.test(token)) { return this.escapeHtml(token); }
      if (!/^[0-9A-Fa-f?*]+$/.test(token)) { return this.escapeHtml(token); }
      const rendered = this.renderCodeToken(token, tokenIndex);
      tokenIndex++;
      return rendered;
    }).join('');
  }

  private renderCodeToken(token: string, index: number): string {
    if (index === 0) {
      const rendered = this.span('ce-syn-command', token.slice(0, 2)) +
        this.span('ce-syn-address', token.slice(2));
      if (/^[0-9A-Fa-f]{8}$/.test(token)) {
        return `<span class="ce-cheat-address-link" title="Go to 0x${this.escapeHtml(token)} in debugger">${rendered}</span>`;
      }
      return rendered;
    }

    if (index === 1) {
      return this.span('ce-syn-data-hi', token.slice(0, 4)) +
        this.span('ce-syn-data-lo', token.slice(4));
    }

    return this.span('ce-syn-address', token);
  }

  private wrapWithOriginalSpacing(original: string, inner: string): string {
    const prefix = original.match(/^\s*/)?.[0] ?? '';
    const suffix = original.match(/\s*$/)?.[0] ?? '';
    return this.escapeHtml(prefix) + inner + this.escapeHtml(suffix);
  }

  private span(cls: string, text: string): string {
    return text ? `<span class="${cls}">${this.escapeHtml(text)}</span>` : '';
  }

  private escapeHtml(text: string): string {
    return text
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');
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
