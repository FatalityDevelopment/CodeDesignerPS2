import { Component, OnInit, computed, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ARCHIVE_REPO, ArchiveEntry, GITHUB_ORG, GithubService } from '../../services/github.service';
import { categoryLabel } from './archive-categories';

const PAGE_SIZE = 50;

/**
 * Paged index of the CheatCodeArchive (github.com/FatalityDevelopment/CheatCodeArchive).
 * Search, category and page live in the query string so Back and shared links keep their place.
 */
@Component({
  selector: 'app-cheats',
  imports: [RouterLink],
  template: `
    <div class="container">
      <h1>Cheat Database</h1>
      <p class="cd-subtitle page-subtitle">
        Index of the <a [href]="archiveUrl" target="_blank" rel="noopener">{{ archiveRepo }}</a>
        @if (entries()) { · {{ entries()!.length.toLocaleString() }} games }
      </p>

      <div class="toolbar">
        <div class="search">
          <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
            <path d="M11.742 10.344a6.5 6.5 0 1 0-1.397 1.398h-.001c.03.04.062.078.098.115l3.85 3.85a1 1 0 0 0 1.415-1.414l-3.85-3.85a1.007 1.007 0 0 0-.115-.1zM12 6.5a5.5 5.5 0 1 1-11 0 5.5 5.5 0 0 1 11 0z"/>
          </svg>
          <input type="search" placeholder="Search by game or serial (e.g. SOCOM, SCUS-97275)"
                 [value]="query()" (input)="setQuery($any($event.target).value)" aria-label="Search games" />
        </div>
        <select [value]="category()" (change)="setCategory($any($event.target).value)" aria-label="Category">
          <option value="">All categories</option>
          @for (c of categories(); track c.name) {
            <option [value]="c.name" [selected]="c.name === category()">{{ c.label }} ({{ c.count }})</option>
          }
        </select>
      </div>

      @if (error()) {
        <div class="cd-card"><div class="cd-card-body notice">
          Couldn't load the archive from GitHub ({{ error() }}).
          <a [href]="archiveUrl" target="_blank" rel="noopener">Browse it on GitHub</a>.
        </div></div>
      } @else if (!entries()) {
        <p class="cd-muted">Loading the archive index…</p>
      } @else {
        <div class="cd-card">
          <table class="index">
            <thead>
              <tr><th>Game</th><th class="col-serial">Serial</th><th class="col-region">Region</th><th class="col-cat">Category</th></tr>
            </thead>
            <tbody>
              @for (e of pageEntries(); track e.path) {
                <tr>
                  <td><a [routerLink]="['/cheats/game']" [queryParams]="{ path: e.path }">{{ e.title }}</a></td>
                  <td class="col-serial mono">{{ e.serial }}</td>
                  <td class="col-region">{{ e.region }}</td>
                  <td class="col-cat cd-muted">{{ label(e.category) }}</td>
                </tr>
              } @empty {
                <tr><td colspan="4" class="cd-muted empty">No games match “{{ query() }}”.</td></tr>
              }
            </tbody>
          </table>
        </div>

        @if (filtered().length > 0) {
          <nav class="pager" aria-label="Pages">
            <span class="cd-muted range">
              {{ rangeStart().toLocaleString() }}–{{ rangeEnd().toLocaleString() }} of {{ filtered().length.toLocaleString() }}
            </span>
            <button class="cd-btn" [disabled]="page() <= 1" (click)="setPage(page() - 1)">‹ Prev</button>
            @for (p of pageNumbers(); track $index) {
              @if (p === 0) {
                <span class="gap">…</span>
              } @else {
                <button class="cd-btn" [class.current]="p === page()" (click)="setPage(p)">{{ p }}</button>
              }
            }
            <button class="cd-btn" [disabled]="page() >= pageCount()" (click)="setPage(page() + 1)">Next ›</button>
          </nav>
        }
      }
    </div>
  `,
  styles: [`
    .page-subtitle { margin: 4px 0 16px; }
    .page-subtitle a { text-transform: none; }

    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin-bottom: 12px;
    }

    .search {
      position: relative;
      flex: 1 1 320px;
    }

    .search svg {
      position: absolute;
      left: 10px;
      top: 50%;
      transform: translateY(-50%);
      color: var(--fg-muted);
    }

    .search input {
      width: 100%;
      padding-left: 30px;
    }

    select { flex: 0 1 280px; min-width: 0; }

    .notice { padding: 12px; }

    .index {
      width: 100%;
      border-collapse: collapse;
      font-size: 12.5px;
    }

    .index th {
      text-align: left;
      padding: 8px 12px;
      font-size: 11px;
      font-weight: 600;
      letter-spacing: 0.04em;
      text-transform: uppercase;
      color: var(--fg-muted);
      border-bottom: 1px solid var(--border);
    }

    .index td {
      padding: 6px 12px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.04);
      vertical-align: top;
    }

    .index tbody tr:hover { background: var(--bg-card-hover); }
    .index tbody tr:last-child td { border-bottom: none; }

    .mono {
      font-family: var(--font-code);
      font-size: 12px;
      color: #9cdcfe;
      white-space: nowrap;
    }

    .col-region, .col-cat { white-space: nowrap; }
    .empty { padding: 16px 12px; }

    @media (max-width: 720px) {
      .col-region, .col-cat { display: none; }
    }

    .pager {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 4px;
      margin-top: 12px;
    }

    .pager .range { margin-right: auto; font-size: 12px; }
    .pager .cd-btn { min-width: 30px; justify-content: center; padding: 0 9px; }
    .pager .cd-btn:disabled { opacity: 0.4; cursor: default; }
    .pager .cd-btn.current {
      background: var(--btn);
      border-color: var(--btn);
      color: #ffffff;
    }
    .pager .gap { color: var(--fg-muted); padding: 0 4px; }
  `]
})
export class CheatsComponent implements OnInit {
  readonly archiveRepo = ARCHIVE_REPO;
  readonly archiveUrl = `https://github.com/${GITHUB_ORG}/${ARCHIVE_REPO}`;

  readonly entries = signal<ArchiveEntry[] | null>(null);
  readonly error = signal('');
  readonly query = signal('');
  readonly category = signal('');
  readonly page = signal(1);

  readonly categories = computed(() => {
    const counts = new Map<string, number>();
    for (const e of this.entries() ?? []) counts.set(e.category, (counts.get(e.category) ?? 0) + 1);
    return [...counts.entries()]
      .map(([name, count]) => ({ name, count, label: categoryLabel(name) }))
      .sort((a, b) => a.label.localeCompare(b.label));
  });

  readonly filtered = computed(() => {
    const words = this.query().toLowerCase().split(/\s+/).filter(Boolean);
    const category = this.category();
    return (this.entries() ?? []).filter(e => {
      if (category && e.category !== category) return false;
      if (!words.length) return true;
      // Serials are written SLUS_202.65 / SLUS-20265; compare them without punctuation too.
      const hay = `${e.title} ${e.region} ${e.serial} ${e.serial.replace(/[^a-z0-9]/gi, '')}`.toLowerCase();
      return words.every(w => hay.includes(w) || hay.includes(w.replace(/[^a-z0-9]/g, '')));
    });
  });

  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.filtered().length / PAGE_SIZE)));
  readonly pageEntries = computed(() => this.filtered().slice((this.page() - 1) * PAGE_SIZE, this.page() * PAGE_SIZE));
  readonly rangeStart = computed(() => (this.page() - 1) * PAGE_SIZE + 1);
  readonly rangeEnd = computed(() => Math.min(this.page() * PAGE_SIZE, this.filtered().length));

  /** Page buttons: first, last and two either side of the current page; 0 marks a gap. */
  readonly pageNumbers = computed(() => {
    const count = this.pageCount();
    const current = this.page();
    const pages: number[] = [];
    for (let p = 1; p <= count; p++) {
      if (p === 1 || p === count || Math.abs(p - current) <= 2) pages.push(p);
      else if (pages[pages.length - 1] !== 0) pages.push(0);
    }
    return pages;
  });

  constructor(private github: GithubService, private route: ActivatedRoute, private router: Router) {}

  ngOnInit(): void {
    this.route.queryParamMap.subscribe(params => {
      this.query.set(params.get('q') ?? '');
      this.category.set(params.get('cat') ?? '');
      this.page.set(Math.max(1, Number(params.get('page')) || 1));
    });
    this.github.archiveIndex().subscribe({
      next: entries => {
        this.entries.set(entries);
        if (this.page() > this.pageCount()) this.setPage(this.pageCount());
      },
      error: err => this.error.set(err?.status === 403 ? 'GitHub rate limit reached, try again later' : err?.message ?? 'network error'),
    });
  }

  label(category: string): string {
    return categoryLabel(category);
  }

  setQuery(q: string): void {
    this.navigate({ q: q || null, page: null }, true);
  }

  setCategory(cat: string): void {
    this.navigate({ cat: cat || null, page: null });
  }

  setPage(page: number): void {
    this.navigate({ page: page > 1 ? page : null });
    window.scrollTo?.({ top: 0 });
    document.querySelector('.main-content')?.scrollTo({ top: 0 });
  }

  private navigate(params: Record<string, string | number | null>, replaceUrl = false): void {
    this.router.navigate([], { relativeTo: this.route, queryParams: params, queryParamsHandling: 'merge', replaceUrl });
  }
}
