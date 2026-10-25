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
import { AssetClient, type AnimationSummary, type FxNode, type ModelSummary, type SessionStatus, type WeaponSummary } from '../model-editor/asset-client';
import { TextureCache } from '../model-editor/model-loader';
import { decodeMesh, type EditorMesh, type MeshWire } from '../model-editor/mesh-data';
import { AnimViewport, type AnimTool, type ClipData, type ShotEffect } from './anim-viewport';
import '../shared/styles/shared.css';
import '../model-editor/model-editor-page.css';
import './animation-editor-page.css';

interface MenuItem {
  label: string;
  shortcut?: string;
  action?: () => void;
  disabled?: () => boolean;
  checked?: () => boolean;
  separator?: boolean;
}

interface Menu { id: string; label: string; items: MenuItem[] }

const PACKAGE_SOURCE = 'MOTION_S.ZAR';

// Animation Editor: every character clip of a package (its MOTION_S.ZAR, editable) and the shared MOTION_P.ZAR
// (read-only, copy in to change), played on a character from the package. Creating, renaming, deleting, retiming
// and keying clips are bridge asset-session edits (addAnimation / renameAnimation / deleteAnimation / setAnimation /
// setAnimationKey), the same ops as assets_edit, so an agent and this page can animate together.
@Component({
  selector: 'app-animation-editor-page',
  standalone: true,
  imports: [IconComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="me-app" (pointerdown)="closeMenusOutside($event)">
  <div class="me-menubar">
    <span class="me-brand"><app-icon name="repeat" [size]="14" /> Animation Editor</span>
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
    <label class="me-toggle">Character
      <select class="me-input" [value]="characterKey()" (change)="pickCharacter($any($event.target).value)">
        @for (c of characters(); track c.archive + ':' + c.name) { <option [value]="c.archive + ':' + c.name" [selected]="c.archive + ':' + c.name === characterKey()">{{ c.name }}</option> }
      </select>
    </label>
    <div class="me-seg" title="Shading">
      @for (s of shadings; track s.id) {
        <button [class.on]="shading() === s.id" (click)="setShading(s.id)">{{ s.label }}</button>
      }
    </div>
    <label class="me-toggle">Weapon
      <select class="me-input" (change)="pickWeapon($any($event.target).value)">
        <option value="" [selected]="!weaponKey()">None</option>
        @for (w of weapons(); track w.model) { <option [value]="w.model" [selected]="w.model === weaponKey()">{{ w.internalName || w.model }}</option> }
      </select>
    </label>
    <button class="lm-tool-btn" [disabled]="!weaponKey()" (click)="fire()" title="Fire one shot (Enter): the weapon's FireAnimName effect">Fire</button>
    <label class="me-toggle" title="Fire at the weapon's FireWait rate"><input type="checkbox" [checked]="autoFire()" [disabled]="!weaponKey()" (change)="setAutoFire($any($event.target).checked)" /> Auto</label>
    <label class="me-toggle"><input type="checkbox" [checked]="showSkeleton()" (change)="setSkeleton($any($event.target).checked)" /> Skeleton</label>
    <label class="me-toggle"><input type="checkbox" [checked]="showMesh()" (change)="setMesh($any($event.target).checked)" /> Mesh</label>
    <span class="me-spacer"></span>
    <div class="me-seg" title="View">
      <button (click)="view('front')">Front</button>
      <button (click)="view('back')">Back</button>
      <button (click)="view('right')">Side</button>
      <button (click)="view('iso')">Iso</button>
      <button (click)="viewport.frameCharacter()" title="Frame (F)">Frame</button>
    </div>
  </div>

  <div class="me-body">
    <aside class="me-assets">
      <div class="me-panel-title">Animations <span class="lm-muted">{{ clips().length }}</span></div>
      <input class="me-search" type="text" placeholder="Filter clips…" [value]="query()" (input)="query.set($any($event.target).value)" />
      @if (sources().length > 1) {
        <!-- Other games' clips come in many source files (GTA: 160+ IFPs): pick one from a list. -->
        <select class="me-search" title="Source file" (change)="source.set($any($event.target).value)">
          <option value="" [selected]="!source()">All sources ({{ sources().length }})</option>
          @for (src of sources(); track src.name) { <option [value]="src.name" [selected]="src.name === source()">{{ src.name }} ({{ src.count }})</option> }
        </select>
      } @else {
        <div class="me-kinds">
          @for (p of prefixes(); track p) {
            <button class="me-chip" [class.active]="prefix() === p" (click)="prefix.set(prefix() === p ? '' : p)">{{ p }}</button>
          }
        </div>
      }
      <div class="me-list">
        @if (!status()) { <div class="me-empty">File › Open package… to load a .ZDB / .CDB, or a GTA: San Andreas .ISO / .IMG.</div> }
        @for (g of groups(); track g.title) {
          <div class="me-group-title">{{ g.title }} <span class="lm-muted">{{ g.clips.length }}</span></div>
          @for (c of g.clips; track c.source + ':' + c.name) {
            <button type="button" class="me-row" [class.active]="isCurrent(c)" (click)="selectClip(c)" (dblclick)="selectClip(c, true)" [title]="c.source + (c.editable ? '' : ' (read-only: copy into the package to edit)')">
              <span class="me-row-name">{{ c.name }}</span>
              <span class="me-row-meta">{{ c.duration.toFixed(2) }}s</span>
            </button>
          }
        }
      </div>
    </aside>

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
          <div>{{ current()?.name ?? 'No clip' }}{{ current() && !current()!.editable ? ' · shared (read-only)' : '' }}</div>
          @if (clip(); as c) { <div class="lm-muted">frame {{ frame() }} / {{ c.samples - 1 }} · {{ time().toFixed(2) }}s / {{ c.duration.toFixed(2) }}s · {{ c.fps.toFixed(1) }} fps</div> }
          @if (selectedBone(); as b) { <div class="lm-muted">bone {{ b }}</div> }
        </div>
        @if (loading()) { <div class="me-vp-center">{{ loading() }}</div> }
        @else if (!status()) { <div class="me-vp-center">File › Open package… (Ctrl+O)</div> }
        @else if (!hasCharacter()) { <div class="me-vp-center">This package has no skinned character to preview on.</div> }
        @if (busy()) { <div class="me-vp-busy">Applying…</div> }
      </div>
      <div class="an-timeline">
        <button class="lm-tool-btn" (click)="stepFrame(-1)" title="Previous frame (,)">⏮</button>
        <button class="lm-tool-btn an-play" (click)="togglePlay()" [title]="playing() ? 'Pause (Space)' : 'Play (Space)'">{{ playing() ? '⏸' : '▶' }}</button>
        <button class="lm-tool-btn" (click)="stepFrame(1)" title="Next frame (.)">⏭</button>
        <input class="an-scrub" type="range" min="0" [max]="(clip()?.samples ?? 1) - 1" step="1" [value]="frame()" (input)="scrub($any($event.target).value)" />
        <span class="lm-muted an-frame">{{ frame() }}</span>
        <label class="me-toggle">Speed
          <select class="me-input" [value]="speed()" (change)="setSpeed(+$any($event.target).value)">
            @for (s of [0.1, 0.25, 0.5, 1, 2]; track s) { <option [value]="s" [selected]="s === speed()">{{ s }}×</option> }
          </select>
        </label>
        <label class="me-toggle"><input type="checkbox" [checked]="loop()" (change)="setLoop($any($event.target).checked)" /> Loop</label>
      </div>
    </section>

    <aside class="me-side">
      <div class="me-outliner">
        <div class="me-panel-title">Bones <span class="lm-muted">{{ bones().length }}</span></div>
        <div class="me-tree">
          @for (b of bones(); track b.name) {
            <div class="me-tree-row" [class.sel]="selectedBone() === b.name" (click)="pickBone(b.name)">
              <span [class.an-animated]="b.animated" [class.an-extra]="b.extra">{{ b.name }}</span>
              <span class="lm-muted">{{ b.extra ? 'mount' : b.animated ? 'animated' : b.tracked ? 'static' : 'bind' }}</span>
            </div>
          }
        </div>
      </div>
      <div class="me-props">
        <div class="me-tab-body">
          @if (current(); as c) {
            <div class="me-card">
              <div class="me-card-title">Clip</div>
              <dl class="me-kv">
                <dt>Name</dt><dd class="lm-mono">{{ c.name }}</dd>
                <dt>Source</dt><dd>{{ c.source }}{{ c.editable ? '' : ' (read-only)' }}</dd>
                <dt>Bones</dt><dd>{{ c.bones }}</dd>
              </dl>
              <div class="me-xform">
                <span>Duration s</span>
                <input class="me-num" type="number" min="0.03" step="0.1" [value]="c.duration.toFixed(3)" [disabled]="!c.editable" (change)="setDuration($any($event.target).value)" />
                <span>Frames</span>
                <input class="me-num" type="number" min="1" step="1" [value]="c.frames" [disabled]="!c.editable" (change)="setFrames($any($event.target).value)" title="Frame intervals (samples = frames + 1); changing it resamples" />
              </div>
              <div class="me-actions">
                @if (c.editable) {
                  <button class="lm-tool-btn" [disabled]="busy()" (click)="renameClip()">Rename…</button>
                  <button class="lm-tool-btn" [disabled]="busy()" (click)="duplicateClip()">Duplicate…</button>
                  <button class="lm-tool-btn lm-danger-btn" [disabled]="busy()" (click)="deleteClip()">Delete</button>
                } @else {
                  <button class="lm-tool-btn" [disabled]="busy() || !canWrite()" (click)="duplicateClip()">Copy into package…</button>
                }
              </div>
            </div>
          }
          @if (weaponInfo(); as w) {
            <div class="me-card">
              <div class="me-card-title">Weapon {{ w.name }}</div>
              <dl class="me-kv">
                <dt>Model</dt><dd class="lm-mono">{{ w.model }}</dd>
                <dt>Fire FX</dt><dd class="lm-mono">{{ w.fireAnim || '—' }}</dd>
                <dt>Spawns</dt><dd class="lm-mono">{{ w.spawns.join(', ') || '—' }}</dd>
                <dt>Flash</dt><dd class="lm-mono">{{ w.flash ?? '—' }}</dd>
                <dt>Shell</dt><dd class="lm-mono">{{ w.shell ?? '—' }}</dd>
                <dt>Rate</dt><dd>{{ w.fireWait }} s between shots</dd>
              </dl>
              <div class="lm-muted me-note">Mounted on the clip's rifle bone. Fire runs the effect scripts as the game does (flash roll, scale curve, timing, light, sub-effects).@if (skipped().length) { Not modelled yet: {{ skipped().join(', ') }}.}</div>
            </div>
            @for (fxn of fxTree(); track fxn.name) {
              <div class="me-card">
                <div class="me-card-title">Effect {{ fxn.name }}</div>
                @if (fxn.nodeRefs?.length) { <div class="lm-muted me-note">objects: {{ refNames(fxn) }}</div> }
                @for (t of fxn.tracks ?? []; track $index) {
                  <div class="an-track">{{ t.target }}</div>
                  @for (i of t.instructions; track $index) {
                    <div class="an-ins" [style.padding-left.px]="8 + i.depth * 12" [class.an-cond]="i.condition"><b>{{ i.name }}</b> <span class="lm-muted">{{ fieldText(i.fields) }}</span></div>
                  }
                }
              </div>
            }
          }
          @if (selectedBone(); as b) {
            <div class="me-card">
              <div class="me-card-title">Bone {{ b }} · frame {{ frame() }}</div>
              @if (boneLocal(); as l) {
                <div class="me-xform">
                  <span>Position</span>
                  @for (k of [0, 1, 2]; track k) { <input class="me-num" type="number" step="0.1" [value]="fmt(l.position[k])" [disabled]="!editable()" (change)="setBoneValue('position', k, $any($event.target).value)" /> }
                  <span>Rotate °</span>
                  @for (k of [0, 1, 2]; track k) { <input class="me-num" type="number" step="5" [value]="fmt(eulerOf(l.rotation)[k])" [disabled]="!editable()" (change)="setBoneValue('rotation', k, $any($event.target).value)" /> }
                </div>
              }
              <div class="me-actions">
                <button class="lm-tool-btn" [disabled]="!editable()" (click)="setTool('rotate')">Rotate (R)</button>
                <button class="lm-tool-btn" [disabled]="!editable()" (click)="setTool('move')">Move (G)</button>
                <button class="lm-tool-btn" [disabled]="!editable() || busy()" (click)="holdPose()" title="Use this frame's pose for every frame of the bone">Hold for all frames</button>
              </div>
              <div class="lm-muted me-note">Gizmo drags and field edits key this bone at the current frame (a static bone becomes animated). Local to the parent bone; X along the limb.</div>
            </div>
          } @else {
            <div class="me-empty">Click a joint (or a bone in the list) to key it. Shared clips are read-only: copy one into the package first.</div>
          }
          <div class="me-card">
            <div class="me-card-title">Agents</div>
            <div class="me-note">assets_list_animations / assets_animation / assets_edit (addAnimation, setAnimation, setAnimationKey…) work on session <span class="lm-mono">{{ status()?.session }}</span>; their edits appear here.</div>
          </div>
        </div>
      </div>
    </aside>
  </div>

  <div class="me-statusbar" [class.err]="messageIsError()">
    <span>{{ message() }}</span>
    <span class="me-spacer"></span>
    @if (hover(); as h) { <span class="lm-muted">{{ h }}</span> }
    <span>{{ characterKey() }}</span>
  </div>
</div>
`,
})
export class AnimationEditorPageComponent implements AfterViewInit, OnDestroy {
  @ViewChild('canvas', { static: true }) private canvasRef!: ElementRef<HTMLCanvasElement>;

  private readonly api = getVsCodeApi();
  private readonly client = new AssetClient();
  private readonly textures = new TextureCache(this.client);
  viewport!: AnimViewport;
  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private nextRequest = 1;
  private readonly waiters = new Map<number, (msg: Record<string, unknown>) => void>();

  readonly shadings: Array<{ id: Shading; label: string }> = [
    { id: 'textured', label: 'Textured' }, { id: 'solid', label: 'Solid' }, { id: 'wireframe', label: 'Wire' }, { id: 'xray', label: 'X-ray' },
  ];
  readonly tools: Array<{ id: AnimTool; label: string; key: string; glyph: string }> = [
    { id: 'select', label: 'Select bone', key: 'Q', glyph: '⬚' },
    { id: 'rotate', label: 'Rotate bone (key)', key: 'R', glyph: '⟳' },
    { id: 'move', label: 'Move bone (key)', key: 'G', glyph: '✥' },
  ];

  readonly status = signal<SessionStatus | null>(null);
  readonly clips = signal<AnimationSummary[]>([]);
  readonly models = signal<ModelSummary[]>([]);
  readonly current = signal<AnimationSummary | null>(null);
  readonly clip = signal<ClipData | null>(null);
  readonly characterKey = signal('');
  readonly hasCharacter = signal(false);
  readonly openMenu = signal<string | null>(null);
  readonly query = signal('');
  readonly prefix = signal('');
  readonly source = signal('');
  readonly tool = signal<AnimTool>('select');
  readonly shading = signal<Shading>('textured');
  readonly showSkeleton = signal(true);
  readonly showMesh = signal(true);
  readonly playing = signal(false);
  readonly loop = signal(true);
  readonly speed = signal(1);
  readonly time = signal(0);
  readonly frame = signal(0);
  readonly selectedBone = signal<string | null>(null);
  readonly boneVersion = signal(0);
  readonly loading = signal('');
  readonly busy = signal(false);
  readonly message = signal('');
  readonly messageIsError = signal(false);
  readonly hover = signal<string | null>(null);
  readonly weapons = signal<WeaponSummary[]>([]);
  readonly weaponKey = signal('');
  readonly autoFire = signal(false);
  readonly fxTree = signal<FxNode[]>([]);
  readonly skipped = signal<string[]>([]);
  readonly weaponInfo = signal<{ name: string; model: string; fireAnim: string; spawns: string[]; flash: string | null; shell: string | null; fireWait: number } | null>(null);

  readonly dirtyCount = computed(() => this.status()?.dirty.length ?? 0);
  // SOCOM II's skinned meshes are MESH_*; in SOCOM 3 / CA packages every character model is skinned.
  readonly characters = computed(() => this.models().filter(m => m.kind === 'character' && (!!this.status()?.game || /^MESH_/i.test(m.name))));
  readonly prefixes = computed(() => {
    const counts = new Map<string, number>();
    for (const c of this.clips()) { const p = c.name.split('_')[0] + '_'; counts.set(p, (counts.get(p) ?? 0) + 1); }
    return [...counts].filter(([, n]) => n >= 3).map(([p]) => p).sort();
  });
  // Other games (status.game set): clips grouped by their source file.
  readonly sources = computed(() => {
    if (!this.status()?.game) { return []; }
    const counts = new Map<string, number>();
    for (const c of this.clips()) { counts.set(c.source, (counts.get(c.source) ?? 0) + 1); }
    return [...counts].map(([name, count]) => ({ name, count }));
  });
  readonly groups = computed(() => {
    const q = this.query().toLowerCase(), pre = this.prefix(), src = this.source();
    const list = this.clips().filter(c => (!q || c.name.toLowerCase().includes(q)) && (!pre || c.name.startsWith(pre)) && (!src || c.source === src));
    if (this.sources().length > 1) {
      const bySource = new Map<string, AnimationSummary[]>();
      for (const c of list) { bySource.set(c.source, [...(bySource.get(c.source) ?? []), c]); }
      return [...bySource].map(([title, clips]) => ({ title, clips }));
    }
    return [
      { title: 'Package (MOTION_S)', clips: list.filter(c => c.editable) },
      { title: 'Shared (MOTION_P, read-only)', clips: list.filter(c => !c.editable) },
    ].filter(g => g.clips.length);
  });
  readonly bones = computed(() => { this.boneVersion(); this.clip(); return this.viewport?.boneNames() ?? []; });
  readonly editable = computed(() => !!this.current()?.editable);
  readonly canWrite = computed(() => !!this.status());
  readonly boneLocal = computed(() => { this.frame(); this.boneVersion(); this.selectedBone(); return this.viewport?.selectedLocal() ?? null; });

  readonly menus: Menu[] = [
    {
      id: 'file', label: 'File', items: [
        { label: 'Open package…', shortcut: 'Ctrl+O', action: () => this.pickPackage() },
        { separator: true, label: '' },
        { label: 'Save copy…', shortcut: 'Ctrl+S', action: () => void this.saveZdbCopy(), disabled: () => !this.dirtyCount() },
        { label: 'Export memory-card pack…', action: () => void this.exportPack(), disabled: () => !this.dirtyCount() },
      ],
    },
    {
      id: 'edit', label: 'Edit', items: [
        { label: 'Undo', shortcut: 'Ctrl+Z', action: () => void this.history(true), disabled: () => !this.status()?.canUndo },
        { label: 'Redo', shortcut: 'Ctrl+Y', action: () => void this.history(false), disabled: () => !this.status()?.canRedo },
      ],
    },
    {
      id: 'anim', label: 'Animation', items: [
        { label: 'New from seal_stand…', shortcut: 'Ctrl+N', action: () => void this.newClip(), disabled: () => !this.status() },
        { label: 'Duplicate / copy into package…', shortcut: 'Ctrl+D', action: () => void this.duplicateClip(), disabled: () => !this.current() },
        { label: 'Rename…', shortcut: 'F2', action: () => void this.renameClip(), disabled: () => !this.editable() },
        { label: 'Delete', shortcut: 'Delete', action: () => void this.deleteClip(), disabled: () => !this.editable() },
        { separator: true, label: '' },
        { label: 'Play / pause', shortcut: 'Space', action: () => this.togglePlay(), disabled: () => !this.clip() },
        { label: 'Loop', action: () => this.setLoop(!this.loop()), checked: () => this.loop() },
      ],
    },
    {
      id: 'view', label: 'View', items: [
        { label: 'Front', action: () => this.view('front') },
        { label: 'Back', action: () => this.view('back') },
        { label: 'Side', action: () => this.view('right') },
        { label: 'Frame character', shortcut: 'F', action: () => this.viewport.frameCharacter() },
        { separator: true, label: '' },
        { label: 'Skeleton', action: () => this.setSkeleton(!this.showSkeleton()), checked: () => this.showSkeleton() },
        { label: 'Mesh', action: () => this.setMesh(!this.showMesh()), checked: () => this.showMesh() },
      ],
    },
    {
      id: 'help', label: 'Help', items: [
        { label: 'Space play / pause · , and . step frames · Home / End first / last frame' },
        { label: 'Click a joint to select a bone · R rotate · G move · Q select (gizmo drags key the current frame)' },
        { label: 'Orbit: middle drag or Alt+left drag · Pan: right drag · Zoom: wheel' },
        { label: 'Shared MOTION_P clips are read-only: Copy into package, then edit the copy' },
      ],
    },
  ];

  constructor() {
    window.addEventListener('message', this.onMessage);
    window.addEventListener('keydown', this.onKey);
  }

  ngAfterViewInit(): void {
    this.viewport = new AnimViewport(this.canvasRef.nativeElement, {
      onBoneSelect: b => { this.selectedBone.set(b); this.boneVersion.update(v => v + 1); },
      onTime: t => this.syncTime(t),
      onKey: (bone, frame, key) => void this.commitKey(bone, frame, key),
      onHover: h => this.hover.set(h),
    });
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
    } else if (typeof msg?.['id'] === 'number' && ['pathPicked', 'textAnswered'].includes(msg['type'] as string)) {
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

  private async askName(title: string, value: string): Promise<string | null> {
    const r = await this.ask('askText', { title, value, pattern: '^[A-Za-z0-9_]{1,63}$', hint: '1-63 letters, digits or _' });
    return typeof r['text'] === 'string' && r['text'] ? r['text'] : null;
  }

  private say(text: string, error = false): void {
    this.message.set(text);
    this.messageIsError.set(error);
    this.api.postMessage({ type: 'log', text });
  }

  toggleMenu(id: string): void { this.openMenu.set(this.openMenu() === id ? null : id); }
  hoverMenu(id: string): void { if (this.openMenu()) { this.openMenu.set(id); } }
  runMenu(item: MenuItem): void { this.openMenu.set(null); item.action?.(); }
  closeMenusOutside(e: Event): void { if (!(e.target as HTMLElement).closest('.me-menu')) { this.openMenu.set(null); } }

  private readonly onKey = (e: KeyboardEvent): void => {
    const target = e.target as HTMLElement;
    if (target && ['INPUT', 'SELECT', 'TEXTAREA'].includes(target.tagName)) { return; }
    const ctrl = e.ctrlKey || e.metaKey;
    const k = e.key.toLowerCase();
    let handled = true;
    if (ctrl && k === 'z') { void this.history(true); }
    else if (ctrl && (k === 'y' || (e.shiftKey && k === 'z'))) { void this.history(false); }
    else if (ctrl && k === 's') { void this.saveZdbCopy(); }
    else if (ctrl && k === 'o') { this.pickPackage(); }
    else if (ctrl && k === 'n') { void this.newClip(); }
    else if (ctrl && k === 'd') { void this.duplicateClip(); }
    else if (ctrl) { handled = false; }
    else if (e.key === ' ') { this.togglePlay(); }
    else if (e.key === 'Enter') { this.fire(); }
    else if (e.key === ',') { this.stepFrame(-1); }
    else if (e.key === '.') { this.stepFrame(1); }
    else if (e.key === 'Home') { this.viewport.setFrame(0); }
    else if (e.key === 'End') { this.viewport.setFrame((this.clip()?.samples ?? 1) - 1); }
    else if (e.key === 'F2') { void this.renameClip(); }
    else if (e.key === 'Delete') { void this.deleteClip(); }
    else if (k === 'f') { this.viewport.frameCharacter(); }
    else if (k === 'q') { this.setTool('select'); }
    else if (k === 'r') { this.setTool('rotate'); }
    else if (k === 'g') { this.setTool('move'); }
    else if (e.key === 'Escape') { this.openMenu.set(null); this.viewport.selectBone(null); }
    else { handled = false; }
    if (handled) { e.preventDefault(); }
  };

  // ------------------------------------------------------------------ view / playback

  setTool(t: AnimTool): void {
    if (t !== 'select' && !this.editable()) { this.say('Shared clips are read-only: copy this one into the package to key it.', true); t = 'select'; }
    this.tool.set(t);
    this.viewport.tool = t;
  }
  setShading(s: Shading): void { this.shading.set(s); this.viewport.shading = s; }
  setSkeleton(on: boolean): void { this.showSkeleton.set(on); this.viewport.setSkeletonVisible(on); }
  setMesh(on: boolean): void { this.showMesh.set(on); this.viewport.setMeshVisible(on); }
  view(p: ViewPreset): void { this.viewport.setView(p); this.viewport.frameCharacter(); }
  togglePlay(): void { if (!this.clip()) { return; } this.viewport.playing = !this.viewport.playing; this.playing.set(this.viewport.playing); }
  setSpeed(v: number): void { this.speed.set(v); this.viewport.speed = v; }
  setLoop(v: boolean): void { this.loop.set(v); this.viewport.loop = v; }
  stepFrame(d: number): void { this.pause(); this.viewport.setFrame(this.viewport.frame + d); }
  scrub(v: string): void { this.pause(); this.viewport.setFrame(+v); }
  private pause(): void { this.viewport.playing = false; this.playing.set(false); }
  private syncTime(t: number): void {
    this.time.set(t);
    this.frame.set(this.viewport.frame);
    if (!this.viewport.playing && this.playing()) { this.playing.set(false); }
  }

  pickBone(name: string): void { this.viewport.selectBone(name); }

  // ------------------------------------------------------------------ weapon

  private async meshAndPixels(archive: string, name: string): Promise<{ mesh: EditorMesh; pixels: Record<string, ModelTexturePixels>; locators: Array<{ name: string; matrix: number[] }> }> {
    const wire = await this.client.mesh(archive, name) as MeshWire;
    const mesh = decodeMesh(wire);
    const pixels: Record<string, ModelTexturePixels> = {};
    await Promise.all([...new Set(mesh.chunks.map(c => c.texture).filter((t): t is string => !!t))].map(async n => {
      const p = await this.textures.get(n, wire.textureNamespace ?? null);
      if (p) { pixels[n.toLowerCase()] = p; }
    }));
    return { mesh, pixels, locators: wire.locators ?? [] };
  }

  async pickWeapon(model: string): Promise<void> {
    this.weaponKey.set(model);
    if (!model) {
      this.fxTree.set([]);
      this.viewport.setWeapon(null, {}, []);
      this.viewport.setEffect(null, {});
      this.weaponInfo.set(null);
      this.setAutoFire(false);
      return;
    }
    const w = this.weapons().find(x => x.model === model);
    if (!w) { return; }
    try {
      const gun = await this.meshAndPixels(w.archive, w.model);
      this.viewport.setWeapon(gun.mesh, gun.pixels, gun.locators);
      const fx = await this.client.weaponFx(w.model);
      const all: FxNode[] = [];
      const walk = (n: FxNode | null | undefined) => { if (!n) { return; } all.push(n); (n.spawns ?? []).forEach(walk); };
      walk(fx.effect);
      const models = all.flatMap(n => n.models ?? []);
      this.fxTree.set(all);
      const flashRef = models.find(m => /^muzzle_flash/i.test(m.name)) ?? null;
      const shellRef = models.find(m => /^bullet_shell/i.test(m.name)) ?? null;
      // Meshes for every model the effect tree uses (flash, shell); the script itself drives them (zanim-player).
      const pixels: Record<string, ModelTexturePixels> = {};
      const meshes = new Map<string, EditorMesh>();
      const seen = new Set<string>();
      for (const ref of models) {
        if (seen.has(ref.name)) { continue; }
        seen.add(ref.name);
        try {
          const got = await this.meshAndPixels(ref.archive, ref.name);
          meshes.set(ref.name, got.mesh);
          Object.assign(pixels, got.pixels);
        } catch { /* a model that does not decode is left out */ }
      }
      this.viewport.setEffect({ root: fx.effect, models: meshes }, pixels);
      this.viewport.fireWait = w.fireWait > 0 ? w.fireWait : 0.12;
      this.weaponInfo.set({
        name: w.internalName || w.model, model: w.model, fireAnim: w.fireAnimName, fireWait: w.fireWait,
        spawns: (fx.effect?.spawns ?? []).map(n => n.name), flash: flashRef?.name ?? null, shell: shellRef?.name ?? null,
      });
    } catch (err) {
      this.say(`${model}: ${errText(err)}`, true);
    }
  }

  fire(): void {
    if (!this.weaponKey()) { return; }
    this.viewport.fire();
    setTimeout(() => this.skipped.set([...this.viewport.skippedOps].sort()), 300);
  }

  refNames(n: FxNode): string { return (n.nodeRefs ?? []).slice(1).map((r, k) => `${k + 1}=${r.name}`).join(', '); }

  fieldText(f: Record<string, unknown>): string {
    return Object.entries(f ?? {}).filter(([k]) => k !== 'flags').map(([k, v]) =>
      `${k}=${Array.isArray(v) ? '(' + v.map(x => +Number(x).toFixed(3)).join(', ') + ')' : typeof v === 'number' ? +v.toFixed(4) : v}`).join(' ');
  }
  setAutoFire(on: boolean): void { this.autoFire.set(on); this.viewport.autoFire = on; }

  // ------------------------------------------------------------------ loading

  pickPackage(): void { this.api.postMessage({ type: 'pickPackage' }); }

  private async openPackage(path: string): Promise<void> {
    this.loading.set(`Opening ${this.fileName(path)}…`);
    try {
      const status = await this.client.open(path);
      this.status.set(status);
      this.textures.invalidate(status.revision);
      this.models.set(await this.client.listModels({ kind: 'character' }));
      await this.refreshClips();
      const chars = this.characters();
      // SOCOM's SEAL; GTA's swat ped (its first ped, male01, is a placeholder blob).
      const seal = chars.find(c => /seal/i.test(c.name) && !/_\d+(_LO)?$/i.test(c.name)) ?? chars.find(c => c.name.toLowerCase() === 'swat') ?? chars[0];
      if (seal) { await this.loadCharacter(`${seal.archive}:${seal.name}`); } else { this.hasCharacter.set(false); }
      try { this.weapons.set(await this.client.weapons()); } catch { this.weapons.set([]); }
      const m4 = this.weapons().find(w => w.model === 'm4Acarbine' || w.model === 'm4a1' || w.model === 'm4') ?? this.weapons()[0];
      if (m4) { await this.pickWeapon(m4.model); }
      const first = this.clips().find(c => c.name === 'seal_stand') ?? this.clips().find(c => c.name.toLowerCase() === 'idle_stance') ?? this.clips()[0];
      if (first) { await this.selectClip(first, true); }
      this.say(`${status.path}: ${this.clips().length} animations (${this.clips().filter(c => c.editable).length} in the package), ${chars.length} characters.`);
      if (!this.pollTimer) { this.pollTimer = setInterval(() => void this.poll(), 2000); }
    } catch (err) {
      this.say(`Could not open ${path}: ${errText(err)}`, true);
    } finally {
      this.loading.set('');
    }
  }

  private async refreshClips(): Promise<void> {
    const before = new Set(this.clips().map(c => `${c.source}:${c.name}`));
    const list = await this.client.listAnimations();
    this.clips.set(list.clips);
    const cur = this.current();
    if (cur) {
      // Gone (an undone rename / add, or an agent's edit): follow the clip that appeared, else drop the selection.
      this.current.set(list.clips.find(c => c.name === cur.name && c.source === cur.source)
        ?? list.clips.find(c => c.name === cur.name)
        ?? list.clips.find(c => c.editable && !before.has(`${c.source}:${c.name}`))
        ?? null);
    }
  }

  async pickCharacter(key: string): Promise<void> { await this.loadCharacter(key); }

  private async loadCharacter(key: string): Promise<void> {
    const cut = key.indexOf(':');
    this.characterKey.set(key);
    this.loading.set(`Loading ${key.slice(cut + 1)}…`);
    try {
      const wire = await this.client.mesh(key.slice(0, cut), key.slice(cut + 1)) as MeshWire;
      const mesh: EditorMesh = decodeMesh(wire);
      const ns = wire.textureNamespace ?? null;
      const pixels: Record<string, ModelTexturePixels> = {};
      await Promise.all([...new Set(mesh.chunks.map(c => c.texture).filter((t): t is string => !!t))].map(async n => {
        const p = await this.textures.get(n, ns);
        if (p) { pixels[n.toLowerCase()] = p; }
      }));
      this.viewport.setCharacter(mesh, pixels, this.hasCharacter());
      this.viewport.setClip(this.clip());
      this.hasCharacter.set(!!mesh.skeleton);
      this.boneVersion.update(v => v + 1);
    } catch (err) {
      this.say(`${key}: ${errText(err)}`, true);
    } finally {
      this.loading.set('');
    }
  }

  isCurrent(c: AnimationSummary): boolean { const cur = this.current(); return !!cur && cur.name === c.name && cur.source === c.source; }

  async selectClip(c: AnimationSummary, play = false): Promise<void> {
    this.current.set(c);
    await this.loadClip(play);
  }

  private async loadClip(play: boolean, keepTime = false): Promise<void> {
    const c = this.current();
    if (!c) { this.clip.set(null); this.viewport.setClip(null); return; }
    try {
      const data = await this.client.animation(c.name, c.source) as ClipData;
      if (this.current() !== c) { return; }
      const t = this.viewport.time;
      this.clip.set(data);
      this.viewport.setClip(data);
      if (keepTime) { this.viewport.setTime(Math.min(t, data.duration)); } else { this.viewport.setTime(0); }
      if (play) { this.viewport.playing = true; this.playing.set(true); }
      if (!c.editable && this.tool() !== 'select') { this.setTool('select'); }
      this.boneVersion.update(v => v + 1);
    } catch (err) {
      this.say(`${c.name}: ${errText(err)}`, true);
    }
  }

  // Edits made by anyone on the session (an agent, the other editors) show up here.
  private async poll(): Promise<void> {
    if (!this.client.session || this.busy() || this.loading()) { return; }
    try {
      const status = await this.client.status();
      const before = this.status()?.revision;
      this.status.set(status);
      if (before !== undefined && status.revision !== before) {
        await this.refreshClips();
        await this.loadClip(this.viewport.playing, true);
        this.say(`Session changed (revision ${status.revision}${status.canUndo ? `: ${status.canUndo}` : ''}).`);
      }
    } catch { /* retried next poll */ }
  }

  // ------------------------------------------------------------------ edits (all through the bridge)

  private async edit(label: string, op: Record<string, unknown>, select?: { name: string; source: string }): Promise<boolean> {
    this.busy.set(true);
    try {
      const raw = await this.client.edit({ label, ...op }) as unknown as { result?: { revision: number; description?: string }; revision?: number; description?: string };
      const result = raw.result ?? (raw as { revision: number; description?: string });
      this.status.set(await this.client.status());
      this.say(`${result.description ?? label} (revision ${result.revision})`);
      await this.refreshClips();
      if (select) {
        const c = this.clips().find(x => x.name === select.name && x.source === select.source);
        if (c) { this.current.set(c); }
      }
      await this.loadClip(false, true);
      return true;
    } catch (err) {
      this.say(`${label}: ${errText(err)}`, true);
      this.viewport.clearOverride();
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  private uniqueName(base: string): string {
    const names = new Set(this.clips().map(c => c.name.toLowerCase()));
    if (!names.has(base.toLowerCase())) { return base; }
    for (let i = 2; ; i++) { const n = `${base}${i}`; if (!names.has(n.toLowerCase())) { return n; } }
  }

  async newClip(): Promise<void> {
    if (!this.status()) { return; }
    const name = await this.askName('New animation (a copy of seal_stand to start from)', this.uniqueName('my_anim'));
    if (!name) { return; }
    await this.edit(`add animation ${name}`, { op: 'addAnimation', name, from: 'seal_stand' }, { name, source: PACKAGE_SOURCE });
  }

  async duplicateClip(): Promise<void> {
    const c = this.current();
    if (!c) { return; }
    const name = await this.askName(c.editable ? `Duplicate ${c.name} as` : `Copy ${c.name} into the package as`, this.uniqueName(c.editable ? `${c.name}_copy` : c.name));
    if (!name) { return; }
    await this.edit(`${c.editable ? 'duplicate' : 'copy'} ${c.name} as ${name}`, { op: 'addAnimation', name, from: c.name, fromSource: c.source, after: c.editable ? c.name : undefined },
      { name, source: PACKAGE_SOURCE });
  }

  async renameClip(): Promise<void> {
    const c = this.current();
    if (!c?.editable) { return; }
    const name = await this.askName(`Rename ${c.name} to`, c.name);
    if (!name || name === c.name) { return; }
    await this.edit(`rename ${c.name} to ${name}`, { op: 'renameAnimation', name: c.name, newName: name }, { name, source: PACKAGE_SOURCE });
  }

  async deleteClip(): Promise<void> {
    const c = this.current();
    if (!c?.editable) { return; }
    const ok = await this.edit(`delete ${c.name}`, { op: 'deleteAnimation', name: c.name });
    if (ok) {
      const next = this.clips().find(x => x.name === c.name) ?? this.clips().find(x => x.editable) ?? this.clips()[0] ?? null;
      this.current.set(next);
      await this.loadClip(false);
    }
  }

  async setDuration(v: string): Promise<void> {
    const c = this.current(), d = parseFloat(v);
    if (!c?.editable || !(d > 0)) { return; }
    await this.edit(`set ${c.name} duration to ${d}s`, { op: 'setAnimation', name: c.name, duration: d });
  }

  async setFrames(v: string): Promise<void> {
    const c = this.current(), n = parseInt(v, 10);
    if (!c?.editable || !(n >= 1) || n === c.frames) { return; }
    await this.edit(`resample ${c.name} to ${n} frames`, { op: 'setAnimation', name: c.name, frames: n });
  }

  private async commitKey(bone: string, frame: number, key: { rotation?: number[]; position?: number[] }): Promise<void> {
    const c = this.current();
    if (!c?.editable) { this.viewport.clearOverride(); return; }
    await this.edit(`key ${bone} at frame ${frame}`, { op: 'setAnimationKey', name: c.name, bone, frame, ...key });
  }

  async setBoneValue(kind: 'position' | 'rotation', axis: number, value: string): Promise<void> {
    const bone = this.selectedBone(), l = this.boneLocal(), v = parseFloat(value);
    if (!bone || !l || !Number.isFinite(v)) { return; }
    this.pause();
    const frame = this.viewport.frame;
    if (kind === 'position') {
      const p = [...l.position]; p[axis] = v;
      await this.commitKey(bone, frame, { position: p });
    } else {
      const e = this.eulerOf(l.rotation); e[axis] = v;
      await this.commitKey(bone, frame, { rotation: quatFromEuler(e) });
    }
  }

  async holdPose(): Promise<void> {
    const c = this.current(), bone = this.selectedBone(), l = this.boneLocal();
    if (!c?.editable || !bone || !l) { return; }
    await this.edit(`hold ${bone} for all frames`, { op: 'setAnimationKey', name: c.name, bone, allFrames: true, rotation: l.rotation });
  }

  private async history(undo: boolean): Promise<void> {
    if (!this.client.session) { return; }
    this.busy.set(true);
    try {
      const r = undo ? await this.client.undo() : await this.client.redo();
      this.status.set(await this.client.status());
      this.say(`${undo ? 'Undid' : 'Redid'} ${r.description ?? ''} (revision ${r.revision})`);
      await this.refreshClips();
      await this.loadClip(false, true);
    } catch (err) {
      this.say(errText(err), true);
    } finally {
      this.busy.set(false);
    }
  }

  async saveZdbCopy(): Promise<void> {
    const s = this.status();
    if (!s || !this.dirtyCount()) { return; }
    // A .CDB pack keeps its name (the game mounts mc0:/BASCUS-97275<NAME>/<NAME>.CDB); a .ZDB gets an _edit copy.
    const picked = await this.ask('pickSavePath', { kind: 'zdb', suggested: /\.cdb$/i.test(s.path) ? s.path : s.path.replace(/(\.zdb)$/i, '_edit$1') });
    if (typeof picked['path'] !== 'string') { return; }
    await this.save({ mode: 'zdb', outputPath: picked['path'] });
  }

  async exportPack(): Promise<void> {
    const picked = await this.ask('pickSavePath', { kind: 'pack', suggested: 'MANIM' });
    if (typeof picked['path'] !== 'string' || typeof picked['packName'] !== 'string') { return; }
    await this.save({ mode: 'pack', packName: picked['packName'], outputPath: picked['path'] });
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

  eulerOf(q: number[]): number[] {
    const [x, y, z, w] = q;
    const rx = Math.atan2(2 * (w * x + y * z), 1 - 2 * (x * x + y * y));
    const ry = Math.asin(Math.max(-1, Math.min(1, 2 * (w * y - z * x))));
    const rz = Math.atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z));
    return [rx, ry, rz].map(r => r * 180 / Math.PI);
  }
  fileName(p: string): string { return p.split(/[\\/]/).pop() ?? p; }
  fmt(v: number): string { return (Math.abs(v) < 1e-4 ? 0 : v).toFixed(2); }
}

// XYZ (roll, pitch, yaw) degrees -> quaternion, the inverse of eulerOf.
function quatFromEuler(e: number[]): number[] {
  const [x, y, z] = e.map(d => d * Math.PI / 360);
  const cx = Math.cos(x), sx = Math.sin(x), cy = Math.cos(y), sy = Math.sin(y), cz = Math.cos(z), sz = Math.sin(z);
  return [sx * cy * cz - cx * sy * sz, cx * sy * cz + sx * cy * sz, cx * cy * sz - sx * sy * cz, cx * cy * cz + sx * sy * sz];
}

function errText(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}
