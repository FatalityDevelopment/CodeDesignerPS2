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
import * as THREE from 'three';
import { getVsCodeApi } from '../shared/vscode-api';
import { IconComponent } from '../shared/icon.component';
import type { ModelTexturePixels } from '../zipper-workbench/zed-model-renderer';
import { AssetClient, type ExportResult, type ModelDescription, type ModelSummary, type SessionStatus, type TextureInfo } from './asset-client';
import { TextureCache } from './model-loader';
import { decodeMesh, toModelMatrix, toModelPoint, type EditorChunk, type EditorMesh, type MeshWire } from './mesh-data';
import { EditorViewport, type HoverInfo, type SelectMode, type Shading, type Tool, type ViewPreset, type VertexSelection } from './editor-viewport';
import { UvEditor, type UvEdits } from './uv-editor';
import '../shared/styles/shared.css';
import './model-editor-page.css';

type PropTab = 'item' | 'material' | 'texture' | 'paint' | 'model';
type BottomTab = 'uv' | 'log';

interface MenuItem {
  label: string;
  shortcut?: string;
  action?: () => void;
  disabled?: () => boolean;
  checked?: () => boolean;
  separator?: boolean;
}

interface Menu { id: string; label: string; items: MenuItem[] }

const KIND_ORDER = ['prop', 'weapon', 'character', 'world', 'sky', 'library'];
const KIND_LABEL: Record<string, string> = {
  prop: 'Props', weapon: 'Weapons', character: 'Characters', world: 'World', sky: 'Sky', library: 'Libraries',
};

// Model Editor. Every edit is a bridge asset-session command (AssetClient), the
// same operations the assets_* MCP tools use, so an agent and this editor can
// work on one package together; the viewport and UV editor only preview.
@Component({
  selector: 'app-model-editor-page',
  standalone: true,
  imports: [IconComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="me-app" (pointerdown)="closeMenusOutside($event)">
  <!-- Menu bar -->
  <div class="me-menubar">
    <span class="me-brand"><app-icon name="library" [size]="14" /> Model Editor</span>
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
      @if (s.game) { <span class="me-badge" title="Edits and saves are not available for this game yet">{{ s.game }} · read-only</span> }
      @if (exportTarget(); as target) {
        <button class="lm-tool-btn" [disabled]="busy()" (click)="openExport()" [title]="'Convert ' + (current()?.name ?? '') + ' into a ' + target + ' package and open it'">Export to {{ target }}…</button>
      }
      @else { <span class="me-badge" [class.me-badge-dirty]="dirtyCount() > 0">{{ dirtyCount() ? dirtyCount() + ' unsaved' : 'saved' }} · rev {{ s.revision }}</span> }
      <span class="me-session" title="Asset session: assets_* MCP tools can work on it too">{{ s.session }}</span>
    }
  </div>

  @if (exportOpen()) {
    <div class="me-dialog-backdrop" (click)="exportOpen.set(false)">
      <div class="me-dialog" (click)="$event.stopPropagation()">
        <div class="me-dialog-title">Export {{ current()?.name }} to {{ exportTarget() }}</div>
        <p class="lm-muted me-dialog-note">
          @if (exportToCa()) { Builds a SOCOM 3 / CA package (.ZDB) from this model, then opens it here. CA loading it in game is not confirmed yet. }
          @else { Builds a SOCOM II memory-card pack from this model with the converter confirmed in game, then opens it here. }
        </p>
        @if (!exportToCa()) {
          <label class="me-field me-field-wide"><span>Pack name</span>
            <input class="me-input" type="text" maxlength="7" placeholder="automatic" [value]="exportPackName()" (input)="exportPackName.set($any($event.target).value)" /></label>
        }
        <label class="me-field me-field-wide"><span>Model name</span>
          <input class="me-input" type="text" maxlength="31" [placeholder]="exportModelPlaceholder()" [value]="exportModelName()" (input)="exportModelName.set($any($event.target).value)" /></label>
        <label class="me-field me-field-wide"><span>Folder</span>
          <input class="me-input" type="text" placeholder="Documents/CodeDesigner/Exports" [value]="exportDir()" (input)="exportDir.set($any($event.target).value)" /></label>
        @if (current()?.kind === 'character') {
          <div class="me-dialog-sub">Animations <span class="lm-muted">({{ exportClips().size }} selected; {{ exportToCa() ? 'written as a CA clip bank' : 'retargeted onto the SOCOM II skeleton' }})</span></div>
          <div class="me-dialog-row">
            <input class="me-search" type="text" placeholder="Filter clips…" [value]="exportClipQuery()" (input)="exportClipQuery.set($any($event.target).value)" />
            <button class="lm-tool-btn" (click)="selectShownClips(true)">All shown</button>
            <button class="lm-tool-btn" (click)="selectShownClips(false)">None</button>
          </div>
          <div class="me-dialog-list">
            @for (c of shownExportClips(); track c) {
              <label class="me-toggle"><input type="checkbox" [checked]="exportClips().has(c)" (change)="toggleExportClip(c)" /> {{ c }}</label>
            }
            @if (!shownExportClips().length) { <div class="lm-muted">No clips match.</div> }
          </div>
        }
        <div class="me-dialog-actions">
          <button class="lm-tool-btn" [disabled]="busy()" (click)="runExport()">{{ busy() ? 'Exporting…' : 'Export and open' }}</button>
          <button class="lm-tool-btn" (click)="exportOpen.set(false)">Cancel</button>
        </div>
      </div>
    </div>
  }

  <!-- Header toolbar -->
  <div class="me-header">
    <div class="me-seg" title="Selection mode (1-5)">
      @for (m of selectModes; track m.id) {
        <button [class.on]="selectMode() === m.id" (click)="setSelectMode(m.id)" [title]="m.label + ' (' + m.key + ')'">{{ m.label }}</button>
      }
    </div>
    <div class="me-seg" title="Shading (Z cycles)">
      @for (s of shadings; track s.id) {
        <button [class.on]="shading() === s.id" (click)="setShading(s.id)">{{ s.label }}</button>
      }
    </div>
    <label class="me-toggle"><input type="checkbox" [checked]="baked()" (change)="setBaked($any($event.target).checked)" /> Baked lighting</label>
    <label class="me-toggle"><input type="checkbox" [checked]="grid()" (change)="grid.set($any($event.target).checked); applyHelpers()" /> Grid</label>
    <label class="me-toggle"><input type="checkbox" [checked]="normals()" (change)="normals.set($any($event.target).checked); applyHelpers()" /> Normals</label>
    <label class="me-toggle"><input type="checkbox" [checked]="snap()" (change)="snap.set($any($event.target).checked); applySnap()" /> Snap</label>
    @if (snap()) {
      <input class="me-num me-num-sm" type="number" min="0.001" step="0.5" [value]="snapMove()" (change)="snapMove.set(+$any($event.target).value || 1); applySnap()" title="Move snap (units)" />
      <input class="me-num me-num-sm" type="number" min="1" step="5" [value]="snapRotate()" (change)="snapRotate.set(+$any($event.target).value || 15); applySnap()" title="Rotate snap (degrees)" />
    }
    <span class="me-spacer"></span>
    <div class="me-seg" title="View">
      <button (click)="view('front')" title="Front (Numpad 1)">Front</button>
      <button (click)="view('back')" title="Back (Ctrl+Numpad 1)">Back</button>
      <button (click)="view('right')" title="Right (Numpad 3)">Right</button>
      <button (click)="view('top')" title="Top (Numpad 7)">Top</button>
      <button (click)="view('iso')" title="Iso">Iso</button>
      <button [class.on]="ortho()" (click)="toggleOrtho()" title="Orthographic (Numpad 5)">Ortho</button>
      <button (click)="frameSelected()" title="Frame selected (F)">Frame</button>
    </div>
  </div>

  <div class="me-body" [class.no-assets]="!showAssets()">
    <!-- Asset browser -->
    @if (showAssets()) {
      <aside class="me-assets">
        <div class="me-panel-title">Assets <button class="lm-row-btn" (click)="showAssets.set(false)" title="Hide">×</button></div>
        <input class="me-search" type="text" placeholder="Filter models…" [value]="query()" (input)="query.set($any($event.target).value)" />
        <div class="me-kinds">
          <button class="me-chip" [class.active]="!kindFilter()" (click)="kindFilter.set('')">All</button>
          @for (k of kinds(); track k) {
            <button class="me-chip" [class.active]="kindFilter() === k" (click)="kindFilter.set(k)">{{ kindLabel(k) }}</button>
          }
        </div>
        <div class="me-list">
          @if (!status()) { <div class="me-empty">File › Open package… to load a .ZDB, a .CDB pack or a loose .ZED.</div> }
          @for (group of groups(); track group.kind) {
            <div class="me-group-title">{{ kindLabel(group.kind) }} <span class="lm-muted">{{ group.models.length }}</span></div>
            @for (m of group.models; track m.archive + ':' + m.name) {
              <button type="button" class="me-row" [class.active]="isCurrent(m)" (click)="select(m)" [title]="m.archive">
                <span class="me-row-name">{{ m.name }}</span>
                <span class="me-row-meta">{{ m.visualCount }} vis</span>
              </button>
            }
          }
        </div>
      </aside>
    }

    <!-- Tool shelf -->
    <nav class="me-tools">
      @if (!showAssets()) { <button class="me-tool" (click)="showAssets.set(true)" title="Show assets"><app-icon name="library" [size]="16" /></button> }
      @for (t of tools; track t.id) {
        <button class="me-tool" [class.on]="tool() === t.id" (click)="setTool(t.id)" [title]="t.label + ' (' + t.key + ')'">
          <span class="me-tool-glyph">{{ t.glyph }}</span>
        </button>
      }
    </nav>

    <!-- Viewport + bottom panel -->
    <section class="me-center">
      <div class="me-viewport">
        <canvas #canvas tabindex="0"></canvas>
        <div class="me-vp-label">
          <div>{{ viewLabel() }} · {{ selectModeLabel() }} mode · {{ toolLabel() }}</div>
          @if (mesh(); as m) { <div class="lm-muted">{{ m.name }} — {{ meshStats() }}</div> }
        </div>
        @if (loading()) { <div class="me-vp-center">Loading {{ current()?.name }}…</div> }
        @else if (!mesh() && status()) { <div class="me-vp-center">Pick a model in Assets.</div> }
        @else if (!status()) { <div class="me-vp-center">File › Open package… (Ctrl+O)</div> }
        @if (busy()) { <div class="me-vp-busy">Applying…</div> }
      </div>
      <div class="me-bottom" [class.collapsed]="!showBottom()">
        <div class="me-bottom-tabs">
          <button [class.on]="bottomTab() === 'uv'" (click)="bottomTab.set('uv'); showBottom.set(true); refreshUv()">UV Editor</button>
          <button [class.on]="bottomTab() === 'log'" (click)="bottomTab.set('log'); showBottom.set(true)">Info</button>
          <span class="me-spacer"></span>
          @if (bottomTab() === 'uv' && showBottom()) {
            <span class="lm-muted">{{ uvCount() }} UV selected</span>
            <button class="lm-row-btn" (click)="uvOp('flipU')" title="Mirror selected UVs horizontally">Flip U</button>
            <button class="lm-row-btn" (click)="uvOp('flipV')" title="Mirror selected UVs vertically">Flip V</button>
            <button class="lm-row-btn" (click)="uvOp('rot90')" title="Rotate selected UVs 90°">Rotate 90°</button>
            <label class="me-toggle"><input type="checkbox" (change)="uvSnap($any($event.target).checked)" /> Pixel snap</label>
            <button class="lm-row-btn" (click)="uvFrame()">Frame</button>
          }
          <button class="lm-row-btn" (click)="showBottom.set(!showBottom())">{{ showBottom() ? '▾' : '▴' }}</button>
        </div>
        <div class="me-bottom-body" [hidden]="!showBottom()">
          <div class="me-uv" [hidden]="bottomTab() !== 'uv'">
            <canvas #uvcanvas></canvas>
            @if (!uvChunks().length) { <div class="me-vp-center">Select chunks, faces or vertices to edit their UVs.</div> }
          </div>
          <div class="me-log" [hidden]="bottomTab() !== 'log'">
            @for (line of logLines(); track $index) { <div [class.err]="line.error">{{ line.text }}</div> }
          </div>
        </div>
      </div>
    </section>

    <!-- Outliner + properties -->
    <aside class="me-side">
      <div class="me-outliner">
        <div class="me-panel-title">Outliner</div>
        @if (mesh(); as m) {
          <div class="me-tree">
            <div class="me-tree-row me-tree-root" (click)="selectAllChunks()"><app-icon name="database" [size]="12" /> {{ m.name }}</div>
            @for (v of visuals(); track v.name) {
              <div class="me-tree-row me-tree-visual" [class.sel]="visualSelected(v.name)">
                <button class="me-eye" (click)="toggleVisualHidden(v.name)" [title]="v.hidden ? 'Show' : 'Hide'">{{ v.hidden ? '◌' : '●' }}</button>
                <span (click)="selectVisual(v.name, $event)">{{ v.name }}</span>
                <span class="lm-muted">{{ v.chunks.length }}</span>
              </div>
              @for (c of v.chunks; track c.index) {
                <div class="me-tree-row me-tree-chunk" [class.sel]="chunkSelected(c.index)">
                  <button class="me-eye" (click)="toggleChunkHidden(c.index)" [title]="isHidden(c.index) ? 'Show' : 'Hide'">{{ isHidden(c.index) ? '◌' : '●' }}</button>
                  <span (click)="selectChunk(c.index, $event)">#{{ c.index }} {{ c.texture ?? '(no texture)' }}</span>
                  <span class="lm-muted">{{ c.vertexCount }}v</span>
                </div>
              }
            }
          </div>
        } @else { <div class="me-empty">No model.</div> }
      </div>

      <div class="me-props">
        <div class="me-tabs">
          @for (t of propTabs; track t.id) {
            <button class="me-tab" [class.active]="propTab() === t.id" (click)="propTab.set(t.id)" [title]="t.label">{{ t.label }}</button>
          }
        </div>
        <div class="me-tab-body">
          @switch (propTab()) {
            @case ('item') {
              <div class="me-card">
                <div class="me-card-title">Selection</div>
                <div class="me-kv2"><span>Chunks</span><b>{{ counts().chunks }}</b><span>Vertices</span><b>{{ counts().vertices }}</b><span>Faces</span><b>{{ counts().faces }}</b></div>
              </div>
              <div class="me-card">
                <div class="me-card-title">Transform</div>
                <div class="me-xform">
                  <span>Location</span>
                  @for (k of [0, 1, 2]; track k) {
                    <input class="me-num" type="number" step="1" [value]="fmt(location()[k])" [disabled]="!counts().vertices" (change)="setLocation(k, $any($event.target).value)" />
                  }
                  <span>Rotate °</span>
                  @for (k of [0, 1, 2]; track k) {
                    <input class="me-num" type="number" step="5" [value]="pendingRot()[k]" [disabled]="!counts().vertices" (input)="setPending('rot', k, $any($event.target).value)" />
                  }
                  <span>Scale</span>
                  @for (k of [0, 1, 2]; track k) {
                    <input class="me-num" type="number" step="0.1" [value]="pendingScale()[k]" [disabled]="!counts().vertices" (input)="setPending('scale', k, $any($event.target).value)" />
                  }
                </div>
                <div class="me-actions">
                  <button class="lm-tool-btn" [disabled]="!counts().vertices || !pendingActive()" (click)="applyPending()">Apply</button>
                  <button class="lm-tool-btn" [disabled]="!pendingActive()" (click)="resetPending()">Reset</button>
                </div>
                <div class="lm-muted me-note">X right, Y up, Z toward the front view; pivot = selection centre.</div>
              </div>
              <div class="me-card">
                <div class="me-card-title">Geometry</div>
                <label class="me-field me-field-wide">Visual
                  <select [value]="targetVisual()" (change)="targetVisual.set($any($event.target).value)">
                    @for (v of visuals(); track v.name) { <option [value]="v.name">{{ v.name }}</option> }
                  </select>
                </label>
                <label class="me-field me-field-wide">Texture
                  <input class="me-input" type="text" list="me-tex-list2" [value]="targetTexture()" (input)="targetTexture.set($any($event.target).value)" />
                </label>
                <datalist id="me-tex-list2">
                  @for (t of description()?.textures ?? []; track t.name) { <option [value]="t.name"></option> }
                </datalist>
                <label class="me-field me-field-wide">Size
                  <input class="me-num" type="number" min="0.1" step="1" [value]="primSize()" (change)="primSize.set(+$any($event.target).value || 10)" />
                </label>
                <div class="me-actions">
                  <button class="lm-tool-btn" [disabled]="busy() || !canAdd()" (click)="addPrimitive('plane')">Add plane</button>
                  <button class="lm-tool-btn" [disabled]="busy() || !canAdd()" (click)="addPrimitive('cube')">Add cube</button>
                  <button class="lm-tool-btn" [disabled]="busy() || !canAdd()" (click)="importObj()">Import OBJ…</button>
                </div>
                <div class="me-actions">
                  <button class="lm-tool-btn" [disabled]="busy() || !counts().vertices" (click)="duplicateSelection()">Duplicate (Shift+D)</button>
                  <button class="lm-tool-btn lm-danger-btn" [disabled]="busy() || !counts().vertices" (click)="deleteSelection()">Delete faces (X)</button>
                </div>
                <div class="lm-muted me-note">New geometry goes into the visual above with the texture above, at the selection centre (or the origin). Delete removes the selected faces (vertex mode: faces whose three vertices are selected; chunk mode: whole chunks).</div>
              </div>
            }
            @case ('material') {
              @if (selectedChunkInfos().length) {
                @for (c of selectedChunkInfos().slice(0, 6); track c.index) {
                  <div class="me-card">
                    <div class="me-card-title">Chunk #{{ c.index }}</div>
                    <dl class="me-kv">
                      <dt>Visual</dt><dd class="lm-mono">{{ c.visual }}</dd>
                      <dt>Texture</dt><dd class="lm-mono">{{ c.texture ?? '—' }}</dd>
                      <dt>Blend</dt><dd>{{ c.materialBlend === null ? '—' : c.materialBlend ? 'alpha blend (0x44)' : 'copy + alpha test' }}</dd>
                      <dt>Vertices</dt><dd>{{ c.vertexCount }} · {{ c.triangles.length / 3 }} tris</dd>
                      <dt>Colours</dt><dd>{{ c.colors ? 'baked vertex colours' : 'none' }}</dd>
                      <dt>Units</dt><dd>{{ c.unit }}</dd>
                    </dl>
                  </div>
                }
                <div class="me-card">
                  <div class="me-card-title">Retexture selected sections</div>
                  <input class="me-input" type="text" list="me-tex-list" placeholder="texture.tif" [value]="retexTo()" (input)="retexTo.set($any($event.target).value)" />
                  <datalist id="me-tex-list">
                    @for (t of description()?.textures ?? []; track t.name) { <option [value]="t.name"></option> }
                  </datalist>
                  <button class="lm-tool-btn" [disabled]="busy() || !retexTo()" (click)="retexture()">Apply</button>
                  <div class="lm-muted me-note">Points every section using the selected chunks' texture at the new .tif (it must exist in the package).</div>
                </div>
              } @else { <div class="me-empty">Select chunks to see their material.</div> }
            }
            @case ('texture') {
              @for (t of selectedTextures(); track t.name) {
                <div class="me-card">
                  <div class="me-tex-head">
                    @if (thumbs()[t.name]; as url) { <img class="me-thumb" [src]="url" [alt]="t.name" /> }
                    <div>
                      <div class="lm-mono">{{ t.name }}</div>
                      <div class="lm-muted">{{ texSummary(t) }}</div>
                    </div>
                  </div>
                  <div class="me-actions">
                    <button class="lm-tool-btn" [disabled]="busy() || !t.found" (click)="replaceTexture(t)">Replace from PNG…</button>
                    <button class="lm-tool-btn" [disabled]="!t.found" (click)="exportTexture(t)">Export PNG…</button>
                  </div>
                </div>
              } @empty { <div class="me-empty">Select chunks to see their textures.</div> }
            }
            @case ('paint') {
              <div class="me-card">
                <div class="me-card-title">Vertex paint (baked lighting)</div>
                <label class="me-field">Colour <input type="color" [value]="brushHex()" (input)="setBrushColor($any($event.target).value)" /></label>
                <label class="me-field">Intensity <input type="range" min="0" max="2" step="0.05" [value]="brushIntensity()" (input)="setBrush('intensity', $any($event.target).value)" /> <span>{{ brushIntensity() }}</span></label>
                <label class="me-field">Radius <input type="range" min="0.5" [max]="maxRadius()" step="0.5" [value]="brushRadius()" (input)="setBrush('radius', $any($event.target).value)" /> <span>{{ brushRadius() }}</span></label>
                <label class="me-field">Strength <input type="range" min="0.05" max="1" step="0.05" [value]="brushStrength()" (input)="setBrush('strength', $any($event.target).value)" /> <span>{{ brushStrength() }}</span></label>
                <div class="me-actions">
                  <button class="lm-tool-btn" (click)="setTool('paint')">Paint tool (P)</button>
                  <button class="lm-tool-btn" [disabled]="!counts().vertices || busy()" (click)="fillSelection()">Fill selection</button>
                  <button class="lm-tool-btn" [disabled]="!counts().vertices || busy()" (click)="resetColors()">Reset to neutral</button>
                </div>
                <div class="lm-muted me-note">White at intensity 1 = the texture as-is (GS 0x80). Painting limits itself to the selected chunks when any are selected.</div>
              </div>
            }
            @case ('model') {
              @if (description(); as d) {
                <div class="me-card">
                  <div class="me-card-title">Model</div>
                  <dl class="me-kv">
                    <dt>Name</dt><dd class="lm-mono">{{ d.name }}</dd>
                    <dt>Kind</dt><dd>{{ d.kind }}</dd>
                    <dt>Archive</dt><dd class="lm-mono">{{ d.archive }}</dd>
                    <dt>Visuals</dt><dd>{{ d.visuals.length }}</dd>
                    <dt>Textures</dt><dd>{{ d.textures.length }} ({{ d.textureNamespace ?? '?' }})</dd>
                    @if (mesh()?.skeleton; as sk) { <dt>Skeleton</dt><dd>{{ sk.length }} bones, skinned (geometry is view-only for now)</dd> }
                    @if (d.geo; as g) {
                      <dt>GEO</dt><dd class="lm-mono">{{ d.geoArchive }} › {{ g.name }}</dd>
                      <dt>Units</dt><dd>{{ g.positionUnit }} {{ g.vparamsFlags }}</dd>
                    }
                  </dl>
                </div>
                @if (missingTextures().length) { <div class="me-card me-warn">Missing textures: {{ missingTextures().join(', ') }}</div> }
              }
              <div class="me-card">
                <div class="me-card-title">Agents</div>
                <div class="me-note">An agent can edit this model with the assets_* MCP tools on session <span class="lm-mono">{{ status()?.session }}</span>; its edits appear here.</div>
              </div>
            }
          }
        </div>
      </div>
    </aside>
  </div>

  <!-- Status bar -->
  <div class="me-statusbar" [class.err]="messageIsError()">
    <span>{{ message() || hint() }}</span>
    <span class="me-spacer"></span>
    @if (hover(); as h) {
      <span class="lm-muted">#{{ h.chunk.index }} {{ h.chunk.texture }}{{ h.vertex !== null ? ' · v' + h.vertex : '' }} · {{ fmtVec(h.point) }}</span>
    }
    <span>{{ counts().chunks }} chunk(s) · {{ counts().vertices }} vert(s) · {{ counts().faces }} face(s)</span>
  </div>
</div>
`,
})
export class ModelEditorPageComponent implements AfterViewInit, OnDestroy {
  @ViewChild('canvas', { static: true }) private canvasRef!: ElementRef<HTMLCanvasElement>;
  @ViewChild('uvcanvas', { static: true }) private uvCanvasRef!: ElementRef<HTMLCanvasElement>;

  private readonly api = getVsCodeApi();
  private readonly client = new AssetClient();
  private readonly textures = new TextureCache(this.client);
  private viewport!: EditorViewport;
  private uv!: UvEditor;
  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private nextRequest = 1;
  private readonly waiters = new Map<number, (msg: Record<string, unknown>) => void>();

  readonly selectModes: Array<{ id: SelectMode; label: string; key: string }> = [
    { id: 'object', label: 'Object', key: '1' }, { id: 'visual', label: 'Visual', key: '2' }, { id: 'chunk', label: 'Chunk', key: '3' },
    { id: 'vertex', label: 'Vertex', key: '4' }, { id: 'face', label: 'Face', key: '5' },
  ];
  readonly shadings: Array<{ id: Shading; label: string }> = [
    { id: 'textured', label: 'Textured' }, { id: 'solid', label: 'Solid' }, { id: 'wireframe', label: 'Wire' }, { id: 'xray', label: 'X-ray' },
  ];
  readonly tools: Array<{ id: Tool; label: string; key: string; glyph: string }> = [
    { id: 'select', label: 'Select (click / box drag)', key: 'Q', glyph: '⬚' },
    { id: 'move', label: 'Move', key: 'G', glyph: '✥' },
    { id: 'rotate', label: 'Rotate', key: 'R', glyph: '⟳' },
    { id: 'scale', label: 'Scale', key: 'S', glyph: '⤢' },
    { id: 'paint', label: 'Vertex paint', key: 'P', glyph: '🖌' },
  ];
  readonly propTabs: Array<{ id: PropTab; label: string }> = [
    { id: 'item', label: 'Item' }, { id: 'material', label: 'Material' }, { id: 'texture', label: 'Texture' }, { id: 'paint', label: 'Paint' }, { id: 'model', label: 'Model' },
  ];

  // Session / data.
  readonly status = signal<SessionStatus | null>(null);
  readonly models = signal<ModelSummary[]>([]);
  readonly current = signal<ModelSummary | null>(null);
  readonly description = signal<ModelDescription | null>(null);
  readonly mesh = signal<EditorMesh | null>(null);
  readonly thumbs = signal<Record<string, string>>({});
  readonly missingTextures = signal<string[]>([]);
  private texturePixels: Record<string, ModelTexturePixels> = {};

  // UI state.
  readonly openMenu = signal<string | null>(null);
  readonly query = signal('');
  readonly kindFilter = signal('');
  readonly showAssets = signal(true);
  readonly showBottom = signal(true);
  readonly bottomTab = signal<BottomTab>('uv');
  readonly propTab = signal<PropTab>('item');
  readonly selectMode = signal<SelectMode>('chunk');
  readonly tool = signal<Tool>('select');
  readonly shading = signal<Shading>('textured');
  // Unlit by default: much retail baked lighting is near black (M51 is a night map). Paint turns it on.
  readonly baked = signal(false);
  readonly grid = signal(true);
  readonly normals = signal(false);
  readonly snap = signal(false);
  readonly snapMove = signal(1);
  readonly snapRotate = signal(15);
  readonly ortho = signal(false);
  readonly loading = signal(false);
  readonly busy = signal(false);

  // ------------------------------------------------------------------ export to the other game
  readonly exportOpen = signal(false);
  readonly exportPackName = signal('');
  readonly exportModelName = signal('');
  readonly exportDir = signal('');
  readonly exportClipQuery = signal('');
  readonly exportClips = signal<Set<string>>(new Set());
  readonly exportClipNames = signal<string[]>([]);
  /** The game the current model converts to: SOCOM II from SOCOM 3 / CA, SOCOM 3 / CA from SOCOM II (characters,
   *  weapons, vehicles, props). */
  readonly exportTarget = computed(() => {
    const s = this.status(), m = this.current();
    if (!s || !m) { return null; }
    if (s.game) { return ['character', 'weapon', 'prop'].includes(m.kind) ? 'SOCOM II' : null; }
    const skinned = m.kind === 'character' && /^MESH_/i.test(m.name);
    return skinned || ['weapon', 'prop', 'library'].includes(m.kind) ? 'SOCOM 3 / CA' : null;
  });
  readonly exportToCa = computed(() => this.exportTarget() === 'SOCOM 3 / CA');
  readonly exportModelPlaceholder = computed(() => {
    const n = this.current()?.name ?? '';
    return this.exportToCa() ? n.replace(/^MESH_/i, '') : 'ca_' + n.replace(/^clib_/, '');
  });
  readonly shownExportClips = computed(() => {
    const q = this.exportClipQuery().trim().toLowerCase();
    return this.exportClipNames().filter(c => !q || c.toLowerCase().includes(q)).slice(0, 400);
  });
  readonly message = signal('');
  readonly messageIsError = signal(false);
  readonly logLines = signal<Array<{ text: string; error: boolean }>>([]);
  readonly hover = signal<HoverInfo | null>(null);
  readonly selectionVersion = signal(0);
  readonly uvCount = signal(0);
  readonly uvChunks = signal<EditorChunk[]>([]);
  readonly retexTo = signal('');
  readonly targetVisual = signal('');
  readonly targetTexture = signal('');
  readonly primSize = signal(10);
  readonly pendingRot = signal<[number, number, number]>([0, 0, 0]);
  readonly pendingScale = signal<[number, number, number]>([1, 1, 1]);
  readonly brushHex = signal('#ffffff');
  readonly brushIntensity = signal(1);
  readonly brushRadius = signal(10);
  readonly brushStrength = signal(0.5);
  readonly maxRadius = signal(100);

  readonly dirtyCount = computed(() => this.status()?.dirty.length ?? 0);
  readonly kinds = computed(() => KIND_ORDER.filter(k => (this.status()?.models?.[k] ?? 0) > 0));
  readonly groups = computed(() => {
    const q = this.query().toLowerCase();
    const kind = this.kindFilter();
    const list = this.models().filter(m => (!kind || m.kind === kind) && (!q || m.name.toLowerCase().includes(q)));
    return KIND_ORDER.map(k => ({ kind: k, models: list.filter(m => m.kind === k) })).filter(g => g.models.length > 0);
  });
  readonly counts = computed(() => { this.selectionVersion(); return this.viewport?.selectionCounts() ?? { chunks: 0, vertices: 0, faces: 0 }; });
  readonly location = computed<[number, number, number]>(() => {
    this.selectionVersion();
    const c = this.viewport?.selectionCenter();
    return c ? [c.x, c.y, c.z] : [0, 0, 0];
  });
  readonly selectedChunkInfos = computed(() => {
    this.selectionVersion();
    const m = this.mesh();
    return m ? (this.viewport?.selectionChunkIndices() ?? []).map(i => m.chunks[i]).filter(Boolean) : [];
  });
  readonly selectedTextures = computed(() => {
    const names = new Set(this.selectedChunkInfos().map(c => c.texture).filter((t): t is string => !!t));
    const all = this.description()?.textures ?? [];
    return names.size ? all.filter(t => names.has(t.name)) : all;
  });
  readonly visuals = computed(() => {
    this.selectionVersion();
    const m = this.mesh();
    if (!m) { return []; }
    const byVisual = new Map<string, EditorChunk[]>();
    for (const c of m.chunks) {
      const k = c.visual ?? '(no visual)';
      byVisual.set(k, [...(byVisual.get(k) ?? []), c]);
    }
    return [...byVisual].map(([name, chunks]) => ({ name, chunks, hidden: chunks.every(c => this.viewport?.isChunkHidden(c.index)) }));
  });
  readonly meshStats = computed(() => {
    const m = this.mesh();
    if (!m) { return ''; }
    let v = 0, t = 0;
    for (const c of m.chunks) { v += c.vertexCount; t += c.triangles.length / 3; }
    return `${m.chunks.length} chunks · ${v} verts · ${t} tris`;
  });
  readonly canAdd = computed(() => !!this.mesh() && !!this.targetVisual() && !!this.targetTexture().trim());
  readonly pendingActive = computed(() => this.pendingRot().some(v => v !== 0) || this.pendingScale().some(v => v !== 1));

  readonly menus: Menu[] = [
    {
      id: 'file', label: 'File', items: [
        { label: 'Open package…', shortcut: 'Ctrl+O', action: () => this.pickPackage() },
        { separator: true, label: '' },
        { label: 'Save copy…', shortcut: 'Ctrl+S', action: () => void this.saveZdbCopy(), disabled: () => !this.dirtyCount() },
        { label: 'Export memory-card pack…', action: () => void this.exportPack(), disabled: () => !this.dirtyCount() },
        { separator: true, label: '' },
        { label: 'Export model as OBJ…', action: () => void this.exportObj(), disabled: () => !this.mesh() },
      ],
    },
    {
      id: 'edit', label: 'Edit', items: [
        { label: 'Undo', shortcut: 'Ctrl+Z', action: () => void this.undo(), disabled: () => !this.status()?.canUndo },
        { label: 'Redo', shortcut: 'Ctrl+Y', action: () => void this.redo(), disabled: () => !this.status()?.canRedo },
      ],
    },
    {
      id: 'select', label: 'Select', items: [
        { label: 'All', shortcut: 'A', action: () => this.viewport.selectAll() },
        { label: 'None', shortcut: 'Alt+A', action: () => this.viewport.clearSelection() },
        { label: 'Invert', shortcut: 'Ctrl+I', action: () => this.viewport.invertSelection() },
        { separator: true, label: '' },
        ...this.selectModes.map(m => ({ label: `${m.label} mode`, shortcut: m.key, action: () => this.setSelectMode(m.id), checked: () => this.selectMode() === m.id })),
      ],
    },
    {
      id: 'view', label: 'View', items: [
        { label: 'Frame selected', shortcut: 'F', action: () => this.frameSelected() },
        { label: 'Frame all', shortcut: 'Home', action: () => this.viewport.frameAll() },
        { separator: true, label: '' },
        { label: 'Front', shortcut: 'Numpad 1', action: () => this.view('front') },
        { label: 'Back', shortcut: 'Ctrl+Numpad 1', action: () => this.view('back') },
        { label: 'Right', shortcut: 'Numpad 3', action: () => this.view('right') },
        { label: 'Left', shortcut: 'Ctrl+Numpad 3', action: () => this.view('left') },
        { label: 'Top', shortcut: 'Numpad 7', action: () => this.view('top') },
        { label: 'Bottom', shortcut: 'Ctrl+Numpad 7', action: () => this.view('bottom') },
        { label: 'Orthographic', shortcut: 'Numpad 5', action: () => this.toggleOrtho(), checked: () => this.ortho() },
        { label: 'Model faces −Z (turn views around)', shortcut: 'Ctrl+Numpad 9', action: () => this.viewport.toggleFacing(), checked: () => { this.selectionVersion(); return this.viewport?.facesNegZ ?? false; } },
        { separator: true, label: '' },
        { label: 'Hide selected', shortcut: 'H', action: () => this.viewport.hideSelected() },
        { label: 'Isolate selected', shortcut: 'Shift+H', action: () => this.viewport.isolateSelected() },
        { label: 'Show all', shortcut: 'Alt+H', action: () => this.viewport.unhideAll() },
        { separator: true, label: '' },
        { label: 'Back faces', action: () => this.toggleBackfaces(), checked: () => this.backfaces },
        { label: 'Asset browser', action: () => this.showAssets.set(!this.showAssets()), checked: () => this.showAssets() },
        { label: 'Bottom panel', action: () => this.showBottom.set(!this.showBottom()), checked: () => this.showBottom() },
      ],
    },
    {
      id: 'add', label: 'Add', items: [
        { label: 'Plane', action: () => void this.addPrimitive('plane'), disabled: () => !this.canAdd() },
        { label: 'Cube', action: () => void this.addPrimitive('cube'), disabled: () => !this.canAdd() },
        { separator: true, label: '' },
        { label: 'Import OBJ…', action: () => void this.importObj(), disabled: () => !this.canAdd() },
      ],
    },
    {
      id: 'mesh', label: 'Mesh', items: [
        { label: 'Duplicate', shortcut: 'Shift+D', action: () => void this.duplicateSelection(), disabled: () => !this.counts().vertices },
        { label: 'Delete faces', shortcut: 'X / Delete', action: () => void this.deleteSelection(), disabled: () => !this.counts().vertices },
        { separator: true, label: '' },
        { label: 'Mirror X', action: () => void this.mirror(0), disabled: () => !this.counts().vertices },
        { label: 'Mirror Y', action: () => void this.mirror(1), disabled: () => !this.counts().vertices },
        { label: 'Mirror Z', action: () => void this.mirror(2), disabled: () => !this.counts().vertices },
        { separator: true, label: '' },
        { label: 'Flip normals', action: () => void this.flipNormals(), disabled: () => !this.counts().vertices },
        { label: 'Snap vertices to grid', action: () => void this.snapToGrid(), disabled: () => !this.counts().vertices },
        { label: 'Centre on origin', action: () => void this.centreOnOrigin(), disabled: () => !this.mesh() },
        { separator: true, label: '' },
        { label: 'Reset vertex colours', action: () => void this.resetColors(), disabled: () => !this.counts().vertices },
      ],
    },
    {
      id: 'help', label: 'Help', items: [
        { label: 'Orbit: middle drag or Alt+left drag · Pan: right drag · Zoom: wheel' },
        { label: 'Select: click, Shift adds, Ctrl removes, drag for box' },
        { label: 'Tools: Q select · G move · R rotate · S scale · P paint' },
        { label: 'Geometry: Shift+D duplicate · X / Delete delete faces · Add menu for planes, cubes, OBJ' },
        { label: 'Modes: 1 object · 2 visual · 3 chunk · 4 vertex · 5 face' },
      ],
    },
  ];
  private backfaces = true;

  constructor() {
    window.addEventListener('message', this.onMessage);
    window.addEventListener('keydown', this.onKey);
  }

  ngAfterViewInit(): void {
    this.viewport = new EditorViewport(this.canvasRef.nativeElement, {
      onSelectionChange: () => { this.selectionVersion.update(v => v + 1); this.refreshUv(); this.followSelection(); },
      onHover: info => this.hover.set(info),
      onTransformCommit: (delta, verts, kind) => void this.commitTransform(delta, verts, kind),
      onPaintCommit: colors => void this.commitPaint(colors),
      onCameraChange: () => undefined,
    });
    this.viewport.selectMode = this.selectMode();
    this.viewport.setLighting(this.baked());
    this.uv = new UvEditor(this.uvCanvasRef.nativeElement, {
      onCommit: edits => void this.commitUv(edits),
      onSelectionChange: n => this.uvCount.set(n),
    });
    this.applySnap();
    this.api.postMessage({ type: 'ready' });
  }

  ngOnDestroy(): void {
    window.removeEventListener('message', this.onMessage);
    window.removeEventListener('keydown', this.onKey);
    if (this.pollTimer) { clearInterval(this.pollTimer); }
    this.viewport?.dispose();
    this.uv?.dispose();
  }

  // ------------------------------------------------------------------ messaging

  private readonly onMessage = (event: MessageEvent): void => {
    const msg = event.data as Record<string, unknown>;
    if (msg?.['type'] === 'openPackage' && typeof msg['path'] === 'string') {
      void this.openPackage(msg['path'], msg['model'] as { archive: string; name: string } | undefined);
    } else if (typeof msg?.['id'] === 'number' && ['imagePicked', 'pathPicked', 'saved', 'textPicked'].includes(msg['type'] as string)) {
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
    this.logLines.update(l => [...l.slice(-199), { text, error }]);
    this.api.postMessage({ type: 'log', text });
  }

  // ------------------------------------------------------------------ menus & keys

  toggleMenu(id: string): void { this.openMenu.set(this.openMenu() === id ? null : id); }
  hoverMenu(id: string): void { if (this.openMenu()) { this.openMenu.set(id); } }
  runMenu(item: MenuItem): void { this.openMenu.set(null); item.action?.(); }
  closeMenusOutside(e: Event): void {
    if (!(e.target as HTMLElement).closest('.me-menu')) { this.openMenu.set(null); }
  }

  private readonly onKey = (e: KeyboardEvent): void => {
    const target = e.target as HTMLElement;
    if (target && ['INPUT', 'SELECT', 'TEXTAREA'].includes(target.tagName)) { return; }
    const ctrl = e.ctrlKey || e.metaKey;
    const k = e.key.toLowerCase();
    const numpad = e.code.startsWith('Numpad');
    let handled = true;
    if (ctrl && k === 'z') { void this.undo(); }
    else if (ctrl && (k === 'y' || (e.shiftKey && k === 'z'))) { void this.redo(); }
    else if (ctrl && k === 's') { void this.saveZdbCopy(); }
    else if (ctrl && k === 'o') { this.pickPackage(); }
    else if (ctrl && k === 'i') { this.viewport.invertSelection(); }
    else if (numpad && e.code === 'Numpad1') { this.view(ctrl ? 'back' : 'front'); }
    else if (numpad && e.code === 'Numpad3') { this.view(ctrl ? 'left' : 'right'); }
    else if (numpad && e.code === 'Numpad7') { this.view(ctrl ? 'bottom' : 'top'); }
    else if (numpad && e.code === 'Numpad5') { this.toggleOrtho(); }
    else if (numpad && e.code === 'Numpad9' && ctrl) { this.viewport.toggleFacing(); }
    else if (numpad && e.code === 'NumpadDecimal') { this.frameSelected(); }
    else if (ctrl) { handled = false; }
    else if (k === 'd' && e.shiftKey) { void this.duplicateSelection(); }
    else if (k === 'x' || e.key === 'Delete') { void this.deleteSelection(); }
    else if (k === 'a' && e.altKey) { this.viewport.clearSelection(); }
    else if (k === 'a') { this.viewport.selectAll(); }
    else if (k === 'h' && e.altKey) { this.viewport.unhideAll(); }
    else if (k === 'h' && e.shiftKey) { this.viewport.isolateSelected(); }
    else if (k === 'h') { this.viewport.hideSelected(); }
    else if (k === 'f') { this.frameSelected(); }
    else if (e.key === 'Home') { this.viewport.frameAll(); }
    else if (k === 'q') { this.setTool('select'); }
    else if (k === 'g' || k === 'w') { this.setTool('move'); }
    else if (k === 'r' || k === 'e') { this.setTool('rotate'); }
    else if (k === 's') { this.setTool('scale'); }
    else if (k === 'p') { this.setTool('paint'); }
    else if (k === 'z') { this.cycleShading(); }
    else if (['1', '2', '3', '4', '5'].includes(e.key) && !numpad) { this.setSelectMode(this.selectModes[+e.key - 1].id); }
    else if (e.key === 'Escape') { this.openMenu.set(null); this.setTool('select'); }
    else { handled = false; }
    if (handled) { e.preventDefault(); }
  };

  // ------------------------------------------------------------------ view

  setSelectMode(mode: SelectMode): void { this.selectMode.set(mode); this.viewport.setSelectMode(mode); }
  setTool(tool: Tool): void {
    this.tool.set(tool);
    this.viewport.tool = tool;
    if (tool === 'paint') {
      this.propTab.set('paint');
      // Painting edits the baked lighting, so show it.
      if (!this.baked()) { this.setBaked(true); }
    }
  }
  setShading(s: Shading): void { this.shading.set(s); this.viewport.shading = s; }
  cycleShading(): void {
    const order: Shading[] = ['textured', 'solid', 'wireframe', 'xray'];
    this.setShading(order[(order.indexOf(this.shading()) + 1) % order.length]);
  }
  setBaked(on: boolean): void { this.baked.set(on); this.viewport.setLighting(on); }
  toggleBackfaces(): void { this.backfaces = !this.backfaces; this.viewport.setBackfaces(this.backfaces); }
  applyHelpers(): void { this.viewport.setHelpers(this.grid(), true, this.normals()); }
  applySnap(): void { this.viewport?.setSnap(this.snap() ? this.snapMove() : null, this.snap() ? this.snapRotate() : null, this.snap() ? 0.1 : null); }
  view(p: ViewPreset): void { this.viewport.setView(p); }
  toggleOrtho(): void { this.viewport.toggleOrtho(); this.ortho.set(this.viewport.isOrtho); }
  frameSelected(): void { this.viewport.frameSelected(); }
  viewLabel(): string { return this.ortho() ? 'Orthographic' : 'Perspective'; }
  selectModeLabel(): string { return this.selectModes.find(m => m.id === this.selectMode())?.label ?? ''; }
  toolLabel(): string { return this.tools.find(t => t.id === this.tool())?.label.split(' (')[0] ?? ''; }
  hint(): string {
    switch (this.tool()) {
      case 'move': case 'rotate': case 'scale': return 'Drag the gizmo handles; snapping in the header. Middle drag orbits, right drag pans.';
      case 'paint': return 'Drag over the model to paint baked vertex colours; brush settings in Paint.';
      default: return 'Click to select (Shift add, Ctrl remove), drag for box select. G / R / S transform, F frame, H hide.';
    }
  }

  // ------------------------------------------------------------------ outliner

  isHidden(i: number): boolean { this.selectionVersion(); return this.viewport?.isChunkHidden(i) ?? false; }
  chunkSelected(i: number): boolean { this.selectionVersion(); return this.viewport?.selectionChunkIndices().includes(i) ?? false; }
  visualSelected(name: string): boolean {
    this.selectionVersion();
    const m = this.mesh();
    const sel = new Set(this.viewport?.selectionChunkIndices() ?? []);
    return !!m && m.chunks.filter(c => (c.visual ?? '(no visual)') === name).every(c => sel.has(c.index));
  }
  toggleChunkHidden(i: number): void { this.viewport.setChunkHidden(i, !this.viewport.isChunkHidden(i)); }
  toggleVisualHidden(name: string): void {
    const chunks = this.mesh()?.chunks.filter(c => (c.visual ?? '(no visual)') === name) ?? [];
    const hide = !chunks.every(c => this.viewport.isChunkHidden(c.index));
    for (const c of chunks) { this.viewport.setChunkHidden(c.index, hide); }
  }
  selectChunk(i: number, e: MouseEvent): void {
    if (this.selectMode() !== 'chunk') { this.setSelectMode('chunk'); }
    this.viewport.selectChunks([i], e.shiftKey);
  }
  selectVisual(name: string, e: MouseEvent): void {
    if (this.selectMode() !== 'chunk' && this.selectMode() !== 'visual') { this.setSelectMode('visual'); }
    this.viewport.selectChunks(this.mesh()?.chunks.filter(c => (c.visual ?? '(no visual)') === name).map(c => c.index) ?? [], e.shiftKey);
  }
  selectAllChunks(): void { this.viewport.selectChunks(this.mesh()?.chunks.map(c => c.index) ?? []); }

  // ------------------------------------------------------------------ session

  pickPackage(): void { this.api.postMessage({ type: 'pickPackage' }); }
  kindLabel(kind: string): string { return KIND_LABEL[kind] ?? kind; }
  isCurrent(m: ModelSummary): boolean { const c = this.current(); return !!c && c.archive === m.archive && c.name === m.name; }
  fileName(p: string): string { return p.split(/[\\/]/).pop() ?? p; }
  fmt(v: number): string { return (Math.abs(v) < 1e-4 ? 0 : v).toFixed(2); }
  fmtVec(v: ArrayLike<number>): string { return Array.from(v).map(x => (Math.abs(x) < 1e-4 ? 0 : x).toFixed(1)).join(', '); }

  texSummary(t: TextureInfo): string {
    if (!t.found) { return 'not in the package'; }
    const parts = [`${t.width}×${t.height}`, `${t.bpp}-bit`, t.namespace ?? ''];
    if (t.palette !== null && t.palette !== undefined) { parts.push(`texpal_${t.palette}`); }
    if (t.hasTransparency) { parts.push(t.alphaTest ? `cutout ≥${t.alphaTest.toFixed(2)}` : 'translucent'); }
    return parts.filter(Boolean).join(' · ');
  }

  async openExport(): Promise<void> {
    this.exportOpen.set(true);
    if (this.current()?.kind === 'character' && !this.exportClipNames().length) {
      try {
        const list = await this.client.listAnimations();
        // Named clips first (clip_<hash> ones have no recovered name).
        const names = list.clips.map(c => c.name);
        this.exportClipNames.set([...names.filter(n => !n.startsWith('clip_')), ...names.filter(n => n.startsWith('clip_'))]);
      } catch { this.exportClipNames.set([]); }
    }
  }

  toggleExportClip(name: string): void {
    const next = new Set(this.exportClips());
    if (next.has(name)) { next.delete(name); } else { next.add(name); }
    this.exportClips.set(next);
  }

  selectShownClips(on: boolean): void {
    const next = new Set(this.exportClips());
    for (const c of this.shownExportClips()) { if (on) { next.add(c); } else { next.delete(c); } }
    if (!on && !this.exportClipQuery().trim()) { next.clear(); }
    this.exportClips.set(next);
  }

  /** Converts the current model, then opens the new package with that model selected. */
  async runExport(): Promise<void> {
    const m = this.current();
    if (!m) { return; }
    this.busy.set(true);
    this.say(`Exporting ${m.name} to ${this.exportTarget()}…`);
    let result: ExportResult;
    try {
      result = await this.client.exportModel(m.archive, m.name, {
        pack: this.exportPackName().trim() || undefined,
        modelName: this.exportModelName().trim() || undefined,
        outputDir: this.exportDir().trim() || undefined,
        clips: m.kind === 'character' ? [...this.exportClips()] : undefined,
      });
    } catch (err) {
      this.say(`Export failed: ${errText(err)}`, true);
      this.busy.set(false);
      return;
    }
    this.busy.set(false);
    this.exportOpen.set(false);
    const extra = result.clips.length ? `, ${result.clips.length} clips` : '';
    this.say(`${result.target} pack ${result.pack}: ${result.model} (${result.kind}, ${result.triangles} tris, ${result.textures} textures${extra}) -> ${result.path}`
      + (result.warnings.length ? ` - ${result.warnings.join('; ')}` : ''));
    await this.openPackage(result.path, result.open ?? undefined);
  }

  private async openPackage(path: string, model?: { archive: string; name: string }): Promise<void> {
    this.busy.set(true);
    this.say(`Opening ${path}…`);
    try {
      const status = await this.client.open(path);
      this.status.set(status);
      this.textures.invalidate(status.revision);
      this.models.set(await this.client.listModels());
      this.current.set(null);
      this.description.set(null);
      this.mesh.set(null);
      this.viewport.setMesh(null, {});
      this.say(`Opened ${status.path}: ${this.models().length} models (session ${status.session}).`);
      const first = model
        ? this.models().find(m => m.archive.toLowerCase() === model.archive.toLowerCase() && m.name === model.name)
        : undefined;
      if (first) { await this.select(first); }
      if (!this.pollTimer) { this.pollTimer = setInterval(() => void this.poll(), 2000); }
    } catch (err) {
      this.say(`Could not open ${path}: ${errText(err)}`, true);
    } finally {
      this.busy.set(false);
    }
  }

  // Edits made by anyone on the session (an agent with assets_edit) show up here.
  private async poll(): Promise<void> {
    if (!this.client.session || this.busy() || this.loading()) { return; }
    try {
      const status = await this.client.status();
      const before = this.status()?.revision;
      this.status.set(status);
      if (before !== undefined && status.revision !== before) {
        this.textures.invalidate(status.revision);
        this.models.set(await this.client.listModels());
        await this.reload(true);
        this.say(`Session changed (revision ${status.revision}${status.canUndo ? `: ${status.canUndo}` : ''}).`);
      }
    } catch { /* retried next poll */ }
  }

  async select(m: ModelSummary): Promise<void> {
    this.current.set(m);
    this.viewport.clearSelection();
    this.viewport.unhideAll();
    await this.reload(false);
  }

  private async reload(keepCamera: boolean): Promise<void> {
    const m = this.current();
    if (!m) { return; }
    this.loading.set(!keepCamera);
    try {
      const [desc, wire] = await Promise.all([
        this.client.describe(m.archive, m.name),
        this.client.mesh(m.archive, m.name) as Promise<MeshWire>,
      ]);
      if (this.current() !== m) { return; }
      const mesh = decodeMesh(wire);
      const pixels: Record<string, ModelTexturePixels> = {};
      const missing: string[] = [];
      const names = [...new Set(mesh.chunks.map(c => c.texture).filter((t): t is string => !!t))];
      await Promise.all(names.map(async n => {
        const p = await this.textures.get(n, desc.textureNamespace);
        if (p) { pixels[n.toLowerCase()] = p; } else { missing.push(n); }
      }));
      if (this.current() !== m) { return; }
      this.texturePixels = pixels;
      this.description.set(desc);
      this.mesh.set(mesh);
      this.missingTextures.set(missing.sort());
      this.viewport.setMesh(mesh, pixels, keepCamera);
      const ext = Math.max(...[0, 1, 2].map(k => {
        let lo = Infinity, hi = -Infinity;
        for (const c of mesh.chunks) { for (let i = k; i < c.positions.length; i += 3) { lo = Math.min(lo, c.positions[i]); hi = Math.max(hi, c.positions[i]); } }
        return hi - lo;
      }));
      this.maxRadius.set(Math.max(5, Math.round(ext / 2)));
      if (!keepCamera) {
        this.setBrush('radius', String(Math.max(1, Math.round(ext / 20))));
        // New primitives default to about a quarter of the model (1-2-5 steps).
        const q = ext / 4, p10 = Math.pow(10, Math.floor(Math.log10(Math.max(q, 1e-3))));
        this.primSize.set([1, 2, 5, 10].map(f => f * p10).find(v => v >= q * 0.75) ?? 10 * p10);
      }
      this.selectionVersion.update(v => v + 1);
      this.refreshUv();
      if (!mesh.chunks.some(c => (c.visual ?? '') === this.targetVisual())) { this.targetVisual.set(mesh.chunks[0]?.visual ?? ''); }
      if (!this.targetTexture()) { this.targetTexture.set(mesh.chunks[0]?.texture ?? ''); }
      void this.loadThumbs(desc);
    } catch (err) {
      this.mesh.set(null);
      this.viewport.setMesh(null, {});
      this.say(`${m.name}: ${errText(err)}`, true);
    } finally {
      this.loading.set(false);
    }
  }

  private async loadThumbs(desc: ModelDescription): Promise<void> {
    const out: Record<string, string> = {};
    for (const t of desc.textures.filter(x => x.found)) {
      try { out[t.name] = 'data:image/png;base64,' + (await this.client.texture(t.name, t.namespace)).png; } catch { /* skip */ }
    }
    if (this.description() === desc) { this.thumbs.set(out); }
  }

  // ------------------------------------------------------------------ edits (all through the bridge)

  private async edit(label: string, op: Record<string, unknown>): Promise<boolean> {
    const m = this.current();
    if (!m) { return false; }
    this.busy.set(true);
    try {
      const raw = await this.client.edit({ archive: m.archive, model: m.name, label, ...op }) as unknown as { result?: { revision: number; description?: string }; newChunks?: number[]; revision?: number; description?: string };
      const result = raw.result ?? (raw as { revision: number; description?: string });
      this.textures.invalidate(result.revision);
      this.status.set(await this.client.status());
      this.say(`${result.description ?? label} (revision ${result.revision})`);
      await this.reload(true);
      if (raw.newChunks?.length) {
        // New geometry comes back selected with the Move tool, ready to place.
        this.setSelectMode('chunk');
        this.viewport.selectChunks(raw.newChunks);
        this.setTool('move');
      }
      return true;
    } catch (err) {
      this.say(`${label}: ${errText(err)}`, true);
      await this.reload(true);
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  private geometryOffsets(verts: VertexSelection): Record<string, number[]> {
    const m = this.mesh()!;
    const out: Record<string, number[]> = {};
    for (const [ci, set] of verts) {
      const key = String(m.chunks[ci].geometryOffset);
      out[key] = [...new Set([...(out[key] ?? []), ...set])];
    }
    return out;
  }

  private commitTransform(delta: number[], verts: VertexSelection, kind: 'move' | 'rotate' | 'scale'): Promise<boolean> {
    let n = 0;
    for (const s of verts.values()) { n += s.size; }
    const label = `${kind === 'move' ? 'Move' : kind === 'rotate' ? 'Rotate' : 'Scale'} ${n} vertex(es)`;
    return this.edit(label, { op: 'transformVertices', matrix: toModelMatrix(delta), vertices: this.geometryOffsets(verts) });
  }

  private commitPaint(colors: Map<number, Map<number, [number, number, number, number]>>): Promise<boolean> {
    const m = this.mesh()!;
    const chunks = [...colors].map(([ci, map]) => ({
      geometryOffset: m.chunks[ci].geometryOffset,
      vertices: [...map].map(([i, c]) => ({ i, color: c.map(x => Math.round(x)) })),
    }));
    return this.edit('Paint vertex colours', { op: 'setVertices', chunks });
  }

  private commitUv(edits: UvEdits): Promise<boolean> {
    const m = this.mesh()!;
    const chunks = [...edits].map(([ci, map]) => ({
      geometryOffset: m.chunks[ci].geometryOffset,
      vertices: [...map].map(([i, uv]) => ({ i, uv })),
    }));
    return this.edit('Edit UVs', { op: 'setVertices', chunks });
  }

  // Numeric transform panel.
  setLocation(axis: number, value: string): void {
    const v = Number(value);
    const c = this.viewport.selectionCenter();
    if (!c || !Number.isFinite(v)) { return; }
    const d = [0, 0, 0];
    d[axis] = v - [c.x, c.y, c.z][axis];
    void this.commitTransform(new THREE.Matrix4().makeTranslation(d[0], d[1], d[2]).toArray(), this.viewport.selectionVertexMap(), 'move');
  }

  setPending(kind: 'rot' | 'scale', axis: number, value: string): void {
    const v = Number(value);
    if (!Number.isFinite(v)) { return; }
    const sig = kind === 'rot' ? this.pendingRot : this.pendingScale;
    const next = [...sig()] as [number, number, number];
    next[axis] = v;
    sig.set(next);
    this.viewport.previewDelta(this.pendingMatrix());
  }

  private pendingMatrix(): THREE.Matrix4 | null {
    const c = this.viewport.selectionCenter();
    if (!c || !this.pendingActive()) { return null; }
    const r = this.pendingRot().map(THREE.MathUtils.degToRad);
    const s = this.pendingScale();
    return new THREE.Matrix4().makeTranslation(c.x, c.y, c.z)
      .multiply(new THREE.Matrix4().makeRotationFromEuler(new THREE.Euler(r[0], r[1], r[2], 'XYZ')))
      .multiply(new THREE.Matrix4().makeScale(s[0], s[1], s[2]))
      .multiply(new THREE.Matrix4().makeTranslation(-c.x, -c.y, -c.z));
  }

  async applyPending(): Promise<void> {
    const m = this.pendingMatrix();
    if (!m) { return; }
    const verts = this.viewport.selectionVertexMap();
    const kind = this.pendingScale().some(v => v !== 1) ? 'scale' : 'rotate';
    this.resetPending();
    await this.commitTransform(m.toArray(), verts, kind);
  }

  resetPending(): void {
    this.pendingRot.set([0, 0, 0]);
    this.pendingScale.set([1, 1, 1]);
    this.viewport.previewDelta(null);
  }

  // Mesh menu.
  mirror(axis: number): Promise<boolean> {
    const c = this.viewport.selectionCenter();
    if (!c) { return Promise.resolve(false); }
    const s = [1, 1, 1];
    s[axis] = -1;
    const m = new THREE.Matrix4().makeTranslation(c.x, c.y, c.z).multiply(new THREE.Matrix4().makeScale(s[0], s[1], s[2])).multiply(new THREE.Matrix4().makeTranslation(-c.x, -c.y, -c.z));
    return this.commitTransform(m.toArray(), this.viewport.selectionVertexMap(), 'scale');
  }

  flipNormals(): Promise<boolean> {
    const m = this.mesh()!;
    const chunks = [...this.viewport.selectionVertexMap()].map(([ci, set]) => {
      const c = m.chunks[ci];
      return {
        geometryOffset: c.geometryOffset,
        // Display normals are Y-flipped: negate in model space.
        vertices: [...set].map(i => ({ i, normal: toModelPoint([-c.normals[i * 3], -c.normals[i * 3 + 1], -c.normals[i * 3 + 2]]) })),
      };
    });
    return this.edit('Flip normals', { op: 'setVertices', chunks });
  }

  snapToGrid(): Promise<boolean> {
    const m = this.mesh()!;
    const step = this.snapMove() || 1;
    const chunks = [...this.viewport.selectionVertexMap()].map(([ci, set]) => {
      const c = m.chunks[ci];
      return {
        geometryOffset: c.geometryOffset,
        vertices: [...set].map(i => ({ i, position: toModelPoint([0, 1, 2].map(k => Math.round(c.positions[i * 3 + k] / step) * step)) })),
      };
    });
    return this.edit(`Snap vertices to ${step}`, { op: 'setVertices', chunks });
  }

  centreOnOrigin(): Promise<boolean> {
    this.viewport.setSelectMode('object');
    this.selectMode.set('object');
    const c = this.viewport.selectionCenter();
    if (!c) { return Promise.resolve(false); }
    return this.commitTransform(new THREE.Matrix4().makeTranslation(-c.x, -c.y, -c.z).toArray(), this.viewport.selectionVertexMap(), 'move');
  }

  // Paint panel.
  setBrushColor(hex: string): void {
    this.brushHex.set(hex);
    const n = parseInt(hex.slice(1), 16);
    this.viewport.brushColor = [(n >> 16) & 255, (n >> 8) & 255, n & 255];
  }

  setBrush(kind: 'intensity' | 'radius' | 'strength', value: string): void {
    const v = Number(value);
    if (!Number.isFinite(v)) { return; }
    if (kind === 'intensity') { this.brushIntensity.set(v); this.viewport.brushIntensity = v; }
    if (kind === 'radius') { this.brushRadius.set(v); this.viewport.brushRadius = v; }
    if (kind === 'strength') { this.brushStrength.set(v); this.viewport.brushStrength = v; }
  }

  private colorEdits(color: (c: EditorChunk, i: number) => number[]): Array<{ geometryOffset: number; vertices: Array<{ i: number; color: number[] }> }> {
    const m = this.mesh()!;
    return [...this.viewport.selectionVertexMap()]
      .filter(([ci]) => !!m.chunks[ci].colors)
      .map(([ci, set]) => ({ geometryOffset: m.chunks[ci].geometryOffset, vertices: [...set].map(i => ({ i, color: color(m.chunks[ci], i) })) }));
  }

  fillSelection(): Promise<boolean> {
    const [r, g, b] = this.viewport.brushColor.map(x => Math.round(x / 255 * 128 * this.brushIntensity()));
    return this.edit('Fill vertex colours', { op: 'setVertices', chunks: this.colorEdits((c, i) => [r, g, b, c.colors![i * 4 + 3]]) });
  }

  resetColors(): Promise<boolean> {
    return this.edit('Reset vertex colours', { op: 'setVertices', chunks: this.colorEdits((c, i) => [128, 128, 128, c.colors![i * 4 + 3]]) });
  }

  // Material / texture panels.
  retexture(): Promise<boolean> {
    const from = [...new Set(this.selectedChunkInfos().map(c => c.texture).filter((t): t is string => !!t))];
    if (!from.length) { return Promise.resolve(false); }
    const to = this.retexTo().trim();
    return (async () => {
      let ok = true;
      for (const f of from) { ok = await this.edit(`Retexture ${f} → ${to}`, { op: 'retexture', from: f, to }) && ok; }
      return ok;
    })();
  }

  async replaceTexture(t: TextureInfo): Promise<void> {
    const picked = await this.ask('pickImage');
    if (typeof picked['base64'] !== 'string') { return; }
    await this.edit(`Replace ${t.name}`, { op: 'replaceTexture', texture: t.name, namespace: t.namespace, pngBase64: picked['base64'] });
  }

  async exportTexture(t: TextureInfo): Promise<void> {
    const { png } = await this.client.texture(t.name, t.namespace);
    this.api.postMessage({ type: 'saveFile', id: this.nextRequest++, suggested: t.name.replace(/\.tif$/i, '.png'), base64: png, filters: { 'PNG image': ['png'] } });
  }

  // ------------------------------------------------------------------ geometry (topology edits)

  // The selection as triangles per chunk geometry offset (shared geometry once).
  private selectedFaces(): Record<string, number[]> {
    const m = this.mesh()!;
    const out: Record<string, Set<number>> = {};
    const add = (ci: number, tri: number) => { const k = String(m.chunks[ci].geometryOffset); (out[k] ??= new Set()).add(tri); };
    const mode = this.selectMode();
    if (mode === 'face') {
      for (const [ci, set] of this.viewport.selectedFaces) { for (const f of set) { add(ci, f); } }
    } else if (mode === 'vertex') {
      for (const [ci, set] of this.viewport.selectedVertices) {
        const t = m.chunks[ci].triangles;
        for (let f = 0; f < t.length / 3; f++) { if (set.has(t[f * 3]) && set.has(t[f * 3 + 1]) && set.has(t[f * 3 + 2])) { add(ci, f); } }
      }
    } else {
      for (const ci of this.viewport.selectionChunkIndices()) {
        for (let f = 0; f < m.chunks[ci].triangles.length / 3; f++) { add(ci, f); }
      }
    }
    return Object.fromEntries(Object.entries(out).map(([k, v]) => [k, [...v]]));
  }

  async deleteSelection(): Promise<void> {
    const faces = this.selectedFaces();
    const n = Object.values(faces).reduce((a, f) => a + f.length, 0);
    if (!n) { this.say('Nothing to delete: select faces (vertex mode: all three vertices of a face).', true); return; }
    if (await this.edit(`Delete ${n} face(s)`, { op: 'deleteFaces', faces })) { this.viewport.clearSelection(); }
  }

  async duplicateSelection(): Promise<void> {
    const faces = this.selectedFaces();
    const n = Object.values(faces).reduce((a, f) => a + f.length, 0);
    if (!n) { this.say('Nothing to duplicate: select faces or chunks.', true); return; }
    await this.edit(`Duplicate ${n} face(s)`, { op: 'duplicateFaces', faces, translate: [0, 0, 0] });
  }

  private placementPoint(): number[] {
    const c = this.viewport.selectionCenter();
    return toModelPoint(c ? [c.x, c.y, c.z] : [0, 0, 0]);
  }

  async addPrimitive(kind: 'plane' | 'cube'): Promise<void> {
    if (!this.canAdd()) { return; }
    await this.edit(`Add ${kind}`, {
      op: 'addMesh', visual: this.targetVisual(), texture: this.targetTexture().trim(),
      primitive: { kind, size: this.primSize(), center: this.placementPoint() },
    });
  }

  async importObj(): Promise<void> {
    if (!this.canAdd()) { return; }
    const picked = await this.ask('pickText', { filters: { 'Wavefront OBJ': ['obj'] } });
    if (typeof picked['text'] !== 'string') { return; }
    await this.edit(`Import ${picked['name'] ?? 'OBJ'}`, {
      op: 'addMesh', visual: this.targetVisual(), texture: this.targetTexture().trim(),
      obj: picked['text'], yUp: true, offset: this.placementPoint(),
    });
  }

  // The geometry targets follow the selection (its visual and texture).
  private followSelection(): void {
    const m = this.mesh();
    const first = m && this.viewport?.selectionChunkIndices()[0];
    if (!m || first === undefined || first === null) { return; }
    const c = m.chunks[first];
    if (c?.visual) { this.targetVisual.set(c.visual); }
    if (c?.texture) { this.targetTexture.set(c.texture); }
  }

  // ------------------------------------------------------------------ UV editor

  refreshUv(): void {
    if (!this.uv) { return; }
    const m = this.mesh();
    const sel = this.viewport?.selectionVertexMap() ?? new Map<number, Set<number>>();
    const chunks = m ? [...sel.keys()].map(i => m.chunks[i]).filter(Boolean) : [];
    this.uvChunks.set(chunks);
    const tex = chunks.find(c => c.texture)?.texture?.toLowerCase();
    this.uv.setTexture(tex ? this.texturePixels[tex] ?? null : null);
    // Vertex / face selections start selected in the UV editor; whole chunks start unselected.
    const start = this.selectMode() === 'vertex' || this.selectMode() === 'face' ? sel : new Map<number, Set<number>>();
    this.uv.setChunks(chunks, start);
  }

  uvOp(op: 'flipU' | 'flipV' | 'rot90'): void {
    const c = this.uv.selectionCenter();
    if (!c) { return; }
    const [cu, cv] = c;
    this.uv.transformSelected((u, v) => op === 'flipU' ? [2 * cu - u, v] : op === 'flipV' ? [u, 2 * cv - v] : [cu - (v - cv), cv + (u - cu)]);
  }

  uvSnap(on: boolean): void { this.uv.snapToPixels = on; }
  uvFrame(): void { this.uv.frame(); }

  // ------------------------------------------------------------------ undo / save / export

  async undo(): Promise<void> { await this.history(true); }
  async redo(): Promise<void> { await this.history(false); }

  private async history(undo: boolean): Promise<void> {
    if (!this.client.session) { return; }
    this.busy.set(true);
    try {
      const r = undo ? await this.client.undo() : await this.client.redo();
      this.textures.invalidate(r.revision);
      this.status.set(await this.client.status());
      this.say(`${undo ? 'Undo' : 'Redo'} (revision ${r.revision})`);
      await this.reload(true);
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
    const picked = await this.ask('pickSavePath', { kind: 'pack', suggested: 'MEDIT' });
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

  async exportObj(): Promise<void> {
    const m = this.mesh();
    if (!m) { return; }
    const lines = [`# ${m.archive}:${m.name} exported by the CodeDesigner Model Editor (Y up)`, `mtllib ${m.name}.mtl`];
    let base = 1;
    for (const c of m.chunks) {
      lines.push(`o chunk_${c.index}_${c.visual ?? ''}`, `usemtl ${c.texture ?? 'none'}`);
      for (let i = 0; i < c.vertexCount; i++) { lines.push(`v ${c.positions[i * 3]} ${c.positions[i * 3 + 1]} ${c.positions[i * 3 + 2]}`); }
      for (let i = 0; i < c.vertexCount; i++) { lines.push(`vt ${c.uvs[i * 2]} ${c.uvs[i * 2 + 1]}`); }
      for (let i = 0; i < c.vertexCount; i++) { lines.push(`vn ${c.normals[i * 3]} ${c.normals[i * 3 + 1]} ${c.normals[i * 3 + 2]}`); }
      for (let f = 0; f < c.triangles.length; f += 3) {
        const [a, b, d] = [c.triangles[f] + base, c.triangles[f + 1] + base, c.triangles[f + 2] + base];
        lines.push(`f ${a}/${a}/${a} ${b}/${b}/${b} ${d}/${d}/${d}`);
      }
      base += c.vertexCount;
    }
    this.api.postMessage({ type: 'saveFile', id: this.nextRequest++, suggested: `${m.name}.obj`, text: lines.join('\n'), filters: { 'Wavefront OBJ': ['obj'] } });
  }
}

function errText(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}
