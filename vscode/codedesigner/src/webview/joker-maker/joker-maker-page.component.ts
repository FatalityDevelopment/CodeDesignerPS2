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
} from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import { IconComponent } from '../shared/icon.component';
import './joker-maker-page.css';

// Geometry
// Coordinates match the draft controller image at 1000x750 reference size.
// Hit regions are invisible overlays on top of the embedded image.

const CANVAS_W = 1000;
const CANVAS_H = 750;

type ButtonName =
    | 'l2' | 'l1' | 'r2' | 'r1'
    | 'up' | 'down' | 'left' | 'right'
    | 'select' | 'start' | 'ps'
    | 'triangle' | 'square' | 'circle' | 'cross'
    | 'l3' | 'r3';

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

const BUTTONS: readonly Button[] = [
    { name: 'l2', kind: 'rect', x: 186, y: 84,  w: 104, h: 36 },
    { name: 'l1', kind: 'rect', x: 178, y: 120, w: 127, h: 52 },
    { name: 'r2', kind: 'rect', x: 709, y: 84,  w: 104, h: 36 },
    { name: 'r1', kind: 'rect', x: 695, y: 120, w: 127, h: 52 },

    { name: 'up',    kind: 'rect', x: 204, y: 198, w: 60, h: 70 },
    { name: 'down',  kind: 'rect', x: 204, y: 314, w: 60, h: 70 },
    { name: 'left',  kind: 'rect', x: 148, y: 254, w: 70, h: 56 },
    { name: 'right', kind: 'rect', x: 250, y: 254, w: 70, h: 56 },

    { name: 'select', kind: 'rect',   x: 388, y: 252, w: 36, h: 30 },
    { name: 'start',  kind: 'rect',   x: 546, y: 252, w: 40, h: 38 },
    { name: 'ps',     kind: 'circle', x: 500, y: 334, r: 24 },

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
    ps: '#007acc', start: '#9cdcfe', select: '#9cdcfe',
    l1: '#569cd6', l2: '#569cd6', r1: '#569cd6', r2: '#569cd6',
    up: '#dcdcaa', down: '#dcdcaa', left: '#dcdcaa', right: '#dcdcaa',
    l3: '#c586c0', r3: '#c586c0',
};

/** Webview keyboard shortcuts for driving the virtual controller. */
const KEY_MAP: Record<string, ButtonName> = {
    ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right',
    KeyW: 'triangle', KeyA: 'square', KeyD: 'circle', KeyS: 'cross',
    KeyQ: 'l1', Digit1: 'l2', KeyE: 'r1', Digit3: 'r2',
    Enter: 'start', ShiftRight: 'select', Space: 'ps',
};

/**
 * Joker hex mask values - same as the original VB tool. PS3/PS2 cheat codes
 * use the inverted (0xFFFF - sum of pressed bits) form.
 */
const JOKER_MASK: Record<ButtonName, number> = {
    select: 0x0001, l3: 0x0002, r3: 0x0004, start: 0x0008,
    up: 0x0010, right: 0x0020, down: 0x0040, left: 0x0080,
    l2: 0x0100, r2: 0x0200, l1: 0x0400, r1: 0x0800,
    triangle: 0x1000, circle: 0x2000, cross: 0x4000, square: 0x8000,
    ps: 0,
};

interface LogEntry { kind: 'press' | 'release' | 'stick'; name: string; t: number; }

interface JsonState {
    pressed:    ButtonName[];
    leftStick:  { x: number; y: number };
    rightStick: { x: number; y: number };
    /** 8-digit GameShark joker data value (0000XXXX). */
    hex:        string;
    /** Raw 16-bit mask in PS2 active-low form (0xFFFF means nothing pressed). */
    buttonsRaw: string;
}

interface ActivePointer {
    kind:  'button' | 'stick';
    name?: ButtonName;
    stick?: Stick;
}

interface InitMsg               { type: 'jokerInit'; controllerImage: string; }
interface ConnectionMsg         { type: 'jokerConnectionStatus'; connected: boolean; }
interface LivePadStateMsg       { type: 'jokerLivePadState'; buttons: number;
                                  lx: number; ly: number; rx: number; ry: number; }
interface LivePollStoppedMsg    { type: 'jokerLivePollStopped'; }
type IncomingMsg = InitMsg | ConnectionMsg | LivePadStateMsg | LivePollStoppedMsg;

@Component({
    selector: 'app-joker-maker-page',
    standalone: true,
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    encapsulation: ViewEncapsulation.None,
    template: `
<div class="jm-host">

  <div class="jm-titlebar">
    <div class="jm-mark"><app-icon name="gamepad2" [size]="17" /></div>
    <div><div class="jm-title">Controller</div><div class="jm-subtitle">Virtual PS2 controller and active-low cheat mask builder</div></div>
    <span class="jm-connection" [class.jm-connection-on]="connected()">{{ connected() ? 'connected' : 'offline' }}</span>
    <button class="jm-panel-toggle" type="button" (click)="toggleSidePanel()" [attr.aria-expanded]="!sideCollapsed()" title="Toggle details panel">
      {{ sideCollapsed() ? 'Show Details' : 'Hide Details' }}
    </button>
  </div>

  <div class="jm-main" [class.jm-main-collapsed]="sideCollapsed()">

    <div class="jm-stage">
      @if (sideCollapsed()) {
        <button class="jm-side-reopen" type="button" (click)="toggleSidePanel()">Show details</button>
      }
      <div class="jm-hint">
        <div><span class="c">// click any button</span></div>
        <div><span class="c">// drag the analog sticks</span></div>
        <div><span class="c">// keys: WASD / arrows / Q E 1 3 / Enter Shift Space</span></div>
      </div>
      <canvas #canvas
              [attr.width]="canvasW"
              [attr.height]="canvasH"
              aria-label="PS controller"></canvas>
    </div>

    <aside class="jm-side" [class.jm-side-collapsed]="sideCollapsed()" [attr.aria-hidden]="sideCollapsed()">

      <div class="jm-metrics">
        <div class="jm-metric"><div class="jm-metric-label">Joker code</div><div class="jm-metric-value">{{ hexCode() }}</div></div>
        <div class="jm-metric"><div class="jm-metric-label">Raw mask</div><div class="jm-metric-value">{{ buttonsRaw() }}</div></div>
        <div class="jm-metric"><div class="jm-metric-label">Pressed</div><div class="jm-metric-value">{{ pressed().length }} buttons</div></div>
      </div>

      <div class="jm-toggles">
        <label class="jm-toggle" title="Click multiple buttons to build a combined code.">
          <input type="checkbox"
                 [checked]="multiPress()"
                 (change)="setMultiPress($any($event.target).checked)" />
          Multi-press
        </label>
        <label class="jm-toggle"
               [class.jm-toggle-dim]="!connected()"
               title="Mirror the real controller state via PINE (PCSX2 mode).">
          <input type="checkbox"
                 [checked]="liveEnabled()"
                 [disabled]="!connected()"
                 (change)="setLiveEnabled($any($event.target).checked)" />
          Live pad
          <span class="jm-live-dot" [class.jm-live-dot-on]="liveActive()"></span>
        </label>
      </div>

      <div class="jm-section-head">controller.state.json</div>
      <div class="jm-json">
        <div class="jm-gutter">{{ gutter() }}</div>
        <pre class="jm-code" [innerHTML]="jsonHtml()"></pre>
      </div>

      <div class="jm-section-head">input events</div>
      <div class="jm-log">
        @if (logEntries().length === 0) {
          <div class="jm-log-empty">// no events yet</div>
        } @else {
          @for (e of logEntries(); track e.t + e.kind + e.name) {
            <div class="jm-log-row">
              <span class="t">[{{ fmtTime(e.t) }}]</span>
              <span class="{{ e.kind }}">{{ e.kind }}</span>
              <span class="name">{{ e.name }}</span>
            </div>
          }
        }
      </div>

      <div class="jm-actions">
        <button (click)="resetAll()">Reset input</button>
        <button class="primary" (click)="copyCode()">{{ copyCodeLabel() }}</button>
        <button (click)="copyJson()">{{ copyLabel() }}</button>
      </div>

    </aside>
  </div>

  <div class="jm-statusbar">
    <span>Controller</span>
    <span>pressed: {{ pressed().length }}</span>
    <span class="jm-status-right">{{ hexCode() }}</span>
  </div>
</div>
`,
})
export class JokerMakerPageComponent implements AfterViewInit, OnDestroy {

    readonly canvasW = CANVAS_W;
    readonly canvasH = CANVAS_H;

    private readonly canvasRef = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
    private readonly zone = inject(NgZone);
    private readonly vscode = getVsCodeApi();

    // Reactive state
    /** Pressed-buttons set as a sorted array signal so templates react. */
    readonly pressed       = signal<ButtonName[]>([]);
    readonly leftStick     = signal({ x: 0, y: 0 });
    readonly rightStick    = signal({ x: 0, y: 0 });
    readonly logEntries    = signal<LogEntry[]>([]);
    readonly multiPress    = signal(false);
    readonly liveEnabled   = signal(false);
    readonly liveActive    = signal(false);
    readonly connected     = signal(false);
    /** Live pad-button mask (0xFFFF = nothing pressed) - only valid while liveActive. */
    readonly livePadButtons = signal(0xFFFF);
    readonly copyLabel     = signal('Copy JSON');
    readonly copyCodeLabel = signal('Copy code');
    readonly sideCollapsed = signal(false);

    /** Hex value computed from currently-pressed buttons (excluding 'ps'). */
    readonly hexCode = computed(() => {
        let h = 0xFFFF;
        for (const name of this.pressed()) {
            h -= JOKER_MASK[name] ?? 0;
        }
        return '0000' + h.toString(16).padStart(4, '0').toUpperCase();
    });

    /** Raw active-low 16-bit mask matching PS2 wire format. */
    readonly buttonsRaw = computed(() => {
        let mask = 0xFFFF;
        for (const name of this.pressed()) {
            mask &= ~(JOKER_MASK[name] ?? 0);
        }
        return '0x' + (mask & 0xFFFF).toString(16).padStart(4, '0').toUpperCase();
    });

    /** Snapshot exposed in the JSON panel + sent back to the provider. */
    readonly jsonState = computed<JsonState>(() => ({
        pressed:    this.pressed(),
        leftStick:  this.leftStick(),
        rightStick: this.rightStick(),
        hex:        this.hexCode(),
        buttonsRaw: this.buttonsRaw(),
    }));

    readonly jsonHtml = computed(() => this.formatJson(this.jsonState()));
    readonly gutter   = computed(() => {
        const lines = this.jsonHtml().split('\n').length;
        return Array.from({ length: lines }, (_, i) => i + 1).join('\n');
    });

    // Non-reactive scratch state
    private ctx:   CanvasRenderingContext2D | null = null;
    private img:   HTMLImageElement | null = null;
    private imgReady = false;
    private hoverTarget: { type: 'button'; button: Button } | { type: 'stick'; stick: Stick } | null = null;
    private readonly activePointers = new Map<number, ActivePointer>();

    private readonly messageListener = (event: MessageEvent) => this.onMessage(event);
    private keydownHandler?: (e: KeyboardEvent) => void;
    private keyupHandler?:   (e: KeyboardEvent) => void;
    /** Coalesces repeat virtual-state posts. */
    private virtualStatePending = false;
    /** Prevents opening the tool from clearing the emulator pad state. */
    private virtualControllerArmed = false;

    constructor() {
        // Every time the rendered state changes, repaint the canvas.
        effect(() => {
            // Touch the dependencies so the effect re-runs.
            this.pressed();
            this.leftStick();
            this.rightStick();
            this.livePadButtons();
            this.liveActive();
            this.draw();
        });

        // Whenever the user changes the virtual pad state, push it to the host.
        effect(() => {
            const snapshot = this.jsonState();
            this.scheduleVirtualStatePost(snapshot);
        });
    }

    ngAfterViewInit(): void {
        const canvas = this.canvasRef().nativeElement;
        this.ctx = canvas.getContext('2d');
        this.attachPointer(canvas);
        this.attachKeyboard();
        window.addEventListener('message', this.messageListener);
        this.vscode.postMessage({ type: 'ready' });
    }

    ngOnDestroy(): void {
        window.removeEventListener('message', this.messageListener);
        if (this.keydownHandler) { window.removeEventListener('keydown', this.keydownHandler); }
        if (this.keyupHandler)   { window.removeEventListener('keyup',   this.keyupHandler); }
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
                    }
                    break;
                case 'jokerLivePadState':
                    this.liveActive.set(true);
                    this.livePadButtons.set(msg.buttons & 0xFFFF);
                    this.logLivePadChange(msg.buttons & 0xFFFF);
                    break;
                case 'jokerLivePollStopped':
                    this.liveActive.set(false);
                    break;
            }
        });
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

    private scheduleVirtualStatePost(_snapshot: JsonState): void {
        if (!this.virtualControllerArmed) { return; }
        if (this.virtualStatePending) { return; }
        this.virtualStatePending = true;
        queueMicrotask(() => {
            this.virtualStatePending = false;
            const snapshot = this.jsonState();
            this.vscode.postMessage({
                type:       'jokerVirtualState',
                pressed:    snapshot.pressed,
                leftStick:  snapshot.leftStick,
                rightStick: snapshot.rightStick,
            });
        });
    }

    // UI handlers

    setMultiPress(on: boolean): void {
        this.multiPress.set(on);
        // Switching modes: any latched buttons are cleared so the user does not
        // get stuck with a phantom press.
        if (this.pressed().length) {
            this.armVirtualController();
            this.pressed.set([]);
            this.logEvent('release', 'all');
        }
    }

    toggleSidePanel(): void {
        this.sideCollapsed.update(collapsed => !collapsed);
    }

    setLiveEnabled(on: boolean): void {
        this.liveEnabled.set(on);
        this.vscode.postMessage({ type: 'jokerLiveToggle', enabled: on });
        if (!on) { this.liveActive.set(false); }
    }

    resetAll(): void {
        this.armVirtualController();
        this.pressed.set([]);
        this.leftStick.set({ x: 0, y: 0 });
        this.rightStick.set({ x: 0, y: 0 });
        this.logEntries.set([]);
        this.activePointers.clear();
    }

    copyCode(): void {
        navigator.clipboard.writeText(this.hexCode()).then(
            () => {
                this.copyCodeLabel.set('Copied!');
                setTimeout(() => this.copyCodeLabel.set('Copy code'), 1500);
            },
            () => this.copyCodeLabel.set('Copy code'),
        );
    }

    copyJson(): void {
        const text = JSON.stringify(this.jsonState(), null, 2);
        navigator.clipboard.writeText(text).then(
            () => {
                this.copyLabel.set('Copied!');
                setTimeout(() => this.copyLabel.set('Copy JSON'), 1500);
            },
            () => this.copyLabel.set('Copy JSON'),
        );
    }

    // Input plumbing

    private attachPointer(canvas: HTMLCanvasElement): void {
        canvas.addEventListener('pointerdown', e => this.onPointerDown(canvas, e));
        canvas.addEventListener('pointermove', e => this.onPointerMove(canvas, e));
        canvas.addEventListener('pointerup',   e => this.onPointerUp(e));
        canvas.addEventListener('pointercancel', e => this.onPointerUp(e));
        canvas.addEventListener('pointerleave', () => {
            if (this.hoverTarget) {
                this.hoverTarget = null;
                canvas.style.cursor = 'default';
                this.draw();
            }
        });
    }

    private onPointerDown(canvas: HTMLCanvasElement, e: PointerEvent): void {
        canvas.setPointerCapture(e.pointerId);
        const p = this.canvasPoint(canvas, e);
        const hit = this.hitTest(p);
        if (!hit) { return; }

        if (hit.type === 'button') {
            const name = hit.button.name;
            if (this.multiPress()) {
                this.togglePressed(name);
            } else {
                this.setSinglePressed(name);
            }
        } else {
            const s = hit.stick;
            this.activePointers.set(e.pointerId, { kind: 'stick', stick: s });
            this.setPressed(s.pressButton, true);
            this.updateStick(s, p);
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

        if (active.kind === 'stick' && active.stick) {
            this.updateStick(active.stick, p);
        }
    }

    private onPointerUp(e: PointerEvent): void {
        const active = this.activePointers.get(e.pointerId);
        if (!active) { return; }
        if (active.kind === 'button' && active.name) {
            this.setPressed(active.name, false);
        } else if (active.kind === 'stick' && active.stick) {
            this.setPressed(active.stick.pressButton, false);
            this.setStick(active.stick.name, 0, 0);
            this.logEvent('stick', active.stick.name);
        }
        this.activePointers.delete(e.pointerId);
    }

    private attachKeyboard(): void {
        this.keydownHandler = (e: KeyboardEvent) => {
            const btn = KEY_MAP[e.code];
            if (!btn || e.repeat) { return; }
            e.preventDefault();
            this.setPressed(btn, true);
        };
        this.keyupHandler = (e: KeyboardEvent) => {
            const btn = KEY_MAP[e.code];
            if (!btn) { return; }
            e.preventDefault();
            // In multi-press mode the keyboard acts as a toggle too, since the
            // intent is to "stack" buttons; otherwise we release on keyup.
            if (!this.multiPress()) { this.setPressed(btn, false); }
        };
        window.addEventListener('keydown', this.keydownHandler);
        window.addEventListener('keyup',   this.keyupHandler);
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

    private setPressed(name: ButtonName, on: boolean): void {
        this.armVirtualController();
        const cur = this.pressed();
        const has = cur.includes(name);
        if (on && !has) {
            const next = [...cur, name].sort();
            this.pressed.set(next);
            this.logEvent('press', name);
        } else if (!on && has) {
            this.pressed.set(cur.filter(n => n !== name));
            this.logEvent('release', name);
        }
    }

    private togglePressed(name: ButtonName): void {
        const has = this.pressed().includes(name);
        this.setPressed(name, !has);
    }

    private setSinglePressed(name: ButtonName): void {
        this.armVirtualController();
        const cur = this.pressed();
        if (cur.length === 1 && cur[0] === name) {
            this.pressed.set([]);
            this.logEvent('release', name);
            return;
        }
        for (const old of cur) {
            this.logEvent('release', old);
        }
        this.pressed.set([name]);
        this.logEvent('press', name);
    }

    private setStick(name: 'leftStick' | 'rightStick', x: number, y: number): void {
        this.armVirtualController();
        const fx = Number(x.toFixed(2));
        const fy = Number(y.toFixed(2));
        if (name === 'leftStick') {
            const cur = this.leftStick();
            if (cur.x !== fx || cur.y !== fy) { this.leftStick.set({ x: fx, y: fy }); }
        } else {
            const cur = this.rightStick();
            if (cur.x !== fx || cur.y !== fy) { this.rightStick.set({ x: fx, y: fy }); }
        }
    }

    private armVirtualController(): void {
        this.virtualControllerArmed = true;
    }

    private logEvent(kind: LogEntry['kind'], name: string): void {
        const entry: LogEntry = { kind, name, t: Date.now() };
        const next = [entry, ...this.logEntries()].slice(0, 12);
        this.logEntries.set(next);
        // Forward to the host so the PadState output channel picks it up.
        const time = new Date(entry.t).toLocaleTimeString();
        this.vscode.postMessage({
            type: 'jokerLog',
            text: `[${time}] ${kind} ${name}`,
        });
    }

    private logLivePadChange(buttons: number): void {
        const pressedNames = (Object.keys(JOKER_MASK) as ButtonName[])
            .filter(n => JOKER_MASK[n] !== 0 && (buttons & JOKER_MASK[n]) === 0);
        const name = pressedNames.length ? pressedNames.join('+') : '(none)';
        this.vscode.postMessage({
            type: 'jokerLog',
            text: `[${new Date().toLocaleTimeString()}] live ${name}`,
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

        const pressedSet = new Set(this.pressed());
        // When the live pad toggle is active, light up the buttons the real
        // controller is currently pressing too (orange so they're distinct).
        const livePressedSet = this.liveActive() ? this.computeLivePressedSet() : new Set<ButtonName>();

        for (const b of BUTTONS) {
            if (pressedSet.has(b.name)) {
                this.drawGlow(b, GLOW[b.name] ?? '#007acc');
            } else if (livePressedSet.has(b.name)) {
                this.drawGlow(b, '#ffb454');
            }
        }
        for (const s of STICKS) { this.drawStickOverlay(s, pressedSet); }
        if (this.hoverTarget) { this.drawHover(this.hoverTarget); }
    }

    private computeLivePressedSet(): Set<ButtonName> {
        const mask = this.livePadButtons();
        const out = new Set<ButtonName>();
        for (const name of Object.keys(JOKER_MASK) as ButtonName[]) {
            const m = JOKER_MASK[name];
            if (m !== 0 && (mask & m) === 0) { out.add(name); }
        }
        return out;
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

        if (dx !== 0 || dy !== 0 || pressed) {
            ctx.save();
            ctx.strokeStyle = 'rgba(0,122,204,0.35)';
            ctx.lineWidth = 1.5;
            ctx.setLineDash([4, 4]);
            ctx.beginPath();
            ctx.arc(s.x, s.y, s.maxOffset, 0, Math.PI * 2);
            ctx.stroke();
            ctx.restore();
        }

        if (dx === 0 && dy === 0 && !pressed) { return; }

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

        ctx.strokeStyle = pressed ? this.hexA('#007acc', 0.8) : 'rgba(255,255,255,0.06)';
        ctx.lineWidth = pressed ? 2 : 1;
        ctx.beginPath();
        ctx.arc(cx, cy, s.capR - 2, 0, Math.PI * 2);
        ctx.stroke();
        ctx.restore();
    }

    private drawHover(h: { type: 'button'; button: Button } | { type: 'stick'; stick: Stick }): void {
        const ctx = this.ctx!;
        ctx.save();
        ctx.strokeStyle = 'rgba(255,255,255,0.08)';
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

    private hexA(hex: string, a: number): string {
        const h = hex.replace('#', '');
        const r = parseInt(h.substring(0, 2), 16);
        const g = parseInt(h.substring(2, 4), 16);
        const b = parseInt(h.substring(4, 6), 16);
        return `rgba(${r},${g},${b},${a})`;
    }

    // JSON formatter

    private escape(s: string): string {
        return s.replace(/[<>&]/g, c => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;' })[c] as string);
    }

    private fmtVal(v: unknown): string {
        if (typeof v === 'number') {
            return `<span class="n">${Number.isInteger(v) ? v : v.toFixed(2)}</span>`;
        }
        if (typeof v === 'string') {
            return `<span class="s">"${this.escape(v)}"</span>`;
        }
        if (typeof v === 'boolean') { return `<span class="b">${v}</span>`; }
        if (v === null) { return `<span class="b">null</span>`; }
        if (Array.isArray(v)) {
            if (v.length === 0) { return `<span class="p">[]</span>`; }
            return `<span class="p">[</span> ` +
                v.map(x => this.fmtVal(x)).join('<span class="p">, </span>') +
                ` <span class="p">]</span>`;
        }
        if (typeof v === 'object') {
            const obj = v as Record<string, unknown>;
            const ks = Object.keys(obj);
            if (ks.length === 0) { return `<span class="p">{}</span>`; }
            return `<span class="p">{ </span>` +
                ks.map(k => `<span class="k">"${k}"</span><span class="p">: </span>${this.fmtVal(obj[k])}`)
                  .join('<span class="p">, </span>') +
                `<span class="p"> }</span>`;
        }
        return this.escape(String(v));
    }

    private formatJson(s: JsonState): string {
        const lines = [
            `<span class="p">{</span>`,
            `  <span class="k">"pressed"</span><span class="p">:</span> ${this.fmtVal(s.pressed)}<span class="p">,</span>`,
            `  <span class="k">"leftStick"</span><span class="p">:</span> ${this.fmtVal(s.leftStick)}<span class="p">,</span>`,
            `  <span class="k">"rightStick"</span><span class="p">:</span> ${this.fmtVal(s.rightStick)}<span class="p">,</span>`,
            `  <span class="k">"hex"</span><span class="p">:</span> ${this.fmtVal(s.hex)}<span class="p">,</span>`,
            `  <span class="k">"buttonsRaw"</span><span class="p">:</span> ${this.fmtVal(s.buttonsRaw)}`,
            `<span class="p">}</span>`,
        ];
        return lines.join('\n');
    }
}
