import { Component, OnDestroy, OnInit, ViewEncapsulation, computed, signal } from '@angular/core';
import { IconComponent } from '../shared/icon.component';
import { getVsCodeApi } from '../shared/vscode-api';
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
              <strong>{{ result()?.entries?.length || saveRecordCount() }}</strong>
            </div>
          </div>

          @if (result()?.entries?.length) {
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
                <div class="zip-file-name">{{ result()?.fileName }}</div>
                <div class="zip-summary">{{ result()?.summary }}</div>
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
export class ZipperWorkbenchPageComponent implements OnInit, OnDestroy {
  private readonly vscode = getVsCodeApi();

  readonly result = signal<ZipperInspectResult | null>(null);
  readonly search = signal('');
  readonly selectedIndex = signal<number | null>(null);
  readonly busy = signal(false);
  readonly statusMsg = signal('');
  readonly statusIsError = signal(false);
  readonly lastOperation = signal<ZipperOperationResult | null>(null);

  readonly filteredEntries = computed(() => {
    const current = this.result()?.entries ?? [];
    const q = this.search().trim().toLowerCase();
    if (!q) { return current; }
    return current.filter(entry =>
      `${entry.name} ${entry.path} ${entry.kind} ${entry.extension ?? ''}`.toLowerCase().includes(q));
  });

  readonly selectedEntry = computed(() => {
    const idx = this.selectedIndex();
    const entries = this.result()?.entries ?? [];
    if (idx === null) { return entries[0] ?? null; }
    return entries.find(e => e.index === idx) ?? entries[0] ?? null;
  });

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
        this.setStatus(this.result()?.summary ?? 'File loaded.', false, false);
        break;
      case 'zipperOperation':
        this.lastOperation.set((data['result'] as ZipperOperationResult) ?? null);
        this.setStatus((data['message'] as string) ?? 'Operation complete.', false, false);
        break;
    }
  };

  ngOnInit(): void {
    window.addEventListener('message', this.onMessage);
    this.vscode.postMessage({ type: 'ready' });
  }

  ngOnDestroy(): void {
    window.removeEventListener('message', this.onMessage);
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
}
