import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  input,
  output,
  computed,
  signal,
  viewChild,
} from '@angular/core';
import { EmuOption, SnapshotEntry } from '../types';
import { McpMemoryOverlayComponent } from './mcp-memory-overlay.component';
import type { McpMemoryEntry, McpMemoryListResult, McpMemoryActionResult } from './mcp-memory-overlay.component';

const SNAP_PREVIEW = 5;

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
  imports: [McpMemoryOverlayComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="settings-container">

      <!-- ── Connection ──────────────────────────────────────────────────── -->
      <div class="acc-section">
        <button class="acc-header" (click)="connOpen.set(!connOpen())">
          <span class="acc-chevron">{{connOpen() ? '▾' : '▸'}}</span>
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
              <button id="connectBtn" (click)="connectClick.emit()">
                {{isConnected() ? 'Disconnect' : 'Connect'}}
              </button>
            </div>

          </div>
        }
      </div>

      <!-- ── Actions ─────────────────────────────────────────────────────── -->
      <div class="acc-section">
        <button class="acc-header" (click)="actOpen.set(!actOpen())">
          <span class="acc-chevron">{{actOpen() ? '▾' : '▸'}}</span>
          <span class="acc-title">Actions</span>
        </button>
        @if (actOpen()) {
          <div class="acc-body">
            <div class="btn-grid">

              <button id="runPcsx2Btn" (click)="runPcsx2Click.emit()"
                      [disabled]="!isPcsx2Selected()">🔌 Run PCSX2</button>

              <button id="restartBtn" (click)="restartClick.emit()" [disabled]="!isConnected()">
                ⟳ Restart
              </button>

              <button id="importBinBtn" (click)="importBinClick.emit()" [disabled]="!isConnected()">
                ⬆ Import
              </button>

              @if (!isPcsx2Selected()) {
                <button id="snapshotBtn" (click)="snapshotClick.emit()" [disabled]="!isConnected()">
                  📷 Snapshot
                </button>
              } @else {
                <button id="snapshotPcsx2Btn" (click)="snapshotPcsx2Click.emit()" [disabled]="!isConnected()">
                  📷 Snapshot
                </button>
                <button id="saveScreenshotBtn" (click)="saveScreenshotClick.emit()" [disabled]="!isConnected()">
                  🖼 Screenshot
                </button>
                <button id="previewBtn" (click)="previewClick.emit()"
                        title="Open the live PCSX2 display inside VS Code (requires the GSStreamServer-enabled PCSX2 build)">
                  🎮 Preview
                </button>
              }

              <button id="docsBtn" (click)="docsClick.emit()">📖 Docs</button>
              <button id="archiveDocsBtn" (click)="archiveDocsClick.emit()">📚 Archive</button>

            </div>
          </div>
        }
      </div>

      <!-- ── Network ──────────────────────────────────────────────────────── -->
      <div class="acc-section">
        <button class="acc-header" (click)="netOpen.set(!netOpen())">
          <span class="acc-chevron">{{netOpen() ? '▾' : '▸'}}</span>
          <span class="acc-title">Network</span>
        </button>
        @if (netOpen()) {
          <div class="acc-body">
            <div class="btn-grid">

              <button id="startNetworkBtn" (click)="startNetworkClick.emit()"
                      title="Launch the CodeDesigner Multiplayer server">
                🌐 Start Servers
              </button>

              <button (click)="openPacketViewerClick.emit()"
                      title="Open the Packet Viewer to capture and inspect PS2 network traffic">
                📡 Packets
              </button>

              <button id="netConfigBtn" (click)="openNetConfigClick.emit()"
                      title="Open the Multiplayer server config.json for editing">
                ⚙ Config
              </button>

            </div>
          </div>
        }
      </div>

      <!-- ── Snapshots ────────────────────────────────────────────────────── -->
      <div class="acc-section">
        <button class="acc-header" (click)="snapOpen.set(!snapOpen())">
          <span class="acc-chevron">{{snapOpen() ? '▾' : '▸'}}</span>
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

      <!-- ── Tools ───────────────────────────────────────────────────────── -->
      <div class="acc-section">
        <button class="acc-header" (click)="toolsOpen.set(!toolsOpen())">
          <span class="acc-chevron">{{toolsOpen() ? '▾' : '▸'}}</span>
          <span class="acc-title">Tools</span>
        </button>
        @if (toolsOpen()) {
          <div class="acc-body">
            <div class="btn-grid">
              <button (click)="openAiConfigClick.emit()"
                      title="Open the AI Configuration panel to manage agent profiles">
                🤖 AI Config
              </button>
              <button (click)="openLabelManagerClick.emit()"
                      title="Open the Label Manager to manage address labels">
                🏷 Labels
              </button>
              <button (click)="openMemoryCardClick.emit()"
                      title="Open the Memory Card Manager to browse and edit PS2 memory cards">
                💾 Mem Card
              </button>
              <button (click)="openHddClick.emit()"
                      title="Open the HDD Image Manager to browse and edit PS2 HDD images">
                💿 HDD
              </button>
              <button (click)="openSnapshotManagerClick.emit()"
                      title="Open the Snapshot Manager to browse and manage memory snapshots">
                📷 Snapshots
              </button>
              <button (click)="openDiffViewerClick.emit()"
                      title="Open the Memory Diff Viewer to compare snapshots">
                ↔ Diff
              </button>
              <button (click)="openCompatBrowserClick.emit()"
                      title="Browse the PCSX2 game compatibility list">
                🎮 Game Compat
              </button>
            </div>
          </div>
        }
      </div>

      <!-- ── Memories ─────────────────────────────────────────────────────── -->
      <!-- Keep the overlay in the DOM (display:none when collapsed) so its   -->
      <!-- loaded entries and state are preserved across open/close cycles.   -->
      <div class="acc-section">
        <button class="acc-header" (click)="memOpen.set(!memOpen())">
          <span class="acc-chevron">{{memOpen() ? '▾' : '▸'}}</span>
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
          <span class="acc-chevron">{{memViewOpen() ? '▾' : '▸'}}</span>
          <span class="acc-title">Memory</span>
        </button>
        @if (memViewOpen()) {
          <div class="acc-body">
            <div class="mem-views-grid">
              <button class="mem-view-btn" (click)="openMemoryViewClick.emit('EE RAM')"
                      title="Open EE RAM memory view (0x00000000)">
                <span class="mvb-icon">🧠</span>
                <span class="mvb-label">EE RAM</span>
                <span class="mvb-badge">32 MB</span>
              </button>
              <button class="mem-view-btn" (click)="openMemoryViewClick.emit('IOP RAM')"
                      title="Open IOP RAM memory view (0x1C000000)">
                <span class="mvb-icon">🧠</span>
                <span class="mvb-label">IOP RAM</span>
                <span class="mvb-badge">2 MB</span>
              </button>
              <button class="mem-view-btn" (click)="openMemoryViewClick.emit('BIOS ROM')"
                      title="Open BIOS ROM memory view (0x1FC00000)">
                <span class="mvb-icon">💿</span>
                <span class="mvb-label">BIOS ROM</span>
              </button>
              <button class="mem-view-btn" (click)="openMemoryViewClick.emit('Scratchpad')"
                      title="Open Scratchpad memory view (0x70000000)">
                <span class="mvb-icon">📋</span>
                <span class="mvb-label">Scratchpad</span>
              </button>
            </div>
          </div>
        }
      </div>

    </div>
  `,
})
export class SettingsTabComponent {

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

  // ── Outputs (emulator settings) ─────────────────────────────────────────────

  readonly connectClick       = output<void>();
  readonly runPcsx2Click      = output<void>();
  readonly startNetworkClick  = output<void>();
  readonly restartClick       = output<void>();
  readonly snapshotClick      = output<void>();
  readonly snapshotPcsx2Click = output<void>();
  readonly saveScreenshotClick = output<void>();
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
  readonly openMemoryCardClick   = output<void>();
  readonly openHddClick          = output<void>();
  readonly openMemoryViewClick = output<string>();
  readonly openDnsMappingClick   = output<void>();
  readonly openNetConfigClick    = output<void>();

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
  readonly toolsOpen   = signal(false);
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

  // ── PCSX2 detection ──────────────────────────────────────────────────────────

  /** True when a PCSX2 emulator is explicitly selected in the dropdown. */
  readonly isPcsx2Selected = computed(() =>
    (this.selectedEmulator() ?? '').toLowerCase().includes('pcsx2')
  );

  // ── Public relay methods (called by parent DisasmPanelComponent) ─────────────

  /** Load (or reload) the memories list. */
  load(): void { this.mcpOverlay()?.load(); }

  /** Forward a memory list result from the extension host to the overlay. */
  setListResult(r: McpMemoryListResult): void { this.mcpOverlay()?.setListResult(r); }

  /** Forward a memory action result from the extension host to the overlay. */
  setActionResult(r: McpMemoryActionResult): void { this.mcpOverlay()?.setActionResult(r); }
}
