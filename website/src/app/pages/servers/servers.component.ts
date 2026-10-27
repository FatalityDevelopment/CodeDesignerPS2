import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Subscription, catchError, of, switchMap, timer } from 'rxjs';

/**
 * The multiplayer server's public stats (GET /stats on its patch HTTP port; see MlsServer.PublicStats).
 * Point this at the deployed server's address.
 */
export const MULTIPLAYER_STATS_URL = 'http://localhost/stats';

const REFRESH_MS = 15_000;

interface GameStats { name: string; players: number; maxPlayers: number; locked: boolean; }
interface RoomStats { id: number; name: string; players: number; maxPlayers: number; playersInGames: number; games: GameStats[]; }
interface ServerStats {
  universe: string;
  description: string;
  updatedUtc: string;
  playersOnline: number;
  playersInRooms: number;
  playersInGames: number;
  playersInCommunity: number;
  games: number;
  rooms: RoomStats[];
}

@Component({
  selector: 'app-servers',
  imports: [],
  template: `
    <div class="container servers">
      <h1>Online Servers</h1>
      <p class="cd-subtitle page-subtitle">Live player counts · refreshes every {{ refreshSeconds }}s</p>

      <section class="cd-card universe">
        <div class="cd-card-body universe-body">
          <div class="universe-head">
            <span class="status-dot" [class.online]="online()" [class.offline]="!online()"></span>
            <div>
              <h2>{{ stats()?.universe || 'SOCOM II' }}</h2>
              <p class="cd-muted">
                @if (online()) {
                  {{ stats()!.description }}
                } @else if (loading()) {
                  Contacting the server…
                } @else {
                  The server is offline or can't be reached.
                }
              </p>
            </div>
          </div>

          <div class="totals">
            <div class="total main"><b>{{ stats()?.playersOnline ?? '–' }}</b><span>Players online</span></div>
            <div class="total"><b>{{ stats()?.playersInRooms ?? '–' }}</b><span>In briefing rooms</span></div>
            <div class="total"><b>{{ stats()?.playersInGames ?? '–' }}</b><span>In games</span></div>
            <div class="total"><b>{{ stats()?.games ?? '–' }}</b><span>Games</span></div>
          </div>
        </div>
      </section>

      @if (stats(); as s) {
        <div class="section-head">
          <span class="cd-subtitle">Briefing rooms</span>
          <span class="cd-muted small">{{ activeRooms() }} of {{ s.rooms.length }} with players · updated {{ updatedAgo() }}</span>
        </div>

        <div class="rooms">
          @for (room of s.rooms; track room.id) {
            <div class="cd-card room" [class.empty]="room.players === 0">
              <div class="room-head">
                <h3>{{ room.name }}</h3>
                <span class="count"><b>{{ room.players }}</b> / {{ room.maxPlayers }}</span>
              </div>
              <div class="bar"><div class="fill" [style.width.%]="percent(room.players, room.maxPlayers)"></div></div>
              <div class="room-meta cd-muted">
                <span>{{ room.players - room.playersInGames }} in lobby</span>
                <span>{{ room.playersInGames }} in games</span>
                <span>{{ room.games.length }} {{ room.games.length === 1 ? 'game' : 'games' }}</span>
              </div>
              @if (room.games.length) {
                <ul class="games">
                  @for (g of room.games; track $index) {
                    <li>
                      <span class="game-name">
                        @if (g.locked) {
                          <svg width="11" height="11" viewBox="0 0 16 16" fill="currentColor" aria-label="Password">
                            <path d="M4 7V5a4 4 0 1 1 8 0v2h1v8H3V7h1zm2 0h4V5a2 2 0 1 0-4 0v2z"/>
                          </svg>
                        }
                        {{ g.name }}
                      </span>
                      <span class="cd-muted">{{ g.players }} / {{ g.maxPlayers }}</span>
                    </li>
                  }
                </ul>
              }
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .servers { display: flex; flex-direction: column; gap: 12px; }
    .page-subtitle { margin: -8px 0 4px; }
    .small { font-size: 11.5px; }

    .universe-body {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      padding: 14px 16px;
    }

    .universe-head {
      display: flex;
      align-items: center;
      gap: 12px;
      min-width: 0;
    }

    .universe-head h2 { font-size: 18px; }
    .universe-head p { font-size: 12.5px; margin-top: 2px; }

    .status-dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      flex-shrink: 0;
      background: var(--fg-muted);
    }

    .status-dot.online { background: var(--success); box-shadow: 0 0 8px var(--success); }
    .status-dot.offline { background: var(--error); }

    .totals {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }

    .total {
      display: flex;
      flex-direction: column;
      align-items: center;
      min-width: 92px;
      padding: 8px 12px;
      border: 1px solid var(--border);
      border-radius: 4px;
      background: var(--bg-card);
    }

    .total b { font-size: 22px; line-height: 1.1; color: var(--fg-strong); }
    .total.main b { color: var(--success); }
    .total span { font-size: 10.5px; text-transform: uppercase; letter-spacing: 0.04em; color: var(--fg-muted); }

    .section-head {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      justify-content: space-between;
      gap: 8px;
      margin-top: 4px;
    }

    .rooms {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
      gap: 10px;
    }

    .room {
      display: flex;
      flex-direction: column;
      gap: 8px;
      padding: 12px;
    }

    .room.empty { opacity: 0.6; }

    .room-head {
      display: flex;
      align-items: baseline;
      justify-content: space-between;
      gap: 8px;
    }

    .room-head h3 {
      font-size: 13px;
      color: var(--fg-strong);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .count { font-size: 12px; color: var(--fg-muted); white-space: nowrap; }
    .count b { font-size: 15px; color: var(--fg-strong); }

    .bar {
      height: 4px;
      border-radius: 2px;
      background: rgba(255, 255, 255, 0.06);
      overflow: hidden;
    }

    .fill { height: 100%; background: var(--accent); transition: width 0.3s; }

    .room-meta {
      display: flex;
      flex-wrap: wrap;
      gap: 4px 12px;
      font-size: 11.5px;
    }

    .games {
      list-style: none;
      display: flex;
      flex-direction: column;
      gap: 3px;
      padding-top: 6px;
      border-top: 1px solid rgba(255, 255, 255, 0.05);
      font-size: 12px;
    }

    .games li { display: flex; justify-content: space-between; gap: 8px; }
    .game-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .game-name svg { vertical-align: -1px; margin-right: 3px; color: var(--fg-muted); }
  `]
})
export class ServersComponent implements OnInit, OnDestroy {
  readonly refreshSeconds = REFRESH_MS / 1000;
  readonly stats = signal<ServerStats | null>(null);
  readonly online = signal(false);
  readonly loading = signal(true);
  private readonly now = signal(Date.now());
  private sub?: Subscription;

  readonly activeRooms = computed(() => this.stats()?.rooms.filter(r => r.players > 0).length ?? 0);

  readonly updatedAgo = computed(() => {
    const s = this.stats();
    if (!s) return '';
    const seconds = Math.max(0, Math.round((this.now() - Date.parse(s.updatedUtc)) / 1000));
    return seconds < 5 ? 'just now' : `${seconds}s ago`;
  });

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    // Errors are caught per request, so one failed poll doesn't end the polling.
    this.sub = timer(0, REFRESH_MS).pipe(
      switchMap(() => this.http.get<ServerStats>(MULTIPLAYER_STATS_URL).pipe(catchError(() => of(null)))),
    ).subscribe(s => s ? this.update(s) : this.fail());
    this.sub.add(timer(1000, 1000).subscribe(() => this.now.set(Date.now())));
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  percent(players: number, max: number): number {
    return max > 0 ? Math.min(100, (players / max) * 100) : 0;
  }

  private update(s: ServerStats): void {
    // updatedUtc has no zone suffix when the server's DateTime kind is lost; treat it as UTC.
    if (!/[zZ]|[+-]\d\d:?\d\d$/.test(s.updatedUtc)) s.updatedUtc += 'Z';
    this.stats.set(s);
    this.online.set(true);
    this.loading.set(false);
    this.now.set(Date.now());
  }

  private fail(): void {
    this.online.set(false);
    this.loading.set(false);
  }
}
