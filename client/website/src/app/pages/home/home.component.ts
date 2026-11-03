import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { GITHUB_ORG } from '../../services/github.service';
import { PROJECTS } from '../projects/project-data';

interface Section { title: string; text: string; link: string; }

/** Fatality Development landing page: featured projects and the rest of the site. */
@Component({
  selector: 'app-home',
  imports: [RouterLink],
  template: `
    <div class="container home">
      <section class="hero">
        <img src="assets/fatality-logo.png" alt="" class="hero-logo" />
        <div>
          <h1>Fatality Development</h1>
          <p class="cd-subtitle">Killing the competition</p>
          <p class="hero-blurb">
            PlayStation 2 reverse engineering, modding tools and online revival. We build CodeDesigner,
            the SOCOM Map Builder and the servers that bring SOCOM II back online, and keep an open
            archive of cheat codes.
          </p>
          <div class="hero-actions">
            <a class="cd-btn cd-btn-primary" routerLink="/projects">Projects</a>
            <a class="cd-btn" [href]="orgUrl" target="_blank" rel="noopener">
              <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
                <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z"/>
              </svg>
              GitHub
            </a>
          </div>
        </div>
      </section>

      <section class="cd-card">
        <div class="cd-card-header">Featured projects</div>
        <div class="cd-card-body featured">
          @for (p of projects; track p.slug) {
            <a class="tile" [routerLink]="['/projects', p.slug]">
              <img [src]="p.logo" alt="" class="tile-logo" />
              <div class="tile-text">
                <h3>{{ p.name }}</h3>
                <p class="cd-muted">{{ p.tagline }}</p>
              </div>
            </a>
          }
        </div>
      </section>

      <section class="cd-card">
        <div class="cd-card-header">On this site</div>
        <div class="cd-card-body sections">
          @for (s of sections; track s.link) {
            <a class="section" [routerLink]="s.link">
              <h4>{{ s.title }}</h4>
              <p class="cd-muted">{{ s.text }}</p>
            </a>
          }
        </div>
      </section>
    </div>
  `,
  styles: [`
    .home {
      display: flex;
      flex-direction: column;
      gap: 14px;
    }

    .hero {
      display: flex;
      align-items: center;
      gap: 24px;
      padding: 16px 0 12px;
    }

    @media (max-width: 560px) {
      .hero { flex-direction: column; align-items: flex-start; gap: 12px; }
    }

    .hero-logo {
      width: 112px;
      height: 112px;
      flex-shrink: 0;
      object-fit: contain;
    }

    .hero h1 { font-size: 30px; line-height: 1.1; }
    .hero .cd-subtitle { margin-top: 4px; }

    .hero-blurb {
      max-width: 640px;
      margin: 12px 0 14px;
      color: var(--fg-muted);
      font-size: 13px;
    }

    .hero-actions {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }

    .featured, .sections {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
      gap: 10px;
    }

    .tile, .section {
      border: 1px solid var(--border);
      border-radius: 4px;
      background: var(--bg-card);
      color: var(--fg);
      transition: border-color 0.1s, background 0.1s;
    }

    .tile:hover, .section:hover {
      border-color: var(--border-hover);
      background: var(--bg-card-hover);
      color: var(--fg);
      text-decoration: none;
    }

    .tile {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px;
    }

    .tile-logo {
      width: 48px;
      height: 48px;
      flex-shrink: 0;
      object-fit: contain;
    }

    .tile h3 { color: var(--accent); }
    .tile p { font-size: 12px; margin-top: 2px; }

    .section { padding: 10px 12px; }
    .section h4 { margin-bottom: 3px; }
    .section p { font-size: 12px; }
  `]
})
export class HomeComponent {
  readonly orgUrl = `https://github.com/${GITHUB_ORG}`;
  readonly projects = PROJECTS;

  readonly sections: Section[] = [
    { title: 'Projects', text: 'Every public repository of the organisation on GitHub.', link: '/projects' },
    { title: 'Cheat Database', text: 'A searchable index of the CheatCodeArchive: thousands of games and their codes.', link: '/cheats' },
    { title: 'Research', text: "A collection of everyone's research memories: game internals, formats and protocols.", link: '/research' },
    { title: 'Servers', text: 'Live status and player counts of PlayStation 2 online servers.', link: '/servers' },
  ];
}
