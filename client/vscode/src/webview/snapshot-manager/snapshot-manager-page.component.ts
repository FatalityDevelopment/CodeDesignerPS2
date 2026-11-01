import {
  Component,
  AfterViewInit,
  ViewEncapsulation,
  signal,
  computed,
  NgZone,
  inject,
} from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import { IconComponent } from '../shared/icon.component';
import { PageHeaderComponent } from '../shared/page-header.component';
import { ConfirmDialogComponent } from '../shared/components/index';
import { SnapshotEntry } from '../disasm-panel/types';
import '../shared/styles/shared.css';
import './snapshot-manager-page.css';

@Component({
  selector: 'app-snapshot-manager-page',
  standalone: true,
  imports: [IconComponent, PageHeaderComponent, ConfirmDialogComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="sm-shell">
  <app-page-header icon="camera" title="Snapshots" subtitle="PCSX2 save states" />

  <div class="sm-toolbar">
    <div class="sm-title">
      <app-icon name="camera" [size]="16" />
      <span>Snapshot Manager</span>
    </div>
    <div class="sm-toolbar-spacer"></div>
    <span class="sm-count">{{ snapshots().length }} state{{ snapshots().length === 1 ? '' : 's' }}</span>
    <button class="sm-btn" (click)="onRefresh()" title="Refresh from disk">
      <app-icon name="refreshcw" [size]="14" /> Refresh
    </button>
  </div>

  @if (statusMsg()) {
    <div class="sm-status" [class.sm-status-error]="statusIsError()">{{ statusMsg() }}</div>
  }

  <div class="sm-main">
    <aside class="sm-sidebar">
      <label class="sm-search">
        <app-icon name="image" [size]="13" />
        <input
          type="text"
          placeholder="Filter by slot, serial, or date…"
          [value]="filterText()"
          (input)="onFilterInput($event)" />
      </label>

      <div class="sm-list">
        @if (snapshots().length === 0) {
          <div class="sm-empty-row">
            No .p2s files found in PCSX2's savestates folder.
          </div>
        } @else if (filteredSnapshots().length === 0) {
          <div class="sm-empty-row">No snapshots match the filter.</div>
        } @else {
          @for (snap of filteredSnapshots(); track snap.path) {
            <button
              type="button"
              class="sm-row"
              [class.active]="selected()?.path === snap.path"
              (click)="selectSnapshot(snap)">
              <span class="sm-row-label">{{ snap.label }}</span>
              <span class="sm-row-meta">{{ snap.isoName || '—' }}</span>
              <span class="sm-row-meta">{{ snap.timestamp }}</span>
            </button>
          }
        }
      </div>
    </aside>

    <section class="sm-content">
      @if (!selected()) {
        <div class="sm-empty">
          <app-icon name="camera" [size]="34" />
          <span>Select a save state on the left to preview its screenshot.</span>
        </div>
      } @else {
        <div class="sm-head">
          <div class="sm-head-main">
            <div class="sm-file-name">{{ selected()!.label }}</div>
            <div class="sm-summary" [title]="selected()!.name">{{ selected()!.name }}</div>
          </div>
        </div>

        <div class="sm-preview">
          <div class="sm-preview-stage">
            @if (screenshotDataUrl()) {
              <img class="sm-preview-pic" [src]="screenshotDataUrl()" alt="Snapshot screenshot" />
            }
            @if (!screenshotDataUrl() || previewMsg()) {
              <div class="sm-preview-overlay">{{ previewMsg() || 'Loading screenshot…' }}</div>
            }
          </div>
          <div class="sm-preview-info">
            <div><span>Slot</span><strong>{{ slotLabel(selected()!) }}</strong></div>
            <div><span>Serial</span><strong>{{ selected()!.serial || '—' }}</strong></div>
            <div><span>CRC</span><strong>{{ selected()!.crc || '—' }}</strong></div>
            <div><span>Size</span><strong>{{ formatBytes(selected()!.sizeBytes ?? 0) }}</strong></div>
            <div><span>Modified</span><strong>{{ selected()!.timestamp }}</strong></div>
            <div class="sm-actions">
              <button class="sm-btn sm-primary" (click)="onReload()" title="Load this save state into PCSX2">
                <app-icon name="play" [size]="14" /> Load State
              </button>
              <button class="sm-btn" (click)="onOpen()" title="Extract memory dumps and open in the hex viewer">
                <app-icon name="harddrive" [size]="14" /> Open Memory
              </button>
              <button class="sm-btn" (click)="onDiff()" title="Compare with another snapshot">
                <app-icon name="gitcompare" [size]="14" /> Diff
              </button>
              <button class="sm-btn sm-danger-btn" (click)="onDeleteRequest()" title="Delete this save state">
                <svg viewBox="0 0 10 12" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" aria-hidden="true"><path d="M1 3h8M4 1.5h2M2 3l.7 7.5h4.6L8 3"/></svg>
                Delete
              </button>
            </div>
          </div>
        </div>
      }
    </section>
  </div>

  <app-confirm-dialog
    [visible]="!!pendingDeleteSnap()"
    [title]="'Delete snapshot?'"
    [body]="pendingDeleteSnap()?.name ?? ''"
    [note]="'This permanently deletes the .p2s file and any .extracted/ companion from disk.'"
    [confirmLabel]="'Delete'"
    (confirmed)="onConfirmDelete()"
    (cancelled)="pendingDeleteSnap.set(null)">
  </app-confirm-dialog>
</div>
`,
})
export class SnapshotManagerPageComponent implements AfterViewInit {
  private readonly vscode = getVsCodeApi();
  private readonly zone   = inject(NgZone);

  readonly snapshots          = signal<SnapshotEntry[]>([]);
  readonly filterText         = signal('');
  readonly selected           = signal<SnapshotEntry | null>(null);
  readonly pendingDeleteSnap  = signal<SnapshotEntry | null>(null);
  readonly statusMsg          = signal('');
  readonly statusIsError      = signal(false);
  /** Base64 PNG (no data-URI prefix) of the currently selected snapshot's screenshot. */
  readonly screenshotData     = signal<string | null>(null);
  /** Status overlay on the preview area (loading / error message). */
  readonly previewMsg         = signal('');

  readonly screenshotDataUrl = computed(() => {
    const b64 = this.screenshotData();
    return b64 ? `data:image/png;base64,${b64}` : '';
  });

  readonly filteredSnapshots = computed(() => {
    const q = this.filterText().trim().toLowerCase();
    if (!q) { return this.snapshots(); }
    return this.snapshots().filter(s =>
      s.label.toLowerCase().includes(q) ||
      s.name.toLowerCase().includes(q) ||
      (s.serial ?? '').toLowerCase().includes(q) ||
      (s.crc    ?? '').toLowerCase().includes(q) ||
      (s.slot   ?? '').toLowerCase().includes(q) ||
      s.timestamp.toLowerCase().includes(q) ||
      s.date.toLowerCase().includes(q)
    );
  });

  ngAfterViewInit(): void {
    window.addEventListener('message', (event: MessageEvent) => {
      const data = event.data as { type: string; [key: string]: unknown };
      this.zone.run(() => { this._handleMessage(data); });
    });
    this.vscode.postMessage({ type: 'ready' });
  }

  // ── UI handlers ─────────────────────────────────────────────────────────

  onRefresh(): void {
    this.vscode.postMessage({ type: 'refresh' });
  }

  onFilterInput(evt: Event): void {
    this.filterText.set((evt.target as HTMLInputElement).value);
  }

  selectSnapshot(snap: SnapshotEntry): void {
    if (this.selected()?.path === snap.path) { return; }
    this.selected.set(snap);
    this.screenshotData.set(null);
    this.previewMsg.set('Loading screenshot…');
    this.vscode.postMessage({ type: 'snapshotScreenshot', path: snap.path });
  }

  onOpen(): void {
    const snap = this.selected();
    if (!snap) { return; }
    this.vscode.postMessage({ type: 'snapshotOpen', path: snap.path });
  }

  onReload(): void {
    const snap = this.selected();
    if (!snap) { return; }
    this.vscode.postMessage({ type: 'snapshotReload', path: snap.path });
  }

  onDiff(): void {
    const snap = this.selected();
    if (!snap) { return; }
    this.vscode.postMessage({ type: 'snapshotDiff', snapshot: snap });
  }

  onDeleteRequest(): void {
    const snap = this.selected();
    if (!snap) { return; }
    this.pendingDeleteSnap.set(snap);
  }

  onConfirmDelete(): void {
    const snap = this.pendingDeleteSnap();
    if (!snap) { return; }
    this.pendingDeleteSnap.set(null);
    this.vscode.postMessage({ type: 'snapshotDelete', path: snap.path });
  }

  // ── Helpers used from the template ──────────────────────────────────────

  slotLabel(snap: SnapshotEntry): string {
    if (!snap.slot) { return '—'; }
    if (snap.slot === 'resume') { return 'Resume'; }
    if (/^\d+$/.test(snap.slot)) { return `Slot ${parseInt(snap.slot, 10)}`; }
    return snap.slot;
  }

  formatBytes(n: number): string {
    if (!n || n <= 0) { return '—'; }
    const units = ['B', 'KB', 'MB', 'GB'];
    let i = 0;
    let v = n;
    while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
    return `${v.toFixed(v < 10 && i > 0 ? 2 : 0)} ${units[i]}`;
  }

  // ── Message dispatch ────────────────────────────────────────────────────

  private _handleMessage(data: { type: string; [key: string]: unknown }): void {
    switch (data['type']) {
      case 'snapshotList': {
        const list = (data['snapshots'] as SnapshotEntry[] | undefined) ?? [];
        this.snapshots.set(list);
        // Preserve the current selection across refreshes when possible.
        const stillSelected = this.selected();
        if (stillSelected && !list.some(s => s.path === stillSelected.path)) {
          this.selected.set(null);
          this.screenshotData.set(null);
          this.previewMsg.set('');
        }
        const err = data['error'] as string | undefined;
        this._showStatus(err ?? '', !!err);
        break;
      }

      case 'snapshotScreenshot': {
        // Race-protection: ignore screenshots that arrive after the user
        // already moved on to a different row.
        if (this.selected()?.path !== data['path']) { return; }
        const b64 = data['data'] as string | null | undefined;
        if (b64) {
          this.screenshotData.set(b64);
          this.previewMsg.set('');
        } else {
          this.screenshotData.set(null);
          const err = data['error'] as string | undefined;
          this.previewMsg.set(err ?? 'No screenshot embedded in this save state.');
        }
        break;
      }

      case 'snapshotActionResult':
        if (data['error']) {
          this._showStatus(data['error'] as string, true);
        } else {
          this._showStatus((data['message'] as string) ?? '', false);
          this.vscode.postMessage({ type: 'refresh' });
        }
        break;

      case 'snapshotError':
        this._showStatus((data['message'] as string) ?? 'An error occurred.', true);
        break;
    }
  }

  private _showStatus(msg: string, isError = false): void {
    this.statusMsg.set(msg);
    this.statusIsError.set(isError);
  }
}
