import { Component, ViewEncapsulation, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import { IconComponent } from '../shared/icon.component';
import '../shared/styles/shared.css';
import './xrefs-view.css';

interface XrefRow {
  address: string;
  addressValue: number;
  kind: string;
  kindLabel: string;
  instruction: string;
  summary: string;
  detail?: string;
}

const KINDS: ReadonlyArray<{ id: string; label: string; title: string }> = [
  { id: 'controlFlow',   label: 'Calls / jumps', title: 'J / JAL / branches to the address' },
  { id: 'resolved',      label: 'Loads',         title: 'LUI + ADDIU / ORI / load / store pairs that build the address' },
  { id: 'pointer',       label: 'Pointers',      title: 'Data words holding the address' },
  { id: 'stringPattern', label: 'Strings',       title: 'String references' },
];

/**
 * Cross-references to the disassembler's cursor address, as a dockable view.
 */
@Component({
  selector: 'app-xrefs-view',
  standalone: true,
  imports: [IconComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="xr-shell">
  <div class="xr-header">
    <span class="xr-target lm-mono">{{ targetText() }}</span>
    @if (label()) { <span class="xr-label">{{ label() }}</span> }
    <span class="xr-spacer"></span>
    <span class="xr-count lm-muted">{{ countText() }}</span>
    <button class="lm-tool-btn" [class.xr-active]="pinned()"
      [title]="pinned() ? 'Pinned — click to follow the disassembler cursor again' : 'Following the cursor — click to pin this address'"
      (click)="togglePinned()">{{ pinned() ? 'Pinned' : 'Follow' }}</button>
    <button class="lm-tool-btn" title="Refresh" (click)="refresh()"><app-icon name="refreshcw" [size]="12" /></button>
  </div>
  <div class="xr-kinds">
    @for (k of kindDefs; track k.id) {
      <button class="xr-chip" [class.xr-active]="kinds().includes(k.id)" [title]="k.title" (click)="toggleKind(k.id)">{{ k.label }}</button>
    }
  </div>
  @if (error()) {
    <div class="xr-empty xr-error">{{ error() }}</div>
  } @else if (address() === null) {
    <div class="xr-empty lm-muted">Move the cursor in a memory view to list its cross-references.</div>
  } @else if (loading() && !rows().length) {
    <div class="xr-empty lm-muted">Looking up references…</div>
  } @else if (!rows().length) {
    <div class="xr-empty lm-muted">No references found.</div>
  } @else {
    <div class="xr-list">
      @for (r of rows(); track r.address + r.kind) {
        <div class="xr-row" (click)="navigate(r)" [title]="r.detail || r.summary">
          <span class="xr-addr lm-mono">0x{{ r.address }}</span>
          <span class="xr-kind">{{ r.kindLabel || r.kind }}</span>
          <span class="xr-instr">{{ r.instruction || r.summary }}</span>
        </div>
      }
    </div>
  }
</div>
  `,
})
export class XrefsViewComponent implements OnInit, OnDestroy {
  private readonly vscode = getVsCodeApi();
  protected readonly kindDefs = KINDS;

  protected readonly address = signal<number | null>(null);
  protected readonly label = signal<string>('');
  protected readonly rows = signal<XrefRow[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly pinned = signal(false);
  protected readonly kinds = signal<string[]>(KINDS.map(k => k.id));

  protected readonly targetText = computed(() => {
    const a = this.address();
    return a === null ? 'Cross-references' : `Refs to 0x${(a >>> 0).toString(16).toUpperCase().padStart(8, '0')}`;
  });

  protected readonly countText = computed(() =>
    this.loading() ? '…' : this.address() === null ? '' : `${this.rows().length}`);

  private readonly onMessage = (e: MessageEvent) => {
    const msg = (e.data ?? {}) as Record<string, unknown>;
    switch (msg['type']) {
      case 'state':
        this.pinned.set(!!msg['pinned']);
        if (Array.isArray(msg['kinds'])) { this.kinds.set(msg['kinds'] as string[]); }
        break;
      case 'loading':
        this.loading.set(true);
        if (msg['address'] !== this.address()) { this.rows.set([]); }
        this.address.set(msg['address'] as number);
        this.label.set((msg['label'] as string | undefined) ?? '');
        this.error.set('');
        break;
      case 'xrefs':
        this.loading.set(false);
        this.address.set(msg['address'] as number);
        this.label.set((msg['label'] as string | undefined) ?? '');
        this.rows.set((msg['refs'] as XrefRow[] | undefined) ?? []);
        this.error.set((msg['error'] as string | undefined) ?? '');
        break;
    }
  };

  ngOnInit(): void {
    window.addEventListener('message', this.onMessage);
    this.vscode.postMessage({ type: 'ready' });
  }

  ngOnDestroy(): void {
    window.removeEventListener('message', this.onMessage);
  }

  protected navigate(r: XrefRow): void {
    const address = Number.isFinite(r.addressValue) ? r.addressValue : parseInt(r.address, 16);
    this.vscode.postMessage({ type: 'navigate', address });
  }

  protected togglePinned(): void {
    this.pinned.update(p => !p);
    this.vscode.postMessage({ type: 'setPinned', pinned: this.pinned() });
  }

  protected toggleKind(id: string): void {
    const next = this.kinds().includes(id) ? this.kinds().filter(k => k !== id) : [...this.kinds(), id];
    if (!next.length) { return; }
    this.kinds.set(next);
    this.vscode.postMessage({ type: 'setKinds', kinds: next });
  }

  protected refresh(): void {
    this.vscode.postMessage({ type: 'refresh' });
  }
}
