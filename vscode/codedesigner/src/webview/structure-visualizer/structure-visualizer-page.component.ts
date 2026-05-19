import {
  Component,
  AfterViewInit,
  ElementRef,
  ViewChild,
  ViewEncapsulation,
  computed,
  effect,
  signal,
} from '@angular/core';
import mermaid from 'mermaid';
import { getVsCodeApi } from '../shared/vscode-api';
import {
  SvLabel, SvGroup, SvCallGraph,
  classDiagramFromStruct, classDiagramFromGroup,
  vtableDiagramFromLabel, vtableDiagramFromGroup,
  callGraphFlowchart, hexKey,
} from './mermaid-generators';
import './structure-visualizer-page.css';

type Mode = 'class' | 'vtable' | 'callgraph';

/**
 * Structure Visualizer — renders Mermaid diagrams of a PS2 game's structure:
 * classes/objects (Struct labels & Class groups), vtables (ArrayOfPointers
 * labels & VTable groups), and call-graph chains around a focus function.
 */
@Component({
  selector: 'app-structure-visualizer-page',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="sv-shell">
      <!-- ── Toolbar ───────────────────────────────────────────────────── -->
      <div class="sv-toolbar">
        <span class="sv-title">Structure Visualizer</span>
        <input class="sv-input" type="text" placeholder="Game ID / serial…"
               [value]="gameId()" (input)="onGameId($event)" />
        <button class="sv-btn" [disabled]="busy()" (click)="loadSets()">Load sets</button>
        <select class="sv-select" [value]="setName()" (change)="onSetPick($event)"
                [disabled]="sets().length === 0">
          <option value="">— label set —</option>
          @for (s of sets(); track s) { <option [value]="s">{{ s }}</option> }
        </select>
        <button class="sv-btn sv-primary" [disabled]="busy() || !setName()" (click)="loadData()">Load</button>
        <span class="sv-spacer"></span>
        <button class="sv-btn" [disabled]="!source()" (click)="copySource()">Copy Mermaid</button>
        <button class="sv-btn" [disabled]="!source()" (click)="exportSvg()">Export SVG</button>
      </div>

      <!-- ── Mode tabs ─────────────────────────────────────────────────── -->
      <div class="sv-tabs">
        <button class="sv-tab" [class.sv-tab-on]="mode() === 'class'"     (click)="setMode('class')">Class / Object</button>
        <button class="sv-tab" [class.sv-tab-on]="mode() === 'vtable'"    (click)="setMode('vtable')">VTable</button>
        <button class="sv-tab" [class.sv-tab-on]="mode() === 'callgraph'" (click)="setMode('callgraph')">Call Graph</button>
      </div>

      <!-- ── Mode controls ─────────────────────────────────────────────── -->
      <div class="sv-controls">
        @if (mode() === 'class') {
          <select class="sv-select sv-grow" [value]="selectedKey()" (change)="onSelect($event)">
            <option value="">— pick a struct or class group —</option>
            @if (structOptions().length) {
              <optgroup label="Struct labels">
                @for (s of structOptions(); track s.address) {
                  <option [value]="'struct:' + s.address">{{ s.name }} ({{ s.address }})</option>
                }
              </optgroup>
            }
            @if (classGroups().length) {
              <optgroup label="Class groups">
                @for (g of classGroups(); track g.id) {
                  <option [value]="'group:' + g.id">{{ g.name }}</option>
                }
              </optgroup>
            }
          </select>
        }
        @if (mode() === 'vtable') {
          <select class="sv-select sv-grow" [value]="selectedKey()" (change)="onSelect($event)">
            <option value="">— pick a vtable —</option>
            @if (vtableOptions().length) {
              <optgroup label="ArrayOfPointers labels">
                @for (v of vtableOptions(); track v.address) {
                  <option [value]="'vt:' + v.address">{{ v.name }} ({{ v.address }})</option>
                }
              </optgroup>
            }
            @if (vtableGroups().length) {
              <optgroup label="VTable groups">
                @for (g of vtableGroups(); track g.id) {
                  <option [value]="'group:' + g.id">{{ g.name }}</option>
                }
              </optgroup>
            }
          </select>
        }
        @if (mode() === 'callgraph') {
          <input class="sv-input" type="text" placeholder="Focus address (0x…)"
                 [value]="focusAddr()" (input)="onFocusAddr($event)" />
          <select class="sv-select" [value]="direction()" (change)="onDirection($event)">
            <option value="both">Callers + callees</option>
            <option value="callers">Callers (back to hook)</option>
            <option value="callees">Callees</option>
          </select>
          <label class="sv-depth">Depth {{ depth() }}
            <input type="range" min="1" max="8" [value]="depth()" (input)="onDepth($event)" />
          </label>
          <button class="sv-btn sv-primary" [disabled]="busy() || !focusAddr()" (click)="buildCallGraph()">Build</button>
          <span class="sv-sep"></span>
          <input class="sv-input sv-sm" type="text" placeholder="Scan from 0x…"
                 [value]="scanAddr()" (input)="onScanAddr($event)" />
          <input class="sv-input sv-sm" type="text" placeholder="Scan length"
                 [value]="scanLen()" (input)="onScanLen($event)" />
          <button class="sv-btn" [disabled]="busy() || !scanAddr() || !scanLen()" (click)="scanCallGraph()">Scan calls</button>
        }
      </div>

      <!-- ── Status ────────────────────────────────────────────────────── -->
      @if (status()) { <div class="sv-status">{{ status() }}</div> }
      @if (error())  { <div class="sv-status sv-error">{{ error() }}</div> }

      <!-- ── Diagram ───────────────────────────────────────────────────── -->
      <div class="sv-diagram" #diagram></div>
      @if (!source() && !error()) {
        <div class="sv-empty">
          Enter a game ID, load a label set, then pick something to visualise.
          Click a node in a call graph to jump to that address in the disassembler.
        </div>
      }
    </div>
  `,
})
export class StructureVisualizerPageComponent implements AfterViewInit {
  @ViewChild('diagram') private diagramRef?: ElementRef<HTMLDivElement>;

  private readonly vscode = getVsCodeApi();
  private renderSeq = 0;

  readonly gameId    = signal('');
  readonly sets      = signal<string[]>([]);
  readonly setName   = signal('');
  readonly entries   = signal<SvLabel[]>([]);
  readonly groups    = signal<SvGroup[]>([]);
  readonly mode      = signal<Mode>('class');
  readonly selectedKey = signal('');
  readonly focusAddr = signal('');
  readonly direction = signal('both');
  readonly depth     = signal(3);
  readonly scanAddr  = signal('');
  readonly scanLen   = signal('');
  readonly callGraph = signal<SvCallGraph | null>(null);
  readonly busy      = signal(false);
  readonly status    = signal('');
  readonly error     = signal('');
  readonly source    = signal('');

  // ── Derived label collections ─────────────────────────────────────────
  readonly structOptions  = computed(() => this.entries().filter(e => e.type === 'Struct' && !e.parentAddress));
  readonly vtableOptions   = computed(() => this.entries().filter(e => e.type === 'ArrayOfPointers'));
  readonly classGroups     = computed(() => this.groups().filter(g => g.kind === 'Class'));
  readonly vtableGroups    = computed(() => this.groups().filter(g => g.kind === 'VTable'));
  private readonly nameMap = computed(() => {
    const m = new Map<string, string>();
    for (const e of this.entries()) { m.set(hexKey(e.address), e.name); }
    return m;
  });

  constructor() {
    mermaid.initialize({
      startOnLoad: false,
      securityLevel: 'strict',
      theme: 'dark',
      flowchart: { useMaxWidth: true },
    });
    // Re-render whenever the selection or underlying data changes.
    effect(() => {
      const src = this.buildSource();
      void this.renderDiagram(src);
    });
  }

  ngAfterViewInit(): void {
    window.addEventListener('message', (event: MessageEvent) => this.onMessage(event.data));
    this.vscode.postMessage({ type: 'ready' });
  }

  // ── Incoming messages ─────────────────────────────────────────────────

  private onMessage(data: unknown): void {
    const msg = data as { type?: string; [k: string]: unknown };
    switch (msg.type) {
      case 'init':
        break;
      case 'sets':
        this.sets.set((msg['sets'] as string[]) ?? []);
        this.busy.set(false);
        this.status.set(`${this.sets().length} label set(s) found.`);
        break;
      case 'data': {
        const ls = msg['labelSet'] as { entries?: SvLabel[] } | undefined;
        this.entries.set(ls?.entries ?? []);
        this.groups.set((msg['groups'] as SvGroup[]) ?? []);
        this.busy.set(false);
        this.status.set(`Loaded ${this.entries().length} label(s), ${this.groups().length} group(s).`);
        break;
      }
      case 'groups':
        this.groups.set((msg['groups'] as SvGroup[]) ?? []);
        break;
      case 'callGraph':
        this.callGraph.set(msg['graph'] as SvCallGraph);
        this.busy.set(false);
        this.status.set('Call graph built.');
        break;
      case 'scanResult': {
        const r = msg['result'] as { totalEdges?: number; directEdges?: number; indirectEdges?: number };
        this.busy.set(false);
        this.status.set(`Scan complete: ${r?.totalEdges ?? 0} edges (${r?.directEdges ?? 0} direct, ${r?.indirectEdges ?? 0} indirect).`);
        break;
      }
      case 'error':
        this.busy.set(false);
        this.error.set(String(msg['message'] ?? 'Unknown error'));
        break;
    }
  }

  // ── Toolbar actions ───────────────────────────────────────────────────

  onGameId(e: Event): void { this.gameId.set((e.target as HTMLInputElement).value); }

  loadSets(): void {
    this.error.set('');
    this.busy.set(true);
    this.status.set('Loading label sets…');
    this.vscode.postMessage({ type: 'loadSets', gameId: this.gameId().trim() });
  }

  onSetPick(e: Event): void { this.setName.set((e.target as HTMLSelectElement).value); }

  loadData(): void {
    this.error.set('');
    this.busy.set(true);
    this.status.set('Loading labels & groups…');
    this.vscode.postMessage({ type: 'loadData', gameId: this.gameId().trim(), setName: this.setName() });
  }

  setMode(m: Mode): void {
    this.mode.set(m);
    this.selectedKey.set('');
  }

  onSelect(e: Event): void { this.selectedKey.set((e.target as HTMLSelectElement).value); }
  onFocusAddr(e: Event): void { this.focusAddr.set((e.target as HTMLInputElement).value); }
  onDirection(e: Event): void { this.direction.set((e.target as HTMLSelectElement).value); }
  onDepth(e: Event): void { this.depth.set(Number((e.target as HTMLInputElement).value)); }
  onScanAddr(e: Event): void { this.scanAddr.set((e.target as HTMLInputElement).value); }
  onScanLen(e: Event): void { this.scanLen.set((e.target as HTMLInputElement).value); }

  buildCallGraph(): void {
    this.error.set('');
    this.busy.set(true);
    this.status.set('Building call graph…');
    this.vscode.postMessage({
      type: 'getCallGraph',
      gameId: this.gameId().trim(),
      address: this.focusAddr().trim(),
      depth: this.depth(),
      direction: this.direction(),
    });
  }

  scanCallGraph(): void {
    this.error.set('');
    this.busy.set(true);
    this.status.set('Scanning code for calls…');
    this.vscode.postMessage({
      type: 'scanCallGraph',
      gameId: this.gameId().trim(),
      address: this.scanAddr().trim(),
      length: parseInt(this.scanLen().trim().replace(/^0x/i, ''), this.scanLen().trim().toLowerCase().startsWith('0x') ? 16 : 10),
    });
  }

  copySource(): void {
    const src = this.source();
    if (src) { navigator.clipboard?.writeText(src).catch(() => { /* ignore */ }); }
  }

  exportSvg(): void {
    const host = this.diagramRef?.nativeElement;
    const svg = host?.querySelector('svg');
    if (!svg) { return; }
    const blob = new Blob([svg.outerHTML], { type: 'image/svg+xml' });
    const url  = URL.createObjectURL(blob);
    const a    = document.createElement('a');
    a.href = url;
    a.download = 'structure-diagram.svg';
    a.click();
    URL.revokeObjectURL(url);
  }

  // ── Diagram building & rendering ──────────────────────────────────────

  /** Compute the Mermaid source for the current mode + selection (signal-tracked). */
  private buildSource(): string {
    const mode = this.mode();
    if (mode === 'callgraph') {
      const g = this.callGraph();
      return g ? callGraphFlowchart(g, addr => this.nameMap().get(hexKey(addr)) ?? '') : '';
    }

    const key = this.selectedKey();
    if (!key) { return ''; }
    const [kind, value] = key.split(':', 2);

    if (kind === 'group') {
      const grp = this.groups().find(g => String(g.id) === value);
      if (!grp) { return ''; }
      return mode === 'vtable' ? vtableDiagramFromGroup(grp) : classDiagramFromGroup(grp);
    }

    const label = this.entries().find(e => e.address === value);
    if (!label) { return ''; }
    const children = this.entries().filter(e => e.parentAddress && hexKey(e.parentAddress) === hexKey(value));
    return kind === 'vt'
      ? vtableDiagramFromLabel(label, children)
      : classDiagramFromStruct(label, children);
  }

  private async renderDiagram(src: string): Promise<void> {
    this.source.set(src);
    const host = this.diagramRef?.nativeElement;
    if (!host) { return; }
    if (!src) { host.innerHTML = ''; return; }
    try {
      const { svg } = await mermaid.render('sv-mmd-' + (this.renderSeq++), src);
      host.innerHTML = svg;
      this.wireNodeClicks(host);
      this.error.set('');
    } catch (e) {
      host.innerHTML = '';
      this.error.set('Diagram render failed: ' + (e instanceof Error ? e.message : String(e)));
    }
  }

  /** Attach click-to-navigate to any diagram node whose id encodes an address. */
  private wireNodeClicks(host: HTMLElement): void {
    const nodes = host.querySelectorAll<SVGElement>('.node, .classGroup, g[id]');
    nodes.forEach(node => {
      const id = node.getAttribute('id') ?? '';
      const m  = /[ncv]([0-9a-fA-F]{8})/.exec(id);
      if (!m) { return; }
      node.style.cursor = 'pointer';
      node.addEventListener('click', () => {
        this.vscode.postMessage({ type: 'navigate', address: '0x' + m[1] });
      });
    });
  }
}
