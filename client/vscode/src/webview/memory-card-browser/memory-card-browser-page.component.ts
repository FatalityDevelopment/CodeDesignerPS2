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

      @if (statusMsg()) {
        <div class="mcb-status" [class.mcb-status-error]="statusIsError()">
          {{ statusMsg() }}
        </div>
      }

      <div class="mcb-main">
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
                [class.active]="selectedSaveName() === save.Name"
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
                <button class="mcb-btn mcb-danger" [disabled]="!selectedSave()" (click)="deleteSelected()">
                  <app-icon name="trash2" [size]="14" /> Delete
                </button>
              </div>
            </div>
          </div>

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
    this.mcPath.set(mcPath);
    this.saves.set(saves);
    this.busy.set(false);
    this.statusIsError.set(false);
    this.statusMsg.set(saves.length === 0 ? 'Memory card is empty.' : `${saves.length} save${saves.length === 1 ? '' : 's'} loaded.`);

    const previous = this.selectedSaveName();
    const next = saves.find(s => s.Name === previous)?.Name ?? saves[0]?.Name ?? '';
    this.selectedSaveName.set(next);
    this.loadSelectedSaveIcon();
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

function findFile(save: McSaveInfo, name: string): McFileInfo | null {
  const lower = name.toLowerCase();
  return save.Files.find(f => f.Name.toLowerCase() === lower) ?? null;
}

function sameName(a: string, b: string): boolean {
  return a.localeCompare(b, undefined, { sensitivity: 'accent' }) === 0 ||
    a.toLowerCase() === b.toLowerCase();
}
