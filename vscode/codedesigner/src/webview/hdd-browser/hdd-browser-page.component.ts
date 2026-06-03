import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  ViewChild,
  ViewEncapsulation,
  computed,
  effect,
  signal,
} from '@angular/core';
import { IconComponent } from '../shared/icon.component';
import { PageHeaderComponent } from '../shared/page-header.component';
import { getVsCodeApi } from '../shared/vscode-api';
import {
  Ps2Icon,
  Ps2IconSys,
  base64ToBytes,
  parseIcon,
  parseIconSys,
  uniqueIconFileNames,
} from '../memory-card-browser/ps2-icon-parser';
import { Ps2IconRenderer } from '../memory-card-browser/ps2-icon-renderer';
import './hdd-browser-page.css';

interface HddFileEntryInfo {
  Name: string;
  Size: number;
  IsDirectory: boolean;
  Modified: string;
  Source?: string;
}

interface HddPartitionInfo {
  Id: string;
  Name: string;
  StartSector: number;
  TotalBytes: number;
  TypeName: string;
  FileCount: number;
  Created: string;
  Files: HddFileEntryInfo[];
}

interface HddOperationResult {
  outputDirectory?: string;
  outputPath?: string;
  count?: number;
}

type IconAction = 'normal' | 'copy' | 'delete';

interface IconSlot {
  action: IconAction;
  label: string;
  fileName: string;
  icon?: Ps2Icon;
  error?: string;
}

@Component({
  selector: 'app-hdd-browser-page',
  standalone: true,
  imports: [IconComponent, PageHeaderComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="hdb-shell">
      <app-page-header icon="harddrive" title="HDD" subtitle="PS2 hard drive image browser" />
      <div class="hdb-toolbar">
        <div class="hdb-title">
          <app-icon name="harddrive" [size]="16" />
          <span>PS2 HDD Browser</span>
        </div>
        <div class="hdb-toolbar-spacer"></div>
        <button class="hdb-btn hdb-primary" [disabled]="busy()" (click)="openHdd()">
          <app-icon name="harddrive" [size]="14" /> Open
        </button>
        <button class="hdb-btn" [disabled]="busy()" (click)="newHdd()">
          <app-icon name="plus" [size]="14" /> New
        </button>
        <button class="hdb-btn" [disabled]="!hddPath() || busy()" (click)="refresh()">
          <app-icon name="refreshcw" [size]="14" /> Refresh
        </button>
        <button class="hdb-btn" [disabled]="!selectedPartition() || busy()" (click)="exportSelectedPartition()">
          <app-icon name="download" [size]="14" /> Export
        </button>
        <button class="hdb-btn hdb-danger" [disabled]="!selectedPartition() || busy()" (click)="deleteSelectedPartition()">
          <app-icon name="trash2" [size]="14" /> Delete
        </button>
      </div>

      @if (statusMsg()) {
        <button
          type="button"
          class="hdb-status"
          [class.hdb-status-error]="statusIsError()"
          [class.hdb-status-busy]="busy()"
          [disabled]="!lastRevealPath()"
          (click)="revealLast()">
          {{ statusMsg() }}
        </button>
      }

      <div class="hdb-main">
        <aside class="hdb-sidebar">
          <div class="hdb-path" [title]="hddPath()">
            {{ hddPath() || 'No HDD image selected' }}
          </div>

          <div class="hdb-meta">
            <div>
              <span>Partitions</span>
              <strong>{{ partitions().length }}</strong>
            </div>
            <div>
              <span>Files</span>
              <strong>{{ totalRootFiles() }}</strong>
            </div>
            <div>
              <span>Size</span>
              <strong>{{ formatBytes(totalBytes()) }}</strong>
            </div>
            <div>
              <span>Icons</span>
              <strong>{{ iconPartitionCount() }}</strong>
            </div>
          </div>

          <label class="hdb-search">
            <app-icon name="search" [size]="14" />
            <input
              type="text"
              placeholder="Search partitions"
              [value]="search()"
              (input)="onSearch($event)" />
          </label>

          <div class="hdb-partition-list">
            @if (!busy() && partitions().length === 0) {
              <div class="hdb-empty">Open an HDD.raw image to browse partitions.</div>
            }
            @for (part of filteredPartitions(); track part.Id) {
              <button
                type="button"
                class="hdb-partition-row"
                [class.active]="selectedPartitionId() === part.Id"
                (click)="selectPartition(part.Id)">
                <span class="hdb-partition-name">{{ partitionTitle(part) }}</span>
                <span class="hdb-partition-meta">
                  {{ part.TypeName }} | {{ formatBytes(part.TotalBytes) }} | {{ fileCountLabel(part) }}
                </span>
              </button>
            }
          </div>
        </aside>

        <section class="hdb-content">
          @if (!selectedPartition()) {
            <div class="hdb-empty hdb-empty-center">
              <app-icon name="harddrive" [size]="34" />
              <span>Select an HDD partition to inspect files and icon metadata.</span>
            </div>
          } @else {
            <div class="hdb-preview-band">
              <div class="hdb-canvas-area">
                <canvas #previewCanvas class="hdb-canvas"></canvas>
                @if (!activeIcon()) {
                  <div class="hdb-canvas-empty">
                    {{ canvasMessage() }}
                  </div>
                }
                @if (iconSlots().length > 1) {
                  <div class="hdb-icon-tabs">
                    @for (slot of iconSlots(); track slot.action) {
                      <button
                        type="button"
                        class="hdb-icon-tab"
                        [class.active]="activeSlot()?.action === slot.action"
                        [disabled]="!slot.icon"
                        [title]="slot.fileName"
                        (click)="setIconSlot($index)">
                        {{ slot.label }}
                      </button>
                    }
                  </div>
                }
              </div>

              <div class="hdb-details">
                <div>
                  <div class="hdb-details-title">{{ partitionTitle(selectedPartition()!) }}</div>
                  <div class="hdb-details-subtitle">{{ selectedPartition()?.Id }}</div>
                </div>

                <div class="hdb-detail-grid">
                  <div><span>Type</span><strong>{{ selectedPartition()?.TypeName }}</strong></div>
                  <div><span>Start</span><strong>{{ hex(selectedPartition()?.StartSector) }}</strong></div>
                  <div><span>Size</span><strong>{{ formatBytes(selectedPartition()?.TotalBytes || 0) }}</strong></div>
                  <div><span>Files</span><strong>{{ selectedFiles().length || selectedPartition()?.FileCount || 0 }}</strong></div>
                  <div><span>Created</span><strong>{{ selectedPartition()?.Created || '-' }}</strong></div>
                  <div><span>Icon</span><strong>{{ iconSys() ? 'Present' : 'Not found' }}</strong></div>
                </div>

                @if (iconSys()) {
                  <div class="hdb-icon-info">
                    <div><span>Title</span><strong>{{ iconSys()?.title || '-' }}</strong></div>
                    <div><span>Normal</span><strong>{{ iconSys()?.iconFileNormal || '-' }}</strong></div>
                    <div><span>Copy</span><strong>{{ iconSys()?.iconFileCopy || '-' }}</strong></div>
                    <div><span>Delete</span><strong>{{ iconSys()?.iconFileDelete || '-' }}</strong></div>
                  </div>
                }

                <div class="hdb-detail-actions">
                  <button class="hdb-btn" [disabled]="busy()" (click)="refreshSelectedFiles()">
                    <app-icon name="refreshcw" [size]="14" /> Files
                  </button>
                  <button class="hdb-btn" [disabled]="busy()" (click)="exportSelectedPartition()">
                    <app-icon name="download" [size]="14" /> Export
                  </button>
                </div>
              </div>
            </div>

            <div class="hdb-files-panel">
              <div class="hdb-section-title">Root Files</div>
              <div class="hdb-files-table">
                <div class="hdb-file-head">
                  <span>Name</span>
                  <span>Kind</span>
                  <span>Size</span>
                  <span>Modified</span>
                  <span></span>
                </div>
                @if (selectedFiles().length === 0) {
                  <div class="hdb-empty hdb-empty-wide">No root files found, or this partition is not PFS-readable.</div>
                }
                @for (file of selectedFiles(); track fileKey(file)) {
                  <div class="hdb-file-row">
                    <span class="hdb-file-name" [title]="file.Name">{{ file.Name }}</span>
                    <span>{{ fileKind(file) }}</span>
                    <span>{{ file.IsDirectory ? '-' : formatBytes(file.Size) }}</span>
                    <span>{{ file.Modified || '-' }}</span>
                    <span class="hdb-file-actions">
                      <button class="hdb-icon-btn" [disabled]="busy() || file.IsDirectory" title="Export file" (click)="exportFile(file)">
                        <app-icon name="download" [size]="13" />
                      </button>
                    </span>
                  </div>
                }
              </div>
            </div>
          }
        </section>
      </div>
    </div>
  `,
})
export class HddBrowserPageComponent implements AfterViewInit, OnDestroy {
  private readonly vscode = getVsCodeApi();
  private readonly requestedFiles = new Set<string>();
  private renderer: Ps2IconRenderer | null = null;
  private rendererCanvas: HTMLCanvasElement | null = null;
  private rendererInitTimer = 0;

  @ViewChild('previewCanvas') previewCanvas?: ElementRef<HTMLCanvasElement>;

  readonly hddPath = signal('');
  readonly partitions = signal<HddPartitionInfo[]>([]);
  readonly selectedPartitionId = signal('');
  readonly search = signal('');
  readonly busy = signal(false);
  readonly statusMsg = signal('');
  readonly statusIsError = signal(false);
  readonly lastOperation = signal<HddOperationResult | null>(null);
  readonly iconSys = signal<Ps2IconSys | null>(null);
  readonly iconSlots = signal<IconSlot[]>([]);
  readonly selectedIconIndex = signal(0);
  readonly rendererVersion = signal(0);

  readonly selectedPartition = computed(() =>
    this.partitions().find(p => p.Id === this.selectedPartitionId()) ?? null);

  readonly selectedFiles = computed(() => this.selectedPartition()?.Files ?? []);

  readonly filteredPartitions = computed(() => {
    const q = this.search().trim().toLowerCase();
    const parts = this.partitions();
    if (!q) { return parts; }
    return parts.filter(part =>
      `${part.Id} ${part.Name} ${part.TypeName}`.toLowerCase().includes(q));
  });

  readonly totalBytes = computed(() =>
    this.partitions().reduce((sum, part) => sum + (part.TotalBytes || 0), 0));

  readonly totalRootFiles = computed(() =>
    this.partitions().reduce((sum, part) => sum + (part.Files?.filter(f => !f.IsDirectory).length ?? part.FileCount ?? 0), 0));

  readonly iconPartitionCount = computed(() =>
    this.partitions().filter(part => Boolean(findFile(part, 'icon.sys'))).length);

  readonly activeSlot = computed(() => {
    const slots = this.iconSlots();
    return slots[this.selectedIconIndex()] ?? slots.find(slot => Boolean(slot.icon)) ?? null;
  });

  readonly activeIcon = computed(() => this.activeSlot()?.icon ?? null);

  readonly lastRevealPath = computed(() => {
    const op = this.lastOperation();
    return op?.outputDirectory ?? op?.outputPath ?? '';
  });

  readonly canvasMessage = computed(() => {
    if (!this.selectedPartition()) { return 'Select a partition.'; }
    if (this.busy()) { return 'Loading...'; }
    if (!findFile(this.selectedPartition(), 'icon.sys')) { return 'No icon.sys found in this partition.'; }
    const errors = this.iconSlots().map(slot => slot.error).filter(Boolean);
    if (errors.length) { return errors[0] ?? 'Icon could not be loaded.'; }
    return 'Icon metadata found; waiting for model file.';
  });

  private readonly onMessage = (event: MessageEvent): void => {
    const data = event.data as { type: string; [key: string]: unknown };
    switch (data.type) {
      case 'hddStatus':
        this.setStatus((data['message'] as string) ?? '', false, !!data['busy']);
        break;
      case 'hddError':
        this.setStatus((data['message'] as string) ?? 'HDD operation failed', true, false);
        break;
      case 'hddLoaded':
        this.handleHddLoaded(
          (data['hddPath'] as string) ?? '',
          (data['partitions'] as HddPartitionInfo[]) ?? [],
        );
        break;
      case 'hddFilesLoaded':
        this.handleFilesLoaded(
          (data['partitionId'] as string) ?? '',
          (data['files'] as HddFileEntryInfo[]) ?? [],
        );
        break;
      case 'hddFileData':
        this.handleFileData(
          (data['partitionId'] as string) ?? '',
          (data['fileName'] as string) ?? '',
          (data['data'] as string) ?? '',
        );
        break;
      case 'hddFileError':
        this.handleFileError(
          (data['partitionId'] as string) ?? '',
          (data['fileName'] as string) ?? '',
          (data['message'] as string) ?? 'File read failed',
        );
        break;
      case 'hddOperation':
        this.lastOperation.set((data['result'] as HddOperationResult) ?? null);
        this.setStatus((data['message'] as string) ?? 'Operation complete.', false, false);
        break;
    }
  };

  constructor() {
    effect(() => {
      this.rendererVersion();
      this.renderer?.setModel(this.iconSys(), this.activeIcon());
    });
  }

  ngAfterViewInit(): void {
    window.addEventListener('message', this.onMessage);
    this.scheduleRendererInit();
    this.vscode.postMessage({ type: 'ready' });
  }

  ngOnDestroy(): void {
    window.removeEventListener('message', this.onMessage);
    if (this.rendererInitTimer) {
      window.clearTimeout(this.rendererInitTimer);
      this.rendererInitTimer = 0;
    }
    this.renderer?.dispose();
    this.renderer = null;
    this.rendererCanvas = null;
  }

  openHdd(): void {
    this.vscode.postMessage({ type: 'openHdd' });
  }

  newHdd(): void {
    this.vscode.postMessage({ type: 'newHdd' });
  }

  refresh(): void {
    if (!this.hddPath()) { return; }
    this.vscode.postMessage({ type: 'refresh', hddPath: this.hddPath() });
  }

  refreshSelectedFiles(): void {
    const part = this.selectedPartition();
    if (!part) { return; }
    this.setStatus('Reading partition files...', false, true);
    this.vscode.postMessage({ type: 'listFiles', hddPath: this.hddPath(), partitionId: part.Id });
  }

  exportSelectedPartition(): void {
    const part = this.selectedPartition();
    if (!part) { return; }
    this.vscode.postMessage({
      type: 'exportPartition',
      hddPath: this.hddPath(),
      partitionId: part.Id,
      files: part.Files,
    });
  }

  exportFile(file: HddFileEntryInfo): void {
    const part = this.selectedPartition();
    if (!part) { return; }
    this.vscode.postMessage({
      type: 'exportFile',
      hddPath: this.hddPath(),
      partitionId: part.Id,
      fileName: file.Name,
      source: file.Source,
    });
  }

  deleteSelectedPartition(): void {
    const part = this.selectedPartition();
    if (!part) { return; }
    this.vscode.postMessage({
      type: 'deletePartition',
      hddPath: this.hddPath(),
      partitionId: part.Id,
    });
  }

  revealLast(): void {
    const target = this.lastRevealPath();
    if (target) { this.vscode.postMessage({ type: 'revealPath', path: target }); }
  }

  onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  selectPartition(partitionId: string): void {
    if (this.selectedPartitionId() === partitionId) { return; }
    this.selectedPartitionId.set(partitionId);
    this.scheduleRendererInit();
    this.loadSelectedPartitionIcon();
  }

  setIconSlot(index: number): void {
    if (this.iconSlots()[index]?.icon) {
      this.selectedIconIndex.set(index);
    }
  }

  partitionTitle(part: HddPartitionInfo): string {
    return part.Name && part.Name !== part.Id ? part.Name : part.Id;
  }

  fileCountLabel(part: HddPartitionInfo): string {
    const count = part.Files?.length || part.FileCount || 0;
    return `${count} file${count === 1 ? '' : 's'}`;
  }

  fileKey(file: HddFileEntryInfo): string {
    return `${file.Source || 'pfs'}:${file.Name}`;
  }

  fileKind(file: HddFileEntryInfo): string {
    if (file.IsDirectory) { return 'Directory'; }
    const lower = file.Name.toLowerCase();
    if (file?.Source === 'header' && lower === 'icon.sys') { return 'Header icon config'; }
    if (file?.Source === 'header') { return lower.endsWith('.ico') ? 'Header icon' : 'Header'; }
    if (lower === 'icon.sys') { return 'Icon config'; }
    if (this.iconSlots().some(slot => slot.fileName.toLowerCase() === lower)) { return '3D icon'; }
    if (lower.endsWith('.ico') || lower.endsWith('.icn')) { return 'Icon'; }
    if (lower.endsWith('.elf')) { return 'Executable'; }
    if (lower.endsWith('.irx')) { return 'IOP module'; }
    if (lower.endsWith('.sys')) { return 'System'; }
    return 'Data';
  }

  formatBytes(bytes: number): string {
    if (!Number.isFinite(bytes) || bytes <= 0) { return '0 B'; }
    if (bytes < 1024) { return `${bytes} B`; }
    if (bytes < 1024 * 1024) { return `${(bytes / 1024).toFixed(1)} KB`; }
    if (bytes < 1024 * 1024 * 1024) { return `${(bytes / (1024 * 1024)).toFixed(2)} MB`; }
    return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
  }

  hex(value: number | undefined, width = 8): string {
    if (value === undefined || value === null || !Number.isFinite(value)) { return '-'; }
    return '0x' + (value >>> 0).toString(16).toUpperCase().padStart(width, '0');
  }

  private handleHddLoaded(hddPath: string, partitions: HddPartitionInfo[]): void {
    this.hddPath.set(hddPath);
    this.partitions.set(partitions);
    this.lastOperation.set(null);
    this.statusIsError.set(false);
    this.busy.set(false);
    this.statusMsg.set(partitions.length === 0 ? 'HDD image has no partitions.' : `${partitions.length} partition${partitions.length === 1 ? '' : 's'} loaded.`);

    const previous = this.selectedPartitionId();
    const next = partitions.find(p => p.Id === previous)?.Id ?? partitions[0]?.Id ?? '';
    this.selectedPartitionId.set(next);
    this.scheduleRendererInit();
    this.loadSelectedPartitionIcon();
  }

  private handleFilesLoaded(partitionId: string, files: HddFileEntryInfo[]): void {
    this.partitions.update(parts => parts.map(part =>
      part.Id === partitionId ? { ...part, Files: files, FileCount: files.length } : part));
    if (partitionId === this.selectedPartitionId()) {
      this.busy.set(false);
      this.loadSelectedPartitionIcon();
    }
  }

  private handleFileData(partitionId: string, fileName: string, data: string): void {
    if (partitionId !== this.selectedPartitionId()) { return; }
    try {
      const bytes = base64ToBytes(data);
      if (fileName.toLowerCase() === 'icon.sys') {
        const sys = looksLikeHddTextIconSys(bytes) ? parseHddTextIconSys(bytes) : parseIconSys(bytes);
        this.iconSys.set(sys);
        this.loadIconFiles(sys);
        return;
      }

      const icon = parseIcon(bytes);
      this.iconSlots.update(slots => slots.map(slot =>
        sameName(slot.fileName, fileName) ? { ...slot, icon, error: undefined } : slot));
      const firstLoaded = this.iconSlots().findIndex(slot => Boolean(slot.icon));
      if (!this.activeIcon() && firstLoaded >= 0) {
        this.selectedIconIndex.set(firstLoaded);
      }
      this.setStatus('', false, false);
    } catch (err) {
      this.handleFileError(partitionId, fileName, err instanceof Error ? err.message : String(err));
    }
  }

  private handleFileError(partitionId: string, fileName: string, message: string): void {
    if (partitionId !== this.selectedPartitionId()) { return; }
    if (fileName.toLowerCase() === 'icon.sys') {
      this.iconSys.set(null);
      this.iconSlots.set([]);
      this.setStatus(message, true, false);
      return;
    }

    this.iconSlots.update(slots => slots.map(slot =>
      sameName(slot.fileName, fileName) ? { ...slot, error: message } : slot));
    this.setStatus(message, true, false);
  }

  private loadSelectedPartitionIcon(): void {
    this.iconSys.set(null);
    this.iconSlots.set([]);
    this.selectedIconIndex.set(0);
    this.requestedFiles.clear();

    const part = this.selectedPartition();
    if (!part) { return; }

    const iconSysFile = findFile(part, 'icon.sys');
    if (!iconSysFile) {
      this.setStatus('Selected partition has no icon.sys file.', false, false);
      return;
    }

    this.setStatus('Loading icon.sys...', false, true);
      this.readPartitionFile(part.Id, iconSysFile);
  }

  private loadIconFiles(iconSys: Ps2IconSys): void {
    const part = this.selectedPartition();
    if (!part) { return; }

    const slots: IconSlot[] = [
      { action: 'normal', label: 'Browse', fileName: iconSys.iconFileNormal },
      { action: 'copy', label: 'Copy', fileName: iconSys.iconFileCopy },
      { action: 'delete', label: 'Delete', fileName: iconSys.iconFileDelete },
    ];
    const visibleSlots = slots.filter(slot => slot.fileName.trim().length > 0);
    this.iconSlots.set(visibleSlots);
    this.selectedIconIndex.set(0);

    const names = uniqueIconFileNames(iconSys);
    if (names.length === 0) {
      this.setStatus('icon.sys does not reference any icon files.', true, false);
      return;
    }

    for (const rawName of names) {
      const file = findFile(part, rawName);
      if (!file) {
        this.iconSlots.update(current => current.map(slot =>
          sameName(slot.fileName, rawName) ? { ...slot, error: `Missing ${rawName}` } : slot));
        continue;
      }
      this.readPartitionFile(part.Id, file);
    }
  }

  private readPartitionFile(partitionId: string, file: HddFileEntryInfo): void {
    const fileName = file.Name;
    const key = `${partitionId.toLowerCase()}/${file.Source || 'pfs'}/${fileName.toLowerCase()}`;
    if (this.requestedFiles.has(key)) { return; }
    this.requestedFiles.add(key);
    this.vscode.postMessage({
      type: 'readPartitionFile',
      hddPath: this.hddPath(),
      partitionId,
      fileName,
      source: file.Source,
    });
  }

  private setStatus(message: string, isError: boolean, busy: boolean): void {
    this.statusMsg.set(message);
    this.statusIsError.set(isError);
    this.busy.set(busy);
  }

  private scheduleRendererInit(): void {
    if (this.rendererInitTimer) {
      window.clearTimeout(this.rendererInitTimer);
    }
    this.rendererInitTimer = window.setTimeout(() => {
      this.rendererInitTimer = 0;
      this.ensureRenderer();
    }, 0);
  }

  private ensureRenderer(): void {
    const canvas = this.previewCanvas?.nativeElement ?? null;
    if (!canvas) { return; }
    if (this.renderer && this.rendererCanvas === canvas) { return; }

    this.renderer?.dispose();
    this.renderer = new Ps2IconRenderer(canvas);
    this.rendererCanvas = canvas;
    this.rendererVersion.update(v => v + 1);
  }
}

function findFile(partition: HddPartitionInfo | null, name: string): HddFileEntryInfo | null {
  if (!partition) { return null; }
  const lower = name.toLowerCase();
  return partition.Files?.find(file => file.Name.toLowerCase() === lower) ?? null;
}

function sameName(a: string, b: string): boolean {
  return a.localeCompare(b, undefined, { sensitivity: 'accent' }) === 0 ||
    a.toLowerCase() === b.toLowerCase();
}

function looksLikeHddTextIconSys(bytes: Uint8Array): boolean {
  return bytes.length >= 4 &&
    bytes[0] === 0x50 &&
    bytes[1] === 0x53 &&
    bytes[2] === 0x32 &&
    bytes[3] === 0x58;
}

function parseHddTextIconSys(bytes: Uint8Array): Ps2IconSys {
  const text = asciiText(bytes);
  const values = new Map<string, string>();
  for (const line of text.split(/\r?\n/)) {
    const idx = line.indexOf('=');
    if (idx <= 0) { continue; }
    values.set(line.slice(0, idx).trim().toLowerCase(), line.slice(idx + 1).trim());
  }

  const title0 = values.get('title0') ?? '';
  const title1 = values.get('title1') ?? '';

  return {
    titleLines: [title0, title1],
    title: [title0, title1].filter(Boolean).join(' ').trim(),
    backgroundTransparency: numberValue(values.get('bgcola'), 64),
    bgColors: [
      colorValue(values.get('bgcol0'), [22, 47, 92, 0]),
      colorValue(values.get('bgcol1'), [3, 10, 28, 0]),
      colorValue(values.get('bgcol2'), [3, 10, 28, 0]),
      colorValue(values.get('bgcol3'), [22, 47, 92, 0]),
    ],
    lightDirs: [
      vecValue(values.get('lightdir0'), [0.5, 0.5, 0.5, 0]),
      vecValue(values.get('lightdir1'), [0, -0.4, -1, 0]),
      vecValue(values.get('lightdir2'), [0.5, -0.5, 0.5, 0]),
    ],
    lightColors: [
      colorFloatValue(values.get('lightcol0'), [62, 62, 55, 128]),
      colorFloatValue(values.get('lightcol1'), [33, 42, 64, 128]),
      colorFloatValue(values.get('lightcol2'), [18, 18, 49, 128]),
    ],
    ambient: colorFloatValue(values.get('lightcolamb'), [31, 31, 31, 128]),
    iconFileNormal: 'list.ico',
    iconFileCopy: 'list.ico',
    iconFileDelete: 'del.ico',
  };
}

function asciiText(bytes: Uint8Array): string {
  let end = bytes.indexOf(0);
  if (end < 0) { end = bytes.length; }
  let out = '';
  for (let i = 0; i < end; i++) {
    out += String.fromCharCode(bytes[i]);
  }
  return out;
}

function numberValue(value: string | undefined, fallback: number): number {
  const n = Number(value);
  return Number.isFinite(n) ? n : fallback;
}

function colorValue(value: string | undefined, fallback: number[]): number[] {
  const parsed = parseCsvNumbers(value);
  return parsed.length >= 3 ? [parsed[0], parsed[1], parsed[2], 0] : fallback;
}

function colorFloatValue(value: string | undefined, fallback: number[]): number[] {
  const parsed = parseCsvNumbers(value);
  return parsed.length >= 3 ? [parsed[0] / 128, parsed[1] / 128, parsed[2] / 128, 1] : fallback.map(v => v / 128);
}

function vecValue(value: string | undefined, fallback: number[]): number[] {
  const parsed = parseCsvNumbers(value);
  return parsed.length >= 3 ? [parsed[0], parsed[1], parsed[2], 0] : fallback;
}

function parseCsvNumbers(value: string | undefined): number[] {
  if (!value) { return []; }
  return value.split(',')
    .map(v => Number(v.trim()))
    .filter(Number.isFinite);
}
