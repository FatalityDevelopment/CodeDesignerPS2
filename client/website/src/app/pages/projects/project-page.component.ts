import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Project, projectBySlug } from './project-data';

/** A project's own page: what it is, how to start, what it does and screenshots. */
@Component({
  selector: 'app-project-page',
  imports: [RouterLink],
  template: `
    <div class="container project-page">
      @if (project(); as p) {
        <a class="back" routerLink="/projects">‹ All projects</a>

        <section class="hero">
          <div class="hero-left">
            <div class="hero-head">
              <img [src]="p.logo" alt="" class="hero-logo" />
              <div>
                <h1>{{ p.name }}</h1>
                <p class="cd-subtitle">{{ p.tagline }}</p>
              </div>
            </div>
            <p class="hero-blurb">{{ p.blurb }}</p>
            <div class="hero-actions">
              @for (l of p.links; track l.url) {
                <a class="cd-btn" [class.cd-btn-primary]="l.primary" [href]="l.url"
                   [attr.target]="l.download ? null : '_blank'" rel="noopener">
                  @if (l.download) {
                    <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
                      <path d="M8 11.5 3.5 7l1.06-1.06 2.69 2.69V1h1.5v7.63l2.69-2.69L12.5 7 8 11.5zM2 13.5h12V15H2v-1.5z"/>
                    </svg>
                  }
                  {{ l.label }}
                </a>
              }
            </div>
          </div>

          <div class="cd-card get-started">
            <div class="cd-card-header">Get started</div>
            <ol class="cd-card-body steps">
              @for (s of p.steps; track $index) { <li [innerHTML]="s"></li> }
            </ol>
            <p class="cd-card-body cd-muted small">{{ p.stepsNote }}</p>
          </div>
        </section>

        <section class="cd-card">
          <div class="cd-card-header">What it does</div>
          <div class="cd-card-body features">
            @for (f of p.features; track f.title) {
              <div class="feature">
                <h4>{{ f.title }}</h4>
                <p class="cd-muted">{{ f.text }}</p>
              </div>
            }
          </div>
        </section>

        <section class="cd-card">
          <div class="cd-card-header">Screenshots</div>
          <div class="cd-card-body gallery">
            @for (s of p.shots; track s.file) {
              <a class="shot" [href]="'assets/screenshots/' + s.file" target="_blank" rel="noopener">
                <img [src]="'assets/screenshots/' + s.file" [alt]="s.caption" loading="lazy" />
                <span>{{ s.caption }}</span>
              </a>
            }
          </div>
        </section>
      } @else {
        <p class="cd-muted">No such project. <a routerLink="/projects">See all projects</a>.</p>
      }
    </div>
  `,
  styles: [`
    .project-page {
      display: flex;
      flex-direction: column;
      gap: 14px;
    }

    .back { font-size: 12px; align-self: flex-start; }

    .hero {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 340px;
      gap: 20px;
      align-items: stretch;
      padding: 4px 0 8px;
    }

    @media (max-width: 880px) {
      .hero { grid-template-columns: 1fr; }
    }

    .hero-left {
      display: flex;
      flex-direction: column;
      gap: 14px;
      min-width: 0;
    }

    .hero-head {
      display: flex;
      align-items: center;
      gap: 14px;
    }

    .hero-logo {
      width: 72px;
      height: 72px;
      flex-shrink: 0;
      border-radius: 8px;
      background: rgba(255, 255, 255, 0.022);
      border: 1px solid var(--border);
      padding: 4px;
      object-fit: contain;
    }

    .hero-head h1 { font-size: 28px; line-height: 1.1; }
    .hero-head .cd-subtitle { margin-top: 4px; }

    .hero-blurb {
      max-width: 640px;
      color: var(--fg-muted);
      font-size: 13px;
    }

    .hero-actions {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }

    .steps {
      display: flex;
      flex-direction: column;
      gap: 8px;
      padding-left: 30px;
      font-size: 12.5px;
    }

    .steps ::ng-deep code {
      color: #9cdcfe;
      font-size: 12px;
    }

    .small { font-size: 11.5px; }

    .features {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 10px;
    }

    @media (max-width: 880px) {
      .features { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    }

    @media (max-width: 560px) {
      .features { grid-template-columns: 1fr; }
    }

    .feature {
      border: 1px solid var(--border);
      border-radius: 4px;
      padding: 10px 12px;
      background: var(--bg-card);
    }

    .feature h4 { margin-bottom: 3px; }
    .feature p { font-size: 12px; }

    .gallery {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
      gap: 12px;
    }

    @media (max-width: 420px) {
      .gallery { grid-template-columns: 1fr; }
    }

    .shot {
      display: flex;
      flex-direction: column;
      gap: 6px;
      color: var(--fg-muted);
      font-size: 12px;
      border: 1px solid var(--border);
      border-radius: 4px;
      padding: 6px;
      background: var(--bg-card);
      transition: border-color 0.1s, background 0.1s;
    }

    .shot:hover {
      border-color: var(--border-hover);
      background: var(--bg-card-hover);
      color: var(--fg);
      text-decoration: none;
    }

    .shot img {
      width: 100%;
      aspect-ratio: 16 / 10;
      object-fit: cover;
      object-position: top;
      border-radius: 3px;
      background: #111;
    }
  `]
})
export class ProjectPageComponent implements OnInit {
  readonly project = signal<Project | undefined>(undefined);

  constructor(private route: ActivatedRoute) {}

  ngOnInit(): void {
    this.route.paramMap.subscribe(params => this.project.set(projectBySlug(params.get('slug'))));
  }
}
