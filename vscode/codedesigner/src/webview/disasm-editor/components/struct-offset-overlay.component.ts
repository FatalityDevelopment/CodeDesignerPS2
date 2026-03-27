import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  ElementRef,
  signal,
  computed,
  output,
  viewChild,
} from '@angular/core';

export interface StructEntryData {
  name:        string;
  baseAddress: string;
  members:     { offset: number; name: string }[];
}

export interface AddStructEvent {
  name:        string;
  baseAddress: string;
}

export interface AddStructMemberEvent {
  structName:  string;
  offset:      number;
  memberName:  string;
}

@Component({
  selector: 'app-struct-offset-overlay',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
<div id="struct-overlay"
     [class.visible]="visible()"
     (click)="onBackdropClick($event)"
     (keydown.escape)="close()">
  <div id="struct-overlay-box">

    <!-- Header -->
    <div id="struct-overlay-header">
      <span id="struct-overlay-title">&#128208; Struct Offset Calculator</span>
      <button id="struct-overlay-close" title="Close (Escape)" (click)="close()">&#xD7;</button>
    </div>

    <div id="struct-overlay-body">

      <!-- Mode toggle -->
      <div class="struct-row">
        <label class="struct-cell-label">Input</label>
        <button class="struct-mode-btn"
                [class.struct-mode-active]="inputMode() === 'known'"
                (click)="inputMode.set('known')">Known Struct</button>
        <button class="struct-mode-btn"
                [class.struct-mode-active]="inputMode() === 'manual'"
                (click)="inputMode.set('manual')">Manual</button>
      </div>

      <!-- Known struct: dropdown -->
      @if (inputMode() === 'known') {
        <div class="struct-row">
          <label class="struct-cell-label">Struct</label>
          @if (structs().length > 0) {
            <select id="struct-select"
                    class="struct-cell-select"
                    [value]="selectedStructName()"
                    (change)="onStructSelect(asSelect($event).value)">
              <option value="">— select struct —</option>
              @for (s of structs(); track s.name) {
                <option [value]="s.name">{{ s.name }} ({{ s.baseAddress }})</option>
              }
            </select>
          } @else {
            <span class="struct-empty-note">No structs defined yet. Switch to Manual or add via the fields below.</span>
          }
        </div>
      }

      <!-- Manual: struct name + base address inputs -->
      @if (inputMode() === 'manual') {
        <div class="struct-row">
          <label class="struct-cell-label">Name</label>
          <input id="struct-name-input"
                 class="struct-cell-input"
                 type="text"
                 spellcheck="false"
                 placeholder="e.g. PlayerData"
                 [value]="manualName()"
                 (input)="manualName.set(asInput($event).value)" />
        </div>
        <div class="struct-row">
          <label class="struct-cell-label">Base Addr</label>
          <input id="struct-base-input"
                 class="struct-addr-input"
                 type="text"
                 spellcheck="false"
                 placeholder="0x80200000"
                 [value]="manualBaseText()"
                 (input)="manualBaseText.set(asInput($event).value)" />
          <button class="struct-action-btn"
                  title="Save struct definition to server"
                  [disabled]="!canSaveStruct()"
                  (click)="saveStruct()">Save Struct</button>
        </div>
      }

      <!-- Target address -->
      <div class="struct-row">
        <label class="struct-cell-label">Address</label>
        <input id="struct-addr-input"
               class="struct-addr-input"
               type="text"
               spellcheck="false"
               placeholder="0x00000000"
               #addrInput
               [value]="targetAddrText()"
               (input)="targetAddrText.set(asInput($event).value)"
               (keydown.enter)="calculate()" />
      </div>

      <!-- Calculate button + result -->
      <div class="struct-row">
        <label class="struct-cell-label"></label>
        <button id="struct-calc-btn"
                class="struct-action-btn"
                [disabled]="!canCalculate()"
                (click)="calculate()">Calculate</button>
        @if (offsetResult() !== null) {
          <span id="struct-offset-result"
                [class.struct-result-ok]="!offsetError()"
                [class.struct-result-error]="offsetError()">
            {{ offsetDisplay() }}
          </span>
        }
      </div>

      <!-- Status bar -->
      @if (statusMessage()) {
        <div id="struct-status"
             [class.struct-status-ok]="statusOk()"
             [class.struct-status-error]="statusError()">{{ statusMessage() }}</div>
      }

      <!-- Add-to-struct section (shown only when a result is available and a struct is chosen) -->
      @if (offsetResult() !== null && !offsetError() && activeStructName()) {
        <div class="struct-divider"></div>
        <div class="struct-row">
          <label class="struct-cell-label">Member</label>
          <input id="struct-member-input"
                 class="struct-cell-input"
                 type="text"
                 spellcheck="false"
                 placeholder="member name"
                 [value]="memberName()"
                 (input)="memberName.set(asInput($event).value)"
                 (keydown.enter)="addToStruct()" />
          <button class="struct-action-btn"
                  [disabled]="!memberName().trim()"
                  (click)="addToStruct()">Add to Struct</button>
        </div>
      }

      <!-- Members list for the active struct -->
      @if (activeStruct()?.members?.length) {
        <div class="struct-divider"></div>
        <div id="struct-members-header">Members of {{ activeStructName() }}</div>
        <div id="struct-members-list">
          @for (m of activeStruct()!.members; track m.offset) {
            <div class="struct-member-row">
              <span class="struct-member-offset">{{ formatOffset(m.offset) }}</span>
              <span class="struct-member-name">{{ m.name }}</span>
            </div>
          }
        </div>
      }

    </div>
  </div>
</div>
  `,
})
export class StructOffsetOverlayComponent {

  // ── Outputs ─────────────────────────────────────────────────────────────────

  readonly close$        = output<void>();
  readonly saveStruct$   = output<AddStructEvent>();
  readonly addMember$    = output<AddStructMemberEvent>();
  readonly loadStructs$  = output<void>();

  // ── State ───────────────────────────────────────────────────────────────────

  readonly visible            = signal(false);
  readonly inputMode          = signal<'known' | 'manual'>('known');
  readonly structs            = signal<StructEntryData[]>([]);
  readonly selectedStructName = signal('');
  readonly manualName         = signal('');
  readonly manualBaseText     = signal('');
  readonly targetAddrText     = signal('');
  readonly offsetResult       = signal<number | null>(null);
  readonly offsetError        = signal(false);
  readonly memberName         = signal('');
  readonly statusMessage      = signal('');
  readonly statusOk           = signal(false);
  readonly statusError        = signal(false);

  // ── Computed ────────────────────────────────────────────────────────────────

  readonly activeStructName = computed(() =>
    this.inputMode() === 'known' ? this.selectedStructName() : this.manualName().trim()
  );

  readonly activeStruct = computed(() =>
    this.structs().find(s => s.name === this.activeStructName()) ?? null
  );

  readonly activeBaseAddress = computed((): number => {
    if (this.inputMode() === 'known') {
      const s = this.activeStruct();
      return s ? this._parseAddress(s.baseAddress) : NaN;
    }
    return this._parseAddress(this.manualBaseText());
  });

  readonly canCalculate = computed(() => {
    const base = this.activeBaseAddress();
    const target = this._parseAddress(this.targetAddrText());
    return !isNaN(base) && !isNaN(target) &&
           (this.inputMode() === 'known' ? !!this.selectedStructName() : !!this.manualBaseText().trim());
  });

  readonly canSaveStruct = computed(() =>
    !!this.manualName().trim() && !isNaN(this._parseAddress(this.manualBaseText()))
  );

  readonly offsetDisplay = computed(() => {
    const v = this.offsetResult();
    if (v === null) return '';
    if (this.offsetError()) return 'Error: negative offset';
    return this._toHexOffset(v);
  });

  private readonly addrInputRef = viewChild<ElementRef<HTMLInputElement>>('addrInput');

  // ── Public API ──────────────────────────────────────────────────────────────

  /** Open the overlay, pre-seeding the target address with the cursor address. */
  open(cursorAddress: number): void {
    this.targetAddrText.set('0x' + cursorAddress.toString(16).toUpperCase().padStart(8, '0'));
    this.offsetResult.set(null);
    this.offsetError.set(false);
    this.memberName.set('');
    this._clearStatus();
    this.visible.set(true);
    this.loadStructs$.emit();
    requestAnimationFrame(() => this.addrInputRef()?.nativeElement.focus());
  }

  close(): void {
    if (this.visible()) {
      this.visible.set(false);
      this.close$.emit();
    }
  }

  /** Called by parent after a GETSTRUCTS response arrives. */
  setStructs(list: StructEntryData[]): void {
    this.structs.set(list);
    // Keep selected struct valid
    if (this.selectedStructName() && !list.find(s => s.name === this.selectedStructName())) {
      this.selectedStructName.set('');
    }
  }

  /** Called by parent after a struct is saved to refresh the list. */
  refreshStructs(): void {
    this.loadStructs$.emit();
  }

  // ── Event Handlers ──────────────────────────────────────────────────────────

  calculate(): void {
    const base   = this.activeBaseAddress();
    const target = this._parseAddress(this.targetAddrText());

    if (isNaN(base) || isNaN(target)) {
      this._setStatus('Invalid address.', false);
      return;
    }

    const offset = target - base;
    if (offset < 0) {
      this.offsetResult.set(offset);
      this.offsetError.set(true);
      this._setStatus('Target address is below struct base address.', false);
      return;
    }

    this.offsetResult.set(offset);
    this.offsetError.set(false);
    this._clearStatus();
  }

  saveStruct(): void {
    const name = this.manualName().trim();
    const base = this.manualBaseText().trim();
    if (!name || isNaN(this._parseAddress(base))) {
      this._setStatus('Please enter a valid name and base address.', false);
      return;
    }
    this.saveStruct$.emit({ name, baseAddress: base });
    this._setStatus(`Struct '${name}' saved.`, true);
  }

  addToStruct(): void {
    const mName  = this.memberName().trim();
    const offset = this.offsetResult();
    const sName  = this.activeStructName();
    if (!mName || offset === null || this.offsetError() || !sName) { return; }
    this.addMember$.emit({ structName: sName, offset, memberName: mName });
    this.memberName.set('');
    this._setStatus(`Member '${mName}' added at ${this.offsetDisplay()}.`, true);
  }

  onStructSelect(value: string): void {
    this.selectedStructName.set(value);
    this.offsetResult.set(null);
    this.offsetError.set(false);
    this._clearStatus();
  }

  onBackdropClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).id === 'struct-overlay') {
      this.close();
    }
  }

  formatOffset(offset: number): string {
    return this._toHexOffset(offset);
  }

  asInput(event: Event): HTMLInputElement { return event.target as HTMLInputElement; }
  asSelect(event: Event): HTMLSelectElement { return event.target as HTMLSelectElement; }

  // ── Private Helpers ─────────────────────────────────────────────────────────

  /** Format a non-negative byte offset as a zero-padded hex string (e.g. 0x002C). */
  private _toHexOffset(offset: number): string {
    return '0x' + offset.toString(16).toUpperCase().padStart(4, '0');
  }

  private _parseAddress(text: string): number {
    const t = (text || '').trim();
    if (t.toLowerCase().startsWith('0x')) {
      return parseInt(t, 16);
    }
    return parseInt(t, 10);
  }

  private _setStatus(msg: string, ok: boolean): void {
    this.statusMessage.set(msg);
    this.statusOk.set(ok);
    this.statusError.set(!ok);
  }

  private _clearStatus(): void {
    this.statusMessage.set('');
    this.statusOk.set(false);
    this.statusError.set(false);
  }
}
