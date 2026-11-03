import {
  Component,
  AfterViewInit,
  ViewEncapsulation,
  signal,
  computed,
  NgZone,
  inject,
} from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import { IconComponent } from '../shared/icon.component';
import { PageHeaderComponent } from '../shared/page-header.component';
import { ConfirmDialogComponent } from '../shared/components/index';
import { DebugCommand, buildCommandLine } from './types';
import '../shared/styles/shared.css';
import './debug-commands-page.css';

interface CommandGroup { name: string; commands: DebugCommand[]; }

@Component({
  selector: 'app-debug-commands-page',
  standalone: true,
  imports: [IconComponent, PageHeaderComponent, ConfirmDialogComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="dc-shell">
  <app-page-header icon="bug" title="Debug" subtitle="Scripts and commands from Mods/debug-commands.json" />

  <div class="dc-toolbar">
    <div class="dc-title">
      <app-icon name="terminal" [size]="16" />
      <span>Commands</span>
    </div>
    <label class="dc-search">
      <app-icon name="search" [size]="13" />
      <input type="text" placeholder="Filter commands…" [value]="filterText()" (input)="onFilterInput($event)" />
    </label>
    <div class="dc-toolbar-spacer"></div>
    <span class="dc-count">{{ commands().length }} command{{ commands().length === 1 ? '' : 's' }}</span>
    <button class="dc-btn" (click)="onOpenJson()" title="Open debug-commands.json in the editor">
      <app-icon name="filecode" [size]="14" /> JSON
    </button>
    <button class="dc-btn" (click)="onRefresh()" title="Reload from disk">
      <app-icon name="refreshcw" [size]="14" /> Refresh
    </button>
    <button class="dc-btn dc-primary" (click)="onAdd()" title="Add a command">
      <app-icon name="plus" [size]="14" /> Add
    </button>
  </div>

  @if (statusMsg()) {
    <div class="dc-status" [class.dc-status-error]="statusIsError()">{{ statusMsg() }}</div>
  }

  <div class="dc-main">
    @if (commands().length === 0) {
      <div class="dc-empty">
        <app-icon name="bug" [size]="34" />
        <span>No commands yet. Press <strong>Add</strong> to create one.</span>
        @if (filePath()) { <span class="dc-empty-path">{{ filePath() }}</span> }
      </div>
    } @else if (groups().length === 0) {
      <div class="dc-empty"><span>No commands match the filter.</span></div>
    } @else {
      @for (g of groups(); track g.name) {
        <section class="dc-group">
          <h2 class="dc-group-title">{{ g.name }}</h2>
          @for (c of g.commands; track c.id) {
            <div class="dc-card">
              <div class="dc-card-head">
                <div class="dc-card-copy">
                  <div class="dc-card-name">{{ c.name }}</div>
                  @if (c.description) { <div class="dc-card-desc">{{ c.description }}</div> }
                </div>
                <div class="dc-card-actions">
                  @if (c.readme) {
                    <button class="dc-icon-btn" (click)="onHelp(c)" [title]="'Help: ' + c.readme">
                      <app-icon name="help" [size]="14" />
                    </button>
                  }
                  <button class="dc-icon-btn" (click)="onEdit(c)" title="Edit">
                    <app-icon name="edit" [size]="14" />
                  </button>
                  <button class="dc-icon-btn dc-danger" (click)="pendingDelete.set(c)" title="Delete">
                    <app-icon name="trash2" [size]="14" />
                  </button>
                </div>
              </div>
              <div class="dc-code">
                <div class="dc-code-bar">
                  <span class="dc-code-lang">{{ shellLabel(c) }}</span>
                  @if (c.script) { <span class="dc-code-script" [title]="c.script">{{ c.script }}</span> }
                  <span class="dc-code-spacer"></span>
                  <button class="dc-code-btn" (click)="onCopy(c)" [title]="'Copy command'">
                    <app-icon [name]="copiedId() === c.id ? 'check' : 'copy'" [size]="13" />
                    <span>{{ copiedId() === c.id ? 'Copied' : 'Copy' }}</span>
                  </button>
                  <button class="dc-code-btn dc-run" (click)="onRun(c)" title="Run in a new terminal">
                    <app-icon name="play" [size]="13" />
                    <span>Run</span>
                  </button>
                </div>
                <pre class="dc-code-line">{{ commandLine(c) }}</pre>
                <div class="dc-code-foot">cwd: {{ cwdLabel(c) }}</div>
              </div>
            </div>
          }
        </section>
      }
    }
  </div>

  @if (editing(); as e) {
    <div class="dc-modal-backdrop" (click)="onCancelEdit()">
      <div class="dc-modal" (click)="$event.stopPropagation()">
        <div class="dc-modal-title">
          <app-icon [name]="isNew() ? 'plus' : 'edit'" [size]="15" />
          {{ isNew() ? 'Add command' : 'Edit command' }}
        </div>
        <div class="dc-form">
          <label>Name<input type="text" [value]="e.name" (input)="setField('name', $event)" placeholder="Add bots" /></label>
          <label>Group<input type="text" [value]="e.group" (input)="setField('group', $event)" placeholder="SOCOM II · Bots" list="dc-groups" /></label>
          <datalist id="dc-groups">
            @for (g of groupNames(); track g) { <option [value]="g"></option> }
          </datalist>
          <label class="dc-wide">Description<input type="text" [value]="e.description ?? ''" (input)="setField('description', $event)" /></label>
          <label>Interpreter<input type="text" [value]="e.interpreter ?? ''" (input)="setField('interpreter', $event)" placeholder="python (empty = plain command)" /></label>
          <label>Script<input type="text" [value]="e.script ?? ''" (input)="setField('script', $event)" placeholder="SOCOM2/bots/socom_bots.py" /></label>
          <label class="dc-wide">{{ e.script ? 'Arguments' : 'Command' }}<input type="text" class="dc-mono" [value]="e.args ?? ''" (input)="setField('args', $event)" placeholder="add 1 23" /></label>
          <label class="dc-wide">Working directory (relative to Mods)<input type="text" [value]="e.cwd ?? ''" (input)="setField('cwd', $event)" placeholder="empty = Mods,  .. = repo root" /></label>
          <label class="dc-wide">Help (Markdown file relative to Mods, optional #heading)<input type="text" [value]="e.readme ?? ''" (input)="setField('readme', $event)" placeholder="SOCOMCA/README.md#props" /></label>
        </div>
        <div class="dc-preview">
          <span class="dc-preview-label">Command</span>
          <pre class="dc-code-line">{{ commandLine(e) || '—' }}</pre>
        </div>
        <div class="dc-modal-actions">
          <button class="dc-btn" (click)="onCancelEdit()">Cancel</button>
          <button class="dc-btn dc-primary" (click)="onSaveEdit()" [disabled]="!e.name.trim() || !commandLine(e)">
            <app-icon name="save" [size]="14" /> Save
          </button>
        </div>
      </div>
    </div>
  }

  <app-confirm-dialog
    [visible]="!!pendingDelete()"
    [title]="'Delete command?'"
    [body]="pendingDelete()?.name ?? ''"
    [note]="'Removes it from debug-commands.json. The script itself is not touched.'"
    [confirmLabel]="'Delete'"
    (confirmed)="onConfirmDelete()"
    (cancelled)="pendingDelete.set(null)">
  </app-confirm-dialog>
</div>
`,
})
export class DebugCommandsPageComponent implements AfterViewInit {
  private readonly vscode = getVsCodeApi();
  private readonly zone   = inject(NgZone);

  readonly commands      = signal<DebugCommand[]>([]);
  readonly filterText    = signal('');
  readonly filePath      = signal('');
  readonly modsDir       = signal('');
  readonly statusMsg     = signal('');
  readonly statusIsError = signal(false);
  readonly editing       = signal<DebugCommand | null>(null);
  readonly isNew         = signal(false);
  readonly pendingDelete = signal<DebugCommand | null>(null);
  readonly copiedId      = signal<string | null>(null);
  private copiedTimer: ReturnType<typeof setTimeout> | undefined;

  readonly groupNames = computed(() => [...new Set(this.commands().map(c => c.group || 'General'))]);

  readonly groups = computed<CommandGroup[]>(() => {
    const q = this.filterText().trim().toLowerCase();
    const out = new Map<string, DebugCommand[]>();
    for (const c of this.commands()) {
      const hay = `${c.name} ${c.group} ${c.description ?? ''} ${buildCommandLine(c)} ${c.readme ?? ''}`.toLowerCase();
      if (q && !hay.includes(q)) { continue; }
      const g = c.group || 'General';
      if (!out.has(g)) { out.set(g, []); }
      out.get(g)!.push(c);
    }
    return [...out].map(([name, commands]) => ({ name, commands }));
  });

  ngAfterViewInit(): void {
    window.addEventListener('message', (event: MessageEvent) => {
      const data = event.data as { type: string; [key: string]: unknown };
      this.zone.run(() => this._handleMessage(data));
    });
    this.vscode.postMessage({ type: 'ready' });
  }

  // ── Template helpers ────────────────────────────────────────────────────

  commandLine(c: DebugCommand): string { return buildCommandLine(c); }

  shellLabel(c: DebugCommand): string { return c.interpreter?.trim() || 'shell'; }

  cwdLabel(c: DebugCommand): string {
    const sub = (c.cwd ?? '').trim();
    if (!sub) { return 'Mods'; }
    return sub === '..' ? 'repo root' : `Mods/${sub}`;
  }

  // ── UI handlers ─────────────────────────────────────────────────────────

  onFilterInput(evt: Event): void { this.filterText.set((evt.target as HTMLInputElement).value); }
  onRefresh(): void { this.vscode.postMessage({ type: 'refresh' }); }
  onOpenJson(): void { this.vscode.postMessage({ type: 'openJson' }); }

  onRun(c: DebugCommand): void { this.vscode.postMessage({ type: 'run', id: c.id }); }
  onHelp(c: DebugCommand): void { this.vscode.postMessage({ type: 'help', id: c.id }); }

  onCopy(c: DebugCommand): void {
    this.vscode.postMessage({ type: 'copy', text: buildCommandLine(c) });
    this.copiedId.set(c.id);
    clearTimeout(this.copiedTimer);
    this.copiedTimer = setTimeout(() => this.copiedId.set(null), 1500);
  }

  onAdd(): void {
    this.isNew.set(true);
    this.editing.set({
      id: '', name: '', group: this.groupNames()[0] ?? 'General', description: '',
      interpreter: 'python', script: '', args: '', cwd: '',
    });
  }

  onEdit(c: DebugCommand): void {
    this.isNew.set(false);
    this.editing.set({ ...c });
  }

  setField(field: keyof DebugCommand, evt: Event): void {
    const value = (evt.target as HTMLInputElement).value;
    const e = this.editing();
    if (e) { this.editing.set({ ...e, [field]: value }); }
  }

  onCancelEdit(): void { this.editing.set(null); }

  onSaveEdit(): void {
    const e = this.editing();
    if (!e || !e.name.trim()) { return; }
    const cmd: DebugCommand = {
      ...e,
      id: e.id || this._newId(e.name),
      name: e.name.trim(),
      group: e.group.trim() || 'General',
    };
    this.vscode.postMessage({ type: 'save', command: cmd });
    this.editing.set(null);
  }

  onConfirmDelete(): void {
    const c = this.pendingDelete();
    this.pendingDelete.set(null);
    if (c) { this.vscode.postMessage({ type: 'delete', id: c.id }); }
  }

  // ── Messages ────────────────────────────────────────────────────────────

  private _handleMessage(data: { type: string; [key: string]: unknown }): void {
    switch (data['type']) {
      case 'debugCommandList':
        this.commands.set((data['commands'] as DebugCommand[] | undefined) ?? []);
        this.filePath.set((data['filePath'] as string) ?? '');
        this.modsDir.set((data['modsDir'] as string) ?? '');
        this._showStatus((data['message'] as string) ?? '', false);
        break;
      case 'debugCommandStatus':
        this._showStatus((data['message'] as string) ?? '', false);
        break;
      case 'debugCommandError':
        this._showStatus((data['message'] as string) ?? 'An error occurred.', true);
        break;
    }
  }

  private _showStatus(msg: string, isError: boolean): void {
    this.statusMsg.set(msg);
    this.statusIsError.set(isError);
  }

  private _newId(name: string): string {
    const base = name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'command';
    const taken = new Set(this.commands().map(c => c.id));
    let id = base;
    for (let n = 2; taken.has(id); n++) { id = `${base}-${n}`; }
    return id;
  }
}
