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
                (click)="switchMode('known')">Known Struct</button>
        <button class="struct-mode-btn"
                [class.struct-mode-active]="inputMode() === 'manual'"
                (click)="switchMode('manual')">Manual</button>
      </div>

      <!-- Known struct: dropdown -->
      @if (inputMode() === 'known') {
        <div class="struct-row">
          <label class="struct-cell-label">Struct</label>
          @if (structsLoading()) {
            <span class="struct-empty-note">Loading&hellip;</span>
          } @else if (structs().length > 0) {
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
            <span class="struct-empty-note">No structs defined yet. Switch to Manual to enter a pointer manually.</span>
          }
        </div>
      }

      <!-- Manual: struct name + pointer address inputs -->
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
          <label class="struct-cell-label">Pointer</label>
          <input id="struct-pointer-input"
                 class="struct-addr-input"
                 type="text"
                 spellcheck="false"
                 placeholder="0x00401244"
                 [value]="manualPointerText()"
                 (input)="manualPointerText.set(asInput($event).value)"
                 (blur)="onPointerBlur()"
                 (keydown.enter)="onPointerBlur()" />
          <button class="struct-action-btn"
                  title="Save struct definition to server"
                  [disabled]="!canSaveStruct()"
                  (click)="saveStruct()">Save Struct</button>
        </div>
      }

      <!-- Resolved base address (read-only, shown for both modes when pointer selected/entered) -->
      @if (activePointerAddress()) {
        <div class="struct-row">
          <label class="struct-cell-label">Base Addr</label>
          @if (pointerReading()) {
            <span class="struct-empty-note">Reading&hellip;</span>
          } @else {
            <input id="struct-base-resolved"
                   class="struct-addr-input"
                   type="text"
                   readonly
                   spellcheck="false"
                   placeholder="connect to emulator to read"
                   [value]="resolvedBaseText()" />
          }
        </div>
      }

      <!-- Target address -->
      <div class="struct-row">
        <label class="struct-cell-label">Target Address</label>
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
  /** Emitted when a pointer address should be dereferenced (read 4 bytes LE). */
  readonly readPointer$  = output<string>();

  // ── State ───────────────────────────────────────────────────────────────────

  readonly visible            = signal(false);
  readonly inputMode          = signal<'known' | 'manual'>('known');
  readonly structs            = signal<StructEntryData[]>([]);
  readonly structsLoading     = signal(false);
  readonly selectedStructName = signal('');
  readonly manualName         = signal('');
  /** Pointer address entered in manual mode (to be dereferenced). */
  readonly manualPointerText  = signal('');
  /** Resolved base address: the 4-byte LE value read from the pointer. */
  readonly resolvedBaseText   = signal('');
  readonly pointerReading     = signal(false);
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

  /** The pointer address currently in use (struct's baseAddress or manual input). */
  readonly activePointerAddress = computed((): string => {
    if (this.inputMode() === 'known') {
      const s = this.activeStruct();
      return s ? s.baseAddress : '';
    }
    return this.manualPointerText().trim();
  });

  /** Parsed numeric value of the resolved (dereferenced) base address. */
  readonly activeBaseAddress = computed((): number =>
    this._parseHex(this.resolvedBaseText())
  );

  readonly canCalculate = computed(() => {
    const base   = this.activeBaseAddress();
    const target = this._parseHex(this.targetAddrText());
    return !isNaN(base) && !isNaN(target) && !!this.resolvedBaseText();
  });

  readonly canSaveStruct = computed(() =>
    !!this.manualName().trim() && !!this.manualPointerText().trim()
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
    this.structsLoading.set(true);
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
    this.structsLoading.set(false);
    if (this.selectedStructName() && !list.find(s => s.name === this.selectedStructName())) {
      this.selectedStructName.set('');
      this.resolvedBaseText.set('');
    }
    // If a struct is already selected, re-read its pointer to refresh the resolved base.
    if (this.inputMode() === 'known' && this.selectedStructName()) {
      const s = list.find(ss => ss.name === this.selectedStructName());
      if (s?.baseAddress) {
        this._emitReadPointer(s.baseAddress);
      }
    }
  }

  /** Called by parent with the 4-byte LE value read from the pointer address. */
  setResolvedBase(hexValue: string): void {
    this.resolvedBaseText.set(hexValue);
    this.pointerReading.set(false);
    this.offsetResult.set(null);
    this.offsetError.set(false);
    this._clearStatus();
  }

  /** Called by parent after a struct is saved to refresh the list. */
  refreshStructs(): void {
    this.loadStructs$.emit();
  }

  // ── Event Handlers ──────────────────────────────────────────────────────────

  switchMode(mode: 'known' | 'manual'): void {
    this.inputMode.set(mode);
    this.resolvedBaseText.set('');
    this.offsetResult.set(null);
    this.offsetError.set(false);
    this._clearStatus();
  }

  calculate(): void {
    const base   = this.activeBaseAddress();
    const target = this._parseHex(this.targetAddrText());

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
    const ptr  = this.manualPointerText().trim();
    if (!name || !ptr) {
      this._setStatus('Please enter a valid name and pointer address.', false);
      return;
    }
    this.saveStruct$.emit({ name, baseAddress: ptr });
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
    this.resolvedBaseText.set('');
    this.offsetResult.set(null);
    this.offsetError.set(false);
    this._clearStatus();
    if (value) {
      const s = this.structs().find(ss => ss.name === value);
      if (s?.baseAddress) {
        this._emitReadPointer(s.baseAddress);
      }
    }
  }

  /** Triggered on blur or Enter for the manual pointer input. */
  onPointerBlur(): void {
    const ptr = this.manualPointerText().trim();
    if (ptr) {
      this._emitReadPointer(ptr);
    }
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

  private _emitReadPointer(pointerAddress: string): void {
    this.resolvedBaseText.set('');
    this.pointerReading.set(true);
    this.readPointer$.emit(pointerAddress);
  }

  /** Format a non-negative byte offset as a zero-padded hex string (e.g. 0x002C). */
  private _toHexOffset(offset: number): string {
    return '0x' + offset.toString(16).toUpperCase().padStart(4, '0');
  }

  private _parseHex(text: string): number {
    const t = (text || '').trim();
    if (!t) { return NaN; }
    const hex = t.toLowerCase().startsWith('0x') ? t.slice(2) : t;
    if (!hex) { return NaN; }
    return parseInt(hex, 16);
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
