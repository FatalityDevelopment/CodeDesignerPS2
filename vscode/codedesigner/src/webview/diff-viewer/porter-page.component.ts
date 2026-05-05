import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  signal,
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

interface PorterGroup {
  blockIndex: number;
  blockName:  string;
  blockText:  string | null;
  entries:    PorterResult[];
  allFound:   boolean;
  anyFound:   boolean;
  checked:    boolean;
}

/**
 * Full-page, non-modal cheat code porter for the diff viewer's Porter tab.
 * Styled to match the Label Manager UI (VS Code dark theme, toolbar layout).
 */
@Component({
  selector: 'app-porter-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  styles: [`
app-porter-page { display: flex; flex-direction: column; height: 100%; overflow: hidden; }

.pp-toolbar {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
  background: #252526;
  border-bottom: 1px solid #3e3e42;
  flex-shrink: 0;
  flex-wrap: wrap;
}
.pp-toolbar-title {
  font-size: 13px;
  font-weight: 600;
  color: #d4d4d4;
  margin-right: 8px;
}
.pp-file-group {
  display: flex;
  align-items: center;
  gap: 4px;
  flex: 1;
  min-width: 160px;
}
.pp-file-label {
  font-size: 11px;
  color: #9cdcfe;
  white-space: nowrap;
  font-family: monospace;
}
.pp-file-input {
  flex: 1;
  background: #1e1e1e;
  border: 1px solid #3e3e42;
  color: #b5b5b5;
  font-family: monospace;
  font-size: 11px;
  padding: 3px 6px;
  border-radius: 2px;
  min-width: 0;
  cursor: default;
  outline: none;
}
.pp-browse-btn {
  background: #3c3c3c;
  border: 1px solid #555;
  color: #d4d4d4;
  cursor: pointer;
  font-size: 12px;
  padding: 2px 7px;
  border-radius: 2px;
  white-space: nowrap;
  flex-shrink: 0;
}
.pp-browse-btn:hover { background: #505050; }

.pp-port-btn {
  background: #007acc;
  border: 1px solid #007acc;
  color: #fff;
  padding: 3px 12px;
  cursor: pointer;
  font-size: 12px;
  border-radius: 2px;
  white-space: nowrap;
  flex-shrink: 0;
}
.pp-port-btn:disabled { opacity: .5; cursor: default; }
.pp-port-btn:not(:disabled):hover { background: #1a8ad4; }

.pp-status { font-size: 11px; white-space: nowrap; }
.pp-status-ok  { color: #73c991; }
.pp-status-err { color: #f48771; }

.pp-body {
  display: flex;
  flex-direction: column;
  flex: 1;
  overflow: hidden;
  padding: 8px;
  gap: 8px;
}
.pp-codes-label { font-size: 11px; color: #9cdcfe; font-family: monospace; }
.pp-codes-textarea {
  flex: 0 0 130px;
  resize: vertical;
  background: #1e1e1e;
  border: 1px solid #3e3e42;
  color: #d4d4d4;
  font-family: monospace;
  font-size: 12px;
  padding: 6px 8px;
  border-radius: 2px;
  outline: none;
  min-height: 80px;
}
.pp-codes-textarea:focus { border-color: #007acc; }

.pp-results {
  flex: 1;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.pp-no-results { color: #666; font-size: 12px; text-align: center; padding: 20px; }

.pp-card {
  background: #252526;
  border: 1px solid #3e3e42;
  border-radius: 3px;
  overflow: hidden;
}
.pp-card-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 5px 8px;
  background: #2d2d30;
  border-bottom: 1px solid #3e3e42;
  font-size: 12px;
}
.pp-card-name { flex: 1; font-weight: 500; }
.pp-badge {
  font-size: 10px;
  padding: 1px 5px;
  border-radius: 2px;
  font-weight: 600;
}
.pp-badge-ok   { background: #0a2a10; color: #73c991; border: 1px solid #0a4020; }
.pp-badge-warn { background: #2a2200; color: #dcdcaa; border: 1px solid #4a3c00; }
.pp-badge-err  { background: #2a0a0a; color: #f48771; border: 1px solid #5a1d1d; }

.pp-block-text { padding: 4px 8px; font-size: 11px; color: #aaa; border-bottom: 1px solid #2a2a2a; }

.pp-result-line {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 3px 8px;
  font-family: monospace;
  font-size: 11px;
  border-bottom: 1px solid #1a1a1a;
}
.pp-result-line:last-child { border-bottom: none; }
.pp-addr-old  { color: #808080; }
.pp-arrow     { color: #555; }
.pp-addr-new  { color: #9cdcfe; }
.pp-not-found { color: #f48771; }
.pp-method    { color: #888; font-size: 10px; }
.pp-candidates { padding: 2px 8px 4px 28px; font-size: 10px; color: #dcdcaa; font-family: monospace; }

.pp-copy-row {
  padding: 6px 8px;
  border-top: 1px solid #3e3e42;
  flex-shrink: 0;
}
.pp-copy-btn {
  background: #3c3c3c;
  border: 1px solid #555;
  color: #d4d4d4;
  padding: 3px 10px;
  cursor: pointer;
  font-size: 11px;
  border-radius: 2px;
}
.pp-copy-btn:hover { background: #4c4c4c; }
`],
  template: `
<div class="pp-toolbar">
  <span class="pp-toolbar-title">&#8651; Cheat Code Porter</span>

  <div class="pp-file-group">
    <span class="pp-file-label">Source:</span>
    <input class="pp-file-input" type="text" readonly
           [value]="sourcePath()"
           placeholder="Select source EE RAM dump…" />
    <button class="pp-browse-btn" title="Browse for source EE RAM dump"
            (click)="browseSource.emit()">&#8230;</button>
  </div>

  <div class="pp-file-group">
    <span class="pp-file-label">Target:</span>
    <input class="pp-file-input" type="text" readonly
           [value]="targetPath()"
           placeholder="Select target EE RAM dump…" />
    <button class="pp-browse-btn" title="Browse for target EE RAM dump"
            (click)="browseTarget.emit()">&#8230;</button>
  </div>

  <button class="pp-port-btn" [disabled]="porting()" (click)="onPort()">
    @if (porting()) { &#9203; Porting… } @else { &#8651; Port Cheats }
  </button>

  @if (statusMessage()) {
    <span class="pp-status"
          [class.pp-status-ok]="statusOk()"
          [class.pp-status-err]="statusError()">
      {{ statusMessage() }}
    </span>
  }
</div>

<div class="pp-body">
  <label class="pp-codes-label">Cheat Codes (raw GameShark format)</label>
  <textarea class="pp-codes-textarea"
            spellcheck="false"
            placeholder="#Code Name&#10;20XXXXXX YYYYYYYY"
            [value]="inputValue()"
            (input)="inputValue.set(asTextarea($event).value)"></textarea>

  <div class="pp-results">
    @if (groups().length === 0 && hasResults()) {
      <div class="pp-no-results">No cheat codes found in input.</div>
    }
    @for (group of groups(); track group.blockIndex) {
      <div class="pp-card">
        <div class="pp-card-header">
          <input type="checkbox"
                 [checked]="group.checked"
                 [disabled]="!group.anyFound"
                 (change)="toggleGroup(group, asCheckbox($event).checked)" />
          <span class="pp-card-name">{{ group.blockName || '(unnamed)' }}</span>
          <span class="pp-badge"
                [class.pp-badge-ok]="group.allFound"
                [class.pp-badge-warn]="!group.allFound && group.anyFound"
                [class.pp-badge-err]="!group.anyFound">
            {{ group.allFound ? '✓ found' : group.anyFound ? '⚠ partial' : '✗ not found' }}
          </span>
        </div>
        @if (group.blockText) {
          <div class="pp-block-text">{{ group.blockText }}</div>
        }
        @for (entry of group.entries; track entry.originalCode) {
          <div class="pp-result-line">
            <span class="pp-addr-old">{{ entry.originalCode }}</span>
            <span class="pp-arrow"> → </span>
            @if (entry.found && entry.newCode) {
              <span class="pp-addr-new">{{ entry.newCode }}</span>
              <span class="pp-method">&nbsp;{{ entry.method }}{{ entry.ambiguous ? ' ⚠' : '' }}</span>
            } @else {
              <span class="pp-not-found">not found</span>
              <span class="pp-method">&nbsp;{{ entry.method }}</span>
            }
          </div>
          @if (entry.ambiguous && entry.candidates) {
            <div class="pp-candidates">Candidates: {{ entry.candidates.join('  ') }}</div>
          }
        }
      </div>
    }
  </div>

  @if (showCopyRow()) {
    <div class="pp-copy-row">
      <button class="pp-copy-btn" (click)="copySelected()">Copy Selected Codes</button>
    </div>
  }
</div>
`,
})
export class PorterPageComponent {
  readonly browseSource = output<void>();
  readonly browseTarget = output<void>();
  readonly port$ = output<PortRequest>();

  readonly inputValue    = signal('');
  readonly sourcePath    = signal('');
  readonly targetPath    = signal('');
  readonly statusMessage = signal('');
  readonly statusOk      = signal(false);
  readonly statusError   = signal(false);
  readonly porting       = signal(false);
  readonly groups        = signal<PorterGroup[]>([]);
  readonly hasResults    = signal(false);
  readonly showCopyRow   = signal(false);

  private _resultData: PorterResult[] = [];

  setSourcePath(path: string): void { this.sourcePath.set(path); }
  setTargetPath(path: string): void { this.targetPath.set(path); }

  setResult(payload: PortResultPayload): void {
    this.porting.set(false);
    if (payload.error) {
      this._setStatus('Error: ' + payload.error, false, true);
      return;
    }
    const results = payload.results ?? [];
    this._resultData = results;
    this.hasResults.set(true);
    if (results.length === 0) {
      this.groups.set([]);
      this.showCopyRow.set(false);
    } else {
      this.groups.set(this._buildGroups(results));
      this.showCopyRow.set(true);
    }
    const found = results.filter(r => r.found).length;
    const total = results.length;
    this._setStatus(
      `Done — ${found} of ${total} code${total === 1 ? '' : 's'} ported.`,
      found === total, found !== total
    );
  }

  onPort(): void {
    const codes = this.inputValue().trim();
    if (!codes)                    { this._setStatus('Please enter cheat codes.', false, true); return; }
    const src = this.sourcePath().trim();
    if (!src)                      { this._setStatus('Please select a source dump.', false, true); return; }
    const tgt = this.targetPath().trim();
    if (!tgt)                      { this._setStatus('Please select a target dump.', false, true); return; }
    this._setStatus('Porting…', false, false);
    this.porting.set(true);
    this.groups.set([]);
    this.hasResults.set(false);
    this.showCopyRow.set(false);
    this.port$.emit({ codes, sourcePath: src, targetPath: tgt });
  }

  toggleGroup(group: PorterGroup, checked: boolean): void {
    this.groups.set(this.groups().map(g => g.blockIndex === group.blockIndex ? { ...g, checked } : g));
  }

  copySelected(): void {
    const selected = this.groups().filter(g => g.checked);
    if (!selected.length) { this._setStatus('No codes selected.', false, true); return; }
    const lines: string[] = [];
    for (const group of selected) {
      const entries = this._resultData.filter(e => e.blockIndex === group.blockIndex && e.found && e.newCode);
      if (!entries.length) { continue; }
      if (group.blockName) { lines.push(group.blockName); }
      if (group.blockText) { lines.push(group.blockText); }
      for (const e of entries) { lines.push(e.newCode!); }
      lines.push('');
    }
    navigator.clipboard.writeText(lines.join('\n').trimEnd())
      .then(() => this._setStatus('Copied!', true, false))
      .catch(() => this._setStatus('Copy failed.', false, true));
  }

  asTextarea(e: Event): HTMLTextAreaElement { return e.target as HTMLTextAreaElement; }
  asCheckbox(e: Event):  HTMLInputElement   { return e.target as HTMLInputElement; }

  private _setStatus(msg: string, ok: boolean, err: boolean): void {
    this.statusMessage.set(msg);
    this.statusOk.set(ok);
    this.statusError.set(err);
  }

  private _buildGroups(results: PorterResult[]): PorterGroup[] {
    const map = new Map<number, PorterGroup>();
    for (const r of results) {
      if (!map.has(r.blockIndex)) {
        map.set(r.blockIndex, {
          blockIndex: r.blockIndex, blockName: r.blockName, blockText: r.blockText,
          entries: [], allFound: true, anyFound: false, checked: r.found,
        });
      }
      const g = map.get(r.blockIndex)!;
      g.entries.push(r);
      if (!r.found) { g.allFound = false; }
      if (r.found)  { g.anyFound = true;  }
    }
    map.forEach(g => { if (!g.anyFound) { g.checked = false; } });
    return Array.from(map.values());
  }
}
