import {
    ChangeDetectionStrategy,
    Component,
    AfterViewInit,
    OnDestroy,
    ViewEncapsulation,
    NgZone,
    inject,
    signal,
    computed,
    effect,
    viewChild,
    ElementRef,
    WritableSignal,
} from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import { PageHeaderComponent } from '../shared/page-header.component';
import {
    ButtonName, BUTTON_LABEL, JokerCandidate, Polarity, WrapFormat,
    buildJokerCode, buttonsFromPadWord, comboLabel, decodeJoker, encodeJoker,
    findJokerCandidates, hex, parseAddress, parseCodeLines, sortButtons,
} from './joker-codes';
import '../shared/styles/shared.css';
import './joker-maker-page.css';

// Geometry
// Coordinates match the draft controller image at 1000x750 reference size.
// Hit regions are invisible overlays on top of the embedded image.

const CANVAS_W = 1000;
const CANVAS_H = 750;

interface RectButton { name: ButtonName; kind: 'rect'; x: number; y: number; w: number; h: number; }
interface CircleButton { name: ButtonName; kind: 'circle'; x: number; y: number; r: number; }
type Button = RectButton | CircleButton;

interface Stick {
    name:        'leftStick' | 'rightStick';
    x:           number;
    y:           number;
    hitR:        number;
    maxOffset:   number;
    capR:        number;
    pressButton: ButtonName; // l3 / r3
}

// The PS button is not part of the pad word (no joker bit, no PINE input),
// so it has no hit region.
const BUTTONS: readonly Button[] = [
    { name: 'l2', kind: 'rect', x: 186, y: 84,  w: 104, h: 36 },
    { name: 'l1', kind: 'rect', x: 178, y: 120, w: 127, h: 52 },
    { name: 'r2', kind: 'rect', x: 709, y: 84,  w: 104, h: 36 },
    { name: 'r1', kind: 'rect', x: 695, y: 120, w: 127, h: 52 },

    { name: 'up',    kind: 'rect', x: 204, y: 198, w: 60, h: 70 },
    { name: 'down',  kind: 'rect', x: 204, y: 314, w: 60, h: 70 },
    { name: 'left',  kind: 'rect', x: 148, y: 254, w: 70, h: 56 },
    { name: 'right', kind: 'rect', x: 250, y: 254, w: 70, h: 56 },

    { name: 'select', kind: 'rect', x: 388, y: 252, w: 36, h: 30 },
    { name: 'start',  kind: 'rect', x: 546, y: 252, w: 40, h: 38 },

    { name: 'triangle', kind: 'circle', x: 760, y: 207, r: 40 },
    { name: 'square',   kind: 'circle', x: 680, y: 290, r: 40 },
    { name: 'circle',   kind: 'circle', x: 840, y: 290, r: 40 },
    { name: 'cross',    kind: 'circle', x: 760, y: 373, r: 40 },
];

const STICKS: readonly Stick[] = [
    { name: 'leftStick',  x: 363, y: 425, hitR: 80, maxOffset: 32, capR: 50, pressButton: 'l3' },
    { name: 'rightStick', x: 638, y: 425, hitR: 80, maxOffset: 32, capR: 50, pressButton: 'r3' },
];

/** Glow colour for pressed buttons. */
const GLOW: Record<ButtonName, string> = {
    triangle: '#4ec9b0', circle: '#f48771', cross: '#569cd6', square: '#c586c0',
    start: '#9cdcfe', select: '#9cdcfe',
    l1: '#569cd6', l2: '#569cd6', r1: '#569cd6', r2: '#569cd6',
    up: '#dcdcaa', down: '#dcdcaa', left: '#dcdcaa', right: '#dcdcaa',
    l3: '#c586c0', r3: '#c586c0',
};
const LIVE_GLOW = '#ffb454';

/** Webview keyboard shortcuts for driving the virtual controller. */
const KEY_MAP: Record<string, ButtonName> = {
    ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right',
    KeyW: 'triangle', KeyA: 'square', KeyD: 'circle', KeyS: 'cross',
    KeyQ: 'l1', Digit1: 'l2', KeyE: 'r1', Digit3: 'r2',
    KeyZ: 'l3', KeyC: 'r3',
    Enter: 'start', ShiftRight: 'select',
};

/** Build: clicks toggle buttons into a joker combo. Play: momentary input sent to PCSX2. */
type Mode = 'build' | 'play';
type PolaritySetting = 'auto' | Polarity;

interface LogEntry { id: number; kind: 'press' | 'release' | 'stick' | 'live' | 'capture'; name: string; t: number; }

type ActivePointer =
    | { kind: 'button'; name: ButtonName }
    | { kind: 'stick'; stick: Stick }
    | { kind: 'stickPress'; stick: Stick };

type GameStatus = 'offline' | 'loading' | 'ok' | 'error';

/** Per-viewer settings kept across reloads via the webview state. */
interface SavedState {
    mode?:          Mode;
    combo?:         ButtonName[];
    wrapText?:      string;
    format?:        WrapFormat;
    polarity?:      PolaritySetting;
    swap?:          boolean;
    addressChoice?: string;
    customAddress?: string;
    sideCollapsed?: boolean;
}

interface InitMsg            { type: 'jokerInit'; controllerImage: string; }
interface ConnectionMsg      { type: 'jokerConnectionStatus'; connected: boolean; }
interface LivePadStateMsg    { type: 'jokerLivePadState'; buttons: number;
                               lx: number; ly: number; rx: number; ry: number; }
interface LivePollStoppedMsg { type: 'jokerLivePollStopped'; }
interface GameContextMsg     { type: 'jokerGameContext'; status: GameStatus; game?: string; serial?: string;
                               cheats?: Array<{ name: string; codes: string[] }>; error?: string; }
type IncomingMsg = InitMsg | ConnectionMsg | LivePadStateMsg | LivePollStoppedMsg | GameContextMsg;

/** Address select value meaning "type an address". */
const CUSTOM = 'custom';

@Component({
    selector: 'app-joker-maker-page',
    standalone: true,
    imports: [PageHeaderComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    encapsulation: ViewEncapsulation.None,
    template: `
<div class="jm-host">
  <app-page-header icon="gamepad2" title="Controller" subtitle="Joker code builder and virtual PS2 controller">
    <div page-header-actions class="jm-header-actions">
      <span class="jm-connection" [class.jm-connection-on]="connected()">{{ connected() ? 'connected' : 'offline' }}</span>
      <button class="lm-tool-btn" type="button" (click)="toggleSidePanel()" [attr.aria-expanded]="!sideCollapsed()">
        {{ sideCollapsed() ? 'Show panel' : 'Hide panel' }}
      </button>
    </div>
  </app-page-header>

  <div class="jm-main">

    <div class="jm-stage">
      <div class="jm-stage-bar">
        <div class="jm-seg" role="tablist" aria-label="Mode">
          <button type="button" role="tab" [class.jm-seg-on]="mode() === 'build'" [attr.aria-selected]="mode() === 'build'"
                  (click)="setMode('build')" title="Click buttons to build a joker combo. Nothing is sent to PCSX2.">Build code</button>
          <button type="button" role="tab" [class.jm-seg-on]="mode() === 'play'" [attr.aria-selected]="mode() === 'play'"
                  (click)="setMode('play')" title="Hold buttons and drag sticks to send input to PCSX2.">Play</button>
        </div>
        <label class="jm-toggle" [class.jm-toggle-dim]="!connected()" title="Mirror the real controller via PINE (orange).">
          <input type="checkbox" [checked]="liveEnabled()" [disabled]="!connected()"
                 (change)="setLiveEnabled($any($event.target).checked)" />
          Live pad
          <span class="jm-live-dot" [class.jm-live-dot-on]="liveActive()"></span>
        </label>
      </div>

      <canvas #canvas
              [attr.width]="canvasW"
              [attr.height]="canvasH"
              aria-label="PS2 controller"></canvas>

      <div class="jm-hint">
        @if (mode() === 'build') {
          Click buttons or press keys to toggle them into the combo. Click a stick for L3 / R3.
        } @else {
          Hold buttons or keys to press them in PCSX2. Drag sticks; right-click or Shift+click a stick for L3 / R3.
          @if (!connected()) { <span class="jm-warn">Not connected: input is not sent.</span> }
        }
        <span class="jm-keys">Keys: arrows &middot; W A S D &middot; Q E 1 3 &middot; Z C &middot; Enter &middot; Right Shift</span>
      </div>
    </div>

    @if (!sideCollapsed()) {
    <aside class="jm-side">

      <!-- Combo -->
      <section class="jm-card">
        <div class="jm-card-head">
          <span class="jm-card-title">Button combo</span>
          <span class="jm-card-tools">
            <button class="lm-row-btn" type="button" (click)="toggleCapture()" [disabled]="!connected()"
                    [class.jm-capturing]="captureArmed()"
                    title="Press and release a combo on the real controller to record it.">
              {{ captureArmed() ? 'Waiting for pad... (cancel)' : 'Capture from pad' }}
            </button>
            <button class="lm-row-btn" type="button" (click)="clearCombo()" [disabled]="combo().length === 0">Clear</button>
          </span>
        </div>
        <div class="jm-chips">
          @for (b of combo(); track b) {
            <button class="jm-chip" type="button" (click)="toggleCombo(b)" title="Remove {{ label(b) }}">
              {{ label(b) }} <span aria-hidden="true">&times;</span>
            </button>
          } @empty {
            <span class="lm-muted">Click buttons on the controller, or capture them from the pad.</span>
          }
        </div>
        <div class="jm-kv">
          <span>Value</span><span class="lm-mono">{{ valueHex() }}</span>
          <span>Pad word</span><span class="lm-mono">0x{{ padWordHex() }}</span>
        </div>
        @if (conflicts().length) {
          <div class="jm-note jm-note-warn">Already used by: {{ conflicts().join(', ') }}</div>
        }
      </section>

      <!-- Joker address -->
      <section class="jm-card">
        <div class="jm-card-head">
          <span class="jm-card-title">Joker address</span>
          <span class="jm-card-tools">
            <button class="lm-row-btn" type="button" (click)="refreshGame()" [disabled]="!connected() || gameStatus() === 'loading'">Refresh</button>
          </span>
        </div>
        <div class="jm-game">
          @switch (gameStatus()) {
            @case ('ok')      { <span class="jm-game-title">{{ gameTitle() || 'Unknown game' }}</span> <span class="lm-mono">{{ gameSerial() }}</span> }
            @case ('loading') { <span class="lm-muted">Reading the game's cheats...</span> }
            @case ('error')   { <span class="jm-err">{{ gameError() }}</span> }
            @default          { <span class="lm-muted">Connect to PCSX2 to find the game's joker address.</span> }
          }
        </div>
        @if (gameStatus() === 'ok' && candidates().length === 0) {
          <div class="jm-note">No joker codes found in this game's archive cheats. Enter the pad address yourself.</div>
        }
        <div class="jm-field">
          @if (candidates().length) {
            <select class="jm-input" [value]="addressSelectValue()" (change)="setAddressChoice($any($event.target).value)">
              @for (c of candidates(); track c.address) {
                <option [value]="addrKey(c.address)">{{ addrLabel(c) }}</option>
              }
              <option [value]="CUSTOM">Custom address...</option>
            </select>
          }
          @if (showCustomAddress()) {
            <input class="jm-input lm-mono" type="text" spellcheck="false" placeholder="e.g. 003D4F4C"
                   [value]="customAddress()" (input)="setCustomAddress($any($event.target).value)"
                   [class.jm-input-bad]="customAddress().trim() !== '' && address() === null" />
          }
        </div>
        <div class="jm-field jm-field-row">
          <label class="jm-field-label" for="jm-pol">Stored as</label>
          <select id="jm-pol" class="jm-input" [value]="polaritySetting()" (change)="setPolarity($any($event.target).value)">
            <option value="auto">Auto ({{ polarityName(detectedPolarity()) }})</option>
            <option value="low">Active-low (pressed = 0)</option>
            <option value="high">Active-high (pressed = 1)</option>
          </select>
          <label class="jm-toggle" title="Some games keep the pad word byte-swapped.">
            <input type="checkbox" [checked]="swap()" (change)="setSwap($any($event.target).checked)" />
            Swap bytes
          </label>
        </div>
      </section>

      <!-- Output -->
      <section class="jm-card">
        <div class="jm-card-head">
          <span class="jm-card-title">Code</span>
          <span class="jm-card-tools">
            <select class="jm-input jm-input-sm" [value]="format()" (change)="setFormat($any($event.target).value)"
                    title="How to gate more than one line">
              <option value="auto">One E line for the block</option>
              <option value="each">D line before each line</option>
            </select>
          </span>
        </div>
        <textarea class="jm-input jm-textarea lm-mono" spellcheck="false" rows="4"
                  placeholder="Codes to activate with this combo (optional)&#10;e.g. 2034A1B0 00000063"
                  [value]="wrapText()" (input)="setWrapText($any($event.target).value)"></textarea>
        @if (ignoredLines() > 0) {
          <div class="jm-note">{{ ignoredLines() }} line(s) are not codes and were left out.</div>
        }
        <pre class="jm-output lm-mono">{{ outputText() }}</pre>
        <div class="jm-actions">
          <button class="jm-primary-btn" type="button" (click)="copyCode()" [disabled]="combo().length === 0">{{ copyCodeLabel() }}</button>
          <button class="lm-tool-btn" type="button" (click)="copyJson()">{{ copyJsonLabel() }}</button>
        </div>
      </section>

      <!-- Combos already in the archive -->
      @if (uses().length) {
        <section class="jm-card">
          <div class="jm-card-head">
            <span class="jm-card-title">Combos in use ({{ uses().length }})</span>
          </div>
          <div class="jm-uses">
            @for (u of uses(); track u.cheat + u.value) {
              <button class="jm-use" type="button" [class.jm-use-match]="u.value === value() && combo().length > 0"
                      (click)="loadCombo(u.buttons)" title="Load this combo">
                <span class="jm-use-combo">{{ u.combo }}</span>
                <span class="jm-use-cheat">{{ u.cheat }}</span>
              </button>
            }
          </div>
        </section>
      }

      <!-- Event log -->
      <section class="jm-card">
        <div class="jm-card-head">
          <span class="jm-card-title">Input events</span>
          <span class="jm-card-tools">
            <button class="lm-row-btn" type="button" (click)="logEntries.set([])" [disabled]="logEntries().length === 0">Clear</button>
          </span>
        </div>
        <div class="jm-log">
          @for (e of logEntries(); track e.id) {
            <div class="jm-log-row">
              <span class="t">{{ fmtTime(e.t) }}</span>
              <span class="{{ e.kind }}">{{ e.kind }}</span>
              <span class="name">{{ e.name }}</span>
            </div>
          } @empty {
            <div class="lm-muted">No events yet.</div>
          }
        </div>
      </section>

    </aside>
    }
  </div>

  <div class="jm-statusbar">
    <span>{{ mode() === 'build' ? 'Build code' : 'Play' }}</span>
    <span>{{ comboText() }}</span>
    <span class="jm-status-right">{{ statusCode() }}</span>
  </div>
</div>
`,
})
export class JokerMakerPageComponent implements AfterViewInit, OnDestroy {

    readonly canvasW = CANVAS_W;
    readonly canvasH = CANVAS_H;
    readonly CUSTOM = CUSTOM;

    private readonly canvasRef = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
    private readonly zone = inject(NgZone);
    private readonly vscode = getVsCodeApi();
    private readonly saved = (this.vscode.getState() as SavedState | undefined) ?? {};

    // Reactive state
    readonly mode          = signal<Mode>(this.saved.mode ?? 'build');
    /** Build-mode joker combo. */
    readonly combo         = signal<ButtonName[]>(sortButtons(this.saved.combo ?? []));
    /** Play-mode buttons currently held. */
    readonly held          = signal<ButtonName[]>([]);
    readonly leftStick     = signal({ x: 0, y: 0 });
    readonly rightStick    = signal({ x: 0, y: 0 });
    readonly logEntries    = signal<LogEntry[]>([]);
    readonly liveEnabled   = signal(false);
    readonly liveActive    = signal(false);
    readonly connected     = signal(false);
    /** Live pad word (0xFFFF = nothing pressed) and sticks - valid while liveActive. */
    readonly livePadButtons = signal(0xFFFF);
    readonly liveSticks    = signal({ lx: 0x7F, ly: 0x7F, rx: 0x7F, ry: 0x7F });
    readonly captureArmed  = signal(false);
    readonly copyJsonLabel = signal('Copy JSON');
    readonly copyCodeLabel = signal('Copy code');
    readonly sideCollapsed = signal(this.saved.sideCollapsed ?? false);

    readonly gameStatus    = signal<GameStatus>('offline');
    readonly gameTitle     = signal('');
    readonly gameSerial    = signal('');
    readonly gameError     = signal('');
    readonly candidates    = signal<JokerCandidate[]>([]);
    readonly addressChoice = signal(this.saved.addressChoice ?? '');
    readonly customAddress = signal(this.saved.customAddress ?? '');
    readonly polaritySetting = signal<PolaritySetting>(this.saved.polarity ?? 'auto');
    readonly swap          = signal(this.saved.swap ?? false);
    readonly wrapText      = signal(this.saved.wrapText ?? '');
    readonly format        = signal<WrapFormat>(this.saved.format ?? 'auto');

    /** Candidate the address select points at (null when custom or none). */
    readonly selectedCandidate = computed<JokerCandidate | null>(() => {
        const list = this.candidates();
        const choice = this.addressChoice();
        if (choice === CUSTOM || list.length === 0) { return null; }
        return list.find(c => this.addrKey(c.address) === choice) ?? list[0];
    });

    readonly showCustomAddress = computed(() => this.candidates().length === 0 || this.addressChoice() === CUSTOM);
    readonly addressSelectValue = computed(() => {
        const c = this.selectedCandidate();
        return c ? this.addrKey(c.address) : CUSTOM;
    });

    readonly address = computed<number | null>(() => {
        const c = this.selectedCandidate();
        return c ? c.address : parseAddress(this.customAddress());
    });

    readonly detectedPolarity = computed<Polarity>(() => this.selectedCandidate()?.polarity ?? 'low');
    readonly polarity = computed<Polarity>(() => {
        const p = this.polaritySetting();
        return p === 'auto' ? this.detectedPolarity() : p;
    });
    private readonly encoding = computed(() => ({ polarity: this.polarity(), swap: this.swap() }));

    readonly value      = computed(() => encodeJoker(this.combo(), this.encoding()));
    readonly valueHex   = computed(() => '0000' + hex(this.value(), 4));
    /** The combo as the PS2 wire word (active-low), independent of the game's encoding. */
    readonly padWordHex = computed(() => hex(encodeJoker(this.combo(), { polarity: 'low', swap: false }), 4));

    private readonly bodyLines = computed(() => parseCodeLines(this.wrapText()));
    readonly ignoredLines = computed(() =>
        this.wrapText().split(/\r?\n/).filter(l => l.replace(/\/\/.*$|#.*$/, '').trim() !== '').length
        - this.bodyLines().length);

    readonly outputLines = computed<string[] | null>(() => {
        const addr = this.address();
        if (addr === null) { return null; }
        return buildJokerCode(addr, this.value(), this.bodyLines(), this.format());
    });
    readonly outputText = computed(() => {
        if (this.combo().length === 0) { return '// pick a button combo'; }
        return this.outputLines()?.join('\n') ?? `// joker value ${this.valueHex()}\n// pick or enter a joker address for the full code`;
    });

    /** Archive cheats gated by the selected joker address, decoded with the current encoding. */
    readonly uses = computed(() => {
        const c = this.selectedCandidate();
        if (!c) { return []; }
        const enc = this.encoding();
        return c.uses
            .map(u => {
                const buttons = decodeJoker(u.value, enc);
                return { cheat: u.cheat, value: u.value, buttons, combo: comboLabel(buttons) };
            })
            .sort((a, b) => a.combo.localeCompare(b.combo) || a.cheat.localeCompare(b.cheat));
    });

    readonly conflicts = computed(() => {
        if (this.combo().length === 0) { return []; }
        const v = this.value();
        return [...new Set(this.uses().filter(u => u.value === v).map(u => u.cheat))];
    });

    readonly comboText  = computed(() => comboLabel(this.mode() === 'build' ? this.combo() : this.held()));
    readonly statusCode = computed(() => {
        const lines = this.outputLines();
        return this.combo().length === 0 ? '' : lines ? lines[0] : this.valueHex();
    });

    // Non-reactive scratch state
    private ctx:   CanvasRenderingContext2D | null = null;
    private img:   HTMLImageElement | null = null;
    private imgReady = false;
    private hoverTarget: { type: 'button'; button: Button } | { type: 'stick'; stick: Stick } | null = null;
    private readonly activePointers = new Map<number, ActivePointer>();
    /** Play-mode buttons held down by keyboard keys (released on blur). */
    private readonly keysHeld = new Set<ButtonName>();
    /** Union of live buttons seen while a capture is armed. */
    private captureUnion = new Set<ButtonName>();
    private logSeq = 0;

    private readonly messageListener = (event: MessageEvent) => this.onMessage(event);
    private readonly keydownHandler = (e: KeyboardEvent) => this.onKey(e, true);
    private readonly keyupHandler   = (e: KeyboardEvent) => this.onKey(e, false);
    private readonly blurHandler    = () => this.zone.run(() => this.releaseKeyboard());
    /** Coalesces repeat virtual-state posts. */
    private virtualStatePending = false;
    /** Set once Play-mode input happens, so opening the page never clears the emulator pad. */
    private virtualControllerArmed = false;

    constructor() {
        // Every time the rendered state changes, repaint the canvas.
        effect(() => {
            this.mode();
            this.combo();
            this.held();
            this.leftStick();
            this.rightStick();
            this.livePadButtons();
            this.liveSticks();
            this.liveActive();
            this.draw();
        });

        // Play-mode input goes to the host as it changes.
        effect(() => {
            this.held();
            this.leftStick();
            this.rightStick();
            if (this.mode() === 'play') { this.scheduleVirtualStatePost(); }
        });

        effect(() => {
            const state: SavedState = {
                mode:          this.mode(),
                combo:         this.combo(),
                wrapText:      this.wrapText(),
                format:        this.format(),
                polarity:      this.polaritySetting(),
                swap:          this.swap(),
                addressChoice: this.addressChoice(),
                customAddress: this.customAddress(),
                sideCollapsed: this.sideCollapsed(),
            };
            this.vscode.setState(state);
        });
    }

    ngAfterViewInit(): void {
        const canvas = this.canvasRef().nativeElement;
        this.ctx = canvas.getContext('2d');
        this.attachPointer(canvas);
        window.addEventListener('keydown', this.keydownHandler);
        window.addEventListener('keyup',   this.keyupHandler);
        window.addEventListener('blur',    this.blurHandler);
        window.addEventListener('message', this.messageListener);
        this.vscode.postMessage({ type: 'ready' });
    }

    ngOnDestroy(): void {
        window.removeEventListener('message', this.messageListener);
        window.removeEventListener('keydown', this.keydownHandler);
        window.removeEventListener('keyup',   this.keyupHandler);
        window.removeEventListener('blur',    this.blurHandler);
    }

    // Host messages

    private onMessage(event: MessageEvent): void {
        const msg = event.data as IncomingMsg;
        if (!msg || typeof msg !== 'object') { return; }
        this.zone.run(() => {
            switch (msg.type) {
                case 'jokerInit':
                    this.loadControllerImage(msg.controllerImage);
                    break;
                case 'jokerConnectionStatus':
                    this.connected.set(!!msg.connected);
                    if (!msg.connected) {
                        this.liveActive.set(false);
                        this.liveEnabled.set(false);
                        this.captureArmed.set(false);
                    }
                    break;
                case 'jokerLivePadState':
                    this.onLivePadState(msg);
                    break;
                case 'jokerLivePollStopped':
                    this.liveActive.set(false);
                    this.liveEnabled.set(false);
                    this.captureArmed.set(false);
                    break;
                case 'jokerGameContext':
                    this.onGameContext(msg);
                    break;
            }
        });
    }

    private onGameContext(msg: GameContextMsg): void {
        this.gameStatus.set(msg.status);
        this.gameError.set(msg.error ?? '');
        if (msg.status === 'ok') {
            const serial = msg.serial ?? '';
            // A different game invalidates the remembered address pick.
            if (serial !== this.gameSerial() && this.gameSerial() !== '') { this.addressChoice.set(''); }
            this.gameTitle.set(msg.game ?? '');
            this.gameSerial.set(serial);
            this.candidates.set(findJokerCandidates(msg.cheats ?? []));
        } else if (msg.status !== 'loading') {
            this.candidates.set([]);
        }
    }

    private onLivePadState(msg: LivePadStateMsg): void {
        const word = msg.buttons & 0xFFFF;
        const prev = this.livePadButtons();
        this.liveActive.set(true);
        this.livePadButtons.set(word);
        this.liveSticks.set({ lx: msg.lx, ly: msg.ly, rx: msg.rx, ry: msg.ry });
        if (word === prev) { return; }

        const names = buttonsFromPadWord(word);
        this.logEvent('live', comboLabel(names));

        if (this.captureArmed()) {
            if (names.length) {
                for (const n of names) { this.captureUnion.add(n); }
            } else if (this.captureUnion.size) {
                // Combo released: take everything that was held together.
                this.combo.set(sortButtons(this.captureUnion));
                this.logEvent('capture', comboLabel(this.combo()));
                this.captureUnion.clear();
                this.captureArmed.set(false);
            }
        }
    }

    private loadControllerImage(uri: string): void {
        const img = new Image();
        img.onload = () => {
            this.imgReady = true;
            this.draw();
        };
        img.src = uri;
        this.img = img;
    }

    // Outgoing virtual state

    private scheduleVirtualStatePost(): void {
        if (!this.virtualControllerArmed) { return; }
        if (this.virtualStatePending) { return; }
        this.virtualStatePending = true;
        queueMicrotask(() => {
            this.virtualStatePending = false;
            if (this.mode() !== 'play') { return; }
            this.vscode.postMessage({
                type:       'jokerVirtualState',
                pressed:    this.held(),
                leftStick:  this.leftStick(),
                rightStick: this.rightStick(),
            });
        });
    }

    // UI handlers

    setMode(mode: Mode): void {
        if (this.mode() === mode) { return; }
        // Drop any Play input so nothing stays held in the game.
        this.activePointers.clear();
        this.keysHeld.clear();
        this.held.set([]);
        this.leftStick.set({ x: 0, y: 0 });
        this.rightStick.set({ x: 0, y: 0 });
        this.mode.set(mode);
        if (mode === 'build' && this.virtualControllerArmed) {
            this.virtualControllerArmed = false;
            this.vscode.postMessage({ type: 'jokerReleasePad' });
        }
    }

    toggleSidePanel(): void {
        this.sideCollapsed.update(collapsed => !collapsed);
    }

    setLiveEnabled(on: boolean): void {
        this.liveEnabled.set(on);
        this.vscode.postMessage({ type: 'jokerLiveToggle', enabled: on });
        if (!on) {
            this.liveActive.set(false);
            this.captureArmed.set(false);
        }
    }

    toggleCapture(): void {
        if (this.captureArmed()) {
            this.captureArmed.set(false);
            return;
        }
        this.captureUnion.clear();
        this.captureArmed.set(true);
        if (!this.liveEnabled()) { this.setLiveEnabled(true); }
    }

    clearCombo(): void {
        if (this.combo().length === 0) { return; }
        this.combo.set([]);
        this.logEvent('release', 'all');
    }

    loadCombo(buttons: ButtonName[]): void {
        this.combo.set(sortButtons(buttons));
        if (this.mode() !== 'build') { this.setMode('build'); }
    }

    toggleCombo(name: ButtonName): void {
        const has = this.combo().includes(name);
        this.combo.set(sortButtons(has ? this.combo().filter(n => n !== name) : [...this.combo(), name]));
        this.logEvent(has ? 'release' : 'press', name);
    }

    refreshGame(): void { this.vscode.postMessage({ type: 'jokerRefreshGame' }); }
    setAddressChoice(v: string): void { this.addressChoice.set(v); }
    setCustomAddress(v: string): void { this.customAddress.set(v); }
    setPolarity(v: PolaritySetting): void { this.polaritySetting.set(v); }
    setSwap(on: boolean): void { this.swap.set(on); }
    setWrapText(v: string): void { this.wrapText.set(v); }
    setFormat(v: WrapFormat): void { this.format.set(v); }

    label(b: ButtonName): string { return BUTTON_LABEL[b]; }
    addrKey(address: number): string { return hex(address, 7); }
    addrLabel(c: JokerCandidate): string {
        return `${hex(c.address, 7)}  -  ${c.uses.length} cheat code${c.uses.length === 1 ? '' : 's'}`;
    }
    polarityName(p: Polarity): string { return p === 'low' ? 'active-low' : 'active-high'; }

    copyCode(): void {
        const text = this.outputLines()?.join('\n') ?? this.valueHex();
        this.copy(text, this.copyCodeLabel, 'Copy code');
    }

    copyJson(): void {
        const addr = this.address();
        const text = JSON.stringify({
            combo:      this.combo(),
            value:      this.valueHex(),
            padWord:    '0x' + this.padWordHex(),
            address:    addr === null ? null : '0x' + hex(addr, 8),
            polarity:   this.polarity(),
            swapBytes:  this.swap(),
            code:       this.outputLines(),
        }, null, 2);
        this.copy(text, this.copyJsonLabel, 'Copy JSON');
    }

    private copy(text: string, label: WritableSignal<string>, idle: string): void {
        navigator.clipboard.writeText(text).then(
            () => {
                label.set('Copied!');
                setTimeout(() => label.set(idle), 1500);
            },
            () => label.set(idle),
        );
    }

    // Input plumbing

    private attachPointer(canvas: HTMLCanvasElement): void {
        canvas.addEventListener('pointerdown', e => this.zone.run(() => this.onPointerDown(canvas, e)));
        canvas.addEventListener('pointermove', e => this.zone.run(() => this.onPointerMove(canvas, e)));
        canvas.addEventListener('pointerup',     e => this.zone.run(() => this.onPointerUp(e)));
        canvas.addEventListener('pointercancel', e => this.zone.run(() => this.onPointerUp(e)));
        canvas.addEventListener('contextmenu', e => e.preventDefault());
        canvas.addEventListener('pointerleave', () => {
            if (this.hoverTarget) {
                this.hoverTarget = null;
                canvas.style.cursor = 'default';
                this.draw();
            }
        });
    }

    private onPointerDown(canvas: HTMLCanvasElement, e: PointerEvent): void {
        const p = this.canvasPoint(canvas, e);
        const hit = this.hitTest(p);
        if (!hit) { return; }
        e.preventDefault();

        if (this.mode() === 'build') {
            if (e.button !== 0) { return; }
            this.toggleCombo(hit.type === 'button' ? hit.button.name : hit.stick.pressButton);
            return;
        }

        canvas.setPointerCapture(e.pointerId);
        this.virtualControllerArmed = true;
        if (hit.type === 'button') {
            this.activePointers.set(e.pointerId, { kind: 'button', name: hit.button.name });
            this.setHeld(hit.button.name, true);
        } else if (e.button === 2 || e.shiftKey) {
            this.activePointers.set(e.pointerId, { kind: 'stickPress', stick: hit.stick });
            this.setHeld(hit.stick.pressButton, true);
        } else {
            this.activePointers.set(e.pointerId, { kind: 'stick', stick: hit.stick });
            this.updateStick(hit.stick, p);
        }
    }

    private onPointerMove(canvas: HTMLCanvasElement, e: PointerEvent): void {
        const p = this.canvasPoint(canvas, e);
        const active = this.activePointers.get(e.pointerId);

        if (!active) {
            const h = this.hitTest(p);
            const same =
                (h && this.hoverTarget &&
                  ((h.type === 'button' && this.hoverTarget.type === 'button' &&
                    h.button.name === this.hoverTarget.button.name) ||
                   (h.type === 'stick' && this.hoverTarget.type === 'stick' &&
                    h.stick.name === this.hoverTarget.stick.name))) ||
                (!h && !this.hoverTarget);
            if (!same) {
                this.hoverTarget = h;
                canvas.style.cursor = h ? 'pointer' : 'default';
                this.draw();
            }
            return;
        }

        if (active.kind === 'stick') {
            this.updateStick(active.stick, p);
        }
    }

    private onPointerUp(e: PointerEvent): void {
        const active = this.activePointers.get(e.pointerId);
        if (!active) { return; }
        this.activePointers.delete(e.pointerId);
        if (active.kind === 'button') {
            this.setHeld(active.name, false);
        } else if (active.kind === 'stickPress') {
            this.setHeld(active.stick.pressButton, false);
        } else {
            this.setStick(active.stick.name, 0, 0);
            this.logEvent('stick', `${active.stick.name} centre`);
        }
    }

    private onKey(e: KeyboardEvent, down: boolean): void {
        const btn = KEY_MAP[e.code];
        if (!btn || e.ctrlKey || e.metaKey || e.altKey) { return; }
        // Typing in the side panel (and Enter on its buttons) must not press pad buttons.
        const t = e.target as HTMLElement | null;
        if (t && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT|BUTTON)$/.test(t.tagName))) { return; }
        e.preventDefault();
        if (e.repeat) { return; }

        this.zone.run(() => {
            if (this.mode() === 'build') {
                if (down) { this.toggleCombo(btn); }
                return;
            }
            this.virtualControllerArmed = true;
            if (down) { this.keysHeld.add(btn); } else { this.keysHeld.delete(btn); }
            this.setHeld(btn, down);
        });
    }

    /** Losing focus never delivers keyup, so let go of everything the keyboard held. */
    private releaseKeyboard(): void {
        const keys = [...this.keysHeld];
        this.keysHeld.clear();
        for (const b of keys) { this.setHeld(b, false); }
    }

    private canvasPoint(canvas: HTMLCanvasElement, e: PointerEvent): { x: number; y: number } {
        const r = canvas.getBoundingClientRect();
        return {
            x: (e.clientX - r.left) * (canvas.width / r.width),
            y: (e.clientY - r.top)  * (canvas.height / r.height),
        };
    }

    private hitTest(p: { x: number; y: number }):
        | { type: 'button'; button: Button }
        | { type: 'stick'; stick: Stick }
        | null {
        for (const s of STICKS) {
            const dx = p.x - s.x, dy = p.y - s.y;
            if (dx * dx + dy * dy <= s.hitR * s.hitR) { return { type: 'stick', stick: s }; }
        }
        for (const b of BUTTONS) {
            if (this.pointInButton(p, b)) { return { type: 'button', button: b }; }
        }
        return null;
    }

    private pointInButton(p: { x: number; y: number }, b: Button): boolean {
        if (b.kind === 'circle') {
            const dx = p.x - b.x, dy = p.y - b.y;
            return dx * dx + dy * dy <= b.r * b.r;
        }
        return p.x >= b.x && p.x <= b.x + b.w && p.y >= b.y && p.y <= b.y + b.h;
    }

    private updateStick(s: Stick, p: { x: number; y: number }): void {
        const dx = p.x - s.x;
        const dy = p.y - s.y;
        const len = Math.hypot(dx, dy);
        const k = len > s.maxOffset ? s.maxOffset / len : 1;
        const x = Math.max(-1, Math.min(1, (dx * k) / s.maxOffset));
        const y = Math.max(-1, Math.min(1, (dy * k) / s.maxOffset));
        this.setStick(s.name, x, y);
    }

    // State mutation

    private setHeld(name: ButtonName, on: boolean): void {
        const cur = this.held();
        const has = cur.includes(name);
        if (on && !has) {
            this.held.set(sortButtons([...cur, name]));
            this.logEvent('press', name);
        } else if (!on && has) {
            // A key and a pointer can hold the same button; keep it until both let go.
            if (this.keysHeld.has(name) || [...this.activePointers.values()].some(a =>
                (a.kind === 'button' && a.name === name) ||
                (a.kind === 'stickPress' && a.stick.pressButton === name))) { return; }
            this.held.set(cur.filter(n => n !== name));
            this.logEvent('release', name);
        }
    }

    private setStick(name: 'leftStick' | 'rightStick', x: number, y: number): void {
        const fx = Number(x.toFixed(2));
        const fy = Number(y.toFixed(2));
        const sig = name === 'leftStick' ? this.leftStick : this.rightStick;
        const cur = sig();
        if (cur.x !== fx || cur.y !== fy) { sig.set({ x: fx, y: fy }); }
    }

    private logEvent(kind: LogEntry['kind'], name: string): void {
        const entry: LogEntry = { id: ++this.logSeq, kind, name, t: Date.now() };
        this.logEntries.set([entry, ...this.logEntries()].slice(0, 40));
        // Forward to the host so the PadState output channel picks it up.
        this.vscode.postMessage({
            type: 'jokerLog',
            text: `[${this.fmtTime(entry.t)}] ${kind} ${name}`,
        });
    }

    fmtTime(t: number): string { return new Date(t).toLocaleTimeString(); }

    // Drawing

    private draw(): void {
        if (!this.ctx) { return; }
        const ctx = this.ctx;
        ctx.clearRect(0, 0, CANVAS_W, CANVAS_H);

        if (this.imgReady && this.img) {
            ctx.drawImage(this.img, 0, 0, CANVAS_W, CANVAS_H);
        } else {
            ctx.fillStyle = '#2a2a2a';
            ctx.fillRect(0, 0, CANVAS_W, CANVAS_H);
            ctx.fillStyle = '#858585';
            ctx.font = '14px monospace';
            ctx.textAlign = 'center';
            ctx.fillText('loading controller...', CANVAS_W / 2, CANVAS_H / 2);
        }

        const pressedSet = new Set(this.mode() === 'build' ? this.combo() : this.held());
        // When the live pad toggle is active, light up the buttons the real
        // controller is currently pressing too (orange so they're distinct).
        const livePressedSet = this.liveActive()
            ? new Set(buttonsFromPadWord(this.livePadButtons()))
            : new Set<ButtonName>();

        for (const b of BUTTONS) {
            if (pressedSet.has(b.name)) {
                this.drawGlow(b, GLOW[b.name] ?? '#007acc');
            } else if (livePressedSet.has(b.name)) {
                this.drawGlow(b, LIVE_GLOW);
            }
        }
        for (const s of STICKS) {
            this.drawStickOverlay(s, pressedSet);
            if (livePressedSet.has(s.pressButton) && !pressedSet.has(s.pressButton)) {
                this.drawStickRing(s, LIVE_GLOW);
            }
            if (this.liveActive()) { this.drawLiveStick(s); }
        }
        if (this.hoverTarget) { this.drawHover(this.hoverTarget); }
    }

    private drawGlow(b: Button, color: string): void {
        const ctx = this.ctx!;
        ctx.save();
        ctx.globalCompositeOperation = 'lighter';
        if (b.kind === 'circle') {
            const grad = ctx.createRadialGradient(b.x, b.y, b.r * 0.2, b.x, b.y, b.r * 1.6);
            grad.addColorStop(0, this.hexA(color, 0.55));
            grad.addColorStop(1, this.hexA(color, 0));
            ctx.fillStyle = grad;
            ctx.beginPath();
            ctx.arc(b.x, b.y, b.r * 1.6, 0, Math.PI * 2);
            ctx.fill();
        } else {
            const cx = b.x + b.w / 2, cy = b.y + b.h / 2;
            const rad = Math.max(b.w, b.h);
            const grad = ctx.createRadialGradient(cx, cy, rad * 0.1, cx, cy, rad);
            grad.addColorStop(0, this.hexA(color, 0.5));
            grad.addColorStop(1, this.hexA(color, 0));
            ctx.fillStyle = grad;
            ctx.fillRect(cx - rad, cy - rad, rad * 2, rad * 2);
        }
        ctx.restore();
    }

    private drawStickOverlay(s: Stick, pressedSet: Set<ButtonName>): void {
        const ctx = this.ctx!;
        const pos = s.name === 'leftStick' ? this.leftStick() : this.rightStick();
        const dx = pos.x * s.maxOffset;
        const dy = pos.y * s.maxOffset;
        const cx = s.x + dx;
        const cy = s.y + dy;
        const pressed = pressedSet.has(s.pressButton);

        if (dx === 0 && dy === 0 && !pressed) { return; }

        ctx.save();
        ctx.strokeStyle = 'rgba(0,122,204,0.35)';
        ctx.lineWidth = 1.5;
        ctx.setLineDash([4, 4]);
        ctx.beginPath();
        ctx.arc(s.x, s.y, s.maxOffset, 0, Math.PI * 2);
        ctx.stroke();
        ctx.restore();

        ctx.save();
        ctx.fillStyle = '#2b2b2b';
        ctx.beginPath();
        ctx.arc(s.x, s.y, s.capR + 2, 0, Math.PI * 2);
        ctx.fill();

        const grad = ctx.createRadialGradient(cx - 12, cy - 14, 4, cx, cy, s.capR);
        grad.addColorStop(0, pressed ? '#3a4a55' : '#3a3a3c');
        grad.addColorStop(0.7, '#161616');
        grad.addColorStop(1, '#050505');
        ctx.fillStyle = grad;
        ctx.beginPath();
        ctx.arc(cx, cy, s.capR, 0, Math.PI * 2);
        ctx.fill();

        ctx.fillStyle = '#0d0d0e';
        ctx.beginPath();
        ctx.arc(cx, cy, s.capR * 0.62, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();

        if (pressed) { this.drawStickRing(s, GLOW[s.pressButton], cx, cy); }
    }

    private drawStickRing(s: Stick, color: string, cx = s.x, cy = s.y): void {
        const ctx = this.ctx!;
        ctx.save();
        ctx.strokeStyle = this.hexA(color, 0.9);
        ctx.lineWidth = 3;
        ctx.beginPath();
        ctx.arc(cx, cy, s.capR - 2, 0, Math.PI * 2);
        ctx.stroke();
        ctx.restore();
    }

    /** Orange dot at the real controller's stick position. */
    private drawLiveStick(s: Stick): void {
        const live = this.liveSticks();
        const ax = s.name === 'leftStick' ? live.lx : live.rx;
        const ay = s.name === 'leftStick' ? live.ly : live.ry;
        const nx = (ax - 0x7F) / 0x80, ny = (ay - 0x7F) / 0x80;
        if (Math.abs(nx) < 0.08 && Math.abs(ny) < 0.08) { return; }
        const ctx = this.ctx!;
        ctx.save();
        ctx.fillStyle = this.hexA(LIVE_GLOW, 0.9);
        ctx.beginPath();
        ctx.arc(s.x + nx * s.maxOffset, s.y + ny * s.maxOffset, 9, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
    }

    private drawHover(h: { type: 'button'; button: Button } | { type: 'stick'; stick: Stick }): void {
        const ctx = this.ctx!;
        ctx.save();
        ctx.strokeStyle = 'rgba(255,255,255,0.12)';
        ctx.lineWidth = 2;
        if (h.type === 'stick') {
            ctx.beginPath();
            ctx.arc(h.stick.x, h.stick.y, h.stick.hitR, 0, Math.PI * 2);
            ctx.stroke();
        } else if (h.button.kind === 'circle') {
            ctx.beginPath();
            ctx.arc(h.button.x, h.button.y, h.button.r + 4, 0, Math.PI * 2);
            ctx.stroke();
        } else {
            ctx.strokeRect(h.button.x - 3, h.button.y - 3, h.button.w + 6, h.button.h + 6);
        }
        ctx.restore();
    }

    private hexA(hexColor: string, a: number): string {
        const h = hexColor.replace('#', '');
        const r = parseInt(h.substring(0, 2), 16);
        const g = parseInt(h.substring(2, 4), 16);
        const b = parseInt(h.substring(4, 6), 16);
        return `rgba(${r},${g},${b},${a})`;
    }
}
