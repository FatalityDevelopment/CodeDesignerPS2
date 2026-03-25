import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  ElementRef,
  signal,
  output,
  viewChild,
} from '@angular/core';

export interface PorterResult {
  blockIndex: number;
  blockName: string;
  blockText: string | null;
  originalCode: string;
  newCode: string | null;
  found: boolean;
  method: string;
  confidence: number;
  ambiguous: boolean;
  candidates: string[] | null;
}

export interface PortRequest {
  codes: string;
  sourcePath: string;
  targetPath: string;
}

export interface PortResultPayload {
  results?: PorterResult[];
  error?: string;
}

/**
 * Internal grouping for rendering cards.
 */
interface PorterGroup {
  blockIndex: number;
  blockName: string;
  blockText: string | null;
  entries: PorterResult[];
  allFound: boolean;
  anyFound: boolean;
  checked: boolean;
}

@Component({
  selector: 'app-porter-overlay',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
<div id="porter-overlay"
     [class.visible]="visible()"
     (click)="onBackdropClick($event)"
     (keydown.escape)="close()">
  <div id="porter-overlay-box">
    <div id="porter-overlay-header">
      <span id="porter-overlay-title">&#8651; Cheat Code Porter</span>
      <button id="porter-overlay-close" title="Close (Escape)" (click)="close()">&#xD7;</button>
    </div>
    <div id="porter-overlay-body">
      <div class="porter-overlay-field">
        <label class="porter-overlay-label" for="porter-input">Cheat Codes (raw GameShark format)</label>
        <textarea id="porter-input"
                  class="porter-overlay-textarea"
                  spellcheck="false"
                  placeholder="#Code Name&#10;20XXXXXX YYYYYYYY"
                  #inputArea
                  [value]="inputValue()"
                  (input)="inputValue.set(asTextarea($event).value)"></textarea>
      </div>
      <div class="porter-dump-row">
        <div class="porter-overlay-field porter-dump-field">
          <label class="porter-overlay-label">Source Dump</label>
          <div class="porter-file-row">
            <input type="text" id="porter-source-path"
                   class="porter-file-input"
                   readonly
                   placeholder="Select source EE RAM dump…"
                   [value]="sourcePath()" />
            <button id="porter-source-browse"
                    class="porter-browse-btn"
                    title="Browse for source RAM dump file"
                    (click)="browseSource.emit()">&#8230;</button>
          </div>
        </div>
        <div class="porter-overlay-field porter-dump-field">
          <label class="porter-overlay-label">Target Dump</label>
          <div class="porter-file-row">
            <input type="text" id="porter-target-path"
                   class="porter-file-input"
                   readonly
                   placeholder="Select target EE RAM dump…"
                   [value]="targetPath()" />
            <button id="porter-target-browse"
                    class="porter-browse-btn"
                    title="Browse for target RAM dump file"
                    (click)="browseTarget.emit()">&#8230;</button>
          </div>
        </div>
      </div>
      <div id="porter-overlay-controls">
        <button id="porter-port-btn"
                [disabled]="porting()"
                (click)="port()">&#8651; Port Cheats</button>
        <span id="porter-overlay-status"
              class="porter-overlay-status"
              [class.porter-status-ok]="statusOk()"
              [class.porter-status-error]="statusError()">{{ statusMessage() }}</span>
      </div>
      <div id="porter-results">
        @if (groups().length === 0 && hasResults()) {
          <div class="porter-no-results">No cheat codes found in input.</div>
        }
        @for (group of groups(); track group.blockIndex) {
          <div class="porter-result-card">
            <div class="porter-result-header">
              <input type="checkbox"
                     class="porter-result-cb"
                     [checked]="group.checked"
                     [disabled]="!group.anyFound"
                     (change)="toggleGroup(group, asCheckbox($event).checked)" />
              <span class="porter-result-name">{{ group.blockName || '(unnamed)' }}</span>
              <span class="porter-badge"
                    [class.porter-badge-ok]="group.allFound"
                    [class.porter-badge-warn]="!group.allFound && group.anyFound"
                    [class.porter-badge-err]="!group.anyFound">
                {{ group.allFound ? '✓ found' : group.anyFound ? '⚠ partial' : '✗ not found' }}
              </span>
            </div>
            @if (group.blockText) {
              <div class="porter-block-text">{{ group.blockText }}</div>
            }
            @for (entry of group.entries; track entry.originalCode) {
              <div class="porter-result-line">
                <span class="porter-addr-old">{{ entry.originalCode }}</span>
                <span class="porter-arrow"> → </span>
                @if (entry.found && entry.newCode) {
                  <span class="porter-addr-new">{{ entry.newCode }}</span>
                  <span class="porter-method"
                        [title]="entry.ambiguous
                          ? (entry.candidates?.length ?? 0) + ' candidates — pick manually'
                          : 'Confidence: ' + entry.confidence + '%'">
                    &nbsp;&nbsp;{{ entry.method }}{{ entry.ambiguous ? ' ⚠' : '' }}
                  </span>
                } @else {
                  <span class="porter-not-found">not found</span>
                  <span class="porter-method">&nbsp;&nbsp;{{ entry.method }}</span>
                }
              </div>
              @if (entry.ambiguous && entry.candidates) {
                <div class="porter-candidates">Candidates: {{ entry.candidates.join('  ') }}</div>
              }
            }
          </div>
        }
      </div>
      @if (showCopyRow()) {
        <div id="porter-copy-row" class="porter-copy-row">
          <button id="porter-copy-btn" (click)="copySelected()">Copy Selected Codes</button>
        </div>
      }
    </div>
  </div>
</div>
  `,
})
export class PorterOverlayComponent {
  // ── Outputs ─────────────────────────────────────────────────────────────────

  readonly close$ = output<void>();
  readonly port$ = output<PortRequest>();
  readonly browseSource = output<void>();
  readonly browseTarget = output<void>();

  // ── State ───────────────────────────────────────────────────────────────────

  readonly visible = signal(false);
  readonly inputValue = signal('');
  readonly sourcePath = signal('');
  readonly targetPath = signal('');
  readonly statusMessage = signal('');
  readonly statusOk = signal(false);
  readonly statusError = signal(false);
  readonly porting = signal(false);
  readonly groups = signal<PorterGroup[]>([]);
  readonly hasResults = signal(false);
  readonly showCopyRow = signal(false);

  private _resultData: PorterResult[] = [];

  private readonly inputAreaRef = viewChild<ElementRef<HTMLTextAreaElement>>('inputArea');

  // ── Public API ──────────────────────────────────────────────────────────────

  open(): void {
    this.statusMessage.set('');
    this.statusOk.set(false);
    this.statusError.set(false);
    this.visible.set(true);
    requestAnimationFrame(() => {
      this.inputAreaRef()?.nativeElement.focus();
    });
  }

  close(): void {
    if (this.visible()) {
      this.visible.set(false);
      this.close$.emit();
    }
  }

  /** Called by parent when browseSourceDumpResult arrives. */
  setSourcePath(path: string): void {
    this.sourcePath.set(path);
  }

  /** Called by parent when browseTargetDumpResult arrives. */
  setTargetPath(path: string): void {
    this.targetPath.set(path);
  }

  /** Called by parent when portCheatsResult arrives. */
  setResult(payload: PortResultPayload): void {
    this.porting.set(false);
    if (payload.error) {
      this.statusMessage.set('Error: ' + payload.error);
      this.statusOk.set(false);
      this.statusError.set(true);
      return;
    }
    const results = payload.results ?? [];
    this._resultData = results;
    this.hasResults.set(true);

    if (results.length === 0) {
      this.groups.set([]);
      this.showCopyRow.set(false);
    } else {
      this.groups.set(this.buildGroups(results));
      this.showCopyRow.set(true);
    }

    const found = results.filter(r => r.found).length;
    const total = results.length;
    this.statusMessage.set(
      `Done — ${found} of ${total} code${total === 1 ? '' : 's'} ported. See output panel for details.`
    );
    this.statusOk.set(found === total);
    this.statusError.set(found !== total);
  }

  // ── Event Handlers ──────────────────────────────────────────────────────────

  port(): void {
    const codes = this.inputValue().trim();
    if (!codes) {
      this.setStatus('Please enter cheat codes in the input box.', true);
      return;
    }
    const src = this.sourcePath().trim();
    if (!src) {
      this.setStatus('Please select a source EE RAM dump.', true);
      return;
    }
    const tgt = this.targetPath().trim();
    if (!tgt) {
      this.setStatus('Please select a target EE RAM dump.', true);
      return;
    }

    this.setStatus('Porting…', false);
    this.porting.set(true);
    this.groups.set([]);
    this.hasResults.set(false);
    this.showCopyRow.set(false);

    this.port$.emit({ codes, sourcePath: src, targetPath: tgt });
  }

  toggleGroup(group: PorterGroup, checked: boolean): void {
    const updated = this.groups().map(g =>
      g.blockIndex === group.blockIndex ? { ...g, checked } : g
    );
    this.groups.set(updated);
  }

  copySelected(): void {
    const selectedGroups = this.groups().filter(g => g.checked);
    if (selectedGroups.length === 0) {
      this.setStatus('No codes selected.', true);
      return;
    }
    const lines: string[] = [];
    for (const group of selectedGroups) {
      const entries = this._resultData.filter(
        e => e.blockIndex === group.blockIndex && e.found && e.newCode
      );
      if (entries.length === 0) { continue; }
      if (group.blockName) { lines.push(group.blockName); }
      if (group.blockText) { lines.push(group.blockText); }
      for (const entry of entries) { lines.push(entry.newCode!); }
      lines.push('');
    }
    const text = lines.join('\n').trimEnd();
    navigator.clipboard.writeText(text).then(() => {
      this.setStatus('Copied to clipboard!', false);
    }).catch(() => {
      this.setStatus('Copy failed — try again.', true);
    });
  }

  onBackdropClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).id === 'porter-overlay') {
      this.close();
    }
  }

  asTextarea(event: Event): HTMLTextAreaElement {
    return event.target as HTMLTextAreaElement;
  }

  asCheckbox(event: Event): HTMLInputElement {
    return event.target as HTMLInputElement;
  }

  // ── Private ─────────────────────────────────────────────────────────────────

  private setStatus(msg: string, isError: boolean): void {
    this.statusMessage.set(msg);
    this.statusOk.set(!isError);
    this.statusError.set(isError);
  }

  private buildGroups(results: PorterResult[]): PorterGroup[] {
    const map = new Map<number, PorterResult[]>();
    for (const r of results) {
      const arr = map.get(r.blockIndex) ?? [];
      arr.push(r);
      map.set(r.blockIndex, arr);
    }
    const groups: PorterGroup[] = [];
    for (const [blockIndex, entries] of map) {
      const allFound = entries.every(e => e.found);
      const anyFound = entries.some(e => e.found);
      groups.push({
        blockIndex,
        blockName: entries[0]?.blockName ?? '',
        blockText: entries[0]?.blockText ?? null,
        entries,
        allFound,
        anyFound,
        checked: allFound,
      });
    }
    return groups;
  }
}
