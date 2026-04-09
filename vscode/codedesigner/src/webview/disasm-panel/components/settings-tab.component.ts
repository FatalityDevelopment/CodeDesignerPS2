import {
  Component,
  ChangeDetectionStrategy,
  ViewEncapsulation,
  input,
  output,
  computed,
} from '@angular/core';
import { EmuOption, SnapshotEntry } from '../types';

/**
 * SettingsTabComponent - Emulator settings tab content.
 *
 * Presents BIOS and game selectors together with action buttons and the
 * snapshot list.  The CodeDesigner.Ps2.Bridge executable is resolved
 * automatically from the install directory — no CodeDesigner path configuration
 * is required.
 *
 * The emulator selector lists PCSX2 entries only (auto-detected from the
 * install directory's `pcsx2` subfolder, or manually added via the browse
 * button).  Selecting a PCSX2 entry enables PCSX2 mode.
 *
 * BIOS files must be provided and configured by the user.
 *
 * All mutable state is owned by the parent; this component is purely
 * presentational and emits events for every user action.
 */
@Component({
  selector: 'app-settings-tab',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="container">

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
          PCSX2 selected. Start PCSX2 launches the external app with CLI args from selected game/BIOS.
          Connect starts CodeDesigner control server; Run sends STARTPCSX2 over socket.
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

      <div class="row">
        <button id="runBtn" (click)="runClick.emit()">▶ Run</button>
      </div>

      @if (!isPcsx2Selected()) {
        <div class="row">
          <button id="restartBtn" (click)="restartClick.emit()" [disabled]="!isConnected()">
            ⟳ Restart
          </button>
        </div>
      }

      @if (isPcsx2Selected()) {
        <div class="row">
          <button id="restartPcsx2Btn" (click)="restartClick.emit()" [disabled]="!isConnected()">
            ⟳ Restart
          </button>
        </div>
        <div class="row">
          <button id="startPcsx2Btn" (click)="startPcsx2Click.emit()">▶ Start PCSX2</button>
        </div>
      }

      <div class="row">
        <button id="importBinBtn" (click)="importBinClick.emit()" [disabled]="!isConnected()">
          ⬆ Import Binary
        </button>
      </div>

      @if (!isPcsx2Selected()) {
        <div class="row">
          <button id="snapshotBtn" (click)="snapshotClick.emit()" [disabled]="!isConnected()">
            Take Snapshot
          </button>
        </div>
      }

      @if (isPcsx2Selected()) {
        <div class="row">
          <button id="snapshotPcsx2Btn" (click)="snapshotPcsx2Click.emit()" [disabled]="!isConnected()">
            Take Snapshot (PCSX2)
          </button>
        </div>
        <div class="row">
          <button id="saveScreenshotBtn" (click)="saveScreenshotClick.emit()" [disabled]="!isConnected()">
            Save Screenshot
          </button>
        </div>
      }

      <div class="snapshot-list">
        <h4>Snapshots @if (currentGameName()) {<span class="snapshot-game-name"> — {{currentGameName()}}</span>}</h4>
        @if (!snapshots().length) {
          <div class="snapshot-empty">No snapshots yet</div>
        }
        <div class="snapshot-scroll">
          @for (snap of snapshots(); track snap.path) {
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
      </div>

      <div class="row" style="margin-top:12px; border-top:1px solid var(--vscode-panel-border,#404040); padding-top:12px;">
        <button (click)="openAiConfigClick.emit()"
                title="Open the AI Configuration panel to manage agent profiles">
          🤖 AI Configuration
        </button>
      </div>

    </div>
  `,
})
export class SettingsTabComponent {
  readonly emulators = input<EmuOption[]>([]);
  readonly biosOptions = input<EmuOption[]>([]);
  readonly gameOptions = input<EmuOption[]>([]);
  readonly selectedEmulator = input('');
  readonly selectedBios = input('');
  readonly selectedGame = input('');
  readonly isConnected = input(false);
  readonly snapshots = input<SnapshotEntry[]>([]);
  readonly currentGameName = input('');

  readonly connectClick = output<void>();
  readonly runClick = output<void>();
  readonly startPcsx2Click = output<void>();
  readonly restartClick = output<void>();
  readonly snapshotClick = output<void>();
  readonly snapshotPcsx2Click = output<void>();
  readonly saveScreenshotClick = output<void>();
  readonly importBinClick = output<void>();
  readonly emulatorChange = output<string>();
  readonly biosChange = output<string>();
  readonly gameChange = output<string>();
  readonly browseEmulatorClick = output<void>();
  readonly browseBiosClick = output<void>();
  readonly browseGameClick = output<void>();
  readonly openSnapshotClick = output<SnapshotEntry>();
  readonly reloadSnapshotClick = output<SnapshotEntry>();
  readonly openAiConfigClick = output<void>();

  /** True when a PCSX2 emulator is explicitly selected in the dropdown. */
  readonly isPcsx2Selected = computed(() =>
    (this.selectedEmulator() ?? '').toLowerCase().includes('pcsx2')
  );
}
