import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  ViewChild,
  ViewEncapsulation,
  computed,
  signal,
} from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import { IconComponent } from '../shared/icon.component';
import type { ModelTexturePixels } from '../zipper-workbench/zed-model-renderer';
import type { Shading, ViewPreset } from '../zipper-core/viewport-core';
import { AssetClient, type MapInstanceWire, type MapPointWire, type ModelSummary, type SessionStatus } from '../model-editor/asset-client';
import { TextureCache } from '../model-editor/model-loader';
import { decodeMesh, type MeshWire } from '../model-editor/mesh-data';
import { MapViewport, type MapLayer, type MapModel, type MapTool } from './map-viewport';
import '../shared/styles/shared.css';
import '../model-editor/model-editor-page.css';
import './map-editor-page.css';

type PropTab = 'item' | 'place' | 'points' | 'map';

interface MenuItem {
  label: string;
  shortcut?: string;
  action?: () => void;
  disabled?: () => boolean;
  checked?: () => boolean;
  separator?: boolean;
}

interface Menu { id: string; label: string; items: MenuItem[] }

interface ModelGroup { source: string; model: string; indices: number[] }

const LAYERS: Array<{ id: MapLayer; label: string }> = [
  { id: 'world', label: 'World' }, { id: 'reference', label: 'Props' }, { id: 'clutter', label: 'Clutter' },
  { id: 'sky', label: 'Sky' }, { id: 'points', label: 'Points' },
];

function layerOf(inst: MapInstanceWire): MapLayer {
  if (inst.kind === 'clutter') { return 'clutter'; }
  if (inst.kind === 'model') { return 'world'; }
  if (/^(sky|thesun|flare|mist)/i.test(inst.model)) { return 'sky'; }
  return 'reference';
}

// Map Editor: the composed map of a package (worldmodel, GEO props, clutter, AIMAPS points). Every change is a
// bridge asset-session edit (moveInstance / moveClutter / movePoint / duplicateInstance / addClutter / delete*),
// the same ops as assets_edit, so an agent and this page can build a map together; the viewport only previews.
@Component({
  selector: 'app-map-editor-page',
  standalone: true,
  imports: [IconComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="me-app" (pointerdown)="closeMenusOutside($event)">
  <div class="me-menubar">
    <span class="me-brand"><app-icon name="globe" [size]="14" /> Map Editor</span>
    @for (menu of menus; track menu.id) {
      <div class="me-menu" [class.open]="openMenu() === menu.id">
        <button class="me-menu-btn" (click)="toggleMenu(menu.id)" (mouseenter)="hoverMenu(menu.id)">{{ menu.label }}</button>
        @if (openMenu() === menu.id) {
          <div class="me-menu-pop">
            @for (item of menu.items; track $index) {
              @if (item.separator) { <div class="me-menu-sep"></div> }
              @else {
                <button class="me-menu-item" [disabled]="item.disabled?.()" (click)="runMenu(item)">
                  <span class="me-menu-check">{{ item.checked?.() ? '✓' : '' }}</span>
                  <span class="me-menu-label">{{ item.label }}</span>
                  <span class="me-menu-key">{{ item.shortcut ?? '' }}</span>
                </button>
              }
            }
          </div>
        }
      </div>
    }
    <span class="me-spacer"></span>
    @if (status(); as s) {
      <span class="me-pkg" [title]="s.path">{{ fileName(s.path) }}</span>
      @if (s.game) { <span class="me-badge" title="Edits, saves and animations are not available for this game yet">{{ s.game }} · read-only</span> }
      @else { <span class="me-badge" [class.me-badge-dirty]="dirtyCount() > 0">{{ dirtyCount() ? dirtyCount() + ' unsaved' : 'saved' }} · rev {{ s.revision }}</span> }
      <span class="me-session" title="Asset session: assets_* MCP tools can work on it too">{{ s.session }}</span>
    }
  </div>

  <div class="me-header">
    <div class="me-seg" title="Shading (Z cycles)">
      @for (s of shadings; track s.id) {
        <button [class.on]="shading() === s.id" (click)="setShading(s.id)">{{ s.label }}</button>
      }
    </div>
    <label class="me-toggle"><input type="checkbox" [checked]="baked()" (change)="setBaked($any($event.target).checked)" /> Baked lighting</label>
    <label class="me-toggle"><input type="checkbox" [checked]="grid()" (change)="setGrid($any($event.target).checked)" /> Grid</label>
    <label class="me-toggle"><input type="checkbox" [checked]="snap()" (change)="snap.set($any($event.target).checked); applySnap()" /> Snap</label>
    @if (snap()) {
      <input class="me-num me-num-sm" type="number" min="0.001" step="1" [value]="snapMove()" (change)="snapMove.set(+$any($event.target).value || 1); applySnap()" title="Move snap (units)" />
      <input class="me-num me-num-sm" type="number" min="1" step="5" [value]="snapRotate()" (change)="snapRotate.set(+$any($event.target).value || 15); applySnap()" title="Rotate snap (degrees)" />
    }
    <label class="me-toggle" title="WASD fly speed (Shift x4)">Fly <input class="me-num me-num-sm" type="number" min="1" step="10" [value]="flySpeed()" (change)="setFlySpeed(+$any($event.target).value)" /></label>
    <span class="me-spacer"></span>
    <div class="me-seg" title="View">
      <button (click)="view('top')" title="Top (Numpad 7)">Top</button>
      <button (click)="view('front')" title="Front (Numpad 1)">Front</button>
      <button (click)="view('iso')" title="Iso">Iso</button>
      <button [class.on]="ortho()" (click)="toggleOrtho()" title="Orthographic (Numpad 5)">Ortho</button>
      <button (click)="frameSelected()" title="Frame selected (F)">Frame</button>
      <button (click)="viewport.frameAll()" title="Frame map (Home)">All</button>
    </div>
  </div>

  <div class="me-body no-assets">
    <aside class="me-assets"></aside>
    <nav class="me-tools">
      @for (t of tools; track t.id) {
        <button class="me-tool" [class.on]="tool() === t.id" (click)="setTool(t.id)" [title]="t.label + ' (' + t.key + ')'">
          <span class="me-tool-glyph">{{ t.glyph }}</span>
        </button>
      }
    </nav>

    <section class="me-center">
      <div class="me-viewport">
        <canvas #canvas tabindex="0"></canvas>
        <div class="me-vp-label">
          <div>{{ viewLabel() }} · {{ toolLabel() }}</div>
          @if (sceneNs()) { <div class="lm-muted">{{ sceneNs() }} — {{ instances().length }} instances · {{ points().length }} points</div> }
          <div class="lm-muted">WASD fly · Q/E down/up · Shift faster</div>
        </div>
        @if (!status()) { <div class="me-vp-center">File › Open package… (Ctrl+O)</div> }
        @if (progress()) { <div class="mp-progress">{{ progress() }}</div> }
        @if (busy()) { <div class="me-vp-busy">Applying…</div> }
      </div>
    </section>

    <aside class="me-side">
      <div class="me-outliner">
        <div class="me-panel-title">Outliner</div>
        <input class="me-search" type="text" placeholder="Filter…" [value]="query()" (input)="query.set($any($event.target).value)" />
        <div class="me-tree">
          @for (layer of layers; track layer.id) {
            <div class="mp-group">
              <button class="mp-twist" (click)="toggleOpen('L:' + layer.id)">{{ isOpen('L:' + layer.id) ? '▾' : '▸' }}</button>
              <button class="me-eye" (click)="toggleLayer(layer.id)" [title]="layerHidden(layer.id) ? 'Show' : 'Hide'">{{ layerHidden(layer.id) ? '◌' : '●' }}</button>
              <span>{{ layer.label }}</span>
              <span class="lm-muted">{{ layer.id === 'points' ? points().length : layerCount(layer.id) }}</span>
            </div>
            @if (isOpen('L:' + layer.id)) {
              @if (layer.id === 'points') {
                @for (p of filteredPoints(); track p.name) {
                  <div class="me-tree-row mp-model" [class.sel]="selectedPoint() === p.name">
                    <span (click)="pickPoint(p.name)">{{ p.name }}</span>
                    <span class="lm-muted">{{ p.kind }}{{ p.team ? ' · t' + p.team : '' }}</span>
                  </div>
                }
              } @else {
                @for (g of groupsOf(layer.id); track g.source) {
                  <div class="me-tree-row mp-model" [class.sel]="groupSelected(g)">
                    <button class="mp-twist" (click)="toggleOpen('M:' + layer.id + g.source)">{{ g.indices.length > 1 ? (isOpen('M:' + layer.id + g.source) ? '▾' : '▸') : '' }}</button>
                    <button class="me-eye" (click)="toggleModel(g.source)" [title]="modelHidden(g.source) ? 'Show' : 'Hide'">{{ modelHidden(g.source) ? '◌' : '●' }}</button>
                    <span (click)="pickGroup(g, $event)" [title]="g.source">{{ g.model }}</span>
                    <span class="lm-muted">{{ g.indices.length }}</span>
                  </div>
                  @if (g.indices.length > 1 && isOpen('M:' + layer.id + g.source)) {
                    @for (i of g.indices.slice(0, 300); track i) {
                      <div class="me-tree-row mp-inst" [class.sel]="isSelected(i)">
                        <span (click)="pickInstance(i, $event)">{{ instanceLabel(i) }}</span>
                      </div>
                    }
                  }
                }
              }
            }
          }
        </div>
      </div>

      <div class="me-props">
        <div class="me-tabs">
          @for (t of propTabs; track t.id) {
            <button class="me-tab" [class.active]="propTab() === t.id" (click)="propTab.set(t.id)">{{ t.label }}</button>
          }
        </div>
        <div class="me-tab-body">
          @switch (propTab()) {
            @case ('item') {
              @if (selectedPoint(); as pn) {
                @if (pointInfo(); as p) {
                  <div class="me-card">
                    <div class="me-card-title">Point {{ p.name }}</div>
                    <dl class="me-kv"><dt>Kind</dt><dd>{{ p.kind }}</dd><dt>Team</dt><dd>{{ p.team }}</dd><dt>Cell</dt><dd>{{ p.cellX }}, {{ p.cellZ }}</dd></dl>
                    <div class="me-xform">
                      <span>X / Z</span>
                      <input class="me-num" type="number" step="1" [value]="fmt(p.x)" (change)="movePointField(pn, 'x', $any($event.target).value)" />
                      <input class="me-num" type="number" disabled [value]="'—'" title="Points have no height (on the ground)" />
                      <input class="me-num" type="number" step="1" [value]="fmt(p.z)" (change)="movePointField(pn, 'z', $any($event.target).value)" />
                    </div>
                    <div class="lm-muted me-note">AIMAPS cells snap to the grid; drag with the Move tool (G) or type a position.</div>
                  </div>
                }
              } @else if (selection().length === 1) {
                @if (single(); as inst) {
                  <div class="me-card">
                    <div class="me-card-title">{{ inst.kind }}</div>
                    <dl class="me-kv">
                      <dt>Model</dt><dd class="lm-mono">{{ inst.model }}</dd>
                      <dt>Instance</dt><dd class="lm-mono">{{ inst.instance }}</dd>
                      @if (inst.geoIndex !== null) { <dt>GEO</dt><dd class="lm-mono">#{{ inst.geoIndex }} {{ inst.path }}</dd> }
                      @if (inst.clutterIndex !== null) { <dt>Clutter</dt><dd class="lm-mono">#{{ inst.clutterIndex }}</dd> }
                      <dt>Source</dt><dd class="lm-mono">{{ inst.source ?? 'unresolved' }}</dd>
                    </dl>
                  </div>
                  <div class="me-card">
                    <div class="me-card-title">Transform (game units)</div>
                    <div class="me-xform">
                      <span>Position</span>
                      @for (k of [0, 1, 2]; track k) {
                        <input class="me-num" type="number" step="1" [value]="fmt(inst.matrix[12 + k])" [disabled]="!movable(inst)" (change)="setField('position', $any($event.target).value, k)" />
                      }
                      <span>Yaw °</span>
                      <input class="me-num" type="number" step="5" [value]="fmt(yawOf(inst.matrix))" [disabled]="!movable(inst)" (change)="setField('yaw', $any($event.target).value)" />
                      <span>Scale</span>
                      <input class="me-num" type="number" step="0.1" min="0.01" [value]="fmt(scaleOf(inst.matrix))" [disabled]="!movable(inst)" (change)="setField('scale', $any($event.target).value)" />
                    </div>
                  </div>
                }
              } @else if (selection().length > 1) {
                <div class="me-card">
                  <div class="me-card-title">{{ selection().length }} instances</div>
                  <div class="me-note">{{ selectionSummary() }}</div>
                </div>
              } @else {
                <div class="me-empty">Click a prop, clutter or point to select it (Shift adds, drag for a box). Double-click opens the model in the Model Editor.</div>
              }
              @if (selection().length) {
                <div class="me-card">
                  <div class="me-card-title">Edit</div>
                  <div class="me-actions">
                    <button class="lm-tool-btn" [disabled]="busy() || !movableCount()" (click)="duplicateSelection()">Duplicate (Shift+D)</button>
                    <button class="lm-tool-btn lm-danger-btn" [disabled]="busy() || !movableCount()" (click)="deleteSelection()">Delete (X)</button>
                    <button class="lm-tool-btn" [disabled]="busy() || !movableCount()" (click)="dropToGround()">Drop to ground</button>
                    <button class="lm-tool-btn" [disabled]="selection().length !== 1 || !single()?.source" (click)="openInModelEditor(selection()[0])">Open model…</button>
                  </div>
                </div>
              }
            }
            @case ('place') {
              <div class="me-card mp-place">
                <div class="me-card-title">Place clutter</div>
                <input class="me-search" type="text" placeholder="Filter models…" [value]="libQuery()" (input)="libQuery.set($any($event.target).value)" />
                <div class="mp-lib">
                  @for (m of library(); track m.archive + ':' + m.name) {
                    <button type="button" class="me-row" [class.active]="placeModel()?.name === m.name" (click)="placeModel.set(m); setTool('place')" [title]="m.archive">
                      <span class="me-row-name">{{ m.name }}</span><span class="me-row-meta">{{ m.kind }}</span>
                    </button>
                  }
                </div>
                <label class="me-field me-field-wide">Yaw ° <input class="me-num" type="number" step="15" [value]="placeYaw()" (change)="placeYaw.set(+$any($event.target).value || 0)" /></label>
                <label class="me-field me-field-wide">Scale <input class="me-num" type="number" step="0.1" min="0.01" [value]="placeScale()" (change)="placeScale.set(+$any($event.target).value || 1)" /></label>
                <label class="me-toggle"><input type="checkbox" [checked]="randomYaw()" (change)="randomYaw.set($any($event.target).checked)" /> Random yaw</label>
                <div class="lm-muted me-note">With the Place tool (P), click the ground to add a clutter placement of the chosen model (addClutter).</div>
              </div>
            }
            @case ('points') {
              <div class="me-card">
                <div class="me-card-title">AIMAPS points</div>
                <dl class="me-kv">
                  @for (k of pointKinds(); track k.kind) { <dt>{{ k.kind }}</dt><dd>{{ k.count }}</dd> }
                </dl>
                <div class="lm-muted me-note">Starts are green, waypoints blue. Select one in the outliner or the viewport and move it (movePoint).</div>
              </div>
            }
            @case ('map') {
              @if (status(); as s) {
                <div class="me-card">
                  <div class="me-card-title">Map</div>
                  <dl class="me-kv">
                    <dt>Package</dt><dd class="lm-mono">{{ s.path }}</dd>
                    <dt>Scene</dt><dd class="lm-mono">{{ sceneNs() }}_GEO.ZED</dd>
                    @for (c of countList(); track c.kind) { <dt>{{ c.kind }}</dt><dd>{{ c.count }}</dd> }
                    <dt>Changed</dt><dd class="lm-mono">{{ s.dirty.join(', ') || '—' }}</dd>
                  </dl>
                </div>
              }
              <div class="me-card">
                <div class="me-card-title">Agents</div>
                <div class="me-note">An agent can build this map with assets_map_scene / assets_map_points / assets_edit on session <span class="lm-mono">{{ status()?.session }}</span>; its edits appear here.</div>
              </div>
            }
          }
        </div>
      </div>
    </aside>
  </div>

  <div class="me-statusbar" [class.err]="messageIsError()">
    <span>{{ message() }}</span>
    <span class="me-spacer"></span>
    @if (hover(); as h) { <span class="lm-muted">{{ h }}</span> }
    <span>{{ selection().length }} selected</span>
  </div>
</div>
`,
})
export class MapEditorPageComponent implements AfterViewInit, OnDestroy {
  @ViewChild('canvas', { static: true }) private canvasRef!: ElementRef<HTMLCanvasElement>;

  private readonly api = getVsCodeApi();
  private readonly client = new AssetClient();
  private readonly textures = new TextureCache(this.client);
  viewport!: MapViewport;
  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private nextRequest = 1;
  private readonly waiters = new Map<number, (msg: Record<string, unknown>) => void>();
  private readonly meshCache = new Map<string, MapModel>();
  private pixelCache: Record<string, ModelTexturePixels> = {};

  readonly layers = LAYERS;
  readonly shadings: Array<{ id: Shading; label: string }> = [
    { id: 'textured', label: 'Textured' }, { id: 'solid', label: 'Solid' }, { id: 'wireframe', label: 'Wire' }, { id: 'xray', label: 'X-ray' },
  ];
  // W A S D Q E fly, so tools use other keys.
  readonly tools: Array<{ id: MapTool; label: string; key: string; glyph: string }> = [
    { id: 'select', label: 'Select (click / box drag)', key: 'V', glyph: '⬚' },
    { id: 'move', label: 'Move', key: 'G', glyph: '✥' },
    { id: 'rotate', label: 'Rotate (yaw)', key: 'R', glyph: '⟳' },
    { id: 'scale', label: 'Scale', key: 'T', glyph: '⤢' },
    { id: 'place', label: 'Place clutter', key: 'P', glyph: '✚' },
  ];
  readonly propTabs: Array<{ id: PropTab; label: string }> = [
    { id: 'item', label: 'Item' }, { id: 'place', label: 'Place' }, { id: 'points', label: 'Points' }, { id: 'map', label: 'Map' },
  ];

  readonly status = signal<SessionStatus | null>(null);
  readonly sceneNs = signal('');
  readonly counts = signal<Record<string, number>>({});
  readonly instances = signal<MapInstanceWire[]>([]);
  readonly points = signal<MapPointWire[]>([]);
  readonly models = signal<ModelSummary[]>([]);
  readonly openMenu = signal<string | null>(null);
  readonly query = signal('');
  readonly libQuery = signal('');
  readonly propTab = signal<PropTab>('item');
  readonly tool = signal<MapTool>('select');
  readonly shading = signal<Shading>('textured');
  readonly baked = signal(false);
  readonly grid = signal(true);
  readonly snap = signal(false);
  readonly snapMove = signal(10);
  readonly snapRotate = signal(15);
  readonly ortho = signal(false);
  readonly flySpeed = signal(100);
  readonly busy = signal(false);
  readonly progress = signal('');
  readonly message = signal('');
  readonly messageIsError = signal(false);
  readonly hover = signal<string | null>(null);
  readonly selectionVersion = signal(0);
  readonly openNodes = signal<Set<string>>(new Set(['L:reference', 'L:clutter']));
  readonly visibilityVersion = signal(0);
  readonly placeModel = signal<ModelSummary | null>(null);
  readonly placeYaw = signal(0);
  readonly placeScale = signal(1);
  readonly randomYaw = signal(false);

  readonly dirtyCount = computed(() => this.status()?.dirty.length ?? 0);
  readonly selection = computed(() => { this.selectionVersion(); return this.viewport ? [...this.viewport.selected].sort((a, b) => a - b) : []; });
  readonly selectedPoint = computed(() => { this.selectionVersion(); return this.viewport?.selectedPoint ?? null; });
  readonly single = computed(() => { const s = this.selection(); return s.length === 1 ? this.instances()[s[0]] ?? null : null; });
  readonly pointInfo = computed(() => this.points().find(p => p.name === this.selectedPoint()) ?? null);
  readonly movableCount = computed(() => this.selection().filter(i => this.movable(this.instances()[i])).length);
  readonly selectionSummary = computed(() => {
    const by = new Map<string, number>();
    for (const i of this.selection()) { const m = this.instances()[i]?.model ?? '?'; by.set(m, (by.get(m) ?? 0) + 1); }
    return [...by].map(([m, n]) => `${n}× ${m}`).join(', ');
  });
  readonly groups = computed(() => {
    const q = this.query().toLowerCase();
    const out = new Map<MapLayer, Map<string, ModelGroup>>();
    this.instances().forEach((inst, i) => {
      if (q && !inst.model.toLowerCase().includes(q) && !inst.instance.toLowerCase().includes(q)) { return; }
      const layer = layerOf(inst);
      const key = inst.source ?? `?:${inst.model}`;
      const byModel = out.get(layer) ?? new Map<string, ModelGroup>();
      const g = byModel.get(key) ?? { source: key, model: inst.model, indices: [] };
      g.indices.push(i);
      byModel.set(key, g);
      out.set(layer, byModel);
    });
    return out;
  });
  readonly filteredPoints = computed(() => {
    const q = this.query().toLowerCase();
    return this.points().filter(p => !q || p.name.toLowerCase().includes(q));
  });
  readonly pointKinds = computed(() => {
    const by = new Map<string, number>();
    for (const p of this.points()) { by.set(p.kind, (by.get(p.kind) ?? 0) + 1); }
    return [...by].map(([kind, count]) => ({ kind, count }));
  });
  readonly countList = computed(() => Object.entries(this.counts()).map(([kind, count]) => ({ kind, count })));
  readonly library = computed(() => {
    const q = this.libQuery().toLowerCase();
    return this.models().filter(m => (m.kind === 'prop' || m.kind === 'library') && (!q || m.name.toLowerCase().includes(q))).slice(0, 400);
  });

  readonly menus: Menu[] = [
    {
      id: 'file', label: 'File', items: [
        { label: 'Open package…', shortcut: 'Ctrl+O', action: () => this.pickPackage() },
        { label: 'Reload', action: () => void this.reload(true, true), disabled: () => !this.status() },
        { separator: true, label: '' },
        { label: 'Save copy…', shortcut: 'Ctrl+S', action: () => void this.saveZdbCopy(), disabled: () => !this.dirtyCount() },
        { label: 'Export memory-card pack…', action: () => void this.exportPack(), disabled: () => !this.dirtyCount() },
      ],
    },
    {
      id: 'edit', label: 'Edit', items: [
        { label: 'Undo', shortcut: 'Ctrl+Z', action: () => void this.undo(), disabled: () => !this.status()?.canUndo },
        { label: 'Redo', shortcut: 'Ctrl+Y', action: () => void this.redo(), disabled: () => !this.status()?.canRedo },
        { separator: true, label: '' },
        { label: 'Duplicate', shortcut: 'Shift+D', action: () => void this.duplicateSelection(), disabled: () => !this.movableCount() },
        { label: 'Delete', shortcut: 'X / Delete', action: () => void this.deleteSelection(), disabled: () => !this.movableCount() },
        { label: 'Drop to ground', shortcut: 'End', action: () => void this.dropToGround(), disabled: () => !this.movableCount() },
      ],
    },
    {
      id: 'select', label: 'Select', items: [
        { label: 'None', shortcut: 'Esc', action: () => this.viewport.clearSelection() },
        { label: 'All of this model', shortcut: 'Shift+A', action: () => this.selectSameModel(), disabled: () => this.selection().length === 0 },
      ],
    },
    {
      id: 'view', label: 'View', items: [
        { label: 'Frame selected', shortcut: 'F', action: () => this.frameSelected() },
        { label: 'Frame map', shortcut: 'Home', action: () => this.viewport.frameAll() },
        { separator: true, label: '' },
        { label: 'Top', shortcut: 'Numpad 7', action: () => this.view('top') },
        { label: 'Front', shortcut: 'Numpad 1', action: () => this.view('front') },
        { label: 'Right', shortcut: 'Numpad 3', action: () => this.view('right') },
        { label: 'Orthographic', shortcut: 'Numpad 5', action: () => this.toggleOrtho(), checked: () => this.ortho() },
        { separator: true, label: '' },
        ...LAYERS.map(l => ({ label: l.label, action: () => this.toggleLayer(l.id), checked: () => !this.layerHidden(l.id) })),
        { separator: true, label: '' },
        { label: 'Back faces', action: () => this.toggleBackfaces(), checked: () => this.backfaces },
      ],
    },
    {
      id: 'help', label: 'Help', items: [
        { label: 'Fly: W A S D, Q / E down / up, Shift faster (viewport focused)' },
        { label: 'Orbit: middle drag or Alt+left drag · Pan: right drag · Zoom: wheel' },
        { label: 'Select: click, Shift adds, Ctrl removes, drag for box' },
        { label: 'Tools: V select · G move · R rotate · T scale · P place' },
        { label: 'Double-click a prop to open its model in the Model Editor' },
      ],
    },
  ];
  backfaces = true;

  constructor() {
    window.addEventListener('message', this.onMessage);
    window.addEventListener('keydown', this.onKey);
  }

  ngAfterViewInit(): void {
    this.viewport = new MapViewport(this.canvasRef.nativeElement, {
      onSelectionChange: () => { this.selectionVersion.update(v => v + 1); if (this.selection().length || this.selectedPoint()) { this.propTab.set('item'); } },
      onHover: text => this.hover.set(text),
      onTransformCommit: (items, kind) => void this.commitTransforms(items, kind),
      onPointMove: (name, position) => void this.edit(`move point ${name}`, { op: 'movePoint', name, position }),
      onPlace: position => void this.place(position),
      onOpenModel: index => this.openInModelEditor(index),
    });
    this.viewport.setLighting(this.baked());
    this.applySnap();
    this.api.postMessage({ type: 'ready' });
  }

  ngOnDestroy(): void {
    window.removeEventListener('message', this.onMessage);
    window.removeEventListener('keydown', this.onKey);
    if (this.pollTimer) { clearInterval(this.pollTimer); }
    this.viewport?.dispose();
  }

  // ------------------------------------------------------------------ messaging

  private readonly onMessage = (event: MessageEvent): void => {
    const msg = event.data as Record<string, unknown>;
    if (msg?.['type'] === 'openPackage' && typeof msg['path'] === 'string') {
      void this.openPackage(msg['path']);
    } else if (typeof msg?.['id'] === 'number' && ['pathPicked'].includes(msg['type'] as string)) {
      this.waiters.get(msg['id'])?.(msg);
      this.waiters.delete(msg['id']);
    }
  };

  private ask(type: string, extra: Record<string, unknown> = {}): Promise<Record<string, unknown>> {
    const id = this.nextRequest++;
    return new Promise(resolve => {
      this.waiters.set(id, resolve);
      this.api.postMessage({ type, id, ...extra });
    });
  }

  private say(text: string, error = false): void {
    this.message.set(text);
    this.messageIsError.set(error);
    this.api.postMessage({ type: 'log', text });
  }

  // ------------------------------------------------------------------ menus & keys

  toggleMenu(id: string): void { this.openMenu.set(this.openMenu() === id ? null : id); }
  hoverMenu(id: string): void { if (this.openMenu()) { this.openMenu.set(id); } }
  runMenu(item: MenuItem): void { this.openMenu.set(null); item.action?.(); }
  closeMenusOutside(e: Event): void {
    if (!(e.target as HTMLElement).closest('.me-menu')) { this.openMenu.set(null); }
  }

  // The viewport swallows W A S D Q E while it has focus (fly), so they never reach here then.
  private readonly onKey = (e: KeyboardEvent): void => {
    const target = e.target as HTMLElement;
    if (target && ['INPUT', 'SELECT', 'TEXTAREA'].includes(target.tagName)) { return; }
    const ctrl = e.ctrlKey || e.metaKey;
    const k = e.key.toLowerCase();
    let handled = true;
    if (ctrl && k === 'z') { void this.undo(); }
    else if (ctrl && (k === 'y' || (e.shiftKey && k === 'z'))) { void this.redo(); }
    else if (ctrl && k === 's') { void this.saveZdbCopy(); }
    else if (ctrl && k === 'o') { this.pickPackage(); }
    else if (e.code === 'Numpad1') { this.view('front'); }
    else if (e.code === 'Numpad3') { this.view('right'); }
    else if (e.code === 'Numpad7') { this.view('top'); }
    else if (e.code === 'Numpad5') { this.toggleOrtho(); }
    else if (ctrl) { handled = false; }
    else if (k === 'd' && e.shiftKey) { void this.duplicateSelection(); }
    else if (k === 'a' && e.shiftKey) { this.selectSameModel(); }
    else if (k === 'x' || e.key === 'Delete') { void this.deleteSelection(); }
    else if (e.key === 'End') { void this.dropToGround(); }
    else if (k === 'f') { this.frameSelected(); }
    else if (e.key === 'Home') { this.viewport.frameAll(); }
    else if (k === 'v') { this.setTool('select'); }
    else if (k === 'g') { this.setTool('move'); }
    else if (k === 'r') { this.setTool('rotate'); }
    else if (k === 't') { this.setTool('scale'); }
    else if (k === 'p') { this.setTool('place'); }
    else if (k === 'z') { this.cycleShading(); }
    else if (e.key === 'Escape') { this.openMenu.set(null); if (this.tool() !== 'select') { this.setTool('select'); } else { this.viewport.clearSelection(); } }
    else { handled = false; }
    if (handled) { e.preventDefault(); }
  };

  // ------------------------------------------------------------------ view

  setTool(tool: MapTool): void {
    this.tool.set(tool);
    this.viewport.tool = tool;
    if (tool === 'place') { this.propTab.set('place'); }
  }
  setShading(s: Shading): void { this.shading.set(s); this.viewport.shading = s; }
  cycleShading(): void {
    const order: Shading[] = ['textured', 'solid', 'wireframe', 'xray'];
    this.setShading(order[(order.indexOf(this.shading()) + 1) % order.length]);
  }
  setBaked(on: boolean): void { this.baked.set(on); this.viewport.setLighting(on); }
  setGrid(on: boolean): void { this.grid.set(on); this.viewport.showGrid = on; this.viewport.showAxes = on; this.viewport.refreshHelpers(); }
  applySnap(): void { this.viewport?.setSnap(this.snap() ? this.snapMove() : null, this.snap() ? this.snapRotate() : null, this.snap() ? 0.1 : null); }
  setFlySpeed(v: number): void { if (v > 0) { this.flySpeed.set(v); this.viewport.flySpeed = v; } }
  toggleBackfaces(): void { this.backfaces = !this.backfaces; this.viewport.setBackfaces(this.backfaces); }
  view(preset: ViewPreset): void { this.viewport.setView(preset); }
  toggleOrtho(): void { this.viewport.toggleOrtho(); this.ortho.set(this.viewport.isOrtho); }
  frameSelected(): void { this.viewport.frameSelected(); }
  viewLabel(): string { this.selectionVersion(); return this.ortho() ? 'Orthographic' : 'Perspective'; }
  toolLabel(): string { return this.tools.find(t => t.id === this.tool())?.label.split(' (')[0] ?? ''; }

  // ------------------------------------------------------------------ outliner

  isOpen(key: string): boolean { return this.openNodes().has(key); }
  toggleOpen(key: string): void {
    const next = new Set(this.openNodes());
    if (next.has(key)) { next.delete(key); } else { next.add(key); }
    this.openNodes.set(next);
  }
  groupsOf(layer: MapLayer): ModelGroup[] {
    return [...(this.groups().get(layer)?.values() ?? [])].sort((a, b) => a.model.localeCompare(b.model));
  }
  layerCount(layer: MapLayer): number { return this.instances().filter(i => layerOf(i) === layer).length; }
  layerHidden(layer: MapLayer): boolean { this.visibilityVersion(); return this.viewport?.isLayerHidden(layer) ?? false; }
  toggleLayer(layer: MapLayer): void { this.viewport.setLayerHidden(layer, !this.viewport.isLayerHidden(layer)); this.visibilityVersion.update(v => v + 1); }
  modelHidden(source: string): boolean { this.visibilityVersion(); return this.viewport?.isModelHidden(source) ?? false; }
  toggleModel(source: string): void { this.viewport.setModelHidden(source, !this.viewport.isModelHidden(source)); this.visibilityVersion.update(v => v + 1); }
  isSelected(i: number): boolean { this.selectionVersion(); return this.viewport?.selected.has(i) ?? false; }
  groupSelected(g: ModelGroup): boolean { this.selectionVersion(); return g.indices.length > 0 && g.indices.every(i => this.viewport.selected.has(i)); }
  pickGroup(g: ModelGroup, e: MouseEvent): void {
    this.viewport.select(g.indices, e.shiftKey);
    if (!e.shiftKey && g.indices.length === 1) { this.viewport.frameSelected(); }
  }
  pickInstance(i: number, e: MouseEvent): void { this.viewport.select([i], e.shiftKey); this.viewport.frameSelected(); }
  pickPoint(name: string): void { this.viewport.selectPoint(name); this.viewport.frameSelected(); }
  instanceLabel(i: number): string {
    const inst = this.instances()[i];
    const p = inst.matrix;
    const id = inst.clutterIndex !== null ? `#${inst.clutterIndex}` : inst.instance;
    return `${id} · ${p[12].toFixed(0)}, ${p[13].toFixed(0)}, ${p[14].toFixed(0)}`;
  }
  selectSameModel(): void {
    const sources = new Set(this.selection().map(i => this.instances()[i]?.source).filter((s): s is string => !!s));
    const kinds = new Set(this.selection().map(i => this.instances()[i]?.kind));
    this.viewport.select(this.instances().map((inst, i) => inst.source && sources.has(inst.source) && kinds.has(inst.kind) ? i : -1).filter(i => i >= 0));
  }

  // ------------------------------------------------------------------ loading

  pickPackage(): void { this.api.postMessage({ type: 'pickPackage' }); }

  private async openPackage(path: string): Promise<void> {
    this.busy.set(true);
    this.say(`Opening ${path}…`);
    try {
      const status = await this.client.open(path);
      this.status.set(status);
      this.textures.invalidate(status.revision);
      this.meshCache.clear();
      this.pixelCache = {};
      this.models.set(await this.client.listModels());
      this.viewport.clearSelection();
      await this.reload(false, false);
      if (!this.pollTimer) { this.pollTimer = setInterval(() => void this.poll(), 2000); }
    } catch (err) {
      this.say(`Could not open ${path}: ${errText(err)}`, true);
    } finally {
      this.busy.set(false);
    }
  }

  // Edits made by anyone on the session (an agent with assets_edit, the Model Editor) show up here.
  private async poll(): Promise<void> {
    if (!this.client.session || this.busy() || this.progress()) { return; }
    try {
      const status = await this.client.status();
      const before = this.status()?.revision;
      this.status.set(status);
      if (before !== undefined && status.revision !== before) {
        await this.reload(true, true);
        this.say(`Session changed (revision ${status.revision}${status.canUndo ? `: ${status.canUndo}` : ''}).`);
      }
    } catch { /* retried next poll */ }
  }

  // Scene + points from the bridge, then meshes and textures per unique model (cached: placement edits keep them).
  private async reload(keepCamera: boolean, modelsChanged: boolean): Promise<void> {
    if (modelsChanged) {
      this.meshCache.clear();
      this.pixelCache = {};
      const s = this.status();
      if (s) { this.textures.invalidate(s.revision); }
    }
    const [scene, pts] = await Promise.all([this.client.mapScene(), this.client.mapPoints().catch(() => ({ points: [] as MapPointWire[] }))]);
    this.sceneNs.set(scene.sceneNamespace);
    this.counts.set(scene.counts);
    const sources = [...new Set(scene.instances.map(i => i.source).filter((s): s is string => !!s))].filter(s => !this.meshCache.has(s));
    let done = 0;
    const loadOne = async (source: string): Promise<void> => {
      const cut = source.indexOf(':');
      try {
        const wire = await this.client.mesh(source.slice(0, cut), source.slice(cut + 1)) as MeshWire;
        const mesh = decodeMesh(wire);
        const ns = wire.textureNamespace ?? null;
        const names = [...new Set(mesh.chunks.map(c => c.texture).filter((t): t is string => !!t))];
        await Promise.all(names.map(async n => {
          const key = `${ns ?? ''}:${n}`.toLowerCase();
          if (key in this.pixelCache) { return; }
          const p = await this.textures.get(n, ns);
          if (p) { this.pixelCache[key] = p; }
        }));
        this.meshCache.set(source, { mesh, textureNamespace: ns });
      } catch (err) {
        this.say(`${source}: ${errText(err)}`, true);
      }
      done++;
      this.progress.set(`Loading models ${done}/${sources.length}…`);
    };
    // A few at a time: the bridge serialises anyway, and the progress stays readable.
    const queue = [...sources];
    await Promise.all(Array.from({ length: Math.min(6, queue.length) }, async () => {
      while (queue.length) { await loadOne(queue.shift()!); }
    }));
    this.progress.set('');
    const instances = scene.instances;
    this.instances.set(instances);
    this.points.set(pts.points);
    this.viewport.setScene(instances, this.meshCache, this.pixelCache, pts.points, keepCamera);
    if (!keepCamera) { this.flySpeed.set(Math.round(this.viewport.flySpeed)); }
    this.selectionVersion.update(v => v + 1);
    if (!keepCamera) { this.say(`${scene.sceneNamespace}: ${instances.length} instances (${this.countList().map(c => `${c.count} ${c.kind}`).join(', ')}), ${pts.points.length} points.`); }
  }

  // ------------------------------------------------------------------ edits (all through the bridge)

  private async edit(label: string, op: Record<string, unknown>): Promise<unknown | null> {
    this.busy.set(true);
    try {
      const raw = await this.client.edit({ label, ...op }) as unknown as { result?: { revision: number; description?: string }; revision?: number; description?: string };
      const result = raw.result ?? (raw as { revision: number; description?: string });
      this.status.set(await this.client.status());
      this.say(`${result.description ?? label} (revision ${result.revision})`);
      await this.reload(true, false);
      return raw;
    } catch (err) {
      this.say(`${label}: ${errText(err)}`, true);
      await this.reload(true, false).catch(() => undefined);
      return null;
    } finally {
      this.busy.set(false);
    }
  }

  private placementOp(inst: MapInstanceWire, fields: Record<string, unknown>): Record<string, unknown> | null {
    if (inst.clutterIndex !== null) { return { op: 'moveClutter', clutterIndex: inst.clutterIndex, ...fields }; }
    if (inst.geoIndex !== null) { return { op: 'moveInstance', geoIndex: inst.geoIndex, ...fields }; }
    return null;
  }

  private async commitTransforms(items: Array<{ index: number; matrix: number[] }>, kind: string): Promise<void> {
    const ops = items.map(it => this.placementOp(this.instances()[it.index], { matrix: it.matrix.map(v => Math.fround(v)) })).filter((o): o is Record<string, unknown> => !!o);
    if (!ops.length) { return; }
    const label = `${kind} ${ops.length === 1 ? this.instances()[items[0].index].model : ops.length + ' instances'}`;
    await this.edit(label, ops.length === 1 ? ops[0] : { op: 'batch', ops });
  }

  async setField(field: 'position' | 'yaw' | 'scale', value: string, axis = 0): Promise<void> {
    const inst = this.single();
    const v = parseFloat(value);
    if (!inst || !Number.isFinite(v)) { return; }
    const m = inst.matrix;
    const position = [m[12], m[13], m[14]];
    if (field === 'position') { position[axis] = v; }
    const yaw = field === 'yaw' ? v : this.yawOf(m);
    const scale = field === 'scale' ? Math.max(0.01, v) : this.scaleOf(m);
    const op = field === 'position' ? this.placementOp(inst, { position }) : this.placementOp(inst, { position, yaw, scale });
    if (op) { await this.edit(`set ${field} of ${inst.model}`, op); }
  }

  async movePointField(name: string, axis: 'x' | 'z', value: string): Promise<void> {
    const p = this.points().find(x => x.name === name);
    const v = parseFloat(value);
    if (!p || !Number.isFinite(v)) { return; }
    await this.edit(`move point ${name}`, { op: 'movePoint', name, position: [axis === 'x' ? v : p.x, 0, axis === 'z' ? v : p.z] });
  }

  async deleteSelection(): Promise<void> {
    const sel = this.selection().map(i => this.instances()[i]).filter(i => this.movable(i));
    if (!sel.length) { return; }
    // Highest index first so earlier deletes do not shift later ones.
    const geo = sel.filter(i => i.geoIndex !== null && i.clutterIndex === null).sort((a, b) => b.geoIndex! - a.geoIndex!).map(i => ({ op: 'deleteInstance', geoIndex: i.geoIndex }));
    const clutter = sel.filter(i => i.clutterIndex !== null).sort((a, b) => b.clutterIndex! - a.clutterIndex!).map(i => ({ op: 'deleteClutter', clutterIndex: i.clutterIndex }));
    const ops = [...geo, ...clutter];
    this.viewport.clearSelection();
    await this.edit(`delete ${ops.length === 1 ? sel[0].model : ops.length + ' instances'}`, ops.length === 1 ? ops[0] : { op: 'batch', ops });
  }

  async duplicateSelection(): Promise<void> {
    const sel = this.selection().map(i => this.instances()[i]).filter(i => this.movable(i));
    if (!sel.length) { return; }
    const beforeGeo = this.instances().filter(i => i.clutterIndex === null).map(i => `${i.model}|${i.path}`);
    // GEO copies insert nodes: go from the highest index down so the earlier indices stay valid.
    const geo = sel.filter(i => i.clutterIndex === null).sort((a, b) => b.geoIndex! - a.geoIndex!).map(i => ({ op: 'duplicateInstance', geoIndex: i.geoIndex }));
    const clutter = sel.filter(i => i.clutterIndex !== null).map(i => ({ op: 'addClutter', model: i.model, matrix: i.matrix }));
    const ops = [...geo, ...clutter];
    const raw = await this.edit(`duplicate ${ops.length === 1 ? sel[0].model : ops.length + ' instances'}`, ops.length === 1 ? ops[0] : { op: 'batch', ops }) as
      { newClutterIndex?: number; results?: Array<{ newClutterIndex?: number }> } | null;
    if (!raw) { return; }
    // Copies come back selected with the Move tool, ready to place.
    const newClutter = new Set([raw.newClutterIndex, ...(raw.results ?? []).map(r => r?.newClutterIndex)].filter((v): v is number => typeof v === 'number' && v >= 0));
    // A GEO copy has its original's path and lands right after it: walk old and new in order, the extras are copies.
    const picked: number[] = [];
    let k = 0;
    this.instances().forEach((inst, i) => {
      if (inst.clutterIndex !== null) { if (newClutter.has(inst.clutterIndex)) { picked.push(i); } return; }
      if (k < beforeGeo.length && beforeGeo[k] === `${inst.model}|${inst.path}`) { k++; } else { picked.push(i); }
    });
    this.viewport.select(picked);
    this.setTool('move');
  }

  // Moves the selection down onto the surface under it (world model and other props).
  async dropToGround(): Promise<void> {
    const ops: Record<string, unknown>[] = [];
    for (const i of this.selection()) {
      const inst = this.instances()[i];
      if (!this.movable(inst)) { continue; }
      const y = this.viewport.groundBelow(i);
      if (y === null) { continue; }
      const op = this.placementOp(inst, { position: [inst.matrix[12], y, inst.matrix[14]] });
      if (op) { ops.push(op); }
    }
    if (ops.length) { await this.edit('drop to ground', ops.length === 1 ? ops[0] : { op: 'batch', ops }); }
    else { this.say('Nothing under the selection to drop onto.'); }
  }

  private async place(position: [number, number, number]): Promise<void> {
    const m = this.placeModel();
    if (!m) { this.propTab.set('place'); this.say('Pick a model in the Place tab first.', true); return; }
    const yaw = this.randomYaw() ? Math.round(Math.random() * 360) : this.placeYaw();
    const raw = await this.edit(`place ${m.name}`, { op: 'addClutter', model: m.name, position, yaw, scale: this.placeScale() }) as { newClutterIndex?: number } | null;
    if (raw && typeof raw.newClutterIndex === 'number') {
      const i = this.instances().findIndex(x => x.clutterIndex === raw.newClutterIndex);
      if (i >= 0) { this.viewport.select([i]); }
    }
  }

  async undo(): Promise<void> { await this.history(true); }
  async redo(): Promise<void> { await this.history(false); }
  private async history(undo: boolean): Promise<void> {
    if (!this.client.session) { return; }
    this.busy.set(true);
    try {
      const r = undo ? await this.client.undo() : await this.client.redo();
      this.status.set(await this.client.status());
      this.say(`${undo ? 'Undid' : 'Redid'} ${r.description ?? ''} (revision ${r.revision})`);
      // Undo can touch models as well as placements (Model Editor edits share the stack).
      await this.reload(true, true);
    } catch (err) {
      this.say(errText(err), true);
    } finally {
      this.busy.set(false);
    }
  }

  openInModelEditor(index: number): void {
    const inst = this.instances()[index];
    if (!inst?.source || !this.status()) { return; }
    const cut = inst.source.indexOf(':');
    this.api.postMessage({ type: 'openModelEditor', path: this.status()!.path, model: { archive: inst.source.slice(0, cut), name: inst.source.slice(cut + 1) } });
    this.say(`Opening ${inst.model} in the Model Editor…`);
  }

  // ------------------------------------------------------------------ save

  async saveZdbCopy(): Promise<void> {
    const s = this.status();
    if (!s || !this.dirtyCount()) { return; }
    // A .CDB pack keeps its name (the game mounts mc0:/BASCUS-97275<NAME>/<NAME>.CDB); a .ZDB gets an _edit copy.
    const picked = await this.ask('pickSavePath', { kind: 'zdb', suggested: /\.cdb$/i.test(s.path) ? s.path : s.path.replace(/(\.zdb)$/i, '_edit$1') });
    if (typeof picked['path'] !== 'string') { return; }
    await this.save({ mode: 'zdb', outputPath: picked['path'] });
  }

  async exportPack(): Promise<void> {
    const picked = await this.ask('pickSavePath', { kind: 'pack', suggested: 'MMAP' });
    const name = picked['packName'];
    const dir = picked['path'];
    if (typeof dir !== 'string' || typeof name !== 'string') { return; }
    await this.save({ mode: 'pack', packName: name, outputPath: dir });
  }

  private async save(args: { mode: 'zdb' | 'pack'; outputPath?: string; packName?: string }): Promise<void> {
    this.busy.set(true);
    try {
      const r = await this.client.save(args);
      this.say(`Wrote ${r.written.join(', ')} (${r.leaves.join(', ')})`);
    } catch (err) {
      this.say(`Save failed: ${errText(err)}`, true);
    } finally {
      this.busy.set(false);
    }
  }

  // ------------------------------------------------------------------ helpers

  movable(inst: MapInstanceWire | undefined): boolean {
    return !!inst && (inst.clutterIndex !== null || (inst.geoIndex !== null && layerOf(inst) === 'reference'));
  }
  yawOf(m: number[]): number { return Math.atan2(-m[2], m[0]) * 180 / Math.PI; }
  scaleOf(m: number[]): number { return Math.hypot(m[0], m[1], m[2]); }
  fileName(p: string): string { return p.split(/[\\/]/).pop() ?? p; }
  fmt(v: number): string { return (Math.abs(v) < 1e-4 ? 0 : v).toFixed(2); }
}

function errText(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}
