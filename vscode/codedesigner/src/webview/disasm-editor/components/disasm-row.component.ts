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
      [class.branch-highlight]="isBranchHighlight()"
      [class.branch-path]="isBranchPath()"
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

  /** Whether this row is the target of the currently selected branch/jump. */
  protected readonly isBranchHighlight = computed(() => {
    if (this.isActive()) { return false; }
    const branch = this.selectedBranch();
    if (!branch?.spansPath) { return false; }

    const rowAddr = parseInt(this.data().addr, 16);
    return this.toPhysAddr(rowAddr) === this.toPhysAddr(branch.target);
  });

  /** Whether this row sits between the selected branch/jump and its target. */
  protected readonly isBranchPath = computed(() => {
    if (this.isActive()) { return false; }
    const branch = this.selectedBranch();
    if (!branch) { return false; }

    const rowAddr = this.toPhysAddr(parseInt(this.data().addr, 16));
    const source = this.toPhysAddr(branch.source);
    const target = this.toPhysAddr(branch.target);
    const low = Math.min(source, target);
    const high = Math.max(source, target);

    return rowAddr > low && rowAddr < high;
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
    if (!rawLabel) return '';

    // FNC_ prefix → render with function label class
    if (rawLabel.startsWith('FNC_')) {
      const escaped = this.escapeHtml(rawLabel);
      return `<span class="lm-label-fnc">${escaped}</span>`;
    }

    // Array / linked-list element label → Name[i] subscript styling
    const arrayMatch = rawLabel.match(/^(.*?)(\[\d+\])$/);
    if (arrayMatch) {
      const base = this.escapeHtml(arrayMatch[1]);
      const idx  = this.escapeHtml(arrayMatch[2]);
      return this.highlightSearch(`${base}<span class="lm-label-idx">${idx}</span>`, 'label');
    }

    // String label → truncate long content, show full text as title tooltip
    const strMatch = rawLabel.match(/^(.*?): "(.*)"$/);
    if (strMatch) {
      const labelPart  = this.escapeHtml(strMatch[1]);
      const strContent = strMatch[2];
      const display    = strContent.length > 40 ? strContent.slice(0, 37) + '…' : strContent;
      const full       = this.escapeHtml(rawLabel);
      return `<span class="lm-label-str" title="${full}">${labelPart}: &quot;${this.escapeHtml(display)}&quot;</span>`;
    }

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
    this.highlightSearch(this.colorizeComment(this.data().comment || ''), 'comment')
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

  private readonly CONTROL_FLOW_OPS = new Set([
    'b', 'beq', 'beql', 'bne', 'bnel', 'blez', 'blezl', 'bgtz', 'bgtzl',
    'bltz', 'bltzl', 'bgez', 'bgezl', 'bltzal', 'bltzall', 'bgezal', 'bgezall',
    'bc1f', 'bc1fl', 'bc1t', 'bc1tl', 'bal', 'j', 'jal',
  ]);

  private readonly BRANCH_PATH_OPS = new Set([
    'b', 'beq', 'beql', 'bne', 'bnel', 'blez', 'blezl', 'bgtz', 'bgtzl',
    'bltz', 'bltzl', 'bgez', 'bgezl', 'bltzal', 'bltzall', 'bgezal', 'bgezall',
    'bc1f', 'bc1fl', 'bc1t', 'bc1tl', 'bal',
  ]);

  private branchTarget(row: DisasmRow | undefined): number | undefined {
    if (!row) { return undefined; }
    const op = row.op.trim().toLowerCase();
    if (!this.CONTROL_FLOW_OPS.has(op)) { return undefined; }

    return this.addressFromArgs(row.args) ?? this.decodeBranchTarget(row);
  }

  private selectedBranch(): { source: number; target: number; spansPath: boolean } | undefined {
    const row = this.state.currentRow();
    const target = this.branchTarget(row);
    if (!row || target === undefined) { return undefined; }
    return {
      source: parseInt(row.addr, 16) >>> 0,
      target,
      spansPath: this.BRANCH_PATH_OPS.has(row.op.trim().toLowerCase()),
    };
  }

  private addressFromArgs(args: string): number | undefined {
    const target = args.match(/(?:\$|0x)([0-9a-f]{8})\b/i)
      ?? args.match(/(?:^|[^0-9a-f])([0-9a-f]{8})(?![0-9a-f])/i);
    return target ? parseInt(target[1], 16) >>> 0 : undefined;
  }

  private decodeBranchTarget(row: DisasmRow): number | undefined {
    const wordHex = row.bytes.replace(/[^0-9a-f]/gi, '');
    if (wordHex.length < 8) { return undefined; }

    const word = parseInt(wordHex.slice(0, 8), 16) >>> 0;
    const opcode = (word >>> 26) & 0x3f;
    const pc = parseInt(row.addr, 16) >>> 0;

    if (opcode === 0x02 || opcode === 0x03) {
      return (((pc + 4) & 0xf0000000) | ((word & 0x03ffffff) << 2)) >>> 0;
    }

    const imm = word & 0xffff;
    const signedImm = imm & 0x8000 ? imm - 0x10000 : imm;
    const relativeTarget = (pc + 4 + (signedImm << 2)) >>> 0;

    if ([0x04, 0x05, 0x06, 0x07, 0x14, 0x15, 0x16, 0x17].includes(opcode)) {
      return relativeTarget;
    }

    if (opcode === 0x01) {
      const rt = (word >>> 16) & 0x1f;
      return [0x00, 0x01, 0x02, 0x03, 0x10, 0x11, 0x12, 0x13].includes(rt)
        ? relativeTarget
        : undefined;
    }

    if (opcode === 0x11) {
      const rs = (word >>> 21) & 0x1f;
      const rt = (word >>> 16) & 0x03;
      return rs === 0x08 && rt <= 0x03 ? relativeTarget : undefined;
    }

    return undefined;
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
    gp: 'dbg-reg-gp', sp: 'dbg-reg-sp', fp: 'dbg-reg-fp', ra: 'dbg-reg-ra',
  };

  /** Regex for matching GPR names */
  private readonly GPR_REGEX = /\$?(zero|at|v[01]|a[0-3]|t[0-9]|s[0-7]|k[01]|gp|sp|fp|ra)(?![A-Za-z0-9_])/g;

  /** Wrap GPR register tokens with color class spans */
  private colorizeRegisters(text: string): string {
    if (!text) return text;
    const colored = text.replace(this.GPR_REGEX, (match, name) => {
      const cls = this.GPR_NAME_TO_CAT[name];
      if (!cls) return match;
      const sigil = match.startsWith('$') ? '$' : '';
      return `${sigil}<span class="${cls}">${name}</span>`;
    });
    return this.colorizeOperandSyntax(colored);
  }

  private colorizeOperandSyntax(html: string): string {
    return html.split(/(<[^>]+>)/g).map(part => {
      if (part.startsWith('<')) { return part; }
      return this.colorizeOperandText(part);
    }).join('');
  }

  private colorizeOperandText(text: string): string {
    let html = '';
    for (let i = 0; i < text.length; i++) {
      const ch = text[i];
      const offset = text.slice(i + 1, i + 5);
      if (ch === '$' && /^[0-9a-f]{4}$/i.test(offset) && text[i + 5] === '(') {
        html += '<span class="dbg-reg-sigil">$</span>';
        html += `<span class="dbg-arg-offset">${offset}</span>`;
        i += 4;
        continue;
      }

      if (ch === '$') {
        html += '<span class="dbg-reg-sigil">$</span>';
      } else if (ch === ',') {
        html += '<span class="dbg-arg-comma">,</span>';
      } else if (ch === '(' || ch === ')') {
        html += `<span class="dbg-reg-paren">${ch}</span>`;
      } else {
        html += ch;
      }
    }
    return html;
  }

  private colorizeComment(text: string): string {
    if (!text) { return text; }
    let html = this.escapeHtml(text);
    html = html.replace(/(&quot;.*?&quot;)/g, '<span class="comment-resolved">$1</span>');
    html = html.split(/(<[^>]+>)/g).map(part => {
      if (part.startsWith('<')) { return part; }
      return part
        .replace(/\b(0x[0-9a-f]{8}|[0-9a-f]{8})\b/gi, '<span class="comment-resolved">$1</span>')
        .replace(/\b(0x[0-9a-f]{4})\b/gi, '<span class="comment-offset">$1</span>');
    }).join('');
    return html;
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

  /** Escape HTML special characters for safe innerHTML insertion. */
  private escapeHtml(text: string): string {
    return text
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }
}
