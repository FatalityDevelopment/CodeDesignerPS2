import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  ElementRef,
  signal,
  output,
  viewChild,
} from '@angular/core';

export type ScanComparison = 'eq' | 'ne' | 'lt' | 'gt' | 'le' | 'ge';
export type ScanDataType =
  | 'uint8' | 'int8'
  | 'uint16' | 'int16'
  | 'uint32' | 'int32'
  | 'uint64' | 'int64'
  | 'float32' | 'float64';

export interface ScanRequest {
  value: string;
  comparison: ScanComparison;
  dataType: ScanDataType;
  startAddress: number;
  endAddress: number;
  region: string;
  /** Addresses from the previous scan result — present only on filter scans. */
  previousAddresses?: number[];
}

export interface ScanResultEntry {
  address: string;
  value:   string;
}

export interface ScanResult {
  entries?: ScanResultEntry[];
  error?: string;
}

@Component({
  selector: 'app-scanner-overlay',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
<div id="scanner-overlay"
     [class.visible]="visible()"
     (click)="onBackdropClick($event)"
     (keydown.escape)="close()">
  <div id="scanner-overlay-box">
    <div id="scanner-overlay-header">
      <span id="scanner-overlay-title">&#128269; Memory Scanner</span>
      <button id="scanner-overlay-close" title="Close (Escape)" (click)="close()">&#xD7;</button>
    </div>
    <div id="scanner-overlay-body">
      <!-- Row 1: Value + Search + Filter Search -->
      <div class="scanner-row">
        <label class="scanner-cell-label">Value</label>
        <input id="scanner-value-input"
               class="scanner-cell-input"
               type="text"
               spellcheck="false"
               #valueInput
               [value]="valueText()"
               (input)="valueText.set(asInput($event).value)"
               (keydown.enter)="scan()" />
        <button id="scanner-search-btn"
                [disabled]="scanning()"
                (click)="scan()">Search</button>
        <button id="scanner-filter-btn"
                [disabled]="scanning() || !hasResults()"
                (click)="filterScan()">Filter Search</button>
      </div>
      <!-- Row 2: Comparison -->
      <div class="scanner-row">
        <label class="scanner-cell-label">Comparison</label>
        <select id="scanner-comparison-select"
                class="scanner-cell-select"
                [value]="comparison()"
                (change)="comparison.set(asSelect($event).value)">
          <option value="eq">Equals</option>
          <option value="ne">Not Equals</option>
          <option value="lt">Less Than</option>
          <option value="gt">Greater Than</option>
          <option value="le">Less or Equal</option>
          <option value="ge">Greater or Equal</option>
        </select>
      </div>
      <!-- Row 3: Type + Hex -->
      <div class="scanner-row">
        <label class="scanner-cell-label">Type</label>
        <select id="scanner-type-select"
                class="scanner-cell-select scanner-type-select"
                [value]="dataType()"
                (change)="dataType.set(asSelect($event).value)">
          <option value="uint8">1 Byte (8 bits)</option>
          <option value="int8">1 Byte signed (8 bits)</option>
          <option value="uint16">2 Bytes (16 bits)</option>
          <option value="int16">2 Bytes signed (16 bits)</option>
          <option value="uint32">4 Bytes (32 bits)</option>
          <option value="int32">4 Bytes signed (32 bits)</option>
          <option value="uint64">8 Bytes (64 bits)</option>
          <option value="int64">8 Bytes signed (64 bits)</option>
          <option value="float32">Float (32 bits)</option>
          <option value="float64">Double (64 bits)</option>
        </select>
        <label class="scanner-hex-label" for="scanner-hex-check">Hex</label>
        <input id="scanner-hex-check" type="checkbox"
               [checked]="hexMode()"
               (change)="hexMode.set(asCheckbox($event).checked)" />
      </div>
      <!-- Row 4: Start / End -->
      <div class="scanner-row">
        <label class="scanner-cell-label">Start</label>
        <input id="scanner-start-input"
               class="scanner-addr-input"
               type="text"
               spellcheck="false"
               [value]="startAddrText()"
               (input)="startAddrText.set(asInput($event).value)" />
        <label class="scanner-cell-label scanner-end-label">End</label>
        <input id="scanner-end-input"
               class="scanner-addr-input"
               type="text"
               spellcheck="false"
               [value]="endAddrText()"
               (input)="endAddrText.set(asInput($event).value)" />
      </div>
      <!-- Status bar -->
      <div id="scanner-status"
           [class.scanner-status-ok]="statusOk()"
           [class.scanner-status-error]="statusError()">{{ statusMessage() }}</div>
      <!-- Results list -->
      <div id="scanner-results">
        @if (results().length === 0 && hasResults()) {
          <div class="scanner-no-results">No addresses found.</div>
        }
        @for (entry of results(); track entry.address) {
          <div class="scanner-result-row" (click)="navigate$.emit(entry.address)">
            <span class="scanner-result-addr">{{ entry.address }}</span>
            <span class="scanner-result-value">{{ entry.value }}</span>
          </div>
        }
      </div>
    </div>
  </div>
</div>
  `,
})
export class ScannerOverlayComponent {

  // ── Outputs ─────────────────────────────────────────────────────────────────

  readonly close$    = output<void>();
  readonly scan$     = output<ScanRequest>();
  readonly navigate$ = output<string>();

  // ── State ───────────────────────────────────────────────────────────────────

  readonly visible       = signal(false);
  readonly valueText     = signal('');
  readonly comparison    = signal<string>('eq');
  readonly dataType      = signal<string>('uint32');
  readonly hexMode       = signal(true);
  readonly startAddrText = signal('0x00');
  readonly endAddrText   = signal('0x2000000');
  readonly statusMessage = signal('');
  readonly statusOk      = signal(false);
  readonly statusError   = signal(false);
  readonly scanning      = signal(false);
  readonly hasResults    = signal(false);
  readonly results       = signal<ScanResultEntry[]>([]);

  /** Addresses from the last scan — used for filter/refine scans. */
  private _previousAddresses: number[] = [];

  private readonly valueInputRef = viewChild<ElementRef<HTMLInputElement>>('valueInput');

  // ── Public API ──────────────────────────────────────────────────────────────

  open(): void {
    this.visible.set(true);
    requestAnimationFrame(() => this.valueInputRef()?.nativeElement.focus());
  }

  close(): void {
    if (this.visible()) {
      this.visible.set(false);
      this.close$.emit();
    }
  }

  /** Called by parent when a scanValueResult message arrives. */
  setResult(result: ScanResult): void {
    this.scanning.set(false);
    if (result.error) {
      this.statusMessage.set('Error: ' + result.error);
      this.statusOk.set(false);
      this.statusError.set(true);
      return;
    }
    const entries = result.entries ?? [];
    this.results.set(entries);
    this.hasResults.set(true);
    this._previousAddresses = entries.map(e => parseInt(e.address, 16));
    const count = entries.length;
    this.statusMessage.set(`Found ${count} address${count !== 1 ? 'es' : ''}.`);
    this.statusOk.set(true);
    this.statusError.set(false);
  }

  // ── Event Handlers ──────────────────────────────────────────────────────────

  scan(): void {
    const req = this._buildRequest();
    if (!req) { return; }
    this._startScan(req);
  }

  filterScan(): void {
    if (!this.hasResults()) { return; }
    const req = this._buildRequest(this._previousAddresses);
    if (!req) { return; }
    this._startScan(req);
  }

  onBackdropClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).id === 'scanner-overlay') {
      this.close();
    }
  }

  asInput(event: Event): HTMLInputElement { return event.target as HTMLInputElement; }
  asSelect(event: Event): HTMLSelectElement { return event.target as HTMLSelectElement; }
  asCheckbox(event: Event): HTMLInputElement { return event.target as HTMLInputElement; }

  // ── Private helpers ─────────────────────────────────────────────────────────

  private _buildRequest(previousAddresses?: number[]): ScanRequest | null {
    const rawValue = this.valueText().trim();
    if (!rawValue) {
      this.statusMessage.set('Please enter a value to scan for.');
      this.statusOk.set(false);
      this.statusError.set(true);
      return null;
    }

    const startAddr = this._parseAddress(this.startAddrText());
    const endAddr   = this._parseAddress(this.endAddrText());

    if (isNaN(startAddr) || isNaN(endAddr)) {
      this.statusMessage.set('Invalid start or end address.');
      this.statusOk.set(false);
      this.statusError.set(true);
      return null;
    }

    // If Hex mode is on and the type is integral, prepend 0x if not already present.
    let value = rawValue;
    if (this.hexMode() && !this._isFloat(this.dataType())) {
      if (!value.toLowerCase().startsWith('0x')) {
        value = '0x' + value;
      }
    }

    return {
      value,
      comparison:       this.comparison() as ScanComparison,
      dataType:         this.dataType() as ScanDataType,
      startAddress:     startAddr,
      endAddress:       endAddr,
      region:           'ee',
      previousAddresses,
    };
  }

  private _startScan(req: ScanRequest): void {
    this.scanning.set(true);
    this.statusMessage.set('Scanning…');
    this.statusOk.set(false);
    this.statusError.set(false);
    this.scan$.emit(req);
  }

  private _parseAddress(text: string): number {
    const t = text.trim();
    if (t.toLowerCase().startsWith('0x')) {
      return parseInt(t, 16);
    }
    return parseInt(t, 10);
  }

  private _isFloat(dt: string): boolean {
    return dt === 'float32' || dt === 'float64';
  }
}
