import { CommonModule } from '@angular/common';
import {
  AfterViewInit,
  Component,
  ElementRef,
  NgZone,
  OnDestroy,
  ViewChild,
  ViewEncapsulation,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { getVsCodeApi } from '../shared/vscode-api';
import { colorizeRegisters } from '../disasm-editor/utils/registers.utils';
import './chat-panel.component.css';

// ── Block contract (mirrors docs/ai-chat-templates.md §3) ─────────────────────
//
// The renderer accepts a strict union of block kinds. Anything else is dropped
// with a console warning so the AI can't accidentally ship un-renderable HTML.

export type ChatStatus = 'streaming' | 'done' | 'error';
export type StepStatus = 'queued' | 'running' | 'done' | 'error' | 'skipped';

export interface Pill { kind: 'pill'; text: string; tone?: 'ok' | 'warn' | 'danger' | 'info' | 'muted'; }

export type Block =
  | { kind: 'text'; md: string }
  | { kind: 'dispatch'; title?: string; rows: { n?: number; agent?: { id?: string; display: string }; task: string; status: StepStatus }[] }
  | {
      kind: 'tool-call';
      tool: string;
      args?: Record<string, unknown>;
      status: StepStatus;
      summary?: string;
      durationMs?: number;
      body?: Block;
    }
  | {
      kind: 'table';
      columns: { key: string; label?: string; align?: 'left' | 'right' | 'center' }[];
      rows: Record<string, string | number | Pill | null | undefined>[];
    }
  | {
      kind: 'disasm';
      header?: string;
      lines: { addr: string; op: string; ops?: string; note?: string; tag?: string; highlight?: boolean }[];
    }
  | {
      kind: 'struct-map';
      base?: string;
      addr?: string;
      summary?: string;
      fields: { offset: string; type: string; name: string; value?: string }[];
    }
  | { kind: 'code'; language: string; filename?: string; source: string; actions?: string[] }
  | { kind: 'kv'; items: { k: string; v: string }[] }
  | {
      kind: 'timeline';
      rows: { t: string; status: StepStatus; label: string; tag?: Pill }[];
    }
  | {
      kind: 'actions';
      chips: { id: string; label: string; command?: string }[];
    }
  | {
      kind: 'handoff';
      label: { text: string };
      to?: { id?: string; display: string };
      note?: string;
    };

// ── Wire DTOs (must mirror SocketClient types) ───────────────────────────────

export interface ChatSession {
  id: number;
  title: string;
  orchestrationId?: number;
  orchestrationName?: string;
  gameSerial?: string;
  gameTag?: string;
  messageCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface ChatMessage {
  id: number;
  sessionId: number;
  kind: 'user' | 'agent' | 'system';
  agentId?: number;
  agentDisplay?: string;
  role?: string;
  blocksJson: string;
  tokens?: number;
  latencyMs?: number;
  status: ChatStatus;
  createdAt: string;
  updatedAt: string;
  // Cached parse of blocksJson — populated by parseBlocks().
  _blocks?: Block[];
}

interface AgentRef { id: number; name: string; imageUri?: string; }
interface OrchestrationRef { id: number; name: string; isActive?: boolean; }
interface AiProviderOption { id: string; label: string; model: string; badge: string; description: string; }

@Component({
  selector: 'app-chat-panel',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  imports: [CommonModule],
  template: `
<div class="chat-shell">
  <!-- Sidebar: one entry per persistent chat session. -->
  <aside class="chat-sidebar">
    <header class="chat-sidebar-head">
      <div><p class="aic-eyebrow">Conversations</p><h2>Chat</h2></div>
      <button class="aic-icon" title="Refresh" (click)="loadSessions()">↻</button>
    </header>
    <div class="aic-action-row">
      <button class="aic-btn primary" (click)="createSession()">+ New chat</button>
    </div>
    <div class="chat-session-list">
      @for (s of sessions(); track s.id) {
        <button [class.selected]="activeSessionId() === s.id" (click)="selectSession(s)">
          <span class="chat-session-title">{{ s.title }}</span>
          <span class="chat-session-sub">
            {{ s.orchestrationName || 'No squad' }}
            <em>· {{ s.messageCount }} msg</em>
          </span>
          <small class="chat-session-time">{{ formatTime(s.updatedAt) }}</small>
        </button>
      } @empty {
        <p class="aic-empty">No conversations yet. Start a new chat to begin.</p>
      }
    </div>
  </aside>

  <!-- Main thread + composer. -->
  <section class="chat-main">
    @if (activeSession(); as session) {
      <header class="chat-thread-head">
        <div class="chat-thread-title">
          <span class="chat-app-name">CodeDesigner AI</span>
          @if (session.orchestrationName) {
            <span class="chat-team-badge">{{ session.orchestrationName }}</span>
          }
        </div>
        <div class="chat-thread-actions">
          <button class="aic-btn" (click)="renameSession()">Rename</button>
          <button class="aic-btn" (click)="clearMessages()">Clear</button>
          <button class="aic-btn danger" (click)="deleteSession()">Delete</button>
        </div>
      </header>

      <!-- Scrollable thread. -->
      <div #scrollEl class="chat-thread-scroll">
        @if (messages().length === 0) {
          <div class="chat-empty-thread">
            <h3>No messages yet</h3>
            <p>Send a prompt below to dispatch the team.</p>
          </div>
        }
        @for (m of messages(); track m.id) {
          <article class="chat-msg" [class]="'chat-msg-' + m.kind" [class.is-streaming]="m.status === 'streaming'" [class.is-error]="m.status === 'error'">
            @if (m.kind === 'user') {
              <div class="chat-msg-user-bubble">
                @for (b of blocksFor(m); track $index) {
                  <ng-container *ngTemplateOutlet="blockTpl; context: { $implicit: b }"></ng-container>
                }
              </div>
              <div class="chat-msg-avatar chat-msg-user-avatar" title="You" aria-label="User">
                <span class="chat-user-icon" aria-hidden="true">👤</span>
              </div>
            } @else {
              <div class="chat-msg-avatar" [title]="m.agentDisplay || ''">
                @if (avatarFor(m); as uri) { <img [src]="uri" [alt]="m.agentDisplay || ''"> }
                @else { <span>{{ initials(m.agentDisplay) }}</span> }
              </div>
              <div class="chat-msg-body">
                <header class="chat-msg-head">
                  <strong>{{ m.agentDisplay || 'Agent' }}</strong>
                  @if (m.role) { <span class="chat-role-badge">{{ m.role }}</span> }
                  <span class="chat-msg-meta">
                    @if (m.tokens != null) { <span>{{ m.tokens }} tok</span> }
                    @if (m.latencyMs != null) { <span>· {{ formatLatency(m.latencyMs) }}</span> }
                    @if (m.status === 'streaming') { <span class="chat-running">···</span> }
                  </span>
                </header>
                @for (b of blocksFor(m); track $index) {
                  <ng-container *ngTemplateOutlet="blockTpl; context: { $implicit: b }"></ng-container>
                }
                <footer class="chat-msg-actions">
                  <button (click)="copyMessage(m)" title="Copy">⎘ Copy</button>
                </footer>
              </div>
            }
          </article>
        }
      </div>

      <!-- Composer. -->
      <footer class="chat-composer" [class.is-collapsed]="composerCollapsed()">
        <div class="chat-composer-top">
          <label class="chat-model-select" [title]="selectedProvider().description">
            <span>Model</span>
            <select [value]="selectedProviderId()" (change)="setProvider($any($event.target).value)">
              @for (provider of providerOptions; track provider.id) {
                <option [value]="provider.id">{{ provider.label }} — {{ provider.model }}</option>
              }
            </select>
          </label>
          <button class="chat-context-button" type="button" [title]="contextTitle(session)" (click)="showContext(session)">
            <span>Context</span><strong>{{ contextCount(session) }}</strong>
          </button>
          <span class="chat-model-summary">{{ selectedProvider().model }}</span>
          <button class="chat-collapse-btn" type="button" (click)="toggleComposer()">
            {{ composerCollapsed() ? 'Expand composer' : 'Collapse' }}
          </button>
        </div>
        @if (!composerCollapsed()) {
          <textarea
            #composerEl
            rows="3"
            [value]="draft()"
            (input)="draft.set($any($event.target).value)"
            (keydown.enter)="onComposerEnter($event)"
            placeholder="Message {{ selectedProvider().label }}… (Enter to send, Shift+Enter for newline)"
            [disabled]="sending()"></textarea>
          <div class="chat-composer-row">
            <span class="chat-composer-hint">{{ status() }}</span>
            <div class="chat-followups">
              @for (chip of followups(); track chip) {
                <button type="button" (click)="useFollowup(chip)">{{ chip }}</button>
              }
            </div>
            <button class="aic-btn primary" [disabled]="!draft().trim() || sending()" (click)="send()">
              {{ sending() ? 'Sending…' : 'Send →' }}
            </button>
          </div>
        } @else {
          <button class="chat-collapsed-prompt" type="button" (click)="toggleComposer()">
            Message {{ selectedProvider().label }}…
          </button>
        }
      </footer>
    } @else {
      <div class="aic-blank">
        <b>✦</b>
        <h3>Pick a conversation</h3>
        <p>Select a chat from the sidebar, or start a new one to dispatch the orchestrator.</p>
      </div>
    }
  </section>
</div>

<!-- ── Block templates ──────────────────────────────────────────────────────── -->

<ng-template #blockTpl let-b>
  @switch (b?.kind) {
    @case ('text')      { <p class="cb-text" [innerHTML]="renderInline(b.md)"></p> }
    @case ('dispatch')  { <ng-container *ngTemplateOutlet="dispatchTpl;  context: { $implicit: b }" /> }
    @case ('tool-call') { <ng-container *ngTemplateOutlet="toolCallTpl;  context: { $implicit: b }" /> }
    @case ('table')     { <ng-container *ngTemplateOutlet="tableTpl;     context: { $implicit: b }" /> }
    @case ('disasm')    { <ng-container *ngTemplateOutlet="disasmTpl;    context: { $implicit: b }" /> }
    @case ('struct-map'){ <ng-container *ngTemplateOutlet="structTpl;    context: { $implicit: b }" /> }
    @case ('code')      { <ng-container *ngTemplateOutlet="codeTpl;      context: { $implicit: b }" /> }
    @case ('kv')        { <ng-container *ngTemplateOutlet="kvTpl;        context: { $implicit: b }" /> }
    @case ('timeline')  { <ng-container *ngTemplateOutlet="timelineTpl;  context: { $implicit: b }" /> }
    @case ('actions')   { <ng-container *ngTemplateOutlet="actionsTpl;   context: { $implicit: b }" /> }
    @case ('handoff')   { <ng-container *ngTemplateOutlet="handoffTpl;   context: { $implicit: b }" /> }
    @default            { <p class="cb-unknown">[unsupported block: {{ b?.kind }}]</p> }
  }
</ng-template>

<ng-template #dispatchTpl let-b>
  <div class="cb-card cb-dispatch">
    <header>{{ b.title || 'Dispatch' }}</header>
    <ol>
      @for (row of b.rows; track $index) {
        <li>
          <span class="cb-n">{{ row.n != null ? padNum(row.n) : (($index + 1) | number:'2.0') }}</span>
          <span class="cb-agent">{{ row.agent?.display || 'agent' }}</span>
          <span class="cb-task">{{ row.task }}</span>
          <span class="cb-status" [attr.data-status]="row.status">{{ row.status }}</span>
        </li>
      }
    </ol>
  </div>
</ng-template>

<ng-template #toolCallTpl let-b>
  <div class="cb-card cb-tool" [class.is-running]="b.status === 'running'" [class.is-error]="b.status === 'error'">
    <header>
      <span class="cb-tool-dot" [attr.data-status]="b.status"></span>
      <code class="cb-tool-sig">{{ b.tool }}{{ formatArgs(b.args) }}</code>
      <span class="cb-tool-sum">{{ b.summary || '' }}</span>
      <span class="cb-tool-dur">{{ b.durationMs != null ? formatLatency(b.durationMs) : '' }}</span>
    </header>
    @if (b.body) {
      <div class="cb-tool-body">
        <ng-container *ngTemplateOutlet="blockTpl; context: { $implicit: b.body }"></ng-container>
      </div>
    }
  </div>
</ng-template>

<ng-template #tableTpl let-b>
  <table class="cb-table">
    @if (anyColumnLabels(b.columns)) {
      <thead><tr>@for (c of b.columns; track c.key) { <th [style.text-align]="c.align || 'left'">{{ c.label || '' }}</th> }</tr></thead>
    }
    <tbody>
      @for (row of b.rows; track $index) {
        <tr>
          @for (c of b.columns; track c.key) {
            <td [style.text-align]="c.align || 'left'">
              @if (isPill(row[c.key])) {
                <span class="cb-pill" [attr.data-tone]="$any(row[c.key]).tone || 'info'">{{ $any(row[c.key]).text }}</span>
              } @else {
                {{ row[c.key] }}
              }
            </td>
          }
        </tr>
      }
    </tbody>
  </table>
</ng-template>

<ng-template #disasmTpl let-b>
  <div class="cb-disasm">
    @if (b.header) { <header>; {{ b.header }}</header> }
    <pre [innerHTML]="renderDisasmBlock(b)"></pre>
  </div>
</ng-template>

<ng-template #structTpl let-b>
  <div class="cb-struct">
    @if (b.base || b.addr) { <header>{{ b.base || 'struct' }}{{ b.addr ? '  @ ' + b.addr : '' }}</header> }
    <table>
      <thead><tr><th>OFFSET</th><th>TYPE</th><th>NAME</th><th>VALUE</th></tr></thead>
      <tbody>@for (f of b.fields; track f.offset + ':' + f.name) {
        <tr><td>{{ f.offset }}</td><td>{{ f.type }}</td><td>{{ f.name }}</td><td>{{ f.value || '' }}</td></tr>
      }</tbody>
    </table>
    @if (b.summary) { <footer>{{ b.summary }}</footer> }
  </div>
</ng-template>

<ng-template #codeTpl let-b>
  <div class="cb-code">
    <header>
      <span class="cb-code-lang">{{ b.language }}</span>
      @if (b.filename) { <span class="cb-code-file">{{ b.filename }}</span> }
      <span class="cb-code-actions">
        @for (action of (b.actions || ['copy']); track action) {
          <button (click)="onCodeAction(b, action)">{{ action }}</button>
        }
      </span>
    </header>
    <pre><code [innerHTML]="renderCodeSource(b)"></code></pre>
  </div>
</ng-template>

<ng-template #kvTpl let-b>
  <div class="cb-kv">@for (it of b.items; track it.k) { <span><strong>{{ it.k }}</strong> {{ it.v }}</span> }</div>
</ng-template>

<ng-template #timelineTpl let-b>
  <ol class="cb-timeline">
    @for (row of b.rows; track $index) {
      <li>
        <span class="cb-t-time">{{ row.t }}</span>
        <span class="cb-t-dot" [attr.data-status]="row.status"></span>
        <span class="cb-t-label" [innerHTML]="renderInline(row.label)"></span>
        @if (row.tag) { <span class="cb-pill" [attr.data-tone]="row.tag.tone || 'info'">{{ row.tag.text }}</span> }
      </li>
    }
  </ol>
</ng-template>

<ng-template #actionsTpl let-b>
  <div class="cb-actions">
    @for (chip of b.chips; track chip.id) {
      <button (click)="onActionChip(chip)">{{ chip.label }}</button>
    }
  </div>
</ng-template>

<ng-template #handoffTpl let-b>
  <p class="cb-handoff">
    @if (b.note) { {{ b.note }} }
    <span class="cb-pill" data-tone="info">{{ b.label?.text }}</span>
    @if (b.to) { <span class="cb-handoff-to">→ {{ b.to.display }}</span> }
  </p>
</ng-template>
  `,
})
export class ChatPanelComponent implements AfterViewInit, OnDestroy {
  /** Agents available for avatar lookup. Passed in from the parent. */
  readonly agents = input<AgentRef[]>([]);
  /** Orchestrations available for the "squad" picker. */
  readonly orchestrations = input<OrchestrationRef[]>([]);

  // ── State signals ──────────────────────────────────────────────────────────
  readonly sessions        = signal<ChatSession[]>([]);
  readonly activeSessionId = signal<number | null>(null);
  readonly messages        = signal<ChatMessage[]>([]);
  readonly draft           = signal<string>('');
  readonly sending         = signal<boolean>(false);
  readonly status          = signal<string>('');
  readonly followups       = signal<string[]>([
    '@Scorpion trace callers of OnTakeDamage',
    '@Kano harden the patch against teammate fire',
    '@Kitana extend struct to +0x80',
  ]);
  readonly selectedProviderId = signal<string>('codex');
  readonly composerCollapsed = signal<boolean>(false);
  readonly providerOptions: AiProviderOption[] = [
    { id: 'codex', label: 'Codex', model: 'GPT-5.5 Codex', badge: 'CX', description: 'Best for repo-aware coding, patches, and agentic implementation.' },
    { id: 'gpt55', label: 'OpenAI', model: 'GPT-5.5', badge: 'AI', description: 'Best for general reasoning, design, and broad assistant work.' },
    { id: 'copilot', label: 'Copilot', model: 'Copilot Chat', badge: 'CP', description: 'Familiar VS Code assistant workflow and model routing.' },
    { id: 'claude', label: 'Claude', model: 'Claude Sonnet', badge: 'CL', description: 'Alternative long-context assistant provider.' },
    { id: 'local', label: 'Local', model: 'Workspace MCP', badge: 'LC', description: 'Custom local or workspace-backed model endpoint.' },
  ];
  readonly selectedProvider = computed<AiProviderOption>(() =>
    this.providerOptions.find(p => p.id === this.selectedProviderId()) ?? this.providerOptions[0],
  );

  readonly activeSession = computed<ChatSession | undefined>(() =>
    this.sessions().find(s => s.id === this.activeSessionId()) ?? undefined,
  );

  @ViewChild('scrollEl') private scrollEl?: ElementRef<HTMLDivElement>;
  @ViewChild('composerEl') private composerEl?: ElementRef<HTMLTextAreaElement>;

  private readonly zone   = inject(NgZone);
  private readonly vscode = getVsCodeApi();

  constructor() {
    // Auto-scroll on every message-list change.
    effect(() => {
      const _ = this.messages();
      queueMicrotask(() => this.scrollToBottom());
    });
  }

  ngAfterViewInit(): void {
    window.addEventListener('message', this.onWindowMessage);
    this.loadSessions();
  }

  ngOnDestroy(): void {
    window.removeEventListener('message', this.onWindowMessage);
  }

  // ── Message channel ────────────────────────────────────────────────────────

  private readonly onWindowMessage = (e: MessageEvent) =>
    this.zone.run(() => this.handle(e.data));

  private post(type: string, extra: Record<string, unknown> = {}): void {
    this.vscode.postMessage({ type, ...extra });
  }

  private handle(data: any): void {
    switch (data?.type) {
      case 'chatSessionListResult':
        this.sessions.set(data.sessions ?? []);
        if (data.error) { this.status.set(data.error); }
        // Auto-select the first session if none is active.
        if (this.activeSessionId() === null && this.sessions().length > 0) {
          this.selectSession(this.sessions()[0]);
        }
        break;

      case 'chatSessionGetResult':
        if (data.error) { this.status.set(data.error); break; }
        if (data.session) {
          this.activeSessionId.set(data.session.id);
          // Merge the freshly-fetched session into the sidebar list.
          this.sessions.update(list => {
            const next = list.filter(s => s.id !== data.session.id);
            next.unshift(data.session);
            return next;
          });
        }
        this.messages.set((data.messages ?? []).map(parseBlocks));
        break;

      case 'chatSessionActionResult':
        if (data.error) { this.status.set(data.error); break; }
        this.status.set(data.message || 'Saved.');
        if (data.deletedId) {
          this.sessions.update(list => list.filter(s => s.id !== data.deletedId));
          if (this.activeSessionId() === data.deletedId) {
            this.activeSessionId.set(null);
            this.messages.set([]);
          }
        }
        this.loadSessions();
        if (data.session && (data.deletedId === null || data.deletedId === undefined)) { this.selectSession(data.session); }
        break;

      case 'chatMessageClearResult':
        if (data.error) { this.status.set(data.error); break; }
        if (data.sessionId === this.activeSessionId()) { this.messages.set([]); }
        this.status.set('Cleared.');
        break;

      case 'chatMessageActionResult':
        if (data.error) { this.status.set(data.error); break; }
        if (data.command) { this.draft.set(data.command); queueMicrotask(() => this.composerEl?.nativeElement.focus()); }
        this.status.set(data.message || 'Action ready.');
        break;

      case 'chatCodeActionResult':
        if (data.error) { this.status.set(data.error); break; }
        this.status.set(data.message || 'Code action queued.');
        break;

      case 'chatSendResult':
        this.sending.set(false);
        if (data.error) { this.status.set(data.error); break; }
        if (data.sessionId === this.activeSessionId()) {
          const appended = (data.messages ?? []).map(parseBlocks);
          this.messages.update(list => [...list, ...appended]);
        }
        // Bump message count in the sidebar.
        this.sessions.update(list => list.map(s =>
          s.id === data.sessionId
            ? { ...s, messageCount: s.messageCount + (data.messages?.length ?? 0), updatedAt: new Date().toISOString() }
            : s,
        ));
        this.status.set('');
        break;
    }
  }

  // ── User actions ───────────────────────────────────────────────────────────

  loadSessions(): void { this.post('chatSessionList'); }

  selectSession(s: ChatSession): void {
    this.activeSessionId.set(s.id);
    this.messages.set([]);
    this.status.set('');
    this.post('chatSessionGet', { id: s.id });
  }

  createSession(): void {
    // VS Code webviews do not reliably support blocking browser prompts, so
    // create the thread immediately and let users rename it from the header.
    const nextNumber = this.sessions().length + 1;
    const title = nextNumber === 1 ? 'New chat' : `New chat ${nextNumber}`;
    const orch = this.orchestrations().find(o => o.isActive);
    this.status.set('Creating chat…');
    this.post('chatSessionCreate', {
      title,
      orchestrationId:  orch?.id,
    });
  }

  renameSession(): void {
    const cur = this.activeSession(); if (!cur) { return; }
    const next = window.prompt?.('Rename chat:', cur.title);
    if (!next || !next.trim() || next.trim() === cur.title) { return; }
    this.post('chatSessionRename', { id: cur.id, title: next.trim() });
  }

  deleteSession(): void {
    const cur = this.activeSession(); if (!cur) { return; }
    if (!window.confirm?.(`Delete '${cur.title}' and all its messages?`)) { return; }
    this.post('chatSessionDelete', { id: cur.id });
  }

  clearMessages(): void {
    const cur = this.activeSession(); if (!cur) { return; }
    if (!window.confirm?.('Clear every message in this chat?')) { return; }
    this.post('chatMessageClear', { sessionId: cur.id });
  }

  send(): void {
    const cur = this.activeSession(); if (!cur) { return; }
    const text = this.draft().trim(); if (!text) { return; }
    this.sending.set(true);
    this.status.set('Dispatching…');
    this.draft.set('');
    this.post('chatSend', { sessionId: cur.id, text });
  }

  onComposerEnter(ev: Event): void {
    const ke = ev as KeyboardEvent;
    if (ke.shiftKey) { return; }      // Shift+Enter inserts a newline.
    ev.preventDefault();
    this.send();
  }

  copyMessage(m: ChatMessage): void {
    const text = blocksFor(m).map(b => blockToText(b)).join('\n\n');
    this.copyText(text);
  }

  copyText(text: string): void {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      void navigator.clipboard.writeText(text);
    }
  }

  onActionChip(chip: { id: string; label: string; command?: string }): void {
    // §3.10 in the chat-templates spec: chip click routes through the host.
    this.post('chatMessageAction', { sessionId: this.activeSessionId(), chipId: chip.id, command: chip.command });
  }

  onCodeAction(block: Extract<Block, { kind: 'code' }>, action: string): void {
    if (action === 'copy') { this.copyText(block.source); this.status.set('Copied code.'); return; }
    this.post('chatCodeAction', { sessionId: this.activeSessionId(), action, filename: block.filename, language: block.language, source: block.source });
  }

  useFollowup(text: string): void {
    this.draft.set(text);
    queueMicrotask(() => this.composerEl?.nativeElement.focus());
  }

  setProvider(id: string): void {
    if (!this.providerOptions.some(p => p.id === id)) { return; }
    this.selectedProviderId.set(id);
    this.status.set(`Model set to ${this.selectedProvider().label} · ${this.selectedProvider().model}.`);
  }

  toggleComposer(): void {
    this.composerCollapsed.update(v => !v);
    if (this.composerCollapsed()) { return; }
    queueMicrotask(() => this.composerEl?.nativeElement.focus());
  }

  contextCount(session: ChatSession): number {
    return [session.gameTag, session.gameSerial, session.orchestrationName].filter(Boolean).length;
  }

  contextTitle(session: ChatSession): string {
    const items = [
      session.gameTag ? `Game: ${session.gameTag}` : '',
      session.gameSerial ? `Serial: ${session.gameSerial}` : '',
      session.orchestrationName ? `Squad: ${session.orchestrationName}` : '',
    ].filter(Boolean);
    return items.length > 0 ? items.join(' • ') : 'No attached chat context yet';
  }

  showContext(session: ChatSession): void {
    this.status.set(this.contextTitle(session));
  }

  // ── Rendering helpers ──────────────────────────────────────────────────────

  blocksFor(m: ChatMessage): Block[] { return m._blocks ?? []; }

  avatarFor(m: ChatMessage): string | undefined {
    if (!m.agentId) {
      // Fall back to a name-prefix match — handy for stub orchestrator replies
      // that don't have an agent FK yet.
      const display = (m.agentDisplay || '').toLowerCase();
      return this.agents().find(a => display.includes(a.name.toLowerCase()))?.imageUri;
    }
    return this.agents().find(a => a.id === m.agentId)?.imageUri;
  }

  initials(name: string | undefined): string {
    if (!name) { return 'AI'; }
    return name.replace(/^\[[^\]]+\]\s*/, '').split(/\s+/).slice(0, 2)
               .map(s => s[0]).join('').toUpperCase() || 'AI';
  }

  formatTime(iso: string): string {
    const d = new Date(iso); if (Number.isNaN(d.getTime())) { return ''; }
    const today = new Date();
    if (d.toDateString() === today.toDateString()) {
      return d.toTimeString().slice(0, 5);
    }
    return d.toLocaleDateString();
  }

  formatLatency(ms: number): string {
    if (ms < 1000) { return `${ms}ms`; }
    return `${(ms / 1000).toFixed(1)}s`;
  }

  formatArgs(args?: Record<string, unknown>): string {
    if (!args || Object.keys(args).length === 0) { return ''; }
    const inner = Object.entries(args).map(([k, v]) => `${k}=${formatArgValue(v)}`).join(', ');
    return ` (${inner})`;
  }

  anyColumnLabels(cols: { label?: string }[]): boolean {
    return cols.some(c => !!c.label);
  }

  isPill(v: unknown): boolean {
    return !!v && typeof v === 'object' && (v as any).kind === 'pill';
  }

  padNum(n: number): string { return n < 10 ? `0${n}` : String(n); }


  renderDisasmBlock(b: Extract<Block, { kind: 'disasm' }>): string {
    return (b.lines || []).map(l => renderDisasmLine(l)).join('');
  }

  renderCodeSource(b: Extract<Block, { kind: 'code' }>): string {
    const language = (b.language || '').toLowerCase();
    return (b.source || '').split('\n').map(line => renderCodeLine(line, language)).join('\n');
  }

  /**
   * Lightweight inline-markdown renderer: code spans (`x`), bold (**x**), and
   * label pills ([[x]]). Anything else passes through escaped. No fenced blocks
   * or headings — those have dedicated `code` / `tool-call` block kinds.
   */
  renderInline(md: string | undefined): string {
    if (!md) { return ''; }
    const escaped = md
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');
    return escaped
      .replace(/\[\[([^\]]+)\]\]/g, '<span class="cb-pill" data-tone="info">$1</span>')
      .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
      .replace(/`([^`]+)`/g, '<code>$1</code>');
  }

  private scrollToBottom(): void {
    const el = this.scrollEl?.nativeElement; if (!el) { return; }
    el.scrollTop = el.scrollHeight;
  }
}



function renderDisasmLine(l: { addr: string; op: string; ops?: string; note?: string; tag?: string; highlight?: boolean }): string {
  const addr = `<span class="addr">${escapeHtml(l.addr)}</span>`;
  const op = `<span class="op">${escapeHtml(l.op || '')}</span>`;
  const args = `<span class="args">${colorizeRegisters(escapeHtml(l.ops || ''))}</span>`;
  const note = l.note ? ` <span class="comment">; ${escapeHtml(l.note)}</span>` : '';
  const tag = l.tag ? ` <em>${escapeHtml(l.tag)}</em>` : '';
  return `<span class="cb-disasm-line${l.highlight ? ' highlight' : ''}">${addr}    ${op}    ${args}${note}${tag}</span>`;
}

function escapeHtml(text: string | undefined): string {
  return String(text ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function renderCodeLine(line: string, language: string): string {
  if (language === 'cds') {
    const directive = line.match(/^(\s*)(@[A-Za-z_][\w]*)\b(.*)$/);
    if (directive) {
      return `${escapeHtml(directive[1])}<span class="cb-cds-directive">${escapeHtml(directive[2])}</span>${renderProgramCode(directive[3])}`;
    }

    const fnLine = line.match(/^(\s*)(fn)\s+([A-Za-z_][\w]*)\b(.*)$/);
    if (fnLine) {
      return `${escapeHtml(fnLine[1])}<span class="cb-cds-keyword">${escapeHtml(fnLine[2])}</span> <span class="label">${escapeHtml(fnLine[3])}</span>${renderProgramCode(fnLine[4])}`;
    }

    const labelLine = line.match(/^(\s*)([A-Za-z_][\w]*:)(.*)$/);
    if (labelLine) {
      return `${escapeHtml(labelLine[1])}<span class="label">${escapeHtml(labelLine[2])}</span>${renderProgramCode(labelLine[3])}`;
    }
  }

  const parts = splitLineComment(line, language);
  const asm = parts.code.match(/^(\s*)([A-Za-z.][\w.]*)\b(.*)$/);
  if (asm && (language === 'mips' || language === 'asm' || looksLikeMipsOp(asm[2]))) {
    return `${escapeHtml(asm[1])}<span class="op">${escapeHtml(asm[2])}</span><span class="args">${colorizeRegisters(escapeHtml(asm[3]))}</span>${renderComment(parts.comment)}`;
  }

  if (language === 'cds' || looksLikeProgramLanguage(language)) {
    return renderProgramCode(parts.code) + renderComment(parts.comment);
  }

  return colorizeRegisters(escapeHtml(parts.code)) + renderComment(parts.comment);
}

function splitLineComment(line: string, language: string): { code: string; comment: string } {
  const commentTokens = language === 'asm' || language === 'mips' ? [';'] : ['//', ';'];
  let quote: string | null = null;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (quote) {
      if (ch === '\\') { i++; continue; }
      if (ch === quote) { quote = null; }
      continue;
    }
    if (ch === '"' || ch === "'" || ch === '`') { quote = ch; continue; }
    const token = commentTokens.find(t => line.startsWith(t, i));
    if (token) {
      return { code: line.slice(0, i), comment: line.slice(i) };
    }
  }
  return { code: line, comment: '' };
}

function renderComment(comment: string): string {
  return comment ? `<span class="comment">${escapeHtml(comment)}</span>` : '';
}

function looksLikeProgramLanguage(language: string): boolean {
  return /^(program|source|code|c|cpp|c\+\+|cs|csharp|js|jsx|ts|tsx|javascript|typescript|java|rust|go|python|py)$/i.test(language);
}

function renderProgramCode(code: string): string {
  const tokenPattern = /("(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`|\b0x[0-9a-fA-F]+\b|\b\d+(?:\.\d+)?\b|\b[A-Za-z_][\w]*\b)/g;
  let out = '';
  let last = 0;
  for (const match of code.matchAll(tokenPattern)) {
    const token = match[0];
    const index = match.index ?? 0;
    out += colorizeRegisters(escapeHtml(code.slice(last, index)));
    const nextChar = code[index + token.length] ?? '';
    if (isStringToken(token)) {
      out += `<span class="cb-prog-string">${escapeHtml(token)}</span>`;
    } else if (/^(?:0x[0-9a-fA-F]+|\d+(?:\.\d+)?)$/.test(token)) {
      out += `<span class="cb-prog-number">${escapeHtml(token)}</span>`;
    } else if (isProgramKeyword(token)) {
      out += `<span class="cb-cds-keyword">${escapeHtml(token)}</span>`;
    } else if (nextChar === '(') {
      out += `<span class="cb-prog-function">${escapeHtml(token)}</span>`;
    } else if (/^[A-Z][A-Za-z0-9_]*$/.test(token)) {
      out += `<span class="cb-prog-type">${escapeHtml(token)}</span>`;
    } else {
      out += colorizeRegisters(escapeHtml(token));
    }
    last = index + token.length;
  }
  out += colorizeRegisters(escapeHtml(code.slice(last)));
  return out;
}

function isStringToken(token: string): boolean {
  return token.startsWith('\"') || token.startsWith("'") || token.startsWith('`');
}

function isProgramKeyword(token: string): boolean {
  return /^(fn|function|proc|program|var|let|const|struct|class|enum|type|interface|impl|return|if|else|for|while|do|switch|case|default|break|continue|goto|try|catch|finally|throw|new|delete|public|private|protected|static|readonly|async|await|yield|import|export|from|using|namespace|include|define|extern|true|false|null|undefined|void|int|uint|short|ushort|long|ulong|float|double|bool|char|string|byte|sbyte|u8|u16|u32|u64|i8|i16|i32|i64|usize|isize)$/i.test(token);
}

function looksLikeMipsOp(op: string): boolean {
  return /^(addiu?|addu|subu?|lw|sw|lb|sb|lh|sh|lui|ori|andi|xori|sll|srl|sra|sltu?|sltiu?|beqz?|bnez?|bgtz|blez|j|jal|jr|nop|move|li|la|multu?|divu?|mfhi|mflo)$/i.test(op);
}

// ── Helpers (free functions so they can be reused) ───────────────────────────

function parseBlocks(m: ChatMessage): ChatMessage {
  try {
    const parsed = JSON.parse(m.blocksJson ?? '[]');
    return { ...m, _blocks: Array.isArray(parsed) ? parsed as Block[] : [] };
  } catch {
    return { ...m, _blocks: [{ kind: 'text', md: '[unrenderable block payload]' }] };
  }
}

function blocksFor(m: ChatMessage): Block[] { return m._blocks ?? []; }

function blockToText(b: Block): string {
  switch (b.kind) {
    case 'text':     return b.md;
    case 'code':     return b.source;
    case 'kv':       return b.items.map(it => `${it.k}: ${it.v}`).join('\n');
    case 'handoff':  return `${b.note || ''} ${b.label?.text || ''} → ${b.to?.display || ''}`.trim();
    case 'dispatch': return (b.title || 'Dispatch') + '\n' + b.rows.map(r => ` - ${r.agent?.display || ''}: ${r.task} [${r.status}]`).join('\n');
    case 'tool-call':return `[${b.tool}] ${b.summary || ''}` + (b.body ? '\n' + blockToText(b.body) : '');
    case 'timeline': return b.rows.map(r => `${r.t}  ${r.label} [${r.status}]`).join('\n');
    case 'table':    return b.rows.map(row => b.columns.map(c => String(row[c.key] ?? '')).join('\t')).join('\n');
    case 'disasm':   return b.lines.map(l => `${l.addr}  ${l.op}  ${l.ops || ''}`).join('\n');
    case 'struct-map': return b.fields.map(f => `${f.offset}  ${f.type}  ${f.name}  ${f.value || ''}`).join('\n');
    case 'actions':  return b.chips.map(c => `[${c.label}]`).join(' ');
    default:         return '';
  }
}

function formatArgValue(v: unknown): string {
  if (v === null || v === undefined) { return ''; }
  if (typeof v === 'string') { return v; }
  if (typeof v === 'number' || typeof v === 'boolean') { return String(v); }
  try { return JSON.stringify(v); } catch { return String(v); }
}
