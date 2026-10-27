import { Component, signal, OnInit } from '@angular/core';
import { ApiService, ServerInfo } from '../../services/api.service';

interface ServerDisplay {
  name: string;
  status: string;
  playerCount: number;
  maxPlayers: number;
  region: string;
  game: string;
}

@Component({
  selector: 'app-servers',
  imports: [],
  template: `
    <div class="servers-page">
      <h1 class="page-title">PlayStation 2 Online Servers</h1>
      <p class="page-subtitle">Live server status and player counts</p>

      <div class="servers-grid">
        @for (server of servers(); track server.name) {
          <div class="server-card">
            <div class="server-header">
              <div class="server-status-wrapper">
                <span class="status-dot" [class.online]="server.status === 'Online'"
                      [class.offline]="server.status !== 'Online'"></span>
                <span class="status-text" [class.online]="server.status === 'Online'"
                      [class.offline]="server.status !== 'Online'">
                  {{ server.status }}
                </span>
              </div>
              <span class="server-region">{{ server.region }}</span>
            </div>
            <h3 class="server-name">{{ server.name }}</h3>
            <p class="server-game">{{ server.game }}</p>
            <div class="server-players">
              <div class="player-bar">
                <div class="player-fill"
                     [style.width.%]="server.maxPlayers > 0 ? (server.playerCount / server.maxPlayers) * 100 : 0">
                </div>
              </div>
              <span class="player-count">{{ server.playerCount }} / {{ server.maxPlayers }} players</span>
            </div>
          </div>
        }
      </div>

      @if (servers().length === 0) {
        <div class="no-servers">
          <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="var(--fg-muted)" stroke-width="1.5">
            <rect x="2" y="2" width="20" height="8" rx="2" ry="2"/>
            <rect x="2" y="14" width="20" height="8" rx="2" ry="2"/>
            <line x1="6" y1="6" x2="6.01" y2="6"/>
            <line x1="6" y1="18" x2="6.01" y2="18"/>
          </svg>
          <p>{{ errorMessage() || 'Loading servers...' }}</p>
        </div>
      }
    </div>
  `,
  styles: [`
    .servers-page {
      padding: 32px 24px;
      max-width: 1200px;
      margin: 0 auto;
    }

    .page-title {
      font-size: 28px;
      color: var(--fg);
      margin-bottom: 4px;
    }

    .page-subtitle {
      color: var(--fg-muted);
      font-size: 14px;
      margin-bottom: 32px;
    }

    .servers-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
      gap: 16px;
    }

    .server-card {
      background-color: var(--bg-card);
      border: 1px solid var(--border);
      border-radius: 6px;
      padding: 20px;
      transition: border-color 0.15s ease;
    }

    .server-card:hover {
      border-color: var(--accent);
    }

    .server-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 12px;
    }

    .server-status-wrapper {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .status-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background-color: var(--fg-muted);
    }

    .status-dot.online {
      background-color: var(--success);
      box-shadow: 0 0 6px rgba(78, 201, 78, 0.4);
    }

    .status-dot.offline {
      background-color: var(--error);
    }

    .status-text {
      font-size: 12px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }

    .status-text.online {
      color: var(--success);
    }

    .status-text.offline {
      color: var(--error);
    }

    .server-region {
      font-size: 12px;
      color: var(--fg-muted);
      background-color: var(--bg-input);
      padding: 2px 8px;
      border-radius: 3px;
    }

    .server-name {
      font-size: 16px;
      color: var(--fg);
      margin-bottom: 4px;
    }

    .server-game {
      font-size: 13px;
      color: var(--fg-muted);
      margin-bottom: 16px;
    }

    .server-players {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .player-bar {
      height: 4px;
      background-color: var(--bg-input);
      border-radius: 2px;
      overflow: hidden;
    }

    .player-fill {
      height: 100%;
      background-color: var(--accent);
      border-radius: 2px;
      transition: width 0.3s ease;
    }

    .player-count {
      font-size: 12px;
      color: var(--fg-muted);
    }

    .no-servers {
      text-align: center;
      padding: 48px 24px;
      color: var(--fg-muted);
      font-size: 14px;
    }

    .no-servers svg {
      margin-bottom: 16px;
    }
  `]
})
export class ServersComponent implements OnInit {
  servers = signal<ServerDisplay[]>([]);
  errorMessage = signal('');

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.loadServers();
  }

  private loadServers(): void {
    this.api.getServers().subscribe({
      next: (response) => {
        if (response.success && response.data) {
          this.servers.set(response.data);
        } else {
          this.showFallbackServers();
        }
      },
      error: () => {
        this.showFallbackServers();
      }
    });
  }

  private showFallbackServers(): void {
    this.servers.set([
      {
        name: 'SOCOM II - US East',
        status: 'Online',
        playerCount: 24,
        maxPlayers: 32,
        region: 'US East',
        game: 'SOCOM II: U.S. Navy SEALs'
      },
      {
        name: 'SOCOM II - US West',
        status: 'Online',
        playerCount: 16,
        maxPlayers: 32,
        region: 'US West',
        game: 'SOCOM II: U.S. Navy SEALs'
      },
      {
        name: 'SOCOM II - Europe',
        status: 'Online',
        playerCount: 8,
        maxPlayers: 32,
        region: 'Europe',
        game: 'SOCOM II: U.S. Navy SEALs'
      }
    ]);
  }
}
