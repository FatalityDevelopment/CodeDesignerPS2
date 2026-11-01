import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ArchiveEntry, CheatFile, GithubService, parseArchivePath } from '../../services/github.service';
import { categoryLabel } from './archive-categories';

/** One game's cheats from the archive, loaded on demand from raw.githubusercontent.com. */
@Component({
  selector: 'app-cheat-game',
  imports: [RouterLink],
  template: `
    <div class="container">
      <a class="back" routerLink="/cheats" (click)="back($event)">‹ Back to the index</a>

      @if (entry(); as e) {
        <div class="head">
          <div>
            <h1>{{ file()?.name || e.title }}</h1>
            <p class="cd-subtitle">
              {{ label(e.category) }}
              @if (file()?.gameId || e.serial) { · <span class="mono">{{ file()?.gameId || e.serial }}</span> }
              @if (e.region) { · {{ e.region }} }
            </p>
          </div>
          <a class="cd-btn" [href]="fileUrl()" target="_blank" rel="noopener">View on GitHub</a>
        </div>

        @if (error()) {
          <div class="cd-card"><div class="cd-card-body notice">Couldn't load this file ({{ error() }}).</div></div>
        } @else if (!file()) {
          <p class="cd-muted">Loading cheats…</p>
        } @else if (!file()!.folders.length) {
          <p class="cd-muted">This file has no cheats.</p>
        } @else {
          @for (folder of file()!.folders; track $index) {
            <section class="cd-card folder">
              <div class="cd-card-header">
                {{ folder.name || 'Cheats' }}
                <span class="count">{{ folder.cheats.length }}</span>
              </div>
              <div class="cd-card-body cheats">
                @for (cheat of folder.cheats; track $index) {
                  <div class="cheat">
                    <div class="cheat-name">{{ cheat.name }}</div>
                    @if (cheat.note) {
                      <div class="note">{{ cheat.note }}</div>
                    }
                    @if (cheat.credits.length) {
                      <div class="credits cd-muted">by {{ cheat.credits.join(', ') }}</div>
                    }
                    @if (cheat.codes.length) {
                      <pre class="codes">{{ cheat.codes.join('\\n') }}</pre>
                    }
                  </div>
                }
              </div>
            </section>
          }
        }
      } @else {
        <p class="cd-muted">No game selected. <a routerLink="/cheats">Open the index</a>.</p>
      }
    </div>
  `,
  styles: [`
    .back {
      display: inline-block;
      font-size: 12px;
      margin-bottom: 12px;
    }

    .head {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 12px;
      margin-bottom: 16px;
    }

    .head .cd-subtitle { margin-top: 4px; }
    .mono { font-family: var(--font-code); text-transform: none; letter-spacing: 0; }
    .notice { padding: 12px; }

    .folder { margin-bottom: 12px; }

    .count {
      margin-left: auto;
      font-weight: 400;
      color: var(--fg-muted);
      letter-spacing: 0;
    }

    .cheats {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
      gap: 8px;
    }

    .cheat {
      border: 1px solid rgba(255, 255, 255, 0.06);
      border-radius: 4px;
      padding: 8px 10px;
      min-width: 0;
    }

    .cheat-name {
      font-weight: 600;
      color: var(--fg-strong);
      font-size: 12.5px;
    }

    .credits { font-size: 11px; }

    .note {
      margin-top: 2px;
      font-size: 11.5px;
      font-style: italic;
      color: var(--fg-muted);
      white-space: pre-line;
    }

    .codes {
      margin-top: 6px;
      font-size: 12px;
      line-height: 1.45;
      color: #9cdcfe;
      white-space: pre-wrap;
      word-break: break-all;
      user-select: all;
    }
  `]
})
export class CheatGameComponent implements OnInit {
  readonly entry = signal<ArchiveEntry | null>(null);
  readonly file = signal<CheatFile | null>(null);
  readonly error = signal('');

  constructor(private github: GithubService, private route: ActivatedRoute) {}

  ngOnInit(): void {
    this.route.queryParamMap.subscribe(params => {
      const path = params.get('path') ?? '';
      // Only paths inside the archive's Database/ folder.
      if (!/^Database\/[^/]+\/[^/]+\.txt$/i.test(path)) {
        this.entry.set(null);
        return;
      }
      this.entry.set(parseArchivePath(path));
      this.file.set(null);
      this.error.set('');
      this.github.cheatFile(path).subscribe({
        next: f => this.file.set(f),
        error: err => this.error.set(err?.status === 404 ? 'not found' : err?.message ?? 'network error'),
      });
    });
  }

  label(category: string): string {
    return categoryLabel(category);
  }

  fileUrl(): string {
    const e = this.entry();
    return e ? this.github.archiveFileUrl(e.path) : '';
  }

  back(event: Event): void {
    // Return to the index with its search and page intact when we came from it.
    if (history.length > 1) {
      event.preventDefault();
      history.back();
    }
  }
}
