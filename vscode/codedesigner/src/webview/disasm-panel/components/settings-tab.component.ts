import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  OnDestroy,
  input,
  output,
  computed,
  signal,
  viewChild,
} from '@angular/core';
import { EmuOption, SnapshotEntry, NavHistoryEntry, RecentLabelEntry, NetworkStatus, AgentSummary, OrchestrationSummary } from '../types';
import { IconComponent } from '../../shared/icon.component';
import { McpMemoryOverlayComponent } from './mcp-memory-overlay.component';
import type { McpMemoryEntry, McpMemoryListResult, McpMemoryActionResult } from './mcp-memory-overlay.component';

const SNAP_PREVIEW = 5;
/** Preview count for the History and Recent Labels lists before "show more". */
const LIST_PREVIEW = 8;
/** Interval for re-pinging Multiplayer servers while the Network section is open. */
const NET_POLL_MS = 4000;

/**
 * SettingsTabComponent - Emulator settings tab content.
 *
 * Organised as collapsible accordion sections:
 *  - Connection (default open): PCSX2/BIOS/Game selectors + Connect
 *  - Actions (default collapsed): Run, Restart, Import, Snapshot, Screenshot
 *  - Network (default collapsed): Server, DNS Mapping, Config
 *  - Snapshots (default open): scrollable snapshot list with show-more toggle
 *  - Tools (default collapsed): AI Config, Packet Viewer
 *  - Memories (default collapsed): MCP Memory overlay (preserved in DOM when collapsed)
 *
 * All mutable state is owned by the parent; this component is purely
 * presentational and emits events for every user action.
 */
@Component({
  selector: 'app-settings-tab',
  standalone: true,
  imports: [McpMemoryOverlayComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="settings-container">

      <!-- ── Connection ──────────────────────────────────────────────────── -->
      <div class="acc-section">
        <button class="acc-header" (click)="connOpen.set(!connOpen())">
          <app-icon class="acc-chevron" [name]="connOpen() ? 'chevrondown' : 'chevronright'" [size]="13" />
          <app-icon class="acc-ico" name="link" [size]="14" />
          <span class="acc-title">Connection</span>
          @if (isConnected()) { <span class="acc-badge-ok">●</span> }
        </button>
        @if (connOpen()) {
          <div class="acc-body">

            <div class="row">
              <select id="emulatorSelect"
                      (change)="emulatorChange.emit($any($event.target).value)">
                <option value="" [selected]="!selectedEmulator()">Select PCSX2...</option>
                @for (e of emulators(); track e.value) {
                  <option [value]="e.value" [selected]="e.value === selectedEmulator()">{{e.label}}</option>
                }
              </select>
              <button class="browse-btn" (click)="browseEmulatorClick.emit()"
                      title="Browse for PCSX2 executable">…</button>
            </div>

            @if (isPcsx2Selected()) {
              <div class="pcsx2-note">
                PCSX2 selected. Run PCSX2 launches the emulator, starts the CodeDesigner
                control server, and attaches once PCSX2's PINE and GS stream are up.
              </div>
            }

            <div class="row">
              <select id="biosSelect"
                      (change)="biosChange.emit($any($event.target).value)">
                <option value="" [selected]="!selectedBios()">Select BIOS...</option>
                @for (b of biosOptions(); track b.value) {
                  <option [value]="b.value" [selected]="b.value === selectedBios()">{{b.label}}</option>
                }
              </select>
              <button class="browse-btn" (click)="browseBiosClick.emit()"
                      title="Browse for PS2 BIOS file">…</button>
            </div>

            <div class="row">
              <select id="gameSelect"
                      (change)="gameChange.emit($any($event.target).value)">
                <option value="" [selected]="!selectedGame()">Select Game...</option>
                @for (g of gameOptions(); track g.value) {
                  <option [value]="g.value" [selected]="g.value === selectedGame()">{{g.label}}</option>
                }
              </select>
              <button class="browse-btn" (click)="browseGameClick.emit()"
                      title="Browse for ISO, BIN, or ZIP disc image">…</button>
            </div>

            <div class="row">
              <button id="runBtn" class="run-btn" (click)="runPcsx2Click.emit()"
                      [disabled]="!isPcsx2Selected()"
                      title="Launch the emulator and attach the debugger">
                <app-icon name="play" [size]="13" /><span>Run</span>
              </button>
            </div>

          </div>
        }
      </div>

      <!-- ── Actions ─────────────────────────────────────────────────────── -->
      <div class="acc-section">
        <button class="acc-header" (click)="actOpen.set(!actOpen())">
          <app-icon class="acc-chevron" [name]="actOpen() ? 'chevrondown' : 'chevronright'" [size]="13" />
          <app-icon class="acc-ico" name="zap" [size]="14" />
          <span class="acc-title">Actions</span>
        </button>
        @if (actOpen()) {
          <div class="acc-body">
            <div class="btn-grid">

              <button id="restartBtn" (click)="restartClick.emit()" [disabled]="!isConnected()">
                <app-icon name="refreshcw" [size]="14" /><span>Restart</span>
              </button>

              <button id="importBinBtn" (click)="importBinClick.emit()" [disabled]="!isConnected()">
                <app-icon name="upload" [size]="14" /><span>Import</span>
              </button>

              @if (!isPcsx2Selected()) {
                <button id="snapshotBtn" (click)="snapshotClick.emit()" [disabled]="!isConnected()">
                  <app-icon name="camera" [size]="14" /><span>Snapshot</span>
                </button>
              } @else {
                <button id="snapshotPcsx2Btn" (click)="snapshotPcsx2Click.emit()" [disabled]="!isConnected()">
                  <app-icon name="camera" [size]="14" /><span>Snapshot</span>
                </button>
                <button id="previewBtn" (click)="previewClick.emit()"
                        title="Open the live PCSX2 display inside VS Code (requires the GSStreamServer-enabled PCSX2 build)">
                  <app-icon name="monitor" [size]="14" /><span>Preview</span>
                </button>
              }

              <button id="docsBtn" (click)="docsClick.emit()">
                <app-icon name="bookopen" [size]="14" /><span>Docs</span>
              </button>
              <button id="archiveDocsBtn" (click)="archiveDocsClick.emit()">
                <app-icon name="library" [size]="14" /><span>Archive</span>
              </button>

            </div>
          </div>
        }
      </div>

      <!-- ── Network ──────────────────────────────────────────────────────── -->
      <div class="acc-section">
        <button class="acc-header" (click)="toggleNet()">
          <app-icon class="acc-chevron" [name]="netOpen() ? 'chevrondown' : 'chevronright'" [size]="13" />
          <app-icon class="acc-ico" name="network" [size]="14" />
          <span class="acc-title">Network</span>
        </button>
        @if (netOpen()) {
          <div class="acc-body">

            @if (networkStatus(); as ns) {
              <div class="net-status">
                <div class="net-row">
                  <span class="net-dot" [class.net-dot-on]="ns.capturing"></span>
                  <span class="net-row-name">Packet capture</span>
                  <span class="net-row-state">{{ ns.capturing ? 'Capturing' : 'Idle' }}</span>
                </div>
                @for (s of ns.servers; track s.port) {
                  <div class="net-row">
                    <span class="net-dot" [class.net-dot-on]="s.online"></span>
                    <span class="net-row-name">{{ s.name }}</span>
                    <span class="net-row-port">:{{ s.port }}</span>
                    <span class="net-row-state">{{ s.online ? 'Online' : 'Offline' }}</span>
                  </div>
                }
              </div>
            } @else {
              <div class="snapshot-empty">Checking servers…</div>
            }

            <div class="btn-grid">

              <button id="startNetworkBtn" (click)="startNetworkClick.emit()"
                      title="Launch the CodeDesigner Multiplayer server">
                <app-icon name="globe" [size]="14" /><span>Start Servers</span>
              </button>

              <button (click)="openPacketViewerClick.emit()"
                      title="Open the Packet Viewer to capture and inspect PS2 network traffic">
                <app-icon name="radiotower" [size]="14" /><span>Packets</span>
              </button>

              <button id="netConfigBtn" (click)="openNetConfigClick.emit()"
                      title="Open the Multiplayer server config.json for editing">
                <app-icon name="settings" [size]="14" /><span>Config</span>
              </button>

            </div>
          </div>
        }
      </div>

      <!-- ── Snapshots ────────────────────────────────────────────────────── -->
      <div class="acc-section">
        <button class="acc-header" (click)="snapOpen.set(!snapOpen())">
          <app-icon class="acc-chevron" [name]="snapOpen() ? 'chevrondown' : 'chevronright'" [size]="13" />
          <app-icon class="acc-ico" name="history" [size]="14" />
          <span class="acc-title">
            Snapshots
            @if (currentGameName()) { <span class="acc-subtitle"> — {{currentGameName()}}</span> }
            @if (snapshots().length) { <span class="acc-count">({{snapshots().length}})</span> }
          </span>
        </button>
        @if (snapOpen()) {
          <div class="acc-body">
            @if (!snapshots().length) {
              <div class="snapshot-empty">No snapshots yet</div>
            }
            <div class="snapshot-scroll">
              @for (snap of displayedSnaps(); track snap.path) {
                <div class="snapshot-item">
                  <div class="snapshot-item-info">
                    <span class="snapshot-label">{{snap.name || snap.label}}</span>
                    <span class="snapshot-date-time">{{snap.date}} {{snap.timestamp}}</span>
                  </div>
                  <button class="open-snapshot-btn" (click)="openSnapshotClick.emit(snap)">Open</button>
                  <button class="reload-snapshot-btn" (click)="reloadSnapshotClick.emit(snap)"
                          [disabled]="!isConnected()"
                          title="Restore this snapshot into the live emulator">Reload</button>
                </div>
              }
            </div>
            @if (snapshots().length > snapPreview) {
              <button class="show-more-btn" (click)="snapShowAll.set(!snapShowAll())">
                {{snapShowAll() ? '▴ Show fewer' : '▾ Show ' + (snapshots().length - snapPreview) + ' more'}}
              </button>
            }
          </div>
        }
      </div>

      <!-- ── History ──────────────────────────────────────────────────────── -->
      <div class="acc-section">
        <button class="acc-header" (click)="toggleHist()">
          <app-icon class="acc-chevron" [name]="histOpen() ? 'chevrondown' : 'chevronright'" [size]="13" />
          <app-icon class="acc-ico" name="history" [size]="14" />
          <span class="acc-title">
            History
            @if (navHistory().length) { <span class="acc-count">({{navHistory().length}})</span> }
          </span>
        </button>
        @if (histOpen()) {
          <div class="acc-body">
            <div class="list-actions">
              <button class="list-action-btn" (click)="requestHistory.emit()" title="Refresh navigation history">
                <app-icon name="refreshcw" [size]="12" /><span>Refresh</span>
              </button>
              <button class="list-action-btn" (click)="clearHistory.emit()"
                      [disabled]="!navHistory().length" title="Clear navigation history">
                <app-icon name="trash2" [size]="12" /><span>Clear</span>
              </button>
            </div>
            @if (navHistoryError()) {
              <div class="list-error">{{navHistoryError()}}</div>
            } @else if (!navHistory().length) {
              <div class="snapshot-empty">No navigation history yet</div>
            } @else {
              <div class="snapshot-scroll">
                @for (h of displayedHistory(); track $index) {
                  <button class="hist-item" (click)="gotoAddress.emit(h.address)"
                          [title]="'Go to ' + h.address">
                    <span class="hist-addr">{{h.address}}</span>
                    <span class="hist-label">{{h.label}}</span>
                    <span class="hist-src" [class.hist-src-ai]="h.source === 'mcp'">
                      {{h.source === 'mcp' ? 'AI' : 'You'}}
                    </span>
                  </button>
                }
              </div>
              @if (navHistory().length > listPreview) {
                <button class="show-more-btn" (click)="histShowAll.set(!histShowAll())">
                  {{histShowAll() ? '▴ Show fewer' : '▾ Show ' + (navHistory().length - listPreview) + ' more'}}
                </button>
              }
            }
          </div>
        }
      </div>

      <!-- ── Recent Labels ────────────────────────────────────────────────── -->
      <div class="acc-section">
        <button class="acc-header" (click)="toggleLabels()">
          <app-icon class="acc-chevron" [name]="labelsOpen() ? 'chevrondown' : 'chevronright'" [size]="13" />
          <app-icon class="acc-ico" name="tag" [size]="14" />
          <span class="acc-title">
            Recent Labels
            @if (recentLabels().length) { <span class="acc-count">({{recentLabels().length}})</span> }
          </span>
        </button>
        @if (labelsOpen()) {
          <div class="acc-body">
            <div class="list-actions">
              <button class="list-action-btn" (click)="requestLabels.emit()" title="Refresh labels">
                <app-icon name="refreshcw" [size]="12" /><span>Refresh</span>
              </button>
            </div>
            @if (recentLabelsError()) {
              <div class="list-error">{{recentLabelsError()}}</div>
            } @else if (!recentLabels().length) {
              <div class="snapshot-empty">No labels in the active set</div>
            } @else {
              <div class="snapshot-scroll">
                @for (l of displayedLabels(); track $index) {
                  <div class="label-item">
                    <span class="label-type" [attr.data-label-type]="l.type">{{l.type}}</span>
                    <div class="label-info">
                      <span class="label-name" [title]="l.name">{{l.name}}</span>
                      <span class="label-addr">{{l.address}}</span>
                    </div>
                    <button class="open-snapshot-btn" (click)="gotoAddress.emit(l.address)"
                            [title]="'Go to ' + l.address">Goto</button>
                  </div>
                }
              </div>
              @if (recentLabels().length > listPreview) {
                <button class="show-more-btn" (click)="labelShowAll.set(!labelShowAll())">
                  {{labelShowAll() ? '▴ Show fewer' : '▾ Show ' + (recentLabels().length - listPreview) + ' more'}}
                </button>
              }
            }
          </div>
        }
      </div>

      <!-- ── Tools ───────────────────────────────────────────────────────── -->
      <div class="acc-section">
        <button class="acc-header" (click)="toolsOpen.set(!toolsOpen())">
          <app-icon class="acc-chevron" [name]="toolsOpen() ? 'chevrondown' : 'chevronright'" [size]="13" />
          <app-icon class="acc-ico" name="wrench" [size]="14" />
          <span class="acc-title">Tools</span>
        </button>
        @if (toolsOpen()) {
          <div class="acc-body">
            <div class="btn-grid">
              <button (click)="openAiConfigClick.emit()"
                      title="Open the AI Configuration panel to manage agent profiles">
                <app-icon name="bot" [size]="14" /><span>AI Config</span>
              </button>
              <button (click)="openLabelManagerClick.emit()"
                      title="Open the Label Manager to manage address labels">
                <app-icon name="tag" [size]="14" /><span>Labels</span>
              </button>
              <button (click)="openMemoryCardClick.emit()"
                      title="Open the Memory Card Manager to browse and edit PS2 memory cards">
                <app-icon name="save" [size]="14" /><span>Mem Card</span>
              </button>
              <button (click)="openHddClick.emit()"
                      title="Open the HDD Image Manager to browse and edit PS2 HDD images">
                <app-icon name="harddrive" [size]="14" /><span>HDD</span>
              </button>
              <button (click)="openSnapshotManagerClick.emit()"
                      title="Open the Snapshot Manager to browse and manage memory snapshots">
                <app-icon name="camera" [size]="14" /><span>Snapshots</span>
              </button>
              <button (click)="openDiffViewerClick.emit()"
                      title="Open the Memory Diff Viewer to compare snapshots">
                <app-icon name="gitcompare" [size]="14" /><span>Diff</span>
              </button>
              <button (click)="openCompatBrowserClick.emit()"
                      title="Browse the PCSX2 game compatibility list">
                <app-icon name="gamepad2" [size]="14" /><span>Games</span>
              </button>
              <button (click)="openCheatsEditorClick.emit()"
                      title="Open the PS2 cheats editor">
                <app-icon name="sparkles" [size]="14" /><span>Cheats</span>
              </button>
            </div>
          </div>
        }
      </div>

      <!-- ── Agents ───────────────────────────────────────────────────────── -->
      <div class="acc-section">
        <button class="acc-header" (click)="toggleAgents()">
          <app-icon class="acc-chevron" [name]="agentsOpen() ? 'chevrondown' : 'chevronright'" [size]="13" />
          <app-icon class="acc-ico" name="bot" [size]="14" />
          <span class="acc-title">
            Agents
            @if (agents().length) { <span class="acc-count">({{agents().length}})</span> }
          </span>
        </button>
        @if (agentsOpen()) {
          <div class="acc-body">
            <div class="list-actions">
              <button class="list-action-btn" (click)="requestAgents.emit()" title="Refresh agent profiles">
                <app-icon name="refreshcw" [size]="12" /><span>Refresh</span>
              </button>
              <button class="list-action-btn" (click)="openAiConfigAgents.emit(undefined)"
                      title="Open the AI Config agents tab">
                <app-icon name="bot" [size]="12" /><span>AI Config</span>
              </button>
            </div>
            @if (agentsError()) {
              <div class="list-error">{{agentsError()}}</div>
            } @else if (!agents().length) {
              <div class="snapshot-empty">No agent profiles</div>
            } @else {
              <div class="snapshot-scroll">
                @for (a of agents(); track a.id) {
                  <div class="agent-item">
                    <button class="agent-star" [class.agent-star-on]="a.isLead"
                            (click)="setAgentLead.emit(a.id)"
                            [title]="a.isLead ? 'Team Lead — click to clear' : 'Set as Team Lead'">
                      <app-icon name="star" [size]="14" />
                    </button>
                    <button class="agent-info" (click)="openAiConfigAgents.emit(a.id)"
                            title="Open this agent in AI Config">
                      @if (a.imageUri) {
                        <img class="agent-avatar" [src]="a.imageUri" [alt]="a.name" />
                      } @else {
                        <span class="agent-avatar agent-avatar-ph">{{ agentInitial(a.name) }}</span>
                      }
                      <span class="agent-text">
                        <span class="agent-name">{{a.name}}</span>
                        @if (a.description) { <span class="agent-desc">{{a.description}}</span> }
                      </span>
                    </button>
                  </div>
                }
              </div>
            }
          </div>
        }
      </div>

      <!-- ── Orchestration ────────────────────────────────────────────────── -->
      <div class="acc-section">
        <button class="acc-header" (click)="toggleOrch()">
          <app-icon class="acc-chevron" [name]="orchOpen() ? 'chevrondown' : 'chevronright'" [size]="13" />
          <app-icon class="acc-ico" name="workflow" [size]="14" />
          <span class="acc-title">
            Orchestration
            @if (orchestrations().length) { <span class="acc-count">({{orchestrations().length}})</span> }
          </span>
        </button>
        @if (orchOpen()) {
          <div class="acc-body">
            <div class="orch-mode-row">
              <span class="orch-mode-label">Multi Agent mode</span>
              <button class="toggle-switch" [class.toggle-on]="multiAgentMode()"
                      (click)="setMultiAgentMode.emit(!multiAgentMode())"
                      [attr.aria-pressed]="multiAgentMode()"
                      [title]="multiAgentMode() ? 'Multi-agent mode on — click to disable' : 'Multi-agent mode off — click to enable'">
                <span class="toggle-knob"></span>
              </button>
            </div>
            <div class="list-actions">
              <button class="list-action-btn" (click)="requestOrchestrations.emit()" title="Refresh orchestration plans">
                <app-icon name="refreshcw" [size]="12" /><span>Refresh</span>
              </button>
              <button class="list-action-btn" (click)="openAiConfigOrchestration.emit(undefined)"
                      title="Open the AI Config orchestration tab">
                <app-icon name="bot" [size]="12" /><span>AI Config</span>
              </button>
            </div>
            @if (orchestrationsError()) {
              <div class="list-error">{{orchestrationsError()}}</div>
            }
            @if (orchestrations().length) {
              <div class="snapshot-scroll">
                @for (o of orchestrations(); track o.id) {
                  <div class="agent-item">
                    <button class="orch-toggle" [class.orch-toggle-on]="o.isActive"
                            (click)="setOrchestrationActive.emit(o.id)"
                            [title]="o.isActive ? 'Active plan — click to deactivate' : 'Activate this plan'">
                      <app-icon [name]="o.isActive ? 'circlecheck' : 'circle'" [size]="15" />
                    </button>
                    <button class="agent-info" (click)="openAiConfigOrchestration.emit(o.id)"
                            title="Open this plan in AI Config">
                      <span class="agent-text">
                        <span class="agent-name">{{o.name}}</span>
                        @if (o.description) { <span class="agent-desc">{{o.description}}</span> }
                      </span>
                    </button>
                  </div>
                }
              </div>
            } @else if (!orchestrationsError()) {
              <div class="snapshot-empty">No orchestration plans</div>
            }
          </div>
        }
      </div>

      <!-- ── Memories ─────────────────────────────────────────────────────── -->
      <!-- Keep the overlay in the DOM (display:none when collapsed) so its   -->
      <!-- loaded entries and state are preserved across open/close cycles.   -->
      <div class="acc-section">
        <button class="acc-header" (click)="memOpen.set(!memOpen())">
          <app-icon class="acc-chevron" [name]="memOpen() ? 'chevrondown' : 'chevronright'" [size]="13" />
          <app-icon class="acc-ico" name="database" [size]="14" />
          <span class="acc-title">Memories</span>
        </button>
        <div [style.display]="memOpen() ? '' : 'none'">
          <app-mcp-memory-overlay
            (listEntries)="listEntries.emit()"
            (addEntry)="addEntry.emit($event)"
            (editEntry)="editEntry.emit($event)"
            (exportEntry)="exportEntry.emit($event)"
            (importEntry)="importEntry.emit()"
            (deleteEntry)="deleteEntry.emit($event)"
          />
        </div>
      </div>

      <!-- ── Memory ───────────────────────────────────────────────────────── -->
      <div class="acc-section">
        <button class="acc-header" (click)="memViewOpen.set(!memViewOpen())">
          <app-icon class="acc-chevron" [name]="memViewOpen() ? 'chevrondown' : 'chevronright'" [size]="13" />
          <app-icon class="acc-ico" name="memorystick" [size]="14" />
          <span class="acc-title">Memory Region</span>
        </button>
        @if (memViewOpen()) {
          <div class="acc-body">
            <div class="mem-views-grid">
              <button class="mem-view-btn" (click)="openMemoryViewClick.emit('EE RAM')"
                      title="Open EE RAM memory view (0x00000000)">
                <app-icon class="mvb-icon" name="memorystick" [size]="14" />
                <span class="mvb-label">EE RAM</span>
                <span class="mvb-badge">32 MB</span>
              </button>
              <button class="mem-view-btn" (click)="openMemoryViewClick.emit('IOP RAM')"
                      title="Open IOP RAM memory view (0x1C000000)">
                <app-icon class="mvb-icon" name="memorystick" [size]="14" />
                <span class="mvb-label">IOP RAM</span>
                <span class="mvb-badge">2 MB</span>
              </button>
              <button class="mem-view-btn" (click)="openMemoryViewClick.emit('BIOS ROM')"
                      title="Open BIOS ROM memory view (0x1FC00000)">
                <app-icon class="mvb-icon" name="disc3" [size]="14" />
                <span class="mvb-label">BIOS ROM</span>
                <span class="mvb-badge">4 MB</span>
              </button>
              <button class="mem-view-btn" (click)="openMemoryViewClick.emit('Scratchpad')"
                      title="Open Scratchpad memory view (0x70000000)">
                <app-icon class="mvb-icon" name="harddrive" [size]="14" />
                <span class="mvb-label">Scratchpad</span>
                <span class="mvb-badge">16 KB</span>
              </button>
            </div>
          </div>
        }
      </div>

    </div>
  `,
})
export class SettingsTabComponent implements OnDestroy {

  // ── Inputs ──────────────────────────────────────────────────────────────────

  readonly emulators      = input<EmuOption[]>([]);
  readonly biosOptions    = input<EmuOption[]>([]);
  readonly gameOptions    = input<EmuOption[]>([]);
  readonly selectedEmulator = input('');
  readonly selectedBios   = input('');
  readonly selectedGame   = input('');
  readonly isConnected    = input(false);
  readonly snapshots      = input<SnapshotEntry[]>([]);
  readonly currentGameName = input('');
  readonly navHistory        = input<NavHistoryEntry[]>([]);
  readonly navHistoryError   = input('');
  readonly recentLabels      = input<RecentLabelEntry[]>([]);
  readonly recentLabelsError = input('');
  readonly networkStatus     = input<NetworkStatus | null>(null);
  readonly agents            = input<AgentSummary[]>([]);
  readonly agentsError       = input('');
  readonly orchestrations      = input<OrchestrationSummary[]>([]);
  readonly orchestrationsError = input('');

  // ── Outputs (emulator settings) ─────────────────────────────────────────────

  readonly connectClick       = output<void>();
  readonly runPcsx2Click      = output<void>();
  readonly startNetworkClick  = output<void>();
  readonly restartClick       = output<void>();
  readonly snapshotClick      = output<void>();
  readonly snapshotPcsx2Click = output<void>();
  readonly previewClick        = output<void>();
  readonly docsClick           = output<void>();
  readonly archiveDocsClick    = output<void>();
  readonly importBinClick     = output<void>();
  readonly emulatorChange     = output<string>();
  readonly biosChange         = output<string>();
  readonly gameChange         = output<string>();
  readonly browseEmulatorClick = output<void>();
  readonly browseBiosClick    = output<void>();
  readonly browseGameClick    = output<void>();
  readonly openSnapshotClick  = output<SnapshotEntry>();
  readonly reloadSnapshotClick = output<SnapshotEntry>();
  readonly openAiConfigClick  = output<void>();
  readonly openPacketViewerClick = output<void>();
  readonly openLabelManagerClick = output<void>();
  readonly openSnapshotManagerClick = output<void>();
  readonly openDiffViewerClick   = output<void>();
  readonly openCompatBrowserClick = output<void>();
  readonly openCheatsEditorClick = output<void>();
  readonly openMemoryCardClick   = output<void>();
  readonly openHddClick          = output<void>();
  readonly openMemoryViewClick = output<string>();
  readonly openDnsMappingClick   = output<void>();
  readonly openNetConfigClick    = output<void>();
  readonly requestHistory        = output<void>();
  readonly clearHistory          = output<void>();
  readonly requestLabels         = output<void>();
  readonly requestNetworkStatus  = output<void>();
  readonly gotoAddress           = output<string>();
  readonly requestAgents         = output<void>();
  readonly setAgentLead          = output<number>();
  readonly openAiConfigAgents    = output<number | undefined>();
  readonly requestOrchestrations    = output<void>();
  readonly setOrchestrationActive   = output<number>();
  readonly setMultiAgentMode        = output<boolean>();
  readonly openAiConfigOrchestration = output<number | undefined>();

  // ── Outputs (memory relay) ──────────────────────────────────────────────────

  readonly listEntries  = output<void>();
  readonly addEntry     = output<string>();
  readonly editEntry    = output<McpMemoryEntry>();
  readonly exportEntry  = output<McpMemoryEntry>();
  readonly importEntry  = output<void>();
  readonly deleteEntry  = output<number>();

  // ── Child reference (memory overlay) ────────────────────────────────────────

  private readonly mcpOverlay = viewChild<McpMemoryOverlayComponent>(McpMemoryOverlayComponent);

  // ── Accordion state ─────────────────────────────────────────────────────────

  readonly connOpen    = signal(true);
  readonly actOpen     = signal(false);
  readonly netOpen     = signal(false);
  readonly snapOpen    = signal(true);
  readonly histOpen    = signal(false);
  readonly labelsOpen  = signal(false);
  readonly agentsOpen  = signal(false);
  readonly orchOpen    = signal(false);
  readonly toolsOpen   = signal(false);

  /** Multi-agent mode is on when any orchestration plan is active. */
  readonly multiAgentMode = computed(() => this.orchestrations().some(o => o.isActive));
  readonly memOpen     = signal(false);
  readonly memViewOpen = signal(true);

  // ── Snapshot show-all ────────────────────────────────────────────────────────

  /** Constant used in template for the threshold. */
  readonly snapPreview = SNAP_PREVIEW;
  readonly snapShowAll = signal(false);
  readonly displayedSnaps = computed(() => {
    const snaps = this.snapshots();
    return this.snapShowAll() ? snaps : snaps.slice(0, SNAP_PREVIEW);
  });

  // ── History / Recent Labels show-all ─────────────────────────────────────────

  /** Preview threshold for the History and Recent Labels lists. */
  readonly listPreview  = LIST_PREVIEW;
  readonly histShowAll  = signal(false);
  readonly labelShowAll = signal(false);
  readonly displayedHistory = computed(() => {
    const h = this.navHistory();
    return this.histShowAll() ? h : h.slice(0, LIST_PREVIEW);
  });
  readonly displayedLabels = computed(() => {
    const l = this.recentLabels();
    return this.labelShowAll() ? l : l.slice(0, LIST_PREVIEW);
  });

  // ── PCSX2 detection ──────────────────────────────────────────────────────────

  /** True when a PCSX2 emulator is explicitly selected in the dropdown. */
  readonly isPcsx2Selected = computed(() =>
    (this.selectedEmulator() ?? '').toLowerCase().includes('pcsx2')
  );

  // ── History / Recent Labels / Network section toggles ───────────────────────

  /** Toggle the History section; requests the history when it opens. */
  toggleHist(): void {
    const open = !this.histOpen();
    this.histOpen.set(open);
    if (open) { this.requestHistory.emit(); }
  }

  /** Toggle the Recent Labels section; requests the labels when it opens. */
  toggleLabels(): void {
    const open = !this.labelsOpen();
    this.labelsOpen.set(open);
    if (open) { this.requestLabels.emit(); }
  }

  /** Toggle the Agents section; requests the agent list when it opens. */
  toggleAgents(): void {
    const open = !this.agentsOpen();
    this.agentsOpen.set(open);
    if (open) { this.requestAgents.emit(); }
  }

  /** Toggle the Orchestration section; requests the plan list when it opens. */
  toggleOrch(): void {
    const open = !this.orchOpen();
    this.orchOpen.set(open);
    if (open) { this.requestOrchestrations.emit(); }
  }

  /** First letter of an agent's name, for the avatar placeholder. */
  agentInitial(name: string): string {
    return (name?.trim()[0] ?? '?').toUpperCase();
  }

  /** Toggle the Network section; starts/stops Multiplayer server polling. */
  toggleNet(): void {
    const open = !this.netOpen();
    this.netOpen.set(open);
    if (open) { this.startNetPoll(); } else { this.stopNetPoll(); }
  }

  private netPollTimer?: ReturnType<typeof setInterval>;

  /** Begin polling Multiplayer server reachability every NET_POLL_MS. */
  private startNetPoll(): void {
    this.requestNetworkStatus.emit();
    this.stopNetPoll();
    this.netPollTimer = setInterval(() => this.requestNetworkStatus.emit(), NET_POLL_MS);
  }

  /** Stop the Multiplayer server poll timer. */
  private stopNetPoll(): void {
    if (this.netPollTimer !== undefined) {
      clearInterval(this.netPollTimer);
      this.netPollTimer = undefined;
    }
  }

  ngOnDestroy(): void {
    this.stopNetPoll();
  }

  // ── Public relay methods (called by parent DisasmPanelComponent) ─────────────

  /** Load (or reload) the memories list. */
  load(): void { this.mcpOverlay()?.load(); }

  /** Forward a memory list result from the extension host to the overlay. */
  setListResult(r: McpMemoryListResult): void { this.mcpOverlay()?.setListResult(r); }

  /** Forward a memory action result from the extension host to the overlay. */
  setActionResult(r: McpMemoryActionResult): void { this.mcpOverlay()?.setActionResult(r); }
}
