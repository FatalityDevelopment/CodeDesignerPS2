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
import './ai-config-page.css';

export interface AgentProfile {
  id:                  number;
  isAgentLead:         boolean;
  name:                string;
  description?:        string;
  status?:             string;
  objective?:          string;
  rules?:              string;
  guidelines?:         string;
  recommendedWorkflow?: string;
  sessionBegin?:       string;
  concurrentTasks?:    string;
  sessionEnd?:         string;
  research?:           string;
  createdAt:           string;
  updatedAt:           string;
}

export interface OrchestrationProfile {
  id:           number;
  name:         string;
  description?: string;
  strategy?:    string;
  roles?:       string;
  workflow?:    string;
  rules?:       string;
  notes?:       string;
  createdAt:    string;
  updatedAt:    string;
}

export interface McpMemoryEntry {
  id:        number;
  name:      string;
  content:   string;
  gameId?:   string;
  createdAt: string;
  updatedAt: string;
}

// ── Markdown field descriptors ────────────────────────────────────────────────

interface MarkdownField<T> {
  key:         keyof T;
  label:       string;
  description: string;
  isMarkdown:  boolean;
  rows:        number;
}

const AGENT_FIELDS: MarkdownField<AgentProfile>[] = [
  { key: 'name',               label: 'Name',                description: 'Display name for this agent profile.',                     isMarkdown: false, rows: 1  },
  { key: 'status',             label: 'Status',              description: 'Current status, e.g. "active", "inactive", "experimental".', isMarkdown: false, rows: 1  },
  { key: 'description',        label: 'Description',         description: 'High-level description of the agent\'s purpose and role.', isMarkdown: true,  rows: 4  },
  { key: 'objective',          label: 'Objective',           description: 'The goal this agent is working toward (Markdown).',        isMarkdown: true,  rows: 4  },
  { key: 'rules',              label: 'Rules',               description: 'Hard rules the agent must always follow (Markdown).',      isMarkdown: true,  rows: 6  },
  { key: 'guidelines',         label: 'Guidelines',          description: 'Recommended best-practice guidelines (Markdown).',         isMarkdown: true,  rows: 6  },
  { key: 'sessionBegin',       label: 'Session Begin',       description: 'Tasks to perform at the start of each session (Markdown).', isMarkdown: true, rows: 4  },
  { key: 'concurrentTasks',    label: 'Concurrent Tasks',    description: 'Tasks that run continuously throughout a session (Markdown).', isMarkdown: true, rows: 4 },
  { key: 'sessionEnd',         label: 'Session End',         description: 'Tasks to perform at the end of each session (Markdown).',  isMarkdown: true,  rows: 4  },
  { key: 'recommendedWorkflow',label: 'Recommended Workflow',description: 'The recommended workflow the agent should follow (Markdown).', isMarkdown: true, rows: 6 },
  { key: 'research',           label: 'Research',            description: 'Knowledge bases or sources the agent should consult (Markdown).', isMarkdown: true, rows: 4 },
];

const ORCHESTRATION_FIELDS: MarkdownField<OrchestrationProfile>[] = [
  { key: 'name',        label: 'Name',        description: 'Display name for this orchestration profile.',                  isMarkdown: false, rows: 1 },
  { key: 'description', label: 'Description', description: 'High-level description of this orchestration profile.',         isMarkdown: true,  rows: 4 },
  { key: 'strategy',    label: 'Strategy',    description: 'The overall coordination strategy for agents (Markdown).',      isMarkdown: true,  rows: 6 },
  { key: 'roles',       label: 'Roles',       description: 'Role assignments for agents within this orchestration (Markdown).', isMarkdown: true, rows: 6 },
  { key: 'workflow',    label: 'Workflow',     description: 'The coordination workflow / process for this profile (Markdown).', isMarkdown: true, rows: 6 },
  { key: 'rules',       label: 'Rules',       description: 'Coordination rules that agents must follow (Markdown).',        isMarkdown: true,  rows: 6 },
  { key: 'notes',       label: 'Notes',       description: 'Additional notes or references for this profile (Markdown).',   isMarkdown: true,  rows: 4 },
];

@Component({
  selector:      'app-ai-config-page',
  standalone:    true,
  encapsulation: ViewEncapsulation.None,
  template: `
<div class="aic-outer">

  <!-- ── Top tab bar ─────────────────────────────────────────────────────── -->
  <div class="aic-tab-bar">
    <button class="aic-tab" [class.active]="activeTab() === 'orchestration'" (click)="activeTab.set('orchestration')">
      Orchestration
    </button>
    <button class="aic-tab" [class.active]="activeTab() === 'agents'" (click)="activeTab.set('agents')">
      Agents
    </button>
  </div>

  <div class="aic-tab-content">

    <!-- ═══════════════════════════ ORCHESTRATION TAB ══════════════════════ -->
    <div class="aic-tab-panel" [class.hidden]="activeTab() !== 'orchestration'">

      <!-- Left sidebar -->
      <aside class="aic-sidebar">
        <div class="aic-sidebar-header">
          <span class="aic-sidebar-title">Orchestration Profiles</span>
          <button class="aic-icon-btn" title="Refresh" (click)="loadOrchestrations()">&#x21BB;</button>
        </div>

        <div class="aic-sidebar-actions">
          <button class="aic-small-btn aic-primary-btn" (click)="onNewOrchestration()">+ New</button>
          <button class="aic-small-btn" title="Import from .md file" (click)="onImportOrchestration()">&#x2B06; Import</button>
        </div>

        @if (orchStatusMessage()) {
          <div class="aic-status"
               [class.aic-status-ok]="orchStatusOk()"
               [class.aic-status-error]="orchStatusError()">
            {{ orchStatusMessage() }}
          </div>
        }

        <ul class="aic-agent-list">
          @for (orch of orchestrations(); track orch.id) {
            <li class="aic-agent-item"
                [class.selected]="orchSelectedId() === orch.id"
                (click)="selectOrchestration(orch)">
              <span class="aic-agent-name" [title]="orch.name">{{ orch.name }}</span>
            </li>
          }
          @if (orchestrations().length === 0) {
            <li class="aic-empty">No profiles yet. Click "+ New" to create one.</li>
          }
        </ul>
      </aside>

      <!-- Right panel -->
      <main class="aic-main">
        @if (!orchSelectedId() && !orchShowNewForm()) {
          <div class="aic-empty-state">
            <p>Select an orchestration profile from the left panel, or click <strong>+ New</strong> to create one.</p>
          </div>
        }

        @if (orchShowNewForm() || orchSelectedId() !== null) {
          <div class="aic-form-header">
            <h2 class="aic-form-title">
              {{ orchShowNewForm() ? 'New Orchestration Profile' : currentOrchestration()?.name ?? 'Edit Profile' }}
            </h2>
            <div class="aic-form-header-actions">
              @if (!orchShowNewForm() && orchSelectedId() !== null) {
                <button class="aic-small-btn" (click)="onExportOrchestration()">&#x2B07; Export</button>
                <button class="aic-small-btn aic-danger-btn" (click)="onDeleteOrchestration()">&#x2715; Delete</button>
              }
            </div>
          </div>

          @for (field of orchFields; track field.key) {
            <div class="aic-setting-row">
              <div class="aic-setting-label">
                <span class="aic-setting-name">{{ field.label }}</span>
                <span class="aic-setting-desc">{{ field.description }}</span>
              </div>
              <div class="aic-setting-control">
                @if (!field.isMarkdown || !orchPreviewModes()[field.key]) {
                  <textarea
                    class="aic-textarea"
                    [class.aic-textarea-single]="!field.isMarkdown"
                    [rows]="field.rows"
                    spellcheck="false"
                    [value]="getOrchFieldValue(field.key)"
                    (input)="setOrchFieldValue(field.key, $any($event.target).value)"></textarea>
                }
                @if (field.isMarkdown && orchPreviewModes()[field.key]) {
                  <div class="aic-md-preview" [innerHTML]="renderMarkdown(getOrchFieldValue(field.key))"></div>
                }
                @if (field.isMarkdown) {
                  <div class="aic-md-toolbar">
                    <button class="aic-md-toggle"
                            [class.active]="!orchPreviewModes()[field.key]"
                            (click)="setOrchPreviewMode(field.key, false)">Edit</button>
                    <button class="aic-md-toggle"
                            [class.active]="orchPreviewModes()[field.key]"
                            (click)="setOrchPreviewMode(field.key, true)">Preview</button>
                  </div>
                }
              </div>
            </div>
          }

          <div class="aic-form-actions">
            @if (orchShowNewForm()) {
              <button class="aic-btn aic-primary-btn" [disabled]="!orchFormName().trim()" (click)="onCreateOrchestration()">Create Profile</button>
              <button class="aic-btn" (click)="orchCancelNew()">Cancel</button>
            } @else {
              <button class="aic-btn aic-primary-btn" (click)="onSaveOrchestration()">Save</button>
              <button class="aic-btn" (click)="orchRevertForm()">Revert</button>
            }
          </div>
        }
      </main>
    </div>

    <!-- ═══════════════════════════════ AGENTS TAB ═════════════════════════ -->
    <div class="aic-tab-panel" [class.hidden]="activeTab() !== 'agents'">

      <!-- Left sidebar: agent list -->
      <aside class="aic-sidebar">
        <div class="aic-sidebar-header">
          <span class="aic-sidebar-title">Agent Profiles</span>
          <button class="aic-icon-btn" title="Refresh" (click)="loadAgents()">&#x21BB;</button>
        </div>

        <div class="aic-sidebar-actions">
          <button class="aic-small-btn aic-primary-btn" (click)="onNewAgent()">+ New</button>
          <button class="aic-small-btn" title="Import from .md file" (click)="onImportAgent()">&#x2B06; Import</button>
        </div>

        @if (statusMessage()) {
          <div class="aic-status"
               [class.aic-status-ok]="statusOk()"
               [class.aic-status-error]="statusError()">
            {{ statusMessage() }}
          </div>
        }

        <ul class="aic-agent-list">
          @for (agent of agents(); track agent.id) {
            <li class="aic-agent-item"
                [class.selected]="selectedId() === agent.id"
                (click)="selectAgent(agent)">
              <span class="aic-agent-name" [title]="agent.name">
                @if (agent.isAgentLead) { <span class="aic-lead-badge" title="Lead agent">&#x2605;</span> }
                {{ agent.name }}
              </span>
              @if (agent.status) {
                <span class="aic-agent-status">{{ agent.status }}</span>
              }
            </li>
          }
          @if (agents().length === 0) {
            <li class="aic-empty">No agents yet. Click "+ New" to create one.</li>
          }
        </ul>
      </aside>

      <!-- Right panel: form -->
      <main class="aic-main">
        @if (!selectedId() && !showNewForm()) {
          <div class="aic-empty-state">
            <p>Select an agent from the left panel, or click <strong>+ New</strong> to create one.</p>
          </div>
        }

        @if (showNewForm() || selectedId() !== null) {
          <div class="aic-form-header">
            <h2 class="aic-form-title">
              {{ showNewForm() ? 'New Agent Profile' : currentAgent()?.name ?? 'Edit Agent' }}
            </h2>
            <div class="aic-form-header-actions">
              @if (!showNewForm() && selectedId() !== null) {
                <button class="aic-small-btn" (click)="onExportAgent()">&#x2B07; Export</button>
                <button class="aic-small-btn aic-danger-btn" (click)="onDeleteAgent()">&#x2715; Delete</button>
              }
            </div>
          </div>

          <!-- Lead agent toggle -->
          <div class="aic-setting-row">
            <div class="aic-setting-label">
              <label class="aic-lead-label">
                <input type="checkbox"
                       [checked]="formIsLead()"
                       (change)="formIsLead.set($any($event.target).checked)" />
                &nbsp;Lead Agent
              </label>
              <span class="aic-setting-desc">Mark this as the primary / lead agent in a multi-agent setup.</span>
            </div>
          </div>

          <!-- All markdown/text fields -->
          @for (field of fields; track field.key) {
            <div class="aic-setting-row">
              <div class="aic-setting-label">
                <span class="aic-setting-name">{{ field.label }}</span>
                <span class="aic-setting-desc">{{ field.description }}</span>
              </div>
              <div class="aic-setting-control">
                @if (!field.isMarkdown || !previewModes()[field.key]) {
                  <textarea
                    class="aic-textarea"
                    [class.aic-textarea-single]="!field.isMarkdown"
                    [rows]="field.rows"
                    spellcheck="false"
                    [value]="getFieldValue(field.key)"
                    (input)="setFieldValue(field.key, $any($event.target).value)"></textarea>
                }
                @if (field.isMarkdown && previewModes()[field.key]) {
                  <div class="aic-md-preview" [innerHTML]="renderMarkdown(getFieldValue(field.key))"></div>
                }
                @if (field.isMarkdown) {
                  <div class="aic-md-toolbar">
                    <button class="aic-md-toggle"
                            [class.active]="!previewModes()[field.key]"
                            (click)="setPreviewMode(field.key, false)">Edit</button>
                    <button class="aic-md-toggle"
                            [class.active]="previewModes()[field.key]"
                            (click)="setPreviewMode(field.key, true)">Preview</button>
                  </div>
                }
              </div>
            </div>
          }

          <!-- Save / Cancel -->
          <div class="aic-form-actions">
            @if (showNewForm()) {
              <button class="aic-btn aic-primary-btn" [disabled]="!formName().trim()" (click)="onCreateAgent()">Create Agent</button>
              <button class="aic-btn" (click)="cancelNew()">Cancel</button>
            } @else {
              <button class="aic-btn aic-primary-btn" (click)="onSaveAgent()">Save</button>
              <button class="aic-btn" (click)="revertForm()">Revert</button>
            }
          </div>

          <!-- Memories section (shown when editing) -->
          @if (!showNewForm() && selectedId() !== null) {
            <div class="aic-memories-section">
              <div class="aic-memories-header">
                <h3 class="aic-memories-title">Agent Memories</h3>
                <div class="aic-memories-actions">
                  <button class="aic-icon-btn" title="Refresh memories" (click)="loadMemories()">&#x21BB;</button>
                  <button class="aic-small-btn" title="Import memory from .md file" (click)="onImportMemory()">&#x2B06; Import</button>
                  <button class="aic-small-btn aic-primary-btn" (click)="onAddMemory()">+ New Memory</button>
                </div>
              </div>

              @if (memStatusMessage()) {
                <div class="aic-status"
                     [class.aic-status-ok]="memStatusOk()"
                     [class.aic-status-error]="memStatusError()">
                  {{ memStatusMessage() }}
                </div>
              }

              @if (showMemAddForm()) {
                <div class="aic-mem-add-form">
                  <input class="aic-mem-name-input"
                         type="text"
                         placeholder="Memory name…"
                         autocomplete="off"
                         spellcheck="false"
                         [value]="newMemName()"
                         (input)="newMemName.set($any($event.target).value)"
                         (keydown.enter)="doAddMemory()"
                         (keydown.escape)="showMemAddForm.set(false)" />
                  <div class="aic-mem-add-row">
                    <button class="aic-small-btn aic-primary-btn"
                            [disabled]="!newMemName().trim()"
                            (click)="doAddMemory()">Create</button>
                    <button class="aic-small-btn" (click)="showMemAddForm.set(false)">Cancel</button>
                  </div>
                </div>
              }

              <div class="aic-mem-list">
                @if (memories().length === 0) {
                  <div class="aic-empty">No memories yet for this agent.</div>
                }
                @for (mem of memories(); track mem.id) {
                  <div class="aic-mem-item">
                    <div class="aic-mem-item-info">
                      <span class="aic-mem-name" [title]="mem.name">{{ mem.name }}</span>
                      <span class="aic-mem-date">{{ formatDate(mem.updatedAt) }}</span>
                    </div>
                    <div class="aic-mem-item-actions">
                      @if (pendingMemDelete() === mem.id) {
                        <span class="aic-confirm-text">Delete?</span>
                        <button class="aic-item-btn aic-danger-btn" (click)="doDeleteMemory(mem.id)">Yes</button>
                        <button class="aic-item-btn" (click)="pendingMemDelete.set(null)">No</button>
                      } @else {
                        <button class="aic-item-btn" title="Edit in editor" (click)="onEditMemory(mem)">Edit</button>
                        <button class="aic-item-btn" title="Export to .md" (click)="onExportMemory(mem)">&#x2B07;</button>
                        <button class="aic-item-btn aic-danger-btn" title="Delete" (click)="pendingMemDelete.set(mem.id)">&#x2715;</button>
                      }
                    </div>
                  </div>
                }
              </div>
            </div>
          }
        }
      </main>
    </div>

  </div>
</div>
  `,
})
export class AiConfigPageComponent implements AfterViewInit {
  private readonly vscode = getVsCodeApi();
  private readonly zone   = inject(NgZone);

  readonly fields     = AGENT_FIELDS;
  readonly orchFields = ORCHESTRATION_FIELDS;

  // ── Tab state ──────────────────────────────────────────────────────────────
  readonly activeTab = signal<'orchestration' | 'agents'>('orchestration');

  // ── Orchestration list state ──────────────────────────────────────────────
  readonly orchestrations     = signal<OrchestrationProfile[]>([]);
  readonly orchSelectedId     = signal<number | null>(null);
  readonly currentOrchestration = computed(() => this.orchestrations().find(o => o.id === this.orchSelectedId()) ?? null);
  readonly orchShowNewForm    = signal(false);
  readonly orchStatusMessage  = signal('');
  readonly orchStatusOk       = signal(false);
  readonly orchStatusError    = signal(false);

  // ── Orchestration form fields ─────────────────────────────────────────────
  readonly orchFormName        = signal('');
  readonly orchFormDescription = signal('');
  readonly orchFormStrategy    = signal('');
  readonly orchFormRoles       = signal('');
  readonly orchFormWorkflow    = signal('');
  readonly orchFormRules       = signal('');
  readonly orchFormNotes       = signal('');
  readonly orchPreviewModes    = signal<Record<string, boolean>>({});
  private _pendingOrchDelete   = false;

  // ── Agent list state ───────────────────────────────────────────────────────
  readonly agents        = signal<AgentProfile[]>([]);
  readonly selectedId    = signal<number | null>(null);
  readonly currentAgent  = computed(() => this.agents().find(a => a.id === this.selectedId()) ?? null);
  readonly showNewForm   = signal(false);
  readonly statusMessage = signal('');
  readonly statusOk      = signal(false);
  readonly statusError   = signal(false);

  // ── Agent form fields ─────────────────────────────────────────────────────
  readonly formName               = signal('');
  readonly formStatus             = signal('');
  readonly formDescription        = signal('');
  readonly formObjective          = signal('');
  readonly formRules              = signal('');
  readonly formGuidelines         = signal('');
  readonly formRecommendedWorkflow = signal('');
  readonly formSessionBegin       = signal('');
  readonly formConcurrentTasks    = signal('');
  readonly formSessionEnd         = signal('');
  readonly formResearch           = signal('');
  readonly formIsLead             = signal(false);
  readonly previewModes           = signal<Record<string, boolean>>({});

  // ── Memory state ──────────────────────────────────────────────────────────
  readonly memories         = signal<McpMemoryEntry[]>([]);
  readonly memStatusMessage = signal('');
  readonly memStatusOk      = signal(false);
  readonly memStatusError   = signal(false);
  readonly showMemAddForm   = signal(false);
  readonly newMemName       = signal('');
  readonly pendingMemDelete = signal<number | null>(null);

  private _pendingAgentDelete = false;

  // ── Lifecycle ──────────────────────────────────────────────────────────────

  ngAfterViewInit(): void {
    window.addEventListener('message', (event: MessageEvent) => {
      const data = event.data as Record<string, unknown>;
      this.zone.run(() => this._handleMessage(data));
    });
    this.vscode.postMessage({ type: 'ready' });
    this.loadOrchestrations();
    this.loadAgents();
  }

  // ── Orchestration list actions ────────────────────────────────────────────

  loadOrchestrations(): void {
    this.setOrchStatus('Loading…', false);
    this.vscode.postMessage({ type: 'orchestrationList' });
  }

  selectOrchestration(orch: OrchestrationProfile): void {
    this.orchSelectedId.set(orch.id);
    this.orchShowNewForm.set(false);
    this._populateOrchForm(orch);
  }

  onNewOrchestration(): void {
    this.orchSelectedId.set(null);
    this.orchShowNewForm.set(true);
    this._clearOrchForm();
  }

  orchCancelNew(): void {
    this.orchShowNewForm.set(false);
    this._clearOrchForm();
  }

  onCreateOrchestration(): void {
    const name = this.orchFormName().trim();
    if (!name) { return; }
    this.setOrchStatus('Creating…', false);
    this.vscode.postMessage({
      type:        'orchestrationAdd',
      name,
      description: this.orchFormDescription(),
      strategy:    this.orchFormStrategy(),
      roles:       this.orchFormRoles(),
      workflow:    this.orchFormWorkflow(),
      rules:       this.orchFormRules(),
      notes:       this.orchFormNotes(),
    });
  }

  onSaveOrchestration(): void {
    const id = this.orchSelectedId();
    if (id === null) { return; }
    this.setOrchStatus('Saving…', false);
    this.vscode.postMessage({
      type:        'orchestrationUpdate',
      id,
      name:        this.orchFormName(),
      description: this.orchFormDescription(),
      strategy:    this.orchFormStrategy(),
      roles:       this.orchFormRoles(),
      workflow:    this.orchFormWorkflow(),
      rules:       this.orchFormRules(),
      notes:       this.orchFormNotes(),
    });
  }

  orchRevertForm(): void {
    const orch = this.currentOrchestration();
    if (orch) { this._populateOrchForm(orch); }
  }

  onDeleteOrchestration(): void {
    const id = this.orchSelectedId();
    if (id === null) { return; }
    if (!this._pendingOrchDelete) {
      this._pendingOrchDelete = true;
      this.setOrchStatus('Click Delete again to confirm.', false);
      return;
    }
    this._pendingOrchDelete = false;
    this.setOrchStatus('Deleting…', false);
    this.vscode.postMessage({ type: 'orchestrationDelete', id });
  }

  onImportOrchestration(): void {
    this.vscode.postMessage({ type: 'orchestrationImport' });
  }

  onExportOrchestration(): void {
    const id = this.orchSelectedId();
    if (id === null) { return; }
    const name = this.currentOrchestration()?.name ?? String(id);
    this.vscode.postMessage({ type: 'orchestrationExport', id, name });
  }

  // ── Orchestration form field helpers ─────────────────────────────────────

  getOrchFieldValue(key: keyof OrchestrationProfile): string {
    switch (key) {
      case 'name':        return this.orchFormName();
      case 'description': return this.orchFormDescription();
      case 'strategy':    return this.orchFormStrategy();
      case 'roles':       return this.orchFormRoles();
      case 'workflow':    return this.orchFormWorkflow();
      case 'rules':       return this.orchFormRules();
      case 'notes':       return this.orchFormNotes();
      default:            return '';
    }
  }

  setOrchFieldValue(key: keyof OrchestrationProfile, value: string): void {
    switch (key) {
      case 'name':        this.orchFormName.set(value); break;
      case 'description': this.orchFormDescription.set(value); break;
      case 'strategy':    this.orchFormStrategy.set(value); break;
      case 'roles':       this.orchFormRoles.set(value); break;
      case 'workflow':    this.orchFormWorkflow.set(value); break;
      case 'rules':       this.orchFormRules.set(value); break;
      case 'notes':       this.orchFormNotes.set(value); break;
    }
  }

  setOrchPreviewMode(key: string, preview: boolean): void {
    this.orchPreviewModes.update(m => ({ ...m, [key]: preview }));
  }

  // ── Orchestration status helper ───────────────────────────────────────────

  setOrchStatus(msg: string, isError: boolean): void {
    this.orchStatusMessage.set(msg);
    this.orchStatusOk.set(!isError && msg !== '');
    this.orchStatusError.set(isError);
  }

  // ── Agent list actions ────────────────────────────────────────────────────

  loadAgents(): void {
    this.setStatus('Loading agents…', false);
    this.vscode.postMessage({ type: 'agentList' });
  }

  selectAgent(agent: AgentProfile): void {
    this.selectedId.set(agent.id);
    this.showNewForm.set(false);
    this._populateForm(agent);
    this.memories.set([]);
    this.memStatusMessage.set('');
    this.loadMemories();
  }

  onNewAgent(): void {
    this.selectedId.set(null);
    this.showNewForm.set(true);
    this._clearForm();
  }

  cancelNew(): void {
    this.showNewForm.set(false);
    this._clearForm();
  }

  onCreateAgent(): void {
    const name = this.formName().trim();
    if (!name) { return; }
    this.setStatus('Creating…', false);
    this.vscode.postMessage({
      type:                'agentAdd',
      name,
      description:         this.formDescription(),
      isAgentLead:         this.formIsLead(),
      status:              this.formStatus(),
      objective:           this.formObjective(),
      rules:               this.formRules(),
      guidelines:          this.formGuidelines(),
      recommendedWorkflow: this.formRecommendedWorkflow(),
      sessionBegin:        this.formSessionBegin(),
      concurrentTasks:     this.formConcurrentTasks(),
      sessionEnd:          this.formSessionEnd(),
      research:            this.formResearch(),
    });
  }

  onSaveAgent(): void {
    const id = this.selectedId();
    if (id === null) { return; }
    this.setStatus('Saving…', false);
    this.vscode.postMessage({
      type:                'agentUpdate',
      id,
      name:                this.formName(),
      description:         this.formDescription(),
      isAgentLead:         this.formIsLead(),
      status:              this.formStatus(),
      objective:           this.formObjective(),
      rules:               this.formRules(),
      guidelines:          this.formGuidelines(),
      recommendedWorkflow: this.formRecommendedWorkflow(),
      sessionBegin:        this.formSessionBegin(),
      concurrentTasks:     this.formConcurrentTasks(),
      sessionEnd:          this.formSessionEnd(),
      research:            this.formResearch(),
    });
  }

  revertForm(): void {
    const agent = this.currentAgent();
    if (agent) { this._populateForm(agent); }
  }

  onDeleteAgent(): void {
    const id = this.selectedId();
    if (id === null) { return; }
    if (!this._pendingAgentDelete) {
      this._pendingAgentDelete = true;
      this.setStatus('Click Delete again to confirm.', false);
      return;
    }
    this._pendingAgentDelete = false;
    this.setStatus('Deleting…', false);
    this.vscode.postMessage({ type: 'agentDelete', id });
  }

  onImportAgent(): void {
    this.vscode.postMessage({ type: 'agentImport' });
  }

  onExportAgent(): void {
    const id = this.selectedId();
    if (id === null) { return; }
    const name = this.currentAgent()?.name ?? String(id);
    this.vscode.postMessage({ type: 'agentExport', id, name });
  }

  // ── Agent form field helpers ──────────────────────────────────────────────

  getFieldValue(key: keyof AgentProfile): string {
    switch (key) {
      case 'name':                return this.formName();
      case 'status':              return this.formStatus();
      case 'description':         return this.formDescription();
      case 'objective':           return this.formObjective();
      case 'rules':               return this.formRules();
      case 'guidelines':          return this.formGuidelines();
      case 'recommendedWorkflow': return this.formRecommendedWorkflow();
      case 'sessionBegin':        return this.formSessionBegin();
      case 'concurrentTasks':     return this.formConcurrentTasks();
      case 'sessionEnd':          return this.formSessionEnd();
      case 'research':            return this.formResearch();
      default:                    return '';
    }
  }

  setFieldValue(key: keyof AgentProfile, value: string): void {
    switch (key) {
      case 'name':                this.formName.set(value); break;
      case 'status':              this.formStatus.set(value); break;
      case 'description':         this.formDescription.set(value); break;
      case 'objective':           this.formObjective.set(value); break;
      case 'rules':               this.formRules.set(value); break;
      case 'guidelines':          this.formGuidelines.set(value); break;
      case 'recommendedWorkflow': this.formRecommendedWorkflow.set(value); break;
      case 'sessionBegin':        this.formSessionBegin.set(value); break;
      case 'concurrentTasks':     this.formConcurrentTasks.set(value); break;
      case 'sessionEnd':          this.formSessionEnd.set(value); break;
      case 'research':            this.formResearch.set(value); break;
    }
  }

  setPreviewMode(key: string, preview: boolean): void {
    this.previewModes.update(m => ({ ...m, [key]: preview }));
  }

  /** Minimal Markdown → HTML renderer (bold, italic, headings, code, lists, paragraphs). */
  renderMarkdown(text: string): string {
    if (!text) { return '<em style="color:var(--vscode-descriptionForeground)">No content</em>'; }
    let html = text
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      // Code blocks (``` ```)
      .replace(/```[\s\S]*?```/g, m => `<pre><code>${m.slice(3, -3)}</code></pre>`)
      // Inline code
      .replace(/`([^`]+)`/g, '<code>$1</code>')
      // Headings
      .replace(/^### (.+)$/gm, '<h3>$1</h3>')
      .replace(/^## (.+)$/gm, '<h2>$1</h2>')
      .replace(/^# (.+)$/gm, '<h1>$1</h1>')
      // Bold / italic
      .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
      .replace(/\*(.+?)\*/g, '<em>$1</em>')
      // Unordered lists
      .replace(/^\s*[-*+] (.+)$/gm, '<li>$1</li>')
      // Ordered lists
      .replace(/^\s*\d+\. (.+)$/gm, '<li>$1</li>')
      // Line breaks → paragraphs (double newline)
      .replace(/\n\n/g, '</p><p>')
      // Single newlines
      .replace(/\n/g, '<br>');
    // Wrap in paragraph
    html = `<p>${html}</p>`;
    // Wrap consecutive <li> in <ul>
    html = html.replace(/(<li>.*<\/li>)/gs, '<ul>$1</ul>');
    return html;
  }

  // ── Memory actions ────────────────────────────────────────────────────────

  loadMemories(): void {
    this.memStatusMessage.set('Loading…');
    this.vscode.postMessage({ type: 'memoryList' });
  }

  onAddMemory(): void {
    this.newMemName.set('');
    this.showMemAddForm.set(true);
  }

  doAddMemory(): void {
    const name = this.newMemName().trim();
    if (!name) { return; }
    this.showMemAddForm.set(false);
    this.memStatusMessage.set('Creating…');
    this.vscode.postMessage({ type: 'memoryAdd', name });
  }

  onEditMemory(mem: McpMemoryEntry): void {
    this.vscode.postMessage({ type: 'memoryEdit', id: mem.id, name: mem.name, content: mem.content });
  }

  onExportMemory(mem: McpMemoryEntry): void {
    this.vscode.postMessage({ type: 'memoryExport', id: mem.id, name: mem.name });
  }

  onImportMemory(): void {
    this.vscode.postMessage({ type: 'memoryImport' });
  }

  doDeleteMemory(id: number): void {
    this.pendingMemDelete.set(null);
    this.memStatusMessage.set('Deleting…');
    this.vscode.postMessage({ type: 'memoryDelete', id });
  }

  formatDate(iso: string): string {
    try { return new Date(iso).toLocaleDateString(); } catch { return iso; }
  }

  // ── Status helpers ────────────────────────────────────────────────────────

  setStatus(msg: string, isError: boolean): void {
    this.statusMessage.set(msg);
    this.statusOk.set(!isError && msg !== '');
    this.statusError.set(isError);
  }

  private setMemStatus(msg: string, isError: boolean): void {
    this.memStatusMessage.set(msg);
    this.memStatusOk.set(!isError && msg !== '');
    this.memStatusError.set(isError);
  }

  // ── Message handler ───────────────────────────────────────────────────────

  private _handleMessage(data: Record<string, unknown>): void {
    switch (data['type']) {
      // ── Orchestration responses ──────────────────────────────────────────
      case 'orchestrationListResult': {
        const profiles = (data['profiles'] as OrchestrationProfile[] | undefined) ?? [];
        const error    = data['error'] as string | undefined;
        if (error) {
          this.setOrchStatus('Error: ' + error, true);
        } else {
          this.orchestrations.set(profiles);
          this.setOrchStatus(profiles.length === 0 ? '' : `${profiles.length} profile${profiles.length === 1 ? '' : 's'}`, false);
          const sel = this.orchSelectedId();
          if (sel !== null && !profiles.find(o => o.id === sel)) {
            this.orchSelectedId.set(null);
          } else if (sel !== null) {
            const updated = profiles.find(o => o.id === sel);
            if (updated) { this._populateOrchForm(updated); }
          }
        }
        break;
      }
      case 'orchestrationActionResult': {
        const profile = data['profile'] as OrchestrationProfile | undefined;
        const error   = data['error']   as string | undefined;
        const msg     = data['message'] as string | undefined;
        if (error) {
          this.setOrchStatus('Error: ' + error, true);
        } else {
          this.setOrchStatus(msg ?? 'Saved.', false);
          if (profile) {
            this.orchShowNewForm.set(false);
            this.orchSelectedId.set(profile.id);
          }
          this.loadOrchestrations();
        }
        break;
      }
      case 'orchestrationDeleteResult': {
        const error = data['error'] as string | undefined;
        if (error) {
          this.setOrchStatus('Error: ' + error, true);
        } else {
          this.setOrchStatus('Deleted.', false);
          this.orchSelectedId.set(null);
          this.orchShowNewForm.set(false);
          this._clearOrchForm();
          this.loadOrchestrations();
        }
        break;
      }
      // ── Agent list responses ─────────────────────────────────────────────
      case 'agentListResult': {
        const agents = (data['agents'] as AgentProfile[] | undefined) ?? [];
        const error  = data['error'] as string | undefined;
        if (error) {
          this.setStatus('Error: ' + error, true);
        } else {
          this.agents.set(agents);
          this.setStatus(agents.length === 0 ? '' : `${agents.length} profile${agents.length === 1 ? '' : 's'}`, false);
          const sel = this.selectedId();
          if (sel !== null && !agents.find(a => a.id === sel)) {
            this.selectedId.set(null);
          } else if (sel !== null) {
            const updated = agents.find(a => a.id === sel);
            if (updated) { this._populateForm(updated); }
          }
        }
        break;
      }
      case 'agentActionResult': {
        const agent = data['agent'] as AgentProfile | undefined;
        const error = data['error'] as string | undefined;
        const msg   = data['message'] as string | undefined;
        if (error) {
          this.setStatus('Error: ' + error, true);
        } else {
          this.setStatus(msg ?? 'Saved.', false);
          if (agent) {
            this.showNewForm.set(false);
            this.selectedId.set(agent.id);
          }
          this.loadAgents();
        }
        break;
      }
      case 'agentDeleteResult': {
        const error = data['error'] as string | undefined;
        if (error) {
          this.setStatus('Error: ' + error, true);
        } else {
          this.setStatus('Deleted.', false);
          this.selectedId.set(null);
          this.showNewForm.set(false);
          this._clearForm();
          this.loadAgents();
        }
        break;
      }
      // ── Memory responses ─────────────────────────────────────────────────
      case 'memoryListResult': {
        const entries = (data['entries'] as McpMemoryEntry[] | undefined) ?? [];
        const error   = data['error'] as string | undefined;
        if (error) {
          this.setMemStatus('Error: ' + error, true);
        } else {
          this.memories.set(entries);
          this.setMemStatus(entries.length === 0 ? '' : `${entries.length} memor${entries.length === 1 ? 'y' : 'ies'}`, false);
        }
        break;
      }
      case 'memoryActionResult': {
        const error = data['error'] as string | undefined;
        const msg   = data['message'] as string | undefined;
        if (error) {
          this.setMemStatus('Error: ' + error, true);
        } else {
          this.setMemStatus(msg ?? 'Done.', false);
          this.loadMemories();
        }
        break;
      }
    }
  }

  // ── Orchestration form helpers ────────────────────────────────────────────

  private _populateOrchForm(orch: OrchestrationProfile): void {
    this.orchFormName.set(orch.name);
    this.orchFormDescription.set(orch.description ?? '');
    this.orchFormStrategy.set(orch.strategy ?? '');
    this.orchFormRoles.set(orch.roles ?? '');
    this.orchFormWorkflow.set(orch.workflow ?? '');
    this.orchFormRules.set(orch.rules ?? '');
    this.orchFormNotes.set(orch.notes ?? '');
    this._pendingOrchDelete = false;
  }

  private _clearOrchForm(): void {
    this.orchFormName.set('');
    this.orchFormDescription.set('');
    this.orchFormStrategy.set('');
    this.orchFormRoles.set('');
    this.orchFormWorkflow.set('');
    this.orchFormRules.set('');
    this.orchFormNotes.set('');
    this._pendingOrchDelete = false;
    this.orchPreviewModes.set({});
  }

  // ── Agent form helpers ────────────────────────────────────────────────────

  private _populateForm(agent: AgentProfile): void {
    this.formName.set(agent.name);
    this.formStatus.set(agent.status ?? '');
    this.formDescription.set(agent.description ?? '');
    this.formObjective.set(agent.objective ?? '');
    this.formRules.set(agent.rules ?? '');
    this.formGuidelines.set(agent.guidelines ?? '');
    this.formRecommendedWorkflow.set(agent.recommendedWorkflow ?? '');
    this.formSessionBegin.set(agent.sessionBegin ?? '');
    this.formConcurrentTasks.set(agent.concurrentTasks ?? '');
    this.formSessionEnd.set(agent.sessionEnd ?? '');
    this.formResearch.set(agent.research ?? '');
    this.formIsLead.set(agent.isAgentLead);
    this._pendingAgentDelete = false;
  }

  private _clearForm(): void {
    this.formName.set('');
    this.formStatus.set('');
    this.formDescription.set('');
    this.formObjective.set('');
    this.formRules.set('');
    this.formGuidelines.set('');
    this.formRecommendedWorkflow.set('');
    this.formSessionBegin.set('');
    this.formConcurrentTasks.set('');
    this.formSessionEnd.set('');
    this.formResearch.set('');
    this.formIsLead.set(false);
    this._pendingAgentDelete = false;
    this.previewModes.set({});
  }
}
