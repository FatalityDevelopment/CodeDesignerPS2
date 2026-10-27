import { Component, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { GITHUB_ORG, GithubRepo, GithubService } from '../../services/github.service';

/** Colors GitHub uses for the languages these repos are written in. */
const LANGUAGE_COLORS: Record<string, string> = {
  'C#': '#178600',
  'C++': '#f34b7d',
  C: '#555555',
  TypeScript: '#3178c6',
  JavaScript: '#f1e05a',
  Python: '#3572A5',
  HTML: '#e34c26',
  PowerShell: '#012456',
};

@Component({
  selector: 'app-projects',
  imports: [DatePipe],
  template: `
    <div class="container">
      <h1>Projects</h1>
      <p class="cd-subtitle page-subtitle">
        Public repositories of <a [href]="orgUrl" target="_blank" rel="noopener">{{ org }}</a> on GitHub
      </p>

      @if (error()) {
        <div class="cd-card notice">
          <div class="cd-card-body">
            Couldn't load the repositories from GitHub ({{ error() }}).
            <a [href]="orgUrl" target="_blank" rel="noopener">Browse them on GitHub</a>.
          </div>
        </div>
      } @else if (!repos()) {
        <p class="cd-muted">Loading repositories…</p>
      } @else {
        <div class="projects-grid">
          @for (repo of repos(); track repo.full_name) {
            <a class="cd-card project" [href]="repo.html_url" target="_blank" rel="noopener">
              <div class="project-head">
                @if (repo.name === 'CodeDesignerPS2') {
                  <img src="assets/codedesigner-logo-64.png" alt="" class="project-logo" />
                } @else {
                  <svg class="project-logo icon" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
                    <path d="M2 2.5A2.5 2.5 0 0 1 4.5 0h8.75a.75.75 0 0 1 .75.75v12.5a.75.75 0 0 1-.75.75h-2.5a.75.75 0 0 1 0-1.5h1.75v-2h-8a1 1 0 0 0-.714 1.7.75.75 0 1 1-1.072 1.05A2.495 2.495 0 0 1 2 11.5Zm10.5-1h-8a1 1 0 0 0-1 1v6.708A2.486 2.486 0 0 1 4.5 9h8ZM5 12.25a.25.25 0 0 1 .25-.25h3.5a.25.25 0 0 1 .25.25v3.25a.25.25 0 0 1-.4.2l-1.45-1.087a.249.249 0 0 0-.3 0L5.4 15.7a.25.25 0 0 1-.4-.2Z"/>
                  </svg>
                }
                <div class="project-title">
                  <h3>{{ repo.name }}</h3>
                  <div class="badges">
                    @if (repo.fork) { <span class="badge">Fork</span> }
                    @if (repo.archived) { <span class="badge">Archived</span> }
                  </div>
                </div>
              </div>

              <p class="project-description">{{ repo.description || 'No description.' }}</p>

              @if (repo.topics?.length) {
                <div class="topics">
                  @for (topic of repo.topics; track topic) { <span class="topic">{{ topic }}</span> }
                </div>
              }

              <div class="project-meta">
                @if (repo.language) {
                  <span><i class="lang-dot" [style.background]="languageColor(repo.language)"></i>{{ repo.language }}</span>
                }
                <span title="Stars">★ {{ repo.stargazers_count }}</span>
                <span title="Forks">⑂ {{ repo.forks_count }}</span>
                <span class="updated">Updated {{ repo.pushed_at | date: 'mediumDate' }}</span>
              </div>
            </a>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .page-subtitle {
      margin: 4px 0 20px;
    }

    .page-subtitle a {
      text-transform: none;
    }

    .notice .cd-card-body {
      padding: 12px;
    }

    .projects-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(340px, 1fr));
      gap: 14px;
    }

    @media (max-width: 420px) {
      .projects-grid { grid-template-columns: 1fr; }
    }

    .project {
      display: flex;
      flex-direction: column;
      gap: 10px;
      padding: 14px;
      color: var(--fg);
      transition: border-color 0.1s, background 0.1s;
    }

    .project:hover {
      border-color: var(--border-hover);
      background: var(--bg-card-hover);
      color: var(--fg);
      text-decoration: none;
    }

    .project-head {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .project-logo {
      width: 36px;
      height: 36px;
      flex-shrink: 0;
      border-radius: 6px;
      border: 1px solid var(--border);
      background: rgba(255, 255, 255, 0.022);
      padding: 3px;
    }

    .project-logo.icon {
      padding: 9px;
      color: var(--fg-muted);
    }

    .project-title {
      display: flex;
      align-items: center;
      gap: 8px;
      min-width: 0;
    }

    .project-title h3 {
      color: var(--accent);
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .badges {
      display: flex;
      gap: 4px;
    }

    .badge {
      font-size: 10.5px;
      font-weight: 600;
      color: var(--fg-muted);
      border: 1px solid var(--border-hover);
      border-radius: 10px;
      padding: 0 7px;
    }

    .project-description {
      flex: 1;
      color: var(--fg-muted);
      font-size: 12.5px;
    }

    .topics {
      display: flex;
      flex-wrap: wrap;
      gap: 5px;
    }

    .topic {
      font-size: 11px;
      color: var(--accent);
      background: rgba(55, 148, 255, 0.10);
      border-radius: 10px;
      padding: 1px 8px;
    }

    .project-meta {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 14px;
      font-size: 11.5px;
      color: var(--fg-muted);
    }

    .lang-dot {
      display: inline-block;
      width: 9px;
      height: 9px;
      border-radius: 50%;
      margin-right: 5px;
      vertical-align: -1px;
    }

    .updated {
      margin-left: auto;
    }
  `]
})
export class ProjectsComponent implements OnInit {
  readonly org = GITHUB_ORG;
  readonly orgUrl = `https://github.com/${GITHUB_ORG}`;
  readonly repos = signal<GithubRepo[] | null>(null);
  readonly error = signal('');

  constructor(private github: GithubService) {}

  ngOnInit(): void {
    this.github.orgRepos().subscribe({
      next: repos => this.repos.set(repos),
      error: err => this.error.set(err?.status === 403 ? 'GitHub rate limit reached, try again later' : err?.message ?? 'network error'),
    });
  }

  languageColor(language: string): string {
    return LANGUAGE_COLORS[language] ?? '#8f8f8f';
  }
}
