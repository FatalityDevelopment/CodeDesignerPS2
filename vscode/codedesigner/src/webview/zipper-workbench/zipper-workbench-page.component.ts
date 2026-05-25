import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  ViewChild,
  ViewEncapsulation,
  computed,
  effect,
  signal,
} from '@angular/core';
import { IconComponent } from '../shared/icon.component';
import { getVsCodeApi } from '../shared/vscode-api';
import {
  aliasPalettesForTextureEntries,
  base64ToBytes,
  parsePaletteEntries,
  parsePaletteNodes,
  parseTexture,
  ZedPalette,
  ZedTexture,
} from './zed-texture-parser';
import { ZedModel, applyMarkerBinding, decodeFallbackPayload, looksLikeZedContainer, parseModel } from './zed-model-parser';
import { InspectedZedArchive, parseZedArchive } from './zed-archive-parser';
import { ZedModelRenderer } from './zed-model-renderer';
import {
  GeoEntry,
  GeoScene,
  collectGeoInstances,
  collectVisualPartNames,
  compactName,
  findSceneNode,
  instanceNameVariants,
  parseGeoScene,
  transformPoint,
} from './zed-geo-parser';
import './zipper-workbench-page.css';

interface ZipperEntryInfo {
  index: number;
  name: string;
  path: string;
  size: number;
  kind: string;
  extension?: string;
  type?: number;
}

interface ZipperSaveRecordInfo {
  fieldName: string;
  type: number;
  flags: number;
  count: number;
  valOffset: number;
  tableOffset: number;
  strOffset: number;
}

interface ZipperInspectResult {
  format: string;
  role: string;
  path: string;
  fileName: string;
  size: number;
  summary: string;
  canExtract: boolean;
  entries: ZipperEntryInfo[];
  apache?: { hasFtScore: boolean; hasZSealEtc: boolean } | null;
  zed?: {
    rootValue: number;
    stringBasePointer: number;
    headerWord5C: number;
    dataAlignment: number;
  } | null;
  save?: {
    title: string;
    decryptedSize: number;
    header: {
      saveType: number;
      slotIndex: number;
      dataSize: number;
      moduleCount: number;
      field5C: number;
      field60: number;
    };
    modules: string[];
    records: ZipperSaveRecordInfo[];
  } | null;
}

interface ZipperOperationResult {
  outputDirectory?: string;
  outputPath?: string;
  writtenFiles?: string[];
  count?: number;
  section?: string;
  buildVersion?: string;
  publicModulusHex?: string;
  generatedNewKey?: boolean;
}

type PreviewMode = 'none' | 'texture' | 'model' | 'rdr' | 'mps';

interface RdrResult {
  sourcePath: string;
  entry?: string;
  version: number;
  stringPoolSize: number;
  atomCount: number;
  root: unknown;
  fallback?: boolean;
  parseError?: string;
}

interface MpsMap {
  offset: number;
  name: string;
  width: number;
  height: number;
  minX: number; minY: number; minZ: number;
  maxX: number; maxY: number; maxZ: number;
  scaleX: number; scaleZ: number;
}

interface MpsResult {
  sourcePath: string;
  declaredCount: number;
  foundCount: number;
  fileSize: number;
  maps: MpsMap[];
}

// One node in the collapsible RDR tree. `path` is a unique key (uses dotted
// breadcrumb + index) so expand/collapse state can be tracked across re-renders.
interface RdrTreeNode {
  path: string;
  name: string;
  kind: 'string' | 'int' | 'float' | 'array' | 'object' | 'leaf';
  value?: string | number;
  children?: RdrTreeNode[];
  depth: number;
}

@Component({
  selector: 'app-zipper-workbench-page',
  standalone: true,
  imports: [IconComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="zip-shell">
      <div class="zip-toolbar">
        <div class="zip-title">
          <app-icon name="database" [size]="16" />
          <span>Zipper FileFormats</span>
        </div>
        <div class="zip-toolbar-spacer"></div>
        <button class="zip-btn zip-primary" [disabled]="busy()" (click)="openFile()">
          <app-icon name="harddrive" [size]="14" /> Open
        </button>
        <button class="zip-btn" [disabled]="!result() || busy()" (click)="refresh()">
          <app-icon name="refreshcw" [size]="14" /> Refresh
        </button>
        <button class="zip-btn" [disabled]="!result() || busy() || packageLoading()" (click)="reloadPackage()" title="Force-reload the full ZDB package — bypasses the extension cache. Use after the source file changes on disk.">
          <app-icon name="refreshcw" [size]="14" /> Reload Package
        </button>
        <button class="zip-btn" [disabled]="!result()?.canExtract || busy()" (click)="extractAll()">
          <app-icon name="save" [size]="14" /> Extract All
        </button>
      </div>

      @if (archiveStack().length > 0) {
        <div class="zip-breadcrumb">
          <button class="zip-btn" (click)="navigateRoot()">
            <app-icon name="harddrive" [size]="13" />
            {{ rootBreadcrumb() }}
          </button>
          @for (layer of archiveStack(); track $index; let i = $index) {
            <span class="zip-breadcrumb-sep">/</span>
            <button class="zip-btn" (click)="navigateToDepth(i + 1)">{{ layer.name }}</button>
          }
          <div class="zip-toolbar-spacer"></div>
          <button class="zip-btn" (click)="navigateBack()">
            <app-icon name="refreshcw" [size]="13" /> Back
          </button>
        </div>
      }

      @if (statusMsg()) {
        <button
          type="button"
          class="zip-status"
          [class.zip-status-error]="statusIsError()"
          [class.zip-status-busy]="busy()"
          [disabled]="!lastRevealPath()"
          (click)="revealLast()">
          {{ statusMsg() }}
        </button>
      }

      <!-- Long-running ZDB package prefetch indicator. Does NOT gate the
           rest of the UI — entries are still browsable while the bytes
           stream in; the bar simply signals that more context is being
           loaded so the model viewer's first preview shows textures. -->
      @if (packageLoading()) {
        <div class="zip-scanning-bar">
          <div class="zip-scanning-bar-inner"></div>
          <span class="zip-scanning-text">{{ packageMessage() || 'Loading package…' }}</span>
        </div>
      }

      <div class="zip-main">
        <aside class="zip-sidebar">
          <div class="zip-path" [title]="result()?.path || ''">
            {{ result()?.path || 'No file selected' }}
          </div>

          <div class="zip-meta">
            <div>
              <span>Format</span>
              <strong>{{ result()?.format || '-' }}</strong>
            </div>
            <div>
              <span>Role</span>
              <strong>{{ roleLabel() }}</strong>
            </div>
            <div>
              <span>Size</span>
              <strong>{{ formatBytes(result()?.size || 0) }}</strong>
            </div>
            <div>
              <span>Items</span>
              <strong>{{ currentEntries().length || saveRecordCount() }}</strong>
            </div>
          </div>

          @if (currentEntries().length) {
            <label class="zip-search">
              <app-icon name="search" [size]="14" />
              <input
                type="text"
                placeholder="Search entries"
                [value]="search()"
                (input)="onSearch($event)" />
            </label>
          }

          <!-- Sidebar texture quick-grid. Mirrors the HTML map viewer's
               left-column thumbnail strip. Populated by sidebarTextures()
               from the loaded TXR companion. Click jumps to that tif entry. -->
          @if (sidebarTextures().length > 0) {
            <div class="zip-sidebar-texgrid-section">
              <div class="zip-sidebar-texgrid-title">Textures ({{ sidebarTextures().length }})</div>
              <input
                class="zip-search-input"
                type="text"
                placeholder="Filter textures…"
                [value]="mapTextureFilter()"
                (input)="onMapTextureFilterInput($event)"
                style="width:100%;margin-bottom:6px;background:var(--vscode-input-background,#1e1e1e);color:var(--vscode-input-foreground,#d4d4d4);border:1px solid var(--vscode-panel-border,#3e3e42);border-radius:4px;padding:4px 6px;font-size:11px" />
              <div class="zip-sidebar-texgrid">
                @for (tex of sidebarTextures(); track tex.name) {
                  <button
                    type="button"
                    class="zip-sidebar-tex"
                    [class.active]="activeTextureName() === tex.name.toLowerCase()"
                    [title]="tex.name"
                    (click)="onSidebarTextureClick(tex.name)">
                    <img [src]="tex.dataUrl" [alt]="tex.name" />
                    <div class="zip-tex-label">{{ tex.name }}</div>
                  </button>
                }
              </div>
            </div>
          }

          @if (isApache()) {
            <div class="zip-side-section">
              <div class="zip-section-title">APACHE00</div>
              <button class="zip-side-btn" [disabled]="busy()" (click)="apacheBuildPatch()">
                <app-icon name="wrench" [size]="14" /> Build Patch
              </button>
              <button class="zip-side-btn" [disabled]="busy() || !result()?.apache?.hasFtScore" (click)="apacheDecrypt('ftscore')">
                <app-icon name="code" [size]="14" /> FTScore
              </button>
              <button class="zip-side-btn" [disabled]="busy() || !result()?.apache?.hasZSealEtc" (click)="apacheDecrypt('zsealetc')">
                <app-icon name="filecode" [size]="14" /> ZSealEtc
              </button>
            </div>
          }

          @if (isSave()) {
            <div class="zip-side-section">
              <div class="zip-section-title">Save</div>
              <button class="zip-side-btn" [disabled]="busy()" (click)="saveExportDecrypted()">
                <app-icon name="save" [size]="14" /> Decrypted
              </button>
              <button class="zip-side-btn" [disabled]="busy()" (click)="saveCraft('active')">
                <app-icon name="plus" [size]="14" /> Active
              </button>
              <button class="zip-side-btn" [disabled]="busy()" (click)="saveCraft('overflow')">
                <app-icon name="zap" [size]="14" /> Overflow
              </button>
              <button class="zip-side-btn" [disabled]="busy()" (click)="saveCraft('extreme')">
                <app-icon name="wrench" [size]="14" /> Extreme
              </button>
              <button class="zip-side-btn" [disabled]="busy()" (click)="saveCraft('all')">
                <app-icon name="workflow" [size]="14" /> All Probes
              </button>
            </div>
          }
        </aside>

        <section class="zip-content">
          @if (!result()) {
            <div class="zip-empty">
              <app-icon name="database" [size]="34" />
              <span>Open a ZDB, ZAR, ZED, APACHE00.ZDB, or SOCOM II SaveGame file.</span>
            </div>
          } @else {
            <div class="zip-head">
              <div class="zip-head-main">
                <div class="zip-file-name">{{ currentLayerTitle() }}</div>
                <div class="zip-summary">{{ currentLayerSummary() }}</div>
              </div>
              @if (selectedEntry(); as entry) {
                <button class="zip-btn" [disabled]="busy()" (click)="extractEntry(entry)">
                  <app-icon name="save" [size]="14" /> Extract
                </button>
              }
            </div>

            @if (result()?.zed) {
              <div class="zip-kv-row">
                <div><span>Root</span><strong>{{ hex(result()?.zed?.rootValue) }}</strong></div>
                <div><span>String Base</span><strong>{{ hex(result()?.zed?.stringBasePointer) }}</strong></div>
                <div><span>Header 5C</span><strong>{{ hex(result()?.zed?.headerWord5C) }}</strong></div>
                <div><span>Align</span><strong>{{ result()?.zed?.dataAlignment }}</strong></div>
              </div>
            }

            @if (previewMode() === 'rdr') {
              <div class="zip-preview">
                <div class="zip-preview-stage" style="display:block;overflow:auto;padding:8px;background:var(--vscode-editorWidget-background, #1a1a1a)">
                  @if (rdr(); as r) {
                    @if (r.fallback) {
                      @if (rdrFallbackLists(); as f) {
                        <div class="zip-rdr-fallback-section">
                          <h4>Texture refs ({{ f.textureRefs.length }})</h4>
                          <ul>
                            @for (s of f.textureRefs; track s) { <li>{{ s }}</li> }
                          </ul>
                        </div>
                        <div class="zip-rdr-fallback-section">
                          <h4>File refs ({{ f.fileRefs.length }})</h4>
                          <ul>
                            @for (s of f.fileRefs; track s) { <li>{{ s }}</li> }
                          </ul>
                        </div>
                        <div class="zip-rdr-fallback-section">
                          <h4>Model refs ({{ f.modelRefs.length }})</h4>
                          <ul>
                            @for (s of f.modelRefs; track s) { <li>{{ s }}</li> }
                          </ul>
                        </div>
                      }
                    } @else {
                      <div class="zip-rdr-tree">
                        @for (row of rdrVisibleRows(); track row.path) {
                          <div
                            class="zip-rdr-tree-row"
                            [class.expandable]="rdrIsExpandable(row)"
                            [style.padding-left.px]="6 + row.depth * 14"
                            (click)="rdrIsExpandable(row) && toggleRdrNode(row.path)">
                            <span class="zip-rdr-caret">
                              @if (rdrIsExpandable(row)) {
                                {{ isRdrExpanded(row.path) ? '▾' : '▸' }}
                              }
                            </span>
                            <span class="zip-rdr-name">{{ row.name }}</span>
                            <span class="zip-rdr-type">:{{ row.kind }}</span>
                            @if (row.kind !== 'object' && row.kind !== 'array') {
                              <span class="zip-rdr-value">{{ row.value }}</span>
                            } @else {
                              <span class="zip-rdr-type">({{ row.value }})</span>
                            }
                          </div>
                        }
                      </div>
                    }
                  } @else {
                    <div style="padding:8px;color:var(--vscode-descriptionForeground)">{{ previewMessage() || 'Loading…' }}</div>
                  }
                </div>
                <div class="zip-preview-info">
                  @if (rdr(); as r) {
                    @if (r.fallback) {
                      <div><span>Mode</span><strong>fallback (string scan)</strong></div>
                      @if (r.parseError) {
                        <div><span>Parse error</span><strong>{{ r.parseError }}</strong></div>
                      }
                    } @else {
                      <div><span>RDR version</span><strong>{{ r.version }}</strong></div>
                      <div><span>Atoms</span><strong>{{ r.atomCount }}</strong></div>
                      <div><span>String pool</span><strong>{{ r.stringPoolSize }} bytes</strong></div>
                    }
                    @if (r.entry) {
                      <div><span>Entry</span><strong>{{ r.entry }}</strong></div>
                    }
                    <div class="zip-dump-buttons">
                      <button class="zip-btn" (click)="dumpRdrReport()">Dump RDR report</button>
                    </div>
                  } @else {
                    <div><span>Status</span><strong>{{ previewMessage() || 'Loading...' }}</strong></div>
                  }
                </div>
              </div>
            }

            <!-- AIMAPS.MPS preview — table of AI maps. -->
            @if (previewMode() === 'mps') {
              <div class="zip-preview">
                <div class="zip-preview-stage zip-mps-table" style="display:block">
                  @if (mps(); as m) {
                    @if (m.maps.length === 0) {
                      <div style="padding:8px;color:var(--vscode-descriptionForeground)">No AI maps found in this MPS file.</div>
                    } @else {
                      <table>
                        <thead>
                          <tr>
                            <th>Name</th>
                            <th>W × H</th>
                            <th>Min XYZ</th>
                            <th>Max XYZ</th>
                            <th>Scale X / Z</th>
                            <th>Offset</th>
                          </tr>
                        </thead>
                        <tbody>
                          @for (map of m.maps; track map.offset) {
                            <tr>
                              <td>{{ map.name }}</td>
                              <td>{{ map.width }} × {{ map.height }}</td>
                              <td>{{ map.minX.toFixed(1) }}, {{ map.minY.toFixed(1) }}, {{ map.minZ.toFixed(1) }}</td>
                              <td>{{ map.maxX.toFixed(1) }}, {{ map.maxY.toFixed(1) }}, {{ map.maxZ.toFixed(1) }}</td>
                              <td>{{ map.scaleX.toFixed(3) }} / {{ map.scaleZ.toFixed(3) }}</td>
                              <td>{{ hex(map.offset) }}</td>
                            </tr>
                          }
                        </tbody>
                      </table>
                    }
                  } @else {
                    <div style="padding:8px;color:var(--vscode-descriptionForeground)">{{ previewMessage() || 'Loading…' }}</div>
                  }
                </div>
                <div class="zip-preview-info">
                  @if (mps(); as m) {
                    <div><span>Declared count</span><strong>{{ m.declaredCount }}</strong></div>
                    <div><span>Found count</span><strong>{{ m.foundCount }}</strong></div>
                    <div><span>File size</span><strong>{{ formatBytes(m.fileSize) }}</strong></div>
                  } @else {
                    <div><span>Status</span><strong>{{ previewMessage() || 'Loading…' }}</strong></div>
                  }
                </div>
              </div>
            }

            @if (pendingArchiveEntry(); as pending) {
              <div class="zip-preview">
                <div class="zip-preview-stage" style="display:flex;align-items:center;justify-content:center;flex-direction:column;gap:12px">
                  <div style="font-size:14px;color:var(--vscode-descriptionForeground)">
                    {{ pending.entryName }} is a nested ZED/ZAR archive ({{ pending.size }} bytes).
                  </div>
                  <button class="zip-btn zip-primary" (click)="enterPendingArchive()">
                    <app-icon name="harddrive" [size]="14" /> Enter Archive
                  </button>
                </div>
                <div class="zip-preview-info">
                  <div>
                    <span>Entry</span>
                    <strong>{{ pending.entryName }}</strong>
                  </div>
                  <div>
                    <span>Size</span>
                    <strong>{{ pending.size }} bytes</strong>
                  </div>
                </div>
              </div>
            }

            @if (selectedEntry() && (previewMode() === 'texture' || previewMode() === 'model')) {
              <div class="zip-preview">
                <div class="zip-preview-stage">
                  @if (previewMode() === 'texture') {
                    <img #previewImg class="zip-preview-pic" [src]="textureDataUrl()" alt="Texture preview" />
                  } @else {
                    <canvas #previewCanvas class="zip-preview-canvas"></canvas>
                  }
                  @if (previewMessage()) {
                    <div class="zip-preview-overlay">{{ previewMessage() }}</div>
                  }
                </div>
                <div class="zip-preview-info">
                  <div class="zip-preview-tabs">
                    <button
                      type="button"
                      class="zip-preview-tab"
                      [class.active]="previewMode() === 'texture'"
                      [disabled]="!canPreviewTexture()"
                      (click)="setPreviewMode('texture')">Texture</button>
                    <button
                      type="button"
                      class="zip-preview-tab"
                      [class.active]="previewMode() === 'model'"
                      [disabled]="!canPreviewModel()"
                      (click)="setPreviewMode('model')">Model</button>
                  </div>
                  @if (previewMode() === 'texture' && texture(); as tex) {
                    <div>
                      <span>Size</span><strong>{{ tex.header.width }} x {{ tex.header.height }}</strong>
                    </div>
                    <div>
                      <span>Format</span><strong>{{ tex.header.isIndexed ? 'Indexed8' : 'RGBA32' }}</strong>
                    </div>
                    <div>
                      <span>Palette</span><strong>{{ paletteLabel(tex) }}</strong>
                    </div>
                  }
                  @if (previewMode() === 'model' && model(); as mdl) {
                    <!-- Stats -->
                    <div class="zip-pane-section">
                      <div class="zip-pane-section-title">Mesh</div>
                      <div><span>Chunks</span><strong>{{ mdl.chunks.length }}</strong></div>
                      <div><span>Vertices / Triangles</span><strong>{{ activeVertexCount() }} / {{ activeTriangleCount() }}</strong></div>
                      @if (mdl.chunks.length > 1) {
                        <div>
                          <span>Chunk</span>
                          <select class="zip-chunk-select" [value]="selectedChunk()" (change)="onChunkChange($event)">
                            <option [value]="-1">All chunks ({{ mdl.chunks.length }})</option>
                            @for (chunk of mdl.chunks; track chunk.offset; let idx = $index) {
                              <option [value]="idx">{{ chunkLabel(idx, chunk) }}</option>
                            }
                          </select>
                        </div>
                      }
                      @if (mdl.textureRefs.length > 0) {
                        <div>
                          <span>Texture refs</span>
                          <div class="zip-texrefs">
                            @for (ref of mdl.textureRefs; track ref) {
                              <span>{{ ref }}</span>
                            }
                          </div>
                        </div>
                      }
                    </div>

                    <!-- Render — all draw / shading toggles in one place.
                         Render mode controls wireframe (was a separate
                         button) and top-down view; V orientation replaces
                         the old Flip Y button; UV interpretation replaces
                         the old UV mode dropdown. -->
                    <div class="zip-pane-section">
                      <div class="zip-pane-section-title">Render</div>
                      <div class="zip-map-controls">
                        <div class="zip-map-ctrl">
                          <span>Mode</span>
                          <select [value]="mapRenderMode()" (change)="onMapRenderModeChange($event)">
                            <option value="view3dTexturedLit">3D textured + fog/lighting</option>
                            <option value="view3dTextured">3D textured solid</option>
                            <option value="view3dSolid">3D material solid</option>
                            <option value="topSolid">top-down material solid</option>
                            <option value="topWire">top-down wire</option>
                            <option value="view3dWire">3D wire</option>
                          </select>
                        </div>
                        <div class="zip-map-ctrl">
                          <span>Triangle stride</span>
                          <select [value]="mapTriStride()" (change)="onMapTriStrideChange($event)">
                            <option [value]="1">1 — full solid</option>
                            <option [value]="2">2 — skip half</option>
                            <option [value]="4">4 — debug overview</option>
                            <option [value]="8">8 — wire overview</option>
                          </select>
                        </div>
                        <div class="zip-map-ctrl">
                          <span>V orientation</span>
                          <select [value]="mapTextureV()" (change)="onMapTextureVChange($event)">
                            <option value="flipV">flip V</option>
                            <option value="direct">direct</option>
                          </select>
                        </div>
                        <div class="zip-map-ctrl">
                          <span>UV interpretation</span>
                          <select [value]="mapUvInterpretation()" (change)="onMapUvInterpretationChange($event)">
                            <option value="autoWorld">auto world UV</option>
                            <option value="norm4096">raw / 4096 normalized</option>
                            <option value="pixel16">raw / 16 pixel tiled</option>
                            <option value="pixel256">raw / 256 pixel tiled</option>
                          </select>
                        </div>
                      </div>
                    </div>

                    <!-- Materials -->
                    <div class="zip-pane-section">
                      <div class="zip-pane-section-title">Materials</div>
                      <div class="zip-map-controls">
                        <div class="zip-map-ctrl">
                          <span>Binding</span>
                          <select [value]="mapMaterialBinding()" (change)="onMapMaterialBindingChange($event)">
                            <option value="tailScan">tail scan (default)</option>
                            <option value="smartWorld">smart world marker</option>
                            <option value="sliceNearest">slice nearest marker</option>
                            <option value="globalNearest">global nearest marker</option>
                          </select>
                        </div>
                        @if (availablePalettes().length > 0) {
                          <div class="zip-map-ctrl">
                            <span>Palette override</span>
                            <select [value]="paletteOverride() ?? ''" (change)="onPaletteOverrideChange($event)">
                              <option value="">(auto)</option>
                              @for (key of availablePalettes(); track key) {
                                <option [value]="key">{{ key }}</option>
                              }
                            </select>
                          </div>
                        }
                      </div>
                    </div>

                    <!-- Compose / dump utilities. Each pipes its output
                         to the Code Designer ZDB output channel. -->
                    <div class="zip-pane-section">
                      <div class="zip-pane-section-title">Tools</div>
                      <div class="zip-dump-buttons">
                        <button class="zip-btn zip-primary" [disabled]="!companionScene()" (click)="composeGeoScene()" title="Walk the GEO scene tree, resolve each placed instance to a model from the loaded package, and render the composed mesh.">
                          Compose GEO scene
                        </button>
                        <button class="zip-btn" (click)="composeHierarchicalModel()" title="Decode chunks from the selected entry AND every child node in its subtree, applying per-node transforms. Required for CLIB players and other hierarchical models.">
                          Compose hierarchical
                        </button>
                        <button class="zip-btn" (click)="dumpMissingTextures()">Dump missing textures</button>
                        <button class="zip-btn" (click)="dumpMaterialReport()">Dump material report</button>
                        <button class="zip-btn" (click)="dumpGeoReport()">Dump GEO report</button>
                        <button class="zip-btn" [disabled]="!companionScene()" (click)="dumpGeoInstances()">Dump GEO instances</button>
                      </div>
                    </div>
                  }
                </div>
              </div>
            }

            @if (isSave()) {
              <div class="zip-save-grid">
                <div>
                  <div class="zip-section-title">Header</div>
                  <div class="zip-save-fields">
                    <div><span>Title</span><strong>{{ result()?.save?.title || '-' }}</strong></div>
                    <div><span>Save Type</span><strong>{{ hex(result()?.save?.header?.saveType) }}</strong></div>
                    <div><span>Slot</span><strong>{{ result()?.save?.header?.slotIndex }}</strong></div>
                    <div><span>Data Size</span><strong>{{ hex(result()?.save?.header?.dataSize) }}</strong></div>
                    <div><span>Modules</span><strong>{{ result()?.save?.modules?.length || 0 }}</strong></div>
                    <div><span>Decrypted</span><strong>{{ formatBytes(result()?.save?.decryptedSize || 0) }}</strong></div>
                  </div>
                </div>
                <div>
                  <div class="zip-section-title">Modules</div>
                  <div class="zip-module-list">
                    @for (module of result()?.save?.modules || []; track module) {
                      <span>{{ module }}</span>
                    }
                  </div>
                </div>
              </div>
            }

            @if (isSave()) {
              <div class="zip-table-wrap">
                <div class="zip-table zip-record-table">
                  <div class="zip-table-head">
                    <span>Field</span>
                    <span>Type</span>
                    <span>Flags</span>
                    <span>Count</span>
                    <span>Value</span>
                  </div>
                  @for (record of result()?.save?.records || []; track record.fieldName + record.strOffset) {
                    <div class="zip-table-row">
                      <span class="zip-name-cell">{{ record.fieldName || '-' }}</span>
                      <span>{{ hex(record.type, 2) }}</span>
                      <span>{{ hex(record.flags, 2) }}</span>
                      <span>{{ record.count }}</span>
                      <span>{{ hex(record.valOffset) }}</span>
                    </div>
                  }
                </div>
              </div>
            } @else if (isCurrentFileTextureArchive() && textureThumbnails().length > 0) {
              <div class="zip-tex-grid-wrap">
                <div class="zip-tex-grid">
                  @for (tex of textureThumbnails(); track tex.entryIndex) {
                    <button
                      type="button"
                      class="zip-tex-tile"
                      [class.active]="selectedIndex() === tex.entryIndex"
                      (click)="selectThumbnail(tex.entryIndex)">
                      <div class="zip-tex-img-wrap">
                        <img [src]="tex.dataUrl" [alt]="tex.name" />
                      </div>
                      <div class="zip-tex-tile-name" [title]="tex.name">{{ tex.name }}</div>
                      <div class="zip-tex-tile-meta">
                        {{ tex.width }}x{{ tex.height }}@if (!tex.paletteResolved) { (no palette) }
                      </div>
                    </button>
                  }
                </div>
              </div>
            } @else {
              <div class="zip-table-wrap">
                <div class="zip-table">
                  <div class="zip-table-head">
                    <span>Name</span>
                    <span>Kind</span>
                    <span>Size</span>
                    <span>Type</span>
                    <span></span>
                  </div>
                  @if (filteredEntries().length === 0) {
                    <div class="zip-empty-row">No matching entries.</div>
                  }
                  @for (entry of filteredEntries(); track entry.index) {
                    <div
                      role="button"
                      tabindex="0"
                      class="zip-table-row zip-entry-row"
                      [class.active]="selectedEntry()?.index === entry.index"
                      (click)="selectEntry(entry)">
                      <span class="zip-name-cell" [title]="entry.path">{{ entry.path || entry.name }}</span>
                      <span>{{ entry.kind }}</span>
                      <span>{{ formatBytes(entry.size) }}</span>
                      <span>{{ entry.type === undefined ? '-' : hex(entry.type) }}</span>
                      <span class="zip-row-actions">
                        <button class="zip-icon-btn" [disabled]="busy()" title="Extract entry" (click)="extractEntry(entry); $event.stopPropagation()">
                          <app-icon name="save" [size]="13" />
                        </button>
                      </span>
                    </div>
                  }
                </div>
              </div>
            }
          }
        </section>
      </div>
    </div>
  `,
})
export class ZipperWorkbenchPageComponent implements OnInit, OnDestroy, AfterViewInit {
  @ViewChild('previewCanvas') private previewCanvas?: ElementRef<HTMLCanvasElement>;

  private readonly vscode = getVsCodeApi();
  private modelRenderer: ZedModelRenderer | null = null;
  private nodeCache = new Map<number, Uint8Array>();
  private inflightNode: number | null = null;
  // Maps a fetched node index back to the selected entry index that triggered
  // the fetch (used when .tif markers redirect to the next texdat entry).
  private nodeAliasForFetch = new Map<number, number>();
  private palettesRequested = false;

  readonly result = signal<ZipperInspectResult | null>(null);
  readonly search = signal('');
  readonly selectedIndex = signal<number | null>(null);
  readonly busy = signal(false);
  readonly statusMsg = signal('');
  readonly statusIsError = signal(false);
  readonly lastOperation = signal<ZipperOperationResult | null>(null);

  readonly palettes = signal<Map<string, ZedPalette> | null>(null);
  readonly paletteMessage = signal('');

  // Stack of inline ZED archives we've drilled into. When non-empty, the
  // entry table at the top level shows the deepest sub-archive's entries and
  // node reads come from the in-memory cache, not the bridge.
  readonly archiveStack = signal<Array<{ name: string; archive: InspectedZedArchive }>>([]);
  // Companion data: textures from sibling _TXR (name -> ZedTexture record bytes
  // + entry index pair) and the parsed scene from sibling _GEO.
  readonly companionTextures = signal<Map<string, Uint8Array> | null>(null);
  readonly companionScene = signal<GeoScene | null>(null);
  readonly previewMode = signal<PreviewMode>('none');
  readonly texture = signal<ZedTexture | null>(null);
  readonly textureDataUrl = signal('');
  readonly model = signal<ZedModel | null>(null);
  readonly previewMessage = signal('');
  readonly wireframe = signal(false);
  readonly flipY = signal(false);
  readonly selectedChunk = signal<number>(-1); // -1 = All chunks
  readonly uvMode = signal<'direct' | 'flipV' | 'swap' | 'swapFlipV'>('direct');
  readonly rdr = signal<RdrResult | null>(null);
  readonly rdrJson = computed(() => {
    const r = this.rdr();
    return r ? JSON.stringify(r.root, null, 2) : '';
  });
  // Tracks which RDR tree paths are currently expanded in the UI. Reset on
  // new RDR load. Use a signal so the template re-renders when toggled.
  readonly rdrExpanded = signal<Set<string>>(new Set());
  // Tree shape derived from the RDR result. Flattened to a depth-first list
  // of nodes that pass the expand filter, ready to render as a row list.
  readonly rdrTreeRoots = computed<RdrTreeNode[]>(() => {
    const r = this.rdr();
    if (!r || r.fallback) { return []; }
    return this.buildRdrTreeRoots(r.root);
  });
  readonly rdrVisibleRows = computed<RdrTreeNode[]>(() => {
    const roots = this.rdrTreeRoots();
    const expanded = this.rdrExpanded();
    const out: RdrTreeNode[] = [];
    const walk = (nodes: RdrTreeNode[]) => {
      for (const n of nodes) {
        out.push(n);
        if (n.children && expanded.has(n.path)) { walk(n.children); }
      }
    };
    walk(roots);
    return out;
  });

  // AIMAPS.MPS preview state.
  readonly mps = signal<MpsResult | null>(null);

  // Full ZDB-package state. After the user opens a .zdb, we fetch every
  // top-level entry's bytes in one socket round trip and decode all
  // textures / palettes / GEO across the whole package — mirrors the HTML
  // reference viewers which pre-load all of RUN.zip. The cached entry
  // bytes also let nested archive drill-ins skip the per-entry readNode.
  readonly packageLoading = signal(false);
  readonly packageMessage = signal('');
  // Lowercased leaf name -> bytes, for the currently-loaded ZDB.
  private packageEntryBytes = new Map<string, Uint8Array>();

  // HTML map-viewer style controls. Drive the renderer config and the
  // material-binding signal used in the model parser path.
  readonly mapRenderMode = signal<'view3dTexturedLit' | 'view3dTextured' | 'view3dSolid' | 'topSolid' | 'topWire' | 'view3dWire'>('view3dTexturedLit');
  readonly mapTriStride = signal<number>(1);
  // Default 'smartWorld' to match the HTML map viewer. tailScan-only
  // misses ~80% of world chunks because their .tif name doesn't sit in
  // the tail area; smartWorld supplements with marker-and-slice rules.
  // For weapons/castles that tail scan handles perfectly, smartWorld's
  // override of an existing chunk.textureName is harmless because the
  // nearest marker IS that same .tif.
  readonly mapMaterialBinding = signal<'tailScan' | 'smartWorld' | 'sliceNearest' | 'globalNearest'>('smartWorld');
  // The raw model bytes for the currently-displayed model, kept so we can
  // re-run the binding pass on dropdown change without re-decoding.
  private currentModelBytes: Uint8Array | null = null;
  private currentSliceOffsets: number[] = [];
  readonly mapTextureV = signal<'flipV' | 'direct'>('flipV');
  readonly mapUvInterpretation = signal<'autoWorld' | 'norm4096' | 'pixel16' | 'pixel256'>('autoWorld');
  readonly mapTextureFilter = signal<string>('');

  // Aggregated thumbnail list for the sidebar texture grid. Built from
  // companionTextures (TXR sibling) plus the current archive's own TXR
  // entries when applicable. Cached to avoid re-decoding on every render.
  readonly sidebarTextures = computed<Array<{ name: string; dataUrl: string }>>(() => {
    const sources = this.companionTextures();
    if (!sources || sources.size === 0) { return []; }
    const palettes = this.palettes();
    const filter = this.mapTextureFilter().trim().toLowerCase();
    const out: Array<{ name: string; dataUrl: string }> = [];
    for (const [name, bytes] of sources) {
      if (filter && !name.toLowerCase().includes(filter)) { continue; }
      try {
        const tex = parseTexture(bytes, palettes, name);
        out.push({ name, dataUrl: textureToDataUrl(tex) });
      } catch { /* skip undecodable */ }
    }
    out.sort((a, b) => a.name.localeCompare(b.name));
    return out;
  });
  // Optional manual palette override key (e.g. "texpal_44"). When set, the
  // model viewer uses this palette instead of whatever the rules / header guess.
  readonly paletteOverride = signal<string | null>(null);
  // List of palette keys available in the current `palettes` signal, for the
  // override dropdown.
  readonly availablePalettes = computed<string[]>(() => {
    const p = this.palettes();
    if (!p) { return []; }
    return [...p.keys()].filter(k => /^texpal_\d+$/i.test(k)).sort((a, b) => {
      const na = Number(/(\d+)/.exec(a)?.[1] ?? 0);
      const nb = Number(/(\d+)/.exec(b)?.[1] ?? 0);
      return na - nb;
    });
  });

  readonly canPreviewTexture = computed(() => {
    const entry = this.selectedEntry();
    return !!entry && /\.tif$/i.test(entry.name);
  });

  readonly canPreviewModel = computed(() => this.model() !== null);


  // Full unfiltered entry list for the current layer. Used internally for
  // texdat / texture lookups (we need to see the Nxxx_xxx stubs to know what
  // belongs to what), and as the base for the user-facing table.
  readonly rawEntries = computed<ZipperEntryInfo[]>(() => {
    const stack = this.archiveStack();
    if (stack.length > 0) {
      const archive = stack[stack.length - 1].archive;
      return archive.entries.map(e => ({
        index: e.index,
        name: e.name,
        path: e.name,
        size: e.size,
        kind: e.size === 0 ? 'node' : 'payload',
        type: e.type,
        extension: e.extension,
      }));
    }
    return this.result()?.entries ?? [];
  });

  // What the user sees in the entry table. For _MDL.ZED files we hide the
  // Nxxx_xxx reference stubs because they're internal pointers into the
  // parent named item, not separate items themselves. The pattern covers both
  // simple form (N000_000) and the extended form (N000_I000_V00) used in
  // CLIB/FLIB/etc.
  readonly currentEntries = computed<ZipperEntryInfo[]>(() => {
    const raw = this.rawEntries();
    if (this.isCurrentFileModelArchive()) {
      return raw.filter(e => !/^N\d+(?:_[A-Z]?\d+)+$/i.test(e.name));
    }
    return raw;
  });

  // File-name detector for the current navigation layer (sub-archive name or
  // root file name). Used to switch UI between table, model preview, or grid.
  private currentFileName(): string {
    const stack = this.archiveStack();
    if (stack.length > 0) { return stack[stack.length - 1].name; }
    const p = this.result()?.path ?? '';
    return p.split(/[\\/]/).pop() ?? '';
  }

  currentLayerTitle(): string {
    const stack = this.archiveStack();
    if (stack.length > 0) {
      // Use only the leaf name (strip RUN\COMMON\... path prefixes).
      const full = stack[stack.length - 1].name;
      return full.split(/[\\/]/).pop() ?? full;
    }
    return this.result()?.fileName ?? '';
  }

  currentLayerSummary(): string {
    const stack = this.archiveStack();
    if (stack.length > 0) {
      const archive = stack[stack.length - 1].archive;
      return `Nested archive with ${archive.entries.length} entry(s)`;
    }
    return this.result()?.summary ?? '';
  }

  isCurrentFileModelArchive(): boolean {
    return /_MDL(?:\([^)]+\))?\.ZED$/i.test(this.currentFileName());
  }

  isCurrentFileTextureArchive(): boolean {
    return /_TXR(?:\([^)]+\))?\.ZED$/i.test(this.currentFileName());
  }

  readonly filteredEntries = computed(() => {
    const current = this.currentEntries();
    const q = this.search().trim().toLowerCase();
    if (!q) { return current; }
    return current.filter(entry =>
      `${entry.name} ${entry.path} ${entry.kind} ${entry.extension ?? ''}`.toLowerCase().includes(q));
  });

  readonly selectedEntry = computed(() => {
    const idx = this.selectedIndex();
    if (idx === null) { return null; }
    const entries = this.currentEntries();
    return entries.find(e => e.index === idx) ?? null;
  });

  // Becomes true when the selected entry's bytes are themselves a ZED-format
  // archive (ZED or ZAR). We park here instead of auto-drilling so the user
  // can keep exploring the parent archive's other entries first.
  readonly pendingArchiveEntry = signal<{ entryIndex: number; entryName: string; size: number } | null>(null);

  readonly lastRevealPath = computed(() => {
    const op = this.lastOperation();
    return op?.outputDirectory ?? op?.outputPath ?? op?.writtenFiles?.[0] ?? '';
  });

  readonly roleLabel = computed(() => {
    switch (this.result()?.role) {
      case 'apache-patch-archive': return 'Apache Patch';
      case 'structured-container': return 'Structure';
      case 'game-save': return 'Game Save';
      case 'archive': return 'Archive';
      default: return this.result()?.role || '-';
    }
  });

  private readonly onMessage = (event: MessageEvent): void => {
    const data = event.data as { type: string; [key: string]: unknown };
    switch (data.type) {
      case 'zipperStatus':
        this.setStatus((data['message'] as string) ?? '', false, !!data['busy']);
        break;
      case 'zipperError':
        this.setStatus((data['message'] as string) ?? 'Zipper operation failed', true, false);
        break;
      case 'zipperLoaded':
        this.result.set((data['result'] as ZipperInspectResult) ?? null);
        this.selectedIndex.set(null);
        this.lastOperation.set(null);
        this.nodeCache.clear();
        this.palettes.set(null);
        this.palettesRequested = false;
        this.archiveStack.set([]);
        this.packageEntryBytes.clear();
        this.companionTextures.set(null);
        this.companionScene.set(null);
        this.resetPreview();
        this.setStatus(this.result()?.summary ?? 'File loaded.', false, false);
        this.requestPalettesIfNeeded();
        // For ZDB packages, kick off the full-package preload so the model
        // viewer never has to chase per-entry round-trips later.
        this.requestZdbPackageIfNeeded();
        break;
      case 'zdbPackage': {
        const nodes = (data['nodes'] as Array<{ index: number; name: string; type: number; data: string }>) ?? [];
        const fromCache = !!data['fromCache'];
        if (fromCache) {
          this.log(`[package] restored from extension cache (${nodes.length} entries)`);
        }
        this.applyZdbPackage(nodes);
        break;
      }
      case 'zdbPackageError': {
        const message = (data['message'] as string) ?? 'unknown';
        this.packageLoading.set(false);
        this.packageMessage.set('Package load failed: ' + message);
        this.log(`[package] ERROR: ${message}`);
        break;
      }
      case 'zipperOperation':
        this.lastOperation.set((data['result'] as ZipperOperationResult) ?? null);
        this.setStatus((data['message'] as string) ?? 'Operation complete.', false, false);
        break;
      case 'nodeData': {
        const fetchedIndex = (data['entryIndex'] as number) ?? -1;
        const dataStr = (data['data'] as string) ?? '';
        // Intercept palette fetches that came from loadNestedPalettesForCurrentLayer.
        if (this.pendingPaletteFetch && this.pendingPaletteFetch.rootEntryIndex === fetchedIndex) {
          const pending = this.pendingPaletteFetch;
          this.pendingPaletteFetch = null;
          try {
            const palBytes = base64ToBytes(dataStr);
            const stack = this.archiveStack();
            if (stack.length > 0) {
              this.applyPaletteBytesForCurrentLayer(palBytes, pending.palName, stack[stack.length - 1].archive);
            }
          } catch { /* ignore decode errors */ }
          break;
        }
        // Intercept root-companion (TXR/PAL/GEO) fetches for a nested MDL.
        const compKind = this.rootCompanionFetches.get(fetchedIndex);
        if (compKind) {
          this.rootCompanionFetches.delete(fetchedIndex);
          try {
            const bytes = base64ToBytes(dataStr);
            const entry = (this.result()?.entries ?? []).find(e => e.index === fetchedIndex);
            this.applyMdlCompanion(compKind, bytes, entry?.name ?? '(unknown)');
          } catch { /* ignore decode errors */ }
          break;
        }
        this.handleNodeData(fetchedIndex, dataStr);
        break;
      }
      case 'nodeError': {
        const failedIndex = (data['entryIndex'] as number) ?? -1;
        const msg = (data['message'] as string) ?? 'Failed to read entry.';
        // Was it a companion fetch?
        const compKind = this.rootCompanionFetches.get(failedIndex);
        if (compKind) {
          this.rootCompanionFetches.delete(failedIndex);
          this.setStatus(`Companion ${compKind.toUpperCase()} fetch failed: ${msg}`, true, false);
        } else if (this.pendingPaletteFetch?.rootEntryIndex === failedIndex) {
          this.pendingPaletteFetch = null;
          this.setStatus(`Palette fetch failed: ${msg}`, true, false);
        } else {
          this.previewMessage.set(msg);
        }
        this.inflightNode = null;
        break;
      }
      case 'paletteFile':
        if ((data['found'] as boolean) && data['nodes']) {
          const palettes = parsePaletteNodes(data['nodes'] as Array<{ index: number; name: string; data: string }>);
          this.palettes.set(palettes);
          this.paletteMessage.set(`Loaded ${palettes.size} palette(s).`);
          this.refreshPreview();
        } else {
          this.paletteMessage.set((data['message'] as string) ?? 'No sibling _PAL.ZED found.');
        }
        break;
      case 'rdrData': {
        const result = data['result'] as RdrResult | undefined;
        this.rdr.set(result ?? null);
        // Reset expand state so the new tree opens fresh.
        this.rdrExpanded.set(new Set());
        if (result) {
          if (result.fallback) {
            this.previewMessage.set(
              `RDR strict parse failed (${result.parseError ?? 'unknown'}); showing printable-string fallback.`,
            );
            this.log(`[RDR] strict parse failed — fallback string scan. ${result.parseError ?? ''}`);
          } else {
            this.previewMessage.set(
              `RDR v${result.version}: ${result.atomCount} atoms, ${result.stringPoolSize}B strings.`,
            );
            this.log(`[RDR] v${result.version}, atoms=${result.atomCount}, pool=${result.stringPoolSize}B`);
          }
        }
        break;
      }
      case 'rdrError':
        this.previewMessage.set('RDR parse failed: ' + ((data['message'] as string) ?? 'unknown'));
        this.log(`[RDR] ERROR: ${(data['message'] as string) ?? 'unknown'}`);
        break;
      case 'mpsData': {
        const result = data['result'] as MpsResult | undefined;
        this.mps.set(result ?? null);
        if (result) {
          this.previewMessage.set(
            `MPS: ${result.foundCount} / ${result.declaredCount} AI maps found.`,
          );
          this.log(`[MPS] declared=${result.declaredCount} found=${result.foundCount} (${result.fileSize}B)`);
        }
        break;
      }
      case 'mpsError':
        this.previewMessage.set('MPS parse failed: ' + ((data['message'] as string) ?? 'unknown'));
        this.log(`[MPS] ERROR: ${(data['message'] as string) ?? 'unknown'}`);
        break;
      case 'companionFiles': {
        const companions = (data['companions'] as Record<string, { path: string; nodes: Array<{ index: number; name: string; type: number; data: string }> }>) ?? {};
        // PAL. If TXR is also available, add exact aliases from TXR texture ids
        // to the real texpal_NNN records, skipping RGBA32 textures that do not
        // consume a palette.
        if (companions['pal']) {
          let palettes = parsePaletteNodes(companions['pal'].nodes);
          if (companions['txr']) {
            const txrNodes = companions['txr'].nodes;
            palettes = aliasPalettesForTextureEntries(
              palettes,
              txrNodes.map(n => ({ name: n.name })),
              i => base64ToBytes(txrNodes[i].data),
            );
          }
          this.palettes.set(palettes);
        }
        // TXR -> .tif name to texdat bytes map
        if (companions['txr']) {
          const map = new Map<string, Uint8Array>();
          const nodes = companions['txr'].nodes;
          for (let i = 0; i < nodes.length; i++) {
            if (/\.tif$/i.test(nodes[i].name)) {
              for (let j = i + 1; j < Math.min(i + 4, nodes.length); j++) {
                if (/^texdat$/i.test(nodes[j].name)) {
                  map.set(nodes[i].name.toLowerCase(), base64ToBytes(nodes[j].data));
                  break;
                }
                if (/\.tif$/i.test(nodes[j].name)) { break; }
              }
            }
          }
          this.companionTextures.set(map);
        }
        // GEO -> scene tree
        if (companions['geo']) {
          const scene = parseGeoScene(companions['geo'].nodes as GeoEntry[]);
          this.companionScene.set(scene);
        }
        this.refreshPreview();
        break;
      }
    }
  };

  constructor() {
    effect(() => {
      // Auto-load when the selected entry changes.
      const entry = this.selectedEntry();
      if (!entry) { this.resetPreview(); return; }
      this.beginPreviewForEntry(entry.index, entry.name);
    });
  }

  ngOnInit(): void {
    window.addEventListener('message', this.onMessage);
    this.vscode.postMessage({ type: 'ready' });
  }

  ngAfterViewInit(): void {
    // Renderer is created lazily when first model preview is shown so the
    // canvas element exists.
  }

  ngOnDestroy(): void {
    window.removeEventListener('message', this.onMessage);
    this.modelRenderer?.dispose();
    this.modelRenderer = null;
  }

  openFile(): void {
    this.vscode.postMessage({ type: 'openFile' });
  }

  // Force re-extract of the loaded package. Used after the source file
  // changes on disk or to recover from a state-loss bug.
  reloadPackage(): void {
    const filePath = this.result()?.path;
    if (!filePath) { return; }
    this.packageEntryBytes.clear();
    this.companionTextures.set(null);
    this.companionScene.set(null);
    this.palettes.set(null);
    this.packageLoading.set(true);
    this.packageMessage.set(`Force-reloading ${filePath.split(/[\\/]/).pop()}…`);
    this.log(`[package] force-reloading ${filePath.split(/[\\/]/).pop()}`);
    this.vscode.postMessage({ type: 'reloadZdbPackage', path: filePath });
  }

  refresh(): void {
    const file = this.result()?.path;
    if (file) { this.vscode.postMessage({ type: 'refresh', path: file }); }
  }

  extractAll(): void {
    const file = this.result()?.path;
    if (file) { this.vscode.postMessage({ type: 'extractAll', path: file }); }
  }

  extractEntry(entry: ZipperEntryInfo): void {
    const file = this.result()?.path;
    if (!file) { return; }
    this.vscode.postMessage({
      type: 'extractEntry',
      path: file,
      entryIndex: entry.index,
      entryName: entry.name || entry.path,
    });
  }

  saveExportDecrypted(): void {
    const file = this.result()?.path;
    if (file) { this.vscode.postMessage({ type: 'saveExportDecrypted', path: file }); }
  }

  saveCraft(mode: 'active' | 'overflow' | 'extreme' | 'all'): void {
    const file = this.result()?.path;
    if (file) { this.vscode.postMessage({ type: 'saveCraft', path: file, mode }); }
  }

  apacheDecrypt(section: 'ftscore' | 'zsealetc'): void {
    const file = this.result()?.path;
    if (file) { this.vscode.postMessage({ type: 'apacheDecryptSection', path: file, section }); }
  }

  apacheBuildPatch(): void {
    const file = this.result()?.path;
    if (file) { this.vscode.postMessage({ type: 'apacheBuildPatch', path: file }); }
  }

  revealLast(): void {
    const target = this.lastRevealPath();
    if (target) { this.vscode.postMessage({ type: 'revealPath', path: target }); }
  }

  selectEntry(entry: ZipperEntryInfo): void {
    this.selectedIndex.set(entry.index);
  }

  selectThumbnail(entryIndex: number): void {
    this.selectedIndex.set(entryIndex);
  }

  navigateBack(): void {
    this.archiveStack.update(s => s.slice(0, -1));
    this.selectedIndex.set(null);
    this.resetPreview();
  }

  navigateRoot(): void {
    this.archiveStack.set([]);
    this.selectedIndex.set(null);
    this.resetPreview();
  }

  navigateToDepth(depth: number): void {
    this.archiveStack.update(s => s.slice(0, depth));
    this.selectedIndex.set(null);
    this.resetPreview();
  }

  rootBreadcrumb(): string {
    const p = this.result()?.path ?? '';
    const file = p.split(/[\\/]/).pop() ?? p;
    return file || 'Root';
  }

  onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  isApache(): boolean {
    return this.result()?.role === 'apache-patch-archive';
  }

  isSave(): boolean {
    return this.result()?.role === 'game-save';
  }

  saveRecordCount(): number {
    return this.result()?.save?.records?.length ?? 0;
  }

  formatBytes(bytes: number): string {
    if (!Number.isFinite(bytes) || bytes <= 0) { return '0 B'; }
    if (bytes < 1024) { return `${bytes} B`; }
    if (bytes < 1024 * 1024) { return `${(bytes / 1024).toFixed(1)} KB`; }
    return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
  }

  hex(value: number | undefined, width = 8): string {
    if (value === undefined || value === null || !Number.isFinite(value)) { return '-'; }
    return '0x' + (value >>> 0).toString(16).toUpperCase().padStart(width, '0');
  }

  private setStatus(message: string, isError: boolean, busy: boolean): void {
    this.statusMsg.set(message);
    this.statusIsError.set(isError);
    this.busy.set(busy);
  }

  setPreviewMode(mode: PreviewMode): void {
    if (mode === 'model' && !this.canPreviewModel()) { return; }
    if (mode === 'texture' && !this.canPreviewTexture()) { return; }
    this.previewMode.set(mode);
    if (mode === 'model') { this.ensureModelRenderer(); }
  }

  toggleWireframe(): void {
    const next = !this.wireframe();
    this.wireframe.set(next);
    this.modelRenderer?.setWireframe(next);
  }

  toggleFlipY(): void {
    const next = !this.flipY();
    this.flipY.set(next);
    this.modelRenderer?.setFlipY(next);
  }

  onChunkChange(event: Event): void {
    const value = Number((event.target as HTMLSelectElement).value);
    this.selectedChunk.set(Number.isFinite(value) ? value : -1);
    this.applyModelToRenderer();
  }

  onUvModeChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value as 'direct' | 'flipV' | 'swap' | 'swapFlipV';
    this.uvMode.set(value);
    this.applyModelToRenderer();
  }

  onPaletteOverrideChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.paletteOverride.set(value && value.length > 0 ? value : null);
    this.applyModelToRenderer();
  }


  activeVertexCount(): number {
    const mdl = this.model();
    if (!mdl) { return 0; }
    const chunk = this.selectedChunk();
    if (chunk >= 0 && chunk < mdl.chunks.length) { return mdl.chunks[chunk].vertexCount; }
    return mdl.vertices.length;
  }

  chunkLabel(idx: number, chunk: { vertexCount: number; decodedTriangleCount: number; textureName: string | null }): string {
    // User asked for ".tif should be the name" - lead the label with it.
    const tifName = chunk.textureName ?? '(no texture)';
    const partName = this.partNameFor(idx);
    const stats = `${chunk.vertexCount}v/${chunk.decodedTriangleCount}t`;
    return partName
      ? `${tifName} - ${partName} (#${idx}, ${stats})`
      : `${tifName} (#${idx}, ${stats})`;
  }

  private partNameFor(chunkIdx: number): string | null {
    const scene = this.companionScene();
    const entry = this.selectedEntry();
    if (!scene || !entry) { return null; }
    const sceneNode = findSceneNode(scene, entry.name);
    const parts = collectVisualPartNames(sceneNode);
    return parts[chunkIdx] ?? null;
  }

  activeTriangleCount(): number {
    const mdl = this.model();
    if (!mdl) { return 0; }
    const chunk = this.selectedChunk();
    if (chunk >= 0 && chunk < mdl.chunks.length) { return mdl.chunks[chunk].decodedTriangleCount; }
    return mdl.indices.length / 3;
  }

  paletteLabel(tex: ZedTexture): string {
    if (!tex.header.isIndexed) { return 'n/a (RGBA32)'; }
    return tex.paletteResolved
      ? `texpal_${tex.header.paletteId}`
      : `missing (texpal_${tex.header.paletteId})`;
  }

  private resetPreview(): void {
    this.previewMode.set('none');
    this.texture.set(null);
    this.textureDataUrl.set('');
    this.model.set(null);
    this.previewMessage.set('');
    this.wireframe.set(false);
    this.selectedChunk.set(-1);
    this.paletteOverride.set(null);
    this.pendingArchiveEntry.set(null);
    this.modelRenderer?.setModel(null);
  }

  enterPendingArchive(): void {
    const pending = this.pendingArchiveEntry();
    if (!pending) { return; }
    const bytes = this.nodeCache.get(pending.entryIndex);
    if (!bytes) { return; }
    try {
      const archive = parseZedArchive(bytes);
      this.archiveStack.update(s => [...s, { name: pending.entryName, archive }]);
      this.selectedIndex.set(null);
      this.pendingArchiveEntry.set(null);
      this.resetPreview();
      this.setStatus(`Entered ${pending.entryName} (${archive.entries.length} entries).`, false, false);
      this.loadNestedPalettesForCurrentLayer();
      // If the sub-archive is a *_MDL.ZED, pull TXR/PAL/GEO companions from
      // the parent (root ZDB or another sub-archive) so model chunks can find
      // their textures and palette.
      this.loadMdlCompanionsForCurrentLayer();
    } catch (err) {
      this.previewMessage.set('Nested archive parse failed: ' + (err instanceof Error ? err.message : String(err)));
    }
  }

  private beginPreviewForEntry(index: number, name: string): void {
    const filePath = this.result()?.path;
    if (!filePath) { return; }

    this.resetPreview();
    const looksTif = /\.tif$/i.test(name);
    this.previewMode.set(looksTif ? 'texture' : 'model');
    this.previewMessage.set('Loading preview...');

    // For .tif entries, pixel data lives in the next `texdat` entry; the .tif
    // itself is a 0-byte name marker. Pull the data entry instead.
    const targetIndex = looksTif ? this.findTextureDataIndex(index) : index;
    if (targetIndex < 0) {
      this.previewMessage.set('No texdat entry found after this .tif marker.');
      return;
    }

    // If we're inside a nested archive, the bytes are already in memory.
    const stackBytes = this.bytesFromActiveArchive(targetIndex);
    if (stackBytes) {
      this.decodePayload(index, stackBytes);
      return;
    }

    const cached = this.nodeCache.get(targetIndex);
    if (cached) {
      this.decodePayload(index, cached);
      return;
    }

    // When the loaded file is a ZDB, the full package was prefetched on
    // open. Look the target entry up by leaf name and use those bytes
    // instead of a per-entry readNode round trip — this is what makes the
    // model viewer have the same package context as the HTML viewers.
    if (this.archiveStack().length === 0) {
      const entries = this.result()?.entries ?? [];
      const target = entries.find(e => e.index === targetIndex);
      if (target) {
        const leaf = (target.name.split(/[\\/]/).pop() ?? target.name).toLowerCase();
        const pkgBytes = this.packageEntryBytes.get(leaf);
        if (pkgBytes) {
          this.nodeCache.set(targetIndex, pkgBytes);
          this.decodePayload(index, pkgBytes);
          return;
        }
      }
    }

    this.inflightNode = targetIndex;
    this.nodeAliasForFetch.set(targetIndex, index);
    this.vscode.postMessage({ type: 'readNode', path: filePath, entryIndex: targetIndex });
  }

  private bytesFromActiveArchive(entryIndex: number): Uint8Array | null {
    const stack = this.archiveStack();
    if (stack.length === 0) { return null; }
    const archive = stack[stack.length - 1].archive;
    return archive.entryData.get(entryIndex) ?? null;
  }

  // When we enter a *_TXR.ZED sub-archive, the matching *_PAL.ZED usually
  // lives next to it in the parent layer (sub-archive or the root ZDB). Find
  // and parse it inline so indexed textures inside resolve to colour instead
  // of grayscale.
  private loadNestedPalettesForCurrentLayer(): void {
    // When the full ZDB package was preloaded (applyZdbPackage), the
    // cross-package merged palette map already contains every palette
    // from every _PAL.ZED in the archive. Re-fetching a single-prefix
    // PAL here would OVERWRITE the merged map with one prefix's
    // palettes and break textures from the other prefixes — that was
    // exactly the bug the user hit drilling into WORL_MDL.ZED inside
    // M51.ZDB. Skip when package context is intact.
    if (this.packageEntryBytes.size > 0) { return; }

    const stack = this.archiveStack();
    if (stack.length === 0) { return; }
    const current = stack[stack.length - 1];
    const currentLeafName = (current.name.split(/[\\/]/).pop() ?? current.name);
    const txrMatch = /^(.+?)_TXR(?:\([^)]+\))?\.ZED$/i.exec(currentLeafName);
    if (!txrMatch) { return; }
    const targetPalLeaf = `${txrMatch[1]}_PAL.ZED`.toLowerCase();

    const previousLayer = stack.length >= 2 ? stack[stack.length - 2].archive : null;

    // Case A: parent is another sub-archive. PAL bytes are in memory.
    if (previousLayer) {
      const palEntry = previousLayer.entries.find(
        e => (e.name.split(/[\\/]/).pop() ?? e.name).toLowerCase() === targetPalLeaf,
      );
      if (!palEntry) { return; }
      const palBytes = previousLayer.entryData.get(palEntry.index);
      if (!palBytes || palBytes.length === 0) { return; }
      this.applyPaletteBytesForCurrentLayer(palBytes, palEntry.name, current.archive);
      return;
    }

    // Case B: parent is the root file. We need to async-fetch the PAL entry
    // bytes from the bridge, then parse and apply.
    const filePath = this.result()?.path;
    if (!filePath) { return; }
    const rootEntries = this.result()?.entries ?? [];
    const palEntry = rootEntries.find(
      e => (e.name.split(/[\\/]/).pop() ?? e.name).toLowerCase() === targetPalLeaf,
    );
    if (!palEntry) { return; }

    // Mark this fetch as for "palette purposes" so handleNodeData routes it
    // to applyPaletteBytesForCurrentLayer instead of decodePayload.
    this.pendingPaletteFetch = { rootEntryIndex: palEntry.index, palName: palEntry.name };
    this.vscode.postMessage({ type: 'readNode', path: filePath, entryIndex: palEntry.index });
  }

  private pendingPaletteFetch: { rootEntryIndex: number; palName: string } | null = null;

  private applyPaletteBytesForCurrentLayer(
    palBytes: Uint8Array,
    palName: string,
    currentArchive: InspectedZedArchive,
  ): void {

    try {
      const palArchive = parseZedArchive(palBytes);
      let palettes = parsePaletteEntries(
        palArchive.entries.map(e => ({ name: e.name })),
        i => palArchive.entryData.get(palArchive.entries[i].index) ?? new Uint8Array(0),
      );
      palettes = aliasPalettesForTextureEntries(
        palettes,
        currentArchive.entries.map(e => ({ name: e.name })),
        i => currentArchive.entryData.get(currentArchive.entries[i].index) ?? new Uint8Array(0),
      );
      if (palettes.size > 0) {
        this.palettes.set(palettes);
        this.paletteMessage.set(`Loaded ${palettes.size} palette(s) from ${palName}.`);
      }
    } catch { /* leave existing palettes in place */ }
  }

  // When user enters a *_MDL.ZED sub-archive, pull the matching TXR / PAL /
  // GEO siblings from the parent layer (in-memory sub-archive or async fetch
  // from the root ZDB). Once bytes arrive, populate companionTextures and
  // palettes so model chunks can resolve their textures by .tif name.
  private rootCompanionFetches = new Map<number, 'txr' | 'pal' | 'geo'>();
  private rootCompanionTxrArchive: InspectedZedArchive | null = null;
  private rootCompanionPalArchive: InspectedZedArchive | null = null;

  private loadMdlCompanionsForCurrentLayer(): void {
    // Same guard as loadNestedPalettesForCurrentLayer — when the ZDB
    // package preload already wired up companionTextures, palettes, and
    // companionScene with merged cross-prefix data, this per-layer
    // single-prefix loader would CLOBBER that with one prefix's data
    // (e.g. drilling into M51_MDL.ZED inside M51.ZDB would replace the
    // M51+CLIB+FLIB+BENZ+WEAP merged palette map with M51-only). Skip.
    if (this.packageEntryBytes.size > 0) {
      this.log(`[companion] skipping per-layer load — package context already loaded`);
      return;
    }

    const stack = this.archiveStack();
    if (stack.length === 0) { return; }
    const current = stack[stack.length - 1];
    const currentLeafName = current.name.split(/[\\/]/).pop() ?? current.name;
    const mdlMatch = /^(.+?)_MDL(?:\([^)]+\))?\.ZED$/i.exec(currentLeafName);
    if (!mdlMatch) { return; }
    const prefix = mdlMatch[1];

    const targets: Array<{ kind: 'txr' | 'pal' | 'geo'; leafName: string }> = [
      { kind: 'txr', leafName: `${prefix}_TXR.ZED`.toLowerCase() },
      { kind: 'pal', leafName: `${prefix}_PAL.ZED`.toLowerCase() },
      { kind: 'geo', leafName: `${prefix}_GEO.ZED`.toLowerCase() },
    ];

    this.rootCompanionTxrArchive = null;
    this.rootCompanionPalArchive = null;

    // Search in-memory sub-archives on the stack first (excluding current).
    const previous = stack.length >= 2 ? stack[stack.length - 2].archive : null;
    if (previous) {
      for (const t of targets) {
        const entry = previous.entries.find(
          e => (e.name.split(/[\\/]/).pop() ?? e.name).toLowerCase() === t.leafName,
        );
        if (!entry) { continue; }
        const bytes = previous.entryData.get(entry.index);
        if (!bytes || bytes.length === 0) { continue; }
        this.applyMdlCompanion(t.kind, bytes, entry.name);
      }
      return;
    }

    // Fall back to the root ZDB. We have to async-fetch each entry's bytes.
    const rootEntries = this.result()?.entries ?? [];
    const filePath = this.result()?.path;
    if (!filePath) { return; }
    const requested: string[] = [];
    const missing: string[] = [];
    for (const t of targets) {
      const entry = rootEntries.find(
        e => (e.name.split(/[\\/]/).pop() ?? e.name).toLowerCase() === t.leafName,
      );
      if (!entry) { missing.push(t.leafName); continue; }
      this.rootCompanionFetches.set(entry.index, t.kind);
      this.vscode.postMessage({ type: 'readNode', path: filePath, entryIndex: entry.index });
      requested.push(t.leafName);
    }
    if (requested.length > 0) {
      this.setStatus(
        `Fetching ${requested.length} companion(s): ${requested.join(', ')}${missing.length ? ` (missing: ${missing.join(', ')})` : ''}`,
        false,
        true,
      );
    } else if (missing.length > 0) {
      this.setStatus(`No companion files found in root for ${prefix} (looked for: ${missing.join(', ')}).`, true, false);
    }
  }

  private applyMdlCompanion(kind: 'txr' | 'pal' | 'geo', bytes: Uint8Array, name: string): void {
    try {
      const archive = parseZedArchive(bytes);
      this.setStatus(`Loaded companion ${kind.toUpperCase()} (${name}, ${archive.entries.length} entries).`, false, false);
      if (kind === 'txr') {
        this.rootCompanionTxrArchive = archive;
        // Build the .tif -> texdat map used by buildTifByteMap.
        const map = new Map<string, Uint8Array>();
        const entries = archive.entries;
        for (let i = 0; i < entries.length; i++) {
          if (!/\.tif$/i.test(entries[i].name)) { continue; }
          for (let j = i + 1; j < Math.min(i + 4, entries.length); j++) {
            if (/^texdat$/i.test(entries[j].name)) {
              const data = archive.entryData.get(entries[j].index);
              if (data && data.length > 0) {
                map.set(entries[i].name.toLowerCase(), data);
              }
              break;
            }
            if (/\.tif$/i.test(entries[j].name)) { break; }
          }
        }
        this.companionTextures.set(map);
      } else if (kind === 'pal') {
        this.rootCompanionPalArchive = archive;
      } else if (kind === 'geo') {
        const entries: GeoEntry[] = archive.entries.map(e => ({
          index: e.index,
          name: e.name,
          type: e.type,
          data: bytesToBase64(archive.entryData.get(e.index) ?? new Uint8Array(0)),
        }));
        const scene = parseGeoScene(entries);
        this.companionScene.set(scene);
      }
      // Re-derive palettes whenever TXR or PAL arrives, since aliasing needs both.
      this.rebuildAliasedPalettes(name);
      // Re-apply textures to the model in view.
      this.applyModelToRenderer();
    } catch { /* skip */ }
  }

  private rebuildAliasedPalettes(sourceName: string): void {
    const pal = this.rootCompanionPalArchive;
    if (!pal) { return; }
    let palettes = parsePaletteEntries(
      pal.entries.map(e => ({ name: e.name })),
      i => pal.entryData.get(pal.entries[i].index) ?? new Uint8Array(0),
    );
    const txr = this.rootCompanionTxrArchive;
    if (txr) {
      palettes = aliasPalettesForTextureEntries(
        palettes,
        txr.entries.map(e => ({ name: e.name })),
        i => txr.entryData.get(txr.entries[i].index) ?? new Uint8Array(0),
      );
    }
    if (palettes.size > 0) {
      this.palettes.set(palettes);
      this.paletteMessage.set(`Loaded ${palettes.size} palette(s) from ${sourceName}.`);
      this.setStatus(
        `Palettes ready (${palettes.size}). TXR loaded: ${!!this.rootCompanionTxrArchive}.`,
        false,
        false,
      );
    } else {
      this.setStatus(`Palette PAL parsed but yielded 0 palettes (${sourceName}).`, true, false);
    }
  }

  // For an entry that names a texture (".tif"), find the next entry that holds
  // the pixel bytes. In real UIBF_TXR files the data entry is named "texdat".
  // IMPORTANT: this must read from the *current* layer's entries (sub-archive
  // when drilled in), not the root inspect result.
  private findTextureDataIndex(tifIndex: number): number {
    // Always use the unfiltered list so adjacency to the texdat record is
    // preserved (the filtered table view drops some neighbours).
    const entries = this.rawEntries();
    const tif = entries.find(e => e.index === tifIndex);
    if (!tif) { return tifIndex; }
    // If the .tif entry itself has data, use it.
    if (tif.size > 0) { return tifIndex; }
    // Otherwise look at up to the next 3 entries.
    const ordered = entries.slice().sort((a, b) => a.index - b.index);
    const pos = ordered.findIndex(e => e.index === tifIndex);
    for (let k = pos + 1; k < Math.min(pos + 4, ordered.length); k++) {
      const next = ordered[k];
      if (/\.tif$/i.test(next.name)) { break; }
      if (next.size > 0) { return next.index; }
    }
    return -1;
  }

  private handleNodeData(fetchedIndex: number, base64: string): void {
    if (this.inflightNode === fetchedIndex) { this.inflightNode = null; }
    try {
      const bytes = base64ToBytes(base64);
      this.nodeCache.set(fetchedIndex, bytes);
      const aliasFor = this.nodeAliasForFetch.get(fetchedIndex) ?? fetchedIndex;
      this.nodeAliasForFetch.delete(fetchedIndex);
      if (this.selectedEntry()?.index === aliasFor) {
        this.decodePayload(aliasFor, bytes);
      }
    } catch (err) {
      this.previewMessage.set(err instanceof Error ? err.message : String(err));
    }
  }

  private decodePayload(index: number, bytes: Uint8Array): void {
    const entry = this.selectedEntry();
    if (!entry || entry.index !== index) { return; }

    // RDR typed-table data: ask the bridge to parse and display structured tree.
    if (/\.rdr$/i.test(entry.name)) {
      this.previewMode.set('rdr');
      this.previewMessage.set('Parsing RDR table...');
      this.rdr.set(null);
      const filePath = this.result()?.path;
      const leaf = entry.name.split(/[\\/]/).pop() ?? entry.name;
      // We already have the .rdr bytes here (passed in via `bytes` arg). Skip
      // the backend's container drill-down and parse the bytes directly —
      // this works for any nesting depth, including .rdr-inside-.zar-inside
      // .zdb where the legacy path was failing.
      const base64 = bytesToBase64(bytes);
      this.log(`[RDR] inspecting ${leaf} (${bytes.length}B, in-memory)`);
      this.vscode.postMessage({ type: 'inspectRdrBytes', base64, entryName: leaf });
      return;
      // Note: legacy `inspectRdr` (path + entryName) is still available on
      // the provider for path-only entrypoints, but `decodePayload` is
      // always called with bytes in hand so we prefer the bytes variant.
      void filePath; // unused, retained for future telemetry context
    }

    // AIMAPS.MPS — parsed server-side via the new MPS handler. The .mps file
    // is always at the package root (not nested), so we pass the full path
    // through unchanged. Mirrors the HTML map viewer's parseAiMaps flow.
    if (/\.mps$/i.test(entry.name)) {
      this.previewMode.set('mps');
      this.previewMessage.set('Parsing AI maps...');
      this.mps.set(null);
      const filePath = this.result()?.path;
      const leaf = entry.name.split(/[\\/]/).pop() ?? entry.name;
      if (filePath) {
        this.log(`[MPS] inspecting ${leaf}…`);
        // .mps only lives as a sibling on disk; if the loaded archive itself
        // is the .mps, use its path. Otherwise the path is the parent ZDB
        // and we can't currently extract a single .mps from it — fall back
        // to displaying an error in the table.
        if (/\.mps$/i.test(filePath)) {
          this.vscode.postMessage({ type: 'inspectMps', path: filePath });
        } else {
          this.previewMessage.set(`MPS preview requires the .mps file as the loaded path (got ${filePath.split(/[\\/]/).pop()}).`);
          this.log(`[MPS] skipped — outer archive is not .mps; open ${leaf} directly`);
        }
      }
      return;
    }

    const looksTif = /\.tif$/i.test(entry.name);

    if (looksTif) {
      // Keep texture mode active even on failure so the user sees an error
      // instead of a collapsed pane.
      this.previewMode.set('texture');
      try {
        const tex = parseTexture(bytes, this.palettes(), entry.name.toLowerCase());
        this.texture.set(tex);
        this.textureDataUrl.set(textureToDataUrl(tex));
        this.previewMessage.set(
          tex.header.isIndexed && !tex.paletteResolved
            ? `Palette texpal_${tex.header.paletteId} not loaded`
            : ''
        );
      } catch (err) {
        this.texture.set(null);
        this.textureDataUrl.set('');
        this.previewMessage.set('Texture decode failed: ' + (err instanceof Error ? err.message : String(err)));
      }
      return;
    }

    // If this entry is itself a nested ZED archive (e.g. a row inside a ZDB
    // or ZAR container), drill into it inline. Pushing onto the archive stack
    // swaps the entries table to show the sub-archive's children and routes
    // node reads through the cached bytes.
    if (looksLikeZedContainer(bytes)) {
      // Don't drill automatically - the user might want to inspect siblings
      // in the parent archive first. Park the entry and show an Enter button.
      this.previewMode.set('none');
      this.model.set(null);
      this.pendingArchiveEntry.set({
        entryIndex: index,
        entryName: entry.name,
        size: bytes.length,
      });
      // Cache so Enter Archive doesn't need a re-fetch.
      this.nodeCache.set(index, bytes);
      this.previewMessage.set('');
      return;
    }
    this.pendingArchiveEntry.set(null);

    // Anything else: try to parse as a model. Keep the pane open even when no
    // chunks are found, so the user sees diagnostics rather than nothing.
    this.previewMode.set('model');
    try {
      let mdl = parseModel(bytes);
      let fallbackUsed = false;
      // If the standard chunk format yielded nothing (common for CLIB
      // MESH_* payloads which start with their name as ASCII rather than
      // the WORL_MDL chunk magic), try the VIF / vertex-cloud fallback
      // decoders. They give the user *something* to inspect instead of an
      // empty pane.
      if (mdl.chunks.length === 0) {
        const fallback = decodeFallbackPayload(bytes);
        if (fallback && fallback.chunks.length > 0) {
          mdl = fallback;
          fallbackUsed = true;
        }
      }
      this.currentModelBytes = bytes;
      // Collect the Nxxx_xxx 4-byte child offsets from the parent archive
      // so the smartWorld binding pass has slices to work with.
      this.currentSliceOffsets = this.collectSliceOffsetsForCurrentEntry();
      this.applyCurrentBinding(mdl);
      this.model.set(mdl);
      if (mdl.chunks.length === 0) {
        this.previewMessage.set(
          `No model chunks found (payload ${bytes.length} bytes, first 4 = ${describeMagic(bytes)})`
        );
      } else if (fallbackUsed) {
        this.previewMessage.set(
          `Decoded ${mdl.vertices.length} verts via VIF / vertex-cloud fallback (no standard chunks).`,
        );
        this.log(`[fallback] ${entry.name}: ${mdl.chunks.length} stream(s), ${mdl.vertices.length} verts`);
      } else {
        this.previewMessage.set('');
      }
      setTimeout(() => this.applyModelToRenderer(), 0);
    } catch (err) {
      this.model.set(null);
      this.currentModelBytes = null;
      this.currentSliceOffsets = [];
      this.previewMessage.set('Model decode failed: ' + (err instanceof Error ? err.message : String(err)));
    }
  }

  // Pulls slice offsets from the Nxxx_xxx child stubs of the selected entry.
  // These nodes hold 4-byte payloads = byte offsets into the parent model
  // payload, defining where each material slice begins. Returns [] when no
  // sub-archive context is available (e.g. opened a raw model directly).
  private collectSliceOffsetsForCurrentEntry(): number[] {
    const entry = this.selectedEntry();
    const stack = this.archiveStack();
    if (!entry || stack.length === 0) { return []; }
    const archive = stack[stack.length - 1].archive;
    const modelEntry = archive.entries.find(e => e.index === entry.index);
    if (!modelEntry) { return []; }
    const offsets: number[] = [];
    for (const childIdx of modelEntry.children) {
      const childBytes = archive.entryData.get(childIdx);
      if (!childBytes || childBytes.length !== 4) { continue; }
      const view = new DataView(childBytes.buffer, childBytes.byteOffset, 4);
      offsets.push(view.getUint32(0, true));
    }
    return offsets;
  }

  // Runs the smartWorld / sliceNearest / globalNearest binding pass per the
  // current dropdown selection. `tailScan` (default) is a no-op so the
  // existing chunk.textureName from findTifInRange is preserved.
  private applyCurrentBinding(mdl: ZedModel): void {
    const mode = this.mapMaterialBinding();
    if (mode === 'tailScan') { return; }
    if (!this.currentModelBytes) { return; }
    applyMarkerBinding(mdl, this.currentModelBytes, this.currentSliceOffsets, mode);
  }

  private refreshPreview(): void {
    const entry = this.selectedEntry();
    if (!entry) { return; }
    const bytes = this.nodeCache.get(entry.index);
    if (bytes) { this.decodePayload(entry.index, bytes); }
  }

  private ensureModelRenderer(): void {
    const canvas = this.previewCanvas?.nativeElement;
    if (!canvas) { return; }
    // Angular destroys/recreates the canvas element each time the @if branch
    // re-mounts. If the renderer is still attached to a detached canvas, the
    // new visible one stays blank. Detect and rebind.
    if (this.modelRenderer && this.modelRenderer.attachedCanvas !== canvas) {
      this.modelRenderer.dispose();
      this.modelRenderer = null;
    }
    if (!this.modelRenderer) {
      this.modelRenderer = new ZedModelRenderer(canvas);
    }
  }

  private applyModelToRenderer(): void {
    this.ensureModelRenderer();
    const mdl = this.model();
    if (!this.modelRenderer || !mdl) { return; }

    const entry = this.selectedEntry();
    const palettes = this.palettes();
    const sources = this.buildTifByteMap();

    // PS2 weapon models use a single atlas .tif per weapon - the chunks
    // sample different UV regions of the same sheet. Pick a primary atlas
    // texture for the weapon, then load any per-chunk overrides on top.
    const textures: Record<string, { width: number; height: number; rgba: Uint8Array }> = {};
    let defaultTif: string | null = null;
    if (entry) {
      defaultTif = guessTextureForWeapon(entry.name, sources, mdl);
      if (defaultTif) {
        const bytes = sources.get(defaultTif);
        if (bytes) {
          try {
            // Honour the user's manual palette override if present.
            const aliasForLookup = this.paletteOverride() ?? defaultTif;
            const tex = parseTexture(bytes, palettes, aliasForLookup);
            textures[defaultTif] = { width: tex.header.width, height: tex.header.height, rgba: tex.rgba };
          } catch { defaultTif = null; }
        } else { defaultTif = null; }
      }
    }

    // Per-chunk overrides for chunks that explicitly reference a different tif.
    const needed = new Set<string>();
    for (const c of mdl.chunks) {
      if (c.textureName) { needed.add(c.textureName.toLowerCase()); }
    }
    for (const name of needed) {
      if (textures[name]) { continue; }
      const bytes = sources.get(name);
      if (!bytes) { continue; }
      try {
        const tex = parseTexture(bytes, palettes, name);
        textures[name] = { width: tex.header.width, height: tex.header.height, rgba: tex.rgba };
      } catch { /* skip */ }
    }

    // Map-viewer dropdowns flow through as part of the render options. The
    // wireframe / topdown / lit toggles are derived from `mapRenderMode`;
    // `wireframe()` and `flipY()` from the button bank still win when set so
    // existing weapon previews aren't disturbed.
    const renderMode = this.mapRenderMode();
    const wireframeFromMode = renderMode === 'view3dWire' || renderMode === 'topWire';
    const topDown = renderMode === 'topSolid' || renderMode === 'topWire';
    const unlit = renderMode === 'view3dTextured' || renderMode === 'view3dSolid' || renderMode === 'topSolid';
    const solidColor = renderMode === 'view3dSolid' || renderMode === 'topSolid';

    // The old separate "UV mode" and "Flip Y" toggles are gone — derive
    // their values from the consolidated V-orientation dropdown so the
    // renderer keeps working without duplicate UI:
    //   mapTextureV === 'flipV' -> uvMode = 'flipV'
    //   otherwise               -> uvMode = 'direct'
    const effectiveUvMode = this.mapTextureV() === 'flipV' ? 'flipV' : 'direct';
    this.modelRenderer.setModel(mdl, {
      wireframe: wireframeFromMode,
      flipY: false,
      uvMode: effectiveUvMode,
      chunkIndex: this.selectedChunk(),
      textures,
      defaultTexture: defaultTif,
      triStride: this.mapTriStride(),
      topDown,
      unlit,
      solidColor,
    });
  }

  // Decoded thumbnail grid for the current texture archive. Re-runs when
  // entries / palettes / loaded textures change.
  readonly textureThumbnails = computed<Array<{
    name: string;
    width: number;
    height: number;
    dataUrl: string;
    entryIndex: number;
    paletteResolved: boolean;
  }>>(() => {
    if (!this.isCurrentFileTextureArchive()) { return []; }
    const entries = this.rawEntries();
    const palettes = this.palettes();
    const sources = this.buildTifByteMap();
    const out: Array<{ name: string; width: number; height: number; dataUrl: string; entryIndex: number; paletteResolved: boolean }> = [];
    for (let i = 0; i < entries.length; i++) {
      const entry = entries[i];
      if (!/\.tif$/i.test(entry.name)) { continue; }
      // Locate the bytes either via companion map (keyed by tif name) or by
      // looking at the very next entry that's a real payload.
      let bytes: Uint8Array | undefined = sources.get(entry.name.toLowerCase());
      if (!bytes) {
        for (let j = i + 1; j < Math.min(i + 4, entries.length); j++) {
          if (/\.tif$/i.test(entries[j].name)) { break; }
          if (entries[j].size > 0) {
            bytes = this.bytesFromActiveArchive(entries[j].index) ?? this.nodeCache.get(entries[j].index);
            break;
          }
        }
      }
      if (!bytes || bytes.length < 0x20) { continue; }
      try {
        const tex = parseTexture(bytes, palettes, entry.name);
        out.push({
          name: entry.name,
          width: tex.header.width,
          height: tex.header.height,
          dataUrl: textureToDataUrl(tex),
          entryIndex: entry.index,
          paletteResolved: tex.paletteResolved,
        });
      } catch { /* skip undecodable tile */ }
    }
    return out;
  });

  // Aggregate all known .tif name -> texdat bytes from every source: the root
  // companion _TXR file and any sub-archive currently on the navigation stack.
  private buildTifByteMap(): Map<string, Uint8Array> {
    const map = new Map<string, Uint8Array>();
    const root = this.companionTextures();
    if (root) {
      for (const [name, bytes] of root) { map.set(name, bytes); }
    }
    for (const layer of this.archiveStack()) {
      const archive = layer.archive;
      const entries = archive.entries;
      for (let i = 0; i < entries.length; i++) {
        if (!/\.tif$/i.test(entries[i].name)) { continue; }
        for (let j = i + 1; j < Math.min(i + 4, entries.length); j++) {
          if (/^texdat$/i.test(entries[j].name)) {
            const data = archive.entryData.get(entries[j].index);
            if (data && data.length > 0) {
              map.set(entries[i].name.toLowerCase(), data);
            }
            break;
          }
          if (/\.tif$/i.test(entries[j].name)) { break; }
        }
      }
    }
    return map;
  }

  private findModelTexture(mdl: ZedModel): { width: number; height: number; rgba: Uint8Array } | null {
    if (!mdl.textureRefs.length) { return null; }
    const entries = this.currentEntries();
    for (const ref of mdl.textureRefs) {
      const entry = entries.find(e => e.name.toLowerCase() === ref.toLowerCase());
      if (!entry) { continue; }
      const cached = this.bytesFromActiveArchive(entry.index) ?? this.nodeCache.get(entry.index);
      if (!cached) {
        // Request asynchronously; will apply on next preview refresh.
        const filePath = this.result()?.path;
        if (filePath && this.archiveStack().length === 0) {
          this.vscode.postMessage({ type: 'readNode', path: filePath, entryIndex: entry.index });
        }
        continue;
      }
      try {
        const tex = parseTexture(cached, this.palettes(), ref.toLowerCase());
        return { width: tex.header.width, height: tex.header.height, rgba: tex.rgba };
      } catch { /* try next */ }
    }
    return null;
  }

  private requestPalettesIfNeeded(): void {
    const filePath = this.result()?.path;
    if (!filePath || this.palettesRequested) { return; }
    const name = filePath.split(/[\\/]/).pop() ?? '';
    if (!/\.ZED$/i.test(name)) { return; }
    this.palettesRequested = true;
    // Single combined call: provider returns _TXR / _PAL / _GEO companions in
    // one round trip so the preview can wire textures and chunk names without
    // chasing the file system multiple times.
    this.vscode.postMessage({ type: 'readCompanionFiles', path: filePath });
  }

  // Kick off a full ZDB package fetch. Mirrors the HTML map viewer's
  // RUN.zip preload: we get every top-level entry's bytes in one round
  // trip so textures / palettes / GEO can be wired up before the user
  // even drills into a sub-archive.
  private requestZdbPackageIfNeeded(): void {
    const filePath = this.result()?.path;
    if (!filePath) { return; }
    if (!/\.zdb$/i.test(filePath)) { return; }
    // Skip apache/save ZDBs — they don't carry the sibling-ZED package layout.
    const role = this.result()?.role ?? '';
    if (role === 'apache-patch-archive' || role === 'game-save') { return; }
    this.packageLoading.set(true);
    this.packageMessage.set(`Loading ${filePath.split(/[\\/]/).pop()} package…`);
    this.log(`[package] requesting full package contents for ${filePath.split(/[\\/]/).pop()}`);
    this.vscode.postMessage({ type: 'loadZdbPackage', path: filePath });
  }

  // Decode every top-level node of the just-loaded ZDB. _TXR / _PAL / _GEO
  // ZEDs are parsed inline so model previews resolve textures cross-archive
  // (the gap that caused 64-missing-textures on M51). Other entries are
  // cached by leaf name so drill-ins don't re-fetch them.
  private applyZdbPackage(nodes: Array<{ index: number; name: string; data: string }>): void {
    this.packageEntryBytes.clear();
    const txrArchives: Array<{ name: string; archive: InspectedZedArchive }> = [];
    const palArchives: Array<{ name: string; archive: InspectedZedArchive }> = [];
    const geoArchives: Array<{ name: string; archive: InspectedZedArchive }> = [];

    for (const node of nodes) {
      if (!node.data) { continue; }
      let bytes: Uint8Array;
      try { bytes = base64ToBytes(node.data); }
      catch { continue; }
      const leaf = (node.name.split(/[\\/]/).pop() ?? node.name).toLowerCase();
      this.packageEntryBytes.set(leaf, bytes);

      // Parse each sibling ZED inline. We tolerate parse errors (some
      // entries may not actually be ZED containers — e.g. raw data files).
      const tryParse = (): InspectedZedArchive | null => {
        try { return parseZedArchive(bytes); } catch { return null; }
      };
      if (/_TXR(?:\([^)]+\))?\.zed$/i.test(leaf)) {
        const ar = tryParse(); if (ar) { txrArchives.push({ name: leaf, archive: ar }); }
      } else if (/_PAL(?:\([^)]+\))?\.zed$/i.test(leaf)) {
        const ar = tryParse(); if (ar) { palArchives.push({ name: leaf, archive: ar }); }
      } else if (/_GEO(?:\([^)]+\))?\.zed$/i.test(leaf)) {
        const ar = tryParse(); if (ar) { geoArchives.push({ name: leaf, archive: ar }); }
      }
    }

    // Build the .tif -> texdat map across every TXR archive in the package.
    const tifMap = new Map<string, Uint8Array>();
    for (const { archive } of txrArchives) {
      const entries = archive.entries;
      for (let i = 0; i < entries.length; i++) {
        if (!/\.tif$/i.test(entries[i].name)) { continue; }
        // texdat sibling is the next non-tif data entry within a few slots.
        for (let j = i + 1; j < Math.min(i + 4, entries.length); j++) {
          if (/^texdat$/i.test(entries[j].name)) {
            const data = archive.entryData.get(entries[j].index);
            if (data && data.length > 0) {
              tifMap.set(entries[i].name.toLowerCase(), data);
            }
            break;
          }
          if (/\.tif$/i.test(entries[j].name)) { break; }
        }
      }
    }
    if (tifMap.size > 0) { this.companionTextures.set(tifMap); }

    // Build palettes across every PAL/TXR pair we found. Match by leaf
    // prefix (e.g. M51_TXR.ZED <-> M51_PAL.ZED) so multi-prefix packages
    // alias correctly.
    let mergedPalettes = new Map<string, ZedPalette>();
    for (const pal of palArchives) {
      const prefix = pal.name.replace(/_pal(?:\([^)]+\))?\.zed$/i, '');
      const txrMatch = txrArchives.find(t => t.name.startsWith(prefix + '_txr'));
      let p = parsePaletteEntries(
        pal.archive.entries.map(e => ({ name: e.name })),
        i => pal.archive.entryData.get(pal.archive.entries[i].index) ?? new Uint8Array(0),
      );
      if (txrMatch) {
        p = aliasPalettesForTextureEntries(
          p,
          txrMatch.archive.entries.map(e => ({ name: e.name })),
          i => txrMatch.archive.entryData.get(txrMatch.archive.entries[i].index) ?? new Uint8Array(0),
        );
      }
      for (const [k, v] of p) { mergedPalettes.set(k, v); }
    }
    if (mergedPalettes.size > 0) { this.palettes.set(mergedPalettes); }

    // Parse the first GEO archive into a scene tree (used by the chunk
    // label / part-name lookups).
    for (const geo of geoArchives) {
      try {
        const geoEntries: GeoEntry[] = geo.archive.entries.map(e => ({
          index: e.index,
          name: e.name,
          type: e.type,
          data: bytesToBase64(geo.archive.entryData.get(e.index) ?? new Uint8Array(0)),
        }));
        this.companionScene.set(parseGeoScene(geoEntries));
        break;
      } catch { /* try next */ }
    }

    this.packageLoading.set(false);
    this.packageMessage.set(
      `Package loaded: ${tifMap.size} texture(s), ${mergedPalettes.size} palette(s), ${geoArchives.length} GEO archive(s).`,
    );
    this.log(`[package] ${this.packageEntryBytes.size} entries; textures=${tifMap.size} palettes=${mergedPalettes.size} GEO=${geoArchives.length}`);

    // If the user already clicked a preview before the package finished
    // loading, the initial render had no companion textures. Re-trigger
    // decodePayload from the cached bytes so the freshly-loaded textures
    // flow through the full pipeline (not just the renderer re-bind) —
    // this is what fixes the "have to open views after a fresh open"
    // symptom.
    const entry = this.selectedEntry();
    if (entry) {
      const cached = this.nodeCache.get(entry.index)
        ?? this.bytesFromActiveArchive(entry.index);
      if (cached) {
        this.log(`[package] re-decoding ${entry.name} with fresh package context`);
        this.decodePayload(entry.index, cached);
      } else {
        // No cached bytes for the selected entry — at least re-bind the
        // renderer so any model already in `this.model()` picks up the
        // new texture map.
        this.applyModelToRenderer();
      }
    } else {
      this.applyModelToRenderer();
    }
  }

  // ------------------------------------------------------------------
  // Logging — every diagnostic line the UI emits is mirrored to the
  // "Code Designer ZDB" output channel via the extension provider, so the
  // user has a persistent feed like the HTML viewers' on-page log pane.
  // ------------------------------------------------------------------
  log(text: string, reveal = false): void {
    if (!text) { return; }
    const fileTag = this.result()?.fileName ? `[${this.result()?.fileName}] ` : '';
    this.vscode.postMessage({ type: 'zipperLog', text: fileTag + text, reveal });
  }

  // ------------------------------------------------------------------
  // Sidebar texture grid — click a thumbnail to jump to the matching .tif
  // entry in the current layer's table (which then loads the preview pane).
  // ------------------------------------------------------------------
  onSidebarTextureClick(name: string): void {
    const lower = name.toLowerCase();
    const target = this.currentEntries().find(
      e => e.name.toLowerCase() === lower || e.path.toLowerCase().endsWith('/' + lower),
    );
    if (target) { this.selectedIndex.set(target.index); }
  }
  activeTextureName(): string {
    const tex = this.texture();
    const entry = this.selectedEntry();
    return tex && entry && /\.tif$/i.test(entry.name) ? entry.name.toLowerCase() : '';
  }
  onMapTextureFilterInput(event: Event): void {
    this.mapTextureFilter.set((event.target as HTMLInputElement).value);
  }

  // ------------------------------------------------------------------
  // Map-viewer dropdowns — set the corresponding signal and ping the
  // renderer config so any in-flight model preview re-draws with the new
  // setting. Defaults match the prior behaviour, so weapon previews don't
  // shift unless the user actively changes a setting.
  // ------------------------------------------------------------------
  onMapRenderModeChange(event: Event): void {
    this.mapRenderMode.set((event.target as HTMLSelectElement).value as ReturnType<typeof this.mapRenderMode>);
    this.applyModelToRenderer();
  }
  onMapTriStrideChange(event: Event): void {
    const n = Number((event.target as HTMLSelectElement).value);
    this.mapTriStride.set(Number.isFinite(n) && n > 0 ? n : 1);
    this.applyModelToRenderer();
  }
  onMapMaterialBindingChange(event: Event): void {
    this.mapMaterialBinding.set((event.target as HTMLSelectElement).value as ReturnType<typeof this.mapMaterialBinding>);
    // Re-run the binding pass against the cached model bytes so the dropdown
    // takes effect immediately. We re-parse from raw bytes to discard the
    // previous binding's overrides; otherwise switching back to tailScan
    // would leave stale smartWorld picks in place.
    if (this.currentModelBytes) {
      try {
        const mdl = parseModel(this.currentModelBytes);
        this.applyCurrentBinding(mdl);
        this.model.set(mdl);
      } catch { /* keep old model */ }
    }
    this.applyModelToRenderer();
  }
  onMapTextureVChange(event: Event): void {
    this.mapTextureV.set((event.target as HTMLSelectElement).value as ReturnType<typeof this.mapTextureV>);
    this.applyModelToRenderer();
  }
  onMapUvInterpretationChange(event: Event): void {
    this.mapUvInterpretation.set((event.target as HTMLSelectElement).value as ReturnType<typeof this.mapUvInterpretation>);
    this.applyModelToRenderer();
  }

  // ------------------------------------------------------------------
  // RDR tree — turns the shaped JSON-like root into a flat list of nodes
  // with stable paths so we can collapse/expand by path. We render only
  // the visible-under-expand subset via rdrVisibleRows().
  // ------------------------------------------------------------------
  private buildRdrTreeRoots(root: unknown): RdrTreeNode[] {
    return this.shapeToTree(root, '$', 0, null);
  }
  private shapeToTree(value: unknown, path: string, depth: number, name: string | null): RdrTreeNode[] {
    const nameLabel = name ?? (depth === 0 ? 'root' : '');
    if (value === null || value === undefined) {
      return [{ path, name: nameLabel, kind: 'leaf', value: 'null', depth }];
    }
    if (typeof value === 'string') {
      return [{ path, name: nameLabel, kind: 'string', value, depth }];
    }
    if (typeof value === 'number') {
      const kind: RdrTreeNode['kind'] = Number.isInteger(value) ? 'int' : 'float';
      return [{ path, name: nameLabel, kind, value, depth }];
    }
    if (Array.isArray(value)) {
      const children: RdrTreeNode[] = [];
      for (let i = 0; i < value.length; i++) {
        children.push(...this.shapeToTree(value[i], `${path}.${i}`, depth + 1, `[${i}]`));
      }
      return [{ path, name: nameLabel, kind: 'array', value: `${value.length}`, children, depth }];
    }
    if (typeof value === 'object') {
      const obj = value as Record<string, unknown>;
      const children: RdrTreeNode[] = [];
      for (const [k, v] of Object.entries(obj)) {
        children.push(...this.shapeToTree(v, `${path}.${k}`, depth + 1, k));
      }
      return [{ path, name: nameLabel, kind: 'object', value: `${children.length}`, children, depth }];
    }
    return [{ path, name: nameLabel, kind: 'leaf', value: String(value), depth }];
  }
  toggleRdrNode(path: string): void {
    const next = new Set(this.rdrExpanded());
    if (next.has(path)) { next.delete(path); } else { next.add(path); }
    this.rdrExpanded.set(next);
  }
  isRdrExpanded(path: string): boolean { return this.rdrExpanded().has(path); }
  rdrIsExpandable(node: RdrTreeNode): boolean { return !!(node.children && node.children.length > 0); }

  // Lists rendered when RDR fell back to printable-string scan. The backend
  // shapes `root` as { strings, textureRefs, fileRefs, modelRefs }.
  rdrFallbackLists(): { strings: string[]; textureRefs: string[]; fileRefs: string[]; modelRefs: string[] } | null {
    const r = this.rdr();
    if (!r || !r.fallback) { return null; }
    const root = r.root as Record<string, unknown> | null;
    if (!root) { return null; }
    const grab = (k: string): string[] => Array.isArray(root[k]) ? (root[k] as string[]) : [];
    return {
      strings: grab('strings'),
      textureRefs: grab('textureRefs'),
      fileRefs: grab('fileRefs'),
      modelRefs: grab('modelRefs'),
    };
  }

  // ------------------------------------------------------------------
  // Dump-report buttons — replicate the HTML map viewer's "Dump *"
  // buttons. Output streams to the Code Designer ZDB output channel via
  // log(reveal=true).
  // ------------------------------------------------------------------
  dumpMissingTextures(): void {
    const mdl = this.model();
    if (!mdl) { this.log('[DUMP missing] no model selected', true); return; }
    const textureSources = this.companionTextures();
    const have = textureSources ? new Set([...textureSources.keys()].map(k => k.toLowerCase())) : new Set<string>();
    const missing: string[] = [];
    for (const ref of mdl.textureRefs ?? []) {
      if (!have.has(ref.toLowerCase())) { missing.push(ref); }
    }
    this.log('--- Missing textures ---', true);
    this.log(`Total refs: ${mdl.textureRefs.length}, missing: ${missing.length}`);
    if (missing.length === 0) { this.log('(none)'); }
    for (const r of missing) { this.log(`  ${r}`); }
  }
  dumpMaterialReport(): void {
    const mdl = this.model();
    if (!mdl) { this.log('[DUMP material] no model selected', true); return; }
    this.log('--- Material report ---', true);
    this.log(`Chunks: ${mdl.chunks.length}, vertices: ${mdl.vertices.length}, triangles: ${mdl.indices.length / 3}`);
    this.log(`Binding mode: ${this.mapMaterialBinding()}, UV interpretation: ${this.mapUvInterpretation()}, V mode: ${this.mapTextureV()}`);
    for (let i = 0; i < mdl.chunks.length; i++) {
      const c = mdl.chunks[i];
      this.log(`  chunk[${i}] tex=${c.textureName ?? '(none)'}  v=${c.vertexCount} t=${c.decodedTriangleCount}/${c.triangleCount} scale=${c.scale.toFixed(4)}`);
    }
  }
  dumpGeoReport(): void {
    const scene = this.companionScene();
    if (!scene) { this.log('[DUMP geo] no GEO scene loaded', true); return; }
    this.log('--- GEO report ---', true);
    const entry = this.selectedEntry();
    if (entry) {
      const node = findSceneNode(scene, entry.name);
      const parts = collectVisualPartNames(node);
      this.log(`Selected entry ${entry.name}: ${parts.length} visual part(s)`);
      for (let i = 0; i < parts.length; i++) { this.log(`  part[${i}] = ${parts[i]}`); }
    } else {
      this.log('(no entry selected)');
    }
  }
  // ------------------------------------------------------------------
  // GEO scene composition. Mirrors the HTML map viewer's
  // buildGeoSceneMesh: walks the loaded GEO scene for placed instances,
  // resolves each instance to a model by name across every loaded
  // *_MDL.ZED in the package, applies the instance's 4x4 transform, and
  // merges everything into one big ZedModel. Renders via the existing
  // model preview pipeline.
  // ------------------------------------------------------------------
  composeGeoScene(): void {
    const scene = this.companionScene();
    if (!scene) {
      this.log('[GEO] no GEO scene loaded for this package', true);
      return;
    }
    const entry = this.selectedEntry();
    // Use the currently-selected node as the GEO root when it matches a
    // scene node (e.g. user clicks `worldmodel`); otherwise start at the
    // very top of the GEO tree.
    let root = entry ? findSceneNode(scene, entry.name) : null;
    if (!root) { root = scene.root; }
    const instances = collectGeoInstances(scene, root);
    if (instances.length === 0) {
      this.log(`[GEO] no placeable instances found under ${root.name}`, true);
      return;
    }

    // Build a model lookup map across EVERY *_MDL.ZED in the package by
    // walking each archive's full entry tree (HTML reference behaviour —
    // CLIB / FLIB style packages have model nodes nested as MESH_*
    // descendants of skeleton roots, not at the top level). Each entry
    // contributes multiple lookup keys (lowercased, compacted, stripped
    // of LOD/index suffixes, MESH_-prefixed) so GEO placements with name
    // variants resolve cleanly.
    type ModelHit = { node: { index: number; name: string }; sourceFile: string; bytes: Uint8Array };
    const mdlArchives: Array<{ leaf: string; archive: InspectedZedArchive }> = [];
    for (const [leaf, bytes] of this.packageEntryBytes) {
      if (!/_MDL(?:\([^)]+\))?\.zed$/i.test(leaf)) { continue; }
      try { mdlArchives.push({ leaf, archive: parseZedArchive(bytes) }); }
      catch { /* skip non-archive entries */ }
    }
    const modelLookup = new Map<string, ModelHit[]>();
    for (const { leaf, archive } of mdlArchives) {
      for (const e of archive.entries) {
        if (!e.size) { continue; }
        const bytes = archive.entryData.get(e.index);
        if (!bytes || bytes.length === 0) { continue; }
        const hit: ModelHit = { node: { index: e.index, name: e.name }, sourceFile: leaf, bytes };
        for (const key of instanceNameVariants(e.name)) {
          const list = modelLookup.get(key);
          if (list) { list.push(hit); } else { modelLookup.set(key, [hit]); }
        }
      }
    }

    const preferOrder = ['m51_mdl.zed', 'worl_mdl.zed', 'clib_mdl.zed', 'flib_mdl.zed', 'benz_mdl.zed'];
    const resolve = (name: string): ModelHit | null => {
      for (const key of instanceNameVariants(name)) {
        const hits = modelLookup.get(key);
        if (!hits || hits.length === 0) { continue; }
        for (const file of preferOrder) {
          const h = hits.find(x => x.sourceFile === file);
          if (h) { return h; }
        }
        return hits[0];
      }
      return null;
    };

    // Compose. Each instance contributes a transformed copy of its model's
    // vertices and a re-based index list. Cache parsed models by source so
    // repeated placements don't re-parse.
    const meshCache = new Map<string, ZedModel>();
    const composedVertices: ZedModel['vertices'] = [];
    const composedIndices: number[] = [];
    const composedChunks: ZedModel['chunks'] = [];
    const composedTextureRefs = new Set<string>();
    const missing: string[] = [];
    let rendered = 0;
    let fallbackUsed = 0;

    for (const inst of instances) {
      const hit = resolve(inst.modelName);
      if (!hit) { missing.push(inst.modelName); continue; }
      const cacheKey = `${hit.sourceFile}|${hit.node.name}|${this.mapMaterialBinding()}`;
      let mdl = meshCache.get(cacheKey);
      if (!mdl) {
        try { mdl = parseModel(hit.bytes); } catch { mdl = null as unknown as ZedModel; }
        // If the resolved model parses to nothing standard (CLIB-style
        // payload), try the VIF/cloud fallback so the instance still
        // contributes geometry to the composed scene.
        if (!mdl || mdl.chunks.length === 0) {
          const fb = decodeFallbackPayload(hit.bytes);
          if (fb && fb.chunks.length > 0) {
            mdl = fb;
            fallbackUsed++;
          } else if (!mdl) {
            continue;
          }
        }
        // Run the marker binding pass against this payload BEFORE we merge
        // its chunks into the scene. Composed chunks have mangled offsets,
        // so binding has to happen here — the per-payload bytes are the
        // only context where chunk.offset is still meaningful.
        const bindMode = this.mapMaterialBinding();
        if (bindMode !== 'tailScan' && mdl.chunks.length > 0) {
          try { applyMarkerBinding(mdl, hit.bytes, [], bindMode); }
          catch { /* keep tail-scan names */ }
        }
        meshCache.set(cacheKey, mdl);
      }

      const vertexBase = composedVertices.length;
      const chunkBase = composedChunks.length;
      for (const v of mdl.vertices) {
        const [tx, ty, tz] = transformPoint(inst.matrix, v.x, v.y, v.z);
        composedVertices.push({ ...v, x: tx, y: ty, z: tz });
      }
      for (const idx of mdl.indices) { composedIndices.push(vertexBase + idx); }
      for (const c of mdl.chunks) {
        composedChunks.push({ ...c, offset: c.offset + chunkBase * 0x80 });
      }
      for (const ref of mdl.textureRefs) { composedTextureRefs.add(ref); }
      rendered++;
    }

    if (composedVertices.length === 0) {
      this.log(`[GEO] composition produced no vertices (instances=${instances.length}, missing=${missing.length})`, true);
      return;
    }

    let radius = 0;
    for (const v of composedVertices) {
      const d = Math.hypot(v.x, v.y, v.z);
      if (d > radius) { radius = d; }
    }
    const composedModel: ZedModel = {
      chunks: composedChunks,
      vertices: composedVertices,
      indices: composedIndices,
      textureRefs: [...composedTextureRefs],
      boundingRadius: radius || 1,
    };

    this.model.set(composedModel);
    this.previewMode.set('model');
    this.currentModelBytes = null; // composed scene has no single payload
    this.currentSliceOffsets = [];
    setTimeout(() => this.applyModelToRenderer(), 0);

    const uniqueMissing = [...new Set(missing.map(m => m.toLowerCase()))];
    this.log('--- GEO scene composed ---', true);
    this.log(`Root: ${root.name}, instances=${instances.length}, rendered=${rendered}, missingModels=${uniqueMissing.length}, fallbackDecodes=${fallbackUsed}`);
    this.log(`Composed model: ${composedVertices.length} verts, ${composedIndices.length / 3 | 0} tris, ${composedChunks.length} chunks across ${meshCache.size} unique source model(s)`);
    if (uniqueMissing.length) {
      this.log(`Missing model refs: ${uniqueMissing.slice(0, 60).join(', ')}${uniqueMissing.length > 60 ? '…' : ''}`);
    }
  }

  // ------------------------------------------------------------------
  // Hierarchical model decode. Walks the currently-selected node's
  // subtree in the active archive, decoding chunks from EVERY child
  // node that contains them and applying any per-node transform matrix
  // pulled from the node's data. Produces one composed mesh — required
  // for CLIB / player rendering, where the actual geometry lives in
  // nested MESH_* children rather than the root payload.
  // (HTML map viewer's decodeHierarchicalModelNode parity.)
  // ------------------------------------------------------------------
  composeHierarchicalModel(): void {
    const entry = this.selectedEntry();
    const stack = this.archiveStack();
    if (!entry || stack.length === 0) {
      this.log('[hierarchical] requires drilling into an archive first', true);
      return;
    }
    const archive = stack[stack.length - 1].archive;
    const rootEntry = archive.entries.find(e => e.index === entry.index);
    if (!rootEntry) {
      this.log(`[hierarchical] entry ${entry.name} not found in current archive`, true);
      return;
    }

    const composedVertices: ZedModel['vertices'] = [];
    const composedIndices: number[] = [];
    const composedChunks: ZedModel['chunks'] = [];
    const composedRefs = new Set<string>();
    let payloadsWithChunks = 0;

    const visit = (entryIndex: number, parentMatrix: number[]) => {
      const ent = archive.entries.find(e => e.index === entryIndex);
      if (!ent) { return; }
      const bytes = archive.entryData.get(ent.index);
      const localMatrix = bytes ? tryReadLocalMatrix(bytes) : identityMatrix();
      const matrix = multiplyMatrices(parentMatrix, localMatrix);

      if (bytes && bytes.length > 0) {
        let local: ZedModel | null = null;
        try { local = parseModel(bytes); } catch { local = null; }
        // CLIB / similar payloads don't use the standard chunk format —
        // fall back to VIF / vertex-cloud decoding so the hierarchical
        // walk actually picks up character meshes etc.
        if (!local || local.chunks.length === 0) {
          local = decodeFallbackPayload(bytes);
        }
        if (local && local.chunks.length > 0) {
          // Apply the user's material-binding mode to THIS payload's
          // chunks while we still have the right bytes in hand. Composed
          // chunk offsets are mangled across payloads so we can't run
          // binding after merging — this is the only place it works.
          const bindMode = this.mapMaterialBinding();
          if (bindMode !== 'tailScan') {
            try { applyMarkerBinding(local, bytes, [], bindMode); }
            catch { /* keep tail-scan names */ }
          }
          payloadsWithChunks++;
          const vertexBase = composedVertices.length;
          const chunkBase = composedChunks.length;
          for (const v of local.vertices) {
            const [tx, ty, tz] = transformPoint(matrix, v.x, v.y, v.z);
            composedVertices.push({ ...v, x: tx, y: ty, z: tz });
          }
          for (const idx of local.indices) { composedIndices.push(vertexBase + idx); }
          for (const c of local.chunks) {
            composedChunks.push({ ...c, offset: c.offset + chunkBase * 0x80 });
          }
          for (const ref of local.textureRefs) { composedRefs.add(ref); }
        }
      }

      for (const childIdx of ent.children) { visit(childIdx, matrix); }
    };
    visit(rootEntry.index, identityMatrix());

    if (composedVertices.length === 0) {
      this.log(`[hierarchical] ${entry.name}: no chunks found in subtree`, true);
      return;
    }

    let radius = 0;
    for (const v of composedVertices) {
      const d = Math.hypot(v.x, v.y, v.z);
      if (d > radius) { radius = d; }
    }
    this.model.set({
      chunks: composedChunks,
      vertices: composedVertices,
      indices: composedIndices,
      textureRefs: [...composedRefs],
      boundingRadius: radius || 1,
    });
    this.previewMode.set('model');
    this.currentModelBytes = null;
    this.currentSliceOffsets = [];
    setTimeout(() => this.applyModelToRenderer(), 0);
    this.log(`[hierarchical] ${entry.name}: ${payloadsWithChunks} payload(s) with chunks, ${composedVertices.length} verts, ${composedIndices.length / 3 | 0} tris`, true);
  }

  // Walks the loaded GEO scene like composeGeoScene does, but instead of
  // building a mesh, logs every instance with its resolved/missing status,
  // the variant keys we tried, and the resolved MDL source file. This is
  // the fastest way to figure out why so many GEO instances aren't matching
  // any of the package's MDL nodes.
  dumpGeoInstances(): void {
    const scene = this.companionScene();
    if (!scene) {
      this.log('[GEO instances] no GEO scene loaded for this package', true);
      return;
    }
    const entry = this.selectedEntry();
    let root = entry ? findSceneNode(scene, entry.name) : null;
    if (!root) { root = scene.root; }
    const instances = collectGeoInstances(scene, root);

    // Build the same lookup map composeGeoScene uses so the report
    // reflects exactly what compose will do.
    type ModelHit = { sourceFile: string; nodeName: string };
    const modelLookup = new Map<string, ModelHit[]>();
    for (const [leaf, bytes] of this.packageEntryBytes) {
      if (!/_MDL(?:\([^)]+\))?\.zed$/i.test(leaf)) { continue; }
      let archive: InspectedZedArchive;
      try { archive = parseZedArchive(bytes); } catch { continue; }
      for (const e of archive.entries) {
        if (!e.size) { continue; }
        for (const key of instanceNameVariants(e.name)) {
          const list = modelLookup.get(key);
          if (list) { list.push({ sourceFile: leaf, nodeName: e.name }); }
          else { modelLookup.set(key, [{ sourceFile: leaf, nodeName: e.name }]); }
        }
      }
    }

    let resolved = 0, missing = 0;
    const missingNames = new Set<string>();
    const resolutionDetails: string[] = [];

    for (const inst of instances) {
      const variants = instanceNameVariants(inst.modelName);
      let hit: ModelHit | null = null;
      for (const key of variants) {
        const hits = modelLookup.get(key);
        if (hits && hits.length > 0) { hit = hits[0]; break; }
      }
      if (hit) {
        resolved++;
        if (resolutionDetails.length < 60) {
          resolutionDetails.push(`  OK   ${inst.modelName.padEnd(40)} -> ${hit.sourceFile} / ${hit.nodeName}`);
        }
      } else {
        missing++;
        missingNames.add(inst.modelName.toLowerCase());
        if (resolutionDetails.length < 60) {
          resolutionDetails.push(`  MISS ${inst.modelName.padEnd(40)} (variants tried: ${variants.slice(0, 4).join(', ')}${variants.length > 4 ? '…' : ''})`);
        }
      }
    }

    this.log('--- GEO instance dump ---', true);
    this.log(`Root: ${root.name}  instances=${instances.length}  resolved=${resolved}  missing=${missing}`);
    this.log(`Package MDL archives: ${[...this.packageEntryBytes.keys()].filter(k => /_MDL(?:\([^)]+\))?\.zed$/i.test(k)).join(', ')}`);
    this.log(`First ${Math.min(resolutionDetails.length, 60)} instance(s):`);
    for (const line of resolutionDetails) { this.log(line); }
    if (missingNames.size > 0) {
      const sorted = [...missingNames].sort();
      this.log(`Unique missing names (${missingNames.size}):`);
      this.log(`  ${sorted.slice(0, 80).join(', ')}${sorted.length > 80 ? '…' : ''}`);
    }
  }

  dumpRdrReport(): void {
    const r = this.rdr();
    if (!r) { this.log('[DUMP rdr] no RDR loaded', true); return; }
    this.log('--- RDR report ---', true);
    this.log(`Source: ${r.sourcePath}${r.entry ? ` / ${r.entry}` : ''}`);
    if (r.fallback) {
      this.log(`(fallback) ${r.parseError ?? ''}`);
      const lists = this.rdrFallbackLists();
      if (lists) {
        this.log(`Strings: ${lists.strings.length}, textureRefs: ${lists.textureRefs.length}, fileRefs: ${lists.fileRefs.length}, modelRefs: ${lists.modelRefs.length}`);
        if (lists.textureRefs.length) { this.log('Texture refs:'); for (const s of lists.textureRefs) { this.log('  ' + s); } }
        if (lists.fileRefs.length) { this.log('File refs:'); for (const s of lists.fileRefs) { this.log('  ' + s); } }
        if (lists.modelRefs.length) { this.log('Model refs:'); for (const s of lists.modelRefs.slice(0, 80)) { this.log('  ' + s); } }
      }
    } else {
      this.log(`Version ${r.version}, atoms ${r.atomCount}, pool ${r.stringPoolSize}B`);
      try { this.log(JSON.stringify(r.root, null, 2)); } catch { this.log('(unable to stringify root)'); }
    }
  }
}

// Match a weapon entry name to the right atlas texture filename.
// Strategy (in order):
//   1. Rules table - picks the PRIMARY atlas for known weapons. Important for
//      multi-texture weapons like m4Acarbine (uses both m4.tif for the body
//      and mark03.tif for the scope) where chunk-frequency would pick the
//      wrong one as default.
//   2. Most-frequent chunk-embedded .tif - fallback when rules don't match
//      (less-known weapons / vehicles / etc.).
//   3. Whole-payload ref scan - last resort.
//   Per-chunk overrides are still applied separately by applyModelToRenderer
//   so individual chunks can use their own texture (e.g. the scope chunk
//   uses mark03 even when the default atlas is m4).
function guessTextureForWeapon(
  weaponName: string,
  sources: Map<string, Uint8Array>,
  model: { chunks: Array<{ textureName: string | null }>; textureRefs: string[] },
): string | null {
  const lower = weaponName.toLowerCase();

  // Specific matches FIRST so longer / more-specific weapon names win over
  // generic substrings (e.g. "mp5sd" must be checked before "mp5").
  const rules: Array<[string, string]> = [
    // Assault / battle rifles
    ['m4acarbine_203', 'm4.tif'],
    ['m4acarbine_sd', 'm4.tif'],
    ['m4acarbine', 'm4.tif'],
    ['m16_m203', 'm16.tif'],
    ['m16a2', 'm16.tif'],
    ['ak105', 'ak47.tif'],
    ['ak47', 'ak47.tif'],
    ['aks74', 'aks74_s.tif'],
    ['m14_gun', 'm14.tif'],
    ['m14', 'm14.tif'],
    ['steyr_aug9', 'steyr.tif'],
    ['steyr', 'steyr.tif'],
    ['iw80a2', 'm16.tif'],
    ['ra14_groza', 'RA14_groza.tif'],
    ['bmmg_f2k', 'BMMG_f2k.tif'],
    // SMGs
    ['mp5sd', 'mp5.tif'],
    ['mp5', 'mp5.tif'],
    ['fnp90', 'fnp90.tif'],
    ['uzi', 'uzi.tif'],
    ['hk5k', 'mp5.tif'],
    ['sp10_gyuraz', 'SP10_gyuraz.tif'],
    // Sniper rifles
    ['barretm82a1', 'barretm82.tif'],
    ['barret', 'barretm82.tif'],
    ['mcmillanm87', 'mcmillan_m87.tif'],
    ['mcmillan', 'mcmillan_m87.tif'],
    ['stoner_sr25', 'stoner_sr25.tif'],
    ['remington700', 'remington700.tif'],
    ['dragunov', 'drag.tif'],
    // LMGs
    ['stoner_m63a', 'stoner_m63a.tif'],
    ['m60e', 'm60.tif'],
    ['sig_commando', 'sig_commando.tif'],
    // Shotguns
    ['remington870', 'remington870.tif'],
    ['spas12', 'spas12.tif'],
    ['jackhammer', 'jackhammer.tif'],
    // Pistols
    ['m11_p228', 'M11_p228.tif'],
    ['a_mark23sd', 'mark23.tif'],
    ['a_mark23', 'mark23.tif'],
    ['mark23', 'mark23.tif'],
    ['fiveseven', 'fiveseven.tif'],
    ['glock18', 'glock18.tif'],
    ['baretta_m9', 'baretta_m9.tif'],
    ['baretta', 'baretta_m9.tif'],
    ['desert_eagle', 'deserteagle.tif'],
    ['hkp9s', 'p9s.tif'],
    ['sig226', 'sig_p226.tif'],
    // Launchers
    ['m4acarbine_203', 'mark03.tif'],
    ['m203', 'mark03.tif'],
    ['m79', 'm79.tif'],
    ['mglmk1', 'mglmk1.tif'],
    ['mglmk2', 'mglmk2.tif'],
    ['at4_heat', 'at4_text.tif'],
    ['at4', 'at4_text.tif'],
    ['rpg7', 'mp5.tif'],
    ['rpgrenade', 'HEgrenade.tif'],
    // Explosives / mines / utility
    ['pmn_mine', 'pmn_mine.tif'],
    ['pmn', 'pmn_mine.tif'],
    ['c4', 'c401.tif'],
    ['claymore', 'claymore.tif'],
    ['flashbang', 'flashbang.tif'],
    ['hegrenade', 'HEgrenade.tif'],
    ['a_smoke_grenade', 'smoke_grenade.tif'],
    ['smoke', 'smoke_grenade.tif'],
    ['grenade', 'HEgrenade.tif'],
    ['detonator', 'a_buttonpanel01.tif'],
    ['fiftycal_box', '50cal_box_top.tif'],
    ['artillery_shell', 'HEgrenade.tif'],
    ['footbomb', 'HEgrenade.tif'],
    ['bleu_chem', 'a_buttonpanel01.tif'],
    // Misc
    ['laser_designator', 'laser_designator.tif'],
  ];

  for (const [needle, tif] of rules) {
    if (lower.includes(needle) && sources.has(tif.toLowerCase())) {
      return tif.toLowerCase();
    }
  }

  // Fallback: most-used chunk-embedded ref.
  const counts = new Map<string, number>();
  for (const c of model.chunks) {
    if (!c.textureName) { continue; }
    const key = c.textureName.toLowerCase();
    if (!sources.has(key)) { continue; }
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  let chunkBest: string | null = null;
  let chunkCount = 0;
  for (const [name, count] of counts) {
    if (count > chunkCount) { chunkCount = count; chunkBest = name; }
  }
  if (chunkBest) { return chunkBest; }

  // Last resort: whole-payload ref scan.
  for (const ref of model.textureRefs ?? []) {
    if (sources.has(ref.toLowerCase())) { return ref.toLowerCase(); }
  }

  return null;
}

function bytesToBase64(bytes: Uint8Array): string {
  let s = '';
  const CHUNK = 0x8000;
  for (let i = 0; i < bytes.length; i += CHUNK) {
    s += String.fromCharCode.apply(null, Array.from(bytes.subarray(i, i + CHUNK)));
  }
  return btoa(s);
}

// Matrix helpers used by composeHierarchicalModel + GEO scene composition.
// Direct ports of the HTML map viewer's tryReadLocalMatrix / multiplyMatrices
// / identityMatrix — same column-major convention so the two composition
// paths interoperate.
function identityMatrix(): number[] {
  return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
}
function multiplyMatrices(a: number[], b: number[]): number[] {
  const out = new Array<number>(16).fill(0);
  for (let row = 0; row < 4; row++) {
    for (let col = 0; col < 4; col++) {
      out[col * 4 + row] =
        a[0 * 4 + row] * b[col * 4 + 0] +
        a[1 * 4 + row] * b[col * 4 + 1] +
        a[2 * 4 + row] * b[col * 4 + 2] +
        a[3 * 4 + row] * b[col * 4 + 3];
    }
  }
  return out;
}
// Reads the first 16 float32s of a node payload as a 4x4 matrix and rejects
// it if any sanity check fails (scale, translation magnitude, bottom row
// near identity). When the data doesn't look like a transform we fall back
// to identity so the chunks aren't accidentally warped into oblivion.
function tryReadLocalMatrix(bytes: Uint8Array): number[] {
  if (!bytes || bytes.length < 0x40) { return identityMatrix(); }
  if (bytes.length > 0x90) { return identityMatrix(); }
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const m: number[] = [];
  for (let i = 0; i < 16; i++) { m.push(view.getFloat32(i * 4, true)); }
  if (!m.every(Number.isFinite)) { return identityMatrix(); }
  const sx = Math.hypot(m[0], m[1], m[2]);
  const sy = Math.hypot(m[4], m[5], m[6]);
  const sz = Math.hypot(m[8], m[9], m[10]);
  const saneScale = sx > 0.001 && sy > 0.001 && sz > 0.001 && sx < 500 && sy < 500 && sz < 500;
  const saneBottom = Math.abs(m[3]) < 0.01 && Math.abs(m[7]) < 0.01 && Math.abs(m[11]) < 0.01 && Math.abs(Math.abs(m[15]) - 1) < 0.25;
  const saneTranslate = Math.abs(m[12]) < 100000 && Math.abs(m[13]) < 100000 && Math.abs(m[14]) < 100000;
  return saneScale && saneBottom && saneTranslate ? m : identityMatrix();
}

function describeMagic(bytes: Uint8Array): string {
  if (bytes.length < 4) { return '(empty)'; }
  return Array.from(bytes.subarray(0, 4))
    .map(b => b.toString(16).padStart(2, '0').toUpperCase())
    .join(' ');
}

function textureToDataUrl(tex: ZedTexture): string {
  const canvas = document.createElement('canvas');
  canvas.width = tex.header.width;
  canvas.height = tex.header.height;
  const ctx = canvas.getContext('2d');
  if (!ctx) { return ''; }
  const image = ctx.createImageData(tex.header.width, tex.header.height);
  image.data.set(tex.rgba);
  ctx.putImageData(image, 0, 0);
  return canvas.toDataURL('image/png');
}
