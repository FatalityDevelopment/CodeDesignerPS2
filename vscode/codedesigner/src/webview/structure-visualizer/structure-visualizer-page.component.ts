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
import { IconComponent } from '../shared/icon.component';
import { PageHeaderComponent } from '../shared/page-header.component';
import '../shared/styles/shared.css';
import {
  SvLabel, SvGroup, SvCallGraph, SvNodeResolved,
  classDiagramFromStruct, classDiagramFromGroup,
  vtableDiagramFromLabel, vtableDiagramFromGroup,
  callGraphFlowchart, hexKey,
} from './mermaid-generators';
import './structure-visualizer-page.css';

type Mode = 'class' | 'vtable' | 'callgraph';

/**
 * Structure Visualizer — renders Mermaid diagrams of a PS2 game's structure and
 * lets the user build that structure in place: typed labels (Struct + fields,
 * ArrayOfPointers vtables, Function labels) and label groups ("objectmap").
 * The loaded game is resolved automatically by the extension host.
 */
@Component({
  selector: 'app-structure-visualizer-page',
  standalone: true,
  imports: [IconComponent, PageHeaderComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="sv-shell">
      <app-page-header icon="workflow" title="Structure" subtitle="Structure visualizer" />
      <!-- ── Toolbar ───────────────────────────────────────────────────── -->
      <div class="sv-toolbar">
        <span class="sv-title"><app-icon name="workflow" [size]="14" /> Structure Visualizer</span>
        <select class="sv-select" [value]="setName()" (change)="onSetPick($event)"
                [disabled]="sets().length === 0">
          @for (s of sets(); track s) { <option [value]="s">{{ s }}</option> }
          @if (sets().length === 0) { <option value="">— no label sets —</option> }
        </select>
        <button class="lm-tool-btn" [disabled]="busy()" (click)="reload()" title="Reload from backend">⟳</button>
        <button class="lm-tool-btn" (click)="newSet()" title="Create a new label set">+ Set</button>
        <span class="sv-spacer"></span>
        <div class="sv-zoom">
          <button class="lm-tool-btn" [disabled]="!source()" (click)="zoomOut()" title="Zoom out">−</button>
          <button class="lm-tool-btn" [disabled]="!source()" (click)="zoomReset()" title="Reset to 100%">{{ zoomPct() }}%</button>
          <button class="lm-tool-btn" [disabled]="!source()" (click)="zoomIn()" title="Zoom in">+</button>
          <button class="lm-tool-btn" [disabled]="!source()" (click)="fitToViewport(false)" title="Fit the whole diagram in view">Fit</button>
        </div>
        <button class="lm-tool-btn" [class.sv-on]="editMode()" (click)="toggleEdit()">{{ editMode() ? '✓ Editing' : '✎ Edit' }}</button>
        <button class="lm-tool-btn" [disabled]="!source()" (click)="copySource()">Copy Mermaid</button>
        <button class="lm-tool-btn" [disabled]="!source()" (click)="exportSvg()">Export SVG</button>
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
              <optgroup label="Struct &amp; Pointer labels">
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
              <optgroup label="Array / VTable labels">
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
          <input class="sv-input" type="text" placeholder="Focus address (optional, 0x…)"
                 [value]="focusAddr()" (input)="onFocusAddr($event)" />
          <select class="sv-select" [value]="direction()" (change)="onDirection($event)">
            <option value="both">Callers + callees</option>
            <option value="callers">Callers (back to hook)</option>
            <option value="callees">Callees</option>
          </select>
          <label class="sv-depth">Depth {{ depth() }}
            <input type="range" min="1" max="8" [value]="depth()" (input)="onDepth($event)" />
          </label>
          <button class="lm-tool-btn sv-primary" [disabled]="busy() || entries().length === 0" (click)="buildCallGraph()">Build</button>
          <span class="sv-sep"></span>
          <input class="sv-input sv-sm" type="text" placeholder="Scan from 0x…"
                 [value]="scanAddr()" (input)="onScanAddr($event)" />
          <input class="sv-input sv-sm" type="text" placeholder="Scan length"
                 [value]="scanLen()" (input)="onScanLen($event)" />
          <button class="lm-tool-btn" [disabled]="busy() || !scanAddr() || !scanLen()" (click)="scanCallGraph()">Scan calls</button>
        }
      </div>

      @if (status()) { <div class="sv-status">{{ status() }}</div> }
      @if (error())  { <div class="sv-status sv-error">{{ error() }}</div> }

      <!-- ── Body: diagram + editor ────────────────────────────────────── -->
      <div class="sv-body">
        <div class="sv-diagram" #diagram></div>
        @if (!source() && !error()) {
          <div class="sv-empty">
            Pick a struct, class group or vtable to visualise — or turn on
            <strong>Edit</strong> to build one. In Call Graph mode, click a node
            to jump to that address in the disassembler.
          </div>
        }

        @if (editMode()) {
          <div class="sv-editor">
            @if (mode() === 'callgraph') {
              <div class="sv-ed-note">Call graphs are derived from the scanned call edges — nothing to edit here. Use the other tabs to build structures.</div>
            } @else {
              <!-- ── Label builder ─────────────────────────────────────── -->
              <div class="sv-ed-section">
                <div class="sv-ed-head">{{ mode() === 'vtable' ? 'New vtable' : 'New struct / function' }}</div>
                <input class="sv-input sv-full" type="text" placeholder="Name"
                       [value]="lblName()" (input)="set(lblName, $event)" />
                <input class="sv-input sv-full" type="text" placeholder="Address (0x…)"
                       [value]="lblAddr()" (input)="set(lblAddr, $event)" />
                <select class="sv-select sv-full" [value]="lblKind()" (change)="set(lblKind, $event)">
                  @if (mode() === 'vtable') {
                    <option value="ArrayOfPointers">ArrayOfPointers (vtable)</option>
                  } @else {
                    <option value="Struct">Struct</option>
                    <option value="Function">Function</option>
                  }
                </select>
                @if (lblKind() === 'Function') {
                  <input class="sv-input sv-full" type="text" placeholder="Arg regs, e.g. a0=playerPtr, a1=flags"
                         [value]="lblArgs()" (input)="set(lblArgs, $event)" />
                }
                <button class="lm-tool-btn sv-primary sv-full" [disabled]="busy() || !lblName() || !lblAddr()"
                        (click)="createLabel()">Create label</button>
              </div>

              <!-- ── Field editor for the selected struct/vtable ───────── -->
              @if (selectedLabel(); as sel) {
                <div class="sv-ed-section">
                  <div class="sv-ed-head">Fields of {{ sel.name }}</div>
                  @for (c of selectedChildren(); track c.address) {
                    <div class="sv-ed-row">
                      <span class="sv-ed-off">+0x{{ hex(c.offset) }}</span>
                      <span class="sv-ed-name">{{ c.name }}</span>
                      <span class="sv-ed-vt">{{ c.valueType || c.type }}</span>
                      <button class="lm-row-btn lm-danger-btn" (click)="deleteLabel(c.address)" title="Delete">✕</button>
                    </div>
                  }
                  @if (selectedChildren().length === 0) {
                    <div class="sv-ed-note">No fields yet.</div>
                  }
                  <div class="sv-ed-addrow">
                    <input class="sv-input sv-xs" type="text" placeholder="Offset" [value]="fldOffset()" (input)="set(fldOffset, $event)" />
                    <input class="sv-input sv-sm" type="text" placeholder="Field name" [value]="fldName()" (input)="set(fldName, $event)" />
                    <input class="sv-input sv-xs" type="text" placeholder="Type" [value]="fldType()" (input)="set(fldType, $event)" />
                    <button class="lm-tool-btn" [disabled]="busy() || !fldName() || !fldOffset()" (click)="addField(sel)">+ Field</button>
                  </div>
                </div>
              }

              <!-- ── Group editor ──────────────────────────────────────── -->
              <div class="sv-ed-section">
                <div class="sv-ed-head">{{ mode() === 'vtable' ? 'VTable groups' : 'Class groups' }}</div>
                @for (g of modeGroups(); track g.id) {
                  <div class="sv-ed-row">
                    <span class="sv-ed-name">{{ g.name }}</span>
                    <span class="sv-ed-vt">{{ g.members.length }} member(s)</span>
                    <button class="lm-row-btn lm-danger-btn" (click)="deleteGroup(g.name)" title="Delete group">✕</button>
                  </div>
                }
                <div class="sv-ed-addrow">
                  <input class="sv-input sv-sm" type="text" placeholder="Group name" [value]="grpName()" (input)="set(grpName, $event)" />
                  <input class="sv-input sv-xs" type="text" placeholder="#color" [value]="grpColor()" (input)="set(grpColor, $event)" />
                  <button class="lm-tool-btn" [disabled]="busy() || !grpName()" (click)="createGroup()">+ Group</button>
                </div>
              </div>

              <!-- ── Membership editor for the selected group ──────────── -->
              @if (selectedGroup(); as grp) {
                <div class="sv-ed-section">
                  <div class="sv-ed-head">Members of {{ grp.name }}</div>
                  @for (m of grp.members; track m.id) {
                    <div class="sv-ed-row">
                      <span class="sv-ed-name">{{ m.labelName }}</span>
                      <span class="sv-ed-vt">{{ m.role || m.labelType }}</span>
                      <button class="lm-row-btn lm-danger-btn" (click)="removeMember(grp.name, m.labelAddress)" title="Remove">✕</button>
                    </div>
                  }
                  <div class="sv-ed-addrow">
                    <select class="sv-select sv-sm" [value]="memberAddr()" (change)="set(memberAddr, $event)">
                      <option value="">— label —</option>
                      @for (e of entries(); track e.address) {
                        <option [value]="e.address">{{ e.name }} ({{ e.type }})</option>
                      }
                    </select>
                    <input class="sv-input sv-xs" type="text" placeholder="Role" [value]="memberRole()" (input)="set(memberRole, $event)" />
                    <button class="lm-tool-btn" [disabled]="busy() || !memberAddr()" (click)="addMember(grp.name)">+ Member</button>
                  </div>
                </div>
              }
            }
          </div>
        }
      </div>
    </div>
  `,
})
export class StructureVisualizerPageComponent implements AfterViewInit {
  @ViewChild('diagram') private diagramRef?: ElementRef<HTMLDivElement>;

  private readonly vscode = getVsCodeApi();
  private renderSeq = 0;
  private panHost?: HTMLElement;
  private panPointerId: number | null = null;
  private panStartX = 0;
  private panStartY = 0;
  private panStartLeft = 0;
  private panStartTop = 0;
  private panMoved = false;
  private suppressNextNodeClick = false;
  /** Natural (unscaled) diagram size, captured after each render for zooming. */
  private baseW = 0;
  private baseH = 0;

  // ── Context & data ────────────────────────────────────────────────────
  readonly gameName  = signal('');
  readonly gameId    = signal('');
  readonly sets      = signal<string[]>([]);
  readonly setName   = signal('');
  readonly entries   = signal<SvLabel[]>([]);
  readonly groups    = signal<SvGroup[]>([]);

  // ── View state ────────────────────────────────────────────────────────
  readonly mode        = signal<Mode>('class');
  readonly selectedKey = signal('');
  readonly editMode    = signal(false);
  readonly focusAddr   = signal('');
  readonly direction   = signal('both');
  readonly depth       = signal(3);
  readonly scanAddr    = signal('');
  readonly scanLen     = signal('');
  readonly callGraph   = signal<SvCallGraph | null>(null);
  readonly busy        = signal(false);
  readonly status      = signal('');
  readonly error       = signal('');
  readonly source      = signal('');
  readonly zoom        = signal(1);

  // ── Editor form fields ────────────────────────────────────────────────
  readonly lblName   = signal('');
  readonly lblAddr   = signal('');
  readonly lblKind   = signal('Struct');
  readonly lblArgs   = signal('');
  readonly fldOffset = signal('');
  readonly fldName   = signal('');
  readonly fldType   = signal('');
  readonly grpName   = signal('');
  readonly grpColor  = signal('');
  readonly memberAddr = signal('');
  readonly memberRole = signal('');

  // ── Derived collections ───────────────────────────────────────────────
  // "Pointer" and "Struct" describe the same thing — an address holding a
  // pointer whose dereferenced value has child fields — so both render as a
  // class. Matched case-insensitively to tolerate wire-format variations.
  readonly structOptions = computed(() => this.entries().filter(e => {
    const t = (e.type ?? '').toLowerCase();
    return (t === 'struct' || t === 'pointer') && !e.parentAddress;
  }));
  // A vtable is an array of function pointers — accept both ArrayOfPointers
  // and plain Array labels (case-insensitive) as candidates.
  readonly vtableOptions  = computed(() => this.entries().filter(e => {
    const t = (e.type ?? '').toLowerCase();
    return t === 'arrayofpointers' || t === 'array';
  }));
  readonly classGroups    = computed(() => this.groups().filter(g => g.kind === 'Class'));
  readonly vtableGroups   = computed(() => this.groups().filter(g => g.kind === 'VTable'));
  readonly modeGroups     = computed(() => this.mode() === 'vtable' ? this.vtableGroups() : this.classGroups());

  /** The label currently selected in class/vtable mode, if any. */
  readonly selectedLabel = computed<SvLabel | null>(() => {
    const [kind, value] = this.selectedKey().split(':', 2);
    if (kind !== 'struct' && kind !== 'vt') { return null; }
    return this.entries().find(e => e.address === value) ?? null;
  });

  readonly selectedChildren = computed<SvLabel[]>(() => {
    const sel = this.selectedLabel();
    if (!sel) { return []; }
    return this.entries()
      .filter(e => e.parentAddress && hexKey(e.parentAddress) === hexKey(sel.address))
      .sort((a, b) => (a.offset ?? 0) - (b.offset ?? 0));
  });

  /** The group currently selected in class/vtable mode, if any. */
  readonly selectedGroup = computed<SvGroup | null>(() => {
    const [kind, value] = this.selectedKey().split(':', 2);
    if (kind !== 'group') { return null; }
    return this.groups().find(g => String(g.id) === value) ?? null;
  });

  constructor() {
    mermaid.initialize({
      startOnLoad: false,
      securityLevel: 'strict',
      theme: 'dark',
      flowchart: { useMaxWidth: true },
    });
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
      case 'init': {
        this.gameName.set(String(msg['gameName'] ?? ''));
        this.gameId.set(String(msg['gameId'] ?? ''));
        this.sets.set((msg['sets'] as string[]) ?? []);
        const active = String(msg['activeSet'] ?? '');
        this.setName.set(active);
        this.busy.set(false);
        if (active) {
          this.status.set(`Loading set "${active}"…`);
          this.loadData();
        } else {
          this.status.set(this.sets().length ? 'Pick a label set.' : 'No label sets for this game yet.');
        }
        break;
      }
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
        this.busy.set(false);
        this.status.set('Groups updated.');
        break;
      case 'callGraph':
        {
          const graph = msg['graph'] as SvCallGraph;
          this.callGraph.set(graph);
          const edgeCount = graph?.edges?.length ?? 0;
          const nodeCount = graph?.nodes?.length ?? 0;
          const labelled = graph?.nodeInfo?.filter(n => !!n.label).length ?? 0;
          const signatures = graph?.nodeInfo?.filter(n => (n.argRegs?.length ?? 0) > 0 || (n.returnRegs?.length ?? 0) > 0).length ?? 0;
          this.status.set(edgeCount > 0
            ? `Call graph built: ${nodeCount} node(s), ${edgeCount} edge(s), ${labelled} label(s), ${signatures} signature(s).`
            : 'No call edges found. Scan the code segment, then build again.');
        }
        this.busy.set(false);
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

  // ── Toolbar / mode ────────────────────────────────────────────────────

  onSetPick(e: Event): void {
    this.setName.set((e.target as HTMLSelectElement).value);
    this.loadData();
  }

  reload(): void {
    this.error.set('');
    this.busy.set(true);
    this.status.set('Reloading…');
    this.vscode.postMessage({ type: 'refresh' });
  }

  newSet(): void {
    this.error.set('');
    this.vscode.postMessage({ type: 'createSet' });
  }

  loadData(): void {
    if (!this.setName()) { return; }
    this.error.set('');
    this.busy.set(true);
    this.vscode.postMessage({ type: 'loadData', setName: this.setName() });
  }

  toggleEdit(): void { this.editMode.update(v => !v); }

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

  /** Generic input/select binder used by the editor forms. */
  set(sig: { set(v: string): void }, e: Event): void {
    sig.set((e.target as HTMLInputElement | HTMLSelectElement).value);
  }

  // ── Call graph ────────────────────────────────────────────────────────

  buildCallGraph(): void {
    this.error.set('');
    this.busy.set(true);
    this.status.set('Building call graph…');
    const selected = this.selectedLabel();
    const focus = this.focusAddr().trim() || selected?.address || this.entries().find(e => e.type === 'Function')?.address || '';
    this.vscode.postMessage({
      type: 'getCallGraph',
      address: focus,
      depth: this.depth(),
      direction: this.direction(),
    });
  }

  scanCallGraph(): void {
    this.error.set('');
    this.busy.set(true);
    this.status.set('Scanning code for calls…');
    const raw = String(this.scanLen() ?? '').trim();
    const lower = raw.toLowerCase();
    const len = lower.startsWith('0x') ? parseInt(raw.slice(2), 16) : parseInt(raw, 10);
    this.vscode.postMessage({ type: 'scanCallGraph', address: this.scanAddr().trim(), length: len });
  }

  // ── Editor actions ────────────────────────────────────────────────────

  createLabel(): void {
    const args = this.lblArgs().split(',').map(s => s.trim()).filter(Boolean);
    const entry: Record<string, unknown> = {
      address: this.lblAddr().trim(),
      name: this.lblName().trim(),
      type: this.lblKind(),
    };
    if (this.lblKind() === 'Function' && args.length) { entry['argRegs'] = args; }
    this.send('saveLabel', { entry });
    this.lblName.set(''); this.lblAddr.set(''); this.lblArgs.set('');
    this.status.set('Creating label…');
  }

  addField(parent: SvLabel): void {
    const offset = this.parseInt(this.fldOffset());
    if (offset === undefined) { this.error.set('Field offset must be a number.'); return; }
    const base = parent.address.replace(/:.*$/, '');
    const entry: Record<string, unknown> = {
      address: `${base}:+0x${offset.toString(16).toUpperCase().padStart(4, '0')}`,
      parentAddress: base,
      offset,
      name: this.fldName().trim(),
      type: 'Address',
    };
    const vt = this.fldType().trim();
    if (vt) { entry['valueType'] = vt; }
    this.send('saveLabel', { entry });
    this.fldOffset.set(''); this.fldName.set(''); this.fldType.set('');
    this.status.set('Adding field…');
  }

  deleteLabel(address: string): void {
    this.send('deleteLabel', { address });
    this.status.set('Deleting label…');
  }

  createGroup(): void {
    this.send('saveGroup', {
      name: this.grpName().trim(),
      kind: this.mode() === 'vtable' ? 'VTable' : 'Class',
      color: this.grpColor().trim() || undefined,
    });
    this.grpName.set(''); this.grpColor.set('');
    this.status.set('Creating group…');
  }

  deleteGroup(groupName: string): void {
    this.send('deleteGroup', { groupName });
    this.status.set('Deleting group…');
  }

  addMember(groupName: string): void {
    this.send('addMember', {
      groupName,
      labelAddress: this.memberAddr(),
      role: this.memberRole().trim() || undefined,
    });
    this.memberAddr.set(''); this.memberRole.set('');
    this.status.set('Adding member…');
  }

  removeMember(groupName: string, labelAddress: string): void {
    this.send('removeMember', { groupName, labelAddress });
    this.status.set('Removing member…');
  }

  private send(type: string, payload: Record<string, unknown>): void {
    this.error.set('');
    this.busy.set(true);
    this.vscode.postMessage({ type, setName: this.setName(), ...payload });
  }

  private parseInt(raw: string | undefined | null): number | undefined {
    const t = String(raw ?? '').trim();
    if (!t) { return undefined; }
    const lower = t.toLowerCase();
    const n = lower.startsWith('0x') ? parseInt(t.slice(2), 16) : parseInt(t, 10);
    return Number.isFinite(n) ? n : undefined;
  }

  hex(n: number | undefined): string {
    return (n ?? 0).toString(16).toUpperCase();
  }

  // ── Export ────────────────────────────────────────────────────────────

  copySource(): void {
    const src = this.source();
    if (src) { navigator.clipboard?.writeText(src).catch(() => { /* ignore */ }); }
  }

  exportSvg(): void {
    const svg = this.diagramRef?.nativeElement.querySelector('svg');
    if (!svg) { return; }
    const url = URL.createObjectURL(new Blob([svg.outerHTML], { type: 'image/svg+xml' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = 'structure-diagram.svg';
    a.click();
    URL.revokeObjectURL(url);
  }

  // ── Diagram building & rendering ──────────────────────────────────────

  private buildSource(): string {
    const mode = this.mode();
    if (mode === 'callgraph') {
      const g = this.callGraph();
      if (!g) { return ''; }
      // Resolve each node against the loaded labels so call-graph functions
      // show the same name + argument/return registers as class diagrams.
      const byKey = new Map<string, SvLabel>();
      for (const e of this.entries()) { byKey.set(hexKey(e.address), e); }
      const resolve = (addr: string): SvNodeResolved | undefined => {
        const e = byKey.get(hexKey(addr));
        return e ? { label: e.name, argRegs: e.argRegs, returnRegs: e.returnRegs } : undefined;
      };
      return callGraphFlowchart(g, resolve);
    }

    const key = this.selectedKey();
    if (!key) { return ''; }
    const [kind, value] = key.split(':', 2);

    if (kind === 'group') {
      const grp = this.groups().find(g => String(g.id) === value);
      if (!grp) { return ''; }
      // Resolve member addresses to full labels so Function members can show
      // their argument / return register signatures.
      const resolve = (addr: string): SvLabel | undefined =>
        this.entries().find(e => hexKey(e.address) === hexKey(addr));
      return mode === 'vtable'
        ? vtableDiagramFromGroup(grp, resolve)
        : classDiagramFromGroup(grp, resolve);
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
      this.prepareScrollableSvg(host);
      this.fitToViewport(true);
      this.wireDiagramPan(host);
      this.wireNodeClicks(host);
      this.error.set('');
    } catch (e) {
      host.innerHTML = '';
      this.error.set('Diagram render failed: ' + (e instanceof Error ? e.message : String(e)));
    }
  }

  /** Attach click-to-navigate to any diagram node whose id encodes an address. */
  private wireNodeClicks(host: HTMLElement): void {
    host.querySelectorAll<SVGElement>('.node, .classGroup, g[id]').forEach(node => {
      const m = /[ncv]([0-9a-fA-F]{8})/.exec(node.getAttribute('id') ?? '');
      if (!m) { return; }
      node.style.cursor = 'pointer';
      node.addEventListener('click', (event) => {
        if (this.suppressNextNodeClick) {
          event.preventDefault();
          event.stopPropagation();
          return;
        }
        this.vscode.postMessage({ type: 'navigate', address: '0x' + m[1] });
      });
    });
  }

  /** Capture the diagram's natural size so the zoom controls can scale it. */
  private prepareScrollableSvg(host: HTMLElement): void {
    const svg = host.querySelector<SVGSVGElement>('svg');
    if (!svg) { this.baseW = 0; this.baseH = 0; return; }
    svg.style.maxWidth = 'none';
    const vb = svg.viewBox.baseVal;
    this.baseW = vb?.width  ? Math.ceil(vb.width)  : svg.clientWidth  || 0;
    this.baseH = vb?.height ? Math.ceil(vb.height) : svg.clientHeight || 0;
  }

  /** Scale the rendered SVG to the current zoom level. */
  private applyZoom(): void {
    const svg = this.diagramRef?.nativeElement.querySelector<SVGSVGElement>('svg');
    if (!svg || !this.baseW || !this.baseH) { return; }
    const z = this.zoom();
    const w = Math.ceil(this.baseW * z);
    const h = Math.ceil(this.baseH * z);
    // Set inline styles, not just attributes — a stylesheet `width: auto` rule
    // beats the attribute, leaving the SVG un-resized so it never overflows
    // the pane and can't be scrolled or panned.
    svg.setAttribute('width',  String(w));
    svg.setAttribute('height', String(h));
    svg.style.width    = `${w}px`;
    svg.style.height   = `${h}px`;
    svg.style.maxWidth = 'none';
  }

  private setZoom(z: number): void {
    this.zoom.set(Math.min(6, Math.max(0.05, z)));
    this.applyZoom();
  }

  zoomIn(): void { this.setZoom(this.zoom() * 1.2); }
  zoomOut(): void { this.setZoom(this.zoom() / 1.2); }
  zoomReset(): void { this.setZoom(1); }
  zoomPct(): number { return Math.round(this.zoom() * 100); }

  /**
   * Scale the diagram to fit the viewport. When <auto> is true (called after a
   * fresh render) it only shrinks oversized diagrams — small ones stay 1:1.
   */
  fitToViewport(auto: boolean): void {
    const host = this.diagramRef?.nativeElement;
    if (!host || !this.baseW || !this.baseH) { return; }
    const pad = 28;
    const fit = Math.min(
      (host.clientWidth  - pad) / this.baseW,
      (host.clientHeight - pad) / this.baseH,
    );
    if (!Number.isFinite(fit) || fit <= 0) { this.applyZoom(); return; }
    this.setZoom(auto ? Math.min(1, fit) : fit);
    host.scrollTo({ left: 0, top: 0 });
  }

  private wireDiagramPan(host: HTMLElement): void {
    if (this.panHost === host) { return; }
    this.panHost = host;

    // Ctrl/Cmd + wheel zooms; a plain wheel still scrolls the diagram.
    host.addEventListener('wheel', (event: WheelEvent) => {
      if (!event.ctrlKey && !event.metaKey) { return; }
      event.preventDefault();
      this.setZoom(this.zoom() * (event.deltaY < 0 ? 1.15 : 1 / 1.15));
    }, { passive: false });

    host.addEventListener('pointerdown', (event: PointerEvent) => {
      if (event.button !== 0) { return; }
      this.panPointerId = event.pointerId;
      this.panStartX = event.clientX;
      this.panStartY = event.clientY;
      this.panStartLeft = host.scrollLeft;
      this.panStartTop = host.scrollTop;
      this.panMoved = false;
      host.classList.add('sv-panning');
      host.setPointerCapture(event.pointerId);
    });

    host.addEventListener('pointermove', (event: PointerEvent) => {
      if (this.panPointerId !== event.pointerId) { return; }
      const dx = event.clientX - this.panStartX;
      const dy = event.clientY - this.panStartY;
      if (Math.abs(dx) > 3 || Math.abs(dy) > 3) {
        this.panMoved = true;
        this.suppressNextNodeClick = true;
      }
      host.scrollLeft = this.panStartLeft - dx;
      host.scrollTop = this.panStartTop - dy;
      event.preventDefault();
    });

    const finishPan = (event: PointerEvent) => {
      if (this.panPointerId !== event.pointerId) { return; }
      this.panPointerId = null;
      host.classList.remove('sv-panning');
      try { host.releasePointerCapture(event.pointerId); } catch { /* already released */ }
      if (this.panMoved) {
        window.setTimeout(() => { this.suppressNextNodeClick = false; }, 0);
      }
    };

    host.addEventListener('pointerup', finishPan);
    host.addEventListener('pointercancel', finishPan);
  }
}
