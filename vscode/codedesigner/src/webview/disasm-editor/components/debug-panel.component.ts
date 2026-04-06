import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  input,
  output,
  signal,
  computed,
  OnDestroy,
} from '@angular/core';
import { RegisterData } from '../disasm-editor.service';
import { PineWatchInfo } from '../state/types';

// ═══════════════════════════════════════════════════════════════════════════════
// Constants for register names
// ═══════════════════════════════════════════════════════════════════════════════

const MIPS_REG_NAMES = [
  '$zero', '$at', '$v0', '$v1', '$a0', '$a1', '$a2', '$a3',
  '$t0', '$t1', '$t2', '$t3', '$t4', '$t5', '$t6', '$t7',
  '$s0', '$s1', '$s2', '$s3', '$s4', '$s5', '$s6', '$s7',
  '$t8', '$t9', '$k0', '$k1', '$gp', '$sp', '$fp', '$ra',
];

const COP0_REG_NAMES = [
  'Index', 'Random', 'EntryLo0', 'EntryLo1', 'Context', 'PageMask', 'Wired', 'Reserved7',
  'BadVAddr', 'Count', 'EntryHi', 'Compare', 'Status', 'Cause', 'EPC', 'PRId',
  'Config', 'LLAddr', 'WatchLo', 'WatchHi', 'XContext', 'Reserved21', 'Reserved22', 'Reserved23',
  'Debug', 'PerfCnt', 'ErrCtl', 'CacheErr', 'TagLo', 'TagHi', 'ErrorEPC', 'Reserved31',
];

const GS_PRIV_NAMES = [
  'PMODE', 'SMODE1', 'SMODE2', 'SRFSH', 'SYNCH1', 'SYNCH2', 'SYNCV',
  'DISPFB1', 'DISPLAY1', 'DISPFB2', 'DISPLAY2',
  'EXTBUF', 'EXTDATA', 'EXTWRITE', 'BGCOLOR',
  'CSR', 'IMR', 'BUSDIR', 'SIGLBLID',
];

const COP2_REG_CTL = [
  'vi00', 'vi01', 'vi02', 'vi03', 'vi04', 'vi05', 'vi06', 'vi07',
  'vi08', 'vi09', 'vi10', 'vi11', 'vi12', 'vi13', 'vi14', 'vi15',
  'Status', 'MACflag', 'ClipFlag', 'c2c19', 'R', 'I', 'Q', 'c2c23',
  'c2c24', 'c2c25', 'TPC', 'CMSAR0', 'FBRST', 'VPU-STAT', 'c2c30', 'CMSAR1',
];

export type DebugTab = 'registers' | 'cop0' | 'fpr' | 'fcr' | 'vu0f' | 'vu0i' | 'gs' | 'inspect';

/**
 * DebugPanelComponent - Displays CPU registers across 8 tabs.
 *
 * Tabs: GPR, CP0, FPR, FCR, VU0f, VU0i, GS, Stack
 *
 * Uses signal inputs for reactive updates without zone.js.
 */
@Component({
  selector: 'app-debug-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="debug-panel" [class.visible]="visible()" [style.width.px]="panelWidth()" [style.minWidth.px]="MIN_WIDTH">
      <div class="debug-resize-handle" (mousedown)="onResizeMouseDown($event)"></div>
      <div class="debug-tabs">
        @for (tab of tabs; track tab.id) {
          <button
            class="debug-tab"
            [class.active]="activeTab() === tab.id"
            (click)="selectTab(tab.id)"
          >{{ tab.label }}</button>
        }
      </div>

      <!-- GPR Tab -->
      @if (activeTab() === 'registers') {
        <div class="debug-tab-content active" [innerHTML]="gprHtml()"></div>
      }

      <!-- COP0 Tab -->
      @if (activeTab() === 'cop0') {
        <div class="debug-tab-content active" [innerHTML]="cop0Html()"></div>
      }

      <!-- FPR Tab -->
      @if (activeTab() === 'fpr') {
        <div class="debug-tab-content active" [innerHTML]="fprHtml()"></div>
      }

      <!-- FCR Tab -->
      @if (activeTab() === 'fcr') {
        <div class="debug-tab-content active" [innerHTML]="fcrHtml()"></div>
      }

      <!-- VU0f Tab -->
      @if (activeTab() === 'vu0f') {
        <div class="debug-tab-content active" [innerHTML]="vu0fHtml()"></div>
      }

      <!-- VU0i Tab -->
      @if (activeTab() === 'vu0i') {
        <div class="debug-tab-content active" [innerHTML]="vu0iHtml()"></div>
      }

      <!-- GS Tab -->
      @if (activeTab() === 'gs') {
        <div class="debug-tab-content active" [innerHTML]="gsHtml()"></div>
      }

      <!-- Inspect Tab (Stack + Watches) -->
      @if (activeTab() === 'inspect') {
        <div class="debug-tab-content active inspect-tab-content">

          <!-- Watches Section -->
          <div class="inspect-section-header">
            <span class="inspect-section-title">PINE Watchpoints</span>
            <button class="bp-clear-all-btn" (click)="clearAllWatches.emit()">Clear All</button>
          </div>
          <table class="dbg-table">
            <thead><tr>
              <th class="dbg-th">CPU</th>
              <th class="dbg-th">Range</th>
              <th class="dbg-th">Condition</th>
              <th class="dbg-th"></th>
            </tr></thead>
            <tbody>
              @if (!sortedWatches().length) {
                <tr><td colspan="4" class="dbg-td-empty">No watchpoints — right-click an instruction to add one</td></tr>
              }
              @for (w of sortedWatches(); track w.Start) {
                <tr class="dbg-table-row clickable" (click)="navigateToWatchAddress.emit(parseWatchStart(w))">
                  <td class="dbg-td-type mono watch-cpu-badge" [class.watch-cpu-iop]="w.Cpu === 'IOP'">{{ w.Cpu }}</td>
                  <td class="dbg-td-addr mono">{{ w.Start }}–{{ w.End }}</td>
                  <td class="dbg-td-type mono watch-cond-badge" [class]="watchCondClass(w.Cond)">{{ w.Cond }}</td>
                  <td class="dbg-td-del">
                    <button class="dbg-bp-del-btn"
                      (click)="$event.stopPropagation(); removeWatch.emit(w)">✕</button>
                  </td>
                </tr>
              }
            </tbody>
          </table>

          <!-- Stack Trace Section -->
          <div class="inspect-section-header inspect-section-stack">
            <span class="inspect-section-title">Stack</span>
          </div>
          <div [innerHTML]="stackHtml()"></div>
        </div>
      }
    </div>
  `,
  styles: [`
    /* Styles provided by parent CSS via ViewEncapsulation.None */
  `],
  host: {
    '(click)': 'onHostClick($event)',
  },
})
export class DebugPanelComponent implements OnDestroy {
  // ═══════════════════════════════════════════════════════════════════════════
  // Resize constants
  // ═══════════════════════════════════════════════════════════════════════════

  readonly MIN_WIDTH = 180;
  private readonly MAX_WIDTH = 800;
  private readonly STORAGE_KEY = 'codedesigner.debugPanel.width';

  // ═══════════════════════════════════════════════════════════════════════════
  // Inputs
  // ═══════════════════════════════════════════════════════════════════════════

  /** Whether the panel is visible */
  readonly visible = input(false);

  /** Register data from emulator */
  readonly registers = input<RegisterData | null>(null);

  /** Stack memory bytes */
  readonly stackBytes = input<number[]>([]);

  /** Base address of stack memory */
  readonly stackBase = input(0);

  /** Code region bounds for stack trace analysis */
  readonly codeStart = input(0);
  readonly codeEnd = input(0x02000000);

  /** Active PINE hardware watchpoints to display in the Inspect tab. */
  readonly watches = input<PineWatchInfo[]>([]);

  // ═══════════════════════════════════════════════════════════════════════════
  // Outputs
  // ═══════════════════════════════════════════════════════════════════════════

  /** Emitted when user wants to edit a register */
  readonly editRegister = output<{ regType: string; regIdx: number; currentValue: string }>();

  /** Emitted when the user clicks the delete button for a watch entry. */
  readonly removeWatch = output<PineWatchInfo>();

  /** Emitted when the user clicks "Clear All" in the watches section. */
  readonly clearAllWatches = output<void>();

  /** Emitted when the user clicks a watch row (navigate to address). */
  readonly navigateToWatchAddress = output<number>();

  // ═══════════════════════════════════════════════════════════════════════════
  // State
  // ═══════════════════════════════════════════════════════════════════════════

  readonly activeTab = signal<DebugTab>('registers');

  /** Current panel width in pixels, persisted to localStorage */
  readonly panelWidth = signal(this._loadWidth());

  /** Sorted array of PINE watchpoints by start address for @for iteration. */
  readonly sortedWatches = computed(() =>
    [...this.watches()].sort((a, b) =>
      parseInt(a.Start, 16) - parseInt(b.Start, 16)),
  );

  readonly tabs: { id: DebugTab; label: string }[] = [
    { id: 'registers', label: 'GPR' },
    { id: 'cop0', label: 'CP0' },
    { id: 'fpr', label: 'FPR' },
    { id: 'fcr', label: 'FCR' },
    { id: 'vu0f', label: 'VU0f' },
    { id: 'vu0i', label: 'VU0i' },
    { id: 'gs', label: 'GS' },
    { id: 'inspect', label: 'Inspect' },
  ];

  // ═══════════════════════════════════════════════════════════════════════════
  // Computed HTML (GPR)
  // ═══════════════════════════════════════════════════════════════════════════

  protected readonly gprHtml = computed(() => {
    const regs = this.registers();
    if (!regs) return '<div class="dbg-row">No register data</div>';

    const lo32 = regs.gprLo32 ?? [];
    const hi32 = regs.gprHi32 ?? [];
    const upLo = regs.gprUpLo32 ?? [];
    const upHi = regs.gprUpHi32 ?? [];

    const gpr128 = (i: number): string => {
      const l0 = lo32[i] ?? 0, l1 = hi32[i] ?? 0;
      const u0 = upLo[i] ?? 0, u1 = upHi[i] ?? 0;
      const upperZero = u0 === 0 && u1 === 0;
      const upperCls = upperZero ? ' class="dbg-qw-upper"' : '';
      return `<span${upperCls}>${this.hex32(u1)}_${this.hex32(u0)}</span>_${this.hex32(l1)}_${this.hex32(l0)}`;
    };

    const gprNz = (i: number): boolean =>
      (lo32[i] ?? 0) !== 0 || (hi32[i] ?? 0) !== 0 ||
      (upLo[i] ?? 0) !== 0 || (upHi[i] ?? 0) !== 0;

    let html = '<div class="dbg-section-title">General Purpose Registers (128-bit)</div>';
    html += `<div class="dbg-row" data-reg-type="PC" data-reg-idx="0"><span class="dbg-name">pc   </span><span class="dbg-val dbg-editable" title="Double-click to edit">${this.hex32(regs.pc ?? 0)}</span></div>`;
    html += `<div class="dbg-row" data-reg-type="HI" data-reg-idx="0"><span class="dbg-name">hi   </span><span class="dbg-val dbg-editable" title="Double-click to edit">${this.hex32(regs.hi ?? 0)}</span>`;
    html += ` <span class="dbg-name" style="margin-left:8px">lo   </span><span class="dbg-val dbg-editable" data-reg-type="LO" title="Double-click to edit">${this.hex32(regs.lo ?? 0)}</span></div>`;
    html += `<div class="dbg-row" data-reg-type="HI1" data-reg-idx="0"><span class="dbg-name">hi1  </span><span class="dbg-val dbg-editable" title="Double-click to edit">${this.hex32(regs.hi1 ?? 0)}</span>`;
    html += ` <span class="dbg-name" style="margin-left:8px">lo1  </span><span class="dbg-val dbg-editable" data-reg-type="LO1" title="Double-click to edit">${this.hex32(regs.lo1 ?? 0)}</span></div>`;
    html += `<div class="dbg-row" data-reg-type="SA" data-reg-idx="0"><span class="dbg-name">sa   </span><span class="dbg-val dbg-editable" title="Double-click to edit">${this.hex32(regs.sa ?? 0)}</span></div>`;
    html += '<div class="dbg-divider"></div>';
    html += '<div class="dbg-row dbg-qw-header"><span class="dbg-name">     </span><span class="dbg-qw-hdr-val"><span class="dbg-qw-col">bits[127:96]</span><span class="dbg-qw-sep"> </span><span class="dbg-qw-col">bits[95:64]</span><span class="dbg-qw-sep"> </span><span class="dbg-qw-col">bits[63:32]</span><span class="dbg-qw-sep"> </span><span class="dbg-qw-col">bits[31:0]</span></span></div>';

    for (let i = 0; i < 32; i++) {
      const name = MIPS_REG_NAMES[i].padEnd(5);
      const val = gpr128(i);
      const nz = gprNz(i);
      const ro = i === 0;
      const cat = this.gprCatClass(i);
      const editAttr = ro ? '' : ` data-reg-type="GPR" data-reg-idx="${i}"`;
      html += `<div class="dbg-row${nz ? ' dbg-nonzero' : ''}"${editAttr}><span class="dbg-name ${cat}">${name}</span><span class="dbg-val dbg-qw-val${ro ? '' : ' dbg-editable'}" title="${ro ? '' : 'Double-click to edit'}">${val}</span></div>`;
    }

    // Inline stack trace
    const stackBytes = this.stackBytes();
    const stackBase = this.stackBase();
    if (stackBytes.length > 0) {
      const sp = lo32[29] ?? 0;
      html += '<div class="dbg-divider"></div>';
      html += `<div class="dbg-section-title">Stack Trace  SP=0x${this.hex32(sp)}</div>`;
      const codeAddrs: { addr: number; val: number }[] = [];
      for (let i = 0; i + 4 <= Math.min(stackBytes.length, 128); i += 4) {
        const word = this.readUInt32LE(stackBytes, i);
        if (this.isCodePointer(word)) {
          codeAddrs.push({ addr: (stackBase + i) >>> 0, val: word });
        }
      }
      if (codeAddrs.length > 0) {
        for (const { addr, val } of codeAddrs) {
          html += `<div class="dbg-row dbg-retaddr"><span class="dbg-addr">${this.hex32(addr)}</span><span class="dbg-val">${this.hex32(val)}</span><span class="dbg-dim" style="margin-left:6px">← return addr</span></div>`;
        }
      } else {
        html += '<div class="dbg-row dbg-dim">No return addresses on stack</div>';
      }
    }

    return html;
  });

  // ═══════════════════════════════════════════════════════════════════════════
  // Computed HTML (COP0)
  // ═══════════════════════════════════════════════════════════════════════════

  protected readonly cop0Html = computed(() => {
    const regs = this.registers();
    if (!regs) return '<div class="dbg-row">No register data</div>';

    let html = '<div class="dbg-section-title">System Control (COP0)</div>';
    const cop0 = regs.cop0Regs ?? [];
    for (let i = 0; i < 32; i++) {
      const name = ('r' + i + ' ' + COP0_REG_NAMES[i]).substring(0, 14).padEnd(14);
      const val = this.hex32(cop0[i] ?? 0);
      const nz = (cop0[i] ?? 0) !== 0;
      html += `<div class="dbg-row${nz ? ' dbg-nonzero' : ''}" data-reg-type="COP0" data-reg-idx="${i}"><span class="dbg-name" style="font-size:10px">${name}</span><span class="dbg-val dbg-editable" title="Double-click to edit">${val}</span></div>`;
    }
    return html;
  });

  // ═══════════════════════════════════════════════════════════════════════════
  // Computed HTML (FPR)
  // ═══════════════════════════════════════════════════════════════════════════

  protected readonly fprHtml = computed(() => {
    const regs = this.registers();
    if (!regs) return '<div class="dbg-row">No register data</div>';

    let html = '<div class="dbg-section-title">Floating Point Registers (COP1)</div>';
    const fpr = regs.fpr ?? [];
    for (let i = 0; i < 32; i++) {
      const bits = (fpr[i] ?? 0) >>> 0;
      const fval = this.rawBitsToFloat(bits);
      const nz = bits !== 0;
      html += `<div class="dbg-row${nz ? ' dbg-nonzero' : ''}" data-reg-type="FPR" data-reg-idx="${i}"><span class="dbg-name">f${String(i).padStart(2, '0')} </span><span class="dbg-val dbg-editable" title="Double-click to edit">${this.hex32(bits)}</span><span class="dbg-dim" style="margin-left:6px">${fval}</span></div>`;
    }
    html += '<div class="dbg-divider"></div>';
    const accBits = (regs.fprAcc ?? 0) >>> 0;
    html += `<div class="dbg-row${accBits !== 0 ? ' dbg-nonzero' : ''}"><span class="dbg-name">ACC  </span><span class="dbg-val">${this.hex32(accBits)}</span><span class="dbg-dim" style="margin-left:6px">${this.rawBitsToFloat(accBits)}</span></div>`;
    return html;
  });

  // ═══════════════════════════════════════════════════════════════════════════
  // Computed HTML (FCR)
  // ═══════════════════════════════════════════════════════════════════════════

  protected readonly fcrHtml = computed(() => {
    const regs = this.registers();
    if (!regs) return '<div class="dbg-row">No register data</div>';

    const fcr0 = (regs.fcr0 ?? 0) >>> 0;
    const fcr31 = (regs.fcr31 ?? 0) >>> 0;

    let html = '<div class="dbg-section-title">FPU Control Registers (FCR)</div>';
    html += `<div class="dbg-row"><span class="dbg-name">FCR0  </span><span class="dbg-val">${this.hex32(fcr0)}</span><span class="dbg-dim" style="margin-left:6px">FIR (implementation)</span></div>`;
    html += `<div class="dbg-row${fcr31 !== 0 ? ' dbg-nonzero' : ''}"><span class="dbg-name">FCR31 </span><span class="dbg-val">${this.hex32(fcr31)}</span><span class="dbg-dim" style="margin-left:6px">FCSR (control/status)</span></div>`;
    html += '<div class="dbg-divider"></div>';
    html += '<div class="dbg-section-title">FCR31 decode</div>';

    const rm = fcr31 & 0x3;
    const flags = (fcr31 >> 7) & 0x1F;
    const ena = (fcr31 >> 12) & 0x1F;
    const cause = (fcr31 >> 18) & 0x3F;
    const cc = (fcr31 >> 23) & 0x1;
    const fs = (fcr31 >> 24) & 0x1;
    const rmStr = ['RN (nearest)', 'RZ (zero)', 'RP (+inf)', 'RM (-inf)'][rm];

    html += `<div class="dbg-row"><span class="dbg-name">RM    </span><span class="dbg-val">${rmStr}</span></div>`;
    html += `<div class="dbg-row"><span class="dbg-name">Flags </span><span class="dbg-val">${flags.toString(2).padStart(5, '0')}</span><span class="dbg-dim" style="margin-left:6px">V Z O U I (sticky)</span></div>`;
    html += `<div class="dbg-row"><span class="dbg-name">Enable</span><span class="dbg-val">${ena.toString(2).padStart(5, '0')}</span><span class="dbg-dim" style="margin-left:6px">V Z O U I</span></div>`;
    html += `<div class="dbg-row"><span class="dbg-name">Cause </span><span class="dbg-val">${cause.toString(2).padStart(6, '0')}</span><span class="dbg-dim" style="margin-left:6px">E V Z O U I</span></div>`;
    html += `<div class="dbg-row"><span class="dbg-name">CC    </span><span class="dbg-val">${cc}</span></div>`;
    html += `<div class="dbg-row"><span class="dbg-name">FS    </span><span class="dbg-val">${fs}</span><span class="dbg-dim" style="margin-left:6px">flush-to-zero</span></div>`;
    return html;
  });

  // ═══════════════════════════════════════════════════════════════════════════
  // Computed HTML (VU0f)
  // ═══════════════════════════════════════════════════════════════════════════

  protected readonly vu0fHtml = computed(() => {
    const regs = this.registers();
    if (!regs) return '<div class="dbg-row">No register data</div>';

    let html = '<div class="dbg-section-title">VU0 Float Registers (vf0–vf31)</div>';
    const xs = regs.vu0fX ?? [], ys = regs.vu0fY ?? [];
    const zs = regs.vu0fZ ?? [], ws = regs.vu0fW ?? [];

    for (let i = 0; i < 32; i++) {
      const xb = (xs[i] ?? 0) >>> 0, yb = (ys[i] ?? 0) >>> 0;
      const zb = (zs[i] ?? 0) >>> 0, wb = (ws[i] ?? 0) >>> 0;
      const nz = xb !== 0 || yb !== 0 || zb !== 0 || wb !== 0;
      html += `<div class="dbg-row${nz ? ' dbg-nonzero' : ''}">
        <span class="dbg-name">vf${String(i).padStart(2, '0')}</span>
        <span class="dbg-dim" style="font-size:9px;margin-left:4px">x:${this.rawBitsToFloat(xb)} y:${this.rawBitsToFloat(yb)} z:${this.rawBitsToFloat(zb)} w:${this.rawBitsToFloat(wb)}</span>
      </div>`;
    }

    html += '<div class="dbg-divider"></div>';
    const axb = (regs.vu0AccX ?? 0) >>> 0, ayb = (regs.vu0AccY ?? 0) >>> 0;
    const azb = (regs.vu0AccZ ?? 0) >>> 0, awb = (regs.vu0AccW ?? 0) >>> 0;
    html += `<div class="dbg-row"><span class="dbg-name">ACC  </span><span class="dbg-dim" style="font-size:9px;margin-left:4px">x:${this.rawBitsToFloat(axb)} y:${this.rawBitsToFloat(ayb)} z:${this.rawBitsToFloat(azb)} w:${this.rawBitsToFloat(awb)}</span></div>`;
    html += `<div class="dbg-row"><span class="dbg-name">Q    </span><span class="dbg-val">${this.hex32((regs.vu0Q ?? 0) >>> 0)}</span><span class="dbg-dim" style="margin-left:6px">${this.rawBitsToFloat((regs.vu0Q ?? 0) >>> 0)}</span></div>`;
    html += `<div class="dbg-row"><span class="dbg-name">P    </span><span class="dbg-val">${this.hex32((regs.vu0P ?? 0) >>> 0)}</span><span class="dbg-dim" style="margin-left:6px">${this.rawBitsToFloat((regs.vu0P ?? 0) >>> 0)}</span></div>`;
    html += `<div class="dbg-row"><span class="dbg-name">I    </span><span class="dbg-val">${this.hex32((regs.vu0I ?? 0) >>> 0)}</span><span class="dbg-dim" style="margin-left:6px">${this.rawBitsToFloat((regs.vu0I ?? 0) >>> 0)}</span></div>`;
    html += '<div class="dbg-divider"></div>';
    html += `<div class="dbg-row"><span class="dbg-name">Status</span><span class="dbg-val">${this.hex32(regs.vu0Status ?? 0)}</span></div>`;
    html += `<div class="dbg-row"><span class="dbg-name">MAC   </span><span class="dbg-val">${((regs.vu0Mac ?? 0) >>> 0).toString(16).toUpperCase().padStart(4, '0')}</span></div>`;
    html += `<div class="dbg-row"><span class="dbg-name">Clip  </span><span class="dbg-val">${this.hex32(regs.vu0Clip ?? 0)}</span></div>`;
    return html;
  });

  // ═══════════════════════════════════════════════════════════════════════════
  // Computed HTML (VU0i)
  // ═══════════════════════════════════════════════════════════════════════════

  protected readonly vu0iHtml = computed(() => {
    const regs = this.registers();
    if (!regs) return '<div class="dbg-row">No register data</div>';

    const vi = regs.vu0i ?? [];
    let html = '<div class="dbg-section-title">VU0 Integer Registers (COP2_REG_CTL)</div>';

    for (let i = 0; i < 16; i++) {
      const val = (vi[i] ?? 0) & 0xFFFF;
      const nz = val !== 0;
      const ro = i === 0;
      const editAttr = ro ? '' : ` data-reg-type="VU0I" data-reg-idx="${i}"`;
      html += `<div class="dbg-row${nz ? ' dbg-nonzero' : ''}"${editAttr}><span class="dbg-name" style="font-size:10px">${COP2_REG_CTL[i].padEnd(8)}</span><span class="dbg-val${ro ? '' : ' dbg-editable'}" title="${ro ? '' : 'Double-click to edit'}">${val.toString(16).toUpperCase().padStart(4, '0')}</span><span class="dbg-dim" style="margin-left:6px">${val}</span></div>`;
    }

    html += '<div class="dbg-divider"></div>';
    html += '<div class="dbg-section-title">VU0 Control Registers</div>';

    for (let i = 16; i < 32; i++) {
      const raw = vi[i] ?? 0;
      const nz = raw !== 0;
      const name = COP2_REG_CTL[i];
      let valStr: string;
      let annot = '';
      if (i === 20 || i === 21 || i === 22) {
        valStr = this.hex32(raw);
        annot = `<span class="dbg-dim" style="margin-left:6px">${this.rawBitsToFloat(raw)}</span>`;
      } else if (i === 17) {
        valStr = (raw & 0xFFFF).toString(16).toUpperCase().padStart(4, '0');
      } else {
        valStr = this.hex32(raw);
      }
      html += `<div class="dbg-row${nz ? ' dbg-nonzero' : ''}"><span class="dbg-name" style="font-size:10px">${name.padEnd(8)}</span><span class="dbg-val">${valStr}</span>${annot}</div>`;
    }
    return html;
  });

  // ═══════════════════════════════════════════════════════════════════════════
  // Computed HTML (GS)
  // ═══════════════════════════════════════════════════════════════════════════

  protected readonly gsHtml = computed(() => {
    const regs = this.registers();
    if (!regs) return '<div class="dbg-row">No register data</div>';

    let html = '<div class="dbg-section-title">Graphics Synthesizer Registers</div>';
    const priv = regs.gsPriv ?? [];

    for (let i = 0; i < GS_PRIV_NAMES.length; i++) {
      const raw = priv[i] ?? 0;
      const lo = (raw & 0xFFFFFFFF) >>> 0;
      const hi = Math.floor(raw / 0x100000000) >>> 0;
      const valStr = hi !== 0 ? this.hex64(lo, hi) : this.hex32(lo);
      const nz = raw !== 0;
      const name = GS_PRIV_NAMES[i].padEnd(10);
      html += `<div class="dbg-row${nz ? ' dbg-nonzero' : ''}"><span class="dbg-name" style="font-size:10px">${name}</span><span class="dbg-val" style="font-size:10px">${valStr}</span></div>`;
    }
    return html;
  });

  // ═══════════════════════════════════════════════════════════════════════════
  // Computed HTML (Stack)
  // ═══════════════════════════════════════════════════════════════════════════

  protected readonly stackHtml = computed(() => {
    const regs = this.registers();
    if (!regs) return '<div class="dbg-row">No register data</div>';

    const sp = (regs.gpr ?? [])[29] ?? 0;
    let html = `<div class="dbg-section-title">Stack  SP=${this.hex32(sp)}</div>`;

    const stackBytes = this.stackBytes();
    const stackBase = this.stackBase();

    if (!stackBytes || stackBytes.length === 0) {
      html += '<div class="dbg-row dbg-dim">Stack data unavailable</div>';
      return html;
    }

    for (let i = 0; i < Math.min(stackBytes.length, 128); i += 4) {
      const addr = (stackBase + i) >>> 0;
      const word = this.readUInt32LE(stackBytes, i);
      const isCodeAddr = this.isCodePointer(word);
      const cls = isCodeAddr ? ' dbg-retaddr' : '';
      const annotation = isCodeAddr ? ' <span class="dbg-dim">← code ptr</span>' : '';
      html += `<div class="dbg-row${cls}"><span class="dbg-addr">${this.hex32(addr)}</span><span class="dbg-val">${this.hex32(word)}</span>${annotation}</div>`;
    }
    return html;
  });

  // ═══════════════════════════════════════════════════════════════════════════
  // Methods
  // ═══════════════════════════════════════════════════════════════════════════

  selectTab(tab: DebugTab): void {
    this.activeTab.set(tab);
  }

  /** Handle double-click to edit registers */
  protected onHostClick(event: MouseEvent): void {
    const target = event.target as HTMLElement;

    // Handle double-click on editable register values
    if (event.detail === 2) {
      const row = target.closest('[data-reg-type]') as HTMLElement | null;
      if (row) {
        const regType = row.dataset['regType'] ?? '';
        const regIdx = parseInt(row.dataset['regIdx'] ?? '0', 10);
        const valEl = row.querySelector('.dbg-editable') as HTMLElement | null;
        if (valEl) {
          this.editRegister.emit({
            regType,
            regIdx,
            currentValue: valEl.textContent ?? '',
          });
        }
      }
    }
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Utility Methods
  // ═══════════════════════════════════════════════════════════════════════════

  toHex(n: number, pad = 8): string {
    return (n >>> 0).toString(16).toUpperCase().padStart(pad, '0');
  }

  /** Parse the start address of a PineWatchInfo as a number (for navigation). */
  parseWatchStart(w: PineWatchInfo): number {
    return parseInt(w.Start, 16) || 0;
  }

  /** CSS class to colour-code the condition badge. */
  watchCondClass(cond: string): string {
    if (cond === 'read')       return 'watch-cond-read';
    if (cond === 'write')      return 'watch-cond-write';
    return 'watch-cond-rw';
  }

  private hex32(n: number): string {
    return (n >>> 0).toString(16).toUpperCase().padStart(8, '0');
  }

  private hex64(lo: number, hi: number): string {
    return (hi >>> 0).toString(16).toUpperCase().padStart(8, '0') + '_' +
           (lo >>> 0).toString(16).toUpperCase().padStart(8, '0');
  }

  private rawBitsToFloat(bits: number): string {
    const buf = new ArrayBuffer(4);
    new Uint32Array(buf)[0] = bits >>> 0;
    const f = new DataView(buf).getFloat32(0, true);
    return f.toPrecision(6).replace(/\.?0+$/, '');
  }

  private readUInt32LE(bytes: number[], offset: number): number {
    return ((bytes[offset + 3] << 24) | (bytes[offset + 2] << 16) |
            (bytes[offset + 1] << 8) | bytes[offset]) >>> 0;
  }

  private isCodePointer(val: number): boolean {
    const phys = this.toPhysAddr(val);
    return phys >= this.codeStart() && phys < this.codeEnd();
  }

  private toPhysAddr(v: number): number {
    if (v >= 0x80000000 && v < 0xC0000000) return v - 0x80000000;
    if (v >= 0xA0000000 && v < 0xC0000000) return v - 0xA0000000;
    return v;
  }

  private gprCatClass(i: number): string {
    if (i === 0) return 'dbg-reg-zero';
    if (i === 1) return 'dbg-reg-at';
    if (i >= 2 && i <= 3) return 'dbg-reg-v';
    if (i >= 4 && i <= 7) return 'dbg-reg-a';
    if ((i >= 8 && i <= 15) || i === 24 || i === 25) return 'dbg-reg-t';
    if (i >= 16 && i <= 23) return 'dbg-reg-s';
    if (i === 26 || i === 27) return 'dbg-reg-k';
    return 'dbg-reg-special';
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // Resize logic
  // ═══════════════════════════════════════════════════════════════════════════

  private _resizeCleanup: (() => void) | null = null;

  ngOnDestroy(): void {
    this._resizeCleanup?.();
  }

  onResizeMouseDown(event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();
    const startX = event.clientX;
    const startWidth = this.panelWidth();
    document.body.style.cursor = 'ew-resize';
    document.body.style.userSelect = 'none';

    const onMouseMove = (e: MouseEvent) => {
      // Moving left (negative delta) increases width; moving right decreases it.
      const newWidth = Math.max(this.MIN_WIDTH, Math.min(this.MAX_WIDTH, startWidth + (startX - e.clientX)));
      this.panelWidth.set(newWidth);
    };

    const onMouseUp = () => {
      cleanup();
      localStorage.setItem(this.STORAGE_KEY, String(this.panelWidth()));
    };

    const cleanup = () => {
      document.removeEventListener('mousemove', onMouseMove);
      document.removeEventListener('mouseup', onMouseUp);
      document.body.style.cursor = '';
      document.body.style.userSelect = '';
      this._resizeCleanup = null;
    };

    this._resizeCleanup = cleanup;
    document.addEventListener('mousemove', onMouseMove);
    document.addEventListener('mouseup', onMouseUp);
  }

  private _loadWidth(): number {
    try {
      const saved = localStorage.getItem(this.STORAGE_KEY);
      if (saved) {
        const w = parseInt(saved, 10);
        if (!isNaN(w)) return Math.max(this.MIN_WIDTH, Math.min(this.MAX_WIDTH, w));
      }
    } catch { /* ignore */ }
    return 320;
  }
}
