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
} from './ps2-icon-parser';
import { Ps2IconRenderer } from './ps2-icon-renderer';
import '../shared/styles/shared.css';
import './memory-card-browser-page.css';

interface McFileInfo {
  Name: string;
  Size: number;
}

interface McSaveInfo {
  Name: string;
  FileCount: number;
  Created: string;
  Modified: string;
  TotalBytes: number;
  Files: McFileInfo[];
}

type IconAction = 'normal' | 'copy' | 'delete';

interface SaveSnapshotInfo {
  id: string;
  saveName: string;
  source: string;
  created: string;
  label: string;
  fileCount: number;
  totalBytes: number;
}

interface FileDiff {
  name: string;
  status: 'same' | 'changed' | 'added' | 'removed';
  sizeBefore: number;
  sizeAfter: number;
  changedBytes: number;
  runs: Array<{ offset: number; before: string; after: string }>;
  truncated: boolean;
}

/** A file or folder dropped from the OS, read here because the host can't see its path. */
interface DroppedItem {
  kind: 'file' | 'folder';
  name: string;
  data?: string;
  files?: Array<{ name: string; data: string }>;
}

interface IconSlot {
  action: IconAction;
  label: string;
  fileName: string;
  icon?: Ps2Icon;
  error?: string;
}

@Component({
  selector: 'app-memory-card-browser-page',
  standalone: true,
  imports: [IconComponent, PageHeaderComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="mcb-shell">
      <app-page-header icon="memorystick" title="Memory Card" subtitle="PS2 memory card browser" />
      <div class="mcb-toolbar">
        <div class="mcb-title">
          <app-icon name="memorystick" [size]="16" />
          <span>PS2 Memory Card Browser</span>
        </div>
        <div class="mcb-toolbar-spacer"></div>
        <button class="mcb-btn mcb-primary" [disabled]="busy()" (click)="openCard()">
          <app-icon name="harddrive" [size]="14" /> Open
        </button>
        <button class="mcb-btn" [disabled]="busy()" (click)="newCard()">
          <app-icon name="plus" [size]="14" /> New
        </button>
        <button class="mcb-btn" [disabled]="!mcPath() || busy()" (click)="refresh()">
          <app-icon name="refreshcw" [size]="14" /> Refresh
        </button>
        <button class="mcb-btn" [disabled]="!mcPath() || busy()" (click)="importSave()">
          <app-icon name="upload" [size]="14" /> Import
        </button>
      </div>
      <div class="mcb-drop-hint">Drop save folders or .psu files on the list to import them (hold Shift while dropping). Drag a save out to export it as .psu.</div>

      @if (statusMsg()) {
        <div class="mcb-status" [class.mcb-status-error]="statusIsError()">
          {{ statusMsg() }}
        </div>
      }

      <div class="mcb-main"
           [class.mcb-drop-active]="dropActive()"
           (dragover)="onDragOver($event)"
           (dragleave)="onDragLeave($event)"
           (drop)="onDrop($event)">
        <aside class="mcb-sidebar">
          <div class="mcb-path" [title]="mcPath()">
            {{ mcPath() || 'No memory card selected' }}
          </div>

          <label class="mcb-search">
            <app-icon name="search" [size]="14" />
            <input
              type="text"
              placeholder="Search saves"
              [value]="search()"
              (input)="onSearch($event)" />
          </label>

          <div class="mcb-save-list">
            @if (!busy() && saves().length === 0) {
              <div class="mcb-empty">Open a .ps2 card to browse saves.</div>
            }
            @for (save of filteredSaves(); track save.Name) {
              <button
                type="button"
                class="mcb-save-row"
                draggable="true"
                [class.active]="selectedSaveName() === save.Name"
                [title]="psuCache.has(save.Name) ? 'Drag out to export as ' + save.Name + '.psu' : save.Name"
                (dragstart)="onSaveDragStart($event, save.Name)"
                (click)="selectSave(save.Name)">
                <span class="mcb-save-name">{{ save.Name }}</span>
                <span class="mcb-save-meta">
                  {{ save.FileCount }} file{{ save.FileCount === 1 ? '' : 's' }} | {{ formatBytes(save.TotalBytes) }}
                </span>
              </button>
            }
          </div>
        </aside>

        <section class="mcb-content">
          <div class="mcb-preview-band">
            <div class="mcb-canvas-area">
              <canvas #previewCanvas class="mcb-canvas"></canvas>
              @if (!activeIcon()) {
                <div class="mcb-canvas-empty">
                  {{ canvasMessage() }}
                </div>
              }
              @if (iconSlots().length > 1) {
                <div class="mcb-icon-tabs">
                  @for (slot of iconSlots(); track slot.action) {
                    <button
                      type="button"
                      class="mcb-icon-tab"
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

            <div class="mcb-save-details">
              <div class="mcb-details-title">{{ displayTitle() }}</div>
              <div class="mcb-details-subtitle">{{ selectedSaveName() || 'No save selected' }}</div>

              <div class="mcb-detail-grid">
                <div>
                  <span>Files</span>
                  <strong>{{ selectedSave()?.FileCount ?? 0 }}</strong>
                </div>
                <div>
                  <span>Size</span>
                  <strong>{{ formatBytes(selectedSave()?.TotalBytes ?? 0) }}</strong>
                </div>
                <div>
                  <span>Modified</span>
                  <strong>{{ selectedSave()?.Modified || '-' }}</strong>
                </div>
              </div>

              @if (iconSys(); as sys) {
                <div class="mcb-icon-info">
                  <div><span>Normal</span><strong>{{ sys.iconFileNormal || '-' }}</strong></div>
                  <div><span>Copy</span><strong>{{ sys.iconFileCopy || '-' }}</strong></div>
                  <div><span>Delete</span><strong>{{ sys.iconFileDelete || '-' }}</strong></div>
                </div>
              }

              <div class="mcb-detail-actions">
                <button class="mcb-btn" [disabled]="!selectedSave()" (click)="exportSelected()">
                  <app-icon name="save" [size]="14" /> Export
                </button>
                <button class="mcb-btn" [disabled]="!selectedSave()" (click)="exportPsu()" title="Export as a single .psu file (uLaunchELF / EMS)">
                  <app-icon name="download" [size]="14" /> .psu
                </button>
                <button class="mcb-btn" [disabled]="!selectedSave() || busy()" (click)="snapshotSave()" title="Store a copy of this save to diff against later">
                  <app-icon name="camera" [size]="14" /> Snapshot
                </button>
                <button class="mcb-btn mcb-danger" [disabled]="!selectedSave()" (click)="deleteSelected()">
                  <app-icon name="trash2" [size]="14" /> Delete
                </button>
              </div>
            </div>
          </div>

          @if (selectedSave() && snapshots().length) {
            <div class="mcb-snapshots">
              <div class="mcb-section-title">Snapshots</div>
              @for (snap of snapshots(); track snap.id) {
                <div class="mcb-snapshot-row" [class.active]="diff()?.id === snap.id">
                  <span class="mcb-snapshot-date">{{ formatDate(snap.created) }}</span>
                  <span class="mcb-snapshot-label">{{ snap.label || '—' }}</span>
                  <span class="mcb-snapshot-meta">{{ snap.fileCount }} files · {{ formatBytes(snap.totalBytes) }}</span>
                  <button class="lm-row-btn lm-goto-btn" [disabled]="busy()" (click)="diffSnapshot(snap.id)">Diff</button>
                  <button class="lm-row-btn lm-danger-btn" title="Delete snapshot" (click)="deleteSnapshot(snap.id)"><app-icon name="trash2" [size]="12" /></button>
                </div>
              }
            </div>
          }

          @if (diff(); as d) {
            <div class="mcb-files-panel">
              <div class="mcb-section-title mcb-diff-title">
                <span>Changes since {{ snapshotDate(d.id) }}</span>
                <button class="lm-row-btn" (click)="diff.set(null)"><app-icon name="x" [size]="12" /> Close</button>
              </div>
              <div class="mcb-files-table">
                <div class="mcb-file-head mcb-diff-head">
                  <span>File</span><span>Status</span><span>Size</span><span>Changed</span>
                </div>
                @for (f of d.files; track f.name) {
                  <div class="mcb-file-row mcb-diff-row" [class.mcb-diff-same]="f.status === 'same'"
                       (click)="toggleDiffFile(f.name)">
                    <span class="mcb-file-name">{{ f.name }}</span>
                    <span class="mcb-diff-status" [attr.data-status]="f.status">{{ f.status }}</span>
                    <span>{{ f.sizeBefore === f.sizeAfter ? formatBytes(f.sizeAfter) : formatBytes(f.sizeBefore) + ' → ' + formatBytes(f.sizeAfter) }}</span>
                    <span>{{ f.status === 'same' ? '—' : f.changedBytes + ' B' }}</span>
                  </div>
                  @if (expandedDiff() === f.name && f.runs.length) {
                    <div class="mcb-diff-runs">
                      @for (r of f.runs; track r.offset) {
                        <div class="mcb-diff-run">
                          <span class="lm-mono">0x{{ r.offset.toString(16).toUpperCase().padStart(4, '0') }}</span>
                          <span class="mcb-diff-before">{{ r.before }}</span>
                          <span class="mcb-diff-after">{{ r.after }}</span>
                        </div>
                      }
                      @if (f.truncated) { <div class="lm-muted">More changes not shown.</div> }
                    </div>
                  }
                }
              </div>
            </div>
          } @else {
          <div class="mcb-files-panel">
            <div class="mcb-section-title">Save Files</div>
            <div class="mcb-files-table">
              <div class="mcb-file-head">
                <span>Name</span>
                <span>Kind</span>
                <span>Size</span>
              </div>
              @if (!selectedSave()) {
                <div class="mcb-empty mcb-empty-wide">Select a save to inspect its files.</div>
              }
              @for (file of selectedFiles(); track file.Name) {
                <div class="mcb-file-row">
                  <span class="mcb-file-name">{{ file.Name }}</span>
                  <span>{{ fileKind(file.Name) }}</span>
                  <span>{{ formatBytes(file.Size) }}</span>
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
export class MemoryCardBrowserPageComponent implements AfterViewInit, OnDestroy {
  @ViewChild('previewCanvas', { static: true }) private readonly previewCanvas?: ElementRef<HTMLCanvasElement>;

  private readonly vscode = getVsCodeApi();
  private renderer: Ps2IconRenderer | null = null;
  private readonly rendererVersion = signal(0);
  private readonly requestedFiles = new Set<string>();

  readonly mcPath = signal('');
  readonly saves = signal<McSaveInfo[]>([]);
  readonly selectedSaveName = signal('');
  readonly search = signal('');
  readonly busy = signal(false);
  readonly statusMsg = signal('');
  readonly statusIsError = signal(false);
  readonly iconSys = signal<Ps2IconSys | null>(null);
  readonly iconSlots = signal<IconSlot[]>([]);
  readonly selectedIconIndex = signal(0);
  readonly dropActive = signal(false);
  readonly snapshots = signal<SaveSnapshotInfo[]>([]);
  readonly diff = signal<{ id: string; files: FileDiff[] } | null>(null);
  readonly expandedDiff = signal('');
  /** Selected saves pre-built as .psu (base64) so dragging them out can carry the file. */
  readonly psuCache = new Map<string, string>();

  readonly selectedSave = computed(() =>
    this.saves().find(s => s.Name === this.selectedSaveName()) ?? null);

  readonly selectedFiles = computed(() =>
    [...(this.selectedSave()?.Files ?? [])].sort((a, b) => a.Name.localeCompare(b.Name)));

  readonly filteredSaves = computed(() => {
    const q = this.search().trim().toLowerCase();
    const list = this.saves();
    if (!q) { return list; }
    return list.filter(s => {
      const files = s.Files.map(f => f.Name).join(' ');
      return `${s.Name} ${files}`.toLowerCase().includes(q);
    });
  });

  readonly activeSlot = computed(() => this.iconSlots()[this.selectedIconIndex()] ?? null);
  readonly activeIcon = computed(() => this.activeSlot()?.icon ?? null);

  readonly displayTitle = computed(() => {
    const title = this.iconSys()?.title.trim();
    return title || this.selectedSaveName() || 'Memory Card';
  });

  readonly canvasMessage = computed(() => {
    if (!this.selectedSave()) { return 'Select a save to preview its icon.'; }
    if (!this.findIconSysFile(this.selectedSave())) { return 'This save has no icon.sys file.'; }
    if (this.iconSlots().some(s => s.error)) { return this.iconSlots().find(s => s.error)?.error ?? 'Icon failed to load.'; }
    return 'Loading icon preview...';
  });

  private readonly onMessage = (event: MessageEvent): void => {
    const data = event.data as { type: string; [key: string]: unknown };
    switch (data.type) {
      case 'browserReady':
        break;
      case 'mcStatus':
        this.setStatus((data['message'] as string) ?? '', false, (data['busy'] as boolean) ?? false);
        break;
      case 'mcError':
        this.setStatus((data['message'] as string) ?? 'Memory card error', true, false);
        break;
      case 'cardLoaded':
        this.handleCardLoaded(
          (data['mcPath'] as string) ?? '',
          (data['saves'] as McSaveInfo[]) ?? [],
        );
        break;
      case 'saveFileData':
        this.handleSaveFileData(
          (data['saveName'] as string) ?? '',
          (data['fileName'] as string) ?? '',
          (data['data'] as string) ?? '',
        );
        break;
      case 'saveSnapshots':
        if (data['saveName'] === this.selectedSaveName()) {
          this.snapshots.set((data['snapshots'] as SaveSnapshotInfo[]) ?? []);
        }
        break;
      case 'saveDiff':
        if (data['saveName'] === this.selectedSaveName()) {
          this.diff.set({ id: (data['id'] as string) ?? '', files: (data['files'] as FileDiff[]) ?? [] });
          this.expandedDiff.set(((data['files'] as FileDiff[]) ?? []).find(f => f.status === 'changed')?.name ?? '');
        }
        break;
      case 'psuReady':
        this.psuCache.set((data['saveName'] as string) ?? '', (data['data'] as string) ?? '');
        break;
      case 'saveFileError':
        this.handleSaveFileError(
          (data['saveName'] as string) ?? '',
          (data['fileName'] as string) ?? '',
          (data['message'] as string) ?? 'File read failed',
        );
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
    if (this.previewCanvas?.nativeElement) {
      this.renderer = new Ps2IconRenderer(this.previewCanvas.nativeElement);
      this.rendererVersion.update(v => v + 1);
    }
    this.vscode.postMessage({ type: 'ready' });
  }

  ngOnDestroy(): void {
    window.removeEventListener('message', this.onMessage);
    this.renderer?.dispose();
    this.renderer = null;
  }

  openCard(): void {
    this.vscode.postMessage({ type: 'openCard' });
  }

  newCard(): void {
    this.vscode.postMessage({ type: 'newCard' });
  }

  refresh(): void {
    if (!this.mcPath()) { return; }
    this.busy.set(true);
    this.vscode.postMessage({ type: 'refresh', mcPath: this.mcPath() });
  }

  importSave(): void {
    if (!this.mcPath()) { return; }
    this.vscode.postMessage({ type: 'importSave', mcPath: this.mcPath() });
  }

  exportSelected(): void {
    const save = this.selectedSave();
    if (!save) { return; }
    this.vscode.postMessage({
      type: 'exportSave',
      mcPath: this.mcPath(),
      saveName: save.Name,
    });
  }

  deleteSelected(): void {
    const save = this.selectedSave();
    if (!save) { return; }
    this.vscode.postMessage({
      type: 'deleteSave',
      mcPath: this.mcPath(),
      saveName: save.Name,
    });
  }

  onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  selectSave(saveName: string): void {
    if (this.selectedSaveName() === saveName) { return; }
    this.selectedSaveName.set(saveName);
    this.loadSelectedSaveIcon();
    this.onSaveSelected();
  }

  // ── Snapshots / diff ─────────────────────────────────────────────────────

  snapshotSave(): void {
    const save = this.selectedSave();
    if (save) { this.vscode.postMessage({ type: 'snapshotSave', mcPath: this.mcPath(), saveName: save.Name }); }
  }

  diffSnapshot(id: string): void {
    this.vscode.postMessage({ type: 'diffSnapshot', mcPath: this.mcPath(), saveName: this.selectedSaveName(), id });
  }

  deleteSnapshot(id: string): void {
    if (this.diff()?.id === id) { this.diff.set(null); }
    this.vscode.postMessage({ type: 'deleteSnapshot', saveName: this.selectedSaveName(), id });
  }

  toggleDiffFile(name: string): void {
    this.expandedDiff.set(this.expandedDiff() === name ? '' : name);
  }

  snapshotDate(id: string): string {
    const snap = this.snapshots().find(s => s.id === id);
    return snap ? `${this.formatDate(snap.created)}${snap.label ? ` (${snap.label})` : ''}` : 'snapshot';
  }

  formatDate(iso: string): string {
    const d = new Date(iso);
    return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
  }

  exportPsu(): void {
    const save = this.selectedSave();
    if (save) { this.vscode.postMessage({ type: 'exportPsu', mcPath: this.mcPath(), saveName: save.Name }); }
  }

  /** Snapshot list, diff reset and drag-out .psu for the newly selected save. */
  private onSaveSelected(): void {
    this.diff.set(null);
    this.snapshots.set([]);
    const name = this.selectedSaveName();
    if (!name) { return; }
    this.vscode.postMessage({ type: 'listSnapshots', saveName: name });
    if (!this.psuCache.has(name)) {
      this.vscode.postMessage({ type: 'buildPsu', mcPath: this.mcPath(), saveName: name });
    }
  }

  // ── Drag and drop ────────────────────────────────────────────────────────

  onSaveDragStart(event: DragEvent, saveName: string): void {
    const dt = event.dataTransfer;
    if (!dt) { return; }
    dt.effectAllowed = 'copy';
    dt.setData('text/plain', saveName);
    const psu = this.psuCache.get(saveName);
    if (psu) {
      // Chromium writes the file where it is dropped (desktop, Explorer).
      dt.setData('DownloadURL', `application/octet-stream:${saveName}.psu:data:application/octet-stream;base64,${psu}`);
    }
  }

  onDragOver(event: DragEvent): void {
    if (!event.dataTransfer || ![...event.dataTransfer.types].some(t => t === 'Files' || t.includes('uri-list'))) { return; }
    event.preventDefault();
    event.dataTransfer.dropEffect = 'copy';
    this.dropActive.set(true);
  }

  onDragLeave(event: DragEvent): void {
    const to = event.relatedTarget as Node | null;
    if (!to || !(event.currentTarget as HTMLElement).contains(to)) { this.dropActive.set(false); }
  }

  async onDrop(event: DragEvent): Promise<void> {
    event.preventDefault();
    this.dropActive.set(false);
    const dt = event.dataTransfer;
    if (!dt) { return; }
    // VS Code explorer drags carry URIs (the host reads the paths itself).
    const uriText = dt.getData('application/vnd.code.uri-list') || dt.getData('text/uri-list');
    const uris = uriText.split(/\r?\n/).map(u => u.trim()).filter(u => u && !u.startsWith('#'));
    // Entries must be taken synchronously, before the first await.
    const entries = uris.length ? [] : Array.from(dt.items)
      .filter(i => i.kind === 'file')
      .map(i => i.webkitGetAsEntry())
      .filter((e): e is FileSystemEntry => !!e);
    if (!uris.length && !entries.length) { return; }

    this.setStatus('Reading dropped files...', false, true);
    try {
      const items: DroppedItem[] = [];
      for (const entry of entries) {
        if (entry.isDirectory) {
          const files = await readDirectoryFiles(entry as FileSystemDirectoryEntry);
          items.push({ kind: 'folder', name: entry.name, files });
        } else {
          const file = await entryFile(entry as FileSystemFileEntry);
          items.push({ kind: 'file', name: entry.name, data: await fileToBase64(file) });
        }
      }
      this.vscode.postMessage({ type: 'dropImport', mcPath: this.mcPath(), uris, items });
    } catch (err: unknown) {
      this.setStatus(err instanceof Error ? err.message : String(err), true, false);
    }
  }

  setIconSlot(index: number): void {
    if (this.iconSlots()[index]?.icon) {
      this.selectedIconIndex.set(index);
    }
  }

  formatBytes(bytes: number): string {
    if (!Number.isFinite(bytes) || bytes <= 0) { return '0 B'; }
    if (bytes < 1024) { return `${bytes} B`; }
    if (bytes < 1024 * 1024) { return `${(bytes / 1024).toFixed(1)} KB`; }
    return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
  }

  fileKind(name: string): string {
    const lower = name.toLowerCase();
    if (lower === 'icon.sys') { return 'Icon config'; }
    if (this.iconSlots().some(s => s.fileName.toLowerCase() === lower)) { return '3D icon'; }
    if (lower.endsWith('.ico')) { return 'Icon'; }
    if (lower.endsWith('.sys')) { return 'System'; }
    return 'Data';
  }

  private handleCardLoaded(mcPath: string, saves: McSaveInfo[]): void {
    // The card changed (import, delete, other card): rebuild drag-out files on demand.
    this.psuCache.clear();
    this.mcPath.set(mcPath);
    this.saves.set(saves);
    this.busy.set(false);
    this.statusIsError.set(false);
    this.statusMsg.set(saves.length === 0 ? 'Memory card is empty.' : `${saves.length} save${saves.length === 1 ? '' : 's'} loaded.`);

    const previous = this.selectedSaveName();
    const next = saves.find(s => s.Name === previous)?.Name ?? saves[0]?.Name ?? '';
    this.selectedSaveName.set(next);
    this.loadSelectedSaveIcon();
    this.onSaveSelected();
  }

  private handleSaveFileData(saveName: string, fileName: string, data: string): void {
    if (saveName !== this.selectedSaveName()) { return; }
    try {
      const bytes = base64ToBytes(data);
      if (fileName.toLowerCase() === 'icon.sys') {
        const sys = parseIconSys(bytes);
        this.iconSys.set(sys);
        this.loadIconFiles(sys);
        return;
      }

      const icon = parseIcon(bytes);
      this.iconSlots.update(slots => slots.map(slot =>
        sameName(slot.fileName, fileName) ? { ...slot, icon, error: undefined } : slot));
      const firstLoaded = this.iconSlots().findIndex(s => Boolean(s.icon));
      if (!this.activeIcon() && firstLoaded >= 0) {
        this.selectedIconIndex.set(firstLoaded);
      }
      this.setStatus('', false, false);
    } catch (err: unknown) {
      this.handleSaveFileError(saveName, fileName, err instanceof Error ? err.message : String(err));
    }
  }

  private handleSaveFileError(saveName: string, fileName: string, message: string): void {
    if (saveName !== this.selectedSaveName()) { return; }
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

  private loadSelectedSaveIcon(): void {
    this.iconSys.set(null);
    this.iconSlots.set([]);
    this.selectedIconIndex.set(0);
    this.requestedFiles.clear();

    const save = this.selectedSave();
    if (!save) { return; }

    const iconSysFile = this.findIconSysFile(save);
    if (!iconSysFile) {
      this.setStatus('Selected save has no icon.sys file.', false, false);
      return;
    }

    this.setStatus('Loading icon.sys...', false, true);
    this.readSaveFile(save.Name, iconSysFile.Name);
  }

  private loadIconFiles(iconSys: Ps2IconSys): void {
    const save = this.selectedSave();
    if (!save) { return; }

    const slots: IconSlot[] = [
      { action: 'normal', label: 'Browse', fileName: iconSys.iconFileNormal },
      { action: 'copy', label: 'Copy', fileName: iconSys.iconFileCopy },
      { action: 'delete', label: 'Delete', fileName: iconSys.iconFileDelete },
    ] satisfies IconSlot[];
    const visibleSlots = slots.filter(slot => slot.fileName.trim().length > 0);

    this.iconSlots.set(visibleSlots);
    this.selectedIconIndex.set(0);

    const names = uniqueIconFileNames(iconSys);
    if (names.length === 0) {
      this.setStatus('icon.sys does not reference any icon files.', true, false);
      return;
    }

    for (const rawName of names) {
      const file = findFile(save, rawName);
      if (!file) {
        this.iconSlots.update(current => current.map(slot =>
          sameName(slot.fileName, rawName) ? { ...slot, error: `Missing ${rawName}` } : slot));
        continue;
      }
      this.readSaveFile(save.Name, file.Name);
    }
  }

  private readSaveFile(saveName: string, fileName: string): void {
    const key = `${saveName.toLowerCase()}/${fileName.toLowerCase()}`;
    if (this.requestedFiles.has(key)) { return; }
    this.requestedFiles.add(key);
    this.vscode.postMessage({
      type: 'readSaveFile',
      mcPath: this.mcPath(),
      saveName,
      fileName,
    });
  }

  private findIconSysFile(save: McSaveInfo | null): McFileInfo | null {
    if (!save) { return null; }
    return findFile(save, 'icon.sys');
  }

  private setStatus(message: string, isError: boolean, busy: boolean): void {
    this.statusMsg.set(message);
    this.statusIsError.set(isError);
    this.busy.set(busy);
  }
}

function entryFile(entry: FileSystemFileEntry): Promise<File> {
  return new Promise((resolve, reject) => entry.file(resolve, reject));
}

function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).replace(/^data:[^,]*,/, ''));
    reader.onerror = () => reject(reader.error ?? new Error(`Could not read ${file.name}`));
    reader.readAsDataURL(file);
  });
}

/** The files directly inside a dropped save folder (saves have no subfolders). */
async function readDirectoryFiles(dir: FileSystemDirectoryEntry): Promise<Array<{ name: string; data: string }>> {
  const reader = dir.createReader();
  const entries: FileSystemEntry[] = [];
  for (;;) {
    const batch = await new Promise<FileSystemEntry[]>((resolve, reject) => reader.readEntries(resolve, reject));
    if (!batch.length) { break; }
    entries.push(...batch);
  }
  const files: Array<{ name: string; data: string }> = [];
  for (const e of entries) {
    if (!e.isFile) { continue; }
    files.push({ name: e.name, data: await fileToBase64(await entryFile(e as FileSystemFileEntry)) });
  }
  if (!files.length) { throw new Error(`${dir.name} has no files to import`); }
  return files;
}

function findFile(save: McSaveInfo, name: string): McFileInfo | null {
  const lower = name.toLowerCase();
  return save.Files.find(f => f.Name.toLowerCase() === lower) ?? null;
}

function sameName(a: string, b: string): boolean {
  return a.localeCompare(b, undefined, { sensitivity: 'accent' }) === 0 ||
    a.toLowerCase() === b.toLowerCase();
}
