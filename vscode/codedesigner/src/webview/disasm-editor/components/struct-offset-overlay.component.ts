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

      <!-- Pointer selector: structs from the list + Manual option -->
      <div class="struct-row">
        <label class="struct-cell-label">Pointer</label>
        @if (structsLoading()) {
          <span class="struct-empty-note">Loading&hellip;</span>
        } @else {
          <select id="struct-select"
                  class="struct-cell-select"
                  [value]="selectedValue()"
                  (change)="onDropdownChange(asSelect($event).value)">
            <option value="">— select pointer —</option>
            @for (s of structs(); track s.name) {
              <option [value]="s.name">{{ s.name }}</option>
            }
            <option value="manual">&#x270F; Manual</option>
          </select>
        }
      </div>

      <!-- Manual: direct base address input -->
      @if (isManual()) {
        <div class="struct-row">
          <label class="struct-cell-label">Base Addr</label>
          <input id="struct-base-input"
                 class="struct-addr-input"
                 type="text"
                 spellcheck="false"
                 placeholder="0x00000000"
                 [value]="baseAddrText()"
                 (input)="baseAddrText.set(asInput($event).value)"
                 (keydown.enter)="calculate()" />
        </div>
      }

      <!-- Target address -->
      <div class="struct-row">
        <label class="struct-cell-label">Target Addr</label>
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

      <!-- Post-result actions (only shown after a valid calculation) -->
      @if (offsetResult() !== null && !offsetError()) {
        @if (isManual()) {
          <!-- Manual: create a new pointer with a label -->
          <div class="struct-row">
            <label class="struct-cell-label">Label</label>
            <input id="struct-label-input"
                   class="struct-cell-input"
                   type="text"
                   spellcheck="false"
                   placeholder="pointer label"
                   [value]="labelText()"
                   (input)="labelText.set(asInput($event).value)"
                   (keydown.enter)="createPointer()" />
            <button class="struct-action-btn"
                    [disabled]="!labelText().trim()"
                    (click)="createPointer()">Create Pointer</button>
          </div>
        } @else if (activeStructName()) {
          <!-- Known struct: add calculated offset as a named member -->
          <div class="struct-row">
            <label class="struct-cell-label">Member</label>
            <input id="struct-member-input"
                   class="struct-cell-input"
                   type="text"
                   spellcheck="false"
                   placeholder="member name"
                   [value]="labelText()"
                   (input)="labelText.set(asInput($event).value)"
                   (keydown.enter)="addToStruct()" />
            <button class="struct-action-btn"
                    [disabled]="!labelText().trim()"
                    (click)="addToStruct()">Add to Struct</button>
          </div>
        }
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

  readonly visible        = signal(false);
  /** Selected value: struct name, 'manual', or '' (none). */
  readonly selectedValue  = signal('');
  readonly structs        = signal<StructEntryData[]>([]);
  readonly structsLoading = signal(false);
  /** Base address typed in manual mode. */
  readonly baseAddrText   = signal('');
  /** Resolved base read from the selected struct's pointer (known mode). */
  readonly resolvedBaseText = signal('');
  readonly pointerReading = signal(false);
  readonly targetAddrText = signal('');
  readonly offsetResult   = signal<number | null>(null);
  readonly offsetError    = signal(false);
  /** Holds the member name (known mode) or pointer label (manual mode) after a result. */
  readonly labelText      = signal('');

  // ── Computed ────────────────────────────────────────────────────────────────

  readonly isManual = computed(() => this.selectedValue() === 'manual');

  readonly activeStructName = computed(() =>
    this.isManual() ? '' : this.selectedValue()
  );

  readonly activeStruct = computed(() =>
    this.structs().find(s => s.name === this.activeStructName()) ?? null
  );

  /** Numeric base address used for calculation. */
  readonly activeBaseAddress = computed((): number => {
    if (this.isManual()) {
      return this._parseHex(this.baseAddrText());
    }
    // Known struct: use resolved (dereferenced) base when available,
    // else fall back to the raw struct baseAddress value.
    const resolved = this._parseHex(this.resolvedBaseText());
    if (!isNaN(resolved)) { return resolved; }
    const s = this.activeStruct();
    return s ? this._parseHex(s.baseAddress) : NaN;
  });

  readonly canCalculate = computed(() => {
    const base   = this.activeBaseAddress();
    const target = this._parseHex(this.targetAddrText());
    return !isNaN(base) && !isNaN(target);
  });

  readonly offsetDisplay = computed(() => {
    const v = this.offsetResult();
    if (v === null) { return ''; }
    if (this.offsetError()) { return 'Error: negative offset'; }
    return this._toHexOffset(v);
  });

  private readonly addrInputRef = viewChild<ElementRef<HTMLInputElement>>('addrInput');

  // ── Public API ──────────────────────────────────────────────────────────────

  /** Open the overlay, pre-seeding the target address with the cursor address. */
  open(cursorAddress: number): void {
    this.targetAddrText.set('0x' + cursorAddress.toString(16).toUpperCase().padStart(8, '0'));
    this.offsetResult.set(null);
    this.offsetError.set(false);
    this.labelText.set('');
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
    // If the previously selected struct was removed, clear selection.
    if (!this.isManual() && this.selectedValue() && !list.find(s => s.name === this.selectedValue())) {
      this.selectedValue.set('');
      this.resolvedBaseText.set('');
    }
    // Re-read the pointer for the currently selected struct.
    if (!this.isManual() && this.selectedValue()) {
      const s = list.find(ss => ss.name === this.selectedValue());
      if (s?.baseAddress) {
        this._emitReadPointer(s.baseAddress);
      }
    }
  }

  /** Called by parent with the 4-byte LE value read from the struct's pointer address. */
  setResolvedBase(hexValue: string): void {
    this.resolvedBaseText.set(hexValue);
    this.pointerReading.set(false);
    this.offsetResult.set(null);
    this.offsetError.set(false);
  }

  // ── Event Handlers ──────────────────────────────────────────────────────────

  onDropdownChange(value: string): void {
    this.selectedValue.set(value);
    this.resolvedBaseText.set('');
    this.baseAddrText.set('');
    this.offsetResult.set(null);
    this.offsetError.set(false);
    this.labelText.set('');
    if (value && value !== 'manual') {
      const s = this.structs().find(ss => ss.name === value);
      if (s?.baseAddress) {
        this._emitReadPointer(s.baseAddress);
      }
    }
  }

  calculate(): void {
    const base   = this.activeBaseAddress();
    const target = this._parseHex(this.targetAddrText());

    if (isNaN(base) || isNaN(target)) { return; }

    const offset = target - base;
    if (offset < 0) {
      this.offsetResult.set(offset);
      this.offsetError.set(true);
      return;
    }

    this.offsetResult.set(offset);
    this.offsetError.set(false);
  }

  /** Create a new pointer (struct entry) from the manual base address and label. */
  createPointer(): void {
    const label = this.labelText().trim();
    const base  = this.baseAddrText().trim();
    if (!label || !base) { return; }
    this.saveStruct$.emit({ name: label, baseAddress: base });
    this.labelText.set('');
  }

  /** Add the calculated offset as a named member to the selected struct. */
  addToStruct(): void {
    const mName  = this.labelText().trim();
    const offset = this.offsetResult();
    const sName  = this.activeStructName();
    if (!mName || offset === null || this.offsetError() || !sName) { return; }
    this.addMember$.emit({ structName: sName, offset, memberName: mName });
    this.labelText.set('');
  }

  onBackdropClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).id === 'struct-overlay') {
      this.close();
    }
  }

  asInput(event: Event): HTMLInputElement { return event.target as HTMLInputElement; }
  asSelect(event: Event): HTMLSelectElement { return event.target as HTMLSelectElement; }

  // ── Private Helpers ─────────────────────────────────────────────────────────

  private _emitReadPointer(pointerAddress: string): void {
    this.resolvedBaseText.set('');
    this.pointerReading.set(true);
    this.readPointer$.emit(pointerAddress);
  }

  /** Format a non-negative byte offset as a 2-byte zero-padded hex string (e.g. 0x002C). */
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
}
