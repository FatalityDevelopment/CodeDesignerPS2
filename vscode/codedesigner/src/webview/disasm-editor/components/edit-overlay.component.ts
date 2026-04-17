import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  ElementRef,
  signal,
  computed,
  input,
  output,
  viewChild,
  effect,
} from '@angular/core';
import { HistoryEntry, RefEntry } from '../state/types';

/**
 * Data needed to open the edit overlay for a specific instruction.
 */
export interface EditOverlayOpenData {
  address: number;
  addressHex: string;
  op: string;
  args: string;
  bytes: string;
  currentLabel: string;
  currentComment: string;
}

/**
 * Emitted when the user saves edits.
 */
export interface EditOverlaySaveEvent {
  address: number;
  addressHex: string;
  newLabel: string;
  newComment: string;
  newBytes: string;
  originalBytes: string;
}

/**
 * Modal overlay for editing an instruction's bytes, label, and comment.
 * Has two tabs: Edit and History.
 *
 * The parent calls `open(data)` to display the overlay and `close()` to hide it.
 * Assembly preview and disassembly are handled via input signals driven by
 * the parent's message handlers.
 */
@Component({
  selector: 'app-edit-overlay',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
<div id="edit-overlay"
     [class.visible]="visible()"
     (click)="onBackdropClick($event)"
     (keydown.escape)="close()">
  <div id="edit-overlay-box">
    <div id="edit-overlay-header">
      <span id="edit-overlay-title">Edit Address</span>
      <button id="edit-overlay-close" title="Close (Escape)" (click)="close()">&#xD7;</button>
    </div>
    <div id="edit-overlay-tabs">
      <button class="edit-overlay-tab"
              [class.active]="activeTab() === 'edit'"
              (click)="activeTab.set('edit')">Edit</button>
      <button class="edit-overlay-tab"
              [class.active]="activeTab() === 'history'"
              (click)="activeTab.set('history')">History</button>
      <button class="edit-overlay-tab"
              [class.active]="activeTab() === 'refs'"
              (click)="onRefsTabClick()">References</button>
    </div>
    <div id="edit-overlay-tab-edit"
         class="edit-overlay-tab-content"
         [class.active]="activeTab() === 'edit'">
      <div class="edit-overlay-field">
        <label class="edit-overlay-label" for="edit-overlay-addr">Address</label>
        <input type="text" id="edit-overlay-addr"
               class="edit-overlay-input edit-overlay-input--disabled"
               [value]="'0x' + addressHex()"
               disabled
               readonly />
      </div>
      <div class="edit-overlay-field">
        <label class="edit-overlay-label" for="edit-overlay-instr-in">Instruction</label>
        <input type="text" id="edit-overlay-instr-in"
               class="edit-overlay-input"
               placeholder="e.g. ADDIU $v0, $zero, 5"
               spellcheck="false"
               #instrInput
               [value]="instrValue()"
               (input)="onInstrInput($event)"
               (keydown.enter)="focusBytes()"
               (keydown.escape)="close()" />
      </div>
      <div class="edit-overlay-field">
        <label class="edit-overlay-label" for="edit-overlay-bytes-in">Bytes (hex)</label>
        <input type="text" id="edit-overlay-bytes-in"
               class="edit-overlay-input"
               placeholder="e.g. 24020005"
               spellcheck="false"
               #bytesInput
               [value]="bytesValue()"
               (input)="onBytesInput($event)"
               (keydown.enter)="focusLabel()"
               (keydown.escape)="close()" />
      </div>
      <div id="edit-overlay-disasm-preview"
           class="edit-overlay-preview"
           [innerHTML]="colorizedDisasmPreview()"></div>
      <div class="edit-overlay-field">
        <label class="edit-overlay-label" for="edit-overlay-label-input">Label</label>
        <input type="text" id="edit-overlay-label-input"
               class="edit-overlay-input"
               placeholder="Label name (leave empty to remove)"
               #labelInput
               [value]="labelValue()"
               (input)="labelValue.set(asInput($event).value)"
               (keydown.enter)="focusComment()"
               (keydown.escape)="close()" />
      </div>
      <div class="edit-overlay-field">
        <label class="edit-overlay-label" for="edit-overlay-comment-input">Comment</label>
        <input type="text" id="edit-overlay-comment-input"
               class="edit-overlay-input"
               placeholder="Comment (leave empty to remove)"
               #commentInput
               [value]="commentValue()"
               (input)="commentValue.set(asInput($event).value)"
               (keydown.enter)="save()"
               (keydown.escape)="close()" />
      </div>
      <div id="edit-overlay-actions">
        <button id="edit-overlay-save" (click)="save()">Save</button>
        <button id="edit-overlay-cancel" (click)="close()">Cancel</button>
      </div>
    </div>
    <div id="edit-overlay-tab-history"
         class="edit-overlay-tab-content"
         [class.active]="activeTab() === 'history'">
      <div id="edit-overlay-history-list" class="edit-overlay-history-list">
        @if (historyLoading()) {
          <div class="edit-overlay-history-empty">Loading history...</div>
        } @else if (history().length === 0) {
          <div class="edit-overlay-history-empty">No snapshot history for this address.</div>
        } @else {
          @for (h of history(); track h.date + h.value) {
            <div class="edit-overlay-history-row">
              <span class="edit-overlay-history-value">{{ h.value }}</span>
              @if (h.disasm) {
                <span class="edit-overlay-history-disasm"
                      [innerHTML]="colorizeRegisters(h.disasm)"></span>
              }
              <span class="edit-overlay-history-name">{{ h.name }}</span>
              <span class="edit-overlay-history-date">{{ h.date }}</span>
            </div>
          }
        }
      </div>
    </div>
    <div id="edit-overlay-tab-refs"
         class="edit-overlay-tab-content"
         [class.active]="activeTab() === 'refs'">
      <div class="edit-overlay-refs-list">
        @if (refsLoading()) {
          <div class="edit-overlay-refs-loading">
            <span class="edit-overlay-spinner"></span>
            Searching references&hellip;
          </div>
        } @else if (refs().length === 0) {
          <div class="edit-overlay-history-empty">No references found.</div>
        } @else {
          @for (r of refs(); track r.address) {
            <div class="edit-overlay-refs-row">
              <span class="edit-overlay-refs-addr">0x{{ r.address }}</span>
              @if (r.instruction) {
                <span class="edit-overlay-refs-instr"
                      [innerHTML]="colorizeRegisters(r.instruction)"></span>
              }
            </div>
          }
        }
      </div>
    </div>
  </div>
</div>
  `,
})
export class EditOverlayComponent {
  // ── Inputs ──────────────────────────────────────────────────────────────────

  /** External disasm preview text (set by parent when assembleInstrResult or disasmBytesResult arrives). */
  readonly externalDisasmPreview = input('');

  /** External hex bytes (set by parent when assembleInstrResult arrives, to sync bytes field). */
  readonly externalHexBytes = input('');

  /** External instruction preview (set by parent when disasmBytesResult arrives, to sync instr field). */
  readonly externalInstrPreview = input('');

  // ── Outputs ─────────────────────────────────────────────────────────────────

  readonly save$ = output<EditOverlaySaveEvent>();
  readonly close$ = output<void>();

  /** Emitted on instruction input to request assembly preview. */
  readonly assembleInstr = output<{ instruction: string; address: number }>();

  /** Emitted on bytes input to request disassembly preview. */
  readonly disasmBytes = output<{ hexBytes: string; address: number }>();

  /** Emitted when the overlay opens to request history. */
  readonly requestHistory = output<number>();

  /** Emitted when the references tab is first activated. */
  readonly requestRefs = output<number>();

  // ── State ───────────────────────────────────────────────────────────────────

  readonly visible = signal(false);
  readonly activeTab = signal<'edit' | 'history' | 'refs'>('edit');

  readonly addressHex = signal('');
  readonly instrValue = signal('');
  readonly bytesValue = signal('');
  readonly labelValue = signal('');
  readonly commentValue = signal('');
  readonly disasmPreview = signal('');

  readonly history = signal<HistoryEntry[]>([]);
  readonly historyLoading = signal(true);

  readonly refs = signal<RefEntry[]>([]);
  readonly refsLoading = signal(false);

  /** Colorized version of disasmPreview for innerHTML binding. */
  readonly colorizedDisasmPreview = computed(() => this.colorizeRegisters(this.disasmPreview()));

  private _address = 0;
  private _originalBytes = '';

  /** Whether refs have been requested for the current overlay session. */
  private _refsRequested = false;

  /** Tracks which field is focused to avoid feedback loops. */
  private _instrFocused = false;
  private _bytesFocused = false;

  // ── View Children ─────────────────────────────────────────────────────────

  private readonly instrInputRef = viewChild<ElementRef<HTMLInputElement>>('instrInput');
  private readonly bytesInputRef = viewChild<ElementRef<HTMLInputElement>>('bytesInput');
  private readonly labelInputRef = viewChild<ElementRef<HTMLInputElement>>('labelInput');
  private readonly commentInputRef = viewChild<ElementRef<HTMLInputElement>>('commentInput');

  constructor() {
    // Sync external disasm preview
    effect(() => {
      const preview = this.externalDisasmPreview();
      this.disasmPreview.set(preview);
    });

    // Sync external hex bytes (from assembleInstrResult) — only when bytes input not focused
    effect(() => {
      const hex = this.externalHexBytes();
      if (hex && !this._bytesFocused) {
        this.bytesValue.set(hex);
      }
    });

    // Sync external instruction preview (from disasmBytesResult) — only when instr input not focused
    effect(() => {
      const instr = this.externalInstrPreview();
      if (instr && instr !== '(invalid)' && !this._instrFocused) {
        this.instrValue.set(instr);
      }
    });
  }

  // ── Public API ──────────────────────────────────────────────────────────────

  /** Opens the overlay with the given instruction data. */
  open(data: EditOverlayOpenData): void {
    this._address = data.address;
    this.addressHex.set(data.addressHex.toUpperCase());

    const instrStr = (data.op || '') + (data.args ? '  ' + data.args : '');
    this.instrValue.set(instrStr);

    const rawBytes = (data.bytes || '').replace(/\s+/g, '');
    this._originalBytes = rawBytes;
    this.bytesValue.set(rawBytes);

    this.disasmPreview.set('');
    this.labelValue.set(data.currentLabel || '');
    this.commentValue.set(data.currentComment || '');

    this.activeTab.set('edit');
    this.historyLoading.set(true);
    this.history.set([]);

    this.refs.set([]);
    this.refsLoading.set(false);
    this._refsRequested = false;

    this.requestHistory.emit(data.address);
    this.visible.set(true);

    // Focus instruction input after render
    requestAnimationFrame(() => {
      const el = this.instrInputRef()?.nativeElement;
      if (el) { el.focus(); el.select(); }
    });
  }

  /** Closes the overlay. */
  close(): void {
    if (this.visible()) {
      this.visible.set(false);
      this.close$.emit();
    }
  }

  /** Sets the history entries (called by parent when addressHistoryResult arrives). */
  setHistory(entries: HistoryEntry[]): void {
    this.history.set(entries);
    this.historyLoading.set(false);
  }

  /** Sets the references list (called by parent when addressRefsResult arrives). */
  setRefs(entries: RefEntry[]): void {
    this.refs.set(entries);
    this.refsLoading.set(false);
  }

  // ── Event Handlers ──────────────────────────────────────────────────────────

  onInstrInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.instrValue.set(value);
    this._instrFocused = true;
    this._bytesFocused = false;
    const instr = value.trim();
    if (instr) {
      this.assembleInstr.emit({ instruction: instr, address: this._address });
    } else {
      this.disasmPreview.set('');
    }
  }

  onBytesInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.bytesValue.set(value);
    this._bytesFocused = true;
    this._instrFocused = false;
    const hex = value.replace(/\s+/g, '');
    if (hex.length === 8 && /^[0-9a-fA-F]{8}$/.test(hex)) {
      this.disasmBytes.emit({ hexBytes: hex, address: this._address });
    } else {
      this.disasmPreview.set('');
    }
  }

  onRefsTabClick(): void {
    this.activeTab.set('refs');
    if (!this._refsRequested) {
      this._refsRequested = true;
      this.refsLoading.set(true);
      this.refs.set([]);
      this.requestRefs.emit(this._address);
    }
  }

  save(): void {
    const newLabel = this.labelValue().trim();
    const newComment = this.commentValue().trim();
    const newBytes = this.bytesValue().replace(/\s+/g, '').toUpperCase();
    this.save$.emit({
      address: this._address,
      addressHex: this.addressHex(),
      newLabel,
      newComment,
      newBytes,
      originalBytes: (this._originalBytes || '').toUpperCase(),
    });
    this.close();
  }

  onBackdropClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).id === 'edit-overlay') {
      this.close();
    }
  }

  focusBytes(): void {
    const el = this.bytesInputRef()?.nativeElement;
    if (el) { el.focus(); el.select(); }
  }

  focusLabel(): void {
    const el = this.labelInputRef()?.nativeElement;
    if (el) { el.focus(); el.select(); }
  }

  focusComment(): void {
    const el = this.commentInputRef()?.nativeElement;
    if (el) { el.focus(); el.select(); }
  }

  /** Wrap GPR register tokens with color class spans. */
  colorizeRegisters(text: string): string {
    if (!text) return text;
    return text.replace(this._GPR_REGEX, (match, name) => {
      const cls = this._GPR_NAME_TO_CAT[name];
      if (!cls) return match;
      const dollar = match.startsWith('$') ? '$' : '';
      return `<span class="${cls}">${dollar}${name}</span>`;
    });
  }

  /** Helper for template: cast Event.target to HTMLInputElement. */
  asInput(event: Event): HTMLInputElement {
    return event.target as HTMLInputElement;
  }

  // ── Private ─────────────────────────────────────────────────────────────────

  private readonly _GPR_NAME_TO_CAT: Readonly<Record<string, string>> = {
    zero: 'dbg-reg-zero', at: 'dbg-reg-at',
    v0: 'dbg-reg-v', v1: 'dbg-reg-v',
    a0: 'dbg-reg-a', a1: 'dbg-reg-a', a2: 'dbg-reg-a', a3: 'dbg-reg-a',
    t0: 'dbg-reg-t', t1: 'dbg-reg-t', t2: 'dbg-reg-t', t3: 'dbg-reg-t',
    t4: 'dbg-reg-t', t5: 'dbg-reg-t', t6: 'dbg-reg-t', t7: 'dbg-reg-t',
    t8: 'dbg-reg-t', t9: 'dbg-reg-t',
    s0: 'dbg-reg-s', s1: 'dbg-reg-s', s2: 'dbg-reg-s', s3: 'dbg-reg-s',
    s4: 'dbg-reg-s', s5: 'dbg-reg-s', s6: 'dbg-reg-s', s7: 'dbg-reg-s',
    k0: 'dbg-reg-k', k1: 'dbg-reg-k',
    gp: 'dbg-reg-special', sp: 'dbg-reg-special', fp: 'dbg-reg-special', ra: 'dbg-reg-special',
  };

  private readonly _GPR_REGEX = /\$?(zero|at|v[01]|a[0-3]|t[0-9]|s[0-7]|k[01]|gp|sp|fp|ra)(?![A-Za-z0-9_])/g;
}
