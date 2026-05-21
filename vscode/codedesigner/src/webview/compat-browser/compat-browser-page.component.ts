import {
  AfterViewInit,
  Component,
  OnDestroy,
  ViewEncapsulation,
  computed,
  effect,
  signal,
} from '@angular/core';
import { IconComponent } from '../shared/icon.component';
import { getVsCodeApi } from '../shared/vscode-api';
import './compat-browser-page.css';

interface CompatGame {
  discId: number;
  gameId: number;
  title: string;
  serial?: string;
  crc?: string;
  region?: string;
  status?: string;
  lastTestedVersion?: string;
  lastTestedDate?: string;
  wikiLink?: string;
  forumLink?: string;
  hasCover: boolean;
}

const PAGE_SIZE = 50;

@Component({
  selector: 'app-compat-browser-page',
  standalone: true,
  imports: [IconComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="cb-shell">
      <div class="cb-toolbar">
        <div class="cb-toolbar-title">
          <app-icon name="gamepad2" [size]="16" />
          <span>PS2 Game Compatibility</span>
        </div>
        <div class="cb-toolbar-spacer"></div>

        <label class="cb-search">
          <app-icon name="search" [size]="14" />
          <input
            type="text"
            placeholder="Search title or serial"
            [value]="search()"
            (input)="onSearch($event)" />
        </label>

        <label class="cb-filter">
          <span>Status</span>
          <select class="cb-select" [value]="statusFilter()" (change)="onStatusFilter($event)">
            <option value="">All</option>
            @for (s of statuses(); track s) {
              <option [value]="s">{{ s }}</option>
            }
          </select>
        </label>

        <label class="cb-filter">
          <span>Region</span>
          <select class="cb-select" [value]="regionFilter()" (change)="onRegionFilter($event)">
            <option value="">All</option>
            @for (r of regions(); track r) {
              <option [value]="r">{{ r }}</option>
            }
          </select>
        </label>

        <button class="cb-btn cb-primary-btn" [disabled]="busy()" (click)="refresh()">
          <app-icon name="refreshcw" [size]="14" />
          <span>{{ busy() ? 'Working' : 'Refresh' }}</span>
        </button>
      </div>

      @if (statusMsg()) {
        <div class="cb-status" [class.cb-status-error]="statusIsError()">{{ statusMsg() }}</div>
      }

      <div class="cb-list">
        @if (!busy() && filtered().length === 0) {
          <div class="cb-empty">
            @if (allGames().length === 0) {
              No compatibility data yet. Click <strong>Refresh</strong> to download PCSX2's list.
            } @else {
              No games match the current filters.
            }
          </div>
        }

        @for (g of pageGames(); track g.discId) {
          <div class="cb-row">
            <div class="cb-cover">
              @if (cover(g.serial); as src) {
                <img class="cb-cover-img" [src]="src" [alt]="g.title" />
              } @else {
                <div class="cb-cover-ph">{{ initial(g.title) }}</div>
              }
            </div>

            <div class="cb-info">
              <div class="cb-title">{{ g.title }}</div>
              <div class="cb-meta">
                <span class="cb-serial">{{ g.serial || '-' }}</span>
                @if (g.region) { <span class="cb-region">{{ g.region }}</span> }
                @if (g.crc) { <span class="cb-crc">CRC {{ g.crc }}</span> }
              </div>
              <div class="cb-meta cb-meta-sub">
                @if (g.lastTestedVersion) {
                  <span>Tested v{{ g.lastTestedVersion }}</span>
                }
                @if (g.lastTestedDate) {
                  <span>{{ g.lastTestedDate }}</span>
                }
              </div>
            </div>

            <div class="cb-status-cell">
              <span class="cb-badge" [class]="'cb-badge-' + statusClass(g.status)">
                {{ g.status || 'Unknown' }}
              </span>
            </div>

            <div class="cb-actions">
              @if (g.wikiLink) {
                <a class="cb-link-btn" [href]="g.wikiLink" target="_blank" rel="noopener"
                   title="Open the PCSX2 wiki page">
                  <app-icon name="bookopen" [size]="13" />
                  <span>Wiki</span>
                </a>
              } @else {
                <span class="cb-action-spacer"></span>
              }

              @if (g.forumLink) {
                <a class="cb-link-btn" [href]="g.forumLink" target="_blank" rel="noopener"
                   title="Open the PCSX2 forum thread">
                  <app-icon name="link" [size]="13" />
                  <span>Forum</span>
                </a>
              } @else {
                <span class="cb-action-spacer"></span>
              }

              @if (canDownload(g)) {
                <button class="cb-link-btn cb-download-btn"
                        title="Download this USA game's ROM from Vimm's Lair"
                        (click)="downloadGame(g)">
                  <app-icon name="download" [size]="13" />
                  <span>Download</span>
                </button>
              } @else {
                <span class="cb-action-spacer"></span>
              }

              <button class="cb-link-btn cb-cheats-btn" title="Open cheats for this game"
                      (click)="openCheats(g)">
                <app-icon name="sparkles" [size]="13" />
                <span>Cheats</span>
              </button>
            </div>
          </div>
        }
      </div>

      @if (pageCount() > 1) {
        <div class="cb-pager">
          <button class="cb-btn" [disabled]="page() === 0" (click)="goPage(page() - 1)">
            <app-icon name="arrowleft" [size]="13" />
            <span>Prev</span>
          </button>
          <span class="cb-pager-label">
            Page {{ page() + 1 }} / {{ pageCount() }}
            - {{ filtered().length }} game{{ filtered().length === 1 ? '' : 's' }}
          </span>
          <button class="cb-btn" [disabled]="page() >= pageCount() - 1" (click)="goPage(page() + 1)">
            <span>Next</span>
            <app-icon name="arrowright" [size]="13" />
          </button>
        </div>
      }
    </div>
  `,
})
export class CompatBrowserPageComponent implements AfterViewInit, OnDestroy {
  private readonly vscode = getVsCodeApi();

  readonly allGames = signal<CompatGame[]>([]);
  readonly search = signal('');
  readonly statusFilter = signal('');
  readonly regionFilter = signal('');
  readonly page = signal(0);
  readonly busy = signal(true);
  readonly statusMsg = signal('Loading...');
  readonly statusIsError = signal(false);

  private readonly coverMap = signal<Record<string, string>>({});
  private readonly coverRequested = new Set<string>();

  readonly statuses = computed(() => {
    const set = new Set<string>();
    for (const g of this.allGames()) { if (g.status) { set.add(g.status); } }
    return [...set].sort();
  });

  readonly regions = computed(() => {
    const set = new Set<string>();
    for (const g of this.allGames()) { if (g.region) { set.add(g.region); } }
    return [...set].sort();
  });

  readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const status = this.statusFilter();
    const region = this.regionFilter();
    return this.allGames().filter(g => {
      if (status && g.status !== status) { return false; }
      if (region && g.region !== region) { return false; }
      if (q) {
        const hay = (g.title + ' ' + (g.serial ?? '')).toLowerCase();
        if (!hay.includes(q)) { return false; }
      }
      return true;
    });
  });

  readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.filtered().length / PAGE_SIZE)));

  readonly pageGames = computed(() => {
    const start = this.page() * PAGE_SIZE;
    return this.filtered().slice(start, start + PAGE_SIZE);
  });

  private readonly onMessage = (event: MessageEvent): void => {
    const data = event.data as { type: string; [key: string]: unknown };
    switch (data['type']) {
      case 'compatData':
        this.allGames.set((data['games'] as CompatGame[]) ?? []);
        this.page.set(0);
        this.busy.set(false);
        this.statusMsg.set('');
        this.statusIsError.set(false);
        break;
      case 'compatStatus':
        this.statusMsg.set((data['message'] as string) ?? '');
        this.statusIsError.set(false);
        this.busy.set((data['busy'] as boolean) ?? false);
        break;
      case 'compatError':
        this.statusMsg.set((data['message'] as string) ?? 'Error');
        this.statusIsError.set(true);
        this.busy.set(false);
        break;
      case 'compatCover': {
        const serial = data['serial'] as string;
        const uri = (data['dataUri'] as string) ?? '';
        this.coverMap.update(m => ({ ...m, [serial]: uri }));
        break;
      }
    }
  };

  constructor() {
    effect(() => {
      for (const g of this.pageGames()) {
        if (g.serial) { this.requestCover(g.serial); }
      }
    });
  }

  ngAfterViewInit(): void {
    window.addEventListener('message', this.onMessage);
    this.vscode.postMessage({ type: 'ready' });
  }

  ngOnDestroy(): void {
    window.removeEventListener('message', this.onMessage);
  }

  onSearch(e: Event): void {
    this.search.set((e.target as HTMLInputElement).value);
    this.page.set(0);
  }

  onStatusFilter(e: Event): void {
    this.statusFilter.set((e.target as HTMLSelectElement).value);
    this.page.set(0);
  }

  onRegionFilter(e: Event): void {
    this.regionFilter.set((e.target as HTMLSelectElement).value);
    this.page.set(0);
  }

  goPage(p: number): void {
    this.page.set(Math.max(0, Math.min(p, this.pageCount() - 1)));
  }

  refresh(): void {
    this.busy.set(true);
    this.statusMsg.set('Syncing with PCSX2...');
    this.statusIsError.set(false);
    this.vscode.postMessage({ type: 'refresh' });
  }

  openCheats(g: CompatGame): void {
    this.vscode.postMessage({ type: 'openCheats', serial: g.serial, title: g.title });
  }

  canDownload(g: CompatGame): boolean {
    return g.region === 'NTSC-U';
  }

  downloadGame(g: CompatGame): void {
    this.vscode.postMessage({ type: 'downloadGame', title: g.title });
  }

  cover(serial: string | undefined): string | null {
    if (!serial) { return null; }
    const uri = this.coverMap()[serial];
    return uri ? uri : null;
  }

  initial(title: string): string {
    return (title.trim()[0] ?? '?').toUpperCase();
  }

  statusClass(status: string | undefined): string {
    switch ((status ?? '').toLowerCase()) {
      case 'perfect':  return 'perfect';
      case 'playable': return 'playable';
      case 'ingame':   return 'ingame';
      case 'menus':    return 'menus';
      case 'intro':    return 'intro';
      case 'nothing':  return 'nothing';
      default:         return 'unknown';
    }
  }

  private requestCover(serial: string): void {
    if (this.coverRequested.has(serial)) { return; }
    this.coverRequested.add(serial);
    this.vscode.postMessage({ type: 'getCover', serial });
  }
}
