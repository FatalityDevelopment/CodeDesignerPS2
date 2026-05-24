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
import { ZedModel, looksLikeZedContainer, parseModel } from './zed-model-parser';
import { InspectedZedArchive, parseZedArchive } from './zed-archive-parser';
import { ZedModelRenderer } from './zed-model-renderer';
import {
  GeoEntry,
  GeoScene,
  collectVisualPartNames,
  findSceneNode,
  parseGeoScene,
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

type PreviewMode = 'none' | 'texture' | 'model' | 'rdr';

interface RdrResult {
  sourcePath: string;
  entry?: string;
  version: number;
  stringPoolSize: number;
  atomCount: number;
  root: unknown;
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
                  <pre style="margin:0;font-family:var(--vscode-editor-font-family, monospace);font-size:12px;color:var(--vscode-editor-foreground, #d4d4d4);white-space:pre-wrap;word-break:break-word">{{ rdrJson() }}</pre>
                </div>
                <div class="zip-preview-info">
                  @if (rdr(); as r) {
                    <div><span>RDR version</span><strong>{{ r.version }}</strong></div>
                    <div><span>Atoms</span><strong>{{ r.atomCount }}</strong></div>
                    <div><span>String pool</span><strong>{{ r.stringPoolSize }} bytes</strong></div>
                    @if (r.entry) {
                      <div><span>Entry</span><strong>{{ r.entry }}</strong></div>
                    }
                  } @else {
                    <div><span>Status</span><strong>{{ previewMessage() || 'Loading...' }}</strong></div>
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
                    <div>
                      <span>Chunks</span><strong>{{ mdl.chunks.length }}</strong>
                    </div>
                    <div>
                      <span>Vertices / Triangles</span><strong>{{ activeVertexCount() }} / {{ activeTriangleCount() }}</strong>
                    </div>
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
                    <div>
                      <span>UV mode</span>
                      <select class="zip-chunk-select" [value]="uvMode()" (change)="onUvModeChange($event)">
                        <option value="direct">direct</option>
                        <option value="flipV">flip V</option>
                        <option value="swap">swap U/V</option>
                        <option value="swapFlipV">swap U/V + flip V</option>
                      </select>
                    </div>
                    @if (availablePalettes().length > 0) {
                      <div>
                        <span>Palette override</span>
                        <select class="zip-chunk-select" [value]="paletteOverride() ?? ''" (change)="onPaletteOverrideChange($event)">
                          <option value="">(auto)</option>
                          @for (key of availablePalettes(); track key) {
                            <option [value]="key">{{ key }}</option>
                          }
                        </select>
                      </div>
                    }
                    <div style="display:flex;gap:6px;flex-wrap:wrap">
                      <button class="zip-btn" (click)="toggleWireframe()">
                        <app-icon name="grid" [size]="13" />
                        {{ wireframe() ? 'Solid' : 'Wireframe' }}
                      </button>
                      <button class="zip-btn" (click)="toggleFlipY()">
                        <app-icon name="refreshcw" [size]="13" />
                        {{ flipY() ? 'Unflip Y' : 'Flip Y' }}
                      </button>
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
        this.resetPreview();
        this.setStatus(this.result()?.summary ?? 'File loaded.', false, false);
        this.requestPalettesIfNeeded();
        break;
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
        if (result) {
          this.previewMessage.set(
            `RDR v${result.version}: ${result.atomCount} atoms, ${result.stringPoolSize}B strings.`,
          );
        }
        break;
      }
      case 'rdrError':
        this.previewMessage.set('RDR parse failed: ' + ((data['message'] as string) ?? 'unknown'));
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
      if (filePath) {
        // The bridge can drill into ZAR/ZED/ZDB to extract the .rdr by leaf
        // name when we're inside a sub-archive.
        const leaf = entry.name.split(/[\\/]/).pop() ?? entry.name;
        this.vscode.postMessage({ type: 'inspectRdr', path: filePath, entryName: leaf });
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
      const mdl = parseModel(bytes);
      this.model.set(mdl);
      if (mdl.chunks.length === 0) {
        this.previewMessage.set(
          `No model chunks found (payload ${bytes.length} bytes, first 4 = ${describeMagic(bytes)})`
        );
      } else {
        this.previewMessage.set('');
      }
      setTimeout(() => this.applyModelToRenderer(), 0);
    } catch (err) {
      this.model.set(null);
      this.previewMessage.set('Model decode failed: ' + (err instanceof Error ? err.message : String(err)));
    }
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

    this.modelRenderer.setModel(mdl, {
      wireframe: this.wireframe(),
      flipY: this.flipY(),
      uvMode: this.uvMode(),
      chunkIndex: this.selectedChunk(),
      textures,
      defaultTexture: defaultTif,
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
