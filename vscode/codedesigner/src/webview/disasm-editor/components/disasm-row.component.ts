import {
  Component,
  ViewEncapsulation,
  input,
  output,
  computed,
  inject,
  ChangeDetectionStrategy,
} from '@angular/core';
import { EditorState } from '../state/editor.state';
import { DisasmRow } from '../state/types';

/**
 * DisasmRowComponent - Renders a single disassembly instruction row.
 *
 * Uses Angular's signal-based inputs and computed properties for optimal
 * change detection in a virtual scroll context.
 */
@Component({
  selector: 'app-disasm-row',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div
      class="row"
      [class.active]="isActive()"
      [class.current-pc]="isPc()"
      [class.search-match]="isSearchMatch()"
      [class.search-current]="isCurrentMatch()"
      [class.breakpoint]="hasBreakpoint()"
      [class.breakpoint-active]="hasBreakpoint()"
      [class.breakpoint-hit]="isBreakpointHit()"
      [attr.data-index]="localIndex()"
      [attr.data-global-index]="globalIndex()"
      (click)="onClick()"
      (dblclick)="onDblClick()"
      (contextmenu)="onContextMenu($event)"
    >
      <span class="addr" [innerHTML]="addrHtml()"></span>
      <span class="bytes" [innerHTML]="bytesHtml()"></span>
      <span class="label" [innerHTML]="labelHtml()"></span>
      <span class="op" [innerHTML]="opHtml()"></span>
      <span class="args" [innerHTML]="argsHtml()"></span>
      <span class="comment" [innerHTML]="commentHtml()"></span>
    </div>
  `,
})
export class DisasmRowComponent {
  protected readonly state = inject(EditorState);

  /** The row data to render */
  readonly data = input.required<DisasmRow>();

  /** Global index of this row in the full instruction list */
  readonly globalIndex = input.required<number>();

  /** Local index within the current page */
  readonly localIndex = input.required<number>();

  /** Pointer label map for resolving address labels */
  readonly pointerLabelMap = input<Map<string, string>>(new Map());

  /** Output events */
  readonly select = output<number>();
  readonly edit = output<{ row: DisasmRow; address: number; addressHex: string }>();
  readonly contextMenu = output<{ event: MouseEvent; row: DisasmRow; globalIndex: number }>();

  // ═══════════════════════════════════════════════════════════════════════════
  // Computed State Classes
  // ═══════════════════════════════════════════════════════════════════════════

  /** Whether this row is the current cursor position */
  protected readonly isActive = computed(() =>
    this.globalIndex() === this.state.cursor()
  );

  /** Whether this row contains the PC */
  protected readonly isPc = computed(() => {
    const pcAddr = this.state.pcAddress();
    if (pcAddr === undefined) return false;

    const rowAddr = parseInt(this.data().addr, 16);
    return this.toPhysAddr(pcAddr) === this.toPhysAddr(rowAddr);
  });

  /** Whether this row has a search match */
  protected readonly isSearchMatch = computed(() => {
    const matches = this.state.searchMatches();
    return matches.some(m => m.instructionIndex === this.globalIndex());
  });

  /** Whether this row is the current search match */
  protected readonly isCurrentMatch = computed(() => {
    const match = this.state.currentMatch();
    return match?.instructionIndex === this.globalIndex();
  });

  /** Whether this row has a breakpoint */
  protected readonly hasBreakpoint = computed(() => {
    const addr = parseInt(this.data().addr, 16);
    return this.state.breakpoints().has(addr);
  });

  /** Whether this row's breakpoint was just hit */
  protected readonly isBreakpointHit = computed(() => {
    const hitAddr = this.state.breakpointHitAddress();
    if (hitAddr === undefined) return false;

    const rowAddr = parseInt(this.data().addr, 16);
    return this.toPhysAddr(hitAddr) === this.toPhysAddr(rowAddr);
  });

  // ═══════════════════════════════════════════════════════════════════════════
  // Computed HTML Content
  // ═══════════════════════════════════════════════════════════════════════════

  protected readonly addrHtml = computed(() =>
    this.highlightSearch(this.data().addr, 'addr')
  );

  protected readonly bytesHtml = computed(() =>
    this.highlightSearch(this.data().bytes, 'bytes')
  );

  protected readonly labelHtml = computed(() => {
    const row = this.data();
    // Label precedence: server/user labels take priority over pointer-manager labels
    const rawLabel = row.label || this.pointerLabelMap().get(row.addr) || '';
    return this.highlightSearch(rawLabel, 'label');
  });

  protected readonly opHtml = computed(() =>
    this.highlightSearch(this.data().op, 'op')
  );

  protected readonly argsHtml = computed(() => {
    const row = this.data();
    let args = row.args;

    // Format branch offset annotations
    const branchMatch = args.match(/^(.*\$[0-9A-F]{8})\s*(\([+-]\d+[▼▲]\))(.*)$/);
    if (branchMatch) {
      args = `${branchMatch[1]} <span class="branch-offset-annotation">${branchMatch[2]}</span>${branchMatch[3]}`;
    }

    // Colorize register names
    args = this.colorizeRegisters(args);

    return this.highlightSearch(args, 'args');
  });

  protected readonly commentHtml = computed(() =>
    this.highlightSearch(this.data().comment || '', 'comment')
  );

  // ═══════════════════════════════════════════════════════════════════════════
  // Event Handlers
  // ═══════════════════════════════════════════════════════════════════════════

  protected onClick(): void {
    this.select.emit(this.globalIndex());
  }

  protected onDblClick(): void {
    const row = this.data();
    this.edit.emit({
      row,
      address: parseInt(row.addr, 16),
      addressHex: row.addr,
    });
  }

  protected onContextMenu(event: MouseEvent): void {
    event.preventDefault();
    this.contextMenu.emit({
      event,
      row: this.data(),
      globalIndex: this.globalIndex(),
    });
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Utility Methods
  // ═══════════════════════════════════════════════════════════════════════════

  /** Convert virtual address to physical address for EE memory */
  private toPhysAddr(v: number): number {
    if (v >= 0x80000000 && v < 0xC0000000) return v - 0x80000000;
    if (v >= 0xA0000000 && v < 0xC0000000) return v - 0xA0000000;
    return v;
  }

  /** GPR name to category class mapping */
  private readonly GPR_NAME_TO_CAT: Readonly<Record<string, string>> = {
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

  /** Regex for matching GPR names */
  private readonly GPR_REGEX = /\$?(zero|at|v[01]|a[0-3]|t[0-9]|s[0-7]|k[01]|gp|sp|fp|ra)(?![A-Za-z0-9_])/g;

  /** Wrap GPR register tokens with color class spans */
  private colorizeRegisters(text: string): string {
    if (!text) return text;
    return text.replace(this.GPR_REGEX, (match, name) => {
      const cls = this.GPR_NAME_TO_CAT[name];
      if (!cls) return match;
      const dollar = match.startsWith('$') ? '$' : '';
      return `<span class="${cls}">${dollar}${name}</span>`;
    });
  }

  /** Highlight search matches within text */
  private highlightSearch(text: string, field: string): string {
    const query = this.state.searchQuery();
    if (!query || !text) return text;

    const matches = this.state.searchMatches();
    const match = matches.find(
      m => m.instructionIndex === this.globalIndex() && m.field === field
    );

    if (!match) return text;

    const isCurrent = this.isCurrentMatch();
    const highlightClass = isCurrent ? 'search-current-highlight' : 'search-match-highlight';

    const before = text.substring(0, match.start);
    const highlighted = text.substring(match.start, match.start + match.length);
    const after = text.substring(match.start + match.length);

    return `${before}<span class="${highlightClass}">${highlighted}</span>${after}`;
  }
}
