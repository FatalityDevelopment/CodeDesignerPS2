import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  signal,
  computed,
  output,
} from '@angular/core';

export interface PorterResult {
  blockIndex:   number;
  blockName:    string;
  blockText:    string | null;
  originalCode: string;
  newCode:      string | null;
  found:        boolean;
  method:       string;
  confidence:   number;
  ambiguous:    boolean;
  candidates:   string[] | null;
}

export interface PortRequest {
  codes:      string;
  sourcePath: string;
  targetPath: string;
}

export interface PortResultPayload {
  results?: PorterResult[];
  error?:   string;
}

interface FlatRow {
  blockIndex:   number;
  blockName:    string;
  blockText:    string | null;
  originalCode: string;
  newCode:      string | null;
  found:        boolean;
  method:       string;
  ambiguous:    boolean;
  candidates:   string[] | null;
  checked:      boolean;
}

type GroupHeader = {
  kind:          'group';
  blockIndex:    number;
  blockName:     string;
  blockText:     string | null;
  groupChecked:  boolean;
  groupAllFound: boolean;
  groupAnyFound: boolean;
};
type CodeRow = FlatRow & { kind: 'row' };
type ViewRow = GroupHeader | CodeRow;

@Component({
  selector: 'app-porter-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  /* Component-specific styles only. Table/pagination/button primitives come from
     PaginatedTableComponent inline styles and shared.css (loaded by diff-viewer-page). */
  styles: [`
app-porter-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

/* ── Toolbar ─────────────────────────────────────────────────────────────── */
.pp-toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 10px;
  background: #252526;
  border-bottom: 1px solid #3e3e42;
  flex-shrink: 0;
  flex-wrap: wrap;
}
.pp-toolbar-title {
  font-size: 12px;
  font-weight: 600;
  color: #d4d4d4;
  white-space: nowrap;
  margin-right: 4px;
}
.pp-file-group {
  display: flex;
  align-items: center;
  gap: 4px;
  flex: 1;
  min-width: 180px;
}
.pp-file-label {
  font-size: 11px;
  color: #9cdcfe;
  white-space: nowrap;
  font-family: monospace;
}
.pp-file-path {
  flex: 1;
  background: #1e1e1e;
  border: 1px solid #3e3e42;
  color: #b5b5b5;
  font-family: monospace;
  font-size: 11px;
  padding: 3px 7px;
  border-radius: 2px;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  user-select: text;
}
.pp-file-path.pp-empty { color: #555; font-style: italic; }
.pp-port-btn {
  background: #0078d4;
  border: 1px solid transparent;
  color: #fff;
  padding: 4px 14px;
  cursor: pointer;
  font-size: 12px;
  border-radius: 2px;
  white-space: nowrap;
  flex-shrink: 0;
  font-family: inherit;
}
.pp-port-btn:disabled { opacity: .5; cursor: default; }
.pp-port-btn:not(:disabled):hover { background: #1a8ad4; }
.pp-status { font-size: 11px; white-space: nowrap; }
.pp-status-ok  { color: #73c991; }
.pp-status-err { color: #f48771; }

/* ── Body ────────────────────────────────────────────────────────────────── */
.pp-body {
  display: flex;
  flex-direction: column;
  flex: 1;
  overflow: hidden;
}
.pp-input-area {
  padding: 8px 10px;
  border-bottom: 1px solid #3e3e42;
  flex-shrink: 0;
}
.pp-input-label {
  display: block;
  font-size: 11px;
  color: #9cdcfe;
  font-family: monospace;
  margin-bottom: 4px;
}
.pp-codes-textarea {
  width: 100%;
  box-sizing: border-box;
  resize: vertical;
  background: #1e1e1e;
  border: 1px solid #3e3e42;
  color: #d4d4d4;
  font-family: monospace;
  font-size: 12px;
  padding: 6px 8px;
  border-radius: 2px;
  outline: none;
  min-height: 72px;
  height: 100px;
}
.pp-codes-textarea:focus { border-color: #007acc; }

/* ── Results area ────────────────────────────────────────────────────────── */
.pp-results-area {
  display: flex;
  flex-direction: column;
  flex: 1;
  overflow: hidden;
}
.pp-no-results {
  color: #666;
  font-size: 12px;
  text-align: center;
  padding: 24px;
}

/* ── Table badges ────────────────────────────────────────────────────────── */
.pp-badge {
  display: inline-block;
  font-size: 10px;
  padding: 1px 5px;
  border-radius: 2px;
  font-weight: 600;
  font-family: monospace;
  white-space: nowrap;
}
.pp-badge-ok     { background: #0a2a10; color: #73c991; border: 1px solid #0a4020; }
.pp-badge-warn   { background: #2a2200; color: #dcdcaa; border: 1px solid #4a3c00; }
.pp-badge-err    { background: #2a0a0a; color: #f48771; border: 1px solid #5a1d1d; }
.pp-badge-method { background: #1e2a3a; color: #9cdcfe; border: 1px solid #1a3a5a; font-size: 9px; }

/* ── Group header rows ───────────────────────────────────────────────────── */
.pp-group-row > td {
  background: #2d2d30;
  color: #ccc;
  font-size: 11px;
  font-weight: 600;
  padding: 5px 8px;
  border-bottom: 1px solid #3e3e42;
  border-top: 1px solid #3e3e42;
}

/* ── Code cells ──────────────────────────────────────────────────────────── */
.pp-code-old  { font-family: monospace; font-size: 11px; color: #808080; }
.pp-code-new  { font-family: monospace; font-size: 11px; color: #9cdcfe; }
.pp-code-miss { font-family: monospace; font-size: 11px; color: #f48771; }
.pp-arrow     { color: #555; padding: 0 2px; }
.pp-candidates { font-size: 10px; color: #dcdcaa; font-family: monospace; margin-top: 2px; }

/* ── Footer copy row ─────────────────────────────────────────────────────── */
.pp-copy-row {
  padding: 6px 10px;
  border-top: 1px solid #3e3e42;
  flex-shrink: 0;
  background: #252526;
  display: flex;
  align-items: center;
  gap: 8px;
}
`],
  template: `
<!-- ── Toolbar ──────────────────────────────────────────────────────────── -->
<div class="pp-toolbar">
  <span class="pp-toolbar-title">&#x21CB; Cheat Code Porter</span>

  <div class="pp-file-group">
    <span class="pp-file-label">Source:</span>
    <span class="pp-file-path" [class.pp-empty]="!sourcePath()" [title]="sourcePath()">
      {{ sourcePath() || 'No file selected…' }}
    </span>
    <button class="lm-tool-btn" (click)="browseSource.emit()" title="Browse for source EE RAM dump">&#x2026;</button>
  </div>

  <div class="pp-file-group">
    <span class="pp-file-label">Target:</span>
    <span class="pp-file-path" [class.pp-empty]="!targetPath()" [title]="targetPath()">
      {{ targetPath() || 'No file selected…' }}
    </span>
    <button class="lm-tool-btn" (click)="browseTarget.emit()" title="Browse for target EE RAM dump">&#x2026;</button>
  </div>

  <button class="pp-port-btn" [disabled]="porting()" (click)="onPort()">
    @if (porting()) { &#x23F3; Porting… } @else { &#x21CB; Port Cheats }
  </button>

  @if (statusMessage()) {
    <span class="pp-status" [class.pp-status-ok]="statusOk()" [class.pp-status-err]="statusError()">
      {{ statusMessage() }}
    </span>
  }
</div>

<!-- ── Body ─────────────────────────────────────────────────────────────── -->
<div class="pp-body">

  <!-- Code input -->
  <div class="pp-input-area">
    <span class="pp-input-label">Cheat Codes (raw GameShark / pnach format)</span>
    <textarea class="pp-codes-textarea"
              spellcheck="false"
              placeholder="#Code Name&#10;20XXXXXX YYYYYYYY"
              [value]="inputValue()"
              (input)="inputValue.set(asTextarea($event).value)"></textarea>
  </div>

  <!-- Results -->
  <div class="pp-results-area">
    @if (flatRows().length === 0 && hasResults()) {
      <div class="pp-no-results">No cheat codes found in input.</div>
    }

    @if (flatRows().length > 0) {
      <!-- Filter row — reuses paginated-table lm-* styles -->
      <div class="lm-filter-row">
        <input class="lm-filter" type="text" spellcheck="false"
               placeholder="Filter by block name or code…"
               [value]="filterText()"
               (input)="filterText.set(asInput($event).value)" />
        @if (filterText()) {
          <button class="lm-clear-filter" (click)="filterText.set('')">&#xD7;</button>
        }
        <span class="lm-muted" style="font-size:11px;white-space:nowrap">
          {{ visibleCodeRows() }} / {{ flatRows().length }}
        </span>
      </div>

      <!-- Results table — reuses lm-table-wrap / lm-table styles -->
      <div class="lm-table-wrap">
        <table class="lm-table">
          <thead>
            <tr>
              <th style="width:28px;text-align:center;padding:5px 4px">
                <input type="checkbox" title="Select / deselect all found codes"
                       [checked]="allChecked()"
                       (change)="onToggleAll(asCheckbox($event).checked)" />
              </th>
              <th>Source Code</th>
              <th style="width:20px"></th>
              <th>Ported Code</th>
              <th style="width:90px">Method</th>
              <th style="width:52px;text-align:center">Status</th>
            </tr>
          </thead>
          <tbody>
            @for (row of groupedView(); track $index) {
              @if (row.kind === 'group') {
                <!-- Block header row -->
                <tr class="pp-group-row">
                  <td colspan="6">
                    <input type="checkbox" style="margin-right:6px;vertical-align:middle"
                           [checked]="row.groupChecked"
                           [disabled]="!row.groupAnyFound"
                           (change)="onToggleBlock(row.blockIndex, asCheckbox($event).checked)" />
                    {{ row.blockName || '(unnamed block)' }}
                    @if (row.blockText) {
                      <span class="lm-muted" style="margin-left:8px;font-weight:400;font-size:10px">
                        {{ row.blockText }}
                      </span>
                    }
                    <span class="pp-badge" style="margin-left:8px"
                          [class.pp-badge-ok]="row.groupAllFound"
                          [class.pp-badge-warn]="!row.groupAllFound && row.groupAnyFound"
                          [class.pp-badge-err]="!row.groupAnyFound">
                      {{ row.groupAllFound ? '✓ all found' : row.groupAnyFound ? '⚠ partial' : '✗ not found' }}
                    </span>
                  </td>
                </tr>
              } @else {
                <!-- Code row -->
                <tr>
                  <td style="text-align:center;padding:4px 4px">
                    <input type="checkbox"
                           [checked]="row.checked"
                           [disabled]="!row.found"
                           (change)="onToggleRow(row, asCheckbox($event).checked)" />
                  </td>
                  <td class="pp-code-old">{{ row.originalCode }}</td>
                  <td class="pp-arrow">→</td>
                  <td>
                    @if (row.found && row.newCode) {
                      <span class="pp-code-new">{{ row.newCode }}</span>
                    } @else {
                      <span class="pp-code-miss">not found</span>
                    }
                    @if (row.ambiguous && row.candidates) {
                      <div class="pp-candidates">Alt: {{ row.candidates.join('  ') }}</div>
                    }
                  </td>
                  <td>
                    @if (row.method) {
                      <span class="pp-badge pp-badge-method">{{ row.method }}</span>
                      @if (row.ambiguous) { <span class="pp-badge pp-badge-warn" style="margin-left:3px">⚠</span> }
                    }
                  </td>
                  <td style="text-align:center">
                    <span class="pp-badge"
                          [class.pp-badge-ok]="row.found && !row.ambiguous"
                          [class.pp-badge-warn]="row.found && row.ambiguous"
                          [class.pp-badge-err]="!row.found">
                      {{ row.found ? (row.ambiguous ? '⚠' : '✓') : '✗' }}
                    </span>
                  </td>
                </tr>
              }
            }
          </tbody>
        </table>
      </div>

      <div class="pp-copy-row">
        <button class="lm-tool-btn" (click)="copySelected()">&#x1F4CB; Copy Selected Codes</button>
      </div>
    }
  </div>
</div>
`,
})
export class PorterPageComponent {
  readonly browseSource = output<void>();
  readonly browseTarget = output<void>();
  readonly port$        = output<PortRequest>();

  readonly inputValue    = signal('');
  readonly sourcePath    = signal('');
  readonly targetPath    = signal('');
  readonly statusMessage = signal('');
  readonly statusOk      = signal(false);
  readonly statusError   = signal(false);
  readonly porting       = signal(false);
  readonly flatRows      = signal<FlatRow[]>([]);
  readonly hasResults    = signal(false);
  readonly filterText    = signal('');

  setSourcePath(p: string): void { this.sourcePath.set(p); }
  setTargetPath(p: string): void { this.targetPath.set(p); }

  readonly filteredRows = computed(() => {
    const q = this.filterText().toLowerCase();
    if (!q) { return this.flatRows(); }
    return this.flatRows().filter(r =>
      r.blockName.toLowerCase().includes(q) ||
      r.originalCode.toLowerCase().includes(q) ||
      (r.newCode ?? '').toLowerCase().includes(q)
    );
  });

  readonly visibleCodeRows = computed(() => this.filteredRows().length);

  readonly groupedView = computed((): ViewRow[] => {
    const rows   = this.filteredRows();
    const result: ViewRow[] = [];
    let lastBlock = -1;

    for (const row of rows) {
      if (row.blockIndex !== lastBlock) {
        lastBlock = row.blockIndex;
        const blockRows    = rows.filter(r => r.blockIndex === row.blockIndex);
        const groupChecked  = blockRows.some(r => r.checked);
        const groupAllFound = blockRows.every(r => r.found);
        const groupAnyFound = blockRows.some(r => r.found);
        result.push({
          kind: 'group',
          blockIndex: row.blockIndex,
          blockName:  row.blockName,
          blockText:  row.blockText,
          groupChecked, groupAllFound, groupAnyFound,
        } as GroupHeader);
      }
      result.push({ ...row, kind: 'row' } as CodeRow);
    }
    return result;
  });

  readonly allChecked = computed(() => {
    const found = this.flatRows().filter(r => r.found);
    return found.length > 0 && found.every(r => r.checked);
  });

  setResult(payload: PortResultPayload): void {
    this.porting.set(false);
    if (payload.error) {
      this._setStatus('Error: ' + payload.error, false, true);
      return;
    }
    const results = payload.results ?? [];
    this.hasResults.set(true);
    this.flatRows.set(results.map(r => ({
      blockIndex:   r.blockIndex,
      blockName:    r.blockName,
      blockText:    r.blockText,
      originalCode: r.originalCode,
      newCode:      r.newCode,
      found:        r.found,
      method:       r.method,
      ambiguous:    r.ambiguous,
      candidates:   r.candidates,
      checked:      r.found,
    })));

    const found = results.filter(r => r.found).length;
    const total = results.length;
    this._setStatus(
      `${found} of ${total} code${total === 1 ? '' : 's'} ported.`,
      found === total,
      found < total,
    );
  }

  onPort(): void {
    const codes = this.inputValue().trim();
    if (!codes)                  { this._setStatus('Enter cheat codes to port.', false, true); return; }
    const src = this.sourcePath().trim();
    if (!src)                    { this._setStatus('Select source dump.', false, true); return; }
    const tgt = this.targetPath().trim();
    if (!tgt)                    { this._setStatus('Select target dump.', false, true); return; }
    this._setStatus('', false, false);
    this.porting.set(true);
    this.flatRows.set([]);
    this.hasResults.set(false);
    this.port$.emit({ codes, sourcePath: src, targetPath: tgt });
  }

  onToggleRow(row: FlatRow, checked: boolean): void {
    this.flatRows.set(this.flatRows().map(r =>
      r.blockIndex === row.blockIndex && r.originalCode === row.originalCode
        ? { ...r, checked } : r
    ));
  }

  onToggleBlock(blockIndex: number, checked: boolean): void {
    this.flatRows.set(this.flatRows().map(r =>
      r.blockIndex === blockIndex && r.found ? { ...r, checked } : r
    ));
  }

  onToggleAll(checked: boolean): void {
    this.flatRows.set(this.flatRows().map(r => r.found ? { ...r, checked } : r));
  }

  copySelected(): void {
    const sel = this.flatRows().filter(r => r.checked && r.found && r.newCode);
    if (!sel.length) { this._setStatus('No codes selected.', false, true); return; }

    const blocks = new Map<number, FlatRow[]>();
    for (const r of sel) {
      if (!blocks.has(r.blockIndex)) { blocks.set(r.blockIndex, []); }
      blocks.get(r.blockIndex)!.push(r);
    }

    const lines: string[] = [];
    blocks.forEach(rows => {
      const first = rows[0];
      if (first.blockName) { lines.push(first.blockName); }
      if (first.blockText) { lines.push(first.blockText); }
      for (const r of rows) { lines.push(r.newCode!); }
      lines.push('');
    });

    navigator.clipboard.writeText(lines.join('\n').trimEnd())
      .then(() => this._setStatus('Copied!', true, false))
      .catch(() => this._setStatus('Copy failed.', false, true));
  }

  asTextarea(e: Event): HTMLTextAreaElement { return e.target as HTMLTextAreaElement; }
  asInput(e: Event):    HTMLInputElement    { return e.target as HTMLInputElement; }
  asCheckbox(e: Event): HTMLInputElement    { return e.target as HTMLInputElement; }

  private _setStatus(msg: string, ok: boolean, err: boolean): void {
    this.statusMessage.set(msg);
    this.statusOk.set(ok);
    this.statusError.set(err);
  }
}
