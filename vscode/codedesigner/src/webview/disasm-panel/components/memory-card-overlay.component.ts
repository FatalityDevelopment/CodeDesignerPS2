import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  signal,
  output,
} from '@angular/core';

export interface McSaveEntry {
  Name:       string;
  FileCount:  number;
  Created:    string;
  Modified:   string;
  TotalBytes: number;
  Files:      Array<{ Name: string; Size: number }>;
}

export interface McListResult {
  saves?: McSaveEntry[];
  error?: string;
}

export interface McActionResult {
  error?: string;
  message?: string;
}

@Component({
  selector: 'app-memory-card-overlay',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
<div id="mc-overlay"
     [class.visible]="visible()"
     (click)="onBackdropClick($event)"
     (keydown.escape)="close()">
  <div id="mc-overlay-box">
    <div id="mc-overlay-header">
      <span id="mc-overlay-title">&#x1F4BE; Memory Card Manager</span>
      <button id="mc-overlay-close" title="Close (Escape)" (click)="close()">&#xD7;</button>
    </div>
    <div id="mc-overlay-body">

      <!-- Card file selector -->
      <div class="mc-field-row">
        <input type="text"
               id="mc-path-input"
               class="mc-path-input"
               readonly
               placeholder="Select a .ps2 memory card file…"
               [value]="mcPath()" />
        <button class="mc-browse-btn"
                title="Open an existing memory card (.ps2)"
                (click)="browseMc.emit()">&#8230;</button>
        <button class="mc-new-btn"
                title="Create a new blank 8 MB memory card"
                (click)="newMc.emit()">New</button>
      </div>

      <!-- Load / Refresh -->
      <div class="mc-controls-row">
        <button id="mc-load-btn"
                [disabled]="!mcPath() || loading()"
                (click)="load()">
          {{ loading() ? 'Loading…' : '&#x21BB; Refresh' }}
        </button>
        <button id="mc-import-btn"
                [disabled]="!mcPath() || loading()"
                title="Import a save folder from the host into this memory card"
                (click)="importSave.emit(mcPath())">&#x2B06; Import Save</button>
        <span class="mc-status"
              [class.mc-status-ok]="statusOk()"
              [class.mc-status-error]="statusError()">{{ statusMessage() }}</span>
      </div>

      <!-- Saves table -->
      <div id="mc-saves-area">
        @if (saves().length === 0 && hasLoaded()) {
          <div class="mc-empty">No saves found on this memory card.</div>
        }
        @if (saves().length > 0) {
          <table class="mc-table">
            <thead>
              <tr>
                <th class="mc-col-name">Save</th>
                <th class="mc-col-files">Files</th>
                <th class="mc-col-size">Size</th>
                <th class="mc-col-modified">Modified</th>
                <th class="mc-col-actions"></th>
              </tr>
            </thead>
            <tbody>
              @for (save of saves(); track save.Name) {
                <tr class="mc-row">
                  <td class="mc-col-name" [title]="save.Name">{{ save.Name }}</td>
                  <td class="mc-col-files">{{ save.FileCount }}</td>
                  <td class="mc-col-size">{{ formatBytes(save.TotalBytes) }}</td>
                  <td class="mc-col-modified">{{ save.Modified }}</td>
                   <td class="mc-col-actions">
                    @if (pendingDelete() === save.Name) {
                      <span class="mc-confirm-delete">
                        Delete?
                        <button class="mc-action-btn mc-delete-btn"
                                (click)="doDelete(save.Name)">Yes</button>
                        <button class="mc-action-btn"
                                (click)="pendingDelete.set(null)">No</button>
                      </span>
                    } @else {
                      <button class="mc-action-btn"
                              title="Export this save to a folder on the host"
                              (click)="exportSave.emit({ mcPath: mcPath(), saveName: save.Name })">
                        &#x2B07; Export
                      </button>
                      <button class="mc-action-btn mc-delete-btn"
                              title="Delete this save from the memory card"
                              (click)="pendingDelete.set(save.Name)">
                        &#x2715; Delete
                      </button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>

    </div>
  </div>
</div>
  `,
})
export class MemoryCardOverlayComponent {

  // ── Outputs ─────────────────────────────────────────────────────────────────

  /** Emitted when the user clicks the browse (…) button. */
  readonly browseMc = output<void>();

  /** Emitted when the user clicks "New" — create a blank card. */
  readonly newMc = output<void>();

  /** Emitted when the user clicks "Import Save" — carries the current mcPath. */
  readonly importSave = output<string>();

  /** Emitted when the user triggers an export. */
  readonly exportSave = output<{ mcPath: string; saveName: string }>();

  /** Emitted when the user confirms a delete. */
  readonly deleteSave = output<{ mcPath: string; saveName: string }>();

  /** Emitted when the user triggers a refresh / initial load. */
  readonly listSaves = output<string>();

  /** Emitted on close. */
  readonly close$ = output<void>();

  // ── State ───────────────────────────────────────────────────────────────────

  readonly visible      = signal(false);
  readonly mcPath       = signal('');
  readonly saves        = signal<McSaveEntry[]>([]);
  readonly hasLoaded    = signal(false);
  readonly loading      = signal(false);
  readonly statusMessage = signal('');
  readonly statusOk     = signal(false);
  readonly statusError  = signal(false);
  /** Name of the save awaiting delete confirmation, or null. */
  readonly pendingDelete = signal<string | null>(null);

  // ── Public API ──────────────────────────────────────────────────────────────

  open(): void {
    this.visible.set(true);
  }

  close(): void {
    if (this.visible()) {
      this.visible.set(false);
      this.close$.emit();
    }
  }

  setMcPath(p: string): void {
    this.mcPath.set(p);
    // Auto-load on path set
    if (p) { this.load(); }
  }

  setListResult(result: McListResult): void {
    this.loading.set(false);
    this.hasLoaded.set(true);
    this.pendingDelete.set(null);
    if (result.error) {
      this.setStatus('Error: ' + result.error, true);
      this.saves.set([]);
    } else {
      const count = result.saves?.length ?? 0;
      this.saves.set(result.saves ?? []);
      this.setStatus(count === 0 ? 'Memory card is empty.' : `${count} save${count === 1 ? '' : 's'} loaded.`, false);
    }
  }

  setActionResult(result: McActionResult): void {
    this.loading.set(false);
    if (result.error) {
      this.setStatus('Error: ' + result.error, true);
    } else {
      this.setStatus(result.message ?? 'Done.', false);
      // Refresh the save list
      const p = this.mcPath();
      if (p) { this.load(); }
    }
  }

  // ── Event Handlers ──────────────────────────────────────────────────────────

  load(): void {
    const p = this.mcPath().trim();
    if (!p) { return; }
    this.loading.set(true);
    this.setStatus('Loading…', false);
    this.listSaves.emit(p);
  }

  confirmDelete(saveName: string): void {
    this.pendingDelete.set(saveName);
  }

  doDelete(saveName: string): void {
    this.pendingDelete.set(null);
    this.loading.set(true);
    this.setStatus('Deleting…', false);
    this.deleteSave.emit({ mcPath: this.mcPath(), saveName });
  }

  onBackdropClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).id === 'mc-overlay') {
      this.close();
    }
  }

  formatBytes(bytes: number): string {
    if (bytes < 1024) { return `${bytes} B`; }
    if (bytes < 1024 * 1024) { return `${(bytes / 1024).toFixed(1)} KB`; }
    return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
  }

  // ── Private ─────────────────────────────────────────────────────────────────

  private setStatus(msg: string, isError: boolean): void {
    this.statusMessage.set(msg);
    this.statusOk.set(!isError);
    this.statusError.set(isError);
  }
}
